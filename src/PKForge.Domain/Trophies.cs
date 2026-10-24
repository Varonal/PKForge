namespace PKForge.Domain;

/// <summary>What a homemade trophy asks the save scan to prove.</summary>
public enum TrophyKind
{
    /// <summary>N dex cells caught in the game's own dex (DexProgress.Caught).</summary>
    DexCaught = 0,
    /// <summary>N dex cells seen.</summary>
    DexSeen = 1,
    /// <summary>N distinct species held shiny across boxes and party.</summary>
    ShinyOwned = 2,
    /// <summary>Completion percentage of the game's own dex reaches N.</summary>
    DexPercent = 3,
    /// <summary>The player's playtime reaches N hours.</summary>
    PlaytimeHours = 4,
    /// <summary>The save has N Pokémon in storage plus party.</summary>
    PokemonOwned = 5,
    /// <summary>The save's dex is complete (every catchable species caught).</summary>
    DexComplete = 6,
    /// <summary>A generation-wide slice: every game of the generation linked and scanned.</summary>
    GenerationComplete = 7,
}

/// <summary>
/// One homemade trophy: what it measures, its threshold, and the reward text. Definitions
/// are generated (see <see cref="TrophyCatalog"/>) so the 39 games stay uniform.
/// </summary>
public sealed record TrophyDefinition(
    string Id,
    string GameLabel,
    int Generation,
    TrophyKind Kind,
    int Threshold,
    string Title,
    string Description,
    /// <summary>The emoji the trophy card shows.</summary>
    string Glyph = "\U0001F3C6");

/// <summary>The outcome of evaluating one trophy against one save scan.</summary>
public sealed record TrophyState(
    string TrophyId,
    bool Unlocked,
    /// <summary>The measured value at scan time (caught count, hours, percent...).</summary>
    int Current)
{
    /// <summary>Progress in whole percent, 100 once unlocked.</summary>
    public int Percent => Unlocked ? 100 : Current <= 0 ? 0 : Math.Min(99, Current);
}

/// <summary>Everything one save scan can measure, in one flat record: the trophy engine's input.</summary>
public sealed record SaveScanFacts(
    string SaveDocumentId,
    string GameLabel,
    int Generation,
    int DexCaught,
    int DexSeen,
    int DexTotal,
    int ShinyOwned,
    int PokemonOwned,
    /// <summary>Playtime in whole hours.</summary>
    int PlaytimeHours,
    string? TrainerName = null)
{
    public int DexPercent => DexTotal == 0 ? 0 : (int)Math.Round(DexCaught * 100.0 / DexTotal);
}

/// <summary>Persists unlocked trophies and their last measured progress.</summary>
public interface ITrophyStore
{
    IReadOnlyList<TrophyState> GetAll();

    TrophyState? Find(string trophyId);

    void Store(TrophyState state);

    /// <summary>Removes every state of one save's games (the shelf's "remove game").</summary>
    void RemoveGame(string gameLabel);
}

/// <summary>
/// The homemade trophy engine: turns save scans into trophy states. Rules are pure —
/// no I/O — so every threshold, percent and unlock can be unit-tested. Manual trophies
/// (bosses, events the bytes cannot prove) stay out: the player confirms those in the UI.
/// </summary>
public static class TrophyEngine
{
    /// <summary>Evaluates one trophy definition against one scan.</summary>
    public static TrophyState Evaluate(TrophyDefinition trophy, SaveScanFacts scan)
    {
        ArgumentNullException.ThrowIfNull(trophy);
        ArgumentNullException.ThrowIfNull(scan);
        var current = Measure(trophy, scan);
        return new TrophyState(trophy.Id, current >= trophy.Threshold, current);
    }

    /// <summary>Evaluates a whole game's trophies against one scan, keeping any unlock.</summary>
    public static IEnumerable<TrophyState> EvaluateGame(IReadOnlyList<TrophyDefinition> trophies, SaveScanFacts scan) =>
        trophies.Select(trophy => Evaluate(trophy, scan));

    /// <summary>The measured value for a trophy kind: the single place thresholds read from.</summary>
    public static int Measure(TrophyDefinition trophy, SaveScanFacts scan) => trophy.Kind switch
    {
        TrophyKind.DexCaught => scan.DexCaught,
        TrophyKind.DexSeen => scan.DexSeen,
        TrophyKind.ShinyOwned => scan.ShinyOwned,
        TrophyKind.DexPercent => scan.DexPercent,
        TrophyKind.PlaytimeHours => scan.PlaytimeHours,
        TrophyKind.PokemonOwned => scan.PokemonOwned,
        TrophyKind.DexComplete => scan.DexTotal == 0 ? 0 : (int)Math.Round(scan.DexCaught * 100.0 / scan.DexTotal),
        TrophyKind.GenerationComplete => scan.DexTotal == 0 ? 0 : (int)Math.Round(scan.DexCaught * 100.0 / scan.DexTotal),
        _ => 0,
    };

    /// <summary>One game's completion percent over its trophy set, for the overview card.</summary>
    public static int GamePercent(IReadOnlyList<TrophyDefinition> trophies, IReadOnlyList<TrophyState> states)
    {
        if (trophies.Count == 0) return 0;
        var unlocked = trophies.Count(t => states.FirstOrDefault(s => s.TrophyId == t.Id)?.Unlocked == true);
        return (int)Math.Round(unlocked * 100.0 / trophies.Count);
    }
}
