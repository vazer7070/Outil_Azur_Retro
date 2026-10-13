using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using Tool_BotProtocol.Game.Data;

namespace Tool_BotProtocol.Game.Combats
{
    /// <summary>
    /// Corps à corps du personnage, que le client range comme sort 0 (<c>CloseCombat</c>, case <c>²</c>, raccourci <c>SH0</c>)
    /// et envoie par <c>GA303&lt;cellule&gt;</c>.
    /// <list type="bullet">
    /// <item>Arme en position 1 : attribut <c>arme</c> de <c>items.xml</c> (<c>I.u[id].e</c> du client : bonus de coup critique,
    /// PA, portée minimale, portée maximale, coup critique 1/x, échec critique 1/x, en ligne, ligne de vue).</item>
    /// <item>Sans arme : poings de la classe, attribut <c>cc</c> de <c>classes.xml</c> (<c>G[classe].cc</c> : effets, bonus,
    /// PA, portée minimale, portée maximale, CC, EC, en ligne, ligne de vue, états requis, états interdits) ; si la classe
    /// manque, 4 PA et une portée de 1, comme le sort 0 « Coup de poing » de StarLoco au niveau 1.</item>
    /// </list>
    /// Les états requis et interdits viennent toujours de la classe, arme ou non, et la portée ne se modifie pas
    /// (<c>canBoostRange</c> faux), comme dans le client. Une arme sans attribut <c>arme</c> (pierre d'âme, textes absents)
    /// n'a pas de données : le serveur décide seul des PA et de la portée.
    /// </summary>
    public sealed class CloseCombatStats
    {
        /// <summary>Poings quand ni l'arme ni la classe ne sont connues : sort 0 de StarLoco, niveau 1.</summary>
        public const int DefaultActionPoints = 4, DefaultMinRange = 1, DefaultMaxRange = 1;
        private static readonly int[] None = new int[0];
        private static readonly object CacheLock = new object();
        private static object cacheSnapshot;
        private static long cacheKey = long.MinValue;
        private static CloseCombatStats cached;

        /// <summary>Modèle de l'arme en position 1 ; 0 à mains nues.</summary>
        public int WeaponTemplateId { get; private set; }
        public bool IsArmed => WeaponTemplateId > 0;
        /// <summary>Nom de l'arme (textes du client, sinon celui de l'inventaire) ; « Poings » à mains nues.</summary>
        public string Name { get; private set; } = "Poings";
        /// <summary>Type d'objet de l'arme (<c>items.xml</c>) ; 0 à mains nues ou inconnu.</summary>
        public int ItemType { get; private set; }
        /// <summary>Faux si l'arme équipée n'a pas d'attribut <c>arme</c> : PA et portée ne sont alors pas vérifiés localement.</summary>
        public bool HasData { get; private set; } = true;
        /// <summary>Origine des valeurs : <c>arme</c>, <c>classe</c>, <c>défaut</c> ou <c>inconnue</c>.</summary>
        public string Source { get; private set; } = "défaut";
        public int ActionPoints { get; private set; } = DefaultActionPoints;
        public int MinRange { get; private set; } = DefaultMinRange;
        public int MaxRange { get; private set; } = DefaultMaxRange;
        /// <summary>Coup critique 1/x (0 : inconnu).</summary>
        public int CriticalHit { get; private set; }
        /// <summary>Échec critique 1/x (0 : inconnu).</summary>
        public int CriticalFailure { get; private set; }
        public bool LineOnly { get; private set; }
        public bool LineOfSight { get; private set; }
        public IReadOnlyList<int> RequiredStates { get; private set; } = None;
        public IReadOnlyList<int> ForbiddenStates { get; private set; } = None;

        /// <summary>Résumé pour une infobulle : « Épée de Mercenaire · 5 PA · portée 1 ».</summary>
        public string Summary
        {
            get
            {
                if (!HasData) return Name + " · PA et portée inconnus";
                string range = MinRange == MaxRange ? MinRange.ToString(CultureInfo.InvariantCulture)
                    : MinRange.ToString(CultureInfo.InvariantCulture) + "–" + MaxRange.ToString(CultureInfo.InvariantCulture);
                return Name + " · " + ActionPoints.ToString(CultureInfo.InvariantCulture) + " PA · portée " + range + (LineOnly ? " en ligne" : string.Empty);
            }
        }

        /// <summary>
        /// Corps à corps de la classe <paramref name="classId"/> avec l'arme <paramref name="weaponTemplateId"/> (0 : aucune).
        /// Le dernier résultat est gardé tant que les textes chargés ne changent pas (la carte en demande un par cellule).
        /// </summary>
        public static CloseCombatStats For(int classId, int weaponTemplateId, string inventoryName = null)
        {
            object snapshot = LangData.CurrentSnapshot;
            long key = ((long)classId << 32) ^ (uint)Math.Max(0, weaponTemplateId);
            lock (CacheLock)
            {
                if (cached != null && ReferenceEquals(snapshot, cacheSnapshot) && key == cacheKey) return cached;
            }
            CloseCombatStats value = Build(classId, Math.Max(0, weaponTemplateId), inventoryName);
            lock (CacheLock) { cached = value; cacheSnapshot = snapshot; cacheKey = key; }
            return value;
        }

        private static CloseCombatStats Build(int classId, int weapon, string inventoryName)
        {
            var stats = new CloseCombatStats();
            JArray cc = ClassCloseCombat(classId);
            if (cc != null) { stats.RequiredStates = States(cc, 9); stats.ForbiddenStates = States(cc, 10); }
            if (weapon > 0)
            {
                stats.WeaponTemplateId = weapon;
                int? type = LangData.Item.Type(weapon);
                string name = type.HasValue ? LangData.Item.Name(weapon) : null;
                stats.Name = !string.IsNullOrEmpty(name) ? name : !string.IsNullOrEmpty(inventoryName) ? inventoryName : "Arme #" + weapon.ToString(CultureInfo.InvariantCulture);
                stats.ItemType = type ?? 0;
                string[] e = LangData.Item.WeaponStats(weapon);
                int ap, min, max;
                if (e.Length >= 8 && TryInt(e[1], out ap) && TryInt(e[2], out min) && TryInt(e[3], out max) && ap >= 0 && min >= 0 && max >= min)
                {
                    stats.Source = "arme"; stats.ActionPoints = ap; stats.MinRange = min; stats.MaxRange = max;
                    stats.CriticalHit = TryInt(e[4], out int hit) ? hit : 0; stats.CriticalFailure = TryInt(e[5], out int failure) ? failure : 0;
                    stats.LineOnly = e[6] == "true"; stats.LineOfSight = e[7] == "true";
                }
                else { stats.Source = "inconnue"; stats.HasData = false; }
                return stats;
            }
            int classAp, classMin, classMax;
            if (cc != null && cc.Count >= 9 && TryInt(cc[2], out classAp) && TryInt(cc[3], out classMin) && TryInt(cc[4], out classMax)
                && classAp >= 0 && classMin >= 0 && classMax >= classMin)
            {
                stats.Source = "classe"; stats.ActionPoints = classAp; stats.MinRange = classMin; stats.MaxRange = classMax;
                stats.CriticalHit = TryInt(cc[5], out int hit) ? hit : 0; stats.CriticalFailure = TryInt(cc[6], out int failure) ? failure : 0;
                stats.LineOnly = Bool(cc[7]); stats.LineOfSight = Bool(cc[8]);
            }
            return stats;
        }

        /// <summary><c>cc</c> de la classe (tableau JSON écrit par <c>lang2xml.py</c>) ; null si absent ou illisible.</summary>
        private static JArray ClassCloseCombat(int classId)
        {
            if (classId <= 0) return null;
            IReadOnlyDictionary<string, string> data = LangData.Raw("classes", "G", classId.ToString(CultureInfo.InvariantCulture));
            if (data == null || !data.TryGetValue("cc", out string text) || string.IsNullOrWhiteSpace(text)) return null;
            try { return JArray.Parse(text); }
            catch (Newtonsoft.Json.JsonException) { return null; }
        }

        private static int[] States(JArray cc, int index)
        {
            if (cc.Count <= index || !(cc[index] is JArray list)) return None;
            var states = new List<int>();
            foreach (JToken token in list) if (TryInt(token, out int state)) states.Add(state);
            return states.Distinct().ToArray();
        }

        private static bool TryInt(JToken token, out int value)
        {
            value = 0;
            if (token == null || token.Type != JTokenType.Integer) return false;
            long number = token.Value<long>();
            if (number < int.MinValue || number > int.MaxValue) return false;
            value = (int)number; return true;
        }
        private static bool TryInt(string text, out int value) =>
            int.TryParse(text?.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
        private static bool Bool(JToken token) => token != null && token.Type == JTokenType.Boolean && token.Value<bool>();
    }
}
