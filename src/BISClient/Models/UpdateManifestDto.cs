namespace BISClient.Models;

internal sealed class UpdateManifestDto
{
    public string? Version { get; set; }

    public string? MinVersion { get; set; }

    public string? DownloadUrl { get; set; }

    public string? Sha256 { get; set; }

    public string? Notes { get; set; }
}
