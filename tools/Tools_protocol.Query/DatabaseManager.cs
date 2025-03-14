using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace Tools_protocol.Query
{
    public class DatabaseManager
    {
        private static readonly object lockObject = new object();

        public static string ConnectionString { get; set; }

        public static bool IsServerConnected(string host, string username, string password, string databaseName)
        {
            string connectionString = $"server={host};uid={username};pwd={password};database={databaseName}";
            using (MySqlConnection connection = new MySqlConnection(connectionString))
            {
                try
                {
                    connection.Open();
                    ConnectionString = connectionString;
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
            using (MySqlConnection connection = new MySqlConnection(ConnectionString))
            using (MySqlCommand command = CreateCommand(query, connection))
            {
                try
                {
                    connection.Open();
                    return command.ExecuteScalar();
                }
                catch (MySqlException)
                {
                    return null;
                }
            }
        }

        public static MySqlDataReader SelectQuery(string query)
        {
            MySqlConnection connection = new MySqlConnection(ConnectionString);
            MySqlCommand command = CreateCommand(query, connection);
            try
            {
                connection.Open();
                return command.ExecuteReader(CommandBehavior.CloseConnection);
            }
            catch (MySqlException)
            {
                connection.Close();
                connection.Dispose();
                return null;
            }
        }

        public static int UpdateQuery(string query)
        {
            using (MySqlConnection connection = new MySqlConnection(ConnectionString))
            using (MySqlCommand command = CreateCommand(query, connection))
            {
                try
                {
                    connection.Open();
                    return command.ExecuteNonQuery();
                }
                catch (MySqlException)
                {
                    return -1;
                }
            }
        }

        private static MySqlCommand CreateCommand(string query, MySqlConnection connection)
        {
            return new MySqlCommand(query ?? "", connection);
        }
    }

}