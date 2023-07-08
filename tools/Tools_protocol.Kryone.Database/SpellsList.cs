using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
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
		public static Dictionary<int, SpellsList> AllSpells = new Dictionary<int, SpellsList>();
		public static List<string> SpellsName = new List<string>();
		public static List<string> EffectLvl1 = new List<string>();
		public static List<string> EffectLvl2 = new List<string>();
		public static List<string> EffectLvl3 = new List<string>();
		public static List<string> EffectLvl4 = new List<string>();
		public static List<string> EffectLvl5 = new List<string>();
		public static List<string> EffectLvl6 = new List<string>();
		public static List<string> SpellsShow = new List<string>();
		public static int CountSpells;
		public static Dictionary<string, SpellsBrain> ParsedSPells = new Dictionary<string, SpellsBrain>();
		public static bool IsDecal = false;
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

		public SpellsList(IDataReader reader)
		{
			Id = (int)reader["id"];
			Nom = (string)reader["nom"];
			Sprite = (int)reader["Sprite"];
			SpriteInfos = (string)reader["spriteInfos"];
			Lvl1 = (string)reader["lvl1"];
			Lvl2 = (string)reader["lvl2"];
			Lvl3 = (string)reader["lvl3"];
			Lvl4 = (string)reader["lvl4"];
			Lvl5 = (string)reader["lvl5"];
			Lvl6 = (string)reader["lvl6"];
			EffectTarget = (string)reader["effectTarget"];
			Type = (int)reader["type"];
			Durée = (int)reader["durer"];
		}

		public static void AddSpellsToList(string data)
		{
			string query = QueryBuilder.SelectFromQuery(new string[] { "*" }, SpellsList.TableSort, "id", data.Split(new char[] { ';' })[0]);

			using (MySqlConnection connection = new MySqlConnection(DatabaseManager.ConnectionString))
			{
				try
				{
					connection.Open();
					MySqlDataReader R = new MySqlCommand(query, connection).ExecuteReader();
					string z = data.Split(new char[] { ';' })[1];
					while (R.Read())
					{
						SpellsShow.Add(string.Concat(R["nom"].ToString(), " - niveau: ", z));
					}
					R.Close();
					R.Dispose();
					connection.Close();
					connection.Dispose();
				}
				catch (MySqlException) { }
			}
		}

		public static string CaracParse(string un, bool decal, bool forbot = false)
		{
			string level;
			string P_M;
			string ByT;
			int PACOST = 6;
			IsDecal= decal;
			StringBuilder S = new StringBuilder();
			if (!decal)
			{
				S.Append("?");
				if (!un.Split(',')[2].Equals("-1"))
				{
					S.Append(un.Split(',')[2]);
				}
				else
				{
					S.Append(PACOST);
				}
				S.Append("?");
				if ((!un.Split(',')[3].Equals("0") ? false : !un.Split(',')[2].Equals("0")))
				{
					S.Append(un.Split(new char[] { ',' })[2]);
				}
				else if ((Convert.ToInt32(un.Split(',' )[2]) <= Convert.ToInt32(un.Split(',')[3]) ? true : un.Split(',')[3].Equals("0")))
				{
					S.Append(string.Concat(un.Split(',')[2], " à ", un.Split(',')[3]));
				}
				else
				{
					S.Append(string.Concat(un.Split(',')[3], " à ", un.Split(',')[2]));
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
				if (forbot)
					S.Append(un.Split(',')[11]);
				else
				{
                    if (un.Split(',')[11].Trim() != "0")
                    {
                        S.Append(un.Split(',')[11]);
                    }
                    else
                    {
                        S.Append("autant que possible");
                    }
                }
				S.Append("?");
				if(forbot)
					S.Append(un.Split(',')[12]);
				else
				{
                    if (un.Split(',')[12].Trim() != "0")
                    {
                        S.Append(string.Concat(un.Split(',')[12], " tour(s)"));
                    }
                    else
                    {
                        S.Append("Tous les tours");
                    }
                }
				S.Append("?");
				ByT = un.Split(',')[13].Trim();
				if (ByT != "0")
				{
					S.Append(ByT);
				}
				else
				{
					S.Append("Immédiat");
				}
				S.Append("?");
				if (!SpellsList.ReturnZone(un.Split(',')[14]).Contains("invalide"))
				{
					if(forbot)
						S.Append(un.Split(',')[14]);
					else
                        S.Append(SpellsList.ReturnZone(un.Split(',')[14]));
                }
				else
				{
					if(forbot)
						S.Append(un.Split(',')[15]);
					else
                        S.Append(SpellsList.ReturnZone(un.Split(',')[15]));
                }
				S.Append("?");
				level = un.Split(',')[17].Trim();
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
					S.Append(un.Split(',')[18]);
				}
				else
				{
					level = un.Split(',')[18].Trim();
					if (level != "0")
					{
						S.Append(level);
					}
					else
					{
						S.Append("all");
					}
					S.Append("?");
					S.Append(un.Split(',')[19]);
				}
			}
			else
			{
				S.Append("?");
				if (!un.Split(',')[2].Equals("-1"))
				{
					S.Append(un.Split(',')[2]);
				}
				else
				{
					S.Append(PACOST);
				}
				S.Append("?");
				if ((!un.Split(',')[3].Equals("0") ? false : !un.Split(',')[2].Equals("0")))
				{
					S.Append(un.Split(',')[2]);
				}
				else if ((Convert.ToInt32(un.Split(',')[2]) <= Convert.ToInt32(un.Split(',')[3]) ? true : un.Split(',')[3].Equals("0")))
				{
					S.Append(string.Concat(un.Split(',')[2], " à ", un.Split(',')[3]));
				}
				else
				{
					S.Append(string.Concat(un.Split(',')[3], " à ", un.Split(',')[2]));
				}
				S.Append("?");
				S.Append(un.Split(',')[4]);
				S.Append("?");
				S.Append(un.Split(',')[6]);
				S.Append("?");
				S.Append(un.Split(',')[7]);
				S.Append("?");
				S.Append(un.Split(',')[8]);
				S.Append("?");
				S.Append(un.Split(',')[9]);
				S.Append("?");
				S.Append(un.Split(',')[10]);
				P_M = un.Split(',')[11].Trim();
				if ((P_M.Equals("false") ? true : P_M.Equals("true")))
				{
					S.Append("?");
					S.Append(P_M);
					S.Append("?");
				}
				S.Append("?");
				if(forbot)
					S.Append(un.Split(',')[12]);
				else
				{
                    if (un.Split(',')[12].Trim() != "0")
                    {
                        S.Append(un.Split(',')[12]);
                    }
                    else
                    {
                        S.Append("autant que possible");
                    }
                }
				S.Append("?");
				if(forbot)
					S.Append(un.Split(',')[13]);
				else
				{
                    if (un.Split(',')[13].Trim() != "0")
                    {
                        S.Append(string.Concat(un.Split(',')[13], " tour(s)"));
                    }
                    else
                    {
                        S.Append("Tous les tours");
                    }
                }
				S.Append("?");
				ByT = un.Split(',')[14].Trim();
				if (ByT != "0")
				{
					S.Append(ByT);
				}
				else
				{
					S.Append("Immédiat");
				}
				S.Append("?");
				if (!SpellsList.ReturnZone(un.Split(',')[15]).Contains("invalide"))
				{
					if(forbot)
						S.Append(un.Split(',')[15]);
					else
                        S.Append(SpellsList.ReturnZone(un.Split(',')[15]));
                }
				else
				{
					if(forbot)
						S.Append(un.Split(',')[16]);
					else
                        S.Append(SpellsList.ReturnZone(un.Split(',')[16]));
                }
				S.Append("?");
				level = un.Split(',')[17].Trim();
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
					S.Append(un.Split(',')[18]);
				}
				else
				{
					level = un.Split(',')[18].Trim();
					if (level != "0")
					{
						S.Append(level);
					}
					else
					{
						S.Append("all");
					}
					S.Append("?");
					S.Append(un.Split(',')[19]);
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
			else if(!Mm.Equals(400) || !Mm.Equals(401) || !Mm.Equals(-100))
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
			string query = QueryBuilder.SelectFromQuery(new string[] { "*" }, TableSort, "", "");

			using (MySqlConnection connection = new MySqlConnection(DatabaseManager.ConnectionString))
			{
				try
				{
					connection.Open();
					MySqlDataReader spelllec = new MySqlCommand(query, connection).ExecuteReader();
					while (spelllec.Read())
					{
						SpellsList spellrecord = new SpellsList(spelllec);
						AllSpells.Add(spellrecord.Id, spellrecord);
						SpellsName.Add(spellrecord.Nom);
					}
					CountSpells = AllSpells.Count();
					spelllec.Close();
					spelllec.Dispose();
					connection.Close();
					connection.Dispose();
				}
				catch (MySqlException) { }
			}
			
			
		}

		public static string NormParse(string un)
		{
			StringBuilder S = new StringBuilder();
			string jetp = "";
			string idspell = un.Split(';')[0].Trim();
			try
			{
				jetp = un.Split(';')[6].Trim();
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
			int Mm = Convert.ToInt32(un.Split(';')[0]);
			int id_m = Convert.ToInt32(un.Split(';')[1]);
			if ((Mm.Equals(181) ? true : Mm.Equals(185)))
			{
				S.Append(MonsterList.ReturnMonstersInfos(id_m, 1));
			}
			else if (!Mm.Equals(400) || !Mm.Equals(401) || !Mm.Equals(-100))
			{
				S.Append(SpellsList.Return_spells(Mm));
			}
			else
			{
				S.Append(EffectsListing.Return_SpellsEffects(Mm.ToString()));
			}
			S.Append("@");
			S.Append(un.Split(';')[2]);
			S.Append("@");
			string etat = un.Split(';')[3].Trim();
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
			S.Append(un.Split(';')[4]);
			return S.ToString();
		}

		public static string ParseJetEffect(string jet)
		{
			string JET = "";
			int dam2 = 0;
			string B = jet.Split('+')[0];
			string C = jet.Split('+')[1];
			int o = Convert.ToInt32(B.Split('d')[0]);
			int t = Convert.ToInt32(B.Split('d')[1]);
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

		public static void ParseLevel(int id, bool forbot = false)
		{
            

            try
			{
                string level1 = SpellsList.AllSpells.FirstOrDefault(x => x.Key == id).Value.Lvl1;
                string level2 = SpellsList.AllSpells.FirstOrDefault(x => x.Key == id).Value.Lvl2;
                string level3 = SpellsList.AllSpells.FirstOrDefault(x => x.Key == id).Value.Lvl3;
                string level4 = SpellsList.AllSpells.FirstOrDefault(x => x.Key == id).Value.Lvl4;
                string level5 = SpellsList.AllSpells.FirstOrDefault(x => x.Key == id).Value.Lvl5;
                string level6 = SpellsList.AllSpells.FirstOrDefault(x => x.Key == id).Value.Lvl6;

                string[] strArrays = level1.Split('|');
                for (int i = 0; i < (int)strArrays.Length; i++)
                {
                    string lvl1 = strArrays[i];
                    SpellsList.EffectLvl1.Add(SpellsList.TradSpells(lvl1, forbot));
                }

                string[] strArrays1 = level2.Split('|');
                for (int j = 0; j < (int)strArrays1.Length; j++)
                {
                    string lvl2 = strArrays1[j];
                    SpellsList.EffectLvl2.Add(SpellsList.TradSpells(lvl2, forbot));
                }

                string[] strArrays2 = level3.Split('|');
                for (int k = 0; k < (int)strArrays2.Length; k++)
                {
                    string lvl3 = strArrays2[k];
                    SpellsList.EffectLvl3.Add(SpellsList.TradSpells(lvl3, forbot));
                }

                string[] strArrays3 = level4.Split('|');
                for (int l = 0; l < (int)strArrays3.Length; l++)
                {
                    string lvl4 = strArrays3[l];
                    SpellsList.EffectLvl4.Add(SpellsList.TradSpells(lvl4,forbot));
                }

                string[] strArrays4 = level5.Split('|');
                for (int m = 0; m < (int)strArrays4.Length; m++)
                {
                    string lvl5 = strArrays4[m];
                    SpellsList.EffectLvl5.Add(SpellsList.TradSpells(lvl5,forbot));
                }

                string[] strArrays5 = level6.Split('|');
                for (int n = 0; n < (int)strArrays5.Length; n++)
                {
                    string lvl6 = strArrays5[n];
                    SpellsList.EffectLvl6.Add(SpellsList.TradSpells(lvl6,forbot));
                }

			}
			catch
			{
				
			}
		}

		public static string Return_spells(int id)
		{
			string nom = "sort inconnu";
			try
            {
				
				nom = AllSpells.FirstOrDefault(x => x.Key == id).Value?.Nom;

			}
            catch
            {
				return nom;
			}
			return nom;
		}

		public static int ReturnSpellsIDByName(string name)
		{
			return SpellsList.AllSpells.FirstOrDefault(x => x.Value.Nom == name).Key;
			
		}

		public static int ReturnSpellSprite(int key)
		{
			return SpellsList.AllSpells.FirstOrDefault(x => x.Key == key).Value.Sprite;
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

		public static string TradSpells(string data, bool forbot = false)
		{
			StringBuilder Spells = new StringBuilder();
			if (data.Contains(","))
			{
				if (!data.Split(',')[1].Contains("+"))
				{
					if ((SpellsList.CRIT == 0 ? true : Convert.ToInt32(data.Split('+')[1].Split(',')[0]) < SpellsList.CRIT))
					{
						if (forbot)
							Spells.Append($"{data.Split(',')[0].Split(';')[0]}|{data.Split(',')[0].Split(';')[4]}|{data.Split(',')[0].Split(';')[1]}|false");
						else
                            Spells.Append(SpellsList.NormParse(data.Split(',')[0]));
                    }
					else
					{
						if (forbot)
							Spells.Append($"{data.Split(',')[0].Split(';')[0]}|{data.Split(',')[0].Split(';')[4]}|{data.Split(',')[0].Split(';')[1]}|true");
						else
                            Spells.Append(SpellsList.CritParse(data.Split(',')[0]));
                    }
					if (data.Split(',').Count() != 20)
					{
						Spells.Append(SpellsList.CaracParse(data, false, forbot));
					}
					else
					{
						Spells.Append(SpellsList.CaracParse(data, true, forbot));
					}
				}
				else
				{
					string n = data.Split(',')[0];
					string c = data.Split(',')[1];
					NORM = Convert.ToInt32(n.Split('+')[1]);
					CRIT = Convert.ToInt32(c.Split('+')[1]);
					if (forbot)
						Spells.Append($"{data.Split(',')[0].Split(';')[0]}|{data.Split(',')[0].Split(';')[4]}|{data.Split(',')[0].Split(';')[1]}|true~{data.Split(',')[0].Split(';')[0]}|{data.Split(',')[0].Split(';')[4]}|{data.Split(',')[0].Split(';')[1]}|true");
					else
						Spells.Append(string.Concat(SpellsList.NormParse(n), "~", SpellsList.CritParse(c)));
                    if ((data.Contains("true") ? true : data.Contains("false")))
					{
						if (data.Split(',').Count<string>() != 20)
						{
							Spells.Append(SpellsList.CaracParse(data, false, forbot));
						}
						else
						{
							Spells.Append(SpellsList.CaracParse(data, true, forbot));
						}
					}
				}
			}
			else if ((SpellsList.CRIT == 0 ? true : Convert.ToInt32(data.Split('+')[1]) < SpellsList.CRIT))
			{
				if (forbot)
					Spells.Append($"{data.Split(',')[0].Split(';')[0]}|{data.Split(',')[0].Split(';')[4]}|{data.Split(',')[0].Split(';')[1]}|false");
				else
                    Spells.Append(SpellsList.NormParse(data));
            }
			else
			{
				if (forbot)
                    Spells.Append($"{data.Split(',')[0].Split(';')[0]}|{data.Split(',')[0].Split(';')[4]}|{data.Split(',')[0].Split(';')[1]}|true");
                else
                    Spells.Append(SpellsList.CritParse(data));
			}
			return Spells.ToString();
		}
	}
}