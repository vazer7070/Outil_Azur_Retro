using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Configuration;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Tool_Editor.maps.data
{
    public class TilesData
    {
        public int ID;

        public string Path;

        public string Folder;

        public TileType type;

		public Bitmap ImageLoaded;
		private Point? inferredAnchor;

        [NonSerialized()]
        public static Pos[] PosGround = new Pos[50000];

        [NonSerialized()]
        public static Pos[] PosObject = new Pos[50000];
		private static readonly Lazy<Dictionary<int, Pos>> GroundAnchors = new Lazy<Dictionary<int, Pos>>(() => IndexAnchors(PosGround));
		private static readonly Lazy<Dictionary<int, Pos>> ObjectAnchors = new Lazy<Dictionary<int, Pos>>(() => IndexAnchors(PosObject));

		private static Dictionary<int, Pos> IndexAnchors(Pos[] positions)
		{
			var index = new Dictionary<int, Pos>();
			foreach (Pos position in positions)
				if (position.ID != 0 || position.X != 0 || position.Y != 0)
					index[position.ID] = position;
			return index;
		}

        public static TilesData[] Backgrounds_Tiles = new TilesData[10000];



        public static TilesData[] ListGrounds = new TilesData[10000];
        public static TilesData[] ListObject = new TilesData[100000];
        public static TilesData SelectedTiles = null;

        static TilesData()
        {


        }

        public TilesData(int id, string p, string f, TileType T)
        {
            ID = id;
            Path = p;
            Folder = f;
            type = T;

        }

        public static void ClearCache()
        {
            foreach (TilesData T in Backgrounds_Tiles)
            {
                if ((T == null ? false : T.ImageLoaded != null))
                {
                    T.ImageLoaded.Dispose();
                    T.ImageLoaded = null;
                }
            }
            foreach (TilesData T in ListGrounds)
            {
                if ((T == null ? false : T.ImageLoaded != null))
                {
                    T.ImageLoaded.Dispose();
                    T.ImageLoaded = null;
                }
            }
            foreach (TilesData T in ListObject)
            {
                if ((T == null ? false : T.ImageLoaded != null))
                {
                    T.ImageLoaded.Dispose();
                    T.ImageLoaded = null;
                }
            }
        }

        public static Pos Get_Grounds(int id)
        {
            Pos po = PosGround.FirstOrDefault((Pos x) => x.ID == id);
            return po;
        }

        public static Pos Get_Object(int id)
        {
            Pos po = PosObject.FirstOrDefault((Pos x) => x.ID == id);
            return po;
        }

        public static TilesData GetBackgrounds(int id)
        {
            return id >= 0 && id < Backgrounds_Tiles.Length && Backgrounds_Tiles[id]?.ID == id
                ? Backgrounds_Tiles[id] : null;
        }

        public static TilesData GetGrounds(int id)
        {
            return id >= 0 && id < ListGrounds.Length && ListGrounds[id]?.ID == id
                ? ListGrounds[id] : null;
        }

        // The supplied PNG library has no offset manifest. A missing entry used
        // to mean (0,0), placing the image's upper-left corner on the cell.
        public static Point Anchor(TilesData tile, Size imageSize)
        {
            if (tile == null) throw new ArgumentNullException(nameof(tile));
            Pos position;
            var positions = tile.type == TileType.ground ? GroundAnchors.Value : ObjectAnchors.Value;
            if (positions.TryGetValue(tile.ID, out position)) return new Point(position.X, position.Y);
            return tile.type == TileType.ground
                ? new Point(imageSize.Width / 2, imageSize.Height / 2)
                : new Point(imageSize.Width / 2, imageSize.Height);
        }

        // Some supplied sprites have large transparent margins. Anchor the
        // visible pixels, not the PNG canvas, when no explicit offset exists.
        public static Point Anchor(TilesData tile, Image image)
        {
            if (tile == null || image == null) throw new ArgumentNullException(tile == null ? nameof(tile) : nameof(image));
            Pos position;
            var positions = tile.type == TileType.ground ? GroundAnchors.Value : ObjectAnchors.Value;
            if (positions.TryGetValue(tile.ID, out position)) return new Point(position.X, position.Y);
            if (tile.inferredAnchor.HasValue) return tile.inferredAnchor.Value;
            using (var pixels = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb))
            {
                using (var graphics = Graphics.FromImage(pixels))
                {
                    graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    graphics.DrawImageUnscaled(image, 0, 0);
                }
                var bounds = new Rectangle(0, 0, pixels.Width, pixels.Height);
                BitmapData locked = pixels.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    byte[] bytes = new byte[Math.Abs(locked.Stride) * pixels.Height];
                    Marshal.Copy(locked.Scan0, bytes, 0, bytes.Length);
                    int left = pixels.Width, right = -1, top = pixels.Height, bottom = -1;
                    for (int y = 0; y < pixels.Height; y++)
                        for (int x = 0; x < pixels.Width; x++)
                            if (bytes[y * locked.Stride + x * 4 + 3] != 0)
                            {
                                left = Math.Min(left, x); right = Math.Max(right, x);
                                top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                            }
                    tile.inferredAnchor = right < 0 ? Anchor(tile, image.Size)
                        : tile.type == TileType.ground
                            ? new Point((left + right + 1) / 2, (top + bottom + 1) / 2)
                            : new Point((left + right + 1) / 2, bottom + 1);
                }
                finally { pixels.UnlockBits(locked); }
            }
            return tile.inferredAnchor.Value;
        }

        public static TilesData GetObjects(int id)
        {
            return id >= 0 && id < ListObject.Length && ListObject[id]?.ID == id
                ? ListObject[id] : null;
        }
        public Bitmap Image(bool cache = false)
        {
            if (cache && ImageLoaded != null) return ImageLoaded;
            Bitmap bitmap;
            using (var source = System.Drawing.Image.FromFile(Path))
                bitmap = new Bitmap(source);
            if (cache) ImageLoaded = bitmap;
            return bitmap;

        }
        public struct Pos
        {
            public int ID;

            public int X;

            public int Y;
        }

        public enum TileType
        {
            background,
            ground,
            objet
        }

        public static int Count_Ground()
        {
            int num = 0;
            foreach (Pos P in PosGround)
            {
                if (P.ID != 0)
                {
                    num += 1;
                }
                continue;
            }
            return num;
        }
        public static int Count_Object()
        {
            int num = 0;
            foreach (Pos P in PosObject)
            {
                if (P.ID != 0)
                {
                    num += 1;
                }
                continue;
            }
            return num;
        }

    }
}
