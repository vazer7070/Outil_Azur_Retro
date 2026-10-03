using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace Outil_Azur_complet.Bot
{
    /// <summary>
    /// Éléments graphiques exportés du client Dofus 1.34 fourni (dossier <c>Resources/Bot/Client</c>, copié à côté
    /// de l'exécutable dans <c>ressources/Bot/UI/Client</c>). Chaque élément est chargé une fois ; un fichier absent
    /// renvoie <c>null</c> et l'interface revient à son rendu dessiné.
    /// </summary>
    internal static class ClientAssets
    {
        private static readonly Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Bitmap> scaled = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private static readonly object sync = new object();

        internal static readonly string[] Folders = {
            Path.Combine("Resources", "Bot", "Client"), Path.Combine("ressources", "Bot", "UI", "Client") };

        /// <summary>Vrai quand les éléments du client sont présents à côté de l'exécutable.</summary>
        internal static bool Available => Get("logo") != null;

        /// <summary>Image originale (échelle 2 du client) ou <c>null</c> si elle manque.</summary>
        internal static Bitmap Get(string name)
        {
            lock (sync) {
                Bitmap image;
                if (cache.TryGetValue(name, out image)) return image;
                image = Load(name); cache[name] = image; return image;
            }
        }

        /// <summary>Copie réduite tenant dans un carré de <paramref name="size"/> pixels, proportions conservées.</summary>
        internal static Bitmap Icon(string name, int size)
        {
            var source = Get(name); if (source == null || size < 1) return null;
            string key = name + "@" + size;
            lock (sync) {
                Bitmap image;
                if (scaled.TryGetValue(key, out image)) return image;
                float ratio = Math.Min((float)size / source.Width, (float)size / source.Height);
                int width = Math.Max(1, (int)Math.Round(source.Width * ratio)), height = Math.Max(1, (int)Math.Round(source.Height * ratio));
                image = new Bitmap(width, height);
                using (var graphics = Graphics.FromImage(image)) {
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.DrawImage(source, new Rectangle(0, 0, width, height));
                }
                scaled[key] = image; return image;
            }
        }

        /// <summary>Dessine une pilule (bouton du client) : extrémités conservées, centre étiré, hauteur adaptée.</summary>
        internal static void DrawPill(Graphics graphics, Image image, Rectangle target, int capWidth)
        {
            if (image == null || target.Width < 2 || target.Height < 2) return;
            float ratio = (float)target.Height / image.Height;
            int cap = Math.Min(image.Width / 2 - 1, capWidth), destCap = Math.Max(1, Math.Min(target.Width / 2 - 1, (int)Math.Round(cap * ratio)));
            var state = graphics.Save();
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.DrawImage(image, new Rectangle(target.X, target.Y, destCap, target.Height),
                new Rectangle(0, 0, cap, image.Height), GraphicsUnit.Pixel);
            graphics.DrawImage(image, new Rectangle(target.X + destCap, target.Y, target.Width - 2 * destCap, target.Height),
                new Rectangle(cap, 0, image.Width - 2 * cap, image.Height), GraphicsUnit.Pixel);
            graphics.DrawImage(image, new Rectangle(target.Right - destCap, target.Y, destCap, target.Height),
                new Rectangle(image.Width - cap, 0, cap, image.Height), GraphicsUnit.Pixel);
            graphics.Restore(state);
        }

        /// <summary>Dessine l'image entière dans le cadre, proportions conservées, alignée en bas et centrée.</summary>
        internal static RectangleF DrawFit(Graphics graphics, Image image, RectangleF bounds, bool alignBottom = false)
        {
            if (image == null || bounds.Width < 1 || bounds.Height < 1) return RectangleF.Empty;
            float ratio = Math.Min(bounds.Width / image.Width, bounds.Height / image.Height);
            float width = image.Width * ratio, height = image.Height * ratio;
            var target = new RectangleF(bounds.X + (bounds.Width - width) / 2,
                alignBottom ? bounds.Bottom - height : bounds.Y + (bounds.Height - height) / 2, width, height);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(image, target); return target;
        }

        private static Bitmap Load(string name)
        {
            string root = Path.GetDirectoryName(typeof(ClientAssets).Assembly.Location) ?? "";
            foreach (string folder in Folders) {
                string path = Path.Combine(root, folder, name + ".png");
                if (!File.Exists(path)) continue;
                try {
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var source = Image.FromStream(stream)) return new Bitmap(source);
                } catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException ||
                    error is System.Runtime.InteropServices.ExternalException || error is OutOfMemoryException) { }
            }
            return null;
        }
    }

    /// <summary>
    /// Bouton peint avec les pilules du client fourni : orange (<c>ChooseCharacterBtnPlay</c>) pour l'action principale,
    /// parchemin (<c>ButtonDownload</c>) sinon. Sans ces fichiers, le bouton reste un bouton plat de la palette.
    /// </summary>
    internal sealed class ClientButton : Button
    {
        private bool pressed, hover;
        internal bool Primary { get; set; }
        /// <summary>Image du client dessinée à gauche du texte (par exemple le dé des couleurs).</summary>
        internal Image Glyph { get; set; }

        internal ClientButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat; Cursor = Cursors.Hand; UseVisualStyleBackColor = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics; var area = ClientRectangle;
            using (var parent = new SolidBrush(Parent?.BackColor ?? BotUi.Paper)) graphics.FillRectangle(parent, area);
            var pill = ClientAssets.Get(Primary ? "bouton-principal-haut" : (pressed ? "bouton-bas" : "bouton-haut"));
            Color text = Primary ? Color.FromArgb(45, 35, 15) : BotUi.Ink;
            if (pill != null) {
                ClientAssets.DrawPill(graphics, pill, area, Primary ? 26 : 30);
                if (pressed && Primary) using (var veil = new SolidBrush(Color.FromArgb(70, 40, 20, 0))) graphics.FillRectangle(veil, area);
                else if (hover && Enabled) using (var veil = new SolidBrush(Color.FromArgb(45, Color.White))) graphics.FillRectangle(veil, area);
            } else {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var shape = Rounded(Rectangle.Inflate(area, -1, -1), 6))
                using (var fill = new SolidBrush(Primary ? (pressed ? Color.FromArgb(69, 80, 40) : hover ? Color.FromArgb(125, 139, 75) : BotUi.Olive)
                    : (pressed ? BotUi.Gold : hover ? Color.FromArgb(249, 240, 203) : BotUi.PaperLight)))
                using (var border = new Pen(Primary ? Color.FromArgb(69, 80, 40) : BotUi.Gold)) {
                    graphics.FillPath(fill, shape); graphics.DrawPath(border, shape);
                }
                if (Primary) text = Color.White;
            }
            if (!Enabled) {
                using (var veil = new SolidBrush(Color.FromArgb(120, BotUi.PaperLight))) graphics.FillRectangle(veil, area);
                text = BotUi.Muted;
            }
            var content = Rectangle.Inflate(area, -6, -2);
            if (Image != null && string.IsNullOrEmpty(Text)) { ClientAssets.DrawFit(graphics, Image, Rectangle.Inflate(area, -5, -4)); return; }
            if (Glyph != null) {
                int size = Math.Min(Glyph.Height, Math.Max(8, area.Height - 10));
                var glyph = new Rectangle(content.X + 2, area.Y + (area.Height - size) / 2, size, size);
                ClientAssets.DrawFit(graphics, Glyph, glyph);
                content = new Rectangle(glyph.Right + 4, content.Y, content.Right - glyph.Right - 4, content.Height);
            }
            // GDI+ plutôt que TextRenderer : les glyphes « › » et « − » des petits boutons s'affichent partout.
            using (var brush = new SolidBrush(text))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap, HotkeyPrefix = System.Drawing.Text.HotkeyPrefix.None })
                graphics.DrawString(Text, Font, brush, content, format);
            if (Focused && Enabled && ShowFocusCues) ControlPaint.DrawFocusRectangle(graphics, Rectangle.Inflate(area, -4, -4));
        }

        private static GraphicsPath Rounded(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath(); int d = radius * 2;
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90); path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90); path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure(); return path;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    }
}
