using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace Outil_Azur_complet.Bot.Controls
{
    /// <summary>
    /// Couleurs d'un acteur, champs <c>Color1</c> à <c>Color3</c> du <c>GM</c> (ou couleurs d'un membre de groupe) : la zone
    /// <c>k</c> des appels <c>GAC.applyColor(&lt;clip&gt;, k)</c> des sprites du client. Une zone absente ou à <c>-1</c> garde
    /// la couleur dessinée dans le SWF. Valeur par défaut : aucune couleur (<see cref="None"/>).
    /// </summary>
    public struct ActorColors : IEquatable<ActorColors>
    {
        // Couleur + 1 (0 : absente) : default(ActorColors) vaut None.
        private readonly int stored1, stored2, stored3;

        public ActorColors(int color1, int color2, int color3)
        {
            stored1 = Store(color1); stored2 = Store(color2); stored3 = Store(color3);
        }

        /// <summary>Aucune couleur : le sprite garde celles du SWF.</summary>
        public static ActorColors None => default(ActorColors);

        /// <summary>Couleur de la zone 1 (RRGGBB), ou -1.</summary>
        public int Color1 => stored1 - 1;
        public int Color2 => stored2 - 1;
        public int Color3 => stored3 - 1;
        public bool IsNone => stored1 == 0 && stored2 == 0 && stored3 == 0;

        /// <summary>Couleur de la zone <paramref name="zone"/> (1 à 3), -1 si elle n'est pas donnée.</summary>
        public int this[int zone] => zone == 1 ? Color1 : zone == 2 ? Color2 : zone == 3 ? Color3 : -1;

        /// <summary>Clé de cache : les trois couleurs en hexadécimal (<c>-</c> pour une zone absente).</summary>
        public string Key => Part(Color1) + "|" + Part(Color2) + "|" + Part(Color3);

        /// <summary>
        /// Couleurs lues comme le client (<c>parseInt(c, 16)</c>) : <c>-1</c>, vide ou illisible = zone absente ; une valeur
        /// courte (<c>30303</c> pour <c>030303</c>, champ <c>colors</c> de StarLoco) se lit telle quelle.
        /// </summary>
        public static ActorColors Parse(string color1, string color2, string color3) =>
            new ActorColors(ParseColor(color1), ParseColor(color2), ParseColor(color3));

        public static ActorColors Parse(IReadOnlyList<string> colors) =>
            colors == null ? None : Parse(colors.Count > 0 ? colors[0] : null, colors.Count > 1 ? colors[1] : null, colors.Count > 2 ? colors[2] : null);

        /// <summary>Champ <c>c1,c2,c3</c> d'un membre de groupe de monstres (GM d'un groupe : couleurs puis accessoires).</summary>
        public static ActorColors ParseList(string colors) =>
            string.IsNullOrEmpty(colors) ? None : Parse(colors.Split(','));

        public static int ParseColor(string text)
        {
            string value = (text ?? string.Empty).Trim();
            if (value.Length == 0 || value.Length > 8 || value == "-1") return -1;
            return int.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int color) ? color & 0xFFFFFF : -1;
        }

        public bool Equals(ActorColors other) => stored1 == other.stored1 && stored2 == other.stored2 && stored3 == other.stored3;
        public override bool Equals(object obj) => obj is ActorColors other && Equals(other);
        public override int GetHashCode() => unchecked((stored1 * 397 ^ stored2) * 397 ^ stored3);
        public static bool operator ==(ActorColors a, ActorColors b) => a.Equals(b);
        public static bool operator !=(ActorColors a, ActorColors b) => !a.Equals(b);
        public override string ToString() => Key;

        private static int Store(int color) => color < 0 ? 0 : (color & 0xFFFFFF) + 1;
        private static string Part(int color) => color < 0 ? "-" : color.ToString("x6", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Recoloration des sprites (lot AN6) : le client pose sur chaque clip d'une zone un aplat de la couleur du <c>GM</c>
    /// (transformation de couleur <c>ra/ga/ba = 0</c>, <c>rb/gb/bb</c> = la couleur, alpha gardé). L'exporteur écrit, pour
    /// chaque PNG <c>&lt;gfx&gt;_&lt;anim&gt;.png</c> d'un gfx coloré, un masque <c>&lt;gfx&gt;_&lt;anim&gt;.couleurs.png</c> de
    /// même taille (rouge : part de la zone dans le pixel, 0 à 255 ; vert : index de la couleur d'origine) et les lignes
    /// <c>gfx anim index zone couleur</c> de <c>couleurs.tsv</c>. Pixel recoloré = PNG + (couleur du GM - couleur d'origine)
    /// × part / 255, borné à 0..255, alpha du PNG ; une zone à -1 garde le PNG. Calcul fait une fois, sur le pool, par
    /// <see cref="ActorSprites"/> (aucun <c>ColorMatrix</c> au dessin).
    /// </summary>
    internal static class SpriteRecolor
    {
        /// <summary>Nom de la table des couleurs d'origine, à côté d'<c>ancres.tsv</c>.</summary>
        public const string TableFile = "couleurs.tsv";
        /// <summary>Suffixe d'un masque : <c>&lt;gfx&gt;_&lt;anim&gt;.couleurs.png</c>.</summary>
        public const string MaskSuffix = ".couleurs.png";

        /// <summary>Couleur d'origine d'un index de masque : sa zone (1 à 3) et sa couleur dans le SWF (RRGGBB).</summary>
        public struct Origin
        {
            public Origin(int zone, int color) { Zone = zone; Color = color; }
            public int Zone { get; }
            public int Color { get; }
        }

        /// <summary>
        /// Lit <c>couleurs.tsv</c> : clé <c>gfx\tanim</c> (sans casse) → index → origine. Lignes illisibles ignorées ; fichier
        /// absent ou illisible : table vide (les sprites gardent leurs couleurs).
        /// </summary>
        public static Dictionary<string, Dictionary<int, Origin>> ReadTable(string path)
        {
            var table = new Dictionary<string, Dictionary<int, Origin>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(path)) return table;
                foreach (string raw in File.ReadAllLines(path))
                {
                    string[] fields = raw.TrimEnd('\r').Split('\t');
                    if (fields.Length != 5 || fields[0] == "gfx") continue;
                    if (!int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out int index) || index < 1 || index > 255) continue;
                    if (!int.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out int zone) || zone < 1 || zone > 3) continue;
                    if (fields[4].Length != 6 || !int.TryParse(fields[4], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int color)) continue;
                    string key = fields[0] + "\t" + fields[1];
                    if (!table.TryGetValue(key, out Dictionary<int, Origin> rows))
                    {
                        rows = new Dictionary<int, Origin>();
                        table[key] = rows;
                    }
                    rows[index] = new Origin(zone, color);
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException
                || error is NotSupportedException || error is System.Security.SecurityException) { table.Clear(); }
            return table;
        }

        /// <summary>Vrai si une couleur donnée touche au moins une zone de la table (sinon la recoloration ne change rien).</summary>
        public static bool Touches(Dictionary<int, Origin> rows, ActorColors colors)
        {
            if (rows == null || colors.IsNone) return false;
            foreach (Origin origin in rows.Values)
                if (colors[origin.Zone] >= 0) return true;
            return false;
        }

        /// <summary>
        /// Lit un masque de <paramref name="width"/> × <paramref name="height"/> px : part (rouge) et index (vert) de chaque
        /// pixel. En-tête lu d'abord : taille différente, fichier qui n'est pas un PNG ou illisible → faux et
        /// <paramref name="refused"/>. Fil appelant (le pool, jamais l'interface).
        /// </summary>
        public static bool ReadMask(string path, int width, int height, out byte[] coverage, out byte[] index, out string refused)
        {
            coverage = index = null; refused = null;
            string name = Path.GetFileName(path);
            if (!ActorSprites.TryReadPngSize(path, out int maskWidth, out int maskHeight))
            {
                refused = name + " n'est pas un PNG lisible.";
                return false;
            }
            if (maskWidth != width || maskHeight != height)
            {
                refused = name + " mesure " + maskWidth + " × " + maskHeight + " px au lieu de " + width + " × " + height + " (bande réexportée sans son masque ?).";
                return false;
            }
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var source = Image.FromStream(stream))
                using (var copy = new Bitmap(width, height, PixelFormat.Format32bppArgb))
                {
                    if (source.Width != width || source.Height != height) { refused = name + " : taille refusée."; return false; }
                    using (var graphics = Graphics.FromImage(copy))
                    {
                        // Copie à l'échelle 1, comme le décodage des sprites : palette (masques de l'exporteur) ou RGB.
                        graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                        graphics.DrawImage(source, 0, 0, width, height);
                    }
                    coverage = new byte[(long)width * height];
                    index = new byte[(long)width * height];
                    BitmapData data = copy.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    try
                    {
                        var row = new int[width];
                        for (int y = 0; y < height; y++)
                        {
                            Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, width);
                            long offset = (long)y * width;
                            for (int x = 0; x < width; x++)
                            {
                                coverage[offset + x] = (byte)(row[x] >> 16 & 0xFF);
                                index[offset + x] = (byte)(row[x] >> 8 & 0xFF);
                            }
                        }
                    }
                    finally { copy.UnlockBits(data); }
                }
                return true;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException
                || error is ExternalException || error is OutOfMemoryException || error is NotSupportedException)
            {
                coverage = index = null;
                refused = name + " est illisible : " + error.Message;
                return false;
            }
        }

        /// <summary>
        /// Recolore <paramref name="image"/> (32 bits ARGB non prémultiplié, copie propre à l'appelant) sur place ; renvoie
        /// le nombre de pixels changés. Un index absent de la table ou une zone à -1 laisse le pixel tel quel.
        /// </summary>
        public static int Apply(Bitmap image, byte[] coverage, byte[] index, Dictionary<int, Origin> rows, ActorColors colors)
        {
            if (image == null || coverage == null || index == null || rows == null || colors.IsNone) return 0;
            int width = image.Width, height = image.Height;
            if (coverage.LongLength != (long)width * height || index.LongLength != coverage.LongLength) return 0;
            // Écart (couleur du GM - couleur d'origine) par index, -1 en rouge pour « rien à faire ».
            var deltas = new int[256, 3];
            var active = new bool[256];
            foreach (KeyValuePair<int, Origin> row in rows)
            {
                int target = colors[row.Value.Zone];
                if (target < 0 || row.Key < 1 || row.Key > 255) continue;
                active[row.Key] = true;
                deltas[row.Key, 0] = (target >> 16 & 0xFF) - (row.Value.Color >> 16 & 0xFF);
                deltas[row.Key, 1] = (target >> 8 & 0xFF) - (row.Value.Color >> 8 & 0xFF);
                deltas[row.Key, 2] = (target & 0xFF) - (row.Value.Color & 0xFF);
            }
            int changed = 0;
            BitmapData data = image.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                var line = new int[width];
                for (int y = 0; y < height; y++)
                {
                    IntPtr scan = IntPtr.Add(data.Scan0, y * data.Stride);
                    Marshal.Copy(scan, line, 0, width);
                    long offset = (long)y * width;
                    bool dirty = false;
                    for (int x = 0; x < width; x++)
                    {
                        int part = coverage[offset + x], slot = index[offset + x];
                        if (part == 0 || !active[slot]) continue;
                        int pixel = line[x];
                        if ((pixel >> 24 & 0xFF) == 0) continue;
                        int r = Clamp((pixel >> 16 & 0xFF) + Scale(deltas[slot, 0], part));
                        int g = Clamp((pixel >> 8 & 0xFF) + Scale(deltas[slot, 1], part));
                        int b = Clamp((pixel & 0xFF) + Scale(deltas[slot, 2], part));
                        int next = (int)((uint)pixel & 0xFF000000) | r << 16 | g << 8 | b;
                        if (next == pixel) continue;
                        line[x] = next; dirty = true; changed++;
                    }
                    if (dirty) Marshal.Copy(line, 0, scan, width);
                }
            }
            finally { image.UnlockBits(data); }
            return changed;
        }

        /// <summary>Écart × part / 255, arrondi au plus proche (exact pour une part de 255).</summary>
        private static int Scale(int delta, int part)
        {
            int product = delta * part;
            return product >= 0 ? (product + 127) / 255 : -((-product + 127) / 255);
        }

        private static int Clamp(int value) => value < 0 ? 0 : value > 255 ? 255 : value;
    }
}
