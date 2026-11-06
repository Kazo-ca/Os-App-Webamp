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
| `WebampApp.cs` | Manifest (`[AppManifest]`): id `ca.kazo.webamp`, name "Webamp", version `1.0.0`, publisher `pub.kazo`, category `multimedia`, **desktop only** (no `MobileEntryPoint`), `SingleInstancePerUser`. 277×388 window (the content: Webamp's three windows stacked, 275×348, plus the OS frame and title bar), resizable, down to the main window alone. Base capabilities: `filesystem.user-home.read`, `filesystem.private.read` and `.write`. Work items 2 to 5 add theirs. |
| `WebampWindow.razor` (+ `.cs`, `.css`) | The window: an element Webamp draws into (`renderInto`) and a status line (starting, unreadable file). On the first render it starts `WebampPlayer` and creates `TrackLibrary`; opened on a file (`LaunchFile`), it plays it. Webamp's close button stops the app's process, which closes the OS window and stops the sound. The CSS hides "Add URL" (decision 9) and keeps Webamp inside the window. |
| `Player/WebampPlayer.cs` | The C# side of one Webamp instance: imports `wwwroot/js/webamp-host.js`, gives it the element and a .NET reference, sends the tracks, receives the events (`Closed`, `Minimized`, `TrackChanged`, `PlaylistChanged`). Implements `ITrackPlayer`. `DisposeAsync` stops the music and releases everything. |
| `Tracks/TrackLibrary.cs` | The files of Webamp's list (decision 14): read whole with `KazoFile.ReadAllBytesAsync`, handed to Webamp, forgotten when they leave the list. API for work items 2 to 5: `AddAsync(paths, play)`, `ReplaceAsync(paths, play)`, `Paths` (current list, in Webamp's order), `LoadedBytes` (for the 1 GB warning of work item 2), `Changed`. An unreadable file comes back in `TrackLoadResult.Failures`; the others are added. |
| `Tracks/ITrackPlayer.cs`, `TrackData.cs`, `TrackLoadResult.cs` | The contract between the library and Webamp (a fake in the tests); one track sent (key, name, type, bytes); the result of an add. |
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
                                                  OnTrackChanged, OnPlaylistChanged
```

| Call | Direction | Content |
| --- | --- | --- |
| `create(element, dotNet, { zIndex, skins, initialSkin })` | C# → JS | Loads `lib/webamp/webamp.bundle.min.mjs`, creates Webamp with `enableHotkeys: false` (decision 17), a `handleAddUrlEvent` that does nothing (decision 9), the skins of the user's folder (`availableSkins`) and the skin in use (`initialSkin`), draws it into the element. Returns a host object (an `IJSObjectReference`). A skin is `{ key, name, bytes }`; `key` is its file name in the folder. |
| `addSkin(skin, apply)` | C# → JS | Adds a skin to the Skins menu (or replaces the one with the same key, whose address is given back); with `apply`, puts it on and waits until Webamp drew it. |
| `addTracks(tracks, play)` | C# → JS | Adds at the end of the list; with `play`, the first one added plays. A track is `{ key, name, mimeType, bytes }`; `bytes` is the C# `byte[]`, received as a `Uint8Array`. |
| `replaceTracks(tracks, play)` | C# → JS | Replaces the list (`setTracksToPlay`); without `play`, Webamp is stopped. |
| `dispose()` | C# → JS | Stops the music, removes Webamp, gives every address back. |
| `OnPlaylistChanged(keys)` | JS → C# | The keys still in the list, in its order, on every real change (not on every tick of Webamp's clock). |
| `OnTrackChanged({ artist, title } \| null)` | JS → C# | The track that starts (for the window title of work item 5). |
| `OnWebampClosed()`, `OnWebampMinimized()` | JS → C# | Webamp's close and minimize buttons. |
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
  (`LaunchFile`) or in the window already open (`OnIntentReceivedAsync`, one
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
  the folder gives the base skin back. Work item 5 may take the skin into its
  `state.json`.
- **The import tip** (decision 20). On the first launch, a notification
  (`notifications.post`): "To change the skin: double-click a .wsz file, or
  right-click ▸ Skins ▸ Load Skin…". `importTipShown` becomes `true` once the
  notification is posted; when it is refused, it is tried again at the next
  launch. The same text is in the README.
- **Memory.** Skins are given to Webamp as `blob:` addresses created by the
  module and given back when a skin is replaced and on `dispose`.

## Tests

| Suite | Command | Content |
| --- | --- | --- |
| C# (xUnit) | `dotnet test Kazo.Webamp.slnx -c Release` | Manifest (with the `.wsz` type and the skins' capabilities); skins: `SkinArchive` (archive, `main.bmp` in any case and folder, not an archive, no `main.bmp`, too large), `SkinLibrary` (folder, sorted list and skipped files, copy, skin already in the folder, replacement, refusal without copy, safe name), `SkinController` (opened skin put on and remembered, skin from the computer, broken skin refused and current skin kept, menu choice and base skin, remembered skin gone, broken settings, tip once and again after a failure); `TrackLibrary`: add in order with the type, add at the end, unreadable file, nothing sent when nothing reads, track that left the list forgotten, Webamp's order, replace, list changes during an add, Webamp failing; audio types. |
| JavaScript | `npm test` | `webamp-host.js` with a fake Webamp: options, addresses created, kept while a batch arrives, given back when they leave the list, on replace and on `dispose`, deduplicated reports, events; skins at start (`availableSkins`, `initialSkin`), sorted add and put on, replacement, addresses given back. `webamp-skins.js` with a fake menu: items recognised by their place, "Load Skin…" taken from Webamp, cancel, choices reported, a dropped skin. |
| E2E (Playwright) | `npm run test:e2e` after the Release build | Webamp opens from the Applications menu in its window, without "Add URL"; an `.mp3` put in `~` with the explorer and opened with `launch ca.kazo.webamp /home/neo/tone.mp3` plays with its tags, and Webamp's close button closes the OS window; a missing file is reported. Skins (`skins.e2e.mjs`, skins made by `fixtures/make-skin.mjs`): the notification on the first launch, a `.wsz` opened with `launch` into the open Webamp is put on and listed in the Skins menu; "Load Skin…" puts a skin on and keeps it, a broken skin is refused with a message box and the skin stays, Webamp opened again starts with the skin and no new notification. |

The test `.mp3` (`tests/e2e/fixtures/tone.mp3`, 4 s, 16 KB) is made by
`tests/e2e/fixtures/make-tone.sh` (ffmpeg) and committed.

## Progress

| Work item | What this document describes |
| --- | --- |
| 1. `webamp/1-solution` | Solution, manifest, window, `WebampPlayer`, `TrackLibrary`, JavaScript module. |
| 4. `webamp/4-skins` | Skins: `.wsz` type, skins folder, "Load Skin…", remembered skin, import tip. |
