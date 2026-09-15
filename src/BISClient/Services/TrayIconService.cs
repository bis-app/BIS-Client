using System.IO;
using System.Windows;
using BISClient.Configuration;
using Microsoft.Extensions.Logging;
using DrawingIcon = System.Drawing.Icon;
using WinForms = System.Windows.Forms;

namespace BISClient.Services;

public interface ITrayIconService
{
    void ShowBalloon(string title, string text);
}

public sealed class TrayIconService : ITrayIconService, IDisposable
{
    private readonly AppSettings _settings;
    private readonly TraySession _session;
    private readonly ILogger<TrayIconService> _logger;
    private WinForms.NotifyIcon? _notifyIcon;
    private MemoryStream? _iconStream;
    private DrawingIcon? _icon;
    private bool _disposed;

    public TrayIconService(AppSettings settings, TraySession session, ILogger<TrayIconService> logger)
    {
        _settings = settings;
        _session = session;
        _logger = logger;
    }

    public void Start()
    {
        if (_disposed || _notifyIcon is not null || !_settings.TrayNotificationsEnabled)
        {
            return;
        }

        try
        {
            var menu = new WinForms.ContextMenuStrip();
            menu.Items.Add("Öffnen", null, (_, _) => _session.RequestRestore());
            menu.Items.Add("Beenden", null, (_, _) => _session.RequestExit());

            _notifyIcon = new WinForms.NotifyIcon
            {
                Text = "BIS Client",
                Visible = true,
                ContextMenuStrip = menu,
                Icon = LoadIcon()
            };
            _notifyIcon.Click += OnClick;
            _notifyIcon.DoubleClick += (_, _) => _session.RequestRestore();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Infobereich-Symbol konnte nicht erzeugt werden");
        }
    }

    public void ShowBalloon(string title, string text)
    {
        var icon = _notifyIcon;
        if (icon is null)
        {
            return;
        }

        void Show()
        {
            icon.BalloonTipTitle = title;
            icon.BalloonTipText = text;
            icon.BalloonTipIcon = WinForms.ToolTipIcon.Info;
            icon.ShowBalloonTip(8000);
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            Show();
            return;
        }

        dispatcher.Invoke(Show);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_notifyIcon is not null)
        {
            _notifyIcon.Click -= OnClick;
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        _icon?.Dispose();
        _icon = null;
        _iconStream?.Dispose();
        _iconStream = null;
    }

    private void OnClick(object? sender, EventArgs e)
    {
        if (e is WinForms.MouseEventArgs mouse && mouse.Button != WinForms.MouseButtons.Left)
        {
            return;
        }

        _session.RequestRestore();
    }

    private DrawingIcon LoadIcon()
    {
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Resources/App.ico"));
        if (resource is null)
        {
            return DrawingIcon.ExtractAssociatedIcon(Environment.ProcessPath ?? "BISClient.exe")
                   ?? System.Drawing.SystemIcons.Application;
        }

        _iconStream = new MemoryStream();
        resource.Stream.CopyTo(_iconStream);
        _iconStream.Position = 0;
        _icon = new DrawingIcon(_iconStream);
        return _icon;
    }
}
