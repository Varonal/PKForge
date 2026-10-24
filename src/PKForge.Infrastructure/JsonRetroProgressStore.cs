using System.Text.Json;
using PKForge.Domain;

namespace PKForge.Infrastructure;

/// <summary>
/// <see cref="IRetroProgressStore"/> as one small JSON file, written atomically (temp file +
/// replace). The last fetched achievement sets survive offline; a null path stays in memory.
/// </summary>
public sealed class JsonRetroProgressStore(string? path) : IRetroProgressStore
{
    private readonly string? _path = path;
    private readonly Dictionary<int, RetroGameProgress> _entries = Load(path);
    private readonly Lock _gate = new();

    public IReadOnlyList<RetroGameProgress> GetAll()
    {
        lock (_gate) return [.. _entries.Values];
    }

    public RetroGameProgress? Find(int gameId)
    {
        lock (_gate) return _entries.GetValueOrDefault(gameId);
    }

    public void Store(RetroGameProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (progress.GameId <= 0) return;
        lock (_gate)
        {
            _entries[progress.GameId] = progress;
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
        catch (IOException) { } // cached progress is a convenience, never a failure
        catch (UnauthorizedAccessException) { }
    }

    private static Dictionary<int, RetroGameProgress> Load(string? path)
    {
        var entries = new Dictionary<int, RetroGameProgress>();
        if (path is null || !File.Exists(path)) return entries;
        try
        {
            foreach (var progress in JsonSerializer.Deserialize<List<RetroGameProgress>>(File.ReadAllText(path)) ?? [])
                if (progress.GameId > 0) entries[progress.GameId] = progress;
        }
        catch (JsonException) { }
        catch (IOException) { }
        return entries;
    }
}
