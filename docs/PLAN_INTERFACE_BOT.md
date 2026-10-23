# Plan de l’interface du bot

État au 12 octobre 2026. Ce document résume le chantier de l’interface de jeu du bot : la demande, les 33 lots du plan, le travail de départ qui les a précédés et les dix lots d’animations et de corrections qui les ont suivis, la couverture des fonctions du client et ce qui reste à faire. Le détail de chaque fonction (formats, règles, limites) est dans [le guide du bot](BOT_STARLOCO.md). Les formats des paquets du client sont relevés dans [la référence du protocole du client 1.34](PROTOCOLE_CLIENT_1_34.md). Des captures de l’interface sont dans [docs/captures](captures/README.md).

## Objectif

La demande était de donner au bot l’interface principale du client Dofus 1.34 et ses fonctions sur un serveur StarLoco : la carte avec les PNJ et les monstres, les interactions et les déplacements, les petits menus et le chat, puis les autres fonctions visibles dans le client et dans l’émulateur. Le bot doit ressembler au client 1.34 et se comporter comme lui face au serveur.

Règles suivies par tous les lots :

- le bot envoie ce que le client 1.34 envoie et lit ce que StarLoco envoie. Quand les deux divergent, le choix retenu est écrit dans le guide du bot ;
- un seul paquet par envoi ; l’état local ne change qu’à la réponse du serveur ;
- aucun décodage de carte, calcul de chemin, lecture d’image ou attente réseau sur le fil de l’interface ;
- les images et les textes du client sont exportés par les outils de `tools/client-analysis`, avec un `PROVENANCE.md` par famille. Les SWF du client et son code décompilé ne sont pas versionnés ;
- aucun compte, personnage, nom ou texte des dumps SQL dans le code ou les tests : les tests construisent des cartes, des acteurs et des textes synthétiques ;
- chaque lot a son test autonome `tests/<Nom>Smoke.cs`, joué contre un serveur fictif local (boucle locale), et met à jour le guide du bot.

**Limite commune à tout le chantier** : chaque fonction est vérifiée avec un serveur fictif local qui envoie des paquets aux formats du client 1.34 et des sources StarLoco. Seul `BotStarLocoLiveSmoke` rejoue une partie du jeu sur un vrai StarLoco local, avec deux comptes : connexion, création et entrée en jeu, même carte et déplacements vus par l’autre, changement de carte, chat général et privé, groupe, échange de kamas, ami, défi (placement, tours, déplacements, sort, dégâts, abandon, `GE`), combat contre un groupe de monstres, dialogue PNJ, achat en boutique et zaap.

Les priorités étaient : P0 données, P1 carte, déplacements et interactions, P2 chat et petits menus, P3 autres fonctions de l’émulateur. Le plan compte 33 lots ; le travail de départ (B0 ci-dessous), fait avant le plan, est décrit avec eux pour que la liste soit complète. Une relecture de fin de chantier a ensuite corrigé des défauts d’intégration et produit les captures.

## Les lots

### Lot de départ

**B0 — Fonctions de base d’après les paquets du client.** Premières fonctions du bot alignées sur le client 1.34 : volets Dialogue, Zaaps et Boutique, actions d’inventaire, clic sur un PNJ ou un zaap. Paquets : `DC`, `DCK`, `DQ`, `DR`, `DV` ; `GA500<cellule>;114`, `WC`, `WU`, `WV` ; `ER0|<pnj>`, `ECK0`, `EL`, `EB`, `ES`, `EV` ; `OM`, `OU`, `OD`. Tests : `BotDialogsSmoke`, `BotShopSmoke`.

### P0 — Données

**D0 — `swfsvg` : scène, images d’animation et index.** L’outil Rust rend la scène des SWF sans symbole exporté, une image choisie d’une animation (`--frame`), la liste des symboles (`--list`) et le cadre de chaque rendu dans `index.tsv`. Les versions 0.2.1 et 0.2.2 corrigent les dégradés, les bitmaps et les masques. Aucun paquet. Test : `cargo test` de `swfsvg` (`Run-Tests.ps1 -Outils`).

**D1 — Décor des cartes.** `exporter_decor.py` exporte les sols, les objets et les fonds du client avec leurs ancres (`ancres.tsv`). `BotMapArtwork` pose chaque image comme le client et lit les PNG hors du fil de l’interface. Aucun paquet. Test : `BotDecorAnchorsSmoke`.

**D2 — Sprites d’acteurs.** `exporter_sprites.py` exporte les poses de repos par orientation, les bandes de marche et de course des 24 gfx de classes, les images des épées de combat et les ancres. Aucun paquet. Tests : `BotSpriteSheetsSmoke`, `test_exporter_sprites.py`.

**D3 — Icônes du client.** `exporter_icons.py` exporte les icônes d’objets, les illustrations des PNJ et monstres, les smileys, les émotes, les métiers, les alignements, les emblèmes, la carte du monde et des éléments de `core.swf`. `ClientAssets` les lit avec un cache borné, hors du fil de l’interface. Aucun paquet. Tests : `BotClientIconsSmoke`, `test_exporter_icons.py`.

**D4 — Textes du client.** `lang2xml.py` convertit les fichiers de langue du client en 28 XML ; `LangData` donne les textes des dialogues, PNJ, cartes, monstres, objets, sorts, émotes, métiers, titres, quêtes et messages. Paquets : textes des messages `Im`. Tests : `BotLangDataSmoke`, `test_lang2xml.py`.

**D5 — Exports du serveur.** Le gestionnaire de ressources exporte les objets interactifs, les cellules déclencheurs, les zaapis, les panoplies et le fond des cartes ; le bot les charge à la préparation. Aucun paquet. Test : `BotServerExportsSmoke`.

### P1 — Socle

**S1 — Session réseau.** Un gestionnaire par préfixe, un paquet par envoi, `GC1` seul après `ASK`, lecture de `GCK`, `AR`, `Ac`, `BT`, `BN`, `AN`, messages `Im` génériques, invitations `PIK` et `gJr` jamais acceptées seules. Test : `BotSessionSmoke`.

**S2 — Modèle d’acteurs.** Un type d’acteur par type de `GM` (joueur, monstre, groupe, PNJ, marchand, percepteur, monture d’enclos, prisme) ; actions `GA` hors combat par `GameActionRouter`. Paquets : `GM`, `GA;0`, `GA;1`, `GA;2`, `GA;4`, `GDF`, `GDO`, `GDC`, `eD`, `Oa`, `Gc`. Test : `BotActorsModelSmoke`.

**S3 — Socle de l’interface.** Tiroir de volets (`IGamePanel`, `PanelHost`), menus contextuels des acteurs (`IActorMenuProvider`), routeur des clics de la carte, bandeau bas (`HudPanel`) et boîtes de dialogue non bloquantes. Paquet : sortie `DV` à la fermeture d’un volet de fenêtre serveur. Test : `BotPanelsSmoke`.

### P1 — Carte

**M1 — Rendu des acteurs.** Pose et orientation, profondeur, marche animée, membres des groupes de monstres, survol au pixel avec surtête, bulles de chat, smileys et émotes au-dessus des têtes, aperçu du chemin. Paquets lus : `GM`, `GA;1`, `cMK`, `cS`, `eUK`. Test : `BotActorRenderSmoke`.

**M2 — Déplacements.** Chemin calculé comme le client hors du fil de l’interface, envoi `GA001` compressé, un seul `GKK` après la durée d’animation, `GKE` pour interrompre une marche. Test : `BotMovementSmoke`.

**M3 — Objets interactifs.** Menu des compétences d’un objet, récolte manuelle, image de l’objet pendant la récolte, zaap reconnu par son gfx, zaapis, codes de coffre, documents et enclos. Paquets : `GA500<cellule>;<compétence>`, action `501` et `GKK`, `IQ`, `IO`, `Wc`, `Wu`, `Wv`, `KCK`, `KK`, `KKE`, `KV`, `dCK`, `dV`. Test : `BotInteractivesSmoke`.

**M4 — Actions sur la carte.** Menu d’un autre joueur, duels, agressions, attaque d’un groupe de monstres, épées de combat, liste des combats de la carte. Paquets : `BW`, `PI`, `ER1`, `GA900` à `GA906`, `GA909`, `GA912`, `GA903<combat>`, `fC`, `fL`, `fD`, `BWK`, `BWE`. Test : `BotMapActionsSmoke`.

**M5 — Dialogue avec les PNJ.** Textes des questions et réponses, nom et illustration du PNJ, menu des actions que le client déclare pour chaque modèle. Paquets : `DC`, `DCK`, `DQ`, `DP`, `DR`, `DV`, `ER0`, `ER2`, `ER9`, `ER10`, `ER11`, `ER17`. Test : `BotNpcDialogTextsSmoke`.

### P2 — Chat et petits menus

**C1 — Protocole du chat.** `ChatService` : messages, canaux, chuchotements, smileys, émotes, messages du serveur et commandes de console. Paquets : `BM`, `cMK`, `cMEf`, `cC±`, `BS`, `cS`, `eU`, `eUK`, `eL`, `eA`, `eR`, `cs`, `M1`. Test : `BotChatProtocolSmoke`.

**C2 — Volet de discussion.** Filtres des canaux, saisie, historique, complétion des noms, menu des canaux, liens sur les noms et les coordonnées, smileys et attitudes, agrandissement du chat. Les paquets passent par `ChatService`. Test : `BotChatUiSmoke`.

**C3 — Bandeau, options et raccourcis.** Boutons ronds des volets, cœur, PA et PM, jauge ronde, illustration (portrait, horloge, boussole), barre de raccourcis des sorts et objets, menu principal, clic droit global, fenêtre d’options et raccourcis du client. Paquets : `SM`, `OU`, `OM<objet>|<case + 34>|<quantité>`, `Rr`, `fL`. Test : `BotBannerSmoke`.

### P3 — Autres fonctions de l’émulateur

**F1 — Groupe.** Invitations, membres, chef, suivi, localisation et boussole ; volet Groupe. Paquets : `PI`, `PIK`, `PIE`, `PCK`, `PL`, `PM`, `PV`, `PR`, `PA`, `PF±`, `PG±`, `PW`, `IC`, `IH`. Test : `BotPartySmoke`.

**F2 — Amis, ennemis et conjoint.** Listes, ajout, retrait, avertissement de connexion, conjoint ; volet Amis. Paquets : `FL`, `FA`, `FD`, `FS`, `FO±`, `FJS`, `FJC±`, `iL`, `iA`, `iD`. Test : `BotFriendsSmoke`.

**F3 — Guilde et percepteurs.** Volet Guilde à cinq onglets (membres, personnalisation, percepteurs, enclos, maisons), invitations, droits, création, percepteurs (échange de type 8). Paquets : `gS`, `gIG`, `gIM`, `gIB`, `gIT`, `gIF`, `gIH`, `gJR`, `gJr`, `gJK`, `gJE`, `gP`, `gK`, `ER8`. Test : `BotGuildSmoke`.

**F4 — Échanges, coffre et banque.** Registre des fenêtres par type d’échange, échange entre joueurs et avec un PNJ, coffre et banque ; volets Échange et Coffre. Paquets : `ECK<type>`, `ER1`, `ER2`, `ERK`, `ERE`, `EA`, `EMO±`, `EMG`, `EK`, `EV`, `EVa`, `EL`, `EMK`, `EmK`, `EsK`. Test : `BotExchangeSmoke`.

**F5 — Hôtel de vente.** Achat et vente (types 10 et 11) ; volets d’achat et de vente. Paquets : `ER10`, `ER11`, `ECK10`, `ECK11`, `EHT`, `EHL`, `EHP`, `EHl`, `EHM`. Test : `BotAuctionSmoke`.

**F6 — Métiers et artisanat.** Volets Métiers, Atelier (type 3) et Livre des artisans (type 14), options d’artisan, recettes connues. Paquets : `JS`, `JX`, `JN`, `JO`, `ECK3`, `EcK`, `EcE`, `EW`. Test : `BotCraftSmoke`.

**F7 — Montures, étable et enclos.** Volets Monture (monture équipée, sacoches, fiche) et Enclos (étable, vente). Paquets : `Re±`, `Rr`, `Rn`, `Rx`, `Rd`, `Rp`, `ER15`, `ECK15`, `ECK16`, `Ew`. Test : `BotMountSmoke`.

**F8 — Maisons, coffres et mode marchand.** Menu des portes et des coffres, vente et achat de maison, menu intérieur, marchand hors ligne (type 4) et magasin (type 6). Paquets : `hP`, `hL`, `hCK`, `hB`, `hV`, `GA507`, `ER4`, `ER6|`, `Eq`. Test : `BotHouseMerchantSmoke`.

**F9 — Alignement, prismes et conquête.** Volet Conquête (statistiques, zones, défense), ailes, prismes et téléportation par prisme. Paquets : `GP+`, `GP*`, `GP-`, `GIP`, `Cb`, `CB`, `CWJ`, `CWV`, `CW`, `CIJ`, `GA512`, `Wp`, `Ww`. Test : `BotAlignmentSmoke`.

**F10 — Quêtes et titres.** Volet Quêtes (liste, étapes, objectifs, récompenses) et titre porté sous le nom. Paquets : `QL`, `QS`, `Im054` à `Im056`, titre lu dans `GM`. Test : `BotQuestsSmoke`.

**F11 — Carte du monde.** Tuiles, zoom, repère de la position, indices, drapeaux du groupe et prismes. Paquets : `CWJ`, `CWV`, `IC`. Test : `BotWorldMapSmoke`.

**F12a — Protocole de combat.** Ordre des tours, effets, zones, options d’équipe, drapeau, abandon et résultat ; table des actions `GA` en combat. Paquets : `GTL`, `GTR`/`GT`, `GTS`, `GTF`, `GIE`, `GDZ`, `Go`, `Gf`, `GQ`, `GV`, `GE`, `Gp`, `GR`, `Gt`, `GA001`, `GA300`. Tests : `BotFightProtocolSmoke`, `BotCombatSmoke`.

**F12b — Interface de combat.** Ligne de temps, options de combat, menu de placement, abandon, drapeau et volet de résultat. Paquets : `fN`, `fP`, `fH`, `fS`, `GR1`, `GR0`, `GQ`, `Gf`. Tests : `BotFightUiSmoke` ; `BotCombatUiSmoke` sous Windows.

**F13a — Fiche de caractéristiques et sorts.** Lecture complète de `As`, boutons « + » des caractéristiques, volet Sorts avec fiche détaillée, amélioration et oubli. Paquets : `As`, `AB<code>`, `SL`, `SLo`, `SB`, `SUK`, `SUE`, `SF`, `SM`. Tests : `BotStatsSheetSmoke`, `BotSpellsSmoke`.

**F13b — Inventaire en grille.** Grille et icônes, plateau d’équipement, fiche d’objet, panoplies, poids, destruction. Paquets : `OAK`, `OM`, `OU`, `OD`, `Od`, `OS±`, `Ow`, `OQ`, `OR`. Tests : `BotInventoryGridSmoke`, `BotShopSmoke`.

**F14 — Commandes du serveur, absent et invisible, pierres d’âme.** Volet d’aide des commandes joueur de StarLoco, `/away` et `/invisible`, ligne de capture au survol d’un groupe, pierre d’âme pleine utilisable en arène, livres. Paquets : `BM*|.<commande>|`, `BYA`, `BYI`, `OU`, `dCK`. Test : `BotServerCommandsSmoke`.

## Relecture de fin de chantier

Après la fusion des 33 lots du plan, une relecture a corrigé les défauts d’intégration suivants (vérifiés par les tests existants et par les captures) :

- le magasin envoie `ER6|` comme le client, car StarLoco expulse le joueur sur `ER6` seul ;
- le tiroir ne reconstruit plus le volet affiché chaque seconde ; la fiche d’un membre de guilde n’est réécrite qu’à un changement ; le volet des combats a sa propre minuterie ; les demandes en attente de l’hôtel de vente, des maisons, de la boutique et du marchand sont notifiées ;
- l’hôtel de vente libère un achat en attente quand le serveur le refuse par un simple message `cs` ;
- le rendu des acteurs lit une copie locale des cellules de la carte ;
- la visée du drapeau est désarmée à la fin du combat et au changement de carte ;
- les touches + et − du pavé numérique agrandissent et réduisent le chat ;
- le décor est réexporté avec `swfsvg` 0.2.2, qui applique les masques des symboles (1 433 PNG remplacés) ;
- défauts visibles corrigés : la barre du haut passe à « En jeu » après la première carte, icône des kamas séparée du montant, noms d’objets sans espaces de bord, filtre et icône de l’atelier, niveau des monstres tiré de leur grade, séparateur des milliers des caractéristiques, noms de personnages trop longs réduits, bouton « Jouer » sans rectangle gris, croix de fermeture des quêtes, étiquettes des raccourcis, bande du menu de placement, bouton principal désactivé sans orange, cellules de placement rouges et bleues des deux équipes.

L’outil `tools/captures` produit 18 captures de la fenêtre de jeu sous Mono et Xvfb, contre un serveur fictif local, et `BotStarLocoLiveSmoke` deux captures `19-reel-*` contre un vrai StarLoco local : [docs/captures](captures/README.md).

## Validation sur un vrai StarLoco, animations et corrections

Après la relecture, le bot a été joué sur une copie locale du StarLoco du kit (`BotStarLocoLiveSmoke`, deux comptes à la fois ; douze scénarios réussis le 12 octobre 2026, voir [le guide](BOT_STARLOCO.md#limites-avant-validation-en-jeu)), puis complété par dix lots qui lui donnent les animations du client. Mêmes règles que les lots du plan : images exportées du client par `swfsvg` et les exporteurs de `tools/client-analysis`, provenance notée, budget de 150 Mo pour l’ensemble des bandes, un test Mono par lot, et un passage sur le vrai serveur après chaque fusion.

**AN1 — Socle des animations.** Événements visuels tirés du protocole (`FightVisuals`), file d’animations par acteur, séquenceur des actions de combat, effets de carte, horloge injectable ; cadence et fin de chaque bande dans `ancres.tsv`. Tests : `BotAnimationQueueSmoke`, `BotSpriteSheetsSmoke`.

**AN2 — Chiffres, coups reçus et morts.** Chiffres au-dessus des têtes, bandes `hit` et `die` des 24 classes ; `swfsvg` 0.2.3 et `exporter_sprites.py --anims`. Test : `BotPointsHitDeathSmoke`.

**AN3 — Bandes des monstres.** `walk`, `run`, `hit`, `die` et `anim0` des 100 gfx de monstres les plus présents sur les cartes de StarLoco (`choisir_gfx_animes.py`). Test : `BotSpriteSheetsSmoke`.

**AN4 — Effets de sorts.** Scènes des sorts (`GA300`), attaques à l’arme (`GA303`), coups et échecs critiques, récolte et effets de carte ; `swfsvg` 0.2.4 et `exporter_sorts.py`. Test : `BotSpellEffectsSmoke`.

**AN5 — Projectiles des sorts.** Types 20 à 51 de `GA300`, à 40 images par seconde. Test : `BotSpellProjectilesSmoke`.

**AN6 — Recoloration des sprites.** Les trois couleurs du `GM` appliquées par masques aux zones des sprites ; `swfsvg` 0.2.5. Test : `BotRecolorSmoke`.

**AN7 — Accessoires des personnages.** Coiffe, cape, arme, bouclier, familier et objet vivant posés aux emplacements que chaque classe réserve ; `swfsvg` 0.2.6 et `exporter_accessoires.py`. Test : `BotAccessoriesSmoke`.

**AN8 — Émotes et clips annexes.** Émotes jouées en bande, auras, cercle d’équipe, sac du marchand, clip des PNJ, ailes d’alignement et bonus PvP ; option Aura. Test : `BotExtraClipsSmoke`.

**AN9 — Son (évaluation).** Mesure de ce que le client fait entendre et de ce que coûterait sa lecture ; aucun son n’est joué. Note : [SON_BOT.md](SON_BOT.md).

**CORR — Corrections relevées sur le vrai StarLoco.** Ticket renvoyé après `ATE`, carte vidée à `GJK` en gardant les états des cellules, barre d’état des PA et PM, test réel durci. Tests : `BotTransportSmoke`, `BotFightProtocolSmoke`, `BotFightUiSmoke`.

**X1 — Corps à corps** (après ces dix lots). `GA303<cellule>` comme le client : case `²` de la barre (arme en position 1 ou poing `DefaultCC`), raccourci `SH0`, clic sur un ennemi adjacent sans sort choisi (ajout du bot) ; PA et portée lus dans `items.xml` (arme) ou `classes.xml` (poings), `GKK` sur `GAF`. Tests : `BotFightProtocolSmoke`, `BotFightUiSmoke`, scénario h de `BotStarLocoLiveSmoke`.

**X2 — Tombe, fantôme et menu de son personnage.** Tombe et fantôme lus dans le sprite du `GM`, menu de son propre personnage (« Se frapper » `BM*`, « Libérer mon âme » `GF`, « Changer de direction » et son sélecteur `eD`), boîte du mariage (`GA;617` → `GA618` / `GA619`), mode héroïque (`ALK` des morts, `AR<id>`, boîte de fin de partie sur `GO`). Tests : `BotSessionSmoke`, `BotMapActionsSmoke` ; scénarios x2a à x2c de `BotStarLocoLiveSmoke` (orientation vue par l’autre compte, tombe ramenée à la vie par la statue du phénix, mariage).

**X3 — Connexion : pseudo, perte de connexion, nouveau niveau, messages système.** Fenêtre du pseudo d’un compte neuf (`AlEr` → saisie, confirmation, ligne brute → `AlEs` ou reprise par `Ad`/`AlK`), cause de chaque déconnexion, boîte `CONNECTION` sur une perte de connexion (texte `DISCONNECT`, le dernier `M0<id>` reçu, `SRV_MSG_<id>`, puis la question `ATTEMPT_RECONNECT` en Oui / Non, `SIMPLE_AUTO_RECONNECT`) : « Oui » reconnecte le compte, « Non » rouvre l’écran de connexion prérempli, boîte `NEW_LEVEL` sur `AN`. `AlEr`/`AlEs` en jeu sont laissés au lot X32. Tests : `BotHandshakeSmoke`, `BotSessionSmoke`, `BotConnectionUiSmoke` ; scénarios x3a à x3c de `BotStarLocoLiveSmoke`.

**X4 — Familiers et montures : nourrir, confier, ressusciter.** Ressource déposée sur le familier porté (`OM<objet>|8`) ou sur la monture annoncée par `Re+` (boîte de quantité du client, `OM<objet>|16|<quantité>`), comme `enabledFromSuperType` ; échange de familiers de type 9 (`ER9` garde, `ER17` résurrection, `ECK9` dans les deux cas) et volet **Familiers**. Tests : `BotInventoryGridSmoke`, `BotExchangeSmoke` ; scénarios x4a à x4c de `BotStarLocoLiveSmoke` (repas, garde chez un gardien d’action 4, résurrection chez un PNJ d’action 7).

**X32 — Session : changement de nom en jeu et roulette du Bouftou.** `AlEr`/`AlEs` reçus en jeu (potion 10860) aiguillés vers le nom du personnage (`NameChange`, socle `NicknamePrompt` partagé avec le pseudo du lot X3) : fenêtre `ChooseNickName` au-dessus de la fenêtre de jeu, ligne brute, `AlEs` = nouvel essai, fermeture par le `GM|~` du personnage au nouveau nom, jamais de déconnexion ; mini-jeu `TC<id>|<clé>` → `TV<rang>|<cellule>|<orientation>` envoyé aussitôt (0 pour le tutoriel 30, 1 pour le 31, 0 pour un inconnu) et boîte « mini-jeu ». Tests : `BotSessionSmoke`, `BotConnectionUiSmoke`, `BotHandshakeSmoke` ; scénarios x32a et x32b de `BotStarLocoLiveSmoke`.

**X5 — Artisanat sécurisé (a) : protocole et fenêtre.** Fenêtre `CraftSecureExchange` des types 12 (artisan) et 13 (client) : `ECK12` / `ECK13|<cases>;<compétence>`, ingrédients des deux côtés (`EMO±`, `EMKO±`, `EmKO±`), paiement et prime du client (`EP<1|2>G`, `EP<1|2>O±` → `Ep<type>;…`), `EK` avec le délai du client, `EcK;<modèle>;T / B<nom>;<effets>`, `EcEF`, `EcEI`, `EV` ; demandes `ERK` de types 12 et 13 posées dans la boîte de l'échange avec les textes du client ; volet **Artisanat sécurisé**. Les entrées de menu `ER12` / `ER13` et `ERET` restent au lot X6. Tests : `BotCraftSmoke`, `BotExchangeSmoke`, scénario x5 de `BotStarLocoLiveSmoke`.

**X6 — Artisanat sécurisé (b) : demandes et menus.** Compétences annoncées par `EW+<id>|<compétences>` (à l'arrivée sur la carte et à chaque changement d'outil, oubliées à chaque `GDM`) ; entrées « Demander à <compétence> » (`ER13|<artisan>|<compétence>`) et « Inviter à <compétence> » (`ER12|<client>|<compétence>`) à la fin du menu d'un joueur ; chaque `ERE` (dont `ERET`, `NOT_NEAR_CRAFT_TABLE`) écrit dans le chat ; clic gauche sur un joueur → son menu (Ctrl : un sous-menu par joueur de la cellule), sur un marchand, un percepteur ou un prisme → menu au clic simple et action par défaut au Maj + clic ; carte de l'artisan du livre (« Message privé » → « /w <nom> », « Localiser » → boussole du bandeau). Tests : `BotCraftSmoke`, `BotMapActionsSmoke` ; scénarios x6 et x6b de `BotStarLocoLiveSmoke`.

**X7 — Combat : événements visibles (chat, zones, cellules, prêt, invisibilité).** Journal de combat dans le filtre Infos du chat selon l'option `ChatEffects` ; zones `GDZ` dessinées au sol (`ZONE_COLOR`, 30 %) ; cellules `GDC` redécodées et redessinées d'après les bits du masque (`Map.CellDataOf`), restaurées par `GDC<cellule>` ; clip `ready.swf` au-dessus de chaque combattant prêt (`GR1<id>`), retiré au premier tour ; icônes `Go` au-dessus de l'épée de l'équipe ou de son chef ; combattant invisible (`GA;150`) caché chez les autres et à 40 % pour soi. Tests : `BotFightUiSmoke`, `BotMapViewSmoke`, `BotChatUiSmoke`, scénario x7 de `BotStarLocoLiveSmoke` (Sram, pièges, invisibilité).

**X12 — Inventaire : boîte de quantité, glisser-déposer, tris.** Boîte de quantité du client (`PopupQuantity`) pour jeter, détruire, déplacer entre le sac et un coffre ou des sacoches, proposer et les kamas, à la place des champs fixes ; option `ConfirmDropItem` (`CONFIRM_DROP_ITEM`) ; objets glissés du sac vers les volets Échange, Coffre, Sacoches et Atelier et de leurs cases vers le sac (`EMO+` / `EMO-` avec la quantité choisie, retrait borné aux pods libres, `SRV_MSG_6`) ; six tris du bouton « plus de choix » ; cases de sort voilées selon la règle du client (V-26 réfuté : `NOT_IN_FIGHT` hors combat, délai, lancers et PA en combat). Tests : `BotInventoryGridSmoke`, `BotShopSmoke`, `BotExchangeSmoke`, `BotCraftSmoke`, `BotMountSmoke`, `BotBannerSmoke`, `BotFightUiSmoke` ; scénario x12 de `BotStarLocoLiveSmoke`.

**X8 — Combat : visée et chemin.** Portée du client (`checkRange` : max(min, max + bonus) pour un sort modifiable, bonus = PO de `As` et effets `GA` de caractéristique 19, `117` et `116`, retirés au tour suivant de leur porteur) dessinée en `CELL_SPELL_RANGE_COLOR` (#246696, 30 %, cercle ou croix) ; sur une cible valable, une forme par zone d'effet sous le pointeur (`P`, `C`, `D`, `X`, `L`, `T`, `O`, zone du type d'arme au corps à corps) en `CELL_SPELL_EFFECT_COLOR` à 30 % ; flèche du système gardée et image du client à côté (icône du sort, de l'arme ou `DefaultCC`, croix `Forbidden` hors des cibles, flèches `MapNavigatorMoveCursor` pendant le glisser) ; pendant son tour, chemin de combat du client survolé (quatre directions, au plus PM cases) dessiné avec les formes de sélection `cellules/s<pente>.png` teintes en #FF6600 et envoyé tel quel par le clic (`GA001`). Écart avec StarLoco (portée min + 1) décrit dans le guide. Tests : `BotCombatSmoke`, `BotMapViewSmoke`, `BotFightUiSmoke`, `BotFightProtocolSmoke`, `BotEntitiesSmoke`, `BotWorldMapSmoke`, scénario x8 de `BotStarLocoLiveSmoke`.

**X13 — Objets vivants et objets à cible (V-19, m-04).** Ce lot couvre :

- l'objet vivant lu dans les effets 970 à 974 (`LivingItem`) ;
- la section `LivingItemsViewer` de la fiche (humeur, niveau, expérience, apparence) ;
- sous la fiche et dans le menu de l'objet : Nourrir (grille des aliments du même type, question `CONFIRM_FOOD_LIVING_ITEM`, `Of<objet>|<position>|<aliment>`), Apparence (une case par niveau, `Os<objet>|<position>|<apparence>`) et Dissocier (`Ox<objet>|<position>`), avec `OCO`, `OAKO` et `Oa` relus ;
- « Cibler avec cet objet » (`CLICK_TO_TARGET`) : le volet se ferme et la carte passe en visée d'objet comme `switchToItemTarget` (icône à côté de la flèche, pointeur d'une cellule, Échap). Le clic envoie `OU<objet>|<sprite>|<cellule>`, ou `OU<objet>||<cellule>` sur une case vide.

Écart de StarLoco : 971 et 973 sont écrits en décimal. Il est décrit dans le guide. Tests : `BotInventoryGridSmoke` ; scénario x13 de `BotStarLocoLiveSmoke`.

**X33 — Combat : ligne de vue locale et mode tactique.** Ligne de vue du client (`checkView` : hauteurs des cellules et des combattants, cellules sans ligne de vue ou inactives, combattants entre le lanceur et la cible, `GDC` compris) vérifiée avant l'envoi d'un sort, avec les états requis et interdits du lanceur (index 16 et 17 du niveau), la cellule libre (`freeCell`), les lancers par cible (compteur de `addLaunchedSpell`) et les bonus `SB` 289 et 291 ; option `AdvancedLineOfSight` (« Mettre en valeur les cases ciblables », vraie par défaut) qui pose sur chaque cible valable la forme de sélection en #0066CC à 50 % (`drawAllowedZone`) ; bouton du mode tactique actif au placement, en combat et seul pour un spectateur, sans paquet : aplat #29261F à la place des fonds 631 et 632 absents, sols retirés, objets 10000, 10001, 10002 et 1030 du décor exporté (`turnTactic`), choix gardé d'un combat à l'autre, second clic pour le décor normal. Écarts avec StarLoco (ligne de vue des joueurs et états non vérifiés par le serveur) décrits dans le guide. Tests : `BotCombatSmoke`, `BotMapViewSmoke`, `BotFightUiSmoke`, scénario x33 de `BotStarLocoLiveSmoke`.

**X9 — Combat : défis (V-01).** Défis d'un combat contre des monstres lus comme le client : `Gd<type>;<1|0>;<cible>;<xp>;<xp de groupe>;<butin>;<butin de groupe>`, aiguillage de `GdOK` / `GdKO` sur le quatrième caractère, huitième champ de StarLoco ignoré, nom, description et icône de `fightChallenge` (`%1` = « cible (Niv. n) »). Dans la fenêtre de jeu : colonne `UI_FightChallenge` (icônes exportées de 38 × 38, infobulle, bouton affiche / cache, symboles `ChallengeOK` / `ChallengeKO`) ; détail `FightChallengeViewer` (bonus de base et de groupe, état) ; « Voir la cible » → `Gdi<type>` au plus une fois par seconde, réponse `Gf` dessinée en drapeau ; lignes `FIGHT_CHALLENGE_DONE` / `FIGHT_CHALLENGE_FAILED` dans le filtre Infos du chat ; rangée « Bonus de challenges » du résultat (icônes de 17 × 17). Comportements de StarLoco (tirage, `Gf` avant `Gd`, `GdOK` même en défaite, `GdOK` / `GdKO` après `GQ` seulement pour le dernier de l'équipe) décrits dans le guide. Tests : `BotFightProtocolSmoke`, `BotFightUiSmoke`, scénario x9 de `BotStarLocoLiveSmoke`.

**X14 — Social (a) : téléportation de guilde, colonne du groupe, Entrée du chat (V-27, V-28, V-29, m-37).** Boutons « Rejoindre » des onglets Maisons (`gh<maison>`, seulement pour une maison qui partage le droit 128) et Enclos (`gf<carte>`), refusés sur place hors des listes `gIH` / `gIF` (StarLoco lèverait une exception pour un `gf` vers une carte sans enclos) ; colonne du groupe `UI_Party` posée sur la carte, au bord droit comme dans le client, de `PCK` à `PV` : portraits `staticR` recolorés par les couleurs de `PM`, vie, chef, suivi, menu du membre au clic, bouton Afficher / Cacher ; Entrée, Maj + Entrée et Ctrl + Entrée hors saisie donnent le focus à la zone de saisie du chat (`setChatFocus`), Échap le rend à la carte. La boussole n'est pas remise sur une cible précédente : le client n'en garde pas. Tests : `BotGuildSmoke`, `BotChatUiSmoke`, `BotPartySmoke`, scénario x14 de `BotStarLocoLiveSmoke`.

## Couverture des fonctions

Légende de la colonne « Bot » : **fait** (fonction utilisable, vérifiée par le test cité contre un serveur fictif local), **partiel** (une partie seulement), **absent**. Les fonctions rejouées par `BotStarLocoLiveSmoke` sont aussi vérifiées sur un vrai StarLoco local ; les autres lignes ne le sont pas encore. La colonne « Client 1.34 » nomme la fenêtre ou les paquets du client ; la colonne « StarLoco » dit ce que le serveur du kit en fait.

### Connexion et session

| Fonction | Client 1.34 | StarLoco | Bot | Test |
|---|---|---|---|---|
| Connexion Login puis Game | `HC`, version, compte, `AX`, `AYK` | traité | fait | `BotTransportSmoke`, `BotSessionSmoke`, `BotHandshakeSmoke` (Windows) |
| Choix du serveur et du personnage | `AH`, `AxK`, `AX`, `ALK`, `AS` | traité | fait | `BotSessionSmoke`, `BotHandshakeSmoke` (Windows) |
| Création et suppression d’un personnage | `AA`, `AD` | traité ; réponse secrète exigée à partir du niveau 20 | partiel : les envois ne sont pas vérifiés par un test | `BotUiSmoke` (Windows, écran de création) |
| Paramètres de connexion | — | — | fait | `BotConfigSmoke` |
| Messages du serveur | `Im`, `cs`, `M1` | traité | fait | `BotSessionSmoke`, `BotLangDataSmoke` |
| Pseudo d’un compte neuf | `AlEr`, ligne brute, `AlEs`, fenêtre `ChooseNickName` | `WAIT_NICKNAME` : toute ligne est le pseudo ; caractère interdit : `AlEs` puis pseudo retenu quand même | fait (en jeu : ligne « Changement de nom en jeu », lot X32) | `BotHandshakeSmoke` (Windows), `BotConnectionUiSmoke`, x3a |
| Messages système et perte de connexion | `M0<id>` gardé, boîte `CONNECTION` à `Aks.onClose` en Oui / Non (`ATTEMPT_RECONNECT`), reconnexion ou retour à la connexion | `M04` à l’arrêt du Game, `M018` à une expulsion, perdus tous deux par la fermeture immédiate de la session | fait | `BotSessionSmoke`, `BotConnectionUiSmoke`, x3c |
| Nouveau niveau | `AN`, boîte `NEW_LEVEL` | `AN` envoyé deux fois par gain | fait ; sans l’astuce `TIP_GAIN_LEVEL` | `BotSessionSmoke`, `BotConnectionUiSmoke`, x3b |
| Changement de nom en jeu (potion 10860) | `AlEr` en jeu → `ChooseNickName`, ligne brute, `AlEs` = refus | `setChangeName` puis `AlEr` ; `AlEr` renvoyé même après la réussite, `GM\|~` au nouveau nom, potion retirée ; `isChangeName` jamais remis à faux | fait ; « Annuler » ferme sans déconnecter ; refus local d'un nom que `parsePacket` prendrait pour un paquet | `BotSessionSmoke`, `BotConnectionUiSmoke`, `BotHandshakeSmoke`, x32a |
| Mini-jeu de la roulette du Bouftou | `TC<id>\|<clé>` → tutoriel joué → `TV<rang>\|<cellule>\|<orientation>` | seul `TC` envoyé : PNJ 408, action 36 ; joueur « absent » et `IN_TUTORIAL` jusqu'au `TV` | fait sans l'animation : `TV` envoyé dès `TC` avec le rang du fichier (0 ou 1), boîte « mini-jeu » | `BotSessionSmoke`, `BotConnectionUiSmoke`, x32b |
| Mode héroïque : morts, réinitialisation, fin de partie | `ALK` (champs 10 et 11), `AR<id>`, `GO` | `HEROIC` faux par défaut ; `AR<id>` ranime sans vérifier la mort | partiel : non rejoué sur le vrai serveur ; écran `GameOver` du client non exporté (boîte à la place) | `BotSessionSmoke`, `BotMapActionsSmoke` |

### A. Carte, décor et acteurs

| # | Fonction | Client 1.34 | StarLoco | Bot | Test |
|---|---|---|---|---|---|
| A1 | Décor (sols, objets, fond, pentes) | SWF de carte après `GDM` | envoie `GDM` | partiel : pas d’images de pente, 31 objets incomplets | `BotDecorAnchorsSmoke`, `BotMapViewSmoke` |
| A2 | Changer de carte | marche vers une cellule de sortie | téléporte à l’arrivée (`GA;2`, `GDM`) | partiel : déclencheurs chargés mais non affichés | `BotActorsModelSmoke`, `BotMovementSmoke` |
| A3 | Voir les joueurs | `GM` type ≥ 0 | traité | partiel : couleurs du `GM` (lot AN6), accessoires (coiffe, cape, familier, bouclier, arme ; lot AN7) appliqués aux 24 classes, ailes dans la surtête et auras 1 et 2 (lot AN8) | `BotActorRenderSmoke`, `BotSpriteSheetsSmoke`, `BotRecolorSmoke`, `BotAccessoriesSmoke`, `BotExtraClipsSmoke` |
| A4 | Voir les PNJ et leur nom | `GM` type -4 | traité | fait | `BotActorRenderSmoke`, `BotActorsModelSmoke` |
| A5 | Groupes de monstres, niveaux, étoiles | `GM` type -3 | traité | fait | `BotActorRenderSmoke` |
| A6 | Marchands, percepteurs, prismes, montures d’enclos, épées | `GM` types -5, -6, -9, -10 ; `Gc` | traité | partiel : pas d’icône de sac, équipes des épées non suivies | `BotActorsModelSmoke`, `BotActorRenderSmoke` |
| A7 | Survoler, sélectionner un acteur | aucun paquet | — | fait | `BotActorRenderSmoke` |
| A8 | Se déplacer | `GA001`, `GKK`, `GKE` | traité | fait | `BotMovementSmoke`, `BotGameplaySmoke` |
| A9 | Orientation et marche animée | `eD`, `GA;1` | traité | partiel : bandes de marche pour les 24 classes seulement | `BotActorRenderSmoke`, `BotMovementSmoke` |
| A10 | Objets interactifs, objets au sol, portes | `GDF`, `GDO`, `GDC` | traité | partiel : images 1 et 2 des objets seulement ; cellule `GDC` redessinée depuis le lot X7 (piège rejoué, portes non) | `BotInteractivesSmoke`, `BotActorsModelSmoke`, `BotMapViewSmoke`, x7 |
| A11 | Bulles, smileys, émotes | `cMK`, `cS`, `eUK` | traité | partiel : émotes 1, 2, 3, 9, 14, 15, 19, 20 et 21 animées en bande pour les classes, icône pour les autres (lot AN8) | `BotActorRenderSmoke`, `BotExtraClipsSmoke` |

### B. Interactions sur la carte

| # | Fonction | Client 1.34 | StarLoco | Bot | Test |
|---|---|---|---|---|---|
| B1 | Parler à un PNJ | `DC`, `DR`, `DV` | traité | fait | `BotDialogsSmoke`, `BotNpcDialogTextsSmoke` |
| B2 | Acheter, vendre chez un PNJ | `ER0`, `EB`, `ES` | traité | fait | `BotShopSmoke` |
| B3 | Attaquer un groupe de monstres | marche vers le groupe | lance le combat à l’arrivée | fait | `BotMapActionsSmoke` |
| B4 | Défier un joueur | `GA900`, `GA901`, `GA902` | traité | fait (droits de la carte inconnus, le serveur tranche) | `BotMapActionsSmoke` |
| B5 | Agresser un joueur | `GA906` | traité | fait (droits de la carte inconnus) | `BotMapActionsSmoke` |
| B6 | Rejoindre un combat, spectateur, liste des combats | `GA903`, `fL`, `fD` | traité | fait | `BotMapActionsSmoke`, `BotBannerSmoke` |
| B7 | Menu d’un autre joueur | `BW`, `PI`, `ER1`, `FA`, `iA`, `gJR`, `ER12`, `ER13` | traité | fait (ouvert aussi au clic gauche, lot X6) | `BotMapActionsSmoke`, `BotFriendsSmoke`, `BotGuildSmoke`, `BotCraftSmoke`, `BotStarLocoLiveSmoke` |
| B8 | Récolter | `GA500`, `GKK` | traité | fait (manuel ; pas de récolte automatique) | `BotInteractivesSmoke` |
| B9 | Zaap | `GA500;114`, `WU` | traité | fait | `BotDialogsSmoke`, `BotInteractivesSmoke` |
| B10 | Zaapi | `GA500;157`, `Wu` | traité | fait | `BotInteractivesSmoke` |
| B11 | Portes, coffres, enclos, pancartes | `GA500`, `KK`, `dCK` | traité | fait | `BotInteractivesSmoke`, `BotHouseMerchantSmoke`, `BotMountSmoke` |
| B12 | Prisme et percepteur | `GA512`, `GA909`, `GA912`, `ER8` | défense de percepteur (`gTJ`/`gTV`) inopérante | partiel : défense non proposée | `BotGuildSmoke`, `BotAlignmentSmoke` |
| B13 | Menu de son personnage : se frapper, changer d’orientation | `BM*`, `eD` | traité ; `eD` ignoré en combat et pour une tombe | fait (« Se frapper » grisé sans `lang.xml`) | `BotSessionSmoke`, `BotMapActionsSmoke`, `BotStarLocoLiveSmoke` |
| B14 | Tombe, fantôme, statue du phénix | sprite du `GM`, `GF`, marche vers la statue (`GA001`) | `GF` sans vérification, cimetière de la zone, `AR6bk` à la résurrection | fait (refus local de `GF` hors tombe ; tombe posée par la base sur le vrai serveur) | `BotSessionSmoke`, `BotMapActionsSmoke`, `BotStarLocoLiveSmoke` |
| B15 | Mariage devant le prêtre | `GA;617`, `GA618`, `GA619` | n’envoie que `GA;617`, à l’interrogé seul ; la bulle du prêtre nomme deux fois l’interrogé | fait | `BotMapActionsSmoke`, `BotStarLocoLiveSmoke` |

### C. Chat

| # | Fonction | Client 1.34 | StarLoco | Bot | Test |
|---|---|---|---|---|---|
| C1 | Envoyer un message | `BM` | traité | fait | `BotChatProtocolSmoke`, `BotChatUiSmoke` |
| C2 | Changer de canal | préfixe ou menu des canaux | traité | fait | `BotChatUiSmoke` |
| C3 | Chuchoter | `BM<nom>` | traité | fait | `BotChatProtocolSmoke` |
| C4 | Filtrer les canaux | `cC±` | lit un caractère par paquet | fait | `BotChatUiSmoke` |
| C5 | Smileys | `BS` | traité | fait | `BotChatUiSmoke` |
| C6 | Émotes, s’asseoir | `eU` | traité | fait | `BotChatProtocolSmoke`, `BotChatUiSmoke` |
| C7 | Commandes de la console | `/w`, `/f`, `/away`… | traité en partie | partiel : commandes propres à l’interface du client refusées | `BotChatProtocolSmoke`, `BotServerCommandsSmoke` |
| C8 | Noms, objets liés, coordonnées | liens du chat | pas d’objets liés | partiel : pas d’objets liés | `BotChatUiSmoke` |
| C9 | Historique, complétion, agrandissement | local | — | fait | `BotChatUiSmoke` |
| C10 | Messages du serveur | `Im`, `cs`, `M1` | traité | fait | `BotSessionSmoke`, `BotChatUiSmoke` |
| C11 | Journal de combat (lot X7) | `INFO_FIGHT_CHAT`, option `ChatEffects` | — | partiel : textes du journal du bot, pas ceux de langue du client | `BotChatUiSmoke`, `BotFightUiSmoke`, x7 |
| — | Entrée → zone de saisie, Échap → carte (lot X14) | `ACCEPT_CURRENT_DIALOG`, `TEAM_MESSAGE`, `GUILD_MESSAGE`, `setChatFocus` | — | fait | `BotChatUiSmoke`, x14 |

### D. Bandeau, fiches et petits menus

| # | Fonction | Client 1.34 | StarLoco | Bot | Test |
|---|---|---|---|---|---|
| D1 | Boutons du bandeau | ouverture des fenêtres | — | fait | `BotBannerSmoke` |
| D2 | Vie, PA, PM, jauge, illustration | `As`, `GTM`, `Ow`, `BT`, `IC` | traité | partiel : modes Boune et mini carte, date absents | `BotBannerSmoke`, `BotMountSmoke` |
| D3 | Menu principal | changer de personnage, déconnexion | — | fait | `BotBannerSmoke` |
| D4 | Options | fenêtre Options | — | partiel : quatre options enregistrées mais non dessinées, pas d’audio | `BotBannerSmoke` |
| D5 | Raccourcis clavier | table des raccourcis du client | — | partiel : raccourci de surtête des monstres non relié | `BotBannerSmoke` |
| D6 | Barre de raccourcis | `SM`, `OM`, `OU` | `SM` sans effet (`BN`) | partiel : barre déplaçable absente | `BotBannerSmoke`, `BotFightUiSmoke` |
| D7 | Fiche des caractéristiques | `As`, `AB` | traité | fait | `BotStatsSheetSmoke` |
| D8 | Inventaire | `OM`, `OU`, `OD`, `Od` | traité | fait | `BotInventoryGridSmoke`, `BotShopSmoke` |
| D8b | Nourrir le familier et la monture (lot X4) | `OM` vers la case 8, `OM` vers la case 16 avec quantité | monture : poisson seul (`Im190` sinon) | fait | `BotInventoryGridSmoke`, `BotStarLocoLiveSmoke` |
| D9 | Sorts | `SB`, `SF`, `SM` | `SF` seulement en fenêtre d’oubli | fait (zone d’effet non dessinée) | `BotStatsSheetSmoke`, `BotSpellsSmoke` |
| D10 | Métiers | `JS`, `JX`, `JO` | traité | fait | `BotCraftSmoke` |

### E. Social

| # | Fonction | Client 1.34 | StarLoco | Bot | Test |
|---|---|---|---|---|---|
| E1 | Groupe | `PI`, `PA`, `PR`, `PV`, `PF`, `PW` | n’envoie ni `PA`, ni `PCE`, ni `PFE` | fait | `BotPartySmoke` |
| E2 | Amis, ennemis, conjoint | `FA`, `FD`, `FL`, `iA`, `iD`, `FJS`, `FJC` | état des amis toujours « ? » | fait (onglet Ignorés absent) | `BotFriendsSmoke` |
| E3 | Guilde | `g…` | défense de percepteur inopérante | partiel : défense non proposée, enclos sans actions | `BotGuildSmoke` |
| — | Téléportation de guilde, colonne du groupe (lot X14) | `gh<maison>`, `gf<carte>`, `UI_Party` | potion 8883 / 9035 retirée ; `gf` hors enclos : exception | fait (téléportation non rejouée sur StarLoco, faute de maison et d'enclos dans sa base) | `BotGuildSmoke`, `BotPartySmoke`, x14 |

### F. Économie, objets, habitat

| # | Fonction | Client 1.34 | StarLoco | Bot | Test |
|---|---|---|---|---|---|
| F1 | Échanger avec un joueur | `ER1`, `EA`, `EMO`, `EK` | traité | fait | `BotExchangeSmoke` |
| F2 | Artisanat | `ECK3`, `EMO`, `EK`, `Ec` | traité ; artisanat sécurisé aux lots X5 et X6 | partiel : pas d'aide à la forgemagie | `BotCraftSmoke`, `BotStarLocoLiveSmoke` |
| F3 | Banque, coffre | `ECK5`, `KK` | traité | fait | `BotExchangeSmoke`, `BotInteractivesSmoke` |
| F4 | Hôtel de vente | `ER10`, `ER11`, `EH…` | traité | fait (ni tri ni filtre par niveau) | `BotAuctionSmoke` |
| F5 | Mode marchand | `ER4`, `ER6|`, `Eq` | traité ; expulse sur `ER6` seul | fait (clic simple : menu, Maj + clic : achat, lot X6) | `BotHouseMerchantSmoke`, `BotMapActionsSmoke`, `BotStarLocoLiveSmoke` |
| F6 | Maison | `hB`, `hS`, `GA507` | traité | fait | `BotHouseMerchantSmoke` |
| F7 | Monture et enclos | `Rr`, `Rn`, `Rx`, `ER15`, `ER16` | ignore `Rr` sous le niveau 60 | partiel : ni ancêtres, ni recoloration, certificat hors inventaire | `BotMountSmoke` |
| — | Garde et résurrection de familiers (type 9, lot X4) | `ER9`, `ER17`, `EMO`, `EK` | `ECK9` aux deux demandes, sans `EL` | fait | `BotExchangeSmoke`, `BotStarLocoLiveSmoke` |
| — | Artisanat sécurisé (types 12 et 13, lots X5 et X6) | `ER12`, `ER13`, `EP`, `EK` | `ECK12` / `ECK13`, `Ep`, `EcK`, `ERET` | fait (fenêtre et volet ; entrées « Demander à » / « Inviter à » du menu d'un joueur, `ERET` dans le chat, carte de l'artisan du livre : lot X6) | `BotCraftSmoke`, `BotExchangeSmoke`, `BotMapActionsSmoke`, `BotStarLocoLiveSmoke` |
| — | Objets vivants et objets à cible (lot X13) | `Of`, `Os`, `Ox`, `OU<id>\|<sprite>\|<cellule>` | `OCO`, `OAKO`, `Oa`, `eUK<cible>\|17` ; 971 et 973 en décimal | fait (sans association d'un objet libre ni image par apparence) | `BotInventoryGridSmoke`, `BotStarLocoLiveSmoke` |

### G. Progression, monde, combat

| # | Fonction | Client 1.34 | StarLoco | Bot | Test |
|---|---|---|---|---|---|
| G1 | Alignement, ailes, grade | `As`, `GP` | traité | ailes dessinées derrière la surtête des joueurs, avec le bonus PvP (lot AN8) | `BotAlignmentSmoke`, `BotExtraClipsSmoke` |
| G2 | Prismes et conquête | `Cb`, `CB`, `CWJ`, `CIJ` | ignore `CFS` et `CFV`, n’envoie jamais `CIV` | fait | `BotAlignmentSmoke`, `BotWorldMapSmoke` |
| G3 | Quêtes | `QL`, `QS` | pas de réponse à `QS` sans question d’introduction | fait | `BotQuestsSmoke` |
| G4 | Titres | titre dans `GM` | aucun paquet de choix | fait (affichage seul) | `BotQuestsSmoke` |
| G5 | Carte du monde | fenêtre de la carte, `CWJ` | ignore `IM` | partiel : ni carte des donjons, ni alignement des zones | `BotWorldMapSmoke` |
| G6 | Pierres d’âme et arènes | `OU` | pierre utilisable en arène seulement | fait | `BotServerCommandsSmoke` |
| G7 | Documents | `dCK`, `dV` | pancartes seulement | fait | `BotInteractivesSmoke`, `BotServerCommandsSmoke` |
| G8 | Commandes joueur du serveur | — | `.commande` dans le canal général | fait | `BotServerCommandsSmoke` |
| G9 | Combat | `G…`, `GA` | traité ; défis `Gd` rejoués (lot X9), `GA303` rejoué (lot X1) ; `Go` envoyé à la carte d'origine seulement | partiel : zones `GDZ`, prêt `GR1<id>`, options `Go` et invisibilité `GA;150` dessinés depuis le lot X7, mode tactique et ligne de vue locale depuis le lot X33, défis (`Gd`, `GdOK`, `GdKO`, `Gdi`) depuis le lot X9 | `BotFightProtocolSmoke`, `BotCombatSmoke`, `BotFightUiSmoke`, `BotCombatUiSmoke` (Windows), x7, x9 |

## Reste à faire

Liste consolidée des points non faits relevés par les lots et par la relecture de fin de chantier. Les points réglés par un lot ultérieur en ont été retirés.

### À rejouer sur un vrai serveur StarLoco

Préalable : une copie isolée d’un vrai StarLoco, avec des comptes et des personnages de test. `BotStarLocoLiveSmoke` en rejoue déjà la connexion et la création, la carte de départ et un changement de carte, le chat général et privé, une émote assise vue en bande par l’autre compte, un groupe, un échange de kamas, un ami, un défi et un combat contre des monstres (placement, tours, déplacement, un sort, résultat), un dialogue, un achat en boutique et un zaap. Puis rejouer le reste :

- la connexion, la création et la suppression de personnages, la réponse secrète ;
- les cartes, les déplacements, les changements de carte et les formats `GM` des marchands, percepteurs, prismes, montures d’enclos et portes (`GDC`) ;
- les dialogues (ordre réel de `DCK` et `DQ`, effets des réponses), les zaaps (`WC` d’un personnage sans zaap sauvegardé), la boutique (prix et effets réels de `EL`, `OQ`/`OR` après une vente, `ERE` quand une fenêtre est déjà ouverte côté serveur) ;
- le combat : placement, tours, sorts, effets, résultat, et la réaction du serveur aux refus ;
- le chat, les groupes, les amis, la guilde, les échanges, l’hôtel de vente, l’artisanat, les montures, les maisons, la conquête, les quêtes, les commandes du serveur, les pierres d’âme ;
- les augmentations de caractéristiques et de sorts ;
- les animations : sur le vrai serveur, seules la bande de l’émote assise et l’arrivée de projectiles (sans vérification d’image) ont été vues ; coups, morts, effets de sorts, recoloration, accessoires, auras et ailes n’ont pas été comparés au client en jeu ;
- la comparaison des textes et des images avec l’affichage du client 1.34 en jeu (textes de langue, sprites, surtêtes, bulles, marges) ;
- la suite complète `tests/Run-Tests.ps1` sous Windows, en Debug et en Release : sa dernière exécution complète date du 2 octobre 2026, avant les tests du bot ;
- les captures sur la vraie carte : l’outil la lit dans l’export SQL du serveur et dans le client au moment de la capture, sans rien versionner ; sans eux, il compose une prairie synthétique.

Lot X2 : l’orientation, « Se frapper », la tombe ramenée à la vie et le mariage sont rejoués (scénarios x2a à x2c). Restent le mode héroïque (`GO`, morts de `ALK`, `AR<id>`), qui demande `HEROIC` dans la configuration du serveur et un redémarrage, et une tombe obtenue par de vraies défaites à 0 d’énergie plutôt qu’écrite dans la base.

Lot X3 : le pseudo d’un compte neuf (par le bot seul et par la fenêtre du pseudo), le nouveau niveau (parchemin d’expérience écrit dans la base) et l’arrêt du serveur de jeu en pleine partie sont rejoués (scénarios x3a à x3c, x3c seulement avec `AZUR_STARLOCO_GAME_STOP` et `AZUR_STARLOCO_GAME_START`). Restent l’expulsion par un administrateur (`M018`), faute de compte d’administration ; `AlEr` et `AlEs` en jeu (potion de changement de nom, manque B-07), repris par le lot X32 (scénario x32a) ; l’astuce `TIP_GAIN_LEVEL` qui suit la boîte du nouveau niveau ; et, sur le StarLoco de test, l’enregistrement du pseudo dans la base, qui échoue côté serveur (voir `docs/BOT_STARLOCO.md`).

Lot X4 : le repas du familier et de la monture, la garde chez un gardien (PNJ 297, carte 1140) et la résurrection (PNJ 1076, carte 741) sont rejoués (scénarios x4a à x4c). Restent les autres gardiens, le ressusciteur 753 (carte 9716, sans chemin connu depuis un zaap), l’objet d’énergie et la potion de vie du familier, et le refus `ERES` d’un compte non abonné (`SUBSCRIBER`).

Lot X32 : le changement de nom en jeu (potion 10860 écrite dans la base, fenêtre du nom, refus sur place, `AlEs`, `AlEr` puis `GM|~` au nouveau nom) et la roulette du Bouftou (`TC30` → `TV0|<cellule>|<orientation>`, boîte « mini-jeu », joueur libéré) sont rejoués (scénarios x32a et x32b). Restent `TC31` (une fois sur 200), le cas sans potion dans le sac, et l’animation du tutoriel, que le bot ne joue pas.

Lot X6 : les demandes « Demander à » / « Inviter à » depuis les menus (`EW+`, `ER13` → `ERET` puis `ERK` 12, `ER12` → `ERK` 12 / 13), le clic gauche sur un joueur, le livre des artisans avec la carte de l'artisan (scénario x6) et le clic sur un marchand hors ligne avec le mode marchand et l'achat (scénario x6b) sont rejoués. Restent le percepteur (StarLoco exige une guilde de dix membres) et le prisme (aucun sur les cartes de test).

Lot X7 : l'option « aide » vue au-dessus de l'épée par un joueur resté sur la carte, le clip « prêt » d'un Sram inventé dans les deux fenêtres, deux Pièges Sournois (`GDZ` et `GDC` à la seule équipe du lanceur), l'Invisibilité (masqué chez l'adversaire, 40 % chez lui, ligne du chat) et le déclenchement d'un piège sont rejoués (scénario x7). Restent les icônes d'options au-dessus du chef d'équipe en combat (StarLoco n'envoie pas `Go` aux combattants), les `GDC` des portes de labyrinthe, les glyphes et les zones de taille supérieure à 0, et la comparaison des captures avec le client en jeu.

Lot X12 : l'échange entre deux comptes par glisser-déposer et la boîte de quantité (`EMO+`, `EMO-`, `EMG`, `EK` des deux) et la banque (`.banque` → `ECK5`, dépôt et retrait glissés, kamas déposés puis retirés) sont rejoués (scénario x12). Le coffre d'une maison ouvre la même fenêtre mais n'est pas rejoué (aucun personnage de test n'a de maison) ; la mise en vente glissée à l'hôtel de vente n'existe pas chez le client (`BigStoreSell`) et n'est pas jouée.

Lot X8 : la portée en cercle et en croix de deux sorts d'un Iop inventé, la croix `Xd` posée deux fois sur une cible alignée, la croix d'interdiction et le clic sans envoi hors des cibles, la ligne `Le` et le lancer `GA300<sort>;<cellule>` accepté, le chemin de combat survolé puis envoyé (`GA001`, renvoyé par StarLoco avec la cellule de départ) et l'absence de chemin pendant le tour de l'adversaire sont rejoués (scénario x8). Restent un bonus de PO reçu de StarLoco (`GA;117` d'un sort ou d'un objet), la ligne de vue (lot X33) et la comparaison des captures avec le client en jeu.

Lot X13 : sont rejoués (scénario x13) un chapeau vivant écrit dans la base, nourri, changé d'apparence (vue par l'autre joueur, `Oa`) et dissocié par le volet de l'inventaire, et un pain donné à l'autre joueur par la visée de la carte (`OU<pain>|<cible>|<cellule>`, `eUK|17`, `Im01`). Restent l'association d'un objet vivant libre à un objet, la dissociation d'un objet vivant inscrit dans `world.entity.obvijevans` et une bière (`eUK|18`).

Lot X33 : le mode tactique basculé au placement sans paquet (aplat et objets 10000 à 10002), la cellule cachée par le décor exclue des cibles en #0066CC et refusée sans envoi, la Flèche Magique lancée sur une cible visible (`GA300161;<cellule>` accepté) et le retour au décor normal sont rejoués (scénario x33). Restent un bonus `SB` reçu de StarLoco, un lanceur qui porte un autre combattant, les limites par cible et les états sur le vrai serveur, et la comparaison des captures avec le client en jeu.

Lot X9 : sont rejoués (scénario x9) un défi tiré par StarLoco et lu dans la colonne de la fenêtre, un défi à cible (`Gf` avant `Gd`, « Voir la cible » → `Gdi3` → `Gf-1|291` en drapeau, puis `Gdi32` → `Gf-5|278` au passage suivant), un échec (`GdKO`, `Im0188`) et une réussite (`GdOK` en fin de combat, défaite comprise), avec leurs lignes du chat et la rangée « Bonus de challenges » du résultat. Restent deux défis dans un même combat (carte à action de fin de combat ou arène), un combat à plusieurs joueurs, le défi 34 dont la cible change à chaque tour, et la comparaison des captures avec le client en jeu.

Lot X14 : sont rejoués (scénario x14) l'Entrée hors saisie puis dans la zone de saisie (`BM*` → `cMK` chez l'autre compte) et Échap, la création d'une guilde au temple (plot de la carte 2196 → `gn`, « Créer » → `gC…` → `gCK`, Guildalogemme retirée), ses onglets Maisons (`gIH+`) et Enclos (`gIF0`) avec leurs boutons, `gh` et `gf` hors liste refusés sur place, le départ (`gKK`, guilde supprimée par StarLoco), puis un groupe de deux comptes : colonne, portraits, menu, suivi (`IC`, `PF+`) et `PV` qui efface la boussole. Restent la téléportation elle-même et la potion retirée : le StarLoco de test n'a ni maison ni enclos dans sa base, et partager une maison demande une guilde de niveau 10 et de 10 membres.

### Carte et acteurs

- Images des sols en pente : `swfsvg` sait rendre une image choisie, mais le décor versionné a été exporté sans les pentes.
- Formes morphées et textes statiques de 31 objets ; images 3 à 5 des objets interactifs (objet vidé, repousse).
- Accessoires des gfx hors classes (PNJ, marchands, monstres en combat : emplacements non relevés) et tête sous la coiffe (image 2 du clip `_Tete`, lot AN7) ; émotes 4 à 8, 10 à 13 et 16 à 18 des classes et émotes des autres gfx (icône) ; aura 3 (scène vide dans le client) ; bandes des 310 autres gfx de monstres et repos animé des PNJ (les 24 classes ont leurs bandes depuis le lot AN2, les 100 monstres les plus présents depuis le lot AN3 ; le reste peut aller dans `sprites-local/`) ; variantes « porté » (`_C`) des bandes.
- Recoloration (lot AN6) : hors classes et monstres d’AN3, seule la pose fixe des gfx colorés est recolorée ; ni monture chevauchée, ni accessoires, ni familiers ; rien n’a été comparé au client en jeu. Les `hit` et `anim0` trop longs d’AN3 sont corrigés par swfsvg 0.2.5 ; les `die` de 1003, 1004, 1007 et 1009 restent longues (clip qui boucle, coupé à 1 500 ms).
- Chiffres au-dessus des têtes (lot AN2) : Tahoma gras à la place de la police Font2 du client, non exportée.
- Icône de sac des marchands ; couleur d’alignement des PNJ ; membres des équipes d’un combat sous les épées.
- Affichage des cellules déclencheurs ; droits de la carte (défis et agressions interdits) non exportés dans `BotMaps`.
- Raccourci maintenu de la surtête de tous les groupes de monstres.
- Le chargement de `BotZaaps` ne tolère pas encore un dossier absent ou un fichier illisible.

### Combat

- Fond des invocations dans la ligne de temps (les défis sont faits depuis le lot X9).
- Corps à corps (lot X1) : ligne de vue et zone de l’arme laissées au serveur (cellules surlignées = cellules à portée), effets de l’arme absents de l’infobulle, arme non changeable pendant un combat (l’inventaire du bot le refuse, StarLoco l’accepterait). Depuis le lot X8, la zone du type d’arme est dessinée sous le pointeur.
- Bouton d’exclusion d’un coéquipier dans le menu de placement (`GQ<id>` existe dans l’API).
- Nombre de tours du résultat compté par le bot (StarLoco n’envoie pas le troisième champ de `GTS`).
- Caractéristiques temporaires et changement d’apparence non appliqués ; noms des états et des effets.
- Effets de sorts (lot AN4) : aucun repli pour les 10 gfx de type 10 ou 11 sans scène (dessinés par script ou vides) ; scripts des SWF non exécutés (hasard, niveau du sort) ; `anim8` des ballons et feux d’artifice non exporté, couleur à 200 % de `GA208` non reproduite ; clip du coup critique limité à `staticF` d’après le code du client, non observé ; rien n’a été comparé au client en jeu.
- Projectiles des sorts (lot AN5) : types 50 et 51 approchés par la scène, ou `shoot`, à la cellule visée (le clip du client se dessine par script) ; rien à dessiner pour 41 des 61 gfx de type 51, pour le gfx de type 50 et pour 103 et 3001 (scripts) ; `duplicate` de 1210 joué en entier au lieu de retirer le clip ; aucune capture prise pendant un vol sur le vrai StarLoco, rien n’a été comparé au client en jeu.
- Événements visibles (lot X7) : journal du chat écrit avec les textes du bot plutôt que ceux de langue du client (`INVISIBILITY`, `ENTER_STATE` / `EXIT_STATE` et le nom de l'état), « Combattant #<id> » quand un état arrive avant le `GM` ; zones tracées cellule par cellule au lieu du losange vectoriel de `Zone.drawCircle`.
- Visée et chemin (lot X8) : ligne de vue dessinée depuis le lot X33 ; icône d'un objet tenu à la souris (lot X12) ; un chemin coupé avant la cellule survolée n'est ni dessiné ni envoyé ; forme `R` réduite à la cellule ; losange opaque sans formes de sélection exportées.
- Ligne de vue et mode tactique (lot X33) : ligne de vue du corps à corps, limite d'invocations et bonus `SB` autres que 289 et 291 laissés au serveur ; fonds 631 et 632 du mode tactique remplacés par un aplat ; commande `/tactic` non reprise (seul le bouton bascule le mode).
- Défis (lot X9) : position du détail choisie par le bot (à droite de la colonne) ; un second `Gd` du même type remplace le premier (le client l'empilerait) ; « (Niv. ?) » pour une cible dont le niveau est inconnu ; captures non comparées au client en jeu.

### Bandeau, options et fiches

- Illustrations Boune et mini carte, barre de raccourcis déplaçable, date de `BD`, onglet Audio, boîte de confirmation du changement de qualité.
- Options Transparency, SpriteInfos et SpriteMove enregistrées mais non dessinées (PointsOverHead l’est depuis le lot AN2).
- Ordre local des sorts de la barre : gardé sur la machine, jamais renvoyé au serveur.
- Glisser un objet de l’inventaire vers la barre : format en place, non rejoué à la souris.
- Fiche d’un sort sans zone d’effet ; infobulle des sorts de la barre restée simple.
- Inventaire : bordure des objets de panoplie non portés, boîte de quantité du client pour jeter et détruire (le lot X4 l’emploie pour la monture et le retour au sac), objets de la barre non signalés dans la grille.
- Les volets prennent la largeur du tiroir au lieu des fenêtres fixes du client.
- Son : le bot ne joue ni bruitage ni musique. L’évaluation chiffrée et la recommandation (pas de lot son avant les animations) sont dans [la note sur le son](SON_BOT.md) (lot AN9).
- Lot X12 : la boîte de quantité sert à jeter et détruire ; restent les cases d'objets de la barre non voilées en combat et les états requis ou interdits des sorts, inconnus du bot.
- Lot X13 : une seule image par modèle d'objet vivant (grille des apparences et case de l'objet) ; pas d'association d'un objet vivant libre ; pas de visée d'objet depuis la barre de raccourcis ; boîtes de choix du bot au lieu de `ChooseFeed` / `ChooseItemSkin` ; sous la taille par défaut de la fenêtre, la grille du sac peut encore chevaucher le plateau.

### Chat

- Icônes des filtres (les icônes exportées sont celles des catégories d’objets), bulles qui ne suivent pas le filtre 2, heure locale au lieu de l’heure du jeu, une ligne par entrée `Im`.
- Variables de saisie du client (`%position%`…), objets liés, vérification locale de `/g`, liste d’ennemis côté chat, option `AutoHideSmileys`, paroles des objets vivants, canal `/q` masqué.

### Social et économie

- Équipe de comptes du bot sans interface (`Regroupement`) ; après un suivi, la boussole n’est pas remise sur la cible précédente.
- Onglet Ignorés des amis ; couleurs du conjoint.
- Défense des percepteurs (`gTJ`/`gTV`, inopérants chez StarLoco), noms des percepteurs (famille de langue non exportée), actions sur les montures de l’onglet Enclos, aperçu de l’emblème à la création.
- Menu du personnage lui-même : se frapper, libérer son âme, changer de direction.
- Aide à la forgemagie.
- Lot X5 : l'artisanat sécurisé a sa fenêtre et son volet ; les entrées de menu `ER12` / `ER13` et la lecture de `ERET` sont faites au lot X6.
- Hôtel de vente : tri et filtre des modèles par niveau, effets complets dans la liste.
- Maison : emblème de guilde dans le volet, fenêtres de vente et d’intérieur flottantes.
- Montures : ancêtres, recoloration et animation, certificat consulté depuis l’inventaire.
- Lot X12 : l'artisanat sécurisé, le marchand, le percepteur et les familiers gardent leurs champs de quantité au lieu de la boîte et du glisser-déposer des autres volets d'échange.
- Lot X14 : le client 1.34 ne remet pas la boussole sur une cible précédente après un suivi (`IC` la remplace, `removeMember` et `onLeave` l'effacent) ; le bot fait de même. La colonne du groupe a un fond brun opaque au lieu de laisser voir la carte, et le bouton de verrouillage du combat en groupe (`FightGroupAutoLock`) n'y figure pas, l'option n'étant pas reprise.

### Monde

- Carte des donjons, alignement des zones, position de la carte du monde gardée d’une session à l’autre.
- État absent ou invisible non affiché sur le personnage ; commandes d’administration (`BA`).
- Les dossiers `BotMonsters` exportés avant le lot F14 doivent être exportés de nouveau pour la ligne de capture.

### Interface relevée sur les captures

- Fond de la carte mis à l’échelle d’une aire de jeu plus grande que les 742 × 432 du client ; chat agrandi d’environ 460 pixels.
- Sous Mono, sélection bleue système des listes et zones de saisie blanches.
- Cinquième texture d’œuf absente de `oeufs.png` ; icône d’énergie non exportée.
- Mise en page à revoir : inventaire (une seule rangée à 1200 × 800), liste des sorts de 150 pixels avec barres de défilement imbriquées, colonnes de la guilde, légende de la carte du monde, contraste de la jauge de monture.
- Les raccourcis de sorts restent grisés hors combat.
- Captures du combat produites seulement avec des paquets injectés : ni déplacement, ni lancer de sort, ni volet de résultat.

### Tests

- Six tests ne passent que sous Windows (`CharacterEditSmoke`, `AllEditorsWorkflowSmoke`, `MapEditorSmoke`, `BotUiSmoke`, `BotCombatUiSmoke`, `BotHandshakeSmoke`) ; sous Mono, chacun passe ses vérifications jusqu’à l’appel propre à Windows.
- `MapActionIntegrationSmoke` échoue sous Mono seulement : l’éditeur de cartes crée un contrôle Syncfusion dont la vérification de licence appelle `Dispatcher.BeginInvoke`, que Mono n’implémente pas.
- Les envois de création (`AA`) et de suppression (`AD`) d’un personnage ne sont vérifiés par aucun test.
- `test_lang2xml.py` n’est pas lancé par `Run-Tests.ps1 -Outils`.
