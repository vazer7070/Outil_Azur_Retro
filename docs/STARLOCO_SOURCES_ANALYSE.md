# Analyse des sources StarLoco pour le bot

Analyse réalisée à partir des sources Java présentes dans `F:\kit` le 1 octobre 2026.

## Périmètre exact

- Login : `F:\kit\03 - Emulateur StarLoco\03 - Login\src\org\starloco\locos` — 33 fichiers Java.
- Game : `F:\kit\03 - Emulateur StarLoco\04 - Game\src\org\starloco\locos` — 273 fichiers Java.
- Total : 306 fichiers Java, avec les classes compilées et les JAR correspondants dans les dossiers `bin`/`out`.

Les 306 fichiers sont inventoriés. Les classes de connexion, de routage, d’actions, de cartes et leurs dépendances ont été lues pour cette analyse ; ce document ne revendique pas une revue exhaustive de chaque ligne des 306 fichiers.

Le chemin Game réel contient bien le préfixe `03 - Emulateur StarLoco`; un chemin qui commencerait directement par `F:\kit\04 - Game` n’existe pas.

## Architecture observée

```text
Client Dofus 1.34
       │ protocole texte MINA, lignes terminées par NUL/\n
       ▼
LoginServer : port 450
       │ relais binaire/texte séparé, trames terminées par #
       ▼
ExchangeServer Login : 127.0.0.1:666
       ▲
       │ ExchangeClient Game
       ▼
GameServer : port 5555 en profil local
       │
       ├── base statique/login : comptes, joueurs, serveurs, groupes, maisons…
       └── base dynamique/game : cartes, monstres, objets, sorts, quêtes…
```

Le Login ne transmet pas directement le mot de passe au Game. Il charge le compte, vérifie son état, demande au Game d’attendre l’identifiant du compte, puis remet au client l’adresse du Game et cet identifiant.

## Protocole Login client

### Ouverture de session

`LoginServer` utilise Apache MINA `TextLineCodecFactory` en UTF-8 : l’encodeur termine par NUL et le décodeur attend `\n\0`. Le bot envoie donc `\n\0` et lit les trames serveur jusqu’au NUL. Les handlers serveur découpent aussi les lignes internes.

À la création de session, `LoginClient` envoie immédiatement :

1. une politique Flash cross-domain permissive ;
2. `HC` suivi d’une clé aléatoire de 32 lettres minuscules ;
3. passage à l’état `WAIT_VERSION`.

Le client envoie ensuite sa version. `Version.verify` contient le contrôle de version, mais celui-ci est entièrement commenté : n’importe quelle version passe actuellement à l’étape suivante.

### Compte et mot de passe

États de `LoginClient.Status` : `WAIT_VERSION`, `WAIT_ACCOUNT`, `WAIT_PASSWORD`, `WAIT_NICKNAME`, `SERVER`.

Dans `PacketHandler` :

- état `WAIT_ACCOUNT` : le paquet doit faire au moins trois caractères ; `AccountName.verify` charge `accounts.account` en minuscules ;
- état `WAIT_PASSWORD` : `Password.verify` déchiffre le mot de passe avec la clé `HC` ;
- état `WAIT_NICKNAME` : `ChooseNickName.verify` crée le pseudo si le compte n’en possède pas ;
- état `SERVER` : les commandes `AF`, `Af`, `AX`, `Ax` et `BA` sont dispatchées.

Le mot de passe envoyé par le client peut commencer par `#1`. Le déchiffrement utilise l’alphabet Dofus `a-zA-Z0-9-_` et la clé de 32 caractères. La vérification en base est exactement :

```text
SHA-512(MD5(mot_de_passe_déchiffré))
```

Le résultat est comparé en hexadécimal minuscule à `accounts.pass`. Ce format StarLoco ne doit pas être confondu avec les formats MD5/SHA512 éventuellement utilisés par Kryone.

### Réponses Login importantes

`AccountQueue.sendInformation` peut envoyer :

- `Af0|0|0|1|-1` ;
- `Ad<pseudo>` ;
- `Ac0` ;
- `AH<id>;<état>;110;1|...` pour la liste des serveurs ;
- `AlK0` ou `AlK1` selon le statut MJ ;
- `AQ<question secrète>`.

Si le compte n’a pas encore de pseudo, le serveur envoie `AlEr` et passe en attente de surnom. Les erreurs principales sont `AlEf` (compte/mot de passe invalide), `AlEb` (banni), `AlEk<jours>|<heures>|<minutes>` (bannissement temporaire), `AlEd` (personnage déjà connecté), `AlEs` (pseudo invalide ou déjà utilisé) et `AlEv<version>` si le contrôle de version est réactivé.

### Liste et sélection de serveur

- `Ax` côté client → `ServerList.get` → `AxK<abonnement>|<serverId>,<nombreDePersonnages>|...`.
- `AF<nom>` → liste des personnages du compte par serveur, réponse `AF...`.
- `AX<serverId>` → `ServerSelected.get`.

Pour `AX`, le Login vérifie l’existence et l’état du serveur, les places et l’abonnement. Il envoie ensuite au Game :

```text
WA<guidCompte>#
```

Puis au client :

```text
AYK<ipGame>:<portGame>;<guidCompte>
```

Le client doit donc ouvrir une nouvelle connexion Game et envoyer `AT<guidCompte>`.

## Relais Login ↔ Game

### Connexion Game vers Login

`ExchangeServer` écoute sur `127.0.0.1:666` en profil local. À la connexion, le Login envoie `SK?`.

Le Game (`ExchangePacketHandler`) répond :

```text
SK<serverId>;<serverKey>;<placesLibres>
```

Le Login valide la clé et répond `SKK#` ou `SKR#`. Le Game envoie ensuite :

```text
SH<ipPubliqueGame>;<portGame>
```

Le Login répond `SHK#` et passe le serveur à l’état disponible. Le Game reçoit aussi `F?` et répond `F<nombreDePlaces>`.

### Messages de session

- `WA<guid>#` : le Login demande au Game de placer le compte en attente ; le Game charge le compte et l’ajoute à `waitingClients`.
- `WK<guid>#` : le Login demande au Game de déconnecter le compte.
- `DI1`, `DI2`, `DI3`, `DI4`, `DI5` : attribution d’identifiants pour montures, objets, quêtes de joueur, guildes et familiers.
- `DM...#` : message/broadcast transmis aux autres serveurs.

Le relais utilise des `IoBuffer` bruts et le séparateur `#`. Il n’y a pas de tampon de trames partagé : une trame partielle ou plusieurs trames regroupées dans un même buffer peuvent être mal découpées.

## Protocole Game client

`GameServer` utilise lui aussi MINA en UTF-8 avec codec texte. À l’ouverture, `GameClient` envoie `HG`.

### Authentification Game

1. Le client reçoit `AYK<ip>:<port>;<guid>` du Login.
2. Il se connecte au port Game.
3. Le Game envoie `HG`.
4. Le client envoie `AT<guid>`.
5. `sendTicket` récupère le compte dans `waitingClients`, marque le compte connecté et répond `ATK0`.
6. Le client demande la liste avec `AL` puis choisit un personnage avec `AS<id>`.

Après sélection, `Player.OnJoinGame` et `sendGameCreate` envoient notamment `GCK`, les statistiques `As...`, les métiers, canaux, sorts, restrictions, puis `GDM|<mapId>|<date>|<key>` et les informations de carte.

### Familles de paquets entrants

Le premier caractère est dispatché par `GameClient.parsePacket` :

| Préfixe | Domaine |
|---|---|
| `A` | compte, personnages, cadeaux, ticket |
| `B` | commandes de base, date, chat général, état absent/invisible |
| `C` / `c` | conquête et canaux |
| `D` / `d` | dialogue et documents |
| `E` | échanges, banque, HDV, crafts, montures |
| `e` | direction et émotes |
| `F` | amis |
| `f` | liste et options de combats |
| `G` | actions de jeu, déplacement, sorts, combat, actions de carte |
| `g` | guilde |
| `h` / `K` | maison et code de coffre |
| `i` | ennemis |
| `J` | métiers |
| `O` | objets/inventaire |
| `P` | groupe |
| `Q` | quêtes |
| `R` | montures |
| `S` | sorts |
| `T` | foire/troll |
| `W` | zaaps, zaapis et déplacements par waypoint |

### Déplacement et combat

Les actions `GA` sont décodées comme suit : `packet.substring(2,5)` contient un identifiant numérique sur trois caractères.

- `GA001<chemin>` : déplacement ; le chemin est composé de triplets direction + code de cellule.
- `GA300<sortId>;<cellId>` : lancer un sort.
- `GA303<cellId>` : attaque au corps à corps.
- `GA500<cellId>;<actionId>` : action sur cellule/interactif.
- `GA900<guid>` : demander un duel.
- `GA901<guid>` : accepter un duel.
- `GA902<guid>` : refuser/annuler un duel.
- `GA903<combatId>` : rejoindre un combat ; vérifier les paramètres supplémentaires du handler pour le type de combat.
- `GA906<guid>` : agresser.
- `GA909...` : action collector.
- `GA912...` : attaque de prisme.

Les autres commandes de combat importantes sont `fD` (détails), `fL` (liste), `fH` (aide), `fN` (verrouillage d’équipe), `fP` (groupe uniquement) et `fS` (spectateurs).

### Chemins de cellules

`CryptManager` définit l’alphabet cellule Dofus :

```text
abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_
```

Une cellule est encodée sur deux caractères (`cellId / 64`, `cellId % 64`). Un déplacement contient des triplets :

```text
<direction><codeCelluleSurDeuxCaractères>
```

`PathFinding.isValidPath` vérifie chaque segment, la marche, les obstacles, les tacles, les pièges et les cellules occupées. Les directions de grille sont `a` à `h`; les directions utilisables en combat sont restreintes.

### Cartes

`MapData.load` charge dans la table `maps` :

```text
id, date, width, heigth, key, places, mapData, monsters,
mappos, numgroup, fixSize, minSize, maxSize, forbidden, sniffed
```

`CryptManager.decompileMapData` lit `mapData` par blocs de dix caractères et reconstruit pour chaque cellule : active, niveau, marchabilité, ligne de vue, pente et objet de couche. Les SWF du client et la colonne SQL `mapData` sont donc deux représentations liées mais distinctes de la carte.

## Bases réellement utilisées par le Game

La base dite « statique » pointe vers `Main.loginHostDB`, `Main.loginNameDB` et charge notamment :

```text
accounts, players, servers, groups, commands, banip, areas, subareas,
guilds, houses, trunks, mounts, mountparks, objects, obvijevans,
pubs, pets, quest player data
```

La base dynamique pointe vers `Main.hostDB`, `Main.nameDB` et charge notamment :

```text
maps, mobgroups_fix, monsters, npc*, item_template, itemsets,
objectsactions, crafts, drops, experience, jobs_data, sorts,
quest_data, quest_etapes, quest_objectifs, zaaps, zaapi,
interactive_objects_data, interactive_doors, houses, banks,
guild_members, hdvs, gifts, mounts et données de monde
```

Le bot doit donc séparer les données de compte/personnage de la base du monde. Le code StarLoco n’utilise pas le schéma Kryone tel quel.

## Anomalies et corrections prioritaires

1. **Boucle Game qui perd des paquets** — `GameHandler.messageReceived` utilise `do { ... } while (i == s.length - 1)`. Avec trois lignes ou plus dans un même buffer, seule la première peut être traitée. La condition doit parcourir `i < s.length` et ignorer les lignes vides.
2. **Attribut MINA incohérent dans le relais** — `ExchangeHandler.sessionCreated` utilise `setAttachment(new ExchangeClient(...))`, mais `sessionClosed` et `ExchangeServer.getClients` cherchent `getAttribute("client")`. Le suivi des serveurs et la remise à zéro d’état peuvent donc être faux.
3. **Découpage de trames fragile** — le relais Login/Game traite des buffers bruts et sépare par `#`, sans conserver les fragments incomplets.
4. **Contrôle de version désactivé** — `Version.verify` accepte n’importe quel client tant que le bloc commenté n’est pas restauré.
5. **Gestion de session pré-auth fragile** — `LoginClient.kick` et `LoginHandler.sessionClosed` utilisent `getAccount()` sans garde ; une déconnexion avant la fin du login peut provoquer une exception.
6. **SQL construit par concaténation dans Login** — `AccountData` concatène les noms, pseudos, adresses IP et identifiants dans les requêtes. Le correctif concerne le serveur. Le bot réseau n’a pas à ouvrir sa base de comptes.
7. **Test de bannissement IP probablement erroné** — `AccountData.isBanned` compare la chaîne littérale `'ip'` au lieu de référencer correctement la colonne.
8. **Secrets historiques dans `Config.java`** — les profils distants contiennent des identifiants et adresses publiques codés en dur ; ils doivent être supprimés et remplacés par une configuration de test.
9. **Liste de serveurs partiellement fictive** — `Server.getHostList` émet des valeurs de capacité codées en dur (`110;1`) au lieu de refléter toutes les valeurs dynamiques.
10. **Validation de ticket minimale** — `AT<guid>` repose sur l’identifiant de compte placé temporairement dans `waitingClients`; ce n’est pas un ticket signé. Le bot reproduit cette séquence et masque le ticket dans son journal. Une expiration ou une signature nécessite une évolution du serveur et de son protocole.

## Ce que le bot peut implémenter à partir de ces sources

Le socle est maintenant suffisamment explicite pour écrire les adaptateurs suivants sans deviner les paquets :

1. Connexion Login : recevoir `HC`, chiffrer le mot de passe clair avec cette clé, traiter `AxK` et `AYK`. Le hash SHA512(MD5) concerne la vérification côté serveur ; il ne faut pas envoyer ce hash comme mot de passe réseau.
2. Le relais `SK/SH/WA/WK` appartient exclusivement à la liaison entre serveurs. Le bot se connecte aux ports publics Login puis Game ; il ne doit pas implémenter un client Exchange d’administration.
3. `StarLocoGameClient` : `HG`, `AT`, `AL`, `AS`, `GA`, `G*`, `f*`, `E*`, `O*`, `P*`, `W*`.
4. `DofusCellCodec` : conversion cellule numérique ↔ alphabet Dofus et génération de chemins par triplets.
5. `StarLocoMapState` : lecture de `GDM`, `GM`, `GDF`, `GDO`, `GDC`, objets interactifs, monstres et joueurs.
6. `StarLocoFightState` : état `GJK`, placements, tours, `GA`, sorts, corps à corps, fin de combat.

La refonte du 2 octobre 2026 s’appuie sur ces classes : dispatch attendu dans l’ordre avec préfixe le plus long, Login/Game, redirections DNS/IPv6, millisecondes d’abonnement, cartes et interactions. Les journaux de diagnostic masquent les identifiants et les tickets. Les tests emploient des échanges fictifs sur boucle locale ; ils ne remplacent pas la validation en jeu. Voir le guide `docs/BOT_STARLOCO.md` dans Azur.
