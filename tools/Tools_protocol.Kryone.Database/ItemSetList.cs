using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Tools_protocol.Data;
using Tools_protocol.Managers;
using Tools_protocol.Query;

namespace Tools_protocol.Kryone.Database
{
	public class ItemSetList 
	{ 
			
		public static Dictionary<int, ItemSetList> AllItemsInSet = new Dictionary<int, ItemSetList>();

		public static List<string> SetName = new List<string>();

		public static List<string> Name_Effects = new List<string>();

		public static int Count_Pano;

		public static string E;

		public static string[] L;

		public string Bonus
		{
			get;
			set;
		}

		public int Id
		{
			get;
			set;
		}

		public string Items
		{
			get;
			set;
		}

		public string Name
		{
			get;
			set;
		}

		public static string TableSet
		{
			get
			{
				return EmuManager.ReturnTable("panoplies", EmuManager.EMUSELECTED);
			}
		}

		public ItemSetList(IDataReader reader)
		{
			Id = (int)reader["id"];
			Name = (string)reader["name"];
			Items = (string)reader["items"];
			Bonus = (string)reader["bonus"];
		}

		public static void LoadPano()
		{
			string query = QueryBuilder.SelectFromQuery(new string[] { "*" }, TableSet, "", "");
			using (MySqlConnection connection = new MySqlConnection(DatabaseManager.ConnectionString))
			{
				try
				{
					connection.Open();
					ItemSetList set = null;
					MySqlDataReader R = new MySqlCommand(query, connection).ExecuteReader();
					while (R.Read())
					{
						set = new ItemSetList(R);
						AllItemsInSet.Add(set.Id, set);
						SetName.Add(set.Name);
					}
					Count_Pano = AllItemsInSet.Count;
					R.Close();
					R.Dispose();
					connection.Close();
					connection.Dispose();
				}
				catch (MySqlException) { }
			}
		}

		public static void ReturnEffect(int id, int surplus)
		{
			if (surplus <= 0)
			{
				E = ReturnItems(id, 2).Split(new char[] { ';' })[0];
			}
			else
			{
				E = ReturnItems(id, 2).Split(new char[] { ';' })[surplus];
			}
			string[] strArrays = E.Split(new char[] { ',' });
			for (int num = 0; num < (int)strArrays.Length; num++)
			{
				string l = strArrays[num];
				if (!l.Contains(";"))
				{
					string eff = l.Split(new char[] { ':' })[0];
					int V = Convert.ToInt32(l.Split(new char[] { ':' })[1]);
					string Def = EffectsListing.ReturnDef(eff);
					string S = Def.Replace("$", string.Format("{0}", V));
					Name_Effects.Add(S);
				}
				else
				{
					string[] strArrays1 = l.Split(new char[] { ';' });
					for (int j = 0; j < (int)strArrays1.Length; j++)
					{
						string i = strArrays1[j];
						string[] strArrays2 = i.Split(new char[] { ',' });
						for (int k = 0; k < (int)strArrays2.Length; k++)
						{
							string R = strArrays2[k];
							string eff = "";
							int V = 0;
							if (!string.IsNullOrEmpty(R.Split(new char[] { ':' })[0]))
							{
								if (!string.IsNullOrEmpty(R.Split(new char[] { ':' })[1]))
								{
									eff = l.Split(new char[] { ':' })[0];
									V = Convert.ToInt32(R.Split(new char[] { ':' })[1]);
									string Def = EffectsListing.ReturnDef(eff);
									string S = Def.Replace("$", V.ToString());
									Name_Effects.Add(S);
								}
							}
						}
					}
				}
			}
		}

		public static string ReturnItems(int id, int sw)
		{
			ItemSetList set;
			string items;
			if (!AllItemsInSet.TryGetValue(id, out set))
			{
				items = null;
			}
			else
			{
				int num = sw;
				if (num == 1)
				{
					items = set.Items;
				}
				else
				{
					items = (num == 2 ? set.Bonus : id.ToString());
				}
			}
			return items;
		}

		public static int ReturnPanoId(string name)
		{
			return AllItemsInSet.FirstOrDefault(x => x.Value.Name == name).Key;
		}
	}
}