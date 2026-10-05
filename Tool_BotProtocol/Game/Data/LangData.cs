using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace Tool_BotProtocol.Game.Data
{
    /// <summary>
    /// Textes que le client 1.34 lit dans ses fichiers de langue (<c>lang/swf/&lt;famille&gt;_fr_&lt;n&gt;.swf</c>),
    /// convertis par <c>tools/client-analysis/lang2xml.py</c> en <c>ressources/Bot/BotLang/&lt;famille&gt;.xml</c> :
    /// dialogues (<c>D.q</c>/<c>D.a</c>), PNJ (<c>N.d</c>/<c>N.a</c>), cartes et zones (<c>MA</c>), monstres (<c>M</c>),
    /// objets (<c>I</c>), sorts (<c>S</c>), émotes (<c>EM</c>), textes d'interface et messages <c>Im</c> (<c>lang_fr</c>),
    /// objets interactifs (<c>IO</c>), compétences (<c>SK</c>), métiers (<c>J</c>), titres (<c>PT</c>), quêtes (<c>Q</c>).
    /// Le chargement se fait hors du thread de l'interface et remplace l'ensemble d'un seul coup ; les lectures
    /// sont sûres depuis n'importe quel thread. Une famille absente ou illisible ne lève jamais d'exception :
    /// chaque accesseur rend alors l'identifiant demandé sous forme de texte.
    /// </summary>
    public static class LangData
    {
        public static string LangPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ressources", "Bot", "BotLang");
        /// <summary>Fichiers ignorés au dernier chargement (XML illisible, racine inattendue…).</summary>
        public static string[] LoadWarnings { get; private set; } = new string[0];

        private static Snapshot current = Snapshot.Empty;

        /// <summary>Nombre total d'entrées chargées (toutes familles).</summary>
        public static int EntryCount => Volatile.Read(ref current).EntryCount;
        /// <summary>Familles chargées (<c>dialog</c>, <c>npc</c>, <c>maps</c>…).</summary>
        public static string[] Families => Volatile.Read(ref current).Families.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        public static bool IsLoaded(string family) => family != null && Volatile.Read(ref current).Families.ContainsKey(family);
        /// <summary>Numéro du SWF de langue d'origine (attribut <c>version</c>), ou <c>null</c>.</summary>
        public static string Version(string family) => Volatile.Read(ref current).Families.TryGetValue(family ?? string.Empty, out FamilyData data) ? data.Version : null;
        /// <summary>Identifiants d'une table (ordre ordinal), vide si la famille ou la table est absente.</summary>
        public static string[] Ids(string family, string table)
        {
            Dictionary<string, Entry> rows = Rows(family, table);
            return rows == null ? new string[0] : rows.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        }
        public static int Count(string family, string table)
        {
            Dictionary<string, Entry> rows = Rows(family, table);
            return rows == null ? 0 : rows.Count;
        }
        /// <summary>
        /// Attributs bruts d'une entrée, pour les familles exportées telles quelles (<c>&lt;entree table="H.h" id="66"&gt;</c>)
        /// ou les champs sans accesseur dédié. <c>null</c> si l'entrée est absente.
        /// </summary>
        public static IReadOnlyDictionary<string, string> Raw(string family, string table, string id)
        {
            Entry entry = Find(family, table, id);
            return entry?.ToDictionary();
        }

        public static Task LoadAsync() => Task.Run(() => { Load(LangPath); });

        /// <summary>
        /// Charge tous les <c>*.xml</c> du dossier et remplace les textes en place d'un seul coup.
        /// Un fichier illisible est journalisé dans <see cref="LoadWarnings"/> et sa famille reste absente.
        /// Renvoie le nombre de familles chargées.
        /// </summary>
        public static int Load(string folder)
        {
            var families = new Dictionary<string, FamilyData>(StringComparer.Ordinal);
            var warnings = new List<string>();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                warnings.Add("Dossier des textes du client introuvable : " + folder);
            }
            else
            {
                string[] files;
                try { files = Directory.GetFiles(folder, "*.xml").OrderBy(f => f, StringComparer.Ordinal).ToArray(); }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { files = new string[0]; warnings.Add(folder + " : " + error.Message); }
                foreach (string file in files)
                {
                    try
                    {
                        FamilyData data = ReadFamily(file);
                        if (families.ContainsKey(data.Name)) warnings.Add(Path.GetFileName(file) + " : famille « " + data.Name + " » déjà chargée, ce fichier la remplace.");
                        families[data.Name] = data;
                    }
                    catch (Exception error)
                    {
                        warnings.Add(Path.GetFileName(file) + " : " + error.Message);
                    }
                }
            }
            Volatile.Write(ref current, new Snapshot(families));
            LoadWarnings = warnings.ToArray();
            return families.Count;
        }

        /// <summary>Oublie tous les textes chargés (les accesseurs reviennent aux identifiants).</summary>
        public static void Clear()
        {
            Volatile.Write(ref current, Snapshot.Empty);
            LoadWarnings = new string[0];
        }

        /// <summary>
        /// Substitution des marqueurs d'un texte du client, comme sa fonction <c>getDescription</c> (décodeur de motifs) :
        /// <c>#n</c> = paramètre n (supprimé s'il manque), <c>~n</c> = la suite n'est gardée que si le paramètre n existe,
        /// <c>{…}</c> = bloc décodé récursivement, <c>[n]</c> = paramètre d'indice n suivi d'une espace.
        /// </summary>
        public static string Describe(string pattern, IList<string> parameters)
        {
            if (pattern == null) return null;
            var chars = new List<string>(pattern.Length);
            foreach (char c in pattern) chars.Add(c.ToString());
            return string.Concat(Decode(chars, parameters ?? new string[0]));
        }

        private static List<string> Decode(List<string> p1, IList<string> p2)
        {
            int length = p1.Count; // le client garde la longueur initiale comme borne de la boucle
            for (int i = 0; i < length; i++)
            {
                if (i < 0 || i >= p1.Count) continue;
                switch (p1[i])
                {
                    case "#":
                        {
                            if (!TryDigit(i + 1 < p1.Count ? p1[i + 1] : null, out int n)) break;
                            string value = Param(p2, n - 1);
                            p1.RemoveRange(i, Math.Min(2, p1.Count - i));
                            if (value != null) { p1.Insert(i, value); i -= 1; }
                            else i -= 2;
                            break;
                        }
                    case "~":
                        {
                            if (!TryDigit(i + 1 < p1.Count ? p1[i + 1] : null, out int n)) break;
                            if (Param(p2, n - 1) == null) return p1.GetRange(0, i);
                            p1.RemoveRange(i, Math.Min(2, p1.Count - i));
                            i -= 2;
                            break;
                        }
                    case "{":
                        {
                            int close = p1.IndexOf("}", i);
                            if (close < 0) return p1; // le client n'avance plus après un « { » sans « } »
                            string inner = string.Concat(Decode(p1.GetRange(i + 1, close - i - 1), p2));
                            p1.RemoveRange(i, close - i + 1);
                            p1.Insert(i, inner);
                            break;
                        }
                    case "[":
                        {
                            int close = p1.IndexOf("]", i);
                            if (close < 0) break;
                            if (!int.TryParse(string.Concat(p1.GetRange(i + 1, close - i - 1)), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int n)) break;
                            p1.RemoveRange(i, close - i + 1);
                            p1.Insert(i, (Param(p2, n) ?? string.Empty) + " ");
                            i -= close - i;
                            break;
                        }
                }
            }
            return p1;
        }

        /// <summary>
        /// Accords du client (sa fonction <c>combine</c>) : dans <c>{~ps}</c> ou <c>{~fe}</c>, « ~x » garde la suite si la marque x
        /// est vraie (<c>m</c>/<c>f</c>/<c>n</c> = genre, <c>p</c> = pluriel, <c>s</c> = singulier) et coupe sinon.
        /// Exemple : « Zone{~ps} » donne « Zone » au singulier et « Zones » au pluriel.
        /// </summary>
        public static string Combine(string pattern, string gender, bool singular)
        {
            if (pattern == null) return null;
            var flags = new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                { "m", gender == "m" }, { "f", gender == "f" }, { "n", gender == "n" }, { "p", !singular }, { "s", singular }
            };
            var chars = new List<string>(pattern.Length);
            foreach (char c in pattern) chars.Add(c.ToString());
            return string.Concat(DecodeCombine(chars, flags));
        }

        private static List<string> DecodeCombine(List<string> p1, Dictionary<string, bool> p2)
        {
            int length = p1.Count;
            for (int i = 0; i < length; i++)
            {
                if (i < 0 || i >= p1.Count) continue;
                if (p1[i] == "~")
                {
                    string mark = i + 1 < p1.Count ? p1[i + 1] : null;
                    if (mark == null || !p2.TryGetValue(mark, out bool on) || !on) return p1.GetRange(0, i);
                    p1.RemoveRange(i, 2);
                    i -= 2;
                }
                else if (p1[i] == "{")
                {
                    int close = p1.IndexOf("}", i);
                    if (close < 0) return p1;
                    string inner = string.Concat(DecodeCombine(p1.GetRange(i + 1, close - i - 1), p2));
                    p1.RemoveRange(i, close - i + 1);
                    p1.Insert(i, inner);
                }
            }
            return p1;
        }

        private static bool TryDigit(string text, out int value)
        {
            value = 0;
            return !string.IsNullOrWhiteSpace(text) && int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
        }

        private static string Param(IList<string> parameters, int index) => parameters != null && index >= 0 && index < parameters.Count ? parameters[index] : null;

        /// <summary>Dialogues PNJ (<c>dialog_fr</c> : <c>D.q</c> questions, <c>D.a</c> réponses), affichés après <c>DQ</c>.</summary>
        public static class Dialog
        {
            public static bool HasQuestion(int id) => Find("dialog", "question", Key(id)) != null;
            public static bool HasAnswer(int id) => Find("dialog", "reponse", Key(id)) != null;
            /// <summary>Texte de la question avec les paramètres de <c>DQ&lt;id&gt;;&lt;p1,p2…&gt;</c> substitués (<c>#1</c>…).</summary>
            public static string Question(int id, IList<string> parameters = null)
            {
                string text = Attribute("dialog", "question", Key(id), "texte");
                return text == null ? Key(id) : Text.Fetch(Describe(text, parameters));
            }
            public static string Answer(int id)
            {
                string text = Attribute("dialog", "reponse", Key(id), "texte");
                return text == null ? Key(id) : Text.Fetch(text);
            }
        }

        /// <summary>Personnages non joueurs (<c>npc_fr</c> : <c>N.d[id] = {n, a}</c>, <c>N.a[action]</c>).</summary>
        public static class Npc
        {
            public static bool Has(int id) => Find("npc", "pnj", Key(id)) != null;
            public static string Name(int id) => Attribute("npc", "pnj", Key(id), "nom") ?? Key(id);
            /// <summary>Actions du menu du PNJ (1 Acheter/Vendre, 2 Échanger, 3 Parler…), dans l'ordre du client.</summary>
            public static int[] Actions(int id) => Ints(Attribute("npc", "pnj", Key(id), "actions"));
            public static string ActionName(int actionId) => Attribute("npc", "action", Key(actionId), "nom") ?? Key(actionId);
            public static string[] ActionNames(int id) => Actions(id).Select(ActionName).ToArray();
        }

        /// <summary>Cartes, sous-zones et zones (<c>maps_fr</c> : <c>MA.m</c>, <c>MA.sa</c>, <c>MA.a</c>, <c>MA.sua</c>).</summary>
        public static class Map
        {
            public static bool Has(int mapId) => Find("maps", "carte", Key(mapId)) != null;
            /// <summary>Nom affiché par le client (<c>MapsServersManager.getMapName</c>) : « Zone (Sous-zone) », ou « Zone » si la sous-zone contient « // ».</summary>
            public static string Name(int mapId)
            {
                int? subArea = SubAreaId(mapId);
                if (subArea == null) return Key(mapId);
                Entry sub = Find("maps", "sousZone", Key(subArea.Value));
                string subName = sub?.Get("nom");
                string areaName = AreaName(Int(sub?.Get("zone")) ?? -1, null);
                if (areaName == null) return subName ?? Key(mapId);
                return subName == null || subName.IndexOf("//", StringComparison.Ordinal) >= 0 ? areaName : areaName + " (" + subName + ")";
            }
            public static Point? Coords(int mapId)
            {
                Entry map = Find("maps", "carte", Key(mapId));
                int? x = Int(map?.Get("x")), y = Int(map?.Get("y"));
                return x == null || y == null ? (Point?)null : new Point(x.Value, y.Value);
            }
            public static int? SubAreaId(int mapId) => Int(Attribute("maps", "carte", Key(mapId), "sousZone"));
            public static int? AreaId(int mapId)
            {
                int? subArea = SubAreaId(mapId);
                return subArea == null ? null : Int(Attribute("maps", "sousZone", Key(subArea.Value), "zone"));
            }
            /// <summary>Nom de la zone de la carte (Amakna, Astrub…).</summary>
            public static string Area(int mapId)
            {
                int? area = AreaId(mapId);
                return area == null ? Key(mapId) : AreaName(area.Value);
            }
            /// <summary>Nom brut de la sous-zone de la carte (peut commencer par « // » : sous-zone non affichée par le client).</summary>
            public static string SubArea(int mapId)
            {
                int? subArea = SubAreaId(mapId);
                return subArea == null ? Key(mapId) : SubAreaName(subArea.Value);
            }
            public static string SubAreaName(int subAreaId) => Attribute("maps", "sousZone", Key(subAreaId), "nom") ?? Key(subAreaId);
            public static string AreaName(int areaId) => AreaName(areaId, Key(areaId));
            private static string AreaName(int areaId, string fallback) => Attribute("maps", "zone", Key(areaId), "nom") ?? fallback;
        }

        /// <summary>Niveau et résistances d'un grade de monstre (<c>M[id].g&lt;n&gt; = {l, r}</c>).</summary>
        public sealed class MonsterGrade
        {
            public int Grade { get; internal set; }
            public int Level { get; internal set; }
            /// <summary>Les sept résistances dans l'ordre du client (neutre, terre, feu, eau, air, puis esquives PA/PM).</summary>
            public int[] Resistances { get; internal set; } = new int[0];
        }

        /// <summary>Monstres (<c>monsters_fr</c> : <c>M[id] = {n, g, b, a, k, g1…}</c>).</summary>
        public static class Monster
        {
            public static bool Has(int id) => Find("monsters", "monstre", Key(id)) != null;
            public static string Name(int id) => Attribute("monsters", "monstre", Key(id), "nom") ?? Key(id);
            public static int? Gfx(int id) => Int(Attribute("monsters", "monstre", Key(id), "gfx"));
            /// <summary>Niveau et résistances du grade (1 à 5, parfois 10) ; <c>null</c> si inconnu.</summary>
            public static MonsterGrade Grade(int id, int grade)
            {
                Entry monster = Find("monsters", "monstre", Key(id));
                Entry child = monster?.Child("grade", Key(grade));
                if (child == null) return null;
                return new MonsterGrade { Grade = grade, Level = Int(child.Get("niveau")) ?? 0, Resistances = Ints(child.Get("resistances")) };
            }
        }

        /// <summary>Objets (<c>items_fr</c> : <c>I.u[id] = {n, d, t, g, l…}</c>, <c>I.t[type]</c>).</summary>
        public static class Item
        {
            public static bool Has(int id) => Find("items", "objet", Key(id)) != null;
            /// <summary>Nom d'objet comme le client l'affiche : marqueurs <c>#n</c> remplacés par <c>I.us</c>.</summary>
            public static string Name(int id)
            {
                string name = Attribute("items", "objet", Key(id), "nom");
                return name == null ? Key(id) : Describe(Text.Fetch(name), UniqueStrings());
            }
            public static string Description(int id)
            {
                string text = Attribute("items", "objet", Key(id), "description");
                return text == null ? Key(id) : Describe(Text.Fetch(text), UniqueStrings());
            }
            public static int? Type(int id) => Int(Attribute("items", "objet", Key(id), "type"));
            /// <summary>Numéro du SWF d'icône : <c>clips/items/&lt;type&gt;/&lt;gfx&gt;.swf</c>.</summary>
            public static int? Gfx(int id) => Int(Attribute("items", "objet", Key(id), "gfx"));
            public static int? Level(int id) => Int(Attribute("items", "objet", Key(id), "niveau"));
            public static string TypeName(int typeId) => Attribute("items", "type", Key(typeId), "nom") ?? Key(typeId);
            private static IList<string> UniqueStrings()
            {
                Dictionary<string, Entry> rows = Rows("items", "texteUnique");
                if (rows == null) return new string[0];
                int max = rows.Keys.Select(k => Int(k) ?? -1).DefaultIfEmpty(-1).Max();
                var values = new string[max + 1];
                foreach (KeyValuePair<string, Entry> row in rows) { int? i = Int(row.Key); if (i >= 0) values[i.Value] = row.Value.Get("texte"); }
                return values;
            }
        }

        /// <summary>Sorts (<c>spells_fr</c> : nom et description ; les niveaux restent ceux de <c>BotSorts</c>).</summary>
        public static class Spell
        {
            public static bool Has(int id) => Find("spells", "sort", Key(id)) != null;
            public static string Name(int id) => Attribute("spells", "sort", Key(id), "nom") ?? Key(id);
            public static string Description(int id) => Attribute("spells", "sort", Key(id), "description") ?? Key(id);
        }

        /// <summary>Émotes (<c>emotes_fr</c> : <c>EM[id] = {n, s}</c> ; <c>s</c> = commande du chat, <c>/sit</c>…).</summary>
        public static class Emote
        {
            public static string Name(int id) => Attribute("emotes", "emote", Key(id), "nom") ?? Key(id);
            public static string Command(int id) => Attribute("emotes", "emote", Key(id), "commande") ?? Key(id);
            /// <summary><c>getEmoteID</c> du client : identifiant de l'émote dont la commande est <paramref name="command"/>.</summary>
            public static int? IdFromCommand(string command)
            {
                Dictionary<string, Entry> rows = Rows("emotes", "emote");
                if (rows == null || command == null) return null;
                foreach (KeyValuePair<string, Entry> row in rows)
                    if (string.Equals(row.Value.Get("commande"), command, StringComparison.Ordinal)) return Int(row.Key);
                return null;
            }
            public static int[] Ids => Rows("emotes", "emote")?.Keys.Select(k => Int(k) ?? -1).Where(i => i >= 0).OrderBy(i => i).ToArray() ?? new int[0];
        }

        /// <summary>Smileys du chat : le volet du client propose les fichiers 1 à 15 de <c>clips/smileys</c> (aucun fichier de langue).</summary>
        public static class Smiley
        {
            public const int Count = 15;
        }

        /// <summary>Canal d'affichage d'un message <c>Im</c> (variable <c>r6</c> de <c>dofus.aks.Infos.onMessage</c>).</summary>
        public enum ImChannel { Info, Error, Pvp, Whisper, Message }

        /// <summary>Résultat de <see cref="Text.FormatIm"/> : texte prêt à afficher et canal du client.</summary>
        public sealed class ImMessage
        {
            public ImChannel Channel { get; internal set; }
            public string Text { get; internal set; }
            /// <summary>Vrai pour <c>INFOS_82</c>/<c>INFOS_83</c>, que le client montre aussi dans une boîte d'information.</summary>
            public bool Popup { get; internal set; }
            /// <summary>Clés de <c>lang_fr</c> utilisées (<c>INFOS_54</c>…), dans l'ordre.</summary>
            public string[] Keys { get; internal set; } = new string[0];
        }

        /// <summary>Textes d'interface et messages du serveur (<c>lang_fr</c>).</summary>
        public static class Text
        {
            public static bool Has(string key) => key != null && Find("lang", "texte", key) != null;
            /// <summary>
            /// <c>Lang.getText</c> : valeur de la clé avec <c>%1</c>…<c>%n</c> remplacés dans l'ordre ; « !CLÉ! » si la clé
            /// manque comme dans le client ; la clé (suivie des paramètres) si <c>lang.xml</c> n'est pas chargé.
            /// </summary>
            public static string Get(string key, params string[] args)
            {
                if (key == null) return string.Empty;
                if (!IsLoaded("lang")) return args == null || args.Length == 0 ? key : key + " : " + string.Join(", ", args);
                string value = Attribute("lang", "texte", key, "valeur");
                if (string.IsNullOrEmpty(value)) return "!" + key + "!";
                if (args != null)
                    for (int i = 0; i < args.Length; i++) value = value.Replace("%" + (i + 1).ToString(CultureInfo.InvariantCulture), args[i] ?? string.Empty);
                return value;
            }

            /// <summary>
            /// Un message <c>Im</c> : <paramref name="type"/> 0 = <c>INFOS_</c>, 1 = <c>ERROR_</c>, 2 = <c>PVP_</c> ; <paramref name="id"/> tel que reçu
            /// (« 054 » devient <c>INFOS_54</c>) ; les paramètres séparés par <c>~</c>. Les identifiants d'objets, métiers,
            /// sorts, quêtes et zones sont remplacés par leur nom comme dans <c>dofus.aks.Infos.onMessage</c>.
            /// </summary>
            public static string Im(int type, string id, IList<string> args)
            {
                return Segment(type, id, args, out _, out _, out _);
            }

            /// <summary>
            /// Corps complet d'un paquet <c>Im</c> (sans les deux lettres) : <c>&lt;type&gt;&lt;id&gt;[;a~b]|&lt;id&gt;…</c>.
            /// Les messages sont joints par une espace comme dans le client ; <c>null</c> si rien n'est à afficher
            /// (type inconnu ou corps vide). Ne lève jamais d'exception.
            /// </summary>
            public static ImMessage FormatIm(string body)
            {
                if (string.IsNullOrEmpty(body)) return null;
                int type = body[0] - '0';
                if (type < 0 || type > 2) return null;
                var texts = new List<string>();
                var keys = new List<string>();
                ImChannel channel = type == 0 ? ImChannel.Info : type == 1 ? ImChannel.Error : ImChannel.Pvp;
                bool popup = false;
                foreach (string segment in body.Substring(1).Split('|'))
                {
                    // Comme le client : id = avant le premier « ; », paramètres = entre le premier et le second « ; », séparés par « ~ ».
                    string[] fields = segment.Split(';');
                    string id = fields[0];
                    if (id.Length == 0) continue;
                    string[] args = fields.Length > 1 ? fields[1].Split('~') : new string[0];
                    string text = Segment(type, id, args, out string key, out ImChannel segmentChannel, out bool segmentPopup);
                    channel = segmentChannel;
                    popup |= segmentPopup;
                    if (key != null) keys.Add(key);
                    if (text != null) texts.Add(text);
                }
                string joined = string.Join(" ", texts);
                return joined.Length == 0 ? null : new ImMessage { Channel = channel, Text = joined, Popup = popup, Keys = keys.ToArray() };
            }

            private static string Segment(int type, string id, IList<string> args, out string key, out ImChannel channel, out bool popup)
            {
                popup = false;
                key = null;
                channel = type == 0 ? ImChannel.Info : type == 1 ? ImChannel.Error : ImChannel.Pvp;
                if (type < 0 || type > 2) return null;
                var a = new List<string>(args ?? new string[0]);
                if (!TryNumber(id, out int n))
                {
                    key = id ?? string.Empty;
                    return Get(key, a.ToArray());
                }
                string Arg(int i) => i < a.Count ? a[i] : null;
                int? ArgInt(int i) => Int(Arg(i));
                if (type == 0)
                {
                    key = "INFOS_" + n.ToString(CultureInfo.InvariantCulture);
                    switch (n)
                    {
                        case 21: case 22:
                            if (a.Count > 1) a = new List<string> { a[0], ItemNameOf(Arg(1)) };
                            break;
                        case 17:
                            a = new List<string> { Arg(0), JobNameOf(Arg(1)) };
                            break;
                        case 2:
                            a = new List<string> { JobNameOf(Arg(0)) };
                            break;
                        case 3:
                            a = new List<string> { ArgInt(0) is int spell ? LangData.Spell.Name(spell) : Arg(0) };
                            break;
                        case 54: case 55: case 56:
                            if (a.Count > 0 && Int(a[0]) is int quest) a[0] = Quest.Name(quest);
                            break;
                        case 65: case 73:
                            if (a.Count > 1) { while (a.Count < 3) a.Add(null); a[2] = ItemNameOf(a[1]); }
                            break;
                        case 82: case 83:
                            popup = true;
                            break;
                        case 123:
                            return InlineItems(Get(key), a);
                        case 150:
                            channel = ImChannel.Message;
                            a = new List<string> { ItemNameOf(Arg(0)), Arg(1), Get("OBJECT_CHAT_" + Arg(2), a.Skip(3).ToArray()) };
                            break;
                        case 151:
                            channel = ImChannel.Whisper;
                            a = new List<string> { ItemNameOf(Arg(0)), Get("OBJECT_CHAT_" + Arg(1), a.Skip(2).ToArray()) };
                            break;
                    }
                }
                else if (type == 1)
                {
                    key = "ERROR_" + n.ToString(CultureInfo.InvariantCulture);
                    switch (n)
                    {
                        case 6: case 46: case 49:
                            a = new List<string> { JobNameOf(Arg(0)) };
                            break;
                        case 7:
                            a = new List<string> { ArgInt(0) is int spell ? LangData.Spell.Name(spell) : Arg(0) };
                            break;
                    }
                }
                else
                {
                    key = "PVP_" + n.ToString(CultureInfo.InvariantCulture);
                    if (n == 41) a = new List<string> { ArgInt(0) is int sub ? LangData.Map.SubAreaName(sub) : Arg(0), ArgInt(1) is int area ? LangData.Map.AreaName(area) : Arg(1) };
                    else if (n >= 86 && n <= 90 && a.Count > 0 && Int(a[0]) is int area) a[0] = LangData.Map.AreaName(area);
                }
                return Get(key, a.Select(s => s ?? string.Empty).ToArray());
            }

            /// <summary><c>ChatManager.parseInlineItems</c> : « °k » devient le nom de l'objet de la paire k (identifiant, effets).</summary>
            private static string InlineItems(string text, IList<string> args)
            {
                for (int i = 0; i < args.Count; i += 2)
                {
                    string marker = "°" + (i / 2).ToString(CultureInfo.InvariantCulture);
                    int at = text.IndexOf(marker, StringComparison.Ordinal);
                    if (at >= 0) text = text.Substring(0, at) + "[" + ItemNameOf(args[i]) + "]" + text.Substring(at + marker.Length);
                }
                return text;
            }

            private static string ItemNameOf(string id) => Int(id) is int item ? LangData.Item.Name(item) : id;
            private static string JobNameOf(string id) => Int(id) is int job ? LangData.Job.Name(job) : id;

            private static bool TryNumber(string id, out int value)
            {
                value = 0;
                if (string.IsNullOrWhiteSpace(id)) return false;
                if (int.TryParse(id.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value)) return true;
                if (!double.TryParse(id.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) || d != Math.Floor(d) || Math.Abs(d) > int.MaxValue) return false;
                value = (int)d;
                return true;
            }

            /// <summary>
            /// <c>Lang.fetchString</c> : remplace <c>`SRVT:&lt;clé&gt;`</c> par le texte propre au serveur (<c>servers_fr</c>, <c>SRVC</c>)
            /// ou, à défaut, par le texte commun (<c>SRVT[i].d</c>). Sans <c>servers.xml</c>, le texte est rendu tel quel.
            /// </summary>
            public static string Fetch(string text, int serverId = 0)
            {
                if (string.IsNullOrEmpty(text) || text.IndexOf("`SRVT:", StringComparison.Ordinal) < 0) return text;
                Dictionary<string, Entry> rows = Rows("servers", "SRVT");
                if (rows == null) return text;
                foreach (KeyValuePair<string, Entry> row in rows)
                {
                    string label = row.Value.Get("l");
                    if (label == null) continue;
                    string value = Attribute("servers", "SRVC", row.Key + "|" + serverId.ToString(CultureInfo.InvariantCulture), "valeur") ?? row.Value.Get("d") ?? string.Empty;
                    text = text.Replace("`SRVT:" + label + "`", value);
                }
                return text;
            }

            /// <summary>Texte du client sans HTML (<c>&lt;b&gt;</c>, <c>&lt;font&gt;</c>, liens), entités décodées, <c>&lt;br&gt;</c> en saut de ligne.</summary>
            public static string Plain(string html)
            {
                if (string.IsNullOrEmpty(html)) return html ?? string.Empty;
                string text = Regex.Replace(html, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);
                text = Regex.Replace(text, "<[^>]*>", string.Empty);
                return System.Net.WebUtility.HtmlDecode(text);
            }
        }

        /// <summary>Objets interactifs (<c>interactiveobjects_fr</c> : <c>IO.g[gfx] = id</c>, <c>IO.d[id] = {n, t, sk}</c>), par gfx de la couche objet 2.</summary>
        public static class Interactive
        {
            public static int? IdFromGfx(int gfx) => Int(Attribute("interactiveobjects", "gfx", Key(gfx), "interactif"));
            public static string Name(int gfx)
            {
                int? id = IdFromGfx(gfx);
                return id == null ? Key(gfx) : NameById(id.Value);
            }
            public static int[] Skills(int gfx)
            {
                int? id = IdFromGfx(gfx);
                return id == null ? new int[0] : SkillsById(id.Value);
            }
            /// <summary>Type de l'objet interactif (<c>IO.d[id].t</c> : 1 ressource, 2 atelier…), ou <c>null</c>.</summary>
            public static int? Type(int gfx)
            {
                int? id = IdFromGfx(gfx);
                return id == null ? null : Int(Attribute("interactiveobjects", "interactif", Key(id.Value), "type"));
            }
            public static string NameById(int id) => Attribute("interactiveobjects", "interactif", Key(id), "nom") ?? Key(id);
            public static int[] SkillsById(int id) => Ints(Attribute("interactiveobjects", "interactif", Key(id), "competences"));
        }

        /// <summary>Compétences (<c>skills_fr</c> : <c>SK[id] = {d, j, io, c, f}</c>), celles de <c>GA500&lt;cellule&gt;;&lt;compétence&gt;</c>.</summary>
        public static class Skill
        {
            public static string Name(int id) => Attribute("skills", "competence", Key(id), "nom") ?? Key(id);
            public static int? Job(int id) => Int(Attribute("skills", "competence", Key(id), "metier"));
            public static int? InteractiveId(int id) => Int(Attribute("skills", "competence", Key(id), "interactif"));
        }

        /// <summary>Métiers (<c>jobs_fr</c> : <c>J[id] = {n, s, g}</c>).</summary>
        public static class Job
        {
            public static string Name(int id) => Attribute("jobs", "metier", Key(id), "nom") ?? Key(id);
            /// <summary>Numéro du SWF d'icône : <c>clips/jobs/&lt;g&gt;.swf</c>.</summary>
            public static int? Icon(int id) => Int(Attribute("jobs", "metier", Key(id), "icone"));
        }

        /// <summary>Titres (<c>titles_fr</c> : <c>PT[id] = {t, c, pt}</c>).</summary>
        public static class Title
        {
            /// <summary>Texte affiché sous le nom, comme le client : « « Titre » », <c>%1</c> remplacé par le paramètre (nom de monstre si <c>pt</c> = 1).</summary>
            public static string Name(int id, string param = null)
            {
                Entry title = Find("titles", "titre", Key(id));
                string text = title?.Get("texte");
                if (text == null) return Key(id);
                string value = param ?? string.Empty;
                if (Int(title.Get("typeParametre")) == 1 && Int(param) is int monster) value = Monster.Name(monster);
                return "« " + text.Replace("%1", value) + " »";
            }
            /// <summary>Couleur RVB du titre (<c>PT[id].c</c>), ou <c>null</c>.</summary>
            public static int? Color(int id) => Int(Attribute("titles", "titre", Key(id), "couleur"));
        }

        /// <summary>Quêtes (<c>quests_fr</c> : <c>Q.q</c> noms, <c>Q.s</c> étapes, <c>Q.o</c> objectifs, <c>Q.t</c> modèles d'objectif).</summary>
        public static class Quest
        {
            public static string Name(int id) => Attribute("quests", "quete", Key(id), "nom") ?? Key(id);
            public static string StepName(int id) => Attribute("quests", "etape", Key(id), "nom") ?? Key(id);
            public static string StepDescription(int id) => Attribute("quests", "etape", Key(id), "description") ?? Key(id);
            /// <summary>Texte d'un objectif de quête comme le client le compose : modèle <c>Q.t[type]</c> et paramètres résolus (PNJ, objet, monstre, sous-zone).</summary>
            public static string Objective(int id)
            {
                Entry objective = Find("quests", "objectif", Key(id));
                int? type = Int(objective?.Get("type"));
                if (type == null) return Key(id);
                string template = Attribute("quests", "modele", Key(type.Value), "texte");
                if (template == null) return Key(id);
                string[] p = Strings(objective.Get("parametres"));
                string P(int i) => i < p.Length ? p[i] : null;
                string NpcName(int i) => Int(P(i)) is int npc ? Attribute("npc", "pnj", Key(npc), "nom") : null;
                string ItemRaw(int i) => Int(P(i)) is int item ? Attribute("items", "objet", Key(item), "nom") : null;
                string MonsterRaw(int i) => Int(P(i)) is int monster ? Attribute("monsters", "monstre", Key(monster), "nom") : null;
                string[] values;
                switch (type.Value)
                {
                    case 0: case 4: values = new[] { P(0) }; break;
                    case 1: case 9: case 10: values = new[] { NpcName(0) }; break;
                    case 2: case 3: values = new[] { NpcName(0), ItemRaw(1), P(2) }; break;
                    case 5: values = new[] { Int(P(0)) is int sub ? Attribute("maps", "sousZone", Key(sub), "nom") : null }; break;
                    case 6: case 7: values = new[] { MonsterRaw(0), P(1) }; break;
                    case 8: values = new[] { ItemRaw(0) }; break;
                    case 12: values = new[] { NpcName(0), MonsterRaw(1), P(2) }; break;
                    default: values = new string[0]; break;
                }
                return Describe(template, values);
            }
        }

        // -----------------------------------------------------------------
        // Stockage
        // -----------------------------------------------------------------

        private sealed class Entry
        {
            internal string Element;
            internal string Id;
            internal KeyValuePair<string, string>[] Attributes;
            internal Entry[] Children;

            internal string Get(string name)
            {
                foreach (KeyValuePair<string, string> pair in Attributes)
                    if (string.Equals(pair.Key, name, StringComparison.Ordinal)) return pair.Value;
                return null;
            }

            internal Entry Child(string element, string n)
            {
                foreach (Entry child in Children)
                    if (child.Element == element && string.Equals(child.Get("n"), n, StringComparison.Ordinal)) return child;
                return null;
            }

            internal IReadOnlyDictionary<string, string> ToDictionary()
            {
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, string> pair in Attributes) values[pair.Key] = pair.Value;
                foreach (Entry child in Children)
                    if (child.Element == "champ" && child.Get("cle") != null) values[child.Get("cle")] = child.Get("valeur");
                return values;
            }
        }

        private sealed class FamilyData
        {
            internal string Name;
            internal string Version;
            internal readonly Dictionary<string, Dictionary<string, Entry>> Tables = new Dictionary<string, Dictionary<string, Entry>>(StringComparer.Ordinal);
            internal int Count;
        }

        private sealed class Snapshot
        {
            internal static readonly Snapshot Empty = new Snapshot(new Dictionary<string, FamilyData>(StringComparer.Ordinal));
            internal readonly Dictionary<string, FamilyData> Families;
            internal readonly int EntryCount;
            internal Snapshot(Dictionary<string, FamilyData> families)
            {
                Families = families;
                EntryCount = families.Values.Sum(f => f.Count);
            }
        }

        private static readonly Entry[] NoChildren = new Entry[0];

        private static FamilyData ReadFamily(string file)
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true, IgnoreProcessingInstructions = true };
            XDocument document;
            using (var stream = File.OpenRead(file))
            using (XmlReader reader = XmlReader.Create(stream, settings))
                document = XDocument.Load(reader);
            XElement root = document.Root;
            if (root == null || root.Name.LocalName != "BotLang") throw new FormatException("racine <BotLang> attendue.");
            var data = new FamilyData
            {
                Name = (string)root.Attribute("famille") ?? Path.GetFileNameWithoutExtension(file),
                Version = (string)root.Attribute("version")
            };
            foreach (XElement element in root.Elements())
            {
                Entry entry = ToEntry(element);
                string table = element.Name.LocalName == "entree" ? entry.Get("table") ?? "entree" : element.Name.LocalName;
                if (entry.Id == null) continue;
                if (!data.Tables.TryGetValue(table, out Dictionary<string, Entry> rows))
                    data.Tables[table] = rows = new Dictionary<string, Entry>(StringComparer.Ordinal);
                if (!rows.ContainsKey(entry.Id)) data.Count++;
                rows[entry.Id] = entry;
            }
            return data;
        }

        private static Entry ToEntry(XElement element)
        {
            var attributes = element.Attributes().Where(a => !a.IsNamespaceDeclaration)
                .Select(a => new KeyValuePair<string, string>(string.Intern(a.Name.LocalName), a.Value)).ToArray();
            var entry = new Entry
            {
                Element = string.Intern(element.Name.LocalName),
                Attributes = attributes,
                Children = element.HasElements ? element.Elements().Select(ToEntry).ToArray() : NoChildren
            };
            entry.Id = entry.Get("id") ?? entry.Get("cle");
            return entry;
        }

        private static Dictionary<string, Entry> Rows(string family, string table)
        {
            if (family == null || table == null) return null;
            if (!Volatile.Read(ref current).Families.TryGetValue(family, out FamilyData data)) return null;
            return data.Tables.TryGetValue(table, out Dictionary<string, Entry> rows) ? rows : null;
        }

        private static Entry Find(string family, string table, string id)
        {
            Dictionary<string, Entry> rows = Rows(family, table);
            return rows != null && id != null && rows.TryGetValue(id, out Entry entry) ? entry : null;
        }

        private static string Attribute(string family, string table, string id, string name) => Find(family, table, id)?.Get(name);

        private static string Key(int id) => id.ToString(CultureInfo.InvariantCulture);

        private static int? Int(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (int.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value)) return value;
            return null;
        }

        /// <summary>« 1,3 » → {1, 3} ; les morceaux non numériques sont ignorés.</summary>
        private static int[] Ints(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new int[0];
            return text.Split(',').Select(Int).Where(v => v.HasValue).Select(v => v.Value).ToArray();
        }

        /// <summary>Liste écrite par lang2xml.py : « a,b,c » (nombres) ou tableau JSON <c>["…", 5]</c>.</summary>
        private static string[] Strings(string text)
        {
            if (string.IsNullOrEmpty(text)) return new string[0];
            if (!text.StartsWith("[", StringComparison.Ordinal)) return text.Split(',');
            try
            {
                var array = Newtonsoft.Json.Linq.JArray.Parse(text);
                return array.Select(t => t.Type == Newtonsoft.Json.Linq.JTokenType.Null ? null : t.Type == Newtonsoft.Json.Linq.JTokenType.String ? (string)t : t.ToString(Newtonsoft.Json.Formatting.None)).ToArray();
            }
            catch (Newtonsoft.Json.JsonException) { return new string[0]; }
        }
    }
}
