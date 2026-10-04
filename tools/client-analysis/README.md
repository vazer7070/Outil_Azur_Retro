# Analyse du client Dofus 1.34

Outils qui ont servi à relever le protocole et les écrans du client Dofus 1.34.1 fourni
(`modules/loader.swf` pour le code, `modules/core.swf` pour les graphismes). Le code
décompilé et les SWF du client ne sont pas versionnés : seuls les outils et les
références qui en découlent (`docs/PROTOCOLE_CLIENT_1_34.md`, `ressources/Bot/ui/`) le sont.

## Prérequis

- Rust (cargo) pour `avm1dump` et `swfsvg` (dépendance : la crate `swf` de Ruffle).
- Python 3 pour `as2lite.py` et `relever_protocole.py` ; `pip install cairosvg` pour `exporter_png.py`.

## Chaîne complète

```sh
# 1. Désassemblage AVM1 en suivant le flux (prédicats opaques repliés, pools de constantes réels)
cargo run --release --manifest-path avm1dump/Cargo.toml -- <client>/modules/loader.swf loader.avm1.txt

# 2. Pseudo-décompilation par simulation de pile
python3 as2lite.py loader.avm1.txt loader.as.txt

# 3. Référence du protocole (routes serveur → client, envois, lecture des réponses)
python3 relever_protocole.py loader.as.txt ../../docs/PROTOCOLE_CLIENT_1_34.md tables/

# 4. Export des symboles graphiques de core.swf en SVG (tous, ou une liste de noms)
cargo run --release --manifest-path swfsvg/Cargo.toml -- <client>/modules/core.swf svg/ UI_Login ButtonNormalUp
#    Scène d'un SWF sans export (icône d'objet, émote, portrait), cycle de marche image par image, liste
cargo run --release --manifest-path swfsvg/Cargo.toml -- --scene --name 16_1234 <client>/clips/items/16/1234.swf svg/
cargo run --release --manifest-path swfsvg/Cargo.toml -- --frame all <client>/clips/sprites/10.swf svg/ walkR
cargo run --release --manifest-path swfsvg/Cargo.toml -- --list <client>/clips/sprites/10.swf

# 5. Conversion en PNG (échelle facultative)
python3 exporter_png.py svg/ png/ 1
```

## Ce que fait chaque outil

| Outil | Rôle |
|---|---|
| `avm1dump` | Lit les tags DoAction / DoInitAction / DefineSprite / ExportAssets, désassemble chaque bloc en suivant les sauts (le code de remplissage entre deux `Jump` est ignoré), replie les prédicats opaques du client (`Push "x"; CharToAscii; If`, `GetTime; Increment; If`, `Push false; Not; If`), choisit le pool de constantes réel parmi les leurres et écrit les instructions dans l'ordre de visite avec des étiquettes `::@offset`. |
| `as2lite.py` | Reconstruit des expressions (`this.aks.send("GA" + p1)`, `p4.split("|")`…) à partir de la sortie précédente. Les boucles ne sont pas restructurées : elles apparaissent sous forme de `goto`. Les paramètres obfusqués sont renommés `p1`, `p2`… |
| `relever_protocole.py` | Suit les chaînes `if (r0 === "x") goto` des méthodes `onMessage` de `dofus.aks.*` pour recomposer les préfixes, relève les `aks.send(...)` et résume la lecture de chaque réponse (`split`, indices, `parseInt`). |
| `swfsvg` | Exporte en SVG un symbole exporté (forme, clip, bouton à l'état relâché) ou la scène d'un SWF, à l'image voulue : dégradés, bitmaps JPEG/sans perte en base64, transformations de couleur, masques, formes morphées. Les aplats magenta `#FF00FF` sont des emplacements remplis à l'exécution : ils sont omis. Écrit aussi `index.tsv` (cadre de chaque rendu). Voir la section suivante. |
| `exporter_png.py` | Convertit les SVG en PNG avec cairosvg. |

## `swfsvg` : symboles, scène, images et index

```text
swfsvg [--frame N|A-B|all] [--append-index] <fichier.swf> <dossier> [nomExport ...]
swfsvg --scene [--name NOM] [--frame N|A-B|all] [--append-index] <fichier.swf> <dossier>
swfsvg --list <fichier.swf>
```

- **Sans option** (forme historique, inchangée) : l'image 1 de chaque symbole d'`ExportAssets`, ou des
  seuls noms donnés, dans `<dossier>/<nom>.svg`. Un nom absent du SWF est signalé sur la sortie
  d'erreur sans faire échouer la commande (les monstres n'ont souvent ni `staticS` ni `staticF`).
- **`--scene`** rend la timeline principale, ce que le client affiche quand il charge le SWF comme
  clip : c'est le seul moyen d'exporter les icônes d'objets (`clips/items/<type>/<gfx>.swf`), émotes,
  métiers, emblèmes, alignements et portraits (`clips/artworks/big`), qui posent leurs formes sur
  la scène sans aucun export. Le fichier s'appelle comme le SWF (`1234.svg`) ou `--name` (utile quand
  plusieurs familles ont des fichiers de même nom, `items/1/1.swf`, `jobs/1.swf`…).
- **`--frame N`** rend l'image N (à partir de 1) de la timeline demandée, comme un `gotoAndStop(N)`.
  Les clips imbriqués, eux, jouent comme à l'écran depuis leur création : ils bouclent et s'arrêtent
  sur leur `stop()`. C'est indispensable pour les sprites du client : `walkR` n'a qu'une image et
  contient le vrai cycle de 26 images ; `--frame 5` de `walkR` donne donc la 5ᵉ image du pas.
  **`--frame A-B`** et **`--frame all`** (toutes les images utiles, voir `--list`) écrivent un
  fichier par image, `<nom>_f001.svg`, `<nom>_f002.svg`…, tous dans **le même cadre** (union des
  cadres de la série) : les PNG ont la même taille et la même ancre, prêts à être assemblés en bande.
- **`--list`** écrit sur la sortie standard, sans rien rendre, une ligne par symbole exporté,
  précédée de la scène : `nom`, `id`, `type` (`forme`, `clip`, `bouton`, `bitmap`, `morph`,
  `texte`, `absent`…), `images` (images utiles : la timeline et les clips qu'elle contient, jusqu'à
  leur `stop()`), `images_timeline` (images de la timeline du symbole seule). Exemple sur
  `sprites/1001.swf` : `walkR clip 26 1`, `runR clip 16 1`, `staticR clip 1 1`, `emote16R clip 122 122`.
- **`--append-index`** ajoute les lignes à un `index.tsv` existant au lieu de le réécrire, pour
  rassembler toute une famille de SWF dans un seul dossier.

`index.tsv` (sans ligne d'en-tête) compte une ligne par SVG écrit, colonnes séparées par des
tabulations ; les sept premières sont celles des versions précédentes :

| n° | colonne | contenu |
|---|---|---|
| 1 | `nom` | nom d'export, ou nom de la scène (`--name` / nom du SWF) |
| 2 | `id` | identifiant du caractère (0 pour la scène) |
| 3-4 | `xmin`, `ymin` | coin haut-gauche du cadre, en pixels, dans le repère du symbole : le point (0, 0) est le point d'ancrage du client (pied d'un personnage, origine de la scène) |
| 5-6 | `largeur`, `hauteur` | taille du cadre |
| 7 | `avertissements` | éléments non rendus (texte statique, bitmap illisible…), séparés par `; ` |
| 8-9 | `xmax`, `ymax` | coin bas-droit du cadre |
| 10 | `image` | image rendue (à partir de 1) |
| 11 | `images` | images utiles du symbole (comme `--list`) |
| 12 | `fichier` | nom du SVG écrit |

Le SVG est cadré au pixel : son `viewBox` part de (⌊xmin⌋, ⌊ymin⌋). Après `exporter_png.py … <échelle>`,
le pixel (0, 0) du PNG correspond donc au point (⌊xmin⌋, ⌊ymin⌋) du symbole ; pour poser le PNG comme
le client, on le dessine à `ancre + (⌊xmin⌋, ⌊ymin⌋) × échelle`.

Rendu : les masques (`clipDepth`) deviennent des `<clipPath>` et le cadre se limite à la partie
visible ; les formes morphées sont interpolées au `ratio` de leur placement ; une forme vide ne
compte pas dans le cadre. Les dégradés et les remplissages bitmap sont exprimés dans le repère de la
forme, à l'intérieur du `<g transform>` de son placement (avant la version 0.2.1, la pose du clip leur
était appliquée deux fois : motifs minuscules répétés, ombres en damier). Quelques symboles `static*` du client sont en réalité des animations
(`sprites/1219.swf` : l'épouvantail sort du sol, caché par un masque à l'image 1) : `--list` en
donne le nombre d'images et `--frame` permet de choisir une image représentative. Un SWF illisible
arrête la commande avec un message et le code 1 (2 pour une option invalide), jamais une panique.

Tests : `cargo test` dans `swfsvg/` (14 tests ; le SWF de test est fabriqué par les tests avec la
crate `swf`, aucun fichier du client n'est nécessaire).

Temps mesurés (conteneur 4 cœurs, un processus par SWF, binaire `--release`) :

| Commande | SWF | Durée | Sortie |
|---|---|---|---|
| `--list` | 933 sprites (`clips/sprites`) | 7,3 s | — |
| `--scene --name <type>_<gfx> --append-index` | 4 672 icônes d'objets (`clips/items`) | 27 à 60 s selon la charge | 4 672 SVG, 105 Mo ; avertissements : texte statique dans 11 SWF, caractère absent dans 2 |
| `--scene --append-index` | 711 portraits (`clips/artworks/big`) | 5,1 s | 711 SVG, 51 Mo |
| `--frame all … walkS … runB` (10 cycles) | 933 sprites | 166 s | 103 101 SVG, 3,4 Go (bitmaps recopiés dans chaque image), aucun avertissement |
| idem | 24 sprites de classes (gfx 10 à 121) | 6,3 s | 5 978 SVG, 132 Mo |
| `exporter_png.py … 1` | cycles de la classe 10 | 7,7 s | 242 PNG, 980 Ko |
| forme historique (tous les exports) | `clips/gfx` (14 SWF) et `core.swf` | 17,4 s | 6 371 SVG ; les formes morphées de `clips/gfx`, auparavant non rendues, le sont |

Robustesse : `--list` et `--scene` sur les 7 735 SWF de `clips/` (195 s) puis `--frame all` sur tous
les exports d'un sprite sur dix (93 SWF, 260 s) : aucun échec, aucune panique.

## Limites connues

- Les JPEG Flash (segment de tables puis image, `FF D9 FF D8` au milieu des données, `JPEGTables` partagé) sont recollés avant décodage ; un bitmap encore illisible est signalé dans `index.tsv` et sa zone reste vide.
- Les écrans construits à l'exécution (bandeau, inventaire, sorts, options) n'ont que peu d'art statique : seul leur cadre est exporté.
- `swfsvg` ne rend pas les filtres ni les modes de fusion (`PlaceObject3` : ombres, lueurs), ni le texte statique (seul son cadre compte), ni les champs de texte. Il n'exécute pas le code des images : un `stop()` est repéré même sous condition, les `gotoAndPlay`, la recoloration des personnages et les accessoires posés par le client ne sont pas reproduits.
- La décompilation est une pseudo-décompilation : elle suffit à lire les formats de paquets, pas à recompiler le client.
