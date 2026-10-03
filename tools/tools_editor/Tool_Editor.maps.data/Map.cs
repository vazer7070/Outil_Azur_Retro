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
        /// <summary>Demande la clé d'une carte chiffrée ; remplaçable pour les tests ou un autre front.</summary>
        public static Func<string, string> RequestKey = AskKey;

        private static string AskKey(string message)
        {
            using (var form = new Form { Text = "Carte chiffrée", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterScreen, MinimizeBox = false, MaximizeBox = false, ClientSize = new Size(420, 130) })
            using (var label = new Label { Text = message, Location = new Point(12, 12), Size = new Size(396, 36) })
            using (var input = new TextBox { Location = new Point(12, 52), Width = 396 })
            using (var ok = new Button { Text = "Valider", DialogResult = DialogResult.OK, Location = new Point(242, 90), Width = 80 })
            using (var cancel = new Button { Text = "Annuler", DialogResult = DialogResult.Cancel, Location = new Point(328, 90), Width = 80 })
            {
                form.Controls.AddRange(new Control[] { label, input, ok, cancel });
                form.AcceptButton = ok; form.CancelButton = cancel;
                return form.ShowDialog() == DialogResult.OK ? input.Text.Trim() : "";
            }
        }

        [NonSerialized]
        public int IDClient = 0;
        [NonSerialized]
        public bool IsEditing = false;
        [NonSerialized]
        public bool HasProjectCells = false;

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
        public CellsData[] Cells = new CellsData[CellCount(15, 17)];

        public static int CellCount(int width, int height)
        {
            return checked(height * (width * 2 - 1) - width + 1);
        }

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
            if (Width < 2 || Width > 100 || Height < 2 || Height > 100)
                throw new FormatException("Les dimensions de carte doivent être comprises entre 2 et 100.");
            CellsData[] previousCells = Cells;
            string previousData = MapData;
            string previousKey = Key;
            try
            {
                if (!string.IsNullOrEmpty(MapData) && !HasProjectCells)
                {
                    if (string.IsNullOrWhiteSpace(Key) && IsCrypt())
                    {
                        Key = RequestKey("Saisissez la clé hexadécimale de cette carte chiffrée.");
                        if (string.IsNullOrWhiteSpace(Key))
                            throw new OperationCanceledException("L'ouverture de la carte chiffrée a été annulée.");
                    }
                    Cells = new CellsData[CellCount(Width, Height)];
                    DecompressMap();
                }
                if (!string.IsNullOrEmpty(fightPlaces) && !HasProjectCells)
                    LoadFightCell();
                if (BackGroundID != 0)
                    Background = TilesData.GetBackgrounds(BackGroundID);
            }
            catch
            {
                Cells = previousCells;
                MapData = previousData;
                Key = previousKey;
                throw;
            }
        }

        private bool IsCrypt()
        {
            return MapData != null && MapData.Length == checked(CellCount(Width, Height) * 20) &&
                MapData.All(Uri.IsHexDigit);
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
            if (cell == null || cell.Length != 10)
                throw new FormatException("Une cellule doit contenir exactement dix caractères.");
            if (Cells == null || CellID < 0 || CellID >= Cells.Length || Cells[CellID] == null)
                throw new ArgumentOutOfRangeException(nameof(CellID));
            int[] intArray = new int[10];

            for (int i = 0; i < cell.Length; i++)
            {
                intArray[i] = (int)DecryptClass.HashCode(cell[i].ToString());
                if (intArray[i] < 0)
                    throw new FormatException($"La cellule {CellID} contient un caractère invalide.");
            }

            Cells[CellID].Los = (intArray[0] & 1) > 0;
            Cells[CellID].Active = (intArray[0] & 32) != 0;
            Cells[CellID].RotaGFX1 = (intArray[1] & 0x30) >> 4;
            Cells[CellID].NivSol = (intArray[1] & 15);
            Cells[CellID].Type(((intArray[2] & 0x38) >> 3) & -1025);
            Cells[CellID].GFX1 = RequiredTile((((intArray[0] & 0x18) << 6) + ((intArray[2] & 7) << 6)) + intArray[3], true, CellID);
            Cells[CellID].IncliSol = ((intArray[4] & 60) >> 2);
            Cells[CellID].FlipGFX1 = (intArray[4] & 2) >> 1 > 0;
            Cells[CellID].GFX2 = RequiredTile((((((intArray[0] & 4) << 11) + ((intArray[4] & 1) << 12)) + (intArray[5] << 6)) + intArray[6]), false, CellID);
            Cells[CellID].RotaGFX2 = (intArray[7] & 0x30) >> 4;
            Cells[CellID].FlipGFX2 = (intArray[7] & 8) >> 3 > 0;
            Cells[CellID].FlipGFX3 = (intArray[7] & 4) >> 2 > 0;
            Cells[CellID].IO = (intArray[7] & 2) >> 1 > 0;
            Cells[CellID].GFX3 = RequiredTile((((((intArray[0] & 2) << 12) + ((intArray[7] & 1) << 12)) + (intArray[8] << 6)) + intArray[9]), false, CellID);
        }

        private static TilesData RequiredTile(int id, bool ground, int cell)
        {
            if (id == 0) return null;
            TilesData tile = ground ? TilesData.GetGrounds(id) : TilesData.GetObjects(id);
            if (tile == null) throw new FormatException($"La tuile {(ground ? "sol" : "objet")} {id} de la cellule {cell} manque dans les ressources.");
            return tile;
        }

        public void DecompressMap()
        {
            if (Cells == null) throw new InvalidOperationException("La grille de carte n'est pas initialisée.");
            string data = MapData;
            if (IsCrypt())
            {
                string key = DecryptClass.PrepareKey((Key ?? "").Trim().Replace("\r", "").Replace("\n", ""));
                int check = Convert.ToInt32(DecryptClass.CheckSum(key), 16) * 2;
                data = DecryptClass.DecypherData(data, key, check);
            }
            int expectedLength = checked(Cells.Length * 10);
            if (data == null || data.Length != expectedLength)
                throw new FormatException($"Les données de carte contiennent {data?.Length ?? 0} caractères au lieu de {expectedLength}.");
            if (data.Any(value => (int)DecryptClass.HashCode(value.ToString()) < 0))
                throw new FormatException("Les données de carte contiennent un caractère invalide.");
            CellsData[] previous = Cells;
            Cells = Enumerable.Range(0, previous.Length).Select(id => new CellsData { ID = id }).ToArray();
            try
            {
                for (int i = 0; i < expectedLength; i += 10)
                    DecompressCells(data.Substring(i, 10), i / 10);
            }
            catch { Cells = previous; throw; }
            MapData = data;
            Key = "";
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
