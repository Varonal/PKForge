using PKForge.Domain;
using Xunit;

namespace PKForge.Domain.Tests;

public sealed class RomLinkRulesTests
{
    [Theory]
    [InlineData(1, "Pokemon - Red Version.gb", EmulatorKind.Embedded)]
    [InlineData(2, "Pokemon - Crystal Version.gbc", EmulatorKind.Embedded)]
    [InlineData(2, "Pokemon - Crystal Version.zip", EmulatorKind.Embedded)]
    [InlineData(3, "Pokemon - Emerald Version.gba", EmulatorKind.Embedded)]
    [InlineData(4, "Pokemon - Diamond Version.nds", EmulatorKind.MelonDS)]
    [InlineData(5, "Pokemon - Black Version 2.nds", EmulatorKind.MelonDS)]
    public void PrefersTheEmulatorThatRunsTheGeneration(int generation, string rom, EmulatorKind first)
    {
        var emulators = RomLinkRules.EmulatorsForRom(generation, rom);
        Assert.NotEmpty(emulators);
        Assert.Equal(first, emulators[0]);
    }

    [Theory]
    [InlineData(1, "Pokemon - Red Version.gb")]
    [InlineData(2, "Pokemon - Crystal Version.gbc")]
    [InlineData(3, "Pokemon - Emerald Version.gba")]
    public void GenOneToThreeRomsHaveEmbeddedAsTheirOnlyRoute(int generation, string rom)
    {
        // Gen 1-3 ROMs can never be handed to an external emulator.
        Assert.Equal([EmulatorKind.Embedded], RomLinkRules.EmulatorsForRom(generation, rom));
    }

    [Theory]
    [InlineData(3, "Pokemon - Emerald Version.nds")]     // a GBA game on a DS file
    [InlineData(4, "Pokemon - Diamond Version.gba")]     // a DS game on a GBA file
    [InlineData(1, "Pokemon - Red Version.3ds")]         // nothing runnable
    [InlineData(6, "Pokemon - X.3ds")]                   // Gen 6 has no launch route yet
    [InlineData(0, "Whatever.gb")]                        // unknown generation
    public void RefusesExtensionsAndGenerationsItCannotRun(int generation, string rom) =>
        Assert.Empty(RomLinkRules.EmulatorsForRom(generation, rom));

    [Theory]
    [InlineData(EmulatorKind.RetroArch, true)]   // package-scoped MAIN + content extras
    [InlineData(EmulatorKind.MelonDS, true)]     // package-scoped VIEW with the ROM URI
    [InlineData(EmulatorKind.Azahar, true)]      // VIEW handoff: the game loads
    [InlineData(EmulatorKind.Eden, true)]
    [InlineData(EmulatorKind.Dolphin, false)]    // no documented handoff: opens its library
    public void DirectHandoffOnlyForEmulatorsThatDocumentOne(EmulatorKind kind, bool canLaunch) =>
        Assert.Equal(canLaunch, RomLinkRules.CanLaunchRom(kind));

    [Fact]
    public void RetroArchCoresFollowTheGeneration()
    {
        Assert.Equal(RetroArchCore.Gambatte, EmulatorCatalog.CoreForGeneration(1));
        Assert.Equal(RetroArchCore.Gambatte, EmulatorCatalog.CoreForGeneration(2));
        Assert.Equal(RetroArchCore.MGba, EmulatorCatalog.CoreForGeneration(3));
        Assert.Equal(RetroArchCore.MelonDs, EmulatorCatalog.CoreForGeneration(4));
        Assert.Equal(RetroArchCore.MelonDs, EmulatorCatalog.CoreForGeneration(5));
    }

    [Fact]
    public void CatalogNamesStablePackages()
    {
        Assert.Equal("com.retroarch", EmulatorCatalog.App(EmulatorKind.RetroArch).PrimaryPackage);
        Assert.Equal("io.github.melonds", EmulatorCatalog.App(EmulatorKind.MelonDS).PrimaryPackage);
        Assert.Contains("me.magnum.melonds", EmulatorCatalog.App(EmulatorKind.MelonDS).PackageIds);
        Assert.Contains("com.retroarch.aarch64", EmulatorCatalog.App(EmulatorKind.RetroArch).PackageIds);
    }

    [Fact]
    public void InstallationDetectsAnyKnownPackage()
    {
        Assert.True(EmulatorCatalog.IsInstalled(EmulatorKind.RetroArch, id => id == "com.retroarch.aarch64"));
        Assert.False(EmulatorCatalog.IsInstalled(EmulatorKind.MelonDS, id => id == "com.retroarch"));
    }

    [Fact]
    public void DisplayNamesAreStable()
    {
        Assert.Equal("melonDS", EmulatorCatalog.Name(EmulatorKind.MelonDS));
        Assert.Equal("DraStic", EmulatorCatalog.Name(EmulatorKind.DraStic));
        Assert.Equal("Pizza Boy GBA", EmulatorCatalog.Name(EmulatorKind.PizzaBoyGba));
        Assert.Equal("Pizza Boy GBC", EmulatorCatalog.Name(EmulatorKind.PizzaBoyGbc));
        Assert.Equal("Citra MMJ", EmulatorCatalog.Name(EmulatorKind.CitraMmj));
        Assert.Equal("RetroArch", EmulatorCatalog.Name(EmulatorKind.RetroArch));
        Assert.Equal("Eden", EmulatorCatalog.Name(EmulatorKind.Eden));
        Assert.Equal("Linkboy", EmulatorCatalog.Name(EmulatorKind.Linkboy));
        Assert.Equal("Azahar", EmulatorCatalog.Name(EmulatorKind.Azahar));
        Assert.Equal("Dolphin", EmulatorCatalog.Name(EmulatorKind.Dolphin));
    }

    [Fact]
    public void DescribeNamesTheRomAndItsEmulator()
    {
        var link = new RomLink("save1", "file:///roms/emerald.gba", "emerald.gba", EmulatorKind.MelonDS, 3, "Pokémon Emerald");
        Assert.Equal("emerald.gba · melonDS", RomLinkRules.Describe(link));
    }
}

public sealed class RomLinkStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"rom-links-{Guid.NewGuid():N}.json");

    [Fact]
    public void SetReplacesAndRemoveClears()
    {
        var store = new Infrastructure.JsonRomLinkStore(_path);
        Assert.Null(store.FindBySave("save1"));
        store.Set(new RomLink("save1", "file:///roms/emerald.gba", "emerald.gba", EmulatorKind.RetroArch, 3, "Pokémon Emerald"));
        var link = store.FindBySave("save1");
        Assert.NotNull(link);
        Assert.Equal("emerald.gba", link.RomFileName);
        store.Set(new RomLink("save1", "file:///roms/emerald2.gba", "emerald2.gba", EmulatorKind.RetroArch, 3, "Pokémon Emerald"));
        Assert.Equal("emerald2.gba", store.FindBySave("save1")!.RomFileName);
        store.Remove("save1");
        Assert.Null(store.FindBySave("save1"));
    }

    [Fact]
    public void SurvivesAReopenLikeARelaunch()
    {
        var store = new Infrastructure.JsonRomLinkStore(_path);
        store.Set(new RomLink("save1", "file:///roms/gold.gbc", "gold.gbc", EmulatorKind.RetroArch, 2, "Pokémon Gold"));
        var reopened = new Infrastructure.JsonRomLinkStore(_path);
        Assert.Equal("gold.gbc", reopened.FindBySave("save1")!.RomFileName);
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}

public sealed class RetroAchievementsRulesTests
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    [InlineData(6, false)] // 3DS: no official set
    [InlineData(7, false)]
    [InlineData(8, false)] // Switch: house awards instead
    public void OfficialSetsExistForGen1To5Only(int generation, bool supported) =>
        Assert.Equal(supported, RetroAchievementsRules.SupportsGeneration(generation));

    [Fact]
    public void ConsolesMapToGenerations()
    {
        Assert.Equal(4, RetroAchievementsRules.ConsoleForGeneration[1]);
        Assert.Equal(5, RetroAchievementsRules.ConsoleForGeneration[2]);
        Assert.Equal(6, RetroAchievementsRules.ConsoleForGeneration[3]);
        Assert.Equal(18, RetroAchievementsRules.ConsoleForGeneration[4]);
        Assert.Equal(18, RetroAchievementsRules.ConsoleForGeneration[5]);
    }

    [Theory]
    [InlineData("Pokémon Emerald Version", "Emerald")]
    [InlineData("Pokemon Emerald Version", "Emerald")]
    [InlineData("Pokémon Red Version", "Red")]
    [InlineData("Pokémon Blue Version", "Blue")]
    [InlineData("Pokémon Crystal Version", "Crystal")]
    [InlineData("Pokémon HeartGold Version | Pokémon SoulSilver Version", "HeartGold")]
    [InlineData("Pokémon HeartGold Version | Pokémon SoulSilver Version", "SoulSilver")]
    [InlineData("Pokémon Black Version 2", "Black 2")]
    public void TitleMatchingAnswersTheExactEdition(string raTitle, string shortName) =>
        Assert.True(RetroAchievementsRules.TitleMatches(raTitle, "", shortName));

    [Theory]
    [InlineData("Pokémon Red Version", "Blue")]
    [InlineData("Pokémon Emerald Version", "Ruby")]
    [InlineData("Pokémon HeartGold Version | Pokémon SoulSilver Version", "Diamond")]
    [InlineData("Pokémon Black Version 2", "Black")] // the sequel is its own set
    public void TitleMatchingRefusesOtherEditions(string raTitle, string shortName) =>
        Assert.False(RetroAchievementsRules.TitleMatches(raTitle, "", shortName));

    [Fact]
    public void ShortNameStripsTheFranchise()
    {
        Assert.Equal("Emerald", RetroAchievementsRules.ShortName("Pokémon Emerald"));
        Assert.Equal("Black 2", RetroAchievementsRules.ShortName("Pokémon Black Version 2"));
    }
}

public sealed class RetroGameProgressTests
{
    private static RetroAchievement Achievement(int id, int points, bool earned, bool hardcore = false) =>
        new(id, $"A{id}", $"Achievement {id}", points, "00000", earned, earned ? DateTimeOffset.UtcNow : null, hardcore);

    [Fact]
    public void CountsUnlocksPointsAndPercent()
    {
        var progress = new RetroGameProgress(1, "Pokémon Red",
        [
            Achievement(1, 5, true), Achievement(2, 10, true), Achievement(3, 10, false), Achievement(4, 25, false),
        ]);
        Assert.Equal(2, progress.Unlocked);
        Assert.Equal(4, progress.Total);
        Assert.Equal(50, progress.Percent);
        Assert.Equal(15, progress.PointsEarned);
        Assert.Equal(50, progress.PointsTotal);
    }

    [Fact]
    public void AnEmptySetIsComplete()
    {
        var progress = new RetroGameProgress(1, "Unset game", []);
        Assert.Equal(100, progress.Percent);
        Assert.Equal(0, progress.PointsTotal);
    }
}
