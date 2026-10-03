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
