# Compatibilité StarLoco — 3 octobre 2026

Le dump StarLoco fourni par l'auteur le 3 octobre 2026 comprend deux bases : `login` (**27 tables**) et `game` (**47 tables**). Leurs définitions de colonnes, clefs et valeurs par défaut sont reprises dans `tests/Fixtures/starloco-login-schema.sql` et `tests/Fixtures/starloco-game-schema.sql`, converties en InnoDB, sans valeur `AUTO_INCREMENT` et sans aucune ligne de données. Les essais utilisent uniquement des données fictives dans une instance MySQL dédiée.

StarLoco reprend le modèle de données de Kryone (mêmes classes `AccountList`, `CharacterList`, `ItemList`…), mais répartit ses tables autrement et renomme certaines colonnes. Le profil `StarLocoProfile` (`tools/Tools_protocol.Emulators/Profiles.cs`) porte les noms réels de ses tables : les fichiers `auth/auth_tables.json` et `world/world_tables.json` ne sont pas consultés pour cet émulateur.

## Configurer les deux bases

Dans **Configuration**, choisissez l'émulateur **StarLoco**, renseignez la connexion **auth** avec la base `login` et la connexion **world** avec la base `game`. Les deux bases doivent être sur le même serveur SQL et accessibles avec le même compte : les opérations qui lient un personnage de `login` à une ressource de `game` (métiers, sorts, création d'objet, suppression d'un modèle) nomment la seconde table par sa base dans la même transaction.

Chaque table logique d'Azur est lue dans la base que le profil lui attribue :

| Base `login` (connexion auth) | Table StarLoco |
| --- | --- |
| comptes | `accounts` |
| personnages | `players` |
| exemplaires d'objets | `world.entity.objects` |
| serveurs, bannissements IP | `servers`, `banip` |
| commandes et groupes d'administration | `administration.commands`, `administration.groups` |
| actualités | `client_rss_news` |
| zones et sous-zones (placement) | `area_data`, `subarea_data` |
| maisons et coffres (placement) | `houses`, `coffres` |
| guildes, montures, familiers et quêtes des joueurs | `world.entity.guilds`, `world.entity.mounts`, `world.entity.pets`, `world.entity.players.quests` |

| Base `game` (connexion world) | Table StarLoco |
| --- | --- |
| modèles d'objets, panoplies, recettes | `item_template`, `itemsets`, `crafts` |
| sorts, métiers, expérience | `sorts`, `jobs_data`, `experience` |
| monstres, groupes fixes, extras, butins | `monsters`, `mobgroups_fix`, `extra_monster`, `drops` |
| cartes, cellules scriptées, déclencheurs | `maps`, `scripted_cells`, `interactive_objects_data`, `interactive_doors` |
| PNJ, modèles, questions et réponses | `npcs`, `npc_template`, `npc_questions`, `npc_reponses_actions` |
| quêtes, étapes, objectifs | `quest_data`, `quest_etapes`, `quest_objectifs` |
| fins de combat, actions d'objets, donjons | `endfight_action`, `objectsactions`, `donjons` |
| enclos (propriété), banques, hôtels de vente | `mountpark_data`, `banks`, `hdvs`, `hdvs_items` |
| zaaps, zaapis, animations, bandits, défis, morphs, cadeaux, membres de guilde, percepteurs, familiers, prismes, runes, schémas de combat, tutoriel | `zaaps`, `zaapi`, `animations`, `bandits`, `challenge`, `full_morphs`, `gifts`, `guild_members`, `percepteurs`, `pets`, `prismes`, `runes`, `schemafights`, `tutoriel` |

Les tables présentes dans les deux bases sont lues une seule fois : `mountpark_data` et `banks` dans `game` (la copie de `game.mountpark_data` porte le propriétaire, la guilde et le prix que l'éditeur d'enclos modifie), `area_data`, `subarea_data`, `houses` et `coffres` dans `login`. Les tables `titres` et `paroli` de Kryone n'existent pas dans StarLoco et ne sont pas proposées.

## Comptes

`accounts` ne possède ni `pass_no_crypt` ni `banRaison`. La création n'écrit que les colonnes présentes et renseigne les champs obligatoires du schéma (`lastConnectionDate`, `lastIP`, `friends`, `enemy`, `lastConnectDay`, `heurevote`, `bannedTime`, `rules`, `admin`) en mode SQL strict ; le mot de passe est stocké au format choisi (texte, MD5 ou SHA512). Un doublon de nom, une table MyISAM ou un champ obligatoire inconnu bloquent la création sans modifier le cache.

Les modifications, le bannissement et la suppression des comptes suivent les règles de Kryone : transaction, compte hors ligne, valeurs d'origine vérifiées. La suppression d'un compte retire ses personnages de `players` et leurs exemplaires de `world.entity.objects`.

## Personnages et compétences

`players.logged` accepte SQL NULL : Azur affiche alors un état **inconnu** et refuse les écritures qui exigent un personnage hors ligne. Les colonnes imposent **30 caractères pour `players.name`** et **300 caractères pour `players.jobs`**. Couleurs, point de sauvegarde, renommage, changement de compte et refus (compte ou personnage connecté, nom déjà pris, compte cible absent, table MyISAM) fonctionnent comme pour kauth.

Les catalogues de métiers (`game.jobs_data`) et de sorts (`game.sorts`) sont lus depuis la base `game` pendant que la liste du personnage est écrite dans `login.players` ; une ressource absente du catalogue est refusée.

## Exemplaires d'objets

La table `login.world.entity.objects` remplace `kworld.items`. Ses colonnes `id`, `quantity` et `position` correspondent à `guid`, `qua` et `pos` de Kryone ; `template`, `stats` et `puit` gardent leur nom. Le profil expose cette correspondance (`ItemColumn`) et toutes les écritures d'inventaire, de modération et de création d'objets l'utilisent : lecture de l'inventaire avec les noms des modèles de `game.item_template`, ajout (nouvel identifiant après le plus grand `id`), retrait partiel ou total, modification avec contrôle des valeurs d'origine, refus des quantités insuffisantes et des tables MyISAM.

## Création d'objets, panoplies et recettes

`item_template` possède `exchangesObject` et `newPrice` mais ni `exchangeable`, ni `doplons`, ni `heroique`. La requête de modèle est construite à partir des colonnes réellement présentes ; l'option **échangeable** du formulaire ne peut pas être enregistrée et est refusée plutôt qu'ignorée. Le modèle, la recette (`crafts`) et la panoplie (`itemsets`, clef `ID`) sont écrits dans `game`, l'exemplaire dans `login.world.entity.objects`, dans une seule transaction ouverte sur `game` ; un identifiant déjà utilisé annule l'ensemble.

## Éditeurs de ressources

Les 24 éditeurs lisent leur table dans `game`. Différences vérifiées par rapport à kauth :

- `sorts` utilise `duration` (défaut 800) à la place de `durer` ; l'éditeur et le cache de recherche lisent l'une ou l'autre colonne.
- `maps` n'a ni `cells`, ni `cases`, ni `background`, mais `sniffed` ; l'ajout d'une carte applique les valeurs par défaut du schéma et l'export du bot écrit `<BACK>0</BACK>`.
- `drops` n'a pas de colonne `id` : la clef est le couple (`monsterId`, `objectId`) ; les noms des monstres sont proposés depuis `game.monsters`, et un monstre absent est refusé.
- `monsters` n'a ni `iaModels` ni `size` ; `administration.groups` utilise `name` et `commands` au lieu de `nom` et `commandes`. Les lectures tolèrent ces colonnes absentes.
- `game.mountpark_data` n'a pas de `cellid` : un enclos est compté sur sa carte sans être tracé sur une cellule, comme pour `kworldsave.sql`.
- Les liens réciproques modèles ↔ panoplies restent dans `game` ; la suppression d'un modèle vérifie ses exemplaires dans `login.world.entity.objects` et la refuse s'il en reste.

L'export SQL complet d'une table depuis le gestionnaire de ressources, l'export des modifications en attente (« Exporter SQL »), la recherche (objets, panoplies, sorts, butins, monstres, métiers), les placements serveur (PNJ, groupes de monstres, zaaps, enclos) et l'export XML pour le bot (sorts, cartes, PNJ avec leurs modèles, objets, monstres, métiers, zaaps, joueurs, maisons, panoplies) sont vérifiés sur les deux bases.

## Ce qui n'est pas couvert

Les tables suivantes ne sont lues par aucun outil : `pubs`, `site.*` (boutique, votes, actualités du site), `world.entity.obvijevans` et les copies `login.banks` / `login.mountpark_data` dans `login` ; `maps_copy` et les copies `game.area_data`, `game.subarea_data`, `game.houses`, `game.coffres` dans `game`. Les titres et le paroli de Kryone n'ont pas d'équivalent. Le mode de hachage des mots de passe attendu par le serveur Login de StarLoco et le comportement en jeu des données écrites (placements, enclos sans cellule, cartes sans fond) restent à valider avec le kit `F:\kit`.

## Vérifications

`StarLocoSchemaIntegrationSmoke` recrée les 74 tables dans deux schémas temporaires, vérifie que chacune des 58 tables du profil existe dans la base attendue, que les 24 éditeurs lisent `game`, puis déroule avec des données fictives : comptes (SHA512, MD5, doublon, MyISAM, éditeur de comptes), personnages (couleurs, point de sauvegarde, renommage, refus, `logged` NULL, limite de 30 caractères), métiers et sorts entre `login` et `game`, inventaire dans `world.entity.objects`, création d'objet/recette/panoplie, éditeurs de sorts, cartes, modèles, panoplies, butins et métiers avec exports SQL, caches de recherche et fenêtre de recherche, placements d'une carte, export XML du bot, modification/bannissement/suppression de comptes et de personnages. Il passe sous Mono avec MySQL 8.0 ; `EmulatorProfileSmoke` couvre le profil sans base. La validation finale sous Windows est `Run-Tests.ps1 -Integration` ; voir [les tests](../tests/README.md).
