using System.Windows;
using System.Windows.Controls;
using BISClient.ViewModels;

namespace BISClient.Controls;

public partial class BisWebViewHost : UserControl
{
    private readonly SemaphoreSlim _attachLock = new(1, 1);
    private BrowserTabViewModel? _attachedTab;
    private bool _webViewDisposed;

    public BisWebViewHost()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is BrowserTabViewModel tab)
        {
            await AttachAsync(tab);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachAndDispose();
    }

    private async void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is BrowserTabViewModel oldTab)
        {
            oldTab.Detach();
        }

        if (IsLoaded && e.NewValue is BrowserTabViewModel tab)
        {
            await AttachAsync(tab);
        }
    }

    private async Task AttachAsync(BrowserTabViewModel tab)
    {
        if (_webViewDisposed)
        {
            return;
        }

        await _attachLock.WaitAsync();
        try
        {
            if (_attachedTab == tab && WebView.CoreWebView2 is not null)
            {
                return;
            }

            _attachedTab = tab;
            await tab.AttachAsync(WebView);
        }
        catch (Exception)
        {
            tab.ShowServerUnavailable();
        }
        finally
        {
            _attachLock.Release();
        }
    }

    private void DetachAndDispose()
    {
        try
        {
            _attachedTab?.Detach();
        }
        catch
        {
            // WebView kann beim Entladen bereits freigegeben sein.
        }

        _attachedTab = null;

        if (_webViewDisposed)
        {
            return;
        }

        _webViewDisposed = true;
        try
        {
            WebView.Dispose();
        }
        catch
        {
            // Best-effort Cleanup
        }
    }
}
