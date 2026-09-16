using System.Windows;
using BISClient.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BISClient.Services;

public sealed class AppUpdateChecker : BackgroundService
{
    private static readonly TimeSpan RestoreCheckCooldown = TimeSpan.FromMinutes(1);

    private readonly AppSettings _settings;
    private readonly IAppUpdateService _updates;
    private readonly AppUpdateState _state;
    private readonly TraySession _tray;
    private readonly ILogger<AppUpdateChecker> _logger;
    private readonly object _checkLock = new();
    private CancellationToken _stoppingToken;
    private DateTime _lastCheckUtc = DateTime.MinValue;
    private int _checking;

    public AppUpdateChecker(
        AppSettings settings,
        IAppUpdateService updates,
        AppUpdateState state,
        TraySession tray,
        ILogger<AppUpdateChecker> logger)
    {
        _settings = settings;
        _updates = updates;
        _state = state;
        _tray = tray;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.UpdateCheckEnabled || _settings.KioskMode)
        {
            return;
        }

        _stoppingToken = stoppingToken;
        _tray.Restored += OnWindowRestoredFromTray;

        try
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(8), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var interval = TimeSpan.FromHours(_settings.UpdateCheckIntervalHours);
            using var timer = new PeriodicTimer(interval);

            await CheckOnceAsync(stoppingToken);

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await CheckOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _tray.Restored -= OnWindowRestoredFromTray;
        }
    }

    private void OnWindowRestoredFromTray(object? sender, EventArgs e)
    {
        if (_state.IsBusy)
        {
            return;
        }

        if (WasCheckedRecently())
        {
            UiThread.Invoke(_state.Remind);
            return;
        }

        _ = CheckOnceAsync(_stoppingToken, fromRestore: true);
    }

    private async Task CheckOnceAsync(CancellationToken cancellationToken, bool fromRestore = false)
    {
        if (Interlocked.CompareExchange(ref _checking, 1, 0) != 0)
        {
            if (fromRestore)
            {
                UiThread.Invoke(_state.Remind);
            }

            return;
        }

        try
        {
            if (_state.IsBusy)
            {
                return;
            }

            var info = await _updates.CheckAsync(cancellationToken);
            MarkChecked();
            if (info is null)
            {
                return;
            }

            UiThread.Invoke(() =>
            {
                _state.Offer(info, forceShow: fromRestore);
                if (!fromRestore && _tray.IsInTray)
                {
                    _tray.RequestRestore();
                }
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Update-Prüfung fehlgeschlagen");
        }
        finally
        {
            Interlocked.Exchange(ref _checking, 0);
        }
    }

    private bool WasCheckedRecently()
    {
        lock (_checkLock)
        {
            return _lastCheckUtc != DateTime.MinValue
                   && DateTime.UtcNow - _lastCheckUtc < RestoreCheckCooldown;
        }
    }

    private void MarkChecked()
    {
        lock (_checkLock)
        {
            _lastCheckUtc = DateTime.UtcNow;
        }
    }
}

internal static class UiThread
{
    public static void Invoke(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }
}
