using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using Tools_protocol.Json;
using Tools_protocol.Query;

namespace Tools_protocol.Kryone.Database
{
	public class MonsterList
	{
		public static Dictionary<int, MonsterList> AllMonster = new Dictionary<int, MonsterList>();
			
		public static int Monsters_count;

		public static Dictionary<string, string> MonsterGrade = new Dictionary<string, string>();

		public int AggroDistance
		{
			get;
			set;
		}

		public int AI_Type
		{
			get;
			set;
		}

		public int Align
		{
			get;
			set;
		}

		public int Capturable
		{
			get;
			set;
		}

		public string Colors
		{
			get;
			set;
		}

		public string Exps
		{
			get;
			set;
		}

		public int GFXid
		{
			get;
			set;
		}

		public string Grades
		{
			get;
			set;
		}

		public string IaModels
		{
			get;
			set;
		}

		public int Id
		{
			get;
			set;
		}

		public string Inits
		{
			get;
			set;
		}

		public int MaxKamas
		{
			get;
			set;
		}

		public int MinKamas
		{
			get;
			set;
		}

		public string Name
		{
			get;
			set;
		}

		public string Pdvs
		{
			get;
			set;
		}

		public string Points
		{
			get;
			set;
		}

		public int Size
		{
			get;
			set;
		}

		public string Spells
		{
			get;
			set;
		}

		public string Stats
		{
			get;
			set;
		}

		public string Statsinfo
		{
			get;
			set;
		}

		public static string TableMonstre
		{
			get
			{
				return JsonManager.SearchWorld("monstres");
			}
		}

		public int Type
		{
			get;
			set;
		}

		public MonsterList(IDataReader reader)
		{
			
			this.Id = (int)reader["id"];
			this.Name = (string)reader["name"];
			this.GFXid = (int)reader["gfxID"];
			this.Align = (int)reader["align"];
			this.Grades = (string)reader["grades"];
			this.Colors = (string)reader["colors"];
			this.Stats = (string)reader["stats"];
			this.Statsinfo = (string)reader["statsInfos"];
			this.Spells = (string)reader["spells"];
			this.Pdvs = (string)reader["pdvs"];
			this.Points = (string)reader["points"];
			this.Inits = (string)reader["inits"];
			this.MinKamas = (int)reader["minKamas"];
			this.MaxKamas = (int)reader["maxKamas"];
			this.Exps = (string)reader["exps"];
			this.AI_Type = (int)reader["AI_Type"];
			this.Capturable = (int)reader["Capturable"];
			this.Type = (int)reader["type"];
			this.AggroDistance = (int)reader["aggroDistance"];
			this.IaModels = (string)reader["iaModels"];
			this.Size = (int)reader["size"];
		}

		public static string CapturableOrNot(int id)
		{
			if (AllMonster.ContainsKey(id))
			{
				int C = AllMonster.FirstOrDefault(x => x.Key == id).Value.Capturable;
                switch (C)
                {
					case 0:
						return "Non";
					case 1:
						return "Oui";
					default:
						return "Non";
                }
			}
			else
				return $"{id}";
		}

		public static void Load_Monster()
		{
			string query = QueryBuilder.SelectFromQuery(new string[] { "*" }, TableMonstre, "", "");

			using (MySqlConnection connection = new MySqlConnection(DatabaseManager2.ConnectionString))
			{
				try
				{
					connection.Open();
					MySqlDataReader reader = new MySqlCommand(query, connection).ExecuteReader();
					while (reader.Read())
					{
						MonsterList MonsterRecord = new MonsterList(reader);
						AllMonster.Add(MonsterRecord.Id, MonsterRecord);
					}
					Monsters_count = AllMonster.Count;
					reader.Close();
					reader.Dispose();
					connection.Close();
					connection.Dispose();
				}
				catch (MySqlException) { }
			}
		}

		public static void ParseGrade(int id)
		{
			if (AllMonster.ContainsKey(id))
			{
				string G = AllMonster.FirstOrDefault(x => x.Key == id).Value.Grades;
				string S = AllMonster.FirstOrDefault(x => x.Key == id).Value.Stats;
				string P = AllMonster.FirstOrDefault(x => x.Key == id).Value.Points;
				string V = AllMonster.FirstOrDefault(x => x.Key == id).Value.Pdvs;
				string X = AllMonster.FirstOrDefault(x => x.Key == id).Value.Exps;
				string SP = AllMonster.FirstOrDefault(x => x.Key == id).Value.Spells;
				int Sc = G.Split(new char[] { '|' }).Length;
				for (int i = 0; i < Sc; i++)
				{
					//2@1;5;5;-9;-9;5;3|3@2;6;6;-8;-8;6;4|4@3;7;7;-7;-7;7;5|5@4;8;8;-6;-6;8;6|6@5;9;9;-5;-5;9;7|1@

					string[] arrayG;
					string[] arrayS;
					string[] arrayP;
					string[] arrayV;
					string[] arrayX;
					string[] arraySP;
					string m = "";
					string j = "";
					string k = "";
					string b = "";
					string e = "";
					string l = "";
					string n = "";
					string o = "";
                    try
                    {
						arrayG = G.Split(new char[] { '|' });
						arrayS = S.Split(new char[] { '|' });
						arrayP = P.Split(new char[] { '|' });
						arrayV = V.Split(new char[] { '|' });
						arrayX = X.Split(new char[] { '|' });
						arraySP = SP.Split(new char[] { '|' });
						m = arrayG[i];
						j = arrayS[i];
						k = arrayP[i];
						b = arrayV[i];
						e = arrayX[i];
						l = arraySP[i];
						n = m.Split(new char[] { '@' })[0];
						o = m.Split(new char[] { '@' })[1];
                    }
					catch (Exception)
                    {
						continue;
                    }

					if (!MonsterGrade.ContainsKey(n))
					{
						if (string.IsNullOrEmpty(o))
						{
							MonsterGrade.Add("", "");
						}
						else
						{
							if (l.Contains("||||"))
							{
								string u = l.Replace("||||", "");
							}
							if (l.Contains(';'))
							{
								MonsterGrade.Add(string.Format("Niveau {0} ({1})", n, i), string.Concat(new string[] { m.Split(new char[] { '@' })[1], "/", j, "/", k, "/", b, "/", e, "%", l }));
							}
							else if (l.Equals("||||") ? false : !string.IsNullOrEmpty(l))
							{
								string NameSpells = SpellsList.Return_spells(Convert.ToInt32(l.Split(new char[] { '@' })[0]));
								MonsterGrade.Add(string.Format("Niveau {0} ({1})", n, i), string.Concat(new string[] { m.Split(new char[] { '@' })[1], "/", j, "/", k, "/", b, "/", e, "/", NameSpells, " (lvl:", l.Split(new char[] { '@' })[1], ")" }));
							}
							else
							{
								MonsterGrade.Add(string.Format("Niveau {0} ({1})", n, i), string.Concat(new string[] { m.Split(new char[] { '@' })[1], "/", j, "/", k, "/", b, "/", e, "/Pas de sorts" }));
							}
						}
					}
				}
			}
		}

		public static string ReturnAlignMonster(int id)
		{
			
			if (AllMonster.ContainsKey(id))
			{
				int A = AllMonster.FirstOrDefault(x => x.Key == id).Value.Align;
                switch (A)
                {
					case -1:
						return "Sans alignement";
				    case 0:
						return "Neutre";
					case 1:
						return "Bontarien";
					case 2:
						return "Brakmarien";
					default:
						return "";
                }
			}
			return $"{id}";
		}

		public static string ReturnKamas(int id)
		{
			string str;
			if (!AllMonster.ContainsKey(id))
			{
				str = null;
			}
			else
			{
				int MinK = AllMonster.FirstOrDefault(x => x.Key == id).Value.MinKamas;
				int MxK = AllMonster.FirstOrDefault(x => x.Key == id).Value.MaxKamas;
				str = $"de {MinK} à {MxK}";
			}
			return str;
		}

		public static int ReturnMonsterByName(string name)
		{
			return AllMonster.FirstOrDefault(x => x.Value.Name == name).Key;
		}

		public static string ReturnMonstersInfos(int id, int sw)
		{
			
			
			if (!AllMonster.ContainsKey(id))
			{
				return $"{id} inconnue.";
			}
			else
			{
				switch (sw)
				{
					case 1:
					{
						return AllMonster.FirstOrDefault(x => x.Key == id).Value.Name;
					}
					case 2:
					{
						return AllMonster.FirstOrDefault(x => x.Key == id).Value.GFXid.ToString();
					}
					case 3:
					{
							return AllMonster.FirstOrDefault(x => x.Key == id).Value.Size.ToString();
					}
					default:
					{
							return $"Le monstre {id} est inconnu.";
					}
				}
			}
		}
	}
}