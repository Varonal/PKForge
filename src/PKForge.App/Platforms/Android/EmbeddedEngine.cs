using System.Diagnostics;
using System.Runtime.InteropServices;
using Android.Media;
using PKForge.Domain;
using SkiaSharp;

namespace PKForge.App.Platforms.Android;

/// <summary>
/// Drives an <see cref="MgbaCore"/> on its own thread at the handheld's frame
/// rate (about 59.7275 Hz), and hands each finished frame to the UI as a
/// ready-to-draw SKBitmap. The UI thread never touches the core;
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
    private readonly ManualResetEventSlim _resume = new(initialState: true);
    private readonly object _coreGate = new();
    private readonly object _lifecycleGate = new();
    private readonly object _audioGate = new();
    private readonly object _frameGate = new();
    private SKBitmap? _frame;
    private SKImage? _frameImage;

    // mGBA's audio output: 32768 Hz stereo, about 548 samples per frame.
    private const int AudioFramesPerDrain = 1024;
    private readonly short[] _audioScratch = new short[AudioFramesPerDrain * 2];
    private readonly Java.Nio.ByteBuffer _audioBuffer;
    private readonly AudioTrack? _track;
    private volatile uint _keysPending;
    private readonly object _keyGate = new();
    private int _speedMultiplier = 1;
    private volatile bool _audioEnabled = true;
    private volatile bool _disposed;

    /// <summary>Raised on the emu thread after each frame; the UI marshals itself.</summary>
    public event Action? FrameProduced;

    public bool IsRunning => _thread.IsAlive;

    public EmbeddedEngine(MgbaCore core)
    {
        _core = core ?? throw new ArgumentNullException(nameof(core));
        if (!core.IsRunning) throw new ArgumentException("The core has no ROM loaded.", nameof(core));
        _thread = new Thread(RunLoop) { IsBackground = true, Name = "PKForge emu" };
        _audioBuffer = Java.Nio.ByteBuffer.AllocateDirect(_audioScratch.Length * sizeof(short));
        _audioBuffer.Order(Java.Nio.ByteOrder.LittleEndian!);
        try
        {
            // 32768 Hz is the GBA's own rate. The buffer holds ~116 ms of audio
            // (3840 frames × 4 bytes): responsive, and one GC pause never starves.
#pragma warning disable CA1416 // AudioTrack.Builder needs API 23+; minSdk is 21 but every shipped device runs 23+.
            var attributesBuilder = new AudioAttributes.Builder();
            attributesBuilder.SetUsage(AudioUsageKind.Game);
            attributesBuilder.SetContentType(AudioContentType.Music);
            var attributes = attributesBuilder.Build()!;

            var formatBuilder = new AudioFormat.Builder();
            formatBuilder.SetEncoding(Encoding.Pcm16bit);
            formatBuilder.SetSampleRate(32768);
            formatBuilder.SetChannelMask(ChannelOut.Stereo);
            var format = formatBuilder.Build()!;

            var trackBuilder = new AudioTrack.Builder();
            trackBuilder.SetAudioAttributes(attributes);
            trackBuilder.SetAudioFormat(format);
            trackBuilder.SetBufferSizeInBytes(3840 * 4);
            trackBuilder.SetTransferMode(AudioTrackMode.Stream);
            _track = trackBuilder.Build();
#pragma warning restore CA1416
            _track.Play();
        }
        catch (Exception)
        {
            try { _track?.Stop(); } catch (Exception) { }
            try { _track?.Release(); } catch (Exception) { }
            _track = null; // no audio device: the game still plays, silently
        }
    }

    public void Start() => _thread.Start();

    /// <summary>Sets the held buttons (thread-safe; latched for the next frame).</summary>
    public void SetKeys(uint bitmask)
    {
        lock (_keyGate) _keysPending = bitmask;
    }

    public void SetPaused(bool paused)
    {
        lock (_lifecycleGate)
        {
            if (_disposed) return;
            if (paused)
            {
                _resume.Reset();
                lock (_audioGate)
                    try { _track?.Pause(); } catch (Exception) { }
                lock (_coreGate) { } // wait until any in-flight frame has finished
            }
            else
            {
                _resume.Set();
                if (Volatile.Read(ref _speedMultiplier) == 1)
                    lock (_audioGate)
                        try { _track?.Play(); } catch (Exception) { }
            }
        }
    }

    public void SetSpeedMultiplier(int multiplier)
    {
        if (multiplier is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(multiplier), "The emulation speed must be between ×1 and ×4.");

        var previous = Interlocked.Exchange(ref _speedMultiplier, multiplier);
        if (previous == multiplier || _track is null) return;

        lock (_audioGate)
        {
            try
            {
                if (multiplier > 1)
                {
                    _track.Pause();
                    _track.Flush();
                }
                else if (_resume.IsSet)
                {
                    _track.Play();
                }
            }
            catch (Exception error)
            {
                Services.AppLog.Warn("embedded", $"Could not update audio for emulation speed ×{multiplier}: {error.Message}");
            }
        }
    }

    public bool ReadBytes(uint address, Span<byte> destination)
    {
        lock (_coreGate)
            return !_disposed && _core.ReadBytes(address, destination);
    }

    public LiveCompanionSnapshot? ReadLiveCompanion(
        int generation,
        int maxSpecies,
        string romFileName)
    {
        lock (_coreGate)
        {
            if (_disposed) return null;

            // mGBA's savedata clone is the canonical battery image for each cartridge
            // type. The GBA flash save can be 128 KiB while its bus window is only 64 KiB.
            var saveRam = new byte[0x20000];
            var saveSize = _core.CopySavedata(saveRam);
            if (saveSize <= 0) return null;
            if (saveSize != saveRam.Length) Array.Resize(ref saveRam, saveSize);

            if (generation is 1 or 2)
                return LiveGen12SaveReader.Read(saveRam, generation, _core.GameTitle, _core.GameCode, romFileName);
            if (generation != 3 || saveSize < 0x1C000)
                return null;

            // A partially written Gen 3 save must not be surfaced to the companion.
            var companionData = LiveGen3SaveReader.Read(saveRam, _core.GameCode);
            if (companionData is null) return null;
            var snapshot = LiveGbaCompanion.Read(_core.GameTitle, _core.GameCode, saveRam, maxSpecies);
            if (snapshot is null) return null;
            return snapshot with { Party = companionData.Party, Bag = companionData.Bag };
        }
    }

    public bool SaveState(string path)
    {
        lock (_coreGate)
            return !_disposed && _core.IsRunning && _core.SaveState(path);
    }

    public bool LoadState(string path)
    {
        lock (_coreGate)
            return !_disposed && _core.IsRunning && _core.LoadState(path);
    }

    public bool FlushSave(string path)
    {
        lock (_coreGate)
            return !_disposed && _core.IsRunning && _core.FlushSave(path);
    }

    /// <summary>Draws the newest frame into the canvas, scaled to fit, pixel-crisp.</summary>
    public void Draw(SKCanvas canvas, SKRect destination)
    {
        lock (_frameGate)
        {
            if (_frameImage is null) return;
            // Nearest-neighbour: the GBA's pixels must stay square and sharp,
            // never smoothed — an integer scale of 240×160.
            var sampling = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
            canvas.DrawImage(_frameImage, destination, sampling);
        }
    }

    private void RunLoop()
    {
        try
        {
            var (width, height) = _core.VideoSize;
            var frame = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
            lock (_frameGate) _frame = frame;
            var basePeriodTicks = Stopwatch.Frequency / FramesPerSecond;
            var frameCount = 0;
            // Keep fractional clock ticks so timing error does not accumulate.
            double deadline = Stopwatch.GetTimestamp();

            while (!_cancel.IsCancellationRequested)
            {
                if (!_resume.IsSet)
                {
                    _resume.Wait(TimeSpan.FromMilliseconds(50));
                    deadline = Stopwatch.GetTimestamp();
                    continue;
                }

                var speedMultiplier = Volatile.Read(ref _speedMultiplier);
                var framePeriodTicks = basePeriodTicks / speedMultiplier;
                var publishFrame = ++frameCount % speedMultiplier == 0;

                int audioFrames;
                lock (_coreGate)
                {
                    if (_cancel.IsCancellationRequested) break;
                    if (!_resume.IsSet) continue;

                    // Latch the newest input, dropping anything older (a toggle during a frame).
                    uint keys;
                    lock (_keyGate) keys = _keysPending;
                    _core.SetKeys(keys);
                    _core.RunFrame();

                    // mGBA's color_t is 0x00BBGGRR: bytes R,G,B,X match Rgba8888.
                    if (publishFrame)
                        _core.CopyFrame(MemoryMarshal.Cast<byte, uint>(frame.GetPixelSpan()));
                    audioFrames = _core.ReadAudio(_audioScratch, AudioFramesPerDrain);
                }

                if (publishFrame)
                {
                    lock (_frameGate)
                    {
                        _frameImage?.Dispose();
                        _frameImage = SKImage.FromBitmap(frame);
                    }
                    try { FrameProduced?.Invoke(); }
                    catch (Exception error) { Services.AppLog.Warn("embedded", $"A frame listener failed: {error.Message}"); }
                }

                // The audio clock paces the loop when AudioTrack's buffer fills.
                if (audioFrames > 0 && _track is not null && _audioEnabled && speedMultiplier == 1)
                {
                    lock (_audioGate)
                    {
                        if (Volatile.Read(ref _speedMultiplier) == 1)
                        {
                            try
                            {
                                _audioBuffer.Rewind();
                                _audioBuffer.AsShortBuffer().Put(_audioScratch, 0, audioFrames * 2);
                                _audioBuffer.Rewind();
                                if (_track.Write(_audioBuffer, audioFrames * 4, WriteMode.Blocking) < 0)
                                {
                                    _audioEnabled = false;
                                    Services.AppLog.Warn("embedded", "Game audio stopped after an AudioTrack write error.");
                                }
                            }
                            catch (Exception error)
                            {
                                _audioEnabled = false;
                                Services.AppLog.Warn("embedded", $"Game audio stopped: {error.Message}");
                            }
                        }
                    }
                }

                // Carry timing error forward so scheduling jitter does not accumulate.
                deadline += framePeriodTicks;
                var remaining = TimeSpan.FromSeconds((deadline - Stopwatch.GetTimestamp()) / Stopwatch.Frequency);
                if (remaining > TimeSpan.Zero)
                {
                    // Sleep with a 1 ms margin, then spin the sub-millisecond slice.
                    var coarseSleep = remaining - TimeSpan.FromMilliseconds(1);
                    if (coarseSleep > TimeSpan.Zero) Thread.Sleep(coarseSleep);
                    while (Stopwatch.GetTimestamp() < deadline && !_cancel.IsCancellationRequested)
                        Thread.SpinWait(64);
                }
                else if (remaining < TimeSpan.FromMilliseconds(-100))
                {
                    deadline = Stopwatch.GetTimestamp(); // recover without unbounded catch-up at higher speeds
                }
            }
        }
        catch (Exception error)
        {
            Services.AppLog.Error("embedded", "The emulation thread stopped", error);
        }
    }

    private void DisposeFrame()
    {
        lock (_frameGate)
        {
            _frameImage?.Dispose();
            _frameImage = null;
            _frame?.Dispose();
            _frame = null;
        }
    }

    public void Dispose()
    {
        lock (_lifecycleGate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _cancel.Cancel();
        _resume.Set();
        try { _track?.Stop(); } catch (Exception) { }
        try
        {
            if (_thread.ThreadState != System.Threading.ThreadState.Unstarted)
                _thread.Join();
        }
        catch (ThreadStateException) { }
        FrameProduced = null;
        DisposeFrame();
        try { _track?.Release(); } catch (Exception) { }
        _resume.Dispose();
        _cancel.Dispose();
    }
}
