using System.Windows;
using System.Windows.Input;
using BISClient.Configuration;
using BISClient.Services;
using BISClient.ViewModels;

namespace BISClient;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly IWindowStateService _windowStateService;
    private readonly MainViewModel _viewModel;
    private readonly TraySession _traySession;
    private readonly TrayIconService _trayIcon;
    private readonly NotificationNavigationService _navigation;
    private WindowStyle _restoreStyle = WindowStyle.SingleBorderWindow;
    private ResizeMode _restoreResizeMode = ResizeMode.CanResize;
    private WindowState _restoreState = WindowState.Normal;
    private bool _isFullscreen;

    public MainWindow(
        MainViewModel viewModel,
        AppSettings settings,
        IWindowStateService windowStateService,
        TraySession traySession,
        TrayIconService trayIcon,
        NotificationNavigationService navigation)
    {
        _viewModel = viewModel;
        _settings = settings;
        _windowStateService = windowStateService;
        _traySession = traySession;
        _trayIcon = trayIcon;
        _navigation = navigation;

        DataContext = viewModel;
        InitializeComponent();

        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(
            new Uri("pack://application:,,,/Resources/App.ico", UriKind.Absolute));

        _windowStateService.Restore(this);
        _isFullscreen = settings.KioskMode;
        viewModel.Initialize();

        _traySession.RestoreRequested += OnRestoreRequested;
        _traySession.ExitRequested += OnExitRequested;
        _trayIcon.Start();
        _traySession.NotifyWindowReady();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_traySession.IsExiting && _traySession.CanMinimizeToTray)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        _windowStateService.Save(this);
        _viewModel.Dispose();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _traySession.RestoreRequested -= OnRestoreRequested;
        _traySession.ExitRequested -= OnExitRequested;
        _trayIcon.Dispose();
    }

    private void HideToTray()
    {
        _windowStateService.Save(this);
        ShowInTaskbar = false;
        Hide();
        _traySession.NotifyHidden();
    }

    private void RestoreFromTray(string? url)
    {
        ShowInTaskbar = true;
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        _traySession.NotifyRestored();
        _navigation.Open(url);
    }

    private void OnRestoreRequested(object? sender, string? url) => RestoreFromTray(url);

    private void OnExitRequested(object? sender, EventArgs e) => Close();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11 && _settings.FullscreenToggleEnabled && !_settings.KioskMode)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
    }

    private void ToggleFullscreen()
    {
        if (!_isFullscreen)
        {
            _restoreStyle = WindowStyle;
            _restoreResizeMode = ResizeMode;
            _restoreState = WindowState;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Normal;
            WindowState = WindowState.Maximized;
            _isFullscreen = true;
            return;
        }

        WindowStyle = _restoreStyle;
        ResizeMode = _restoreResizeMode;
        WindowState = _restoreState;
        _isFullscreen = false;
    }
}
