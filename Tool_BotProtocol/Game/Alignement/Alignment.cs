using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Perso.Stats;

namespace Tool_BotProtocol.Game.Alignement
{
    /// <summary>
    /// Alignement du personnage, comme <c>Player.alignment</c> / <c>Player.rank</c> du client 1.34 : camp, valeur, grade, honneur,
    /// déshonneur et ailes lus dans le cinquième champ de <c>As</c> (<c>&lt;camp&gt;~&lt;camp&gt;,&lt;valeur&gt;,&lt;grade&gt;,&lt;honneur&gt;,&lt;déshonneur&gt;,&lt;ailes&gt;</c>,
    /// <c>Player.getAsPacket</c> de StarLoco), lus à la demande dans les caractéristiques du personnage ; spécialisation de <c>ZS</c>.
    /// </summary>
    public sealed class Alignment
    {
        /// <summary>Camp neutre (<c>Constant.ALIGNEMENT_NEUTRE</c>).</summary>
        public const int Neutral = 0;
        public const int Bonta = 1;
        public const int Brakmar = 2;
        public const int Mercenary = 3;
        /// <summary>Honneur maximal chez StarLoco (<c>Player.addHonor</c>).</summary>
        public const int MaxHonor = 18000;
        private readonly Func<CharacterStats> stats;

        internal Alignment(Func<CharacterStats> stats) { this.stats = stats; }

        /// <summary>Camp : 0 neutre, 1 Bonta, 2 Brâkmar, 3 mercenaire.</summary>
        public int Side => stats()?.Alignement ?? Neutral;
        /// <summary>Valeur d'alignement (<c>_aLvl</c>).</summary>
        public int Value => stats()?.AlignLVL ?? 0;
        /// <summary>Grade de 0 (neutre) à 10 (<c>Player.getGrade</c>).</summary>
        public int Grade => stats()?.GradeAli ?? 0;
        public int Honor => stats()?.Honor ?? 0;
        public int Dishonor => stats()?.Dishonor ?? 0;
        /// <summary>Ailes affichées (mode joueur contre joueur actif, <c>rank.enable</c> du client).</summary>
        public bool WingsEnabled => stats()?.HasWings == true;
        /// <summary>Spécialisation annoncée par <c>ZS</c> / <c>ZC</c> (0 : aucune). StarLoco y envoie l'identifiant du camp.</summary>
        public int SpecializationId { get; internal set; }

        public bool IsAligned => Side != Neutral;
        /// <summary>Honneur perdu en désactivant les ailes : 5 % chez StarLoco (<c>Player.toggleWings</c>), annoncé par <c>GIP</c>.</summary>
        public int HonorLossOnDisable => Honor * 5 / 100;
        public string Name => AlignmentTexts.Name(Side);
        public string GradeName => AlignmentTexts.GradeName(Side, Grade);
        public string SpecializationName => AlignmentTexts.SpecializationName(SpecializationId);
        /// <summary>Bornes d'honneur du grade courant (<c>getGradeHonourPointsBounds</c> du client, table <c>hp</c> de <c>pvp_fr</c>).</summary>
        public int HonorMin => AlignmentTexts.HonorBounds(Grade).Item1;
        public int HonorMax => AlignmentTexts.HonorBounds(Grade).Item2;
        public int MaxDishonor => AlignmentTexts.MaxDishonor();

        public override string ToString() => IsAligned ? Name + " · grade " + Grade.ToString(CultureInfo.InvariantCulture) + " (" + GradeName + ")" : Name;
    }

    /// <summary>Sous-zone de conquête annoncée par <c>CW</c> (liste « areas » du client, sous-zones chez StarLoco) : <c>id,camp,combat,carte du prisme,1</c>.</summary>
    public sealed class ConquestZone
    {
        public int Id { get; internal set; }
        /// <summary>Camp : 0 neutre (StarLoco envoie -1), 1 Bonta, 2 Brâkmar.</summary>
        public int Side { get; internal set; }
        public bool Fighting { get; internal set; }
        /// <summary>Carte du prisme (0 : aucun).</summary>
        public int PrismMapId { get; internal set; }
        public bool HasPrism => PrismMapId > 0;
        public string Name => AlignmentTexts.ZoneName(Id);
        public string SideName => AlignmentTexts.Name(Side);
    }

    /// <summary>Zone (« village » du client) annoncée par <c>CW</c> : <c>id,camp,1,prisme</c>.</summary>
    public sealed class ConquestVillage
    {
        public int Id { get; internal set; }
        public int Side { get; internal set; }
        public bool Fighting { get; internal set; }
        public bool HasPrism { get; internal set; }
        public string Name => AlignmentTexts.AreaName(Id);
        public string SideName => AlignmentTexts.Name(Side);
    }

    /// <summary>Données de conquête du monde (<c>Conquest.onWorldData</c>, réponse <c>CW</c> à <c>CWJ</c>).</summary>
    public sealed class ConquestWorld
    {
        private static readonly IReadOnlyList<ConquestZone> NoZones = new ConquestZone[0];
        private static readonly IReadOnlyList<ConquestVillage> NoVillages = new ConquestVillage[0];
        public int OwnedAreas { get; internal set; }
        public int TotalAreas { get; internal set; }
        public int PossibleAreas { get; internal set; }
        public int OwnedVillages { get; internal set; }
        public int TotalVillages { get; internal set; }
        public IReadOnlyList<ConquestZone> Zones { get; internal set; } = NoZones;
        public IReadOnlyList<ConquestVillage> Villages { get; internal set; } = NoVillages;
        /// <summary>Vrai quand StarLoco a envoyé sa forme courte pour un personnage neutre (zones et villages sans les compteurs).</summary>
        public bool CountsUnknown { get; internal set; }
        public ConquestZone FindZone(int id) => Zones.FirstOrDefault(zone => zone.Id == id);
        public ConquestVillage FindVillage(int id) => Villages.FirstOrDefault(village => village.Id == id);
    }

    /// <summary>Triplet expérience, butin, récolte d'un paquet <c>CB</c>.</summary>
    public sealed class ConquestModifier
    {
        public double Xp { get; internal set; }
        public double Drop { get; internal set; }
        public double Collect { get; internal set; }
    }

    /// <summary>Bonus de conquête (<c>Conquest.onConquestBonus</c>) : bonus d'alignement, multiplicateur de grade, malus.</summary>
    public sealed class ConquestBonus
    {
        public ConquestModifier AlignBonus { get; internal set; } = new ConquestModifier();
        public ConquestModifier RankMultiplier { get; internal set; } = new ConquestModifier();
        public ConquestModifier AlignMalus { get; internal set; } = new ConquestModifier();
    }

    /// <summary>Défenseur (<c>CP</c>) ou attaquant (<c>Cp</c>) du prisme de la sous-zone.</summary>
    public sealed class PrismFighter
    {
        public long Id { get; internal set; }
        public string Name { get; internal set; } = string.Empty;
        public int Level { get; internal set; }
        /// <summary>Défenseurs seulement : gfx et couleurs du personnage.</summary>
        public int Gfx { get; internal set; }
        public int Color1 { get; internal set; } = -1;
        public int Color2 { get; internal set; } = -1;
        public int Color3 { get; internal set; } = -1;
        public bool Reservist { get; internal set; }
        public bool IsDefender { get; internal set; }
    }

    /// <summary>Onglet « Défendre » du client (<c>ConquestJoinViewer</c>) : réponse <c>CIJ</c> et listes <c>CP</c> / <c>Cp</c>.</summary>
    public sealed class PrismDefense
    {
        /// <summary>Nombre de places de l'équipe du client (<c>TEAM_COUNT</c>).</summary>
        public const int TeamCount = 7;
        private readonly List<PrismFighter> defenders = new List<PrismFighter>();
        private readonly List<PrismFighter> attackers = new List<PrismFighter>();

        /// <summary><c>null</c> avant <c>CIJ</c> ; 0 combat rejoignable ; -1 aucun combat ; -2 déjà en combat ; -3 aucun prisme dans la sous-zone.</summary>
        public int? Error { get; internal set; }
        /// <summary>Temps restant avant le combat (ms) et sa durée totale, au moment de <see cref="ReceivedAt"/>.</summary>
        public int Timer { get; internal set; }
        public int MaxTimer { get; internal set; }
        public int MaxTeamPositions { get; internal set; }
        public DateTime ReceivedAt { get; internal set; }
        public bool IsJoinable => Error == 0;
        public IReadOnlyList<PrismFighter> Defenders { get { lock (defenders) return defenders.ToArray(); } }
        public IReadOnlyList<PrismFighter> Attackers { get { lock (attackers) return attackers.ToArray(); } }
        /// <summary>Temps restant estimé (ms), jamais négatif.</summary>
        public int RemainingMilliseconds => Error == 0 ? Math.Max(0, Timer - (int)Math.Min(int.MaxValue, (DateTime.UtcNow - ReceivedAt).TotalMilliseconds)) : 0;

        internal void Upsert(PrismFighter fighter)
        {
            List<PrismFighter> list = fighter.IsDefender ? defenders : attackers;
            lock (list)
            {
                int index = list.FindIndex(entry => entry.Id == fighter.Id);
                if (index < 0) list.Add(fighter); else list[index] = fighter;
            }
        }
        internal void Remove(long id, bool defender)
        {
            List<PrismFighter> list = defender ? defenders : attackers;
            lock (list) list.RemoveAll(entry => entry.Id == id);
        }
        internal void Reset()
        {
            Error = null; Timer = MaxTimer = MaxTeamPositions = 0;
            lock (defenders) defenders.Clear();
            lock (attackers) attackers.Clear();
        }
    }

    /// <summary>Prisme proposé par <c>Wp</c> (<c>Subway.onPrismCreate</c>) : <c>&lt;carte&gt;;&lt;coût&gt;</c> ou <c>&lt;carte&gt;;*</c> (prisme en combat).</summary>
    public sealed class PrismDestination
    {
        public int MapId { get; internal set; }
        public int Cost { get; internal set; }
        /// <summary>Vrai pour un prisme attaqué (<c>*</c>) : le client le propose grisé.</summary>
        public bool InFight { get; internal set; }
        public bool IsCurrent { get; internal set; }
        /// <summary>Nom de la carte (<c>maps_fr</c>) ou coordonnées, sinon « Carte &lt;id&gt; ».</summary>
        public string Label { get; internal set; } = string.Empty;
    }

    /// <summary>Textes de l'alignement et de la conquête : <c>lang_fr</c>, <c>alignment_fr</c> (<c>A.a</c>, <c>A.s</c>) et <c>pvp_fr</c> (<c>PP</c>), avec repli.</summary>
    public static class AlignmentTexts
    {
        private static readonly Regex GradeNames = new Regex("\"nl\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);
        private static readonly int[] DefaultHonorBounds = { 0, 500, 1500, 3000, 5000, 7500, 10000, 12500, 15000, 17500, Alignment.MaxHonor };

        public static string Get(string key, string fallback, params string[] args)
        {
            args = args ?? new string[0];
            try
            {
                if (key != null && LangData.Text.Has(key)) return LangData.Text.Plain(LangData.Text.Get(key, args));
            }
            catch (Exception) { /* Fichier de langue illisible : texte de repli. */ }
            try { return string.Format(CultureInfo.CurrentCulture, fallback ?? key ?? string.Empty, args.Cast<object>().ToArray()); }
            catch (FormatException) { return fallback ?? key ?? string.Empty; }
        }

        /// <summary>Nom du camp (<c>getAlignment(id).n</c>) ; repli sur les noms du client 1.34.</summary>
        public static string Name(int side)
        {
            string name = Attribute("alignment", "A.a", side, "n");
            if (!string.IsNullOrEmpty(name)) return name;
            switch (side)
            {
                case Alignment.Neutral: return "Neutre";
                case Alignment.Bonta: return "Bontarien";
                case Alignment.Brakmar: return "Brakmarien";
                case Alignment.Mercenary: return "Mercenaire";
                default: return "Alignement " + side.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Camp qui peut conquérir des zones (<c>A.a[id].c</c>) ; sans textes, Bonta et Brâkmar.</summary>
        public static bool IsConqueror(int side)
        {
            string flag = Attribute("alignment", "A.a", side, "c");
            return flag != null ? string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase) : side == Alignment.Bonta || side == Alignment.Brakmar;
        }

        /// <summary>Camps connus des textes (<c>getAlignments</c>), sinon 0 à 3.</summary>
        public static int[] Sides()
        {
            try
            {
                int[] ids = LangData.Ids("alignment", "A.a").Select(id => int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : -1)
                    .Where(value => value >= 0).OrderBy(value => value).ToArray();
                if (ids.Length > 0) return ids;
            }
            catch (Exception) { /* repli */ }
            return new[] { 0, 1, 2, 3 };
        }

        /// <summary>Nom long du grade (<c>getRankLongName</c>, table <c>grds</c> de <c>pvp_fr</c>) ; « Grade n » sans textes.</summary>
        public static string GradeName(int side, int grade)
        {
            try
            {
                string json = Attribute("pvp", "PP", "grds", "valeur");
                if (!string.IsNullOrEmpty(json))
                {
                    string[] groups = json.Split(new[] { "],[" }, StringSplitOptions.None);
                    if (side >= 0 && side < groups.Length)
                    {
                        MatchCollection names = GradeNames.Matches(groups[side]);
                        if (grade >= 0 && grade < names.Count)
                        {
                            string name = names[grade].Groups[1].Value.Trim();
                            if (name.Length > 0) return name;
                        }
                    }
                }
            }
            catch (Exception) { /* repli */ }
            return grade <= 0 ? Name(Alignment.Neutral) : "Grade " + grade.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Bornes d'honneur du grade (<c>hp</c> de <c>pvp_fr</c> : 0, 500, 1 500… 18 000) : minimum inclus, maximum du grade.</summary>
        public static Tuple<int, int> HonorBounds(int grade)
        {
            int[] bounds = DefaultHonorBounds;
            try
            {
                string raw = Attribute("pvp", "PP", "hp", "valeur");
                if (!string.IsNullOrEmpty(raw))
                {
                    int[] parsed = raw.Split(',').Select(part => int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : -1).ToArray();
                    if (parsed.Length >= 2 && parsed.All(value => value >= 0)) bounds = parsed;
                }
            }
            catch (Exception) { /* repli */ }
            int index = Math.Max(0, Math.Min(bounds.Length - 1, grade));
            int min = index == 0 ? 0 : bounds[index - 1];
            int max = bounds[index];
            if (grade <= 0) { min = 0; max = bounds[Math.Min(1, bounds.Length - 1)]; }
            return Tuple.Create(min, Math.Max(max, min));
        }

        /// <summary>Déshonneur maximal (<c>maxdp</c> de <c>pvp_fr</c>) ; 500 sans textes.</summary>
        public static int MaxDishonor()
        {
            string raw = Attribute("pvp", "PP", "maxdp", "valeur");
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value > 0 ? value : 500;
        }

        /// <summary>Nom d'une spécialisation (<c>A.s[id].n</c>) ; vide pour 0.</summary>
        public static string SpecializationName(int id)
        {
            if (id <= 0) return string.Empty;
            return Attribute("alignment", "A.s", id, "n") ?? "Spécialisation " + id.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Nom d'une sous-zone (<c>getMapSubAreaText(id).n</c>) sans le préfixe « // » des sous-zones cachées ; « Sous-zone n » sans textes.</summary>
        public static string ZoneName(int subAreaId)
        {
            string name = LangData.IsLoaded("maps") ? LangData.Map.SubAreaName(subAreaId) : null;
            if (string.IsNullOrEmpty(name) || name == subAreaId.ToString(CultureInfo.InvariantCulture)) return "Sous-zone " + subAreaId.ToString(CultureInfo.InvariantCulture);
            return name.StartsWith("//", StringComparison.Ordinal) ? name.Substring(2) : name;
        }

        /// <summary>Nom d'une zone (<c>getMapAreaText(id).n</c>) ; « Zone n » sans textes.</summary>
        public static string AreaName(int areaId)
        {
            string name = LangData.IsLoaded("maps") ? LangData.Map.AreaName(areaId) : null;
            return string.IsNullOrEmpty(name) || name == areaId.ToString(CultureInfo.InvariantCulture) ? "Zone " + areaId.ToString(CultureInfo.InvariantCulture) : name;
        }

        /// <summary>Nom d'une carte pour les listes de prismes : <c>maps_fr</c> puis coordonnées de <c>BotMaps</c>, sinon « Carte n ».</summary>
        public static string MapLabel(int mapId)
        {
            string id = mapId.ToString(CultureInfo.InvariantCulture);
            try
            {
                if (LangData.IsLoaded("maps") && LangData.Map.Has(mapId))
                {
                    string name = LangData.Map.Name(mapId);
                    System.Drawing.Point? coords = LangData.Map.Coords(mapId);
                    return name + (coords != null ? " [" + coords.Value.X.ToString(CultureInfo.InvariantCulture) + "," + coords.Value.Y.ToString(CultureInfo.InvariantCulture) + "]" : string.Empty);
                }
            }
            catch (Exception) { /* repli */ }
            Maps.Map known;
            return Maps.Map.AllBotMaps.TryGetValue(mapId, out known) ? known.GetCoordinates + " · carte " + id : "Carte " + id;
        }

        private static string Attribute(string family, string table, int id, string attribute) => Attribute(family, table, id.ToString(CultureInfo.InvariantCulture), attribute);
        private static string Attribute(string family, string table, string id, string attribute)
        {
            try
            {
                IReadOnlyDictionary<string, string> row = LangData.Raw(family, table, id);
                return row != null && row.TryGetValue(attribute, out string value) ? value : null;
            }
            catch (Exception) { return null; }
        }
    }
}
