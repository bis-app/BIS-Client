using System.IO;
using BISClient.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

namespace BISClient.Services;

public interface IDownloadService
{
    void HandleDownloadStarting(CoreWebView2DownloadStartingEventArgs args, Action<string, string>? openPdfPreview);
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

    public void HandleDownloadStarting(
        CoreWebView2DownloadStartingEventArgs args,
        Action<string, string>? openPdfPreview)
    {
        var fileName = Path.GetFileName(args.ResultFilePath);
        var downloadUri = args.DownloadOperation.Uri;
        var source = LogUrl.Sanitize(downloadUri);

        if (IsPdfPreviewSource(downloadUri))
        {
            SaveToDownloadFolder(args, SuggestedSaveName(fileName, downloadUri), source);
            return;
        }

        if (IsPdf(args, fileName, downloadUri))
        {
            QueuePdfPreview(args, fileName, downloadUri, source, openPdfPreview);
            return;
        }

        if (!_settings.AllowDownloads)
        {
            args.Cancel = true;
            _logger.LogInformation("Download blockiert ({File}) von {Url}", fileName, source);
            return;
        }

        SaveToDownloadFolder(args, fileName, source);
    }

    private void QueuePdfPreview(
        CoreWebView2DownloadStartingEventArgs args,
        string fileName,
        string downloadUri,
        string source,
        Action<string, string>? openPdfPreview)
    {
        CleanupOldPreviews();
        Directory.CreateDirectory(_settings.PdfPreviewFolder);

        var previewName = MakeSafePdfFileName(fileName, downloadUri);
        var target = UniquePath(_settings.PdfPreviewFolder, previewName, urlSafe: true);
        args.ResultFilePath = target;
        args.Handled = true;

        var operation = args.DownloadOperation;
        var opened = 0;
        void OnStateChanged(object? sender, object e)
        {
            if (operation.State == CoreWebView2DownloadState.InProgress)
            {
                return;
            }

            operation.StateChanged -= OnStateChanged;
            if (Interlocked.Exchange(ref opened, 1) != 0)
            {
                return;
            }

            if (operation.State != CoreWebView2DownloadState.Completed)
            {
                _logger.LogWarning("PDF-Download unterbrochen ({File}) von {Url}", previewName, source);
                return;
            }

            var previewUrl = BuildPreviewUrl(target);
            _logger.LogInformation("PDF-Vorschau bereit ({File}) von {Url}", Path.GetFileName(target), source);
            openPdfPreview?.Invoke(previewUrl, downloadUri);
        }

        operation.StateChanged += OnStateChanged;
        OnStateChanged(operation, EventArgs.Empty);
        _logger.LogInformation("PDF wird für Vorschau geladen ({File}) von {Url}", previewName, source);
    }

    private void SaveToDownloadFolder(CoreWebView2DownloadStartingEventArgs args, string fileName, string source)
    {
        Directory.CreateDirectory(_settings.DownloadFolder);
        var target = UniquePath(
            _settings.DownloadFolder,
            string.IsNullOrWhiteSpace(fileName) ? "Download" : fileName,
            urlSafe: false);
        args.ResultFilePath = target;
        args.Handled = true;
        _logger.LogInformation("Download erlaubt: {File} von {Url}", Path.GetFileName(target), source);
    }

    private void CleanupOldPreviews()
    {
        try
        {
            if (!Directory.Exists(_settings.PdfPreviewFolder))
            {
                return;
            }

            var cutoff = DateTime.UtcNow.AddDays(-1);
            foreach (var file in Directory.EnumerateFiles(_settings.PdfPreviewFolder))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "PDF-Vorschauordner konnte nicht bereinigt werden");
        }
    }

    private static bool IsPdf(CoreWebView2DownloadStartingEventArgs args, string fileName, string downloadUri)
    {
        var mime = args.DownloadOperation.MimeType;
        if (!string.IsNullOrWhiteSpace(mime)
            && mime.Contains("pdf", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return LooksLikePdfPath(downloadUri);
    }

    private static bool LooksLikePdfPath(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return uri.AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPdfPreviewSource(string? uri)
        => Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
           && parsed.IdnHost.Equals(NavigationGuard.PdfPreviewHost, StringComparison.OrdinalIgnoreCase);

    private static string SuggestedSaveName(string fileName, string downloadUri)
    {
        if (!string.IsNullOrWhiteSpace(fileName)
            && !fileName.Equals("download", StringComparison.OrdinalIgnoreCase))
        {
            return fileName;
        }

        if (Uri.TryCreate(downloadUri, UriKind.Absolute, out var parsed))
        {
            var fromUri = Uri.UnescapeDataString(Path.GetFileName(parsed.AbsolutePath));
            if (!string.IsNullOrWhiteSpace(fromUri))
            {
                return fromUri;
            }
        }

        return "Dokument.pdf";
    }

    private static string BuildPreviewUrl(string filePath)
    {
        var name = Path.GetFileName(filePath);
        return $"https://{NavigationGuard.PdfPreviewHost}/{Uri.EscapeDataString(name)}";
    }

    private static string MakeSafePdfFileName(string? fileName, string? downloadUri)
    {
        var name = Path.GetFileName(fileName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name)
            && Uri.TryCreate(downloadUri, UriKind.Absolute, out var uri)
            && (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            name = Path.GetFileName(uri.AbsolutePath);
        }

        if (string.IsNullOrWhiteSpace(name) || name.Equals("download", StringComparison.OrdinalIgnoreCase))
        {
            name = "Dokument.pdf";
        }

        var builder = new System.Text.StringBuilder(name.Length);
        foreach (var c in name)
        {
            builder.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_');
        }

        name = builder.ToString().Trim('_');
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "Dokument.pdf";
        }

        if (!name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            name += ".pdf";
        }

        return name;
    }

    private static string UniquePath(string folder, string fileName, bool urlSafe)
    {
        var path = Path.Combine(folder, fileName);
        if (!File.Exists(path))
        {
            return path;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var i = 2; i < 1000; i++)
        {
            var suffix = urlSafe ? $"-{i}" : $" ({i})";
            var candidate = Path.Combine(folder, $"{stem}{suffix}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(folder, $"{stem}-{Guid.NewGuid():N}{extension}");
    }
}
