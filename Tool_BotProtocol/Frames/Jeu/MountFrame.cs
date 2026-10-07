using System;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Montures;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
    /// <summary>
    /// Montures, enclos et étable (propriétaire : lot F7) : préfixes <c>Re</c>, <c>Rx</c>, <c>Rn</c>, <c>Rr</c>, <c>Rd</c>, <c>Rp</c>,
    /// <c>RD</c>, <c>Rv</c> (<c>dofus.aks.Mount</c> du client 1.34), <c>Ee</c> et <c>Ef</c> (<c>Exchange.onMountStorage</c> et
    /// <c>Exchange.onMountPark</c>), confiés au service <see cref="MountActions"/> du compte (<c>Game.Interactions.Mount</c>) et à
    /// l'étable ouverte (<see cref="ShedExchange"/>). <c>Rc</c>, <c>Rf</c>, <c>Rb</c>, <c>Rs</c> et <c>Ro</c> ne sont qu'envoyés.
    /// Un paquet mal formé est journalisé, jamais propagé.
    /// </summary>
    internal class MountFrame : Frame
    {
        /// <summary><c>Re+&lt;fiche&gt;</c>, <c>Re-</c>, <c>ReE&lt;code&gt;</c> : monture équipée (<c>Mount.onEquip</c>).</summary>
        [MessageAttribution("Re")]
        public void GetInfoMonture(TcpClient client, string message)
        {
            // L'annonce d'une monture ne dépend pas de la lecture de sa fiche (« Re+ » seul suffit).
            if (client?.account != null && message != null && message.StartsWith("Re+", StringComparison.Ordinal)) client.account.CanUseMount = true;
            Apply(client, message, m => m.OnEquip(Body(message, 2)));
        }

        /// <summary><c>Rx&lt;%&gt;</c> : part de l'expérience donnée à la monture (<c>Mount.onXP</c>).</summary>
        [MessageAttribution("Rx")]
        public void XpRatio(TcpClient client, string message) => Apply(client, message, m => m.OnXp(Body(message, 2)));

        /// <summary><c>Rn&lt;nom&gt;</c> : nouveau nom (<c>Mount.onName</c>).</summary>
        [MessageAttribution("Rn")]
        public void Name(TcpClient client, string message) => Apply(client, message, m => m.OnName(Body(message, 2)));

        /// <summary><c>Rr+</c> / <c>Rr-</c> : sur la monture ou non (<c>Mount.onRidingState</c>).</summary>
        [MessageAttribution("Rr")]
        public void RidingState(TcpClient client, string message) => Apply(client, message, m => m.OnRidingState(Body(message, 2)));

        /// <summary><c>Rd&lt;fiche&gt;</c> : fiche d'une monture d'enclos ou d'un certificat (<c>Mount.onData</c>).</summary>
        [MessageAttribution("Rd")]
        public void Data(TcpClient client, string message) => Apply(client, message, m => m.OnData(Body(message, 2)));

        /// <summary><c>Rp&lt;propriétaire;prix;taille;objets;guilde;emblème&gt;</c> : enclos de la carte (<c>Mount.onMountPark</c>).</summary>
        [MessageAttribution("Rp")]
        public void MountPark(TcpClient client, string message) => Apply(client, message, m => m.OnMountPark(Body(message, 2)));

        /// <summary><c>RD&lt;prix&gt;|&lt;prix&gt;</c> : fenêtre d'achat ou de vente de l'enclos (<c>Mount.onMountParkBuy</c>).</summary>
        [MessageAttribution("RD")]
        public void MountParkSale(TcpClient client, string message) => Apply(client, message, m => m.OnParkSale(Body(message, 2)));

        /// <summary><c>Rv</c> : fermeture de la fenêtre de l'enclos (<c>Mount.onLeave</c>).</summary>
        [MessageAttribution("Rv")]
        public void Leave(TcpClient client, string message) => Apply(client, message, m => m.OnLeave());

        /// <summary><c>Ee&lt;~|+|-|E&gt;…</c> : étable (<c>Exchange.onMountStorage</c>).</summary>
        [MessageAttribution("Ee")]
        public void Shed(TcpClient client, string message) => Apply(client, message, m => Window(client)?.OnShedMovement(Body(message, 2)));

        /// <summary><c>Ef&lt;+|-|E&gt;…</c> : enclos (<c>Exchange.onMountPark</c>).</summary>
        [MessageAttribution("Ef")]
        public void Park(TcpClient client, string message) => Apply(client, message, m => Window(client)?.OnParkMovement(Body(message, 2)));

        private static ShedExchange Window(TcpClient client) => client?.account?.Game?.Interactions?.Exchanges?.Get<ShedExchange>();

        private static string Body(string message, int start) => message != null && message.Length > start ? message.Substring(start) : string.Empty;

        private static void Apply(TcpClient client, string message, Action<MountActions> handler)
        {
            MountActions mount = client?.account?.Game?.Interactions?.Mount;
            if (mount == null || message == null) return;
            try { handler(mount); }
            catch (Exception error) when (!(error is OutOfMemoryException))
            {
                client.account?.Logger?.LogError("MONTURE", "Paquet de monture illisible ignoré (" + Preview(message) + ") : " + error.Message);
            }
        }

        private static string Preview(string message) => message.Length > 80 ? message.Substring(0, 80) + "…" : message;
    }
}
