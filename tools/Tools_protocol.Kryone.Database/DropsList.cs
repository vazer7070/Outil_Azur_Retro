using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using Tools_protocol.Json;
using Tools_protocol.Query;

namespace Tools_protocol.Kryone.Database
{
	public class DropsList
	{
		public static Dictionary<uint, DropsList> AllDrops;

		public static List<string> DropsName;

		public static int Drops_Count;

		public string Action
		{
			get;
			set;
		}

		public uint Ceil
		{
			get;
			set;
		}

		public uint Id
		{
			get;
			set;
		}

		public int Level
		{
			get;
			set;
		}

		public uint MonsterId
		{
			get;
			set;
		}

		public string MonsterName
		{
			get;
			set;
		}

		public uint ObjectId
		{
			get;
			set;
		}

		public string ObjectName
		{
			get;
			set;
		}

		public decimal PercentGrade1
		{
			get;
			set;
		}

		public decimal PercentGrade2
		{
			get;
			set;
		}

		public decimal PercentGrade3
		{
			get;
			set;
		}

		public decimal PercentGrade4
		{
			get;
			set;
		}

		public decimal PercentGrade5
		{
			get;
			set;
		}

		public static string TableDrops
		{
			get
			{
				return JsonManager.SearchWorld("drops");
			}
		}

		static DropsList()
		{
			DropsList.AllDrops = new Dictionary<uint, DropsList>();
			DropsList.DropsName = new List<string>();
		}

		public DropsList(IDataReader reader)
		{
			this.Id = (uint)reader["id"];
			this.MonsterName = (string)reader["monsterName"];
			this.MonsterId = (uint)reader["monsterid"];
			this.ObjectName = (string)reader["objectName"];
			this.ObjectId = (uint)reader["objectid"];
			this.PercentGrade1 = (decimal)reader["percentGrade1"];
			this.PercentGrade2 = (decimal)reader["percentGrade2"];
			this.PercentGrade3 = (decimal)reader["percentGrade3"];
			this.PercentGrade4 = (decimal)reader["percentGrade4"];
			this.PercentGrade5 = (decimal)reader["percentGrade5"];
			this.Ceil = (uint)reader["ceil"];
			this.Action = (string)reader["action"];
			this.Level = (int)reader["level"];
		}

		public static uint DropId(string data)
		{
			KeyValuePair<uint, DropsList> keyValuePair = DropsList.AllDrops.FirstOrDefault<KeyValuePair<uint, DropsList>>((KeyValuePair<uint, DropsList> x) => x.Value.ObjectName == data);
			return keyValuePair.Key;
		}

		public static uint DropIdByMonster(int data)
		{
			KeyValuePair<uint, DropsList> keyValuePair = DropsList.AllDrops.FirstOrDefault<KeyValuePair<uint, DropsList>>((KeyValuePair<uint, DropsList> x) => (long)x.Value.MonsterId == (long)data);
			return keyValuePair.Key;
		}

		public static string DropInfo(uint id, int sw)
		{
			DropsList D;
			string monsterName;
			if (DropsList.AllDrops.TryGetValue(id, out D))
			{
				switch (sw)
				{
					case 1:
					{
						monsterName = D.MonsterName;
						break;
					}
					case 2:
					{
						monsterName = D.MonsterId.ToString();
						break;
					}
					case 3:
					{
						monsterName = D.ObjectId.ToString();
						break;
					}
					case 4:
					{
						monsterName = D.PercentGrade1.ToString();
						break;
					}
					case 5:
					{
						monsterName = D.PercentGrade2.ToString();
						break;
					}
					case 6:
					{
						monsterName = D.PercentGrade3.ToString();
						break;
					}
					case 7:
					{
						monsterName = D.PercentGrade4.ToString();
						break;
					}
					case 8:
					{
						monsterName = D.PercentGrade5.ToString();
						break;
					}
					case 9:
					{
						monsterName = D.Ceil.ToString();
						break;
					}
					case 10:
					{
						monsterName = D.Level.ToString();
						break;
					}
					case 11:
					{
						monsterName = D.Action;
						break;
					}
					case 12:
					{
						monsterName = D.ObjectName;
						break;
					}
					default:
					{
						monsterName = null;
						return monsterName;
					}
				}
			}
			else
			{
				monsterName = null;
				return monsterName;
			}
			return monsterName;
		}

		public static void Load_Drops()
		{
			MySqlDataReader reader = DatabaseManager2.SelectQuery(QueryBuilder.SelectFromQuery(new string[] { "*" }, TableDrops, "", ""));
			DropsList D = null;
			while (reader.Read())
			{
				D = new DropsList(reader);
				AllDrops.Add(D.Id, D);
				DropsName.Add(D.ObjectName);
				Drops_Count = DropsName.Count;
			}
			reader.Close();
			reader.Dispose();
		}
	}
}