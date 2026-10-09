# Effets de sorts et clips annexes du client fourni

Ce dossier est copié à côté de l'exécutable dans `ressources/Bot/Effets` (cible `CopyBotAssets` de
`Outil_Azur_complet.csproj`, qui copie tous les PNG et `.tsv` de `Resources/Bot`). Le bot le lit par
`ActorSprites.ResolveFixed(famille, id, anim)` : `Effets/<famille>/<id>_<anim>.png`, ancre et colonnes de
`Effets/<famille>/effets.tsv` (même format qu'`ancres.tsv` des sprites).

| Dossier | Contenu | Utilisé par |
| --- | --- | --- |
| `sorts/` | `<gfx>_scene.png` (scène de `clips/spells/<gfx>.swf`), `<gfx>_shoot.png`, `<gfx>_move.png`, `<gfx>_duplicate.png` (symboles des projectiles), `effets.tsv`, `exclusions.tsv` | Effets de GA300 (types 10, 11 et 12), GA208 et GA228 (lot AN4) ; symboles des projectiles (lot AN5) |
| `extra/` | `5_scene.png` (scène de `clips/extra/5.swf`, le « ! » du coup critique), `effets.tsv` | Coup critique GA301 (lot AN4) |

## Source et commandes exactes

Source : client Dofus 1.34 remis par l'utilisateur (`04 - Dofus 1.34 - Qu'Tan et Ilyzaelle`),
`clips/spells/<gfx>.swf` et `clips/extra/5.swf`, non versionnés. Outils du dépôt : `swfsvg` 0.2.4
(`--list`, `--scene --frame all`, `--frame all <symbole>`) et `tools/client-analysis/exporter_sorts.py`
(qui importe le découpage en bandes d'`exporter_sprites.py`), puis cairosvg 2.9 et Pillow 12. La liste
des gfx vient de `tools/client-analysis/sorts_utilises.txt` (versionnée, voir plus bas). Depuis la racine
du dépôt :

```sh
(cd tools/client-analysis/swfsvg && cargo build --release)
SWFSVG=tools/client-analysis/swfsvg/target/release/swfsvg \
python3 tools/client-analysis/exporter_sorts.py "<client>/clips/spells" \
    Outil_Azur_complet/Resources/Bot/Effets/sorts --liste tools/client-analysis/sorts_utilises.txt --pas 2 --jobs 4
SWFSVG=tools/client-analysis/swfsvg/target/release/swfsvg \
python3 tools/client-analysis/exporter_sorts.py "<client>/clips/extra" \
    Outil_Azur_complet/Resources/Bot/Effets/extra --gfx 5
```

Mesuré le 8 octobre 2026 (conteneur 4 cœurs) : environ 1 min 10 s pour les 212 SWF de la liste ;
202 PNG pour 16,4 Mo (109 scènes, 56 `shoot`, 32 `move`, 5 `duplicate` ; 9 389 images au pas 2) ;
`extra/5` : 62 images au pas 1, 18 Ko. Aucun PNG n'est passé en palette 8 bits : les effets ont des
dégradés d'alpha que la palette abîme au-delà du seuil d'`exporter_sprites.py` (`--sans-palette` donne
donc le même résultat). Les bandes les plus larges (`513`, `705`, `706`) tiennent sur une ligne de moins
de 32 767 px. Les trois échecs de cairo (`NO_MEMORY` sur 704, 705 et 707) relevés avec swfsvg 0.2.2 ne
se reproduisent plus.

Pas d'export : `--pas 2` pour les sorts (une image sur deux, `ips` 20 : même durée, deux fois moins
d'images) ; pas 1 pour `extra/5`, petit et affiché 5 s en boucle.

## `effets.tsv`

Mêmes colonnes qu'`ancres.tsv` (voir `../sprites/PROVENANCE.md`) : `gfx`, `anim` (`scene`, `shoot`,
`move` ou `duplicate`), `xmin` et `ymin` (coin haut-gauche de l'image par rapport à l'origine du clip,
le point où le client le pose : lanceur, cellule ou acteur), `largeur`, `hauteur`, `images`, `ips`
(40 / pas) et `fin` (colonne `fin` de `swfsvg --list` : `static` quand le clip se retire lui-même par
`removeMovieClip`, `arret` pour un `stop()` final, `boucle` sinon). Sur les 109 scènes : 94 `static`,
4 `arret`, 11 `boucle`. Le bot joue chaque scène une fois puis la retire, et jamais plus de 20 s
(`VISUAL_EFFECT_MAX_TIMER` du client) ; le « ! » du coup critique boucle pendant 5 s.

## `exclusions.tsv` : gfx sans scène

Une ligne par gfx de la liste sans `<gfx>_scene.png`, avec sa raison (`gfx`, `raison`, `detail`). Les
scripts des SWF (hasard, niveau du sort, `attachMovie`, `duplicateMovieClip`, `onEnterFrame`) ne sont
pas exécutés : on rend la timeline telle quelle, et ce que seul un script dessine manque.

| Raison | Nombre | Sens |
| --- | --- | --- |
| `symboles` | 51 | Scène vide ; le dessin est dans `shoot`, `move` ou `duplicate`, exportés (projectiles, lot AN5) |
| `script` | 31 | Scène vide ; le SWF appelle `attachMovie`, `duplicateMovieClip` ou `onEnterFrame` |
| `vide` | 21 | Scène vide sans script reconnu (un cadre sans dessin, souvent un champ de texte ou un clip vide) |

Par type d'affichage (`sorts_utilises.txt`) : 10 gfx de type 10 ou 11 n'ont pas de scène, donc aucun
effet affiché (105, 201, 210, 302, 404, 1201, 1209 et 3000 par script ; 1013 et 1208 vides) ; les autres
exclusions sont des projectiles (types 30 à 51). Les symboles vides
(`shoot` de 103, 2014, 2015, 2017, 2018 ; `move` de 103 ; `duplicate` de 501 et 814) sont signalés par
l'outil et absents d'`effets.tsv`.

Trois projectiles n'ont aucun symbole exporté : 103 (type 30) et 3001 (types 40 et 41), exclus pour
`script`, et 1100, employé en types 10, 11 et 31, dont le SWF n'a aucun symbole `shoot`, `move` ni
`duplicate` : seule sa scène est exportée, et le lot AN5 devra décider quoi afficher pour le type 31.
`2116.swf`, le seul SWF de sort qui porte des filtres, n'est employé par aucun sort de StarLoco : il
n'est pas dans la liste et n'est donc pas exporté (le rendu de ses filtres par swfsvg n'a pas été
examiné).

## `tools/client-analysis/sorts_utilises.txt`

Liste des 212 gfx que le serveur StarLoco fait afficher, générée puis versionnée pour que le réexport
n'ait pas besoin de StarLoco : 201 gfx de la table `sorts` (colonne `sprite`, type de `spriteInfos` à
partir de 10), 7 gfx des fées d'artifice (GA228 : `objectsactions` de type 5, puis table `animations`)
et 4 ballons (GA208 : 2906 à 2909, lus dans `ObjectAction.java`). Seuls des numéros de gfx et des types
d'affichage y figurent. Commande (dump `game.sql` et sources de StarLoco, non versionnés) :

```sh
python3 tools/client-analysis/exporter_sorts.py --starloco "<StarLoco>/02 - BDD/game.sql" \
    --java "<StarLoco>/04 - Game/src/org/starloco/locos/object/ObjectAction.java" \
    --liste tools/client-analysis/sorts_utilises.txt
```

## Bandes `rotate` des types 20 et 21 (lot AN5)

Pour les types 20 et 21, le client tourne l'enfant `rotate` de la scène vers la cible (`onLoadInit`
donne à sa propriété `_rotation` l'angle lanceur → cible). Les 12 gfx de `sorts_utilises.txt` affichés
seulement en types 20 et 21 ont tous une instance `rotate` (profondeur 1, image 1, sans rotation) :
306, 403, 405, 406, 510, 804, 806, 809, 903, 906, 2003 et 2050. Pour eux, `exporter_sorts.py` écrit
`<gfx>_rotate.png` (`swfsvg --scene --instance rotate --frame all`) et rend la scène sans cette
instance (`--sans-instance rotate`). Le cadre de chaque image de `<gfx>_rotate.png` est centré sur la
translation de l'instance, lue dans le `PlaceObject2` du SWF et arrondie au pixel : `xmin + largeur / 2`
et `ymin + hauteur / 2` donnent le point autour duquel le bot tourne la bande (0,5 px près). La colonne
`fin` est celle de la scène. Les 12 scènes privées de `rotate` sont vides : elles passent en exclusion
`symboles` (`rotate`, ou `rotate,shoot`). Commande, depuis la racine du dépôt (seules les lignes de ces
gfx changent ; `--liste` fournit leurs types) :

```sh
SWFSVG=tools/client-analysis/swfsvg/target/release/swfsvg \
python3 tools/client-analysis/exporter_sorts.py "<client>/clips/spells" \
    Outil_Azur_complet/Resources/Bot/Effets/sorts --liste tools/client-analysis/sorts_utilises.txt \
    --gfx 306,403,405,406,510,804,806,809,903,906,2003,2050 --pas 2 --jobs 4
```

Mesuré le 9 octobre 2026 avec swfsvg 0.2.4, cairosvg 2.9.1 et Pillow 12.3 : 12 bandes `rotate`
(468 images au pas 2, 427 Ko) à la place des 12 scènes retirées (399 Ko) ; les `shoot` de ces gfx sont
inchangés à l'octet. `Effets/` passe à 16,47 Mo : 202 PNG dans `sorts/` (97 scènes, 56 `shoot`,
32 `move`, 5 `duplicate`, 12 `rotate`) et 115 exclusions (63 `symboles`, 31 `script`, 21 `vide`).
Les bandes sont plus larges que la scène d'origine, le cadre étant symétrique autour du pivot (2050 :
22 images de 972 px, une ligne de 21 384 px). Le gfx 2103, qui a aussi une instance `rotate`, est
affiché en type 51 : sa scène reste entière.
