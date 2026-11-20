using System.Text;
using System.Runtime.InteropServices;

namespace PKForge.App.Platforms.Android;

/// <summary>One melonDS cart instance. The engine serializes all native access.</summary>
internal sealed class MelonDsCore : IDisposable
{
    private nint _core;

    public bool IsRunning => _core != nint.Zero && MelonDsNative.IsRunning(_core) != 0;
    public string? RomPath { get; private set; }
    public string? LoadError { get; private set; }
    public string GameTitle { get; private set; } = string.Empty;
    public string GameCode { get; private set; } = string.Empty;
    public (int Width, int Height) VideoSize => IsRunning ? (256, 384) : (0, 0);

    public bool Load(string romPath, string savePath)
    {
        LoadError = null;
        if (_core != nint.Zero)
        {
            LoadError = "Une ROM est déjà chargée dans melonDS.";
            return false;
        }

        _core = MelonDsNative.Create(romPath, savePath);
        if (_core == nint.Zero)
        {
            LoadError = MarshalError();
            return false;
        }

        try
        {
            Span<byte> title = stackalloc byte[13];
            Span<byte> code = stackalloc byte[5];
            MelonDsNative.GameTitle(_core, title, (nuint)title.Length);
            MelonDsNative.GameCode(_core, code, (nuint)code.Length);
            GameTitle = DecodeHeader(title);
            GameCode = DecodeHeader(code);
            if (!IsRunning)
            {
                LoadError = "melonDS a chargé la ROM, mais la console ne tourne pas.";
                Dispose();
                return false;
            }
            RomPath = romPath;
            return true;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private static string DecodeHeader(ReadOnlySpan<byte> bytes)
    {
        var zero = bytes.IndexOf((byte)0);
        return Encoding.ASCII.GetString(zero < 0 ? bytes : bytes[..zero]).Trim();
    }

    private static string MarshalError()
    {
        var error = Marshal.PtrToStringUTF8(MelonDsNative.LastError());
        return string.IsNullOrWhiteSpace(error) ? "melonDS n'a pas pu démarrer cette ROM DS." : error;
    }

    public void RunFrame(uint keys, bool touchDown, int touchX, int touchY)
    {
        ThrowWhenNotRunning();
        MelonDsNative.RunFrame(_core, keys, touchDown ? 1 : 0, touchX, touchY);
    }

    public void CopyFrame(Span<uint> buffer)
    {
        ThrowWhenNotRunning();
        const int pixelCount = 256 * 384;
        if (buffer.Length < pixelCount)
            throw new ArgumentException($"The buffer holds {buffer.Length} pixels; the DS frame needs {pixelCount}.");
        if (MelonDsNative.CopyFrame(_core, buffer, (nuint)buffer.Length) == 0)
            throw new InvalidOperationException("melonDS could not copy the current dual-screen frame.");
    }

    public int ReadAudio(Span<short> destination, int frameCapacity)
    {
        if (!IsRunning || frameCapacity <= 0) return 0;
        return MelonDsNative.ReadAudio(_core, destination, frameCapacity);
    }

    public bool FlushSave() => !IsRunning || MelonDsNative.FlushSave(_core) != 0;

    public int BatterySaveSize
    {
        get
        {
            ThrowWhenNotRunning();
            const nuint maximumSaveSize = 16 * 1024 * 1024;
            var size = MelonDsNative.SaveSize(_core);
            return size == 0 || size > maximumSaveSize ? 0 : (int)size;
        }
    }

    public bool CopyBatterySave(Span<byte> destination)
    {
        ThrowWhenNotRunning();
        var size = BatterySaveSize;
        if (size == 0 || destination.Length < size) return false;
        return MelonDsNative.CopySave(_core, destination, (nuint)size) == (nuint)size;
    }

    public bool SaveState(string path) => IsRunning && MelonDsNative.SaveState(_core, path) != 0;
    public bool LoadState(string path) => IsRunning && MelonDsNative.LoadState(_core, path) != 0;

    private void ThrowWhenNotRunning()
    {
        if (!IsRunning) throw new InvalidOperationException("No Nintendo DS ROM is loaded.");
    }

    public void Dispose()
    {
        if (_core == nint.Zero) return;
        var core = _core;
        _core = nint.Zero;
        RomPath = null;
        try { MelonDsNative.Destroy(core); }
        catch (DllNotFoundException) { }
    }
}
