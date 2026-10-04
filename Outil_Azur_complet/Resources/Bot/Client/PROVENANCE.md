# Éléments du client fourni utilisés par le bot

Ces PNG proviennent de `modules/core.swf` du client Dofus 1.34 remis par l'utilisateur (`04 - Dofus 1.34 - Qu'Tan et Ilyzaelle`). Ils ont été exportés avec les outils du dépôt (`tools/client-analysis` : `swfsvg` puis `exporter_png.py` à l'échelle 2, zones magenta rendues transparentes) et recadrés avec Pillow. Aucun décompilateur externe ni accès au client n'est nécessaire à l'exécution : le projet copie ce dossier vers `ressources/Bot/UI/Client` à côté de l'exécutable, et chaque fenêtre revient à son rendu dessiné si un fichier manque (`Outil_Azur_complet/Bot/ClientAssets.cs`).

| PNG | Symbole de `core.swf` | Usage dans le bot |
| --- | --- | --- |
| logo.png | `UI_Login` (DefineSprite 579), recadrage du logo | Bandeau de l'écran de connexion |
| oeufs.png | `UI_Login`, bandeau des œufs de classe | Bas de l'écran de connexion |
| bandeau-connexion.png | `UI_Login`, illustration du bandeau supérieur (bitmap JPEG) | Haut de l'écran de connexion |
| bandeau-serveurs.png | `UI_ChooseServer` (DefineSprite 787), bannière illustrée | Au-dessus de la liste des serveurs |
| socle.png | `UI_CreateCharacter` (DefineSprite 764), blason et socle | Sous le portrait de l'aperçu de création |
| de-couleur.png, sexe-homme.png, sexe-femme.png | `UI_CreateCharacter`, dé des couleurs et sélecteurs de sexe | Dé sur les boutons « Choisir… » ; sélecteurs disponibles |
| bouton-principal-haut.png | `ChooseCharacterBtnPlayUp` (1322) | Pilule orange des actions principales (`ClientButton`) |
| bouton-haut.png, bouton-bas.png | `ButtonDownloadUp` / `ButtonDownloadDown` (1744 / 1746), volutes retirées | Pilule parchemin des autres boutons |
| ok-haut.png, ok-bas.png | `ButtonLoginUp` / `ButtonLoginDown` (1704 / 1710) | Disponibles |
| tour-suivant-haut.png, tour-suivant-bas.png | `ButtonNextTurnUp` / `Down` (1659 / 1662) | Bouton « Passer » du combat |
| abandon-haut.png, abandon-bas.png | `ButtonGiveUpUp` / `Down` (1720 / 1723) | Disponibles |
| icone-*.png | `UI_BannerStatsIcon`, `SpellIcon`, `InventoryIcon`, `BookIcon`, `MapIcon`, `FriendsIcon`, `GuildIcon`, `MountIcon`, `PvpIcon` | Icônes du bandeau de jeu |
| stat-*.png, kamas.png | `IconVita`, `IconWisdom`, `IconEarth`, `IconFire`, `IconWater`, `IconAir`, `IconNeutral`, `IconMP`, `IconPP`, `IconInit`, `UI_QuestKamaSymbol` | Disponibles pour les fiches |
| case-inventaire.png, case-surbrillance.png | `UI_InventoryGridBackground` / `Highlight` | Disponibles |
| coche-bas.png, fermer-haut.png, fermer-bas.png, plus.png, moins.png | `ButtonCheckDown`, `ButtonCloseUp` / `Down`, `ButtonPlusUp`, `ButtonMoinsUp` | Disponibles |
| alerte.png, drapeau.png, zaap.png, onglet-menu.png | `UI_LoginAlertIcon`, `UI_MapExplorerFlag`, `UI_WaypointItemLocate`, `UI_MainMenu` | Disponibles |

Les boutons génériques du client (`ButtonNormalUp`, `TextInput`, `ScrollBar`…) sont des formes grises colorées à l'exécution par les feuilles de style du client : ils n'apportent rien et ne sont pas exportés. Les illustrations conservent les droits de leurs titulaires d'origine, comme celles de `../Selection`.

## Symboles du bandeau, du chat et du combat (`exporter_icons.py`)

Les PNG suivants, nommés comme leur symbole exporté de `modules/core.swf`, sont produits par `tools/client-analysis/exporter_icons.py` (famille `UI`, liste `SYMBOLES_UI`) : rendu SVG par `swfsvg`, PNG par cairosvg à l'échelle 2, transformations de couleur du SWF appliquées, magenta pur rendu transparent, palette de 256 couleurs quand l'écart reste invisible. Commande exacte, depuis la racine du dépôt :

```sh
python3 tools/client-analysis/exporter_icons.py --client "<client 1.34>" --sortie Outil_Azur_complet/Resources/Bot --familles UI
```

| PNG | Symbole de `core.swf` | Usage prévu |
| --- | --- | --- |
| Heart.png, Heart_vide.png | `Heart` ; `Heart_vide.png` sans l'instance `_mcRectangle` (le rectangle rouge que le client masque selon les points de vie), dans le même cadre | Jauge de vie du bandeau |
| PointsViewerAP.png, PointsViewerMP.png | `PointsViewerAP`, `PointsViewerMP` | PA et PM du bandeau |
| ButtonBannerRoundUp.png, ButtonBannerRoundDown.png | `ButtonBannerRoundUp` / `Down` | Boutons ronds du bandeau |
| Eye.png, Eye2.png, NoEye.png | `Eye`, `Eye2`, `NoEye` | Disponibles (œil du bandeau) |
| ButtonMainMenuUp.png, ButtonMainMenuDown.png | `ButtonMainMenuUp` / `Down` | Bouton du menu principal |
| UI_MainMenuSubscribe.png, UI_MainMenuOptions.png, UI_MainMenuHelp.png, UI_MainMenuCross.png, UI_MainMenuBugs.png | symboles du même nom | Entrées du menu principal (le fond est `onglet-menu.png`) |
| UI_BannerChatCommandAll.png | `UI_BannerChatCommandAll` | Bouton des canaux du chat |
| UI_BannerClockBack.png, UI_BannerClockArrowHours.png, UI_BannerClockArrowMinutes.png | symboles du même nom | Horloge du bandeau : `Clock` n'est qu'un cadre invisible, le client y pose ces pièces par le code |
| UI_BannerCompassBack.png, UI_BannerCompassArrow.png, UI_BannerCompassNoArrow.png | symboles du même nom | Boussole du bandeau : même cas que l'horloge (`Compass`) |
| FilterIcon0.png, FilterIcon1.png, FilterIcon2.png, FilterIcon3.png, FilterIcon4.png, FilterIcon5.png, FilterIcon6.png, FilterIcon7.png | `FilterIcon0` à `FilterIcon7` | Boutons de filtre du chat (`_btnFilter0` à `_btnFilter7`) ; `FilterIcon8` (canal des débutants) n'est pas un symbole exporté de `core.swf` |
| ButtonChatUp.png, ButtonChatDown.png, ButtonSitUp.png, ButtonSitDown.png, ButtonEmoteUp.png, ButtonEmoteDown.png, SmileysHighlight.png | symboles du même nom | Boutons de la barre de chat, surbrillance du panneau des smileys |
| Star.png | `Star` | Disponible |
| StarBorder.png, StarBorder_fill.png, StarBorder_contour.png | `StarBorder` ; `_fill` : l'instance `fill` seule (la partie recolorée par `STARS_COLORS`), `_contour` : le reste, dans le même cadre | Étoiles des groupes de monstres |
| UI_Party.png | `UI_Party` | Fond de la liste du groupe |
| UI_Timeline.png, TimelineItem.png, TimelinePointer.png, TimelineItemSummonedBg.png | symboles du même nom | Ligne de temps du combat |

`CircleChrono`, `Clock`, `Compass` et `Emblem` ne contiennent qu'un cadre invisible : le client les dessine par le code. Les emblèmes de guilde sont composés par `ClientAssets.Emblem` à partir de `../Emblems`. `UI_MainMenu` et `UI_WaypointItemLocate` ne sont pas réexportés : ce sont `onglet-menu.png` et `zaap.png` ci-dessus.
