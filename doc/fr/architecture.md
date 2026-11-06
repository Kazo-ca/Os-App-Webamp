> [English version](../en/architecture.md)

# Architecture de l'app Webamp

## But

Webamp est une app fenêtrée de Kazo OS : un lecteur de musique dans le style de
Winamp 2, construit sur [Webamp](https://github.com/captbaritone/webamp) 2.3.1.
Le but et les décisions sont dans le [plan](plan.md). Ce document décrit le
code ; chaque chantier de la série `webamp/base` le complète.

## Solution

```text
Os-App-Webamp/
├─ external/KazoOs/          # sous-module : Kazo OS sur le tag v3.0.0 (la version du dépôt)
├─ Directory.Build.props     # racine du sous-module, profil, import de Versions.props
├─ Directory.Build.targets   # mode source en Debug, KOSAPP0070 sans sous-module
├─ global.json
├─ Kazo.Webamp.slnx
├─ src/
│  ├─ Kazo.Webamp/           # l'app
│  └─ Kazo.Webamp.Os/        # hôte : Kazo OS, ses apps de base et Webamp
├─ tests/
│  ├─ Kazo.Webamp.Tests/     # tests unitaires C# (xUnit)
│  ├─ js/                    # tests du module JavaScript (node --test)
│  └─ e2e/                   # parcours dans l'hôte (node --test + Playwright)
├─ store/                    # fiche du Kazo OS Store (complétée au chantier 6)
├─ .github/workflows/        # ci.yml, publish-*.yml, tag-release.yml
└─ package.json              # outils des tests JavaScript
```

Le dépôt suit la sortie du modèle **`kazoos-solution`** de Kazo OS 3.0.0
(ADR 0176 et 0207), comme [Chess](https://github.com/Kazo-ca/Os-App-Chess) :
`external/KazoOs` est Kazo OS en sous-module, exactement sur un tag de release,
et son `build/Versions.props` donne **la** version du dépôt. On ne modifie
jamais le sous-module : la CI refuse un sous-module modifié ou hors de son tag
(`.github/scripts/check-kazoos-submodule.sh`). Les workflows viennent de Chess
(les mêmes que ceux du modèle, avec les corrections faites depuis : fork du
catalogue trouvé par propriétaire, workflow « Tag a release », CI qui lance aussi
les tests JavaScript et Playwright).

| Profil | Kazo OS vient de | Usage |
| --- | --- | --- |
| `Debug` (`KazoOsUseSource=true`) | les projets du sous-module (on entre dans le SDK et l'OS) | développement, débogage |
| `Release` (`KazoOsUseSource=false`) | les paquets `Kazo.Os.*` de nuget.org, à la version du sous-module | CI, tests E2E, Kazo OS Store |

Cloner : `git clone --recurse-submodules`, ou `git submodule update --init`
après coup (sans sous-module, le build s'arrête sur `KOSAPP0070`).

**Hôte.** `Kazo.Webamp.Os` est un Kazo OS complet avec les apps de base et
Webamp en `ProjectReference` : ses fichiers `wwwroot/` sont servis à
`_content/Kazo.Webamp/`. `dotnet run --project src/Kazo.Webamp.Os` le lance
sur <http://localhost:5290>.

## L'app `Kazo.Webamp`

| Dossier ou fichier | Rôle |
| --- | --- |
| `WebampApp.cs` | Manifeste (`[AppManifest]`) : id `ca.kazo.webamp`, nom « Webamp », version `1.0.0`, éditeur `pub.kazo`, catégorie `multimedia`, **ordinateur seulement** (pas de `MobileEntryPoint`), `SingleInstancePerUser`. Fenêtre de 277×388 (le contenu : les trois fenêtres de Webamp empilées, 275×348, plus le cadre et la barre de titre de l'OS), redimensionnable, au plus petit la fenêtre principale seule. Capacités de base : `filesystem.user-home.read`, `filesystem.private.read` et `.write` ; le chantier 3 ajoute `filesystem.user-home.write`, `dialogs.file-open` et `dialogs.file-save` (« Load list », « Save list »). Types associés du chantier 3 : `audio/x-mpegurl .m3u`, `application/vnd.apple.mpegurl .m3u8`, `audio/x-scpls .pls` (action `open`). Les chantiers 2, 4 et 5 ajoutent les leurs. |
| `WebampWindow.razor` (+ `.cs`, `.css`) | La fenêtre : un élément où Webamp se dessine (`renderInto`) et une ligne d'état (démarrage, fichier illisible, ce qu'une liste a laissé de côté), qu'un bouton × ferme. Au premier rendu, elle démarre `WebampPlayer` et crée `TrackLibrary` ; ouverte sur un fichier (`LaunchFile`), elle le joue, sur une liste elle ajoute ses morceaux et joue le premier (`OpenFilesAsync`). Elle répond à « Load list » et « Save list » de Webamp par les dialogues de l'OS. Le bouton fermer de Webamp arrête le processus de l'app, ce qui ferme la fenêtre de l'OS et coupe le son. Le CSS cache « Add URL » (décision 9) et garde Webamp dans la fenêtre. Au démarrage, elle relit l'état (`state.json`) et le remet dans Webamp ; elle écrit l'état à chaque changement (regroupé) et une dernière fois à la fermeture (`IWindowCloseGuard`, arrêt du processus, `DisposeAsync`). Son titre suit le morceau en cours (`Player/TrackTitle`), et elle dit au module quand elle a le focus (raccourcis). |
| `Player/WebampPlayer.cs` | Le côté C# d'une instance de Webamp : importe `wwwroot/js/webamp-host.js`, lui passe l'élément, une référence .NET et les réglages enregistrés, envoie les morceaux, reçoit les événements (`Closed`, `Minimized`, `TrackChanged`, `PlaylistChanged`, `LoadListRequested`, `SaveListRequested`, `StateChanged`). Implémente `ITrackPlayer` et `IStatePlayer` (lire l'état, reprendre un morceau à sa position, appliquer un skin) ; `SetFocusedAsync` règle les raccourcis. `DisposeAsync` arrête la musique et libère tout. |
| `Player/TrackTitle.cs` | Le titre de la fenêtre de l'OS : « Artiste - Titre - Webamp », « Titre - Webamp » sans artiste, le nom du fichier sans étiquettes, « Webamp » sans morceau (décision 8). |
| `State/` | L'état retenu (chantier 5, ci-dessous) : `WebampState` (le contenu de `state.json`), `WebampStateJson` (lecture tolérante, écriture), `WebampStateFile` (le fichier), `StateSaveScheduler` (écritures regroupées), `PlaylistMemory` (fichiers disparus gardés dans la liste), `WebampStateKeeper` (remettre l'état dans Webamp, le relire), `IStatePlayer`. |
| `Tracks/TrackLibrary.cs` | Les fichiers de la liste de Webamp (décision 14) : lus entiers par `KazoFile.ReadAllBytesAsync`, donnés à Webamp, oubliés quand ils sortent de la liste. API des chantiers 2 à 5 : `AddAsync(chemins, play)`, `ReplaceAsync(chemins, play)` (et leurs surcharges qui prennent des `PlaylistEntry`, avec titre et durée), `TryGetPath(clé)`, `Paths` (liste courante, dans l'ordre de Webamp), `LoadedBytes` (pour l'avertissement de 1 Go du chantier 2), `Changed`. Un fichier illisible est rendu dans `TrackLoadResult.Failures`, les autres sont ajoutés. |
| `Tracks/ITrackPlayer.cs`, `TrackData.cs`, `TrackLoadResult.cs` | Le contrat entre la bibliothèque et Webamp (un faux dans les tests) ; un morceau envoyé (clé, nom, type, octets, durée connue d'une liste) ; le résultat d'un ajout. |
| `Playlists/` | Les listes de lecture (chantier 3, ci-dessous) : `PlaylistReader`, `PlaylistWriter`, `PlaylistPaths`, `PlaylistFiles`, `PlaylistFormats`. |
| `Player/WebampListEntry.cs` | Un morceau de la liste de Webamp pour « Save list » (clé, titre, durée). |
| `Tracks/AudioMediaTypes.cs` | Extension → type audio (décision 4), donné au `Blob` pour que le navigateur choisisse le bon décodeur. |
| `Strings.cs`, `Resources/` | Textes en anglais et en français (ADR 0104 de Kazo OS), nom et description traduits du manifeste (`AppManifest.fr.resx`). |
| `wwwroot/js/webamp-host.js` | Le module JavaScript (ci-dessous). |
| `wwwroot/lib/` | Webamp 2.3.1, Butterchurn et ses préréglages, chacun avec sa licence ([THIRD-PARTY-NOTICES.md](../../THIRD-PARTY-NOTICES.md)). |
| `wwwroot/icons/app.svg` | L'icône. |

## C# et JavaScript

Le C# garde la logique : quels fichiers sont dans la liste, d'où ils viennent,
quoi jouer. Le module `webamp-host.js` fait seulement ce que le C# ne peut pas
atteindre : Webamp lui-même, les adresses `blob:` et les événements du
navigateur.

```text
WebampWindow ── TrackLibrary ── lit ──► KazoFile.ReadAllBytesAsync (le fichier entier)
                    │
                    ▼  ITrackPlayer
               WebampPlayer ── import ──► webamp-host.js ── new Webamp(...) ──► renderInto(élément)
                    ▲                          │
                    └──── invokeMethodAsync ───┘  OnWebampClosed, OnWebampMinimized,
                                                  OnTrackChanged, OnPlaylistChanged,
                                                  OnLoadListRequested, OnSaveListRequested
```

| Appel | Sens | Contenu |
| --- | --- | --- |
| `create(élément, dotNet, { zIndex, settings, focused })` | C# → JS | Charge `lib/webamp/webamp.bundle.min.mjs`, crée Webamp avec `enableMediaSession: true` (décision 8), un `handleAddUrlEvent` qui ne fait rien (décision 9), Milkdrop paresseux (décision 12) et, depuis `settings` (les `PlayerSettings` enregistrés, `null` au premier lancement), la disposition des fenêtres (`windowLayout`) et la taille double ; remet le volume, la balance, l'égaliseur, shuffle et repeat et, si l'utilisateur avait déplacé les fenêtres, leurs places exactes, avant la première image ; le dessine dans l'élément. Rend un objet hôte (une `IJSObjectReference`). |
| `addTracks(morceaux, play)` | C# → JS | Ajoute à la fin de la liste ; avec `play`, le premier ajouté joue. Un morceau est `{ key, name, mimeType, bytes, durationSeconds }` ; `bytes` est le `byte[]` du C#, reçu en `Uint8Array` ; `durationSeconds` (d'une liste, sinon `null`) est montrée jusqu'à ce que Webamp lise le fichier. |
| `replaceTracks(morceaux, play)` | C# → JS | Remplace la liste (`setTracksToPlay`) ; sans `play`, Webamp est arrêté. |
| `dispose()` | C# → JS | Arrête la musique, retire Webamp, rend toutes les adresses. |
| `OnPlaylistChanged(clés)` | JS → C# | Les clés encore dans la liste, dans son ordre, à chaque changement réel (pas à chaque tic de l'horloge de Webamp). |
| `OnTrackChanged({ artist, title, name } \| null)` | JS → C# | Le morceau en cours, qu'il joue ou soit en pause, à chaque changement de morceau ou d'étiquettes (`name` : le nom du fichier) ; `null` sans morceau. Pour le titre de la fenêtre. |
| `readState()` | C# → JS | Ce que Webamp tient (`PlayerSnapshot`) : la clé du morceau en cours, la position (0 à l'arrêt), le volume, la balance, shuffle, repeat, l'égaliseur, la taille double, les quatre fenêtres (place, ouverte, enroulée, taille en plus) et si leurs places sont fixées par l'utilisateur. |
| `resume(index, position)` | C# → JS | Rend un morceau courant sans le jouer ; avec une position, attend qu'il soit chargé, s'y place et met Webamp en pause (Play repart de là). |
| `setSkin(octets)` | C# → JS | Applique un skin (`.wsz`) par une adresse `blob:`, rendue au skin suivant ou à `dispose`. |
| `setFocused(focus)` | C# → JS | Les raccourcis de Webamp agissent seulement quand la fenêtre a le focus. |
| `OnStateChanged()` | JS → C# | Un élément de l'état a changé (pas l'horloge : la position compte par pas de 5 s pendant la lecture). |
| `OnWebampClosed()`, `OnWebampMinimized()` | JS → C# | Les boutons fermer et réduire de Webamp. |
| `OnLoadListRequested()` | JS → C# | « Load list » (`handleLoadListEvent`, qui rend `null` à Webamp : le C# remplace la liste lui-même). |
| `OnSaveListRequested(morceaux, nonEnregistrés)` | JS → C# | « Save list » (`handleSaveListEvent`) : les morceaux qui ont une clé, `{ key, title, duration }` dans l'ordre (titre « Artiste - Titre » des étiquettes, sinon le nom affiché), et le nombre de morceaux sans clé (glissés depuis l'ordinateur). |

**Les `Blob`.** Pour chaque morceau, le module crée un `Blob` typé et son
adresse (`URL.createObjectURL`) et donne à Webamp un morceau **par adresse**
(`{ url, defaultName }`) plutôt qu'un `BlobTrack` : à partir d'un `Blob`, Webamp
crée l'adresse lui-même et ne la rend jamais. Le module suit la liste par
`webamp.store.subscribe` : une adresse qui a été dans la liste et n'y est plus
est rendue (`URL.revokeObjectURL`) et sa clé disparaît du rapport suivant, ce que
`TrackLibrary.OnPlaylistChanged` traduit en oubliant le fichier. Webamp ajoute
un lot morceau par morceau : une adresse pas encore vue dans la liste n'est donc
jamais rendue. Les morceaux que l'utilisateur glisse depuis l'ordinateur sont
ceux de Webamp : sans clé, ils n'apparaissent pas dans `Paths`.

**Le bundle.** L'app prend `webamp.bundle.min.mjs` (940 Ko, avec JSZip et
music-metadata) et non `webamp.lazy-bundle.min.mjs` : le bundle paresseux
demande qu'on lui fournisse JSZip et music-metadata, et music-metadata n'existe
pas en module prêt pour le navigateur. Butterchurn n'est pas dans ce bundle : le
module le charge à la première ouverture de Milkdrop (voir « État, titre,
raccourcis, Milkdrop ») par
`__butterchurnOptions` (`importButterchurn` vers `lib/butterchurn/butterchurn.min.js`,
module ES autonome ; `getPresets` vers `lib/butterchurn-presets/base.min.js`, qui
se déclare dans `self.base`). Le bundle `webamp.butterchurn-bundle.min.mjs` de
Webamp ne sert pas : c'est un Webamp complet de 2 Mo qui charge Butterchurn dès
le démarrage.

## Fermer et réduire

- **Fermer** (bouton de Webamp, ou « Exit » de son menu) : `WebampPlayer.Closed`
  → la fenêtre arrête le processus de l'app
  (`AppContext.Processes.StopAsync(AppContext.ProcessId, …)`, permis sans
  capacité). La fenêtre de l'OS se ferme, `DisposeAsync` arrête la musique et
  rend la mémoire (décision 8).
- **Réduire** : le SDK de Kazo OS 3.0.0 ne permet pas à une app de réduire sa
  propre fenêtre. Benoit a choisi (2026-11-06, question 1, réponse A) : une issue
  TODO [Os#384](https://github.com/Kazo-ca/Os/issues/384) ; d'ici là le bouton
  réduire de Webamp ne fait rien et celui de la barre de titre de l'OS marche. Le
  chantier 7 le branche (`WebampPlayer.Minimized`) quand le sous-module avance à
  la version qui l'apporte.

## Webamp dans une fenêtre de l'OS

- Webamp centre ses trois fenêtres dans l'élément ; on peut les déplacer à
  l'intérieur, comme sur une page.
- Une première pression sur une fenêtre de Webamp qui n'est pas au premier plan
  la sélectionne seulement (Webamp la passe devant), la suivante agit : c'est le
  fonctionnement de Webamp, les tests E2E en tiennent compte.
- Webamp ajoute ses styles dans la page lui-même ; le `.razor` n'a ni style ni
  script en ligne (ADR 0007).

## Listes de lecture

Chantier 3, décision 10 du [plan](plan.md).

- **Lire** (`PlaylistReader`) : `.m3u`, `.m3u8` et `.pls`. Chemins absolus ou
  relatifs au dossier de la liste, `\` accepté (listes faites sous Windows),
  `file:///…` lu comme un chemin. `#EXTINF:secondes,titre` (et `TitleN`,
  `LengthN` d'un `.pls`) donne le titre et la durée affichés jusqu'à ce que
  Webamp lise les étiquettes ; `-1` veut dire « durée inconnue ». Les adresses
  web sont laissées de côté (pas d'« Add URL », décision 9) et comptées ; un
  lecteur Windows (`C:\`), un partage (`\\serveur`) ou un chemin qui sort de `/`
  aussi. Encodage : une marque d'ordre des octets d'abord ; sinon `.m3u8` en
  UTF-8, `.m3u` et `.pls` en UTF-8 s'ils en sont, en Latin-1 sinon.
- **Écrire** (`PlaylistWriter`) : `.m3u8` en UTF-8 sans marque, `#EXTM3U`, puis
  `#EXTINF:secondes,titre` et le chemin **relatif** au dossier de la liste
  (`../` pour remonter, `PlaylistPaths.GetRelativePath`). Sans titre, le nom du
  fichier sans extension ; un saut de ligne dans un titre devient une espace.
  Relue, la liste redonne les mêmes morceaux, et elle marche encore quand on
  déplace le dossier qui la contient avec la musique.
- **Ouvrir une liste** (double-clic, « Ouvrir avec », `launch`) : comme un
  fichier audio (décision 5), ses morceaux s'ajoutent à la fin de la liste en
  cours et le premier joue (`WebampWindow.OpenFilesAsync`, sur
  `PlaylistFiles.ExpandAsync`). Un fichier manquant de la liste échoue à la
  lecture comme tout fichier ; la ligne d'état dit le premier fichier illisible,
  combien d'autres, et ce que la liste a laissé de côté.
- **« Load list »** : la boîte « Ouvrir » de l'OS (`dialogs.file-open`, filtre
  des listes), puis la liste **remplace** celle de Webamp, arrêtée, comme dans
  Winamp. Une liste sans aucun morceau lisible laisse la liste en cours telle
  quelle.
- **« Save list »** : la boîte « Enregistrer sous » de l'OS
  (`dialogs.file-save`, `.m3u8` par défaut, confirmation avant de remplacer),
  puis l'écriture par `KazoFile.WriteAllBytesAsync`
  (`filesystem.user-home.write`). Les morceaux glissés depuis l'ordinateur n'ont
  pas de fichier dans Kazo OS : ils ne sont pas écrits, et la ligne d'état le dit.
- Les menus de Webamp appellent le C# depuis JavaScript, hors des gestionnaires
  de la fenêtre : `WebampWindow` entre lui-même dans le contexte ambiant de l'app
  (`KazoExecutionContext.Enter`, ADR 0043 de Kazo OS) avant `KazoFile`, et
  montre toute erreur dans la ligne d'état.
- Les entrées du menu des fichiers (« Lire dans Webamp », « Ajouter à la liste
  de Webamp ») sont celles du chantier 2 ; elles passent par `OpenFilesAsync`,
  qui ouvre aussi les listes.

## État, titre, raccourcis, Milkdrop

Chantier 5, décisions 8, 12, 13 et 17 du [plan](plan.md).

**L'état** est dans `~/.config/ca.kazo.webamp/state.json`, le dossier de
réglages propre à l'app (ADR 0044 de Kazo OS : `filesystem.private.read` et
`.write`, déjà dans le manifeste), comme un programme Linux garde le sien dans
`~/.config/<programme>/`. Un JSON lisible :

```json
{
  "version": 1,
  "tracks": ["/home/neo/Music/a.mp3", "/mnt/nas/b.flac"],
  "currentTrack": 1,
  "player": {
    "position": 42.5, "volume": 63, "balance": 0, "shuffle": false, "repeat": true,
    "equalizer": { "on": true, "auto": false, "preamp": 50, "bands": [50, 50, 50, 50, 50, 50, 50, 50, 50, 50] },
    "doubleSize": false,
    "windows": { "absolute": true, "main": { "x": 0, "y": 0, "open": true, "shade": false, "extraWidth": 0, "extraHeight": 0 }, "…": "equalizer, playlist, milkdrop" }
  },
  "skin": "/home/neo/.local/share/webamp/skins/vert.wsz"
}
```

- **Lire** (`WebampStateFile.LoadAsync`, `WebampStateJson.Parse`) : un fichier
  absent, illisible, qui n'est pas du JSON ou d'une autre version donne un état
  vide (Webamp démarre comme la première fois) ; une valeur hors limites est
  ramenée dans ses limites (volume 0 à 100, balance -100 à 100, égaliseur 0 à
  100, dix bandes ou un égaliseur plat), un chemin que l'OS refuse est laissé de
  côté.
- **Remettre** : les réglages et la disposition partent avec la création de
  Webamp (`create`), donc dès la première image ; puis `WebampStateKeeper`
  remet le skin, ajoute la liste **sans jouer**, rend le morceau courant et le
  replace à sa position, en pause. Un fichier qui a disparu (lecteur réseau
  absent, fichier déplacé) est **sauté** et **reste dans la liste enregistrée**,
  à sa place, après le morceau qui le précédait (`PlaylistMemory`) : il revient
  au lancement suivant s'il est là. Si c'est le morceau courant, le suivant
  devient courant, depuis son début. Vider la liste oublie ces fichiers.
- **Écrire** : le module signale chaque changement (`OnStateChanged`) ;
  `StateSaveScheduler` écrit une seconde après le dernier, jamais deux fois en
  même temps, et une dernière fois à la fermeture : bouton fermer de Webamp,
  fermeture de la fenêtre de l'OS (`IWindowCloseGuard`, qui ne demande rien à
  l'utilisateur), arrêt du processus (`OnAppStopRequestedAsync`), et
  `DisposeAsync` en dernier recours. Une écriture qui échoue est dite une fois
  dans la ligne d'état ; la suivante réessaie. Ces écritures partent d'un rappel
  JavaScript ou d'une minuterie : la fenêtre entre dans le contexte ambiant de
  l'app (`KazoExecutionContext.Enter`) avant `KazoFile`.
- **Position** : lue sur l'élément audio de Webamp (0 à l'arrêt). Webamp
  repart du début quand on lance un morceau arrêté : la reprise le laisse donc
  **en pause** à la position enregistrée.
- **Skin** : `WebampStateKeeper.SkinPath` est enregistré et réappliqué au
  démarrage (`setSkin`) ; un skin disparu laisse le skin par défaut. Le
  chantier 4 (skins) le règle quand l'utilisateur choisit un skin.

**Le titre** de la fenêtre de l'OS suit le morceau en cours, qu'il joue ou soit
en pause : « Artiste - Titre - Webamp » (décision 8, comme le bouton de Winamp
dans la barre des tâches), avec le nom du fichier tant que Webamp n'a pas lu les
étiquettes, « Webamp » sans morceau (`TrackTitle`, `SetWindowTitleAsync`).
L'événement `onTrackDidChange` de Webamp ne sert pas : il ne nomme qu'un
morceau qui joue.

**Touches multimédia** : `enableMediaSession: true` (décision 8) ; Webamp
renseigne la Media Session du navigateur (titre, artiste, lecture, pause,
suivant, précédent).

**Raccourcis** (décision 17) : `enableHotkeys` de Webamp pose un écouteur
`keydown` sur toute la page, qui prendrait les touches des autres apps. Le
module crée Webamp avec cette option mais **capture** cet écouteur pendant la
construction (`constructCapturingHotkeys` : `document.addEventListener` est
remplacé le temps du constructeur, synchrone) et ne l'appelle que quand la
fenêtre de l'OS a le focus (`OnWindowActivated` / `OnWindowDeactivated` →
`setFocused`). On garde ainsi tous les raccourcis de Webamp 2.3.1 (X, C, V, B,
Z, R, S, L, flèches, Ctrl+D, Alt+W/E/G…) sans les recopier. Comme dans Webamp,
une touche tapée dans un champ (`input`, `textarea`, `select`) est ignorée.

**Milkdrop** (décision 12) : Webamp demande Butterchurn et ses préréglages dès
sa construction (`__butterchurnOptions.importButterchurn` et `getPresets`).
`lazyButterchurn` lui donne des promesses qui attendent que la fenêtre Milkdrop
s'ouvre pour la première fois (le module surveille
`windows.genWindows.milkdrop.open`) ; alors seulement il importe
`lib/butterchurn/butterchurn.min.js` et `lib/butterchurn-presets/base.min.js`.
Milkdrop reste dans les menus de Webamp. Une disposition enregistrée avec
Milkdrop ouvert le charge au démarrage.

## Tests

| Suite | Commande | Contenu |
| --- | --- | --- |
| C# (xUnit) | `dotnet test Kazo.Webamp.slnx -c Release` | Manifeste ; `TrackLibrary` : ajout dans l'ordre avec le type, ajout à la fin, fichier illisible, rien envoyé quand rien ne se lit, morceau sorti de la liste oublié, ordre de Webamp, remplacement, changements de liste pendant l'ajout, échec de Webamp ; types audio ; listes : lecture (chemins relatifs et absolus, `\`, `#EXTINF`, adresses web, lecteurs Windows, Latin-1, UTF-8 avec et sans marque, `.pls`), écriture (UTF-8, chemins relatifs, titres sur une ligne), aller-retour, ouverture d'une liste avec d'autres fichiers, liste illisible, titre et durée jusqu'à Webamp, « Load list », « Save list » ; types associés et capacités. ; état : aller-retour du JSON, fichier corrompu ou d'une autre version = état vide, valeurs ramenées dans leurs limites, chemins refusés, fichier absent, dossier créé ; fichiers disparus gardés à leur place et sautés, morceau courant suivi ; écritures regroupées, immédiates à la fermeture, jamais deux à la fois, échec signalé ; reprise de la liste, de la position et du skin ; titre de la fenêtre. |
| JavaScript | `npm test` | `webamp-host.js` avec un faux Webamp : options, adresses créées, gardées pendant l'arrivée d'un lot, rendues à la sortie de la liste, au remplacement et à `dispose`, rapports dédoublonnés, événements ; « Load list » et « Save list » (morceaux avec clé, titre, durée, morceaux sans clé comptés), durée d'une liste donnée à Webamp. ; Media Session, disposition et réglages remis avant la première image, état lu, changements signalés (l'horloge par pas de 5 s), reprise en pause à une position, raccourcis seulement avec le focus (écouteur de Webamp capturé), Milkdrop chargé à la première ouverture, skin par adresse `blob:`. |
| E2E (Playwright) | `npm run test:e2e` après le build Release | Webamp s'ouvre depuis le menu Applications dans sa fenêtre, sans « Add URL » ; un `.mp3` mis dans `~` par l'explorateur et ouvert par `launch ca.kazo.webamp /home/neo/tone.mp3` joue avec ses étiquettes, et le bouton fermer de Webamp ferme la fenêtre de l'OS ; un fichier absent est signalé ; « Save list » enregistre `mix.m3u8` par la boîte de l'OS, « New list » vide Webamp et « Load list » le remplit à nouveau ; un `.m3u` ouvert joue son morceau et signale le fichier manquant et l'adresse web. ; titre « Kazo Test - Tone 440 - Webamp » dans la barre des tâches, puis après un rechargement de la page Webamp revient avec sa liste, en pause, avec le même volume et repeat ; les raccourcis de Webamp ne font rien quand l'explorateur a le focus et agissent quand Webamp l'a ; Butterchurn n'est téléchargé qu'à l'ouverture de Milkdrop (`state.e2e.mjs`). |

Le `.mp3` de test (`tests/e2e/fixtures/tone.mp3`, 4 s, 16 Ko) est fabriqué par
`tests/e2e/fixtures/make-tone.sh` (ffmpeg) et validé dans le dépôt ;
`tests/e2e/fixtures/tones.m3u` liste ce morceau, un fichier absent et une adresse web.

## Avancement

| Chantier | Ce que ce document décrit |
| --- | --- |
| 1. `webamp/1-solution` | Solution, manifeste, fenêtre, `WebampPlayer`, `TrackLibrary`, module JavaScript. |
| 3. `webamp/3-playlists` | Listes de lecture : lire, écrire, ouvrir, « Load list », « Save list ». |
| 5. `webamp/5-state-and-media` | État retenu, titre de la fenêtre, Media Session, raccourcis avec le focus, Milkdrop paresseux. |
