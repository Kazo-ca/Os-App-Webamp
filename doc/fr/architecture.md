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
| `WebampApp.cs` | Manifeste (`[AppManifest]`) : id `ca.kazo.webamp`, nom « Webamp », version `1.0.0`, éditeur `pub.kazo`, catégorie `multimedia`, **ordinateur seulement** (pas de `MobileEntryPoint`), `SingleInstancePerUser`. Fenêtre de 277×388 (le contenu : les trois fenêtres de Webamp empilées, 275×348, plus le cadre et la barre de titre de l'OS), redimensionnable, au plus petit la fenêtre principale seule. Capacités de base : `filesystem.user-home.read`, `filesystem.private.read` et `.write` ; le chantier 3 ajoute `filesystem.user-home.write`, `dialogs.file-open` et `dialogs.file-save` (« Load list », « Save list ») ; le chantier 2, `dialogs.folder-select` et `shell.context-menu`. Types associés : les listes du chantier 3 (`audio/x-mpegurl .m3u`, `application/vnd.apple.mpegurl .m3u8`, `audio/x-scpls .pls`) et les types audio du chantier 2, action `open` ; l'intent `ca.kazo.webamp.files.v1` et les deux entrées du menu des fichiers (chantier 2). Les chantiers 4 et 5 ajoutent les leurs. |
| `WebampWindow.razor` (+ `.cs`, `.css`) | La fenêtre : un élément où Webamp se dessine (`renderInto`) et une ligne d'état (démarrage, fichier illisible, ce qu'une liste a laissé de côté), qu'un bouton × ferme. Au premier rendu, elle démarre `WebampPlayer` et crée `TrackLibrary` ; ce qu'on lui ouvre (au lancement, par un intent, par le menu des fichiers ou par « Ouvrir » de Webamp) passe par `OpenQueue` puis `OpenAsync` (ci-dessous, « Ouvrir depuis l'OS »). Elle répond à « Load list » et « Save list » de Webamp par les dialogues de l'OS. Le bouton fermer de Webamp arrête le processus de l'app, ce qui ferme la fenêtre de l'OS et coupe le son. Le CSS cache « Add URL » (décision 9) et garde Webamp dans la fenêtre. |
| `Player/WebampPlayer.cs` | Le côté C# d'une instance de Webamp : importe `wwwroot/js/webamp-host.js`, lui passe l'élément et une référence .NET, envoie les morceaux, reçoit les événements (`Closed`, `Minimized`, `TrackChanged`, `PlaylistChanged`, `LoadListRequested`, `SaveListRequested`, `OpenRequested`). Implémente `ITrackPlayer`. `DisposeAsync` arrête la musique et libère tout. |
| `Tracks/TrackLibrary.cs` | Les fichiers de la liste de Webamp (décision 14) : lus entiers par `KazoFile.ReadAllBytesAsync`, donnés à Webamp, oubliés quand ils sortent de la liste. API des chantiers 2 à 5 : `AddAsync(chemins, play)`, `ReplaceAsync(chemins, play)` (et leurs surcharges qui prennent des `PlaylistEntry`, avec titre et durée), `TryGetPath(clé)`, `Paths` (liste courante, dans l'ordre de Webamp), `LoadedBytes` (pour l'avertissement de 1 Go du chantier 2), `Changed`. Un fichier illisible est rendu dans `TrackLoadResult.Failures`, les autres sont ajoutés. |
| `Tracks/ITrackPlayer.cs`, `TrackData.cs`, `TrackLoadResult.cs` | Le contrat entre la bibliothèque et Webamp (un faux dans les tests) ; un morceau envoyé (clé, nom, type, octets, durée connue d'une liste) ; le résultat d'un ajout. |
| `Opening/` | Ouvrir depuis l'OS (chantier 2, ci-dessous) : `OpenRequest`, `OpenIntents`, `OpenQueue`, `AudioFileCollector`, `LoadedMemoryWarning`. |
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
                                                  OnLoadListRequested, OnSaveListRequested,
                                                  OnOpenRequested
```

| Appel | Sens | Contenu |
| --- | --- | --- |
| `create(élément, dotNet, { zIndex })` | C# → JS | Charge `lib/webamp/webamp.bundle.min.mjs`, crée Webamp avec `enableHotkeys: false` (décision 17) et un `handleAddUrlEvent` qui ne fait rien (décision 9), le dessine dans l'élément. Rend un objet hôte (une `IJSObjectReference`). |
| `addTracks(morceaux, play)` | C# → JS | Ajoute à la fin de la liste ; avec `play`, le premier ajouté joue. Un morceau est `{ key, name, mimeType, bytes, durationSeconds }` ; `bytes` est le `byte[]` du C#, reçu en `Uint8Array` ; `durationSeconds` (d'une liste, sinon `null`) est montrée jusqu'à ce que Webamp lise le fichier. |
| `replaceTracks(morceaux, play)` | C# → JS | Remplace la liste (`setTracksToPlay`) ; sans `play`, Webamp est arrêté. |
| `dispose()` | C# → JS | Arrête la musique, retire Webamp, rend toutes les adresses. |
| `OnPlaylistChanged(clés)` | JS → C# | Les clés encore dans la liste, dans son ordre, à chaque changement réel (pas à chaque tic de l'horloge de Webamp). |
| `OnTrackChanged({ artist, title } \| null)` | JS → C# | Le morceau qui commence (pour le titre de la fenêtre du chantier 5). |
| `OnWebampClosed()`, `OnWebampMinimized()` | JS → C# | Les boutons fermer et réduire de Webamp. |
| `OnLoadListRequested()` | JS → C# | « Load list » (`handleLoadListEvent`, qui rend `null` à Webamp : le C# remplace la liste lui-même). |
| `OnOpenRequested(type)` | JS → C# | Un sélecteur de fichiers de Webamp intercepté (`open`, `add-files`, `add-folder`) : le C# montre la boîte de l'OS (« Ouvrir depuis l'OS »). |
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
chantier 5 le charge à la première ouverture de Milkdrop par
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

## Tests

| Suite | Commande | Contenu |
| --- | --- | --- |
| C# (xUnit) | `dotnet test Kazo.Webamp.slnx -c Release` | Manifeste ; `TrackLibrary` : ajout dans l'ordre avec le type, ajout à la fin, fichier illisible, rien envoyé quand rien ne se lit, morceau sorti de la liste oublié, ordre de Webamp, remplacement, changements de liste pendant l'ajout, échec de Webamp ; types audio ; listes : lecture (chemins relatifs et absolus, `\`, `#EXTINF`, adresses web, lecteurs Windows, Latin-1, UTF-8 avec et sans marque, `.pls`), écriture (UTF-8, chemins relatifs, titres sur une ligne), aller-retour, ouverture d'une liste avec d'autres fichiers, liste illisible, titre et durée jusqu'à Webamp, « Load list », « Save list » ; types associés et capacités ; chantier 2 : intents (fichier, menu des fichiers, `launch`), dossiers récursifs triés par chemin, dossier illisible, file d'ouverture (attente, fusion d'une rafale, ordre, erreur), avertissement de 1 Go (une fois, réarmé sous la limite), entrées du menu dans `manifest.json`. |
| JavaScript | `npm test` | `webamp-host.js` avec un faux Webamp : options, adresses créées, gardées pendant l'arrivée d'un lot, rendues à la sortie de la liste, au remplacement et à `dispose`, rapports dédoublonnés, événements ; « Load list » et « Save list » (morceaux avec clé, titre, durée, morceaux sans clé comptés), durée d'une liste donnée à Webamp ; `FilePromptInterceptor` : sélecteur de musique intercepté pendant un geste sur Webamp, pas hors de Webamp ni pour un skin ou un préréglage, posé une fois par page et retiré avec la dernière fenêtre. |
| E2E (Playwright) | `npm run test:e2e` après le build Release | Webamp s'ouvre depuis le menu Applications dans sa fenêtre, sans « Add URL » ; un `.mp3` mis dans `~` par l'explorateur et ouvert par `launch ca.kazo.webamp /home/neo/tone.mp3` joue avec ses étiquettes, et le bouton fermer de Webamp ferme la fenêtre de l'OS ; un fichier absent est signalé ; « Save list » enregistre `mix.m3u8` par la boîte de l'OS, « New list » vide Webamp et « Load list » le remplit à nouveau ; un `.m3u` ouvert joue son morceau et signale le fichier manquant et l'adresse web ; chantier 2 (`open-files.e2e.mjs`) : un double-clic sur un `.mp3` de l'explorateur le joue, deux fichiers ouverts ensemble s'ajoutent dans l'ordre et le premier joue, dans la même fenêtre ; « Ajouter à la liste de Webamp » sur un dossier ajoute ses morceaux triés sans jouer, « Lire dans Webamp » joue ; éjecter et ADD ▸ Add file montrent la boîte de l'OS, jamais celle du navigateur. |

Les `.mp3` de test (`tests/e2e/fixtures/tone.mp3` et `tone-220.mp3`, 4 s, 16 Ko) sont fabriqués par
`tests/e2e/fixtures/make-tone.sh` (ffmpeg) et validé dans le dépôt ;
`tests/e2e/fixtures/tones.m3u` liste ce morceau, un fichier absent et une adresse web.

## Avancement

| Chantier | Ce que ce document décrit |
| --- | --- |
| 1. `webamp/1-solution` | Solution, manifeste, fenêtre, `WebampPlayer`, `TrackLibrary`, module JavaScript. |
| 2. `webamp/2-open-files` | Ouvrir depuis l'OS : types audio, une seule fenêtre, file d'ouverture, menu des fichiers, dossiers, boîtes de l'OS pour « Ouvrir » et « Ajouter », avertissement de 1 Go. |
| 3. `webamp/3-playlists` | Listes de lecture : lire, écrire, ouvrir, « Load list », « Save list ». |
