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
		public static Dictionary<int, MonsterList> AllMonster;

		public static int Monsters_count;

		public static Dictionary<string, string> MonsterGrade;

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

		static MonsterList()
		{
			AllMonster = new Dictionary<int, MonsterList>();
			MonsterGrade = new Dictionary<string, string>();
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
			string str;
			if (AllMonster.ContainsKey(id))
			{
				KeyValuePair<int, MonsterList> keyValuePair = AllMonster.FirstOrDefault<KeyValuePair<int, MonsterList>>((KeyValuePair<int, MonsterList> x) => x.Key == id);
				int C = keyValuePair.Value.Capturable;
				if (C == 1)
				{
					str = "oui";
					return str;
				}
				else if (C == 0)
				{
					str = "non";
					return str;
				}
			}
			str = null;
			return str;
		}

		public static void Load_Monster()
		{
			MySqlDataReader reader = DatabaseManager2.SelectQuery(QueryBuilder.SelectFromQuery(new string[] { "*" }, TableMonstre, "", ""));
			while (reader.Read())
			{
				MonsterList MonsterRecord = new MonsterList(reader);
				AllMonster.Add(MonsterRecord.Id, MonsterRecord);
			}
			Monsters_count = AllMonster.Count;
			reader.Close();
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
					catch (Exception mp)
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
			string str;
			if (AllMonster.ContainsKey(id))
			{
				KeyValuePair<int, MonsterList> keyValuePair = AllMonster.FirstOrDefault<KeyValuePair<int, MonsterList>>((KeyValuePair<int, MonsterList> x) => x.Key == id);
				int A = keyValuePair.Value.Align;
				if (A.Equals(-1))
				{
					str = "Sans alignement";
				}
				else if (A.Equals(0))
				{
					str = "Neutre";
				}
				else if (!A.Equals(1))
				{
					if (A.Equals(2))
					{
						str = "Brakmarien";
						return str;
					}
					str = null;
					return str;
				}
				else
				{
					str = "Bontarien";
				}
			}
			else
			{
				str = null;
				return str;
			}
			return str;
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
				KeyValuePair<int, MonsterList> keyValuePair = AllMonster.FirstOrDefault<KeyValuePair<int, MonsterList>>((KeyValuePair<int, MonsterList> x) => x.Key == id);
				int MinK = keyValuePair.Value.MinKamas;
				keyValuePair = AllMonster.FirstOrDefault((KeyValuePair<int, MonsterList> x) => x.Key == id);
				int MxK = keyValuePair.Value.MaxKamas;
				str = MxK.Equals(0) ? "0" : string.Format("de {0} à {1}", MinK, MxK);
			}
			return str;
		}

		public static int ReturnMonsterByName(string name)
		{
			KeyValuePair<int, MonsterList> keyValuePair = MonsterList.AllMonster.FirstOrDefault<KeyValuePair<int, MonsterList>>((KeyValuePair<int, MonsterList> x) => x.Value.Name == name);
			return keyValuePair.Key;
		}

		public static string ReturnMonstersInfos(int id, int sw)
		{
			KeyValuePair<int, MonsterList> keyValuePair;
			string name;
			int gFXid;
			if (!MonsterList.AllMonster.ContainsKey(id))
			{
				name = null;
			}
			else
			{
				switch (sw)
				{
					case 1:
					{
						keyValuePair = MonsterList.AllMonster.FirstOrDefault<KeyValuePair<int, MonsterList>>((KeyValuePair<int, MonsterList> x) => x.Key == id);
						name = keyValuePair.Value.Name;
						break;
					}
					case 2:
					{
						keyValuePair = MonsterList.AllMonster.FirstOrDefault<KeyValuePair<int, MonsterList>>((KeyValuePair<int, MonsterList> x) => x.Key == id);
						gFXid = keyValuePair.Value.GFXid;
						name = gFXid.ToString();
						break;
					}
					case 3:
					{
						keyValuePair = MonsterList.AllMonster.FirstOrDefault<KeyValuePair<int, MonsterList>>((KeyValuePair<int, MonsterList> x) => x.Key == id);
						gFXid = keyValuePair.Value.Size;
						name = gFXid.ToString();
						break;
					}
					default:
					{
						name = string.Format("{0} monstre inconnu", id);
						break;
					}
				}
			}
			return name;
		}
	}
}