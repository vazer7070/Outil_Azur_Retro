using Org.BouncyCastle.Crypto.Engines;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
    internal class ServerSelectionFrame : Frame
    {
        [MessageAttribution("HG")]
        public Task WelcomeInGame(TcpClient client, string message) => Task.Run(async () => await client.SendPacket($"AT{client.account.GameTicket}"));

        [MessageAttribution("ATK0")]
        public Task ServerSelected(TcpClient client, string message) => Task.Run(async () =>
        {
            await client.SendPacket("Ak0");
            await client.SendPacket("AV");
        });
        [MessageAttribution("ATK")]
        public Task ServerSelectionned(TcpClient client, string message) => Task.Run(async () =>
        {
            await client.SendPacketAsync("AV");
        });
        [MessageAttribution("AV0")]
        public Task List_Perso(TcpClient client, string message) => Task.Run(async () =>
        {
            await client.SendPacket("Ages");
            await client.SendPacket("AL");
            await client.SendPacket("Af");
        });

        [MessageAttribution("ALK")]
        public Task Perso_Selection(TcpClient client, string message) => Task.Run(async () =>
        {

            Accounts A = client.account;
            A.AccountCharactersInfo.Clear();
            string[] S = message.Substring(3).Split('|');
            int count = 2;
            int idconnected = 0;
            while (count < S.Length)
            {
                string[] S2 = S[count].Split(';');
                int id = int.Parse(S2[0]);
                string name = S2[1];
                int lvl = int.Parse(S2[2]);
                int GfxId = int.Parse(S2[3]);

                A.AccountCharactersInfo.TryAdd(id, $"{name}|{lvl}|{GfxId}|");

                if (name.Equals(A.Game.Server.NameNewCharacter))
                {
                    idconnected = id;
                }
                count++;
            }
            if (!A.Game.Server.ExitCreationMenu)
                A.Game.Server.AddCharacterMenu();
            else
            {
                await A.Connexion.SendPacket($"AS{idconnected}");
                await A.Connexion.SendPacket("AF");
            }
        });
        [MessageAttribution("BT")]
        public Task GetServerTime(TcpClient client, string message) => Task.Run(async () => await client.SendPacket("GI"));

        [MessageAttribution("ASK")]
        public Task HaveSelectedPerso(TcpClient client, string message) => Task.Run(async () =>
        {
            Accounts A = client?.account;
            string[] S = message.Substring(4).Split('|');

            int id = int.Parse(S[0]);
            string name = S[1];
            byte level = byte.Parse(S[2]);
            byte ID_Race = byte.Parse(S[3]);
            byte Sex = byte.Parse(S[4]);

            A.Game.character.SetPerso_Data(id, name, level, Sex, ID_Race);
            A.Game.character.Inventory.Add_Items(S[9]);
            A.Game.character.PersoSelectedEvent();
            A.Game.character.AFK_Timer.Change(1200000, 1200000);
            client.account.AccountStates = AccountStates.CONNECTED_INACTIVE;

            await client.SendPacketAsync("BYA");
            await client.SendPacket("GC1");

            
        });

    }
}
