using System.Threading.Tasks;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Exchanges;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
    /// <summary>
    /// Échanges (famille <c>E</c> de <c>dofus.aks.Exchange</c>, client 1.34) : <c>ECK&lt;type&gt;[|&lt;données&gt;]</c> est aiguillé
    /// vers la fenêtre enregistrée pour ce type (<see cref="ExchangeRegistry"/>), qui reçoit ensuite les mouvements
    /// <c>EMK</c>/<c>EmK</c>/<c>EsK</c>, les états <c>EK</c> et la charge <c>Ew</c>. <c>ERK</c> est une demande d'échange,
    /// présentée au joueur (jamais acceptée seule) ; <c>ERE</c> un refus. <c>EL</c> et <c>EV</c> passent par
    /// <c>InteractionFrame</c>. Propriétaire : lot F4.
    /// </summary>
    internal class ExchangeFrame : Frame
    {
        private static ExchangeRegistry Exchanges(TcpClient client) => client?.account?.Game?.Interactions?.Exchanges;

        /// <summary><c>ECK&lt;type&gt;[|&lt;données&gt;]</c> : échange créé ; un type sans fenêtre est journalisé et refermé (<c>EV</c>).</summary>
        [MessageAttribution("ECK")]
        public Task ExchangeCreated(TcpClient client, string message) => Exchanges(client)?.OnCreatedAsync(message.Substring(3)) ?? Task.CompletedTask;

        /// <summary><c>ERK&lt;demandeur&gt;|&lt;cible&gt;|&lt;type&gt;</c> : demande d'échange envoyée ou reçue.</summary>
        [MessageAttribution("ERK")]
        public Task ExchangeRequest(TcpClient client, string message) =>
            client?.account?.Game?.Interactions?.Exchange?.OnRequestAsync(message.Substring(3)) ?? Task.CompletedTask;

        /// <summary><c>ERE&lt;code&gt;</c> : demande refusée par le serveur (joueur occupé, atelier trop loin…).</summary>
        [MessageAttribution("ERE")]
        public void ExchangeRequestError(TcpClient client, string message) => Exchanges(client)?.OnRequestError(message.Substring(3));

        /// <summary><c>EMK&lt;O|G&gt;…</c> : mouvement confirmé de son propre côté.</summary>
        [MessageAttribution("EMK")]
        public void LocalMovement(TcpClient client, string message) => Exchanges(client)?.OnLocalMovement(message.Substring(3));

        /// <summary><c>EmK&lt;O|G&gt;…</c> : mouvement de l'autre côté.</summary>
        [MessageAttribution("EmK")]
        public void DistantMovement(TcpClient client, string message) => Exchanges(client)?.OnDistantMovement(message.Substring(3));

        /// <summary><c>EsK&lt;O|G&gt;…</c> : mouvement du coffre, de la banque ou d'un autre stockage.</summary>
        [MessageAttribution("EsK")]
        public void StorageMovement(TcpClient client, string message) => Exchanges(client)?.OnStorageMovement(message.Substring(3));

        /// <summary><c>EK&lt;0|1&gt;[&lt;acteur&gt;]</c> : état « prêt » d'un participant.</summary>
        [MessageAttribution("EK")]
        public void ExchangeReady(TcpClient client, string message) => Exchanges(client)?.OnReady(message.Substring(2));

        /// <summary><c>Ew&lt;pods&gt;;&lt;max&gt;</c> : charge de la monture pendant un échange avec ses sacoches.</summary>
        [MessageAttribution("Ew")]
        public void MountPods(TcpClient client, string message) => Exchanges(client)?.OnPods(message.Substring(2));
    }
}
