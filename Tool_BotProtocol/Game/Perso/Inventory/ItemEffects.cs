using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Perso.Inventory.Enums;

namespace Tool_BotProtocol.Game.Perso.Inventory
{
    /// <summary>
    /// Un effet d'une fiche d'objet du client 1.34 (<c>Item.setEffects</c>) : <c>type#min#max#special#texte</c>, les quatre
    /// premiers champs en hexadécimal ; un champ vide ou « 0 » vaut « absent » pour min, max et special.
    /// </summary>
    public sealed class ItemEffect
    {
        public int Type { get; internal set; }
        public int? Min { get; internal set; }
        public int? Max { get; internal set; }
        public int? Special { get; internal set; }
        /// <summary>Cinquième champ (jet de dés « 1d5+0 », texte libre), vide s'il manque.</summary>
        public string Text { get; internal set; } = string.Empty;
        /// <summary>Champ brut tel que reçu.</summary>
        public string Raw { get; internal set; } = string.Empty;

        /// <summary>Lit un effet ; null si le type n'est pas un nombre hexadécimal.</summary>
        public static ItemEffect Parse(string field)
        {
            if (string.IsNullOrWhiteSpace(field)) return null;
            string[] parts = field.Split('#');
            if (!int.TryParse(parts[0].Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int type)) return null;
            return new ItemEffect
            {
                Type = type, Raw = field,
                Min = Hex(parts, 1), Max = Hex(parts, 2), Special = Hex(parts, 3),
                Text = parts.Length > 4 ? parts[4] : string.Empty
            };
        }

        private static int? Hex(string[] parts, int index)
        {
            if (parts.Length <= index) return null;
            string text = parts[index].Trim();
            if (text.Length == 0 || text == "0") return null;
            return int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value) ? value : (int?)null;
        }
    }

    /// <summary>
    /// Textes des effets et des conditions d'un objet comme le client 1.34 les affiche dans <c>ItemViewer</c> : motifs
    /// <c>effects_fr</c> (<c>E[type].d</c>, lot D4) substitués par <see cref="LangData.Describe"/>, cas particuliers de
    /// <c>Effect.description</c> (sorts, objets, états, alignements, repas des familiers), conditions <c>Item.conditions</c>
    /// (<c>ITEM_CHARACTERISTICS</c>, opérateurs, « ou », objets à posséder). Sans fichiers de langue, chaque accesseur rend
    /// un repli lisible (« Effet 125 : 5 ») et ne lève jamais d'exception.
    /// </summary>
    public static class ItemEffects
    {
        /// <summary><c>OBJECT_ACTION_SUMMON</c> du client : les invocations listent plusieurs identifiants séparés par « : ».</summary>
        public const int SummonEffect = 623;

        /// <summary>Lit une liste d'effets « a#b#c#d#e,… » ; les entrées illisibles sont ignorées.</summary>
        public static IReadOnlyList<ItemEffect> Parse(string stats)
        {
            var effects = new List<ItemEffect>();
            if (string.IsNullOrWhiteSpace(stats)) return effects;
            foreach (string field in stats.Split(','))
            {
                ItemEffect effect = ItemEffect.Parse(field);
                if (effect != null) effects.Add(effect);
            }
            return effects;
        }

        /// <summary>Textes des effets d'une fiche, dans l'ordre, sans les effets au texte vide (comme <c>ItemViewer.updateCurrentTabInformations</c>).</summary>
        public static IReadOnlyList<string> DescribeAll(string stats)
        {
            var lines = new List<string>();
            foreach (ItemEffect effect in Parse(stats))
            {
                if (effect.Type == SummonEffect && effect.Text.Length > 0)
                {
                    // Invocations : un identifiant hexadécimal par monstre, séparés par « : » (EFFECT_APPEND_CHAR).
                    foreach (string id in effect.Text.Split(':'))
                    {
                        int? monster = int.TryParse(id, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value) ? value : (int?)null;
                        string text = Describe(new ItemEffect { Type = effect.Type, Special = monster, Raw = effect.Raw });
                        if (!string.IsNullOrEmpty(text)) lines.Add(text);
                    }
                    continue;
                }
                string line = Describe(effect);
                if (!string.IsNullOrEmpty(line)) lines.Add(line);
            }
            return lines;
        }

        /// <summary>Texte d'un effet ; null si le client n'en afficherait aucun (description vide ou « NOTHING »).</summary>
        public static string Describe(ItemEffect effect)
        {
            if (effect == null) return null;
            string pattern = LangData.Effect.Pattern(effect.Type);
            string p1 = Number(effect.Min), p2 = Number(effect.Max), p3 = Number(effect.Special), p4 = string.IsNullOrEmpty(effect.Text) ? null : effect.Text;
            if (pattern == null) return Fallback(effect);
            if (pattern == "NOTHING" || pattern.Length == 0) return null;
            if (effect.Type == 666) return LangData.Text.Get("DO_NOTHING");
            switch (effect.Type)
            {
                case 281: case 282: case 283: case 284: case 285: case 286: case 287: case 288: case 289: case 290: case 291: case 292:
                    if (effect.Min.HasValue) p1 = LangData.Spell.Name(effect.Min.Value);
                    break;
                case 939: case 940: case 969:
                    if (effect.Special.HasValue) p3 = ItemName(effect.Special.Value);
                    break;
                case 814:
                    if (effect.Special.HasValue) p1 = ItemName(effect.Special.Value);
                    break;
                case 807:
                    p1 = effect.Special.HasValue ? ItemName(effect.Special.Value) : LangData.Text.Get("NO_LAST_MEAL");
                    break;
                case 806:
                    p1 = LangData.Text.Get(effect.Max > 6 ? "FAT" : effect.Special > 6 ? "LEAN" : "NORMAL");
                    break;
                case 950: case 951:
                    if (effect.Special.HasValue) p3 = LangData.Raw("states", "ST", Key(effect.Special.Value))?["n"] ?? p3;
                    break;
                case 960:
                    if (effect.Special.HasValue) p3 = LangData.Raw("alignment", "A.a", Key(effect.Special.Value))?["n"] ?? p3;
                    break;
            }
            string text = LangData.Describe(pattern, new[] { p1, p2, p3, p4 });
            return string.IsNullOrWhiteSpace(text) || text == "null" ? null : text.Trim();
        }

        /// <summary>Sans fichier d'effets : « Effet 125 : 5 à 10 » (valeurs décimales), jamais vide.</summary>
        private static string Fallback(ItemEffect effect)
        {
            string values = effect.Min.HasValue && effect.Max.HasValue ? effect.Min.Value + " à " + effect.Max.Value
                : effect.Min.HasValue ? Number(effect.Min) : effect.Max.HasValue ? Number(effect.Max) : effect.Special.HasValue ? Number(effect.Special) : effect.Text;
            return "Effet " + effect.Type.ToString(CultureInfo.InvariantCulture) + (string.IsNullOrEmpty(values) ? string.Empty : " : " + values);
        }

        /// <summary>Nom d'un modèle d'objet : fichiers de langue, sinon <c>BotObjets</c>, sinon « Objet n° X ».</summary>
        public static string ItemName(int templateId)
        {
            if (LangData.Item.Has(templateId)) return LangData.Item.Name(templateId);
            return InventoryObjects.DisplayName(templateId);
        }

        /// <summary>
        /// Lignes de l'onglet Conditions du client (<c>Item.conditions</c>) : « Force &gt; 4 », « Niveau &gt; 10 », « ou Classe = Féca »,
        /// « Posséder l'objet 'X' » ; « Aucune » (<c>NO_CONDITIONS</c>) sans condition. Sans fichier de langue, l'expression brute est rendue.
        /// </summary>
        public static IReadOnlyList<string> DescribeConditions(string conditions)
        {
            var lines = new List<string>();
            if (string.IsNullOrWhiteSpace(conditions)) { lines.Add(LangData.Text.Has("NO_CONDITIONS") ? LangData.Text.Get("NO_CONDITIONS") : "Aucune"); return lines; }
            string[] names = LangData.Text.Has("ITEM_CHARACTERISTICS") ? LangData.Text.Get("ITEM_CHARACTERISTICS").Split(',') : new string[0];
            foreach (string group in conditions.Split('&'))
            {
                string[] alternatives = group.Replace("(", string.Empty).Replace(")", string.Empty).Split('|');
                for (int index = 0; index < alternatives.Length; index++)
                {
                    string condition = alternatives[index];
                    char op = '\0'; int at = -1;
                    foreach (char candidate in Operators)
                    {
                        at = condition.IndexOf(candidate);
                        if (at > 0) { op = candidate; break; }
                    }
                    if (op == '\0') { if (condition.Trim().Length > 0) lines.Add(condition.Trim()); continue; }
                    string key = condition.Substring(0, at).Trim(), value = condition.Substring(at + 1).Trim();
                    if (key == "PZ" || key == "PM") continue;
                    if (key == "BI") { lines.Add(LangData.Text.Get("UNUSABLE")); continue; }
                    string prefix = index > 0 ? LangData.Text.Get("ITEM_OR") + " " : string.Empty;
                    if (key == "PO")
                    {
                        string name = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int template) ? ItemName(template) : value;
                        lines.Add(prefix + LangData.Text.Get(op == '!' ? "ITEM_DO_NOT_POSSESS" : "ITEM_DO_POSSESS", name));
                        continue;
                    }
                    value = ConditionValue(key, value);
                    int position = Array.IndexOf(ConditionKeys, key);
                    string label = position >= 0 && position < names.Length ? names[position] : key;
                    string symbol = op == '!' ? LangData.Text.Get("ITEM_NO") : op.ToString();
                    lines.Add(prefix + label + " " + symbol + " " + value);
                }
            }
            if (lines.Count == 0) lines.Add(LangData.Text.Has("NO_CONDITIONS") ? LangData.Text.Get("NO_CONDITIONS") : "Aucune");
            return lines;
        }

        private static readonly char[] Operators = { '>', '<', '=', '!' };
        /// <summary>Clés du client, dans l'ordre des libellés de <c>ITEM_CHARACTERISTICS</c>.</summary>
        private static readonly string[] ConditionKeys =
        {
            "CS", "Cs", "CV", "Cv", "CA", "Ca", "CI", "Ci", "CW", "Cw", "CC", "Cc", "CA", "PG", "PJ", "Pj", "PM", "PA", "PN", "PE",
            "<NO>", "PS", "PR", "PL", "PK", "Pg", "Pr", "Ps", "Pa", "PP", "PZ", "CM"
        };

        private static string ConditionValue(string key, string value)
        {
            switch (key)
            {
                case "Ps": return Int(value) is int alignment ? LangData.Raw("alignment", "A.a", Key(alignment))?["n"] ?? value : value;
                case "PS": return LangData.Text.Get(value == "1" ? "FEMELE" : "MALE");
                case "PG": return Int(value) is int classId ? LangData.Raw("classes", "G", Key(classId))?["sn"] ?? value : value;
                case "PJ": case "Pj":
                    {
                        string[] parts = value.Split(',');
                        string job = Int(parts[0]) is int jobId ? LangData.Job.Name(jobId) : parts[0];
                        return parts.Length > 1 ? job + " (" + LangData.Text.Get("LEVEL_SMALL") + " " + parts[1] + ")" : job;
                    }
                default: return value;
            }
        }

        /// <summary>
        /// Onglet Caractéristiques d'une arme (<c>Item.characteristics</c>) : PA, portée, bonus de coups critiques, critique et échec.
        /// Vide si le modèle n'a pas de caractéristiques d'arme dans <c>items_fr</c>.
        /// </summary>
        public static IReadOnlyList<string> DescribeWeapon(int templateId)
        {
            var lines = new List<string>();
            string[] stats = LangData.Item.WeaponStats(templateId);
            if (stats.Length < 6) return lines;
            string criticalBonus = stats[0], ap = stats[1], rangeMin = stats[2], rangeMax = stats[3], critical = stats[4], failure = stats[5];
            lines.Add(LangData.Text.Get("ITEM_AP", ap));
            lines.Add(LangData.Text.Get("ITEM_RANGE", rangeMin == "0" || rangeMin == rangeMax ? rangeMax : rangeMin + " " + LangData.Text.Get("TO_RANGE") + " " + rangeMax));
            lines.Add(LangData.Text.Get("ITEM_CRITICAL_BONUS", Int(criticalBonus) > 0 ? "+" + criticalBonus : criticalBonus));
            string hit = critical != "0" ? LangData.Text.Get("ITEM_CRITICAL", critical) : string.Empty;
            string miss = failure != "0" ? LangData.Text.Get("ITEM_MISS", failure) : string.Empty;
            string line = hit + (hit.Length > 0 && miss.Length > 0 ? " - " : string.Empty) + miss;
            if (line.Length > 0) lines.Add(line);
            return lines;
        }

        private static int? Int(string text) => int.TryParse((text ?? string.Empty).Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value) ? value : (int?)null;
        private static string Number(int? value) => value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : null;
        private static string Key(int id) => id.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Les quatre filtres de la fenêtre d'inventaire du client (<c>FILTER_EQUIPEMENT</c>, <c>FILTER_NONEQUIPEMENT</c>, <c>FILTER_RESSOURECES</c>, <c>FILTER_QUEST</c>).</summary>
    public enum ItemCategory
    {
        Equipment,
        Miscellaneous,
        Resources,
        Quest,
    }

    /// <summary>
    /// Classement et emplacements des objets d'après les textes du client (super-types et <c>I.ss</c>, lot D4), avec le
    /// repli des tables du bot (<see cref="InventoryUtilities"/>) quand ils manquent.
    /// </summary>
    public static class ItemSlots
    {
        /// <summary>Emplacement de la monture (<c>_ctr16</c>, <c>ITEM_POS_DRAGODINDE</c>), hors de l'énumération <see cref="InventorySlots"/>.</summary>
        public const int MountPosition = 16;
        /// <summary>Première position de la barre de raccourcis du client (<c>ITEM_OFFSET</c>) ; au-delà, l'objet n'est pas équipé.</summary>
        public const int ShortcutOffset = 34;

        /// <summary>Super-types équipables d'après <c>Inventory.SUPERTYPE_NOT_EQUIPABLE</c> du client : tout sauf 6, 8, 9, 14 à 22.</summary>
        private static readonly int[] NotEquipable = { 9, 14, 15, 16, 17, 18, 6, 19, 21, 20, 8, 22 };

        /// <summary>Super-type d'un type d'objet (textes du client), ou null.</summary>
        public static int? SuperType(int type) => LangData.Item.SuperType(type);

        /// <summary>Filtre de la fenêtre d'inventaire pour un type d'objet ; <see cref="ItemCategory.Miscellaneous"/> pour les super-types sans filtre (15 à 22) afin que rien ne soit caché.</summary>
        public static ItemCategory CategoryOf(int type)
        {
            int? superType = SuperType(type);
            if (superType.HasValue)
            {
                switch (superType.Value)
                {
                    case 1: case 2: case 3: case 4: case 5: case 7: case 8: case 10: case 11: case 12: case 13: return ItemCategory.Equipment;
                    case 9: return ItemCategory.Resources;
                    case 14: return ItemCategory.Quest;
                    default: return ItemCategory.Miscellaneous;
                }
            }
            switch (InventoryUtilities.GetTypeForObjectInInventory(type > 255 || type < 0 ? (byte)0 : (byte)type))
            {
                case InventoryObjectsTypes.EQUIPMENTS: return ItemCategory.Equipment;
                case InventoryObjectsTypes.RESOURCES: return ItemCategory.Resources;
                case InventoryObjectsTypes.QUEST_ITEMS: return ItemCategory.Quest;
                default: return ItemCategory.Miscellaneous;
            }
        }

        /// <summary>
        /// Emplacements (0 à 16) où un type d'objet peut être posé : <c>I.ss[superType]</c> du client (positions au-delà de 16
        /// ignorées), sinon la table du bot. Vide pour un objet non équipable.
        /// </summary>
        public static IReadOnlyList<int> PositionsFor(int type)
        {
            int? superType = SuperType(type);
            if (superType.HasValue)
            {
                if (Array.IndexOf(NotEquipable, superType.Value) >= 0 && type != 83) return new int[0];
                int[] slots = LangData.Item.SlotsOfSuperType(superType.Value).Where(slot => slot >= 0 && slot <= MountPosition).ToArray();
                if (slots.Length > 0) return slots;
            }
            List<InventorySlots> known = InventoryUtilities.GetPosition(type);
            return known == null ? new int[0] : known.Select(slot => (int)slot).ToArray();
        }

        public static bool Accepts(int position, int type) => position >= 0 && PositionsFor(type).Contains(position);

        /// <summary>Nom français de l'emplacement (le client n'en nomme aucun dans ses textes).</summary>
        public static string Name(int position)
        {
            switch (position)
            {
                case -1: return "Sac";
                case 0: return "Amulette";
                case 1: return "Arme";
                case 2: return "Anneau gauche";
                case 3: return "Ceinture";
                case 4: return "Anneau droit";
                case 5: return "Bottes";
                case 6: return "Coiffe";
                case 7: return "Cape";
                case 8: return "Familier";
                case 9: case 10: case 11: case 12: case 13: case 14: return "Dofus " + (position - 8).ToString(CultureInfo.InvariantCulture);
                case 15: return "Bouclier";
                case MountPosition: return "Monture";
                default: return position >= ShortcutOffset ? "Raccourci " + (position - ShortcutOffset + 1).ToString(CultureInfo.InvariantCulture) : "Position " + position.ToString(CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>
    /// Fiche d'un objet telle que <c>ItemViewer</c> du client la présente : nom, niveau, catégorie, description, effets,
    /// conditions, caractéristiques d'arme, poids, prix, panoplie et drapeaux (deux mains, utilisable, ciblable, maudit).
    /// Construite sans interface, depuis une fiche d'inventaire ou un article de boutique ; les textes viennent des
    /// fichiers de langue du client (lot D4), puis de <c>BotObjets</c>, puis d'un repli numéroté.
    /// </summary>
    public sealed class ItemSheet
    {
        public uint InventoryId { get; private set; }
        public int TemplateId { get; private set; }
        public string Name { get; private set; } = string.Empty;
        public int Level { get; private set; }
        public int Type { get; private set; }
        public string TypeName { get; private set; } = string.Empty;
        public int? SuperType { get; private set; }
        public ItemCategory Category { get; private set; }
        public string Description { get; private set; } = string.Empty;
        public int Quantity { get; private set; }
        public int Position { get; private set; } = -1;
        public int? Weight { get; private set; }
        public int? Price { get; private set; }
        public int? SetId { get; private set; }
        public string SetName { get; private set; }
        public bool TwoHanded { get; private set; }
        public bool Usable { get; private set; }
        public bool Targetable { get; private set; }
        public bool Cursed { get; private set; }
        public bool Ethereal { get; private set; }
        /// <summary>Type et numéro d'image de l'icône <c>Items/&lt;type&gt;/&lt;gfx&gt;.png</c> ; null sans textes du client.</summary>
        public int? IconGfx { get; private set; }
        public string Stats { get; private set; } = string.Empty;
        public string Conditions { get; private set; } = string.Empty;
        public IReadOnlyList<string> Effects { get; private set; } = new string[0];
        public IReadOnlyList<string> ConditionLines { get; private set; } = new string[0];
        public IReadOnlyList<string> Characteristics { get; private set; } = new string[0];
        /// <summary>Vrai quand le modèle est décrit par les textes du client ou par <c>BotObjets</c>.</summary>
        public bool Known { get; private set; }

        public bool IsEquipped => Position >= 0 && Position < ItemSlots.ShortcutOffset;
        /// <summary>Le client refuse de jeter ou de détruire les objets de quête (super-type 14) et les objets maudits.</summary>
        public bool CanDestroy => SuperType != 14 && !Cursed;
        public bool CanDrop => CanDestroy;

        /// <summary>Fiche d'un objet de l'inventaire.</summary>
        public static ItemSheet From(InventoryObjects item)
        {
            if (item == null) return null;
            ItemSheet sheet = FromTemplate(item.ID, item.Stats, item.Qua, null);
            sheet.InventoryId = item.Inventory_ID;
            sheet.Position = (int)item.position;
            if (!LangData.Item.Has(item.ID) && item.HasMetadata)
            {
                sheet.Name = item.Name; sheet.Level = item.Level; sheet.Type = item.Type; sheet.Weight = item.pods;
                sheet.Conditions = item.Conditions ?? string.Empty; sheet.Known = true;
                sheet.TypeName = LangData.Item.TypeName(item.Type);
                sheet.Category = ItemSlots.CategoryOf(item.Type);
                sheet.ConditionLines = ItemEffects.DescribeConditions(sheet.Conditions);
            }
            else if (!sheet.Known && item.HasMetadata) sheet.Known = true;
            return sheet;
        }

        /// <summary>Fiche d'un modèle (article de boutique, objet d'une panoplie) avec ses effets et son prix éventuel.</summary>
        public static ItemSheet FromTemplate(int templateId, string stats, int quantity, int? price)
        {
            var sheet = new ItemSheet { TemplateId = templateId, Quantity = Math.Max(1, quantity), Stats = stats ?? string.Empty, Price = price };
            InventoryObjects metadata = InventoryObjects.ReturnInventory(templateId);
            if (LangData.Item.Has(templateId))
            {
                sheet.Known = true;
                sheet.Name = LangData.Item.Name(templateId);
                sheet.Level = LangData.Item.Level(templateId) ?? metadata?.Level ?? 0;
                sheet.Type = LangData.Item.Type(templateId) ?? metadata?.Type ?? 0;
                sheet.Description = LangData.Item.Description(templateId);
                sheet.Weight = LangData.Item.Pods(templateId) ?? metadata?.pods;
                if (sheet.Price == null) sheet.Price = LangData.Item.Price(templateId);
                sheet.Conditions = LangData.Item.Conditions(templateId);
                if (sheet.Conditions.Length == 0 && metadata != null) sheet.Conditions = metadata.Conditions ?? string.Empty;
                sheet.SetId = LangData.Item.SetId(templateId);
                sheet.TwoHanded = LangData.Item.IsTwoHanded(templateId);
                sheet.Usable = LangData.Item.IsUsable(templateId);
                sheet.Targetable = LangData.Item.IsTargetable(templateId);
                sheet.Cursed = LangData.Item.IsCursed(templateId);
                sheet.Ethereal = LangData.Item.IsEthereal(templateId);
                sheet.IconGfx = LangData.Item.Gfx(templateId);
                sheet.Characteristics = ItemEffects.DescribeWeapon(templateId);
            }
            else if (metadata != null)
            {
                sheet.Known = true;
                sheet.Name = string.IsNullOrEmpty(metadata.Name) ? InventoryObjects.DisplayName(templateId) : metadata.Name;
                sheet.Level = metadata.Level; sheet.Type = metadata.Type; sheet.Weight = metadata.pods;
                sheet.Conditions = metadata.Conditions ?? string.Empty;
            }
            else sheet.Name = InventoryObjects.DisplayName(templateId);
            if (sheet.SetId == null)
            {
                ItemSet serverSet = ItemSets.ForItem(templateId);
                if (serverSet != null) sheet.SetId = serverSet.Id;
            }
            if (sheet.SetId.HasValue) sheet.SetName = NameOfSet(sheet.SetId.Value);
            sheet.TypeName = LangData.Item.TypeName(sheet.Type);
            sheet.SuperType = ItemSlots.SuperType(sheet.Type);
            sheet.Category = ItemSlots.CategoryOf(sheet.Type);
            sheet.Effects = ItemEffects.DescribeAll(sheet.Stats);
            sheet.ConditionLines = ItemEffects.DescribeConditions(sheet.Conditions);
            return sheet;
        }

        /// <summary>Nom d'une panoplie : textes du client, sinon export <c>BotItemSets</c> du serveur (lot D5), sinon « Panoplie n° X ».</summary>
        public static string NameOfSet(int setId)
        {
            if (LangData.ItemSet.Has(setId)) return LangData.ItemSet.Name(setId);
            ItemSet set = ItemSets.Get(setId);
            return set != null && !string.IsNullOrEmpty(set.Name) ? set.Name : "Panoplie n° " + setId.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Modèles d'une panoplie : textes du client, sinon export du serveur, sinon vide.</summary>
        public static IReadOnlyList<int> ItemsOfSet(int setId)
        {
            int[] items = LangData.ItemSet.Items(setId);
            if (items.Length > 0) return items;
            return ItemSets.Get(setId)?.Items ?? new int[0];
        }

        /// <summary>« Niv. 12 » (<c>LEVEL_SMALL</c>).</summary>
        public string LevelText => (LangData.Text.Has("LEVEL_SMALL") ? LangData.Text.Get("LEVEL_SMALL") : "Niv.") + " " + Level.ToString(CultureInfo.InvariantCulture);

        /// <summary>« 20 pods » avec l'accord du client (<c>PODS</c> : « pod{~ps} »).</summary>
        public string WeightText
        {
            get
            {
                if (!Weight.HasValue) return string.Empty;
                string unit = LangData.Text.Has("PODS") ? LangData.Describe(LangData.Combine(LangData.Text.Get("PODS"), "m", Weight.Value < 2), new string[0]) : (Weight.Value < 2 ? "pod" : "pods");
                return Weight.Value.ToString(CultureInfo.InvariantCulture) + " " + unit;
            }
        }

        /// <summary>Lignes de la fiche, prêtes pour une infobulle texte : nom (niveau), catégorie, effets, conditions, poids, prix.</summary>
        public IReadOnlyList<string> ToLines(bool withDescription = false)
        {
            var lines = new List<string> { Name + " (" + LevelText + ")" + (Quantity > 1 ? " × " + Quantity.ToString(CultureInfo.InvariantCulture) : string.Empty) };
            string category = (LangData.Text.Has("ITEM_TYPE") ? LangData.Text.Get("ITEM_TYPE") : "Catégorie") + " : " + TypeName;
            lines.Add(SetName != null ? SetName + " (" + category + ")" : category);
            if (withDescription && Description.Length > 0) lines.Add(Description);
            lines.AddRange(Effects);
            if (Effects.Count == 0) lines.Add(LangData.Text.Has("NO_EFFECTS") ? LangData.Text.Get("NO_EFFECTS") : "Aucun effet");
            if (Conditions.Length > 0) lines.Add((LangData.Text.Has("CONDITIONS") ? LangData.Text.Get("CONDITIONS") : "Conditions") + " : " + string.Join(", ", ConditionLines));
            string footer = WeightText;
            if (Price.HasValue) footer += (footer.Length > 0 ? " · " : string.Empty) + Price.Value.ToString("N0", CultureInfo.GetCultureInfo("fr-FR")) + " " + (LangData.Text.Has("KAMAS") ? LangData.Text.Get("KAMAS").ToLowerInvariant() : "kamas");
            if (TwoHanded) footer += (footer.Length > 0 ? " · " : string.Empty) + (LangData.Text.Has("TWO_HANDS_WEAPON") ? LangData.Text.Get("TWO_HANDS_WEAPON") : "Arme à deux mains");
            if (footer.Length > 0) lines.Add(footer);
            return lines;
        }
    }

    /// <summary>Panoplie portée (paquet <c>OS+</c>) présentée comme <c>ItemSetViewer</c> : nom, objets (portés ou non) et bonus actuels.</summary>
    public sealed class ItemSetSheet
    {
        public int Id { get; private set; }
        public string Name { get; private set; } = string.Empty;
        /// <summary>Modèles de la panoplie avec, pour chacun, « porté » selon la liste du serveur.</summary>
        public IReadOnlyList<KeyValuePair<int, bool>> Items { get; private set; } = new KeyValuePair<int, bool>[0];
        public IReadOnlyList<string> Effects { get; private set; } = new string[0];
        public int EquippedCount => Items.Count(item => item.Value);

        public static ItemSetSheet From(ItemSetState state)
        {
            if (state == null) return null;
            var sheet = new ItemSetSheet { Id = state.Id, Name = ItemSheet.NameOfSet(state.Id) };
            IReadOnlyList<int> models = ItemSheet.ItemsOfSet(state.Id);
            // Sans liste de modèles connue, seuls les objets portés annoncés par le serveur sont montrés.
            IEnumerable<int> all = models.Count > 0 ? models.Concat(state.ItemIds.Where(id => !models.Contains(id))) : state.ItemIds;
            sheet.Items = all.Select(id => new KeyValuePair<int, bool>(id, state.ItemIds.Contains(id))).ToArray();
            sheet.Effects = ItemEffects.DescribeAll(state.Bonus);
            return sheet;
        }
    }
}
