namespace PKForge.App.Services;

public enum GameCompanionTab
{
    Party,
    Bag,
    Pokedex,
    Session,
    RestorePoints,
}

/// <summary>Persists the lower-screen companion's visible tabs and opening tab.</summary>
public static class GameCompanionPreferences
{
    private const string TabsKey = "game_companion_tabs";
    private const string HomeTabKey = "game_companion_home_tab";
    private static readonly GameCompanionTab[] DefaultTabs =
        [GameCompanionTab.Party, GameCompanionTab.Bag, GameCompanionTab.Pokedex, GameCompanionTab.Session];

    public static IReadOnlyList<GameCompanionTab> VisibleTabs
    {
        get
        {
            var raw = Preferences.Default.Get(TabsKey, string.Join(',', DefaultTabs));
            var tabs = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => Enum.TryParse<GameCompanionTab>(value, out var tab) ? tab : (GameCompanionTab?)null)
                .Where(tab => tab.HasValue)
                .Select(tab => tab!.Value)
                .Distinct()
                .ToArray();
            return tabs.Length == 0 ? DefaultTabs : tabs;
        }
    }

    public static GameCompanionTab HomeTab
    {
        get
        {
            var raw = Preferences.Default.Get(HomeTabKey, nameof(GameCompanionTab.Party));
            return Enum.TryParse<GameCompanionTab>(raw, out var tab) && VisibleTabs.Contains(tab)
                ? tab
                : VisibleTabs[0];
        }
    }

    public static void ToggleTab(GameCompanionTab tab)
    {
        var tabs = VisibleTabs.ToList();
        if (!tabs.Remove(tab)) tabs.Add(tab);
        if (tabs.Count == 0) return;
        Preferences.Default.Set(TabsKey, string.Join(',', tabs));
        if (!tabs.Contains(HomeTab)) SetHomeTab(tabs[0]);
    }

    public static void SetHomeTab(GameCompanionTab tab)
    {
        if (VisibleTabs.Contains(tab)) Preferences.Default.Set(HomeTabKey, tab.ToString());
    }
}
