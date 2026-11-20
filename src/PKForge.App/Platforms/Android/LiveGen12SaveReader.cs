using PKForge.App.Services;
using PKForge.Domain;
using PKHeX.Core;

namespace PKForge.App.Platforms.Android;

/// <summary>Reads read-only companion data from validated GB/GBC battery saves.</summary>
internal static class LiveGen12SaveReader
{
    private const int MaximumPartySize = 6;

    public static LiveCompanionSnapshot? Read(
        byte[] saveRam,
        int generation,
        string gameTitle,
        string gameCode,
        string romFileName)
    {
        ArgumentNullException.ThrowIfNull(saveRam);
        if (generation is not (1 or 2) || !HasSaveData(saveRam)) return null;
        if (generation == 1 && saveRam.Length != SaveUtil.SIZE_G1RAW) return null;
        if (generation == 2 && saveRam.Length is not (SaveUtil.SIZE_G2RAW_U or SaveUtil.SIZE_G2RAW_J)) return null;

        try
        {
            var language = DetectLanguage($"{gameTitle} {gameCode} {romFileName}");
            var version = DetectVersion(generation, $"{gameTitle} {romFileName}");
            SaveFile save = generation == 1
                ? new SAV1(saveRam, language, version)
                : new SAV2(saveRam, language, version);
            if (!save.ChecksumsValid || !save.IsVersionValid() || save.PartyCount is < 0 or > MaximumPartySize)
                return null;

            var maxSpecies = generation == 1 ? 151 : 251;
            var dexCaught = 0;
            var dexSeen = 0;
            for (ushort species = 1; species <= maxSpecies; species++)
            {
                if (save.GetCaught(species)) dexCaught++;
                if (save.GetSeen(species)) dexSeen++;
            }

            var languageCode = LanguageCode(language);
            var party = ReadParty(save, maxSpecies, language);
            var bag = ReadBag(save, languageCode);
            return new LiveCompanionSnapshot(
                gameTitle, gameCode, dexCaught, dexSeen, maxSpecies,
                party.Count, Math.Max(0, save.PlayedHours), Math.Clamp(save.PlayedMinutes, 0, 59),
                party, bag);
        }
        catch (Exception error) when (error is ArgumentException or IndexOutOfRangeException or OverflowException)
        {
            // A partially written or malformed battery image must never be shown as game data.
            return null;
        }
    }

    private static IReadOnlyList<GameCompanionPartyMember> ReadParty(SaveFile save, int maxSpecies, LanguageID language)
    {
        var party = new List<GameCompanionPartyMember>(save.PartyCount);
        for (var index = 0; index < save.PartyCount; index++)
        {
            var pokemon = save.GetPartySlotAtIndex(index);
            if (!pokemon.ChecksumValid || (pokemon.Species == 0 && !pokemon.IsEgg) || pokemon.Species > maxSpecies)
                continue;

            var speciesName = pokemon.IsEgg
                ? AppStrings.CompanionEgg
                : SpeciesName.GetSpeciesNameGeneration(pokemon.Species, (int)language, save.Generation);
            var nickname = pokemon.IsEgg || string.IsNullOrWhiteSpace(pokemon.Nickname)
                ? speciesName
                : pokemon.Nickname;
            party.Add(new GameCompanionPartyMember(
                pokemon.Species,
                speciesName,
                nickname,
                pokemon.IsEgg ? 0 : pokemon.CurrentLevel,
                Math.Max(0, pokemon.Stat_HPCurrent),
                Math.Max(0, pokemon.Stat_HPMax),
                StatusName(pokemon.Status_Condition),
                pokemon.IsEgg,
                pokemon.Form,
                pokemon.IsShiny));
        }

        return party;
    }

    private static IReadOnlyList<GameCompanionBagPocket> ReadBag(SaveFile save, string languageCode)
    {
        var itemNames = GameInfo.GetStrings(languageCode).GetItemStrings(save.Context, save.Version);
        var pockets = new List<GameCompanionBagPocket>();
        foreach (var pouch in save.Inventory.Pouches)
        {
            var items = pouch.Items
                .Where(item => item.Count > 0 && item.Index > 0 && item.Index < itemNames.Length)
                .Select(item => new GameCompanionBagItem(item.Index, itemNames[item.Index], item.Count))
                .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                .ToArray();
            if (items.Length == 0) continue;

            pockets.Add(new GameCompanionBagPocket(
                pouch.Type.ToString(),
                PocketName(pouch.Type),
                items));
        }

        return pockets;
    }

    private static GameVersion DetectVersion(int generation, string identity)
    {
        if (generation == 1)
            return identity.Contains("yellow", StringComparison.OrdinalIgnoreCase)
                ? GameVersion.YW
                : identity.Contains("red", StringComparison.OrdinalIgnoreCase)
                    || identity.Contains("blue", StringComparison.OrdinalIgnoreCase)
                    ? GameVersion.RB
                    : GameVersion.RBY;

        return identity.Contains("crystal", StringComparison.OrdinalIgnoreCase)
            ? GameVersion.C
            : GameVersion.GS;
    }

    private static LanguageID DetectLanguage(string identity)
    {
        if (ContainsAny(identity, "japan", "japanese", "jpn", "japon")) return LanguageID.Japanese;
        if (ContainsAny(identity, "korea", "korean", "kor")) return LanguageID.Korean;
        if (ContainsAny(identity, "french", "france", "(fr)", "_fr", "-fr")) return LanguageID.French;
        if (ContainsAny(identity, "german", "(de)", "_de", "-de")) return LanguageID.German;
        if (ContainsAny(identity, "italian", "(it)", "_it", "-it")) return LanguageID.Italian;
        if (ContainsAny(identity, "spanish", "(es)", "_es", "-es")) return LanguageID.Spanish;
        return LanguageID.English;
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static string LanguageCode(LanguageID language) => language switch
    {
        LanguageID.French => "fr",
        LanguageID.German => "de",
        LanguageID.Italian => "it",
        LanguageID.Spanish => "es",
        LanguageID.Japanese => "ja",
        LanguageID.Korean => "ko",
        _ => "en",
    };

    private static bool HasSaveData(ReadOnlySpan<byte> saveRam)
    {
        foreach (var value in saveRam)
            if (value is not (0 or 0xFF)) return true;
        return false;
    }

    private static string PocketName(InventoryType type) => type switch
    {
        InventoryType.Items => AppStrings.CompanionPocketItems,
        InventoryType.KeyItems => AppStrings.CompanionPocketKeyItems,
        InventoryType.Balls => AppStrings.CompanionPocketBalls,
        InventoryType.TMHMs => AppStrings.CompanionPocketMachines,
        InventoryType.Berries => AppStrings.CompanionPocketBerries,
        _ => type.ToString().ToUpperInvariant(),
    };

    private static string StatusName(int status)
    {
        if ((status & 0x80) != 0) return "TOX";
        if ((status & 0x40) != 0) return "PAR";
        if ((status & 0x20) != 0) return AppStrings.Fr ? "GEL" : "FRZ";
        if ((status & 0x10) != 0) return AppStrings.Fr ? "BRÛ" : "BRN";
        if ((status & 0x08) != 0) return "PSN";
        return (status & 0x07) != 0 ? "SLP" : string.Empty;
    }
}
