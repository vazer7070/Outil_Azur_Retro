using System;
using System.Drawing;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Exchanges;
using Tool_BotProtocol.Game.Guildes;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>
    /// Contenu d'un percepteur (interface <c>TaxCollectorStorage</c> du client 1.34, lot F3) : objets en grille et kamas. S'ouvre sur
    /// <c>ECK8|&lt;id&gt;</c> (réponse à « Collecter », <c>ER8|&lt;id&gt;</c>) puis <c>EL</c>, se ferme sur <c>EV</c>. « Retirer » envoie
    /// <c>EMO-&lt;objet&gt;|&lt;quantité&gt;</c>, « Récupérer » (<c>GET_ITEM</c>) un retrait par objet puis <c>EMG-&lt;kamas&gt;</c> ; le contenu ne
    /// change qu'aux réponses <c>EsK…</c>. « Fermer » envoie <c>EV</c> : StarLoco relève alors la collecte et retire le percepteur.
    /// </summary>
    public sealed class CollectorPanel : GamePanel
    {
        private const string IdleText = "Aucun percepteur ouvert. « Collecter » sur un percepteur de votre guilde : le volet s'ouvre lorsque le serveur l'annonce (ECK8 puis EL).";
        private Label kamas, status, warning;
        private ExchangeItemGrid grid;
        private NumericUpDown quantity;
        private Control withdraw, withdrawAll, leave;
        private bool wasOpen;
        private CollectorExchange bound;

        public override string Title => GuildTexts.Get("GUILD_TAXCOLLECTORS", "Percepteurs");
        public override Image Icon => ClientAssets.Icon("icone-guilde", 24);
        public override bool IsModal => true;
        public override bool IsServerWindowOpen => Window?.IsOpen == true;
        public ExchangeItemGrid Grid => grid;

        private CollectorExchange Window => Game?.Interactions?.Guild?.CollectorWindow;

        protected override Control CreateView()
        {
            var page = Page();
            var title = MakeLabel("Contenu du percepteur", 9, true); title.Dock = DockStyle.Top; title.Height = 22;
            kamas = MakeLabel("0 kamas", 9); kamas.Dock = DockStyle.Bottom; kamas.Height = 22; kamas.TextAlign = ContentAlignment.MiddleLeft; kamas.Name = "collector-kamas";
            grid = new ExchangeItemGrid { Dock = DockStyle.Fill, AccessibleName = "Contenu du percepteur", EmptyText = "Percepteur vide.", Name = "collector-grid" };
            grid.SelectionChanged += (s, e) => UpdateButtons();
            quantity = InventoryPanel.Quantity();
            withdraw = MakeButton(GuildTexts.Get("REMOVE", "Retirer"), async (s, e) => await WithdrawSelected(), false, 100); withdraw.Name = "collector-withdraw";
            withdrawAll = MakeButton(GuildTexts.Get("GET_ITEM", "Récupérer"), async (s, e) => await Run(window => window.WithdrawAllAsync()), true, 120); withdrawAll.Name = "collector-withdraw-all";
            warning = MakeLabel("Fermer relève la collecte : StarLoco retire le percepteur et ce qui n'a pas été récupéré est perdu.", 8); warning.Dock = DockStyle.Bottom; warning.Height = 32;
            warning.ForeColor = BotUi.Muted; warning.Name = "collector-warning";
            status = MakeStatus(IdleText); status.Name = "collector-status";
            leave = MakeButton(GuildTexts.Get("CLOSE", "Fermer"), async (s, e) => await Leave(), false, 100); leave.Name = "collector-leave";
            // Ordre d'ancrage : le dernier ajouté prend le bord en premier.
            page.Controls.Add(grid); page.Controls.Add(kamas); page.Controls.Add(title);
            page.Controls.Add(BotUi.Actions(withdraw, InventoryPanel.QuantityLabel(), quantity, withdrawAll)); page.Controls.Add(warning); page.Controls.Add(status); page.Controls.Add(BotUi.Actions(leave));
            return page;
        }

        protected override void OnBind(GameClass game)
        {
            bound = game.Interactions?.Guild?.CollectorWindow;
            if (bound != null) bound.Changed += OnServerChanged;
        }

        protected override void OnUnbind(GameClass game)
        {
            if (bound != null) bound.Changed -= OnServerChanged;
            bound = null;
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

        /// <summary>Thread de l'interface par la fenêtre de jeu (voir <see cref="ExchangePanel.PostToForm"/>).</summary>
        private bool Post(Action action) => ExchangePanel.PostToForm(Host, () => { if (!IsDisposed) action(); });

        public override void RefreshView()
        {
            CollectorExchange window = Window;
            if (window == null || grid == null || IsDisposed) return;
            grid.SetItems(window.Items);
            kamas.Text = window.Kamas.ToString("N0", CultureInfo.CurrentCulture) + " kamas dans le percepteur";
            string message = window.LastMessage;
            if (!window.IsOpen) status.Text = IdleText + (message.Length > 0 ? " " + message : string.Empty);
            else if (!window.ContentReceived) status.Text = "Percepteur ouvert ; en attente de son contenu (EL).";
            else status.Text = window.Items.Count + " objet(s) dans le percepteur n° " + window.CollectorId.ToString(CultureInfo.InvariantCulture) + " ; le contenu suit les réponses du serveur (EsK).";
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            CollectorExchange window = Window;
            if (window == null || leave == null || IsDisposed) return;
            bool open = Connected && window.IsOpen;
            ExchangeItem stored = grid.SelectedItem;
            if (stored != null) quantity.Maximum = Math.Max(1, stored.Quantity);
            withdraw.Enabled = open && stored != null;
            withdrawAll.Enabled = open && !window.IsEmpty;
            leave.Enabled = open;
        }

        protected internal override bool OnUserClose()
        {
            if (!IsServerWindowOpen) return true;
            _ = Leave();
            return false;
        }

        private Task Run(Func<CollectorExchange, Task<Tool_BotProtocol.Game.Interactions.InteractionResult>> action)
        {
            CollectorExchange window = Window;
            return window == null ? Task.CompletedTask : ReportAsync(() => action(window));
        }

        private Task WithdrawSelected()
        {
            ExchangeItem stored = grid.SelectedItem;
            if (stored == null) return Task.CompletedTask;
            int count = Math.Min((int)quantity.Value, stored.Quantity);
            return Run(window => window.WithdrawAsync(stored.Id, count));
        }

        private Task Leave() => Run(window => window.LeaveAsync());
    }
}
