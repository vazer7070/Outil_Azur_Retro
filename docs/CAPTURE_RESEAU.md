# Capture réseau locale dans Azur

Le **Gestionnaire de ressources → Capture réseau** propose un relais TCP local de diagnostic. Il transmet les octets entre un client configuré pour l'utiliser et un serveur de test choisi. Il affiche les messages UTF-8 terminés par NUL sans modifier leur transmission. Il n'ouvre aucun port au démarrage d'Azur.

## Utilisation

1. Indiquez l'**adresse du serveur** et son **port TCP**. Le port proposé est 450, comme la configuration auth locale du bot ; adaptez-le à votre serveur.
2. Choisissez un **port local** libre, puis cliquez sur **Démarrer le relais**. Le relais écoute uniquement sur `127.0.0.1` et accepte au maximum 16 connexions simultanées.
3. Configurez le client de test pour joindre `127.0.0.1` et ce port local. Azur ne modifie pas automatiquement les fichiers du client ni les adresses annoncées par le serveur.
4. Choisissez le sens des messages ou recherchez du texte. Sélectionnez une ligne pour lire le message complet ; **Suivre le flux** reste sur les messages les plus récents.
5. **Exporter le journal** enregistre les messages filtrés actuellement en mémoire avec heure UTC, numéro de connexion et direction. Les retours à la ligne, tabulations et barres obliques inverses sont échappés. L'export est remplacé seulement après l'écriture complète.
6. Cliquez sur **Arrêter** pour fermer le relais et ses connexions. Le journal reste visible. Fermer le gestionnaire arrête également le relais.

La liste garde les 1 000 derniers messages, dans une limite de 2 Mo de texte. Si l'interface prend du retard, sa file d'attente conserve au maximum 500 messages et indique combien ont été ignorés. Ces limites concernent le journal ; les octets continuent à être transmis.

## Masquage des informations sensibles

Avant un succès d'authentification serveur reconnu (`ATK` ou `ALK`), tous les messages client → serveur sont remplacés par une indication de masquage. Les réponses de connexion et les messages dont les préfixes correspondent à des tickets, jetons ou mots de passe restent masqués ensuite. Une nouvelle négociation ou un échec reconnu réactive le masquage préalable. L'état est distinct pour chaque connexion.

Le masquage est une heuristique adaptée au protocole présent dans les sources. Il ne garantit pas l'anonymisation des extensions du serveur, du nom des personnages ou des discussions. La liste et les exports utilisent exclusivement les messages déjà masqués ; aucune option de l'interface ne révèle les messages d'authentification. Aucun journal n'est enregistré automatiquement.

## Limites

Le relais transporte du TCP. Il ne déchiffre pas TLS, n'injecte aucun message et ne redirige pas automatiquement une connexion auth vers un serveur de jeu annoncé par un paquet. Une connexion qui contourne l'adresse locale n'apparaît donc pas dans cette capture. Ce diagnostic local ne remplace pas une capture transparente de toutes les connexions du client.

Un message incomplet est accumulé jusqu'au NUL, avec une limite de 1 Mo par message. Si une direction fournit un texte UTF-8 invalide ou dépasse cette limite, son affichage est désactivé avec un diagnostic ; le transport des octets continue. Le relais applique un délai de connexion de 10 secondes et ferme les connexions inactives depuis deux minutes. Une fermeture du sens d'émission d'un client laisse le retour du serveur se terminer.

Les tests utilisent uniquement des programmes TCP sur la boucle locale et des messages fictifs. Ils vérifient les fragments, UTF-8, octets transmis dans les deux sens, masquage, connexions distinctes, fermeture partielle, arrêt, redémarrage et serveur indisponible. La validation avec le client et l'émulateur de jeu reste nécessaire. Voir [les tests](../tests/README.md) et [l'état du projet](ETAT_PROJET.md).
