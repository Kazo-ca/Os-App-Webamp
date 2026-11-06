# Webamp

Winamp 2's music player, in a Kazo OS window. Built on [Webamp](https://github.com/captbaritone/webamp)
by Jordan Eldredge: player, equalizer, playlist and the Milkdrop visualizer, as back in the day.

## Play your files

- **Double-click a music file** in the File Explorer: it is added to the list and plays. Webamp
  plays `.mp3`, `.wav`, `.ogg`, `.oga`, `.opus`, `.flac`, `.m4a`, `.aac` and `.weba`.
- Right-click files or a **folder**: "Play in Webamp" or "Add to Webamp's list" (a folder adds its
  music and the music of its subfolders, sorted by path).
- Webamp's "Open", "Add file" and "Add dir" show the Kazo OS dialogs.
- One window: what you open is added to the current list.

## Playlists

- Open `.m3u`, `.m3u8` and `.pls` lists (relative paths included); save yours as `.m3u8` with
  "Save list".

## Skins

To change the skin: **double-click a `.wsz` file**, or **right-click ▸ Skins ▸ Load Skin…** in
Webamp. The skin is applied and kept in `~/.local/share/webamp/skins/`: it then shows in the Skins
menu. A notification says so on the first launch.

## Also

- Webamp comes back as you left it: list, track and position, volume, balance, equalizer, skin, the
  place of its windows and double size.
- The window's title follows the track ("Artist - Title - Webamp"); the keyboard's media keys work.
- Webamp's shortcuts only act while its window has the focus.
- **Milkdrop** (Butterchurn, 2 MB) is downloaded only when its window first opens.

## Limits of the first version

- Desktop only (no phone version).
- No music from the internet ("Add URL" is hidden).
- Webamp's minimize button does nothing: use the Kazo OS window's.
- Music on network drives (`/mnt`) needs a coming Kazo OS version.
- With Kazo OS 3.0.0, sign out and in again after installing, before opening music: the OS gives a
  new app its access only at the next sign-in.

## Licenses

Webamp (the app) is free software under the MIT license. It bundles Webamp 2.3.1 (MIT), Butterchurn
and its presets (MIT). The default skin is the **Winamp 2.91 base skin**, as Webamp distributes it:
its artwork is Nullsoft's and not under MIT (see `THIRD-PARTY-NOTICES.md` in the repository). No
community skin is included.
