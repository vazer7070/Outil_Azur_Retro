using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Tool_BotProtocol.Game.Perso.Inventory
{
    /// <summary>
    /// Panoplie du serveur (<c>itemsets</c>), exportée dans <c>ressources/Bot/BotItemSets/&lt;id&gt;.xml</c> : modèles
    /// d'objets et bonus par nombre d'objets portés (le premier groupe du serveur vaut pour 2 objets, comme
    /// <c>ObjectSet.getBonusStatByItemNumb</c> chez StarLoco).
    /// </summary>
    public sealed class ItemSet
    {
        private static readonly IReadOnlyDictionary<int, int> NoBonus = new Dictionary<int, int>();
        private readonly Dictionary<int, IReadOnlyDictionary<int, int>> bonuses = new Dictionary<int, IReadOnlyDictionary<int, int>>();

        public int Id { get; private set; }
        public string Name { get; private set; } = "";
        public IReadOnlyList<int> Items { get; private set; } = new int[0];
        /// <summary>Nombres d'objets portés pour lesquels un bonus est défini.</summary>
        public IEnumerable<int> BonusCounts => bonuses.Keys.OrderBy(count => count);

        /// <summary>Bonus (effet → valeur) pour <paramref name="equipped"/> objets portés ; vide s'il n'y en a pas.</summary>
        public IReadOnlyDictionary<int, int> BonusFor(int equipped)
        {
            IReadOnlyDictionary<int, int> bonus;
            return bonuses.TryGetValue(equipped, out bonus) ? bonus : NoBonus;
        }

        /// <summary>Lit une fiche <c>RECORD</c> ; lève <see cref="FormatException"/> si elle est illisible.</summary>
        public static ItemSet Parse(XElement record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            var set = new ItemSet { Id = Number((string)record.Element("ID"), "ID"), Name = ((string)record.Element("NOM") ?? "").Trim() };
            if (set.Id <= 0) throw new FormatException("Identifiant de panoplie invalide.");
            string items = ((string)record.Element("OBJETS") ?? "").Trim();
            set.Items = items.Length == 0 ? new int[0] : items.Split(',').Select(item => Number(item, "OBJETS")).ToArray();
            foreach (XElement group in record.Elements("BONUS"))
            {
                int count = Number((string)group.Attribute("OBJETS"), "BONUS/OBJETS");
                if (count < 2) throw new FormatException("Un bonus de panoplie vaut pour au moins 2 objets.");
                var effects = new Dictionary<int, int>();
                foreach (XElement effect in group.Elements("EFFET"))
                {
                    int stat = Number((string)effect.Attribute("STAT"), "EFFET/STAT");
                    int value;
                    effects.TryGetValue(stat, out value);
                    effects[stat] = value + Number((string)effect.Attribute("VALEUR"), "EFFET/VALEUR");
                }
                set.bonuses[count] = effects;
            }
            return set;
        }

        private static int Number(string text, string name)
        {
            int value;
            if (!int.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                throw new FormatException("Valeur " + name + " absente ou invalide : « " + text + " ».");
            return value;
        }
    }

    /// <summary>Panoplies chargées depuis <c>ressources/Bot/BotItemSets</c>.</summary>
    public static class ItemSets
    {
        private static volatile Dictionary<int, ItemSet> byId = new Dictionary<int, ItemSet>();

        public static string DefaultPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ressources", "Bot", "BotItemSets");
        public static string[] LoadWarnings { get; private set; } = new string[0];
        public static int Count => byId.Count;

        public static ItemSet Get(int id)
        {
            ItemSet set;
            return byId.TryGetValue(id, out set) ? set : null;
        }

        /// <summary>Panoplie qui contient un modèle d'objet, ou null.</summary>
        public static ItemSet ForItem(int templateId) => byId.Values.OrderBy(set => set.Id).FirstOrDefault(set => set.Items.Contains(templateId));

        public static Task LoadAllItemSetsAsync() => LoadAllItemSetsAsync(DefaultPath);

        /// <summary>
        /// Charge un dossier hors du thread appelant ; les fichiers illisibles sont listés dans <see cref="LoadWarnings"/>
        /// et un dossier absent donne une liste vide.
        /// </summary>
        public static Task LoadAllItemSetsAsync(string directory)
        {
            return Task.Run(() =>
            {
                var loaded = new Dictionary<int, ItemSet>();
                var warnings = new List<string>();
                if (Directory.Exists(directory))
                {
                    foreach (string file in Directory.EnumerateFiles(directory, "*.xml"))
                    {
                        try
                        {
                            ItemSet set = ItemSet.Parse(XElement.Load(file));
                            if (loaded.ContainsKey(set.Id)) throw new FormatException("panoplie " + set.Id + " déjà définie.");
                            loaded[set.Id] = set;
                        }
                        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException
                            || error is System.Xml.XmlException || error is FormatException)
                        { warnings.Add(Path.GetFileName(file) + " : " + error.Message); }
                    }
                }
                else warnings.Add("Dossier absent : " + directory);
                byId = loaded;
                LoadWarnings = warnings.ToArray();
            });
        }
    }
}
