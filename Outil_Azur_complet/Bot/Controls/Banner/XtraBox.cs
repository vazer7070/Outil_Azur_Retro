using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace Outil_Azur_complet.Bot.Controls.Banner
{
    /// <summary>
    /// Illustration ronde du bandeau (<c>_mcXtra</c>) selon l'option <c>BannerIllustrationMode</c> : portrait de la classe
    /// (<c>Artworks/Faces/&lt;gfx&gt;</c>), horloge (heure du serveur reçue par <c>BT</c>) ou boussole (cible de <c>IC</c> ou d'un
    /// lien <c>[x,y]</c> du chat). Les modes « Boune » (<c>helper</c>) et « Mini carte » (<c>map</c>) du client ne sont pas
    /// livrés : ils affichent le portrait. Comme <c>GameManager.updateCompass</c>, une cible passe en boussole et son
    /// effacement revient au portrait.
    /// </summary>
    public sealed class XtraBox : Control
    {
        public static readonly string[] Modes = { "artwork", "clock", "compass", "helper", "map" };
        private Bitmap artwork, clockBack, hours, minutes, compassBack, compassArrow, compassNoArrow;
        private int artworkGfx = -1;
        private string mode = "artwork", displayed = "artwork";
        private Point? target, current;
        private DateTime? time;

        public XtraBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = BotUi.FrameLight; Cursor = Cursors.Hand; Size = new Size(72, 72);
            AccessibleName = "Illustration du bandeau"; AccessibleRole = AccessibleRole.PushButton;
            BannerArt.Request(this, "UI_BannerClockBack", image => { clockBack = image; Invalidate(); });
            BannerArt.Request(this, "UI_BannerClockArrowHours", image => { hours = image; Invalidate(); });
            BannerArt.Request(this, "UI_BannerClockArrowMinutes", image => { minutes = image; Invalidate(); });
            BannerArt.Request(this, "UI_BannerCompassBack", image => { compassBack = image; Invalidate(); });
            BannerArt.Request(this, "UI_BannerCompassArrow", image => { compassArrow = image; Invalidate(); });
            BannerArt.Request(this, "UI_BannerCompassNoArrow", image => { compassNoArrow = image; Invalidate(); });
        }

        /// <summary>Mode choisi (option <c>BannerIllustrationMode</c>) ; le changer l'affiche aussitôt.</summary>
        public string Mode
        {
            get => mode;
            set { mode = Array.IndexOf(Modes, value) >= 0 ? value : "artwork"; displayed = mode; Invalidate(); }
        }
        /// <summary>Mode réellement dessiné : la boussole quand une cible arrive, le portrait pour les modes non livrés.</summary>
        public string DisplayedMode => displayed == "helper" || displayed == "map" ? "artwork" : displayed;
        public Point? CompassTarget => target;
        public Point? CurrentCoordinates => current;
        public DateTime? Time => time;

        /// <summary>Angle de la flèche (degrés, sens horaire depuis l'est) ; <c>null</c> sans cible ou sur la carte visée.</summary>
        public double? CompassAngle
        {
            get
            {
                if (target == null || current == null) return null;
                int dx = target.Value.X - current.Value.X, dy = target.Value.Y - current.Value.Y;
                if (dx == 0 && dy == 0) return null;
                return Math.Atan2(dy, dx) * 180 / Math.PI;
            }
        }
        /// <summary>Aiguille des heures, comme l'horloge du client : 30 × h + 6 × m / 12 − 90.</summary>
        public double HoursAngle => time == null ? -90 : 30 * (time.Value.Hour % 12) + 6.0 * time.Value.Minute / 12 - 90;
        /// <summary>Aiguille des minutes : 6 × m − 90.</summary>
        public double MinutesAngle => time == null ? -90 : 6 * time.Value.Minute - 90;

        /// <summary>Cible de la boussole (<c>IC&lt;x&gt;|&lt;y&gt;</c>, lien du chat) ; <c>null</c> l'efface et revient au portrait.</summary>
        public void SetCompassTarget(Point? value)
        {
            target = value;
            if (mode != "map") displayed = value.HasValue ? "compass" : "artwork";
            Invalidate();
        }

        public void SetCurrentCoordinates(Point? value) { if (current == value) return; current = value; Invalidate(); }

        /// <summary>Heure du serveur (UTC de la référence <c>BT</c> plus le temps écoulé) ; l'horloge est redessinée si la minute change.</summary>
        public void SetTime(DateTime? value)
        {
            DateTime? minute = value.HasValue ? new DateTime(value.Value.Year, value.Value.Month, value.Value.Day, value.Value.Hour, value.Value.Minute, 0) : (DateTime?)null;
            if (minute == time) return;
            time = minute; if (DisplayedMode == "clock") Invalidate();
        }

        /// <summary>Portrait de la classe : buste <c>Artworks/Faces/&lt;gfx&gt;</c> (gfx = classe × 10 + sexe).</summary>
        public void SetArtwork(int gfx)
        {
            if (gfx == artworkGfx) return;
            artworkGfx = gfx; artwork = null; Invalidate();
            if (gfx <= 0) return;
            BannerArt.Request(this, "Artworks", "Faces/" + gfx.ToString(CultureInfo.InvariantCulture), image => { if (artworkGfx == gfx) { artwork = image; Invalidate(); } });
        }

        /// <summary>Texte de l'infobulle : heure, coordonnées de la cible ou invitation à poser un drapeau, comme le client.</summary>
        public string Tooltip
        {
            get
            {
                switch (DisplayedMode)
                {
                    case "clock":
                        return time == null ? BannerArt.Text("BANNER_CLOCK", "Horloge") + " : heure du serveur pas encore reçue (BT)"
                            : time.Value.ToString("HH:mm", CultureInfo.InvariantCulture);
                    case "compass":
                        return target == null ? BannerArt.Text("BANNER_SET_FLAG", "Place un drapeau sur la carte du monde.")
                            : target.Value.X.ToString(CultureInfo.InvariantCulture) + ", " + target.Value.Y.ToString(CultureInfo.InvariantCulture);
                    default:
                        return BannerArt.Text("BANNER_ARTWORK", "Portrait") + " · clic : choisir l'illustration et la jauge";
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            int side = Math.Min(Width, Height);
            if (side < 4) return;
            var circle = new RectangleF((Width - side) / 2f, (Height - side) / 2f, side - 1, side - 1);
            using (var clip = new GraphicsPath())
            {
                clip.AddEllipse(circle);
                GraphicsState state = graphics.Save();
                graphics.SetClip(clip);
                switch (DisplayedMode)
                {
                    case "clock": PaintClock(graphics, circle); break;
                    case "compass": PaintCompass(graphics, circle); break;
                    default: PaintArtwork(graphics, circle); break;
                }
                graphics.Restore(state);
            }
            using (var rule = new Pen(BotUi.Gold)) graphics.DrawEllipse(rule, circle);
        }

        private void PaintArtwork(Graphics graphics, RectangleF circle)
        {
            using (var paper = new SolidBrush(BotUi.Paper)) graphics.FillEllipse(paper, circle);
            if (artwork == null) return;
            int width, height;
            lock (artwork) { width = artwork.Width; height = artwork.Height; }
            // Buste cadré par le haut, comme le masque rond du client.
            float scale = Math.Max(circle.Width / width, circle.Height * 1.15f / height);
            var box = new RectangleF(circle.X + (circle.Width - width * scale) / 2f, circle.Y + circle.Height * 0.05f, width * scale, height * scale);
            BannerArt.Draw(graphics, artwork, box);
        }

        private void PaintClock(Graphics graphics, RectangleF circle)
        {
            if (clockBack != null) BannerArt.Draw(graphics, clockBack, circle);
            else using (var face = new SolidBrush(Color.White)) graphics.FillEllipse(face, circle);
            if (time == null) return;
            if (hours != null) BannerArt.DrawRotated(graphics, hours, circle, HoursAngle);
            else DrawHand(graphics, circle, HoursAngle, 0.28f, 2.5f);
            if (minutes != null) BannerArt.DrawRotated(graphics, minutes, circle, MinutesAngle);
            else DrawHand(graphics, circle, MinutesAngle, 0.4f, 1.5f);
        }

        private void PaintCompass(Graphics graphics, RectangleF circle)
        {
            if (compassBack != null) BannerArt.Draw(graphics, compassBack, circle);
            else using (var back = new SolidBrush(Color.FromArgb(33, 43, 60))) graphics.FillEllipse(back, circle);
            double? angle = CompassAngle;
            if (angle == null)
            {
                if (compassNoArrow != null) BannerArt.Draw(graphics, compassNoArrow, circle);
                return;
            }
            if (compassArrow != null) BannerArt.DrawRotated(graphics, compassArrow, circle, angle.Value);
            else DrawHand(graphics, circle, angle.Value, 0.42f, 3f, Color.Gold);
        }

        private static void DrawHand(Graphics graphics, RectangleF circle, double degrees, float length, float width, Color? color = null)
        {
            float cx = circle.X + circle.Width / 2f, cy = circle.Y + circle.Height / 2f;
            double radians = degrees * Math.PI / 180;
            using (var pen = new Pen(color ?? BotUi.Frame, width) { EndCap = LineCap.Round, StartCap = LineCap.Round })
                graphics.DrawLine(pen, cx, cy, cx + (float)(Math.Cos(radians) * circle.Width * length), cy + (float)(Math.Sin(radians) * circle.Height * length));
        }
    }
}
