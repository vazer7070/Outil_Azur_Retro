using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Runtime.CompilerServices;
using Tools_protocol.Emulators;
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

		/// <summary>Table des groupes de droits selon le profil d'émulateur courant.</summary>
		public static string TableGroupe
		{
			get
			{
				return EmulatorRegistry.Current.Table("groupes");
			}
		}

		static GroupesList()
		{
			GroupesList.Grades = new Dictionary<int, string>();
		}

		/// <summary>Kryone nomme les colonnes nom/commandes, StarLoco name/commands.</summary>
		public GroupesList(IDataReader reader)
		{
			this.Id = Convert.ToInt32(reader["id"]);
			this.Nom = Text(reader, "nom", "name");
			this.Commandes = Text(reader, "commandes", "commands");
		}

		private static string Text(IDataRecord reader, params string[] columns)
		{
			foreach (string column in columns)
				for (int index = 0; index < reader.FieldCount; index++)
					if (string.Equals(reader.GetName(index), column, StringComparison.OrdinalIgnoreCase))
						return reader.IsDBNull(index) ? "" : Convert.ToString(reader.GetValue(index));
			return "";
		}

		public static void groupe()
		{
			string[] args = new string[] { "*" };
			string query = QueryBuilder.SelectFromQuery(args, GroupesList.TableGroupe, "", "");

			using (MySqlConnection connection = new MySqlConnection(EmulatorRegistry.ConnectionFor("groupes")))
			{
				try
				{
					connection.Open();
					MySqlDataReader lecteur = new MySqlCommand(query, connection).ExecuteReader();
					while (lecteur.Read())
					{
						if (!GroupesList.Grades.ContainsKey(Convert.ToInt32(lecteur["id"])))
						{
							GroupesList.Grades.Add(Convert.ToInt32(lecteur["id"]), Text(lecteur, "nom", "name"));
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
