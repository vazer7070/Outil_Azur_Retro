using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Tools_protocol.Kryone.Database;
using Tools_protocol.Query;
using Tools_protocol.Emulators;

namespace Outil_Azur_complet.editeur_items
{
    internal enum InventoryChangeKind { Add, Remove, Update }

    internal sealed class InventoryChange
    {
        public int CharacterId { get; set; }
        public string CharacterName { get; set; }
        public InventoryChangeKind Kind { get; set; }
        public int ItemGuid { get; set; }
        public int TemplateId { get; set; }
        public int Quantity { get; set; }
        public int Position { get; set; }
        public int Puit { get; set; }
        public string Stats { get; set; }
        public int OriginalTemplate, OriginalQuantity, OriginalPosition, OriginalPuit;
        public string OriginalStats;
    }

    internal sealed class InventoryTemplateChoice
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public override string ToString() { return $"{Name} (#{Id})"; }
    }

    internal static class InventoryUpdateService
    {
        private sealed class Tables
        {
            public string Players { get; set; }
            public string Items { get; set; }
            public string Templates { get; set; }
            public string ConnectionString { get; set; }
            public string AuthSchema { get; set; }
            public string WorldSchema { get; set; }
            public string PlayerTable { get; set; }
            public string ItemTable { get; set; }
        }

        internal static string UnavailableReason()
        {
            try
            {
                var tables = ResolveTables();
                using (var connection = new MySqlConnection(tables.ConnectionString))
                {
                    connection.Open();
                    RequireTransactionalTables(connection, tables);
                }
                return null;
            }
            catch (Exception error) { return error.Message; }
        }

        internal static List<InventoryTemplateChoice> LoadTemplateChoices()
        {
            string connectionString = EmulatorRegistry.ConnectionFor("Template");
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("La base auth n'est pas connectée.");
            var auth = new MySqlConnectionStringBuilder(connectionString);
            string qualifiedTable = $"`{Identifier(auth.Database)}`.`{Identifier(ItemTemplateList.TableTemplate)}`";
            var choices = new List<InventoryTemplateChoice>();
            using (var connection = new MySqlConnection(auth.ConnectionString))
            using (var command = new MySqlCommand(
                $"SELECT `id`, `name` FROM {qualifiedTable} ORDER BY `name`, `id`", connection))
            {
                connection.Open();
                using (var reader = command.ExecuteReader())
                    while (reader.Read())
                        choices.Add(new InventoryTemplateChoice
                        {
                            Id = Convert.ToInt32(reader["id"]),
                            Name = Convert.ToString(reader["name"])
                        });
            }
            return choices;
        }

        private static string Identifier(string value)
        {
            if (!QueryBuilder.IsIdentifier(value))
                throw new InvalidOperationException("Un nom de base ou de table SQL est invalide.");
            return value;
        }

        /// <summary>Colonne réelle de la table des exemplaires, entre accents graves (guid/qua/pos selon le profil).</summary>
        private static string Col(string logicalColumn)
        {
            return "`" + Identifier(EmulatorRegistry.Current.ItemColumn(logicalColumn)) + "`";
        }

        private static MySqlConnectionStringBuilder Connection(string logicalTable)
        {
            string connectionString = EmulatorRegistry.ConnectionFor(logicalTable);
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("Les connexions auth et world doivent être actives.");
            return new MySqlConnectionStringBuilder(connectionString);
        }

        private static Tables ResolveTables()
        {
            ServerSql.Require(EmulatorFeature.Inventory);
            // Personnages, exemplaires et modèles sont dans la base que le profil leur attribue :
            // Kryone place les exemplaires dans world, StarLoco les garde avec les personnages dans login.
            var players = Connection("perso");
            var items = Connection("items");
            var templates = Connection("Template");
            ServerSql.RequireSameServer(players, items);
            ServerSql.RequireSameServer(players, templates);
            string authSchema = Identifier(players.Database);
            string worldSchema = Identifier(items.Database);
            string templateSchema = Identifier(templates.Database);
            string playerTable = Identifier(CharacterList.TablePerso);
            string itemTable = Identifier(ItemList.TableItems);
            string templateTable = Identifier(ItemTemplateList.TableTemplate);
            return new Tables
            {
                ConnectionString = players.ConnectionString,
                AuthSchema = authSchema,
                WorldSchema = worldSchema,
                PlayerTable = playerTable,
                ItemTable = itemTable,
                Players = $"`{authSchema}`.`{playerTable}`",
                Items = $"`{worldSchema}`.`{itemTable}`",
                Templates = $"`{templateSchema}`.`{templateTable}`"
            };
        }

        private static void RequireTransactionalTables(MySqlConnection connection, Tables tables)
        {
            ServerSql.RequireStrictWrites(connection);
            foreach (var entry in new[] {
                Tuple.Create(tables.AuthSchema, tables.PlayerTable),
                Tuple.Create(tables.WorldSchema, tables.ItemTable) })
            {
                using (var command = new MySqlCommand(
                    "SELECT ENGINE FROM information_schema.TABLES WHERE TABLE_SCHEMA=@schema AND TABLE_NAME=@table",
                    connection))
                {
                    command.Parameters.AddWithValue("@schema", entry.Item1);
                    command.Parameters.AddWithValue("@table", entry.Item2);
                    string engine = Convert.ToString(command.ExecuteScalar());
                    if (!string.Equals(engine, "InnoDB", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException(
                            $"L'écriture de l'inventaire exige InnoDB pour {entry.Item1}.{entry.Item2} (actuel : {engine ?? "absent"}).");
                }
            }
        }

        internal static Dictionary<int, string> Apply(IReadOnlyCollection<InventoryChange> changes)
        {
            if (changes == null || changes.Count == 0)
                throw new ArgumentException("Aucune modification d'inventaire à enregistrer.", nameof(changes));
            foreach (var change in changes)
                if (change.CharacterId <= 0 || string.IsNullOrWhiteSpace(change.CharacterName) ||
                    change.Quantity <= 0 || !Enum.IsDefined(typeof(InventoryChangeKind), change.Kind) ||
                    change.Kind == InventoryChangeKind.Remove && change.ItemGuid <= 0 ||
                    change.Kind == InventoryChangeKind.Update && (change.ItemGuid <= 0 || change.Puit < 0 || change.Stats == null) ||
                    change.Kind == InventoryChangeKind.Add && change.TemplateId < 0)
                    throw new FormatException("Une modification d'inventaire est invalide.");
            if (changes.GroupBy(change => new { change.CharacterId, change.Kind,
                    Target = change.Kind == InventoryChangeKind.Add ? change.TemplateId : change.ItemGuid })
                .Any(group => group.Count() > 1))
                throw new InvalidOperationException("Une modification d'inventaire est présente plusieurs fois.");
            if (changes.Where(change=>change.Kind!=InventoryChangeKind.Add).GroupBy(change=>new{change.CharacterId,change.ItemGuid}).Any(group=>group.Select(change=>change.Kind).Distinct().Count()>1))
                throw new InvalidOperationException("Annulez la suppression d'un objet avant de le modifier, ou annulez sa modification avant de le supprimer.");

            var tables = ResolveTables();
            var inventories = new Dictionary<int, string>();
            using (var connection = new MySqlConnection(tables.ConnectionString))
            {
                connection.Open();
                RequireTransactionalTables(connection, tables);
                using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
                {
                    int nextGuid = -1;
                    foreach (var group in changes.GroupBy(change => change.CharacterId).OrderBy(group => group.Key))
                    {
                        string expectedName = group.First().CharacterName;
                        if (group.Any(change => !string.Equals(change.CharacterName, expectedName, StringComparison.Ordinal)))
                            throw new InvalidOperationException("Un personnage apparaît sous plusieurs noms dans les modifications.");

                        string originalInventory;
                        using (var command = new MySqlCommand(
                            $"SELECT `name`, `objets`, `logged` FROM {tables.Players} WHERE `id`=@id FOR UPDATE",
                            connection, transaction))
                        {
                            command.Parameters.AddWithValue("@id", group.Key);
                            using (var reader = command.ExecuteReader())
                            {
                                if (!reader.Read()) throw new InvalidOperationException($"Le personnage {expectedName} n'existe plus.");
                                if (!string.Equals(Convert.ToString(reader["name"]), expectedName, StringComparison.Ordinal))
                                    throw new InvalidOperationException($"Le personnage {expectedName} a été renommé.");
                                ServerSql.RequireOffline(reader["logged"], expectedName);
                                originalInventory = reader["objets"] == DBNull.Value ? string.Empty : Convert.ToString(reader["objets"]);
                            }
                        }
                        var itemIds = originalInventory.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                        foreach (var change in group)
                        {
                            if (change.Kind == InventoryChangeKind.Remove)
                                RemoveItem(connection, transaction, tables, itemIds, change);
                            else if (change.Kind == InventoryChangeKind.Update)
                                UpdateItem(connection,transaction,tables,itemIds,change);
                            else
                            {
                                if (nextGuid < 0) nextGuid = ReadNextGuid(connection, transaction, tables);
                                AddItem(connection, transaction, tables, itemIds, change, nextGuid++);
                            }
                        }
                        string updatedInventory = string.Join("|", itemIds);
                        if (originalInventory.EndsWith("|", StringComparison.Ordinal) && updatedInventory.Length > 0)
                            updatedInventory += "|";
                        if (!string.Equals(updatedInventory, originalInventory, StringComparison.Ordinal))
                        {
                            using (var command = new MySqlCommand(
                                $"UPDATE {tables.Players} SET `objets`=@updated WHERE `id`=@id AND `name`=@name AND `logged`=0",
                                connection, transaction))
                            {
                                command.Parameters.AddWithValue("@updated", updatedInventory);
                                command.Parameters.AddWithValue("@id", group.Key);
                                command.Parameters.AddWithValue("@name", expectedName);
                                if (command.ExecuteNonQuery() != 1)
                                    throw new InvalidOperationException($"L'inventaire de {expectedName} n'a pas été enregistré.");
                            }
                        }
                        inventories[group.Key] = updatedInventory;
                    }
                    transaction.Commit();
                }
            }
            return inventories;
        }

        private static int ReadNextGuid(MySqlConnection connection, MySqlTransaction transaction, Tables tables)
        {
            using (var command = new MySqlCommand(
                $"SELECT {Col("guid")} FROM {tables.Items} ORDER BY {Col("guid")} DESC LIMIT 1 FOR UPDATE", connection, transaction))
            {
                object last = command.ExecuteScalar();
                return checked((last == null ? 0 : Convert.ToInt32(last)) + 1);
            }
        }
        private static void UpdateItem(MySqlConnection connection,MySqlTransaction transaction,Tables tables,List<string> inventory,InventoryChange change)
        {
            if(inventory.Count(id=>id==change.ItemGuid.ToString())!=1)throw new InvalidOperationException("L'objet n'appartient plus à ce personnage.");
            EmulatorProfile emulator=EmulatorRegistry.Current;
            using(var command=new MySqlCommand("SELECT * FROM "+tables.Items+" WHERE "+Col("guid")+"=@id FOR UPDATE",connection,transaction))
            {
                command.Parameters.AddWithValue("@id",change.ItemGuid);
                using(var reader=command.ExecuteReader())
                {
                    if(!reader.Read() || Convert.ToInt32(reader["template"])!=change.OriginalTemplate || Convert.ToInt32(reader[emulator.ItemColumn("qua")])!=change.OriginalQuantity || Convert.ToInt32(reader[emulator.ItemColumn("pos")])!=change.OriginalPosition || Convert.ToInt32(reader["puit"])!=change.OriginalPuit || Convert.ToString(reader["stats"])!=change.OriginalStats)
                        throw new InvalidOperationException("L'objet a changé depuis son chargement. Rechargez son inventaire.");
                }
            }
            if(change.Position!=change.OriginalPosition)
            {
                if(change.Position < -1 || change.Position>15)throw new FormatException("La position d'équipement est invalide.");
                if(change.Position>=0)
                {
                    using(var command=new MySqlCommand("SELECT `type` FROM "+tables.Templates+" WHERE `id`=@id",connection,transaction))
                    {
                        command.Parameters.AddWithValue("@id",change.OriginalTemplate);object type=command.ExecuteScalar();
                        var slots=type==null?null:Tool_BotProtocol.Game.Perso.Inventory.InventoryUtilities.GetPosition(Convert.ToInt32(type));
                        if(slots==null || !slots.Any(slot=>(int)slot==change.Position))throw new FormatException("Ce type d'objet ne peut pas être équipé à cet emplacement.");
                    }
                    foreach(string id in inventory.Where(id=>id!=change.ItemGuid.ToString()))
                    using(var command=new MySqlCommand("SELECT "+Col("pos")+" FROM "+tables.Items+" WHERE "+Col("guid")+"=@id FOR UPDATE",connection,transaction))
                    {command.Parameters.AddWithValue("@id",id);object pos=command.ExecuteScalar();if(pos!=null && Convert.ToInt32(pos)==change.Position)throw new InvalidOperationException("Cet emplacement est déjà occupé par un autre objet.");}
                }
            }
            if(change.Position>=0 && change.Position<=15 && change.Quantity!=1)throw new FormatException("Un objet équipé doit avoir une quantité de 1.");
            Editors.ItemFieldCatalog.ValidateEffects(change.Stats);
            using(var command=new MySqlCommand("UPDATE "+tables.Items+" SET "+Col("qua")+"=@quantity,"+Col("pos")+"=@position,`stats`=@stats,`puit`=@puit WHERE "+Col("guid")+"=@id",connection,transaction))
            {command.Parameters.AddWithValue("@quantity",change.Quantity);command.Parameters.AddWithValue("@position",change.Position);command.Parameters.AddWithValue("@stats",change.Stats);command.Parameters.AddWithValue("@puit",change.Puit);command.Parameters.AddWithValue("@id",change.ItemGuid);command.ExecuteNonQuery();}
        }

        private static void RemoveItem(MySqlConnection connection, MySqlTransaction transaction, Tables tables,
            List<string> inventory, InventoryChange change)
        {
            string guid = change.ItemGuid.ToString();
            if (inventory.Count(item => item == guid) != 1)
                throw new InvalidOperationException($"L'objet {guid} n'appartient plus une seule fois à {change.CharacterName}.");
            int currentQuantity;
            using (var command = new MySqlCommand(
                $"SELECT {Col("qua")} FROM {tables.Items} WHERE {Col("guid")}=@guid FOR UPDATE", connection, transaction))
            {
                command.Parameters.AddWithValue("@guid", change.ItemGuid);
                object result = command.ExecuteScalar();
                if (result == null) throw new InvalidOperationException($"L'objet {guid} n'existe plus.");
                currentQuantity = Convert.ToInt32(result);
            }
            if (change.Quantity > currentQuantity)
                throw new InvalidOperationException($"La quantité demandée dépasse le stock de l'objet {guid}.");
            if (change.Quantity == currentQuantity)
            {
                using (var command = new MySqlCommand(
                    $"DELETE FROM {tables.Items} WHERE {Col("guid")}=@guid", connection, transaction))
                {
                    command.Parameters.AddWithValue("@guid", change.ItemGuid);
                    if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException($"Suppression de l'objet {guid} impossible.");
                }
                inventory.Remove(guid);
            }
            else
            {
                using (var command = new MySqlCommand(
                    $"UPDATE {tables.Items} SET {Col("qua")}=@quantity WHERE {Col("guid")}=@guid AND {Col("qua")}=@previous",
                    connection, transaction))
                {
                    command.Parameters.AddWithValue("@quantity", currentQuantity - change.Quantity);
                    command.Parameters.AddWithValue("@guid", change.ItemGuid);
                    command.Parameters.AddWithValue("@previous", currentQuantity);
                    if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException($"Quantité de l'objet {guid} non modifiée.");
                }
            }
        }

        private static void AddItem(MySqlConnection connection, MySqlTransaction transaction, Tables tables,
            List<string> inventory, InventoryChange change, int newGuid)
        {
            string stats;
            using (var command = new MySqlCommand(
                $"SELECT `statstemplate` FROM {tables.Templates} WHERE `id`=@template", connection, transaction))
            {
                command.Parameters.AddWithValue("@template", change.TemplateId);
                object result = command.ExecuteScalar();
                if (result == null) throw new InvalidOperationException($"Le modèle d'objet {change.TemplateId} est absent.");
                stats = result == DBNull.Value ? string.Empty : Convert.ToString(result);
            }
            using (var command = new MySqlCommand(
                $"INSERT INTO {tables.Items} ({Col("guid")},`template`,{Col("qua")},{Col("pos")},`stats`,`puit`) " +
                "VALUES (@guid,@template,@quantity,-1,@stats,0)", connection, transaction))
            {
                command.Parameters.AddWithValue("@guid", newGuid);
                command.Parameters.AddWithValue("@template", change.TemplateId);
                command.Parameters.AddWithValue("@quantity", change.Quantity);
                command.Parameters.AddWithValue("@stats", stats);
                if (command.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException($"L'objet {change.TemplateId} n'a pas été créé.");
            }
            inventory.Add(newGuid.ToString());
        }
    }
}
