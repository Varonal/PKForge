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

    /// <summary>True when the current language is French, for inline site-specific strings.</summary>
    public static bool Fr => Language == AppLanguage.French;

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

    // ---- Home: cards and shelf ----
    public static string DexAutopilot => Fr ? "Autopilot Dex" : "Dex Autopilot";
    public static string Pokepark => "Poképark";
    public static string Games => Fr ? "Jeux" : "Games";
    public static string Bank => Fr ? "Banque" : "Bank";
    public static string Events => Fr ? "Événements" : "Events";
    public static string Settings => Fr ? "Réglages" : "Settings";
    public static string Open => Fr ? "Ouvrir" : "Open";
    public static string OpenSave => Fr ? "Ouvrir la sauv." : "Open save";
    public static string EditGameHold => Fr ? "Modifier · maintenir" : "Edit game · hold";
    public static string QuitPkforge => Fr ? "Quitter PKForge ?" : "Quit PKForge?";
    public static string Quit => Fr ? "Quitter" : "Quit";
    public static string Back => Fr ? "Retour" : "Back";
    public static string Cancel => Fr ? "Annuler" : "Cancel";
    public static string Ok => "OK";
    public static string Close => Fr ? "Fermer" : "Close";
    public static string WhichSaveToOpen => Fr ? "Quelle sauvegarde ouvrir ?" : "Which save do you want to open?";
    public static string SaveCouldNotOpen => Fr ? "La sauvegarde n'a pas pu s'ouvrir" : "Save could not open";

    // ---- Home: link and storage ----
    public static string Link => Fr ? "Lier" : "Link";
    public static string LinkEmulator => Fr ? "Lier un émulateur" : "Link an emulator";
    public static string LinkStorageUnit => Fr ? "Lier un stockage" : "Link a storage unit";
    public static string ChoosePlatform => Fr ? "Choisissez une plateforme pour voir ses émulateurs." : "Choose a platform to see its emulators.";
    public static string ChooseFolder => Fr ? "Choisir le dossier" : "Choose folder";
    public static string LinkedStorage => Fr ? "Stockages liés" : "Linked storage";
    public static string ManageLinkedStorage => Fr ? "Gérer les stockages liés" : "Manage linked storage";
    public static string NoLinkedStorage => Fr ? "Aucun stockage lié." : "No linked storage units.";
    public static string UnlinkAllStorage => Fr ? "Délier tous les stockages" : "Unlink all storage units";
    public static string UnlinkAll => Fr ? "Tout délier" : "Unlink all";
    public static string Unlink => Fr ? "Délier" : "Unlink";
    public static string UnlinkStorageQuestion => Fr ? "Délier ce stockage ?" : "Unlink storage?";
    public static string UnlinkAllStorageQuestion => Fr ? "Tout délier ?" : "Unlink all storage?";
    public static string UnlinkAllRemoved => Fr ? "Tous les stockages liés ont été retirés." : "All linked storage units removed.";
    public static string OpenSingleSave => Fr ? "Ouvrir un fichier de sauvegarde" : "Open a single save file";
    public static string SingleSaveFile => Fr ? "Fichier de sauvegarde unique" : "Single save file";
    public static string File => Fr ? "Fichier" : "File";
    public static string RescanGames => Fr ? "Rescanner les jeux" : "Rescan games";
    public static string ScanReport => Fr ? "Rapport de scan" : "Scan report";
    public static string ScanReportCopied => Fr ? "Rapport copié" : "Scan report copied";
    public static string CopyReport => Fr ? "Copier le rapport" : "Copy report";
    public static string ShareReport => Fr ? "Partager le rapport" : "Share report";
    public static string ShareLogs => Fr ? "Partager les logs" : "Share logs";
    public static string HiddenSaves => Fr ? "Sauvegardes masquées" : "Hidden saves";
    public static string ShowHiddenSaves(int count) => Fr ? $"Afficher les sauvegardes masquées ({count})" : $"Show hidden saves ({count})";

    // ---- Settings ----
    public static string CheckForUpdate => Fr ? "Rechercher une mise à jour" : "Check for update";
    public static string AboutPkforge => Fr ? "À propos de PKForge" : "About PKForge";
    public static string SecondScreenOn => Fr ? "Second écran : activé" : "Second screen: ON";
    public static string SecondScreenOff => Fr ? "Second écran : désactivé" : "Second screen: OFF";
    public static string RestorePoints => Fr ? "Points de restauration" : "Restore points";
    public static string BoxBackground => Fr ? "Fond des boîtes" : "Box background";
    public static string ColorScheme => Fr ? "Thème de couleur" : "Color scheme";
    public static string EventDatabase => Fr ? "Base d'événements" : "Event database";
    public static string WonderCards => Fr ? "Wonder cards" : "Wonder cards";
    public static string CommunityBoxes => Fr ? "Boîtes communautaires" : "Community boxes";
    public static string Music => Fr ? "Musique" : "Music";
    public static string Misc => Fr ? "Divers" : "Misc";

    // ---- Filter ----
    public static string Filter => Fr ? "Filtrer" : "Filter";
    public static string FilterGames => Fr ? "Filtrer les jeux" : "Filter games";
    public static string AllGames => Fr ? "Tous les jeux" : "All games";
    public static string All => "Tous";
    public static string ReleaseOrder => Fr ? "Ordre de sortie" : "Release order";
    public static string Alphabetical => Fr ? "Alphabétique (A-Z)" : "Alphabetical (A-Z)";

    // ---- Wizard ----
    public static string GetStarted => Fr ? "Commencer" : "Get started";
    public static string Welcome => Fr ? "Bonjour ! Bienvenue sur PKForge !" : "Hello! Welcome to PKForge!";
    public static string FindYourGames => Fr ? "Et maintenant... trouvons vos jeux !" : "Now then... let's find your games!";
    public static string MaybeLater => Fr ? "Plus tard" : "Maybe later";
    public static string NotNow => Fr ? "Pas maintenant" : "Not now";
    public static string DecideLater => Fr ? "Décider plus tard" : "Decide later";
    public static string DownloadSpritePack => Fr ? "Télécharger le pack de sprites" : "Download the sprite pack";

    // ---- Platform names (menu Link) ----
    public static string PlatformGbGbc => Fr ? "Game Boy / Game Boy Color" : "Game Boy / Game Boy Color";
    public static string PlatformGba => "Game Boy Advance";
    public static string PlatformDs => "Nintendo DS";
    public static string PlatformGc => "GameCube";
    public static string Platform3ds => "Nintendo 3DS";
    public static string PlatformSwitch => "Nintendo Switch";
}
