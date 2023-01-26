using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Network;
using Tool_BotProtocol.Network.Enums;
using Tool_BotProtocol.Utils.Crypto;

namespace Tool_BotProtocol.Frames.Auth
{
    public class AccountLoginFrame : Frame
    {
        [MessageAttribution("HC")]
        public void GetWelcomeKey(TcpClient client, string message)
        {
            Accounts A = client.account;
            A.AccountStates = AccountStates.CONNECTED;
            A.WelcomeKey = message.Substring(2);

            client.SendPacket(GlobalConfig.VERSION);
            client.SendPacket(client.account.accountConfig.Account + "\n" + Hash.Crypt_Password(client.account.accountConfig.Password, client.account.WelcomeKey));
            client.SendPacket("Af");
        }
        [MessageAttribution("Af")]
        public void GetLoginQueue(TcpClient client, string message)
        {
            client.account.Logger.LogInfo("File d'attente", $"Position: {message[2]}/{message[4]}");
        }

        [MessageAttribution("Ad")]
        public void Getpseudo(TcpClient client, string message) => client.account.Name = message.Substring(2);

        [MessageAttribution("AH")]
        public void GetServerState(TcpClient client, string message)
        {
            Accounts A = client.account;
            string[] Serverlist = message.Substring(2).Split('|');
            GameServer G = A.Game.Server;
            bool first = true;
            foreach (string server in Serverlist)
            {
                if (!string.IsNullOrWhiteSpace(server))
                {
                    string[] vs = server.Split(';');
                    int id = int.Parse(vs[0].Trim());

                    ServerStates SS = (ServerStates)byte.Parse(vs[1].Trim());
                    if(id == A.accountConfig.Server_id)
                    {
                        G.RefreshData(id, $"{id}", SS);
                        A.Logger.LogInfo("LOGIN", $"Le serveur avec l'id {id} est {A.Game.Server.GetState(SS)}");

                        if(SS != ServerStates.ONLINE)
                            first = false;
                    }
                }
            }
            if (!first && G.ServerStates == ServerStates.ONLINE)
                client.SendPacket("Ax");
        }
        [MessageAttribution("AQ")]
        public void GetSecretQuestion(TcpClient client, string message)
        {
                client.SendPacket("Ax", true);
        }
        [MessageAttribution("AxK")]
        public void GetServerList(TcpClient client, string message)
        {
              Accounts A = client.account;
              string[] S = message.Substring(4).Split('|');
              int count = 1;
              A.AboTime = int.Parse(message.Substring(3).Split('|')[0]);

              while(count < S.Length)
              {
                 A.accountConfig.Servers.Add(S[count]);
                  count++;
              }

           // client.SendPacket($"AX{A.Game.Server.ServerID}", true);




        }
        [MessageAttribution("AXK")]
        public void GetServerSelection(TcpClient client, string message)
        {
            client.account.GameTicket = message.Substring(14);
            client.account.SwitchToGameServer($"{Hash.Decrypt_IP(message.Substring(3, 8))}:{Hash.Decrypt_Port(message.Substring(11, 3).ToCharArray())}");
        }
        [MessageAttribution("AYK")]
        public void GetServerSelectionRemaster(TcpClient client, string message)
        {
            string[] Data = message.Substring(3).Split(';');
            if(Data.Length != 0)
            {
                client.account.GameTicket = Data[1];
                client.account.SwitchToGameServer(Data[0]);
                client.account.Logger.LogInfo("[BOT]", "Connexion au world server");
            }
            else
            {
                client.account.Logger.LogError("[BOT]", "Redirection au world server impossible");
                client.account.Disconnect();
            }
        }

    }
}
