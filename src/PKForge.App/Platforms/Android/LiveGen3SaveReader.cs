using PKForge.App.Services;
using PKForge.Domain;
using PKHeX.Core;

namespace PKForge.App.Platforms.Android;

/// <summary>Reads read-only companion views from a validated Gen 3 battery save.</summary>
internal static class LiveGen3SaveReader
{
    private const int MaximumPartySize = 6;
    private const int MaximumSpeciesId = 386;

    public static LiveGen3SaveData? Read(byte[] saveRam, string gameCode)
    {
        ArgumentNullException.ThrowIfNull(saveRam);
        var save = OpenValidatedSave(saveRam, gameCode);
        if (save is null)
            return null;

        return new LiveGen3SaveData(ReadParty(save), ReadBag(save, gameCode));
    }

    private static SAV3? OpenValidatedSave(byte[] saveRam, string gameCode)
    {
        if (!SAV3.IsAllMainSectorsPresent(saveRam, 0, out _)
            && !SAV3.IsAllMainSectorsPresent(saveRam, 1, out _))
            return null;

        var save = CreateSave(saveRam, gameCode);
        return save is { ChecksumsValid: true } ? save : null;
    }

    private static IReadOnlyList<GameCompanionPartyMember> ReadParty(SAV3 save)
    {
        if (save.PartyCount is < 0 or > MaximumPartySize)
            return [];

        var party = new List<GameCompanionPartyMember>(save.PartyCount);
        for (var index = 0; index < save.PartyCount; index++)
        {
            try
            {
                var pokemon = save.GetPartySlotAtIndex(index);
                if (!pokemon.ChecksumValid || pokemon.Species > MaximumSpeciesId
                    || (pokemon.Species == 0 && !pokemon.IsEgg))
                    continue;

                var language = pokemon.Language is >= 1 and <= 5 or 7
                    ? pokemon.Language
                    : (int)LanguageID.English;
                var speciesName = pokemon.IsEgg
                    ? AppStrings.CompanionEgg
                    : SpeciesName.GetSpeciesNameGeneration(pokemon.Species, language, generation: 3);
                var nickname = pokemon.IsEgg || language != pokemon.Language
                    ? speciesName
                    : string.IsNullOrWhiteSpace(pokemon.Nickname) ? speciesName : pokemon.Nickname;
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
            catch (ArgumentOutOfRangeException)
            {
                // A save sector can be changing during the game's save operation.
                return [];
            }
        }

        return party;
    }

    private static IReadOnlyList<GameCompanionBagPocket> ReadBag(SAV3 save, string gameCode)
    {
        var strings = GameInfo.GetStrings(GetLanguageCode(gameCode));
        var itemNames = strings.GetItemStrings(EntityContext.Gen3, save.Version);
        var pockets = new List<GameCompanionBagPocket>();

        foreach (var pouch in save.Inventory.Pouches)
        {
            // PC storage is separate from the bag the player carries.
            if (pouch.Type == InventoryType.PCItems)
                continue;

            var items = pouch.Items
                .Where(item => item.Count > 0 && item.Index > 0 && item.Index < itemNames.Length)
                .Select(item => new GameCompanionBagItem(item.Index, itemNames[item.Index], item.Count))
                .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                .ToArray();
            if (items.Length == 0)
                continue;

            pockets.Add(new GameCompanionBagPocket(
                pouch.Type.ToString(),
                PocketName(pouch.Type),
                items));
        }

        return pockets;
    }

    private static SAV3? CreateSave(byte[] saveRam, string gameCode)
    {
        if (gameCode.StartsWith("BPE", StringComparison.Ordinal))
            return new SAV3E(saveRam);

        if (gameCode.StartsWith("BPR", StringComparison.Ordinal))
            return new SAV3FRLG(saveRam) { Version = GameVersion.FR };

        if (gameCode.StartsWith("BPG", StringComparison.Ordinal))
            return new SAV3FRLG(saveRam) { Version = GameVersion.LG };

        if (gameCode.StartsWith("AXV", StringComparison.Ordinal))
            return new SAV3RS(saveRam) { Version = GameVersion.R };

        if (gameCode.StartsWith("AXP", StringComparison.Ordinal))
            return new SAV3RS(saveRam) { Version = GameVersion.S };

        return null;
    }

    private static string GetLanguageCode(string gameCode) => gameCode.Length >= 4
        ? char.ToUpperInvariant(gameCode[3]) switch
        {
            'F' => "fr",
            'I' => "it",
            'D' => "de",
            'S' => "es",
            'J' => "ja",
            _ => "en",
        }
        : "en";

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

internal sealed record LiveGen3SaveData(
    IReadOnlyList<GameCompanionPartyMember> Party,
    IReadOnlyList<GameCompanionBagPocket> Bag);
