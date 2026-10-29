using PKForge.Domain;
using Xunit;

namespace PKForge.Domain.Tests;

public sealed class TrophyEngineTests
{
    private static SaveScanFacts Facts(
        int caught = 0, int seen = 0, int total = 151, int shiny = 0, int owned = 0, int hours = 0) =>
        new("save1", "Pokémon Red", 1, caught, seen, total, shiny, owned, hours);

    [Theory]
    [InlineData(TrophyKind.DexCaught, 60, 60)]
    [InlineData(TrophyKind.DexCaught, 30, 60)]
    [InlineData(TrophyKind.DexSeen, 100, 100)]
    [InlineData(TrophyKind.ShinyOwned, 5, 5)]
    [InlineData(TrophyKind.PlaytimeHours, 50, 50)]
    [InlineData(TrophyKind.PokemonOwned, 120, 120)]
    public void MeasuresTheFactTheKindReads(TrophyKind kind, int threshold, int expectedCurrent)
    {
        var scan = Facts(caught: 60, seen: 100, shiny: 5, owned: 120, hours: 50);
        var trophy = new TrophyDefinition("t", "Pokémon Red", 1, kind, threshold, "T", "D");
        var state = TrophyEngine.Evaluate(trophy, scan);
        Assert.Equal(expectedCurrent, state.Current);
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
