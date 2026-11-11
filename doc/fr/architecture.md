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
├─ external/KazoOs/          # sous-module : Kazo OS sur le tag v3.1.0 (la version du dépôt)
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
| `WebampApp.cs` | Manifeste (`[AppManifest]`) : id `ca.kazo.webamp`, nom « Webamp », version `1.0.0`, éditeur `pub.kazo`, catégorie `multimedia`, **ordinateur seulement** (pas de `MobileEntryPoint`), `SingleInstancePerUser`. Fenêtre de 277×388 (le contenu : les trois fenêtres de Webamp empilées, 275×348, plus le cadre et la barre de titre de l'OS), redimensionnable, au plus petit la fenêtre principale seule. Capacités de base : `filesystem.user-home.read`, `filesystem.private.read` et `.write` ; le chantier 3 ajoute `filesystem.user-home.write`, `dialogs.file-open` et `dialogs.file-save` (« Load list », « Save list ») ; le chantier 2, `dialogs.folder-select` et `shell.context-menu`. Types associés : les listes du chantier 3 (`audio/x-mpegurl .m3u`, `application/vnd.apple.mpegurl .m3u8`, `audio/x-scpls .pls`) et les types audio du chantier 2, action `open` ; l'intent `ca.kazo.webamp.files.v1` et les deux entrées du menu des fichiers (chantier 2). Le chantier 4 ajoute les skins (ci-dessous), le chantier 5 aucune capacité (son `state.json` est dans le dossier privé). |
| `WebampWindow.razor` (+ `.cs`, `.css`) | La fenêtre : un élément où Webamp se dessine (`renderInto`) et une ligne d'état (démarrage, fichier illisible, ce qu'une liste a laissé de côté), qu'un bouton × ferme. Au premier rendu, elle démarre `WebampPlayer` et crée `TrackLibrary` ; ce qu'on lui ouvre (au lancement, par un intent, par le menu des fichiers ou par « Ouvrir » de Webamp) passe par `OpenQueue` puis `OpenAsync` (ci-dessous, « Ouvrir depuis l'OS »). Elle répond à « Load list » et « Save list » de Webamp par les dialogues de l'OS. Le bouton fermer de Webamp arrête le processus de l'app, ce qui ferme la fenêtre de l'OS et coupe le son. Le CSS cache « Add URL » (décision 9) et garde Webamp dans la fenêtre. Au démarrage, elle relit l'état (`state.json`) et le remet dans Webamp ; elle écrit l'état à chaque changement (regroupé) et une dernière fois à la fermeture (`IWindowCloseGuard`, arrêt du processus, `DisposeAsync`). Son titre suit le morceau en cours (`Player/TrackTitle`), et elle dit au module quand elle a le focus (raccourcis). |
| `Player/WebampPlayer.cs` | Le côté C# d'une instance de Webamp : importe `wwwroot/js/webamp-host.js`, lui passe l'élément, une référence .NET et les réglages enregistrés, envoie les morceaux, reçoit les événements (`Closed`, `Minimized`, `TrackChanged`, `PlaylistChanged`, `LoadListRequested`, `SaveListRequested`, `OpenRequested`, `StateChanged`). Implémente `ITrackPlayer` et `IStatePlayer` (lire l'état, reprendre un morceau à sa position) ; `SetFocusedAsync` règle les raccourcis. `DisposeAsync` arrête la musique et libère tout. |
| `Player/TrackTitle.cs` | Le titre de la fenêtre de l'OS : « Artiste - Titre - Webamp », « Titre - Webamp » sans artiste, le nom du fichier sans étiquettes, « Webamp » sans morceau (décision 8). |
| `State/` | L'état retenu (chantier 5, ci-dessous) : `WebampState` (le contenu de `state.json`), `WebampStateJson` (lecture tolérante, écriture), `WebampStateFile` (le fichier), `StateSaveScheduler` (écritures regroupées), `PlaylistMemory` (fichiers disparus gardés dans la liste), `WebampStateKeeper` (remettre l'état dans Webamp, le relire), `IStatePlayer`. |
| `Tracks/TrackLibrary.cs` | Les fichiers de la liste de Webamp (décision 14) : lus entiers par `KazoFile.ReadAllBytesAsync`, donnés à Webamp, oubliés quand ils sortent de la liste. API des chantiers 2 à 5 : `AddAsync(chemins, play)`, `ReplaceAsync(chemins, play)` (et leurs surcharges qui prennent des `PlaylistEntry`, avec titre et durée), `TryGetPath(clé)`, `Paths` (liste courante, dans l'ordre de Webamp), `LoadedBytes` (pour l'avertissement de 1 Go du chantier 2), `Changed`. Un fichier illisible est rendu dans `TrackLoadResult.Failures`, les autres sont ajoutés. |
| `Tracks/ITrackPlayer.cs`, `TrackData.cs`, `TrackLoadResult.cs` | Le contrat entre la bibliothèque et Webamp (un faux dans les tests) ; un morceau envoyé (clé, nom, type, octets, durée connue d'une liste) ; le résultat d'un ajout. |
| `Opening/` | Ouvrir depuis l'OS (chantier 2, ci-dessous) : `OpenRequest`, `OpenIntents`, `OpenQueue`, `AudioFileCollector`, `LoadedMemoryWarning`. |
| `Playlists/` | Les listes de lecture (chantier 3, ci-dessous) : `PlaylistReader`, `PlaylistWriter`, `PlaylistPaths`, `PlaylistFiles`, `PlaylistFormats`. |
| `Player/WebampListEntry.cs` | Un morceau de la liste de Webamp pour « Save list » (clé, titre, durée). |
| `Tracks/AudioMediaTypes.cs` | Extension → type audio (décision 4), donné au `Blob` pour que le navigateur choisisse le bon décodeur. |
| `Strings.cs`, `Resources/` | Textes en anglais et en français (ADR 0104 de Kazo OS), nom et description traduits du manifeste (`AppManifest.fr.resx`). |
| `WebampWindow.Skins.cs` | Les skins de la fenêtre (chantier 4, [Skins](#skins)) : ouvrir un `.wsz`, « Load Skin… », skin choisi retenu, skin refusé avec un message, notification d'explication. |
| `Skins/` | `SkinArchive` (un `.wsz` est-il un skin ?), `SkinLibrary` (le dossier `~/.local/share/webamp/skins/`), `SkinSettings` (`~/.config/ca.kazo.webamp/skins.json`), `SkinController` (le tout pour une fenêtre), `ISkinFileSystem` et `KazoSkinFileSystem` (les fichiers, en mémoire dans les tests), `ISkinPlayer` (ce que les skins demandent à Webamp). |
| `wwwroot/js/webamp-host.js` | Le module JavaScript (ci-dessous). |
| `wwwroot/js/webamp-skins.js` | Le pont du menu Skins de Webamp (voir [Skins](#skins)). |
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
                                                  OnLoadListRequested, OnSaveListRequested,
                                                  OnOpenRequested
```

| Appel | Sens | Contenu |
| --- | --- | --- |
| `create(élément, dotNet, { zIndex, skins, initialSkin, settings, focused })` | C# → JS | Charge `lib/webamp/webamp.bundle.min.mjs`, crée Webamp avec `enableHotkeys: true` et son écouteur de page capturé (décision 17), `enableMediaSession: true` (décision 8), un `handleAddUrlEvent` qui ne fait rien (décision 9), les skins du dossier de l'utilisateur (`availableSkins`) et le skin en cours (`initialSkin`) et, depuis `settings` (les `PlayerSettings` enregistrés, `null` au premier lancement), la disposition des fenêtres (`windowLayout`) et la taille double, Milkdrop paresseux (décision 12) ; remet le volume, la balance, l'égaliseur, shuffle et repeat et, si l'utilisateur avait déplacé les fenêtres, leurs places exactes, avant la première image ; le dessine dans l'élément. Rend un objet hôte (une `IJSObjectReference`). Un skin est `{ key, name, bytes }` ; `key` est son nom de fichier dans le dossier. |
| `addSkin(skin, apply)` | C# → JS | Ajoute un skin au menu Skins (ou remplace celui de même clé, dont l'adresse est rendue) ; avec `apply`, le met et attend que Webamp l'ait dessiné. |
| `addTracks(morceaux, play)` | C# → JS | Ajoute à la fin de la liste ; avec `play`, le premier ajouté joue. Un morceau est `{ key, name, mimeType, bytes, durationSeconds }` ; `bytes` est le `byte[]` du C#, reçu en `Uint8Array` ; `durationSeconds` (d'une liste, sinon `null`) est montrée jusqu'à ce que Webamp lise le fichier. |
| `replaceTracks(morceaux, play)` | C# → JS | Remplace la liste (`setTracksToPlay`) ; sans `play`, Webamp est arrêté. |
| `dispose()` | C# → JS | Arrête la musique, retire Webamp, rend toutes les adresses. |
| `OnPlaylistChanged(clés)` | JS → C# | Les clés encore dans la liste, dans son ordre, à chaque changement réel (pas à chaque tic de l'horloge de Webamp). |
| `OnTrackChanged({ artist, title, name } \| null)` | JS → C# | Le morceau en cours, qu'il joue ou soit en pause, à chaque changement de morceau ou d'étiquettes (`name` : le nom du fichier) ; `null` sans morceau. Pour le titre de la fenêtre. |
| `readState()` | C# → JS | Ce que Webamp tient (`PlayerSnapshot`) : la clé du morceau en cours, la position (0 à l'arrêt), le volume, la balance, shuffle, repeat, l'égaliseur, la taille double, les quatre fenêtres (place, ouverte, enroulée, taille en plus) et si leurs places sont fixées par l'utilisateur. |
| `resume(index, position)` | C# → JS | Rend un morceau courant sans le jouer ; avec une position, attend qu'il soit chargé, s'y place et met Webamp en pause (Play repart de là). |
| `setFocused(focus)` | C# → JS | Les raccourcis de Webamp agissent seulement quand la fenêtre a le focus. |
| `OnStateChanged()` | JS → C# | Un élément de l'état a changé (pas l'horloge : la position compte par pas de 5 s pendant la lecture). |
| `OnWebampClosed()`, `OnWebampMinimized()` | JS → C# | Les boutons fermer et réduire de Webamp. |
| `OnLoadListRequested()` | JS → C# | « Load list » (`handleLoadListEvent`, qui rend `null` à Webamp : le C# remplace la liste lui-même). |
| `OnOpenRequested(type)` | JS → C# | Un sélecteur de fichiers de Webamp intercepté (`open`, `add-files`, `add-folder`) : le C# montre la boîte de l'OS (« Ouvrir depuis l'OS »). |
| `OnSaveListRequested(morceaux, nonEnregistrés)` | JS → C# | « Save list » (`handleSaveListEvent`) : les morceaux qui ont une clé, `{ key, title, duration }` dans l'ordre (titre « Artiste - Titre » des étiquettes, sinon le nom affiché), et le nombre de morceaux sans clé (glissés depuis l'ordinateur). |
| `OnSkinFileChosen(nom, octets)` | JS → C# | Un skin de l'ordinateur (« Skins ▸ Load Skin… » ou déposé sur Webamp), pas encore mis. |
| `OnSkinChosen(clé \| null)` | JS → C# | Un skin choisi dans le menu Skins (Webamp le met lui-même) ; `null` pour « &lt;Base Skin&gt; ». |

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
- **Réduire** (bouton de Webamp) : `WebampPlayer.Minimized` → la fenêtre appelle
  `MinimizeWindowAsync` de `WindowAppBase`, que Kazo OS 3.1.0 apporte (issue
  [Os#384](https://github.com/Kazo-ca/Os/issues/384), ADR 0234 de Kazo OS ; pas
  de capacité, la demande ne touche que sa propre fenêtre). La musique continue ;
  le bouton de la barre des tâches rend la fenêtre. Avec le SDK 3.0.0 le bouton
  ne faisait rien (réponse A de Benoit, 2026-11-06) ; branché au chantier 7.

## Webamp dans une fenêtre de l'OS

- Webamp centre ses trois fenêtres dans l'élément ; on peut les déplacer à
  l'intérieur, comme sur une page.
- Une première pression sur une fenêtre de Webamp qui n'est pas au premier plan
  la sélectionne seulement (Webamp la passe devant), la suivante agit : c'est le
  fonctionnement de Webamp, les tests E2E en tiennent compte.
- Webamp ajoute ses styles dans la page lui-même ; le `.razor` n'a ni style ni
  script en ligne (ADR 0007).

## Ouvrir depuis l'OS

Chantier 2, décisions 4, 5, 14 et 16 du [plan](plan.md).

- **Types audio** (décision 4) : `audio/mpeg .mp3`, `audio/wav .wav`,
  `audio/ogg .ogg .oga`, `audio/opus .opus`, `audio/flac .flac`,
  `audio/mp4 .m4a`, `audio/aac .aac`, `audio/webm .weba`, action `open` :
  Webamp devient l'app des fichiers audio (double-clic, « Ouvrir avec »).
- **Une seule fenêtre** (décision 5) : `SingleInstancePerUser`. Un fichier
  ouvert quand Webamp tourne arrive dans `OnIntentReceivedAsync` (intent
  `launch` avec le chemin en argument) ; au lancement, dans `LaunchIntent` et
  `LaunchArguments`. `OpenIntents.Read` traduit l'un ou l'autre en
  `OpenRequest(chemins, mode)` ; mode `Play` (ajouter à la fin et jouer le
  premier), `Enqueue` (ajouter sans toucher à la lecture) ou `Replace`.
- **Une sélection multiple** : l'explorateur ouvre les fichiers un par un (le
  premier lance l'app, les autres arrivent comme des intents). `OpenQueue`
  rassemble ce qui arrive en 300 ms et fusionne les demandes voisines du même
  mode (`OpenQueue.Merge`) : la sélection s'ajoute en entier, dans l'ordre, et
  son premier fichier joue. Les demandes sont traitées une par une ; une erreur
  va dans la ligne d'état et n'arrête pas les suivantes.
- **Le menu des fichiers** : `[RegisterFileContextMenu]` déclare « Lire dans
  Webamp » (`play`, après « Ouvrir ») et « Ajouter à la liste de Webamp »
  (`enqueue`), sur les fichiers audio, les listes (`.m3u .m3u8 .pls`) et les
  dossiers (`inode/directory`), sélection multiple permise
  (`shell.context-menu`). Les deux envoient l'intent `ca.kazo.webamp.files.v1`
  (déclaré dans `Intents`) ; `KazoFileMenuIntent.ReadPaths` et `ReadEntry`
  donnent les chemins et l'entrée choisie. Titres dans `AppManifest.resx` et
  `AppManifest.fr.resx`.
- **Un dossier** (décision 16) : `AudioFileCollector` le remplace par ses
  fichiers audio et ceux de ses sous-dossiers
  (`KazoDirectory.GetFilesAsync(…, AllDirectories)`), triés par chemin segment
  par segment, sans tenir compte de la casse (`Album/` avant `album 2/`). Les
  listes trouvées dans un dossier sont laissées de côté ; une liste choisie
  elle-même est ouverte (`PlaylistFiles.ExpandAsync`, chantier 3). Un dossier
  illisible est signalé dans la ligne d'état ; un dossier sans fichier audio
  aussi (« … n'a aucun fichier audio que Webamp peut lire »).
- **« Ouvrir » et « Ajouter » de Webamp** (décision 16) : Webamp demande
  toujours ses fichiers par un `<input type=file>` jamais attaché, sur lequel il
  appelle `click()` pendant le clic de l'utilisateur. `FilePromptInterceptor`
  (`webamp-host.js`) écoute en capture les clics et les touches sur Webamp et
  son menu contextuel (`#webamp-context-menu`), et remplace une fois par page
  `HTMLInputElement.prototype.click` : pendant ce geste, un sélecteur de
  musique (pas `.wsz`/`.zip` ni `.eqf`) n'ouvre pas la boîte du navigateur mais
  appelle `OnOpenRequested("open" | "add-files" | "add-folder")`. Le C# montre
  la boîte de l'OS : « Ouvrir dans Webamp » (bouton éjecter, Play ▸ File…) et
  « Ajouter des fichiers à Webamp » (ADD ▸ Add file) par `dialogs.file-open`,
  sélection multiple, filtre « Musique et listes de lecture » ;
  « Ajouter un dossier » (ADD ▸ Add dir) par `dialogs.folder-select`.
  Éjecter **remplace** la liste et joue, comme dans Winamp ; les deux autres
  ajoutent à la fin. Les boîtes s'ouvrent dans le dernier dossier choisi, sinon
  `~/Music`, sinon `~`.
- **Avertissement de 1 Go** (décision 14) : après chaque ajout,
  `LoadedMemoryWarning` compare `TrackLibrary.LoadedBytes` à 1 Go ; au-delà, une
  boîte de l'OS (`MessageBox`, avertissement) le dit une fois, et revient
  seulement après être repassé sous la limite.

**Contournement de Kazo OS 3.0.0.** Sans `InitialDirectory`, les boîtes
`dialogs.file-open` et `dialogs.folder-select` s'ouvrent sur
`/home/<id de l'utilisateur>`, qui n'existe pas, et font tomber l'OS. L'app
donne donc toujours un dossier de départ.

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

## Skins

Décisions 11 et 20 du [plan](plan.md).

- **Le dossier.** Chaque skin ouvert ou chargé est copié dans
  `~/.local/share/webamp/skins/` (capacité `filesystem.user-home.write`), sous
  son propre nom de fichier, rendu sûr (sans chemin ni caractère de contrôle)
  et terminé par `.wsz` (un `.zip` devient un `.wsz`). Un skin du même nom
  remplace l'ancien. Au démarrage, `SkinLibrary` relit le dossier : chaque
  `.wsz` valide devient une entrée du menu Skins de Webamp, triée par nom ; un
  fichier illisible ou qui n'est pas un skin est laissé là et pas listé.
- **Un skin valide.** `SkinArchive` vérifie en C#, avant toute copie et avant
  Webamp : une archive ZIP d'au plus 16 Mo qui contient `main.bmp` (à la
  racine ou dans un dossier, casse ignorée). Sinon une boîte de message
  (« Bento.wsz n'a pas été chargé : … Le skin en cours reste. ») et rien n'est
  envoyé à Webamp. Webamp, lui, ne sait signaler un skin cassé que par un
  `alert` du navigateur.
- **Ouvrir un `.wsz` dans l'OS** (double-clic, « Ouvrir avec », `webamp
  skin.wsz`) : type associé `application/x-winamp-skin` `.wsz` ; au lancement
  ou dans la fenêtre déjà ouverte (par `OpenQueue`, comme la musique, une
  fenêtre par utilisateur), le skin est copié, ajouté au menu et mis. Un `.wsz`
  qui est déjà dans le dossier est seulement mis.
- **« Skins ▸ Load Skin… » de Webamp.** Webamp 2.3.1 n'a pas d'option pour ce
  menu : il ouvre son propre `input type=file` et applique le fichier sans le
  rendre. `webamp-skins.js` écoute les clics de la page en phase de capture
  (avant Webamp) et reconnaît l'entrée par sa place dans le menu de Webamp
  (`#webamp-context-menu`, sous-menu dont le texte est « Skins » : « Load
  Skin... », un séparateur, « &lt;Base Skin&gt; », puis les skins dans l'ordre
  donné). Pour « Load Skin… », il arrête le clic, ferme le menu, ouvre le
  sélecteur de fichiers du navigateur (fichier de l'ordinateur, décision 11) et
  envoie le fichier au C#, qui le vérifie, le copie et le met. Un seul `.wsz`
  ou `.zip` déposé sur Webamp suit le même chemin.
- **Skin retenu.** Un skin choisi dans le menu est mis par Webamp lui-même ; le
  module rapporte lequel (`OnSkinChosen`, par sa place dans le menu) et
  `SkinSettings` l'écrit dans `~/.config/ca.kazo.webamp/skins.json`
  (`{ "current": "Bento.wsz", "importTipShown": true }`). Au lancement suivant,
  Webamp démarre avec ce skin (`initialSkin`) ; un skin retenu qui n'est plus
  dans le dossier redonne le skin de base. Le chantier 5 l'a laissé là :
  `state.json` ne contient pas le skin, qui n'a ainsi qu'une place.
- **Explication de l'import** (décision 20). Au premier lancement, une
  notification (`notifications.post`) : « Pour changer de skin : double-cliquez
  un fichier .wsz, ou clic droit ▸ Skins ▸ Load Skin… ». `importTipShown` passe
  à `true` une fois la notification postée ; si elle est refusée, elle sera
  tentée au lancement suivant. Même texte dans le README.
- **Mémoire.** Les skins sont donnés à Webamp en adresses `blob:` créées par le
  module et rendues quand un skin est remplacé et à `dispose`.

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
  }
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
  ajoute la liste **sans jouer** (avant les fichiers sur lesquels Webamp a été ouvert, que la file d'ouverture ajoute une fois l'état remis), rend le morceau courant et le
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
- **Skin** : pas dans `state.json`. Le skin en cours reste dans `skins.json`
  ([Skins](#skins)), que Webamp lit à sa création (`initialSkin`) : un seul
  fichier pour le skin, pas de copie à tenir à jour.

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

**Place de Milkdrop** (chantier 6) : Webamp 2.3.1 laisse la fenêtre Milkdrop en
0,0, par-dessus la fenêtre principale. Quand elle s'ouvre et recouvre une autre
fenêtre de Webamp, `milkdropPlacement` la déplace, comme Winamp : à droite des
autres fenêtres si la fenêtre de l'OS a la place, sinon en dessous, sinon à la
place de la liste de lecture (sous le lecteur et l'égaliseur), pour qu'elle
reste visible sans cacher les boutons du lecteur. La place visible est mesurée
dans la page (`_visibleSurface`), car Webamp centre ses fenêtres dans l'élément
et son `browserWindowSize` ne correspond pas à l'élément. Une place enregistrée
est gardée.

## Fiche du Kazo OS Store et app installée

Le dossier `store/` est la fiche du Kazo OS Store (format du catalogue :
`doc/fr/catalog-format.md` de Kazo-ca/Os-AppMarketPlace) : `icon.png` (256×256,
rendu de `wwwroot/icons/app.svg`), `short_description.{fr,en}.txt`,
`description.{fr,en}.md` (fonctions, explication de l'import des skins de la
décision 20, limites de la première version, licences et note du skin par
défaut de Winamp 2.91, dessin de Nullsoft, pas sous MIT), `screenshots/1.png` à
`3.png` (1280×800 : menu des fichiers et Webamp qui joue, liste et égaliseur,
Milkdrop) et `store.json` (catégorie `multimedia`). Les captures sont prises
dans l'hôte par `npm run store:screenshots` (`tests/store/screenshots.mjs`),
avec les morceaux de test du dépôt. L'outil du catalogue
(`Kazo.Os.MarketPlace.Tool validate` et `check-package`) accepte le dossier
assemblé comme le fait `publish-to-kazoos-store.yml` : « the package matches
its folder », 630 802 octets.

Comme pour Chess, `npm run test:installed -- <wwwroot de Kazo OS> <dossier des
paquets>` (`tests/installed/install-and-play.mjs`) vérifie Webamp tel que le
Store l'installe, dans l'OS publié et rogné : `sudo kazopkg install` (contrôle
des API compris), double-clic sur un `.mp3` dans l'explorateur (types associés
de l'app installée), lecture, CSS isolé lié, Milkdrop avec Butterchurn venu du
paquet, aucune erreur dans la console. Il a trouvé deux choses :

1. `kazopkg` refusait Webamp : `JsonSerializerOptions.set_NumberHandling`
   n'existe plus dans l'OS rogné, et le JSON source-généré de `state.json`
   l'appelait parce que `NumberHandling = Strict` était écrit dans
   `JsonSourceGenerationOptions`. C'est la valeur par défaut : la ligne est
   retirée.
2. Kazo OS 3.0.0 donnait les capacités déclarées d'une app à la connexion : une
   app installée pendant la session n'en avait aucune avant la connexion
   suivante. Issue [Kazo-ca/Os#386](https://github.com/Kazo-ca/Os/issues/386),
   corrigée dans Kazo OS 3.1.0 ; le test se reconnecte encore après
   l'installation (vérifié sur 3.0.0, pas relancé sur 3.1.0).

Plus tard (version 1.1.1, 2026-11-09), un contrôle exact des signatures, fait
pour IRC, a trouvé dans Webamp 1.1.0 un appel à `String.Split(string[],
StringSplitOptions)` (lecture des listes `.m3u`/`.pls`) absent de l'OS rogné.
Le contrôle de `kazopkg` ne comparait que le nombre de paramètres et l'avait
laissé passer : ouvrir une liste de lecture aurait arrêté la fenêtre. Les
lignes sont maintenant découpées par `Replace` puis `Split(char)`, et Kazo OS
garde CoreLib entière ([ADR 0242](https://github.com/Kazo-ca/Os/blob/main/doc/fr/wasm/dev/adr/0242-framework-api-for-installed-apps.md)).

Vérifié le 2026-11-06 avec Kazo OS 3.0.0 (sous-module `v3.0.0`, publication
Release de `OS/Kazo.Os.Ecosystem`) et le paquet de `KazoOsPack` (14 fichiers,
non signé : `KOSAPP0063`). Le tag `v1.0.0` et la PR au catalogue viennent après
la fusion dans `main`, par le workflow « Tag a release ».

## Lecteurs réseau

Un lecteur réseau est un dossier de l'ordinateur monté dans `/mnt/<nom>`
(ADR 0181 de Kazo OS, Chrome et Edge sur ordinateur). Depuis Kazo OS 3.1.0
(ADR 0231, [Os#381](https://github.com/Kazo-ca/Os/issues/381)), les lecteurs de
l'utilisateur comptent comme ses fichiers personnels : la capacité
`filesystem.user-home.read` de Webamp suffit, et le texte de l'installation dit
« vos fichiers, y compris vos lecteurs réseau ». Webamp n'a donc **rien de
particulier** pour `/mnt` : un morceau, une liste (chemins relatifs lus depuis
son dossier) ou un dossier s'ouvrent par `KazoFile`/`KazoDirectory` comme dans
`~`. Le lecteur d'un autre utilisateur et `/mnt` lui-même restent refusés par
l'OS. Le manifeste demande `sdkCompatibility` 3.1.0 (écrit par le générateur à
partir du sous-module) : sur un OS plus ancien, un fichier de `/mnt` donnait
« Permission refusée ».

`tests/e2e/network-drive.e2e.mjs` monte un dossier privé de l'origine
(`navigator.storage.getDirectory()`) par le crochet de test de l'OS
`globalThis.__kazoOsPickHostFolder` et `mount -t hostfs /mnt/music`, puis ouvre
par `launch` un `.mp3`, une liste `.m3u` et un dossier du lecteur.

## Tests

| Suite | Commande | Contenu |
| --- | --- | --- |
| C# (xUnit) | `dotnet test Kazo.Webamp.slnx -c Release` | Manifeste ; skins : `SkinArchive` (archive, `main.bmp` en casse et dossier quelconques, pas une archive, sans `main.bmp`, trop gros), `SkinLibrary` (dossier, liste triée et fichiers ignorés, copie, skin déjà dans le dossier, remplacement, refus sans copie, nom rendu sûr), `SkinController` (skin ouvert mis et retenu, skin de l'ordinateur, skin cassé refusé et skin en cours gardé, choix du menu et skin de base, skin retenu disparu, réglages cassés, notification une seule fois et de nouveau après un échec) ; `TrackLibrary` : ajout dans l'ordre avec le type, ajout à la fin, fichier illisible, rien envoyé quand rien ne se lit, morceau sorti de la liste oublié, ordre de Webamp, remplacement, changements de liste pendant l'ajout, échec de Webamp ; types audio ; listes : lecture (chemins relatifs et absolus, `\`, `#EXTINF`, adresses web, lecteurs Windows, Latin-1, UTF-8 avec et sans marque, `.pls`), écriture (UTF-8, chemins relatifs, titres sur une ligne), aller-retour, ouverture d'une liste avec d'autres fichiers, liste illisible, titre et durée jusqu'à Webamp, « Load list », « Save list » ; types associés et capacités; chantier 2 : intents (fichier, menu des fichiers, `launch`), dossiers récursifs triés par chemin, dossier illisible, file d'ouverture (attente, fusion d'une rafale, ordre, erreur), avertissement de 1 Go (une fois, réarmé sous la limite), entrées du menu dans `manifest.json` ; état : aller-retour du JSON, fichier corrompu ou d'une autre version = état vide, valeurs ramenées dans leurs limites, chemins refusés, fichier absent, dossier créé ; fichiers disparus gardés à leur place et sautés, morceau courant suivi ; écritures regroupées, immédiates à la fermeture, jamais deux à la fois, échec signalé ; reprise de la liste et de la position ; titre de la fenêtre. |
| JavaScript | `npm test` | `webamp-host.js` avec un faux Webamp : options, adresses créées, gardées pendant l'arrivée d'un lot, rendues à la sortie de la liste, au remplacement et à `dispose`, rapports dédoublonnés, événements ; « Load list » et « Save list » (morceaux avec clé, titre, durée, morceaux sans clé comptés), durée d'une liste donnée à Webamp ; skins au démarrage (`availableSkins`, `initialSkin`), ajout trié et mis, remplacement, adresses rendues. `webamp-skins.js` avec un faux menu : entrées reconnues par leur place, « Load Skin… » pris à Webamp, annulation, choix rapportés, dépôt d'un skin; `FilePromptInterceptor` : sélecteur de musique intercepté pendant un geste sur Webamp, pas hors de Webamp ni pour un skin ou un préréglage, posé une fois par page et retiré avec la dernière fenêtre ; Media Session, disposition et réglages remis avant la première image, état lu, changements signalés (l'horloge par pas de 5 s), reprise en pause à une position, raccourcis seulement avec le focus (écouteur de Webamp capturé), Milkdrop chargé à la première ouverture ; place de Milkdrop (`windowBox`, à droite, en dessous, à la place de la liste, place gardée). |
| E2E (Playwright) | `npm run test:e2e` après le build Release | Webamp s'ouvre depuis le menu Applications dans sa fenêtre, sans « Add URL » ; un `.mp3` mis dans `~` par l'explorateur et ouvert par `launch ca.kazo.webamp /home/neo/tone.mp3` joue avec ses étiquettes, et le bouton fermer de Webamp ferme la fenêtre de l'OS ; un fichier absent est signalé ; « Save list » enregistre `mix.m3u8` par la boîte de l'OS, « New list » vide Webamp et « Load list » le remplit à nouveau ; un `.m3u` ouvert joue son morceau et signale le fichier manquant et l'adresse web. Skins (`skins.e2e.mjs`, skins fabriqués par `fixtures/make-skin.mjs`) : la notification au premier lancement, un `.wsz` ouvert par `launch` dans Webamp déjà ouvert est mis et listé dans le menu Skins ; « Load Skin… » met un skin et le garde, un skin cassé est refusé par une boîte de message et le skin reste, Webamp rouvert démarre avec le skin et sans nouvelle notification; chantier 2 (`open-files.e2e.mjs`) : un double-clic sur un `.mp3` de l'explorateur le joue, deux fichiers ouverts ensemble s'ajoutent dans l'ordre et le premier joue, dans la même fenêtre ; « Ajouter à la liste de Webamp » sur un dossier ajoute ses morceaux triés sans jouer, « Lire dans Webamp » joue ; éjecter et ADD ▸ Add file montrent la boîte de l'OS, jamais celle du navigateur ; titre « Kazo Test - Tone 440 - Webamp » dans la barre des tâches, puis après un rechargement de la page Webamp revient avec sa liste, en pause, avec le même volume et repeat ; les raccourcis de Webamp ne font rien quand l'explorateur a le focus et agissent quand Webamp l'a ; Butterchurn n'est téléchargé qu'à l'ouverture de Milkdrop, qui ne recouvre pas la fenêtre principale (`state.e2e.mjs`) ; chantier 7 (`network-drive.e2e.mjs`) : un `.mp3`, une liste et un dossier d'un lecteur réseau monté jouent, le bouton réduire de Webamp réduit la fenêtre de l'OS sans arrêter la musique et la barre des tâches la rend. |
| App installée | `npm run test:installed -- <wwwroot> <paquets>` | Voir « Fiche du Kazo OS Store et app installée ». Pas dans la CI (publication de l'OS d'une dizaine de minutes). |

Les `.mp3` de test (`tests/e2e/fixtures/tone.mp3` et `tone-220.mp3`, 4 s, 16 Ko) sont fabriqués par
`tests/e2e/fixtures/make-tone.sh` (ffmpeg) et validé dans le dépôt ;
`tests/e2e/fixtures/tones.m3u` liste ce morceau, un fichier absent et une adresse web.

## Avancement

| Chantier | Ce que ce document décrit |
| --- | --- |
| 1. `webamp/1-solution` | Solution, manifeste, fenêtre, `WebampPlayer`, `TrackLibrary`, module JavaScript. |
| 2. `webamp/2-open-files` | Ouvrir depuis l'OS : types audio, une seule fenêtre, file d'ouverture, menu des fichiers, dossiers, boîtes de l'OS pour « Ouvrir » et « Ajouter », avertissement de 1 Go. |
| 3. `webamp/3-playlists` | Listes de lecture : lire, écrire, ouvrir, « Load list », « Save list ». |
| 4. `webamp/4-skins` | Skins : type `.wsz`, dossier des skins, « Load Skin… », skin retenu, explication de l'import. |
| 5. `webamp/5-state-and-media` | État retenu, titre de la fenêtre, Media Session, raccourcis avec le focus, Milkdrop paresseux. |
| 6. `webamp/6-store` | Fiche du Kazo OS Store, captures, app installée par `kazopkg` vérifiée, place de Milkdrop. |
| 7. `webamp/7-network-drive` | Kazo OS 3.1.0, lecteurs réseau, bouton réduire de Webamp. |
