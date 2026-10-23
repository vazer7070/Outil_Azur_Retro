# Emblems : images du client utilisées par le bot

Source : `clips/emblems/back/<n>.swf, clips/emblems/up/<n>.swf` du client Dofus 1.34 remis par l'utilisateur (`04 - Dofus 1.34 - Qu'Tan et Ilyzaelle`). Les SWF ne sont pas versionnés ; seuls ces PNG, qui en sont dérivés, le sont.

Usage : Emblèmes de guilde (`Emblem` du client) : le fond `back/<n>` et le motif `up/<n>` sont recolorés à l'exécution par `Color.setRGB`. `back/<n>.png` est l'instance `back` seule (la partie recolorée par la couleur du fond), `back/<n>_contour.png` tout le reste du fond, dans le même cadre ; `up/<n>.png` est recoloré en entier par la couleur du motif. `ClientAssets.Emblem` compose les trois comme le composant `Emblem` de `core.swf` (fond ajusté à 78 × 78 en (1, 1), motif à 50 × 50 en (15, 15), cadre de 80 × 80).

Fichiers : `Emblems/back/<n>.png + Emblems/back/<n>_contour.png, Emblems/up/<n>.png` — 121 PNG (138 fichiers écrits), 0.2 Mo. Copiés à côté de l'exécutable dans `ressources/Bot/Emblems` par la cible `CopyBotAssets` du projet ; lus par `Outil_Azur_complet/Bot/ClientAssets.cs` (`ClientAssets.Get("Emblems", nom)`, nom relatif sans `.png`), qui renvoie `null` si un fichier manque ou est illisible.

Outil : `tools/client-analysis/exporter_icons.py` (`swfsvg` 0.2.2, cairosvg, Pillow) : rendu SVG par `swfsvg` (--scene ; calques obtenus en retirant ou en gardant seule l'instance `back` d'une copie du SWF ; échelle 2), PNG par cairosvg, magenta pur rendu transparent, palette de 256 couleurs quand l'écart moyen par canal reste sous 4,5/255 sur les pixels visibles.

Commande exacte (depuis la racine du dépôt) :

```sh
python3 tools/client-analysis/exporter_icons.py --client "<client 1.34>" --sortie Outil_Azur_complet/Resources/Bot --familles Emblems
```

Date : 2026-10-04.

Les illustrations conservent les droits de leurs titulaires d'origine, comme celles de `../Selection` et `../Client`.
