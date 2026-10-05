using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Chat;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
    /// <summary>
    /// Émotes : <c>eUK</c>, <c>eUE</c>, <c>eL</c>, <c>eA</c>, <c>eR</c>. Propriétaire : lot C1. L'orientation <c>eD</c> reste au modèle
    /// d'acteurs (lot S2).
    /// </summary>
    internal class EmoteFrame : Frame
    {
        private static ChatService Chat(TcpClient client) => client?.account?.Game?.Chat;

        /// <summary><c>eUK&lt;acteur&gt;|&lt;émote&gt;</c> (deux champs chez StarLoco) : 1, 19 et 20 assoient le personnage.</summary>
        [MessageAttribution("eUK")]
        public void GetEmote(TcpClient client, string message) => Chat(client)?.OnEmotePacket(message);

        /// <summary><c>eUE</c> : refus d'émote du client 1.34 (StarLoco ne l'envoie jamais).</summary>
        [MessageAttribution("eUE")]
        public void EmoteError(TcpClient client, string message) => Chat(client)?.OnEmoteErrorPacket();

        /// <summary><c>eL&lt;masque&gt;|&lt;masque&gt;</c> : émotes disponibles, envoyé à l'entrée en jeu.</summary>
        [MessageAttribution("eL")]
        public void EmoteList(TcpClient client, string message) => Chat(client)?.OnEmoteListPacket(message);

        /// <summary><c>eA&lt;id&gt;[|0]</c> : émote apprise.</summary>
        [MessageAttribution("eA")]
        public void EmoteAdded(TcpClient client, string message) => Chat(client)?.OnEmoteChangePacket(message, true);

        /// <summary><c>eR&lt;id&gt;[|0]</c> : émote retirée.</summary>
        [MessageAttribution("eR")]
        public void EmoteRemoved(TcpClient client, string message) => Chat(client)?.OnEmoteChangePacket(message, false);
    }
}
