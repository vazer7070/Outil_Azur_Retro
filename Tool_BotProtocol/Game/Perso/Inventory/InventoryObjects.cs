using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        public short Regen { get; }
        public string Stats { get; set; }
        public string Conditions { get; set; }
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
        public InventoryObjects(string paquet)
        {
            string[] parse = paquet.Split('~');
            Inventory_ID = Convert.ToUInt32(parse[0], 16);
            ID = Convert.ToInt32(parse[1], 16);
            Qua = Convert.ToInt32(parse[2], 16);

            if(!string.IsNullOrEmpty(parse[3]))
                position = (InventorySlots)Convert.ToSByte(parse[3], 16);
            string [] S = parse[4].Split(',');
            foreach(string stats in S)
            {
                string[] Parse_stats = stats.Split('#');
                string id = Parse_stats[0];

                if(string.IsNullOrEmpty(id))
                    continue;
                int stats_id = Convert.ToInt32(id, 16);
                if(stats_id == 110)
                    Regen = Convert.ToInt16(Parse_stats[1], 16);
            }
            if (InventoryObjects.FullInventory.ContainsKey((int)ID))
            {
                Name = ReturnInventory(ID).Name;
                pods = ReturnInventory(ID).pods;
                Type = ReturnInventory(ID).Type;
                Level = ReturnInventory(ID).Level;
                Inventory = InventoryUtilities.GetTypeForObjectInInventory(Type);
            }
        }
        public bool IsEquipped() => position > InventorySlots.NOT_EQUIPPED;
    }
}
