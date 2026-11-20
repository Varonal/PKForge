using PKForge.Domain;
using SkiaSharp;

namespace PKForge.App.Platforms.Android;

/// <summary>The common controls exposed by the in-app GB/GBC/GBA and DS sessions.</summary>
public interface IEmbeddedGameSession : IAsyncDisposable
{
    event Action? FrameProduced;
    (int Width, int Height) VideoSize { get; }
    string GameTitle { get; }
    string GameCode { get; }
    int Generation { get; }
    string QuickStatePath { get; }
    bool HasQuickState { get; }
    bool HasSaveConflict { get; }
    bool SupportsTouchScreen { get; }
    bool SupportsLiveCompanion { get; }
    bool IsNintendoDs { get; }
    void SetKeys(uint bitmask);
    void SetSpeedMultiplier(int multiplier);
    void SetTouchScreen(bool down, int x, int y);
    void Draw(SKCanvas canvas, SKRect destination);
    void DrawMainScreen(SKCanvas canvas, SKRect destination) => Draw(canvas, destination);
    void DrawSecondaryScreen(SKCanvas canvas, SKRect destination) { }
    LiveCompanionSnapshot? ReadLiveCompanion();
    void SetPaused(bool paused);
    Task<bool> FlushBatterySaveAsync();
    bool SaveQuickState();
    bool LoadQuickState();
    Task<bool> CloseAsync();
}
