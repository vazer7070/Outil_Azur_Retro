using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace Tools_protocol.Query
{
	public class DatabaseManager2
	{
		public readonly static object Objects = new object();

		static Dictionary<string, string> SelectConnection;

		public static string ConnectionString { get; set; }

		public static bool IsServerConnected(string host, string username, string password, string databaseName)
		{
			string connectionString = string.Concat(new string[] { "server=", host, ";uid=", username, ";pwd=", password, ";database=", databaseName });
			using (MySqlConnection connection = new MySqlConnection(connectionString))
			{
				try
				{
					connection.Open();
					ConnectionString = connectionString;
					connection.Close();
					connection.Dispose();
					return true;
				}
				catch (MySqlException)
				{
					return false;
				}
			}
		}
		public static object ExecuteScalar(string query)
		{
			object obj = null;
			using (MySqlConnection connection = new MySqlConnection(ConnectionString))
			{
				try
				{
					connection.Open();
					obj = new MySqlCommand(query ?? "", connection).ExecuteScalar();
					return obj;
				}
				catch (MySqlException)
				{
					return obj = null;
				}
			}
		}

		public static MySqlDataReader SelectQuery(string query)
		{
			using (MySqlConnection connection = new MySqlConnection(ConnectionString))
			{
				try
				{
					connection.Open();
					return new MySqlCommand(query, connection).ExecuteReader();
				}
				catch (MySqlException) { return null; }
			}

		}

		public static object UpdateQuery(string query)
		{
			object obj = null;
			using (MySqlConnection connection = new MySqlConnection(ConnectionString))
			{
				try
				{
					connection.Open();
					obj = new MySqlCommand(query, connection).ExecuteNonQuery();
					return obj;
				}
				catch (MySqlException)
				{
					return obj = null;
				}
			}
		}
	}
}