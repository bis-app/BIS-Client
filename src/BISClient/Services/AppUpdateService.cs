using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Diagnostics;
using BISClient.Configuration;
using BISClient.Models;
using Microsoft.Extensions.Logging;

namespace BISClient.Services;

public interface IAppUpdateService
{
    Task<AppUpdateInfo?> CheckAsync(CancellationToken cancellationToken);

    Task<string> DownloadAsync(AppUpdateInfo info, IProgress<double>? progress, CancellationToken cancellationToken);

    bool TryLaunchInstaller(string setupPath);
}

public sealed class AppUpdateService : IAppUpdateService, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AppSettings _settings;
    private readonly INavigationGuard _navigationGuard;
    private readonly ILogger<AppUpdateService> _logger;
    private readonly HttpClient _httpClient;

    public AppUpdateService(
        AppSettings settings,
        INavigationGuard navigationGuard,
        ILogger<AppUpdateService> logger)
    {
        _settings = settings;
        _navigationGuard = navigationGuard;
        _logger = logger;
        _httpClient = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            UseCookies = false
        })
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(ClientVersion.UserAgent);
    }

    public async Task<AppUpdateInfo?> CheckAsync(CancellationToken cancellationToken)
    {
        var manifestUri = ResolveManifestUri();
        if (manifestUri is null || !_navigationGuard.IsAllowed(manifestUri))
        {
            _logger.LogDebug("Update-Manifest-URL ist nicht erlaubt oder ungültig");
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, manifestUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        using var response = await _httpClient.SendAsync(request, cts.Token);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogDebug("Update-Manifest antwortete {Status}", (int)response.StatusCode);
            return null;
        }

        var payload = await response.Content.ReadAsStringAsync(cts.Token);
        var dto = JsonSerializer.Deserialize<UpdateManifestDto>(payload, JsonOptions);
        if (dto is null || !ClientVersion.TryParse(dto.Version, out var remoteVersion))
        {
            _logger.LogWarning("Update-Manifest ist ungültig");
            return null;
        }

        if (!ClientVersion.IsNewer(remoteVersion, ClientVersion.Current))
        {
            _logger.LogDebug(
                "Kein Client-Update: lokal {Local}, remote {Remote}",
                ClientVersion.Display,
                ClientVersion.Format(remoteVersion));
            return null;
        }

        var downloadUri = ResolveDownloadUri(manifestUri, dto.DownloadUrl);
        if (downloadUri is null || !_navigationGuard.IsAllowed(downloadUri))
        {
            _logger.LogWarning("Update-Download-URL ist nicht erlaubt: {Url}", LogUrl.Sanitize(dto.DownloadUrl));
            return null;
        }

        var sha256 = (dto.Sha256 ?? string.Empty).Trim();
        if (sha256.Length != 64 || !sha256.All(char.IsAsciiHexDigit))
        {
            _logger.LogWarning("Update-Manifest ohne gültige SHA256-Prüfsumme");
            return null;
        }

        var notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim();
        return new AppUpdateInfo(remoteVersion, downloadUri, sha256, notes);
    }

    public async Task<string> DownloadAsync(
        AppUpdateInfo info,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (!_navigationGuard.IsAllowed(info.DownloadUri))
        {
            throw new InvalidOperationException("Die Download-Adresse ist nicht erlaubt.");
        }

        var folder = Path.Combine(Path.GetTempPath(), "BISClient");
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, "BISClientSetup.exe");
        var temp = target + ".partial";

        if (File.Exists(temp))
        {
            File.Delete(temp);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, info.DownloadUri);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        await using (var file = new FileStream(
            temp,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            var buffer = new byte[81920];
            long read = 0;
            int n;
            while ((n = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, n), cancellationToken);
                sha.AppendData(buffer.AsSpan(0, n));
                read += n;
                if (total > 0)
                {
                    progress?.Report(100d * read / total);
                }
            }

            await file.FlushAsync(cancellationToken);
        }

        var actual = Convert.ToHexString(sha.GetHashAndReset());
        if (!string.Equals(actual, info.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(temp);
            throw new InvalidOperationException("Die Prüfsumme der Setup-Datei stimmt nicht.");
        }

        if (File.Exists(target))
        {
            File.Delete(target);
        }

        File.Move(temp, target);
        progress?.Report(100);
        _logger.LogInformation("Client-Update heruntergeladen: {Path}", target);
        return target;
    }

    public bool TryLaunchInstaller(string setupPath)
    {
        if (!File.Exists(setupPath))
        {
            return false;
        }

        try
        {
            var start = new ProcessStartInfo
            {
                FileName = setupPath,
                Arguments = "/SILENT /NORESTART /CLOSEAPPLICATIONS",
                UseShellExecute = true
            };
            Process.Start(start);
            _logger.LogInformation("Client-Setup gestartet: {Path}", setupPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Client-Setup konnte nicht gestartet werden");
            return false;
        }
    }

    public void Dispose() => _httpClient.Dispose();

    private Uri? ResolveManifestUri()
    {
        var configured = (_settings.UpdateManifestUrl ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Uri.TryCreate(configured, UriKind.Absolute, out var absolute) ? absolute : null;
        }

        if (!Uri.TryCreate(_settings.BisUrl, UriKind.Absolute, out var baseUri))
        {
            return null;
        }

        return new Uri(baseUri, "client/version.json");
    }

    private static Uri? ResolveDownloadUri(Uri manifestUri, string? downloadUrl)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            return new Uri(manifestUri, "BISClientSetup.exe");
        }

        var trimmed = downloadUrl.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute))
        {
            return absolute;
        }

        return Uri.TryCreate(manifestUri, trimmed, out var relative) ? relative : null;
    }
}
