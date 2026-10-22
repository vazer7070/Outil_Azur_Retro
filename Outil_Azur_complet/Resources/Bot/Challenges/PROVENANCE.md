# Challenges : images du client utilisées par le bot

Source : `clips/challenges/<g>.swf et modules/core.swf (symboles ChallengeOK, ChallengeKO)` du client Dofus 1.34 remis par l'utilisateur (`04 - Dofus 1.34 - Qu'Tan et Ilyzaelle`). Les SWF ne sont pas versionnés ; seuls ces PNG, qui en sont dérivés, le sont.

Usage : Défis de combat (`Gd`, lot X9) : icône d'un défi (`FIGHT_CHALLENGE_PATH + FC[id].g`, `g` venant de `fightChallenge_fr`), affichée en 38 × 38 dans la colonne des défis (`FightChallengeIcon`) et en 17 × 17 dans le résultat du combat ; `ChallengeOK.png` (coche) et `ChallengeKO.png` (croix) sont les symboles que le client pose sur l'icône d'un défi réussi ou raté (`_ldrState`).

Fichiers : `Challenges/<g>.png, Challenges/ChallengeOK.png, Challenges/ChallengeKO.png` — 50 PNG (50 fichiers écrits), 0.1 Mo. Copiés à côté de l'exécutable dans `ressources/Bot/Challenges` par la cible `CopyBotAssets` du projet ; lus par `Outil_Azur_complet/Bot/ClientAssets.cs` (`ClientAssets.Get("Challenges", nom)`, nom relatif sans `.png`), qui renvoie `null` si un fichier manque ou est illisible.

Outil : `tools/client-analysis/exporter_icons.py` (`swfsvg` 0.2.6, cairosvg, Pillow) : rendu SVG par `swfsvg` (--scene pour les icônes (côté le plus long limité à 76 px), nom d'export pour les deux symboles ; échelle 2), PNG par cairosvg, magenta pur rendu transparent, palette de 256 couleurs quand l'écart moyen par canal reste sous 4,5/255 sur les pixels visibles.

Commande exacte (depuis la racine du dépôt) :

```sh
python3 tools/client-analysis/exporter_icons.py --client "<client 1.34>" --sortie Outil_Azur_complet/Resources/Bot --familles Challenges
```

Date : 2026-10-22.

Les illustrations conservent les droits de leurs titulaires d'origine, comme celles de `../Selection` et `../Client`.
