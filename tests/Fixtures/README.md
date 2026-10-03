# Fixtures de référence Azur

`azur-v1.ame` a été généré avec le binaire Release d’Azur version AME 1, avant le passage au format 2. Il contient une carte vide d’identifiant 777, de dimensions 3 × 4 et de 17 cellules. Il vérifie que les anciennes sauvegardes restent lisibles ; il ne s’agit pas d’un fichier AME d’Astria.

`azur-v2.ame` contient une carte Azur de version 2 et vérifie l'ajout de la dernière cellule manquante lors de sa lecture. Ces deux fixtures AME ne valident pas le format historique d'Astria.

Les fixtures SWF synthétiques sont assemblées directement dans `MapSwfSmoke.cs`, indépendamment du sérialiseur de production. Elles utilisent les noms de propriétés publiés par Astria, un pool de constantes, des affectations dans un ordre différent, des nombres et booléens, ainsi que des variantes FWS/CWS.

`10000_0612041200.swf` est une carte de référence du pack « Ile Nowel » fourni par l'utilisateur, avec Kenzhu indiqué comme auteur du pack. Ce fichier est inclus dans les fixtures pour contrôler les 479 cellules, les autorisations 98 et la conservation des données à l'export. Il ne constitue pas un client Dofus complet et les tests ne vérifient pas son ouverture dans le jeu.

`kauth-schema.sql` reprend uniquement les **58 définitions de tables InnoDB** du fichier `F:\Téléchargements\kauth.sql` fourni. Il ne contient ni `INSERT`, ni `REPLACE`, ni compte ou personnage réel. `KauthSchemaIntegrationSmoke` crée séparément des données synthétiques dans une base temporaire. Les comptes, mots de passe et autres données privées du fichier d'origine ne sont jamais distribués avec ces fixtures.
