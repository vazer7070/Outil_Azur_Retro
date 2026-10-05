using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Habitat;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
    /// <summary>
    /// Maisons (famille <c>h</c>, classe <c>Houses</c> du client 1.34 et <c>parseHousePacket</c> de StarLoco) : propriétés <c>hP</c>,
    /// liste de ses maisons <c>hL</c>, fenêtre de vente <c>hCK</c>, confirmation de mise en vente <c>hSK</c>/<c>hSE</c>,
    /// maison de guilde <c>hG</c>, fermeture <c>hV</c>. Les codes de porte (<c>KCK</c>) restent au lot M3. Chaque paquet est
    /// appliqué par <see cref="HouseActions"/>, qui journalise un paquet illisible sans jamais lever d'exception.
    /// </summary>
    class HouseFrame : Frame
    {
        private static HouseActions House(TcpClient client) => client.account.Game.Interactions.House;

        [MessageAttribution("hP")]
        public void Properties(TcpClient client, string message) => House(client).OnProperties(message.Substring(2));

        [MessageAttribution("hL")]
        public void List(TcpClient client, string message) => House(client).OnList(message.Substring(2));

        [MessageAttribution("hCK")]
        public void SaleWindow(TcpClient client, string message) => House(client).OnSaleWindow(message.Substring(3));

        [MessageAttribution("hSK")]
        public void SaleAccepted(TcpClient client, string message) => House(client).OnSold(true, message.Substring(3));

        /// <summary>Lu par le client (<c>!p3</c>), jamais émis par StarLoco : relevé pour l'exhaustivité.</summary>
        [MessageAttribution("hSE")]
        public void SaleRefused(TcpClient client, string message) => House(client).OnSold(false, message.Substring(3));

        [MessageAttribution("hG")]
        public void GuildInfos(TcpClient client, string message) => House(client).OnGuildInfos(message.Substring(2));

        [MessageAttribution("hV")]
        public void Leave(TcpClient client, string message) => House(client).OnLeave();
    }
}
