> [Version française](readme-fr.md)

# Os-App-Webamp

Music player for [Kazo OS](https://github.com/Kazo-ca/Os) in the style of Winamp 2, built on
[Webamp](https://github.com/captbaritone/webamp) (MIT). Audio files, playlists (`.m3u`, `.m3u8`,
`.pls`) and skins (`.wsz`) open from the OS, including from a network drive.

The plan is validated; the implementation runs in the `webamp/base` series.

- Plan: [English](doc/en/plan.md) · [Français](doc/fr/plan.md)
- Architecture: [English](doc/en/architecture.md) · [Français](doc/fr/architecture.md)
- License: MIT ([LICENSE](LICENSE)); bundled works and the default skin: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Build and run

The repository was made with the Kazo OS `kazoos-solution` template, on Kazo OS 3.0.0
(`external/KazoOs`, a submodule on the `v3.0.0` tag).

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
```
