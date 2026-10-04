using System;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Actions;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
    /// <summary>
    /// Objets interactifs : zaapis (<c>Wc</c>, <c>Wu</c>, <c>Wv</c> de <c>dofus.aks.Subway</c>), codes (<c>KC</c>, <c>KK</c>, <c>KKE</c>,
    /// <c>KV</c> de <c>dofus.aks.Key</c>), documents (<c>dCK</c>, <c>dV</c> de <c>dofus.aks.Documents</c>), quantités et résultats
    /// (<c>IQ</c>, <c>IO</c>) et l'action 501 (<c>GA&lt;id&gt;;501;…</c>) enregistrée dans <see cref="GameActionRouter"/>.
    /// Un paquet mal formé est journalisé, jamais fatal. Les préfixes en majuscules (<c>WC</c>, <c>WU</c>, <c>WV</c>) restent aux zaaps.
    /// </summary>
    internal class InteractiveFrame : Frame
    {
        private static InteractionsClass Interactions(TcpClient client) => client?.account?.Game?.Interactions;

        private static void Run(TcpClient client, string message, Action<InteractionsClass> action)
        {
            InteractionsClass interactions = Interactions(client);
            if (interactions == null) return;
            try { action(interactions); }
            catch (Exception error)
            {
                client.account?.Logger?.LogError("INTERACTIF", "Paquet ignoré (" + (message ?? string.Empty) + ") : " + error.Message);
            }
        }

        [MessageAttribution("Wc")]
        public void ZaapiList(TcpClient client, string message) => Run(client, message, i => i.Zaapi.OnList(message.Substring(2)));

        /// <summary><c>Wu…</c> reçu du serveur : transport refusé (<c>Subway.onUseError</c>).</summary>
        [MessageAttribution("Wu")]
        public void ZaapiUseError(TcpClient client, string message) => Run(client, message, i => i.Zaapi.OnUseError());

        [MessageAttribution("Wv")]
        public void ZaapiLeave(TcpClient client, string message) => Run(client, message, i => i.Zaapi.OnLeave());

        /// <summary><c>KCK&lt;type&gt;|&lt;cases&gt;</c> : le client lit à partir du quatrième caractère.</summary>
        [MessageAttribution("KC")]
        public void KeyCreate(TcpClient client, string message) => Run(client, message, i => i.Interactive.Code.OnCreate(message.Length > 3 ? message.Substring(3) : string.Empty));

        [MessageAttribution("KKE")]
        public void KeyRefused(TcpClient client, string message) => Run(client, message, i => i.Interactive.Code.OnCodeRefused());

        [MessageAttribution("KK")]
        public void KeyChanged(TcpClient client, string message) => Run(client, message, i => i.Interactive.Code.OnCodeChanged());

        [MessageAttribution("KV")]
        public void KeyLeave(TcpClient client, string message) => Run(client, message, i => i.Interactive.Code.OnLeave());

        [MessageAttribution("dCK")]
        public void DocumentCreate(TcpClient client, string message) => Run(client, message, i => i.Interactive.Document.OnCreate(message.Substring(3)));

        [MessageAttribution("dV")]
        public void DocumentLeave(TcpClient client, string message) => Run(client, message, i => i.Interactive.Document.OnLeave());

        [MessageAttribution("IQ")]
        public void Quantity(TcpClient client, string message) => Run(client, message, i => i.Interactive.OnQuantity(message.Substring(2)));

        [MessageAttribution("IO")]
        public void ObjectResult(TcpClient client, string message) => Run(client, message, i => i.Interactive.OnObjectResult(message.Substring(2)));

        /// <summary><c>GA&lt;id&gt;;501;&lt;acteur&gt;;&lt;cellule&gt;,&lt;durée&gt;</c> : utilisation d'un objet interactif (hors combat).</summary>
        [GameActionHandler(501)]
        private static void InteractiveAction(GameActionContext context)
        {
            InteractionsClass interactions = context?.Account?.Game?.Interactions;
            if (interactions == null) return;
            try { interactions.Interactive.OnActionStarted(context.Packet); }
            catch (Exception error) { context.Account?.Logger?.LogError("INTERACTIF", "Action 501 ignorée : " + error.Message); }
        }
    }
}
