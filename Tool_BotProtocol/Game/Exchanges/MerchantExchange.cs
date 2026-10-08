using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Habitat;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Maps.Interfaces;
using Tool_BotProtocol.Game.Perso.Inventory;
using Tool_BotProtocol.Game.Session;

namespace Tool_BotProtocol.Game.Exchanges
{
    /// <summary>Lot d'un magasin de joueur, lu dans <c>EL&lt;exemplaire&gt;;&lt;quantité&gt;;&lt;modèle&gt;;&lt;effets&gt;;&lt;prix&gt;|…</c>.</summary>
    public sealed class MerchantItem
    {
        /// <summary>Identifiant de l'exemplaire dans le magasin (celui que <c>EB</c> et <c>EMO-</c> désignent).</summary>
        public uint Id { get; internal set; }
        public int Quantity { get; internal set; }
        public int TemplateId { get; internal set; }
        public string Effects { get; internal set; } = string.Empty;
        /// <summary>Prix unitaire en kamas.</summary>
        public long Price { get; internal set; }
        public string Name { get; internal set; } = string.Empty;

        internal MerchantItem Copy() => (MerchantItem)MemberwiseClone();
    }

    /// <summary>
    /// Magasin d'un marchand hors ligne (type 4, interface <c>PlayerShop</c> du client 1.34) et organisation de son propre
    /// magasin (type 6, <c>PlayerShopModifier</c>). Ouverture par <c>ER4|&lt;marchand&gt;|&lt;cellule&gt;</c> (menu du marchand) ou <c>ER6|</c>
    /// (menu de soi-même, <c>ORGANIZE_SHOP</c>) ; StarLoco répond <c>ECK4|&lt;marchand&gt;</c> ou <c>ECK6</c> puis la liste
    /// <c>EL&lt;exemplaire&gt;;&lt;quantité&gt;;&lt;modèle&gt;;&lt;effets&gt;;&lt;prix&gt;|…</c>, renvoyée en entier après chaque mouvement.
    /// Acheter envoie <c>EB&lt;exemplaire&gt;|&lt;quantité&gt;</c> (<c>EBK</c> ou <c>EBE</c>, objets et kamas par <c>OAK</c>/<c>OQ</c>/<c>As</c>) ;
    /// mettre en vente <c>EMO+&lt;objet&gt;|&lt;quantité&gt;|&lt;prix&gt;</c>, retirer <c>EMO-&lt;exemplaire&gt;|&lt;quantité&gt;</c> ; fermer <c>EV</c>.
    /// Le mode marchand (<c>MERCHANT_MODE</c>) envoie <c>Eq</c> ; StarLoco répond <c>Eq1|1|&lt;taxe&gt;</c> ou un <c>Im</c> (123 magasin vide,
    /// 113 carte interdite, 125;&lt;n&gt; trop de marchands) ; <c>EQ</c> paie la taxe (<c>Im176</c> si les kamas manquent) et déconnecte le client.
    /// <c>askOfflineExchange</c> ignore <c>Eq</c> pendant un échange (<c>getExchangeAction() != null</c>) : comme le client, qui ne propose
    /// <c>MERCHANT_MODE</c> que depuis le menu du personnage, le bot refuse <c>Eq</c> tant qu'une fenêtre est ouverte. Les refus <c>Im</c>
    /// (lot S1, <see cref="GameSession.ServerMessageReceived"/>) libèrent la demande en attente.
    /// </summary>
    [ExchangeType(ExchangeTypes.OfflineMerchant)]
    [ExchangeType(ExchangeTypes.MyShop)]
    public sealed class MerchantExchange : ExchangeWindow
    {
        /// <summary>Quantité maximale acceptée par StarLoco dans <c>buy</c>.</summary>
        public const int MaxQuantity = 100000;
        /// <summary>Bit de <c>AR</c> qui interdit le mode marchand (<c>canBeMerchant</c> du client).</summary>
        public const int CannotBeMerchantBit = 32;
        private readonly object sync = new object();
        private List<MerchantItem> items = new List<MerchantItem>();
        private AccountStates openState = AccountStates.BUYING;
        /// <summary>Dernier contenu connu de son magasin (<c>EL</c> de type 6) : vide ou non ; <c>null</c> tant qu'il n'a pas été ouvert.</summary>
        private bool? shopKnownEmpty;
        private bool sessionSubscribed;

        internal MerchantExchange(Accounts.Accounts account) : base(account) { }

        protected override string Reference => "MARCHAND";
        /// <summary><c>BUYING</c> devant le magasin d'un marchand, <c>SELLING</c> en organisant le sien.</summary>
        protected override AccountStates OpenState => openState;

        /// <summary>Magasin d'un autre joueur ouvert (<c>ECK4</c>).</summary>
        public bool IsBuying => IsOpen && ExchangeType == ExchangeTypes.OfflineMerchant;
        /// <summary>Son propre magasin ouvert (<c>ECK6</c>).</summary>
        public bool IsOrganizing => IsOpen && ExchangeType == ExchangeTypes.MyShop;
        /// <summary>Identifiant du marchand (type 4) ; -1 sinon.</summary>
        public long MerchantId { get; private set; } = -1;
        public string MerchantName { get; private set; } = string.Empty;
        /// <summary>Lots du magasin (copies), dans l'ordre de <c>EL</c>.</summary>
        public IReadOnlyList<MerchantItem> Items { get { lock (sync) return items.Select(item => item.Copy()).ToArray(); } }
        /// <summary>Vrai dès que la liste <c>EL</c> a été reçue.</summary>
        public bool ContentReceived { get; private set; }
        /// <summary>Vrai tant qu'un achat attend <c>EBK</c>/<c>EBE</c>.</summary>
        public bool IsPending { get; private set; }
        /// <summary>Somme des prix du magasin (prix × quantité), base de la taxe du mode marchand.</summary>
        public long TotalPrice { get { lock (sync) return items.Sum(item => item.Price * item.Quantity); } }
        /// <summary>Taxe annoncée par <c>Eq1</c> et pas encore acceptée ni refusée ; <c>null</c> sinon.</summary>
        public MerchantTax PendingTax { get; private set; }
        /// <summary>Vrai entre l'envoi de <c>Eq</c> et la réponse du serveur.</summary>
        public bool TaxRequested { get; private set; }
        /// <summary>Vrai après l'envoi de <c>EQ</c> : StarLoco déconnecte alors le client.</summary>
        public bool MerchantModeRequested { get; private set; }

        /// <summary><c>Player.canBeMerchant</c> : bit 32 des restrictions <c>AR</c> absent.</summary>
        public bool CanBeMerchant
        {
            get
            {
                int raw = Account?.Game?.Session?.RawRestrictions ?? -1;
                return raw < 0 || (raw & CannotBeMerchantBit) == 0;
            }
        }

        // ---- Envois du client -----------------------------------------------------------------------------

        /// <summary>Envoie <c>ER4|&lt;marchand&gt;|&lt;cellule&gt;</c> (menu « Acheter » d'un marchand de la carte).</summary>
        public Task<InteractionResult> OpenAsync(long merchantId, short cellId)
        {
            InteractionResult refused = CheckCanOpen();
            if (refused != null) return Task.FromResult(refused);
            var merchant = Account?.Game?.Map?.GetActor(merchantId) as MerchantActor;
            if (merchant == null) return Task.FromResult(Refuse("Ce marchand n'est pas sur la carte."));
            return SendAsync("ER" + ExchangeTypes.OfflineMerchant + "|" + merchantId.ToString(CultureInfo.InvariantCulture) + "|" + cellId.ToString(CultureInfo.InvariantCulture),
                "Ouverture du magasin de " + merchant.DisplayName + " demandée.");
        }

        /// <summary>Envoie <c>ER6|</c> (<c>ORGANIZE_SHOP</c> du menu de soi-même) : sans le « | », <c>request</c> de StarLoco lit
        /// <c>substring(2, 4)</c> hors de la chaîne et le serveur expulse le client.</summary>
        public Task<InteractionResult> OrganizeAsync()
        {
            InteractionResult refused = CheckCanOpen();
            if (refused != null) return Task.FromResult(refused);
            if (!CanBeMerchant) return Task.FromResult(Refuse("Le mode marchand est interdit à ce personnage (restriction du serveur)."));
            return SendAsync("ER" + ExchangeTypes.MyShop + "|", "Organisation du magasin demandée.");
        }

        /// <summary>Envoie <c>EB&lt;exemplaire&gt;|&lt;quantité&gt;</c> pour un lot du marchand.</summary>
        public Task<InteractionResult> BuyAsync(uint itemId, int quantity)
        {
            if (!IsBuying) return Task.FromResult(Refuse("Aucun magasin de marchand ouvert."));
            if (IsPending) return Task.FromResult(Refuse("Attendez la réponse du serveur à l'achat précédent."));
            MerchantItem item = Find(itemId);
            if (item == null) return Task.FromResult(Refuse("Ce lot n'est pas dans le magasin."));
            if (quantity <= 0 || quantity > item.Quantity || quantity > MaxQuantity) return Task.FromResult(Refuse("Quantité invalide (1 à " + item.Quantity + ")."));
            long kamas = Account?.Game?.character?.Kamas ?? 0;
            long total = item.Price * quantity;
            if (total > kamas) return Task.FromResult(Refuse(ExchangeRegistry.Text("NOT_ENOUGH_RICH", "Tu n'as pas assez de kamas pour réaliser cette action.")
                + " (" + total.ToString(CultureInfo.InvariantCulture) + " demandés, " + kamas.ToString(CultureInfo.InvariantCulture) + " disponibles)"));
            IsPending = true;
            Notify();
            return SendAsync("EB" + itemId.ToString(CultureInfo.InvariantCulture) + "|" + quantity.ToString(CultureInfo.InvariantCulture),
                "Achat de " + quantity + " × " + item.Name + " pour " + total + " kamas demandé ; le serveur confirme.");
        }

        /// <summary>Envoie <c>EMO+&lt;objet&gt;|&lt;quantité&gt;|&lt;prix&gt;</c> pour un objet non équipé du sac (prix unitaire).</summary>
        public Task<InteractionResult> AddToShopAsync(uint inventoryId, int quantity, long price)
        {
            if (!IsOrganizing) return Task.FromResult(Refuse("Votre magasin n'est pas ouvert."));
            InventoryObjects item = Account?.Game?.character?.Inventory?.GetByInventoryId(inventoryId);
            if (item == null) return Task.FromResult(Refuse("Cet objet n'est pas dans votre inventaire."));
            if (item.IsEquipped()) return Task.FromResult(Refuse("Déséquipez cet objet avant de le mettre en vente."));
            if (quantity <= 0 || quantity > item.Qua) return Task.FromResult(Refuse("Quantité invalide (1 à " + item.Qua + ")."));
            if (price <= 0 || price > int.MaxValue) return Task.FromResult(Refuse("Prix invalide (StarLoco ignore un prix nul)."));
            return SendAsync("EMO+" + inventoryId.ToString(CultureInfo.InvariantCulture) + "|" + quantity.ToString(CultureInfo.InvariantCulture) + "|" + price.ToString(CultureInfo.InvariantCulture),
                quantity + " × " + item.Name + " mis en vente à " + price + " kamas ; le serveur renvoie la liste.");
        }

        /// <summary>
        /// Nouveau prix d'un lot du magasin : envoie <c>EMO+&lt;exemplaire&gt;|&lt;quantité du lot&gt;|&lt;prix&gt;</c>. Le client envoie la quantité 0
        /// (<c>PlayerShopModifier</c>), que StarLoco rejette ; avec la quantité du lot, <c>addInStore</c> ne change que le prix.
        /// </summary>
        public Task<InteractionResult> ChangePriceAsync(uint itemId, long price)
        {
            if (!IsOrganizing) return Task.FromResult(Refuse("Votre magasin n'est pas ouvert."));
            MerchantItem item = Find(itemId);
            if (item == null) return Task.FromResult(Refuse("Ce lot n'est pas dans le magasin."));
            if (price <= 0 || price > int.MaxValue) return Task.FromResult(Refuse("Prix invalide (StarLoco ignore un prix nul)."));
            return SendAsync("EMO+" + itemId.ToString(CultureInfo.InvariantCulture) + "|" + Math.Max(1, item.Quantity).ToString(CultureInfo.InvariantCulture) + "|" + price.ToString(CultureInfo.InvariantCulture),
                "Prix de " + item.Name + " porté à " + price + " kamas ; le serveur renvoie la liste.");
        }

        /// <summary>Envoie <c>EMO-&lt;exemplaire&gt;|&lt;quantité&gt;</c> ; StarLoco retire le lot entier quelle que soit la quantité.</summary>
        public Task<InteractionResult> RemoveFromShopAsync(uint itemId, int quantity)
        {
            if (!IsOrganizing) return Task.FromResult(Refuse("Votre magasin n'est pas ouvert."));
            MerchantItem item = Find(itemId);
            if (item == null) return Task.FromResult(Refuse("Ce lot n'est pas dans le magasin."));
            if (quantity <= 0 || quantity > item.Quantity) return Task.FromResult(Refuse("Quantité invalide (1 à " + item.Quantity + ")."));
            return SendAsync("EMO-" + itemId.ToString(CultureInfo.InvariantCulture) + "|" + quantity.ToString(CultureInfo.InvariantCulture),
                "Retrait de " + item.Name + " du magasin demandé (StarLoco retire le lot entier).");
        }

        /// <summary>Envoie <c>EV</c> ; la fenêtre se ferme au <c>EV</c> du serveur.</summary>
        public Task<InteractionResult> LeaveAsync()
        {
            if (!IsOpen) return Task.FromResult(Refuse("Aucun magasin ouvert."));
            return SendAsync("EV", "Fermeture du magasin demandée.");
        }

        /// <summary>
        /// Envoie <c>Eq</c> (<c>MERCHANT_MODE</c>) hors de toute fenêtre : le serveur annonce la taxe par <c>Eq1</c>, ou refuse par <c>Im123</c>
        /// (magasin vide), <c>Im113</c> (carte interdite) ou <c>Im125;&lt;n&gt;</c> (trop de marchands). StarLoco ignore <c>Eq</c> pendant un échange.
        /// </summary>
        public Task<InteractionResult> AskMerchantModeAsync()
        {
            EnsureSessionSubscribed();
            if (Account?.Connexion == null || !Account.Connexion.IsConnected()) return Task.FromResult(Refuse("Connectez le personnage avant cette action."));
            if (Account.IsFighting()) return Task.FromResult(Refuse("Action impossible pendant un combat."));
            if (IsOpen || Account.Is_In_Dialog()) return Task.FromResult(Refuse("Fermez d'abord la fenêtre en cours (EV) : StarLoco ignore Eq pendant un échange ou un dialogue, et le client ne propose le mode marchand que depuis le menu du personnage."));
            if (!CanBeMerchant) return Task.FromResult(Refuse("Le mode marchand est interdit à ce personnage (restriction du serveur)."));
            if (TaxRequested || PendingTax != null) return Task.FromResult(Refuse("La taxe du mode marchand a déjà été demandée."));
            if (shopKnownEmpty == true) return Task.FromResult(Refuse("Mettez au moins un objet en vente (Organiser mon magasin) avant de passer en mode marchand (Im123)."));
            TaxRequested = true;
            Notify();
            return SendAsync("Eq", "Taxe du mode marchand demandée ; le serveur répond par Eq1 ou un refus Im.");
        }

        /// <summary>Envoie <c>EQ</c> après l'accord sur la taxe (<c>DO_U_OFFLINEEXCHANGE</c>) ; StarLoco déconnecte ensuite le client.</summary>
        public Task<InteractionResult> ConfirmMerchantModeAsync()
        {
            EnsureSessionSubscribed();
            MerchantTax tax = PendingTax;
            if (tax == null) return Task.FromResult(Refuse("Demandez d'abord la taxe du mode marchand (Eq)."));
            if (Account?.Connexion == null || !Account.Connexion.IsConnected()) return Task.FromResult(Refuse("Connectez le personnage avant cette action."));
            long kamas = Account?.Game?.character?.Kamas ?? 0;
            if (tax.Tax > kamas) return Task.FromResult(Refuse(ExchangeRegistry.Text("NOT_ENOUGH_RICH", "Tu n'as pas assez de kamas pour réaliser cette action.")
                + " (taxe de " + tax.Tax + " kamas, " + kamas + " disponibles ; Im176)"));
            PendingTax = null;
            MerchantModeRequested = true;
            Notify();
            return SendAsync("EQ", "Passage en mode marchand demandé (taxe " + tax.Tax + " kamas) : StarLoco déconnecte le client.");
        }

        /// <summary>Refuse la taxe : rien n'est envoyé, comme le « Non » de la boîte du client.</summary>
        public void DeclineMerchantMode()
        {
            if (PendingTax == null) return;
            PendingTax = null;
            Log("Mode marchand abandonné.");
            Notify();
        }

        private void EnsureSessionSubscribed()
        {
            if (sessionSubscribed) return;
            GameSession session = Account?.Game?.Session;
            if (session == null) return;
            session.ServerMessageReceived += OnServerMessage;
            sessionSubscribed = true;
        }

        // ---- Réceptions ------------------------------------------------------------------------------------

        /// <summary>
        /// Refus de StarLoco lus dans la famille <c>Im</c> (type 1 = <c>ERROR_</c>) : 23 magasin vide, 13 carte interdite, 25 trop de marchands
        /// après <c>Eq</c> ; 76 kamas insuffisants après <c>EQ</c>. Ignorés quand rien n'est en attente (ces codes servent aussi ailleurs).
        /// </summary>
        private void OnServerMessage(ServerMessage message)
        {
            try
            {
                if (message == null || message.Kind != ServerMessageKind.Error || !message.NumericId.HasValue) return;
                int code = message.NumericId.Value;
                if ((code == 23 || code == 13 || code == 25) && (TaxRequested || PendingTax != null))
                {
                    TaxRequested = false;
                    PendingTax = null;
                    if (code == 23) shopKnownEmpty = true;
                    LogError("Mode marchand refusé par le serveur (Im1" + code.ToString(CultureInfo.InvariantCulture) + ") : " + message.Text);
                    Notify();
                }
                else if (code == 76 && MerchantModeRequested)
                {
                    MerchantModeRequested = false;
                    LogError("Mode marchand refusé : kamas insuffisants pour la taxe (Im176).");
                    Notify();
                }
            }
            catch (Exception error) when (!(error is OutOfMemoryException)) { Account?.Logger?.LogException(Reference, error); }
        }

        /// <summary><c>Eq1|1|&lt;taxe&gt;</c> (<c>Exchange.onAskOfflineExchange</c>) : type, taux (champ ÷ 10, en %) et taxe en kamas.</summary>
        internal void OnTaxProposed(string payload)
        {
            TaxRequested = false;
            try
            {
                string[] fields = (payload ?? string.Empty).Split('|');
                if (fields.Length < 3 || !TryInt(fields[0], out int type) || !TryInt(fields[1], out int rate) || !TryLong(fields[2], out long tax) || tax < 0)
                {
                    Malformed("Eq", payload);
                    Notify();
                    return;
                }
                PendingTax = new MerchantTax(type, rate / 10.0, tax);
                Log("Taxe du mode marchand : " + tax + " kamas (" + (rate / 10.0).ToString(CultureInfo.InvariantCulture) + " %).");
            }
            catch (Exception error) when (!(error is OutOfMemoryException)) { Account?.Logger?.LogError(Reference, "Paquet Eq non appliqué : " + error.Message); }
            Notify();
        }

        /// <summary><c>EiK+&lt;exemplaire&gt;|&lt;quantité&gt;|&lt;modèle&gt;|&lt;effets&gt;|&lt;prix&gt;</c>, <c>EiK-&lt;exemplaire&gt;</c> (client) ; StarLoco ne l'envoie jamais.</summary>
        internal void OnShopMovement(bool accepted, string payload)
        {
            try
            {
                if (!accepted) { LogError("Mouvement du magasin refusé par le serveur (EiE)."); Notify(); return; }
                string data = payload ?? string.Empty;
                if (data.Length < 2 || (data[0] != '+' && data[0] != '-')) { Malformed("Ei", payload); return; }
                string[] fields = data.Substring(1).Split('|');
                if (!TryId(fields[0], out uint id)) { Malformed("Ei", payload); return; }
                if (data[0] == '-')
                {
                    lock (sync) items = items.Where(item => item.Id != id).ToList();
                    Notify();
                    return;
                }
                if (fields.Length < 5 || !TryInt(fields[1], out int quantity) || quantity <= 0 || !TryInt(fields[2], out int template) || !TryLong(fields[4], out long price))
                {
                    Malformed("Ei", payload);
                    return;
                }
                var moved = new MerchantItem { Id = id, Quantity = quantity, TemplateId = template, Effects = fields[3], Price = price, Name = InventoryObjects.DisplayName(template) };
                lock (sync)
                {
                    var updated = items.ToList();
                    int index = updated.FindIndex(item => item.Id == id);
                    if (index >= 0) updated[index] = moved; else updated.Add(moved);
                    items = updated;
                }
                Notify();
            }
            catch (Exception error) when (!(error is OutOfMemoryException)) { Account?.Logger?.LogError(Reference, "Paquet Ei non appliqué : " + error.Message); }
        }

        /// <summary><c>EBK</c>/<c>EBE</c> relayés par la boutique PNJ quand c'est ce magasin qui est ouvert.</summary>
        internal void OnBuy(bool accepted)
        {
            IsPending = false;
            if (accepted) Log("Achat accepté par le serveur ; le magasin est relu dans EL.");
            else LogError("Achat refusé par le serveur (lot, quantité ou kamas).");
            Notify();
        }

        /// <summary><c>ECK4|&lt;marchand&gt;</c> ou <c>ECK6</c> : fenêtre ouverte ; la liste suit dans <c>EL</c>.</summary>
        protected override void HandleCreated(int type, string data)
        {
            openState = type == ExchangeTypes.MyShop ? AccountStates.SELLING : AccountStates.BUYING;
            if (type == ExchangeTypes.MyShop)
            {
                MerchantId = Account?.Game?.character?.id ?? -1;
                MerchantName = Account?.Game?.character?.Name ?? string.Empty;
            }
            else
            {
                bool known = TryLong(data.Split('|')[0], out long merchantId);
                MerchantId = known ? merchantId : -1;
                MerchantName = known ? (Account?.Game?.Map?.GetActor(merchantId)?.DisplayName ?? "Marchand " + merchantId) : "Marchand inconnu";
            }
            MarkOpen();
            Log(type == ExchangeTypes.MyShop ? "Magasin ouvert ; en attente de son contenu." : "Magasin de " + MerchantName + " ouvert ; en attente de la liste.");
            Notify();
        }

        /// <summary><c>EL&lt;exemplaire&gt;;&lt;quantité&gt;;&lt;modèle&gt;;&lt;effets&gt;;&lt;prix&gt;|…</c> (branche marchand de <c>Exchange.onList</c>) ; vide : magasin vide.</summary>
        protected override void HandleList(string payload)
        {
            var list = new List<MerchantItem>();
            int unreadable = 0;
            foreach (string entry in payload.Split('|'))
            {
                if (entry.Length == 0) continue;
                string[] fields = entry.Split(';');
                if (fields.Length < 5 || !TryId(fields[0], out uint id) || !TryInt(fields[1], out int quantity) || !TryInt(fields[2], out int template) || !TryLong(fields[4], out long price))
                {
                    unreadable++;
                    continue;
                }
                list.Add(new MerchantItem { Id = id, Quantity = Math.Max(0, quantity), TemplateId = template, Effects = fields[3], Price = Math.Max(0, price),
                    Name = InventoryObjects.DisplayName(template) });
            }
            lock (sync) items = list;
            ContentReceived = true;
            IsPending = false;
            if (ExchangeType == ExchangeTypes.MyShop) shopKnownEmpty = list.Count == 0;
            if (unreadable > 0) Account?.Logger?.LogError(Reference, unreadable + " lot(s) illisible(s) dans le magasin ignoré(s).");
            Log(list.Count + " lot(s) dans le magasin pour " + TotalPrice + " kamas.");
            Notify();
        }

        protected override void ResetState()
        {
            lock (sync) items = new List<MerchantItem>();
            MerchantId = -1;
            MerchantName = string.Empty;
            ContentReceived = false;
            IsPending = false;
        }

        protected override void OnClearing()
        {
            PendingTax = null;
            TaxRequested = false;
            MerchantModeRequested = false;
            shopKnownEmpty = null;
        }

        private MerchantItem Find(uint id) { lock (sync) return items.FirstOrDefault(item => item.Id == id)?.Copy(); }
    }
}
