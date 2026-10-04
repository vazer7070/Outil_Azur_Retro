using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Tool_BotProtocol.Game.Data;

namespace Outil_Azur_complet.Bot.Controls.Banner
{
    /// <summary>
    /// Texte centré sur la carte (<c>CenterText</c> du client) : nom de la zone à l'entrée d'une nouvelle sous-zone (2 s, sans
    /// fond), « Le combat commence ! » (<c>GAME_LAUNCH</c>, 2 s, avec fond). Le contrôle se cache tout seul à la fin du délai
    /// et ne capte pas la souris.
    /// </summary>
    public sealed class CenterText : Control
    {
        private readonly Timer timer = new Timer();
        private bool background;

        public CenterText()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Visible = false; Font = BotFonts.Get(14, FontStyle.Bold); ForeColor = Color.White; BackColor = BotUi.Frame;
            AccessibleRole = AccessibleRole.StaticText; TabStop = false;
            timer.Tick += (s, e) => { timer.Stop(); Visible = false; };
        }

        /// <summary>Dernier texte affiché (tests, accessibilité).</summary>
        public string Message { get; private set; } = string.Empty;
        public bool HasBackground => background;

        /// <summary>
        /// Texte du client pour une arrivée sur la carte, comme <c>onMapLoaded</c> : « Zone » ou « Zone\n(Sous-zone) » si les
        /// noms diffèrent ; <c>null</c> si la sous-zone est masquée (« // ») ou si les textes de cartes manquent.
        /// </summary>
        public static string ZoneText(int mapId)
        {
            if (!LangData.IsLoaded("maps") || LangData.Map.SubAreaId(mapId) == null) return null;
            string area = LangData.Map.Area(mapId), subArea = LangData.Map.SubArea(mapId);
            if (string.IsNullOrEmpty(area)) return null;
            if (string.IsNullOrEmpty(subArea) || subArea.StartsWith("//", StringComparison.Ordinal) || subArea == area) return area;
            return area + "\n(" + subArea + ")";
        }

        /// <summary>Affiche <paramref name="text"/> au centre de la carte pendant <paramref name="milliseconds"/> (thread de l'interface).</summary>
        public void Show(string text, int milliseconds, bool withBackground)
        {
            if (IsDisposed || string.IsNullOrEmpty(text)) return;
            Message = text; background = withBackground; AccessibleName = text.Replace("\n", " ");
            Size measured = TextRenderer.MeasureText(text, Font);
            Size = new Size(measured.Width + 32, measured.Height + 16);
            Center();
            Visible = true; BringToFront(); Invalidate();
            timer.Stop(); timer.Interval = Math.Max(100, milliseconds); timer.Start();
        }

        /// <summary>Recentre le texte dans son parent (appelé au redimensionnement de la carte).</summary>
        public void Center()
        {
            if (Parent == null) return;
            Location = new Point(Math.Max(0, (Parent.ClientSize.Width - Width) / 2), Math.Max(0, (Parent.ClientSize.Height - Height) / 3));
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (background) { using (var fill = new SolidBrush(Color.FromArgb(41, 38, 31))) e.Graphics.FillRectangle(fill, ClientRectangle); }
            else base.OnPaintBackground(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (background) using (var rule = new Pen(BotUi.Gold)) e.Graphics.DrawRectangle(rule, 0, 0, Width - 1, Height - 1);
            const TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
            Rectangle shadow = ClientRectangle; shadow.Offset(2, 2);
            TextRenderer.DrawText(e.Graphics, Message, Font, shadow, Color.Black, flags);
            TextRenderer.DrawText(e.Graphics, Message, Font, ClientRectangle, ForeColor, flags);
        }

        // Le texte ne bloque pas la carte : les clics passent au contrôle situé dessous.
        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x84, HTTRANSPARENT = -1;
            if (m.Msg == WM_NCHITTEST) { m.Result = (IntPtr)HTTRANSPARENT; return; }
            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Coordonnées de la carte en haut à gauche (<c>MapInfos</c>, option du même nom, raccourci Maj + ') : nom de la carte
    /// (« Zone (Sous-zone) ») et coordonnées, texte blanc ombré comme le client.
    /// </summary>
    public sealed class MapInfosLabel : Control
    {
        public MapInfosLabel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = BotFonts.Get(9, FontStyle.Bold); ForeColor = Color.White; BackColor = BotUi.Frame; Location = new Point(6, 6); Size = new Size(240, 36);
            AccessibleRole = AccessibleRole.StaticText; TabStop = false;
        }

        public string AreaText { get; private set; } = string.Empty;
        public string CoordinatesText { get; private set; } = string.Empty;

        /// <summary>Carte affichée : nom du client si les textes de cartes sont chargés, sinon l'identifiant ; coordonnées « x,y ».</summary>
        public void SetMap(int mapId, int x, int y)
        {
            AreaText = LangData.IsLoaded("maps") && LangData.Map.Has(mapId) ? LangData.Map.Name(mapId) : "Carte " + mapId.ToString(CultureInfo.InvariantCulture);
            CoordinatesText = x.ToString(CultureInfo.InvariantCulture) + "," + y.ToString(CultureInfo.InvariantCulture);
            AccessibleName = AreaText + " " + CoordinatesText;
            Size area = TextRenderer.MeasureText(AreaText, Font), coords = TextRenderer.MeasureText(CoordinatesText, Font);
            Size = new Size(Math.Max(area.Width, coords.Width) + 8, area.Height + coords.Height + 4);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            int line = TextRenderer.MeasureText("Ag", Font).Height;
            foreach (var entry in new[] { Tuple.Create(AreaText, 2), Tuple.Create(CoordinatesText, 2 + line) })
            {
                TextRenderer.DrawText(e.Graphics, entry.Item1, Font, new Point(5, entry.Item2 + 1), Color.Black, flags);
                TextRenderer.DrawText(e.Graphics, entry.Item1, Font, new Point(4, entry.Item2), ForeColor, flags);
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x84, HTTRANSPARENT = -1;
            if (m.Msg == WM_NCHITTEST) { m.Result = (IntPtr)HTTRANSPARENT; return; }
            base.WndProc(ref m);
        }
    }
}
