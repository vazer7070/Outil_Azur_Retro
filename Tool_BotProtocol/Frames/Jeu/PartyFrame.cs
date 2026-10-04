using System;
using System.Threading.Tasks;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Perso;
using Tool_BotProtocol.Game.Session;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
    /// <summary>
    /// Groupe : <c>PIK</c>, <c>PCK</c>, <c>PM</c>, <c>PV</c>.
    /// Extrait de <c>CharacterFrame</c> (lot S1) : seule l'invitation <c>PIK</c> change (plus d'acceptation automatique,
    /// événement <c>GameSession.PartyInviteReceived</c> à brancher) ; propriétaire : lot F1.
    /// </summary>
    internal class PartyFrame : Frame
    {
        /// <summary>
        /// <c>PIK&lt;invitant&gt;|&lt;invité&gt;</c> : StarLoco l'envoie à l'invitant et à l'invité. Comme <c>Party.onInvite</c> du
        /// client : noms manquants → refus <c>PR</c> ; invitant = soi → simple information ; invité = soi → l'invitation
        /// est proposée à <c>GameSession.PartyInviteReceived</c>. Le bot n'accepte jamais seul : sans abonné qui s'en
        /// charge, il refuse poliment (<c>PR</c>), sans délai qui bloquerait la file de réception.
        /// </summary>
        [MessageAttribution("PIK")]
        public Task GetGroup(TcpClient client, string message)
        {
            Accounts account = client.account;
            if (account == null) return Task.CompletedTask;
            string[] names = message.Substring(3).Split('|');
            if (names.Length < 2)
            {
                account.Logger.LogDanger("GROUPE", "Invitation de groupe incomplète : refusée.");
                return client.SendPacket("PR");
            }
            string inviter = names[0], target = names[1], self = account.Game.character.Name;
            if (string.IsNullOrEmpty(self)) return Task.CompletedTask;
            if (string.Equals(inviter, self, StringComparison.Ordinal))
            {
                account.Logger.LogInfo("GROUPE", "Invitation de groupe envoyée à " + target + " : en attente de sa réponse.");
                return Task.CompletedTask;
            }
            if (!string.Equals(target, self, StringComparison.Ordinal)) return Task.CompletedTask;
            if (account.Game.Session.OnPartyInvite(new InvitationEventArgs(inviter, string.Empty, target, string.Empty)))
            {
                account.Logger.LogInfo("GROUPE", inviter + " vous invite dans son groupe.");
                return Task.CompletedTask;
            }
            account.Logger.LogInfo("GROUPE", "Invitation de groupe de " + inviter + " refusée : le bot n'accepte pas d'invitation automatiquement.");
            return client.SendPacket("PR");
        }

        [MessageAttribution("PCK")]
        public void AcceptGroupe(TcpClient client, string message) => client.account.Game.character.InGroupe = true;

        [MessageAttribution("PM")]
        public void InGroupParse(TcpClient client, string message)
        {
            CharacterClass character = client.account.Game.character;
            foreach (string entry in message.Substring(2).TrimStart('|').Split('|'))
            {
                if (string.IsNullOrEmpty(entry)) continue;
                if (entry[0] == '-')
                {
                    if (int.TryParse(entry.Substring(1), out int removedId)
                        && character.GroupMembers.TryRemove(removedId, out string removedName))
                        character.InEquip.TryRemove(removedName, out bool ignored);
                    continue;
                }
                string[] parts = entry.TrimStart('+', '~').Split(';');
                if (parts.Length < 2 || !int.TryParse(parts[0], out int memberId)) continue;
                character.GroupMembers[memberId] = parts[1];
                character.InEquip[parts[1]] = false;
                character.InGroupe = true;
            }
        }
        [MessageAttribution("PV")]
        public void EjectGroup(TcpClient client, string message)
        {
            client.account.Game.character.InEquip.Clear();
            client.account.Game.character.GroupMembers.Clear();
            client.account.Game.character.InGroupe = false;
            client.account.Logger.LogError("GROUPE", $"{client.account.Game.character.EquipLeader} vous a éjecté du groupe.");
            client.account.Game.character.EquipLeader = "";
        }
    }
}
