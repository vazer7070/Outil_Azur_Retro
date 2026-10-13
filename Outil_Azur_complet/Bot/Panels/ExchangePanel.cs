using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Exchanges;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Perso.Inventory;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>
    /// Échange avec un joueur ou un PNJ (interface <c>Exchange</c> du client 1.34) : sa proposition et celle de l'autre côte
    /// à côte, kamas et état « prêt » de chaque côté, sac du personnage dessous. Le volet s'ouvre sur <c>ECK1</c>/<c>ECK2</c>
    /// (ou pendant l'attente d'une demande envoyée) et se ferme sur <c>EV</c>. Une demande reçue (<c>ERK</c>) est posée dans une
    /// boîte Oui / Non / Ignorer : Oui envoie <c>EA</c>, Non <c>EV</c>, Ignorer <c>EV</c> et ignore le joueur pour la session.
    /// « Valider » (<c>EK</c>) reste grisé 3 secondes après chaque modification, comme le bouton du client.
    /// </summary>
    public sealed class ExchangePanel : GamePanel
    {
        private const string Reference = "ÉCHANGE";
        private const string IdleText = "Aucun échange en cours. Une demande reçue s'affiche dans une boîte Oui / Non / Ignorer ; l'échange s'ouvre lorsque le serveur l'annonce (ECK1).";
        private Label exchangeHeading, localTitle, distantTitle, localKamas, distantKamas, exchangeStatus;
        private ExchangeItemGrid localGrid, distantGrid;
        private ListView exchangeBag;
        private NumericUpDown exchangeQuantity, exchangeKamas;
        private Control exchangeAdd, exchangeRemove, exchangeKamasSend, exchangeValidate, exchangeCancel;
        private Timer countdown;
        private bool wasOpen;
        private InventoryClass inventory;
        private PlayerExchange bound;
        private Form requestDialog;
        private ExchangeRequest requestShown;

        public override string Title => "Échange";
        public override Image Icon => ClientAssets.Icon("icone-amis", 24);
        public override bool IsModal => true;
        public override bool IsServerWindowOpen
        {
            get
            {
                PlayerExchange exchange = Game?.Interactions?.Exchange;
                return exchange != null && (exchange.IsOpen || exchange.PendingRequest?.Outgoing == true);
            }
        }
        /// <summary>Boîte Oui / Non / Ignorer de la demande reçue encore affichée, sinon <c>null</c>.</summary>
        public Form RequestDialog => requestDialog;

        protected override Control CreateView()
        {
            var page = Page();
            exchangeHeading = MakeLabel("Échange", 10, true); exchangeHeading.Dock = DockStyle.Top; exchangeHeading.Height = 24; exchangeHeading.AutoEllipsis = true;

            // Deux colonnes comme la fenêtre du client : sa proposition à gauche, celle de l'autre à droite.
            var columns = new TableLayoutPanel { Dock = DockStyle.Top, Height = 200, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            columns.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            columns.Controls.Add(Column("Vous", new Padding(0, 0, 4, 0), out localTitle, out localGrid, out localKamas), 0, 0);
            columns.Controls.Add(Column("Autre joueur", new Padding(4, 0, 0, 0), out distantTitle, out distantGrid, out distantKamas), 1, 0);
            localGrid.EmptyText = "Proposez des objets de votre sac.";
            distantGrid.EmptyText = "Rien de proposé pour l'instant.";
            localGrid.SelectionChanged += (s, e) => UpdateButtons();

            exchangeKamas = InventoryPanel.Quantity(); exchangeKamas.Minimum = 0; exchangeKamas.Maximum = 0; exchangeKamas.Width = 110;
            exchangeKamas.ThousandsSeparator = true;
            exchangeKamasSend = MakeButton("Proposer les kamas", async (s, e) => await SendKamas(), false, 150);
            var kamasLabel = InventoryPanel.QuantityLabel(); kamasLabel.Text = "Kamas";
            var kamasBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, MinimumSize = new Size(0, 43), WrapContents = true,
                Padding = new Padding(0, 7, 0, 0) };
            kamasBar.Controls.AddRange(new[] { kamasLabel, (Control)exchangeKamas, exchangeKamasSend });

            var bagArea = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            var bagTitle = MakeLabel("Votre sac", 9, true); bagTitle.Dock = DockStyle.Top; bagTitle.Height = 22;
            exchangeBag = MakeList(9, "Objet", "Qté");
            exchangeBag.Columns[0].Width = 250; exchangeBag.Columns[1].Width = 75;
            exchangeBag.SelectedIndexChanged += (s, e) => UpdateButtons();
            exchangeQuantity = InventoryPanel.Quantity();
            exchangeAdd = MakeButton("Proposer", async (s, e) => await AddSelected(), false, 100);
            exchangeRemove = MakeButton("Retirer", async (s, e) => await RemoveSelected(), false, 100);
            bagArea.Controls.Add(exchangeBag); bagArea.Controls.Add(bagTitle);
            bagArea.Controls.Add(BotUi.Actions(exchangeAdd, exchangeRemove, InventoryPanel.QuantityLabel(), exchangeQuantity));

            exchangeStatus = MakeStatus(IdleText);
            exchangeValidate = MakeButton("Valider", async (s, e) => await Validate(), true, 130);
            exchangeCancel = MakeButton("Annuler", async (s, e) => await Leave(), false, 100);
            // Ordre d'ancrage : le dernier ajouté prend le bord en premier.
            page.Controls.Add(bagArea); page.Controls.Add(kamasBar); page.Controls.Add(columns); page.Controls.Add(exchangeHeading);
            page.Controls.Add(exchangeStatus); page.Controls.Add(BotUi.Actions(exchangeValidate, exchangeCancel));
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
            bound = game.Interactions?.Exchange;
            if (bound != null) { bound.Changed += OnServerChanged; bound.RequestReceived += OnRequestReceived; }
            inventory = game.character?.Inventory;
            if (inventory != null) inventory.RefreshInventory += OnInventoryChanged;
        }

        protected override void OnUnbind(GameClass game)
        {
            if (bound != null) { bound.Changed -= OnServerChanged; bound.RequestReceived -= OnRequestReceived; }
            bound = null;
            if (inventory != null) inventory.RefreshInventory -= OnInventoryChanged;
            inventory = null;
            wasOpen = false;
            countdown?.Stop();
            CloseRequestDialog();
        }

        /// <summary>
        /// Demande reçue (fil réseau) : prise en charge seulement si le tiroir peut afficher la question ; sinon le modèle
        /// la refuse lui-même. La réponse n'est envoyée qu'après le choix du joueur.
        /// </summary>
        private void OnRequestReceived(object sender, ExchangeRequestEventArgs e)
        {
            PanelHost host = Host;
            var exchange = sender as PlayerExchange;
            if (IsDisposed || exchange == null || e == null || host == null || host.IsDisposed) return;
            ExchangeRequest request = e.Request;
            if (Post(() => AskRequest(exchange, request))) e.Handled = true;
        }

        private async void AskRequest(PlayerExchange exchange, ExchangeRequest request)
        {
            try
            {
                if (IsDisposed || !ReferenceEquals(exchange.PendingRequest, request)) return;
                CloseRequestDialog();
                Form[] before = BotDialogs.OpenDialogs.ToArray();
                Task<BotDialogResult> question = BotDialogs.AskYesNoIgnoreAsync(Host, ExchangeRegistry.Text("EXCHANGE", "Échange"),
                    ExchangeRegistry.Text("A_WANT_EXCHANGE", request.PartnerName + " vous propose un échange.", request.PartnerName));
                requestShown = request;
                requestDialog = BotDialogs.OpenDialogs.Except(before).LastOrDefault();
                BotDialogResult answer = await question;
                if (ReferenceEquals(requestShown, request)) { requestShown = null; requestDialog = null; }
                // Demande annulée entre-temps (EV de l'autre joueur, échange ouvert, déconnexion) : plus rien à répondre.
                if (!ReferenceEquals(exchange.PendingRequest, request)) return;
                Func<Task<InteractionResult>> reply;
                if (answer == BotDialogResult.Yes) reply = exchange.AcceptAsync;
                else if (answer == BotDialogResult.Ignore) reply = exchange.IgnoreAsync;
                else reply = exchange.RefuseAsync;
                if (IsDisposed) await reply();
                else await ReportAsync(reply);
            }
            catch (Exception error) { Account?.Logger?.LogException(Reference, error); }
        }

        private void CloseRequestDialog()
        {
            Form dialog = requestDialog;
            requestDialog = null; requestShown = null;
            if (dialog != null && !dialog.IsDisposed) dialog.Close();
        }

        private void OnServerChanged() => Post(() =>
        {
            bool open = IsServerWindowOpen;
            if (open && !wasOpen)
            {
                if (exchangeKamas != null) exchangeKamas.Value = 0;
                RequestShow();
            }
            else if (!open && wasOpen) RaiseClosed();
            wasOpen = open;
            if (requestShown != null && !ReferenceEquals(Game?.Interactions?.Exchange?.PendingRequest, requestShown)) CloseRequestDialog();
            RefreshView();
        });

        private void OnInventoryChanged(bool changed) { if (changed) Post(RefreshView); }

        /// <summary>
        /// Passe sur le thread de l'interface par la fenêtre de jeu plutôt que par le tiroir : tant que celui-ci n'a jamais
        /// été affiché il n'a pas de poignée, et l'action serait perdue (.NET) ou exécutée sur le fil réseau (Mono).
        /// Renvoie faux quand aucune fenêtre ne peut la recevoir.
        /// </summary>
        internal static bool PostToForm(Control anchor, Action action)
        {
            Form form;
            bool direct;
            try
            {
                form = anchor == null || anchor.IsDisposed ? null : anchor.FindForm();
                if (form == null || form.IsDisposed || !form.IsHandleCreated) return false;
                direct = !form.InvokeRequired;
                if (!direct) form.BeginInvoke(action);
            }
            catch (Exception error) when (error is InvalidOperationException || error is ObjectDisposedException) { return false; }
            if (direct) action();
            return true;
        }

        private bool Post(Action action) => PostToForm(Host, () => { if (!IsDisposed) action(); });

        public override void RefreshView()
        {
            PlayerExchange exchange = Game?.Interactions?.Exchange;
            if (exchange == null || localGrid == null) return;
            ExchangeRequest pending = exchange.PendingRequest;
            string partner = exchange.IsOpen ? exchange.PartnerName : pending?.PartnerName ?? string.Empty;
            exchangeHeading.Text = exchange.IsOpen ? "Échange avec " + partner
                : pending != null && pending.Outgoing ? "Demande envoyée à " + partner : "Échange";
            localTitle.Text = "Vous" + (exchange.LocalReady ? " · prêt" : string.Empty);
            localTitle.ForeColor = exchange.LocalReady ? BotUi.Olive : BotUi.Ink;
            distantTitle.Text = (exchange.IsOpen && partner.Length > 0 ? partner : exchange.PartnerIsNpc ? "PNJ" : "Autre joueur")
                + (exchange.DistantReady ? " · prêt" : string.Empty);
            distantTitle.ForeColor = exchange.DistantReady ? BotUi.Olive : BotUi.Ink;
            var offers = exchange.LocalItems;
            localGrid.SetItems(offers); localGrid.Ready = exchange.LocalReady;
            distantGrid.SetItems(exchange.DistantItems); distantGrid.Ready = exchange.DistantReady;
            localKamas.Text = Kamas(exchange.LocalKamas);
            distantKamas.Text = Kamas(exchange.DistantKamas);

            uint selected = exchangeBag.SelectedItems.Count == 0 ? 0u : (uint)exchangeBag.SelectedItems[0].Tag;
            exchangeBag.BeginUpdate(); exchangeBag.Items.Clear();
            var bag = Game.character?.Inventory?.Objets;
            if (bag != null)
                foreach (InventoryObjects item in bag.Where(entry => entry != null && !entry.IsEquipped()).OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    int available = item.Qua - offers.Where(offer => offer.Id == item.Inventory_ID).Sum(offer => offer.Quantity);
                    if (available <= 0) continue;
                    var row = exchangeBag.Items.Add(item.Name); row.SubItems.Add(available.ToString(CultureInfo.CurrentCulture)); row.Tag = item.Inventory_ID;
                    if (item.Inventory_ID == selected) row.Selected = true;
                }
            exchangeBag.EndUpdate();

            string message = exchange.LastMessage;
            if (exchange.IsOpen)
                exchangeStatus.Text = (exchange.LocalReady ? "Vous avez validé. " : string.Empty)
                    + (exchange.DistantReady ? partner + " a validé. " : string.Empty) + message;
            else if (pending != null && pending.Outgoing)
                exchangeStatus.Text = "En attente de la réponse de " + pending.PartnerName + " ; « Annuler » retire la demande.";
            else exchangeStatus.Text = IdleText + (message.Length > 0 ? " " + message : string.Empty);
            UpdateButtons();
        }

        private static string Kamas(long value) => value.ToString("N0", CultureInfo.CurrentCulture) + " kamas";

        private void UpdateButtons()
        {
            PlayerExchange exchange = Game?.Interactions?.Exchange;
            if (exchange == null || exchangeValidate == null || IsDisposed) return;
            bool open = Connected && exchange.IsOpen;
            InventoryObjects bagItem = InventoryPanel.SelectedItem(Game, exchangeBag);
            ExchangeItem offer = localGrid.SelectedItem;
            int limit = Math.Max(bagItem?.Qua ?? 1, offer?.Quantity ?? 1);
            exchangeQuantity.Maximum = Math.Max(1, limit);
            exchangeAdd.Enabled = open && bagItem != null;
            exchangeRemove.Enabled = open && offer != null;
            long owned = Math.Max(0, Game.character?.Kamas ?? 0);
            exchangeKamas.Maximum = owned;
            exchangeKamasSend.Enabled = open;
            TimeSpan wait = exchange.ValidationWait;
            bool waiting = open && wait > TimeSpan.Zero;
            exchangeValidate.Enabled = open && !waiting;
            exchangeValidate.Text = waiting ? "Valider (" + Math.Ceiling(wait.TotalSeconds).ToString(CultureInfo.InvariantCulture) + ")" : "Valider";
            exchangeCancel.Enabled = Connected && (exchange.IsOpen || exchange.PendingRequest?.Outgoing == true);
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
            PlayerExchange exchange = Game?.Interactions?.Exchange;
            InventoryObjects item = InventoryPanel.SelectedItem(Game, exchangeBag);
            if (exchange == null || item == null) return Task.CompletedTask;
            int offered = exchange.LocalItems.Where(entry => entry.Id == item.Inventory_ID).Sum(entry => entry.Quantity);
            int quantity = Math.Min((int)exchangeQuantity.Value, Math.Max(1, item.Qua - offered));
            return ReportAsync(() => exchange.AddItemAsync(item.Inventory_ID, quantity));
        }

        private Task RemoveSelected()
        {
            PlayerExchange exchange = Game?.Interactions?.Exchange;
            ExchangeItem offer = localGrid.SelectedItem;
            if (exchange == null || offer == null) return Task.CompletedTask;
            int quantity = Math.Min((int)exchangeQuantity.Value, offer.Quantity);
            return ReportAsync(() => exchange.RemoveItemAsync(offer.Id, quantity));
        }

        private Task SendKamas()
        {
            PlayerExchange exchange = Game?.Interactions?.Exchange;
            if (exchange == null) return Task.CompletedTask;
            long kamas = (long)exchangeKamas.Value;
            return ReportAsync(() => exchange.SetKamasAsync(kamas));
        }

        private Task Validate()
        {
            PlayerExchange exchange = Game?.Interactions?.Exchange;
            return exchange == null ? Task.CompletedTask : ReportAsync(exchange.ValidateAsync);
        }

        private Task Leave()
        {
            PlayerExchange exchange = Game?.Interactions?.Exchange;
            return exchange == null ? Task.CompletedTask : ReportAsync(exchange.LeaveAsync);
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing) return;
            if (countdown != null) { countdown.Stop(); countdown.Dispose(); countdown = null; }
            CloseRequestDialog();
        }
    }
}
