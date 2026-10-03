# Outil Azur Retro

L’état des fonctions corrigées, les écarts encore présents avec ce README et les critères de validation sont décrits dans [l’état du projet](docs/ETAT_PROJET.md). La suite reproductible compte **38 tests : 29 sans base et 9 d'intégration** ; voir [les tests](tests/README.md). Pour reprendre le projet rapidement, consulter aussi l'[inventaire de reprise](docs/INVENTAIRE_REPRISE.md).

## Émulateurs pris en charge

Azur décrit chaque émulateur par un **profil** (`tools/Tools_protocol.Emulators`) : où se trouve chaque table (base auth ou world), quelles fonctions l'outil sait y utiliser et ce qu'il charge au démarrage. Le menu grise les outils qu'un profil ne gère pas et en donne la raison ; la configuration affiche la prise en charge de chaque émulateur. Ajouter un émulateur revient à ajouter un profil.

| Émulateur | Pris en charge aujourd'hui |
| --- | --- |
| **Kryone V2** (kauth / kworld) | Tous les outils : comptes, personnages, inventaires, création d'objets, 24 éditeurs de ressources, recherche, placements et export XML vers le bot. C'est la cible de référence des tests d'intégration. |
| **StarLoco** (login / game) | Les mêmes outils SQL que Kryone sur ses deux bases : comptes, personnages et exemplaires d'objets (`world.entity.objects`) dans `login` ; modèles d'objets, panoplies, recettes, 24 éditeurs de ressources, recherche, placements et export XML vers le bot dans `game`. Le profil porte les noms de ses 58 tables et les colonnes renommées ; pas de titres ni de paroli. Vérifié par un test d'intégration sur ses schémas `login`/`game` ; voir [la compatibilité StarLoco](docs/STARLOCO_COMPATIBILITE.md). Le client bot (connexion Login/Game, personnages, déplacements, sorts) reste à valider sur un vrai serveur. |
| **Sunshine** | Consultation des comptes uniquement. |
| **Codebreak** | Connexion aux bases uniquement ; les outils SQL ne sont pas encore adaptés à son schéma. |


![forthebadge](https://forthebadge.com/images/badges/made-with-c-sharp.svg)

Voici le projet couteau suisse permettant la génération de carte, un bot, un outil de recherche et de création, un gestionnaire de base de joueurs et de personnages.

Ne pouvant pas le finir comme je voudrais, je le place ici en open source, je connais la communauté FR de l'émulation, elle est horrible entre elle et égoïste, ne voulant rien faire pour faire progresser le groupe, préférant penser qu'il est simple de monter et de tenir un serveur en écrasant les autres et reprenant inlassablement les mêmes outils défaillants, vérolés et la plupart du temps dépassés.
Je pense parler dans le vide mais au moins je le dis, si vous voulez faire grandir l'émulation ou du moins faire perdurer ce qu'il en reste, agissons en groupe solidaire, pour la passion plus que pour l'argent.
(il suffit de voir le GIT de [TrinityCore](https://github.com/TrinityCore/TrinityCore) pour WOW pour voir que eux ils ont compris et ça fait des années que ce projet perdure).

A l'instar de moi, il reste des vieux de l'émulation encore plus ou moins actif, qui ont connus l'ère de Britana, Aidemu, Worldemu, l'apogée de DOE et bien d'autres forums éphémères, c'est peut-être peine perdu mais arrêtez d'être cynique et con avec les nouveaux, c'est comme ça que l'on regresse de plus en plus et que maintenant on se retrouve avec des leechers et non plus de vrais devs car il n'existe plus rien de concret, l'émulation rétro est hispanique pour la plus stable (une vague version de StarLoco et encore...) et la 2.x est morte, certains innovent un peu mais les git meurent faute de commit et Stump est comme la licence SW de Disney et puis...c'est tout...hélas.

Je partage ce projet pour vous montrer qu'il est possible de faire de gros projet en commun, certains vont le prendre, le repomper et si ils trouvent des pigeons essayer de le vendre, certains vont le prendre pour eux et l'étudier, le faire à sa sauce mais j'ose espérer que hormis me dire "il y a un bug là", "ça on dirait tel code PLAGIAT" ou que sais-je, certains tenteront l'aventure de le faire perdurer et grandir.

Le potentiel est là, alors faisons ce qu'il faut pour que ça se fasse.
**(les trolls, fermez vos gueules, vous pourrez vous la ramenez quand vous sortirez du code potable et encore)**

Je ferais des mises à jour quand je pourrais, en corrigeant des bugs remontés et ajoutant des fonctions, si vous avez des commits, n'hésitez pas, on peut en discuter sans problèmes.

## Installation

L'outil peut se gérer en local mais alors certains outils ne seront pas disponible faute de BDD active (compatible WAMP/XAMPP).

Les anciennes mises à jour automatiques ont été désactivées : leurs archives ne fournissent pas de signature vérifiable et le démarrage pouvait lancer un exécutable présent dans le dossier `MAJ`. Pour cette version, compilez en Release ou utilisez le [paquet portable](docs/PORTABLE.md), puis remplacez les fichiers du logiciel en conservant vos trois fichiers JSON de configuration et vos créations. Le bouton de configuration peut encore consulter l'annonce de version, mais ne redémarre plus pour installer une archive non vérifiée.

Si il manque les images pour l'éditeur de carte, il s'agit des mêmes que pour **Astria Map Editor** avec juste des noms de dossiers différents (parce que why not), vous ne pourrez pas le lancer si il ne les trouves pas.

Les bibliothèques fournies sous `Outil_Azur_complet/bin/Debug/ressources` sont maintenant copiées automatiquement dans les autres configurations à la compilation. Les outils utilisent le dossier de l'exécutable au démarrage, même si Azur est lancé depuis un autre dossier.

La solution se compile avec Visual Studio 2019 ou plus récent (`Outil_Azur_complet.sln`, .NET Framework 4.8), après restauration NuGet. Les identifiants SQL sont dans `config.json`, à côté de l'exécutable ; ce fichier est ignoré par Git et aucune configuration locale n'est versionnée.

Vous pouvez modifier les identifiants de la/des BDD dans le panneau de configuration et relancer la connexion depuis ce dernier pour vous permettre de lancer les outils manquants.
Les outils de recherche et les éditeurs ciblent **Kryone V2**. La connexion **auth** doit accéder aux comptes (`accounts`), personnages (`players`) et ressources statiques de `kauth` ; la connexion **world** doit accéder aux exemplaires d'objets (`items`), présents dans `kworldsave.sql`. Les deux connexions restent à configurer pour votre installation. Pour les autres émulateurs, voir le tableau ci-dessus.

Les correspondances sont enregistrées dans **auth/auth_tables.json** et **world/world_tables.json**, sous le dossier de l'exécutable. Les nouvelles configurations utilisent les noms corrigés de `kauth`. Les anciens JSON personnalisés sont conservés : vérifiez leurs noms dans **Configuration → Configurer les tables → Correspondances**, notamment `coffre → coffres`, `extra_monstres → extra_monster`, `object_action → objectsactions`, `schema_fight → schemafights` et `titres → titre`.

La correspondance world historique `personnages → characterinstance` n'est pas utilisée par les éditeurs Kryone ; leurs personnages proviennent de **auth → players**.

Il est possible de modifier ces fichiers directement depuis le panneau de configuration de l'outil, dans le menu principal.


Si vous souhaitez une version particulière, vous pouvez la demander ou alors la coder et la mettre en commit pour la placer dans l'outil.

## Outils

Je vous présente les outils un à un, les fonctions peuvent changer au gré des mises à jour donc je reste généraliste.

### Éditeur de compte
 Cet outil permet de visualiser le compte et son état (VIP/banni/staff), les informations de compte (MDP, question et réponse secrète), l'état de connexion ainsi que les personnages en jeu qui sont liés à ce compte ainsi que divers informations notamment les points (mais c'est à voir selon les BDD)
 
 Les champs présentés sont modifiables et enregistrés ensemble dans une transaction InnoDB. Le compte doit être hors ligne ; les changements concurrents refusent l'enregistrement. Les mots de passe existants conservent leur format serveur : une valeur hashée doit être remplacée par un hash compatible.

 Il est également possible de bannir/débannir, rendre/retirer VIP voir de supprimer complètement le compte.

 ### Création de compte
 Un outil permet de créer un compte avec le format de mot de passe attendu par le serveur : texte, MD5 ou SHA512. La création lit les colonnes réelles, initialise le pseudo avec le nom du compte et la date d'inscription avec la date du jour, puis relit la ligne avant de valider sa transaction. Le pseudo reste modifiable. `pass_no_crypt` reste vide ; un champ obligatoire inconnu ou un moteur différent d'InnoDB bloque la création avec un diagnostic. Le cache est actualisé après validation SQL.

 ### Éditeur de personnage
 *Il n'est pas possible depuis l'outil de créer un personnage sauf depuis le bot qui simule la page de création de personnage lors de la connexion à son compte (depuis officiel comme privé).*

Cet outil recense tout les personnages du serveur ainsi que toute les informations qui les concernent, ça va de l'id à la liste de son inventaire et de ses sorts.

L'identifiant et l'état de prison restent gérés par le serveur. Les autres champs présentés sont modifiables, y compris le point de sauvegarde et les couleurs, avec une palette et l'option couleur par défaut. Les modifications du contenu nécessitent le compte et le personnage hors ligne. Le bannissement et la suppression sont également accessibles.

La sauvegarde vérifie l'état hors ligne, les valeurs originales et l'existence du compte cible avant un transfert. Les limites du schéma réel sont appliquées, notamment 30 caractères pour le nom et 300 caractères pour la liste des métiers. Un état de connexion SQL NULL est affiché comme inconnu : la consultation reste possible et les écritures sont bloquées.

Pour les objets et les sorts, un boutons est là pour faire le lien avec l'éditeur qui va bien (éditeur d'inventaire pour les objets, de sort pour les sorts et de changement de métier pour les métiers).

### Outils de recherches
Il existe plusieurs outils de recherche dans une seule interface:
* **Outil de recherche de Drop**
* **Outil de recherche de monstres**
* **Outil de recherche de sorts**
* **Outils de recherche d'objets**
* **Outils de recherche de panoplies**
  
Voici la liste actuelle des interfaces fonctionnelles à l'instant de l'écriture de ce texte (donc modifiée lors des mises à jour).

Chacun de ses outils plonge dans la BDD et traduisent les informations puis à l'aide d'image et d'une UI assez intuitive, permet de comprendre ce qu'il s'y trouve.
Ils utilisent des ressources présentes dans la racine de l'application, donc faites attention si jamais vous modifiez ces dernières, ça peut tronquer le résultat des outils de recherche.

Si ces outils présentent les effets de façon brut c'est qu'il ne les connait pas, n'hésitez pas à les rajouter au besoin afin qu'ils puissent vous donner le résultat le plus juste possible.

 
### Créateur d'objet
Cet outil permet de créer un objet avec des conditions et des effets. Il génère les requêtes SQL, permet leur enregistrement direct groupé et produit un SWF binaire pour le client, avec source ActionScript facultative. Il peut intégrer l'objet dans une copie d'un fichier client existant. Le nouvel éditeur **Objets du client**, utilisable sans SQL, permet de rechercher et modifier les fiches d'un SWF d'objets. Les variantes acceptées et la validation en jeu encore nécessaire sont décrites dans [le guide des SWF d'objets](docs/FORMAT_SWF_OBJETS.md). Les éditeurs de modèles, panoplies et recettes permettent aussi de modifier les ressources existantes.

Il est possible aussi de l'ajouter à une panoplie existante (vanilla ou crée par vous préalablement) ainsi que le rendre fabricable de faire la recette directement depuis cette interface.

### Éditeur d'inventaire
Cet outil permet d'ajouter, retirer et modifier les objets de chaque personnage : quantité, emplacement, effets et puits de forge-magie. Les changements restent en attente jusqu'à leur enregistrement commun, avec contrôles de connexion, d'appartenance et de modifications concurrentes.

Les kamas du personnage se modifient dans l'éditeur de personnage et non pas sur cette interface.

*Attention à bien faire une déco/reco du personnage après modification de l'inventaire afin d'appliquer correctement les modifications*

### Gestionnaire de ressources
Cet outil exporte les ressources de la base en XML ou en SQL. Le SQL complet contient des INSERT pour une table vide de même schéma ; les éditeurs de fiches proposent séparément l'export de leurs modifications.

La fonction d'extraction XML permet notamment d'utiliser les informations de la BDD que vous utilisez pour les rendres compatibles avec le bot. Pas toute les données sont compatibles avec lui mais le gestionnaire fait le tri lui même, que ce soit pour le type de données comme pour les informations à lintérieur des tables.

Vous pouvez définir un chemin spécifique uniquement lors d'une extractions simple, sinon le gestionnaire place lui-même les fichiers à la racine du bot afin d'éviter toute erreur empêchant son bon fonctionnement.

Les autres rubriques permettent d'ouvrir des cartes AME/SWF, de déchiffrer des données de carte et de produire des cartes SWF binaires ou AME. Elles permettent aussi d'importer des images avec aperçu et de lire/filtrer un journal local. **Capture réseau** propose désormais un relais TCP local démarré manuellement, avec messages, filtres et export masqué. Il nécessite de configurer le client vers le port local ; les redirections auth/jeu et TLS ne sont pas pris en charge automatiquement. Voir [son utilisation et ses limites](docs/CAPTURE_RESEAU.md).

### Éditeur de cartes
L'éditeur utilise maintenant un grand canevas central, des onglets pour les cartes ouvertes, des propriétés par rubrique à droite et une bibliothèque de tuiles en bas. Il est issu d'un travail inspiré d'Astria Map Editor, avec des fonctions de création et de placement propres à Azur.

Il en possède toute les fonctions de base mais peut également placer des PNJs et des groupes de monstres, des interactives (zaaps, établies, etc...) ainsi que des enclos (et gérer si ils sont privés ou publics).

Il lit les projets AME Azur v1/v2/v3 et les cartes SWF FWS/CWS à affectations AVM1 simples. Les exports AME, SWF binaire et SQL sont accessibles dans l'interface. La compatibilité avec les anciens AME Astria, les scripts SWF complexes et l'ouverture dans le client de jeu restent à valider ; voir [les formats pris en charge](docs/FORMAT_SWF_CARTES.md).

Le bouton **Placements serveur** superpose les PNJ, groupes fixes, zaaps et enclos sur les cellules de la carte ouverte lorsque le schéma fournit leur `cellid` ; les fiches restent modifiables depuis le menu **Outils**. Les cellules d'enclos définies dans la carte sont également signalées. Cette couche est facultative et nécessite la connexion Kryone configurée.

L'éditeur ne peut pas être lancé si l'application ne détecte pas les fichiers d'images nécéssaires à son bon fonctionnement, il est donc important de vérifier leurs présence et que ce soit bien des images de tuiles de carte correspondantes aux dossiers présents à la racine d'AzurToolRetro.

### Client AzurToolBot
Le client est une re-création en C# du client Dofus Retro, qui parle directement le protocole **Login/Game de StarLoco** (connexion, serveurs, sélection et création de personnage, cartes, déplacements, discussion, caractéristiques, inventaire, dialogues PNJ, zaaps, boutique PNJ, sorts et métiers). Il reprend des éléments du bot de **Salesprendes**. Son interface suit la composition du client d'origine : grande carte, bandeau de jeu en bas, fiches refermables. Ses boutons, le bandeau de connexion, le socle de l'aperçu de création et les icônes du bandeau de jeu sont les éléments graphiques du client Dofus 1.34 fourni, exportés de `core.swf` ([provenance](Outil_Azur_complet/Resources/Bot/Client/PROVENANCE.md)). Les formats de paquets que le bot suit sont ceux relevés dans le code du client : [référence du protocole](docs/PROTOCOLE_CLIENT_1_34.md), produite par les outils de [`tools/client-analysis`](tools/client-analysis/README.md). Le [guide du bot](docs/BOT_STARLOCO.md) décrit son utilisation et ses limites.

Ce qui est vérifié aujourd'hui l'est par des tests sur boucle locale avec des serveurs fictifs ; la connexion à un vrai StarLoco reste à valider. Le combat reste partiel ; les dialogues PNJ, les zaaps, la boutique PNJ et les actions d'inventaire (équiper, utiliser, jeter) sont vérifiés avec des paquets fictifs aux formats du client 1.34 et de StarLoco, sans validation en jeu réel.

Les trois modes historiquement annoncés ne sont **pas disponibles** et ne sont pas proposés dans l'interface :

* **Manuel** : jouer avec le client comme alternative au client officiel. C'est le mode actuel, en cours de validation.
* **Admin** : moderer son serveur depuis le client (bannir, invoquer, modifier les joueurs) en le reliant aux bases d'Azur. Non réalisé.
* **Automatique** : automatiser le client avec des plugins C# ou des scripts Lua. Non réalisé ; aucun script n'est fourni.

Si jamais vous vous faites bannir, la responsabilité est vôtre, je décline toute responsabilité si vous vous faites attraper et que vous perdez votre compte suite à l'utilisation du client **AzurToolRétro**.

Il est important de noter que à ce jour, le client est un prototype, son contenu, fonctionnement et aspect peuvent être amenés à changer.

Il est possible de mettre à jour les ressources utilisées par le client via le gestionnaire si vous avez des cartes, des pnjs ou créatures qui ne sont pas des ressources officielles (ça permet de les voirs en jeux et de vous y rendre correctement (pour les cartes)).

# Remerciement
Toute les personnes citées ici ont contribuées d'une façon ou d'une autre au dévelloppement de l'application et je tiens à les remercier.
(si vous devenez un contributeur, vous y serez aussi et au-delà d'un nom cité, vous serez une personne qui a aidée à faire grandir la communauté de l'émulation FR).

* [Salesprendes](https://github.com/salesprendes)
* [Zano](https://github.com/ZanoQuentin)
* Adlesne (créateur de *AdCreator*)
