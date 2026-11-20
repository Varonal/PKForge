namespace PKForge.Domain;

/// <summary>Whether a game's external play session is running, paused, or closed.</summary>
public enum SessionState
{
    /// <summary>No session: nothing is measured (the companion is stopped).</summary>
    Idle = 0,
    /// <summary>Playing now: the clock runs from <see cref="PlaySessionStats.ActiveSinceUtc"/>.</summary>
    Active = 1,
    /// <summary>Paused (app in background): the current stretch is banked, nothing accrues.</summary>
    Paused = 2,
}

/// <summary>
/// The play-time ledger of one external game (US-006), keyed by the launched ROM's path.
/// Persisted per game so the total survives restarts. No timer, no polling: durations are
/// computed from event timestamps only, so nothing is measured in the background once the
/// session is closed (<see cref="SessionState.Idle"/>).
/// </summary>
/// <param name="Key">The game's key (the ROM path the session was started from).</param>
/// <param name="GameLabel">Shelf label for display.</param>
/// <param name="TotalSeconds">Accumulated active seconds across all sessions.</param>
/// <param name="State">Idle / Active / Paused.</param>
/// <param name="ActiveSinceUtc">When the current active stretch started (null unless Active).</param>
/// <param name="LastClosedUtc">When the last session was closed (null if never played).</param>
public sealed record PlaySessionStats(
    string Key,
    string GameLabel,
    long TotalSeconds,
    SessionState State,
    DateTimeOffset? ActiveSinceUtc,
    DateTimeOffset? LastClosedUtc)
{
    public TimeSpan Total => TimeSpan.FromSeconds(TotalSeconds);

    /// <summary>The current session's elapsed time at <paramref name="now"/> (zero unless Active).</summary>
    public TimeSpan SessionElapsed(DateTimeOffset now) =>
        State == SessionState.Active && ActiveSinceUtc is { } since && now >= since
            ? now - since
            : TimeSpan.Zero;

    /// <summary>Total including the running session, as seen at <paramref name="now"/>.</summary>
    public TimeSpan GrandTotal(DateTimeOffset now) => Total + SessionElapsed(now);

    /// <summary>Formats "12:34:56" (h:mm:ss) for the companion's session chrono.</summary>
    public static string FormatDuration(TimeSpan duration) =>
        duration < TimeSpan.Zero ? "0:00:00"
        : duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes}:{duration.Seconds:00}";
}

/// <summary>
/// The pure session state machine (US-006): start / pause / resume / close on timestamps.
/// No clocks, no threads — every transition is a pure function of (current, now), so the
/// whole aggregation is unit-testable and nothing measures after Close.
/// </summary>
public static class SessionClock
{
    /// <summary>Opens a session: from Idle/Paused, the clock starts at <paramref name="now"/>; Active is a no-op.</summary>
    public static PlaySessionStats Start(PlaySessionStats? current, string key, string gameLabel, DateTimeOffset now)
    {
        if (current is { State: SessionState.Active }) return current;
        return new PlaySessionStats(
            key,
            gameLabel,
            current?.TotalSeconds ?? 0,
            SessionState.Active,
            now,
            current?.LastClosedUtc);
    }

    /// <summary>Banks the running stretch into the total and pauses. Idle/Paused are no-ops.</summary>
    public static PlaySessionStats Pause(PlaySessionStats? current, DateTimeOffset now)
    {
        if (current is not { State: SessionState.Active }) return current ?? Idle(key: "", gameLabel: "");
        return current with
        {
            TotalSeconds = current.TotalSeconds + (long)current.SessionElapsed(now).TotalSeconds,
            State = SessionState.Paused,
            ActiveSinceUtc = null,
        };
    }

    /// <summary>Resumes a paused session: the clock restarts at <paramref name="now"/>; total is kept.</summary>
    public static PlaySessionStats Resume(PlaySessionStats? current, DateTimeOffset now)
    {
        if (current is not { State: SessionState.Paused }) return current ?? Idle(key: "", gameLabel: "");
        return current with { State = SessionState.Active, ActiveSinceUtc = now };
    }

    /// <summary>Closes the session: banks the last stretch, goes Idle. Closing twice is a no-op.</summary>
    public static PlaySessionStats Close(PlaySessionStats? current, DateTimeOffset now)
    {
        if (current is null) return Idle(key: "", gameLabel: "");
        if (current.State == SessionState.Idle) return current; // nothing measures after close
        var banked = current.State == SessionState.Active
            ? current.TotalSeconds + (long)current.SessionElapsed(now).TotalSeconds
            : current.TotalSeconds;
        return current with
        {
            TotalSeconds = banked,
            State = SessionState.Idle,
            ActiveSinceUtc = null,
            LastClosedUtc = now,
        };
    }

    /// <summary>A read-only view at <paramref name="now"/>: never mutates, and Idle stays Idle.</summary>
    public static PlaySessionStats Tick(PlaySessionStats? current, DateTimeOffset now) =>
        current is null ? Idle(key: "", gameLabel: "") : current; // elapsed is derived in SessionElapsed/GrandTotal

    private static PlaySessionStats Idle(string key, string gameLabel) =>
        new(key, gameLabel, 0, SessionState.Idle, null, null);
}

/// <summary>Persists the per-game play-time ledger (one JSON document, atomic — the Infrastructure pattern).</summary>
public interface IPlaySessionStore
{
    IReadOnlyList<PlaySessionStats> GetAll();

    PlaySessionStats? Find(string key);

    /// <summary>Upserts one game's ledger.</summary>
    void Set(PlaySessionStats stats);
}
