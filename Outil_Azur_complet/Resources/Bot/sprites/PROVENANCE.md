# Sprites d'acteurs du client fourni

Ce dossier est copié à côté de l'exécutable dans `ressources/Bot/sprites` (cibles `CopyBotSpellAssets`
pour les PNG et `CopyBotSpriteAnchors` pour `ancres.tsv` dans `Outil_Azur_complet.csproj`).

## Deux générations de fichiers

| Fichiers | Origine | Utilisés par |
| --- | --- | --- |
| `<gfx><O>.png` (2 304, ex. `1001R.png`) et les quelques `1.png`, `1381k.png`, `7029c.png`… | Arrivés avec la fusion du 3 octobre 2026, outil et réglages non documentés ; même contenu que la **première** image des symboles `static<O>`, recadrée, sans ancre. | Chargeur actuel (`UserMapControl.LoadSprite`). Conservés tels quels pour ne rien casser. |
| `<gfx>_static<O>.png`, `<gfx>_scene.png`, `<gfx>_walk<O>.png`, `<gfx>_run<O>.png`, `ancres.tsv` | Générés par `tools/client-analysis/exporter_sprites.py` (commande ci-dessous). | Rendu des acteurs (lot M1) : nouveau nom d'abord, ancien nom en repli. |

## Source et commande exacte

Source : `clips/sprites/<gfx>.swf` du client Dofus 1.34 remis par l'utilisateur
(`04 - Dofus 1.34 - Qu'Tan et Ilyzaelle`, 933 SWF), non versionné. Outils du dépôt : `swfsvg` 0.2.1
(rendu SVG des symboles, image par image ; la 0.2.0 appliquait deux fois la pose du clip aux dégradés
et aux bitmaps) puis cairosvg 2.9 et Pillow 12 (PNG). Depuis la racine du dépôt :

```sh
(cd tools/client-analysis/swfsvg && cargo build --release)
python3 tools/client-analysis/exporter_sprites.py "<client>/clips/sprites" \
    Outil_Azur_complet/Resources/Bot/sprites \
    --swfsvg tools/client-analysis/swfsvg/target/release/swfsvg --jobs 4
```

Mesuré le 4 octobre 2026 (conteneur 4 cœurs) : 3 min 10 s, 933 SWF, 2 556 PNG pour 16,1 Mo,
un seul message (`7003`, voir plus bas). Pour un seul gfx : `--gfx 1001` (seules ses lignes
d'`ancres.tsv` et ses PNG sont remplacés). Pour donner des cycles de marche à un autre gfx
(monstre, PNJ), l'ajouter à `sprites_animes.txt` puis relancer avec `--gfx <id>`.

## `ancres.tsv`

UTF-8, tabulations, une ligne d'en-tête, une ligne par PNG (`<gfx>_<anim>.png`) :

| colonne | contenu |
| --- | --- |
| `gfx` | numéro du SWF (`clips/sprites/<gfx>.swf`) |
| `anim` | `staticS` … `staticB`, `walkS` … `walkB`, `runS` … `runB`, ou `scene` |
| `xmin`, `ymin` | position du coin haut-gauche du PNG par rapport au point d'ancrage du client (pied du personnage), en pixels ; le point d'ancrage est donc le pixel (`-xmin`, `-ymin`) de chaque image |
| `largeur`, `hauteur` | taille d'**une** image |
| `images` | nombre d'images de la bande (1 pour `static` et `scene`) ; l'image k (à partir de 0) occupe les colonnes `[k × largeur, (k + 1) × largeur)` |

Pour poser un sprite sur la cellule dont le point d'ancrage est (`ax`, `ay`) : coin du PNG en
(`ax + xmin`, `ay + ymin`). Pour une orientation retournée, l'image retournée horizontalement se pose
en (`ax - (xmin + largeur)`, `ay + ymin`).

## Orientations

Le client choisit le suffixe et un retournement selon la direction 0-7 (table du client, voir
`docs/BOT_STARLOCO.md`, section « Sprites d'acteurs ») : 0 → `S`, 1 → `R`, 2 → `F`, 3 → `R` retourné,
4 → `S` retourné, 5 → `L`, 6 → `B`, 7 → `L` retourné. Il n'y a donc pas de fichier pour 3, 4 et 7.
Sur 932 gfx exportés : `staticR` 913, `staticL` 857, `staticS`/`staticF`/`staticB` 176 chacun ; la plupart
des monstres n'ont que `R` et `L`.

## Règles de l'export

- **Statiques** : symbole exporté `static<O>`, sans tenir compte de la casse (`1118` et `1714`-`1717`
  exportent `StaticR`/`StaticL` ; que le client les trouve n'a pas été vérifié en jeu). On rend la
  **dernière image utile** (`swfsvg --list`) : sur les 661 symboles animés, c'est l'image où le client
  s'arrête après le `stop()` de l'animation de repos (l'épouvantail `1219` sorti du sol, la créature
  `1135` posée, l'ange `1999` atterri) ; sur une respiration en boucle, c'est une image du cycle.
  Les anciens `<gfx><O>.png` montrent l'image 1 (avant l'apparition).
- **Scène** : les 18 SWF sans aucun `static<O>` reçoivent `<gfx>_scene.png`, l'image 1 de la scène
  (ce que montre le clip chargé quand aucune animation n'y est attachée) : épées de combat `0` à `5`
  et tombes `13`, `23` … `123`. Pour chaque équipe de `Gc+` (`id;cellule;type;alignement`), le client
  1.34 choisit l'épée d'après le type et l'alignement : `1` Bonta, `2` Brâkmar, sinon `0` (équipe de
  joueurs), `3` (monstres) ou `4` (percepteur) ; `5` n'est référencé nulle part.
- **Cycles** : pour les 24 gfx de `sprites_animes.txt` (classes 1 à 12, deux sexes), `walk<O>` et
  `run<O>` dans les cinq orientations, toutes les images utiles (20 à 34 selon le cycle, 5 978 au
  total), rendues dans un même cadre (`swfsvg --frame all`) puis assemblées en bande horizontale.
- **Pixels** : échelle 1 (comme les anciens PNG ; `--echelle 2` double PNG et ancres), aplats magenta
  rendus transparents (comme `exporter_png.py`), marges entièrement transparentes rognées (cadre
  commun à toutes les images d'une bande). Palette 8 bits essayée sur chaque PNG et retenue seulement
  si l'écart moyen sur les pixels visibles reste sous 1,5/255 : aucun sprite de ce client ne passe
  (ombres douces et bords en dégradé d'alpha), tous sont en RGBA 32 bits optimisé. Le bot charge ces
  PNG avec `new Bitmap(Image.FromStream(...))`, comme les anciens.

## Non exporté

- `7003` : uniquement des calques `static<O>_Front`/`_Back` (monture chevauchée, composée avec son
  cavalier par le client) ; les calques `_Front`/`_Back` de `1381`, `1440`, `1441`, `7002`, `7005` et
  `9097` sont aussi ignorés, ces gfx ayant des `static<O>` simples. Les montures d'enclos envoyées par
  StarLoco utilisent `7002` ou `7005`, exportés.
- Recoloration des personnages (couleurs de `GM`), accessoires (`clips/sprites/accessories`), montures
  composées (`chevauchor`), auras, émotes, coups et morts : hors de portée de PNG statiques par symbole.

Les illustrations conservent les droits de leurs titulaires d'origine, comme celles de `../Client` et
`../Selection` ; les SWF du client ne sont ni versionnés ni nécessaires à l'exécution.
