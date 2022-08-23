using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using Tools_protocol.Json;
using Tools_protocol.Query;

namespace Tools_protocol.Kryone.Database
{
	public class CharacterList
	{
		

		public static Dictionary<string, CharacterList> PersoAll = new Dictionary<string, CharacterList>();

		public static List<string> ItemsPerso = new List<string>();

		public static Dictionary<int, string> IdAccount = new Dictionary<int, string>();

		public static Dictionary<int, string> IdCompte = new Dictionary<int, string>();

		public static List<string> preinventory = new List<string>();

		public int Account { get; set; }

		public int Agilite { get; set; }

		public int Alignement { get; set; }

		public int Alvl { get; set; }

		public int Capital { get; set; }

		public int Cell { get; set; }

		public int Chance { get; set; }

		public short Class { get; set; }

		public int Color1 { get; set; }

		public int Color2 { get; set; }

		public int Color3 { get; set; }

		public int Deshonor { get; set; }

		public int Energy { get; set; }

		public int Force { get; set; }

		public int Gfx { get; set; }

		public int Groupe { get; set; }

		public int Honor { get; set; }

		public int Id { get; set; }

		public int Intelligence { get; set; }

		public string Jobs { get; set; }

		public long Kamas { get; set; }

		public int Level { get; set; }

		public int Logged { get; set; }

		public int Map { get; set; }

		public int Mount { get; set; }

		public string Name { get; set; }

		public string Objets { get; set; }

		public long Prison { get; set; }

		public int Sagesse { get; set; }

		public string Savepos { get; set; }

		public int Server { get; set; }

		public sbyte Sexe { get; set; }

		public int Size { get; set; }

		public int Spellboost { get; set; }

		public string Spells { get; set; }

		public string StoreObjets { get; set; }

		public static string TablePerso
		{
			get
			{
				return JsonManager.SearchAuth("perso");
			}
		}

		public int Title { get; set; }

		public int Vitalite { get; set; }

		public int Wife { get; set; }

		public long Xp { get; set; }

		public string Zaaps { get; set; }

		public static int PersoCount = 0;
		public CharacterList(IDataReader reader)
		{
			Id = (int)reader["id"];
			Name = (string)reader["name"];
			Account = (int)reader["account"];
			Groupe = (int)reader["groupe"];
			Sexe = (sbyte)reader["sexe"];
			Class = (short)reader["class"];
			Color1 = (int)reader["color1"];
			Color2 = (int)reader["color2"];
			Color3 = (int)reader["color3"];
			Kamas = (long)reader["kamas"];
			Spellboost = (int)reader["spellboost"];
			Capital = (int)reader["capital"];
			Energy = (int)reader["energy"];
			Level = (int)reader["level"];
			Xp = (long)reader["xp"];
			Size = (int)reader["size"];
			Gfx = (int)reader["gfx"];
			Alignement = (int)reader["alignement"];
			Honor = (int)reader["honor"];
			Deshonor = (int)reader["deshonor"];
			Alvl = (int)reader["alvl"];
			Vitalite = (int)reader["vitalite"];
			Force = (int)reader["force"];
			Sagesse = (int)reader["sagesse"];
			Intelligence = (int)reader["intelligence"];
			Chance = (int)reader["chance"];
			Agilite = (int)reader["agilite"];
			Map = (int)reader["map"];
			Cell = (int)reader["cell"];
			Spells = (string)reader["spells"];
			Objets = (string)reader["objets"];
			StoreObjets = (string)reader["storeObjets"];
			Savepos = (string)reader["savepos"];
			Zaaps = (string)reader["zaaps"];
			Jobs = (string)reader["jobs"];
			Mount = (int)reader["mount"];
			Title = (int)reader["title"];
			Wife = (int)reader["wife"];
			Prison = (long)reader["prison"];
			Server = (int)reader["server"];
			Logged = (int)reader["logged"];
		}

		public static void AllPerso()
		{
			string[] args = new string[] { "*" };
			string query = QueryBuilder.SelectFromQuery(args, TablePerso, "", "");

			using (MySqlConnection connection = new MySqlConnection(DatabaseManager.ConnectionString))
			{
				try
				{
					connection.Open();
					MySqlDataReader lecteur = new MySqlCommand(query, connection).ExecuteReader();
					CharacterList CL = null;
					while (lecteur.Read())
					{
						CL = new CharacterList(lecteur);
						PersoAll.Add(CL.Name, CL);
						if (!IdAccount.ContainsKey(CL.Id))
						{
							IdAccount.Add(CL.Id, CL.Name);
						}
					}
					PersoCount = PersoAll.Count;
					lecteur.Close();
					lecteur.Dispose();
					connection.Close();
					connection.Dispose();
				}
				catch (MySqlException) { }
			}
			
		}

		public static void GetInventory(string perso)
		{
			preinventory.Clear();
			ItemsPerso.Clear();
			foreach(string item in Listing(perso).Objets.Split('|'))
            {
				if (!string.IsNullOrWhiteSpace(item))
                    preinventory.Add(item);
            }	
			foreach(string i in preinventory)
            {
				int h = Convert.ToInt32(i);
				if (ItemList.ItemsList.ContainsKey(h))
                {
					int n = ItemList.ItemsList.FirstOrDefault( x => x.Key == h).Value.Template;
					string name = ItemTemplateList.GetItem(n, 1);
				   ItemsPerso.Add($"{name} ({h}) x{ItemList.ItemsList.FirstOrDefault(x => x.Key == h).Value.Qua}");
				}
			}


		}
		public static string GetObjectFromInventory(string name)
        {
			return Listing(name).Objets;
        }

		public static void GetSpells(string perso)
		{
			string[] strArrays = Listing(perso).Spells.Split(new char[] { ',' });
			for (int i = 0; i < (int)strArrays.Length; i++)
			{
				SpellsList.AddSpellsToList(strArrays[i]);
			}
		}

		public static List<string> Informations(int guid)
		{
			List<string> listPerso = new List<string>();
			listPerso.Clear();
            try
            {
				foreach (CharacterList h in PersoAll.Values)
				{
					if(h.Account == guid)
						listPerso.Add(h.Name);
				}
            }
            catch { }
			return listPerso;
		}

		public static CharacterList Listing(string name)
		{
			
				try
				{
					return CharacterList.PersoAll[name];
				}
				catch (MySqlException) { return null; }
			
			
		}
	}
}