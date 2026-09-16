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
    private bool _firstPollCompleted;
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
                return;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Found or HttpStatusCode.SeeOther
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.MovedPermanently)
            {
                NotifySessionExpiredIfNeeded();
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Benachrichtigungs-API antwortete {Status}", (int)response.StatusCode);
                return;
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            var data = JsonSerializer.Deserialize<UngelesenApiResponse>(payload, JsonOptions);
            if (data?.Benachrichtigungen is null)
            {
                return;
            }

            _sessionExpiredHintShown = false;

            var ids = data.Benachrichtigungen.Select(static b => b.ID).ToList();
            var isFirstPoll = !_firstPollCompleted;
            _firstPollCompleted = true;

            var newItems = data.Benachrichtigungen
                .Where(item => !_shown.Contains(item.ID))
                .ToList();

            _shown.Add(ids);

            if (isFirstPoll || !_traySession.IsInTray || newItems.Count == 0)
            {
                return;
            }

            foreach (var item in newItems.Take(MaxToastsPerPoll))
            {
                _toasts.Show(item.Titel ?? "BIS", item.Nachricht ?? string.Empty, item.ZielUrl);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Benachrichtigungen konnten nicht abgerufen werden");
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

        return new Uri(baseUri, "api/benachrichtigungen/ungelesen");
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
                _logger.LogDebug(ex, "Cookies konnten nicht gelesen werden");
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
