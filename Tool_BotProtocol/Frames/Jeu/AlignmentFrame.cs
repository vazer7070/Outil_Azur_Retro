using System;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Alignement;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Frames.Jeu
{
    /// <summary>
    /// Alignement, conquête et prismes (propriétaire : lot F9) : préfixes <c>ZS</c>, <c>ZC</c>, <c>al</c>, <c>am</c>, <c>aM</c>, <c>GIP</c>,
    /// <c>Cb</c>, <c>CB</c>, <c>CW</c>, <c>Cp</c>, <c>CP</c>, <c>CIJ</c>, <c>CIV</c>, <c>CA</c>, <c>CD</c>, <c>CS</c>, <c>Wp</c> et <c>Ww</c>,
    /// aiguillés comme <c>dofus.aks.Specialization</c>, <c>Subareas</c>, <c>Game.onPVP</c>, <c>Conquest</c> et <c>Subway</c> du client 1.34 puis
    /// confiés au service <see cref="AlignmentActions"/> du compte (<c>Game.Interactions.Alignment</c>). <c>GP</c> reçu reste le placement
    /// de combat (<c>CombatFrame</c>) : les ailes ne s'envoient que par <c>GP+</c>, <c>GP-</c>, <c>GP*</c>. Un paquet mal formé est journalisé,
    /// jamais propagé.
    /// </summary>
    internal class AlignmentFrame : Frame
    {
        /// <summary><c>ZS&lt;id&gt;</c> : spécialisation à l'entrée en jeu (<c>Specialization.onSet</c>).</summary>
        [MessageAttribution("ZS")]
        public void SpecializationSet(TcpClient client, string message) => Apply(client, message, a => a.OnSpecialization(Body(message, 2), false));

        /// <summary><c>ZC&lt;id&gt;</c> : spécialisation changée (<c>Specialization.onChange</c>).</summary>
        [MessageAttribution("ZC")]
        public void SpecializationChanged(TcpClient client, string message) => Apply(client, message, a => a.OnSpecialization(Body(message, 2), true));

        /// <summary><c>al|&lt;sous-zone&gt;;&lt;camp&gt;|…</c> : sous-zones conquérables (<c>Subareas.onList</c>).</summary>
        [MessageAttribution("al")]
        public void SubAreaList(TcpClient client, string message) => Apply(client, message, a => a.OnZoneList(Body(message, 2)));

        /// <summary><c>am&lt;sous-zone&gt;|&lt;camp&gt;|&lt;silencieux&gt;</c> : camp d'une sous-zone (<c>Subareas.onSet</c>).</summary>
        [MessageAttribution("am")]
        public void SubAreaChanged(TcpClient client, string message) => Apply(client, message, a => a.OnZoneChanged(Body(message, 2)));

        /// <summary><c>aM&lt;zone&gt;|&lt;camp&gt;</c> : camp d'une zone (<c>Conquest.onAreaAlignmentChanged</c>).</summary>
        [MessageAttribution("aM")]
        public void AreaChanged(TcpClient client, string message) => Apply(client, message, a => a.OnAreaChanged(Body(message, 2)));

        /// <summary><c>GIP&lt;honneur perdu&gt;</c> : question avant le retrait des ailes (<c>Game.onPVP</c>).</summary>
        [MessageAttribution("GIP")]
        public void WingsQuestion(TcpClient client, string message) => Apply(client, message, a => a.OnWingsQuestion(Body(message, 3)));

        /// <summary><c>Cb&lt;monde&gt;;&lt;zone&gt;</c> : balance de conquête.</summary>
        [MessageAttribution("Cb")]
        public void Balance(TcpClient client, string message) => Apply(client, message, a => a.OnBalance(Body(message, 2)));

        /// <summary><c>CB&lt;bonus&gt;;&lt;multiplicateur&gt;;&lt;malus&gt;</c> : bonus de conquête.</summary>
        [MessageAttribution("CB")]
        public void Bonus(TcpClient client, string message) => Apply(client, message, a => a.OnBonus(Body(message, 2)));

        /// <summary><c>CW…</c> : données de conquête du monde.</summary>
        [MessageAttribution("CW")]
        public void WorldData(TcpClient client, string message) => Apply(client, message, a => a.OnWorldData(Body(message, 2)));

        /// <summary><c>Cp±…</c> : attaquants du prisme.</summary>
        [MessageAttribution("Cp")]
        public void PrismAttackers(TcpClient client, string message) => Apply(client, message, a => a.OnPrismFighters(Body(message, 2), false));

        /// <summary><c>CP±…</c> : défenseurs du prisme.</summary>
        [MessageAttribution("CP")]
        public void PrismDefenders(TcpClient client, string message) => Apply(client, message, a => a.OnPrismFighters(Body(message, 2), true));

        /// <summary><c>CIJ&lt;erreur ou 0;délai;durée;places&gt;</c> : réponse à l'onglet « Défendre ».</summary>
        [MessageAttribution("CIJ")]
        public void PrismInfos(TcpClient client, string message) => Apply(client, message, a => a.OnPrismInfos(Body(message, 3)));

        /// <summary><c>CIV</c> : le serveur ferme les informations du prisme.</summary>
        [MessageAttribution("CIV")]
        public void PrismInfosClosing(TcpClient client, string message) => Apply(client, message, a => a.OnPrismInfosClosing());

        /// <summary><c>CA&lt;carte&gt;|&lt;x&gt;|&lt;y&gt;</c> : prisme attaqué.</summary>
        [MessageAttribution("CA")]
        public void PrismAttacked(TcpClient client, string message) => Apply(client, message, a => a.OnPrismAlert('A', Body(message, 2)));

        /// <summary><c>CD&lt;carte&gt;|&lt;x&gt;|&lt;y&gt;</c> : prisme détruit.</summary>
        [MessageAttribution("CD")]
        public void PrismDied(TcpClient client, string message) => Apply(client, message, a => a.OnPrismAlert('D', Body(message, 2)));

        /// <summary><c>CS&lt;carte&gt;|&lt;x&gt;|&lt;y&gt;</c> : prisme survivant.</summary>
        [MessageAttribution("CS")]
        public void PrismSurvived(TcpClient client, string message) => Apply(client, message, a => a.OnPrismAlert('S', Body(message, 2)));

        /// <summary><c>Wp&lt;carte courante&gt;|&lt;carte&gt;;&lt;coût&gt;|…</c> : liste des prismes (<c>Subway.onPrismCreate</c>).</summary>
        [MessageAttribution("Wp")]
        public void PrismList(TcpClient client, string message) => Apply(client, message, a => a.Prism.OnList(Body(message, 2)));

        /// <summary><c>Ww</c> : fermeture de la liste des prismes (<c>Subway.onPrismClose</c>).</summary>
        [MessageAttribution("Ww")]
        public void PrismClosed(TcpClient client, string message) => Apply(client, message, a => a.Prism.OnLeave());

        private static string Body(string message, int start) => message != null && message.Length > start ? message.Substring(start) : string.Empty;

        private static void Apply(TcpClient client, string message, Action<AlignmentActions> handler)
        {
            AlignmentActions alignment = client?.account?.Game?.Interactions?.Alignment;
            if (alignment == null || message == null) return;
            try { handler(alignment); }
            catch (Exception error)
            {
                client.account?.Logger?.LogError("ALIGNEMENT", "Paquet d'alignement illisible ignoré (" + Preview(message) + ") : " + error.Message);
            }
        }

        private static string Preview(string message) => message.Length > 80 ? message.Substring(0, 80) + "…" : message;
    }
}
