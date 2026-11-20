using PKForge.Domain;

namespace PKForge.App.Services;

/// <summary>
/// Restore points of the save an external emulator is playing (US-005), on top of the
/// existing atomic backup pipeline (<see cref="IBackupService"/>): listing is newest-first
/// via <see cref="RestorePointRules"/>, creating a point is a named, timestamped backup of
/// the current save bytes (never an in-place write), and restoring is a destructive action
/// that always asks first (PadMenu.ConfirmAsync) and always takes a safety backup of the
/// live save before overwriting it — atomically (temp file + move), like every store here.
/// </summary>
public sealed class ExternalRestorePointService(IBackupService backups, ISaveEngine engine)
{
    /// <summary>The restore points of this save file, newest first. Empty when the save does not parse.</summary>
    public async Task<IReadOnlyList<CompanionRestorePoint>> ListForAsync(string savePath, CancellationToken cancellationToken = default)
    {
        var open = await OpenSnapshotAsync(savePath, cancellationToken).ConfigureAwait(false);
        var all = await backups.ListAsync(cancellationToken).ConfigureAwait(false);
        return RestorePointRules.ForGame(all, savePath, open);
    }

    /// <summary>
    /// Creates a named, timestamped restore point of the save's current bytes through the
    /// atomic backup pipeline. Returns the receipt, or null when the save cannot be read/parsed.
    /// </summary>
    public async Task<BackupReceipt?> CreatePointAsync(string savePath, CancellationToken cancellationToken = default)
    {
        var snapshot = await OpenSnapshotAsync(savePath, cancellationToken).ConfigureAwait(false);
        if (snapshot is null) return null;
        var name = RestorePointRules.NameFor(RestorePointKind.Manual, DateTimeOffset.UtcNow, AppStrings.Fr);
        return await backups.CreateAsync(snapshot, name, savePath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Restores a point into the save file: asks for confirmation (destructive), takes a
    /// safety backup of the live save first, then writes the point's bytes atomically.
    /// Returns false when the user cancels, the save is unreadable, or the point is corrupt.
    /// </summary>
    public async Task<bool> RestoreAsync(Grid host, string savePath, string backupId, CancellationToken cancellationToken = default)
    {
        var confirmed = await PadMenu.ConfirmAsync(host,
            AppStrings.RestorePointTitle, AppStrings.RestorePointConfirm, AppStrings.Restore).ConfigureAwait(false);
        if (!confirmed) return false;

        try
        {
            // Safety backup of the live save before anything destructive — never in-place.
            var live = await OpenSnapshotAsync(savePath, cancellationToken).ConfigureAwait(false);
            if (live is not null)
            {
                var safetyName = RestorePointRules.NameFor(RestorePointKind.SafetyBeforeRestore, DateTimeOffset.UtcNow, AppStrings.Fr);
                await backups.CreateAsync(live, safetyName, savePath, cancellationToken).ConfigureAwait(false);
            }

            var bytes = await backups.ReadAsync(backupId, cancellationToken).ConfigureAwait(false); // hash-checked
            WriteAtomically(savePath, bytes.Span);
            return true;
        }
        catch (Exception error)
        {
            AppLog.Error("companion", $"Restore of {backupId} into {savePath} failed", error);
            return false;
        }
    }

    /// <summary>Opens the save file as a snapshot for backup matching; null when unreadable/unrecognized.</summary>
    private async Task<SaveSnapshot?> OpenSnapshotAsync(string savePath, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await Task.Run(() => File.ReadAllBytes(savePath), cancellationToken).ConfigureAwait(false);
            if (!engine.Validate(bytes)) return null;
            return engine.Open(bytes, Path.GetFileName(savePath));
        }
        catch (Exception error)
        {
            AppLog.Info("companion", $"Reading {savePath} for a restore point failed: {error.Message}");
            return null;
        }
    }

    /// <summary>Temp file + move: the live save is never written in-place mid-flush.</summary>
    private static void WriteAtomically(string path, ReadOnlySpan<byte> bytes)
    {
        var temp = path + ".restore-tmp";
        File.WriteAllBytes(temp, bytes.ToArray());
        File.Move(temp, path, overwrite: true);
    }
}
