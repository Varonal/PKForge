namespace PKForge.Domain;

/// <summary>
/// The embedded emulator core's contract: PKForge runs the game itself (the
/// PokeDaisy model — the core in-process, the companion reads its memory live)
/// instead of handing the ROM to an external emulator. The GBA/GB core (mGBA)
/// comes first; the same shape extends to DS (melonDS) later.
/// </summary>
public interface IEmbeddedCore : IDisposable
{
    /// <summary>True while a ROM runs. Read before any other member.</summary>
    bool IsRunning { get; }

    /// <summary>The loaded ROM's file path.</summary>
    string? RomPath { get; }

    /// <summary>
    /// Loads a ROM and its save (both plain file paths). Returns false when the
    /// file is not a ROM this core runs — nothing is left half-loaded.
    /// </summary>
    bool Load(string romPath, string? savePath = null);

    /// <summary>Advances exactly one emulated frame. Call ~60×/s from the emu thread.</summary>
    void RunFrame();

    /// <summary>The game's own title, from the ROM header.</summary>
    string GameTitle { get; }

    /// <summary>The game code from the header ("BPRE" = FireRed, "BPEE" = Emerald).</summary>
    string GameCode { get; }

    // ---- Video ----
    /// <summary>The framebuffer's dimensions (GBA: 240×160).</summary>
    (int Width, int Height) VideoSize { get; }

    /// <summary>
    /// Copies the current frame as 32-bit RGBA into <paramref name="buffer"/>.
    /// The buffer must hold Width×Height pixels; the copy is one memcpy.
    /// </summary>
    void CopyFrame(Span<uint> buffer);

    // ---- Input ----
    /// <summary>Sets the held button bitmask (A=1&lt;&lt;0 .. Start=1&lt;&lt;3, GBA order).</summary>
    void SetKeys(uint bitmask);

    // ---- Memory (the companion's data bridge) ----
    /// <summary>Reads <paramref name="length"/> bytes at a bus address into the
    /// span. Returns false when the address range is unmapped (nothing copied).</summary>
    bool ReadBytes(uint address, Span<byte> destination);

    /// <summary>Convenience 32-bit read at a bus address; null when unmapped.</summary>
    uint? Read32(uint address);

    // ---- Save states (mGBA's own .ss format, desktop-compatible) ----
    /// <summary>Saves a state to the path (creates or overwrites).</summary>
    bool SaveState(string path);

    /// <summary>Loads a state from the path. False when unreadable.</summary>
    bool LoadState(string path);
}

/// <summary>The GBA button bitmask, in mGBA's own bit order.</summary>
public static class GbaKeys
{
    public const uint A = 1 << 0;
    public const uint B = 1 << 1;
    public const uint Select = 1 << 2;
    public const uint Start = 1 << 3;
    public const uint Right = 1 << 4;
    public const uint Left = 1 << 5;
    public const uint Up = 1 << 6;
    public const uint Down = 1 << 7;
    public const uint R = 1 << 8;
    public const uint L = 1 << 9;
}
