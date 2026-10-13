# Accessoires des personnages (lot AN7)

Coiffes, capes, familiers, boucliers et armes que le client Dofus 1.34 attache aux sprites des
personnages (`GAC.applyAccessory`), en bandes PNG avec leurs ancres, et les clips que chaque image des
sprites de classes leur réserve. Ce dossier est copié à côté de l'exécutable dans
`ressources/Bot/Accessoires` (cible `CopyBotAssets` de `Outil_Azur_complet.csproj`, PNG et TSV,
sous-dossier `emplacements` compris). Le bot (`Bot/Controls/ActorAccessories.cs`, `ActorSprites`) les
lit comme les sprites : dossier `sprites-local/Accessoires` d'abord s'il existe, puis celui-ci.

## Fichiers

| Fichiers | Contenu |
| --- | --- |
| `<type>_<gfx>_<étiquette>.png` (4 200) | Une bande par étiquette d'un symbole d'accessoire : `<type>_<gfx>` est le nom d'export du symbole dans `a1.swf`…`a5.swf` (type et gfx de l'objet dans `../BotLang/items.xml`), `<étiquette>` l'image sur laquelle le client l'arrête (`S`, `R`, `L`, `F`, `B` pour une coiffe ; `L`, `R`, `RR`, `RL` pour une cape ; `L`, `R`, `S`, `F`, `B` et `WL`, `WR`, `WS`, `WF`, `WB` pour un familier à l'arrêt et en marche ; `R` et `L` pour une arme ou un bouclier ; `R0`…`R19` pour les objets vivants `16_211` et `17_149`). Une bande de plusieurs images quand l'image tenue contient un clip qui joue (familier qui marche). |
| `ancres.tsv` | Même format que `../sprites/ancres.tsv` (`gfx anim xmin ymin largeur hauteur images ips fin`) : `gfx` est le symbole (`16_2`), `anim` l'étiquette ; `xmin`, `ymin` : coin haut-gauche du PNG par rapport à l'origine du symbole, le point que le client pose sur l'origine du clip réservé. |
| `emplacements/<gfx>.tsv` (24, un par classe et sexe) | Clips réservés aux accessoires dans chaque image de chaque bande du gfx (`../sprites/<gfx>_<anim>.png`) : `anim image emplacement etiquette miroir a b c d tx ty tx_miroir ty_miroir devant`. `image` : rang dans la bande à partir de 0 (au pas de la bande) ; `emplacement` : 0 arme, 1 coiffe, 2 cape, 3 familier, 4 bouclier ; `etiquette` : celle que `applyAccessory` donne au clip ; `miroir` : 1 si le client inverse `_x` du clip quand le sprite est retourné (familiers) ; `a b c d tx ty` : matrice qui pose le repère de l'accessoire dans celui du sprite (pixels, origine au pied), `tx_miroir ty_miroir` : translation à prendre à la place de `tx ty` quand le sprite est retourné et `miroir` vaut 1 ; `devant` : 1 si le clip est dessiné après le corps (au moins la moitié des formes de l'image sont derrière lui), 0 derrière. |

Pose d'un pixel `(x, y)` de l'image `k` d'une bande d'accessoire sur un acteur dont le pied est en
`(ax, ay)`, à l'échelle `(sx, sy)` : `X = xmin + x`, `Y = ymin + y`, puis `(a X + c Y + tx, b X + d Y + ty)`,
puis `(ax + sx X', ay + sy Y')` (`X'` changé de signe quand le sprite est retourné).

## Source et commandes exactes

Source : `clips/sprites/accessories/a1.swf` à `a5.swf` (accessoires, importés par `accessories.swf`) et
`clips/sprites/<gfx>.swf` des 24 classes du client Dofus 1.34 remis par l'utilisateur
(`04 - Dofus 1.34 - Qu'Tan et Ilyzaelle`), non versionnés. Outils du dépôt : `swfsvg` 0.2.6
(`--etiquettes-list`, `--etiquettes`, `--emplacements`), `exporter_accessoires.py` et
`exporter_sprites.py --emplacements` du même commit, cairosvg 2.9 et Pillow 12. Depuis la racine du
dépôt :

```sh
(cd tools/client-analysis/swfsvg && cargo build --release)
SW=tools/client-analysis/swfsvg/target/release/swfsvg
# 1. Les accessoires : toutes les étiquettes de tous les symboles <type>_<gfx> de a1..a5, au pas 1.
python3 tools/client-analysis/exporter_accessoires.py "<client>/clips/sprites/accessories" \
    Outil_Azur_complet/Resources/Bot/Accessoires --swfsvg $SW --jobs 4
# 2. Les clips réservés des 24 classes : réexport du lot AN6 (bandes et masques gardés tels quels
#    par --conserver) qui écrit en plus emplacements/<gfx>.tsv.
python3 tools/client-analysis/exporter_sprites.py "<client>/clips/sprites" \
    Outil_Azur_complet/Resources/Bot/sprites --swfsvg $SW \
    --gfx 10,11,20,21,30,31,40,41,50,51,60,61,70,71,80,81,90,91,100,101,110,111,120,121 \
    --masques --conserver --emplacements Outil_Azur_complet/Resources/Bot/Accessoires/emplacements --jobs 4
```

Mesuré le 12 octobre 2026 (conteneur 4 cœurs) :

| commande | durée | résultat |
| --- | --- | --- |
| 1. accessoires | 1 min 41 s | 1 186 symboles, 4 200 PNG pour 9 302 093 octets (9,3 Mo), 4 200 lignes d'`ancres.tsv` ; 5 étiquettes sans aucun pixel, sans PNG (`16_11 B`, `17_18 RR`, `17_34 RR`, `17_200 R` et `L`) |
| 2. emplacements | 10 min 57 s | 24 SWF, 94 627 lignes dans 24 fichiers pour 6 068 358 octets (6,1 Mo) ; 1 080 bandes et 1 080 masques des classes gardés tels quels, `ancres.tsv` et `couleurs.tsv` de `sprites/` inchangés |

Le dossier pèse 15 370 451 octets (≈ 20 Mo prévus par le plan des animations).

| type | symboles exportés | bandes | images |
| --- | --- | --- | --- |
| 16 coiffes (`a1.swf`, quelques-unes dans `a4` et `a5`) | 274 | 1 464 | 1 470 |
| 17 capes (`a2.swf`) | 157 | 702 | 1 552 (les `RR` et `RL` de 141 capes tiennent 2 à 6 images) |
| 18 familiers (`a3.swf`, `a5.swf`) | 62 | 620 | 6 372 (`W*` en boucle de 12 à 38 images, 40 ips) |
| 1 à 8, 19 à 22, 81, 82 : armes, outils, boucliers (`a4.swf`, `a5.swf`) | 692 | 1 414 | 1 416 |

Toutes les bandes jouent à 40 ips ; 496 finissent en `boucle` (familiers qui marchent, capes au vent),
les autres en `arret`. La palette 8 bits n'est retenue pour aucun PNG (bords en dégradé d'alpha).

**Rendus refusés par cairo.** 156 images de trois symboles (`18_62` en marche, `6_87`, `18_55` en
marche) ont un dégradé dont la matrice, en pixels, est presque singulière : cairo répond
`CAIRO_STATUS_NO_MEMORY`. `exporter_sprites.svg_vers_image` les rend alors sur-échantillonnées (×4 ou
×16) puis réduites par moyenne des blocs (alpha prémultiplié), et pour 28 images de `18_55` retire
d'abord les formes à dégradé posées par une matrice de déterminant inférieur à 10⁻⁵ (surface nulle,
invisibles). Les autres images de ces bandes sont rendues normalement.

## Ce que le bot en fait

`ActorAccessories.Worn` traduit le champ `stuff` du `GM` (`arme,coiffe,cape,familier,bouclier`,
identifiants d'objets en hexadécimal ; `id~type~image` pour un objet vivant) en symboles
`<type>_<gfx>` par `items.xml` ; `UserMapControl` pose, pour l'image affichée de la bande affichée, chaque
accessoire porté sur son clip réservé (étiquette du clip, suivie de l'image pour un objet vivant),
derrière puis devant le corps, retourné avec le sprite. Voir `docs/BOT_STARLOCO.md`, section
« Accessoires des personnages (lot AN7) ».

## Non exporté

- La tête sous une coiffe : le client place le clip `_Tete` du sprite sur son image 2 (cheveux cachés)
  quand une coiffe est portée ; le bot garde l'image 1 (la coiffe recouvre la chevelure).
- Les clips réservés des gfx hors classes (PNJ, montures, monstres) : `exporter_sprites.py
  --emplacements` sait les relever (`--gfx <id>`), rien n'est versionné pour eux.
- Les accessoires d'`a1_charria.swf`, `a2_charria.swf` et des copies de `a2.swf` du dossier du client
  (variantes non chargées par `accessories.swf`).

Les illustrations conservent les droits de leurs titulaires d'origine, comme celles de `../sprites`.
