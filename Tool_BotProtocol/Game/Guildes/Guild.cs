using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Tool_BotProtocol.Game.Data;

namespace Tool_BotProtocol.Game.Guildes
{
    /// <summary>
    /// Droits d'un membre : <c>dofus.datacenter.GuildRights</c> du client 1.34 et <c>Constant.G_*</c> de StarLoco (masque de
    /// bits, en base 36 dans <c>gS</c>, en décimal dans <c>gIM</c> et <c>gP</c>). Le meneur vaut <c>1</c> et a tous les droits.
    /// </summary>
    [Flags]
    public enum GuildRight
    {
        None = 0,
        Boss = 1,
        Boost = 2,
        ManageRights = 4,
        Invite = 8,
        Ban = 16,
        ManageAllXp = 32,
        ManageRanks = 64,
        HireCollector = 128,
        ManageOwnXp = 256,
        Collect = 512,
        UsePaddock = 4096,
        ArrangePaddock = 8192,
        ManageOtherMounts = 16384,
    }

    /// <summary>Onglets de la fenêtre <c>Guild</c> du client (<c>currentTab</c>) : chacun est rempli par son paquet <c>gI…</c>.</summary>
    public enum GuildTab { Members, Boosts, Collectors, Paddocks, Houses }

    /// <summary>Caractéristiques des percepteurs que <c>gB</c> augmente ; la valeur est la lettre envoyée (<c>Guild.boostCharacteristic</c>).</summary>
    public enum GuildBoostKind { Prospecting = 'p', Wisdom = 'x', Pods = 'o', Collectors = 'k' }

    public static class GuildRights
    {
        private static readonly KeyValuePair<GuildRight, string>[] Labels =
        {
            Pair(GuildRight.Boost, "GUILD_RIGHTS_BOOST"), Pair(GuildRight.ManageRights, "GUILD_RIGHTS_RIGHTS"), Pair(GuildRight.Invite, "GUILD_RIGHTS_INVIT"),
            Pair(GuildRight.Ban, "GUILD_RIGHTS_BANN"), Pair(GuildRight.ManageAllXp, "GUILD_RIGHTS_PERCENTXP"), Pair(GuildRight.ManageRanks, "GUILD_RIGHTS_RANK"),
            Pair(GuildRight.HireCollector, "GUILD_RIGHTS_HIRETAX"), Pair(GuildRight.ManageOwnXp, "GUILD_RIGHT_MANAGE_OWN_XP"), Pair(GuildRight.Collect, "GUILD_RIGHTS_COLLECT"),
            Pair(GuildRight.UsePaddock, "GUILD_RIGHTS_MOUNT_PARK_USE"), Pair(GuildRight.ArrangePaddock, "GUILD_RIGHTS_MOUNT_PARK_ARRANGE"),
            Pair(GuildRight.ManageOtherMounts, "GUILD_RIGHTS_MANAGE_OTHER_MOUNT"),
        };
        private static readonly Dictionary<GuildRight, string> Fallbacks = new Dictionary<GuildRight, string>
        {
            { GuildRight.Boost, "Gérer les boosts" }, { GuildRight.ManageRights, "Gérer les droits" }, { GuildRight.Invite, "Inviter de nouveaux membres" },
            { GuildRight.Ban, "Bannir" }, { GuildRight.ManageAllXp, "Gérer les répartitions d'XP" }, { GuildRight.ManageRanks, "Gérer les rangs" },
            { GuildRight.HireCollector, "Poser un percepteur" }, { GuildRight.ManageOwnXp, "Gérer sa répartition d'XP" }, { GuildRight.Collect, "Collecter sur un percepteur" },
            { GuildRight.UsePaddock, "Utiliser les enclos" }, { GuildRight.ArrangePaddock, "Aménager les enclos" }, { GuildRight.ManageOtherMounts, "Gérer les montures des autres membres" },
        };

        public static bool IsBoss(int rights) => rights == (int)GuildRight.Boss;
        /// <summary><c>GuildRights.canDo</c> du client et <c>GuildMember.canDo</c> de StarLoco : le meneur peut tout, sinon le bit du droit.</summary>
        public static bool CanDo(int rights, GuildRight right) => IsBoss(rights) || (rights & (int)right) != 0;
        /// <summary>Droits de la fiche d'un membre du client (<c>GuildMemberInfos</c>), dans l'ordre des cases.</summary>
        public static IReadOnlyList<GuildRight> Editable => Labels.Select(pair => pair.Key).ToArray();
        public static string Label(GuildRight right)
        {
            foreach (KeyValuePair<GuildRight, string> pair in Labels)
                if (pair.Key == right) return GuildTexts.Get(pair.Value, Fallbacks[right]);
            return right.ToString();
        }
        /// <summary>Liste lisible des droits (« Meneur » pour <c>1</c>, « Aucun droit » pour 0).</summary>
        public static string Describe(int rights)
        {
            if (IsBoss(rights)) return GuildTexts.RankName(1);
            string[] names = Labels.Where(pair => (rights & (int)pair.Key) != 0).Select(pair => Label(pair.Key)).ToArray();
            return names.Length == 0 ? "Aucun droit" : string.Join(", ", names);
        }
        private static KeyValuePair<GuildRight, string> Pair(GuildRight right, string key) => new KeyValuePair<GuildRight, string>(right, key);
    }

    /// <summary>Palier d'un boost des percepteurs (<c>guilds_fr</c>, table <c>GU</c>, entrée <c>b</c>) : coût en points, gain, maximum.</summary>
    public sealed class GuildBoostRule
    {
        public int Cost { get; internal set; }
        public int Gain { get; internal set; }
        public int Max { get; internal set; }
    }

    /// <summary>
    /// Textes de la guilde : clé de <c>lang_fr</c> quand les fichiers de langue sont chargés (<see cref="LangData"/>), sinon un texte
    /// de repli (formaté avec <see cref="string.Format(string, object[])"/>) ; rangs de <c>ranks_fr</c> (table <c>R</c>) ; paliers des
    /// boosts de <c>guilds_fr</c>. Le HTML des textes du client est retiré.
    /// </summary>
    public static class GuildTexts
    {
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

        /// <summary>Titre d'un onglet, comme la fenêtre <c>Guild</c> du client.</summary>
        public static string Tab(GuildTab tab)
        {
            switch (tab)
            {
                case GuildTab.Boosts: return Get("GUILD_BOOSTS", "Personnalisation");
                case GuildTab.Collectors: return Get("GUILD_TAXCOLLECTORS", "Percepteurs");
                case GuildTab.Paddocks: return Get("MOUNT_PARK", "Enclos");
                case GuildTab.Houses: return Get("HOUSES_WORD", "Maisons");
                default: return Get("GUILD_MEMBERS", "Membres");
            }
        }

        /// <summary>Nom du rang (<c>getRankInfos(rang).n</c>) ; « Rang n » sans <c>ranks_fr</c>.</summary>
        public static string RankName(int rank)
        {
            IReadOnlyDictionary<string, string> row = Rank(rank);
            string name;
            return row != null && row.TryGetValue("n", out name) && !string.IsNullOrEmpty(name) ? name : "Rang " + rank.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Ordre d'affichage du rang (<c>getRankInfos(rang).o</c>, les membres sont triés dessus) ; 999 sans table.</summary>
        public static int RankOrder(int rank)
        {
            IReadOnlyDictionary<string, string> row = Rank(rank);
            string order; int value;
            return row != null && row.TryGetValue("o", out order) && int.TryParse(order, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : 999;
        }

        /// <summary>Rangs connus (identifiant, nom), triés par ordre d'affichage ; vide sans <c>ranks_fr</c>.</summary>
        public static IReadOnlyList<KeyValuePair<int, string>> Ranks()
        {
            var ranks = new List<KeyValuePair<int, string>>();
            try
            {
                foreach (string id in LangData.Ids("ranks", "R"))
                {
                    int rank;
                    if (int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out rank)) ranks.Add(new KeyValuePair<int, string>(rank, RankName(rank)));
                }
            }
            catch (Exception) { /* Table illisible : aucun rang. */ }
            return ranks.OrderBy(pair => RankOrder(pair.Key)).ThenBy(pair => pair.Key).ToArray();
        }

        /// <summary>
        /// Nom d'un percepteur : le client compose prénom et nom depuis <c>names_fr</c> (<c>getFullNameText</c>), famille que le lot D3
        /// n'exporte pas ; le bot montre les deux identifiants.
        /// </summary>
        public static string CollectorName(int firstNameId, int lastNameId) =>
            "Percepteur " + firstNameId.ToString(CultureInfo.InvariantCulture) + "-" + lastNameId.ToString(CultureInfo.InvariantCulture);

        /// <summary>« Nom de la carte (x, y) » comme <c>r8.position</c> du client, avec les coordonnées de <c>maps_fr</c> ou celles transmises.</summary>
        public static string Position(int mapId, int? x, int? y)
        {
            string name = null; System.Drawing.Point? coords = null;
            try
            {
                if (LangData.Map.Has(mapId)) { name = LangData.Map.Name(mapId); coords = LangData.Map.Coords(mapId); }
            }
            catch (Exception) { /* Table illisible : coordonnées transmises. */ }
            int? px = coords.HasValue ? coords.Value.X : x, py = coords.HasValue ? coords.Value.Y : y;
            string where = px.HasValue && py.HasValue ? " (" + px.Value.ToString(CultureInfo.InvariantCulture) + ", " + py.Value.ToString(CultureInfo.InvariantCulture) + ")" : string.Empty;
            return (string.IsNullOrEmpty(name) ? "Carte " + mapId.ToString(CultureInfo.InvariantCulture) : name) + where;
        }

        /// <summary>Nom affiché pour une caractéristique de percepteur (<c>DISCERNMENT</c>, <c>WISDOM</c>, <c>WEIGHT</c>, <c>TAX_COLLECTOR_COUNT</c>).</summary>
        public static string BoostName(GuildBoostKind kind)
        {
            switch (kind)
            {
                case GuildBoostKind.Prospecting: return Get("DISCERNMENT", "Prospection");
                case GuildBoostKind.Wisdom: return Get("WISDOM", "Sagesse");
                case GuildBoostKind.Pods: return Get("WEIGHT", "Pods");
                default: return Get("TAX_COLLECTOR_COUNT", "Nombre de percepteur");
            }
        }

        /// <summary>
        /// Palier d'un boost : <c>guilds_fr</c> (<c>GU</c>/<c>b</c> : <c>p</c>/<c>pm</c> prospection, <c>x</c>/<c>xm</c> sagesse, <c>w</c>/<c>wm</c>
        /// pods, <c>c</c>/<c>cm</c> percepteurs, triplets « depuis, coût, gain »), sinon les constantes de StarLoco (1 point par pas,
        /// 10 points par percepteur ; maxima 500, 400, 5000 et 50).
        /// </summary>
        public static GuildBoostRule BoostRule(GuildBoostKind kind)
        {
            string attribute; GuildBoostRule fallback;
            switch (kind)
            {
                case GuildBoostKind.Prospecting: attribute = "p"; fallback = new GuildBoostRule { Cost = 1, Gain = 1, Max = 500 }; break;
                case GuildBoostKind.Wisdom: attribute = "x"; fallback = new GuildBoostRule { Cost = 1, Gain = 1, Max = 400 }; break;
                case GuildBoostKind.Pods: attribute = "w"; fallback = new GuildBoostRule { Cost = 1, Gain = 20, Max = 5000 }; break;
                default: attribute = "c"; fallback = new GuildBoostRule { Cost = 10, Gain = 1, Max = 50 }; break;
            }
            IReadOnlyDictionary<string, string> row = Boosts();
            string steps, max;
            if (row == null || !row.TryGetValue(attribute, out steps) || !row.TryGetValue(attribute + "m", out max)) return fallback;
            int[] numbers = Numbers(steps);
            int limit;
            if (numbers.Length < 3 || !int.TryParse(max, NumberStyles.Integer, CultureInfo.InvariantCulture, out limit)) return fallback;
            return new GuildBoostRule { Cost = Math.Max(1, numbers[1]), Gain = Math.Max(1, numbers[2]), Max = limit };
        }

        /// <summary>Palier des sorts de percepteur (<c>s</c>/<c>sm</c>) : 5 points par niveau, niveau 5 au plus (StarLoco : capital ≥ 5).</summary>
        public static GuildBoostRule SpellRule()
        {
            IReadOnlyDictionary<string, string> row = Boosts();
            string steps, max; int limit;
            var fallback = new GuildBoostRule { Cost = 5, Gain = 1, Max = 5 };
            if (row == null || !row.TryGetValue("s", out steps) || !row.TryGetValue("sm", out max) || !int.TryParse(max, NumberStyles.Integer, CultureInfo.InvariantCulture, out limit)) return fallback;
            int[] numbers = Numbers(steps);
            return numbers.Length < 2 ? fallback : new GuildBoostRule { Cost = Math.Max(1, numbers[1]), Gain = 1, Max = limit };
        }

        private static int[] Numbers(string steps)
        {
            var values = new List<int>();
            foreach (Match match in Regex.Matches(steps ?? string.Empty, "-?\\d+"))
            {
                int value;
                if (int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) values.Add(value);
            }
            return values.ToArray();
        }

        private static IReadOnlyDictionary<string, string> Rank(int rank)
        {
            try { return LangData.Raw("ranks", "R", rank.ToString(CultureInfo.InvariantCulture)); }
            catch (Exception) { return null; }
        }

        private static IReadOnlyDictionary<string, string> Boosts()
        {
            try { return LangData.Raw("guilds", "GU", "b"); }
            catch (Exception) { return null; }
        }

        internal static int Int(string[] fields, int index, int fallback = 0)
        {
            int value;
            return index < fields.Length && int.TryParse(fields[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        internal static long Long(string[] fields, int index, long fallback = 0)
        {
            long value;
            return index < fields.Length && long.TryParse(fields[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        /// <summary>Entier en base 36 comme <c>parseInt(texte, 36)</c> du client (signe toléré) ; <c>false</c> si aucun chiffre n'est lisible.</summary>
        internal static bool TryBase36(string text, out long value)
        {
            value = 0;
            text = (text ?? string.Empty).Trim();
            bool negative = text.StartsWith("-", StringComparison.Ordinal);
            if (negative) text = text.Substring(1);
            if (text.Length == 0 || text.Length > 12) return false;
            foreach (char character in text)
            {
                int digit = character >= '0' && character <= '9' ? character - '0'
                    : character >= 'a' && character <= 'z' ? character - 'a' + 10
                    : character >= 'A' && character <= 'Z' ? character - 'A' + 10 : -1;
                if (digit < 0) return false;
                value = value * 36 + digit;
            }
            if (negative) value = -value;
            return true;
        }

        internal static string Base36(long value)
        {
            const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
            if (value == 0) return "0";
            bool negative = value < 0; value = Math.Abs(value);
            string text = string.Empty;
            while (value > 0) { text = digits[(int)(value % 36)] + text; value /= 36; }
            return negative ? "-" + text : text;
        }
    }

    /// <summary>
    /// Membre (<c>gIM</c>, <c>Guild.onInfosMembers</c>) : <c>id;nom;niveau;gfx;rang;xpDonnée;pourcentXp;droits;état;alignement;heures</c>
    /// (état 0 hors ligne, 1 connecté, 2 en combat ; heures depuis la dernière connexion). Instance immuable.
    /// </summary>
    public sealed class GuildMember
    {
        public long Id { get; private set; }
        public string Name { get; private set; } = string.Empty;
        public int Level { get; private set; }
        public int Gfx { get; private set; }
        public int Rank { get; private set; }
        public long XpGiven { get; private set; }
        public int XpPercent { get; private set; }
        public int Rights { get; private set; }
        public int State { get; private set; }
        public int Alignment { get; private set; }
        public int HoursSinceConnection { get; private set; }

        public bool IsOnline => State > 0;
        public bool IsInFight => State == 2;
        public bool IsBoss => GuildRights.IsBoss(Rights);
        public string RankName => GuildTexts.RankName(Rank);
        public int RankOrder => GuildTexts.RankOrder(Rank);
        public bool CanDo(GuildRight right) => GuildRights.CanDo(Rights, right);

        /// <summary>Lit une entrée de <c>gIM+</c> ; <c>false</c> sans identifiant numérique. Les autres champs absents valent 0.</summary>
        public static bool TryParse(string entry, out GuildMember member)
        {
            member = null;
            string[] fields = (entry ?? string.Empty).Split(';');
            long id;
            if (fields.Length < 2 || !long.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) return false;
            member = new GuildMember
            {
                Id = id, Name = fields[1], Level = GuildTexts.Int(fields, 2), Gfx = GuildTexts.Int(fields, 3), Rank = GuildTexts.Int(fields, 4),
                XpGiven = GuildTexts.Long(fields, 5), XpPercent = GuildTexts.Int(fields, 6), Rights = GuildTexts.Int(fields, 7), State = GuildTexts.Int(fields, 8),
                Alignment = GuildTexts.Int(fields, 9), HoursSinceConnection = GuildTexts.Int(fields, 10),
            };
            return true;
        }

        public override string ToString() => Name + " (" + RankName + ", niveau " + Level.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>Attaquant (<c>gITp</c> : <c>id36;nom;niveau;0</c>) ou défenseur (<c>gITP</c> : <c>id36;nom;gfx;niveau;c1;c2;c3</c>) d'un percepteur.</summary>
    public sealed class GuildCollectorFighter
    {
        public long Id { get; private set; }
        public string Name { get; private set; } = string.Empty;
        public int Level { get; private set; }
        public int Gfx { get; private set; }
        public bool IsDefender { get; private set; }

        public static bool TryParse(string entry, bool defender, out GuildCollectorFighter fighter)
        {
            fighter = null;
            string[] fields = (entry ?? string.Empty).Split(';');
            long id;
            if (fields.Length < 2 || !GuildTexts.TryBase36(fields[0], out id)) return false;
            fighter = new GuildCollectorFighter
            {
                Id = id, Name = fields[1], IsDefender = defender,
                Gfx = defender ? GuildTexts.Int(fields, 2) : 0, Level = defender ? GuildTexts.Int(fields, 3) : GuildTexts.Int(fields, 2),
            };
            return true;
        }
    }

    /// <summary>
    /// Percepteur de la guilde (<c>gITM</c>, <c>Guild.onInfosTaxCollectorsMovement</c>) :
    /// <c>id36;prénom36,nom36[,poseur,datePose,dernierRécolteur,dateRécolte,prochaineRécolte];carte36,x,y;état;minuteur;minuteurMax;places</c>.
    /// Les listes d'attaquants et de défenseurs sont remplacées par copie (instance immuable).
    /// </summary>
    public sealed class GuildCollector
    {
        public long Id { get; private set; }
        public int FirstNameId { get; private set; }
        public int LastNameId { get; private set; }
        /// <summary>Vrai quand StarLoco a transmis les sept champs du nom (poseur, dates, récolteur).</summary>
        public bool HasDetails { get; private set; }
        public string CallerName { get; private set; } = "?";
        public long StartDate { get; private set; } = -1;
        public string LastHarvesterName { get; private set; } = "?";
        public long LastHarvestDate { get; private set; } = -1;
        public long NextHarvestDate { get; private set; } = -1;
        public int MapId { get; private set; }
        public int? X { get; private set; }
        public int? Y { get; private set; }
        /// <summary>0 en récolte, sinon attaqué ou en combat (<c>inFight</c> de StarLoco).</summary>
        public int State { get; private set; }
        public long Timer { get; private set; }
        public long MaxTimer { get; private set; }
        public int MaxPlayers { get; private set; }
        public IReadOnlyList<GuildCollectorFighter> Attackers { get; private set; } = new GuildCollectorFighter[0];
        public IReadOnlyList<GuildCollectorFighter> Defenders { get; private set; } = new GuildCollectorFighter[0];

        public bool IsInFight => State > 0;
        public string Name => GuildTexts.CollectorName(FirstNameId, LastNameId);
        public string Position => GuildTexts.Position(MapId, X, Y);

        public static bool TryParse(string entry, out GuildCollector collector)
        {
            collector = null;
            string[] fields = (entry ?? string.Empty).Split(';');
            long id, first = 0, last = 0, map;
            if (fields.Length < 3 || !GuildTexts.TryBase36(fields[0], out id)) return false;
            string[] names = fields[1].Split(',');
            GuildTexts.TryBase36(names[0], out first);
            if (names.Length > 1) GuildTexts.TryBase36(names[1], out last);
            string[] place = fields[2].Split(',');
            if (!GuildTexts.TryBase36(place[0], out map)) return false;
            int x, y;
            collector = new GuildCollector
            {
                Id = id, FirstNameId = (int)first, LastNameId = (int)last, MapId = (int)map,
                X = place.Length > 1 && int.TryParse(place[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out x) ? x : (int?)null,
                Y = place.Length > 2 && int.TryParse(place[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out y) ? y : (int?)null,
                State = GuildTexts.Int(fields, 3), Timer = GuildTexts.Long(fields, 4), MaxTimer = GuildTexts.Long(fields, 5), MaxPlayers = GuildTexts.Int(fields, 6),
            };
            if (names.Length > 2)
            {
                // Comme le client : champs absents ou vides affichés « ? » et -1.
                collector.HasDetails = true;
                collector.CallerName = names[2].Length > 0 ? names[2] : "?";
                collector.StartDate = GuildTexts.Long(names, 3, -1);
                collector.LastHarvesterName = names.Length > 4 && names[4].Length > 0 ? names[4] : "?";
                collector.LastHarvestDate = GuildTexts.Long(names, 5, -1);
                collector.NextHarvestDate = GuildTexts.Long(names, 6, -1);
            }
            return true;
        }

        internal GuildCollector WithFighters(bool defenders, IReadOnlyList<GuildCollectorFighter> list)
        {
            var copy = (GuildCollector)MemberwiseClone();
            if (defenders) copy.Defenders = list; else copy.Attackers = list;
            return copy;
        }
    }

    /// <summary>Monture d'un enclos (<c>modèle,nom,propriétaire</c> dans <c>gIF</c>).</summary>
    public sealed class GuildPaddockMount
    {
        public int ModelId { get; internal set; }
        public string Name { get; internal set; } = string.Empty;
        public string OwnerName { get; internal set; } = string.Empty;
    }

    /// <summary>Enclos (<c>gIF</c>, <c>Guild.onInfosMountPark</c>) : <c>carte;taille;objetsMax[;modèle,nom,propriétaire,…]</c>.</summary>
    public sealed class GuildPaddock
    {
        public int MapId { get; private set; }
        public int Size { get; private set; }
        public int MaxObjects { get; private set; }
        public IReadOnlyList<GuildPaddockMount> Mounts { get; private set; } = new GuildPaddockMount[0];
        public string Position => GuildTexts.Position(MapId, null, null);

        public static bool TryParse(string entry, out GuildPaddock paddock)
        {
            paddock = null;
            string[] fields = (entry ?? string.Empty).Split(';');
            int map;
            if (fields.Length < 1 || !int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out map)) return false;
            var mounts = new List<GuildPaddockMount>();
            if (fields.Length > 3 && fields[3].Length > 0)
            {
                string[] parts = fields[3].Split(',');
                for (int index = 0; index + 2 < parts.Length; index += 3)
                    mounts.Add(new GuildPaddockMount
                    {
                        ModelId = GuildTexts.Int(parts, index), OwnerName = parts[index + 2],
                        Name = parts[index + 1].Length > 0 ? parts[index + 1] : GuildTexts.Get("NO_NAME", "SansNom"),
                    });
            }
            paddock = new GuildPaddock { MapId = map, Size = GuildTexts.Int(fields, 1), MaxObjects = GuildTexts.Int(fields, 2), Mounts = mounts.ToArray() };
            return true;
        }
    }

    /// <summary>Maison de guilde (<c>gIH</c>, <c>Guild.onInfosHouses</c>) : <c>id;propriétaire;x,y;compétences;droits</c>.</summary>
    public sealed class GuildHouse
    {
        public int Id { get; private set; }
        public string OwnerName { get; private set; } = string.Empty;
        public int X { get; private set; }
        public int Y { get; private set; }
        public IReadOnlyList<int> Skills { get; private set; } = new int[0];
        public string RightsRaw { get; private set; } = string.Empty;
        public int Rights { get; private set; }

        public static bool TryParse(string entry, out GuildHouse house)
        {
            house = null;
            string[] fields = (entry ?? string.Empty).Split(';');
            int id;
            if (fields.Length < 2 || !int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) return false;
            string[] coords = fields.Length > 2 ? fields[2].Split(',') : new string[0];
            var skills = new List<int>();
            if (fields.Length > 3 && fields[3].Length > 0)
                foreach (string text in fields[3].Split(','))
                {
                    int value;
                    if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) skills.Add(value);
                }
            house = new GuildHouse
            {
                Id = id, OwnerName = fields[1], X = GuildTexts.Int(coords, 0), Y = GuildTexts.Int(coords, 1), Skills = skills.ToArray(),
                RightsRaw = fields.Length > 4 ? fields[4] : string.Empty, Rights = fields.Length > 4 ? GuildTexts.Int(fields, 4) : 0,
            };
            return true;
        }
    }

    /// <summary>Sort de percepteur (<c>id;niveau</c> dans <c>gIB</c>).</summary>
    public sealed class GuildSpell
    {
        public int Id { get; internal set; }
        public int Level { get; internal set; }
    }

    /// <summary>
    /// Personnalisation (<c>gIB</c>, <c>Guild.onInfosBoosts</c>) : <c>percepteursMax|percepteurs|vie|dommages|pods|prospection|sagesse|
    /// population|points|coûtPose|sort;niveau|…</c> (StarLoco : <c>Guild.parseCollectorToGuild</c>).
    /// </summary>
    public sealed class GuildBoosts
    {
        public int MaxCollectors { get; private set; }
        public int Collectors { get; private set; }
        public int Life { get; private set; }
        public int Damage { get; private set; }
        public int Pods { get; private set; }
        public int Prospecting { get; private set; }
        public int Wisdom { get; private set; }
        public int Population { get; private set; }
        /// <summary>Points restant à répartir (<c>GUILD_BONUSPOINTS</c>, capital de StarLoco).</summary>
        public int Points { get; private set; }
        public int HireCost { get; private set; }
        public IReadOnlyList<GuildSpell> Spells { get; private set; } = new GuildSpell[0];

        public int Value(GuildBoostKind kind)
        {
            switch (kind)
            {
                case GuildBoostKind.Prospecting: return Prospecting;
                case GuildBoostKind.Wisdom: return Wisdom;
                case GuildBoostKind.Pods: return Pods;
                default: return MaxCollectors;
            }
        }

        public static bool TryParse(string payload, out GuildBoosts boosts)
        {
            boosts = null;
            string[] fields = (payload ?? string.Empty).Split('|');
            if (fields.Length < 10) return false;
            var spells = new List<GuildSpell>();
            for (int index = 10; index < fields.Length; index++)
            {
                string[] parts = fields[index].Split(';');
                int id;
                if (parts.Length >= 2 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
                    spells.Add(new GuildSpell { Id = id, Level = GuildTexts.Int(parts, 1) });
            }
            boosts = new GuildBoosts
            {
                MaxCollectors = GuildTexts.Int(fields, 0), Collectors = GuildTexts.Int(fields, 1), Life = GuildTexts.Int(fields, 2), Damage = GuildTexts.Int(fields, 3),
                Pods = GuildTexts.Int(fields, 4), Prospecting = GuildTexts.Int(fields, 5), Wisdom = GuildTexts.Int(fields, 6), Population = GuildTexts.Int(fields, 7),
                Points = GuildTexts.Int(fields, 8), HireCost = GuildTexts.Int(fields, 9), Spells = spells.OrderBy(spell => spell.Id).ToArray(),
            };
            return true;
        }
    }

    /// <summary>
    /// Guilde du personnage telle que le serveur l'annonce : <c>gS</c> (nom, emblème, droits propres), <c>gIG</c> (niveau et xp),
    /// <c>gIM</c> (membres), <c>gIB</c> (personnalisation), <c>gITM</c>/<c>gITp</c>/<c>gITP</c> (percepteurs), <c>gIF</c> (enclos),
    /// <c>gIH</c> (maisons). Les listes sont des instantanés immuables remplacés sous verrou (fil réseau) et lisibles sans verrou.
    /// </summary>
    public sealed class Guild
    {
        private readonly object sync = new object();
        private GuildMember[] members = new GuildMember[0];
        private GuildCollector[] collectors = new GuildCollector[0];
        private GuildPaddock[] paddocks = new GuildPaddock[0];
        private GuildHouse[] houses = new GuildHouse[0];

        public string Name { get; private set; } = string.Empty;
        public int EmblemBackId { get; private set; }
        public int EmblemBackColor { get; private set; }
        public int EmblemUpId { get; private set; }
        public int EmblemUpColor { get; private set; }
        /// <summary>Emblème « fond,couleurFond,motif,couleurMotif » en base 36, le format de <c>GM</c> (lisible par le compositeur d'emblèmes).</summary>
        public string Emblem { get; private set; } = string.Empty;
        /// <summary>Droits du personnage dans sa guilde (dernier <c>gS</c>).</summary>
        public int OwnRights { get; private set; }
        public bool HasGuild => Name.Length > 0;
        public bool IsBoss => HasGuild && GuildRights.IsBoss(OwnRights);
        public bool CanDo(GuildRight right) => HasGuild && GuildRights.CanDo(OwnRights, right);

        public bool GeneralReceived { get; private set; }
        /// <summary>Premier champ de <c>gIG</c> : la guilde compte dix membres (fonctions complètes) ; sinon <c>GUILD_INVALID_INFOS</c>.</summary>
        public bool IsValid { get; private set; }
        public int Level { get; private set; }
        public long XpMin { get; private set; }
        public long Xp { get; private set; }
        /// <summary>-1 au dernier niveau.</summary>
        public long XpMax { get; private set; } = -1;

        public IReadOnlyList<GuildMember> Members { get { lock (sync) return members; } }
        public bool MembersReceived { get; private set; }
        public GuildBoosts Boosts { get; private set; }
        public bool BoostsReceived { get; private set; }
        public IReadOnlyList<GuildCollector> Collectors { get { lock (sync) return collectors; } }
        public bool CollectorsReceived { get; private set; }
        public int MaxPaddocks { get; private set; }
        public IReadOnlyList<GuildPaddock> Paddocks { get { lock (sync) return paddocks; } }
        public bool PaddocksReceived { get; private set; }
        public IReadOnlyList<GuildHouse> Houses { get { lock (sync) return houses; } }
        public bool HousesReceived { get; private set; }

        public GuildMember FindMember(long id) => Members.FirstOrDefault(member => member.Id == id);
        public GuildMember FindMember(string name) => Members.FirstOrDefault(member => string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase));
        public GuildCollector FindCollector(long id) => Collectors.FirstOrDefault(collector => collector.Id == id);
        public int OnlineCount => Members.Count(member => member.IsOnline);

        internal void SetStats(string name, int backId, int backColor, int upId, int upColor, int rights)
        {
            lock (sync)
            {
                Name = name ?? string.Empty;
                EmblemBackId = backId; EmblemBackColor = backColor; EmblemUpId = upId; EmblemUpColor = upColor;
                Emblem = GuildTexts.Base36(backId) + "," + GuildTexts.Base36(backColor) + "," + GuildTexts.Base36(upId) + "," + GuildTexts.Base36(upColor);
                OwnRights = rights;
            }
        }

        internal void SetGeneral(bool valid, int level, long xpMin, long xp, long xpMax)
        {
            lock (sync) { IsValid = valid; Level = level; XpMin = xpMin; Xp = xp; XpMax = xpMax; GeneralReceived = true; }
        }

        /// <summary>Comme <c>onInfosMembers</c> : <c>+</c> ajoute ou met à jour, <c>-</c> retire ; la liste est triée par ordre de rang.</summary>
        internal void ApplyMembers(bool add, IReadOnlyList<GuildMember> entries)
        {
            lock (sync)
            {
                var updated = members.ToList();
                foreach (GuildMember entry in entries)
                {
                    int index = updated.FindIndex(member => member.Id == entry.Id);
                    if (!add) { if (index >= 0) updated.RemoveAt(index); }
                    else if (index >= 0) updated[index] = entry;
                    else updated.Add(entry);
                }
                members = updated.OrderBy(member => member.RankOrder).ThenBy(member => member.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
                MembersReceived = true;
            }
        }

        internal void SetBoosts(GuildBoosts boosts) { lock (sync) { Boosts = boosts; BoostsReceived = true; } }

        /// <summary><c>gITM+</c> ajoute ou met à jour (les combattants connus sont gardés), <c>gITM-</c> retire ; <c>gITM</c> vide efface tout.</summary>
        internal void ApplyCollectors(bool add, IReadOnlyList<GuildCollector> entries)
        {
            lock (sync)
            {
                var updated = collectors.ToList();
                foreach (GuildCollector entry in entries)
                {
                    int index = updated.FindIndex(collector => collector.Id == entry.Id);
                    if (!add) { if (index >= 0) updated.RemoveAt(index); }
                    else if (index >= 0) updated[index] = entry.WithFighters(false, updated[index].Attackers).WithFighters(true, updated[index].Defenders);
                    else updated.Add(entry);
                }
                collectors = updated.ToArray();
                CollectorsReceived = true;
            }
        }

        internal void ClearCollectors() { lock (sync) { collectors = new GuildCollector[0]; CollectorsReceived = true; } }

        /// <summary><c>gITp±</c> / <c>gITP±</c> : <c>false</c> si le percepteur est inconnu (le client journalise « impossible de trouver le percepteur »).</summary>
        internal bool ApplyFighters(long collectorId, bool add, bool defenders, IReadOnlyList<GuildCollectorFighter> entries)
        {
            lock (sync)
            {
                int index = Array.FindIndex(collectors, collector => collector.Id == collectorId);
                if (index < 0) return false;
                GuildCollector target = collectors[index];
                var list = (defenders ? target.Defenders : target.Attackers).ToList();
                foreach (GuildCollectorFighter entry in entries)
                {
                    int existing = list.FindIndex(fighter => fighter.Id == entry.Id);
                    if (!add) { if (existing >= 0) list.RemoveAt(existing); }
                    else if (existing >= 0) list[existing] = entry;
                    else list.Add(entry);
                }
                var updated = collectors.ToArray();
                updated[index] = target.WithFighters(defenders, list.ToArray());
                collectors = updated;
                return true;
            }
        }

        internal void SetPaddocks(int max, IReadOnlyList<GuildPaddock> list) { lock (sync) { MaxPaddocks = max; paddocks = list.ToArray(); PaddocksReceived = true; } }
        internal void SetHouses(IReadOnlyList<GuildHouse> list) { lock (sync) { houses = list.ToArray(); HousesReceived = true; } }

        /// <summary>Oublie la guilde (départ, bannissement, déconnexion).</summary>
        internal void Clear()
        {
            lock (sync)
            {
                Name = string.Empty; Emblem = string.Empty; EmblemBackId = EmblemBackColor = EmblemUpId = EmblemUpColor = 0; OwnRights = 0;
                GeneralReceived = MembersReceived = BoostsReceived = CollectorsReceived = PaddocksReceived = HousesReceived = false;
                IsValid = false; Level = 0; XpMin = Xp = 0; XpMax = -1; Boosts = null; MaxPaddocks = 0;
                members = new GuildMember[0]; collectors = new GuildCollector[0]; paddocks = new GuildPaddock[0]; houses = new GuildHouse[0];
            }
        }
    }
}
