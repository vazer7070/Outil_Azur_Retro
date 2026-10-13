using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Game.Data;

namespace Outil_Azur_complet.Bot
{
    /// <summary>Un raccourci du client : nom (<c>CHARAC</c>, <c>SH3</c>…), description, catégorie et touches en vigueur.</summary>
    public sealed class ShortcutDefinition
    {
        internal ShortcutDefinition(string name, string description, string category, ShortcutKey key, ShortcutKey? alternate, ShortcutKey clientKey, bool handled,
            bool outsideChatOnly)
        {
            Name = name; Description = description; Category = category; Key = key; Alternate = alternate; ClientKey = clientKey; Handled = handled;
            OutsideChatOnly = outsideChatOnly;
        }
        public string Name { get; }
        public string Description { get; }
        public string Category { get; }
        /// <summary>Touche en vigueur : celle de l'utilisateur (<see cref="BotOptions.GetShortcut"/>) ou celle du client.</summary>
        public ShortcutKey Key { get; }
        /// <summary>Seconde touche du client (<c>k2</c>), gardée quand l'utilisateur ne change pas la touche.</summary>
        public ShortcutKey? Alternate { get; }
        public ShortcutKey ClientKey { get; }
        /// <summary>Faux pour les raccourcis que le bot n'exécute pas (console d'administration, barre déplaçable…) ou que gère la discussion.</summary>
        public bool Handled { get; }
        /// <summary>
        /// <c>o</c> du client : vrai, le raccourci est ignoré pendant la saisie d'un texte (<c>_aNoChatShortcuts</c>) ; faux,
        /// il agit à tout moment (Échap, Ctrl+Fin, Ctrl+F).
        /// </summary>
        public bool OutsideChatOnly { get; }
        public bool IsCustom => !Key.Equals(ClientKey);
        public string Label => Shortcuts.Describe(Key) + (Alternate.HasValue ? " / " + Shortcuts.Describe(Alternate.Value) : string.Empty);
    }

    /// <summary>Table construite une fois par changement d'options : touche → raccourcis, dans l'ordre du client.</summary>
    public sealed class ShortcutTable
    {
        private readonly Dictionary<ShortcutKey, List<ShortcutDefinition>> byKey = new Dictionary<ShortcutKey, List<ShortcutDefinition>>();
        private readonly Dictionary<string, ShortcutDefinition> byName = new Dictionary<string, ShortcutDefinition>(StringComparer.Ordinal);

        internal ShortcutTable(IEnumerable<ShortcutDefinition> definitions, bool fromClientData)
        {
            FromClientData = fromClientData;
            Definitions = definitions.ToArray();
            foreach (ShortcutDefinition definition in Definitions)
            {
                byName[definition.Name] = definition;
                Add(definition.Key, definition);
                if (definition.Alternate.HasValue) Add(definition.Alternate.Value, definition);
            }
        }

        public IReadOnlyList<ShortcutDefinition> Definitions { get; }
        /// <summary>Vrai si la table vient de <c>shortcuts.xml</c> (textes du client), faux pour la table intégrée de repli.</summary>
        public bool FromClientData { get; }
        public ShortcutDefinition this[string name] => name != null && byName.TryGetValue(name, out ShortcutDefinition definition) ? definition : null;

        /// <summary>Raccourcis exécutés par le bot associés à cette touche.</summary>
        public IEnumerable<ShortcutDefinition> Find(ShortcutKey key) =>
            !key.IsEmpty && byKey.TryGetValue(key, out List<ShortcutDefinition> list) ? list.Where(d => d.Handled) : Enumerable.Empty<ShortcutDefinition>();

        public IEnumerable<ShortcutDefinition> Find(Keys keyData) => Find(Shortcuts.FromKeys(keyData));

        private void Add(ShortcutKey key, ShortcutDefinition definition)
        {
            if (key.IsEmpty) return;
            if (!byKey.TryGetValue(key, out List<ShortcutDefinition> list)) byKey[key] = list = new List<ShortcutDefinition>();
            list.Add(definition);
        }
    }

    /// <summary>
    /// Raccourcis clavier du client (fichier de langue <c>shortcuts_fr</c>, jeu 1 « Clavier français - France », exporté dans
    /// <c>BotLang/shortcuts.xml</c>). Les codes de touche Flash sont les codes virtuels Windows (<see cref="Keys"/>) ; les
    /// modificateurs valent 0, 1 Ctrl, 2 Maj, 3 Ctrl + Maj. Sans <c>shortcuts.xml</c>, une table intégrée reprend les raccourcis
    /// exécutés par le bot avec les touches du jeu 1. L'utilisateur change une touche dans les options (<see cref="BotOptions.SetShortcut"/>).
    /// </summary>
    public static class Shortcuts
    {
        public const int KeySet = 1;

        /// <summary>Raccourcis exécutés par la fenêtre de jeu ; ceux de la discussion restent à la zone de saisie.</summary>
        public static readonly IReadOnlyCollection<string> HandledNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "CHARAC", "SPELLS", "INVENTORY", "QUESTS", "MAP", "FRIENDS", "GUILD", "MOUNT", "MOUNTING",
            "SH0", "SH1", "SH2", "SH3", "SH4", "SH5", "SH6", "SH7", "SH8", "SH9", "SH10", "SH11", "SH12", "SH13", "SH14",
            "GRID", "TRANSPARENCY", "SPRITEINFOS", "COORDS", "TOGGLE_FIGHT_INFOS", "FULLSCREEN", "NEXTTURN", "ESCAPE", "SWAP",
            "MAXI", "MINI",
        };

        /// <summary>Raccourcis du bandeau, soumis à l'option <c>BannerShortcuts</c> comme <c>Banner.onShortcut</c>.</summary>
        public static readonly IReadOnlyCollection<string> BannerNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "CHARAC", "SPELLS", "INVENTORY", "QUESTS", "MAP", "FRIENDS", "GUILD", "MOUNT",
        };

        // Repli sans shortcuts.xml : touches du jeu 1 (code, modificateurs) des raccourcis exécutés, descriptions du bot.
        private static readonly (string Name, int Key, int Modifiers, string Category, string Description)[] BuiltIn =
        {
            ("CHARAC", 67, 0, "Menus", "Caractéristiques"), ("SPELLS", 83, 0, "Menus", "Sorts"), ("INVENTORY", 73, 0, "Menus", "Inventaire"),
            ("QUESTS", 81, 0, "Menus", "Quêtes"), ("MAP", 77, 0, "Menus", "Carte du monde"), ("FRIENDS", 70, 0, "Menus", "Amis"),
            ("GUILD", 71, 0, "Menus", "Guilde"), ("MOUNT", 68, 0, "Menus", "Monture"), ("MOUNTING", 68, 2, "Autres raccourcis", "Monter ou descendre de sa monture"),
            ("SH0", 222, 0, "Combat", "Corps à corps"),
            ("SH1", 49, 0, "Barre de raccourcis rapide", "Case 1"), ("SH2", 50, 0, "Barre de raccourcis rapide", "Case 2"),
            ("SH3", 51, 0, "Barre de raccourcis rapide", "Case 3"), ("SH4", 52, 0, "Barre de raccourcis rapide", "Case 4"),
            ("SH5", 53, 0, "Barre de raccourcis rapide", "Case 5"), ("SH6", 54, 0, "Barre de raccourcis rapide", "Case 6"),
            ("SH7", 55, 0, "Barre de raccourcis rapide", "Case 7"), ("SH8", 49, 1, "Barre de raccourcis rapide", "Case 8"),
            ("SH9", 50, 1, "Barre de raccourcis rapide", "Case 9"), ("SH10", 51, 1, "Barre de raccourcis rapide", "Case 10"),
            ("SH11", 52, 1, "Barre de raccourcis rapide", "Case 11"), ("SH12", 53, 1, "Barre de raccourcis rapide", "Case 12"),
            ("SH13", 54, 1, "Barre de raccourcis rapide", "Case 13"), ("SH14", 55, 1, "Barre de raccourcis rapide", "Case 14"),
            ("SWAP", 226, 0, "Barre de raccourcis rapide", "Sorts / objets"),
            ("GRID", 49, 2, "Interface", "Grille"), ("TRANSPARENCY", 50, 2, "Interface", "Transparence des joueurs"),
            ("SPRITEINFOS", 51, 2, "Interface", "Informations des joueurs en combat"), ("COORDS", 52, 2, "Interface", "Coordonnées de la carte"),
            ("TOGGLE_FIGHT_INFOS", 73, 1, "Interface", "Informations de combat dans le chat"),
            ("MAXI", 107, 0, "Interface", "Agrandir le chat"), ("MINI", 109, 0, "Interface", "Réduire le chat"),
            ("NEXTTURN", 35, 1, "Combat", "Passer son tour"), ("FULLSCREEN", 70, 1, "Autres raccourcis", "Plein écran"),
            ("ESCAPE", 27, 0, "Autres raccourcis", "Fermer l'interface ouverte"),
        };

        // Raccourcis du jeu 1 marqués o="false" (actifs pendant la saisie) dans shortcuts_fr.
        private static readonly HashSet<string> AnyTime = new HashSet<string>(StringComparer.Ordinal) { "NEXTTURN", "FULLSCREEN", "ESCAPE" };

        /// <summary>Table en vigueur : <c>shortcuts.xml</c> si chargé, sinon la table intégrée ; touches de l'utilisateur appliquées.</summary>
        public static ShortcutTable Build(BotOptions options)
        {
            IReadOnlyDictionary<string, ShortcutKey> overrides = options?.ShortcutOverrides ?? new Dictionary<string, ShortcutKey>();
            var definitions = new List<ShortcutDefinition>();
            string prefix = KeySet.ToString(CultureInfo.InvariantCulture) + "|";
            string[] ids = LangData.Ids("shortcuts", "SSK").Where(id => id.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
            if (ids.Length > 0)
            {
                foreach (string id in ids.OrderBy(id => Order(id.Substring(prefix.Length))).ThenBy(id => id, StringComparer.Ordinal))
                {
                    IReadOnlyDictionary<string, string> key = LangData.Raw("shortcuts", "SSK", id);
                    string name = id.Substring(prefix.Length);
                    ShortcutKey client = new ShortcutKey(Int(key, "k"), Int(key, "c"));
                    ShortcutKey? alternate = key.ContainsKey("k2") ? new ShortcutKey(Int(key, "k2"), Int(key, "c2")) : (ShortcutKey?)null;
                    IReadOnlyDictionary<string, string> info = LangData.Raw("shortcuts", "SH", name);
                    string description = info != null && info.TryGetValue("d", out string d) && !string.IsNullOrEmpty(d) ? d : name;
                    string category = info != null && info.TryGetValue("c", out string c) ? CategoryName(c) : string.Empty;
                    bool outsideChat = !key.TryGetValue("o", out string o) || o != "false";
                    definitions.Add(Make(name, description, category, client, alternate, overrides, outsideChat));
                }
                return new ShortcutTable(definitions, true);
            }
            foreach (var entry in BuiltIn)
                definitions.Add(Make(entry.Name, entry.Description, entry.Category, new ShortcutKey(entry.Key, entry.Modifiers), null, overrides, !AnyTime.Contains(entry.Name)));
            return new ShortcutTable(definitions, false);
        }

        /// <summary>Touche et modificateurs d'un événement clavier ; Alt n'existe dans aucun raccourci du client.</summary>
        public static ShortcutKey FromKeys(Keys keyData)
        {
            if ((keyData & Keys.Alt) == Keys.Alt) return default(ShortcutKey);
            Keys code = keyData & Keys.KeyCode;
            if (code == Keys.ControlKey || code == Keys.ShiftKey || code == Keys.Menu || code == Keys.None) return default(ShortcutKey);
            int modifiers = ((keyData & Keys.Control) == Keys.Control ? 1 : 0) | ((keyData & Keys.Shift) == Keys.Shift ? 2 : 0);
            return new ShortcutKey((int)code, modifiers);
        }

        public static Keys ToKeys(ShortcutKey key) =>
            (Keys)key.Key | (key.Control ? Keys.Control : Keys.None) | (key.Shift ? Keys.Shift : Keys.None);

        /// <summary>Libellé français : « Ctrl+Maj+D », « Maj+& », « Ctrl+Fin », « ² ».</summary>
        public static string Describe(ShortcutKey key)
        {
            if (key.IsEmpty) return "aucune";
            string name = KeyName(key.Key);
            return (key.Control ? "Ctrl+" : string.Empty) + (key.Shift ? "Maj+" : string.Empty) + name;
        }

        private static string KeyName(int code)
        {
            switch (code)
            {
                case 13: return "Entrée";
                case 27: return "Échap";
                case 32: return "Espace";
                case 33: return "Page préc.";
                case 34: return "Page suiv.";
                case 35: return "Fin";
                case 36: return "Début";
                case 37: return "Gauche";
                case 38: return "Haut";
                case 39: return "Droite";
                case 40: return "Bas";
                case 46: return "Suppr";
                case 107: return "+";
                case 109: return "-";
                case 222: return "²";
                case 226: return "<";
            }
            // Rangée des chiffres d'un clavier AZERTY : le client nomme la touche par son caractère sans Maj (« Maj+& »).
            if (code >= 48 && code <= 57) return "à&é\"'(-è_ç"[code - 48].ToString();
            if (code >= 65 && code <= 90) return ((char)(code + 32)).ToString();
            if (code >= 112 && code <= 123) return "F" + (code - 111).ToString(CultureInfo.InvariantCulture);
            return ((Keys)code).ToString();
        }

        private static ShortcutDefinition Make(string name, string description, string category, ShortcutKey client, ShortcutKey? alternate,
            IReadOnlyDictionary<string, ShortcutKey> overrides, bool outsideChatOnly)
        {
            bool custom = overrides.TryGetValue(name, out ShortcutKey chosen);
            return new ShortcutDefinition(name, description, category, custom ? chosen : client, custom ? null : alternate, client, HandledNames.Contains(name),
                outsideChatOnly);
        }

        private static string CategoryName(string id)
        {
            IReadOnlyDictionary<string, string> category = LangData.Raw("shortcuts", "SSC", id);
            return category != null && category.TryGetValue("d", out string text) ? text : string.Empty;
        }

        // Ordre d'affichage : catégorie du client (attribut o de SSC), puis nom.
        private static int Order(string name)
        {
            IReadOnlyDictionary<string, string> info = LangData.Raw("shortcuts", "SH", name);
            if (info == null || !info.TryGetValue("c", out string category)) return int.MaxValue;
            IReadOnlyDictionary<string, string> data = LangData.Raw("shortcuts", "SSC", category);
            return data != null && data.TryGetValue("o", out string order) && int.TryParse(order, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : int.MaxValue;
        }

        private static int Int(IReadOnlyDictionary<string, string> values, string key) =>
            values != null && values.TryGetValue(key, out string text) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
    }
}
