using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Perso.Inventory;

namespace Outil_Azur_complet.Bot
{
    /// <summary>
    /// Volets ouverts par le serveur (Dialogue, Zaaps, Boutique) et actions du volet Inventaire.
    /// Chaque bouton envoie le paquet du client 1.34 ; l'affichage ne change qu'à la réponse du serveur.
    /// </summary>
    public partial class GameClientFullform
    {
        /// <summary>Index des volets dans le tiroir, après Caractéristiques (0), Inventaire (1), Sorts (2), Métiers (3) et Journal (4).</summary>
        public const int DialogPanel = 5, ZaapPanel = 6, ShopPanel = 7;
        private const string NoDialogText = "Cliquez sur un personnage non joueur de la carte pour lui parler.\nLes textes des questions et réponses ne sont pas exportés dans BotNPCs : seuls leurs numéros sont affichés.";
        private const string NoZaapText = "Cliquez sur le zaap de la carte pour recevoir la liste des destinations.";
        private const string NoShopText = "Clic droit sur un personnage non joueur de la carte : « Échanger » (ER0|pnj).\nLa boutique s'ouvre lorsque le serveur l'annonce (ECK0 puis EL) ; fermez d'abord un dialogue en cours.";
        private Label dialogTitle, dialogQuestion, dialogStatus, zaapStatus, shopStatus, inventoryHelp;
        private FlowLayoutPanel dialogAnswers;
        private Control dialogLeave, zaapTeleport, zaapLeave, shopBuy, shopSell, shopLeave, equipItem, unequipItem, useItem, dropItem;
        private ListView zaapList, shopList, shopInventory;
        private NumericUpDown inventoryQuantity, shopQuantity, sellQuantity;
        private string dialogAnswersKey = string.Empty;
        private bool dialogWasOpen, zaapWasOpen, shopWasOpen, dropArmed, refreshingLists;

        private void BuildInteractionPanels()
        {
            // Volet Inventaire existant : équiper, déséquiper, utiliser, jeter (avec confirmation).
            inventoryHelp = BotUi.Status("Sélectionnez un objet. Le serveur confirme chaque action."); inventoryHelp.Height = 46;
            inventoryQuantity = Quantity();
            equipItem = BotUi.Button("Équiper", async (s, e) => await EquipSelected(true), true, 95);
            unequipItem = BotUi.Button("Déséquiper", async (s, e) => await EquipSelected(false), false, 112);
            useItem = BotUi.Button("Utiliser", async (s, e) => await UseSelected(), false, 92);
            dropItem = BotUi.Button("Jeter", async (s, e) => await DropSelected(), false, 100);
            inventory.Parent.Controls.Add(inventoryHelp);
            inventory.Parent.Controls.Add(BotUi.Actions(equipItem, unequipItem, useItem, dropItem, QuantityLabel(), inventoryQuantity));
            inventory.SelectedIndexChanged += (s, e) => { if (!refreshingLists) DisarmDrop(); UpdateInventoryActions(); };

            // Dialogue PNJ : question et réponses sous forme de boutons.
            var dialogPage = new TabPage("Dialogue") { BackColor = BotUi.Paper, Padding = new Padding(7) };
            dialogTitle = BotUi.Label("Aucun dialogue en cours", 11, true); dialogTitle.Dock = DockStyle.Top; dialogTitle.Height = 28; dialogTitle.AutoEllipsis = true;
            dialogQuestion = BotUi.Label(NoDialogText, 9); dialogQuestion.Dock = DockStyle.Top; dialogQuestion.Height = 96; dialogQuestion.ForeColor = BotUi.Muted;
            dialogAnswers = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                AutoScroll = true, BackColor = BotUi.Paper, Padding = new Padding(0, 4, 0, 0) };
            dialogStatus = BotUi.Status(string.Empty);
            dialogLeave = BotUi.Button("Quitter", async (s, e) => await LeaveDialog(), false, 110);
            dialogPage.Controls.Add(dialogAnswers); dialogPage.Controls.Add(dialogQuestion); dialogPage.Controls.Add(dialogTitle);
            dialogPage.Controls.Add(dialogStatus); dialogPage.Controls.Add(BotUi.Actions(dialogLeave));
            panels.TabPages.Add(dialogPage);

            // Zaaps : destinations, coût, téléportation.
            var zaapPage = new TabPage("Zaaps") { BackColor = BotUi.Paper, Padding = new Padding(7) };
            zaapList = BotUi.List("Destination", "Coût"); zaapList.Font = new Font("Tahoma", 9);
            zaapList.Columns[0].Width = 225; zaapList.Columns[1].Width = 100;
            zaapList.SelectedIndexChanged += (s, e) => UpdateInteractionButtons();
            zaapStatus = BotUi.Status(NoZaapText);
            zaapTeleport = BotUi.Button("Se téléporter", async (s, e) => await TeleportSelected(), true, 140);
            zaapLeave = BotUi.Button("Fermer", async (s, e) => await LeaveZaap(), false, 100);
            zaapPage.Controls.Add(zaapList); zaapPage.Controls.Add(zaapStatus); zaapPage.Controls.Add(BotUi.Actions(zaapTeleport, zaapLeave));
            panels.TabPages.Add(zaapPage);

            // Boutique PNJ : articles à acheter en haut, objets du sac à vendre en bas.
            var shopPage = new TabPage("Boutique") { BackColor = BotUi.Paper, Padding = new Padding(7) };
            var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
            split.RowStyles.Add(new RowStyle(SizeType.Percent, 55)); split.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
            var buyArea = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 6) };
            var buyTitle = BotUi.Label("Articles du marchand", 9, true); buyTitle.Dock = DockStyle.Top; buyTitle.Height = 22;
            shopList = BotUi.List("Article", "Prix"); shopList.Font = new Font("Tahoma", 9);
            shopList.Columns[0].Width = 210; shopList.Columns[1].Width = 115;
            shopList.SelectedIndexChanged += (s, e) => UpdateInteractionButtons();
            shopQuantity = Quantity(); shopQuantity.Maximum = NpcShop.MaxQuantity;
            shopBuy = BotUi.Button("Acheter", async (s, e) => await BuySelected(), true, 100);
            buyArea.Controls.Add(shopList); buyArea.Controls.Add(buyTitle); buyArea.Controls.Add(BotUi.Actions(shopBuy, QuantityLabel(), shopQuantity));
            var sellArea = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            var sellTitle = BotUi.Label("Vos objets à vendre (sac)", 9, true); sellTitle.Dock = DockStyle.Top; sellTitle.Height = 22;
            shopInventory = BotUi.List("Objet", "Qté"); shopInventory.Font = new Font("Tahoma", 9);
            shopInventory.Columns[0].Width = 250; shopInventory.Columns[1].Width = 75;
            shopInventory.SelectedIndexChanged += (s, e) => UpdateInteractionButtons();
            sellQuantity = Quantity();
            shopSell = BotUi.Button("Vendre", async (s, e) => await SellSelected(), true, 100);
            sellArea.Controls.Add(shopInventory); sellArea.Controls.Add(sellTitle); sellArea.Controls.Add(BotUi.Actions(shopSell, QuantityLabel(), sellQuantity));
            split.Controls.Add(buyArea, 0, 0); split.Controls.Add(sellArea, 0, 1);
            shopStatus = BotUi.Status(NoShopText);
            shopLeave = BotUi.Button("Fermer", async (s, e) => await LeaveShop(), false, 100);
            shopPage.Controls.Add(split); shopPage.Controls.Add(shopStatus); shopPage.Controls.Add(BotUi.Actions(shopLeave));
            panels.TabPages.Add(shopPage);
        }

        private static NumericUpDown Quantity() => new NumericUpDown { Minimum = 1, Maximum = 1, Value = 1, Width = 80, Height = 34,
            Font = new Font("Tahoma", 9), BackColor = BotUi.PaperLight, ForeColor = BotUi.Ink, Margin = new Padding(0, 4, 7, 0) };
        private static Label QuantityLabel()
        {
            var label = BotUi.Label("Quantité", 8); label.Width = 58; label.Height = 34; label.TextAlign = ContentAlignment.MiddleLeft;
            label.Margin = new Padding(4, 0, 0, 0); return label;
        }

        private void SubscribeInteractions()
        {
            var game = ActualCompte.Game;
            game.Interactions.Npc.Changed += InteractionChanged;
            game.Interactions.Zaap.Changed += InteractionChanged;
            game.Interactions.Shop.Changed += InteractionChanged;
            game.character.Inventory.RefreshInventory += InventoryChanged;
        }
        private void UnsubscribeInteractions()
        {
            var game = ActualCompte.Game;
            if (game == null) return;
            game.Interactions.Npc.Changed -= InteractionChanged;
            game.Interactions.Zaap.Changed -= InteractionChanged;
            game.Interactions.Shop.Changed -= InteractionChanged;
            game.character.Inventory.RefreshInventory -= InventoryChanged;
        }
        private void InteractionChanged() => BotUi.OnUi(this, () => { AutoOpenInteractionPanels(); RefreshState(); });
        private void InventoryChanged(bool changed) => BotUi.OnUi(this, () =>
        {
            if (!changed && ActualCompte.Game != null) ShowActionFeedback(ActualCompte.Game.character.Inventory.LastServerMessage);
            else RefreshState();
        });

        /// <summary>Ouvre le volet correspondant lorsque le serveur crée une fenêtre, et le referme lorsqu'elle se termine.</summary>
        private void AutoOpenInteractionPanels()
        {
            var interactions = ActualCompte.Game?.Interactions;
            if (interactions == null || panels == null) return;
            TogglePanel(ref dialogWasOpen, interactions.Npc.IsOpen, DialogPanel);
            TogglePanel(ref zaapWasOpen, interactions.Zaap.IsOpen, ZaapPanel);
            TogglePanel(ref shopWasOpen, interactions.Shop.IsOpen, ShopPanel);
        }
        private void TogglePanel(ref bool wasOpen, bool isOpen, int panel)
        {
            if (isOpen && !wasOpen) ShowPanel(panel);
            else if (!isOpen && wasOpen && drawer.Visible && panels.SelectedIndex == panel) ClosePanel();
            wasOpen = isOpen;
        }

        /// <summary>Met à jour les trois volets et les boutons d'inventaire depuis l'état reçu du serveur.</summary>
        private void RefreshInteractionPanels()
        {
            var game = ActualCompte.Game;
            if (game == null || dialogTitle == null) return;
            NpcDialog npc = game.Interactions.Npc; ZaapDialog zaap = game.Interactions.Zaap; NpcShop shop = game.Interactions.Shop;

            dialogTitle.Text = npc.IsOpen ? "Dialogue avec " + npc.NpcName : "Aucun dialogue en cours";
            dialogQuestion.Text = !npc.IsOpen ? NoDialogText
                : npc.QuestionId < 0 ? "En attente de la question du serveur…"
                : "Question n° " + npc.QuestionId + (npc.Parameters.Length > 0 ? " · paramètres : " + string.Join(", ", npc.Parameters) : string.Empty)
                    + (npc.IsPaused ? "\nLe serveur demande d'attendre." : string.Empty)
                    + "\nLes textes ne sont pas exportés dans BotNPCs : seuls les numéros sont affichés.";
            string key = npc.IsOpen ? npc.QuestionId + ":" + string.Join(",", npc.AnswerIds) : string.Empty;
            if (key != dialogAnswersKey)
            {
                dialogAnswersKey = key;
                dialogAnswers.SuspendLayout();
                foreach (Control old in dialogAnswers.Controls.Cast<Control>().ToArray()) { dialogAnswers.Controls.Remove(old); old.Dispose(); }
                foreach (int id in npc.AnswerIds)
                {
                    int answer = id;
                    var button = BotUi.Button("Réponse n° " + answer, async (s, e) => await AnswerDialog(answer), true, 300);
                    button.Margin = new Padding(0, 0, 0, 6); button.Tag = answer; dialogAnswers.Controls.Add(button);
                }
                dialogAnswers.ResumeLayout();
            }
            dialogStatus.Text = npc.LastMessage;

            int selectedZaap = zaapList.SelectedItems.Count == 0 ? -1 : (int)zaapList.SelectedItems[0].Tag;
            zaapList.BeginUpdate(); zaapList.Items.Clear();
            foreach (ZaapDestination destination in zaap.Destinations)
            {
                var row = zaapList.Items.Add(destination.Label + (destination.IsSaved ? " · sauvegarde" : string.Empty) + (destination.IsCurrent ? " · ici" : string.Empty));
                row.SubItems.Add(destination.Cost + " kamas"); row.Tag = destination.MapId;
                if (destination.IsCurrent) row.ForeColor = BotUi.Muted;
                if (destination.MapId == selectedZaap) row.Selected = true;
            }
            zaapList.EndUpdate();
            zaapStatus.Text = zaap.IsOpen ? zaap.Destinations.Count + " destination(s) proposée(s). " + zaap.LastMessage
                : NoZaapText + (zaap.LastMessage.Length > 0 ? " " + zaap.LastMessage : string.Empty);

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
            foreach (InventoryObjects item in game.character.Inventory.Objets.Where(entry => !entry.IsEquipped()).OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var row = shopInventory.Items.Add(item.Name); row.SubItems.Add(item.Qua.ToString()); row.Tag = item.Inventory_ID;
                if (item.Inventory_ID == selectedSale) row.Selected = true;
            }
            shopInventory.EndUpdate();
            shopStatus.Text = shop.IsOpen ? "Boutique de " + shop.NpcName + " · " + shop.Articles.Count + " article(s). " + shop.LastMessage
                : NoShopText + (shop.LastMessage.Length > 0 ? " " + shop.LastMessage : string.Empty);
            UpdateInteractionButtons();
            UpdateInventoryActions();
        }

        private void UpdateInteractionButtons()
        {
            var game = ActualCompte.Game;
            if (game == null || dialogLeave == null) return;
            NpcDialog npc = game.Interactions.Npc; ZaapDialog zaap = game.Interactions.Zaap; NpcShop shop = game.Interactions.Shop;
            bool connected = send.Enabled;
            dialogLeave.Enabled = connected && npc.IsOpen;
            foreach (Control answer in dialogAnswers.Controls) answer.Enabled = connected && npc.IsOpen && !npc.IsPaused;
            ZaapDestination selected = zaapList.SelectedItems.Count == 1
                ? zaap.Destinations.FirstOrDefault(entry => entry.MapId == (int)zaapList.SelectedItems[0].Tag) : null;
            zaapTeleport.Enabled = connected && zaap.IsOpen && selected != null && !selected.IsCurrent && selected.Cost <= game.character.Kamas;
            zaapLeave.Enabled = connected && zaap.IsOpen;
            shopBuy.Enabled = connected && shop.IsOpen && !shop.IsPending && shopList.SelectedItems.Count == 1;
            InventoryObjects sale = SelectedInventoryItem(shopInventory);
            if (sale != null) sellQuantity.Maximum = Math.Max(1, sale.Qua);
            shopSell.Enabled = connected && shop.IsOpen && !shop.IsPending && sale != null;
            shopLeave.Enabled = connected && shop.IsOpen;
        }

        private InventoryObjects SelectedInventoryItem(ListView list)
        {
            if (list == null || list.SelectedItems.Count != 1 || !(list.SelectedItems[0].Tag is uint id) || ActualCompte.Game == null) return null;
            return ActualCompte.Game.character.Inventory.GetByInventoryId(id);
        }

        private void UpdateInventoryActions()
        {
            if (equipItem == null || ActualCompte.Game == null) return;
            InventoryObjects item = SelectedInventoryItem(inventory);
            bool ready = send.Enabled && item != null && !ActualCompte.Game.Fight.IsInFight && !ActualCompte.IsMoving();
            equipItem.Enabled = ready && !item.IsEquipped() && InventoryUtilities.GetPosition(item.Type) != null;
            unequipItem.Enabled = ready && item.IsEquipped();
            useItem.Enabled = ready && !item.IsEquipped();
            dropItem.Enabled = ready && !item.IsEquipped();
            if (item != null) inventoryQuantity.Maximum = Math.Max(1, item.Qua);
            if (dropArmed) return;
            dropItem.Text = "Jeter";
            inventoryHelp.Text = item == null ? "Sélectionnez un objet. Le serveur confirme chaque action."
                : item.Name + " · " + item.Qua + " · " + (item.IsEquipped() ? "équipé" : "dans le sac")
                    + (item.HasMetadata ? string.Empty : " · fiche absente de BotObjets : type et niveau inconnus");
        }

        private void DisarmDrop() { dropArmed = false; if (dropItem != null) dropItem.Text = "Jeter"; }

        private async Task EquipSelected(bool equip)
        {
            InventoryObjects item = SelectedInventoryItem(inventory);
            if (item == null) return;
            DisarmDrop();
            InventoryClass bag = ActualCompte.Game.character.Inventory;
            try
            {
                bool sent = equip ? await bag.Equip_item(item) : await bag.Desequip_Item(item);
                ShowActionFeedback(sent ? (equip ? "Équipement demandé ; le serveur confirme par OM." : "Retrait demandé ; le serveur confirme par OM.") : "Demande refusée : voir le journal.");
            }
            catch (Exception ex) { ShowActionFeedback(ex.Message); }
        }
        private async Task UseSelected()
        {
            InventoryObjects item = SelectedInventoryItem(inventory);
            if (item == null) return;
            DisarmDrop();
            try
            {
                bool sent = await ActualCompte.Game.character.Inventory.Use_Item(item);
                ShowActionFeedback(sent ? "Utilisation demandée ; le serveur confirme." : "Demande refusée : voir le journal.");
            }
            catch (Exception ex) { ShowActionFeedback(ex.Message); }
        }
        private async Task DropSelected()
        {
            InventoryObjects item = SelectedInventoryItem(inventory);
            if (item == null) return;
            int quantity = (int)inventoryQuantity.Value;
            if (!dropArmed)
            {
                dropArmed = true; dropItem.Text = "Confirmer";
                inventoryHelp.Text = "Jeter " + quantity + " × " + item.Name + " au sol ? Cliquez de nouveau sur Confirmer ; changer de sélection annule.";
                return;
            }
            DisarmDrop();
            try
            {
                bool sent = await ActualCompte.Game.character.Inventory.Drop_Item(item, quantity);
                ShowActionFeedback(sent ? "Objet jeté ; le serveur confirme par OR ou OQ." : "Demande refusée : voir le journal.");
            }
            catch (Exception ex) { ShowActionFeedback(ex.Message); }
        }

        private async Task AnswerDialog(int answerId) => await Report(() => ActualCompte.Game.Interactions.Npc.AnswerAsync(answerId));
        private async Task LeaveDialog() => await Report(() => ActualCompte.Game.Interactions.Npc.LeaveAsync());
        private async Task TeleportSelected()
        {
            if (zaapList.SelectedItems.Count != 1) return;
            int mapId = (int)zaapList.SelectedItems[0].Tag;
            await Report(() => ActualCompte.Game.Interactions.Zaap.TeleportAsync(mapId));
        }
        private async Task LeaveZaap() => await Report(() => ActualCompte.Game.Interactions.Zaap.LeaveAsync());
        private async Task BuySelected()
        {
            if (shopList.SelectedItems.Count != 1) return;
            int template = (int)shopList.SelectedItems[0].Tag; int quantity = (int)shopQuantity.Value;
            await Report(() => ActualCompte.Game.Interactions.Shop.BuyAsync(template, quantity));
        }
        private async Task SellSelected()
        {
            InventoryObjects item = SelectedInventoryItem(shopInventory);
            if (item == null) return;
            int quantity = (int)sellQuantity.Value;
            await Report(() => ActualCompte.Game.Interactions.Shop.SellAsync(item.Inventory_ID, quantity));
        }
        private async Task LeaveShop() => await Report(() => ActualCompte.Game.Interactions.Shop.LeaveAsync());

        private async Task Report(Func<Task<InteractionResult>> action)
        {
            try
            {
                InteractionResult result = await action();
                ShowActionFeedback(result.Message);
                if (!result.Sent) ActualCompte.Logger.LogError("INTERFACE", result.Message);
            }
            catch (Exception ex) { ShowActionFeedback(ex.Message); }
        }
    }
}
