using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using Tool_BotProtocol.Game.Perso.Inventory.Enums;
using Tool_BotProtocol.Utils.Interfaces;

namespace Tool_BotProtocol.Game.Perso.Inventory
{
    public class InventoryClass : IDisposable, IEliminable
    {
        private Accounts.Accounts Account;
        static string ItemPath = @".\ressources\Bot\BotObjets";
        private ConcurrentDictionary<uint, InventoryObjects> PlayerItems;

        public int Kamas { get; set; }
        public short Actual_pods { get; set; }
        public short Pods_Max { get; set; }

        public IEnumerable<InventoryObjects> Objets => PlayerItems.Values;
        public IEnumerable<InventoryObjects> Equipement => Objets.Where(x => x.Inventory == Enums.InventoryObjectsTypes.EQUIPMENTS);
        public IEnumerable<InventoryObjects> Misc => Objets.Where(x => x.Inventory == Enums.InventoryObjectsTypes.MISCELLANEOUS);
        public IEnumerable<InventoryObjects> Ressources => Objets.Where(x => x.Inventory == Enums.InventoryObjectsTypes.RESOURCES);
        public IEnumerable<InventoryObjects> QuestItems => Objets.Where(x => x.Inventory == Enums.InventoryObjectsTypes.QUEST_ITEMS);
        public int Percent_Pods => (int)((double)Actual_pods / Pods_Max * 100);

        public event Action<bool> RefreshInventory;
        public event Action Open_StockAction;
        public event Action Close_StockAction;

        internal InventoryClass(Accounts.Accounts account)
        {
            Account = account;
            PlayerItems = new ConcurrentDictionary<uint, InventoryObjects>();

        }
        public InventoryObjects GetItemsById(int id) => Objets.FirstOrDefault( x => x.ID == id);
        public InventoryObjects GetObjetsPosition(InventorySlots P) => Objets.FirstOrDefault(x => x.position == P);

        public void Add_Items(string paquet)
        {
            Task.Run(() =>
            {
                foreach(string o in paquet.Split(';'))
                {
                    if (!string.IsNullOrEmpty(o))
                    {
                        string[] parts = o.Split('~');
                        uint id = Convert.ToUInt32(parts[0], 16);
                        InventoryObjects I = new InventoryObjects(o);
                        PlayerItems.TryAdd(id, I);
                    }
                }
            }).Wait();

            RefreshInventory?.Invoke(true);
        }

        public void Modify_Items(string paquet)
        {
            if (!string.IsNullOrEmpty(paquet))
            {
                string[] part = paquet.Split('|');
                InventoryObjects U = Objets.FirstOrDefault(x => x.Inventory_ID == uint.Parse(part[0]));

                if(U != null)
                {
                    int qua = int.Parse(part[1]);
                    InventoryObjects NewItems = U;
                    NewItems.Qua = qua;

                    if(PlayerItems.TryUpdate(U.Inventory_ID, NewItems, U))
                        RefreshInventory?.Invoke(true);
                            
                }
            }
        }
        public async void Delete_Item(InventoryObjects I, int qua, bool DeleteMessage)
        {
            if (I == null)
                return;

            qua = qua == 0 ? I.Qua : qua > I.Qua ? I.Qua : qua;

            if(I.Qua > qua)
            {
                InventoryObjects NewItem = I;
                NewItem.Qua -= qua;
                PlayerItems.TryUpdate(I.Inventory_ID,NewItem, I);
            }
            else
            {
                PlayerItems.TryRemove(I.Inventory_ID, out InventoryObjects k);
            }
            if (DeleteMessage)
            {
                await Account.Connexion.SendPacket($"Od{I.Inventory_ID}|{qua}");
                Account.Logger.LogInfo("INVENTAIRE", $"{qua} {I.Name} retiré(s)");
            }

            RefreshInventory?.Invoke(true);
        }
        public void SuppItem(uint ID_inventory, int qua, bool DeleteMessage)
        {
            if (!PlayerItems.TryGetValue(ID_inventory, out InventoryObjects I))
                return;

            Delete_Item(I, qua, DeleteMessage);
        }
        public async Task<bool> Equip_item(InventoryObjects I)
        {
            if(I == null || I.Qua == 0 || Account.Isbusy())
            {
                Account.Logger.LogError("INVENTAIRE", $"L'objet {I.Name} ne peux être équipé");
                return false;
            }
            if(I.Level > Account.Game.character.Level)
            {
                Account.Logger.LogError("INVENTAIRE", $"Le niveau de l'objet {I.Name} est supérieur à ton niveau");
                return false;
            }
            if(I.position != InventorySlots.NOT_EQUIPPED)
            {
                Account.Logger.LogError("INVENTAIRE", $"L'objet {I.Name} est déjà sur toi");
                return false;
            }
            List<InventorySlots> Possible_P = InventoryUtilities.GetPosition(I.Type);
            if(Possible_P == null || Possible_P.Count == 0)
            {
                Account.Logger.LogError("INVENTAIRE", $"L'objet {I.Name} n'est pas un objet équipable");
                return false;
            }
            foreach(InventorySlots I_S in Possible_P)
            {
                if(GetObjetsPosition(I_S) == null)
                {
                    await Account.Connexion.SendPacket($"OM{I.Inventory_ID}|{I_S}", true);
                    Account.Logger.LogInfo("INVENTAIRE", $"L'objet {I.Name} est équipé");
                    I.position = I_S;
                    RefreshInventory?.Invoke(true);
                    return true;
                }
            }
            if(PlayerItems.TryGetValue(GetObjetsPosition(Possible_P[0]).Inventory_ID, out InventoryObjects H))
            {
                H.position = InventorySlots.NOT_EQUIPPED;
                await Account.Connexion.SendPacket($"OM{H.Inventory_ID}|{InventorySlots.NOT_EQUIPPED}");
            }
            await Account.Connexion.SendPacket($"OM{H.Inventory_ID}|{Possible_P[0]}");

            if(I.Qua == 1)
                I.position = Possible_P[0];

            Account.Logger.LogInfo("INVENTAIRE", $"L'objet {I.Name} est équipé");
            RefreshInventory?.Invoke(true);
            return true;
        }
        public async Task<bool> Desequip_Item(InventoryObjects O)
        {
            if (O == null)
                return false;
            if(O.position == InventorySlots.NOT_EQUIPPED)
                return false;
            await Account.Connexion.SendPacket($"OM{O.Inventory_ID}|{InventorySlots.NOT_EQUIPPED}");
            O.position = InventorySlots.NOT_EQUIPPED;
            Account.Logger.LogInfo("INVENTAIRE", $"{O.Name} déséquipé");
            RefreshInventory.Invoke(true);
            return true;
        }
        public async void Use_Item(InventoryObjects G)
        {
            if (G == null)
                return;
            if (G.Qua == 0)
                return;
            await Account.Connexion.SendPacket($"OU{G.Inventory_ID}|");
            Delete_Item(G, 1, false);
            Account.Logger.LogInfo("INVENTAIRE", $"Objet {G.Name} utilisé");

        }
        public void CanOpen_stock() => Open_StockAction?.Invoke();
        public void CanCloseStock() => Close_StockAction?.Invoke();
        public void Dispose() => Dispose(true);
        ~InventoryClass() => Dispose(true);
        public void Clear()
        {
            Kamas = 0;
            Actual_pods = 0;
            Pods_Max = 0;
            PlayerItems.Clear();
        }
        public virtual void Dispose(bool disposed)
        {
            if (!disposed)
            {
                PlayerItems.Clear();
                PlayerItems = null;
                Account = null;
                disposed = true;
            }
        }
        public static async Task LoadAllObjectsAsync()
        {
            try
            {
                DirectoryInfo itemsFolder = new DirectoryInfo(ItemPath);
                FileInfo[] files = itemsFolder.GetFiles();

                ConcurrentDictionary<int, InventoryObjects> inventoryObjects = new ConcurrentDictionary<int, InventoryObjects>();
                List<Task> tasks = new List<Task>();

                foreach (FileInfo file in files)
                {
                    if (file.Exists)
                    {
                        tasks.Add(Task.Run(async () =>
                        {
                            XElement xmlmap = await Task.Run(() => XElement.Load(file.FullName));

                            InventoryObjects L = new InventoryObjects()
                            {
                                ID = int.Parse(xmlmap.Element("ID").Value),
                                Type = byte.Parse(xmlmap.Element("TYPE").Value),
                                Name = xmlmap.Element("NOM").Value,
                                pods = short.Parse(xmlmap.Element("PODS").Value),
                                Level = short.Parse(xmlmap.Element("NIVEAU").Value),
                                Stats = xmlmap.Element("STATS").Value,
                                Conditions = xmlmap.Element("CONDITIONS").Value
                            };

                            inventoryObjects.TryAdd(L.ID, L);
                        }));
                    }
                }

                await Task.WhenAll(tasks);
                InventoryObjects.FullInventory = inventoryObjects;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                return;
            }
        }

    }
}
