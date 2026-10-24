using System.Text.Json;
using PKForge.Domain;

namespace PKForge.Infrastructure;

/// <summary>
/// <see cref="IConsoleEmulatorStore"/> as one small JSON file, written atomically. A null
/// path keeps the bindings in memory. Keyed by console name: the enum values are persisted
/// as their <see cref="EmulatorKind"/> ints, stable across releases.
/// </summary>
public sealed class JsonConsoleEmulatorStore(string? path) : IConsoleEmulatorStore
{
    private readonly string? _path = path;
    private readonly Dictionary<GameConsole, EmulatorKind> _entries = Load(path);
    private readonly Lock _gate = new();

    public EmulatorKind? Find(GameConsole console)
    {
        lock (_gate) return _entries.GetValueOrDefault(console);
    }

    public void Set(GameConsole console, EmulatorKind? emulator)
    {
        lock (_gate)
        {
            if (emulator is null)
            {
                if (!_entries.Remove(console)) return;
            }
            else
            {
                _entries[console] = emulator.Value;
            }
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
            File.WriteAllText(temp, JsonSerializer.Serialize(
                _entries.Select(e => new ConsoleEmulatorLink(e.Key, e.Value)).ToList()));
            File.Move(temp, _path, overwrite: true);
        }
        catch (IOException) { } // a binding is a convenience; never fail a launch over it
        catch (UnauthorizedAccessException) { }
    }

    private static Dictionary<GameConsole, EmulatorKind> Load(string? path)
    {
        var entries = new Dictionary<GameConsole, EmulatorKind>();
        if (path is null || !File.Exists(path)) return entries;
        try
        {
            foreach (var link in JsonSerializer.Deserialize<List<ConsoleEmulatorLink>>(File.ReadAllText(path)) ?? [])
                entries[link.Console] = link.Emulator;
        }
        catch (JsonException) { }
        catch (IOException) { }
        return entries;
    }
}

/// <summary>
/// <see cref="IRomLibraryStore"/> as one small JSON file, written atomically. The whole
/// library is replaced per scan (the scan is the source of truth), so partial writes
/// never mix old and new results.
/// </summary>
public sealed class JsonRomLibraryStore(string? path) : IRomLibraryStore
{
    private readonly string? _path = path;
    private readonly Lock _gate = new();

    public IReadOnlyList<LibraryRom> GetAll()
    {
        lock (_gate) return Load(_path);
    }

    public void ReplaceAll(IReadOnlyList<LibraryRom> roms)
    {
        ArgumentNullException.ThrowIfNull(roms);
        lock (_gate)
        {
            try
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                var temp = _path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(roms.ToList()));
                File.Move(temp, _path, overwrite: true);
            }
            catch (IOException) { } // the library is rebuilt per scan; a lost write is recovered next scan
            catch (UnauthorizedAccessException) { }
        }
    }

    private static List<LibraryRom> Load(string? path)
    {
        if (path is null || !File.Exists(path)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<LibraryRom>>(File.ReadAllText(path)) ?? [];
        }
        catch (JsonException) { }
        catch (IOException) { }
        return [];
    }
}
