# Inventaire de `F:\kit`

> La version détaillée est également conservée dans le dossier de travail avec les manifestes CSV/JSON : `C:\Users\kishi\Documents\Codex\2026-09-14\explique-moi-ce-projet-en-quoi\`.

Inventaire physique réalisé le **1 octobre 2026**. Les fichiers ont été parcourus pour identifier leur rôle, mais les valeurs de mots de passe, clés, comptes, licences et données personnelles ne sont pas recopiées dans ce document.

## Vue d’ensemble

| Élément | Valeur |
|---|---:|
| Fichiers | 41 280 |
| Dossiers | 433 |
| Volume total | 1 092 272 583 octets (environ 1,02 Go décimal) |
| Ressources `.swf` | 38 028 (355 124 686 octets) |
| Sources `.java` | 306 |
| Classes compilées `.class` | 710 |
| Archives Java `.jar` | 31 |
| Dumps SQL | 2 (58 265 807 octets au total) |

Le manifeste exhaustif recense chaque fichier par chemin relatif, taille, extension, date de modification, composant racine et indicateur de présence possible d’un élément sensible. Le manifeste des dossiers inclut aussi les dossiers vides.

## Arborescence fonctionnelle

### `03 - Emulateur StarLoco` — 23 285 fichiers, 159 934 664 octets

Ce dossier contient le serveur Java StarLoco et son jeu de ressources Dofus 1.34.

| Sous-dossier | Contenu | Fichiers | Taille |
|---|---|---:|---:|
| `01 - Lang` | Ressources client : cartes, documentation, tutoriels, fichiers de langue et SWF | 20 209 | 60 767 657 o |
| `02 - BDD` | Dumps MySQL du serveur login et du serveur game | 2 | 58 265 807 o |
| `03 - Login` | Serveur d’authentification, serveur d’échange, sources, JAR et bibliothèques | 1 912 | 17 147 749 o |
| `04 - Game` | Serveur de jeu, sources, monde, combats, IA, bases statiques/dynamiques, JAR et bibliothèques | 1 161 | 23 753 111 o |
| `Vos compte initiaux.txt` | Fichier de comptes initiaux ; données sensibles à traiter comme un secret | 1 | 340 o |

#### Ressources `01 - Lang`

`01 - Lang\dofus` reprend une arborescence de ressources client :

- `docs` : 804 fichiers, dont 802 SWF et 2 images ;
- `lang` : 537 fichiers, principalement des SWF (526), plus texte/XML/HTML/PHP ;
- `maps` : 18 811 SWF de cartes, avec des noms du type `id_version.swf` ;
- `tutorials` : 57 SWF.

Ces ressources sont très proches de celles livrées dans le client Dofus du dossier `04`. Il faut les considérer comme une bibliothèque de référence et vérifier les doublons/versions avant de les importer dans Azur.

#### Base de données `02 - BDD`

- `login.sql` — 6 314 512 octets, snapshot Navicat daté du 7 décembre 2021, 27 tables et 37 480 instructions `INSERT`. Tables repérées : comptes, joueurs, serveurs, bannissements, maisons, coffres, montures, guildes, boutiques et données de site.
- `game.sql` — 51 951 295 octets, snapshot Navicat daté du 7 décembre 2021, 47 tables et 72 414 instructions `INSERT`. Tables repérées : cartes, monstres, PNJ, objets, sorts, crafts, drops, quêtes, combats, montures, métiers, maisons, zaaps/zaapis et données statiques/dynamiques du monde.

Ces dumps contiennent des enregistrements réels ou historiques, dont des comptes et des personnages. Les utiliser uniquement sur une instance MySQL de test isolée ; ne pas les importer directement dans une base de production.

#### Serveur login `03 - Login`

Fichiers importants :

- `login.jar` : exécutable du serveur login ;
- `Login.bat` : démarre le JAR avec `java -Xmx1024M -jar login.jar -o true` ;
- `config.properties` : ports login/échange, version client, connexion MySQL et URL web ; la configuration observée utilise le loopback pour le login et MySQL ;
- `src\org\starloco\locos\kernel\Main.java` et `Config.java` : démarrage, chargement de configuration et contrôle des paramètres ;
- `src\...\login\LoginServer.java` et `LoginHandler.java` : protocole d’authentification ;
- `src\...\exchange\ExchangeServer.java` et handlers : relais entre login et game ;
- `src\...\login\packet\` : paquets de nom de compte, file d’attente, liste de serveurs, sélection de serveur et réponse de serveur ;
- `src\...\database\` : accès et modèles de données login ;
- `libs\` : Apache MINA 2.0.7, HikariCP, pilote MySQL, Logback/SLF4J, Typesafe Config, Commons CLI/Lang/Logging, Javassist ;
- `bin`, `out`, `.idea`, `Logs` : sorties de compilation, projet IDE et anciens journaux.

La configuration observée expose un login local sur le port `450` et un échange local sur le port `666`. Les champs de connexion SQL sont présents dans `config.properties` : les valeurs doivent rester hors du dépôt et être remplacées par une configuration locale de test.

#### Serveur game `04 - Game`

Fichiers importants :

- `game.jar` : exécutable du serveur game ;
- `Game.bat` : démarre le JAR avec `java -Xmx1024M -jar game.jar -o true` ;
- `config.txt` : identifiant de serveur, clé de serveur, mode debug/log, taux d’expérience/drop/métiers/kamas/FM et options de monde ;
- `src\org\starloco\locos\kernel\Main.java` et `Config.java` : initialisation du monde, chargement des bases et lancement du serveur ;
- `src\...\game\GameServer.java` et `GameHandler.java` : serveur et protocole game ;
- `src\...\game\world\World.java` et `game\scheduler\` : monde, sauvegarde, annonces et tâches planifiées ;
- `src\...\area\map\` : cartes, cellules, entités de carte et labyrinthes ;
- `src\...\fight\` : combats, tours, sorts, pièges et 82 classes d’IA ;
- `src\...\entity\`, `object\`, `job\`, `quest\`, `hdv\`, `exchange\` : objets métier et systèmes de jeu ;
- `src\...\database\statics\` et `database\dynamics\` : accès aux données statiques et aux données du monde ;
- `libs\` : MINA 2.0.9, HikariCP, pilote MySQL, Logback/SLF4J, Joda-Time, Jep, Jansi, Google Translate, Typesafe Config, Commons et Javassist ;
- `README.md` : modèle Bitbucket non renseigné, donc il ne documente pas réellement le déploiement.

Point de sécurité prioritaire : `src\...\kernel\Config.java` contient une branche de configuration avec des identifiants SQL et une adresse IP publique écrits en dur. Il faut supprimer ces valeurs du code, les déplacer vers un fichier local ignoré par Git ou des variables d’environnement, puis faire tourner le serveur avec une base de test.

### `04 - Dofus 1.34 - Qu'Tan et Ilyzaelle` — 17 867 fichiers, 306 279 642 octets

C’est un client Dofus 1.34 complet accompagné de ses ressources locales.

| Élément | Rôle | Fichiers | Taille |
|---|---|---:|---:|
| `Dofus.exe` | Exécutable client | 1 | 4 154 692 o |
| `cache.exe` | Composant cache | 1 | 528 384 o |
| `config.xml` | Configuration locale utilisée | 1 | 1 620 o |
| `config-ori.xml` | Configuration d’origine avec CDN Ankama | 1 | 1 570 o |
| `D1ElectronLauncher.html` | Lanceur HTML | 1 | 902 o |
| `loader.swf`, `preloader.swf` | Chargement client | 2 | 1 084 877 o |
| `data\maps` | Cartes locales | 9 223 | 23 154 502 o |
| `data\docs` | Documentation SWF/images | 804 | 1 403 452 o |
| `data\tutorials` | Tutoriels SWF | 66 | 52 067 o |
| `clips` | Bibliothèque d’éléments d’interface et d’animations | 7 736 | 186 650 994 o |
| `audio` | Ressources audio | 2 | 80 499 294 o |
| `modules` | Modules client | 2 | 1 629 673 o |
| `styles` | Feuilles CSS | 6 | 2 149 o |
| `loadingbanners` | 16 bannières de chargement | 16 | 7 049 682 o |

Métadonnées exécutables observées : `Dofus.exe` est signé comme produit Dofus d’Ankama (version de fichier générique `1.0.0.0`), `cache.exe` est le nettoyeur de cache Dofus `1.29.1.1` et Navicat est la version `16.1.8.0`.

`config.xml` est préparé pour le test local : connexion au login sur `127.0.0.1:450`, données distantes locales sous `http://127.0.0.1/dofus/` et repli sur `data/`. `config-ori.xml` conserve le mode d’origine avec le CDN public et un paramètre de connexion Zaap. C’est le fichier à utiliser comme référence pour comparer le comportement du client, sans le mélanger avec la configuration de test.

### `PremiumSoft` — 127 fichiers, 468 474 821 octets

`PremiumSoft\Navicat Premium 16` est une distribution complète de Navicat :

- `navicat.exe` et `navicat.exe.BAK` ;
- DLL de connectivité MySQL/MariaDB, PostgreSQL, SQLite, SSH/Kerberos et bibliothèques graphiques ;
- `instantclient_11_2`, `resource`, certificats et fichiers de support ;
- `navicat.pdf` ;
- `license.txt` et fichiers de désinstallation.

Le dossier peut servir à administrer les bases de test. Le fichier de licence et les éventuels profils enregistrés ne doivent pas être copiés dans un dépôt ni une archive de partage.

### Installateur XAMPP

`xampp-windows-x64-8.2.12-0-VS16-installer.exe` est l’installateur XAMPP 8.2.12 (157 583 456 octets). Il peut fournir Apache/PHP/MySQL ou MariaDB pour héberger les données et éventuellement `data/` en HTTP local. Aucun service n’a été démarré pendant l’inventaire.

## Chaîne de fonctionnement déduite

```text
Dofus.exe / config.xml
        │ TCP 127.0.0.1:450
        ▼
StarLoco Login (login.jar)
        │ TCP échange 127.0.0.1:666
        ▼
StarLoco Game (game.jar)
        │
        ├── MySQL login.sql : comptes/authentification
        ├── MySQL game.sql  : monde, cartes, objets, combats, quêtes…
        └── data/ + ressources SWF locales
```

Cette chaîne est une lecture des configurations et des sources ; elle ne constitue pas une validation de bout en bout. Il reste à tester avec des copies de bases, des ports dédiés et un compte synthétique.

## Points à corriger ou à vérifier avant intégration dans Azur

1. **Secrets et données personnelles** : sortir la clé du serveur, les identifiants SQL codés dans Java, les mots de passe de configuration, `Vos compte initiaux.txt`, les dumps SQL et les licences des archives de tout paquet partageable.
2. **Configuration unique** : remplacer les fichiers `.bat` et les configurations dispersées par un profil `test` explicite (login, game, MySQL, HTTP data), sans adresse publique par défaut.
3. **Dumps datés** : comparer les 27 tables login et 47 tables game avec le schéma Kryone/Azur actuel ; vérifier encodage, moteurs MyISAM, clés, auto-incréments et présence de données historiques.
4. **Ressources de cartes** : les deux bibliothèques contiennent des variantes de SWF ; indexer l’identifiant de carte et la version de fichier pour éviter qu’Azur choisisse une mauvaise variante.
5. **JAR et sources** : distinguer clairement les sources, les classes compilées et les JAR publiés. Recompiler dans un répertoire propre avant toute adaptation et documenter la version Java attendue.
6. **Dépendances** : verrouiller les versions divergentes de MINA/SLF4J entre login et game et vérifier la compatibilité avec le JDK utilisé par Azur.
7. **Client local** : conserver `config-ori.xml` comme référence historique, mais utiliser une copie de `config.xml` dédiée au test ; ne pas basculer par erreur vers le CDN public.
8. **Documentation** : remplacer le `README.md` générique de StarLoco par une procédure française : ordre de démarrage, ports, import SQL, création d’un compte de test, arrêt propre et dépannage.
9. **Nettoyage** : isoler les journaux, dossiers IDE (`.idea`), sorties `bin/out` et anciennes sauvegardes `.BAK` afin qu’ils ne soient pas pris pour la source de vérité.

## Fichiers d’inventaire associés

- `outputs\F_KIT_MANIFEST.csv` : 41 280 lignes, une par fichier.
- `outputs\F_KIT_DIRECTORIES.csv` : 433 lignes, une par dossier, y compris les dossiers vides.
- `outputs\F_KIT_SUMMARY.json` : compteurs et regroupements machine-readable.
- `scripts\Inventaire-FKit.ps1` : script reproductible qui régénère les trois fichiers.
- `scripts\Inspect-KitText.ps1` : extraction non destructive des tables SQL, paquets Java et racines de sources.

Les manifestes décrivent les chemins et métadonnées sans recopier les contenus. Pour une analyse détaillée ultérieure, le modèle doit commencer par ce document, puis ouvrir les fichiers ciblés sous `F:\kit` en gardant les éléments signalés sensibles hors des réponses et des commits.
