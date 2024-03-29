using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Runtime.CompilerServices;
using Tools_protocol.Json;
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

		public static string TableJobs
		{
			get
			{
				return JsonManager.SearchAuth("metiers");
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
			using (MySqlConnection connection = new MySqlConnection(DatabaseManager.ConnectionString))
			{
				try
				{
					connection.Open();
					JobsList jobs;
					MySqlDataReader lecteur = new MySqlCommand(query, connection).ExecuteReader();
					while (lecteur.Read())
					{
						jobs = new JobsList(lecteur);
						AllJobs.Add(jobs);
					}
					lecteur.Close();
					JobsCount = AllJobs.Count;
					lecteur.Dispose();
					connection.Close();
					connection.Dispose();
				}
				catch (MySqlException) { }
			}
		}

		public static string LookJobs(string id)
		{
			return CharacterList.Listing(id).Jobs;
		}

		public static string Name_Jobs(string id)
		{
			if(AllJobs[Convert.ToInt32(id)] != null)
			   return AllJobs[Convert.ToInt32(id)].Name;
			else
				return null;
		}
	}
}