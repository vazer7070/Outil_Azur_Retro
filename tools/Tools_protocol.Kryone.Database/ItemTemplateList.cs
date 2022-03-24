using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using Tools_protocol.Data;
using Tools_protocol.Json;
using Tools_protocol.Query;

namespace Tools_protocol.Kryone.Database
{
	public class ItemTemplateList
	{
		public static Dictionary<int, ItemTemplateList> ItemFullDico;

		public static List<string> U;

		public static List<string> StatsItems;

		public static int CountItems;

		public string ArmesInfos
		{
			get;
			set;
		}

		public int AvgPrice
		{
			get;
			set;
		}

		public string Conditions
		{
			get;
			set;
		}

		public int Doplons
		{
			get;
			set;
		}

		public int Exchangeable
		{
			get;
			set;
		}

		public int Heroique
		{
			get;
			set;
		}

		public int Id
		{
			get;
			set;
		}

		public int Level
		{
			get;
			set;
		}

		public string Name
		{
			get;
			set;
		}

		public int Panoplie
		{
			get;
			set;
		}

		public int Pod
		{
			get;
			set;
		}

		public int Points
		{
			get;
			set;
		}

		public int Prix
		{
			get;
			set;
		}

		public int Sold
		{
			get;
			set;
		}

		public string StatsTemplate
		{
			get;
			set;
		}

		public static string TableTemplate
		{
			get
			{
				return JsonManager.SearchAuth("Template");
			}
		}

		public int Type
		{
			get;
			set;
		}

		static ItemTemplateList()
		{
			ItemFullDico = new Dictionary<int, ItemTemplateList>();
			U = new List<string>();
		    StatsItems = new List<string>();
		}

		public ItemTemplateList(IDataReader reader)
		{
			Id = (int)reader["id"];
			Type = (int)reader["type"];
			Name = (string)reader["name"];
			Level = (int)reader["level"];
			StatsTemplate = (string)reader["statstemplate"];
			Pod = (int)reader["pod"];
			Panoplie = (int)reader["panoplie"];
			Prix = (int)reader["prix"];
			Conditions = (string)reader["conditions"];
			ArmesInfos = (string)reader["armesinfos"];
			Sold = (int)reader["sold"];
			AvgPrice = (int)reader["avgprice"];
			Points = (int)reader["points"];
			Doplons = (int)reader["doplons"];
			Exchangeable = (int)reader["exchangeable"];
			Heroique = (int)reader["heroique"];
		}

		public static void AddNameByData(int type)
		{
			string query = QueryBuilder.SelectFromQuery(new string[] { "name" }, TableTemplate, "type", type.ToString());

			using (MySqlConnection connection = new MySqlConnection(DatabaseManager.ConnectionString))
			{
				try
				{
					connection.Open();
					MySqlDataReader R = new MySqlCommand(query, connection).ExecuteReader();
					if (R != null)
					{
						while (R.Read())
						{
							U.Add(R["name"].ToString());
						}
					}
					R.Close();
					R.Dispose();
					connection.Close();
					connection.Dispose();
				}
				catch (MySqlException) { }
			}
		}

		public static void DeleteItem(string name)
		{
			int id = ReturnItemId(name);
			DatabaseManager2.UpdateQuery(QueryBuilder.DeleteFromQuery(ItemList.TableItems, "template", id.ToString()));
			DatabaseManager.UpdateQuery(QueryBuilder.DeleteFromQuery(TableTemplate, "id", id.ToString()));
		}

		public static string GetItem(int template, int sw)
		{
			ItemTemplateList item;
			string name;
			if (ItemFullDico.TryGetValue(template, out item))
			{
				switch (sw)
				{
					case 1:
					{
						name = item.Name;
						break;
					}
					case 2:
					{
						name = item.Type.ToString();
						break;
					}
					case 3:
					{
						name = item.Pod.ToString();
						break;
					}
					case 4:
					{
						name = item.AvgPrice.ToString();
						break;
					}
					case 5:
					{
						name = item.Panoplie.ToString();
						break;
					}
					case 6:
					{
						name = item.Level.ToString();
						break;
					}
					case 7:
					{
						name = item.ArmesInfos;
						break;
					}
					case 8:
					{
						name = item.StatsTemplate;
						break;
					}
					case 9:
					{
						name = item.Conditions;
						break;
					}
					default:
					{
						name = template.ToString();
						return name;
					}
				}
			}
			else
			{
				name = template.ToString();
				return name;
			}
			return name;
		}

		public static void Load_Item()
		{
			string query = QueryBuilder.SelectFromQuery(new string[] { "*" }, TableTemplate, "", "");
			using (MySqlConnection connection = new MySqlConnection(DatabaseManager.ConnectionString))
			{
				try
				{
					connection.Open();
					MySqlDataReader lecteur = new MySqlCommand(query, connection).ExecuteReader();
					ItemTemplateList record = null;
					while (lecteur.Read())
					{
						record = new ItemTemplateList(lecteur);
						if (!ItemFullDico.ContainsKey(record.Id))
						{
							ItemFullDico.Add(record.Id, record);
						}
					}
					CountItems = ItemFullDico.Count;
					lecteur.Close();
					lecteur.Dispose();
					connection.Close();
					connection.Dispose();
				}
				catch (MySqlException) { }
			}
		}

		public static void ParseTemplate(int id)
		{
			string Stats = ItemTemplateList.GetItem(id, 8);
			string ajust = "";
			if (!string.IsNullOrEmpty(Stats))
			{
				string[] strArrays = Stats.Split(new char[] { ',' });
				for (int i = 0; i < (int)strArrays.Length; i++)
				{
					string JustOne = strArrays[i];
					string[] Parse_one = JustOne.Split(new char[] { '#' });
					string stats = EffectsListing.ReturnStatItem(Parse_one[0]);
					int min = Convert.ToInt32(Parse_one[1], 16);
					int max = Convert.ToInt32(Parse_one[2], 16);
					ajust = (max.Equals(0) ? stats.Replace("$1", string.Format("{0}", min)).Replace("a $2 ", "") : stats.Replace("$1", string.Format("{0}", min)).Replace("$2", string.Format("{0}", max)));
					StatsItems.Add(ajust);
				}
			}
		}

		public static int ReturnItemId(string name)
		{
			return ItemFullDico.FirstOrDefault(x => x.Value.Name == name).Key;
		}

		public static ItemTemplateList ReturnItemName(int id)
		{
			return ItemFullDico[id];
		}

		public static int ReturnItemType(int value)
		{
			KeyValuePair<int, ItemTemplateList> keyValuePair = ItemFullDico.FirstOrDefault<KeyValuePair<int, ItemTemplateList>>((KeyValuePair<int, ItemTemplateList> x) => x.Value.Type == value);
			return keyValuePair.Key;
		}
	}
}