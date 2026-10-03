using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Utils.Crypto;
using Tool_Editor.maps.data;
using Map = Tool_BotProtocol.Game.Maps.Map;

namespace Outil_Azur_complet.Bot.Controls
{
    /// <summary>Owned, portable PNG scenery decoded from the same map data used by the client.</summary>
    public sealed class BotMapArtwork : IDisposable
    {
        public const float CellWidth = 53f;
        public const float CellHeight = 26.5f;
        private readonly Dictionary<string, TilePicture> pictures = new Dictionary<string, TilePicture>();
        private readonly Dictionary<int, string> grounds;
        private readonly Dictionary<int, string> objects;
        private readonly Dictionary<int, string> backgrounds;
        private static readonly ConcurrentDictionary<string, Lazy<AssetLibrary>> libraries =
            new ConcurrentDictionary<string, Lazy<AssetLibrary>>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> missing = new HashSet<string>();
        private TilePicture background;
        private RectangleF backgroundBounds;
        private bool disposed;
        private DepthLayer[] depthLayers = new DepthLayer[0];
        public ArtworkCell[] Cells { get; private set; } = new ArtworkCell[0];
        public RectangleF WorldBounds { get; private set; }
        public int MissingAssetCount => missing.Count;
        public int LoadedAssetCount => pictures.Values.Count(value => value != null);
        public string Status => MissingAssetCount == 0
            ? LoadedAssetCount == 0 ? "Aucun visuel dans cette carte · vue des cellules" : "Décor chargé · " + LoadedAssetCount + " visuels"
            : MissingAssetCount + " visuel(s) absent(s) · " + string.Join(", ", missing.Take(4)) + (MissingAssetCount > 4 ? "…" : "");

        public sealed class ArtworkCell
        {
            public int Id, GroundId, Object1Id, Object2Id, GroundRotation, Object1Rotation, Level, Slope;
            public bool Active, GroundFlip, Object1Flip, Object2Flip;
            public PointF Center;
            public PointF[] Polygon;
            internal TilePicture Ground, Object1, Object2;
        }

        public sealed class DepthLayer
        {
            public float Depth;
            public int Order;
            public Action<Graphics> Draw;
        }

        private sealed class AssetLibrary
        {
            public readonly Dictionary<int, string> Grounds = new Dictionary<int, string>();
            public readonly Dictionary<int, string> Objects = new Dictionary<int, string>();
            public readonly Dictionary<int, string> Backgrounds = new Dictionary<int, string>();
            public AssetLibrary(string directory)
            {
                Index(Path.Combine(directory, "sols"), Grounds);
                Index(Path.Combine(directory, "objets"), Objects);
                Index(Path.Combine(directory, "backgrounds"), Backgrounds);
            }
        }

        private static string ResourcePath(string directory) => Path.GetFullPath(directory ?? Path.Combine(
            Path.GetDirectoryName(typeof(BotMapArtwork).Assembly.Location), "ressources", "maps"));
        private static AssetLibrary Library(string directory)
        {
            string path = ResourcePath(directory);
            return libraries.GetOrAdd(path, location => new Lazy<AssetLibrary>(() => new AssetLibrary(location))).Value;
        }

        // The resource loader can do the one-time directory scan away from the UI thread.
        public static Task WarmupAsync(string resourceDirectory = null) => Task.Run(() => Library(resourceDirectory));
        public static void InvalidateAssetIndex(string resourceDirectory = null)
        {
            libraries.TryRemove(ResourcePath(resourceDirectory), out Lazy<AssetLibrary> ignored);
        }

        internal sealed class TilePicture : IDisposable
        {
            public Bitmap Image;
            public Point Anchor;
            public Rectangle VisibleBounds;
            public RectangleF At(PointF center) => new RectangleF(center.X - Anchor.X, center.Y - Anchor.Y, Image.Width, Image.Height);
            public RectangleF VisibleAt(PointF center) => new RectangleF(center.X - Anchor.X + VisibleBounds.X,
                center.Y - Anchor.Y + VisibleBounds.Y, VisibleBounds.Width, VisibleBounds.Height);
            public void Dispose() { Image?.Dispose(); Image = null; }
        }

        public BotMapArtwork(Map map, string resourceDirectory = null)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            AssetLibrary library = Library(resourceDirectory);
            grounds = library.Grounds; objects = library.Objects; backgrounds = library.Backgrounds;
            if (map.MapWidth < 2 || string.IsNullOrEmpty(map.MapData) || map.MapData.Length % 10 != 0)
                return;
            if (map.MapData.Any(character => Hash.get_Hash(character) < 0))
                throw new FormatException("Les données visuelles de la carte sont invalides.");
            Cells = new ArtworkCell[map.MapData.Length / 10];
            int period = 2 * map.MapWidth - 1;
            for (int id = 0; id < Cells.Length; id++)
            {
                int[] value = map.MapData.Substring(id * 10, 10).Select(character => (int)Hash.get_Hash(character)).ToArray();
                if (value.Any(part => part < 0)) throw new FormatException("Les données visuelles de la carte sont invalides.");
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
                if (cell.Active) y -= (cell.Level - 7) * 20;
                cell.Center = new PointF(x, y);
                cell.Polygon = Diamond(cell.Center);
                cell.Ground = Picture(cell.GroundId, grounds, "sol", cell.GroundFlip, cell.GroundRotation);
                cell.Object1 = Picture(cell.Object1Id, objects, "objet", cell.Object1Flip, cell.Object1Rotation);
                cell.Object2 = Picture(cell.Object2Id, objects, "objet", cell.Object2Flip, 0);
                Cells[id] = cell;
            }
            float gridHeight = (2 * ((Cells.Length - 1) / period) + ((Cells.Length - 1) % period >= map.MapWidth ? 2 : 1) + 1) * CellHeight / 2;
            backgroundBounds = new RectangleF(0, 0, map.MapWidth * CellWidth, gridHeight);
            WorldBounds = backgroundBounds;
            foreach (ArtworkCell cell in Cells)
            {
                WorldBounds = RectangleF.Union(WorldBounds, Bounds(cell.Polygon));
                foreach (TilePicture picture in new[] { cell.Ground, cell.Object1, cell.Object2 })
                    if (picture != null && !picture.VisibleBounds.IsEmpty) WorldBounds = RectangleF.Union(WorldBounds, picture.VisibleAt(cell.Center));
            }
            if (map.Back_ID != 0) background = Picture(map.Back_ID, backgrounds, "fond", false, 0);
            var layers = new List<DepthLayer>();
            foreach (ArtworkCell cell in Cells)
            {
                if (cell.Object1 != null) layers.Add(new DepthLayer { Depth = cell.Center.Y, Order = 0,
                    Draw = graphics => DrawPicture(graphics, cell.Object1, cell.Center) });
                if (cell.Object2 != null) layers.Add(new DepthLayer { Depth = cell.Center.Y, Order = 2,
                    Draw = graphics => DrawPicture(graphics, cell.Object2, cell.Center) });
            }
            depthLayers = layers.OrderBy(layer => layer.Depth).ThenBy(layer => layer.Order).ToArray();
        }

        public static PointF[] Diamond(PointF center) => new[]
        {
            new PointF(center.X - CellWidth / 2, center.Y), new PointF(center.X, center.Y - CellHeight / 2),
            new PointF(center.X + CellWidth / 2, center.Y), new PointF(center.X, center.Y + CellHeight / 2)
        };

        private static RectangleF Bounds(PointF[] points) => RectangleF.FromLTRB(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y));

        private static void Index(string directory, Dictionary<int, string> destination)
        {
            if (!Directory.Exists(directory)) return;
            foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                if (new[] { ".png", ".jpg", ".jpeg", ".bmp" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
                    && int.TryParse(Path.GetFileNameWithoutExtension(path), out int id))
                    destination[id] = path;
        }

        private TilePicture Picture(int id, Dictionary<int, string> index, string kind, bool flip, int rotation)
        {
            if (id == 0) return null;
            string key = kind + " " + id + "/" + flip + "/" + rotation;
            if (pictures.TryGetValue(key, out TilePicture result)) return result;
            if (!index.TryGetValue(id, out string path))
            {
                missing.Add(kind + " " + id); pictures[key] = null; return null;
            }
            var tile = new TilesData(id, path, "", kind == "sol" ? TilesData.TileType.ground : TilesData.TileType.objet);
            Bitmap ownedImage = null;
            try
            {
                using (var stream = new FileStream(tile.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var source = Image.FromStream(stream)) ownedImage = new Bitmap(source);
                Bitmap image = ownedImage;
                Point anchor = TilesData.Anchor(tile, image);
                int width = image.Width, height = image.Height;
                if (flip) { image.RotateFlip(RotateFlipType.RotateNoneFlipX); anchor.X = width - anchor.X; }
                switch (rotation)
                {
                    case 1: image.RotateFlip(RotateFlipType.Rotate90FlipNone); anchor = new Point(height - anchor.Y, anchor.X); break;
                    case 2: image.RotateFlip(RotateFlipType.Rotate180FlipNone); anchor = new Point(width - anchor.X, height - anchor.Y); break;
                    case 3: image.RotateFlip(RotateFlipType.Rotate270FlipNone); anchor = new Point(anchor.Y, width - anchor.X); break;
                }
                result = new TilePicture { Image = image, Anchor = anchor, VisibleBounds = VisiblePixels(image) };
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException
                || error is System.Runtime.InteropServices.ExternalException || error is OutOfMemoryException)
            {
                ownedImage?.Dispose();
                missing.Add(kind + " " + id + " illisible"); result = null;
            }
            pictures[key] = result;
            return result;
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

        public void DrawGround(Graphics graphics)
        {
            if (background != null) graphics.DrawImage(background.Image, backgroundBounds);
            foreach (ArtworkCell cell in Cells)
                DrawPicture(graphics, cell.Ground, cell.Center);
        }

        public void DrawObjects(Graphics graphics, Action<ArtworkCell> drawEntity)
        {
            foreach (ArtworkCell cell in Cells)
            {
                DrawPicture(graphics, cell.Object1, cell.Center);
                drawEntity?.Invoke(cell);
                DrawPicture(graphics, cell.Object2, cell.Center);
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

        private static void DrawPicture(Graphics graphics, TilePicture picture, PointF center)
        {
            if (picture == null) return;
            RectangleF bounds = picture.At(center);
            if (picture.VisibleAt(center).IntersectsWith(graphics.ClipBounds)) graphics.DrawImage(picture.Image, bounds);
        }

        public bool HasGroundArtwork(int id) => id >= 0 && id < Cells.Length && (Cells[id].Ground != null || background != null);

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (TilePicture picture in pictures.Values) picture?.Dispose();
            pictures.Clear(); Cells = new ArtworkCell[0]; depthLayers = new DepthLayer[0];
        }
    }
}
