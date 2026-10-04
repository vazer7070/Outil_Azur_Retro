# Bot Azur et StarLoco

La refonte du 2 octobre 2026 utilise les sources Login et Game de `F:\kit\03 - Emulateur StarLoco`, les designs historiques d’Azur et les ressources du client Dofus 1.34 fourni dans le kit. Les corrections réseau disposent de tests avec des serveurs fictifs locaux. Le serveur et le client Dofus du kit ne sont pas lancés par ces tests. Une précédente livraison avait passé 29 tests locaux en Debug et Release ; la nouvelle sélection officielle et les actions manuelles de combat font l’objet d’une nouvelle vérification. La validation en jeu réel reste à effectuer.

## Utilisation

1. Ouvrez le module **Bot**. La préparation indique les ressources réellement chargées, les fichiers rejetés et les dossiers vides.
2. Ouvrez **Connexion → Serveur et protocole**. Le préréglage StarLoco local renseigne `127.0.0.1`, Login `450`, Game `5555` et version `1.34.1`. Appliquez ces valeurs si elles correspondent à votre serveur de test.
3. Renseignez un compte créé sur ce serveur. Le bot se connecte en TCP ; une réponse au ping réseau n’est pas nécessaire. Les noms DNS et IPv6 sont acceptés.
4. Choisissez un serveur en ligne, puis un personnage sur la scène à cinq podiums inspirée de l’écran officiel. La bannière, les podiums, les cartouches et le bouton **Jouer** viennent du client fourni. Les flèches parcourent les pages lorsqu’il y a plus de cinq personnages ; l’actualisation conserve l’identifiant sélectionné. **Jouer**, un double-clic sur un personnage ou la touche Entrée valident la sélection. Un serveur sans personnage reste visible grâce à `AH`, même lorsque `AxK` ne l’énumère pas.
5. La fenêtre de jeu donne la place principale à la carte. Le chat reste en bas à gauche, la vie et l’expérience au centre du bandeau inférieur, les raccourcis à droite. Les fiches de personnage, inventaire, sorts et métiers s’ouvrent dans un panneau refermable. Une carte absente des ressources est signalée ; le bot ne fabrique pas de cellules pour la remplacer.
6. Sur la carte, un **clic gauche sur un PNJ** envoie `DC<pnj>` et un **clic droit** envoie `ER0|<pnj>` (l’entrée « Échanger » du client). Un **clic gauche sur un zaap** connu de `BotZaaps` envoie `GA500<cellule>;114`. Les volets **Dialogue**, **Zaaps** et **Boutique** du panneau latéral s’ouvrent uniquement à la réponse du serveur (`DCK`/`DQ`, `WC`, `ECK0`/`EL`) et se referment sur `DV`, `WV` ou `EV`. Le dialogue affiche le nom du PNJ connu de `BotNPCs`, « Question n° X » et un bouton par « Réponse n° Y » (`DR<question>|<réponse>`), puis **Quitter** (`DV`) : les textes des questions et des réponses ne sont pas exportés dans `BotNPCs`, seuls leurs numéros sont affichés. Le volet Zaaps liste les destinations de `WC` avec les coordonnées de `BotMaps` (sinon « Carte <id> »), leur coût en kamas, le zaap de sauvegarde et la carte courante ; **Se téléporter** envoie `WU<carte>` et **Fermer** `WV`. La boutique liste les articles de `EL` avec le nom de `BotObjets` (sinon « Objet n° X ») et le prix (« prix non transmis » lorsque le serveur l’omet) ; **Acheter** envoie `EB<modèle>|<quantité>`, **Vendre** depuis la liste du sac envoie `ES<objet>|<quantité>` et **Fermer** `EV`. Dans la fiche **Inventaire**, **Équiper** et **Déséquiper** envoient `OM<objet>|<emplacement>|1` (`-1` pour déséquiper), **Utiliser** `OU<objet>|` et **Jeter** `OD<objet>|<quantité>` après un second clic de confirmation. Aucune de ces actions ne modifie l’état local avant la confirmation du serveur.
7. Le journal des paquets permet de filtrer, suspendre l’affichage, effacer ou exporter les 300 derniers messages. Les identifiants, clés de connexion et tickets sont masqués.

Le port Game saisi sert de référence dans les paramètres ; la redirection annoncée par le Login dans `AYK` ou `AXK` détermine la destination réelle. Le port interne `666` est réservé au relais entre les serveurs : le bot ne s’y connecte pas.

## Carte et interface de jeu

L’interface reprend la composition de `GameClientFullform.Designer.cs` : menu en haut, carte au centre et bandeau de jeu en bas. Depuis le 3 octobre 2026, les éléments du client fourni sont exportés de `modules/core.swf` dans [`Outil_Azur_complet/Resources/Bot/Client`](../Outil_Azur_complet/Resources/Bot/Client/PROVENANCE.md) et copiés à côté de l'exécutable (`ressources/Bot/UI/Client`) : pilules orange et parchemin des boutons (`ChooseCharacterBtnPlay`, `ButtonDownload`), logo et œufs de classe de l'écran de connexion, socle et dé des couleurs de l'écran de création, neuf icônes `UI_Banner*Icon` du bandeau de jeu, flèche de fin de tour. Les icônes du fichier `GameClientFullform.resx` et le rendu dessiné servent de repli quand un fichier manque. Les tons bruns, olive et parchemin viennent des formes du même `core.swf`. `BotClientSkinSmoke` vérifie la livraison et l'usage de ces éléments. La fenêtre principale s’ouvre à 1100 × 760 et reste redimensionnable jusqu’à 850 × 620.

Le menu **Personnage** et les icônes du bandeau inférieur ouvrent les fiches. Le bouton de fermeture ou la touche Échap libèrent la carte. Les dix raccourcis de sorts utilisent des icônes de 30 × 30 pixels ; les flèches donnent accès à tous les sorts appris, par pages de dix. Un clic droit ouvre la fiche du sort. Pendant votre tour de combat, un clic gauche sélectionne le sort à lancer puis le clic sur une cellule demande son lancement ; Échap annule la sélection. Les boutons Quêtes, Amis, Guilde, Monture et Conquête restent explicitement désactivés lorsque leur interface n’est pas implémentée.

Pour utiliser la carte :

- **Clic gauche** sur une cellule accessible : demander un déplacement au serveur hors combat. Pendant un combat, le même clic demande le placement, le déplacement ou le lancement du sort sélectionné selon la phase courante.
- **Molette** ou boutons **− / +** : zoomer, avec maintien du point sous la souris pour la molette.
- **Bouton central maintenu**, ou **Ctrl + glisser avec le bouton gauche** : déplacer la vue sans envoyer de déplacement du personnage.
- **Adapter** ou **Affichage → Ajuster la carte** : retrouver la vue complète centrée. La touche Début a le même rôle lorsque la carte a le focus.
- **Affichage → Afficher la grille / Numéros des cellules** : activer les repères de diagnostic.
- **F11** : basculer en plein écran.

Le statut de la carte expose les visuels absents au lieu de les présenter comme chargés. Les personnages, PNJ et groupes de monstres reçus dans `GM` sont représentés avec les sprites PNG disponibles et leurs cellules annoncées. Un déplacement anime la position du PNG le long du trajet ; ce mouvement ne reproduit pas le cycle de marche du sprite Flash. Lorsque le décor ou le sprite d’une entité manque, des repères permettent encore de distinguer les cellules et entités. Le rendu et la détection du clic doivent utiliser la même transformation après redimensionnement, zoom et déplacement de la vue ; cette correspondance fait partie des vérifications de livraison.

La création de personnage montre le portrait de la classe et du sexe sélectionnés, à partir des PNG disponibles. Les trois nuanciers indiquent les couleurs envoyées lors de la création. Le portrait utilise les couleurs d’origine : la recoloration des sprites Flash n’est pas simulée par une teinte globale du PNG.

## Caractéristiques et sorts : actions manuelles

Dans la fiche **Caractéristiques**, choisissez la caractéristique à augmenter, puis utilisez **Augmenter**. L’interface indique les points disponibles et un coût estimé selon la classe et la valeur actuelle. Le serveur décide si l’augmentation est autorisée et renvoie les nouvelles statistiques ; son gain dépend de la classe, notamment deux points de vitalité pour un Sacrieur. Le bot ne répartit pas les points automatiquement.

L’onglet **Sorts** affiche les sorts réellement annoncés par le serveur, avec leur nom et leur niveau. Sélectionnez un sort, puis utilisez **Améliorer de 1 niveau**. Le passage du niveau 2 au niveau 3 coûte, par exemple, 2 points de sort : le coût correspond au niveau actuel. Le niveau maximal est 6. Le bouton tient compte des points disponibles et de l’état du personnage ; le serveur vérifie aussi le niveau de personnage requis.

Une amélioration n’est affichée qu’après confirmation du serveur. En cas de refus, le journal explique qu’il faut vérifier les points et le niveau requis. Si les ressources d’un sort sont absentes, sa ligne reste visible sous **Sort #identifiant**, mais son amélioration est désactivée jusqu’au chargement des données correspondantes dans `BotSorts`. Le bouton de cet onglet améliore le niveau ; les raccourcis du bandeau servent au lancement manuel en combat.

## Combat manuel en cours de validation

Le client suit la phase de placement, le combattant dont c’est le tour et les PA/PM annoncés. En placement, cliquez sur une cellule autorisée puis utilisez **Prêt** ; le bouton devient **Annuler** pour retirer cet état. Pendant votre tour, utilisez les sorts du bandeau ou les touches **1 à 9 / 0**, cliquez sur une cellule cible, ou déplacez-vous sans sort sélectionné. **Passer** termine le tour. Les raccourcis de prêt et de passage sont indiqués dans l’interface.

Les contrôles locaux portent sur la connexion, le tour, les cellules connues, les PA/PM disponibles et les contraintes connues du sort : portée, lancer en ligne, cellule vide, nombre de lancers et intervalle. Les PA/PM ne sont pas dépensés par une simple demande locale : seuls les messages du serveur confirment l’action. La ligne de vue complète, les états, les modificateurs d’équipement et les limites par cible restent sous l’autorité du serveur. Les cellules proposées ne garantissent donc pas qu’un lancement sera accepté.

Ce parcours manuel et le traitement des confirmations/refus doivent encore être vérifiés sur le vrai serveur isolé. Il ne constitue pas une IA de combat automatique ni une couverture complète de tous les effets et règles.

## Question secrète et suppression d’un personnage

La page de sélection des personnages conserve la question secrète reçue à la connexion. Ouvrez **Réponse secrète pour la suppression** pour afficher la question et le champ masqué ; la réponse n’est pas enregistrée avec le compte. Sélectionnez le personnage, renseignez la réponse si nécessaire, puis confirmez la suppression proposée par l’interface.

Dans les sources StarLoco du kit, la suppression d’un personnage de niveau 20 ou plus exige la bonne réponse secrète. Les personnages de niveau inférieur peuvent être supprimés sans cette réponse. Le serveur confirme la suppression en renvoyant la liste des personnages ; un refus conserve le personnage et affiche une erreur. Les règles peuvent différer sur un autre émulateur.

## Ressources

Les données du serveur cible doivent être exportées en XML dans les dossiers `ressources/Bot` : `BotMaps`, `BotObjets`, `BotJobs`, `BotMonsters`, `BotNPCs`, `BotZaaps` et `BotSorts`. Le parseur d’Azur sait exporter les ressources compatibles du schéma Kryone ; cela ne constitue pas automatiquement une adaptation de toutes les tables StarLoco.

Une carte XML contient notamment `ID`, `LARGEUR`, `LONGUEUR`, `X`, `Y`, `MAP_DATA` et `BACK`. Les cellules utilisent dix caractères chacune. Le code de cellule et le chemin de déplacement utilisent l’alphabet Dofus à 64 caractères.

L’index des PNG est préparé en arrière-plan avant la connexion et partagé entre les cartes. Les zones transparentes des images ne diminuent pas le cadrage du terrain. Le zoom affiché est relatif à la vue adaptée : 100 % correspond à **Adapter**.

Le décor portable est reconstruit à partir des images de `ressources/maps/sols`, `ressources/maps/objets` et `ressources/maps/backgrounds`. Les noms de fichiers numériques correspondent aux identifiants graphiques. Les apparences des personnages et entités utilisent les images de `ressources/Bot/sprites` ; les données de carte restent dans `ressources/Bot/BotMaps`. Les images illisibles ou absentes sont signalées. Les visuels chargés sont conservés pendant la vie de la carte puis libérés lors du changement ou de la fermeture, sans verrouillage durable des fichiers.

La sélection et l’aperçu de création utilisent les 24 illustrations de classe et de sexe exportées depuis `clips/artworks/big/{gfx}.swf`. Le dossier source `Outil_Azur_complet/Resources/Bot/Selection` contient également les éléments de l’écran `UI_ChooseCharacter` de `core.swf` et leur [provenance](../Outil_Azur_complet/Resources/Bot/Selection/PROVENANCE.md). Ils sont copiés à côté de l’exécutable dans `Resources/Bot/Selection` : aucun accès à `F:\kit` ni décompilateur n’est nécessaire au lancement. Ces illustrations gardent les couleurs d’origine ; elles ne simulent pas la personnalisation Flash.

Cette reconstruction affiche les images disponibles et les couches de décor ; elle n’exécute pas le client Flash. Le rendu vectoriel intégral des SWF, leurs timelines, la recoloration par zones, les animations originales et la déformation complète des sols en pente restent à implémenter. Les niveaux de terrain sont pris en compte pour placer les cellules, mais cela ne suffit pas à garantir l’aspect de toutes les pentes du client officiel.

Les caches de ressources sont rechargés sans multiplier les tâches par fichier. Une erreur de lecture est rendue à l’interface ; les données précédentes restent conservées lorsqu’un chargement complet échoue. Le chargement des cartes peut écarter des fichiers invalides et en donner le détail.

## Comptes enregistrés

L’enregistrement est facultatif. Les nouveaux mots de passe enregistrés sont protégés avec Windows DPAPI pour l’utilisateur Windows courant. Ils restent déchiffrables sur cet ordinateur dans sa session Windows ; copier les fichiers sur une autre machine ou vers un autre utilisateur ne garantit pas leur lecture.

Les anciens fichiers JSON contenant un mot de passe en clair restent lisibles. Un nouvel enregistrement du même compte remplace son fichier par la version protégée. Un fichier illisible n’est ni effacé ni écrasé au chargement. Les espaces des mots de passe sont conservés. Les noms de fichier sont calculés à partir du compte et ne servent pas directement de chemin.

La configuration locale du bot et le dossier `AccountSingle` sont exclus des nouvelles archives portables, même si des comptes ont été enregistrés dans le dossier Release.

## Contrat réseau vérifié

- Réception UTF-8 jusqu’au NUL, réassemblage des fragments et traitement des messages dans leur ordre d’arrivée.
- Initialisation du registre sans doublons ; choix du préfixe reconnu le plus long (`ATK0` avant `ATK`) et attente des gestionnaires asynchrones.
- `HC → version → compte → #1<mot de passe chiffré> → Af`, puis `AH/AQ/AxK → AX → AYK/AXK`.
- `HG → AT → ATK0 → Ak0/AV → AV0 → Agfr/AL/Af → ALK → AS → ASK → GC1`.
- `AQ` renseigne `SecretQuestion` après décodage de la question. La suppression manuelle envoie `AD<id>|<réponse>`, avec les espaces de la réponse encodés en `%20`, conformément aux sources du kit.
- `As` actualise les points de caractéristiques et de sorts. Une augmentation manuelle envoie `AB<caractéristique>` : vitalité `11`, sagesse `12`, force `10`, intelligence `15`, chance `13`, agilité `14`.
- `SL<id>~<niveau>~<emplacement>;…` remplace la liste des sorts du personnage. Une demande manuelle `SB<id>` reçoit `SUK<id>~<niveau>` en cas de succès ou `SUE` en cas de refus. Les informations de chaque personnage sont copiées sans modifier les modèles de ressources ni les autres comptes ; une liste vide efface les anciens sorts.
- `GDM` actualise les ressources de carte avant `GI`. Les entités `GM`, états interactifs `GDF` et accusés de déplacement utilisent les identifiants/cellules fournis par le serveur.
- Les demandes manuelles de combat utilisent `Gp<cellule>` pour le placement, `GR1/GR0` pour l’état prêt, `Gt` pour passer, `GA001<chemin>` pour se déplacer et `GA300<sort>;<cellule>` pour lancer un sort. L’intégration du combat fait l’objet de la nouvelle série de contrôles ; ces ajouts ne sont pas couverts par les résultats de la précédente livraison à 29 tests.
- Inventaire : `OAK` lu en fiches séparées par `*`, de type `O` (`id~modèle~quantité~position~effets`, nombres en hexadécimal comme dans `Items.onAdd` du client ; les fiches `G` et illisibles sont ignorées sans bloquer les suivantes), `OAE` A/L/F, `OQ<id>|<quantité>`, `OM<id>|<position>` (position vide = sac), `OC` dont le troisième caractère est ignoré comme dans le client (StarLoco envoie `OC|…` ou `OCO…`), `OS+<panoplie>|<objets>|<bonus>` / `OS-<panoplie>`, `OT<métier>` / `OT`, `OK` journalisé sans réponse automatique, `OR<id>` retirant l’objet entier. Envois `OM<id>|<position>|1`, `OU<id>|` et `OD<id>|<quantité>` ; l’état local attend la réponse du serveur.
- Dialogues PNJ : `DC<pnj>` → `DCK<pnj>` ou `DCE`, `DQ<question>[;<paramètres>]|<réponse>;<réponse>` (un `DQ` isolé ouvre aussi la fenêtre), `DP`, envoi `DR<question>|<réponse>` limité aux réponses proposées, `DV` dans les deux sens. Les identifiants de PNJ reçus dans `GM` sont négatifs chez StarLoco. Ouverture refusée localement en combat, en déplacement, en récolte ou lorsqu’une autre fenêtre est ouverte.
- Zaaps : `GA500<cellule>;114` sur une cellule de `BotZaaps` → `WC<zaap sauvegardé>|<carte>;<coût>|…` (le premier champ est le zaap de sauvegarde, pas la carte courante ; la carte courante coûte 0 ; les entrées illisibles sont ignorées), `WU<carte>` refusé localement pour la carte courante, une carte absente de la liste ou des kamas insuffisants, tout `WU…` reçu traité comme l’erreur `WUE`, `WV` dans les deux sens.
- Boutique PNJ : `ER0|<pnj>` → `ECK0|<pnj>` puis `EL<modèle>;<effets>[;<prix>]|…` (StarLoco omet le prix nul : « prix non transmis »), `EB<modèle>|<quantité>` → `EBK` / `EBE`, `ES<objet>|<quantité>` → `ESK` / `ESE` puis `OQ` ou `OR`, `EV` et `EVa`. Un seul achat ou une seule vente en attente à la fois ; quantité limitée à 100 000 comme chez StarLoco. Un `ECK` d’un autre type place le compte en état d’échange sans ouvrir la boutique ; `EL` hors boutique est ignoré.
- Envois complets et sérialisés, anciennes sessions isolées, délais de connexion, nettoyage et reconnexion après erreur.
- Durées d’abonnement en millisecondes sur 64 bits. Le bot ne confond plus les millisecondes avec des jours.

Le mot de passe réseau est chiffré avec la clé `HC`. Le hash `SHA512(MD5)` observé dans le Login appartient à la vérification en base par le serveur ; il n’est pas envoyé à sa place. Les erreurs d’identifiants, de version, de bannissement, de redirection et de création/sélection sont expliquées en français.

Session (lot S1) : comme le client 1.34, `ASK` n'est suivi que de `GC1` et une création que de `AS<id>` (ni `BYA` ni `AF`). `GCK|1|<nom>`, `AR<restrictions en base 36>` (masques 1 à 256 du client, `6bk` = 8192 = aucune restriction), `Ac<communauté>`, `BT<heure serveur en ms>`, `BN` et `AN<niveau>` alimentent `GameClass.Session` sans réponse. Chaque `Im<type><id>[;a~b…]|…` (0 info, 1 erreur, 2 JcJ) produit l'événement `ServerMessageReceived` et un journal, texte fourni par `ServerMessages.Resolver` sinon `Im<type><id> : arguments`, sans envoi automatique. Une invitation de groupe `PIK<invitant>|<invité>` ou de guilde `gJr<id>|<nom>|<guilde>` n'est jamais acceptée seule : elle est proposée à `PartyInviteReceived`/`GuildInviteReceived` puis refusée par `PR` ou `gJE<id>` si aucun abonné ne s'en charge ; `gJR` n'est que la confirmation côté invitant. `Bp` reste sans réponse et `pong` mesure l'aller-retour de `ping`. Un paquet sans gestionnaire est journalisé en débogage, un préfixe déclaré deux fois fait échouer `MessagesReception.Init`, et chaque envoi ne contient qu'un paquet (`PacketSent` ; vide, NUL ou retour à la ligne refusés avec `PacketRejected`).

## Limites avant validation en jeu

Cette version améliore le client manuel et son diagnostic. Elle ne réalise pas encore les modes automatique/admin, les scripts Lua ou plugins annoncés dans le README. Les définitions complètes des interactifs, la récolte, les échanges avancés et les règles de combat non couvertes doivent encore être complétés et vérifiés. Les actions manuelles de combat sont en cours de validation ; les profils officiels et l’authentification via launcher ne sont pas validés par ces tests.

Les dialogues PNJ, les zaaps, la boutique PNJ et les actions d’inventaire ne sont vérifiés qu’avec des paquets fictifs reproduisant les formats du client 1.34 et des sources StarLoco du kit (`BotDialogsSmoke`, `BotShopSmoke`). Il reste à les rejouer sur un vrai StarLoco : ordre réel de `DCK` et `DQ`, effets des réponses de dialogue, `WC` d’un personnage sans zaap sauvegardé, prix et effets réels de `EL`, `OQ`/`OR` après une vente, conditions `OK` (jamais envoyées dans les sources du kit) et erreur `ERE` lorsqu’une fenêtre est déjà ouverte côté serveur. Les textes des questions et réponses ne sont pas exportés dans `BotNPCs` : seuls leurs numéros sont affichés. Les zaaps sont reconnus par `BotZaaps`, non par les interactifs de la carte dont les définitions ne sont pas chargées ; le gestionnaire de téléportation historique (`GA500…;157`, zaapi) n’a pas été modifié. La boutique n’offre ni artisanat, ni échange entre joueurs, ni hôtel de vente ; la réponse automatique `EV` à une demande d’échange `ERK` d’un autre joueur est conservée. Le clic droit « Échanger » n’est pas confirmé dans le code décompilé du client fourni : il reprend le paquet `ER0|<pnj>` que StarLoco attend pour ouvrir une boutique.

Il reste à connecter le bot à une copie isolée du vrai StarLoco, avec comptes et personnages de test, puis vérifier cartes, déplacements, créations/suppressions de personnages et réponse secrète, discussions, métiers, augmentations de caractéristiques et de sorts, interactions et combats. Les corrections d’Azur ne modifient pas les sources du kit ni les bases utilisateur. Les défauts internes de l’émulateur relevés dans [l’analyse des sources](STARLOCO_SOURCES_ANALYSE.md) restent distincts du client bot.

Pour la reprise de développement, consulter [le brief de refonte de l’interface](BRIEF_REDESIGN_UI_BOT.md). Il précise les références, les limites du rendu actuel et les vérifications visuelles à effectuer avant de considérer la refonte terminée. Les résultats automatisés et les livrables du 2 octobre 2026 sont consignés dans ce brief ; ils ne remplacent pas le parcours en jeu réel.

La session du lot S1 n'est vérifiée qu'avec des paquets fictifs (`BotSessionSmoke`) : les textes `Im` attendent les fichiers lang du client (aucun traducteur n'est branché, d'où le texte de repli), les arguments ne sont pas convertis en noms d'objets, de sorts ou de métiers, les restrictions des autres joueurs (`Personnages.Restrictions`) sont lues dans `GM` en base 36 comme le fait le client (StarLoco place à cet endroit la vitesse du joueur, ce qui peut produire des restrictions fantaisistes), et l'ancien envoi `#Z` du mode launcher, sur deux lignes, est désormais refusé par la règle d'un paquet par envoi.

## Modèle d’acteurs de la carte (`GM`, `GA` hors combat)

Les entités d’une carte sont des `MapActor` (`Tool_BotProtocol/Game/Maps/Entities`), qui implémentent toujours l’interface historique `Entites`. `Map.Actors` (alias `Map.Entites`, clé `long`) contient les autres acteurs ; le personnage du compte est rangé à part dans `Map.Self`. Les épées de combat (`Map.FightSwords`, par identifiant de combat), les objets au sol (`Map.GroundObjects`) et les états d’objets interactifs (`Map.ObjectStates`) sont tenus séparément. Les événements `ActorAdded`, `ActorRemoved`, `ActorUpdated`, `ActorsCleared`, `CellUpdated`, `GroundObjectChanged`, `ObjectStateChanged` et `MapChanging` sont levés sur le fil réseau, après la mise à jour de l’état. Les vues `NPC_List`, `MonsterList`, `PersoList` et `CellsOccuped` restent disponibles : elles ne listent que les acteurs dont la cellule existe sur la carte chargée.

`GmParser` découpe les `GM` concaténés par NUL, puis les entrées `+` (ajout), `~` (remplacement) et `-` (retrait). Le champ type choisit la classe, comme `Game.onMovement` du client 1.34 :

| Type | Acteur | Champs conservés |
|---|---|---|
| ≥ 0 (classe) | `PlayerActor` | titre après la virgule du type, sexe, alignement (`côté,valeur,grade,niveau+id[,déchu]`), couleurs, `Stuff` et accessoires, aura, émote, guilde et emblème, restrictions, monture ; en combat : niveau, PV/PA/PM, résistances, équipe |
| `-1` / `-2` | `FightMonsterActor` | modèle, grade (index 7, pas le niveau), couleurs, accessoires, PV/PA/PM, résistances et équipe |
| `-3` | `MonsterGroupActor` | `Stars` = champ [2] brut (bonus du groupe ; son affichage en étoiles est laissé au rendu), `Members` (modèle, niveau, sprite, couleurs, accessoires), `Leader` |
| `-4` | `NpcActor` | modèle (l’identifiant de sprite, négatif chez StarLoco, n’est jamais pris pour le modèle), sexe, couleurs, accessoires, `ExtraClip`, `Artwork` |
| `-5` | `MerchantActor` | nom, couleurs (dès l’index 7), `Stuff`, guilde, type hors ligne |
| `-6` | `CollectorActor` | prénom et nom en base 36, niveau, guilde : « Percepteur de &lt;guilde&gt; » (le préfixe doublé `GM|GM|+…` de StarLoco est accepté) |
| `-9` | `ParkMountActor` | nom, propriétaire, niveau, modèle |
| `-10` | `PrismActor` | niveau, valeur et côté de l’alignement |
| autre | `UnknownActor` | champs bruts |

Le champ graphique accepte `gfx^taille`, `gfx^largeurxhauteur`, les marqueurs `*` (pas de miroir, pas de mode fantôme) et les sprites liés séparés par `,` ou `:`. Le nom d’un PNJ ou d’un monstre vient de `PNJ.ClientNameResolver` / `Monstres.ClientNameResolver` lorsque les textes du client y sont branchés, sinon de `BotNPCs` / `BotMonsters`, sinon « PNJ #modèle » / « Monstre #modèle ». Une entrée illisible est journalisée (`CARTE`) et ignorée sans arrêter les suivantes.

Paquets traités par `MapFrame` hors combat :

- `GA;0` : déplacement en cours annulé et refus journalisé.
- `GA<id>;1;<acteur>;a<cellule de départ><chemin>` : le chemin est décodé depuis la cellule de départ (`ServerMovePath.Path` le donne sans ce préfixe). StarLoco n’envoie pas de `GAF` hors combat : pour le personnage du compte, le bot envoie un seul `GKK<id>` après la durée calculée localement (le gestionnaire attend encore cette durée) ; un autre acteur prend la cellule d’arrivée et l’orientation du dernier pas.
- `GA;2;<acteur>;[cinématique]` : l’acteur quitte la carte. Pour le personnage du compte, la carte est vidée et `MapChanging` est levé avant le `GDM` suivant.
- Les autres actions passent par `GameActionRouter` : chaque fonction ajoute ses gestionnaires dans son propre fichier (`[GameActionHandler(id)]` sur une méthode statique de `Tool_BotProtocol`, ou `GameActionRouter.Register`). Une action n’a qu’un gestionnaire ; un doublon lève une exception. `GA;4` (repositionnement `<acteur>,<cellule>`) y est enregistré.
- `GDM|<carte>|<date>|<clé>` envoie toujours `GI`, même lorsque la carte manque dans `BotMaps` ou que son identifiant est illisible (carte vide et message dans le journal).
- `GDF|<cellule>;<état>[;<1|0>]|…` : tous les triplets sont gardés dans `Map.ObjectStates` (1 plein, 2 en cours, 3 vide ou porte ouverte, 4, 5) ; l’indicateur historique `IsUsable` vaut toujours « état 1 ».
- `GDO+<cellule>;<modèle>;<type>[;<durabilité>;<max>]` et `GDO-<cellule>` : objets au sol.
- `GDC<cellule>;<10 caractères><masque hexadécimal>;<permanent>` modifie la cellule (portes, labyrinthes) ; `GDC<cellule>` seul la restaure. Le bot applique la ligne de vue (bit 4096) et le type de déplacement (bit 2048) ; les bits graphiques et le bit « active » (8192) ne sont pas appliqués.
- `eD<acteur>|<direction>` (ignoré en combat), `Oa<acteur>|<accessoires>`, `Gc+<combat>;<type>|<équipe>;<cellule>;<type d’équipe>;<alignement>|…` et `Gc-<combat>`.

`BotActorsModelSmoke` vérifie ces formats sur un serveur fictif local avec une carte synthétique. Ils proviennent des sources StarLoco du kit et du client 1.34 ; ils restent à rejouer sur un vrai StarLoco (marchands, percepteurs, prismes, montures d’enclos et portes `GDC` notamment). La recoloration des sprites, l’affichage des étoiles, des titres et des épées et les menus d’acteurs relèvent du rendu et de l’interface, pas de ce modèle.

## Protocole de combat (lot F12a)

`Fights` suit le combat d’après les paquets de StarLoco, lus comme le fait le client 1.34. Aucun paquet mal formé n’interrompt la lecture : il est journalisé (`COMBAT`) puis ignoré. Les événements (`CombatChanged`, `CombatResultReceived`, `FlagReceived`, `FightOptionChanged`, `JournalEntryAdded`) sont levés sur le fil réseau, après la mise à jour de l’état et hors du verrou.

Tours :

- `GTL|<id>|<id>…` donne l’ordre de jeu des combattants vivants (`TurnOrder`, `Timeline.Order`). La forme sans `|` initial (`GTL1|2|3`) est aussi lue. StarLoco le renvoie souvent dans `GA;999` après une invocation ou une mort.
- En combat, `GTR<id>` reçoit la réponse `GT`, comme `Game.turnOk` du client ; StarLoco l’ignore (matrice §2 n° 39).
- `GTS<id>|<durée ms>[|<tour>]` : StarLoco n’envoie que deux champs (matrice §2 n° 16). Le troisième champ, lu par le client, est facultatif (`Timeline.TableTurn`). `Timeline.RemainingMilliseconds` calcule le temps restant à partir de l’heure de réception.
- `GTF<id>` termine le tour. Les effets du combattant qui vient de jouer perdent un tour au `GTS` suivant (`GameManager.cleanPlayer` du client).

Effets, zones, options et drapeau :

- `GIE<effet>;<cibles séparées par ,>;<p1>;<p2>;<p3>;<p4>;<tours>;<sort>[;<lanceur>]` crée un effet par cible (`Effects`, `GetEffects`). Un effet de même numéro et de même durée s’additionne au précédent. Posé sur le combattant dont c’est le tour, il gagne un tour, comme dans `Game.onEffect`. Une durée négative (`-1`) ne s’épuise pas : seuls `GA;132`, la mort ou la fin du combat le retirent. `GIe` retire tous les effets.
- `GDZ+<cellule>;<taille>;<couleur>` dessine une zone de glyphe ou de piège et `GDZ-…` l’efface (`Zones`). Plusieurs entrées peuvent être séparées par `|`.
- `Go<+|-><A|S|P|H><équipe>` met à jour les options d’une équipe identifiée par son initiateur (`TeamOptions`, `OwnTeamOptions`). Ce paquet est diffusé à toute la carte : il est aussi lu hors combat. Comme dans le client, seules les lettres majuscules sont reconnues. `ToggleOptionAsync` envoie `fN` (bloquer), `fS` (spectateurs), `fP` (groupe seulement) ou `fH` (aide). Après le placement, seule l’option spectateurs reste disponible. StarLoco n’accepte ces demandes que de l’initiateur et vérifie lui-même le groupe pour `fP`.
- `SetFlagAsync` envoie `Gf<cellule>` ; un coéquipier signalé arrive sous la forme `Gf<combattant>|<cellule>` (`LastFlag`).
- `GiveUpAsync` envoie `GQ`. En placement, le personnage quitte le combat. En combat actif, StarLoco le compte comme mort (matrice §2 n° 22). En spectateur, il cesse d’observer. La réponse attendue est `GV`. `KickAsync(id)` envoie `GQ<id>` pendant le placement pour un coéquipier connu ; le serveur refuse l’exclusion d’un initiateur. `GR1` / `GR0` restent envoyés par `SetReadyAsync`.

Actions `GA` en combat : `MapFrame` transmet tout `GA` reçu en combat à `Fights`. Celui-ci traite `0` (refus) et `1` (déplacement), puis consulte `FightActionTable` et enfin `GameActionRouter` (défis, interactifs…). Une action sans gestionnaire est journalisée en débogage. Un acteur vide désigne le personnage du compte. Un module peut ajouter une action de combat avec `[FightActionHandler(id, …)]` sur une méthode statique `void|Task Nom(FightActionContext)` ou avec `FightActionTable.Register` ; un doublon lève une exception.

| Action | Paramètres | Effet sur l’état |
|---|---|---|
| `4`, `5` | `<cible>,<cellule>` | téléportation, glissement ; le combattant porté suit son porteur |
| `11` | `<cible>,<orientation>` | orientation |
| `50` | `<porté>` | l’acteur porte la cible, qui prend sa cellule |
| `51` | `<cellule>` | l’acteur lance le combattant porté : c’est le porté qui change de cellule. L’ancien code déplaçait le porteur. |
| `52` | `<porté>,<cellule>` | le porté est déposé |
| `100`, `108`, `110` | `<cible>,<variation>[,<élément>]` | PV |
| `101`, `102`, `111`, `120`, `168` | `<cible>,<variation>[,<tours>]` | PA ; `102` = PA utilisés, `GA;102;id;id,-0` libère le sort en attente |
| `78`, `127`, `128`, `129`, `169` | même format | PM ; `129` = PM utilisés |
| `103` | `<mort>` | hors combat ; le combattant porté reste sur la cellule ; retrait des effets de la cible et de ceux qu’elle a lancés |
| `132` | `<cible>` | retire tous les effets de la cible |
| `112`, `114` à `119`, `122` à `126`, `138`, `142`, `145`, `152` à `157`, `160` à `163`, `182`, `606` à `611` | `<cible>,<valeur>,<tours>` | caractéristique temporaire (`Effects`, source `GameAction`) |
| `149` | `<cible>,<apparence>,<nouvelle apparence>,<tours>` | changement d’apparence (effet) |
| `150` | `<cible>,<tours>` | invisible si tours > 0 (`IsInvisible`) |
| `950` | `<cible>,<état>,<1 ou 0>` | états (`GetStates`, `HasState`) ; un porté qui sort d’un état est déposé |
| `147`, `180`, `181`, `185`, `780` | entrée `GM` sans son préfixe | réinjectée comme `GM` (invocation, retour au combat) |
| `200` | `<cellule>,<image>` | image d’un objet interactif |
| `300`, `302` | `<sort>,…` | sort lancé ou échec critique : confirmation, limites par tour et intervalle pour le personnage du compte |
| `104` à `107`, `130`, `140`, `151`, `164`, `166`, `301`, `303` à `309` | selon l’action | journal seulement |
| `165`, `208`, `228`, `501` | – | animations, rien à mémoriser |
| `999` | paquet complet | exécuté comme un paquet du serveur (`GTL`, `GDZ`, `GDC`, `GIE`…) ; un `GA;999` imbriqué est refusé |

Le journal (`Journal`, 200 lignes) reprend en français les messages de combat du client. Les noms viennent de la carte, les sorts de `BotSorts`, sinon « le sort n° id ». Les numéros d’état et d’effet sont affichés tels quels. Le journal et le résultat restent consultables après la fin du combat, jusqu’au combat suivant.

Résultat : `GE<durée ms>[;<bonus d’étoiles>]|<initiateur>|<type>|<ligne>|<ligne>…` (matrice §2 n° 23, `Fight.getGE` de StarLoco) est lu dans `LastResult`, puis `CombatResultReceived` est levé avant `CombatFinished`. Le bonus d’étoiles n’existe qu’en combat contre des monstres. Chaque ligne commence par sa catégorie : `0` perdant, `2` gagnant, `5` percepteur, `6` butin commun.

- Type `0` (défi, monstres, percepteur) : `<cat>;<id>;<nom ou modèle>;<niveau>;<mort>;<xp min>;<xp>;<xp max>;<xp gagnée>;<xp guilde>;<xp monture>;<objets>;<kamas>`.
- Type `1` (agression, conquête) : `…;<mort>;<honneur min>;<honneur>;<honneur max>;<honneur gagné>;<grade>;<déshonneur>;<déshonneur gagné>;<objets>;<kamas>;<xp min>;<xp>;<xp max>;<xp gagnée>`.

Les objets s’écrivent `<modèle>~<quantité>` séparés par `,`. Une ligne illisible est rangée dans `Rejected` sans bloquer les autres. Un `GE` dont la durée est illisible termine quand même le combat, sans résultat.

Limites : ces formats proviennent des sources StarLoco du kit et du client 1.34. Ils ne sont vérifiés qu’avec des paquets fictifs (`BotFightProtocolSmoke`, `BotCombatSmoke`) et restent à rejouer sur un vrai StarLoco. Ne sont pas couverts : les challenges (`Gd…`), l’attaque à l’arme envoyée par le bot (`GA303<cellule>`), les actions `998` (jamais envoyée par StarLoco, format ambigu dans le client), `1039` et `1077` (envoyées par StarLoco, absentes du client, ignorées), `2`, `3`, `201` à `207`, `209` et `210` (absentes du client). Les caractéristiques temporaires ne modifient pas les statistiques affichées : seuls `GTM` et les actions de PA/PM le font. Le changement d’apparence n’est pas appliqué au sprite. Ce lot n’ajoute pas d’interface : options, drapeau, abandon et exclusion sont disponibles dans l’API de `Fights`.
