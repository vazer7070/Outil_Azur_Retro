using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Windows.Forms;

namespace Outil_Azur_complet.Bot.Controls
{
    /// <summary>Contenu d'une case de la barre de raccourcis.</summary>
    public enum ShortcutSlotKind { Empty, Spell, Item, CloseCombat }

    /// <summary>
    /// Case de la barre de raccourcis (<c>MouseShortcuts</c>, 25 × 25) : sort, objet utilisable ou corps à corps, avec
    /// l'icône du client, la touche (<c>SH1</c>…<c>SH14</c>) et la quantité d'un objet. Une case inutilisable est assombrie
    /// (« case inactive 50 % » du client) ; le sort choisi pour viser a un liseré doré. Lot X12 : l'icône d'une case inutilisable
    /// est teinte comme <c>MouseShortcuts</c> (<c>INACTIVE_TRANSFORM</c> : rouge, vert et bleu à 50 %, <c>WRONG_STATE_TRANSFORM</c> :
    /// bleu à 70 % pour un état requis ou interdit) ; <see cref="Badge"/> affiche les tours d'attente d'un sort (<c>showLabel</c>).
    /// </summary>
    internal sealed class SpellShortcutButton : Button
    {
        public const int CellSize = 25;
        public Image Icon { get; set; }
        public string Shortcut { get; set; }
        public string Fallback { get; set; }
        public bool SelectedSpell { get; set; }
        public bool Available { get; set; }
        /// <summary>Indisponible à cause d'un état requis ou interdit : teinte <c>WRONG_STATE_TRANSFORM</c>.</summary>
        public bool WrongState { get; set; }
        /// <summary>Texte du coin supérieur gauche d'un sort (tours d'attente), <c>null</c> sinon.</summary>
        public string Badge { get; set; }
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
            WrongState = false; Badge = null;
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
            bool dimmed = Occupied && !Available;
            if (icon != null)
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                if (!dimmed) lock (icon) graphics.DrawImage(icon, inner);
                else
                    // Transformation de couleur du client (multiplicateurs ra / ga / ba, décalages nuls) plutôt qu'un voile.
                    using (var attributes = new ImageAttributes())
                    {
                        attributes.SetColorMatrix(new ColorMatrix { Matrix00 = 0.5f, Matrix11 = 0.5f, Matrix22 = WrongState ? 0.7f : 0.5f });
                        lock (icon) graphics.DrawImage(icon, inner, 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, attributes);
                    }
            }
            else if (Occupied)
                // Au-dessus de l'étiquette de la touche (en bas), qui recouvrait sinon le texte de repli (« CàC »).
                TextRenderer.DrawText(graphics, Fallback ?? "?", Font, new Rectangle(inner.X, inner.Y, inner.Width, Math.Max(1, inner.Height - 8)), BotUi.PaperLight,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            if (dimmed && icon == null)
                using (var shade = new SolidBrush(Color.FromArgb(128, 41, 38, 31))) graphics.FillRectangle(shade, inner);
            using (var border = new Pen(SelectedSpell ? Color.FromArgb(255, 208, 82) : hovered && Occupied ? BotUi.PaperLight : BotUi.Gold, SelectedSpell ? 2 : 1))
                graphics.DrawRectangle(border, 1, 1, Width - 3, Height - 3);
            const TextFormatFlags small = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            if (Occupied && !string.IsNullOrEmpty(Shortcut))
            {
                // Mesure sans NoPadding : avec, les glyphes AZERTY (« é », « " », « ^& ») dépassaient du cadre et étaient coupés.
                Size size = TextRenderer.MeasureText(graphics, Shortcut, Font, new Size(Width, Height), TextFormatFlags.SingleLine);
                int width = Math.Min(Width - 2, size.Width), height = Math.Min(Height - 2, Math.Max(10, size.Height));
                var label = new Rectangle(Width - width - 1, Height - height - 1, width, height);
                using (var shade = new SolidBrush(Color.FromArgb(200, 41, 38, 31))) graphics.FillRectangle(shade, label);
                TextRenderer.DrawText(graphics, Shortcut, Font, label, Color.White, small);
            }
            if (Occupied && (Quantity > 1 || !string.IsNullOrEmpty(Badge)))
            {
                string text = !string.IsNullOrEmpty(Badge) ? Badge : Quantity.ToString(CultureInfo.InvariantCulture);
                Size size = TextRenderer.MeasureText(graphics, text, Font, new Size(Width, Height), small);
                var label = new Rectangle(1, 1, size.Width + 1, 10);
                using (var shade = new SolidBrush(Color.FromArgb(200, 41, 38, 31))) graphics.FillRectangle(shade, label);
                TextRenderer.DrawText(graphics, text, Font, label, Color.White, small);
            }
            if (Focused) ControlPaint.DrawFocusRectangle(graphics, inner, BotUi.PaperLight, BotUi.Frame);
        }
    }
}
