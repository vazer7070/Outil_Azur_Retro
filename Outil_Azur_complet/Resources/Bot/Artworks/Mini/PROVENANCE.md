# Petites illustrations de personnage (`clips/artworks/mini`)

Ces 50 PNG sont les petites illustrations du client Dofus 1.34 fourni (`04 - Dofus 1.34 - Qu'Tan et Ilyzaelle`), une par fichier `clips/artworks/mini/<gfx>.swf`. Le client les charge par le gfx du personnage (classe × 10 + sexe pour les 24 apparences de classe : `GUILDS_MINI_PATH + gfx + ".swf"`, propriété `gfxBreedFile`) dans ses listes de personnages ; les autres numéros (1001, 1169, 7002, 8000 à 8037, 9001…) sont des apparences particulières (incarnations, transformations). Le bot les affiche dans le volet Groupe, dans la case de chaque membre annoncé par `PM` (champ gfx).

Ces SWF n'exportent aucun symbole par `ExportAssets` : l'illustration est posée sur la scène principale, que `swfsvg --scene` rend. Le nom du PNG est le numéro du SWF.

## Commande exacte

Depuis la racine du dépôt, avec `CLIENT` = dossier du client fourni (non versionné) :

```sh
CARGO_TARGET_DIR=/tmp/cargo-swfsvg cargo build --release --manifest-path tools/client-analysis/swfsvg/Cargo.toml
python3 tools/client-analysis/exporter_groupe.py "$CLIENT" Outil_Azur_complet/Resources/Bot --swfsvg /tmp/cargo-swfsvg/release/swfsvg
```

Le script lance, pour chaque SWF, `swfsvg --scene --name <numéro> <swf> <svg>/`, puis `exporter_png.py <svg> Outil_Azur_complet/Resources/Bot/Artworks/Mini 2` ; il écrit aussi les éléments du volet dans `../../Party` (voir son `PROVENANCE.md`).

- `swfsvg` 0.2.1 (`tools/client-analysis/swfsvg`), mode `--scene` : image 1 de la timeline principale, cadrée sur les formes (pas de marge transparente à retirer).
- `exporter_png.py` à l'échelle 2 (cairosvg, magenta pur rendu transparent) : 50 PNG RGBA de 20 × 22 à 64 × 78 pixels, 105 209 octets au total. La réduction en palette 8 bits a été essayée avec Pillow : elle ne gagne que 27 Ko et altère les bords semi-transparents (écart jusqu'à 30 sur un canal), elle n'est donc pas appliquée.
- Durée mesurée : moins de 2 secondes pour les 50 SWF et les 4 éléments du volet. Deux exécutions donnent les mêmes PNG (sommes SHA-256 identiques).
- Réexport du 4 octobre 2026 avec `swfsvg` 0.2.2 (après la fusion du lot des icônes) : les 50 illustrations ne diffèrent que sur quelques pixels de bord (au plus 14 pixels par image s'écartent de plus de 8 niveaux) ; les PNG de la version 0.2.1 sont gardés.

Les couleurs personnalisées du personnage (`c1;c2;c3` de `PM`) ne sont pas appliquées : le client les pose à l'exécution sur les clips colorables, ce qu'un PNG statique ne peut pas reproduire. Le volet `Party` du client dessine en fait le sprite du membre (`staticR` retourné, à 65 %) ; le bot montre cette illustration de classe à la place, comme ses listes de personnages.

## Exécution

`Outil_Azur_complet.csproj` copie ce dossier vers `ressources/Bot/Artworks/Mini` à côté de l'exécutable (cible générique `CopyBotAssets`, qui copie tous les PNG de `Resources/Bot`). Le volet Groupe lit `ressources/Bot/Artworks/Mini/<gfx>.png` (puis `Resources/Bot/Artworks/Mini`) hors du thread de l'interface (`PartyArtworks`) ; un PNG absent ou illisible laisse la case du membre avec l'initiale de son nom.

Les images conservent les droits de leurs titulaires d'origine ; aucun SWF du client n'est versionné.
