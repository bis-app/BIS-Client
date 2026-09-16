using System.Windows;
using BISClient.Configuration;
using BISClient.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace BISClient.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly AppSettings _settings;
    private readonly IAppUpdateService _updates;
    private readonly TraySession _tray;
    private readonly IUserSettingsStore _userSettings;
    private readonly IImportUploadService _uploads;
    private readonly ISettingsDialogService _settingsDialog;

    public MainViewModel(
        TabManager tabs,
        AppSettings settings,
        AppUpdateState update,
        IAppUpdateService updates,
        TraySession tray,
        IUserSettingsStore userSettings,
        IImportUploadService uploads,
        ISettingsDialogService settingsDialog)
    {
        Tabs = tabs;
        _settings = settings;
        Update = update;
        _updates = updates;
        _tray = tray;
        _userSettings = userSettings;
        _uploads = uploads;
        _settingsDialog = settingsDialog;
        Tabs.ChromeStateChanged += OnChromeStateChanged;
        Update.PropertyChanged += OnUpdatePropertyChanged;
        _userSettings.Changed += OnUserSettingsChanged;
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

    public AppUpdateState Update { get; }

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

    public bool ShowChromeActions => !_settings.KioskMode;

    public bool ShowUploadButton => ShowChromeActions && _userSettings.Current.HasUploadToken;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UploadFilesCommand))]
    private bool _isUploading;

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

    [RelayCommand]
    private void OpenSettings()
    {
        _settingsDialog.Show(Application.Current?.MainWindow);
    }

    [RelayCommand(CanExecute = nameof(CanUploadFiles))]
    private async Task UploadFiles(CancellationToken cancellationToken)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Dateien in den Import-Ordner hochladen",
            Filter = _uploads.FileDialogFilter,
            Multiselect = true,
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != true || dialog.FileNames.Length == 0)
        {
            return;
        }

        IsUploading = true;
        try
        {
            var summary = await _uploads.UploadAsync(dialog.FileNames, cancellationToken);
            var icon = summary.Failed == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning;
            MessageBox.Show(
                Application.Current?.MainWindow,
                summary.ToDisplayText(),
                "BIS Client",
                MessageBoxButton.OK,
                icon);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            MessageBox.Show(
                Application.Current?.MainWindow,
                "Die Dateien konnten nicht hochgeladen werden.",
                "BIS Client",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsUploading = false;
        }
    }

    private bool CanUploadFiles() => ShowUploadButton && !IsUploading;

    [RelayCommand(CanExecute = nameof(CanApplyUpdate))]
    private async Task ApplyUpdate(CancellationToken cancellationToken)
    {
        var info = Update.Current;
        if (info is null || Update.IsBusy)
        {
            return;
        }

        Update.BeginDownload();
        RefreshUpdateCommands();

        try
        {
            var progress = new Progress<double>(Update.ReportProgress);
            var path = await _updates.DownloadAsync(info, progress, cancellationToken);
            if (!_updates.TryLaunchInstaller(path))
            {
                Update.Fail("Das Setup konnte nicht gestartet werden.");
                return;
            }

            _tray.RequestExit();
        }
        catch (OperationCanceledException)
        {
            Update.Fail("Der Download wurde abgebrochen.");
        }
        catch (Exception)
        {
            Update.Fail("Das Update konnte nicht heruntergeladen werden.");
        }
        finally
        {
            RefreshUpdateCommands();
        }
    }

    private bool CanApplyUpdate() => Update.CanApply;

    [RelayCommand(CanExecute = nameof(CanDismissUpdate))]
    private void DismissUpdate()
    {
        Update.Dismiss();
        RefreshUpdateCommands();
    }

    private bool CanDismissUpdate() => Update.IsBannerVisible && !Update.IsBusy;

    public void Dispose()
    {
        Tabs.ChromeStateChanged -= OnChromeStateChanged;
        Update.PropertyChanged -= OnUpdatePropertyChanged;
        _userSettings.Changed -= OnUserSettingsChanged;
        Tabs.Dispose();
    }

    private void OnChromeStateChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(WindowTitle));
        RefreshCommands();
    }

    private void OnUpdatePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        => RefreshUpdateCommands();

    private void OnUserSettingsChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(ShowUploadButton));
        UploadFilesCommand.NotifyCanExecuteChanged();
    }

    private void RefreshCommands()
    {
        GoBackCommand.NotifyCanExecuteChanged();
        GoForwardCommand.NotifyCanExecuteChanged();
    }

    private void RefreshUpdateCommands()
    {
        ApplyUpdateCommand.NotifyCanExecuteChanged();
        DismissUpdateCommand.NotifyCanExecuteChanged();
    }
}
