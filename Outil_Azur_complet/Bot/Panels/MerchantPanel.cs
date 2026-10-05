using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Controls;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Exchanges;
using Tool_BotProtocol.Game.Habitat;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Perso.Inventory;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>
    /// Magasin d'un marchand hors ligne (<c>PlayerShop</c> du client 1.34, <c>ECK4</c>) et organisation de son propre magasin
    /// (<c>PlayerShopModifier</c>, <c>ECK6</c>) : lots de <c>EL</c> en haut, objets du sac en bas quand on organise. Acheter envoie
    /// <c>EB&lt;lot&gt;|&lt;quantité&gt;</c>, Mettre en vente <c>EMO+&lt;objet&gt;|&lt;quantité&gt;|&lt;prix&gt;</c>, Retirer <c>EMO-&lt;lot&gt;|&lt;quantité&gt;</c>,
    /// Modifier le prix <c>EMO+&lt;lot&gt;|&lt;quantité du lot&gt;|&lt;prix&gt;</c>, Fermer (ou ×/Échap) <c>EV</c>. « Mode marchand » envoie <c>Eq</c> ;
    /// à la réponse <c>Eq1</c>, la boîte <c>DO_U_OFFLINEEXCHANGE</c> (Oui / Non) précède <c>EQ</c>, avec l'avertissement que StarLoco
    /// déconnecte alors le client. Hors magasin, le volet (menu global) propose <c>ER6</c> et <c>Eq</c>.
    /// </summary>
    public sealed class MerchantPanel : GamePanel
    {
        private const string Reference = "MARCHAND";
        private const string NoShopText = "Clic sur un marchand de la carte (« Acheter », ER4|marchand|cellule) ou clic droit sur votre personnage (« Organiser mon magasin », ER6).\nLe magasin s'ouvre lorsque le serveur l'annonce (ECK4 ou ECK6 puis EL).";
        private Label merchantHeading, merchantTotal, merchantStatus, lotsTitle;
        private ListView merchantList, merchantBag;
        private NumericUpDown merchantQuantity, merchantPrice, bagQuantity, bagPrice;
        private Control merchantBuy, merchantRemove, merchantReprice, merchantAdd, merchantMode, merchantOrganize, merchantLeave;
        private Control lotsActions, bagActions;
        private Panel bagArea;
        private TableLayoutPanel split;
        private ItemTooltipPopup itemTips;
        private InventoryClass inventory;
        private MerchantExchange bound;
        private bool wasOpen;
        private MerchantTax taxShown;
        private Form taxDialog;

        public override string Title => "Magasin";
        public override Image Icon => ClientAssets.Icon("kamas", 24);
        public override bool IsModal => true;
        public override bool IsServerWindowOpen => Game?.Interactions?.Merchant?.IsOpen == true;
        /// <summary>Boîte <c>DO_U_OFFLINEEXCHANGE</c> ouverte, ou <c>null</c> (diagnostic et tests).</summary>
        public Form TaxDialog => taxDialog;

        protected override Control CreateView()
        {
            var page = Page();
            merchantHeading = MakeLabel("Magasin", 11, true); merchantHeading.Dock = DockStyle.Top; merchantHeading.Height = 26;
            split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
            split.RowStyles.Add(new RowStyle(SizeType.Percent, 55)); split.RowStyles.Add(new RowStyle(SizeType.Percent, 45));

            var lotsArea = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 6) };
            lotsTitle = MakeLabel("Lots en vente", 9, true); lotsTitle.Dock = DockStyle.Top; lotsTitle.Height = 22;
            merchantList = MakeList(9, "Lot", "Qté", "Prix");
            merchantList.Columns[0].Width = 170; merchantList.Columns[1].Width = 50; merchantList.Columns[2].Width = 100;
            merchantList.SelectedIndexChanged += (s, e) => UpdateButtons();
            merchantQuantity = InventoryPanel.Quantity(); merchantQuantity.Maximum = MerchantExchange.MaxQuantity;
            merchantPrice = Price();
            merchantBuy = MakeButton(HouseTexts.Text("BUY", "Acheter"), async (s, e) => await BuySelected(), true, 100);
            merchantRemove = MakeButton("Retirer", async (s, e) => await RemoveSelected(), false, 90);
            merchantReprice = MakeButton("Modifier le prix", async (s, e) => await RepriceSelected(), true, 130);
            lotsActions = BotUi.Actions(merchantBuy, merchantRemove, InventoryPanel.QuantityLabel(), merchantQuantity, merchantReprice, PriceLabel(), merchantPrice);
            lotsArea.Controls.Add(merchantList); lotsArea.Controls.Add(lotsTitle); lotsArea.Controls.Add(lotsActions);

            bagArea = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            var bagTitle = MakeLabel("Vos objets à mettre en vente (sac)", 9, true); bagTitle.Dock = DockStyle.Top; bagTitle.Height = 22;
            merchantBag = MakeList(9, "Objet", "Qté");
            merchantBag.Columns[0].Width = 250; merchantBag.Columns[1].Width = 75;
            merchantBag.SelectedIndexChanged += (s, e) => UpdateButtons();
            bagQuantity = InventoryPanel.Quantity();
            bagPrice = Price();
            merchantAdd = MakeButton("Mettre en vente", async (s, e) => await AddSelected(), true, 130);
            bagActions = BotUi.Actions(merchantAdd, InventoryPanel.QuantityLabel(), bagQuantity, PriceLabel(), bagPrice);
            bagArea.Controls.Add(merchantBag); bagArea.Controls.Add(bagTitle); bagArea.Controls.Add(bagActions);

            split.Controls.Add(lotsArea, 0, 0); split.Controls.Add(bagArea, 0, 1);
            merchantTotal = MakeLabel(string.Empty, 9); merchantTotal.Dock = DockStyle.Bottom; merchantTotal.Height = 22; merchantTotal.ForeColor = BotUi.Muted;
            merchantStatus = MakeStatus(NoShopText);
            merchantOrganize = MakeButton(HouseTexts.Text("ORGANIZE_SHOP", "Organiser mon magasin"), async (s, e) => await ReportAsync(() => Game.Interactions.Merchant.OrganizeAsync()), false, 160);
            merchantMode = MakeButton("Mode marchand", async (s, e) => await AskMerchantModeAsync(), false, 130);
            merchantLeave = MakeButton(HouseTexts.Text("CLOSE", "Fermer"), async (s, e) => await Leave(), false, 90);
            page.Controls.Add(split); page.Controls.Add(merchantHeading); page.Controls.Add(merchantTotal); page.Controls.Add(merchantStatus);
            page.Controls.Add(BotUi.Actions(merchantOrganize, merchantMode, merchantLeave));
            itemTips = new ItemTooltipPopup();
            Hover(merchantList, row => "lot:" + row.Tag, row => LotSheet((uint)row.Tag));
            Hover(merchantBag, row => "sac:" + row.Tag, row => ItemSheet.From(Game?.character?.Inventory?.GetByInventoryId((uint)row.Tag)));
            return page;
        }

        private static NumericUpDown Price() => new NumericUpDown { Minimum = 1, Maximum = int.MaxValue, Value = 1, Width = 110, Height = 34,
            ThousandsSeparator = true, Font = BotFonts.Get(9), BackColor = BotUi.PaperLight, ForeColor = BotUi.Ink, BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(2, 7, 6, 0), AccessibleName = "Prix" };
        private static Label PriceLabel()
        {
            Label label = BotUi.Label(HouseTexts.Text("PRICE", "Prix").Trim(), 9);
            label.AutoSize = true; label.Margin = new Padding(6, 12, 0, 0); label.ForeColor = BotUi.Muted;
            return label;
        }

        /// <summary>Fiche d'un lot de <c>EL</c> : modèle, effets transmis, quantité et prix unitaire.</summary>
        private ItemSheet LotSheet(uint lotId)
        {
            MerchantItem lot = Game?.Interactions?.Merchant?.Items.FirstOrDefault(entry => entry.Id == lotId);
            return lot == null ? null : ItemSheet.FromTemplate(lot.TemplateId, lot.Effects, lot.Quantity, (int?)Math.Min(int.MaxValue, lot.Price));
        }

        private void Hover(ListView list, Func<ListViewItem, object> key, Func<ListViewItem, ItemSheet> build)
        {
            list.MouseMove += (s, e) =>
            {
                if (itemTips == null || IsDisposed) return;
                ListViewItem row = list.GetItemAt(e.X, e.Y);
                if (row == null || row.Tag == null) { itemTips.Hide(); return; }
                itemTips.Request(list, list.PointToScreen(e.Location), key(row), () => build(row));
            };
            list.MouseLeave += (s, e) => itemTips?.Hide();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { CloseTaxDialog(); itemTips?.Dispose(); itemTips = null; }
            base.Dispose(disposing);
        }

        protected override void OnBind(GameClass game)
        {
            bound = game.Interactions?.Merchant;
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
            CloseTaxDialog();
        }

        /// <summary>Passe par la fenêtre de jeu : le tiroir n'a pas de poignée tant qu'il n'a jamais été affiché (voir <see cref="ExchangePanel.PostToForm"/>).</summary>
        private bool Post(Action action) => ExchangePanel.PostToForm(Host, () => { if (!IsDisposed) action(); });

        private void OnServerChanged()
        {
            if (!Post(ApplyServerChange)) OnUi(ApplyServerChange);
        }

        private void ApplyServerChange()
        {
            bool open = IsServerWindowOpen;
            if (open && !wasOpen) RequestShow();
            else if (!open && wasOpen) { itemTips?.Hide(); RaiseClosed(); }
            wasOpen = open;
            MerchantTax tax = bound?.PendingTax;
            if (tax != null && !ReferenceEquals(taxShown, tax)) AskTax(tax);
            else if (tax == null && taxShown != null) CloseTaxDialog();
            RefreshView();
        }

        private void OnInventoryChanged(bool changed) { if (changed) OnUi(RefreshView); }

        /// <summary><c>Eq</c> : la question n'est posée qu'à la réponse <c>Eq1</c> du serveur.</summary>
        public async Task<string> AskMerchantModeAsync()
        {
            MerchantExchange shop = Game?.Interactions?.Merchant;
            if (shop == null) return "La session n’est plus disponible.";
            InteractionResult result = await shop.AskMerchantModeAsync();
            Feedback(result?.Message);
            if (result != null && !result.Sent) Account?.Logger?.LogError(Reference, result.Message);
            return result?.Message;
        }

        /// <summary><c>DO_U_OFFLINEEXCHANGE</c> (Oui / Non) : « Oui » envoie <c>EQ</c>, « Non » n'envoie rien.</summary>
        private async void AskTax(MerchantTax tax)
        {
            try
            {
                CloseTaxDialog();
                MerchantExchange shop = bound;
                if (shop == null || IsDisposed) return;
                string rate = tax.Rate.ToString("0.##", CultureInfo.InvariantCulture);
                string message = HouseTexts.Text("DO_U_OFFLINEEXCHANGE",
                        "Pour passer en mode marchand, vous devez payer une taxe s'élevant à " + rate + "% de la somme des prix de vos objets en vente, soit " + tax.Tax + " kamas.\nUne fois le mode marchand activé, vous serez déconnecté du jeu.",
                        tax.Type.ToString(CultureInfo.InvariantCulture), rate, tax.Tax.ToString(CultureInfo.InvariantCulture))
                    + "\n\nStarLoco déconnecte le client dès la réception de EQ : le bot perd la session et le personnage reste marchand sur la carte.";
                Form[] before = BotDialogs.OpenDialogs.ToArray();
                Task<BotDialogResult> question = BotDialogs.AskYesNoAsync(Host, HouseTexts.Text("MERCHANT_MODE", "Passer en mode 'marchand'"), message);
                taxShown = tax;
                taxDialog = BotDialogs.OpenDialogs.Except(before).LastOrDefault();
                BotDialogResult answer = await question;
                if (ReferenceEquals(taxShown, tax)) { taxShown = null; taxDialog = null; }
                if (!ReferenceEquals(shop.PendingTax, tax)) return;
                if (answer == BotDialogResult.Yes) { if (IsDisposed) await shop.ConfirmMerchantModeAsync(); else await ReportAsync(shop.ConfirmMerchantModeAsync); }
                else { shop.DeclineMerchantMode(); Feedback("Mode marchand abandonné : rien n'a été envoyé."); }
            }
            catch (Exception error) { Account?.Logger?.LogException(Reference, error); }
        }

        private void CloseTaxDialog()
        {
            Form dialog = taxDialog;
            taxDialog = null; taxShown = null;
            if (dialog != null && !dialog.IsDisposed) dialog.Close();
        }

        public override void RefreshView()
        {
            if (Game == null || merchantList == null) return;
            MerchantExchange shop = Game.Interactions.Merchant;
            bool organizing = shop.IsOrganizing, buying = shop.IsBuying;
            merchantHeading.Text = buying ? "Magasin de " + shop.MerchantName : organizing ? HouseTexts.Text("ORGANIZE_SHOP", "Organiser mon magasin") : "Magasin";
            lotsTitle.Text = buying ? "Lots en vente" : "Vos lots en vente";
            uint selectedLot = merchantList.SelectedItems.Count == 0 ? 0u : (uint)merchantList.SelectedItems[0].Tag;
            merchantList.BeginUpdate(); merchantList.Items.Clear();
            foreach (MerchantItem lot in shop.Items)
            {
                var row = merchantList.Items.Add(lot.Name);
                row.SubItems.Add(lot.Quantity.ToString(CultureInfo.InvariantCulture)); row.SubItems.Add(lot.Price.ToString("N0", CultureInfo.CurrentCulture) + " kamas"); row.Tag = lot.Id;
                if (lot.Id == selectedLot) row.Selected = true;
            }
            merchantList.EndUpdate();
            uint selectedBag = merchantBag.SelectedItems.Count == 0 ? 0u : (uint)merchantBag.SelectedItems[0].Tag;
            merchantBag.BeginUpdate(); merchantBag.Items.Clear();
            if (organizing)
                foreach (InventoryObjects item in Game.character.Inventory.Objets.Where(entry => !entry.IsEquipped()).OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    var row = merchantBag.Items.Add(item.Name); row.SubItems.Add(item.Qua.ToString(CultureInfo.InvariantCulture)); row.Tag = item.Inventory_ID;
                    if (item.Inventory_ID == selectedBag) row.Selected = true;
                }
            merchantBag.EndUpdate();
            bagArea.Visible = organizing;
            split.RowStyles[0].SizeType = SizeType.Percent; split.RowStyles[0].Height = organizing ? 55 : 100;
            split.RowStyles[1].Height = organizing ? 45 : 0;
            merchantBuy.Visible = !organizing; merchantQuantity.Visible = true;
            merchantRemove.Visible = merchantReprice.Visible = merchantPrice.Visible = organizing;
            foreach (Control label in lotsActions.Controls.OfType<Label>()) label.Visible = true;
            merchantOrganize.Visible = !shop.IsOpen;
            merchantTotal.Text = shop.IsOpen ? shop.Items.Count + " lot(s), " + shop.TotalPrice.ToString("N0", CultureInfo.CurrentCulture) + " kamas au total"
                + (organizing ? " · taxe estimée " + (shop.TotalPrice / 1000).ToString("N0", CultureInfo.CurrentCulture) + " kamas (prix ÷ 1 000 chez StarLoco)" : string.Empty) : string.Empty;
            string tax = shop.PendingTax != null ? " Taxe proposée : " + shop.PendingTax.Tax + " kamas." : shop.MerchantModeRequested ? " EQ envoyé : StarLoco déconnecte le client." : string.Empty;
            merchantStatus.Text = shop.IsOpen ? (shop.LastMessage.Length > 0 ? shop.LastMessage : "Magasin ouvert.") + tax
                : NoShopText + (shop.LastMessage.Length > 0 ? "\n" + shop.LastMessage : string.Empty) + tax;
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            if (Game == null || merchantBuy == null) return;
            MerchantExchange shop = Game.Interactions.Merchant;
            bool connected = Connected;
            uint lotId = merchantList.SelectedItems.Count == 1 ? (uint)merchantList.SelectedItems[0].Tag : 0u;
            MerchantItem lot = lotId == 0 ? null : shop.Items.FirstOrDefault(entry => entry.Id == lotId);
            if (lot != null) merchantQuantity.Maximum = Math.Max(1, lot.Quantity);
            merchantBuy.Enabled = connected && shop.IsBuying && !shop.IsPending && lot != null;
            merchantRemove.Enabled = merchantReprice.Enabled = connected && shop.IsOrganizing && lot != null;
            InventoryObjects bagItem = InventoryPanel.SelectedItem(Game, merchantBag);
            if (bagItem != null) bagQuantity.Maximum = Math.Max(1, bagItem.Qua);
            merchantAdd.Enabled = connected && shop.IsOrganizing && bagItem != null;
            merchantOrganize.Enabled = connected && !shop.IsOpen && shop.CanBeMerchant;
            merchantMode.Enabled = connected && shop.CanBeMerchant && shop.PendingTax == null && !shop.TaxRequested && !shop.MerchantModeRequested;
            merchantLeave.Enabled = connected && shop.IsOpen;
        }

        protected internal override bool OnUserClose()
        {
            if (!IsServerWindowOpen) return true;
            _ = Leave();
            return false;
        }

        private Task BuySelected()
        {
            if (merchantList.SelectedItems.Count != 1) return Task.CompletedTask;
            uint lot = (uint)merchantList.SelectedItems[0].Tag; int quantity = (int)merchantQuantity.Value;
            return ReportAsync(() => Game.Interactions.Merchant.BuyAsync(lot, quantity));
        }
        private Task RemoveSelected()
        {
            if (merchantList.SelectedItems.Count != 1) return Task.CompletedTask;
            uint lot = (uint)merchantList.SelectedItems[0].Tag; int quantity = (int)merchantQuantity.Value;
            return ReportAsync(() => Game.Interactions.Merchant.RemoveFromShopAsync(lot, quantity));
        }
        private Task RepriceSelected()
        {
            if (merchantList.SelectedItems.Count != 1) return Task.CompletedTask;
            uint lot = (uint)merchantList.SelectedItems[0].Tag; long price = (long)merchantPrice.Value;
            return ReportAsync(() => Game.Interactions.Merchant.ChangePriceAsync(lot, price));
        }
        private Task AddSelected()
        {
            InventoryObjects item = InventoryPanel.SelectedItem(Game, merchantBag);
            if (item == null) return Task.CompletedTask;
            int quantity = (int)bagQuantity.Value; long price = (long)bagPrice.Value;
            return ReportAsync(() => Game.Interactions.Merchant.AddToShopAsync(item.Inventory_ID, quantity, price));
        }
        private Task Leave() => ReportAsync(() => Game.Interactions.Merchant.LeaveAsync());
    }
}
