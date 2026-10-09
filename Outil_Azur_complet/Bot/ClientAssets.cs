using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Outil_Azur_complet.Bot
{
    /// <summary>
    /// Éléments graphiques exportés du client Dofus 1.34 fourni (dossier <c>Resources/Bot/Client</c>, copié à côté
    /// de l'exécutable dans <c>ressources/Bot/UI/Client</c>). Chaque élément est chargé une fois ; un fichier absent
    /// renvoie <c>null</c> et l'interface revient à son rendu dessiné. Les autres familles exportées (objets,
    /// portraits, emblèmes, carte du monde…) passent par <see cref="Get(string, string)"/>, sous <see cref="Root"/>.
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

        // ------------------------------------------------------------------------------------------------------
        // Familles d'images exportées du client (Resources/Bot/<Famille>, voir chaque PROVENANCE.md), copiées à
        // côté de l'exécutable dans ressources/Bot/<Famille>. Accès par famille et nom relatif (« 1/107 » pour
        // Items, « back/3 » pour Emblems, « 0/-1_-1 » pour WorldMap) ; tout fichier absent, nom invalide ou PNG
        // illisible donne null. Les images rendues sont partagées par le cache : ne pas les modifier ni les
        // libérer, et les verrouiller (lock (image)) le temps d'un dessin, comme Tinted, Emblem et Heart qui
        // peuvent tourner hors du thread de l'interface (GDI+ refuse deux dessins simultanés d'un Bitmap).
        // Get lit le disque sur le thread appelant : depuis l'interface, passer par GetAsync ou TryCached
        // (jamais de lecture de PNG pendant un Paint).

        private sealed class CachedAsset
        {
            internal Bitmap Image; internal long Bytes; internal LinkedListNode<string> Node;
        }

        private static readonly Dictionary<string, CachedAsset> assets = new Dictionary<string, CachedAsset>(StringComparer.Ordinal);
        private static readonly LinkedList<string> assetOrder = new LinkedList<string>();
        private static long assetBytes;
        private static int assetGeneration;
        private static string assetRoot;

        /// <summary>Nombre maximal d'entrées (images et absences) gardées par le cache des familles.</summary>
        internal const int AssetCacheEntries = 1024;
        /// <summary>Mémoire maximale (pixels × 4 octets) des images gardées par le cache des familles.</summary>
        internal const long AssetCacheBytes = 64L * 1024 * 1024;

        /// <summary>Diagnostic : levé après chaque lecture d'un PNG sur le disque, avec « famille/nom » et le thread lecteur.</summary>
        internal static event Action<string, int> AssetFileRead;

        /// <summary>
        /// Dossier des familles (par défaut <c>ressources/Bot</c> à côté de l'exécutable). Le changer vide le cache ;
        /// les images déjà rendues restent valides pour qui les tient.
        /// </summary>
        internal static string Root
        {
            get { lock (sync) return assetRoot ?? (assetRoot = DefaultRoot()); }
            set {
                lock (sync) {
                    assetRoot = string.IsNullOrWhiteSpace(value) ? DefaultRoot() : value;
                    assets.Clear(); assetOrder.Clear(); assetBytes = 0; assetGeneration++;
                }
            }
        }

        /// <summary>Image d'une famille (« Items », « Portraits », « Smileys », « Emotes », « Jobs », « Alignments »,
        /// « Emblems », « WorldMap », « Spells », « Sprites », « Client », « Selection ») ou null.</summary>
        internal static Bitmap Get(string family, string name)
        {
            string key = AssetKey(family, name); if (key == null) return null;
            Bitmap image; int generation; string root;
            lock (sync) {
                if (TryCachedLocked(key, out image)) return image;
                generation = assetGeneration; root = assetRoot ?? (assetRoot = DefaultRoot());
            }
            image = ReadAsset(root, family, key);
            lock (sync) {
                CachedAsset existing;
                if (assets.TryGetValue(key, out existing)) { Touch(existing); image?.Dispose(); return existing.Image; }
                if (generation == assetGeneration) Store(key, image);
            }
            return image;
        }

        /// <summary>Comme <see cref="Get(string, string)"/>, la lecture et le décodage se faisant sur le pool de threads ;
        /// une image déjà en cache (ou un nom invalide) est rendue sans attendre.</summary>
        internal static Task<Bitmap> GetAsync(string family, string name)
        {
            Bitmap cached;
            if (AssetKey(family, name) == null) return Task.FromResult<Bitmap>(null);
            if (TryCached(family, name, out cached)) return Task.FromResult(cached);
            return Task.Factory.StartNew(() => Get(family, name), CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
        }

        /// <summary>Vrai si l'image (ou son absence) est déjà en cache ; ne lit jamais le disque.</summary>
        internal static bool TryCached(string family, string name, out Bitmap image)
        {
            image = null; string key = AssetKey(family, name); if (key == null) return false;
            lock (sync) return TryCachedLocked(key, out image);
        }

        /// <summary>Icône d'un objet : <c>Items/&lt;type&gt;/&lt;gfx&gt;.png</c> (type et gfx du modèle d'objet).</summary>
        internal static Bitmap ItemIcon(int type, int gfx)
        {
            return type < 0 || gfx < 0 ? null : Get("Items", type.ToString(CultureInfo.InvariantCulture) + "/" + gfx.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Copie recolorée comme <c>Color.setRGB</c> du client : chaque pixel prend la couleur <paramref name="rgb"/>
        /// (0xRRGGBB) et garde son alpha ; -1 rend l'image transparente (le client met alors <c>_alpha</c> à 0).
        /// L'appelant libère l'image rendue.
        /// </summary>
        internal static Bitmap Tinted(Image source, int rgb)
        {
            if (source == null) return null;
            int width, height;
            lock (source) { width = source.Width; height = source.Height; }
            if (width < 1 || height < 1) return null;
            var copy = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            if (rgb == -1) return copy;
            try {
                using (var graphics = Graphics.FromImage(copy)) {
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    lock (source) graphics.DrawImage(source, 0, 0, width, height);
                }
                var data = copy.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                try {
                    int color = rgb & 0xFFFFFF; var row = new int[width];
                    for (int y = 0; y < height; y++) {
                        IntPtr line = IntPtr.Add(data.Scan0, y * data.Stride);
                        Marshal.Copy(line, row, 0, width);
                        for (int x = 0; x < width; x++) row[x] = (row[x] & unchecked((int)0xFF000000)) | color;
                        Marshal.Copy(row, 0, line, width);
                    }
                } finally { copy.UnlockBits(data); }
                return copy;
            } catch { copy.Dispose(); throw; }
        }

        /// <summary>Nombre de fonds et de motifs d'emblème du client (<c>EMBLEM_BACKS_COUNT</c>, <c>EMBLEM_UPS_COUNT</c>).</summary>
        internal const int EmblemBacks = 17, EmblemUps = 104;

        /// <summary>
        /// Lit un emblème de guilde « fond,couleurFond,motif,couleurMotif » en base 36, comme <c>createGuildEmblem</c>
        /// du client : un identifiant hors de 1..17 (fond) ou 1..104 (motif) devient 1. Écarts voulus : un identifiant
        /// illisible devient aussi 1 (le client garderait NaN et ne chargerait rien) et une couleur illisible devient
        /// 0 (noir). Faux seulement si la chaîne n'a pas quatre parties.
        /// </summary>
        internal static bool TryParseEmblem(string value, out int backId, out int backColor, out int upId, out int upColor)
        {
            backId = upId = 1; backColor = upColor = 0;
            if (string.IsNullOrEmpty(value)) return false;
            string[] parts = value.Split(',');
            if (parts.Length < 4) return false;
            long number;
            backId = ParseBase36(parts[0], out number) && number >= 1 && number <= EmblemBacks ? (int)number : 1;
            backColor = ParseBase36(parts[1], out number) ? (int)(number & 0xFFFFFF) : 0;
            upId = ParseBase36(parts[2], out number) && number >= 1 && number <= EmblemUps ? (int)number : 1;
            upColor = ParseBase36(parts[3], out number) ? (int)(number & 0xFFFFFF) : 0;
            return true;
        }

        /// <summary>Emblème composé depuis sa chaîne (voir <see cref="TryParseEmblem"/>) ; null si elle est illisible.</summary>
        internal static Bitmap Emblem(string value, int size, bool shadow = false)
        {
            int backId, backColor, upId, upColor;
            return TryParseEmblem(value, out backId, out backColor, out upId, out upColor) ? Emblem(backId, backColor, upId, upColor, size, shadow) : null;
        }

        /// <summary>
        /// Emblème de guilde de <paramref name="size"/> × <paramref name="size"/> pixels, composé comme le composant
        /// <c>Emblem</c> de <c>core.swf</c> (cadre de 80) : ombre facultative (fond entier en blanc, 80 × 80 en (0, 0)),
        /// fond (calque <c>back</c> recoloré + contour, 78 × 78 en (1, 1)) puis motif recoloré (50 × 50 en (15, 15)),
        /// chacun ajusté et centré dans sa case comme le fait le <c>Loader</c> du client. Null si les images manquent ;
        /// l'appelant libère l'image rendue. Lit le disque au premier appel : hors du thread de l'interface.
        /// </summary>
        internal static Bitmap Emblem(int backId, int backColor, int upId, int upColor, int size, bool shadow = false)
        {
            if (size < 1 || size > 1024) return null;
            if (backId < 1 || backId > EmblemBacks) backId = 1;
            if (upId < 1 || upId > EmblemUps) upId = 1;
            string back = backId.ToString(CultureInfo.InvariantCulture);
            var fill = Get("Emblems", "back/" + back); var contour = Get("Emblems", "back/" + back + "_contour");
            var motif = Get("Emblems", "up/" + upId.ToString(CultureInfo.InvariantCulture));
            if (fill == null && contour == null && motif == null) return null;
            var result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            float scale = size / 80f;
            try {
                using (var graphics = Graphics.FromImage(result)) {
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    var backBox = new RectangleF(1 * scale, 1 * scale, 78 * scale, 78 * scale);
                    if (shadow) {
                        var shadowBox = new RectangleF(0, 0, 80 * scale, 80 * scale);
                        DrawTinted(graphics, fill, 0xFFFFFF, shadowBox); DrawTinted(graphics, contour, 0xFFFFFF, shadowBox);
                    }
                    DrawTinted(graphics, fill, backColor, backBox);
                    DrawShared(graphics, contour, backBox);
                    DrawTinted(graphics, motif, upColor, new RectangleF(15 * scale, 15 * scale, 50 * scale, 50 * scale));
                }
                return result;
            } catch { result.Dispose(); throw; }
        }

        /// <summary>
        /// Cœur des points de vie du bandeau à <paramref name="ratio"/> (0 à 1) : <c>Heart_vide</c> puis la partie
        /// basse de <c>Heart</c>, comme le client qui règle la hauteur de <c>_mcRectangle</c> (ancré en y = 18,
        /// 36 de haut, dans un PNG d'origine (-22, -20) à l'échelle 2). Null si les images manquent ; l'appelant
        /// libère l'image rendue. Lit le disque au premier appel : hors du thread de l'interface.
        /// </summary>
        internal static Bitmap Heart(double ratio)
        {
            var full = Get("Client", "Heart"); var empty = Get("Client", "Heart_vide");
            if (full == null || empty == null) return null;
            if (double.IsNaN(ratio) || ratio < 0) ratio = 0; else if (ratio > 1) ratio = 1;
            int width, height;
            lock (empty) { width = empty.Width; height = empty.Height; }
            var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            try {
                using (var graphics = Graphics.FromImage(result)) {
                    lock (empty) graphics.DrawImage(empty, 0, 0, width, height);
                    float bottom = (18 + 20) * 2f, top = bottom - (float)ratio * 36 * 2f;
                    if (bottom > top) {
                        graphics.SetClip(new RectangleF(0, top, width, bottom - top));
                        lock (full) graphics.DrawImage(full, 0, 0, full.Width, full.Height);
                    }
                }
                return result;
            } catch { result.Dispose(); throw; }
        }

        private static void DrawTinted(Graphics graphics, Bitmap source, int rgb, RectangleF box)
        {
            if (source == null) return;
            using (var tinted = Tinted(source, rgb)) DrawShared(graphics, tinted, box);
        }

        private static void DrawShared(Graphics graphics, Bitmap source, RectangleF box)
        {
            if (source == null) return;
            lock (source) {
                float ratio = Math.Min(box.Width / source.Width, box.Height / source.Height);
                float width = source.Width * ratio, height = source.Height * ratio;
                graphics.DrawImage(source, new RectangleF(box.X + (box.Width - width) / 2, box.Y + (box.Height - height) / 2, width, height));
            }
        }

        private static bool ParseBase36(string text, out long value)
        {
            // parseInt(texte, 36) du client : espaces initiaux ignorés, signe facultatif, chiffres jusqu'au premier invalide.
            value = 0; if (text == null) return false;
            int i = 0; while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            bool negative = false;
            if (i < text.Length && (text[i] == '-' || text[i] == '+')) { negative = text[i] == '-'; i++; }
            int digits = 0;
            for (; i < text.Length && digits < 12; i++, digits++) {
                char c = char.ToLowerInvariant(text[i]);
                int digit = c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'z' ? c - 'a' + 10 : -1;
                if (digit < 0) break;
                value = value * 36 + digit;
            }
            if (negative) value = -value;
            return digits > 0;
        }

        private static string DefaultRoot()
        {
            string folder = Path.GetDirectoryName(typeof(ClientAssets).Assembly.Location);
            return Path.Combine(string.IsNullOrEmpty(folder) ? AppDomain.CurrentDomain.BaseDirectory : folder, "ressources", "Bot");
        }

        /// <summary>« famille/segment/…/nom », ou null si la famille ou le nom pourrait sortir du dossier de la famille.</summary>
        private static string AssetKey(string family, string name)
        {
            if (!ValidSegment(family, false) || string.IsNullOrEmpty(name) || name.Length > 160) return null;
            string[] segments = name.Split('/', '\\');
            if (segments.Length > 6) return null;
            foreach (string segment in segments) if (!ValidSegment(segment, true)) return null;
            return family + "/" + string.Join("/", segments);
        }

        private static bool ValidSegment(string text, bool allowDash)
        {
            if (string.IsNullOrEmpty(text) || text.Length > 64) return false;
            foreach (char c in text)
                if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '_' || allowDash && c == '-')) return false;
            return true;
        }

        private static string FamilyFolder(string family)
        {
            switch (family) {
                case "Client": return Path.Combine("UI", "Client");
                case "Selection": return Path.Combine("UI", "Selection");
                case "Spells": return "sorts";
                case "Sprites": return "sprites";
                default: return family;
            }
        }

        private static Bitmap ReadAsset(string root, string family, string key)
        {
            Bitmap image = null;
            try {
                string relative = key.Substring(family.Length + 1).Replace('/', Path.DirectorySeparatorChar) + ".png";
                string path = Path.Combine(root, FamilyFolder(family), relative);
                if (File.Exists(path)) {
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var source = Image.FromStream(stream)) {
                        image = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
                        using (var graphics = Graphics.FromImage(image)) {
                            graphics.CompositingMode = CompositingMode.SourceCopy;
                            graphics.DrawImage(source, 0, 0, source.Width, source.Height);
                        }
                    }
                }
            } catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException ||
                error is NotSupportedException || error is System.Security.SecurityException ||
                error is ExternalException || error is OutOfMemoryException || error is InvalidOperationException) {
                image?.Dispose(); image = null;
            }
            try { AssetFileRead?.Invoke(key, Thread.CurrentThread.ManagedThreadId); } catch (Exception) { /* diagnostic seulement */ }
            return image;
        }

        private static bool TryCachedLocked(string key, out Bitmap image)
        {
            CachedAsset entry;
            if (assets.TryGetValue(key, out entry)) { Touch(entry); image = entry.Image; return true; }
            image = null; return false;
        }

        private static void Touch(CachedAsset entry)
        {
            assetOrder.Remove(entry.Node); assetOrder.AddFirst(entry.Node);
        }

        private static void Store(string key, Bitmap image)
        {
            var entry = new CachedAsset { Image = image, Bytes = image == null ? 0 : 4L * image.Width * image.Height, Node = new LinkedListNode<string>(key) };
            assets[key] = entry; assetOrder.AddFirst(entry.Node); assetBytes += entry.Bytes;
            // Éviction des plus anciennes sans Dispose : un appelant peut encore dessiner l'image ; le GC la libère.
            while (assetOrder.Count > 1 && (assets.Count > AssetCacheEntries || assetBytes > AssetCacheBytes)) {
                string oldest = assetOrder.Last.Value; assetOrder.RemoveLast();
                CachedAsset evicted;
                if (assets.TryGetValue(oldest, out evicted)) { assetBytes -= evicted.Bytes; assets.Remove(oldest); }
            }
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
                    error is ExternalException || error is OutOfMemoryException) { }
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
            // Désactivé, l'action principale perd son orange : simplement voilée, elle semblait encore cliquable.
            bool primary = Primary && Enabled;
            using (var parent = new SolidBrush(Parent?.BackColor ?? BotUi.Paper)) graphics.FillRectangle(parent, area);
            var pill = ClientAssets.Get(primary ? "bouton-principal-haut" : (pressed ? "bouton-bas" : "bouton-haut"));
            Color text = primary ? Color.FromArgb(45, 35, 15) : BotUi.Ink;
            if (pill != null) {
                ClientAssets.DrawPill(graphics, pill, area, primary ? 26 : 30);
                if (pressed && primary) using (var veil = new SolidBrush(Color.FromArgb(70, 40, 20, 0))) graphics.FillRectangle(veil, area);
                else if (hover && Enabled) using (var veil = new SolidBrush(Color.FromArgb(45, Color.White))) graphics.FillRectangle(veil, area);
            } else {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var shape = Rounded(Rectangle.Inflate(area, -1, -1), 6))
                using (var fill = new SolidBrush(primary ? (pressed ? Color.FromArgb(69, 80, 40) : hover ? Color.FromArgb(125, 139, 75) : BotUi.Olive)
                    : (pressed ? BotUi.Gold : hover ? Color.FromArgb(249, 240, 203) : BotUi.PaperLight)))
                using (var border = new Pen(primary ? Color.FromArgb(69, 80, 40) : BotUi.Gold)) {
                    graphics.FillPath(fill, shape); graphics.DrawPath(border, shape);
                }
                if (primary) text = Color.White;
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
