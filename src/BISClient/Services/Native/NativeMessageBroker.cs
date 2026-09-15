using System.Text.Json;
using BISClient.Models;
using Microsoft.Extensions.Logging;

namespace BISClient.Services.Native;

public interface INativeMessageBroker
{
    Task HandleRawMessageAsync(string json, CancellationToken cancellationToken = default);
}

public sealed class NativeMessageBroker : INativeMessageBroker
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IPrinterService _printerService;
    private readonly IScannerService _scannerService;
    private readonly ILogger<NativeMessageBroker> _logger;

    public NativeMessageBroker(
        IPrinterService printerService,
        IScannerService scannerService,
        ILogger<NativeMessageBroker> logger)
    {
        _printerService = printerService;
        _scannerService = scannerService;
        _logger = logger;
    }

    public async Task HandleRawMessageAsync(string json, CancellationToken cancellationToken = default)
    {
        NativeMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<NativeMessage>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Ungueltige Native-Nachricht empfangen");
            return;
        }

        if (message is null || string.IsNullOrWhiteSpace(message.Type))
        {
            _logger.LogWarning("Native-Nachricht ohne Typ ignoriert");
            return;
        }

        var type = message.Type.Trim();
        _logger.LogInformation("Native-Nachricht: {Type}", type);

        switch (type.ToLowerInvariant())
        {
            case "print":
                await _printerService.PrintAsync(
                    new PrintRequest { Printer = message.Printer, Document = message.Document },
                    cancellationToken);
                break;
            case "nativefunction":
                await _scannerService.HandleAsync(
                    new ScannerRequest { Action = message.Action },
                    cancellationToken);
                break;
            default:
                _logger.LogInformation("Kein Handler fuer Native-Nachricht {Type}", type);
                break;
        }
    }
}
