<p align="center"><img src="docs/images/logo-transparent.png" alt="PKForge" width="140" /></p>

<p align="center"><b>Le hub Pokémon ultime pour consoles Android double écran — émulateurs, ROMs, trophées et companion, pensés pour l'AYN Thor.</b></p>

<p align="center">
  <a href="https://discord.gg/bMtzZmTDfu"><img src="https://img.shields.io/badge/Discord-Rejoindre-5865F2?logo=discord&logoColor=white" alt="Discord" /></a>
  <a href="#-téléchargement"><img src="https://img.shields.io/badge/Télécharger-APK-2B4E95" alt="Download" /></a>
  <a href="#-roadmap"><img src="https://img.shields.io/badge/Roadmap-Prochainement-2B4E95" alt="Roadmap" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/Licence-GPLv3-blue" alt="License" /></a>
</p>

---

**PKForge** relie vos ROMs à vos émulateurs, lit vos sauvegardes pour en tirer trophées
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
- **Liaison console → émulateur** (style Cocoon) : un tap par console choisit
  l'émulateur qui lance ses ROMs. RetroArch, melonDS, Azahar, Eden, Dolphin, DraStic,
  Pizza Boy, Linkboy détectés automatiquement.
- **Play** : le jeu démarre directement dans le bon émulateur, sans passer par son menu.

### 🏆 Trophées & progression
- **Trophées maison** (~30 par jeu) : paliers de dex, shinies, temps de jeu,
  collection — débloqués par scan de vos sauvegardes, permanents.
- **Trophées manuels** : champion, boss, événements — confirmés par le joueur.
- **RetroAchievements** : connexion à votre compte, sets officiels Gen 1-5,
  progression en cache hors-ligne.
- **Watcher automatique** : au retour d'un émulateur, les nouveaux trophées sont
  détectés et annoncés par notification.

### 🏛️ Musée & banque
- **Musée** : le Pokédex par jeu (vu / capturé / chromatique) lu depuis vos sauvegardes.
- **Banque** : stockage inter-générations, import/export `.pk`, éditeur complet,
  autopilot living dex.
- **Écran secondaire** : trophées, boîtes et journal Poképark sur l'écran Thor.

## 🗺️ Roadmap

### ✅ Fait
- [x] Onglet Émulateurs + liaison par console (style Cocoon)
- [x] Bibliothèque de ROMs avec scan ES-DE et catalogue complet Gen 1-9
- [x] Launcher Play (RetroArch, melonDS, Azahar, Eden + détection étendue)
- [x] **GB/GBC/GBA joués nativement dans l'app** : cœur mGBA embarqué
      (`libpkforge_mgba.so`, arm64), rendu SkiaSharp pixel-crisp à 59,7275 Hz,
      audio 32768 Hz, sauvegardes `.sav` mappées — sans émulateur externe
- [x] **Companion live (modèle PokéDaisy)** : pendant qu'un jeu GBA tourne dans
      l'app, le second écran lit la mémoire de la console en direct — dex, équipe,
      temps de jeu — rafraîchi chaque seconde
- [x] Trophées maison par scan de sauvegardes + trophées manuels
- [x] RetroAchievements (Gen 1-5) avec cache hors-ligne
- [x] Watcher automatique + notifications de déblocage
- [x] Écran secondaire : ladder de trophées pendant la page Trophées
- [x] Interface française par défaut (anglais disponible)

### 🚧 En cours
- [ ] **Companion de jeu externe** : le second écran reste vivant pendant l'émulation
      externe (émulateurs tiers) — stats, Pokédex, points de restauration
- [ ] **Intégration des mods** : Radical Red, Unbound, GS Chronicles… détectés et
      lancés depuis l'app avec leurs réglages dédiés
- [ ] Traduction française complète des surfaces restantes (box browser, éditeurs)

### 🔮 Planifié
- [ ] Cœur DS embarqué (melonDS) pour les jeux DS natifs
- [ ] Musée (grille Pokédex) sur l'écran secondaire
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

- **Domain** : règles pures (trophées, parsing des ROMs, catalogues) — testées xunit
- **Infrastructure** : persistance JSON atomique
- **App** : .NET MAUI 10 (net10.0-android), MVVM, double écran

## 📄 Licence

GPLv3 — voir [LICENSE](LICENSE). Basé sur
[PKForge](https://github.com/sofianeelhor/PKForge) et
[PKHeX](https://github.com/kwsch/PKHeX).

Non affilié à Nintendo, Game Freak ou The Pokémon Company.

---

<p align="center">
  <sub>Built with ❤️ for dual-screen handhelds</sub>
</p>
