using System.Security.Cryptography;
using System.IO.Compression;
using System.Text;
using PKForge.Domain;
using SkiaSharp;

namespace PKForge.App.Platforms.Android;

public sealed record EmbeddedGameStartResult(IEmbeddedGameSession? Session, string? Error);

/// <summary>
/// One running game: the core plus its engine, owned together. Created by the
/// Play flow when the embedded library is present; disposed when the player
/// leaves the game screen. The pages and the second-screen companion both read
/// through this facade.
/// </summary>
public sealed class EmbeddedGameSession : IEmbeddedGameSession, IDisposable
{
    private readonly MgbaCore _core;
    private readonly string _romFileName;
    private readonly string _batterySavePath;
    private readonly string? _linkedSaveSyncMarkerPath;
    private readonly string? _linkedSaveDocumentId;
    private readonly ISaveFileAccess? _saveFileAccess;
    private readonly object _disposeGate = new();
    private readonly SemaphoreSlim _saveSyncGate = new(1, 1);
    private EmbeddedEngine? _engine;
    private Task<bool>? _closeTask;

    /// <summary>Raised on the emu thread after each frame; no-ops once disposed.</summary>
    public event Action? FrameProduced
    {
        add { if (_engine is not null) _engine.FrameProduced += value; }
        remove { if (_engine is not null) _engine.FrameProduced -= value; }
    }

    public (int Width, int Height) VideoSize => _core.VideoSize;
    public string GameTitle => _core.GameTitle;
    public string GameCode => _core.GameCode;
    public int Generation { get; }
    public string QuickStatePath { get; }
    public bool HasQuickState => File.Exists(QuickStatePath);
    public bool HasSaveConflict { get; private set; }
    public bool SupportsTouchScreen => false;
    public bool SupportsLiveCompanion => true;
    public bool IsNintendoDs => false;

    private EmbeddedGameSession(
        MgbaCore core,
        EmbeddedEngine engine,
        string romPath,
        string? romFileName,
        string batterySavePath,
        int generation,
        string? linkedSaveDocumentId,
        ISaveFileAccess? saveFileAccess,
        bool hasSaveConflict)
    {
        _core = core;
        _romFileName = romFileName ?? Path.GetFileName(romPath);
        _engine = engine;
        _batterySavePath = batterySavePath;
        _linkedSaveSyncMarkerPath = linkedSaveDocumentId is null ? null : batterySavePath + ".synced";
        _linkedSaveDocumentId = linkedSaveDocumentId;
        _saveFileAccess = saveFileAccess;
        Generation = generation;
        QuickStatePath = StatePathForRom(romPath, romFileName);
        HasSaveConflict = hasSaveConflict;
    }

    /// <summary>True when libpkforge_mgba.so is packed in this APK (the embedded core can run).
    /// Also publishes the flag to EmulatorCatalog.EmbeddedCoreAvailable for the Emulators tab.</summary>
    public static bool IsAvailable
    {
        get
        {
            try
            {
                var available = MgbaNative.CoreAvailable() != 0;
                Domain.EmulatorCatalog.EmbeddedCoreAvailable = available;
                return available;
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            catch (BadImageFormatException) { }
            Domain.EmulatorCatalog.EmbeddedCoreAvailable = false;
            return false;
        }
    }

    /// <summary>The battery save path for a ROM or a linked save document.</summary>
    public static string SavePathForRom(string romPath, string? saveDocumentId = null)
    {
        if (!string.IsNullOrWhiteSpace(saveDocumentId))
            return PrivateFilePath("linked-saves", saveDocumentId, ".sav");
        if (IsContentUri(romPath))
            return PrivateFilePath("battery-saves", romPath, ".sav");
        if (Uri.TryCreate(romPath, UriKind.Absolute, out var fileUri) && fileUri.IsFile)
            romPath = fileUri.LocalPath;
        return Path.ChangeExtension(romPath, ".sav")
            ?? throw new ArgumentException("The ROM path cannot produce a battery save path.", nameof(romPath));
    }

    /// <summary>
    /// Loads a supported Gen 1-5 ROM (and its save when given) and starts the emulation
    /// thread. Failure is returned to the caller; integrated generations never fall back
    /// to an external emulator.
    /// </summary>
    public static EmbeddedGameStartResult TryStart(
        string romPath,
        int generation = 0,
        string? romFileName = null,
        string? linkedSaveDocumentId = null,
        ISaveFileAccess? saveFileAccess = null)
    {
        var sourceName = romFileName ?? Path.GetFileName(romPath);
        if (!RomLinkRules.UsesIntegratedCore(generation))
            generation = RomLinkRules.ResolveIntegratedGeneration(generation, sourceName);
        if (!RomLinkRules.UsesIntegratedCore(generation))
            return new EmbeddedGameStartResult(null,
                $"PKForge n'a pas encore de cœur intégré pour la génération {generation}.");
        if (!RomLinkRules.IsSupportedEmbeddedFormat(generation, sourceName))
        {
            var expected = generation switch
            {
                1 or 2 => ".gb ou .gbc",
                3 => ".gba",
                4 or 5 => ".nds ou .zip contenant une seule ROM .nds",
                _ => "un format pris en charge",
            };
            return new EmbeddedGameStartResult(null,
                $"Format ROM non pris en charge par le cœur intégré : {Path.GetExtension(sourceName)}. Format attendu : {expected}.");
        }

        if (generation is 4 or 5)
            return NintendoDsGameSession.TryStart(romPath, generation, romFileName, linkedSaveDocumentId, saveFileAccess);

        MgbaCore? core = null;
        EmbeddedEngine? engine = null;
        try
        {
            var localRomPath = MaterializeRom(romPath, romFileName);
            var savePath = SavePathForRom(romPath, linkedSaveDocumentId);
            Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
            var hasSaveConflict = false;
            if (linkedSaveDocumentId is not null)
            {
                if (saveFileAccess is null)
                    throw new InvalidOperationException("The linked save file service is unavailable.");
                var originalSave = saveFileAccess.ReadAsync(linkedSaveDocumentId).AsTask().GetAwaiter().GetResult();
                hasSaveConflict = ReconcileLinkedSave(savePath, savePath + ".synced", originalSave.ToArray());
            }

            core = new MgbaCore();
            if (!core.Load(localRomPath, savePath))
            {
                var reason = core.LoadError ?? "mGBA n'a pas pu charger cette ROM.";
                Services.AppLog.Error("embedded", $"Could not load {Path.GetFileName(romPath)}: {reason}", new InvalidOperationException(reason));
                core.Dispose();
                return new EmbeddedGameStartResult(null, reason);
            }
            engine = new EmbeddedEngine(core);
            var session = new EmbeddedGameSession(
                core, engine, romPath, romFileName, savePath, generation, linkedSaveDocumentId, saveFileAccess, hasSaveConflict);
            engine.Start();
            return new EmbeddedGameStartResult(session, null);
        }
        catch (DllNotFoundException error)
        {
            var reason = $"Android n'a pas pu charger le cœur mGBA : {error.Message}";
            Services.AppLog.Error("embedded", reason, error);
            engine?.Dispose();
            core?.Dispose();
            return new EmbeddedGameStartResult(null, reason);
        }
        catch (EntryPointNotFoundException error)
        {
            const string reason = "Le cœur mGBA de l'APK est trop ancien pour cette version de PKForge.";
            Services.AppLog.Error("embedded", reason, error);
            engine?.Dispose();
            core?.Dispose();
            return new EmbeddedGameStartResult(null, reason);
        }
        catch (BadImageFormatException error)
        {
            const string reason = "L'architecture du cœur mGBA ne correspond pas à cet appareil. Installe l'APK arm64.";
            Services.AppLog.Error("embedded", reason, error);
            engine?.Dispose();
            core?.Dispose();
            return new EmbeddedGameStartResult(null, reason);
        }
        catch (Exception error)
        {
            Services.AppLog.Error("embedded", $"Could not start {Path.GetFileName(romPath)}", error);
            engine?.Dispose();
            core?.Dispose();
            return new EmbeddedGameStartResult(null, $"{error.GetType().Name}: {error.Message}");
        }
    }

    internal static string StatePathForRom(string romPath, string? romFileName)
    {
        var fileName = Path.GetFileNameWithoutExtension(romFileName ?? romPath);
        return PrivateFilePath("states", romPath, ".ss0", fileName);
    }

    internal static string PrivateFilePath(string folder, string identity, string extension, string? prefix = null)
    {
        var hash = HashIdentity(identity);
        var safePrefix = string.IsNullOrWhiteSpace(prefix) ? "game" : SafeFileName(prefix);
        return Path.Combine(FileSystem.AppDataDirectory, "emulator", folder, $"{safePrefix}-{hash}{extension}");
    }

    internal static string HashIdentity(string identity) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16];

    internal static bool IsContentUri(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme.Equals("content", StringComparison.OrdinalIgnoreCase);

    internal static string MaterializeRom(string romPath, string? romFileName)
    {
        if (!IsContentUri(romPath))
        {
            if (Uri.TryCreate(romPath, UriKind.Absolute, out var fileUri)
                && fileUri.IsFile)
                return fileUri.LocalPath;
            return romPath;
        }

        var directory = Path.Combine(FileSystem.CacheDirectory, "embedded-roms");
        Directory.CreateDirectory(directory);
        var name = SafeFileName(romFileName ?? "game.gba");
        var extension = Path.GetExtension(name);
        var stem = Path.GetFileNameWithoutExtension(name);
        var path = Path.Combine(directory, $"{stem}-{HashIdentity(romPath)}{extension}");
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            var uri = global::Android.Net.Uri.Parse(romPath)
                ?? throw new ArgumentException("The ROM document URI is invalid.", nameof(romPath));
            using var input = global::Android.App.Application.Context.ContentResolver?.OpenInputStream(uri)
                ?? throw new IOException("The ROM document provider could not open this file.");
            using (var output = File.Create(temporaryPath))
                input.CopyTo(output);
            File.Move(temporaryPath, path, overwrite: true);
            return path;
        }
        catch
        {
            try { File.Delete(temporaryPath); } catch (IOException) { }
            throw;
        }
    }

    /// <summary>Materializes a DS ROM or extracts the only .nds entry from a ZIP into app cache.</summary>
    internal static string MaterializeNintendoDsRom(string romPath, string? romFileName)
    {
        var sourceName = romFileName ?? Path.GetFileName(romPath);
        var extension = Path.GetExtension(sourceName);
        var localPath = MaterializeRom(romPath, sourceName);
        if (extension.Equals(".nds", StringComparison.OrdinalIgnoreCase))
            return localPath;
        if (!extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Sélectionne un fichier .nds ou une archive .zip.");

        using var source = File.OpenRead(localPath);
        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: false);
        var entries = archive.Entries
            .Where(entry => !entry.FullName.EndsWith('/') &&
                Path.GetExtension(entry.FullName).Equals(".nds", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (entries.Length == 0)
            throw new InvalidDataException("L'archive ne contient aucune ROM .nds.");
        if (entries.Length > 1)
            throw new InvalidDataException("L'archive contient plusieurs ROM .nds; conserve uniquement le jeu à lancer.");

        var rom = entries[0];
        const long maximumRomSize = 512L * 1024 * 1024;
        if (rom.Length <= 0 || rom.Length > maximumRomSize)
            throw new InvalidDataException("La taille décompressée de la ROM dépasse la limite acceptée.");

        var cacheDirectory = Path.Combine(FileSystem.CacheDirectory, "embedded-roms", "nds");
        Directory.CreateDirectory(cacheDirectory);
        var cacheIdentity = $"{romPath}\n{rom.FullName}";
        var outputPath = Path.Combine(cacheDirectory, $"{HashIdentity(cacheIdentity)}.nds");
        var temporaryPath = $"{outputPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using var input = rom.Open();
            using (var output = File.Create(temporaryPath))
            {
                var buffer = new byte[64 * 1024];
                long copied = 0;
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
                {
                    copied += read;
                    if (copied > maximumRomSize || copied > rom.Length)
                        throw new InvalidDataException("L'entrée .nds décompressée dépasse sa taille annoncée.");
                    output.Write(buffer, 0, read);
                }

                if (copied != rom.Length)
                    throw new InvalidDataException("La ROM .nds extraite est incomplète.");
            }

            File.Move(temporaryPath, outputPath, overwrite: true);
            return outputPath;
        }
        catch
        {
            try { File.Delete(temporaryPath); } catch (IOException) { }
            throw;
        }
    }

    internal static string SafeFileName(string value)
    {
        var fileName = Path.GetFileName(value);
        if (string.IsNullOrWhiteSpace(fileName)) return "game";
        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(invalidCharacter, '_');
        return fileName;
    }

    internal static bool ReconcileLinkedSave(string savePath, string markerPath, byte[] linkedBytes)
    {
        if (!File.Exists(savePath))
        {
            WriteFileAtomically(savePath, linkedBytes);
            TryWriteSyncMarker(markerPath, linkedBytes);
            return false;
        }

        var localBytes = File.ReadAllBytes(savePath);
        if (localBytes.AsSpan().SequenceEqual(linkedBytes))
        {
            TryWriteSyncMarker(markerPath, linkedBytes);
            return false;
        }

        var marker = ReadSyncMarker(markerPath);
        if (marker is not null)
        {
            var localMatchesMarker = ContentHash(localBytes).Equals(marker, StringComparison.Ordinal);
            var linkedMatchesMarker = ContentHash(linkedBytes).Equals(marker, StringComparison.Ordinal);
            if (localMatchesMarker && !linkedMatchesMarker)
            {
                WriteFileAtomically(savePath, linkedBytes);
                TryWriteSyncMarker(markerPath, linkedBytes);
                return false;
            }
            if (linkedMatchesMarker && !localMatchesMarker)
                return false; // The private copy contains progress not yet written through SAF.
        }

        Services.AppLog.Warn("embedded", "The linked save and private game save both changed; preserving both copies.");
        return true;
    }

    internal static string ContentHash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));

    internal static string? ReadSyncMarker(string markerPath)
    {
        try
        {
            return File.Exists(markerPath) ? File.ReadAllText(markerPath).Trim() : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Services.AppLog.Warn("embedded", $"Could not read the linked save marker: {error.Message}");
            return null;
        }
    }

    internal static void TryWriteSyncMarker(string markerPath, byte[] bytes)
    {
        try { WriteFileAtomically(markerPath, Encoding.ASCII.GetBytes(ContentHash(bytes))); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Services.AppLog.Warn("embedded", $"Could not update the linked save marker: {error.Message}");
        }
    }

    internal static void WriteFileAtomically(string path, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporaryPath, bytes);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Sets the held buttons (thread-safe).</summary>
    public void SetKeys(uint bitmask) => _engine?.SetKeys(bitmask);

    public void SetTouchScreen(bool down, int x, int y) { }

    /// <summary>Draws the newest frame, pixel-crisp.</summary>
    public void Draw(SKCanvas canvas, SKRect destination) => _engine?.Draw(canvas, destination);

    /// <summary>Reads the live game memory — the companion's data bridge.</summary>
    public bool ReadBytes(uint address, Span<byte> destination) =>
        _engine?.ReadBytes(address, destination) == true;

    /// <summary>
    /// The companion snapshot read from the running core's battery-save image. The
    /// game writes that image when it saves; unsaved changes remain in emulated RAM.
    /// </summary>
    public LiveCompanionSnapshot? ReadLiveCompanion()
    {
        var maxSpecies = Generation switch
        {
            1 => 151,
            2 => 251,
            3 => MaxSpeciesGen3,
            _ => 0,
        };
        return maxSpecies == 0
            ? null
            : _engine?.ReadLiveCompanion(Generation, maxSpecies, _romFileName);
    }

    /// <summary>Pauses or resumes emulation while keeping the loaded game and battery save open.</summary>
    public void SetPaused(bool paused) => _engine?.SetPaused(paused);

    public void SetSpeedMultiplier(int multiplier) => _engine?.SetSpeedMultiplier(multiplier);

    /// <summary>Pauses at a frame boundary and copies a linked battery save back to Android storage.</summary>
    public async Task<bool> FlushBatterySaveAsync()
    {
        var engine = _engine;
        if (engine is not null)
        {
            engine.SetPaused(true);
            if (!engine.FlushSave(_batterySavePath))
            {
                Services.AppLog.Warn("embedded", $"Could not flush the battery save for {GameTitle}.");
                return false;
            }
        }
        return await SyncLinkedSaveAsync(sessionClosed: false).ConfigureAwait(false);
    }

    /// <summary>Saves a quick state atomically in PKForge's private data folder.</summary>
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
            Services.AppLog.Warn("embedded", $"Could not save a quick state for {GameTitle}: {error.Message}");
            try { File.Delete(temporaryPath); } catch (IOException) { }
            return false;
        }
    }

    /// <summary>Loads the last quick state for this ROM.</summary>
    public bool LoadQuickState() => HasQuickState && _engine?.LoadState(QuickStatePath) == true;

    private const int MaxSpeciesGen3 = 386; // PKHeX Legal.MaxSpeciesID_3

    public Task<bool> CloseAsync()
    {
        lock (_disposeGate)
            return _closeTask ??= CloseCoreAsync();
    }

    private async Task<bool> CloseCoreAsync()
    {
        var gameTitle = GameTitle;
        var engine = _engine;
        _engine = null;
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
                        saveFlushed = engine.FlushSave(_batterySavePath);
                    }
                }
                finally
                {
                    engine?.Dispose();
                    _core.Dispose();
                }
            }).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            Services.AppLog.Error("embedded", $"Could not stop {gameTitle}", error);
            return false;
        }

        if (!saveFlushed)
            Services.AppLog.Warn("embedded", $"Could not flush the battery save for {gameTitle}.");
        if (!saveFlushed) return false;
        return await SyncLinkedSaveAsync(sessionClosed: true).ConfigureAwait(false);
    }

    private async Task<bool> SyncLinkedSaveAsync(bool sessionClosed)
    {
        if (_linkedSaveDocumentId is null || _saveFileAccess is null)
            return true;

        await _saveSyncGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var bytes = await File.ReadAllBytesAsync(_batterySavePath).ConfigureAwait(false);
            var linkedBytes = (await _saveFileAccess.ReadAsync(_linkedSaveDocumentId).ConfigureAwait(false)).ToArray();
            if (!bytes.AsSpan().SequenceEqual(linkedBytes))
            {
                var marker = _linkedSaveSyncMarkerPath is null ? null : ReadSyncMarker(_linkedSaveSyncMarkerPath);
                if (marker is null)
                    return SaveSyncConflict();

                var localMatchesMarker = ContentHash(bytes).Equals(marker, StringComparison.Ordinal);
                var linkedMatchesMarker = ContentHash(linkedBytes).Equals(marker, StringComparison.Ordinal);
                if (localMatchesMarker && !linkedMatchesMarker)
                {
                    if (!sessionClosed) return SaveSyncConflict();
                    WriteFileAtomically(_batterySavePath, linkedBytes);
                    if (_linkedSaveSyncMarkerPath is not null) TryWriteSyncMarker(_linkedSaveSyncMarkerPath, linkedBytes);
                    HasSaveConflict = false;
                    return true; // The core is closed, so the external update can safely replace its stale copy.
                }
                if (!localMatchesMarker || !linkedMatchesMarker)
                {
                    if (!linkedMatchesMarker) return SaveSyncConflict();
                }
            }
            await _saveFileAccess.WriteAtomicallyAsync(_linkedSaveDocumentId, bytes).ConfigureAwait(false);
            if (_linkedSaveSyncMarkerPath is not null) TryWriteSyncMarker(_linkedSaveSyncMarkerPath, bytes);
            HasSaveConflict = false;
            return true;
        }
        catch (Exception error)
        {
            Services.AppLog.Error("embedded", $"Could not write the game save back to {_linkedSaveDocumentId}", error);
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
        Services.AppLog.Warn("embedded", "The linked save changed during play; neither copy was overwritten.");
        return false;
    }

    public void Dispose() => CloseAsync().GetAwaiter().GetResult();

    public ValueTask DisposeAsync() => new(CloseAsync());
}
