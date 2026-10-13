# Portraits : images du client utilisées par le bot

Source : `clips/artworks/big/<gfx>.swf` du client Dofus 1.34 remis par l'utilisateur (`04 - Dofus 1.34 - Qu'Tan et Ilyzaelle`). Les SWF ne sont pas versionnés ; seuls ces PNG, qui en sont dérivés, le sont.

Usage : Portraits des PNJ et des monstres dans la fenêtre de dialogue (`ARTWORKS_BIG_PATH + gfx` ou `customArtwork`).

Fichiers : `Portraits/<gfx>.png` — 709 PNG (709 fichiers écrits), 9.3 Mo. Copiés à côté de l'exécutable dans `ressources/Bot/Portraits` par la cible `CopyBotAssets` du projet ; lus par `Outil_Azur_complet/Bot/ClientAssets.cs` (`ClientAssets.Get("Portraits", nom)`, nom relatif sans `.png`), qui renvoie `null` si un fichier manque ou est illisible.

Outil : `tools/client-analysis/exporter_icons.py` (`swfsvg` 0.2.2, cairosvg, Pillow) : rendu SVG par `swfsvg` (--scene, échelle 1, côté le plus long limité à 320 px), PNG par cairosvg, magenta pur rendu transparent, palette de 256 couleurs quand l'écart moyen par canal reste sous 4,5/255 sur les pixels visibles.

Commande exacte (depuis la racine du dépôt) :

```sh
python3 tools/client-analysis/exporter_icons.py --client "<client 1.34>" --sortie Outil_Azur_complet/Resources/Bot --familles Portraits
```

Date : 2026-10-04.

Non exportés (1) :

- `clips/artworks/big/9058.swf` : rendu vide

Sans dessin dans le SWF même (sprite ou scène vide), donc sans PNG (1) :

- `clips/artworks/big/884.swf`

Les illustrations conservent les droits de leurs titulaires d'origine, comme celles de `../Selection` et `../Client`.
