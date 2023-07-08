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
        public Task VerifGameFiles(TcpClient client, string message) => Task.Run(async () =>
        {
            string[] part = message.Substring(2).Split(';');
            int id = int.Parse(part[0]), bytes = - 1;
            string data = part[1];

            if (data.Contains("core.swf"))
                bytes = byte.Parse(GlobalConfig.CORESIZE);
            else if (data.Contains("loader.swf"))
                bytes = byte.Parse(GlobalConfig.LOADERSIZE);

            await client.SendPacket($"BC{id};{bytes}");
            client.account.Logger.LogInfo("ANTI-CHEAT", "un modérateur vient de vérifier les fichiers du jeu.");
            client.account.Logger.LogTchatPrivate("ANTI-CHEAT", "un modérateur vient de vérifier les fichiers du jeu.");
        });
    }
}
