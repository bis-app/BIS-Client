using System.Text.Json.Serialization;

namespace BISClient.Models;

internal sealed class UngelesenApiResponse
{
    public bool Success { get; set; }

    public List<BenachrichtigungApiItem> Benachrichtigungen { get; set; } = [];

    public int AnzahlUngelesen { get; set; }
}

internal sealed class BenachrichtigungApiItem
{
    public int ID { get; set; }

    public string? Titel { get; set; }

    public string? Nachricht { get; set; }

    [JsonPropertyName("ziel_url")]
    public string? ZielUrl { get; set; }
}
