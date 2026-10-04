using System;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
    /// <summary>
    /// Monture : <c>Re</c>.
    /// Extrait de <c>CharacterFrame</c> sans changement de logique (lot S1) ; propriétaire : lot F7.
    /// </summary>
    internal class MountFrame : Frame
    {
        [MessageAttribution("Re")]
        public void GetInfoMonture(TcpClient client, string message) => client.account.CanUseMount = true;
    }
}
