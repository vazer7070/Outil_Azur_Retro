using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Interactives;

namespace Tool_BotProtocol.Game.Interactions
{
    /// <summary>
    /// Destination proposée par le serveur dans <c>Wc</c>, nommée comme <c>dofus.datacenter.Subway</c> : un repère
    /// (table <c>HI</c> des textes <c>hints</c>) de la carte, rangé dans sa catégorie (<c>HIC</c>).
    /// </summary>
    public sealed class ZaapiDestination
    {
        public int MapId { get; internal set; }
        public int Cost { get; internal set; }
        /// <summary>Vrai pour la carte où se trouve le personnage.</summary>
        public bool IsCurrent { get; internal set; }
        /// <summary>Nom du repère, ou « Carte &lt;id&gt; » si la carte n'a pas de repère.</summary>
        public string Name { get; internal set; }
        /// <summary>Catégorie du repère (1 lieux de classes, 2 hôtels de vente, 3 ateliers…), 0 sans repère.</summary>
        public int CategoryId { get; internal set; }
        public string Category { get; internal set; }
        /// <summary>« [x,y] » si les textes des cartes sont chargés, sinon vide.</summary>
        public string Coordinates { get; internal set; }
        /// <summary>Vrai si la destination vient d'un repère : le client n'affiche que celles-là.</summary>
        public bool HasHint { get; internal set; }
        public string Label => Coordinates.Length == 0 ? Name : Name + " " + Coordinates;
    }

    /// <summary>
    /// Fenêtre des zaapis selon <c>dofus.aks.Subway</c> du client 1.34 : ouverture par <c>GA500&lt;cellule&gt;;157</c> sur un
    /// zaapi (gfx 7030/7031), réception <c>Wc&lt;carte courante&gt;|&lt;carte&gt;;&lt;coût&gt;|…</c>, <c>Wv</c> (fermeture) et
    /// <c>Wu…</c> (erreur, <c>CANT_USE_SUBWAY</c>) ; envoi <c>Wu&lt;carte&gt;</c> et <c>Wv</c>. Jamais <c>WU</c>, réservé aux zaaps.
    /// </summary>
    public sealed class ZaapiDialog : InteractionWindow
    {
        /// <summary>Compétence « Se faire transporter » : <c>case 157</c> dans <c>GameCase.startAction</c> de StarLoco.</summary>
        public const short ZaapiSkill = InteractiveGfx.ZaapiSkill;
        private List<ZaapiDestination> destinations = new List<ZaapiDestination>();

        /// <summary>Carte annoncée en tête de <c>Wc</c> (celle du personnage chez StarLoco) ; -1 sans fenêtre.</summary>
        public int CurrentMapId { get; private set; } = -1;
        /// <summary>Toutes les destinations reçues, un élément par repère (une carte sans repère donne un élément « Carte &lt;id&gt; »).</summary>
        public IReadOnlyList<ZaapiDestination> Destinations => destinations;
        /// <summary>
        /// Destinations affichées par le client : celles qui ont un repère. Si les textes <c>hints</c> ne sont pas chargés,
        /// toutes les destinations, pour que le bot reste utilisable.
        /// </summary>
        public IReadOnlyList<ZaapiDestination> VisibleDestinations =>
            LangData.Count("hints", "HI") == 0 ? destinations : destinations.Where(entry => entry.HasHint).ToList();

        protected override string Reference => "ZAAPI";
        protected override AccountStates OpenState => AccountStates.ZAAP;

        internal ZaapiDialog(Accounts.Accounts account) : base(account) { }

        /// <summary>Vrai si la cellule porte un zaapi : gfx 7030/7031 (<c>GameCase.canDoAction</c>) ou compétence 157.</summary>
        public bool IsZaapiCell(short cellId)
        {
            Map map = Account?.Game?.Map;
            return map != null && map.Interactives.TryGetValue(cellId, out Interactives interactive) && interactive != null && interactive.IsZaapi;
        }

        /// <summary>Utilise le zaapi de la cellule : <c>GA500&lt;cellule&gt;;157</c>.</summary>
        public Task<InteractionResult> OpenAsync(short cellId)
        {
            InteractionResult refused = CheckCanOpen();
            if (refused != null) return Task.FromResult(refused);
            if (!IsZaapiCell(cellId)) return Task.FromResult(Refuse("Aucun zaapi connu sur la cellule " + cellId + "."));
            return SendAsync("GA500" + cellId + ";" + ZaapiSkill, "Utilisation du zaapi demandée ; le serveur envoie la liste des destinations.");
        }

        /// <summary>Envoie <c>Wu&lt;carte&gt;</c> pour une destination de la liste.</summary>
        public Task<InteractionResult> TeleportAsync(int mapId)
        {
            if (!IsOpen) return Task.FromResult(Refuse("La fenêtre des zaapis n'est pas ouverte."));
            ZaapiDestination destination = destinations.FirstOrDefault(entry => entry.MapId == mapId);
            if (destination == null) return Task.FromResult(Refuse("Cette destination n'est pas proposée par le serveur."));
            if (destination.IsCurrent) return Task.FromResult(Refuse("Vous êtes déjà sur cette carte."));
            // StarLoco retire le prix sans vérifier le solde (Player.Zaapi_use) : le bot refuse avant d'envoyer.
            int kamas = Account?.Game?.character?.Kamas ?? 0;
            if (destination.Cost > kamas) return Task.FromResult(Refuse("Kamas insuffisants : " + destination.Cost + " demandés, " + kamas + " disponibles."));
            return SendAsync("Wu" + mapId.ToString(CultureInfo.InvariantCulture),
                "Transport vers " + destination.Label + " demandé (" + destination.Cost + " kamas).");
        }

        /// <summary>Envoie <c>Wv</c> ; le serveur confirme par <c>Wv</c>.</summary>
        public Task<InteractionResult> LeaveAsync()
        {
            if (!IsOpen) return Task.FromResult(Refuse("La fenêtre des zaapis n'est pas ouverte."));
            return SendAsync("Wv", "Fermeture de la fenêtre des zaapis demandée.");
        }

        /// <summary>
        /// <c>Wc&lt;carte&gt;|&lt;carte&gt;;&lt;coût&gt;|…</c>, lu comme <c>Subway.onCreate</c> : chaque destination devient un élément
        /// par repère de la carte (<c>getHintsByMapID</c>). StarLoco termine la liste par « | » : l'entrée vide est ignorée.
        /// </summary>
        internal void OnList(string payload)
        {
            string[] parts = (payload ?? string.Empty).Split('|');
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int current))
            {
                LogError("Liste de zaapis illisible : " + Shorten(payload));
                Notify();
                return;
            }
            int currentMap = Account?.Game?.Map?.MapID ?? current;
            Dictionary<int, List<IReadOnlyDictionary<string, string>>> hints = HintsByMap();
            var list = new List<ZaapiDestination>();
            for (int index = 1; index < parts.Length; index++)
            {
                if (parts[index].Length == 0) continue;
                string[] fields = parts[index].Split(';');
                if (fields.Length < 2 || !int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int mapId)
                    || !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int cost) || cost < 0) continue;
                string coordinates = Coordinates(mapId);
                if (hints.TryGetValue(mapId, out List<IReadOnlyDictionary<string, string>> mapHints))
                {
                    foreach (IReadOnlyDictionary<string, string> hint in mapHints)
                    {
                        int category = Int(hint, "c");
                        list.Add(new ZaapiDestination
                        {
                            MapId = mapId, Cost = cost, IsCurrent = mapId == currentMap, HasHint = true, Coordinates = coordinates,
                            Name = Value(hint, "n", "Carte " + mapId), CategoryId = category, Category = CategoryName(category)
                        });
                    }
                }
                else
                {
                    list.Add(new ZaapiDestination
                    {
                        MapId = mapId, Cost = cost, IsCurrent = mapId == currentMap, HasHint = false, Coordinates = coordinates,
                        Name = "Carte " + mapId, CategoryId = 0, Category = "Autres"
                    });
                }
            }
            CurrentMapId = current;
            destinations = list.OrderBy(entry => entry.CategoryId == 0 ? int.MaxValue : entry.CategoryId)
                .ThenBy(entry => entry.Name + entry.MapId, StringComparer.CurrentCultureIgnoreCase).ToList();
            MarkOpen();
            Log(VisibleDestinations.Count + " destination(s) de zaapi reçue(s).");
            Notify();
        }

        /// <summary><c>Wu…</c> reçu du serveur : téléportation refusée (<c>CANT_USE_SUBWAY</c>) ; la fenêtre reste ouverte.</summary>
        internal void OnUseError()
        {
            LogError(LangData.Text.Has("CANT_USE_SUBWAY") ? LangData.Text.Get("CANT_USE_SUBWAY").Trim() : "Le serveur refuse ce transport.");
            Notify();
        }

        /// <summary><c>Wv</c> : fermeture, aussi envoyée par StarLoco après un transport.</summary>
        internal void OnLeave()
        {
            bool wasOpen = IsOpen;
            Reset();
            MarkClosed();
            if (wasOpen) Log("Fenêtre des zaapis fermée.");
            Notify();
        }

        protected override void Reset()
        {
            CurrentMapId = -1;
            destinations = new List<ZaapiDestination>();
        }

        private static Dictionary<int, List<IReadOnlyDictionary<string, string>>> HintsByMap()
        {
            var byMap = new Dictionary<int, List<IReadOnlyDictionary<string, string>>>();
            foreach (string id in LangData.Ids("hints", "HI"))
            {
                IReadOnlyDictionary<string, string> hint = LangData.Raw("hints", "HI", id);
                if (hint == null || !hint.TryGetValue("m", out string text)
                    || !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int mapId)) continue;
                if (!byMap.TryGetValue(mapId, out List<IReadOnlyDictionary<string, string>> list)) byMap[mapId] = list = new List<IReadOnlyDictionary<string, string>>();
                list.Add(hint);
            }
            return byMap;
        }

        private static string CategoryName(int category)
        {
            IReadOnlyDictionary<string, string> entry = category > 0 ? LangData.Raw("hints", "HIC", category.ToString(CultureInfo.InvariantCulture)) : null;
            return Value(entry, "n", category > 0 ? "Catégorie " + category : "Autres");
        }

        private static string Coordinates(int mapId)
        {
            Point? point = LangData.Map.Coords(mapId);
            if (point.HasValue) return "[" + point.Value.X + "," + point.Value.Y + "]";
            return Map.AllBotMaps.TryGetValue(mapId, out Map known) ? known.GetCoordinates : string.Empty;
        }

        private static string Value(IReadOnlyDictionary<string, string> entry, string key, string fallback) =>
            entry != null && entry.TryGetValue(key, out string value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : fallback;

        private static int Int(IReadOnlyDictionary<string, string> entry, string key) =>
            entry != null && entry.TryGetValue(key, out string value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) ? number : 0;

        private static string Shorten(string text) => text == null ? string.Empty : text.Length <= 80 ? text : text.Substring(0, 80) + "…";
    }
}
