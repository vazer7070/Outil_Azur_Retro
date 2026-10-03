using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using Tools_protocol.Managers;
using Tools_protocol.Query;
using Tools_protocol.Emulators;

namespace Outil_Azur_complet.editeur_items
{
    internal static class ItemCreationService
    {
        private static readonly string[] CommonTemplateColumns = {
            "id", "type", "name", "level", "statsTemplate", "pod", "panoplie", "prix",
            "conditions", "armesInfos", "sold", "avgPrice", "points" };

        internal static string BuildTemplateQuery(string table, string[] commonValues,
            bool exchangeable, bool inShop)
        {
            if (commonValues == null || commonValues.Length != CommonTemplateColumns.Length)
                throw new ArgumentException("Les informations du template sont incomplètes.");
            var available = ReadTemplateColumns(table);
            var columns = CommonTemplateColumns.ToList();
            var values = commonValues.ToList();
            foreach (string required in columns)
                if (!available.Contains(required))
                    throw new InvalidOperationException($"La colonne {required} manque dans {table}.");

            if (available.Contains("doplons")) { columns.Add("doplons"); values.Add("0"); }
            if (available.Contains("exchangeable"))
            {
                columns.Add("exchangeable");
                values.Add(exchangeable ? "1" : "0");
            }
            else if (exchangeable)
                throw new InvalidOperationException("Ce schéma n'expose pas de colonne exchangeable pour définir cette option.");
            if (available.Contains("exchangesObject")) { columns.Add("exchangesObject"); values.Add("0"); }
            if (available.Contains("heroique")) { columns.Add("heroique"); values.Add("0"); }
            if (available.Contains("boutique"))
            {
                columns.Add("boutique");
                values.Add(inShop ? "1" : "0");
            }
            else if (inShop)
                throw new InvalidOperationException("Ce schéma n'expose pas de colonne boutique pour définir cette option.");
            return QueryBuilder.InsertIntoQuery(table, columns.ToArray(), values.ToArray(), "");
        }

        private static HashSet<string> ReadTemplateColumns(string table)
        {
            Identifier(table);
            if (string.IsNullOrWhiteSpace(DatabaseManager.ConnectionString))
                return new HashSet<string>(CommonTemplateColumns.Concat(new[] {
                    "doplons", "exchangeable", "heroique" }), StringComparer.OrdinalIgnoreCase);

            var configuration = new MySqlConnectionStringBuilder(DatabaseManager.ConnectionString);
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var connection = new MySqlConnection(configuration.ConnectionString))
            using (var command = new MySqlCommand(
                "SELECT COLUMN_NAME FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=@schema AND TABLE_NAME=@table",
                connection))
            {
                command.Parameters.AddWithValue("@schema", configuration.Database);
                command.Parameters.AddWithValue("@table", table);
                connection.Open();
                using (var reader = command.ExecuteReader())
                    while (reader.Read()) columns.Add(Convert.ToString(reader[0]));
            }
            if (columns.Count == 0) throw new InvalidOperationException($"La table de templates {table} est introuvable.");
            return columns;
        }

        internal static string BuildPanoplyQuery(string table, string itemsColumn, string nameColumn,
            string name, int templateId)
        {
            string column = QuoteIdentifier(itemsColumn);
            string value = QuoteValue(templateId.ToString());
            return $"UPDATE {QuoteIdentifier(table)} SET {column}=CONCAT_WS(',',NULLIF(TRIM({column}),''),{value}) " +
                $"WHERE {QuoteIdentifier(nameColumn)}={QuoteValue(name)} " +
                $"AND FIND_IN_SET({value},REPLACE({column},' ',''))=0";
        }

        internal static void Inject(IReadOnlyDictionary<string, string> queries, int templateId, int itemGuid)
        {
            ServerSql.Require(EmulatorFeature.ItemCreation);
            if (queries == null || !queries.ContainsKey("template") || !queries.ContainsKey("item") ||
                templateId <= 0 || itemGuid <= 0)
                throw new ArgumentException("La création d'objet est incomplète.");
            if (string.IsNullOrWhiteSpace(DatabaseManager.ConnectionString) ||
                string.IsNullOrWhiteSpace(DatabaseManager2.ConnectionString))
                throw new InvalidOperationException("Les connexions auth et world doivent être actives.");
            var auth = new MySqlConnectionStringBuilder(DatabaseManager.ConnectionString);
            var world = new MySqlConnectionStringBuilder(DatabaseManager2.ConnectionString);
            if (!string.Equals(auth.Server, world.Server, StringComparison.OrdinalIgnoreCase) || auth.Port != world.Port ||
                !string.Equals(auth.UserID, world.UserID, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Les bases auth et world doivent être sur le même serveur SQL et accessibles avec le même compte.");

            var targets = new Dictionary<string, Tuple<string, string>>(StringComparer.Ordinal)
            {
                { "template", Tuple.Create(Identifier(auth.Database), Identifier(EmulatorRegistry.Current.Table("Template"))) },
                { "item", Tuple.Create(Identifier(world.Database), Identifier(EmulatorRegistry.Current.Table("items"))) },
                { "craft", Tuple.Create(Identifier(auth.Database), Identifier(EmulatorRegistry.Current.Table("crafts"))) },
                { "pano", Tuple.Create(Identifier(auth.Database), Identifier(EmulatorRegistry.Current.Table("panoplies"))) }
            };
            foreach (var query in queries)
                if (!targets.ContainsKey(query.Key) || string.IsNullOrWhiteSpace(query.Value))
                    throw new ArgumentException("Une requête de création est invalide.");

            using (var connection = new MySqlConnection(auth.ConnectionString))
            {
                connection.Open();
                foreach (string key in queries.Keys)
                    RequireInnoDb(connection, targets[key]);
                using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
                {
                    RequireUnusedId(connection, transaction, targets["template"], "id", templateId);
                    RequireUnusedId(connection, transaction, targets["item"], "guid", itemGuid);
                    foreach (string key in new[] { "template", "craft", "pano", "item" })
                    {
                        if (!queries.TryGetValue(key, out string sql)) continue;
                        string table = QuoteIdentifier(targets[key].Item2);
                        string prefix = key == "pano" ? "UPDATE " : "INSERT INTO ";
                        if (!sql.StartsWith(prefix + table, StringComparison.Ordinal))
                            throw new ArgumentException("La requête ne cible pas la table attendue.");
                        string qualifiedSql = prefix + Qualified(targets[key]) + sql.Substring(prefix.Length + table.Length);
                        using (var command = new MySqlCommand(qualifiedSql, connection, transaction))
                            if (command.ExecuteNonQuery() != 1)
                                throw new InvalidOperationException($"La requête {key} n'a pas modifié exactement une ligne.");
                    }
                    transaction.Commit();
                }
            }
        }

        private static void RequireUnusedId(MySqlConnection connection, MySqlTransaction transaction,
            Tuple<string, string> target, string column, int id)
        {
            using (var command = new MySqlCommand(
                $"SELECT {QuoteIdentifier(column)} FROM {Qualified(target)} WHERE {QuoteIdentifier(column)}=@id FOR UPDATE",
                connection, transaction))
            {
                command.Parameters.AddWithValue("@id", id);
                if (command.ExecuteScalar() != null)
                    throw new InvalidOperationException($"L'identifiant {id} existe déjà dans {target.Item2}.");
            }
        }

        private static void RequireInnoDb(MySqlConnection connection, Tuple<string, string> target)
        {
            using (var command = new MySqlCommand(
                "SELECT ENGINE FROM information_schema.TABLES WHERE TABLE_SCHEMA=@schema AND TABLE_NAME=@table", connection))
            {
                command.Parameters.AddWithValue("@schema", target.Item1);
                command.Parameters.AddWithValue("@table", target.Item2);
                string engine = Convert.ToString(command.ExecuteScalar());
                if (!string.Equals(engine, "InnoDB", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"La création directe exige InnoDB pour {target.Item1}.{target.Item2} (actuel : {engine}).");
            }
        }

        private static string Qualified(Tuple<string, string> target)
        {
            return QuoteIdentifier(target.Item1) + "." + QuoteIdentifier(target.Item2);
        }

        private static string Identifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || !Regex.IsMatch(value, @"\A[A-Za-z_][A-Za-z0-9_]*\z"))
                throw new ArgumentException("Un nom SQL est invalide.");
            return value;
        }

        private static string QuoteIdentifier(string value) { return "`" + Identifier(value) + "`"; }
        private static string QuoteValue(string value)
        {
            return "'" + (value ?? "").Replace("\\", "\\\\").Replace("'", "''") + "'";
        }
    }
}
