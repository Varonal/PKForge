using CommunityToolkit.Mvvm.ComponentModel;
using PKForge.Domain;

namespace PKForge.App.Services;

/// <summary>
/// <see cref="IGameCompanionStore"/> as an observable session state: set when Play
/// launches a game, cleared when the player comes back to PKForge. The second
/// screen's swap reads <see cref="Current"/> while the emulator owns the foreground.
/// </summary>
public sealed partial class GameCompanionStore : ObservableObject, IGameCompanionStore
{
    // The generated property "Current" implements the interface's reader.
    [ObservableProperty] private GameCompanion? _current;

    [ObservableProperty] private LiveCompanionSnapshot? _live;

    public void Start(GameCompanion companion) => Current = companion;

    public void UpdateLive(LiveCompanionSnapshot snapshot) => Live = snapshot;

    public void ClearLive() => Live = null;

    public void Stop()
    {
        Current = null;
        Live = null;
    }

    /// <summary>
    /// Starts the companion for a launched game: its dex progress and restore points
    /// when the game's save is linked, the bare game card otherwise. Called by every
    /// Play site after a successful handoff; the watcher's resume scan clears it.
    /// </summary>
    public async Task StartForGameAsync(RomLink link)
    {
        var services = IPlatformApplication.Current?.Services;
        var points = new List<CompanionRestorePoint>();
        try
        {
            if (services?.GetService<IBackupService>() is { } backups)
            {
                foreach (var info in await backups.ListAsync())
                    points.Add(new CompanionRestorePoint(info.BackupId, info.CreatedUtc, info.Sha256));
                points.Reverse(); // newest first
            }
        }
        catch (Exception error)
        {
            AppLog.Info("companion", $"Restore points unavailable: {error.Message}");
        }
        // The dex lands with the watcher's first resume scan; the companion opens
        // with the game and its restore points right away.
        Start(new GameCompanion(link.GameLabel, link.Generation, 0, 0, 0, points));
    }
}
