namespace BISClient.Models;

public sealed class WindowBounds
{
    public double Left { get; set; }

    public double Top { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public string State { get; set; } = "Normal";
}
