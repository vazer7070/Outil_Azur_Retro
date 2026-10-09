using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Panels;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Perso;

namespace Outil_Azur_complet.Bot.Controls
{
    /// <summary>
    /// Titre porté par le personnage dans sa fiche (lot F10) : texte et couleur de <see cref="Titles"/> (<c>titles_fr</c>), lus sur
    /// la dernière entrée <c>GM</c> du personnage (<see cref="Map.Self"/>, champ type <c>classe,titre</c>). Pastille brune du client,
    /// texte dans la couleur du titre (souvent blanc) ; « Aucun titre » en gris sinon. StarLoco n'a pas de paquet de choix de titre :
    /// la pastille ne fait qu'afficher. Les événements de la carte arrivent sur le fil réseau : seul un redessin est demandé, la
    /// lecture se fait au dessin.
    /// </summary>
    public sealed class TitleBadge : Control
    {
        public const int BadgeHeight = 28;
        private Map map;

        public TitleBadge()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = BotUi.Paper; Height = BadgeHeight + 6; Name = "character-title"; AccessibleRole = AccessibleRole.StaticText;
        }

        /// <summary>Titre affiché, ou <c>null</c> sans titre (ou avant le premier <c>GM</c> du personnage).</summary>
        public PlayerTitle Current => Titles.Of(map?.Self as PlayerActor);
        /// <summary>Texte affiché dans la pastille.</summary>
        public string DisplayText
        {
            get
            {
                PlayerTitle title = Current;
                return title == null ? "Aucun titre" : title.Name;
            }
        }

        /// <summary>Suit la carte de la session (ou rien) ; à appeler depuis <c>OnBind</c> / <c>OnUnbind</c> du volet hôte.</summary>
        public void Bind(GameClass game)
        {
            Map next = game?.Map;
            if (ReferenceEquals(next, map)) return;
            if (map != null) { map.ActorUpdated -= OnActor; map.ActorRemoved -= OnActor; }
            map = next;
            if (map != null) { map.ActorUpdated += OnActor; map.ActorRemoved += OnActor; }
            Redraw();
        }

        private void OnActor(MapActor actor)
        {
            Map current = map;
            if (actor == null || current == null) return;
            MapActor self = current.Self;
            if (self == null || ReferenceEquals(actor, self) || actor.Id == self.Id) Redraw();
        }

        private void Redraw()
        {
            if (IsDisposed) return;
            if (!InvokeRequired && IsHandleCreated) { Apply(); return; }
            // Sans poignée, rien à redessiner : la création de la poignée relit le titre (OnHandleCreated).
            ExchangePanel.PostToForm(this, () => { if (!IsDisposed) Apply(); });
        }

        private void Apply() { AccessibleName = DisplayText; Invalidate(); }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Apply(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            using (var paper = new SolidBrush(BackColor)) graphics.FillRectangle(paper, ClientRectangle);
            PlayerTitle title = Current;
            string label = title == null ? "Aucun titre" : title.Name;
            var badge = new Rectangle(0, 3, Math.Max(20, Width - 1), BadgeHeight - 1);
            if (title == null)
            {
                TextRenderer.DrawText(graphics, "Titre : " + label, BotFonts.Get(8.25f, FontStyle.Italic), badge, BotUi.Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                return;
            }
            SmoothingMode smoothing = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath pill = Pill(badge, badge.Height / 2))
            using (var frame = new SolidBrush(BotUi.Frame))
            using (var rule = new Pen(BotUi.Gold))
            {
                graphics.FillPath(frame, pill);
                graphics.DrawPath(rule, pill);
            }
            graphics.SmoothingMode = smoothing;
            Color color = Color.FromArgb(255, Color.FromArgb(title.Color));
            TextRenderer.DrawText(graphics, label, BotFonts.Get(9, FontStyle.Bold), Rectangle.Inflate(badge, -badge.Height / 2, 0), color,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private static GraphicsPath Pill(Rectangle area, int radius)
        {
            var path = new GraphicsPath();
            int diameter = Math.Max(2, Math.Min(radius * 2, Math.Min(area.Width, area.Height)));
            path.AddArc(area.X, area.Y, diameter, diameter, 90, 180);
            path.AddArc(area.Right - diameter, area.Y, diameter, diameter, 270, 180);
            path.CloseFigure();
            return path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && map != null) { map.ActorUpdated -= OnActor; map.ActorRemoved -= OnActor; map = null; }
            base.Dispose(disposing);
        }
    }
}
