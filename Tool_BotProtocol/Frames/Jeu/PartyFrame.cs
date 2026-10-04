using System;
using System.Threading.Tasks;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Groupes;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
    /// <summary>
    /// Groupe (propriétaire : lot F1) : préfixes <c>PIK</c>, <c>PIE</c>, <c>PCK</c>, <c>PCE</c>, <c>PL</c>, <c>PM</c>, <c>PV</c>,
    /// <c>PR</c>, <c>PA</c>, <c>PF</c> et les informations <c>IC</c> (boussole) et <c>IH</c> (positions), lus comme
    /// <c>dofus.aks.Party</c> et <c>dofus.aks.Infos</c> du client 1.34 et tels que <c>GameClient.parseGroupPacket</c> de StarLoco
    /// les envoie. Chaque paquet est confié au service <see cref="PartyActions"/> du compte
    /// (<c>Game.Interactions.Party</c>) ; un paquet mal formé est journalisé, jamais propagé.
    /// </summary>
    internal class PartyFrame : Frame
    {
        /// <summary>
        /// <c>PIK&lt;invitant&gt;|&lt;invité&gt;</c> : envoyé à l'invitant (simple information) et à l'invité. L'invitation reçue
        /// n'est jamais acceptée seule : elle est proposée à <c>GameSession.PartyInviteReceived</c> (boîte Oui / Non / Ignorer
        /// du volet Groupe), sinon refusée par <c>PR</c>. Seul le chef d'une équipe de comptes du bot (<see cref="Regroupement"/>)
        /// est accepté sans question.
        /// </summary>
        [MessageAttribution("PIK")]
        public Task GetGroup(TcpClient client, string message) => Run(client, message, party => party.OnInvitePacket(message));

        /// <summary><c>PIE&lt;n|a|f&gt;[nom]</c> : invitation impossible (introuvable, déjà groupé, groupe complet).</summary>
        [MessageAttribution("PIE")]
        public void InviteError(TcpClient client, string message) => Apply(client, message, party => party.OnInviteErrorPacket(message));

        /// <summary><c>PCK&lt;chef&gt;</c> : le personnage entre dans un groupe.</summary>
        [MessageAttribution("PCK")]
        public void AcceptGroupe(TcpClient client, string message) => Apply(client, message, party => party.OnCreatePacket(message));

        /// <summary><c>PCE&lt;a|f&gt;</c> : création impossible (lu comme le client ; StarLoco ne l'envoie pas).</summary>
        [MessageAttribution("PCE")]
        public void CreateError(TcpClient client, string message) => Apply(client, message, party => party.OnCreateErrorPacket(message));

        /// <summary><c>PL&lt;id&gt;</c> : chef du groupe.</summary>
        [MessageAttribution("PL")]
        public void Leader(TcpClient client, string message) => Apply(client, message, party => party.OnLeaderPacket(message));

        /// <summary><c>PM+…</c>, <c>PM~…</c>, <c>PM-&lt;id&gt;</c> : membres (format de <c>Player.parseToPM</c>).</summary>
        [MessageAttribution("PM")]
        public void InGroupParse(TcpClient client, string message) => Apply(client, message, party => party.OnMembersPacket(message));

        /// <summary><c>PV[&lt;id de l'exclueur&gt;]</c> : le personnage n'est plus dans le groupe.</summary>
        [MessageAttribution("PV")]
        public void EjectGroup(TcpClient client, string message) => Apply(client, message, party => party.OnLeavePacket(message));

        /// <summary><c>PR</c> : invitation refusée ou annulée par l'autre joueur (le client ferme ses boîtes).</summary>
        [MessageAttribution("PR")]
        public void Refused(TcpClient client, string message) => Apply(client, message, party => party.OnInvitationAnsweredPacket(message));

        /// <summary><c>PA</c> : invitation acceptée (lu comme le client ; StarLoco ne l'envoie pas).</summary>
        [MessageAttribution("PA")]
        public void Accepted(TcpClient client, string message) => Apply(client, message, party => party.OnInvitationAnsweredPacket(message));

        /// <summary><c>PF+&lt;id&gt;</c>, <c>PF-</c>, <c>PFE</c> : suivi d'un membre.</summary>
        [MessageAttribution("PF")]
        public void Follow(TcpClient client, string message) => Apply(client, message, party => party.OnFollowPacket(message));

        /// <summary><c>IC&lt;x&gt;|&lt;y&gt;</c> ou <c>IC|</c> : cible de la boussole (suivi, recherche d'un conjoint, quêtes…).</summary>
        [MessageAttribution("IC")]
        public void Compass(TcpClient client, string message) => Apply(client, message, party => party.OnCompassPacket(message));

        /// <summary><c>IH&lt;x;y;carte;type;id;nom&gt;|…</c> : positions à mettre en évidence (réponse à <c>PW</c>).</summary>
        [MessageAttribution("IH")]
        public void Highlight(TcpClient client, string message) => Apply(client, message, party => party.OnHighlightPacket(message));

        private static void Apply(TcpClient client, string message, Action<PartyActions> handler)
        {
            PartyActions party = client?.account?.Game?.Interactions?.Party;
            if (party == null) return;
            try { handler(party); }
            catch (Exception error)
            {
                client.account?.Logger?.LogError("GROUPE", "Paquet de groupe illisible ignoré (" + Preview(message) + ") : " + error.Message);
            }
        }

        private static async Task Run(TcpClient client, string message, Func<PartyActions, Task> handler)
        {
            PartyActions party = client?.account?.Game?.Interactions?.Party;
            if (party == null) return;
            try { await handler(party).ConfigureAwait(false); }
            catch (Exception error)
            {
                client.account?.Logger?.LogError("GROUPE", "Paquet de groupe illisible ignoré (" + Preview(message) + ") : " + error.Message);
            }
        }

        private static string Preview(string message) =>
            message == null ? string.Empty : message.Length > 80 ? message.Substring(0, 80) + "…" : message;
    }
}
