using PKForge.Domain;
using Xunit;

namespace PKForge.Domain.Tests;

public sealed class RestorePointRulesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 19, 16, 0, 0, TimeSpan.Zero);

    private static BackupInfo Info(string id, int minutes, string? documentId = "/saves/emerald.sav", string format = "SAV3", int gen = 3, string? name = "emerald.sav") =>
        new(id, T0.AddMinutes(minutes), "sha-" + id, name, format, gen, 65536, "change", documentId);

    private static SaveSnapshot Open(string format = "SAV3", int gen = 3, string? name = "emerald.sav") =>
        new(format, gen, ReadOnlyMemory<byte>.Empty, [], name);

    [Fact]
    public void ForGameReturnsNewestFirst()
    {
        var all = new[] { Info("old", 5), Info("new", 50), Info("mid", 20) };
        var points = RestorePointRules.ForGame(all, "/saves/emerald.sav", Open());

        Assert.Equal(["new", "mid", "old"], points.Select(p => p.BackupId).ToArray());
        Assert.All(points, p => Assert.StartsWith("sha-", p.Sha256));
    }

    [Fact]
    public void ForGameKeepsOnlyTheSavesOwnPoints()
    {
        var all = new[]
        {
            Info("mine", 10, "/saves/emerald.sav"),
            Info("other", 20, "/saves/diamond.sav", "SAV4", 4, "diamond.sav"),
        };
        var points = RestorePointRules.ForGame(all, "/saves/emerald.sav", Open());
        Assert.Equal(["mine"], points.Select(p => p.BackupId).ToArray());
    }

    [Fact]
    public void ForGameWithoutOpenSnapshotMatchesOnDocumentIdAlone()
    {
        var all = new[]
        {
            Info("mine", 10, "/saves/emerald.sav"),
            Info("legacy-no-doc", 20, null), // pre-documentId point: not attributable -> excluded
            Info("other", 30, "/saves/diamond.sav"),
        };
        var points = RestorePointRules.ForGame(all, "/saves/emerald.sav", open: null);
        Assert.Equal(["mine"], points.Select(p => p.BackupId).ToArray());
    }

    [Fact]
    public void LegacyPointsWithoutDocumentIdStillMatchByFormatGenerationAndName()
    {
        var all = new[] { Info("legacy", 10, null, "SAV3", 3, "emerald.sav"), Info("other", 20, null, "SAV4", 4, "diamond.sav") };
        var points = RestorePointRules.ForGame(all, "/saves/emerald.sav", Open());
        Assert.Equal(["legacy"], points.Select(p => p.BackupId).ToArray());
    }

    [Fact]
    public void OrderNewestFirstIsStableForEqualTimestamps()
    {
        var a = new CompanionRestorePoint("a", T0);
        var b = new CompanionRestorePoint("b", T0);
        var c = new CompanionRestorePoint("c", T0.AddMinutes(1));
        Assert.Equal(["c", "a", "b"], RestorePointRules.OrderNewestFirst([a, b, c]).Select(p => p.BackupId).ToArray());
    }

    [Theory]
    [InlineData(true, RestorePointKind.Manual, "Point de restauration · 2026-11-19 16:50")]
    [InlineData(false, RestorePointKind.Manual, "Restore point · 2026-11-19 16:50")]
    [InlineData(true, RestorePointKind.SafetyBeforeRestore, "Sauvegarde de sécurité avant restauration · 2026-11-19 16:50")]
    [InlineData(false, RestorePointKind.SafetyBeforeRestore, "Safety backup before restore · 2026-11-19 16:50")]
    public void NamesAreLocalizedAndTimestampedInvariantly(bool french, RestorePointKind kind, string expected) =>
        Assert.Equal(expected, RestorePointRules.NameFor(kind, new DateTimeOffset(2026, 11, 19, 16, 50, 0, TimeSpan.Zero), french));

    [Fact]
    public void NameTimestampUsesUtcInvariantCulture()
    {
        // 23:05 UTC must not shift with the machine's timezone.
        var name = RestorePointRules.NameFor(RestorePointKind.Manual, new DateTimeOffset(2026, 1, 2, 23, 5, 0, TimeSpan.Zero), french: false);
        Assert.Contains("2026-01-02 23:05", name);
    }
}
