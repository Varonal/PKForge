using System.Diagnostics;
using SkiaSharp;

namespace PKForge.App.Platforms.Android;

/// <summary>
/// Drives an <see cref="MgbaCore"/> on its own thread at the GBA's exact frame
/// rate (59.7275 Hz — the hardware's, not 60), and hands each finished frame to
/// the UI as a ready-to-draw SKBitmap. The UI thread never touches the core;
/// the engine never touches the UI except through the frame event.
/// </summary>
internal sealed class EmbeddedEngine : IDisposable
{
    // The GBA's PPU emits one frame every 123 us × 912 = 280896 cycles at
    // 16777216 Hz → 59.7275 frames per second. Rounding to 60 loses sync with
    // the audio clock over minutes (audible drift on long sessions).
    private const double FramesPerSecond = 59.727468;

    private readonly MgbaCore _core;
    private readonly Thread _thread;
    private readonly CancellationTokenSource _cancel = new();
    private readonly object _frameGate = new();
    private SKBitmap? _frame;

    private uint _keys;
    private volatile uint _keysPending;
    private readonly object _keyGate = new();

    /// <summary>Raised on the emu thread after each frame; the UI marshals itself.</summary>
    public event Action? FrameProduced;

    public bool IsRunning => _thread.IsAlive;

    public EmbeddedEngine(MgbaCore core)
    {
        _core = core ?? throw new ArgumentNullException(nameof(core));
        if (!core.IsRunning) throw new ArgumentException("The core has no ROM loaded.", nameof(core));
        _thread = new Thread(RunLoop) { IsBackground = true, Name = "PKForge emu" };
    }

    public void Start() => _thread.Start();

    /// <summary>Sets the held buttons (thread-safe; latched for the next frame).</summary>
    public void SetKeys(uint bitmask)
    {
        lock (_keyGate) _keysPending = bitmask;
    }

    /// <summary>Draws the newest frame into the canvas, scaled to fit, pixel-crisp.</summary>
    public void Draw(SKCanvas canvas, SKRect destination)
    {
        lock (_frameGate)
        {
            if (_frame is null) return;
            // Nearest-neighbour: the GBA's pixels must stay square and sharp,
            // never smoothed — an integer scale of 240×160.
            var sampling = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
            canvas.DrawBitmap(_frame, destination, sampling);
        }
    }

    private void RunLoop()
    {
        var (width, height) = _core.VideoSize;
        var frame = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        var periodTicks = Stopwatch.Frequency / FramesPerSecond;
        // The deadline accumulates: each frame is due periodTicks after the last
        // one, so per-frame jitter never accumulates into drift (audio stays in
        // sync over hours).
        long deadline = Stopwatch.GetTimestamp();

        while (!_cancel.IsCancellationRequested)
        {
            // Latch the newest input, drop anything older (a toggle during a frame).
            uint keys;
            lock (_keyGate) keys = _keysPending;
            _core.SetKeys(keys);
            _core.RunFrame();

            // The frame is mGBA's 0x00BBGGRR little-endian: bytes R,G,B,X — the
            // bitmap's Rgba8888 row layout, copied as-is.
            _core.CopyFrame(frame.GetPixelSpan().As<uint>());

            lock (_frameGate)
            {
                _frame?.Dispose();
                _frame = frame;
            }
            FrameProduced?.Invoke();

            // Pace to the hardware rate. Sleep granularity (~15 ms on some
            // kernels) is corrected by measuring and carrying the error forward.
            deadline += (long)periodTicks;
            var remaining = TimeSpan.FromSeconds((double)(deadline - Stopwatch.GetTimestamp()) / Stopwatch.Frequency);
            if (remaining > TimeSpan.Zero)
            {
                // Sleep with a 1 ms margin, then spin the sub-millisecond slice:
                // Sleep alone drifts with the scheduler's granularity.
                Thread.Sleep(remaining + TimeSpan.FromMilliseconds(1));
                while (Stopwatch.GetTimestamp() < deadline)
                    Thread.SpinWait(64);
            }
        }
        frame.Dispose();
    }

    public void Dispose()
    {
        _cancel.Cancel();
        try { _thread.Join(500); } catch (ThreadStateException) { }
        lock (_frameGate)
        {
            _frame?.Dispose();
            _frame = null;
        }
        _cancel.Dispose();
    }
}
