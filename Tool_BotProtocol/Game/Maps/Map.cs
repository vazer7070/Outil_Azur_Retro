using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using Tool_BotProtocol.Game.Maps.Enums;
using Tool_BotProtocol.Game.Maps.Interfaces;
using Tool_BotProtocol.Utils.Crypto;
using Tool_BotProtocol.Utils.Interfaces;
using Tool_BotProtocol.Utils.Extensions;
using Tool_BotProtocol.Game.Monstres;
using Tool_BotProtocol.Game.NPC;
using Tool_BotProtocol.Game.Perso;

namespace Tool_BotProtocol.Game.Maps
{
    public partial class Map: IEliminable, IDisposable
    {
        public int MapID { get; set; }
        public byte MapWidth { get; set; }
        public byte MapHeight { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Back_ID { get; set; }
        public string MapData { get; set; }
        public static string MapPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ressources", "Bot", "BotMaps");
        public static string[] LoadWarnings { get; private set; } = new string[0];
        public bool HasMapData => MapCells != null && MapCells.Length > 0;
        public string LoadError { get; private set; }
        public Cell[] MapCells;
        public Dictionary<TeleportCellsEnum, List<short>> TeleportCells;
        public ConcurrentDictionary<int, Interactives.Interactives> Interactives;
        public static ConcurrentDictionary<int, Map> AllBotMaps = new ConcurrentDictionary<int, Map>();
        public event Action RefreshMap;
        public event Action RefreshEntities;
        public event Action<int, List<Cell>, int> EntityMovement;
        public bool Disposed = false;
        public Map()
        {
            InitActors();
            Interactives = new ConcurrentDictionary<int, Interactives.Interactives>();
            TeleportCells = new Dictionary<TeleportCellsEnum, List<short>>();
        }
        public static Task LoadAllMapsAsync() => LoadAllMapsAsync(MapPath);

        public static Task LoadAllMapsAsync(string directory)
        {
            return Task.Run(() =>
            {
                var loaded = new ConcurrentDictionary<int, Map>();
                var warnings = new ConcurrentQueue<string>();
                if (!Directory.Exists(directory))
                    throw new DirectoryNotFoundException("Le dossier des cartes du bot est introuvable : " + directory);
                // Bound disk/CPU pressure instead of creating two tasks for every XML file.
                Parallel.ForEach(Directory.EnumerateFiles(directory, "*.xml"),
                    new ParallelOptions { MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount) }, file =>
                    {
                        try
                        {
                            XElement xmlmap = XElement.Load(file);
                            var map = new Map
                            {
                                MapID = int.Parse(xmlmap.Element("ID").Value),
                                MapWidth = byte.Parse(xmlmap.Element("LARGEUR").Value),
                                MapHeight = byte.Parse(xmlmap.Element("LONGUEUR").Value),
                                X = int.Parse(xmlmap.Element("X").Value),
                                Y = int.Parse(xmlmap.Element("Y").Value),
                                MapData = xmlmap.Element("MAP_DATA").Value,
                                Back_ID = int.Parse(xmlmap.Element("BACK").Value)
                            };
                            if (map.MapWidth < 2 || map.MapHeight == 0 || string.IsNullOrEmpty(map.MapData)
                                || map.MapData.Length % 10 != 0 || map.MapData.Length / 10 > 4096
                                || map.MapData.Any(c => !Hash.caracteres_array.Contains(c)))
                                throw new FormatException("Dimensions ou données de cellules invalides.");
                            loaded[map.MapID] = map;
                        }
                        catch (Exception error) { warnings.Enqueue(Path.GetFileName(file) + " : " + error.Message); }
                    });
                AllBotMaps.Clear();
                foreach (var pair in loaded) AllBotMaps[pair.Key] = pair.Value;
                LoadWarnings = warnings.ToArray();
            });
        }

        public Map ReturnMapInfo(int MAPID)
        {
            AllBotMaps.TryGetValue(MAPID, out Map value);
            return value;
        }
        public void SetRefreshMap(string packet)
        {
            string[] P = packet.Split('|');
            if (!int.TryParse(P[0], out int id)) throw new FormatException("Identifiant de carte invalide.");
            Clear();
            MapID = id;
            Map info = ReturnMapInfo(MapID);
            if (info == null)
            {
                LoadError = "Carte " + MapID + " absente des ressources du bot. Exportez les cartes depuis le parseur.";
                RefreshMap?.Invoke();
                return;
            }
            MapWidth = info.MapWidth;
            MapHeight = info.MapHeight;
            X = info.X;
            Y = info.Y;
            Back_ID = info.Back_ID;
            MapData = info.MapData;
            DecompressMap(MapData);
            getTeleportCell(MapCells);
            RefreshMap?.Invoke();
            
        }
        public string GetCoordinates => $"[{X},{Y}]";
        public Cell GetCellFromId(short cellid)
        {
            try
            {
                return MapCells[cellid];
            }
            catch
            {
                return null;
            }
        }
        public bool IsInMap(string position) => position == MapID.ToString() || position == GetCoordinates;
        public Cell GetCellByposition(int x, int y) => MapCells?.FirstOrDefault(Cell => Cell.X == x && Cell.Y == y);
        // Vues historiques sur Actors : seulement les acteurs dont la cellule est résolue sur la carte chargée.
        public List<PNJ> NPC_List() => AllActors.OfType<PNJ>().Where(x => x.Cell != null).ToList();
        public List<Cell>CellsOccuped() => AllActors.OfType<Monstres.Monstres>().Where(x => x.Cell != null).Select(x => x.Cell).ToList();
        public List<Monstres.Monstres> MonsterList() => AllActors.OfType<Monstres.Monstres>().Where(x => x.Cell != null).ToList();
        public List<Personnages> PersoList() => AllActors.OfType<Personnages>().Where(x => x.Cell != null).ToList();
        public List <Monstres.Monstres>GetMobsGroup(int min, int max, int level_min, int level_max, List<int> Mobs_forbidden, List<int> MobsYouNeed)
        {
            List<Monstres.Monstres> MobsAvailable = new List<Monstres.Monstres>();
            foreach(Monstres.Monstres M in MonsterList())
            {
                if(M.GetAllMonster < min || M.GetAllMonster > max)
                    continue;
                if (M.MobsGroupelevel < level_min || M.MobsGroupelevel > level_max)
                    continue;
                if (M.Cell.C_Types == CellTypes.TELEPORT_CELL)
                    continue;
                bool IsGood = true;
                if(Mobs_forbidden != null)
                {
                    for(int i = 0; i < Mobs_forbidden.Count; i++)
                    {
                        if (M.GroupHasThisMob(Mobs_forbidden[i]))
                        {
                            IsGood = false;
                            break;
                        }
                    }
                }

                if(MobsYouNeed != null && IsGood)
                {
                    for(int i = 0;i < MobsYouNeed.Count; i++)
                    {
                        if (!M.GroupHasThisMob(MobsYouNeed[i]))
                        {
                            IsGood=false;
                            break;
                        }
                    }
                }
                if(IsGood)
                    MobsAvailable.Add(M);
            }
            return MobsAvailable;
        }
        public bool IsCapturable(List<KeyValuePair<int, int>> Mobslist, Monstres.Monstres ActualGroup)
        {
            bool capturable = false;
            foreach(var i in Mobslist)
            {
                if(i.Value == ActualGroup.GroupSize(i.Key))
                {
                    capturable = true;
                    break;
                }
            }
            return capturable;
        }
        public void GetMapRefreshEvent() => RefreshMap?.Invoke();
        public void GetEntitiesRefreshEvent() => RefreshEntities?.Invoke();
        public void NotifyEntityMovement(int id, List<Cell> path, int duration)
        {
            if (path != null && path.Count > 1) EntityMovement?.Invoke(id, path.ToList(), Math.Max(20, duration));
        }
        public void DecompressMap(string mapdata)
        {
            if (MapWidth < 2 || string.IsNullOrEmpty(mapdata) || mapdata.Length % 10 != 0 || mapdata.Length / 10 > 4096)
                throw new FormatException("Données de carte invalides : une cellule doit contenir dix caractères.");
            if (mapdata.Any(c => !Hash.caracteres_array.Contains(c)))
                throw new FormatException("Caractère inconnu dans les données de carte.");
            Interactives.Clear();
            MapCells = new Cell[mapdata.Length / 10];
            string values;
            for (int i = 0; i < mapdata.Length; i += 10)
            {
                values = mapdata.Substring(i,10);
                MapCells[ i /10] = DecompressCell(values, Convert.ToInt16(i /10));
            }
        }
        public Cell DecompressCell(string data, short CellID)
        {
            byte[] cellsinfos = new byte[data.Length];

            for (int i = 0; i < cellsinfos.Length; i++)
                cellsinfos[i] = Convert.ToByte(Hash.get_Hash(data[i]));

            int mapW = MapWidth;
            int loc5 = CellID / ((mapW * 2) - 1);
            int loc6 = CellID - (loc5 * ((mapW * 2) - 1));
            int loc7 = loc6 % mapW;
            short I = ((cellsinfos[7] & 2) >> 1) != 0 ? Convert.ToInt16(((cellsinfos[0] & 2) << 12) + ((cellsinfos[7] & 1) << 12) + (cellsinfos[8] << 6) + cellsinfos[9]) : Convert.ToInt16(-1);
            
            bool Active = (cellsinfos[0] & 32) >> 5 != 0;
            CellTypes C_T = (CellTypes)((cellsinfos[2] & 56) >> 3);
            bool Vision = (cellsinfos[0] & 1) == 1;
            short layer2 = Convert.ToInt16(((cellsinfos[0] & 2) << 12) + ((cellsinfos[7] & 1) << 12) + (cellsinfos[8] << 6) + cellsinfos[9]);
            short layer1 = Convert.ToInt16(((cellsinfos[0] & 4) << 11) + ((cellsinfos[4] & 1) << 12) + (cellsinfos[5] << 6) + cellsinfos[6]);
            byte level = Convert.ToByte(cellsinfos[1] & 15);
            byte slope = Convert.ToByte((cellsinfos[4] & 60) >> 2);

            return new Cell(CellID, Active, C_T, Vision, level, slope, I, layer1, layer2, this);
            
        }
        public void getTeleportCell(Cell[] cells)
        {
            TeleportCells.Clear();
            if (cells == null || cells.Length == 0 || MapWidth < 2) return;
            int period = 2 * MapWidth - 1;
            int last = cells.Length - 1;
            int lastRow = 2 * (last / period) + (last % period >= MapWidth ? 1 : 0);
            foreach (Cell cell in cells.Where(c => c.IsTrigger() || c.C_Types == CellTypes.TELEPORT_CELL))
            {
                int within = cell.CellID % period;
                int row = 2 * (cell.CellID / period) + (within >= MapWidth ? 1 : 0);
                int column = within >= MapWidth ? within - MapWidth : within;
                var sides = new List<TeleportCellsEnum>();
                if (row <= 1) sides.Add(TeleportCellsEnum.TOP);
                if (row >= lastRow - 1) sides.Add(TeleportCellsEnum.BOTTOM);
                if (column == 0) sides.Add(TeleportCellsEnum.LEFT);
                if (column == (row % 2 == 0 ? MapWidth - 1 : MapWidth - 2)) sides.Add(TeleportCellsEnum.RIGHT);
                if (sides.Count == 0) sides.Add(TeleportCellsEnum.NULL);
                foreach (var side in sides)
                {
                    if (!TeleportCells.TryGetValue(side, out List<short> ids))
                        TeleportCells[side] = ids = new List<short>();
                    ids.Add(cell.CellID);
                }
            }
        }
        public string TransformToCellId(string[] cellsDirection)
        {
            StringBuilder st = new StringBuilder();
            for (int i = 0; i < cellsDirection.Length; i++)
            {
                switch (cellsDirection[i])
                {
                    case "RIGHT":
                        st.Append(TeleportCells[TeleportCellsEnum.RIGHT].First());
                        break;
                    case "LEFT":
                        st.Append(TeleportCells[TeleportCellsEnum.LEFT].First());
                        break;
                    case "TOP":
                        st.Append(TeleportCells[TeleportCellsEnum.TOP].First());
                        break;
                    case "BOTTOM":
                        st.Append(TeleportCells[TeleportCellsEnum.BOTTOM].First());
                        break;
                    default:
                        break;
                }
                if (i < cellsDirection.Length - 1)
                    st.Append('|');
            }
            return st.ToString();
        }
        public string TransformToCell(string cellsDirection)
        {
            StringBuilder st = new StringBuilder();
           
                switch (cellsDirection)
                {
                    case "RIGHT":
                        st.Append(TeleportCells[TeleportCellsEnum.RIGHT].First());
                        break;
                    case "LEFT":
                        st.Append(TeleportCells[TeleportCellsEnum.LEFT].First());
                        break;
                    case "TOP":
                        st.Append(TeleportCells[TeleportCellsEnum.TOP].First());
                        break;
                    case "BOTTOM":
                        st.Append(TeleportCells[TeleportCellsEnum.BOTTOM].First());
                        break;
                    default:
                        break;
                }
                
            return st.ToString();
        }

        public void Clear()
        {
            MapID = 0;
            X = 0;
            Y = 0;
            ClearActors();
            Interactives.Clear();
            TeleportCells.Clear();
            MapCells = null;
            MapWidth = MapHeight = 0;
            MapData = null;
            LoadError = null;
        }

        protected virtual void Dispose (bool disposed)
        {
            if (Disposed)
                return;
            DisposeActors();
            Interactives.Clear ();
            MapCells = null;
            TeleportCells = null;
            Disposed = true;
            RefreshMap = null;
            RefreshEntities = null;
            EntityMovement = null;
        }
        public void Dispose() { Dispose(true); GC.SuppressFinalize(this); }
    }
}
