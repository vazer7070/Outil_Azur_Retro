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
cargo run --release --manifest-path avm1dump/Cargo.toml -- <client>/modules/loader.swf > loader.avm1.txt

# 2. Pseudo-décompilation par simulation de pile
python3 as2lite.py loader.avm1.txt > loader.as.txt

# 3. Référence du protocole (routes serveur → client, envois, lecture des réponses)
python3 relever_protocole.py loader.as.txt ../../docs/PROTOCOLE_CLIENT_1_34.md tables/

# 4. Export des symboles graphiques de core.swf en SVG (tous, ou une liste de noms)
cargo run --release --manifest-path swfsvg/Cargo.toml -- <client>/modules/core.swf svg/ UI_Login ButtonNormalUp

# 5. Conversion en PNG (échelle facultative)
python3 exporter_png.py svg/ png/ 1
```

## Ce que fait chaque outil

| Outil | Rôle |
|---|---|
| `avm1dump` | Lit les tags DoAction / DoInitAction / DefineSprite / ExportAssets, désassemble chaque bloc en suivant les sauts (le code de remplissage entre deux `Jump` est ignoré), replie les prédicats opaques du client (`Push "x"; CharToAscii; If`, `GetTime; Increment; If`, `Push false; Not; If`), choisit le pool de constantes réel parmi les leurres et écrit les instructions dans l'ordre de visite avec des étiquettes `::@offset`. |
| `as2lite.py` | Reconstruit des expressions (`this.aks.send("GA" + p1)`, `p4.split("|")`…) à partir de la sortie précédente. Les boucles ne sont pas restructurées : elles apparaissent sous forme de `goto`. Les paramètres obfusqués sont renommés `p1`, `p2`… |
| `relever_protocole.py` | Suit les chaînes `if (r0 === "x") goto` des méthodes `onMessage` de `dofus.aks.*` pour recomposer les préfixes, relève les `aks.send(...)` et résume la lecture de chaque réponse (`split`, indices, `parseInt`). |
| `swfsvg` | Exporte un symbole (forme, sprite à la première image, bouton à l'état relâché) en SVG : dégradés, bitmaps JPEG/sans perte en base64, transformations de couleur. Les aplats magenta `#FF00FF` sont des emplacements remplis à l'exécution : ils sont omis. Écrit aussi `index.tsv` (nom, identifiant, cadre, avertissements). |
| `exporter_png.py` | Convertit les SVG en PNG avec cairosvg. |

## Limites connues

- Les JPEG Flash (segment de tables puis image, `FF D9 FF D8` au milieu des données, `JPEGTables` partagé) sont recollés avant décodage ; un bitmap encore illisible est signalé dans `index.tsv` et sa zone reste vide.
- Les écrans construits à l'exécution (bandeau, inventaire, sorts, options) n'ont que peu d'art statique : seul leur cadre est exporté.
- La décompilation est une pseudo-décompilation : elle suffit à lire les formats de paquets, pas à recompiler le client.
