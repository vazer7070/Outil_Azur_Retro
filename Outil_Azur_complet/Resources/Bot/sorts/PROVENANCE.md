# Spells : images du client utilisées par le bot

Source : `clips/spells/icons/<id>.swf` du client Dofus 1.34 remis par l'utilisateur (`04 - Dofus 1.34 - Qu'Tan et Ilyzaelle`). Les SWF ne sont pas versionnés ; seuls ces PNG, qui en sont dérivés, le sont.

Usage : Icônes de sorts (barre de sorts, fiche de sort). Le dossier contenait déjà 512 icônes de 80 × 80, versionnées sans provenance ; l'outil n'exporte que les icônes absentes (sauf `--remplacer`), au même format : un rendu de ces mêmes SWF par l'outil diffère des 512 existantes de moins de 1,3/255 en moyenne par canal (vérifié sur 0, 1, 100 et 161).

Fichiers : `sorts/<id>.png` — 13 PNG (13 fichiers écrits), 0.0 Mo. Copiés à côté de l'exécutable dans `ressources/Bot/sorts` par la cible `CopyBotAssets` du projet ; lus par `Outil_Azur_complet/Bot/ClientAssets.cs` (`ClientAssets.Get("Spells", nom)`, nom relatif sans `.png`), qui renvoie `null` si un fichier manque ou est illisible.

Outil : `tools/client-analysis/exporter_icons.py` (`swfsvg` 0.2.2, cairosvg, Pillow) : rendu SVG par `swfsvg` (--scene, échelle 2, côté le plus long limité à 80 px), PNG par cairosvg, magenta pur rendu transparent, palette de 256 couleurs quand l'écart moyen par canal reste sous 4,5/255 sur les pixels visibles.

Icônes écrites par l'outil (13) : `202.png`, `706.png`, `708.png`, `709.png`, `710.png`, `765.png`, `766.png`, `767.png`, `768.png`, `769.png`, `888.png`, `1369.png`, `1372.png`.

Commande exacte (depuis la racine du dépôt) :

```sh
python3 tools/client-analysis/exporter_icons.py --client "<client 1.34>" --sortie Outil_Azur_complet/Resources/Bot --familles Spells
```

Date : 2026-10-04.

Les illustrations conservent les droits de leurs titulaires d'origine, comme celles de `../Selection` et `../Client`.
