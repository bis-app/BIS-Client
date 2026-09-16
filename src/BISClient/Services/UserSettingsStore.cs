using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BISClient.Configuration;
using BISClient.Models;
using Microsoft.Extensions.Logging;

namespace BISClient.Services;

public interface IUserSettingsStore
{
    UserSettings Current { get; }

    event EventHandler? Changed;

    void Save(UserSettings settings);
}

public sealed class UserSettingsStore : IUserSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static readonly byte[] Entropy = "BISClient.UserSettings.v1"u8.ToArray();

    private readonly AppSettings _appSettings;
    private readonly ILogger<UserSettingsStore> _logger;
    private readonly object _gate = new();
    private UserSettings _current;

    public UserSettingsStore(AppSettings appSettings, ILogger<UserSettingsStore> logger)
    {
        _appSettings = appSettings;
        _logger = logger;
        _current = Load();
    }

    public UserSettings Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public event EventHandler? Changed;

    public void Save(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var snapshot = new UserSettings
        {
            NotificationsEnabled = settings.NotificationsEnabled,
            UploadToken = settings.UploadToken?.Trim() ?? string.Empty
        };

        try
        {
            var dto = new UserSettingsFileDto
            {
                NotificationsEnabled = snapshot.NotificationsEnabled,
                UploadTokenProtected = ProtectToken(snapshot.UploadToken)
            };

            Directory.CreateDirectory(Path.GetDirectoryName(_appSettings.UserSettingsFile)!);
            File.WriteAllText(_appSettings.UserSettingsFile, JsonSerializer.Serialize(dto, JsonOptions));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Persönliche Einstellungen konnten nicht gespeichert werden");
            throw;
        }

        lock (_gate)
        {
            _current = snapshot;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private UserSettings Load()
    {
        try
        {
            if (!File.Exists(_appSettings.UserSettingsFile))
            {
                return new UserSettings();
            }

            var dto = JsonSerializer.Deserialize<UserSettingsFileDto>(
                File.ReadAllText(_appSettings.UserSettingsFile),
                JsonOptions);
            if (dto is null)
            {
                return new UserSettings();
            }

            return new UserSettings
            {
                NotificationsEnabled = dto.NotificationsEnabled,
                UploadToken = UnprotectToken(dto.UploadTokenProtected)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Persönliche Einstellungen konnten nicht gelesen werden");
            return new UserSettings();
        }
    }

    private static string? ProtectToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var bytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(token),
            Entropy,
            DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    private string UnprotectToken(string? protectedToken)
    {
        if (string.IsNullOrWhiteSpace(protectedToken))
        {
            return string.Empty;
        }

        try
        {
            var bytes = ProtectedData.Unprotect(
                Convert.FromBase64String(protectedToken),
                Entropy,
                DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Upload-Token konnte nicht entschlüsselt werden");
            return string.Empty;
        }
    }

    private sealed class UserSettingsFileDto
    {
        public bool NotificationsEnabled { get; set; } = true;

        public string? UploadTokenProtected { get; set; }
    }
}
