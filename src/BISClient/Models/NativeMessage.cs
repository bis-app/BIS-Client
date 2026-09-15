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
