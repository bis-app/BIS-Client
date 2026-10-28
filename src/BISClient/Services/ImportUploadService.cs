using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using BISClient.Configuration;
using BISClient.Models;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic.FileIO;

namespace BISClient.Services;

public interface IImportUploadService
{
    IReadOnlyList<string> AllowedExtensions { get; }

    string FileDialogFilter { get; }

    Task<ImportUploadSummary> UploadAsync(IReadOnlyList<string> filePaths, CancellationToken cancellationToken);
}

public sealed class ImportUploadService : IImportUploadService, IDisposable
{
    public const long MaxFileSizeBytes = 32L * 1024 * 1024;

    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".txt", ".eml", ".msg"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AppSettings _settings;
    private readonly IUserSettingsStore _userSettings;
    private readonly ILogger<ImportUploadService> _logger;
    private readonly HttpClient _httpClient;

    public ImportUploadService(
        AppSettings settings,
        IUserSettingsStore userSettings,
        ILogger<ImportUploadService> logger)
    {
        _settings = settings;
        _userSettings = userSettings;
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
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public IReadOnlyList<string> AllowedExtensions { get; } = Allowed.ToArray();

    public string FileDialogFilter
        => "Erlaubte Dateien|*.png;*.jpg;*.jpeg;*.gif;*.pdf;*.doc;*.docx;*.xls;*.xlsx;*.txt;*.eml;*.msg";

    public async Task<ImportUploadSummary> UploadAsync(IReadOnlyList<string> filePaths, CancellationToken cancellationToken)
    {
        var details = new List<string>();
        var succeeded = 0;
        var failed = 0;
        var attempted = filePaths.Count;

        var token = _userSettings.Current.UploadToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            return new ImportUploadSummary
            {
                Attempted = attempted,
                Failed = attempted,
                Details = ["Kein Upload-Token hinterlegt. Bitte unter Einstellungen ein Token aus dem BIS-Profil eintragen."]
            };
        }

        var requestUri = BuildUploadUri();
        if (requestUri is null)
        {
            return new ImportUploadSummary
            {
                Attempted = attempted,
                Failed = attempted,
                Details = ["Die BIS-Adresse ist ungültig. Upload ist nicht möglich."]
            };
        }

        foreach (var path in filePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(path);
            try
            {
                var error = ValidateFile(path, name);
                if (error is not null)
                {
                    failed++;
                    details.Add($"{name}: {error}");
                    continue;
                }

                var result = await UploadOneAsync(requestUri, token, path, name, cancellationToken);
                if (result.Ok)
                {
                    succeeded++;
                    _logger.LogInformation("Import-Upload gespeichert: {File}", name);
                    if (_userSettings.Current.DeleteUploadedFiles)
                    {
                        TryMoveToRecycleBin(path, name, details);
                    }

                    continue;
                }

                failed++;
                details.Add($"{name}: {result.Message}");
                _logger.LogWarning("Import-Upload fehlgeschlagen: {File} {Status}", name, result.Status);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                details.Add($"{name}: Die Datei konnte nicht hochgeladen werden.");
                _logger.LogWarning(ex, "Import-Upload fehlgeschlagen: {File}", name);
            }
        }

        return new ImportUploadSummary
        {
            Attempted = attempted,
            Succeeded = succeeded,
            Failed = failed,
            Details = details
        };
    }

    public void Dispose() => _httpClient.Dispose();

    private void TryMoveToRecycleBin(string path, string name, ICollection<string> details)
    {
        try
        {
            FileSystem.DeleteFile(
                path,
                UIOption.OnlyErrorDialogs,
                RecycleOption.SendToRecycleBin,
                UICancelOption.ThrowException);
            _logger.LogInformation("Original nach Upload in den Papierkorb verschoben: {File}", name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Original konnte nicht in den Papierkorb verschoben werden: {File}", name);
            details.Add($"{name}: gespeichert, das Original konnte nicht in den Papierkorb verschoben werden.");
        }
    }

    private static string? ValidateFile(string path, string name)
    {
        if (!File.Exists(path))
        {
            return "Datei wurde nicht gefunden.";
        }

        var extension = Path.GetExtension(path);
        if (!Allowed.Contains(extension))
        {
            return "Dateityp nicht erlaubt. Erlaubt: doc, docx, eml, gif, jpeg, jpg, msg, pdf, png, txt, xls, xlsx";
        }

        var info = new FileInfo(path);
        if (info.Length > MaxFileSizeBytes)
        {
            return "Die Datei ist zu groß (maximal 32 MB).";
        }

        if (string.IsNullOrWhiteSpace(Path.GetExtension(name)))
        {
            return "Dateiendung fehlt.";
        }

        return null;
    }

    private async Task<(bool Ok, int Status, string Message)> UploadOneAsync(
        Uri requestUri,
        string token,
        string path,
        string fileName,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var content = new MultipartFormDataContent();
        await using var stream = File.OpenRead(path);
        using var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(GetMime(Path.GetExtension(path)));
        content.Add(fileContent, "file", fileName);
        content.Add(new StringContent(fileName), "filename");
        request.Content = content;

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        var dto = TryParse(payload);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return (false, (int)response.StatusCode, "Token ungültig oder fehlt. Bitte im BIS-Profil ein neues Token erzeugen.");
        }

        if (response.StatusCode == HttpStatusCode.RequestEntityTooLarge)
        {
            return (false, (int)response.StatusCode, dto?.Error ?? dto?.Message ?? "Die Datei ist zu groß (maximal 32 MB).");
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return (false, (int)response.StatusCode, "Zu viele Uploads. Bitte kurz warten und erneut versuchen.");
        }

        if (response.IsSuccessStatusCode && dto?.Success == true)
        {
            return (true, (int)response.StatusCode, dto.Message ?? "Gespeichert.");
        }

        var message = dto?.Message ?? dto?.Error;
        if (string.IsNullOrWhiteSpace(message))
        {
            message = response.IsSuccessStatusCode
                ? "Der Server hat den Upload nicht bestätigt."
                : $"Upload fehlgeschlagen (HTTP {(int)response.StatusCode}).";
        }

        return (false, (int)response.StatusCode, message);
    }

    private Uri? BuildUploadUri()
    {
        if (!Uri.TryCreate(_settings.BisUrl, UriKind.Absolute, out var baseUri))
        {
            return null;
        }

        return new Uri(baseUri, "api/import/shortcut");
    }

    private static ImportUploadResponseDto? TryParse(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ImportUploadResponseDto>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string GetMime(string extension)
        => extension.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".pdf" => "application/pdf",
            ".txt" => "text/plain",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".eml" => "message/rfc822",
            ".msg" => "application/vnd.ms-outlook",
            _ => "application/octet-stream"
        };
}
