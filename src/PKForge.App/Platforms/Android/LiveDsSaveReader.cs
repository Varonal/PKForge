using PKForge.App.Services;
using PKForge.Domain;
using PKHeX.Core;

namespace PKForge.App.Platforms.Android;

/// <summary>Reads companion data from checksum-valid Generation 4 and 5 battery saves.</summary>
internal static class LiveDsSaveReader
{
    private const int MaximumPartySize = 6;
    private const int MaximumGen4Species = 493;
    private const int MaximumGen5Species = 649;

    public static LiveCompanionSnapshot? Read(
        byte[] saveRam,
        int expectedGeneration,
        string gameTitle,
        string gameCode)
    {
        ArgumentNullException.ThrowIfNull(saveRam);
        if (expectedGeneration is not (4 or 5) || saveRam.Length != SaveUtil.SIZE_G4RAW)
            return null;

        try
        {
            if (!SaveUtil.TryGetSaveFile(saveRam.AsMemory(), out var save)
                || save is not (SAV4 or SAV5)
                || save.Generation != expectedGeneration
                || !save.ChecksumsValid
                || save.PartyCount is < 0 or > MaximumPartySize)
                return null;

            var maxSpecies = expectedGeneration == 4 ? MaximumGen4Species : MaximumGen5Species;
            var language = DetectLanguage(save.Language, gameCode);
            var party = ReadParty(save, maxSpecies, language);
            var bag = ReadBag(save, language);
            var caught = 0;
            var seen = 0;
            for (ushort species = 1; species <= maxSpecies; species++)
            {
                if (save.GetCaught(species)) caught++;
                if (save.GetSeen(species)) seen++;
            }

            return new LiveCompanionSnapshot(
                gameTitle,
                gameCode,
                caught,
                seen,
                maxSpecies,
                party.Count,
                Math.Max(0, save.PlayedHours),
                Math.Clamp(save.PlayedMinutes, 0, 59),
                party,
                bag);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException
            or IndexOutOfRangeException or OverflowException or NotSupportedException or NullReferenceException)
        {
            // The live image may have been captured during a game save or contain damaged blocks.
            return null;
        }
    }

    private static IReadOnlyList<GameCompanionPartyMember> ReadParty(
        SaveFile save,
        int maxSpecies,
        LanguageID saveLanguage)
    {
        var party = new List<GameCompanionPartyMember>(save.PartyCount);
        for (var index = 0; index < save.PartyCount; index++)
        {
            var pokemon = save.GetPartySlotAtIndex(index);
            if (!pokemon.ChecksumValid
                || pokemon.Species > maxSpecies
                || (pokemon.Species == 0 && !pokemon.IsEgg))
                continue;

            var language = IsSupportedLanguage(pokemon.Language)
                ? (LanguageID)pokemon.Language
                : saveLanguage;
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

    private static IReadOnlyList<GameCompanionBagPocket> ReadBag(SaveFile save, LanguageID language)
    {
        var itemNames = GameInfo.GetStrings(LanguageCode(language)).GetItemStrings(save.Context, save.Version);
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

    private static LanguageID DetectLanguage(int saveLanguage, string gameCode)
    {
        if (IsSupportedLanguage(saveLanguage)) return (LanguageID)saveLanguage;
        if (gameCode.Length > 0)
        {
            var codeLanguage = char.ToUpperInvariant(gameCode[^1]) switch
            {
                'J' => LanguageID.Japanese,
                'E' => LanguageID.English,
                'F' => LanguageID.French,
                'D' => LanguageID.German,
                'I' => LanguageID.Italian,
                'S' => LanguageID.Spanish,
                'K' => LanguageID.Korean,
                _ => (LanguageID)0,
            };
            if ((int)codeLanguage != 0) return codeLanguage;
        }

        return LanguageID.English;
    }

    private static bool IsSupportedLanguage(int language) => language is 1 or 2 or 3 or 4 or 5 or 7 or 8;

    private static string LanguageCode(LanguageID language) => language switch
    {
        LanguageID.Japanese => "ja",
        LanguageID.French => "fr",
        LanguageID.German => "de",
        LanguageID.Italian => "it",
        LanguageID.Spanish => "es",
        LanguageID.Korean => "ko",
        _ => "en",
    };

    private static string PocketName(InventoryType type) => type switch
    {
        InventoryType.Items => AppStrings.CompanionPocketItems,
        InventoryType.KeyItems => AppStrings.CompanionPocketKeyItems,
        InventoryType.Medicine => AppStrings.CompanionPocketMedicine,
        InventoryType.Balls => AppStrings.CompanionPocketBalls,
        InventoryType.TMHMs => AppStrings.CompanionPocketMachines,
        InventoryType.Berries => AppStrings.CompanionPocketBerries,
        InventoryType.BattleItems => AppStrings.CompanionPocketBattleItems,
        InventoryType.MailItems => AppStrings.CompanionPocketMail,
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
