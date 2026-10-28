using System.IO;
using System.Windows;
using BISClient.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace BISClient.Services;

public interface IDownloadService
{
    void HandleDownloadStarting(
        CoreWebView2DownloadStartingEventArgs args,
        Action<string, string>? openPdfPreview,
        string? currentSource);

    void HandleSaveAsUIShowing(CoreWebView2SaveAsUIShowingEventArgs args, string? currentSource);

    bool TryGetPreviewFile(string? uri, out string localPath);
}

public sealed class DownloadService : IDownloadService
{
    private readonly AppSettings _settings;
    private readonly ILogger<DownloadService> _logger;
    private int _promptingSave;
    private string? _lastHandledPreview;
    private long _lastHandledTicks;

    public DownloadService(AppSettings settings, ILogger<DownloadService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public void HandleDownloadStarting(
        CoreWebView2DownloadStartingEventArgs args,
        Action<string, string>? openPdfPreview,
        string? currentSource)
    {
        var fileName = Path.GetFileName(args.ResultFilePath);
        var downloadUri = args.DownloadOperation.Uri;
        var source = LogUrl.Sanitize(downloadUri);

        if (TrySaveExistingPreview(downloadUri, currentSource, fileName, source))
        {
            args.Cancel = true;
            args.Handled = true;
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

    public bool TryGetPreviewFile(string? uri, out string localPath)
        => TryResolvePreviewFile(uri, out localPath, out _);

    public void HandleSaveAsUIShowing(CoreWebView2SaveAsUIShowingEventArgs args, string? currentSource)
    {
        if (!TryResolvePreviewFile(currentSource, out var localPath, out var suggestedName))
        {
            return;
        }

        var deferral = args.GetDeferral();
        try
        {
            args.SuppressDefaultDialog = true;
            args.Cancel = true;
            PromptCopyPreview(localPath, suggestedName, LogUrl.Sanitize(currentSource));
        }
        finally
        {
            deferral.Complete();
        }
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

    private bool TrySaveExistingPreview(string downloadUri, string? currentSource, string fileName, string source)
    {
        if (!IsPdfPreviewSource(downloadUri) && !IsPdfPreviewBlob(downloadUri))
        {
            return false;
        }

        if (!TryResolvePreviewFile(downloadUri, out var localPath, out var suggestedName)
            && !TryResolvePreviewFile(currentSource, out localPath, out suggestedName))
        {
            _logger.LogWarning("PDF-Vorschau konnte nicht gespeichert werden, Datei nicht gefunden ({Url})", source);
            return true;
        }

        PromptCopyPreview(localPath, FirstNonEmpty(fileName, suggestedName), source);
        return true;
    }

    private void PromptCopyPreview(string localPath, string suggestedName, string source)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        if (!dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => PromptCopyPreview(localPath, suggestedName, source));
            return;
        }

        if (WasJustHandled(localPath) || Interlocked.Exchange(ref _promptingSave, 1) != 0)
        {
            return;
        }

        try
        {
            if (WasJustHandled(localPath))
            {
                return;
            }

            if (!File.Exists(localPath))
            {
                _logger.LogWarning("PDF-Vorschau fehlt ({File}) von {Url}", Path.GetFileName(localPath), source);
                return;
            }

            Directory.CreateDirectory(_settings.DownloadFolder);
            var dialog = new SaveFileDialog
            {
                Title = "PDF speichern",
                Filter = "PDF-Dokument|*.pdf|Alle Dateien|*.*",
                DefaultExt = ".pdf",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = MakeSuggestedSaveName(suggestedName, localPath),
                InitialDirectory = _settings.DownloadFolder
            };

            var owner = Application.Current?.MainWindow;
            var confirmed = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
            if (confirmed != true || string.IsNullOrWhiteSpace(dialog.FileName))
            {
                _logger.LogInformation("PDF-Speichern abgebrochen ({File})", Path.GetFileName(localPath));
                return;
            }

            File.Copy(localPath, dialog.FileName, overwrite: true);
            RememberHandled(localPath);
            _logger.LogInformation("PDF gespeichert ({File}) nach {Target}", Path.GetFileName(localPath), dialog.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PDF konnte nicht gespeichert werden ({File}) von {Url}", Path.GetFileName(localPath), source);
        }
        finally
        {
            Interlocked.Exchange(ref _promptingSave, 0);
        }
    }

    private bool WasJustHandled(string localPath)
        => string.Equals(_lastHandledPreview, localPath, StringComparison.OrdinalIgnoreCase)
           && DateTime.UtcNow.Ticks - _lastHandledTicks < TimeSpan.FromSeconds(2).Ticks;

    private void RememberHandled(string localPath)
    {
        _lastHandledPreview = localPath;
        _lastHandledTicks = DateTime.UtcNow.Ticks;
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

    private bool TryResolvePreviewFile(string? uri, out string localPath, out string suggestedName)
    {
        localPath = string.Empty;
        suggestedName = "Dokument.pdf";
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
            || !parsed.IdnHost.Equals(NavigationGuard.PdfPreviewHost, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fileName = Path.GetFileName(Uri.UnescapeDataString(parsed.AbsolutePath));
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var folder = Path.GetFullPath(_settings.PdfPreviewFolder);
        var candidate = Path.GetFullPath(Path.Combine(folder, fileName));
        var prefix = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                     + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(candidate))
        {
            return false;
        }

        localPath = candidate;
        suggestedName = fileName;
        return true;
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

    private static bool IsPdfPreviewBlob(string? uri)
        => Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
           && parsed.Scheme.Equals("blob", StringComparison.OrdinalIgnoreCase)
           && parsed.OriginalString.Contains(NavigationGuard.PdfPreviewHost, StringComparison.OrdinalIgnoreCase);

    private static string FirstNonEmpty(string first, string second)
        => string.IsNullOrWhiteSpace(first) || first.Equals("download", StringComparison.OrdinalIgnoreCase)
            ? second
            : first;

    private static string MakeSuggestedSaveName(string suggestedName, string localPath)
    {
        var name = Path.GetFileName(suggestedName);
        if (string.IsNullOrWhiteSpace(name) || name.Equals("download", StringComparison.OrdinalIgnoreCase))
        {
            name = Path.GetFileName(localPath);
        }

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
