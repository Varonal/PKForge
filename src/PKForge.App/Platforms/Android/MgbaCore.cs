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
    private (int Width, int Height) _videoSize;
    private string _gameTitle = string.Empty;
    private string _gameCode = string.Empty;

    public bool IsRunning { get; private set; }

    public string? RomPath { get; private set; }
    public string? LoadError { get; private set; }

    public bool Load(string romPath, string? savePath = null)
    {
        LoadError = null;
        if (IsRunning)
        {
            LoadError = "Une ROM est déjà chargée dans ce cœur.";
            return false;
        }

        var core = MgbaNative.CoreCreate(romPath);
        if (core == nint.Zero)
        {
            LoadError = "mGBA ne reconnaît pas ce fichier comme une ROM GB, GBC ou GBA valide.";
            return false;
        }

        var initialized = false;
        try
        {
            if (MgbaNative.CoreLoad(core, romPath, savePath) == 0)
            {
                LoadError = "mGBA a reconnu la ROM, mais n'a pas pu charger son contenu ou sa sauvegarde. Vérifie le fichier et ses droits d'accès.";
                return false;
            }

            var videoSize = ReadVideoSize(core);
            if (videoSize.Width <= 0 || videoSize.Height <= 0)
            {
                LoadError = "mGBA n'a pas fourni de dimensions vidéo valides pour cette ROM.";
                return false;
            }

            var gameTitle = ReadGameTitle(core);
            var gameCode = ReadGameCode(core);
            _core = core;
            _videoSize = videoSize;
            _gameTitle = gameTitle;
            _gameCode = gameCode;
            RomPath = romPath;
            IsRunning = true;
            initialized = true;
            return true;
        }
        catch (Exception error)
        {
            LoadError = $"Erreur pendant le chargement natif : {error.Message}";
            return false;
        }
        finally
        {
            if (!initialized)
            {
                try { MgbaNative.CoreDestroy(core); }
                catch (Exception error) { Services.AppLog.Warn("embedded", $"Could not release a failed mGBA core: {error.Message}"); }
            }
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
        get => IsRunning ? _gameTitle : string.Empty;
    }

    public string GameCode
    {
        get => IsRunning ? _gameCode : string.Empty;
    }

    public (int Width, int Height) VideoSize
    {
        get => IsRunning ? _videoSize : (0, 0);
    }

    private static string ReadGameTitle(nint core)
    {
        Span<byte> title = stackalloc byte[17];
        MgbaNative.CoreGameTitle(core, title);
        var length = title.IndexOf((byte)0);
        return length < 0 ? Encoding.ASCII.GetString(title) : Encoding.ASCII.GetString(title[..length]);
    }

    private static string ReadGameCode(nint core)
    {
        Span<byte> code = stackalloc byte[5];
        MgbaNative.CoreGameCode(core, code);
        var length = code.IndexOf((byte)0);
        return length < 0 ? Encoding.ASCII.GetString(code) : Encoding.ASCII.GetString(code[..length]);
    }

    private static (int Width, int Height) ReadVideoSize(nint core)
    {
        MgbaNative.CoreVideoSize(core, out var width, out var height);
        return ((int)width, (int)height);
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

    /// <summary>Copies mGBA's raw battery data without relying on the cartridge bus window.</summary>
    public int CopySavedata(Span<byte> destination)
    {
        if (!IsRunning || destination.IsEmpty) return 0;
        var copied = MgbaNative.CoreCopySavedata(_core, destination, (UIntPtr)destination.Length).ToUInt64();
        return copied <= (ulong)destination.Length ? (int)copied : 0;
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

    public bool FlushSave(string path)
    {
        ThrowWhenNotRunning();
        return MgbaNative.CoreFlushSave(_core, path) != 0;
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
