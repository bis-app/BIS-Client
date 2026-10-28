using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BISClient.Configuration;
using BISClient.Models;
using BISClient.Services;
using BISClient.Services.Native;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace BISClient.ViewModels;

public sealed class WebViewTabSession : IDisposable
{
    private readonly AppSettings _settings;
    private readonly IWebViewEnvironmentService _environmentService;
    private readonly INavigationGuard _navigationGuard;
    private readonly IDownloadService _downloadService;
    private readonly INativeMessageBroker _nativeMessageBroker;
    private readonly IExternalBrowserLauncher _browser;
    private readonly ILogger<WebViewTabSession> _logger;
    private readonly TaskCompletionSource<CoreWebView2?> _coreReady = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private BrowserTabViewModel? _tab;
    private WebView2? _webView;
    private bool _guardCancelled;
    private bool _disposed;
    private CoreWebView2Controller? _controller;

    public WebViewTabSession(
        AppSettings settings,
        IWebViewEnvironmentService environmentService,
        INavigationGuard navigationGuard,
        IDownloadService downloadService,
        INativeMessageBroker nativeMessageBroker,
        IExternalBrowserLauncher browser,
        ILogger<WebViewTabSession> logger)
    {
        _settings = settings;
        _environmentService = environmentService;
        _navigationGuard = navigationGuard;
        _downloadService = downloadService;
        _nativeMessageBroker = nativeMessageBroker;
        _browser = browser;
        _logger = logger;
    }

    public void AttachViewModel(BrowserTabViewModel tab) => _tab = tab;

    public async Task AttachAsync(WebView2 webView, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _webView = webView;

        try
        {
            var environment = await _environmentService.GetEnvironmentAsync(cancellationToken);
            await webView.EnsureCoreWebView2Async(environment);
            var core = webView.CoreWebView2
                       ?? throw new InvalidOperationException("WebView2 Core ist nicht verfügbar.");

            ApplySettings(core);
            HookEvents(core);
            HookAccelerators(webView);
            _coreReady.TrySetResult(core);

            if (!string.IsNullOrWhiteSpace(_tab?.StartUrl))
            {
                _logger.LogInformation("Navigation startet: {Url}", LogUrl.Sanitize(_tab.StartUrl));
                core.Navigate(_tab.StartUrl);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WebView2-Tab konnte nicht initialisiert werden");
            _coreReady.TrySetResult(null);
            _tab?.ShowServerUnavailable();
        }
    }

    public void Detach()
    {
        if (_webView?.CoreWebView2 is { } core)
        {
            UnhookEvents(core);
        }

        UnhookAccelerators();
        _webView = null;
    }

    public void GoBack()
    {
        if (_webView?.CoreWebView2?.CanGoBack == true)
        {
            _webView.CoreWebView2.GoBack();
        }
    }

    public void GoForward()
    {
        if (_webView?.CoreWebView2?.CanGoForward == true)
        {
            _webView.CoreWebView2.GoForward();
        }
    }

    public void Reload()
    {
        if (_webView?.CoreWebView2 is { } core)
        {
            core.Reload();
            return;
        }

        Navigate(_tab?.StartUrl);
    }

    public void ReloadOrHome()
    {
        if (_webView?.CoreWebView2 is { } core && !string.IsNullOrWhiteSpace(core.Source) && core.Source != "about:blank")
        {
            core.Reload();
            return;
        }

        Navigate(_tab?.StartUrl);
    }

    public void Navigate(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || _webView?.CoreWebView2 is null)
        {
            return;
        }

        _webView.CoreWebView2.Navigate(url);
    }

    public Task<CoreWebView2?> WaitForCoreAsync(CancellationToken cancellationToken = default)
        => _coreReady.Task.WaitAsync(cancellationToken);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Detach();
        _coreReady.TrySetResult(null);
    }

    private void ApplySettings(CoreWebView2 core)
    {
        var settings = core.Settings;
        settings.AreDevToolsEnabled = _settings.AreDevToolsEnabled;
        settings.AreDefaultContextMenusEnabled = true;
        settings.AreHostObjectsAllowed = false;
        settings.IsStatusBarEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.IsSwipeNavigationEnabled = false;
        settings.IsZoomControlEnabled = true;
        settings.IsBuiltInErrorPageEnabled = true;

        if (!settings.UserAgent.Contains("BISClient", StringComparison.Ordinal))
        {
            settings.UserAgent += " BISClient/1.0";
        }

        core.SetVirtualHostNameToFolderMapping(
            NavigationGuard.PdfPreviewHost,
            _settings.PdfPreviewFolder,
            CoreWebView2HostResourceAccessKind.Allow);
    }

    private void HookEvents(CoreWebView2 core)
    {
        core.NavigationStarting += OnNavigationStarting;
        core.FrameNavigationStarting += OnFrameNavigationStarting;
        core.NavigationCompleted += OnNavigationCompleted;
        core.NewWindowRequested += OnNewWindowRequested;
        core.DocumentTitleChanged += OnDocumentTitleChanged;
        core.FaviconChanged += OnFaviconChanged;
        core.HistoryChanged += OnHistoryChanged;
        core.DownloadStarting += OnDownloadStarting;
        core.SaveAsUIShowing += OnSaveAsUIShowing;
        core.WebMessageReceived += OnWebMessageReceived;
        core.ProcessFailed += OnProcessFailed;
        core.ContextMenuRequested += OnContextMenuRequested;
    }

    private void UnhookEvents(CoreWebView2 core)
    {
        core.NavigationStarting -= OnNavigationStarting;
        core.FrameNavigationStarting -= OnFrameNavigationStarting;
        core.NavigationCompleted -= OnNavigationCompleted;
        core.NewWindowRequested -= OnNewWindowRequested;
        core.DocumentTitleChanged -= OnDocumentTitleChanged;
        core.FaviconChanged -= OnFaviconChanged;
        core.HistoryChanged -= OnHistoryChanged;
        core.DownloadStarting -= OnDownloadStarting;
        core.SaveAsUIShowing -= OnSaveAsUIShowing;
        core.WebMessageReceived -= OnWebMessageReceived;
        core.ProcessFailed -= OnProcessFailed;
        core.ContextMenuRequested -= OnContextMenuRequested;
    }

    private void HookAccelerators(WebView2 webView)
    {
        _controller = WebView2ControllerHelper.TryGetController(webView);
        if (_controller is not null)
        {
            _controller.AcceleratorKeyPressed += OnAcceleratorKeyPressed;
        }
    }

    private void UnhookAccelerators()
    {
        if (_controller is not null)
        {
            _controller.AcceleratorKeyPressed -= OnAcceleratorKeyPressed;
            _controller = null;
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        _guardCancelled = false;
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || !_navigationGuard.IsAllowed(uri))
        {
            e.Cancel = true;
            _guardCancelled = true;
            if (TryOpenExternal(uri, e.Uri))
            {
                return;
            }

            _logger.LogWarning("Navigation blockiert: {Url}", LogUrl.Sanitize(e.Uri));
            _tab?.ShowNavigationBlocked();
            return;
        }

        if (_tab is not null)
        {
            _tab.IsLoading = true;
            if (_tab.ErrorKind == TabErrorKind.NavigationBlocked)
            {
                _tab.ClearError();
            }
        }

        UpdatePdfPreview(e.Uri);
        _logger.LogInformation("Navigation: {Url}", LogUrl.Sanitize(e.Uri));
    }

    private void OnFrameNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || !_navigationGuard.IsAllowed(uri))
        {
            e.Cancel = true;
            _logger.LogWarning("Frame-Navigation blockiert: {Url}", LogUrl.Sanitize(e.Uri));
        }
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_tab is not null)
        {
            _tab.IsLoading = false;
        }

        UpdatePdfPreview(_webView?.CoreWebView2?.Source);
        RefreshHistory();

        if (_guardCancelled)
        {
            return;
        }

        if (e.IsSuccess)
        {
            _tab?.ClearError();
            return;
        }

        if (IsIgnorableNavigationError(e.WebErrorStatus))
        {
            return;
        }

        _logger.LogWarning(
            "Navigation fehlgeschlagen: {Status} {Url}",
            e.WebErrorStatus,
            LogUrl.Sanitize(_webView?.CoreWebView2?.Source));
        _tab?.ShowServerUnavailable();
    }

    private async void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;

        if (!string.IsNullOrWhiteSpace(e.Uri))
        {
            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || !_navigationGuard.IsAllowed(uri))
            {
                if (TryOpenExternal(uri, e.Uri))
                {
                    return;
                }

                _logger.LogWarning("Neues Fenster blockiert: {Url}", LogUrl.Sanitize(e.Uri));
                _tab?.ShowNavigationBlocked();
                return;
            }
        }

        if (!_settings.OpenNewWindowsInTabs)
        {
            _logger.LogInformation("Neues Fenster blockiert (Konfiguration)");
            return;
        }

        _logger.LogInformation("Neues Fenster als Tab: {Url}", LogUrl.Sanitize(e.Uri));

        var deferral = e.GetDeferral();
        try
        {
            var core = await RequestBlankTabCoreAsync();
            if (core is not null)
            {
                e.NewWindow = core;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Neues Fenster konnte nicht als Tab geöffnet werden");
        }
        finally
        {
            deferral.Complete();
        }
    }

    private bool TryOpenExternal(Uri? uri, string? rawUrl)
    {
        if (uri is null || !_browser.TryOpen(uri))
        {
            return false;
        }

        _logger.LogInformation("Externe Navigation im Browser: {Url}", LogUrl.Sanitize(rawUrl));
        return true;
    }

    private Task<CoreWebView2?> RequestBlankTabCoreAsync()
    {
        var tcs = new TaskCompletionSource<CoreWebView2?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _tab?.RequestBlankNewWindow(tcs);
        return tcs.Task;
    }

    private void OnDocumentTitleChanged(object? sender, object e)
    {
        if (_webView?.CoreWebView2 is null || _tab is null)
        {
            return;
        }

        var title = _webView.CoreWebView2.DocumentTitle;
        _tab.Title = string.IsNullOrWhiteSpace(title) ? "BIS" : title;
    }

    private async void OnFaviconChanged(object? sender, object e)
    {
        if (_webView?.CoreWebView2 is null || _tab is null)
        {
            return;
        }

        try
        {
            using var stream = await _webView.CoreWebView2.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png);
            if (stream is null || stream.Length == 0)
            {
                await RunOnUiAsync(() => _tab.Favicon = null);
                return;
            }

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            await RunOnUiAsync(() => _tab.Favicon = image);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Favicon konnte nicht geladen werden");
        }
    }

    private void OnHistoryChanged(object? sender, object e) => RefreshHistory();

    private void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
        => _downloadService.HandleDownloadStarting(e, OpenPdfPreview, _webView?.CoreWebView2?.Source);

    private void OnSaveAsUIShowing(object? sender, CoreWebView2SaveAsUIShowingEventArgs e)
        => _downloadService.HandleSaveAsUIShowing(e, _webView?.CoreWebView2?.Source);

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            await _nativeMessageBroker.HandleRawMessageAsync(
                e.TryGetWebMessageAsString(),
                new NativeMessageContext
                {
                    ActivePdfPath = _tab?.IsPdfPreview == true ? _tab.PdfPreviewPath : null,
                    WebView = _webView?.CoreWebView2
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Native-Nachricht konnte nicht verarbeitet werden");
        }
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        _logger.LogError(
            "Unerwarteter WebView2-Fehler: {Kind} Exit={ExitCode}",
            e.ProcessFailedKind,
            e.ExitCode);
        _tab?.ShowServerUnavailable();
    }

    private void OnContextMenuRequested(object? sender, CoreWebView2ContextMenuRequestedEventArgs e)
    {
        if (_settings.AreDevToolsEnabled)
        {
            return;
        }

        var isPdfPage = IsPdfSource(_webView?.CoreWebView2?.Source);
        for (var i = e.MenuItems.Count - 1; i >= 0; i--)
        {
            var name = e.MenuItems[i].Name;
            if (name is "inspectElement" or "share")
            {
                e.MenuItems.RemoveAt(i);
                continue;
            }

            if (!isPdfPage && name is "saveAs" or "saveLinkAs")
            {
                e.MenuItems.RemoveAt(i);
            }
        }
    }

    private void OnAcceleratorKeyPressed(object? sender, CoreWebView2AcceleratorKeyPressedEventArgs e)
    {
        if (e.KeyEventKind != CoreWebView2KeyEventKind.KeyDown
            && e.KeyEventKind != CoreWebView2KeyEventKind.SystemKeyDown)
        {
            return;
        }

        var modifiers = Keyboard.Modifiers;
        var ctrl = modifiers.HasFlag(ModifierKeys.Control);
        var shift = modifiers.HasFlag(ModifierKeys.Shift);
        var accelerator = MapAccelerator(e.VirtualKey, ctrl, shift);
        if (accelerator is null)
        {
            return;
        }

        e.Handled = true;
        _tab?.HandleAccelerator(accelerator.Value);
    }

    private static WebViewAccelerator? MapAccelerator(uint virtualKey, bool ctrl, bool shift)
    {
        const uint keyT = 0x54;
        const uint keyW = 0x57;
        const uint keyR = 0x52;
        const uint keyTab = 0x09;
        const uint keyF5 = 0x74;

        if (virtualKey == keyF5)
        {
            return WebViewAccelerator.Reload;
        }

        if (!ctrl)
        {
            return null;
        }

        return virtualKey switch
        {
            keyT => WebViewAccelerator.NewTab,
            keyW => WebViewAccelerator.CloseTab,
            keyR => WebViewAccelerator.Reload,
            keyTab when shift => WebViewAccelerator.PreviousTab,
            keyTab => WebViewAccelerator.NextTab,
            _ => null
        };
    }

    private void UpdatePdfPreview(string? source)
    {
        if (_tab is null)
        {
            return;
        }

        if (_downloadService.TryGetPreviewFile(source, out var path))
        {
            _tab.IsPdfPreview = true;
            _tab.PdfPreviewPath = path;
            _tab.NotifyChromeChanged();
            return;
        }

        _tab.IsPdfPreview = false;
        _tab.PdfPreviewPath = null;
        _tab.NotifyChromeChanged();
    }

    private void RefreshHistory()
    {
        if (_tab is null || _webView?.CoreWebView2 is null)
        {
            return;
        }

        _tab.CanGoBack = _webView.CoreWebView2.CanGoBack;
        _tab.CanGoForward = _webView.CoreWebView2.CanGoForward;
        _tab.NotifyChromeChanged();
    }

    private void OpenPdfPreview(string previewUrl, string downloadUri)
        => _ = OpenPdfPreviewAsync(previewUrl, downloadUri);

    private async Task OpenPdfPreviewAsync(string previewUrl, string downloadUri)
    {
        try
        {
            await RunOnUiAsync(() =>
            {
                if (_disposed || _tab is null || _webView?.CoreWebView2 is null)
                {
                    return;
                }

                if (ShouldShowPdfInCurrentTab(_webView.CoreWebView2.Source, downloadUri))
                {
                    _logger.LogInformation("PDF-Vorschau im aktuellen Tab");
                    _tab.ClearError();
                    _webView.CoreWebView2.Navigate(previewUrl);
                    return;
                }

                _logger.LogInformation("PDF-Vorschau in neuem Tab");
                _tab.RequestOpenUrlInNewTab(previewUrl);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PDF-Vorschau konnte nicht geöffnet werden");
        }
    }

    private static bool ShouldShowPdfInCurrentTab(string? source, string? downloadUri)
    {
        if (string.IsNullOrWhiteSpace(source)
            || source.StartsWith("about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(downloadUri)
            && string.Equals(source, downloadUri, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IsPdfSource(source);
    }

    private static bool IsPdfSource(string? source)
        => Uri.TryCreate(source, UriKind.Absolute, out var uri)
           && (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
               || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
           && (uri.IdnHost.Equals(NavigationGuard.PdfPreviewHost, StringComparison.OrdinalIgnoreCase)
               || uri.AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));

    private static bool IsIgnorableNavigationError(CoreWebView2WebErrorStatus status)
        => status is CoreWebView2WebErrorStatus.OperationCanceled
            or CoreWebView2WebErrorStatus.ConnectionAborted
            or CoreWebView2WebErrorStatus.ValidAuthenticationCredentialsRequired
            or CoreWebView2WebErrorStatus.ValidProxyAuthenticationRequired;

    private static Task RunOnUiAsync(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action, DispatcherPriority.DataBind).Task;
    }
}
