using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using BISClient.Configuration;

namespace BISClient.Services;

public interface IWebViewEnvironmentService
{
    Task<CoreWebView2Environment> GetEnvironmentAsync(CancellationToken cancellationToken = default);
}

public sealed class WebViewEnvironmentService : IWebViewEnvironmentService
{
    private readonly AppSettings _settings;
    private readonly ILogger<WebViewEnvironmentService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CoreWebView2Environment? _environment;

    public WebViewEnvironmentService(AppSettings settings, ILogger<WebViewEnvironmentService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public async Task<CoreWebView2Environment> GetEnvironmentAsync(CancellationToken cancellationToken = default)
    {
        if (_environment is not null)
        {
            return _environment;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_environment is not null)
            {
                return _environment;
            }

            Directory.CreateDirectory(_settings.WebViewUserDataFolder);
            _logger.LogInformation(
                "Initialisiere WebView2-Umgebung. UserData={Folder}",
                _settings.WebViewUserDataFolder);

            var options = new CoreWebView2EnvironmentOptions
            {
                Language = "de-DE"
            };

            _environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: _settings.WebViewUserDataFolder,
                options: options);

            _logger.LogInformation(
                "WebView2-Umgebung bereit. Runtime={Version}",
                _environment.BrowserVersionString);
            return _environment;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WebView2-Umgebung konnte nicht initialisiert werden");
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }
}
