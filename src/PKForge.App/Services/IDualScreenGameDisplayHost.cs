using PKForge.App.Platforms.Android;

namespace PKForge.App.Services;

/// <summary>Routes a dual-screen console's second framebuffer to Android's secondary display.</summary>
public interface IDualScreenGameDisplayHost
{
    event Action? DisplayContentChanged;

    bool IsShowingNintendoDsBottomScreen(IEmbeddedGameSession session);

    Task<bool> ShowNintendoDsBottomScreenAsync(
        IEmbeddedGameSession session,
        CancellationToken cancellationToken = default);

    ValueTask RestoreDefaultScreenAsync(
        IEmbeddedGameSession session,
        CancellationToken cancellationToken = default);
}
