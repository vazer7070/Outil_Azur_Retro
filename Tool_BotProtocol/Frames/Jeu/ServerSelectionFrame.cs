using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
    internal class ServerSelectionFrame : Frame
    {
        [MessageAttribution("HG")]
        public void WelcomeInGame(TcpClient client, string message) => client.SendPacket($"AT{client.account.GameTicket}");

        [MessageAttribution("ATK0")]
        public void ServerSelected(TcpClient client, string message)
        {
            client.SendPacket("Ak0");
            client.SendPacket("AV");
        }

        [MessageAttribution("AV0")]
        public void List_Perso(TcpClient client, string message)
        {
            client.SendPacket("Ages");
            client.SendPacket("AL");
            client.SendPacket("Af");
        }

        [MessageAttribution("ALK")]
        public void Perso_Selection(TcpClient client, string message)
        {
            Accounts A = client.account;
            A.AccountCharactersInfo.Clear();
            string[] S = message.Substring(3).Split('|');
            int count = 2;
            while(count < S.Length)
            {
                string[] S2 = S[count].Split(';');
                int id = int.Parse(S2[0]);
                string name = S2[1];
                int lvl = int.Parse(S2[2]);
                int GfxId = int.Parse(S2[3]);

                A.AccountCharactersInfo.Add($"{name}|{lvl}|{GfxId}|{id}");

              /*  if(name.ToLower().Equals(A.accountConfig.player.ToLower()) || string.IsNullOrEmpty(A.accountConfig.player))
                {
                    client.SendPacket($"AS{id}", true);
                    found = true;
                }*/
                count++;
            }
        }
        [MessageAttribution("BT")]
        public void GetServerTime(TcpClient client, string message) => client.SendPacket("GI");

        [MessageAttribution("ASK")]
        public void HaveSelectedPerso(TcpClient client, string message)
        {
            Accounts A = client?.account;
            string[] S = message.Substring(4).Split('|');

            int id = int.Parse(S[0]);
            string name = S[1];
            byte level = byte.Parse(S[2]);
            byte ID_Race = byte.Parse(S[3]);
            byte Sex = byte.Parse(S[4]);

            A.Game.character.SetPerso_Data(id, name, level,Sex,ID_Race);
            A.Game.character.Inventory.Add_Items(S[9]);

            client.SendPacket("GC1");

            A.Game.character.PersoSelectedEvent();
            A.Game.character.AFK_Timer.Change(1200000, 1200000);
            client.account.AccountStates = AccountStates.CONNECTED_INACTIVE;
        }

    }
}
