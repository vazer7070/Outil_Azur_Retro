# Textes du client 1.34 pour le bot (`BotLang`)

Ces XML reprennent les textes que le client Dofus 1.34 lit dans ses fichiers de langue : noms des PNJ et de leurs actions, questions et réponses des dialogues, noms et coordonnées des cartes, sous-zones et zones, monstres et leurs grades, objets, sorts, émotes, textes d'interface et messages `Im`, objets interactifs, compétences, métiers, titres et quêtes. Aucune table StarLoco ne contient ces textes : le serveur n'envoie que des identifiants.

## Source

Fichiers `lang/swf/<famille>_fr_<version>.swf` du pack « Lang » de l'émulateur StarLoco remis par l'utilisateur le 3 octobre 2026 (`03 - Emulateur StarLoco/01 - Lang/dofus/`). C'est la racine que le client 1.34 télécharge comme serveur de données (`config.xml`, `<dataserver url=".../dofus/">`). La version de chaque famille est celle annoncée par `lang/versions_fr.txt` du même pack. Les SWF eux-mêmes ne sont pas versionnés.

## Outil et commande

`tools/client-analysis/lang2xml.py` enchaîne, pour chaque famille, `avm1dump` (désassemblage du `DoAction` unique du SWF), `as2lite.py` (une affectation AS2 par ligne) puis sa propre lecture des affectations littérales. Les lignes qui ne sont pas des littéraux (le code présent dans `states_fr`) sont ignorées sans être exécutées ; quand le fichier affecte deux fois la même entrée, la dernière affectation est gardée, comme dans le client.

Commande exacte, depuis la racine du dépôt (`avm1dump` compilé depuis `tools/client-analysis/avm1dump`) :

```sh
python3 tools/client-analysis/lang2xml.py "<pack Lang>/dofus/lang/swf" Outil_Azur_complet/Resources/Bot/BotLang \
    --familles toutes --avm1dump tools/client-analysis/avm1dump/target/release/avm1dump
```

Durée : environ 20 secondes pour les 28 familles. `--familles bot` (défaut) se limite aux 13 familles lues par `LangData` ; `--travail <dossier>` conserve les fichiers intermédiaires.

## Contenu

Chaque fichier a pour racine `<BotLang famille="…" langue="fr" version="<numéro du SWF>" source="<nom du SWF>">` et un élément par entrée, encodé en UTF-8. Les listes de nombres sont écrites « 1,3 » ; les structures imbriquées (listes de textes, récompenses de quêtes…) sont écrites en JSON compact dans l'attribut.

| Fichier | SWF | Entrées | Éléments |
| --- | --- | --- | --- |
| `dialog.xml` | `dialog_fr_520` | 5 666 questions, 5 114 réponses | `question`, `reponse` (`texte`) |
| `npc.xml` | `npc_fr_508` | 1 083 PNJ, 10 actions | `pnj` (`nom`, `actions`), `action` (`nom`) |
| `maps.xml` | `maps_fr_1047` | 9 199 cartes, 372 sous-zones, 47 zones, 2 super-zones | `carte` (`x`, `y`, `sousZone`, `episode`, `placementEquipe1/2`, `maxDefi`, `maxEquipe`, `donjon`), `sousZone`, `zone`, `superZone` |
| `monsters.xml` | `monsters_fr_1051` | 1 380 monstres, 77 races, 20 super-races | `monstre` (`nom`, `gfx`, `race`, `alignement`, `expulsable`) et ses `grade` (`n`, `niveau`, `resistances`), `race`, `superRace` |
| `items.xml` | `items_fr_1044` | 9 705 objets, 114 types | `objet` (`nom`, `type`, `gfx`, `niveau`, `pods`, `prix`, `description`, `conditions`, `panoplie`, `arme`…), `type`, `superType`, `emplacements`, `texteUnique` |
| `spells.xml` | `spells_fr_350` | 2 477 sorts | `sort` (`nom`, `description` ; les niveaux restent ceux de `BotSorts`) |
| `emotes.xml` | `emotes_fr_112` | 20 émotes | `emote` (`nom`, `commande`) |
| `lang.xml` | `lang_fr_807` | 2 207 textes, 82 réglages | `texte` (`cle`, `valeur` : `INFOS_54`, `ERROR_6`…), `config`, `entree` (`CSR`, `COM`, `CNS`, `ABR`) |
| `interactiveobjects.xml` | `interactiveobjects_fr_198` | 109 objets, 189 gfx | `interactif` (`nom`, `type`, `competences`), `gfx` (`interactif`) |
| `skills.xml` | `skills_fr_284` | 150 compétences | `competence` (`nom`, `metier`, `interactif`, `condition`, `forgemagie`) |
| `jobs.xml` | `jobs_fr_143` | 39 métiers | `metier` (`nom`, `specialisation`, `icone`) |
| `titles.xml` | `titles_fr_7` | 206 titres | `titre` (`texte`, `couleur`, `typeParametre`) |
| `quests.xml` | `quests_fr_411` | 480 quêtes, 824 étapes, 3 687 objectifs, 14 modèles | `quete`, `etape`, `objectif` (`type`, `parametres`), `modele` |

Familles exportées telles quelles (`<entree table="…" id="…">` avec les clés d'origine), sans accesseur dédié, pour les fonctions à venir (alignement, maisons, montures, panoplies, recettes, carte du monde…) : `alignment` (147), `classes` (180), `effects` (266), `itemsets` (180), `crafts` (288), `hints` (116), `houses` (129), `rides` (117), `fightChallenge` (25), `states` (206, entrées `ST` seulement), `guilds` (109), `ranks` (108), `pvp` (103), `servers` (201), `dungeons` (120), `shortcuts` (566). Elles se lisent avec `LangData.Raw(famille, table, id)`.

`shortcuts.xml` (`shortcuts_fr_228`) est la table des raccourcis clavier du client : jeux de touches `SST` (8, dont « Clavier français - France » = 1), catégories `SSC` (8), raccourcis `SH` (61, description `d`, catégorie `c`) et touches `SSK` (489, identifiant `<jeu>|<NOM>`, code de touche `k`, modificateurs `c` 0 aucun / 1 Ctrl / 2 Maj / 3 Ctrl + Maj, libellé `s`, seconde touche éventuelle `k2`/`c2`/`s2`). `Bot/Shortcuts.cs` en lit le jeu 1. Commande exacte, ajoutée après les autres familles :

```sh
python3 tools/client-analysis/lang2xml.py "<pack Lang>/dofus/lang/swf" Outil_Azur_complet/Resources/Bot/BotLang \
    --familles shortcuts --avm1dump tools/client-analysis/avm1dump/target/release/avm1dump
```

Taille totale : 8,2 Mo (29 fichiers). Le projet copie `*.xml` de ce dossier vers `ressources/Bot/BotLang` à côté de l'exécutable (cible `CopyBotLangAssets` de `Outil_Azur_complet.csproj`) ; `LoadingBotForm` les charge hors du thread de l'interface avec `Tool_BotProtocol.Game.Data.LangData`.

## Droits

Les textes restent la propriété de leurs titulaires d'origine, comme les illustrations de `../Client` et `../Selection`. Ils ne proviennent d'aucun dump SQL et ne contiennent ni compte ni personnage.
