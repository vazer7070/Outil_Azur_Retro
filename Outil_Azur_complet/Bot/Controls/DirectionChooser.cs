using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game.Actions;
using Tool_BotProtocol.Game.Interactions;

namespace Outil_Azur_complet.Bot.Controls
{
    /// <summary>
    /// Sélecteur d'orientation du client 1.34 (<c>DirectionChooser</c>, entrée <c>CHANGE_DIRECTION</c> du menu de son
    /// personnage) : une flèche par orientation autour du personnage, huit si <c>Player.canMoveInAllDirections</c> (bit 8192
    /// de <c>AR</c>), sinon les quatre diagonales (1, 3, 5, 7). Un clic sur une flèche envoie <c>eD&lt;direction&gt;</c>, un clic
    /// ailleurs ou Échap referme sans rien envoyer. Les flèches du client sont des formes colorées à l'exécution, vides une fois
    /// exportées : le bot les dessine dans la palette Retro (parchemin, filet doré, brun, survol olive, orientation actuelle dorée).
    /// </summary>
    public sealed class DirectionChooser : Control
    {
        /// <summary>Taille du sélecteur, centré sur le personnage.</summary>
        public static readonly Size ChooserSize = new Size(124, 100);
        /// <summary>Position des flèches par rapport au centre, dans l'ordre du client (0 = droite, puis sens horaire).</summary>
        private static readonly Point[] Offsets =
        {
            new Point(46, 0), new Point(33, 23), new Point(0, 36), new Point(-33, 23),
            new Point(-46, 0), new Point(-33, -23), new Point(0, -36), new Point(33, -23),
        };
        private const float ArrowLength = 13f, ArrowWidth = 9f, HitRadius = 12f;

        private readonly MapActions actions;
        private readonly Action<string> report;
        private readonly HashSet<int> allowed;
        private readonly ToolTip hint = new ToolTip { InitialDelay = 300, ShowAlways = true };
        private ToolStripDropDown drop;
        private int hover = -1;

        /// <summary>Dernier sélecteur ouvert et encore affiché (tests et diagnostic), sinon <c>null</c>.</summary>
        public static DirectionChooser Current { get; private set; }

        private DirectionChooser(MapActions actions, IReadOnlyList<int> directions, int currentDirection, Action<string> report)
        {
            this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
            this.report = report;
            Directions = (directions ?? new int[0]).Where(direction => direction >= 0 && direction <= 7).Distinct().OrderBy(d => d).ToArray();
            allowed = new HashSet<int>(Directions);
            CurrentDirection = currentDirection;
            Size = ChooserSize; MinimumSize = ChooserSize;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = BotUi.Paper; ForeColor = BotUi.Ink; Cursor = Cursors.Hand; TabStop = true;
            Name = "direction-chooser"; AccessibleName = MapActionTexts.ChangeDirection; AccessibleRole = AccessibleRole.ButtonDropDownGrid;
        }

        /// <summary>Orientations proposées (flèches visibles).</summary>
        public IReadOnlyList<int> Directions { get; }
        /// <summary>Orientation actuelle du personnage, mise en évidence (-1 si inconnue).</summary>
        public int CurrentDirection { get; }
        public bool IsOpen => drop != null && !drop.IsDisposed && drop.Visible;

        /// <summary>
        /// Ouvre le sélecteur centré sur <paramref name="screenCenter"/> (le personnage à l'écran). Le précédent est refermé.
        /// Renvoie <c>null</c> si la carte n'est plus affichée.
        /// </summary>
        public static DirectionChooser Open(Control owner, Point screenCenter, MapActions actions, IReadOnlyList<int> directions,
            int currentDirection, Action<string> report)
        {
            if (owner == null || owner.IsDisposed || !owner.IsHandleCreated || actions == null) return null;
            Current?.Close();
            var chooser = new DirectionChooser(actions, directions, currentDirection, report);
            var host = new ToolStripControlHost(chooser) { AutoSize = false, Size = ChooserSize, Margin = Padding.Empty, Padding = Padding.Empty };
            var drop = new ToolStripDropDown { AutoSize = false, Size = ChooserSize, Padding = Padding.Empty, Margin = Padding.Empty,
                DropShadowEnabled = false, BackColor = BotUi.Paper, Renderer = new RetroMenuRenderer(), Name = "direction-chooser-popup",
                AccessibleName = MapActionTexts.ChangeDirection };
            drop.Items.Add(host);
            chooser.drop = drop;
            drop.Closed += (s, e) =>
            {
                if (ReferenceEquals(Current, chooser)) Current = null;
                // Libéré après la fin du message de fermeture (le clic d'une flèche est encore en cours).
                try { if (owner.IsHandleCreated && !owner.IsDisposed) { owner.BeginInvoke((Action)drop.Dispose); return; } }
                catch (InvalidOperationException) { }
                drop.Dispose();
            };
            Current = chooser;
            Point topLeft = owner.PointToClient(new Point(screenCenter.X - ChooserSize.Width / 2, screenCenter.Y - ChooserSize.Height / 2));
            try { drop.Show(owner, topLeft); chooser.Focus(); }
            catch (Exception error) when (error is InvalidOperationException || error is ArgumentException || error is NotImplementedException)
            {
                Current = null; drop.Dispose();
                report?.Invoke("Sélecteur d'orientation indisponible : " + error.Message);
                return null;
            }
            return chooser;
        }

        /// <summary>Clic sur une flèche : referme le sélecteur et envoie <c>eD&lt;direction&gt;</c> ; renvoie le message affiché.</summary>
        public async Task<string> ChooseAsync(int direction)
        {
            if (!allowed.Contains(direction)) return "Orientation " + direction + " absente du sélecteur.";
            Close();
            InteractionResult result = await actions.SetDirectionAsync(direction);
            return result?.Message;
        }

        /// <summary>Referme sans rien envoyer (clic ailleurs, Échap).</summary>
        public void Close()
        {
            if (drop != null && !drop.IsDisposed && drop.Visible) drop.Close(ToolStripDropDownCloseReason.CloseCalled);
            if (ReferenceEquals(Current, this)) Current = null;
        }

        /// <summary>Orientation sous un point du sélecteur, ou -1.</summary>
        public int DirectionAt(Point location)
        {
            PointF center = Center;
            foreach (int direction in Directions)
            {
                PointF arrow = ArrowCenter(center, direction);
                float dx = location.X - arrow.X, dy = location.Y - arrow.Y;
                if (dx * dx + dy * dy <= HitRadius * HitRadius) return direction;
            }
            return -1;
        }

        /// <summary>Centre de la flèche d'une orientation dans le sélecteur (tests).</summary>
        public Point ArrowLocation(int direction)
        {
            PointF value = ArrowCenter(Center, direction);
            return new Point((int)Math.Round(value.X), (int)Math.Round(value.Y));
        }

        private PointF Center => new PointF(Width / 2f, Height / 2f);

        private static PointF ArrowCenter(PointF center, int direction) =>
            direction < 0 || direction > 7 ? center : new PointF(center.X + Offsets[direction].X, center.Y + Offsets[direction].Y);

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var fill = new SolidBrush(BotUi.Paper)) g.FillRectangle(fill, bounds);
            using (var dark = new Pen(BotUi.Frame)) g.DrawRectangle(dark, bounds);
            using (var gold = new Pen(BotUi.Gold)) g.DrawRectangle(gold, Rectangle.Inflate(bounds, -1, -1));
            PointF center = Center;
            // Losange de la cellule du personnage, comme la case sous le sprite.
            PointF[] cell = { new PointF(center.X, center.Y - 9), new PointF(center.X + 18, center.Y), new PointF(center.X, center.Y + 9), new PointF(center.X - 18, center.Y) };
            using (var fill = new SolidBrush(BotUi.PaperLight)) g.FillPolygon(fill, cell);
            using (var line = new Pen(BotUi.Gold)) g.DrawPolygon(line, cell);
            using (var dot = new SolidBrush(BotUi.Olive)) g.FillEllipse(dot, center.X - 3, center.Y - 3, 6, 6);
            foreach (int direction in Directions) DrawArrow(g, center, direction);
        }

        private void DrawArrow(Graphics g, PointF center, int direction)
        {
            PointF middle = ArrowCenter(center, direction);
            float dx = Offsets[direction].X, dy = Offsets[direction].Y;
            float length = (float)Math.Sqrt(dx * dx + dy * dy);
            float ux = dx / length, uy = dy / length, nx = -uy, ny = ux;
            float half = ArrowLength / 2f, wing = ArrowWidth / 2f;
            PointF tip = new PointF(middle.X + ux * half, middle.Y + uy * half);
            PointF back = new PointF(middle.X - ux * half, middle.Y - uy * half);
            PointF[] arrow =
            {
                tip,
                new PointF(back.X + nx * wing, back.Y + ny * wing),
                new PointF(middle.X - ux * (half * 0.35f), middle.Y - uy * (half * 0.35f)),
                new PointF(back.X - nx * wing, back.Y - ny * wing),
            };
            Color fillColor = direction == hover ? BotUi.Olive : direction == CurrentDirection ? BotUi.Gold : BotUi.Frame;
            using (var fill = new SolidBrush(fillColor)) g.FillPolygon(fill, arrow);
            using (var outline = new Pen(direction == CurrentDirection ? BotUi.Frame : BotUi.FrameLight)) g.DrawPolygon(outline, arrow);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int value = DirectionAt(e.Location);
            if (value == hover) return;
            hover = value;
            string name = value < 0 ? string.Empty : MapActions.DirectionName(value);
            hint.SetToolTip(this, name.Length == 0 ? string.Empty : char.ToUpperInvariant(name[0]) + name.Substring(1));
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover < 0) return;
            hover = -1; Invalidate();
        }

        protected override async void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            int direction = DirectionAt(e.Location);
            if (direction < 0) { Close(); return; }
            try
            {
                string message = await ChooseAsync(direction);
                if (!string.IsNullOrEmpty(message)) report?.Invoke(message);
            }
            catch (Exception error) when (!(error is OutOfMemoryException)) { report?.Invoke("Orientation impossible : " + error.Message); }
        }

        protected override bool IsInputKey(Keys keyData) => keyData == Keys.Escape || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; return; }
            base.OnKeyDown(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                hint.Dispose();
                if (ReferenceEquals(Current, this)) Current = null;
            }
            base.Dispose(disposing);
        }
    }
}
