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

    [ObservableProperty] private ExternalCompanionSnapshot? _external;

    public void Start(GameCompanion companion) => Current = companion;

    public void UpdateLive(LiveCompanionSnapshot snapshot) => Live = snapshot;

    public void ClearLive() => Live = null;

    public void UpdateExternal(ExternalCompanionSnapshot snapshot) => External = snapshot;

    public void ClearExternal() => External = null;

    public void Stop()
    {
        Current = null;
        Live = null;
        External = null;
    }

    /// <summary>
    /// Starts the companion for a launched game: its dex progress and restore points
    /// when the game's save is linked, the bare game card otherwise. Called by every
    /// Play site after a successful handoff; the watcher's resume scan clears it.
    /// For an external emulator (never Embedded) this also starts the save monitor
    /// (US-003) and persists the session (US-004) so it survives an app kill.
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

        // External emulation only: watch the save the emulator writes and remember the
        // session, so the companion resumes if PKForge itself is killed mid-game.
        if (link.Emulator == EmulatorKind.Embedded) return;
        try
        {
            services?.GetService<IExternalCompanionStateStore>()
                ?.Set(new ExternalCompanionState(link.RomUri, link.Emulator, link.GameLabel, string.Empty, DateTimeOffset.UtcNow));
            services?.GetService<ExternalSaveMonitor>()
                ?.Start(link.RomUri, link.Emulator, link.GameLabel);
            // US-006: the play-time session starts with the external launch.
            if (services?.GetService<IPlaySessionStore>() is { } sessions)
                sessions.Set(SessionClock.Start(sessions.Find(link.RomUri), link.RomUri, link.GameLabel, DateTimeOffset.UtcNow));
        }
        catch (Exception error)
        {
            AppLog.Info("companion", $"External monitor start failed: {error.Message}");
        }
    }

    /// <summary>
    /// Resumes the external companion from the persisted state (app restarted while an
    /// emulator was running). No-op when nothing is persisted. Resilient by design:
    /// if the emulator or its save are gone, the monitor degrades to no-save mode.
    /// </summary>
    public void ResumeExternalIfAny()
    {
        var services = IPlatformApplication.Current?.Services;
        var state = services?.GetService<IExternalCompanionStateStore>()?.Current;
        if (state is null || state.Emulator == EmulatorKind.Embedded) return;
        try
        {
            services?.GetService<ExternalSaveMonitor>()?.Start(state.RomPath, state.Emulator, state.GameLabel, state.GameCode);
            if (services?.GetService<IPlaySessionStore>() is { } sessions)
                sessions.Set(SessionClock.Resume(sessions.Find(state.RomPath), DateTimeOffset.UtcNow));
        }
        catch (Exception error)
        {
            AppLog.Info("companion", $"External monitor resume failed: {error.Message}");
        }
    }

    /// <summary>
    /// Stops the external companion: monitor off, state cleared. Called when the player
    /// is back in PKForge (resume), right before the trophy watcher re-scans.
    /// </summary>
    public void StopExternal()
    {
        try
        {
            var services = IPlatformApplication.Current?.Services;
            services?.GetService<ExternalSaveMonitor>()?.Stop();
            // US-006: close the play session of the game we were watching (keyed by its ROM
            // path) before clearing the persisted state — after Close, nothing measures.
            if (services?.GetService<IExternalCompanionStateStore>()?.Current is { } state
                && services.GetService<IPlaySessionStore>() is { } sessions)
            {
                sessions.Set(SessionClock.Close(sessions.Find(state.RomPath), DateTimeOffset.UtcNow));
            }
            services?.GetService<IExternalCompanionStateStore>()?.Set(null);
        }
        catch (Exception error)
        {
            AppLog.Info("companion", $"External monitor stop failed: {error.Message}");
        }
    }
}
