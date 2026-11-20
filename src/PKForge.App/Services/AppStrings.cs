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
    public static string LibrarySearch => Fr ? "Rechercher un jeu" : "Search games";
    public static string LibrarySearchPlaceholder => Fr ? "Jeu ou nom du fichier ROM…" : "Game or ROM filename…";
    public static string LibraryClearSearch => Fr ? "Effacer la recherche" : "Clear search";
    public static string LibraryScanHint => Fr
        ? "Appuyez sur Scanner pour chercher vos ROMs dans /storage/emulated/0/ROMs."
        : "Tap Scan to look for your ROMs in /storage/emulated/0/ROMs.";
    public static string LibraryReady => Fr
        ? "Touchez une ROM pour la lancer, ou sélectionnez-la puis appuyez sur A."
        : "Tap a ROM to launch it, or select it and press A.";
    public static string LibraryNoResults(string query) => Fr
        ? $"Aucun jeu ne correspond à « {query} »."
        : $"No games match “{query}”.";
    public static string LibraryResults(int count, int roms) => Fr
        ? $"{count} résultat(s) · {roms} ROM(s)"
        : $"{count} result(s) · {roms} ROM(s)";
    public static string NoRomRoot => Fr
        ? "Dossier ROMs introuvable. Accordez l'accès à tous les fichiers dans les réglages Android, puis rescannez."
        : "No ROMs folder found. Grant All files access in Android settings, then scan again.";
    public static string Scanning => Fr ? "Scan en cours…" : "Scanning…";
    public static string Play => Fr ? "Jouer" : "Play";

    // ---- Cartridge menu ----
    public static string LinkRom => Fr ? "Lier une ROM…" : "Link ROM…";
    public static string UnlinkRom => Fr ? "Délier la ROM" : "Unlink ROM";
    public static string PickFromLibrary => Fr ? "Choisir dans la bibliothèque" : "Pick from library";
    public static string RomMissing => Fr ? "Émulateur manquant" : "Emulator missing";
    public static string GamePause => Fr ? "Pause" : "Pause";
    public static string GameResume => Fr ? "Reprendre" : "Resume";
    public static string GameOptions => Fr ? "Options" : "Options";
    public static string GameOptionsSpeed(int multiplier) => $"Options · ×{multiplier}";
    public static string GameSpeed => Fr ? "Vitesse du jeu" : "Game speed";
    public static string GameSpeedMenuLabel(int multiplier) => Fr
        ? $"Vitesse du jeu · ×{multiplier}"
        : $"Game speed · ×{multiplier}";
    public static string GameSpeedHelp => Fr
        ? "Choisis une accélération. Le son est coupé au-dessus de ×1 et revient à vitesse normale."
        : "Choose a fast-forward speed. Audio is muted above ×1 and returns at normal speed.";
    public static string GameSpeedChoice(int multiplier) => multiplier == 1
        ? Fr ? "×1 · normale" : "×1 · normal"
        : $"×{multiplier}";
    public static string GameDisplay => Fr ? "Taille du jeu" : "Game size";
    public static string GameDisplayHelp => Fr
        ? "Choisis la taille de l'image. Le rapport du jeu reste toujours respecté."
        : "Choose the game image size. The game's aspect ratio is always preserved.";
    public static string GameScaleFit => Fr ? "Maximum · adapter à l'écran" : "Maximum · fit screen";
    public static string GameScale90 => Fr ? "90 % de la taille maximale" : "90% of maximum size";
    public static string GameScale75 => Fr ? "75 % de la taille maximale" : "75% of maximum size";
    public static string GameScalePixelPerfect => Fr ? "Pixels entiers · pixel perfect" : "Whole pixels · pixel perfect";
    public static string GameEnterFullScreen => Fr ? "Plein écran" : "Full screen";
    public static string GameExitFullScreen => Fr ? "Afficher l'interface" : "Show interface";
    public static string GameToggleTouchControls => Fr ? "Afficher les commandes tactiles" : "Toggle touch controls";
    public static string GameHideTouchControls => Fr ? "Masquer les commandes" : "Hide controls";
    public static string GameShowTouchControls => Fr ? "Afficher les commandes" : "Show controls";
    public static string GameSaveState => Fr ? "Sauvegarder l'état" : "Save state";
    public static string GameLoadState => Fr ? "Charger l'état" : "Load state";
    public static string GameExit => Fr ? "Quitter le jeu" : "Exit game";
    public static string GameSavingState => Fr ? "Sauvegarde de l'état…" : "Saving state…";
    public static string GameLoadingState => Fr ? "Chargement de l'état…" : "Loading state…";
    public static string GamePaused => Fr ? "Jeu en pause." : "Game paused.";
    public static string GameStateSaved => Fr ? "État sauvegardé." : "State saved.";
    public static string GameStateLoaded => Fr ? "État chargé." : "State loaded.";
    public static string GameStateSaveFailed => Fr ? "Impossible de sauvegarder l'état." : "Could not save the state.";
    public static string GameStateLoadFailed => Fr ? "Impossible de charger l'état." : "Could not load the state.";
    public static string CompanionParty => Fr ? "ÉQUIPE" : "PARTY";
    public static string CompanionBag => Fr ? "SAC" : "BAG";
    public static string CompanionPokedex => Fr ? "POKÉDEX" : "POKÉDEX";
    public static string CompanionSession => Fr ? "SESSION" : "SESSION";
    public static string CompanionRestorePoints => Fr ? "REPRISES" : "RESTORES";
    public static string CompanionSettings => Fr ? "OPTIONS" : "SETTINGS";
    public static string CompanionSettingsTitle => Fr ? "Options du compagnon" : "Companion settings";
    public static string CompanionSettingsHelp => Fr
        ? "Choisis les pages visibles et celle qui s'ouvre en premier."
        : "Choose which pages are visible and which one opens first.";
    public static string CompanionShowTab => Fr ? "Afficher" : "Show";
    public static string CompanionHideTab => Fr ? "Masquer" : "Hide";
    public static string CompanionMakeHome => Fr ? "Ouvrir au démarrage" : "Open on launch";
    public static string CompanionCurrentHome => Fr ? "Page d'accueil" : "Home page";
    public static string CompanionNoParty => Fr
        ? "Aucune équipe n'est enregistrée dans cette sauvegarde valide."
        : "No party is recorded in this valid save.";
    public static string CompanionNoSavedData => Fr
        ? "Aucune sauvegarde valide n'est disponible pour ce jeu."
        : "No valid save is available for this game.";
    public static string CompanionSavedDataNotice => Fr
        ? "Données de la dernière sauvegarde valide"
        : "Data from the latest valid game save";
    public static string CompanionBagEmpty => Fr ? "Le sac est vide." : "The bag is empty.";
    public static string CompanionEgg => Fr ? "ŒUF" : "EGG";
    public static string CompanionPocketAll => Fr ? "TOUT" : "ALL";
    public static string CompanionPocketItems => Fr ? "OBJETS" : "ITEMS";
    public static string CompanionPocketKeyItems => Fr ? "OBJETS RARES" : "KEY ITEMS";
    public static string CompanionPocketMedicine => Fr ? "MÉDICAMENTS" : "MEDICINE";
    public static string CompanionPocketBalls => Fr ? "POKÉ BALLS" : "POKÉ BALLS";
    public static string CompanionPocketMachines => Fr ? "CT / CS" : "TMs / HMs";
    public static string CompanionPocketBerries => Fr ? "BAIES" : "BERRIES";
    public static string CompanionPocketBattleItems => Fr ? "OBJETS DE COMBAT" : "BATTLE ITEMS";
    public static string CompanionPocketMail => Fr ? "COURRIER" : "MAIL";
    public static string CompanionPartyMember(int index, string nickname, string species, int level, bool isEgg = false) =>
        isEgg
            ? $"{index}. {nickname} · {species}"
            : Fr
                ? $"{index}. {nickname} · {species} · N. {level}"
                : $"{index}. {nickname} · {species} · Lv. {level}";
    public static string CompanionHp(int current, int maximum) => Fr ? $"{current}/{maximum} PV" : $"{current}/{maximum} HP";
    public static string CompanionDex(int caught, int seen, int total) => Fr
        ? $"Capturés {caught} · vus {seen} · total {total}"
        : $"Caught {caught} · seen {seen} · total {total}";
    public static string CompanionPlaytime(int hours, int minutes) => Fr
        ? $"Temps de jeu · {hours} h {minutes:D2}"
        : $"Play time · {hours} h {minutes:D2}";
    public static string CompanionPartyLabel(int count) => Fr ? $"Équipe · {count}/6" : $"Party · {count}/6";
    public static string CompanionNoRestorePoints => Fr ? "Aucun point de restauration disponible." : "No restore points are available.";
    public static string GameBatterySaveFailed => Fr
        ? "PKForge n'a pas pu confirmer l'enregistrement de la sauvegarde de jeu. Vérifiez l'espace disponible et l'accès au fichier."
        : "PKForge could not confirm the game save. Check available storage and file access.";
    public static string GameBatterySaveFailedTitle => Fr ? "Sauvegarde de jeu non synchronisée" : "Game save not synchronized";
    public static string GameSaveConflict => Fr
        ? "Le fichier lié et la sauvegarde locale ont changé séparément. Aucune copie n'a été écrasée."
        : "The linked file and private game save changed separately. Neither copy was overwritten.";
    public static string GameLaunchFailedTitle => Fr ? "Jeu impossible à lancer" : "Could not launch";
    public static string GameLaunchFailed(string name, string? reason = null, int generation = 0)
    {
        var core = generation is 4 or 5 ? "melonDS" : "mGBA";
        var message = Fr
            ? $"{name} n'a pas pu démarrer dans PKForge. Vérifie la ROM, le cœur {core} intégré et l'accès à la sauvegarde."
            : $"{name} could not start in PKForge. Check the ROM, the integrated {core} core, and save-file access.";
        return string.IsNullOrWhiteSpace(reason) ? message : $"{message}\n\n{reason}";
    }
    public static string EmbeddedCoreMissing(int generation) => Fr
        ? $"Le cœur PKForge pour la génération {generation} n'est pas présent ou chargeable dans cette APK. La ROM ne sera pas envoyée à un émulateur externe."
        : $"PKForge's integrated core for generation {generation} is missing or could not be loaded from this APK. The ROM will not be handed to an external emulator.";
    public static string EmbeddedRomFormatUnsupported(int generation, string fileName) => Fr
        ? $"PKForge ne prend pas encore en charge ce format pour la génération {generation} : {fileName}. Les archives et formats DSi ne sont pas encore intégrés."
        : $"PKForge does not support this format for generation {generation} yet: {fileName}. Archives and DSi formats are not integrated yet.";

    // ---- External companion: play stats ----
    public static string SessionTime => Fr ? "Session" : "Session";
    public static string TotalPlaytime => Fr ? "Temps de jeu total" : "Total playtime";
    public static string SavePlaytime => Fr ? "Temps en sauvegarde" : "Save playtime";
    public static string DexProgressLabel => Fr ? "Dex" : "Dex";
    public static string PlaytimeHours(int hours, int minutes) => Fr ? $"{hours} h {minutes:00} min" : $"{hours}h {minutes:00}m";

    // ---- Trophies ----
    public static string TrophiesTitle => Fr ? "Trophées" : "Trophies";
    public static string MuseumTitle => Fr ? "Musée" : "Museum";
    public static string MuseumScan => Fr ? "Actualiser" : "Refresh";
    public static string MuseumPrevious => Fr ? "Précédent" : "Previous";
    public static string MuseumNext => Fr ? "Suivant" : "Next";
    public static string MuseumReading => Fr ? "Lecture des Pokédex liés…" : "Reading linked Pokédexes…";
    public static string MuseumReadOnly => Fr
        ? "Lecture seule · progression lue dans la dernière sauvegarde de chaque jeu."
        : "Read only · progress is read from each game's latest save.";
    public static string MuseumNoSaves => Fr
        ? "Aucune sauvegarde liée. Liez un jeu depuis l'écran d'accueil pour remplir le Musée."
        : "No linked saves. Link a game from Home to fill the Museum.";
    public static string MuseumReadError(string error) => Fr
        ? $"Sauvegarde illisible : {error}"
        : $"Save could not be read: {error}";
    public static string MuseumProgress(int seen, int caught, int total, int shiny) => Fr
        ? $"{seen}/{total} vus · {caught}/{total} capturés · {shiny} chromatiques"
        : $"{seen}/{total} seen · {caught}/{total} caught · {shiny} shiny";
    public static string MuseumNoDex => Fr
        ? "Aucune entrée Pokédex lisible pour cette sauvegarde."
        : "No readable Pokédex entries in this save.";
    public static string MuseumSpeciesState(bool seen, bool caught, bool shiny) => Fr
        ? $"{(caught ? "Capturé" : seen ? "Vu" : "Non découvert")}{(shiny ? " · chromatique dans la sauvegarde" : "")}"
        : $"{(caught ? "Caught" : seen ? "Seen" : "Unseen")}{(shiny ? " · shiny in this save" : "")}";
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

    // ---- Shelf status (SavePickerViewModel) ----
    public static string LinkStorageToBegin => Fr ? "Liez le stockage d'un émulateur pour commencer." : "Link an emulator's storage to begin.";
    public static string NoStorageLinkedYet => Fr ? "Aucun stockage lié. Ouvrez Lier et choisissez votre plateforme et émulateur." : "No storage units linked yet. Open Link and choose your platform and emulator.";
    public static string LinkCancelled => Fr ? "Liaison annulée." : "Link cancelled.";
    public static string StorageLinked => Fr ? "Stockage lié." : "Storage linked.";
    public static string Connected => Fr ? "Connecté." : "Connected.";
    public static string SelectSaveFile => Fr ? "Sélectionnez le fichier de sauvegarde à lier…" : "Select the save file to link…";
    public static string LinkingFile(string name) => Fr ? $"Liaison de {name}…" : $"Linking {name}…";
    public static string ConnectingTo(string game) => Fr ? $"Connexion à {game}…" : $"Connecting to {game}…";
    public static string CouldNotConnect(string error) => Fr ? $"Connexion impossible : {error}" : $"Could not connect: {error}";
    public static string CouldNotLinkSave(string error) => Fr ? $"Liaison impossible : {error}" : $"Could not link save: {error}";
    public static string EmulatorLinked(string emulator) => Fr ? $"{emulator} lié" : $"{emulator} linked";
    public static string NoSavesFoundIn(string folder, int rejected) => Fr
        ? $"Aucune sauvegarde trouvée dans {folder}" + (rejected > 0 ? $"{rejected} fichier(s) ressemblaient à des sauvegardes mais illisibles. " : "")
        : $"No saves found in {folder}";
    public static string FoundSaves(int found, string folder) => Fr
        ? $"{found} sauvegarde(s) Pokémon trouvée(s) dans {folder}."
        : $"Found {found} Pokémon save{(found == 1 ? "" : "s")} in {folder}.";
    public static string ScanningUnit(string emulator, string folder) => Fr ? $"Scan de l'unité {emulator} · {folder}…" : $"Scanning {emulator} unit · {folder}…";
    public static string ScanFailed(string emulator, string error) => Fr ? $"Le scan de {emulator} a échoué : {error}" : $"Scan of {emulator} failed: {error}";
    public static string SavesOnShelf(int saves, int files) => Fr
        ? $"{saves} sauvegarde(s) sur l'étagère. {files} fichier(s) scanné(s)."
        : $"{saves} save(s) on the shelf. Scanned {files} file(s).";
    public static string NoGamesFound(int files, int rejected) => Fr
        ? $"Aucun jeu trouvé. {files} fichier(s) scanné(s), {rejected} ressemblaient à des sauvegardes sans se laisser lire."
        : $"No games found. Scanned {files} file(s), {rejected} looked like saves but did not parse.";
    public static string ScanReportEmpty => Fr ? "Aucun scan diagnostique n'a encore tourné. Utilisez Rescanner les jeux, puis rouvrez le Rapport de scan." : "No diagnostic scan has run yet. Use Rescan games, then open Scan report again.";
    public static string FolderStaysLinked => Fr ? " Le dossier reste lié ; déliez-le depuis Gérer les stockages liés." : " The folder stays linked; unlink it from Manage linked storage.";

    // ---- Per-emulator folder guidance (the "nothing found" hints) ----
    public static string GuidanceDefault => Fr ? "Choisissez le dossier qui contient vos fichiers de sauvegarde." : "Pick the folder that holds your save files.";
    public static string GuidanceAzahar => Fr ? "Choisissez le dossier utilisateur de l'émulateur, celui qui contient sdmc (sdmc ou Nintendo 3DS fonctionnent aussi). Les sauvegardes vivent dans sdmc/Nintendo 3DS/…/title/00040000/<jeu>/data/00000001/main." : "Pick the emulator's user folder, the one that contains sdmc (sdmc or Nintendo 3DS work too). Saves live in sdmc/Nintendo 3DS/…/title/00040000/<game>/data/00000001/main.";
    public static string GuidanceMelonDs => Fr ? "Choisissez le dossier de vos fichiers .sav : à côté de vos ROMs par défaut, ou le dossier de sauvegardes réglé dans melonDS." : "Pick the folder that holds your .sav files: next to your ROMs by default, or the save folder set in melonDS's settings.";
    public static string GuidanceDrastic => Fr ? "Choisissez le dossier de sauvegardes de DraStic (ses .dsv) ou son dossier de données." : "Pick DraStic's backup folder (its .dsv battery saves) or the DraStic data folder.";
    public static string GuidanceEden => Fr ? "Choisissez la racine de fichiers d'Eden, le dossier qui contient son stockage émulé." : "Pick Eden's files root, the folder that contains its emulated storage.";

    // ---- Trophies page ----
    public static string ScanFailed(string error) => Fr ? $"Le scan a échoué : {error}" : $"Scan failed: {error}";
    public static string ScanningSaves => Fr ? "Analyse des sauvegardes…" : "Scanning saves…";
    public static string NoSaveScanned => Fr ? "Aucune sauvegarde liée n'a pu être analysée. Liez un émulateur d'abord, puis réanalysez." : "No linked save could be scanned. Link an emulator first, then scan again.";
    public static string ScannedSaves(int count) => Fr ? $"{count} sauvegarde(s) analysée(s). Chaque trophée prouvé par le scan est débloqué." : $"Scanned {count} save(s). Every trophy the scans prove is unlocked.";
    public static string Relock => Fr ? "Verrouiller à nouveau" : "Relock";
    public static string RelockQuestion(string title) => Fr ? $"Verrouiller à nouveau {title} ?" : $"Relock {title}?";
    public static string ManualTrophyRelockDetail => Fr ? "Les trophées manuels sont votre propre relevé. Le relock retire le déblocage." : "Manual trophies are your own record. Locking it back removes the unlock.";

    // ---- RetroAchievements page ----
    public static string SignIn => Fr ? "Se connecter" : "Sign in";
    public static string SignOut => Fr ? "Se déconnecter" : "Sign out";
    public static string SignedInAs(string user) => Fr ? $"Connecté en tant que {user}." : $"Signed in as {user}.";
    public static string SignedOut => Fr ? "Déconnecté. Les succès officiels restent sur retroachievements.org." : "Signed out. Official achievements stay on retroachievements.org.";
    public static string RaUsername => Fr ? "Nom d'utilisateur RA" : "RA username";
    public static string RaApiKey => Fr ? "Clé API web (panneau de contrôle)" : "Web API key (control panel)";
    public static string RaBadCredentials => Fr ? "RetroAchievements n'a pas reconnu ces identifiants." : "RetroAchievements did not recognise those credentials.";
    public static string RaSignInFailed(string error) => Fr ? $"Échec de connexion : {error}" : $"Sign-in failed: {error}";
    public static string RaRefreshFailed(string error) => Fr ? $"Échec du rafraîchissement : {error}" : $"Refresh failed: {error}";
    public static string RaNoSets => Fr ? "Aucun set de succès officiel trouvé pour vos jeux Gen 1-5." : "No official achievement sets found for your Gen 1-5 games.";
    public static string RaOffline => Fr ? "RetroAchievements injoignable : {0}. La progression en cache est affichée." : "Could not reach RetroAchievements: {0}. Cached progress is shown.";

    // ---- Restore points ----
    public static string RestorePointsTitle => Fr ? "Points de restauration" : "Restore points";
    public static string Restore => Fr ? "Restaurer" : "Restore";
    public static string RestoreThisPoint => Fr ? "Restaurer ce point ?" : "Restore this point?";
    public static string RestoreAnyway => Fr ? "Restaurer quand même" : "Restore anyway";
    public static string CancelKeepCurrent => Fr ? "Annuler (garder la sauvegarde actuelle)" : "Cancel (keep current save)";

    // ---- Box editor: shared buttons and actions ----
    public static string CancelLabel => Fr ? "Annuler" : "Cancel";
    public static string Apply => Fr ? "Appliquer" : "Apply";
    public static string Delete => Fr ? "Supprimer" : "Delete";
    public static string Duplicate => Fr ? "Dupliquer" : "Duplicate";
    public static string Release => Fr ? "Relâcher" : "Release";
    public static string MoveLabel => Fr ? "Déplacer" : "Move";
    public static string SortLabel => Fr ? "Trier" : "Sort";
    public static string Heal => Fr ? "Soigner" : "Heal";
    public static string EditLabel => Fr ? "Modifier" : "Edit";
    public static string SendToBank => Fr ? "Envoyer vers la Banque" : "Send to Bank";
    public static string CopyToBank => Fr ? "Copier vers la Banque" : "Copy to Bank";
    public static string CopyToAnotherGame => Fr ? "Copier vers un autre jeu…" : "Copy to another game…";
    public static string BoxManager => Fr ? "Gestion des boîtes…" : "Box manager…";
    public static string BatchEditor => Fr ? "Éditeur par lot" : "Batch editor";
    public static string LegalizeAll => Fr ? "Légaliser tout l'illégal" : "Legalize all illegal";
    public static string Legalizing => Fr ? "Légalisation…" : "Legalizing…";
    public static string LegalityCheck => Fr ? "Vérification de légalité" : "Legality check";
    public static string SaveChanges => Fr ? "ENREGISTRER" : "SAVE CHANGES";
    public static string RibbonAlbum => Fr ? "Album de rubans" : "Ribbon album";
    public static string HeldItem => Fr ? "Objet tenu" : "Held item";
    public static string MaxFriendship => Fr ? "Amitié max" : "Max friendship";
    public static string ImportShowdown => Fr ? "Importer une équipe Showdown" : "Import Showdown team";
    public static string PasteShowdown => Fr ? "Coller un set Showdown" : "Paste a Showdown set";
    public static string CopyToClipboard => Fr ? "Copier dans le presse-papiers" : "Copy to clipboard";
    public static string AuditReport => Fr ? "Rapport d'audit" : "Audit report";
    public static string NuzlockeReport => Fr ? "Rapport Nuzlocke" : "Nuzlocke report";
    public static string EmptySelectedBoxes => Fr ? "Vider les boîtes sélectionnées" : "Empty selected boxes";
    public static string ExportSave => Fr ? "Exporter la sauvegarde modifiée" : "Export modified save";
    public static string ThisBox => Fr ? "Cette boîte" : "This box";
    public static string AllBoxes => Fr ? "Toutes les boîtes" : "All boxes";
    public static string MyPresets => Fr ? "Mes présets d'objets" : "My item presets";
    public static string SaveBagPreset => Fr ? "Enregistrer la sacoche actuelle comme préset" : "Save current bag as preset";
    public static string RngIvs => Fr ? "RNG / IVs" : "RNG / IVs";
    public static string RemoveItems => Fr ? "Retirer tous les objets de cette poche" : "Remove every item in this pouch";
    public static string WonderCardsLabel => Fr ? "Wonder cards" : "Wonder cards";

    // ---- Platform names (menu Link) ----
    public static string PlatformGbGbc => Fr ? "Game Boy / Game Boy Color" : "Game Boy / Game Boy Color";
    public static string PlatformGba => "Game Boy Advance";
    public static string PlatformDs => "Nintendo DS";
    public static string PlatformGc => "GameCube";
    public static string Platform3ds => "Nintendo 3DS";
    public static string PlatformSwitch => "Nintendo Switch";
}
