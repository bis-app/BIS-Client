using System.IO;
using System.Text.Json;
using BISClient.Configuration;
using Microsoft.Extensions.Logging;

namespace BISClient.Services;

public interface ISingleInstanceService : IDisposable
{
    bool TryAcquire();

    void SignalExisting(string? url);

    void StartWatching(Action<string?> onActivate);
}

public sealed class SingleInstanceService : ISingleInstanceService
{
    public const string MutexName = @"Local\BIS.BISClient";
    public const string EventName = @"Local\BIS.BISClient.Activate";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly AppSettings _settings;
    private readonly ILogger<SingleInstanceService>? _logger;
    private Mutex? _mutex;
    private EventWaitHandle? _activateEvent;
    private RegisteredWaitHandle? _wait;
    private Action<string?>? _onActivate;
    private bool _disposed;

    public SingleInstanceService(AppSettings settings, ILogger<SingleInstanceService>? logger = null)
    {
        _settings = settings;
        _logger = logger;
    }

    public bool TryAcquire()
    {
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            _mutex.Dispose();
            _mutex = null;
            _activateEvent.Dispose();
            _activateEvent = null;
            return false;
        }

        return true;
    }

    public void SignalExisting(string? url)
    {
        try
        {
            WriteActivationRequest(url);
            using var evt = EventWaitHandle.OpenExisting(EventName);
            evt.Set();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Laufende Instanz konnte nicht aktiviert werden");
        }
    }

    public void StartWatching(Action<string?> onActivate)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _onActivate = onActivate;
        if (_activateEvent is null)
        {
            return;
        }

        _wait = ThreadPool.RegisterWaitForSingleObject(
            _activateEvent,
            OnActivateSignaled,
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _wait?.Unregister(_activateEvent);
        _wait = null;
        _activateEvent?.Dispose();
        _activateEvent = null;

        try
        {
            _mutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
        }

        _mutex?.Dispose();
        _mutex = null;
    }

    private void OnActivateSignaled(object? state, bool timedOut)
    {
        var url = ReadActivationRequest();
        try
        {
            _onActivate?.Invoke(url);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Aktivierung der vorhandenen Instanz ist fehlgeschlagen");
        }
    }

    private void WriteActivationRequest(string? url)
    {
        var payload = new ActivationRequest { Url = string.IsNullOrWhiteSpace(url) ? null : url };
        File.WriteAllText(_settings.ActivationRequestFile, JsonSerializer.Serialize(payload, JsonOptions));
    }

    private string? ReadActivationRequest()
    {
        try
        {
            if (!File.Exists(_settings.ActivationRequestFile))
            {
                return null;
            }

            var json = File.ReadAllText(_settings.ActivationRequestFile);
            try
            {
                File.Delete(_settings.ActivationRequestFile);
            }
            catch
            {
            }

            var payload = JsonSerializer.Deserialize<ActivationRequest>(json, JsonOptions);
            return string.IsNullOrWhiteSpace(payload?.Url) ? null : payload.Url;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Aktivierungsdatei konnte nicht gelesen werden");
            return null;
        }
    }

    private sealed class ActivationRequest
    {
        public string? Url { get; set; }
    }
}
