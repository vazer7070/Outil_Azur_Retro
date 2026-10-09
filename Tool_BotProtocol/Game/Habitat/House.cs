using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Tool_BotProtocol.Game.Data;

namespace Tool_BotProtocol.Game.Habitat
{
    /// <summary>
    /// Maison connue du personnage, comme <c>dofus.datacenter.House</c> du client 1.34 : propriétés annoncées par
    /// <c>hP&lt;id&gt;|&lt;pseudo&gt;;&lt;achetable&gt;[;&lt;guilde&gt;;&lt;emblème&gt;]</c> à l'arrivée sur la carte de sa porte
    /// (<c>House.load</c> de StarLoco), complétées pour ses propres maisons par <c>hL+|&lt;id&gt;;&lt;accès&gt;;&lt;en vente&gt;;&lt;prix&gt;</c>
    /// et par les réponses <c>hCK</c>, <c>hSK</c> et <c>hG</c>. Nom et description viennent des textes <c>houses</c> du client.
    /// </summary>
    public sealed class HouseInfo
    {
        public int Id { get; internal set; }
        /// <summary>Pseudo du propriétaire ; vide pour une maison sans propriétaire (StarLoco envoie un champ vide).</summary>
        public string OwnerName { get; internal set; } = string.Empty;
        public bool HasOwner => OwnerName.Length > 0;
        public bool IsForSale { get; internal set; }
        /// <summary>Maison de guilde : nom et emblème (<c>back,backColor,up,upColor</c>) quand StarLoco les transmet.</summary>
        public string GuildName { get; internal set; } = string.Empty;
        public string GuildEmblem { get; internal set; } = string.Empty;
        public bool IsGuildHouse => GuildName.Length > 0;
        /// <summary>Vrai après <c>hL+</c> : la maison appartient au compte (<c>localOwner</c> du client).</summary>
        public bool LocalOwner { get; internal set; }
        /// <summary>Deuxième champ de <c>hL+</c>, lu comme <c>isLocked</c> par le client (StarLoco y écrit l'accès de la maison).</summary>
        public bool IsLocked { get; internal set; }
        /// <summary>Prix de vente connu (<c>hL+</c>, <c>hCK</c>, <c>hSK</c>) ; 0 quand la maison n'est pas en vente.</summary>
        public int Price { get; internal set; }
        /// <summary>Droits de la maison de guilde (<c>hG</c>), somme des <see cref="HouseGuildRights"/> ; 0 tant qu'ils sont inconnus.</summary>
        public int GuildRights { get; internal set; }
        /// <summary>Vrai après un <c>hG</c> reçu pour cette maison.</summary>
        public bool GuildRightsKnown { get; internal set; }

        public string Name => HouseTexts.Name(Id);
        public string Description => HouseTexts.Description(Id);

        /// <summary>Ligne d'en-tête du menu de la porte, comme <c>onObjectRelease</c> du client : « Chez moi ! », « Chez X » ou « Maison abandonnée ».</summary>
        public string OwnerLine()
        {
            if (LocalOwner) return HouseTexts.Text("MY_HOME", "Chez moi !");
            if (HasOwner) return HouseTexts.Text("HOME_OF", "Chez " + OwnerName, OwnerName);
            return HouseTexts.Text("HOUSE_WITH_NO_OWNER", "Maison abandonnée");
        }

        internal HouseInfo Copy() => (HouseInfo)MemberwiseClone();
    }

    /// <summary>Droits d'une maison de guilde (<c>Constant.H_*</c> de StarLoco, clés <c>GUILD_HOUSE_RIGHT_&lt;n&gt;</c> du client).</summary>
    [Flags]
    public enum HouseGuildRights
    {
        None = 0,
        /// <summary>Maison visible pour la guilde (valeur 1 : StarLoco la tient pour « aucun droit »).</summary>
        Visible = 1,
        EmblemForGuild = 2,
        EmblemForOthers = 4,
        GuildEntersWithoutCode = 8,
        OthersCannotEnter = 16,
        GuildOpensChestsWithoutCode = 32,
        OthersCannotOpenChests = 64,
        GuildCanTeleport = 128,
        GuildCanRest = 256
    }

    /// <summary>Taxe du mode marchand annoncée par <c>Eq1|1|&lt;taxe&gt;</c> (<c>Exchange.onAskOfflineExchange</c>).</summary>
    public sealed class MerchantTax
    {
        internal MerchantTax(int type, double rate, long tax) { Type = type; Rate = rate; Tax = tax; }
        /// <summary>Premier champ (toujours 1 chez StarLoco).</summary>
        public int Type { get; }
        /// <summary>Taux en pour cent : deuxième champ divisé par 10, comme le client (StarLoco envoie 1, soit 0,1 %).</summary>
        public double Rate { get; }
        /// <summary>Taxe en kamas (StarLoco : somme des prix du magasin divisée par 1 000).</summary>
        public long Tax { get; }
    }

    /// <summary>Textes <c>houses</c> et <c>lang</c> du client utiles aux maisons (fichiers de langue convertis par le lot D4).</summary>
    public static class HouseTexts
    {
        /// <summary>Compétences du menu intérieur de la maison quand les textes manquent (<c>H.ids</c> du client 1.34).</summary>
        private static readonly short[] DefaultIndoorSkills = { 81, 97, 98, 100, 108 };

        /// <summary>Nom de la maison (<c>H.h[id].n</c>), sinon « Maison n° id ».</summary>
        public static string Name(int id)
        {
            IReadOnlyDictionary<string, string> row = Row("H.h", id.ToString(CultureInfo.InvariantCulture));
            return row != null && row.TryGetValue("n", out string name) && !string.IsNullOrWhiteSpace(name) ? name
                : HouseTexts.Text("HOUSE_WORD", "Maison") + " n° " + id.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Description de la maison (<c>H.h[id].d</c>), sinon vide.</summary>
        public static string Description(int id)
        {
            IReadOnlyDictionary<string, string> row = Row("H.h", id.ToString(CultureInfo.InvariantCulture));
            return row != null && row.TryGetValue("d", out string description) ? description.Trim() : string.Empty;
        }

        /// <summary>Maison dont la porte est sur cette cellule de cette carte (<c>H.d[carte].c&lt;cellule&gt;</c>), ou <c>null</c>.</summary>
        public static int? HouseForDoor(int mapId, short cellId)
        {
            IReadOnlyDictionary<string, string> row = Row("H.d", mapId.ToString(CultureInfo.InvariantCulture));
            return row != null && row.TryGetValue("c" + cellId.ToString(CultureInfo.InvariantCulture), out string value) ? ParseInt(value) : null;
        }

        /// <summary>Maison dont cette carte est l'intérieur (<c>H.m[carte]</c>, <c>getHousesMapText</c>), ou <c>null</c>.</summary>
        public static int? HouseForIndoorMap(int mapId)
        {
            IReadOnlyDictionary<string, string> row = Row("H.m", mapId.ToString(CultureInfo.InvariantCulture));
            return row != null && row.TryGetValue("valeur", out string value) ? ParseInt(value) : null;
        }

        /// <summary>Compétences du menu intérieur (<c>H.ids</c>, <c>getHousesIndoorSkillsText</c>) dans l'ordre du client.</summary>
        public static short[] IndoorSkills()
        {
            try
            {
                var skills = new List<KeyValuePair<int, short>>();
                foreach (string id in LangData.Ids("houses", "H.ids") ?? new string[0])
                {
                    IReadOnlyDictionary<string, string> row = Row("H.ids", id);
                    if (row == null || !row.TryGetValue("valeur", out string value)) continue;
                    int? skill = ParseInt(value);
                    if (skill.HasValue && skill.Value > 0 && skill.Value <= short.MaxValue && int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out int order))
                        skills.Add(new KeyValuePair<int, short>(order, (short)skill.Value));
                }
                if (skills.Count > 0) return skills.OrderBy(pair => pair.Key).Select(pair => pair.Value).Distinct().ToArray();
            }
            catch (Exception) { /* Textes du client illisibles : liste du client 1.34. */ }
            return (short[])DefaultIndoorSkills.Clone();
        }

        /// <summary>Texte <c>lang</c> du client avec ses paramètres, ou le texte de repli du bot.</summary>
        public static string Text(string key, string fallback, params string[] args)
        {
            try
            {
                if (LangData.Text.Has(key))
                {
                    string value = LangData.Text.Get(key, args);
                    if (!string.IsNullOrWhiteSpace(value) && !value.StartsWith("!", StringComparison.Ordinal)) return value.Trim();
                }
            }
            catch (Exception) { /* Textes du client illisibles : le texte du bot suffit. */ }
            return fallback;
        }

        /// <summary>Libellé d'un droit de maison de guilde (<c>GUILD_HOUSE_RIGHT_&lt;n&gt;</c>).</summary>
        public static string RightName(HouseGuildRights right)
        {
            int value = (int)right;
            string fallback;
            switch (right)
            {
                case HouseGuildRights.Visible: fallback = "Maison visible pour la guilde"; break;
                case HouseGuildRights.EmblemForGuild: fallback = "Blason visible pour la guilde"; break;
                case HouseGuildRights.EmblemForOthers: fallback = "Blason visible pour tout le monde"; break;
                case HouseGuildRights.GuildEntersWithoutCode: fallback = "Accès autorisé aux membres de la guilde"; break;
                case HouseGuildRights.OthersCannotEnter: fallback = "Accès interdit aux non-membres de la guilde"; break;
                case HouseGuildRights.GuildOpensChestsWithoutCode: fallback = "Accès aux coffres autorisé aux membres de la guilde"; break;
                case HouseGuildRights.OthersCannotOpenChests: fallback = "Accès aux coffres interdit aux non-membres de la guilde"; break;
                case HouseGuildRights.GuildCanTeleport: fallback = "Téléportation autorisée vers cette maison"; break;
                case HouseGuildRights.GuildCanRest: fallback = "Repos autorisé aux membres de la guilde dans cette maison"; break;
                default: fallback = "Droit " + value.ToString(CultureInfo.InvariantCulture); break;
            }
            return Text("GUILD_HOUSE_RIGHT_" + value.ToString(CultureInfo.InvariantCulture), fallback);
        }

        private static IReadOnlyDictionary<string, string> Row(string table, string id)
        {
            try { return LangData.Raw("houses", table, id); }
            catch (Exception) { return null; }
        }

        private static int? ParseInt(string value) =>
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : (int?)null;
    }
}
