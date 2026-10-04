using System;
using System.Globalization;
using System.Threading.Tasks;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Session;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
    /// <summary>
    /// Guilde : invitations <c>gJR</c> (envoyée) et <c>gJr</c> (reçue). Extrait de <c>CharacterFrame</c> (lot S1), avec le
    /// refus <c>gJE&lt;id&gt;</c> du client (matrice §2 n° 36) ; propriétaire : lot F3.
    /// </summary>
    internal class GuildFrame : Frame
    {
        /// <summary>
        /// <c>gJR&lt;nom&gt;</c> : confirmation, côté invitant, que l'invitation est partie (<c>Guild.onRequestLocal</c>).
        /// Ce n'est pas une invitation reçue : aucune réponse n'est envoyée.
        /// </summary>
        [MessageAttribution("gJR")]
        public void GuildInviteSent(TcpClient client, string message)
        {
            client.account?.Logger.LogInfo("GUILDE", "Invitation de guilde envoyée à " + message.Substring(3) + " : en attente de sa réponse.");
        }

        /// <summary>
        /// <c>gJr&lt;id invitant&gt;|&lt;nom&gt;|&lt;guilde&gt;</c> : invitation reçue (<c>Guild.onRequestDistant</c>). Elle est proposée à
        /// <c>GameSession.GuildInviteReceived</c> ; sans abonné qui s'en charge, le bot la refuse comme le client :
        /// <c>gJE&lt;id invitant&gt;</c>, l'identifiant étant ce que StarLoco compare (<c>GameClient.invitationGuild</c>).
        /// </summary>
        [MessageAttribution("gJr")]
        public Task GuildInviteReceived(TcpClient client, string message)
        {
            Accounts account = client.account;
            if (account == null) return Task.CompletedTask;
            string[] fields = message.Substring(3).Split('|');
            string inviterId = fields[0];
            if (!int.TryParse(inviterId, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                account.Logger.LogDanger("GUILDE", "Invitation de guilde sans identifiant d'invitant ignorée : " + message);
                return Task.CompletedTask;
            }
            string inviter = fields.Length > 1 ? fields[1] : inviterId, guild = fields.Length > 2 ? fields[2] : string.Empty;
            if (account.Game.Session.OnGuildInvite(new InvitationEventArgs(inviter, inviterId, string.Empty, guild)))
            {
                account.Logger.LogInfo("GUILDE", inviter + " vous invite dans la guilde " + guild + ".");
                return Task.CompletedTask;
            }
            account.Logger.LogInfo("GUILDE", "Invitation de " + inviter + " dans la guilde " + guild + " refusée : le bot n'accepte pas d'invitation automatiquement.");
            return client.SendPacket("gJE" + inviterId);
        }
    }
}
