using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Tool_BotProtocol.Game.Maps.Interactives
{
    /// <summary>
    /// Catégorie d'un objet interactif, numérotée comme le champ <c>t</c> des objets interactifs du client 1.34
    /// (1 récolte, 2 atelier, 3 zaap, 5 porte de maison, 6 coffre, 10 zaapi, 13 enclos ; 0 autre).
    /// </summary>
    public enum InteractiveType
    {
        Other = 0,
        Harvest = 1,
        Workshop = 2,
        Zaap = 3,
        House = 5,
        Chest = 6,
        Zaapi = 10,
        MountPark = 13,
    }

    /// <summary>
    /// Définition d'un objet interactif, chargée depuis <c>ressources/Bot/BotInteractives/&lt;gfx&gt;.xml</c> (export du
    /// parseur d'Azur : <c>interactive_objects_data</c>, compétences des ateliers de la table des métiers et compétences
    /// que le serveur accepte par gfx). Le gfx est celui de la couche objet 2 d'une cellule marquée interactive.
    /// </summary>
    public class InteractivesParent
    {
        private static readonly short[] NoSkills = new short[0];
        private static readonly int[] NoJobs = new int[0];
        private static volatile Dictionary<short, InteractivesParent> byGfx = new Dictionary<short, InteractivesParent>();

        public int Id { get; private set; }
        public short Gfx { get; private set; }
        public string Name { get; private set; }
        public InteractiveType Type { get; private set; }
        /// <summary>Compétences acceptées par le serveur pour <c>GA500&lt;cellule&gt;;&lt;compétence&gt;</c>.</summary>
        public IReadOnlyList<short> Skills => Capacities;
        /// <summary>Métiers dont la table des métiers rattache des compétences d'atelier à ce gfx.</summary>
        public IReadOnlyList<int> Jobs { get; private set; } = NoJobs;
        public bool Walkable { get; private set; }
        /// <summary>Délai de réapparition de la ressource en millisecondes (0 si le serveur n'a pas de modèle).</summary>
        public int RespawnMs { get; private set; }
        /// <summary>Durée d'utilisation annoncée par le serveur dans <c>GA;501</c>, en millisecondes.</summary>
        public int DurationMs { get; private set; }
        /// <summary>Vrai si l'objet figure dans la table du serveur, faux s'il n'est connu que par ses compétences.</summary>
        public bool FromTable { get; private set; }

        /// <summary>Compétences, sous la forme lue par les gestionnaires historiques (récolte, zaap).</summary>
        public short[] Capacities { get; private set; } = NoSkills;
        public short[] GFX_Arrays { get; private set; } = NoSkills;
        public bool Recoltable => Type == InteractiveType.Harvest;

        public static string DefaultPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ressources", "Bot", "BotInteractives");
        public static string[] LoadWarnings { get; private set; } = new string[0];
        public static int Count => byGfx.Count;

        private InteractivesParent() { }

        public bool HasSkill(short skill) => Array.IndexOf(Capacities, skill) >= 0;

        /// <summary>Lit une fiche <c>RECORD</c> ; lève <see cref="FormatException"/> si elle est illisible.</summary>
        public static InteractivesParent Parse(XElement record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            short gfx = ShortValue(record, "GFX", true);
            if (gfx <= 0) throw new FormatException("Le gfx d'un objet interactif doit être positif.");
            short[] skills = List(record, "COMPETENCES").Select(value => checked((short)value)).Distinct().ToArray();
            int type = Number(record, "TYPE", false);
            return new InteractivesParent
            {
                Id = Number(record, "ID", true),
                Gfx = gfx,
                GFX_Arrays = new[] { gfx },
                Name = ((string)record.Element("NOM") ?? "").Trim(),
                Type = Enum.IsDefined(typeof(InteractiveType), type) ? (InteractiveType)type : InteractiveType.Other,
                Capacities = skills,
                Jobs = List(record, "METIERS").Distinct().ToArray(),
                Walkable = Number(record, "MARCHABLE", false) == 1,
                RespawnMs = Math.Max(0, Number(record, "REAPPARITION", false)),
                DurationMs = Math.Max(0, Number(record, "DUREE", false)),
                FromTable = !string.Equals((string)record.Element("SOURCE"), "serveur", StringComparison.Ordinal),
            };
        }

        public static Task LoadAllInteractivesAsync() => LoadAllInteractivesAsync(DefaultPath);

        /// <summary>
        /// Charge un dossier hors du thread appelant. Les fichiers illisibles sont écartés et listés dans
        /// <see cref="LoadWarnings"/> ; un dossier absent donne une liste vide.
        /// </summary>
        public static Task LoadAllInteractivesAsync(string directory)
        {
            return Task.Run(() =>
            {
                var loaded = new Dictionary<short, InteractivesParent>();
                var warnings = new List<string>();
                if (Directory.Exists(directory))
                {
                    foreach (string file in Directory.EnumerateFiles(directory, "*.xml"))
                    {
                        try
                        {
                            InteractivesParent parent = Parse(XElement.Load(file));
                            if (loaded.ContainsKey(parent.Gfx)) throw new FormatException("gfx " + parent.Gfx + " déjà défini.");
                            loaded[parent.Gfx] = parent;
                        }
                        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException
                            || error is System.Xml.XmlException || error is FormatException || error is OverflowException)
                        { warnings.Add(Path.GetFileName(file) + " : " + error.Message); }
                    }
                }
                else warnings.Add("Dossier absent : " + directory);
                byGfx = loaded;
                LoadWarnings = warnings.ToArray();
            });
        }

        public static InteractivesParent ReturnByGFX(short gfx_id)
        {
            InteractivesParent parent;
            return byGfx.TryGetValue(gfx_id, out parent) ? parent : null;
        }

        public static InteractivesParent GetInteractiveBySkill(short skillID)
        {
            return byGfx.Values.OrderBy(parent => parent.Gfx).FirstOrDefault(parent => parent.HasSkill(skillID));
        }

        public static List<InteractivesParent> GetInteractives() => byGfx.Values.OrderBy(parent => parent.Gfx).ToList();

        private static int Number(XElement record, string name, bool required)
        {
            string text = ((string)record.Element(name) ?? "").Trim();
            if (text.Length == 0)
            {
                if (required) throw new FormatException("Élément " + name + " absent.");
                return 0;
            }
            int value;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                throw new FormatException("Élément " + name + " invalide : « " + text + " ».");
            return value;
        }

        private static short ShortValue(XElement record, string name, bool required) => checked((short)Number(record, name, required));

        private static IEnumerable<int> List(XElement record, string name)
        {
            string text = ((string)record.Element(name) ?? "").Trim();
            if (text.Length == 0) return Enumerable.Empty<int>();
            return text.Split(',').Select(part =>
            {
                int value;
                if (!int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value <= 0)
                    throw new FormatException("Élément " + name + " invalide : « " + text + " ».");
                return value;
            }).ToList();
        }
    }
}
