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
    public class ZaapsList
    {
        public int Mapid { get; set; }
        public int Cellid { get; set; }
        public static int ZaapsCount;
        public static List<ZaapsList> AllZaaps = new List<ZaapsList>();
        public static string TableZaaps = JsonManager.SearchAuth("zaaps");

        public ZaapsList(IDataReader reader)
        {
            Mapid = (int)reader["mapID"];
            Cellid = (int)reader["cellID"];
        }
        public static void LoadallZaaps()
        {
            string query = QueryBuilder.SelectFromQuery(new string[] { "*" }, TableZaaps, "", "");

            using (MySqlConnection connection = new MySqlConnection(DatabaseManager.ConnectionString))
            {
                try
                {
                    connection.Open();
                    MySqlDataReader reader = new MySqlCommand(query, connection).ExecuteReader();
                    ZaapsList Z = null;
                    while (reader.Read())
                    {
                        Z = new ZaapsList(reader);
                        AllZaaps.Add(Z);
                    }
                    reader.Close();
                    ZaapsCount = AllZaaps.Count;
                    reader.Dispose();
                    connection.Close();
                    connection.Dispose();
                }
                catch (MySqlException) { }
            }
        }
    }
}
