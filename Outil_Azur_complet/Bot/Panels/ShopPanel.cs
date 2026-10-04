using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Perso.Inventory;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>
    /// Boutique d'un PNJ (<c>NpcShop</c> du client) : s'ouvre sur <c>ECK0</c> puis <c>EL</c>, se ferme sur <c>EV</c>.
    /// Acheter envoie <c>EB&lt;modèle&gt;|&lt;quantité&gt;</c>, Vendre <c>ES&lt;objet&gt;|&lt;quantité&gt;</c>, Fermer (ou ×/Échap) <c>EV</c>.
    /// </summary>
    public sealed class ShopPanel : GamePanel
    {
        private const string NoShopText = "Clic droit sur un personnage non joueur de la carte, puis « Acheter/Vendre » (ER0|pnj).\nLa boutique s'ouvre lorsque le serveur l'annonce (ECK0 puis EL) ; fermez d'abord un dialogue en cours.";
        private ListView shopList, shopInventory;
        private NumericUpDown shopQuantity, sellQuantity;
        private Control shopBuy, shopSell, shopLeave;
        private Label shopStatus;
        private bool wasOpen;
        private InventoryClass inventory;

        public override string Title => "Boutique";
        public override Image Icon => ClientAssets.Icon("kamas", 24);
        public override bool IsModal => true;
        public override bool IsServerWindowOpen => Game?.Interactions?.Shop?.IsOpen == true;

        protected override Control CreateView()
        {
            var page = Page();
            // Articles à acheter en haut, objets du sac à vendre en bas.
            var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
            split.RowStyles.Add(new RowStyle(SizeType.Percent, 55)); split.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
            var buyArea = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 6) };
            var buyTitle = MakeLabel("Articles du marchand", 9, true); buyTitle.Dock = DockStyle.Top; buyTitle.Height = 22;
            shopList = MakeList(9, "Article", "Prix");
            shopList.Columns[0].Width = 210; shopList.Columns[1].Width = 115;
            shopList.SelectedIndexChanged += (s, e) => UpdateButtons();
            shopQuantity = InventoryPanel.Quantity(); shopQuantity.Maximum = NpcShop.MaxQuantity;
            shopBuy = MakeButton("Acheter", async (s, e) => await BuySelected(), true, 100);
            buyArea.Controls.Add(shopList); buyArea.Controls.Add(buyTitle); buyArea.Controls.Add(BotUi.Actions(shopBuy, InventoryPanel.QuantityLabel(), shopQuantity));
            var sellArea = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            var sellTitle = MakeLabel("Vos objets à vendre (sac)", 9, true); sellTitle.Dock = DockStyle.Top; sellTitle.Height = 22;
            shopInventory = MakeList(9, "Objet", "Qté");
            shopInventory.Columns[0].Width = 250; shopInventory.Columns[1].Width = 75;
            shopInventory.SelectedIndexChanged += (s, e) => UpdateButtons();
            sellQuantity = InventoryPanel.Quantity();
            shopSell = MakeButton("Vendre", async (s, e) => await SellSelected(), true, 100);
            sellArea.Controls.Add(shopInventory); sellArea.Controls.Add(sellTitle); sellArea.Controls.Add(BotUi.Actions(shopSell, InventoryPanel.QuantityLabel(), sellQuantity));
            split.Controls.Add(buyArea, 0, 0); split.Controls.Add(sellArea, 0, 1);
            shopStatus = MakeStatus(NoShopText);
            shopLeave = MakeButton("Fermer", async (s, e) => await LeaveShop(), false, 100);
            page.Controls.Add(split); page.Controls.Add(shopStatus); page.Controls.Add(BotUi.Actions(shopLeave));
            return page;
        }

        protected override void OnBind(GameClass game)
        {
            game.Interactions.Shop.Changed += OnServerChanged;
            inventory = game.character?.Inventory;
            if (inventory != null) inventory.RefreshInventory += OnInventoryChanged;
        }
        protected override void OnUnbind(GameClass game)
        {
            if (game.Interactions != null) game.Interactions.Shop.Changed -= OnServerChanged;
            if (inventory != null) inventory.RefreshInventory -= OnInventoryChanged;
            inventory = null;
        }

        private void OnServerChanged() => OnUi(() =>
        {
            bool open = IsServerWindowOpen;
            if (open && !wasOpen) RequestShow();
            else if (!open && wasOpen) RaiseClosed();
            wasOpen = open;
            RefreshView();
        });
        private void OnInventoryChanged(bool changed) { if (changed) OnUi(RefreshView); }

        public override void RefreshView()
        {
            if (Game == null || shopList == null) return;
            NpcShop shop = Game.Interactions.Shop;
            int selectedArticle = shopList.SelectedItems.Count == 0 ? -1 : (int)shopList.SelectedItems[0].Tag;
            shopList.BeginUpdate(); shopList.Items.Clear();
            foreach (ShopArticle article in shop.Articles)
            {
                var row = shopList.Items.Add(article.Name);
                row.SubItems.Add(article.Price.HasValue ? article.Price.Value + " kamas" : "prix non transmis"); row.Tag = article.TemplateId;
                if (article.TemplateId == selectedArticle) row.Selected = true;
            }
            shopList.EndUpdate();
            uint selectedSale = shopInventory.SelectedItems.Count == 0 ? 0u : (uint)shopInventory.SelectedItems[0].Tag;
            shopInventory.BeginUpdate(); shopInventory.Items.Clear();
            foreach (InventoryObjects item in Game.character.Inventory.Objets.Where(entry => !entry.IsEquipped()).OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var row = shopInventory.Items.Add(item.Name); row.SubItems.Add(item.Qua.ToString()); row.Tag = item.Inventory_ID;
                if (item.Inventory_ID == selectedSale) row.Selected = true;
            }
            shopInventory.EndUpdate();
            shopStatus.Text = shop.IsOpen ? "Boutique de " + shop.NpcName + " · " + shop.Articles.Count + " article(s). " + shop.LastMessage
                : NoShopText + (shop.LastMessage.Length > 0 ? " " + shop.LastMessage : string.Empty);
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            if (Game == null || shopBuy == null) return;
            NpcShop shop = Game.Interactions.Shop;
            bool connected = Connected;
            shopBuy.Enabled = connected && shop.IsOpen && !shop.IsPending && shopList.SelectedItems.Count == 1;
            InventoryObjects sale = InventoryPanel.SelectedItem(Game, shopInventory);
            if (sale != null) sellQuantity.Maximum = Math.Max(1, sale.Qua);
            shopSell.Enabled = connected && shop.IsOpen && !shop.IsPending && sale != null;
            shopLeave.Enabled = connected && shop.IsOpen;
        }

        protected internal override bool OnUserClose()
        {
            if (!IsServerWindowOpen) return true;
            _ = LeaveShop();
            return false;
        }

        private Task BuySelected()
        {
            if (shopList.SelectedItems.Count != 1) return Task.CompletedTask;
            int template = (int)shopList.SelectedItems[0].Tag; int quantity = (int)shopQuantity.Value;
            return ReportAsync(() => Game.Interactions.Shop.BuyAsync(template, quantity));
        }
        private Task SellSelected()
        {
            InventoryObjects item = InventoryPanel.SelectedItem(Game, shopInventory);
            if (item == null) return Task.CompletedTask;
            int quantity = (int)sellQuantity.Value;
            return ReportAsync(() => Game.Interactions.Shop.SellAsync(item.Inventory_ID, quantity));
        }
        private Task LeaveShop() => ReportAsync(() => Game.Interactions.Shop.LeaveAsync());
    }
}
