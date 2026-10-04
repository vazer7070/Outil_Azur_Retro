using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Chat;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
    /// <summary>
    /// Chat : <c>cMK</c>, <c>cME</c>, <c>cC±</c>, <c>cS</c>, <c>cs</c>, <c>M1</c>. Propriétaire : lot C1. La lecture et l'état sont dans
    /// <see cref="ChatService"/> ; un paquet mal formé est journalisé en débogage et ignoré.
    /// </summary>
    internal class ChatFrame : Frame
    {
        private static ChatService Chat(TcpClient client) => client?.account?.Game?.Chat;

        /// <summary><c>cC+&lt;lettres&gt;</c> : écho d'un abonnement, ou liste des canaux à l'entrée en jeu.</summary>
        [MessageAttribution("cC+")]
        public void AddCanal(TcpClient client, string message) => Chat(client)?.OnSubscriptionPacket(message);

        /// <summary><c>cC-&lt;lettres&gt;</c> : écho d'un désabonnement.</summary>
        [MessageAttribution("cC-")]
        public void DeleteCanal(TcpClient client, string message) => Chat(client)?.OnSubscriptionPacket(message);

        /// <summary><c>cMK&lt;canal&gt;|&lt;id&gt;|&lt;nom&gt;|&lt;texte&gt;[|…]</c> : message d'un canal, chuchotement reçu (F) ou envoyé (T).</summary>
        [MessageAttribution("cMK")]
        public void GetTchat(TcpClient client, string message) => Chat(client)?.OnMessagePacket(message);

        /// <summary><c>cMEf&lt;nom&gt;</c> : destinataire d'un chuchotement absent.</summary>
        [MessageAttribution("cME")]
        public void ChatError(TcpClient client, string message) => Chat(client)?.OnErrorPacket(message);

        /// <summary><c>cS&lt;acteur&gt;|&lt;smiley&gt;</c> : smiley au-dessus d'un acteur de la carte ou du combat.</summary>
        [MessageAttribution("cS")]
        public void Smiley(TcpClient client, string message) => Chat(client)?.OnSmileyPacket(message);

        /// <summary><c>cs&lt;texte&gt;</c> : message du serveur dans le chat (message du jour, annonces, commandes « . »).</summary>
        [MessageAttribution("cs")]
        public void ServerNotice(TcpClient client, string message) => Chat(client)?.OnServerNoticePacket(message);

        /// <summary><c>M1&lt;id&gt;[|&lt;a;b&gt;[|&lt;nom&gt;]]</c> : message serveur en fenêtre ; <c>M10</c> = anti-flood du canal général.</summary>
        [MessageAttribution("M1")]
        public void ServerPopup(TcpClient client, string message) => Chat(client)?.OnServerPopupPacket(message);
    }
}
