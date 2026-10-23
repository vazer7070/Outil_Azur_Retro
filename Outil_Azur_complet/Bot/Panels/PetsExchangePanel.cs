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
    /// Garde et résurrection des familiers chez un PNJ (échange de type 9, lot X4). Le client 1.34 ouvre pour ce type la même
    /// interface <c>Exchange</c> que pour un joueur : le volet reprend donc la disposition du volet Échange (sa proposition
    /// et celle du PNJ côte à côte, kamas, sac dessous, Valider grisé 3 secondes après chaque modification, Annuler), avec
    /// le nom du PNJ en face. Il s'ouvre sur <c>ECK9</c> (réponse de StarLoco à <c>ER9</c> comme à <c>ER17</c>) et se ferme sur
    /// <c>EV</c>/<c>EVa</c> ; les envois sont ceux de <see cref="PetsExchange"/> et rien ne change avant la réponse du serveur.
    /// </summary>
    public sealed class PetsExchangePanel : GamePanel
    {
        private const string IdleText = "Aucun échange de familiers en cours. Choisissez « Déposer/Reprendre un familier » ou « Ressusciter un familier » dans le menu d'un PNJ.";
        private Label petsHeading, localTitle, distantTitle, localKamas, distantKamas, petsStatus;
        private ExchangeItemGrid localGrid, distantGrid;
        private ListView petsBag;
        private NumericUpDown petsQuantity, petsKamas;
        private Control petsAdd, petsRemove, petsKamasSend, petsValidate, petsCancel;
        private Timer countdown;
        private bool wasOpen;
        private InventoryClass inventory;
        private PetsExchange bound;

        public override string Title => "Familiers";
        public override Image Icon => ClientAssets.Icon("icone-amis", 24);
        public override bool IsModal => true;
        public override bool IsServerWindowOpen => Exchange?.IsOpen == true;

        private PetsExchange Exchange => Game?.Interactions?.Exchanges?.Get<PetsExchange>();

        protected override Control CreateView()
        {
            var page = Page();
            petsHeading = MakeLabel("Familiers", 10, true); petsHeading.Dock = DockStyle.Top; petsHeading.Height = 24; petsHeading.AutoEllipsis = true;

            var columns = new TableLayoutPanel { Dock = DockStyle.Top, Height = 200, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            columns.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            columns.Controls.Add(Column("Vous", new Padding(0, 0, 4, 0), out localTitle, out localGrid, out localKamas), 0, 0);
            columns.Controls.Add(Column("PNJ", new Padding(4, 0, 0, 0), out distantTitle, out distantGrid, out distantKamas), 1, 0);
            localGrid.EmptyText = "Proposez un familier, un certificat ou un fantôme.";
            distantGrid.EmptyText = "Le PNJ répond quand la proposition lui convient.";
            localGrid.SelectionChanged += (s, e) => UpdateButtons();

            petsKamas = InventoryPanel.Quantity(); petsKamas.Minimum = 0; petsKamas.Maximum = 0; petsKamas.Width = 110;
            petsKamas.ThousandsSeparator = true;
            petsKamasSend = MakeButton("Proposer les kamas", async (s, e) => await SendKamas(), false, 150);
            var kamasLabel = InventoryPanel.QuantityLabel(); kamasLabel.Text = "Kamas";
            var kamasBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, MinimumSize = new Size(0, 43), WrapContents = true,
                Padding = new Padding(0, 7, 0, 0) };
            kamasBar.Controls.AddRange(new[] { kamasLabel, (Control)petsKamas, petsKamasSend });

            var bagArea = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            var bagTitle = MakeLabel("Votre sac", 9, true); bagTitle.Dock = DockStyle.Top; bagTitle.Height = 22;
            petsBag = MakeList(9, "Objet", "Qté");
            petsBag.Columns[0].Width = 250; petsBag.Columns[1].Width = 75;
            petsBag.SelectedIndexChanged += (s, e) => UpdateButtons();
            petsQuantity = InventoryPanel.Quantity();
            petsAdd = MakeButton("Proposer", async (s, e) => await AddSelected(), false, 100);
            petsRemove = MakeButton("Retirer", async (s, e) => await RemoveSelected(), false, 100);
            bagArea.Controls.Add(petsBag); bagArea.Controls.Add(bagTitle);
            bagArea.Controls.Add(BotUi.Actions(petsAdd, petsRemove, InventoryPanel.QuantityLabel(), petsQuantity));

            petsStatus = MakeStatus(IdleText); petsStatus.Height = 56;
            petsValidate = MakeButton("Valider", async (s, e) => await Validate(), true, 130);
            petsCancel = MakeButton("Annuler", async (s, e) => await Leave(), false, 100);
            // Ordre d'ancrage : le dernier ajouté prend le bord en premier.
            page.Controls.Add(bagArea); page.Controls.Add(kamasBar); page.Controls.Add(columns); page.Controls.Add(petsHeading);
            page.Controls.Add(petsStatus); page.Controls.Add(BotUi.Actions(petsValidate, petsCancel));
            countdown = new Timer { Interval = 250 };
            countdown.Tick += (s, e) => UpdateButtons();
            return page;
        }

        private static Control Column(string title, Padding margin, out Label heading, out ExchangeItemGrid grid, out Label kamas)
        {
            var area = new Panel { Dock = DockStyle.Fill, Margin = margin, BackColor = BotUi.Paper };
            heading = MakeLabel(title, 9, true); heading.Dock = DockStyle.Top; heading.Height = 22; heading.AutoEllipsis = true;
            grid = new ExchangeItemGrid { Dock = DockStyle.Fill, AccessibleName = title };
            kamas = MakeLabel("0 kamas", 9); kamas.Dock = DockStyle.Bottom; kamas.Height = 24; kamas.TextAlign = ContentAlignment.MiddleLeft;
            area.Controls.Add(grid); area.Controls.Add(kamas); area.Controls.Add(heading);
            return area;
        }

        protected override void OnBind(GameClass game)
        {
            bound = game.Interactions?.Exchanges?.Get<PetsExchange>();
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
            countdown?.Stop();
        }

        /// <summary>Changement annoncé sur le fil réseau : ramené sur le fil de l'interface par la fenêtre de jeu.</summary>
        private void OnServerChanged() => Post(() =>
        {
            bool open = IsServerWindowOpen;
            if (open && !wasOpen)
            {
                if (petsKamas != null) petsKamas.Value = 0;
                RequestShow();
            }
            else if (!open && wasOpen) RaiseClosed();
            wasOpen = open;
            RefreshView();
        });

        private void OnInventoryChanged(bool changed) { if (changed) Post(RefreshView); }

        private bool Post(Action action) => ExchangePanel.PostToForm(Host, () => { if (!IsDisposed) action(); });

        public override void RefreshView()
        {
            PetsExchange exchange = Exchange;
            if (exchange == null || localGrid == null) return;
            string npc = exchange.NpcName;
            petsHeading.Text = !exchange.IsOpen ? "Familiers"
                : (exchange.Service == PetsService.Resurrection ? "Résurrection chez " : exchange.Service == PetsService.Keeping ? "Garde chez " : "Familiers chez ") + npc;
            localTitle.Text = "Vous" + (exchange.LocalReady ? " · prêt" : string.Empty);
            localTitle.ForeColor = exchange.LocalReady ? BotUi.Olive : BotUi.Ink;
            distantTitle.Text = (exchange.IsOpen && npc.Length > 0 ? npc : "PNJ") + (exchange.DistantReady ? " · prêt" : string.Empty);
            distantTitle.ForeColor = exchange.DistantReady ? BotUi.Olive : BotUi.Ink;
            var offers = exchange.LocalItems;
            localGrid.SetItems(offers); localGrid.Ready = exchange.LocalReady;
            distantGrid.SetItems(exchange.DistantItems); distantGrid.Ready = exchange.DistantReady;
            localKamas.Text = Kamas(exchange.LocalKamas);
            distantKamas.Text = Kamas(exchange.DistantKamas);

            uint selected = petsBag.SelectedItems.Count == 0 ? 0u : (uint)petsBag.SelectedItems[0].Tag;
            petsBag.BeginUpdate(); petsBag.Items.Clear();
            var bag = Game.character?.Inventory?.Objets;
            if (bag != null)
                foreach (InventoryObjects item in bag.Where(entry => entry != null && !entry.IsEquipped()).OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    int available = item.Qua - offers.Where(offer => offer.Id == item.Inventory_ID).Sum(offer => offer.Quantity);
                    if (available <= 0) continue;
                    var row = petsBag.Items.Add(item.Name); row.SubItems.Add(available.ToString(CultureInfo.CurrentCulture)); row.Tag = item.Inventory_ID;
                    if (item.Inventory_ID == selected) row.Selected = true;
                }
            petsBag.EndUpdate();

            string message = exchange.LastMessage;
            if (exchange.IsOpen)
                petsStatus.Text = exchange.ServiceHint + Environment.NewLine
                    + (exchange.LocalReady ? "Vous avez validé. " : string.Empty)
                    + (exchange.DistantReady ? npc + " accepte la proposition. " : string.Empty) + message;
            else petsStatus.Text = IdleText + (message.Length > 0 ? " " + message : string.Empty);
            UpdateButtons();
        }

        private static string Kamas(long value) => value.ToString("N0", CultureInfo.CurrentCulture) + " kamas";

        private void UpdateButtons()
        {
            PetsExchange exchange = Exchange;
            if (exchange == null || petsValidate == null || IsDisposed) return;
            bool open = Connected && exchange.IsOpen;
            InventoryObjects bagItem = InventoryPanel.SelectedItem(Game, petsBag);
            ExchangeItem offer = localGrid.SelectedItem;
            int limit = Math.Max(bagItem?.Qua ?? 1, offer?.Quantity ?? 1);
            petsQuantity.Maximum = Math.Max(1, limit);
            petsAdd.Enabled = open && bagItem != null;
            petsRemove.Enabled = open && offer != null;
            petsKamas.Maximum = Math.Max(0, Game.character?.Kamas ?? 0);
            petsKamasSend.Enabled = open;
            TimeSpan wait = exchange.ValidationWait;
            bool waiting = open && wait > TimeSpan.Zero;
            petsValidate.Enabled = open && !waiting;
            petsValidate.Text = waiting ? "Valider (" + Math.Ceiling(wait.TotalSeconds).ToString(CultureInfo.InvariantCulture) + ")" : "Valider";
            petsCancel.Enabled = open;
            if (waiting) { if (!countdown.Enabled) countdown.Start(); }
            else countdown.Stop();
        }

        protected internal override bool OnUserClose()
        {
            if (!IsServerWindowOpen) return true;
            _ = Leave();
            return false;
        }

        private Task AddSelected()
        {
            PetsExchange exchange = Exchange;
            InventoryObjects item = InventoryPanel.SelectedItem(Game, petsBag);
            if (exchange == null || item == null) return Task.CompletedTask;
            int offered = exchange.LocalItems.Where(entry => entry.Id == item.Inventory_ID).Sum(entry => entry.Quantity);
            int quantity = Math.Min((int)petsQuantity.Value, Math.Max(1, item.Qua - offered));
            return ReportAsync(() => exchange.AddItemAsync(item.Inventory_ID, quantity));
        }

        private Task RemoveSelected()
        {
            PetsExchange exchange = Exchange;
            ExchangeItem offer = localGrid.SelectedItem;
            if (exchange == null || offer == null) return Task.CompletedTask;
            int quantity = Math.Min((int)petsQuantity.Value, offer.Quantity);
            return ReportAsync(() => exchange.RemoveItemAsync(offer.Id, quantity));
        }

        private Task SendKamas()
        {
            PetsExchange exchange = Exchange;
            if (exchange == null) return Task.CompletedTask;
            long kamas = (long)petsKamas.Value;
            return ReportAsync(() => exchange.SetKamasAsync(kamas));
        }

        private Task Validate()
        {
            PetsExchange exchange = Exchange;
            return exchange == null ? Task.CompletedTask : ReportAsync(exchange.ValidateAsync);
        }

        private Task Leave()
        {
            PetsExchange exchange = Exchange;
            return exchange == null ? Task.CompletedTask : ReportAsync(exchange.LeaveAsync);
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing) return;
            if (countdown != null) { countdown.Stop(); countdown.Dispose(); countdown = null; }
        }
    }
}
