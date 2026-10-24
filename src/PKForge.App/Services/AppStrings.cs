using PKForge.Domain;

namespace PKForge.App.Services;

/// <summary>
/// Every user-facing string the new screens add, in French and English. French is the
/// default (the project's own language). The rest of the app keeps its built-in English
/// copy; these tables cover the Emulators, Trophies, Awards and Library screens.
/// </summary>
public static class AppStrings
{
    private const string Key = "app_language";

    /// <summary>The current language: the persisted choice, else the device locale, else French.</summary>
    public static AppLanguage Language { get; private set; } = ResolveCurrent();

    /// <summary>Raised when the language changes, so open pages can re-read their strings.</summary>
    public static event Action? Changed;

    private static AppLanguage ResolveCurrent()
    {
        var hasChoice = Preferences.Default.ContainsKey(Key);
        var choice = hasChoice ? (AppLanguage)Preferences.Default.Get(Key, (int)AppLanguage.French) : AppLanguage.French;
        return LanguageRules.Resolve(hasChoice, choice, System.Globalization.CultureInfo.CurrentUICulture?.TwoLetterISOLanguageName);
    }

    /// <summary>Persists the choice and switches every open surface to it.</summary>
    public static void Set(AppLanguage language)
    {
        Preferences.Default.Set(Key, (int)language);
        Language = language;
        Changed?.Invoke();
    }

    private static bool Fr => Language == AppLanguage.French;

    // ---- Emulators ----
    public static string EmulatorsTitle => Fr ? "Émulateurs" : "Emulators";
    public static string LinkAllInstalled => Fr ? "Tout lier" : "Link all installed";
    public static string Linked => Fr ? "Lié" : "Linked";
    public static string NotLinked => Fr ? "Non lié" : "Not linked";
    public static string NotInstalled => Fr ? "Non installé" : "Not installed";
    public static string BindConsole => Fr ? "Choisir l'émulateur" : "Choose emulator";
    public static string ConsoleBound => Fr ? "Émulateur des ROMs" : "ROMs emulator";
    public static string AlreadyAllLinked => Fr ? "Tous les émulateurs installés sont déjà liés." : "Every installed emulator is already linked.";

    // ---- Library ----
    public static string LibraryTitle => Fr ? "Bibliothèque" : "Library";
    public static string ScanRoms => Fr ? "Scanner les ROMs" : "Scan ROMs";
    public static string NoRomRoot => Fr
        ? "Dossier ROMs introuvable. Accordez l'accès à tous les fichiers dans les réglages Android, puis rescannez."
        : "No ROMs folder found. Grant All files access in Android settings, then scan again.";
    public static string Scanning => Fr ? "Scan en cours…" : "Scanning…";
    public static string RomCount(int count) => Fr ? $"{count} ROM(s) Pokémon trouvée(s)." : $"{count} Pokémon ROM(s) found.";
    public static string Play => Fr ? "Jouer" : "Play";

    // ---- Cartridge menu ----
    public static string LinkRom => Fr ? "Lier une ROM…" : "Link ROM…";
    public static string UnlinkRom => Fr ? "Délier la ROM" : "Unlink ROM";
    public static string PickFromLibrary => Fr ? "Choisir dans la bibliothèque" : "Pick from library";
    public static string RomMissing => Fr ? "Émulateur manquant" : "Emulator missing";

    // ---- Trophies ----
    public static string TrophiesTitle => Fr ? "Trophées" : "Trophies";
    public static string ScanSaves => Fr ? "Analyser les sauv." : "Scan saves";
    public static string Unlocked => Fr ? "Débloqué" : "Unlocked";
}
