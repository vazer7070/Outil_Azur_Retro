using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Auth
{
     class AuthFrame :Frame
    {
        [MessageAttribution("AlEf")]
        public void WrongCredential(TcpClient client, string message)
        {
            client.account.Logger.LogError("[LOGIN]", "Connexion rejetée. Nom de compte ou mot de passe incorrect.");
            client.account.Disconnect();
        }
        [MessageAttribution("AlEa")]
        public void AlreadyConnected(TcpClient client, string message)
        {
            client.account.Logger.LogError("[LOGIN]", "Vous êtes déjà connecté.");
            client.account.Disconnect();
        }
        [MessageAttribution("AlEv")]
        public void WrongVersion(TcpClient client, string message)
        {
            client.account.Logger.LogError("[LOGIN]", $"La version {GlobalConfig.VERSION} n'est pas correcte.");
            client.account.Disconnect();
        }
        [MessageAttribution("AlEd")]
        public void PlayerAlreadyInGame(TcpClient client, string message)
        {
            client.account.Game.Server.DisplayErrorConnected();
            client.account.Disconnect();
        }
        [MessageAttribution("AlEk")]
        public void AccountBanned(TcpClient client, string message)
        {
            string[] BanInfo = message.Substring(3).Split('|');
            int days = int.Parse(BanInfo[0].Substring(1)), hours = int.Parse(BanInfo[1]), min = int.Parse(BanInfo[2]);
            StringBuilder SB = new StringBuilder().Append("Votre compte sera invalide pendant ");
            if (days > 0)
                SB.Append($"{days} jour(s)");
            if (hours > 0)
                SB.Append($"{hours} heure(s)");
            if (min > 0)
                SB.Append($"{min} minute(s)");
            client.account.Logger.LogError("[LOGIN]", SB.ToString());
            client.account.Disconnect();
        }
    }
}
