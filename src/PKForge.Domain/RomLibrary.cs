namespace PKForge.Domain;

/// <summary>One console generation of Pokémon games, as the library and launcher see it.</summary>
public enum GameConsole
{
    GameBoy = 0,
    GameBoyColor = 1,
    GameBoyAdvance = 2,
    NintendoDs = 3,
    Nintendo3Ds = 4,
    GameCube = 5,
    NintendoSwitch = 6,
}

/// <summary>
/// The player's emulator of choice per console (the Cocoon-style binding): which app
/// launches the console's ROMs, so a ROM never asks again once its console is bound.
/// </summary>
public sealed record ConsoleEmulatorLink(GameConsole Console, EmulatorKind Emulator);

/// <summary>Stores one emulator choice per console.</summary>
public interface IConsoleEmulatorStore
{
    EmulatorKind? Find(GameConsole console);

    /// <summary>Points <paramref name="console"/> at <paramref name="emulator"/>, or clears it when null.</summary>
    void Set(GameConsole console, EmulatorKind? emulator);
}

/// <summary>Which consoles a Pokémon generation runs on, for the library's folders.</summary>
public static class ConsoleRules
{
    /// <summary>The ES-DE folder name per console (retrogamecorps/ES-DE-Directories layout).</summary>
    public static string FolderName(GameConsole console) => console switch
    {
        GameConsole.GameBoy => "gameboy",
        GameConsole.GameBoyColor => "gameboyadvance/gbc", // ES-DE keeps GB and GBC apart under gameboy*
        GameConsole.NintendoDs => "nds",
        GameConsole.Nintendo3Ds => "3ds",
        GameConsole.GameCube => "gamecube",
        GameConsole.NintendoSwitch => "switch",
        _ => "gameboyadvance",
    };

    /// <summary>The consoles a generation's ROMs run on.</summary>
    public static IReadOnlyList<GameConsole> ForGeneration(int generation) => generation switch
    {
        1 => [GameConsole.GameBoy],
        2 => [GameConsole.GameBoyColor],
        3 => [GameConsole.GameBoyAdvance],
        4 or 5 => [GameConsole.NintendoDs],
        6 or 7 => [GameConsole.Nintendo3Ds],
        8 => [GameConsole.NintendoSwitch, GameConsole.GameCube],
        9 => [GameConsole.NintendoSwitch],
        _ => [],
    };

    /// <summary>The ROM file extensions a console's games come in.</summary>
    public static IReadOnlyList<string> RomExtensions(GameConsole console) => console switch
    {
        GameConsole.GameBoy => [".gb", ".zip", ".7z"],
        GameConsole.GameBoyColor => [".gbc", ".zip", ".7z"],
        GameConsole.GameBoyAdvance => [".gba", ".zip", ".7z"],
        GameConsole.NintendoDs => [".nds", ".dsi", ".zip", ".7z"],
        GameConsole.Nintendo3Ds => [".3ds", ".cci", ".cxi", ".zip", ".7z"],
        GameConsole.GameCube => [".iso", ".gcm", ".rvz"],
        GameConsole.NintendoSwitch => [".xci", ".nsp", ".zip", ".7z"],
        _ => [],
    };

    /// <summary>The emulators that can run a console's ROMs, best first — the Cocoon choice list.</summary>
    public static IReadOnlyList<EmulatorKind> EmulatorsFor(GameConsole console) => console switch
    {
        GameConsole.GameBoy or GameConsole.GameBoyColor =>
            [EmulatorKind.RetroArch, EmulatorKind.Linkboy, EmulatorKind.PizzaBoyGbc],
        GameConsole.GameBoyAdvance =>
            [EmulatorKind.RetroArch, EmulatorKind.Linkboy, EmulatorKind.PizzaBoyGba],
        GameConsole.NintendoDs =>
            [EmulatorKind.MelonDS, EmulatorKind.RetroArch, EmulatorKind.DraStic],
        GameConsole.Nintendo3Ds =>
            [EmulatorKind.Azahar, EmulatorKind.CitraMmj],
        GameConsole.GameCube => [EmulatorKind.Dolphin],
        GameConsole.NintendoSwitch => [EmulatorKind.Eden],
        _ => [],
    };

    /// <summary>True when one of the emulators that run this console is installed.</summary>
    public static bool AnyInstalled(GameConsole console, Func<string, bool> isPackageInstalled) =>
        EmulatorsFor(console).Any(kind => EmulatorCatalog.IsInstalled(kind, isPackageInstalled));
}

/// <summary>
/// A ROM discovered by the library scan: its path, the console folder it came from, and
/// the game label parsed out of the file name ("Pokemon - Emerald Version.gba" →
/// "Emerald"). Paths are plain file paths under /storage/emulated/0 — the direct-filesystem
/// read the launcher needs.
/// </summary>
public sealed record LibraryRom(
    string FilePath,
    string FileName,
    GameConsole Console,
    /// <summary>The ES-DE folder it was found in ("gameboy", "gameboyadvance", ...).</summary>
    string SystemFolder,
    /// <summary>The parsed short game name ("Emerald"), never the whole file name.</summary>
    string GameName,
    /// <summary>The file's last-modified time, so rescans can skip unchanged files.</summary>
    DateTimeOffset? LastModified = null)
{
    /// <summary>The Pokémon generation the parsed game name belongs to (0 when unknown).</summary>
    public int Generation => RomNameRules.GenerationOf(GameName);

    /// <summary>The extension including the dot, lower-case.</summary>
    public string Extension
    {
        get
        {
            var dot = FileName.LastIndexOf('.');
            return dot < 0 ? string.Empty : FileName[dot..].ToLowerInvariant();
        }
    }
}

/// <summary>The Pokémon game a library ROM is, resolved from the file name alone.</summary>
public static class RomNameRules
{
    /// <summary>
    /// "Pokemon - Emerald Version (France).gba" → "Emerald". The franchise prefix, the
    /// "Version" suffix, revision/region tags and disc numbers are all stripped; what
    /// remains is the edition name the trophy and RA lookups expect.
    /// </summary>
    public static string GameName(string fileName)
    {
        var name = fileName;
        var dot = name.LastIndexOf('.');
        if (dot > 0) name = name[..dot];
        foreach (var prefix in new[] { "Pokemon - ", "Pokémon - ", "Pokemon ", "Pokémon " })
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { name = name[prefix.Length..]; break; }
        var cut = name.IndexOf(" (", StringComparison.Ordinal);
        if (cut > 0) name = name[..cut];
        cut = name.IndexOf(" [", StringComparison.Ordinal);
        if (cut > 0) name = name[..cut];
        name = name.Replace(" Version", "", StringComparison.OrdinalIgnoreCase).Trim();
        return name;
    }

    /// <summary>True when a file name looks like a Pokémon ROM at all.</summary>
    public static bool LooksLikePokemon(string fileName) =>
        fileName.StartsWith("Pokemon - ", StringComparison.OrdinalIgnoreCase)
        || fileName.StartsWith("Pokémon - ", StringComparison.OrdinalIgnoreCase)
        || fileName.StartsWith("Pokemon ", StringComparison.OrdinalIgnoreCase)
        || fileName.StartsWith("Pokémon ", StringComparison.OrdinalIgnoreCase);

    /// <summary>The Pokémon generation a game name belongs to (0 when unknown).</summary>
    public static int GenerationOf(string gameName) => gameName switch
    {
        "Red" or "Blue" or "Yellow" or "Green" => 1,
        "Gold" or "Silver" or "Crystal" => 2,
        "Ruby" or "Sapphire" or "Emerald" or "FireRed" or "LeafGreen" => 3,
        "Diamond" or "Pearl" or "Platinum" or "HeartGold" or "SoulSilver" => 4,
        "Black" or "White" or "Black 2" or "White 2" => 5,
        "X" or "Y" or "Omega Ruby" or "Alpha Sapphire" => 6,
        "Sun" or "Moon" or "Ultra Sun" or "Ultra Moon" => 7,
        "Let's Go Pikachu" or "Let's Go Eevee" => 7,
        "Sword" or "Shield" or "Brilliant Diamond" or "Shining Pearl" or "Legends: Arceus" => 8,
        "Scarlet" or "Violet" => 9,
        _ => 0,
    };
}

/// <summary>Walks the ROM folders and reads the installed emulator packages.</summary>
public interface IRomScanner
{
    /// <summary>Scans one console's ES-DE folder for Pokémon ROMs (paths, not SAF ids).</summary>
    IReadOnlyList<LibraryRom> Scan(GameConsole console, CancellationToken cancellationToken = default);

    /// <summary>The ES-DE ROM root that was found ("/storage/emulated/0/ROMs"), or null.</summary>
    string? RomRoot { get; }
}

/// <summary>The ROM library: every discovered Pokémon ROM, persisted between scans.</summary>
public interface IRomLibraryStore
{
    IReadOnlyList<LibraryRom> GetAll();

    /// <summary>Replaces the whole library with one scan's results.</summary>
    void ReplaceAll(IReadOnlyList<LibraryRom> roms);
}
