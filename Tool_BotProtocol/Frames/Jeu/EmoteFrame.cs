using System;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
    /// <summary>
    /// Émotes : <c>eUK</c>.
    /// Extrait de <c>CharacterFrame</c> sans changement de logique (lot S1) ; propriétaire : lot C1.
    /// </summary>
    internal class EmoteFrame : Frame
    {
        [MessageAttribution("eUK")]
        public void GetEmote(TcpClient client, string message)
        {
            string[] sep = message.Substring(3).Split('|');
            int id = int.Parse(sep[0]);
            int emote_id = int.Parse(sep[1]);
            Accounts A = client.account;

            if (A.Game.character.id != id)
                return;

            if (emote_id == 1 && A.AccountStates != AccountStates.REGENERATION)
                A.AccountStates = AccountStates.REGENERATION;
            else if (emote_id == 0 && A.AccountStates == AccountStates.REGENERATION)
                A.AccountStates = AccountStates.CONNECTED_INACTIVE;
        }
    }
}
