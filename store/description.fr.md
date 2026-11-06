# Webamp

Le lecteur de musique de Winamp 2, dans une fenêtre de Kazo OS. Construit sur
[Webamp](https://github.com/captbaritone/webamp) de Jordan Eldredge : lecteur, égaliseur, liste de
lecture et visualisation Milkdrop, comme à l'époque.

## Écouter vos fichiers

- **Double-cliquez une musique** dans l'explorateur de fichiers : elle s'ajoute à la liste et joue.
  Webamp lit `.mp3`, `.wav`, `.ogg`, `.oga`, `.opus`, `.flac`, `.m4a`, `.aac` et `.weba`.
- Clic droit sur des fichiers ou un **dossier** : « Lire dans Webamp » ou « Ajouter à la liste de
  Webamp » (un dossier ajoute ses musiques et celles de ses sous-dossiers, triées par chemin).
- « Open », « Add file » et « Add dir » de Webamp ouvrent les dialogues de Kazo OS.
- Une seule fenêtre : ce que vous ouvrez s'ajoute à la liste en cours.

## Listes de lecture

- Ouvrez des listes `.m3u`, `.m3u8` et `.pls` (chemins relatifs compris) ; enregistrez la vôtre en
  `.m3u8` par « Save list ».

## Skins

Pour changer de skin : **double-cliquez un fichier `.wsz`**, ou **clic droit ▸ Skins ▸ Load
Skin…** dans Webamp. Le skin est appliqué et gardé dans `~/.local/share/webamp/skins/` : il apparaît
ensuite dans le menu Skins. Une notification le rappelle au premier lancement.

## Et aussi

- Webamp reprend comme vous l'avez laissé : liste, morceau et position, volume, balance, égaliseur,
  skin, place de ses fenêtres et taille double.
- Le titre de la fenêtre suit le morceau (« Artiste - Titre - Webamp ») ; les touches multimédia du
  clavier fonctionnent.
- Les raccourcis de Webamp n'agissent que quand sa fenêtre a le focus.
- **Milkdrop** (Butterchurn, 2 Mo) n'est téléchargé qu'à la première ouverture de sa fenêtre.

## Limites de la première version

- Ordinateur seulement (pas de version téléphone).
- Pas de musique depuis internet (« Add URL » est masqué).
- Le bouton réduire de Webamp ne fait rien : utilisez celui de la fenêtre de Kazo OS.
- Les musiques des lecteurs réseau (`/mnt`) demandent une version de Kazo OS à venir.
- Avec Kazo OS 3.0.0, après l'installation, déconnectez-vous et reconnectez-vous avant d'ouvrir de
  la musique : l'OS ne donne ses accès à une nouvelle app qu'à la connexion suivante.

## Licences

Webamp (l'app) est un logiciel libre sous licence MIT. Il embarque Webamp 2.3.1 (MIT), Butterchurn
et ses préréglages (MIT). Le skin par défaut est le **skin de base de Winamp 2.91**, tel que Webamp
le distribue : son dessin appartient à Nullsoft et n'est pas sous MIT (voir
`THIRD-PARTY-NOTICES.md` dans le dépôt). Aucun skin de la communauté n'est fourni.
