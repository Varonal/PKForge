using PKForge.Domain;
using Xunit;

namespace PKForge.Domain.Tests;

public sealed class LiveGbaCompanionTests
{
    /// <summary>Minimal in-memory IEmbeddedCore holding a synthetic GBA save image.</summary>
    private sealed class FakeCore(byte[] sram, string title = "POKEMON EMER", string code = "BPEE") : IEmbeddedCore
    {
        public bool IsRunning => true;
        public string? RomPath => "/roms/emerald.gba";
        public string GameTitle => title;
        public string GameCode => code;
        public (int Width, int Height) VideoSize => (240, 160);

        public bool Load(string romPath, string? savePath = null) => true;
        public void RunFrame() { }
        public void CopyFrame(Span<uint> buffer) { }
        public void SetKeys(uint bitmask) { }
        public bool ReadBytes(uint address, Span<byte> destination)
        {
            if (address != LiveGbaCompanion.SaveBase) return false;
            if (destination.Length > sram.Length) return false;
            sram.AsSpan(0, destination.Length).CopyTo(destination);
            return true;
        }
        public uint? Read32(uint address) => 0;
        public bool SaveState(string path) => true;
        public bool LoadState(string path) => true;
        public void Dispose() { }
    }

    private const int Sector = 0x1000;
    private const int Main = 14 * Sector;

    /// <summary>Builds a 128 KiB SRAM image: sector 0 of slot 0 holds the dex flags + trainer block.</summary>
    private static byte[] SramWithDex(int caughtBits, int seenBits, byte partyCount = 3, byte hours = 12, byte minutes = 34)
    {
        var sram = new byte[Main * 2];
        // Sector ID at 0xFF4 of each sector.
        for (var s = 0; s < 14; s++)
        {
            sram[s * Sector + 0xFF4] = (byte)s;
            sram[Main + s * Sector + 0xFF4] = (byte)s;
        }
        // Sector 0 footer counter (slot comparison) — slot 0 newer than slot 1 by default.
        sram[0xFFC] = 0x02; // counter low byte = 2
        sram[Main + 0xFFC] = 0x01;
        // Dex flags: caught at 0x28, seen at 0x5C of the small sector.
        sram[0x28] = (byte)caughtBits;
        sram[0x5C] = (byte)seenBits;
        // Trainer block: playtime at 0x0C (hours) / 0x0D (minutes).
        sram[0x0C] = hours;
        sram[0x0D] = minutes;
        // Party count at 0x234 of sector 1 (large block).
        sram[Sector + 0x234] = partyCount;
        return sram;
    }

    [Fact]
    public void ReadsDexCaughtSeenPartyAndPlaytimeFromTheActiveSlot()
    {
        var sram = SramWithDex(caughtBits: 0b0000_0111, seenBits: 0b0000_1111, partyCount: 4, hours: 7, minutes: 55);
        var core = new FakeCore(sram);

        var snapshot = LiveGbaCompanion.Read(core, maxSpeciesId: 386);

        Assert.NotNull(snapshot);
        Assert.Equal(3, snapshot.DexCaught);   // bits 0,1,2 set
        Assert.Equal(4, snapshot.DexSeen);     // bits 0..3 set
        Assert.Equal(386, snapshot.DexTotal);
        Assert.Equal(4, snapshot.PartyCount);
        Assert.Equal(7, snapshot.PlaytimeHours);
        Assert.Equal(55, snapshot.PlaytimeMinutes);
        Assert.Equal("POKEMON EMER", snapshot.GameTitle);
        Assert.Equal("BPEE", snapshot.GameCode);
    }

    [Fact]
    public void PartyCountIsCappedAtSix()
    {
        var sram = SramWithDex(0, 0, partyCount: 200);
        var snapshot = LiveGbaCompanion.Read(new FakeCore(sram), 386);
        Assert.Equal(6, snapshot!.PartyCount);
    }

    [Fact]
    public void ReturnsNullWhenTheSaveRegionIsUnreadable()
    {
        // The bus read fails: nothing is copied, no snapshot.
        Assert.Null(LiveGbaCompanion.Read(new UnreadableCore(), 386));
    }

    private sealed class UnreadableCore : IEmbeddedCore
    {
        public bool IsRunning => true;
        public string? RomPath => null;
        public string GameTitle => "";
        public string GameCode => "BPEE";
        public (int Width, int Height) VideoSize => (240, 160);
        public bool Load(string romPath, string? savePath = null) => false;
        public void RunFrame() { }
        public void CopyFrame(Span<uint> buffer) { }
        public void SetKeys(uint bitmask) { }
        public bool ReadBytes(uint address, Span<byte> destination) => false;
        public uint? Read32(uint address) => null;
        public bool SaveState(string path) => false;
        public bool LoadState(string path) => false;
        public void Dispose() { }
    }

    [Fact]
    public void DexPercentIsCaughtOverTotal()
    {
        var sram = SramWithDex(caughtBits: 0xFF, seenBits: 0xFF);
        var snapshot = LiveGbaCompanion.Read(new FakeCore(sram), 386);
        Assert.Equal(8, snapshot!.DexCaught);
        Assert.True(snapshot.DexPercent >= 2 && snapshot.DexPercent <= 3); // 8/386 ≈ 2.07%
    }

    [Fact]
    public void ReturnsNullWhenNotRunning()
    {
        Assert.Null(LiveGbaCompanion.Read(new StoppedCore(), 386));
    }

    private sealed class StoppedCore : IEmbeddedCore
    {
        public bool IsRunning => false;
        public string? RomPath => null;
        public string GameTitle => "";
        public string GameCode => "BPEE";
        public (int Width, int Height) VideoSize => (0, 0);
        public bool Load(string romPath, string? savePath = null) => false;
        public void RunFrame() { }
        public void CopyFrame(Span<uint> buffer) { }
        public void SetKeys(uint bitmask) { }
        public bool ReadBytes(uint address, Span<byte> destination) => false;
        public uint? Read32(uint address) => null;
        public bool SaveState(string path) => false;
        public bool LoadState(string path) => false;
        public void Dispose() { }
    }
}
