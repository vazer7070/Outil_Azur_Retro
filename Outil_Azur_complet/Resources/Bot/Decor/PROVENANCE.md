# Décor des cartes exporté du client fourni

Ces PNG et `ancres.tsv` proviennent des bibliothèques graphiques des cartes du client Dofus 1.34 remis par l'utilisateur (`04 - Dofus 1.34 - Qu'Tan et Ilyzaelle`) : `clips/gfx/g1.swf` et `g2.swf` (sols et fonds), `o1.swf` à `o11.swf` (objets), `cell.swf` (formes de sélection et d'interaction des cellules). Ils ont été produits par les outils du dépôt, sans décompilateur externe ; aucun SWF du client n'est versionné. Le projet copie ce dossier vers `ressources/maps` à côté de l'exécutable (cible `CopyBotDecorAssets`), où `Bot/Controls/BotMapArtwork.cs` les lit.

## Commande

Depuis la racine du dépôt :

```sh
CARGO_TARGET_DIR=<cible> cargo build --release --manifest-path tools/client-analysis/swfsvg/Cargo.toml
python3 tools/client-analysis/exporter_decor.py "<kit>/04 - Dofus 1.34 - Qu'Tan et Ilyzaelle" \
    Outil_Azur_complet/Resources/Bot/Decor --swfsvg <cible>/release/swfsvg
```

Outils : `swfsvg` du dépôt (état du commit 8b428d5), `exporter_png.py` inchangé (cairosvg 2.9.1, échelle 1, aplats magenta rendus transparents), Pillow 12.3.0 sous Python 3.11 ; `swfsvg` a été compilé avec `CARGO_TARGET_DIR` hors du dépôt. Durée mesurée sur 4 cœurs : 14 s de SVG, 185 s de PNG, 47 s de découpe et de palette.

## Contenu

| Chemin | Nombre | Origine |
| --- | --- | --- |
| `sols/<id>.png` | 554 | symboles numérotés de g1/g2 jusqu'à 500 px, image 1 (sol à plat) |
| `backgrounds/<id>.png` | 67 | symboles de g1/g2 de plus de 500 px : les fonds désignés par `BACK` (backgroundNum) |
| `objets/<id>.png` | 4 998 | symboles numérotés de o1 à o11, image 1 |
| `cellules/s<n>.png`, `cellules/i<n>.png` | 30 | formes de sélection et d'interaction de cell.swf (exportées, non dessinées par le bot) |
| `ancres.tsv` | 5 649 lignes | une ligne par PNG |

`ancres.tsv` contient `type id xmin ymin largeur hauteur image`, séparés par des tabulations. `(xmin, ymin)` est la position du pixel en haut à gauche du PNG par rapport au point d'enregistrement du symbole, donc par rapport à la position de la cellule dans le client (à l'origine de la carte pour un fond) ; `largeur × hauteur` est la taille du PNG, qui permet au bot d'ignorer une ancre si le fichier a été remplacé ; `image` vaut 1, ou n pour `sols/<id>_<n>.png`, image d'un sol en pente.

## Traitements

- Marges transparentes découpées ; l'ancre en tient compte.
- Palette de 256 couleurs (Pillow, FASTOCTREE) quand elle est exacte (256 couleurs au plus) ou que l'écart reste faible : moyenne ≤ 1,5, 99e centile ≤ 6 et alpha au 99e centile ≤ 8 pour les sols et les fonds, 5,5 / 22 / 64 pour les objets ; sinon RGBA 32 bits. 4 929 PNG sur 5 649 sont en palette ; le dossier pèse 63,3 Mo (146 Mo en RGBA).
- 12 SVG dont cairosvg ne pouvait pas rendre les dégradés (matrices arrondies presque nulles, `MemoryError`) ont été rendus après agrandissement de ces matrices à 0,01, 0,02 ou 0,05 : objets 2253, 2259, 2260, 2261, 1932, 2113, 2114, 2766, 2767, 2768, 6559 et 2843.
- 36 noms d'objets sont exportés plusieurs fois : 27 dans plusieurs bibliothèques (29 doublons), dont la première occurrence dans l'ordre o1… o11 est gardée et les autres journalisées, et 9 répétés dans une même bibliothèque avec le même symbole, exporté une fois.
- 41 symboles entièrement transparents (11 sols, 30 objets) sont des PNG transparents de 1 px, ancrés : le client n'y dessine rien et le bot ne les remplace pas par une couleur à plat.

## Limites

- Aucune image de sol en pente : le `swfsvg` actuel n'exporte que l'image 1. Le script détecte une option `--frame N` et produira `sols/<id>_<n>.png` lorsqu'elle existera ; en attendant, le bot dessine l'image 1 sur les cellules en pente.
- Les formes morphées et les textes statiques ne sont pas rendus par `swfsvg` : 31 symboles d'objets ont des zones vides (pancartes, inscriptions), signalées par l'export.

Les illustrations conservent les droits de leurs titulaires d'origine, comme celles de `../Selection` et `../Client`.
