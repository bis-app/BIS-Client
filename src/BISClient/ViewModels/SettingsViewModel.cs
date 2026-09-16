using BISClient.Models;
using BISClient.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BISClient.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IUserSettingsStore _store;

    public SettingsViewModel(IUserSettingsStore store)
    {
        _store = store;
        var current = store.Current;
        NotificationsEnabled = current.NotificationsEnabled;
        UploadToken = current.UploadToken;
    }

    [ObservableProperty]
    private bool _notificationsEnabled = true;

    [ObservableProperty]
    private string _uploadToken = string.Empty;

    public void Save()
    {
        _store.Save(new UserSettings
        {
            NotificationsEnabled = NotificationsEnabled,
            UploadToken = UploadToken ?? string.Empty
        });
    }
}
