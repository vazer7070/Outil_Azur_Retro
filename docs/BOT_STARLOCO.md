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
- `HG → AT → ATK0 → Ak0/AV → AV0 → Agfr/AL/Af → ALK → AS → ASK → BYA/GC1`.
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

## Limites avant validation en jeu

Cette version améliore le client manuel et son diagnostic. Elle ne réalise pas encore les modes automatique/admin, les scripts Lua ou plugins annoncés dans le README. Les définitions complètes des interactifs, la récolte, les échanges avancés et les règles de combat non couvertes doivent encore être complétés et vérifiés. Les actions manuelles de combat sont en cours de validation ; les profils officiels et l’authentification via launcher ne sont pas validés par ces tests.

Les dialogues PNJ, les zaaps, la boutique PNJ et les actions d’inventaire ne sont vérifiés qu’avec des paquets fictifs reproduisant les formats du client 1.34 et des sources StarLoco du kit (`BotDialogsSmoke`, `BotShopSmoke`). Il reste à les rejouer sur un vrai StarLoco : ordre réel de `DCK` et `DQ`, effets des réponses de dialogue, `WC` d’un personnage sans zaap sauvegardé, prix et effets réels de `EL`, `OQ`/`OR` après une vente, conditions `OK` (jamais envoyées dans les sources du kit) et erreur `ERE` lorsqu’une fenêtre est déjà ouverte côté serveur. Les textes des questions et réponses ne sont pas exportés dans `BotNPCs` : seuls leurs numéros sont affichés. Les zaaps sont reconnus par `BotZaaps`, non par les interactifs de la carte dont les définitions ne sont pas chargées ; le gestionnaire de téléportation historique (`GA500…;157`, zaapi) n’a pas été modifié. La boutique n’offre ni artisanat, ni échange entre joueurs, ni hôtel de vente ; la réponse automatique `EV` à une demande d’échange `ERK` d’un autre joueur est conservée. Le clic droit « Échanger » n’est pas confirmé dans le code décompilé du client fourni : il reprend le paquet `ER0|<pnj>` que StarLoco attend pour ouvrir une boutique.

Il reste à connecter le bot à une copie isolée du vrai StarLoco, avec comptes et personnages de test, puis vérifier cartes, déplacements, créations/suppressions de personnages et réponse secrète, discussions, métiers, augmentations de caractéristiques et de sorts, interactions et combats. Les corrections d’Azur ne modifient pas les sources du kit ni les bases utilisateur. Les défauts internes de l’émulateur relevés dans [l’analyse des sources](STARLOCO_SOURCES_ANALYSE.md) restent distincts du client bot.

Pour la reprise de développement, consulter [le brief de refonte de l’interface](BRIEF_REDESIGN_UI_BOT.md). Il précise les références, les limites du rendu actuel et les vérifications visuelles à effectuer avant de considérer la refonte terminée. Les résultats automatisés et les livrables du 2 octobre 2026 sont consignés dans ce brief ; ils ne remplacent pas le parcours en jeu réel.

## Décor

Le décor du client est versionné dans `Outil_Azur_complet/Resources/Bot/Decor` (sols, objets, fonds et `ancres.tsv` ; voir sa [provenance](../Outil_Azur_complet/Resources/Bot/Decor/PROVENANCE.md)) et copié vers `ressources/maps` à la compilation. Il se régénère avec `tools/client-analysis/exporter_decor.py`.

`BotMapArtwork` place chaque PNG comme le client : position de la cellule (colonnes de 53 px, lignes tous les 13,5 px, 20 px par niveau au-dessus du niveau 7) plus l'ancre `(xmin, ymin)` du fichier ; le fond `BACK` est attaché à l'origine de la carte. Les règles d'affichage du client sont reprises : rien sur une cellule inactive, image n du sol sur une cellule de pente n et sans rotation, quart de tour du sol et de l'objet 1 avec la déformation 51,85 % × 192,86 % sur une cellule à plat, miroir horizontal. Les objets 1 sont dessinés avec les sols, sous la grille et les personnages ; les objets 2 s'intercalent avec les personnages selon la ligne de la cellule. Un PNG sans ancre (bibliothèque historique rangée en sous-dossiers, fichier remplacé dont la taille ne correspond plus) garde le placement approché.

Les PNG sont lus et transformés par une tâche de fond, jamais par le fil de l'interface, puis publiés en une fois : la carte apparaît d'abord en cellules à plat avec le statut « Chargement du décor… ». Les images sont partagées entre les cartes par un cache à compteur de références qui garde au plus 64 Mo d'images inutilisées. Un PNG absent ou illisible laisse la couleur à plat de la cellule et figure dans le statut, sans exception. `BotDecorAnchorsSmoke` vérifie ces règles sur des PNG, des ancres et des cartes synthétiques.

Limites : aucune image de sol en pente n'est encore exportée (il faut l'option `--frame` de `swfsvg`), les formes morphées et les textes statiques de 31 objets ne sont pas rendus, et l'ordre entre objets 2 et personnages suit la ligne de cellule plutôt que les profondeurs exactes du client.
