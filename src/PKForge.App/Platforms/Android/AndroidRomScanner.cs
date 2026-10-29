using Android.OS;
using PKForge.Domain;

namespace PKForge.App;

/// <summary>
/// Scans the ES-DE ROM tree with direct filesystem access: /storage/emulated/0/ROMs by
/// default, the layout retrogamecorps/ES-DE-Directories documents (roms/gameboy,
/// roms/gameboyadvance, roms/nds, roms/switch, ...). Emulators on Android launch by plain
/// path, so the library stores paths, not SAF ids. Requires "All files access"
/// (MANAGE_EXTERNAL_STORAGE); without it the scan reports an empty root and the UI says so.
/// </summary>
public sealed class AndroidRomScanner : IRomScanner
{
    private static readonly string[] Roots =
    [
        "/storage/emulated/0/ROMs",
        "/storage/emulated/0/Roms",
        "/storage/emulated/0/roms",
        "/storage/emulated/0/EasyRoms",
    ];

    private string? _root;

    public string? RomRoot
    {
        get
        {
            if (_root is not null) return _root;
            foreach (var candidate in Roots)
            {
                try
                {
                    if (Directory.Exists(candidate))
                    {
                        _root = candidate;
                        return _root;
                    }
                }
                catch (Exception) { /* an unreadable root is the same as an absent one */ }
            }
            return null;
        }
    }

    public IReadOnlyList<LibraryRom> Scan(GameConsole console, CancellationToken cancellationToken = default)
    {
        var results = new List<LibraryRom>();
        var root = RomRoot;
        if (root is null) return results;
        var extensions = ConsoleRules.RomExtensions(console).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in ConsoleFolders(console, root))
        {
            string[] files;
            try
            {
                files = Directory.GetFiles(folder);
            }
            catch (Exception)
            {
                continue; // an unreadable folder simply holds no games for us
            }
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileName(file);
                if (!RomNameRules.LooksLikePokemon(name)) continue;
                var extension = Path.GetExtension(name);
                if (string.IsNullOrEmpty(extension) || !extensions.Contains(extension)) continue;
                DateTimeOffset? modified = null;
                try { modified = File.GetLastWriteTimeUtc(file); } catch (Exception) { }
                results.Add(new LibraryRom(file, name, console, Path.GetFileName(folder), RomNameRules.GameName(name), modified));
            }
        }
        return results;
    }

    /// <summary>Every folder that may hold this console's ROMs in the ES-DE layout.</summary>
    private static IEnumerable<string> ConsoleFolders(GameConsole console, string root)
    {
        // ES-DE's canonical folders per system; both the plain name and the roms/ nesting
        // are accepted, because setups differ between frontends.
        string[] names = console switch
        {
            GameConsole.GameBoy => ["gameboy", "gb"],
            GameConsole.GameBoyColor => ["gameboycolor", "gbc", "gameboy"],
            GameConsole.GameBoyAdvance => ["gameboyadvance", "gba"],
            GameConsole.NintendoDs => ["nds", "ds"],
            GameConsole.Nintendo3Ds => ["3ds", "n3ds", "3dscloud"],
            GameConsole.GameCube => ["gamecube", "gc"],
            GameConsole.NintendoSwitch => ["switch"],
            _ => [],
        };
        foreach (var name in names)
        {
            var nested = Path.Combine(root, name);
            if (Directory.Exists(nested)) yield return nested;
            var flat = Path.Combine(Path.GetDirectoryName(root) ?? root, name);
            if (Directory.Exists(flat) && !string.Equals(flat, nested, StringComparison.OrdinalIgnoreCase)) yield return flat;
        }
    }
}
