using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Perso.Inventory;

namespace Tool_BotProtocol.Game.Montures
{
    /// <summary>
    /// Fiche d'une monture telle que <c>dofus.aks.Mount.createMount</c> du client 1.34 la lit : champs séparés par « : »
    /// (<c>Mount.parse</c> de StarLoco) — identifiant, modèle, ancêtres (« , »), capacités (« , », 0 ignoré), nom, sexe,
    /// <c>xp,xpMin,xpMax</c>, niveau, montable (0/1), pods max, sauvage (0/1), <c>endurance,max</c>, <c>maturité,max</c>,
    /// <c>énergie,max</c>, <c>sérénité,min,max</c>, <c>amour,max</c>, fécondation (heures, -1 sans), fécondable (0/10),
    /// effets (format des objets), <c>fatigue,max</c>, <c>reproductions,max</c>. Le modèle donne le nom, le gfx et les
    /// couleurs de <c>rides_fr</c> (<c>RI[id] = {n, g, c1, c2, c3}</c>), les capacités le texte de <c>RIA[id].n</c>.
    /// Une fiche n'est jamais modifiée après sa lecture : un nouveau nom (<c>Rn</c>) produit une copie.
    /// </summary>
    public sealed class Mount
    {
        /// <summary>Gfx de repli d'une dragodinde lorsque <c>rides_fr</c> est absent.</summary>
        public const int DefaultGfx = 7002;
        /// <summary>Taux d'expérience donné maximal (<c>PopupQuantity</c> de l'interface <c>Mount</c>, <c>max: 90</c>).</summary>
        public const int MaxXpPercent = 90;
        /// <summary>Niveau du personnage requis pour monter (<c>toogleOnMount</c> de StarLoco).</summary>
        public const int RideLevel = 60;
        /// <summary>Capacité « caméléone » (<c>isChameleon</c>).</summary>
        public const int ChameleonCapacity = 9;

        public int Id { get; private set; }
        /// <summary>Modèle (couleur), clé de <c>RI</c>.</summary>
        public int ModelId { get; private set; }
        public IReadOnlyList<string> Ancestors { get; private set; } = new string[0];
        public IReadOnlyList<int> Capacities { get; private set; } = new int[0];
        /// <summary>Nom transmis ; vide lorsque la monture n'en a pas (voir <see cref="DisplayName"/>).</summary>
        public string Name { get; private set; } = string.Empty;
        /// <summary>0 : mâle, autre : femelle (<c>getToolTip</c>).</summary>
        public int Sex { get; private set; }
        public bool IsFemale => Sex != 0;
        public long Xp { get; private set; }
        public long XpMin { get; private set; }
        public long XpMax { get; private set; }
        public int Level { get; private set; }
        public bool Mountable { get; private set; }
        public int PodsMax { get; private set; }
        public bool Wild { get; private set; }
        public int Stamina { get; private set; }
        public int StaminaMax { get; private set; }
        public int Maturity { get; private set; }
        public int MaturityMax { get; private set; }
        public int Energy { get; private set; }
        public int EnergyMax { get; private set; }
        public int Serenity { get; private set; }
        public int SerenityMin { get; private set; }
        public int SerenityMax { get; private set; }
        public int Love { get; private set; }
        public int LoveMax { get; private set; }
        /// <summary>Heures depuis la fécondation ; 0 ou -1 : pas fécondée.</summary>
        public int Fecondation { get; private set; }
        public bool Fecondable { get; private set; }
        /// <summary>Effets bruts (<c>hex#hex#hex#hex,…</c>, <c>setEffects</c>).</summary>
        public string Effects { get; private set; } = string.Empty;
        public int Tired { get; private set; }
        public int TiredMax { get; private set; }
        /// <summary>Reproductions faites ; -1 : castrée.</summary>
        public int Reproductions { get; private set; }
        public int ReproductionsMax { get; private set; }
        /// <summary>Monture née dans l'étable (<c>Ee~</c>).</summary>
        public bool NewBorn { get; private set; }
        /// <summary>Pods portés, connus par <c>Ew</c> (sacoches ouvertes) ; null tant qu'ils n'ont pas été reçus.</summary>
        public int? Pods { get; private set; }

        public bool IsPregnant => Fecondation > 0;
        public bool IsCastrated => Reproductions == -1;
        public bool IsSterile => !IsCastrated && ReproductionsMax > -1 && Reproductions == ReproductionsMax;
        public bool IsChameleon => Capacities.Contains(ChameleonCapacity);

        /// <summary>Nom affiché : celui transmis, sinon <c>NO_NAME</c> du client.</summary>
        public string DisplayName => Name.Length > 0 ? Name : Text("NO_NAME", "Sans nom");

        /// <summary>Nom du modèle (<c>RI[id].n</c>), sinon « Monture n° X ».</summary>
        public string ModelName => Ride("n") ?? "Monture n° " + ModelId.ToString(CultureInfo.InvariantCulture);

        /// <summary>Gfx du modèle (<c>RI[id].g</c>) ; 7002 sans fichier de langue.</summary>
        public int Gfx => int.TryParse(Ride("g"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int gfx) && gfx > 0 ? gfx : DefaultGfx;

        /// <summary>Couleurs du modèle (<c>c1</c>, <c>c2</c>, <c>c3</c>) ; -1 ou absente : couleur d'origine du sprite.</summary>
        public IReadOnlyList<int> Colors => new[] { RideColor("c1"), RideColor("c2"), RideColor("c3") };

        /// <summary>Texte des capacités (<c>RIA[id].n</c>).</summary>
        public IReadOnlyList<string> CapacityNames => Capacities.Select(CapacityName).ToArray();

        /// <summary>Lignes d'effets comme l'onglet « Effets » de <c>MountViewer</c>.</summary>
        public IReadOnlyList<string> EffectLines
        {
            get
            {
                try { return ItemEffects.DescribeAll(Effects); }
                catch (Exception) { return new string[0]; }
            }
        }

        /// <summary>Avancement dans le niveau (0 à 100) entre <see cref="XpMin"/> et <see cref="XpMax"/>.</summary>
        public int XpProgress => Percent(Xp - XpMin, XpMax - XpMin);

        /// <summary>Texte d'état de reproduction de <c>MountViewer</c> : fécondée, féconde, castrée, stérile ou reproductions.</summary>
        public string ReproductionText
        {
            get
            {
                if (IsPregnant) return Text("PREGNANT_SINCE", "Fécondée depuis " + Fecondation + " heures", Fecondation.ToString(CultureInfo.InvariantCulture));
                if (Fecondable) return Text("FECONDABLE", "Féconde");
                if (IsCastrated) return Text("CASTRATED", "Castrée");
                if (IsSterile) return Text("STERILE", "Stérile");
                string max = ReproductionsMax > -1 ? ReproductionsMax.ToString(CultureInfo.InvariantCulture) : Text("UNLIMITED_WORD", "Illimité").Trim();
                return Text("REPRODUCTIONS", "Reproductions").Trim() + " : " + Reproductions.ToString(CultureInfo.InvariantCulture) + " / " + max;
            }
        }

        public string SexText => IsFemale ? Text("ANIMAL_WOMEN", "Femelle") : Text("ANIMAL_MEN", "Mâle");

        /// <summary>Résumé d'une ligne (journal, infobulle) : modèle, nom, niveau, sexe.</summary>
        public string Describe() =>
            ModelName + " « " + DisplayName + " », niveau " + Level.ToString(CultureInfo.InvariantCulture) + ", " + SexText.ToLowerInvariant()
            + (Mountable ? ", montable" : string.Empty);

        /// <summary>
        /// Lit une fiche ; renvoie <c>null</c> si l'identifiant, le modèle ou l'un des champs numériques est illisible
        /// (le client n'afficherait que des NaN). Les champs absents en fin de fiche valent 0.
        /// </summary>
        public static Mount Parse(string record, bool newBorn = false)
        {
            if (string.IsNullOrEmpty(record)) return null;
            string[] f = record.Split(':');
            if (f.Length < 2 || !TryInt(f[0], out int id) || !TryInt(f[1], out int model)) return null;
            var mount = new Mount { Id = id, ModelId = model, NewBorn = newBorn };
            try
            {
                mount.Ancestors = Field(f, 2).Split(',').ToArray();
                var capacities = new List<int>();
                foreach (string value in Field(f, 3).Split(','))
                    if (TryInt(value, out int capacity) && capacity != 0 && !capacities.Contains(capacity)) capacities.Add(capacity);
                mount.Capacities = capacities.ToArray();
                if (f.Length <= 4) return mount;
                mount.Name = Field(f, 4);
                mount.Sex = Int(Field(f, 5));
                long[] xp = Longs(Field(f, 6), 3);
                mount.Xp = xp[0]; mount.XpMin = xp[1]; mount.XpMax = xp[2];
                mount.Level = Int(Field(f, 7));
                mount.Mountable = Int(Field(f, 8)) != 0;
                mount.PodsMax = Int(Field(f, 9));
                mount.Wild = Int(Field(f, 10)) != 0;
                int[] stamina = Ints(Field(f, 11), 2); mount.Stamina = stamina[0]; mount.StaminaMax = stamina[1];
                int[] maturity = Ints(Field(f, 12), 2); mount.Maturity = maturity[0]; mount.MaturityMax = maturity[1];
                int[] energy = Ints(Field(f, 13), 2); mount.Energy = energy[0]; mount.EnergyMax = energy[1];
                int[] serenity = Ints(Field(f, 14), 3); mount.Serenity = serenity[0]; mount.SerenityMin = serenity[1]; mount.SerenityMax = serenity[2];
                int[] love = Ints(Field(f, 15), 2); mount.Love = love[0]; mount.LoveMax = love[1];
                mount.Fecondation = Int(Field(f, 16));
                mount.Fecondable = Int(Field(f, 17)) != 0;
                mount.Effects = Field(f, 18);
                int[] tired = Ints(Field(f, 19), 2); mount.Tired = tired[0]; mount.TiredMax = tired[1];
                int[] reprod = Ints(Field(f, 20), 2); mount.Reproductions = reprod[0]; mount.ReproductionsMax = reprod[1];
            }
            catch (FormatException) { return null; }
            catch (OverflowException) { return null; }
            return mount;
        }

        /// <summary>Copie renommée (<c>Rn</c>).</summary>
        public Mount WithName(string name)
        {
            Mount copy = (Mount)MemberwiseClone();
            copy.Name = name ?? string.Empty;
            return copy;
        }

        /// <summary>Copie avec la charge des sacoches (<c>Ew&lt;pods&gt;;&lt;max&gt;</c>, <c>onMountPods</c>).</summary>
        public Mount WithPods(int pods, int podsMax)
        {
            Mount copy = (Mount)MemberwiseClone();
            copy.Pods = pods; copy.PodsMax = podsMax;
            return copy;
        }

        /// <summary>Nom d'une capacité (<c>RIA[id].n</c>), sinon « Capacité n° X ».</summary>
        public static string CapacityName(int capacity)
        {
            string name = Lang("RIA", capacity, "n");
            return string.IsNullOrWhiteSpace(name) ? "Capacité n° " + capacity.ToString(CultureInfo.InvariantCulture) : name.Trim();
        }

        /// <summary>Description d'une capacité (<c>RIA[id].d</c>), vide sans fichier de langue.</summary>
        public static string CapacityDescription(int capacity) => (Lang("RIA", capacity, "d") ?? string.Empty).Trim();

        /// <summary>Nom d'un modèle (<c>RI[id].n</c>), ou <c>null</c>.</summary>
        public static string ModelNameOf(int model) => Lang("RI", model, "n");

        internal static int Percent(long value, long range)
        {
            if (range <= 0) return value > 0 ? 100 : 0;
            if (value <= 0) return 0;
            return (int)Math.Min(100, value * 100 / range);
        }

        private string Ride(string key) => Lang("RI", ModelId, key);

        private int RideColor(string key) =>
            int.TryParse(Ride(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int color) ? color : -1;

        private static string Lang(string table, int id, string key)
        {
            try
            {
                IReadOnlyDictionary<string, string> row = LangData.Raw("rides", table, id.ToString(CultureInfo.InvariantCulture));
                return row != null && row.TryGetValue(key, out string value) && !string.IsNullOrEmpty(value) && value != "null" ? value : null;
            }
            catch (Exception) { return null; }
        }

        internal static string Text(string key, string fallback, params string[] args) => Exchanges.ExchangeRegistry.Text(key, fallback, args);

        private static string Field(string[] fields, int index) => index < fields.Length ? fields[index] : string.Empty;

        private static bool TryInt(string value, out int result) =>
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

        /// <summary>Entier d'un champ ; vide : 0 ; illisible : <see cref="FormatException"/> (fiche rejetée).</summary>
        private static int Int(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            return int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        private static long Long(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            return long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        private static int[] Ints(string value, int count)
        {
            string[] parts = value.Split(',');
            var result = new int[count];
            for (int i = 0; i < count; i++) result[i] = i < parts.Length ? Int(parts[i]) : 0;
            return result;
        }

        private static long[] Longs(string value, int count)
        {
            string[] parts = value.Split(',');
            var result = new long[count];
            for (int i = 0; i < count; i++) result[i] = i < parts.Length ? Long(parts[i]) : 0;
            return result;
        }
    }
}
