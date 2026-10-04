using System;
using System.Globalization;
using System.Linq;
using Tool_BotProtocol.Game.Data;

namespace Tool_BotProtocol.Game.Social
{
    /// <summary>Liste concernée : amis (<c>F…</c>) ou ennemis (<c>i…</c>), les deux onglets de la fenêtre <c>Friends</c> du client.</summary>
    public enum FriendListKind
    {
        Friends,
        Enemies,
    }

    /// <summary>
    /// État d'une entrée, deuxième champ de la ligne (<c>getFriendObjectFromData</c> du client) : absent = hors ligne
    /// (<c>DISCONNECT</c>), « 1 » = <c>IN_SOLO</c>, « 2 » = <c>IN_MULTI</c> (en combat), « ? » ou autre = <c>IN_UNKNOW</c>.
    /// StarLoco envoie toujours « ? » pour un joueur connecté (<c>Player.parseToFriendList</c>).
    /// </summary>
    public enum FriendState
    {
        Disconnected,
        Solo,
        InFight,
        Unknown,
    }

    /// <summary>
    /// Une ligne de liste d'amis ou d'ennemis, au format lu par <c>dofus.aks.Friends.getFriendObjectFromData</c> (et son
    /// double <c>Enemies.getEnemyObjectFromData</c>) : <c>compte[;état;nom;niveau;alignement;guilde;sexe;gfx]</c>. Un compte
    /// seul est un joueur hors ligne (le nom affiché est alors le compte). StarLoco (<c>Account.parseFriendList</c>) met le
    /// pseudo du compte en tête et, pour un joueur connecté, <c>;?;nom;niveau;alignement;classe;sexe;gfx</c> : niveau « ? » et
    /// alignement -1 quand ce joueur n'a pas le personnage dans sa propre liste d'amis. Le sixième champ, lu <c>guild</c> par
    /// le client (qui ne l'affiche pas), y porte la classe. Immuable.
    /// </summary>
    public sealed class FriendEntry
    {
        private FriendEntry() { }

        /// <summary>Pseudo du compte (premier champ), ou <c>null</c> quand il n'est pas fiable (<c>iAK</c> de StarLoco).</summary>
        public string Account { get; private set; }
        /// <summary>Nom du personnage connecté ; le compte pour un joueur hors ligne (comme le client).</summary>
        public string Name { get; private set; }
        public FriendState State { get; private set; }
        public bool IsOnline => State != FriendState.Disconnected;
        /// <summary>« Dans un combat » : la petite épée de la ligne du client (<c>_mcFight</c>, état <c>IN_MULTI</c>).</summary>
        public bool IsInFight => State == FriendState.InFight;
        /// <summary>Niveau, ou <c>null</c> s'il est inconnu (« ? » : le joueur n'a pas le personnage dans ses amis).</summary>
        public int? Level { get; private set; }
        /// <summary>Texte du niveau tel que le client l'affiche (« ? » compris), vide hors ligne.</summary>
        public string LevelText { get; private set; } = string.Empty;
        /// <summary>Alignement (0 neutre, 1 Bonta, 2 Brâkmar, 3 mercenaire…), -1 si inconnu : pas d'icône dans le client.</summary>
        public int Alignment { get; private set; } = -1;
        /// <summary>Sixième champ brut (<c>guild</c> chez le client ; la classe chez StarLoco), jamais affiché.</summary>
        public string Guild { get; private set; } = string.Empty;
        /// <summary>Sexe (0 ou 1), ou <c>null</c>.</summary>
        public int? Sex { get; private set; }
        /// <summary>Apparence (classe × 10 + sexe) : petite illustration <c>artworks/mini/&lt;gfx&gt;</c> de la ligne, ou <c>null</c>.</summary>
        public int? Gfx { get; private set; }
        /// <summary>
        /// Entrée incomplète : seul le nom est retenu (<c>iAK</c> de StarLoco, dont les champs suivants sont des constantes et le
        /// compte un nom de compte, <c>SocketManager.GAME_SEND_ADD_ENEMY</c>). La prochaine liste <c>iL</c> la remplace.
        /// </summary>
        public bool IsPartial { get; private set; }

        /// <summary>
        /// Cible du bouton « retirer » de la ligne du client : <c>*&lt;compte&gt;</c> si le compte est connu
        /// (<c>removeFriend("*" + account)</c>), sinon le nom du personnage.
        /// </summary>
        public string RemoveTarget => Account != null ? "*" + Account : Name;

        /// <summary>
        /// Lit une ligne comme le client ; <c>false</c> (sans exception) si le compte est vide. Un champ illisible est laissé
        /// inconnu, sans faire échouer la ligne.
        /// </summary>
        public static bool TryParse(string data, out FriendEntry entry)
        {
            entry = null;
            if (string.IsNullOrEmpty(data)) return false;
            string[] fields = data.Split(';');
            string account = fields[0];
            if (account.Length == 0) return false;
            var result = new FriendEntry { Account = account };
            if (fields.Length < 2)
            {
                result.Name = account;
                result.State = FriendState.Disconnected;
                entry = result;
                return true;
            }
            switch (fields[1])
            {
                case "1": result.State = FriendState.Solo; break;
                case "2": result.State = FriendState.InFight; break;
                default: result.State = FriendState.Unknown; break;
            }
            string name = Field(fields, 2);
            result.Name = name.Length > 0 ? name : account;
            result.LevelText = Field(fields, 3);
            result.Level = TryInt(result.LevelText, out int level) && level >= 0 ? level : (int?)null;
            result.Alignment = TryInt(Field(fields, 4), out int alignment) && alignment >= 0 ? alignment : -1;
            result.Guild = Field(fields, 5);
            result.Sex = TryInt(Field(fields, 6), out int sex) && (sex == 0 || sex == 1) ? sex : (int?)null;
            result.Gfx = TryInt(Field(fields, 7), out int gfx) && gfx > 0 ? gfx : (int?)null;
            entry = result;
            return true;
        }

        /// <summary>
        /// Ligne de <c>iAK</c> (ennemi ajouté). StarLoco l'écrit <c>&lt;nom de compte&gt;;2;&lt;nom&gt;;36;10;0;100.FL.</c>
        /// (<c>SM:2025</c>, matrice §2 n° 38) : quand le sexe et le gfx ne se lisent pas, seul le nom du personnage est gardé
        /// (<see cref="IsPartial"/>) ; une ligne complète d'un autre serveur est lue normalement.
        /// </summary>
        public static bool TryParseAddedEnemy(string data, out FriendEntry entry)
        {
            if (!TryParse(data, out entry)) return false;
            if (!entry.IsOnline || (entry.Sex.HasValue && entry.Gfx.HasValue)) return true;
            string name = (data ?? string.Empty).Split(';').ElementAtOrDefault(2) ?? string.Empty;
            if (name.Length == 0) { entry = null; return false; }
            entry = new FriendEntry { Account = null, Name = name, State = FriendState.Unknown, IsPartial = true };
            return true;
        }

        /// <summary>Même joueur (compte connu identique, sinon même nom), sans tenir compte de la casse comme StarLoco.</summary>
        public bool SamePlayer(FriendEntry other)
        {
            if (other == null) return false;
            if (Account != null && other.Account != null) return string.Equals(Account, other.Account, StringComparison.OrdinalIgnoreCase);
            return string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);
        }

        private static string Field(string[] fields, int index) => index < fields.Length ? fields[index] ?? string.Empty : string.Empty;

        private static bool TryInt(string text, out int value) =>
            int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// Conjoint, paquet <c>FS</c> (<c>Friends.onSpouse</c> du client) : <c>nom|gfx|c1|c2|c3|carte|niveau|combat|suivi</c>.
    /// StarLoco (<c>Player.get_wife_friendlist</c>) n'envoie <c>FS</c> qu'avec <c>FL</c>, laisse carte, niveau et combat vides
    /// hors ligne, n'envoie jamais le champ « suivi » et écrit le gfx comme la somme classe + sexe (et non classe × 10 + sexe) :
    /// l'illustration lue par le client (<c>artworks/faces/&lt;gfx&gt;</c>) est donc souvent absente ou fausse. Immuable.
    /// </summary>
    public sealed class SpouseInfo
    {
        private SpouseInfo() { }

        public string Name { get; private set; }
        public int? Gfx { get; private set; }
        public int[] Colors { get; private set; } = new int[0];
        /// <summary>Carte du conjoint connecté, sinon <c>null</c>.</summary>
        public int? MapId { get; private set; }
        /// <summary>Connecté : carte lisible, comme <c>!isNaN(mapID)</c> du client.</summary>
        public bool IsConnected => MapId.HasValue;
        public int? Level { get; private set; }
        public bool IsInFight { get; private set; }
        /// <summary>Suivi de ses déplacements (<c>FJC+</c>) : neuvième champ, ou état local après un envoi.</summary>
        public bool IsFollowed { get; private set; }
        /// <summary>« f » ou « m » pour les accords des textes (<c>SPOUSE</c> = « Conjoint{~fe} ») : l'inverse du sexe du personnage.</summary>
        public string Sex { get; private set; } = "m";

        /// <summary>Nom de la carte du conjoint (<c>MapsServersManager.getMapName</c>), vide hors ligne.</summary>
        public string Area => MapId.HasValue ? LangData.Map.Name(MapId.Value) : string.Empty;

        /// <summary>Lit le corps de <c>FS</c> (sans le préfixe) ; <c>false</c> si le nom est vide (<c>FS|</c> : conjoint introuvable).</summary>
        public static bool TryParse(string body, int characterSex, out SpouseInfo spouse)
        {
            spouse = null;
            string[] fields = (body ?? string.Empty).Split('|');
            if (fields[0].Length == 0) return false;
            spouse = new SpouseInfo
            {
                Name = fields[0],
                Gfx = Int(fields, 1),
                Colors = new[] { Int(fields, 2) ?? -1, Int(fields, 3) ?? -1, Int(fields, 4) ?? -1 },
                MapId = Int(fields, 5),
                Level = Int(fields, 6),
                IsInFight = Text(fields, 7) == "1",
                IsFollowed = Text(fields, 8) == "1",
                Sex = characterSex == 0 ? "f" : "m",
            };
            return true;
        }

        /// <summary>Copie avec un autre état de suivi (le client bascule <c>isFollow</c> à chaque clic, sans réponse du serveur).</summary>
        public SpouseInfo WithFollow(bool followed)
        {
            var copy = (SpouseInfo)MemberwiseClone();
            copy.IsFollowed = followed;
            return copy;
        }

        private static string Text(string[] fields, int index) => index < fields.Length ? fields[index] : string.Empty;

        private static int? Int(string[] fields, int index) =>
            int.TryParse(Text(fields, index), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value) ? value : (int?)null;
    }

    /// <summary>Textes du client pour les amis (fichier de langue), avec repli quand il n'est pas chargé ; jamais d'exception.</summary>
    public static class FriendsTexts
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

        /// <summary>Texte avec accords (<c>{~fe}</c>…) selon le genre « m » ou « f », comme <c>PatternDecoder.combine</c>.</summary>
        public static string Gendered(string key, string fallback, string gender) =>
            LangData.Combine(Get(key, fallback), gender, true) ?? fallback;

        public static string Title(FriendListKind kind) =>
            kind == FriendListKind.Enemies ? Get("ENEMIES", "Ennemis") : Get("FRIENDS", "Amis");

        /// <summary>État affiché par le client après le niveau (« dans le village. », « dans un combat. », « Quelque part ! »).</summary>
        public static string State(FriendState state)
        {
            switch (state)
            {
                case FriendState.Solo: return Get("IN_SOLO", "dans le village.");
                case FriendState.InFight: return Get("IN_MULTI", "dans un combat.");
                case FriendState.Disconnected: return Get("OFFLINE", "Non connecté");
                default: return Get("IN_UNKNOW", "Quelque part !");
            }
        }
    }
}
