using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Controls;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Exchanges;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Perso.Inventory;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>Textes et contrôles partagés par les deux volets de l'hôtel de vente.</summary>
    internal static class AuctionUi
    {
        internal const string Reference = "HDV";

        internal static string Text(string key, string fallback, params string[] args) => ExchangeRegistry.Text(key, fallback, args);

        /// <summary>Liste déroulante aux couleurs du parchemin.</summary>
        internal static ComboBox Combo(string name, int width)
        {
            var box = new ComboBox { Name = name, Width = width, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
                Font = BotFonts.Get(9), BackColor = BotUi.PaperLight, ForeColor = BotUi.Ink, Margin = new Padding(0, 5, 7, 0) };
            return box;
        }

        internal static Label Caption(string text)
        {
            Label label = BotUi.Label(text, 9);
            label.AutoSize = true; label.Margin = new Padding(0, 11, 4, 0); label.ForeColor = BotUi.Muted;
            return label;
        }

        internal static NumericUpDown Price(string name) => new NumericUpDown { Name = name, Minimum = 1, Maximum = int.MaxValue, Value = 1, Width = 110, Height = 34,
            ThousandsSeparator = true, Font = BotFonts.Get(9), BackColor = BotUi.PaperLight, ForeColor = BotUi.Ink, BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 5, 7, 0), AccessibleName = "Prix" };

        internal static string Kamas(long value) => value.ToString("N0", CultureInfo.CurrentCulture) + " kamas";

        /// <summary>Ligne d'en-tête commune : catégories, taxe, lots par compte, durée et porte-monnaie.</summary>
        internal static string Summary(AuctionHouseInfo info, long kamas) =>
            info.Categories.Length + " catégorie(s) · " + Text("BIGSTORE_TAX", "Taxe de mise en vente") + " " + info.Tax.ToString("0.#", CultureInfo.CurrentCulture) + " % · "
            + Text("BIGSTORE_MAX_ITEM_PER_ACCOUNT", "Nombre d'objets maximum par compte") + " " + info.MaxItems + " · "
            + Text("BIGSTORE_MAX_SELL_TIME", "Temps de vente maximum par objet") + " " + info.SellTime + " h · "
            + Text("WALLET", "Porte monnaie") + " : " + Kamas(kamas);

        /// <summary>Survol d'une liste : fiche d'objet de la ligne, cachée hors des lignes.</summary>
        internal static void Hover(ListView list, ItemTooltipPopup tips, Func<bool> disposed, Func<ListViewItem, object> key, Func<ListViewItem, ItemSheet> build)
        {
            list.MouseMove += (s, e) =>
            {
                if (tips == null || disposed()) return;
                ListViewItem row = list.GetItemAt(e.X, e.Y);
                if (row == null || row.Tag == null) { tips.Hide(); return; }
                tips.Request(list, list.PointToScreen(e.Location), key(row), () => build(row));
            };
            list.MouseLeave += (s, e) => tips?.Hide();
        }
    }

    /// <summary>Choix de quantité d'un lot dans une liste déroulante : indice 1, 2 ou 3 et prix affiché.</summary>
    internal sealed class AuctionQuantityChoice
    {
        internal AuctionQuantityChoice(int index, int quantity, long? price) { Index = index; Quantity = quantity; Price = price; }
        internal int Index { get; }
        internal int Quantity { get; }
        internal long? Price { get; }
        public override string ToString() => "x" + Quantity + (Price.HasValue ? " : " + AuctionUi.Kamas(Price.Value) : string.Empty);
    }

    /// <summary>
    /// Hôtel de vente, mode achat (interface <c>BigStoreBuy</c> du client 1.34, <c>ECK11</c>) : catégorie (<c>EHT</c>), objets en vente de la catégorie
    /// (<c>EHL</c>), lignes de lots d'un objet avec un prix par quantité x1 / x10 / x100 (<c>EHl</c>, après le prix moyen <c>EHP</c>), recherche d'un objet
    /// par son nom (<c>EHS</c>). **Acheter** pose la question <c>DO_U_BUY_ITEM_BIGSTORE</c> puis envoie <c>EHB&lt;ligne&gt;|&lt;indice&gt;|&lt;prix&gt;</c> ;
    /// la liste ne change qu'aux <c>EHm</c> du serveur. **Mode vente** envoie <c>ER10|-1</c> comme le client ; **Fermer**, × et Échap envoient <c>EV</c>.
    /// </summary>
    public sealed class AuctionBuyPanel : GamePanel
    {
        private const string IdleText = "Aucun hôtel de vente ouvert. Parlez à un PNJ d'hôtel de vente (« Acheter », ER11|pnj) : le volet s'ouvre lorsque le serveur l'annonce (ECK11).";
        private Label heading, summary, templatesTitle, linesTitle, averageLabel, status;
        private ComboBox categoryBox, quantityBox;
        private TextBox searchBox;
        private ListView templateList, lineList;
        private Control searchButton, buyButton, switchButton, leaveButton;
        private ItemTooltipPopup itemTips;
        private AuctionHouse bound;
        private bool wasOpen, refreshing;
        private int[] shownCategories = new int[0];
        private Form buyDialog;

        public override string Title => AuctionUi.Text("BIGSTORE", "Hôtel de vente");
        public override Image Icon => ClientAssets.Icon("kamas", 24);
        public override bool IsModal => true;
        public override bool IsServerWindowOpen => Game?.Interactions?.Auction?.IsBuying == true;
        /// <summary>Boîte <c>DO_U_BUY_ITEM_BIGSTORE</c> ouverte, ou <c>null</c> (diagnostic et tests).</summary>
        public Form BuyDialog => buyDialog;

        protected override Control CreateView()
        {
            var page = Page();
            heading = MakeLabel(Title, 11, true); heading.Dock = DockStyle.Top; heading.Height = 26; heading.AutoEllipsis = true;
            summary = MakeLabel(string.Empty, 8); summary.Dock = DockStyle.Top; summary.Height = 34; summary.ForeColor = BotUi.Muted;

            categoryBox = AuctionUi.Combo("auction-category", 190);
            categoryBox.SelectedIndexChanged += async (s, e) => await CategoryChosen();
            searchBox = BotUi.Input("auction-search"); searchBox.Width = 150; searchBox.Margin = new Padding(8, 5, 4, 0);
            searchBox.KeyDown += async (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await Search(); } };
            searchButton = MakeButton(AuctionUi.Text("SEARCH", "Rechercher"), async (s, e) => await Search(), false, 110);
            var categoryBar = BotUi.Actions(AuctionUi.Caption(AuctionUi.Text("ITEM_TYPE", "Catégorie")), categoryBox, searchBox, searchButton);
            categoryBar.Dock = DockStyle.Top;

            var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
            split.RowStyles.Add(new RowStyle(SizeType.Percent, 45)); split.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
            var templatesArea = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 6) };
            templatesTitle = MakeLabel(AuctionUi.Text("BIGSTORE_ITEM_LIST", "Objets en vente"), 9, true); templatesTitle.Dock = DockStyle.Top; templatesTitle.Height = 22;
            templateList = MakeList(9, "Objet", "Niv."); templateList.Name = "auction-templates";
            templateList.Columns[0].Width = 250; templateList.Columns[1].Width = 60;
            templateList.SelectedIndexChanged += async (s, e) => await TemplateChosen();
            templatesArea.Controls.Add(templateList); templatesArea.Controls.Add(templatesTitle);

            var linesArea = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            linesTitle = MakeLabel(AuctionUi.Text("BIGSTORE_ITEM_SET_PRICES", "Prix des lots"), 9, true); linesTitle.Dock = DockStyle.Top; linesTitle.Height = 22;
            lineList = MakeList(9, "Lot", "x1", "x10", "x100"); lineList.Name = "auction-lines";
            lineList.Columns[0].Width = 140; lineList.Columns[1].Width = 70; lineList.Columns[2].Width = 70; lineList.Columns[3].Width = 70;
            lineList.SelectedIndexChanged += (s, e) => { FillQuantities(); UpdateButtons(); };
            averageLabel = MakeLabel(string.Empty, 8); averageLabel.Dock = DockStyle.Bottom; averageLabel.Height = 20; averageLabel.ForeColor = BotUi.Muted;
            linesArea.Controls.Add(lineList); linesArea.Controls.Add(averageLabel); linesArea.Controls.Add(linesTitle);
            split.Controls.Add(templatesArea, 0, 0); split.Controls.Add(linesArea, 0, 1);

            quantityBox = AuctionUi.Combo("auction-quantity", 170);
            quantityBox.SelectedIndexChanged += (s, e) => UpdateButtons();
            buyButton = MakeButton(AuctionUi.Text("BUY", "Acheter"), async (s, e) => await AskBuy(), true, 110);
            var buyBar = BotUi.Actions(AuctionUi.Caption(AuctionUi.Text("SET_QUANTITY", "Lot (quantité)")), quantityBox, buyButton);
            status = MakeStatus(IdleText);
            switchButton = MakeButton(AuctionUi.Text("BIGSTORE_MODE_SELL", "Mode vente"), async (s, e) => await ReportAsync(() => Game.Interactions.Auction.SwitchModeAsync()), false, 120);
            leaveButton = MakeButton(AuctionUi.Text("CLOSE", "Fermer"), async (s, e) => await Leave(), false, 90);

            page.Controls.Add(split); page.Controls.Add(categoryBar); page.Controls.Add(summary); page.Controls.Add(heading);
            page.Controls.Add(buyBar); page.Controls.Add(status); page.Controls.Add(BotUi.Actions(switchButton, leaveButton));
            itemTips = new ItemTooltipPopup();
            AuctionUi.Hover(templateList, itemTips, () => IsDisposed, row => "modèle:" + row.Tag, row => ItemSheet.FromTemplate((int)row.Tag, string.Empty, 1, null));
            AuctionUi.Hover(lineList, itemTips, () => IsDisposed, row => "ligne:" + row.Tag, row => LineSheet((uint)row.Tag));
            return page;
        }

        private ItemSheet LineSheet(uint lineId)
        {
            AuctionLine line = Game?.Interactions?.Auction?.Lines.FirstOrDefault(entry => entry.Id == lineId);
            if (line == null) return null;
            long? price = line.Prices.FirstOrDefault(value => value.HasValue);
            return ItemSheet.FromTemplate(line.TemplateId, line.Effects, 1, price.HasValue ? (int?)Math.Min(int.MaxValue, price.Value) : null);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { CloseBuyDialog(); itemTips?.Dispose(); itemTips = null; }
            base.Dispose(disposing);
        }

        protected override void OnBind(GameClass game)
        {
            bound = game.Interactions?.Auction;
            if (bound != null) bound.Changed += OnServerChanged;
            wasOpen = false;
        }

        protected override void OnUnbind(GameClass game)
        {
            if (bound != null) bound.Changed -= OnServerChanged;
            bound = null;
            wasOpen = false;
            CloseBuyDialog();
        }

        /// <summary>Passe par la fenêtre de jeu : le tiroir n'a pas de poignée tant qu'il n'a jamais été affiché (voir <see cref="ExchangePanel.PostToForm"/>).</summary>
        private bool Post(Action action) => ExchangePanel.PostToForm(Host, () => { if (!IsDisposed) action(); });

        private void OnServerChanged() { if (!Post(ApplyServerChange)) OnUi(ApplyServerChange); }

        private void ApplyServerChange()
        {
            bool open = IsServerWindowOpen;
            if (open && !wasOpen) RequestShow();
            else if (!open && wasOpen) { itemTips?.Hide(); CloseBuyDialog(); RaiseClosed(); }
            wasOpen = open;
            RefreshView();
        }

        public override void RefreshView()
        {
            if (Game == null || categoryBox == null) return;
            AuctionHouse auction = Game.Interactions.Auction;
            refreshing = true;
            try
            {
                AuctionHouseInfo info = auction.Info;
                heading.Text = Title + (auction.IsBuying ? " (" + AuctionUi.Text("BIGSTORE_MAX_LEVEL", "Niveau maximum") + " : " + info.MaxLevel + ")" : string.Empty);
                summary.Text = auction.IsBuying ? AuctionUi.Summary(info, Game.character?.Kamas ?? 0) : string.Empty;
                if (!shownCategories.SequenceEqual(info.Categories))
                {
                    categoryBox.Items.Clear();
                    foreach (int category in info.Categories.OrderBy(AuctionHouse.CategoryName, StringComparer.CurrentCultureIgnoreCase))
                        categoryBox.Items.Add(new CategoryChoice(category));
                    shownCategories = (int[])info.Categories.Clone();
                }
                int wanted = auction.CurrentCategory;
                int index = -1;
                for (int i = 0; i < categoryBox.Items.Count; i++) if (((CategoryChoice)categoryBox.Items[i]).Id == wanted) { index = i; break; }
                if (categoryBox.SelectedIndex != index) categoryBox.SelectedIndex = index;

                int selectedTemplate = auction.CurrentTemplate;
                templateList.BeginUpdate(); templateList.Items.Clear();
                foreach (int template in auction.Templates)
                {
                    var row = templateList.Items.Add(InventoryObjects.DisplayName(template));
                    int? level = null;
                    try { level = InventoryObjects.ReturnInventory(template)?.Level ?? Tool_BotProtocol.Game.Data.LangData.Item.Level(template); } catch (Exception) { /* fiche absente */ }
                    row.SubItems.Add(level.HasValue && level.Value > 0 ? level.Value.ToString(CultureInfo.InvariantCulture) : string.Empty);
                    row.Tag = template;
                    if (template == selectedTemplate) row.Selected = true;
                }
                templateList.EndUpdate();
                templatesTitle.Text = AuctionUi.Text("BIGSTORE_ITEM_LIST", "Objets en vente") + (auction.CurrentCategory >= 0 ? " · " + AuctionHouse.CategoryName(auction.CurrentCategory) + " (" + templateList.Items.Count + ")" : string.Empty);

                uint selectedLine = lineList.SelectedItems.Count == 0 ? 0u : (uint)lineList.SelectedItems[0].Tag;
                lineList.BeginUpdate(); lineList.Items.Clear();
                foreach (AuctionLine line in auction.Lines)
                {
                    var row = lineList.Items.Add(line.Name + (line.Effects.Length > 0 ? " (" + ItemEffects.DescribeAll(line.Effects).FirstOrDefault() + ")" : string.Empty));
                    for (int i = 0; i < AuctionHouse.QuantityIndexes; i++) row.SubItems.Add(line.Prices[i].HasValue ? line.Prices[i].Value.ToString("N0", CultureInfo.CurrentCulture) : "—");
                    row.Tag = line.Id;
                    if (line.Id == selectedLine) row.Selected = true;
                }
                lineList.EndUpdate();
                linesTitle.Text = AuctionUi.Text("BIGSTORE_ITEM_SET_PRICES", "Prix des lots") + (auction.CurrentTemplate >= 0 ? " · " + InventoryObjects.DisplayName(auction.CurrentTemplate) : string.Empty);
                averageLabel.Text = auction.CurrentTemplate >= 0 && auction.AveragePrices.TryGetValue(auction.CurrentTemplate, out long average)
                    ? AuctionUi.Text("BIGSTORE_MIDDLEPRICE", "Prix moyen constaté dans cet hôtel : " + average + " kamas/u.", average.ToString(CultureInfo.InvariantCulture)) : string.Empty;
                status.Text = auction.IsBuying ? (auction.LastMessage.Length > 0 ? auction.LastMessage : AuctionUi.Text("BIGSTORE_HELP_SELECT_TYPE", "Sélectionne une catégorie..."))
                    : IdleText + (auction.LastMessage.Length > 0 ? "\n" + auction.LastMessage : string.Empty);
            }
            finally { refreshing = false; }
            FillQuantities();
            UpdateButtons();
        }

        private sealed class CategoryChoice
        {
            internal CategoryChoice(int id) { Id = id; }
            internal int Id { get; }
            public override string ToString() => AuctionHouse.CategoryName(Id);
        }

        /// <summary>Quantités disponibles pour la ligne sélectionnée (prix présent), comme les trois boutons de <c>BigStoreBuy</c>.</summary>
        private void FillQuantities()
        {
            if (quantityBox == null || Game == null) return;
            AuctionHouse auction = Game.Interactions.Auction;
            uint lineId = lineList.SelectedItems.Count == 1 ? (uint)lineList.SelectedItems[0].Tag : 0u;
            AuctionLine line = lineId == 0 ? null : auction.Lines.FirstOrDefault(entry => entry.Id == lineId);
            int previous = quantityBox.SelectedItem is AuctionQuantityChoice chosen ? chosen.Index : 0;
            refreshing = true;
            try
            {
                quantityBox.Items.Clear();
                if (line != null)
                    for (int index = 1; index <= AuctionHouse.QuantityIndexes; index++)
                        if (line.PriceOf(index).HasValue) quantityBox.Items.Add(new AuctionQuantityChoice(index, auction.Info.QuantityOf(index), line.PriceOf(index)));
                for (int i = 0; i < quantityBox.Items.Count; i++) if (((AuctionQuantityChoice)quantityBox.Items[i]).Index == previous) quantityBox.SelectedIndex = i;
                if (quantityBox.SelectedIndex < 0 && quantityBox.Items.Count > 0) quantityBox.SelectedIndex = 0;
            }
            finally { refreshing = false; }
        }

        private void UpdateButtons()
        {
            if (Game == null || buyButton == null) return;
            AuctionHouse auction = Game.Interactions.Auction;
            bool connected = Connected, buying = auction.IsBuying;
            categoryBox.Enabled = connected && buying;
            searchBox.Enabled = searchButton.Enabled = connected && buying;
            buyButton.Enabled = connected && buying && !auction.IsBuyPending && quantityBox.SelectedItem is AuctionQuantityChoice;
            switchButton.Enabled = connected && buying && !auction.IsOpeningPending;
            leaveButton.Enabled = connected && auction.IsOpen;
        }

        private async Task CategoryChosen()
        {
            if (refreshing || Game == null || !(categoryBox.SelectedItem is CategoryChoice choice)) return;
            AuctionHouse auction = Game.Interactions.Auction;
            if (choice.Id == auction.CurrentCategory) return;
            await ReportAsync(() => auction.SelectCategoryAsync(choice.Id));
        }

        /// <summary>Comme <c>BigStoreBuy.itemSelected</c> : prix moyen (<c>EHP</c>) puis lots (<c>EHl</c>).</summary>
        private async Task TemplateChosen()
        {
            if (refreshing || Game == null || templateList.SelectedItems.Count != 1) return;
            int template = (int)templateList.SelectedItems[0].Tag;
            AuctionHouse auction = Game.Interactions.Auction;
            if (template == auction.CurrentTemplate) return;
            await auction.RequestAveragePriceAsync(template);
            await ReportAsync(() => auction.SelectTemplateAsync(template));
        }

        /// <summary>Recherche par le nom de l'objet (<c>BigStoreSearch</c>) : modèle trouvé dans les fiches, catégorie de l'hôtel, puis <c>EHS</c>.</summary>
        private async Task Search()
        {
            if (Game == null) return;
            AuctionHouse auction = Game.Interactions.Auction;
            KeyValuePair<int, int>? found = AuctionHouse.FindTemplate(searchBox.Text);
            if (found == null) { Feedback("Aucun objet de ce nom dans les fiches d'objets."); return; }
            if (!auction.Info.HasCategory(found.Value.Value)) { Feedback(AuctionUi.Text("BIGSTORE_BAD_TYPE", "Il est impossible de mettre en vente cette catégorie d'objet dans cet hôtel de vente.")); return; }
            await ReportAsync(() => auction.SearchAsync(found.Value.Value, found.Value.Key));
        }

        /// <summary><c>DO_U_BUY_ITEM_BIGSTORE</c> (Oui / Non) après le contrôle des kamas (<c>NOT_ENOUGH_RICH</c>) ; « Oui » envoie <c>EHB</c>.</summary>
        private async Task AskBuy()
        {
            try
            {
                if (Game == null || lineList.SelectedItems.Count != 1 || !(quantityBox.SelectedItem is AuctionQuantityChoice choice) || !choice.Price.HasValue) return;
                AuctionHouse auction = Game.Interactions.Auction;
                uint lineId = (uint)lineList.SelectedItems[0].Tag;
                AuctionLine line = auction.Lines.FirstOrDefault(entry => entry.Id == lineId);
                if (line == null) return;
                long price = choice.Price.Value;
                if (price > (Game.character?.Kamas ?? 0)) { Feedback(AuctionUi.Text("NOT_ENOUGH_RICH", "Tu n'as pas assez de kamas pour réaliser cette action.")); return; }
                CloseBuyDialog();
                string lot = "x" + choice.Quantity + " " + line.Name;
                Form[] before = BotDialogs.OpenDialogs.ToArray();
                Task<BotDialogResult> question = BotDialogs.AskYesNoAsync(Host, Title,
                    AuctionUi.Text("DO_U_BUY_ITEM_BIGSTORE", "Confirmez-vous l'achat de '" + lot + "' au prix de " + price + " kamas ?", lot, price.ToString(CultureInfo.InvariantCulture)));
                buyDialog = BotDialogs.OpenDialogs.Except(before).LastOrDefault();
                BotDialogResult answer = await question;
                buyDialog = null;
                if (answer != BotDialogResult.Yes) { Feedback("Achat abandonné : rien n'a été envoyé."); return; }
                if (IsDisposed) await auction.BuyAsync(lineId, choice.Index, price); else await ReportAsync(() => auction.BuyAsync(lineId, choice.Index, price));
            }
            catch (Exception error) { Account?.Logger?.LogException(AuctionUi.Reference, error); }
        }

        private void CloseBuyDialog()
        {
            Form dialog = buyDialog;
            buyDialog = null;
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
