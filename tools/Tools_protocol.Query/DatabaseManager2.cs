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

		private static MySqlConnection Connection
		{
			get;
			set;
		}


		public static void CloseConnect()
		{
			Connection.Close();
			Connection = null;
		}
		public static bool IsServerConnected(string host, string username, string password, string databaseName)
		{
			string connectionString = string.Concat(new string[] { "server=", host, ";uid=", username, ";pwd=", password, ";database=", databaseName });
			using (MySqlConnection connection = new MySqlConnection(connectionString))
			{
				try
				{
					connection.Open();
					return true;
				}
				catch (MySqlException)
				{
					return false;
				}
			}
		}
		public static void Connect(string host, string username, string password, string databaseName)
		{
		     SelectConnection = new Dictionary<string, string>();
			lock (Objects)
			{
				try
				{
					string connectionString = string.Concat(new string[] { "server=", host, ";uid=", username, ";pwd=", password, ";database=", databaseName });
					Connection = new MySqlConnection()
					{
						ConnectionString = connectionString
					};
					Connection.Open();
				}
				catch
				{
					return;
				}
			}
		}

		public static object ExecuteScalar(string query)
		{
			object obj;
			lock (Objects)
			{
				obj = (new MySqlCommand(query ?? "", Connection)).ExecuteScalar();
			}
			return obj;
		}

		public static MySqlDataReader SelectQuery(string query)
		{
			MySqlDataReader mySqlDataReader;
			lock (Objects)
			{
				mySqlDataReader = (new MySqlCommand(query, Connection)).ExecuteReader();
			}
			return mySqlDataReader;
		}

		public static object UpdateQuery(string query)
		{
			object obj;
			lock (Objects)
			{
				obj = (new MySqlCommand(query, Connection)).ExecuteNonQuery();
			}
			return obj;
		}
	}
}