using System;
using System.Text.RegularExpressions;
using MySql.Data.MySqlClient;
using Tools_protocol.Query;
using Tools_protocol.Emulators;

namespace Outil_Azur_complet
{
    internal static class ServerSql
    {
        internal static void RequireStrictWrites(MySqlConnection connection)
        {
            using (var command = new MySqlCommand("SET SESSION sql_mode=CONCAT_WS(',',NULLIF(@@SESSION.sql_mode,''),'STRICT_ALL_TABLES')", connection))
                command.ExecuteNonQuery();
        }
        internal static void RequireOffline(object logged, string subject)
        {
            if (logged == null || logged == DBNull.Value)
                throw new InvalidOperationException("L'état de connexion de " + subject + " est inconnu. Vérifiez cet état sur le serveur avant de modifier ses données.");
            if (Convert.ToInt32(logged) != 0)
                throw new InvalidOperationException("Déconnectez " + subject + " avant de modifier ses données.");
        }

        internal static void RequireTextLength(MySqlConnection connection, MySqlTransaction transaction,
            string table, string column, string value)
        {
            using (var command = new MySqlCommand("SELECT CHARACTER_MAXIMUM_LENGTH FROM information_schema.COLUMNS " +
                "WHERE TABLE_SCHEMA=@schema AND TABLE_NAME=@table AND COLUMN_NAME=@column", connection, transaction))
            {
                command.Parameters.AddWithValue("@schema", connection.Database);
                command.Parameters.AddWithValue("@table", table);
                command.Parameters.AddWithValue("@column", column);
                object maximum = command.ExecuteScalar();
                if (maximum == null) throw new InvalidOperationException("Le champ " + table + "." + column + " n'existe plus.");
                if (maximum != DBNull.Value && value != null && value.Length > Convert.ToInt64(maximum))
                    throw new FormatException("Le champ " + column + " accepte au maximum " + maximum + " caractères dans cette base.");
            }
        }

        internal static void Require(EmulatorFeature feature)
        {
            EmulatorProfile emulator = EmulatorRegistry.Current;
            if (!EmulatorRegistry.HasEmulator)
                throw new NotSupportedException("Aucun émulateur n'est configuré : choisissez-en un dans la configuration d'Azur.");
            if (!emulator.Supports(feature))
                throw new NotSupportedException("Cette fonction n'est pas disponible pour l'émulateur " + emulator.DisplayName + ".");
        }
        internal static string Identifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || !Regex.IsMatch(value, @"\A[A-Za-z_][A-Za-z0-9_]*\z", RegexOptions.CultureInvariant))
                throw new InvalidOperationException("Un nom de base ou de table SQL est invalide.");
            return "`" + value + "`";
        }
        internal static string AuthConnection(EmulatorFeature feature)
        {
            Require(feature);
            if (string.IsNullOrWhiteSpace(DatabaseManager.ConnectionString)) throw new InvalidOperationException("Connectez la base auth.");
            var builder = new MySqlConnectionStringBuilder(DatabaseManager.ConnectionString);
            Identifier(builder.Database);
            return builder.ConnectionString;
        }
        internal static void RequireInnoDb(MySqlConnection connection, params string[] tables)
        { RequireInnoDbInSchema(connection,connection.Database,tables); }
        internal static void RequireInnoDbInSchema(MySqlConnection connection,string schema,params string[] tables)
        {
            RequireStrictWrites(connection);
            foreach (string table in tables)
            {
                using (var command = new MySqlCommand("SELECT ENGINE FROM information_schema.TABLES WHERE TABLE_SCHEMA=@schema AND TABLE_NAME=@table", connection))
                {
                    command.Parameters.AddWithValue("@schema", schema);
                    command.Parameters.AddWithValue("@table", table);
                    string engine = Convert.ToString(command.ExecuteScalar());
                    if (!string.Equals(engine, "InnoDB", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"L'écriture exige InnoDB pour {schema}.{table} (actuel : {engine}).");
                }
            }
        }
    }
}
