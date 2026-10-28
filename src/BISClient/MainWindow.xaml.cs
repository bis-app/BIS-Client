using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
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
    private readonly TaskbarBadgeService _taskbarBadge;
    private readonly NotificationNavigationService _navigation;
    private ResizeMode _restoreResizeMode = ResizeMode.CanResize;
    private WindowState _restoreState = WindowState.Normal;
    private bool _isFullscreen;
    private bool _attentionMinimized;
    private bool _applyingAttention;

    public MainWindow(
        MainViewModel viewModel,
        AppSettings settings,
        IWindowStateService windowStateService,
        TraySession traySession,
        TrayIconService trayIcon,
        TaskbarBadgeService taskbarBadge,
        NotificationNavigationService navigation)
    {
        _viewModel = viewModel;
        _settings = settings;
        _windowStateService = windowStateService;
        _traySession = traySession;
        _trayIcon = trayIcon;
        _taskbarBadge = taskbarBadge;
        _navigation = navigation;

        DataContext = viewModel;
        InitializeComponent();

        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(
            new Uri("pack://application:,,,/Resources/App.ico", UriKind.Absolute));

        _windowStateService.Restore(this);
        _isFullscreen = settings.KioskMode;
        if (settings.KioskMode)
        {
            SetResizeBorder(false);
        }

        viewModel.Initialize();

        _traySession.RestoreRequested += OnRestoreRequested;
        _traySession.ExitRequested += OnExitRequested;
        _traySession.UnreadWhileHidden += OnUnreadWhileHidden;
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

        if (!_attentionMinimized)
        {
            _windowStateService.Save(this);
        }
        _viewModel.Dispose();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _traySession.RestoreRequested -= OnRestoreRequested;
        _traySession.ExitRequested -= OnExitRequested;
        _traySession.UnreadWhileHidden -= OnUnreadWhileHidden;
        ClearTaskbarAttention();
        _trayIcon.Dispose();
        _taskbarBadge.Dispose();
    }

    private void HideToTray()
    {
        var keepSavedState = _attentionMinimized;
        _attentionMinimized = false;
        if (!keepSavedState)
        {
            _windowStateService.Save(this);
        }

        ClearTaskbarAttention();
        ShowInTaskbar = false;
        Hide();
        _traySession.NotifyHidden();
    }

    private void RestoreFromTray(string? url)
    {
        _attentionMinimized = false;
        ShowActivated = true;
        ClearTaskbarAttention();
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

    private void OnUnreadWhileHidden(object? sender, int count)
    {
        if (count <= 0)
        {
            if (_attentionMinimized)
            {
                HideToTray();
            }
            else
            {
                ClearTaskbarAttention();
            }

            return;
        }

        ShowAttention(count);
    }

    private void ShowAttention(int count)
    {
        _applyingAttention = true;
        try
        {
            _attentionMinimized = true;
            ShowActivated = false;
            ShowInTaskbar = true;
            if (WindowState != WindowState.Minimized)
            {
                WindowState = WindowState.Minimized;
            }

            if (!IsVisible)
            {
                Show();
            }

            var hwnd = new WindowInteropHelper(this).EnsureHandle();
            _trayIcon.SetUnreadCount(count);
            _taskbarBadge.Show(hwnd, count);
        }
        finally
        {
            _applyingAttention = false;
        }

        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ContextIdle,
            () => KeepAttentionMinimized(count));
    }

    private void KeepAttentionMinimized(int count)
    {
        if (!_attentionMinimized || _traySession.IsExiting || WindowState == WindowState.Minimized)
        {
            return;
        }

        _applyingAttention = true;
        try
        {
            ShowActivated = false;
            ShowInTaskbar = true;
            WindowState = WindowState.Minimized;
            var hwnd = new WindowInteropHelper(this).EnsureHandle();
            _taskbarBadge.Show(hwnd, count);
        }
        finally
        {
            _applyingAttention = false;
        }
    }

    private void ClearTaskbarAttention()
    {
        ShowActivated = true;
        _trayIcon.SetUnreadCount(0);
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            _taskbarBadge.Clear(hwnd);
        }
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
            _restoreResizeMode = ResizeMode;
            _restoreState = WindowState;
            ResizeMode = ResizeMode.NoResize;
            SetResizeBorder(false);
            WindowState = WindowState.Normal;
            WindowState = WindowState.Maximized;
            _isFullscreen = true;
            return;
        }

        ResizeMode = _restoreResizeMode;
        SetResizeBorder(_restoreResizeMode != ResizeMode.NoResize);
        WindowState = _restoreState;
        _isFullscreen = false;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        if (HwndSource.FromHwnd(hwnd) is { } source)
        {
            source.AddHook(OnWindowMessage);
        }
    }

    private void OnUploadDragOver(object sender, DragEventArgs e)
    {
        var allowed = CanAcceptUploadDrop(e.Data);
        e.Effects = allowed ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        if (sender is Button button)
        {
            button.Background = allowed
                ? (System.Windows.Media.Brush)button.FindResource("ChromeHoverBrush")
                : System.Windows.Media.Brushes.Transparent;
        }
    }

    private void OnUploadDragLeave(object sender, DragEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        var position = e.GetPosition(button);
        if (position.X >= 0 && position.Y >= 0 && position.X < button.ActualWidth && position.Y < button.ActualHeight)
        {
            return;
        }

        button.ClearValue(Button.BackgroundProperty);
    }

    private async void OnUploadDrop(object sender, DragEventArgs e)
    {
        if (sender is Button button)
        {
            button.ClearValue(Button.BackgroundProperty);
        }

        e.Handled = true;
        if (!CanAcceptUploadDrop(e.Data))
        {
            return;
        }

        var paths = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        var files = new List<string>();
        var skippedFolders = false;
        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                files.Add(path);
                continue;
            }

            if (Directory.Exists(path))
            {
                skippedFolders = true;
            }
        }

        if (files.Count == 0)
        {
            if (skippedFolders)
            {
                MessageBox.Show(
                    this,
                    "Ordner können nicht hochgeladen werden. Bitte Dateien auf den Upload-Button ziehen.",
                    "BIS Client",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            return;
        }

        await _viewModel.UploadShellFilesAsync(files);
    }

    private bool CanAcceptUploadDrop(IDataObject data)
        => _viewModel.ShowUploadButton
           && !_viewModel.IsUploading
           && data.GetDataPresent(DataFormats.FileDrop);

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnStateChanged(object? sender, EventArgs e)
    {
        var maximized = WindowState == WindowState.Maximized;
        if (MaximizeButton is not null)
        {
            MaximizeButton.Content = maximized ? "\uE923" : "\uE922";
            MaximizeButton.ToolTip = maximized ? "Wiederherstellen" : "Maximieren";
        }

        if (WindowBorder is not null)
        {
            WindowBorder.BorderThickness = maximized ? new Thickness(0) : new Thickness(1);
        }

        if (_applyingAttention || !_attentionMinimized || WindowState == WindowState.Minimized)
        {
            return;
        }

        _attentionMinimized = false;
        ShowActivated = true;
        ClearTaskbarAttention();
        _traySession.NotifyRestored();
    }

    private void OnCaptionMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.Handled || IsInteractiveSource(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            e.Handled = true;
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void ToggleMaximize()
    {
        if (_settings.KioskMode || ResizeMode == ResizeMode.NoResize)
        {
            return;
        }

        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void SetResizeBorder(bool enabled)
    {
        var chrome = WindowChrome.GetWindowChrome(this);
        if (chrome is not null)
        {
            chrome.ResizeBorderThickness = enabled ? new Thickness(6) : new Thickness(0);
        }
    }

    private static bool IsInteractiveSource(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Button or ScrollBar or Thumb or RepeatButton or TextBoxBase)
            {
                return true;
            }

            if (source is Border { InputBindings.Count: > 0 })
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int wmGetMinMaxInfo = 0x0024;
        if (msg == wmGetMinMaxInfo)
        {
            ApplyWorkArea(hwnd, lParam);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private static void ApplyWorkArea(IntPtr hwnd, IntPtr lParam)
    {
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return;
        }

        var monitorInfo = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        var work = monitorInfo.rcWork;
        var display = monitorInfo.rcMonitor;
        info.ptMaxPosition.X = Math.Abs(work.Left - display.Left);
        info.ptMaxPosition.Y = Math.Abs(work.Top - display.Top);
        info.ptMaxSize.X = Math.Abs(work.Right - work.Left);
        info.ptMaxSize.Y = Math.Abs(work.Bottom - work.Top);
        Marshal.StructureToPtr(info, lParam, true);
    }

    private const uint MonitorDefaultToNearest = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct PointNative
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public PointNative ptReserved;
        public PointNative ptMaxSize;
        public PointNative ptMaxPosition;
        public PointNative ptMinTrackSize;
        public PointNative ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RectNative
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int cbSize;
        public RectNative rcMonitor;
        public RectNative rcWork;
        public int dwFlags;
    }
}
