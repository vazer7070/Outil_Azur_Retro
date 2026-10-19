using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Exchanges;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Jobs;
using Tool_BotProtocol.Game.Perso.Inventory;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>
    /// Artisanat sécurisé (interface <c>SecureCraft</c> du client 1.34, lot X5) : les ingrédients du personnage et ceux de l'autre joueur
    /// côte à côte (cadre olive quand un côté a validé), la recette reconnue ou l'objet obtenu, le paiement et la prime si réussite
    /// (objets et kamas ; seul le client les propose), le sac dessous. S'ouvre sur <c>ECK12</c> (artisan) ou <c>ECK13</c> (client), se ferme
    /// sur <c>EV</c>. « Poser » envoie <c>EMO+</c>, « Retirer » <c>EMO-</c>, « Payer » / « Prime » <c>EP&lt;1|2&gt;G</c> ou <c>EP&lt;1|2&gt;O±</c>,
    /// « Combiner » <c>EK</c> (grisé 3 s après chaque modification, confirmation <c>WRONG_CRAFT_CONFIRM</c> sans recette reconnue),
    /// « Fermer » <c>EV</c>. Les recettes de la compétence sont lues hors du thread de l'interface.
    /// </summary>
    public sealed class CraftSecurePanel : GamePanel
    {
        private const string IdleText = "Aucun artisanat sécurisé en cours. La demande d'un artisan ou d'un client s'affiche dans une boîte Oui / Non / Ignorer ; la fenêtre s'ouvre lorsque le serveur l'annonce (ECK12 ou ECK13).";
        private Label heading, subheading, localTitle, distantTitle, expectedText, payKamasText, craftStatus;
        private ExchangeItemGrid localGrid, distantGrid;
        private ListView payList, bagList;
        private NumericUpDown placeQuantity, payKamas;
        private Control placeButton, removeButton, payItemButton, payIfSuccessItemButton, payRemoveButton, payKamasButton, payIfSuccessKamasButton, combineButton, leaveButton;
        private FlowLayoutPanel payBar;
        private Timer countdown;
        private bool wasOpen;
        private InventoryClass inventory;
        private CraftSecureExchange bound;
        private Form combineDialog;
        private string recipesKey = string.Empty;
        private int recipesRequest;
        private bool recipesReady;
        private IReadOnlyList<Recipe> recipes = new Recipe[0];
        private Recipe expectedRecipe;

        public override string Title => "Artisanat sécurisé";
        public override Image Icon
        {
            get
            {
                Jobs job = bound?.Job;
                return (job == null ? null : JobIcons.TryGet(job.ID)) ?? ClientAssets.Icon("icone-caracteristiques", 24);
            }
        }
        public override bool IsModal => true;
        public override bool IsServerWindowOpen => Exchange?.IsOpen == true;

        /// <summary>Boîte <c>WRONG_CRAFT_CONFIRM</c> affichée, ou <c>null</c>.</summary>
        public Form CombineDialog => combineDialog;
        /// <summary>Recettes de la compétence qui tiennent dans les cases, une fois lues.</summary>
        public IReadOnlyList<Recipe> KnownRecipes => recipes;
        public bool RecipesReady => recipesReady;
        /// <summary>Recette reconnue d'après tous les ingrédients posés, ou <c>null</c>.</summary>
        public Recipe ExpectedRecipe => expectedRecipe;

        private CraftSecureExchange Exchange => Game?.Interactions?.Exchanges?.Get<CraftSecureExchange>();
        private static string Text(string key, string fallback, params string[] args) => JobCatalog.Text(key, fallback, args);

        protected override Control CreateView()
        {
            var page = Page();
            heading = MakeLabel("Artisanat sécurisé", 10, true); heading.Dock = DockStyle.Top; heading.Height = 21; heading.AutoEllipsis = true; heading.Name = "secure-heading";
            subheading = MakeLabel(string.Empty, 8); subheading.Dock = DockStyle.Top; subheading.Height = 18; subheading.ForeColor = BotUi.Muted; subheading.AutoEllipsis = true;

            // Ingrédients des deux côtés, comme les deux grilles de SecureCraft.
            var columns = new TableLayoutPanel { Dock = DockStyle.Top, Height = 118, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            columns.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            columns.Controls.Add(Column("Vos ingrédients", new Padding(0, 0, 4, 0), out localTitle, out localGrid), 0, 0);
            columns.Controls.Add(Column("Autre joueur", new Padding(4, 0, 0, 0), out distantTitle, out distantGrid), 1, 0);
            localGrid.EmptyText = "Posez des ingrédients de votre sac.";
            distantGrid.EmptyText = "Rien de posé pour l'instant.";
            localGrid.SelectionChanged += (s, e) => UpdateButtons();
            expectedText = MakeLabel(string.Empty, 8); expectedText.Dock = DockStyle.Top; expectedText.Height = 18; expectedText.AutoEllipsis = true; expectedText.Name = "secure-expected";

            // Paiement et prime si réussite : liste des objets proposés, kamas, saisie du client.
            var payArea = new Panel { Dock = DockStyle.Top, Height = 118, Margin = new Padding(0) };
            payKamasText = MakeLabel(string.Empty, 9, true); payKamasText.Dock = DockStyle.Top; payKamasText.Height = 20; payKamasText.AutoEllipsis = true;
            payList = MakeList(8, "Objet", "Qté", "Au titre de");
            payList.Columns[0].Width = 170; payList.Columns[1].Width = 45; payList.Columns[2].Width = 110;
            payList.SelectedIndexChanged += (s, e) => UpdateButtons();
            payKamas = InventoryPanel.Quantity(); payKamas.Minimum = 0; payKamas.Maximum = 0; payKamas.Width = 100; payKamas.ThousandsSeparator = true;
            payKamasButton = MakeButton(Text("PAY", "Paiement"), async (s, e) => await SendKamas(CraftPayment.Pay), false, 90);
            payIfSuccessKamasButton = MakeButton(Text("GRANT_IF_SUCCESS", "Prime si réussite"), async (s, e) => await SendKamas(CraftPayment.PayIfSuccess), false, 120);
            var kamasLabel = InventoryPanel.QuantityLabel(); kamasLabel.Text = "Kamas";
            payBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, MinimumSize = new Size(0, 36), WrapContents = true, Padding = new Padding(0, 2, 0, 0) };
            payBar.Controls.AddRange(new[] { kamasLabel, (Control)payKamas, payKamasButton, payIfSuccessKamasButton });
            payArea.Controls.Add(payList); payArea.Controls.Add(payBar); payArea.Controls.Add(payKamasText);

            // Sac : poser un ingrédient, le proposer en paiement ou en prime (client), retirer.
            var bagArea = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            var bagTitle = MakeLabel("Votre sac", 9, true); bagTitle.Dock = DockStyle.Top; bagTitle.Height = 22;
            bagList = MakeList(9, "Objet", "Qté");
            bagList.Columns[0].Width = 250; bagList.Columns[1].Width = 75;
            bagList.SelectedIndexChanged += (s, e) => UpdateButtons();
            placeQuantity = InventoryPanel.Quantity();
            placeButton = MakeButton("Poser", async (s, e) => await PlaceSelected(), false, 80);
            removeButton = MakeButton("Retirer", async (s, e) => await RemoveSelected(), false, 80);
            payItemButton = MakeButton("Payer avec", async (s, e) => await OfferSelected(CraftPayment.Pay), false, 95);
            payIfSuccessItemButton = MakeButton("Prime avec", async (s, e) => await OfferSelected(CraftPayment.PayIfSuccess), false, 95);
            payRemoveButton = MakeButton("Reprendre", async (s, e) => await TakeBackSelected(), false, 90);
            bagArea.Controls.Add(bagList); bagArea.Controls.Add(bagTitle);
            bagArea.Controls.Add(BotUi.Actions(placeButton, removeButton, InventoryPanel.QuantityLabel(), placeQuantity, payItemButton, payIfSuccessItemButton, payRemoveButton));

            craftStatus = MakeStatus(IdleText); craftStatus.Height = 44; craftStatus.Name = "secure-status";
            combineButton = MakeButton(Text("COMBINE", "Combiner"), async (s, e) => await Combine(), true, 130);
            leaveButton = MakeButton("Fermer", async (s, e) => await Leave(), false, 100);
            // Ordre d'ancrage : le dernier ajouté prend le bord en premier.
            page.Controls.Add(bagArea); page.Controls.Add(payArea); page.Controls.Add(expectedText); page.Controls.Add(columns);
            page.Controls.Add(subheading); page.Controls.Add(heading);
            page.Controls.Add(craftStatus); page.Controls.Add(BotUi.Actions(combineButton, leaveButton));
            countdown = new Timer { Interval = 250 };
            countdown.Tick += (s, e) => UpdateButtons();
            return page;
        }

        private static Control Column(string title, Padding margin, out Label heading, out ExchangeItemGrid grid)
        {
            var area = new Panel { Dock = DockStyle.Fill, Margin = margin, BackColor = BotUi.Paper };
            heading = MakeLabel(title, 9, true); heading.Dock = DockStyle.Top; heading.Height = 22; heading.AutoEllipsis = true;
            grid = new ExchangeItemGrid { Dock = DockStyle.Fill, AccessibleName = title };
            area.Controls.Add(grid); area.Controls.Add(heading);
            return area;
        }

        protected override void OnBind(GameClass game)
        {
            bound = game.Interactions?.Exchanges?.Get<CraftSecureExchange>();
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
            CloseCombineDialog();
        }

        /// <summary>Changement annoncé sur le fil réseau : ramené sur le fil de l'interface par la fenêtre de jeu.</summary>
        private void OnServerChanged() => Post(() =>
        {
            bool open = IsServerWindowOpen;
            if (open && !wasOpen)
            {
                if (payKamas != null) payKamas.Value = 0;
                RequestShow();
            }
            else if (!open && wasOpen) { CloseCombineDialog(); RaiseClosed(); }
            wasOpen = open;
            RefreshView();
        });

        private void OnInventoryChanged(bool changed) { if (changed) Post(RefreshView); }

        private bool Post(Action action) => ExchangePanel.PostToForm(Host, () => { if (!IsDisposed) action(); });

        public override void RefreshView()
        {
            CraftSecureExchange exchange = Exchange;
            if (exchange == null || localGrid == null) return;
            bool open = exchange.IsOpen;
            string partner = exchange.PartnerName.Length > 0 ? exchange.PartnerName : "Autre joueur";
            LoadRecipes(exchange);
            heading.Text = !open ? "Artisanat sécurisé"
                : (exchange.SkillName.Length > 0 ? exchange.SkillName : "Artisanat sécurisé") + (exchange.Slots > 0 ? " · " + exchange.Slots + " " + JobCatalog.SlotWord(exchange.Slots) : string.Empty);
            subheading.Text = !open ? string.Empty
                : exchange.IsClient ? "Vous êtes le client de l'artisan " + partner + " : l'objet obtenu ira dans votre sac."
                : "Vous êtes l'artisan de " + partner + (exchange.Job != null ? " (" + exchange.Job.name + ", niveau " + exchange.Job.Level + ")" : string.Empty) + ".";
            localTitle.Text = "Vos ingrédients" + (exchange.LocalReady ? " · prêt" : string.Empty);
            localTitle.ForeColor = exchange.LocalReady ? BotUi.Olive : BotUi.Ink;
            distantTitle.Text = (open ? partner : "Autre joueur") + (exchange.DistantReady ? " · prêt" : string.Empty);
            distantTitle.ForeColor = exchange.DistantReady ? BotUi.Olive : BotUi.Ink;
            var placed = exchange.LocalIngredients;
            localGrid.SetItems(placed); localGrid.Ready = exchange.LocalReady;
            distantGrid.SetItems(exchange.DistantIngredients); distantGrid.Ready = exchange.DistantReady;

            expectedRecipe = open ? JobCatalog.Match(exchange.PlacedByTemplate(), recipes) : null;
            if (!open) expectedText.Text = string.Empty;
            else if (exchange.LastOutcome == CraftOutcome.Success && exchange.LastResultTemplateId > 0 && exchange.IngredientCount == 0)
                expectedText.Text = Text("CRAFTED_ITEM", "Objet obtenu") + " : " + JobCatalog.ItemName(exchange.LastResultTemplateId);
            else if (expectedRecipe != null) expectedText.Text = "Recette reconnue : " + expectedRecipe.Name;
            else if (exchange.IngredientCount == 0) expectedText.Text = recipesReady ? recipes.Count + " recette(s) connue(s) pour ces cases." : "Lecture des recettes…";
            else expectedText.Text = "Aucune recette connue ne correspond : Combiner demandera confirmation.";
            expectedText.ForeColor = expectedRecipe != null ? BotUi.Olive : BotUi.Muted;

            payKamasText.Text = Text("PAY", "Paiement") + " : " + Kamas(exchange.PayKamas) + " · " + Text("GRANT_IF_SUCCESS", "Prime si réussite") + " : " + Kamas(exchange.PayIfSuccessKamas);
            var offers = exchange.PayItems.Select(item => new { Item = item, Type = CraftPayment.Pay }).Concat(exchange.PayIfSuccessItems.Select(item => new { Item = item, Type = CraftPayment.PayIfSuccess })).ToArray();
            object selectedOffer = payList.SelectedItems.Count == 0 ? null : payList.SelectedItems[0].Tag;
            payList.BeginUpdate(); payList.Items.Clear();
            foreach (var offer in offers)
            {
                var row = payList.Items.Add(offer.Item.Name); row.SubItems.Add(offer.Item.Quantity.ToString(CultureInfo.CurrentCulture));
                row.SubItems.Add(offer.Type == CraftPayment.Pay ? Text("PAY", "Paiement") : Text("GRANT_IF_SUCCESS", "Prime si réussite"));
                row.Tag = new Tuple<uint, CraftPayment>(offer.Item.Id, offer.Type);
                if (Equals(row.Tag, selectedOffer)) row.Selected = true;
            }
            payList.EndUpdate();
            payBar.Visible = exchange.IsClient;

            uint selected = bagList.SelectedItems.Count == 0 ? 0u : (uint)bagList.SelectedItems[0].Tag;
            bagList.BeginUpdate(); bagList.Items.Clear();
            var bag = Game.character?.Inventory?.Objets;
            if (bag != null)
                foreach (InventoryObjects item in bag.Where(entry => entry != null && !entry.IsEquipped()).OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    int available = item.Qua - exchange.PlacedQuantity(item.Inventory_ID) - exchange.OfferedQuantity(item.Inventory_ID);
                    if (available <= 0) continue;
                    var row = bagList.Items.Add(item.Name); row.SubItems.Add(available.ToString(CultureInfo.CurrentCulture)); row.Tag = item.Inventory_ID;
                    if (item.Inventory_ID == selected) row.Selected = true;
                }
            bagList.EndUpdate();

            string message = exchange.LastMessage;
            if (open)
                craftStatus.Text = (exchange.LocalReady ? "Vous avez validé. " : string.Empty) + (exchange.DistantReady ? partner + " a validé. " : string.Empty)
                    + (exchange.IsCombinePending ? "Fabrication en cours… " : string.Empty) + message;
            else craftStatus.Text = IdleText + (message.Length > 0 ? " " + message : string.Empty);
            UpdateButtons();
        }

        /// <summary>Recettes de la compétence (textes du client) lues une fois par ouverture, hors du thread de l'interface.</summary>
        private void LoadRecipes(CraftSecureExchange exchange)
        {
            string key = exchange.IsOpen && exchange.SkillId > 0 ? exchange.SkillId + "/" + exchange.Slots : string.Empty;
            if (key == recipesKey) return;
            recipesKey = key;
            recipes = new Recipe[0]; recipesReady = false;
            int request = ++recipesRequest;
            if (key.Length == 0) return;
            short skill = exchange.SkillId; int slots = exchange.Slots > 0 ? exchange.Slots : int.MaxValue;
            Task.Run(() =>
            {
                IReadOnlyList<Recipe> found;
                try { found = JobCatalog.Recipes(skill, slots); }
                catch (Exception error) { Account?.Logger?.LogException("ARTISANAT", error); found = new Recipe[0]; }
                Post(() => { if (request != recipesRequest) return; recipes = found; recipesReady = true; RefreshView(); });
            });
        }

        private static string Kamas(long value) => value.ToString("N0", CultureInfo.CurrentCulture) + " kamas";

        private void UpdateButtons()
        {
            CraftSecureExchange exchange = Exchange;
            if (exchange == null || combineButton == null || IsDisposed) return;
            bool open = Connected && exchange.IsOpen, editable = open && !exchange.IsCombinePending, client = editable && exchange.IsClient;
            InventoryObjects bagItem = InventoryPanel.SelectedItem(Game, bagList);
            ExchangeItem placed = localGrid.SelectedItem;
            var offer = payList.SelectedItems.Count == 0 ? null : payList.SelectedItems[0].Tag as Tuple<uint, CraftPayment>;
            int bagAvailable = bagItem == null ? 0 : bagItem.Qua - exchange.PlacedQuantity(bagItem.Inventory_ID) - exchange.OfferedQuantity(bagItem.Inventory_ID);
            int offered = offer == null ? 0 : (offer.Item2 == CraftPayment.Pay ? exchange.PayItems : exchange.PayIfSuccessItems).Where(item => item.Id == offer.Item1).Sum(item => item.Quantity);
            placeQuantity.Maximum = Math.Max(1, Math.Max(bagAvailable, Math.Max(placed?.Quantity ?? 1, offered)));
            placeButton.Enabled = editable && bagItem != null && bagAvailable > 0;
            removeButton.Enabled = editable && placed != null;
            payItemButton.Enabled = client && bagItem != null && bagAvailable > 0;
            payIfSuccessItemButton.Enabled = client && bagItem != null && bagAvailable > 0;
            payRemoveButton.Enabled = client && offer != null;
            payItemButton.Visible = exchange.IsClient; payIfSuccessItemButton.Visible = exchange.IsClient; payRemoveButton.Visible = exchange.IsClient;
            payKamas.Maximum = Math.Max(0, Game.character?.Kamas ?? 0);
            payKamasButton.Enabled = client; payIfSuccessKamasButton.Enabled = client;
            TimeSpan wait = exchange.ValidationWait;
            bool waiting = open && wait > TimeSpan.Zero;
            combineButton.Enabled = open && !waiting && !exchange.IsCombinePending && !exchange.IsValidatePending && (exchange.LocalReady || exchange.IngredientCount > 0);
            string combine = Text("COMBINE", "Combiner");
            combineButton.Text = waiting ? combine + " (" + Math.Ceiling(wait.TotalSeconds).ToString(CultureInfo.InvariantCulture) + ")" : exchange.LocalReady ? "Annuler la validation" : combine;
            leaveButton.Enabled = open;
            if (waiting) { if (!countdown.Enabled) countdown.Start(); }
            else countdown.Stop();
        }

        protected internal override bool OnUserClose()
        {
            if (!IsServerWindowOpen) return true;
            _ = Leave();
            return false;
        }

        private Task PlaceSelected()
        {
            CraftSecureExchange exchange = Exchange;
            InventoryObjects item = InventoryPanel.SelectedItem(Game, bagList);
            if (exchange == null || item == null) return Task.CompletedTask;
            int available = Math.Max(1, item.Qua - exchange.PlacedQuantity(item.Inventory_ID) - exchange.OfferedQuantity(item.Inventory_ID));
            int quantity = Math.Min((int)placeQuantity.Value, available);
            return ReportAsync(() => exchange.AddIngredientAsync(item.Inventory_ID, quantity));
        }

        private Task RemoveSelected()
        {
            CraftSecureExchange exchange = Exchange;
            ExchangeItem placed = localGrid.SelectedItem;
            if (exchange == null || placed == null) return Task.CompletedTask;
            int quantity = Math.Min((int)placeQuantity.Value, placed.Quantity);
            return ReportAsync(() => exchange.RemoveIngredientAsync(placed.Id, quantity));
        }

        private Task OfferSelected(CraftPayment type)
        {
            CraftSecureExchange exchange = Exchange;
            InventoryObjects item = InventoryPanel.SelectedItem(Game, bagList);
            if (exchange == null || item == null) return Task.CompletedTask;
            int available = Math.Max(1, item.Qua - exchange.PlacedQuantity(item.Inventory_ID) - exchange.OfferedQuantity(item.Inventory_ID));
            int quantity = Math.Min((int)placeQuantity.Value, available);
            return ReportAsync(() => exchange.AddPayItemAsync(item.Inventory_ID, quantity, type));
        }

        private Task TakeBackSelected()
        {
            CraftSecureExchange exchange = Exchange;
            var offer = payList.SelectedItems.Count == 0 ? null : payList.SelectedItems[0].Tag as Tuple<uint, CraftPayment>;
            if (exchange == null || offer == null) return Task.CompletedTask;
            int offered = (offer.Item2 == CraftPayment.Pay ? exchange.PayItems : exchange.PayIfSuccessItems).Where(item => item.Id == offer.Item1).Sum(item => item.Quantity);
            int quantity = Math.Min((int)placeQuantity.Value, Math.Max(1, offered));
            return ReportAsync(() => exchange.RemovePayItemAsync(offer.Item1, quantity, offer.Item2));
        }

        private Task SendKamas(CraftPayment type)
        {
            CraftSecureExchange exchange = Exchange;
            if (exchange == null) return Task.CompletedTask;
            long kamas = (long)payKamas.Value;
            return ReportAsync(() => exchange.SetPayKamasAsync(kamas, type));
        }

        /// <summary>« Combiner » : confirmation <c>WRONG_CRAFT_CONFIRM</c> si aucune recette connue ne correspond (sauf pour retirer sa validation), puis <c>EK</c>.</summary>
        private async Task Combine()
        {
            CraftSecureExchange exchange = Exchange;
            if (exchange == null || !exchange.IsOpen) return;
            try
            {
                if (!exchange.LocalReady && recipesReady && JobCatalog.Match(exchange.PlacedByTemplate(), recipes) == null)
                {
                    CloseCombineDialog();
                    Form[] before = BotDialogs.OpenDialogs.ToArray();
                    Task<BotDialogResult> question = BotDialogs.AskYesNoAsync(Host, Text("CRAFT", "Artisanat"),
                        Text("WRONG_CRAFT_CONFIRM", "Ces ingrédients ne correspondent à aucune recette connue. Combiner quand même ?"));
                    combineDialog = BotDialogs.OpenDialogs.Except(before).LastOrDefault();
                    BotDialogResult answer = await question;
                    combineDialog = null;
                    if (answer != BotDialogResult.Yes || IsDisposed || !exchange.IsOpen) return;
                }
                await ReportAsync(exchange.CombineAsync);
            }
            catch (Exception error) { Feedback(error.Message); }
        }

        private void CloseCombineDialog()
        {
            Form dialog = combineDialog;
            combineDialog = null;
            if (dialog != null && !dialog.IsDisposed) dialog.Close();
        }

        private Task Leave()
        {
            CraftSecureExchange exchange = Exchange;
            return exchange == null ? Task.CompletedTask : ReportAsync(exchange.LeaveAsync);
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing) return;
            if (countdown != null) { countdown.Stop(); countdown.Dispose(); countdown = null; }
        }
    }
}
