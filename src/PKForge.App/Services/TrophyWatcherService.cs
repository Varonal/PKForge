using System.Collections.Concurrent;
using PKForge.Domain;

namespace PKForge.App.Services;

/// <summary>
/// The automatic trophy watcher: whenever the app comes back to the foreground (typically
/// returning from an emulator), the linked saves are re-scanned off the UI thread and any
/// newly unlocked homemade trophy is announced. Announcements go to a queue the UI drains
/// and to the platform's local notification (Android notification channel), so the player
/// hears about a trophy even when the Trophies page is not open.
/// </summary>
public sealed class TrophyWatcherService(
    TrophyScanService scanner,
    ITrophyStore trophies)
{
    /// <summary>Raised on the UI thread with each newly unlocked trophy, in scan order.</summary>
    public event Action<TrophyDefinition>? TrophyUnlocked;

    /// <summary>True while a background scan is in flight (the UI stays quiet about it).</summary>
    public bool IsScanning => Volatile.Read(ref _scanning) != 0;

    private int _scanning;
    private readonly ConcurrentDictionary<string, byte> _announced = new(StringComparer.Ordinal);

    /// <summary>
    /// Scans every visible linked save and announces the trophies that were locked at the
    /// last scan and are unlocked now. Safe to call repeatedly: one scan at a time, and a
    /// scan while another is running is skipped (the next resume catches up).
    /// </summary>
    public async Task<IReadOnlyList<TrophyDefinition>> ScanAndAnnounceAsync(
        IReadOnlyList<DetectedSave> saves,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _scanning, 1) != 0) return [];
        try
        {
            var unlocked = new List<TrophyDefinition>();
            foreach (var save in saves.Where(s => s.Identity?.IsHidden != true))
            {
                var before = trophies.GetAll().Where(s => s.Unlocked).Select(s => s.TrophyId).ToHashSet(StringComparer.Ordinal);
                var facts = await scanner.ScanAsync(save, cancellationToken).ConfigureAwait(false);
                if (facts is null) continue;
                foreach (var definition in TrophyCatalog.ForGame(facts.GameLabel))
                {
                    var state = trophies.Find(definition.Id);
                    if (state is not { Unlocked: true } || before.Contains(definition.Id)) continue;
                    if (!_announced.TryAdd(definition.Id, 0)) continue;
                    unlocked.Add(definition);
                }
            }
            if (unlocked.Count > 0)
            {
                await AnnounceAsync(unlocked).ConfigureAwait(false);
                foreach (var definition in unlocked)
                    MainThread.BeginInvokeOnMainThread(() => TrophyUnlocked?.Invoke(definition));
            }
            return unlocked;
        }
        finally
        {
            Volatile.Write(ref _scanning, 0);
        }
    }

    /// <summary>The platform's local notification hook; Android shows a channel notification.</summary>
    private static async Task AnnounceAsync(IReadOnlyList<TrophyDefinition> unlocked)
    {
        try
        {
            var notifier = IPlatformApplication.Current?.Services.GetService<ITrophyNotifier>();
            if (notifier is null) return;
            await notifier.NotifyAsync(unlocked).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            AppLog.Info("trophies", $"Announcing {unlocked.Count} unlock(s) failed: {error.Message}");
        }
    }
}

/// <summary>Shows local notifications for trophy unlocks (implemented per platform).</summary>
public interface ITrophyNotifier
{
    Task NotifyAsync(IReadOnlyList<TrophyDefinition> unlocked);
}
