using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools_protocol.Emulators;
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
        static string TableMap => EmulatorRegistry.Current.Table("cartes");


        public MapsList(IDataReader reader)
        {
            ID = (int)reader["id"];
            Date = (string)reader["date"];
            Width = (int)reader["width"];
            Heigth = (int)reader["heigth"];
            Places = (string)reader["places"];
            Key = (string)reader["key"];
            MapData = (string)reader["mapData"];
            // StarLoco n'a ni cells, ni cases, ni background : ces colonnes restent facultatives.
            Cells = OptionalText(reader, "cells");
            Monsters = (string)reader["monsters"];
            Capabilities = (int)reader["capabilities"];
           MapPos = (string)reader["mappos"];
           NumGroup = (int)reader["numgroup"];
           Minsize = (int)reader["minSize"];
           Fixsize = (int)reader["fixSize"];
            MaxSize = (int)reader["maxSize"];
            Cases = OptionalText(reader, "cases");
            Forbidden = (string)reader["forbidden"];
            BackGround = OptionalInt(reader, "background");
           // Houses = (int)reader["house"];

        }

        private static int Ordinal(IDataRecord reader, string column)
        {
            for (int index = 0; index < reader.FieldCount; index++)
                if (string.Equals(reader.GetName(index), column, StringComparison.OrdinalIgnoreCase))
                    return index;
            return -1;
        }

        private static string OptionalText(IDataRecord reader, string column)
        {
            int index = Ordinal(reader, column);
            return index < 0 || reader.IsDBNull(index) ? "" : Convert.ToString(reader.GetValue(index));
        }

        private static int OptionalInt(IDataRecord reader, string column)
        {
            int index = Ordinal(reader, column);
            return index < 0 || reader.IsDBNull(index) ? 0 : Convert.ToInt32(reader.GetValue(index));
        }

        public static void LoadAllMaps()
        {
            string[] args = new string[] { "*" };
            string query = QueryBuilder.SelectFromQuery(args, TableMap, "", "");
            using (MySqlConnection connection = new MySqlConnection(EmulatorRegistry.ConnectionFor("cartes")))
            {
                try
                {
                    connection.Open();
                    AllMaps.Clear();
                    AllMapsDico.Clear();
                    MapsList M = null;
                    MySqlDataReader lecteur = new MySqlCommand(query, connection).ExecuteReader();
                    while (lecteur.Read())
                    {
                        M = new MapsList(lecteur);
                        AllMaps.Add(M);
                        AllMapsDico.Add(M.ID, M);
                    }
                    MapsCount = AllMaps.Count;
                    lecteur.Close();
                    lecteur.Dispose();
                    connection.Close();
                    connection.Dispose();
                }
                catch (MySqlException) { }
            }
        }
        public static MapsList ReturnMapInfo (int id)
        {
            return AllMapsDico[id];
        }
    }
}
