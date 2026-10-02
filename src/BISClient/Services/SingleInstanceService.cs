using System.IO;
using System.Text.Json;
using BISClient.Configuration;
using Microsoft.Extensions.Logging;

namespace BISClient.Services;

public interface ISingleInstanceService : IDisposable
{
    bool TryAcquire();

    void SignalExisting(InstanceActivationRequest request);

    void StartWatching(Action<InstanceActivationRequest> onActivate);
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
    private Action<InstanceActivationRequest>? _onActivate;
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

    public void SignalExisting(InstanceActivationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            WriteActivationRequest(request);
            using var evt = EventWaitHandle.OpenExisting(EventName);
            evt.Set();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Laufende Instanz konnte nicht aktiviert werden");
        }
    }

    public void StartWatching(Action<InstanceActivationRequest> onActivate)
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
        var request = ReadActivationRequest();
        try
        {
            _onActivate?.Invoke(request);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Aktivierung der vorhandenen Instanz ist fehlgeschlagen");
        }
    }

    private void WriteActivationRequest(InstanceActivationRequest request)
    {
        var payload = new InstanceActivationRequest
        {
            Url = string.IsNullOrWhiteSpace(request.Url) ? null : request.Url,
            Files = request.Files
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .ToList()
        };
        File.WriteAllText(_settings.ActivationRequestFile, JsonSerializer.Serialize(payload, JsonOptions));
    }

    private InstanceActivationRequest ReadActivationRequest()
    {
        try
        {
            if (!File.Exists(_settings.ActivationRequestFile))
            {
                return new InstanceActivationRequest();
            }

            var json = File.ReadAllText(_settings.ActivationRequestFile);
            try
            {
                File.Delete(_settings.ActivationRequestFile);
            }
            catch
            {
            }

            var payload = JsonSerializer.Deserialize<InstanceActivationRequest>(json, JsonOptions);
            if (payload is null)
            {
                return new InstanceActivationRequest();
            }

            payload.Url = string.IsNullOrWhiteSpace(payload.Url) ? null : payload.Url;
            payload.Files ??= [];
            payload.Files = payload.Files.Where(static path => !string.IsNullOrWhiteSpace(path)).ToList();
            return payload;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Aktivierungsdatei konnte nicht gelesen werden");
            return new InstanceActivationRequest();
        }
    }
}
