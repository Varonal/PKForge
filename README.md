<p align="center"><img src="docs/images/logo-transparent.png" alt="PKForge" width="140" /></p>

<p align="center"><b>Le hub Pokémon ultime pour consoles Android double écran — émulateurs, ROMs, trophées et companion, pensés pour l'AYN Thor.</b></p>

<p align="center">
  <a href="https://discord.gg/bMtzZmTDfu"><img src="https://img.shields.io/badge/Discord-Rejoindre-5865F2?logo=discord&logoColor=white" alt="Discord" /></a>
  <a href="#-téléchargement"><img src="https://img.shields.io/badge/Télécharger-APK-2B4E95" alt="Download" /></a>
  <a href="#-roadmap"><img src="https://img.shields.io/badge/Roadmap-Prochainement-2B4E95" alt="Roadmap" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/Licence-GPLv3-blue" alt="License" /></a>
</p>

---

**PKForge** intègre progressivement l'émulation Pokémon, lit vos sauvegardes pour en tirer trophées
et musée, et transforme le second écran de votre console en companion de jeu. Construit
sur [PKHeX.Core](https://github.com/kwsch/PKHeX) et l'
[Auto Legality Mod](https://github.com/santacrab2/PKHeX-Plugins), tout fonctionne
hors-ligne, sur la console elle-même.

<p align="center">
  <img src="docs/screenshots/dual-screen.png" alt="Une boîte sur l'écran principal, l'inspecteur Pokémon sur le second écran" width="100%" />
</p>

## ✨ Fonctionnalités

### 🎮 Launcher & émulateurs
- **Bibliothèque de jeux** : les 37 jeux Pokémon (Gen 1 → 9) en cartes avec artwork,
  label coloré possédée/absente, et scan automatique de l'arborescence ES-DE
  (`/storage/emulated/0/ROMs/`) — dumps No-Intro, français et anglais reconnus.
- **Cœurs intégrés** : GB/GBC/GBA et DS `.nds` démarrent dans PKForge, sans lancer
  une autre application. Les anciens choix DS vers melonDS/DraStic sont ignorés et
  corrigés au prochain lancement. Les jeux DS `.zip` sont acceptés s'ils contiennent
  une seule ROM `.nds`; les archives multi-ROM, `.7z` et les jeux DSi restent à intégrer.
- **Compatibilité transitoire** : les jeux des consoles qui n'ont pas encore de cœur
  PKForge peuvent toujours être lancés par RetroArch, Azahar, Eden ou Dolphin.
  Ces intégrations externes ne sont pas l'objectif final.
- **Play** : les consoles intégrées restent dans PKForge; les plateformes en attente
  conservent leur parcours externe temporaire.

### 🏆 Trophées & progression
- **Trophées maison** (~30 par jeu) : paliers de dex, shinies, temps de jeu,
  collection — débloqués par scan de vos sauvegardes, permanents.
- **Trophées manuels** : champion, boss, événements — confirmés par le joueur.
- **RetroAchievements** : connexion à votre compte, sets officiels Gen 1-5,
  progression en cache hors-ligne.
- **Watcher automatique** : au retour d'un émulateur, les nouveaux trophées sont
  détectés et annoncés par notification.

### 🏛️ Musée & banque
- **Musée** : une entrée par sauvegarde liée, avec la grille Pokédex en lecture seule,
  les états vus/capturés et les chromatiques actuellement stockés dans le jeu. La fiche
  de l'espèce sélectionnée s'affiche aussi sur l'écran secondaire.
- **Banque** : stockage inter-générations, import/export `.pk`, éditeur complet,
  autopilot living dex.
- **Échange entre jeux** : aperçu de conversion et de légalité, écriture destination
  avant libération de la source, retour arrière vérifié si la source refuse l'écriture.
- **Écran secondaire** : trophées, boîtes et journal Poképark sur l'écran Thor.

## 🗺️ Roadmap

### ✅ Fait
- [x] Onglet Émulateurs + liaison par console (style Cocoon)
- [x] Bibliothèque de ROMs avec scan ES-DE, recherche par jeu/fichier, prise en charge
      de plusieurs fichiers pour une même édition et navigation à la manette
- [x] Parcours externe temporaire pour les consoles dont le cœur PKForge reste à intégrer
- [x] **GB/GBC/GBA joués dans l'app, uniquement** : cœur mGBA embarqué
      (`libpkforge_mgba.so`, arm64), ROMs locales ou choisies via Android, rendu
      SkiaSharp pixel-crisp au rythme de la console, audio 32768 Hz et sauvegardes
      batterie `.sav` persistantes. Une sauvegarde liée est synchronisée à la sortie
      et lors de la mise en arrière-plan.
      L'image peut remplir l'écran, conserver les pixels entiers ou être réduite
      à 90/75 %; le plein écran est activé par défaut. Pause, états rapides,
      vitesse ×1/×2/×3/×4, commandes tactiles et manettes sont accessibles pendant le jeu;
      la vitesse est aussi réglable depuis les options du compagnon sur l'écran secondaire;
      l'audio est coupé en accéléré. Ces consoles
      ne sont plus envoyées à un émulateur externe.
- [x] **Compagnon GB/GBC/GBA** : équipe (sprites de forme, chromatiques, œufs, noms,
      niveaux, PV et statuts), Pokédex, temps de jeu et sac en lecture seule
      (poches, noms, quantités) depuis
      la dernière sauvegarde valide. GB/GBC/GBA sont analysées avec les formats
      de sauvegarde de chaque génération; les pages visibles et la page d'accueil sont
      réglables. Les changements non sauvegardés en jeu ne sont pas encore lus.
- [x] **Companion de jeu externe (modèle polling .sav)** : pendant qu'un jeu tourne dans un
      émulateur tiers, le second écran surveille sa sauvegarde (stat toutes les 5 s,
      relecture après flush stable) et affiche dex, équipe, temps de sauvegarde, temps de
      session. Le companion reste en lecture seule.
- [x] Trophées maison par scan de sauvegardes + trophées manuels
- [x] Musée en lecture seule : Pokédex distinct par sauvegarde liée, grille par jeu,
      états vus/capturés, marques des chromatiques stockés et fiche sur l'écran secondaire
- [x] Échange entre sauvegardes : aperçu avant écriture et transfert en saga; un échec
      de libération de la source tente un retour arrière après comparaison des cases de
      destination avec le reçu exact du transfert
- [x] RetroAchievements (Gen 1-5) avec cache hors-ligne
- [x] Watcher automatique + notifications de déblocage
- [x] Écran secondaire : ladder de trophées pendant la page Trophées
- [x] Interface française par défaut (anglais disponible)

### 🚧 En cours
- [ ] **Points de restauration du compagnon externe** : les règles de filtrage Domain existent,
      mais la vue ne liste/crée/restaure pas encore les backups du `.sav` de l'émulateur.
      La restauration doit attendre l'arrêt de l'émulateur pour éviter qu'il réécrive sa RAM
      sur le fichier restauré.
- [ ] **DS Gen 4-5 : valider le cœur intégré de bout en bout** : melonDS 1.1 arm64 est
      compilé dans PKForge pour les ROM `.nds` et les archives `.zip` qui contiennent une
      seule ROM `.nds`. L'écran haut s'affiche sur
      l'écran principal et l'écran tactile bas sur la dalle secondaire lorsqu'Android en expose
      une; sur un appareil à écran unique, les deux restent empilés. Boutons, stylet, audio,
      vitesse ×1/×2/×3/×4 (audio coupé en accéléré), sauvegarde batterie et état rapide sont reliés à l'interface. La route Play ignore les
      anciennes liaisons externes. Il reste la validation sur AYN Thor avec des jeux réels et la
      vérification des sauvegardes en jeu. Les archives contenant plusieurs `.nds`, `.7z`,
      DSiWare, Wi-Fi et microphone ne sont pas encore pris en charge.
- [ ] **Données du compagnon DS Gen 4-5** : équipe, Pokédex, temps de jeu et sac lus depuis
      l'image batterie melonDS uniquement quand le format et ses checksums sont valides;
      l'instantané publié au compagnon décrit la dernière sauvegarde, pas la RAM non
      sauvegardée. La lecture et le flux sont prêts; il reste la validation avec des saves
      réelles.
- [ ] **Compagnon DS pendant l'émulation** : les données live sont lues depuis la sauvegarde,
      mais la page compagnon n'est pas affichée pendant la session. L'écran inférieur reste
      rendu sur le second panneau de la Thor; concevoir une présentation accessible sans
      recouvrir l'un des deux écrans et la valider sur appareil.
- [ ] **3DS Gen 6-7 : cœur embarqué en préparation** : le code source Azahar
      `2127.0-alpha1` est épinglé et le script Android arm64 prépare son cœur libretro
      avec OpenGL et Vulkan comme dans sa configuration Android. Le cœur et le frontend ne
      sont pas encore reliés au parcours de jeu; l'émulation intégrée, les commandes, l'audio,
      le rendu dans l'app, l'écran tactile, les sauvegardes et les fichiers système restent
      à implémenter.
- [ ] **Intégration des mods** : Radical Red, Unbound, GS Chronicles… détectés et
      lancés depuis l'app avec leurs réglages dédiés
- [ ] Traduction française complète des surfaces restantes (box browser, éditeurs)

### 🔮 Planifié
- [ ] **Émulation Pokémon sans dépendance à un émulateur tiers** : intégrer un cœur PKForge
      pour chaque console ayant reçu un jeu Pokémon — GB, GBC, GBA, DS, 3DS, Nintendo 64,
      GameCube, Wii, Wii U, Switch et Pokémon mini — puis retirer le lancement externe du
      parcours Pokémon. Construire et valider chaque plateforme séparément : cœur natif,
      rendu, commandes, audio, sauvegardes/états, cycle de vie Android et tests sur appareils.
      Les consoles de salon et systèmes historiques suivront selon leur faisabilité technique
      et leurs contraintes de licence.
- [ ] Grille complète du Musée sur l'écran secondaire (la fiche de l'espèce sélectionnée y est affichée)
- [ ] Watcher en arrière-plan (WorkManager) — scans sans l'app ouverte
- [ ] Mises à jour in-app des définitions de trophées et du catalogue
- [ ] Social : partage de progression, classements entre amis
- [ ] Hardcore mode RetroAchievements complet

## 📥 Téléchargement

Les APK signés sont publiés dans [Releases](../../releases). Le build
diagnostic (sideload) s'installe à côté de la version Play sans la toucher.

```bash
dotnet build src/PKForge.App/PKForge.App.csproj -f net10.0-android -p:DiagnosticBuild=true
```

## 🛠️ Compilation

```bash
git clone --recurse-submodules https://github.com/Varonal/PKForge
cd PKForge
dotnet restore PKForge.sln
dotnet test tests/PKForge.Domain.Tests --configuration Release
dotnet build src/PKForge.App/PKForge.App.csproj -f net10.0-android
```

Le cœur DS natif est épinglé comme sous-module melonDS. Pour le reconstruire sur Windows
avec un NDK installé, exécute `powershell -File tools/build-melonds/build.ps1`.
Le script construit le cœur arm64, retire les symboles de débogage et le place dans l'APK.

Le cœur 3DS préparatoire est épinglé sur Azahar `2127.0-alpha1`. Après installation de
ses sous-modules et d'un NDK Android arm64, son cœur libretro se construit avec
`powershell -File tools/build-azahar/build.ps1`. Cette étape ne rend pas encore les ROMs
3DS jouables dans PKForge; le frontend intégré reste à construire.

- **Domain** : règles pures (trophées, parsing des ROMs, catalogues) — testées xunit
- **Infrastructure** : persistance JSON atomique
- **App** : .NET MAUI 10 (net10.0-android), MVVM, double écran

## 📄 Licence

GPLv3 — voir [LICENSE](LICENSE). Basé sur
[PKForge](https://github.com/sofianeelhor/PKForge) et
[PKHeX](https://github.com/kwsch/PKHeX). L'émulation DS embarquée utilise
[melonDS](https://github.com/melonDS-emu/melonDS) 1.1; son code source et sa licence GPL-3.0
sont conservés dans `external/melonDS`. Le cœur 3DS préparatoire est dérivé d'
[Azahar](https://github.com/azahar-emu/azahar) et sa licence GPL-3.0 est conservée dans
`external/azahar`.

Non affilié à Nintendo, Game Freak ou The Pokémon Company.

---

<p align="center">
  <sub>Built with ❤️ for dual-screen handhelds</sub>
</p>
