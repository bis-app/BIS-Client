using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using BISClient.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

namespace BISClient.Services;

public sealed class OutlookMailDraft
{
    public string? To { get; init; }

    public string? Subject { get; init; }

    public string? Body { get; init; }

    public string? AttachmentPath { get; init; }

    public string? PdfUrl { get; init; }

    public CoreWebView2? WebView { get; init; }
}

public interface IOutlookMailService
{
    Task<string?> ShowAsync(OutlookMailDraft draft, CancellationToken cancellationToken = default);
}

public sealed class OutlookMailService : IOutlookMailService, IDisposable
{
    private const int MaxPdfBytes = 32 * 1024 * 1024;
    private const int MaxRedirects = 5;

    private readonly AppSettings _settings;
    private readonly INavigationGuard _navigationGuard;
    private readonly IDownloadService _downloads;
    private readonly ILogger<OutlookMailService> _logger;
    private readonly HttpClient _httpClient;

    public OutlookMailService(
        AppSettings settings,
        INavigationGuard navigationGuard,
        IDownloadService downloads,
        ILogger<OutlookMailService> logger)
    {
        _settings = settings;
        _navigationGuard = navigationGuard;
        _downloads = downloads;
        _logger = logger;
        _httpClient = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false
        })
        {
            Timeout = TimeSpan.FromSeconds(60)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(ClientVersion.UserAgent);
    }

    public async Task<string?> ShowAsync(OutlookMailDraft draft, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveAttachmentAsync(draft, cancellationToken);
        if (resolved.Error is not null)
        {
            return resolved.Error;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            return await dispatcher.InvokeAsync(() => ShowOnUi(draft, resolved.Path), DispatcherPriority.Normal);
        }

        return ShowOnUi(draft, resolved.Path);
    }

    public void Dispose() => _httpClient.Dispose();

    private async Task<(string? Path, string? Error)> ResolveAttachmentAsync(
        OutlookMailDraft draft,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(draft.PdfUrl))
        {
            return await DownloadPdfAsync(draft.PdfUrl.Trim(), draft.WebView, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(draft.AttachmentPath))
        {
            return (null, "Es ist keine PDF zum Anhängen vorhanden.");
        }

        if (!IsInsidePreviewFolder(draft.AttachmentPath))
        {
            return (null, "Die PDF-Datei wurde nicht gefunden.");
        }

        return (Path.GetFullPath(draft.AttachmentPath), null);
    }

    private async Task<(string? Path, string? Error)> DownloadPdfAsync(
        string pdfUrl,
        CoreWebView2? webView,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(pdfUrl, UriKind.Absolute, out var uri))
        {
            return (null, "Die PDF-Adresse ist nicht erlaubt.");
        }

        if (IsPreviewHost(uri))
        {
            return _downloads.TryGetPreviewFile(pdfUrl, out var local)
                ? (local, null)
                : (null, "Die PDF-Datei wurde nicht gefunden.");
        }

        if (!IsFetchable(uri))
        {
            return (null, "Die PDF-Adresse ist nicht erlaubt.");
        }

        var current = uri;
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            var cookies = await ReadCookieHeaderAsync(webView, current.AbsoluteUri, cancellationToken);
            if (!string.IsNullOrWhiteSpace(cookies))
            {
                request.Headers.TryAddWithoutValidation("Cookie", cookies);
            }

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (IsRedirect(response.StatusCode))
            {
                var location = response.Headers.Location;
                if (location is null)
                {
                    return (null, "Die PDF konnte nicht geladen werden.");
                }

                if (!location.IsAbsoluteUri)
                {
                    location = new Uri(current, location);
                }

                if (!IsFetchable(location) || hop == MaxRedirects)
                {
                    return (null, "Die PDF-Adresse ist nicht erlaubt.");
                }

                current = location;
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "PDF-Download für Outlook fehlgeschlagen: {Status} {Url}",
                    (int)response.StatusCode,
                    LogUrl.Sanitize(current.AbsoluteUri));
                return (null, "Die PDF konnte nicht geladen werden.");
            }

            var length = response.Content.Headers.ContentLength;
            if (length is > MaxPdfBytes)
            {
                return (null, "Die PDF ist zu groß (maximal 32 MB).");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new MemoryStream();
            var chunk = new byte[81920];
            var total = 0;
            while (true)
            {
                var read = await stream.ReadAsync(chunk, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                total += read;
                if (total > MaxPdfBytes)
                {
                    return (null, "Die PDF ist zu groß (maximal 32 MB).");
                }

                buffer.Write(chunk, 0, read);
            }

            var bytes = buffer.ToArray();
            if (!LooksLikePdf(bytes))
            {
                return (null, "Die Adresse liefert keine PDF.");
            }

            var suggested = response.Content.Headers.ContentDisposition?.FileNameStar
                            ?? response.Content.Headers.ContentDisposition?.FileName;
            var path = AllocatePreviewPath(suggested, current);
            Directory.CreateDirectory(_settings.PdfPreviewFolder);
            await File.WriteAllBytesAsync(path, bytes, cancellationToken);
            _logger.LogInformation("PDF für Outlook geladen: {File}", Path.GetFileName(path));
            return (path, null);
        }

        return (null, "Die PDF konnte nicht geladen werden.");
    }

    private string? ShowOnUi(OutlookMailDraft draft, string? attachmentPath)
    {
        object? outlook = null;
        try
        {
            var type = Type.GetTypeFromProgID("Outlook.Application");
            if (type is null)
            {
                return "Klassisches Outlook wurde nicht gefunden. Das neue Outlook unterstützt diesen Versand nicht.";
            }

            outlook = Activator.CreateInstance(type);
            if (outlook is null)
            {
                return "Klassisches Outlook wurde nicht gefunden. Das neue Outlook unterstützt diesen Versand nicht.";
            }

            dynamic item = ((dynamic)outlook).CreateItem(0);
            var to = TrimTo(draft.To, 2000);
            var subject = TrimTo(draft.Subject, 255);
            var body = TrimTo(draft.Body, 32000);
            if (!string.IsNullOrEmpty(to))
            {
                item.To = to;
            }

            if (!string.IsNullOrEmpty(subject))
            {
                item.Subject = subject;
            }

            if (!string.IsNullOrEmpty(body))
            {
                item.Body = body;
            }

            if (!string.IsNullOrWhiteSpace(attachmentPath))
            {
                item.Attachments.Add(attachmentPath);
            }

            item.Display(false);
            GC.KeepAlive(outlook);
            _logger.LogInformation("Outlook-Nachricht geöffnet");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Outlook-Nachricht konnte nicht geöffnet werden");
            return "Die Outlook-Nachricht konnte nicht geöffnet werden.";
        }
    }

    private string AllocatePreviewPath(string? suggested, Uri source)
    {
        var name = (suggested ?? string.Empty).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(name))
        {
            name = Path.GetFileName(source.AbsolutePath);
        }

        name = Path.GetFileName(name);
        var builder = new System.Text.StringBuilder(name.Length);
        foreach (var c in name)
        {
            builder.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_');
        }

        name = builder.ToString().Trim('_');
        if (string.IsNullOrWhiteSpace(name) || name.Equals("download", StringComparison.OrdinalIgnoreCase))
        {
            name = "Dokument.pdf";
        }

        if (!name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            name += ".pdf";
        }

        var folder = _settings.PdfPreviewFolder;
        var path = Path.Combine(folder, name);
        if (!File.Exists(path))
        {
            return path;
        }

        var stem = Path.GetFileNameWithoutExtension(name);
        for (var i = 2; i < 1000; i++)
        {
            var candidate = Path.Combine(folder, $"{stem}-{i}.pdf");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(folder, $"{stem}-{Guid.NewGuid():N}.pdf");
    }

    private bool IsInsidePreviewFolder(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var folder = Path.GetFullPath(_settings.PdfPreviewFolder)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(path);
        return full.StartsWith(folder, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsFetchable(Uri uri)
        => (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
           && _navigationGuard.IsAllowed(uri)
           && !IsPreviewHost(uri);

    private static bool IsPreviewHost(Uri uri)
        => uri.IdnHost.Equals(NavigationGuard.PdfPreviewHost, StringComparison.OrdinalIgnoreCase);

    private static bool IsRedirect(System.Net.HttpStatusCode status)
        => status is System.Net.HttpStatusCode.MovedPermanently
            or System.Net.HttpStatusCode.Redirect
            or System.Net.HttpStatusCode.RedirectMethod
            or System.Net.HttpStatusCode.TemporaryRedirect
            or System.Net.HttpStatusCode.PermanentRedirect;

    private static bool LooksLikePdf(byte[] bytes)
    {
        if (bytes.Length < 5)
        {
            return false;
        }

        var limit = Math.Min(bytes.Length, 1024);
        for (var i = 0; i <= limit - 5; i++)
        {
            if (bytes[i] == (byte)'%'
                && bytes[i + 1] == (byte)'P'
                && bytes[i + 2] == (byte)'D'
                && bytes[i + 3] == (byte)'F'
                && bytes[i + 4] == (byte)'-')
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<string?> ReadCookieHeaderAsync(
        CoreWebView2? webView,
        string url,
        CancellationToken cancellationToken)
    {
        if (webView is null)
        {
            return null;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            return await dispatcher.InvokeAsync(
                () => ReadCookieHeaderCoreAsync(webView, url),
                DispatcherPriority.Normal).Task.Unwrap();
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await ReadCookieHeaderCoreAsync(webView, url);
    }

    private static async Task<string?> ReadCookieHeaderCoreAsync(CoreWebView2 webView, string url)
    {
        var cookies = await webView.CookieManager.GetCookiesAsync(url);
        if (cookies is null || cookies.Count == 0)
        {
            return null;
        }

        return string.Join("; ", cookies.Select(static cookie => $"{cookie.Name}={cookie.Value}"));
    }

    private static string TrimTo(string? value, int max)
    {
        var text = value?.Trim() ?? string.Empty;
        return text.Length <= max ? text : text[..max];
    }
}
