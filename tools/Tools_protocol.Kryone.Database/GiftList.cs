﻿using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools_protocol.Query;

namespace Tools_protocol.Kryone.Database
{
    public class GiftList
    {
        public int ID { get; set; }
        public string Objects { get; set; }
        public static string TableGift => Json.JsonManager.SearchWorld("gifts");
        public static Dictionary<int, string> AllGiftsDico = new Dictionary<int, string>();
        public static int GiftsCount;

        public GiftList(IDataReader reader)
        {
            ID = (int)reader["id"];
            Objects = (string)reader["objects"];
        }
        public static void LoadAllgifts()
        {
            MySqlDataReader reader = DatabaseManager2.SelectQuery(QueryBuilder.SelectFromQuery(new string[] { "*" }, TableGift, "", ""));
            GiftList G = null;
            while (reader.Read())
            {
                G = new GiftList(reader);
                if (string.IsNullOrEmpty(G.Objects))
                {
                    AllGiftsDico.Add(G.ID, "@");
                }
                else
                {
                    AllGiftsDico.Add(G.ID, G.Objects);
                }
                GiftsCount = AllGiftsDico.Count;
            }
            reader.Close();
            reader.Dispose();
        }
    }
}