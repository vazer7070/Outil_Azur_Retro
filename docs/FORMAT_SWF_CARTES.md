# Import et export des cartes SWF

L’éditeur propose **Ouvrir → SWF / AME** et **Exporter SWF**. L’export SWF contient les informations destinées au client : les cellules et les propriétés ci-dessous. Pour conserver les coordonnées, les équipes de combat et les paramètres serveur, utilisez aussi **Sauvegarder**, qui enregistre un projet AME Azur.

| Propriété SWF | Information dans Azur |
| --- | --- |
| `id` | Identifiant de carte |
| `width`, `height` | Dimensions de la grille |
| `backgroundNum` | Identifiant du fond |
| `ambianceId`, `musicId` | Ambiance et musique |
| `bOutdoor` | Carte extérieure |
| `capabilities` | Restrictions de téléportation, sauvegarde et combat |
| `mapData` | Dix caractères par cellule |

Une grille 15 × 17 comporte **479 cellules**, soit 4 790 caractères `mapData`. Le même calcul est utilisé par la grille graphique, les sauvegardes AME et les exports SWF/SQL. Les valeurs de `capabilities` supérieures à 15 sont conservées : les cartes de référence utilisent notamment 98. L'interface propose les quatre autorisations connues séparément et la valeur numérique complète pour modifier les autres bits sans en deviner le sens.

## Lecture

Les signatures **FWS** et **CWS** sont acceptées. Pour CWS, la décompression zlib vérifie la taille annoncée et la somme de contrôle. Le fichier et les données décompressées sont limités à 32 Mo. Les longueurs des tags et des actions, les terminateurs de chaînes et les balises de fin sont vérifiés.

Les propriétés sont récupérées par leur nom dans les affectations AVM1 de la première image, sans dépendre de l’ordre du pool de constantes ou du texte décompilé. Le lecteur reconnaît les littéraux, les références aux constantes sur 8 ou 16 bits, les registres simples, les affectations globales ou sur `this`/`_root`, et les concaténations de chaînes. Il reconnaît le préambule `System.security.allowDomain` utilisé par Astria, sans l’exécuter.

Ce lecteur n’est pas un interpréteur ActionScript général. Les branchements, appels arbitraires, fonctions et cartes animées comportant des actions après la première image sont refusés. AVM2 et la compression LZMA/ZWS ne sont pas pris en charge. Les données de carte chiffrées peuvent être importées ; l’ouverture demande leur clé, comme auparavant.

Une tuile de sol ou d’objet référencée et absente des ressources provoque une erreur indiquant la cellule et l’identifiant concernés. Les cellules précédentes et les données d’origine sont conservées si le décodage échoue.

## Écriture et conservation des cellules

L’export produit un **SWF 6 FWS binaire**, avec une image, un tag DoAction, des affectations AVM1 et les balises ShowFrame/End. Il ne nécessite pas Flasm. `mapData` est écrit en clair à partir des cellules actuelles. Les grandes chaînes sont réparties entre plusieurs actions Push puis concaténées, pour respecter la limite de 65 535 octets par action.

Le drapeau « cellule active », les huit valeurs de déplacement, la visibilité, l’élévation, la pente, les identifiants de tuiles, rotations, retournements et le drapeau interactif sont conservés. Les identifiants doivent tenir dans les champs du format client : sol de 0 à 2 047, objets de 0 à 16 383. Un export invalide ne remplace pas un fichier existant. La sauvegarde utilise un fichier temporaire dans le même dossier, puis un remplacement après écriture complète.

Le format **AME Azur 3** enregistre la grille complète. Le lecteur accepte aussi les versions 1 et 2 : leur ancienne grille avait une cellule de moins, qui est ajoutée vide à l'ouverture. La version 2 conserve les drapeaux actifs et les huit types de déplacement. Pour la version 1, le drapeau actif est récupéré depuis `mapData` lorsqu’il contient des cellules en clair ; sinon les cellules sont considérées actives. Les données absentes de ces anciennes sauvegardes ne peuvent pas être reconstituées. La compatibilité avec l’ancien AME d’Astria reste à développer.

## Validation et références

`MapSwfSmoke` vérifie les fixtures indépendantes FWS/CWS, un pool de plus de 256 constantes, le préambule de domaine, les propriétés réordonnées, les fichiers malformés, les imports concurrents, le cycle de sauvegarde/réouverture, les valeurs maximales de tuiles et les cartes de 19 801 cellules. Il vérifie aussi la lecture du SWF exporté par SwfDotNet, le décodage des cellules par le bot et leur conservation dans AME Azur 3. La suite conserve des fichiers de référence AME Azur 1 et 2.

Le pack « Ile Nowel » fourni par l'utilisateur contient 108 SWF CWS version 6 et 108 lignes SQL. Les 108 SWF s'importent et se réexportent avec leurs 51 732 cellules encodées inchangées. La suite garde la carte 10000 comme fixture et applique le SQL exporté pour celle-ci à une table Kryone de test. Le SQL du pack utilise un ancien `INSERT INTO maps VALUES (...)` à 13 colonnes, incompatible tel quel avec la table Kryone fournie ici à 18 colonnes. Azur exporte le SQL avec les **noms de colonnes du schéma configuré** et conserve les autres colonnes d'une carte déjà présente. Le pack lui-même diffère entre SWF et SQL sur la largeur de la carte 10009 et sur les autorisations des cartes 9955 et 9990 ; ses SWF sont utilisés comme référence pour les données client. Sa notice précise que 9955 et 9990 ont été recréées par l'auteur.

Les records SWF/AVM1 ont été implémentés à partir de la [spécification Adobe SWF 10](https://www.flashrealtime.com/content/dam/Adobe/en/devnet/swf/pdf/swf_file_format_spec_v10.pdf), notamment ActionPush, ActionConstantPool, ActionSetVariable et DoAction. Les noms de propriétés et le préambule sont documentés dans le [générateur Flasm d’Astria](https://github.com/quentinrozados/AstriaMapEditor/blob/master/AstriaMapEditor/SWF/Flasm.vb). Le nouveau sérialiseur est une implémentation indépendante.

**La lecture dans le client Dofus cible n’a pas été vérifiée.** Aucun client de jeu ni serveur auth/world complet de test n’est fourni dans le dossier. Cette validation reste nécessaire avant de qualifier les exports de compatibles en jeu. La suite ne valide pas non plus le chiffrement des exports, la compatibilité en jeu des [SWF d'objets](FORMAT_SWF_OBJETS.md) ou les anciens AME d’Astria.
