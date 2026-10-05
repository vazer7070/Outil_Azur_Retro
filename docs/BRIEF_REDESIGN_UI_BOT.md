# Reprise de la refonte du bot Azur

> **Mise à jour du 3 octobre 2026.** Le client et l'émulateur ont été fournis dans le projet. Leur analyse est désormais reproductible dans le dépôt : `tools/client-analysis` (désassembleur AVM1, pseudo-décompilation, relevé du protocole, export SVG/PNG des symboles de `core.swf`), `docs/PROTOCOLE_CLIENT_1_34.md` (244 routes et 202 envois du client) et `Outil_Azur_complet/Resources/Bot/Client` (éléments graphiques utilisés par les fenêtres du bot, voir son `PROVENANCE.md`). Les chemins `F:\kit` ci-dessous désignent le poste de l'utilisateur ; ils ne sont plus nécessaires.

État de travail du 2 octobre 2026. Ce brief reprend la demande explicite de l’utilisateur : une interface proche du client Dofus Retro fourni et de son design initial, avec une carte réellement exploitable pour se déplacer. La précédente coque de tableau de bord a été rejetée. Une simple modification de couleurs ne répond pas à la demande.

## Références à examiner avant une nouvelle modification

Le dépôt est `G:\RECOVERY FOLDER\Outil_Azur_complet`. Il contient des modifications de plusieurs modules ; les préserver. Ne pas restaurer globalement un fichier ou le dépôt pour retrouver un ancien design. Lire une ancienne version avec `git show HEAD:<chemin>` puis reprendre les éléments utiles dans le code courant.

| Référence | Rôle |
|---|---|
| `Outil_Azur_complet/Bot/GameClientFullform.Designer.cs` | Composition originale : menu supérieur, grande carte centrale, bandeau inférieur de jeu. |
| `Outil_Azur_complet/Bot/GameClientFullform.resx` | Icônes historiques déjà présentes et utilisables sans inventer de nouveaux pictogrammes. |
| `Outil_Azur_complet/Bot/SelectPlayerPerso.Designer.cs` | Sélection historique par silhouettes et noms, à étendre au-delà de cinq personnages. |
| `Outil_Azur_complet/Bot/PersoForm.Designer.cs` | Référence des caractéristiques et métiers, avec leurs icônes. |
| `F:\kit\04 - Dofus 1.34 - Qu'Tan et Ilyzaelle\modules\core.swf` | Composants et palette du client Dofus 1.34. |
| `F:\kit\04 - Dofus 1.34 - Qu'Tan et Ilyzaelle\loader.swf` | Classes et fonctionnement du client Flash. |
| `F:\kit\04 - Dofus 1.34 - Qu'Tan et Ilyzaelle\clips\gfx` | Bibliothèques de sols `g1.swf`, `g2.swf` et objets `o1.swf` à `o11.swf`. |
| `F:\kit\04 - Dofus 1.34 - Qu'Tan et Ilyzaelle\clips\sprites` | Apparences et animations originales des entités. |
| `F:\kit\04 - Dofus 1.34 - Qu'Tan et Ilyzaelle\data\maps` | Cartes du client, utiles pour comparer données et résultat attendu. |
| `F:\kit\03 - Emulateur StarLoco\03 - Login\src\org\starloco\locos` | Sources de la connexion, version, compte, question secrète et redirection. |
| `F:\kit\03 - Emulateur StarLoco\04 - Game\src\org\starloco\locos` | Sources de la sélection/création, cartes, déplacements, statistiques, sorts et interactions. |

L’audit a lu ces références sans lancer le vrai client, les serveurs ni une connexion à leurs bases. Les fichiers de configuration du kit peuvent contenir des accès locaux ; ne pas les recopier dans la documentation, les captures ou le portable.

## Direction visuelle retenue

La fenêtre de jeu doit donner la priorité à la carte. La composition reste celle d’un client : menu compact en haut, carte sur la largeur disponible, chat en bas à gauche, vie et expérience au centre du bandeau inférieur, icônes et raccourcis à droite. Les fiches de caractéristiques, inventaire, sorts, métiers et journal s’ouvrent dans un tiroir refermable. Un tiroir fermé doit rendre la place à la carte.

Le design historique utilisait une carte de 791 × 457 dans une fenêtre de 822 × 677. Reprendre sa composition plutôt que ses positions fixes. La fenêtre actuelle vise 1100 × 760, avec un minimum de 850 × 620 ; contrôler les deux tailles. Éviter les gros titres et états permanents qui consomment l’espace de jeu. La carte doit occuper l’essentiel de la fenêtre lorsque les fiches sont fermées.

La palette a été relevée dans les formes de `core.swf` :

| Usage | Couleur |
|---|---|
| Cadre et fond principal | `#29261F` |
| Surfaces sombres | `#514A3C`, `#796F5A` |
| Contours sombres | `#433C2B` |
| Panneaux parchemin | `#EBE3CB` |
| Bordures et séparations | `#B4AC8D` |
| Accents clairs | `#D8CC9E` |

Le fond `#29261F` apparaît aussi dans `D1ElectronLauncher.html`. Le rose technique `#FF00FF` n’est pas une couleur à introduire dans l’interface. Les polices, contrastes, proportions et infobulles doivent rester lisibles sans transformer le client en formulaire administratif.

Les clés des icônes de `GameClientFullform.resx` sont : `roundedButton9.Image` pour les caractéristiques, `roundedButton1.Image` pour les sorts, `roundedButton2.Image` pour l’inventaire, `roundedButton3.Image` pour les quêtes, `roundedButton4.Image` pour la géoposition, puis `roundedButton5.Image` à `roundedButton8.Image` pour amis, guilde, monture et conquête. `iTalk_Button_11.Image` et `iTalk_Button_21.Image` sont les icônes historiques de canal et d’envoi. Utiliser ces images avec une infobulle et un nom accessible.

## Code actuel et comportement attendu

| Fichier | Responsabilité |
|---|---|
| `Outil_Azur_complet/Bot/BotUi.cs` | Palette, composants communs, cadres et jauge de vie. |
| `Outil_Azur_complet/Bot/GameClientFullform.cs` | Coque de jeu, chat inférieur, menus, tiroir et raccourcis. |
| `Outil_Azur_complet/Bot/Interfaces/MapControl.cs` | Liaison de la carte à la session et demande de déplacement. |
| `Outil_Azur_complet/Bot/Controls/UserMapControl.cs` | Géométrie, dessin, survol, détection des cellules, zoom et déplacement de la vue. |
| `Outil_Azur_complet/Bot/Controls/BotMapArtwork.cs` | Lecture graphique de `MapData` et composition des ressources raster disponibles. |
| `Outil_Azur_complet/Bot/SelectPlayerPerso.cs` | Vignettes de personnages, sélection stable et confirmation des actions. |
| `Outil_Azur_complet/Bot/CreateCharacter.cs` | Identité, classe, sexe, couleurs et aperçu authentique de l’apparence. |

La sélection utilise des vignettes avec nom, niveau, classe et portrait, tout en conservant le `ListView` privé `characters` et les identifiants dans `Tag`. La liste doit défiler, accepter plus de cinq personnages et conserver le personnage sélectionné après actualisation. Les images proviennent de `ressources/Bot/sprites/<gfx>F.png`, avec recours à d’autres orientations disponibles si nécessaire. Si aucune image n’est utilisable, afficher un état compréhensible plutôt qu’un faux portrait.

La création présente un portrait selon la classe et le sexe sélectionnés. Les trois couleurs sont modifiables au sélecteur ou par valeur, avec `-1` pour le choix par défaut. Les nuanciers se mettent à jour ; le PNG du portrait conserve les couleurs d’origine. Une véritable recoloration nécessite de respecter les zones du sprite Flash : une teinte uniforme sur l’image serait trompeuse.

La carte permet le clic gauche pour le déplacement du personnage, la molette pour le zoom, le bouton central ou Ctrl + glisser pour déplacer la vue, et **Adapter** pour retrouver la carte complète. La grille et les numéros de cellules restent optionnels. Le glissement de la vue ne doit jamais envoyer un déplacement du personnage. Le dessin, le survol et le clic doivent partager la même transformation.

Les boutons correspondant aux fonctions non implémentées sont désactivés et expliqués par leur infobulle. Les raccourcis de sorts ouvrent leurs fiches ; ils n’exécutent pas un sort en combat. Les modes automatique/admin, scripts Lua, plugins et interfaces avancées ne sont pas à annoncer comme utilisables avant leur implémentation et validation.

## Limites graphiques à traiter honnêtement

Le rendu courant reconstruit un décor avec les images de `ressources/maps/sols`, `objets` et `backgrounds`, puis les sprites raster disponibles. `BotMapArtwork` lit les identifiants, les niveaux et les couches de `MapData`. Il n’exécute pas le moteur Flash du client.

Les symboles de `clips/gfx/*.swf` sont exportés sous des noms numériques. Le GFX d’une cellule correspond à ce **nom d’export**, pas au `CharacterId` interne du fichier. Par exemple, dans `o1.swf`, le symbole interne 4 exporte le nom `10002`. Toute extraction ou conversion future doit préserver cette correspondance.

Le client contient majoritairement des formes et sprites SWF, et pas une simple collection de JPEG. Un rendu complet nécessite encore les formes vectorielles, les transformations et origines des symboles, leurs timelines, masques, recolorations et animations. Les niveaux de terrain peuvent décaler les cellules sans reproduire la déformation exacte des sols en pente. Ne pas qualifier la reconstruction PNG d’exécution du client officiel ni de reproduction graphique complète.

Toute ressource absente ou illisible doit apparaître dans l’état de la carte. Garder une représentation minimale exploitable des cellules et des entités, mais ne pas présenter un décor incomplet comme entièrement chargé. Mettre en cache les images pendant leur utilisation et les libérer au changement de carte ou à la fermeture, sans conserver leurs fichiers verrouillés.

## Vérifications avant livraison

1. Compiler la solution puis exécuter les contrôles réseau, ressources, gameplay et interface dans Debug et Release. Les résultats du 2 octobre 2026 figurent ci-dessous.
2. Vérifier visuellement connexion, serveurs, personnages, création, carte et tiroirs à 1100 × 760 et 850 × 620. Montrer de vraies captures du logiciel compilé.
3. Sur une carte munie de sols, objets et entités, comparer plusieurs centres et bords de cellules au survol et au clic, avant et après redimensionnement, zoom et déplacement de la vue. Tester les cellules extrêmes et la cellule zéro.
4. Vérifier les repères et informations lorsque certaines images sont absentes, et lors d’un changement vers une carte indisponible. Ne pas conserver le décor ou les entités de la précédente carte.
5. Vérifier qu’ouvrir puis fermer une fiche rend la place à la carte et qu’aucun élément du chat, de la jauge ou des raccourcis n’est coupé à la taille minimale.
6. Vérifier la sélection stable des personnages et sorts, les états en attente/refus, et l’absence de fuite d’images après plusieurs changements de classe, carte ou fenêtre.
7. Préserver les corrections de protocole et les protections des comptes. Une amélioration de statistiques ou de sort doit attendre la confirmation du serveur ; une suppression garde sa confirmation explicite.
8. Effectuer ensuite une validation avec une copie isolée du vrai StarLoco et des comptes de test : connexion, personnages, carte, mouvements, chat, question secrète, améliorations et interactions. Cette validation réelle n’a pas encore été effectuée.
9. Construire et vérifier un nouveau portable après les contrôles automatisés et visuels, en indiquant explicitement que la validation réelle reste à effectuer. Ne pas inclure la configuration personnelle du bot ni `AccountSingle`.

## Résultats du 2 octobre 2026

- Compilation Debug et Release ; **29 tests réussis dans chacune** (21 sans base, 8 MySQL isolé).
- Captures natives vérifiées : connexion, sélection/création, Nowel à 1100 × 760 et 850 × 620, fiches caractéristiques/sorts, Astrub, zoom et discussion privée. Le terrain utilise les vrais fichiers fournis ; les personnages et comptes sont fictifs.
- Les textes superposés des vignettes, le contraste de l’option d’enregistrement, l’accès aux trois couleurs et le chevauchement du coût de caractéristiques ont été corrigés après inspection des captures.
- Captures : `C:\Users\kishi\Documents\Codex\2026-09-14\explique-moi-ce-projet-en-quoi\outputs\bot-client-2026-10-02` ; aperçu `outputs/Apercu_bot_Azur_2026-10-02.html` dans le même espace de travail.
- Journaux : `outputs/Verification_Azur_Debug_Client_2026-10-02.log` et `outputs/Verification_Azur_Release_Client_2026-10-02.log`.
- Archive de livraison : `outputs/Azur_portable_2026-10-02.zip`, construit depuis Release avec les guides, puis contrôlé intégralement par `tests/verify_portable.py`. Le journal `outputs/Verification_portable_2026-10-02.log` consigne le contrôle des empreintes et l’exclusion des configurations locales.
- Limites restantes : test sur les vrais Login/Game isolés, fonctions désactivées, interactions/combat/automatisation, fidélité SWF vectorielle, pentes, recoloration et animations. Aucun essai en jeu réel n’est revendiqué.
