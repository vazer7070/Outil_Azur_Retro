using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace Tool_BotProtocol.Game.Jobs
{
    /// <summary>
    /// Options d'artisan d'un métier (<c>JO&lt;position&gt;|&lt;options&gt;|&lt;cases minimum&gt;</c>, <c>dofus.datacenter.JobOptions</c>
    /// du client 1.34) : bit 1 payant, bit 2 gratuit sur échec, bit 4 ne fournit aucune ressource ; nombre minimum
    /// d'ingrédients d'une recette, ramené à 2 en deçà (comme le client).
    /// </summary>
    public sealed class JobOptions
    {
        public const int NotFreeFlag = 1, FreeIfFailedFlag = 2, ResourcesNeededFlag = 4;

        public JobOptions(int flags, int minSlots)
        {
            Flags = flags & 7;
            MinSlots = minSlots > 1 ? minSlots : 2;
        }

        public int Flags { get; }
        public int MinSlots { get; }
        public bool IsNotFree => (Flags & NotFreeFlag) != 0;
        public bool IsFreeIfFailed => (Flags & FreeIfFailedFlag) != 0;
        /// <summary>« Ne fournit aucune ressource » : le client doit apporter les ingrédients.</summary>
        public bool ResourcesNeeded => (Flags & ResourcesNeededFlag) != 0;

        /// <summary>Options composées comme <c>JobOptionsViewer</c> (gratuit sur échec n'a de sens que si le métier est payant).</summary>
        public static int Compose(bool notFree, bool freeIfFailed, bool resourcesNeeded) =>
            (notFree ? NotFreeFlag : 0) + (notFree && freeIfFailed ? FreeIfFailedFlag : 0) + (resourcesNeeded ? ResourcesNeededFlag : 0);

        public static readonly JobOptions Default = new JobOptions(0, 2);
    }

    /// <summary>
    /// Métier du personnage : niveau et expérience (<c>JX</c>), compétences (<c>JS</c>), options d'artisan (<c>JO</c>) ;
    /// modèle commun (<see cref="AllJobs"/>) lu de l'export <c>BotJobs</c> du serveur : nom, outils et recettes acceptées.
    /// </summary>
    public class Jobs
    {
        public int ID { get; private set; }
        public int Level { get; set; }
        public string name { get; private set; }
        /// <summary>Expérience du début du niveau (<c>xpMin</c>).</summary>
        public uint BaseXP { get; private set; }
        public uint ActualXP { get; private set; }
        /// <summary>Expérience du niveau suivant (<c>xpMax</c>) ; 0 au niveau 100.</summary>
        public uint NextXP { get; private set; }
        /// <summary>Compétences du dernier <c>JS</c> ; la liste est remplacée (jamais modifiée) à chaque mise à jour.</summary>
        public List<JobSkills> Skills { get; private set; }
        public JobOptions Options { get; private set; } = JobOptions.Default;
        /// <summary>Modèles d'outils du métier d'après l'export du serveur (<c>jobs_data.tools</c>) ; vide sans export.</summary>
        public IReadOnlyList<int> Tools { get; private set; } = new int[0];
        /// <summary>Recettes acceptées par le serveur pour chaque compétence (<c>jobs_data.crafts</c>, <c>Job.canCraft</c>) ; vide sans export.</summary>
        public IReadOnlyDictionary<short, int[]> ServerCrafts { get; private set; } = new Dictionary<short, int[]>();

        public static ConcurrentDictionary<int, Jobs> AllJobs = new ConcurrentDictionary<int, Jobs>();

        static string Jobspath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ressources", "Bot", "BotJobs");

        /// <summary>Lit l'export <c>BotJobs</c> (un fichier par métier : <c>ID</c>, <c>NOM</c>, <c>TOOLS</c>, <c>CRAFTS</c>) hors du thread de l'interface.</summary>
        public static Task LoadAllJobsAsync() => Task.Run(() => { LoadAllJobs(Jobspath); });

        /// <summary>
        /// Lecture synchrone d'un dossier d'export ; un fichier illisible est ignoré et compté. Renvoie le nombre de métiers lus.
        /// Un dossier absent laisse un modèle vide (les noms viennent alors des textes du client).
        /// </summary>
        public static int LoadAllJobs(string folder)
        {
            var loaded = new ConcurrentDictionary<int, Jobs>();
            int failures = 0;
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                foreach (string file in Directory.EnumerateFiles(folder, "*.xml"))
                {
                    try
                    {
                        Jobs job = ReadTemplate(file);
                        if (job != null) loaded[job.ID] = job; else failures++;
                    }
                    catch (Exception error) when (error is IOException || error is XmlException || error is UnauthorizedAccessException || error is FormatException)
                    {
                        failures++;
                    }
                }
            AllJobs = loaded;
            LoadFailures = failures;
            return loaded.Count;
        }

        /// <summary>Fichiers <c>BotJobs</c> illisibles au dernier chargement.</summary>
        public static int LoadFailures { get; private set; }

        private static Jobs ReadTemplate(string file)
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            XElement xml;
            using (XmlReader reader = XmlReader.Create(file, settings)) xml = XElement.Load(reader);
            if (!int.TryParse(((string)xml.Element("ID") ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) || id <= 0) return null;
            return new Jobs(0)
            {
                ID = id,
                name = ((string)xml.Element("NOM") ?? string.Empty).Trim(),
                Tools = ParseTools((string)xml.Element("TOOLS")),
                ServerCrafts = ParseCrafts((string)xml.Element("CRAFTS"))
            };
        }

        /// <summary><c>tools</c> de StarLoco : modèles séparés par des virgules (entrées illisibles ignorées).</summary>
        public static IReadOnlyList<int> ParseTools(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return new int[0];
            return value.Split(',').Select(part => int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int tool) ? tool : 0)
                .Where(tool => tool > 0).Distinct().ToArray();
        }

        /// <summary><c>crafts</c> de StarLoco : <c>&lt;compétence&gt;;&lt;modèle&gt;,&lt;modèle&gt;|…</c> (<c>Job</c> de StarLoco) ; entrées illisibles ignorées.</summary>
        public static IReadOnlyDictionary<short, int[]> ParseCrafts(string value)
        {
            var crafts = new Dictionary<short, int[]>();
            if (string.IsNullOrWhiteSpace(value)) return crafts;
            foreach (string entry in value.Split('|'))
            {
                string[] parts = entry.Split(';');
                if (parts.Length < 2 || !short.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out short skill)) continue;
                int[] templates = parts[1].Split(',').Select(part => int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int template) ? template : 0)
                    .Where(template => template > 0).Distinct().ToArray();
                if (templates.Length > 0) crafts[skill] = templates;
            }
            return crafts;
        }

        public Jobs(int id = 0)
        {
            Skills = new List<JobSkills>();
            if (id != 0)
            {
                ID = id;
                if (AllJobs.TryGetValue(id, out Jobs template))
                {
                    name = string.IsNullOrEmpty(template.name) ? JobCatalog.JobName(id) : template.name;
                    Tools = template.Tools;
                    ServerCrafts = template.ServerCrafts;
                }
                else name = JobCatalog.JobName(id);
            }
        }

        public double GetXpPercentage => NextXP <= BaseXP ? (Level >= 100 ? 100 : 0)
            : Math.Round(Math.Max(0, Math.Min(100, ((double)ActualXP - BaseXP) / (NextXP - BaseXP) * 100)), 2);

        /// <summary>Numéro de l'icône du client (<c>clips/jobs/&lt;g&gt;.swf</c>), ou <c>null</c>.</summary>
        public int? Icon => JobCatalog.JobIcon(ID);

        /// <summary>Compétences d'atelier (artisanat) du métier.</summary>
        public IReadOnlyList<JobSkills> CraftSkills => (Skills ?? new List<JobSkills>()).Where(skill => skill != null && skill.CanCraft).ToArray();
        /// <summary>Vrai si le métier a au moins une compétence d'atelier (options d'artisan, mode public).</summary>
        public bool IsCraftJob => CraftSkills.Count > 0;
        /// <summary>Plus grand nombre de cases des compétences d'atelier (<c>Job.getMaxSkillSlot</c>), 0 sans atelier.</summary>
        public int MaxSkillSlots => CraftSkills.Select(skill => skill.Slots).DefaultIfEmpty(0).Max();

        /// <summary>
        /// Recettes connues (<c>dofus.datacenter.Job.crafts</c>) : pour chaque compétence d'atelier (la plus grande si elle est
        /// répétée), recettes des textes du client dont le nombre d'ingrédients tient dans ses cases ; triées par nom.
        /// </summary>
        public IReadOnlyList<Recipe> Recipes
        {
            get
            {
                var recipes = new List<Recipe>();
                foreach (var group in CraftSkills.GroupBy(skill => skill.Id))
                {
                    JobSkills best = group.OrderByDescending(skill => skill.Slots).First();
                    recipes.AddRange(JobCatalog.Recipes(best.Id, best.Slots));
                }
                return recipes.GroupBy(recipe => recipe.ResultTemplateId).Select(group => group.First())
                    .OrderBy(recipe => recipe.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
            }
        }

        /// <summary>
        /// Vrai si le serveur accepte de fabriquer ce modèle avec cette compétence (<c>Job.canCraft</c> de StarLoco) d'après
        /// l'export <c>BotJobs</c> ; sans export pour la compétence, d'après les recettes du client (<c>SK.cl</c>).
        /// </summary>
        public bool CanCraft(short skillId, int templateId)
        {
            if (ServerCrafts.TryGetValue(skillId, out int[] templates)) return templates.Contains(templateId);
            return JobCatalog.SkillRecipeIds(skillId).Contains(templateId);
        }

        /// <summary>Vrai si le modèle est un outil du métier (export <c>BotJobs</c>).</summary>
        public bool IsTool(int templateId) => Tools.Contains(templateId);

        public void AcutalizeJob(int level, uint basexp, uint actualxp, uint nextxp)
        {
            Level = level;
            BaseXP = basexp;
            ActualXP = actualxp;
            NextXP = nextxp;
        }

        /// <summary>Remplace les compétences (nouvelle liste : les lecteurs de l'ancienne ne sont pas perturbés).</summary>
        internal void ReplaceSkills(IEnumerable<JobSkills> skills) => Skills = (skills ?? Enumerable.Empty<JobSkills>()).Where(skill => skill != null).ToList();

        internal void SetOptions(JobOptions options) => Options = options ?? JobOptions.Default;

        public override string ToString() => (string.IsNullOrEmpty(name) ? JobCatalog.JobName(ID) : name) + " (" + Level.ToString(CultureInfo.CurrentCulture) + ")";
    }
}
