# Sprites d'acteurs du client fourni

Ce dossier est copié à côté de l'exécutable dans `ressources/Bot/sprites` (cible `CopyBotAssets`
de `Outil_Azur_complet.csproj`, pour les PNG et `ancres.tsv`).

## Deux générations de fichiers

| Fichiers | Origine | Utilisés par |
| --- | --- | --- |
| `<gfx><O>.png` (2 304, ex. `1001R.png`) et les quelques `1.png`, `1381k.png`, `7029c.png`… | Arrivés avec la fusion du 3 octobre 2026, outil et réglages non documentés ; même contenu que la **première** image des symboles `static<O>`, recadrée, sans ancre. | Chargeur actuel (`UserMapControl.LoadSprite`). Conservés tels quels pour ne rien casser. |
| `<gfx>_static<O>.png`, `<gfx>_scene.png`, `<gfx>_walk<O>.png`, `<gfx>_run<O>.png`, `ancres.tsv` | Générés par `tools/client-analysis/exporter_sprites.py` (commande ci-dessous). | Rendu des acteurs (lot M1) : nouveau nom d'abord, ancien nom en repli. |
| `<gfx>_hit<O>.png`, `<gfx>_die<O>.png` (96, `O` = `R` ou `L`) | Même outil, export partiel du lot AN2 (section « Coups reçus et morts »). | Coup reçu et mort des personnages joueurs (lot AN2). |

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
| `images` | nombre d'images de la bande (1 pour `static` et `scene`) ; l'image k (à partir de 0) occupe les colonnes `[k × largeur, (k + 1) × largeur)` (toutes les bandes actuelles tiennent sur une ligne ; au-delà de 32 767 px de large, l'outil range les images en grille, ligne par ligne) |
| `ips` | images par seconde de la bande : 40 (cadence des clips du client, `DOUBLEFRAMERATE`) divisé par le pas d'export ; 40 partout aujourd'hui |
| `fin` | ce que fait le client à la dernière image, d'après la colonne `fin` de `swfsvg --list` (0.2.3) : `boucle`, `arret` (dernière image tenue), `static` (retour à la pose de repos) ou `suite:<anim>` ; `arret` pour une image seule |

Pour poser un sprite sur la cellule dont le point d'ancrage est (`ax`, `ay`) : coin du PNG en
(`ax + xmin`, `ay + ymin`). Pour une orientation retournée, l'image retournée horizontalement se pose
en (`ax - (xmin + largeur)`, `ay + ymin`).

## Coups reçus et morts (lot AN2)

Familles `hit` et `die` des 24 gfx de `sprites_animes.txt` (une ligne `<gfx> walk,run,hit,die` par
classe et sexe). Le client n'exporte ces symboles qu'en `R` et `L` ; les directions 0, 2, 4, 6 prennent
`R` ou `L` et le retournement (voir `docs/BOT_STARLOCO.md`). Outil : `swfsvg` 0.2.3 (colonne `fin` de
`--list`) et `exporter_sprites.py` du même commit. Depuis la racine du dépôt :

```sh
(cd tools/client-analysis/swfsvg && cargo build --release)
python3 tools/client-analysis/exporter_sprites.py "<client>/clips/sprites" \
    Outil_Azur_complet/Resources/Bot/sprites \
    --swfsvg tools/client-analysis/swfsvg/target/release/swfsvg \
    --gfx $(grep -v '^#' Outil_Azur_complet/Resources/Bot/sprites/sprites_animes.txt | cut -d' ' -f1 | paste -sd,) \
    --anims hit,die --jobs 4
```

Mesuré le 8 octobre 2026 (4 cœurs) : 28 s, 24 SWF, 96 PNG pour 3 540 281 octets (3,5 Mo), 3 444
images, aucun message. Seules les lignes `hit`/`die` d'`ancres.tsv` ont été ajoutées ; les 2 556
lignes existantes sont identiques sur leurs 7 premières colonnes et ont reçu `ips` 40 et leur `fin`.

| bande | images | fin |
| --- | --- | --- |
| `hitR`, `hitL` | 24 (600 ms), 26 pour `90`, 28 pour `31_hitR` | `static` : le clip revient à la pose de repos |
| `dieR`, `dieL` | 25 à 112 selon la classe ; la plus large, `60_dieR`, fait 6 832 px | `arret` (dernière image tenue), sauf `60_dieR` et `61_dieR` : `boucle` |

Le client joue la mort pendant exactement 1 500 ms (60 images) puis retire le sprite (voir
`docs/BOT_STARLOCO.md`) : une bande plus longue est coupée, une plus courte reste sur sa dernière image.
Les variantes `_C` (personnage qui en porte un autre, par exemple `hit_CR`) existent dans 7 SWF :
`120` et `121` (Pandawa, toutes les familles plus `carring*`), `110` (`static` et `walk` seulement),
`1360`, `8006`, `8009` et `8026`. Le bot ne suit pas l'état « porte » : elles ne sont pas exportées.

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
  composées (`chevauchor`), auras, émotes : hors de portée de PNG statiques par symbole. Coups et morts
  des monstres et PNJ : non exportés (familles à ajouter par gfx dans `sprites_animes.txt`).

Les illustrations conservent les droits de leurs titulaires d'origine, comme celles de `../Client` et
`../Selection` ; les SWF du client ne sont ni versionnés ni nécessaires à l'exécution.

## Animations d'attaque des classes (lot AN4)

`<gfx>_anim<n><O>.png` (624 bandes, `O` = `R` ou `L`, comme `hit` et `die`) : familles `anim0` à
`anim2`, `anim3` et `anim10` à `anim18` des 24 gfx de classes, ajoutées à leur ligne de
`sprites_animes.txt`, toutes au pas 2 (`ips` 20). Le bot les joue sur le lanceur d'un sort (GA300 :
`anim` + champ 6), d'un coup d'arme (GA303) et pendant une récolte (GA501) : `ToolAnimation` du client
vaut `anim` + l'attribut `an` de l'arme (premier accessoire du `GM`), sinon `anim0` en combat et `anim3`
hors combat. Valeurs d'`an` dans `../BotLang/items.xml` : 10 arcs, 11 baguettes, 12 bâtons, 13 dagues,
14 épées, 15 marteaux, 16 pelles, 17 haches et outils de récolte (bûcheron, paysan, mineur), 18 cannes à
pêche ; 0, 1, 3 et 4 portent sur 22 objets. Outils : `swfsvg` 0.2.4 et `exporter_sprites.py` du même
commit. Depuis la racine du dépôt :

```sh
(cd tools/client-analysis/swfsvg && cargo build --release)
python3 tools/client-analysis/exporter_sprites.py "<client>/clips/sprites" \
    Outil_Azur_complet/Resources/Bot/sprites \
    --swfsvg tools/client-analysis/swfsvg/target/release/swfsvg \
    --gfx 10,11,20,21,30,31,40,41,50,51,60,61,70,71,80,81,90,91,100,101,110,111,120,121 \
    --anims anim0,anim1,anim2,anim3,anim10,anim11,anim12,anim13,anim14,anim15,anim16,anim17,anim18 \
    --pas 2 --jobs 4
```

Mesuré le 9 octobre 2026 (4 cœurs) : environ 4 min, 24 SWF, 624 PNG pour 21 676 728 octets (21,7 Mo),
13 994 images, aucun message, tous en RGBA (palette refusée par le seuil, comme les autres sprites).
Seules les lignes `anim<n>` d'`ancres.tsv` ont été ajoutées.

| familles | images | octets | fin |
| --- | --- | --- | --- |
| `anim0` | 916 | 1 239 716 | `static` |
| `anim1` | 1 218 | 1 881 184 | `boucle` pour 24 bandes sur 48, `static` sinon |
| `anim2` | 1 208 | 1 863 329 | `boucle` pour 20 bandes sur 48, `static` sinon |
| `anim3` | 1 380 | 1 131 693 | `static` |
| `anim10` à `anim17` | 7 354 | 12 711 746 | `static` |
| `anim18` (le plus large : `41_anim18R`, 6 519 px) | 1 918 | 2 849 060 | `static` |

Écarts avec le plan des animations, qui prévoyait `anim0` à `anim2` au pas 1 (≈ 7 Mo) et le reste au
pas 2 (≈ 11 Mo) : au pas 1, `anim0` à `anim2` pèsent 9 006 153 octets (6 684 images) et l'ensemble
25 698 652 octets, au-delà des 23 Mo de la part du lot dans le dossier ; les trois familles des sorts
sont donc aussi au pas 2 (même durée, 20 images par seconde comme les monstres). `anim8` (lancer d'un
ballon ou d'un feu d'artifice, GA208 et GA228) n'est pas exporté : 52 à 56 images par bande, environ
2 Mo de plus au pas 2 ; sans bande, le bot passe directement à l'effet. `anim4` (4 fioles) non plus.
