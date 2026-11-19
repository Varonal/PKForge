namespace PKForge.Domain;

/// <summary>
/// One stat read of the watched save file (the App fills it from FileInfo; the Domain
/// never touches the disk). Two reads with the same stamp mean the emulator finished
/// flushing — that is the debounce signal before parsing.
/// </summary>
/// <param name="Path">The resolved save path being watched.</param>
/// <param name="Length">File size in bytes.</param>
/// <param name="LastWriteUtc">Last write time (mtime), UTC.</param>
public sealed record SaveFileStamp(string Path, long Length, DateTimeOffset LastWriteUtc)
{
    /// <summary>True when the size and mtime moved since <paramref name="other"/> — the flush signal.</summary>
    public bool DiffersFrom(SaveFileStamp? other) =>
        other is null || other.Length != Length || other.LastWriteUtc != LastWriteUtc;
}

/// <summary>What one poll of the watched save decides.</summary>
public enum SaveWatchDecision
{
    /// <summary>Nothing to do: the file has not changed since the last read.</summary>
    NoChange,
    /// <summary>The file changed but the previous read is not confirmed stable yet — wait one more poll.</summary>
    PendingStable,
    /// <summary>The file changed and is now stable (two identical reads): safe to read and parse.</summary>
    ChangedStable,
    /// <summary>The watched path does not exist (yet): the companion shows its "no save yet" mode.</summary>
    Missing,
}

/// <summary>
/// The pure decision core of the external companion's polling (US-003): given the current
/// stat of the save file and the watcher's memory, decide whether to re-read and parse.
/// The App's monitor calls <see cref="Poll"/> every few seconds; the Domain owns all the
/// rules (changed? stable? debounce?) so they are unit-testable without a filesystem.
/// </summary>
public sealed class ExternalSaveWatch
{
    private SaveFileStamp? _lastRead;
    private SaveFileStamp? _pendingCandidate;

    /// <summary>The stamp of the last save we actually parsed and published (null = none yet).</summary>
    public SaveFileStamp? LastPublished { get; private set; }

    /// <summary>
    /// Decides what one poll does. <paramref name="current"/> is null when the file does not
    /// exist (yet). A changed file must read identical twice in a row before it counts as
    /// stable, so a partial flush mid-write is never parsed.
    /// </summary>
    public SaveWatchDecision Poll(SaveFileStamp? current)
    {
        if (current is null)
        {
            _lastRead = null;
            _pendingCandidate = null;
            return SaveWatchDecision.Missing;
        }
        if (!current.DiffersFrom(_lastRead))
        {
            // Same as the last read: if a candidate was pending, it is now confirmed stable.
            if (_pendingCandidate is not null && _pendingCandidate.DiffersFrom(LastPublished))
            {
                LastPublished = _pendingCandidate;
                _lastRead = current;
                _pendingCandidate = null;
                return SaveWatchDecision.ChangedStable;
            }
            _lastRead = current;
            return SaveWatchDecision.NoChange;
        }
        // The file moved since the last read: remember it as a candidate, confirm next poll.
        _pendingCandidate = current;
        _lastRead = current;
        return SaveWatchDecision.PendingStable;
    }

    /// <summary>Forgets everything (the external session ended): the next poll starts fresh.</summary>
    public void Reset()
    {
        _lastRead = null;
        _pendingCandidate = null;
        LastPublished = null;
    }
}

/// <summary>
/// Pure gate for "did anything worth publishing happen": a new snapshot replaces the
/// previous one only when it is non-null (a failed parse keeps the last good snapshot —
/// partial/invalid flushes are skipped, never crash) and the stamp actually moved.
/// </summary>
public static class ExternalSavePublish
{
    /// <summary>True when <paramref name="candidate"/> should replace <paramref name="current"/>.</summary>
    public static bool ShouldPublish(ExternalCompanionSnapshot? candidate, ExternalCompanionSnapshot? current, SaveFileStamp? stamp, SaveFileStamp? publishedStamp) =>
        candidate is not null && stamp is not null && stamp.DiffersFrom(publishedStamp) &&
        (current is null || candidate.SnapshotUtc != current.SnapshotUtc);
}
