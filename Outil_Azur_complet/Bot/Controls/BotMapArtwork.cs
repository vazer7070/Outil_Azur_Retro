using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Utils.Crypto;
using Tool_Editor.maps.data;
using Map = Tool_BotProtocol.Game.Maps.Map;

namespace Outil_Azur_complet.Bot.Controls
{
    /// <summary>
    /// Owned, portable PNG scenery decoded from the same map data used by the client.
    /// Cells are decoded by the constructor (a few string operations); every PNG is read, transformed and
    /// measured by a background task, then published at once on the creator's synchronization context
    /// (<see cref="AssetsLoaded"/>). Pictures are shared between maps through a reference-counted cache.
    /// </summary>
    public sealed class BotMapArtwork : IDisposable
    {
        // ank.battlefield.Constants of the client: CELL_WIDTH 53, CELL_HEIGHT 27 (rows every 13.5 px), LEVEL_HEIGHT 20.
        public const float CellWidth = 53f;
        public const float CellHeight = 27f;
        public const float LevelHeight = 20f;
        public const string AnchorFileName = "ancres.tsv";
        /// <summary>World position of the client's map origin (cell 0 at level 7), where the client attaches backgroundNum.</summary>
        public static readonly PointF ClientOrigin = new PointF(CellWidth / 2, CellHeight / 2);
        // MapHandler.build: a quarter-turned ground or object1 is rescaled so its diamond still covers the cell.
        private const float QuarterTurnScaleX = .5185f, QuarterTurnScaleY = 1.9286f;
        private const int MaxSlopeFrame = 15;

        private static readonly ConcurrentDictionary<string, Lazy<AssetLibrary>> libraries =
            new ConcurrentDictionary<string, Lazy<AssetLibrary>>(StringComparer.OrdinalIgnoreCase);
        private readonly object gate = new object();
        private readonly SynchronizationContext context;
        private readonly int ownerThreadId;
        private readonly int backgroundId;
        private readonly ManualResetEventSlim publishedSignal = new ManualResetEventSlim(false);
        private readonly List<PictureCache.Entry> acquired = new List<PictureCache.Entry>();
        private readonly HashSet<string> missing = new HashSet<string>();
        private readonly Task loader;
        private Request[] requests = new Request[0];
        private Request background;
        private RectangleF gridBounds;
        private LoadResult result;
        private int publishState;
        private volatile bool ready;
        private volatile bool disposed;
        private volatile int loadThreadId;
        private int rejectedAnchorLines;
        private bool resolved;
        private AssetLibrary owner;
        private DepthLayer[] depthLayers = new DepthLayer[0];

        public ArtworkCell[] Cells { get; private set; } = new ArtworkCell[0];
        public RectangleF WorldBounds { get; private set; }
        /// <summary>True once the PNG of this map have been read and published (or could not be).</summary>
        public bool AssetsReady => ready;
        /// <summary>Managed thread that read the PNG files (never the thread that built the artwork).</summary>
        public int LoadThreadId => loadThreadId;
        /// <summary>Raised once, on the creator's synchronization context when it has one, after the PNG are published.</summary>
        public event EventHandler AssetsLoaded;
        public int MissingAssetCount { get { lock (gate) return missing.Count; } }
        // requests holds every distinct picture of the map, the background included.
        public int LoadedAssetCount => ready ? requests.Count(request => request.Picture != null) : 0;
        /// <summary>Pictures placed with the registration point read from ancres.tsv (the others use the legacy heuristic).</summary>
        public int AnchoredAssetCount => ready ? requests.Count(request => request.Picture?.Anchored == true) : 0;
        public int RejectedAnchorLines => rejectedAnchorLines;
        public string Status
        {
            get
            {
                if (!ready) return "Chargement du décor…";
                lock (gate)
                {
                    if (missing.Count == 0)
                        return LoadedAssetCount == 0 ? "Aucun visuel dans cette carte · vue des cellules" : "Décor chargé · " + LoadedAssetCount + " visuels";
                    return missing.Count + " visuel(s) absent(s) · " + string.Join(", ", missing.Take(4)) + (missing.Count > 4 ? "…" : "");
                }
            }
        }

        public sealed class ArtworkCell
        {
            public int Id, GroundId, Object1Id, Object2Id, GroundRotation, Object1Rotation, Level, Slope;
            public bool Active, GroundFlip, Object1Flip, Object2Flip;
            /// <summary>Client cell position (registration point of its ground and objects), shifted by <see cref="ClientOrigin"/>.</summary>
            public PointF Center;
            public PointF[] Polygon;
            internal Request Ground, Object1, Object2;
        }

        public sealed class DepthLayer
        {
            public float Depth;
            public int Order;
            public Action<Graphics> Draw;
        }

        internal enum DecorKind { Ground = 0, Object = 1, Background = 2 }

        internal sealed class Request
        {
            public DecorKind Kind;
            public string Label, Path, CacheKey;
            public bool Anchored, Flip;
            public int Rotation;
            public RectangleF Anchor;
            public TilePicture Picture;
        }

        internal sealed class TilePicture : IDisposable
        {
            public Bitmap Image;
            /// <summary>Top-left corner of <see cref="Image"/> relative to the registration point.</summary>
            public PointF Offset;
            /// <summary>Visible pixels relative to the registration point.</summary>
            public RectangleF Visible;
            public bool Anchored, Stretch;
            public long Bytes;
            public RectangleF At(PointF position) => new RectangleF(position.X + Offset.X, position.Y + Offset.Y, Image.Width, Image.Height);
            public RectangleF VisibleAt(PointF position) => new RectangleF(position.X + Visible.X, position.Y + Visible.Y, Visible.Width, Visible.Height);
            public void Dispose() { Image?.Dispose(); Image = null; }
        }

        private sealed class LoadResult
        {
            public readonly List<string> Missing = new List<string>();
            public readonly Dictionary<Request, PictureCache.Entry> Entries = new Dictionary<Request, PictureCache.Entry>();
        }

        // ---- library: file index + ancres.tsv, built once per directory away from the UI thread ----

        private sealed class AssetLibrary
        {
            public readonly string Directory;
            public readonly Dictionary<long, string> Files = new Dictionary<long, string>();
            public readonly HashSet<long> Generated = new HashSet<long>();
            public readonly Dictionary<long, RectangleF> Anchors = new Dictionary<long, RectangleF>();
            public readonly PictureCache Cache = new PictureCache();
            public int RejectedAnchorLines;

            public AssetLibrary(string directory)
            {
                Directory = directory;
                Index(DecorKind.Ground); Index(DecorKind.Object); Index(DecorKind.Background);
                ReadAnchors(System.IO.Path.Combine(directory, AnchorFileName));
            }

            private void Index(DecorKind kind)
            {
                string folder = System.IO.Path.Combine(Directory, Folder(kind));
                if (!System.IO.Directory.Exists(folder)) return;
                string[] paths;
                try
                {
                    paths = System.IO.Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { return; }
                foreach (string path in paths)
                {
                    if (!new[] { ".png", ".jpg", ".jpeg", ".bmp" }.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) continue;
                    if (!TryParseName(System.IO.Path.GetFileNameWithoutExtension(path), out int id, out int frame)) continue;
                    long key = Key(kind, id, frame);
                    // Files written by exporter_decor.py sit directly in the folder and own their anchors;
                    // older libraries sorted in sub-folders only fill the identifiers the export lacks.
                    bool generated = string.Equals(System.IO.Path.GetDirectoryName(path), folder, StringComparison.OrdinalIgnoreCase);
                    if (!generated && Generated.Contains(key)) continue;
                    Files[key] = path;
                    if (generated) Generated.Add(key);
                }
            }

            private void ReadAnchors(string path)
            {
                if (!File.Exists(path)) return;
                IEnumerable<string> lines;
                try { lines = File.ReadAllLines(path); }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { RejectedAnchorLines++; return; }
                foreach (string raw in lines)
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    string[] fields = line.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (fields.Length > 0 && (fields[0] == "selection" || fields[0] == "interaction")) continue;
                    if (fields.Length < 6 || !TryKind(fields[0], out DecorKind kind)
                        || !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) || id < 0
                        || !float.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) || !Plausible(x)
                        || !float.TryParse(fields[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) || !Plausible(y)
                        || !float.TryParse(fields[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float width) || width <= 0 || !Plausible(width)
                        || !float.TryParse(fields[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float height) || height <= 0 || !Plausible(height))
                    {
                        RejectedAnchorLines++;
                        continue;
                    }
                    int frame = 1;
                    if (fields.Length > 6 && (!int.TryParse(fields[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out frame) || frame < 1 || frame > 255))
                    {
                        RejectedAnchorLines++;
                        continue;
                    }
                    Anchors[Key(kind, id, frame)] = new RectangleF(x, y, width, height);
                }
            }

            /// <summary>Resolves a picture like the client: frame n of a ground (slope), else its last exported frame,
            /// and the ground library for a background (both live in g1/g2.swf) or the other way round.</summary>
            public bool TryResolve(DecorKind kind, int id, int frame, out long key)
            {
                foreach (DecorKind candidate in kind == DecorKind.Object ? new[] { kind }
                    : new[] { kind, kind == DecorKind.Ground ? DecorKind.Background : DecorKind.Ground })
                    for (int current = Math.Max(1, frame); current >= 1; current--)
                        if (Files.ContainsKey(key = Key(candidate, id, current))) return true;
                key = 0;
                return false;
            }

            // NaN, infinities and absurd sizes would poison the fitted bounds: the largest client picture is about 1 300 px.
            private static bool Plausible(float value) => !float.IsNaN(value) && Math.Abs(value) <= 100000f;

            private static bool TryParseName(string name, out int id, out int frame)
            {
                frame = 1;
                int separator = name.IndexOf('_');
                if (separator < 0) return int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id >= 0;
                return int.TryParse(name.Substring(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out id)
                    && int.TryParse(name.Substring(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out frame)
                    && frame >= 1 && frame <= 255;
            }

            private static bool TryKind(string text, out DecorKind kind)
            {
                switch (text)
                {
                    case "sol": kind = DecorKind.Ground; return true;
                    case "objet": kind = DecorKind.Object; return true;
                    case "fond": kind = DecorKind.Background; return true;
                    default: kind = DecorKind.Ground; return false;
                }
            }
        }

        private static long Key(DecorKind kind, int id, int frame) => ((long)kind << 40) | ((long)(uint)id << 8) | (uint)(frame & 255);
        private static DecorKind KindOf(long key) => (DecorKind)(key >> 40);
        private static string Folder(DecorKind kind) => kind == DecorKind.Ground ? "sols" : kind == DecorKind.Object ? "objets" : "backgrounds";
        private static string Label(DecorKind kind) => kind == DecorKind.Ground ? "sol" : kind == DecorKind.Object ? "objet" : "fond";

        private static string ResourcePath(string directory) => Path.GetFullPath(directory ?? Path.Combine(
            Path.GetDirectoryName(typeof(BotMapArtwork).Assembly.Location), "ressources", "maps"));
        private static Lazy<AssetLibrary> LibraryEntry(string directory) =>
            libraries.GetOrAdd(ResourcePath(directory), location => new Lazy<AssetLibrary>(() => new AssetLibrary(location)));

        // The resource loader can do the one-time directory scan away from the UI thread.
        public static Task WarmupAsync(string resourceDirectory = null) => Task.Run(() => LibraryEntry(resourceDirectory).Value);
        public static void InvalidateAssetIndex(string resourceDirectory = null)
        {
            if (libraries.TryRemove(ResourcePath(resourceDirectory), out Lazy<AssetLibrary> removed) && removed.IsValueCreated)
                removed.Value.Cache.Retire();
        }

        // ---- shared, reference-counted picture cache ----

        private sealed class PictureCache
        {
            private const long IdleBudget = 64L * 1024 * 1024;
            private readonly object sync = new object();
            private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
            private readonly LinkedList<Entry> idle = new LinkedList<Entry>();
            private long idleBytes;
            private bool retired;

            public sealed class Entry
            {
                public string Key;
                public TilePicture Picture;
                public string Error;
                public int References;
                public LinkedListNode<Entry> Idle;
            }

            public Entry Acquire(string key, Func<Entry> load)
            {
                lock (sync)
                    if (!retired && entries.TryGetValue(key, out Entry cached)) { Use(cached); return cached; }
                Entry created = load();
                created.Key = key;
                lock (sync)
                {
                    if (!retired && entries.TryGetValue(key, out Entry raced))
                    {
                        created.Picture?.Dispose();
                        Use(raced);
                        return raced;
                    }
                    created.References = 1;
                    if (!retired) entries[key] = created;
                    return created;
                }
            }

            private void Use(Entry entry)
            {
                if (entry.Idle != null) { idle.Remove(entry.Idle); idleBytes -= entry.Picture?.Bytes ?? 0; entry.Idle = null; }
                entry.References++;
            }

            public void Release(Entry entry)
            {
                if (entry == null) return;
                lock (sync)
                {
                    if (--entry.References > 0) return;
                    if (retired || !entries.TryGetValue(entry.Key, out Entry current) || current != entry) { entry.Picture?.Dispose(); return; }
                    entry.Idle = idle.AddLast(entry);
                    idleBytes += entry.Picture?.Bytes ?? 0;
                    while (idleBytes > IdleBudget && idle.First != null)
                    {
                        Entry oldest = idle.First.Value;
                        idle.RemoveFirst(); oldest.Idle = null;
                        idleBytes -= oldest.Picture?.Bytes ?? 0;
                        entries.Remove(oldest.Key);
                        oldest.Picture?.Dispose();
                    }
                }
            }

            public void Retire()
            {
                lock (sync)
                {
                    retired = true;
                    foreach (Entry entry in idle) { entries.Remove(entry.Key); entry.Picture?.Dispose(); entry.Idle = null; }
                    idle.Clear(); idleBytes = 0;
                }
            }
        }

        // ---- construction ----

        public BotMapArtwork(Map map, string resourceDirectory = null)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            context = SynchronizationContext.Current;
            ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            backgroundId = map.Back_ID;
            if (map.MapWidth >= 2 && !string.IsNullOrEmpty(map.MapData) && map.MapData.Length % 10 == 0)
                DecodeCells(map);
            Lazy<AssetLibrary> library = LibraryEntry(resourceDirectory);
            // An index already built (normal case: LoadingBotForm warms it) resolves paths and anchors right now,
            // so the fitted bounds are known before any PNG is read. Otherwise the loader builds the index too.
            if (library.IsValueCreated)
            {
                Resolve(library.Value);
                resolved = true;
                WorldBounds = Bounds(Cells);
            }
            loader = Task.Run(() => LoadAssets(library));
        }

        private void DecodeCells(Map map)
        {
            if (map.MapData.Any(character => Hash.get_Hash(character) < 0))
                throw new FormatException("Les données visuelles de la carte sont invalides.");
            var cells = new ArtworkCell[map.MapData.Length / 10];
            int period = 2 * map.MapWidth - 1;
            for (int id = 0; id < cells.Length; id++)
            {
                int[] value = map.MapData.Substring(id * 10, 10).Select(character => (int)Hash.get_Hash(character)).ToArray();
                var cell = new ArtworkCell
                {
                    Id = id, Active = (value[0] & 32) != 0, Level = value[1] & 15,
                    Slope = (value[4] & 60) >> 2, GroundRotation = (value[1] & 48) >> 4,
                    GroundId = ((value[0] & 24) << 6) + ((value[2] & 7) << 6) + value[3],
                    GroundFlip = (value[4] & 2) != 0,
                    Object1Id = ((value[0] & 4) << 11) + ((value[4] & 1) << 12) + (value[5] << 6) + value[6],
                    Object1Rotation = (value[7] & 48) >> 4, Object1Flip = (value[7] & 8) != 0,
                    Object2Id = ((value[0] & 2) << 12) + ((value[7] & 1) << 12) + (value[8] << 6) + value[9],
                    Object2Flip = (value[7] & 4) != 0
                };
                int within = id % period, row = 2 * (id / period) + (within >= map.MapWidth ? 1 : 0);
                int column = within >= map.MapWidth ? within - map.MapWidth : within;
                float x = (column + (row % 2 == 0 ? .5f : 1f)) * CellWidth;
                float y = (row + 1) * CellHeight / 2;
                // Level 7 is the client's flat reference. The polygon and visual use the same elevation.
                if (cell.Active) y -= (cell.Level - 7) * LevelHeight;
                cell.Center = new PointF(x, y);
                cell.Polygon = Diamond(cell.Center);
                cells[id] = cell;
            }
            float gridHeight = (2 * ((cells.Length - 1) / period) + ((cells.Length - 1) % period >= map.MapWidth ? 2 : 1) + 1) * CellHeight / 2;
            gridBounds = new RectangleF(0, 0, map.MapWidth * CellWidth, gridHeight);
            Cells = cells;
            WorldBounds = Bounds(cells);
            var layers = new List<DepthLayer>();
            foreach (ArtworkCell cell in cells)
                if (cell.Active && cell.Object2Id != 0)
                    layers.Add(new DepthLayer { Depth = cell.Center.Y, Order = 2, Draw = graphics => DrawPicture(graphics, cell.Object2?.Picture, cell.Center) });
            depthLayers = layers.OrderBy(layer => layer.Depth).ThenBy(layer => layer.Order).ToArray();
        }

        /// <summary>Builds the picture requests of every cell with the client's rules (MapHandler.build):
        /// inactive cells show nothing; a sloped ground shows frame <c>groundSlope</c> and never turns;
        /// ground and object1 turn by quarters only on flat cells; object2 is only mirrored.</summary>
        private void Resolve(AssetLibrary library)
        {
            var unique = new Dictionary<string, Request>(StringComparer.Ordinal);
            var absent = new List<string>();
            Request Make(DecorKind kind, int id, int frame, bool flip, int rotation)
            {
                if (id <= 0) return null;
                if (!library.TryResolve(kind, id, frame, out long fileKey))
                {
                    absent.Add(Label(kind) + " " + id);
                    return null;
                }
                string path = library.Files[fileKey];
                bool anchored = library.Generated.Contains(fileKey) && library.Anchors.ContainsKey(fileKey);
                string cacheKey = path + "|" + (flip ? 1 : 0) + "|" + rotation;
                if (unique.TryGetValue(cacheKey, out Request existing)) return existing;
                var request = new Request
                {
                    Kind = KindOf(fileKey), Label = Label(kind) + " " + id, Path = path, CacheKey = cacheKey,
                    Flip = flip, Rotation = rotation, Anchored = anchored,
                    Anchor = anchored ? library.Anchors[fileKey] : RectangleF.Empty
                };
                unique[cacheKey] = request;
                return request;
            }
            foreach (ArtworkCell cell in Cells)
            {
                if (!cell.Active) continue;
                bool flat = cell.Slope == 1;
                int groundFrame = cell.Slope >= 2 && cell.Slope <= MaxSlopeFrame ? cell.Slope : 1;
                cell.Ground = Make(DecorKind.Ground, cell.GroundId, groundFrame, cell.GroundFlip, flat ? cell.GroundRotation : 0);
                cell.Object1 = Make(DecorKind.Object, cell.Object1Id, 1, cell.Object1Flip, flat ? cell.Object1Rotation : 0);
                cell.Object2 = Make(DecorKind.Object, cell.Object2Id, 1, cell.Object2Flip, 0);
            }
            Request backgroundRequest = backgroundId > 0 ? Make(DecorKind.Background, backgroundId, 1, false, 0) : null;
            lock (gate)
            {
                rejectedAnchorLines = library.RejectedAnchorLines;
                requests = unique.Values.ToArray();
                background = backgroundRequest;
                foreach (string label in absent) missing.Add(label);
            }
        }

        // ---- background loading ----

        private void LoadAssets(Lazy<AssetLibrary> libraryEntry)
        {
            loadThreadId = Thread.CurrentThread.ManagedThreadId;
            var loaded = new LoadResult();
            try
            {
                AssetLibrary library = libraryEntry.Value;
                lock (gate) owner = library;
                if (!resolved && !disposed) Resolve(library);
                foreach (Request request in requests)
                {
                    if (disposed) break;
                    string cacheKey = request.CacheKey + "|" + Stamp(request.Path);
                    PictureCache.Entry entry = library.Cache.Acquire(cacheKey, () => LoadPicture(request));
                    lock (gate)
                    {
                        if (disposed) { library.Cache.Release(entry); break; }
                        acquired.Add(entry);
                    }
                    loaded.Entries[request] = entry;
                    if (entry.Picture == null) loaded.Missing.Add(request.Label + " illisible");
                }
            }
            catch (Exception error)
            {
                // The worker never faults: the status shows the reason and the cells keep their flat colours.
                loaded.Missing.Add("décor illisible (" + error.GetType().Name + ")");
            }
            lock (gate) result = loaded;
            SynchronizationContext target = context;
            if (target == null) { Publish(); return; }
            try { target.Post(state => ((BotMapArtwork)state).Publish(), this); }
            catch (Exception) { Publish(); } // context gone (window closed): publish here, nobody draws anymore
        }

        private static long Stamp(string path)
        {
            try { return File.GetLastWriteTimeUtc(path).Ticks ^ new FileInfo(path).Length; }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException) { return 0; }
        }

        private static PictureCache.Entry LoadPicture(Request request)
        {
            Bitmap source = null;
            try
            {
                using (var stream = new FileStream(request.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var decoded = Image.FromStream(stream)) source = new Bitmap(decoded);
                return new PictureCache.Entry { Picture = Transform(request, source) };
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException
                || error is ExternalException || error is OutOfMemoryException || error is InvalidOperationException)
            {
                return new PictureCache.Entry { Error = error.Message };
            }
            finally { source?.Dispose(); }
        }

        /// <summary>Flash matrix of a decor clip (x' = a·x + c·y, y' = b·x + d·y): _rotation = quarter × 90,
        /// _xscale 51.85 / _yscale 192.86 on odd quarters, then _xscale negated by the flip bit.</summary>
        private static float[] FlashMatrix(bool flip, int rotation)
        {
            float scaleX = rotation % 2 != 0 ? QuarterTurnScaleX : 1f, scaleY = rotation % 2 != 0 ? QuarterTurnScaleY : 1f;
            if (flip) scaleX = -scaleX;
            int quarter = ((rotation % 4) + 4) % 4;
            float cos = quarter == 0 ? 1 : quarter == 2 ? -1 : 0, sin = quarter == 1 ? 1 : quarter == 3 ? -1 : 0;
            return new[] { scaleX * cos, scaleX * sin, -scaleY * sin, scaleY * cos };
        }

        private static RectangleF TransformBounds(RectangleF local, float[] m)
        {
            float[] xs = new float[4], ys = new float[4];
            int i = 0;
            foreach (PointF point in new[] { new PointF(local.Left, local.Top), new PointF(local.Right, local.Top),
                new PointF(local.Left, local.Bottom), new PointF(local.Right, local.Bottom) })
            {
                xs[i] = m[0] * point.X + m[2] * point.Y; ys[i] = m[1] * point.X + m[3] * point.Y; i++;
            }
            return RectangleF.FromLTRB(xs.Min(), ys.Min(), xs.Max(), ys.Max());
        }

        /// <summary>Visible rectangle of a picture relative to its cell, known from ancres.tsv before the PNG is read.</summary>
        private static RectangleF AnchoredVisible(Request request) => TransformBounds(request.Anchor, FlashMatrix(request.Flip, request.Rotation));

        private static TilePicture Transform(Request request, Bitmap source)
        {
            // ancres.tsv describes the exported PNG: ignore it if the file on disk was replaced by another size.
            bool anchored = request.Anchored && Math.Abs(request.Anchor.Width - source.Width) < 1.5f && Math.Abs(request.Anchor.Height - source.Height) < 1.5f;
            if (request.Kind == DecorKind.Background && !anchored)
            {
                // Legacy background library (no registration point): stretched over the cell grid as before.
                Bitmap copy = new Bitmap(source);
                return new TilePicture { Image = copy, Stretch = true, Bytes = 4L * copy.Width * copy.Height };
            }
            RectangleF local;
            if (anchored) local = new RectangleF(request.Anchor.X, request.Anchor.Y, source.Width, source.Height);
            else
            {
                var tile = new TilesData(0, request.Path, "", request.Kind == DecorKind.Ground ? TilesData.TileType.ground : TilesData.TileType.objet);
                Point anchor = TilesData.Anchor(tile, source);
                local = new RectangleF(-anchor.X, -anchor.Y, source.Width, source.Height);
            }
            float[] m = FlashMatrix(request.Flip, request.Rotation);
            Bitmap image;
            PointF offset;
            int quarter = ((request.Rotation % 4) + 4) % 4;
            if (quarter % 2 == 0)
            {
                // Mirror and half-turn are exact pixel permutations.
                image = new Bitmap(source);
                bool mirrorX = m[0] < 0, mirrorY = m[3] < 0;
                if (mirrorX && mirrorY) image.RotateFlip(RotateFlipType.Rotate180FlipNone);
                else if (mirrorX) image.RotateFlip(RotateFlipType.RotateNoneFlipX);
                else if (mirrorY) image.RotateFlip(RotateFlipType.RotateNoneFlipY);
                offset = new PointF(mirrorX ? -local.Right : local.Left, mirrorY ? -local.Bottom : local.Top);
            }
            else
            {
                RectangleF bounds = TransformBounds(local, m);
                float left = (float)Math.Floor(bounds.Left), top = (float)Math.Floor(bounds.Top);
                int width = Math.Max(1, (int)Math.Ceiling(bounds.Right - left)), height = Math.Max(1, (int)Math.Ceiling(bounds.Bottom - top));
                image = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                try
                {
                    using (Graphics graphics = Graphics.FromImage(image))
                    using (var matrix = new Matrix(m[0], m[1], m[2], m[3], -left, -top))
                    {
                        graphics.Clear(Color.Transparent);
                        graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        graphics.PixelOffsetMode = PixelOffsetMode.Half;
                        graphics.Transform = matrix;
                        graphics.DrawImage(source, local);
                    }
                }
                catch { image.Dispose(); throw; }
                offset = new PointF(left, top);
            }
            RectangleF visible;
            if (anchored) visible = TransformBounds(local, m);
            else
            {
                Rectangle pixels = VisiblePixels(image);
                visible = pixels.IsEmpty ? RectangleF.Empty : new RectangleF(offset.X + pixels.X, offset.Y + pixels.Y, pixels.Width, pixels.Height);
            }
            return new TilePicture { Image = image, Offset = offset, Visible = visible, Anchored = anchored, Bytes = 4L * image.Width * image.Height };
        }

        private static Rectangle VisiblePixels(Bitmap image)
        {
            var bounds = new Rectangle(0, 0, image.Width, image.Height);
            BitmapData locked = image.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = Math.Abs(locked.Stride);
                byte[] row = new byte[stride];
                int left = image.Width, right = -1, top = image.Height, bottom = -1;
                for (int y = 0; y < image.Height; y++)
                {
                    Marshal.Copy(IntPtr.Add(locked.Scan0, y * locked.Stride), row, 0, stride);
                    for (int x = 0; x < image.Width; x++)
                        if (row[x * 4 + 3] != 0)
                        {
                            left = Math.Min(left, x); right = Math.Max(right, x);
                            top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                        }
                }
                return right < 0 ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
            }
            finally { image.UnlockBits(locked); }
        }

        private void Publish()
        {
            if (Interlocked.Exchange(ref publishState, 1) != 0) return;
            lock (gate)
            {
                if (!disposed && result != null)
                {
                    foreach (KeyValuePair<Request, PictureCache.Entry> pair in result.Entries) pair.Key.Picture = pair.Value.Picture;
                    foreach (string label in result.Missing) missing.Add(label);
                    WorldBounds = Bounds(Cells);
                }
                ready = true;
            }
            publishedSignal.Set();
            if (!disposed) AssetsLoaded?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Waits for the background loading; on the creator's thread it also publishes a pending result
        /// (whose posted callback cannot run while that thread waits). Returns false on timeout.</summary>
        public bool WaitForAssets(int millisecondsTimeout = Timeout.Infinite)
        {
            if (ready) return true;
            if (Thread.CurrentThread.ManagedThreadId == ownerThreadId)
            {
                if (!loader.Wait(millisecondsTimeout)) return false;
                Publish();
                return true;
            }
            return publishedSignal.Wait(millisecondsTimeout);
        }

        // A loaded picture knows its visible pixels (and whether its anchor still matched the PNG); before loading,
        // only anchored pictures contribute, from ancres.tsv.
        private RectangleF Bounds(ArtworkCell[] cells)
        {
            RectangleF bounds = gridBounds;
            foreach (ArtworkCell cell in cells)
            {
                bounds = RectangleF.Union(bounds, Bounds(cell.Polygon));
                foreach (Request request in new[] { cell.Ground, cell.Object1, cell.Object2 })
                {
                    if (request == null) continue;
                    RectangleF? visible = request.Picture != null ? request.Picture.Visible : request.Anchored ? AnchoredVisible(request) : (RectangleF?)null;
                    if (visible.HasValue && !visible.Value.IsEmpty)
                        bounds = RectangleF.Union(bounds, new RectangleF(cell.Center.X + visible.Value.X, cell.Center.Y + visible.Value.Y,
                            visible.Value.Width, visible.Value.Height));
                }
            }
            return bounds;
        }

        public static PointF[] Diamond(PointF center) => new[]
        {
            new PointF(center.X - CellWidth / 2, center.Y), new PointF(center.X, center.Y - CellHeight / 2),
            new PointF(center.X + CellWidth / 2, center.Y), new PointF(center.X, center.Y + CellHeight / 2)
        };

        private static RectangleF Bounds(PointF[] points) => RectangleF.FromLTRB(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y));

        // ---- drawing (UI thread) ----

        /// <summary>Background, grounds and object1 layers: in the client these containers lie under the grid and every sprite.</summary>
        public void DrawGround(Graphics graphics)
        {
            TilePicture back = background?.Picture;
            if (back?.Image != null)
            {
                if (back.Stretch) graphics.DrawImage(back.Image, gridBounds);
                else DrawPicture(graphics, back, ClientOrigin);
            }
            foreach (ArtworkCell cell in Cells) DrawPicture(graphics, cell.Ground?.Picture, cell.Center);
            foreach (ArtworkCell cell in Cells) DrawPicture(graphics, cell.Object1?.Picture, cell.Center);
        }

        /// <summary>Object2 layer in cell order, with an optional callback before each cell's object.</summary>
        public void DrawObjects(Graphics graphics, Action<ArtworkCell> drawEntity)
        {
            foreach (ArtworkCell cell in Cells)
            {
                drawEntity?.Invoke(cell);
                DrawPicture(graphics, cell.Object2?.Picture, cell.Center);
            }
        }

        public void DrawDepthScene(Graphics graphics, IEnumerable<DepthLayer> actors)
        {
            DepthLayer[] dynamicLayers = (actors ?? Enumerable.Empty<DepthLayer>()).OrderBy(layer => layer.Depth).ThenBy(layer => layer.Order).ToArray();
            int actorIndex = 0;
            foreach (DepthLayer layer in depthLayers)
            {
                while (actorIndex < dynamicLayers.Length && (dynamicLayers[actorIndex].Depth < layer.Depth
                    || (dynamicLayers[actorIndex].Depth == layer.Depth && dynamicLayers[actorIndex].Order < layer.Order)))
                    dynamicLayers[actorIndex++].Draw?.Invoke(graphics);
                layer.Draw?.Invoke(graphics);
            }
            while (actorIndex < dynamicLayers.Length) dynamicLayers[actorIndex++].Draw?.Invoke(graphics);
        }

        private static void DrawPicture(Graphics graphics, TilePicture picture, PointF position)
        {
            Bitmap image = picture?.Image;
            if (image == null) return;
            if (picture.VisibleAt(position).IntersectsWith(graphics.ClipBounds)) graphics.DrawImage(image, picture.At(position));
        }

        public bool HasGroundArtwork(int id) => id >= 0 && id < Cells.Length
            && (Cells[id].Ground?.Picture != null || Cells[id].Object1?.Picture != null || background?.Picture != null);

        public void Dispose()
        {
            PictureCache.Entry[] release;
            AssetLibrary library;
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                release = acquired.ToArray();
                acquired.Clear();
                foreach (Request request in requests) request.Picture = null;
                Cells = new ArtworkCell[0]; depthLayers = new DepthLayer[0];
                library = owner;
            }
            // Pictures go back to the shared cache: another map may still draw them, the cache frees the rest.
            if (library != null)
                foreach (PictureCache.Entry entry in release) library.Cache.Release(entry);
            publishedSignal.Set();
        }
    }
}
