using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Tool_BotProtocol.Config
{
    /// <summary>
    /// Touche d'un raccourci au format du client (<c>SSK</c> de <c>shortcuts_fr</c>) : code de touche Flash, identique au code
    /// virtuel Windows, et modificateurs 0 aucun, 1 Ctrl, 2 Maj, 3 Ctrl + Maj.
    /// </summary>
    public struct ShortcutKey : IEquatable<ShortcutKey>
    {
        public ShortcutKey(int key, int modifiers) { Key = key; Modifiers = modifiers & 3; }
        public int Key { get; }
        public int Modifiers { get; }
        public bool Control => (Modifiers & 1) != 0;
        public bool Shift => (Modifiers & 2) != 0;
        public bool IsEmpty => Key <= 0;
        public bool Equals(ShortcutKey other) => Key == other.Key && Modifiers == other.Modifiers;
        public override bool Equals(object obj) => obj is ShortcutKey other && Equals(other);
        public override int GetHashCode() => Key * 4 + Modifiers;
        public override string ToString() => Key.ToString(CultureInfo.InvariantCulture) + "|" + Modifiers.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Option modifiée : <see cref="Name"/> est le nom du client (<c>Grid</c>, <c>MapInfos</c>…), ou <c>Shortcuts</c> / <c>SpellBar</c>.</summary>
    public sealed class BotOptionChangedEventArgs : EventArgs
    {
        public BotOptionChangedEventArgs(string name, object value) { Name = name; Value = value; }
        public string Name { get; }
        public object Value { get; }
    }

    /// <summary>
    /// Options du client (<c>OptionsManager</c>, valeurs par défaut de <c>DEFAULT_VALUES</c>) gardées dans
    /// <c>ressources/Bot/BotOptions.json</c>, à côté de <c>BotConfig.json</c>. Chaque modification est enregistrée puis annoncée par
    /// <see cref="OptionChanged"/> (sur le fil de l'appelant) ; la carte et le chat s'y abonnent.
    /// Le fichier garde aussi les touches choisies par l'utilisateur et l'ordre local de la barre de sorts : StarLoco ne répond
    /// à <c>SM&lt;id&gt;|&lt;position&gt;</c> que par <c>BN</c> (matrice §2 n°28), le bot retient donc lui-même cet ordre.
    /// </summary>
    public sealed class BotOptions
    {
        public const string ShortcutsOption = "Shortcuts";
        public const string SpellBarOption = "SpellBar";
        public const int SpellBarSlots = 14;

        private static readonly object CurrentSync = new object();
        private static BotOptions current;

        private static readonly KeyValuePair<string, object>[] DefaultValues =
        {
            Pair("Grid", false), Pair("Transparency", false), Pair("SpriteInfos", true), Pair("SpriteMove", true),
            Pair("MapInfos", true), Pair("ChatEffects", true), Pair("PointsOverHead", true), Pair("ViewAllMonsterInGroup", true),
            Pair("TimestampInChat", true), Pair("BannerShortcuts", true), Pair("CensorshipFilter", true),
            Pair("DefaultQuality", "high"), Pair("BannerGaugeMode", "xp"), Pair("BannerIllustrationMode", "artwork"),
        };

        private static readonly Dictionary<string, string[]> ChoiceValues = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["DefaultQuality"] = new[] { "low", "medium", "high", "best" },
            ["BannerGaugeMode"] = new[] { "none", "xp", "xpcurrentjob", "xpmount", "pods", "energy" },
            ["BannerIllustrationMode"] = new[] { "artwork", "clock", "compass", "helper", "map" },
        };

        private readonly object sync = new object();
        private readonly Dictionary<string, object> values = new Dictionary<string, object>(StringComparer.Ordinal);
        private readonly Dictionary<string, ShortcutKey> shortcuts = new Dictionary<string, ShortcutKey>(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<int, short>> spellBars = new Dictionary<string, Dictionary<int, short>>(StringComparer.Ordinal);
        private bool keepInvalidCopy;

        /// <summary>Options par défaut, enregistrées dans <paramref name="path"/> à la première modification.</summary>
        public BotOptions(string path)
        {
            FilePath = path;
            foreach (var pair in DefaultValues) values[pair.Key] = pair.Value;
        }

        /// <summary>Fichier des options du bot, dans le dossier de configuration existant (<c>ressources/Bot</c>, comme <c>BotConfig.json</c>).</summary>
        public static string DefaultPath => Path.Combine("ressources", "Bot", "BotOptions.json");

        /// <summary>Options partagées par les fenêtres de jeu, lues au premier accès depuis <see cref="DefaultPath"/>.</summary>
        public static BotOptions Current
        {
            get { lock (CurrentSync) return current ?? (current = Load(DefaultPath)); }
            set { lock (CurrentSync) current = value; }
        }

        /// <summary>Noms des options gérées, dans l'ordre de la fenêtre d'options du client.</summary>
        public static IReadOnlyList<string> Names { get; } = DefaultValues.Select(pair => pair.Key).ToArray();
        /// <summary>Options booléennes (cases à cocher).</summary>
        public static IReadOnlyList<string> BooleanNames { get; } = DefaultValues.Where(pair => pair.Value is bool).Select(pair => pair.Key).ToArray();
        /// <summary>Valeurs permises d'une option à choix (<c>DefaultQuality</c>, <c>BannerGaugeMode</c>, <c>BannerIllustrationMode</c>).</summary>
        public static IReadOnlyList<string> Choices(string name) => name != null && ChoiceValues.TryGetValue(name, out string[] list) ? list : new string[0];
        /// <summary>Valeur par défaut du client, <c>null</c> pour une option inconnue.</summary>
        public static object DefaultValue(string name) => DefaultValues.FirstOrDefault(pair => pair.Key == name).Value;

        public string FilePath { get; }
        /// <summary>Dernière erreur de lecture ou d'écriture (fichier illisible, dossier protégé), <c>null</c> sinon.</summary>
        public string LastError { get; private set; }
        /// <summary>Enregistre le fichier à chaque modification (vrai par défaut).</summary>
        public bool AutoSave { get; set; } = true;

        public event EventHandler<BotOptionChangedEventArgs> OptionChanged;

        public bool Grid { get => GetBool("Grid"); set => Set("Grid", value); }
        public bool Transparency { get => GetBool("Transparency"); set => Set("Transparency", value); }
        public bool SpriteInfos { get => GetBool("SpriteInfos"); set => Set("SpriteInfos", value); }
        public bool SpriteMove { get => GetBool("SpriteMove"); set => Set("SpriteMove", value); }
        public bool MapInfos { get => GetBool("MapInfos"); set => Set("MapInfos", value); }
        public bool ChatEffects { get => GetBool("ChatEffects"); set => Set("ChatEffects", value); }
        public bool PointsOverHead { get => GetBool("PointsOverHead"); set => Set("PointsOverHead", value); }
        public bool ViewAllMonsterInGroup { get => GetBool("ViewAllMonsterInGroup"); set => Set("ViewAllMonsterInGroup", value); }
        public bool TimestampInChat { get => GetBool("TimestampInChat"); set => Set("TimestampInChat", value); }
        public bool BannerShortcuts { get => GetBool("BannerShortcuts"); set => Set("BannerShortcuts", value); }
        public bool CensorshipFilter { get => GetBool("CensorshipFilter"); set => Set("CensorshipFilter", value); }
        public string DefaultQuality { get => GetString("DefaultQuality"); set => Set("DefaultQuality", value); }
        public string BannerGaugeMode { get => GetString("BannerGaugeMode"); set => Set("BannerGaugeMode", value); }
        public string BannerIllustrationMode { get => GetString("BannerIllustrationMode"); set => Set("BannerIllustrationMode", value); }

        public object Get(string name) { lock (sync) return name != null && values.TryGetValue(name, out object value) ? value : null; }
        public bool GetBool(string name) => Get(name) is bool value && value;
        public string GetString(string name) => Get(name) as string;

        /// <summary>
        /// Change une option ; renvoie <c>false</c> si la valeur est déjà celle-là. Une option inconnue, un type inattendu ou un
        /// choix hors liste lève <see cref="ArgumentException"/>.
        /// </summary>
        public bool Set(string name, object value)
        {
            object normalized = Validate(name, value);
            lock (sync)
            {
                if (Equals(values[name], normalized)) return false;
                values[name] = normalized;
            }
            Changed(name, normalized);
            return true;
        }

        /// <summary>Inverse une option booléenne, comme <c>OptionsManager.setOption(nom)</c> sans valeur (raccourcis <c>GRID</c>, <c>COORDS</c>…).</summary>
        public bool Toggle(string name)
        {
            if (!(DefaultValue(name) is bool)) throw new ArgumentException("Option non booléenne : " + name, nameof(name));
            bool value = !GetBool(name);
            Set(name, value);
            return value;
        }

        /// <summary>Remet toutes les options à leur valeur par défaut (bouton « Défaut » du client) ; touches et barre de sorts restent.</summary>
        public void ResetDefaults()
        {
            foreach (var pair in DefaultValues) Set(pair.Key, pair.Value);
        }

        /// <summary>Touche choisie par l'utilisateur pour un raccourci (<c>CHARAC</c>, <c>SH3</c>…), <c>null</c> pour celle du client.</summary>
        public ShortcutKey? GetShortcut(string name)
        {
            lock (sync) return name != null && shortcuts.TryGetValue(name, out ShortcutKey key) ? key : (ShortcutKey?)null;
        }

        /// <summary>Copie des touches modifiées, par nom de raccourci.</summary>
        public IReadOnlyDictionary<string, ShortcutKey> ShortcutOverrides
        {
            get { lock (sync) return new Dictionary<string, ShortcutKey>(shortcuts, StringComparer.Ordinal); }
        }

        /// <summary>Remplace la touche d'un raccourci ; <c>null</c> revient à celle du client.</summary>
        public void SetShortcut(string name, ShortcutKey? key)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Nom de raccourci vide.", nameof(name));
            lock (sync)
            {
                if (key.HasValue) { if (shortcuts.TryGetValue(name, out ShortcutKey known) && known.Equals(key.Value)) return; shortcuts[name] = key.Value; }
                else if (!shortcuts.Remove(name)) return;
            }
            Changed(ShortcutsOption, name);
        }

        public void ResetShortcuts()
        {
            lock (sync) { if (shortcuts.Count == 0) return; shortcuts.Clear(); }
            Changed(ShortcutsOption, null);
        }

        /// <summary>Ordre local de la barre de sorts d'un personnage : position 1 à 14 → identifiant du sort.</summary>
        public IReadOnlyDictionary<int, short> SpellBar(string character)
        {
            lock (sync)
                return character != null && spellBars.TryGetValue(character, out Dictionary<int, short> bar)
                    ? new Dictionary<int, short>(bar) : new Dictionary<int, short>();
        }

        /// <summary>
        /// Place un sort dans la case <paramref name="position"/> (1 à 14) de la barre du personnage : le sort quitte son ancienne
        /// case et remplace celui qui occupait la nouvelle, comme <c>set_SpellPlace</c> de StarLoco.
        /// </summary>
        public void SetSpellSlot(string character, int position, short spellId)
        {
            if (string.IsNullOrEmpty(character)) throw new ArgumentException("Personnage inconnu.", nameof(character));
            if (position < 1 || position > SpellBarSlots) throw new ArgumentOutOfRangeException(nameof(position));
            lock (sync)
            {
                if (!spellBars.TryGetValue(character, out Dictionary<int, short> bar)) spellBars[character] = bar = new Dictionary<int, short>();
                if (bar.TryGetValue(position, out short known) && known == spellId) return;
                foreach (int old in bar.Where(entry => entry.Value == spellId).Select(entry => entry.Key).ToArray()) bar.Remove(old);
                bar[position] = spellId;
            }
            Changed(SpellBarOption, character);
        }

        /// <summary>Lit le fichier ; absent = valeurs par défaut ; illisible = valeurs par défaut, erreur dans <see cref="LastError"/>.</summary>
        public static BotOptions Load(string path)
        {
            var options = new BotOptions(path);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return options;
            try
            {
                options.Read(JObject.Parse(File.ReadAllText(path)));
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is JsonException || error is InvalidCastException || error is FormatException || error is ArgumentException || error is OverflowException)
            {
                options.LastError = "Options illisibles (" + error.Message + ") : valeurs par défaut du client utilisées.";
                // Le fichier abîmé est copié à côté avant le premier enregistrement, jamais écrasé sans trace.
                options.keepInvalidCopy = true;
            }
            return options;
        }

        /// <summary>Enregistre toutes les options (écriture atomique) ; renvoie <c>false</c> et renseigne <see cref="LastError"/> en cas d'échec.</summary>
        public bool Save()
        {
            if (string.IsNullOrEmpty(FilePath)) return false;
            string text;
            lock (sync) text = ToJson().ToString(Formatting.Indented);
            try
            {
                string folder = Path.GetDirectoryName(Path.GetFullPath(FilePath));
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                if (keepInvalidCopy && File.Exists(FilePath)) File.Copy(FilePath, FilePath + ".illisible", true);
                keepInvalidCopy = false;
                GlobalConfig.AtomicWrite(FilePath, text);
                return true;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is NotSupportedException || error is ArgumentException)
            {
                LastError = "Options non enregistrées : " + error.Message;
                return false;
            }
        }

        private void Changed(string name, object value)
        {
            if (AutoSave) Save();
            OptionChanged?.Invoke(this, new BotOptionChangedEventArgs(name, value));
        }

        private static object Validate(string name, object value)
        {
            object fallback = DefaultValue(name);
            if (fallback == null) throw new ArgumentException("Option inconnue : " + name, nameof(name));
            if (fallback is bool)
            {
                if (value is bool) return value;
                throw new ArgumentException("L'option " + name + " attend vrai ou faux.", nameof(value));
            }
            string text = value as string;
            if (text == null || !ChoiceValues[name].Contains(text))
                throw new ArgumentException("Valeur « " + value + " » invalide pour " + name + " (" + string.Join(", ", ChoiceValues[name]) + ").", nameof(value));
            return text;
        }

        private JObject ToJson()
        {
            var options = new JObject();
            foreach (var pair in values) options[pair.Key] = JToken.FromObject(pair.Value);
            var keys = new JObject();
            foreach (var pair in shortcuts.OrderBy(entry => entry.Key, StringComparer.Ordinal))
                keys[pair.Key] = new JObject(new JProperty("k", pair.Value.Key), new JProperty("c", pair.Value.Modifiers));
            var bars = new JObject();
            foreach (var pair in spellBars.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                var bar = new JObject();
                foreach (var slot in pair.Value.OrderBy(entry => entry.Key)) bar[slot.Key.ToString(CultureInfo.InvariantCulture)] = slot.Value;
                bars[pair.Key] = bar;
            }
            return new JObject(new JProperty("Options", options), new JProperty("Shortcuts", keys), new JProperty("SpellBars", bars));
        }

        /// <summary>Valeurs inconnues ou mal typées ignorées une à une : un fichier ancien ou retouché à la main garde le reste.</summary>
        private void Read(JObject root)
        {
            if (root["Options"] is JObject options)
                foreach (JProperty property in options.Properties())
                {
                    object fallback = DefaultValue(property.Name);
                    if (fallback is bool && property.Value.Type == JTokenType.Boolean) values[property.Name] = property.Value.Value<bool>();
                    else if (fallback is string && property.Value.Type == JTokenType.String && ChoiceValues[property.Name].Contains(property.Value.Value<string>()))
                        values[property.Name] = property.Value.Value<string>();
                }
            if (root["Shortcuts"] is JObject keys)
                foreach (JProperty property in keys.Properties())
                    if (property.Value is JObject key && key["k"]?.Type == JTokenType.Integer)
                        shortcuts[property.Name] = new ShortcutKey(key["k"].Value<int>(), key["c"]?.Type == JTokenType.Integer ? key["c"].Value<int>() : 0);
            if (root["SpellBars"] is JObject bars)
                foreach (JProperty character in bars.Properties())
                {
                    if (!(character.Value is JObject slots)) continue;
                    var bar = new Dictionary<int, short>();
                    foreach (JProperty slot in slots.Properties())
                        if (int.TryParse(slot.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out int position) && position >= 1 && position <= SpellBarSlots
                            && slot.Value.Type == JTokenType.Integer && short.TryParse(slot.Value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out short spell)
                            && !bar.ContainsValue(spell))
                            bar[position] = spell;
                    if (bar.Count > 0) spellBars[character.Name] = bar;
                }
        }

        private static KeyValuePair<string, object> Pair(string name, object value) => new KeyValuePair<string, object>(name, value);
    }
}
