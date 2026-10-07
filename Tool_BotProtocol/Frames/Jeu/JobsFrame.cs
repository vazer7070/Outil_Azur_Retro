using System;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Jobs;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
    /// <summary>
    /// Métiers et artisanat (propriétaire : lot F6) : <c>JS</c>, <c>JX</c>, <c>JN</c>, <c>JR</c>, <c>JO</c> (<c>dofus.aks.Job</c>) ; atelier
    /// <c>EcK</c>, <c>EcE</c>, <c>EA</c>, <c>Ea</c> ; livre des artisans <c>EJ</c> ; référencement <c>Ej</c> ; mode public <c>EW</c>
    /// (<c>dofus.aks.Exchange</c>). Les fenêtres d'échange elles-mêmes (<c>ECK3</c>, <c>ECK14</c>, <c>EMK</c>, <c>EmK</c>, <c>EV</c>) passent par
    /// le registre des échanges. Un paquet mal formé est journalisé, jamais fatal.
    /// </summary>
    internal class JobsFrame : Frame
    {
        private static void Run(TcpClient client, string message, Action<JobsActions> action)
        {
            JobsActions jobs = client?.account?.Game?.Interactions?.Jobs;
            if (jobs == null) return;
            try { action(jobs); }
            catch (Exception error) when (!(error is OutOfMemoryException))
            {
                client.account?.Logger?.LogError("MÉTIERS", "Paquet ignoré (" + Preview(message) + ") : " + error.Message);
            }
        }

        private static string Preview(string message) => message == null ? string.Empty : message.Length > 80 ? message.Substring(0, 80) + "…" : message;

        [MessageAttribution("JS")]
        public void GetJobsSkills(TcpClient client, string message) => Run(client, message, jobs => jobs.OnSkills(message.Substring(2)));

        [MessageAttribution("JX")]
        public void GetExpInJob(TcpClient client, string message) => Run(client, message, jobs => jobs.OnExperience(message.Substring(2)));

        /// <summary><c>JN&lt;métier&gt;|&lt;niveau&gt;</c>.</summary>
        [MessageAttribution("JN")]
        public void JobLevel(TcpClient client, string message) => Run(client, message, jobs => jobs.OnLevel(message.Substring(2)));

        /// <summary><c>JR&lt;métier&gt;</c>.</summary>
        [MessageAttribution("JR")]
        public void JobRemoved(TcpClient client, string message) => Run(client, message, jobs => jobs.OnRemove(message.Substring(2)));

        /// <summary><c>JO&lt;position&gt;|&lt;options&gt;|&lt;cases minimum&gt;</c>.</summary>
        [MessageAttribution("JO")]
        public void JobOptionsChanged(TcpClient client, string message) => Run(client, message, jobs => jobs.OnOptions(message.Substring(2)));

        /// <summary><c>EcK;&lt;modèle&gt;…</c> : objet créé.</summary>
        [MessageAttribution("EcK")]
        public void CraftSuccess(TcpClient client, string message) => Run(client, message, jobs => jobs.OnCraft(true, message.Substring(3)));

        /// <summary><c>EcEI</c> / <c>EcEF</c> : recette inconnue ou échec.</summary>
        [MessageAttribution("EcE")]
        public void CraftError(TcpClient client, string message) => Run(client, message, jobs => jobs.OnCraft(false, message.Substring(3)));

        /// <summary><c>EA&lt;n&gt;</c> : étape d'une série de fabrication.</summary>
        [MessageAttribution("EA")]
        public void CraftLoop(TcpClient client, string message) => Run(client, message, jobs => jobs.OnCraftLoop(message.Substring(2)));

        /// <summary><c>Ea&lt;code&gt;</c> : fin d'une série de fabrication.</summary>
        [MessageAttribution("Ea")]
        public void CraftLoopEnd(TcpClient client, string message) => Run(client, message, jobs => jobs.OnCraftLoopEnd(message.Substring(2)));

        /// <summary><c>EJ±…</c> : artisan ajouté ou retiré du livre des artisans.</summary>
        [MessageAttribution("EJ")]
        public void CrafterList(TcpClient client, string message) => Run(client, message, jobs => jobs.OnCrafterListChanged(message.Substring(2)));

        /// <summary><c>Ej±&lt;métier&gt;</c> : référencement dans le livre des artisans.</summary>
        [MessageAttribution("Ej")]
        public void CrafterReference(TcpClient client, string message) => Run(client, message, jobs => jobs.OnCrafterReference(message.Substring(2)));

        /// <summary><c>EW±[&lt;id&gt;|&lt;compétences&gt;]</c> : mode public.</summary>
        [MessageAttribution("EW")]
        public void PublicMode(TcpClient client, string message) => Run(client, message, jobs => jobs.OnPublicMode(message.Substring(2)));
    }
}
