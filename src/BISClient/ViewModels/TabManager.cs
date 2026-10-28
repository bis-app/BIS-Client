using System.Collections.ObjectModel;
using BISClient.Configuration;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

namespace BISClient.ViewModels;

public sealed class TabManager : ObservableObject, IDisposable
{
    private readonly IServiceProvider _services;
    private readonly AppSettings _settings;
    private readonly ILogger<TabManager> _logger;
    private BrowserTabViewModel? _activeTab;

    public TabManager(IServiceProvider services, AppSettings settings, ILogger<TabManager> logger)
    {
        _services = services;
        _settings = settings;
        _logger = logger;
        Tabs = [];
    }

    public ObservableCollection<BrowserTabViewModel> Tabs { get; }

    public BrowserTabViewModel? ActiveTab
    {
        get => _activeTab;
        private set
        {
            if (_activeTab == value)
            {
                return;
            }

            if (_activeTab is not null)
            {
                _activeTab.IsActive = false;
            }

            _activeTab = value;
            if (_activeTab is not null)
            {
                _activeTab.IsActive = true;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(CanGoForward));
            ChromeStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool CanGoBack => ActiveTab?.CanGoBack == true;

    public bool CanGoForward => ActiveTab?.CanGoForward == true;

    public event EventHandler? ChromeStateChanged;

    public BrowserTabViewModel OpenHomeTab() => OpenTab(_settings.BisUrl);

    public void ReplaceWithHome()
    {
        foreach (var tab in Tabs.ToArray())
        {
            Tabs.Remove(tab);
            Unhook(tab);
            tab.Dispose();
            _logger.LogInformation("Tab geschlossen {TabId}", tab.Id);
        }

        ActiveTab = null;
        OpenHomeTab();
    }

    public void NavigateTo(string url)
    {
        var tab = ActiveTab ?? Tabs.FirstOrDefault();
        if (tab is null)
        {
            OpenTab(url);
            return;
        }

        tab.Navigate(url);
    }

    public BrowserTabViewModel OpenTab(string? url)
    {
        var tab = _services.GetRequiredService<BrowserTabViewModel>();
        tab.StartUrl = string.IsNullOrWhiteSpace(url) ? string.Empty : url;
        tab.Title = "BIS";
        tab.ChromeStateChanged += OnTabChromeChanged;
        tab.OpenUrlInNewTabRequested += OnOpenUrlInNewTabRequested;
        tab.BlankNewWindowRequested += OnBlankNewWindowRequested;
        tab.AcceleratorRequested += OnAcceleratorRequested;
        Tabs.Add(tab);
        ActiveTab = tab;
        _logger.LogInformation("Tab erstellt {TabId}", tab.Id);
        return tab;
    }

    public void Activate(BrowserTabViewModel tab)
    {
        if (Tabs.Contains(tab))
        {
            ActiveTab = tab;
        }
    }

    public void CloseTab(BrowserTabViewModel? tab)
    {
        tab ??= ActiveTab;
        if (tab is null || !Tabs.Contains(tab))
        {
            return;
        }

        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        Unhook(tab);
        tab.Dispose();
        _logger.LogInformation("Tab geschlossen {TabId}", tab.Id);

        if (Tabs.Count == 0)
        {
            OpenHomeTab();
            return;
        }

        var nextIndex = Math.Min(index, Tabs.Count - 1);
        ActiveTab = Tabs[nextIndex];
    }

    public void CloseActiveTab() => CloseTab(ActiveTab);

    public void ActivateNext()
    {
        if (ActiveTab is null || Tabs.Count == 0)
        {
            return;
        }

        var index = Tabs.IndexOf(ActiveTab);
        ActiveTab = Tabs[(index + 1) % Tabs.Count];
    }

    public void ActivatePrevious()
    {
        if (ActiveTab is null || Tabs.Count == 0)
        {
            return;
        }

        var index = Tabs.IndexOf(ActiveTab);
        ActiveTab = Tabs[(index - 1 + Tabs.Count) % Tabs.Count];
    }

    public void Dispose()
    {
        foreach (var tab in Tabs.ToArray())
        {
            Unhook(tab);
            tab.Dispose();
        }

        Tabs.Clear();
        ActiveTab = null;
    }

    private void Unhook(BrowserTabViewModel tab)
    {
        tab.ChromeStateChanged -= OnTabChromeChanged;
        tab.OpenUrlInNewTabRequested -= OnOpenUrlInNewTabRequested;
        tab.BlankNewWindowRequested -= OnBlankNewWindowRequested;
        tab.AcceleratorRequested -= OnAcceleratorRequested;
    }

    private void OnTabChromeChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        ChromeStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnOpenUrlInNewTabRequested(object? sender, string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            OpenHomeTab();
            return;
        }

        OpenTab(url);
    }

    private async void OnBlankNewWindowRequested(object? sender, TaskCompletionSource<CoreWebView2?> completion)
    {
        try
        {
            var tab = OpenTab(string.Empty);
            var core = await tab.WaitForCoreAsync();
            completion.TrySetResult(core);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Leeres neues Fenster konnte nicht bereitgestellt werden");
            completion.TrySetResult(null);
        }
    }

    private void OnAcceleratorRequested(object? sender, WebViewAccelerator accelerator)
    {
        switch (accelerator)
        {
            case WebViewAccelerator.NewTab:
                OpenHomeTab();
                break;
            case WebViewAccelerator.CloseTab:
                CloseActiveTab();
                break;
            case WebViewAccelerator.NextTab:
                ActivateNext();
                break;
            case WebViewAccelerator.PreviousTab:
                ActivatePrevious();
                break;
            case WebViewAccelerator.Reload:
                ActiveTab?.Reload();
                break;
        }
    }
}
