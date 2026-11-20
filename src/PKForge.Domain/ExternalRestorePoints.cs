using System.Globalization;

namespace PKForge.Domain;

/// <summary>What kind of restore point a name is for.</summary>
public enum RestorePointKind
{
    /// <summary>A point the player asked for by name ("before the Elite Four").</summary>
    Manual,
    /// <summary>The automatic safety backup taken before a restore overwrites the live save.</summary>
    SafetyBeforeRestore,
}

/// <summary>
/// Pure rules for the external companion's restore points (US-005): which backups belong
/// to the running game's save (newest first), and how a new point is named and timestamped.
/// No I/O, no locale — the caller passes the UI language in, so everything is unit-testable.
/// </summary>
public static class RestorePointRules
{
    /// <summary>
    /// The restore points of one save, newest first. Matching uses
    /// <see cref="BackupInfo.BelongsTo"/> when the open snapshot is available; with only a
    /// document id, points that recorded it match directly.
    /// </summary>
    public static IReadOnlyList<CompanionRestorePoint> ForGame(
        IEnumerable<BackupInfo> all, string? documentId, SaveSnapshot? open)
    {
        ArgumentNullException.ThrowIfNull(all);
        return all
            .Where(info => open is not null
                ? info.BelongsTo(documentId ?? string.Empty, open)
                : info.DocumentId is { } own && string.Equals(own, documentId, StringComparison.Ordinal))
            .OrderByDescending(info => info.CreatedUtc)
            .Select(info => new CompanionRestorePoint(info.BackupId, info.CreatedUtc, info.Sha256))
            .ToList();
    }

    /// <summary>Orders restore points newest first (stable for equal timestamps: keeps input order).</summary>
    public static IReadOnlyList<CompanionRestorePoint> OrderNewestFirst(IEnumerable<CompanionRestorePoint> points) =>
        points.OrderByDescending(p => p.CreatedUtc).ToList();

    /// <summary>
    /// The display/change name of a new restore point, localized and timestamped:
    /// "Point de restauration · 2026-11-19 16:50" / "Restore point · 2026-11-19 16:50".
    /// The stamp is UTC and formatted invariantly so names sort and read the same everywhere.
    /// </summary>
    public static string NameFor(RestorePointKind kind, DateTimeOffset utc, bool french)
    {
        var stamp = utc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        return kind switch
        {
            RestorePointKind.Manual => french
                ? $"Point de restauration · {stamp}"
                : $"Restore point · {stamp}",
            RestorePointKind.SafetyBeforeRestore => french
                ? $"Sauvegarde de sécurité avant restauration · {stamp}"
                : $"Safety backup before restore · {stamp}",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }
}
