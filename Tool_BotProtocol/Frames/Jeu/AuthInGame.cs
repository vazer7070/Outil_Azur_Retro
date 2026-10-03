using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Frames.Auth;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
     class AuthInGame : Frame
    {
        [MessageAttribution("M030")]
        public void NoConnexion(TcpClient client, string message)
        {
            AccountLoginFrame.Fail(client, "La connexion a expiré. Vérifiez la connexion puis retentez.");
        }

        [MessageAttribution("M031")]
        public void ErrorDNS(TcpClient client, string message)
        {
            AccountLoginFrame.Fail(client, "Le serveur de jeu ne dispose pas du ticket de connexion. Reconnectez-vous par le serveur Login.");
        }
        [MessageAttribution("M032")]
        public void FloodConnexion(TcpClient client, string message)
        {
            AccountLoginFrame.Fail(client, "Trop de tentatives de connexion. Patientez avant de recommencer.");
        }
    }
}
