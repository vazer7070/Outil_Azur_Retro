using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Tool_BotProtocol.Game.Data;

namespace Tool_BotProtocol.Game.Actions
{
    /// <summary>Type d'un combattant de <c>fD</c>, déduit comme <c>CharactersManager.getNameFromData</c> du client.</summary>
    public enum MapFightMemberKind { Player, Monster, Collector }

    /// <summary>Un combattant de <c>fD</c> : <c>&lt;nom&gt;~&lt;niveau&gt;</c>.</summary>
    public sealed class MapFightMember
    {
        internal MapFightMember(string raw, string name, int level, MapFightMemberKind kind) { Raw = raw; Name = name; Level = level; Kind = kind; }
        /// <summary>Champ brut : nom du joueur, modèle de monstre, ou <c>prénom,nom</c> (base 36) d'un percepteur.</summary>
        public string Raw { get; }
        public string Name { get; }
        public int Level { get; }
        public MapFightMemberKind Kind { get; }
    }

    /// <summary>Une équipe de <c>fL</c> (<c>type,alignement,nombre</c>) complétée par <c>fD</c>.</summary>
    public sealed class MapFightTeam
    {
        internal MapFightTeam(int number, int type, int alignment, int count, IReadOnlyList<MapFightMember> members)
        {
            Number = number; Type = type; Alignment = alignment; Count = count; Members = members;
        }
        /// <summary>1 ou 2, comme <c>addTeam(1|2, …)</c> du client.</summary>
        public int Number { get; }
        /// <summary>0 joueurs, 1 monstres, 4 percepteur… (valeur de StarLoco).</summary>
        public int Type { get; }
        public int Alignment { get; }
        /// <summary>Combattants annoncés par <c>fL</c> (invocations exclues chez StarLoco).</summary>
        public int Count { get; }
        /// <summary>Combattants lus dans <c>fD</c>, ou <c>null</c> tant que le détail n'est pas reçu.</summary>
        public IReadOnlyList<MapFightMember> Members { get; }
        /// <summary>Somme des niveaux des combattants (« Niveau » du volet du client) ; 0 sans détail.</summary>
        public int TotalLevel => Members?.Sum(member => member.Level) ?? 0;

        internal MapFightTeam WithMembers(IReadOnlyList<MapFightMember> members) => new MapFightTeam(Number, Type, Alignment, Count, members);
    }

    /// <summary>Un combat de la carte : entrée de <c>fL</c>, détail de <c>fD</c>. Instance immuable (remplacée à chaque paquet).</summary>
    public sealed class MapFightInfo
    {
        internal MapFightInfo(long fightId, long? startTime, MapFightTeam team1, MapFightTeam team2)
        {
            FightId = fightId; StartTime = startTime; Team1 = team1; Team2 = team2;
        }
        public long FightId { get; }
        /// <summary>Début en millisecondes du serveur, ou <c>null</c> (<c>-1</c> : placement en cours).</summary>
        public long? StartTime { get; }
        public MapFightTeam Team1 { get; }
        public MapFightTeam Team2 { get; }
        public bool HasDetails => Team1.Members != null && Team2.Members != null;

        /// <summary>
        /// Durée comme <c>NightManager.getDiffDate</c> : heure du serveur estimée (<c>BT</c>) moins le début annoncé ;
        /// <c>null</c> sans début, sans <c>BT</c> ou si l'écart est négatif.
        /// </summary>
        public TimeSpan? Elapsed(DateTime? serverNow)
        {
            if (!StartTime.HasValue || !serverNow.HasValue) return null;
            DateTime start;
            try { start = DateTimeOffset.FromUnixTimeMilliseconds(StartTime.Value).UtcDateTime; }
            catch (ArgumentOutOfRangeException) { return null; }
            TimeSpan elapsed = serverNow.Value - start;
            return elapsed < TimeSpan.Zero ? (TimeSpan?)null : elapsed;
        }

        internal MapFightInfo WithDetails(IReadOnlyList<MapFightMember> team1, IReadOnlyList<MapFightMember> team2) =>
            new MapFightInfo(FightId, StartTime, Team1.WithMembers(team1), Team2.WithMembers(team2));
    }

    /// <summary>Réponse <c>BWK&lt;pseudo&gt;|&lt;état&gt;|&lt;personnage&gt;|&lt;zone&gt;</c> à <c>BW&lt;nom&gt;</c>.</summary>
    public sealed class WhoisInfo
    {
        internal WhoisInfo(string pseudo, int state, string characterName, int? areaId, string areaName, string text)
        {
            Pseudo = pseudo; State = state; CharacterName = characterName; AreaId = areaId; AreaName = areaName; Text = text;
        }
        public string Pseudo { get; }
        /// <summary>1 : sur une carte ; 2 : en combat (aiguillage de <c>Basics.onWhoIs</c>).</summary>
        public int State { get; }
        public string CharacterName { get; }
        /// <summary>Zone (<c>-1</c> chez StarLoco hors amis : <c>null</c>).</summary>
        public int? AreaId { get; }
        /// <summary>Nom de la zone (textes du client), <c>null</c> si inconnue.</summary>
        public string AreaName { get; }
        /// <summary>Phrase prête à afficher.</summary>
        public string Text { get; internal set; }
    }

    /// <summary>Lecture tolérante de <c>fC</c>, <c>fL</c>, <c>fD</c> et <c>BWK</c> : une entrée illisible est ignorée, jamais d'exception.</summary>
    public static class MapFightParser
    {
        /// <summary><c>fL&lt;id&gt;;&lt;début|-1&gt;;&lt;t1&gt;,&lt;a1&gt;,&lt;n1&gt;;&lt;t2&gt;,&lt;a2&gt;,&lt;n2&gt;;|…</c> (<c>Fights.onList</c>).</summary>
        public static IReadOnlyList<MapFightInfo> ParseList(string payload, out int skipped)
        {
            skipped = 0;
            var fights = new List<MapFightInfo>();
            if (string.IsNullOrEmpty(payload)) return fights;
            foreach (string entry in payload.Split('|'))
            {
                if (entry.Length == 0) continue;
                MapFightInfo fight = ParseEntry(entry);
                if (fight == null) { skipped++; continue; }
                if (fights.Any(other => other.FightId == fight.FightId)) { skipped++; continue; }
                fights.Add(fight);
            }
            return fights;
        }

        private static MapFightInfo ParseEntry(string entry)
        {
            string[] fields = entry.Split(';');
            if (fields.Length < 4 || !TryLong(fields[0], out long id) || !TryLong(fields[1], out long start)) return null;
            MapFightTeam team1 = ParseTeam(1, fields[2]), team2 = ParseTeam(2, fields[3]);
            if (team1 == null || team2 == null) return null;
            return new MapFightInfo(id, start < 0 ? (long?)null : start, team1, team2);
        }

        private static MapFightTeam ParseTeam(int number, string value)
        {
            string[] parts = (value ?? string.Empty).Split(',');
            if (parts.Length < 3 || !TryInt(parts[0], out int type) || !TryInt(parts[1], out int alignment) || !TryInt(parts[2], out int count) || count < 0)
                return null;
            return new MapFightTeam(number, type, alignment, count, null);
        }

        /// <summary><c>fD&lt;id&gt;|&lt;nom&gt;~&lt;niveau&gt;;…|&lt;nom&gt;~&lt;niveau&gt;;…</c> (<c>Fights.onDetails</c>) ; false si l'en-tête est illisible.</summary>
        public static bool TryParseDetails(string payload, out long fightId, out IReadOnlyList<MapFightMember> team1, out IReadOnlyList<MapFightMember> team2)
        {
            fightId = 0; team1 = null; team2 = null;
            string[] parts = (payload ?? string.Empty).Split('|');
            if (parts.Length < 3 || !TryLong(parts[0], out fightId)) return false;
            team1 = ParseMembers(parts[1]);
            team2 = ParseMembers(parts[2]);
            return true;
        }

        private static IReadOnlyList<MapFightMember> ParseMembers(string value)
        {
            var members = new List<MapFightMember>();
            foreach (string entry in (value ?? string.Empty).Split(';'))
            {
                if (entry.Length == 0) continue;
                string[] fields = entry.Split('~');
                if (fields.Length < 2 || fields[0].Length == 0 || !TryInt(fields[1], out int level)) continue;
                members.Add(Member(fields[0], level));
            }
            return members;
        }

        /// <summary>Comme <c>getNameFromData</c> : deux champs séparés par une virgule → percepteur ; nombre → monstre ; sinon joueur.</summary>
        private static MapFightMember Member(string raw, int level)
        {
            if (raw.Split(',').Length == 2) return new MapFightMember(raw, CollectorName(raw), level, MapFightMemberKind.Collector);
            if (TryInt(raw, out int templateId)) return new MapFightMember(raw, Monstres.Monstres.ResolveName(templateId), level, MapFightMemberKind.Monster);
            return new MapFightMember(raw, raw, level, MapFightMemberKind.Player);
        }

        /// <summary>Les noms de percepteurs du client (<c>getFullNameText</c>) ne sont pas exportés : libellé générique.</summary>
        private static string CollectorName(string raw) => "Percepteur (" + raw + ")";

        /// <summary><c>BWK&lt;pseudo&gt;|&lt;état&gt;|&lt;personnage&gt;|&lt;zone&gt;</c> : exactement quatre champs comme le client, sinon null.</summary>
        public static WhoisInfo ParseWhois(string payload)
        {
            string[] fields = (payload ?? string.Empty).Split('|');
            if (fields.Length != 4 || !TryInt(fields[1], out int state)) return null;
            int? area = TryInt(fields[3], out int parsed) && parsed >= 0 ? parsed : (int?)null;
            string areaName = null;
            if (area.HasValue && LangData.IsLoaded("maps"))
            {
                string name = LangData.Map.AreaName(area.Value);
                if (!string.IsNullOrEmpty(name) && name != area.Value.ToString(CultureInfo.InvariantCulture)) areaName = name;
            }
            return new WhoisInfo(fields[0], state, fields[2], area, areaName, null);
        }

        internal static bool TryInt(string value, out int result) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        internal static bool TryLong(string value, out long result) => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }
}
