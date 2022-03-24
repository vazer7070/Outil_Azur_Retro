using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Runtime.CompilerServices;
using Tools_protocol.Json;
using Tools_protocol.Query;

namespace Tools_protocol.Kryone.Database
{
	public class GroupesList
	{
		public static Dictionary<int, string> Grades;

		public string Commandes
		{
			get;
			set;
		}

		public int Id
		{
			get;
			set;
		}

		public string Nom
		{
			get;
			set;
		}

		public static string TableGroupe
		{
			get
			{
				return JsonManager.SearchAuth("groupes");
			}
		}

		static GroupesList()
		{
			GroupesList.Grades = new Dictionary<int, string>();
		}

		public GroupesList(IDataReader reader)
		{
			this.Id = (int)reader["id"];
			this.Nom = (string)reader["nom"];
			this.Commandes = (string)reader["commandes"];
		}

		public static void groupe()
		{
			string[] args = new string[] { "*" };
			string query = QueryBuilder.SelectFromQuery(args, GroupesList.TableGroupe, "", "");

			using (MySqlConnection connection = new MySqlConnection(DatabaseManager.ConnectionString))
			{
				try
				{
					connection.Open();
					MySqlDataReader lecteur = new MySqlCommand(query, connection).ExecuteReader();
					while (lecteur.Read())
					{
						if (!GroupesList.Grades.ContainsKey(Convert.ToInt32(lecteur["id"])))
						{
							GroupesList.Grades.Add(Convert.ToInt32(lecteur["id"]), lecteur["nom"].ToString());
						}
					}
					lecteur.Close();
					lecteur.Dispose();
					connection.Close();
					connection.Dispose();
				}
				catch (MySqlException) { }
			}
		}
	}
}