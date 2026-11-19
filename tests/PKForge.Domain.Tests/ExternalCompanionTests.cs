using System.Reflection;
using PKForge.Domain;
using Xunit;

namespace PKForge.Domain.Tests;

public sealed class ExternalCompanionSnapshotTests
{
    private static readonly DateTimeOffset Utc = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private static SaveSnapshot SnapshotWithParty(int generation = 3, params int?[] partySpecies)
    {
        var slots = new List<SlotSummary> { new(0, 0, null, null, false, true) }; // one empty box slot
        for (var i = 0; i < partySpecies.Length; i++)
            slots.Add(new SlotSummary(-1, i, partySpecies[i], null, false, true));
        return new SaveSnapshot("SAV3", generation, ReadOnlyMemory<byte>.Empty, slots, "Pokemon - Emerald Version.sav");
    }

    private static DexProgress Dex(int seen, int caught, int total) => new(seen, caught, total);

    private static TrainerStats Stats(bool supports, int hours = 0, int minutes = 0) =>
        new(supports, hours, minutes, 999, false, 0, 0, false, 0, 0);

    [Fact]
    public void MapsAValidParsedSaveToASnapshot()
    {
        var snap = ExternalCompanionSnapshotFactory.FromFacts(
            SnapshotWithParty(3, 25, 16, 0, 4, 0, 0), // 3 party mons (species > 0), 3 empty slots
            Dex(120, 80, 386),
            Stats(supports: true, hours: 42, minutes: 7),
            "Pokémon Emerald", "BPEE", Utc);

        Assert.NotNull(snap);
        Assert.Equal("Pokémon Emerald", snap.GameLabel);
        Assert.Equal("BPEE", snap.GameCode);
        Assert.Equal(3, snap.Generation);
        Assert.Equal(80, snap.DexCaught);
        Assert.Equal(120, snap.DexSeen);
        Assert.Equal(386, snap.DexTotal);
        Assert.Equal(3, snap.PartyCount);
        Assert.Equal(42, snap.PlaytimeHours);
        Assert.Equal(7, snap.PlaytimeMinutes);
        Assert.Equal(Utc, snap.SnapshotUtc);
        Assert.Equal(21, snap.DexPercent); // round(80*100/386) = 21
    }

    [Fact]
    public void NullSnapshotMeansUnrecognizedSave()
    {
        Assert.Null(ExternalCompanionSnapshotFactory.FromFacts(
            null, Dex(1, 1, 1), Stats(true), "X", "", Utc));
    }

    [Fact]
    public void MissingDexOrStatsDegradeToZeroInsteadOfNull()
    {
        // A save that parses but whose dex/stats the engine could not read still yields
        // a snapshot — the companion shows "no data" rather than disappearing.
        var snap = ExternalCompanionSnapshotFactory.FromFacts(
            SnapshotWithParty(1), null, null, "Pokémon Red", "", Utc);

        Assert.NotNull(snap);
        Assert.Equal(0, snap.DexCaught);
        Assert.Equal(0, snap.DexTotal);
        Assert.Equal(0, snap.DexPercent);
        Assert.Equal(0, snap.PartyCount);
        Assert.Equal(0, snap.PlaytimeHours);
        Assert.Equal(0, snap.PlaytimeMinutes);
    }

    [Fact]
    public void PlaytimeIsZeroWhenTheFormatHasNoCounter()
    {
        var snap = ExternalCompanionSnapshotFactory.FromFacts(
            SnapshotWithParty(2, 152), Dex(10, 5, 251), Stats(supports: false, hours: 99, minutes: 59),
            "Pokémon Crystal", "", Utc);

        Assert.NotNull(snap);
        Assert.Equal(0, snap.PlaytimeHours);
        Assert.Equal(0, snap.PlaytimeMinutes);
    }

    [Fact]
    public void PartyCountIsClampedToSix()
    {
        var snap = ExternalCompanionSnapshotFactory.FromFacts(
            SnapshotWithParty(3, 1, 2, 3, 4, 5, 6, 7, 8), // 8 party entries (corrupt/edge)
            Dex(0, 0, 0), Stats(true), "X", "", Utc);

        Assert.NotNull(snap);
        Assert.Equal(6, snap.PartyCount);
    }

    [Fact]
    public void FromSessionReturnsNullForNullSession()
    {
        Assert.Null(ExternalCompanionSnapshotFactory.FromSession(null, "X", "BPEE", Utc));
    }

    [Fact]
    public void FromSessionSwallowsAnUnreadableSave()
    {
        // Every extraction throws (save not recognized): the factory degrades to null.
        var session = SessionProxy.Create(throwing: true);
        Assert.Null(ExternalCompanionSnapshotFactory.FromSession(session, "X", "BPEE", Utc));
    }

    [Fact]
    public void FromSessionMapsThroughTheExtractedFacts()
    {
        var session = SessionProxy.Create(
            snapshot: SnapshotWithParty(4, 393, 25),
            dex: Dex(300, 150, 493),
            stats: Stats(true, 100, 30),
            gameNames: ["Pokémon Diamond"]);

        var snap = ExternalCompanionSnapshotFactory.FromSession(session, gameCode: "ADAE", snapshotUtc: Utc);

        Assert.NotNull(snap);
        Assert.Equal("Pokémon Diamond", snap.GameLabel); // falls back to GameNames[0]
        Assert.Equal("ADAE", snap.GameCode);
        Assert.Equal(4, snap.Generation);
        Assert.Equal(150, snap.DexCaught);
        Assert.Equal(300, snap.DexSeen);
        Assert.Equal(493, snap.DexTotal);
        Assert.Equal(2, snap.PartyCount);
        Assert.Equal(100, snap.PlaytimeHours);
        Assert.Equal(30, snap.PlaytimeMinutes);
        Assert.Equal(Utc, snap.SnapshotUtc);
    }

    /// <summary>
    /// A DispatchProxy over ISaveEngineSession (107 members — never hand-faked): only the
    /// members the factory reads return values; everything else throws, which doubles as
    /// the "unreadable save" case when <paramref name="throwing"/> is set.
    /// </summary>
    private class SessionProxy : DispatchProxy
    {
        private SaveSnapshot? _snapshot;
        private DexProgress _dex = new(0, 0, 0);
        private TrainerStats _stats = new(false, 0, 0, 0, false, 0, 0, false, 0, 0);
        private IReadOnlyList<string> _gameNames = [];
        private bool _throwing;

        public static ISaveEngineSession Create(
            bool throwing = false,
            SaveSnapshot? snapshot = null,
            DexProgress? dex = null,
            TrainerStats? stats = null,
            IReadOnlyList<string>? gameNames = null)
        {
            var proxy = Create<ISaveEngineSession, SessionProxy>();
            // DispatchProxy.Create returns T, but the runtime object is the proxy class;
            // cast through object because T (the interface) is not the proxy type.
            var p = (SessionProxy)(object)proxy;
            p._throwing = throwing;
            p._snapshot = snapshot;
            p._dex = dex ?? new DexProgress(0, 0, 0);
            p._stats = stats ?? new TrainerStats(false, 0, 0, 0, false, 0, 0, false, 0, 0);
            p._gameNames = gameNames ?? [];
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (_throwing) throw new InvalidOperationException("save not recognized");
            // Property getters arrive as get_Xxx; map them to their property name so
            // nameof() comparisons work for both getters and methods.
            var name = targetMethod?.Name is { } n && n.StartsWith("get_", StringComparison.Ordinal)
                ? n["get_".Length..]
                : targetMethod?.Name;
            return name switch
            {
                nameof(ISaveEngineSession.Snapshot) => _snapshot,
                nameof(ISaveEngineSession.GetDexProgress) => _dex,
                nameof(ISaveEngineSession.GetTrainerStats) => _stats,
                nameof(ISaveEngineSession.GameNames) => _gameNames,
                nameof(ISaveEngineSession.Generation) => _snapshot?.Generation ?? 0,
                nameof(IDisposable.Dispose) => null,
                _ => throw new NotSupportedException(name),
            };
        }
    }
}
