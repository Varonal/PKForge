namespace PKForge.Domain;

/// <summary>
/// Maps French Pokémon edition names to the English editions the catalog, art and
/// trophy lookups expect — French ROM dumps ("Version Émeraude", "Rouge Feu") and
/// the bundle's own alias table. Null when the name is unknown (the caller keeps it).
/// </summary>
public static class FrenchEditionName
{
    private static readonly Dictionary<string, string> FrenchToEnglish =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Gen 1
            ["rouge"] = "Red", ["bleu"] = "Blue", ["jaune"] = "Yellow", ["vert"] = "Green (JP)",
            // Gen 2
            ["or"] = "Gold", ["argent"] = "Silver", ["cristal"] = "Crystal",
            // Gen 3
            ["rubis"] = "Ruby", ["saphir"] = "Sapphire", ["émeraude"] = "Emerald",
            ["emeraude"] = "Emerald", ["rouge feu"] = "FireRed", ["vert feuille"] = "LeafGreen",
            // Gen 4
            ["diamant"] = "Diamond", ["perle"] = "Pearl", ["platine"] = "Platinum",
            ["or heartgold"] = "HeartGold", ["argent soulsilver"] = "SoulSilver",
            ["heartgold"] = "HeartGold", ["soulsilver"] = "SoulSilver",
            // Gen 5
            ["noir"] = "Black", ["blanc"] = "White", ["noir 2"] = "Black 2", ["blanc 2"] = "White 2",
            // Gen 6
            ["x"] = "X", ["y"] = "Y", ["rubis oméga"] = "Omega Ruby", ["saphir alpha"] = "Alpha Sapphire",
            ["rubis omega"] = "Omega Ruby", ["saphir alpha"] = "Alpha Sapphire",
            // Gen 7
            ["soleil"] = "Sun", ["lune"] = "Moon", ["ultra soleil"] = "Ultra Sun", ["ultra lune"] = "Ultra Moon",
            ["let's go pikachu"] = "Let's Go Pikachu", ["let's go évoli"] = "Let's Go Eevee",
            ["let's go evoli"] = "Let's Go Eevee",
            // Gen 8
            ["épée"] = "Sword", ["epee"] = "Sword", ["bouclier"] = "Shield",
            ["diamant étincelant"] = "Brilliant Diamond", ["diamant etincelant"] = "Brilliant Diamond",
            ["perle scintillante"] = "Shining Pearl",
            ["légendes arceus"] = "Legends: Arceus", ["legendes arceus"] = "Legends: Arceus",
            // Gen 9
            ["écarlate"] = "Scarlet", ["ecarlate"] = "Scarlet", ["violet"] = "Violet",
            ["légies z-a"] = "Legends: Z-A",
        };

    /// <summary>The catalog's English edition for a French (or already-English) name;
    /// null when the name matches no known edition. English names pass through.</summary>
    public static string? ToEnglish(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var clean = name.Trim();
        if (FrenchToEnglish.TryGetValue(clean, out var english)) return english;
        return EnglishEditions.Contains(clean) ? clean : null;
    }

    private static readonly HashSet<string> EnglishEditions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Red", "Blue", "Yellow", "Green (JP)",
            "Gold", "Silver", "Crystal",
            "Ruby", "Sapphire", "Emerald", "FireRed", "LeafGreen",
            "Diamond", "Pearl", "Platinum", "HeartGold", "SoulSilver",
            "Black", "White", "Black 2", "White 2",
            "X", "Y", "Omega Ruby", "Alpha Sapphire",
            "Sun", "Moon", "Ultra Sun", "Ultra Moon", "Let's Go Pikachu", "Let's Go Eevee",
            "Sword", "Shield", "Brilliant Diamond", "Shining Pearl", "Legends: Arceus",
            "Scarlet", "Violet", "Legends: Z-A",
        };
}
