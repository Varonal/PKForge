namespace PKForge.Domain;

/// <summary>
/// The in-game companion's state: what the second screen shows while an emulator
/// runs the game. PKForge stays alive behind the emulator (the presentation
/// suspends on pause and resumes on focus), so the lower screen becomes the
/// game's companion — dex, restore points, play stats — for single-screen games.
/// </summary>
public sealed record GameCompanion(
    string GameLabel,
    int Generation,
    /// <summary>The dex read at launch: caught/seen/total for the companion's progress bar.</summary>
    int DexCaught,
    int DexSeen,
    int DexTotal,
    /// <summary>Restore points that exist for this game's save, newest first.</summary>
    IReadOnlyList<CompanionRestorePoint> RestorePoints)
{
    public int DexPercent => DexTotal == 0 ? 0 : (int)Math.Round(DexCaught * 100.0 / DexTotal);
}

/// <summary>One restore point the companion can name.</summary>
public sealed record CompanionRestorePoint(string BackupId, DateTimeOffset CreatedUtc, string? Sha256 = null);

/// <summary>
/// Tracks the companion: set when Play launches a game, cleared when the player
/// returns to PKForge's own screens. The second screen's swap reads it.
/// </summary>
public interface IGameCompanionStore
{
    GameCompanion? Current { get; }

    /// <summary>Shows this game's companion on the lower screen for the whole emulation.</summary>
    void Start(GameCompanion companion);

    /// <summary>Back to PKForge's own lower-screen surfaces.</summary>
    void Stop();
}
