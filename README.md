> [Version française](readme-fr.md)

# Os-App-Webamp

Music player for [Kazo OS](https://github.com/Kazo-ca/Os) in the style of Winamp 2, built on
[Webamp](https://github.com/captbaritone/webamp) (MIT). Audio files, playlists (`.m3u`, `.m3u8`,
`.pls`) and skins (`.wsz`) open from the OS, including from a network drive.

The plan is validated; the implementation runs in the `webamp/base` series.

- Plan: [English](doc/en/plan.md) · [Français](doc/fr/plan.md)
- Architecture: [English](doc/en/architecture.md) · [Français](doc/fr/architecture.md)
- License: MIT ([LICENSE](LICENSE)); bundled works and the default skin: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Change the skin

To change the skin: **double-click a `.wsz` file**, or **right-click Webamp ▸
Skins ▸ Load Skin…** (a file of the computer). The skin is put on at once and
kept in `~/.local/share/webamp/skins/`: Webamp's Skins menu lists every kept
skin, and Webamp starts again with the last one chosen. A file that is not a
Winamp skin is refused with a message; the current skin stays. Webamp says so
in a notification on its first launch.

## Build and run

The repository was made with the Kazo OS `kazoos-solution` template, now on Kazo OS 3.1.0
(`external/KazoOs`, a submodule on the `v3.1.0` tag).

```bash
git clone --recurse-submodules https://github.com/Kazo-ca/Os-App-Webamp.git
cd Os-App-Webamp
dotnet run --project src/Kazo.Webamp.Os      # Kazo OS with Webamp, on http://localhost:5290
```

`Debug` builds Kazo OS from the submodule's sources; `Release` uses the `Kazo.Os.*` packages of
nuget.org, at the submodule's version. Never modify the submodule: CI refuses it.

## Tests

```bash
dotnet build Kazo.Webamp.slnx -c Release
dotnet test Kazo.Webamp.slnx -c Release --no-build   # C# (xUnit)
npm ci
npm test                                            # the JavaScript module (node --test)
npm run test:e2e                                    # Webamp inside the Kazo OS host (Playwright, Chromium)
npm run test:installed -- <Kazo OS wwwroot> <packages>   # Webamp installed by kazopkg in the trimmed OS
npm run store:screenshots                           # the Kazo OS Store screenshots (store/screenshots)
```

`test:installed` needs a Kazo OS publish (`dotnet publish external/KazoOs/wasm2/KazoOs/OS/Kazo.Os.Ecosystem -c Release`,
its `wwwroot`) and Webamp's package (`dotnet build src/Kazo.Webamp -c Release -t:KazoOsPack`, folder
`src/Kazo.Webamp/bin/kazoos-packages`). See [architecture](doc/en/architecture.md#kazo-os-store-listing-and-installed-app).

## Install

Once released: from the Kazo OS Store (category Multimedia), or `sudo kazopkg install Kazo-ca/Os-App-Webamp`
(this repository's `kazoos-packages` branch). Webamp needs Kazo OS 3.1.0 or later.
