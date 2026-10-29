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
            return null; // the .so is not packed in this build — external emulators only
        }
        catch (EntryPointNotFoundException)
        {
            return null; // the .so is older than the shim — external emulators only
        }
    }

    /// <summary>Sets the held buttons (thread-safe).</summary>
    public void SetKeys(uint bitmask) => _engine?.SetKeys(bitmask);

    /// <summary>Draws the newest frame, pixel-crisp.</summary>
    public void Draw(SKCanvas canvas, SKRect destination) => _engine?.Draw(canvas, destination);

    /// <summary>Reads the live game memory — the companion's data bridge.</summary>
    public bool ReadBytes(uint address, Span<byte> destination) => _core.ReadBytes(address, destination);

    public void Dispose()
    {
        _engine?.Dispose();
        _engine = null;
        _core.Dispose();
    }
}
