# AGENTS.md — PKForge

Conventions générales du dépôt (voir aussi `progress.txt`, section `## Codebase Patterns`,
qui accumule les apprentissages de chaque itération).

## Architecture en 3 couches (stricte)
- **`src/PKForge.Domain`** : règles pures — records, calculs, parsing. **Aucune** dépendance
  Android, MAUI, I/O fichier ou PKHeX. Un « SaveFile PKHeX parsée » côté Domain = un
  `ISaveEngineSession` ouvert ou les records extraits (`SaveSnapshot`, `DexProgress`,
  `TrainerStats` dans `Contracts.cs` / `EntityEditing.cs`). Toute I/O va dans App/Infrastructure.
- **`src/PKForge.Infrastructure`** : persistance JSON **atomique** (temp + `File.Move(overwrite:true)`,
  catch `IOException`/`UnauthorizedAccessException`, Load tolérant au JSON corrompu → vide/null,
  path null = mémoire seule). Pattern de référence : `JsonConsoleEmulatorStore`.
- **`src/PKForge.App`** : MAUI MVVM. Services dans `Services/` (enregistrés dans `MauiProgram`),
  vues dans `Views/`. Chaînes UI **français par défaut** dans `Services/AppStrings.cs`
  (`Fr ? "…" : "…"`) — la logique pure Domain prend la locale en paramètre `bool`, jamais l'inverse.

## Patterns clés
- **Tests** : xunit dans `tests/PKForge.Domain.Tests`. Interfaces énormes (~107 membres,
  ex. `ISaveEngineSession`) : ne jamais écrire un fake à la main → `System.Reflection.DispatchProxy`
  (classe proxy **non sealed**, cast via `(TProxy)(object)proxy`, propriétés reçues en `get_Xxx`
  dans `Invoke`). `TreatWarningsAsErrors=true` : initialiser les champs non-nullables.
- **Polling économe** : jamais de boucle serrée. Stat toutes les ~5 s (`PeriodicTimer`) uniquement
  pendant une session active ; décisions (changé ? stable ? publier ?) en fonctions pures Domain
  sur stamps injectés (`SaveFileStamp`), I/O côté App. Anti-flush-partiel : 2 lectures identiques
  avant de parser + `ISaveEngine.Validate` avant `OpenSession`. `PeriodicTimer` disposé →
  `ObjectDisposedException` (pas `OperationCanceledException`) à attraper silencieusement.
- **Mesure de durée sans batterie** : pas de timer — durées calculées sur timestamps d'événements
  (`SessionClock.Start/Pause/Resume/Close`); après `Close`/`Idle`, rien ne mesure.
- **Sauvegardes externes** : chemins candidats par émulateur dans `ExternalSavePathResolver`
  (registre `Conventions` extensible, pur, existence via `Func<string,bool>` injecté);
  restore points via `IBackupService` (pipeline atomique, hash SHA256 vérifié au read,
  `BackupInfo.BelongsTo(documentId, snapshot)`); restore = confirmation `PadMenu.ConfirmAsync`
  + safety backup + écriture atomique, **jamais in-place**.
- **Lifecycle « retour d'émulateur »** : unique hook `App.Resumed` → `HomePage.OnAppResumed` →
  `WatchTrophiesAfterResumeAsync` (companion Stop + rescan + trophées). Ne pas en créer un second.
- **Chemins Android dans le Domain** : normaliser `\` → `/` soi-même (les tests tournent sur Linux).

## Boucle Ralph (itérations autonomes)
État dans `prd.json` (stories, champ `branchName`) + `progress.txt` (APPEND uniquement).
Une story par itération = priorité la plus haute avec `passes: false`. Commit conventionnel
`feat: US-XXX - ...`, `prd.json` → `passes: true` (+ `notes` pour les vérifs manuelles console),
`progress.txt` en append. CI `build` (tests Release + APK `net10.0-android`) doit rester verte.
