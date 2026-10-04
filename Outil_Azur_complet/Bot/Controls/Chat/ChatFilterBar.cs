using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using Tool_BotProtocol.Game.Chat;

namespace Outil_Azur_complet.Bot.Controls.Chat
{
    /// <summary>Case de la barre du chat.</summary>
    public enum ChatBarButton
    {
        Filter,
        /// <summary>Smileys et attitudes (<c>_btnSmileys</c>, <c>ButtonEmoteUp/Down</c>).</summary>
        Smileys,
        /// <summary>S'asseoir (<c>_btnSitDown</c>, <c>ButtonSitUp/Down</c>).</summary>
        Sit,
        /// <summary>Agrandir / réduire (<c>_btnOpenClose</c>, <c>ButtonChatUp/Down</c>).</summary>
        OpenClose,
    }

    /// <summary>
    /// Barre du chat du client : les neuf boutons de filtre (<c>_btnFilter0..8</c>, infobulles <c>CHAT_TYPE0..8</c>) et les
    /// boutons smileys, s'asseoir et agrandir/réduire, dessinés avec les symboles de <c>core.swf</c> exportés par le lot D3.
    /// Trois dispositions : tout sur une ligne, filtres seuls en grille verticale (<see cref="Vertical"/>), boutons seuls
    /// (<see cref="ShowFilters"/> faux) ; le volet de discussion utilise les deux dernières dans le bandeau bas.
    /// Les icônes <c>FilterIcon0..7</c> de cet export sont celles des catégories d'objets (épée, bottes…), pas celles des
    /// filtres : chaque filtre est une pastille à la couleur de son canal, pleine quand il est actif. Un clic bascule la
    /// pastille et lève <see cref="FilterClicked"/> ; l'état réel suit ensuite les échos <c>cC±</c> du serveur.
    /// </summary>
    public sealed class ChatFilterBar : Control
    {
        public const int FilterCount = 9;
        public const int CellSize = 20;
        private const int ButtonWidth = 24;

        private static readonly string[] FilterColors =
        {
            ChatColors.Info, ChatColors.Error, ChatColors.Message, ChatColors.Whisper, ChatChannels.Guild.Color,
            ChatChannels.Alignment.Color, ChatChannels.Recruitment.Color, ChatChannels.Trade.Color, ChatChannels.Incarnam.Color,
        };
        private static readonly string[] FilterFallbacks =
        {
            "Affiche ou cache les messages d'information.", "Affiche ou cache les messages d'erreur.",
            "Affiche ou cache les messages du canal général.", "Affiche ou cache les messages privés, de groupe et d'équipe.",
            "Affiche ou cache les messages de guilde.", "Affiche ou cache les messages d'alignement et de conquête.",
            "Affiche ou cache les messages de recrutement.", "Affiche ou cache les messages de commerce.",
            "Affiche ou cache les messages du canal des débutants (Incarnam).",
        };

        private readonly bool[] filters = Enumerable.Repeat(true, FilterCount).ToArray();
        private readonly ToolTip tips = new ToolTip { InitialDelay = 400, ReshowDelay = 100, AutoPopDelay = 12000 };
        private int hover = -2;
        private int pressed = -2;
        private bool expanded, sitVisible = true, smileysOpen, vertical, showFilters = true, showButtons = true;
        private string tipShown;

        public ChatFilterBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = BotUi.Paper; Height = CellSize + 2; Cursor = Cursors.Hand;
            AccessibleName = "Filtres et boutons du chat"; AccessibleRole = AccessibleRole.ToolBar;
        }

        /// <summary>Filtre cliqué et son nouvel état.</summary>
        public event Action<int, bool> FilterClicked;
        public event EventHandler SmileysClicked;
        public event EventHandler SitClicked;
        public event EventHandler OpenCloseClicked;

        public bool IsFilterOn(int filter) => filter >= 0 && filter < FilterCount && filters[filter];

        /// <summary>Change l'état affiché d'un filtre sans lever d'événement.</summary>
        public void SetFilter(int filter, bool on)
        {
            if (filter < 0 || filter >= FilterCount || filters[filter] == on) return;
            filters[filter] = on; Invalidate();
        }

        public bool Expanded { get { return expanded; } set { if (expanded == value) return; expanded = value; Invalidate(); } }
        public bool SitVisible { get { return sitVisible; } set { if (sitVisible == value) return; sitVisible = value; Invalidate(); } }
        public bool SmileysOpen { get { return smileysOpen; } set { if (smileysOpen == value) return; smileysOpen = value; Invalidate(); } }

        /// <summary>Filtres en grille de deux colonnes le long du texte (bandeau étroit) plutôt qu'en ligne.</summary>
        public bool Vertical { get { return vertical; } set { if (vertical == value) return; vertical = value; Invalidate(); } }
        /// <summary>Faux : la barre ne montre que les boutons (smileys, s'asseoir, agrandir), alignés à gauche.</summary>
        public bool ShowFilters { get { return showFilters; } set { if (showFilters == value) return; showFilters = value; Invalidate(); } }
        /// <summary>Faux : la barre ne montre que les filtres.</summary>
        public bool ShowButtons { get { return showButtons; } set { if (showButtons == value) return; showButtons = value; Invalidate(); } }

        /// <summary>Largeur d'une barre de filtres verticale.</summary>
        public const int VerticalWidth = 2 * VerticalCell + 3;
        /// <summary>Largeur d'une barre de boutons seule.</summary>
        public const int ButtonsWidth = 3 * (ButtonWidth + 1);
        private const int VerticalCell = 16;

        public Rectangle FilterBounds(int filter)
        {
            if (!showFilters || filter < 0 || filter >= FilterCount) return Rectangle.Empty;
            if (!vertical) return new Rectangle(1 + filter * CellSize, 1, CellSize, CellSize);
            int rowsCount = (FilterCount + 1) / 2;
            int height = Math.Max(9, Math.Min(CellSize, (ClientSize.Height - 2) / rowsCount));
            return new Rectangle(1 + filter % 2 * VerticalCell, 1 + filter / 2 * height, VerticalCell, height);
        }

        public Rectangle ButtonBounds(ChatBarButton button)
        {
            if (!showButtons) return Rectangle.Empty;
            if (!showFilters)
            {
                int height = Math.Max(10, Math.Min(24, ClientSize.Height - 2)), top = Math.Max(0, (ClientSize.Height - height) / 2), x = 0;
                foreach (ChatBarButton candidate in new[] { ChatBarButton.Smileys, ChatBarButton.Sit, ChatBarButton.OpenClose })
                {
                    if (candidate == ChatBarButton.Sit && !sitVisible) { if (button == candidate) return Rectangle.Empty; continue; }
                    if (candidate == button) return new Rectangle(x, top, ButtonWidth, height);
                    x += ButtonWidth + 1;
                }
                return Rectangle.Empty;
            }
            if (vertical) return Rectangle.Empty; // grille verticale : les boutons vont dans une barre à part
            int right = ClientSize.Width - 1;
            switch (button)
            {
                case ChatBarButton.OpenClose: return new Rectangle(right - ButtonWidth, 1, ButtonWidth, CellSize);
                case ChatBarButton.Sit: return sitVisible ? new Rectangle(right - 2 * ButtonWidth - 2, 1, ButtonWidth, CellSize) : Rectangle.Empty;
                case ChatBarButton.Smileys: return new Rectangle(right - (sitVisible ? 3 : 2) * ButtonWidth - (sitVisible ? 4 : 2), 1, ButtonWidth, CellSize);
                default: return Rectangle.Empty;
            }
        }

        /// <summary>Traite un clic à cette position (aussi utilisé par les tests) ; vrai si une case a répondu.</summary>
        public bool ClickAt(Point point)
        {
            int cell = CellAt(point);
            if (cell == -2) return false;
            try
            {
                if (cell >= 0)
                {
                    filters[cell] = !filters[cell]; Invalidate();
                    FilterClicked?.Invoke(cell, filters[cell]);
                }
                else if (cell == -10) SmileysClicked?.Invoke(this, EventArgs.Empty);
                else if (cell == -11) SitClicked?.Invoke(this, EventArgs.Empty);
                else if (cell == -12) OpenCloseClicked?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception error) when (!(error is OutOfMemoryException)) { System.Diagnostics.Trace.WriteLine("Chat : " + error.Message); }
            return true;
        }

        /// <summary>Texte de l'infobulle d'une case (filtre, bouton) ou null.</summary>
        public string TooltipAt(Point point)
        {
            int cell = CellAt(point);
            if (cell >= 0) return ChatUiText.Get("CHAT_TYPE" + cell, FilterFallbacks[cell]);
            switch (cell)
            {
                case -10: return ChatUiText.Get("CHAT_SHOW_SMILEYS", "Smileys et attitudes");
                case -11: return ChatUiText.Get("SITDOWN_TOOLTIP", "S'asseoir pour regagner des points de vie plus vite.");
                case -12: return ChatUiText.Get("CHAT_SHOW_MORE", "Agrandir ou réduire la fenêtre de discussion");
                default: return null;
            }
        }

        private int CellAt(Point point)
        {
            for (int filter = 0; filter < FilterCount; filter++) if (FilterBounds(filter).Contains(point)) return filter;
            if (ButtonBounds(ChatBarButton.Smileys).Contains(point)) return -10;
            if (ButtonBounds(ChatBarButton.Sit).Contains(point)) return -11;
            if (ButtonBounds(ChatBarButton.OpenClose).Contains(point)) return -12;
            return -2;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.Clear(BackColor);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (showFilters) for (int filter = 0; filter < FilterCount; filter++) PaintFilter(graphics, filter);
            Rectangle smileys = ButtonBounds(ChatBarButton.Smileys);
            if (showButtons && (!showFilters || vertical || smileys.X > FilterBounds(FilterCount - 1).Right + 2))
            {
                PaintButton(graphics, ChatBarButton.Smileys, smileysOpen ? "ButtonEmoteDown" : "ButtonEmoteUp", "☺", -10, smileysOpen);
                if (sitVisible) PaintButton(graphics, ChatBarButton.Sit, "ButtonSitUp", "S", -11, false);
                PaintButton(graphics, ChatBarButton.OpenClose, expanded ? "ButtonChatDown" : "ButtonChatUp", expanded ? "−" : "+", -12, false);
            }
            if (showFilters)
                using (var line = new Pen(BotUi.Gold))
                    if (vertical) graphics.DrawLine(line, ClientSize.Width - 1, 0, ClientSize.Width - 1, ClientSize.Height);
                    else graphics.DrawLine(line, 0, ClientSize.Height - 1, ClientSize.Width, ClientSize.Height - 1);
        }

        private void PaintFilter(Graphics graphics, int filter)
        {
            Rectangle cell = FilterBounds(filter);
            Color color = ChatLineBuilder.ParseColor(FilterColors[filter], BotUi.Ink);
            int size = Math.Max(5, Math.Min(cell.Width, cell.Height) - (vertical ? 4 : 10));
            var dot = new Rectangle(cell.X + (cell.Width - size) / 2, cell.Y + (cell.Height - size) / 2, size, size);
            if (hover == filter)
                using (var glow = new SolidBrush(Color.FromArgb(70, BotUi.Gold))) graphics.FillEllipse(glow, Rectangle.Inflate(dot, 2, 2));
            if (pressed == filter) dot.Offset(0, 1);
            if (filters[filter])
            {
                using (var fill = new LinearGradientBrush(dot, ControlPaint.Light(color, 0.6f), color, 90f)) graphics.FillEllipse(fill, dot);
                using (var edge = new Pen(ControlPaint.Dark(color, 0.1f))) graphics.DrawEllipse(edge, dot);
                using (var shine = new SolidBrush(Color.FromArgb(120, Color.White))) graphics.FillEllipse(shine, dot.X + 2, dot.Y + 1, dot.Width / 2, dot.Height / 3);
            }
            else
            {
                using (var fill = new SolidBrush(BotUi.PaperLight)) graphics.FillEllipse(fill, dot);
                using (var edge = new Pen(Color.FromArgb(150, color), 1.5f)) graphics.DrawEllipse(edge, dot);
            }
        }

        private void PaintButton(Graphics graphics, ChatBarButton button, string asset, string fallback, int id, bool active)
        {
            Rectangle bounds = ButtonBounds(button);
            if (bounds.IsEmpty) return;
            Bitmap image = ChatImages.Get("Client", asset, this);
            var target = new RectangleF(bounds.X + 1, bounds.Y + 2, bounds.Width - 2, bounds.Height - 4);
            if (pressed == id) target.Offset(0, 1);
            if (image != null) ChatImages.Draw(graphics, image, target);
            else
            {
                using (var fill = new SolidBrush(active ? BotUi.Gold : BotUi.PaperLight)) graphics.FillRectangle(fill, Rectangle.Round(target));
                using (var edge = new Pen(BotUi.Gold)) graphics.DrawRectangle(edge, Rectangle.Round(target));
                using (var brush = new SolidBrush(BotUi.Ink))
                using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    graphics.DrawString(fallback, BotFonts.Get(8.25f, FontStyle.Bold), brush, target, format);
            }
            if (hover == id)
                using (var veil = new SolidBrush(Color.FromArgb(45, Color.White))) graphics.FillRectangle(veil, target);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int cell = CellAt(e.Location);
            if (cell != hover) { hover = cell; Invalidate(); }
            string tip = TooltipAt(e.Location);
            if (tip != tipShown) { tipShown = tip; tips.SetToolTip(this, tip ?? string.Empty); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = -2; pressed = -2; Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) { pressed = CellAt(e.Location); Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            bool click = e.Button == MouseButtons.Left && pressed != -2 && pressed == CellAt(e.Location);
            pressed = -2; Invalidate();
            if (click) ClickAt(e.Location);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                tips.Dispose();
                FilterClicked = null; SmileysClicked = null; SitClicked = null; OpenCloseClicked = null;
            }
            base.Dispose(disposing);
        }
    }
}
