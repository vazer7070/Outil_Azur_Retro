using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Tools_protocol.Data;
using Tools_protocol.Json;
using Tools_protocol.Query;

namespace Tools_protocol.Kryone.Database
{
	public class SpellsList
	{
		public static int CRIT;

		public static int NORM;

		public static Dictionary<int, SpellsList> AllSpells;

		public static List<string> SpellsName;

		public static List<string> EffectLvl1;

		public static List<string> EffectLvl2;

		public static List<string> EffectLvl3;

		public static List<string> EffectLvl4;

		public static List<string> EffectLvl5;

		public static List<string> EffectLvl6;

		public static List<string> SpellsShow;

		public static int CountSpells;

		public int Durée
		{
			get;
			set;
		}

		public string EffectTarget
		{
			get;
			set;
		}

		public int Id
		{
			get;
			set;
		}

		public string Lvl1
		{
			get;
			set;
		}

		public string Lvl2
		{
			get;
			set;
		}

		public string Lvl3
		{
			get;
			set;
		}

		public string Lvl4
		{
			get;
			set;
		}

		public string Lvl5
		{
			get;
			set;
		}

		public string Lvl6
		{
			get;
			set;
		}

		public string Nom
		{
			get;
			set;
		}

		public int Sprite
		{
			get;
			set;
		}

		public string SpriteInfos
		{
			get;
			set;
		}

		public static string TableSort
		{
			get
			{
				return JsonManager.SearchAuth("sort");
			}
		}

		public int Type
		{
			get;
			set;
		}

		static SpellsList()
		{
			AllSpells = new Dictionary<int, SpellsList>();
			SpellsName = new List<string>();
			EffectLvl1 = new List<string>();
			EffectLvl2 = new List<string>();
			EffectLvl3 = new List<string>();
			EffectLvl4 = new List<string>();
			EffectLvl5 = new List<string>();
			EffectLvl6 = new List<string>();
			SpellsShow = new List<string>();
		}

		public SpellsList(IDataReader reader)
		{
			this.Id = (int)reader["id"];
			this.Nom = (string)reader["nom"];
			this.Sprite = (int)reader["Sprite"];
			this.SpriteInfos = (string)reader["spriteInfos"];
			this.Lvl1 = (string)reader["lvl1"];
			this.Lvl2 = (string)reader["lvl2"];
			this.Lvl3 = (string)reader["lvl3"];
			this.Lvl4 = (string)reader["lvl4"];
			this.Lvl5 = (string)reader["lvl5"];
			this.Lvl6 = (string)reader["lvl6"];
			this.EffectTarget = (string)reader["effectTarget"];
			this.Type = (int)reader["type"];
			this.Durée = (int)reader["durer"];
		}

		public static void AddSpellsToList(string data)
		{
			MySqlDataReader R = DatabaseManager.SelectQuery(QueryBuilder.SelectFromQuery(new string[] { "*" }, SpellsList.TableSort, "id", data.Split(new char[] { ';' })[0]));
			string z = data.Split(new char[] { ';' })[1];
			while (R.Read())
			{
				SpellsShow.Add(string.Concat(R["nom"].ToString(), " - niveau: ", z));
			}
			R.Close();
		}

		public static string CaracParse(string un, bool decal)
		{
			string level;
			string P_M;
			string ByT;
			int PACOST = 6;
			StringBuilder S = new StringBuilder();
			if (!decal)
			{
				S.Append("?");
				if (!un.Split(new char[] { ',' })[2].Equals("-1"))
				{
					S.Append(un.Split(new char[] { ',' })[2]);
				}
				else
				{
					S.Append(PACOST);
				}
				S.Append("?");
				if ((!un.Split(new char[] { ',' })[3].Equals("0") ? false : !un.Split(new char[] { ',' })[2].Equals("0")))
				{
					S.Append(un.Split(new char[] { ',' })[2]);
				}
				else if ((Convert.ToInt32(un.Split(new char[] { ',' })[2]) <= Convert.ToInt32(un.Split(new char[] { ',' })[3]) ? true : un.Split(new char[] { ',' })[3].Equals("0")))
				{
					S.Append(string.Concat(un.Split(new char[] { ',' })[2], " à ", un.Split(new char[] { ',' })[3]));
				}
				else
				{
					S.Append(string.Concat(un.Split(new char[] { ',' })[3], " à ", un.Split(new char[] { ',' })[2]));
				}
				S.Append("?");
				S.Append(un.Split(new char[] { ',' })[4]);
				S.Append("?");
				S.Append(un.Split(new char[] { ',' })[5]);
				S.Append("?");
				S.Append(un.Split(new char[] { ',' })[6]);
				S.Append("?");
				S.Append(un.Split(new char[] { ',' })[7]);
				S.Append("?");
				S.Append(un.Split(new char[] { ',' })[8]);
				S.Append("?");
				S.Append(un.Split(new char[] { ',' })[9]);
				P_M = un.Split(new char[] { ',' })[10].Trim();
				if ((P_M.Equals("false") ? true : P_M.Equals("true")))
				{
					S.Append("?");
					S.Append(P_M);
					S.Append("?");
				}
				S.Append("?");
				if (un.Split(new char[] { ',' })[11].Trim() != "0")
				{
					S.Append(un.Split(new char[] { ',' })[11]);
				}
				else
				{
					S.Append("autant que possible");
				}
				S.Append("?");
				if (un.Split(new char[] { ',' })[12].Trim() != "0")
				{
					S.Append(string.Concat(un.Split(new char[] { ',' })[12], " tour(s)"));
				}
				else
				{
					S.Append("Tous les tours");
				}
				S.Append("?");
				ByT = un.Split(new char[] { ',' })[13].Trim();
				if (ByT != "0")
				{
					S.Append(ByT);
				}
				else
				{
					S.Append("Immédiat");
				}
				S.Append("?");
				if (!SpellsList.ReturnZone(un.Split(new char[] { ',' })[14]).Contains("invalide"))
				{
					S.Append(SpellsList.ReturnZone(un.Split(new char[] { ',' })[14]));
				}
				else
				{
					S.Append(SpellsList.ReturnZone(un.Split(new char[] { ',' })[15]));
				}
				S.Append("?");
				level = un.Split(new char[] { ',' })[17].Trim();
				if (!level.Contains(";"))
				{
					if (level != "0")
					{
						S.Append(level);
					}
					else
					{
						S.Append("all");
					}
					S.Append("?");
					S.Append(un.Split(new char[] { ',' })[18]);
				}
				else
				{
					level = un.Split(new char[] { ',' })[18].Trim();
					if (level != "0")
					{
						S.Append(level);
					}
					else
					{
						S.Append("all");
					}
					S.Append("?");
					S.Append(un.Split(new char[] { ',' })[19]);
				}
			}
			else
			{
				S.Append("?");
				if (!un.Split(new char[] { ',' })[2].Equals("-1"))
				{
					S.Append(un.Split(new char[] { ',' })[2]);
				}
				else
				{
					S.Append(PACOST);
				}
				S.Append("?");
				if ((!un.Split(new char[] { ',' })[3].Equals("0") ? false : !un.Split(new char[] { ',' })[2].Equals("0")))
				{
					S.Append(un.Split(new char[] { ',' })[2]);
				}
				else if ((Convert.ToInt32(un.Split(new char[] { ',' })[2]) <= Convert.ToInt32(un.Split(new char[] { ',' })[3]) ? true : un.Split(new char[] { ',' })[3].Equals("0")))
				{
					S.Append(string.Concat(un.Split(new char[] { ',' })[2], " à ", un.Split(new char[] { ',' })[3]));
				}
				else
				{
					S.Append(string.Concat(un.Split(new char[] { ',' })[3], " à ", un.Split(new char[] { ',' })[2]));
				}
				S.Append("?");
				S.Append(un.Split(new char[] { ',' })[4]);
				S.Append("?");
				S.Append(un.Split(new char[] { ',' })[6]);
				S.Append("?");
				S.Append(un.Split(new char[] { ',' })[7]);
				S.Append("?");
				S.Append(un.Split(new char[] { ',' })[8]);
				S.Append("?");
				S.Append(un.Split(new char[] { ',' })[9]);
				S.Append("?");
				S.Append(un.Split(new char[] { ',' })[10]);
				P_M = un.Split(new char[] { ',' })[11].Trim();
				if ((P_M.Equals("false") ? true : P_M.Equals("true")))
				{
					S.Append("?");
					S.Append(P_M);
					S.Append("?");
				}
				S.Append("?");
				if (un.Split(new char[] { ',' })[12].Trim() != "0")
				{
					S.Append(un.Split(new char[] { ',' })[12]);
				}
				else
				{
					S.Append("autant que possible");
				}
				S.Append("?");
				if (un.Split(new char[] { ',' })[13].Trim() != "0")
				{
					S.Append(string.Concat(un.Split(new char[] { ',' })[13], " tour(s)"));
				}
				else
				{
					S.Append("Tous les tours");
				}
				S.Append("?");
				ByT = un.Split(new char[] { ',' })[14].Trim();
				if (ByT != "0")
				{
					S.Append(ByT);
				}
				else
				{
					S.Append("Immédiat");
				}
				S.Append("?");
				if (!SpellsList.ReturnZone(un.Split(new char[] { ',' })[15]).Contains("invalide"))
				{
					S.Append(SpellsList.ReturnZone(un.Split(new char[] { ',' })[15]));
				}
				else
				{
					S.Append(SpellsList.ReturnZone(un.Split(new char[] { ',' })[16]));
				}
				S.Append("?");
				level = un.Split(new char[] { ',' })[17].Trim();
				if (!level.Contains(";"))
				{
					if (level != "0")
					{
						S.Append(level);
					}
					else
					{
						S.Append("all");
					}
					S.Append("?");
					S.Append(un.Split(new char[] { ',' })[18]);
				}
				else
				{
					level = un.Split(new char[] { ',' })[18].Trim();
					if (level != "0")
					{
						S.Append(level);
					}
					else
					{
						S.Append("all");
					}
					S.Append("?");
					S.Append(un.Split(new char[] { ',' })[19]);
				}
			}
			return S.ToString();
		}

		public static string CritParse(string un)
		{
			StringBuilder S = new StringBuilder();
			string jetp = "";
			string idspell = un.Split(new char[] { ';' })[0].Trim();
			try
			{
				jetp = un.Split(new char[] { ';' })[6].Trim();
			}
			catch
			{
				jetp = "0";
			}
			string VB = EffectsListing.Return_SpellsEffects(idspell);
			if (jetp == "0")
			{
				S.Append(VB);
			}
			else if (!VB.Contains("$"))
			{
				S.Append(string.Concat(SpellsList.ParseJetEffect(jetp), " ", VB));
			}
			else
			{
				S.Append(VB.Replace("$", SpellsList.ParseJetEffect(jetp)));
			}
			S.Append("§");
			int Mm = Convert.ToInt32(un.Split(new char[] { ';' })[0]);
			int id_m = Convert.ToInt32(un.Split(new char[] { ';' })[1]);
			if ((Mm.Equals(181) ? true : Mm.Equals(185)))
			{
				S.Append(MonsterList.ReturnMonstersInfos(id_m, 1));
			}
			else if ((Mm.Equals(401) ? false : !Mm.Equals(400)))
			{
				S.Append(SpellsList.Return_spells(Mm));
			}
			else
			{
				S.Append(EffectsListing.Return_SpellsEffects(Mm.ToString()));
			}
			S.Append("§");
			S.Append(un.Split(new char[] { ';' })[2]);
			S.Append("§");
			string etat = un.Split(new char[] { ';' })[3].Trim();
			if (!idspell.Equals("950"))
			{
				S.Append(etat);
			}
			else if (etat.Equals("-1"))
			{
				S.Append(string.Concat("null/", etat));
			}
			else
			{
				S.Append(SpellsList.SwitchEtat(etat));
			}
			S.Append("§");
			S.Append(un.Split(new char[] { ';' })[4]);
			return S.ToString();
		}

		public static void Load_Spells()
		{
			MySqlDataReader spelllec = DatabaseManager.SelectQuery(QueryBuilder.SelectFromQuery(new string[] { "*" }, TableSort, "", ""));
			while (spelllec.Read())
			{
				SpellsList spellrecord = new SpellsList(spelllec);
				AllSpells.Add(spellrecord.Id, spellrecord);
				SpellsName.Add(spellrecord.Nom);
			}
			CountSpells = AllSpells.Count();
			spelllec.Close();
			spelllec.Dispose();
		}

		public static string NormParse(string un)
		{
			StringBuilder S = new StringBuilder();
			string jetp = "";
			string idspell = un.Split(new char[] { ';' })[0].Trim();
			try
			{
				jetp = un.Split(new char[] { ';' })[6].Trim();
			}
			catch
			{
				jetp = "0";
			}
			string VB = EffectsListing.Return_SpellsEffects(idspell);
			if (jetp == "0")
			{
				S.Append(VB);
			}
			else if (!VB.Contains("$"))
			{
				S.Append(string.Concat(SpellsList.ParseJetEffect(jetp), " ", VB));
			}
			else
			{
				S.Append(VB.Replace("$", SpellsList.ParseJetEffect(jetp)));
			}
			S.Append("@");
			int Mm = Convert.ToInt32(un.Split(new char[] { ';' })[0]);
			int id_m = Convert.ToInt32(un.Split(new char[] { ';' })[1]);
			if ((Mm.Equals(181) ? true : Mm.Equals(185)))
			{
				S.Append(MonsterList.ReturnMonstersInfos(id_m, 1));
			}
			else if ((Mm.Equals(401) ? false : !Mm.Equals(400)))
			{
				S.Append(SpellsList.Return_spells(Mm));
			}
			else
			{
				S.Append(EffectsListing.Return_SpellsEffects(Mm.ToString()));
			}
			S.Append("@");
			S.Append(un.Split(new char[] { ';' })[2]);
			S.Append("@");
			string etat = un.Split(new char[] { ';' })[3].Trim();
			if (!idspell.Equals("950"))
			{
				S.Append(etat);
			}
			else if (etat.Equals("-1"))
			{
				S.Append(string.Concat("null/", etat));
			}
			else
			{
				S.Append(SpellsList.SwitchEtat(etat));
			}
			S.Append("@");
			S.Append(un.Split(new char[] { ';' })[4]);
			return S.ToString();
		}

		public static string ParseJetEffect(string jet)
		{
			string JET = "";
			int dam2 = 0;
			string B = jet.Split(new char[] { '+' })[0];
			string C = jet.Split(new char[] { '+' })[1];
			int o = Convert.ToInt32(B.Split(new char[] { 'd' })[0]);
			int t = Convert.ToInt32(B.Split(new char[] { 'd' })[1]);
			int dam = o * t;
			if (!C.Equals("0"))
			{
				dam2 = dam + Convert.ToInt32(C);
			}
			if (dam2 == 0)
			{
				JET = string.Format("{0}", dam);
			}
			else
			{
				JET = (dam == 0 ? string.Format("{0}", dam2) : string.Format("{0} à {1}", dam, dam2));
			}
			return JET;
		}

		public static void ParseLevel(int id)
		{
			KeyValuePair<int, SpellsList> keyValuePair = SpellsList.AllSpells.FirstOrDefault<KeyValuePair<int, SpellsList>>((KeyValuePair<int, SpellsList> x) => x.Key == id);
			string level1 = keyValuePair.Value.Lvl1;
			keyValuePair = SpellsList.AllSpells.FirstOrDefault<KeyValuePair<int, SpellsList>>((KeyValuePair<int, SpellsList> x) => x.Key == id);
			string level2 = keyValuePair.Value.Lvl2;
			keyValuePair = SpellsList.AllSpells.FirstOrDefault<KeyValuePair<int, SpellsList>>((KeyValuePair<int, SpellsList> x) => x.Key == id);
			string level3 = keyValuePair.Value.Lvl3;
			keyValuePair = SpellsList.AllSpells.FirstOrDefault<KeyValuePair<int, SpellsList>>((KeyValuePair<int, SpellsList> x) => x.Key == id);
			string level4 = keyValuePair.Value.Lvl4;
			keyValuePair = SpellsList.AllSpells.FirstOrDefault<KeyValuePair<int, SpellsList>>((KeyValuePair<int, SpellsList> x) => x.Key == id);
			string level5 = keyValuePair.Value.Lvl5;
			keyValuePair = SpellsList.AllSpells.FirstOrDefault<KeyValuePair<int, SpellsList>>((KeyValuePair<int, SpellsList> x) => x.Key == id);
			string level6 = keyValuePair.Value.Lvl6;
			string[] strArrays = level1.Split(new char[] { '|' });
			for (int i = 0; i < (int)strArrays.Length; i++)
			{
				string lvl1 = strArrays[i];
				SpellsList.EffectLvl1.Add(SpellsList.TradSpells(lvl1));
			}
			string[] strArrays1 = level2.Split(new char[] { '|' });
			for (int j = 0; j < (int)strArrays1.Length; j++)
			{
				string lvl2 = strArrays1[j];
				SpellsList.EffectLvl2.Add(SpellsList.TradSpells(lvl2));
			}
			string[] strArrays2 = level3.Split(new char[] { '|' });
			for (int k = 0; k < (int)strArrays2.Length; k++)
			{
				string lvl3 = strArrays2[k];
				SpellsList.EffectLvl3.Add(SpellsList.TradSpells(lvl3));
			}
			string[] strArrays3 = level4.Split(new char[] { '|' });
			for (int l = 0; l < (int)strArrays3.Length; l++)
			{
				string lvl4 = strArrays3[l];
				SpellsList.EffectLvl4.Add(SpellsList.TradSpells(lvl4));
			}
			string[] strArrays4 = level5.Split(new char[] { '|' });
			for (int m = 0; m < (int)strArrays4.Length; m++)
			{
				string lvl5 = strArrays4[m];
				SpellsList.EffectLvl5.Add(SpellsList.TradSpells(lvl5));
			}
			string[] strArrays5 = level6.Split(new char[] { '|' });
			for (int n = 0; n < (int)strArrays5.Length; n++)
			{
				string lvl6 = strArrays5[n];
				SpellsList.EffectLvl6.Add(SpellsList.TradSpells(lvl6));
			}
		}

		public static string Return_spells(int id)
		{
			string nom;
			try
            {
				
				nom = AllSpells.FirstOrDefault(x => x.Key == id).Value.Nom;

			}
            catch
            {
				nom = "sort inconnu";
			}
			return nom;
		}

		public static int ReturnSpellsIDByName(string name)
		{
			KeyValuePair<int, SpellsList> keyValuePair = SpellsList.AllSpells.FirstOrDefault<KeyValuePair<int, SpellsList>>((KeyValuePair<int, SpellsList> x) => x.Value.Nom == name);
			return keyValuePair.Key;
		}

		public static int ReturnSpellSprite(int key)
		{
			KeyValuePair<int, SpellsList> keyValuePair = SpellsList.AllSpells.FirstOrDefault<KeyValuePair<int, SpellsList>>((KeyValuePair<int, SpellsList> x) => x.Key == key);
			return keyValuePair.Value.Sprite;
		}

		public static string ReturnZone(string zone)
		{
			string str;
			if (zone.Contains("Pa"))
			{
				str = "Case";
			}
			else if (zone.Contains("Cc"))
			{
				str = "Zone de 2";
			}
			else if (zone.Contains("Cd"))
			{
				str = "Zone de 3";
			}
			else if (zone.Contains("Ce"))
			{
				str = "Zone de 4";
			}
			else if (zone.Contains("Cf"))
			{
				str = "Zone de 5";
			}
			else if (zone.Contains("Cj"))
			{
				str = "Zone de 10";
			}
			else if (zone.Contains("C_"))
			{
				str = "Zone infinie";
			}
			else if (zone.Contains("Xb"))
			{
				str = "Croix de 1";
			}
			else if (zone.Contains("Xc"))
			{
				str = "Croix de 2";
			}
			else if (!zone.Contains("Lb"))
			{
				str = (!zone.Contains("Cb") ? string.Concat(zone, " invalide") : "Cercle de 3");
			}
			else
			{
				str = "Ligne de 2";
			}
			return str;
		}

		public static string SwitchEtat(string id)
		{
			string str;
			string str1 = id;
			switch (str1)
			{
				case null:
				{
					str = null;
					return str;
				}
				case "0":
				{
					str = "neutre";
					break;
				}
				case "1":
				{
					str = "saoul";
					break;
				}
				case "2":
				{
					str = "capture d'ame";
					break;
				}
				case "3":
				{
					str = "porteur";
					break;
				}
				case "4":
				{
					str = "peureux";
					break;
				}
				case "5":
				{
					str = "desorienté";
					break;
				}
				case "6":
				{
					str = "enraciné";
					break;
				}
				case "7":
				{
					str = "pesenteur";
					break;
				}
				case "8":
				{
					str = "Porté";
					break;
				}
				case "9":
				{
					str = "motivation sylvestre";
					break;
				}
				case "10":
				{
					str = "apprivoisement";
					break;
				}
				default:
				{
					if (str1 != "11")
					{
						str = null;
						return str;
					}
					str = "chevauchant";
					break;
				}
			}
			return str;
		}

		public static string TradSpells(string data)
		{
			StringBuilder Spells = new StringBuilder();
			if (data.Contains(","))
			{
				if (!data.Split(new char[] { ',' })[1].Contains("+"))
				{
					if ((SpellsList.CRIT == 0 ? true : Convert.ToInt32(data.Split(new char[] { '+' })[1].Split(new char[] { ',' })[0]) < SpellsList.CRIT))
					{
						Spells.Append(SpellsList.NormParse(data.Split(new char[] { ',' })[0]));
					}
					else
					{
						Spells.Append(SpellsList.CritParse(data.Split(new char[] { ',' })[0]));
					}
					if (data.Split(new char[] { ',' }).Count<string>() != 20)
					{
						Spells.Append(SpellsList.CaracParse(data, false));
					}
					else
					{
						Spells.Append(SpellsList.CaracParse(data, true));
					}
				}
				else
				{
					string n = data.Split(new char[] { ',' })[0];
					string c = data.Split(new char[] { ',' })[1];
					NORM = Convert.ToInt32(n.Split(new char[] { '+' })[1]);
					CRIT = Convert.ToInt32(c.Split(new char[] { '+' })[1]);
					Spells.Append(string.Concat(SpellsList.NormParse(n), "~", SpellsList.CritParse(c)));
					if ((data.Contains("true") ? true : data.Contains("false")))
					{
						if (data.Split(new char[] { ',' }).Count<string>() != 20)
						{
							Spells.Append(SpellsList.CaracParse(data, false));
						}
						else
						{
							Spells.Append(SpellsList.CaracParse(data, true));
						}
					}
				}
			}
			else if ((SpellsList.CRIT == 0 ? true : Convert.ToInt32(data.Split(new char[] { '+' })[1]) < SpellsList.CRIT))
			{
				Spells.Append(SpellsList.NormParse(data));
			}
			else
			{
				Spells.Append(SpellsList.CritParse(data));
			}
			return Spells.ToString();
		}
	}
}