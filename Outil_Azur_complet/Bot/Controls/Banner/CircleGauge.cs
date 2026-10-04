using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Tool_BotProtocol.Game.Perso;

namespace Outil_Azur_complet.Bot.Controls.Banner
{
    /// <summary>
    /// Jauge ronde autour de l'illustration (<c>_ccChrono</c>, <c>CircleChrono</c>) : secteur de 3,6° par point de pourcentage,
    /// dans le sens des aiguilles d'une montre depuis midi (<c>chronoUpdate</c> : angle = 360 × (1 − reste / max)).
    /// Hors combat elle suit l'option <c>BannerGaugeMode</c> (<c>showGaugeMode</c>) ; en combat elle devient le chrono du tour.
    /// </summary>
    public sealed class CircleGauge : Panel
    {
        public const int Thickness = 6;
        public static readonly Color XpColor = BannerArt.Rgb(0x7EA0A4);
        public static readonly Color JobColor = BannerArt.Rgb(0x9F5025);
        public static readonly Color PodsColor = BannerArt.Rgb(0x60BE34);
        public static readonly Color EnergyColor = BannerArt.Rgb(0xA7C31D);
        /// <summary>Couleur pleine que le client pose en combat (<c>setGaugeChrono(100, 2109246)</c>).</summary>
        public static readonly Color ChronoColor = BannerArt.Rgb(2109246);
        private double value;
        private Color gaugeColor = XpColor;
        private bool ringVisible = true;

        public CircleGauge()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = BotUi.FrameLight; Padding = new Padding(Thickness + 1); Size = new Size(86, 86);
            AccessibleName = "Jauge du bandeau"; AccessibleRole = AccessibleRole.ProgressBar;
        }

        /// <summary>Pourcentage affiché, borné à 0..100 comme <c>setGaugeChrono</c>.</summary>
        public double Value
        {
            get => value;
            set
            {
                double bounded = double.IsNaN(value) ? 0 : Math.Max(0, Math.Min(100, value));
                if (Math.Abs(bounded - this.value) < 0.001) return;
                this.value = bounded; AccessibleDescription = ((int)Math.Round(bounded)).ToString(CultureInfo.InvariantCulture) + " %"; Invalidate();
            }
        }
        public Color GaugeColor { get => gaugeColor; set { if (gaugeColor == value) return; gaugeColor = value; Invalidate(); } }
        /// <summary>Faux en mode <c>none</c> (« Désactiver ») : l'anneau reste vide.</summary>
        public bool RingVisible { get => ringVisible; set { if (ringVisible == value) return; ringVisible = value; Invalidate(); } }
        /// <summary>Mode affiché : <c>none</c>, <c>xp</c>, <c>xpcurrentjob</c>, <c>xpmount</c>, <c>pods</c>, <c>energy</c> ou <c>chrono</c>.</summary>
        public string Mode { get; private set; } = "xp";

        /// <summary>Applique un mode de l'option <c>BannerGaugeMode</c> au personnage ; renvoie l'infobulle de la jauge.</summary>
        public string Show(string mode, CharacterClass character)
        {
            Mode = mode ?? "xp";
            string tip = Compute(Mode, character, out double percent, out Color color);
            RingVisible = Mode != "none";
            GaugeColor = color; Value = percent;
            return tip;
        }

        /// <summary>Chrono du tour : part écoulée du temps du tour (<c>startTimer</c>).</summary>
        public void ShowChrono(int remainingMilliseconds, int durationMilliseconds)
        {
            Mode = "chrono"; RingVisible = true; GaugeColor = ChronoColor;
            Value = durationMilliseconds <= 0 ? 100 : 100.0 * (1 - Math.Max(0, Math.Min(durationMilliseconds, remainingMilliseconds)) / (double)durationMilliseconds);
        }

        /// <summary>Valeur et couleur d'un mode (<c>showGaugeMode</c>) ; 0 quand la donnée manque, comme le client.</summary>
        public static string Compute(string mode, CharacterClass character, out double percent, out Color color)
        {
            percent = 0; color = XpColor;
            var stats = character?.stats;
            switch (mode)
            {
                case "none":
                    return BannerArt.Text("DISABLE", "Désactiver");
                case "xpcurrentjob":
                {
                    color = JobColor;
                    var job = CurrentJob(character);
                    if (job == null) return BannerArt.Text("WORD_XP", "XP") + " " + BannerArt.Text("JOB", "Métier") + " : aucun outil de métier équipé";
                    percent = job.GetXpPercentage;
                    return BannerArt.Text("WORD_XP", "XP") + " " + (job.name ?? BannerArt.Text("JOB", "Métier")) + " : " + Percent(percent);
                }
                case "xpmount":
                    return BannerArt.Text("WORD_XP", "XP") + " " + BannerArt.Text("MOUNT", "Monture") + " : aucune monture connue du bot";
                case "pods":
                {
                    color = PodsColor;
                    var inventory = character?.Inventory;
                    if (inventory == null || inventory.Pods_Max <= 0) return BannerArt.Text("WEIGHT", "Pods") + " : inconnus";
                    percent = 100.0 * inventory.Actual_pods / inventory.Pods_Max;
                    return BannerArt.Text("PLAYER_WEIGHT", "%1 pods sur %2", BannerArt.Thousands(inventory.Actual_pods), BannerArt.Thousands(inventory.Pods_Max));
                }
                case "energy":
                {
                    color = EnergyColor;
                    if (stats == null || stats.EnergyMax <= 0) return BannerArt.Text("ENERGY", "Energie") + " : inconnue";
                    percent = 100.0 * stats.ActualEnergy / stats.EnergyMax;
                    return BannerArt.Text("ENERGY", "Energie") + " : " + BannerArt.Thousands(stats.ActualEnergy) + " / " + BannerArt.Thousands(stats.EnergyMax);
                }
                default:
                {
                    if (stats == null) return BannerArt.Text("WORD_XP", "XP");
                    double span = stats.ExpNivNext - stats.MinExpNiv;
                    percent = span > 0 ? (stats.ActualEXP - stats.MinExpNiv) / span * 100 : 0;
                    return BannerArt.Text("WORD_XP", "XP") + " : " + BannerArt.Thousands((long)stats.ActualEXP) + " / " + BannerArt.Thousands((long)stats.ExpNivNext) + " (" + Percent(percent) + ")";
                }
            }
        }

        // La liste des métiers est remplie par le fil réseau (JS) : une copie concurrente peut échouer, la jauge reste alors vide.
        private static Tool_BotProtocol.Game.Jobs.Jobs CurrentJob(CharacterClass character)
        {
            int? tool = character?.CurrentJobTool;
            if (tool == null || character.Jobs == null) return null;
            try { return character.Jobs.ToArray().FirstOrDefault(j => j != null && j.ID == tool.Value); }
            catch (ArgumentException) { return null; }
        }

        private static string Percent(double value) => ((int)Math.Max(0, Math.Min(100, value))).ToString(CultureInfo.InvariantCulture) + " %";

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int side = Math.Min(Width, Height) - 2;
            if (side < 4) return;
            var ring = new RectangleF((Width - side) / 2f + Thickness / 2f, (Height - side) / 2f + Thickness / 2f, side - Thickness, side - Thickness);
            using (var track = new Pen(Color.FromArgb(30, 29, 24), Thickness)) graphics.DrawEllipse(track, ring);
            if (ringVisible && value > 0)
                using (var gauge = new Pen(gaugeColor, Thickness)) graphics.DrawArc(gauge, ring, -90, (float)(value * 3.6));
            using (var rule = new Pen(BotUi.Gold, 1))
            {
                graphics.DrawEllipse(rule, (Width - side) / 2f, (Height - side) / 2f, side, side);
                graphics.DrawEllipse(rule, ring.X + Thickness / 2f, ring.Y + Thickness / 2f, ring.Width - Thickness, ring.Height - Thickness);
            }
        }
    }
}
