using System;
using System.Threading.Tasks;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
    /// <summary>
    /// Échanges : création <c>ECK</c> et demande <c>ERK</c>.
    /// Extrait de <c>CharacterFrame</c> sans changement de logique (lot S1) ; propriétaire : lot F4.
    /// </summary>
    internal class ExchangeFrame : Frame
    {
        /// <summary>ECK&lt;type&gt;|&lt;identifiant&gt; : échange créé. Le type 0 ouvre la boutique PNJ ; les autres gardent l'état « stockage ».</summary>
        [MessageAttribution("ECK")]
        public void GoInStorage(TcpClient client, string message) => client.account.Game.Interactions.Shop.OnExchangeCreated(message.Substring(3));

        [MessageAttribution("ERK")]
        public Task AskExchange(TcpClient client, string message) => Task.Run(async () =>
        {
            client.account.Logger.LogInfo("DOFUS", "Quelqu'un demande un échange");
            await client.SendPacket("EV", true);
        });
    }
}
