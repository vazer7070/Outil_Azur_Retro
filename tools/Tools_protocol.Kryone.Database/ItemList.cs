using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Runtime.CompilerServices;
using Tools_protocol.Json;
using Tools_protocol.Query;

namespace Tools_protocol.Kryone.Database
{
	public class ItemList
	{
		public static Dictionary<int, int> ItemsId = new Dictionary<int, int>();
		public static Dictionary<int, ItemList> ItemsList = new Dictionary<int,ItemList>();

		public int Guid
		{
			get;
			set;
		}

		public int Pos
		{
			get;
			set;
		}

		public int Puit
		{
			get;
			set;
		}

		public int Qua
		{
			get;
			set;
		}

		public string Stat
		{
			get;
			set;
		}

		public static string TableItems
		{
			get
			{
				return JsonManager.SearchWorld("items");
			}
		}

		public int Template
		{
			get;
			set;
		}

		static ItemList()
		{

		}

		public ItemList(IDataReader reader)
		{
			Guid = (int)reader["guid"];
			Template = (int)reader["template"];
			Qua = (int)reader["qua"];
			Pos = (int)reader["pos"];
			Stat = (string)reader["stats"];
			Puit = (int)reader["puit"];
		}
		
		public static void AddItemIdToList()
		{
			string[] args = new string[] { "*" };
			string query = QueryBuilder.SelectFromQuery(args, TableItems, "", "");

			using (MySqlConnection connection = new MySqlConnection(DatabaseManager2.ConnectionString))
			{
				try
				{
					connection.Open();
					ItemsId.Clear();
					ItemList G = null;
					MySqlDataReader lecteur = new MySqlCommand(query, connection).ExecuteReader();
					while (lecteur.Read())
					{
						G = new ItemList(lecteur);
						ItemsId.Add((int)lecteur["guid"], (int)lecteur["template"]);
						ItemsList.Add((int)lecteur["guid"], G);
					}
					lecteur.Close();
					lecteur.Dispose();
					connection.Close();
					connection.Dispose();
				}
				catch (MySqlException) { }
			}
		}
		public static int ReturnItemQua(string GU)
        {
			bool ok = ItemsList.TryGetValue(Convert.ToInt32(GU), out ItemList I);
			if (ok)
				return I.Qua;
			return -1;
        }
	}
}