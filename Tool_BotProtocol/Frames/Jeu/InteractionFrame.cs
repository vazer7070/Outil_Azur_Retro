using System;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
    /// <summary>
    /// Dialogues PNJ (D), zaaps (W) et boutique PNJ (E) : routes des classes <c>Dialog</c>, <c>Waypoints</c>
    /// et <c>Exchange</c> du client 1.34. Le troisième caractère K/E porte le succès ou l'erreur, comme
    /// dans le routeur du client (<c>!p3</c>).
    /// </summary>
    class InteractionFrame : Frame
    {
        private static InteractionsClass Interactions(TcpClient client) => client.account.Game.Interactions;

        [MessageAttribution("DCK")]
        public void DialogCreated(TcpClient client, string message) => Interactions(client).Npc.OnCreated(message.Substring(3));

        [MessageAttribution("DCE")]
        public void DialogRefused(TcpClient client, string message) => Interactions(client).Npc.OnCreateError();

        [MessageAttribution("DQ")]
        public void DialogQuestion(TcpClient client, string message) => Interactions(client).Npc.OnQuestion(message.Substring(2));

        [MessageAttribution("DP")]
        public void DialogPause(TcpClient client, string message) => Interactions(client).Npc.OnPause();

        [MessageAttribution("DV")]
        public void DialogLeave(TcpClient client, string message) => Interactions(client).Npc.OnLeave();

        [MessageAttribution("WC")]
        public void ZaapList(TcpClient client, string message) => Interactions(client).Zaap.OnList(message.Substring(2));

        /// <summary><c>WU</c> reçu du serveur est toujours une erreur d'utilisation (<c>WUE</c> chez StarLoco) ; le client n'en lit pas le reste.</summary>
        [MessageAttribution("WU")]
        public void ZaapUseError(TcpClient client, string message) => Interactions(client).Zaap.OnUseError();

        [MessageAttribution("WV")]
        public void ZaapLeave(TcpClient client, string message) => Interactions(client).Zaap.OnLeave();

        [MessageAttribution("EL")]
        public void ExchangeList(TcpClient client, string message) => Interactions(client).Shop.OnList(message.Substring(2));

        [MessageAttribution("EBK")]
        public void BuyAccepted(TcpClient client, string message) => Interactions(client).Shop.OnBuy(true);

        [MessageAttribution("EBE")]
        public void BuyRefused(TcpClient client, string message) => Interactions(client).Shop.OnBuy(false);

        [MessageAttribution("ESK")]
        public void SellAccepted(TcpClient client, string message) => Interactions(client).Shop.OnSell(true);

        [MessageAttribution("ESE")]
        public void SellRefused(TcpClient client, string message) => Interactions(client).Shop.OnSell(false);

        [MessageAttribution("EV")]
        public void ExchangeLeave(TcpClient client, string message) => Interactions(client).Shop.OnLeave(message.Substring(2));
    }
}
