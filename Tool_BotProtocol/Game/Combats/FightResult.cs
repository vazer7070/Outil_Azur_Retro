using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Tool_BotProtocol.Game.Combats
{
    /// <summary>Catégorie d'une ligne de <c>GE</c> (premier champ, lu par <c>Game.parsePlayerData</c> du client 1.34).</summary>
    public enum FightResultKind
    {
        /// <summary><c>0</c> : perdant.</summary>
        Loser = 0,
        /// <summary><c>2</c> : gagnant.</summary>
        Winner = 2,
        /// <summary><c>5</c> : percepteur de la carte (expérience de guilde, butin).</summary>
        Collector = 5,
        /// <summary><c>6</c> : butin commun à répartir entre les gagnants (lu par le client ; jamais émis par StarLoco).</summary>
        Pool = 6,
        /// <summary>Autre valeur : la ligne est conservée mais le client ne l'affiche pas.</summary>
        Other = -1
    }

    /// <summary>Objet gagné : <c>&lt;modèle&gt;~&lt;quantité&gt;</c>.</summary>
    public sealed class FightResultItem
    {
        internal FightResultItem(int templateId, int quantity) { TemplateId = templateId; Quantity = quantity; }
        public int TemplateId { get; }
        public int Quantity { get; }
    }

    /// <summary>
    /// Une ligne de résultat de combat. Disposition des champs selon le type de combat de l'en-tête :
    /// type 0 (défi, monstres, percepteur) <c>&lt;cat&gt;;&lt;id&gt;;&lt;nom&gt;;&lt;niveau&gt;;&lt;mort&gt;;&lt;xpMin&gt;;&lt;xp&gt;;&lt;xpMax&gt;;&lt;xpGagnée&gt;;&lt;xpGuilde&gt;;&lt;xpMonture&gt;;&lt;objets&gt;;&lt;kamas&gt;</c> ;
    /// type 1 (agression, conquête) <c>…;&lt;mort&gt;;&lt;honneurMin&gt;;&lt;honneur&gt;;&lt;honneurMax&gt;;&lt;honneurGagné&gt;;&lt;grade&gt;;&lt;déshonneur&gt;;&lt;déshonneurGagné&gt;;&lt;objets&gt;;&lt;kamas&gt;;&lt;xpMin&gt;;&lt;xp&gt;;&lt;xpMax&gt;;&lt;xpGagnée&gt;</c> ;
    /// catégorie 6 : <c>6;&lt;objets&gt;;&lt;kamas&gt;</c>. Les champs vides ou absents valent null.
    /// </summary>
    public sealed class FightResultEntry
    {
        internal FightResultEntry() { }

        public FightResultKind Kind { get; internal set; }
        /// <summary>Valeur brute du premier champ.</summary>
        public int RawKind { get; internal set; }
        public int Id { get; internal set; }
        /// <summary>Troisième champ brut : nom du joueur, numéro de modèle de monstre, ou <c>prénom,nom</c> d'un percepteur.</summary>
        public string NameData { get; internal set; } = string.Empty;
        /// <summary>Modèle de monstre lorsque <see cref="NameData"/> est un nombre seul ; null sinon.</summary>
        public int? MonsterTemplateId { get; internal set; }
        public int Level { get; internal set; }
        public bool IsDead { get; internal set; }

        public long? MinExperience { get; internal set; }
        public long? Experience { get; internal set; }
        public long? MaxExperience { get; internal set; }
        public long? WonExperience { get; internal set; }
        public long? GuildExperience { get; internal set; }
        public long? MountExperience { get; internal set; }

        public int? MinHonour { get; internal set; }
        public int? Honour { get; internal set; }
        public int? MaxHonour { get; internal set; }
        public int? WonHonour { get; internal set; }
        public int? Rank { get; internal set; }
        public int? Disgrace { get; internal set; }
        public int? WonDisgrace { get; internal set; }

        public IReadOnlyList<FightResultItem> Items { get; internal set; } = new FightResultItem[0];
        public long? Kamas { get; internal set; }
        /// <summary>Ligne brute, pour le diagnostic.</summary>
        public string Raw { get; internal set; } = string.Empty;
    }

    /// <summary>
    /// Résultat de fin de combat : <c>GE&lt;durée ms&gt;[;&lt;bonus étoiles&gt;]|&lt;initiateur&gt;|&lt;type&gt;|&lt;ligne&gt;|&lt;ligne&gt;…</c>
    /// (<c>Fight.getGE</c> de StarLoco, <c>Game.onEnd</c> du client 1.34). Le bonus d'étoiles n'est présent qu'en combat contre des monstres.
    /// </summary>
    public sealed class FightResult
    {
        private FightResult() { }

        public long DurationMilliseconds { get; private set; }
        /// <summary>Bonus d'étoiles du groupe de monstres (multiples de 15 chez StarLoco) ; null s'il est absent (le client retient -1).</summary>
        public int? StarBonus { get; private set; }
        /// <summary>Deuxième champ : identifiant de l'initiateur du combat (<c>getInit0</c>) ; 0 s'il est illisible.</summary>
        public int InitiatorId { get; private set; }
        /// <summary>Type de combat de l'en-tête : 0 défi/monstres/percepteur, 1 agression/conquête ; -1 s'il manque.</summary>
        public int FightType { get; private set; } = -1;
        public IReadOnlyList<FightResultEntry> Entries { get; private set; } = new FightResultEntry[0];
        public IReadOnlyList<FightResultEntry> Winners => Entries.Where(entry => entry.Kind == FightResultKind.Winner).ToList();
        public IReadOnlyList<FightResultEntry> Losers => Entries.Where(entry => entry.Kind == FightResultKind.Loser).ToList();
        public IReadOnlyList<FightResultEntry> Collectors => Entries.Where(entry => entry.Kind == FightResultKind.Collector).ToList();
        /// <summary>Butin commun (catégorie 6) que le client répartit ensuite entre les gagnants.</summary>
        public IReadOnlyList<FightResultEntry> Pools => Entries.Where(entry => entry.Kind == FightResultKind.Pool).ToList();
        /// <summary>Lignes illisibles, ignorées sans arrêter la lecture des suivantes.</summary>
        public IReadOnlyList<string> Rejected { get; private set; } = new string[0];
        public string Raw { get; private set; } = string.Empty;

        public FightResultEntry Find(int fighterId) => Entries.FirstOrDefault(entry => entry.Kind != FightResultKind.Pool && entry.Id == fighterId);
        public bool IsWinner(int fighterId) => Find(fighterId)?.Kind == FightResultKind.Winner;

        /// <summary>
        /// Lit le contenu de <c>GE</c> sans le préfixe. Renvoie null si la durée est illisible ; une ligne mal formée est rangée
        /// dans <see cref="Rejected"/> sans empêcher la lecture des autres.
        /// </summary>
        public static FightResult Parse(string payload)
        {
            if (string.IsNullOrEmpty(payload)) return null;
            string[] parts = payload.Split('|');
            string[] header = parts[0].Split(';');
            if (!long.TryParse(header[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long duration) || duration < 0) return null;
            var result = new FightResult { DurationMilliseconds = duration, Raw = payload };
            if (header.Length > 1 && TryInt(header[1], out int stars)) result.StarBonus = stars;
            if (parts.Length > 1 && TryInt(parts[1], out int initiator)) result.InitiatorId = initiator;
            if (parts.Length > 2 && TryInt(parts[2], out int type)) result.FightType = type;
            var entries = new List<FightResultEntry>();
            var rejected = new List<string>();
            for (int index = 3; index < parts.Length; index++)
            {
                string line = parts[index];
                if (line.Length == 0) continue;
                FightResultEntry entry = ParseEntry(line, result.FightType);
                if (entry == null) rejected.Add(line);
                else entries.Add(entry);
            }
            result.Entries = entries;
            result.Rejected = rejected;
            return result;
        }

        private static FightResultEntry ParseEntry(string line, int fightType)
        {
            string[] fields = line.Split(';');
            if (!TryInt(fields[0], out int kind)) return null;
            var entry = new FightResultEntry { RawKind = kind, Kind = KindOf(kind), Raw = line };
            if (entry.Kind == FightResultKind.Pool)
            {
                if (fields.Length < 2) return null;
                entry.Items = ParseItems(fields[1]);
                entry.Kamas = fields.Length > 2 ? OptionalLong(fields[2]) : null;
                return entry;
            }
            if (fields.Length < 5 || !TryInt(fields[1], out int id)) return null;
            entry.Id = id;
            entry.NameData = fields[2];
            if (entry.NameData.IndexOf(',') < 0 && TryInt(entry.NameData, out int monster)) entry.MonsterTemplateId = monster;
            entry.Level = TryInt(fields[3], out int level) ? level : 0;
            entry.IsDead = fields[4] == "1";
            if (fightType == 0)
            {
                entry.MinExperience = OptionalLong(At(fields, 5));
                entry.Experience = OptionalLong(At(fields, 6));
                entry.MaxExperience = OptionalLong(At(fields, 7));
                entry.WonExperience = OptionalLong(At(fields, 8));
                entry.GuildExperience = OptionalLong(At(fields, 9));
                entry.MountExperience = OptionalLong(At(fields, 10));
                entry.Items = ParseItems(At(fields, 11));
                entry.Kamas = OptionalLong(At(fields, 12));
            }
            else if (fightType == 1)
            {
                entry.MinHonour = OptionalInt(At(fields, 5));
                entry.Honour = OptionalInt(At(fields, 6));
                entry.MaxHonour = OptionalInt(At(fields, 7));
                entry.WonHonour = OptionalInt(At(fields, 8));
                entry.Rank = OptionalInt(At(fields, 9));
                entry.Disgrace = OptionalInt(At(fields, 10));
                entry.WonDisgrace = OptionalInt(At(fields, 11));
                entry.Items = ParseItems(At(fields, 12));
                entry.Kamas = OptionalLong(At(fields, 13));
                entry.MinExperience = OptionalLong(At(fields, 14));
                entry.Experience = OptionalLong(At(fields, 15));
                entry.MaxExperience = OptionalLong(At(fields, 16));
                entry.WonExperience = OptionalLong(At(fields, 17));
            }
            return entry;
        }

        /// <summary><c>modèle~quantité,modèle~quantité</c> ; comme le client, la lecture s'arrête au premier modèle illisible et ignore le modèle 0.</summary>
        private static IReadOnlyList<FightResultItem> ParseItems(string text)
        {
            var items = new List<FightResultItem>();
            if (string.IsNullOrEmpty(text)) return items;
            foreach (string entry in text.Split(','))
            {
                string[] pair = entry.Split('~');
                if (!TryInt(pair[0], out int template)) break;
                if (template == 0) continue;
                int quantity = pair.Length > 1 && TryInt(pair[1], out int count) ? count : 1;
                items.Add(new FightResultItem(template, quantity));
            }
            return items;
        }

        private static FightResultKind KindOf(int kind)
        {
            switch (kind)
            {
                case 0: return FightResultKind.Loser;
                case 2: return FightResultKind.Winner;
                case 5: return FightResultKind.Collector;
                case 6: return FightResultKind.Pool;
                default: return FightResultKind.Other;
            }
        }

        private static string At(string[] fields, int index) => index < fields.Length ? fields[index] : string.Empty;
        private static bool TryInt(string text, out int value) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        private static int? OptionalInt(string text) => TryInt(text, out int value) ? value : (int?)null;
        private static long? OptionalLong(string text) =>
            long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : (long?)null;
    }
}
