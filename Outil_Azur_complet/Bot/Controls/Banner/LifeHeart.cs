using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace Outil_Azur_complet.Bot.Controls.Banner
{
    /// <summary>
    /// Cœur des points de vie du bandeau (<c>_hHeart</c>, symbole <c>Heart</c> 44 × 40) : <c>Heart_vide</c>, puis la partie
    /// basse de <c>Heart</c> sur une hauteur proportionnelle à PV / PV max (rectangle ancré en y = 18, 36 de haut dans le
    /// PNG d'origine (-22, -20) à l'échelle 2), la valeur au centre. Les deux PNG sont lus hors du thread de l'interface.
    /// </summary>
    public sealed class LifeHeart : Control
    {
        private Bitmap full, empty;
        private int life, maximum;

        public LifeHeart()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
            Size = new Size(50, 46); BackColor = BotUi.FrameLight; Font = BotFonts.Get(8, FontStyle.Bold);
            AccessibleName = "Points de vie"; AccessibleRole = AccessibleRole.ProgressBar;
            BannerArt.Request(this, "Heart", image => { full = image; Invalidate(); });
            BannerArt.Request(this, "Heart_vide", image => { empty = image; Invalidate(); });
        }

        public int Life => life;
        public int MaximumLife => maximum;
        /// <summary>PV / PV max entre 0 et 1 (0 si le maximum est inconnu).</summary>
        public double Ratio => maximum <= 0 ? 0 : Math.Max(0, Math.Min(1, (double)life / maximum));
        public int Percent => (int)Math.Round(Ratio * 100);
        /// <summary>Vrai quand les deux images du client sont chargées (sinon un cœur dessiné les remplace).</summary>
        public bool HasClientImages => full != null && empty != null;

        public void SetLife(int value, int max)
        {
            value = Math.Max(0, value); max = Math.Max(0, max);
            if (value == life && max == maximum) return;
            life = value; maximum = max;
            AccessibleDescription = life.ToString(CultureInfo.InvariantCulture) + " / " + maximum.ToString(CultureInfo.InvariantCulture);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            // Proportions du PNG (88 × 80) gardées dans le contrôle.
            float scale = Math.Min(Width / 88f, Height / 80f);
            var box = new RectangleF((Width - 88 * scale) / 2f, (Height - 80 * scale) / 2f, 88 * scale, 80 * scale);
            double ratio = Ratio;
            if (HasClientImages)
            {
                BannerArt.Draw(graphics, empty, box);
                float bottom = box.Y + 76 * scale, top = bottom - (float)ratio * 72 * scale;
                if (bottom > top)
                {
                    GraphicsState state = graphics.Save();
                    graphics.SetClip(new RectangleF(box.X, top, box.Width, bottom - top));
                    BannerArt.Draw(graphics, full, box);
                    graphics.Restore(state);
                }
            }
            else
            {
                using (GraphicsPath heart = HeartPath(Rectangle.Round(box)))
                {
                    using (var back = new SolidBrush(Color.FromArgb(235, 222, 120))) graphics.FillPath(back, heart);
                    GraphicsState state = graphics.Save();
                    graphics.SetClip(new RectangleF(box.X, box.Bottom - (float)ratio * box.Height, box.Width, (float)ratio * box.Height));
                    using (var red = new SolidBrush(Color.FromArgb(200, 20, 20))) graphics.FillPath(red, heart);
                    graphics.Restore(state);
                    using (var border = new Pen(Color.FromArgb(41, 38, 31), 1.5f)) graphics.DrawPath(border, heart);
                }
            }
            BannerArt.OutlinedText(graphics, maximum > 0 ? life.ToString(CultureInfo.InvariantCulture) : "?", Font,
                new Rectangle(0, (int)(box.Y + box.Height * 0.18f), Width, (int)(box.Height * 0.5f)), Color.White);
        }

        private static GraphicsPath HeartPath(Rectangle box)
        {
            var path = new GraphicsPath();
            float x = box.X, y = box.Y, w = Math.Max(4, box.Width), h = Math.Max(4, box.Height);
            path.AddBezier(x + w / 2, y + h * 0.25f, x + w * 0.1f, y - h * 0.1f, x - w * 0.05f, y + h * 0.45f, x + w / 2, y + h * 0.95f);
            path.AddBezier(x + w / 2, y + h * 0.95f, x + w * 1.05f, y + h * 0.45f, x + w * 0.9f, y - h * 0.1f, x + w / 2, y + h * 0.25f);
            path.CloseFigure();
            return path;
        }
    }
}
