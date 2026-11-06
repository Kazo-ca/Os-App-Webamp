> [Version française](../fr/architecture.md)

# Architecture of the Webamp app

## Purpose

Webamp is a Kazo OS window app: a Winamp 2 style music player built on
[Webamp](https://github.com/captbaritone/webamp) 2.3.1. The goal and the
decisions are in the [plan](plan.md). This document describes the code; each
work item of the `webamp/base` series completes it.

## Solution

```text
Os-App-Webamp/
├─ external/KazoOs/          # submodule: Kazo OS on the v3.0.0 tag (the repository's version)
├─ Directory.Build.props     # submodule root, profile, import of Versions.props
├─ Directory.Build.targets   # source mode in Debug, KOSAPP0070 without the submodule
├─ global.json
├─ Kazo.Webamp.slnx
├─ src/
│  ├─ Kazo.Webamp/           # the app
│  └─ Kazo.Webamp.Os/        # host: Kazo OS, its base apps and Webamp
├─ tests/
│  ├─ Kazo.Webamp.Tests/     # C# unit tests (xUnit)
│  ├─ js/                    # tests of the JavaScript module (node --test)
│  └─ e2e/                   # runs in the host (node --test + Playwright)
├─ store/                    # Kazo OS Store listing (completed by work item 6)
├─ .github/workflows/        # ci.yml, publish-*.yml, tag-release.yml
└─ package.json              # tools of the JavaScript tests
```

The repository follows the output of the Kazo OS 3.0.0 **`kazoos-solution`**
template (ADR 0176 and 0207), like [Chess](https://github.com/Kazo-ca/Os-App-Chess):
`external/KazoOs` is Kazo OS as a submodule, exactly on a release tag, and its
`build/Versions.props` gives **the** version of the repository. The submodule is
never modified: CI refuses a modified submodule or one off its tag
(`.github/scripts/check-kazoos-submodule.sh`). The workflows come from Chess
(the template's, with the fixes made since: catalog fork found by owner, "Tag a
release" workflow, CI that also runs the JavaScript and Playwright tests).

| Profile | Kazo OS comes from | Use |
| --- | --- | --- |
| `Debug` (`KazoOsUseSource=true`) | the submodule's projects (step into the SDK and the OS) | development, debugging |
| `Release` (`KazoOsUseSource=false`) | the `Kazo.Os.*` packages of nuget.org, at the submodule's version | CI, E2E tests, Kazo OS Store |

Clone with `git clone --recurse-submodules`, or run `git submodule update --init`
afterwards (without the submodule, the build stops on `KOSAPP0070`).

**Host.** `Kazo.Webamp.Os` is a full Kazo OS with the base apps and Webamp as a
`ProjectReference`: its `wwwroot/` files are served at `_content/Kazo.Webamp/`.
`dotnet run --project src/Kazo.Webamp.Os` starts it on <http://localhost:5290>.

## The `Kazo.Webamp` app

| Folder or file | Role |
| --- | --- |
| `WebampApp.cs` | Manifest (`[AppManifest]`): id `ca.kazo.webamp`, name "Webamp", version `1.0.0`, publisher `pub.kazo`, category `multimedia`, **desktop only** (no `MobileEntryPoint`), `SingleInstancePerUser`. 277×388 window (the content: Webamp's three windows stacked, 275×348, plus the OS frame and title bar), resizable, down to the main window alone. Base capabilities: `filesystem.user-home.read`, `filesystem.private.read` and `.write`; work item 3 adds `filesystem.user-home.write`, `dialogs.file-open` and `dialogs.file-save` ("Load list", "Save list"); work item 2, `dialogs.folder-select` and `shell.context-menu`. File types: the lists of work item 3 (`audio/x-mpegurl .m3u`, `application/vnd.apple.mpegurl .m3u8`, `audio/x-scpls .pls`) and the audio types of work item 2, action `open`; the `ca.kazo.webamp.files.v1` intent and the two file menu entries (work item 2). Work item 4 adds skins (below), work item 5 no capability (its `state.json` is in the private folder). |
| `WebampWindow.razor` (+ `.cs`, `.css`) | The window: an element Webamp draws into (`renderInto`) and a status line (starting, unreadable file, what a list left out) that a × button closes. On the first render it starts `WebampPlayer` and creates `TrackLibrary`; what it is given to open (at launch, by an intent, by the file menu or by Webamp's "Open") goes through `OpenQueue` then `OpenAsync` (below, "Opening from the OS"). It answers Webamp's "Load list" and "Save list" with the OS dialogs. Webamp's close button stops the app's process, which closes the OS window and stops the sound. The CSS hides "Add URL" (decision 9) and keeps Webamp inside the window. At start it reads the state (`state.json`) back and puts it into Webamp; it writes the state on every change (grouped) and a last time on close (`IWindowCloseGuard`, process stop, `DisposeAsync`). Its title follows the current track (`Player/TrackTitle`), and it tells the module when it has the focus (shortcuts). |
| `Player/WebampPlayer.cs` | The C# side of one Webamp instance: imports `wwwroot/js/webamp-host.js`, gives it the element, a .NET reference and the saved settings, sends the tracks, receives the events (`Closed`, `Minimized`, `TrackChanged`, `PlaylistChanged`, `LoadListRequested`, `SaveListRequested`, `OpenRequested`, `StateChanged`). Implements `ITrackPlayer` and `IStatePlayer` (read the state, resume a track at its position); `SetFocusedAsync` sets the shortcuts. `DisposeAsync` stops the music and releases everything. |
| `Player/TrackTitle.cs` | The OS window's title: "Artist - Title - Webamp", "Title - Webamp" without an artist, the file name without tags, "Webamp" without a track (decision 8). |
| `State/` | The remembered state (work item 5, below): `WebampState` (the content of `state.json`), `WebampStateJson` (tolerant reading, writing), `WebampStateFile` (the file), `StateSaveScheduler` (grouped writes), `PlaylistMemory` (files that disappeared kept in the list), `WebampStateKeeper` (put the state back into Webamp, read it), `IStatePlayer`. |
| `Tracks/TrackLibrary.cs` | The files of Webamp's list (decision 14): read whole with `KazoFile.ReadAllBytesAsync`, handed to Webamp, forgotten when they leave the list. API for work items 2 to 5: `AddAsync(paths, play)`, `ReplaceAsync(paths, play)` (and their overloads that take `PlaylistEntry` items, with title and duration), `TryGetPath(key)`, `Paths` (current list, in Webamp's order), `LoadedBytes` (for the 1 GB warning of work item 2), `Changed`. An unreadable file comes back in `TrackLoadResult.Failures`; the others are added. |
| `Tracks/ITrackPlayer.cs`, `TrackData.cs`, `TrackLoadResult.cs` | The contract between the library and Webamp (a fake in the tests); one track sent (key, name, type, bytes, duration known from a list); the result of an add. |
| `Opening/` | Opening from the OS (work item 2, below): `OpenRequest`, `OpenIntents`, `OpenQueue`, `AudioFileCollector`, `LoadedMemoryWarning`. |
| `Playlists/` | Playlists (work item 3, below): `PlaylistReader`, `PlaylistWriter`, `PlaylistPaths`, `PlaylistFiles`, `PlaylistFormats`. |
| `Player/WebampListEntry.cs` | One track of Webamp's list for "Save list" (key, title, duration). |
| `Tracks/AudioMediaTypes.cs` | Extension → audio type (decision 4), given to the `Blob` so the browser picks the right decoder. |
| `Strings.cs`, `Resources/` | English and French texts (Kazo OS ADR 0104), translated name and description of the manifest (`AppManifest.fr.resx`). |
| `WebampWindow.Skins.cs` | The window's skins (work item 4, [Skins](#skins)): open a `.wsz`, "Load Skin…", chosen skin remembered, a refused skin with a message, the tip notification. |
| `Skins/` | `SkinArchive` (is a `.wsz` a skin?), `SkinLibrary` (the `~/.local/share/webamp/skins/` folder), `SkinSettings` (`~/.config/ca.kazo.webamp/skins.json`), `SkinController` (all of it for one window), `ISkinFileSystem` and `KazoSkinFileSystem` (the files, in memory in the tests), `ISkinPlayer` (what the skins ask of Webamp). |
| `wwwroot/js/webamp-host.js` | The JavaScript module (below). |
| `wwwroot/js/webamp-skins.js` | The bridge to Webamp's Skins menu (see [Skins](#skins)). |
| `wwwroot/lib/` | Webamp 2.3.1, Butterchurn and its presets, each with its license ([THIRD-PARTY-NOTICES.md](../../THIRD-PARTY-NOTICES.md)). |
| `wwwroot/icons/app.svg` | The icon. |

## C# and JavaScript

C# keeps the logic: which files are in the list, where they come from, what to
play. The `webamp-host.js` module only does what C# cannot reach: Webamp itself,
`blob:` addresses and the browser's events.

```text
WebampWindow ── TrackLibrary ── reads ──► KazoFile.ReadAllBytesAsync (the whole file)
                    │
                    ▼  ITrackPlayer
               WebampPlayer ── import ──► webamp-host.js ── new Webamp(...) ──► renderInto(element)
                    ▲                          │
                    └──── invokeMethodAsync ───┘  OnWebampClosed, OnWebampMinimized,
                                                  OnTrackChanged, OnPlaylistChanged,
                                                  OnLoadListRequested, OnSaveListRequested,
                                                  OnOpenRequested
```

| Call | Direction | Content |
| --- | --- | --- |
| `create(element, dotNet, { zIndex, skins, initialSkin, settings, focused })` | C# → JS | Loads `lib/webamp/webamp.bundle.min.mjs`, creates Webamp with `enableHotkeys: true` with its page-wide listener captured (decision 17), `enableMediaSession: true` (decision 8), a `handleAddUrlEvent` that does nothing (decision 9), the skins of the user's folder (`availableSkins`) and the skin in use (`initialSkin`) and, from `settings` (the saved `PlayerSettings`, `null` on a first run), the window layout (`windowLayout`) and double size, lazy Milkdrop (decision 12); puts back the volume, balance, equalizer, shuffle and repeat and, if the user had moved the windows, their exact places, before the first frame; draws it into the element. Returns a host object (an `IJSObjectReference`). A skin is `{ key, name, bytes }`; `key` is its file name in the folder. |
| `addSkin(skin, apply)` | C# → JS | Adds a skin to the Skins menu (or replaces the one with the same key, whose address is given back); with `apply`, puts it on and waits until Webamp drew it. |
| `addTracks(tracks, play)` | C# → JS | Adds at the end of the list; with `play`, the first one added plays. A track is `{ key, name, mimeType, bytes, durationSeconds }`; `bytes` is the C# `byte[]`, received as a `Uint8Array`; `durationSeconds` (from a list, otherwise `null`) shows until Webamp reads the file. |
| `replaceTracks(tracks, play)` | C# → JS | Replaces the list (`setTracksToPlay`); without `play`, Webamp is stopped. |
| `dispose()` | C# → JS | Stops the music, removes Webamp, gives every address back. |
| `OnPlaylistChanged(keys)` | JS → C# | The keys still in the list, in its order, on every real change (not on every tick of Webamp's clock). |
| `OnTrackChanged({ artist, title, name } \| null)` | JS → C# | The current track, playing or paused, on every change of track or tags (`name`: the file name); `null` without a track. For the window title. |
| `readState()` | C# → JS | What Webamp holds (`PlayerSnapshot`): the current track's key, the position (0 when stopped), volume, balance, shuffle, repeat, equalizer, double size, the four windows (place, open, rolled up, extra size) and whether the user fixed their places. |
| `resume(index, position)` | C# → JS | Makes a track current without playing it; with a position, waits until it is loaded, goes there and pauses Webamp (Play continues from there). |
| `setFocused(focused)` | C# → JS | Webamp's shortcuts act only while the window has the focus. |
| `OnStateChanged()` | JS → C# | Something of the state changed (not the clock: the position counts in steps of 5 s while playing). |
| `OnWebampClosed()`, `OnWebampMinimized()` | JS → C# | Webamp's close and minimize buttons. |
| `OnLoadListRequested()` | JS → C# | "Load list" (`handleLoadListEvent`, which gives Webamp `null`: C# replaces the list itself). |
| `OnOpenRequested(kind)` | JS → C# | A Webamp file picker intercepted (`open`, `add-files`, `add-folder`): C# shows the OS dialog ("Opening from the OS"). |
| `OnSaveListRequested(tracks, unsaved)` | JS → C# | "Save list" (`handleSaveListEvent`): the tracks that have a key, `{ key, title, duration }` in order (title "Artist - Title" from the tags, otherwise the name shown), and how many tracks have no key (dropped from the computer). |
| `OnSkinFileChosen(name, bytes)` | JS → C# | A skin of the computer ("Skins ▸ Load Skin…" or dropped on Webamp), not put on yet. |
| `OnSkinChosen(key \| null)` | JS → C# | A skin picked in the Skins menu (Webamp puts it on itself); `null` for "&lt;Base Skin&gt;". |

**The `Blob`s.** For each track the module creates a typed `Blob` and its address
(`URL.createObjectURL`) and gives Webamp a track **by address**
(`{ url, defaultName }`) rather than a `BlobTrack`: given a `Blob`, Webamp creates
the address itself and never gives it back. The module follows the list through
`webamp.store.subscribe`: an address that was in the list and is no longer there
is given back (`URL.revokeObjectURL`) and its key leaves the next report, which
`TrackLibrary.OnPlaylistChanged` turns into forgetting the file. Webamp adds a
batch one track at a time, so an address not yet seen in the list is never given
back. Tracks the user drops from the computer are Webamp's own: with no key, they
are not in `Paths`.

**The bundle.** The app takes `webamp.bundle.min.mjs` (940 KB, with JSZip and
music-metadata), not `webamp.lazy-bundle.min.mjs`: the lazy bundle needs JSZip
and music-metadata to be provided, and music-metadata has no browser-ready
module. Butterchurn is not in this bundle: the module loads it when Milkdrop
first opens (see "State, title, shortcuts, Milkdrop"), through `__butterchurnOptions` (`importButterchurn` to
`lib/butterchurn/butterchurn.min.js`, a self-contained ES module; `getPresets` to
`lib/butterchurn-presets/base.min.js`, which declares itself in `self.base`).
Webamp's `webamp.butterchurn-bundle.min.mjs` is not used: it is a whole 2 MB
Webamp that loads Butterchurn at start.

## Close and minimize

- **Close** (Webamp's button, or "Exit" in its menu): `WebampPlayer.Closed` → the
  window stops the app's process
  (`AppContext.Processes.StopAsync(AppContext.ProcessId, …)`, allowed without a
  capability). The OS window closes, `DisposeAsync` stops the music and gives the
  memory back (decision 8).
- **Minimize**: the Kazo OS 3.0.0 SDK does not let an app minimize its own
  window. Benoit chose (2026-11-06, question 1, answer A): a TODO issue
  [Os#384](https://github.com/Kazo-ca/Os/issues/384); until then Webamp's
  minimize button does nothing and the one of the OS title bar works. Work item 7
  wires it (`WebampPlayer.Minimized`) when the submodule moves to the version
  that brings it.

## Webamp inside an OS window

- Webamp centers its three windows in the element; they can be moved inside it,
  as on a page.
- A first press on a Webamp window that is not in front only selects it (Webamp
  brings it forward), the next one acts: that is how Webamp works, and the E2E
  tests take it into account.
- Webamp adds its styles to the page itself; the `.razor` has no inline style or
  script (ADR 0007).

## Opening from the OS

Work item 2, decisions 4, 5, 14 and 16 of the [plan](plan.md).

- **Audio types** (decision 4): `audio/mpeg .mp3`, `audio/wav .wav`,
  `audio/ogg .ogg .oga`, `audio/opus .opus`, `audio/flac .flac`,
  `audio/mp4 .m4a`, `audio/aac .aac`, `audio/webm .weba`, action `open`:
  Webamp becomes the app of audio files (double-click, "Open with").
- **One window** (decision 5): `SingleInstancePerUser`. A file opened while
  Webamp runs arrives in `OnIntentReceivedAsync` (the `launch` intent with the
  path as argument); at launch, in `LaunchIntent` and `LaunchArguments`.
  `OpenIntents.Read` turns either into an `OpenRequest(paths, mode)`; mode
  `Play` (add at the end and play the first one), `Enqueue` (add without
  touching playback) or `Replace`.
- **A multiple selection**: the explorer opens the files one at a time (the
  first launches the app, the others arrive as intents). `OpenQueue` gathers
  what arrives within 300 ms and merges neighbouring requests of the same mode
  (`OpenQueue.Merge`): the selection is added whole, in order, and its first
  file plays. Requests are handled one at a time; an error goes to the status
  line and does not stop the next ones.
- **The file menu**: `[RegisterFileContextMenu]` declares "Play in Webamp"
  (`play`, after "Open") and "Add to Webamp's list" (`enqueue`), on audio files,
  lists (`.m3u .m3u8 .pls`) and folders (`inode/directory`), multiple selection
  allowed (`shell.context-menu`). Both send the `ca.kazo.webamp.files.v1` intent
  (declared in `Intents`); `KazoFileMenuIntent.ReadPaths` and `ReadEntry` give
  the paths and the chosen entry. Titles in `AppManifest.resx` and
  `AppManifest.fr.resx`.
- **A folder** (decision 16): `AudioFileCollector` replaces it with its audio
  files and those of its subfolders
  (`KazoDirectory.GetFilesAsync(…, AllDirectories)`), sorted by path segment by
  segment, ignoring case (`Album/` before `album 2/`). Lists found inside a
  folder are left out; a list chosen itself is opened
  (`PlaylistFiles.ExpandAsync`, work item 3). An unreadable folder is reported
  in the status line; so is a folder with no audio file ("… has no audio file
  Webamp can play").
- **Webamp's "Open" and "Add"** (decision 16): Webamp always asks for files
  through an `<input type=file>` that is never attached, calling `click()` on it
  during the user's click. `FilePromptInterceptor` (`webamp-host.js`) listens,
  in the capture phase, to clicks and keys on Webamp and its context menu
  (`#webamp-context-menu`), and replaces `HTMLInputElement.prototype.click` once
  per page: during that gesture, a music picker (not `.wsz`/`.zip` nor `.eqf`)
  does not open the browser's picker but calls
  `OnOpenRequested("open" | "add-files" | "add-folder")`. C# shows the OS
  dialog: "Open in Webamp" (eject button, Play ▸ File…) and "Add files to
  Webamp" (ADD ▸ Add file) through `dialogs.file-open`, multiple selection,
  filter "Music and playlists"; "Add folder" (ADD ▸ Add dir) through
  `dialogs.folder-select`. Eject **replaces** the list and plays, as in Winamp;
  the other two add at the end. The dialogs open in the last folder chosen,
  otherwise `~/Music`, otherwise `~`.
- **1 GB warning** (decision 14): after each add, `LoadedMemoryWarning`
  compares `TrackLibrary.LoadedBytes` with 1 GB; above it, an OS message box
  (warning) says so once, and comes back only after going under the limit again.

**Kazo OS 3.0.0 workaround.** Without `InitialDirectory`, the
`dialogs.file-open` and `dialogs.folder-select` dialogs open on
`/home/<user id>`, which does not exist, and bring the OS down. The app therefore
always gives a starting folder.

## Playlists

Work item 3, decision 10 of the [plan](plan.md).

- **Read** (`PlaylistReader`): `.m3u`, `.m3u8` and `.pls`. Absolute paths or
  paths relative to the list's folder, `\` accepted (lists made on Windows),
  `file:///…` read as a path. `#EXTINF:seconds,title` (and `TitleN`, `LengthN`
  of a `.pls`) gives the title and duration shown until Webamp reads the tags;
  `-1` means "unknown duration". Web addresses are left out (no "Add URL",
  decision 9) and counted; so are a Windows drive (`C:\`), a share
  (`\\server`) and a path that goes above `/`. Encoding: a byte order mark
  first; otherwise `.m3u8` is UTF-8, `.m3u` and `.pls` are UTF-8 when they are,
  Latin-1 when not.
- **Write** (`PlaylistWriter`): `.m3u8` in UTF-8 without a mark, `#EXTM3U`, then
  `#EXTINF:seconds,title` and the path **relative** to the list's folder (`../`
  to go up, `PlaylistPaths.GetRelativePath`). Without a title, the file name
  without extension; a line break in a title becomes a space. Read back, the
  list gives the same tracks, and it still works when the folder that holds it
  and the music moves.
- **Opening a list** (double-click, "Open with", `launch`): like an audio file
  (decision 5), its tracks are added at the end of the current list and the
  first one plays (`WebampWindow.OpenFilesAsync`, on
  `PlaylistFiles.ExpandAsync`). A missing file of the list fails when it is
  read, like any file; the status line names the first unreadable file, how
  many others, and what the list left out.
- **"Load list"**: the OS Open dialog (`dialogs.file-open`, playlist filter),
  then the list **replaces** Webamp's, stopped, as in Winamp. A list with no
  readable track leaves the current list as it is.
- **"Save list"**: the OS Save As dialog (`dialogs.file-save`, `.m3u8` by
  default, confirmation before replacing), then the write with
  `KazoFile.WriteAllBytesAsync` (`filesystem.user-home.write`). Tracks dropped
  from the computer have no file in Kazo OS: they are not written, and the
  status line says so.
- Webamp's menus call C# from JavaScript, outside the window's own handlers:
  `WebampWindow` enters the app's ambient context itself
  (`KazoExecutionContext.Enter`, Kazo OS ADR 0043) before `KazoFile`, and shows
  any error in the status line.
- The file menu entries ("Play in Webamp", "Add to Webamp's list") are work
  item 2's; they go through `OpenFilesAsync`, which opens lists too.

## Skins

Decisions 11 and 20 of the [plan](plan.md).

- **The folder.** Every skin opened or loaded is copied to
  `~/.local/share/webamp/skins/` (capability `filesystem.user-home.write`),
  under its own file name, made safe (no path, no control character) and
  ending in `.wsz` (a `.zip` becomes a `.wsz`). A skin of the same name
  replaces the old one. At start, `SkinLibrary` reads the folder again: every
  valid `.wsz` becomes an item of Webamp's Skins menu, sorted by name; a file
  that does not read or is not a skin is left there and not listed.
- **A valid skin.** `SkinArchive` checks in C#, before any copy and before
  Webamp: a ZIP archive of at most 16 MB holding `main.bmp` (at the root or in
  a folder, case ignored). Otherwise a message box ("Bento.wsz was not loaded:
  … The current skin stays.") and nothing is sent to Webamp. Webamp itself can
  only report a broken skin with a browser `alert`.
- **Opening a `.wsz` in the OS** (double-click, "Open with", `webamp
  skin.wsz`): associated type `application/x-winamp-skin` `.wsz`; at launch
  or in the window already open (through `OpenQueue`, like music, one
  window per user), the skin is copied, added to the menu and put on. A `.wsz`
  already in the folder is only put on.
- **Webamp's "Skins ▸ Load Skin…".** Webamp 2.3.1 has no option for this menu
  item: it opens its own `input type=file` and applies the file without giving
  it out. `webamp-skins.js` listens to the page's clicks in the capture phase
  (before Webamp) and recognises the item by its place in Webamp's menu
  (`#webamp-context-menu`, the submenu whose text is "Skins": "Load Skin...", a
  separator, "&lt;Base Skin&gt;", then the skins in the order given). For "Load
  Skin…", it stops the click, closes the menu, opens the browser's file picker
  (a file of the computer, decision 11) and sends the file to C#, which checks
  it, copies it and puts it on. A single `.wsz` or `.zip` dropped on Webamp
  takes the same path.
- **Remembered skin.** A skin picked in the menu is put on by Webamp itself;
  the module reports which one (`OnSkinChosen`, by its place in the menu) and
  `SkinSettings` writes it to `~/.config/ca.kazo.webamp/skins.json`
  (`{ "current": "Bento.wsz", "importTipShown": true }`). At the next launch
  Webamp starts with that skin (`initialSkin`); a remembered skin no longer in
  the folder gives the base skin back. Work item 5 left it there: `state.json`
  does not hold the skin, so there is one place for it.
- **The import tip** (decision 20). On the first launch, a notification
  (`notifications.post`): "To change the skin: double-click a .wsz file, or
  right-click ▸ Skins ▸ Load Skin…". `importTipShown` becomes `true` once the
  notification is posted; when it is refused, it is tried again at the next
  launch. The same text is in the README.
- **Memory.** Skins are given to Webamp as `blob:` addresses created by the
  module and given back when a skin is replaced and on `dispose`.

## State, title, shortcuts, Milkdrop

Work item 5, decisions 8, 12, 13 and 17 of the [plan](plan.md).

**The state** is in `~/.config/ca.kazo.webamp/state.json`, the app's own
settings folder (ADR 0044 of Kazo OS: `filesystem.private.read` and `.write`,
already in the manifest), as a Linux program keeps its own under
`~/.config/<program>/`. Readable JSON:

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

- **Reading** (`WebampStateFile.LoadAsync`, `WebampStateJson.Parse`): a file
  that is missing, unreadable, not JSON or of another version gives an empty
  state (Webamp starts as the first time); a value out of range is brought back
  into its range (volume 0 to 100, balance -100 to 100, equalizer 0 to 100, ten
  bands or a flat equalizer), a path the OS refuses is left out.
- **Putting back**: the settings and the layout go with Webamp's creation
  (`create`), so from the first frame; then `WebampStateKeeper` adds the
  list **without playing** (before the files Webamp was opened on, which the open queue adds once the state is back), makes the current track current and
  goes back to its position, paused. A file that has disappeared (network drive
  missing, file moved) is **skipped** and **stays in the saved list**, at its
  place, after the track that preceded it (`PlaylistMemory`): it comes back at
  the next start if it is there. If it is the current track, the next one
  becomes current, from its start. Emptying the list forgets those files.
- **Writing**: the module reports each change (`OnStateChanged`);
  `StateSaveScheduler` writes one second after the last one, never twice at the
  same time, and a last time on close: Webamp's close button, the OS window
  closing (`IWindowCloseGuard`, which asks the user nothing), the process
  stopping (`OnAppStopRequestedAsync`), and `DisposeAsync` as a last resort. A
  failed write is shown once in the status line; the next one tries again.
  These writes start from a JavaScript callback or a timer: the window enters
  the app's ambient context (`KazoExecutionContext.Enter`) before `KazoFile`.
- **Position**: read on Webamp's audio element (0 when stopped). Webamp starts
  a stopped track from its beginning: resuming therefore leaves it **paused** at
  the saved position.
- **Skin**: not in `state.json`. The skin in use stays in `skins.json`
  ([Skins](#skins)), which Webamp reads at creation (`initialSkin`): one
  file for the skin, no copy to keep in step.

**The title** of the OS window follows the current track, playing or paused:
"Artist - Title - Webamp" (decision 8, like Winamp's taskbar button), with the
file name until Webamp has read the tags, "Webamp" without a track
(`TrackTitle`, `SetWindowTitleAsync`). Webamp's `onTrackDidChange` event is not
used: it only names a track that plays.

**Media keys**: `enableMediaSession: true` (decision 8); Webamp fills the
browser's Media Session (title, artist, play, pause, next, previous, seek).

**Shortcuts** (decision 17): Webamp's `enableHotkeys` puts a `keydown`
listener on the whole page, which would take the keys of the other apps. The
module creates Webamp with that option but **captures** that listener during
construction (`constructCapturingHotkeys`: `document.addEventListener` is
replaced for the time of the constructor, which is synchronous) and calls it
only while the OS window has the focus (`OnWindowActivated` /
`OnWindowDeactivated` → `setFocused`). All of Webamp 2.3.1's shortcuts are kept
(X, C, V, B, Z, R, S, L, arrows, Ctrl+D, Alt+W/E/G…) without copying them. As in
Webamp, a key typed in a field (`input`, `textarea`, `select`) is ignored.

**Milkdrop** (decision 12): Webamp asks for Butterchurn and its presets as
soon as it is built (`__butterchurnOptions.importButterchurn` and
`getPresets`). `lazyButterchurn` gives it promises that wait until the Milkdrop
window first opens (the module watches `windows.genWindows.milkdrop.open`);
only then does it import `lib/butterchurn/butterchurn.min.js` and
`lib/butterchurn-presets/base.min.js`. Milkdrop stays in Webamp's menus. A
saved layout with Milkdrop open loads it at start.

## Tests

| Suite | Command | Content |
| --- | --- | --- |
| C# (xUnit) | `dotnet test Kazo.Webamp.slnx -c Release` | Manifest; skins: `SkinArchive` (archive, `main.bmp` in any case and folder, not an archive, no `main.bmp`, too large), `SkinLibrary` (folder, sorted list and skipped files, copy, skin already in the folder, replacement, refusal without copy, safe name), `SkinController` (opened skin put on and remembered, skin from the computer, broken skin refused and current skin kept, menu choice and base skin, remembered skin gone, broken settings, tip once and again after a failure); `TrackLibrary`: add in order with the type, add at the end, unreadable file, nothing sent when nothing reads, track that left the list forgotten, Webamp's order, replace, list changes during an add, Webamp failing; audio types; playlists: reading (relative and absolute paths, `\`, `#EXTINF`, web addresses, Windows drives, Latin-1, UTF-8 with and without a mark, `.pls`), writing (UTF-8, relative paths, one-line titles), round trip, a list opened with other files, unreadable list, title and duration up to Webamp, "Load list", "Save list"; file types and capabilities; work item 2: intents (file, file menu, `launch`), recursive folders sorted by path, unreadable folder, open queue (waiting, burst merged, order, error), 1 GB warning (once, re-armed under the limit), menu entries in `manifest.json`; state: JSON round trip, corrupt file or of another version = empty state, values brought back into range, refused paths, missing file, folder created; files that disappeared kept at their place and skipped, current track followed; grouped writes, immediate on close, never two at once, failure reported; list and position put back; window title. |
| JavaScript | `npm test` | `webamp-host.js` with a fake Webamp: options, addresses created, kept while a batch arrives, given back when they leave the list, on replace and on `dispose`, deduplicated reports, events; "Load list" and "Save list" (keyed tracks with title and duration, unkeyed tracks counted), a list's duration given to Webamp; skins at start (`availableSkins`, `initialSkin`), sorted add and put on, replacement, addresses given back. `webamp-skins.js` with a fake menu: items recognised by their place, "Load Skin…" taken from Webamp, cancel, choices reported, a dropped skin; `FilePromptInterceptor`: music picker intercepted during a gesture on Webamp, not outside Webamp nor for a skin or a preset, installed once per page and removed with the last window; Media Session, layout and settings put back before the first frame, state read, changes reported (the clock in steps of 5 s), paused resume at a position, shortcuts only with the focus (Webamp's listener captured), Milkdrop loaded when first opened. |
| E2E (Playwright) | `npm run test:e2e` after the Release build | Webamp opens from the Applications menu in its window, without "Add URL"; an `.mp3` put in `~` with the explorer and opened with `launch ca.kazo.webamp /home/neo/tone.mp3` plays with its tags, and Webamp's close button closes the OS window; a missing file is reported; "Save list" saves `mix.m3u8` through the OS dialog, "New list" empties Webamp and "Load list" fills it again; an opened `.m3u` plays its track and reports the missing file and the web address. Skins (`skins.e2e.mjs`, skins made by `fixtures/make-skin.mjs`): the notification on the first launch, a `.wsz` opened with `launch` into the open Webamp is put on and listed in the Skins menu; "Load Skin…" puts a skin on and keeps it, a broken skin is refused with a message box and the skin stays, Webamp opened again starts with the skin and no new notification; work item 2 (`open-files.e2e.mjs`): a double-click on an `.mp3` in the explorer plays it, two files opened together are added in order and the first plays, in the same window; "Add to Webamp's list" on a folder adds its tracks sorted without playing, "Play in Webamp" plays; eject and ADD ▸ Add file show the OS dialog, never the browser's; title "Kazo Test - Tone 440 - Webamp" on the taskbar, then after a page reload Webamp comes back with its list, paused, with the same volume and repeat; Webamp's shortcuts do nothing while the explorer has the focus and act when Webamp has it; Butterchurn is downloaded only when Milkdrop opens (`state.e2e.mjs`). |

The test `.mp3` files (`tests/e2e/fixtures/tone.mp3` and `tone-220.mp3`, 4 s, 16 KB) are made by
`tests/e2e/fixtures/make-tone.sh` (ffmpeg) and committed;
`tests/e2e/fixtures/tones.m3u` lists that track, a missing file and a web address.

## Progress

| Work item | What this document describes |
| --- | --- |
| 1. `webamp/1-solution` | Solution, manifest, window, `WebampPlayer`, `TrackLibrary`, JavaScript module. |
| 2. `webamp/2-open-files` | Opening from the OS: audio types, one window, open queue, file menu, folders, OS dialogs for "Open" and "Add", 1 GB warning. |
| 3. `webamp/3-playlists` | Playlists: read, write, open, "Load list", "Save list". |
| 4. `webamp/4-skins` | Skins: `.wsz` type, skins folder, "Load Skin…", remembered skin, import tip. |
| 5. `webamp/5-state-and-media` | Remembered state, window title, Media Session, shortcuts with the focus, lazy Milkdrop. |
