using System;
using System.Collections.Generic;
using System.Globalization;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Chat;
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
        /// <c>AR&lt;base 36&gt;</c> : restrictions du personnage joué (<c>Account.onRestrictions</c>, bits de
        /// <c>dofus.datacenter.Player</c>). StarLoco envoie <c>AR6bk</c> (8192 : huit orientations, aucune interdiction) à la
        /// connexion et après une résurrection, et <c>AR3K</c> (128 : pas d'interaction avec les percepteurs) à toute
        /// transformation (<c>Player.setGfxId</c>), fantôme compris : la tombe se lit dans le sprite, pas ici.
        /// Une valeur négative, que StarLoco n'envoie jamais, est refusée (-1 signifie « pas encore reçu »).
        /// </summary>
        [MessageAttribution("AR")]
        public void Restrictions(TcpClient client, string message)
        {
            Accounts account = client.account;
            if (account == null) return;
            if (!PlayerRestrictionsParser.TryParse(message.Substring(2), out int raw, out PlayerRestrictions restrictions) || raw < 0)
            {
                account.Logger.LogDanger("SESSION", "Restrictions illisibles ignorées : " + message);
                return;
            }
            account.Game.Session.OnRestrictions(raw, restrictions);
            string described = Describe(raw);
            if (described.Length == 0)
                account.Logger.LogDebug("SESSION", "Aucune restriction du joueur (valeur " + raw + ").");
            else
                account.Logger.LogInfo("SESSION", "Restrictions du joueur (valeur " + raw + ") : " + described + ".");
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
            // StarLoco envoie deux fois AN pour un même gain (Player.addXp : levelUp, puis de nouveau à la fin de la boucle) :
            // le niveau déjà annoncé n'ouvre ni deuxième ligne ni deuxième boîte.
            if (account.Game.character.Level == level)
            {
                account.Logger.LogDebug("PERSO", "AN" + level + " répété : niveau déjà annoncé.");
                return;
            }
            account.Game.character.Level = (byte)level;
            account.Logger.LogInfo("PERSO", "Niveau " + level + " atteint.");
            // Le client ouvre la boîte NEW_LEVEL sans rien écrire au chat ; le bot y garde aussi une ligne, lisible après la boîte.
            account.Game.Chat?.AddLocal(ChatMessageKind.Info, ConnectionTexts.NewLevelLine(level));
            account.Game.Session.OnLevelUp(level);
        }

        /// <summary>Bits de <c>AR</c> dans le sens des accesseurs de <c>dofus.datacenter.Player</c> (posé = libellé).</summary>
        private static readonly KeyValuePair<int, string>[] PlayerBits =
        {
            new KeyValuePair<int, string>(1, "ne peut pas agresser"),
            new KeyValuePair<int, string>(2, "ne peut pas défier"),
            new KeyValuePair<int, string>(4, "ne peut pas échanger"),
            new KeyValuePair<int, string>(8, "peut attaquer"),
            new KeyValuePair<int, string>(16, "ne peut pas parler à tous"),
            new KeyValuePair<int, string>(32, "ne peut pas être marchand"),
            new KeyValuePair<int, string>(64, "ne peut pas utiliser d'objet"),
            new KeyValuePair<int, string>(128, "ne peut pas interagir avec un percepteur"),
            new KeyValuePair<int, string>(256, "ne peut pas utiliser d'objet interactif"),
            new KeyValuePair<int, string>(512, "ne peut pas parler aux PNJ"),
            new KeyValuePair<int, string>(4096, "peut attaquer les monstres de donjon en mutant"),
            new KeyValuePair<int, string>(8192, "huit orientations"),
            new KeyValuePair<int, string>(16384, "peut attaquer les monstres partout en mutant"),
            new KeyValuePair<int, string>(32768, "ne peut pas interagir avec un prisme"),
        };

        private static string Describe(int raw)
        {
            var parts = new List<string>();
            foreach (KeyValuePair<int, string> bit in PlayerBits)
                if ((raw & bit.Key) != 0) parts.Add(bit.Value);
            return string.Join(", ", parts);
        }
    }
}
