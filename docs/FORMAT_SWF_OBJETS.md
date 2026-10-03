# Objets du client : SWF binaires et modification d'une copie

## Créer un objet

Dans **Objets → Création et inventaires**, complétez la fiche, l'arme et les conditions. Dans **Exportation**, choisissez SQL, SWF du client ou les deux.

La rubrique **Fichier client** propose deux usages :

- sans intégration : un SWF binaire contenant uniquement le nouvel objet ; ce fichier sert à vérifier ou transférer cette définition et ne remplace pas une bibliothèque complète d'objets ;
- avec intégration : sélectionnez un SWF d'objets existant. Azur valide son contenu, ajoute la définition et demande un autre nom de fichier. Les autres objets et les balises d'origine sont conservés.

La **source ActionScript .txt** est facultative. Le SWF est déjà compilé, sans outil externe. Le fichier texte décrit uniquement l'objet créé.

Le décodage, la compilation et le choix de destination précèdent l'injection SQL. Annuler le choix de destination annule cette création avant toute écriture SQL. L'enregistrement SQL et l'export de fichiers ne forment pas une transaction commune : une erreur de disque après la validation SQL est signalée explicitement. La fiche peut ensuite être exportée avec **SWF du client** uniquement, sans recréer le modèle serveur.

Un identifiant déjà présent dans le SWF ne peut pas être ajouté une seconde fois. Pour le modifier, utilisez l'éditeur ci-dessous.

## Modifier les objets d'un fichier client

**Objets → Objets du client** fonctionne aussi sans connexion SQL.

1. Cliquez sur **Ouvrir un SWF…**, puis choisissez le fichier qui contient `I.u`.
2. Recherchez un nom ou un identifiant et sélectionnez la fiche.
3. Modifiez nom, description, apparence, type, niveau, poids, prix, conditions, forge-magie, utilisation ou arme.
4. Passez aux autres fiches : leurs modifications sont conservées en attente.
5. Cliquez sur **Enregistrer une copie…**. Toutes les fiches modifiées sont exportées ensemble. Le résultat devient ensuite le fichier de référence de la fenêtre.

Les données serveur et client sont indépendantes : changer le niveau ou le type dans cette fenêtre ne modifie pas automatiquement le modèle SQL. Leur cohérence doit être maintenue avec l'éditeur de modèles.

Toutes les propriétés présentes dans la fiche restent accessibles. Les six paramètres numériques connus de `e` sont présentés séparément. Les éléments supplémentaires de l'arme, `ep` et les propriétés propres à une variante sont affichés comme paramètres techniques, avec leur nom exact ; leur sens n'est pas inventé. Les booléens se choisissent avec Oui/Non. Les valeurs nulles, nombres et textes gardent leur type. Les tableaux et objets supplémentaires sont modifiables en JSON, sans exécution de code.

**Annuler les changements** retire tous les brouillons non exportés. Une saisie invalide empêche de changer de fiche ou d'exporter ; corrigez-la ou annulez les changements. La fermeture signale les modifications en attente.

## Format et limites

Les fichiers acceptés sont les SWF AVM1 FWS ou CWS, version 6 ou supérieure, à une image, contenant une table `I.u`. Azur interprète uniquement les données littérales : constantes, nombres, textes UTF-8, booléens, valeurs nulles, objets/tableaux, constructeurs Object/Array sans argument, registres et affectations. Il n'exécute jamais l'ActionScript importé.

Les appels de fonctions, embranchements, boucles, AVM2, clips, scripts animés et autres variantes sont refusés avec une explication. L'export est FWS, même si l'origine est compressée. Les données décompressées et les balises d'origine sont conservées ; un bloc d'affectations est ajouté avant l'affichage de l'image. Seuls l'en-tête de taille et ces nouvelles affectations changent.

Limites : fichier décompressé de 32 Mo, chaîne de 60 000 octets UTF-8 sans caractère nul, pile de 4 096 valeurs, structures de fiche imbriquées sur 64 niveaux. Les identifiants peuvent être espacés sans allocation d'un tableau de cette taille. Les exports sont écrits dans un fichier temporaire, puis remplacés atomiquement après validation.

L'encodage des actions suit les instructions et l'ordre de pile de la [spécification SWF publiée par Adobe](https://www.flashrealtime.com/content/dam/Adobe/en/devnet/swf/pdf/swf_file_format_spec_v10.pdf).

## Vérification et validation encore nécessaire

Les tests vérifient le SWF avec SwfDotNet, un lecteur indépendant, ainsi qu'un fichier de référence assemblé séparément. Ils couvrent Unicode, ordre des paramètres d'arme, constantes/registres, import compressé, propriétés supplémentaires, plusieurs fiches, ajout sans perte, conservation des balises, identifiants déjà présents, corruption et conservation du fichier lors d'un export invalide. Les tests graphiques modifient la fiche et relisent les valeurs dans le SWF exporté.

**La lecture dans le client de jeu cible reste à vérifier.** Aucun SWF d'objets du client cible n'est fourni dans ce dossier. Testez une copie de sa bibliothèque, le chargement des objets existants et nouveaux, leur affichage et leur utilisation avant d'installer le résultat. Le bon nom de fichier, sa version, le cache et le chargement dépendent du client utilisé ; Azur ne les déduit pas d'un fichier isolé.
