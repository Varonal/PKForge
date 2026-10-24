namespace PKForge.Domain;

/// <summary>
/// The 39 mainline Pokémon games (Gen 1-9) with per-generation trophy definitions, in the
/// uniform ladders the spec describes: dex milestones (caught, seen, percent), shinies,
/// playtime, collection size, and completion. Manual trophies (bosses, events) are added
/// per game by the player in the UI and live in the same store.
/// </summary>
public static class TrophyCatalog
{
    /// <summary>One row per game: label, generation, and its catchable dex total.</summary>
    public static readonly IReadOnlyList<(string Label, int Generation, int DexTotal)> Games =
    [
        ("Pokémon Red", 1, 151), ("Pokémon Blue", 1, 151), ("Pokémon Yellow", 1, 151),
        ("Pokémon Gold", 2, 251), ("Pokémon Silver", 2, 251), ("Pokémon Crystal", 2, 251),
        ("Pokémon Ruby", 3, 386), ("Pokémon Sapphire", 3, 386), ("Pokémon Emerald", 3, 386),
        ("Pokémon FireRed", 3, 386), ("Pokémon LeafGreen", 3, 386),
        ("Pokémon Diamond", 4, 493), ("Pokémon Pearl", 4, 493), ("Pokémon Platinum", 4, 493),
        ("Pokémon HeartGold", 4, 493), ("Pokémon SoulSilver", 4, 493),
        ("Pokémon Black", 5, 649), ("Pokémon White", 5, 649),
        ("Pokémon Black 2", 5, 649), ("Pokémon White 2", 5, 649),
        ("Pokémon X", 6, 721), ("Pokémon Y", 6, 721),
        ("Pokémon Omega Ruby", 6, 721), ("Pokémon Alpha Sapphire", 6, 721),
        ("Pokémon Sun", 7, 809), ("Pokémon Moon", 7, 809),
        ("Pokémon Ultra Sun", 7, 809), ("Pokémon Ultra Moon", 7, 809),
        ("Pokémon Let's Go Pikachu", 7, 151), ("Pokémon Let's Go Eevee", 7, 151),
        ("Pokémon Sword", 8, 905), ("Pokémon Shield", 8, 905),
        ("Pokémon Brilliant Diamond", 8, 493), ("Pokémon Shining Pearl", 8, 493),
        ("Pokémon Legends: Arceus", 8, 242),
        ("Pokémon Scarlet", 9, 1025), ("Pokémon Violet", 9, 1025),
    ];

    /// <summary>The dex-caught milestone ladder per generation, in species.</summary>
    private static readonly Dictionary<int, int[]> CaughtLadders =
        new()
        {
            [1] = [10, 30, 60, 90, 120, 151],
            [2] = [10, 30, 60, 100, 151, 251],
            [3] = [10, 30, 60, 100, 200, 386],
            [4] = [10, 30, 60, 100, 250, 493],
            [5] = [10, 30, 60, 100, 300, 649],
            [6] = [10, 30, 60, 100, 300, 721],
            [7] = [10, 30, 60, 100, 400, 809],
            [8] = [10, 30, 60, 100, 400, 905],
            [9] = [10, 30, 60, 100, 500, 1025],
        };

    /// <summary>The shiny ladder: distinct shiny species held at once.</summary>
    private static readonly int[] ShinyLadder = [1, 5, 10, 25, 50, 100];

    /// <summary>The playtime ladder, in hours.</summary>
    private static readonly int[] PlaytimeLadder = [10, 25, 50, 100, 200, 500];

    /// <summary>The collection ladder: Pokémon owned across boxes and party.</summary>
    private static readonly int[] CollectionLadder = [30, 60, 120, 240, 360, 480];

    /// <summary>Every trophy of one game, deterministic from the catalogue.</summary>
    public static IReadOnlyList<TrophyDefinition> ForGame(string label)
    {
        var game = Games.FirstOrDefault(g => LabelMatches(g.Label, label));
        if (game.Label is null) return [];
        var id = Slug(game.Label);
        var list = new List<TrophyDefinition>();

        foreach (var caught in CaughtLadders[game.Generation])
            list.Add(new TrophyDefinition($"{id}-dex{caught}", game.Label, game.Generation, TrophyKind.DexCaught,
                caught, $"Catcher {TierName(caught)}", $"Catch {caught} species.",
                caught >= game.DexTotal ? "\U0001F511" : "\U0001F3AF"));

        foreach (var seen in new[] { 10, 50, 100, game.DexTotal })
            list.Add(new TrophyDefinition($"{id}-seen{seen}", game.Label, game.Generation, TrophyKind.DexSeen,
                seen, $"Spotter {TierName(seen)}", $"See {seen} species.", "\U0001F441"));

        foreach (var shiny in ShinyLadder)
            list.Add(new TrophyDefinition($"{id}-shiny{shiny}", game.Label, game.Generation, TrophyKind.ShinyOwned,
                shiny, $"Shiny Hunter {TierName(shiny)}", $"Hold {shiny} distinct shiny species.", "\u2728"));

        foreach (var hours in PlaytimeLadder)
            list.Add(new TrophyDefinition($"{id}-time{hours}", game.Label, game.Generation, TrophyKind.PlaytimeHours,
                hours, $"Trainer {TierName(hours)}", $"Play for {hours} hours.", "\u23F1"));

        foreach (var owned in CollectionLadder)
            list.Add(new TrophyDefinition($"{id}-own{owned}", game.Label, game.Generation, TrophyKind.PokemonOwned,
                owned, $"Collector {TierName(owned)}", $"Own {owned} Pokémon across boxes and party.", "\U0001F4E6"));

        list.Add(new TrophyDefinition($"{id}-complete", game.Label, game.Generation, TrophyKind.DexComplete,
            100, "Dex Master", $"Catch every species the {game.Label} dex registers.", "\U0001F3C6"));

        return list;
    }

    /// <summary>Every trophy of every game (the full 39-game ladder set).</summary>
    public static IReadOnlyList<TrophyDefinition> All() => [.. Games.SelectMany(g => ForGame(g.Label))];

    /// <summary>True when the label is the same game (edition-exact, accent-tolerant).</summary>
    public static bool LabelMatches(string catalogLabel, string label)
    {
        var a = Normalise(catalogLabel);
        var b = Normalise(label);
        return a == b;

        static string Normalise(string text) =>
            text.Trim().ToLowerInvariant().Replace("pokémon ", "pokemon ").Replace("é", "e").Replace(":", "").Replace("'", "");
    }

    private static string TierName(int threshold) => threshold switch
    {
        <= 10 => "I",
        <= 30 => "II",
        <= 60 => "III",
        < 100 => "IV",
        < 500 => "V",
        _ => "VI",
    };

    private static string Slug(string label) =>
        label.ToLowerInvariant().Replace("é", "e").Replace(":", "").Replace(" ", "-").Replace("'", "");
}
