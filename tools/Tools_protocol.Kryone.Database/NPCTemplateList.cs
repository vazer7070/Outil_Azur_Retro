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
    public class NPCTemplateList
    {
        public int ID { get; set; }
        public int BonusValue { get; set; }
        public int GFXID { get; set; }
        public int ScaleX { get; set; }
        public int ScaleY { get; set; }
        public int Sexe { get; set; }
        public int Color1 { get; set; }
        public int Color2 { get; set; }
        public int Color3 { get; set; }
        public string Accessories { get; set; }
        public int Extraclip { get; set; }
        public int CustomArtwork { get; set; }
        public string Initquestion { get; set; }
        public string Ventes { get; set; }
        public string Quests { get; set; }
        public string Exchanges { get; set; }
        public static Dictionary<int, NPCTemplateList> TemplatesPNJ = new Dictionary<int, NPCTemplateList>();
        public static int PNJcount;
        public static string TablePNJTemplate = JsonManager.SearchAuth("npc_template");
        public NPCTemplateList(IDataReader reader)
        {
            ID = (int)reader["id"];
            BonusValue = (int)reader["bonusValue"];
            GFXID = (int)reader["gfxID"];
            ScaleX = (int)reader["scaleX"];
            ScaleY = (int)reader["scaleY"];
            Sexe = (int)reader["sex"];
            Color1 = (int)reader["color1"];
            Color2 = (int)reader["color2"];
            Color3 = (int)reader["color3"];
            Accessories = (string)reader["accessories"];
            Extraclip = (int)reader["extraClip"];
            CustomArtwork = (int)reader["customArtWork"];
            Initquestion = (string)reader["initQuestion"];
            Ventes = (string)reader["ventes"];
            Quests = (string)reader["quests"];
            Exchanges = (string)reader["exchanges"];
        }
        public static void LoadAllPnjTemplate()
        {
            string query = QueryBuilder.SelectFromQuery(new string[] { "*" }, TablePNJTemplate, "", "");

            using (MySqlConnection connection = new MySqlConnection(DatabaseManager.ConnectionString))
            {
                try
                {
                    connection.Open();
                    MySqlDataReader reader = new MySqlCommand(query, connection).ExecuteReader();
                    NPCTemplateList npcsT = null;
                    while (reader.Read())
                    {
                        npcsT = new NPCTemplateList(reader);
                        TemplatesPNJ.Add(npcsT.ID, npcsT);
                    }
                    reader.Close();
                    PNJcount = TemplatesPNJ.Count;
                    reader.Dispose();
                    connection.Close();
                    connection.Dispose();
                }
                catch (MySqlException) { }
            }
            
        }
    }
}
