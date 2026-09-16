using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace BISClient.Services;

public interface IExternalBrowserLauncher
{
    bool TryOpen(Uri uri);
}

public sealed class ExternalBrowserLauncher : IExternalBrowserLauncher
{
    private readonly ILogger<ExternalBrowserLauncher> _logger;

    public ExternalBrowserLauncher(ILogger<ExternalBrowserLauncher> logger)
    {
        _logger = logger;
    }

    public static bool IsHttp(Uri uri)
        => uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
           || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    public bool TryOpen(Uri uri)
    {
        if (!IsHttp(uri))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Standardbrowser konnte nicht geöffnet werden: {Url}", LogUrl.Sanitize(uri.AbsoluteUri));
            return false;
        }
    }
}
