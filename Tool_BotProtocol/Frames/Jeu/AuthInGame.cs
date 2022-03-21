using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
     class AuthInGame : Frame
    {
        [MessageAttribution("M030")]
        public void NoConnexion(TcpClient client, string message)
        {
            client.account.Logger.LogError("[LOGIN]", "La connexion a expirée, veuillez vérifier votre connexion");
            client.account.Disconnect();
        }

        [MessageAttribution("M031")]
        public void ErrorDNS(TcpClient client, string message)
        {
            client.account.Logger.LogError("[LOGIN]", "Connexion rejetée, le serveur n'a pas les informations pour mener à bien l'authentification");
            client.account.Disconnect();
        }
        [MessageAttribution("M032")]
        public void FloodConnexion(TcpClient client, string message)
        {
            client.account.Logger.LogError("[LOGIN]", "Trop de tentatives de connexion, merci de patienter avant de recommencer.");
            client.account.Disconnect();
        }
    }
}
