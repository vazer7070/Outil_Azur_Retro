# Analyse du client Dofus 1.34

Outils qui ont servi à relever le protocole et les écrans du client Dofus 1.34.1 fourni
(`modules/loader.swf` pour le code, `modules/core.swf` pour les graphismes). Le code
décompilé et les SWF du client ne sont pas versionnés : seuls les outils et les
références qui en découlent (`docs/PROTOCOLE_CLIENT_1_34.md`, `ressources/Bot/ui/`) le sont.

## Prérequis

- Rust (cargo) pour `avm1dump` et `swfsvg` (dépendance : la crate `swf` de Ruffle).
- Python 3 pour `as2lite.py` et `relever_protocole.py` ; `pip install cairosvg` pour `exporter_png.py` ; `pip install cairosvg pillow` pour `exporter_sprites.py`, `exporter_icons.py` et leurs tests (sous Windows, cairosvg demande aussi la bibliothèque Cairo, `libcairo-2.dll`).

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

# 6. Sprites d'acteurs du bot : images de repos, cycles de marche et de course, ancres
python3 exporter_sprites.py <client>/clips/sprites ../../Outil_Azur_complet/Resources/Bot/sprites \
    --swfsvg swfsvg/target/release/swfsvg --jobs 4
```

## Ce que fait chaque outil

| Outil | Rôle |
|---|---|
| `avm1dump` | Lit les tags DoAction / DoInitAction / DefineSprite / ExportAssets, désassemble chaque bloc en suivant les sauts (le code de remplissage entre deux `Jump` est ignoré), replie les prédicats opaques du client (`Push "x"; CharToAscii; If`, `GetTime; Increment; If`, `Push false; Not; If`), choisit le pool de constantes réel parmi les leurres et écrit les instructions dans l'ordre de visite avec des étiquettes `::@offset`. |
| `as2lite.py` | Reconstruit des expressions (`this.aks.send("GA" + p1)`, `p4.split("|")`…) à partir de la sortie précédente. Les boucles ne sont pas restructurées : elles apparaissent sous forme de `goto`. Les paramètres obfusqués sont renommés `p1`, `p2`… |
| `relever_protocole.py` | Suit les chaînes `if (r0 === "x") goto` des méthodes `onMessage` de `dofus.aks.*` pour recomposer les préfixes, relève les `aks.send(...)` et résume la lecture de chaque réponse (`split`, indices, `parseInt`). |
| `swfsvg` | Exporte en SVG un symbole exporté (forme, clip, bouton à l'état relâché) ou la scène d'un SWF, à l'image voulue : dégradés, bitmaps JPEG/sans perte en base64, transformations de couleur, masques, formes morphées. Les aplats magenta `#FF00FF` sont des emplacements remplis à l'exécution : ils sont omis, sauf sous une transformation de couleur du SWF qui remplace la teinte (0.2.2). Écrit aussi `index.tsv` (cadre de chaque rendu). Voir la section suivante. |
| `exporter_png.py` | Convertit les SVG en PNG avec cairosvg. |
| `exporter_sprites.py` | Enchaîne `swfsvg --list`, `--frame` et `--scene` sur `clips/sprites/<gfx>.swf`, convertit avec cairosvg, rogne les marges transparentes, assemble les cycles en bandes et écrit `ancres.tsv`. Voir la section « Sprites d'acteurs ». |
| `exporter_icons.py` | Exporte les icônes et images que le client charge à l'exécution (objets, portraits, smileys, émotes, métiers, alignements, emblèmes, carte du monde, sorts manquants, symboles du bandeau et du chat de `core.swf`) vers `Outil_Azur_complet/Resources/Bot/<Famille>`, avec un `PROVENANCE.md` par famille. Voir la section « Icônes du client ». |
| `exporter_groupe.py` | Exporte les petites illustrations `clips/artworks/mini` et les éléments du volet `Party` de `core.swf` (couronne, flèche du suivi, infobulle) vers `Outil_Azur_complet/Resources/Bot/{Artworks/Mini,Party}`. Voir la section « Volet Groupe ». |
| `exporter_artworks.py` | Exporte les bustes des classes `clips/artworks/faces/<gfx>.swf` (scène principale, gfx = classe × 10 + sexe) vers `Outil_Azur_complet/Resources/Bot/Artworks/Faces` pour la fiche du conjoint du volet Amis ; commande et provenance dans son `PROVENANCE.md`, tests dans `tests/test_exporter_artworks.py`. |
| `exporter_quetes.py` | Exporte de `modules/core.swf` les pièces de la fenêtre des quêtes (icône d'expérience, coche, marque en cours, flèche d'étape, boussole d'objectif) vers `Outil_Azur_complet/Resources/Bot/Client`, en réutilisant le rendu d'`exporter_icons.py` ; commande dans le `PROVENANCE.md` de ce dossier. |

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
était appliquée deux fois : motifs minuscules répétés, ombres en damier). Depuis la version 0.2.2, la partie
linéaire des matrices de dégradé et de motif est écrite avec six décimales (les icônes d'objets ont des
coefficients de l'ordre de 0,001 qu'un arrondi à trois décimales rendait singuliers : cairo refusait alors
toute l'image), un dégradé dégénéré (aire de son carré de 1 638,4 px inférieure à 0,05 px²) est remplacé
par la couleur de son milieu, et un aplat magenta sous une transformation de couleur qui remplace la teinte
(multiplicateurs RGB nuls, comme `Color.setRGB` : le cœur des points de vie de `core.swf`) est gardé. Quelques symboles `static*` du client sont en réalité des animations
(`sprites/1219.swf` : l'épouvantail sort du sol, caché par un masque à l'image 1) : `--list` en
donne le nombre d'images et `--frame` permet de choisir une image représentative. Un SWF illisible
arrête la commande avec un message et le code 1 (2 pour une option invalide), jamais une panique.

Tests : `cargo test` dans `swfsvg/` (17 tests ; le SWF de test est fabriqué par les tests avec la
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

## Sprites d'acteurs : `exporter_sprites.py`

```text
exporter_sprites.py <client>/clips/sprites <sortie> [--swfsvg CHEMIN] [--animes FICHIER]
                    [--gfx 10,11,...] [--echelle 1] [--jobs N] [--sans-palette]
```

Requiert swfsvg 0.2.1 ou plus (`--swfsvg`, sinon `$SWFSVG`, le `PATH` ou `swfsvg/target/release`),
cairosvg et Pillow. Pour chaque `<gfx>.swf` :

- `<gfx>_static<O>.png` (`O` = `S`, `R`, `L`, `F`, `B`, casse du nom d'export ignorée) : la **dernière
  image utile** du symbole (`--list`), celle où le client s'arrête après l'animation de repos ;
- `<gfx>_scene.png` quand le SWF n'exporte aucun `static<O>` (épées de combat 0-5, tombes) : image 1
  de la scène ;
- pour les gfx du fichier `--animes` (par défaut `<sortie>/sprites_animes.txt`, un gfx par ligne) :
  `<gfx>_walk<O>.png` et `<gfx>_run<O>.png`, bandes horizontales de toutes les images utiles rendues
  dans un même cadre (`--frame all`) ;
- `<sortie>/ancres.tsv` : `gfx anim xmin ymin largeur hauteur images` (en-tête compris), où le point
  d'ancrage du client est le pixel (`-xmin`, `-ymin`) de chaque image.

Les marges transparentes sont rognées (cadre commun pour une bande), le magenta est effacé comme dans
`exporter_png.py`, la palette 8 bits n'est retenue que si elle ne change presque rien aux pixels
visibles. `--gfx` remplace seulement les lignes et les PNG des gfx cités ; les autres fichiers du
dossier (les anciens `<gfx><O>.png` du bot) ne sont jamais touchés. Un SWF illisible est signalé et
la série continue. Le détail des conventions et la commande utilisée pour le dépôt sont dans
`Outil_Azur_complet/Resources/Bot/sprites/PROVENANCE.md`.

| Commande | Durée (4 cœurs) | Sortie |
|---|---|---|
| tous les sprites, `--jobs 4`, 24 gfx animés | 3 min 10 s | 2 556 PNG, 16,1 Mo, `ancres.tsv` de 2 556 lignes |

Tests : `python3 tests/test_exporter_sprites.py` (faux `swfsvg` écrit en Python dans `tests/`, aucun
fichier du client) : image de repos, casse, scène, bandes, magenta, rognage, échelle, `--gfx`.

## Limites connues

- Les JPEG Flash (segment de tables puis image, `FF D9 FF D8` au milieu des données, `JPEGTables` partagé) sont recollés avant décodage ; un bitmap encore illisible est signalé dans `index.tsv` et sa zone reste vide.
- Les écrans construits à l'exécution (bandeau, inventaire, sorts, options) n'ont que peu d'art statique : seul leur cadre est exporté.
- `swfsvg` ne rend pas les filtres ni les modes de fusion (`PlaceObject3` : ombres, lueurs), ni le texte statique (seul son cadre compte), ni les champs de texte. Il n'exécute pas le code des images : un `stop()` est repéré même sous condition, les `gotoAndPlay`, la recoloration des personnages et les accessoires posés par le client ne sont pas reproduits.
- La décompilation est une pseudo-décompilation : elle suffit à lire les formats de paquets, pas à recompiler le client.

## Textes de langue → XML du bot (`lang2xml.py`)

Les noms de PNJ, les dialogues, les noms de zones, les monstres, les objets, les sorts, les émotes et les messages `Im` ne sont dans aucune table de l'émulateur : le client les lit dans `lang/swf/<famille>_fr_<version>.swf` (un seul `DoAction` qui affecte des objets AS2 : `D.q[id] = "…"`, `N.d[id] = {n, a}`, `MA.m[id] = {x, y, sa…}`). `lang2xml.py` enchaîne `avm1dump`, `as2lite.py` et une lecture des affectations littérales (aucun code n'est exécuté ; une affectation répétée garde la dernière valeur), puis écrit un XML par famille pour `Tool_BotProtocol.Game.Data.LangData` :

```sh
# 13 familles lues par le bot (défaut) ou toutes les familles connues (28)
python3 lang2xml.py "<pack Lang>/dofus/lang/swf" ../../Outil_Azur_complet/Resources/Bot/BotLang \
    [--familles bot|toutes|dialog,npc…] [--avm1dump avm1dump/target/release/avm1dump] [--travail <dossier>]

# conversion d'une pseudo-décompilation déjà produite
python3 lang2xml.py --as npc_fr_508.as.txt --famille npc --version 508 --sortie npc.xml

# tests (affectations écrites dans le test, aucun SWF)
python3 test_lang2xml.py
```

La version de chaque famille est lue dans `lang/versions_fr.txt` quand le fichier existe, sinon la plus haute présente. Mesuré le 4 octobre 2026 : 28 familles en ≈ 20 s, 8,1 Mo de XML. Format, contenu et commande exacte : `Outil_Azur_complet/Resources/Bot/BotLang/PROVENANCE.md`.

## Décor des cartes (`exporter_decor.py`)

```sh
# swfsvg compilé au préalable (étape 4) ; Pillow et cairosvg : pip install pillow cairosvg
python3 exporter_decor.py <client> ../../Outil_Azur_complet/Resources/Bot/Decor
```

Le script exporte les symboles numérotés de `clips/gfx/g1.swf` et `g2.swf` (sols ; au-delà de 500 px, fonds désignés par `BACK`), `o1.swf` à `o11.swf` (objets) et `cell.swf` vers `sols/`, `backgrounds/`, `objets/` et `cellules/`, puis écrit `ancres.tsv` (`type id xmin ymin largeur hauteur image`, séparés par des tabulations) : coin haut gauche de chaque PNG par rapport au point d'enregistrement du symbole, c'est-à-dire la position de la cellule dans le client (l'origine de la carte pour un fond). `BotMapArtwork` dessine chaque PNG à « cellule + (xmin, ymin) ». Les marges transparentes sont découpées, les PNG passent en palette de 256 couleurs quand l'écart reste faible et un symbole vide devient un PNG transparent de 1 px. Le journal final énumère les identifiants présents dans deux bibliothèques (la première occurrence, dans l'ordre o1… o11, est gardée), les fonds de plus de 500 px, les SVG repris parce que cairosvg échoue sur des dégradés minuscules, les symboles vides et les avertissements de `swfsvg`. Une nouvelle exécution remplace les PNG numérotés écrits à la racine de chaque dossier ; une bibliothèque rangée en sous-dossiers reste intacte.

Mesuré sur le client 1.34 fourni (4 cœurs) : 5 649 PNG et 63,3 Mo en un peu plus de 4 minutes (voir `Outil_Azur_complet/Resources/Bot/Decor/PROVENANCE.md`). Les images 2 à 15 des sols, affichées sur les cellules en pente, ne sont exportées que si `swfsvg` accepte `--frame N` (le script le détecte) ; les formes morphées et les textes statiques ne sont pas rendus.

## Icônes du client (`exporter_icons.py`)

```sh
# swfsvg 0.2.2 compilé au préalable (étape 4) ; Pillow et cairosvg : pip install pillow cairosvg
python3 exporter_icons.py --client "<client 1.34>" --sortie ../../Outil_Azur_complet/Resources/Bot \
    [--familles Smileys,Emotes,Jobs,Alignments,Emblems,Portraits,Items,Spells,WorldMap,UI] \
    [--swfsvg swfsvg/target/release/swfsvg] [--processus N] [--travail <dossier>] [--limite N] [--remplacer]

# tests (SWF fabriqués dans le test, faux swfsvg de tests/, aucun fichier du client)
python3 tests/test_exporter_icons.py
```

| Famille | Source | Sortie (sous `--sortie`) | Rendu |
|---|---|---|---|
| `Smileys`, `Emotes` | `clips/smileys/<n>.swf`, `clips/emotes/<n>.swf` | `Smileys/<n>.png`, `Emotes/<n>.png` | scène, échelle 2, 48 px au plus |
| `Jobs`, `Alignments` | `clips/jobs/<g>.swf`, `clips/alignments/{,mini/,orders/,feats/}<n>.swf` | même arborescence | scène, échelle 2, 64 px au plus |
| `Emblems` | `clips/emblems/back/<n>.swf`, `clips/emblems/up/<n>.swf` | `Emblems/back/<n>.png` (instance `back` seule, à teinter), `back/<n>_contour.png` (le reste, même cadre), `up/<n>.png` | scène, échelle 2 |
| `Portraits` | `clips/artworks/big/<n>.swf` | `Portraits/<n>.png` | scène, échelle 1, 320 px au plus |
| `Items` | `clips/items/<type>/<gfx>.swf` | `Items/<type>/<gfx>.png` | scène, échelle 2, 80 px au plus |
| `Spells` | `clips/spells/icons/<id>.swf` | `sorts/<id>.png`, seulement les absents (sauf `--remplacer`) | scène, échelle 2, 80 px au plus |
| `WorldMap` | exports `x_y` et `subarea_<id>` de `clips/maps/<zone>.swf`, `hints.swf`, `dungeon.swf` | `WorldMap/<zone>/<x_y>.png` + `tuiles.tsv`, `<zone>/sous-zones/<id>.png` + `sous-zones.tsv`, `hints/<id>.png`, `dungeon.png` | symboles, échelle 1 (indices : 2, 48 px) |
| `UI` | symboles de `modules/core.swf` listés dans `SYMBOLES_UI` ; scènes de `SCENES_UI` (`clips/flag.swf` à l'image 30) | `Client/<Symbole>.png` (+ calques `StarBorder_fill`, `StarBorder_contour`, `Heart_vide`, `TimelineItem_fond`/`_vie`, `UI_ChallengeMenu_fond`/`_coche`, `UI_GameResultPlayer_mort`), `Client/FlagCell.png` | symboles, échelle 2 ; scène du drapeau, échelle 1 |

Le client affiche ces SWF dans un `Loader` qui ajuste le contenu à son cadre : le recadrage de `swfsvg` sur le contenu est donc le sien. Les transformations de couleur que `swfsvg` écrit en `feColorMatrix` (ignorés par cairosvg) sont appliquées aux couleurs mêmes, le magenta pur restant devient transparent, et chaque PNG passe en palette de 256 couleurs quand l'écart moyen par canal reste sous 4,5/255 sur les pixels visibles. Les calques recolorés à l'exécution (`Color.setRGB`) sont obtenus en réécrivant une copie du SWF sans l'instance nommée, ou avec elle seule, et posés dans le même cadre que le rendu complet. `tuiles.tsv` et `sous-zones.tsv` (`nom x y largeur hauteur`) donnent le coin haut gauche de chaque PNG dans le repère de la carte du monde au zoom 100 (tuile `x_y` posée en `x × 600`, `y × 345`). Quand cairo refuse un dégradé devenu presque ponctuel à une échelle inférieure à 1, l'image est rendue à l'échelle 1 puis réduite. Un SWF illisible ou vide est journalisé et listé dans le `PROVENANCE.md` de sa famille ; la série continue. Un SWF dont le nom n'est pas un nombre (hors `hints.swf` et `dungeon.swf`) n'est pas rendu, puisque le client compose ces chemins avec un identifiant numérique (seul `smileys/all.swf`, réservé à son mode « streaming », fait exception), et il est listé comme ignoré dans ce même fichier. La commande exacte et les fichiers non exportés de chaque famille sont dans `Outil_Azur_complet/Resources/Bot/<Famille>/PROVENANCE.md` (`Client/PROVENANCE.md` pour `UI`, `sorts/PROVENANCE.md` pour `Spells`).

Mesuré le 4 octobre 2026 sur le client 1.34 fourni (4 cœurs) : 6 208 SWF ou symboles en 2 min 40 s, 6 034 PNG et 29 Mo ; 191 contours de sous-zones de `0.swf`, le portrait `884` et l'objet `15/488` n'ont aucun dessin dans le SWF, et le portrait `9058` ne rend rien ; `clips/smileys/all.swf` et trois SWF d'objets au nom non numérique (`11/a1`, `16/111_bis`, `41/Sans nom-1`) sont ignorés. Une nouvelle exécution sur Smileys et Items (40 s) redonne des PNG identiques à l'octet.

## Volet Groupe (`exporter_groupe.py`)

```sh
python3 exporter_groupe.py <client> ../../Outil_Azur_complet/Resources/Bot --swfsvg swfsvg/target/release/swfsvg
```

Les SWF de `clips/artworks/mini` n'exportent aucun symbole : chacun est rendu par `swfsvg --scene --name <numéro>`, puis converti par `exporter_png.py` à l'échelle 2 dans `Artworks/Mini/<gfx>.png`. De `modules/core.swf` (ou `--core`), `UI_PartyItem` est découpé à la bande transparente qui sépare la couronne du chef de la flèche du suivi (`Party/chef.png`, `Party/suivi.png`) ; `UI_PartyItemInfo` et `UI_FightOptionBlockJoinerExceptPartyMemberUp` donnent `Party/infos.png` et `Party/groupe.png`. Les marges transparentes sont retirées (Pillow). Le dossier de travail est temporaire et supprimé, sauf `--travail`. Mesuré le 4 octobre 2026 : moins de 2 s pour les 54 PNG (108 Ko). Avec `swfsvg` 0.2.2, `infos.png` prend un liseré magenta (contour recoloré par le client) : les PNG versionnés viennent de la 0.2.1 (voir `Resources/Bot/Party/PROVENANCE.md`).
