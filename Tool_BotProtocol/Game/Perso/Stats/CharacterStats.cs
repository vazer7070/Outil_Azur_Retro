using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Utils.Interfaces;

namespace Tool_BotProtocol.Game.Perso.Stats
{
    /// <summary>Coût d'un point de capital : <see cref="Cost"/> points donnent <see cref="Count"/> points de la caractéristique.</summary>
    public struct BoostCost
    {
        public int Cost { get; private set; }
        public int Count { get; private set; }
        /// <summary>Vrai quand le coût vient des fichiers de langue (<c>classes.xml</c>), comme dans le client ; faux pour la table de secours du bot.</summary>
        public bool FromLang { get; private set; }
        public BoostCost(int cost, int count, bool fromLang) { Cost = cost; Count = count; FromLang = fromLang; }
    }

    /// <summary>
    /// Caractéristiques du personnage reçues par <c>As</c> (<c>Player.getAsPacket</c> de StarLoco, lu comme
    /// <c>Account.onStats</c> du client 1.34). Un paquet produit un nouvel objet : <see cref="CharacterClass.RefreshCaracs"/>
    /// le remplace d'un coup, si bien qu'un paquet illisible laisse la fiche précédente intacte.
    /// </summary>
    public class CharacterStats : IEliminable
    {
        public double ActualEXP { get; set; }
        public double MinExpNiv { get; set; }
        public double ExpNivNext { get; set; }
        public int ActualEnergy { get; set; }
        public int EnergyMax { get; set; }
        public int VitalityActual { get; set; }
        public int MaxVitality { get; set; }
        public StatsBase Initiative { get; set; }
        public StatsBase Propec { get; set;}
        public StatsBase PA { get; set; }
        public StatsBase PM { get; set; }
        public StatsBase Vita { get; set; }
        public StatsBase Sagesse { get; set; }
        public StatsBase Force {get; set;}
        public StatsBase Intell { get; set; }
        public StatsBase Chance { get; set; }
        public StatsBase Agility { get; set; }
        public StatsBase Atteignable { get; set; }
        public StatsBase Invoc { get; set; }
        public int Alignement { get; set; }
        /// <summary>Second alignement de « a~b » : le client parle d'alignement simulé quand il diffère du premier.</summary>
        public int FakeAlignment { get; set; }
        public bool HasFakeAlignment => FakeAlignment != Alignement;
        public int AlignLVL { get; set; }
        public int Honor { get; set; }
        public int Dishonor { get; set; }
        public int GradeAli { get; set; }
        public bool HasWings { get; set; }
        /// <summary>Kamas, capital et points de sort du même paquet (recopiés dans <see cref="CharacterClass"/>).</summary>
        public int Kamas { get; set; }
        public int CapitalPoints { get; set; }
        public int SpellPoints { get; set; }
        /// <summary>Nombre de champs du paquet (51 pour StarLoco) ; 0 pour une fiche jamais reçue.</summary>
        public int FieldCount { get; private set; }
        public int LifePercent => MaxVitality == 0?0 : (int)((double)VitalityActual / MaxVitality * 100);

        private readonly Dictionary<AsField, StatsBase> fields = new Dictionary<AsField, StatsBase>();

        public CharacterStats()
        {
            Initiative = new StatsBase(0, 0, 0, 0);
            Propec = new StatsBase(0, 0, 0, 0);
            for (int i = StatsCodes.FirstField; i <= StatsCodes.LastField; i++) fields[(AsField)i] = new StatsBase(0, 0, 0, 0);
            PA = fields[AsField.PA];
            PM = fields[AsField.PM];
            Vita = fields[AsField.Vitalite];
            Sagesse = fields[AsField.Sagesse];
            Force = fields[AsField.Force];
            Intell = fields[AsField.Intelligence];
            Chance = fields[AsField.Chance];
            Agility = fields[AsField.Agilite];
            Atteignable = fields[AsField.Portee];
            Invoc = fields[AsField.Invocations];
        }

        /// <summary>Champ 9 à 50 du dernier <c>As</c> (base, équipement, dons, boost et cinquième valeur éventuelle).</summary>
        public StatsBase Get(AsField field)
        {
            return fields.TryGetValue(field, out StatsBase value) ? value : new StatsBase(0, 0, 0, 0);
        }

        public StatsBase Get(BoostableStat stat) => Get(StatsCodes.FieldOf(stat));

        /// <summary>
        /// Lit un paquet <c>As</c> complet. Renvoie faux, avec la raison, si un champ attendu manque ou n'est pas un nombre :
        /// rien n'est alors modifié. Les champs au-delà du 50e sont ignorés comme dans le client ; un champ 9 à 50 absent
        /// (serveur plus ancien) vaut zéro.
        /// </summary>
        public static bool TryParse(string packet, out CharacterStats stats, out string error)
        {
            stats = null;
            error = null;
            if (packet == null || !packet.StartsWith("As", StringComparison.Ordinal)) { error = "préfixe As absent"; return false; }
            string[] parts = packet.Substring(2).Split('|');
            if (parts.Length < 9) { error = "seulement " + parts.Length + " champ(s) sur 51"; return false; }
            var result = new CharacterStats();
            try
            {
                string[] xp = Values(parts[0], 3, "expérience");
                result.ActualEXP = Double(xp[0], "expérience");
                result.MinExpNiv = Double(xp[1], "expérience du niveau");
                result.ExpNivNext = Double(xp[2], "expérience du niveau suivant");
                result.Kamas = Int(parts[1], "kamas");
                result.CapitalPoints = Int(parts[2], "capital");
                result.SpellPoints = Int(parts[3], "points de sort");

                string[] alignment = Values(parts[4], 6, "alignement");
                string[] sides = alignment[0].Split('~');
                result.Alignement = Int(sides[0], "alignement");
                result.FakeAlignment = sides.Length > 1 ? Int(sides[1], "alignement affiché") : result.Alignement;
                result.AlignLVL = Int(alignment[1], "niveau d'alignement");
                result.GradeAli = Int(alignment[2], "grade");
                result.Honor = Int(alignment[3], "honneur");
                result.Dishonor = Int(alignment[4], "déshonneur");
                result.HasWings = alignment[5] == "1";

                string[] life = Values(parts[5], 2, "points de vie");
                result.VitalityActual = Int(life[0], "points de vie");
                result.MaxVitality = Int(life[1], "points de vie maximum");
                string[] energy = Values(parts[6], 2, "énergie");
                result.ActualEnergy = Int(energy[0], "énergie");
                result.EnergyMax = Int(energy[1], "énergie maximum");
                result.Initiative = new StatsBase(Int(parts[7], "initiative"));
                result.Propec = new StatsBase(Int(parts[8], "prospection"));

                int read = 9;
                for (int i = StatsCodes.FirstField; i <= StatsCodes.LastField && i < parts.Length; i++)
                {
                    if (i == parts.Length - 1 && parts[i].Length == 0) break; // « | » final de StarLoco
                    string name = "champ " + i.ToString(CultureInfo.InvariantCulture);
                    string[] values = Values(parts[i], 4, name);
                    StatsBase target = result.fields[(AsField)i];
                    target.RefreshStats(Int(values[0], name), Int(values[1], name), Int(values[2], name), Int(values[3], name));
                    target.Extra = values.Length > 4 && values[4].Length > 0 ? Int(values[4], name) : (int?)null;
                    read = i + 1;
                }
                result.FieldCount = read;
            }
            catch (FormatException format)
            {
                error = format.Message;
                return false;
            }
            stats = result;
            return true;
        }

        private static string[] Values(string field, int minimum, string name)
        {
            string[] values = (field ?? string.Empty).Split(',');
            if (values.Length < minimum) throw new FormatException(name + " : " + minimum + " valeur(s) attendue(s), " + values.Length + " reçue(s)");
            return values;
        }

        private static int Int(string value, string name)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            if (int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int result)) return result;
            throw new FormatException(name + " illisible (« " + Clip(value) + " »)");
        }

        private static double Double(string value, string name)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            if (double.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double result)) return result;
            throw new FormatException(name + " illisible (« " + Clip(value) + " »)");
        }

        private static string Clip(string value) => value.Length <= 24 ? value : value.Substring(0, 24) + "…";

        /// <summary>
        /// Coût d'un point dans <paramref name="stat"/> pour la classe <paramref name="classId"/>, calculé comme la fiche du client :
        /// paliers <c>b10</c>…<c>b15</c> de <c>classes.xml</c> (« [seuil, coût, nombre] », le dernier seuil atteint par la base
        /// l'emporte, nombre 1 par défaut). Sans fichier de langue, la table historique du bot sert de repli.
        /// </summary>
        public BoostCost BoostCost(byte classId, BoostableStat stat)
        {
            int current = Get(stat).BasePerso;
            BoostCost? fromLang = LangCost(classId, stat, current);
            if (fromLang.HasValue) return fromLang.Value;
            int legacy = GetCapitalStatsBoost(classId, StatsCodes.ToLegacy(stat));
            // StarLoco (Player.boostStat) : un point de capital donne deux points de vitalité au Sacrieur.
            return new BoostCost(legacy, stat == BoostableStat.Vitalite && classId == 11 ? 2 : 1, false);
        }

        private static BoostCost? LangCost(byte classId, BoostableStat stat, int current)
        {
            try
            {
                IReadOnlyDictionary<string, string> row = LangData.Raw("classes", "G", classId.ToString(CultureInfo.InvariantCulture));
                if (row == null || !row.TryGetValue("b" + ((byte)stat).ToString(CultureInfo.InvariantCulture), out string json) || string.IsNullOrEmpty(json)) return null;
                // Comme Player.getBoostCostAndCountForCharacteristic : 1 pour 1 par défaut, puis chaque palier dont le seuil
                // est atteint par la base remplace coût et nombre ; le premier seuil non atteint arrête la lecture.
                int cost = 1, count = 1;
                foreach (JToken tier in JArray.Parse(json))
                {
                    var values = tier as JArray;
                    if (values == null || values.Count < 2) continue;
                    if (current < (int)values[0]) break;
                    cost = (int)values[1];
                    count = values.Count > 2 && values[2].Type == JTokenType.Integer ? (int)values[2] : 1;
                }
                return new BoostCost(Math.Max(1, cost), Math.Max(1, count), true);
            }
            catch (Exception)
            {
                return null; // paliers illisibles : la table de secours prend le relais
            }
        }

        /// <summary>Table historique du bot (coût seul), gardée en repli quand <c>classes.xml</c> est absent.</summary>
        public int GetCapitalStatsBoost(byte id, StatsEnum id_stat)
        {
            switch (id_stat)
            {
                case StatsEnum.VITALITE:
                    return 1;
                case StatsEnum.SAGESSE:
                    return 3;
                case StatsEnum.FORCE:
                    switch (id)
                    {
                        case 1:
                            if (Force.BasePerso < 50)
                                return 2;
                            if (Force.BasePerso < 150)
                                return 3;
                            if (Force.BasePerso < 250)
                                return 4;
                            return 5;

                        case 11:
                            return 3;

                        case 5:
                            if (Force.BasePerso < 50)
                                return 2;
                            if (Force.BasePerso < 150)
                                return 3;
                            if (Force.BasePerso < 250)
                                return 4;
                            return 5;

                        case 4:
                            if (Force.BasePerso < 100)
                                return 1;
                            if (Force.BasePerso < 200)
                                return 2;
                            if (Force.BasePerso < 300)
                                return 3;
                            if (Force.BasePerso < 400)
                                return 4;
                            return 5;

                        case 2:
                            if (Force.BasePerso < 50)
                                return 2;
                            if (Force.BasePerso < 150)
                                return 3;
                            if (Force.BasePerso < 250)
                                return 4;
                            return 5;

                        case 7:
                            if (Force.BasePerso < 50)
                                return 2;
                            if (Force.BasePerso < 150)
                                return 3;
                            if (Force.BasePerso < 250)
                                return 4;
                            return 5;

                        case 12:
                            if (Force.BasePerso < 50)
                                return 1;
                            if (Force.BasePerso < 200)
                                return 2;
                            return 3;

                        case 10:
                            if (Force.BasePerso < 50)
                                return 1;
                            if (Force.BasePerso < 250)
                                return 2;
                            if (Force.BasePerso < 300)
                                return 3;
                            if (Force.BasePerso < 400)
                                return 4;
                            return 5;

                        case 9:
                            if (Force.BasePerso < 50)
                                return 1;
                            if (Force.BasePerso < 150)
                                return 2;
                            if (Force.BasePerso < 250)
                                return 3;
                            if (Force.BasePerso < 350)
                                return 4;
                            return 5;

                        case 3:
                            if (Force.BasePerso < 50)
                                return 1;
                            if (Force.BasePerso < 150)
                                return 2;
                            if (Force.BasePerso < 250)
                                return 3;
                            if (Force.BasePerso < 350)
                                return 4;
                            return 5;

                        case 6:
                            if (Force.BasePerso < 100)
                                return 1;
                            if (Force.BasePerso < 200)
                                return 2;
                            if (Force.BasePerso < 300)
                                return 3;
                            if (Force.BasePerso < 400)
                                return 4;
                            return 5;

                        case 8:
                            if (Force.BasePerso < 100)
                                return 1;
                            if (Force.BasePerso < 200)
                                return 2;
                            if (Force.BasePerso < 300)
                                return 3;
                            if (Force.BasePerso < 400)
                                return 4;
                            return 5;
                    }
                    break;
                case StatsEnum.AGILITE:
                    switch (id)
                    {
                        case 1:
                            if (Agility.BasePerso < 20)
                                return 1;
                            if (Agility.BasePerso < 40)
                                return 2;
                            if (Agility.BasePerso < 60)
                                return 3;
                            if (Agility.BasePerso < 80)
                                return 4;
                            return 5;

                        case 5:
                            if (Agility.BasePerso < 20)
                                return 1;
                            if (Agility.BasePerso < 40)
                                return 2;
                            if (Agility.BasePerso < 60)
                                return 3;
                            if (Agility.BasePerso < 80)
                                return 4;
                            return 5;

                        case 11:
                            return 3;

                        case 4:
                            if (Agility.BasePerso < 100)
                                return 1;
                            if (Agility.BasePerso < 200)
                                return 2;
                            if (Agility.BasePerso < 300)
                                return 3;
                            if (Agility.BasePerso < 400)
                                return 4;
                            return 5;

                        case 10:
                            if (Agility.BasePerso < 20)
                                return 1;
                            if (Agility.BasePerso < 40)
                                return 2;
                            if (Agility.BasePerso < 60)
                                return 3;
                            if (Agility.BasePerso < 80)
                                return 4;
                            return 5;

                        case 12:
                            if (Agility.BasePerso < 50)
                                return 1;
                            if (Agility.BasePerso < 200)
                                return 2;
                            return 3;

                        case 7:
                            if (Agility.BasePerso < 20)
                                return 1;
                            if (Agility.BasePerso < 40)
                                return 2;
                            if (Agility.BasePerso < 60)
                                return 3;
                            if (Agility.BasePerso < 80)
                                return 4;
                            return 5;

                        case 8:
                            if (Agility.BasePerso < 20)
                                return 1;
                            if (Agility.BasePerso < 40)
                                return 2;
                            if (Agility.BasePerso < 60)
                                return 3;
                            if (Agility.BasePerso < 80)
                                return 4;
                            return 5;

                        

                        case 6:
                            if (Agility.BasePerso < 50)
                                return 1;
                            if (Agility.BasePerso < 100)
                                return 2;
                            if (Agility.BasePerso < 150)
                                return 3;
                            if (Agility.BasePerso < 200)
                                return 4;
                            return 5;

                        case 9:
                            if (Agility.BasePerso < 50)
                                return 1;
                            if (Agility.BasePerso < 100)
                                return 2;
                            if (Agility.BasePerso < 150)
                                return 3;
                            if (Agility.BasePerso < 200)
                                return 4;
                            return 5;

                        case 2:
                            if (Agility.BasePerso < 20)
                                return 1;
                            if (Agility.BasePerso < 40)
                                return 2;
                            if (Agility.BasePerso < 60)
                                return 3;
                            if (Agility.BasePerso < 80)
                                return 4;
                            return 5;
                    }
                    break;

                case StatsEnum.INTELLIGENCE:
                    switch (id)
                    {
                        case 5:
                            if (Intell.BasePerso < 100)
                                return 1;
                            if (Intell.BasePerso < 200)
                                return 2;
                            if (Intell.BasePerso < 300)
                                return 3;
                            if (Intell.BasePerso < 400)
                                return 4;
                            return 5;

                        case 1:
                            if (Intell.BasePerso < 100)
                                return 1;
                            if (Intell.BasePerso < 200)
                                return 2;
                            if (Intell.BasePerso < 300)
                                return 3;
                            if (Intell.BasePerso < 400)
                                return 4;
                            return 5;

                        case 11:
                            return 3;

                        case 10:
                            if (Intell.BasePerso < 100)
                                return 1;
                            if (Intell.BasePerso < 200)
                                return 2;
                            if (Intell.BasePerso < 300)
                                return 3;
                            if (Intell.BasePerso < 400)
                                return 4;
                            return 5;

                        case 4:
                            if (Intell.BasePerso < 50)
                                return 2;
                            if (Intell.BasePerso < 150)
                                return 3;
                            if (Intell.BasePerso < 250)
                                return 4;
                            return 5;

                        case 3:
                            if (Intell.BasePerso < 20)
                                return 1;
                            if (Intell.BasePerso < 60)
                                return 2;
                            if (Intell.BasePerso < 100)
                                return 3;
                            if (Intell.BasePerso < 140)
                                return 4;
                            return 5;

                        case 12:
                            if (Intell.BasePerso < 50)
                                return 1;
                            if (Intell.BasePerso < 200)
                                return 2;
                            return 3;

                        case 8:
                            if (Intell.BasePerso < 20)
                                return 1;
                            if (Intell.BasePerso < 40)
                                return 2;
                            if (Intell.BasePerso < 60)
                                return 3;
                            if (Intell.BasePerso < 80)
                                return 4;
                            return 5;

                        case 7:
                            if (Intell.BasePerso < 100)
                                return 1;
                            if (Intell.BasePerso < 200)
                                return 2;
                            if (Intell.BasePerso < 300)
                                return 3;
                            if (Intell.BasePerso < 400)
                                return 4;
                            return 5;

                        case 2:
                            if (Intell.BasePerso < 100)
                                return 1;
                            if (Intell.BasePerso < 200)
                                return 2;
                            if (Intell.BasePerso < 300)
                                return 3;
                            if (Intell.BasePerso < 400)
                                return 4;
                            return 5;

                        case 9:
                            if (Intell.BasePerso < 50)
                                return 1;
                            if (Intell.BasePerso < 150)
                                return 2;
                            if (Intell.BasePerso < 250)
                                return 3;
                            if (Intell.BasePerso < 350)
                                return 4;
                            return 5;

                        case 6:
                            if (Intell.BasePerso < 20)
                                return 1;
                            if (Intell.BasePerso < 40)
                                return 2;
                            if (Intell.BasePerso < 60)
                                return 3;
                            if (Intell.BasePerso < 80)
                                return 4;
                            return 5;

                    }
                    break;
                case StatsEnum.CHANCE:
                    switch (id)
                    {
                        case 1:
                            if (Chance.BasePerso < 20)
                                return 1;
                            if (Chance.BasePerso < 40)
                                return 2;
                            if (Chance.BasePerso < 60)
                                return 3;
                            if (Chance.BasePerso < 80)
                                return 4;
                            return 5;

                        case 3:
                            if (Chance.BasePerso < 20)
                                return 1;
                            if (Chance.BasePerso < 40)
                                return 2;
                            if (Chance.BasePerso < 60)
                                return 3;
                            if (Chance.BasePerso < 80)
                                return 4;
                            return 5;

                        case 5:
                            if (Chance.BasePerso < 20)
                                return 1;
                            if (Chance.BasePerso < 40)
                                return 2;
                            if (Chance.BasePerso < 60)
                                return 3;
                            if (Chance.BasePerso < 80)
                                return 4;
                            return 5;

                        case 11:
                            return 3;

                        case 4:
                            if (Chance.BasePerso < 20)
                                return 1;
                            if (Chance.BasePerso < 40)
                                return 2;
                            if (Chance.BasePerso < 60)
                                return 3;
                            if (Chance.BasePerso < 80)
                                return 4;
                            return 5;

                        case 10:
                            if (Chance.BasePerso < 100)
                                return 1;
                            if (Chance.BasePerso < 200)
                                return 2;
                            if (Chance.BasePerso < 300)
                                return 3;
                            if (Chance.BasePerso < 400)
                                return 4;
                            return 5;

                        case 12:
                            if (Chance.BasePerso < 50)
                                return 1;
                            if (Chance.BasePerso < 200)
                                return 2;
                            return 3;

                        case 8:
                            if (Chance.BasePerso < 20)
                                return 1;
                            if (Chance.BasePerso < 40)
                                return 2;
                            if (Chance.BasePerso < 60)
                                return 3;
                            if (Chance.BasePerso < 80)
                                return 4;
                            return 5;

                        case 2:
                            if (Chance.BasePerso < 100)
                                return 1;
                            if (Chance.BasePerso < 200)
                                return 2;
                            if (Chance.BasePerso < 300)
                                return 3;
                            if (Chance.BasePerso < 400)
                                return 4;
                            return 5;

                        case 6:
                            if (Chance.BasePerso < 20)
                                return 1;
                            if (Chance.BasePerso < 40)
                                return 2;
                            if (Chance.BasePerso < 60)
                                return 3;
                            if (Chance.BasePerso < 80)
                                return 4;
                            return 5;

                        case 7:
                            if (Chance.BasePerso < 20)
                                return 1;
                            if (Chance.BasePerso < 40)
                                return 2;
                            if (Chance.BasePerso < 60)
                                return 3;
                            if (Chance.BasePerso < 80)
                                return 4;
                            return 5;

                        case 9:
                            if (Chance.BasePerso < 20)
                                return 1;
                            if (Chance.BasePerso < 40)
                                return 2;
                            if (Chance.BasePerso < 60)
                                return 3;
                            if (Chance.BasePerso < 80)
                                return 4;
                            return 5;
                    }
                    break;
            }
            return 5;
        }

        public void Clear()
        {
            ActualEXP = 0;
            MinExpNiv = 0;
            ExpNivNext = 0;
            ActualEnergy = 0;
            EnergyMax = 0;
            VitalityActual = 0;
            MaxVitality = 0;
            Alignement = 0;
            FakeAlignment = 0;
            AlignLVL = 0;
            Honor = 0;
            Dishonor = 0;
            GradeAli = 0;
            HasWings = false;
            Kamas = CapitalPoints = SpellPoints = 0;
            FieldCount = 0;

            Initiative.Clear();
            Propec.Clear();
            foreach (StatsBase field in fields.Values) field.Clear();
        }
    }
}
