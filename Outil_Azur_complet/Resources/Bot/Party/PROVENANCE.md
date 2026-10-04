# Éléments du volet Groupe (`core.swf`)

Quatre petits PNG tirés des symboles exportés de `modules/core.swf` du client Dofus 1.34 fourni (`04 - Dofus 1.34 - Qu'Tan et Ilyzaelle`, SHA-256 `917d4cf5…6dce`) :

| Fichier | Symbole source | Usage dans le client | Usage dans le bot |
|---|---|---|---|
| `chef.png` (17 × 14) | `UI_PartyItem` (DefineSprite 376), bloc du haut : `_mcLeader` | couronne sur la case du chef du groupe | même place, case du membre `PL` |
| `suivi.png` (16 × 16) | `UI_PartyItem`, bloc du bas : `_mcFollow` | flèche sur la case du membre suivi (`PF+`) | même place |
| `infos.png` (24 × 25) | `UI_PartyItemInfo` (DefineSprite 378) : `_mcInfo` | infobulle « niveau total / prospection totale » | ligne d'informations du volet |
| `groupe.png` (36 × 29) | `UI_FightOptionBlockJoinerExceptPartyMemberUp` (DefineSprite 1293) | bouton « groupe seulement » du volet `Party` | icône du volet dans le tiroir |

## Commande exacte

Depuis la racine du dépôt, avec `CLIENT` = dossier du client fourni (non versionné) :

```sh
CARGO_TARGET_DIR=/tmp/cargo-swfsvg cargo build --release --manifest-path tools/client-analysis/swfsvg/Cargo.toml
python3 tools/client-analysis/exporter_groupe.py "$CLIENT" Outil_Azur_complet/Resources/Bot --swfsvg /tmp/cargo-swfsvg/release/swfsvg
```

Le script lance `swfsvg <CLIENT>/modules/core.swf <svg>/ UI_PartyItem UI_PartyItemInfo UI_FightOptionBlockJoinerExceptPartyMemberUp` (swfsvg 0.2.1), puis `exporter_png.py <svg> <png> 2` (cairosvg, échelle 2). `UI_PartyItem` ne rend que la couronne et la flèche (le fond et la jauge de vie du clip sont teintés par des transformations de couleur que cairosvg ignore) : le script le découpe à la bande transparente qui sépare ces deux blocs, puis retire les marges transparentes de chaque image (Pillow). 5 008 octets au total, RGBA 32 bits (trop petits pour gagner à la palette).

Le fond de case et la jauge de vie verticale (rouge `#D80101`, transformation de couleur de `_mcHealth`) sont redessinés par le bot. Les images conservent les droits de leurs titulaires d'origine ; aucun SWF du client n'est versionné.

## Exécution

`Outil_Azur_complet.csproj` copie ce dossier vers `ressources/Bot/Party` à côté de l'exécutable (cible `CopyBotPartyAssets`). Sans ces fichiers, le volet dessine une couronne et une flèche simplifiées.
