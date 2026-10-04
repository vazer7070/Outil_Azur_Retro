using System;
using System.Threading.Tasks;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Perso;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
    /// <summary>
    /// Groupe : <c>PIK</c>, <c>PCK</c>, <c>PM</c>, <c>PV</c>.
    /// Extrait de <c>CharacterFrame</c> sans changement de logique (lot S1) ; propriétaire : lot F1.
    /// </summary>
    internal class PartyFrame : Frame
    {
        [MessageAttribution("PIK")]
        public Task GetGroup(TcpClient client, string message) => Task.Run(async () =>
        {
            if (client.account.UseMasterCommands == true)
            {
                if (client.account.HasGroup == true)
                {
                    await Task.Delay(1250);
                    await client.SendPacket("PR");
                    client.account.Logger.LogInfo("GROUPE", "Vous êtes déjà dans un groupe, rejet de l'invitation.");

                }
                else if (client.account.IsGroupLeader == false)
                {
                    string PlayerWhoInvite = message.Substring(3).Split('|')[0];
                    Accounts Leader = client.account.Groupe.leader;
                    string LeaderName = Leader?.Game?.character?.Name;
                    if (string.IsNullOrEmpty(LeaderName)) { await client.SendPacket("PR"); return; }
                    if (PlayerWhoInvite.ToLower() == LeaderName.ToLower())
                    {

                        await Task.Delay(550);
                        await client.account.Connexion.SendPacket("PA");
                        client.account.Logger.LogInfo("GROUPE", $"Je suis maintenant dans le groupe de {LeaderName}");
                    }
                    else
                    {
                        await client.SendPacket("PR");
                        client.account.Logger.LogInfo("GROUPE", "Rejet de l'invitation.");
                    }

                }
                else if (message.Substring(3).Split('|').Length == 1)
                {
                    await Task.Delay(1250);
                    await client.SendPacket("PR");
                    client.account.Logger.LogInfo("GROUPE", "Rejet de l'invitation.");
                }
            }
            else
            {
                if (client.account.Game.character.InGroupe == true)
                {
                    await Task.Delay(1250);
                    await client.SendPacket("PR");
                    client.account.Logger.LogInfo("GROUPE", "Vous êtes déjà dans un groupe, rejet de l'invitation.");

                }
                else
                {
                    await client.account.Connexion.SendPacket("PA");
                }
            }
        });
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
