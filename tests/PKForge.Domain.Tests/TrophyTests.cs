using PKForge.Domain;
using Xunit;

namespace PKForge.Domain.Tests;

public sealed class TrophyEngineTests
{
    private static SaveScanFacts Facts(
        int caught = 0, int seen = 0, int total = 151, int shiny = 0, int owned = 0, int hours = 0) =>
        new("save1", "Pokémon Red", 1, caught, seen, total, shiny, owned, hours);

    [Theory]
    [InlineData(TrophyKind.DexCaught, 60)]
    [InlineData(TrophyKind.DexSeen, 100)]
    [InlineData(TrophyKind.ShinyOwned, 5)]
    [InlineData(TrophyKind.PlaytimeHours, 50)]
    [InlineData(TrophyKind.PokemonOwned, 120)]
    public void MeasuresTheFactTheKindReads(TrophyKind kind, int measured)
    {
        // One scan, fixed facts: each kind reads its own number, whatever the threshold.
        var scan = Facts(caught: 60, seen: 100, shiny: 5, owned: 120, hours: 50);
        var trophy = new TrophyDefinition("t", "Pokémon Red", 1, kind, measured + 1, "T", "D");
        var state = TrophyEngine.Evaluate(trophy, scan);
        Assert.Equal(measured, state.Current);
    }

    [Fact]
    public void UnlocksAtTheThresholdAndNotBefore()
    {
        var trophy = new TrophyDefinition("t", "Pokémon Red", 1, TrophyKind.DexCaught, 60, "Catcher III", "Catch 60.");
        Assert.False(TrophyEngine.Evaluate(trophy, Facts(caught: 59)).Unlocked);
        Assert.True(TrophyEngine.Evaluate(trophy, Facts(caught: 60)).Unlocked);
        Assert.True(TrophyEngine.Evaluate(trophy, Facts(caught: 151)).Unlocked);
    }

    [Fact]
    public void DexPercentRoundsTheCaughtShare()
    {
        var scan = Facts(caught: 75, total: 151); // 49.67 → 50
        var trophy = new TrophyDefinition("t", "Pokémon Red", 1, TrophyKind.DexPercent, 50, "Half", "50%.");
        Assert.True(TrophyEngine.Evaluate(trophy, scan).Unlocked);
    }

    [Fact]
    public void DexCompleteUnlocksOnlyAtEverySpecies()
    {
        var trophy = new TrophyDefinition("t", "Pokémon Red", 1, TrophyKind.DexComplete, 100, "Dex Master", "All.");
        Assert.False(TrophyEngine.Evaluate(trophy, Facts(caught: 150)).Unlocked);
        Assert.True(TrophyEngine.Evaluate(trophy, Facts(caught: 151)).Unlocked);
    }

    [Fact]
    public void GamePercentCountsUnlockedTrophies()
    {
        var trophies = TrophyCatalog.ForGame("Pokémon Red");
        Assert.NotEmpty(trophies);
        var states = trophies.Take(3).Select(t => new TrophyState(t.Id, true, t.Threshold)).ToList();
        Assert.True(TrophyEngine.GamePercent(trophies, states) > 0);
        Assert.Equal(100, TrophyEngine.GamePercent(trophies, trophies.Select(t => new TrophyState(t.Id, true, t.Threshold)).ToList()));
        Assert.Equal(0, TrophyEngine.GamePercent(trophies, []));
    }
}

public sealed class TrophyCatalogTests
{
    [Fact]
    public void CoversTheMainlineGames()
    {
        Assert.True(TrophyCatalog.Games.Count >= 37);
        Assert.Equal(9, TrophyCatalog.Games.Select(g => g.Generation).Distinct().Count());
        Assert.Equal(151, TrophyCatalog.Games.First(g => g.Generation == 1).DexTotal);
        Assert.Equal(1025, TrophyCatalog.Games.First(g => g.Generation == 9).DexTotal);
    }

    [Fact]
    public void EveryGameHasItsLadder()
    {
        foreach (var game in TrophyCatalog.Games)
        {
            var trophies = TrophyCatalog.ForGame(game.Label);
            Assert.NotEmpty(trophies);
            Assert.Equal(game.Label, trophies[0].GameLabel);
            Assert.Equal(game.Generation, trophies[0].Generation);
            Assert.Contains(trophies, t => t.Kind == TrophyKind.DexComplete);
            Assert.Contains(trophies, t => t.Kind == TrophyKind.ShinyOwned);
            Assert.Contains(trophies, t => t.Kind == TrophyKind.PlaytimeHours);
        }
    }

    [Theory]
    [InlineData("Pokémon Emerald")]
    [InlineData("Pokemon Emerald")]
    [InlineData("Pokémon Let's Go Pikachu")]
    [InlineData("Pokémon Legends: Arceus")]
    public void LabelMatchingIsAccentAndPunctuationTolerant(string label)
    {
        Assert.NotEmpty(TrophyCatalog.ForGame(label));
        Assert.True(TrophyCatalog.LabelMatches("Pokémon Emerald", "Pokemon Emerald"));
    }

    [Theory]
    [InlineData("Pokémon Radical Red")]  // ROM hack: no ladder, the house system shows nothing
    [InlineData("Unknown Game")]
    [InlineData("")]
    public void GamesOutsideTheCatalogHaveNoLadder(string label) =>
        Assert.Empty(TrophyCatalog.ForGame(label));

    [Fact]
    public void TrophyIdsAreUniqueAcrossTheCatalog()
    {
        var all = TrophyCatalog.All();
        Assert.Equal(all.Count, all.Select(t => t.Id).Distinct().Count());
    }
}

public sealed class TrophyStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"trophies-{Guid.NewGuid():N}.json");

    [Fact]
    public void AnUnlockIsPermanent()
    {
        var store = new Infrastructure.JsonTrophyStore(_path);
        store.Store(new TrophyState("red-dex60", true, 60));
        store.Store(new TrophyState("red-dex60", false, 10));
        var state = store.Find("red-dex60");
        Assert.NotNull(state);
        Assert.True(state.Unlocked);
        Assert.Equal(60, state.Current);
    }

    [Fact]
    public void LockedProgressTracksTheBestValue()
    {
        var store = new Infrastructure.JsonTrophyStore(_path);
        store.Store(new TrophyState("red-dex10", false, 4));
        store.Store(new TrophyState("red-dex10", false, 7));
        Assert.Equal(7, store.Find("red-dex10")!.Current);
    }

    [Fact]
    public void SurvivesAReopenLikeARelaunch()
    {
        var store = new Infrastructure.JsonTrophyStore(_path);
        store.Store(new TrophyState("red-dex60", true, 60));
        var reopened = new Infrastructure.JsonTrophyStore(_path);
        Assert.True(reopened.Find("red-dex60")!.Unlocked);
    }

    [Fact]
    public void RemoveGameClearsItsTrophiesOnly()
    {
        var store = new Infrastructure.JsonTrophyStore(_path);
        store.Store(new TrophyState("pokemon-red-dex60", true, 60));
        store.Store(new TrophyState("pokemon-emerald-dex60", true, 60));
        store.RemoveGame("Pokémon Red");
        Assert.Null(store.Find("pokemon-red-dex60"));
        Assert.NotNull(store.Find("pokemon-emerald-dex60"));
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}

public sealed class TrophyManualCatalogTests
{
    [Theory]
    [InlineData("Pokémon Emerald", 3)]
    [InlineData("Pokémon Red", 2)]
    [InlineData("Pokémon Scarlet", 2)]
    public void MainlineGamesHaveTheirManualLadder(string label, int expected)
    {
        var manual = TrophyManualCatalog.ForGame(label);
        Assert.Equal(expected, manual.Count);
        Assert.All(manual, t => Assert.Equal(TrophyKind.Manual, t.Kind));
        Assert.All(manual, t => Assert.Equal(1, t.Threshold));
        Assert.Contains(manual, t => t.Title == "Champion");
    }

    [Theory]
    [InlineData("Pokémon Radical Red")]
    [InlineData("Unknown Game")]
    public void GamesOutsideTheCatalogHaveNoManualLadder(string label) =>
        Assert.Empty(TrophyManualCatalog.ForGame(label));

    [Fact]
    public void ManualTrophiesNeverUnlockFromScans()
    {
        var manual = TrophyManualCatalog.ForGame("Pokémon Emerald")[0];
        // Even a perfect save measures zero: only the player's confirmation unlocks it.
        var scan = new SaveScanFacts("s", "Pokémon Emerald", 3, 386, 386, 386, 386, 600, 500);
        Assert.False(TrophyEngine.Evaluate(manual, scan).Unlocked);
    }

    [Fact]
    public void ManualAndScanTrophiesShareOneIdSpace()
    {
        var scanTrophies = TrophyCatalog.ForGame("Pokémon Emerald");
        var manualTrophies = TrophyManualCatalog.ForGame("Pokémon Emerald");
        var ids = scanTrophies.Select(t => t.Id).Concat(manualTrophies.Select(t => t.Id)).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void EveryCatalogGameHasAManualChampionTrophy()
    {
        foreach (var game in TrophyCatalog.Games)
            Assert.Contains(TrophyManualCatalog.ForGame(game.Label), t => t.Title == "Champion");
    }
}

public sealed class TrophyStoreManualTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"trophies-manual-{Guid.NewGuid():N}.json");

    [Fact]
    public void ARelockedManualTrophyIsForgotten()
    {
        var store = new Infrastructure.JsonTrophyStore(_path);
        store.Store(new TrophyState("pokemon-red-manual-champion", true, 1));
        store.Remove("pokemon-red-manual-champion");
        Assert.Null(store.Find("pokemon-red-manual-champion"));
    }

    [Fact]
    public void RemoveKeepsTheOtherTrophies()
    {
        var store = new Infrastructure.JsonTrophyStore(_path);
        store.Store(new TrophyState("pokemon-red-manual-champion", true, 1));
        store.Store(new TrophyState("pokemon-red-dex60", true, 60));
        store.Remove("pokemon-red-manual-champion");
        Assert.NotNull(store.Find("pokemon-red-dex60"));
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}

public sealed class RomNameRulesTests
{
    // GameName and LooksLikePokemon cases live in FrenchEditionNameTests, which
    // covers English, French, No-Intro and bare-name files in one place.

    [Theory]
    [InlineData("Emerald", 3)]
    [InlineData("Red", 1)]
    [InlineData("Black 2", 5)]
    [InlineData("Scarlet", 9)]
    [InlineData("Unknown Hack", 0)]
    public void MapsEditionsToGenerations(string gameName, int expected) =>
        Assert.Equal(expected, RomNameRules.GenerationOf(gameName));
}

public sealed class ConsoleRulesTests
{
    [Fact]
    public void GenerationsMapToTheirConsoles()
    {
        Assert.Equal(GameConsole.GameBoy, ConsoleRules.ForGeneration(1)[0]);
        Assert.Equal(GameConsole.GameBoyColor, ConsoleRules.ForGeneration(2)[0]);
        Assert.Equal(GameConsole.GameBoyAdvance, ConsoleRules.ForGeneration(3)[0]);
        Assert.Equal(GameConsole.NintendoDs, ConsoleRules.ForGeneration(4)[0]);
        Assert.Equal(GameConsole.NintendoSwitch, ConsoleRules.ForGeneration(9)[0]);
    }

    [Fact]
    public void EachConsoleNamesItsEmulatorsInPreferenceOrder()
    {
        Assert.Equal(EmulatorKind.RetroArch, ConsoleRules.EmulatorsFor(GameConsole.GameBoy)[0]);
        Assert.Equal(EmulatorKind.MelonDS, ConsoleRules.EmulatorsFor(GameConsole.NintendoDs)[0]);
        Assert.Equal(EmulatorKind.Azahar, ConsoleRules.EmulatorsFor(GameConsole.Nintendo3Ds)[0]);
        Assert.Equal(EmulatorKind.Eden, ConsoleRules.EmulatorsFor(GameConsole.NintendoSwitch)[0]);
    }

    [Fact]
    public void InstallChecksFollowThePreferenceList()
    {
        Assert.True(ConsoleRules.AnyInstalled(GameConsole.GameBoy, id => id == "com.retroarch.aarch64"));
        Assert.False(ConsoleRules.AnyInstalled(GameConsole.Nintendo3Ds, id => id == "com.retroarch"));
    }
}

public sealed class ConsoleEmulatorStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"consoles-{Guid.NewGuid():N}.json");

    [Fact]
    public void BindsAndClearsPerConsole()
    {
        var store = new Infrastructure.JsonConsoleEmulatorStore(_path);
        Assert.Null(store.Find(GameConsole.GameBoyAdvance));
        store.Set(GameConsole.GameBoyAdvance, EmulatorKind.RetroArch);
        Assert.Equal(EmulatorKind.RetroArch, store.Find(GameConsole.GameBoyAdvance));
        store.Set(GameConsole.GameBoyAdvance, null);
        Assert.Null(store.Find(GameConsole.GameBoyAdvance));
    }

    [Fact]
    public void TwoConsolesBindIndependently()
    {
        var store = new Infrastructure.JsonConsoleEmulatorStore(_path);
        store.Set(GameConsole.NintendoDs, EmulatorKind.MelonDS);
        store.Set(GameConsole.GameBoy, EmulatorKind.RetroArch);
        Assert.Equal(EmulatorKind.MelonDS, store.Find(GameConsole.NintendoDs));
        Assert.Equal(EmulatorKind.RetroArch, store.Find(GameConsole.GameBoy));
    }

    [Fact]
    public void SurvivesAReopenLikeARelaunch()
    {
        var store = new Infrastructure.JsonConsoleEmulatorStore(_path);
        store.Set(GameConsole.NintendoDs, EmulatorKind.MelonDS);
        Assert.Equal(EmulatorKind.MelonDS, new Infrastructure.JsonConsoleEmulatorStore(_path).Find(GameConsole.NintendoDs));
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}

public sealed class LanguageRulesTests
{
    [Fact]
    public void FrenchIsTheDefault()
    {
        Assert.Equal(AppLanguage.French, LanguageRules.Default);
        Assert.Equal(AppLanguage.French, LanguageRules.Resolve(hasChoice: false, AppLanguage.English, deviceLocale: "de"));
        Assert.Equal(AppLanguage.French, LanguageRules.Resolve(hasChoice: false, AppLanguage.English, deviceLocale: null));
    }

    [Theory]
    [InlineData("fr", true)]
    [InlineData("fr-FR", true)]
    [InlineData("fr_CA", true)]
    [InlineData("en-US", false)]
    [InlineData(null, false)]
    public void RecognisesFrenchLocales(string? locale, bool expected) =>
        Assert.Equal(expected, LanguageRules.IsFrenchLocale(locale));

    [Fact]
    public void ThePlayersChoiceWins()
    {
        Assert.Equal(AppLanguage.English, LanguageRules.Resolve(hasChoice: true, AppLanguage.English, deviceLocale: "fr-FR"));
        Assert.Equal(AppLanguage.French, LanguageRules.Resolve(hasChoice: true, AppLanguage.French, deviceLocale: "en-US"));
    }
}

public sealed class FrenchEditionNameTests
{
    [Theory]
    [InlineData("Pokemon - Emerald Version (France).gba", "Emerald")]
    [InlineData("1636 - Pokemon Emerald (Independent).gba", "Emerald")]
    [InlineData("Pokemon - Version Émeraude (Fr).gba", "Emerald")]
    [InlineData("Pokémon Emeraude.gba", "Emerald")]
    [InlineData("emerald.gba", "Emerald")]
    [InlineData("Pokemon - Rouge Feu (France).gba", "FireRed")]
    [InlineData("Pokemon - Or.gbc", "Gold")]
    [InlineData("Pokemon - Version Écarlate.nsz", "Scarlet")]
    [InlineData("Pokemon - Épée.nsp", "Sword")]
    public void ParsesNoIntroFrenchAndBareNames(string fileName, string expected) =>
        Assert.Equal(expected, RomNameRules.GameName(fileName));

    [Theory]
    [InlineData("rouge", "Red")]
    [InlineData("rouge feu", "FireRed")]
    [InlineData("émeraude", "Emerald")]
    [InlineData("emeraude", "Emerald")]
    [InlineData("diamant étincelant", "Brilliant Diamond")]
    [InlineData("perle scintillante", "Shining Pearl")]
    [InlineData("écarlate", "Scarlet")]
    [InlineData("épée", "Sword")]
    public void MapsFrenchEditionsToEnglish(string french, string expected) =>
        Assert.Equal(expected, FrenchEditionName.ToEnglish(french));

    [Theory]
    [InlineData("Emerald")]
    [InlineData("FireRed")]
    [InlineData("Scarlet")]
    public void EnglishEditionsPassThrough(string english) =>
        Assert.Equal(english, FrenchEditionName.ToEnglish(english));

    [Theory]
    [InlineData("Radical Red")]
    [InlineData("Unbound")]
    [InlineData("Something Else")]
    public void UnknownNamesReturnNull(string unknown) =>
        Assert.Null(FrenchEditionName.ToEnglish(unknown));

    [Theory]
    [InlineData("1636 - Pokemon Emerald (Independent).gba", true)]
    [InlineData("emerald.gba", true)]
    [InlineData("rouge feu.gba", true)]
    [InlineData("Mario Kart DS.nds", false)]
    [InlineData("tetris.gb", false)]
    public void TheScannerRecognisesPokemonFilesOnly(string fileName, bool expected) =>
        Assert.Equal(expected, RomNameRules.LooksLikePokemon(fileName));
}
