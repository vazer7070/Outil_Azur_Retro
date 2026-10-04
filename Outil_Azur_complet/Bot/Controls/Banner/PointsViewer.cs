using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace Outil_Azur_complet.Bot.Controls.Banner
{
    /// <summary>Points d'action ou de mouvement du bandeau (<c>_pvAP</c> / <c>_pvMP</c>, symboles <c>PointsViewerAP/MP</c> 33 × 35).</summary>
    public enum PointsKind { Action, Movement }

    /// <summary>
    /// Pastille PA ou PM : image du client et valeur au centre. Le client ne l'affiche qu'en combat (<c>showPoints</c>) ;
    /// le bot la garde aussi hors combat avec les totaux du personnage (<c>As</c>), en combat les points du tour (<c>GA;129</c>/<c>GTM</c>).
    /// </summary>
    public sealed class PointsViewer : Control
    {
        private Bitmap image;
        private int value = -1;

        public PointsViewer(PointsKind kind)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Kind = kind; Size = new Size(33, 35); BackColor = BotUi.FrameLight; Font = BotFonts.Get(9, FontStyle.Bold);
            AccessibleName = kind == PointsKind.Action ? BannerArt.Text("ACTIONPOINTS", "Points d'actions") : BannerArt.Text("MOVEPOINTS", "Points de mouvement");
            BannerArt.Request(this, kind == PointsKind.Action ? "PointsViewerAP" : "PointsViewerMP", loaded => { image = loaded; Invalidate(); });
        }

        public PointsKind Kind { get; }
        /// <summary>Valeur affichée ; négative = inconnue (« ? »).</summary>
        public int Value
        {
            get => value;
            set { if (this.value == value) return; this.value = value; AccessibleDescription = Text = value < 0 ? "?" : value.ToString(CultureInfo.InvariantCulture); Invalidate(); }
        }
        public bool HasClientImage => image != null;

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            float scale = Math.Min(Width / 68f, Height / 70f);
            var box = new RectangleF((Width - 68 * scale) / 2f, (Height - 70 * scale) / 2f, 68 * scale, 70 * scale);
            if (image != null) BannerArt.Draw(graphics, image, box);
            else
            {
                var disc = new RectangleF(box.X + 3, box.Y + 4, box.Width - 6, box.Height - 7);
                using (var fill = new SolidBrush(Kind == PointsKind.Action ? Color.FromArgb(52, 92, 168) : Color.FromArgb(70, 128, 52))) graphics.FillEllipse(fill, disc);
                using (var border = new Pen(BotUi.Gold, 1.5f)) graphics.DrawEllipse(border, disc);
            }
            BannerArt.OutlinedText(graphics, value < 0 ? "?" : value.ToString(CultureInfo.InvariantCulture), Font, ClientRectangle, Color.White);
        }
    }
}
