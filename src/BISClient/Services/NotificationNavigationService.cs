using BISClient.Configuration;
using BISClient.ViewModels;
using Microsoft.Extensions.Logging;

namespace BISClient.Services;

public sealed class NotificationNavigationService
{
    private readonly AppSettings _settings;
    private readonly TabManager _tabs;
    private readonly INavigationGuard _navigationGuard;
    private readonly ILogger<NotificationNavigationService> _logger;

    public NotificationNavigationService(
        AppSettings settings,
        TabManager tabs,
        INavigationGuard navigationGuard,
        ILogger<NotificationNavigationService> logger)
    {
        _settings = settings;
        _tabs = tabs;
        _navigationGuard = navigationGuard;
        _logger = logger;
    }

    public void Open(string? zielUrl)
    {
        if (string.IsNullOrWhiteSpace(zielUrl))
        {
            return;
        }

        if (!TryResolveAllowedUrl(zielUrl, out var absolute) || absolute is null)
        {
            _logger.LogWarning("Toast-Ziel wurde blockiert: {Url}", LogUrl.Sanitize(zielUrl));
            return;
        }

        _tabs.NavigateTo(absolute);
    }

    public bool TryResolveAllowedUrl(string? zielUrl, out string? absolute)
    {
        absolute = null;
        if (string.IsNullOrWhiteSpace(zielUrl))
        {
            return false;
        }

        if (Uri.TryCreate(zielUrl, UriKind.Absolute, out var parsed))
        {
            if (!_navigationGuard.IsAllowed(parsed))
            {
                return false;
            }

            absolute = parsed.ToString();
            return true;
        }

        if (!Uri.TryCreate(_settings.BisUrl, UriKind.Absolute, out var baseUri))
        {
            return false;
        }

        if (!Uri.TryCreate(baseUri, zielUrl, out var combined) || !_navigationGuard.IsAllowed(combined))
        {
            return false;
        }

        absolute = combined.ToString();
        return true;
    }
}
