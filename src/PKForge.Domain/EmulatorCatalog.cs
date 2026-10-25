namespace PKForge.Domain;

/// <summary>
/// The Android launch facts for one <see cref="EmulatorKind"/>: which packages answer, and
/// how a ROM is handed over. Package ids are the public Play Store / release ids; alternates
/// cover fork builds that share the same storage layout.
/// </summary>
public sealed record EmulatorApp(
    EmulatorKind Kind,
    IReadOnlyList<string> PackageIds,
    /// <summary>How a ROM reaches the emulator (see <see cref="RomLaunchStyle"/>).</summary>
    RomLaunchStyle LaunchStyle,
    /// <summary>RetroArch cores, in preference order, for the systems this emulator runs.</summary>
    IReadOnlyList<RetroArchCore>? Cores = null)
{
    /// <summary>The id every APK of this emulator is known by (alternates follow).</summary>
    public string PrimaryPackage => PackageIds[0];
}

/// <summary>How PKForge hands a ROM path to an installed emulator.</summary>
public enum RomLaunchStyle
{
    /// <summary>RetroArch's RetroActivityFuture: explicit ROM + LIBRETRO intent extras.</summary>
    RetroArch = 0,
    /// <summary>A content URI in the intent data, as melonDS and most modern frontends accept.</summary>
    UriData = 1,
    /// <summary>A plain file path in the PATH extra (melonDS's EmulatorActivity).</summary>
    PathExtra = 2,
    /// <summary>No documented launch intent: the emulator's own library screen opens instead.</summary>
    OpenAppOnly = 3,
}

/// <summary>The RetroArch cores PKForge can name when launching a ROM, per system.</summary>
public enum RetroArchCore
{
    Gambatte = 0,
    MGba = 1,
    MelonDs = 2,
    DesMuMe = 3,
}

/// <summary>
/// The static emulator catalogue: everything the Emulators tab, the ROM linker and the
/// launcher ask about, in one place. Package ids must stay stable: they identify installs.
/// </summary>
public static class EmulatorCatalog
{
    /// <summary>Every emulator PKForge can link and launch, in shelf order.</summary>
    public static readonly IReadOnlyList<EmulatorApp> All =
    [
        new(EmulatorKind.RetroArch,
            ["com.retroarch", "com.retroarch.aarch64", "com.retroarch.ra32"],
            RomLaunchStyle.RetroArch,
            [RetroArchCore.Gambatte, RetroArchCore.MGba, RetroArchCore.MelonDs, RetroArchCore.DesMuMe]),
        new(EmulatorKind.MelonDS,
            ["io.github.melonds", "me.magnum.melonds"],
            RomLaunchStyle.UriData),
        new(EmulatorKind.Azahar,
            ["io.github.lime3ds.android", "org.azahar.azahar", "org.citra.emu", "io.github.azahar"],
            RomLaunchStyle.OpenAppOnly),
        new(EmulatorKind.CitraMmj,
            ["org.citra.emu"],
            RomLaunchStyle.OpenAppOnly),
        new(EmulatorKind.Eden,
            ["dev.eden.eden_emulator", "org.sudachi.sudachi_emu", "org.yuzu.yuzu_emu", "org.ryujinx", "com.skyline.emu", "dev.eden.eden_emulator.dualscreen"],
            RomLaunchStyle.OpenAppOnly),
        new(EmulatorKind.Dolphin,
            ["org.dolphinemu.dolphin"],
            RomLaunchStyle.OpenAppOnly),
        new(EmulatorKind.DraStic,
            ["com.dsemu.drastic"],
            RomLaunchStyle.OpenAppOnly),
        new(EmulatorKind.Linkboy,
            ["com.gitlab.Misigno.linkboy"],
            RomLaunchStyle.OpenAppOnly),
        new(EmulatorKind.PizzaBoyGba,
            ["it.dbtecno.pizzaboy"],
            RomLaunchStyle.OpenAppOnly),
        new(EmulatorKind.PizzaBoyGbc,
            ["it.dbtecno.pizzaboygbc"],
            RomLaunchStyle.OpenAppOnly),
    ];

    public static EmulatorApp App(EmulatorKind kind) =>
        All.FirstOrDefault(app => app.Kind == kind)
        ?? new EmulatorApp(kind, [kind.ToString().ToLowerInvariant()], RomLaunchStyle.OpenAppOnly);

    /// <summary>True when any of the emulator's packages is installed.</summary>
    public static bool IsInstalled(EmulatorKind kind, Func<string, bool> isPackageInstalled) =>
        App(kind).PackageIds.Any(isPackageInstalled);

    /// <summary>The RetroArch core file name (without directories) for a core.</summary>
    public static string CoreFileName(RetroArchCore core) => core switch
    {
        RetroArchCore.Gambatte => "gambatte_libretro_android.so",
        RetroArchCore.MGba => "mgba_libretro_android.so",
        RetroArchCore.MelonDs => "melonds_libretro_android.so",
        RetroArchCore.DesMuMe => "desmume_libretro_android.so",
        _ => throw new ArgumentOutOfRangeException(nameof(core)),
    };

    /// <summary>The core a Pokémon generation should run on, for GB/GBC/GBA/DS RetroArch launches.</summary>
    public static RetroArchCore CoreForGeneration(int generation) => generation switch
    {
        1 or 2 => RetroArchCore.Gambatte,
        3 => RetroArchCore.MGba,
        4 or 5 => RetroArchCore.MelonDs,
        _ => throw new ArgumentOutOfRangeException(nameof(generation), $"RetroArch does not run Gen {generation} Pokémon."),
    };

    /// <summary>Every folder grant of this emulator kind, as the Link flow stores them.</summary>
    public static bool SupportsSaveScan(EmulatorKind kind) => kind != EmulatorKind.Eden && kind != EmulatorKind.Dolphin;

    /// <summary>The systems whose ROMs this emulator runs, for the launcher's expectations.</summary>
    public static string SystemsOf(EmulatorKind kind) => kind switch
    {
        EmulatorKind.RetroArch => "GB, GBC, GBA, DS",
        EmulatorKind.MelonDS => "Nintendo DS",
        EmulatorKind.Azahar or EmulatorKind.CitraMmj => "Nintendo 3DS",
        EmulatorKind.Eden => "Nintendo Switch",
        EmulatorKind.Dolphin => "GameCube",
        EmulatorKind.DraStic => "Nintendo DS",
        EmulatorKind.Linkboy => "GB, GBC, GBA",
        EmulatorKind.PizzaBoyGba => "Game Boy Advance",
        EmulatorKind.PizzaBoyGbc => "GB, GBC",
        _ => kind.ToString(),
    };
}
