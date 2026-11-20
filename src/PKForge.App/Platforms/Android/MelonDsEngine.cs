using System.Diagnostics;
using System.Runtime.InteropServices;
using Android.Media;
using PKForge.Domain;
using SkiaSharp;

namespace PKForge.App.Platforms.Android;

/// <summary>Drives one melonDS instance, audio stream and dual-screen frame.</summary>
internal sealed class MelonDsEngine : IDisposable
{
    private const double FramesPerSecond = 59.8261;
    private const int AudioRate = 32768;
    private const int AudioFramesPerDrain = 1024;
    private const int SaveFlushIntervalFrames = 300;

    private readonly MelonDsCore _core;
    private readonly Thread _thread;
    private readonly CancellationTokenSource _cancel = new();
    private readonly ManualResetEventSlim _resume = new(initialState: true);
    private readonly object _coreGate = new();
    private readonly object _lifecycleGate = new();
    private readonly object _audioGate = new();
    private readonly object _frameGate = new();
    private readonly object _inputGate = new();
    private readonly short[] _audioScratch = new short[AudioFramesPerDrain * 2];
    private readonly uint[] _frameScratch = new uint[256 * 384];
    private readonly Java.Nio.ByteBuffer _audioBuffer;
    private readonly AudioTrack? _track;
    private SKBitmap? _frame;
    private SKImage? _frameImage;
    private uint _keysPending;
    private int _speedMultiplier = 1;
    private byte[]? _companionSaveBuffer;
    private byte[]? _lastCompanionSave;
    private LiveCompanionSnapshot? _lastCompanionSnapshot;
    private bool _touchDown;
    private int _touchX;
    private int _touchY;
    private volatile bool _audioEnabled = true;
    private volatile bool _disposed;

    public event Action? FrameProduced;
    public bool IsRunning => _thread.IsAlive;

    public MelonDsEngine(MelonDsCore core)
    {
        _core = core ?? throw new ArgumentNullException(nameof(core));
        if (!core.IsRunning) throw new ArgumentException("The DS core has no running ROM.", nameof(core));
        _thread = new Thread(RunLoop) { IsBackground = true, Name = "PKForge melonDS" };
        _audioBuffer = Java.Nio.ByteBuffer.AllocateDirect(_audioScratch.Length * sizeof(short));
        _audioBuffer.Order(Java.Nio.ByteOrder.LittleEndian!);
        try
        {
#pragma warning disable CA1416 // AudioTrack.Builder is available on Android 23+ devices.
            var attributesBuilder = new AudioAttributes.Builder();
            attributesBuilder.SetUsage(AudioUsageKind.Game);
            attributesBuilder.SetContentType(AudioContentType.Music);
            var attributes = attributesBuilder.Build()!;

            var formatBuilder = new AudioFormat.Builder();
            formatBuilder.SetEncoding(Encoding.Pcm16bit);
            formatBuilder.SetSampleRate(AudioRate);
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
        catch (Exception error)
        {
            try { _track?.Stop(); } catch (Exception) { }
            try { _track?.Release(); } catch (Exception) { }
            _track = null;
            Services.AppLog.Warn("embedded", $"DS audio is unavailable; the game will run silently: {error.Message}");
        }
    }

    public void Start() => _thread.Start();

    public void SetKeys(uint bitmask)
    {
        lock (_inputGate) _keysPending = bitmask & 0xFFFu;
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
                Services.AppLog.Warn("embedded", $"Could not update DS audio for emulation speed ×{multiplier}: {error.Message}");
            }
        }
    }

    public LiveCompanionSnapshot? ReadLiveCompanion(int generation)
    {
        lock (_coreGate)
        {
            if (_disposed || !_core.IsRunning) return null;
            var saveSize = _core.BatterySaveSize;
            if (saveSize == 0) return null;
            if (_companionSaveBuffer is null || _companionSaveBuffer.Length != saveSize)
                _companionSaveBuffer = new byte[saveSize];
            if (!_core.CopyBatterySave(_companionSaveBuffer)) return null;
            if (_lastCompanionSave is not null && _companionSaveBuffer.AsSpan().SequenceEqual(_lastCompanionSave))
                return _lastCompanionSnapshot;

            _lastCompanionSave = _companionSaveBuffer.ToArray();
            _lastCompanionSnapshot = LiveDsSaveReader.Read(_companionSaveBuffer, generation, _core.GameTitle, _core.GameCode);
            return _lastCompanionSnapshot;
        }
    }

    public void SetTouchScreen(bool down, int x, int y)
    {
        lock (_inputGate)
        {
            _touchDown = down;
            _touchX = Math.Clamp(x, 0, 255);
            _touchY = Math.Clamp(y, 0, 191);
        }
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
                lock (_coreGate) { }
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

    public bool FlushSave()
    {
        lock (_coreGate) return !_disposed && _core.FlushSave();
    }

    public bool SaveState(string path)
    {
        lock (_coreGate) return !_disposed && _core.IsRunning && _core.SaveState(path);
    }

    public bool LoadState(string path)
    {
        lock (_coreGate) return !_disposed && _core.IsRunning && _core.LoadState(path);
    }

    public void Draw(SKCanvas canvas, SKRect destination)
    {
        lock (_frameGate)
        {
            if (_frameImage is null) return;
            var sampling = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
            canvas.DrawImage(_frameImage, destination, sampling);
        }
    }

    public void DrawMainScreen(SKCanvas canvas, SKRect destination) => DrawScreen(canvas, destination, top: true);

    public void DrawSecondaryScreen(SKCanvas canvas, SKRect destination) => DrawScreen(canvas, destination, top: false);

    private void DrawScreen(SKCanvas canvas, SKRect destination, bool top)
    {
        lock (_frameGate)
        {
            if (_frameImage is null) return;
            var source = top
                ? new SKRect(0, 0, 256, 192)
                : new SKRect(0, 192, 256, 384);
            var sampling = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
            canvas.DrawImage(_frameImage, source, destination, sampling);
        }
    }

    private void RunLoop()
    {
        var frame = new SKBitmap(256, 384, SKColorType.Rgba8888, SKAlphaType.Opaque);
        lock (_frameGate) _frame = frame;
        var baseFramePeriod = Stopwatch.Frequency / FramesPerSecond;
        double deadline = Stopwatch.GetTimestamp();
        var frameCount = 0;
        var renderedFrameCount = 0;
        try
        {
            while (!_cancel.IsCancellationRequested)
            {
                if (!_resume.IsSet)
                {
                    _resume.Wait(TimeSpan.FromMilliseconds(50));
                    deadline = Stopwatch.GetTimestamp();
                    continue;
                }

                var speedMultiplier = Volatile.Read(ref _speedMultiplier);
                var framePeriod = baseFramePeriod / speedMultiplier;
                var publishFrame = ++renderedFrameCount % speedMultiplier == 0;

                int audioFrames;
                lock (_coreGate)
                {
                    if (_cancel.IsCancellationRequested) break;
                    if (!_resume.IsSet) continue;

                    uint keys;
                    bool touchDown;
                    int touchX;
                    int touchY;
                    lock (_inputGate)
                    {
                        keys = _keysPending;
                        touchDown = _touchDown;
                        touchX = _touchX;
                        touchY = _touchY;
                    }
                    _core.RunFrame(keys, touchDown, touchX, touchY);
                    if (publishFrame)
                    {
                        _core.CopyFrame(_frameScratch);
                        MemoryMarshal.AsBytes(_frameScratch.AsSpan()).CopyTo(frame.GetPixelSpan());
                    }
                    audioFrames = _core.ReadAudio(_audioScratch, AudioFramesPerDrain);
                    if (++frameCount % SaveFlushIntervalFrames == 0 && !_core.FlushSave())
                        Services.AppLog.Warn("embedded", "Could not flush the DS battery save after a periodic checkpoint.");
                }

                if (publishFrame)
                {
                    lock (_frameGate)
                    {
                        _frameImage?.Dispose();
                        _frameImage = SKImage.FromBitmap(frame);
                    }
                    try { FrameProduced?.Invoke(); }
                    catch (Exception error) { Services.AppLog.Warn("embedded", $"A DS frame listener failed: {error.Message}"); }
                }

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
                                    Services.AppLog.Warn("embedded", "DS audio stopped after an AudioTrack write error.");
                                }
                            }
                            catch (Exception error)
                            {
                                _audioEnabled = false;
                                Services.AppLog.Warn("embedded", $"DS audio stopped: {error.Message}");
                            }
                        }
                    }
                }

                deadline += framePeriod;
                var remaining = TimeSpan.FromSeconds((deadline - Stopwatch.GetTimestamp()) / Stopwatch.Frequency);
                if (remaining > TimeSpan.Zero)
                {
                    var coarseSleep = remaining - TimeSpan.FromMilliseconds(1);
                    if (coarseSleep > TimeSpan.Zero) Thread.Sleep(coarseSleep);
                    while (!_cancel.IsCancellationRequested && Stopwatch.GetTimestamp() < deadline)
                        Thread.SpinWait(32);
                }
                else if (remaining < TimeSpan.FromMilliseconds(-100))
                    deadline = Stopwatch.GetTimestamp(); // recover from a long pause without runaway catch-up
            }
        }
        catch (Exception error)
        {
            Services.AppLog.Error("embedded", "The melonDS emulation loop stopped unexpectedly.", error);
        }
    }

    public void Dispose()
    {
        lock (_lifecycleGate)
        {
            if (_disposed) return;
            _disposed = true;
            _cancel.Cancel();
            _resume.Set();
        }
        if (_thread.IsAlive && _thread != Thread.CurrentThread) _thread.Join();
        lock (_frameGate)
        {
            _frameImage?.Dispose();
            _frameImage = null;
            _frame?.Dispose();
            _frame = null;
        }
        try { _track?.Stop(); } catch (Exception) { }
        try { _track?.Release(); } catch (Exception) { }
        _resume.Dispose();
        _cancel.Dispose();
    }
}
