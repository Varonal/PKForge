namespace PKForge.App.Services;

public enum EmbeddedGameScaleMode
{
    Fit,
    NinetyPercent,
    SeventyFivePercent,
    PixelPerfect,
}

/// <summary>Display preferences for the in-app emulator. Image proportions are always preserved.</summary>
public static class EmbeddedGameDisplayPreferences
{
    private const string FullScreenKey = "embedded_game_full_screen";
    private const string ScaleModeKey = "embedded_game_scale_mode";
    private const string TouchControlsKey = "embedded_game_touch_controls";

    public static bool FullScreen
    {
        get => Preferences.Default.Get(FullScreenKey, true);
        set => Preferences.Default.Set(FullScreenKey, value);
    }

    public static EmbeddedGameScaleMode ScaleMode
    {
        get
        {
            var stored = Preferences.Default.Get(ScaleModeKey, nameof(EmbeddedGameScaleMode.Fit));
            return Enum.TryParse<EmbeddedGameScaleMode>(stored, out var mode)
                ? mode
                : EmbeddedGameScaleMode.Fit;
        }
        set => Preferences.Default.Set(ScaleModeKey, value.ToString());
    }

    public static bool TouchControlsVisible
    {
        get => Preferences.Default.Get(TouchControlsKey, true);
        set => Preferences.Default.Set(TouchControlsKey, value);
    }

    public static float ScaleFor(EmbeddedGameScaleMode mode, float maximumScale) => mode switch
    {
        EmbeddedGameScaleMode.NinetyPercent => maximumScale * 0.90f,
        EmbeddedGameScaleMode.SeventyFivePercent => maximumScale * 0.75f,
        EmbeddedGameScaleMode.PixelPerfect when maximumScale >= 1f => MathF.Floor(maximumScale),
        _ => maximumScale,
    };
}
