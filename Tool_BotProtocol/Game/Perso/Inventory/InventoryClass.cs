using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using Tool_BotProtocol.Game.Exchanges;
using Tool_BotProtocol.Game.Perso.Inventory.Enums;
using Tool_BotProtocol.Utils.Interfaces;

namespace Tool_BotProtocol.Game.Perso.Inventory
{
    /// <summary>Panoplie annoncée par le serveur (paquet OS du client 1.34).</summary>
    public sealed class ItemSetState
    {
        public int Id { get; internal set; }
        public int[] ItemIds { get; internal set; } = new int[0];
        public string Bonus { get; internal set; } = string.Empty;
    }

    public class InventoryClass : IDisposable, IEliminable
    {
        private Accounts.Accounts Account;
        static string ItemPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ressources", "Bot", "BotObjets");
        private ConcurrentDictionary<uint, InventoryObjects> PlayerItems;

        public int Kamas { get; set; }
        public int Actual_pods { get; set; }
        public int Pods_Max { get; set; }

        public IEnumerable<InventoryObjects> Objets => PlayerItems.Values;
        public IEnumerable<InventoryObjects> Equipement => Objets.Where(x => x.Inventory == Enums.InventoryObjectsTypes.EQUIPMENTS);
        public IEnumerable<InventoryObjects> Misc => Objets.Where(x => x.Inventory == Enums.InventoryObjectsTypes.MISCELLANEOUS);
        public IEnumerable<InventoryObjects> Ressources => Objets.Where(x => x.Inventory == Enums.InventoryObjectsTypes.RESOURCES);
        public IEnumerable<InventoryObjects> QuestItems => Objets.Where(x => x.Inventory == Enums.InventoryObjectsTypes.QUEST_ITEMS);
        public int Percent_Pods => Pods_Max > 0 ? (int)((double)Actual_pods / Pods_Max * 100) : 0;
        /// <summary>Panoplies portées, telles qu'annoncées par OS+ ; OS- retire l'entrée.</summary>
        public ConcurrentDictionary<int, ItemSetState> ItemSets { get; } = new ConcurrentDictionary<int, ItemSetState>();

        /// <summary>Dernier refus ou demande de confirmation du serveur (OAE…, OK…) à présenter dans l'interface.</summary>
        public string LastServerMessage { get; private set; } = string.Empty;

        public event Action<bool> RefreshInventory;
        public event Action Open_StockAction;
        public event Action Close_StockAction;

        /// <summary>Mémorise un message du serveur et prévient l'interface sans modifier les objets.</summary>
        public void NotifyRefused(string reason)
        {
            LastServerMessage = reason ?? string.Empty;
            RefreshInventory?.Invoke(false);
        }

        internal InventoryClass(Accounts.Accounts account)
        {
            Account = account;
            PlayerItems = new ConcurrentDictionary<uint, InventoryObjects>();

        }
        public InventoryObjects GetItemsById(int id) => Objets.FirstOrDefault( x => x.ID == id);
        public InventoryObjects GetByInventoryId(uint inventoryId) => PlayerItems != null && PlayerItems.TryGetValue(inventoryId, out InventoryObjects item) ? item : null;
        public InventoryObjects GetObjetsPosition(InventorySlots P) => Objets.FirstOrDefault(x => x.position == P);

        /// <summary>Objet porté sur la case du familier (8), celui que nourrit une ressource déposée dessus, ou null.</summary>
        public InventoryObjects EquippedPet => Objets.FirstOrDefault(x => x != null && (int)x.position == ItemSlots.PetPosition);
        /// <summary>Monture annoncée par le serveur (<c>Re+</c>) : la case 16 accepte alors une ressource (nourrir la monture).</summary>
        public bool HasMount => Account?.CanUseMount == true;

        /// <summary>
        /// Ajoute les objets d'un enregistrement « O » du paquet OAK : fiches séparées par « ; »,
        /// chacune au format client <c>id~modèle~quantité~position~effets</c> en hexadécimal.
        /// Une fiche déjà connue est remplacée (le serveur renvoie l'objet complet après une fusion de pile).
        /// </summary>
        public void Add_Items(string paquet)
        {
            int added = 0;
            foreach (string o in (paquet ?? string.Empty).Split(';'))
            {
                if (string.IsNullOrWhiteSpace(o)) continue;
                InventoryObjects item = InventoryObjects.Parse(o);
                if (item == null) { Account?.Logger?.LogError("INVENTAIRE", "Fiche d'objet illisible : " + o); continue; }
                PlayerItems[item.Inventory_ID] = item;
                added++;
            }
            if (added > 0) RefreshInventory?.Invoke(true);
        }

        /// <summary>Remplace une fiche d'objet (paquet OC) ; un objet inconnu est ajouté.</summary>
        public bool Update_Item(string record)
        {
            InventoryObjects item = InventoryObjects.Parse(record);
            if (item == null) return false;
            PlayerItems[item.Inventory_ID] = item;
            RefreshInventory?.Invoke(true);
            return true;
        }

        /// <summary>Déplace un objet vers l'emplacement annoncé par le serveur (paquet OM) ; vide = sac.</summary>
        public bool Move_Item(uint inventoryId, string position)
        {
            if (!PlayerItems.TryGetValue(inventoryId, out InventoryObjects item)) return false;
            item.position = int.TryParse(position, NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot)
                ? (InventorySlots)slot : InventorySlots.NOT_EQUIPPED;
            RefreshInventory?.Invoke(true);
            return true;
        }

        public void Modify_Items(string paquet)
        {
            if (!string.IsNullOrEmpty(paquet))
            {
                string[] part = paquet.Split('|');
                if (part.Length < 2 || !uint.TryParse(part[0], out uint id) || !int.TryParse(part[1], out int qua)) return;
                InventoryObjects U = Objets.FirstOrDefault(x => x.Inventory_ID == id);

                if(U != null)
                {
                    U.Qua = qua;
                    RefreshInventory?.Invoke(true);
                }
            }
        }
        /// <summary>
        /// Retire localement <paramref name="qua"/> exemplaires (0 = tout) après un paquet du serveur (<c>OR</c>, <c>OQ</c>).
        /// Aucun paquet n'est émis : la destruction volontaire passe par <see cref="Destroy"/>, qui attend la confirmation du serveur.
        /// </summary>
        public void Delete_Item(InventoryObjects I, int qua)
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

            RefreshInventory?.Invoke(true);
        }
        public void SuppItem(uint ID_inventory, int qua)
        {
            if (!PlayerItems.TryGetValue(ID_inventory, out InventoryObjects I))
                return;

            Delete_Item(I, qua);
        }
        /// <summary>
        /// Détruit des exemplaires d'un objet du sac : <c>Od&lt;id&gt;|&lt;quantité&gt;</c>, comme <c>Items.destroy</c> du client
        /// après la confirmation <c>DO_U_DESTROY</c> (à demander par l'interface). Le serveur répond par <c>OR</c> (tout détruit)
        /// ou <c>OQ</c> (quantité restante) ; <c>OdE</c> signale une erreur. StarLoco ignore en silence un objet équipé ou
        /// inconnu : ces cas sont refusés localement pour ne pas laisser l'utilisateur sans réponse.
        /// </summary>
        public async Task<bool> Destroy(InventoryObjects item, int quantity)
        {
            if (!CanAct(item, out string reason)) { Account?.Logger?.LogError("INVENTAIRE", reason); return false; }
            if (quantity <= 0 || quantity > item.Qua)
            {
                Account.Logger.LogError("INVENTAIRE", $"Quantité invalide pour détruire {item.Name} (1 à {item.Qua}).");
                return false;
            }
            if (item.position != InventorySlots.NOT_EQUIPPED && (int)item.position < ItemSlots.ShortcutOffset)
            {
                Account.Logger.LogError("INVENTAIRE", $"{item.Name} est équipé : le serveur ignore la destruction d'un objet porté, retirez-le d'abord.");
                return false;
            }
            await Account.Connexion.SendPacket($"Od{item.Inventory_ID}|{quantity}", true);
            Account.Logger.LogInfo("INVENTAIRE", $"Destruction de {quantity} × {item.Name} demandée ; le serveur confirme par OR ou OQ.");
            return true;
        }
        /// <summary>
        /// Déplace un objet vers un emplacement : <c>OM&lt;id&gt;|&lt;position&gt;</c>, la quantité n'étant ajoutée
        /// (<c>|&lt;quantité&gt;</c>) que lorsqu'elle est précisée, comme <c>Items.movement</c> du client. -1 range dans le sac,
        /// 0 à 16 équipe (16 = monture), 34 et plus pose dans la barre de raccourcis. Un emplacement d'équipement doit accepter
        /// le type de l'objet (<see cref="ItemSlots.Accepts(int, int, bool, bool)"/>) ; le serveur confirme par <c>OM</c> ou refuse
        /// par <c>OAE</c>/<c>Im119</c>. Une ressource posée sur le familier porté (8) ou sur la monture annoncée (16) le nourrit
        /// (lot X4) : <c>OM&lt;id&gt;|8</c>, <c>OM&lt;id&gt;|16|&lt;quantité&gt;</c> ; StarLoco répond <c>OQ</c>/<c>OR</c>, <c>OCO</c>,
        /// <c>Im032</c>… pour le familier, <c>Re+</c> et <c>Im0105</c> pour la monture, <c>Im153</c> ou <c>Im190</c> en cas de refus.
        /// Refusé sans envoi pendant un échange ouvert, comme <c>canMoveItem</c> du client (<see cref="MoveForbiddenByExchange"/>).
        /// </summary>
        public async Task<bool> MoveToSlot(InventoryObjects item, int position, int? quantity = null)
        {
            if (!CanAct(item, out string reason)) { Account?.Logger?.LogError("INVENTAIRE", reason); return false; }
            if (MoveForbiddenByExchange(out string exchange)) { Account.Logger.LogError("INVENTAIRE", exchange); return false; }
            if (position < -1 || (position > ItemSlots.MountPosition && position < ItemSlots.ShortcutOffset))
            {
                Account.Logger.LogError("INVENTAIRE", $"Emplacement {position} inconnu pour {item.Name}.");
                return false;
            }
            if ((int)item.position == position)
            {
                Account.Logger.LogError("INVENTAIRE", $"{item.Name} est déjà à l'emplacement {ItemSlots.Name(position)}.");
                return false;
            }
            if (position >= 0 && position <= ItemSlots.MountPosition
                && !ItemSlots.Accepts(position, item.Type, EquippedPet != null && EquippedPet != item, HasMount))
            {
                string why = !ItemSlots.IsFood(item.Type) ? $"{item.Name} ne peut pas être posé sur l'emplacement {ItemSlots.Name(position)}."
                    : position == ItemSlots.PetPosition ? $"Aucun familier porté à nourrir avec {item.Name}."
                    : position == ItemSlots.MountPosition ? $"Aucune monture équipée à nourrir avec {item.Name}."
                    : $"{item.Name} ne peut pas être posé sur l'emplacement {ItemSlots.Name(position)}.";
                Account.Logger.LogError("INVENTAIRE", why);
                return false;
            }
            if (quantity.HasValue && (quantity.Value <= 0 || quantity.Value > item.Qua))
            {
                Account.Logger.LogError("INVENTAIRE", $"Quantité invalide pour déplacer {item.Name} (1 à {item.Qua}).");
                return false;
            }
            string packet = $"OM{item.Inventory_ID}|{position}" + (quantity.HasValue ? $"|{quantity.Value}" : string.Empty);
            await Account.Connexion.SendPacket(packet, true);
            bool feeding = (position == ItemSlots.PetPosition || position == ItemSlots.MountPosition) && !ItemSlots.Accepts(position, item.Type);
            Account.Logger.LogInfo("INVENTAIRE", feeding
                ? $"{item.Name} donné à manger {(position == ItemSlots.PetPosition ? "au familier" : "à la monture")} ; le serveur répond par OQ ou OR et un message Im."
                : $"Déplacement de {item.Name} vers {ItemSlots.Name(position)} demandé ; le serveur confirme par OM.");
            return true;
        }
        private bool CanAct(InventoryObjects item, out string reason)
        {
            reason = null;
            if (item == null) { reason = "Aucun objet sélectionné."; return false; }
            if (Account?.Connexion == null || !Account.Connexion.IsConnected()) { reason = "Connectez le personnage avant d'agir sur l'inventaire."; return false; }
            if (Account.IsFighting() || Account.IsMoving()) { reason = "Action impossible pendant un combat ou un déplacement."; return false; }
            if (item.Qua <= 0) { reason = $"L'objet {item.Name} n'est plus disponible."; return false; }
            return true;
        }
        /// <summary>
        /// Comme <c>Inventory.canMoveItem</c> du client 1.34 : aucun déplacement d'objet (glisser, déposer, double-clic, jeter au sol :
        /// <c>OM</c>, <c>OD</c>) tant que <c>datacenter.Exchange</c> existe, c'est-à-dire pendant un échange ouvert par <c>ECK</c> 1, 2, 3,
        /// 9, 12, 13, 17 ou 18 (<c>Exchange.onCreate</c> et <c>onRequest</c>) ; le client affiche alors <c>CANT_MOVE_ITEM</c>. Boutique,
        /// banque, hôtel de vente, étable… n'en créent pas : le client y laisse déplacer. Utiliser (<c>OU</c>) et détruire (<c>Od</c>) ne
        /// passent pas par ce contrôle chez le client. Rien n'est envoyé : StarLoco ne vérifie aucun échange dans
        /// <c>movementObject</c>, et <c>NpcExchangePets.apply</c> lève une <c>NullPointerException</c> (session coupée) si le familier
        /// proposé a été équipé entre-temps (lot X4-CORR).
        /// </summary>
        public bool MoveForbiddenByExchange(out string reason)
        {
            reason = null;
            IExchange current = Account?.Game?.Interactions?.Exchanges?.Current;
            if (current == null || !current.IsOpen) return false;
            switch (current.ExchangeType)
            {
                case ExchangeTypes.Player:
                case ExchangeTypes.NpcExchange:
                case ExchangeTypes.Craft:
                case ExchangeTypes.Pets:
                case ExchangeTypes.SecureCraftClient:
                case ExchangeTypes.SecureCraftArtisan:
                case ExchangeTypes.PetsResurrection:
                case ExchangeTypes.NpcExchangeWindow:
                    reason = ExchangeRegistry.Text("CANT_MOVE_ITEM", "Vous ne pouvez pas déplacer d'objet pendant un échange.");
                    return true;
                default:
                    return false;
            }
        }
        /// <summary>
        /// Demande au serveur d'équiper l'objet : <c>OM&lt;id&gt;|&lt;emplacement&gt;</c>, comme <c>Items.movement</c> du client
        /// (sans quantité : le client ne l'ajoute que lorsqu'elle est fournie).
        /// La position locale n'est modifiée qu'à la réception de <c>OM</c> ; un refus arrive en <c>OAE…</c>.
        /// </summary>
        public async Task<bool> Equip_item(InventoryObjects I)
        {
            if (!CanAct(I, out string reason)) { Account?.Logger?.LogError("INVENTAIRE", reason); return false; }
            if (MoveForbiddenByExchange(out string exchange)) { Account.Logger.LogError("INVENTAIRE", exchange); return false; }
            if(I.Level > Account.Game.character.Level)
            {
                Account.Logger.LogError("INVENTAIRE", $"Le niveau de l'objet {I.Name} est supérieur à ton niveau");
                return false;
            }
            if(I.position != InventorySlots.NOT_EQUIPPED && (int)I.position < ItemSlots.ShortcutOffset)
            {
                Account.Logger.LogError("INVENTAIRE", $"L'objet {I.Name} est déjà sur toi");
                return false;
            }
            List<InventorySlots> Possible_P = ItemSlots.PositionsFor(I.Type).Where(p => p <= (int)InventorySlots.SHIELD).Select(p => (InventorySlots)p).ToList();
            if(Possible_P.Count == 0)
            {
                Account.Logger.LogError("INVENTAIRE", $"L'objet {I.Name} n'est pas un objet équipable");
                return false;
            }
            // Premier emplacement libre, sinon le premier emplacement possible : le serveur gère l'échange ou le refus.
            InventorySlots slot = Possible_P.FirstOrDefault(candidate => GetObjetsPosition(candidate) == null);
            if (!Possible_P.Contains(slot) || GetObjetsPosition(slot) != null) slot = Possible_P[0];
            await Account.Connexion.SendPacket($"OM{I.Inventory_ID}|{(int)slot}", true);
            Account.Logger.LogInfo("INVENTAIRE", $"Demande d'équipement de {I.Name} envoyée ; le serveur confirme par OM.");
            return true;
        }
        /// <summary>Demande au serveur de ranger l'objet dans le sac : <c>OM&lt;id&gt;|-1</c>.</summary>
        public async Task<bool> Desequip_Item(InventoryObjects O)
        {
            if (!CanAct(O, out string reason)) { Account?.Logger?.LogError("INVENTAIRE", reason); return false; }
            if (MoveForbiddenByExchange(out string exchange)) { Account.Logger.LogError("INVENTAIRE", exchange); return false; }
            if(O.position == InventorySlots.NOT_EQUIPPED || (int)O.position >= ItemSlots.ShortcutOffset)
            {
                Account.Logger.LogError("INVENTAIRE", $"{O.Name} n'est pas équipé");
                return false;
            }
            await Account.Connexion.SendPacket($"OM{O.Inventory_ID}|{(int)InventorySlots.NOT_EQUIPPED}", true);
            Account.Logger.LogInfo("INVENTAIRE", $"Demande de retrait de {O.Name} envoyée ; le serveur confirme par OM.");
            return true;
        }
        /// <summary>
        /// Utilise un objet comme <c>Items.use(id, sprite, cellule)</c> du client : <c>OU&lt;id&gt;|</c> sans cible,
        /// <c>OU&lt;id&gt;|&lt;sprite&gt;|&lt;cellule&gt;</c> sur un acteur et <c>OU&lt;id&gt;||&lt;cellule&gt;</c> sur une case sans
        /// sprite (lot X13, visée d'objet de la carte). StarLoco (<c>useObject</c>) n'applique l'effet à la cible que si c'est un
        /// joueur ; sinon il s'applique au lanceur. La quantité est mise à jour par OQ/OR.
        /// </summary>
        public async Task<bool> Use_Item(InventoryObjects G, long? target = null, int? cell = null)
        {
            if (!CanAct(G, out string reason)) { Account?.Logger?.LogError("INVENTAIRE", reason); return false; }
            if (cell.HasValue && cell.Value < 0)
            {
                Account.Logger.LogError("INVENTAIRE", $"Cellule {cell.Value} invalide pour utiliser {G.Name}.");
                return false;
            }
            await Account.Connexion.SendPacket(UsePacket(G.Inventory_ID, target, cell), true);
            Account.Logger.LogInfo("INVENTAIRE", target.HasValue || cell.HasValue
                ? $"Utilisation de {G.Name} sur {(target.HasValue ? "l'acteur " + target.Value.ToString(CultureInfo.InvariantCulture) : "la case")}"
                    + (cell.HasValue ? $" (cellule {cell.Value.ToString(CultureInfo.InvariantCulture)})" : string.Empty) + " demandée ; le serveur confirme."
                : $"Utilisation de {G.Name} demandée ; le serveur confirme.");
            return true;
        }

        /// <summary>Paquet <c>OU</c> exact du client 1.34 (voir <see cref="Use_Item"/>).</summary>
        public static string UsePacket(uint inventoryId, long? target, int? cell) =>
            "OU" + inventoryId.ToString(CultureInfo.InvariantCulture)
            + (target.HasValue ? "|" + target.Value.ToString(CultureInfo.InvariantCulture) : "|")
            + (cell.HasValue ? "|" + cell.Value.ToString(CultureInfo.InvariantCulture) : string.Empty);

        /// <summary>
        /// Nourrit un objet vivant associé : <c>Of&lt;objet&gt;|&lt;position&gt;|&lt;aliment&gt;</c> (<c>Items.feed</c>, fenêtre
        /// <c>ChooseFeed</c> après la confirmation <c>CONFIRM_FOOD_LIVING_ITEM</c>, à demander par l'interface). La position est
        /// celle de l'objet, -1 dans le sac. L'aliment doit passer le filtre du client (<see cref="LivingItem.FoodsFor"/>) : StarLoco
        /// (<c>feedObvi</c>) ne vérifie ni le type ni le propriétaire de l'aliment. Le serveur retire l'aliment (<c>OR</c> ou
        /// <c>OQ</c>) et renvoie l'objet (<c>OCO</c>) avec l'expérience augmentée du tiers du niveau de l'aliment.
        /// </summary>
        public async Task<ItemUseResult> FeedAsync(InventoryObjects item, InventoryObjects food)
        {
            if (!CanAct(item, out string reason) || (food != null && !CanAct(food, out reason))) return Refuse(reason);
            if (food != null && GetByInventoryId(food.Inventory_ID) == null) return Refuse($"{food.Name} n'est plus dans l'inventaire.");
            if (!LivingItem.IsFoodFor(item, food, out reason)) return Refuse(reason);
            int position = (int)item.position;
            await Account.Connexion.SendPacket("Of" + Invariant(item.Inventory_ID) + "|" + Invariant(position) + "|" + Invariant(food.Inventory_ID), true);
            string message = $"{item.Name} nourri avec {food.Name} ; le serveur retire l'aliment (OR/OQ) et renvoie l'objet (OCO).";
            Account.Logger.LogInfo("INVENTAIRE", message);
            return new ItemUseResult(true, message);
        }

        /// <summary>
        /// Change l'apparence d'un objet vivant : <c>Os&lt;objet&gt;|&lt;position&gt;|&lt;apparence&gt;</c>, l'apparence allant de 1
        /// au niveau de l'objet (<c>ChooseItemSkin</c> envoie l'indice choisi plus un). StarLoco (<c>setSkinObvi</c>) accepte 1 à 20
        /// et répond <c>OCO</c>, plus l'apparence portée aux joueurs de la carte si l'objet est équipé.
        /// </summary>
        public async Task<ItemUseResult> SetSkinAsync(InventoryObjects item, int skin)
        {
            if (!CanAct(item, out string reason)) return Refuse(reason);
            LivingItem living = LivingItem.From(item);
            if (living == null || !living.Skineable) return Refuse($"{item.Name} n'a pas d'apparence à choisir.");
            if (skin < 1 || skin > living.Level)
                return Refuse($"Apparence {skin} indisponible pour {item.Name} : 1 à {living.Level} au niveau {living.Level}.");
            await Account.Connexion.SendPacket("Os" + Invariant(item.Inventory_ID) + "|" + Invariant((int)item.position) + "|" + Invariant(skin), true);
            string message = $"Apparence {skin} demandée pour {item.Name} ; le serveur renvoie l'objet (OCO).";
            Account.Logger.LogInfo("INVENTAIRE", message);
            return new ItemUseResult(true, message);
        }

        /// <summary>
        /// Dissocie l'objet vivant de son objet : <c>Ox&lt;objet&gt;|&lt;position&gt;</c> (<c>Items.dissociate</c>, bouton
        /// <c>DISSOCIATE</c> du client, sans confirmation). StarLoco recrée l'objet vivant dans le sac (<c>OAKO</c>) et renvoie
        /// l'objet sans les effets 970 à 974 (<c>OCO</c>).
        /// </summary>
        public async Task<ItemUseResult> DissociateAsync(InventoryObjects item)
        {
            if (!CanAct(item, out string reason)) return Refuse(reason);
            LivingItem living = LivingItem.From(item);
            if (living == null || !living.IsAssociated) return Refuse($"{item.Name} ne porte pas d'objet vivant à dissocier.");
            await Account.Connexion.SendPacket("Ox" + Invariant(item.Inventory_ID) + "|" + Invariant((int)item.position), true);
            string message = $"Dissociation de {item.Name} demandée ; l'objet vivant revient dans le sac (OAKO) et l'objet est renvoyé (OCO).";
            Account.Logger.LogInfo("INVENTAIRE", message);
            return new ItemUseResult(true, message);
        }

        // Chiffres ASCII et signe « - » quelle que soit la culture (une culture peut écrire le moins en U+2212).
        private static string Invariant(long value) => value.ToString(CultureInfo.InvariantCulture);

        private ItemUseResult Refuse(string reason)
        {
            reason = string.IsNullOrEmpty(reason) ? "Action refusée." : reason;
            Account?.Logger?.LogError("INVENTAIRE", reason);
            return new ItemUseResult(false, reason);
        }
        /// <summary>
        /// Jette un objet au sol : <c>OD&lt;id&gt;|&lt;quantité&gt;</c> comme <c>Items.drop</c>. Le serveur répond par OR/OQ ou ODE.
        /// Refusé pendant un échange ouvert, comme <c>dropDownItem</c> du client (<see cref="MoveForbiddenByExchange"/>) ; StarLoco
        /// ignorerait d'ailleurs le paquet sans répondre (<c>dropObject</c>).
        /// </summary>
        public async Task<bool> Drop_Item(InventoryObjects item, int quantity)
        {
            if (!CanAct(item, out string reason)) { Account?.Logger?.LogError("INVENTAIRE", reason); return false; }
            if (MoveForbiddenByExchange(out string exchange)) { Account.Logger.LogError("INVENTAIRE", exchange); return false; }
            if (quantity <= 0 || quantity > item.Qua)
            {
                Account.Logger.LogError("INVENTAIRE", $"Quantité invalide pour jeter {item.Name} (1 à {item.Qua}).");
                return false;
            }
            await Account.Connexion.SendPacket($"OD{item.Inventory_ID}|{quantity}", true);
            Account.Logger.LogInfo("INVENTAIRE", $"Demande de jeter {quantity} × {item.Name} envoyée ; le serveur confirme.");
            return true;
        }
        /// <summary>Panoplie annoncée par OS : <c>+id|objets;…|bonus</c> ajoute ou remplace, <c>-id</c> retire.</summary>
        public bool Apply_ItemSet(string payload)
        {
            if (string.IsNullOrEmpty(payload)) return false;
            bool added = payload[0] == '+';
            if (!added && payload[0] != '-') return false;
            string[] parts = payload.Substring(1).Split('|');
            if (!int.TryParse(parts[0], out int setId)) return false;
            if (added)
            {
                var set = new ItemSetState { Id = setId, Bonus = parts.Length > 2 ? parts[2] : string.Empty };
                if (parts.Length > 1)
                    set.ItemIds = parts[1].Split(';').Select(value => int.TryParse(value, out int id) ? id : -1).Where(id => id >= 0).ToArray();
                ItemSets[setId] = set;
            }
            else ItemSets.TryRemove(setId, out ItemSetState ignored);
            RefreshInventory?.Invoke(true);
            return true;
        }
        public void CanOpen_stock() => Open_StockAction?.Invoke();
        public void CanCloseStock() => Close_StockAction?.Invoke();
        private bool isDisposed;
        public void Dispose() { Dispose(true); GC.SuppressFinalize(this); }
        public void Clear()
        {
            Kamas = 0;
            Actual_pods = 0;
            Pods_Max = 0;
            PlayerItems.Clear();
            ItemSets.Clear();
        }
        public virtual void Dispose(bool disposed)
        {
            if (!isDisposed)
            {
                PlayerItems.Clear();
                ItemSets.Clear();
                PlayerItems = null;
                Account = null;
                isDisposed = true;
            }
        }
        public static Task LoadAllObjectsAsync()
        {
            return Task.Run(() =>
            {
                var loaded = new ConcurrentDictionary<int, InventoryObjects>();
                foreach (string file in Directory.EnumerateFiles(ItemPath, "*.xml"))
                {
                    XElement xml = XElement.Load(file);
                    var item = new InventoryObjects { ID = int.Parse(xml.Element("ID").Value),
                        Type = byte.Parse(xml.Element("TYPE").Value), Name = xml.Element("NOM").Value,
                        pods = short.Parse(xml.Element("PODS").Value), Level = short.Parse(xml.Element("NIVEAU").Value),
                        Stats = xml.Element("STATS").Value, Conditions = xml.Element("CONDITIONS").Value };
                    loaded[item.ID] = item;
                }
                InventoryObjects.FullInventory = loaded;
            });
        }

    }
}
