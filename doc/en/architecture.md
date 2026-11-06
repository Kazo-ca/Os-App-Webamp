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
| `WebampApp.cs` | Manifest (`[AppManifest]`): id `ca.kazo.webamp`, name "Webamp", version `1.0.0`, publisher `pub.kazo`, category `multimedia`, **desktop only** (no `MobileEntryPoint`), `SingleInstancePerUser`. 277×388 window (the content: Webamp's three windows stacked, 275×348, plus the OS frame and title bar), resizable, down to the main window alone. Base capabilities: `filesystem.user-home.read`, `filesystem.private.read` and `.write`; work item 3 adds `filesystem.user-home.write`, `dialogs.file-open` and `dialogs.file-save` ("Load list", "Save list"). File types of work item 3: `audio/x-mpegurl .m3u`, `application/vnd.apple.mpegurl .m3u8`, `audio/x-scpls .pls` (action `open`). Work items 2, 4 and 5 add theirs. |
| `WebampWindow.razor` (+ `.cs`, `.css`) | The window: an element Webamp draws into (`renderInto`) and a status line (starting, unreadable file, what a list left out) that a × button closes. On the first render it starts `WebampPlayer` and creates `TrackLibrary`; opened on a file (`LaunchFile`), it plays it, on a list it adds its tracks and plays the first one (`OpenFilesAsync`). It answers Webamp's "Load list" and "Save list" with the OS dialogs. Webamp's close button stops the app's process, which closes the OS window and stops the sound. The CSS hides "Add URL" (decision 9) and keeps Webamp inside the window. |
| `Player/WebampPlayer.cs` | The C# side of one Webamp instance: imports `wwwroot/js/webamp-host.js`, gives it the element and a .NET reference, sends the tracks, receives the events (`Closed`, `Minimized`, `TrackChanged`, `PlaylistChanged`, `LoadListRequested`, `SaveListRequested`). Implements `ITrackPlayer`. `DisposeAsync` stops the music and releases everything. |
| `Tracks/TrackLibrary.cs` | The files of Webamp's list (decision 14): read whole with `KazoFile.ReadAllBytesAsync`, handed to Webamp, forgotten when they leave the list. API for work items 2 to 5: `AddAsync(paths, play)`, `ReplaceAsync(paths, play)` (and their overloads that take `PlaylistEntry` items, with title and duration), `TryGetPath(key)`, `Paths` (current list, in Webamp's order), `LoadedBytes` (for the 1 GB warning of work item 2), `Changed`. An unreadable file comes back in `TrackLoadResult.Failures`; the others are added. |
| `Tracks/ITrackPlayer.cs`, `TrackData.cs`, `TrackLoadResult.cs` | The contract between the library and Webamp (a fake in the tests); one track sent (key, name, type, bytes, duration known from a list); the result of an add. |
| `Playlists/` | Playlists (work item 3, below): `PlaylistReader`, `PlaylistWriter`, `PlaylistPaths`, `PlaylistFiles`, `PlaylistFormats`. |
| `Player/WebampListEntry.cs` | One track of Webamp's list for "Save list" (key, title, duration). |
| `Tracks/AudioMediaTypes.cs` | Extension → audio type (decision 4), given to the `Blob` so the browser picks the right decoder. |
| `Strings.cs`, `Resources/` | English and French texts (Kazo OS ADR 0104), translated name and description of the manifest (`AppManifest.fr.resx`). |
| `wwwroot/js/webamp-host.js` | The JavaScript module (below). |
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
                                                  OnLoadListRequested, OnSaveListRequested
```

| Call | Direction | Content |
| --- | --- | --- |
| `create(element, dotNet, { zIndex })` | C# → JS | Loads `lib/webamp/webamp.bundle.min.mjs`, creates Webamp with `enableHotkeys: false` (decision 17) and a `handleAddUrlEvent` that does nothing (decision 9), draws it into the element. Returns a host object (an `IJSObjectReference`). |
| `addTracks(tracks, play)` | C# → JS | Adds at the end of the list; with `play`, the first one added plays. A track is `{ key, name, mimeType, bytes, durationSeconds }`; `bytes` is the C# `byte[]`, received as a `Uint8Array`; `durationSeconds` (from a list, otherwise `null`) shows until Webamp reads the file. |
| `replaceTracks(tracks, play)` | C# → JS | Replaces the list (`setTracksToPlay`); without `play`, Webamp is stopped. |
| `dispose()` | C# → JS | Stops the music, removes Webamp, gives every address back. |
| `OnPlaylistChanged(keys)` | JS → C# | The keys still in the list, in its order, on every real change (not on every tick of Webamp's clock). |
| `OnTrackChanged({ artist, title } \| null)` | JS → C# | The track that starts (for the window title of work item 5). |
| `OnWebampClosed()`, `OnWebampMinimized()` | JS → C# | Webamp's close and minimize buttons. |
| `OnLoadListRequested()` | JS → C# | "Load list" (`handleLoadListEvent`, which gives Webamp `null`: C# replaces the list itself). |
| `OnSaveListRequested(tracks, unsaved)` | JS → C# | "Save list" (`handleSaveListEvent`): the tracks that have a key, `{ key, title, duration }` in order (title "Artist - Title" from the tags, otherwise the name shown), and how many tracks have no key (dropped from the computer). |

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
module. Butterchurn is not in this bundle: work item 5 loads it when Milkdrop
first opens, through `__butterchurnOptions` (`importButterchurn` to
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

## Tests

| Suite | Command | Content |
| --- | --- | --- |
| C# (xUnit) | `dotnet test Kazo.Webamp.slnx -c Release` | Manifest; `TrackLibrary`: add in order with the type, add at the end, unreadable file, nothing sent when nothing reads, track that left the list forgotten, Webamp's order, replace, list changes during an add, Webamp failing; audio types; playlists: reading (relative and absolute paths, `\`, `#EXTINF`, web addresses, Windows drives, Latin-1, UTF-8 with and without a mark, `.pls`), writing (UTF-8, relative paths, one-line titles), round trip, a list opened with other files, unreadable list, title and duration up to Webamp, "Load list", "Save list"; file types and capabilities. |
| JavaScript | `npm test` | `webamp-host.js` with a fake Webamp: options, addresses created, kept while a batch arrives, given back when they leave the list, on replace and on `dispose`, deduplicated reports, events; "Load list" and "Save list" (keyed tracks with title and duration, unkeyed tracks counted), a list's duration given to Webamp. |
| E2E (Playwright) | `npm run test:e2e` after the Release build | Webamp opens from the Applications menu in its window, without "Add URL"; an `.mp3` put in `~` with the explorer and opened with `launch ca.kazo.webamp /home/neo/tone.mp3` plays with its tags, and Webamp's close button closes the OS window; a missing file is reported; "Save list" saves `mix.m3u8` through the OS dialog, "New list" empties Webamp and "Load list" fills it again; an opened `.m3u` plays its track and reports the missing file and the web address. |

The test `.mp3` (`tests/e2e/fixtures/tone.mp3`, 4 s, 16 KB) is made by
`tests/e2e/fixtures/make-tone.sh` (ffmpeg) and committed;
`tests/e2e/fixtures/tones.m3u` lists that track, a missing file and a web address.

## Progress

| Work item | What this document describes |
| --- | --- |
| 1. `webamp/1-solution` | Solution, manifest, window, `WebampPlayer`, `TrackLibrary`, JavaScript module. |
| 3. `webamp/3-playlists` | Playlists: read, write, open, "Load list", "Save list". |
