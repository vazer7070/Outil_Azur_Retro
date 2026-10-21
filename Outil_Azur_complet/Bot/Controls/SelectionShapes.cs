using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Outil_Azur_complet.Bot.Controls
{
    /// <summary>
    /// Formes de sélection du client 1.34 (lot X8) : le clip <c>s&lt;pente&gt;</c> que <c>selectionHandler.select</c> attache à la
    /// position de la cellule, teint de la couleur demandée (<c>Color.setRGB</c>) à l'opacité 100. Les quinze formes viennent de
    /// <c>cellules/s&lt;n&gt;.png</c> du décor exporté (<c>Resources/Bot/Decor</c>, copié dans <c>ressources/maps</c>) avec leur
    /// ancre <c>selection</c> d'<c>ancres.tsv</c> (pixel en haut à gauche par rapport au point d'enregistrement de la cellule).
    /// Lecture, décodage et teinte se font sur le pool de threads, une fois par dossier, pente et couleur ; <see cref="TryGet"/>
    /// ne lit jamais le disque. Les images teintes (quelques kilo-octets) sont partagées et gardées pour la durée du processus.
    /// </summary>
    public sealed class SelectionShapes
    {
        /// <summary>Dernière pente du client (<c>groundSlope</c> de 1 à 15) ; une autre valeur prend la forme à plat.</summary>
        public const int MaxSlope = 15;
        public const string Folder = "cellules";
        private static readonly ConcurrentDictionary<string, SelectionShapes> shared =
            new ConcurrentDictionary<string, SelectionShapes>(StringComparer.OrdinalIgnoreCase);
        private readonly object sync = new object();
        private readonly string directory;
        // Valeur null : forme absente, illisible ou d'une taille autre que son ancre (le dessin garde alors le losange).
        private readonly Dictionary<long, Shape> shapes = new Dictionary<long, Shape>();
        private readonly HashSet<long> pending = new HashSet<long>();
        private Lazy<Dictionary<int, RectangleF>> anchors;

        private sealed class Shape { public Bitmap Image; public RectangleF Anchor; }

        private SelectionShapes(string directory)
        {
            this.directory = directory;
            anchors = new Lazy<Dictionary<int, RectangleF>>(() => ReadAnchors(Path.Combine(directory, BotMapArtwork.AnchorFileName)));
        }

        /// <summary>Formes du dossier du décor <paramref name="resourceDirectory"/> (celui de <see cref="BotMapArtwork.ResourceDirectory"/>).</summary>
        public static SelectionShapes For(string resourceDirectory) =>
            string.IsNullOrEmpty(resourceDirectory) ? null : shared.GetOrAdd(Path.GetFullPath(resourceDirectory), path => new SelectionShapes(path));

        /// <summary>Levé (sur le pool de threads) quand une forme demandée est prête ou se révèle absente.</summary>
        public event Action Loaded;

        /// <summary>
        /// Forme teinte de la pente <paramref name="slope"/> et son ancre si elle est prête ; sinon demande sa lecture et rend faux
        /// (la vue garde le losange de la cellule jusqu'à <see cref="Loaded"/>). Ne lit jamais le disque.
        /// </summary>
        public bool TryGet(int slope, int rgb, out Bitmap image, out RectangleF anchor)
        {
            image = null; anchor = RectangleF.Empty;
            int frame = slope >= 1 && slope <= MaxSlope ? slope : 1;
            long key = ((long)frame << 32) | (uint)(rgb & 0xFFFFFF);
            lock (sync)
            {
                if (shapes.TryGetValue(key, out Shape shape))
                {
                    if (shape == null) return false;
                    image = shape.Image; anchor = shape.Anchor;
                    return true;
                }
                if (!pending.Add(key)) return false;
            }
            Task.Run(() => Load(key, frame, rgb & 0xFFFFFF));
            return false;
        }

        /// <summary>Vrai si toutes les formes demandées sont prêtes ou absentes (tests).</summary>
        public bool Idle { get { lock (sync) return pending.Count == 0; } }

        private void Load(long key, int frame, int rgb)
        {
            Shape shape = null;
            Bitmap source = null;
            try
            {
                if (anchors.Value.TryGetValue(frame, out RectangleF anchor))
                {
                    string path = Path.Combine(directory, Folder, "s" + frame.ToString(CultureInfo.InvariantCulture) + ".png");
                    if (File.Exists(path))
                    {
                        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        using (var decoded = Image.FromStream(stream)) source = new Bitmap(decoded);
                        // Une image d'une autre taille que son ancre a été remplacée : la placer serait deviner.
                        if (source.Width == (int)anchor.Width && source.Height == (int)anchor.Height)
                            shape = new Shape { Image = ClientAssets.Tinted(source, rgb), Anchor = anchor };
                    }
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException
                || error is ExternalException || error is OutOfMemoryException || error is InvalidOperationException)
            {
                shape = null;
            }
            finally { source?.Dispose(); }
            if (shape != null && shape.Image == null) shape = null;
            lock (sync)
            {
                shapes[key] = shape;
                pending.Remove(key);
            }
            try { Loaded?.Invoke(); }
            catch (Exception error) when (!(error is OutOfMemoryException)) { System.Diagnostics.Trace.WriteLine("SelectionShapes.Loaded : " + error.Message); }
        }

        /// <summary>Lignes <c>selection n xmin ymin largeur hauteur image</c> d'<c>ancres.tsv</c> ; une ligne illisible est ignorée.</summary>
        internal static Dictionary<int, RectangleF> ReadAnchors(string path)
        {
            var result = new Dictionary<int, RectangleF>();
            string[] lines;
            try { if (!File.Exists(path)) return result; lines = File.ReadAllLines(path); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException) { return result; }
            foreach (string raw in lines)
            {
                string[] fields = raw.Trim().Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length < 6 || fields[0] != "selection") continue;
                if (!int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int frame) || frame < 1 || frame > MaxSlope
                    || !float.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) || Math.Abs(x) > 1000
                    || !float.TryParse(fields[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) || Math.Abs(y) > 1000
                    || !float.TryParse(fields[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float width) || width <= 0 || width > 1000
                    || !float.TryParse(fields[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float height) || height <= 0 || height > 1000)
                    continue;
                result[frame] = new RectangleF(x, y, width, height);
            }
            return result;
        }
    }
}
