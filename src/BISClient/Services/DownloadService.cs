using System.IO;
using BISClient.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

namespace BISClient.Services;

public interface IDownloadService
{
    void HandleDownloadStarting(CoreWebView2DownloadStartingEventArgs args);
}

public sealed class DownloadService : IDownloadService
{
    private readonly AppSettings _settings;
    private readonly ILogger<DownloadService> _logger;

    public DownloadService(AppSettings settings, ILogger<DownloadService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public void HandleDownloadStarting(CoreWebView2DownloadStartingEventArgs args)
    {
        var fileName = Path.GetFileName(args.ResultFilePath);
        var source = LogUrl.Sanitize(args.DownloadOperation.Uri);

        if (!_settings.AllowDownloads)
        {
            args.Cancel = true;
            _logger.LogInformation("Download blockiert ({File}) von {Url}", fileName, source);
            return;
        }

        Directory.CreateDirectory(_settings.DownloadFolder);
        var target = Path.Combine(_settings.DownloadFolder, fileName);
        args.ResultFilePath = target;
        args.Handled = true;
        _logger.LogInformation("Download erlaubt: {File}", fileName);
    }
}
