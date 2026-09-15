using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace BISClient.Services;

internal sealed class ShownNotificationStore
{
    private const int MaxIds = 500;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _path;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private readonly List<int> _ids = [];
    private readonly HashSet<int> _set = [];

    public ShownNotificationStore(string path, ILogger logger)
    {
        _path = path;
        _logger = logger;
        Load();
    }

    public bool Contains(int id)
    {
        lock (_gate)
        {
            return _set.Contains(id);
        }
    }

    public void Add(IEnumerable<int> ids)
    {
        lock (_gate)
        {
            var changed = false;
            foreach (var id in ids)
            {
                if (!_set.Add(id))
                {
                    continue;
                }

                _ids.Add(id);
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            while (_ids.Count > MaxIds)
            {
                var removed = _ids[0];
                _ids.RemoveAt(0);
                _set.Remove(removed);
            }

            Save();
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return;
            }

            var payload = JsonSerializer.Deserialize<StorePayload>(File.ReadAllText(_path), JsonOptions);
            if (payload?.Ids is null)
            {
                return;
            }

            foreach (var id in payload.Ids)
            {
                if (_set.Add(id))
                {
                    _ids.Add(id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gespeicherte Benachrichtigungs-IDs konnten nicht gelesen werden");
        }
    }

    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_path, JsonSerializer.Serialize(new StorePayload { Ids = _ids }, JsonOptions));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gespeicherte Benachrichtigungs-IDs konnten nicht geschrieben werden");
        }
    }

    private sealed class StorePayload
    {
        public List<int> Ids { get; set; } = [];
    }
}
