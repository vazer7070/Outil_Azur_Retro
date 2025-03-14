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
    public class Map: IEliminable, IDisposable
    {
        public int MapID { get; set; }
        public byte MapWidth { get; set; }
        public byte MapHeight { get; set; }
        public sbyte X { get; set; }
        public sbyte Y { get; set; }
        public int Back_ID { get; set; }
        public string MapData { get; set; }
        static string MapPath = @".\ressources\Bot\BotMaps";
        public Cell[] MapCells;
        public Dictionary<TeleportCellsEnum, List<short>> TeleportCells;
        public ConcurrentDictionary<int, Entites> Entites;
        public ConcurrentDictionary<int, Interactives.Interactives> Interactives;
        public static ConcurrentDictionary<int, Map> AllBotMaps = new ConcurrentDictionary<int, Map>();
        public event Action RefreshMap;
        public event Action RefreshEntities;
        public bool Disposed = false;
        public Map()
        {
            Entites = new ConcurrentDictionary<int, Entites>();
            Interactives = new ConcurrentDictionary<int, Interactives.Interactives>();
            TeleportCells = new Dictionary<TeleportCellsEnum, List<short>>();
        }
        public static async Task LoadAllMapsAsync()
        {
            try
            {
                DirectoryInfo mapFolder = new DirectoryInfo(MapPath);
                List<Task> tasks = new List<Task>();

                foreach (FileInfo file in mapFolder.GetFiles())
                {
                    if (file.Exists)
                    {
                        tasks.Add(Task.Run(async () =>
                        {
                            XElement xmlmap = await Task.Run(() => XElement.Load(file.FullName));
                            Map L = new Map()
                            {
                                MapID = int.Parse(xmlmap.Element("ID").Value),
                                MapWidth = byte.Parse(xmlmap.Element("LARGEUR").Value),
                                MapHeight = byte.Parse(xmlmap.Element("LONGUEUR").Value),
                                X = sbyte.Parse(xmlmap.Element("X").Value),
                                Y = sbyte.Parse(xmlmap.Element("Y").Value),
                                MapData = xmlmap.Element("MAP_DATA").Value,
                                Back_ID = int.Parse(xmlmap.Element("BACK").Value)
                            };
                            AllBotMaps.TryAdd(L.MapID, L);
                        }));
                    }
                }

                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                return;
            }
        }

        public Map ReturnMapInfo(int MAPID)
        {
            return AllBotMaps[MAPID];
        }
        public void SetRefreshMap(string packet)
        {
            Entites.Clear();
            Interactives.Clear();
            TeleportCells.Clear();

            string[] P = packet.Split('|');
            MapID = Convert.ToInt32(P[0]);

            MapWidth = ReturnMapInfo(MapID).MapWidth;
            MapHeight = ReturnMapInfo(MapID).MapHeight;
            X = ReturnMapInfo(MapID).X;
            Y = ReturnMapInfo(MapID).Y;
            Back_ID = ReturnMapInfo(MapID).Back_ID;
            
           
            Task.Run(() => DecompressMap(ReturnMapInfo(MapID).MapData)).Wait();
            Task.Run(() => getTeleportCell(MapCells)).Wait();
            RefreshMap();
            
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
        public Cell GetCellByposition(int x, int y) => MapCells.FirstOrDefault(Cell => Cell.X == x && Cell.Y == y);
        public List<PNJ> NPC_List() => Entites.Values.Where(x => x is PNJ).Select( x => x as PNJ ).ToList();
        public List<Cell>CellsOccuped() => Entites.Values.Where( x => x is Monstres.Monstres).Select(x => x.Cell).ToList();
        public List<Monstres.Monstres> MonsterList() => Entites.Values.Where(x => x is Monstres.Monstres).Select(x => x as Monstres.Monstres).ToList();
        public List<Personnages> PersoList() => Entites.Values.Where( x => x is Personnages).Select(x => x as Personnages).ToList();
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
                    for(int i = 0;i < MobsAvailable.Count; i++)
                    {
                        if (M.GroupHasThisMob(MobsYouNeed[i]))
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
        public void DecompressMap(string mapdata)
        {
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
            bool Vision = (cellsinfos[0] & 1) != 1;
            short layer2 = Convert.ToInt16(((cellsinfos[0] & 2) << 12) + ((cellsinfos[7] & 1) << 12) + (cellsinfos[8] << 6) + cellsinfos[9]);
            short layer1 = Convert.ToInt16(((cellsinfos[0] & 4) << 11) + ((cellsinfos[4] & 1) << 12) + (cellsinfos[5] << 6) + cellsinfos[6]);
            byte level = Convert.ToByte(cellsinfos[1] & 15);
            byte slope = Convert.ToByte((cellsinfos[4] & 60) >> 2);

            return new Cell(CellID, Active, C_T, Vision, level, slope, I, layer1, layer2, this);
            
        }
        public void getTeleportCell(Cell[] cells)
        {
            List<Cell> cellsToManipulate = cells.ToList().Where(c => c.IsTrigger()).ToList();
            cellsToManipulate.ForEach(cell =>
            {
                TeleportCells.Add(cell.CellID);
            });
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
            Entites.Clear();
            Interactives.Clear();
            TeleportCells.Clear();
            MapCells = null;
        }

        protected virtual void Dispose (bool disposed)
        {
            if (disposed)
                return;
            Entites.Clear ();
            Interactives.Clear ();
            MapCells = null;
            Entites = null;
            TeleportCells = null;
            disposed = true;
        }
        public void Dispose() => Dispose(true);
        ~Map() => Dispose(true);
    }
}
