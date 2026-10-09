# Smileys : images du client utilisées par le bot

Source : `clips/smileys/<n>.swf` du client Dofus 1.34 remis par l'utilisateur (`04 - Dofus 1.34 - Qu'Tan et Ilyzaelle`). Les SWF ne sont pas versionnés ; seuls ces PNG, qui en sont dérivés, le sont.

Usage : Smileys du chat (`BS<n>`, `cS<id>|<n>`) : 1 à 15 dans le panneau « Smileys » (`SMILEYS_ICONS_PATH + n`), 91 à 99 pour les jauges de la monture. `all.swf`, qui regroupe les mêmes smileys pour le mode « streaming » du client (`SMILEYS_ICONS_PATH + "all.swf"`), n'est pas exporté.

Fichiers : `Smileys/<n>.png` — 24 PNG (24 fichiers écrits), 0.0 Mo. Copiés à côté de l'exécutable dans `ressources/Bot/Smileys` par la cible `CopyBotAssets` du projet ; lus par `Outil_Azur_complet/Bot/ClientAssets.cs` (`ClientAssets.Get("Smileys", nom)`, nom relatif sans `.png`), qui renvoie `null` si un fichier manque ou est illisible.

Outil : `tools/client-analysis/exporter_icons.py` (`swfsvg` 0.2.2, cairosvg, Pillow) : rendu SVG par `swfsvg` (--scene, échelle 2, côté le plus long limité à 48 px), PNG par cairosvg, magenta pur rendu transparent, palette de 256 couleurs quand l'écart moyen par canal reste sous 4,5/255 sur les pixels visibles.

Commande exacte (depuis la racine du dépôt) :

```sh
python3 tools/client-analysis/exporter_icons.py --client "<client 1.34>" --sortie Outil_Azur_complet/Resources/Bot --familles Smileys
```

Date : 2026-10-04.

SWF ignorés : nom non numérique, hors des chemins `<dossier>/<n>.swf` que compose le client (1) :

- `clips/smileys/all.swf`

Les illustrations conservent les droits de leurs titulaires d'origine, comme celles de `../Selection` et `../Client`.
