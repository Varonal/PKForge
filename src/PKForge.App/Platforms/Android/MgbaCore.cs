using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace PKForge.App.Platforms.Android;

/// <summary>
/// <see cref="Domain.IEmbeddedCore"/> over PKForge's libmgba shim (jni_bridge.c):
/// load, run, read memory, save states. The core runs on the caller's thread —
/// <see cref="EmbeddedEngine"/> owns the emu thread; this class never touches the
/// UI thread. One instance per ROM.
/// </summary>
internal sealed class MgbaCore : Domain.IEmbeddedCore
{
    private nint _core;

    public bool IsRunning { get; private set; }

    public string? RomPath { get; private set; }

    public bool Load(string romPath, string? savePath = null)
    {
        if (IsRunning) return false;
        var core = MgbaNative.CoreCreate(romPath);
        if (core == nint.Zero) return false;
        try
        {
            if (MgbaNative.CoreLoad(core, romPath, savePath) == 0)
            {
                MgbaNative.CoreDestroy(core);
                return false;
            }
            _core = core;
            RomPath = romPath;
            IsRunning = true;
            return true;
        }
        catch (Exception)
        {
            try { MgbaNative.CoreDestroy(core); } catch (DllNotFoundException) { }
            return false;
        }
    }

    private void ThrowWhenNotRunning()
    {
        if (!IsRunning) throw new InvalidOperationException("No ROM is loaded.");
    }

    public void RunFrame()
    {
        ThrowWhenNotRunning();
        MgbaNative.CoreRunFrame(_core);
    }

    public string GameTitle
    {
        get
        {
            if (!IsRunning) return string.Empty;
            Span<byte> title = stackalloc byte[17];
            MgbaNative.CoreGameTitle(_core, title);
            var length = title.IndexOf((byte)0);
            return length < 0 ? Encoding.ASCII.GetString(title) : Encoding.ASCII.GetString(title[..length]);
        }
    }

    public string GameCode
    {
        get
        {
            if (!IsRunning) return string.Empty;
            Span<byte> code = stackalloc byte[5];
            MgbaNative.CoreGameCode(_core, code);
            var length = code.IndexOf((byte)0);
            return length < 0 ? Encoding.ASCII.GetString(code) : Encoding.ASCII.GetString(code[..length]);
        }
    }

    public (int Width, int Height) VideoSize
    {
        get
        {
            if (!IsRunning) return (0, 0);
            MgbaNative.CoreVideoSize(_core, out var width, out var height);
            return ((int)width, (int)height);
        }
    }

    public void CopyFrame(Span<uint> buffer)
    {
        ThrowWhenNotRunning();
        var (width, height) = VideoSize;
        if (buffer.Length < width * height)
            throw new ArgumentException($"The buffer holds {buffer.Length} pixels; the frame needs {width * height}.");
        // mGBA's native color_t is 0x00BBGGRR (interface.h's M_COLOR_*): the bytes in
        // memory are R,G,B,X — SkiaSharp's Rgba8888 row layout, copied as-is.
        MgbaNative.CoreCopyFrame(_core, buffer, (UIntPtr)(width * height));
    }

    public void SetKeys(uint bitmask)
    {
        if (!IsRunning) return;
        MgbaNative.CoreSetKeys(_core, bitmask);
    }

    public bool ReadBytes(uint address, Span<byte> destination)
    {
        if (!IsRunning || destination.IsEmpty) return false;
        return MgbaNative.CoreReadBytes(_core, address, destination, (UIntPtr)destination.Length) != 0;
    }

    public uint? Read32(uint address)
    {
        Span<byte> four = stackalloc byte[4];
        if (!ReadBytes(address, four)) return null;
        return BinaryPrimitives.ReadUInt32LittleEndian(four);
    }

    /// <summary>
    /// Drains the core's audio into the destination as interleaved stereo int16
    /// (AudioTrack's PCM16 stereo layout). Returns the frames (sample pairs) read.
    /// </summary>
    public int ReadAudio(Span<short> destination, int samplesPerChannel)
    {
        if (!IsRunning || samplesPerChannel <= 0) return 0;
        return MgbaNative.CoreReadAudio(_core, destination, samplesPerChannel);
    }

    public bool SaveState(string path)
    {
        ThrowWhenNotRunning();
        return MgbaNative.CoreSaveState(_core, path) != 0;
    }

    public bool LoadState(string path)
    {
        ThrowWhenNotRunning();
        return MgbaNative.CoreLoadState(_core, path) != 0;
    }

    public void Dispose()
    {
        if (!IsRunning) return;
        IsRunning = false;
        RomPath = null;
        try { MgbaNative.CoreDestroy(_core); }
        catch (DllNotFoundException) { }
        _core = nint.Zero;
    }
}
