using System.IO;

namespace BISClient.Configuration;

public sealed class AppSettings
{
    public const string SectionName = "BisClient";

    public string BisUrl { get; set; } = string.Empty;

    public string ConfiguredBisUrl => _configuredBisUrl;

    public List<string> AllowedHosts { get; set; } = [];

    private string _configuredBisUrl = string.Empty;

    private List<string> _baseAllowedHosts = [];

    public bool KioskMode { get; set; }

    public bool AllowDownloads { get; set; }

    public string DownloadFolder { get; set; } = @"%USERPROFILE%\Downloads\BISClient";

    public bool OpenNewWindowsInTabs { get; set; } = true;

    public bool EnableDeveloperTools { get; set; }

    public bool DevelopmentMode { get; set; }

    public string UserDataFolder { get; set; } = @"%LOCALAPPDATA%\BISClient";

    public string LogDirectory { get; set; } = @"%LOCALAPPDATA%\BISClient\Logs";

    public bool FullscreenToggleEnabled { get; set; } = true;

    public bool MinimizeToTray { get; set; } = true;

    public int NotificationPollIntervalSeconds { get; set; } = 30;

    public bool UpdateCheckEnabled { get; set; } = true;

    public string UpdateManifestUrl { get; set; } = string.Empty;

    public int UpdateCheckIntervalHours { get; set; } = 24;

    public bool AreDevToolsEnabled => DevelopmentMode && EnableDeveloperTools;

    public bool TrayNotificationsEnabled => MinimizeToTray && !KioskMode;

    public string AppDataRoot { get; private set; } = string.Empty;

    public string WebViewUserDataFolder { get; private set; } = string.Empty;

    public string WindowStateFile { get; private set; } = string.Empty;

    public string ShownNotificationsFile { get; private set; } = string.Empty;

    public string ActivationRequestFile { get; private set; } = string.Empty;

    public string UserSettingsFile { get; private set; } = string.Empty;

    public string PdfPreviewFolder { get; private set; } = string.Empty;

    public void Normalize()
    {
        BisUrl = (BisUrl ?? string.Empty).Trim();
        UpdateManifestUrl = (UpdateManifestUrl ?? string.Empty).Trim();
        UserDataFolder = PathExpander.Expand(UserDataFolder);
        LogDirectory = PathExpander.Expand(LogDirectory);
        DownloadFolder = PathExpander.Expand(DownloadFolder);

        if (NotificationPollIntervalSeconds < 10)
        {
            NotificationPollIntervalSeconds = 10;
        }
        else if (NotificationPollIntervalSeconds > 300)
        {
            NotificationPollIntervalSeconds = 300;
        }

        if (UpdateCheckIntervalHours < 1)
        {
            UpdateCheckIntervalHours = 1;
        }
        else if (UpdateCheckIntervalHours > 168)
        {
            UpdateCheckIntervalHours = 168;
        }

        AppDataRoot = UserDataFolder;
        WebViewUserDataFolder = Path.Combine(AppDataRoot, "WebView2");
        WindowStateFile = Path.Combine(AppDataRoot, "window.json");
        ShownNotificationsFile = Path.Combine(AppDataRoot, "shown-notifications.json");
        ActivationRequestFile = Path.Combine(AppDataRoot, "activate.json");
        UserSettingsFile = Path.Combine(AppDataRoot, "user-settings.json");
        PdfPreviewFolder = Path.Combine(AppDataRoot, "PdfPreview");

        Directory.CreateDirectory(AppDataRoot);
        Directory.CreateDirectory(WebViewUserDataFolder);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(PdfPreviewFolder);

        AllowedHosts ??= [];
        AllowedHosts = AllowedHosts
            .Where(static h => !string.IsNullOrWhiteSpace(h))
            .Select(static h => h.Trim().TrimEnd('.').ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (Uri.TryCreate(BisUrl, UriKind.Absolute, out var bisUri)
            && !string.IsNullOrWhiteSpace(bisUri.IdnHost))
        {
            var host = bisUri.IdnHost.ToLowerInvariant();
            if (!AllowedHosts.Contains(host, StringComparer.OrdinalIgnoreCase))
            {
                AllowedHosts.Add(host);
            }
        }

        _configuredBisUrl = BisUrl;
        _baseAllowedHosts = AllowedHosts.ToList();
    }

    public static bool TryNormalizeServerUrl(string? value, out string normalized, out string? error)
    {
        normalized = string.Empty;
        error = null;
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return true;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(uri.IdnHost))
        {
            error = "Die Server-Adresse muss eine vollständige http- oder https-Adresse sein.";
            return false;
        }

        var builder = new UriBuilder(uri)
        {
            Fragment = string.Empty,
            Query = string.Empty
        };
        var text = builder.Uri.AbsoluteUri;
        if (!text.EndsWith('/'))
        {
            text += "/";
        }

        normalized = text;
        return true;
    }

    public void ApplyServerUrl(string? normalized)
    {
        BisUrl = string.IsNullOrWhiteSpace(normalized) ? _configuredBisUrl : normalized.Trim();
        var hosts = new List<string>(_baseAllowedHosts);
        if (Uri.TryCreate(BisUrl, UriKind.Absolute, out var bisUri)
            && !string.IsNullOrWhiteSpace(bisUri.IdnHost))
        {
            var host = bisUri.IdnHost.ToLowerInvariant();
            if (!hosts.Contains(host, StringComparer.OrdinalIgnoreCase))
            {
                hosts.Add(host);
            }
        }

        AllowedHosts = hosts;
    }
}

internal static class PathExpander
{
    public static string Expand(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return Environment.ExpandEnvironmentVariables(path.Trim());
    }
}
