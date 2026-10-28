using System.Text.Json;
using System.Windows;
using BISClient.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

namespace BISClient.Services.Native;

public interface INativeMessageBroker
{
    Task HandleRawMessageAsync(
        string json,
        NativeMessageContext? context = null,
        CancellationToken cancellationToken = default);
}

public sealed class NativeMessageContext
{
    public string? ActivePdfPath { get; init; }

    public CoreWebView2? WebView { get; init; }
}

public sealed class NativeMessageBroker : INativeMessageBroker
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IPrinterService _printerService;
    private readonly IScannerService _scannerService;
    private readonly IOutlookMailService _outlookMail;
    private readonly ILogger<NativeMessageBroker> _logger;

    public NativeMessageBroker(
        IPrinterService printerService,
        IScannerService scannerService,
        IOutlookMailService outlookMail,
        ILogger<NativeMessageBroker> logger)
    {
        _printerService = printerService;
        _scannerService = scannerService;
        _outlookMail = outlookMail;
        _logger = logger;
    }

    public async Task HandleRawMessageAsync(
        string json,
        NativeMessageContext? context = null,
        CancellationToken cancellationToken = default)
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
            case "outlookmail":
                await ShowOutlookMailAsync(message, context, cancellationToken);
                break;
            default:
                _logger.LogInformation("Kein Handler fuer Native-Nachricht {Type}", type);
                break;
        }
    }

    private async Task ShowOutlookMailAsync(
        NativeMessage message,
        NativeMessageContext? context,
        CancellationToken cancellationToken)
    {
        var error = await _outlookMail.ShowAsync(
            new OutlookMailDraft
            {
                To = message.To,
                Subject = message.Subject,
                Body = message.Body,
                PdfUrl = message.PdfUrl,
                AttachmentPath = string.IsNullOrWhiteSpace(message.PdfUrl) ? context?.ActivePdfPath : null,
                WebView = context?.WebView
            },
            cancellationToken);

        if (error is null)
        {
            return;
        }

        _logger.LogWarning("Outlook-Mail konnte nicht geöffnet werden: {Error}", error);
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        await dispatcher.InvokeAsync(() =>
            MessageBox.Show(
                Application.Current?.MainWindow,
                error,
                "BIS Client",
                MessageBoxButton.OK,
                MessageBoxImage.Warning));
    }
}
