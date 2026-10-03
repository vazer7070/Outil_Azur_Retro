using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using MySql.Data.MySqlClient;
using Tools_protocol.Json;
using Tools_protocol.Emulators;

namespace Outil_Azur_complet
{
    public enum ServerResourceKind { Spells, Jobs, Npcs, MonsterGroups, Paddocks, Interactives, Zaaps, NpcTemplates, Monsters, ItemTemplates, ItemSets, Crafts, Drops, NpcQuestions, NpcResponses, MapTriggers, EndFightActions, Quests, QuestSteps, QuestObjectives, InteractiveDoors, ObjectActions, Dungeons, Maps }
    public sealed class ServerDataSnapshot
    {
        public DataTable Data { get; internal set; }
        public ServerResourceKind Kind { get; internal set; }
        public int? MapId { get; internal set; }
        public int? CellCount { get; internal set; }
        internal string ConnectionString, Table, MapColumn, NpcTemplateTable;
        internal string[] Keys;
        internal Dictionary<string, object> Defaults;
        internal Dictionary<string, string> ReferenceNames = new Dictionary<string, string>();
        internal Dictionary<string,Dictionary<string,string>> ReferenceLists = new Dictionary<string,Dictionary<string,string>>(StringComparer.OrdinalIgnoreCase);
        internal string ReferenceNotice;
        internal Dictionary<string,string> RelatedTables = new Dictionary<string,string>();
        internal bool HasDatabaseKey;
        internal string WorldSchema, WorldItemTable;
    }
    public static partial class ServerDataService
    {
        private static string Key(ServerResourceKind kind)
        {
            switch (kind)
            {
                case ServerResourceKind.Spells: return "sort";
                case ServerResourceKind.Jobs: return "metiers";
                case ServerResourceKind.Npcs: return "npcs";
                case ServerResourceKind.MonsterGroups: return "groupe_monstre";
                case ServerResourceKind.Paddocks: return "enclos";
                case ServerResourceKind.Interactives: return "interactions";
                case ServerResourceKind.Zaaps: return "zaaps";
                case ServerResourceKind.NpcTemplates: return "npc_template";
                case ServerResourceKind.Monsters: return "monstres";
                case ServerResourceKind.ItemTemplates: return "Template";
                case ServerResourceKind.ItemSets: return "panoplies";
                case ServerResourceKind.Crafts: return "crafts";
                case ServerResourceKind.Drops: return "drops";
                case ServerResourceKind.NpcQuestions: return "npc_questions";
                case ServerResourceKind.NpcResponses: return "npc_reponse";
                case ServerResourceKind.MapTriggers: return "cellule";
                case ServerResourceKind.EndFightActions: return "endfight";
                case ServerResourceKind.Quests: return "quete";
                case ServerResourceKind.QuestSteps: return "quete_etape";
                case ServerResourceKind.QuestObjectives: return "quete_objectif";
                case ServerResourceKind.InteractiveDoors: return "Iporte";
                case ServerResourceKind.ObjectActions: return "objets_actions";
                case ServerResourceKind.Dungeons: return "donjons";
                case ServerResourceKind.Maps: return "cartes";
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
        // Column names come from the server schema, never from user-supplied SQL.
        private static string Column(string name) { return "`" + name.Replace("`", "``") + "`"; }
        public static ServerDataSnapshot Load(ServerResourceKind kind, int? mapId = null, int? cellCount = null)
        {
            string key = Key(kind);
            var snapshot = new ServerDataSnapshot { Kind = kind, MapId = mapId, CellCount = cellCount,
                ConnectionString = ServerSql.AuthConnection(EmulatorFeature.ResourceEditors), Table = JsonManager.SearchAuth(key), Defaults = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) };
            if (kind == ServerResourceKind.Npcs) snapshot.NpcTemplateTable = JsonManager.SearchAuth("npc_template");
            foreach (string related in new[] { "Template", "panoplies", "crafts", "monstres", "drops", "npc_questions", "npc_reponse", "npc_template", "npcs" })
                snapshot.RelatedTables[related] = JsonManager.SearchAuth(related);
            if(kind==ServerResourceKind.ItemTemplates && !string.IsNullOrWhiteSpace(Tools_protocol.Query.DatabaseManager2.ConnectionString))
            {
                var auth=new MySqlConnectionStringBuilder(snapshot.ConnectionString);var world=new MySqlConnectionStringBuilder(Tools_protocol.Query.DatabaseManager2.ConnectionString);
                if(string.Equals(auth.Server,world.Server,StringComparison.OrdinalIgnoreCase) && auth.Port==world.Port && string.Equals(auth.UserID,world.UserID,StringComparison.OrdinalIgnoreCase))
                {snapshot.WorldSchema=world.Database;snapshot.WorldItemTable=JsonManager.SearchWorld("items");}
            }
            string table = ServerSql.Identifier(snapshot.Table);
            using (var connection = new MySqlConnection(snapshot.ConnectionString))
            {
                connection.Open();
                using (var command = new MySqlCommand($"SELECT * FROM {table} LIMIT 0", connection))
                using (var reader = command.ExecuteReader()) { snapshot.Data = new DataTable(); snapshot.Data.Load(reader); }
                snapshot.MapColumn = snapshot.Data.Columns.Cast<DataColumn>().FirstOrDefault(column => column.ColumnName.Equals("mapid", StringComparison.OrdinalIgnoreCase) || column.ColumnName.Equals("map", StringComparison.OrdinalIgnoreCase))?.ColumnName;
                if (mapId.HasValue && (mapId <= 0 || snapshot.MapColumn == null || !cellCount.HasValue || cellCount <= 0))
                    throw new InvalidOperationException("Cette ressource ne peut pas être éditée pour la carte sélectionnée.");
                using (var command = new MySqlCommand("SELECT INDEX_NAME, COLUMN_NAME FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=@schema AND TABLE_NAME=@table AND NON_UNIQUE=0 ORDER BY (INDEX_NAME='PRIMARY') DESC, INDEX_NAME, SEQ_IN_INDEX", connection))
                {
                    command.Parameters.AddWithValue("@schema", connection.Database); command.Parameters.AddWithValue("@table", snapshot.Table);
                    var indexes = new Dictionary<string, List<string>>();
                    using (var reader = command.ExecuteReader()) while (reader.Read())
                    {
                        string index = Convert.ToString(reader[0]);
                        if (!indexes.ContainsKey(index)) indexes[index] = new List<string>();
                        indexes[index].Add(Convert.ToString(reader[1]));
                    }
                    snapshot.Keys = indexes.Values.FirstOrDefault(columns => columns.All(name => snapshot.Data.Columns.Contains(name)))?.ToArray() ?? new string[0];
                    snapshot.HasDatabaseKey = snapshot.Keys.Length > 0;
                }
                using (var command = new MySqlCommand("SELECT COLUMN_NAME, COLUMN_DEFAULT, IS_NULLABLE, EXTRA, CHARACTER_MAXIMUM_LENGTH FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=@schema AND TABLE_NAME=@table ORDER BY ORDINAL_POSITION", connection))
                {
                    command.Parameters.AddWithValue("@schema", connection.Database); command.Parameters.AddWithValue("@table", snapshot.Table);
                    using (var reader = command.ExecuteReader()) while (reader.Read())
                    {
                        DataColumn column = snapshot.Data.Columns[Convert.ToString(reader[0])];
                        column.AllowDBNull = Convert.ToString(reader[2]) == "YES";
                        string extra = Convert.ToString(reader[3]);
                        column.ReadOnly = extra.IndexOf("STORED GENERATED", StringComparison.OrdinalIgnoreCase) >= 0 || extra.IndexOf("VIRTUAL GENERATED", StringComparison.OrdinalIgnoreCase) >= 0;
                        column.AutoIncrement = false; // Explicit IDs also work with AUTO_INCREMENT tables.
                        if (column.DataType == typeof(string) && reader[4] != DBNull.Value)
                            column.MaxLength = (int)Math.Min(int.MaxValue, Convert.ToInt64(reader[4]));
                        object value = reader[1];
                        if (value != DBNull.Value)
                        {
                            try { value = Convert.ChangeType(value, column.DataType, CultureInfo.InvariantCulture); }
                            catch (Exception) { value = DBNull.Value; }
                        }
                        snapshot.Defaults[column.ColumnName] = value;
                    }
                }
                string filter = mapId.HasValue ? " WHERE " + Column(snapshot.MapColumn) + "=@map" : "";
                using (var command = new MySqlCommand($"SELECT * FROM {table}{filter}" + (snapshot.Keys.Length > 0 ? " ORDER BY " + string.Join(",", snapshot.Keys.Select(Column)) : ""), connection))
                {
                    if (mapId.HasValue) command.Parameters.AddWithValue("@map", mapId.Value);
                    using (var reader = command.ExecuteReader()) snapshot.Data.Load(reader);
                }
                if (snapshot.Keys.Length > 0) snapshot.Data.PrimaryKey = snapshot.Keys.Select(name => snapshot.Data.Columns[name]).ToArray();
                // Old Kryone tables sometimes have no unique index. In that case
                // compare the entire original row and refuse ambiguous matches.
                if (!snapshot.HasDatabaseKey) snapshot.Keys = snapshot.Data.Columns.Cast<DataColumn>().Where(column => !column.ReadOnly).Select(column => column.ColumnName).ToArray();
                snapshot.Data.AcceptChanges();
                if (kind == ServerResourceKind.Npcs || kind == ServerResourceKind.MonsterGroups || kind == ServerResourceKind.Jobs || kind == ServerResourceKind.Monsters || kind == ServerResourceKind.ItemSets || kind == ServerResourceKind.Crafts || kind == ServerResourceKind.Drops || kind == ServerResourceKind.ItemTemplates || kind == ServerResourceKind.NpcQuestions || kind == ServerResourceKind.Quests || kind == ServerResourceKind.QuestSteps || kind == ServerResourceKind.NpcTemplates || kind == ServerResourceKind.ObjectActions || kind == ServerResourceKind.Dungeons)
                {
                    string referenceTable = kind == ServerResourceKind.Npcs ? snapshot.NpcTemplateTable : JsonManager.SearchAuth(kind == ServerResourceKind.Monsters ? "sort" : kind == ServerResourceKind.MonsterGroups ? "monstres" : kind == ServerResourceKind.ItemTemplates ? "panoplies" : kind == ServerResourceKind.NpcQuestions ? "npc_reponse" : kind == ServerResourceKind.Quests ? "quete_etape" : kind == ServerResourceKind.QuestSteps ? "quete_objectif" : kind == ServerResourceKind.NpcTemplates ? "npc_questions" : "Template");
                    if (!string.IsNullOrWhiteSpace(referenceTable))
                    {
                        try
                        {
                            string selection = kind == ServerResourceKind.Jobs ? "`id`,`name`" : kind == ServerResourceKind.Monsters ? "`id`,`nom`" : "*";
                            using (var command = new MySqlCommand("SELECT " + selection + " FROM " + ServerSql.Identifier(referenceTable), connection))
                            using (var reader = command.ExecuteReader())
                            {
                                int nameIndex = Enumerable.Range(0, reader.FieldCount).Where(i => reader.GetName(i).Equals("name", StringComparison.OrdinalIgnoreCase) || reader.GetName(i).Equals("nom", StringComparison.OrdinalIgnoreCase) || reader.GetName(i).Equals("description",StringComparison.OrdinalIgnoreCase)).DefaultIfEmpty(-1).First();
                                int gfxIndex = Enumerable.Range(0, reader.FieldCount).Where(i => reader.GetName(i).Equals("gfxID", StringComparison.OrdinalIgnoreCase)).DefaultIfEmpty(-1).First();
                                while (reader.Read())
                                {
                                    string id = Convert.ToString(reader["id"], CultureInfo.InvariantCulture), name;
                                    if (nameIndex >= 0) name = Convert.ToString(reader[nameIndex]);
                                    else if (!Tools_protocol.Kryone.Database.NPCList.PNJIdName.TryGetValue(id, out name)) name = (kind==ServerResourceKind.Npcs?"PNJ ":"Fiche ") + id + (gfxIndex >= 0 ? " · apparence " + reader[gfxIndex] : "");
                                    if(name.Length>100)name=name.Substring(0,100)+"…";
                                    snapshot.ReferenceNames[id] = name;
                                }
                            }
                        }
                        catch (MySqlException) { snapshot.ReferenceNotice = "Liste des noms indisponible : les identifiants restent modifiables."; }
                    }
                }
                if(kind==ServerResourceKind.Drops && !string.IsNullOrWhiteSpace(JsonManager.SearchAuth("monstres")))
                {
                    try{var names=new Dictionary<string,string>();using(var command=new MySqlCommand("SELECT `id`,`name` FROM "+ServerSql.Identifier(JsonManager.SearchAuth("monstres")),connection))using(var reader=command.ExecuteReader())while(reader.Read())names[Convert.ToString(reader[0])]=Convert.ToString(reader[1]);snapshot.ReferenceLists["monsterId"]=names;}
                    catch(MySqlException){snapshot.ReferenceNotice="Certains noms ne sont pas disponibles ; les identifiants restent modifiables.";}
                }
            }
            return snapshot;
        }
        public static DataRow Add(ServerDataSnapshot snapshot, int selectedCell = 0)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            DataRow row = snapshot.Data.NewRow();
            foreach (DataColumn column in snapshot.Data.Columns)
            {
                if (column.ReadOnly) continue;
                object value = snapshot.Defaults[column.ColumnName];
                if (value == DBNull.Value && !column.AllowDBNull)
                    value = column.DataType == typeof(string) ? (object)"" : column.DataType == typeof(byte[]) ? new byte[0] : Activator.CreateInstance(column.DataType);
                row[column] = value;
            }
            if (snapshot.MapId.HasValue) row[snapshot.MapColumn] = snapshot.MapId.Value;
            SetIfPresent(row, "cellid", selectedCell);
            if (snapshot.Kind == ServerResourceKind.Spells)
                for (int level = 1; level <= 6; level++) SetIfPresent(row, "lvl" + level, "-1");
            if (snapshot.Kind == ServerResourceKind.Paddocks) { SetIfPresent(row, "owner", -1); SetIfPresent(row, "guild", -1); }
            if (snapshot.Kind == ServerResourceKind.ItemTemplates) { SetIfPresent(row,"level",1); SetIfPresent(row,"panoplie",-1); SetIfPresent(row,"type",1); }
            if (snapshot.Kind == ServerResourceKind.Npcs && snapshot.ReferenceNames.Count > 0) SetIfPresent(row, "npcid", snapshot.ReferenceNames.Keys.Select(int.Parse).OrderBy(id => id).First());
            if (row.Table.Columns.Contains("id"))
            {
                long next = row.Table.Rows.Cast<DataRow>().Where(existing => existing.RowState != DataRowState.Deleted).Select(existing => Convert.ToInt64(existing["id"])).DefaultIfEmpty(0).Max() + 1;
                row["id"] = next;
            }
            if (snapshot.HasDatabaseKey)
            {
                Func<bool> used = () => snapshot.Data.Rows.Find(snapshot.Keys.Select(key => row[key]).ToArray()) != null;
                if (used() && snapshot.Keys.Any(key => key.Equals("cellid", StringComparison.OrdinalIgnoreCase)))
                {
                    int limit = snapshot.CellCount ?? 19801;
                    for (int offset = 1; offset < limit && used(); offset++) row["cellid"] = (selectedCell + offset) % limit;
                }
                if (used()) throw new InvalidOperationException("Une fiche utilise déjà cette clé. Modifiez la fiche existante ou choisissez une autre carte.");
            }
            snapshot.Data.Rows.Add(row);
            return row;
        }
        private static void SetIfPresent(DataRow row, string column, object value)
        { if (row.Table.Columns.Contains(column)) row[column] = value; }
        private static bool Same(object left, object right)
        {
            if (left is byte[] && right is byte[]) return ((byte[])left).SequenceEqual((byte[])right);
            return Equals(left, right);
        }
        private static void Validate(ServerDataSnapshot snapshot, DataRow row)
        {
            if (snapshot.MapColumn != null && Convert.ToInt32(row[snapshot.MapColumn]) <= 0) throw new FormatException("L'identifiant de carte doit être positif.");
            if (snapshot.MapId.HasValue && Convert.ToInt32(row[snapshot.MapColumn]) != snapshot.MapId.Value)
                throw new FormatException("Une ligne appartient à une autre carte.");
            foreach (string name in new[] { "cellid" })
                if (row.Table.Columns.Contains(name) &&
                    (Convert.ToInt32(row[name]) < 0 || snapshot.CellCount.HasValue && Convert.ToInt32(row[name]) >= snapshot.CellCount.Value))
                    throw new FormatException("La cellule est hors de la carte.");
            if (row.Table.Columns.Contains("id") && Convert.ToInt64(row["id"]) <= 0) throw new FormatException("L'identifiant doit être positif.");
            switch (snapshot.Kind)
            {
                case ServerResourceKind.Spells:
                    if (string.IsNullOrWhiteSpace(Convert.ToString(row["nom"]))) throw new FormatException("Le nom du sort est obligatoire.");
                    for (int level = 1; level <= 6; level++) ValidateSpellLevel(Convert.ToString(row["lvl" + level]));
                    break;
                case ServerResourceKind.Jobs:
                    if (string.IsNullOrWhiteSpace(Convert.ToString(row["name"]))) throw new FormatException("Le nom du métier est obligatoire.");
                    break;
                case ServerResourceKind.Npcs:
                    if (Convert.ToInt32(row["npcid"]) <= 0 || Convert.ToInt32(row["orientation"]) < 0 || Convert.ToInt32(row["orientation"]) > 7)
                        throw new FormatException("PNJ invalide : identifiant positif et orientation entre 0 et 7 requis.");
                    if (row.Table.Columns.Contains("isMovable") && Convert.ToInt32(row["isMovable"]) != 0 && Convert.ToInt32(row["isMovable"]) != 1)
                        throw new FormatException("isMovable doit valoir 0 ou 1.");
                    break;
                case ServerResourceKind.MonsterGroups:
                    if (string.IsNullOrWhiteSpace(Convert.ToString(row["groupData"]))) throw new FormatException("Définissez les monstres du groupe.");
                    foreach (string entry in Convert.ToString(row["groupData"]).Split(';').Where(entry => entry.Trim() != ""))
                    {
                        string[] fields = entry.Split(',');
                        if (fields.Length < 3 || !int.TryParse(fields[0], out int monster) || monster <= 0 || !int.TryParse(fields[1], out int min) || min <= 0 || !int.TryParse(fields[2], out int max) || max < min)
                            throw new FormatException("Composition du groupe : identifiant de monstre positif et niveaux minimal/maximal cohérents requis.");
                    }
                    if (row.Table.Columns.Contains("Timer") && Convert.ToInt64(row["Timer"]) < 0) throw new FormatException("Le délai doit être positif ou nul.");
                    break;
                case ServerResourceKind.Paddocks:
                    if (Convert.ToInt64(row["price"]) < 0) throw new FormatException("Le prix de l'enclos doit être positif ou nul.");
                    break;
                case ServerResourceKind.Interactives:
                    foreach (string name in new[] { "respawn", "duration" })
                        if (row.Table.Columns.Contains(name) && Convert.ToInt64(row[name]) < 0) throw new FormatException("Les délais doivent être positifs ou nuls.");
                    break;
                case ServerResourceKind.ItemTemplates:
                    if (string.IsNullOrWhiteSpace(Convert.ToString(row["name"]))) throw new FormatException("Le nom de l'objet est obligatoire.");
                    if (Convert.ToInt32(row["level"]) <= 0 || Convert.ToInt32(row["type"]) <= 0) throw new FormatException("Le niveau et le type doivent être positifs.");
                    foreach (string name in new[] { "pod", "prix", "points", "avgPrice", "sold" }) if (row.Table.Columns.Contains(name) && Convert.ToInt64(row[name]) < 0) throw new FormatException("Poids, prix et points doivent être positifs ou nuls.");
                    Editors.ItemFieldCatalog.ValidateEffects(Convert.ToString(row["statsTemplate"]));
                    break;
                case ServerResourceKind.ItemSets:
                    if (string.IsNullOrWhiteSpace(Convert.ToString(row["name"]))) throw new FormatException("Le nom de la panoplie est obligatoire.");
                    Editors.ItemFieldCatalog.PositiveIds(Convert.ToString(row["items"]), ',');
                    Editors.ItemFieldCatalog.ValidateSetBonus(Convert.ToString(row["bonus"]));
                    break;
                case ServerResourceKind.Crafts:
                    Editors.ItemFieldCatalog.ValidateRecipe(Convert.ToString(row["craft"]));
                    break;
                case ServerResourceKind.Drops:
                    foreach (string name in new[] { "monsterId", "objectId" }) if (Convert.ToInt64(row[name]) <= 0) throw new FormatException("Choisissez un monstre et un objet valides.");
                    foreach (DataColumn column in row.Table.Columns) if (column.ColumnName.StartsWith("percentGrade",StringComparison.OrdinalIgnoreCase) && (Convert.ToDecimal(row[column]) < 0 || Convert.ToDecimal(row[column]) > 100)) throw new FormatException("Un taux de butin doit être compris entre 0 et 100 %.");
                    if (Convert.ToInt64(row["ceil"]) < 0) throw new FormatException("Le seuil de prospection doit être positif ou nul.");
                    break;
                case ServerResourceKind.Maps:
                    foreach(string name in new[]{"width","heigth","height"})if(row.Table.Columns.Contains(name)){int size=Convert.ToInt32(row[name]);if(size!=-1 && (size<2 || size>100))throw new FormatException("Largeur et hauteur : 2 à 100, ou -1 pour la valeur spéciale du serveur.");}
                    foreach(string name in new[]{"numgroup","minSize","maxSize"})if(row.Table.Columns.Contains(name) && Convert.ToInt32(row[name])<0)throw new FormatException("Les tailles et nombres doivent être positifs ou nuls.");
                    break;
            }
        }
        public static void ValidateSpellLevel(string data)
        {
            if (string.IsNullOrWhiteSpace(data) || data == "-1") return;
            string[] fields = data.Split(',').Select(value => value.Trim()).ToArray();
            if (fields.Length != 19 && fields.Length != 20) throw new FormatException("Un niveau de sort doit contenir 19 ou 20 champs.");
            foreach (int index in new[] { 2, 3, 4, 5, 6, 11, 12, 13 })
                if (!int.TryParse(fields[index], out int number) || number < (index == 2 ? -1 : 0)) throw new FormatException("Un paramètre numérique du niveau de sort est invalide.");
            foreach (int index in new[] { 7, 8, 9, 10 })
                if (fields[index] != "0" && fields[index] != "1" && !bool.TryParse(fields[index], out bool value)) throw new FormatException("Un booléen du niveau de sort est invalide.");
            foreach (string effects in fields.Take(2))
                if (effects != "-1" && effects != "") foreach (string effect in effects.Split('|'))
                {
                    string[] parts = effect.Split(';');
                    if (parts.Length < 5 || !int.TryParse(parts[0], out int id) || id < 0) throw new FormatException("Un effet de sort est incomplet.");
                    for (int i = 1; i < Math.Min(6, parts.Length); i++)
                        if (parts[i] != "" && !int.TryParse(parts[i], out int parameter)) throw new FormatException("Une valeur, durée ou probabilité d'effet n'est pas numérique.");
                }
            int zoneIndex = fields.Length - 5;
            if (!int.TryParse(fields[zoneIndex + 3], out int requiredLevel) || requiredLevel < 0) throw new FormatException("Le niveau requis du personnage doit être positif ou nul.");
            string ending = fields[zoneIndex + 4];
            if (ending != "0" && ending != "1" && !bool.TryParse(ending, out bool endsTurn)) throw new FormatException("L'option de fin de tour sur échec doit valoir Oui ou Non.");
            if (int.Parse(fields[3]) > int.Parse(fields[4])) throw new FormatException("La portée minimale ne peut pas dépasser la portée maximale.");
        }
        private static MySqlCommand ChangeCommand(ServerDataSnapshot snapshot, DataRow row, MySqlConnection connection, MySqlTransaction transaction)
        {
            var command = new MySqlCommand { Connection = connection, Transaction = transaction };
            DataColumn[] writable = row.Table.Columns.Cast<DataColumn>().Where(column => !column.ReadOnly).ToArray();
            if (row.RowState == DataRowState.Added)
            {
                command.CommandText = "INSERT INTO " + ServerSql.Identifier(snapshot.Table) + " (" + string.Join(",", writable.Select(column => Column(column.ColumnName))) + ") VALUES (" + string.Join(",", writable.Select((column, index) => "@n" + index)) + ")";
            }
            else
            {
                string where = string.Join(" AND ", snapshot.Keys.Select((name, index) => Column(name) + " <=> @k" + index));
                for (int index = 0; index < snapshot.Keys.Length; index++) command.Parameters.AddWithValue("@k" + index, row[snapshot.Keys[index], DataRowVersion.Original]);
                command.CommandText = row.RowState == DataRowState.Deleted ? "DELETE FROM " + ServerSql.Identifier(snapshot.Table) + " WHERE " + where :
                    "UPDATE " + ServerSql.Identifier(snapshot.Table) + " SET " + string.Join(",", writable.Select((column, index) => Column(column.ColumnName) + "=@n" + index)) + " WHERE " + where;
            }
            if (row.RowState != DataRowState.Deleted)
                for (int index = 0; index < writable.Length; index++) command.Parameters.AddWithValue("@n" + index, row[writable[index]]);
            return command;
        }
        private static void CheckSnapshot(ServerDataSnapshot snapshot, DataRow row, MySqlConnection connection, MySqlTransaction transaction)
        {
            if (row.RowState == DataRowState.Added) return;
            string where = string.Join(" AND ", snapshot.Keys.Select((name, index) => Column(name) + " <=> @k" + index));
            using (var command = new MySqlCommand("SELECT * FROM " + ServerSql.Identifier(snapshot.Table) + " WHERE " + where + " FOR UPDATE", connection, transaction))
            {
                for (int index = 0; index < snapshot.Keys.Length; index++) command.Parameters.AddWithValue("@k" + index, row[snapshot.Keys[index], DataRowVersion.Original]);
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read()) throw new InvalidOperationException("Une ligne a été supprimée. Rechargez les ressources.");
                    foreach (DataColumn column in row.Table.Columns)
                        if (!Same(reader[column.ColumnName], row[column, DataRowVersion.Original]))
                            throw new InvalidOperationException("Une ligne a changé depuis son chargement. Aucune modification n'a été enregistrée.");
                    if (reader.Read()) throw new InvalidOperationException("La clé ne désigne pas une ligne unique.");
                }
            }
        }
        public static int Save(ServerDataSnapshot snapshot)
        {
            ServerSql.Require(EmulatorFeature.ResourceEditors);
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (snapshot.Keys.Length == 0) throw new NotSupportedException("Cette table exige une clé primaire ou un index unique pour être modifiée.");
            DataTable changes = snapshot.Data.GetChanges();
            if (changes == null) return 0;
            foreach (DataRow row in changes.Rows) if (row.RowState != DataRowState.Deleted) Validate(snapshot, row);
            using (var connection = new MySqlConnection(snapshot.ConnectionString))
            {
                connection.Open(); ServerSql.RequireInnoDb(connection, snapshot.Table);
                using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
                {
                    foreach (DataRow row in changes.Rows)
                    {
                        CheckSnapshot(snapshot, row, connection, transaction);
                        CheckRelations(snapshot,row,connection,transaction);
                        if (snapshot.Kind == ServerResourceKind.Npcs && row.RowState != DataRowState.Deleted)
                        {
                            ServerSql.RequireInnoDb(connection,snapshot.NpcTemplateTable);
                            using (var command = new MySqlCommand("SELECT `id` FROM " + ServerSql.Identifier(snapshot.NpcTemplateTable) + " WHERE `id`=@id", connection, transaction))
                            { command.Parameters.AddWithValue("@id", row["npcid"]); if (command.ExecuteScalar() == null) throw new FormatException("Ce template de PNJ n'existe pas."); }
                        }
                        using (var command = ChangeCommand(snapshot, row, connection, transaction)) command.ExecuteNonQuery();
                        ApplyRelations(snapshot,row,connection,transaction);
                    }
                    transaction.Commit();
                }
            }
            snapshot.Data.AcceptChanges(); return changes.Rows.Count;
        }
        private static string Literal(object value)
        {
            if (value == null || value == DBNull.Value) return "NULL";
            if (value is byte[]) return "X'" + BitConverter.ToString((byte[])value).Replace("-", "") + "'";
            if (value is string || value is DateTime) return "CONVERT(X'" + BitConverter.ToString(Encoding.UTF8.GetBytes(value is DateTime ? ((DateTime)value).ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture) : (string)value)).Replace("-", "") + "' USING utf8mb4)";
            if (value is bool) return (bool)value ? "1" : "0";
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }
        public static void ExportSql(ServerDataSnapshot snapshot, string path)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (snapshot.Keys.Length == 0) throw new NotSupportedException("Une clé unique est nécessaire pour exporter les modifications.");
            var text = new StringBuilder("-- Modifications Azur ; vérifier les schémas et arrêter le serveur de jeu avant application.\r\nSTART TRANSACTION;\r\n");
            foreach (DataRow row in snapshot.Data.Rows)
            {
                if (row.RowState == DataRowState.Unchanged) continue;
                if (row.RowState != DataRowState.Deleted) Validate(snapshot, row);
                using (var command = ChangeCommand(snapshot, row, null, null))
                {
                    string sql = command.CommandText;
                    foreach (MySqlParameter parameter in command.Parameters.Cast<MySqlParameter>().OrderByDescending(parameter => parameter.ParameterName.Length))
                        sql = System.Text.RegularExpressions.Regex.Replace(sql, System.Text.RegularExpressions.Regex.Escape(parameter.ParameterName) + @"\b", match => Literal(parameter.Value));
                    text.Append(sql).Append(";\r\n");
                    foreach(string relation in RelationSql(snapshot,row)) text.Append(relation).Append(";\r\n");
                }
            }
            text.Append("COMMIT;\r\n");
            AtomicText(path, text.ToString());
        }
        public static void AtomicText(string path, string text)
        {
            string fullPath = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            string temporary = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporary, text, new UTF8Encoding(false));
                if (File.Exists(fullPath)) File.Replace(temporary, fullPath, null); else File.Move(temporary, fullPath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public static int ExportAllSql(string tableKey,string path)
        {
            string connectionString=ServerSql.AuthConnection(EmulatorFeature.ResourceEditors),table=ServerSql.Identifier(JsonManager.SearchAuth(tableKey));
            string fullPath=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(fullPath));string temp=fullPath+".tmp-"+Guid.NewGuid().ToString("N");int count=0;
            try
            {
                using(var connection=new MySqlConnection(connectionString))
                using(var writer=new StreamWriter(temp,false,new UTF8Encoding(false)))
                {
                    connection.Open();using(var transaction=connection.BeginTransaction(IsolationLevel.RepeatableRead))
                    using(var command=new MySqlCommand("SELECT * FROM "+table,connection,transaction){CommandTimeout=180})
                    using(var reader=command.ExecuteReader())
                    {
                        writer.WriteLine("-- Export Azur : INSERT pour une table vide ; le schéma n'est pas modifié.");writer.WriteLine("START TRANSACTION;");string columns=string.Join(",",Enumerable.Range(0,reader.FieldCount).Select(index=>Column(reader.GetName(index))));
                        while(reader.Read()){object[] values=new object[reader.FieldCount];reader.GetValues(values);writer.WriteLine("INSERT INTO "+table+" ("+columns+") VALUES ("+string.Join(",",values.Select(Literal))+");");count++;}
                        writer.WriteLine("COMMIT;");
                    }
                }
                if(File.Exists(fullPath))File.Replace(temp,fullPath,null);else File.Move(temp,fullPath);return count;
            }
            finally{if(File.Exists(temp))File.Delete(temp);}
        }
    }
}
