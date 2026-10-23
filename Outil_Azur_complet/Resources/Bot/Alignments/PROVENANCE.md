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

## Ailes d'alignement (lot AN8) : `ailes/`

Le client dessine les ailes dans la surtête (`TextOverHead.drawGfx`), derrière le nom, à l'origine de la
surtête (haut du sprite, centré) : `clips/demonangel.swf`, ou `clips/fallenDemonAngel.swf` pour un ange
ou démon déchu (5e champ de l'alignement du `GM` à 1), 30 images arrêtées sur une seule : image = grade
(Bonta), 10 + grade (Brâkmar), 20 + grade (mercenaire), seulement quand le grade (3e champ) dépasse 0.
`pvpGain` ajoute un filtre : −1, matrice de couleurs RVB × 0,5 ; 1, lueur `0xAAEAFF` (Bonta),
`0xCC0000` (Brâkmar) ou `0x000000` (mercenaire), alpha 0,5, flou 10 × 10, force 2.

Fichiers : `ailes/1_scene.png` (`demonangel.swf`, 30 images de 162 × 136, cadre −81, −94), `ailes/2_scene.png`
(`fallenDemonAngel.swf`, 30 images de 150 × 136, cadre −75, −94), `ailes/effets.tsv` (format d'`ancres.tsv`,
voir `../sprites/PROVENANCE.md` ; la colonne `ips` 40 et la fin `boucle` sont celles de la timeline, que le
client n'anime pas). L'image `n` du client (1 à 30) est la colonne `n − 1` de la bande. Lus par
`OverheadLayer` (`OverheadImages`). `exporter_sorts.py` n'acceptant que des SWF nommés par un nombre, les
deux SWF sont copiés en `1.swf` et `2.swf` avant l'export. Commande, depuis la racine du dépôt :

```sh
SW=tools/client-analysis/swfsvg/target/release/swfsvg   # 0.2.5
mkdir -p /tmp/ailes && cp "<client>/clips/demonangel.swf" /tmp/ailes/1.swf && cp "<client>/clips/fallenDemonAngel.swf" /tmp/ailes/2.swf
python3 tools/client-analysis/exporter_sorts.py /tmp/ailes Outil_Azur_complet/Resources/Bot/Alignments/ailes --gfx 1,2 --swfsvg $SW --jobs 2
```

Mesuré le 12 octobre 2026 : 231 945 et 251 389 octets (483 334 en tout, ≈ 0,3 Mo prévus), en RGBA (les
dégradés d'alpha refusent la palette). Date : 2026-10-12.
