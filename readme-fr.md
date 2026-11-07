> [English version](README.md)

# Os-App-Webamp

Lecteur de musique pour [Kazo OS](https://github.com/Kazo-ca/Os) dans le style de Winamp 2, construit
sur [Webamp](https://github.com/captbaritone/webamp) (MIT). Les fichiers audio, les listes de lecture
(`.m3u`, `.m3u8`, `.pls`) et les skins (`.wsz`) s'ouvrent depuis l'OS, y compris depuis un lecteur
réseau.

Le plan est validé ; l'implémentation se fait dans la série `webamp/base`.

- Plan : [Français](doc/fr/plan.md) · [English](doc/en/plan.md)
- Architecture : [Français](doc/fr/architecture.md) · [English](doc/en/architecture.md)
- Licence : MIT ([LICENSE](LICENSE)) ; œuvres incluses et skin par défaut : [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Changer de skin

Pour changer de skin : **double-cliquez un fichier `.wsz`**, ou **clic droit sur
Webamp ▸ Skins ▸ Load Skin…** (un fichier de l'ordinateur). Le skin est mis tout
de suite et gardé dans `~/.local/share/webamp/skins/` : le menu Skins de Webamp
liste tous les skins gardés, et Webamp redémarre avec le dernier choisi. Un
fichier qui n'est pas un skin Winamp est refusé avec un message ; le skin en
cours reste. Webamp le rappelle par une notification à son premier lancement.

## Compiler et lancer

Le dépôt a été créé avec le modèle `kazoos-solution` de Kazo OS, aujourd'hui sur Kazo OS 3.1.0
(`external/KazoOs`, un sous-module sur le tag `v3.1.0`).

```bash
git clone --recurse-submodules https://github.com/Kazo-ca/Os-App-Webamp.git
cd Os-App-Webamp
dotnet run --project src/Kazo.Webamp.Os      # Kazo OS avec Webamp, sur http://localhost:5290
```

`Debug` compile Kazo OS depuis les sources du sous-module ; `Release` prend les paquets `Kazo.Os.*` de
nuget.org, à la version du sous-module. Ne modifiez jamais le sous-module : la CI le refuse.

## Tests

```bash
dotnet build Kazo.Webamp.slnx -c Release
dotnet test Kazo.Webamp.slnx -c Release --no-build   # C# (xUnit)
npm ci
npm test                                            # le module JavaScript (node --test)
npm run test:e2e                                    # Webamp dans l'hôte Kazo OS (Playwright, Chromium)
npm run test:installed -- <wwwroot de Kazo OS> <paquets>   # Webamp installé par kazopkg dans l'OS rogné
npm run store:screenshots                           # les captures du Kazo OS Store (store/screenshots)
```

`test:installed` demande une publication de Kazo OS (`dotnet publish external/KazoOs/wasm2/KazoOs/OS/Kazo.Os.Ecosystem -c Release`,
son `wwwroot`) et le paquet de Webamp (`dotnet build src/Kazo.Webamp -c Release -t:KazoOsPack`, dossier
`src/Kazo.Webamp/bin/kazoos-packages`). Voir l'[architecture](doc/fr/architecture.md#fiche-du-kazo-os-store-et-app-installée).

## Installer

Une fois publié : depuis le Kazo OS Store (catégorie Multimédia), ou `sudo kazopkg install Kazo-ca/Os-App-Webamp`
(branche `kazoos-packages` de ce dépôt). Webamp demande Kazo OS 3.1.0 ou plus récent.
