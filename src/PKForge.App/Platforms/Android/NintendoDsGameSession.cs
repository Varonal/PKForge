using System.Security.Cryptography;
using System.Text;
using PKForge.Domain;
using SkiaSharp;

namespace PKForge.App.Platforms.Android;

/// <summary>melonDS session and the DS battery-save synchronization boundary.</summary>
internal sealed class NintendoDsGameSession : IEmbeddedGameSession
{
    private readonly string _batterySavePath;
    private readonly string? _linkedSaveSyncMarkerPath;
    private readonly string? _linkedSaveDocumentId;
    private readonly ISaveFileAccess? _saveFileAccess;
    private readonly SemaphoreSlim _saveSyncGate = new(1, 1);
    private readonly object _disposeGate = new();
    private MelonDsCore? _core;
    private MelonDsEngine? _engine;
    private Task<bool>? _closeTask;

    public event Action? FrameProduced
    {
        add { if (_engine is not null) _engine.FrameProduced += value; }
        remove { if (_engine is not null) _engine.FrameProduced -= value; }
    }

    public (int Width, int Height) VideoSize => _core?.VideoSize ?? (0, 0);
    public string GameTitle => _core?.GameTitle ?? string.Empty;
    public string GameCode => _core?.GameCode ?? string.Empty;
    public int Generation { get; }
    public string QuickStatePath { get; }
    public bool HasQuickState => File.Exists(QuickStatePath);
    public bool HasSaveConflict { get; private set; }
    public bool SupportsTouchScreen => true;
    public bool SupportsLiveCompanion => true;
    public bool IsNintendoDs => true;

    private NintendoDsGameSession(
        MelonDsCore core,
        MelonDsEngine engine,
        string romPath,
        string? romFileName,
        string batterySavePath,
        int generation,
        string? linkedSaveDocumentId,
        ISaveFileAccess? saveFileAccess,
        bool hasSaveConflict)
    {
        _core = core;
        _engine = engine;
        _batterySavePath = batterySavePath;
        _linkedSaveSyncMarkerPath = linkedSaveDocumentId is null ? null : batterySavePath + ".synced";
        _linkedSaveDocumentId = linkedSaveDocumentId;
        _saveFileAccess = saveFileAccess;
        Generation = generation;
        var fileName = Path.GetFileNameWithoutExtension(romFileName ?? romPath);
        QuickStatePath = EmbeddedGameSession.PrivateFilePath("ds-states", romPath, ".ss0", fileName);
        HasSaveConflict = hasSaveConflict;
    }

    public static bool IsAvailable
    {
        get
        {
            try
            {
                var available = MelonDsNative.Available() != 0;
                EmulatorCatalog.EmbeddedDsCoreAvailable = available;
                return available;
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            catch (BadImageFormatException) { }
            EmulatorCatalog.EmbeddedDsCoreAvailable = false;
            return false;
        }
    }

    public static EmbeddedGameStartResult TryStart(
        string romPath,
        int generation,
        string? romFileName,
        string? linkedSaveDocumentId,
        ISaveFileAccess? saveFileAccess)
    {
        MelonDsCore? core = null;
        MelonDsEngine? engine = null;
        try
        {
            var extension = Path.GetExtension(romFileName ?? romPath);
            if (!extension.Equals(".nds", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                return new EmbeddedGameStartResult(null, "Le cœur DS intégré accepte les fichiers .nds et les archives .zip contenant une seule ROM .nds.");

            var localRomPath = EmbeddedGameSession.MaterializeNintendoDsRom(romPath, romFileName);
            var savePath = EmbeddedGameSession.SavePathForRom(romPath, linkedSaveDocumentId);
            Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
            var hasSaveConflict = false;
            if (linkedSaveDocumentId is not null)
            {
                if (saveFileAccess is null)
                    throw new InvalidOperationException("Le service de sauvegarde liée n'est pas disponible.");
                var linkedBytes = saveFileAccess.ReadAsync(linkedSaveDocumentId).AsTask().GetAwaiter().GetResult().ToArray();
                hasSaveConflict = EmbeddedGameSession.ReconcileLinkedSave(savePath, savePath + ".synced", linkedBytes);
            }

            core = new MelonDsCore();
            if (!core.Load(localRomPath, savePath))
            {
                var reason = core.LoadError ?? "melonDS n'a pas pu charger cette ROM DS.";
                Services.AppLog.Error("embedded", $"Could not load {Path.GetFileName(romPath)}: {reason}", new InvalidOperationException(reason));
                core.Dispose();
                return new EmbeddedGameStartResult(null, reason);
            }

            engine = new MelonDsEngine(core);
            var session = new NintendoDsGameSession(core, engine, romPath, romFileName, savePath,
                generation, linkedSaveDocumentId, saveFileAccess, hasSaveConflict);
            engine.Start();
            return new EmbeddedGameStartResult(session, null);
        }
        catch (DllNotFoundException error)
        {
            const string reason = "Android n'a pas pu charger le cœur melonDS intégré à cette APK.";
            Services.AppLog.Error("embedded", reason, error);
            engine?.Dispose();
            core?.Dispose();
            return new EmbeddedGameStartResult(null, $"{reason} {error.Message}");
        }
        catch (EntryPointNotFoundException error)
        {
            const string reason = "Le cœur melonDS de l'APK est incomplet ou trop ancien pour PKForge.";
            Services.AppLog.Error("embedded", reason, error);
            engine?.Dispose();
            core?.Dispose();
            return new EmbeddedGameStartResult(null, reason);
        }
        catch (BadImageFormatException error)
        {
            const string reason = "L'architecture du cœur melonDS ne correspond pas à cet appareil.";
            Services.AppLog.Error("embedded", reason, error);
            engine?.Dispose();
            core?.Dispose();
            return new EmbeddedGameStartResult(null, reason);
        }
        catch (InvalidDataException error)
        {
            const string reason = "Archive DS invalide.";
            Services.AppLog.Warn("embedded", $"Could not open DS archive {Path.GetFileName(romPath)}: {error.Message}");
            engine?.Dispose();
            core?.Dispose();
            return new EmbeddedGameStartResult(null, $"{reason} {error.Message}");
        }
        catch (Exception error)
        {
            Services.AppLog.Error("embedded", $"Could not start DS ROM {Path.GetFileName(romPath)}", error);
            engine?.Dispose();
            core?.Dispose();
            return new EmbeddedGameStartResult(null, $"{error.GetType().Name}: {error.Message}");
        }
    }

    public void SetKeys(uint bitmask) => _engine?.SetKeys(bitmask);
    public void SetTouchScreen(bool down, int x, int y) => _engine?.SetTouchScreen(down, x, y);
    public void Draw(SKCanvas canvas, SKRect destination) => _engine?.Draw(canvas, destination);
    public void DrawMainScreen(SKCanvas canvas, SKRect destination) => _engine?.DrawMainScreen(canvas, destination);
    public void DrawSecondaryScreen(SKCanvas canvas, SKRect destination) => _engine?.DrawSecondaryScreen(canvas, destination);
    public LiveCompanionSnapshot? ReadLiveCompanion() => _engine?.ReadLiveCompanion(Generation);
    public void SetPaused(bool paused) => _engine?.SetPaused(paused);
    public void SetSpeedMultiplier(int multiplier) => _engine?.SetSpeedMultiplier(multiplier);

    public async Task<bool> FlushBatterySaveAsync()
    {
        var engine = _engine;
        if (engine is not null)
        {
            engine.SetPaused(true);
            if (!engine.FlushSave())
            {
                Services.AppLog.Warn("embedded", $"Could not flush the DS battery save for {GameTitle}.");
                return false;
            }
        }
        return await SyncLinkedSaveAsync(sessionClosed: false).ConfigureAwait(false);
    }

    public bool SaveQuickState()
    {
        var temporaryPath = QuickStatePath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(QuickStatePath)!);
            if (_engine?.SaveState(temporaryPath) != true)
            {
                File.Delete(temporaryPath);
                return false;
            }
            File.Move(temporaryPath, QuickStatePath, overwrite: true);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Services.AppLog.Warn("embedded", $"Could not save a DS quick state for {GameTitle}: {error.Message}");
            try { File.Delete(temporaryPath); } catch (IOException) { }
            return false;
        }
    }

    public bool LoadQuickState() => HasQuickState && _engine?.LoadState(QuickStatePath) == true;

    public Task<bool> CloseAsync()
    {
        lock (_disposeGate) return _closeTask ??= CloseCoreAsync();
    }

    private async Task<bool> CloseCoreAsync()
    {
        var gameTitle = GameTitle;
        var engine = _engine;
        var core = _core;
        _engine = null;
        _core = null;
        var saveFlushed = true;
        try
        {
            await Task.Run(() =>
            {
                try
                {
                    if (engine is not null)
                    {
                        engine.SetPaused(true);
                        saveFlushed = engine.FlushSave();
                    }
                }
                finally
                {
                    engine?.Dispose();
                    core?.Dispose();
                }
            }).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            Services.AppLog.Error("embedded", $"Could not stop DS game {gameTitle}", error);
            return false;
        }

        if (!saveFlushed)
        {
            Services.AppLog.Warn("embedded", $"Could not flush the DS battery save for {gameTitle}.");
            return false;
        }
        return await SyncLinkedSaveAsync(sessionClosed: true).ConfigureAwait(false);
    }

    private async Task<bool> SyncLinkedSaveAsync(bool sessionClosed)
    {
        if (_linkedSaveDocumentId is null || _saveFileAccess is null) return true;
        await _saveSyncGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var bytes = await File.ReadAllBytesAsync(_batterySavePath).ConfigureAwait(false);
            var linkedBytes = (await _saveFileAccess.ReadAsync(_linkedSaveDocumentId).ConfigureAwait(false)).ToArray();
            if (!bytes.AsSpan().SequenceEqual(linkedBytes))
            {
                var marker = _linkedSaveSyncMarkerPath is null
                    ? null
                    : EmbeddedGameSession.ReadSyncMarker(_linkedSaveSyncMarkerPath);
                if (marker is null) return SaveSyncConflict();
                var localMatchesMarker = EmbeddedGameSession.ContentHash(bytes).Equals(marker, StringComparison.Ordinal);
                var linkedMatchesMarker = EmbeddedGameSession.ContentHash(linkedBytes).Equals(marker, StringComparison.Ordinal);
                if (localMatchesMarker && !linkedMatchesMarker)
                {
                    if (!sessionClosed) return SaveSyncConflict();
                    EmbeddedGameSession.WriteFileAtomically(_batterySavePath, linkedBytes);
                    if (_linkedSaveSyncMarkerPath is not null)
                        EmbeddedGameSession.TryWriteSyncMarker(_linkedSaveSyncMarkerPath, linkedBytes);
                    HasSaveConflict = false;
                    return true;
                }
                if (!localMatchesMarker || !linkedMatchesMarker)
                {
                    if (!linkedMatchesMarker) return SaveSyncConflict();
                }
            }
            await _saveFileAccess.WriteAtomicallyAsync(_linkedSaveDocumentId, bytes).ConfigureAwait(false);
            if (_linkedSaveSyncMarkerPath is not null)
                EmbeddedGameSession.TryWriteSyncMarker(_linkedSaveSyncMarkerPath, bytes);
            HasSaveConflict = false;
            return true;
        }
        catch (Exception error)
        {
            Services.AppLog.Error("embedded", $"Could not sync the linked DS save {_linkedSaveDocumentId}", error);
            return false;
        }
        finally
        {
            _saveSyncGate.Release();
        }
    }

    private bool SaveSyncConflict()
    {
        HasSaveConflict = true;
        Services.AppLog.Warn("embedded", "The linked DS save changed during play; neither copy was overwritten.");
        return false;
    }

    public ValueTask DisposeAsync() => new(CloseAsync());
}
