using System.Text.Json.Serialization;

namespace BISClient.Models;

public sealed class NativeMessage
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("printer")]
    public string? Printer { get; set; }

    [JsonPropertyName("document")]
    public string? Document { get; set; }

    [JsonPropertyName("action")]
    public string? Action { get; set; }

    [JsonPropertyName("to")]
    public string? To { get; set; }

    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    /// <summary>
    /// Optionale PDF-Adresse für type "outlookMail". Nur http(s) auf einem erlaubten BIS-Host.
    /// Ohne diese Angabe wird die PDF des Tabs verwendet, aus dem die Nachricht kam.
    /// </summary>
    [JsonPropertyName("pdfUrl")]
    public string? PdfUrl { get; set; }
}

public sealed class PrintRequest
{
    public string? Printer { get; init; }

    public string? Document { get; init; }
}

public sealed class ScannerRequest
{
    public string? Action { get; init; }
}
