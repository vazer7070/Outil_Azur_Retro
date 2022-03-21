using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
    class ChatFrame : Frame
    {
        [MessageAttribution("cC+")]
        public void AddCanal(TcpClient client, string message) => client.account.Game.character.AddCanalPlayer(message.Substring(3));
        [MessageAttribution("cC-")]
        public void DeleteCanal(TcpClient client, string message) => client.account.Game.character.DeleteCanalPlayer(message.Substring(3));
        [MessageAttribution("cMK")]
        public void GetTchat(TcpClient client, string message)
        {
            string[] part = message.Substring(3).Split('|');
            string canal = string.Empty;
            switch (part[0])
            {
                case "?":
                    canal = "RECRUTEMENT";
                    break;

                case ":":
                    canal = "COMMERCE";
                    break;

                case "^":
                    canal = "INCARNAM";
                    break;

                case "i":
                    canal = "INFORMATION";
                    break;

                case "#":
                    canal = "EQUIPE";
                    break;

                case "$":
                    canal = "GROUPE";
                    break;

                case "%":
                    canal = "GUILDE";
                    break;
                case "F":
                    client.account.Logger.log_privado("[MESSAGE RECU]", $"{part[2]}:{part[3]}");
                    client.account.Game.character.CheckWhoSpeak(part[2]);
                    // réponse auto en privé
                    break;
                case "T":
                    client.account.Logger.log_privado("[MESSAGE ENVOYÉ]", $"{part[2]}:{part[3]}");
                    break;
                    default:
                    canal = "GÉNÉRAL";
                    break;
            }
            if(!canal.Equals(string.Empty))
                client.account.Logger.log_normal(canal, $"{part[2]}:{part[3]}");
        }
    }
}
