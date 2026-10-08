# Sprites d'acteurs du client fourni

Ce dossier est copié à côté de l'exécutable dans `ressources/Bot/sprites` (cible `CopyBotAssets`
de `Outil_Azur_complet.csproj`, pour les PNG et `ancres.tsv`).

## Deux générations de fichiers

| Fichiers | Origine | Utilisés par |
| --- | --- | --- |
| `<gfx><O>.png` (2 304, ex. `1001R.png`) et les quelques `1.png`, `1381k.png`, `7029c.png`… | Arrivés avec la fusion du 3 octobre 2026, outil et réglages non documentés ; même contenu que la **première** image des symboles `static<O>`, recadrée, sans ancre. | Chargeur actuel (`UserMapControl.LoadSprite`). Conservés tels quels pour ne rien casser. |
| `<gfx>_static<O>.png`, `<gfx>_scene.png`, `<gfx>_walk<O>.png`, `<gfx>_run<O>.png`, `ancres.tsv` | Générés par `tools/client-analysis/exporter_sprites.py` (commande ci-dessous). | Rendu des acteurs (lot M1) : nouveau nom d'abord, ancien nom en repli. |
| `<gfx>_hit<O>.png`, `<gfx>_die<O>.png` (96, `O` = `R` ou `L`) | Même outil, export partiel du lot AN2 (section « Coups reçus et morts »). | Coup reçu et mort des personnages joueurs (lot AN2). |
| `<gfx>_walk<O>.png`, `<gfx>_run<O>.png`, `<gfx>_hit<O>.png`, `<gfx>_die<O>.png`, `<gfx>_anim0<O>.png` de 100 gfx de monstres (1 034 bandes à 20 ips) | Même outil au pas 2, gfx choisis par `choisir_gfx_animes.py` (section « Monstres (lot AN3) »). | Marche, course, coup reçu, mort et attaque des monstres. |

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

## Monstres (lot AN3)

Les 100 gfx de monstres les plus présents sur les cartes de StarLoco marchent, courent, encaissent,
meurent et attaquent : familles `walk`, `run`, `hit`, `die` et `anim0`, **au pas 2** (une image sur
deux, colonne `ips` à 20 ; la durée reste celle du client). Sources : `clips/sprites/<gfx>.swf` du
client et, pour le choix des gfx, `02 - BDD/game.sql` de l'archive StarLoco remise par l'utilisateur
(non versionnés ; seuls des numéros de gfx en sont tirés).

**Choix des gfx.** `tools/client-analysis/choisir_gfx_animes.py` lit les tables `maps`, `monsters` et
`mobgroups_fix` du dump comme le serveur les charge et compte les **présences** de chaque gfx : une
entrée `<monstre>,<niveau>` de `maps.monsters` d'une carte qui fait apparaître des groupes
(`numgroup` > 0) et dont le monstre a un grade de ce niveau, plus chaque membre d'un groupe fixe de
donjon. Quand StarLoco compose un groupe, il tire ses membres dans ces entrées. Sur ce dump : 410 gfx
présents, 110 862 présences ; les 50 premiers en couvrent 67,7 %, les 100 premiers 84,2 %, les 150
premiers 91,8 %. Sans les groupes fixes (`--sans-fixes`) : 290 gfx et 85,3 % pour les 100 premiers,
ce que donnait le recomptage du plan. Avec `--sprites`, l'outil ne garde que les familles que le SWF
exporte vraiment (au moins deux images au pas 2) : `8010` n'a pas d'`anim0`.

**Commandes exactes**, depuis la racine du dépôt (`<SL>` : dossier de l'archive StarLoco, `<client>` :
dossier du client) :

```sh
(cd tools/client-analysis/swfsvg && cargo build --release)
python3 tools/client-analysis/choisir_gfx_animes.py "<SL>/02 - BDD/game.sql" \
    --sprites "<client>/clips/sprites" --swfsvg tools/client-analysis/swfsvg/target/release/swfsvg \
    --exclure Outil_Azur_complet/Resources/Bot/sprites/sprites_animes.txt --budget-mo 55 \
    >> Outil_Azur_complet/Resources/Bot/sprites/sprites_animes.txt
python3 tools/client-analysis/exporter_sprites.py "<client>/clips/sprites" \
    Outil_Azur_complet/Resources/Bot/sprites \
    --swfsvg tools/client-analysis/swfsvg/target/release/swfsvg \
    --gfx $(awk '/^# Monstres \(lot AN3\)/{f=1} f && /^[0-9]/{print $1}' \
            Outil_Azur_complet/Resources/Bot/sprites/sprites_animes.txt | paste -sd,) \
    --anims walk,run,hit,die,anim0 --pas 2 --jobs 4
```

Les trois lignes de commentaire « # Monstres (lot AN3) … » ont été écrites dans `sprites_animes.txt`
avant celles de l'outil. Réglages par défaut de l'outil : `--nombre 100`, `--pas 2`,
`--familles walk,run,hit,die,anim0`, `--ko-par-image 2.5`. Il a estimé 53,0 Mio ; l'export réel fait
**35 206 465 octets** (33,6 Mio) pour 1 034 PNG et 21 691 images, soit 1,6 Kio par image (swfsvg
0.2.3, cairosvg 2.9, Pillow 12 ; 2 min 38 s sur 4 cœurs le 8 octobre 2026, un seul message :
`8010 : aucun symbole anim0<O>`). Aucune bande ne passe en palette 8 bits (ombres et bords en dégradé
d'alpha). Seules les lignes de ces 100 gfx et de ces familles ont été ajoutées à `ancres.tsv` : aucune
ligne existante n'a changé et les poses `static` n'ont pas été réexportées.

| famille | bandes | images | octets | fin |
| --- | --- | --- | --- | --- |
| `walk` | 218 | 3 729 | 6 321 853 | `boucle` |
| `run` | 218 | 3 460 | 5 489 539 | `boucle` ; `static` pour `runR` et `runL` de `1002` et `1161` |
| `hit` | 200 | 3 202 | 2 346 569 | `static`, sauf `1003`, `1004`, `1007`, `1009`, `1564` et `1572` (voir plus bas) |
| `die` | 200 | 5 277 | 9 606 543 | `arret` ; `boucle` ou `static` pour 14 bandes de 10 gfx (la mort dure de toute façon 1 500 ms) |
| `anim0` | 198 | 6 023 | 11 441 961 | `static`, sauf `1003`, `1004`, `1007`, `1009` et `1564` |

Les monstres n'exportent le plus souvent `walk` et `run` qu'en `R` et `L` (94 gfx sur 100) : un groupe
qui avance en ligne droite (directions 0, 2, 4, 6) garde alors sa pose fixe, comme avant ce lot.

**Défaut connu (swfsvg 0.2.3).** Le nombre d'images utiles d'un symbole est celui de son clip imbriqué
le plus long, même quand un clip plus court se termine par `GAC.applyAnim(this, "static")`. Les
symboles orientés `R` de `1003`, `1004` et `1007`, et tous ceux de `1009`, contiennent un clip en boucle
de 275, 275, 232 et 350 images : leurs bandes `hit`, `die` et `anim0` concernées font 116 à 175 images
au pas 2 avec la fin `boucle`, alors que le client revient au repos à la fin de l'animation (24 images
pour `1003_hitL`). Le bot joue donc ces coups et ces attaques en une seule passe de 5,8 à 8,8 s.
`1564_hitL` et les `hit` de `1572` finissent aussi en `boucle`, `1564_hitR` (7 images) en `arret`. La
correction relève de `swfsvg` (lot suivant qui l'édite) ; il suffira ensuite de relancer la seconde
commande avec `--gfx 1003,1004,1007,1009,1564,1572`.

**Le reste.** Les 310 autres gfx présents, les familles `anim1` et suivantes, `bonus`, `appear` et le
repos animé (le `static` à plusieurs images, que l'exporteur rend sur une seule image) ne sont pas
versionnés. Le dossier `sprites-local/` à côté de l'exécutable (ignoré par git) est lu avant celui-ci :
on y dépose un export plus large avec les mêmes outils, par exemple la liste de
`choisir_gfx_animes.py --nombre 410` (sans `--budget-mo`) écrite dans un fichier à part, puis
`exporter_sprites.py` avec ces gfx et `<bin>/sprites-local` comme dossier de sortie.

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
  des monstres et PNJ : non exportés hors des 100 gfx de la section « Monstres (lot AN3) » (familles à
  ajouter par gfx dans `sprites_animes.txt`).

Les illustrations conservent les droits de leurs titulaires d'origine, comme celles de `../Client` et
`../Selection` ; les SWF du client ne sont ni versionnés ni nécessaires à l'exécution.
