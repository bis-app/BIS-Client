namespace BISClient.Models;

internal sealed class ImportUploadResponseDto
{
    public bool Success { get; set; }

    public string? Message { get; set; }

    public string? Error { get; set; }

    public string? Filename { get; set; }
}

public sealed class ImportUploadSummary
{
    public int Attempted { get; init; }

    public int Succeeded { get; init; }

    public int Failed { get; init; }

    public IReadOnlyList<string> Details { get; init; } = [];

    public string ToDisplayText()
    {
        if (Attempted == 0)
        {
            return "Es wurden keine Dateien ausgewählt.";
        }

        if (Failed == 0)
        {
            var text = Succeeded == 1
                ? "1 Datei wurde im persönlichen Import-Ordner gespeichert."
                : $"{Succeeded} Dateien wurden im persönlichen Import-Ordner gespeichert.";
            if (Details.Count == 0)
            {
                return text;
            }

            return text + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, Details);
        }

        var header = $"{Succeeded} von {Attempted} Dateien gespeichert.";
        if (Details.Count == 0)
        {
            return header;
        }

        return header + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, Details);
    }
}
