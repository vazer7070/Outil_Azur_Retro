using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace Outil_Azur_complet.Bot.Controls
{
    /// <summary>Contenu d'une case de la barre de raccourcis.</summary>
    public enum ShortcutSlotKind { Empty, Spell, Item, CloseCombat }

    /// <summary>
    /// Case de la barre de raccourcis (<c>MouseShortcuts</c>, 25 × 25) : sort, objet utilisable ou corps à corps, avec
    /// l'icône du client, la touche (<c>SH1</c>…<c>SH14</c>) et la quantité d'un objet. Une case inutilisable est assombrie
    /// (« case inactive 50 % » du client) ; le sort choisi pour viser a un liseré doré.
    /// </summary>
    internal sealed class SpellShortcutButton : Button
    {
        public const int CellSize = 25;
        public Image Icon { get; set; }
        public string Shortcut { get; set; }
        public string Fallback { get; set; }
        public bool SelectedSpell { get; set; }
        public bool Available { get; set; }
        public bool Occupied { get; set; }
        public ShortcutSlotKind Kind { get; set; }
        /// <summary>Position dans la barre (1 à 14 ; 0 pour le corps à corps).</summary>
        public int Position { get; set; }
        /// <summary>Quantité de l'objet, affichée en bas à gauche (0 = rien).</summary>
        public int Quantity { get; set; }
        private bool hovered;

        internal SpellShortcutButton()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint, true);
            Size = new Size(CellSize, CellSize); Margin = new Padding(1, 0, 1, 0);
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            Tag = "client-icon"; Text = ""; TabStop = true;
        }

        /// <summary>Vide la case (aucun sort, objet ni icône).</summary>
        public void Clear(string accessibleName)
        {
            Kind = ShortcutSlotKind.Empty; Occupied = false; Icon = null; Fallback = null; SelectedSpell = false; Quantity = 0; Available = false;
            AccessibleName = accessibleName; Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.Clear(Color.FromArgb(67, 60, 43));
            var inner = new Rectangle(2, 2, Math.Max(1, Width - 4), Math.Max(1, Height - 4));
            Image icon = Icon;
            if (icon != null)
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                lock (icon) graphics.DrawImage(icon, inner);
            }
            else if (Occupied)
                TextRenderer.DrawText(graphics, Fallback ?? "?", Font, inner, BotUi.PaperLight,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            if (Occupied && !Available)
                using (var shade = new SolidBrush(Color.FromArgb(128, 41, 38, 31))) graphics.FillRectangle(shade, inner);
            using (var border = new Pen(SelectedSpell ? Color.FromArgb(255, 208, 82) : hovered && Occupied ? BotUi.PaperLight : BotUi.Gold, SelectedSpell ? 2 : 1))
                graphics.DrawRectangle(border, 1, 1, Width - 3, Height - 3);
            const TextFormatFlags small = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            if (Occupied && !string.IsNullOrEmpty(Shortcut))
            {
                Size size = TextRenderer.MeasureText(graphics, Shortcut, Font, new Size(Width, Height), small);
                var label = new Rectangle(Width - size.Width - 2, Height - 11, size.Width + 1, 10);
                using (var shade = new SolidBrush(Color.FromArgb(200, 41, 38, 31))) graphics.FillRectangle(shade, label);
                TextRenderer.DrawText(graphics, Shortcut, Font, label, Color.White, small);
            }
            if (Occupied && Quantity > 1)
            {
                string text = Quantity.ToString(CultureInfo.InvariantCulture);
                Size size = TextRenderer.MeasureText(graphics, text, Font, new Size(Width, Height), small);
                var label = new Rectangle(1, 1, size.Width + 1, 10);
                using (var shade = new SolidBrush(Color.FromArgb(200, 41, 38, 31))) graphics.FillRectangle(shade, label);
                TextRenderer.DrawText(graphics, text, Font, label, Color.White, small);
            }
            if (Focused) ControlPaint.DrawFocusRectangle(graphics, inner, BotUi.PaperLight, BotUi.Frame);
        }
    }
}
