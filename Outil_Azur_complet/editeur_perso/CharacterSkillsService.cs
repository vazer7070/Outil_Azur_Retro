using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using MySql.Data.MySqlClient;
using Tools_protocol.Kryone.Database;

namespace Outil_Azur_complet.editeur_perso
{
    public enum CharacterSkillKind { Spells, Jobs }
    public sealed class CharacterSkillEntry
    {
        public int Id { get; set; }
        public long Value { get; set; }
        public string Tail { get; set; }
    }
    public sealed class CharacterSkillChoice
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }
    public sealed class CharacterSkillsSnapshot
    {
        public int CharacterId { get; internal set; }
        public string CharacterName { get; internal set; }
        public CharacterSkillKind Kind { get; internal set; }
        public IReadOnlyList<CharacterSkillEntry> Entries { get; internal set; }
        public IReadOnlyList<CharacterSkillChoice> Choices { get; internal set; }
        internal string ConnectionString, PlayerTable, AccountTable, TemplateTable;
        internal object Original;
    }
    public static class CharacterSkillsService
    {
        private static string Column(CharacterSkillKind kind)
        {
            if (!Enum.IsDefined(typeof(CharacterSkillKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            return kind == CharacterSkillKind.Spells ? "spells" : "jobs";
        }
        public static CharacterSkillsSnapshot Load(int characterId, CharacterSkillKind kind)
        {
            if (characterId <= 0) throw new ArgumentOutOfRangeException(nameof(characterId));
            string column = Column(kind);
            var result = new CharacterSkillsSnapshot
            {
                CharacterId = characterId, Kind = kind, ConnectionString = ServerSql.AuthConnection(),
                PlayerTable = CharacterList.TablePerso, AccountTable = AccountList.TableCompte,
                TemplateTable = kind == CharacterSkillKind.Spells ? SpellsList.TableSort : JobsList.TableJobs
            };
            using (var connection = new MySqlConnection(result.ConnectionString))
            {
                connection.Open();
                using (var command = new MySqlCommand($"SELECT `name`, `{column}` FROM {ServerSql.Identifier(result.PlayerTable)} WHERE `id`=@id", connection))
                {
                    command.Parameters.AddWithValue("@id", characterId);
                    using (var reader = command.ExecuteReader())
                    {
                        if (!reader.Read()) throw new InvalidOperationException("Ce personnage n'existe plus.");
                        result.CharacterName = Convert.ToString(reader["name"]);
                        result.Original = reader[column];
                    }
                }
                result.Entries = Parse(Convert.ToString(result.Original), kind);
                var choices = new List<CharacterSkillChoice>();
                string nameColumn = kind == CharacterSkillKind.Spells ? "nom" : "name";
                using (var command = new MySqlCommand($"SELECT `id`, `{nameColumn}` FROM {ServerSql.Identifier(result.TemplateTable)} ORDER BY `{nameColumn}`, `id`", connection))
                using (var reader = command.ExecuteReader())
                    while (reader.Read()) choices.Add(new CharacterSkillChoice { Id = Convert.ToInt32(reader["id"]), Name = Convert.ToString(reader[nameColumn]) });
                result.Choices = choices;
            }
            return result;
        }
        public static List<CharacterSkillEntry> Parse(string data, CharacterSkillKind kind)
        {
            Column(kind);
            var entries = new List<CharacterSkillEntry>();
            if (string.IsNullOrWhiteSpace(data)) return entries;
            if (data.Length > 200000) throw new FormatException("La liste des compétences est trop grande.");
            char separator = kind == CharacterSkillKind.Spells ? ',' : ';';
            char fieldSeparator = kind == CharacterSkillKind.Spells ? ';' : ',';
            foreach (string record in data.Split(separator))
            {
                if (string.IsNullOrWhiteSpace(record)) continue;
                string[] parts = record.Split(fieldSeparator);
                if (parts.Length < 2 || !int.TryParse(parts[0], out int id) || !long.TryParse(parts[1], out long value))
                    throw new FormatException($"Compétence non reconnue : {record}. Les données n'ont pas été modifiées.");
                entries.Add(new CharacterSkillEntry { Id = id, Value = value, Tail = string.Join(fieldSeparator.ToString(), parts.Skip(2)) });
            }
            return entries;
        }
        public static string Serialize(IEnumerable<CharacterSkillEntry> entries, CharacterSkillKind kind)
        {
            Column(kind);
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            CharacterSkillEntry[] list = entries.ToArray();
            if (list.Length > 1000 || list.Any(entry => entry == null || entry.Id <= 0 || entry.Value < 0 ||
                kind == CharacterSkillKind.Spells && (entry.Value < 1 || entry.Value > 6) ||
                kind == CharacterSkillKind.Jobs && entry.Value > int.MaxValue))
                throw new FormatException("Les identifiants doivent être positifs, les niveaux de sort compris entre 1 et 6 et l'expérience métier entre 0 et 2 147 483 647.");
            if (list.GroupBy(entry => entry.Id).Any(group => group.Count() > 1)) throw new FormatException("Une compétence est présente plusieurs fois.");
            char recordSeparator = kind == CharacterSkillKind.Spells ? ',' : ';';
            char fieldSeparator = kind == CharacterSkillKind.Spells ? ';' : ',';
            foreach (var entry in list)
                if ((entry.Tail ?? "").IndexOfAny(new[] { recordSeparator, '\r', '\n', '\0' }) >= 0)
                    throw new FormatException("Un paramètre de compétence contient un séparateur invalide.");
            string data = string.Join(recordSeparator.ToString(), list.Select(entry => entry.Id.ToString(CultureInfo.InvariantCulture) + fieldSeparator +
                entry.Value.ToString(CultureInfo.InvariantCulture) + (string.IsNullOrEmpty(entry.Tail) ? "" : fieldSeparator + entry.Tail)));
            if (data.Length > 200000) throw new FormatException("La liste des compétences est trop grande.");
            return data;
        }
        public static string Save(CharacterSkillsSnapshot snapshot, IEnumerable<CharacterSkillEntry> entries)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            ServerSql.RequireKryone();
            var list = entries?.ToArray() ?? throw new ArgumentNullException(nameof(entries));
            string data = Serialize(list, snapshot.Kind);
            string column = Column(snapshot.Kind);
            using (var connection = new MySqlConnection(snapshot.ConnectionString))
            {
                connection.Open();
                ServerSql.RequireInnoDb(connection, snapshot.PlayerTable, snapshot.AccountTable);
                using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
                {
                    ServerSql.RequireTextLength(connection, transaction, snapshot.PlayerTable, column, data);
                    int account;
                    using (var command = new MySqlCommand($"SELECT `name`, `account`, `logged`, `{column}` FROM {ServerSql.Identifier(snapshot.PlayerTable)} WHERE `id`=@id FOR UPDATE", connection, transaction))
                    {
                        command.Parameters.AddWithValue("@id", snapshot.CharacterId);
                        using (var reader = command.ExecuteReader())
                        {
                            if (!reader.Read() || Convert.ToString(reader["name"]) != snapshot.CharacterName || !Equals(reader[column], snapshot.Original))
                                throw new InvalidOperationException("Les compétences ou le personnage ont changé. Rechargez avant d'enregistrer.");
                            ServerSql.RequireOffline(reader["logged"], "ce personnage");
                            account = Convert.ToInt32(reader["account"]);
                        }
                    }
                    using (var command = new MySqlCommand($"SELECT `logged` FROM {ServerSql.Identifier(snapshot.AccountTable)} WHERE `guid`=@id FOR UPDATE", connection, transaction))
                    {
                        command.Parameters.AddWithValue("@id", account);
                        object logged = command.ExecuteScalar();
                        if (logged == null || logged == DBNull.Value || Convert.ToInt32(logged) != 0)
                            throw new InvalidOperationException("Le compte doit exister et être déconnecté.");
                    }
                    foreach (var entry in list)
                        using (var command = new MySqlCommand($"SELECT `id` FROM {ServerSql.Identifier(snapshot.TemplateTable)} WHERE `id`=@id", connection, transaction))
                        {
                            command.Parameters.AddWithValue("@id", entry.Id);
                            if (command.ExecuteScalar() == null) throw new FormatException($"La compétence {entry.Id} n'existe pas dans les ressources.");
                        }
                    using (var command = new MySqlCommand($"UPDATE {ServerSql.Identifier(snapshot.PlayerTable)} SET `{column}`=@data WHERE `id`=@id", connection, transaction))
                    { command.Parameters.AddWithValue("@data", data); command.Parameters.AddWithValue("@id", snapshot.CharacterId); command.ExecuteNonQuery(); }
                    transaction.Commit();
                }
            }
            snapshot.Original = data;
            return data;
        }
    }
}
