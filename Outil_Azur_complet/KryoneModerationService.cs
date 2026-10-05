using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using Tools_protocol.Kryone.Database;
using Tools_protocol.Query;
using Tools_protocol.Emulators;

namespace Outil_Azur_complet
{
    internal sealed class CharacterDeletionResult
    {
        public int CharacterId { get; set; }
        public int AccountId { get; set; }
        public string CharacterName { get; set; }
        public IReadOnlyList<int> ItemIds { get; set; }
    }

    internal sealed class AccountDeletionResult
    {
        public uint AccountId { get; set; }
        public string AccountName { get; set; }
        public IReadOnlyList<int> CharacterIds { get; set; }
        public IReadOnlyList<string> CharacterNames { get; set; }
        public IReadOnlyList<int> ItemIds { get; set; }
    }

    internal static class KryoneModerationService
    {
        private sealed class Tables
        {
            public string ConnectionString { get; set; }
            public string AuthSchema { get; set; }
            public string WorldSchema { get; set; }
            public string PlayerSchema { get; set; }
            public string AccountTable { get; set; }
            public string PlayerTable { get; set; }
            public string ItemTable { get; set; }
            public string Accounts { get; set; }
            public string Players { get; set; }
            public string Items { get; set; }
        }

        internal static bool SetAccountBannedForCharacter(int characterId, string characterName, bool banned)
        {
            if (characterId <= 0 || string.IsNullOrWhiteSpace(characterName))
                throw new ArgumentException("Le personnage est invalide.");
            var tables = ResolveTables(false);
            using (var connection = new MySqlConnection(tables.ConnectionString))
            {
                connection.Open();
                RequireTransactionalTables(connection, tables,
                    Tuple.Create(tables.PlayerSchema, tables.PlayerTable),
                    Tuple.Create(tables.AuthSchema, tables.AccountTable));
                using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
                {
                    int accountId = ReadCharacterAccount(connection, transaction, tables,
                        characterId, characterName, true);
                    int previous;
                    using (var command = new MySqlCommand(
                        $"SELECT `account`, `logged`, `banned` FROM {tables.Accounts} " +
                        "WHERE `guid`=@id FOR UPDATE", connection, transaction))
                    {
                        command.Parameters.AddWithValue("@id", accountId);
                        using (var reader = command.ExecuteReader())
                        {
                            if (!reader.Read())
                                throw new InvalidOperationException("Le compte du personnage n'existe plus.");
                            ServerSql.RequireOffline(reader["logged"], "ce compte");
                            previous = Convert.ToInt32(reader["banned"]);
                        }
                    }
                    int next = banned ? 1 : 0;
                    if (previous != next)
                    {
                        using (var command = new MySqlCommand(
                            $"UPDATE {tables.Accounts} SET `banned`=@next " +
                            "WHERE `guid`=@id AND `logged`=0 AND `banned`=@previous",
                            connection, transaction))
                        {
                            command.Parameters.AddWithValue("@next", next);
                            command.Parameters.AddWithValue("@id", accountId);
                            command.Parameters.AddWithValue("@previous", previous);
                            if (command.ExecuteNonQuery() != 1)
                                throw new InvalidOperationException("L'état du compte a changé. Rechargez les données.");
                        }
                    }
                    transaction.Commit();
                    return previous != next;
                }
            }
        }

        internal static CharacterDeletionResult DeleteCharacter(int characterId, string characterName)
        {
            if (characterId <= 0 || string.IsNullOrWhiteSpace(characterName))
                throw new ArgumentException("Le personnage est invalide.");
            var tables = ResolveTables(true);
            using (var connection = new MySqlConnection(tables.ConnectionString))
            {
                connection.Open();
                RequireTransactionalTables(connection, tables,
                    Tuple.Create(tables.PlayerSchema, tables.PlayerTable),
                    Tuple.Create(tables.WorldSchema, tables.ItemTable));
                using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
                {
                    int accountId;
                    List<int> itemIds;
                    using (var command = new MySqlCommand(
                        $"SELECT `account`, `logged`, `objets`, `storeObjets` FROM {tables.Players} " +
                        "WHERE `id`=@id AND `name`=@name FOR UPDATE", connection, transaction))
                    {
                        command.Parameters.AddWithValue("@id", characterId);
                        command.Parameters.AddWithValue("@name", characterName);
                        using (var reader = command.ExecuteReader())
                        {
                            if (!reader.Read()) throw new InvalidOperationException("Le personnage n'existe plus.");
                            ServerSql.RequireOffline(reader["logged"], "ce personnage");
                            accountId = Convert.ToInt32(reader["account"]);
                            itemIds = ParseItemIds(reader["objets"], reader["storeObjets"]);
                        }
                    }
                    DeleteItems(connection, transaction, tables, itemIds);
                    using (var command = new MySqlCommand(
                        $"DELETE FROM {tables.Players} WHERE `id`=@id AND `name`=@name AND `logged`=0",
                        connection, transaction))
                    {
                        command.Parameters.AddWithValue("@id", characterId);
                        command.Parameters.AddWithValue("@name", characterName);
                        if (command.ExecuteNonQuery() != 1)
                            throw new InvalidOperationException("Le personnage a changé. Rechargez les données.");
                    }
                    transaction.Commit();
                    return new CharacterDeletionResult
                    {
                        CharacterId = characterId,
                        AccountId = accountId,
                        CharacterName = characterName,
                        ItemIds = itemIds
                    };
                }
            }
        }

        internal static AccountDeletionResult DeleteAccount(uint accountId, string accountName)
        {
            if (accountId == 0 || string.IsNullOrWhiteSpace(accountName))
                throw new ArgumentException("Le compte est invalide.");
            var tables = ResolveTables(true);
            using (var connection = new MySqlConnection(tables.ConnectionString))
            {
                connection.Open();
                RequireTransactionalTables(connection, tables,
                    Tuple.Create(tables.AuthSchema, tables.AccountTable),
                    Tuple.Create(tables.PlayerSchema, tables.PlayerTable),
                    Tuple.Create(tables.WorldSchema, tables.ItemTable));
                using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
                {
                    using (var command = new MySqlCommand(
                        $"SELECT `account`, `logged` FROM {tables.Accounts} WHERE `guid`=@id FOR UPDATE",
                        connection, transaction))
                    {
                        command.Parameters.AddWithValue("@id", accountId);
                        using (var reader = command.ExecuteReader())
                        {
                            if (!reader.Read()) throw new InvalidOperationException("Le compte n'existe plus.");
                            if (!string.Equals(Convert.ToString(reader["account"]), accountName,
                                StringComparison.Ordinal))
                                throw new InvalidOperationException("Le compte a été renommé. Rechargez les données.");
                            ServerSql.RequireOffline(reader["logged"], "ce compte");
                        }
                    }

                    var characterIds = new List<int>();
                    var characterNames = new List<string>();
                    var itemIds = new List<int>();
                    using (var command = new MySqlCommand(
                        $"SELECT `id`, `name`, `logged`, `objets`, `storeObjets` FROM {tables.Players} " +
                        "WHERE `account`=@accountId ORDER BY `id` FOR UPDATE", connection, transaction))
                    {
                        command.Parameters.AddWithValue("@accountId", accountId);
                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string name = Convert.ToString(reader["name"]);
                                ServerSql.RequireOffline(reader["logged"], name);
                                characterIds.Add(Convert.ToInt32(reader["id"]));
                                characterNames.Add(name);
                                itemIds.AddRange(ParseItemIds(reader["objets"], reader["storeObjets"]));
                            }
                        }
                    }
                    itemIds = itemIds.Distinct().ToList();
                    DeleteItems(connection, transaction, tables, itemIds);
                    using (var command = new MySqlCommand(
                        $"DELETE FROM {tables.Players} WHERE `account`=@accountId", connection, transaction))
                    {
                        command.Parameters.AddWithValue("@accountId", accountId);
                        if (command.ExecuteNonQuery() != characterIds.Count)
                            throw new InvalidOperationException("La liste des personnages a changé. Rechargez les données.");
                    }
                    using (var command = new MySqlCommand(
                        $"DELETE FROM {tables.Accounts} WHERE `guid`=@id AND `account`=@name AND `logged`=0",
                        connection, transaction))
                    {
                        command.Parameters.AddWithValue("@id", accountId);
                        command.Parameters.AddWithValue("@name", accountName);
                        if (command.ExecuteNonQuery() != 1)
                            throw new InvalidOperationException("Le compte a changé. Rechargez les données.");
                    }
                    transaction.Commit();
                    return new AccountDeletionResult
                    {
                        AccountId = accountId,
                        AccountName = accountName,
                        CharacterIds = characterIds,
                        CharacterNames = characterNames,
                        ItemIds = itemIds
                    };
                }
            }
        }

        private static int ReadCharacterAccount(MySqlConnection connection, MySqlTransaction transaction,
            Tables tables, int characterId, string characterName, bool requireOffline)
        {
            using (var command = new MySqlCommand(
                $"SELECT `account`, `logged` FROM {tables.Players} " +
                "WHERE `id`=@id AND `name`=@name FOR UPDATE", connection, transaction))
            {
                command.Parameters.AddWithValue("@id", characterId);
                command.Parameters.AddWithValue("@name", characterName);
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read()) throw new InvalidOperationException("Le personnage n'existe plus.");
                    if (requireOffline) ServerSql.RequireOffline(reader["logged"], "ce personnage");
                    return Convert.ToInt32(reader["account"]);
                }
            }
        }

        private static List<int> ParseItemIds(params object[] inventories)
        {
            var result = new List<int>();
            foreach (object raw in inventories)
            {
                string inventory = raw == null || raw == DBNull.Value ? string.Empty : Convert.ToString(raw);
                foreach (string token in inventory.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!int.TryParse(token.Trim(), out int id) || id <= 0)
                        throw new FormatException($"L'inventaire contient un identifiant d'objet invalide : {token}.");
                    result.Add(id);
                }
            }
            return result.Distinct().ToList();
        }

        private static void DeleteItems(MySqlConnection connection, MySqlTransaction transaction,
            Tables tables, IReadOnlyList<int> itemIds)
        {
            for (int offset = 0; offset < itemIds.Count; offset += 500)
            {
                var batch = itemIds.Skip(offset).Take(500).ToArray();
                string parameters = string.Join(",", batch.Select((id, index) => "@item" + index));
                using (var command = new MySqlCommand(
                    $"DELETE FROM {tables.Items} WHERE `{Identifier(EmulatorRegistry.Current.ItemColumn("guid"))}` IN ({parameters})", connection, transaction))
                {
                    for (int index = 0; index < batch.Length; index++)
                        command.Parameters.AddWithValue("@item" + index, batch[index]);
                    command.ExecuteNonQuery();
                }
            }
        }

        private static Tables ResolveTables(bool requireWorld)
        {
            ServerSql.Require(EmulatorFeature.AccountEditing);
            // Comptes, personnages et exemplaires sont dans la base que le profil leur attribue.
            string accountsConnection = EmulatorRegistry.ConnectionFor("comptes");
            string playersConnection = EmulatorRegistry.ConnectionFor("perso");
            if (string.IsNullOrWhiteSpace(accountsConnection) || string.IsNullOrWhiteSpace(playersConnection))
                throw new InvalidOperationException("La connexion auth doit être active.");
            var auth = new MySqlConnectionStringBuilder(accountsConnection);
            var players = new MySqlConnectionStringBuilder(playersConnection);
            if (!string.Equals(auth.Server, players.Server, StringComparison.OrdinalIgnoreCase) ||
                auth.Port != players.Port ||
                !string.Equals(auth.UserID, players.UserID, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Les bases des comptes et des personnages doivent partager le même serveur et le même compte SQL.");
            MySqlConnectionStringBuilder world = null;
            if (requireWorld)
            {
                string itemsConnection = EmulatorRegistry.ConnectionFor("items");
                if (string.IsNullOrWhiteSpace(itemsConnection))
                    throw new InvalidOperationException("La connexion world doit être active.");
                world = new MySqlConnectionStringBuilder(itemsConnection);
                if (!string.Equals(auth.Server, world.Server, StringComparison.OrdinalIgnoreCase) ||
                    auth.Port != world.Port ||
                    !string.Equals(auth.UserID, world.UserID, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        "Les bases auth et world doivent partager le même serveur et le même compte SQL.");
            }
            string authSchema = Identifier(auth.Database);
            string playerSchema = Identifier(players.Database);
            string worldSchema = requireWorld ? Identifier(world.Database) : null;
            string accountTable = Identifier(AccountList.TableCompte);
            string playerTable = Identifier(CharacterList.TablePerso);
            string itemTable = requireWorld ? Identifier(ItemList.TableItems) : null;
            return new Tables
            {
                ConnectionString = auth.ConnectionString,
                AuthSchema = authSchema,
                PlayerSchema = playerSchema,
                WorldSchema = worldSchema,
                AccountTable = accountTable,
                PlayerTable = playerTable,
                ItemTable = itemTable,
                Accounts = $"`{authSchema}`.`{accountTable}`",
                Players = $"`{playerSchema}`.`{playerTable}`",
                Items = requireWorld ? $"`{worldSchema}`.`{itemTable}`" : null
            };
        }

        private static string Identifier(string value)
        {
            if (!QueryBuilder.IsIdentifier(value))
                throw new InvalidOperationException("Un nom de base ou de table SQL est invalide.");
            return value;
        }

        private static void RequireTransactionalTables(MySqlConnection connection, Tables tables,
            params Tuple<string, string>[] required)
        {
            ServerSql.RequireStrictWrites(connection);
            foreach (var entry in required)
            {
                using (var command = new MySqlCommand(
                    "SELECT ENGINE FROM information_schema.TABLES " +
                    "WHERE TABLE_SCHEMA=@schema AND TABLE_NAME=@table", connection))
                {
                    command.Parameters.AddWithValue("@schema", entry.Item1);
                    command.Parameters.AddWithValue("@table", entry.Item2);
                    string engine = Convert.ToString(command.ExecuteScalar());
                    if (!string.Equals(engine, "InnoDB", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException(
                            $"Cette action exige InnoDB pour {entry.Item1}.{entry.Item2} " +
                            $"(actuel : {(string.IsNullOrEmpty(engine) ? "absent" : engine)})." );
                }
            }
        }
    }
}
