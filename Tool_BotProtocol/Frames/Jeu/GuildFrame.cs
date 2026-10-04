using System;
using System.Threading.Tasks;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
    /// <summary>
    /// Guilde : invitations <c>gJ…</c>.
    /// Extrait de <c>CharacterFrame</c> sans changement de logique (lot S1) ; propriétaire : lot F3.
    /// </summary>
    internal class GuildFrame : Frame
    {
        [MessageAttribution("gJR")]
        public Task HandleGuild(TcpClient client, string message) => Task.Run(async () =>
        {
            if (client.account.Game.character.HasGuild == true)
            {
                await Task.Delay(100);
                client.account.Logger.LogInfo("PERSO", "Invitation à la guilde refusée");
                await client.SendPacket("gJE");
            }
        });
    }
}
