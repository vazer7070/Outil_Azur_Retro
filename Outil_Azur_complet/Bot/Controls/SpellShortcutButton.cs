using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Outil_Azur_complet.Bot.Controls
{
    // Fixed-size spell slots preserve the client's compact HUD at every window size.
    internal sealed class SpellShortcutButton : Button
    {
        public Image Icon { get; set; }
        public string Shortcut { get; set; }
        public string Fallback { get; set; }
        public bool SelectedSpell { get; set; }
        public bool Available { get; set; }
        public bool Occupied { get; set; }
        private bool hovered;

        internal SpellShortcutButton()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint, true);
            Size = new Size(30, 30); Margin = new Padding(2, 0, 2, 0);
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            Tag = "client-icon"; Text = ""; TabStop = true;
        }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.Clear(Color.FromArgb(67, 60, 43));
            var inner = new Rectangle(2, 2, Math.Max(1, Width - 4), Math.Max(1, Height - 4));
            if (Icon != null)
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(Icon, inner);
            }
            else if (Occupied)
                TextRenderer.DrawText(graphics, Fallback ?? "?", Font, inner, BotUi.PaperLight,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Occupied && !Available)
                using (var shade = new SolidBrush(Color.FromArgb(110, 41, 38, 31))) graphics.FillRectangle(shade, inner);
            using (var border = new Pen(SelectedSpell ? Color.FromArgb(255, 208, 82) : hovered && Occupied ? BotUi.PaperLight : BotUi.Gold, SelectedSpell ? 2 : 1))
                graphics.DrawRectangle(border, 1, 1, Width - 3, Height - 3);
            if (Occupied && !string.IsNullOrEmpty(Shortcut))
            {
                var label = new Rectangle(Width - 11, Height - 13, 10, 12);
                using (var shade = new SolidBrush(Color.FromArgb(200, 41, 38, 31))) graphics.FillRectangle(shade, label);
                TextRenderer.DrawText(graphics, Shortcut, Font, label, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            if (Focused) ControlPaint.DrawFocusRectangle(graphics, inner, BotUi.PaperLight, BotUi.Frame);
        }
    }
}
