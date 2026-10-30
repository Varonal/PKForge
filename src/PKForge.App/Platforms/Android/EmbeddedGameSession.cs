using PKForge.Domain;
using SkiaSharp;

namespace PKForge.App.Platforms.Android;

/// <summary>
/// One running game: the core plus its engine, owned together. Created by the
/// Play flow when the embedded library is present; disposed when the player
/// leaves the game screen. The pages and the second-screen companion both read
/// through this facade.
/// </summary>
public sealed class EmbeddedGameSession : IDisposable
{
    private readonly MgbaCore _core;
    private EmbeddedEngine? _engine;

    /// <summary>Raised on the emu thread after each frame; no-ops once disposed.</summary>
    public event Action? FrameProduced
    {
        add { if (_engine is not null) _engine.FrameProduced += value; }
        remove { if (_engine is not null) _engine.FrameProduced -= value; }
    }

    public (int Width, int Height) VideoSize => _core.VideoSize;
    public string GameTitle => _core.GameTitle;
    public string GameCode => _core.GameCode;

    private EmbeddedGameSession(MgbaCore core, EmbeddedEngine engine)
    {
        _core = core;
        _engine = engine;
    }

    /// <summary>
    /// Loads a ROM (and its save when given) and starts the emu thread. Returns
    /// null when the embedded core is unavailable (no library) or the file is not
    /// a GBA/GB ROM — the caller falls back to the external emulator path.
    /// </summary>
    /// <summary>True when libpkforge_mgba.so is packed in this APK (the embedded core can run).</summary>
    public static bool IsAvailable
    {
        get
        {
            try
            {
                var core = new MgbaCore();
                core.Dispose();
                Domain.EmulatorCatalog.EmbeddedCoreAvailable = true;
                return true;
            }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
        }
    }

    public static EmbeddedGameSession? TryStart(string romPath, string? savePath)
    {
        try
        {
            var core = new MgbaCore();
            if (!core.Load(romPath, savePath)) { core.Dispose(); return null; }
            var engine = new EmbeddedEngine(core);
            var session = new EmbeddedGameSession(core, engine);
            engine.Start();
            return session;
        }
        catch (DllNotFoundException)
        {
            return null; // the .so is not packed in this build — no embedded play
        }
        catch (EntryPointNotFoundException)
        {
            return null; // the .so is older than the shim — no embedded play
        }
    }

    /// <summary>Sets the held buttons (thread-safe).</summary>
    public void SetKeys(uint bitmask) => _engine?.SetKeys(bitmask);

    /// <summary>Draws the newest frame, pixel-crisp.</summary>
    public void Draw(SKCanvas canvas, SKRect destination) => _engine?.Draw(canvas, destination);

    /// <summary>Reads the live game memory — the companion's data bridge.</summary>
    public bool ReadBytes(uint address, Span<byte> destination) => _core.ReadBytes(address, destination);

    /// <summary>
    /// The live companion snapshot (dex, party, playtime) read from the running
    /// core's save RAM. Null when the game isn't a Gen 3 GBA title or the memory
    /// is unreadable. Gen 1/2 (GB/GBC) saves live elsewhere and are not read live.
    /// </summary>
    public LiveGbaCompanion.LiveCompanionSnapshot? ReadLiveCompanion()
    {
        var code = _core.GameCode;
        if (!IsGen3Code(code)) return null;
        return LiveGbaCompanion.Read(_core, MaxSpeciesGen3);
    }

    private static bool IsGen3Code(string code) => code is "BPRE" or "BPEE" or "BPEI" or "BPED"
        or "BPSO" or "BPEO" or "AXVE" or "AXPE" or "AXVI" or "AXPI" or "AXVD" or "AXPD";

    private const int MaxSpeciesGen3 = 386; // PKHeX Legal.MaxSpeciesID_3

    public void Dispose()
    {
        _engine?.Dispose();
        _engine = null;
        _core.Dispose();
    }
}
