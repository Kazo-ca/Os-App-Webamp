> [Version française](../fr/plan.md)

# Plan: the Webamp app for Kazo OS

Plan **validated on 2026-11-06** by Benoit (thread "Concevoir l'app Webamp
avec Benoit", plan merged by PR #2). This document sets the goal, the decisions
and the order of the work; progress is at the end of the page.

## Goal

Webamp is a **Kazo OS music player** in the style of Winamp 2, built on
[Webamp](https://github.com/captbaritone/webamp) 2.3.1 (MIT). Music opens from
the OS as on a real system: double-click in the File Explorer, "Open with", the
file menu, the OS dialogs, and also from a **network drive** (a folder of the
computer mounted at `/mnt/<name>`, Kazo OS ADR 0181). Audio files, playlists and
skins are associated with the app.

It is the second Kazo OS Store app after Chess: a third-party app in its own
repository, following the `kazoos-solution` template.

## Decisions

Benoit's answers on 2026-11-06 ("ok" = the recommendation).

| # | Topic | Decision |
| --- | --- | --- |
| 1 | "Remote folder" | The network drive of ADR 0181: `/mnt/<name>`, Chrome and Edge on desktop. |
| 2 | Access to `/mnt` | OS change: the user's network drives count as their personal files (`filesystem.user-home.*`). [ADR 0231](https://github.com/Kazo-ca/Os/blob/main/doc/en/wasm/dev/adr/0231-network-drives-count-as-personal-files.md), issue [Os#381](https://github.com/Kazo-ca/Os/issues/381). On an OS that does not have it yet, a file of `/mnt` gives "Permission denied". |
| 3 | Repository base | `kazoos-solution` template on Kazo OS **3.0.0** now (the repository is empty, nothing to migrate); the submodule moves to the version that brings ADR 0231. |
| 4 | Audio formats | `.mp3 .wav .ogg .oga .opus .flac .m4a .aac .weba` (what browsers play). No other OS app opens audio: Webamp becomes the default app. |
| 5 | Opening while Webamp is open | One window (`SingleInstancePerUser`). Opening a file **adds it to the list and plays it**; a multiple selection is added whole. File menu entries: "Play in Webamp" and "Add to the Webamp list" (files and folders). |
| 6 | Display | An OS window that contains Webamp (`renderInto`), sized to its windows; its close and minimize buttons act on the OS window. Minimize waits for an SDK API ([Os#384](https://github.com/Kazo-ca/Os/issues/384), Benoit's answer A on 2026-11-06): until then only the OS minimize button minimizes; work item 7 wires Webamp's. Floating windows later: [Os#378](https://github.com/Kazo-ca/Os/issues/378). |
| 7 | Mobile | Desktop only for the first version. |
| 8 | Background playback | Plays while the window is minimized, stops when it closes. Window title "Artist - Title - Webamp". Media keys through Media Session. |
| 9 | Music from the internet | Not in the first version, "Add URL" hidden. Later: [#1](https://github.com/Kazo-ca/Os-App-Webamp/issues/1). |
| 10 | Playlists | Open `.m3u`, `.m3u8`, `.pls` (relative paths resolved from the list's folder); save as UTF-8 `.m3u8` with relative paths. The three types are associated. "Load list" and "Save list" use the OS dialogs. |
| 11 | Skins | Webamp's default skin only. Opening a `.wsz` (double-click) applies it and copies it to `~/.local/share/webamp/skins/`; the Skins menu lists the copied skins. Webamp's "Skins ▸ Load Skin…" (file of the computer) keeps the skin too. `.wsz` associated. **The user is told how to import a skin** (see 20). |
| 12 | Milkdrop | Included, downloaded only when its window first opens (Butterchurn, 2 MB). |
| 13 | What is remembered | Everything: list, track and position, volume, balance, equalizer, skin, position of Webamp's windows, double size. In `~/.config/ca.kazo.webamp/`. |
| 14 | From the file to Webamp | One `Blob` per track, read through `KazoFile` when the track is added, released when it leaves the list; a warning above 1 GB in total. Later, an address served on demand by the OS: [Os#379](https://github.com/Kazo-ca/Os/issues/379). |
| 15 | Dragging from the File Explorer | Not in the first version (dragging from the computer already works, Webamp handles it). Public contract later: [Os#380](https://github.com/Kazo-ca/Os/issues/380). |
| 16 | "Open" and "Add folder" | OS dialogs; a folder adds its audio files and those of its subfolders, sorted by path. |
| 17 | Keyboard shortcuts | Webamp's only while its window has the focus (the `enableHotkeys` option listens to the whole page and would steal other apps' keys). |
| 18 | Identity | Id `ca.kazo.webamp`, name "Webamp", publisher `pub.kazo`, category `multimedia`, version `1.0.0`, MIT license. |
| 19 | Documents | This plan in this repository (fr + en); ADR 0231 in Kazo OS. |
| 20 | Explaining how to import a skin | Recommendation taken (Benoit started the implementation without another choice): a notification at first launch ("To change skin: double-click a .wsz file, or right-click ▸ Skins ▸ Load Skin…") and the same explanation in the Kazo OS Store listing and the README. |

## The app

| Item | Value |
| --- | --- |
| Manifest | `[AppManifest]` `WebampApp`: window, desktop only, `SingleInstancePerUser`. |
| Capabilities | `filesystem.user-home.read` and `.write` (music, lists, copied skins), `filesystem.private.read` and `.write` (`~/.config/ca.kazo.webamp/`), `dialogs.file-open`, `dialogs.file-save`, `dialogs.folder-select`, `shell.context-menu` (file menu entries), `notifications.post` (skins explanation). No dangerous capability. |
| Associated types | Audio: `audio/mpeg` `.mp3`, `audio/wav` `.wav`, `audio/ogg` `.ogg .oga`, `audio/opus` `.opus`, `audio/flac` `.flac`, `audio/mp4` `.m4a`, `audio/aac` `.aac`, `audio/webm` `.weba`. Lists: `audio/x-mpegurl` `.m3u`, `application/vnd.apple.mpegurl` `.m3u8`, `audio/x-scpls` `.pls`. Skins: `application/x-winamp-skin` `.wsz`. |
| Menu entries | "Play in Webamp" and "Add to the Webamp list" on audio files, lists and folders. |
| Webamp | Bundle `webamp.lazy-bundle.min.mjs` 2.3.1 and Butterchurn in `wwwroot/lib/`; a `webamp-host.js` module that creates the instance, hands tracks over as `Blob`s and raises events to C#. C# keeps the logic: lists, paths, state, intents. |
| Window | Content sized to Webamp's windows (player, equalizer, list stacked: 275×348 at normal size), resizable. `onClose` closes the OS window, `onMinimize` minimizes it. |
| Shortcuts | The page listener of `enableHotkeys` is captured when Webamp is created and called only while the window has the focus (never on the whole page): Webamp's shortcuts only act while the window has the focus. |
| State | `~/.config/ca.kazo.webamp/state.json`: list (paths), track, position, volume, balance, equalizer, skin, window layout, double size. A missing file stays in the list and is skipped when played, as in Winamp. |

## Work, in order

Series `webamp/base` (one branch `webamp/<n>-<topic>` and one PR per work item).

0. **Kazo OS prerequisite**: ADR 0231 coded ([Os#381](https://github.com/Kazo-ca/Os/issues/381)) and released in an OS version. Work items 1 to 6 do not depend on it, except the test on `/mnt`.
1. `webamp/1-solution`: `kazoos-solution` solution on 3.0.0, manifest, Webamp in `wwwroot/`, window that contains Webamp, close and minimize, `THIRD-PARTY-NOTICES.md`.
2. `webamp/2-open-files`: associated types, one window, open = add and play, `Blob`s read and released, 1 GB warning, OS dialogs for "Open" and "Add folder", file menu entries.
3. `webamp/3-playlists`: reading `.m3u`, `.m3u8`, `.pls`, writing `.m3u8`, "Load list" and "Save list".
4. `webamp/4-skins`: `.wsz` associated, copy to `~/.local/share/webamp/skins/`, Skins menu, "Load Skin…" kept, import explanation (question 20).
5. `webamp/5-state-and-media`: remembered state, window title, Media Session, shortcuts on focus, Milkdrop loaded on demand.
6. `webamp/6-store`: `store/` folder (icon, fr and en descriptions with the skins explanation, screenshots, `multimedia` category), tests, tag `v1.0.0`, PR to the catalog.
7. When the OS releases ADR 0231: the submodule moves to that version, test of a track and a list on a network drive.

## Tests

As for Chess: C# tests (xUnit) for lists (`.m3u`, `.m3u8`, `.pls`, relative paths, encodings), state, intents (add and play, multiple selection), the 1 GB limit; `node --test` tests of the JavaScript module; Playwright journeys in the host (open an `.mp3` from the File Explorer, two files at once, save then reopen a list, apply a `.wsz`, get the state back after a reload).

## Licenses

- This repository: MIT.
- Webamp 2.3.1: MIT. Butterchurn and its presets: MIT. The default skin is the
  one Webamp distributes; its origin is checked and written in
  `THIRD-PARTY-NOTICES.md` in work item 1. No community skin is shipped.

## Later

- Music from the internet: [#1](https://github.com/Kazo-ca/Os-App-Webamp/issues/1).
- Floating windows on the desktop: [Os#378](https://github.com/Kazo-ca/Os/issues/378).
- File address served on demand (no copy in memory): [Os#379](https://github.com/Kazo-ca/Os/issues/379).
- Dragging files from the File Explorer to Webamp: [Os#380](https://github.com/Kazo-ca/Os/issues/380).
- Mobile.

## Progress

On 2026-11-06, Benoit started the implementation in several parallel sessions.
Series `webamp/base` (created from `main`), one PR per work item to
`webamp/base`, then one PR `webamp/base` → `main` that Benoit merges.

| Work item | Branch | Depends on | State |
| --- | --- | --- | --- |
| Kazo OS: ADR 0231 coded ([Os#381](https://github.com/Kazo-ca/Os/issues/381)) | `network-drives/app-access` (Kazo-ca/Os repository, PR to `main`) | nothing | To do |
| 1. Solution, manifest, window that contains Webamp, tracks loaded as `Blob`s | `webamp/1-solution` | nothing | Done ([architecture](architecture.md)) |
| 2. Open from the OS: audio types, one window, add and play, menus, dialogs, 1 GB | `webamp/2-open-files` | 1 | To do |
| 3. `.m3u`, `.m3u8`, `.pls` lists | `webamp/3-playlists` | 1 | Done ([architecture](architecture.md#playlists)) |
| 4. `.wsz` skins and the import explanation | `webamp/4-skins` | 1 | To do |
| 5. Remembered state, title, Media Session, shortcuts, Milkdrop | `webamp/5-state-and-media` | 1 | Done ([architecture](architecture.md#state-title-shortcuts-milkdrop)) |
| 6. Store listing, end-to-end tests, `v1.0.0` | `webamp/6-store` | 2 to 5 | To do |
| 7. Submodule on the OS version with ADR 0231, test on `/mnt`; Webamp's minimize button once [Os#384](https://github.com/Kazo-ca/Os/issues/384) is released | `webamp/7-network-drive` | Os#381 released, 1 | To do |
