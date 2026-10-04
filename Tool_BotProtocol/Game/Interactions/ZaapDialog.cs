using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Interactives;

namespace Tool_BotProtocol.Game.Interactions
{
    /// <summary>Destination proposée par le serveur dans <c>WC</c>.</summary>
    public sealed class ZaapDestination
    {
        public int MapId { get; internal set; }
        public int Cost { get; internal set; }
        /// <summary>Vrai pour la carte où se trouve le personnage (coût 0 chez StarLoco).</summary>
        public bool IsCurrent { get; internal set; }
        /// <summary>Vrai pour le zaap de sauvegarde annoncé en tête du paquet.</summary>
        public bool IsSaved { get; internal set; }
        /// <summary>Coordonnées de la carte si <c>BotMaps</c> la connaît, sinon « Carte &lt;id&gt; ».</summary>
        public string Label { get; internal set; }
    }

    /// <summary>
    /// Fenêtre des zaaps selon <c>dofus.aks.Waypoints</c> du client 1.34 : ouverture par l'action
    /// <c>GA500&lt;cellule&gt;;114</c> sur le zaap de la carte, réception <c>WC&lt;sauvegarde&gt;|&lt;carte&gt;;&lt;coût&gt;|…</c>,
    /// <c>WV</c> (fermeture) et <c>WU…</c> (erreur) ; envoi <c>WU&lt;carte&gt;</c> et <c>WV</c>.
    /// </summary>
    public sealed class ZaapDialog : InteractionWindow
    {
        /// <summary>Compétence « Utiliser » du zaap : <c>case 114</c> dans <c>GameCase.startAction</c> de StarLoco.</summary>
        public const short ZaapSkill = 114;
        private List<ZaapDestination> destinations = new List<ZaapDestination>();

        /// <summary>Carte du zaap de sauvegarde annoncée en tête de <c>WC</c> ; -1 sans fenêtre.</summary>
        public int SavedMapId { get; private set; } = -1;
        public IReadOnlyList<ZaapDestination> Destinations => destinations;

        protected override string Reference => "ZAAP";
        protected override AccountStates OpenState => AccountStates.ZAAP;

        internal ZaapDialog(Accounts.Accounts account) : base(account) { }

        /// <summary>
        /// Vrai si la cellule porte le zaap de la carte : <c>BotZaaps</c>, ou un objet interactif de gfx 7000/7026/7029/4287
        /// (les gfx que <c>GameCase.canDoAction</c> de StarLoco accepte pour 114), ou à compétence 114.
        /// </summary>
        public bool IsZaapCell(short cellId)
        {
            Map map = Account?.Game?.Map;
            if (map == null) return false;
            if (Zaaps.Z.TryGetValue(map.MapID, out int zaapCell) && zaapCell == cellId) return true;
            return map.Interactives.TryGetValue(cellId, out Interactives interactive) && interactive != null && interactive.IsZaap;
        }

        /// <summary>Utilise le zaap de la carte : <c>GA500&lt;cellule&gt;;114</c>, comme <c>GameActions.sendActions</c>.</summary>
        public Task<InteractionResult> OpenAsync(short cellId)
        {
            InteractionResult refused = CheckCanOpen();
            if (refused != null) return Task.FromResult(refused);
            if (!IsZaapCell(cellId)) return Task.FromResult(Refuse("Aucun zaap connu sur la cellule " + cellId + "."));
            return SendAsync("GA500" + cellId + ";" + ZaapSkill, "Utilisation du zaap demandée ; le serveur envoie la liste des destinations.");
        }

        /// <summary>Envoie <c>WU&lt;carte&gt;</c> pour une destination de la liste.</summary>
        public Task<InteractionResult> TeleportAsync(int mapId)
        {
            if (!IsOpen) return Task.FromResult(Refuse("La fenêtre des zaaps n'est pas ouverte."));
            ZaapDestination destination = destinations.FirstOrDefault(entry => entry.MapId == mapId);
            if (destination == null) return Task.FromResult(Refuse("Cette destination n'est pas proposée par le serveur."));
            if (destination.IsCurrent) return Task.FromResult(Refuse("Vous êtes déjà sur cette carte."));
            int kamas = Account?.Game?.character?.Kamas ?? 0;
            if (destination.Cost > kamas) return Task.FromResult(Refuse("Kamas insuffisants : " + destination.Cost + " demandés, " + kamas + " disponibles."));
            return SendAsync("WU" + mapId, "Téléportation vers " + destination.Label + " demandée (" + destination.Cost + " kamas).");
        }

        /// <summary>Envoie <c>WV</c> ; le serveur confirme par <c>WV</c>.</summary>
        public Task<InteractionResult> LeaveAsync()
        {
            if (!IsOpen) return Task.FromResult(Refuse("La fenêtre des zaaps n'est pas ouverte."));
            return SendAsync("WV", "Fermeture de la fenêtre des zaaps demandée.");
        }

        /// <summary><c>WC&lt;sauvegarde&gt;|&lt;carte&gt;;&lt;coût&gt;|…</c>, lu comme <c>Waypoints.onCreate</c>.</summary>
        internal void OnList(string payload)
        {
            string[] parts = (payload ?? string.Empty).Split('|');
            if (!int.TryParse(parts[0], out int saved))
            {
                LogError("Liste de zaaps illisible : " + payload);
                Notify();
                return;
            }
            int currentMap = Account?.Game?.Map?.MapID ?? -1;
            var list = new List<ZaapDestination>();
            for (int index = 1; index < parts.Length; index++)
            {
                string[] fields = parts[index].Split(';');
                if (fields.Length < 2 || !int.TryParse(fields[0], out int mapId) || !int.TryParse(fields[1], out int cost)) continue;
                list.Add(new ZaapDestination { MapId = mapId, Cost = cost, IsCurrent = mapId == currentMap, IsSaved = mapId == saved, Label = Describe(mapId) });
            }
            SavedMapId = saved;
            destinations = list;
            MarkOpen();
            Log(list.Count + " destination(s) de zaap reçue(s).");
            Notify();
        }

        /// <summary><c>WUE</c> (ou tout <c>WU…</c>) : téléportation refusée ; la fenêtre reste ouverte.</summary>
        internal void OnUseError()
        {
            LogError("Le serveur refuse cette téléportation (zaap inconnu, kamas ou zone).");
            Notify();
        }

        /// <summary><c>WV</c> : fermeture, aussi envoyée après une téléportation réussie.</summary>
        internal void OnLeave()
        {
            bool wasOpen = IsOpen;
            Reset();
            MarkClosed();
            if (wasOpen) Log("Fenêtre des zaaps fermée.");
            Notify();
        }

        private static string Describe(int mapId)
        {
            Map known;
            return Map.AllBotMaps.TryGetValue(mapId, out known) ? known.GetCoordinates + " · carte " + mapId : "Carte " + mapId;
        }

        protected override void Reset()
        {
            SavedMapId = -1;
            destinations = new List<ZaapDestination>();
        }
    }
}
