using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using Tools_protocol.Emulators;
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

		/// <summary>Table des butins selon le profil d'émulateur courant.</summary>
		public static string TableDrops
		{
			get
			{
				return EmulatorRegistry.Current.Table("drops");
			}
		}

		static DropsList()
		{
			DropsList.AllDrops = new Dictionary<uint, DropsList>();
			DropsList.DropsName = new List<string>();
		}

		/// <summary>
		/// Lit un butin. StarLoco n'a pas de colonne id (la clef est monstre + objet) :
		/// l'identifiant vaut alors 0 et <see cref="Load_Drops"/> en attribue un.
		/// </summary>
		public DropsList(IDataReader reader)
		{
			this.Id = Number(reader, "id");
			this.MonsterName = Text(reader, "monsterName");
			this.MonsterId = Number(reader, "monsterid");
			this.ObjectName = Text(reader, "objectName");
			this.ObjectId = Number(reader, "objectid");
			this.PercentGrade1 = Percent(reader, "percentGrade1");
			this.PercentGrade2 = Percent(reader, "percentGrade2");
			this.PercentGrade3 = Percent(reader, "percentGrade3");
			this.PercentGrade4 = Percent(reader, "percentGrade4");
			this.PercentGrade5 = Percent(reader, "percentGrade5");
			this.Ceil = Number(reader, "ceil");
			this.Action = Text(reader, "action");
			this.Level = Ordinal(reader, "level") < 0 ? -1 : Convert.ToInt32(reader[Ordinal(reader, "level")]);
		}

		private static int Ordinal(IDataRecord reader, string column)
		{
			for (int index = 0; index < reader.FieldCount; index++)
				if (string.Equals(reader.GetName(index), column, StringComparison.OrdinalIgnoreCase))
					return index;
			return -1;
		}

		private static string Text(IDataRecord reader, string column)
		{
			int index = Ordinal(reader, column);
			return index < 0 || reader.IsDBNull(index) ? "" : Convert.ToString(reader.GetValue(index));
		}

		private static uint Number(IDataRecord reader, string column)
		{
			int index = Ordinal(reader, column);
			return index < 0 || reader.IsDBNull(index) ? 0 : Convert.ToUInt32(reader.GetValue(index));
		}

		private static decimal Percent(IDataRecord reader, string column)
		{
			int index = Ordinal(reader, column);
			return index < 0 || reader.IsDBNull(index) ? 0 : Convert.ToDecimal(reader.GetValue(index));
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
			string query = QueryBuilder.SelectFromQuery(new string[] { "*" }, TableDrops, "", "");

			using (MySqlConnection connection = new MySqlConnection(EmulatorRegistry.ConnectionFor("drops")))
			{
				try
				{
					connection.Open();
					var loaded = new Dictionary<uint, DropsList>();
					var names = new List<string>();
					using (var command = new MySqlCommand(query, connection))
					using (var reader = command.ExecuteReader())
					{
						bool hasId = Ordinal(reader, "id") >= 0;
						while (reader.Read())
						{
							DropsList D = new DropsList(reader);
							if (!hasId) D.Id = (uint)loaded.Count + 1;
							loaded.Add(D.Id, D);
							names.Add(D.ObjectName);
						}
					}
					AllDrops = loaded;
					DropsName = names;
					Drops_Count = names.Count;
				}
				catch (MySqlException) {  }
			}
		}
	}
}
