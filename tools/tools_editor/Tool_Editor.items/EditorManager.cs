
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Windows.Forms;
using Tools_protocol.Managers;

namespace Tool_Editor.items
{
	public class EditorManager
	{
		public static List<string> TradStat;

		static EditorManager()
		{
			TradStat = new List<string>();
		}

		public EditorManager()
		{
		}

		public static string ConditionsParse(string P, int Count)
		{
			string H;
			H = (Count >= 2 ? string.Concat(P.Split(new char[] { '%' })[0], P.Split(new char[] { '%' })[1]) : P.Replace("%", ""));
			return H;
		}

		private static string CreateJet(int min, int max)
		{
			string str = string.Format("1d{0}+{1}", min, max - min);
			return str;
		}

		public static void CreateSQLEditor(string editor, Dictionary<string, string> r)
		{
			string file = string.Concat(".\\creations\\", editor, ".sql");
			if (File.Exists(file))
			{
				file = string.Format(".\\creations\\{0}.{1}.sql", editor, r.Count);
			}
			StreamWriter SW = new StreamWriter(file);
			foreach (string h in r.Values)
			{
				SW.WriteLine(h);
			}
			SW.Close();
			SW.Dispose();
		}

		public static void CreateStatItems(string stat)
		{
			string id = stat.Split(new char[] { '%' })[0];
			string param = stat.Split(new char[] { '%' })[1];
			StringBuilder SB = new StringBuilder();
			if (!param.Equals("@"))
			{
				int min = Convert.ToInt32(param.Split(new char[] { '|' })[0]);
				int max = Convert.ToInt32(param.Split(new char[] { '|' })[1]);
				string hxmin = min.ToString("X");
				string hxmax = max.ToString("X");
				SB.Append(id ?? "");
				SB.Append("#");
				SB.Append(hxmin);
				SB.Append("#");
				SB.Append(hxmax);
				SB.Append("#0#");
				SB.Append(CreateJet(min, max));
			}
			else
			{
				SB.Append(string.Concat(id, "#1#0#0#0d0+1"));
			}
			TradStat.Add(SB.ToString());
		}
		public static void InjectSql(Dictionary<string, string> query)
        {
			
			foreach(string u in query.Keys)
            {
			     if(query.TryGetValue(u, out string j))
                {
					EmuManager.ExecuteQueryByEmu(u, j);

                }
                else
                {
					MessageBox.Show($"Aucune valeur trouvée pour la clé: {u}", "Clé introuvable", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
					return;
                }
				
            }
        }
		
		public static void CreateSwf(string path, string data)
		{
            try
            {
				if (File.Exists(path))
					File.Delete(path);
				File.WriteAllText(path, data);
				MessageBox.Show("Le fichier texte contenant la ligne SWF a été correctement crée", "Succès de la création", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
			catch(Exception e)
            {
				MessageBox.Show(e.Message);
				return;
            }
			
		}
		//[5 (bonus CC),3(PA),1(PO mini),1(PO max),40(Taux CC),40(Taux EC),false,false]
		//c:"CI>30&CS>4"
		public static string CreateSwfLine(int id, string name, string description, string GFX, string type, string level, bool FM, string pods, string AI, string condition, string prix, bool usable, bool twohand)
		{
			StringBuilder SB = new StringBuilder();
			SB.Append(string.Concat(string.Format("I.u[{0}] = ", id), "{"));
			SB.Append(string.Concat("p:'", prix, "',"));
			SB.Append(string.Concat("t:", type, ","));
			if (string.IsNullOrEmpty(description))
			{
				SB.Append("d:'#1',");
			}
			else
			{
				SB.Append(string.Concat("d:'", description, "',"));
			}
			SB.Append("ep:7,");
			SB.Append(string.Concat("g:", GFX, ","));
			SB.Append(string.Concat("l:", level, ","));
			SB.Append("wd:,");
			SB.Append(string.Concat("fm:", FM.ToString(), ","));
			SB.Append(string.Concat("w:", pods, ","));
			SB.Append("an:,");
			if (type == "2")
			{
				SB.Append(string.Concat("tw:", twohand.ToString(), ","));
			}
			SB.Append(string.Concat(new string[] { "e:[", AI.Split(new char[] { ';' })[5], ",", AI.Split(new char[] { ';' })[0], ",", AI.Split(new char[] { ';' })[1], ",", AI.Split(new char[] { ';' })[2], ",", AI.Split(new char[] { ';' })[3], ",", AI.Split(new char[] { ';' })[4], ",false,false]," }));
			SB.Append(string.Concat("c:'", condition, "',"));
			if (type == "89")
			{
				SB.Append(string.Concat("u:", usable.ToString(), ","));
			}
			SB.Append(string.Concat("n:", name, "};"));
			char c = '\"';
			string str = SB.ToString().Replace("'", string.Format("{0}", c));
			return str;
		}

		public static int SwitchType(string typename)
		{
			int IdType = 0;
			string str = typename;
			if (str != null)
			{
				switch (str)
				{
					case "amulette":
					{
						IdType = 1;
						break;
					}
					case "arc":
					{
						IdType = 2;
						break;
					}
					case "baguette":
					{
						IdType = 3;
						break;
					}
					case "baton":
					{
						IdType = 4;
						break;
					}
					case "dague":
					{
						IdType = 5;
						break;
					}
					case "épée":
					{
						IdType = 6;
						break;
					}
					case "marteau":
					{
						IdType = 7;
						break;
					}
					case "pelle":
					{
						IdType = 8;
						break;
					}
					case "anneau":
					{
						IdType = 9;
						break;
					}
					case "ceinture":
					{
						IdType = 10;
						break;
					}
					case "botte":
					{
						IdType = 11;
						break;
					}
					case "potion":
					{
						IdType = 12;
						break;
					}
					case "chapeau":
					{
						IdType = 16;
						break;
					}
					case "cape":
					{
						IdType = 17;
						break;
					}
					case "familier":
					{
						IdType = 18;
						break;
					}
					case "hache":
					{
						IdType = 19;
						break;
					}
					case "dofus":
					{
						IdType = 23;
						break;
					}
					case "divers":
					{
						IdType = 24;
						break;
					}
					case "bonbon":
					{
						IdType = 28;
						break;
					}
					case "pain":
					{
						IdType = 33;
						break;
					}
					case "ingrédient":
					{
						IdType = 34;
						break;
					}
					case "potion (oubli)":
					{
						IdType = 43;
						break;
					}
					case "peluche":
					{
						IdType = 61;
						break;
					}
					case "parchemin (sort)":
					{
						IdType = 75;
						break;
					}
					case "parchemin (carac)":
					{
						IdType = 76;
						break;
					}
					case "rune":
					{
						IdType = 78;
						break;
					}
					case "bouclier":
					{
						IdType = 82;
						break;
					}
					case "pierre d'âme":
					{
						IdType = 83;
						break;
					}
					case "clef":
					{
						IdType = 84;
						break;
					}
					case "gemme spirituelle":
					{
						IdType = 85;
						break;
					}
					case "rune (métier)":
					{
						IdType = 88;
						break;
					}
					case "cadeau":
					{
						IdType = 89;
						break;
					}
					case "dragodinde":
					{
						IdType = 97;
						break;
					}
					case "objivivant":
					{
						IdType = 113;
						break;
					}
					case "potion (amélioration)":
					{
						IdType = 116;
						break;
					}
				}
			}
			return IdType;
		}
	}
}