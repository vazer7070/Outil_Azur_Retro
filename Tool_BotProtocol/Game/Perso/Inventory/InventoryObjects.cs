using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game.Perso.Inventory.Enums;

namespace Tool_BotProtocol.Game.Perso.Inventory
{
    public class InventoryObjects
    {
        public uint Inventory_ID { get;  set; }
        public int ID { get; set; }
        public string Name { get; set; } = "Inconnu";
        public int Qua { get; set; }

        public InventorySlots position { get; set; } = InventorySlots.NOT_EQUIPPED;
        public short pods { get; set; }
        public short Level { get; set; } = 0;
        public byte Type { get;  set; }
        public short Regen { get; private set; }
        public string Stats { get; set; }
        public string Conditions { get; set; }
        /// <summary>Vrai lorsque la fiche du modèle est disponible dans <c>ressources/Bot/BotObjets</c>.</summary>
        public bool HasMetadata { get; private set; }
        public static ConcurrentDictionary<int, InventoryObjects> FullInventory = new ConcurrentDictionary<int, InventoryObjects>();
        public InventoryObjectsTypes Inventory { get; private set; } = InventoryObjectsTypes.UNKNOWN;
        public InventoryObjects()
        {

        }
        public static InventoryObjects ReturnInventory(int id)
        {
           if(InventoryObjects.FullInventory.ContainsKey(id))
                return InventoryObjects.FullInventory[id];
            return null;
        }
        /// <summary>Nom à afficher pour un modèle d'objet : la fiche BotObjets si elle existe, sinon « Objet n° X ».</summary>
        public static string DisplayName(int templateId)
        {
            InventoryObjects template = ReturnInventory(templateId);
            return template != null && !string.IsNullOrEmpty(template.Name) ? template.Name : "Objet n° " + templateId;
        }
        /// <summary>
        /// Lit une fiche d'objet au format du client 1.34 (<c>CharactersManager.getItemObjectFromData</c>) :
        /// <c>id~modèle~quantité~position~effets</c>, les quatre premiers champs en hexadécimal, la position vide valant -1.
        /// Retourne null si la fiche est illisible au lieu de lever une exception.
        /// </summary>
        public static InventoryObjects Parse(string record)
        {
            if (string.IsNullOrWhiteSpace(record)) return null;
            string[] parse = record.Split('~');
            if (parse.Length < 4) return null;
            if (!uint.TryParse(parse[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint inventoryId)
                || !int.TryParse(parse[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int template)
                || !int.TryParse(parse[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int quantity)) return null;
            var item = new InventoryObjects { Inventory_ID = inventoryId, ID = template, Qua = quantity };
            if (!string.IsNullOrEmpty(parse[3]))
            {
                if (!int.TryParse(parse[3], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int slot)) return null;
                item.position = (InventorySlots)slot;
            }
            item.Stats = parse.Length > 4 ? parse[4] : string.Empty;
            foreach (string stats in item.Stats.Split(','))
            {
                string[] Parse_stats = stats.Split('#');
                if (string.IsNullOrEmpty(Parse_stats[0])) continue;
                if (!int.TryParse(Parse_stats[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int statsId)) continue;
                if (statsId == 110 && Parse_stats.Length > 1
                    && short.TryParse(Parse_stats[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out short regen))
                    item.Regen = regen;
            }
            InventoryObjects metadata = ReturnInventory(template);
            if (metadata != null)
            {
                item.Name = metadata.Name;
                item.pods = metadata.pods;
                item.Type = metadata.Type;
                item.Level = metadata.Level;
                item.Conditions = metadata.Conditions;
                item.Inventory = InventoryUtilities.GetTypeForObjectInInventory(metadata.Type);
                item.HasMetadata = true;
            }
            else item.Name = "Objet n° " + template;
            return item;
        }
        public InventoryObjects(string paquet)
        {
            InventoryObjects parsed = Parse(paquet);
            if (parsed == null) throw new FormatException("Fiche d'objet illisible : " + paquet);
            Inventory_ID = parsed.Inventory_ID; ID = parsed.ID; Qua = parsed.Qua; position = parsed.position;
            Stats = parsed.Stats; Regen = parsed.Regen; Name = parsed.Name; pods = parsed.pods; Type = parsed.Type;
            Level = parsed.Level; Conditions = parsed.Conditions; Inventory = parsed.Inventory; HasMetadata = parsed.HasMetadata;
        }
        public bool IsEquipped() => position > InventorySlots.NOT_EQUIPPED;
    }
}
