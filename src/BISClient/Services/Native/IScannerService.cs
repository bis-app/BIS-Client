using BISClient.Models;
using Microsoft.Extensions.Logging;

namespace BISClient.Services.Native;

public interface IScannerService
{
    Task HandleAsync(ScannerRequest request, CancellationToken cancellationToken = default);
}

public sealed class NullScannerService : IScannerService
{
    private readonly ILogger<NullScannerService> _logger;

    public NullScannerService(ILogger<NullScannerService> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(ScannerRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Scanner-Nachricht empfangen, Hardware-Anbindung ist noch nicht implementiert. Action={Action}",
            string.IsNullOrWhiteSpace(request.Action) ? "(leer)" : request.Action);
        return Task.CompletedTask;
    }
}
