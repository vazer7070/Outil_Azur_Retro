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

Réexportation (même commande, `--sans-pentes`) avec `swfsvg` 0.2.2 du dépôt (commit b132bdf), mêmes cairosvg, Pillow et Python : cette version applique les masques (`clipDepth`) que la première dessinait comme des aplats (gazon 1849 et 3626 en vert vif, arbre 7509, faisceaux cyan, socles jaunes…) et rend elle-même les dégradés minuscules. Seuls les PNG dont la taille, l'ancre ou plus de 0,5 % des pixels (écart ≥ 40) changent ont été remplacés, avec leur ligne d'`ancres.tsv` : 1 433 PNG (sols, fonds et objets, image 1) ; les autres gardent l'export d'origine. Les images d'état `objets/<id>_2.png` et `ancres-etats.tsv` ne changent pas.

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
- Premier export : 12 SVG dont cairosvg ne pouvait pas rendre les dégradés (matrices arrondies presque nulles, `MemoryError`) ont été rendus après agrandissement de ces matrices à 0,01, 0,02 ou 0,05 : objets 2253, 2259, 2260, 2261, 1932, 2113, 2114, 2766, 2767, 2768, 6559 et 2843 (la réexportation n'en a plus besoin).
- 36 noms d'objets sont exportés plusieurs fois : 27 dans plusieurs bibliothèques (29 doublons), dont la première occurrence dans l'ordre o1… o11 est gardée et les autres journalisées, et 9 répétés dans une même bibliothèque avec le même symbole, exporté une fois.
- 43 symboles entièrement transparents après la réexportation (14 sols, 29 objets ; 41 au premier export) sont des PNG transparents de 1 px, ancrés : le client n'y dessine rien et le bot ne les remplace pas par une couleur à plat.

## Limites

- Aucune image de sol en pente : le `swfsvg` actuel n'exporte que l'image 1. Le script détecte une option `--frame N` et produira `sols/<id>_<n>.png` lorsqu'elle existera ; en attendant, le bot dessine l'image 1 sur les cellules en pente.
- Les formes morphées et les textes statiques ne sont pas rendus par `swfsvg` : 31 symboles d'objets ont des zones vides (pancartes, inscriptions), signalées par l'export.

## Images d'état des objets interactifs

`objets/<id>_2.png` (80 PNG, 381 Kio, 70 en palette) et `ancres-etats.tsv` (même format qu'`ancres.tsv`, colonne `image` = 2) viennent des mêmes bibliothèques `o1.swf` à `o11.swf`, produits par `tools/client-analysis/exporter_etats_interactifs.py` avec les mêmes outils (swfsvg au commit 7696904, `exporter_png.py`, cairosvg 2.9.1, Pillow 12.3.0, Python 3.11) et les mêmes traitements (découpe, palette des objets). Depuis la racine du dépôt, après `exporter_decor.py` (qui efface les `objets/<id>_<n>.png`) :

```sh
python3 tools/client-analysis/exporter_etats_interactifs.py "<kit>/04 - Dofus 1.34 - Qu'Tan et Ilyzaelle" \
    Outil_Azur_complet/Resources/Bot/Decor --swfsvg <cible>/release/swfsvg
```

Les gfx retenus sont ceux que `BotLang/interactiveobjects.xml` rattache à un objet interactif et dont la timeline compte plusieurs images (80 sur 189 ; ressources, ateliers, portes). L'image 2 est celle que le client affiche pendant une récolte ou un atelier (`GDF|cellule;2`) ; `Bot/Controls/BotMapArtwork.cs` la dessine tant que `Map.ObjectStates` la demande et revient à l'image 1 sinon.

Limites :

- Les images 3 à 5 ne sont pas exportées (option `--images-max`, défaut 2). `swfsvg` rend une image comme si ses clips imbriqués venaient d'être créés, sans `onClipEvent(load)` ni la fin de leurs animations ; or le client y joue une animation qui s'achève sur une autre image (arbre : image 3 = chute puis image 4, souche ; image 5 = repousse puis image 1). Le bot garde donc l'image 1 pour ces états.
- Dans les arbres (gfx 7500 à 7509) et les minerais, les parties de l'objet choisissent leur variante par script (`gotoAndStop(_parent._parent.n_arbre + 1)` ou `n + 1`, la variable valant 1 dès l'image 1) ; `swfsvg` ne l'exécute pas, si bien que `objets/<id>.png` (image 1, export d'`exporter_decor.py`) montre la première variante des parties pour tous les arbres de 7500 à 7508 ; l'aplat vert de l'arbre 7509 était un masque, corrigé par la réexportation.

Les illustrations conservent les droits de leurs titulaires d'origine, comme celles de `../Selection` et `../Client`.
