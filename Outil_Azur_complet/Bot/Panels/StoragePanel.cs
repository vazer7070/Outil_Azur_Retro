using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Controls;
using Outil_Azur_complet.Bot.Controls.Chat;
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
    /// Lot X12 : un objet du sac glissé sur le coffre (ou « Déposer ») et une case du coffre glissée sur le sac (ou « Retirer »)
    /// passent par la boîte de quantité quand la pile dépasse 1 (<c>GridInventoryViewer.dropItem</c>) ; le retrait est borné à ce
    /// que le personnage peut porter (<c>getPossibleItemReceiveQuantity</c>), <c>SRV_MSG_6</c> s'il ne peut rien prendre. Les
    /// kamas passent par la boîte, préremplie avec tous les kamas du côté d'où ils partent (<c>askKamaQuantity</c>).
    /// </summary>
    public sealed class StoragePanel : GamePanel
    {
        private const string IdleText = "Aucun coffre ouvert. Parlez à un banquier ou ouvrez un coffre : le volet s'ouvre lorsque le serveur l'annonce (ECK5 puis EL).";
        private Label storageKamas, storageStatus;
        private ExchangeItemGrid storageGrid;
        private ListView storageBag;
        private ItemDropZone bagDrop;
        private readonly TransferGate gate = new TransferGate();
        private Control storageWithdraw, storageDeposit, storageKamasDeposit, storageKamasWithdraw, storageLeave;
        private bool wasOpen;
        private InventoryClass inventory;
        private StorageExchange bound;

        public override string Title => "Coffre";
        public override Image Icon => ClientAssets.Icon("icone-inventaire", 24);
        public override bool IsModal => true;
        public override bool IsServerWindowOpen => Game?.Interactions?.Storage?.IsOpen == true;

        /// <summary>Dépose un contenu glissé sur la liste du sac (glisser-déposer, tests) : faux s'il n'y est pas accepté.</summary>
        public bool DropOnBag(object payload) => bagDrop != null && bagDrop.AcceptDrop(payload);

        protected override Control CreateView()
        {
            var page = Page();
            var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
            split.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); split.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

            var storageArea = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 6) };
            var storageTitle = MakeLabel("Contenu du coffre", 9, true); storageTitle.Dock = DockStyle.Top; storageTitle.Height = 22;
            storageKamas = MakeLabel("0 kamas", 9); storageKamas.Dock = DockStyle.Bottom; storageKamas.Height = 22; storageKamas.TextAlign = ContentAlignment.MiddleLeft;
            storageGrid = new ExchangeItemGrid { Dock = DockStyle.Fill, AccessibleName = "Contenu du coffre", EmptyText = "Coffre vide.", AllowItemDrag = true };
            storageGrid.SelectionChanged += (s, e) => UpdateButtons();
            storageGrid.DropFilter = payload => ItemTransfer.IsBagItem(payload) && CanEdit();
            storageGrid.Dropped += payload => _ = Deposit(ItemTransfer.BagItemId(payload));
            storageWithdraw = MakeButton("Retirer", async (s, e) => await WithdrawSelected(), false, 100);
            storageArea.Controls.Add(storageGrid); storageArea.Controls.Add(storageKamas); storageArea.Controls.Add(storageTitle);
            storageArea.Controls.Add(BotUi.Actions(storageWithdraw));

            var bagArea = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            var bagTitle = MakeLabel("Votre sac", 9, true); bagTitle.Dock = DockStyle.Top; bagTitle.Height = 22;
            storageBag = MakeList(9, "Objet", "Qté");
            storageBag.Columns[0].Width = 250; storageBag.Columns[1].Width = 75;
            storageBag.SelectedIndexChanged += (s, e) => UpdateButtons();
            ItemTransfer.MakeBagSource(storageBag, id => Game?.character?.Inventory?.GetByInventoryId(id), item => item.Qua);
            bagDrop = new ItemDropZone(storageBag, payload => ItemTransfer.IsFrom(payload, storageGrid) && CanEdit(),
                payload => _ = Withdraw(((ExchangeItemDrag)payload).Id));
            storageDeposit = MakeButton("Déposer", async (s, e) => await DepositSelected(), false, 100);
            bagArea.Controls.Add(storageBag); bagArea.Controls.Add(bagTitle);
            bagArea.Controls.Add(BotUi.Actions(storageDeposit));
            split.Controls.Add(storageArea, 0, 0); split.Controls.Add(bagArea, 0, 1);

            storageKamasDeposit = MakeButton("Déposer des kamas", async (s, e) => await MoveKamas(true), false, 150);
            storageKamasWithdraw = MakeButton("Retirer des kamas", async (s, e) => await MoveKamas(false), false, 150);
            var kamasBar = BotUi.Actions(storageKamasDeposit, storageKamasWithdraw);

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
            long owned = Math.Max(0, Game.character?.Kamas ?? 0);
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

        private bool CanEdit() => Connected && Game?.Interactions?.Storage?.IsOpen == true;

        private Task WithdrawSelected()
        {
            ExchangeItem stored = storageGrid.SelectedItem;
            return stored == null ? Task.CompletedTask : Withdraw(stored.Id);
        }

        private Task DepositSelected()
        {
            InventoryObjects item = InventoryPanel.SelectedItem(Game, storageBag);
            return item == null ? Task.CompletedTask : Deposit(item.Inventory_ID);
        }

        /// <summary>
        /// Retrait (dépôt sur le sac) : quantité bornée à ce que le personnage peut porter (<c>getPossibleItemReceiveQuantity</c>,
        /// poids de <c>LangData</c>, charge <c>Ow</c>) ; rien ne tient : <c>SRV_MSG_6</c> ; sinon la boîte si plus d'un exemplaire,
        /// puis <c>EMO-&lt;objet&gt;|&lt;quantité&gt;</c> borné à la pile relue.
        /// </summary>
        private Task Withdraw(uint storedId) => gate.RunAsync(async () =>
        {
            StorageExchange storage = Game?.Interactions?.Storage;
            ExchangeItem stored = storage?.Items.FirstOrDefault(entry => entry.Id == storedId);
            if (storage == null || !storage.IsOpen || stored == null) return;
            int limit = ItemTransfer.Receivable(stored.Quantity, ItemTransfer.UnitWeight(stored.TemplateId), ItemTransfer.FreePods(Game?.character?.Inventory));
            if (limit <= 0)
            {
                await BotDialogs.InfoAsync(View, ChatUiText.Get("INFORMATIONS", "Informations"),
                    ChatUiText.Get("SRV_MSG_6", "Le nombre maximum d'objets pour cet inventaire est déjà atteint."));
                return;
            }
            int? chosen = await ItemTransfer.AskAsync(View, limit);
            if (chosen == null) return;
            stored = storage.Items.FirstOrDefault(entry => entry.Id == storedId);
            if (!storage.IsOpen || stored == null || stored.Quantity <= 0) return;
            int quantity = Math.Min(Math.Min(chosen.Value, limit), stored.Quantity);
            await ReportAsync(() => storage.WithdrawAsync(storedId, quantity));
        });

        /// <summary>Dépôt d'un objet du sac : la boîte si la pile dépasse 1, puis <c>EMO+&lt;objet&gt;|&lt;quantité&gt;</c> borné à la pile relue.</summary>
        private Task Deposit(uint inventoryId) => gate.RunAsync(async () =>
        {
            StorageExchange storage = Game?.Interactions?.Storage;
            InventoryObjects item = Game?.character?.Inventory?.GetByInventoryId(inventoryId);
            if (storage == null || !storage.IsOpen || item == null || item.IsEquipped()) return;
            int? chosen = await ItemTransfer.AskAsync(View, item.Qua);
            if (chosen == null) return;
            item = Game?.character?.Inventory?.GetByInventoryId(inventoryId);
            if (!storage.IsOpen || item == null || item.Qua <= 0) return;
            int quantity = Math.Min(chosen.Value, item.Qua);
            await ReportAsync(() => storage.DepositAsync(inventoryId, quantity));
        });

        /// <summary>
        /// Kamas comme <c>GridInventoryViewer.askKamaQuantity</c> : la boîte propose tous les kamas du côté d'où ils partent (sa
        /// bourse pour un dépôt, le coffre pour un retrait) ; moins de 1 n'envoie rien, plus est ramené à ce côté (<c>validateKama</c>).
        /// </summary>
        private Task MoveKamas(bool deposit) => gate.RunAsync(async () =>
        {
            StorageExchange storage = Game?.Interactions?.Storage;
            if (storage == null || !storage.IsOpen) return;
            long available = deposit ? Math.Max(0, Game?.character?.Kamas ?? 0) : Math.Max(0, storage.Kamas);
            int maximum = (int)Math.Min(int.MaxValue, available);
            if (maximum < 1) return;
            int? chosen = await QuantityPopup.AskAsync(View, maximum, maximum);
            if (chosen == null || chosen.Value < 1 || !storage.IsOpen) return;
            available = deposit ? Math.Max(0, Game?.character?.Kamas ?? 0) : Math.Max(0, storage.Kamas);
            long kamas = Math.Min(chosen.Value, available);
            if (kamas < 1) return;
            await ReportAsync(() => deposit ? storage.DepositKamasAsync(kamas) : storage.WithdrawKamasAsync(kamas));
        });

        private Task Leave()
        {
            StorageExchange storage = Game?.Interactions?.Storage;
            return storage == null ? Task.CompletedTask : ReportAsync(storage.LeaveAsync);
        }
    }
}
