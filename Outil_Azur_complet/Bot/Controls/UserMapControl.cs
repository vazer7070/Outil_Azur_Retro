using Outil_Azur_complet.Bot.Controls.tooltip;
using Syncfusion.Windows.Forms;
using Syncfusion.WinForms.Controls;
using Syncfusion.WinForms.Controls.Events;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Forms;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Interactives;
using Tool_BotProtocol.Game.Monstres;
using Tool_BotProtocol.Game.NPC;
using Tool_BotProtocol.Game.Perso;
using Tool_BotProtocol.Utils.Pics;
using System.Diagnostics;
using System.IO;
using Tool_BotProtocol.Game.Maps.Interfaces;
using Tool_Editor.maps.data;
using Map = Tool_BotProtocol.Game.Maps.Map;

namespace Outil_Azur_complet.Bot.Controls
{
    [Serializable]
    public partial class UserMapControl : UserControl
    {
        public int H { get; set; }
        public int W { get; set; }
        public int SizeCell = 26;
        private bool MouseIn;
        private UserMapCell CellH;
        private UserMapCell cell2;
        private UserMapCell CellBottom;
        private Accounts Account;
        public MapQuality MQ;
        private ConcurrentDictionary<int, Animations> Anim;
        private System.Windows.Forms.Timer AnimTimer;
        private readonly Func<double> animationClock;
        private bool ShowAnim;
        private bool ShowCell;
        private Bitmap TriggerPic = Properties.Resources._21000;
        SfToolTip sf = new SfToolTip();
        private readonly ToolTip hoverTip = new ToolTip();
        private BotMapArtwork artwork;
        private string renderedMapData;
        private int renderedMapId = -1;
        private int renderedWidth, renderedHeight, renderedBackground;
        private string artworkError;
        private PointF[][] worldPolygons;
        private RectangleF worldBounds;
        private float fitScale = 1, viewScale = 1;
        private double zoom = 1;
        private PointF pan, origin;
        private Point dragStart;
        private PointF dragPan;
        private bool panning, movedDuringClick;
        private bool showGrid;
        private readonly Dictionary<string, Bitmap> spriteImages = new Dictionary<string, Bitmap>();
        private readonly Dictionary<string, Point> spriteAnchors = new Dictionary<string, Point>();
        private readonly HashSet<string> missingSprites = new HashSet<string>();
        private readonly Dictionary<string, string> spriteReasons = new Dictionary<string, string>();
        private HashSet<short> spellTargets;
        public Func<short, string> SpellTargetReason { get; set; }
        public void SetSpellTargets(IEnumerable<short> validCells)
        {
            spellTargets = validCells == null ? null : new HashSet<short>(validCells);
            Cursor = spellTargets == null ? Cursors.Default : Cursors.Cross;
            Invalidate();
        }
        public event Action DisplayStateChanged;
        public int ZoomPercent => (int)Math.Round(zoom * 100);
        public int MissingAssetCount => (artwork?.MissingAssetCount ?? 0) + missingSprites.Count;
        public string ArtworkStatus => (artwork?.Status ?? artworkError ?? Account?.Game?.Map?.LoadError ?? "En attente de la carte")
            + (missingSprites.Count == 0 ? "" : " · " + missingSprites.Count + " sprite(s) absent(s), repères affichés : "
                + string.Join(" · ", spriteReasons.Where(pair => spriteImages.TryGetValue(pair.Key, out Bitmap image) && image == null)
                    .Select(pair => pair.Value).Where(reason => !string.IsNullOrEmpty(reason)).Distinct().Take(2)));
        public bool ShowGrid { get => showGrid; set { showGrid = value; Invalidate(); DisplayStateChanged?.Invoke(); } }

        private readonly int[] DoorGFX = { 6750, 6749, 6744, 6745, 6746, 6747, 6748, 6751, 6752, 6753, 6754, 6755, 6756, 6757, 6758, 6759, 6760, 6762, 6763, 6764, 6765, 6766, 6767, 6768, 6772, 6773, 6774, 6775, 6776 };
        private readonly int[] StatueGFX = { 1854, 708, 922, 1351, 1470, 1570, 1591, 1592, 1583, 1597, 1598, 1845, 1853, 1854, 1855, 1856, 1857, 1858, 1859, 1860, 1861, 1862, 2054 };
        private readonly int[] MiscGFX = { 7352, 260, 261, 262, 263, 264, 265, 266, 267, 268, 938, 939, 940, 941, 942, 943, 944, 945, 946, 2520, 2521, 2522, 2523, 2524, 2525, 2526, 2527, 2528, 2529, 2530, 2531, 2532, 2533, 2534, 2535, 2536, 2537, 2538, 2538, 2539, 2540, 2541, 2542, 7519, 7041, 7042, 7043, 7044, 7045, 7046, 7001, 7002, 7003, 7004, 7005, 7006, 7007, 7008, 7009, 7010, 7011, 7012, 7013, 7014, 7015, 7016, 7017, 7019, 7020, 7021, 7022, 7023, 7024, 7025, 7027, 7028, 7032, 7033, 7034, 7035, 7036, 7037, 7038, 7039, 7350, 7351, 7353 };
        private readonly int[] TreeGFX = { 7500, 215, 211, 217, 219, 211, 212, 947, 948, 949, 950, 951, 1657, 1658, 1666, 1667, 1668, 1669, 2726, 2727, 2728, 2729, 2932, 2733, 7542, 7557, 7541, 7509 };

        private readonly int[] RecolteGFX = { 7511, 7512, 7513, 7514, 7515, 7516, 7517, 7518 };


        [Browsable(false)]
        public int RealCellHeight { get; private set; }
        [Browsable(false)]
        public int RealcellWidth { get; private set; }
        public Color CellInactive { get; set; }
        public Color CellActive { get; set; }
        public bool TraceOnOver { get; set; }

        [Browsable(false)]
        public UserMapCell CurrentCellHover { get; set; }
        public Color BorderColorOver { get; set; }

        [Browsable(false)]
        public UserMapCell[] Cells { get; set; }
        public void SetAccount(Accounts A) => Account = A;
        public bool ShowAnimations
        {
            get => ShowAnim;
            set
            {
                ShowAnim = value;
                if (ShowAnim)
                    AnimTimer?.Start();
            }
        }
        public bool ShowCellId
        {
            get => ShowCell;
            set
            {
                ShowCell = value;
                Invalidate();
                DisplayStateChanged?.Invoke();
            }
        }
        public MapQuality MapQ
        {
            get => MQ;
            set
            {
                MQ = value;
                Invalidate();
            }
        }
        public delegate void CellClickedHandler(UserMapCell cell, MouseButtons Buttons, bool Goodies);
        public event CellClickedHandler CellClicked;
        public event Action<UserMapCell, UserMapCell> HasClickedOnCell;

        public UserMapControl() : this(null, null) { }
        public UserMapControl(Func<double> movementClock, string actorSpriteDirectory = null)
        {
            Stopwatch elapsed = Stopwatch.StartNew();
            animationClock = movementClock ?? (() => elapsed.Elapsed.TotalMilliseconds);
            spriteDirectory = actorSpriteDirectory ?? Path.Combine(Path.GetDirectoryName(typeof(UserMapControl).Assembly.Location), "ressources", "Bot", "sprites");
            // Configuration du double buffering
            SetStyle(ControlStyles.OptimizedDoubleBuffer | 
                    ControlStyles.AllPaintingInWmPaint | 
                    ControlStyles.UserPaint, true);
            
            // Initialisation des composants de base
            InitializeComponent();
            
            // Initialisation des propriétés
            MQ = MapQuality.HAUT;
            H = 17;
            W = 15;
            TraceOnOver = false;
            CellInactive = Color.DarkGray;
            CellActive = Color.Azure;
            ShowAnim = true;
            BackColor = Color.FromArgb(211, 204, 169);
            TabStop = true;
            
            // Initialisation des collections et timers
            Anim = new ConcurrentDictionary<int, Animations>();
            AnimTimer = new System.Windows.Forms.Timer { Interval = 33 };
            AnimTimer.Tick += OnAnimationTick;
            
            // Initialisation de la grille
            SetCellNum();
            DrawGrille();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (sf != null)
            {
                sf.ToolTipShowing += Sf_ToolTipShowing;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (AnimTimer != null)
                {
                    AnimTimer.Stop();
                    AnimTimer.Dispose();
                }
                
                if (Anim != null)
                {
                    foreach (var animation in Anim.Values)
                    {
                        animation?.Dispose();
                    }
                    Anim.Clear();
                }
                
                if (sf != null)
                {
                    sf.Dispose();
                }
                hoverTip.Dispose();
                artwork?.Dispose();
                foreach (Bitmap sprite in spriteImages.Values) sprite?.Dispose();
                spriteImages.Clear();
            }
            base.Dispose(disposing);
        }

        public void SetCellNum()
        {
            int count = Account != null ? Account.Game?.Map?.MapCells?.Length ?? 0 : H * (2 * W - 1) - (W - 1);
            Cells = new UserMapCell[Math.Max(0, count)];
            for (int id = 0; id < Cells.Length; id++) Cells[id] = new UserMapCell((short)id);
            LoadArtwork();
        }

        public void DrawGrille()
        {
            if (Cells == null || Cells.Length == 0 || W < 2 || Width < 1 || Height < 1) return;
            int period = 2 * W - 1;
            int last = Cells.Length - 1;
            int rows = 2 * (last / period) + (last % period >= W ? 1 : 0) + 1;
            worldPolygons = new PointF[Cells.Length][];
            worldBounds = artwork != null && artwork.Cells.Length == Cells.Length
                ? artwork.WorldBounds : new RectangleF(0, 0, W * BotMapArtwork.CellWidth, (rows + 1) * BotMapArtwork.CellHeight / 2);
            foreach (UserMapCell cell in Cells)
            {
                int within = cell.id % period;
                int row = 2 * (cell.id / period) + (within >= W ? 1 : 0);
                int column = within >= W ? within - W : within;
                var center = new PointF((column + (row % 2 == 0 ? .5f : 1f)) * BotMapArtwork.CellWidth,
                    (row + 1) * BotMapArtwork.CellHeight / 2);
                worldPolygons[cell.id] = artwork != null && artwork.Cells.Length == Cells.Length
                    ? artwork.Cells[cell.id].Polygon : BotMapArtwork.Diamond(center);
            }
            UpdateViewport();
            Invalidate();
        }

        private void LoadArtwork()
        {
            Map map = Account?.Game?.Map;
            if (map != null && renderedMapId == map.MapID && renderedMapData == map.MapData
                && renderedWidth == map.MapWidth && renderedHeight == map.MapHeight && renderedBackground == map.Back_ID) return;
            artwork?.Dispose(); artwork = null;
            StopAnimations();
            foreach (Bitmap sprite in spriteImages.Values) sprite?.Dispose();
            spriteImages.Clear(); spriteAnchors.Clear(); missingSprites.Clear(); spriteReasons.Clear();
            renderedMapId = map?.MapID ?? -1; renderedMapData = map?.MapData;
            renderedWidth = map?.MapWidth ?? 0; renderedHeight = map?.MapHeight ?? 0; renderedBackground = map?.Back_ID ?? 0;
            artworkError = null;
            if (map?.HasMapData == true)
            {
                try { artwork = new BotMapArtwork(map); }
                catch (Exception error)
                {
                    artworkError = "Décor indisponible : " + error.Message;
                    Account.Logger.LogError("CARTE", artworkError);
                }
            }
            zoom = 1; pan = PointF.Empty; CellH = null;
            DisplayStateChanged?.Invoke();
        }

        private void UpdateViewport()
        {
            if (worldPolygons == null || worldBounds.Width <= 0 || worldBounds.Height <= 0) return;
            fitScale = Math.Max(.01f, Math.Min((Width - 16f) / worldBounds.Width, (Height - 16f) / worldBounds.Height));
            viewScale = fitScale * (float)zoom;
            origin = new PointF((Width - worldBounds.Width * viewScale) / 2 - worldBounds.Left * viewScale + pan.X,
                (Height - worldBounds.Height * viewScale) / 2 - worldBounds.Top * viewScale + pan.Y);
            foreach (UserMapCell cell in Cells)
                cell.Points = worldPolygons[cell.id].Select(point => new Point(
                    (int)Math.Round(point.X * viewScale + origin.X), (int)Math.Round(point.Y * viewScale + origin.Y))).ToArray();
            RealCellHeight = Math.Max(1, (int)Math.Round(BotMapArtwork.CellHeight * viewScale));
            RealcellWidth = Math.Max(1, (int)Math.Round(BotMapArtwork.CellWidth * viewScale));
        }

        public void ZoomIn() => ZoomAt(zoom * 1.2, new Point(Width / 2, Height / 2));
        public void ZoomOut() => ZoomAt(zoom / 1.2, new Point(Width / 2, Height / 2));
        public void Fit()
        {
            zoom = 1; pan = PointF.Empty; UpdateViewport(); Invalidate(); DisplayStateChanged?.Invoke();
        }

        public void ZoomAt(double factor, Point anchor)
        {
            if (worldPolygons == null || viewScale <= 0) return;
            factor = Math.Max(.5, Math.Min(6, factor));
            PointF worldAnchor = new PointF((anchor.X - origin.X) / viewScale, (anchor.Y - origin.Y) / viewScale);
            zoom = factor; pan = PointF.Empty; UpdateViewport();
            pan = new PointF(anchor.X - (worldAnchor.X * viewScale + origin.X), anchor.Y - (worldAnchor.Y * viewScale + origin.Y));
            UpdateViewport(); Invalidate(); DisplayStateChanged?.Invoke();
        }

        public void PanBy(Point offset)
        {
            pan = new PointF(pan.X + offset.X, pan.Y + offset.Y);
            UpdateViewport(); Invalidate(); DisplayStateChanged?.Invoke();
        }

        private void StopAnimations()
        {
            if (Anim == null) return;
            foreach (Animations animation in Anim.Values) animation.Dispose();
            Anim.Clear();
        }

        private void OnAnimationTick(object sender, EventArgs e)
        {
            if (Anim.Count > 0)
            {
                Invalidate();
            }
            else if (!ShowAnim)
            {
                AnimTimer.Stop();
            }
        }
        protected void OnCellclicked(UserMapCell cell, MouseButtons buttons, bool G) => CellClicked?.Invoke(cell, buttons, G);
        protected void OnCellOver(UserMapCell cell, UserMapCell last) => HasClickedOnCell?.Invoke(cell, last);

        private void ApplyQuality(Graphics g)
        {
            switch (MQ)
            {
                case MapQuality.BAS:
                    g.CompositingMode = CompositingMode.SourceOver;
                    g.CompositingQuality = CompositingQuality.HighSpeed;
                    g.InterpolationMode = InterpolationMode.Low;
                    g.SmoothingMode = SmoothingMode.HighSpeed;
                    break;

                case MapQuality.MOYEN:
                    g.CompositingMode = CompositingMode.SourceOver;
                    g.CompositingQuality = CompositingQuality.GammaCorrected;
                    g.InterpolationMode = InterpolationMode.High;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    break;


                case MapQuality.HAUT:
                    g.CompositingMode = CompositingMode.SourceOver;
                    g.CompositingQuality = CompositingQuality.HighQuality;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    break;
            }
        }

        public virtual void DrawUniqueCell(Graphics G, UserMapCell cell)
        {
            if (cell?.Points == null) return;
            if (cell.IsRectangle(G.ClipBounds))
            {
                switch (cell.State)
                {
                    case CellState.WALKABLE:
                        cell.DrawColor(G, Color.Gray, Color.White);
                        if (ShowCellId)
                            cell.DrawCell_ID(this, G);
                        break;
                    case CellState.OBSTACLE:
                        if (ShowCellId)
                            cell.DrawCell_ID(this, G);
                        else
                            cell.DrawObstacle(G, Color.Gray, Color.FromArgb(60, 60, 60));
                        break;
                    case CellState.TRIGGER:

                        if (ShowCellId)
                        {
                            cell.DrawColor(G, Color.Gray, Color.Yellow);
                            cell.DrawCell_ID(this, G);
                        }
                        else
                        {
                            cell.DrawTrigger(TriggerPic, G);
                        }
                        break;
                    case CellState.INTERACTIVE:
                        if (!ShowCellId)
                        {
                            int map = Account.Game.Map.MapID;
                            int gfx = Account.Game.Map.GetCellFromId(cell.id)?.Interactives?.gfx ?? -1;
                            bool isZaap = Zaaps.Z.TryGetValue(map, out int zaapCell) && cell.id == zaapCell;
                            bool reverse = isZaap && (map == 7411 || map == 8785 || map == 5295 || map == 11210);
                            using (Bitmap artwork = PicturesManager.InteractivePicGfx(gfx, reverse))
                            {
                                if (artwork == null)
                                {
                                    cell.DrawColor(G, Color.LightGoldenrodYellow, Color.LightGoldenrodYellow);
                                    cell.DrawCell_ID(this, G);
                                }
                                else if (isZaap) cell.DrawZaap(artwork, G, reverse);
                                else if (DoorGFX.Contains(gfx)) cell.DrawDoor(artwork, G);
                                else if (StatueGFX.Contains(gfx)) cell.DrawStatue(artwork, G);
                                else if (MiscGFX.Contains(gfx)) cell.DrawMisc(artwork, G);
                                else if (TreeGFX.Contains(gfx)) cell.DrawTree(artwork, G);
                                else cell.DrawZaapi(artwork, G);
                            }

                        }
                        else
                        {
                            cell.DrawColor(G, Color.LightGoldenrodYellow, Color.LightGoldenrodYellow);
                            cell.DrawCell_ID(this, G);
                        }
                        break;
                    default:
                        cell.DrawColor(G, Color.Gray, Color.DarkGray);
                        break;
                }
                if (Account != null)
                {
                    if (Account.Game.character.Cell != null && cell.id == Account.Game.character.Cell.CellID && !Anim.ContainsKey(Account.Game.character.id))
                        cell.Draw_FillingPie(G, Color.Blue, RealCellHeight / 2);
                    else if (Account.Game.Map.Entites.Values.Where(x => x is Monstres).FirstOrDefault(x => x.Cell?.CellID == cell.id && !Anim.ContainsKey(x.id)) != null)
                        cell.Draw_FillingPie(G, Color.DarkRed, RealCellHeight / 2);
                    else if (Account.Game.Map.Entites.Values.Where(x => x is PNJ).FirstOrDefault(x => x.Cell?.CellID == cell.id && !Anim.ContainsKey(x.id)) != null)
                    {

                        PNJ P = PNJ.ReturnNpc(Account.Game.Map.NPC_List().FirstOrDefault(x => x.Cell?.CellID == cell.id).NPc_ID, true);
                        if (P != null)
                        {
                            using (Bitmap bmp = PicturesManager.InteractivePicSprite(P.GFX, P.Orientation))
                            {
                                if (bmp != null) cell.DrawNPC(bmp, G);
                                else cell.Draw_FillingPie(G, Color.FromArgb(179, 120, 211), RealCellHeight / 2);
                            }
                        }
                        else
                            cell.Draw_FillingPie(G, Color.FromArgb(78, 119, 185), RealCellHeight / 2);


                    }
                    else if (Account.Game.Map.Entites.Values.Where(x => x is Personnages).FirstOrDefault(x => x.Cell?.CellID == cell.id && !Anim.ContainsKey(x.id)) != null)
                        cell.Draw_FillingPie(G, Color.FromArgb(81, 113, 202), RealCellHeight / 2);

                }
            }
        }
        public static Bitmap ReturnMonsterStar(int star)
        {

            switch (star)
            {
                case 0:
                    return Properties.Resources.re11_1;
                case 15:
                    return Properties.Resources.re1_1;
                case 30:
                    return Properties.Resources.re2_1;
                case 45:
                    return Properties.Resources.re3_1;
                case 60:
                    return Properties.Resources.re4_1;
                case 75:
                    return Properties.Resources.re5_1;
                case 90:
                    return Properties.Resources.re6_1;
                case 105:
                    return Properties.Resources.re7_1;
                case 120:
                    return Properties.Resources.re8_1;
                case 135:
                    return Properties.Resources.re9_1;
                case 150:
                    return Properties.Resources.re10_1;
                default:
                    return Properties.Resources.re11_1;
            };
        }
        public void DrawCells(Graphics G)
        {
            ApplyQuality(G);
            G.Clear(BackColor);
            Map map = Account?.Game?.Map;
            if (map == null || !map.HasMapData || Cells == null || Cells.Length == 0)
            {
                StopAnimations();
                DrawWaiting(G, map?.LoadError);
                return;
            }
            GraphicsState saved = G.Save();
            using (var transform = new Matrix(viewScale, 0, 0, viewScale, origin.X, origin.Y)) G.Transform = transform;
            if (artwork != null) artwork.DrawGround(G);
            // A missing PNG remains visibly diagnostic while cells still use the real map geometry.
            foreach (UserMapCell cell in Cells)
            {
                if (worldPolygons == null) break;
                bool noScenery = artwork == null || !artwork.HasGroundArtwork(cell.id);
                if (noScenery)
                {
                    Color fallback = cell.State == CellState.WALKABLE ? Color.FromArgb(90, 184, 183, 137)
                        : cell.State == CellState.INTERACTIVE ? Color.FromArgb(120, 180, 151, 89)
                        : Color.FromArgb(55, 83, 80, 58);
                    using (var brush = new SolidBrush(fallback)) G.FillPolygon(brush, worldPolygons[cell.id]);
                }
                if (ShowGrid || noScenery)
                    using (var pen = new Pen(Color.FromArgb(70, 67, 62, 40), 1 / viewScale)) G.DrawPolygon(pen, worldPolygons[cell.id]);
                if (spellTargets?.Contains(cell.id) == true)
                    using (var target = new SolidBrush(Color.FromArgb(65, 70, 146, 207))) G.FillPolygon(target, worldPolygons[cell.id]);
            }
            ActorVisualState[] actors = GetActorVisualStates();
            if (artwork != null) artwork.DrawDepthScene(G, actors.Where(actor => actor.IsVisible).Select(actor =>
                new BotMapArtwork.DepthLayer { Depth = actor.WorldPosition.Y, Order = 1, Draw = graphics => DrawActor(graphics, actor) }));
            else foreach (ActorVisualState actor in actors.OrderBy(actor => actor.WorldPosition.Y)) DrawActor(G, actor);
            G.Restore(saved);
            if (ShowCellId)
                foreach (UserMapCell cell in Cells)
                {
                    using (var backing = new SolidBrush(Color.FromArgb(160, 249, 239, 202)))
                        G.FillRectangle(backing, cell.Centre.X - 13, cell.Centre.Y - 8, 26, 16);
                    cell.DrawCell_ID(this, G);
                }
            DrawMapLegend(G);
        }

        private void DrawWorldEntities(Graphics graphics, int cellId, PointF center, Entites[] entities)
        {
            CharacterClass character = Account.Game.character;
            if (character.Cell?.CellID == cellId && !Anim.ContainsKey(character.id))
                DrawEntity(graphics, center, character.Name ?? "Vous", character.Race_ID * 10 + character.Sex, 2, Color.FromArgb(72, 103, 156), true);
            foreach (Entites entity in entities.Where(entry => entry.Cell.CellID == cellId && !Anim.ContainsKey(entry.id)))
            {
                if (entity is PNJ npc)
                    DrawEntity(graphics, center, npc.Name, npc.GFX, npc.Orientation, Color.FromArgb(154, 106, 172), false);
                else if (entity is Monstres monster)
                    DrawEntity(graphics, center, monster.Name + " · " + monster.GetAllMonster, monster.GFX != 0 ? monster.GFX
                        : Monstres.ReturnMonsters(monster.TemplateID)?.GFX ?? 0, 2, Color.FromArgb(156, 63, 48), false);
                else DrawEntity(graphics, center, entity.Name, 0, 2, Color.FromArgb(85, 103, 143), false);
            }
        }

        private void DrawEntity(Graphics graphics, PointF center, string name, int gfx, int direction, Color color, bool self)
        {
            string key = gfx + "/" + direction;
            if (!spriteImages.TryGetValue(key, out Bitmap image))
            {
                image = gfx > 0 ? LoadSprite(gfx, direction) : null;
                spriteImages[key] = image;
                if (image != null)
                    spriteAnchors[key] = TilesData.Anchor(new TilesData(gfx, "", "", TilesData.TileType.objet), image);
                else if (gfx > 0 && missingSprites.Add(key)) DisplayStateChanged?.Invoke();
            }
            using (var shadow = new SolidBrush(Color.FromArgb(70, 35, 32, 23))) graphics.FillEllipse(shadow, center.X - 12, center.Y - 4, 24, 8);
            if (image != null)
            {
                Point anchor = spriteAnchors[key];
                graphics.DrawImageUnscaled(image, (int)Math.Round(center.X - anchor.X), (int)Math.Round(center.Y - anchor.Y));
            }
            else
            {
                using (var body = new SolidBrush(color))
                {
                    graphics.FillEllipse(body, center.X - 6, center.Y - 28, 12, 12);
                    graphics.FillPolygon(body, new[] { new PointF(center.X, center.Y - 18), new PointF(center.X - 9, center.Y - 3), new PointF(center.X + 9, center.Y - 3) });
                }
            }
            if (self)
                using (var pen = new Pen(Color.FromArgb(216, 190, 76), 2)) graphics.DrawEllipse(pen, center.X - 14, center.Y - 6, 28, 12);
        }

        private static Bitmap LoadSprite(int gfx, int direction)
        {
            string suffix = direction == 2 ? "F" : direction == 3 ? "L" : "R";
            string path = Path.Combine(Path.GetDirectoryName(typeof(UserMapControl).Assembly.Location),
                "ressources", "Bot", "sprites", gfx + suffix + ".png");
            try
            {
                if (!File.Exists(path)) return null;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var source = Image.FromStream(stream)) return new Bitmap(source);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException
                || error is System.Runtime.InteropServices.ExternalException || error is OutOfMemoryException)
            { return null; }
        }

        private void DrawWaiting(Graphics graphics, string error)
        {
            using (var font = new Font("Segoe UI", 13, FontStyle.Bold))
            using (var brush = new SolidBrush(Color.FromArgb(92, 88, 65)))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                graphics.DrawString(error ?? "La carte apparaîtra après la sélection du personnage.", font, brush,
                    new RectangleF(28, Height / 2f - 45, Math.Max(1, Width - 56), 65), format);
        }

        private void DrawMapLegend(Graphics graphics)
        {
            string text = "Molette : zoom · Glisser avec le bouton du milieu : déplacer la vue";
            if (MissingAssetCount > 0 || artwork?.LoadedAssetCount == 0 || artworkError != null)
                text = ArtworkStatus + "\n" + text;
            Size size = TextRenderer.MeasureText(text, Font, new Size(Math.Max(1, Width - 20), 0), TextFormatFlags.WordBreak);
            var rectangle = new Rectangle(8, Math.Max(0, Height - size.Height - 15), Math.Min(Width - 16, size.Width + 14), size.Height + 7);
            using (var brush = new SolidBrush(Color.FromArgb(210, 233, 223, 181))) graphics.FillRectangle(brush, rectangle);
            TextRenderer.DrawText(graphics, text, Font, new Rectangle(rectangle.X + 6, rectangle.Y + 3, rectangle.Width - 12, rectangle.Height - 6),
                Color.FromArgb(78, 72, 47), TextFormatFlags.WordBreak);
        }
        public void AddAnimations(int id, List<Cell> path, int d, AnimationType T)
        {
            if (path == null || path.Count < 2 || !ShowAnimations || path.Any(c => c.CellID < 0 || c.CellID >= Cells.Length))
                return;
            short[] ids = path.Select(cell => cell.CellID).ToArray();
            if (Anim.TryGetValue(id, out Animations previous) && previous.Matches(ids)) return;
            CancelAnimation(id);
            var points = ids.Select(cellId => WorldCenter(cellId)).ToArray();
            int[] directions = Enumerable.Range(1, path.Count - 1).Select(index => path[index].GetCharDirection(path[index - 1]) - 'a').ToArray();
            Anim[id] = new Animations(id, ids, points, directions, d, T, animationClock());
            SetActorOrientation(id, directions.Last());
            AnimTimer.Start(); Invalidate();
        }
        public void CancelAnimation(int id)
        {
            if (Anim.TryRemove(id, out Animations previous)) previous.Dispose();
            Invalidate();
        }
        private Color AnimColor(Animations A)
        {
            switch (A.AnimationType)
            {
                case AnimationType.PERSONNAGE:
                    return Color.Blue;
                case AnimationType.GROUPE_MONSTRES:
                    return Color.DarkRed;
                default:
                    return Color.FromArgb(81, 113, 202);
            }
        }
        public void RefreshMap()
        {

            if (Account?.Game?.Map == null)
                return;
            StopAnimations();
            AnimTimer.Stop();


            Cell[] MapCells = Account.Game.Map.MapCells;
            if (MapCells == null)
                return;

            foreach (Cell cell in MapCells)
            {
                if (cell != null)
                {

                    if (cell.CellID < 0 || cell.CellID >= Cells.Length) continue;
                    Cells[cell.CellID].State = CellState.NO_WALKABLE;

                    if (cell.IsWalkable())
                        Cells[cell.CellID].State = CellState.WALKABLE;
                    if (!cell.LineofSight && !cell.IsWalkable())
                        Cells[cell.CellID].State = CellState.OBSTACLE;
                    if (cell.IsTrigger())
                        Cells[cell.CellID].State = CellState.TRIGGER;
                    if (cell.IsInteractiveCell())
                    {
                        Cells[cell.CellID].State = CellState.INTERACTIVE;
                    }
                }

            }
            AnimTimer.Start();
            Invalidate();
        }
        public UserMapCell GetCell(Point point)
        {
            if (!ClientRectangle.Contains(point)) return null;
            return Cells?.Reverse().FirstOrDefault(cell => cell.Points != null && pointPoly(cell.Points, point));
        }

        public bool pointPoly(Point[] polygon, Point point)
        {
            if (polygon == null || polygon.Length < 3) return false;
            int sign = 0;
            for (int i = 0; i < polygon.Length; i++)
            {
                Point start = polygon[i], end = polygon[(i + 1) % polygon.Length];
                long cross = (long)(end.X - start.X) * (point.Y - start.Y)
                    - (long)(end.Y - start.Y) * (point.X - start.X);
                if (cross == 0) continue;
                int current = cross > 0 ? 1 : -1;
                if (sign != 0 && sign != current) return false;
                sign = current;
            }
            return sign != 0;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            DrawCells(e.Graphics);
            if (CellH?.Points != null) CellH.Border(e.Graphics, spellTargets == null ? Brushes.RoyalBlue
                : spellTargets.Contains(CellH.id) ? Brushes.ForestGreen : Brushes.IndianRed);
            base.OnPaint(e);
        }
        protected override void OnResize(EventArgs e)
        {
            DrawGrille();
            base.OnResize(e);
        }
        private void Sf_ToolTipShowing(object sender, ToolTipShowingEventArgs e)
        {
            if (CellH != null) e.Location = CellH.Centre;

        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (panning)
            {
                pan = new PointF(dragPan.X + e.X - dragStart.X, dragPan.Y + e.Y - dragStart.Y);
                if (Math.Abs(e.X - dragStart.X) + Math.Abs(e.Y - dragStart.Y) > 3) movedDuringClick = true;
                UpdateViewport(); Invalidate(); base.OnMouseMove(e); return;
            }
            if (MouseIn && Math.Abs(e.X - dragStart.X) + Math.Abs(e.Y - dragStart.Y) > 3) movedDuringClick = true;
            UserMapCell hover = GetCell(e.Location);
            if (hover != CellH)
            {
                CellH = hover;
                hoverTip.Hide(this);
                if (hover != null && Account?.Game != null)
                {
                    Cell cell = Account.Game.Map.GetCellFromId(hover.id);
                    var actors = GetActorVisualStates().Where(actor => actor.CellId == hover.id).ToArray();
                    var names = actors.Select(actor => actor.Name).Where(name => !string.IsNullOrEmpty(name)).Distinct().ToList();
                    names.AddRange(actors.Select(actor => actor.SpriteReason).Where(reason => !string.IsNullOrEmpty(reason)).Distinct());
                    string status = cell == null ? "Non chargée" : cell.IsWalkable() ? "Accessible" : "Obstacle";
                    if (cell?.IsInteractiveCell() == true) status += " · " + (cell.Interactives.Interactive?.Name ?? "Objet interactif");
                    string text = "Cellule " + hover.id + " · " + status;
                    if (spellTargets != null)
                    {
                        string reason = SpellTargetReason?.Invoke(hover.id);
                        text += "\n" + (string.IsNullOrEmpty(reason) ? "Cible valide pour le sort sélectionné." : reason);
                    }
                    if (names.Count > 0) text += "\n" + string.Join("\n", names);
                    hoverTip.Show(text, this, hover.Centre.X + 10, hover.Centre.Y + 10, 2500);
                }
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            CellH = null;
            hoverTip.Hide(this);
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            dragStart = e.Location; dragPan = pan; movedDuringClick = false;
            panning = e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && (ModifierKeys & Keys.Control) != 0);
            if (panning) { Capture = true; Cursor = Cursors.SizeAll; hoverTip.Hide(this); }
            CellBottom = GetCell(e.Location);
            MouseIn = true;
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            UserMapCell clicked = GetCell(e.Location);
            if (MouseIn && clicked != null && !panning && !movedDuringClick) OnCellclicked(clicked, e.Button, clicked != CellBottom);
            if (panning) { Capture = false; Cursor = spellTargets == null ? Cursors.Default : Cursors.Cross; DisplayStateChanged?.Invoke(); }
            panning = false;
            MouseIn = false;
            CellBottom = null;
            base.OnMouseUp(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ZoomAt(zoom * Math.Pow(1.2, e.Delta / 120.0), e.Location);
            base.OnMouseWheel(e);
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == Keys.Home) { Fit(); return true; }
            if (keyData == Keys.Add || keyData == Keys.Oemplus) { ZoomIn(); return true; }
            if (keyData == Keys.Subtract || keyData == Keys.OemMinus) { ZoomOut(); return true; }
            return base.ProcessCmdKey(ref message, keyData);
        }

        private void UserMapControl_Paint(object sender, PaintEventArgs e)
        {

        }
    }
}
