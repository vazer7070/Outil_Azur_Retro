using System;
using System.Collections.Generic;
using System.Globalization;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Perso;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
    /// <summary>
    /// Session de jeu : création de partie <c>GCK</c>, restrictions <c>AR</c>, communauté <c>Ac</c>, horloge <c>BT</c>,
    /// accusé vide <c>BN</c> et passage de niveau <c>AN</c>, lus comme <c>dofus.aks.Account/Basics/Game</c> du client 1.34.
    /// Propriétaire : lot S1.
    /// </summary>
    internal class SessionFrame : Frame
    {
        /// <summary>
        /// <c>GCK|&lt;type&gt;|&lt;nom&gt;</c> : réponse de StarLoco à <c>GC1</c> (<c>Player.sendGameCreate</c>). Le client lit
        /// le type à partir du cinquième caractère et n'accepte que 1 (partie solo) ; le nom n'est pas utilisé.
        /// </summary>
        [MessageAttribution("GCK")]
        public void GameCreated(TcpClient client, string message)
        {
            Accounts account = client.account;
            if (account == null) return;
            string[] fields = message.Length > 4 ? message.Substring(4).Split('|') : new string[0];
            if (fields.Length == 0 || !int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int type) || type != 1)
            {
                account.Logger.LogError("SESSION", "Création de partie illisible ou d'un type inattendu : " + message);
                return;
            }
            account.Game.Session.OnGameCreated(type, fields.Length > 1 ? fields[1] : string.Empty);
            account.Logger.LogInfo("SESSION", "Partie créée par le serveur de jeu.");
        }

        /// <summary>
        /// <c>AR&lt;base 36&gt;</c> : restrictions du personnage joué (<c>Account.onRestrictions</c>). StarLoco envoie
        /// <c>AR6bk</c> (8192 : aucune restriction connue du client) et <c>AR3K</c> (128 : tombe).
        /// </summary>
        [MessageAttribution("AR")]
        public void Restrictions(TcpClient client, string message)
        {
            Accounts account = client.account;
            if (account == null) return;
            if (!PlayerRestrictionsParser.TryParse(message.Substring(2), out int raw, out PlayerRestrictions restrictions))
            {
                account.Logger.LogDanger("SESSION", "Restrictions illisibles ignorées : " + message);
                return;
            }
            account.Game.Session.OnRestrictions(raw, restrictions);
            if (restrictions == PlayerRestrictions.None)
                account.Logger.LogDebug("SESSION", "Aucune restriction visible (valeur " + raw + ").");
            else
                account.Logger.LogInfo("SESSION", "Restrictions du personnage : " + Describe(restrictions) + ".");
        }

        /// <summary><c>Ac&lt;communauté&gt;</c> (envoyé par le Login StarLoco : <c>Ac0</c>) ; une valeur négative est ignorée comme dans le client.</summary>
        [MessageAttribution("Ac")]
        public void Community(TcpClient client, string message)
        {
            Accounts account = client.account;
            if (account == null) return;
            if (int.TryParse(message.Substring(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out int community))
                account.Game.Session.OnCommunity(community);
            else
                account.Logger.LogDebug("SESSION", "Communauté illisible ignorée : " + message);
        }

        /// <summary>
        /// <c>BT&lt;millisecondes&gt;</c> : horloge du serveur (<c>Basics.onReferenceTime</c>). StarLoco ne répond à <c>BD</c>
        /// que par <c>BT</c> (jamais <c>BD</c>, matrice §2 n° 7) et l'envoie aussi seul : il est consommé sans rien attendre.
        /// </summary>
        [MessageAttribution("BT")]
        public void ServerTime(TcpClient client, string message)
        {
            Accounts account = client.account;
            if (account == null) return;
            if (long.TryParse(message.Substring(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out long milliseconds))
                account.Game.Session.OnServerTime(milliseconds);
            else
                account.Logger.LogDebug("SESSION", "Horloge du serveur illisible ignorée : " + message);
        }

        /// <summary><c>BN</c> : accusé sans contenu que StarLoco envoie après de nombreuses actions ; rien à faire.</summary>
        [MessageAttribution("BN")]
        public void Nothing(TcpClient client, string message) { }

        /// <summary><c>AN&lt;niveau&gt;</c> : nouveau niveau (<c>Account.onNewLevel</c> met à jour le niveau du joueur).</summary>
        [MessageAttribution("AN")]
        public void NewLevel(TcpClient client, string message)
        {
            Accounts account = client.account;
            if (account == null) return;
            if (!int.TryParse(message.Substring(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out int level) || level < 1 || level > byte.MaxValue)
            {
                account.Logger.LogDanger("PERSO", "Nouveau niveau illisible ignoré : " + message);
                return;
            }
            account.Game.character.Level = (byte)level;
            account.Logger.LogInfo("PERSO", "Niveau " + level + " atteint.");
            account.Game.Session.OnLevelUp(level);
        }

        private static string Describe(PlayerRestrictions restrictions)
        {
            var parts = new List<string>();
            if ((restrictions & PlayerRestrictions.CannotBeAssaulted) != 0) parts.Add("ne peut pas être agressé");
            if ((restrictions & PlayerRestrictions.CannotBeChallenged) != 0) parts.Add("ne peut pas être défié");
            if ((restrictions & PlayerRestrictions.CannotExchange) != 0) parts.Add("ne peut pas échanger");
            if ((restrictions & PlayerRestrictions.CannotBeAttacked) != 0) parts.Add("ne peut pas être attaqué");
            if ((restrictions & PlayerRestrictions.ForceWalk) != 0) parts.Add("marche forcée");
            if ((restrictions & PlayerRestrictions.Slow) != 0) parts.Add("ralenti");
            if ((restrictions & PlayerRestrictions.CannotSwitchToCreatureMode) != 0) parts.Add("mode créature interdit");
            if ((restrictions & PlayerRestrictions.Tomb) != 0) parts.Add("tombe");
            if ((restrictions & PlayerRestrictions.AdminSonicSpeed) != 0) parts.Add("vitesse d'administrateur");
            return string.Join(", ", parts);
        }
    }
}
