namespace PKForge.Domain;

/// <summary>
/// The app's two languages. French is the default (the project's own language); English
/// follows for everyone else. The choice persists and overrides the device locale.
/// </summary>
public enum AppLanguage
{
    French = 0,
    English = 1,
}

/// <summary>Reads and stores the player's language choice.</summary>
public interface ILanguageStore
{
    AppLanguage Current();

    /// <summary>The persisted choice, or the default inferred from the device locale.</summary>
    void Set(AppLanguage language);
}

/// <summary>The language rules: the persisted choice wins, else the device locale decides,
/// else French. Pure, so the fallback chain is unit-testable.</summary>
public static class LanguageRules
{
    /// <summary>French is the app's home language.</summary>
    public const AppLanguage Default = AppLanguage.French;

    /// <summary>True when a locale string names a French device ("fr", "fr-FR", "fr_CA"...).</summary>
    public static bool IsFrenchLocale(string? locale) =>
        locale is not null && locale.StartsWith("fr", StringComparison.OrdinalIgnoreCase);

    /// <summary>The language for one launch: the choice when the player made one, else the locale.</summary>
    public static AppLanguage Resolve(bool hasChoice, AppLanguage choice, string? deviceLocale) =>
        hasChoice ? choice : IsFrenchLocale(deviceLocale) ? AppLanguage.French : Default;
}
