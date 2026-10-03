using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
    public  class NoCheatFrame : Frame
    {
        [MessageAttribution("BC")]
        public async Task VerifGameFiles(TcpClient client, string message)
        {
            string[] part = message.Substring(2).Split(';');
            if (part.Length < 2 || !int.TryParse(part[0], out int id)) return;
            long bytes = -1;
            string data = part[1];

            if (data.Contains("core.swf"))
                long.TryParse(GlobalConfig.CORESIZE, out bytes);
            else if (data.Contains("loader.swf"))
                long.TryParse(GlobalConfig.LOADERSIZE, out bytes);

            await client.SendPacket($"BC{id};{bytes}");
            client.account.Logger.LogInfo("DIAGNOSTIC", "Le serveur a demandé la taille configurée d’un fichier client.");
        }
    }
}
