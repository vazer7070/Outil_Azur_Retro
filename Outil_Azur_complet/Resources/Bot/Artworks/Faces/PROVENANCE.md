# Bustes des classes (`clips/artworks/faces`)

Ces 24 PNG sont les bustes des 12 classes jouables du client Dofus 1.34 fourni (`04 - Dofus 1.34 - Qu'Tan et Ilyzaelle`), un par fichier `clips/artworks/faces/<gfx>.swf` avec gfx = classe × 10 + sexe (10 à 121). La fiche du conjoint du client (`SpouseViewer`, onglet Amis de la fenêtre `Friends`) charge `GUILDS_FACES_PATH + gfx + ".swf"` ; le bot affiche ce buste dans la fiche du conjoint du volet Amis (paquet `FS`). Les petites illustrations de la liste d'amis viennent de `../Mini` (lot Groupe).

Ces SWF n'exportent aucun symbole par `ExportAssets` : le buste est posé sur la scène principale, que `swfsvg --scene` rend. Le nom du PNG est le numéro du SWF.

## Commande exacte

Depuis la racine du dépôt, avec `CLIENT` = dossier du client fourni (non versionné) :

```sh
CARGO_TARGET_DIR=/tmp/cargo-swfsvg cargo build --release --manifest-path tools/client-analysis/swfsvg/Cargo.toml
python3 tools/client-analysis/exporter_artworks.py "$CLIENT" Outil_Azur_complet/Resources/Bot --swfsvg /tmp/cargo-swfsvg/release/swfsvg
```

- `swfsvg` 0.2.2 (`tools/client-analysis/swfsvg`), mode `--scene --name <gfx>` : image 1 de la timeline principale.
- cairosvg à l'échelle 1, puis Pillow : magenta pur rendu transparent, marges transparentes découpées. La réduction en palette de 256 couleurs n'est gardée que si l'écart reste dans la tolérance stricte de `exporter_decor.py` (`TOLERANCE_STRICTE`) : aucun buste ne la respecte (dégradés), les 24 PNG restent donc en RGBA, de 65 × 128 à 218 × 172 pixels, 457 445 octets au total.
- Durée mesurée : 2,5 secondes. Deux exécutions donnent des PNG identiques à l'octet.

Les couleurs personnalisées du conjoint (`c1|c2|c3` de `FS`) ne sont pas appliquées : le client les pose à l'exécution sur les clips colorables (`api.colors.addSprite`), ce qu'un PNG statique ne peut pas reproduire.

## Exécution

`Outil_Azur_complet.csproj` copie ce dossier vers `ressources/Bot/Artworks/Faces` à côté de l'exécutable (cible générique `CopyBotAssets`). Le volet Amis le lit par `ClientAssets.GetAsync("Artworks", "Faces/<gfx>")`, hors du thread de l'interface ; un PNG absent ou illisible laisse la fiche du conjoint avec son initiale.

Les images conservent les droits de leurs titulaires d'origine ; aucun SWF du client n'est versionné.
