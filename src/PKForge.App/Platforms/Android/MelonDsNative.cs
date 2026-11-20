using System.Runtime.InteropServices;

namespace PKForge.App.Platforms.Android;

/// <summary>Flat UTF-8 C ABI over melonDS 1.1, built as libpkforge_melonds.so.</summary>
internal static partial class MelonDsNative
{
    private const string Library = "pkforge_melonds";

    [LibraryImport(Library, EntryPoint = "pkforge_melonds_available")]
    internal static partial int Available();

    [LibraryImport(Library, EntryPoint = "pkforge_melonds_last_error")]
    internal static partial nint LastError();

    [LibraryImport(Library, EntryPoint = "pkforge_melonds_create", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint Create(string romPath, string savePath);

    [LibraryImport(Library, EntryPoint = "pkforge_melonds_destroy")]
    internal static partial void Destroy(nint core);

    [LibraryImport(Library, EntryPoint = "pkforge_melonds_is_running")]
    internal static partial int IsRunning(nint core);

    [LibraryImport(Library, EntryPoint = "pkforge_melonds_game_title")]
    internal static partial void GameTitle(nint core, Span<byte> output, nuint capacity);

    [LibraryImport(Library, EntryPoint = "pkforge_melonds_game_code")]
    internal static partial void GameCode(nint core, Span<byte> output, nuint capacity);

    [LibraryImport(Library, EntryPoint = "pkforge_melonds_run_frame")]
    internal static partial void RunFrame(nint core, uint keys, int touchDown, int touchX, int touchY);

    [LibraryImport(Library, EntryPoint = "pkforge_melonds_copy_frame")]
    internal static partial int CopyFrame(nint core, Span<uint> output, nuint pixelCapacity);

    [LibraryImport(Library, EntryPoint = "pkforge_melonds_read_audio")]
    internal static partial int ReadAudio(nint core, Span<short> output, int frameCapacity);

    [LibraryImport(Library, EntryPoint = "pkforge_melonds_flush_save")]
    internal static partial int FlushSave(nint core);

    [LibraryImport(Library, EntryPoint = "pkforge_melonds_save_size")]
    internal static partial nuint SaveSize(nint core);

    [LibraryImport(Library, EntryPoint = "pkforge_melonds_copy_save")]
    internal static partial nuint CopySave(nint core, Span<byte> output, nuint capacity);

    [LibraryImport(Library, EntryPoint = "pkforge_melonds_save_state", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int SaveState(nint core, string path);

    [LibraryImport(Library, EntryPoint = "pkforge_melonds_load_state", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int LoadState(nint core, string path);
}
