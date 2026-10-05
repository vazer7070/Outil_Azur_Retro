using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using Tools_protocol.Emulators;
using Tools_protocol.Managers;
using Tools_protocol.Query;

namespace Tools_protocol.Kryone.Database
{
    [EmuManager("Kryone", "CharacterList")]
    public class CharacterList
	{
		

		public static Dictionary<string, CharacterList> PersoAll = new Dictionary<string, CharacterList>(StringComparer.OrdinalIgnoreCase);

		public static List<string> ItemsPerso = new List<string>();
		public static string InventoryLoadError { get; private set; }

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

		/// <summary>Table des personnages selon le profil d'émulateur courant.</summary>
		public static string TablePerso
		{
			get
			{
				return EmulatorRegistry.Current.Table("perso");
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
			// NULL is an unknown connection state, never proof that a player is offline.
			Logged = reader["logged"] == DBNull.Value ? -1 : Convert.ToInt32(reader["logged"]);
		}

		public static void AllPerso()
		{
			string[] args = new string[] { "*" };
			string query = QueryBuilder.SelectFromQuery(args, TablePerso, "", "");
			var characters = new Dictionary<string, CharacterList>(StringComparer.OrdinalIgnoreCase);
			var namesById = new Dictionary<int, string>();
			using (var connection = new MySqlConnection(EmulatorRegistry.ConnectionFor("perso")))
			using (var command = new MySqlCommand(query, connection))
			{
				connection.Open();
				using (var reader = command.ExecuteReader())
				{
					while (reader.Read())
					{
						var character = new CharacterList(reader);
							if (characters.TryGetValue(character.Name, out var duplicate))
								throw new InvalidOperationException($"Les personnages {duplicate.Id} et {character.Id} portent le même nom ({character.Name}). Corrigez ces noms dans le serveur avant de recharger les personnages.");
							characters.Add(character.Name, character);
						namesById.Add(character.Id, character.Name);
					}
				}
			}
			PersoAll = characters;
			IdAccount = namesById;
			PersoCount = characters.Count;
		}

		public static void GetInventory(string perso)
		{
			InventoryLoadError = null;
			preinventory.Clear();
			ItemsPerso.Clear();
			string inventory = Listing(perso)?.Objets;
			if (string.IsNullOrWhiteSpace(inventory))
				return;
			foreach(string item in inventory.Split('|'))
            {
				if (!string.IsNullOrWhiteSpace(item))
                    preinventory.Add(item);
			}
			var itemIds = preinventory.Select(item => int.TryParse(item, out int id) ? id : -1)
				.Where(id => id >= 0).Distinct().ToArray();
			LoadInventoryItems(itemIds);
			var templateIds = itemIds.Where(id => ItemList.ItemsList.ContainsKey(id))
				.Select(id => ItemList.ItemsList[id].Template).Distinct().ToArray();
			var templateNames = LoadTemplateNames(templateIds);
			foreach(string i in preinventory)
            {
				if (!int.TryParse(i, out int h))
				{
					ItemsPerso.Add($"Objet inconnu ({i})");
					continue;
				}
				if (ItemList.ItemsList.TryGetValue(h, out ItemList item))
                {
					string name = templateNames.TryGetValue(item.Template, out string loadedName)
						? loadedName : ItemTemplateList.GetItem(item.Template, 1);
					ItemsPerso.Add($"{name} ({h}) x{item.Qua}");
				}
				else ItemsPerso.Add($"Objet ({h}) - " +
					(InventoryLoadError == null ? "absent de la table world" : "base world indisponible"));
			}


		}
		public static string GetObjectFromInventory(string name)
        {
			return Listing(name).Objets;
        }

		public static void GetSpells(string perso)
		{
			SpellsList.SpellsShow.Clear();
			string spells = Listing(perso)?.Spells;
			if (string.IsNullOrWhiteSpace(spells))
				return;
			string[] strArrays = spells.Split(new char[] { ',' });
			for (int i = 0; i < (int)strArrays.Length; i++)
			{
				if (!string.IsNullOrWhiteSpace(strArrays[i]) && strArrays[i].Contains(";"))
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
			if (string.IsNullOrWhiteSpace(name))
				return null;
			return PersoAll.TryGetValue(name, out CharacterList character) ? character : null;
		}

		private static bool ValidTable(string table)
		{
			return QueryBuilder.IsIdentifier(table);
		}

		private static void LoadInventoryItems(int[] ids)
		{
			string table = ItemList.TableItems;
			if (ids.Length == 0) return;
			if (!ValidTable(table)) { InventoryLoadError = "Le nom de la table des objets world est invalide dans la configuration."; return; }
			string connectionString = EmulatorRegistry.ConnectionFor("items");
			if (string.IsNullOrWhiteSpace(connectionString)) { InventoryLoadError = "La connexion à la base world n'est pas active. Vérifiez sa configuration."; return; }
			try
			{
				var loaded = new Dictionary<int, ItemList>();
				EmulatorProfile emulator = EmulatorRegistry.Current;
				string columns = string.Join(",", new[] { "guid", "template", "qua", "pos", "stats", "puit" }
					.Select(column => QueryBuilder.QuoteIdentifier(emulator.ItemColumn(column))));
				string key = QueryBuilder.QuoteIdentifier(emulator.ItemColumn("guid"));
				using (var connection = new MySqlConnection(connectionString))
				{
					connection.Open();
					for (int offset = 0; offset < ids.Length; offset += 500)
						using (var command = new MySqlCommand { Connection = connection })
						{
							int[] batch = ids.Skip(offset).Take(500).ToArray();
							string parameters = string.Join(",", batch.Select((id, index) => "@id" + index));
							command.CommandText = $"SELECT {columns} FROM `{table}` WHERE {key} IN ({parameters})";
							for (int index = 0; index < batch.Length; index++) command.Parameters.AddWithValue("@id" + index, batch[index]);
							using (var reader = command.ExecuteReader())
								while (reader.Read())
								{
									var item = new ItemList(reader);
									loaded[item.Guid] = item;
								}
						}
				}
				foreach (int id in ids) ItemList.ItemsList.Remove(id);
				foreach (var entry in loaded) ItemList.ItemsList[entry.Key] = entry.Value;
			}
			catch (MySqlException error) { InventoryLoadError = "Lecture des objets world impossible : " + error.Message; }
		}

		private static Dictionary<int, string> LoadTemplateNames(int[] ids)
		{
			var names = new Dictionary<int, string>();
			string table = ItemTemplateList.TableTemplate;
			string connectionString = EmulatorRegistry.ConnectionFor("Template");
			if (ids.Length == 0 || !ValidTable(table) ||
				string.IsNullOrWhiteSpace(connectionString)) return names;
			try
			{
				using (var connection = new MySqlConnection(connectionString))
				using (var command = new MySqlCommand { Connection = connection })
				{
					string parameters = string.Join(",", ids.Select((id, index) => "@id" + index));
					command.CommandText = $"SELECT `id`, `name` FROM `{table}` WHERE `id` IN ({parameters})";
					for (int index = 0; index < ids.Length; index++)
						command.Parameters.AddWithValue("@id" + index, ids[index]);
					connection.Open();
					using (var reader = command.ExecuteReader())
						while (reader.Read())
							names[Convert.ToInt32(reader["id"])] = Convert.ToString(reader["name"]);
				}
			}
			catch (MySqlException) { /* Le numéro du modèle reste visible. */ }
			return names;
		}
	}
}
