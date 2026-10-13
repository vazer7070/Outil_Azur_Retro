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
using Tools_protocol.Kryone.Database;
using Tools_protocol.Managers;
using Tools_protocol.Query;
using Tools_protocol.Emulators;

namespace Tools_protocol.Parser.XML
{
    /// <summary>Carte dont l'export du bot cherche le fond (<c>backgroundNum</c>) hors de la base.</summary>
    public sealed class MapBackgroundRequest
    {
        public MapBackgroundRequest(int mapId, string date, string key, string mapData, int width, int height)
        {
            MapId = mapId; Date = date ?? ""; Key = key ?? ""; MapData = mapData ?? ""; Width = width; Height = height;
        }

        public int MapId { get; }
        /// <summary>Colonne <c>date</c> de la table des cartes : partie du nom de fichier du client (<c>&lt;id&gt;_&lt;date&gt;[X].swf</c>).</summary>
        public string Date { get; }
        /// <summary>Clé hexadécimale de la carte (celle de <c>GDM|id|date|clé</c>) ; vide si le client lit une carte non chiffrée.</summary>
        public string Key { get; }
        /// <summary>Cellules en clair de la base, pour vérifier que le fichier du client décrit la même carte.</summary>
        public string MapData { get; }
        public int Width { get; }
        public int Height { get; }
        /// <summary>Renseigné par le lecteur quand le fond n'a pas pu être lu ou vérifié.</summary>
        public string Warning { get; set; }
    }

    /// <summary>
    /// Données annexes d'un export de ressources du bot : règles de compétences de l'émulateur, compétences des
    /// ateliers lues dans la table des métiers, lecteur du fond des cartes et avertissements.
    /// </summary>
    public sealed class BotExportContext
    {
        private const int StoredWarnings = 200;
        private readonly object sync = new object();
        private readonly List<string> warnings = new List<string>();
        private readonly Dictionary<int, SortedSet<int>> workshopSkills = new Dictionary<int, SortedSet<int>>();
        private readonly Dictionary<int, SortedSet<int>> workshopJobs = new Dictionary<int, SortedSet<int>>();

        public BotExportContext() : this(null) { }

        public BotExportContext(IEnumerable<InteractiveSkillRule> rules)
        {
            Rules = (rules ?? Enumerable.Empty<InteractiveSkillRule>()).Where(rule => rule != null).ToArray();
        }

        /// <summary>Contexte qui reprend les règles de l'émulateur sélectionné.</summary>
        public static BotExportContext ForCurrentEmulator() => new BotExportContext(EmulatorRegistry.Current.InteractiveSkillRules);

        public IReadOnlyList<InteractiveSkillRule> Rules { get; }

        /// <summary>
        /// Lit le fond d'une carte quand la table n'a pas de colonne <c>background</c> (cas de StarLoco).
        /// Sans lecteur, <c>BACK</c> vaut 0 et un avertissement est ajouté.
        /// </summary>
        public Func<MapBackgroundRequest, int> MapBackground { get; set; }

        /// <summary>Premiers avertissements (au plus 200) ; <see cref="WarningCount"/> donne le total.</summary>
        public IReadOnlyList<string> Warnings { get { lock (sync) return warnings.ToArray(); } }
        public int WarningCount { get; private set; }

        public void Warn(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            lock (sync)
            {
                WarningCount++;
                if (warnings.Count < StoredWarnings) warnings.Add(message);
            }
        }

        /// <summary>
        /// Lit la colonne <c>skills</c> de la table des métiers (<c>gfx,gfx;compétence,compétence|…</c>, format de
        /// <c>Job</c> chez StarLoco) : ce sont les ateliers. Une entrée illisible est ignorée avec un avertissement,
        /// comme le serveur qui l'ignore. Renvoie le nombre d'ateliers distincts.
        /// </summary>
        public int AddJobSkills(IDataReader jobs)
        {
            if (jobs == null) throw new ArgumentNullException(nameof(jobs));
            int idColumn = XmlParser.Ordinal(jobs, "id"), skillsColumn = XmlParser.Ordinal(jobs, "skills");
            if (idColumn < 0 || skillsColumn < 0)
            {
                Warn("La table des métiers n'a pas de colonnes id/skills : seules les règles de l'émulateur décrivent les ateliers.");
                return workshopSkills.Count;
            }
            while (jobs.Read())
            {
                string job = jobs.IsDBNull(idColumn) ? "" : XmlParser.Text(jobs.GetValue(idColumn));
                if (jobs.IsDBNull(skillsColumn)) continue;
                string data = XmlParser.Text(jobs.GetValue(skillsColumn)).Trim();
                if (data.Length == 0) continue;
                int jobId;
                bool knownJob = int.TryParse(job, NumberStyles.Integer, CultureInfo.InvariantCulture, out jobId);
                foreach (string entry in data.Split('|'))
                {
                    string[] parts = entry.Split(';');
                    int[] gfx, skills;
                    if (parts.Length != 2 || !Numbers(parts[0], out gfx) || !Numbers(parts[1], out skills))
                    {
                        Warn("Métier " + job + " : entrée de compétences illisible « " + entry + " ».");
                        continue;
                    }
                    foreach (int io in gfx)
                    {
                        Set(workshopSkills, io).UnionWith(skills);
                        if (knownJob) Set(workshopJobs, io).Add(jobId);
                    }
                }
            }
            return workshopSkills.Count;
        }

        internal IEnumerable<int> KnownGfx()
        {
            var known = new SortedSet<int>(workshopSkills.Keys);
            foreach (InteractiveSkillRule rule in Rules)
                for (int gfx = rule.FirstGfx; gfx <= rule.LastGfx; gfx++) known.Add(gfx);
            return known;
        }

        internal SortedSet<int> SkillsFor(int gfx)
        {
            var skills = new SortedSet<int>(Rules.Where(rule => rule.Matches(gfx)).Select(rule => rule.Skill));
            SortedSet<int> workshop;
            if (workshopSkills.TryGetValue(gfx, out workshop)) skills.UnionWith(workshop);
            return skills;
        }

        internal SortedSet<int> JobsFor(int gfx)
        {
            SortedSet<int> jobs;
            return workshopJobs.TryGetValue(gfx, out jobs) ? new SortedSet<int>(jobs) : new SortedSet<int>();
        }

        /// <summary>Catégorie du gfx ; une compétence de zaap l'emporte sur une compétence de maison, etc.</summary>
        internal InteractiveKind KindFor(int gfx)
        {
            var kinds = new HashSet<InteractiveKind>(Rules.Where(rule => rule.Matches(gfx)).Select(rule => rule.Kind));
            foreach (InteractiveKind kind in new[] { InteractiveKind.Zaap, InteractiveKind.Zaapi, InteractiveKind.MountPark,
                InteractiveKind.House, InteractiveKind.Chest, InteractiveKind.Harvest, InteractiveKind.Workshop })
                if (kinds.Contains(kind)) return kind;
            return workshopSkills.ContainsKey(gfx) ? InteractiveKind.Workshop : InteractiveKind.Other;
        }

        private static SortedSet<int> Set(Dictionary<int, SortedSet<int>> map, int key)
        {
            SortedSet<int> set;
            if (!map.TryGetValue(key, out set)) map[key] = set = new SortedSet<int>();
            return set;
        }

        private static bool Numbers(string text, out int[] values)
        {
            var list = new List<int>();
            foreach (string part in text.Split(','))
            {
                int value;
                if (!int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value <= 0)
                {
                    values = null;
                    return false;
                }
                list.Add(value);
            }
            values = list.ToArray();
            return values.Length > 0;
        }
    }

    public static class XmlParser
    {
        private static readonly Dictionary<string, string> Tables = new Dictionary<string, string>
        {
            { "Maps", "cartes" }, { "Objets", "Template" }, { "Sorts", "sort" },
            { "Panoplies", "panoplies" }, { "Joueurs", "perso" }, { "Métiers", "metiers" },
            { "Maisons", "maisons" }, { "Zaaps", "zaaps" }, { "PNJs", "npcs" }, { "Monstres", "monstres" },
            { "Interactifs", "interactions" }, { "Déclencheurs", "cellule" }, { "Zaapis", "zaapi" }
        };

        public static Task<int> ParseSQLToXML(string path, string type, bool ForBot = false) => ParseSQLToXML(path, type, ForBot, null);

        /// <summary>
        /// Exporte une table en XML. Pour le bot, <paramref name="context"/> porte les règles de l'émulateur, le lecteur
        /// du fond des cartes et reçoit les avertissements ; sans contexte, celui de l'émulateur sélectionné est utilisé.
        /// </summary>
        public static Task<int> ParseSQLToXML(string path, string type, bool forBot, BotExportContext context)
        {
            if (!EmulatorRegistry.Current.Supports(EmulatorFeature.BotResourceExport))
                throw new NotSupportedException("L'export de ressources n'est pas disponible pour l'émulateur " + EmulatorRegistry.Current.DisplayName + ".");
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Choisissez un dossier de sortie.", nameof(path));
            if (type == null || !Tables.TryGetValue(type, out string key))
                throw new ArgumentException("Ce type de ressource n'est pas pris en charge.", nameof(type));
            if (forBot && (type == "Joueurs" || type == "Maisons"))
                throw new NotSupportedException("Le bot n'utilise pas ce type de ressource XML.");
            // La table et sa base (auth ou world) viennent du profil de l'émulateur.
            string table = EmulatorRegistry.Current.Table(key);
            string query = QueryBuilder.SelectFromQuery(new[] { "*" }, table, "", "");
            string connectionString = EmulatorRegistry.ConnectionFor(key);
            if (string.IsNullOrWhiteSpace(connectionString)) throw new InvalidOperationException("La base de données n'est pas connectée.");
            string folder = Path.GetFullPath(path);
            if (forBot && context == null) context = BotExportContext.ForCurrentEmulator();
            // Les ateliers (compétences par gfx) sont décrits par la table des métiers.
            string jobsQuery = null, jobsConnection = null;
            if (forBot && type == "Interactifs" && EmulatorRegistry.Current.KnowsTable("metiers"))
            {
                string jobs = EmulatorRegistry.Current.Table("metiers");
                jobsConnection = EmulatorRegistry.ConnectionFor("metiers");
                if (!string.IsNullOrWhiteSpace(jobs) && !string.IsNullOrWhiteSpace(jobsConnection))
                    jobsQuery = QueryBuilder.SelectFromQuery(new[] { "*" }, jobs, "", "");
            }
            return Task.Run(() => Export(folder, type, forBot, table, query, connectionString, context, jobsQuery, jobsConnection));
        }

        private static int Export(string folder, string type, bool forBot, string table, string query, string connectionString,
            BotExportContext context, string jobsQuery, string jobsConnection)
        {
            if (forBot && type == "PNJs")
            {
                if (EmulatorRegistry.Current.Locate("npc_template") != EmulatorRegistry.Current.Locate("npcs"))
                    throw new NotSupportedException("Les PNJ et leurs modèles doivent être dans la même base pour l'export du bot.");
                string templates = EmulatorRegistry.Current.Table("npc_template");
                QueryBuilder.SelectFromQuery(new[] { "*" }, templates, "", "");
                query = $"SELECT n.*, t.gfxID AS azur_gfx, t.sex AS azur_sex FROM `{table}` n " +
                    $"LEFT JOIN `{templates}` t ON t.id=n.npcid";
            }
            if (jobsQuery != null)
            {
                try
                {
                    using (var connection = new MySqlConnection(jobsConnection))
                    using (var command = new MySqlCommand(jobsQuery, connection))
                    {
                        connection.Open();
                        using (var reader = command.ExecuteReader()) context.AddJobSkills(reader);
                    }
                }
                catch (MySqlException error) { context.Warn("Compétences des ateliers non lues dans la table des métiers : " + error.Message); }
            }
            using (var connection = new MySqlConnection(connectionString))
            using (var command = new MySqlCommand(query, connection))
            {
                connection.Open();
                using (var reader = command.ExecuteReader())
                {
                    if (forBot) return ExportBotRecords(folder, type, reader, context);
                    return Staged(folder, staging =>
                    {
                        int count = 0;
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
                        return count;
                    });
                }
            }
        }

        /// <summary>
        /// Écrit les ressources du bot d'un type à partir de lignes déjà lues (base, table en mémoire…). Les fichiers
        /// existants ne sont remplacés qu'une fois toute la conversion réussie. Renvoie le nombre de fichiers écrits.
        /// </summary>
        public static int ExportBotRecords(string folder, string type, IDataReader rows, BotExportContext context)
        {
            if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("Choisissez un dossier de sortie.", nameof(folder));
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (context == null) context = new BotExportContext();
            if (type == null || !Tables.ContainsKey(type) || type == "Joueurs" || type == "Maisons")
                throw new NotSupportedException("Le bot n'utilise pas ce type de ressource XML.");
            int missingBackgrounds = 0;
            int written = Staged(Path.GetFullPath(folder), staging =>
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                Action<XElement, string> save = (record, name) =>
                {
                    ValidateFilename(name);
                    if (!names.Add(name)) throw new InvalidDataException("Deux ressources produisent le même nom de fichier : " + name);
                    record.Save(Path.Combine(staging, name));
                };
                if (type == "Interactifs") return WriteInteractives(rows, context, save);
                int count = 0;
                while (rows.Read())
                {
                    string name;
                    bool backgroundMissing;
                    XElement record = BotRecord(type, rows, context, out name, out backgroundMissing);
                    if (backgroundMissing) missingBackgrounds++;
                    save(record, name);
                    count++;
                }
                return count;
            });
            if (missingBackgrounds > 0)
                context.Warn(missingBackgrounds + " carte(s) exportée(s) avec BACK = 0 : la table n'a pas de colonne background. "
                    + "Indiquez le dossier du client pour lire backgroundNum dans data/maps.");
            return written;
        }

        private static int Staged(string folder, Func<string, int> write)
        {
            Directory.CreateDirectory(folder);
            string staging = Path.Combine(folder, ".azur-export-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                int count = write(staging);
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

        internal static string Text(object value)
        {
            if (value is byte[] bytes) return Convert.ToBase64String(bytes);
            if (value is DateTime date) return date.ToString("o", CultureInfo.InvariantCulture);
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        }
        private static XElement Element(string name, object value) => new XElement(name, Text(value));

        /// <summary>Position d'une colonne sans tenir compte de la casse, ou -1.</summary>
        internal static int Ordinal(IDataRecord row, string column)
        {
            for (int i = 0; i < row.FieldCount; i++)
                if (row.GetName(i).Equals(column, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /// <summary>Valeur d'une colonne facultative (StarLoco n'a pas de fond de carte « background »).</summary>
        private static object Optional(IDataRecord row, string column, object fallback)
        {
            int index = Ordinal(row, column);
            return index < 0 || row.IsDBNull(index) ? fallback : row.GetValue(index);
        }

        private static object Required(IDataRecord row, string column)
        {
            int index = Ordinal(row, column);
            if (index < 0) throw new InvalidDataException("La colonne " + column + " est absente de la table exportée.");
            return row.IsDBNull(index) ? "" : row.GetValue(index);
        }

        private static int Integer(IDataRecord row, string column)
        {
            string text = Text(Required(row, column)).Trim();
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                throw new FormatException("La valeur " + column + " n'est pas un entier : « " + text + " ».");
            return value;
        }

        private static XElement BotRecord(string type, IDataRecord row, BotExportContext context, out string filename, out bool backgroundMissing)
        {
            var record = new XElement("RECORD");
            object id;
            backgroundMissing = false;
            switch (type)
            {
                case "Maps":
                    id = row["id"];
                    string[] position = Text(row["mappos"]).Split(',');
                    if (position.Length < 2 || !int.TryParse(position[0], out _) || !int.TryParse(position[1], out _))
                        throw new FormatException("La position de la carte " + id + " est invalide.");
                    record.Add(Element("ID", id), Element("LARGEUR", row["width"]), Element("LONGUEUR", row["heigth"]),
                        Element("X", position[0]), Element("Y", position[1]), Element("MAP_DATA", row["mapData"]),
                        Element("BACK", MapBackground(row, context, out backgroundMissing)));
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
                    return record;
                case "Zaaps":
                    id = row["mapID"];
                    record.Add(Element("MAP", id), Element("CELLULE", row["cellID"]));
                    break;
                case "Monstres":
                    id = row["id"];
                    record.Add(Element("ID", id), Element("NAME", row["name"]), Element("GFX", row["gfxID"]));
                    // Colonne capturable (Kryone et StarLoco) : capture des âmes affichée au survol des groupes (lot F14).
                    object capturable = Optional(row, "capturable", null);
                    if (capturable != null)
                    {
                        // Comme MonsterData.java : capturable seulement si la colonne vaut 1.
                        string flag = Text(capturable).Trim();
                        record.Add(Element("CAPTURABLE", flag == "1" || flag.Equals("true", StringComparison.OrdinalIgnoreCase) ? 1 : 0));
                    }
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
                case "Déclencheurs":
                    // scripted_cells : le serveur n'applique que l'événement 1 (arrêt sur la cellule) ; l'action 0 téléporte vers « carte,cellule ».
                    int map = Integer(row, "MapID"), cell = Integer(row, "CellID");
                    record.Add(Element("MAP", map), Element("CELLULE", cell), Element("ACTION", Integer(row, "ActionID")),
                        Element("EVENEMENT", Integer(row, "EventID")), Element("ARGUMENTS", Required(row, "ActionsArgs")),
                        Element("CONDITIONS", Required(row, "Conditions")));
                    filename = map + "_" + cell + ".xml";
                    return record;
                case "Zaapis":
                    // zaapi : 1 Bonta, 2 Brâkmar ; StarLoco range toute autre valeur dans la liste neutre.
                    int zaapi = Integer(row, "mapid");
                    id = zaapi;
                    record.Add(Element("MAP", zaapi), Element("ALIGNEMENT", Integer(row, "align")));
                    break;
                case "Panoplies":
                    int set = Integer(row, "ID");
                    id = set;
                    record.Add(Element("ID", set), Element("NOM", Required(row, "name")));
                    ItemSet(record, set, Text(Required(row, "items")), Text(Required(row, "bonus")), context);
                    break;
                default: throw new NotSupportedException("Ce type de ressource ne possède pas de format XML pour le bot.");
            }
            filename = Text(id) + ".xml";
            return record;
        }

        /// <summary>
        /// Fond de la carte : colonne <c>background</c> quand la table l'a (Kryone), sinon lecteur du client
        /// (StarLoco n'a pas cette colonne), sinon 0.
        /// </summary>
        private static int MapBackground(IDataRecord row, BotExportContext context, out bool missing)
        {
            missing = false;
            int column = Ordinal(row, "background");
            if (column >= 0)
            {
                if (row.IsDBNull(column)) return 0;
                string text = Text(row.GetValue(column)).Trim();
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int stored) || stored < 0)
                    throw new FormatException("Le fond de la carte " + Text(row["id"]) + " est invalide : « " + text + " ».");
                return stored;
            }
            if (context.MapBackground == null) { missing = true; return 0; }
            var request = new MapBackgroundRequest(Integer(row, "id"), Text(Optional(row, "date", "")), Text(Optional(row, "key", "")),
                Text(Optional(row, "mapData", "")), Integer(row, "width"), Integer(row, "heigth"));
            int background;
            try { background = context.MapBackground(request); }
            catch (Exception error) when (error is IOException || error is InvalidDataException || error is FormatException
                || error is UnauthorizedAccessException || error is ArgumentException || error is NotSupportedException)
            {
                request.Warning = error.Message;
                background = 0;
            }
            if (!string.IsNullOrEmpty(request.Warning)) context.Warn("Carte " + request.MapId + " : " + request.Warning);
            return Math.Max(0, background);
        }

        /// <summary>
        /// Panoplie au format de <c>ObjectSet</c> (StarLoco) : <c>items</c> = modèles séparés par des virgules,
        /// <c>bonus</c> = groupes séparés par « ; » dont le premier vaut pour 2 objets portés, chaque groupe listant
        /// <c>effet:valeur</c> séparés par des virgules. Une entrée illisible est ignorée comme par le serveur.
        /// </summary>
        private static void ItemSet(XElement record, int set, string items, string bonus, BotExportContext context)
        {
            var templates = new List<string>();
            foreach (string item in items.Split(','))
            {
                string trimmed = item.Trim();
                if (trimmed.Length == 0) continue;
                if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int template) && template > 0)
                    templates.Add(template.ToString(CultureInfo.InvariantCulture));
                else context.Warn("Panoplie " + set + " : objet illisible « " + trimmed + " » ignoré.");
            }
            record.Add(new XElement("OBJETS", string.Join(",", templates)));
            string[] groups = bonus.Split(';');
            for (int index = 0; index < groups.Length; index++)
            {
                var group = new XElement("BONUS", new XAttribute("OBJETS", index + 2));
                foreach (string effect in groups[index].Split(','))
                {
                    string trimmed = effect.Trim();
                    if (trimmed.Length == 0) continue;
                    string[] parts = trimmed.Split(':');
                    if (parts.Length == 2 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int stat)
                        && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                        group.Add(new XElement("EFFET", new XAttribute("STAT", stat), new XAttribute("VALEUR", value)));
                    else context.Warn("Panoplie " + set + " : bonus illisible « " + trimmed + " » ignoré.");
                }
                if (group.HasElements) record.Add(group);
            }
        }

        /// <summary>
        /// Objets interactifs : une fiche par gfx de <c>interactive_objects_data</c> (l'identifiant de cette table est le
        /// gfx de la couche objet 2 de la cellule), puis une fiche pour chaque gfx que seules les règles de l'émulateur ou
        /// la table des métiers connaissent (le serveur accepte leurs compétences sans modèle : non marchable, durée 1500 ms).
        /// </summary>
        private static int WriteInteractives(IDataReader rows, BotExportContext context, Action<XElement, string> save)
        {
            var records = new SortedDictionary<int, XElement>();
            while (rows.Read())
            {
                int gfx = Integer(rows, "id");
                if (gfx <= 0 || gfx > short.MaxValue) throw new InvalidDataException("Identifiant d'objet interactif invalide : " + gfx + ".");
                if (records.ContainsKey(gfx)) throw new InvalidDataException("L'objet interactif " + gfx + " est défini deux fois.");
                records[gfx] = Interactive(gfx, Text(Optional(rows, "Name IO", "")), Integer(rows, "walkable") == 1,
                    Integer(rows, "respawn"), Integer(rows, "duration"), "table", context);
            }
            foreach (int gfx in context.KnownGfx())
                if (!records.ContainsKey(gfx)) records[gfx] = Interactive(gfx, "", false, 0, 1500, "serveur", context);
            foreach (var pair in records) save(pair.Value, pair.Key.ToString(CultureInfo.InvariantCulture) + ".xml");
            return records.Count;
        }

        private static XElement Interactive(int gfx, string name, bool walkable, int respawn, int duration, string source, BotExportContext context)
        {
            return new XElement("RECORD", Element("ID", gfx), Element("GFX", gfx), Element("NOM", name),
                Element("TYPE", (int)context.KindFor(gfx)), Element("COMPETENCES", string.Join(",", context.SkillsFor(gfx))),
                Element("METIERS", string.Join(",", context.JobsFor(gfx))), Element("MARCHABLE", walkable ? 1 : 0),
                Element("REAPPARITION", respawn), Element("DUREE", duration), Element("SOURCE", source));
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
