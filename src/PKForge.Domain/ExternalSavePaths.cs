using System.Text.RegularExpressions;

namespace PKForge.Domain;

/// <summary>
/// Resolves where an external emulator writes the save file for a launched ROM, so the
/// companion can watch it (US-002). Pure path algebra: <b>no I/O</b> — existence is
/// checked by the caller through an injected predicate, and "no save yet" simply yields
/// an empty list, so the companion degrades to its "no save yet" mode instead of crashing.
/// All paths are Android-style ('/' separators) regardless of the host running the tests.
/// </summary>
public static partial class ExternalSavePathResolver
{
    /// <summary>The shared storage root on Android handhelds.</summary>
    public const string StorageRoot = "/storage/emulated/0";

    /// <summary>
    /// All candidate save paths for (romPath, emulator), most likely first. Pure: never
    /// touches the disk. Empty for a null/empty romPath.
    /// </summary>
    public static IReadOnlyList<string> CandidatePaths(string? romPath, EmulatorKind emulator)
    {
        if (string.IsNullOrWhiteSpace(romPath)) return [];
        var convention = Conventions.TryGetValue(emulator, out var resolve) ? resolve : DefaultConvention;
        return convention(romPath);
    }

    /// <summary>
    /// Only the candidates that actually exist (the caller injects the file check, e.g.
    /// File.Exists on Android). Empty when none exists — the companion's "no save yet" mode.
    /// </summary>
    public static IReadOnlyList<string> ExistingPaths(string? romPath, EmulatorKind emulator, Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(fileExists);
        return CandidatePaths(romPath, emulator).Where(fileExists).ToList();
    }

    /// <summary>
    /// The per-emulator save-naming conventions, as romPath → ordered candidate paths.
    /// Extensible: add an entry to register a new emulator (keep the existing keys stable).
    /// </summary>
    public static IReadOnlyDictionary<EmulatorKind, Func<string, IReadOnlyList<string>>> Conventions { get; } =
        new Dictionary<EmulatorKind, Func<string, IReadOnlyList<string>>>
        {
            [EmulatorKind.RetroArch] = RetroArchPaths,
            [EmulatorKind.MelonDS] = MelonDsPaths,
            [EmulatorKind.Azahar] = AzaharPaths,
            [EmulatorKind.CitraMmj] = AzaharPaths, // same Citra-family NAND layout
            [EmulatorKind.Dolphin] = DolphinPaths,
        };

    // ── Conventions ──────────────────────────────────────────────────────────────

    /// <summary>RetroArch: .srm beside the ROM (saves-in-content setups), then its saves folders.</summary>
    private static IReadOnlyList<string> RetroArchPaths(string romPath)
    {
        var dir = DirOf(romPath);
        var stem = StemOf(romPath);
        var file = FileNameOf(romPath);
        return
        [
            Combine(dir, stem + ".srm"),
            Combine(dir, stem + ".sav"),
            Combine(dir, file + ".srm"),
            Combine(StorageRoot, "RetroArch/saves", stem + ".srm"),
            Combine(StorageRoot, "retroarch/saves", stem + ".srm"),
            Combine(StorageRoot, "RetroArch/saves", file + ".srm"),
            Combine(StorageRoot, "retroarch/saves", file + ".srm"),
        ];
    }

    /// <summary>melonDS: a .sav beside the ROM first, then the emulator's own saves folders.</summary>
    private static IReadOnlyList<string> MelonDsPaths(string romPath)
    {
        var dir = DirOf(romPath);
        var stem = StemOf(romPath);
        var file = FileNameOf(romPath);
        return
        [
            Combine(dir, stem + ".sav"),
            Combine(StorageRoot, "melonDS", file + ".sav"),
            Combine(StorageRoot, "melonds", file + ".sav"),
            Combine(StorageRoot, "Android/data/io.github.melonds/files/saves", file + ".sav"),
            Combine(StorageRoot, "Android/data/me.magnum.melonds/files/saves", file + ".sav"),
        ];
    }

    /// <summary>
    /// Azahar/Citra keep 3DS saves in the emulator's NAND, not beside the ROM; the title id
    /// in the ROM file name ("Pokemon X (0004000000055E00).3ds") is the only key we have,
    /// so those paths are best-effort candidates. The sibling .sav covers exported saves.
    /// </summary>
    private static IReadOnlyList<string> AzaharPaths(string romPath)
    {
        var dir = DirOf(romPath);
        var stem = StemOf(romPath);
        var paths = new List<string> { Combine(dir, stem + ".sav") };
        if (TitleIdOf(FileNameOf(romPath)) is { } tid)
        {
            foreach (var baseDir in new[] { "Azahar", "azahar", "Lime3DS", "lime3ds", "Citra", "citra-emu" })
                paths.Add(Combine(StorageRoot, baseDir, "nand/data", tid, "save/00000001"));
        }
        return paths;
    }

    /// <summary>Dolphin links individual GCI saves (never whole memory cards): beside the ROM, then GC folders.</summary>
    private static IReadOnlyList<string> DolphinPaths(string romPath)
    {
        var dir = DirOf(romPath);
        var stem = StemOf(romPath);
        var file = FileNameOf(romPath);
        return
        [
            Combine(dir, stem + ".gci"),
            Combine(StorageRoot, "dolphin-emu/GC", file + ".gci"),
            Combine(StorageRoot, "Dolphin/GC", file + ".gci"),
        ];
    }

    /// <summary>Default convention (DraStic, Linkboy, Pizza Boy, Eden...): common battery-save extensions beside the ROM.</summary>
    private static IReadOnlyList<string> DefaultConvention(string romPath)
    {
        var dir = DirOf(romPath);
        var stem = StemOf(romPath);
        return new[] { ".sav", ".srm", ".dsv", ".bin" }
            .Select(ext => Combine(dir, stem + ext))
            .Concat([Combine(dir, "main")])
            .ToList();
    }

    // ── Pure path helpers (Android-style separators on every host) ───────────────

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static string DirOf(string romPath)
    {
        var normalized = Normalize(romPath);
        var slash = normalized.LastIndexOf('/');
        return slash < 0 ? string.Empty : normalized[..slash];
    }

    private static string FileNameOf(string romPath)
    {
        var normalized = Normalize(romPath);
        var slash = normalized.LastIndexOf('/');
        return slash < 0 ? normalized : normalized[(slash + 1)..];
    }

    private static string StemOf(string romPath)
    {
        var file = FileNameOf(romPath);
        var dot = file.LastIndexOf('.');
        return dot > 0 ? file[..dot] : file;
    }

    private static string Combine(string dir, params string[] parts)
    {
        var path = parts.Aggregate(dir, (current, part) =>
            current.Length == 0 ? part : current.TrimEnd('/') + "/" + part.TrimStart('/'));
        return Normalize(path);
    }

    /// <summary>The 16-hex title id in parentheses in a ROM file name, lower-case; null when absent.</summary>
    private static string? TitleIdOf(string romFileName)
    {
        var match = TitleIdRegex().Match(romFileName);
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
    }

    [GeneratedRegex(@"\(([0-9A-Fa-f]{16})\)")]
    private static partial Regex TitleIdRegex();
}
