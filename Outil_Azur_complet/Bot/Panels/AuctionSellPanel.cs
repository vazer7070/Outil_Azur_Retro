using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Controls;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Exchanges;
using Tool_BotProtocol.Game.Perso.Inventory;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>
    /// Hôtel de vente, mode vente (interface <c>BigStoreSell</c> du client 1.34, <c>ECK10</c> puis <c>EL</c>) : sac du personnage en haut (objets
    /// refusés par l'hôtel grisés, cachés par le filtre <c>BIGSTORE_FILTER</c>), lots en vente en bas (<c>SHOP_STOCK</c>). Un objet du sac choisi
    /// demande son prix moyen (<c>EHP</c>) et propose les lots x1 / x10 / x100 possédés ; **Mettre en vente** calcule la taxe, pose
    /// <c>DO_U_SELL_ITEM_BIGSTORE</c> puis envoie <c>EMO+&lt;objet&gt;|&lt;indice&gt;|&lt;prix&gt;</c> ; **Retirer** envoie <c>EMO-&lt;ligne&gt;|&lt;quantité&gt;</c>.
    /// La liste ne change qu'aux <c>EL</c> et <c>EmK-</c> du serveur. **Mode achat** envoie <c>ER11|-1</c> ; **Fermer**, × et Échap envoient <c>EV</c>.
    /// </summary>
    public sealed class AuctionSellPanel : GamePanel
    {
        private const string IdleText = "Aucun hôtel de vente ouvert en mode vente. Parlez à un PNJ d'hôtel de vente (« Vendre », ER10|pnj) : le volet s'ouvre lorsque le serveur l'annonce (ECK10 puis EL).";
        private Label heading, summary, bagTitle, stockTitle, taxLabel, averageLabel, status;
        private ListView bagList, stockList;
        private CheckBox filterCheck;
        private ComboBox quantityBox;
        private NumericUpDown priceBox;
        private Control sellButton, removeButton, switchButton, leaveButton;
        private ItemTooltipPopup itemTips;
        private InventoryClass inventory;
        private AuctionHouse bound;
        private bool wasOpen, refreshing;
        private Form sellDialog;

        public override string Title => AuctionUi.Text("BIGSTORE", "Hôtel de vente") + " · " + AuctionUi.Text("BIGSTORE_MODE_SELL", "Mode vente");
        public override Image Icon => ClientAssets.Icon("kamas", 24);
        public override bool IsModal => true;
        public override bool IsServerWindowOpen => Game?.Interactions?.Auction?.IsSelling == true;
        /// <summary>Boîte <c>DO_U_SELL_ITEM_BIGSTORE</c> ouverte, ou <c>null</c> (diagnostic et tests).</summary>
        public Form SellDialog => sellDialog;

        protected override Control CreateView()
        {
            var page = Page();
            heading = MakeLabel(Title, 11, true); heading.Dock = DockStyle.Top; heading.Height = 26; heading.AutoEllipsis = true;
            summary = MakeLabel(string.Empty, 8); summary.Dock = DockStyle.Top; summary.Height = 34; summary.ForeColor = BotUi.Muted;

            var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
            split.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); split.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            var bagArea = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 6) };
            bagTitle = MakeLabel("Votre sac", 9, true); bagTitle.Dock = DockStyle.Top; bagTitle.Height = 22;
            bagList = MakeList(9, "Objet", "Qté", "Niv."); bagList.Name = "auction-bag";
            bagList.Columns[0].Width = 220; bagList.Columns[1].Width = 50; bagList.Columns[2].Width = 50;
            bagList.SelectedIndexChanged += async (s, e) => await BagChosen();
            filterCheck = new CheckBox { Name = "auction-filter", Text = AuctionUi.Text("BIGSTORE_FILTER", "Filtrer pour cet HDV"), AutoSize = true, Dock = DockStyle.Bottom,
                Font = BotFonts.Get(8), ForeColor = BotUi.Muted, BackColor = BotUi.Paper, Checked = false, Padding = new Padding(2, 2, 0, 0) };
            filterCheck.CheckedChanged += (s, e) => { if (!refreshing) RefreshView(); };
            bagArea.Controls.Add(bagList); bagArea.Controls.Add(filterCheck); bagArea.Controls.Add(bagTitle);

            var stockArea = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            stockTitle = MakeLabel(AuctionUi.Text("SHOP_STOCK", "Stock en magasin"), 9, true); stockTitle.Dock = DockStyle.Top; stockTitle.Height = 22;
            stockList = MakeList(9, "Lot", "Qté", "Prix", AuctionUi.Text("BIGSTORE_TIME", "Temps restant")); stockList.Name = "auction-stock";
            stockList.Columns[0].Width = 150; stockList.Columns[1].Width = 45; stockList.Columns[2].Width = 85; stockList.Columns[3].Width = 70;
            stockList.SelectedIndexChanged += (s, e) => UpdateButtons();
            removeButton = MakeButton(AuctionUi.Text("REMOVE", "Retirer"), async (s, e) => await RemoveSelected(), false, 100);
            stockArea.Controls.Add(stockList); stockArea.Controls.Add(stockTitle); stockArea.Controls.Add(BotUi.Actions(removeButton));
            split.Controls.Add(bagArea, 0, 0); split.Controls.Add(stockArea, 0, 1);

            quantityBox = AuctionUi.Combo("auction-sell-quantity", 90);
            quantityBox.SelectedIndexChanged += (s, e) => UpdateButtons();
            priceBox = AuctionUi.Price("auction-price");
            priceBox.ValueChanged += (s, e) => UpdateTax();
            taxLabel = AuctionUi.Caption(string.Empty);
            sellButton = MakeButton(AuctionUi.Text("PUT_ON_SELL", "Mettre en vente"), async (s, e) => await AskSell(), true, 130);
            var sellBar = BotUi.Actions(AuctionUi.Caption(AuctionUi.Text("SET_QUANTITY", "Lot (quantité)")), quantityBox, AuctionUi.Caption(AuctionUi.Text("SET_PRICE", "Prix du lot")), priceBox, taxLabel, sellButton);
            averageLabel = MakeLabel(string.Empty, 8); averageLabel.Dock = DockStyle.Bottom; averageLabel.Height = 20; averageLabel.ForeColor = BotUi.Muted;
            status = MakeStatus(IdleText);
            switchButton = MakeButton(AuctionUi.Text("BIGSTORE_MODE_BUY", "Mode achat"), async (s, e) => await ReportAsync(() => Game.Interactions.Auction.SwitchModeAsync()), false, 120);
            leaveButton = MakeButton(AuctionUi.Text("CLOSE", "Fermer"), async (s, e) => await Leave(), false, 90);

            page.Controls.Add(split); page.Controls.Add(summary); page.Controls.Add(heading);
            page.Controls.Add(averageLabel); page.Controls.Add(sellBar); page.Controls.Add(status); page.Controls.Add(BotUi.Actions(switchButton, leaveButton));
            itemTips = new ItemTooltipPopup();
            AuctionUi.Hover(bagList, itemTips, () => IsDisposed, row => "sac:" + row.Tag, row => ItemSheet.From(Game?.character?.Inventory?.GetByInventoryId((uint)row.Tag)));
            AuctionUi.Hover(stockList, itemTips, () => IsDisposed, row => "lot:" + row.Tag, row => SaleSheet((uint)row.Tag));
            return page;
        }

        private ItemSheet SaleSheet(uint lineId)
        {
            AuctionSale sale = Game?.Interactions?.Auction?.Sales.FirstOrDefault(entry => entry.LineId == lineId);
            return sale == null ? null : ItemSheet.FromTemplate(sale.TemplateId, sale.Effects, sale.Quantity, (int?)Math.Min(int.MaxValue, sale.Price));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { CloseSellDialog(); itemTips?.Dispose(); itemTips = null; }
            base.Dispose(disposing);
        }

        protected override void OnBind(GameClass game)
        {
            bound = game.Interactions?.Auction;
            if (bound != null) bound.Changed += OnServerChanged;
            inventory = game.character?.Inventory;
            if (inventory != null) inventory.RefreshInventory += OnInventoryChanged;
            wasOpen = false;
        }

        protected override void OnUnbind(GameClass game)
        {
            if (bound != null) bound.Changed -= OnServerChanged;
            bound = null;
            if (inventory != null) inventory.RefreshInventory -= OnInventoryChanged;
            inventory = null;
            wasOpen = false;
            CloseSellDialog();
        }

        private bool Post(Action action) => ExchangePanel.PostToForm(Host, () => { if (!IsDisposed) action(); });

        private void OnServerChanged() { if (!Post(ApplyServerChange)) OnUi(ApplyServerChange); }

        private void ApplyServerChange()
        {
            bool open = IsServerWindowOpen;
            if (open && !wasOpen) RequestShow();
            else if (!open && wasOpen) { itemTips?.Hide(); CloseSellDialog(); RaiseClosed(); }
            wasOpen = open;
            RefreshView();
        }

        private void OnInventoryChanged(bool changed) { if (changed && !Post(RefreshView)) OnUi(RefreshView); }

        public override void RefreshView()
        {
            if (Game == null || bagList == null) return;
            AuctionHouse auction = Game.Interactions.Auction;
            AuctionHouseInfo info = auction.Info;
            refreshing = true;
            try
            {
                heading.Text = Title + (auction.IsSelling ? " (" + AuctionUi.Text("BIGSTORE_MAX_LEVEL", "Niveau maximum") + " : " + info.MaxLevel + ")" : string.Empty);
                summary.Text = auction.IsSelling ? AuctionUi.Summary(info, Game.character?.Kamas ?? 0) : string.Empty;
                bagTitle.Text = string.IsNullOrEmpty(Game.character?.Name) ? "Votre sac" : Game.character.Name;
                uint selectedBag = bagList.SelectedItems.Count == 0 ? 0u : (uint)bagList.SelectedItems[0].Tag;
                bagList.BeginUpdate(); bagList.Items.Clear();
                if (auction.IsSelling)
                    foreach (InventoryObjects item in Game.character.Inventory.Objets.Where(entry => !entry.IsEquipped()).OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase))
                    {
                        bool sellable = auction.CanSell(item, out string reason);
                        if (!sellable && filterCheck.Checked) continue;
                        var row = bagList.Items.Add(item.Name);
                        row.SubItems.Add(item.Qua.ToString(CultureInfo.InvariantCulture));
                        int level = AuctionHouse.LevelOf(item);
                        row.SubItems.Add(level > 0 ? level.ToString(CultureInfo.InvariantCulture) : string.Empty);
                        row.Tag = item.Inventory_ID;
                        if (!sellable) { row.ForeColor = BotUi.Muted; row.ToolTipText = reason; }
                        if (item.Inventory_ID == selectedBag) row.Selected = true;
                    }
                bagList.EndUpdate();

                uint selectedSale = stockList.SelectedItems.Count == 0 ? 0u : (uint)stockList.SelectedItems[0].Tag;
                stockList.BeginUpdate(); stockList.Items.Clear();
                foreach (AuctionSale sale in auction.Sales)
                {
                    var row = stockList.Items.Add(sale.Name);
                    row.SubItems.Add(sale.Quantity.ToString(CultureInfo.InvariantCulture));
                    row.SubItems.Add(sale.Price.ToString("N0", CultureInfo.CurrentCulture));
                    row.SubItems.Add(sale.RemainingHours + " h");
                    row.Tag = sale.LineId;
                    if (sale.LineId == selectedSale) row.Selected = true;
                }
                stockList.EndUpdate();
                stockTitle.Text = AuctionUi.Text("SHOP_STOCK", "Stock en magasin") + (auction.IsSelling ? " (" + stockList.Items.Count + "/" + Math.Min(info.MaxItems, AuctionHouse.MaxSalesPerAccount) + ")" : string.Empty);
                status.Text = auction.IsSelling ? (auction.LastMessage.Length > 0 ? auction.LastMessage : "Choisissez un objet du sac à mettre en vente.")
                    : IdleText + (auction.LastMessage.Length > 0 ? "\n" + auction.LastMessage : string.Empty);
            }
            finally { refreshing = false; }
            FillQuantities();
            UpdateTax();
            UpdateButtons();
        }

        /// <summary>Lots possibles pour l'objet sélectionné (<c>BigStoreSell.populateComboBox</c>) ; vide hors sélection.</summary>
        private void FillQuantities()
        {
            if (quantityBox == null || Game == null) return;
            AuctionHouse auction = Game.Interactions.Auction;
            InventoryObjects item = InventoryPanel.SelectedItem(Game, bagList);
            int previous = quantityBox.SelectedItem is AuctionQuantityChoice chosen ? chosen.Index : 0;
            refreshing = true;
            try
            {
                quantityBox.Items.Clear();
                if (item != null && auction.CanSell(item, out string _))
                    foreach (int index in auction.QuantityIndexesFor(item.Qua)) quantityBox.Items.Add(new AuctionQuantityChoice(index, auction.Info.QuantityOf(index), null));
                for (int i = 0; i < quantityBox.Items.Count; i++) if (((AuctionQuantityChoice)quantityBox.Items[i]).Index == previous) quantityBox.SelectedIndex = i;
                if (quantityBox.SelectedIndex < 0 && quantityBox.Items.Count > 0) quantityBox.SelectedIndex = 0;
                averageLabel.Text = item != null && auction.AveragePrices.TryGetValue(item.ID, out long average)
                    ? AuctionUi.Text("BIGSTORE_MIDDLEPRICE", "Prix moyen constaté dans cet hôtel : " + average + " kamas/u.", average.ToString(CultureInfo.InvariantCulture)) : string.Empty;
            }
            finally { refreshing = false; }
        }

        /// <summary><c>BIGSTORE_TAX</c> : taxe telle que StarLoco la débite pour le prix saisi.</summary>
        private void UpdateTax()
        {
            if (taxLabel == null || Game == null) return;
            AuctionHouse auction = Game.Interactions.Auction;
            taxLabel.Text = auction.IsSelling ? AuctionUi.Text("BIGSTORE_TAX", "Taxe de mise en vente") + " : " + AuctionHouse.TaxFor((long)priceBox.Value, auction.Info.Tax) : string.Empty;
        }

        private void UpdateButtons()
        {
            if (Game == null || sellButton == null) return;
            AuctionHouse auction = Game.Interactions.Auction;
            bool connected = Connected, selling = auction.IsSelling;
            InventoryObjects item = InventoryPanel.SelectedItem(Game, bagList);
            bool sellable = item != null && auction.CanSell(item, out string _);
            sellButton.Enabled = connected && selling && !auction.IsSellPending && sellable && quantityBox.SelectedItem is AuctionQuantityChoice;
            quantityBox.Enabled = priceBox.Enabled = connected && selling && sellable;
            removeButton.Enabled = connected && selling && stockList.SelectedItems.Count == 1;
            switchButton.Enabled = connected && selling && !auction.IsOpeningPending;
            leaveButton.Enabled = connected && auction.IsOpen;
            filterCheck.Enabled = selling;
        }

        /// <summary>Comme <c>BigStoreSell.selectedItem</c> : raison du refus affichée, lots possibles, prix moyen demandé (<c>EHP</c>).</summary>
        private async Task BagChosen()
        {
            if (refreshing || Game == null || bagList.SelectedItems.Count != 1) return;
            AuctionHouse auction = Game.Interactions.Auction;
            InventoryObjects item = InventoryPanel.SelectedItem(Game, bagList);
            FillQuantities();
            UpdateButtons();
            if (item == null || !auction.IsSelling) return;
            if (!auction.CanSell(item, out string reason)) { status.Text = reason; return; }
            await auction.RequestAveragePriceAsync(item.ID);
        }

        /// <summary><c>DO_U_SELL_ITEM_BIGSTORE</c> (Oui / Non) après les contrôles locaux ; « Oui » envoie <c>EMO+</c>.</summary>
        private async Task AskSell()
        {
            try
            {
                if (Game == null || !(quantityBox.SelectedItem is AuctionQuantityChoice choice)) return;
                AuctionHouse auction = Game.Interactions.Auction;
                InventoryObjects item = InventoryPanel.SelectedItem(Game, bagList);
                if (!auction.CanSell(item, out string reason)) { Feedback(reason); return; }
                long price = (long)priceBox.Value;
                if (price <= 0) { Feedback(AuctionUi.Text("ERROR_INVALID_PRICE", "Le prix est invalide.")); return; }
                long tax = AuctionHouse.TaxFor(price, auction.Info.Tax);
                if (tax > (Game.character?.Kamas ?? 0)) { Feedback(AuctionUi.Text("NOT_ENOUGH_RICH", "Tu n'as pas assez de kamas pour réaliser cette action.") + " (taxe " + tax + ")"); return; }
                CloseSellDialog();
                string lot = "x" + choice.Quantity + " " + item.Name;
                Form[] before = BotDialogs.OpenDialogs.ToArray();
                Task<BotDialogResult> question = BotDialogs.AskYesNoAsync(Host, AuctionUi.Text("BIGSTORE", "Hôtel de vente"),
                    AuctionUi.Text("DO_U_SELL_ITEM_BIGSTORE", "Confirmez-vous la vente de '" + lot + "' au prix de " + price + " kamas ?", lot, price.ToString(CultureInfo.InvariantCulture))
                    + "\n" + AuctionUi.Text("BIGSTORE_TAX", "Taxe de mise en vente") + " : " + tax + " kamas.");
                sellDialog = BotDialogs.OpenDialogs.Except(before).LastOrDefault();
                BotDialogResult answer = await question;
                sellDialog = null;
                if (answer != BotDialogResult.Yes) { Feedback("Mise en vente abandonnée : rien n'a été envoyé."); return; }
                uint inventoryId = item.Inventory_ID;
                if (IsDisposed) await auction.SellAsync(inventoryId, choice.Index, price); else await ReportAsync(() => auction.SellAsync(inventoryId, choice.Index, price));
            }
            catch (Exception error) { Account?.Logger?.LogException(AuctionUi.Reference, error); }
        }

        private Task RemoveSelected()
        {
            if (Game == null || stockList.SelectedItems.Count != 1) return Task.CompletedTask;
            uint lineId = (uint)stockList.SelectedItems[0].Tag;
            return ReportAsync(() => Game.Interactions.Auction.RemoveSaleAsync(lineId));
        }

        private void CloseSellDialog()
        {
            Form dialog = sellDialog;
            sellDialog = null;
            if (dialog != null && !dialog.IsDisposed) dialog.Close();
        }

        protected internal override bool OnUserClose()
        {
            if (!IsServerWindowOpen) return true;
            _ = Leave();
            return false;
        }

        private Task Leave() => ReportAsync(() => Game.Interactions.Auction.LeaveAsync());
    }
}
