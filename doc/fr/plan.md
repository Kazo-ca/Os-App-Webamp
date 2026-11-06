> [English version](../en/plan.md)

# Plan : l'app Webamp pour Kazo OS

Plan **validé le 2026-11-06** par Benoit (fil « Concevoir l'app Webamp avec
Benoit », plan fusionné par la PR #2). Ce document fixe le but, les décisions et
l'ordre des choses à faire ; l'avancement est en fin de page.

## But

Webamp est un **lecteur de musique de Kazo OS** dans le style de Winamp 2,
construit sur [Webamp](https://github.com/captbaritone/webamp) 2.3.1 (MIT). On
ouvre une musique depuis l'OS comme sur un vrai système : double-clic dans
l'explorateur, « Ouvrir avec », menu des fichiers, dialogues de l'OS, et aussi
depuis un **lecteur réseau** (un dossier de l'ordinateur monté dans
`/mnt/<nom>`, ADR 0181 de Kazo OS). Les fichiers audio, les listes de lecture et
les skins sont associés à l'app.

C'est la deuxième app du Kazo OS Store après Chess : une app tierce dans son
propre dépôt, qui suit le modèle `kazoos-solution`.

## Décisions

Réponses de Benoit le 2026-11-06 (« ok » = la recommandation).

| # | Sujet | Décision |
| --- | --- | --- |
| 1 | « Remote folder » | Le lecteur réseau de l'ADR 0181 : `/mnt/<nom>`, Chrome et Edge sur ordinateur. |
| 2 | Accès à `/mnt` | Changement de l'OS : les lecteurs réseau de l'utilisateur comptent comme ses fichiers personnels (`filesystem.user-home.*`). [ADR 0231](https://github.com/Kazo-ca/Os/blob/main/doc/fr/wasm/dev/adr/0231-network-drives-count-as-personal-files.md), issue [Os#381](https://github.com/Kazo-ca/Os/issues/381). Sur un OS qui ne l'a pas encore, un fichier de `/mnt` donne « Permission refusée ». |
| 3 | Base du dépôt | Modèle `kazoos-solution` sur Kazo OS **3.0.0** maintenant (le dépôt est vide, rien à migrer) ; le sous-module avance à la version qui apporte l'ADR 0231. |
| 4 | Formats audio | `.mp3 .wav .ogg .oga .opus .flac .m4a .aac .weba` (ce que lisent les navigateurs). Aucune autre app de l'OS n'ouvre l'audio : Webamp devient l'app par défaut. |
| 5 | Ouvrir quand Webamp est ouvert | Une seule fenêtre (`SingleInstancePerUser`). Ouvrir un fichier **l'ajoute à la liste et le joue** ; une sélection multiple s'ajoute en entier. Entrées du menu des fichiers : « Lire dans Webamp » et « Ajouter à la liste de Webamp » (fichiers et dossiers). |
| 6 | Affichage | Une fenêtre de l'OS qui contient Webamp (`renderInto`), à la taille de ses fenêtres ; ses boutons fermer et réduire agissent sur la fenêtre de l'OS. Réduire attend une API du SDK ([Os#384](https://github.com/Kazo-ca/Os/issues/384), réponse A de Benoit le 2026-11-06) : d'ici là, seul le bouton réduire de l'OS réduit ; le chantier 7 branche celui de Webamp. Fenêtres flottantes plus tard : [Os#378](https://github.com/Kazo-ca/Os/issues/378). |
| 7 | Mobile | Ordinateur seulement pour la première version. |
| 8 | Lecture en arrière-plan | Joue quand la fenêtre est réduite, s'arrête à la fermeture. Titre de la fenêtre « Artiste - Titre - Webamp ». Touches multimédia par Media Session. |
| 9 | Musique depuis internet | Pas dans la première version, « Add URL » masqué. Plus tard : [#1](https://github.com/Kazo-ca/Os-App-Webamp/issues/1). |
| 10 | Listes de lecture | Ouvrir `.m3u`, `.m3u8`, `.pls` (chemins relatifs résolus depuis le dossier de la liste) ; enregistrer en `.m3u8` UTF-8, chemins relatifs. Les trois types sont associés. « Load list » et « Save list » passent par les dialogues de l'OS. |
| 11 | Skins | Skin par défaut de Webamp seulement. Ouvrir un `.wsz` (double-clic) l'applique et le copie dans `~/.local/share/webamp/skins/` ; le menu Skins liste les skins copiés. « Skins ▸ Load Skin… » de Webamp (fichier de l'ordinateur) garde aussi le skin. `.wsz` associé. **On explique à l'utilisateur comment importer un skin** (voir 20). |
| 12 | Milkdrop | Inclus, téléchargé seulement à la première ouverture de sa fenêtre (Butterchurn, 2 Mo). |
| 13 | Ce qui est retenu | Tout : liste, morceau et position, volume, balance, égaliseur, skin, position des fenêtres de Webamp, taille double. Dans `~/.config/ca.kazo.webamp/`. |
| 14 | Du fichier à Webamp | Un `Blob` par morceau, lu par `KazoFile` quand le morceau est ajouté, libéré quand il sort de la liste ; avertissement au-delà de 1 Go au total. Plus tard, une adresse servie à la demande par l'OS : [Os#379](https://github.com/Kazo-ca/Os/issues/379). |
| 15 | Glisser depuis l'explorateur | Pas dans la première version (le glisser depuis l'ordinateur marche déjà, Webamp le gère). Contrat public plus tard : [Os#380](https://github.com/Kazo-ca/Os/issues/380). |
| 16 | « Ouvrir » et « Ajouter un dossier » | Dialogues de l'OS ; un dossier ajoute ses fichiers audio et ceux de ses sous-dossiers, triés par chemin. |
| 17 | Raccourcis clavier | Ceux de Webamp seulement quand sa fenêtre a le focus (l'option `enableHotkeys` écoute toute la page et volerait les touches des autres apps). |
| 18 | Identité | Id `ca.kazo.webamp`, nom « Webamp », éditeur `pub.kazo`, catégorie `multimedia`, version `1.0.0`, licence MIT. |
| 19 | Documents | Ce plan dans ce dépôt (fr + en) ; l'ADR 0231 dans Kazo OS. |
| 20 | Expliquer l'import d'un skin | Recommandation retenue (Benoit a lancé l'implémentation sans autre choix) : une notification au premier lancement (« Pour changer de skin : double-cliquez un fichier .wsz, ou clic droit ▸ Skins ▸ Load Skin… ») et la même explication dans la fiche du Kazo OS Store et le README. |

## L'app

| Élément | Valeur |
| --- | --- |
| Manifeste | `[AppManifest]` `WebampApp` : fenêtre, ordinateur seulement, `SingleInstancePerUser`. |
| Capacités | `filesystem.user-home.read` et `.write` (musique, listes, skins copiés), `filesystem.private.read` et `.write` (`~/.config/ca.kazo.webamp/`), `dialogs.file-open`, `dialogs.file-save`, `dialogs.folder-select`, `shell.context-menu` (entrées du menu des fichiers), `notifications.post` (explication des skins). Aucune capacité dangereuse. |
| Types associés | Audio : `audio/mpeg` `.mp3`, `audio/wav` `.wav`, `audio/ogg` `.ogg .oga`, `audio/opus` `.opus`, `audio/flac` `.flac`, `audio/mp4` `.m4a`, `audio/aac` `.aac`, `audio/webm` `.weba`. Listes : `audio/x-mpegurl` `.m3u`, `application/vnd.apple.mpegurl` `.m3u8`, `audio/x-scpls` `.pls`. Skins : `application/x-winamp-skin` `.wsz`. |
| Entrées de menu | « Lire dans Webamp » et « Ajouter à la liste de Webamp » sur les fichiers audio, les listes et les dossiers. |
| Webamp | Bundle `webamp.lazy-bundle.min.mjs` 2.3.1 et Butterchurn dans `wwwroot/lib/` ; un module `webamp-host.js` qui crée l'instance, donne les morceaux en `Blob` et remonte les événements au C#. Le C# garde la logique : listes, chemins, état, intents. |
| Fenêtre | Contenu à la taille des fenêtres de Webamp (lecteur, égaliseur, liste empilés : 275×348 en taille normale), redimensionnable. `onClose` ferme la fenêtre de l'OS, `onMinimize` la réduit. |
| Raccourcis | `enableHotkeys` désactivé ; les raccourcis de Webamp n'agissent que quand la fenêtre a le focus. |
| État | `~/.config/ca.kazo.webamp/state.json` : liste (chemins), morceau, position, volume, balance, égaliseur, skin, disposition des fenêtres, taille double. Un fichier disparu reste dans la liste et est sauté à la lecture, comme dans Winamp. |

## Choses à faire, dans l'ordre

Série `webamp/base` (une branche `webamp/<n>-<sujet>` et une PR par chantier).

0. **Prérequis Kazo OS** : l'ADR 0231 codée ([Os#381](https://github.com/Kazo-ca/Os/issues/381)) et publiée dans une version de l'OS. Les chantiers 1 à 6 n'en dépendent pas, sauf l'essai sur `/mnt`.
1. `webamp/1-solution` : solution `kazoos-solution` sur 3.0.0, manifeste, Webamp dans `wwwroot/`, fenêtre qui contient Webamp, fermer et réduire, `THIRD-PARTY-NOTICES.md`.
2. `webamp/2-open-files` : types associés, une seule fenêtre, ouvrir = ajouter et jouer, `Blob` lus et libérés, avertissement 1 Go, dialogues de l'OS pour « Ouvrir » et « Ajouter un dossier », entrées du menu des fichiers.
3. `webamp/3-playlists` : lecture `.m3u`, `.m3u8`, `.pls`, écriture `.m3u8`, « Load list » et « Save list ».
4. `webamp/4-skins` : `.wsz` associé, copie dans `~/.local/share/webamp/skins/`, menu Skins, « Load Skin… » gardé, explication de l'import (question 20).
5. `webamp/5-state-and-media` : état retenu, titre de la fenêtre, Media Session, raccourcis au focus, Milkdrop chargé à la demande.
6. `webamp/6-store` : dossier `store/` (icône, descriptions fr et en avec l'explication des skins, captures, catégorie `multimedia`), tests, tag `v1.0.0`, PR au catalogue.
7. Quand l'OS publie l'ADR 0231 : le sous-module avance à cette version, essai d'une musique et d'une liste sur un lecteur réseau.

## Tests

Comme Chess : tests C# (xUnit) pour les listes (`.m3u`, `.m3u8`, `.pls`, chemins relatifs, encodages), l'état, les intents (ajouter et jouer, sélection multiple), la limite de 1 Go ; tests `node --test` du module JavaScript ; parcours Playwright dans l'hôte (ouvrir un `.mp3` de l'explorateur, deux fichiers d'un coup, enregistrer puis rouvrir une liste, appliquer un `.wsz`, retrouver l'état après un rechargement).

## Licences

- Ce dépôt : MIT.
- Webamp 2.3.1 : MIT. Butterchurn et ses préréglages : MIT. Le skin par défaut
  est celui que Webamp distribue ; sa provenance est vérifiée et notée dans
  `THIRD-PARTY-NOTICES.md` au chantier 1. Aucun skin de la communauté n'est
  livré.

## Plus tard

- Musique depuis internet : [#1](https://github.com/Kazo-ca/Os-App-Webamp/issues/1).
- Fenêtres flottantes sur le bureau : [Os#378](https://github.com/Kazo-ca/Os/issues/378).
- Adresse de fichier servie à la demande (sans copie en mémoire) : [Os#379](https://github.com/Kazo-ca/Os/issues/379).
- Glisser des fichiers de l'explorateur vers Webamp : [Os#380](https://github.com/Kazo-ca/Os/issues/380).
- Mobile.

## Avancement

Le 2026-11-06, Benoit a lancé l'implémentation en plusieurs sessions en
parallèle. Série `webamp/base` (créée depuis `main`), une PR par chantier vers
`webamp/base`, puis une PR `webamp/base` → `main` que Benoit fusionne.

| Chantier | Branche | Dépend de | État |
| --- | --- | --- | --- |
| Kazo OS : ADR 0231 codée ([Os#381](https://github.com/Kazo-ca/Os/issues/381)) | `network-drives/app-access` (dépôt Kazo-ca/Os, PR vers `main`) | rien | À faire |
| 1. Solution, manifeste, fenêtre qui contient Webamp, chargement des morceaux en `Blob` | `webamp/1-solution` | rien | Fait ([architecture](architecture.md)) |
| 2. Ouvrir depuis l'OS : types audio, une seule fenêtre, ajouter et jouer, menus, dialogues, 1 Go | `webamp/2-open-files` | 1 | À faire |
| 3. Listes `.m3u`, `.m3u8`, `.pls` | `webamp/3-playlists` | 1 | Fait ([architecture](architecture.md#listes-de-lecture)) |
| 4. Skins `.wsz` et explication de l'import | `webamp/4-skins` | 1 | Fait ([architecture](architecture.md#skins)) |
| 5. État retenu, titre, Media Session, raccourcis, Milkdrop | `webamp/5-state-and-media` | 1 | À faire |
| 6. Fiche du Store, tests de bout en bout, `v1.0.0` | `webamp/6-store` | 2 à 5 | À faire |
| 7. Sous-module sur la version de l'OS avec l'ADR 0231, essai sur `/mnt` ; bouton réduire de Webamp si [Os#384](https://github.com/Kazo-ca/Os/issues/384) est publiée | `webamp/7-network-drive` | Os#381 publiée, 1 | À faire |
