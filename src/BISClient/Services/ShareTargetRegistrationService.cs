using System.IO;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Windows.Management.Deployment;

namespace BISClient.Services;

public interface IShareTargetRegistrationService
{
    Task SyncAsync(bool enabled, CancellationToken cancellationToken = default);
}

public sealed class ShareTargetRegistrationService : IShareTargetRegistrationService
{
    private readonly ILogger<ShareTargetRegistrationService> _logger;

    public ShareTargetRegistrationService(ILogger<ShareTargetRegistrationService> logger)
    {
        _logger = logger;
    }

    public async Task SyncAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        try
        {
            if (enabled)
            {
                await RegisterAsync(cancellationToken);
                return;
            }

            await UnregisterAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Share-Target-Registrierung konnte nicht aktualisiert werden");
        }
    }

    private async Task RegisterAsync(CancellationToken cancellationToken)
    {
        var msixPath = Path.Combine(AppContext.BaseDirectory, ExplorerUploadConstants.IdentityMsixFileName);
        if (!File.Exists(msixPath))
        {
            _logger.LogDebug("Identity-Paket fehlt, Share-Target wird nicht registriert: {Path}", msixPath);
            return;
        }

        TrustCertificate();

        var manager = new PackageManager();
        var options = new AddPackageOptions
        {
            ExternalLocationUri = ToDirectoryUri(AppContext.BaseDirectory),
            ForceUpdateFromAnyVersion = true
        };

        cancellationToken.ThrowIfCancellationRequested();
        var result = await manager.AddPackageByUriAsync(new Uri(msixPath), options);
        if (!result.IsRegistered)
        {
            await UnregisterAsync(cancellationToken);
            result = await manager.AddPackageByUriAsync(new Uri(msixPath), options);
        }

        if (!result.IsRegistered)
        {
            _logger.LogWarning(
                "Identity-Paket konnte nicht registriert werden: {Error}",
                string.IsNullOrWhiteSpace(result.ErrorText) ? result.ExtendedErrorCode?.Message : result.ErrorText);
            return;
        }

        _logger.LogInformation("Share-Target für {Package} registriert", ExplorerUploadConstants.IdentityPackageName);
    }

    private async Task UnregisterAsync(CancellationToken cancellationToken)
    {
        var manager = new PackageManager();
        var packages = manager.FindPackagesForUser(
                string.Empty,
                ExplorerUploadConstants.IdentityPackageName,
                ExplorerUploadConstants.IdentityPublisher)
            .ToList();
        if (packages.Count == 0)
        {
            return;
        }

        foreach (var package in packages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await manager.RemovePackageAsync(package.Id.FullName);
            if (!string.IsNullOrWhiteSpace(result.ErrorText))
            {
                _logger.LogWarning("Identity-Paket konnte nicht entfernt werden: {Error}", result.ErrorText);
                continue;
            }

            _logger.LogInformation("Share-Target {Package} entfernt", package.Id.FullName);
        }
    }

    private void TrustCertificate()
    {
        var cerPath = Path.Combine(AppContext.BaseDirectory, ExplorerUploadConstants.IdentityCerFileName);
        if (!File.Exists(cerPath))
        {
            return;
        }

        using var cert = X509CertificateLoader.LoadCertificateFromFile(cerPath);
        using var store = new X509Store(StoreName.TrustedPeople, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        var existing = store.Certificates.Find(X509FindType.FindByThumbprint, cert.Thumbprint, validOnly: false);
        if (existing.Count == 0)
        {
            store.Add(cert);
            _logger.LogInformation("Identity-Zertifikat in TrustedPeople aufgenommen");
        }
    }

    private static Uri ToDirectoryUri(string path)
    {
        var full = Path.GetFullPath(path);
        if (!full.EndsWith(Path.DirectorySeparatorChar) && !full.EndsWith(Path.AltDirectorySeparatorChar))
        {
            full += Path.DirectorySeparatorChar;
        }

        return new Uri(full);
    }
}
