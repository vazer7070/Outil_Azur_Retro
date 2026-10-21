using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Tool_BotProtocol.Game.Data;

namespace Tool_BotProtocol.Game.Perso.Inventory
{
    /// <summary>
    /// Objet vivant (lot X13) lu dans les effets d'une fiche comme <c>Item.updateDataFromEffect</c> du client 1.34, qui ne lit
    /// que le quatrième champ (<see cref="ItemEffect.Special"/>, hexadécimal) : 970 modèle de l'objet vivant associé
    /// (<c>realUnicId</c>), 971 humeur (0 maigrichon, 1 rassasié, sinon obèse), 972 apparence (valeur − 1, et l'objet devient
    /// « à apparence »), 973 type que l'objet prend (<c>_nRealType</c>), 974 points d'expérience, 983 non échangeable.
    /// Niveau, bornes d'expérience et « associé » suivent <c>maxSkin</c>, <c>currentLivingLevelXpMin/Max</c> et
    /// <c>isAssociate</c> (à apparence et d'un type de modèle autre que 113). StarLoco écrit 971 et 973 en décimal et
    /// 970, 972, 974 en hexadécimal (<c>GameObject.parseStatsString</c>) : un chapeau (type 16) arrive en « 3cd#0#0#10 », que
    /// le client et le bot lisent 0x10 = 16. Rien n'est lu sur le disque ; aucune méthode ne lève d'exception.
    /// </summary>
    public sealed class LivingItem
    {
        public const int AssociatedEffect = 970, MoodEffect = 971, SkinEffect = 972, TypeEffect = 973, XpEffect = 974;
        /// <summary>Texte « A mangé le : … » montré sous la barre d'expérience (<c>LivingItemsViewer</c>, effet 808).</summary>
        public const int MealDateEffect = 808;
        /// <summary>Objet non échangeable (<c>_bCanBeExchange = false</c>) : il ne peut pas servir de nourriture.</summary>
        public const int NotExchangeableEffect = 983;
        /// <summary>Type des objets vivants libres (obvijevans) : à apparence mais jamais « associés ».</summary>
        public const int LivingItemType = 113;

        /// <summary><c>LEVEL_STEP</c> du client : expérience qui ouvre chaque niveau (20 niveaux, autant d'apparences).</summary>
        public static readonly IReadOnlyList<int> LevelSteps = new[] { 0, 10, 21, 33, 46, 60, 75, 91, 108, 126, 145, 165, 186, 208, 231, 255, 280, 306, 333, 361 };

        private LivingItem() { }

        /// <summary>Modèle de l'objet vivant porté (effet 970), ou null.</summary>
        public int? AssociatedTemplate { get; private set; }
        /// <summary>Humeur (effet 971) : 0 maigrichon, 1 rassasié, 2 obèse.</summary>
        public int Mood { get; private set; }
        /// <summary>Apparence actuelle, de 0 à <see cref="Level"/> − 1 (effet 972 moins un, 0 sans valeur).</summary>
        public int Skin { get; private set; }
        /// <summary>L'objet porte l'effet 972 (<c>skineable</c> du client).</summary>
        public bool Skineable { get; private set; }
        /// <summary>Type pris par l'objet (effet 973), ou null.</summary>
        public int? RealType { get; private set; }
        /// <summary>Points d'expérience (effet 974).</summary>
        public int Xp { get; private set; }
        /// <summary>Type du modèle de l'objet (<c>realType</c> du client).</summary>
        public int TemplateType { get; private set; }
        /// <summary>Texte de l'effet 808 (date du dernier repas), vide sans repas.</summary>
        public string MealDate { get; private set; } = string.Empty;
        /// <summary>Faux si l'objet porte l'effet 983.</summary>
        public bool CanBeExchanged { get; private set; } = true;

        /// <summary>Type affiché par le client (<c>type</c> : effet 973 s'il est présent, sinon celui du modèle) ; c'est aussi celui des aliments.</summary>
        public int ItemType => RealType ?? TemplateType;
        /// <summary>Objet vivant posé sur un objet (<c>isAssociate</c>) : seul celui-ci se nourrit et se dissocie.</summary>
        public bool IsAssociated => Skineable && TemplateType != LivingItemType;
        /// <summary>Objet vivant libre (type 113) ou associé : la fiche montre la section « objet vivant ».</summary>
        public bool IsLiving => IsAssociated || TemplateType == LivingItemType;
        /// <summary>Niveau, de 1 à 20 (<c>maxSkin</c>) : c'est aussi le nombre d'apparences proposées.</summary>
        public int Level => LevelOf(Xp);
        /// <summary>Expérience du début du niveau, -1 au niveau 20 (<c>currentLivingLevelXpMin</c>).</summary>
        public int LevelXpMin => Level < LevelSteps.Count ? LevelSteps[Level - 1] : -1;
        /// <summary>Expérience du niveau suivant, -1 au niveau 20 (<c>currentLivingLevelXpMax</c>).</summary>
        public int LevelXpMax => Level < LevelSteps.Count ? LevelSteps[Level] : -1;
        /// <summary>Clé de texte du client pour l'humeur (<c>LivingItemsViewer.initTexts</c>).</summary>
        public string MoodKey => Mood == 1 ? "SATISFIED_WORD" : Mood == 0 ? "LEAN" : "FAT";
        /// <summary>Humeur en clair (textes du client, sinon repli).</summary>
        public string MoodText => Text(MoodKey, Mood == 1 ? "Rassasié" : Mood == 0 ? "Maigrichon" : "Obèse");
        /// <summary>Modèle dont le client dessine les apparences : celui de l'objet vivant associé, sinon l'objet lui-même.</summary>
        public int SkinTemplate(int ownTemplate) => IsAssociated && AssociatedTemplate.HasValue ? AssociatedTemplate.Value : ownTemplate;

        /// <summary>Niveau d'après l'expérience : premier palier non atteint, 20 au-delà de 361.</summary>
        public static int LevelOf(int xp)
        {
            for (int level = 1; level < LevelSteps.Count; level++)
                if (xp < LevelSteps[level]) return level;
            return LevelSteps.Count;
        }

        /// <summary>Objet vivant d'une fiche d'inventaire, ou null pour un objet ordinaire.</summary>
        public static LivingItem From(InventoryObjects item) => item == null ? null : Parse(item.Stats, item.Type);

        /// <summary>
        /// Lit les effets d'une fiche (<c>a#b#c#d#texte,…</c>) ; null si l'objet n'est ni à apparence ni du type 113.
        /// <paramref name="templateType"/> : type du modèle (<c>realType</c>).
        /// </summary>
        public static LivingItem Parse(string stats, int templateType)
        {
            var living = new LivingItem { TemplateType = templateType };
            foreach (ItemEffect effect in ItemEffects.Parse(stats))
            {
                switch (effect.Type)
                {
                    case AssociatedEffect: living.AssociatedTemplate = effect.Special; break;
                    case MoodEffect: living.Mood = effect.Special ?? 0; break;
                    case SkinEffect: living.Skin = effect.Special.HasValue ? Math.Max(0, effect.Special.Value - 1) : 0; living.Skineable = true; break;
                    case TypeEffect: living.RealType = effect.Special; break;
                    case XpEffect: living.Xp = Math.Max(0, effect.Special ?? 0); break;
                    case NotExchangeableEffect: living.CanBeExchanged = false; break;
                    case MealDateEffect: living.MealDate = SafeDescribe(effect); break;
                }
            }
            return living.Skineable || templateType == LivingItemType ? living : null;
        }

        /// <summary>
        /// Aliments proposés par <c>ChooseFeed</c> : objets du sac du même type que l'objet vivant, sans apparence, échangeables,
        /// autres que lui. Vide si l'objet n'est pas associé.
        /// </summary>
        public static IReadOnlyList<InventoryObjects> FoodsFor(IEnumerable<InventoryObjects> objects, InventoryObjects item)
        {
            LivingItem living = From(item);
            if (living == null || !living.IsAssociated || objects == null) return new InventoryObjects[0];
            return objects.Where(candidate => IsFoodFor(living, item, candidate)).OrderBy(candidate => candidate.Name, StringComparer.CurrentCulture)
                .ThenBy(candidate => candidate.Inventory_ID).ToList();
        }

        /// <summary>Même filtre que <see cref="FoodsFor"/> pour un seul candidat ; <paramref name="reason"/> dit pourquoi il est refusé.</summary>
        public static bool IsFoodFor(InventoryObjects item, InventoryObjects food, out string reason)
        {
            reason = null;
            LivingItem living = From(item);
            if (living == null || !living.IsAssociated) { reason = (item?.Name ?? "Cet objet") + " n'est pas un objet vivant associé : il ne se nourrit pas."; return false; }
            if (IsFoodFor(living, item, food)) return true;
            if (food == null) reason = "Aucun aliment choisi.";
            else if (ReferenceEquals(food, item) || food.Inventory_ID == item.Inventory_ID) reason = "Un objet vivant ne se nourrit pas de lui-même.";
            else if (food.position != Enums.InventorySlots.NOT_EQUIPPED) reason = food.Name + " doit être dans le sac pour servir de nourriture.";
            else if (food.Type != living.ItemType) reason = food.Name + " n'est pas du type de " + item.Name + " (" + TypeName(living.ItemType) + ").";
            else reason = food.Name + " ne peut pas servir de nourriture (objet vivant ou non échangeable).";
            return false;
        }

        private static bool IsFoodFor(LivingItem living, InventoryObjects item, InventoryObjects food)
        {
            if (food == null || item == null || ReferenceEquals(food, item) || food.Inventory_ID == item.Inventory_ID || food.Qua <= 0) return false;
            if (food.position != Enums.InventorySlots.NOT_EQUIPPED || food.Type != living.ItemType) return false;
            LivingItem other = Parse(food.Stats, food.Type);
            if (other != null && other.Skineable) return false;
            return !ItemEffects.Parse(food.Stats).Any(effect => effect.Type == NotExchangeableEffect);
        }

        private static string SafeDescribe(ItemEffect effect)
        {
            try { return ItemEffects.Describe(effect) ?? string.Empty; }
            catch (Exception error) when (!(error is OutOfMemoryException)) { return effect.Text ?? string.Empty; }
        }

        private static string TypeName(int type)
        {
            try
            {
                string name = LangData.Item.TypeName(type);
                return string.IsNullOrWhiteSpace(name) ? "type " + type.ToString(CultureInfo.InvariantCulture) : name;
            }
            catch (Exception error) when (!(error is OutOfMemoryException)) { return "type " + type.ToString(CultureInfo.InvariantCulture); }
        }

        /// <summary>Texte du client (sans espace final : « Rassasié » en porte un), sinon le repli.</summary>
        public static string Text(string key, string fallback)
        {
            try { return LangData.Text.Has(key) ? (LangData.Text.Get(key) ?? fallback).Trim() : fallback; }
            catch (Exception error) when (!(error is OutOfMemoryException)) { return fallback; }
        }
    }
}
