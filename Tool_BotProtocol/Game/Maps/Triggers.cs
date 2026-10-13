using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Tool_BotProtocol.Game.Maps
{
    /// <summary>
    /// Cellule scriptée du serveur (<c>scripted_cells</c>) : action appliquée quand un personnage s'arrête sur la cellule.
    /// L'action 0 téléporte vers <c>carte,cellule</c> (<c>Action.apply</c> chez StarLoco) ; la sortie n'est connue que par
    /// cette table, la carte du client ne la décrit pas.
    /// </summary>
    public sealed class Trigger
    {
        /// <summary>Événement « arrêt sur la cellule », le seul que StarLoco charge (<c>ScriptedCellData</c>).</summary>
        public const int StopOnCellEvent = 1;
        /// <summary>Action de téléportation (<c>carte,cellule</c>).</summary>
        public const int TeleportAction = 0;

        public int MapId { get; private set; }
        public int CellId { get; private set; }
        public int ActionId { get; private set; }
        public int EventId { get; private set; }
        public string Arguments { get; private set; } = "";
        /// <summary>Conditions du serveur (texte brut, évaluées par le serveur seulement).</summary>
        public string Conditions { get; private set; } = "";
        /// <summary>Carte d'arrivée, ou -1 si l'action n'est pas une téléportation lisible.</summary>
        public int TargetMapId { get; private set; } = -1;
        public int TargetCellId { get; private set; } = -1;
        public bool IsTeleport => TargetMapId > 0 && TargetCellId >= 0;

        /// <summary>Lit une fiche <c>RECORD</c> ; lève <see cref="FormatException"/> si elle est illisible.</summary>
        public static Trigger Parse(XElement record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            var trigger = new Trigger
            {
                MapId = Number(record, "MAP"),
                CellId = Number(record, "CELLULE"),
                ActionId = Number(record, "ACTION"),
                EventId = Number(record, "EVENEMENT"),
                Arguments = (string)record.Element("ARGUMENTS") ?? "",
                Conditions = ((string)record.Element("CONDITIONS") ?? "").Trim(),
            };
            if (trigger.MapId <= 0 || trigger.CellId < 0) throw new FormatException("Carte ou cellule invalide.");
            if (trigger.ActionId == TeleportAction)
            {
                // Même lecture que le serveur : split(",", 2), Short.parseShort puis Integer.parseInt, sans espaces tolérés.
                string[] target = trigger.Arguments.Split(new[] { ',' }, 2);
                short map;
                int cell;
                if (target.Length == 2 && short.TryParse(target[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out map)
                    && int.TryParse(target[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out cell) && map > 0 && cell >= 0)
                {
                    trigger.TargetMapId = map;
                    trigger.TargetCellId = cell;
                }
            }
            return trigger;
        }

        private static int Number(XElement record, string name)
        {
            string text = ((string)record.Element(name) ?? "").Trim();
            int value;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                throw new FormatException("Élément " + name + " absent ou invalide : « " + text + " ».");
            return value;
        }
    }

    /// <summary>Cellules déclencheurs exportées dans <c>ressources/Bot/BotTriggers</c>, par carte puis par cellule.</summary>
    public static class Triggers
    {
        public static readonly IReadOnlyDictionary<int, Trigger> None = new Dictionary<int, Trigger>();
        private static volatile Dictionary<int, IReadOnlyDictionary<int, Trigger>> byMap = new Dictionary<int, IReadOnlyDictionary<int, Trigger>>();

        public static string DefaultPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ressources", "Bot", "BotTriggers");
        public static string[] LoadWarnings { get; private set; } = new string[0];
        public static int Count { get; private set; }

        /// <summary>Déclencheurs actifs d'une carte (événement 1), jamais null.</summary>
        public static IReadOnlyDictionary<int, Trigger> ForMap(int mapId)
        {
            IReadOnlyDictionary<int, Trigger> triggers;
            return byMap.TryGetValue(mapId, out triggers) ? triggers : None;
        }

        public static Task LoadAllTriggersAsync() => LoadAllTriggersAsync(DefaultPath);

        /// <summary>
        /// Charge un dossier hors du thread appelant. Les fichiers illisibles et les événements que le serveur ignore
        /// sont listés dans <see cref="LoadWarnings"/> ; un dossier absent donne une liste vide.
        /// </summary>
        public static Task LoadAllTriggersAsync(string directory)
        {
            return Task.Run(() =>
            {
                var loaded = new Dictionary<int, Dictionary<int, Trigger>>();
                var warnings = new List<string>();
                int count = 0;
                if (Directory.Exists(directory))
                {
                    foreach (string file in Directory.EnumerateFiles(directory, "*.xml"))
                    {
                        try
                        {
                            Trigger trigger = Trigger.Parse(XElement.Load(file));
                            if (trigger.EventId != Trigger.StopOnCellEvent)
                            {
                                warnings.Add(Path.GetFileName(file) + " : événement " + trigger.EventId + " ignoré par le serveur.");
                                continue;
                            }
                            Dictionary<int, Trigger> cells;
                            if (!loaded.TryGetValue(trigger.MapId, out cells)) loaded[trigger.MapId] = cells = new Dictionary<int, Trigger>();
                            if (cells.ContainsKey(trigger.CellId)) throw new FormatException("cellule " + trigger.CellId + " déjà définie.");
                            cells[trigger.CellId] = trigger;
                            count++;
                        }
                        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException
                            || error is System.Xml.XmlException || error is FormatException)
                        { warnings.Add(Path.GetFileName(file) + " : " + error.Message); }
                    }
                }
                else warnings.Add("Dossier absent : " + directory);
                var snapshot = new Dictionary<int, IReadOnlyDictionary<int, Trigger>>();
                foreach (var pair in loaded) snapshot[pair.Key] = pair.Value;
                byMap = snapshot;
                Count = count;
                LoadWarnings = warnings.ToArray();
            });
        }
    }
}
