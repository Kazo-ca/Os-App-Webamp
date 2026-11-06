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
| `WebampApp.cs` | Manifeste (`[AppManifest]`) : id `ca.kazo.webamp`, nom « Webamp », version `1.0.0`, éditeur `pub.kazo`, catégorie `multimedia`, **ordinateur seulement** (pas de `MobileEntryPoint`), `SingleInstancePerUser`. Fenêtre de 277×388 (le contenu : les trois fenêtres de Webamp empilées, 275×348, plus le cadre et la barre de titre de l'OS), redimensionnable, au plus petit la fenêtre principale seule. Capacités de base : `filesystem.user-home.read`, `filesystem.private.read` et `.write`. Les chantiers 2 à 5 ajoutent les leurs. |
| `WebampWindow.razor` (+ `.cs`, `.css`) | La fenêtre : un élément où Webamp se dessine (`renderInto`) et une ligne d'état (démarrage, fichier illisible). Au premier rendu, elle démarre `WebampPlayer` et crée `TrackLibrary` ; ouverte sur un fichier (`LaunchFile`), elle le joue. Le bouton fermer de Webamp arrête le processus de l'app, ce qui ferme la fenêtre de l'OS et coupe le son. Le CSS cache « Add URL » (décision 9) et garde Webamp dans la fenêtre. |
| `Player/WebampPlayer.cs` | Le côté C# d'une instance de Webamp : importe `wwwroot/js/webamp-host.js`, lui passe l'élément et une référence .NET, envoie les morceaux, reçoit les événements (`Closed`, `Minimized`, `TrackChanged`, `PlaylistChanged`). Implémente `ITrackPlayer`. `DisposeAsync` arrête la musique et libère tout. |
| `Tracks/TrackLibrary.cs` | Les fichiers de la liste de Webamp (décision 14) : lus entiers par `KazoFile.ReadAllBytesAsync`, donnés à Webamp, oubliés quand ils sortent de la liste. API des chantiers 2 à 5 : `AddAsync(chemins, play)`, `ReplaceAsync(chemins, play)`, `Paths` (liste courante, dans l'ordre de Webamp), `LoadedBytes` (pour l'avertissement de 1 Go du chantier 2), `Changed`. Un fichier illisible est rendu dans `TrackLoadResult.Failures`, les autres sont ajoutés. |
| `Tracks/ITrackPlayer.cs`, `TrackData.cs`, `TrackLoadResult.cs` | Le contrat entre la bibliothèque et Webamp (un faux dans les tests) ; un morceau envoyé (clé, nom, type, octets) ; le résultat d'un ajout. |
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
                                                  OnTrackChanged, OnPlaylistChanged
```

| Appel | Sens | Contenu |
| --- | --- | --- |
| `create(élément, dotNet, { zIndex })` | C# → JS | Charge `lib/webamp/webamp.bundle.min.mjs`, crée Webamp avec `enableHotkeys: false` (décision 17) et un `handleAddUrlEvent` qui ne fait rien (décision 9), le dessine dans l'élément. Rend un objet hôte (une `IJSObjectReference`). |
| `addTracks(morceaux, play)` | C# → JS | Ajoute à la fin de la liste ; avec `play`, le premier ajouté joue. Un morceau est `{ key, name, mimeType, bytes }` ; `bytes` est le `byte[]` du C#, reçu en `Uint8Array`. |
| `replaceTracks(morceaux, play)` | C# → JS | Remplace la liste (`setTracksToPlay`) ; sans `play`, Webamp est arrêté. |
| `dispose()` | C# → JS | Arrête la musique, retire Webamp, rend toutes les adresses. |
| `OnPlaylistChanged(clés)` | JS → C# | Les clés encore dans la liste, dans son ordre, à chaque changement réel (pas à chaque tic de l'horloge de Webamp). |
| `OnTrackChanged({ artist, title } \| null)` | JS → C# | Le morceau qui commence (pour le titre de la fenêtre du chantier 5). |
| `OnWebampClosed()`, `OnWebampMinimized()` | JS → C# | Les boutons fermer et réduire de Webamp. |

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

## Tests

| Suite | Commande | Contenu |
| --- | --- | --- |
| C# (xUnit) | `dotnet test Kazo.Webamp.slnx -c Release` | Manifeste ; `TrackLibrary` : ajout dans l'ordre avec le type, ajout à la fin, fichier illisible, rien envoyé quand rien ne se lit, morceau sorti de la liste oublié, ordre de Webamp, remplacement, changements de liste pendant l'ajout, échec de Webamp ; types audio. |
| JavaScript | `npm test` | `webamp-host.js` avec un faux Webamp : options, adresses créées, gardées pendant l'arrivée d'un lot, rendues à la sortie de la liste, au remplacement et à `dispose`, rapports dédoublonnés, événements. |
| E2E (Playwright) | `npm run test:e2e` après le build Release | Webamp s'ouvre depuis le menu Applications dans sa fenêtre, sans « Add URL » ; un `.mp3` mis dans `~` par l'explorateur et ouvert par `launch ca.kazo.webamp /home/neo/tone.mp3` joue avec ses étiquettes, et le bouton fermer de Webamp ferme la fenêtre de l'OS ; un fichier absent est signalé. |

Le `.mp3` de test (`tests/e2e/fixtures/tone.mp3`, 4 s, 16 Ko) est fabriqué par
`tests/e2e/fixtures/make-tone.sh` (ffmpeg) et validé dans le dépôt.

## Avancement

| Chantier | Ce que ce document décrit |
| --- | --- |
| 1. `webamp/1-solution` | Solution, manifeste, fenêtre, `WebampPlayer`, `TrackLibrary`, module JavaScript. |
