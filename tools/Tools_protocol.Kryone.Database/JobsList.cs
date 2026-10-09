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
	public class JobsList
	{
		public static string level;

		public string AP
		{
			get;
			set;
		}

		public string Crafts
		{
			get;
			set;
		}

		public int ID
		{
			get;
			set;
		}

		public string Name
		{
			get;
			set;
		}

		public string Skills
		{
			get;
			set;
		}

		/// <summary>Table des métiers selon le profil d'émulateur courant.</summary>
		public static string TableJobs
		{
			get
			{
				return EmulatorRegistry.Current.Table("metiers");
			}
		}

		public string Tools
		{
			get;
			set;
		}

		public static List<JobsList> AllJobs = new List<JobsList>();
		public JobsList(IDataReader reader)
		{
			ID = (int)reader["id"];
			Name = (string)reader["name"];
			Tools = (string)(!reader.IsDBNull(reader.GetOrdinal("tools")) ? reader[reader.GetOrdinal("tools")] : default(string));
			Crafts = (string)(!reader.IsDBNull(reader.GetOrdinal("crafts")) ? reader[reader.GetOrdinal("crafts")] : default(string));
			Skills = (string)(!reader.IsDBNull(reader.GetOrdinal("skills")) ? reader[reader.GetOrdinal("skills")] : default(string));
			AP = (string)reader["AP"];
		}
		public static int JobsCount;
		public static void ANPE()
		{
			string[] args = new string[] { "*" };
			string query = QueryBuilder.SelectFromQuery(args, TableJobs, "", "");
			using (MySqlConnection connection = new MySqlConnection(EmulatorRegistry.ConnectionFor("metiers")))
			{
				try
				{
					connection.Open();
					var loaded = new List<JobsList>();
					using (var command = new MySqlCommand(query, connection))
					using (var lecteur = command.ExecuteReader())
						while (lecteur.Read()) loaded.Add(new JobsList(lecteur));
					AllJobs.Clear(); AllJobs.AddRange(loaded); JobsCount = loaded.Count;
				}
				catch (MySqlException) { }
			}
		}

		public static string LookJobs(string id)
		{
			return CharacterList.Listing(id)?.Jobs;
		}

		public static string Name_Jobs(string id)
		{
			if (!int.TryParse(id, out int jobId)) return id;
			string cachedName = AllJobs.FirstOrDefault(job => job.ID == jobId)?.Name;
			if (!string.IsNullOrWhiteSpace(cachedName)) return cachedName;
			string table = TableJobs;
			string connectionString = EmulatorRegistry.ConnectionFor("metiers");
			if (!string.IsNullOrWhiteSpace(connectionString) && QueryBuilder.IsIdentifier(table))
			{
				try
				{
					using (var connection = new MySqlConnection(connectionString))
					using (var command = new MySqlCommand($"SELECT `name` FROM `{table}` WHERE `id`=@id", connection))
					{
						command.Parameters.AddWithValue("@id", jobId);
						connection.Open();
						string name = Convert.ToString(command.ExecuteScalar());
						if (!string.IsNullOrWhiteSpace(name)) return name;
					}
				}
				catch (MySqlException) { /* Afficher l'identifiant si la table est absente. */ }
			}
			return $"Métier #{jobId}";
		}
	}
}
