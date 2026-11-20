using System.Text.Json;
using PKForge.Domain;

namespace PKForge.Infrastructure;

/// <summary>
/// <see cref="IPlaySessionStore"/> as one small JSON file, written atomically (temp + move —
/// the same pattern as <see cref="JsonConsoleEmulatorStore"/>), keyed by game key (ROM path).
/// A null path keeps the ledger in memory. Corrupt file reads as empty.
/// </summary>
public sealed class JsonPlaySessionStore(string? path) : IPlaySessionStore
{
    private readonly string? _path = path;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, PlaySessionStats> _entries = Load(path);

    public IReadOnlyList<PlaySessionStats> GetAll()
    {
        lock (_gate) return _entries.Values.ToList();
    }

    public PlaySessionStats? Find(string key)
    {
        lock (_gate) return _entries.TryGetValue(key, out var stats) ? stats : null;
    }

    public void Set(PlaySessionStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        lock (_gate)
        {
            _entries[stats.Key] = stats;
            Persist();
        }
    }

    private void Persist()
    {
        if (_path is not { } path) return;
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_entries.Values.ToList()));
            File.Move(temp, path, overwrite: true);
        }
        catch (IOException) { } // play-time is a convenience; never fail a launch over it
        catch (UnauthorizedAccessException) { }
    }

    private static Dictionary<string, PlaySessionStats> Load(string? path)
    {
        var entries = new Dictionary<string, PlaySessionStats>(StringComparer.Ordinal);
        if (path is null || !File.Exists(path)) return entries;
        try
        {
            foreach (var stats in JsonSerializer.Deserialize<List<PlaySessionStats>>(File.ReadAllText(path)) ?? [])
                entries[stats.Key] = stats;
        }
        catch (JsonException) { }
        catch (IOException) { }
        return entries;
    }
}
