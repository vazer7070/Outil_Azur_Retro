using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Exchanges;
using Tool_BotProtocol.Game.Perso.Inventory;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>
    /// Coffre ou banque (interface <c>Storage</c> du client 1.34) : contenu en grille, sac du personnage dessous, kamas des
    /// deux côtés. S'ouvre sur <c>ECK5</c> (réponse du banquier ou code de coffre accepté) puis <c>EL</c>, se ferme sur <c>EV</c>.
    /// Déposer envoie <c>EMO+&lt;objet&gt;|&lt;quantité&gt;</c>, Retirer <c>EMO-&lt;objet&gt;|&lt;quantité&gt;</c>, les kamas
    /// <c>EMG&lt;montant&gt;</c> ou <c>EMG-&lt;montant&gt;</c> ; le contenu ne change qu'à la réponse <c>EsK…</c> du serveur.
    /// </summary>
    public sealed class StoragePanel : GamePanel
    {
        private const string IdleText = "Aucun coffre ouvert. Parlez à un banquier ou ouvrez un coffre : le volet s'ouvre lorsque le serveur l'annonce (ECK5 puis EL).";
        private Label storageKamas, storageStatus;
        private ExchangeItemGrid storageGrid;
        private ListView storageBag;
        private NumericUpDown withdrawQuantity, depositQuantity, storageKamasAmount;
        private Control storageWithdraw, storageDeposit, storageKamasDeposit, storageKamasWithdraw, storageLeave;
        private bool wasOpen;
        private InventoryClass inventory;
        private StorageExchange bound;

        public override string Title => "Coffre";
        public override Image Icon => ClientAssets.Icon("icone-inventaire", 24);
        public override bool IsModal => true;
        public override bool IsServerWindowOpen => Game?.Interactions?.Storage?.IsOpen == true;

        protected override Control CreateView()
        {
            var page = Page();
            var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
            split.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); split.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

            var storageArea = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 6) };
            var storageTitle = MakeLabel("Contenu du coffre", 9, true); storageTitle.Dock = DockStyle.Top; storageTitle.Height = 22;
            storageKamas = MakeLabel("0 kamas", 9); storageKamas.Dock = DockStyle.Bottom; storageKamas.Height = 22; storageKamas.TextAlign = ContentAlignment.MiddleLeft;
            storageGrid = new ExchangeItemGrid { Dock = DockStyle.Fill, AccessibleName = "Contenu du coffre", EmptyText = "Coffre vide." };
            storageGrid.SelectionChanged += (s, e) => UpdateButtons();
            withdrawQuantity = InventoryPanel.Quantity();
            storageWithdraw = MakeButton("Retirer", async (s, e) => await WithdrawSelected(), false, 100);
            storageArea.Controls.Add(storageGrid); storageArea.Controls.Add(storageKamas); storageArea.Controls.Add(storageTitle);
            storageArea.Controls.Add(BotUi.Actions(storageWithdraw, InventoryPanel.QuantityLabel(), withdrawQuantity));

            var bagArea = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            var bagTitle = MakeLabel("Votre sac", 9, true); bagTitle.Dock = DockStyle.Top; bagTitle.Height = 22;
            storageBag = MakeList(9, "Objet", "Qté");
            storageBag.Columns[0].Width = 250; storageBag.Columns[1].Width = 75;
            storageBag.SelectedIndexChanged += (s, e) => UpdateButtons();
            depositQuantity = InventoryPanel.Quantity();
            storageDeposit = MakeButton("Déposer", async (s, e) => await DepositSelected(), false, 100);
            bagArea.Controls.Add(storageBag); bagArea.Controls.Add(bagTitle);
            bagArea.Controls.Add(BotUi.Actions(storageDeposit, InventoryPanel.QuantityLabel(), depositQuantity));
            split.Controls.Add(storageArea, 0, 0); split.Controls.Add(bagArea, 0, 1);

            storageKamasAmount = InventoryPanel.Quantity(); storageKamasAmount.Minimum = 1; storageKamasAmount.Maximum = 1; storageKamasAmount.Width = 110;
            storageKamasAmount.ThousandsSeparator = true;
            var kamasLabel = InventoryPanel.QuantityLabel(); kamasLabel.Text = "Kamas";
            storageKamasDeposit = MakeButton("Déposer", async (s, e) => await MoveKamas(true), false, 90);
            storageKamasWithdraw = MakeButton("Retirer", async (s, e) => await MoveKamas(false), false, 90);
            var kamasBar = BotUi.Actions(kamasLabel, storageKamasAmount, storageKamasDeposit, storageKamasWithdraw);

            storageStatus = MakeStatus(IdleText);
            storageLeave = MakeButton("Fermer", async (s, e) => await Leave(), false, 100);
            // Ordre d'ancrage : le dernier ajouté prend le bord en premier.
            page.Controls.Add(split); page.Controls.Add(kamasBar); page.Controls.Add(storageStatus); page.Controls.Add(BotUi.Actions(storageLeave));
            return page;
        }

        protected override void OnBind(GameClass game)
        {
            bound = game.Interactions?.Storage;
            if (bound != null) bound.Changed += OnServerChanged;
            inventory = game.character?.Inventory;
            if (inventory != null) inventory.RefreshInventory += OnInventoryChanged;
        }

        protected override void OnUnbind(GameClass game)
        {
            if (bound != null) bound.Changed -= OnServerChanged;
            bound = null;
            if (inventory != null) inventory.RefreshInventory -= OnInventoryChanged;
            inventory = null;
            wasOpen = false;
        }

        private void OnServerChanged() => Post(() =>
        {
            bool open = IsServerWindowOpen;
            if (open && !wasOpen) RequestShow();
            else if (!open && wasOpen) RaiseClosed();
            wasOpen = open;
            RefreshView();
        });

        private void OnInventoryChanged(bool changed) { if (changed) Post(RefreshView); }

        /// <summary>Thread de l'interface par la fenêtre de jeu (voir <see cref="ExchangePanel.PostToForm"/>).</summary>
        private bool Post(Action action) => ExchangePanel.PostToForm(Host, () => { if (!IsDisposed) action(); });

        public override void RefreshView()
        {
            StorageExchange storage = Game?.Interactions?.Storage;
            if (storage == null || storageGrid == null) return;
            storageGrid.SetItems(storage.Items);
            storageKamas.Text = storage.Kamas.ToString("N0", CultureInfo.CurrentCulture) + " kamas dans le coffre";

            uint selected = storageBag.SelectedItems.Count == 0 ? 0u : (uint)storageBag.SelectedItems[0].Tag;
            storageBag.BeginUpdate(); storageBag.Items.Clear();
            var bag = Game.character?.Inventory?.Objets;
            if (bag != null)
                foreach (InventoryObjects item in bag.Where(entry => entry != null && !entry.IsEquipped()).OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    var row = storageBag.Items.Add(item.Name); row.SubItems.Add(item.Qua.ToString(CultureInfo.CurrentCulture)); row.Tag = item.Inventory_ID;
                    if (item.Inventory_ID == selected) row.Selected = true;
                }
            storageBag.EndUpdate();

            string message = storage.LastMessage;
            if (!storage.IsOpen) storageStatus.Text = IdleText + (message.Length > 0 ? " " + message : string.Empty);
            else if (!storage.ContentReceived) storageStatus.Text = "Coffre ouvert ; en attente de son contenu (EL).";
            else storageStatus.Text = storage.Items.Count + " objet(s) dans le coffre ; le contenu suit les réponses du serveur (EsK).";
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            StorageExchange storage = Game?.Interactions?.Storage;
            if (storage == null || storageLeave == null || IsDisposed) return;
            bool open = Connected && storage.IsOpen;
            ExchangeItem stored = storageGrid.SelectedItem;
            InventoryObjects item = InventoryPanel.SelectedItem(Game, storageBag);
            if (stored != null) withdrawQuantity.Maximum = Math.Max(1, stored.Quantity);
            if (item != null) depositQuantity.Maximum = Math.Max(1, item.Qua);
            long owned = Math.Max(0, Game.character?.Kamas ?? 0);
            storageKamasAmount.Maximum = Math.Max(1, Math.Max(owned, storage.Kamas));
            storageWithdraw.Enabled = open && stored != null;
            storageDeposit.Enabled = open && item != null;
            storageKamasDeposit.Enabled = open && owned > 0;
            storageKamasWithdraw.Enabled = open && storage.Kamas > 0;
            storageLeave.Enabled = open;
        }

        protected internal override bool OnUserClose()
        {
            if (!IsServerWindowOpen) return true;
            _ = Leave();
            return false;
        }

        private Task WithdrawSelected()
        {
            StorageExchange storage = Game?.Interactions?.Storage;
            ExchangeItem stored = storageGrid.SelectedItem;
            if (storage == null || stored == null) return Task.CompletedTask;
            int quantity = Math.Min((int)withdrawQuantity.Value, stored.Quantity);
            return ReportAsync(() => storage.WithdrawAsync(stored.Id, quantity));
        }

        private Task DepositSelected()
        {
            StorageExchange storage = Game?.Interactions?.Storage;
            InventoryObjects item = InventoryPanel.SelectedItem(Game, storageBag);
            if (storage == null || item == null) return Task.CompletedTask;
            int quantity = Math.Min((int)depositQuantity.Value, item.Qua);
            return ReportAsync(() => storage.DepositAsync(item.Inventory_ID, quantity));
        }

        private Task MoveKamas(bool deposit)
        {
            StorageExchange storage = Game?.Interactions?.Storage;
            if (storage == null) return Task.CompletedTask;
            long kamas = (long)storageKamasAmount.Value;
            return ReportAsync(() => deposit ? storage.DepositKamasAsync(kamas) : storage.WithdrawKamasAsync(kamas));
        }

        private Task Leave()
        {
            StorageExchange storage = Game?.Interactions?.Storage;
            return storage == null ? Task.CompletedTask : ReportAsync(storage.LeaveAsync);
        }
    }
}
