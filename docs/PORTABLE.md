# Azur portable — Windows x64

Documentation mise à jour le **2 octobre 2026**. Les compilations et les **29 tests** sont validés en Debug et Release avant la construction du paquet.

Décompressez tout le dossier `Azur` dans un emplacement où vous pouvez écrire, puis lancez `Outil_Azur_complet.exe` depuis ce dossier. Le programme utilise .NET Framework 4.8. Les ressources de cartes et du bot sont incluses ; gardez-les à côté de l'exécutable.

Le dossier `docs` contient les guides des éditeurs, de compatibilité kauth et des formats SWF, ainsi que l'état du projet. Le [guide de capture réseau](CAPTURE_RESEAU.md) décrit le relais local, le masquage et les limites. `tests/README.md` décrit les vérifications à lancer depuis les sources du dépôt.

Le [guide du bot StarLoco](BOT_STARLOCO.md) décrit la grande carte, les portraits, les fiches refermables, les ressources XML et les actions manuelles disponibles. Le [brief de reprise](BRIEF_REDESIGN_UI_BOT.md) précise les références visuelles et les fonctions encore à compléter. Dans **Bot → Connexion → Serveur et protocole**, configurez votre adresse et vos ports ; le préréglage local propose Login `450`, Game `5555` et version `1.34.1`. Le kit StarLoco s'utilise séparément et n'est pas lancé automatiquement.

Au premier démarrage, Azur crée `config.json`, `auth/auth_tables.json` et `world/world_tables.json` avec des valeurs locales par défaut. Renseignez vos connexions dans **Configuration** avant d'utiliser les éditeurs liés à la base. Le paquet ne contient aucun mot de passe, aucune base utilisateur ni les exports précédents.

Le module bot crée sa propre configuration `ressources/Bot/BotConfig.json`. L'enregistrement des comptes est facultatif : les nouveaux mots de passe sont protégés avec Windows DPAPI pour l'utilisateur courant. Ces fichiers et le dossier `AccountSingle` sont exclus du paquet, même s'ils existent dans votre dossier Release. Une mise à niveau doit préserver séparément les réglages et comptes que vous souhaitez garder ; les mots de passe protégés ne sont pas portables entre utilisateurs Windows.

Pour Kryone, configurez **auth** sur kauth (`accounts`, `players` et ressources statiques) et **world** sur kworld (`items`). Les nouveaux JSON possèdent les correspondances corrigées. Lors d'une mise à niveau, les JSON conservés gardent vos choix : vérifiez **Configuration → Configurer les tables → Correspondances** à l'aide du [guide de compatibilité kauth](KAUTH_COMPATIBILITE.md). La valeur world historique `personnages → characterinstance` n'est pas utilisée par les éditeurs Kryone, qui lisent les personnages dans auth.

`SHA256SUMS.txt` liste les empreintes SHA-256 de l'exécutable, des bibliothèques et des ressources du paquet. Ce paquet portable ne prouve pas encore la compatibilité des exports SWF avec un client de jeu réel. Les mises à jour automatiques historiques sont désactivées tant qu'elles ne possèdent pas de manifeste signé ; utilisez une nouvelle archive complète pour mettre à niveau une installation portable, en conservant vos trois fichiers JSON de configuration et vos créations.

Pour reconstruire et contrôler l'archive depuis la racine du dépôt après une compilation Release :

```powershell
.\tools\Build-PortableRelease.ps1 -OutputPath .\Releases\Azur-portable.zip
python .\tests\verify_portable.py .\Releases\Azur-portable.zip
```

Le constructeur stocke directement les images PNG/JPG/JPEG déjà compressées dans l'archive et compresse les autres fichiers. Il évite ainsi de recompresser toute la bibliothèque de tuiles, ce qui accélère la création du paquet sans modifier les images ni leurs empreintes.

Les fichiers `kauth.sql` et `kworldsave.sql` ne sont pas ajoutés au paquet portable. Les essais SQL utilisent des schémas temporaires et des données synthétiques ; la présence des fixtures dans les sources ne signifie pas qu'une base de jeu est installée avec le logiciel.

Le kit de test StarLoco Login/Game et Dofus 1.34 fourni dans `F:\kit` s'utilise séparément. Les essais réseau et en jeu restent à réaliser ; les correspondances SQL Kryone/kauth ne constituent pas une adaptation des bases StarLoco.
