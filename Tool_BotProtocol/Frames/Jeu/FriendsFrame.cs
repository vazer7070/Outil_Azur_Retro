using System;
using System.Threading.Tasks;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Social;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
    /// <summary>
    /// Amis et ennemis (propriétaire : lot F2) : préfixes <c>FL</c>, <c>FA</c> (<c>FAK</c>/<c>FAE</c>), <c>FD</c>
    /// (<c>FDK</c>/<c>FDE</c>), <c>FS</c>, <c>FO</c>, <c>iL</c>, <c>iA</c> (<c>iAK</c>/<c>iAE</c>) et <c>iD</c> (<c>iDK</c>/<c>iDE</c>),
    /// aiguillés sur leurs deux premières lettres comme le client 1.34 (familles <c>F</c> et <c>i</c> de <c>dofus.aks</c>), puis
    /// confiés au service <see cref="FriendsActions"/> du compte (<c>Game.Interactions.Friends</c>). <c>FJS</c> et <c>FJC±</c> ne
    /// sont que des envois. Un paquet mal formé est journalisé, jamais propagé.
    /// </summary>
    internal class FriendsFrame : Frame
    {
        /// <summary><c>FL|&lt;compte;état;nom;niveau;alignement;guilde;sexe;gfx&gt;|…</c> : liste d'amis.</summary>
        [MessageAttribution("FL")]
        public void FriendsList(TcpClient client, string message) => Apply(client, message, friends => friends.OnFriendsListPacket(message));

        /// <summary><c>FAK&lt;ligne&gt;</c> : ami ajouté ; <c>FAE&lt;f|y|a|m&gt;</c> : ajout impossible.</summary>
        [MessageAttribution("FA")]
        public void AddFriend(TcpClient client, string message) => Apply(client, message, friends => friends.OnFriendAddPacket(message));

        /// <summary><c>FDK</c> : ami retiré (la liste est redemandée) ; <c>FDEf</c> : joueur introuvable.</summary>
        [MessageAttribution("FD")]
        public Task RemoveFriend(TcpClient client, string message) => Run(client, message, friends => friends.OnFriendRemovePacket(message));

        /// <summary><c>FS&lt;nom|gfx|c1|c2|c3|carte|niveau|combat|suivi&gt;</c> : conjoint (envoyé avec <c>FL</c>).</summary>
        [MessageAttribution("FS")]
        public void Spouse(TcpClient client, string message) => Apply(client, message, friends => friends.OnSpousePacket(message));

        /// <summary><c>FO+</c> / <c>FO-</c> : avertissement à la connexion d'un ami.</summary>
        [MessageAttribution("FO")]
        public void Notify(TcpClient client, string message) => Apply(client, message, friends => friends.OnNotifyPacket(message));

        /// <summary><c>iL|&lt;ligne&gt;|…</c> : liste d'ennemis.</summary>
        [MessageAttribution("iL")]
        public void EnemiesList(TcpClient client, string message) => Apply(client, message, friends => friends.OnEnemiesListPacket(message));

        /// <summary><c>iAK&lt;ligne&gt;</c> : ennemi ajouté (format partiel de StarLoco toléré) ; <c>iAE…</c> : ajout impossible.</summary>
        [MessageAttribution("iA")]
        public void AddEnemy(TcpClient client, string message) => Apply(client, message, friends => friends.OnEnemyAddPacket(message));

        /// <summary><c>iDK</c> : ennemi retiré (la liste est redemandée) ; <c>iDE…</c> : retrait impossible.</summary>
        [MessageAttribution("iD")]
        public Task RemoveEnemy(TcpClient client, string message) => Run(client, message, friends => friends.OnEnemyRemovePacket(message));

        private static void Apply(TcpClient client, string message, Action<FriendsActions> handler)
        {
            FriendsActions friends = client?.account?.Game?.Interactions?.Friends;
            if (friends == null || message == null) return;
            try { handler(friends); }
            catch (Exception error)
            {
                client.account?.Logger?.LogError("AMIS", "Paquet d'amis illisible ignoré (" + Preview(message) + ") : " + error.Message);
            }
        }

        private static async Task Run(TcpClient client, string message, Func<FriendsActions, Task> handler)
        {
            FriendsActions friends = client?.account?.Game?.Interactions?.Friends;
            if (friends == null || message == null) return;
            try { await handler(friends).ConfigureAwait(false); }
            catch (Exception error)
            {
                client.account?.Logger?.LogError("AMIS", "Paquet d'amis illisible ignoré (" + Preview(message) + ") : " + error.Message);
            }
        }

        private static string Preview(string message) => message.Length > 80 ? message.Substring(0, 80) + "…" : message;
    }
}
