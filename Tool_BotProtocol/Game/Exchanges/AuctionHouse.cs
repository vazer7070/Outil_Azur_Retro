using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Perso.Inventory;
using Tool_BotProtocol.Game.Session;

namespace Tool_BotProtocol.Game.Exchanges
{
    /// <summary>Paramètres d'un hôtel de vente, lus dans <c>ECK10|…</c> / <c>ECK11|…</c> (objet <c>Shop</c> du client 1.34).</summary>
    public sealed class AuctionHouseInfo
    {
        /// <summary>Quantités des trois lots (<c>1,10,100</c> chez StarLoco) ; l'indice 1, 2 ou 3 des paquets désigne l'une d'elles.</summary>
        public int[] Quantities { get; internal set; } = { 1, 10, 100 };
        /// <summary>Catégories d'objets (types <c>I.t</c> des textes du client) acceptées par cet hôtel.</summary>
        public int[] Categories { get; internal set; } = new int[0];
        /// <summary>Taxe de mise en vente, en pour cent du prix demandé.</summary>
        public double Tax { get; internal set; }
        public int MaxLevel { get; internal set; }
        /// <summary>Nombre maximal de lots en vente par compte dans cet hôtel.</summary>
        public int MaxItems { get; internal set; }
        /// <summary>Identifiant transmis par le serveur (toujours -1 chez StarLoco) : le client le renvoie dans <c>ER10</c>/<c>ER11</c> pour changer de mode.</summary>
        public int NpcId { get; internal set; } = -1;
        /// <summary>Durée de vente maximale (heures pour le client).</summary>
        public long SellTime { get; internal set; }

        /// <summary>Quantité du lot d'indice 1, 2 ou 3 ; 0 pour un indice invalide.</summary>
        public int QuantityOf(int index) => index >= 1 && index <= Quantities.Length ? Quantities[index - 1] : 0;
        public bool HasCategory(int category) => Array.IndexOf(Categories, category) >= 0;
    }

    /// <summary>Ligne de lots d'un modèle à l'achat (<c>EHl</c>, <c>EHm+</c>) : un prix par quantité (<c>priceSet1..3</c> du client), absent quand aucun lot n'existe.</summary>
    public sealed class AuctionLine
    {
        public uint Id { get; internal set; }
        public int TemplateId { get; internal set; }
        /// <summary>Effets communs aux lots de la ligne (format des fiches d'objets).</summary>
        public string Effects { get; internal set; } = string.Empty;
        /// <summary>Prix du lot pour chaque quantité (indice 0 = x1), <c>null</c> sans lot de cette quantité.</summary>
        public long?[] Prices { get; internal set; } = new long?[3];
        public string Name { get; internal set; } = string.Empty;

        /// <summary>Prix du lot d'indice 1, 2 ou 3, ou <c>null</c>.</summary>
        public long? PriceOf(int index) => index >= 1 && index <= Prices.Length ? Prices[index - 1] : null;
        internal AuctionLine Copy() { var copy = (AuctionLine)MemberwiseClone(); copy.Prices = (long?[])Prices.Clone(); return copy; }
    }

    /// <summary>Lot du personnage en vente (<c>EL</c> de l'hôtel en mode vente).</summary>
    public sealed class AuctionSale
    {
        /// <summary>Identifiant de ligne de l'hôtel, celui que <c>EMO-</c> renvoie pour retirer le lot.</summary>
        public uint LineId { get; internal set; }
        public int Quantity { get; internal set; }
        public int TemplateId { get; internal set; }
        public string Effects { get; internal set; } = string.Empty;
        public long Price { get; internal set; }
        /// <summary>Heures restantes annoncées (350 fixe chez StarLoco).</summary>
        public int RemainingHours { get; internal set; }
        public string Name { get; internal set; } = string.Empty;

        internal AuctionSale Copy() => (AuctionSale)MemberwiseClone();
    }

    /// <summary>
    /// Hôtel de vente (interfaces <c>BigStoreBuy</c> et <c>BigStoreSell</c> du client 1.34, types 11 et 10 du registre des échanges).
    /// Ouverture par l'action 6 (<c>ER11|&lt;pnj&gt;</c>) ou 5 (<c>ER10|&lt;pnj&gt;</c>) du menu d'un PNJ ; StarLoco (<c>GameClient.request</c>) répond
    /// <c>ECK11|1,10,100;&lt;catégories&gt;;&lt;taxe&gt;;&lt;niveau max&gt;;&lt;lots max&gt;;-1;&lt;durée&gt;</c> ou <c>ECK10|…</c> suivi de <c>EL</c> (lots du vendeur),
    /// refuse par <c>Im183</c> au-delà de 5 points de déshonneur, et ne répond rien sur une carte sans hôtel.
    /// Achat (<c>GameClient.bigStore</c>) : <c>EHT&lt;catégorie&gt;</c> → <c>EHL&lt;catégorie&gt;|&lt;modèle;…&gt;</c> ; <c>EHl&lt;modèle&gt;</c> → <c>EHl&lt;modèle&gt;|&lt;ligne;effets;prix x1;prix x10;prix x100|…&gt;</c>
    /// (ou <c>EHM-&lt;modèle&gt;</c> s'il n'est plus en vente) ; <c>EHP&lt;modèle&gt;</c> → <c>EHP&lt;modèle&gt;|&lt;prix moyen&gt;</c> ; <c>EHS&lt;catégorie&gt;|&lt;modèle&gt;</c> → <c>EHS</c> (introuvable)
    /// ou <c>EHSK</c> puis <c>EHL</c>, <c>EHP</c>, <c>EHl</c> ; <c>EHB&lt;ligne&gt;|&lt;indice&gt;|&lt;prix&gt;</c> → <c>EHm-&lt;ligne&gt;</c>, <c>EHm+&lt;ligne|modèle|effets|prix x1|x10|x100&gt;</c> si la ligne reste garnie,
    /// <c>Ow</c>, <c>Im068</c> (lot acheté) ou <c>Im172</c> (plus disponible à ce prix). Vente (<c>movementItemOrKamas</c>) : <c>EMO+&lt;objet&gt;|&lt;indice&gt;|&lt;prix&gt;</c> → <c>As</c>,
    /// <c>OR</c>/<c>OQ</c>, <c>EmK+…</c> puis la liste <c>EL</c> entière, refus <c>Im058</c> (lots max) ou <c>Im176</c> (kamas insuffisants pour la taxe) ;
    /// <c>EMO-&lt;ligne&gt;|&lt;quantité&gt;</c> → <c>EmK-&lt;ligne&gt;</c>. Changer de mode envoie <c>ER10|-1</c> / <c>ER11|-1</c> comme le client ; fermer <c>EV</c>.
    /// </summary>
    [ExchangeType(ExchangeTypes.AuctionSell)]
    [ExchangeType(ExchangeTypes.AuctionBuy)]
    public sealed class AuctionHouse : ExchangeWindow
    {
        /// <summary>Nombre de lots possibles (x1, x10, x100) : un indice de 1 à 3 dans <c>EHB</c> et <c>EMO+</c>.</summary>
        public const int QuantityIndexes = 3;
        /// <summary>Plafond de StarLoco : <c>maxAccountItem</c> est un <c>short</c>, <c>EMO+</c> refuse un indice au-delà de 127.</summary>
        public const int MaxSalesPerAccount = 127;
        private readonly object sync = new object();
        private AccountStates openState = AccountStates.BUYING;
        private List<int> templates = new List<int>();
        private List<AuctionLine> lines = new List<AuctionLine>();
        private readonly Dictionary<int, long> averagePrices = new Dictionary<int, long>();
        private List<AuctionSale> sales = new List<AuctionSale>();
        private bool sessionSubscribed;

        internal AuctionHouse(Accounts.Accounts account) : base(account) { }

        protected override string Reference => "HDV";
        /// <summary><c>BUYING</c> en mode achat, <c>SELLING</c> en mode vente.</summary>
        protected override AccountStates OpenState => openState;

        public bool IsBuying => IsOpen && ExchangeType == ExchangeTypes.AuctionBuy;
        public bool IsSelling => IsOpen && ExchangeType == ExchangeTypes.AuctionSell;
        /// <summary>Paramètres de l'hôtel ouvert (jamais <c>null</c> ; valeurs par défaut hors hôtel).</summary>
        public AuctionHouseInfo Info { get; private set; } = new AuctionHouseInfo();
        /// <summary>Vrai entre l'envoi de <c>ER10</c>/<c>ER11</c> et la réponse du serveur (<c>ECK</c>, <c>Im183</c>).</summary>
        public bool IsOpeningPending { get; private set; }

        // ---- Achat ----------------------------------------------------------------------------------------

        /// <summary>Catégorie demandée par <c>EHT</c> et confirmée par <c>EHL</c> ; -1 sans catégorie.</summary>
        public int CurrentCategory { get; private set; } = -1;
        /// <summary>Modèles en vente dans la catégorie courante (<c>EHL</c>, retraits <c>EHM-</c>).</summary>
        public IReadOnlyList<int> Templates { get { lock (sync) return templates.ToArray(); } }
        /// <summary>Modèle demandé par <c>EHl</c> ; -1 sans modèle.</summary>
        public int CurrentTemplate { get; private set; } = -1;
        /// <summary>Lignes de lots du modèle courant (copies).</summary>
        public IReadOnlyList<AuctionLine> Lines { get { lock (sync) return lines.Select(line => line.Copy()).ToArray(); } }
        /// <summary>Prix moyens reçus par <c>EHP</c>, par modèle.</summary>
        public IReadOnlyDictionary<int, long> AveragePrices { get { lock (sync) return new Dictionary<int, long>(averagePrices); } }
        /// <summary>Vrai entre <c>EHB</c> et la réponse du serveur (<c>EHm</c>, <c>Im068</c> ou <c>Im172</c>).</summary>
        public bool IsBuyPending { get; private set; }
        /// <summary>Résultat de la dernière recherche <c>EHS</c> : <c>true</c> trouvé, <c>false</c> introuvable, <c>null</c> sans recherche.</summary>
        public bool? LastSearchFound { get; private set; }

        // ---- Vente ----------------------------------------------------------------------------------------

        /// <summary>Lots du personnage en vente dans cet hôtel (copies, ordre de <c>EL</c>).</summary>
        public IReadOnlyList<AuctionSale> Sales { get { lock (sync) return sales.Select(sale => sale.Copy()).ToArray(); } }
        /// <summary>Vrai dès que la liste <c>EL</c> du mode vente a été reçue.</summary>
        public bool SalesReceived { get; private set; }
        /// <summary>Vrai entre <c>EMO+</c> et la réponse du serveur (<c>EL</c>, <c>Im058</c> ou <c>Im176</c>).</summary>
        public bool IsSellPending { get; private set; }

        /// <summary>Taxe de mise en vente telle que StarLoco la débite : <c>(int)(prix × taxe / 100)</c> (le client affiche <c>max(1, round(…))</c>).</summary>
        public static long TaxFor(long price, double taxPercent) => price <= 0 || taxPercent <= 0 ? 0 : (long)(price * (taxPercent / 100.0));

        /// <summary>Catégorie (type <c>I.t</c>) d'un objet du sac : fiche <c>BotObjets</c>, sinon textes du client, sinon 0.</summary>
        public static int CategoryOf(InventoryObjects item)
        {
            if (item == null) return 0;
            if (item.Type != 0) return item.Type;
            try { return LangData.Item.Type(item.ID) ?? 0; } catch (Exception) { return 0; }
        }

        /// <summary>Niveau d'un objet du sac : fiche <c>BotObjets</c>, sinon textes du client, sinon 0.</summary>
        public static int LevelOf(InventoryObjects item)
        {
            if (item == null) return 0;
            if (item.Level != 0) return item.Level;
            try { return LangData.Item.Level(item.ID) ?? 0; } catch (Exception) { return 0; }
        }

        /// <summary>
        /// Vérifie, comme <c>BigStoreSell.selectedItem</c>, qu'un objet peut être mis en vente ici : catégorie acceptée et niveau au plus égal au maximum.
        /// Indispensable : StarLoco (<c>Hdv.addEntry</c>) encaisse la taxe et retire l'objet du sac sans l'ajouter quand la catégorie n'est pas celle de l'hôtel.
        /// </summary>
        public bool CanSell(InventoryObjects item, out string reason)
        {
            reason = null;
            if (item == null) { reason = "Cet objet n'est pas dans votre inventaire."; return false; }
            if (item.IsEquipped()) { reason = "Déséquipez cet objet avant de le mettre en vente."; return false; }
            if (!Info.HasCategory(CategoryOf(item)))
            {
                reason = ExchangeRegistry.Text("BIGSTORE_BAD_TYPE", "Il est impossible de mettre en vente cette catégorie d'objet dans cet hôtel de vente.");
                return false;
            }
            if (LevelOf(item) > Info.MaxLevel)
            {
                reason = ExchangeRegistry.Text("BIGSTORE_BAD_LEVEL", "Cet objet est trop haut niveau pour cet hôtel de vente.");
                return false;
            }
            return true;
        }

        /// <summary>Indices de lots (1, 2, 3) dont la quantité ne dépasse pas celle possédée (<c>BigStoreSell.populateComboBox</c>).</summary>
        public int[] QuantityIndexesFor(int owned) =>
            Enumerable.Range(1, Math.Min(QuantityIndexes, Info.Quantities.Length)).Where(index => Info.QuantityOf(index) <= owned && Info.QuantityOf(index) > 0).ToArray();

        /// <summary>Modèle d'objet dont le nom (fiche <c>BotObjets</c> ou textes <c>items</c> du client) correspond, et sa catégorie ; <c>null</c> sinon.</summary>
        public static KeyValuePair<int, int>? FindTemplate(string name)
        {
            string wanted = (name ?? string.Empty).Trim();
            if (wanted.Length == 0) return null;
            try
            {
                foreach (InventoryObjects template in InventoryObjects.FullInventory.Values.OrderBy(entry => entry.ID))
                    if (string.Equals(template.Name, wanted, StringComparison.CurrentCultureIgnoreCase)) return new KeyValuePair<int, int>(template.ID, template.Type);
                if (LangData.IsLoaded("items"))
                    foreach (string id in LangData.Ids("items", "objet"))
                        if (int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out int templateId)
                            && string.Equals(LangData.Item.Name(templateId), wanted, StringComparison.CurrentCultureIgnoreCase))
                            return new KeyValuePair<int, int>(templateId, LangData.Item.Type(templateId) ?? 0);
            }
            catch (Exception) { /* Fiches illisibles : objet introuvable. */ }
            return null;
        }

        // ---- Envois du client -----------------------------------------------------------------------------

        /// <summary>Envoie <c>ER11|&lt;pnj&gt;</c> (action 6 « Acheter » du menu d'un PNJ, <c>Exchange.request</c>).</summary>
        public Task<InteractionResult> OpenBuyAsync(int npcId) => OpenAsync(ExchangeTypes.AuctionBuy, npcId);

        /// <summary>Envoie <c>ER10|&lt;pnj&gt;</c> (action 5 « Vendre »).</summary>
        public Task<InteractionResult> OpenSellAsync(int npcId) => OpenAsync(ExchangeTypes.AuctionSell, npcId);

        /// <summary>
        /// Passe de l'achat à la vente ou l'inverse (<c>BIGSTORE_MODE_SELL</c>/<c>BIGSTORE_MODE_BUY</c>) : <c>ER10|&lt;npcID&gt;</c> ou <c>ER11|&lt;npcID&gt;</c> avec
        /// l'identifiant reçu dans <c>ECK</c> (-1 chez StarLoco). StarLoco oublie l'échange en cours et répond par le nouvel <c>ECK</c>, sans <c>EV</c>.
        /// </summary>
        public Task<InteractionResult> SwitchModeAsync()
        {
            if (!IsOpen) return Task.FromResult(Refuse("Aucun hôtel de vente ouvert."));
            if (IsOpeningPending) return Task.FromResult(Refuse("Attendez la réponse du serveur au changement de mode précédent."));
            if (Account?.IsFighting() == true) return Task.FromResult(Refuse("Action impossible pendant un combat."));
            int type = IsBuying ? ExchangeTypes.AuctionSell : ExchangeTypes.AuctionBuy;
            IsOpeningPending = true;
            return SendAsync("ER" + type.ToString(CultureInfo.InvariantCulture) + "|" + Info.NpcId.ToString(CultureInfo.InvariantCulture),
                (type == ExchangeTypes.AuctionSell ? "Mode vente" : "Mode achat") + " demandé ; le serveur rouvre l'hôtel.");
        }

        /// <summary>Envoie <c>EHT&lt;catégorie&gt;</c> ; la catégorie doit être proposée par l'hôtel (StarLoco ne répond rien sinon).</summary>
        public Task<InteractionResult> SelectCategoryAsync(int category)
        {
            if (!IsBuying) return Task.FromResult(Refuse("L'hôtel de vente n'est pas ouvert en mode achat."));
            if (!Info.HasCategory(category)) return Task.FromResult(Refuse("Cette catégorie n'est pas proposée par cet hôtel de vente."));
            lock (sync) { templates = new List<int>(); lines = new List<AuctionLine>(); }
            CurrentCategory = category;
            CurrentTemplate = -1;
            Notify();
            return SendAsync("EHT" + category.ToString(CultureInfo.InvariantCulture), "Objets de la catégorie « " + CategoryName(category) + " » demandés.");
        }

        /// <summary>Envoie <c>EHl&lt;modèle&gt;</c> pour un modèle de la catégorie courante ; les lignes arrivent dans <c>EHl</c>.</summary>
        public Task<InteractionResult> SelectTemplateAsync(int templateId)
        {
            if (!IsBuying) return Task.FromResult(Refuse("L'hôtel de vente n'est pas ouvert en mode achat."));
            lock (sync) { if (!templates.Contains(templateId)) return Task.FromResult(Refuse("Ce modèle n'est pas dans la liste reçue du serveur.")); lines = new List<AuctionLine>(); }
            CurrentTemplate = templateId;
            Notify();
            return SendAsync("EHl" + templateId.ToString(CultureInfo.InvariantCulture), "Lots de « " + InventoryObjects.DisplayName(templateId) + " » demandés.");
        }

        /// <summary>Envoie <c>EHP&lt;modèle&gt;</c> (<c>getItemMiddlePriceInBigStore</c>) ; réponse <c>EHP&lt;modèle&gt;|&lt;prix moyen&gt;</c>.</summary>
        public Task<InteractionResult> RequestAveragePriceAsync(int templateId)
        {
            if (!IsOpen) return Task.FromResult(Refuse("Aucun hôtel de vente ouvert."));
            if (templateId < 0) return Task.FromResult(Refuse("Modèle invalide."));
            return SendAsync("EHP" + templateId.ToString(CultureInfo.InvariantCulture), "Prix moyen de « " + InventoryObjects.DisplayName(templateId) + " » demandé.");
        }

        /// <summary>Envoie <c>EHS&lt;catégorie&gt;|&lt;modèle&gt;</c> (<c>bigStoreSearch</c>) ; StarLoco répond <c>EHS</c> (introuvable) ou <c>EHSK</c> puis <c>EHL</c>, <c>EHP</c>, <c>EHl</c>.</summary>
        public Task<InteractionResult> SearchAsync(int category, int templateId)
        {
            if (!IsBuying) return Task.FromResult(Refuse("L'hôtel de vente n'est pas ouvert en mode achat."));
            if (!Info.HasCategory(category)) return Task.FromResult(Refuse("Cette catégorie n'est pas proposée par cet hôtel de vente."));
            if (templateId < 0) return Task.FromResult(Refuse("Modèle invalide."));
            LastSearchFound = null;
            lock (sync) { templates = new List<int>(); lines = new List<AuctionLine>(); }
            CurrentCategory = category;
            CurrentTemplate = templateId;
            Notify();
            return SendAsync("EHS" + category.ToString(CultureInfo.InvariantCulture) + "|" + templateId.ToString(CultureInfo.InvariantCulture),
                "Recherche de « " + InventoryObjects.DisplayName(templateId) + " » demandée.");
        }

        /// <summary>
        /// Envoie <c>EHB&lt;ligne&gt;|&lt;indice&gt;|&lt;prix&gt;</c> pour le lot d'une ligne reçue, au prix affiché (StarLoco cherche un lot à ce prix exact,
        /// sinon <c>Im172</c>) après le contrôle des kamas (<c>NOT_ENOUGH_RICH</c> dans le client).
        /// </summary>
        public Task<InteractionResult> BuyAsync(uint lineId, int quantityIndex, long price)
        {
            if (!IsBuying) return Task.FromResult(Refuse("L'hôtel de vente n'est pas ouvert en mode achat."));
            if (IsBuyPending) return Task.FromResult(Refuse("Attendez la réponse du serveur à l'achat précédent."));
            AuctionLine line;
            lock (sync) line = lines.FirstOrDefault(entry => entry.Id == lineId)?.Copy();
            if (line == null) return Task.FromResult(Refuse("Ce lot n'est pas dans la liste reçue du serveur."));
            long? listed = line.PriceOf(quantityIndex);
            if (listed == null) return Task.FromResult(Refuse("Aucun lot de cette quantité pour cette ligne."));
            if (listed.Value != price) return Task.FromResult(Refuse("Le prix demandé ne correspond pas au lot affiché (" + listed.Value + " kamas)."));
            long kamas = Account?.Game?.character?.Kamas ?? 0;
            if (price > kamas) return Task.FromResult(Refuse(ExchangeRegistry.Text("NOT_ENOUGH_RICH", "Tu n'as pas assez de kamas pour réaliser cette action.")
                + " (" + price.ToString(CultureInfo.InvariantCulture) + " demandés, " + kamas.ToString(CultureInfo.InvariantCulture) + " disponibles)"));
            IsBuyPending = true;
            Notify();
            return SendAsync("EHB" + lineId.ToString(CultureInfo.InvariantCulture) + "|" + quantityIndex.ToString(CultureInfo.InvariantCulture) + "|" + price.ToString(CultureInfo.InvariantCulture),
                "Achat de x" + Info.QuantityOf(quantityIndex) + " " + line.Name + " pour " + price + " kamas demandé ; le serveur confirme.");
        }

        /// <summary>
        /// Envoie <c>EMO+&lt;objet&gt;|&lt;indice&gt;|&lt;prix&gt;</c> (<c>Exchange.movementItem</c> avec un prix) pour un objet du sac : catégorie et niveau
        /// acceptés par l'hôtel, quantité du lot possédée, prix entier positif, lots du compte sous le maximum (<c>Im058</c>) et kamas pour la taxe (<c>Im176</c>).
        /// </summary>
        public Task<InteractionResult> SellAsync(uint inventoryId, int quantityIndex, long price)
        {
            if (!IsSelling) return Task.FromResult(Refuse("L'hôtel de vente n'est pas ouvert en mode vente."));
            if (IsSellPending) return Task.FromResult(Refuse("Attendez la réponse du serveur à la mise en vente précédente."));
            InventoryObjects item = Account?.Game?.character?.Inventory?.GetByInventoryId(inventoryId);
            if (!CanSell(item, out string reason)) return Task.FromResult(Refuse(reason));
            int quantity = Info.QuantityOf(quantityIndex);
            if (quantityIndex < 1 || quantityIndex > QuantityIndexes || quantity <= 0) return Task.FromResult(Refuse(ExchangeRegistry.Text("ERROR_INVALID_QUANTITY", "La quantité est invalide.")));
            if (quantity > item.Qua) return Task.FromResult(Refuse("Vous ne possédez pas " + quantity + " exemplaires de " + item.Name + " (" + item.Qua + ")."));
            if (price <= 0 || price > int.MaxValue) return Task.FromResult(Refuse(ExchangeRegistry.Text("ERROR_INVALID_PRICE", "Le prix est invalide.")));
            int count; lock (sync) count = sales.Count;
            if (count >= Math.Min(Info.MaxItems, MaxSalesPerAccount)) return Task.FromResult(Refuse("Nombre maximal de lots en vente atteint (" + Math.Min(Info.MaxItems, MaxSalesPerAccount) + ")."));
            long tax = TaxFor(price, Info.Tax);
            long kamas = Account?.Game?.character?.Kamas ?? 0;
            if (tax > kamas) return Task.FromResult(Refuse(ExchangeRegistry.Text("NOT_ENOUGH_RICH", "Tu n'as pas assez de kamas pour réaliser cette action.")
                + " (taxe de " + tax + " kamas, " + kamas + " disponibles)"));
            IsSellPending = true;
            Notify();
            return SendAsync("EMO+" + inventoryId.ToString(CultureInfo.InvariantCulture) + "|" + quantityIndex.ToString(CultureInfo.InvariantCulture) + "|" + price.ToString(CultureInfo.InvariantCulture),
                "Mise en vente de x" + quantity + " " + item.Name + " à " + price + " kamas (taxe " + tax + ") demandée ; le serveur renvoie la liste.");
        }

        /// <summary>Envoie <c>EMO-&lt;ligne&gt;|&lt;quantité&gt;</c> pour un lot en vente ; StarLoco répond <c>EmK-&lt;ligne&gt;</c> et rend l'objet au sac.</summary>
        public Task<InteractionResult> RemoveSaleAsync(uint lineId)
        {
            if (!IsSelling) return Task.FromResult(Refuse("L'hôtel de vente n'est pas ouvert en mode vente."));
            AuctionSale sale;
            lock (sync) sale = sales.FirstOrDefault(entry => entry.LineId == lineId)?.Copy();
            if (sale == null) return Task.FromResult(Refuse("Ce lot n'est pas dans votre liste de vente."));
            return SendAsync("EMO-" + lineId.ToString(CultureInfo.InvariantCulture) + "|" + Math.Max(1, sale.Quantity).ToString(CultureInfo.InvariantCulture),
                "Retrait de x" + sale.Quantity + " " + sale.Name + " demandé.");
        }

        /// <summary>Envoie <c>EV</c> ; StarLoco oublie l'échange et répond <c>EV</c>.</summary>
        public Task<InteractionResult> LeaveAsync()
        {
            if (!IsOpen) return Task.FromResult(Refuse("Aucun hôtel de vente ouvert."));
            return SendAsync("EV", "Fermeture de l'hôtel de vente demandée.");
        }

        /// <summary>Une demande en attente ne bloque pas la suivante : sur une carte sans hôtel, StarLoco ne répond rien.</summary>
        private Task<InteractionResult> OpenAsync(int type, int npcId)
        {
            EnsureSessionSubscribed();
            InteractionResult refused = CheckCanOpen();
            if (refused != null) return Task.FromResult(refused);
            IsOpeningPending = true;
            string packet = "ER" + type.ToString(CultureInfo.InvariantCulture) + "|" + npcId.ToString(CultureInfo.InvariantCulture);
            return SendAsync(packet, "Hôtel de vente (" + (type == ExchangeTypes.AuctionBuy ? "achat" : "vente") + ") demandé (" + packet + ") ; sans hôtel sur cette carte, StarLoco ne répond rien.");
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
        /// Messages <c>Im</c> de l'hôtel (lot S1) : <c>Im068</c> lot acheté et <c>Im172</c> plus disponible après <c>EHB</c> ; <c>Im058</c> lots max et <c>Im176</c>
        /// kamas insuffisants après <c>EMO+</c> ; <c>Im183</c> déshonneur après <c>ER10</c>/<c>ER11</c>. Ignorés quand rien n'est en attente (codes partagés).
        /// </summary>
        private void OnServerMessage(ServerMessage message)
        {
            try
            {
                if (message == null || !message.NumericId.HasValue) return;
                int code = message.NumericId.Value;
                if (message.Kind == ServerMessageKind.Info && code == 68 && IsBuyPending)
                {
                    IsBuyPending = false;
                    Log(message.Text);
                    Notify();
                }
                else if (message.Kind == ServerMessageKind.Error && code == 72 && IsBuyPending)
                {
                    IsBuyPending = false;
                    LogError("Achat refusé par le serveur (Im172) : " + message.Text);
                    Notify();
                }
                else if (IsSellPending && ((message.Kind == ServerMessageKind.Info && code == 58) || (message.Kind == ServerMessageKind.Error && code == 76)))
                {
                    IsSellPending = false;
                    LogError("Mise en vente refusée par le serveur (Im" + (message.Kind == ServerMessageKind.Info ? "0" : "1") + code.ToString(CultureInfo.InvariantCulture) + ") : " + message.Text);
                    Notify();
                }
                else if (message.Kind == ServerMessageKind.Error && code == 83 && IsOpeningPending)
                {
                    IsOpeningPending = false;
                    LogError("Hôtel de vente refusé par le serveur (Im183, déshonneur) : " + message.Text);
                    Notify();
                }
            }
            catch (Exception error) when (!(error is OutOfMemoryException)) { Account?.Logger?.LogException(Reference, error); }
        }

        /// <summary><c>ECK10|…</c> / <c>ECK11|…</c> : <c>q1,q2,q3;catégories;taxe;niveau max;lots max;pnj;durée</c> (<c>Exchange.onCreate</c>) ; un champ illisible garde sa valeur par défaut.</summary>
        protected override void HandleCreated(int type, string data)
        {
            EnsureSessionSubscribed();
            openState = type == ExchangeTypes.AuctionSell ? AccountStates.SELLING : AccountStates.BUYING;
            IsOpeningPending = false;
            var info = new AuctionHouseInfo();
            string[] fields = data.Split(';');
            bool readable = fields.Length >= 7;
            if (fields.Length > 0 && fields[0].Length > 0)
            {
                int[] quantities = fields[0].Split(',').Select(value => TryInt(value, out int quantity) && quantity > 0 ? quantity : 0).ToArray();
                if (quantities.Length == QuantityIndexes && quantities.All(quantity => quantity > 0)) info.Quantities = quantities; else readable = false;
            }
            else readable = false;
            if (fields.Length > 1)
            {
                var categories = new List<int>();
                foreach (string value in fields[1].Split(','))
                {
                    if (value.Length == 0) continue;
                    if (TryInt(value, out int category)) categories.Add(category); else readable = false;
                }
                info.Categories = categories.Distinct().ToArray();
            }
            if (fields.Length > 2) { if (double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double tax) && tax >= 0) info.Tax = tax; else readable = false; }
            if (fields.Length > 3) { if (TryInt(fields[3], out int level)) info.MaxLevel = level; else readable = false; }
            if (fields.Length > 4) { if (TryInt(fields[4], out int max)) info.MaxItems = max; else readable = false; }
            if (fields.Length > 5) { if (TryInt(fields[5], out int npc)) info.NpcId = npc; else readable = false; }
            if (fields.Length > 6) { if (TryLong(fields[6], out long time)) info.SellTime = time; else readable = false; }
            Info = info;
            if (!readable) Malformed("ECK" + type, data);
            MarkOpen();
            Log(type == ExchangeTypes.AuctionSell
                ? "Hôtel de vente ouvert en mode vente (" + info.Categories.Length + " catégories, taxe " + info.Tax.ToString(CultureInfo.InvariantCulture) + " %, " + info.MaxItems + " lots max) ; en attente de vos lots."
                : "Hôtel de vente ouvert en mode achat (" + info.Categories.Length + " catégories, niveau max " + info.MaxLevel + ").");
            Notify();
        }

        /// <summary><c>EL&lt;ligne;quantité;modèle;effets;prix;heures|…&gt;</c> (branche 10 de <c>Exchange.onList</c>) : lots du vendeur, vide quand il n'en a aucun.</summary>
        protected override void HandleList(string payload)
        {
            if (!IsSelling) { Unexpected("EL", payload); return; }
            var list = new List<AuctionSale>();
            int unreadable = 0;
            foreach (string entry in payload.Split('|'))
            {
                if (entry.Length == 0) continue;
                string[] fields = entry.Split(';');
                if (fields.Length < 5 || !TryId(fields[0], out uint lineId) || !TryInt(fields[1], out int quantity) || !TryInt(fields[2], out int template) || !TryLong(fields[4], out long price))
                {
                    unreadable++;
                    continue;
                }
                int hours = fields.Length > 5 && TryInt(fields[5], out int remaining) ? remaining : 0;
                list.Add(new AuctionSale { LineId = lineId, Quantity = Math.Max(0, quantity), TemplateId = template, Effects = fields[3], Price = Math.Max(0, price),
                    RemainingHours = hours, Name = InventoryObjects.DisplayName(template) });
            }
            if (unreadable > 0 && list.Count == 0) { IsSellPending = false; Malformed("EL", payload); Notify(); return; }
            lock (sync) sales = list;
            SalesReceived = true;
            IsSellPending = false;
            if (unreadable > 0) Account?.Logger?.LogError(Reference, unreadable + " lot(s) illisible(s) dans la liste de vente ignoré(s).");
            Log(list.Count + " lot(s) en vente dans cet hôtel (" + Math.Min(Info.MaxItems, MaxSalesPerAccount) + " max).");
            Notify();
        }

        /// <summary>
        /// <c>EmK</c> en mode vente. StarLoco envoie <c>EmK+&lt;exemplaire&gt;|&lt;quantité&gt;|&lt;modèle&gt;|&lt;effets&gt;|&lt;prix&gt;|&lt;heures&gt;</c> (sans la lettre <c>O</c> du
        /// client, avec l'identifiant d'exemplaire et non celui de la ligne) puis la liste <c>EL</c> entière : l'ajout n'est que journalisé, la liste fait foi.
        /// <c>EmK-&lt;ligne&gt;</c> (retrait, sans <c>EL</c> ensuite) retire la ligne. Le format du client (<c>O+</c>/<c>O-</c>) est lu aussi.
        /// </summary>
        protected override void HandleDistantMovement(string data)
        {
            if (!IsSelling) { Unexpected("EmK", data); return; }
            string body = data.Length > 0 && data[0] == 'O' ? data.Substring(1) : data;
            if (body.Length < 2 || (body[0] != '+' && body[0] != '-')) { Malformed("EmK", data); return; }
            string[] fields = body.Substring(1).Split('|');
            if (!TryId(fields[0], out uint id)) { Malformed("EmK", data); return; }
            if (body[0] == '-')
            {
                bool removed;
                lock (sync)
                {
                    removed = sales.Any(sale => sale.LineId == id);
                    if (removed) sales = sales.Where(sale => sale.LineId != id).ToList();
                }
                if (removed) Log("Lot retiré de l'hôtel de vente ; l'objet revient dans le sac."); else Account?.Logger?.LogDebug(Reference, "EmK- pour une ligne inconnue (" + id + ").");
                Notify();
                return;
            }
            if (fields.Length < 5 || !TryInt(fields[1], out int quantity) || !TryInt(fields[2], out int template) || !TryLong(fields[4], out long price)) { Malformed("EmK", data); return; }
            Log("Mise en vente acceptée : x" + quantity + " " + InventoryObjects.DisplayName(template) + " à " + price + " kamas ; la liste suit.");
            Notify();
        }

        /// <summary><c>EHL&lt;catégorie&gt;|&lt;modèle;modèle;…&gt;</c> (<c>onBigStoreTypeItemsList</c>) ; liste vide sans objet.</summary>
        internal void OnTemplates(string payload)
        {
            try
            {
                if (!IsBuying) { Unexpected("EHL", payload); return; }
                string body = payload ?? string.Empty;
                int separator = body.IndexOf('|');
                if (separator < 0 || !TryInt(body.Substring(0, separator), out int category)) { Malformed("EHL", payload); return; }
                var list = new List<int>();
                int unreadable = 0;
                foreach (string value in body.Substring(separator + 1).Split(';'))
                {
                    if (value.Length == 0) continue;
                    if (TryInt(value, out int template)) { if (!list.Contains(template)) list.Add(template); } else unreadable++;
                }
                lock (sync) { templates = list; if (CurrentTemplate >= 0 && !list.Contains(CurrentTemplate)) { CurrentTemplate = -1; lines = new List<AuctionLine>(); } }
                CurrentCategory = category;
                if (unreadable > 0) Account?.Logger?.LogError(Reference, unreadable + " modèle(s) illisible(s) ignoré(s) dans EHL.");
                Log(list.Count + " objet(s) en vente dans la catégorie « " + CategoryName(category) + " ».");
                Notify();
            }
            catch (Exception error) when (!(error is OutOfMemoryException)) { Account?.Logger?.LogError(Reference, "Paquet EHL non appliqué : " + error.Message); }
        }

        /// <summary><c>EHM+&lt;modèle&gt;</c> / <c>EHM-&lt;modèle&gt;</c> (<c>onBigStoreTypeItemsMovement</c>) ; StarLoco n'envoie que le retrait, quand un modèle n'est plus en vente.</summary>
        internal void OnTemplateMovement(string payload)
        {
            try
            {
                if (!IsBuying) { Unexpected("EHM", payload); return; }
                string body = payload ?? string.Empty;
                if (body.Length < 2 || (body[0] != '+' && body[0] != '-') || !TryInt(body.Substring(1), out int template)) { Malformed("EHM", payload); return; }
                lock (sync)
                {
                    if (body[0] == '+') { if (!templates.Contains(template)) templates.Add(template); }
                    else
                    {
                        templates.Remove(template);
                        if (CurrentTemplate == template) { CurrentTemplate = -1; lines = new List<AuctionLine>(); }
                    }
                }
                Log(body[0] == '+' ? "« " + InventoryObjects.DisplayName(template) + " » est maintenant en vente." : "« " + InventoryObjects.DisplayName(template) + " » n'est plus en vente.");
                Notify();
            }
            catch (Exception error) when (!(error is OutOfMemoryException)) { Account?.Logger?.LogError(Reference, "Paquet EHM non appliqué : " + error.Message); }
        }

        /// <summary><c>EHl&lt;modèle&gt;|&lt;ligne;effets;prix x1;prix x10;prix x100|…&gt;</c> (<c>onBigStoreItemsList</c>) ; <c>EHl</c> vide quand StarLoco ne retrouve pas le modèle.</summary>
        internal void OnLines(string payload)
        {
            try
            {
                if (!IsBuying) { Unexpected("EHl", payload); return; }
                string body = payload ?? string.Empty;
                string[] parts = body.Split('|');
                if (body.Length == 0) { lock (sync) lines = new List<AuctionLine>(); Log("Aucun lot pour ce modèle."); Notify(); return; }
                if (!TryInt(parts[0], out int template)) { Malformed("EHl", payload); return; }
                var list = new List<AuctionLine>();
                int unreadable = 0;
                for (int index = 1; index < parts.Length; index++)
                {
                    AuctionLine line = ParseLine(template, parts[index].Split(';'), 0);
                    if (line == null) unreadable++; else list.Add(line);
                }
                if (unreadable > 0 && list.Count == 0) { Malformed("EHl", payload); return; }
                lock (sync) { lines = list; if (!templates.Contains(template)) templates.Add(template); }
                CurrentTemplate = template;
                if (unreadable > 0) Account?.Logger?.LogError(Reference, unreadable + " ligne(s) illisible(s) ignorée(s) dans EHl.");
                Log(list.Count + " ligne(s) de lots pour « " + InventoryObjects.DisplayName(template) + " ».");
                Notify();
            }
            catch (Exception error) when (!(error is OutOfMemoryException)) { Account?.Logger?.LogError(Reference, "Paquet EHl non appliqué : " + error.Message); }
        }

        /// <summary><c>EHm+&lt;ligne|modèle|effets|prix x1|x10|x100&gt;</c> / <c>EHm-&lt;ligne&gt;</c> (<c>onBigStoreItemsMovement</c>) après un achat.</summary>
        internal void OnLineMovement(string payload)
        {
            try
            {
                if (!IsBuying) { Unexpected("EHm", payload); return; }
                string body = payload ?? string.Empty;
                if (body.Length < 2 || (body[0] != '+' && body[0] != '-')) { Malformed("EHm", payload); return; }
                string[] fields = body.Substring(1).Split('|');
                if (!TryId(fields[0], out uint id)) { Malformed("EHm", payload); return; }
                IsBuyPending = false;
                if (body[0] == '-')
                {
                    lock (sync) lines = lines.Where(line => line.Id != id).ToList();
                    Notify();
                    return;
                }
                if (fields.Length < 3 || !TryInt(fields[1], out int template)) { Malformed("EHm", payload); return; }
                AuctionLine moved = ParseLine(template, new[] { fields[0] }.Concat(fields.Skip(2)).ToArray(), 0);
                if (moved == null) { Malformed("EHm", payload); return; }
                lock (sync)
                {
                    var updated = lines.ToList();
                    int index = updated.FindIndex(line => line.Id == id);
                    if (index >= 0) updated[index] = moved; else if (CurrentTemplate == template) updated.Add(moved);
                    lines = updated;
                }
                Notify();
            }
            catch (Exception error) when (!(error is OutOfMemoryException)) { Account?.Logger?.LogError(Reference, "Paquet EHm non appliqué : " + error.Message); }
        }

        /// <summary><c>EHP&lt;modèle&gt;|&lt;prix moyen&gt;</c> (<c>onItemMiddlePriceInBigStore</c>), lu dans les deux modes.</summary>
        internal void OnAveragePrice(string payload)
        {
            try
            {
                if (!IsOpen) { Unexpected("EHP", payload); return; }
                string[] fields = (payload ?? string.Empty).Split('|');
                if (fields.Length < 2 || !TryInt(fields[0], out int template) || !TryLong(fields[1], out long price)) { Malformed("EHP", payload); return; }
                lock (sync) averagePrices[template] = price;
                Notify();
            }
            catch (Exception error) when (!(error is OutOfMemoryException)) { Account?.Logger?.LogError(Reference, "Paquet EHP non appliqué : " + error.Message); }
        }

        /// <summary><c>EHS</c> (objet introuvable, <c>ITEM_NOT_IN_BIGSTORE</c>) ou <c>EHSK</c> (trouvé : <c>EHL</c>, <c>EHP</c> et <c>EHl</c> suivent) (<c>onSearch</c>).</summary>
        internal void OnSearch(string payload)
        {
            try
            {
                if (!IsBuying) { Unexpected("EHS", payload); return; }
                bool found = payload == "K";
                LastSearchFound = found;
                if (found) Log("Objet trouvé dans cet hôtel de vente ; les lots suivent.");
                else LogError(ExchangeRegistry.Text("ITEM_NOT_IN_BIGSTORE", "L'objet recherché n'est pas en vente dans cet hôtel des ventes."));
                Notify();
            }
            catch (Exception error) when (!(error is OutOfMemoryException)) { Account?.Logger?.LogError(Reference, "Paquet EHS non appliqué : " + error.Message); }
        }

        protected override void ResetState()
        {
            lock (sync) { templates = new List<int>(); lines = new List<AuctionLine>(); averagePrices.Clear(); sales = new List<AuctionSale>(); }
            Info = new AuctionHouseInfo();
            CurrentCategory = -1;
            CurrentTemplate = -1;
            IsBuyPending = false;
            IsSellPending = false;
            IsOpeningPending = false;
            SalesReceived = false;
            LastSearchFound = null;
        }

        protected override void OnClearing() { IsOpeningPending = false; }

        /// <summary>Nom d'une catégorie (<c>I.t</c> des textes du client), sinon « Catégorie n° X ».</summary>
        public static string CategoryName(int category)
        {
            string key = category.ToString(CultureInfo.InvariantCulture);
            try
            {
                string name = LangData.Item.TypeName(category);
                if (!string.IsNullOrEmpty(name) && name != key) return name;
            }
            catch (Exception) { /* Textes illisibles : repli. */ }
            return "Catégorie n° " + key;
        }

        /// <summary>Champs <c>ligne;effets;prix x1;prix x10;prix x100</c> à partir de <paramref name="offset"/> ; prix vide = aucun lot.</summary>
        private static AuctionLine ParseLine(int template, string[] fields, int offset)
        {
            if (fields.Length < offset + 2 || !TryId(fields[offset], out uint id)) return null;
            var line = new AuctionLine { Id = id, TemplateId = template, Effects = fields[offset + 1], Name = InventoryObjects.DisplayName(template) };
            for (int index = 0; index < QuantityIndexes; index++)
            {
                int field = offset + 2 + index;
                if (field >= fields.Length || fields[field].Length == 0) continue;
                if (!TryLong(fields[field], out long price)) return null;
                line.Prices[index] = price > 0 ? price : (long?)null;
            }
            return line;
        }
    }
}
