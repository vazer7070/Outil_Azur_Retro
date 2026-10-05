using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using Tool_BotProtocol.Game.Perso.Inventory;

namespace Tool_BotProtocol.Game.Perso.Spells
{
    public class Spell
    {
        public short ID { get; set; }
        public string Name { get; set; }
        public byte Level { get; set; }
        public string Position { get; set; }
        public bool HasMetadata => Stats.Count > 0;
        public Dictionary<byte, SpellStats> Stats;
        static string SpellsPath = Path.Combine(".", "ressources", "Bot", "BotSorts");

        public static Dictionary<short, Spell>AllSpells = new Dictionary<short, Spell>();

        public Spell(short id, string name) : this(id, name, true) { }

        private Spell(short id, string name, bool register)
        {
            ID = id;
            Name = name;
            Stats = new Dictionary<byte, SpellStats>();

            if (register) AllSpells.Add(id, this);
        }

        public void GetSpellsStats(byte level, SpellStats spellStats)
        {
            if(Stats.ContainsKey(level))
                Stats.Remove(level);
            Stats.Add(level, spellStats);
        }
        public SpellStats GetStats()
        {
            SpellStats value;
            return Stats.TryGetValue(Level, out value) ? value : null;
        }
        public static Spell ForCharacter(short id, byte level, string position = null)
        {
            Spell template = getSpell(id);
            return template == null ? new Spell(id, "Sort #" + id, false) { Level = level, Position = position }
                : template.CopyForCharacter(level, position);
        }
        public Spell CopyForCharacter(byte level, string position = null)
        {
            var copy = new Spell(ID, Name, false) { Level = level, Position = position ?? Position };
            foreach (var entry in Stats)
            {
                SpellStats source = entry.Value;
                var statistics = new SpellStats
                {
                    PA = source.PA, Min_portee = source.Min_portee, Max_portee = source.Max_portee,
                    IsInLine = source.IsInLine, AvecLigneDeVue = source.AvecLigneDeVue,
                    EmptyCell = source.EmptyCell, portee_modifiable = source.portee_modifiable,
                    PerTurn = source.PerTurn, PerObjective = source.PerObjective, Interval = source.Interval
                };
                foreach (var effect in source.NormalEffect) statistics.NormalEffect.Add(CopyEffect(effect));
                foreach (var effect in source.CriticalEffect) statistics.CriticalEffect.Add(CopyEffect(effect));
                copy.Stats.Add(entry.Key, statistics);
            }
            return copy;
        }
        private static SpellEffect CopyEffect(SpellEffect effect) => new SpellEffect(effect.Id,
            effect.ZoneEffet == null ? null : new Zones(effect.ZoneEffet.Type, effect.ZoneEffet.taille));
        public static Spell getSpell(short id)
        {
            Spell value;
            return AllSpells.TryGetValue(id, out value) ? value : null;
        }
        public static void LoadAllSpells() => LoadAllSpells(SpellsPath);

        public static void LoadAllSpells(string directory)
        {
            try
            {
                var loaded = new Dictionary<short, Spell>();
                if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("Le dossier XML des sorts est introuvable : " + directory);
                DirectoryInfo MapFolder = new DirectoryInfo(directory);
                var errors = new List<string>();
                foreach (FileInfo f in MapFolder.GetFiles("*.xml").OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        XElement root = XElement.Load(f.FullName, LoadOptions.PreserveWhitespace);
                        foreach (XElement map in root.Descendants("SORT"))
                        {
                            XAttribute idAttribute = map.Attribute("ID"); short id;
                            if (idAttribute == null || !short.TryParse(idAttribute.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
                                continue;
                            string name = ((string)map.Element("NOM") ?? (string)root.Element("NOM") ?? "Sort #" + id).Trim();
                            Spell spell = new Spell(id, string.IsNullOrEmpty(name) ? "Sort #" + id : name, false);
                            AddLevels(spell, map, root);
                            loaded[id] = spell;
                        }
                    }
                    catch (Exception error)
                    {
                        errors.Add(f.Name + " : " + error.Message);
                    }
                }
                if (loaded.Count == 0 && errors.Count > 0) throw new InvalidDataException(string.Join(" | ", errors.Take(3)));
                AllSpells = loaded;
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("Chargement des sorts impossible : " + ex.Message, ex);
            }
        }

        private static void AddLevels(Spell spell, XElement sort, XElement root)
        {
            XElement[] nested = sort.Elements("NIVEAU").ToArray();
            if (nested.Length > 0)
            {
                foreach (XElement node in nested) AddLevel(spell, node, node.Elements("EFFETS"));
                return;
            }
            // Early Azur exports wrote SORT, NOM, NIVEAU and EFFETS as siblings.
            // Keep them readable so existing resource packs remain useful.
            XElement current = null;
            foreach (XElement node in root.Elements())
            {
                if (node.Name.LocalName == "NIVEAU")
                {
                    current = node;
                    AddLevel(spell, node, Enumerable.Empty<XElement>());
                }
                else if (node.Name.LocalName == "EFFETS" && current != null)
                {
                    byte level;
                    if (!ByteAttribute(current, "NIVEAU", out level)) continue;
                    SpellStats stats;
                    if (!spell.Stats.TryGetValue(level, out stats)) continue;
                    AddEffect(stats, node, stats.NormalEffect.Count + stats.CriticalEffect.Count);
                }
            }
        }

        private static void AddLevel(Spell spell, XElement node, IEnumerable<XElement> effects)
        {
            byte level, value;
            if (!ByteAttribute(node, "NIVEAU", out level)) return;
            var stats = new SpellStats
            {
                PA = ByteAttribute(node, "PA", out value) ? value : (byte)0,
                Min_portee = ByteAttribute(node, "MIN_RANGE", out value) ? value : (byte)0,
                Max_portee = ByteAttribute(node, "MAX_RANGE", out value) ? value : (byte)0,
                IsInLine = BoolAttribute(node, "LIGNE"),
                AvecLigneDeVue = BoolAttribute(node, "LIGNE_DE_VUE"),
                EmptyCell = BoolAttribute(node, "NEED_EMPTY_CELL"),
                portee_modifiable = BoolAttribute(node, "MODIF"),
                PerTurn = ByteAttribute(node, "PER_TURN", out value) ? value : (byte)0,
                PerObjective = ByteAttribute(node, "PER_OBJECTIVE", out value) ? value : (byte)0,
                Interval = ByteAttribute(node, "INTERVAL", out value) ? value : (byte)0
            };
            foreach (XElement effect in effects) AddEffect(stats, effect, stats.NormalEffect.Count + stats.CriticalEffect.Count);
            spell.GetSpellsStats(level, stats);
        }

        private static void AddEffect(SpellStats stats, XElement effect, int index)
        {
            int id; XAttribute type = effect.Attribute("TYPE");
            if (type == null || !int.TryParse(type.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) return;
            bool critical = BoolAttribute(effect, "CRITIQUE");
            string encodedZone = ((string)effect.Attribute("ZONE") ?? "P0").Trim();
            if (encodedZone.Length < 2 || encodedZone == "-1") encodedZone = "P0";
            int offset = encodedZone.Length >= 2 * (index + 1) ? index * 2 : 0;
            string zone = encodedZone.Substring(offset, 2);
            try { stats.AddEffect(new SpellEffect(id, Zones.Parse(zone)), critical); }
            catch (Exception) { stats.AddEffect(new SpellEffect(id, new Zones(SpellActionZone.SOLO, 0)), critical); }
        }

        private static bool ByteAttribute(XElement node, string name, out byte value)
        {
            XAttribute attribute = node.Attribute(name);
            value = 0;
            return attribute != null && byte.TryParse(attribute.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
        private static bool BoolAttribute(XElement node, string name)
        {
            XAttribute attribute = node.Attribute(name); if (attribute == null) return false;
            string value = attribute.Value.Trim(); bool result;
            if (value == "1") return true; if (value == "0") return false;
            return bool.TryParse(value, out result) && result;
        }
    }
}
