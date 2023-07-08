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
                    client.account.Logger.LogRecruitTchat(canal, $"{part[2]}:{part[3]}");
                    break;

                case ":":
                    canal = "COMMERCE";
                    client.account.Logger.LogCommerceTchat(canal, $"{part[2]}:{part[3]}");
                    break;

                case "^":
                    canal = "INCARNAM";
                    client.account.Logger.log_normal(canal, $"{part[2]}:{part[3]}");
                    break;

                case "i":
                    canal = "INFORMATION";
                    client.account.Logger.log_normal(canal, $"{part[2]}:{part[3]}");
                    break;

                case "#":
                    canal = "EQUIPE";
                    client.account.Logger.LogTchatTeam(canal, $"{part[2]}:{part[3]}");
                    break;

                case "$":
                    canal = "GROUPE";
                    client.account.Logger.LogTchatGroupe(canal, $"{part[2]}:{part[3]}");
                    break;

                case "%":
                    canal = "GUILDE";
                    client.account.Logger.LogTchatGuild(canal, $"{part[2]}:{part[3]}");
                    break;
                case "@":
                    canal = "ADMIN";
                    client.account.Logger.LogAdminTchat(canal, $"{part[2]}:{part[3]}");
                    break;
                case "F":
                    client.account.Logger.LogTchatPrivate("MP reçu", $"{part[2]}:{part[3]}");
                    client.account.Game.character.CheckWhoSpeak(part[2]);
                    // réponse auto en privé
                    break;
                case "T":
                    client.account.Logger.LogTchatPrivate("MP envoyé", $"{part[2]}:{part[3]}");
                    break;
                    default:
                    canal = "GÉNÉRAL";
                    client.account.Logger.log_normal(canal, $"{part[2]}:{part[3]}");
                    break;
            }
        }
    }
}
