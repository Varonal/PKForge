namespace PKForge.App.Services;

/// <summary>Preferences shared by PKForge's in-app game sessions.</summary>
public static class EmbeddedGamePreferences
{
    private const string SpeedMultiplierKey = "embedded_game_speed_multiplier";

    public static int SpeedMultiplier
    {
        get
        {
            var stored = Preferences.Default.Get(SpeedMultiplierKey, 1);
            return stored is >= 1 and <= 4 ? stored : 1;
        }
        set
        {
            if (value is < 1 or > 4)
                throw new ArgumentOutOfRangeException(nameof(value), "The embedded game speed must be between ×1 and ×4.");
            Preferences.Default.Set(SpeedMultiplierKey, value);
        }
    }
}
