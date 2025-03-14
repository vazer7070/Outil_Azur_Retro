using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_Editor.maps.managers;

namespace Tool_Editor.maps.data
{
    [Serializable]
    public class Map
    {
        [NonSerialized]
        public int IDClient = 0;
        [NonSerialized]
        public bool IsEditing = false;

        public Bitmap ScreenShot = null;

        public int ID = 1;
        public string DateMap = "AZ";

        [NonSerialized]
        public TilesData Background = null;

        public int BackGroundID = 0;
        public int Musique = 0;
        public string MusiqueName = "";
        public int Ambiance = 0;
        public bool IsOutDoor = false;
        public int Capabilities = 0;
        public int Width = 15;
        public int Height = 17;
        public string Key = "";
        public string MapData = "";
        public string fightPlaces = "";
        public int NbGroups = 5;
        public int GroupMaxSize = 6;

        [NonSerialized]
        public CellsData[] Cells = new CellsData[17 * (15 * 2 - 1) - 15];

        public int X = 0;
        public int Y = 0;
        public int Area = 0;
        public int SubArea = 0;
        public int SuperArea = 0;
        public int NextRoom = 0;
        public int NextCell = 0;
        public string Mobs = "";
        public string GroupFixe_Mobs = "";
        public int Groupefixe_Cell = 0;

        public void Load()
        {
            if (MapData != "")
            {
                if (Key == "" && IsCrypt())
                {
                    Key = Interaction.InputBox("Impossible de charger la carte, celle-ci est chiffrée", "Lecture impossible");
                    return;
                }
                CellsData[] cell = new CellsData[Height * (Width * 2 - 1) - Width];
                int j = (Height * (Width * 2 - 1) - Width);
                for(int i = 0; i < j; i++)
                {
                    cell[i] = new CellsData();
                    cell[i].ID = i;
                }
                Cells = cell;
                DecompressMap();
            }
            if (fightPlaces != "")
                LoadFightCell();
            if (BackGroundID != 0)
                Background = TilesData.GetBackgrounds(BackGroundID);
        }

        private bool IsCrypt()
        {
            int num = 0;
            foreach (char a in MapData)
            {
                if (char.IsNumber(a))
                    num += 1;
            }
            return num >= 1000;
        }

        public void SaveFightCell()
        {
            fightPlaces = (string)FightCellManager.GetHashCode(this);
        }

        public void LoadFightCell()
        {
            if (fightPlaces != "")
                Cells = FightCellManager.ParseCellFight(fightPlaces, Cells);
        }

        public void DecompressCells(string cell, int CellID)
        {
            int[] intArray = new int[10];

            for (int i = 0; i < cell.Length; i++)
            {
                intArray[i] = (int)DecryptClass.HashCode(cell[i].ToString());
            }

            Cells[CellID].Los = (intArray[0] & 1) > 0;
            Cells[CellID].RotaGFX1 = (intArray[1] & 0x30) >> 4;
            Cells[CellID].NivSol = (intArray[1] & 15);
            Cells[CellID].Type(((intArray[2] & 0x38) >> 3) & -1025);
            Cells[CellID].GFX1 = TilesData.GetGrounds((((intArray[0] & 0x18) << 6) + ((intArray[2] & 7) << 6)) + intArray[3]);
            Cells[CellID].IncliSol = ((intArray[4] & 60) >> 2);
            Cells[CellID].FlipGFX1 = (intArray[4] & 2) >> 1 > 0;
            Cells[CellID].GFX2 = TilesData.GetObjects((((((intArray[0] & 4) << 11) + ((intArray[4] & 1) << 12)) + (intArray[5] << 6)) + intArray[6]));
            Cells[CellID].RotaGFX2 = (intArray[7] & 0x30) >> 4;
            Cells[CellID].FlipGFX2 = (intArray[7] & 8) >> 3 > 0;
            Cells[CellID].FlipGFX3 = (intArray[7] & 4) >> 2 > 0;
            Cells[CellID].IO = (intArray[7] & 2) >> 1 > 0;
            Cells[CellID].GFX3 = TilesData.GetObjects((((((intArray[0] & 2) << 12) + ((intArray[7] & 1) << 12)) + (intArray[8] << 6)) + intArray[9]));
        }

        public void DecompressMap()
        {
            try
            {
                if (Key != "")
                {
                    Key = Key.Trim();
                    Key = Key.Replace("\r", "").Replace("\n", "").Replace("\r\n", "");
                    Key = DecryptClass.PrepareKey(Key);
                    int check = Convert.ToInt32(Convert.ToInt64(DecryptClass.CheckSum(Key), 0x10) * 2);
                    MapData = DecryptClass.DecypherData(MapData, Key, check);
                    Key = "";
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
                return;
            }

            int num8 = (Cells.Length * 10) - 10;
            for (int i = 0; i <= num8; i += 10)
            {
                DecompressCells(MapData.Substring(i, 10), i / 10);
            }
        }

        #region Functions

        [NonSerialized]
        public static Dictionary<int, Map> MapList = new Dictionary<int, Map>();

        public static void AddMap(Map M)
        {
            if (!MapList.ContainsKey(M.ID))
            {
                M.IDClient = MapList.Count;
                MapList.Add(M.ID, M);
            }
        }

        public static bool IDAlreadyLoad(Map M)
        {
            return MapList.Values.Any(map => map.ID == M.ID && map.MapData == M.MapData);
        }

        public static Map GetByID(int id)
        {
            return MapList.TryGetValue(id, out Map map) ? map : null;
        }

        public static string Get_Capabilities(bool cantp, bool cansave, bool canattack, bool canhall)
        {
            string S = (!cantp ? "1" : "0") + (!cansave ? "1" : "0") + (!canattack ? "1" : "0") + (!canhall ? "1" : "0");
            return S;
        }

        #endregion
    }

}
