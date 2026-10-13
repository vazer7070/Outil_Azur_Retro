using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Exchanges;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Perso.Inventory;

namespace Tool_BotProtocol.Game.Perso.Spells
{
    /// <summary>Un effet d'un niveau de sort, lu dans <c>spells.xml</c> : « [type, p1, p2, p3, tours, probabilité, dés] ».</summary>
    public sealed class SpellEffectLine
    {
        public int Type { get; internal set; }
        public int? Param1 { get; internal set; }
        public int? Param2 { get; internal set; }
        public int? Param3 { get; internal set; }
        public int? Turns { get; internal set; }
        public int? Probability { get; internal set; }
        public string Dice { get; internal set; }

        /// <summary>
        /// Texte comme <c>Effect.description</c> du client : « Dans x% des cas: » devant un effet aléatoire, motif de
        /// <c>effects.xml</c> avec les paramètres, puis la durée entre parenthèses (« 3 tours », « Infini »).
        /// </summary>
        public string Describe()
        {
            string text = ItemEffects.Describe(new ItemEffect { Type = Type, Min = Param1, Max = Param2, Special = Param3, Text = Dice ?? string.Empty });
            if (string.IsNullOrEmpty(text)) return null;
            if (Probability.HasValue && Probability.Value > 0)
                text = ExchangeRegistry.Text("IN_CASE_PERCENT", "Dans " + Probability.Value.ToString(CultureInfo.InvariantCulture) + "% des cas",
                    Probability.Value.ToString(CultureInfo.InvariantCulture)) + ": " + text;
            string turns = TurnsText();
            return turns.Length == 0 ? text : text + " (" + turns + ")";
        }

        private string TurnsText()
        {
            if (!Turns.HasValue || Turns.Value == 0) return string.Empty;
            string count = Turns.Value.ToString(CultureInfo.InvariantCulture);
            return Turns.Value > 1 ? count + " " + ExchangeRegistry.Text("TURNS", "tours") : count + " " + ExchangeRegistry.Text("TURN", "tour");
        }
    }

    /// <summary>
    /// Un niveau de sort tel que la fiche détaillée du client l'affiche (<c>SpellFullInfosViewer</c>) : effets normaux et
    /// critiques, PA, portée, probabilités, relances, ligne de vue, niveau de personnage requis. Les données viennent des
    /// attributs <c>niveau1</c>…<c>niveau6</c> de <c>spells.xml</c> ; sans eux, de <c>BotSorts</c> (sans valeurs d'effets).
    /// </summary>
    public sealed class SpellLevelInfo
    {
        public short SpellId { get; private set; }
        public int Level { get; private set; }
        public bool FromLang { get; private set; }
        public IReadOnlyList<SpellEffectLine> Effects { get; private set; } = new SpellEffectLine[0];
        public IReadOnlyList<SpellEffectLine> CriticalEffects { get; private set; } = new SpellEffectLine[0];
        public int ApCost { get; private set; }
        public int RangeMin { get; private set; }
        public int RangeMax { get; private set; }
        /// <summary>Coup critique : 1 chance sur <c>n</c> (0 = aucun).</summary>
        public int CriticalHit { get; private set; }
        public int CriticalFailure { get; private set; }
        public bool LineOnly { get; private set; }
        public bool LineOfSight { get; private set; }
        public bool FreeCell { get; private set; }
        public bool RangeBoostable { get; private set; }
        /// <summary>Type du filtre de la fenêtre des sorts : 0 classe, 1 élémentaire, 2 invocation, 3 maîtrise, 4 spécial.</summary>
        public int SpellType { get; private set; }
        public int PerTurn { get; private set; }
        public int PerTarget { get; private set; }
        public int Delay { get; private set; }
        public string Zones { get; private set; } = string.Empty;
        /// <summary>Niveau de personnage requis (0 = inconnu).</summary>
        public int MinPlayerLevel { get; private set; }
        public bool FailureEndsTurn { get; private set; }

        /// <summary>Nombre de niveaux du sort (6 par défaut quand aucune donnée n'est connue).</summary>
        public static int MaxLevel(short spellId)
        {
            IReadOnlyDictionary<string, string> row = Row(spellId);
            if (row != null)
            {
                int count = 0;
                for (int level = 1; level <= SpellBook.MaxSpellLevel; level++)
                    if (row.TryGetValue("niveau" + level.ToString(CultureInfo.InvariantCulture), out string value) && !string.IsNullOrEmpty(value)) count = level;
                if (count > 0) return count;
            }
            Spell template = Spell.getSpell(spellId);
            if (template != null && template.Stats.Count > 0) return Math.Min(SpellBook.MaxSpellLevel, template.Stats.Keys.Max(k => (int)k));
            return SpellBook.MaxSpellLevel;
        }

        /// <summary>Données d'un niveau, ou null si ni les fichiers de langue ni <c>BotSorts</c> ne le décrivent.</summary>
        public static SpellLevelInfo Get(short spellId, int level)
        {
            if (level < 1 || level > SpellBook.MaxSpellLevel) return null;
            return FromLangData(spellId, level) ?? FromBotSorts(spellId, level);
        }

        private static IReadOnlyDictionary<string, string> Row(short spellId)
        {
            try { return LangData.Raw("spells", "sort", spellId.ToString(CultureInfo.InvariantCulture)); }
            catch (Exception) { return null; }
        }

        /// <summary>Niveau lu dans <c>spells.xml</c> seulement (null sans fichiers de langue ou pour un niveau absent).</summary>
        internal static SpellLevelInfo LangLevel(short spellId, int level) => level < 1 || level > SpellBook.MaxSpellLevel ? null : FromLangData(spellId, level);

        /// <summary>
        /// Caractéristiques utilisées en combat (PA, portée, ligne, cellule libre, relances, effets et zones), comme
        /// <c>dofus.datacenter.Spell</c> du client : la zone d'un effet normal est la paire n° i de la chaîne des zones, celle
        /// d'un effet critique la paire n° (nombre d'effets normaux + i).
        /// </summary>
        internal SpellStats ToStats()
        {
            var stats = new SpellStats
            {
                PA = ToByte(ApCost), Min_portee = ToByte(RangeMin), Max_portee = ToByte(RangeMax),
                IsInLine = LineOnly, AvecLigneDeVue = LineOfSight, EmptyCell = FreeCell, portee_modifiable = RangeBoostable,
                PerTurn = ToByte(PerTurn), PerObjective = ToByte(PerTarget), Interval = ToByte(Delay)
            };
            int index = 0;
            foreach (SpellEffectLine effect in Effects) stats.AddEffect(new SpellEffect(effect.Type, ZoneAt(index++)), false);
            foreach (SpellEffectLine effect in CriticalEffects) stats.AddEffect(new SpellEffect(effect.Type, ZoneAt(index++)), true);
            return stats;
        }

        private Spells.Zones ZoneAt(int index)
        {
            string zones = Zones ?? string.Empty;
            if (zones.Length >= 2 * (index + 1))
            {
                string pair = zones.Substring(index * 2, 2);
                try { return Spells.Zones.Parse(pair); }
                catch (Exception) { ReportUnreadableZone(SpellId, Level, index, pair); }
            }
            return new Spells.Zones(SpellActionZone.SOLO, 0);
        }

        private const int MaxZoneWarnings = 200;
        // Une entrée par sort et niveau (clé sort × 8 + niveau) : une zone illisible n'est signalée qu'une fois par processus.
        private static readonly ConcurrentDictionary<int, string> zoneWarnings = new ConcurrentDictionary<int, string>();

        /// <summary>
        /// Zones d'effet illisibles rencontrées dans <c>spells.xml</c>, une ligne par sort et niveau (au plus 200), comme les
        /// <c>LoadWarnings</c> des chargeurs. L'effet concerné est alors appliqué sur sa seule cellule cible.
        /// </summary>
        public static string[] ZoneWarnings => zoneWarnings.OrderBy(entry => entry.Key).Select(entry => entry.Value).ToArray();

        /// <summary>Levé une seule fois par sort et niveau, à la première zone illisible, avec la ligne ajoutée à <see cref="ZoneWarnings"/>.</summary>
        public static event Action<string> ZoneWarning;

        private static void ReportUnreadableZone(short spellId, int level, int index, string pair)
        {
            if (zoneWarnings.Count >= MaxZoneWarnings) return;
            string message = "Sort " + spellId.ToString(CultureInfo.InvariantCulture) + " niveau " + level.ToString(CultureInfo.InvariantCulture)
                + " : zone d'effet « " + pair + " » illisible (paire n° " + (index + 1).ToString(CultureInfo.InvariantCulture)
                + " des zones) dans spells.xml, effet appliqué sur une seule cellule.";
            if (!zoneWarnings.TryAdd(spellId * 8 + level, message)) return;
            Action<string> subscribers = ZoneWarning;
            if (subscribers == null) return;
            foreach (Action<string> subscriber in subscribers.GetInvocationList())
                try { subscriber(message); } catch { /* Un journal fermé ne doit pas empêcher le calcul des caractéristiques. */ }
        }

        private static byte ToByte(int value) => (byte)Math.Max(0, Math.Min(byte.MaxValue, value));

        private static SpellLevelInfo FromLangData(short spellId, int level)
        {
            IReadOnlyDictionary<string, string> row = Row(spellId);
            if (row == null || !row.TryGetValue("niveau" + level.ToString(CultureInfo.InvariantCulture), out string json) || string.IsNullOrEmpty(json)) return null;
            try
            {
                var data = JArray.Parse(json);
                return new SpellLevelInfo
                {
                    SpellId = spellId,
                    Level = level,
                    FromLang = true,
                    Effects = EffectsOf(At(data, 0)),
                    CriticalEffects = EffectsOf(At(data, 1)),
                    ApCost = Int(At(data, 2)),
                    RangeMin = Int(At(data, 3)),
                    RangeMax = Int(At(data, 4)),
                    CriticalHit = Int(At(data, 5)),
                    CriticalFailure = Int(At(data, 6)),
                    LineOnly = Bool(At(data, 7)),
                    LineOfSight = Bool(At(data, 8)),
                    FreeCell = Bool(At(data, 9)),
                    RangeBoostable = Bool(At(data, 10)),
                    SpellType = Int(At(data, 11)),
                    PerTurn = Int(At(data, 12)),
                    PerTarget = Int(At(data, 13)),
                    Delay = Int(At(data, 14)),
                    Zones = At(data, 15)?.Type == JTokenType.String ? (string)At(data, 15) : string.Empty,
                    MinPlayerLevel = Int(At(data, 18)),
                    FailureEndsTurn = Bool(At(data, 19)),
                };
            }
            catch (Exception)
            {
                return null; // niveau illisible : BotSorts prend le relais
            }
        }

        private static SpellLevelInfo FromBotSorts(short spellId, int level)
        {
            Spell template = Spell.getSpell(spellId);
            if (template == null || !template.Stats.TryGetValue((byte)level, out SpellStats stats) || stats == null) return null;
            return new SpellLevelInfo
            {
                SpellId = spellId,
                Level = level,
                FromLang = false,
                Effects = stats.NormalEffect.Select(e => new SpellEffectLine { Type = e.Id }).ToArray(),
                CriticalEffects = stats.CriticalEffect.Select(e => new SpellEffectLine { Type = e.Id }).ToArray(),
                ApCost = stats.PA,
                RangeMin = stats.Min_portee,
                RangeMax = stats.Max_portee,
                LineOnly = stats.IsInLine,
                LineOfSight = stats.AvecLigneDeVue,
                FreeCell = stats.EmptyCell,
                RangeBoostable = stats.portee_modifiable,
                PerTurn = stats.PerTurn,
                PerTarget = stats.PerObjective,
                Delay = stats.Interval,
            };
        }

        private static JToken At(JArray data, int index) => index < data.Count ? data[index] : null;

        private static int Int(JToken token)
        {
            if (token == null) return 0;
            if (token.Type == JTokenType.Integer) return (int)token;
            if (token.Type == JTokenType.Float) return (int)(double)token;
            return 0;
        }

        private static int? NullableInt(JToken token) => token != null && (token.Type == JTokenType.Integer || token.Type == JTokenType.Float) ? Int(token) : (int?)null;

        private static bool Bool(JToken token)
        {
            if (token == null) return false;
            if (token.Type == JTokenType.Boolean) return (bool)token;
            return Int(token) != 0;
        }

        private static SpellEffectLine[] EffectsOf(JToken token)
        {
            var list = token as JArray;
            if (list == null) return new SpellEffectLine[0];
            var effects = new List<SpellEffectLine>();
            foreach (JToken entry in list)
            {
                var values = entry as JArray;
                if (values == null || values.Count == 0 || NullableInt(values[0]) == null) continue;
                effects.Add(new SpellEffectLine
                {
                    Type = Int(values[0]),
                    Param1 = values.Count > 1 ? NullableInt(values[1]) : null,
                    Param2 = values.Count > 2 ? NullableInt(values[2]) : null,
                    Param3 = values.Count > 3 ? NullableInt(values[3]) : null,
                    Turns = values.Count > 4 ? NullableInt(values[4]) : null,
                    Probability = values.Count > 5 ? NullableInt(values[5]) : null,
                    Dice = values.Count > 6 && values[6].Type == JTokenType.String ? (string)values[6] : null,
                });
            }
            return effects.ToArray();
        }
    }

    /// <summary>
    /// Sorts du personnage côté fenêtres du client 1.34 : amélioration (<c>SB&lt;id&gt;</c> → <c>SUK</c> + <c>As</c> ou <c>SUE</c>),
    /// fenêtre d'oubli ouverte par le serveur (<c>SF+</c>/<c>SF-</c>, envois <c>SF&lt;id&gt;</c> et <c>SF-1</c>), option
    /// « voir tous les sorts » (<c>SLo±</c>) et bonus d'objets de classe (<c>SB&lt;effet&gt;;&lt;sort&gt;;&lt;valeur&gt;</c>).
    /// L'ordre de la barre de sorts reste local (<c>SM</c> n'est répondu que par <c>BN</c>).
    /// </summary>
    public sealed class SpellBook
    {
        public const int MaxSpellLevel = 6;
        private const string Reference = "SORTS";
        private readonly Accounts.Accounts account;
        private readonly CharacterClass character;
        private readonly ConcurrentDictionary<string, int> modificators = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        private volatile bool forgetOpen, seeAll;
        private string lastMessage = string.Empty;

        /// <summary>Vrai entre <c>SF+</c> et <c>SF-</c> (ou l'envoi de <c>SF&lt;id&gt;</c>/<c>SF-1</c>) : seul moment où StarLoco accepte un oubli.</summary>
        public bool ForgetWindowOpen => forgetOpen;
        /// <summary><c>SLo+</c> : le serveur autorise l'affichage des sorts de classe non appris.</summary>
        public bool CanSeeAllSpells => seeAll;
        public string LastMessage { get => Volatile.Read(ref lastMessage); private set => Volatile.Write(ref lastMessage, value ?? string.Empty); }

        /// <summary>Fenêtre d'oubli, option ou bonus modifiés ; levé sur le fil réseau.</summary>
        public event Action Changed;
        /// <summary><c>SUE</c> reçu : le serveur a refusé l'amélioration.</summary>
        public event Action<string> UpgradeRefused;

        internal SpellBook(Accounts.Accounts account, CharacterClass character)
        {
            this.account = account;
            this.character = character;
        }

        private void Notify()
        {
            try { Changed?.Invoke(); }
            catch (Exception error) { account?.Logger?.LogException(Reference, error); }
        }

        internal void SetForgetWindow(bool open)
        {
            forgetOpen = open;
            LastMessage = open ? "Le serveur propose d'oublier un sort." : "Fenêtre d'oubli de sort fermée.";
            Notify();
        }

        internal void SetSeeAllSpells(bool allowed)
        {
            seeAll = allowed;
            Notify();
        }

        internal void SetModificator(int effect, short spellId, int value)
        {
            string key = Key(spellId, effect);
            if (value == 0) modificators.TryRemove(key, out _);
            else modificators[key] = value;
            Notify();
        }

        internal void OnUpgraded(short spellId, byte level, byte? previous)
        {
            string name = NameOf(spellId);
            LastMessage = previous.HasValue && previous.Value > level
                ? "Sort « " + name + " » ramené au niveau " + level.ToString(CultureInfo.InvariantCulture) + "."
                : "Sort « " + name + " » au niveau " + level.ToString(CultureInfo.InvariantCulture) + ".";
            Notify();
        }

        internal void OnUpgradeRefused()
        {
            LastMessage = ExchangeRegistry.Text("CANT_BOOST_SPELL", "Impossible d'améliorer ce sort.");
            try { UpgradeRefused?.Invoke(LastMessage); }
            catch (Exception error) { account?.Logger?.LogException(Reference, error); }
            Notify();
        }

        /// <summary>Bonus d'objets de classe d'un sort : couples (effet, valeur) reçus par <c>SB</c>.</summary>
        public IReadOnlyList<KeyValuePair<int, int>> ModificatorsOf(short spellId)
        {
            string prefix = spellId.ToString(CultureInfo.InvariantCulture) + ":";
            return modificators.Where(entry => entry.Key.StartsWith(prefix, StringComparison.Ordinal))
                .Select(entry => new KeyValuePair<int, int>(int.Parse(entry.Key.Substring(prefix.Length), CultureInfo.InvariantCulture), entry.Value))
                .OrderBy(entry => entry.Key).ToArray();
        }

        /// <summary>Bonus d'un sort décrits avec les motifs d'effets du client (« +10 de dommages sur le sort … »).</summary>
        public IReadOnlyList<string> DescribeModificators(short spellId)
        {
            return ModificatorsOf(spellId)
                .Select(m => ItemEffects.Describe(new ItemEffect { Type = m.Key, Min = spellId, Special = m.Value }))
                .Where(text => !string.IsNullOrEmpty(text)).ToArray();
        }

        /// <summary>Nom du sort : fichiers de langue, sinon <c>BotSorts</c>, sinon « Sort #id ».</summary>
        public static string NameOf(short spellId, Spell known = null)
        {
            try { if (LangData.Spell.Has(spellId)) return LangData.Spell.Name(spellId); }
            catch (Exception) { /* fichiers de langue illisibles */ }
            return known?.Name ?? Spell.getSpell(spellId)?.Name ?? "Sort #" + spellId.ToString(CultureInfo.InvariantCulture);
        }

        public static string DescriptionOf(short spellId)
        {
            try { if (LangData.Spell.Has(spellId)) return LangData.Spell.Description(spellId) ?? string.Empty; }
            catch (Exception) { /* fichiers de langue illisibles */ }
            return string.Empty;
        }

        /// <summary>Coût d'un niveau : le niveau actuel en points de sort (<c>SPELL_BOOST_BONUS</c> du client, <c>boostSpell</c> de StarLoco).</summary>
        public static int UpgradeCost(int currentLevel) => Math.Max(0, currentLevel);

        /// <summary>Raison qui empêche l'amélioration (contrôles de <c>Spells.boostSpell</c> du client), ou null.</summary>
        public string CannotUpgrade(short spellId)
        {
            if (account?.Connexion == null || !account.Connexion.IsConnected()) return "Connectez le personnage avant cette action.";
            if (!character.Spells.TryGetValue(spellId, out Spell spell) || spell == null) return "Ce sort n'est pas appris.";
            int max = SpellLevelInfo.MaxLevel(spellId);
            if (spell.Level >= max) return "Ce sort est déjà à son niveau maximal.";
            int cost = UpgradeCost(spell.Level);
            if (cost > character.SpellPoints)
                return "Points de sort insuffisants : " + cost.ToString(CultureInfo.InvariantCulture) + " requis, "
                    + character.SpellPoints.ToString(CultureInfo.InvariantCulture) + " disponible(s).";
            SpellLevelInfo next = SpellLevelInfo.Get(spellId, spell.Level + 1);
            if (next != null && next.MinPlayerLevel > 0 && character.Level < next.MinPlayerLevel)
            {
                string level = next.MinPlayerLevel.ToString(CultureInfo.InvariantCulture);
                return ExchangeRegistry.Text("LEVEL_NEED_TO_BOOST", "Le niveau " + level + " est requis.", level);
            }
            return null;
        }

        public bool CanUpgrade(short spellId) => CannotUpgrade(spellId) == null;

        /// <summary>Envoie <c>SB&lt;id&gt;</c> ; le niveau change à la réception de <c>SUK</c>, <c>SUE</c> signale un refus.</summary>
        public Task<InteractionResult> UpgradeAsync(short spellId)
        {
            string refusal = CannotUpgrade(spellId);
            if (refusal != null) return Task.FromResult(Refuse(refusal));
            return SendAsync("SB" + spellId.ToString(CultureInfo.InvariantCulture), "Amélioration du sort « " + NameOf(spellId) + " » demandée.");
        }

        /// <summary>Sorts proposés par la fenêtre d'oubli du client : niveau supérieur à 1.</summary>
        public IReadOnlyList<Spell> ForgettableSpells()
        {
            return character.Spells.Values.Where(spell => spell != null && spell.Level > 1).OrderBy(spell => spell.ID).ToArray();
        }

        /// <summary>Raison qui empêche d'oublier ce sort maintenant, ou null.</summary>
        public string CannotForget(short spellId)
        {
            if (account?.Connexion == null || !account.Connexion.IsConnected()) return "Connectez le personnage avant cette action.";
            if (!forgetOpen) return "L'oubli de sort n'est possible que dans la fenêtre ouverte par le serveur (PNJ ou objet d'oubli).";
            if (!character.Spells.TryGetValue(spellId, out Spell spell) || spell == null) return "Ce sort n'est pas appris.";
            if (spell.Level <= 1) return "Un sort de niveau 1 ne peut pas être oublié.";
            return null;
        }

        /// <summary>Envoie <c>SF&lt;id&gt;</c> et ferme la fenêtre comme le client ; StarLoco répond <c>SUK</c> + <c>As</c>.</summary>
        public async Task<InteractionResult> ForgetAsync(short spellId)
        {
            string refusal = CannotForget(spellId);
            if (refusal != null) return Refuse(refusal);
            InteractionResult result = await SendAsync("SF" + spellId.ToString(CultureInfo.InvariantCulture),
                "Oubli du sort « " + NameOf(spellId) + " » demandé.").ConfigureAwait(false);
            if (result.Sent) { forgetOpen = false; Notify(); }
            return result;
        }

        /// <summary>« Annuler » ou fermeture de la fenêtre d'oubli : <c>SF-1</c>.</summary>
        public async Task<InteractionResult> CancelForgetAsync()
        {
            if (!forgetOpen) return Refuse("Aucune fenêtre d'oubli de sort n'est ouverte.");
            InteractionResult result = await SendAsync("SF-1", "Oubli de sort annulé.").ConfigureAwait(false);
            if (result.Sent) { forgetOpen = false; Notify(); }
            return result;
        }

        private async Task<InteractionResult> SendAsync(string packet, string message)
        {
            var connection = account?.Connexion;
            if (connection == null || !connection.IsConnected()) return Refuse("Connectez le personnage avant cette action.");
            try
            {
                await connection.SendPacket(packet, true).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                account?.Logger?.LogException(Reference, error);
                return Refuse("Envoi impossible : " + error.Message);
            }
            LastMessage = message;
            account?.Logger?.LogInfo(Reference, message);
            return new InteractionResult(true, message);
        }

        private InteractionResult Refuse(string message)
        {
            LastMessage = message;
            return new InteractionResult(false, message);
        }

        private static string Key(short spellId, int effect) => spellId.ToString(CultureInfo.InvariantCulture) + ":" + effect.ToString(CultureInfo.InvariantCulture);

        /// <summary>Oublie fenêtre, option et bonus sans rien envoyer (déconnexion, changement de personnage).</summary>
        internal void Clear()
        {
            forgetOpen = false;
            seeAll = false;
            modificators.Clear();
            LastMessage = string.Empty;
        }
    }
}
