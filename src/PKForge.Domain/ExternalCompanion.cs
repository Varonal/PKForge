namespace PKForge.Domain;

/// <summary>
/// What the second screen shows while an <b>external</b> emulator runs the game (the
/// polling model): a snapshot built by parsing the save file the emulator flushes to
/// disk, mirroring what <see cref="LiveGbaCompanion"/> reads straight from the embedded
/// core's memory. Refreshed each time the save file changes and parses cleanly.
/// </summary>
/// <param name="GameLabel">The game's shelf label ("Pokémon Emerald").</param>
/// <param name="GameCode">The cartridge/game code when known ("BPEE"), empty otherwise.</param>
/// <param name="Generation">The save's console generation (1-9).</param>
/// <param name="DexCaught">Dex species caught.</param>
/// <param name="DexSeen">Dex species seen.</param>
/// <param name="DexTotal">Dex size for this game.</param>
/// <param name="PartyCount">Pokémon currently in the party (0-6).</param>
/// <param name="PlaytimeHours">The save's own playtime counter, hours.</param>
/// <param name="PlaytimeMinutes">The save's own playtime counter, minutes.</param>
/// <param name="SnapshotUtc">When this snapshot was taken (the save's mtime is the trigger, this is the read time).</param>
public sealed record ExternalCompanionSnapshot(
    string GameLabel,
    string GameCode,
    int Generation,
    int DexCaught,
    int DexSeen,
    int DexTotal,
    int PartyCount,
    int PlaytimeHours,
    int PlaytimeMinutes,
    DateTimeOffset SnapshotUtc)
{
    public int DexPercent => DexTotal == 0 ? 0 : (int)Math.Round(DexCaught * 100.0 / DexTotal);
}

/// <summary>
/// Builds <see cref="ExternalCompanionSnapshot"/> from an already-parsed save. The Domain
/// never sees PKHeX types: the engine session is the parsed save's pure contract, and
/// the mapping itself works on the extracted records (<see cref="SaveSnapshot"/>,
/// <see cref="DexProgress"/>, <see cref="TrainerStats"/>) so it stays unit-testable.
/// </summary>
public static class ExternalCompanionSnapshotFactory
{
    /// <summary>
    /// Maps an open engine session to a snapshot. Returns null when the session is null
    /// or the save is not recognized/readable (any extraction failure) — the caller keeps
    /// the last good snapshot instead.
    /// </summary>
    public static ExternalCompanionSnapshot? FromSession(
        ISaveEngineSession? session,
        string? gameLabel = null,
        string? gameCode = null,
        DateTimeOffset? snapshotUtc = null)
    {
        if (session is null) return null;
        try
        {
            return FromFacts(
                session.Snapshot,
                session.GetDexProgress(),
                session.GetTrainerStats(),
                gameLabel ?? session.GameNames.FirstOrDefault() ?? string.Empty,
                gameCode ?? string.Empty,
                snapshotUtc ?? DateTimeOffset.UtcNow);
        }
        catch (Exception)
        {
            return null; // unrecognized or unreadable save: degrade, never crash
        }
    }

    /// <summary>
    /// Pure mapping from the extracted facts. Party slots are the snapshot's box -1 entries
    /// with a species; playtime comes from the save's own counter when the format keeps one.
    /// Returns null when the snapshot itself is null (nothing parsed).
    /// </summary>
    public static ExternalCompanionSnapshot? FromFacts(
        SaveSnapshot? snapshot,
        DexProgress? dex,
        TrainerStats? stats,
        string gameLabel,
        string gameCode,
        DateTimeOffset snapshotUtc)
    {
        if (snapshot is null) return null;
        var party = snapshot.Slots.Count(s => s.Box == -1 && s.Species is > 0);
        return new ExternalCompanionSnapshot(
            gameLabel,
            gameCode,
            snapshot.Generation,
            dex?.Caught ?? 0,
            dex?.Seen ?? 0,
            dex?.Total ?? 0,
            Math.Clamp(party, 0, 6),
            stats is { SupportsPlayTime: true } ? stats.PlayTimeHours : 0,
            stats is { SupportsPlayTime: true } ? stats.PlayTimeMinutes : 0,
            snapshotUtc);
    }
}
