using System;
using System.Threading.Tasks;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Guildes;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
    /// <summary>
    /// Guilde (propriétaire : lot F3) : préfixes <c>gS</c>, <c>gIG</c>, <c>gIM</c>, <c>gIB</c>, <c>gIT</c>, <c>gIF</c>, <c>gIH</c>, <c>gJR</c>,
    /// <c>gJr</c>, <c>gJE</c>, <c>gJK</c>, <c>gJC</c>, <c>gK</c> (<c>gKK</c>, <c>gKE</c>), <c>gV</c>, <c>gC</c>, <c>gn</c>, <c>gH</c>, <c>gT</c>
    /// (<c>gTS</c>, <c>gTR</c>, <c>gTG</c>), <c>gTK</c>, <c>gTE</c>, <c>gA</c> et <c>gU</c>, aiguillés sur leurs premières lettres comme le client 1.34
    /// (famille <c>g</c> de <c>dofus.aks.Guild</c>) puis confiés au service <see cref="GuildActions"/> du compte
    /// (<c>Game.Interactions.Guild</c>). Le refus <c>gJE&lt;id&gt;</c> d'une invitation sans abonné suit la matrice §2 n° 36. Un paquet
    /// mal formé est journalisé, jamais propagé.
    /// </summary>
    internal class GuildFrame : Frame
    {
        /// <summary><c>gS&lt;nom&gt;|&lt;fond&gt;|&lt;couleur&gt;|&lt;motif&gt;|&lt;couleur&gt;|&lt;droits&gt;</c> : guilde et droits du personnage.</summary>
        [MessageAttribution("gS")]
        public void Stats(TcpClient client, string message) => Apply(client, message, guild => guild.OnStatsPacket(message));

        /// <summary><c>gIG&lt;1|0&gt;|&lt;niveau&gt;|&lt;xp min&gt;|&lt;xp&gt;|&lt;xp max&gt;</c> : informations générales.</summary>
        [MessageAttribution("gIG")]
        public void InfosGeneral(TcpClient client, string message) => Apply(client, message, guild => guild.OnInfosPacket(message));

        /// <summary><c>gIM±&lt;membre&gt;|…</c> : membres.</summary>
        [MessageAttribution("gIM")]
        public void InfosMembers(TcpClient client, string message) => Apply(client, message, guild => guild.OnInfosPacket(message));

        /// <summary><c>gIB…</c> : personnalisation des percepteurs.</summary>
        [MessageAttribution("gIB")]
        public void InfosBoosts(TcpClient client, string message) => Apply(client, message, guild => guild.OnInfosPacket(message));

        /// <summary><c>gITM</c> / <c>gITp</c> / <c>gITP</c> : percepteurs, attaquants, défenseurs (quatrième lettre).</summary>
        [MessageAttribution("gIT")]
        public void InfosCollectors(TcpClient client, string message) => Apply(client, message, guild => guild.OnInfosPacket(message));

        /// <summary><c>gIF…</c> : enclos.</summary>
        [MessageAttribution("gIF")]
        public void InfosPaddocks(TcpClient client, string message) => Apply(client, message, guild => guild.OnInfosPacket(message));

        /// <summary><c>gIH…</c> : maisons.</summary>
        [MessageAttribution("gIH")]
        public void InfosHouses(TcpClient client, string message) => Apply(client, message, guild => guild.OnInfosPacket(message));

        /// <summary><c>gJR&lt;nom&gt;</c> : confirmation, côté invitant, que l'invitation est partie (<c>Guild.onRequestLocal</c>).</summary>
        [MessageAttribution("gJR")]
        public Task GuildInviteSent(TcpClient client, string message) => Run(client, message, guild => guild.OnJoinPacket(message));

        /// <summary><c>gJr&lt;id invitant&gt;|&lt;nom&gt;|&lt;guilde&gt;</c> : invitation reçue (<c>Guild.onRequestDistant</c>), refusée par <c>gJE&lt;id&gt;</c> sans abonné.</summary>
        [MessageAttribution("gJr")]
        public Task GuildInviteReceived(TcpClient client, string message) => Run(client, message, guild => guild.OnJoinPacket(message));

        /// <summary><c>gJE&lt;a|d|u|o|r nom|c&gt;</c> : invitation impossible ou refusée (<c>Guild.onJoinError</c>).</summary>
        [MessageAttribution("gJE")]
        public Task JoinError(TcpClient client, string message) => Run(client, message, guild => guild.OnJoinPacket(message));

        /// <summary><c>gJKa&lt;nom&gt;</c> : le joueur a rejoint la guilde ; <c>gJKj</c> : le personnage vient d'intégrer la guilde (<c>Guild.onJoinOk</c>).</summary>
        [MessageAttribution("gJK")]
        public Task JoinOk(TcpClient client, string message) => Run(client, message, guild => guild.OnJoinPacket(message));

        /// <summary><c>gJC</c> : la boîte d'invitation se ferme (<c>Guild.onJoinDistantOk</c>).</summary>
        [MessageAttribution("gJC")]
        public Task JoinClosed(TcpClient client, string message) => Run(client, message, guild => guild.OnJoinPacket(message));

        /// <summary><c>gKK&lt;a&gt;|&lt;b&gt;</c> / <c>gKK&lt;a&gt;</c> : départ ou bannissement ; <c>gKE&lt;d|a&gt;</c> : refus.</summary>
        [MessageAttribution("gK")]
        public void Kick(TcpClient client, string message) => Apply(client, message, guild => guild.OnKickPacket(message));

        /// <summary><c>gV</c> : fermeture du panneau de création.</summary>
        [MessageAttribution("gV")]
        public void Leave(TcpClient client, string message) => Apply(client, message, guild => guild.OnLeavePacket());

        /// <summary><c>gCK</c> : guilde créée ; <c>gCE&lt;a|an|ae&gt;</c> : création refusée.</summary>
        [MessageAttribution("gC")]
        public void Create(TcpClient client, string message) => Apply(client, message, guild => guild.OnCreatePacket(message));

        /// <summary><c>gn</c> : le serveur ouvre le panneau de création.</summary>
        [MessageAttribution("gn")]
        public void New(TcpClient client, string message) => Apply(client, message, guild => guild.OnNewPacket());

        /// <summary><c>gHE&lt;code&gt;</c> : pose de percepteur refusée (jamais envoyé par StarLoco, qui répond <c>Im…</c>).</summary>
        [MessageAttribution("gH")]
        public void Hire(TcpClient client, string message) => Apply(client, message, guild => guild.OnHirePacket(message));

        /// <summary><c>gT&lt;S|R|G&gt;…</c> : percepteur posé, retiré, récolté (<c>Guild.onTaxCollectorInfo</c>).</summary>
        [MessageAttribution("gT")]
        public void CollectorInfo(TcpClient client, string message) => Apply(client, message, guild => guild.OnCollectorInfoPacket(message));

        /// <summary><c>gTK…</c> : réponse à <c>gTJ</c>, que le bot n'émet jamais (matrice §2 n° 37) : journalisée, ignorée.</summary>
        [MessageAttribution("gTK")]
        public void CollectorJoined(TcpClient client, string message) => Apply(client, message, guild => guild.OnCollectorInfoPacket(message));

        /// <summary><c>gTE…</c> : erreur de <c>gTJ</c> / <c>gTV</c>, jamais émis : journalisée, ignorée.</summary>
        [MessageAttribution("gTE")]
        public void CollectorError(TcpClient client, string message) => Apply(client, message, guild => guild.OnCollectorInfoPacket(message));

        /// <summary><c>gA&lt;A|S|D&gt;…</c> : percepteur attaqué, survivant, mort.</summary>
        [MessageAttribution("gA")]
        public void CollectorAttacked(TcpClient client, string message) => Apply(client, message, guild => guild.OnCollectorAttackedPacket(message));

        /// <summary><c>gUT</c> / <c>gUF</c> : ouverture de la fenêtre sur les maisons ou les enclos.</summary>
        [MessageAttribution("gU")]
        public void UserInterface(TcpClient client, string message) => Apply(client, message, guild => guild.OnUserInterfacePacket(message));

        private static void Apply(TcpClient client, string message, Action<GuildActions> handler)
        {
            GuildActions guild = client?.account?.Game?.Interactions?.Guild;
            if (guild == null || message == null) return;
            try { handler(guild); }
            catch (Exception error)
            {
                client.account?.Logger?.LogError("GUILDE", "Paquet de guilde illisible ignoré (" + Preview(message) + ") : " + error.Message);
            }
        }

        private static async Task Run(TcpClient client, string message, Func<GuildActions, Task> handler)
        {
            GuildActions guild = client?.account?.Game?.Interactions?.Guild;
            if (guild == null || message == null) return;
            try { await handler(guild).ConfigureAwait(false); }
            catch (Exception error)
            {
                client.account?.Logger?.LogError("GUILDE", "Paquet de guilde illisible ignoré (" + Preview(message) + ") : " + error.Message);
            }
        }

        private static string Preview(string message) => message.Length > 80 ? message.Substring(0, 80) + "…" : message;
    }
}
