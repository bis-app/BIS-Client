namespace BISClient.Models;

public sealed class UserSettings
{
    public bool NotificationsEnabled { get; init; } = true;

    public string UploadToken { get; init; } = string.Empty;

    public bool HasUploadToken => !string.IsNullOrWhiteSpace(UploadToken);
}
