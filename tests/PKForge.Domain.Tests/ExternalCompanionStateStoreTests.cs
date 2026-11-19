using PKForge.Domain;
using Xunit;

namespace PKForge.Domain.Tests;

public sealed class ExternalCompanionStateStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"extcompanion-{Guid.NewGuid():N}.json");

    private static ExternalCompanionState State() =>
        new("/storage/emulated/0/ROMs/nds/Pokemon - Diamond Version.nds",
            EmulatorKind.MelonDS, "Pokémon Diamond", "ADAE",
            new DateTimeOffset(2026, 11, 19, 16, 30, 0, TimeSpan.Zero));

    [Fact]
    public void StartsEmpty()
    {
        Assert.Null(new Infrastructure.JsonExternalCompanionStateStore(_path).Current);
    }

    [Fact]
    public void SetAndClearRoundTrips()
    {
        var store = new Infrastructure.JsonExternalCompanionStateStore(_path);
        store.Set(State());
        var read = store.Current;
        Assert.NotNull(read);
        Assert.Equal(EmulatorKind.MelonDS, read.Emulator);
        Assert.Equal("Pokémon Diamond", read.GameLabel);
        Assert.Equal("ADAE", read.GameCode);
        Assert.Equal("/storage/emulated/0/ROMs/nds/Pokemon - Diamond Version.nds", read.RomPath);

        store.Set(null);
        Assert.Null(store.Current);
    }

    [Fact]
    public void SurvivesAReopenLikeAnAppKill()
    {
        var store = new Infrastructure.JsonExternalCompanionStateStore(_path);
        store.Set(State());

        // A fresh instance over the same file (the app relaunched) reads the session back.
        var reopened = new Infrastructure.JsonExternalCompanionStateStore(_path).Current;
        Assert.NotNull(reopened);
        Assert.Equal(State(), reopened);
    }

    [Fact]
    public void CorruptFileReadsAsNoSession()
    {
        File.WriteAllText(_path, "{ not json");
        Assert.Null(new Infrastructure.JsonExternalCompanionStateStore(_path).Current);
    }

    [Fact]
    public void NullPathKeepsStateInMemory()
    {
        var store = new Infrastructure.JsonExternalCompanionStateStore(null);
        Assert.Null(store.Current);
        store.Set(State());
        Assert.NotNull(store.Current);
        store.Set(null);
        Assert.Null(store.Current);
    }

    public void Dispose()
    {
        try { if (File.Exists(_path)) File.Delete(_path); } catch { }
        try { if (File.Exists(_path + ".tmp")) File.Delete(_path + ".tmp"); } catch { }
    }
}
