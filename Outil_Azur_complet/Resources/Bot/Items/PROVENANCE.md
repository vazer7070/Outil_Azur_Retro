# Items : images du client utilisées par le bot

Source : `clips/items/<type>/<gfx>.swf` du client Dofus 1.34 remis par l'utilisateur (`04 - Dofus 1.34 - Qu'Tan et Ilyzaelle`). Les SWF ne sont pas versionnés ; seuls ces PNG, qui en sont dérivés, le sont.

Usage : Icônes d'objets (inventaire, boutique, échanges) : chemin `ITEMS_PATH + type + "/" + gfx` du client ; `type` et `gfx` d'un modèle d'objet viennent de `items_fr` (`I.u[id].t`, `I.u[id].g`).

Fichiers : `Items/<type>/<gfx>.png` — 4668 PNG (4668 fichiers écrits), 14.7 Mo. Copiés à côté de l'exécutable dans `ressources/Bot/Items` par la cible `CopyBotAssets` du projet ; lus par `Outil_Azur_complet/Bot/ClientAssets.cs` (`ClientAssets.Get("Items", nom)`, nom relatif sans `.png`), qui renvoie `null` si un fichier manque ou est illisible.

Outil : `tools/client-analysis/exporter_icons.py` (`swfsvg` 0.2.2, cairosvg, Pillow) : rendu SVG par `swfsvg` (--scene, échelle 2, côté le plus long limité à 80 px), PNG par cairosvg, magenta pur rendu transparent, palette de 256 couleurs quand l'écart moyen par canal reste sous 4,5/255 sur les pixels visibles.

Commande exacte (depuis la racine du dépôt) :

```sh
python3 tools/client-analysis/exporter_icons.py --client "<client 1.34>" --sortie Outil_Azur_complet/Resources/Bot --familles Items
```

Date : 2026-10-04.

Sans dessin dans le SWF même (sprite ou scène vide), donc sans PNG (1) :

- `clips/items/15/488.swf`

SWF ignorés : nom non numérique, hors des chemins `<dossier>/<n>.swf` que compose le client (3) :

- `clips/items/11/a1.swf`
- `clips/items/16/111_bis.swf`
- `clips/items/41/Sans nom-1.swf`

Les illustrations conservent les droits de leurs titulaires d'origine, comme celles de `../Selection` et `../Client`.
