using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Exchanges;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
    /// <summary>
    /// Mode marchand (classe <c>Exchange</c> du client 1.34) : <c>Eq1|1|&lt;taxe&gt;</c> annonce la taxe demandée par <c>Eq</c>
    /// (<c>onAskOfflineExchange</c>) ; <c>EiK±…</c> est le mouvement d'un lot de son magasin que le client sait lire
    /// (<c>onShopMovement</c>, troisième caractère K/E) mais que StarLoco n'envoie jamais. <c>ECK4</c>/<c>ECK6</c>, <c>EL</c>
    /// et <c>EV</c> passent par le registre des échanges (<see cref="MerchantExchange"/>).
    /// </summary>
    class MerchantFrame : Frame
    {
        private static MerchantExchange Merchant(TcpClient client) => client.account.Game.Interactions.Merchant;

        [MessageAttribution("Eq1")]
        /// <summary>Le client lit <c>p4.substr(2)</c> : le « 1 » du préfixe est le premier champ (type) de <c>Eq&lt;type&gt;|&lt;taux&gt;|&lt;taxe&gt;</c>.</summary>
        public void TaxProposed(TcpClient client, string message) => Merchant(client).OnTaxProposed(message.Substring(2));

        [MessageAttribution("Ei")]
        public void ShopMovement(TcpClient client, string message) =>
            Merchant(client).OnShopMovement(message.Length > 2 && message[2] == 'K', message.Length > 3 ? message.Substring(3) : string.Empty);
    }
}
