using BISClient.Configuration;
using Microsoft.Extensions.Logging;

namespace BISClient.Services;

public interface INavigationGuard
{
    bool IsAllowed(Uri uri);

    bool TryParseAndAllow(string? uriString, out Uri? uri);
}

public sealed class NavigationGuard : INavigationGuard
{
    public const string PdfPreviewHost = "bisclient-pdf.localhost";

    private readonly AppSettings _settings;
    private readonly ILogger<NavigationGuard> _logger;

    public NavigationGuard(AppSettings settings, ILogger<NavigationGuard> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public bool TryParseAndAllow(string? uriString, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(uriString))
        {
            return false;
        }

        if (!Uri.TryCreate(uriString, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        uri = parsed;
        return IsAllowed(parsed);
    }

    public bool IsAllowed(Uri uri)
    {
        if (uri.Scheme.Equals("about", StringComparison.OrdinalIgnoreCase))
        {
            return uri.AbsolutePath is "blank" or "srcdoc" || uri.OriginalString.StartsWith("about:blank", StringComparison.OrdinalIgnoreCase);
        }

        if (uri.Scheme.Equals("blob", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (uri.Scheme.Equals("data", StringComparison.OrdinalIgnoreCase))
        {
            return uri.OriginalString.StartsWith("data:application/pdf", StringComparison.OrdinalIgnoreCase);
        }

        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var host = uri.IdnHost.Trim().TrimEnd('.').ToLowerInvariant();
        if (host.Equals(PdfPreviewHost, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var pattern in _settings.AllowedHosts)
        {
            if (HostMatches(host, pattern))
            {
                return true;
            }
        }

        return false;
    }

    public void LogBlocked(string? uriString)
    {
        _logger.LogWarning("Navigation blockiert: {Url}", LogUrl.Sanitize(uriString));
    }

    private static bool HostMatches(string host, string pattern)
    {
        if (string.Equals(host, pattern, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (pattern.StartsWith("*.", StringComparison.Ordinal))
        {
            var suffix = pattern[1..];
            var bare = pattern[2..];
            return host.Equals(bare, StringComparison.OrdinalIgnoreCase)
                   || host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        if (pattern.EndsWith(".*", StringComparison.Ordinal))
        {
            var prefix = pattern[..^1];
            return host.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
