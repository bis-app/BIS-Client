using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using BISClient.Configuration;
using BISClient.Models;
using BISClient.ViewModels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

namespace BISClient.Services;

public sealed class BisNotificationPoller : BackgroundService
{
    private const int MaxToastsPerPoll = 3;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AppSettings _settings;
    private readonly IUserSettingsStore _userSettings;
    private readonly TraySession _traySession;
    private readonly TabManager _tabs;
    private readonly IWindowsToastService _toasts;
    private readonly ITrayIconService _trayIcon;
    private readonly ILogger<BisNotificationPoller> _logger;
    private readonly HttpClient _httpClient;
    private readonly ShownNotificationStore _shown;
    private bool _sessionExpiredHintShown;

    public BisNotificationPoller(
        AppSettings settings,
        IUserSettingsStore userSettings,
        TraySession traySession,
        TabManager tabs,
        IWindowsToastService toasts,
        ITrayIconService trayIcon,
        ILogger<BisNotificationPoller> logger)
    {
        _settings = settings;
        _userSettings = userSettings;
        _traySession = traySession;
        _tabs = tabs;
        _toasts = toasts;
        _trayIcon = trayIcon;
        _logger = logger;
        _shown = new ShownNotificationStore(settings.ShownNotificationsFile, logger);
        _httpClient = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false
        })
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(ClientVersion.UserAgent);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.TrayNotificationsEnabled)
        {
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var interval = TimeSpan.FromSeconds(_settings.NotificationPollIntervalSeconds);
        using var timer = new PeriodicTimer(interval);

        await PollOnceAsync(stoppingToken);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await PollOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public override void Dispose()
    {
        _httpClient.Dispose();
        base.Dispose();
    }

    private async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        if (!_userSettings.Current.NotificationsEnabled)
        {
            return;
        }

        try
        {
            var cookieHeader = await GetCookieHeaderAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(cookieHeader))
            {
                return;
            }

            var requestUri = BuildUnreadUri();
            if (requestUri is null)
            {
                _logger.LogWarning("Benachrichtigungs-API-URL konnte nicht gebildet werden (BisUrl ungültig)");
                return;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Found or HttpStatusCode.SeeOther
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.MovedPermanently)
            {
                _logger.LogInformation(
                    "Benachrichtigungs-API antwortete {Status} für {Url} (Sitzung ungültig)",
                    (int)response.StatusCode,
                    requestUri);
                NotifySessionExpiredIfNeeded();
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Benachrichtigungs-API antwortete {Status} für {Url}",
                    (int)response.StatusCode,
                    requestUri);
                return;
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            var data = JsonSerializer.Deserialize<UngelesenApiResponse>(payload, JsonOptions);
            if (data?.Benachrichtigungen is null)
            {
                _logger.LogWarning(
                    "Benachrichtigungs-API lieferte keine verwertbare Liste für {Url}",
                    requestUri);
                return;
            }

            _sessionExpiredHintShown = false;

            var ids = data.Benachrichtigungen.Select(static b => b.ID).ToList();
            var newItems = data.Benachrichtigungen
                .Where(item => !_shown.Contains(item.ID))
                .ToList();

            if (!_traySession.IsInTray)
            {
                _shown.Add(ids);
                return;
            }

            if (newItems.Count == 0)
            {
                return;
            }

            var toShow = newItems.Take(MaxToastsPerPoll).ToList();
            foreach (var item in toShow)
            {
                _toasts.Show(item.Titel ?? "BIS", item.Nachricht ?? string.Empty, item.ZielUrl);
            }

            _shown.Add(toShow.Select(static item => item.ID));
            _logger.LogInformation(
                "Windows-Toast angezeigt: {Shown} von {New} neuen Benachrichtigungen",
                toShow.Count,
                newItems.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Benachrichtigungen konnten nicht abgerufen werden");
        }
    }

    private void NotifySessionExpiredIfNeeded()
    {
        if (_sessionExpiredHintShown || !_traySession.IsInTray)
        {
            return;
        }

        _sessionExpiredHintShown = true;
        _trayIcon.ShowBalloon(
            "BIS Client",
            "Die Anmeldung ist abgelaufen. Bitte erneut anmelden, um Benachrichtigungen zu erhalten.");
    }

    private Uri? BuildUnreadUri()
    {
        if (!Uri.TryCreate(_settings.BisUrl, UriKind.Absolute, out var baseUri))
        {
            return null;
        }

        return new Uri(baseUri, "dashboard/api/benachrichtigungen/ungelesen");
    }

    private async Task<string?> GetCookieHeaderAsync(CancellationToken cancellationToken)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return null;
        }

        var operation = dispatcher.InvokeAsync(
            () => ReadCookiesOnUiAsync(cancellationToken),
            DispatcherPriority.Background);
        return await await operation.Task;
    }

    private async Task<string?> ReadCookiesOnUiAsync(CancellationToken cancellationToken)
    {
        foreach (var tab in _tabs.Tabs.ToArray())
        {
            CoreWebView2? core;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                core = await tab.WaitForCoreAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                continue;
            }

            if (core is null)
            {
                continue;
            }

            IReadOnlyList<CoreWebView2Cookie> cookies;
            try
            {
                cookies = await core.CookieManager.GetCookiesAsync(_settings.BisUrl);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cookies konnten nicht gelesen werden");
                continue;
            }

            if (cookies.Count == 0)
            {
                continue;
            }

            return string.Join("; ", cookies.Select(static c => $"{c.Name}={c.Value}"));
        }

        return null;
    }
}
