using System.Windows;
using BISClient.Configuration;

namespace BISClient.Services;

public sealed class TraySession
{
    private readonly AppSettings _settings;
    private bool _windowReady;
    private bool _pendingRestore;
    private string? _pendingUrl;

    public TraySession(AppSettings settings)
    {
        _settings = settings;
    }

    public bool CanMinimizeToTray => _settings.TrayNotificationsEnabled;

    public bool IsInTray { get; private set; }

    public bool IsExiting { get; private set; }

    public event EventHandler<string?>? RestoreRequested;

    public event EventHandler? ExitRequested;

    public event EventHandler? Restored;

    public event EventHandler<int>? UnreadWhileHidden;

    public void NotifyWindowReady()
    {
        _windowReady = true;
        if (_pendingRestore)
        {
            _pendingRestore = false;
            RaiseRestore(_pendingUrl);
            _pendingUrl = null;
        }
    }

    public void NotifyHidden() => IsInTray = true;

    public void NotifyRestored()
    {
        var wasInTray = IsInTray;
        IsInTray = false;
        if (wasInTray)
        {
            Restored?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ReportUnreadCount(int count)
    {
        if (!IsInTray)
        {
            return;
        }

        var unread = Math.Max(0, count);

        void Invoke()
        {
            if (!IsInTray)
            {
                return;
            }

            UnreadWhileHidden?.Invoke(this, unread);
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            Invoke();
            return;
        }

        dispatcher.Invoke(Invoke);
    }

    public void RequestRestore(string? url = null)
    {
        void Invoke()
        {
            if (!_windowReady)
            {
                _pendingRestore = true;
                if (!string.IsNullOrWhiteSpace(url))
                {
                    _pendingUrl = url;
                }

                return;
            }

            RaiseRestore(url);
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            Invoke();
            return;
        }

        dispatcher.Invoke(Invoke);
    }

    public void RequestExit()
    {
        void Invoke()
        {
            IsExiting = true;
            ExitRequested?.Invoke(this, EventArgs.Empty);
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            Invoke();
            return;
        }

        dispatcher.Invoke(Invoke);
    }

    private void RaiseRestore(string? url)
    {
        RestoreRequested?.Invoke(this, url);
    }
}
