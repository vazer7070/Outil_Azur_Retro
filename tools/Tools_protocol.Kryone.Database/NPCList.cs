using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools_protocol.Json;
using Tools_protocol.Query;

namespace Tools_protocol.Kryone.Database
{
    public class NPCList
    {
        public int MapId { get; set; }
        public int NPCId { get; set; }
        public int CellId { get; set; }
        public int Orientation { get; set; }
        public byte Ismovable { get; set; }

        public static int NpcCount;
        public static List<NPCList> AllPnj = new List<NPCList>();
        public static Dictionary<string, string> PNJIdName = new Dictionary<string, string>();
        public static string TablePNJ => JsonManager.SearchAuth("npcs");
        public NPCList(IDataReader reader)
        {
            MapId = (int)reader["mapid"];
            NPCId = (int)reader["npcid"];
            CellId = (int)reader["cellid"];
            Orientation = (int)reader["orientation"];
            //Ismovable = (byte)reader["isMovable"];
        }
        public static void LoadAllPnj()
        {
            string query = QueryBuilder.SelectFromQuery(new string[] { "*" }, TablePNJ, "", "");

            using (MySqlConnection connection = new MySqlConnection(DatabaseManager.ConnectionString))
            {
                try
                {
                    connection.Open();
                    MySqlDataReader reader = new MySqlCommand(query, connection).ExecuteReader();
                    NPCList npcs = null;
                    while (reader.Read())
                    {
                        npcs = new NPCList(reader);
                        AllPnj.Add(npcs);
                    }
                    reader.Close();
                    NpcCount = AllPnj.Count;
                    reader.Dispose();
                    connection.Close();
                    connection.Dispose();
                }
                catch (MySqlException) { }
            }
        }
        public static void AddNameToPnj(string path)
        {
            string npc;

            using (StreamReader sr = File.OpenText(path))
            {
                npc = sr.ReadToEnd();
            }
            using (var reader = new StringReader(npc))
            {
                for (string line = reader.ReadLine(); line != null; line = reader.ReadLine())
                {
                    PNJIdName.Add(line.Split('|')[0].Split('=')[1], line.Split('|')[1].Split('=')[1]);
                }
            }
    }

}
}
