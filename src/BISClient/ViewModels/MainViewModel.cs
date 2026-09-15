using BISClient.Configuration;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BISClient.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly AppSettings _settings;

    public MainViewModel(TabManager tabs, AppSettings settings)
    {
        Tabs = tabs;
        _settings = settings;
        Tabs.ChromeStateChanged += OnChromeStateChanged;
        Tabs.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TabManager.ActiveTab))
            {
                OnPropertyChanged(nameof(WindowTitle));
                RefreshCommands();
            }
        };
    }

    public TabManager Tabs { get; }

    public string WindowTitle
    {
        get
        {
            var tabTitle = Tabs.ActiveTab?.Title;
            if (string.IsNullOrWhiteSpace(tabTitle) || tabTitle == "BIS")
            {
                return "BIS Client";
            }

            return $"{tabTitle} – BIS Client";
        }
    }

    public bool IsKioskMode => _settings.KioskMode;

    public void Initialize()
    {
        if (Tabs.Tabs.Count == 0)
        {
            Tabs.OpenHomeTab();
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack() => Tabs.ActiveTab?.GoBack();

    private bool CanGoBack() => Tabs.CanGoBack;

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void GoForward() => Tabs.ActiveTab?.GoForward();

    private bool CanGoForward() => Tabs.CanGoForward;

    [RelayCommand]
    private void Reload() => Tabs.ActiveTab?.Reload();

    [RelayCommand]
    private void NewTab() => Tabs.OpenHomeTab();

    [RelayCommand]
    private void CloseTab(BrowserTabViewModel? tab) => Tabs.CloseTab(tab);

    [RelayCommand]
    private void ActivateTab(BrowserTabViewModel? tab)
    {
        if (tab is not null)
        {
            Tabs.Activate(tab);
        }
    }

    [RelayCommand]
    private void NextTab() => Tabs.ActivateNext();

    [RelayCommand]
    private void PreviousTab() => Tabs.ActivatePrevious();

    [RelayCommand]
    private void CloseActiveTab() => Tabs.CloseActiveTab();

    public void Dispose()
    {
        Tabs.ChromeStateChanged -= OnChromeStateChanged;
        Tabs.Dispose();
    }

    private void OnChromeStateChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(WindowTitle));
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        GoBackCommand.NotifyCanExecuteChanged();
        GoForwardCommand.NotifyCanExecuteChanged();
    }
}
