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
    private readonly IOutlookMailService _outlookMail;
    private readonly SemaphoreSlim _uploadGate = new(1, 1);

    public MainViewModel(
        TabManager tabs,
        AppSettings settings,
        AppUpdateState update,
        IAppUpdateService updates,
        TraySession tray,
        IUserSettingsStore userSettings,
        IImportUploadService uploads,
        ISettingsDialogService settingsDialog,
        IOutlookMailService outlookMail)
    {
        Tabs = tabs;
        _settings = settings;
        Update = update;
        _updates = updates;
        _tray = tray;
        _userSettings = userSettings;
        _uploads = uploads;
        _settingsDialog = settingsDialog;
        _outlookMail = outlookMail;
        Tabs.ChromeStateChanged += OnChromeStateChanged;
        Update.PropertyChanged += OnUpdatePropertyChanged;
        _userSettings.Changed += OnUserSettingsChanged;
        Tabs.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TabManager.ActiveTab))
            {
                OnPropertyChanged(nameof(WindowTitle));
                OnPropertyChanged(nameof(ShowPdfMailButton));
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

    public bool ShowPdfMailButton => ShowChromeActions && Tabs.ActiveTab?.IsPdfPreview == true;

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

    [RelayCommand(CanExecute = nameof(CanSendActivePdf))]
    private async Task SendActivePdf()
    {
        var path = Tabs.ActiveTab?.PdfPreviewPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var error = await _outlookMail.ShowAsync(new OutlookMailDraft { AttachmentPath = path });
        if (error is null)
        {
            return;
        }

        MessageBox.Show(
            Application.Current?.MainWindow,
            error,
            "BIS Client",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private bool CanSendActivePdf()
        => ShowPdfMailButton && !string.IsNullOrWhiteSpace(Tabs.ActiveTab?.PdfPreviewPath);

    [RelayCommand]
    private void OpenSettings()
    {
        var before = _settings.BisUrl;
        if (_settingsDialog.Show(Application.Current?.MainWindow) == true
            && !string.Equals(before, _settings.BisUrl, StringComparison.OrdinalIgnoreCase))
        {
            Tabs.ReplaceWithHome();
        }
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

        await UploadShellFilesAsync(dialog.FileNames, cancellationToken);
    }

    public Task UploadShellFilesAsync(IReadOnlyList<string> filePaths, CancellationToken cancellationToken = default)
    {
        if (filePaths.Count == 0)
        {
            return Task.CompletedTask;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            return dispatcher.InvokeAsync(() => UploadShellFilesAsync(filePaths, cancellationToken)).Task.Unwrap();
        }

        return UploadShellFilesCoreAsync(filePaths, cancellationToken);
    }

    private async Task UploadShellFilesCoreAsync(IReadOnlyList<string> filePaths, CancellationToken cancellationToken)
    {
        await _uploadGate.WaitAsync(cancellationToken);
        IsUploading = true;
        try
        {
            var summary = await _uploads.UploadAsync(filePaths, cancellationToken);
            var icon = summary.Failed == 0 && summary.Details.Count == 0
                ? MessageBoxImage.Information
                : MessageBoxImage.Warning;
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
            _uploadGate.Release();
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
        _uploadGate.Dispose();
    }

    private void OnChromeStateChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(ShowPdfMailButton));
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
        SendActivePdfCommand.NotifyCanExecuteChanged();
    }

    private void RefreshUpdateCommands()
    {
        ApplyUpdateCommand.NotifyCanExecuteChanged();
        DismissUpdateCommand.NotifyCanExecuteChanged();
    }
}
