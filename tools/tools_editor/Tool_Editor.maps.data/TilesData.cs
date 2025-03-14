using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Configuration;
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

        [NonSerialized()]
        public static Pos[] PosGround = new Pos[50000];

        [NonSerialized()]
        public static Pos[] PosObject = new Pos[50000];

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
            foreach (TilesData T in Backgrounds_Tiles)
            {
                if (T != null)
                {
                    if (T.ID == id)
                        return T;
                }
            }
            return null;
        }

        public static TilesData GetGrounds(int id)
        {
            TilesData tilesDatum = ListGrounds.FirstOrDefault(x => x.ID == id);
            return tilesDatum;
        }

        public static TilesData GetObjects(int id)
        {
            TilesData tilesDatum = ListObject.FirstOrDefault(x => x.ID == id);
            return tilesDatum;
        }
        public Bitmap Image(bool cache = false)
        {
            if (cache)
            {
                if (ImageLoaded == null)
                    ImageLoaded = (Bitmap)System.Drawing.Image.FromFile(Path);
                return ImageLoaded;
            }
            return (Bitmap)System.Drawing.Image.FromFile(Path);

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