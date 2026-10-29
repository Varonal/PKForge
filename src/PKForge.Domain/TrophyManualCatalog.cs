namespace PKForge.Domain;

/// <summary>
/// The manual trophy ladder per game: bosses, league titles, event legendaries — feats the
/// save bytes cannot prove, so the player confirms them. Per generation, mirroring each
/// generation's own milestones; unknown games get none.
/// </summary>
public static class TrophyManualCatalog
{
    /// <summary>Every manual trophy of one game, keyed by the same label rules as the scans.</summary>
    public static IReadOnlyList<TrophyDefinition> ForGame(string label)
    {
        if (!TrophyCatalog.Games.Any(g => TrophyCatalog.LabelMatches(g.Label, label))) return [];
        var game = TrophyCatalog.Games.First(g => TrophyCatalog.LabelMatches(g.Label, label));
        var id = Slug(game.Label);
        return [.. ManualLadder(game.Generation, game.Label, id)];
    }

    private static IEnumerable<TrophyDefinition> ManualLadder(int generation, string label, string id) => generation switch
    {
        1 =>
        [
            new($"{id}-manual-champion", label, generation, TrophyKind.Manual, 1, "Champion", "Beat the Elite Four and enter the Hall of Fame.", "\U0001F3C6"),
            new($"{id}-manual-mew", label, generation, TrophyKind.Manual, 1, "Mew Hunter", "Catch or receive Mew.", "\U0001F9E1"),
        ],
        2 =>
        [
            new($"{id}-manual-champion", label, generation, TrophyKind.Manual, 1, "Champion", "Beat the Elite Four and enter the Hall of Fame.", "\U0001F3C6"),
            new($"{id}-manual-redgyarados", label, generation, TrophyKind.Manual, 1, "Red Gyarados", "Catch the rampaging Red Gyarados at the Lake of Rage.", "\U0001F534"),
            new($"{id}-manual-celebi", label, generation, TrophyKind.Manual, 1, "Celebi Hunter", "Receive Celebi.", "\U0001F343"),
        ],
        3 =>
        [
            new($"{id}-manual-champion", label, generation, TrophyKind.Manual, 1, "Champion", "Beat the Elite Four and enter the Hall of Fame.", "\U0001F3C6"),
            new($"{id}-manual-battlefrontier", label, generation, TrophyKind.Manual, 1, "Frontier Brain", "Beat a Battle Frontier Brain.", "\U0001F396"),
            new($"{id}-manual-contest", label, generation, TrophyKind.Manual, 1, "Contest Master", "Win a Master Rank Pokémon Contest.", "\U0001F3AD"),
        ],
        4 =>
        [
            new($"{id}-manual-champion", label, generation, TrophyKind.Manual, 1, "Champion", "Beat the Elite Four and enter the Hall of Fame.", "\U0001F3C6"),
            new($"{id}-manual-pokelathon", label, generation, TrophyKind.Manual, 1, "Pokéathlon Ace", "Win all Pokéathlon events.", "\U0001F3AF"),
            new($"{id}-manual-ribbon", label, generation, TrophyKind.Manual, 1, "Ribbon Collector", "Earn every ribbon on one Pokémon.", "\U0001F380"),
        ],
        5 =>
        [
            new($"{id}-manual-champion", label, generation, TrophyKind.Manual, 1, "Champion", "Beat the Elite Four and enter the Hall of Fame.", "\U0001F3C6"),
            new($"{id}-manual-subway", label, generation, TrophyKind.Manual, 1, "Subway Boss", "Beat a Battle Subway boss.", "\U0001F687"),
            new($"{id}-manual-musical", label, generation, TrophyKind.Manual, 1, "Musical Star", "Star in a Pokémon Musical.", "\U0001F3B5"),
        ],
        6 =>
        [
            new($"{id}-manual-champion", label, generation, TrophyKind.Manual, 1, "Champion", "Beat the Elite Four and enter the Hall of Fame.", "\U0001F3C6"),
            new($"{id}-manual-maison", label, generation, TrophyKind.Manual, 1, "Maison Boss", "Beat a Battle Maison boss.", "\U0001F3E0"),
            new($"{id}-manual-contest", label, generation, TrophyKind.Manual, 1, "Contest Master", "Win a Master Rank Pokémon Contest.", "\U0001F3AD"),
        ],
        7 =>
        [
            new($"{id}-manual-champion", label, generation, TrophyKind.Manual, 1, "Champion", "Become the first Alola Champion.", "\U0001F3C6"),
            new($"{id}-manual-royal", label, generation, TrophyKind.Manual, 1, "Royal Winner", "Win a Battle Royal.", "\U0001F451"),
            new($"{id}-manual-magearna", label, generation, TrophyKind.Manual, 1, "Magearna Hunter", "Receive Magearna.", "\U0001F9E9"),
        ],
        8 =>
        [
            new($"{id}-manual-champion", label, generation, TrophyKind.Manual, 1, "Champion", "Beat the Champion Cup.", "\U0001F3C6"),
            new($"{id}-manual-tower", label, generation, TrophyKind.Manual, 1, "Tower Boss", "Beat a Battle Tower boss.", "\U0001F3ED"),
            new($"{id}-manual-galarstar", label, generation, TrophyKind.Manual, 1, "Galar Star", "Complete the Galar Star Tournament.", "\u2B50"),
        ],
        _ =>
        [
            new($"{id}-manual-champion", label, generation, TrophyKind.Manual, 1, "Champion", "Beat the Champion Cup.", "\U0001F3C6"),
            new($"{id}-manual-ace", label, generation, TrophyKind.Manual, 1, "Ace Trainer", "Beat the Ace Tournament.", "\U0001F3AF"),
        ],
    };

    private static string Slug(string label) =>
        label.ToLowerInvariant().Replace("é", "e").Replace(":", "").Replace(" ", "-").Replace("'", "");
}
