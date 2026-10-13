using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Exchanges;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Montures;
using Tool_BotProtocol.Game.Perso.Inventory;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>
    /// Monture du personnage (interface <c>Mount</c> du client 1.34, 742 × 435 dans le client, ramenée à la largeur du tiroir) :
    /// fiche <see cref="MountSheet"/> comme <c>MountViewer</c> (modèle, nom, niveau, sexe, jauges, capacités, effets), boutons
    /// « Monter / Descendre » (<c>Rr</c>), « Sacoches » (<c>ER15|</c>), renommer (<c>Rn</c>), part d'expérience donnée (<c>Rx</c>, 0 à 90),
    /// « Castrer » et « Libérer » (<c>Rc</c>, <c>Rf</c>, après la confirmation du client). L'onglet « Sacoches » s'ouvre sur
    /// <c>ECK15</c> (contenu <c>EL</c>, charge <c>Ew</c>, dépôt <c>EMO+</c>, retrait <c>EMO-</c>, fermeture <c>EV</c>) ; l'onglet
    /// « Fiche consultée » montre la dernière fiche <c>Rd</c> (monture d'enclos ou certificat). L'état vient du serveur.
    /// </summary>
    public sealed class MountPanel : GamePanel
    {
        private const string Reference = "MONTURE";
        public const int OwnTab = 0, BagsTab = 1, ViewedTab = 2;
        private MountTabStrip tabs;
        private Panel ownArea, bagsArea, viewedArea;
        private MountSheet ownSheet, viewedSheet;
        private FlowLayoutPanel ownActions, renameBar, xpBar;
        private Control ride, openBags, castrate, free, rename, applyXp;
        private TextBox nameInput;
        private NumericUpDown xpInput;
        private Label status, bagsPods;
        private ExchangeItemGrid bagsGrid;
        private ListView bagsBag;
        private NumericUpDown withdrawQuantity, depositQuantity;
        private Control bagsWithdraw, bagsDeposit, bagsLeave;
        private MountActions bound;
        private MountExchange boundBags;
        private InventoryClass inventory;
        private bool bagsWereOpen, openedForBags;
        private int viewedSerial;
        private Form confirmDialog;

        public override string Title => MountTexts.Get("MOUNT", "Monture");
        public override Image Icon => ClientAssets.Icon("icone-monture", 24);
        /// <summary>Modal tant que les sacoches sont ouvertes : ouvert depuis le bandeau, le volet ne ferme pas les autres.</summary>
        public override bool IsModal => IsServerWindowOpen;
        public override bool IsServerWindowOpen => Game?.Interactions?.Mount?.Inventory?.IsOpen == true;
        /// <summary>Onglet affiché (<see cref="OwnTab"/>, <see cref="BagsTab"/>, <see cref="ViewedTab"/>).</summary>
        public int CurrentTab => tabs?.Selected ?? OwnTab;
        /// <summary>Fiche de la monture équipée (tests et diagnostic).</summary>
        public MountSheet OwnSheet => ownSheet;
        /// <summary>Fiche reçue par <c>Rd</c>.</summary>
        public MountSheet ViewedSheet => viewedSheet;
        /// <summary>Contenu des sacoches affiché.</summary>
        public ExchangeItemGrid BagsGrid => bagsGrid;
        /// <summary>Boîte de confirmation ouverte (castrer, libérer), ou <c>null</c>.</summary>
        public Form ConfirmDialog => confirmDialog;

        protected override Control CreateView()
        {
            var page = Page();
            tabs = new MountTabStrip(MountTexts.Get("MOUNT_INVENTORY", "Monture équipée"), "Sacoches", "Fiche consultée") { Dock = DockStyle.Top, Name = "mount-tabs" };
            tabs.TabClicked += (s, index) => SelectTab(index);

            ownSheet = new MountSheet { Dock = DockStyle.Top, Name = "mount-own-sheet", EmptyText = MountTexts.Get("MOUNT_NO_EQUIP", "Tu n'as pas de monture équipée.") };
            ownArea = Scroller(ownSheet, "mount-own");
            viewedSheet = new MountSheet { Dock = DockStyle.Top, Name = "mount-viewed-sheet", EmptyText = "Aucune fiche consultée : « Consulter la fiche » d'une monture d'enclos (Rp) ou d'un certificat (Rd)." };
            viewedArea = Scroller(viewedSheet, "mount-viewed");
            bagsArea = BuildBags();

            ride = MakeButton("Monter", async (s, e) => await ReportAsync(() => Game.Interactions.Mount.RideAsync()), true, 96);
            openBags = MakeButton("Sacoches", async (s, e) => await ReportAsync(() => Game.Interactions.Mount.OpenInventoryAsync()), false, 96);
            castrate = MakeButton("Castrer…", async (s, e) => await Confirm(false), false, 86);
            free = MakeButton("Libérer…", async (s, e) => await Confirm(true), false, 86);
            ownActions = BotUi.Actions(ride, openBags, castrate, free);
            ownActions.Name = "mount-actions";
            nameInput = new TextBox { Width = 170, Height = 29, Font = BotFonts.Get(9), BorderStyle = BorderStyle.FixedSingle, BackColor = BotUi.PaperLight,
                ForeColor = BotUi.Ink, Margin = new Padding(0, 4, 6, 0), AccessibleName = "Nouveau nom de la monture", Name = "mount-name" };
            nameInput.KeyDown += async (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await Rename(); } };
            rename = MakeButton(MountTexts.Get("MOUNT_RENAME_TOOLTIP", "Renommer la monture"), async (s, e) => await Rename(), false, 170);
            nameInput.TextChanged += (s, e) => rename.Enabled = Connected && Game?.Interactions?.Mount?.HasMount == true && nameInput.Text.Length > 0;
            renameBar = BotUi.Actions(nameInput, rename);
            xpInput = new NumericUpDown { Minimum = 0, Maximum = Mount.MaxXpPercent, Value = 0, Width = 70, Height = 34, Font = BotFonts.Get(9), BackColor = BotUi.PaperLight,
                ForeColor = BotUi.Ink, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(2, 7, 6, 0), AccessibleName = "Part d'expérience donnée à la monture", Name = "mount-xp" };
            Label xpLabel = BotUi.Label(MountTexts.Get("MOUNT_PERCENT_XP", "XP donnée") + " (%)", 9); xpLabel.AutoSize = true; xpLabel.Margin = new Padding(0, 12, 0, 0); xpLabel.ForeColor = BotUi.Muted;
            applyXp = MakeButton("Appliquer", async (s, e) => await ReportAsync(() => Game.Interactions.Mount.SetXpRatioAsync((int)xpInput.Value)), false, 96);
            xpBar = BotUi.Actions(xpLabel, xpInput, applyXp);

            status = MakeStatus("Équipez une monture (étable d'un enclos) pour l'afficher ici ; tout changement vient du serveur (Re, Rx, Rn, Rr).");
            page.Controls.Add(ownArea); page.Controls.Add(bagsArea); page.Controls.Add(viewedArea);
            page.Controls.Add(xpBar); page.Controls.Add(renameBar); page.Controls.Add(ownActions);
            page.Controls.Add(status); page.Controls.Add(tabs);
            SelectTab(OwnTab);
            return page;
        }

        private static Panel Scroller(MountSheet sheet, string name)
        {
            var area = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = BotUi.Paper, Name = name, Margin = new Padding(0) };
            area.Controls.Add(sheet);
            area.Resize += (s, e) => sheet.FitWidth(area.ClientSize.Width);
            return area;
        }

        private Panel BuildBags()
        {
            var area = new Panel { Dock = DockStyle.Fill, BackColor = BotUi.Paper, Name = "mount-bags", Visible = false };
            var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
            split.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); split.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            var top = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 6) };
            var title = MakeLabel("Sacoches de la monture", 9, true); title.Dock = DockStyle.Top; title.Height = 22;
            bagsPods = MakeLabel(string.Empty, 9); bagsPods.Dock = DockStyle.Bottom; bagsPods.Height = 22; bagsPods.TextAlign = ContentAlignment.MiddleLeft;
            bagsGrid = new ExchangeItemGrid { Dock = DockStyle.Fill, AccessibleName = "Sacoches de la monture", EmptyText = "Sacoches vides." };
            bagsGrid.SelectionChanged += (s, e) => UpdateBagButtons();
            withdrawQuantity = InventoryPanel.Quantity();
            bagsWithdraw = MakeButton("Retirer", async (s, e) => await Withdraw(), false, 100);
            top.Controls.Add(bagsGrid); top.Controls.Add(bagsPods); top.Controls.Add(title);
            top.Controls.Add(BotUi.Actions(bagsWithdraw, InventoryPanel.QuantityLabel(), withdrawQuantity));
            var bottom = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            var bagTitle = MakeLabel("Votre sac", 9, true); bagTitle.Dock = DockStyle.Top; bagTitle.Height = 22;
            bagsBag = MakeList(9, "Objet", "Qté");
            bagsBag.Columns[0].Width = 250; bagsBag.Columns[1].Width = 75;
            bagsBag.SelectedIndexChanged += (s, e) => UpdateBagButtons();
            depositQuantity = InventoryPanel.Quantity();
            bagsDeposit = MakeButton("Déposer", async (s, e) => await Deposit(), false, 100);
            bagsLeave = MakeButton("Fermer", async (s, e) => await LeaveBags(), false, 90);
            bottom.Controls.Add(bagsBag); bottom.Controls.Add(bagTitle);
            bottom.Controls.Add(BotUi.Actions(bagsDeposit, InventoryPanel.QuantityLabel(), depositQuantity, bagsLeave));
            split.Controls.Add(top, 0, 0); split.Controls.Add(bottom, 0, 1);
            area.Controls.Add(split);
            return area;
        }

        /// <summary>Affiche un onglet (clic, ouverture des sacoches, fiche <c>Rd</c>).</summary>
        public void SelectTab(int index)
        {
            if (tabs == null) return;
            index = Math.Max(OwnTab, Math.Min(ViewedTab, index));
            tabs.Selected = index;
            ownArea.Visible = index == OwnTab; bagsArea.Visible = index == BagsTab; viewedArea.Visible = index == ViewedTab;
            ownActions.Visible = renameBar.Visible = xpBar.Visible = index == OwnTab;
            RefreshView();
        }

        protected override void OnBind(GameClass game)
        {
            bound = game.Interactions?.Mount;
            if (bound != null) { bound.Changed += OnServerChanged; viewedSerial = bound.ViewedSerial; }
            boundBags = bound?.Inventory;
            if (boundBags != null) boundBags.Changed += OnServerChanged;
            inventory = game.character?.Inventory;
            if (inventory != null) inventory.RefreshInventory += OnInventoryChanged;
            bagsWereOpen = false; openedForBags = false;
        }

        protected override void OnUnbind(GameClass game)
        {
            if (bound != null) bound.Changed -= OnServerChanged;
            if (boundBags != null) boundBags.Changed -= OnServerChanged;
            if (inventory != null) inventory.RefreshInventory -= OnInventoryChanged;
            bound = null; boundBags = null; inventory = null;
            bagsWereOpen = false; openedForBags = false;
            CloseConfirm();
        }

        private void OnServerChanged()
        {
            if (!ExchangePanel.PostToForm(Host, () => { if (!IsDisposed) ApplyServerChange(); })) OnUi(ApplyServerChange);
        }

        private void OnInventoryChanged(bool changed)
        {
            if (!changed) return;
            if (!ExchangePanel.PostToForm(Host, () => { if (!IsDisposed) RefreshView(); })) OnUi(RefreshView);
        }

        private void ApplyServerChange()
        {
            if (tabs == null || Game == null) return;
            bool open = IsServerWindowOpen;
            MountActions mount = Game.Interactions?.Mount;
            if (open && !bagsWereOpen)
            {
                openedForBags = Host == null || !Host.IsOpen(this);
                SelectTab(BagsTab); RequestShow();
            }
            else if (!open && bagsWereOpen)
            {
                if (CurrentTab == BagsTab) SelectTab(OwnTab);
                if (openedForBags) RaiseClosed();
                openedForBags = false;
            }
            bagsWereOpen = open;
            if (mount != null && mount.ViewedSerial != viewedSerial)
            {
                viewedSerial = mount.ViewedSerial;
                if (mount.Viewed != null) { SelectTab(ViewedTab); RequestShow(); }
            }
            if (mount != null && mount.Current == null) CloseConfirm();
            RefreshView();
        }

        public override void RefreshView()
        {
            if (Game == null || ownSheet == null) return;
            MountActions mount = Game.Interactions?.Mount;
            if (mount == null) return;
            Mount current = mount.Current;
            ownSheet.SetMount(current, mount.XpPercent, mount.IsRiding, mount.HasMount && current == null ? "Monture annoncée par le serveur, fiche illisible." : null);
            ownSheet.FitWidth(ownArea.ClientSize.Width);
            viewedSheet.SetMount(mount.Viewed, null, false, null);
            viewedSheet.FitWidth(viewedArea.ClientSize.Width);
            tabs.SetBadge(BagsTab, IsServerWindowOpen);
            tabs.SetBadge(ViewedTab, mount.Viewed != null);
            bool connected = Connected;
            bool has = mount.HasMount;
            ride.Text = mount.IsRiding ? "Descendre" : "Monter";
            ride.Enabled = connected && has;
            openBags.Enabled = connected && has && !IsServerWindowOpen;
            castrate.Enabled = free.Enabled = connected && has && confirmDialog == null;
            rename.Enabled = connected && has && nameInput.Text.Length > 0;
            applyXp.Enabled = connected && has;
            if (mount.XpPercent.HasValue && !xpInput.Focused) xpInput.Value = Math.Max(0, Math.Min(Mount.MaxXpPercent, mount.XpPercent.Value));
            RefreshBags();
            string message = mount.LastMessage;
            MountExchange bags = mount.Inventory;
            if (CurrentTab == BagsTab && bags != null && bags.LastMessage.Length > 0) message = bags.LastMessage;
            status.Text = message.Length > 0 ? message : has ? (current?.Describe() ?? "Monture équipée.") : MountTexts.Get("MOUNT_NO_EQUIP", "Tu n'as pas de monture équipée.");
        }

        private void RefreshBags()
        {
            MountExchange bags = Game?.Interactions?.Mount?.Inventory;
            if (bags == null || bagsGrid == null) return;
            bagsGrid.SetItems(bags.Items);
            bagsPods.Text = bags.Pods.HasValue ? MountTexts.Get("WEIGHT", "Pods").Trim() + " : " + bags.Pods.Value.ToString("N0", CultureInfo.CurrentCulture)
                + " / " + (bags.PodsMax ?? 0).ToString("N0", CultureInfo.CurrentCulture) : bags.IsOpen ? "Charge inconnue (Ew attendu)." : string.Empty;
            uint selected = bagsBag.SelectedItems.Count == 0 ? 0u : (uint)bagsBag.SelectedItems[0].Tag;
            bagsBag.BeginUpdate(); bagsBag.Items.Clear();
            var bag = Game.character?.Inventory?.Objets;
            if (bag != null)
                foreach (InventoryObjects item in bag.Where(entry => entry != null && !entry.IsEquipped()).OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase).ToArray())
                {
                    var row = bagsBag.Items.Add(item.Name); row.SubItems.Add(item.Qua.ToString(CultureInfo.CurrentCulture)); row.Tag = item.Inventory_ID;
                    if (item.Inventory_ID == selected) row.Selected = true;
                }
            bagsBag.EndUpdate();
            UpdateBagButtons();
        }

        private void UpdateBagButtons()
        {
            MountExchange bags = Game?.Interactions?.Mount?.Inventory;
            if (bags == null || bagsLeave == null || IsDisposed) return;
            bool open = Connected && bags.IsOpen;
            ExchangeItem stored = bagsGrid.SelectedItem;
            InventoryObjects item = InventoryPanel.SelectedItem(Game, bagsBag);
            if (stored != null) withdrawQuantity.Maximum = Math.Max(1, stored.Quantity);
            if (item != null) depositQuantity.Maximum = Math.Max(1, item.Qua);
            bagsWithdraw.Enabled = open && stored != null;
            bagsDeposit.Enabled = open && item != null;
            bagsLeave.Enabled = open;
        }

        protected internal override bool OnUserClose()
        {
            if (!IsServerWindowOpen) return true;
            _ = LeaveBags();
            return false;
        }

        private Task Rename()
        {
            string name = nameInput?.Text ?? string.Empty;
            return ReportAsync(() => Game.Interactions.Mount.RenameAsync(name));
        }

        private Task Withdraw()
        {
            ExchangeItem stored = bagsGrid.SelectedItem;
            if (stored == null) return Task.CompletedTask;
            int quantity = (int)withdrawQuantity.Value;
            return ReportAsync(() => Game.Interactions.Mount.Inventory.WithdrawAsync(stored.Id, quantity));
        }

        private Task Deposit()
        {
            InventoryObjects item = InventoryPanel.SelectedItem(Game, bagsBag);
            if (item == null) return Task.CompletedTask;
            int quantity = (int)depositQuantity.Value;
            return ReportAsync(() => Game.Interactions.Mount.Inventory.DepositAsync(item.Inventory_ID, quantity));
        }

        private Task LeaveBags() => ReportAsync(() => Game.Interactions.Mount.Inventory.LeaveAsync());

        /// <summary><c>DO_U_CASTRATE_YOUR_MOUNT</c> / <c>DO_U_KILL_YOUR_MOUNT</c> (Oui / Non) avant <c>Rc</c> / <c>Rf</c>, comme l'interface <c>Mount</c>.</summary>
        private async Task Confirm(bool freeMount)
        {
            try
            {
                MountActions mount = Game?.Interactions?.Mount;
                if (mount == null || !mount.HasMount) return;
                CloseConfirm();
                Form[] before = BotDialogs.OpenDialogs.ToArray();
                Task<BotDialogResult> question = freeMount
                    ? BotDialogs.AskYesNoAsync(Host, MountTexts.Get("MOUNT_KILL_TOOLTIP", "Libérer cette monture"),
                        MountTexts.Get("DO_U_KILL_YOUR_MOUNT", "Êtes-vous certain de vouloir rendre sa liberté à votre monture ? Cette action est définitive."))
                    : BotDialogs.AskYesNoAsync(Host, MountTexts.Get("MOUNT_CASTRATE_TOOLTIP", "Castrer cette monture"),
                        MountTexts.Get("DO_U_CASTRATE_YOUR_MOUNT", "Êtes-vous certain de vouloir castrer votre monture ? Cette action est irréversible."));
                confirmDialog = BotDialogs.OpenDialogs.Except(before).LastOrDefault();
                RefreshView();
                BotDialogResult answer = await question;
                confirmDialog = null;
                if (!IsDisposed) RefreshView();
                if (answer != BotDialogResult.Yes || !mount.HasMount) return;
                if (IsDisposed) { if (freeMount) await mount.FreeAsync(); else await mount.CastrateAsync(); return; }
                await ReportAsync(() => freeMount ? mount.FreeAsync() : mount.CastrateAsync());
            }
            catch (Exception error) { Account?.Logger?.LogException(Reference, error); }
        }

        private void CloseConfirm()
        {
            Form dialog = confirmDialog;
            confirmDialog = null;
            if (dialog != null && !dialog.IsDisposed) dialog.Close();
        }
    }

    /// <summary>Textes du client (<c>lang.xml</c>) avec repli rédigé pour le bot, espaces de fin retirées.</summary>
    internal static class MountTexts
    {
        internal static string Get(string key, string fallback, params string[] args) => (ExchangeRegistry.Text(key, fallback, args) ?? fallback).Trim();
    }

    /// <summary>Onglets du volet, dessinés comme ceux du client (parchemin pour l'onglet actif, brun sinon) ; un point olive signale du contenu.</summary>
    public sealed class MountTabStrip : Control
    {
        public const int TabHeight = 24;
        private readonly string[] titles;
        private readonly bool[] badges;
        private int selected;

        public MountTabStrip(params string[] titles)
        {
            this.titles = titles ?? new string[0];
            badges = new bool[this.titles.Length];
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = BotUi.Paper; Height = TabHeight + 4; Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.PageTabList;
            AccessibleName = string.Join(", ", this.titles);
        }

        public event EventHandler<int> TabClicked;
        public int Count => titles.Length;
        public string TitleOf(int index) => index >= 0 && index < titles.Length ? titles[index] : string.Empty;

        public int Selected
        {
            get => selected;
            set { if (selected == value || value < 0 || value >= titles.Length) return; selected = value; AccessibleDescription = TitleOf(value); Invalidate(); }
        }

        public void SetBadge(int index, bool visible)
        {
            if (index < 0 || index >= badges.Length || badges[index] == visible) return;
            badges[index] = visible; Invalidate();
        }

        public Rectangle TabBounds(int index)
        {
            int count = Math.Max(1, titles.Length);
            int width = Math.Max(40, (Width - (count - 1) * 3) / count);
            return new Rectangle(index * (width + 3), 0, width, TabHeight);
        }

        /// <summary>Simule un clic sur l'onglet (tests, clavier).</summary>
        public void ClickTab(int index) => TabClicked?.Invoke(this, index);

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            using (var paper = new SolidBrush(BackColor)) graphics.FillRectangle(paper, ClientRectangle);
            using (var rule = new Pen(BotUi.Gold)) graphics.DrawLine(rule, 0, TabHeight, Width, TabHeight);
            for (int index = 0; index < titles.Length; index++)
            {
                Rectangle area = TabBounds(index);
                bool active = index == selected;
                using (var fill = new SolidBrush(active ? BotUi.PaperLight : BotUi.FrameLight)) graphics.FillRectangle(fill, area);
                using (var border = new Pen(BotUi.Gold)) graphics.DrawRectangle(border, area.X, area.Y, area.Width - 1, area.Height - (active ? 0 : 1));
                Rectangle text = new Rectangle(area.X + 2, area.Y, area.Width - (badges[index] ? 14 : 4), area.Height);
                TextRenderer.DrawText(graphics, titles[index], BotFonts.Get(8.25f, active ? FontStyle.Bold : FontStyle.Regular), text,
                    active ? BotUi.Ink : BotUi.PaperLight, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                if (badges[index])
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var dot = new SolidBrush(BotUi.Olive)) graphics.FillEllipse(dot, area.Right - 12, area.Y + (area.Height - 7) / 2, 7, 7);
                    graphics.SmoothingMode = SmoothingMode.None;
                }
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            for (int index = 0; index < titles.Length; index++)
                if (TabBounds(index).Contains(e.Location) && index != selected) { TabClicked?.Invoke(this, index); return; }
        }
    }

    /// <summary>
    /// Fiche d'une monture comme <c>MountViewer</c> du client 1.34 : bandeau brun (modèle, niveau), illustration du modèle
    /// (<c>sprites/&lt;gfx&gt;_staticS.png</c>, lue en tâche de fond) et ses trois couleurs (<c>rides_fr</c>), nom, sexe, montable,
    /// sauvage, état de reproduction, puis les jauges (expérience, énergie, fatigue, maturité, endurance, amour, sérénité de
    /// -10 000 à 10 000, charge), capacités (<c>RIA</c>) et effets. Le dessin ne lit que le cache des images.
    /// </summary>
    public sealed class MountSheet : Control
    {
        public const int HeaderHeight = 28, SpriteSize = 92, GaugeHeight = 20, Margin8 = 8;
        private static readonly Color FatigueColor = Color.FromArgb(159, 80, 37), EnergyColor = Color.FromArgb(167, 195, 29),
            XpColor = Color.FromArgb(126, 160, 164), LoveColor = Color.FromArgb(176, 98, 74);
        private Mount mount;
        private int? xpPercent;
        private bool riding;
        private string note;
        private string emptyText = "Aucune monture.";

        public MountSheet()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = BotUi.Paper; Height = HeaderHeight + 40; AccessibleRole = AccessibleRole.Grouping; AccessibleName = "Fiche de monture";
        }

        public Mount Mount => mount;
        public string EmptyText { get => emptyText; set { emptyText = value ?? string.Empty; if (mount == null) Invalidate(); } }

        /// <summary>Lignes de texte de la fiche, dans l'ordre d'affichage (accessibilité et tests).</summary>
        public IReadOnlyList<string> Lines => Describe(mount, xpPercent, riding, note, emptyText);

        /// <summary>Jauges affichées : libellé, valeur, minimum, maximum.</summary>
        public IReadOnlyList<Tuple<string, long, long, long>> Gauges => GaugesOf(mount);

        public void SetMount(Mount value, int? xp, bool isRiding, string message)
        {
            bool changed = !ReferenceEquals(mount, value) || xpPercent != xp || riding != isRiding || note != message;
            mount = value; xpPercent = xp; riding = isRiding; note = message;
            if (!changed) return;
            AccessibleDescription = string.Join(" ; ", Lines);
            if (value != null) LoadSprite(value.Gfx);
            FitWidth(Width);
            Invalidate();
        }

        /// <summary>Ajuste la hauteur au contenu pour la largeur donnée (le parent fait défiler).</summary>
        public void FitWidth(int width)
        {
            if (width <= 0) return;
            int height = Measure(Math.Max(200, width - SystemInformation.VerticalScrollBarWidth));
            if (Height != height) Height = height;
        }

        internal static IReadOnlyList<string> Describe(Mount mount, int? xpPercent, bool riding, string note, string empty)
        {
            var lines = new List<string>();
            if (mount == null) { lines.Add(note ?? empty); return lines; }
            lines.Add(mount.ModelName + " · " + MountTexts.Get("LEVEL", "Niveau") + " " + mount.Level.ToString(CultureInfo.InvariantCulture));
            lines.Add(MountTexts.Get("NAME_BIG", "Nom") + " : " + mount.DisplayName + (riding ? " (montée)" : string.Empty));
            lines.Add(mount.SexText + " · " + MountTexts.Get("MOUNTABLE", "Montable") + " : " + YesNo(mount.Mountable) + " · " + MountTexts.Get("WILD", "Sauvage") + " : " + YesNo(mount.Wild));
            lines.Add(mount.ReproductionText);
            if (xpPercent.HasValue) lines.Add(MountTexts.Get("MOUNT_PERCENT_XP", "XP donnée") + " : " + xpPercent.Value.ToString(CultureInfo.InvariantCulture) + " %");
            foreach (var gauge in GaugesOf(mount)) lines.Add(gauge.Item1 + " : " + Number(gauge.Item2) + " / " + Number(gauge.Item4));
            lines.Add(MountTexts.Get("CAPACITIES", "Capacités") + " : " + (mount.Capacities.Count == 0 ? "aucune" : string.Join(", ", mount.CapacityNames)));
            IReadOnlyList<string> effects = mount.EffectLines;
            if (effects.Count > 0) lines.Add(MountTexts.Get("EFFECTS", "Effets") + " : " + string.Join(", ", effects));
            if (!string.IsNullOrEmpty(note)) lines.Add(note);
            return lines;
        }

        private static IReadOnlyList<Tuple<string, long, long, long>> GaugesOf(Mount mount)
        {
            var gauges = new List<Tuple<string, long, long, long>>();
            if (mount == null) return gauges;
            gauges.Add(Tuple.Create(MountTexts.Get("EXPERIMENT", "Expérience"), mount.Xp, mount.XpMin, mount.XpMax));
            gauges.Add(Tuple.Create(MountTexts.Get("ENERGY", "Energie"), (long)mount.Energy, 0L, (long)mount.EnergyMax));
            gauges.Add(Tuple.Create(MountTexts.Get("TIRE", "Fatigue"), (long)mount.Tired, 0L, (long)mount.TiredMax));
            gauges.Add(Tuple.Create(MountTexts.Get("MATURITY", "Maturité"), (long)mount.Maturity, 0L, (long)mount.MaturityMax));
            gauges.Add(Tuple.Create(MountTexts.Get("STAMINA", "Endurance"), (long)mount.Stamina, 0L, (long)mount.StaminaMax));
            gauges.Add(Tuple.Create(MountTexts.Get("LOVE", "Amour"), (long)mount.Love, 0L, (long)mount.LoveMax));
            gauges.Add(Tuple.Create(MountTexts.Get("SERENITY", "Sérénité"), (long)mount.Serenity, (long)mount.SerenityMin, (long)mount.SerenityMax));
            if (mount.Pods.HasValue) gauges.Add(Tuple.Create(MountTexts.Get("WEIGHT", "Pods"), (long)mount.Pods.Value, 0L, (long)mount.PodsMax));
            return gauges;
        }

        private static string YesNo(bool value) => value ? MountTexts.Get("YES", "Oui") : MountTexts.Get("NO", "Non");
        private static string Number(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

        private int Measure(int width)
        {
            if (mount == null) return HeaderHeight + 44;
            int textWidth = Math.Max(80, width - 2 * Margin8);
            int y = HeaderHeight + Margin8 + Math.Max(SpriteSize + 18, 5 * 18) + Margin8;
            y += GaugesOf(mount).Count * GaugeHeight + Margin8;
            y += TextHeight(CapacitiesText(), textWidth) + 22;
            IReadOnlyList<string> effects = mount.EffectLines;
            if (effects.Count > 0) y += 20 + TextHeight(string.Join("\n", effects), textWidth);
            return y + Margin8;
        }

        private string CapacitiesText() => mount.Capacities.Count == 0 ? "Aucune capacité." : string.Join(", ", mount.CapacityNames);

        private static int TextHeight(string text, int width) =>
            TextRenderer.MeasureText(text ?? string.Empty, BotFonts.Get(8.25f), new Size(width, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + 2;

        private void LoadSprite(int gfx)
        {
            Task<Bitmap> load = ClientAssets.GetAsync("Sprites", SpriteName(gfx));
            if (!load.IsCompleted) load.ContinueWith(_ => RedrawLater(), TaskScheduler.Default);
        }

        internal static string SpriteName(int gfx) => gfx.ToString(CultureInfo.InvariantCulture) + "_staticS";

        private void RedrawLater()
        {
            try { if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)Invalidate); }
            catch (Exception error) when (error is InvalidOperationException || error is ObjectDisposedException) { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            using (var paper = new SolidBrush(BackColor)) graphics.FillRectangle(paper, ClientRectangle);
            var header = new Rectangle(0, 0, Width, HeaderHeight);
            using (var frame = new SolidBrush(BotUi.Frame)) graphics.FillRectangle(frame, header);
            using (var rule = new Pen(BotUi.Gold)) graphics.DrawLine(rule, 0, HeaderHeight - 1, Width, HeaderHeight - 1);
            Mount shown = mount;
            if (shown == null)
            {
                TextRenderer.DrawText(graphics, MountTexts.Get("MOUNT", "Monture"), BotFonts.Get(9.75f, FontStyle.Bold), new Rectangle(Margin8, 0, Width - 16, HeaderHeight),
                    BotUi.Gold, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(graphics, note ?? emptyText, BotFonts.Get(9), new Rectangle(Margin8, HeaderHeight + 6, Width - 16, Height - HeaderHeight - 8),
                    BotUi.Muted, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                return;
            }
            string level = MountTexts.Get("LEVEL", "Niveau") + " " + shown.Level.ToString(CultureInfo.InvariantCulture);
            TextRenderer.DrawText(graphics, shown.ModelName, BotFonts.Get(9.75f, FontStyle.Bold), new Rectangle(Margin8, 0, Width - 110, HeaderHeight),
                BotUi.Gold, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(graphics, level, BotFonts.Get(9, FontStyle.Bold), new Rectangle(Width - 104, 0, 96, HeaderHeight),
                BotUi.PaperLight, TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPrefix);

            int top = HeaderHeight + Margin8;
            var box = new Rectangle(Margin8, top, SpriteSize, SpriteSize);
            using (var light = new SolidBrush(BotUi.PaperLight)) graphics.FillRectangle(light, box);
            using (var border = new Pen(BotUi.Gold)) graphics.DrawRectangle(border, box);
            if (ClientAssets.TryCached("Sprites", SpriteName(shown.Gfx), out Bitmap sprite) && sprite != null)
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                lock (sprite) ClientAssets.DrawFit(graphics, sprite, RectangleF.Inflate(box, -4, -4), true);
            }
            IReadOnlyList<int> colors = shown.Colors;
            int swatch = (SpriteSize - 4) / 3;
            for (int index = 0; index < colors.Count; index++)
            {
                var area = new Rectangle(Margin8 + index * (swatch + 2), box.Bottom + 4, swatch, 10);
                if (colors[index] >= 0) using (var fill = new SolidBrush(Color.FromArgb(255, (colors[index] >> 16) & 255, (colors[index] >> 8) & 255, colors[index] & 255))) graphics.FillRectangle(fill, area);
                else using (var hatch = new HatchBrush(HatchStyle.BackwardDiagonal, BotUi.Gold, BotUi.PaperLight)) graphics.FillRectangle(hatch, area);
                using (var border = new Pen(BotUi.FrameLight)) graphics.DrawRectangle(border, area);
            }

            int x = box.Right + 10, width = Math.Max(60, Width - x - Margin8), y = top;
            TextRenderer.DrawText(graphics, shown.DisplayName + (riding ? "  (montée)" : string.Empty), BotFonts.Get(10, FontStyle.Bold), new Rectangle(x, y, width, 20),
                BotUi.Ink, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            y += 20;
            Line(graphics, shown.SexText + " · " + MountTexts.Get("MOUNTABLE", "Montable") + " : " + YesNo(shown.Mountable), x, ref y, width, BotUi.Ink);
            Line(graphics, MountTexts.Get("WILD", "Sauvage") + " : " + YesNo(shown.Wild) + (shown.IsChameleon ? " · caméléone" : string.Empty), x, ref y, width, BotUi.Ink);
            Color reproduction = shown.IsPregnant || shown.IsCastrated || shown.IsSterile ? FatigueColor : shown.Fecondable ? BotUi.Olive : BotUi.Muted;
            Line(graphics, shown.ReproductionText, x, ref y, width, reproduction);
            if (xpPercent.HasValue) Line(graphics, MountTexts.Get("MOUNT_PERCENT_XP", "XP donnée") + " : " + xpPercent.Value.ToString(CultureInfo.InvariantCulture) + " %", x, ref y, width, BotUi.Muted);

            y = top + Math.Max(SpriteSize + 18, 5 * 18) + Margin8;
            foreach (var gauge in GaugesOf(shown))
            {
                DrawGauge(graphics, gauge.Item1, gauge.Item2, gauge.Item3, gauge.Item4, new Rectangle(Margin8, y, Width - 2 * Margin8, GaugeHeight - 4));
                y += GaugeHeight;
            }
            y += Margin8;
            int textWidth = Math.Max(80, Width - 2 * Margin8);
            Title(graphics, MountTexts.Get("CAPACITIES", "Capacités"), ref y);
            int capacities = TextHeight(CapacitiesText(), textWidth);
            TextRenderer.DrawText(graphics, CapacitiesText(), BotFonts.Get(8.25f), new Rectangle(Margin8, y, textWidth, capacities), BotUi.Ink, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            y += capacities;
            IReadOnlyList<string> effects = shown.EffectLines;
            if (effects.Count == 0) return;
            Title(graphics, MountTexts.Get("EFFECTS", "Effets"), ref y);
            string text = string.Join("\n", effects);
            TextRenderer.DrawText(graphics, text, BotFonts.Get(8.25f), new Rectangle(Margin8, y, textWidth, TextHeight(text, textWidth)), BotUi.Ink, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }

        private static void Line(Graphics graphics, string text, int x, ref int y, int width, Color color)
        {
            TextRenderer.DrawText(graphics, text, BotFonts.Get(8.25f), new Rectangle(x, y, width, 18), color, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
            y += 18;
        }

        private void Title(Graphics graphics, string text, ref int y)
        {
            TextRenderer.DrawText(graphics, text, BotFonts.Get(8.25f, FontStyle.Bold), new Rectangle(Margin8, y, Width - 16, 18), BotUi.Ink, TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
            using (var rule = new Pen(BotUi.Gold)) graphics.DrawLine(rule, Margin8, y + 18, Width - Margin8, y + 18);
            y += 22;
        }

        /// <summary>Jauge à libellé : barre parchemin filetée d'or, remplissage selon la caractéristique ; la sérénité part du centre.</summary>
        private static void DrawGauge(Graphics graphics, string label, long value, long min, long max, Rectangle area)
        {
            const int labelWidth = 86;
            TextRenderer.DrawText(graphics, label, BotFonts.Get(8.25f), new Rectangle(area.X, area.Y, labelWidth, area.Height), BotUi.Ink,
                TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            var bar = new Rectangle(area.X + labelWidth, area.Y + 1, Math.Max(20, area.Width - labelWidth), area.Height - 2);
            using (var track = new SolidBrush(BotUi.PaperLight)) graphics.FillRectangle(track, bar);
            bool bipolar = min < 0 && max > 0;
            Color color = ColorOf(label, value);
            if (bipolar)
            {
                int center = bar.X + (int)(bar.Width * (double)(-min) / (max - min));
                long bounded = Math.Max(min, Math.Min(max, value));
                int end = bar.X + (int)(bar.Width * (double)(bounded - min) / (max - min));
                using (var fill = new SolidBrush(color)) graphics.FillRectangle(fill, Math.Min(center, end), bar.Y, Math.Abs(end - center), bar.Height);
                using (var axis = new Pen(BotUi.FrameLight)) graphics.DrawLine(axis, center, bar.Y, center, bar.Bottom);
            }
            else
            {
                int percent = max > min ? (int)Math.Max(0, Math.Min(100, (value - min) * 100 / (max - min))) : 0;
                using (var fill = new SolidBrush(color)) graphics.FillRectangle(fill, bar.X, bar.Y, bar.Width * percent / 100, bar.Height);
            }
            using (var border = new Pen(BotUi.Gold)) graphics.DrawRectangle(border, bar);
            string text = value.ToString("N0", CultureInfo.CurrentCulture) + " / " + max.ToString("N0", CultureInfo.CurrentCulture);
            TextRenderer.DrawText(graphics, text, BotFonts.Get(7.5f, FontStyle.Bold), bar, BotUi.Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        private static Color ColorOf(string label, long value)
        {
            if (label == MountTexts.Get("TIRE", "Fatigue")) return FatigueColor;
            if (label == MountTexts.Get("ENERGY", "Energie")) return EnergyColor;
            if (label == MountTexts.Get("EXPERIMENT", "Expérience")) return XpColor;
            if (label == MountTexts.Get("LOVE", "Amour")) return LoveColor;
            if (label == MountTexts.Get("SERENITY", "Sérénité")) return value < 0 ? FatigueColor : BotUi.Olive;
            return BotUi.Olive;
        }
    }
}
