using System.Collections.Generic;
using System.Linq;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Session;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
    /// <summary>Messages du serveur <c>Im</c>. Propriétaire : lot S1.</summary>
    internal class MiscFrame : Frame
    {
        /// <summary>
        /// <c>Im&lt;type&gt;&lt;id&gt;[;a~b~c][|&lt;id&gt;[;…]]</c>, lu comme <c>Infos.onMessage</c> du client 1.34 : type 0 = information
        /// (<c>INFOS_&lt;id&gt;</c>), 1 = erreur (<c>ERROR_&lt;id&gt;</c>), 2 = JcJ (<c>PVP_&lt;id&gt;</c>). Un seul gestionnaire pour
        /// tous les identifiants : le texte vient des fichiers de langue (<see cref="ServerMessages.Resolver"/>), sinon le
        /// repli « Im&lt;type&gt;&lt;id&gt; » et les paramètres sont affichés. Aucune réponse automatique n'est envoyée.
        /// </summary>
        [MessageAttribution("Im")]
        public void ReceiveMessage(TcpClient client, string message)
        {
            Accounts account = client.account;
            if (account == null) return;
            IReadOnlyList<ServerMessage> messages = ServerMessages.Parse(message);
            if (messages.Count == 0)
            {
                account.Logger.LogDebug("DOFUS", "Message Im vide ou d'un type inconnu ignoré : " + message);
                return;
            }
            // The client joins every entry of one packet into a single line.
            string text = string.Join(" ", messages.Select(entry => entry.Text));
            switch (messages[0].Kind)
            {
                case ServerMessageKind.Error: account.Logger.LogError("DOFUS", text); break;
                case ServerMessageKind.Pvp: account.Logger.LogDanger("DOFUS", text); break;
                default: account.Logger.LogInfo("DOFUS", text); break;
            }
            account.Game.Session.OnServerMessages(messages);
        }
    }
}
