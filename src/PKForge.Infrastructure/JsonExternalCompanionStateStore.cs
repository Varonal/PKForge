using System.Text.Json;
using PKForge.Domain;

namespace PKForge.Infrastructure;

/// <summary>
/// <see cref="IExternalCompanionStateStore"/> as one small JSON file, written atomically
/// (temp file + move — the same pattern as <see cref="JsonConsoleEmulatorStore"/>). A null
/// path keeps the state in memory. The file holds either one state object or null.
/// </summary>
public sealed class JsonExternalCompanionStateStore(string? path) : IExternalCompanionStateStore
{
    private readonly string? _path = path;
    private readonly Lock _gate = new();
    private ExternalCompanionState? _current = Load(path);

    public ExternalCompanionState? Current
    {
        get { lock (_gate) return _current; }
    }

    public void Set(ExternalCompanionState? state)
    {
        lock (_gate)
        {
            _current = state;
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
            File.WriteAllText(temp, JsonSerializer.Serialize(_current));
            File.Move(temp, path, overwrite: true);
        }
        catch (IOException) { } // the companion is a convenience; never fail a launch over it
        catch (UnauthorizedAccessException) { }
    }

    private static ExternalCompanionState? Load(string? path)
    {
        if (path is null || !File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<ExternalCompanionState>(File.ReadAllText(path));
        }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
    }
}
