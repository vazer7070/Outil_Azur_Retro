using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools_protocol.Json;
using Tools_protocol.Query;

namespace Tools_protocol.Kryone.Database
{
    public class MapsList
    {
        public int ID { get; set; }
        public string Date { get; set; }
        public int Width { get; set; }
        public int Heigth { get; set; }
        public string Places { get; set; }
        public string Key { get; set; }
        public string MapData { get; set; }
        public string Cells { get; set; }
        public string Monsters { get; set; }
        public int Capabilities { get; set; }
        public string MapPos { get; set; }
        public int NumGroup { get; set; }
        public int Minsize { get; set; }
        public int Fixsize { get; set; }
        public int MaxSize { get; set; }
        public string Cases { get; set; }
        public string Forbidden { get; set; }
        public int BackGround { get; set; }
        public int Houses { get; set; }

        public static Dictionary<int, MapsList> AllMapsDico = new Dictionary<int, MapsList>();
        public static List<MapsList> AllMaps = new List<MapsList>();
        public static int MapsCount;
        static string TableMap = JsonManager.SearchAuth("cartes");

        
        public MapsList(IDataReader reader)
        {
            ID = (int)reader["id"];
            Date = (string)reader["date"];
            Width = (int)reader["width"];
            Heigth = (int)reader["heigth"];
            Places = (string)reader["places"];
            Key = (string)reader["key"];
            MapData = (string)reader["mapData"];
            Cells = (string)reader["cells"];
            Monsters = (string)reader["monsters"];
            Capabilities = (int)reader["capabilities"];
           MapPos = (string)reader["mappos"];
           NumGroup = (int)reader["numgroup"];
           Minsize = (int)reader["minSize"];
           Fixsize = (int)reader["fixSize"];
            MaxSize = (int)reader["maxSize"];
            Cases = (string)reader["cases"];
            Forbidden = (string)reader["forbidden"];
            BackGround = (int)reader["background"];
           // Houses = (int)reader["house"];

        }
        public static void LoadAllMaps()
        {
            MapsList M = null;
            string[] args = new string[] { "*" };
            MySqlDataReader lecteur = DatabaseManager.SelectQuery(QueryBuilder.SelectFromQuery(args, TableMap, "", ""));
            while (lecteur.Read())
            {
                M = new MapsList(lecteur);
                AllMaps.Add(M);
                AllMapsDico.Add(M.ID, M);
            }
            MapsCount = AllMaps.Count;
            lecteur.Close();
        }
        public static MapsList ReturnMapInfo (int id)
        {
            return AllMapsDico[id];
        }
    }
}
