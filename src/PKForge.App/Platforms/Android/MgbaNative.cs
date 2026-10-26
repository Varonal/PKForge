using System.Runtime.InteropServices;

namespace PKForge.App.Platforms.Android;

/// <summary>
/// P/Invoke over PKForge's libmgba shim (tools/build-mgba/jni_bridge.c), built as
/// libpkforge_mgba.so for arm64-v8a. The shim is flat C over mGBA's mCore API
/// (0.10.5, signature-for-signature from mgba/core/core.h): every function
/// returns int (1 = success, 0 = failure) and every string is UTF-8 — no C bool
/// marshalling, no struct layouts on the managed side.
/// </summary>
internal static partial class MgbaNative
{
    private const string Library = "pkforge_mgba";

    // ---- Lifecycle ----

    /// <summary>mCoreFind + init + loadConfig; returns the core handle, 0 on failure.</summary>
    [LibraryImport(Library, EntryPoint = "pkforg_core_create", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint CoreCreate(string romPath);

    [LibraryImport(Library, EntryPoint = "pkforg_core_destroy")]
    internal static partial void CoreDestroy(nint core);

    /// <summary>loadROM + loadSave + reset; 1 on success.</summary>
    [LibraryImport(Library, EntryPoint = "pkforg_core_load", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int CoreLoad(nint core, string romPath, string? savePath);

    // ---- Run ----

    [LibraryImport(Library, EntryPoint = "pkforg_core_run_frame")]
    internal static partial void CoreRunFrame(nint core);

    [LibraryImport(Library, EntryPoint = "pkforg_core_reset")]
    internal static partial void CoreReset(nint core);

    // ---- Video ----

    [LibraryImport(Library, EntryPoint = "pkforg_core_video_size")]
    internal static partial void CoreVideoSize(nint core, out uint width, out uint height);

    /// <summary>Copies the frame as RGBA8888 into buffer (pixels × 4 bytes).</summary>
    [LibraryImport(Library, EntryPoint = "pkforg_core_copy_frame")]
    internal static partial void CoreCopyFrame(nint core, Span<uint> buffer, UIntPtr pixels);

    // ---- Input ----

    [LibraryImport(Library, EntryPoint = "pkforg_core_set_keys")]
    internal static partial void CoreSetKeys(nint core, uint keys);

    // ---- Memory (the companion's data bridge) ----

    /// <summary>Reads length bytes at a GBA bus address; 1 when the range is mapped.</summary>
    [LibraryImport(Library, EntryPoint = "pkforg_core_read_bytes")]
    internal static partial int CoreReadBytes(nint core, uint address, Span<byte> destination, UIntPtr length);

    // ---- Identity ----

    /// <summary>Copies the ROM header's game title (max 16 bytes + NUL).</summary>
    [LibraryImport(Library, EntryPoint = "pkforg_core_game_title")]
    internal static partial void CoreGameTitle(nint core, Span<byte> title);

    /// <summary>Copies the ROM header's game code (4 bytes + NUL).</summary>
    [LibraryImport(Library, EntryPoint = "pkforg_core_game_code")]
    internal static partial void CoreGameCode(nint core, Span<byte> code);

    // ---- Save states (mGBA's .ss format, desktop-compatible) ----

    [LibraryImport(Library, EntryPoint = "pkforg_core_save_state", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int CoreSaveState(nint core, string path);

    [LibraryImport(Library, EntryPoint = "pkforg_core_load_state", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int CoreLoadState(nint core, string path);
}
