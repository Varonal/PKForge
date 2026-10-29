using PKForge.Domain;

namespace PKForge.App.Services;

/// <summary>
/// The standard save folders per emulator on Android handhelds (AYN Thor, Retroid,
/// Anbernic...), where frontends like ES-DE and Cocoon expect them. This answers the
/// "which folder?" question: none is asked — the tap scans these paths directly.
/// </summary>
public static class EmulatorSaveFolders
{
    private const string StorageRoot = "/storage/emulated/0";

    /// <summary>One emulator's candidate save folders, relative to the storage root.</summary>
    public static IReadOnlyList<string> For(EmulatorKind kind) => kind switch
    {
        EmulatorKind.RetroArch => ["RetroArch/saves", "retroarch/saves", "RetroArch/system"],
        EmulatorKind.MelonDS => ["melonDS", "melonds", "MelonDS", "Android/data/io.github.melonds/files/saves"],
        EmulatorKind.Azahar or EmulatorKind.CitraMmj =>
            ["Azahar", "azahar", "Lime3DS", "lime3ds", "Citra", "citra-emu", "citra-emu"],
        EmulatorKind.Eden => ["Eden", "eden", "Sudachi", "sudachi", "Yuzu", "yuzu", "Ryujinx", "ryujinx", "Skyline", "skyline"],
        EmulatorKind.Dolphin => ["dolphin-emu", "Dolphin"],
        EmulatorKind.DraStic => ["DraStic", "drastic", "Android/data/com.dsemu.drastic/files/backup"],
        EmulatorKind.Linkboy => ["Linkboy", "linkboy"],
        EmulatorKind.PizzaBoyGba => ["Pizza Boy GBA", "pizzaboy"],
        EmulatorKind.PizzaBoyGbc => ["Pizza Boy GBC", "pizzaboygbc"],
        _ => [],
    };

    /// <summary>The folders that exist on this device for one emulator.</summary>
    public static IReadOnlyList<string> ExistingFor(EmulatorKind kind) =>
        For(kind)
            .Select(folder => Path.Combine(StorageRoot, folder))
            .Where(AbsoluteExists)
            .ToList();

    /// <summary>The ES-DE ROM roots, plus the emulator-specific ROM folders.</summary>
    public static readonly IReadOnlyList<string> RomRoots =
        ["/storage/emulated/0/ROMs", "/storage/emulated/0/roms", "/storage/emulated/0/Roms"];

    /// <summary>True when at least one standard ROM root is readable.</summary>
    public static bool HasRomRoot() => RomRoots.Any(AbsoluteExists);

    private static bool AbsoluteExists(string path)
    {
        try
        {
            return Directory.Exists(path);
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>
/// Scans the emulators' standard save folders directly on the filesystem — no SAF
/// picker, no "which folder?" question. Requires all-files access on Android 11+;
/// without it, the scan reports nothing and the caller says so.
/// </summary>
public sealed class DirectSaveScanner(ISaveEngine engine)
{
    private static readonly HashSet<string> SaveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".sav", ".srm", ".dsv", ".bin", ".dat", ".main",
    };

    public sealed record FoundSave(string FullPath, string FileName, string GameLabel, int Generation);

    /// <summary>Scans every existing standard folder of one emulator for Pokémon saves.</summary>
    public IReadOnlyList<FoundSave> Scan(EmulatorKind kind, CancellationToken cancellationToken = default)
    {
        var found = new List<FoundSave>();
        foreach (var dir in EmulatorSaveFolders.ExistingFor(kind))
        {
            cancellationToken.ThrowIfCancellationRequested();
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories);
            }
            catch (Exception error) when (error is UnauthorizedAccessException or IOException)
            {
                continue;
            }
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileName = Path.GetFileName(file);
                var extension = Path.GetExtension(fileName);
                if (string.IsNullOrEmpty(extension) || !SaveExtensions.Contains(extension)) continue;
                var label = RomNameRules.GameName(fileName);
                if (string.IsNullOrWhiteSpace(label)) continue;
                found.Add(new FoundSave(file, fileName, $"Pokémon {label}", RomNameRules.GenerationOf(label)));
            }
        }
        return found;
    }

    /// <summary>Quick probe: the bytes parse as a Pokémon save.</summary>
    public bool LooksLikePokemonSave(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            return bytes.Length > 0 && engine.Validate(bytes);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
