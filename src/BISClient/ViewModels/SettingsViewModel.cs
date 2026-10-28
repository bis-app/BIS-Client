using BISClient.Configuration;
using BISClient.Models;
using BISClient.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BISClient.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IUserSettingsStore _store;
    private readonly AppSettings _appSettings;

    public SettingsViewModel(IUserSettingsStore store, AppSettings appSettings)
    {
        _store = store;
        _appSettings = appSettings;
        var current = store.Current;
        NotificationsEnabled = current.NotificationsEnabled;
        UploadToken = current.UploadToken;
        ServerUrl = appSettings.BisUrl;
        DeleteUploadedFiles = current.DeleteUploadedFiles;
    }

    [ObservableProperty]
    private bool _notificationsEnabled = true;

    [ObservableProperty]
    private string _uploadToken = string.Empty;

    [ObservableProperty]
    private string _serverUrl = string.Empty;

    [ObservableProperty]
    private bool _deleteUploadedFiles;

    public bool TrySave(out string? error)
    {
        if (!AppSettings.TryNormalizeServerUrl(ServerUrl, out var normalized, out error))
        {
            return false;
        }

        var storedUrl = string.IsNullOrEmpty(normalized)
                        || string.Equals(normalized, _appSettings.ConfiguredBisUrl, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : normalized;

        _store.Save(new UserSettings
        {
            NotificationsEnabled = NotificationsEnabled,
            UploadToken = UploadToken ?? string.Empty,
            ServerUrl = storedUrl,
            DeleteUploadedFiles = DeleteUploadedFiles
        });
        error = null;
        return true;
    }
}
