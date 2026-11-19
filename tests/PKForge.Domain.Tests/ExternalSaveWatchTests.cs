using PKForge.Domain;
using Xunit;

namespace PKForge.Domain.Tests;

public sealed class ExternalSaveWatchTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 19, 16, 0, 0, TimeSpan.Zero);

    private static SaveFileStamp Stamp(long length, int secondsOffset) =>
        new("/storage/emulated/0/ROMs/gba/emerald.srm", length, T0.AddSeconds(secondsOffset));

    [Fact]
    public void MissingFileIsReportedAndResetsTheWatch()
    {
        var watch = new ExternalSaveWatch();
        Assert.Equal(SaveWatchDecision.Missing, watch.Poll(null));
        Assert.Null(watch.LastPublished);
    }

    [Fact]
    public void FirstSightingIsPendingThenConfirmedStableOnTheNextIdenticalPoll()
    {
        var watch = new ExternalSaveWatch();
        var s1 = Stamp(65536, 10);

        Assert.Equal(SaveWatchDecision.PendingStable, watch.Poll(s1));
        Assert.Null(watch.LastPublished); // not published yet — the flush may still be mid-write

        Assert.Equal(SaveWatchDecision.ChangedStable, watch.Poll(s1));
        Assert.Equal(s1, watch.LastPublished);
    }

    [Fact]
    public void UnchangedFileIsNoChange()
    {
        var watch = new ExternalSaveWatch();
        var s1 = Stamp(65536, 10);
        watch.Poll(s1);
        watch.Poll(s1); // confirmed stable

        Assert.Equal(SaveWatchDecision.NoChange, watch.Poll(Stamp(65536, 10)));
    }

    [Fact]
    public void AChangeMidWriteNeedsTwoIdenticalReadsBeforeItCounts()
    {
        var watch = new ExternalSaveWatch();
        var s1 = Stamp(65536, 10);
        watch.Poll(s1);
        watch.Poll(s1); // stable, published

        var s2 = Stamp(65540, 20); // flush started
        Assert.Equal(SaveWatchDecision.PendingStable, watch.Poll(s2));
        Assert.Equal(s1, watch.LastPublished); // still the old one

        var s2b = Stamp(65544, 21); // flush still going, different again
        Assert.Equal(SaveWatchDecision.PendingStable, watch.Poll(s2b));
        Assert.Equal(s1, watch.LastPublished);

        Assert.Equal(SaveWatchDecision.ChangedStable, watch.Poll(s2b)); // now stable
        Assert.Equal(s2b, watch.LastPublished);
    }

    [Fact]
    public void SizeOnlyChangeCountsAsAChange()
    {
        var watch = new ExternalSaveWatch();
        var s1 = Stamp(65536, 10);
        watch.Poll(s1);
        watch.Poll(s1);

        var s2 = Stamp(65537, 10); // same mtime, different size
        Assert.Equal(SaveWatchDecision.PendingStable, watch.Poll(s2));
        Assert.Equal(SaveWatchDecision.ChangedStable, watch.Poll(s2));
    }

    [Fact]
    public void MtimeOnlyChangeCountsAsAChange()
    {
        var watch = new ExternalSaveWatch();
        var s1 = Stamp(65536, 10);
        watch.Poll(s1);
        watch.Poll(s1);

        var s2 = Stamp(65536, 11); // same size, newer mtime
        Assert.Equal(SaveWatchDecision.PendingStable, watch.Poll(s2));
        Assert.Equal(SaveWatchDecision.ChangedStable, watch.Poll(s2));
    }

    [Fact]
    public void SaveDisappearingThenReappearingStartsFresh()
    {
        var watch = new ExternalSaveWatch();
        var s1 = Stamp(65536, 10);
        watch.Poll(s1);
        watch.Poll(s1);
        Assert.Equal(s1, watch.LastPublished);

        Assert.Equal(SaveWatchDecision.Missing, watch.Poll(null));
        Assert.Null(watch.LastPublished); // reset by the disappearance

        var s2 = Stamp(65536, 30);
        Assert.Equal(SaveWatchDecision.PendingStable, watch.Poll(s2));
        Assert.Equal(SaveWatchDecision.ChangedStable, watch.Poll(s2));
    }

    [Fact]
    public void ResetClearsEverything()
    {
        var watch = new ExternalSaveWatch();
        watch.Poll(Stamp(1, 1));
        watch.Poll(Stamp(1, 1));
        Assert.NotNull(watch.LastPublished);

        watch.Reset();
        Assert.Null(watch.LastPublished);
        Assert.Equal(SaveWatchDecision.PendingStable, watch.Poll(Stamp(1, 1)));
    }
}

public sealed class ExternalSavePublishTests
{
    private static readonly DateTimeOffset Utc = new(2026, 11, 19, 16, 0, 0, TimeSpan.Zero);

    private static ExternalCompanionSnapshot Snap(DateTimeOffset at) =>
        new("Pokémon Emerald", "BPEE", 3, 10, 20, 386, 3, 5, 30, at);

    [Fact]
    public void PublishesANewNonNullSnapshotWhenTheStampMoved()
    {
        var stamp = new SaveFileStamp("/x.srm", 65536, Utc);
        var published = new SaveFileStamp("/x.srm", 65000, Utc.AddSeconds(-10));
        Assert.True(ExternalSavePublish.ShouldPublish(Snap(Utc), null, stamp, published));
    }

    [Fact]
    public void NullCandidateNeverReplacesTheLastGoodSnapshot()
    {
        var stamp = new SaveFileStamp("/x.srm", 65536, Utc);
        var lastGood = Snap(Utc.AddMinutes(-1));
        Assert.False(ExternalSavePublish.ShouldPublish(null, lastGood, stamp, null));
    }

    [Fact]
    public void SameStampDoesNotRepublish()
    {
        var stamp = new SaveFileStamp("/x.srm", 65536, Utc);
        Assert.False(ExternalSavePublish.ShouldPublish(Snap(Utc), Snap(Utc), stamp, stamp));
    }

    [Fact]
    public void NullStampNeverPublishes()
    {
        Assert.False(ExternalSavePublish.ShouldPublish(Snap(Utc), null, null, null));
    }
}
