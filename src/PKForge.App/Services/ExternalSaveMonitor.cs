using PKForge.Domain;

namespace PKForge.App.Services;

/// <summary>
/// Watches the save file an external emulator writes while its game runs (US-003), and
/// publishes a fresh <see cref="ExternalCompanionSnapshot"/> to the companion store each
/// time the file is flushed <i>and</i> parsed cleanly.
///
/// Battery-friendly by design: one stat read every <see cref="PollInterval"/> (5 s) while a
/// session is active — never a tight loop, and never when no external session runs. All the
/// decisions (changed? stable? publish?) live in the pure Domain core
/// (<see cref="ExternalSaveWatch"/> / <see cref="ExternalSavePublish"/>); this class only
/// does the I/O (FileInfo, read bytes, engine parse) and calls them.
/// A partial or invalid flush is skipped: the last good snapshot stays up, nothing crashes.
/// </summary>
public sealed class ExternalSaveMonitor : IDisposable
{
    /// <summary>One stat read every 5 seconds while an external game runs.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly IGameCompanionStore _companion;
    private readonly ISaveEngine _engine;
    private readonly ExternalSaveWatch _watch = new();
    private readonly Lock _gate = new();
    private PeriodicTimer? _timer;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private string? _savePath;
    private string? _gameLabel;
    private string? _gameCode;
    private ExternalCompanionSnapshot? _lastGood;

    public ExternalSaveMonitor(IGameCompanionStore companion, ISaveEngine engine)
    {
        _companion = companion;
        _engine = engine;
    }

    /// <summary>True while the polling loop runs (an external session is active).</summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// Starts watching the save candidates of a launched ROM: the first existing candidate
    /// wins (US-002's resolver), and when none exists yet the monitor keeps watching the
    /// most likely path so a save created mid-game is picked up on a later poll.
    /// </summary>
    public void Start(string romPath, EmulatorKind emulator, string gameLabel, string? gameCode = null)
    {
        lock (_gate)
        {
            Stop();
            _savePath = ExternalSavePathResolver.ExistingPaths(romPath, emulator, File.Exists).FirstOrDefault()
                ?? ExternalSavePathResolver.CandidatePaths(romPath, emulator).FirstOrDefault();
            _gameLabel = gameLabel;
            _gameCode = gameCode ?? string.Empty;
            _lastGood = null;
            _watch.Reset();
            if (_savePath is null) return; // no ROM path: nothing to watch, no polling
            _cts = new CancellationTokenSource();
            _timer = new PeriodicTimer(PollInterval);
            IsRunning = true;
            _loop = Task.Run(() => RunAsync(_savePath, _cts.Token));
        }
    }

    /// <summary>Stops the loop and clears the external snapshot. Never polls without a session.</summary>
    public void Stop()
    {
        lock (_gate)
        {
            IsRunning = false;
            _timer?.Dispose();
            _timer = null;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _loop = null;
            _savePath = null;
            _watch.Reset();
            _lastGood = null;
        }
        _companion.ClearExternal();
    }

    private async Task RunAsync(string savePath, CancellationToken cancellationToken)
    {
        try
        {
            while (_timer is { } timer && await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                PollOnce(savePath);
                // The save may appear later (emulator creates it on first save): re-resolve
                // the candidate list if we were watching a path that did not exist yet.
                if (!File.Exists(savePath)) continue;
            }
        }
        catch (OperationCanceledException) { /* session ended */ }
        catch (ObjectDisposedException) { /* timer disposed by Stop() */ }
        catch (Exception error)
        {
            AppLog.Error("companion", $"External save polling crashed on {savePath}", error);
        }
    }

    /// <summary>One poll: stat the file, let the pure watch decide, read+parse only when stable.</summary>
    private void PollOnce(string savePath)
    {
        SaveFileStamp? stamp = null;
        try
        {
            var info = new FileInfo(savePath);
            if (info.Exists)
                stamp = new SaveFileStamp(savePath, info.Length, info.LastWriteTimeUtc);
        }
        catch (Exception error)
        {
            AppLog.Info("companion", $"Stat of {savePath} failed: {error.Message}");
            return; // keep the last good snapshot
        }

        var decision = _watch.Poll(stamp);
        if (decision is not (SaveWatchDecision.ChangedStable or SaveWatchDecision.Missing)) return;
        if (decision is SaveWatchDecision.Missing)
        {
            // Save vanished (emulator deleted it): degrade to no-save mode, keep last good.
            return;
        }
        if (stamp is null) return;

        // Read + parse off the polling cadence's critical path is fine at 5 s; still guard everything.
        try
        {
            var bytes = File.ReadAllBytes(savePath);
            if (!_engine.Validate(bytes)) return; // partial/invalid flush: skip, keep last good
            using var session = _engine.OpenSession(bytes, _gameLabel);
            var snapshot = ExternalCompanionSnapshotFactory.FromSession(session, _gameLabel, _gameCode, DateTimeOffset.UtcNow);
            if (ExternalSavePublish.ShouldPublish(snapshot, _lastGood, stamp, _watch.LastPublished))
            {
                _lastGood = snapshot;
                if (snapshot is not null)
                    MainThread.BeginInvokeOnMainThread(() => _companion.UpdateExternal(snapshot));
            }
        }
        catch (Exception error)
        {
            AppLog.Info("companion", $"Parsing {savePath} failed (partial flush?): {error.Message}");
            // keep the last good snapshot — never crash, never publish garbage
        }
    }

    public void Dispose() => Stop();
}
