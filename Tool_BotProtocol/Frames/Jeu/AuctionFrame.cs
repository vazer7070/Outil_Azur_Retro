using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Exchanges;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
    /// <summary>
    /// Hôtel de vente (classe <c>Exchange</c> du client 1.34, méthodes <c>onBigStore…</c>) : listes et mouvements de l'achat
    /// <c>EHL</c>/<c>EHM</c> (modèles d'une catégorie), <c>EHl</c>/<c>EHm</c> (lots d'un modèle), prix moyen <c>EHP</c> et résultat de
    /// recherche <c>EHS</c>/<c>EHSK</c>. Le client lit <c>p4.substr(3)</c> pour chacun. <c>ECK10</c>/<c>ECK11</c>, <c>EL</c>, <c>EmK</c> et <c>EV</c>
    /// passent par le registre des échanges (<see cref="AuctionHouse"/>). Propriétaire : lot F5.
    /// </summary>
    class AuctionFrame : Frame
    {
        private static AuctionHouse Auction(TcpClient client) => client?.account?.Game?.Interactions?.Auction;

        [MessageAttribution("EHL")]
        public void TemplatesList(TcpClient client, string message) => Auction(client)?.OnTemplates(message.Substring(3));

        [MessageAttribution("EHM")]
        public void TemplateMovement(TcpClient client, string message) => Auction(client)?.OnTemplateMovement(message.Substring(3));

        [MessageAttribution("EHl")]
        public void LinesList(TcpClient client, string message) => Auction(client)?.OnLines(message.Substring(3));

        [MessageAttribution("EHm")]
        public void LineMovement(TcpClient client, string message) => Auction(client)?.OnLineMovement(message.Substring(3));

        [MessageAttribution("EHP")]
        public void AveragePrice(TcpClient client, string message) => Auction(client)?.OnAveragePrice(message.Substring(3));

        [MessageAttribution("EHS")]
        public void SearchResult(TcpClient client, string message) => Auction(client)?.OnSearch(message.Substring(3));
    }
}
