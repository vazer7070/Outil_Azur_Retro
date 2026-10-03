using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Tools_protocol.Json;
using Tools_protocol.Kryone.Database;
using Tools_protocol.Managers;
using Tools_protocol.Query;

namespace Tools_protocol.Parser.XML
{
    public static class XmlParser
    {
        private static readonly Dictionary<string, string> Tables = new Dictionary<string, string>
        {
            { "Maps", "cartes" }, { "Objets", "Template" }, { "Sorts", "sort" },
            { "Panoplies", "panoplies" }, { "Joueurs", "perso" }, { "Métiers", "metiers" },
            { "Maisons", "maisons" }, { "Zaaps", "zaaps" }, { "PNJs", "npcs" }, { "Monstres", "monstres" }
        };

        public static Task<int> ParseSQLToXML(string path, string type, bool ForBot = false)
        {
            if (EmuManager.EMUSELECTED != "Kryone")
                throw new NotSupportedException("L'export de ressources est actuellement pris en charge pour Kryone.");
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Choisissez un dossier de sortie.", nameof(path));
            if (type == null || !Tables.TryGetValue(type, out string key))
                throw new ArgumentException("Ce type de ressource n'est pas pris en charge.", nameof(type));
            if (ForBot && (type == "Panoplies" || type == "Joueurs" || type == "Maisons"))
                throw new NotSupportedException("Le bot n'utilise pas ce type de ressource XML.");
            string table = JsonManager.SearchAuth(key);
            string query = QueryBuilder.SelectFromQuery(new[] { "*" }, table, "", "");
            string connectionString = DatabaseManager.ConnectionString;
            if (string.IsNullOrWhiteSpace(connectionString)) throw new InvalidOperationException("La base de données n'est pas connectée.");
            string folder = Path.GetFullPath(path);
            return Task.Run(() => Export(folder, type, ForBot, table, query, connectionString));
        }

        private static int Export(string folder, string type, bool forBot, string table, string query, string connectionString)
        {
            Directory.CreateDirectory(folder);
            string staging = Path.Combine(folder, ".azur-export-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                if (forBot && type == "PNJs")
                {
                    string templates = JsonManager.SearchAuth("npc_template");
                    QueryBuilder.SelectFromQuery(new[] { "*" }, templates, "", "");
                    query = $"SELECT n.*, t.gfxID AS azur_gfx, t.sex AS azur_sex FROM `{table}` n " +
                        $"LEFT JOIN `{templates}` t ON t.id=n.npcid";
                }
                int count = 0;
                using (var connection = new MySqlConnection(connectionString))
                using (var command = new MySqlCommand(query, connection))
                {
                    connection.Open();
                    using (var reader = command.ExecuteReader())
                    {
                        if (forBot)
                        {
                            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            while (reader.Read())
                            {
                                string name;
                                XElement record = BotRecord(type, reader, out name);
                                if (!names.Add(name)) throw new InvalidDataException("Deux ressources produisent le même nom de fichier : " + name);
                                record.Save(Path.Combine(staging, name));
                                count++;
                            }
                        }
                        else
                        {
                            var settings = new XmlWriterSettings { Indent = true, CheckCharacters = true };
                            using (var writer = XmlWriter.Create(Path.Combine(staging, type + ".xml"), settings))
                            {
                                writer.WriteStartElement("TABLE");
                                writer.WriteAttributeString("NAME", table);
                                while (reader.Read())
                                {
                                    writer.WriteStartElement("RECORD");
                                    for (int i = 0; i < reader.FieldCount; i++)
                                    {
                                        writer.WriteStartElement(XmlConvert.EncodeLocalName(reader.GetName(i)));
                                        if (reader.IsDBNull(i)) writer.WriteAttributeString("NULL", "true");
                                        else writer.WriteString(Text(reader.GetValue(i)));
                                        writer.WriteEndElement();
                                    }
                                    writer.WriteEndElement();
                                    count++;
                                }
                                writer.WriteEndElement();
                            }
                        }
                    }
                }
                // Finish conversion before replacing any existing resource.
                foreach (string source in Directory.GetFiles(staging, "*.xml"))
                {
                    string destination = Path.Combine(folder, Path.GetFileName(source));
                    if (File.Exists(destination)) File.Replace(source, destination, null);
                    else File.Move(source, destination);
                }
                return count;
            }
            finally
            {
                foreach (string file in Directory.GetFiles(staging)) File.Delete(file);
                Directory.Delete(staging);
            }
        }

        private static string Text(object value)
        {
            if (value is byte[] bytes) return Convert.ToBase64String(bytes);
            if (value is DateTime date) return date.ToString("o", CultureInfo.InvariantCulture);
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        }
        private static XElement Element(string name, object value) => new XElement(name, Text(value));

        private static XElement BotRecord(string type, IDataRecord row, out string filename)
        {
            var record = new XElement("RECORD");
            object id;
            switch (type)
            {
                case "Maps":
                    id = row["id"];
                    string[] position = Text(row["mappos"]).Split(',');
                    if (position.Length < 2 || !int.TryParse(position[0], out _) || !int.TryParse(position[1], out _))
                        throw new FormatException("La position de la carte " + id + " est invalide.");
                    record.Add(Element("ID", id), Element("LARGEUR", row["width"]), Element("LONGUEUR", row["heigth"]),
                        Element("X", position[0]), Element("Y", position[1]), Element("MAP_DATA", row["mapData"]), Element("BACK", row["background"]));
                    break;
                case "Objets":
                    id = row["id"];
                    record.Add(Element("ID", id), Element("TYPE", row["type"]), Element("NOM", row["name"]),
                        Element("NIVEAU", row["level"]), Element("PODS", row["pod"]), Element("ETHERE", 0),
                        Element("CONDITIONS", row["conditions"]), Element("STATS", row["statsTemplate"]));
                    break;
                case "Métiers":
                    id = row["id"];
                    record.Add(Element("ID", id), Element("NOM", row["name"]), Element("TOOLS", row["tools"]),
                        Element("CRAFTS", row["crafts"]), Element("SKILLS", row["skills"]), Element("AP", row["ap"]));
                    break;
                case "PNJs":
                    id = row["npcid"];
                    if (row["azur_gfx"] == DBNull.Value || row["azur_sex"] == DBNull.Value)
                        throw new InvalidDataException("Le modèle du PNJ " + id + " est absent.");
                    string name;
                    if (!NPCList.PNJIdName.TryGetValue(Text(id), out name)) name = "PNJ #" + Text(id);
                    record.Add(Element("ID", id), Element("NOM", name), Element("MAP", row["mapid"]),
                        Element("CELLULE", row["cellid"]), Element("ORIENTATION", row["orientation"]),
                        Element("GFX", row["azur_gfx"]), Element("SEXE", row["azur_sex"]));
                    filename = Text(id) + "_" + Text(row["mapid"]) + "_" + Text(row["cellid"]) + ".xml";
                    ValidateFilename(filename);
                    return record;
                case "Zaaps":
                    id = row["mapID"];
                    record.Add(Element("MAP", id), Element("CELLULE", row["cellID"]));
                    break;
                case "Monstres":
                    id = row["id"];
                    record.Add(Element("ID", id), Element("NAME", row["name"]), Element("GFX", row["gfxID"]));
                    break;
                case "Sorts":
                    id = row["id"];
                    var spell = new XElement("SORT", new XAttribute("ID", Text(id)), Element("NOM", row["nom"]));
                    for (int level = 1; level <= 6; level++)
                    {
                        string data = Text(row["lvl" + level]);
                        if (!string.IsNullOrWhiteSpace(data) && data != "-1") spell.Add(SpellLevel(data, level));
                    }
                    record = new XElement("SORTS", spell);
                    break;
                default: throw new NotSupportedException("Ce type de ressource ne possède pas de format XML pour le bot.");
            }
            filename = Text(id) + ".xml";
            ValidateFilename(filename);
            return record;
        }

        private static void ValidateFilename(string filename)
        {
            if (Path.GetFileName(filename) != filename || filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidDataException("L'identifiant de ressource ne peut pas être utilisé comme nom de fichier.");
        }

        private static XElement SpellLevel(string data, int level)
        {
            string[] fields = data.Split(',').Select(value => value.Trim()).ToArray();
            if (fields.Length != 19 && fields.Length != 20)
                throw new FormatException($"Le niveau {level} du sort utilise un format inconnu ({fields.Length} champs).");
            int zoneIndex = fields.Length - 5;
            var node = new XElement("NIVEAU", new XAttribute("NIVEAU", level),
                Number("PA", fields[2]), Number("MIN_RANGE", fields[3]), Number("MAX_RANGE", fields[4]),
                Flag("LIGNE", fields[7]), Flag("LIGNE_DE_VUE", fields[8]), Flag("NEED_EMPTY_CELL", fields[9]),
                Flag("MODIF", fields[10]), Number("PER_TURN", fields[11]), Number("PER_OBJECTIVE", fields[12]),
                Number("INTERVAL", fields[13]));
            int effectIndex = 0;
            AddEffects(node, fields[0], false, fields[zoneIndex], ref effectIndex);
            AddEffects(node, fields[1], true, fields[zoneIndex], ref effectIndex);
            return node;
        }

        private static XAttribute Number(string name, string value)
        {
            if (!byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte number))
                throw new FormatException("La valeur " + name + " du sort est invalide : " + value);
            return new XAttribute(name, number);
        }
        private static XAttribute Flag(string name, string value)
        {
            if (value == "1") value = "true";
            if (value == "0") value = "false";
            if (!bool.TryParse(value, out bool flag)) throw new FormatException("La valeur " + name + " du sort est invalide.");
            return new XAttribute(name, flag);
        }
        private static void AddEffects(XElement parent, string data, bool critical, string zones, ref int index)
        {
            if (string.IsNullOrWhiteSpace(data) || data == "-1") return;
            foreach (string encoded in data.Split('|'))
            {
                string[] effect = encoded.Split(';');
                if (effect.Length < 5 || !int.TryParse(effect[0], out int id))
                    throw new FormatException("Un effet de sort est incomplet.");
                string zone;
                if (zones.Length == 2) zone = zones;
                else if (zones.Length >= (index + 1) * 2) zone = zones.Substring(index * 2, 2);
                else throw new FormatException("La zone d'un effet de sort est absente.");
                parent.Add(new XElement("EFFETS", new XAttribute("TYPE", id), new XAttribute("COOLDOWN", effect[4]),
                    new XAttribute("BUT", effect[1]), new XAttribute("ZONE", zone), new XAttribute("CRITIQUE", critical)));
                index++;
            }
        }
    }
}
