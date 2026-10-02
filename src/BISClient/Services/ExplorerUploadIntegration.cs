using BISClient.Configuration;
using Microsoft.Extensions.Logging;

namespace BISClient.Services;

public interface IExplorerUploadIntegration
{
    Task StartAsync();
}

public sealed class ExplorerUploadIntegration : IExplorerUploadIntegration, IDisposable
{
    private readonly AppSettings _settings;
    private readonly IUserSettingsStore _userSettings;
    private readonly ISendToShortcutService _sendTo;
    private readonly IShareTargetRegistrationService _shareTarget;
    private readonly ILogger<ExplorerUploadIntegration> _logger;
    private int _started;

    public ExplorerUploadIntegration(
        AppSettings settings,
        IUserSettingsStore userSettings,
        ISendToShortcutService sendTo,
        IShareTargetRegistrationService shareTarget,
        ILogger<ExplorerUploadIntegration> logger)
    {
        _settings = settings;
        _userSettings = userSettings;
        _sendTo = sendTo;
        _shareTarget = shareTarget;
        _logger = logger;
    }

    public async Task StartAsync()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return;
        }

        _userSettings.Changed += OnUserSettingsChanged;
        await SyncAsync();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _started, 0) == 1)
        {
            _userSettings.Changed -= OnUserSettingsChanged;
        }
    }

    private void OnUserSettingsChanged(object? sender, EventArgs e)
        => _ = SyncAsync();

    private async Task SyncAsync()
    {
        var enabled = !_settings.KioskMode && _userSettings.Current.HasUploadToken;
        try
        {
            _sendTo.Sync(enabled);
            await _shareTarget.SyncAsync(enabled);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Explorer-Upload-Integration konnte nicht abgeglichen werden");
        }
    }
}
