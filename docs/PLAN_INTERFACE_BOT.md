# Plan de l’interface du bot

État au 8 octobre 2026. Ce document résume le chantier de l’interface de jeu du bot : la demande, les 33 lots du plan et le travail de départ qui les a précédés, la couverture des fonctions du client et ce qui reste à faire. Le détail de chaque fonction (formats, règles, limites) est dans [le guide du bot](BOT_STARLOCO.md). Les formats des paquets du client sont relevés dans [la référence du protocole du client 1.34](PROTOCOLE_CLIENT_1_34.md). Des captures de l’interface sont dans [docs/captures](captures/README.md).

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

### A. Carte, décor et acteurs

| # | Fonction | Client 1.34 | StarLoco | Bot | Test |
|---|---|---|---|---|---|
| A1 | Décor (sols, objets, fond, pentes) | SWF de carte après `GDM` | envoie `GDM` | partiel : pas d’images de pente, 31 objets incomplets | `BotDecorAnchorsSmoke`, `BotMapViewSmoke` |
| A2 | Changer de carte | marche vers une cellule de sortie | téléporte à l’arrivée (`GA;2`, `GDM`) | partiel : déclencheurs chargés mais non affichés | `BotActorsModelSmoke`, `BotMovementSmoke` |
| A3 | Voir les joueurs | `GM` type ≥ 0 | traité | partiel : ni recoloration, ni accessoires, ni ailes, ni auras | `BotActorRenderSmoke`, `BotSpriteSheetsSmoke` |
| A4 | Voir les PNJ et leur nom | `GM` type -4 | traité | fait | `BotActorRenderSmoke`, `BotActorsModelSmoke` |
| A5 | Groupes de monstres, niveaux, étoiles | `GM` type -3 | traité | fait | `BotActorRenderSmoke` |
| A6 | Marchands, percepteurs, prismes, montures d’enclos, épées | `GM` types -5, -6, -9, -10 ; `Gc` | traité | partiel : pas d’icône de sac, équipes des épées non suivies | `BotActorsModelSmoke`, `BotActorRenderSmoke` |
| A7 | Survoler, sélectionner un acteur | aucun paquet | — | fait | `BotActorRenderSmoke` |
| A8 | Se déplacer | `GA001`, `GKK`, `GKE` | traité | fait | `BotMovementSmoke`, `BotGameplaySmoke` |
| A9 | Orientation et marche animée | `eD`, `GA;1` | traité | partiel : bandes de marche pour les 24 classes seulement | `BotActorRenderSmoke`, `BotMovementSmoke` |
| A10 | Objets interactifs, objets au sol, portes | `GDF`, `GDO`, `GDC` | traité | partiel : images 1 et 2 des objets seulement | `BotInteractivesSmoke`, `BotActorsModelSmoke` |
| A11 | Bulles, smileys, émotes | `cMK`, `cS`, `eUK` | traité | partiel : émotes en icône, sans animation | `BotActorRenderSmoke` |

### B. Interactions sur la carte

| # | Fonction | Client 1.34 | StarLoco | Bot | Test |
|---|---|---|---|---|---|
| B1 | Parler à un PNJ | `DC`, `DR`, `DV` | traité | fait | `BotDialogsSmoke`, `BotNpcDialogTextsSmoke` |
| B2 | Acheter, vendre chez un PNJ | `ER0`, `EB`, `ES` | traité | fait | `BotShopSmoke` |
| B3 | Attaquer un groupe de monstres | marche vers le groupe | lance le combat à l’arrivée | fait | `BotMapActionsSmoke` |
| B4 | Défier un joueur | `GA900`, `GA901`, `GA902` | traité | fait (droits de la carte inconnus, le serveur tranche) | `BotMapActionsSmoke` |
| B5 | Agresser un joueur | `GA906` | traité | fait (droits de la carte inconnus) | `BotMapActionsSmoke` |
| B6 | Rejoindre un combat, spectateur, liste des combats | `GA903`, `fL`, `fD` | traité | fait | `BotMapActionsSmoke`, `BotBannerSmoke` |
| B7 | Menu d’un autre joueur | `BW`, `PI`, `ER1`, `FA`, `iA`, `gJR` | traité | fait | `BotMapActionsSmoke`, `BotFriendsSmoke`, `BotGuildSmoke` |
| B8 | Récolter | `GA500`, `GKK` | traité | fait (manuel ; pas de récolte automatique) | `BotInteractivesSmoke` |
| B9 | Zaap | `GA500;114`, `WU` | traité | fait | `BotDialogsSmoke`, `BotInteractivesSmoke` |
| B10 | Zaapi | `GA500;157`, `Wu` | traité | fait | `BotInteractivesSmoke` |
| B11 | Portes, coffres, enclos, pancartes | `GA500`, `KK`, `dCK` | traité | fait | `BotInteractivesSmoke`, `BotHouseMerchantSmoke`, `BotMountSmoke` |
| B12 | Prisme et percepteur | `GA512`, `GA909`, `GA912`, `ER8` | défense de percepteur (`gTJ`/`gTV`) inopérante | partiel : défense non proposée | `BotGuildSmoke`, `BotAlignmentSmoke` |

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

### D. Bandeau, fiches et petits menus

| # | Fonction | Client 1.34 | StarLoco | Bot | Test |
|---|---|---|---|---|---|
| D1 | Boutons du bandeau | ouverture des fenêtres | — | fait | `BotBannerSmoke` |
| D2 | Vie, PA, PM, jauge, illustration | `As`, `GTM`, `Ow`, `BT`, `IC` | traité | partiel : modes Boune et mini carte, date absents | `BotBannerSmoke`, `BotMountSmoke` |
| D3 | Menu principal | changer de personnage, déconnexion | — | fait | `BotBannerSmoke` |
| D4 | Options | fenêtre Options | — | partiel : quatre options enregistrées mais non dessinées, pas d’audio | `BotBannerSmoke` |
| D5 | Raccourcis clavier | table des raccourcis du client | — | partiel : raccourci de surtête des monstres non relié | `BotBannerSmoke` |
| D6 | Barre de raccourcis | `SM`, `OM`, `OU` | `SM` sans effet (`BN`) | partiel : corps à corps et barre déplaçable absents | `BotBannerSmoke` |
| D7 | Fiche des caractéristiques | `As`, `AB` | traité | fait | `BotStatsSheetSmoke` |
| D8 | Inventaire | `OM`, `OU`, `OD`, `Od` | traité | fait | `BotInventoryGridSmoke`, `BotShopSmoke` |
| D9 | Sorts | `SB`, `SF`, `SM` | `SF` seulement en fenêtre d’oubli | fait (zone d’effet non dessinée) | `BotStatsSheetSmoke`, `BotSpellsSmoke` |
| D10 | Métiers | `JS`, `JX`, `JO` | traité | fait | `BotCraftSmoke` |

### E. Social

| # | Fonction | Client 1.34 | StarLoco | Bot | Test |
|---|---|---|---|---|---|
| E1 | Groupe | `PI`, `PA`, `PR`, `PV`, `PF`, `PW` | n’envoie ni `PA`, ni `PCE`, ni `PFE` | fait | `BotPartySmoke` |
| E2 | Amis, ennemis, conjoint | `FA`, `FD`, `FL`, `iA`, `iD`, `FJS`, `FJC` | état des amis toujours « ? » | fait (onglet Ignorés absent) | `BotFriendsSmoke` |
| E3 | Guilde | `g…` | défense de percepteur inopérante | partiel : défense non proposée, enclos sans actions | `BotGuildSmoke` |

### F. Économie, objets, habitat

| # | Fonction | Client 1.34 | StarLoco | Bot | Test |
|---|---|---|---|---|---|
| F1 | Échanger avec un joueur | `ER1`, `EA`, `EMO`, `EK` | traité | fait | `BotExchangeSmoke` |
| F2 | Artisanat | `ECK3`, `EMO`, `EK`, `Ec` | traité ; artisanat sécurisé non retenu | partiel : ni artisanat sécurisé, ni aide à la forgemagie | `BotCraftSmoke` |
| F3 | Banque, coffre | `ECK5`, `KK` | traité | fait | `BotExchangeSmoke`, `BotInteractivesSmoke` |
| F4 | Hôtel de vente | `ER10`, `ER11`, `EH…` | traité | fait (ni tri ni filtre par niveau) | `BotAuctionSmoke` |
| F5 | Mode marchand | `ER4`, `ER6|`, `Eq` | traité ; expulse sur `ER6` seul | fait | `BotHouseMerchantSmoke` |
| F6 | Maison | `hB`, `hS`, `GA507` | traité | fait | `BotHouseMerchantSmoke` |
| F7 | Monture et enclos | `Rr`, `Rn`, `Rx`, `ER15`, `ER16` | ignore `Rr` sous le niveau 60 | partiel : ni ancêtres, ni recoloration, certificat hors inventaire | `BotMountSmoke` |
| — | Garde de familiers (type 9), type 17 | `ER9`, `ER17` | traité | absent : le bot referme ces fenêtres | `BotExchangeSmoke` |

### G. Progression, monde, combat

| # | Fonction | Client 1.34 | StarLoco | Bot | Test |
|---|---|---|---|---|---|
| G1 | Alignement, ailes, grade | `As`, `GP` | traité | partiel : ailes non dessinées sur les sprites | `BotAlignmentSmoke` |
| G2 | Prismes et conquête | `Cb`, `CB`, `CWJ`, `CIJ` | ignore `CFS` et `CFV`, n’envoie jamais `CIV` | fait | `BotAlignmentSmoke`, `BotWorldMapSmoke` |
| G3 | Quêtes | `QL`, `QS` | pas de réponse à `QS` sans question d’introduction | fait | `BotQuestsSmoke` |
| G4 | Titres | titre dans `GM` | aucun paquet de choix | fait (affichage seul) | `BotQuestsSmoke` |
| G5 | Carte du monde | fenêtre de la carte, `CWJ` | ignore `IM` | partiel : ni carte des donjons, ni alignement des zones | `BotWorldMapSmoke` |
| G6 | Pierres d’âme et arènes | `OU` | pierre utilisable en arène seulement | fait | `BotServerCommandsSmoke` |
| G7 | Documents | `dCK`, `dV` | pancartes seulement | fait | `BotInteractivesSmoke`, `BotServerCommandsSmoke` |
| G8 | Commandes joueur du serveur | — | `.commande` dans le canal général | fait | `BotServerCommandsSmoke` |
| G9 | Combat | `G…`, `GA` | traité ; défis et `GA303` à rejouer | partiel : ni défis, ni corps à corps, ni mode tactique | `BotFightProtocolSmoke`, `BotCombatSmoke`, `BotFightUiSmoke`, `BotCombatUiSmoke` (Windows) |

## Reste à faire

Liste consolidée des points non faits relevés par les lots et par la relecture de fin de chantier. Les points réglés par un lot ultérieur en ont été retirés.

### À rejouer sur un vrai serveur StarLoco

Préalable : une copie isolée d’un vrai StarLoco, avec des comptes et des personnages de test. `BotStarLocoLiveSmoke` en rejoue déjà la connexion et la création, la carte de départ et un changement de carte, le chat général et privé, un groupe, un échange de kamas, un ami, un défi et un combat contre des monstres (placement, tours, déplacement, un sort, résultat), un dialogue, un achat en boutique et un zaap. Puis rejouer le reste :

- la connexion, la création et la suppression de personnages, la réponse secrète ;
- les cartes, les déplacements, les changements de carte et les formats `GM` des marchands, percepteurs, prismes, montures d’enclos et portes (`GDC`) ;
- les dialogues (ordre réel de `DCK` et `DQ`, effets des réponses), les zaaps (`WC` d’un personnage sans zaap sauvegardé), la boutique (prix et effets réels de `EL`, `OQ`/`OR` après une vente, `ERE` quand une fenêtre est déjà ouverte côté serveur) ;
- le combat : placement, tours, sorts, effets, résultat, et la réaction du serveur aux refus ;
- le chat, les groupes, les amis, la guilde, les échanges, l’hôtel de vente, l’artisanat, les montures, les maisons, la conquête, les quêtes, les commandes du serveur, les pierres d’âme ;
- les augmentations de caractéristiques et de sorts ;
- la comparaison des textes et des images avec l’affichage du client 1.34 en jeu (textes de langue, sprites, surtêtes, bulles, marges) ;
- la suite complète `tests/Run-Tests.ps1` sous Windows, en Debug et en Release : sa dernière exécution complète date du 2 octobre 2026, avant les tests du bot ;
- les captures sur la vraie carte : l’outil la lit dans l’export SQL du serveur et dans le client au moment de la capture, sans rien versionner ; sans eux, il compose une prairie synthétique.

### Carte et acteurs

- Images des sols en pente : `swfsvg` sait rendre une image choisie, mais le décor versionné a été exporté sans les pentes.
- Formes morphées et textes statiques de 31 objets ; images 3 à 5 des objets interactifs (objet vidé, repousse).
- Recoloration des sprites (couleurs de `GM`), accessoires, ailes d’alignement, auras, bandes d’émote ; bandes des 310 autres gfx de monstres et repos animé des PNJ (les 24 classes ont leurs bandes depuis le lot AN2, les 100 monstres les plus présents depuis le lot AN3 ; le reste peut aller dans `sprites-local/`) ; `hit` et `anim0` trop longs de six de ces monstres (défaut de swfsvg 0.2.3, voir `PROVENANCE.md`) ; variantes « porté » (`_C`) des bandes.
- Chiffres au-dessus des têtes (lot AN2) : Tahoma gras à la place de la police Font2 du client, non exportée.
- Icône de sac des marchands ; couleur d’alignement des PNJ ; membres des équipes d’un combat sous les épées.
- Affichage des cellules déclencheurs ; droits de la carte (défis et agressions interdits) non exportés dans `BotMaps`.
- Raccourci maintenu de la surtête de tous les groupes de monstres.
- Le chargement de `BotZaaps` ne tolère pas encore un dossier absent ou un fichier illisible.

### Combat

- Défis (`Gd…`), attaque au corps à corps (`GA303`), mode tactique, fond des invocations dans la ligne de temps.
- Bouton d’exclusion d’un coéquipier dans le menu de placement (`GQ<id>` existe dans l’API).
- Nombre de tours du résultat compté par le bot (StarLoco n’envoie pas le troisième champ de `GTS`).
- Caractéristiques temporaires et changement d’apparence non appliqués ; noms des états et des effets.
- Aperçu du chemin pendant un combat.
- Effets de sorts (lot AN4) : aucun repli pour les 10 gfx de type 10 ou 11 sans scène (dessinés par script ou vides) ; scripts des SWF non exécutés (hasard, niveau du sort) ; `anim8` des ballons et feux d’artifice non exporté, couleur à 200 % de `GA208` non reproduite ; clip du coup critique limité à `staticF` d’après le code du client, non observé ; rien n’a été comparé au client en jeu.
- Projectiles des sorts (lot AN5) : types 50 et 51 approchés par la scène, ou `shoot`, à la cellule visée (le clip du client se dessine par script) ; rien à dessiner pour 41 des 61 gfx de type 51, pour le gfx de type 50 et pour 103 et 3001 (scripts) ; `duplicate` de 1210 joué en entier au lieu de retirer le clip ; aucune capture prise pendant un vol sur le vrai StarLoco, rien n’a été comparé au client en jeu.

### Bandeau, options et fiches

- Illustrations Boune et mini carte, barre de raccourcis déplaçable, date de `BD`, onglet Audio, boîte de confirmation du changement de qualité.
- Options Transparency, SpriteInfos et SpriteMove enregistrées mais non dessinées (PointsOverHead l’est depuis le lot AN2).
- Ordre local des sorts de la barre : gardé sur la machine, jamais renvoyé au serveur.
- Glisser un objet de l’inventaire vers la barre : format en place, non rejoué à la souris.
- Fiche d’un sort sans zone d’effet ; infobulle des sorts de la barre restée simple.
- Inventaire : bordure des objets de panoplie non portés, boîte de quantité du client, objets de la barre non signalés dans la grille.
- Les volets prennent la largeur du tiroir au lieu des fenêtres fixes du client.
- Son : le bot ne joue ni bruitage ni musique. L’évaluation chiffrée et la recommandation (pas de lot son avant les animations) sont dans [la note sur le son](SON_BOT.md) (lot AN9).

### Chat

- Icônes des filtres (les icônes exportées sont celles des catégories d’objets), bulles qui ne suivent pas le filtre 2, heure locale au lieu de l’heure du jeu, une ligne par entrée `Im`.
- Variables de saisie du client (`%position%`…), objets liés, vérification locale de `/g`, liste d’ennemis côté chat, option `AutoHideSmileys`, paroles des objets vivants, canal `/q` masqué.

### Social et économie

- Équipe de comptes du bot sans interface (`Regroupement`) ; après un suivi, la boussole n’est pas remise sur la cible précédente.
- Onglet Ignorés des amis ; couleurs du conjoint.
- Défense des percepteurs (`gTJ`/`gTV`, inopérants chez StarLoco), noms des percepteurs (famille de langue non exportée), actions sur les montures de l’onglet Enclos, aperçu de l’emblème à la création.
- Menu du personnage lui-même : se frapper, libérer son âme, changer de direction.
- Artisanat sécurisé (`ER12`/`ER13`), aide à la forgemagie, compétences des artisans publics sur la carte.
- Hôtel de vente : tri et filtre des modèles par niveau, effets complets dans la liste.
- Maison : emblème de guilde dans le volet, fenêtres de vente et d’intérieur flottantes.
- Montures : ancêtres, recoloration et animation, certificat consulté depuis l’inventaire.
- Garde de familiers (`ER9`) et échange de type 17 sans volet.

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

- Huit tests ne passent que sous Windows (`CharacterEditSmoke`, `MapEditorSmoke`, `EditorWorkflowSmoke`, `AllEditorsWorkflowSmoke`, `BotHandshakeSmoke`, `BotUiSmoke`, `BotCombatUiSmoke`, `BotEntitiesSmoke`).
- `MapActionIntegrationSmoke` échoue sous Mono seulement : l’éditeur de cartes crée un contrôle Syncfusion dont la vérification de licence appelle `Dispatcher.BeginInvoke`, que Mono n’implémente pas.
- Les envois de création (`AA`) et de suppression (`AD`) d’un personnage ne sont vérifiés par aucun test.
- `test_lang2xml.py` n’est pas lancé par `Run-Tests.ps1 -Outils`.
