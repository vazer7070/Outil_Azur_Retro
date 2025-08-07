using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace Tools_protocol.Data
{
	public class ConditionsListing
	{
		public static Dictionary<string, string> ConditionsDico;

		static ConditionsListing()
		{
			ConditionsListing.ConditionsDico = new Dictionary<string, string>();
		}

		public ConditionsListing()
		{
		}
        private static List<string> Conditions = new List<string>
{
    "BI=Inutilisable",
    "QE=Quete en cours",
    "QT=Quete terminee",
    "CI=Ajoute de l'intelligence",
    "CA=Ajoute de l'agilite",
    "CW=Ajoute de la sagesse",
    "CC=Ajoute de la chance",
    "CS=Ajoute de la force",
    "CV=Ajoute de la vitalite",
    "CM=Ajoute un PM",
    "Ci=Recupere l'intelligence",
    "Cs=Recupere la force",
    "Cv=Recupere la vitalite",
    "Ca=Recupere l'agilite",
    "Cw=Recupere la sagesse",
    "Cc=Recupere la chance",
    "Ps=Alignement du personnage",
    "Pa=Promotion de l'alignement du personnage",
    "PP=Niveau de l'alignement du personnage",
    "PL=Niveau du personnage",
    "PK=Kamas dans l'inventaire du personnage",
    "PO=Relatif a un objet en inventaire",
    "PG=Classe du personnage",
    "PS=Sexe du personnage",
    "PZ=Abonnement",
    "PN=Nom du personnage",
    "PJ=Relatif au metier",
    "MK=est du metier",
    "Pg=Relatif au don",
    "PR=Relatif au mariage",
    "PX=Verification si administrateur",
    "PW=Relatif au poids",
    "PB=ID Zone du personnage",
    "SI=ID Map du personnage",
    "MiS=ID du personnage"
};

        public static void ConditionsLoad(string path)
		{
			if (!File.Exists(path))
				File.WriteAllLines(path, Conditions);
            string[] strArrays = File.ReadAllLines(path);
            for (int i = 0; i < (int)strArrays.Length; i++)
            {
                string line = strArrays[i];
                ConditionsListing.ConditionsDico.Add(line.Split(new char[] { '=' })[0], line.Split(new char[] { '=' })[1]);
            }
        }

		public static string ReturnConditionIdByName(string name)
		{
			KeyValuePair<string, string> keyValuePair = ConditionsListing.ConditionsDico.FirstOrDefault<KeyValuePair<string, string>>((KeyValuePair<string, string> x) => x.Value == name);
			return keyValuePair.Key;
		}

		public static string TradConditions(string condi)
		{
			
			string h;
			string j;
			
			StringBuilder F = new StringBuilder();
			if (condi.Contains("&"))
			{
				string[] strArrays = condi.Split(new char[] { '&' });
				for (int i = 0; i < (int)strArrays.Length; i++)
				{
					string C1 = strArrays[i];
					if (C1.Contains(">"))
					{
						if (!ConditionsListing.ConditionsDico.TryGetValue(C1.Split(new char[] { '>' })[0], out h))
						{
							F.Append(string.Concat(C1.Split(new char[] { '>' })[0], " non connu"));
						}
						else
						{
							F.Append(string.Concat(h, " > ", C1.Split(new char[] { '>' })[1], " "));
						}
					}
					else if (C1.Contains("<"))
					{
						if (!ConditionsListing.ConditionsDico.TryGetValue(C1.Split(new char[] { '<' })[0], out h))
						{
							F.Append(string.Concat(C1.Split(new char[] { '<' })[0], " non connu"));
						}
						else
						{
							F.Append(string.Concat(h, " < ", C1.Split(new char[] { '<' })[1], " "));
						}
					}
					else if (C1.Contains("="))
					{
						if (!ConditionsListing.ConditionsDico.TryGetValue(C1.Split(new char[] { '=' })[0], out h))
						{
							F.Append(string.Concat(C1.Split(new char[] { '=' })[0], " non connu"));
						}
						else
						{
							F.Append(string.Concat(h, " = ", C1.Split(new char[] { '=' })[1], " "));
						}
					}
					else if (C1.Contains("!"))
					{
						if (!ConditionsListing.ConditionsDico.TryGetValue(C1.Split(new char[] { '!' })[0], out h))
						{
							F.Append(string.Concat(C1.Split(new char[] { '!' })[0], " non connu"));
						}
						else
						{
							F.Append(string.Concat(h, " n'est pas ", C1.Split(new char[] { '!' })[1], " "));
						}
					}
				}
			}
			else if (condi.Contains("|"))
			{
				string[] strArrays1 = condi.Split(new char[] { '|' });
				for (int num = 0; num < (int)strArrays1.Length; num++)
				{
					string C2 = strArrays1[num];
					if (C2.Contains(">"))
					{
						if (!ConditionsListing.ConditionsDico.TryGetValue(C2.Split(new char[] { '>' })[0], out h))
						{
							F.Append(string.Concat(C2.Split(new char[] { '>' })[0], " non connu"));
						}
						else
						{
							F.Append(string.Concat(h, " > ", C2.Split(new char[] { '>' })[1], " "));
						}
					}
					else if (C2.Contains("<"))
					{
						if (!ConditionsListing.ConditionsDico.TryGetValue(C2.Split(new char[] { '<' })[0], out h))
						{
							F.Append(string.Concat(C2.Split(new char[] { '<' })[0], " non connu"));
						}
						else
						{
							F.Append(string.Concat(h, " < ", C2.Split(new char[] { '<' })[1], " "));
						}
					}
					else if (C2.Contains("="))
					{
						if (!ConditionsListing.ConditionsDico.TryGetValue(C2.Split(new char[] { '=' })[0], out h))
						{
							F.Append(string.Concat(C2.Split(new char[] { '=' })[0], " non connu"));
						}
						else
						{
							F.Append(string.Concat(h, " = ", C2.Split(new char[] { '=' })[1], " "));
						}
					}
					else if (C2.Contains("!"))
					{
						if (!ConditionsListing.ConditionsDico.TryGetValue(C2.Split(new char[] { '!' })[0], out h))
						{
							F.Append(string.Concat(C2.Split(new char[] { '!' })[0], " non connu"));
						}
						else
						{
							F.Append(string.Concat(h, " n'est pas ", C2.Split(new char[] { '!' })[1], " "));
						}
					}
				}
			}
			else if (condi.Contains(";"))
			{
				if (condi.Contains("="))
				{
					if (!ConditionsListing.ConditionsDico.TryGetValue(condi.Split(new char[] { '=' })[0], out j))
					{
						F.Append(string.Concat(condi.Split(new char[] { '=' })[0], " non connu"));
					}
					else
					{
						string[] strArrays2 = new string[] { j, " = ", null, null, null };
						strArrays2[2] = condi.Split(new char[] { '=' })[1].Split(new char[] { ';' })[0];
						strArrays2[3] = " et ";
						strArrays2[4] = condi.Split(new char[] { '=' })[1].Split(new char[] { ';' })[1];
						F.Append(string.Concat(strArrays2));
					}
				}
			}
			else if (condi.Contains("="))
			{
				if (!ConditionsListing.ConditionsDico.TryGetValue(condi.Split(new char[] { '=' })[0], out j))
				{
					F.Append(string.Concat(condi.Split(new char[] { '=' })[0], " non connu"));
				}
				else
				{
					F.Append(string.Concat(j, " = ", condi.Split(new char[] { '=' })[1]));
				}
			}
			else if (condi.Contains("<"))
			{
				if (!ConditionsListing.ConditionsDico.TryGetValue(condi.Split(new char[] { '<' })[0], out j))
				{
					F.Append(string.Concat(condi.Split(new char[] { '<' })[0], " non connu"));
				}
				else
				{
					F.Append(string.Concat(j, " < ", condi.Split(new char[] { '<' })[1]));
				}
			}
			else if (condi.Contains(">"))
			{
				if (!ConditionsListing.ConditionsDico.TryGetValue(condi.Split(new char[] { '>' })[0], out j))
				{
					F.Append(string.Concat(condi.Split(new char[] { '>' })[0], " non connu"));
				}
				else
				{
					F.Append(string.Concat(j, " > ", condi.Split(new char[] { '>' })[1]));
				}
			}
			else if (condi.Contains("!"))
			{
				if (!ConditionsListing.ConditionsDico.TryGetValue(condi.Split(new char[] { '!' })[0], out j))
				{
					F.Append(string.Concat(condi.Split(new char[] { '!' })[0], " non connu"));
				}
				else
				{
					F.Append(string.Concat(j, " n'est pas ", condi.Split(new char[] { '!' })[1]));
				}
			}
			else if (condi.Contains("~"))
			{
				if (!ConditionsListing.ConditionsDico.TryGetValue(condi.Split(new char[] { '~' })[0], out j))
				{
					F.Append(string.Concat(condi.Split(new char[] { '~' })[0], " non connu"));
				}
				else
				{
					F.Append(string.Concat(j, " doit être ", condi.Split(new char[] { '~' })[1]));
				}
			}
			return F.ToString();
		}
	}
}