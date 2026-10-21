using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Threading.Tasks;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Game.Combats;
using Tool_BotProtocol.Game.Maps;

namespace Outil_Azur_complet.Bot.Controls
{
    /// <summary>
    /// Visée et chemin de combat dessinés comme le client 1.34 (lot X8) : portée du sort (<c>drawZone</c>, couleur
    /// <c>CELL_SPELL_RANGE_COLOR</c>), formes du pointeur sous la souris (<c>PointerHandler</c>, <c>CELL_SPELL_EFFECT_COLOR</c>),
    /// chemin de combat en formes de sélection opaques (<c>selectionHandler.select</c>, <c>CELL_PATH_OVER_COLOR</c>) et
    /// curseurs posés à côté de la flèche (icône du sort 25 × 25 à +15,+15, croix d'interdiction, quatre flèches du glisser).
    /// Rien n'est lu sur le disque pendant un dessin : images par <see cref="ClientAssets.TryCached"/> et
    /// <see cref="SelectionShapes.TryGet"/>, lectures sur le pool de threads puis redessin.
    /// </summary>
    public partial class UserMapControl
    {
        /// <summary><c>CELL_SPELL_RANGE_COLOR</c> (et <c>CELL_PATH_SELECT_COLOR</c>) du client : portée d'un sort.</summary>
        public const int SpellRangeRgb = 0x246696;
        /// <summary><c>CELL_SPELL_EFFECT_COLOR</c> (et <c>CELL_PATH_OVER_COLOR</c>) du client : zone d'effet et chemin survolé.</summary>
        public const int SpellEffectRgb = 0xFF6600;
        /// <summary>Cadre du curseur d'un sort (<c>setCursor(icône, {width: 25, height: 25, x: 15, y: 15})</c>), relatif à la souris.</summary>
        public static readonly Rectangle AimCursorBox = new Rectangle(15, 15, 25, 25);
        /// <summary>Cadre par défaut d'un curseur (<c>CURSOR_MAX_SIZE</c> 40, <c>CURSOR_CENTER</c> −20,−20), relatif à la souris.</summary>
        public static readonly Rectangle DefaultCursorBox = new Rectangle(-20, -20, 40, 40);
        /// <summary>Fichiers des curseurs exportés (<c>Resources/Bot/Client</c>).</summary>
        public const string ForbiddenCursor = "Forbidden", MoveCursor = "MapNavigatorMoveCursor";

        private HashSet<short> spellRange;
        private CellZoneLayer rangeLayer;
        // Formes du pointeur sous la souris (une par forme d'effet, dessinées l'une après l'autre : l'alpha s'additionne).
        private List<HashSet<short>> pointerSets;
        private List<CellZoneLayer> pointerLayers;
        private Func<short, IEnumerable<IEnumerable<short>>> pointerShapes;
        private string aimIconFamily, aimIconName;
        private Point? cursorPoint;
        private Rectangle cursorPainted;
        private bool pathSelection;
        private SelectionShapes selectionShapes;
        private readonly HashSet<string> cursorRequests = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Cellule sous la souris (null hors de la carte).</summary>
        public UserMapCell HoveredCell => CellH;
        /// <summary>Un sort (ou le corps à corps) est armé : la portée et le curseur du sort sont dessinés.</summary>
        public bool IsAiming => spellTargets != null;
        /// <summary>Cellules de la portée dessinée (<see cref="SpellRangeRgb"/>), vide hors visée.</summary>
        public IReadOnlyCollection<short> SpellRange => spellRange ?? (IReadOnlyCollection<short>)new short[0];
        /// <summary>Cellules de chaque forme du pointeur sous la souris (<see cref="SpellEffectRgb"/>), vide sans pointeur.</summary>
        public IReadOnlyList<IReadOnlyCollection<short>> PointerCells =>
            pointerSets == null ? (IReadOnlyList<IReadOnlyCollection<short>>)new IReadOnlyCollection<short>[0] : pointerSets.ToArray();
        /// <summary>La croix d'interdiction est posée sur le curseur du sort (cellule survolée hors des cibles valides).</summary>
        public bool CursorForbidden => IsAiming && (CellH == null || !spellTargets.Contains(CellH.id));
        /// <summary>L'aperçu du chemin suit les formes de sélection du client (combat) plutôt que l'aplat hors combat.</summary>
        public bool PathPreviewUsesSelection => pathSelection;
        /// <summary>Famille et nom de l'icône du curseur de visée (<see cref="SetAimIcon"/>).</summary>
        public string AimIcon => aimIconName == null ? null : aimIconFamily + "/" + aimIconName;

        /// <summary>
        /// Formes du pointeur pour la cellule survolée (une liste de cellules par forme), ou <c>null</c> sans pointeur. En visée,
        /// elles ne sont dessinées que sur une cible valide, comme <c>drawPointer</c> après <c>checkCanLaunchSpellOnCell</c>.
        /// Appelé sur le fil de l'interface à chaque changement de cellule survolée : pas de recherche de chemin ici.
        /// </summary>
        public Func<short, IEnumerable<IEnumerable<short>>> PointerShapes
        {
            get => pointerShapes;
            set { pointerShapes = value; UpdatePointer(); }
        }

        /// <summary>Cellules de la portée du sort armé (null : aucune).</summary>
        public void SetSpellRange(IEnumerable<short> cells)
        {
            HashSet<short> next = cells == null ? null : new HashSet<short>(cells);
            if ((spellRange == null && next == null) || (spellRange != null && next != null && spellRange.SetEquals(next))) return;
            spellRange = next;
            rangeLayer?.Dispose(); rangeLayer = null;
            Invalidate();
        }

        /// <summary>Icône du curseur de visée : famille et nom de <see cref="ClientAssets"/> (sort, poing, arme) ; null : aucune.</summary>
        public void SetAimIcon(string family, string name)
        {
            if (family == aimIconFamily && name == aimIconName) return;
            aimIconFamily = name == null ? null : family; aimIconName = name;
            InvalidateCursor();
        }

        /// <summary>
        /// Remplace l'aperçu du chemin ; <paramref name="clientSelection"/> le dessine comme le chemin de combat du client
        /// (forme de sélection de la pente du sol, teinte opaque <see cref="SpellEffectRgb"/>).
        /// </summary>
        public void SetPathPreview(IEnumerable<short> cells, bool clientSelection)
        {
            HashSet<short> next = cells == null ? null : new HashSet<short>(cells);
            if (next != null && next.Count == 0) next = null;
            bool same = (pathPreview == null && next == null) || (pathPreview != null && next != null && pathPreview.SetEquals(next));
            if (same && (next == null || pathSelection == clientSelection)) return;
            pathPreview = next;
            pathSelection = next != null && clientSelection;
            Invalidate();
        }

        /// <summary>Recalcule les formes du pointeur pour la cellule survolée (cibles ou fournisseur changés).</summary>
        public void UpdatePointer()
        {
            List<HashSet<short>> next = null;
            UserMapCell hover = CellH;
            if (hover != null && pointerShapes != null && (spellTargets == null || spellTargets.Contains(hover.id)))
            {
                IEnumerable<IEnumerable<short>> shapes = null;
                try { shapes = pointerShapes(hover.id); }
                catch (Exception error) when (!(error is OutOfMemoryException))
                {
                    Account?.Logger?.LogError("CARTE", "Pointeur de la cellule " + hover.id + " impossible : " + error.Message);
                }
                if (shapes != null)
                    next = shapes.Where(shape => shape != null).Select(shape => new HashSet<short>(shape)).Where(set => set.Count > 0).ToList();
                if (next != null && next.Count == 0) next = null;
            }
            bool same = (pointerSets == null && next == null)
                || (pointerSets != null && next != null && pointerSets.Count == next.Count && pointerSets.Zip(next, (a, b) => a.SetEquals(b)).All(equal => equal));
            if (same) return;
            DisposePointerLayers();
            pointerSets = next;
            Invalidate();
        }

        private void DisposePointerLayers()
        {
            if (pointerLayers != null) foreach (CellZoneLayer layer in pointerLayers) layer?.Dispose();
            pointerLayers = null;
        }

        private void DisposeAimLayers()
        {
            rangeLayer?.Dispose(); rangeLayer = null;
            DisposePointerLayers();
            if (selectionShapes != null) { selectionShapes.Loaded -= SelectionArrived; selectionShapes = null; }
        }

        /// <summary>Portée du sort (couche Zone du client), au-dessus des cellules et des zones <c>GDZ</c>.</summary>
        private void DrawSpellRange(Graphics graphics)
        {
            if (spellRange == null || worldPolygons == null) return;
            if (rangeLayer == null || !ReferenceEquals(rangeLayer.Source, worldPolygons))
            {
                rangeLayer?.Dispose();
                rangeLayer = CellZoneLayer.Build(spellRange, worldPolygons);
            }
            rangeLayer?.Draw(graphics, Color.FromArgb(SpellRangeRgb), 1 / Math.Max(.01f, viewScale));
        }

        /// <summary>Formes du pointeur (couche Pointer du client, au-dessus de la sélection, sous les objets et les acteurs).</summary>
        private void DrawPointer(Graphics graphics)
        {
            if (pointerSets == null || worldPolygons == null) return;
            if (pointerLayers == null || pointerLayers.Any(layer => layer != null && !ReferenceEquals(layer.Source, worldPolygons)))
            {
                DisposePointerLayers();
                pointerLayers = pointerSets.Select(set => CellZoneLayer.Build(set, worldPolygons)).ToList();
            }
            float pen = 1 / Math.Max(.01f, viewScale);
            foreach (CellZoneLayer layer in pointerLayers) layer?.Draw(graphics, Color.FromArgb(SpellEffectRgb), pen);
        }

        /// <summary>
        /// Chemin de combat : forme <c>s&lt;pente&gt;</c> teinte, posée au point d'enregistrement de la cellule ; la cellule
        /// praticable qui porte un objet 2 reçoit la sienne dans la scène, juste après l'objet (<c>cellule × 100 + 2</c>).
        /// Sans forme exportée, le losange de la cellule est rempli de la même couleur opaque.
        /// </summary>
        private void DrawPathSelection(Graphics graphics, bool depthCells)
        {
            if (pathPreview == null || worldPolygons == null) return;
            foreach (short cell in pathPreview)
                if (InObjectLayer(cell) == depthCells) DrawSelectionShape(graphics, cell);
        }

        private bool InObjectLayer(short cellId)
        {
            Cell cell = Account?.Game?.Map?.GetCellFromId(cellId);
            return cell != null && (int)cell.C_Types > 1 && cell.layer_object_2_num != 0;
        }

        private IEnumerable<BotMapArtwork.DepthLayer> SelectionDepthLayers()
        {
            if (pathSelection && pathPreview != null)
                foreach (short cell in pathPreview.ToArray())
                    if (InObjectLayer(cell))
                    {
                        short id = cell;
                        yield return new BotMapArtwork.DepthLayer { Depth = id * 100f + 2, Order = 0, Draw = graphics => DrawSelectionShape(graphics, id) };
                    }
            if (!AllowedZoneShown) yield break;
            foreach (short cell in spellTargets.ToArray())
                if (InObjectLayer(cell))
                {
                    short id = cell;
                    yield return new BotMapArtwork.DepthLayer { Depth = id * 100f + 2, Order = 0,
                        Draw = graphics => DrawSelectionShape(graphics, id, AllowedTargetRgb, AllowedTargetAlphaPercent) };
                }
        }

        private void DrawSelectionShape(Graphics graphics, short cellId) => DrawSelectionShape(graphics, cellId, SpellEffectRgb, 100);

        /// <summary>
        /// Forme de sélection de la cellule (<c>selectionHandler.select</c>) teinte de <paramref name="rgb"/> à l'opacité
        /// <paramref name="alphaPercent"/> ; la pente est celle du décor dessiné (à plat en mode tactique).
        /// </summary>
        private void DrawSelectionShape(Graphics graphics, short cellId, int rgb, int alphaPercent)
        {
            if (cellId < 0 || worldPolygons == null || cellId >= worldPolygons.Length) return;
            BotMapArtwork.ArtworkCell[] shown = artwork?.DisplayedCells;
            BotMapArtwork.ArtworkCell decor = shown != null && shown.Length == worldPolygons.Length ? shown[cellId] : null;
            int alpha = Math.Max(0, Math.Min(255, alphaPercent * 255 / 100));
            if (decor != null)
            {
                SelectionShapes shapes = SelectionShapesOf(artwork);
                if (shapes != null && shapes.TryGet(decor.Slope, rgb, out Bitmap image, out RectangleF anchor))
                {
                    var target = new RectangleF(decor.Center.X + anchor.X, decor.Center.Y + anchor.Y, anchor.Width, anchor.Height);
                    Bitmap drawn = alpha >= 255 ? image : Faded(image, alpha);
                    if (drawn != null) { lock (drawn) graphics.DrawImage(drawn, target); return; }
                }
            }
            using (var brush = new SolidBrush(Color.FromArgb(alpha, Color.FromArgb(rgb)))) graphics.FillPolygon(brush, worldPolygons[cellId]);
        }

        // Formes teintes à l'opacité d'un clip (_alpha du client), par forme partagée puis par alpha. Le calcul est fait une fois
        // (une forme fait 52 × 26 pixels) : sous Mono, DrawImage avec une ColorMatrix d'ImageAttributes ne dessinait rien ici.
        private static readonly Dictionary<Bitmap, Dictionary<int, Bitmap>> fadedShapes = new Dictionary<Bitmap, Dictionary<int, Bitmap>>();

        /// <summary>Copie de <paramref name="shape"/> dont l'alpha de chaque pixel est multiplié par <paramref name="alpha"/> / 255.</summary>
        private static Bitmap Faded(Bitmap shape, int alpha)
        {
            lock (fadedShapes)
                if (fadedShapes.TryGetValue(shape, out Dictionary<int, Bitmap> byAlpha) && byAlpha.TryGetValue(alpha, out Bitmap done)) return done;
            Bitmap copy = null;
            try
            {
                lock (shape) copy = new Bitmap(Math.Max(1, shape.Width), Math.Max(1, shape.Height), PixelFormat.Format32bppArgb);
                using (Graphics graphics = Graphics.FromImage(copy))
                {
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    lock (shape) graphics.DrawImage(shape, 0, 0, copy.Width, copy.Height);
                }
                BitmapData data = copy.LockBits(new Rectangle(0, 0, copy.Width, copy.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                try
                {
                    var row = new int[copy.Width];
                    for (int y = 0; y < copy.Height; y++)
                    {
                        IntPtr line = IntPtr.Add(data.Scan0, y * data.Stride);
                        System.Runtime.InteropServices.Marshal.Copy(line, row, 0, row.Length);
                        for (int x = 0; x < row.Length; x++)
                            row[x] = (((int)((uint)row[x] >> 24) * alpha / 255) << 24) | (row[x] & 0xFFFFFF);
                        System.Runtime.InteropServices.Marshal.Copy(row, 0, line, row.Length);
                    }
                }
                finally { copy.UnlockBits(data); }
            }
            catch (Exception error) when (error is ArgumentException || error is System.Runtime.InteropServices.ExternalException
                || error is InvalidOperationException || error is OutOfMemoryException)
            {
                copy?.Dispose();
                return null;
            }
            lock (fadedShapes)
            {
                if (!fadedShapes.TryGetValue(shape, out Dictionary<int, Bitmap> byAlpha)) fadedShapes[shape] = byAlpha = new Dictionary<int, Bitmap>();
                if (byAlpha.TryGetValue(alpha, out Bitmap other)) { copy.Dispose(); return other; }
                byAlpha[alpha] = copy;
                return copy;
            }
        }

        // ---------------------------------------------------------------- lot X33 : cases ciblables et mode tactique

        /// <summary>Couleur de <c>drawAllowedZone</c> du client (<c>select(cellule, 26316, "spell", 50)</c>) : #0066CC.</summary>
        public const int AllowedTargetRgb = 0x0066CC;
        /// <summary>Opacité de <c>drawAllowedZone</c>, en pour cent.</summary>
        public const int AllowedTargetAlphaPercent = 50;
        private BotOptions displayOptions, hookedOptions;
        private Fights hookedFight;
        private volatile bool drawnTactic;

        /// <summary>Options lues pour la mise en valeur des cases ciblables ; <c>null</c> : <see cref="BotOptions.Current"/>.</summary>
        public BotOptions Options
        {
            get => displayOptions ?? BotOptions.Current;
            set { displayOptions = value; HookFight(); Invalidate(); }
        }

        /// <summary>
        /// Les cibles valides du sort armé sont mises en valeur (option <c>AdvancedLineOfSight</c> du client, vraie par défaut) :
        /// sinon seule la portée est dessinée et une cible refusée (hors de vue…) n'est signalée que par le curseur.
        /// </summary>
        public bool AllowedZoneShown => spellTargets != null && Options?.AdvancedLineOfSight == true;

        /// <summary>Le décor est dessiné en mode tactique : <see cref="Fights.TacticMode"/> pendant un combat (placement compris).</summary>
        public bool TacticView
        {
            get
            {
                Fights fight = Account?.Game?.Fight;
                return fight != null && fight.TacticMode && fight.IsInFight;
            }
        }

        /// <summary>
        /// <c>drawAllowedZone</c> : chaque cible valide reçoit la forme de sélection de sa pente, #0066CC à 50 %, au-dessus de la
        /// portée ; une cellule praticable qui porte un objet 2 la reçoit dans la scène (<see cref="SelectionDepthLayers"/>).
        /// </summary>
        private void DrawAllowedZone(Graphics graphics)
        {
            if (!AllowedZoneShown || worldPolygons == null) return;
            foreach (short cell in spellTargets)
                if (!InObjectLayer(cell)) DrawSelectionShape(graphics, cell, AllowedTargetRgb, AllowedTargetAlphaPercent);
        }

        /// <summary>Suit le mode tactique du combat et l'option des cases ciblables du compte affiché (fil de l'interface).</summary>
        private void HookFight()
        {
            Fights fight = Account?.Game?.Fight;
            if (!ReferenceEquals(fight, hookedFight))
            {
                if (hookedFight != null) { hookedFight.TacticModeChanged -= OnTacticModeChanged; hookedFight.CombatChanged -= OnFightStateChanged; }
                hookedFight = fight;
                if (fight != null) { fight.TacticModeChanged += OnTacticModeChanged; fight.CombatChanged += OnFightStateChanged; }
            }
            BotOptions options = Account == null ? null : Options;
            if (!ReferenceEquals(options, hookedOptions))
            {
                if (hookedOptions != null) hookedOptions.OptionChanged -= OnDisplayOptionChanged;
                hookedOptions = options;
                if (options != null) options.OptionChanged += OnDisplayOptionChanged;
            }
        }

        private void UnhookFight()
        {
            if (hookedFight != null) { hookedFight.TacticModeChanged -= OnTacticModeChanged; hookedFight.CombatChanged -= OnFightStateChanged; }
            if (hookedOptions != null) hookedOptions.OptionChanged -= OnDisplayOptionChanged;
            hookedFight = null; hookedOptions = null;
        }

        private void OnTacticModeChanged(bool active) => RequestRepaint();

        // Début ou fin du combat : le décor tactique n'est dessiné qu'en combat.
        private void OnFightStateChanged() { if (TacticView != drawnTactic) RequestRepaint(); }

        private void OnDisplayOptionChanged(object sender, BotOptionChangedEventArgs e)
        {
            if (e != null && e.Name == "AdvancedLineOfSight" && spellTargets != null) RequestRepaint();
        }

        /// <summary>Applique le mode tactique au décor avant de le dessiner (fil de l'interface).</summary>
        private void ApplyTacticView()
        {
            bool tactic = TacticView;
            drawnTactic = tactic;
            if (artwork != null) artwork.TacticMode = tactic;
        }

        private void SelectionArrived() => RequestRepaint();

        private SelectionShapes SelectionShapesOf(BotMapArtwork scenery)
        {
            if (scenery == null) return null;
            if (selectionShapes == null)
            {
                selectionShapes = SelectionShapes.For(scenery.ResourceDirectory);
                if (selectionShapes != null) selectionShapes.Loaded += SelectionArrived;
            }
            return selectionShapes;
        }

        // ---------------------------------------------------------------- curseurs posés à côté de la flèche

        /// <summary>Cadre du curseur dessiné pour la position <paramref name="mouse"/> (vide sans curseur du client).</summary>
        public Rectangle CursorBounds(Point mouse)
        {
            if (panning) return new Rectangle(mouse.X + DefaultCursorBox.X, mouse.Y + DefaultCursorBox.Y, DefaultCursorBox.Width, DefaultCursorBox.Height);
            if (IsAiming) return new Rectangle(mouse.X + AimCursorBox.X, mouse.Y + AimCursorBox.Y, AimCursorBox.Width, AimCursorBox.Height);
            return Rectangle.Empty;
        }

        /// <summary>La souris bouge (ou le mode change) : redessine l'ancien et le nouveau cadre du curseur seulement.</summary>
        private void MoveCursorOverlay(Point? mouse)
        {
            cursorPoint = mouse;
            InvalidateCursor();
        }

        private void InvalidateCursor()
        {
            Rectangle next = cursorPoint.HasValue ? CursorBounds(cursorPoint.Value) : Rectangle.Empty;
            if (next == cursorPainted) { if (!next.IsEmpty) Invalidate(Rectangle.Inflate(next, 2, 2)); return; }
            if (!cursorPainted.IsEmpty) Invalidate(Rectangle.Inflate(cursorPainted, 2, 2));
            if (!next.IsEmpty) Invalidate(Rectangle.Inflate(next, 2, 2));
        }

        /// <summary>Dernier dessin de l'écran : curseur du sort et croix, ou quatre flèches pendant le glisser de la vue.</summary>
        private void DrawCursorOverlay(Graphics graphics)
        {
            cursorPainted = Rectangle.Empty;
            if (!cursorPoint.HasValue) return;
            Rectangle box = CursorBounds(cursorPoint.Value);
            if (box.IsEmpty) return;
            cursorPainted = box;
            if (panning) { DrawCursorImage(graphics, "Client", MoveCursor, box); return; }
            if (aimIconName != null) DrawCursorImage(graphics, aimIconFamily, aimIconName, box);
            if (CursorForbidden) DrawCursorImage(graphics, "Client", ForbiddenCursor, box);
        }

        /// <summary>Image entière dans le cadre, proportions gardées et centrée (<c>Loader</c> du client avec <c>scaleContent</c>).</summary>
        private void DrawCursorImage(Graphics graphics, string family, string name, Rectangle box)
        {
            Bitmap image = CursorImage(family, name);
            if (image == null) return;
            GraphicsState state = graphics.Save();
            try { lock (image) ClientAssets.DrawFit(graphics, image, box); }
            finally { graphics.Restore(state); }
        }

        private Bitmap CursorImage(string family, string name)
        {
            if (string.IsNullOrEmpty(family) || string.IsNullOrEmpty(name)) return null;
            if (ClientAssets.TryCached(family, name, out Bitmap cached)) return cached;
            string key = family + "/" + name;
            lock (cursorRequests) if (!cursorRequests.Add(key)) return null;
            Task<Bitmap> load;
            try { load = ClientAssets.GetAsync(family, name); }
            catch (Exception error) when (!(error is OutOfMemoryException)) { lock (cursorRequests) cursorRequests.Remove(key); return null; }
            load.ContinueWith(task =>
            {
                // Une seule lecture à la fois par curseur ; une image chassée du cache plus tard est redemandée au dessin suivant.
                lock (cursorRequests) cursorRequests.Remove(key);
                if (task.Status == TaskStatus.RanToCompletion && task.Result != null) RequestRepaint();
            }, TaskScheduler.Default);
            return null;
        }
    }
}
