using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Runtime.CompilerServices;
using Tools_protocol.Emulators;
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

		/// <summary>Table des exemplaires d'objets selon le profil d'émulateur courant.</summary>
		public static string TableItems
		{
			get
			{
				return EmulatorRegistry.Current.Table("items");
			}
		}

		public int Template
		{
			get;
			set;
		}

		/// <summary>
		/// Lit un exemplaire : les colonnes portent les noms réels du profil courant
		/// (guid/qua/pos pour Kryone, id/quantity/position pour StarLoco).
		/// </summary>
		public ItemList(IDataReader reader)
		{
			EmulatorProfile emulator = EmulatorRegistry.Current;
			Guid = Convert.ToInt32(reader[emulator.ItemColumn("guid")]);
			Template = Convert.ToInt32(reader[emulator.ItemColumn("template")]);
			Qua = Convert.ToInt32(reader[emulator.ItemColumn("qua")]);
			Pos = Convert.ToInt32(reader[emulator.ItemColumn("pos")]);
			Stat = Convert.ToString(reader[emulator.ItemColumn("stats")]);
			Puit = Convert.ToInt32(reader[emulator.ItemColumn("puit")]);
		}

		public static void AddItemIdToList()
		{
			string[] args = new string[] { "*" };
			string query = QueryBuilder.SelectFromQuery(args, TableItems, "", "");

			using (MySqlConnection connection = new MySqlConnection(EmulatorRegistry.ConnectionFor("items")))
			{
				try
				{
					connection.Open();
					ItemsId.Clear();
					ItemsList.Clear();
					ItemList G = null;
					MySqlDataReader lecteur = new MySqlCommand(query, connection).ExecuteReader();
					while (lecteur.Read())
					{
						G = new ItemList(lecteur);
						ItemsId.Add(G.Guid, G.Template);
						ItemsList.Add(G.Guid, G);
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
