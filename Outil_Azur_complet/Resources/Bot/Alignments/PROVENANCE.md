# Alignments : images du client utilisées par le bot

Source : `clips/alignments/{,mini/,orders/,feats/}<n>.swf` du client Dofus 1.34 remis par l'utilisateur (`04 - Dofus 1.34 - Qu'Tan et Ilyzaelle`). Les SWF ne sont pas versionnés ; seuls ces PNG, qui en sont dérivés, le sont.

Usage : Alignements (`ALIGNMENTS_PATH`), petites icônes à côté des noms (`ALIGNMENTS_MINI_PATH + alignement`), ordres et dons (`ORDERS_PATH`, `FEATS_PATH`).

Fichiers : `Alignments/{,mini/,orders/,feats/}<n>.png` — 29 PNG (29 fichiers écrits), 0.1 Mo. Copiés à côté de l'exécutable dans `ressources/Bot/Alignments` par la cible `CopyBotAssets` du projet ; lus par `Outil_Azur_complet/Bot/ClientAssets.cs` (`ClientAssets.Get("Alignments", nom)`, nom relatif sans `.png`), qui renvoie `null` si un fichier manque ou est illisible.

Outil : `tools/client-analysis/exporter_icons.py` (`swfsvg` 0.2.2, cairosvg, Pillow) : rendu SVG par `swfsvg` (--scene, échelle 2, côté le plus long limité à 64 px), PNG par cairosvg, magenta pur rendu transparent, palette de 256 couleurs quand l'écart moyen par canal reste sous 4,5/255 sur les pixels visibles.

Commande exacte (depuis la racine du dépôt) :

```sh
python3 tools/client-analysis/exporter_icons.py --client "<client 1.34>" --sortie Outil_Azur_complet/Resources/Bot --familles Alignments
```

Date : 2026-10-04.

Les illustrations conservent les droits de leurs titulaires d'origine, comme celles de `../Selection` et `../Client`.
