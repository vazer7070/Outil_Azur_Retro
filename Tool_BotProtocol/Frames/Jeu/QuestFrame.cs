using System;
using System.Threading.Tasks;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Quetes;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
    /// <summary>
    /// Quêtes (propriétaire : lot F10) : préfixes <c>QL</c> et <c>QS</c>, aiguillés comme <c>dofus.aks.Quests</c> du client 1.34
    /// (<c>onList</c>, <c>onStep</c>) puis confiés au service <see cref="QuestsActions"/> du compte (<c>Game.Interactions.Quests</c>).
    /// Les messages de progression <c>Im054/055/056</c> restent lus par la session (lot S1). Un paquet mal formé est journalisé,
    /// jamais propagé.
    /// </summary>
    internal class QuestFrame : Frame
    {
        /// <summary><c>QL+&lt;id;fini|…&gt;</c> (StarLoco <c>Player.getQuestGmPacket</c>) : liste des quêtes du personnage.</summary>
        [MessageAttribution("QL")]
        public Task QuestList(TcpClient client, string message) => Run(client, message, quests => quests.OnListPacket(message));

        /// <summary><c>QS&lt;quête&gt;|&lt;étape&gt;|&lt;objectifs&gt;|&lt;précédentes&gt;|&lt;suivantes&gt;[|&lt;question&gt;]</c> (<c>QuestPlayer.getQuestStepPacket</c>).</summary>
        [MessageAttribution("QS")]
        public void QuestStep(TcpClient client, string message) => Apply(client, message, quests => quests.OnStepPacket(message));

        private static void Apply(TcpClient client, string message, Action<QuestsActions> handler)
        {
            QuestsActions quests = client?.account?.Game?.Interactions?.Quests;
            if (quests == null || message == null) return;
            try { handler(quests); }
            catch (Exception error) { Log(client, message, error); }
        }

        private static async Task Run(TcpClient client, string message, Func<QuestsActions, Task> handler)
        {
            QuestsActions quests = client?.account?.Game?.Interactions?.Quests;
            if (quests == null || message == null) return;
            try { await handler(quests).ConfigureAwait(false); }
            catch (Exception error) { Log(client, message, error); }
        }

        private static void Log(TcpClient client, string message, Exception error)
        {
            try { client.account?.Logger?.LogError("QUÊTES", "Paquet de quêtes illisible ignoré (" + Preview(message) + ") : " + error.Message); }
            catch (Exception) { /* Journal fermé. */ }
        }

        private static string Preview(string message) => message.Length > 80 ? message.Substring(0, 80) + "…" : message;
    }
}
