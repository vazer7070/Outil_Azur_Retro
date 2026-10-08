using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Mouvements;
using Map = Tool_BotProtocol.Game.Maps.Map;

namespace Outil_Azur_complet.Bot.Controls
{
    /// <summary>
    /// Vue de la carte du bot : décor (<see cref="BotMapArtwork"/>), cellules, acteurs animés (partie
    /// <c>UserMapControl.Actors.cs</c>), zoom et déplacement de la vue. Un clic sur le sprite d'un acteur lève
    /// <see cref="ActorClicked"/>, ailleurs <see cref="CellClicked"/> ; le survol d'un acteur affiche sa surtête.
    /// </summary>
    [Serializable]
    public partial class UserMapControl : UserControl
    {
        public int H { get; set; }
        public int W { get; set; }
        private bool MouseIn;
        private UserMapCell CellH;
        private UserMapCell CellBottom;
        private Accounts Account;
        public MapQuality MQ;
        private ConcurrentDictionary<int, Animations> Anim;
        private System.Windows.Forms.Timer AnimTimer;
        private readonly Func<double> animationClock;
        private bool ShowAnim;
        private bool ShowCell;
        private readonly ToolTip hoverTip = new ToolTip();
        private string hoverTipText;
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
        private HashSet<short> spellTargets;
        public Func<short, string> SpellTargetReason { get; set; }
        public void SetSpellTargets(IEnumerable<short> validCells)
        {
            spellTargets = validCells == null ? null : new HashSet<short>(validCells);
            Cursor = BaseCursor;
            Invalidate();
        }
        private Cursor BaseCursor => spellTargets == null ? Cursors.Default : Cursors.Cross;
        public event Action DisplayStateChanged;
        public int ZoomPercent => (int)Math.Round(zoom * 100);
        public int MissingAssetCount => (artwork?.MissingAssetCount ?? 0) + missingSprites.Count;
        public string ArtworkStatus => (artwork?.Status ?? artworkError ?? Account?.Game?.Map?.LoadError ?? "En attente de la carte")
            + (missingSprites.Count == 0 ? "" : " · " + missingSprites.Count + " sprite(s) absent(s), repères affichés : "
                + string.Join(" · ", missingSprites.Values.Where(reason => !string.IsNullOrEmpty(reason)).Distinct().Take(2)));
        public bool ShowGrid { get => showGrid; set { showGrid = value; Invalidate(); DisplayStateChanged?.Invoke(); } }

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
                if (!ShowAnim) StopAnimations();
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
        /// <summary>La souris passe sur une autre cellule (null : elle quitte la carte). Sert à l'aperçu du chemin.</summary>
        public event Action<UserMapCell> CellHovered;

        public UserMapControl() : this(null, null) { }
        /// <param name="movementClock">Horloge (ms) des déplacements et des bulles ; injectée par les tests.</param>
        /// <param name="actorSpriteDirectory">Dossier des sprites (par défaut <c>ressources/Bot/sprites</c>).</param>
        /// <param name="overheadDirectory">Dossier des smileys, émotes et étoiles (par défaut <c>ressources/Bot</c>).</param>
        public UserMapControl(Func<double> movementClock, string actorSpriteDirectory = null, string overheadDirectory = null)
        {
            Stopwatch elapsed = Stopwatch.StartNew();
            animationClock = movementClock ?? (() => elapsed.Elapsed.TotalMilliseconds);
            sprites = new ActorSprites(actorSpriteDirectory);
            sprites.SheetsLoaded += AssetsArrived;
            overheadImages = new OverheadImages(overheadDirectory);
            overheadImages.Loaded += AssetsArrived;
            bubbles = new BubbleLayer(animationClock);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            InitializeComponent();
            MQ = MapQuality.HAUT;
            H = 17;
            W = 15;
            TraceOnOver = false;
            CellInactive = Color.DarkGray;
            CellActive = Color.Azure;
            ShowAnim = true;
            BackColor = Color.FromArgb(211, 204, 169);
            TabStop = true;
            Anim = new ConcurrentDictionary<int, Animations>();
            AnimTimer = new System.Windows.Forms.Timer { Interval = 33 };
            AnimTimer.Tick += OnAnimationTick;
            SetCellNum();
            DrawGrille();
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
                StopAnimations();
                hoverTip.Dispose();
                artwork?.Dispose();
                // Après le dernier Paint, sur le fil de l'interface : les images ne sont plus dessinées.
                sprites.SheetsLoaded -= AssetsArrived;
                sprites.Dispose();
                overheadImages.Loaded -= AssetsArrived;
                overheadImages.Dispose();
                bubbles.Clear();
                components?.Dispose();
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
            // Nouvelle carte : les sprites retournent à la bibliothèque partagée, bulles et survol sont oubliés.
            sprites.ReleaseAll();
            missingSprites.Clear(); bubbles.Clear(); staleEntryEmotes.Clear();
            hoveredKey = null; pathPreview = null;
            renderedMapId = map?.MapID ?? -1; renderedMapData = map?.MapData;
            renderedWidth = map?.MapWidth ?? 0; renderedHeight = map?.MapHeight ?? 0; renderedBackground = map?.Back_ID ?? 0;
            artworkError = null;
            if (map?.HasMapData == true)
            {
                try
                {
                    // Les PNG sont lus par une tâche de fond : la vue se recadre et se redessine quand ils arrivent.
                    artwork = new BotMapArtwork(map);
                    artwork.AssetsLoaded += ArtworkAssetsLoaded;
                    if (artwork.AssetsReady) ArtworkAssetsLoaded(artwork, EventArgs.Empty);
                }
                catch (Exception error)
                {
                    artworkError = "Décor indisponible : " + error.Message;
                    Account.Logger.LogError("CARTE", artworkError);
                }
            }
            zoom = 1; pan = PointF.Empty; CellH = null;
            DisplayStateChanged?.Invoke();
        }

        private void ArtworkAssetsLoaded(object sender, EventArgs e)
        {
            if (IsDisposed || !ReferenceEquals(sender, artwork)) return;
            if (InvokeRequired)
            {
                try { if (IsHandleCreated) BeginInvoke(new Action(() => ArtworkAssetsLoaded(sender, e))); }
                catch (InvalidOperationException) { } // poignée détruite entre-temps : plus rien à redessiner
                return;
            }
            DrawGrille();
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

        private void StartTimer()
        {
            if (AnimTimer != null && !AnimTimer.Enabled && !IsDisposed) AnimTimer.Start();
        }

        /// <summary>Minuterie de 33 ms : redessine pendant les déplacements, retire les bulles expirées, s'arrête sinon.</summary>
        private void OnAnimationTick(object sender, EventArgs e)
        {
            bool expired = bubbles.Prune();
            if (Anim.Count > 0 || expired) Invalidate();
            if (Anim.Count == 0 && !bubbles.HasTimedItems) AnimTimer.Stop();
        }

        protected void OnCellclicked(UserMapCell cell, MouseButtons buttons, bool G) => CellClicked?.Invoke(cell, buttons, G);

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

        /// <summary>Dessine toute la vue (décor, cellules, acteurs, surtêtes, bulles, légende) ; utilisé par Paint et les tests.</summary>
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
            // Placement d'un combat : cellules de départ de l'équipe 0 en rouge, de l'équipe 1 en bleu, comme le client.
            var fight = Account?.Game?.Fight;
            HashSet<short> redStarts = null, blueStarts = null;
            if (fight != null && fight.IsPlacement)
            {
                redStarts = new HashSet<short>(fight.TeamPlacementCells(0)); blueStarts = new HashSet<short>(fight.TeamPlacementCells(1));
            }
            // Un PNG absent reste visible comme diagnostic, les cellules gardant la géométrie réelle de la carte.
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
                if (redStarts != null && (redStarts.Contains(cell.id) || blueStarts.Contains(cell.id)))
                    using (var start = new SolidBrush(redStarts.Contains(cell.id) ? Color.FromArgb(110, 255, 0, 0) : Color.FromArgb(110, 0, 0, 255)))
                        G.FillPolygon(start, worldPolygons[cell.id]);
                if (spellTargets?.Contains(cell.id) == true)
                    using (var target = new SolidBrush(Color.FromArgb(65, 70, 146, 207))) G.FillPolygon(target, worldPolygons[cell.id]);
            }
            DrawPathPreview(G);
            ActorVisualState[] actors = GetActorVisualStates();
            // Profondeur du client : objets à cellule × 100, sprites à cellule × 100 + 30 (dessinés après l'objet de leur cellule).
            if (artwork != null) artwork.DrawDepthScene(G, actors.Where(actor => actor.IsVisible).Select(actor =>
                new BotMapArtwork.DepthLayer { Depth = actor.Depth, Order = 3, Draw = graphics => DrawActor(graphics, actor) }));
            else foreach (ActorVisualState actor in actors) DrawActor(G, actor);
            G.Restore(saved);
            if (ShowCellId)
                foreach (UserMapCell cell in Cells)
                {
                    using (var backing = new SolidBrush(Color.FromArgb(160, 249, 239, 202)))
                        G.FillRectangle(backing, cell.Centre.X - 13, cell.Centre.Y - 8, 26, 16);
                    cell.DrawCell_ID(this, G);
                }
            DrawOverheads(G, actors);
            DrawMapLegend(G);
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

        /// <summary>
        /// Déplacement visuel de l'acteur <paramref name="id"/> le long de <paramref name="path"/> en <paramref name="d"/> ms :
        /// la durée de chaque pas et l'allure (marche ou course) suivent le minutage du client pour cet acteur
        /// (<see cref="AnimDuration"/>), ramené à la durée donnée.
        /// </summary>
        public void AddAnimations(int id, List<Cell> path, int d, AnimationType T)
        {
            if (path == null || path.Count < 2) return;
            AddAnimations(id, path, AnimDuration.Compute(path, ProfileOf(id)), d, T);
        }

        /// <summary>Déplacement visuel avec un minutage déjà calculé (hors du fil de l'interface par l'appelant).</summary>
        public void AddAnimations(int id, List<Cell> path, AnimDuration timing, AnimationType T) =>
            AddAnimations(id, path, timing, timing?.Total ?? 0, T);

        private void AddAnimations(int id, List<Cell> path, AnimDuration timing, int total, AnimationType type)
        {
            if (path == null || path.Count < 2 || !ShowAnimations || Cells == null || path.Any(c => c == null || c.CellID < 0 || c.CellID >= Cells.Length))
                return;
            short[] ids = path.Select(cell => cell.CellID).ToArray();
            if (Anim.TryGetValue(id, out Animations previous) && previous.Matches(ids)) return;
            CancelAnimation(id);
            var points = ids.Select(cellId => WorldCenter(cellId)).ToArray();
            var directions = new int[path.Count - 1];
            for (int index = 1; index < path.Count; index++)
            {
                int direction = timing != null && timing.StepCount == directions.Length ? timing.Directions[index - 1] : -1;
                if (direction < 0)
                {
                    try { direction = path[index].GetCharDirection(path[index - 1]) - 'a'; }
                    catch (Exception) { direction = index > 1 ? directions[index - 2] : 1; } // deux fois la même cellule
                }
                directions[index - 1] = ActorOrientation.Normalize(direction);
            }
            double[] steps = Animations.ScaleSteps(timing, directions.Length, total);
            Anim[id] = new Animations(id, ids, points, directions, steps, timing?.Mode ?? MoveMode.Walk, type, animationClock());
            // Le client interrompt l'émote d'un sprite qui se met en marche.
            bubbles.RemoveEmote(id); staleEntryEmotes.Add(id);
            StartTimer(); Invalidate();
        }

        private MoveProfile ProfileOf(int id) => MovementProfile(Account, id);

        public void CancelAnimation(int id)
        {
            if (Anim.TryRemove(id, out Animations previous)) previous.Dispose();
            Invalidate();
        }

        public void RefreshMap()
        {
            if (Account?.Game?.Map == null)
                return;
            StopAnimations();
            Cell[] MapCells = Account.Game.Map.MapCells;
            if (MapCells == null)
                return;
            foreach (Cell cell in MapCells)
            {
                if (cell == null || cell.CellID < 0 || cell.CellID >= Cells.Length) continue;
                Cells[cell.CellID].State = CellState.NO_WALKABLE;
                if (cell.IsWalkable())
                    Cells[cell.CellID].State = CellState.WALKABLE;
                if (!cell.LineofSight && !cell.IsWalkable())
                    Cells[cell.CellID].State = CellState.OBSTACLE;
                if (cell.IsTrigger())
                    Cells[cell.CellID].State = CellState.TRIGGER;
                if (cell.IsInteractiveCell())
                    Cells[cell.CellID].State = CellState.INTERACTIVE;
            }
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

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (panning)
            {
                pan = new PointF(dragPan.X + e.X - dragStart.X, dragPan.Y + e.Y - dragStart.Y);
                if (Math.Abs(e.X - dragStart.X) + Math.Abs(e.Y - dragStart.Y) > 3) movedDuringClick = true;
                UpdateViewport(); Invalidate(); base.OnMouseMove(e); return;
            }
            if (MouseIn && Math.Abs(e.X - dragStart.X) + Math.Abs(e.Y - dragStart.Y) > 3) movedDuringClick = true;
            ActorVisualState actor = HitTest(e.Location);
            UpdateHover(actor);
            Cursor wanted = actor != null && spellTargets == null ? Cursors.Hand : BaseCursor;
            if (Cursor != wanted) Cursor = wanted;
            UserMapCell hover = GetCell(e.Location);
            if (hover != CellH)
            {
                CellH = hover;
                Invalidate();
                CellHovered?.Invoke(hover);
            }
            UpdateCellTip(actor == null ? hover : null);
            base.OnMouseMove(e);
        }

        /// <summary>
        /// Infobulle de cellule réservée à ce que la surtête ne dit pas : raison d'une cible de sort refusée et nom d'un
        /// objet interactif. Les acteurs ont leur surtête, comme dans le client.
        /// </summary>
        private void UpdateCellTip(UserMapCell hover)
        {
            string text = null;
            if (hover != null && Account?.Game?.Map != null)
            {
                if (spellTargets != null)
                {
                    string reason = SpellTargetReason?.Invoke(hover.id);
                    text = "Cellule " + hover.id.ToString(CultureInfo.InvariantCulture) + " · "
                        + (string.IsNullOrEmpty(reason) ? "cible valide pour le sort sélectionné." : reason);
                }
                else
                {
                    Cell cell = Account.Game.Map.GetCellFromId(hover.id);
                    if (cell?.IsInteractiveCell() == true)
                        text = cell.Interactives?.Interactive?.Name ?? "Objet interactif";
                }
            }
            string key = text == null ? null : hover.id.ToString(CultureInfo.InvariantCulture) + "|" + text;
            if (key == hoverTipText) return;
            hoverTipText = key;
            hoverTip.Hide(this);
            if (text != null) hoverTip.Show(text, this, hover.Centre.X + 10, hover.Centre.Y + 10, 2500);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            CellH = null;
            UpdateHover(null);
            UpdateCellTip(null);
            if (!panning) Cursor = BaseCursor;
            SetPathPreview(null);
            CellHovered?.Invoke(null);
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            dragStart = e.Location; dragPan = pan; movedDuringClick = false;
            panning = e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && (ModifierKeys & Keys.Control) != 0);
            if (panning) { Capture = true; Cursor = Cursors.SizeAll; UpdateCellTip(null); }
            CellBottom = GetCell(e.Location);
            MouseIn = true;
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (MouseIn && !panning && !movedDuringClick)
            {
                ActorVisualState actor = ActorClicked == null ? null : HitTest(e.Location);
                if (actor != null)
                {
                    SetPathPreview(null);
                    ActorClicked(actor.Entity, (short)actor.CellId, e.Button, ModifierKeys & (Keys.Shift | Keys.Control));
                }
                else
                {
                    UserMapCell clicked = GetCell(e.Location);
                    if (clicked != null) { SetPathPreview(null); OnCellclicked(clicked, e.Button, clicked != CellBottom); }
                }
            }
            if (panning) { Capture = false; Cursor = BaseCursor; DisplayStateChanged?.Invoke(); }
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
            // + et - du pavé numérique restent aux raccourcis MAXI / MINI du chat (Shortcuts) : ProcessCmdKey passe avant le KeyPreview.
            if (keyData == Keys.Home) { Fit(); return true; }
            if (keyData == Keys.Oemplus) { ZoomIn(); return true; }
            if (keyData == Keys.OemMinus) { ZoomOut(); return true; }
            return base.ProcessCmdKey(ref message, keyData);
        }
    }
}
