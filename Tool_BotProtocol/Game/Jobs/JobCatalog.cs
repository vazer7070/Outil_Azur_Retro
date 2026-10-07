using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Tool_BotProtocol.Game.Data;

namespace Tool_BotProtocol.Game.Jobs
{
    /// <summary>Ingrédient d'une recette : modèle d'objet et quantité (<c>crafts_fr</c> : <c>CR[recette] = [[modèle, quantité], …]</c>).</summary>
    public sealed class RecipeIngredient
    {
        public RecipeIngredient(int templateId, int quantity) { TemplateId = templateId; Quantity = quantity; }
        public int TemplateId { get; }
        public int Quantity { get; }
        /// <summary>Nom de l'objet d'après les textes du client, sinon « Objet n° X ».</summary>
        public string Name => JobCatalog.ItemName(TemplateId);
        public override string ToString() => Quantity.ToString(CultureInfo.CurrentCulture) + " × " + Name;
    }

    /// <summary>Recette connue : objet produit et ingrédients, comme <c>dofus.datacenter.Craft</c> du client 1.34.</summary>
    public sealed class Recipe
    {
        public Recipe(int resultTemplateId, IReadOnlyList<RecipeIngredient> ingredients, short skillId)
        {
            ResultTemplateId = resultTemplateId;
            Ingredients = ingredients ?? new RecipeIngredient[0];
            SkillId = skillId;
        }
        public int ResultTemplateId { get; }
        public IReadOnlyList<RecipeIngredient> Ingredients { get; }
        /// <summary>Compétence d'atelier qui fabrique l'objet.</summary>
        public short SkillId { get; }
        /// <summary>Nombre d'ingrédients différents (<c>Craft.itemsCount</c>) : il doit tenir dans les cases de l'atelier.</summary>
        public int ItemsCount => Ingredients.Count;
        public string Name => JobCatalog.ItemName(ResultTemplateId);
        public override string ToString() => Name;
    }

    /// <summary>
    /// Données des métiers tirées des textes du client 1.34 chargés par <see cref="LangData"/> : métiers (<c>jobs_fr</c>),
    /// compétences (<c>skills_fr</c> : nom, métier, objet récolté <c>i</c>, recettes <c>cl</c>, forgemagie) et recettes
    /// (<c>crafts_fr</c>). Sans ces textes, chaque accesseur rend une valeur de repli (numéro, liste vide, <c>null</c>).
    /// Les recettes réellement acceptées par le serveur viennent de l'export <c>BotJobs</c> (<see cref="Jobs.CanCraft"/>).
    /// </summary>
    public static class JobCatalog
    {
        /// <summary>Rune de signature (<c>GameManager.analyseReceipts</c> du client la tolère en plus d'une recette exacte).</summary>
        public const int SigningRune = 7508;

        private static readonly Regex Pair = new Regex(@"\[\s*(-?\d+)\s*,\s*(-?\d+)\s*\]", RegexOptions.CultureInvariant);

        /// <summary>Nom du métier (<c>J[id].n</c>), sinon « Métier n° X ».</summary>
        public static string JobName(int jobId)
        {
            string name = Safe(() => LangData.IsLoaded("jobs") ? LangData.Job.Name(jobId) : null);
            return string.IsNullOrEmpty(name) || name == jobId.ToString(CultureInfo.InvariantCulture) ? "Métier n° " + jobId.ToString(CultureInfo.InvariantCulture) : name;
        }

        /// <summary>Numéro de l'icône du métier (<c>J[id].g</c>, fichier <c>clips/jobs/&lt;g&gt;.swf</c>), ou <c>null</c>.</summary>
        public static int? JobIcon(int jobId) => Safe(() => LangData.IsLoaded("jobs") ? LangData.Job.Icon(jobId) : null);

        /// <summary>Métier dont le métier est une spécialisation (<c>J[id].s</c>), 0 sinon.</summary>
        public static int SpecializationOf(int jobId)
        {
            string value = Attribute("jobs", "metier", jobId, "specialisation");
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parent) ? parent : 0;
        }

        /// <summary>Nom de la compétence (<c>SK[id].d</c>), sinon « Compétence n° X ».</summary>
        public static string SkillName(int skillId)
        {
            string name = Attribute("skills", "competence", skillId, "nom");
            return string.IsNullOrEmpty(name) ? "Compétence n° " + skillId.ToString(CultureInfo.InvariantCulture) : name;
        }

        /// <summary>Métier de la compétence (<c>SK[id].j</c>), ou <c>null</c>.</summary>
        public static int? SkillJob(int skillId) => Int(Attribute("skills", "competence", skillId, "metier"));

        /// <summary>Objet récolté par la compétence (<c>SK[id].i</c>), ou <c>null</c> (atelier, compétence inconnue).</summary>
        public static int? HarvestedItem(int skillId) => Int(Attribute("skills", "competence", skillId, "i"));

        /// <summary>Nom de l'objet interactif où s'exerce la compétence (<c>IO[SK.io].n</c>), ou <c>null</c>.</summary>
        public static string SkillSource(int skillId)
        {
            int? io = Int(Attribute("skills", "competence", skillId, "interactif"));
            if (io == null) return null;
            string name = Safe(() => LangData.IsLoaded("interactiveobjects") ? LangData.Interactive.NameById(io.Value) : null);
            return string.IsNullOrEmpty(name) || name == io.Value.ToString(CultureInfo.InvariantCulture) ? null : name;
        }

        /// <summary>
        /// Vrai pour une compétence d'atelier, faux pour une récolte, <c>null</c> si les textes du client ne la connaissent pas.
        /// Comme <c>JobViewerSkillItem</c> du client : une compétence qui produit un objet (<c>i</c>) est une récolte, toute
        /// autre compétence connue (recettes <c>cl</c>, forgemagie, concassage…) se présente en cases et chance.
        /// </summary>
        public static bool? IsCraftSkill(int skillId)
        {
            IReadOnlyDictionary<string, string> raw = Raw("skills", "competence", skillId);
            if (raw == null) return null;
            return !raw.ContainsKey("i") || string.IsNullOrWhiteSpace(raw["i"]);
        }

        /// <summary>Vrai pour une compétence de forgemagie (<c>Lang.getSkillForgemagus</c> &gt; 0) : l'atelier s'ouvre alors en forgemagie.</summary>
        public static bool IsForgemagusSkill(int skillId)
        {
            int? value = Int(Attribute("skills", "competence", skillId, "forgemagie"));
            return value.HasValue && value.Value > 0;
        }

        /// <summary>Recettes de la compétence (<c>SK[id].cl</c>) dont l'objet existe dans les textes du client (<c>Skill.craftsList</c>).</summary>
        public static IReadOnlyList<int> SkillRecipeIds(int skillId)
        {
            string list = Attribute("skills", "competence", skillId, "cl");
            if (string.IsNullOrWhiteSpace(list)) return new int[0];
            bool items = Safe(() => LangData.IsLoaded("items"));
            return list.Split(',')
                .Select(part => Int(part))
                .Where(id => id.HasValue && (!items || Safe(() => LangData.Item.Has(id.Value))))
                .Select(id => id.Value).Distinct().ToArray();
        }

        /// <summary>Ingrédients de la recette d'un objet (<c>CR[id]</c>), liste vide si elle est inconnue ou illisible.</summary>
        public static IReadOnlyList<RecipeIngredient> Ingredients(int resultTemplateId)
        {
            string value = Attribute("crafts", "CR", resultTemplateId, "valeur");
            if (string.IsNullOrWhiteSpace(value)) return new RecipeIngredient[0];
            var result = new List<RecipeIngredient>();
            foreach (Match match in Pair.Matches(value))
            {
                if (!int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int template)
                    || !int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int quantity)
                    || template <= 0 || quantity <= 0) return new RecipeIngredient[0];
                result.Add(new RecipeIngredient(template, quantity));
            }
            return result;
        }

        /// <summary>
        /// Recettes connues d'une compétence pour un atelier de <paramref name="slots"/> cases, triées par nom
        /// (<c>dofus.datacenter.Job</c> : recettes dont le nombre d'ingrédients tient dans les cases).
        /// </summary>
        public static IReadOnlyList<Recipe> Recipes(short skillId, int slots)
        {
            var recipes = new List<Recipe>();
            foreach (int id in SkillRecipeIds(skillId))
            {
                IReadOnlyList<RecipeIngredient> ingredients = Ingredients(id);
                if (ingredients.Count == 0 || ingredients.Count > slots) continue;
                recipes.Add(new Recipe(id, ingredients, skillId));
            }
            return recipes.OrderBy(recipe => recipe.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }

        /// <summary>
        /// Recette correspondant exactement aux ingrédients posés (modèle → quantité), comme <c>GameManager.analyseReceipts</c> :
        /// chaque ingrédient de la recette présent avec la même quantité, rien d'autre sauf une rune de signature. <c>null</c> sinon.
        /// </summary>
        public static Recipe Analyse(IReadOnlyDictionary<int, int> placed, short skillId, int slots)
        {
            if (placed == null || placed.Count == 0) return null;
            foreach (Recipe recipe in Recipes(skillId, slots))
            {
                if (!recipe.Ingredients.All(ingredient => placed.TryGetValue(ingredient.TemplateId, out int quantity) && quantity == ingredient.Quantity)) continue;
                if (placed.Count == recipe.ItemsCount) return recipe;
                if (placed.Count == recipe.ItemsCount + 1 && placed.ContainsKey(SigningRune)) return recipe;
            }
            return null;
        }

        /// <summary>Vrai si le modèle entre dans une recette de la compétence (<c>GameManager.isItemUseful</c>).</summary>
        public static bool IsUseful(int templateId, short skillId, int slots) =>
            Recipes(skillId, slots).Any(recipe => recipe.Ingredients.Any(ingredient => ingredient.TemplateId == templateId));

        /// <summary>Nom d'un objet d'après les textes du client, sinon « Objet n° X ».</summary>
        public static string ItemName(int templateId)
        {
            string name = Safe(() => LangData.IsLoaded("items") && LangData.Item.Has(templateId) ? LangData.Item.Name(templateId) : null);
            return string.IsNullOrEmpty(name) ? "Objet n° " + templateId.ToString(CultureInfo.InvariantCulture) : name;
        }

        /// <summary>« case » ou « cases » d'après le texte <c>SLOT</c> du client et son accord, sinon le mot du bot.</summary>
        public static string SlotWord(int count)
        {
            string pattern = Safe(() => LangData.Text.Has("SLOT") ? LangData.Text.Get("SLOT") : null);
            string word = string.IsNullOrEmpty(pattern) ? null : Safe(() => LangData.Combine(pattern, "f", count < 2));
            return string.IsNullOrWhiteSpace(word) ? (count < 2 ? "case" : "cases") : word.Trim();
        }

        /// <summary>Texte du client (<c>lang.xml</c>) avec ses paramètres <c>%1</c>…, ou le texte de repli du bot.</summary>
        public static string Text(string key, string fallback, params string[] args)
        {
            try
            {
                if (LangData.Text.Has(key))
                {
                    string value = LangData.Text.Get(key, args);
                    if (!string.IsNullOrWhiteSpace(value) && !value.StartsWith("!", StringComparison.Ordinal)) return value;
                }
            }
            catch (Exception) { /* Textes du client illisibles : le texte du bot suffit. */ }
            return fallback;
        }

        private static IReadOnlyDictionary<string, string> Raw(string family, string table, int id) =>
            Safe(() => LangData.IsLoaded(family) ? LangData.Raw(family, table, id.ToString(CultureInfo.InvariantCulture)) : null);

        private static string Attribute(string family, string table, int id, string name)
        {
            IReadOnlyDictionary<string, string> raw = Raw(family, table, id);
            return raw != null && raw.TryGetValue(name, out string value) ? value : null;
        }

        private static int? Int(string value) =>
            int.TryParse((value ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : (int?)null;

        private static T Safe<T>(Func<T> read)
        {
            try { return read(); }
            catch (Exception error) when (!(error is OutOfMemoryException)) { return default(T); }
        }
    }
}
