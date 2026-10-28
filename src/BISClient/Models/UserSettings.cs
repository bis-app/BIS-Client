namespace BISClient.Models;

public sealed class UserSettings
{
    public bool NotificationsEnabled { get; init; } = true;

    public string UploadToken { get; init; } = string.Empty;

    public string ServerUrl { get; init; } = string.Empty;

    public bool DeleteUploadedFiles { get; init; }

    public bool HasUploadToken => !string.IsNullOrWhiteSpace(UploadToken);
}
