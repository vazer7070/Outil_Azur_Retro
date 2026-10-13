# Éléments du client pour les objets interactifs

PNG de `modules/core.swf` du client Dofus 1.34 remis par l'utilisateur (`04 - Dofus 1.34 - Qu'Tan et Ilyzaelle`), utilisés par les volets Code et Document du bot (`Outil_Azur_complet/Bot/Panels/KeyCodePanel.cs`, `DocumentPanel.cs`). Ils sont copiés avec le reste de `Resources/Bot/Client` vers `ressources/Bot/UI/Client/Interactifs` (cible `CopyBotClientAssets`) ; un fichier absent laisse le volet dessiner son rendu de repli.

| PNG | Symbole de `core.swf` | Usage |
| --- | --- | --- |
| `code-0.png` … `code-9.png` | `UI_KeyCodeSymbol0` … `UI_KeyCodeSymbol9` | Touches et cases du clavier de code (`KeyCode`) |
| `document-livre.png` | `UI_DocumentBook` | Fond d'un livre |
| `document-parchemin.png` | `UI_DocumentParchment` | Fond d'un parchemin (bitmap opaque : le noir relié aux bords est rendu transparent) |
| `document-pancarte.png` | `UI_DocumentRoadSignLeft` | Fond d'une pancarte |

## Commande exacte

Depuis la racine du dépôt, avec `swfsvg` 0.2.1 compilé depuis `tools/client-analysis/swfsvg` (`cargo build --release`) :

```sh
python3 tools/client-analysis/exporter_ui_interactifs.py "<client 1.34>/modules/core.swf" \
    Outil_Azur_complet/Resources/Bot/Client/Interactifs --swfsvg <dossier cible cargo>/release/swfsvg
```

Chaîne : `swfsvg` (symboles nommés), `exporter_png.py` à l'échelle 2 (aplats magenta rendus transparents), puis Pillow : marges transparentes retirées, fonds réduits à 380 px de large et quantifiés en palette de 256 couleurs ; les chiffres restent en RGBA. Le résultat est déterministe (mêmes octets d'une exécution à l'autre). Taille : 48 Ko pour 13 fichiers.
