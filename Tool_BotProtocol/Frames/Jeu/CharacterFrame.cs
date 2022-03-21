using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Groupes;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
    class CharacterFrame : Frame
    {
        [MessageAttribution("PIK")]
        public void GetGroup(TcpClient client, string message)
        {
           if(client.account.UseMasterCommands == true)
            {
                if (client.account.HasGroup == true)
                {
                    Task.Delay(1250);
                    client.SendPacket("PR");
                    client.account.Logger.LogInfo("GROUPE", "Vous êtes déjà dans un groupe, rejet de l'invitation.");

                }
                else if (client.account.IsGroupLeader == false)
                {
                    string PlayerWhoInvite = message.Substring(3).Split('|')[0];
                    Accounts Leader = client.account.Groupe.leader;
                    string LeaderName = Leader.Game.character.Name;
                    if (PlayerWhoInvite.ToLower() == LeaderName.ToLower())
                    {

                        Task.Delay(550);
                        client.account.Connexion.SendPacket("PA");
                        client.account.Logger.LogInfo("GROUPE", $"Je suis maintenant dans le groupe de {LeaderName}");
                    }
                    else
                    {
                        client.SendPacket("PR");
                        client.account.Logger.LogInfo("GROUPE", "Rejet de l'invitation.");
                    }

                }
                else if (message.Substring(3).Split('|').Length == 1)
                {
                    Task.Delay(1250);
                    client.SendPacket("PR");
                    client.account.Logger.LogInfo("GROUPE", "Rejet de l'invitation.");
                }
            }
            else
            {
                if (client.account.Game.character.InGroupe == true)
                {
                    Task.Delay(1250);
                    client.SendPacket("PR");
                    client.account.Logger.LogInfo("GROUPE", "Vous êtes déjà dans un groupe, rejet de l'invitation.");

                }
                else
                {
                    client.account.Connexion.SendPacket("PA");
                }
            }
        }
        [MessageAttribution("PCK")]
        public void AcceptGroupe(TcpClient client, string message) => client.account.Game.character.InGroupe = true;

        [MessageAttribution("PM")]
        public void InGroupParse(TcpClient client, string message)
        {
            string chief = "";
            string member = "";
            if (client.account.Game.character.InGroupe == true && client.account.Game.character.InEquip.Count > 0)
            {
                foreach(string l in message.Split('|'))
                {
                    string w = l.Split(';')[1];
                    member = w;
                    if (!client.account.Game.character.InEquip.ContainsKey(l))
                        client.account.Game.character.InEquip.TryAdd(l, false);
                }
                client.account.Logger.LogError("GROUPE", $"Ajout de {member} dans le groupe.");
            }
            else
            {
                foreach (string s in message.Split('|'))
                {
                    string j = s.Split(';')[1];
                    if (client.account.Game.character.InEquip.Count > 0)
                        client.account.Game.character.InEquip.TryAdd(j, false);
                    else
                    {
                        client.account.Game.character.InEquip.TryAdd(j, true);
                        chief = j;
                        client.account.Game.character.EquipLeader = chief;
                    }
                }
                client.account.Logger.LogError("GROUPE", $"Vous êtes dans le groupe de {chief}.");
            }
        }
        [MessageAttribution("PV")]
        public void EjectGroup(TcpClient client, string message)
        {
            client.account.Game.character.InEquip.Clear();
            client.account.Game.character.InGroupe = false;
            client.account.Logger.LogError("GROUPE", $"{client.account.Game.character.EquipLeader} vous a éjecté du groupe.");
            client.account.Game.character.EquipLeader = "";
        }
        [MessageAttribution("pong")]
        public void GetPingPong(TcpClient client, string message) => client.account.Logger.LogInfo("DOFUS", $"Ping: {client.GetPing()} ms");

        [MessageAttribution("Bp")]
        public void GetAllPing(TcpClient client, string message) => client.SendPacket($"Bp{client.GetPingAverage()}|{client.GetTotalPings()}|50");
        [MessageAttribution("gJR")]
        public void HandleGuild(TcpClient client, string message)
        {
            if(client.account.Game.character.HasGuild == true)
            {
                Task.Delay(100);
                client.account.Logger.LogInfo("PERSO", "Invitation à la guilde refusée");
                client.SendPacket("gJE");
            }
        }


    }
}
