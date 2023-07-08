using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        public Dictionary<byte, SpellStats> Stats;
        static string SpellsPath = @".\ressources\Bot\BotSorts";

        public static Dictionary<short, Spell>AllSpells = new Dictionary<short, Spell>();

        public Spell(short id, string name)
        {
            ID = id;
            Name = name;
            Stats = new Dictionary<byte, SpellStats>();

            AllSpells.Add(id, this);
        }

        public void GetSpellsStats(byte level, SpellStats spellStats)
        {
            if(Stats.ContainsKey(level))
                Stats.Remove(level);
            Stats.Add(level, spellStats);
        }
        public SpellStats GetStats() => Stats[Level];
        public static Spell getSpell(short id)
        {
            Spell value;
            return AllSpells.TryGetValue(id, out value) ? value : null;
        }
        public static void LoadAllSpells()
        {
            try
            {
                DirectoryInfo MapFolder = new DirectoryInfo($"{SpellsPath}");
                foreach (FileInfo f in MapFolder.GetFiles())
                {
                    if (f.Exists)
                    {
                        XElement.Parse(f.FullName).Descendants("SORT").ToList().ForEach(map =>
                        {
                             Spell S = new Spell(short.Parse(map.Attribute("ID").Value), map.Element("NOM").Value);

                            map.Descendants("NIVEAU").ToList().ForEach(stats =>
                            {
                                SpellStats SST = new SpellStats();

                                SST.PA = byte.Parse(stats.Attribute("PA").Value);
                                SST.Min_portee = byte.Parse(stats.Attribute("MIN_RANGE").Value);
                                SST.Max_portee = byte.Parse(stats.Attribute("MAX_RANGE").Value);

                                SST.AvecLigneDeVue = bool.Parse(stats.Attribute("LIGNE_DE_VUE").Value);
                                SST.IsInLine = bool.Parse(stats.Attribute("LIGNE").Value);
                                SST.EmptyCell = bool.Parse(stats.Attribute("NEED_EMPTY_CELL").Value);
                                SST.portee_modifiable = bool.Parse(stats.Attribute("MODIF").Value);

                                SST.PerTurn = byte.Parse(stats.Attribute("PER_TURN").Value);
                                SST.PerObjective = byte.Parse(stats.Attribute("PER_OBJECTIVE").Value);
                                SST.Interval = byte.Parse(stats.Attribute("INTERVAL").Value);

                                stats.Descendants("EFFETS").ToList().ForEach(effect => SST.AddEffect(new SpellEffect(int.Parse(effect.Attribute("TYPE").Value), Zones.Parse(effect.Attribute("ZONE").Value)), bool.Parse(effect.Attribute("CRITIQUE").Value) ));
                                S.GetSpellsStats(byte.Parse(stats.Attribute("NIVEAU").Value), SST);
                            });

                      
                        });

                     }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                return;
            }
        }
    }
}
