using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Maps.Entities;

namespace Tool_BotProtocol.Game.Alignement
{
    /// <summary>
    /// Fenêtre des prismes de transport, comme <c>dofus.aks.Subway</c> en mode <c>SUBWAY_TYPE_PRISM</c> du client 1.34 : « Utiliser » un
    /// prisme de son camp envoie <c>GA512&lt;id&gt;</c> (<c>GameActions.usePrism</c>) ; StarLoco (<c>Player.openPrismeMenu</c>) répond
    /// <c>Wp&lt;carte courante&gt;|&lt;carte&gt;;&lt;coût&gt;|&lt;carte&gt;;*…</c> (<c>*</c> : prisme en combat), ou <c>Im183</c> au-delà de deux
    /// points de déshonneur ; un personnage neutre n'a aucune réponse. <c>Wp&lt;carte&gt;</c> téléporte (<c>Player.usePrisme</c> : kamas retirés,
    /// <c>As</c>, <c>GDM</c> puis <c>Ww</c>) ; <c>Ww</c> ferme. L'état ne change qu'à la réception des paquets.
    /// </summary>
    public sealed class PrismTravel : InteractionWindow
    {
        /// <summary>Action du menu « Utiliser » d'un prisme (<c>GA512</c>).</summary>
        public const int UsePrismAction = 512;
        private List<PrismDestination> destinations = new List<PrismDestination>();

        internal PrismTravel(Accounts.Accounts account) : base(account) { }

        protected override string Reference => "PRISME";
        /// <summary>Même état que les zaaps : déplacements et autres fenêtres bloqués tant que la liste est ouverte.</summary>
        protected override AccountStates OpenState => AccountStates.ZAAP;

        /// <summary>Carte du personnage annoncée en tête de <c>Wp</c> ; -1 sans fenêtre.</summary>
        public int CurrentMapId { get; private set; } = -1;
        public IReadOnlyList<PrismDestination> Destinations => destinations;

        /// <summary>« Utiliser » un prisme de la carte : <c>GA512&lt;id&gt;</c>. Les règles du client (camp) sont vérifiées par <see cref="AlignmentActions.UsePrismAsync"/>.</summary>
        internal Task<InteractionResult> RequestAsync(long prismId)
        {
            InteractionResult refused = CheckCanOpen();
            if (refused != null) return Task.FromResult(refused);
            return SendAsync("GA" + UsePrismAction.ToString(CultureInfo.InvariantCulture) + prismId.ToString(CultureInfo.InvariantCulture),
                "Utilisation du prisme demandée ; le serveur envoie la liste des prismes.");
        }

        /// <summary>Envoie <c>Wp&lt;carte&gt;</c> pour un prisme de la liste (<c>Subway.prismUse</c>).</summary>
        public Task<InteractionResult> TeleportAsync(int mapId)
        {
            if (!IsOpen) return Task.FromResult(Refuse("La liste des prismes n'est pas ouverte."));
            PrismDestination destination = destinations.FirstOrDefault(entry => entry.MapId == mapId);
            if (destination == null) return Task.FromResult(Refuse("Ce prisme n'est pas proposé par le serveur."));
            if (destination.InFight) return Task.FromResult(Refuse("Ce prisme est attaqué : le client ne le propose pas."));
            if (destination.IsCurrent) return Task.FromResult(Refuse("Vous êtes déjà sur cette carte."));
            int kamas = Account?.Game?.character?.Kamas ?? 0;
            if (destination.Cost > kamas)
                return Task.FromResult(Refuse(AlignmentTexts.Get("NOT_ENOUGH_RICH", "Tu n'as pas assez de kamas pour réaliser cette action.") + " (" + destination.Cost + " demandés, " + kamas + " disponibles)"));
            return SendAsync("Wp" + mapId.ToString(CultureInfo.InvariantCulture), "Téléportation vers " + destination.Label + " demandée (" + destination.Cost + " kamas).");
        }

        /// <summary>Envoie <c>Ww</c> (<c>Subway.prismLeave</c>) ; le serveur confirme par <c>Ww</c>.</summary>
        public Task<InteractionResult> LeaveAsync()
        {
            if (!IsOpen) return Task.FromResult(Refuse("La liste des prismes n'est pas ouverte."));
            return SendAsync("Ww", "Fermeture de la liste des prismes demandée.");
        }

        /// <summary><c>Wp&lt;carte courante&gt;|&lt;carte&gt;;&lt;coût&gt;|…</c>, lu comme <c>Subway.onPrismCreate</c>.</summary>
        internal void OnList(string payload)
        {
            string[] parts = (payload ?? string.Empty).Split('|');
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int current))
            {
                LogError("Liste de prismes illisible : " + payload);
                Notify();
                return;
            }
            int here = Account?.Game?.Map?.MapID ?? current;
            var list = new List<PrismDestination>();
            for (int index = 1; index < parts.Length; index++)
            {
                string[] fields = parts[index].Split(';');
                if (fields.Length < 2 || !int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int mapId)) continue;
                string cost = fields[1];
                bool inFight = cost.EndsWith("*", StringComparison.Ordinal);
                if (inFight) cost = cost.Substring(0, cost.Length - 1);
                int price = int.TryParse(cost, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : inFight ? -1 : 0;
                if (price < 0 && !inFight) continue;
                list.Add(new PrismDestination { MapId = mapId, Cost = Math.Max(0, price), InFight = inFight, IsCurrent = mapId == here || mapId == current, Label = AlignmentTexts.MapLabel(mapId) });
            }
            CurrentMapId = current;
            destinations = list;
            MarkOpen();
            Log(list.Count + " prisme(s) proposé(s).");
            Notify();
        }

        /// <summary><c>Ww</c> : fermeture, aussi envoyée après une téléportation réussie.</summary>
        internal void OnLeave()
        {
            bool wasOpen = IsOpen;
            Reset();
            MarkClosed();
            if (wasOpen) Log("Liste des prismes fermée.");
            Notify();
        }

        protected override void Reset()
        {
            CurrentMapId = -1;
            destinations = new List<PrismDestination>();
        }
    }
}
