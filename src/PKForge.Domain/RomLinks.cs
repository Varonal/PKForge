namespace PKForge.Domain;

/// <summary>
/// One ROM pinned to one save's game identity: the path the launcher hands to the
/// emulator, plus everything the shelf needs to say "Play" instead of "Open save".
/// </summary>
/// <param name="SaveDocumentId">The save this ROM belongs to (a stable SAF document id).</param>
/// <param name="RomUri">The ROM's SAF/document URI, as the platform picker produced it.</param>
/// <param name="RomFileName">The ROM file name ("Pokemon - Emerald Version.gba").</param>
/// <param name="Emulator">The emulator the game launches in (from <see cref="EmulatorKind"/>).</param>
/// <param name="Generation">The game's generation; decides the RetroArch core and save layout hints.</param>
/// <param name="GameLabel">The engine's game label at link time, for shelf subtitles.</param>
public sealed record RomLink(
    string SaveDocumentId,
    string RomUri,
    string RomFileName,
    EmulatorKind Emulator,
    int Generation,
    string GameLabel);

/// <summary>Stores the player's ROM links, one per save.</summary>
public interface IRomLinkStore
{
    IReadOnlyList<RomLink> GetAll();

    RomLink? FindBySave(string saveDocumentId);

    /// <summary>Points <paramref name="saveDocumentId"/> at a new ROM, replacing any previous link.</summary>
    void Set(RomLink link);

    /// <summary>Removes the ROM link of a save (the save itself is untouched).</summary>
    void Remove(string saveDocumentId);
}

/// <summary>Which emulator a linked ROM should launch with, and whether PKForge can hand it over directly.</summary>
public static class RomLinkRules
{
    /// <summary>Generations with an in-process PKForge core. A stale saved emulator
    /// selection must never send one of these Pokémon ROMs to another app.</summary>
    public static bool UsesIntegratedCore(int generation) => generation is >= 1 and <= 5;

    /// <summary>Whether this Pokémon ROM belongs to an integrated generation or carries
    /// a DS cartridge extension that must never be handed to an external emulator.</summary>
    public static bool MustRunInApp(int generation, string romFileName) =>
        UsesIntegratedCore(generation) || ExtensionOf(romFileName) is ".nds" or ".dsi";

    /// <summary>Uses the generation stored on a save link, then the recognized Pokémon
    /// edition in its ROM filename. Unknown editions remain unresolved.</summary>
    public static int ResolveIntegratedGeneration(int generation, string romFileName)
    {
        if (UsesIntegratedCore(generation)) return generation;
        var inferred = RomNameRules.GenerationOf(RomNameRules.GameName(romFileName));
        return UsesIntegratedCore(inferred) ? inferred : 0;
    }

    /// <summary>Formats that the currently integrated cores can load or safely materialize.</summary>
    public static bool IsSupportedEmbeddedFormat(int generation, string romFileName)
    {
        ArgumentNullException.ThrowIfNull(romFileName);
        var extension = ExtensionOf(romFileName);
        return generation switch
        {
            1 or 2 => extension is ".gb" or ".gbc",
            3 => extension == ".gba",
            4 or 5 => extension is ".nds" or ".zip",
            _ => false,
        };
    }

    /// <summary>
    /// The currently supported core for this ROM, inferred from generation, extension and
    /// the matching embedded library. Integrated Pokémon generations have no external fallback.
    /// </summary>
    public static IReadOnlyList<EmulatorKind> EmulatorsForRom(int generation, string romFileName)
    {
        if (!GenerationKnows(generation))
            return [];
        if (!IsSupportedEmbeddedFormat(generation, romFileName))
            return [];
        return generation switch
        {
            1 or 2 or 3 when EmulatorCatalog.EmbeddedCoreAvailable => [EmulatorKind.Embedded],
            1 or 2 or 3 => [],
            4 or 5 when EmulatorCatalog.EmbeddedDsCoreAvailable => [EmulatorKind.Embedded],
            4 or 5 => [],
            _ => [],
        };
    }

    /// <summary>True when the emulator accepts a direct ROM handoff (otherwise it opens its library).</summary>
    public static bool CanLaunchRom(EmulatorKind kind) => EmulatorCatalog.App(kind).LaunchStyle != RomLaunchStyle.OpenAppOnly;

    /// <summary>The link's subtitle, with a neutral emulator name the App can restyle.</summary>
    public static string Describe(RomLink link) =>
        $"{link.RomFileName} · {EmulatorCatalog.Name(link.Emulator)}";

    private static string ExtensionOf(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        return dot < 0 ? string.Empty : fileName[dot..].ToLowerInvariant();
    }

    private static bool GenerationKnows(int generation) => generation is >= 1 and <= 5;
}

/// <summary>Launches a linked ROM in its emulator, and reports which emulators are installed.</summary>
public interface IEmulatorLauncher
{
    /// <summary>True when the app answering <paramref name="packageId"/> is installed
    /// (the manifest's package queries make this visible on Android 11+).</summary>
    bool IsInstalled(string packageId);

    /// <summary>Hands the ROM to the emulator. False when the emulator could not be started at all.</summary>
    Task<bool> LaunchAsync(RomLink link, CancellationToken cancellationToken = default);

    /// <summary>Opens the emulator's own library screen, when no direct ROM handoff exists.</summary>
    Task<bool> OpenAppAsync(EmulatorKind kind, CancellationToken cancellationToken = default);
}
