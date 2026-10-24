using System.Text.Json;
using PKForge.Domain;

namespace PKForge.Infrastructure;

/// <summary>
/// <see cref="IRomLinkStore"/> as one small JSON file, written atomically (temp file +
/// replace) so a crash mid-write never loses every link. A null path keeps it in memory.
/// </summary>
public sealed class JsonRomLinkStore(string? path) : IRomLinkStore
{
    private readonly string? _path = path;
    private readonly Dictionary<string, RomLink> _entries = Load(path);
    private readonly Lock _gate = new();

    public IReadOnlyList<RomLink> GetAll()
    {
        lock (_gate) return [.. _entries.Values];
    }

    public RomLink? FindBySave(string saveDocumentId)
    {
        lock (_gate) return _entries.GetValueOrDefault(saveDocumentId);
    }

    public void Set(RomLink link)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentException.ThrowIfNullOrWhiteSpace(link.SaveDocumentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(link.RomUri);
        lock (_gate)
        {
            _entries[link.SaveDocumentId] = link;
            Persist();
        }
    }

    public void Remove(string saveDocumentId)
    {
        lock (_gate)
        {
            if (!_entries.Remove(saveDocumentId)) return;
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
        catch (IOException) { } // links are a convenience; never fail a launch over them
        catch (UnauthorizedAccessException) { }
    }

    private static Dictionary<string, RomLink> Load(string? path)
    {
        var entries = new Dictionary<string, RomLink>(StringComparer.Ordinal);
        if (path is null || !File.Exists(path)) return entries;
        try
        {
            foreach (var link in JsonSerializer.Deserialize<List<RomLink>>(File.ReadAllText(path)) ?? [])
                if (!string.IsNullOrWhiteSpace(link.SaveDocumentId) && !string.IsNullOrWhiteSpace(link.RomUri))
                    entries[link.SaveDocumentId] = link;
        }
        catch (JsonException) { }
        catch (IOException) { }
        return entries;
    }
}
