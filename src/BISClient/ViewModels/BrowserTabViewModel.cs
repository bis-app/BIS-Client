using System.Windows.Media.Imaging;
using BISClient.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace BISClient.ViewModels;

public sealed partial class BrowserTabViewModel : ObservableObject, IDisposable
{
    private readonly WebViewTabSession _session;
    private bool _disposed;

    public BrowserTabViewModel(WebViewTabSession session)
    {
        _session = session;
        _session.AttachViewModel(this);
        Title = "BIS";
        StartUrl = string.Empty;
    }

    public Guid Id { get; } = Guid.NewGuid();

    public string StartUrl { get; set; } = string.Empty;

    public event EventHandler<TaskCompletionSource<CoreWebView2?>>? BlankNewWindowRequested;
    public event EventHandler<string?>? OpenUrlInNewTabRequested;
    public event EventHandler? ChromeStateChanged;

    [ObservableProperty]
    private string _title = "BIS";

    [ObservableProperty]
    private BitmapSource? _favicon;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _canGoBack;

    [ObservableProperty]
    private bool _canGoForward;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isPdfPreview;

    [ObservableProperty]
    private string? _pdfPreviewPath;

    [ObservableProperty]
    private TabErrorKind _errorKind = TabErrorKind.None;

    [ObservableProperty]
    private string _errorTitle = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public bool HasError => ErrorKind != TabErrorKind.None;

    public bool ShowRetry => ErrorKind == TabErrorKind.ServerUnavailable;

    public bool ShowDismiss => ErrorKind == TabErrorKind.NavigationBlocked;

    public Task AttachAsync(WebView2 webView, CancellationToken cancellationToken = default)
        => _session.AttachAsync(webView, cancellationToken);

    public void Detach() => _session.Detach();

    public void GoBack() => _session.GoBack();

    public void GoForward() => _session.GoForward();

    public void Reload()
    {
        ClearError();
        _session.Reload();
    }

    public void NavigateHome()
    {
        ClearError();
        _session.Navigate(StartUrl);
    }

    public void Navigate(string url)
    {
        ClearError();
        _session.Navigate(url);
    }

    public Task<CoreWebView2?> WaitForCoreAsync(CancellationToken cancellationToken = default)
        => _session.WaitForCoreAsync(cancellationToken);

    public void HandleAccelerator(WebViewAccelerator accelerator)
        => AcceleratorRequested?.Invoke(this, accelerator);

    public event EventHandler<WebViewAccelerator>? AcceleratorRequested;

    public void RequestBlankNewWindow(TaskCompletionSource<CoreWebView2?> completion)
        => BlankNewWindowRequested?.Invoke(this, completion);

    public void RequestOpenUrlInNewTab(string? url) => OpenUrlInNewTabRequested?.Invoke(this, url);

    public void NotifyChromeChanged() => ChromeStateChanged?.Invoke(this, EventArgs.Empty);

    public void ShowServerUnavailable()
    {
        ErrorKind = TabErrorKind.ServerUnavailable;
        ErrorTitle = "BIS-Server nicht erreichbar.";
        ErrorMessage = "Die Verbindung zum BIS-Server ist fehlgeschlagen. Prüfen Sie das Netzwerk und versuchen Sie es erneut.";
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(ShowRetry));
        OnPropertyChanged(nameof(ShowDismiss));
    }

    public void ShowNavigationBlocked()
    {
        ErrorKind = TabErrorKind.NavigationBlocked;
        ErrorTitle = "Diese Adresse ist nicht erlaubt.";
        ErrorMessage = "Der BIS Client darf nur freigegebene BIS-Adressen öffnen.";
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(ShowRetry));
        OnPropertyChanged(nameof(ShowDismiss));
    }

    public void ClearError()
    {
        if (ErrorKind == TabErrorKind.None)
        {
            return;
        }

        ErrorKind = TabErrorKind.None;
        ErrorTitle = string.Empty;
        ErrorMessage = string.Empty;
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(ShowRetry));
        OnPropertyChanged(nameof(ShowDismiss));
    }

    [RelayCommand]
    private void Retry()
    {
        ClearError();
        _session.ReloadOrHome();
    }

    [RelayCommand]
    private void DismissError() => ClearError();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.Dispose();
    }
}

public enum WebViewAccelerator
{
    NewTab,
    CloseTab,
    NextTab,
    PreviousTab,
    Reload
}
