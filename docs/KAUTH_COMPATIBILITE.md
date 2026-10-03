# Compatibilité Kryone et kauth — 1er octobre 2026

Le fichier `F:\Téléchargements\kauth.sql` fourni contient les comptes (`accounts`), personnages (`players`) et ressources statiques nécessaires aux éditeurs Kryone. Ses **58 tables utilisent InnoDB**. La vérification reprend leurs définitions de colonnes, clefs et valeurs par défaut dans `tests/Fixtures/kauth-schema.sql` ; aucune ligne réelle n'est importée. Les essais de création et de modification utilisent uniquement des données fictives dans une instance MySQL dédiée.

## Configurer les deux bases

Dans **Configuration**, renseignez la connexion **auth** pour kauth et la connexion **world** pour kworld. Auth fournit les comptes, personnages et modèles de ressources ; world fournit notamment les exemplaires d'objets dans `items`. Les opérations qui utilisent les deux bases demandent qu'elles soient accessibles sur le même serveur SQL avec un compte compatible.

Les noms se règlent dans **Configurer les tables → Correspondances** et sont conservés dans `auth/auth_tables.json` et `world/world_tables.json`, sous le dossier de l'exécutable. Les nouvelles configurations utilisent les valeurs suivantes :

| Clef JSON auth | Table kauth |
| --- | --- |
| `comptes` | `accounts` |
| `personnages` | `players` |
| `coffre` | `coffres` |
| `extra_monstres` | `extra_monster` |
| `object_action` | `objectsactions` |
| `schema_fight` | `schemafights` |
| `titres` | `titre` |

Les cinq dernières correspondances corrigent d'anciens noms par défaut. Un JSON personnalisé existant n'est pas remplacé automatiquement : ajustez uniquement les correspondances nécessaires, enregistrez et redémarrez Azur. La correspondance world historique `personnages → characterinstance` n'est pas utilisée pour Kryone ; ses éditeurs chargent les joueurs depuis **auth → players**.

## Création et modification des comptes

La création lit les colonnes présentes et renseigne les champs connus nécessaires au schéma, y compris en mode SQL strict. Le pseudo initial correspond au nom du compte et reste modifiable. La date d'inscription est celle du jour. `pass_no_crypt` reste vide ; le format du champ `pass` est celui choisi dans le formulaire, selon le serveur : texte, MD5 ou SHA512.

Les autres valeurs par défaut sont préservées. Un champ obligatoire inconnu sans valeur par défaut bloque la création avec son nom dans le diagnostic. Les champs absents d'une variante plus petite ne sont pas ajoutés à la requête. La table comptes doit utiliser InnoDB : l'insertion et la relecture de la ligne sont validées ensemble, puis le cache est actualisé après le commit. Un refus ne crée pas de fiche dans le cache.

Les modifications de comptes sont regroupées en transaction et vérifient l'état hors ligne ainsi que les valeurs originales. Si le compte s'est connecté, a disparu ou a changé en base, l'enregistrement est refusé et doit être précédé d'un rechargement.

Chaque connexion d'écriture active le mode SQL strict pour empêcher la troncature ou le remplacement silencieux des caractères. Les colonnes `latin1` du schéma fourni n'acceptent pas tous les alphabets ni les emojis : une saisie incompatible est refusée avant le commit.

## Personnages et compétences

`players.logged` accepte SQL NULL dans le schéma fourni. Azur affiche alors un état **inconnu**, permet la consultation et refuse les écritures qui exigent un personnage hors ligne. NULL n'est pas traité comme une déconnexion.

La sauvegarde des personnages inclut les couleurs (`color1`, `color2`, `color3`) et le point de sauvegarde (`savepos`). La couleur par défaut reste `-1`. Les écritures contrôlent le personnage et son compte hors ligne, les valeurs d'origine et l'existence du compte cible avant un transfert. Elles exigent les tables InnoDB concernées.

Chaque fenêtre conserve ses propres valeurs d'origine. Si une autre fenêtre enregistre entre-temps le même champ, les saisies de la première restent en attente et sa sauvegarde est refusée ; le cache partagé ne remplace pas son point de comparaison.

Les limites proviennent des colonnes réellement présentes. Le schéma kauth impose notamment **30 caractères pour `players.name`** et **300 caractères pour la liste sérialisée `players.jobs`**. Une saisie trop longue est refusée avant l'enregistrement. Les éditeurs de sorts et métiers conservent les paramètres complémentaires et vérifient l'existence des ressources choisies.

## Cartes, enclos et exemplaires d'objets

Le schéma kauth expose `mountpark_data.cellid`, ce qui permet de situer les enclos dans la couche facultative **Placements serveur**. Le schéma plus ancien de `kworldsave.sql` ne possède pas cette colonne : une fiche d'enclos seule ne permet alors pas de tracer son emplacement.

La table world `items` de `kworldsave.sql` utilise MyISAM. Les écritures d'inventaire et les opérations de modération qui touchent ces objets exigent InnoDB et restent bloquées tant que les tables concernées ne sont pas préparées. Les éditeurs de ressources proposent une préparation explicite des tables et de leurs dépendances ; aucune conversion du serveur utilisateur n'est exécutée par les tests.

## Vérifications et limites

La suite compte **23 tests : 15 sans base et 8 d'intégration**, tous réussis en Debug et Release le 1er octobre 2026. `KauthSchemaIntegrationSmoke` reproduit les 58 tables, vérifie les 50 correspondances auth et les 24 familles d'éditeurs, puis utilise des données synthétiques pour les parcours de comptes/personnages et leurs refus. Il contrôle aussi un schéma de comptes réduit, les conflits entre fenêtres et les caractères incompatibles avec `latin1` sur un serveur configuré en mode SQL permissif. L'instance isolée utilise `127.0.0.1:43306` et est arrêtée après les essais. Les commandes et prérequis sont détaillés dans [les tests](../tests/README.md).

Le kit `F:\kit` fourni contient désormais les serveurs **StarLoco Login et Game** et un client **Dofus 1.34**. Les essais réseau et en jeu avec ce kit restent à réaliser. Le schéma kauth est validé pour les éditeurs **Kryone** ; la présence de StarLoco ne rend pas ses tables compatibles avec les correspondances SQL décrites ici.

Les exports SWF sont vérifiés par réouverture et lecture indépendante, mais leur chargement dans le client cible reste à contrôler. Le protocole du bot, les placements et les comportements publics/privés des enclos restent également à valider avec un serveur de jeu. Le [travail restant](ETAT_PROJET.md) décrit ces critères.
