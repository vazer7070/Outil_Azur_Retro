using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Controls.Banner;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Combats;

namespace Outil_Azur_complet.Bot.Controls.Fight
{
    /// <summary>Un portrait de la ligne de temps (instantané pour le dessin, les infobulles et les tests).</summary>
    public sealed class TimelineEntry
    {
        internal TimelineEntry(int id, CombatFighter fighter, Rectangle bounds, bool current, bool highlighted)
        {
            FighterId = id; Bounds = bounds; IsCurrent = current; IsHighlighted = highlighted;
            if (fighter == null) { Name = "Combattant #" + id.ToString(CultureInfo.InvariantCulture); Team = -1; Life = MaximumLife = ActionPoints = MovementPoints = -1; return; }
            Name = string.IsNullOrEmpty(fighter.Name) ? "Combattant #" + id.ToString(CultureInfo.InvariantCulture) : fighter.Name;
            Gfx = fighter.Gfx; Team = fighter.Team; Life = fighter.Life; MaximumLife = fighter.MaximumLife;
            ActionPoints = fighter.ActionPoints; MovementPoints = fighter.MovementPoints; IsInvisible = fighter.IsInvisible; IsDead = fighter.IsDead;
        }
        public int FighterId { get; }
        public string Name { get; }
        public int Gfx { get; }
        public int Team { get; }
        public int Life { get; }
        public int MaximumLife { get; }
        public int ActionPoints { get; }
        public int MovementPoints { get; }
        public bool IsInvisible { get; }
        public bool IsDead { get; }
        /// <summary>Cadre du portrait dans le contrôle.</summary>
        public Rectangle Bounds { get; }
        /// <summary>Combattant dont c'est le tour (<c>GTS</c>) : le pointeur est dessiné derrière lui.</summary>
        public bool IsCurrent { get; }
        /// <summary>Combattant annoncé par <c>GTR</c> en attendant son <c>GTS</c>, ou le combattant courant : cadre doré.</summary>
        public bool IsHighlighted { get; }
        /// <summary>Points de vie restants entre 0 et 1 ; 1 quand ils sont inconnus (le serveur ne les a pas encore envoyés).</summary>
        public double LifeRatio => MaximumLife > 0 && Life >= 0 ? Math.Max(0, Math.Min(1, (double)Life / MaximumLife)) : (Life == 0 ? 0 : 1);
    }

    /// <summary>
    /// Ligne de temps du combat (<c>Timeline</c> du client 1.34) : un portrait par combattant vivant dans l'ordre de jeu
    /// (<c>GTL</c>), pointeur orange derrière le combattant dont c'est le tour (<c>GTS</c>), cadre doré sur celui qu'annonce
    /// <c>GTR</c>, barre de vie teintée de la couleur de l'équipe (<c>TEAMS_COLOR</c> : rouge, bleu), chrono vertical du tour
    /// (<c>GTS&lt;id&gt;|&lt;ms&gt;</c>) et portrait pris dans la pose <c>static</c> orientée à droite des sprites exportés, grisé pour
    /// un combattant invisible. Les images du client sont lues hors du fil de l'interface ; sans elles, le contrôle dessine
    /// des cadres de la palette. Les événements de <see cref="Fights"/> arrivent sur le fil réseau : tout passe par <see cref="BotUi.OnUi"/>.
    /// </summary>
    public sealed class TimelineControl : Control
    {
        /// <summary><c>ITEM_WIDTH</c> du client : pas entre deux portraits.</summary>
        public const int ItemWidth = 34;
        /// <summary>Taille d'un portrait (symbole <c>TimelineItem</c> à l'échelle 1).</summary>
        public const int ItemHeight = 35, ItemImageWidth = 30;
        /// <summary>Hauteur du pointeur au-dessus du portrait (symbole <c>TimelinePointer</c>, 68 × 104 à l'échelle 2).</summary>
        public const int PointerOverhang = 17;
        private const int PaddingX = 8, PaddingTop = 4, PaddingBottom = 6;
        private static readonly Color[] TeamColors = { Color.FromArgb(255, 0, 0), Color.FromArgb(0, 0, 255) };

        private readonly Timer chrono = new Timer { Interval = 250 };
        private readonly ToolTip tips = new ToolTip();
        private readonly ActorSprites sprites;
        private Accounts account;
        private Bitmap itemBack, itemLife, pointer;
        private Bitmap lifeBlue;
        private FightTimeline timeline;
        private List<TimelineEntry> entries = new List<TimelineEntry>();
        private int hovered = -1;
        private bool released;

        public TimelineControl() : this(null) { }

        /// <param name="spriteDirectory">Dossier des sprites d'acteurs ; <c>null</c> = <see cref="ActorSprites.DefaultDirectory"/>.</param>
        public TimelineControl(string spriteDirectory)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = BotUi.Frame; ForeColor = BotUi.PaperLight; Font = BotFonts.Get(8); TabStop = false; Visible = false;
            AccessibleRole = AccessibleRole.List; AccessibleName = "Ligne de temps du combat"; Name = "fight-timeline";
            Height = PaddingTop + PointerOverhang + ItemHeight + PaddingBottom;
            sprites = new ActorSprites(spriteDirectory);
            sprites.SheetsLoaded += (s, e) => BotUi.OnUi(this, () => Invalidate());
            chrono.Tick += (s, e) => { if (timeline == null || timeline.CurrentActorId == 0) chrono.Stop(); Invalidate(); };
            BannerArt.Request(this, "TimelineItem_fond", image => { itemBack = image; Invalidate(); });
            BannerArt.Request(this, "TimelineItem_vie", image => { itemLife = image; Invalidate(); });
            BannerArt.Request(this, "TimelinePointer", image => { pointer = image; Invalidate(); });
        }

        /// <summary>Portraits affichés, dans l'ordre de jeu.</summary>
        public IReadOnlyList<TimelineEntry> Entries => entries;
        /// <summary>Combattant dont c'est le tour (<c>GTS</c>) ; 0 hors tour.</summary>
        public int CurrentFighterId => timeline?.CurrentActorId ?? 0;
        /// <summary>Combattant mis en avant : celui du tour, sinon celui qu'annonce <c>GTR</c>.</summary>
        public int HighlightedFighterId => timeline == null ? 0 : (timeline.CurrentActorId != 0 ? timeline.CurrentActorId : timeline.ReadyActorId);
        /// <summary>Temps restant du tour courant, en millisecondes (0 hors tour).</summary>
        public int RemainingMilliseconds => timeline?.RemainingMilliseconds(DateTime.UtcNow) ?? 0;
        /// <summary>Vrai quand les trois symboles du client (<c>TimelineItem</c>, sa barre de vie, <c>TimelinePointer</c>) sont chargés.</summary>
        public bool HasClientImages => itemBack != null && itemLife != null && pointer != null;

        /// <summary>Associe la ligne de temps à la session (thread de l'interface) ; <c>null</c> la détache.</summary>
        public void Bind(Accounts value)
        {
            Release();
            account = value;
            var fight = account?.Game?.Fight;
            if (fight != null) { fight.CombatChanged += OnCombatChanged; released = false; }
            RefreshFromFight();
        }

        /// <summary>Retire les abonnements (fermeture de la fenêtre) ; tolère une session déjà libérée.</summary>
        public void Release()
        {
            if (released) return;
            released = true;
            var fight = account?.Game?.Fight;
            if (fight != null) fight.CombatChanged -= OnCombatChanged;
            account = null; chrono.Stop();
        }

        private void OnCombatChanged() => BotUi.OnUi(this, RefreshFromFight);

        /// <summary>Recopie l'état du combat (ordre, tour, combattants) et recalcule les portraits (thread de l'interface).</summary>
        public void RefreshFromFight()
        {
            if (IsDisposed || Disposing) return;
            var fight = account?.Game?.Fight;
            if (fight == null || !fight.IsInFight) { timeline = null; entries = new List<TimelineEntry>(); Visible = false; chrono.Stop(); sprites.ReleaseAll(); return; }
            timeline = fight.Timeline;
            IReadOnlyDictionary<int, CombatFighter> fighters = fight.Fighters;
            var list = new List<TimelineEntry>();
            int x = PaddingX, top = PaddingTop + PointerOverhang, highlighted = HighlightedFighterId;
            foreach (int id in timeline.Order)
            {
                CombatFighter fighter;
                fighters.TryGetValue(id, out fighter);
                if (fighter != null && fighter.IsDead) continue;
                var bounds = new Rectangle(x + (ItemWidth - ItemImageWidth) / 2, top, ItemImageWidth, ItemHeight);
                list.Add(new TimelineEntry(id, fighter, bounds, id == timeline.CurrentActorId, id == highlighted));
                x += ItemWidth;
            }
            entries = list;
            Width = Math.Max(ItemWidth + 2 * PaddingX, x + PaddingX);
            Visible = list.Count > 0;
            if (timeline.CurrentActorId != 0 && timeline.TurnDurationMilliseconds > 0) chrono.Start(); else chrono.Stop();
            AccessibleName = "Ligne de temps du combat : " + string.Join(", ", list.Select(entry => entry.Name));
            Center();
            Invalidate();
        }

        /// <summary>Place le contrôle en haut de son parent, centré (appelé au redimensionnement de la carte).</summary>
        public void Center()
        {
            if (Parent == null) return;
            Location = new Point(Math.Max(0, (Parent.ClientSize.Width - Width) / 2), 6);
        }

        /// <summary>Portrait sous le point donné (repère du contrôle), ou <c>null</c>.</summary>
        public TimelineEntry EntryAt(Point point) => entries.FirstOrDefault(entry => Rectangle.Inflate(entry.Bounds, 2, 2).Contains(point));

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            using (var rule = new Pen(BotUi.Gold)) graphics.DrawRectangle(rule, 0, 0, Width - 1, Height - 1);
            if (entries.Count == 0) return;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            int remaining = RemainingMilliseconds, duration = timeline?.TurnDurationMilliseconds ?? 0;
            foreach (TimelineEntry entry in entries)
            {
                Rectangle item = entry.Bounds;
                if (entry.IsCurrent)
                {
                    var pointerBounds = new Rectangle(item.X - 2, item.Y - PointerOverhang, item.Width + 4, item.Height + PointerOverhang);
                    if (pointer != null) BannerArt.Draw(graphics, pointer, pointerBounds);
                    else using (var brush = new SolidBrush(Color.FromArgb(255, 102, 0))) graphics.FillRectangle(brush, pointerBounds);
                }
                if (itemBack != null) BannerArt.Draw(graphics, itemBack, item);
                else
                {
                    using (var paper = new SolidBrush(BotUi.PaperLight)) graphics.FillRectangle(paper, item);
                    using (var edge = new Pen(BotUi.Frame)) graphics.DrawRectangle(edge, item);
                }
                DrawPortrait(graphics, entry, new Rectangle(item.X + 2, item.Y + 2, item.Width - 8, item.Height - 4));
                DrawLife(graphics, entry, item);
                if (entry.IsCurrent && duration > 0)
                {
                    // Chrono du client (_mcChrono) : rectangle vertical qui se vide avec le temps du tour, à gauche du portrait.
                    int height = (int)Math.Round((item.Height - 4) * Math.Max(0, Math.Min(1, (double)remaining / duration)));
                    using (var brush = new SolidBrush(Color.FromArgb(220, 255, 102, 0)))
                        graphics.FillRectangle(brush, item.X + 1, item.Bottom - 2 - height, 3, height);
                }
                if (entry.IsHighlighted) using (var gold = new Pen(BotUi.Gold, 2)) graphics.DrawRectangle(gold, Rectangle.Inflate(item, 1, 1));
            }
        }

        private void DrawPortrait(Graphics graphics, TimelineEntry entry, Rectangle area)
        {
            SpritePose pose = entry.Gfx > 0 ? sprites.Resolve(entry.Gfx, 1, false, "static") : null;
            SpriteSheet sheet = pose?.Sheet;
            if (sheet == null)
            {
                // Silhouette de la palette : initiale du nom, grisée pour un combattant invisible.
                using (var brush = new SolidBrush(entry.IsInvisible ? BotUi.Muted : BotUi.Ink))
                using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    graphics.DrawString(entry.Name.Length > 0 ? entry.Name.Substring(0, 1).ToUpperInvariant() : "?", BotFonts.Get(10, FontStyle.Bold), brush, area, format);
                return;
            }
            Rectangle source = sheet.Source(0);
            // Le client montre le sprite « staticR » à 80 % : ici il est ajusté au cadre blanc du portrait, proportions gardées.
            float scale = Math.Min((float)area.Width / source.Width, (float)area.Height / source.Height);
            var target = new RectangleF(area.X + (area.Width - source.Width * scale) / 2, area.Bottom - source.Height * scale, source.Width * scale, source.Height * scale);
            GraphicsState state = graphics.Save();
            try
            {
                graphics.SetClip(area);
                lock (sheet.Image)
                {
                    if (!entry.IsInvisible) graphics.DrawImage(sheet.Image, target, source, GraphicsUnit.Pixel);
                    else using (var attributes = new ImageAttributes())
                    {
                        attributes.SetColorMatrix(new ColorMatrix(new[] {
                            new[] { 0.30f, 0.30f, 0.30f, 0, 0 }, new[] { 0.59f, 0.59f, 0.59f, 0, 0 }, new[] { 0.11f, 0.11f, 0.11f, 0, 0 },
                            new[] { 0, 0, 0, 0.45f, 0f }, new[] { 0, 0, 0, 0, 1f } }));
                        var points = new[] { target.Location, new PointF(target.Right, target.Top), new PointF(target.Left, target.Bottom) };
                        graphics.DrawImage(sheet.Image, points, source, GraphicsUnit.Pixel, attributes);
                    }
                }
            }
            finally { graphics.Restore(state); }
        }

        private void DrawLife(Graphics graphics, TimelineEntry entry, Rectangle item)
        {
            double ratio = entry.LifeRatio;
            if (ratio <= 0) return;
            int team = entry.Team == 1 ? 1 : 0;
            Bitmap bar = team == 0 ? itemLife : BlueLife();
            // _mcHealth._yscale = pv / pvMax : la barre se remplit depuis le bas, teintée de TEAMS_COLOR[équipe].
            var clip = new Rectangle(item.X, item.Y + (int)Math.Round(item.Height * (1 - ratio)), item.Width, item.Height);
            GraphicsState state = graphics.Save();
            try
            {
                graphics.SetClip(clip);
                if (bar != null) BannerArt.Draw(graphics, bar, item);
                else using (var brush = new SolidBrush(TeamColors[team])) graphics.FillRectangle(brush, new Rectangle(item.Right - 6, item.Y + 3, 4, item.Height - 4));
            }
            finally { graphics.Restore(state); }
        }

        private Bitmap BlueLife()
        {
            if (lifeBlue != null || itemLife == null) return lifeBlue;
            try { lifeBlue = ClientAssets.Tinted(itemLife, 0x0000FF); }
            catch (Exception error) when (!(error is OutOfMemoryException)) { lifeBlue = null; }
            return lifeBlue;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            TimelineEntry entry = EntryAt(e.Location);
            int index = entry == null ? -1 : entries.IndexOf(entry);
            if (index == hovered) return;
            hovered = index;
            tips.SetToolTip(this, entry == null ? string.Empty : Describe(entry));
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = -1; tips.SetToolTip(this, string.Empty); }

        /// <summary>Infobulle d'un portrait : nom, points de vie, PA / PM, temps restant du tour, invisibilité.</summary>
        public string Describe(TimelineEntry entry)
        {
            if (entry == null) return string.Empty;
            var text = new System.Text.StringBuilder(entry.Name);
            if (entry.Life >= 0) text.Append(" · ").Append(entry.Life.ToString(CultureInfo.InvariantCulture)).Append(entry.MaximumLife > 0 ? " / " + entry.MaximumLife.ToString(CultureInfo.InvariantCulture) : string.Empty).Append(" PV");
            if (entry.ActionPoints >= 0) text.Append(" · ").Append(entry.ActionPoints.ToString(CultureInfo.InvariantCulture)).Append(" PA");
            if (entry.MovementPoints >= 0) text.Append(" · ").Append(entry.MovementPoints.ToString(CultureInfo.InvariantCulture)).Append(" PM");
            if (entry.IsCurrent) text.Append(" · tour en cours, ").Append(((RemainingMilliseconds + 999) / 1000).ToString(CultureInfo.InvariantCulture)).Append(" s");
            if (entry.IsInvisible) text.Append(" · invisible");
            return text.ToString();
        }

        // La ligne de temps ne capte pas le clic droit ni la molette : ils passent à la carte, seule la souris survole.
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Release();
                chrono.Dispose(); tips.Dispose(); sprites.Dispose();
                lifeBlue?.Dispose(); lifeBlue = null;
            }
            base.Dispose(disposing);
        }
    }
}
