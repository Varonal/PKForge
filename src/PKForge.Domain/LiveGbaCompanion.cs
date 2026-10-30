namespace PKForge.Domain;

/// <summary>
/// The live, in-emulator companion (the PokéDaisy model): the GBA's save RAM is read
/// straight out of the running core, so the second screen tracks the game as it
/// plays — dex progress, party count, playtime — without waiting for a save flush.
/// All offsets are relative to the SRAM/Flash bus base 0x0E000000, in the sector
/// layout PKHeX's SAV3 parses (4 KiB sectors, sector ID at 0xFF4, active slot first).
/// </summary>
public static class LiveGbaCompanion
{
    /// <summary>The GBA cartridge save region (SRAM 64 KiB, Flash 128 KiB, mirrored at 0x0F000000).</summary>
    public const uint SaveBase = 0x0E000000;

    private const int SectorSize = 0x1000;
    private const int MainSectors = 14;
    private const int MainSize = MainSectors * SectorSize;
    private const int SectorIdOffset = 0xFF4;

    // The "small" sector (ID 0) holds the trainer block; the dex flags live at
    // PokeDex 0x18: caught at +0x10, seen at +0x44 (SAV3.GetCaught/GetSeen).
    private const int CaughtOffset = 0x18 + 0x10;
    private const int SeenOffset = 0x18 + 0x44;

    /// <summary>One live snapshot of the running game's companion data.</summary>
    public sealed record LiveCompanionSnapshot(
        string GameTitle,
        string GameCode,
        int DexCaught,
        int DexSeen,
        int DexTotal,
        int PartyCount,
        int PlaytimeHours,
        int PlaytimeMinutes)
    {
        public int DexPercent => DexTotal == 0 ? 0 : (int)Math.Round(DexCaught * 100.0 / DexTotal);
    }

    /// <summary>
    /// Reads the active save slot out of the core's memory and measures it.
    /// Returns null when the memory is unreadable or no small sector is found.
    /// </summary>
    public static LiveCompanionSnapshot? Read(IEmbeddedCore core, int maxSpeciesId)
    {
        ArgumentNullException.ThrowIfNull(core);
        if (!core.IsRunning || maxSpeciesId <= 0) return null;

        // Read both 128 KiB slots when the cart has them; slot 0 always exists.
        var both = new byte[MainSize * 2];
        if (!core.ReadBytes(SaveBase, both)) return null;

        var active = ActiveSlot(both);
        var baseOffset = active * MainSize;

        var small = SectorOf(both, baseOffset, 0);
        if (small is null) return null;

        var caught = 0;
        var seen = 0;
        for (var species = 1; species <= maxSpeciesId; species++)
        {
            var bit = species - 1;
            if (Flag(small, CaughtOffset + (bit >> 3), bit & 7)) caught++;
            if (Flag(small, SeenOffset + (bit >> 3), bit & 7)) seen++;
        }

        var large = SectorOf(both, baseOffset, 1);
        var partyCount = large is { Length: > 0x234 } ? Math.Min((int)large[0x234], 6) : 0;

        // Playtime: trainer block of the small sector, offset 0x0C = hours, 0x0D = minutes.
        var hours = small.Length > 0x0C ? small[0x0C] : (byte)0;
        var minutes = small.Length > 0x0D ? small[0x0D] : (byte)0;

        return new LiveCompanionSnapshot(
            core.GameTitle, core.GameCode, caught, seen, maxSpeciesId,
            partyCount, hours, minutes);
    }

    /// <summary>SAV3.GetActiveSlot: with both slots present, the one with the newer footer wins.</summary>
    private static int ActiveSlot(byte[] data)
    {
        var v0 = HasAllSectors(data, 0);
        var v1 = data.Length >= MainSize * 2 && HasAllSectors(data, MainSize);
        if (!v0) return v1 ? 1 : 0;
        if (!v1) return 0;
        // Footer: sector counter at 0xFFC of sector 0 (SAV3BlockDetection.CompareFooters).
        var c0 = ReadUInt32LE(data, 0xFFC);
        var c1 = ReadUInt32LE(data, MainSize + 0xFFC);
        return c1 > c0 ? 1 : 0;
    }

    private static bool HasAllSectors(byte[] data, int start)
    {
        if (start + MainSize > data.Length) return false;
        for (var ofs = start; ofs < start + MainSize; ofs += SectorSize)
        {
            var id = data[ofs + SectorIdOffset] | (data[ofs + SectorIdOffset + 1] << 8);
            if (id is < 0 or > 13) return false;
        }
        return true;
    }

    private static byte[]? SectorOf(byte[] data, int slotBase, int id)
    {
        for (var ofs = slotBase; ofs + SectorSize <= Math.Min(data.Length, slotBase + MainSize); ofs += SectorSize)
        {
            var sectorId = data[ofs + SectorIdOffset] | (data[ofs + SectorIdOffset + 1] << 8);
            if (sectorId == id) return data[ofs..(ofs + SectorSize)];
        }
        return null;
    }

    private static uint ReadUInt32LE(byte[] data, int offset) =>
        offset + 4 <= data.Length
            ? (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24))
            : 0;

    private static bool Flag(byte[] data, int offset, int bit) =>
        offset < data.Length && (data[offset] & (1 << bit)) != 0;
}
