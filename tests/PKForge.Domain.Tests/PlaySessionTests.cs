using PKForge.Domain;
using Xunit;

namespace PKForge.Domain.Tests;

public sealed class SessionClockTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
    private static DateTimeOffset At(int minutes) => T0.AddMinutes(minutes);

    [Fact]
    public void StartFromNothingOpensAnActiveSession()
    {
        var s = SessionClock.Start(null, "/roms/emerald.gba", "Pokémon Emerald", T0);
        Assert.Equal(SessionState.Active, s.State);
        Assert.Equal(T0, s.ActiveSinceUtc);
        Assert.Equal(0, s.TotalSeconds);
    }

    [Fact]
    public void CloseBanksTheWholeStretchIntoTheTotal()
    {
        var s = SessionClock.Start(null, "/roms/emerald.gba", "Emerald", T0);
        var closed = SessionClock.Close(s, At(45)); // 45 minutes of play
        Assert.Equal(SessionState.Idle, closed.State);
        Assert.Null(closed.ActiveSinceUtc);
        Assert.Equal(45 * 60, closed.TotalSeconds);
        Assert.Equal(At(45), closed.LastClosedUtc);
    }

    [Fact]
    public void PauseResumeKeepsTheTotalAndRestartsTheClock()
    {
        var s = SessionClock.Start(null, "/k", "G", T0);
        var paused = SessionClock.Pause(s, At(10)); // banked 10 min
        Assert.Equal(SessionState.Paused, paused.State);
        Assert.Equal(600, paused.TotalSeconds);
        Assert.Equal(TimeSpan.Zero, paused.SessionElapsed(At(20))); // paused: nothing accrues

        var resumed = SessionClock.Resume(paused, At(20));
        Assert.Equal(SessionState.Active, resumed.State);
        Assert.Equal(600, resumed.TotalSeconds); // total kept
        Assert.Equal(TimeSpan.FromMinutes(5), resumed.SessionElapsed(At(25))); // 5 min since resume

        var closed = SessionClock.Close(resumed, At(25));
        Assert.Equal(600 + 300, closed.TotalSeconds); // 15 min total
    }

    [Fact]
    public void DoubleCloseIsANoOpAndNothingMeasuresAfterClose()
    {
        var s = SessionClock.Start(null, "/k", "G", T0);
        var closed = SessionClock.Close(s, At(30));
        var closedAgain = SessionClock.Close(closed, At(90)); // 60 more minutes pass
        Assert.Equal(closed, closedAgain); // unchanged: no measuring after Stop
        Assert.Equal(1800, closedAgain.TotalSeconds);
        Assert.Equal(TimeSpan.Zero, closedAgain.SessionElapsed(At(120)));
        Assert.Equal(TimeSpan.FromSeconds(1800), closedAgain.GrandTotal(At(120)));
    }

    [Fact]
    public void PauseOnIdleAndResumeOnActiveAreNoOps()
    {
        var idle = SessionClock.Close(null, T0);
        Assert.Equal(SessionState.Idle, SessionClock.Pause(idle, At(5)).State);

        var active = SessionClock.Start(null, "/k", "G", T0);
        Assert.Equal(active, SessionClock.Resume(active, At(5))); // already active: no restart
        Assert.Equal(T0, SessionClock.Resume(active, At(5)).ActiveSinceUtc);
    }

    [Fact]
    public void StartOnActiveIsANoOp()
    {
        var s = SessionClock.Start(null, "/k", "G", T0);
        Assert.Equal(s, SessionClock.Start(s, "/k", "G", At(10))); // clock keeps its original start
    }

    [Fact]
    public void SessionsArePerGameAndAccumulateAcrossRuns()
    {
        var a = SessionClock.Close(SessionClock.Start(null, "/roms/a.gba", "A", T0), At(60)); // 1 h
        var again = SessionClock.Close(SessionClock.Start(a, "/roms/a.gba", "A", At(120)), At(150)); // +30 min
        Assert.Equal(5400, again.TotalSeconds); // 1 h 30

        var b = SessionClock.Close(SessionClock.Start(null, "/roms/b.nds", "B", T0), At(5));
        Assert.Equal(300, b.TotalSeconds); // independent ledger
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(65, "1:05")]
    [InlineData(3600, "1:00:00")]
    [InlineData(3661, "1:01:01")]
    [InlineData(36000 + 599, "10:09:59")]
    public void DurationFormatsAsHmmss(long seconds, string expected) =>
        Assert.Equal(expected, PlaySessionStats.FormatDuration(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void SessionElapsedIgnoresClockSkewBackwards()
    {
        var s = SessionClock.Start(null, "/k", "G", T0);
        Assert.Equal(TimeSpan.Zero, s.SessionElapsed(T0.AddMinutes(-5))); // now < since -> zero, never negative
    }
}

public sealed class PlaySessionStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"playsessions-{Guid.NewGuid():N}.json");

    private static PlaySessionStats Stats(string key, long seconds) =>
        new(key, "Game " + key, seconds, SessionState.Idle, null, new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void UpsertFindAndListPerGame()
    {
        var store = new Infrastructure.JsonPlaySessionStore(_path);
        store.Set(Stats("/roms/a.gba", 3600));
        store.Set(Stats("/roms/b.nds", 120));
        store.Set(Stats("/roms/a.gba", 5400)); // upsert

        Assert.Equal(5400, store.Find("/roms/a.gba")!.TotalSeconds);
        Assert.Equal(120, store.Find("/roms/b.nds")!.TotalSeconds);
        Assert.Null(store.Find("/roms/c.gba"));
        Assert.Equal(2, store.GetAll().Count);
    }

    [Fact]
    public void SurvivesAReopenLikeAnAppRestart()
    {
        var store = new Infrastructure.JsonPlaySessionStore(_path);
        store.Set(Stats("/roms/a.gba", 5400));
        Assert.Equal(5400, new Infrastructure.JsonPlaySessionStore(_path).Find("/roms/a.gba")!.TotalSeconds);
    }

    [Fact]
    public void CorruptFileReadsAsEmpty()
    {
        File.WriteAllText(_path, "{ nope");
        Assert.Empty(new Infrastructure.JsonPlaySessionStore(_path).GetAll());
    }

    [Fact]
    public void NullPathKeepsTheLedgerInMemory()
    {
        var store = new Infrastructure.JsonPlaySessionStore(null);
        store.Set(Stats("/k", 42));
        Assert.Equal(42, store.Find("/k")!.TotalSeconds);
    }

    public void Dispose()
    {
        try { if (File.Exists(_path)) File.Delete(_path); } catch { }
        try { if (File.Exists(_path + ".tmp")) File.Delete(_path + ".tmp"); } catch { }
    }
}
