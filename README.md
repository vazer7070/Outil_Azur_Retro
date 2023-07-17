# Outil Azur Retro

![forthebadge](https://forthebadge.com/images/badges/made-with-c-sharp.svg)

Voici le projet couteau suisse permettant la génération de carte, un bot, un outil de recherche et de création, un gestionnaire de base de joueurs et de personnages.

Ne pouvant pas le finir comme je voudrais, je le place ici en open source, je connais la communauté FR de l'émulation, elle est horrible entre elle et égoïste, ne voulant rien faire pour faire progresser le groupe, préférant penser qu'il est simple de monter et de tenir un serveur en écrasant les autres et reprenant inlassablement les mêmes outils défaillants, vérolés et la plupart du temps dépassés.
Je pense parler dans le vide mais au moins je le dis, si vous voulez faire grandir l'émulation ou du moins faire perdurer ce qu'il en reste, agissons en groupe solidaire, pour la passion plus que pour l'argent.
(il suffit de voir le GIT de [TrinityCore](https://github.com/TrinityCore/TrinityCore) pour WOW pour voir que eux ils ont compris et ça fait des années que ce projet perdure).

A l'instar de moi, il reste des vieux de l'émulation encore plus ou moins actif, qui ont connus l'ère de Britana, Aidemu, Worldemu, l'apogée de DOE et bien d'autres forums éphémères, c'est peut-être peine perdu mais arrêtez d'être cynique et con avec les nouveaux, c'est comme ça que l'on regresse de plus en plus et que maintenant on se retrouve avec des leechers et non plus de vrais devscar il n'existe plus rien de concret, l'émulation rétro est hispanique pour la plus stable (une vague version de StarLoco et encore...) et la 2.x est morte, certains innovent un peu mais les git meurent faute de commit et Stump est comme la licence SW de Disney et puis...c'est tout...hélas.

Je partage ce projet pour vous montrer qu'il est possible de faire de gros projet en commun, certains vont le prendre, le repomper et si ils trouvent des pigeons essayer de le vendre, certains vont le prendre pour eux et l'étudier, le faire à sa sauce mais j'ose espérer que hormis me dire "il y a un bug là", "ça on dirait tel code PLAGIAT" ou que sais-je, certains tenteront l'aventure de le faire perdurer et grandir.

Le potentiel est là, alors faisons ce qu'il faut pour que ça se fasse.
**(les trolls, fermez vos gueules, vous pourrez vous la ramenez quand vous sortirez du code potable et encore)**

Je ferais des mises à jour quand je pourrais, en corrigeant des bugs remontés et ajoutant des fonctions, si vous avez des commits, n'hésitez pas, on peut en discuter sans problèmes.

## Installation

L'outil peut se gérer en local mais alors certains outils ne seront pas disponible faute de BDD active (compatible WAMP/XAMPP).

Il prends ses mises à jours sur un serveur distant, ça veut donc dire que à chaque fois que je publie une mise à jour, si vous avez laissés le système tel quel, ça s'installera et vous aurez le changelog sur ce GIT.

Si il manque les images pour l'éditeur de carte, il s'agit des mêmes que pour **Astria Map Editor** avec juste des noms de dossiers différents (parce que why not), vous ne pourrez pas le lancer si il ne les trouves pas.

Vous pouvez modifier les identifiants de la/des BDD dans le panneau de configuration et relancer la connexion depuis ce dernier pour vous permettre de lancer les outils manquants.
Actuellement, l'outil de recherche pour les objets IG et les éditeurs (contenant donc ceux de personnages et de comptes) sont adaptés pour les bases de données de **Kryone V2**, ces dernières sont fournis pour que vous puissiez voir si les votres correspondent.

Pour la mise en place de la BDD, vous avez 2 Json: **auth_tables.json** et **world_tables.json** qui se trouvent à la racine du programme, il est important de bien noter les noms des tables à chaques fonctions afin que l'outil puisse correctement choisir les tables associées aux fonctions demandées.

Il est possible de modifier ces fichiers directement depuis le panneau de configuration de l'outil, dans le menu principal.


Si vous souhaitez une version particulière, vous pouvez la demander ou alors la coder et la mettre en commit pour la placer dans l'outil.

## Outils

Je vous présente les outils un à un, les fonctions peuvent changer au gré des mises à jour donc je reste généraliste.

### Éditeur de compte
 Cet outil permet de visualiser le compte et son état (VIP/banni/staff), les informations de compte (MDP, question et réponse secrète), l'état de connexion ainsi que les personnages en jeu qui sont liés à ce compte ainsi que divers informations notamment les points (mais c'est à voir selon les BDD)
 
 Il est possible de tout modifier, attention par contre, si vous modifier le MDP hashé à bien le hasher avant car sinon ça ne fonctionnera pas.

 Il est également possible de bannir/débannir, rendre/retirer VIP voir de supprimer complètement le compte.

 ### Création de compte
 Un outil simple qui permet une création rapide d'un compte directement depuis l'outil, il y a des fonctions de hash prédéfinies (MD5 et SHA512) ou le mettre en clair si vous le désirez.

 ### Éditeur de personnage
 *Il n'est pas possible depuis l'outil de créer un personnage sauf depuis le bot qui simule la page de création de personnage lors de la connexion à son compte (depuis officiel comme privé).*

Cet outil recense tout les personnages du serveur ainsi que toute les informations qui les concernent, ça va de l'id à la liste de son inventaire et de ses sorts.

Il est possible de tout modifier sauf l'id du personnage, si il est en prison ou pas et son point de sauvegarde et les couleurs (si vraiment vous souhaitez, c'est très simple de le rajouter).De plus il est possible de bannir ainsi que de supprimer le personnage depuis cette interface.

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
Cet outil permet de créer un objet depuis 0 avec toutes les possibilités de conditions et d'effets possibles. Vous pourrez le générer en SQL et en SWF avec possibilités d'injection directe dans les 2 cas (**à l'instant T de ces lignes, les librairies SWF ne permettent pas encore de le faire mais une maj le prévoit**) mais il est aussi possible de générer les lignes pour une injection ultérieure pour le SQL comme le SWF.

Il est possible aussi de l'ajouter à une panoplie existante (vanilla ou crée par vous préalablement) ainsi que le rendre fabricable de faire la recette directement depuis cette interface.

