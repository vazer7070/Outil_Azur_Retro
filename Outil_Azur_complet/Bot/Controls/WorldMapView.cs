using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game.Data;

namespace Outil_Azur_complet.Bot.Controls
{
    /// <summary>Modes d'interaction du <c>MapNavigator</c> du client (<c>interactionMode</c>).</summary>
    public enum WorldMapMode { Move, Select, ZoomIn, ZoomOut }

    /// <summary>Drapeau posé sur une carte (<c>UI_MapExplorerFlag</c> teinté) : positions <c>IH</c>, cible de la boussole.</summary>
    public sealed class WorldMapFlag
    {
        public WorldMapFlag(int x, int y, int rgb, string label) { X = x; Y = y; Rgb = rgb & 0xFFFFFF; Label = label ?? string.Empty; }
        public int X { get; }
        public int Y { get; }
        /// <summary>Couleur <c>0xRRGGBB</c> appliquée à l'instance <c>_mcColor</c> du drapeau.</summary>
        public int Rgb { get; }
        public string Label { get; }
    }

    /// <summary>Indice de la carte du monde (<c>HI</c> de <c>hints_fr</c>, ou prisme d'un paquet <c>CW</c>) : icône <c>hints/&lt;gfx&gt;.png</c> sur une carte.</summary>
    public sealed class WorldMapHint
    {
        public WorldMapHint(int category, int gfx, int x, int y, string name, int superArea)
        { Category = category; Gfx = gfx; X = x; Y = y; Name = name ?? string.Empty; SuperArea = superArea; }
        public int Category { get; }
        public int Gfx { get; }
        public int X { get; }
        public int Y { get; }
        public string Name { get; }
        /// <summary>Super-zone de la carte de l'indice (<c>MA.a[zone].sua</c>) : seuls les indices de la super-zone affichée sont dessinés.</summary>
        public int SuperArea { get; }
        /// <summary>Infobulle du client : « x,y (nom) ».</summary>
        public string Tooltip => X.ToString(CultureInfo.InvariantCulture) + "," + Y.ToString(CultureInfo.InvariantCulture) + " (" + Name + ")";
    }

    /// <summary>Catégorie d'indices (<c>HIC</c>) : identifiant, couleur du style du bouton (<c>Orange</c>, <c>Blue</c>…) et nom.</summary>
    public sealed class WorldMapHintCategory
    {
        public WorldMapHintCategory(int id, string color, string name) { Id = id; Color = color ?? string.Empty; Name = name ?? string.Empty; }
        public int Id { get; }
        public string Color { get; }
        public string Name { get; }
    }

    /// <summary>
    /// Index des coordonnées → zone / sous-zone, comme <c>AreasManager</c> du client : pour chaque carte de <c>maps_fr</c>,
    /// la clé « superZone_x_y » reçoit la première zone et sous-zone rencontrées. Construit hors du fil de l'interface
    /// depuis <see cref="LangData"/> ; vide quand les textes des cartes ne sont pas chargés.
    /// </summary>
    public sealed class WorldMapAreas
    {
        private static WorldMapAreas cached;
        private static readonly object sync = new object();
        private readonly Dictionary<string, int> areas = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> subAreas = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<int, int> superAreaOfSubArea = new Dictionary<int, int>();
        private readonly Dictionary<int, int> superAreaOfMap = new Dictionary<int, int>();
        private readonly List<WorldMapHint> hints = new List<WorldMapHint>();
        private readonly List<WorldMapHintCategory> categories = new List<WorldMapHintCategory>();
        private readonly int generation;

        private WorldMapAreas(int generation) { this.generation = generation; }

        public static readonly WorldMapAreas Empty = new WorldMapAreas(-1);

        /// <summary>Nombre de couples de coordonnées indexés.</summary>
        public int Count => areas.Count;
        public IReadOnlyList<WorldMapHint> Hints => hints;
        public IReadOnlyList<WorldMapHintCategory> Categories => categories;

        /// <summary>Zone et sous-zone des coordonnées dans la super-zone, ou <c>false</c> (<c>getAreaIDFromCoordinates</c>).</summary>
        public bool TryGet(int superArea, int x, int y, out int areaId, out int subAreaId)
        {
            string key = Key(superArea, x, y);
            subAreaId = 0;
            return areas.TryGetValue(key, out areaId) && subAreas.TryGetValue(key, out subAreaId);
        }

        /// <summary>Super-zone d'une carte (<c>carte.sousZone → sousZone.zone → zone.superZone</c>), <c>null</c> si inconnue.</summary>
        public int? SuperAreaOf(int mapId) => superAreaOfMap.TryGetValue(mapId, out int value) ? value : (int?)null;
        public int? SuperAreaOfSubArea(int subAreaId) => superAreaOfSubArea.TryGetValue(subAreaId, out int value) ? value : (int?)null;

        /// <summary>Index à jour des textes chargés : construit (sur le pool de threads) seulement si les textes ont changé.</summary>
        public static Task<WorldMapAreas> GetAsync()
        {
            int generation = LangData.EntryCount;
            lock (sync) if (cached != null && cached.generation == generation) return Task.FromResult(cached);
            return Task.Factory.StartNew(() => Build(generation), CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
        }

        /// <summary>Index déjà construit pour les textes actuels, sinon <see cref="Empty"/> (ne construit rien).</summary>
        public static WorldMapAreas Current
        {
            get { int generation = LangData.EntryCount; lock (sync) return cached != null && cached.generation == generation ? cached : Empty; }
        }

        private static WorldMapAreas Build(int generation)
        {
            var result = new WorldMapAreas(generation);
            try
            {
                var superAreaOfArea = new Dictionary<int, int>();
                foreach (string id in LangData.Ids("maps", "zone"))
                    if (Int(id) is int areaId) superAreaOfArea[areaId] = Int(LangData.Raw("maps", "zone", id)?["superZone"]) ?? 0;
                var areaOfSubArea = new Dictionary<int, int>();
                foreach (string id in LangData.Ids("maps", "sousZone"))
                    if (Int(id) is int subId && Int(LangData.Raw("maps", "sousZone", id)?["zone"]) is int areaId)
                    {
                        areaOfSubArea[subId] = areaId;
                        result.superAreaOfSubArea[subId] = superAreaOfArea.TryGetValue(areaId, out int sua) ? sua : 0;
                    }
                foreach (string id in LangData.Ids("maps", "carte"))
                {
                    IReadOnlyDictionary<string, string> map = LangData.Raw("maps", "carte", id);
                    if (map == null || !(Int(id) is int mapId) || !(Int(Value(map, "x")) is int x) || !(Int(Value(map, "y")) is int y)) continue;
                    if (!(Int(Value(map, "sousZone")) is int subId) || !areaOfSubArea.TryGetValue(subId, out int areaId)) continue;
                    int superArea = result.superAreaOfSubArea.TryGetValue(subId, out int value) ? value : 0;
                    result.superAreaOfMap[mapId] = superArea;
                    string key = Key(superArea, x, y);
                    if (!result.areas.ContainsKey(key)) { result.areas[key] = areaId; result.subAreas[key] = subId; }
                }
                foreach (string id in LangData.Ids("hints", "HIC"))
                {
                    IReadOnlyDictionary<string, string> row = LangData.Raw("hints", "HIC", id);
                    if (row != null && Int(id) is int category) result.categories.Add(new WorldMapHintCategory(category, Value(row, "c"), Value(row, "n")));
                }
                result.categories.Sort((a, b) => a.Id.CompareTo(b.Id));
                foreach (string id in LangData.Ids("hints", "HI"))
                {
                    IReadOnlyDictionary<string, string> row = LangData.Raw("hints", "HI", id);
                    if (row == null || !(Int(Value(row, "m")) is int mapId)) continue;
                    Point? coords = LangData.Map.Coords(mapId);
                    if (coords == null) continue;
                    int superArea = result.superAreaOfMap.TryGetValue(mapId, out int sua) ? sua : 0;
                    result.hints.Add(new WorldMapHint(Int(Value(row, "c")) ?? 0, Int(Value(row, "g")) ?? 0, coords.Value.X, coords.Value.Y, Value(row, "n"), superArea));
                }
            }
            catch (Exception) { /* Textes incomplets : l'index reste partiel, jamais d'exception vers l'interface. */ }
            lock (sync) cached = result;
            return result;
        }

        private static string Value(IReadOnlyDictionary<string, string> row, string name) => row != null && row.TryGetValue(name, out string value) ? value : null;
        private static int? Int(string text) => int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value) ? value : (int?)null;
        private static string Key(int superArea, int x, int y) =>
            superArea.ToString(CultureInfo.InvariantCulture) + "_" + x.ToString(CultureInfo.InvariantCulture) + "_" + y.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Carte du monde du client 1.34 (<c>ank.gapi.controls.MapNavigator</c> dans <c>UI_MapExplorer</c>) : tuiles
    /// <c>WorldMap/&lt;superZone&gt;/&lt;x_y&gt;.png</c> exportées de <c>clips/maps/&lt;superZone&gt;.swf</c> (D3), chaque carte
    /// occupant 40 × 23 pixels au zoom 100 et chaque tuile 15 × 15 cartes posées en (x × 600, y × 345) plus le décalage
    /// de <c>tuiles.tsv</c> ; le centre de la vue est le centre de la carte <see cref="Current"/> (<c>setMapPosition</c>),
    /// le zoom va de 10 à 100 par pas de 5 (molette, boutons). Par-dessus : contour de la sous-zone survolée
    /// (<c>addSubareaClip</c>, <c>AREA_NO_ALIGNMENT_COLOR</c>), grille, indices (<c>hints/&lt;gfx&gt;.png</c>) filtrés par
    /// catégorie, rectangle rouge de la carte actuelle (<c>MAP_CURRENT_POSITION</c>), rectangle bleu d'une carte visée
    /// (<c>MAP_WAYPOINT_POSITION</c>), drapeau de la boussole et drapeaux <c>IH</c> teintés (<c>FLAG_MAP_*</c>).
    /// Lecture des tables et des PNG hors du fil de l'interface (<see cref="ClientAssets.GetAsync"/>) ; le dessin ne lit
    /// jamais le disque. Les images partagées du cache ne sont jamais libérées ici.
    /// </summary>
    public sealed class WorldMapView : Control
    {
        /// <summary>Largeur et hauteur d'une carte au zoom 100 (<c>wPage</c> / <c>hPage</c> de <c>UI_MapExplorer</c>).</summary>
        public const int PageWidth = 40, PageHeight = 23;
        /// <summary>Cartes par tuile (<c>setMapPosition</c>) et taille d'une tuile au zoom 100.</summary>
        public const int TileMaps = 15, TileWidth = PageWidth * TileMaps, TileHeight = PageHeight * TileMaps;
        public const int MinZoom = 10, MaxZoom = 100, ZoomStep = 5, DefaultZoom = 50;
        /// <summary>Couleurs des rectangles et drapeaux (<c>dofus.Constants</c>).</summary>
        public const int CurrentPositionColor = 0xFF0000, WaypointPositionColor = 0x0000FF, FlagSeekColor = 0xCCFF00, FlagGroupColor = 0x006699,
            FlagPhoenixColor = 0xFF0000, FlagOthersColor = 0xFF0000, NoAlignmentAreaColor = 0xFFFF99;
        /// <summary>Flèches de déplacement : couleur au repos (<c>OUT_TRIANGLE_TRANSFORM</c>) et au survol (<c>OVER_TRIANGLE_TRANSFORM</c>).</summary>
        private static readonly Color ArrowColor = Color.FromArgb(184, 177, 143), ArrowHoverColor = Color.FromArgb(255, 102, 0);
        private const int ArrowSize = 14;
        /// <summary>Les huit directions du client (<c>MapExplorer.DIRECTIONS</c>) et leur pas (<c>moveMap</c>).</summary>
        private static readonly string[] Directions = { "NW", "N", "NE", "W", "E", "SW", "S", "SE" };
        private static readonly Point[] DirectionSteps = { new Point(-1, -1), new Point(0, -1), new Point(1, -1), new Point(-1, 0), new Point(1, 0), new Point(-1, 1), new Point(0, 1), new Point(1, 1) };

        private sealed class TileInfo { public int X, Y, Width, Height; }
        private sealed class SubAreaInfo { public int X, Y, Width, Height; }
        private sealed class TileTable
        {
            public int SuperArea;
            public readonly Dictionary<string, TileInfo> Tiles = new Dictionary<string, TileInfo>(StringComparer.Ordinal);
            public readonly Dictionary<int, SubAreaInfo> SubAreas = new Dictionary<int, SubAreaInfo>();
        }

        private readonly object sync = new object();
        private readonly Dictionary<string, Bitmap> images = new Dictionary<string, Bitmap>(StringComparer.Ordinal);
        private readonly HashSet<string> pending = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<int> hiddenCategories = new HashSet<int>();
        private TileTable table;
        private WorldMapAreas areas = WorldMapAreas.Empty;
        private int superArea, zoom = DefaultZoom, loadGeneration, pendingLoaded;
        private Point current;
        private Point? currentMap, waypoint, compass, dragCell, hover;
        private WorldMapFlag[] highlights = new WorldMapFlag[0];
        private WorldMapHint[] prisms = new WorldMapHint[0];
        private WorldMapMode mode = WorldMapMode.Move;
        private bool showGrid, disposed;
        private int hoveredArrow = -1, hoveredSubArea = -1;
        private Bitmap subAreaOverlay;
        private int subAreaOverlayId = -1;
        private string hoveredText = string.Empty;

        public WorldMapView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = BotUi.Paper; Size = new Size(742, 433); TabStop = true;
            Name = "world-map"; AccessibleName = "Carte du monde";
            _ = WorldMapAreas.GetAsync().ContinueWith(task => { if (task.Status == TaskStatus.RanToCompletion) ApplyAreas(task.Result); }, TaskScheduler.Default);
        }

        // ----- État -----

        /// <summary>Super-zone affichée (<c>Map.superarea</c>, dossier <c>WorldMap/&lt;n&gt;</c>) ; 0 = continent principal.</summary>
        public int SuperArea => superArea;
        /// <summary>Carte dont le centre est au centre de la vue (<c>currentX</c> / <c>currentY</c>).</summary>
        public Point Current => current;
        public WorldMapMode Mode { get { return mode; } set { if (mode == value) return; mode = value; dragCell = null; Invalidate(); ModeChanged?.Invoke(this, EventArgs.Empty); } }
        public bool ShowGrid { get { return showGrid; } set { if (showGrid == value) return; showGrid = value; Invalidate(); } }
        /// <summary>Zoom de 10 à 100 (<c>MapNavigator.zoom</c>) ; toute valeur hors bornes est ramenée aux bornes.</summary>
        public int Zoom
        {
            get { return zoom; }
            set
            {
                int bounded = Math.Max(MinZoom, Math.Min(MaxZoom, value));
                if (bounded == zoom) return;
                zoom = bounded; Invalidate(); ZoomChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        /// <summary>Carte actuelle du personnage (rectangle rouge), ou <c>null</c>.</summary>
        public Point? CurrentMap => currentMap;
        /// <summary>Carte visée (rectangle bleu, volet ouvert depuis un zaap par exemple), ou <c>null</c>.</summary>
        public Point? Waypoint => waypoint;
        /// <summary>Cible de la boussole (drapeau sans teinte), ou <c>null</c>.</summary>
        public Point? CompassTarget => compass;
        public IReadOnlyList<WorldMapFlag> Highlights => highlights;
        public IReadOnlyList<WorldMapHint> Prisms => prisms;
        /// <summary>Index des zones utilisé (celui des textes chargés, construit hors du fil de l'interface).</summary>
        public WorldMapAreas Areas => areas;
        public IReadOnlyList<WorldMapHintCategory> HintCategories => areas.Categories;
        /// <summary>Texte de la zone survolée (« Zone (Sous-zone) »), vide hors de la carte ou sans texte.</summary>
        public string HoveredText => hoveredText;
        /// <summary>Coordonnées sous la souris, ou <c>null</c> hors de la vue.</summary>
        public Point? Hovered => hover;
        /// <summary>Nombre de tuiles de la super-zone (table lue) et nombre de PNG de tuiles déjà décodés.</summary>
        public int TileCount { get { lock (sync) return table != null && table.SuperArea == superArea ? table.Tiles.Count : 0; } }
        public int LoadedTileCount { get { lock (sync) return table == null ? 0 : table.Tiles.Keys.Count(name => images.TryGetValue(TileKey(table.SuperArea, name), out Bitmap image) && image != null); } }
        /// <summary>Vrai quand la table des tuiles de la super-zone a été lue (même vide).</summary>
        public bool TilesReady { get { lock (sync) return table != null && table.SuperArea == superArea; } }

        public event EventHandler ZoomChanged;
        public event EventHandler ModeChanged;
        /// <summary>Souris déplacée sur une autre carte (ou sortie : <c>null</c>) ; le texte de zone est dans <see cref="HoveredText"/>.</summary>
        public event EventHandler HoverChanged;
        /// <summary>Clic en mode <see cref="WorldMapMode.Select"/> : coordonnées choisies (<c>select</c> du client).</summary>
        public event EventHandler<WorldMapSelectEventArgs> CoordinateSelected;
        /// <summary>Tuiles ou index chargés : la vue vient d'être redessinée avec de nouvelles données.</summary>
        public event EventHandler DataLoaded;

        // ----- Commandes -----

        /// <summary><c>showMapSuperArea</c> : lit <c>tuiles.tsv</c> et <c>sous-zones.tsv</c> de la super-zone hors du fil de l'interface.</summary>
        public void SetSuperArea(int value)
        {
            superArea = value;
            int generation = Interlocked.Increment(ref loadGeneration);
            string root = ClientAssets.Root;
            lock (sync) { images.Clear(); pending.Clear(); }
            ReleaseOverlay();
            Invalidate();
            Task.Factory.StartNew(() => ReadTables(root, value), CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default)
                .ContinueWith(task =>
                {
                    if (task.Status != TaskStatus.RanToCompletion || generation != Volatile.Read(ref loadGeneration)) return;
                    lock (sync) table = task.Result;
                    RequestRedraw(true);
                }, TaskScheduler.Default);
        }

        /// <summary><c>setMapPosition</c> : met le centre de la carte (x, y) au centre de la vue.</summary>
        public void SetMapPosition(int x, int y)
        {
            if (current.X == x && current.Y == y) return;
            current = new Point(x, y); Invalidate();
        }
        /// <summary><c>moveMap</c> : décale la vue d'un nombre de cartes.</summary>
        public void MoveMap(int dx, int dy) => SetMapPosition(current.X + dx, current.Y + dy);

        public void SetCurrentMap(Point? value) { if (currentMap == value) return; currentMap = value; Invalidate(); }
        public void SetWaypoint(Point? value) { if (waypoint == value) return; waypoint = value; Invalidate(); }
        public void SetCompassTarget(Point? value) { if (compass == value) return; compass = value; Invalidate(); }
        /// <summary>Drapeaux des positions <c>IH</c> (<c>multipleSelect</c>) ; une liste vide les efface.</summary>
        public void SetHighlights(IEnumerable<WorldMapFlag> flags) { highlights = (flags ?? new WorldMapFlag[0]).Where(f => f != null).ToArray(); Invalidate(); }
        /// <summary>Prismes de la catégorie « Territoires de conquête » (paquet <c>CW</c> lu par le lot de l'alignement) ; vide = aucun.</summary>
        public void SetPrisms(IEnumerable<WorldMapHint> values) { prisms = (values ?? new WorldMapHint[0]).Where(h => h != null).ToArray(); Invalidate(); }
        /// <summary>Affiche ou masque une catégorie d'indices (<c>showHintsCategory</c>).</summary>
        public void SetHintCategoryVisible(int category, bool visible)
        {
            bool changed = visible ? hiddenCategories.Remove(category) : hiddenCategories.Add(category);
            if (changed) Invalidate();
        }
        public bool IsHintCategoryVisible(int category) => !hiddenCategories.Contains(category);

        /// <summary>Indices dessinés : ceux de la super-zone affichée dont la catégorie est visible, plus les prismes.</summary>
        public IReadOnlyList<WorldMapHint> VisibleHints
        {
            get
            {
                var result = new List<WorldMapHint>();
                foreach (WorldMapHint hint in areas.Hints) if (hint.SuperArea == superArea && !hiddenCategories.Contains(hint.Category)) result.Add(hint);
                foreach (WorldMapHint prism in prisms) if (prism.SuperArea == superArea && !hiddenCategories.Contains(prism.Category)) result.Add(prism);
                return result;
            }
        }

        // ----- Géométrie (MapNavigator) -----

        /// <summary>Largeur et hauteur d'une carte au zoom courant (<c>virtualWPage</c> / <c>virtualHPage</c>).</summary>
        public float CellWidth => PageWidth * zoom / 100f;
        public float CellHeight => PageHeight * zoom / 100f;

        /// <summary>Rectangle d'une carte dans la vue (<c>getRealFromCoordinates</c>, origine au centre de la vue).</summary>
        public RectangleF CellBounds(int x, int y) => CellBounds(x, y, ClientSize);
        private RectangleF CellBounds(int x, int y, Size size) =>
            new RectangleF(size.Width / 2f + CellWidth * (x - current.X - 0.5f), size.Height / 2f + CellHeight * (y - current.Y - 0.5f), CellWidth, CellHeight);

        /// <summary>Carte sous un point de la vue (<c>getCoordinatesFromReal</c>).</summary>
        public Point MapAt(Point client) => MapAt(client, ClientSize);
        private Point MapAt(Point client, Size size)
        {
            int x = (int)Math.Floor((client.X - size.Width / 2f + CellWidth * 0.5f) / CellWidth) + current.X;
            int y = (int)Math.Floor((client.Y - size.Height / 2f + CellHeight * 0.5f) / CellHeight) + current.Y;
            return new Point(x, y);
        }

        /// <summary>Survol d'un point de la vue (<c>onMouseMove</c> → <c>overMap</c>) ; renvoie le texte de zone (<see cref="HoveredText"/>).</summary>
        public string HoverAt(Point client)
        {
            Point cell = MapAt(client);
            int arrow = mode == WorldMapMode.Move ? ArrowAt(client) : -1;
            if (arrow != hoveredArrow) { hoveredArrow = arrow; Invalidate(); }
            if (hover == cell) return hoveredText;
            hover = cell;
            UpdateHoveredArea(cell);
            HoverChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
            return hoveredText;
        }

        /// <summary>Sortie de la souris (<c>outMap</c>) : plus de sous-zone surlignée ni de texte.</summary>
        public void ClearHover()
        {
            bool changed = hover != null || hoveredSubArea != -1 || hoveredArrow != -1;
            hover = null; hoveredSubArea = -1; hoveredArrow = -1; hoveredText = string.Empty;
            if (changed) { HoverChanged?.Invoke(this, EventArgs.Empty); Invalidate(); }
        }

        /// <summary>Clic sur un point de la vue selon le mode (<c>_btnLocateClick</c>) ; vrai si le clic a eu un effet.</summary>
        public bool ClickAt(Point client)
        {
            if (mode == WorldMapMode.Move)
            {
                int arrow = ArrowAt(client);
                if (arrow < 0) return false;
                MoveMap(DirectionSteps[arrow].X, DirectionSteps[arrow].Y);
                return true;
            }
            Point cell = MapAt(client);
            switch (mode)
            {
                case WorldMapMode.ZoomIn: Zoom = zoom + ZoomStep; return true;
                case WorldMapMode.ZoomOut: Zoom = zoom - ZoomStep; return true;
                case WorldMapMode.Select: CoordinateSelected?.Invoke(this, new WorldMapSelectEventArgs(cell)); return true;
            }
            return false;
        }

        // ----- Rendu -----

        /// <summary>Dessine la vue entière dans <paramref name="graphics"/> pour une taille donnée (sans lire le disque).</summary>
        public void Render(Graphics graphics, Size size)
        {
            if (graphics == null || size.Width < 1 || size.Height < 1) return;
            using (var back = new SolidBrush(BackColor)) graphics.FillRectangle(back, 0, 0, size.Width, size.Height);
            TileTable tiles;
            lock (sync) tiles = table != null && table.SuperArea == superArea ? table : null;
            DrawTiles(graphics, size, tiles);
            DrawSubArea(graphics, size, tiles);
            if (showGrid) DrawGrid(graphics, size);
            DrawHints(graphics, size);
            if (currentMap != null) DrawRectangle(graphics, size, currentMap.Value, CurrentPositionColor);
            if (waypoint != null && waypoint != currentMap) DrawRectangle(graphics, size, waypoint.Value, WaypointPositionColor);
            foreach (WorldMapFlag flag in highlights) DrawFlag(graphics, size, flag.X, flag.Y, Color.FromArgb(255, Color.FromArgb(flag.Rgb)));
            if (compass != null) DrawFlag(graphics, size, compass.Value.X, compass.Value.Y, Color.FromArgb(235, 227, 203));
            if (mode == WorldMapMode.Move) DrawArrows(graphics, size);
            using (var pen = new Pen(BotUi.Gold)) graphics.DrawRectangle(pen, 0, 0, size.Width - 1, size.Height - 1);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            try { Render(e.Graphics, ClientSize); }
            catch (Exception error) when (error is ArgumentException || error is InvalidOperationException || error is System.Runtime.InteropServices.ExternalException)
            {
                // Image partagée libérée ou GDI+ occupé : on garde le fond, le prochain dessin reprendra.
                using (var back = new SolidBrush(BackColor)) e.Graphics.FillRectangle(back, ClientRectangle);
            }
        }

        private void DrawTiles(Graphics graphics, Size size, TileTable current)
        {
            if (current == null || current.Tiles.Count == 0) return;
            Point first = MapAt(new Point(0, 0), size), last = MapAt(new Point(size.Width, size.Height), size);
            int tx0 = (int)Math.Floor(first.X / (double)TileMaps), tx1 = (int)Math.Floor(last.X / (double)TileMaps);
            int ty0 = (int)Math.Floor(first.Y / (double)TileMaps), ty1 = (int)Math.Floor(last.Y / (double)TileMaps);
            float scale = zoom / 100f;
            var state = graphics.Save();
            graphics.InterpolationMode = zoom >= MaxZoom ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBilinear;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            try
            {
                for (int tx = tx0; tx <= tx1; tx++)
                    for (int ty = ty0; ty <= ty1; ty++)
                    {
                        string name = tx.ToString(CultureInfo.InvariantCulture) + "_" + ty.ToString(CultureInfo.InvariantCulture);
                        if (!current.Tiles.TryGetValue(name, out TileInfo tile)) continue;
                        Bitmap image = ImageFor(TileKey(current.SuperArea, name), "WorldMap", current.SuperArea.ToString(CultureInfo.InvariantCulture) + "/" + name);
                        if (image == null) continue;
                        RectangleF origin = CellBounds(tx * TileMaps, ty * TileMaps, size);
                        var target = new RectangleF(origin.X + tile.X * scale, origin.Y + tile.Y * scale, tile.Width * scale, tile.Height * scale);
                        lock (image) graphics.DrawImage(image, target);
                    }
            }
            finally { graphics.Restore(state); }
        }

        private void DrawSubArea(Graphics graphics, Size size, TileTable current)
        {
            if (current == null || hoveredSubArea < 0 || !current.SubAreas.TryGetValue(hoveredSubArea, out SubAreaInfo info)) return;
            Bitmap overlay = SubAreaOverlay(current, hoveredSubArea);
            if (overlay == null) return;
            float scale = zoom / 100f;
            RectangleF origin = CellBounds(0, 0, size);
            var target = new RectangleF(origin.X + info.X * scale, origin.Y + info.Y * scale, info.Width * scale, info.Height * scale);
            var state = graphics.Save();
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear; graphics.PixelOffsetMode = PixelOffsetMode.Half;
            try { graphics.DrawImage(overlay, target); } finally { graphics.Restore(state); }
        }

        /// <summary>Contour de la sous-zone teinté comme <c>addSubareaClip(id, AREA_NO_ALIGNMENT_COLOR, 20)</c> ; recalculé à chaque changement de sous-zone.</summary>
        private Bitmap SubAreaOverlay(TileTable current, int subArea)
        {
            if (subAreaOverlay != null && subAreaOverlayId == subArea) return subAreaOverlay;
            string key = "sub/" + current.SuperArea.ToString(CultureInfo.InvariantCulture) + "/" + subArea.ToString(CultureInfo.InvariantCulture);
            Bitmap source = ImageFor(key, "WorldMap", current.SuperArea.ToString(CultureInfo.InvariantCulture) + "/sous-zones/" + subArea.ToString(CultureInfo.InvariantCulture));
            if (source == null) return null;
            ReleaseOverlay();
            Bitmap tinted = ClientAssets.Tinted(source, NoAlignmentAreaColor);
            if (tinted == null) return null;
            // _alpha = 20 : l'aplat est posé à 20 % par-dessus les tuiles.
            var faded = new Bitmap(tinted.Width, tinted.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                using (var g = Graphics.FromImage(faded))
                using (var attributes = new System.Drawing.Imaging.ImageAttributes())
                {
                    attributes.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.2f });
                    g.DrawImage(tinted, new Rectangle(0, 0, tinted.Width, tinted.Height), 0, 0, tinted.Width, tinted.Height, GraphicsUnit.Pixel, attributes);
                }
            }
            catch { faded.Dispose(); tinted.Dispose(); throw; }
            tinted.Dispose();
            subAreaOverlay = faded; subAreaOverlayId = subArea;
            return faded;
        }

        private void DrawGrid(Graphics graphics, Size size)
        {
            Point first = MapAt(new Point(0, 0), size), last = MapAt(new Point(size.Width, size.Height), size);
            using (var pen = new Pen(Color.FromArgb(90, BotUi.Frame)))
            {
                for (int x = first.X; x <= last.X + 1; x++) { float px = CellBounds(x, 0, size).X; graphics.DrawLine(pen, px, 0, px, size.Height); }
                for (int y = first.Y; y <= last.Y + 1; y++) { float py = CellBounds(0, y, size).Y; graphics.DrawLine(pen, 0, py, size.Width, py); }
            }
        }

        private void DrawHints(Graphics graphics, Size size)
        {
            float scale = Math.Max(0.5f, zoom / 100f);
            var state = graphics.Save();
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear; graphics.PixelOffsetMode = PixelOffsetMode.Half;
            try
            {
                foreach (WorldMapHint hint in VisibleHints)
                {
                    RectangleF cell = CellBounds(hint.X, hint.Y, size);
                    if (cell.Right < -64 || cell.Bottom < -64 || cell.X > size.Width + 64 || cell.Y > size.Height + 64) continue;
                    string gfx = hint.Gfx.ToString(CultureInfo.InvariantCulture);
                    Bitmap icon = ImageFor("hint/" + gfx, "WorldMap", "hints/" + gfx);
                    float cx = cell.X + cell.Width / 2, cy = cell.Y + cell.Height / 2;
                    if (icon == null)
                    {
                        // Icône absente : pastille de la couleur de la catégorie, pour que l'indice reste visible.
                        float r = Math.Max(3f, 5f * scale);
                        using (var brush = new SolidBrush(CategoryColor(hint.Category))) graphics.FillEllipse(brush, cx - r, cy - r, 2 * r, 2 * r);
                        continue;
                    }
                    int width, height;
                    lock (icon) { width = icon.Width; height = icon.Height; }
                    // PNG exporté à l'échelle 2 : moitié de sa taille au zoom 100, puis le zoom du calque _mcXtra.
                    float w = width / 2f * scale, h = height / 2f * scale;
                    lock (icon) graphics.DrawImage(icon, new RectangleF(cx - w / 2, cy - h / 2, w, h));
                }
            }
            finally { graphics.Restore(state); }
        }

        /// <summary>Couleur de repli d'une catégorie d'indices (styles <c>&lt;Couleur&gt;MapHintCheckButton</c> du client).</summary>
        public static Color CategoryColor(int category)
        {
            switch (category)
            {
                case 1: return Color.FromArgb(230, 140, 30);   // Orange
                case 2: return Color.FromArgb(60, 110, 200);   // Blue
                case 3: return Color.FromArgb(90, 150, 60);    // Green
                case 4: return Color.FromArgb(205, 180, 130);  // Beige
                case 5: return Color.FromArgb(190, 40, 40);    // Red
                case 6: return Color.FromArgb(130, 70, 160);   // Violet
                default: return Color.FromArgb(220, 190, 60); // Yellow (grille)
            }
        }

        /// <summary>Couleur d'un style de bouton de filtre (<c>HIC.c</c>) : <c>Orange</c>, <c>Blue</c>, <c>Green</c>, <c>Beige</c>, <c>Red</c>, <c>Violet</c>, <c>Yellow</c>.</summary>
        public static Color StyleColor(string style)
        {
            switch ((style ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "orange": return CategoryColor(1);
                case "blue": return CategoryColor(2);
                case "green": return CategoryColor(3);
                case "beige": return CategoryColor(4);
                case "red": return CategoryColor(5);
                case "violet": return CategoryColor(6);
                default: return CategoryColor(0);
            }
        }

        private void DrawRectangle(Graphics graphics, Size size, Point cell, int rgb)
        {
            RectangleF bounds = CellBounds(cell.X, cell.Y, size);
            if (bounds.Right < 0 || bounds.Bottom < 0 || bounds.X > size.Width || bounds.Y > size.Height) return;
            var state = graphics.Save();
            graphics.SmoothingMode = SmoothingMode.None;
            try
            {
                using (var pen = new Pen(Color.FromArgb(255, Color.FromArgb(rgb)), 2f))
                    graphics.DrawRectangle(pen, bounds.X + 1, bounds.Y + 1, Math.Max(1, bounds.Width - 2), Math.Max(1, bounds.Height - 2));
            }
            finally { graphics.Restore(state); }
        }

        /// <summary>Drapeau du client (mât sombre, fanion teinté par <c>_mcColor</c>) planté au centre de la carte, à l'échelle du zoom (au moins 60 %).</summary>
        private void DrawFlag(Graphics graphics, Size size, int x, int y, Color color)
        {
            RectangleF cell = CellBounds(x, y, size);
            if (cell.Right < -40 || cell.Bottom < -40 || cell.X > size.Width + 40 || cell.Y > size.Height + 40) return;
            float scale = Math.Max(0.6f, zoom / 100f);
            float cx = cell.X + cell.Width / 2, baseY = cell.Y + cell.Height / 2;
            float height = 26 * scale, width = 16 * scale;
            var state = graphics.Save();
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            try
            {
                var pennant = new[] { new PointF(cx, baseY - height), new PointF(cx + width, baseY - height + 6 * scale), new PointF(cx, baseY - height + 12 * scale) };
                using (var fill = new SolidBrush(color)) graphics.FillPolygon(fill, pennant);
                using (var edge = new Pen(Color.FromArgb(120, 60, 60, 60), 1f)) graphics.DrawPolygon(edge, pennant);
                using (var pole = new Pen(Color.FromArgb(50, 48, 35), Math.Max(2f, 3 * scale))) graphics.DrawLine(pole, cx, baseY - height, cx, baseY);
            }
            finally { graphics.Restore(state); }
        }

        private void DrawArrows(Graphics graphics, Size size)
        {
            var state = graphics.Save();
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            try
            {
                for (int index = 0; index < Directions.Length; index++)
                {
                    PointF[] triangle = ArrowShape(index, size);
                    using (var brush = new SolidBrush(index == hoveredArrow ? ArrowHoverColor : ArrowColor)) graphics.FillPolygon(brush, triangle);
                }
            }
            finally { graphics.Restore(state); }
        }

        /// <summary>Triangle d'une direction, pointe vers l'extérieur, au bord ou au coin de la vue.</summary>
        private static PointF[] ArrowShape(int index, Size size)
        {
            Rectangle box = ArrowBox(index, size);
            Point step = DirectionSteps[index];
            float cx = box.X + box.Width / 2f, cy = box.Y + box.Height / 2f, half = ArrowSize / 2f;
            // Pointe dans la direction (dx, dy), base perpendiculaire.
            float length = (float)Math.Sqrt(step.X * step.X + step.Y * step.Y);
            float dx = step.X / length, dy = step.Y / length;
            var tip = new PointF(cx + dx * half, cy + dy * half);
            var left = new PointF(cx - dx * half + dy * half, cy - dy * half - dx * half);
            var right = new PointF(cx - dx * half - dy * half, cy - dy * half + dx * half);
            return new[] { tip, left, right };
        }

        private static Rectangle ArrowBox(int index, Size size)
        {
            Point step = DirectionSteps[index];
            int margin = 3, box = ArrowSize + 4;
            int x = step.X < 0 ? margin : step.X > 0 ? size.Width - box - margin : (size.Width - box) / 2;
            int y = step.Y < 0 ? margin : step.Y > 0 ? size.Height - box - margin : (size.Height - box) / 2;
            return new Rectangle(x, y, box, box);
        }

        private int ArrowAt(Point client)
        {
            for (int index = 0; index < Directions.Length; index++) if (ArrowBox(index, ClientSize).Contains(client)) return index;
            return -1;
        }

        // ----- Survol : zone et sous-zone (overMap) -----

        private void UpdateHoveredArea(Point cell)
        {
            string text = string.Empty;
            int subArea = -1;
            if (areas.TryGet(superArea, cell.X, cell.Y, out int areaId, out int subAreaId))
            {
                string areaName = LangData.Map.AreaName(areaId), subName = LangData.Map.SubAreaName(subAreaId);
                text = subName.StartsWith("//", StringComparison.Ordinal) ? areaName : areaName + " (" + subName + ")";
                subArea = subAreaId;
            }
            hoveredText = text;
            hoveredSubArea = subArea;
        }

        // ----- Chargement hors du fil de l'interface -----

        private static string TileKey(int superArea, string name) => "tile/" + superArea.ToString(CultureInfo.InvariantCulture) + "/" + name;

        /// <summary>Image déjà décodée pour la clé, sinon demande son chargement (pool de threads) et rend <c>null</c> pour ce dessin.</summary>
        private Bitmap ImageFor(string key, string family, string name)
        {
            lock (sync)
            {
                if (images.TryGetValue(key, out Bitmap cached)) return cached;
                if (!pending.Add(key)) return null;
            }
            int generation = Volatile.Read(ref loadGeneration);
            Task<Bitmap> load;
            try { load = ClientAssets.GetAsync(family, name); }
            catch (Exception) { lock (sync) { pending.Remove(key); images[key] = null; } return null; }
            load.ContinueWith(task =>
            {
                Bitmap image = task.Status == TaskStatus.RanToCompletion ? task.Result : null;
                lock (sync)
                {
                    pending.Remove(key);
                    if (generation == Volatile.Read(ref loadGeneration)) images[key] = image;
                }
                if (image != null) RequestRedraw(false);
            }, TaskScheduler.Default);
            return null;
        }

        private void ApplyAreas(WorldMapAreas value)
        {
            if (value == null) return;
            areas = value;
            RequestRedraw(true);
        }

        /// <summary>Redessine depuis n'importe quel fil ; sans poignée, la création de la poignée dessinera l'état à jour et lèvera
        /// <see cref="DataLoaded"/> sur le fil de l'interface (jamais depuis le pool de threads).</summary>
        private void RequestRedraw(bool loaded)
        {
            if (disposed) return;
            if (loaded) Interlocked.Exchange(ref pendingLoaded, 1);
            try
            {
                if (!IsHandleCreated) return;
                if (InvokeRequired) BeginInvoke((Action)FlushRedraw);
                else FlushRedraw();
            }
            catch (InvalidOperationException) { /* poignée en cours de destruction ou contrôle libéré */ }
        }

        /// <summary>Sur le fil de l'interface : redessine et signale les données arrivées depuis le dernier dessin.</summary>
        private void FlushRedraw()
        {
            if (disposed || IsDisposed) return;
            Invalidate();
            if (Interlocked.Exchange(ref pendingLoaded, 0) == 1) DataLoaded?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            FlushRedraw();
        }

        /// <summary>Lit <c>tuiles.tsv</c> et <c>sous-zones/sous-zones.tsv</c> d'une super-zone ; une table absente ou illisible donne une table vide.</summary>
        private static TileTable ReadTables(string root, int superArea)
        {
            var result = new TileTable { SuperArea = superArea };
            try
            {
                string folder = Path.Combine(root ?? string.Empty, "WorldMap", superArea.ToString(CultureInfo.InvariantCulture));
                foreach (string[] fields in ReadTsv(Path.Combine(folder, "tuiles.tsv")))
                {
                    string name = fields[0];
                    int underscore = name.IndexOf('_', 1);
                    if (underscore < 0 || !TryInt(name.Substring(0, underscore), out int _) || !TryInt(name.Substring(underscore + 1), out int _)) continue;
                    if (!TryInt(fields[1], out int x) || !TryInt(fields[2], out int y) || !TryInt(fields[3], out int width) || !TryInt(fields[4], out int height)) continue;
                    if (width < 1 || height < 1) continue;
                    result.Tiles[name] = new TileInfo { X = x, Y = y, Width = width, Height = height };
                }
                foreach (string[] fields in ReadTsv(Path.Combine(folder, "sous-zones", "sous-zones.tsv")))
                {
                    if (!TryInt(fields[0], out int id) || !TryInt(fields[1], out int x) || !TryInt(fields[2], out int y) || !TryInt(fields[3], out int width) || !TryInt(fields[4], out int height)) continue;
                    if (width < 1 || height < 1) continue;
                    result.SubAreas[id] = new SubAreaInfo { X = x, Y = y, Width = width, Height = height };
                }
            }
            catch (Exception error) when (error is ArgumentException || error is IOException || error is UnauthorizedAccessException || error is NotSupportedException)
            {
                // Racine ou chemin invalide : la table reste vide (fond uni), la vue n'attend pas une table qui ne viendra pas.
            }
            return result;
        }

        /// <summary>Lignes d'un TSV à cinq colonnes (en-tête ignoré) ; fichier absent ou illisible = aucune ligne.</summary>
        private static IEnumerable<string[]> ReadTsv(string path)
        {
            string[] lines;
            try { lines = File.Exists(path) ? File.ReadAllLines(path) : new string[0]; }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException || error is NotSupportedException) { lines = new string[0]; }
            bool header = true;
            foreach (string line in lines)
            {
                if (header) { header = false; if (line.StartsWith("nom", StringComparison.Ordinal)) continue; }
                string[] fields = line.Split('\t');
                if (fields.Length >= 5 && fields[0].Length > 0) yield return fields;
            }
        }

        private static bool TryInt(string text, out int value) => int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);

        // ----- Souris et clavier -----

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (e.Delta == 0) return;
            Zoom = zoom + (e.Delta > 0 ? ZoomStep : -ZoomStep);
            HoverAt(e.Location);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button != MouseButtons.Left) return;
            if (mode == WorldMapMode.Move && ArrowAt(e.Location) < 0) { dragCell = MapAt(e.Location); Cursor = Cursors.SizeAll; }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragCell != null && e.Button == MouseButtons.Left)
            {
                Point now = MapAt(e.Location);
                if (now != dragCell.Value) MoveMap(dragCell.Value.X - now.X, dragCell.Value.Y - now.Y);
                return;
            }
            HoverAt(e.Location);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            bool wasDragging = dragCell != null;
            dragCell = null; Cursor = Cursors.Default;
            if (!wasDragging) ClickAt(e.Location);
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (dragCell == null) ClearHover(); }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode) { case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down: return true; }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.KeyCode)
            {
                case Keys.Left: MoveMap(-1, 0); e.Handled = true; break;
                case Keys.Right: MoveMap(1, 0); e.Handled = true; break;
                case Keys.Up: MoveMap(0, -1); e.Handled = true; break;
                case Keys.Down: MoveMap(0, 1); e.Handled = true; break;
                // + et - du pavé numérique sont les raccourcis MAXI / MINI du chat, exécutés avant par le KeyPreview de la fenêtre.
                case Keys.Oemplus: Zoom = zoom + ZoomStep; e.Handled = true; break;
                case Keys.OemMinus: Zoom = zoom - ZoomStep; e.Handled = true; break;
            }
        }

        private void ReleaseOverlay()
        {
            subAreaOverlay?.Dispose(); subAreaOverlay = null; subAreaOverlayId = -1;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                disposed = true;
                Interlocked.Increment(ref loadGeneration);
                ReleaseOverlay();
                // Les tuiles et icônes viennent du cache partagé de ClientAssets : jamais libérées ici.
                lock (sync) { images.Clear(); pending.Clear(); }
            }
            base.Dispose(disposing);
        }
    }

    public sealed class WorldMapSelectEventArgs : EventArgs
    {
        public WorldMapSelectEventArgs(Point coordinates) { Coordinates = coordinates; }
        public Point Coordinates { get; }
    }
}
