using BISClient.Models;
using Microsoft.Extensions.Logging;

namespace BISClient.Services.Native;

public interface IPrinterService
{
    Task PrintAsync(PrintRequest request, CancellationToken cancellationToken = default);
}

public sealed class NullPrinterService : IPrinterService
{
    private readonly ILogger<NullPrinterService> _logger;

    public NullPrinterService(ILogger<NullPrinterService> logger)
    {
        _logger = logger;
    }

    public Task PrintAsync(PrintRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Druckauftrag empfangen, native Druckfunktion ist noch nicht implementiert. Printer={Printer}",
            string.IsNullOrWhiteSpace(request.Printer) ? "(nicht angegeben)" : request.Printer);
        return Task.CompletedTask;
    }
}
