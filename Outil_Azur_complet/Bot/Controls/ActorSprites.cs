using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Outil_Azur_complet.Bot.Controls
{
    /// <summary>
    /// Choix du symbole d'un sprite selon son orientation, comme <c>Sprite.setAnim</c> du client 1.34 :
    /// 0 → <c>S</c>, 1 → <c>R</c>, 2 → <c>F</c>, 3 → <c>R</c> retourné, 4 → <c>S</c> retourné, 5 → <c>L</c>, 6 → <c>B</c>,
    /// 7 → <c>L</c> retourné. Le suffixe <c>*</c> du champ graphique (<c>noFlip</c>) supprime le retournement.
    /// </summary>
    public static class ActorOrientation
    {
        private static readonly string[] Suffixes = { "S", "R", "F", "R", "S", "L", "B", "L" };

        public static int Normalize(int direction) => ((direction % 8) + 8) % 8;
        public static string Suffix(int direction) => Suffixes[Normalize(direction)];
        public static bool IsMirrored(int direction, bool noFlip = false)
        {
            int normalized = Normalize(direction);
            return !noFlip && (normalized == 3 || normalized == 4 || normalized == 7);
        }
    }

    public enum SpriteLoadState { Loading, Ready, Missing }

    /// <summary>
    /// Image d'un sprite (une pose ou une bande d'images de même taille) et son ancrage : le coin haut-gauche d'une image
    /// se pose en (ancre + <see cref="XMin"/>, ancre + <see cref="YMin"/>), l'ancre étant le pied du sprite sur la cellule
    /// (convention de <c>ancres.tsv</c>). Immuable ; l'image appartient à la bibliothèque et ne doit pas être libérée.
    /// </summary>
    public sealed class SpriteSheet
    {
        private readonly byte[] mask;

        internal SpriteSheet(Bitmap image, int frames, int frameWidth, int frameHeight, int xMin, int yMin, string file, bool legacy, byte[] mask)
        {
            Image = image; Frames = Math.Max(1, frames); FrameWidth = Math.Max(1, frameWidth); FrameHeight = Math.Max(1, frameHeight);
            XMin = xMin; YMin = yMin; File = file; Legacy = legacy; this.mask = mask;
        }

        public Bitmap Image { get; }
        public int Frames { get; }
        public int FrameWidth { get; }
        public int FrameHeight { get; }
        public int XMin { get; }
        public int YMin { get; }
        /// <summary>Nom du PNG lu (<c>1001_staticR.png</c>, ou l'ancien <c>1001R.png</c>).</summary>
        public string File { get; }
        /// <summary>Vrai pour un ancien PNG sans ancre : ancre déduite des pixels visibles (milieu, bas).</summary>
        public bool Legacy { get; }
        internal long Bytes => (long)Image.Width * Image.Height * 4 + (mask?.Length ?? 0);

        public Rectangle Source(int frame)
        {
            int index = ((frame % Frames) + Frames) % Frames;
            return new Rectangle(index * FrameWidth, 0, FrameWidth, FrameHeight);
        }

        /// <summary>Cadre de l'image posée sur <paramref name="anchor"/> (repère de la carte), retournée ou non.</summary>
        public RectangleF Destination(PointF anchor, float scaleX, float scaleY, bool mirrored)
        {
            float left = mirrored ? anchor.X - (XMin + FrameWidth) * scaleX : anchor.X + XMin * scaleX;
            return new RectangleF(left, anchor.Y + YMin * scaleY, FrameWidth * scaleX, FrameHeight * scaleY);
        }

        /// <summary>Vrai si le pixel (<paramref name="x"/>, <paramref name="y"/>) de l'image <paramref name="frame"/> n'est pas transparent.</summary>
        public bool IsOpaque(int frame, int x, int y)
        {
            if (x < 0 || y < 0 || x >= FrameWidth || y >= FrameHeight) return false;
            if (mask == null) return true;
            Rectangle source = Source(frame);
            int width = FrameWidth * Frames, px = source.X + x;
            long bit = (long)y * width + px;
            return bit / 8 < mask.Length && (mask[bit / 8] & (1 << (int)(bit % 8))) != 0;
        }
    }

    /// <summary>Pose résolue d'un acteur : image (ou silhouette), retournement et diagnostic.</summary>
    public sealed class SpritePose
    {
        internal SpritePose(SpriteSheet sheet, bool mirrored, SpriteLoadState state, string reason, string animation)
        {
            Sheet = sheet; Mirrored = mirrored; State = state; Reason = reason; Animation = animation;
        }
        public SpriteSheet Sheet { get; }
        public bool Mirrored { get; }
        public SpriteLoadState State { get; }
        /// <summary>Repli utilisé ou cause de l'absence (null si le symbole demandé a été trouvé).</summary>
        public string Reason { get; }
        public string Animation { get; }
    }

    /// <summary>
    /// Sprites d'acteurs (<c>ressources/Bot/sprites</c>) pour une vue de la carte. Les PNG sont lus et décodés sur le pool
    /// de threads, jamais pendant un Paint : <see cref="Resolve"/> rend l'image si elle est prête, sinon l'état
    /// <see cref="SpriteLoadState.Loading"/> et lance la lecture ; <see cref="SheetsLoaded"/> est levé (hors du fil de
    /// l'interface) quand une image demandée arrive. Les images sont partagées entre les vues d'un même dossier par un cache
    /// à compteur de références (une vue garde ses images jusqu'à <see cref="ReleaseAll"/>) ; les images que plus aucune vue
    /// ne tient restent en cache jusqu'à 48 Mo, puis sont libérées de la plus ancienne à la plus récente.
    /// Nouveaux noms d'abord (<c>&lt;gfx&gt;_static&lt;O&gt;.png</c> + <c>ancres.tsv</c>, lot D2), anciens ensuite
    /// (<c>&lt;gfx&gt;&lt;O&gt;.png</c>), puis une autre orientation du même gfx, sinon silhouette.
    /// </summary>
    public sealed class ActorSprites : IDisposable
    {
        /// <summary>Images par seconde des clips de sprites (<c>preloader.swf</c> du client : 40).</summary>
        public const int FramesPerSecond = 40;
        private const long IdleBudget = 48L * 1024 * 1024;

        private static readonly Dictionary<string, Library> libraries = new Dictionary<string, Library>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string[]> Alternatives = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            // Orientation la plus proche d'abord ; toutes en dernier recours (un monstre n'a souvent que R et L, ou F seul).
            { "S", new[] { "R", "L", "F", "B" } }, { "R", new[] { "S", "F", "L", "B" } }, { "F", new[] { "R", "S", "L", "B" } },
            { "L", new[] { "B", "R", "S", "F" } }, { "B", new[] { "L", "S", "R", "F" } }
        };

        private readonly Library library;
        private readonly HashSet<Entry> held = new HashSet<Entry>();
        private bool disposed;

        public ActorSprites(string directory)
        {
            Directory = string.IsNullOrEmpty(directory) ? DefaultDirectory : directory;
            string key;
            try { key = Path.GetFullPath(Directory); } catch (Exception error) when (error is ArgumentException || error is NotSupportedException
                || error is PathTooLongException || error is System.Security.SecurityException) { key = Directory; }
            lock (libraries)
            {
                if (!libraries.TryGetValue(key, out library)) { library = new Library(Directory); libraries[key] = library; }
            }
        }

        public static string DefaultDirectory => Path.Combine(Path.GetDirectoryName(typeof(ActorSprites).Assembly.Location) ?? ".",
            "ressources", "Bot", "sprites");

        public string Directory { get; }

        /// <summary>Une image demandée par cette vue vient d'être chargée (ou s'est révélée absente). Levé sur le pool.</summary>
        public event EventHandler SheetsLoaded;

        /// <summary>Nombre d'images demandées par cette vue et encore en lecture.</summary>
        public int PendingCount
        {
            get { lock (library.Sync) { int count = 0; foreach (Entry entry in held) if (entry.State == SpriteLoadState.Loading) count++; return count; } }
        }

        /// <summary>Attend la fin des lectures demandées (tests, captures) ; faux si le délai expire.</summary>
        public bool WaitForPending(int milliseconds)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (PendingCount > 0)
            {
                if (clock.ElapsedMilliseconds > milliseconds) return false;
                Thread.Sleep(5);
            }
            return true;
        }

        /// <summary>
        /// Pose d'un acteur pour l'animation demandée (<c>static</c>, <c>walk</c>, <c>run</c>, <c>emote&lt;n&gt;</c>, <c>scene</c>).
        /// Ne lit jamais le disque. Une animation absente rend <see cref="SpriteLoadState.Missing"/> : l'appelant revient à
        /// <c>static</c>.
        /// </summary>
        public SpritePose Resolve(int gfx, int direction, bool noFlip, string animation = "static")
        {
            animation = string.IsNullOrEmpty(animation) ? "static" : animation;
            bool scene = animation == "scene";
            string suffix = scene ? string.Empty : ActorOrientation.Suffix(direction);
            bool mirrored = !scene && ActorOrientation.IsMirrored(direction, noFlip);
            if (gfx <= 0)
                return new SpritePose(null, mirrored, SpriteLoadState.Missing, "L'identifiant graphique de cet acteur n'a pas été transmis.", animation);
            if (disposed) return new SpritePose(null, mirrored, SpriteLoadState.Missing, "Vue de la carte fermée.", animation);
            SpriteLoadState state = Acquire(gfx.ToString(CultureInfo.InvariantCulture) + "|" + animation + "|" + suffix, gfx, animation, suffix,
                out LoadResult result);
            if (state == SpriteLoadState.Loading) return new SpritePose(null, mirrored, SpriteLoadState.Loading, null, animation);
            if (result?.Sheet == null) return new SpritePose(null, mirrored, SpriteLoadState.Missing, result?.Reason, animation);
            string reason = result.Reason;
            if (reason != null && !scene) reason = "Orientation " + ActorOrientation.Normalize(direction) + " : " + reason;
            return new SpritePose(result.Sheet, mirrored, SpriteLoadState.Ready, reason, animation);
        }

        /// <summary>Demande la lecture d'une pose sans attendre (préchargement depuis n'importe quel fil).</summary>
        public void Prefetch(int gfx, int direction, string animation = "static")
        {
            if (gfx > 0 && !disposed) Resolve(gfx, direction, false, animation);
        }

        /// <summary>Rend à la bibliothèque toutes les images tenues par cette vue (changement de carte).</summary>
        public void ReleaseAll()
        {
            lock (library.Sync)
            {
                foreach (Entry entry in held) library.Release(entry, this);
                held.Clear();
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            ReleaseAll();
            SheetsLoaded = null;
        }

        private SpriteLoadState Acquire(string key, int gfx, string animation, string suffix, out LoadResult result)
        {
            Entry entry; bool start = false; SpriteLoadState state;
            lock (library.Sync)
            {
                if (!library.Entries.TryGetValue(key, out entry))
                {
                    entry = new Entry(key);
                    library.Entries[key] = entry;
                    start = true;
                }
                if (held.Add(entry)) library.Use(entry);
                if (entry.State == SpriteLoadState.Loading && !entry.Waiters.Contains(this)) entry.Waiters.Add(this);
                state = entry.State;
                result = entry.Result;
            }
            if (start)
            {
                Library owner = library;
                Task.Factory.StartNew(() => owner.Complete(entry, owner.Load(gfx, animation, suffix)),
                    CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
            }
            return state;
        }

        private void OnLoaded()
        {
            if (disposed) return;
            try { SheetsLoaded?.Invoke(this, EventArgs.Empty); }
            catch (Exception error) when (!(error is OutOfMemoryException)) { /* un abonné défaillant n'arrête pas la lecture */ }
        }

        // ---------------------------------------------------------------- bibliothèque partagée

        private sealed class LoadResult
        {
            public SpriteSheet Sheet;
            public string Reason;
        }

        private sealed class Entry
        {
            public Entry(string key) { Key = key; }
            public readonly string Key;
            public SpriteLoadState State = SpriteLoadState.Loading;
            public LoadResult Result;
            public int References;
            public LinkedListNode<Entry> Idle;
            public List<ActorSprites> Waiters = new List<ActorSprites>();
        }

        private sealed class AnchorRow
        {
            public int XMin, YMin, Width, Height, Frames;
        }

        private sealed class Library
        {
            public readonly object Sync = new object();
            public readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
            private readonly LinkedList<Entry> idle = new LinkedList<Entry>();
            private readonly string directory;
            private readonly object anchorSync = new object();
            private Dictionary<string, AnchorRow> anchors;
            private long idleBytes;

            public Library(string directory) { this.directory = directory; }

            public void Use(Entry entry)
            {
                if (entry.Idle != null) { idle.Remove(entry.Idle); idleBytes -= Bytes(entry); entry.Idle = null; }
                entry.References++;
            }

            public void Release(Entry entry, ActorSprites view)
            {
                entry.Waiters?.Remove(view);
                if (--entry.References > 0) return;
                entry.Idle = idle.AddLast(entry);
                idleBytes += Bytes(entry);
                while (idleBytes > IdleBudget && idle.First != null)
                {
                    Entry oldest = idle.First.Value;
                    idle.RemoveFirst(); oldest.Idle = null;
                    idleBytes -= Bytes(oldest);
                    Entries.Remove(oldest.Key);
                    oldest.Result?.Sheet?.Image.Dispose();
                    oldest.Result = null;
                }
            }

            private static long Bytes(Entry entry) => entry.Result?.Sheet?.Bytes ?? 0;

            public void Complete(Entry entry, LoadResult result)
            {
                List<ActorSprites> waiters;
                lock (Sync)
                {
                    if (!Entries.TryGetValue(entry.Key, out Entry current) || current != entry)
                    {
                        // Évincée pendant la lecture : plus personne ne la tient.
                        result.Sheet?.Image.Dispose();
                        return;
                    }
                    entry.Result = result;
                    entry.State = result.Sheet != null ? SpriteLoadState.Ready : SpriteLoadState.Missing;
                    if (entry.Idle != null) idleBytes += Bytes(entry);
                    waiters = entry.Waiters;
                    entry.Waiters = null;
                }
                if (waiters != null) foreach (ActorSprites view in waiters) view.OnLoaded();
            }

            public LoadResult Load(int gfx, string animation, string suffix)
            {
                try { return LoadCore(gfx, animation, suffix); }
                catch (Exception error) when (!(error is ThreadAbortException))
                {
                    return new LoadResult { Reason = "Le sprite " + gfx + " est illisible : " + error.Message };
                }
            }

            private LoadResult LoadCore(int gfx, string animation, string suffix)
            {
                string id = gfx.ToString(CultureInfo.InvariantCulture);
                if (animation == "scene")
                {
                    SpriteSheet scene = ReadSheet(id, "scene", allowLegacy: false);
                    return scene != null ? new LoadResult { Sheet = scene }
                        : new LoadResult { Reason = "Le sprite " + gfx + " (scène) est absent du dossier des personnages/monstres." };
                }
                if (animation != "static")
                {
                    SpriteSheet strip = ReadSheet(id, animation + suffix, allowLegacy: false);
                    return strip != null ? new LoadResult { Sheet = strip }
                        : new LoadResult { Reason = "Animation " + animation + suffix + " absente pour le sprite " + gfx + "." };
                }
                string unreadable = null;
                foreach (string candidate in Candidates(suffix))
                {
                    SpriteSheet sheet;
                    try { sheet = ReadSheet(id, "static" + candidate, allowLegacy: true, legacySuffix: candidate); }
                    catch (InvalidDataException error) { unreadable = error.Message; continue; }
                    if (sheet == null) continue;
                    string reason = candidate == suffix ? null : "variante " + suffix + " absente, visuel " + candidate + " utilisé.";
                    return new LoadResult { Sheet = sheet, Reason = reason };
                }
                SpriteSheet fallback = ReadSheet(id, "scene", allowLegacy: false);
                if (fallback != null) return new LoadResult { Sheet = fallback, Reason = "aucune pose static, image de la scène utilisée." };
                return new LoadResult { Reason = unreadable ?? "Le sprite " + gfx + " est absent du dossier des personnages/monstres." };
            }

            private static IEnumerable<string> Candidates(string suffix)
            {
                yield return suffix;
                if (Alternatives.TryGetValue(suffix, out string[] others)) foreach (string other in others) yield return other;
            }

            /// <summary>Lit <c>&lt;gfx&gt;_&lt;anim&gt;.png</c> (ancre de <c>ancres.tsv</c>) puis, si permis, l'ancien <c>&lt;gfx&gt;&lt;O&gt;.png</c>.</summary>
            private SpriteSheet ReadSheet(string id, string anim, bool allowLegacy, string legacySuffix = null)
            {
                string file = id + "_" + anim + ".png";
                string path = Path.Combine(directory, file);
                if (File.Exists(path))
                {
                    Bitmap image = Decode(path, out byte[] mask);
                    if (image != null)
                    {
                        AnchorRow row = null;
                        Anchors().TryGetValue(id + "\t" + anim, out row);
                        if (row != null && row.Width > 0 && row.Height > 0)
                        {
                            int frames = Math.Max(1, row.Frames);
                            if (row.Width * frames != image.Width) frames = Math.Max(1, image.Width / row.Width);
                            return new SpriteSheet(image, frames, row.Width, Math.Min(row.Height, image.Height), row.XMin, row.YMin, file, false, mask);
                        }
                        Point foot = Foot(mask, image.Width, image.Height);
                        return new SpriteSheet(image, 1, image.Width, image.Height, -foot.X, -foot.Y, file, true, mask);
                    }
                }
                if (!allowLegacy) return null;
                file = id + legacySuffix + ".png";
                path = Path.Combine(directory, file);
                if (!File.Exists(path)) return null;
                Bitmap legacy = Decode(path, out byte[] legacyMask);
                if (legacy == null) throw new InvalidDataException("Le PNG du sprite " + id + legacySuffix + " est illisible.");
                Point anchor = Foot(legacyMask, legacy.Width, legacy.Height);
                return new SpriteSheet(legacy, 1, legacy.Width, legacy.Height, -anchor.X, -anchor.Y, file, true, legacyMask);
            }

            /// <summary>Pied d'un PNG sans ancre : milieu des colonnes visibles, sous la dernière ligne visible.</summary>
            private static Point Foot(byte[] mask, int width, int height)
            {
                int left = width, right = -1, bottom = -1;
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        long bit = (long)y * width + x;
                        if ((mask[bit / 8] & (1 << (int)(bit % 8))) == 0) continue;
                        if (x < left) left = x;
                        if (x > right) right = x;
                        bottom = y;
                    }
                return right < 0 ? new Point(width / 2, height) : new Point((left + right + 1) / 2, bottom + 1);
            }

            private static Bitmap Decode(string path, out byte[] mask)
            {
                mask = null;
                Bitmap copy = null;
                try
                {
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var source = System.Drawing.Image.FromStream(stream))
                    {
                        if (source.Width < 1 || source.Height < 1 || (long)source.Width * source.Height > 16L * 1024 * 1024) return null;
                        copy = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
                        using (var graphics = Graphics.FromImage(copy))
                        {
                            graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                            graphics.DrawImage(source, 0, 0, source.Width, source.Height);
                        }
                    }
                    mask = AlphaMask(copy);
                    return copy;
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException
                    || error is ExternalException || error is OutOfMemoryException || error is NotSupportedException)
                {
                    copy?.Dispose();
                    mask = null;
                    return null;
                }
            }

            private static byte[] AlphaMask(Bitmap image)
            {
                int width = image.Width, height = image.Height;
                var mask = new byte[(int)(((long)width * height + 7) / 8)];
                BitmapData data = image.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    var row = new int[width];
                    for (int y = 0; y < height; y++)
                    {
                        Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, width);
                        for (int x = 0; x < width; x++)
                        {
                            if ((row[x] >> 24 & 0xFF) < 16) continue;
                            long bit = (long)y * width + x;
                            mask[bit / 8] |= (byte)(1 << (int)(bit % 8));
                        }
                    }
                }
                finally { image.UnlockBits(data); }
                return mask;
            }

            private Dictionary<string, AnchorRow> Anchors()
            {
                lock (anchorSync)
                {
                    if (anchors != null) return anchors;
                    var rows = new Dictionary<string, AnchorRow>(StringComparer.Ordinal);
                    string path = Path.Combine(directory, "ancres.tsv");
                    try
                    {
                        if (File.Exists(path))
                            foreach (string raw in File.ReadAllLines(path))
                            {
                                // ancres.tsv peut être extrait en CRLF sous Windows (.gitattributes « text=auto »).
                                string line = raw.TrimEnd('\r');
                                if (line.Length == 0 || line[0] == '#') continue;
                                string[] fields = line.Split('\t');
                                if (fields.Length < 7) continue;
                                if (!int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int xMin)
                                    || !int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int yMin)
                                    || !int.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int width)
                                    || !int.TryParse(fields[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int height)
                                    || !int.TryParse(fields[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int frames)) continue;
                                rows[fields[0] + "\t" + fields[1]] = new AnchorRow { XMin = xMin, YMin = yMin, Width = width, Height = height, Frames = frames };
                            }
                    }
                    catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException
                        || error is NotSupportedException) { /* ancres illisibles : chaque PNG est ancré sur ses pixels */ }
                    anchors = rows;
                    return anchors;
                }
            }
        }
    }
}
