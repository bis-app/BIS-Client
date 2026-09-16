namespace BISClient.Services;

public sealed class AppUpdateInfo
{
    public AppUpdateInfo(Version version, Uri downloadUri, string sha256, string? notes)
    {
        Version = version;
        DownloadUri = downloadUri;
        Sha256 = sha256;
        Notes = notes;
    }

    public Version Version { get; }

    public Uri DownloadUri { get; }

    public string Sha256 { get; }

    public string? Notes { get; }

    public string DisplayVersion => ClientVersion.Format(Version);
}
