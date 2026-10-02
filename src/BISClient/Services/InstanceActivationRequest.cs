namespace BISClient.Services;

public sealed class InstanceActivationRequest
{
    public string? Url { get; set; }

    public List<string> Files { get; set; } = [];
}
