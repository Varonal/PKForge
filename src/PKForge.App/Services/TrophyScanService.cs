using PKForge.Domain;

namespace PKForge.App.Services;

/// <summary>
/// Scans linked saves into <see cref="SaveScanFacts"/> and updates the homemade trophy
/// store, the same read path as the Museum's dex editor: bytes in, dex progress, shiny
/// holdings and playtime out. Unlocks are permanent; the store keeps the best value.
/// </summary>
public sealed class TrophyScanService(
    ISaveFileAccess files,
    ISaveEngine engine,
    ITrophyStore trophies)
{
    /// <summary>Scans one save and stores every trophy state of its game. Null when the bytes do not parse.</summary>
    public async Task<SaveScanFacts?> ScanAsync(DetectedSave save, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(save);
        var bytes = await files.ReadAsync(save.DocumentId, cancellationToken).ConfigureAwait(false);
        var facts = await Task.Run(() => Measure(save, bytes), cancellationToken).ConfigureAwait(false);
        if (facts is null) return null;
        foreach (var state in TrophyEngine.EvaluateGame(TrophyCatalog.ForGame(facts.GameLabel), facts))
            trophies.Store(state);
        return facts;
    }

    /// <summary>Scans every visible linked save; returns the facts per save that parsed.</summary>
    public async Task<IReadOnlyList<SaveScanFacts>> ScanAllAsync(IReadOnlyList<DetectedSave> saves, CancellationToken cancellationToken = default)
    {
        var scanned = new List<SaveScanFacts>();
        foreach (var save in saves.Where(s => s.Identity?.IsHidden != true))
        {
            try
            {
                var facts = await ScanAsync(save, cancellationToken).ConfigureAwait(false);
                if (facts is not null) scanned.Add(facts);
            }
            catch (Exception error)
            {
                AppLog.Error("trophies", $"Scanning {save.GameLabel} ({save.FileName}) failed", error);
            }
        }
        return scanned;
    }

    /// <summary>Reads one save's measurable facts; null when the engine cannot open it.</summary>
    private SaveScanFacts? Measure(DetectedSave save, ReadOnlyMemory<byte> bytes)
    {
        try
        {
            using var session = engine.OpenSession(bytes, save.EngineHint);
            var progress = session.GetDexProgress();
            var shinySpecies = new HashSet<int>();
            var owned = 0;
            foreach (var slot in session.Snapshot.Slots)
            {
                if (slot.Species is not { } species || species <= 0) continue;
                owned++;
                if (slot.IsShiny) shinySpecies.Add(species);
            }
            var playtime = TryParsePlaytime(save.PlayTime);
            return new SaveScanFacts(
                save.DocumentId,
                save.EngineHint,
                save.Generation,
                progress.Caught,
                progress.Seen,
                progress.Total,
                shinySpecies.Count,
                owned,
                playtime,
                save.TrainerName);
        }
        catch (Exception error)
        {
            AppLog.Error("trophies", $"Measuring {save.GameLabel} ({save.FileName}) failed", error);
            return null;
        }
    }

    /// <summary>The shelf's playtime strings: "123:45" (hours:minutes) or plain minutes.</summary>
    private static int TryParsePlaytime(string? playtime)
    {
        if (string.IsNullOrEmpty(playtime)) return 0;
        var colon = playtime.IndexOf(':');
        if (colon > 0 && int.TryParse(playtime[..colon], out var hours)) return hours;
        return int.TryParse(playtime, out var minutes) ? minutes / 60 : 0;
    }
}
