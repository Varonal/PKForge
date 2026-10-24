using System.Text.Json;
using PKForge.Domain;

namespace PKForge.Infrastructure;

/// <summary>
/// <see cref="ITrophyStore"/> as one small JSON file, written atomically (temp file +
/// replace). A null path keeps everything in memory (tests, design-time).
/// </summary>
public sealed class JsonTrophyStore(string? path) : ITrophyStore
{
    private readonly string? _path = path;
    private readonly Dictionary<string, TrophyState> _entries = Load(path);
    private readonly Lock _gate = new();

    public IReadOnlyList<TrophyState> GetAll()
    {
        lock (_gate) return [.. _entries.Values];
    }

    public TrophyState? Find(string trophyId)
    {
        lock (_gate) return _entries.GetValueOrDefault(trophyId);
    }

    public void Store(TrophyState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.TrophyId);
        lock (_gate)
        {
            // An unlock is permanent: a later scan with a lower value never relocks it.
            if (_entries.TryGetValue(state.TrophyId, out var previous) && previous.Unlocked)
                _entries[state.TrophyId] = previous with { Current = Math.Max(previous.Current, state.Current) };
            else
                _entries[state.TrophyId] = state;
            Persist();
        }
    }

    public void RemoveGame(string gameLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameLabel);
        var prefix = $"{gameLabel.ToLowerInvariant().Replace(" ", "-").Replace("é", "e").Replace(":", "").Replace("'", "")}-";
        lock (_gate)
        {
            var doomed = _entries.Keys.Where(id => id.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            if (doomed.Count == 0) return;
            foreach (var id in doomed) _entries.Remove(id);
            Persist();
        }
    }

    private void Persist()
    {
        if (_path is null) return;
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_entries.Values.ToList()));
            File.Move(temp, _path, overwrite: true);
        }
        catch (IOException) { } // trophy progress is a convenience, never a failure
        catch (UnauthorizedAccessException) { }
    }

    private static Dictionary<string, TrophyState> Load(string? path)
    {
        var entries = new Dictionary<string, TrophyState>(StringComparer.Ordinal);
        if (path is null || !File.Exists(path)) return entries;
        try
        {
            foreach (var state in JsonSerializer.Deserialize<List<TrophyState>>(File.ReadAllText(path)) ?? [])
                if (!string.IsNullOrWhiteSpace(state.TrophyId)) entries[state.TrophyId] = state;
        }
        catch (JsonException) { }
        catch (IOException) { }
        return entries;
    }
}
