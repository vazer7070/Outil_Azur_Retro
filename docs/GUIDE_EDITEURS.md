# Utiliser les éditeurs Azur

Les éditeurs utilisent une interface commune, dans la palette du client Dofus Retro partagée avec le bot : barre Azur, navigation par rubrique, fiches parchemin, champs en français dont l'aide s'affiche en infobulle, et actions explicites. Le menu **Atelier Azur** regroupe les outils par domaine ; un outil que l'émulateur configuré ne gère pas apparaît grisé avec la raison. La configuration et le gestionnaire utilisent aussi cette présentation. Ce guide correspond aux corrections du **3 octobre 2026**.

## Ouvrir un éditeur

| Domaine | Éditeurs disponibles |
| --- | --- |
| Administration | Comptes, création de compte, personnages, sorts et métiers des personnages |
| Objets | Création, inventaires, modèles d'objets, panoplies, recettes, actions des objets |
| Combat et métiers | Sorts, monstres, butins, métiers |
| PNJ et quêtes | Définitions des PNJ, questions, réponses/actions, quêtes, étapes, objectifs/récompenses |
| Cartes | Éditeur graphique, données serveur des cartes, téléportations, fins de combat, donjons, portes/mécanismes |
| Placements | PNJ, groupes fixes de monstres, enclos, zaaps |
| Ressources | Définitions des interactifs, recherche et gestionnaire |

Les **24 éditeurs de ressources serveur** permettent de rechercher, ajouter, modifier, retirer et exporter les fiches. Ils nécessitent les connexions SQL configurées et ciblent Kryone. L'édition graphique locale des cartes fonctionne sans base.

## Modifier et enregistrer

1. Recherchez une fiche par son nom ou son numéro et sélectionnez-la.
2. Choisissez une rubrique et modifiez ses options. Les noms de ressources sont proposés lorsque leurs tables sont accessibles ; leur identifiant reste disponible.
3. Pour un champ structuré, **Modifier…** ouvre le détail. **Appliquer** reporte les valeurs dans la fiche.
4. Cliquez sur **Enregistrer en base** dans l'éditeur principal. Les modifications de plusieurs fiches sont enregistrées ensemble.

La saisie ne déclenche pas d'écriture SQL. Les ajouts et modifications sont signalés dans la liste ; le compteur indique les changements en attente. **Retirer la fiche** prépare sa suppression. **Exporter SQL** exporte ces changements et les conserve en attente. **Recharger** et fermer proposent de les abandonner.

Une saisie invalide bloque le changement de fiche et reste visible pour correction. Une modification concurrente en base refuse l'enregistrement et annule sa transaction. La rubrique choisie est conservée au passage d'une fiche à l'autre. **Ctrl+S** enregistre dans les éditeurs de ressources.

Un identifiant existant reste en lecture seule. Pour une nouvelle fiche, un identifiant libre est proposé et peut être changé avant l'enregistrement. Cette proposition n'est pas une réservation : une collision concurrente est refusée par la base.

## Comptes, personnages et inventaires

Les comptes présentent compte, sécurité, statut et personnages. Les mots de passe existants sont des données du serveur : l'éditeur ne les transforme pas automatiquement. La création propose explicitement texte, MD5 ou SHA512 selon votre serveur.

Un compte créé reçoit initialement le même pseudo que son nom de connexion ; ce pseudo reste modifiable. Sa date d'inscription correspond au jour de création et `pass_no_crypt` reste vide. Azur renseigne les champs connus présents dans la table, conserve les autres valeurs par défaut et refuse un champ obligatoire inconnu. La création relit la ligne enregistrée avant de valider sa transaction ; le cache est actualisé ensuite. Les créations et modifications nécessitent InnoDB. Pour modifier un compte existant, déconnectez-le ; si ses valeurs changent entre le chargement et la sauvegarde, rechargez sa fiche.

Les personnages disposent des rubriques identité, progression, caractéristiques, position, apparence, social et contenu. Les couleurs sont modifiables avec une palette, un code **#RRGGBB** ou une valeur décimale. **Couleur par défaut** conserve **-1**. Le point de sauvegarde est modifiable sous la forme **carte,cellule**. L'identifiant et l'état de prison restent gérés par le serveur.

**Enregistrer** persiste également les couleurs et le point de sauvegarde. La sauvegarde exige le personnage et son compte hors ligne, vérifie que les valeurs chargées sont toujours actuelles et que le compte cible existe avant un transfert. Les limites du schéma sont appliquées : dans kauth, le nom possède 30 caractères au maximum et la liste sérialisée des métiers 300 caractères. Un état de connexion SQL NULL apparaît comme **inconnu** : la consultation reste accessible, mais les écritures sont refusées jusqu'à ce que le serveur fournisse un état fiable.

**Inventaire**, **Sorts** et **Métiers** ouvrent les éditeurs associés. L'édition du contenu exige le personnage et son compte hors ligne. Les sorts proposent niveau 1 à 6, position dans la barre et paramètres complémentaires ; les métiers proposent l'expérience et leurs paramètres conservés.

L'inventaire affiche les personnages à gauche, leurs objets au centre et les modèles à ajouter à droite. **Modifier l'objet sélectionné** permet de changer quantité, emplacement, effets et puits de forge-magie. Les ajouts, retraits et modifications sont placés dans **Changements d'inventaire**. Double-cliquer sur une ligne l'annule ; **Enregistrer l'inventaire** applique l'ensemble dans une transaction. Les emplacements incompatibles, occupés ou équipés en quantité supérieure à un sont refusés.

Choisir un personnage relit ses objets dans la base **world** : une lecture ratée ne reste plus affichée indéfiniment après le rétablissement de la connexion. Le bandeau de l'inventaire indique si la connexion world manque, si la lecture SQL échoue ou si des identifiants du personnage sont absents de la table d'objets configurée. Les informations déjà en mémoire peuvent rester visibles en cas de panne, mais la cause est signalée ; vérifiez les connexions et la correspondance de la table `items` avant de modifier l'inventaire.

## Objets, panoplies et recettes

Les modèles regroupent type, niveau, poids, effets, commerce, conditions et réglages d'arme. Les effets sont sélectionnables par nom : les valeurs sont présentées en décimal et encodées dans le format hexadécimal du serveur. La consultation conserve l'écriture originale ; modifier les bornes recalcule le jet lorsque son ancienne valeur reste inchangée.

Les panoplies proposent une liste d'objets et des bonus par palier : 2 objets équipés, puis 3, 4, etc. Les effets de chaque palier se modifient séparément. L'enregistrement tient à jour **la composition de la panoplie et l'appartenance des modèles**. Un objet appartenant à une autre panoplie doit d'abord en être retiré.

Les recettes proposent les ingrédients par nom et leurs quantités positives. L'objet fabriqué et tous ses ingrédients doivent exister. Les butins proposent objet, monstre, prospection et probabilités par grade de **0 à 100 %**, décimales comprises.

La suppression d'un modèle est refusée lorsqu'il possède encore des exemplaires ou est utilisé dans les recettes/butins contrôlés. La vérification des exemplaires nécessite la connexion world sur le même serveur SQL avec un utilisateur compatible. Les autres références propres à votre émulateur doivent également être vérifiées avant une suppression.

Le créateur génère du **SQL** et/ou un **SWF binaire**, avec source ActionScript facultative. Dans **Fichier client**, choisissez un SWF d'origine pour y ajouter l'objet dans une copie ; sans cette option, le SWF contient uniquement cet objet. L'enregistrement SQL direct est distinct du fichier à appliquer ultérieurement. La compilation et la validation client précèdent l'injection SQL. L'option « éthéré » n'a pas de représentation vérifiée dans le créateur ; les paramètres présents dans un fichier client restent accessibles dans le nouvel éditeur.

**Objets du client** ouvre un SWF sans connexion SQL, recherche les fiches et permet de modifier leurs propriétés. Les changements de plusieurs objets restent en attente jusqu'à **Enregistrer une copie**. Les champs connus sont traduits, l'arme est décomposée en options et les propriétés propres à une variante restent modifiables. Les données SQL ne sont pas synchronisées automatiquement. Voir [le guide des objets SWF](FORMAT_SWF_OBJETS.md) pour les formats acceptés et les limites.

## Sorts, monstres et métiers

Les sorts présentent leurs six niveaux avec :

- lancement : PA, portée, chances de critique/échec, limites, intervalle ;
- effets normaux/critiques : type, valeurs, paramètres, durée, probabilité et jet ;
- zones : forme en français, taille et ordre des effets ;
- conditions : ligne, ligne de vue, cellule vide, portée modifiable, états et niveau ;
- format serveur complet pour les variantes à 19 ou 20 champs.

Le champ supplémentaire d'une variante reste accessible sans lui attribuer une signification non vérifiée. **Lire les données** recharge les champs depuis le format avancé. Les deux sources ne sont pas écrasées silencieusement.

Les monstres proposent leurs données par grade : niveaux, résistances, force/sagesse/intelligence/chance/agilité, PA/PM, vie, initiative, expérience et sorts. Les métiers proposent outils, fabrications et compétences. Les paramètres supplémentaires sont conservés et restent accessibles.

## Dialogues, quêtes et mécanismes

Les questions des PNJ proposent une liste ordonnée de réponses ; les quêtes une liste ordonnée d'étapes. Les conditions disposent d'un constructeur guidé : caractéristique/état, comparaison, valeur et relation **ET/OU**. L'expression complète reste accessible pour les variantes du serveur.

Les portes et mécanismes proposent des couples **carte / cellule** pour les emplacements activés, désactivés ou requis, ainsi que le bouton et la durée. Les téléportations, fins de combat et actions des objets exposent les identifiants d'actions et leurs arguments. Les codes dépendant de l'émulateur restent nommés explicitement et modifiables ; leur sens n'est pas remplacé par une traduction inventée.

## Cartes et placements

L'éditeur graphique utilise toute la zone centrale pour la carte. Les documents ouverts disposent d'onglets en haut, les outils sont à gauche, les propriétés par rubrique à droite et les tuiles en bas. La grille est affichée par défaut. Les préférences permettent d'ajuster les cellules à la zone de travail ou de conserver leur taille.

Les tuiles se placent sur la cellule indiquée par la souris. Si la bibliothèque d'images ne fournit pas de point d'ancrage, Azur calcule les limites des pixels visibles en ignorant les marges transparentes : les sols sont centrés sur la cellule et les objets sont attachés par le milieu de leur bord inférieur. Le survol et le contour de sélection utilisent le même calcul, y compris quand la carte est ajustée à la fenêtre.

Les propriétés regroupent identité/position, zones, ambiance/fond, monstres, autorisations, déplacement des cellules, calques, orientation et équipes de combat. Sélectionner une cellule donne accès à ses options ; les huit types de déplacement restent disponibles. Un placement de combat sur une cellule bloquée est refusé.

**Sauvegarder** enregistre le projet AME Azur. **AME / SWF / SQL** enregistre le projet et les exports sélectionnés : vrai SWF binaire, SQL basé sur les colonnes réellement présentes dans la table des cartes. L'export SQL produit un fichier et n'écrit pas directement dans le serveur. Les autres colonnes d'une carte existante sont conservées. L'import accepte AME Azur v1/v2/v3 et les cartes SWF FWS/CWS à affectations AVM1 simples. Une carte 15 × 17 contient 479 cellules ; les anciens projets Azur de 478 cellules gagnent une dernière cellule vide à l'ouverture. Dans **Autorisations de la carte**, les cases règlent quatre droits connus ; la valeur complète permet aussi de modifier les autres bits, comme la valeur 98 des cartes Nowel.

Le menu **Outils** ouvre les placements de la carte sélectionnée. L'ajout propose la cellule courante ; l'appartenance à la carte est fixe dans cet éditeur. Le bouton **Placements serveur** charge en lecture seule les PNJ (P bleu), groupes fixes (M violet), zaaps (Z vert) et les enclos (E orange) lorsqu'une cellule serveur est connue ; survoler une cellule affiche le détail. Un contour orange repère aussi les cellules d'enclos définies dans la carte. Le dump `kauth.sql` fourni possède `mountpark_data.cellid` ; le dump `kworldsave.sql` plus ancien n'en possède pas, et ne peut donc situer l'enclos depuis sa seule fiche serveur. Les repères se rechargent après un enregistrement, restent facultatifs et n'entrent ni dans l'AME ni dans le SWF.

L'atelier donne aussi accès aux placements de toutes les cartes et à l'ensemble des colonnes serveur des cartes.

## Gestionnaire et configuration

Le gestionnaire propose :

- **Exporter XML** : toutes les colonnes ou ressources adaptées à AzurBot ; export SQL de la catégorie choisie, sous forme d'INSERT pour une table vide disposant du même schéma ;
- **Cartes SWF / AME** : dimensions, identifiant, clef et données encodées, validation/déchiffrement et production SWF binaire ou AME ;
- **Images** : aperçu, identifiant numérique, dossier de bibliothèque et enregistrement PNG ; remplacement d'un fichier existant après confirmation ;
- **Messages** : ouverture et filtrage d'un journal local, limité à 20 Mo et aux 5 000 premières lignes correspondantes ;
- **Capture réseau** : relais TCP sur 127.0.0.1, adresse/port du serveur, démarrage et arrêt, messages par sens, recherche, détail et export masqué.

Le convertisseur vérifie les 479 cellules des cartes 15 × 17 et travaille sur une copie. Les propriétés supplémentaires des cellules AME sont conservées ; un changement de dimensions d'une carte importée doit passer par l'éditeur graphique.

La capture nécessite de configurer le client vers le relais local. Les échanges d'authentification reconnus sont masqués ; ce masquage ne couvre pas toutes les données personnelles possibles. Aucun fichier de capture n'est enregistré automatiquement. Voir [le guide de capture](CAPTURE_RESEAU.md).

La configuration masque le mot de passe SQL et présente connexions, émulateur et versions. **Configurer les tables** propose des noms français pour les fonctions sans changer leurs clefs internes. Les noms restent en attente jusqu'à l'enregistrement, suivi d'un redémarrage.

Pour kauth, la connexion **auth** doit accéder à `accounts`, `players` et aux ressources statiques ; **world** doit accéder aux exemplaires `items` de kworld. Les joueurs Kryone utilisent la correspondance auth **Personnages → players**. La valeur world historique **Personnages → characterinstance** reste conservée pour les anciennes configurations et n'est pas utilisée par ces éditeurs Kryone.

Dans **Configuration → Configurer les tables → Correspondances**, vérifiez les valeurs suivantes si vous conservez un ancien JSON :

| Fonction / clef de configuration | Table kauth |
| --- | --- |
| Coffres (`coffre`) | `coffres` |
| Monstres supplémentaires (`extra_monstres`) | `extra_monster` |
| Actions des objets (`object_action`) | `objectsactions` |
| Schémas de combat (`schema_fight`) | `schemafights` |
| Titres (`titres`) | `titre` |

Les nouvelles configurations utilisent ces correspondances. Les JSON personnalisés existants restent conservés ; enregistrez les ajustements voulus, puis redémarrez Azur. Voir [la compatibilité kauth](KAUTH_COMPATIBILITE.md).

## Variantes, NULL et stockage

Toutes les colonnes chargées disposent d'un champ. Les colonnes propres à une variante sont accessibles dans **Avancé**, avec leur nom technique. **Non défini** correspond à SQL NULL, distinct d'un texte vide ou de zéro. Les colonnes calculées restent au serveur. Les tables sans clef unique sont modifiées en comparant toute la ligne originale ; plusieurs correspondances identiques bloquent l'écriture.

Les enregistrements groupés et les vérifications de relations nécessitent les tables InnoDB concernées. **Activer l'enregistrement…** affiche les tables nécessaires, leur état et l'option **Préparer ces tables** pour convertir explicitement MyISAM en InnoDB. La conversion modifie durablement le stockage : utilisez une sauvegarde et arrêtez le serveur de jeu avant de la lancer. La liste inclut les dépendances et, lorsque world est connecté, les exemplaires nécessaires aux contrôles de modèles d'objets. Les conversions terminées restent affichées après une interruption. Aucune conversion n'est lancée automatiquement.

Le fichier `kauth.sql` fourni contient les comptes, personnages et ressources statiques ; `kworldsave.sql` fournit notamment les exemplaires d'objets. Les tests reprennent uniquement les définitions de tables et créent des données fictives dans une base temporaire. Aucune ligne privée du dump n'est importée.

## Vérification et limites

Les commandes `tests/Run-Tests.ps1 -Integration` et `tests/Run-Tests.ps1 -Configuration Release -Integration` exécutent **23 tests : 15 sans base et 8 d'intégration**, réussis dans les deux configurations le 1er octobre 2026. Ils vérifient les 24 familles d'éditeurs, formulaires, valeurs inchangées, saisies invalides conservées, contrôles numériques, couleurs, formats structurés, conflits, écritures et refus sur une instance MySQL indépendante. Le test kauth reproduit les 58 tables InnoDB réelles et vérifie les parcours de comptes/personnages avec des données synthétiques. Les tests locaux couvrent aussi la capture, le transport du bot et le convertisseur du gestionnaire. Chaque exécution produit des captures PNG des vrais formulaires, avec des données de démonstration.

La validation dans le client/serveur de jeu, la compatibilité des SWF d'objets du client cible, l'ancien AME Astria, les scripts SWF complexes et les redirections auth/jeu de la capture restent nécessaires. L'état du projet détaille les autres critères de fin.

Le kit fourni dans `F:\kit` contient les serveurs StarLoco Login/Game et le client Dofus 1.34 pour les prochains essais réseau et visuels. La compatibilité SQL décrite dans ce guide reste celle de Kryone/kauth ; les tables de StarLoco nécessitent leur propre adaptation et validation.
