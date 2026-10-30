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
    /// <summary>
    /// The emulators that can run this ROM, best first, inferred from the generation and the
    /// file extension. The player's linked choice always wins over this preference order.
    /// </summary>
    public static IReadOnlyList<EmulatorKind> EmulatorsForRom(int generation, string romFileName)
    {
        if (!GenerationKnows(generation))
            return [];
        var extension = ExtensionOf(romFileName);
        if (generation <= 2 && extension is not (".gb" or ".gbc" or ".zip" or ".7z"))
            return [];
        if (generation == 3 && extension is not (".gba" or ".zip" or ".7z"))
            return [];
        if (generation is 4 or 5 && extension is not (".nds" or ".dsi" or ".zip" or ".7z"))
            return [];
        return generation switch
        {
            1 or 2 or 3 => [EmulatorKind.Embedded],
            4 or 5 => [EmulatorKind.MelonDS, EmulatorKind.RetroArch, EmulatorKind.DraStic],
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
