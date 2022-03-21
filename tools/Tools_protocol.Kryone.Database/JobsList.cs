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

		static JobsList()
		{
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
			JobsList jobs;
			string[] args = new string[] { "*" };
			MySqlDataReader lecteur = DatabaseManager.SelectQuery(QueryBuilder.SelectFromQuery(args, TableJobs, "", ""));
			while (lecteur.Read())
			{
				jobs = new JobsList(lecteur);
				AllJobs.Add(jobs);
			}
			lecteur.Close();
			JobsCount = AllJobs.Count;
		}

		public static string LookJobs(string id)
		{
			return CharacterList.Listing(id).Jobs;
		}

		public static string Name_Jobs(string id)
		{
			return AllJobs[Convert.ToInt32(id)].Name;
		}
	}
}