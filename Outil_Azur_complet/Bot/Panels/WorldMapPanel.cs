using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Controls;
using Outil_Azur_complet.Bot.Controls.Chat;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Groupes;
using Tool_BotProtocol.Game.Maps;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>
    /// Carte du monde du client 1.34 (<c>UI_MapExplorer</c>, 742 × 433) dans le tiroir : la vue <see cref="WorldMapView"/>
    /// (tuiles de D3, zoom, déplacement), le nom de la zone survolée (<c>_lblArea</c>), les coordonnées, les boutons zoom + / −,
    /// Déplacer, Repère, Centrer sur moi (<c>_btnZoomPlus</c>…), la grille et les filtres d'indices (<c>HIC</c>). Le repère rouge
    /// suit la carte du personnage (<c>GDM</c> → <c>Map.RefreshMap</c>, coordonnées de <c>maps_fr</c>), le drapeau de la boussole
    /// suit <c>IC</c> (<c>Infos.onInfoCompass</c> → <c>select</c>), les drapeaux teintés suivent <c>IH</c> (<c>multipleSelect</c> :
    /// phénix, membres du groupe, joueur cherché). Comme le client, l'ouverture envoie <c>CWJ</c> et la fermeture <c>CWV</c>
    /// (<c>Conquest.worldInfosJoin</c> / <c>Leave</c>) ; la lecture de la réponse <c>CW</c> appartient au lot de l'alignement,
    /// qui pose les prismes par <see cref="SetPrisms"/>. Le mode Repère fixe la boussole du bandeau sans rien envoyer
    /// (<c>GameManager.updateCompass</c>) ; <c>IM</c> et la commande <c>BaM</c> (droits d'administration) ne sont pas émis.
    /// Les événements réseau arrivent sur d'autres fils et repassent par <see cref="GamePanel.OnUi"/>.
    /// </summary>
    public sealed class WorldMapPanel : GamePanel
    {
        private const string Reference = "CARTE";
        private WorldMapView view;
        private Label areaLabel, coordsLabel, status;
        private TrackBar zoomSlider;
        private Control zoomIn, zoomOut, move, select, center;
        private CheckBox grid;
        private FlowLayoutPanel filters;
        private readonly ToolTip tips = new ToolTip { InitialDelay = 300, ReshowDelay = 100, ShowAlways = true };
        private PartyActions party;
        private Map boundMap;
        private PanelHost subscribedHost;
        private bool opened, conquestJoined, syncingZoom;
        private int loadedSuperArea = -1, filterCount = -1;

        public override string Title => Text("WORLD_MAP", "Carte du monde");
        public override Image Icon => ClientAssets.Icon("icone-carte", 24);
        /// <summary>La vue de la carte (tests, diagnostic).</summary>
        public WorldMapView MapView => view;
        /// <summary>Vrai entre l'envoi de <c>CWJ</c> à l'ouverture et celui de <c>CWV</c> à la fermeture.</summary>
        public bool IsConquestJoined => conquestJoined;
        /// <summary>Texte de la zone affiché sous le titre (vide hors survol).</summary>
        public string AreaText => areaLabel?.Text ?? string.Empty;

        protected override Control CreateView()
        {
            var page = Page();
            areaLabel = MakeLabel(string.Empty, 9, true); areaLabel.Dock = DockStyle.Top; areaLabel.Height = 20; areaLabel.AutoEllipsis = true;
            areaLabel.Name = "worldmap-area"; areaLabel.AccessibleName = Text("AREA", "Région");
            coordsLabel = MakeLabel(string.Empty, 8.25f); coordsLabel.Dock = DockStyle.Top; coordsLabel.Height = 16; coordsLabel.ForeColor = BotUi.Muted;
            coordsLabel.Name = "worldmap-coords";

            var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, BackColor = Color.Transparent, Margin = new Padding(0), Padding = new Padding(0, 2, 0, 2) };
            zoomOut = Tool("−", Text("MAP_EXPLORER_ZOOM_MINOUS", "Diminuer le zoom"), (s, e) => SetMode(WorldMapMode.ZoomOut), 32, "worldmap-zoom-out");
            zoomSlider = new TrackBar { Minimum = WorldMapView.MinZoom, Maximum = WorldMapView.MaxZoom, TickFrequency = 10, SmallChange = WorldMapView.ZoomStep, LargeChange = 10,
                Value = WorldMapView.DefaultZoom, Width = 104, Height = 26, AutoSize = false, TickStyle = TickStyle.None, BackColor = BotUi.Paper, Margin = new Padding(0, 4, 2, 0),
                Name = "worldmap-zoom", AccessibleName = Text("ZOOM", "Zoom") };
            zoomSlider.ValueChanged += (s, e) => { if (syncingZoom || view == null) return; syncingZoom = true; try { view.Zoom = zoomSlider.Value; } finally { syncingZoom = false; } };
            zoomIn = Tool("+", Text("MAP_EXPLORER_ZOOM_PLUS", "Augmenter le zoom"), (s, e) => SetMode(WorldMapMode.ZoomIn), 32, "worldmap-zoom-in");
            move = Tool("Déplacer", Text("MAP_EXPLORER_MOVE", "Déplacer la carte"), (s, e) => SetMode(WorldMapMode.Move), 76, "worldmap-move");
            select = Tool("Repère", Text("MAP_EXPLORER_SELECT", "Poser un repère"), (s, e) => SetMode(WorldMapMode.Select), 68, "worldmap-select");
            center = Tool("Centrer", Text("MAP_EXPLORER_CENTER", "Centrer la carte sur ma position"), (s, e) => CenterOnMe(), 68, "worldmap-center");
            tools.Controls.AddRange(new[] { zoomOut, zoomSlider, zoomIn, move, select, center });

            filters = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, BackColor = Color.Transparent, Margin = new Padding(0), Padding = new Padding(0, 0, 0, 3), Name = "worldmap-filters" };
            grid = Filter(Text("OPTION_GRID", "Afficher la grille"), WorldMapView.StyleColor("Yellow"), "worldmap-grid");
            grid.CheckedChanged += (s, e) => { if (view != null) view.ShowGrid = grid.Checked; };
            filters.Controls.Add(grid);

            view = new WorldMapView { Dock = DockStyle.Fill, Margin = new Padding(0) };
            view.ZoomChanged += (s, e) => { if (syncingZoom) return; syncingZoom = true; try { zoomSlider.Value = Math.Max(zoomSlider.Minimum, Math.Min(zoomSlider.Maximum, view.Zoom)); } finally { syncingZoom = false; } };
            view.HoverChanged += (s, e) => RefreshHover();
            view.ModeChanged += (s, e) => RefreshModeButtons();
            view.CoordinateSelected += (s, e) => OnCoordinateSelected(e.Coordinates);
            view.DataLoaded += (s, e) => { RefreshFilters(); RefreshView(); };

            status = MakeStatus(string.Empty); status.Name = "worldmap-status"; status.Height = 20;
            // Ordre d'ancrage : le dernier ajouté prend le bord en premier.
            page.Controls.Add(view); page.Controls.Add(filters); page.Controls.Add(tools); page.Controls.Add(coordsLabel); page.Controls.Add(areaLabel); page.Controls.Add(status);
            RefreshModeButtons(); RefreshFilters();
            return page;
        }

        private Control Tool(string text, string tip, EventHandler action, int width, string name)
        {
            Control button = MakeButton(text, action, false, width);
            button.Height = 28; button.Margin = new Padding(0, 0, 3, 0); button.Name = name; button.AccessibleName = tip;
            tips.SetToolTip(button, tip);
            return button;
        }

        private CheckBox Filter(string text, Color color, string name)
        {
            var box = new CheckBox { Text = text, AutoSize = true, ForeColor = color, Font = BotFonts.Get(8.25f, FontStyle.Bold), BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 8, 0), Name = name, AccessibleName = text, UseVisualStyleBackColor = false, Cursor = Cursors.Hand };
            tips.SetToolTip(box, text);
            return box;
        }

        protected override void OnBind(GameClass game)
        {
            boundMap = game.Map;
            if (boundMap != null) boundMap.RefreshMap += OnMapChanged;
            party = game.Interactions?.Party;
            if (party != null)
            {
                party.CompassChanged += OnCompass;
                party.LocationsReceived += OnLocations;
            }
            subscribedHost = Host;
            if (subscribedHost != null) { subscribedHost.PanelShown += OnPanelShown; subscribedHost.PanelClosed += OnPanelClosed; }
            view?.SetCompassTarget(party?.Compass);
            view?.SetHighlights(Flags(party?.Locations));
        }

        protected override void OnUnbind(GameClass game)
        {
            if (boundMap != null) boundMap.RefreshMap -= OnMapChanged;
            if (party != null) { party.CompassChanged -= OnCompass; party.LocationsReceived -= OnLocations; }
            if (subscribedHost != null) { subscribedHost.PanelShown -= OnPanelShown; subscribedHost.PanelClosed -= OnPanelClosed; }
            boundMap = null; party = null; subscribedHost = null; conquestJoined = false;
            view?.SetCompassTarget(null); view?.SetHighlights(null); view?.SetCurrentMap(null);
        }

        /// <summary>Repère rouge et super-zone depuis la carte du personnage (<c>initWorldMap</c> / <c>showMapSuperArea</c>).</summary>
        public override void RefreshView()
        {
            if (view == null) return;
            Map map = Game?.Map;
            if (map == null || map.MapID == 0) { view.SetCurrentMap(null); status.Text = Connected ? "Carte du personnage inconnue." : "Connectez le personnage pour situer sa carte."; return; }
            Point coords = LangData.Map.Coords(map.MapID) ?? new Point(map.X, map.Y);
            int superArea = WorldMapAreas.Current.SuperAreaOf(map.MapID) ?? Math.Max(0, loadedSuperArea);
            bool centre = false;
            if (superArea != loadedSuperArea)
            {
                loadedSuperArea = superArea;
                view.SetSuperArea(superArea);
                centre = true;
            }
            view.SetCurrentMap(coords);
            if (centre || !opened) view.SetMapPosition(coords.X, coords.Y);
            string name = LangData.IsLoaded("maps") && LangData.Map.Has(map.MapID) ? LangData.Map.Name(map.MapID) : "Carte " + map.MapID.ToString(CultureInfo.InvariantCulture);
            status.Text = name + " [" + coords.X.ToString(CultureInfo.InvariantCulture) + "," + coords.Y.ToString(CultureInfo.InvariantCulture) + "]"
                + (view.TilesReady && view.TileCount == 0 ? " · tuiles de la super-zone " + superArea.ToString(CultureInfo.InvariantCulture) + " absentes (fond uni)" : string.Empty);
        }

        /// <summary>Prismes de la catégorie « Territoires de conquête » (lus dans <c>CW</c> par le lot de l'alignement) ; une liste vide les retire.</summary>
        public void SetPrisms(IEnumerable<WorldMapHint> prisms)
        {
            var list = (prisms ?? new WorldMapHint[0]).ToArray();
            OnUi(() => view?.SetPrisms(list));
        }

        private void SetMode(WorldMapMode mode) { if (view != null) view.Mode = mode; }

        /// <summary>Comme le client : le bouton du mode actif est désactivé, les flèches ne servent qu'en déplacement.</summary>
        private void RefreshModeButtons()
        {
            if (view == null) return;
            zoomIn.Enabled = view.Mode != WorldMapMode.ZoomIn;
            zoomOut.Enabled = view.Mode != WorldMapMode.ZoomOut;
            move.Enabled = view.Mode != WorldMapMode.Move;
            select.Enabled = view.Mode != WorldMapMode.Select;
        }

        /// <summary>Une case par catégorie d'indices de <c>HIC</c>, dans l'ordre du client, après la grille.</summary>
        private void RefreshFilters()
        {
            if (view == null) return;
            IReadOnlyList<WorldMapHintCategory> categories = view.HintCategories;
            if (categories.Count == filterCount) return;
            filterCount = categories.Count;
            foreach (Control old in filters.Controls.Cast<Control>().Where(c => !ReferenceEquals(c, grid)).ToArray()) { filters.Controls.Remove(old); old.Dispose(); }
            foreach (WorldMapHintCategory category in categories)
            {
                int id = category.Id;
                CheckBox box = Filter(category.Name, WorldMapView.StyleColor(category.Color), "worldmap-filter-" + id.ToString(CultureInfo.InvariantCulture));
                box.Checked = view.IsHintCategoryVisible(id);
                box.CheckedChanged += (s, e) => view.SetHintCategoryVisible(id, box.Checked);
                filters.Controls.Add(box);
            }
        }

        private void RefreshHover()
        {
            if (view == null) return;
            Point? cell = view.Hovered;
            coordsLabel.Text = cell == null ? string.Empty : cell.Value.X.ToString(CultureInfo.InvariantCulture) + ", " + cell.Value.Y.ToString(CultureInfo.InvariantCulture);
            string text = view.HoveredText;
            areaLabel.Text = text.Length == 0 ? string.Empty : Text("AREA", "Région") + " : " + text;
        }

        private void CenterOnMe()
        {
            if (view == null) return;
            Point? current = view.CurrentMap;
            if (current == null) { Feedback("Carte du personnage inconnue : rien à centrer."); return; }
            view.SetMapPosition(current.Value.X, current.Value.Y);
        }

        /// <summary><c>select</c> : la boussole du bandeau vise les coordonnées choisies et le drapeau est posé, sans rien envoyer.</summary>
        private void OnCoordinateSelected(Point coordinates)
        {
            view?.SetCompassTarget(coordinates);
            (Host?.FindForm() as GameClientFullform)?.Banner?.SetCompassTarget(coordinates);
            Feedback(ChatLinks.DescribeCompass(coordinates.X, coordinates.Y, Game?.Map));
        }

        private void OnMapChanged() => OnUi(RefreshView);
        private void OnCompass(Point? target) => OnUi(() => view?.SetCompassTarget(target));
        private void OnLocations(IReadOnlyList<PartyLocation> locations)
        {
            WorldMapFlag[] flags = Flags(locations);
            OnUi(() => view?.SetHighlights(flags));
        }

        /// <summary>Drapeaux de <c>multipleSelect</c> : type 1 phénix, 2 membre du groupe « x,y (nom) », 3 joueur cherché, autres rouges.</summary>
        private WorldMapFlag[] Flags(IReadOnlyList<PartyLocation> locations)
        {
            var result = new List<WorldMapFlag>();
            if (locations == null) return result.ToArray();
            foreach (PartyLocation location in locations)
            {
                if (location == null) continue;
                string coords = location.X.ToString(CultureInfo.InvariantCulture) + "," + location.Y.ToString(CultureInfo.InvariantCulture);
                switch (location.Type)
                {
                    case 1: result.Add(new WorldMapFlag(location.X, location.Y, WorldMapView.FlagPhoenixColor, coords)); break;
                    case PartyLocation.PartyMemberType:
                    {
                        string name = location.PlayerId.HasValue ? party?.Group?.Find(location.PlayerId.Value)?.Name : null;
                        if (string.IsNullOrEmpty(name)) name = location.PlayerName;
                        if (string.IsNullOrEmpty(name)) continue; // le client retire l'entrée d'un membre inconnu
                        result.Add(new WorldMapFlag(location.X, location.Y, WorldMapView.FlagGroupColor, coords + " (" + name + ")"));
                        break;
                    }
                    case 3: result.Add(new WorldMapFlag(location.X, location.Y, WorldMapView.FlagSeekColor, coords + " (" + location.PlayerName + ")")); break;
                    default: result.Add(new WorldMapFlag(location.X, location.Y, WorldMapView.FlagOthersColor, coords)); break;
                }
            }
            return result.ToArray();
        }

        /// <summary>Ouverture du volet (<c>initData</c>) : <c>CWJ</c>, puis la super-zone et le repère ; la position est conservée entre deux ouvertures.</summary>
        private void OnPanelShown(object sender, PanelEventArgs e)
        {
            if (!ReferenceEquals(e.Panel, this)) return;
            bool first = !opened;
            RefreshView();
            opened = true;
            if (first && view?.CurrentMap != null) view.SetMapPosition(view.CurrentMap.Value.X, view.CurrentMap.Value.Y);
            if (!conquestJoined && Connected) { conquestJoined = true; _ = SendAsync("CWJ"); }
            view?.Focus();
        }

        /// <summary>Fermeture du volet (<c>destroy</c>) : <c>CWV</c>.</summary>
        private void OnPanelClosed(object sender, PanelEventArgs e)
        {
            if (!ReferenceEquals(e.Panel, this) || !conquestJoined) return;
            conquestJoined = false;
            if (Connected) _ = SendAsync("CWV");
        }

        private async Task SendAsync(string packet)
        {
            var connection = Account?.Connexion;
            if (connection == null || !connection.IsConnected()) return;
            try
            {
                await connection.SendPacket(packet).ConfigureAwait(false);
                Account?.Logger?.LogDebug(Reference, packet + " envoyé (carte du monde).");
            }
            catch (Exception error) { Account?.Logger?.LogException(Reference, error); }
        }

        private static string Text(string key, string fallback)
        {
            try { if (LangData.Text.Has(key)) return LangData.Text.Plain(LangData.Text.Get(key)); }
            catch (Exception) { /* Fichier de langue illisible : texte de repli. */ }
            return fallback;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tips.Dispose();
            base.Dispose(disposing);
        }
    }
}
