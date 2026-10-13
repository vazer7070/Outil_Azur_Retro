using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Controls;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Guildes;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Session;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>
    /// Volet Guilde : la fenêtre <c>Guild</c> du client 1.34 (742 × 457) rendue dans le tiroir. En-tête avec l'emblème composé
    /// (<see cref="ClientAssets.Emblem(string, int, bool)"/>, hors du fil de l'interface), le nom, le niveau et l'xp (<c>gIG</c>) ;
    /// onglets Membres / Personnalisation / Percepteurs / Enclos / Maisons (<c>currentTab</c> du client), chacun demandé au serveur
    /// à l'ouverture et au changement d'onglet (<c>gIG</c> puis <c>gIM</c> / <c>gIB</c> / <c>gIT</c> / <c>gIF</c> / <c>gIH</c>), <c>gITV</c> à
    /// la fermeture. Membres : lignes <c>UI_GuildMemberItem</c> (alignement, classe, nom, rang, niveau, % xp, xp donnée, épée en combat),
    /// fiche <c>GuildMemberInfos</c> (rang, xp, droits → <c>gP</c>, × → <c>gK</c> après <c>DO_U_DELETE_MEMBER</c> / <c>DO_U_DELETE_YOU</c>).
    /// Personnalisation : caractéristiques et sorts des percepteurs (+ → <c>gB</c> / <c>gb</c>), « Poser un percepteur » (<c>gH</c> après
    /// <c>DO_YOU_HIRE_TAXCOLLECTOR</c>). Percepteurs : liste <c>gITM</c> et « Retirer » (<c>gF</c>) ; rejoindre ou quitter la défense
    /// (<c>gTJ</c> / <c>gTV</c>) n'est pas proposé (matrice §2 n° 37). Une invitation reçue (<c>gJr</c>) est posée dans une boîte
    /// Oui / Non / Ignorer (<c>gJK</c> / <c>gJE</c> / <c>gJE</c> et invitant ignoré pour la session). Le panneau de création n'apparaît
    /// qu'après <c>gn</c> (guildalogemme au temple des guildes).
    /// </summary>
    public sealed class GuildPanel : GamePanel
    {
        private const string Reference = "GUILDE";
        /// <summary>Nom de la boîte Oui / Non / Ignorer d'une invitation reçue (<see cref="BotDialogs.Dismiss"/>).</summary>
        public const string InviteDialogName = "GuildInviteDialog";
        private const int EmblemSize = 48;
        private static readonly GuildBoostKind[] BoostKinds = { GuildBoostKind.Pods, GuildBoostKind.Prospecting, GuildBoostKind.Wisdom, GuildBoostKind.Collectors };
        private GuildTab tab = GuildTab.Members;
        private GuildTabStrip tabs;
        private PictureBox emblemBox;
        private Label nameLabel, levelLabel, countLabel, status, boostPoints, boostLife, boostDamage, boostCount, collectorsInfo, paddocksInfo, housesInfo;
        private Panel[] pages;
        private GuildMemberList members;
        private GuildMemberSheet sheet;
        private readonly Dictionary<GuildBoostKind, Label> boostValues = new Dictionary<GuildBoostKind, Label>();
        private readonly Dictionary<GuildBoostKind, Control> boostButtons = new Dictionary<GuildBoostKind, Control>();
        private ListView spells, collectors, paddocks, houses;
        private Control spellBoost, hire, removeCollector, createButton, cancelCreate;
        private Panel creationBar;
        private TextBox createName, createBackColor, createUpColor;
        private NumericUpDown createBack, createUp;
        private ToolTip tips;
        private GuildActions bound;
        private GameSession session;
        private PanelHost watched;
        private Form inviteDialog;
        private string inviteShown, emblemShown;
        private Bitmap emblemImage;
        private int emblemRequest;
        private IReadOnlyList<GuildCollector> shownCollectors;
        private IReadOnlyList<GuildPaddock> shownPaddocks;
        private IReadOnlyList<GuildHouse> shownHouses;
        private IReadOnlyList<GuildSpell> shownSpells;

        public GuildPanel()
        {
            // Épée du combat lue d'avance hors du fil de l'interface (comme le volet Groupe).
            _ = PartyArtworks.LoadAsync(PartyArtworks.UiFamily, PartyArtworks.Info);
        }

        public override string Title => GuildTexts.Get("GUILD", "Guilde");
        public override Image Icon => ClientAssets.Icon("icone-guilde", 24);
        public GuildTab CurrentTab => tab;
        /// <summary>Boîte Oui / Non / Ignorer de l'invitation reçue encore affichée, sinon <c>null</c>.</summary>
        public Form InvitationDialog => inviteDialog;
        public GuildMemberList MemberList => members;
        public GuildMemberSheet MemberSheet => sheet;
        public ListView CollectorList => collectors;
        public GuildTabStrip Tabs => tabs;

        protected override Control CreateView()
        {
            tips = new ToolTip { InitialDelay = 300, ReshowDelay = 100 };
            var page = Page();
            var header = new Panel { Dock = DockStyle.Top, Height = EmblemSize + 8, BackColor = BotUi.Paper, Margin = new Padding(0), Padding = new Padding(0, 2, 0, 2) };
            emblemBox = new PictureBox { Dock = DockStyle.Left, Width = EmblemSize + 8, SizeMode = PictureBoxSizeMode.CenterImage, BackColor = Color.Transparent, Name = "guild-emblem" };
            var texts = new Panel { Dock = DockStyle.Fill, BackColor = BotUi.Paper, Padding = new Padding(4, 0, 0, 0), Margin = new Padding(0) };
            nameLabel = MakeLabel(string.Empty, 11, true); nameLabel.Dock = DockStyle.Top; nameLabel.Height = 22; nameLabel.AutoEllipsis = true; nameLabel.Name = "guild-name";
            levelLabel = MakeLabel(string.Empty, 8.25f); levelLabel.Dock = DockStyle.Top; levelLabel.Height = 16; levelLabel.ForeColor = BotUi.Muted; levelLabel.AutoEllipsis = true; levelLabel.Name = "guild-level";
            countLabel = MakeLabel(string.Empty, 8.25f); countLabel.Dock = DockStyle.Top; countLabel.Height = 16; countLabel.ForeColor = BotUi.Muted; countLabel.AutoEllipsis = true; countLabel.Name = "guild-count";
            texts.Controls.Add(countLabel); texts.Controls.Add(levelLabel); texts.Controls.Add(nameLabel);
            header.Controls.Add(texts); header.Controls.Add(emblemBox);

            tabs = new GuildTabStrip { Dock = DockStyle.Top, Name = "guild-tabs" };
            tabs.TabClicked += (s, e) => SelectTab(e);

            pages = new Panel[5];
            pages[(int)GuildTab.Members] = BuildMembers();
            pages[(int)GuildTab.Boosts] = BuildBoosts();
            pages[(int)GuildTab.Collectors] = BuildCollectors();
            pages[(int)GuildTab.Paddocks] = BuildPaddocks();
            pages[(int)GuildTab.Houses] = BuildHouses();
            var content = new Panel { Dock = DockStyle.Fill, BackColor = BotUi.Paper, Margin = new Padding(0) };
            foreach (Panel item in pages) { item.Visible = false; content.Controls.Add(item); }

            creationBar = BuildCreation();
            status = MakeStatus(string.Empty); status.Name = "guild-status";
            // Ordre d'ancrage : le dernier ajouté prend le bord en premier.
            page.Controls.Add(content); page.Controls.Add(creationBar); page.Controls.Add(tabs); page.Controls.Add(header); page.Controls.Add(status);
            return page;
        }

        private Panel BuildMembers()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = BotUi.Paper, Margin = new Padding(0) };
            members = new GuildMemberList { Dock = DockStyle.Top, Name = "guild-members", AccessibleName = "Membres de la guilde" };
            members.RowClicked += (s, e) => ShowMember(e.Member);
            var area = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = BotUi.Paper, Margin = new Padding(0) };
            area.Controls.Add(members);
            sheet = new GuildMemberSheet { Dock = DockStyle.Bottom, Visible = false, Name = "guild-member-sheet" };
            sheet.ApplyRequested += async (s, e) => await ApplyProfile(e);
            sheet.KickRequested += async (s, e) => await Kick(e);
            panel.Controls.Add(area); panel.Controls.Add(sheet);
            return panel;
        }

        private Panel BuildBoosts()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = BotUi.Paper, Margin = new Padding(0), AutoScroll = true };
            var title = MakeLabel(GuildTexts.Get("GUILD_TAXCHARACTERISTICS", "Caractéristiques d'un percepteur"), 9, true); title.Dock = DockStyle.Top; title.Height = 22;
            boostPoints = MakeLabel(string.Empty, 8.25f); boostPoints.Dock = DockStyle.Top; boostPoints.Height = 18; boostPoints.ForeColor = BotUi.Muted; boostPoints.Name = "guild-boost-points";
            var table = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 3, AutoSize = true, Margin = new Padding(0), Padding = new Padding(0, 2, 0, 2), BackColor = BotUi.Paper };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));
            boostLife = AddBoostRow(table, GuildTexts.Get("LIFEPOINTS", "Points de vie"), null);
            boostDamage = AddBoostRow(table, GuildTexts.Get("DAMAGES_BONUS", "Bonus aux dommages"), null);
            foreach (GuildBoostKind kind in BoostKinds) boostValues[kind] = AddBoostRow(table, GuildTexts.BoostName(kind), kind);
            var spellsTitle = MakeLabel(GuildTexts.Get("GUILD_TAXSPELLS", "Sorts"), 9, true); spellsTitle.Dock = DockStyle.Top; spellsTitle.Height = 22;
            spells = MakeList(8.25f, GuildTexts.Get("GUILD_TAXSPELLS", "Sorts"), GuildTexts.Get("LEVEL_SMALL", "Niv."));
            spells.Columns[0].Width = 230; spells.Columns[1].Width = 50; spells.Dock = DockStyle.Top; spells.Height = 96; spells.Name = "guild-spells";
            spells.SelectedIndexChanged += (s, e) => UpdateButtons();
            spellBoost = MakeButton("+ " + GuildTexts.Get("LEVEL_SMALL", "Niv."), async (s, e) => await BoostSpell(), false, 90); spellBoost.Name = "guild-spell-boost";
            var spellActions = BotUi.Actions(spellBoost); spellActions.Dock = DockStyle.Top;
            boostCount = MakeLabel(string.Empty, 8.25f); boostCount.Dock = DockStyle.Top; boostCount.Height = 18; boostCount.ForeColor = BotUi.Muted; boostCount.Name = "guild-tax-count";
            hire = MakeButton(GuildTexts.Get("HIRE_TAXCOLLECTOR", "Poser un percepteur"), async (s, e) => await Hire(), true, 190); hire.Name = "guild-hire";
            var hireActions = BotUi.Actions(hire); hireActions.Dock = DockStyle.Top;
            panel.Controls.Add(hireActions); panel.Controls.Add(boostCount); panel.Controls.Add(spellActions); panel.Controls.Add(spells); panel.Controls.Add(spellsTitle);
            panel.Controls.Add(table); panel.Controls.Add(boostPoints); panel.Controls.Add(title);
            return panel;
        }

        private Label AddBoostRow(TableLayoutPanel table, string name, GuildBoostKind? kind)
        {
            int row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            var label = MakeLabel(name, 8.25f); label.Dock = DockStyle.Fill; label.TextAlign = ContentAlignment.MiddleLeft; label.AutoEllipsis = true;
            var value = MakeLabel("–", 8.25f, true); value.Dock = DockStyle.Fill; value.TextAlign = ContentAlignment.MiddleRight;
            value.Name = "guild-boost-" + (kind.HasValue ? kind.Value.ToString().ToLowerInvariant() : name.ToLowerInvariant().Replace(' ', '-'));
            table.Controls.Add(label, 0, row); table.Controls.Add(value, 1, row);
            if (kind.HasValue)
            {
                GuildBoostKind chosen = kind.Value;
                Control plus = MakeButton("+", async (s, e) => await Run(guild => guild.BoostAsync(chosen)), false, 36);
                plus.Dock = DockStyle.Fill; plus.Margin = new Padding(4, 2, 0, 2); plus.Name = "guild-boost-plus-" + chosen.ToString().ToLowerInvariant();
                boostButtons[chosen] = plus;
                table.Controls.Add(plus, 2, row);
            }
            return value;
        }

        private Panel BuildCollectors()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = BotUi.Paper, Margin = new Padding(0) };
            collectorsInfo = MakeLabel(string.Empty, 8.25f); collectorsInfo.Dock = DockStyle.Top; collectorsInfo.Height = 20; collectorsInfo.ForeColor = BotUi.Muted; collectorsInfo.Name = "guild-collectors-info";
            collectors = MakeList(8.25f, GuildTexts.Get("GUILD_TAXCOLLECTORS", "Percepteurs"), "Position", GuildTexts.Get("STATE", "Etat"), "Places");
            collectors.Columns[0].Width = 120; collectors.Columns[1].Width = 140; collectors.Columns[2].Width = 70; collectors.Columns[3].Width = 50; collectors.Name = "guild-collectors";
            collectors.SelectedIndexChanged += (s, e) => UpdateButtons();
            removeCollector = MakeButton(GuildTexts.Get("REMOVE", "Retirer"), async (s, e) => await RemoveSelectedCollector(), false, 120); removeCollector.Name = "guild-collector-remove";
            var actions = BotUi.Actions(removeCollector); actions.Dock = DockStyle.Bottom;
            panel.Controls.Add(collectors); panel.Controls.Add(actions); panel.Controls.Add(collectorsInfo);
            return panel;
        }

        private Panel BuildPaddocks()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = BotUi.Paper, Margin = new Padding(0) };
            paddocksInfo = MakeLabel(string.Empty, 8.25f); paddocksInfo.Dock = DockStyle.Top; paddocksInfo.Height = 20; paddocksInfo.ForeColor = BotUi.Muted; paddocksInfo.Name = "guild-paddocks-info";
            paddocks = MakeList(8.25f, GuildTexts.Get("MOUNT_PARK", "Enclos"), "Taille", "Objets", "Montures");
            paddocks.Columns[0].Width = 150; paddocks.Columns[1].Width = 50; paddocks.Columns[2].Width = 50; paddocks.Columns[3].Width = 130; paddocks.Name = "guild-paddocks";
            panel.Controls.Add(paddocks); panel.Controls.Add(paddocksInfo);
            return panel;
        }

        private Panel BuildHouses()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = BotUi.Paper, Margin = new Padding(0) };
            housesInfo = MakeLabel(string.Empty, 8.25f); housesInfo.Dock = DockStyle.Top; housesInfo.Height = 20; housesInfo.ForeColor = BotUi.Muted; housesInfo.Name = "guild-houses-info";
            houses = MakeList(8.25f, GuildTexts.Get("HOUSES_WORD", "Maisons"), "Propriétaire", "Position", GuildTexts.Get("RIGHTS", "Droits"));
            houses.Columns[0].Width = 70; houses.Columns[1].Width = 120; houses.Columns[2].Width = 80; houses.Columns[3].Width = 100; houses.Name = "guild-houses";
            panel.Controls.Add(houses); panel.Controls.Add(housesInfo);
            return panel;
        }

        /// <summary>Panneau <c>CreateGuild</c> réduit : nom, fond et motif (identifiants) et leurs couleurs (hexadécimal) ; visible après <c>gn</c>.</summary>
        private Panel BuildCreation()
        {
            var bar = new Panel { Dock = DockStyle.Top, Height = 96, BackColor = BotUi.PaperLight, Padding = new Padding(6, 3, 6, 3), Visible = false, Name = "guild-creation" };
            var title = MakeLabel("Création de guilde (panneau ouvert par le serveur)", 9, true); title.Dock = DockStyle.Top; title.Height = 20;
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 2, Margin = new Padding(0), BackColor = BotUi.PaperLight };
            for (int column = 0; column < 6; column++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, column % 2 == 0 ? 12 : 21));
            createName = new TextBox { Dock = DockStyle.Fill, Font = BotFonts.Get(9), BackColor = BotUi.Paper, ForeColor = BotUi.Ink, BorderStyle = BorderStyle.FixedSingle, MaxLength = 20,
                Name = "guild-create-name", AccessibleName = GuildTexts.Get("GUILD_NAME", "Nom de la guilde") };
            createBack = new NumericUpDown { Minimum = 1, Maximum = ClientAssets.EmblemBacks, Value = 1, Dock = DockStyle.Fill, Font = BotFonts.Get(9), BackColor = BotUi.Paper, Name = "guild-create-back" };
            createUp = new NumericUpDown { Minimum = 1, Maximum = ClientAssets.EmblemUps, Value = 1, Dock = DockStyle.Fill, Font = BotFonts.Get(9), BackColor = BotUi.Paper, Name = "guild-create-up" };
            createBackColor = new TextBox { Dock = DockStyle.Fill, Font = BotFonts.Get(9), BackColor = BotUi.Paper, ForeColor = BotUi.Ink, BorderStyle = BorderStyle.FixedSingle, MaxLength = 6, Text = "000000", Name = "guild-create-back-color" };
            createUpColor = new TextBox { Dock = DockStyle.Fill, Font = BotFonts.Get(9), BackColor = BotUi.Paper, ForeColor = BotUi.Ink, BorderStyle = BorderStyle.FixedSingle, MaxLength = 6, Text = "FFFFFF", Name = "guild-create-up-color" };
            grid.Controls.Add(Caption("Nom"), 0, 0); grid.Controls.Add(createName, 1, 0);
            grid.Controls.Add(Caption("Fond"), 2, 0); grid.Controls.Add(createBack, 3, 0);
            grid.Controls.Add(Caption("Couleur"), 4, 0); grid.Controls.Add(createBackColor, 5, 0);
            grid.Controls.Add(Caption("Motif"), 2, 1); grid.Controls.Add(createUp, 3, 1);
            grid.Controls.Add(Caption("Couleur"), 4, 1); grid.Controls.Add(createUpColor, 5, 1);
            createButton = MakeButton("Créer", async (s, e) => await Create(), true, 80); createButton.Name = "guild-create";
            cancelCreate = MakeButton("Annuler", async (s, e) => await Run(guild => guild.LeaveCreationAsync()), false, 80); cancelCreate.Name = "guild-create-cancel";
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0), Padding = new Padding(0), BackColor = BotUi.PaperLight };
            actions.Controls.Add(createButton); actions.Controls.Add(cancelCreate);
            grid.Controls.Add(actions, 0, 1); grid.SetColumnSpan(actions, 2);
            bar.Controls.Add(grid); bar.Controls.Add(title);
            return bar;
        }

        private static Label Caption(string text)
        {
            var label = MakeLabel(text, 8.25f); label.Dock = DockStyle.Fill; label.TextAlign = ContentAlignment.MiddleLeft; label.AutoEllipsis = true;
            return label;
        }

        protected override void OnBind(GameClass game)
        {
            bound = game.Interactions?.Guild;
            if (bound != null)
            {
                bound.Changed += OnGuildChanged;
                bound.ErrorBox += OnErrorBox;
                bound.InvitationClosed += OnInvitationClosed;
                bound.OpenTabRequested += OnOpenTabRequested;
                bound.CollectorAlert += OnCollectorAlert;
                bound.WindowOpen = Host != null && Host.IsOpen(this);
            }
            session = game.Session;
            if (session != null) session.GuildInviteReceived += OnInviteReceived;
            watched = Host;
            if (watched != null) { watched.PanelShown += OnPanelShown; watched.PanelClosed += OnPanelClosed; }
        }

        protected override void OnUnbind(GameClass game)
        {
            if (bound != null)
            {
                bound.Changed -= OnGuildChanged;
                bound.ErrorBox -= OnErrorBox;
                bound.InvitationClosed -= OnInvitationClosed;
                bound.OpenTabRequested -= OnOpenTabRequested;
                bound.CollectorAlert -= OnCollectorAlert;
                bound.WindowOpen = false;
            }
            if (session != null) session.GuildInviteReceived -= OnInviteReceived;
            if (watched != null) { watched.PanelShown -= OnPanelShown; watched.PanelClosed -= OnPanelClosed; }
            bound = null; session = null; watched = null;
            CloseInviteDialog();
        }

        // ----- Ouverture, onglets (fil de l'interface) -----

        private void OnPanelShown(object sender, PanelEventArgs e)
        {
            if (!ReferenceEquals(e?.Panel, this) || bound == null || bound.WindowOpen) return;
            // Comme loadUIAutoHideComponent("Guild") : informations générales puis l'onglet courant.
            bound.WindowOpen = true;
            GuildTab current = tab;
            _ = Run(async guild =>
            {
                InteractionResult first = await guild.OpenAsync();
                return first.Sent ? await guild.RequestAsync(current) : first;
            });
        }

        private void OnPanelClosed(object sender, PanelEventArgs e)
        {
            if (!ReferenceEquals(e?.Panel, this) || bound == null) return;
            bound.WindowOpen = false;
            // Guild.leaveTaxInterface du client : gITV à la fermeture.
            if (bound.HasGuild && Connected) _ = bound.CloseAsync();
        }

        /// <summary>Change d'onglet et demande son contenu (<c>gIM</c>…), comme <c>setCurrentTab</c> du client.</summary>
        public void SelectTab(GuildTab value)
        {
            if (tab == value && tabs != null && tabs.Selected == value) return;
            tab = value;
            RefreshView();
            if (bound != null && bound.WindowOpen) _ = Run(guild => guild.RequestAsync(value));
        }

        // ----- Événements du modèle (fil réseau) -----

        private void OnGuildChanged() => Post(RefreshView);

        private void OnErrorBox(string title, string text) => Post(async () =>
        {
            try { await BotDialogs.InfoAsync(Host, title, text); }
            catch (Exception error) { Account?.Logger?.LogException(Reference, error); }
        });

        private void OnInvitationClosed() => Post(() => { CloseInviteDialog(); RefreshView(); });

        private void OnCollectorAlert(string text) => Post(() => Feedback(text));

        /// <summary><c>gUT</c> / <c>gUF</c> / <c>gCK</c> : la fenêtre s'ouvre sur l'onglet demandé (sauf devant une fenêtre serveur).</summary>
        private void OnOpenTabRequested(GuildTab requested) => Post(() =>
        {
            bool wasOpen = Host != null && Host.IsOpen(this);
            tab = requested;
            ShowUnlessServerWindow();
            RefreshView();
            if (wasOpen && bound != null && bound.WindowOpen) _ = Run(guild => guild.RequestAsync(requested));
        });

        /// <summary>
        /// Invitation reçue (fil réseau) : prise en charge seulement si la fenêtre de jeu peut poser la question ; sinon le modèle
        /// refuse lui-même (<c>gJE&lt;id&gt;</c>). La réponse n'est envoyée qu'après le choix du joueur.
        /// </summary>
        private void OnInviteReceived(object sender, InvitationEventArgs e)
        {
            GuildActions guild = bound;
            if (IsDisposed || e == null || guild == null || Host == null || Host.IsDisposed) return;
            string id = e.InviterId, inviter = e.InviterName, name = e.GuildName;
            if (Post(() => AskInvitation(guild, id, inviter, name))) e.Handled = true;
        }

        private async void AskInvitation(GuildActions guild, string id, string inviter, string guildName)
        {
            try
            {
                if (IsDisposed || !string.Equals(guild.PendingInviterId, id, StringComparison.Ordinal)) return;
                CloseInviteDialog();
                Form[] before = BotDialogs.OpenDialogs.ToArray();
                Task<BotDialogResult> question = BotDialogs.AskYesNoIgnoreAsync(Host, GuildTexts.Get("GUILD", "Guilde"),
                    GuildTexts.Get("A_INVIT_YOU_IN_GUILD", "{0} vous invite à rejoindre sa guilde ({1}). Acceptez-vous ?", inviter, guildName), InviteDialogName);
                inviteShown = id;
                inviteDialog = BotDialogs.OpenDialogs.Except(before).LastOrDefault();
                BotDialogResult answer = await question;
                if (string.Equals(inviteShown, id, StringComparison.Ordinal)) { inviteShown = null; inviteDialog = null; }
                // Invitation close entre-temps (gJC, gJKj, déconnexion) : plus rien à répondre.
                if (!string.Equals(guild.PendingInviterId, id, StringComparison.Ordinal)) return;
                Func<Task<InteractionResult>> reply;
                if (answer == BotDialogResult.Yes) reply = guild.AcceptAsync;
                else if (answer == BotDialogResult.Ignore) reply = guild.IgnoreAsync;
                else reply = guild.RefuseAsync;
                if (IsDisposed) await reply();
                else await ReportAsync(reply);
            }
            catch (Exception error) { Account?.Logger?.LogException(Reference, error); }
        }

        private void CloseInviteDialog()
        {
            Form dialog = inviteDialog;
            inviteDialog = null; inviteShown = null;
            if (dialog != null && !dialog.IsDisposed) dialog.Close();
        }

        private void ShowUnlessServerWindow()
        {
            if (Host == null || Host.IsDisposed) return;
            if (Host.Current is GamePanel current && !ReferenceEquals(current, this) && current.IsServerWindowOpen) return;
            RequestShow();
        }

        /// <summary>Passe par la fenêtre de jeu (le tiroir jamais affiché n'a pas de poignée) ; faux si aucune fenêtre ne peut la recevoir.</summary>
        private bool Post(Action action) => ExchangePanel.PostToForm(Host, () => { if (!IsDisposed) action(); });

        // ----- Affichage -----

        public override void RefreshView()
        {
            GuildActions guild = Game?.Interactions?.Guild;
            if (guild == null || tabs == null || IsDisposed) return;
            Guild model = guild.Guild;
            tabs.Selected = tab;
            for (int index = 0; index < pages.Length; index++) pages[index].Visible = index == (int)tab;
            nameLabel.Text = model.HasGuild ? model.Name : "Aucune guilde";
            if (!model.HasGuild) levelLabel.Text = GuildTexts.Get("UI_ONLY_FOR_GUILD", "Pour accéder à cette interface, il faut que tu fasses partie d'une guilde.");
            else if (!model.GeneralReceived) levelLabel.Text = "Niveau et expérience en attente (gIG).";
            else levelLabel.Text = GuildTexts.Get("LEVEL", "Niveau") + " " + model.Level.ToString(CultureInfo.CurrentCulture) + " · "
                + model.Xp.ToString("N0", CultureInfo.CurrentCulture) + " / " + (model.XpMax < 0 ? "max" : model.XpMax.ToString("N0", CultureInfo.CurrentCulture)) + " xp"
                + (model.IsValid ? string.Empty : " · " + GuildTexts.Get("GUILD_INVALID_INFOS", "La guilde doit compter un minimum de 10 membres pour être valide."));
            countLabel.Text = model.MembersReceived
                ? model.Members.Count.ToString(CultureInfo.CurrentCulture) + " membre(s), " + model.OnlineCount.ToString(CultureInfo.CurrentCulture) + " connecté(s) · "
                    + GuildRights.Describe(model.OwnRights)
                : model.HasGuild ? GuildRights.Describe(model.OwnRights) : string.Empty;
            UpdateEmblem(model);
            long selfId = Game.character?.id ?? 0;
            switch (tab)
            {
                case GuildTab.Members:
                    members.SetMembers(model.Members, selfId);
                    members.EmptyText = model.MembersReceived ? "Aucun membre." : "Liste des membres en attente (gIM).";
                    if (sheet.Visible)
                    {
                        GuildMember current = sheet.Current == null ? null : model.FindMember(sheet.Current.Id);
                        if (current == null) sheet.Clear(); else sheet.Show(current, model, selfId, Connected);
                    }
                    break;
                case GuildTab.Boosts: RefreshBoosts(model); break;
                case GuildTab.Collectors: RefreshCollectors(model); break;
                case GuildTab.Paddocks: RefreshPaddocks(model); break;
                case GuildTab.Houses: RefreshHouses(model); break;
            }
            creationBar.Visible = guild.CreationWindowOpen;
            status.Text = guild.LastMessage;
            UpdateButtons();
        }

        private void RefreshBoosts(Guild model)
        {
            GuildBoosts boosts = model.Boosts;
            boostPoints.Text = boosts == null
                ? (model.BoostsReceived ? "Aucun boost (gIB vide)." : "Personnalisation en attente (gIB).")
                : GuildTexts.Get("GUILD_BONUSPOINTS", "Reste à répartir") + " : " + GuildTexts.Get("POINTS", "{0} point(s)", boosts.Points.ToString(CultureInfo.CurrentCulture));
            boostLife.Text = boosts == null ? "–" : boosts.Life.ToString(CultureInfo.CurrentCulture);
            boostDamage.Text = boosts == null ? "–" : boosts.Damage.ToString(CultureInfo.CurrentCulture);
            foreach (GuildBoostKind kind in BoostKinds)
            {
                GuildBoostRule rule = GuildTexts.BoostRule(kind);
                boostValues[kind].Text = boosts == null ? "–" : boosts.Value(kind).ToString(CultureInfo.CurrentCulture) + " / " + rule.Max.ToString(CultureInfo.CurrentCulture);
                tips.SetToolTip(boostButtons[kind], rule.Cost.ToString(CultureInfo.CurrentCulture) + " point(s) → +" + rule.Gain.ToString(CultureInfo.CurrentCulture) + " (gB" + (char)kind + ")");
            }
            IReadOnlyList<GuildSpell> list = boosts?.Spells ?? new GuildSpell[0];
            if (!ReferenceEquals(list, shownSpells))
            {
                shownSpells = list;
                int selected = spells.SelectedItems.Count == 0 ? -1 : (int)spells.SelectedItems[0].Tag;
                spells.BeginUpdate(); spells.Items.Clear();
                foreach (GuildSpell spell in list)
                {
                    var row = spells.Items.Add(LangData.Spell.Has(spell.Id) ? LangData.Spell.Name(spell.Id) : "Sort n° " + spell.Id.ToString(CultureInfo.InvariantCulture));
                    row.SubItems.Add(spell.Level.ToString(CultureInfo.CurrentCulture)); row.Tag = spell.Id;
                    if (spell.Id == selected) row.Selected = true;
                }
                spells.EndUpdate();
            }
            boostCount.Text = boosts == null ? string.Empty
                : GuildTexts.Get("GUILD_TAX_COUNT", "Actuellement {0} sur {1} percepteurs", boosts.Collectors.ToString(CultureInfo.CurrentCulture), boosts.MaxCollectors.ToString(CultureInfo.CurrentCulture))
                    + " · " + GuildTexts.Get("HIRE_TAXCOLLECTOR", "Poser un percepteur") + " : " + boosts.HireCost.ToString("N0", CultureInfo.CurrentCulture) + " kamas";
        }

        private void RefreshCollectors(Guild model)
        {
            IReadOnlyList<GuildCollector> list = model.Collectors;
            collectorsInfo.Text = !model.CollectorsReceived ? "Liste des percepteurs en attente (gITM)."
                : list.Count == 0 ? "Aucun percepteur posé."
                : list.Count.ToString(CultureInfo.CurrentCulture) + " percepteur(s) ; la défense (gTJ / gTV) n'est pas proposée : StarLoco ne la gère pas.";
            if (ReferenceEquals(list, shownCollectors)) return;
            shownCollectors = list;
            long selected = collectors.SelectedItems.Count == 0 ? -1 : ((GuildCollector)collectors.SelectedItems[0].Tag).Id;
            collectors.BeginUpdate(); collectors.Items.Clear();
            foreach (GuildCollector collector in list)
            {
                var row = collectors.Items.Add(collector.Name);
                row.SubItems.Add(collector.Position);
                row.SubItems.Add(collector.IsInFight ? "Combat" + (collector.Attackers.Count > 0 ? " (" + collector.Attackers.Count.ToString(CultureInfo.CurrentCulture) + " att.)" : string.Empty) : "Récolte");
                row.SubItems.Add(collector.Defenders.Count.ToString(CultureInfo.CurrentCulture) + "/" + collector.MaxPlayers.ToString(CultureInfo.CurrentCulture));
                row.ToolTipText = "Posé par " + collector.CallerName + (collector.HasDetails ? " · dernier récolteur : " + collector.LastHarvesterName : string.Empty);
                row.Tag = collector;
                if (collector.Id == selected) row.Selected = true;
            }
            collectors.EndUpdate();
        }

        private void RefreshPaddocks(Guild model)
        {
            IReadOnlyList<GuildPaddock> list = model.Paddocks;
            paddocksInfo.Text = !model.PaddocksReceived ? "Liste des enclos en attente (gIF)."
                : GuildTexts.Get("GUILD_MOUNTPARKS_COUNT", "Actuellement {0} sur {1} enclos", list.Count.ToString(CultureInfo.CurrentCulture), model.MaxPaddocks.ToString(CultureInfo.CurrentCulture));
            if (ReferenceEquals(list, shownPaddocks)) return;
            shownPaddocks = list;
            paddocks.BeginUpdate(); paddocks.Items.Clear();
            foreach (GuildPaddock paddock in list)
            {
                var row = paddocks.Items.Add(paddock.Position);
                row.SubItems.Add(paddock.Size.ToString(CultureInfo.CurrentCulture));
                row.SubItems.Add(paddock.MaxObjects.ToString(CultureInfo.CurrentCulture));
                row.SubItems.Add(paddock.Mounts.Count == 0 ? "–" : string.Join(", ", paddock.Mounts.Select(mount => mount.Name + " (" + mount.OwnerName + ")")));
                row.Tag = paddock;
            }
            paddocks.EndUpdate();
        }

        private void RefreshHouses(Guild model)
        {
            IReadOnlyList<GuildHouse> list = model.Houses;
            housesInfo.Text = !model.HousesReceived ? "Liste des maisons en attente (gIH)." : list.Count.ToString(CultureInfo.CurrentCulture) + " maison(s) de guilde.";
            if (ReferenceEquals(list, shownHouses)) return;
            shownHouses = list;
            houses.BeginUpdate(); houses.Items.Clear();
            foreach (GuildHouse house in list)
            {
                var row = houses.Items.Add(house.Id.ToString(CultureInfo.CurrentCulture));
                row.SubItems.Add(house.OwnerName.Length > 0 ? house.OwnerName : "–");
                row.SubItems.Add("[" + house.X.ToString(CultureInfo.InvariantCulture) + "," + house.Y.ToString(CultureInfo.InvariantCulture) + "]");
                row.SubItems.Add(house.RightsRaw.Length > 0 ? house.RightsRaw : "–");
                row.Tag = house;
            }
            houses.EndUpdate();
        }

        private void UpdateEmblem(Guild model)
        {
            string emblem = model.HasGuild ? model.Emblem : null;
            if (string.Equals(emblem, emblemShown, StringComparison.Ordinal)) return;
            emblemShown = emblem;
            int request = ++emblemRequest;
            if (emblem == null) { SetEmblem(null); return; }
            // Les PNG de l'emblème sont lus et composés sur le pool de fils ; l'image arrive par la fenêtre de jeu.
            Task.Run(() => { try { return ClientAssets.Emblem(emblem, EmblemSize); } catch (Exception) { return null; } })
                .ContinueWith(task =>
                {
                    Bitmap image = task.Status == TaskStatus.RanToCompletion ? task.Result : null;
                    if (!Post(() => { if (request == emblemRequest) SetEmblem(image); else image?.Dispose(); })) image?.Dispose();
                }, TaskScheduler.Default);
        }

        private void SetEmblem(Bitmap image)
        {
            if (emblemBox == null || emblemBox.IsDisposed) { image?.Dispose(); return; }
            Bitmap previous = emblemImage;
            emblemBox.Image = image;
            emblemImage = image;
            previous?.Dispose();
        }

        private void UpdateButtons()
        {
            GuildActions guild = Game?.Interactions?.Guild;
            if (guild == null || hire == null || IsDisposed) return;
            Guild model = guild.Guild;
            GuildBoosts boosts = model.Boosts;
            bool canBoost = Connected && model.CanDo(GuildRight.Boost);
            foreach (GuildBoostKind kind in BoostKinds)
            {
                GuildBoostRule rule = GuildTexts.BoostRule(kind);
                boostButtons[kind].Enabled = canBoost && (boosts == null || (boosts.Points >= rule.Cost && boosts.Value(kind) < rule.Max));
            }
            spellBoost.Enabled = canBoost && spells.SelectedItems.Count == 1 && (boosts == null || boosts.Points >= GuildTexts.SpellRule().Cost);
            hire.Enabled = Connected && model.CanDo(GuildRight.HireCollector);
            removeCollector.Enabled = Connected && model.CanDo(GuildRight.HireCollector) && collectors.SelectedItems.Count == 1;
            createButton.Enabled = Connected && guild.CreationWindowOpen && !model.HasGuild;
            cancelCreate.Enabled = Connected && guild.CreationWindowOpen;
        }

        // ----- Actions -----

        private Task Run(Func<GuildActions, Task<InteractionResult>> action)
        {
            GuildActions guild = Game?.Interactions?.Guild;
            return guild == null ? Task.CompletedTask : ReportAsync(() => action(guild));
        }

        /// <summary>Clic sur une ligne : fiche du membre (<c>GuildMemberInfos</c>) sous la liste.</summary>
        public void ShowMember(GuildMember member)
        {
            GuildActions guild = Game?.Interactions?.Guild;
            if (member == null || guild == null || sheet == null) return;
            sheet.Show(member, guild.Guild, Game.character?.id ?? 0, Connected);
            members.SelectedId = member.Id;
        }

        private async Task ApplyProfile(GuildProfileEventArgs e)
        {
            GuildActions guild = Game?.Interactions?.Guild;
            if (guild == null || e == null) return;
            long selfId = Game.character?.id ?? 0;
            if (e.Rank == 1 && guild.Guild.IsBoss && e.MemberId != selfId)
            {
                // DO_U_GIVERIGHTS : le meneur cède sa place.
                BotDialogResult answer = await BotDialogs.AskYesNoAsync(Host, GuildTexts.Get("GUILD", "Guilde"),
                    GuildTexts.Get("DO_U_GIVERIGHTS", "En donnant le rang de « Meneur » à ce membre, vous perdrez votre statut de chef. Le nouveau chef de la guilde sera {0}. Voulez-vous vraiment continuer ?", e.Name));
                if (answer != BotDialogResult.Yes) { Feedback("Changement de meneur annulé."); return; }
            }
            await ReportAsync(() => guild.SetProfileAsync(e.MemberId, e.Rank, e.XpPercent, e.Rights));
        }

        private async Task Kick(GuildMember member)
        {
            GuildActions guild = Game?.Interactions?.Guild;
            if (guild == null || member == null) return;
            bool self = member.Id == (Game.character?.id ?? 0);
            BotDialogResult answer = await BotDialogs.AskYesNoAsync(Host, GuildTexts.Get("GUILD", "Guilde"), self
                ? GuildTexts.Get("DO_U_DELETE_YOU", "Voulez-vous vraiment quitter votre guilde ?")
                : GuildTexts.Get("DO_U_DELETE_MEMBER", "Voulez-vous vraiment exclure {0} de ta guilde ?", member.Name));
            if (answer != BotDialogResult.Yes) return;
            await ReportAsync(() => self ? guild.LeaveAsync() : guild.KickAsync(member.Name));
        }

        private Task BoostSpell()
        {
            if (spells.SelectedItems.Count != 1) return Task.CompletedTask;
            int spellId = (int)spells.SelectedItems[0].Tag;
            return Run(guild => guild.BoostSpellAsync(spellId));
        }

        private async Task Hire()
        {
            GuildActions guild = Game?.Interactions?.Guild;
            if (guild == null) return;
            string cost = guild.Guild.Boosts?.HireCost.ToString("N0", CultureInfo.CurrentCulture) ?? "?";
            BotDialogResult answer = await BotDialogs.AskYesNoAsync(Host, GuildTexts.Get("HIRE_TAXCOLLECTOR", "Poser un percepteur"),
                GuildTexts.Get("DO_YOU_HIRE_TAXCOLLECTOR", "Pour placer un percepteur sur cette carte, vous devez dépenser {0} kamas. Voulez-vous continuer ?", cost));
            if (answer != BotDialogResult.Yes) return;
            await ReportAsync(guild.HireCollectorAsync);
        }

        private async Task RemoveSelectedCollector()
        {
            GuildActions guild = Game?.Interactions?.Guild;
            if (guild == null || collectors.SelectedItems.Count != 1) return;
            var collector = collectors.SelectedItems[0].Tag as GuildCollector;
            if (collector == null) return;
            BotDialogResult answer = await BotDialogs.AskYesNoAsync(Host, GuildTexts.Get("GUILD", "Guilde"),
                GuildTexts.Get("DO_U_REMOVE_TAXCOLLECTOR", "Etes vous sur de vouloir retirer le percepteur {0} ?", collector.Name));
            if (answer != BotDialogResult.Yes) return;
            long id = collector.Id;
            await ReportAsync(() => guild.RemoveCollectorAsync(id));
        }

        private Task Create()
        {
            int backColor = ParseColor(createBackColor.Text), upColor = ParseColor(createUpColor.Text);
            if (backColor < 0 || upColor < 0) { Feedback("Couleur invalide : six chiffres hexadécimaux (RRGGBB)."); return Task.CompletedTask; }
            string name = createName.Text.Trim();
            return Run(guild => guild.CreateAsync((int)createBack.Value, backColor, (int)createUp.Value, upColor, name));
        }

        private static int ParseColor(string text)
        {
            int value;
            text = (text ?? string.Empty).Trim().TrimStart('#');
            return text.Length > 0 && text.Length <= 6 && int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value) ? value : -1;
        }

        protected internal override bool OnUserClose() => true;

        protected override void Dispose(bool disposing)
        {
            if (!disposing) return;
            tips?.Dispose();
            if (emblemBox != null && !emblemBox.IsDisposed) emblemBox.Image = null;
            emblemImage?.Dispose(); emblemImage = null;
        }
    }

    /// <summary>Onglets de la fenêtre <c>Guild</c> du client (<c>_btnTabMembers</c>, <c>_btnTabBoosts</c>, <c>_btnTabTaxCollectors</c>, <c>_btnTabMountParks</c>, <c>_btnTabGuildHouses</c>).</summary>
    public sealed class GuildTabStrip : Control
    {
        public const int TabHeight = 24;
        private static readonly GuildTab[] Kinds = { GuildTab.Members, GuildTab.Boosts, GuildTab.Collectors, GuildTab.Paddocks, GuildTab.Houses };
        private GuildTab selected = GuildTab.Members;

        public GuildTabStrip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = BotUi.Paper; Height = TabHeight + 3; Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.PageTabList;
        }

        public event EventHandler<GuildTab> TabClicked;

        public GuildTab Selected
        {
            get => selected;
            set { if (selected == value) return; selected = value; Invalidate(); }
        }

        /// <summary>Rectangle de l'onglet (un cinquième de la largeur chacun).</summary>
        public Rectangle TabBounds(GuildTab tab)
        {
            int width = Math.Max(30, (Width - 4 * 3) / 5);
            return new Rectangle((int)tab * (width + 3), 0, width, TabHeight);
        }

        /// <summary>Simule un clic sur l'onglet (raccourci clavier, tests).</summary>
        public void ClickTab(GuildTab tab) => TabClicked?.Invoke(this, tab);

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var paper = new SolidBrush(BackColor)) e.Graphics.FillRectangle(paper, ClientRectangle);
            using (var rule = new Pen(BotUi.Gold)) e.Graphics.DrawLine(rule, 0, TabHeight, Width, TabHeight);
            foreach (GuildTab tab in Kinds)
            {
                Rectangle area = TabBounds(tab);
                bool active = tab == selected;
                using (var fill = new SolidBrush(active ? BotUi.PaperLight : BotUi.FrameLight)) e.Graphics.FillRectangle(fill, area);
                using (var border = new Pen(BotUi.Gold)) e.Graphics.DrawRectangle(border, area.X, area.Y, area.Width - 1, area.Height - (active ? 0 : 1));
                TextRenderer.DrawText(e.Graphics, GuildTexts.Tab(tab), BotFonts.Get(8, active ? FontStyle.Bold : FontStyle.Regular), area,
                    active ? BotUi.Ink : BotUi.PaperLight, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            foreach (GuildTab tab in Kinds)
                if (TabBounds(tab).Contains(e.Location) && tab != selected) { TabClicked?.Invoke(this, tab); return; }
        }
    }

    /// <summary>Ligne cliquée dans <see cref="GuildMemberList"/>.</summary>
    public sealed class GuildMemberEventArgs : EventArgs
    {
        public GuildMember Member { get; }
        public Point Location { get; }
        internal GuildMemberEventArgs(GuildMember member, Point location) { Member = member; Location = location; }
    }

    /// <summary>
    /// Lignes des membres, comme <c>UI_GuildMemberItem</c> du client : alignement 12 px, illustration de classe 16 px, nom, rang
    /// (<c>ranks_fr</c>), niveau, % xp, xp donnée, épée en combat ; un membre hors ligne est grisé. Les images (<c>Alignments/mini</c>,
    /// <c>Artworks/Mini</c>, épée de <c>Party/infos</c>) sont lues en tâche de fond par <see cref="ClientAssets.GetAsync"/> ; le dessin ne
    /// lit que le cache.
    /// </summary>
    public sealed class GuildMemberList : Control
    {
        public const int RowHeight = 20, HeaderHeight = 18;
        private const int RankWidth = 84, LevelWidth = 38, PercentWidth = 40, XpWidth = 56, FightWidth = 22, TextLeft = 38;
        private readonly ToolTip tips = new ToolTip { InitialDelay = 300, ReshowDelay = 100 };
        private IReadOnlyList<GuildMember> items = new GuildMember[0];
        private long selfId;
        private long? selectedId;
        private int hover = -1;
        private string emptyText = "Aucun membre.";

        public GuildMemberList()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = BotUi.Paper; Cursor = Cursors.Hand;
            Height = HeaderHeight + RowHeight + 1;
            Load("Party", "infos");
        }

        public event EventHandler<GuildMemberEventArgs> RowClicked;

        public IReadOnlyList<GuildMember> Members => items;
        public long? SelectedId { get => selectedId; set { if (selectedId == value) return; selectedId = value; Invalidate(); } }
        public string EmptyText { get => emptyText; set { if (emptyText == value) return; emptyText = value ?? string.Empty; if (items.Count == 0) Invalidate(); } }

        /// <summary>Remplace les lignes (fil de l'interface) et lance la lecture des images manquantes.</summary>
        public void SetMembers(IReadOnlyList<GuildMember> members, long self)
        {
            members = members ?? new GuildMember[0];
            selfId = self;
            if (ReferenceEquals(items, members)) return;
            items = members;
            hover = -1;
            Height = HeaderHeight + Math.Max(1, items.Count) * RowHeight + 1;
            AccessibleDescription = string.Join(", ", items.Select(member => member.Name));
            foreach (GuildMember member in items)
            {
                if (member.Gfx > 0) Load("Artworks", "Mini/" + member.Gfx.ToString(CultureInfo.InvariantCulture));
                if (member.Alignment >= 0) Load("Alignments", "mini/" + member.Alignment.ToString(CultureInfo.InvariantCulture));
            }
            Invalidate();
        }

        public Rectangle RowBounds(int index) => new Rectangle(0, HeaderHeight + index * RowHeight, Width, RowHeight);

        /// <summary>Ligne sous le point, ou -1.</summary>
        public int IndexAt(Point point)
        {
            if (point.Y < HeaderHeight) return -1;
            int index = (point.Y - HeaderHeight) / RowHeight;
            return index >= 0 && index < items.Count ? index : -1;
        }

        /// <summary>Simule un clic sur la ligne du membre (tests, clavier) ; faux s'il n'est pas listé.</summary>
        public bool ClickRow(string name)
        {
            for (int index = 0; index < items.Count; index++)
                if (string.Equals(items[index].Name, name, StringComparison.Ordinal))
                {
                    RowClicked?.Invoke(this, new GuildMemberEventArgs(items[index], new Point(40, RowBounds(index).Y + 4)));
                    return true;
                }
            return false;
        }

        private void Load(string family, string name)
        {
            Task<Bitmap> load = ClientAssets.GetAsync(family, name);
            if (!load.IsCompleted) load.ContinueWith(_ => RedrawLater(), TaskScheduler.Default);
        }

        private static Bitmap Cached(string family, string name) => ClientAssets.TryCached(family, name, out Bitmap image) ? image : null;

        private void RedrawLater()
        {
            try { if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)Invalidate); }
            catch (Exception error) when (error is InvalidOperationException || error is ObjectDisposedException) { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            using (var paper = new SolidBrush(BackColor)) graphics.FillRectangle(paper, ClientRectangle);
            int fightX = Width - FightWidth - 2, xpX = fightX - XpWidth, percentX = xpX - PercentWidth, levelX = percentX - LevelWidth, rankX = levelX - RankWidth;
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            using (var rule = new Pen(BotUi.Gold))
            {
                Font header = BotFonts.Get(8, FontStyle.Bold);
                TextRenderer.DrawText(graphics, GuildTexts.Get("NAME", "nom"), header, new Rectangle(TextLeft, 0, Math.Max(10, rankX - TextLeft - 2), HeaderHeight), BotUi.Muted, flags);
                TextRenderer.DrawText(graphics, GuildTexts.Get("GUILD_RANK", "Rang"), header, new Rectangle(rankX + 3, 0, RankWidth - 4, HeaderHeight), BotUi.Muted, flags);
                TextRenderer.DrawText(graphics, GuildTexts.Get("LEVEL_SMALL", "Niv."), header, new Rectangle(levelX + 3, 0, LevelWidth - 4, HeaderHeight), BotUi.Muted, flags);
                TextRenderer.DrawText(graphics, GuildTexts.Get("PERCENT_XP", "% XP"), header, new Rectangle(percentX + 3, 0, PercentWidth - 4, HeaderHeight), BotUi.Muted, flags);
                TextRenderer.DrawText(graphics, GuildTexts.Get("WIN_XP", "XP gagnée"), header, new Rectangle(xpX + 3, 0, XpWidth - 4, HeaderHeight), BotUi.Muted, flags);
                graphics.DrawLine(rule, 0, HeaderHeight - 1, Width, HeaderHeight - 1);
                if (items.Count == 0)
                {
                    TextRenderer.DrawText(graphics, emptyText, BotFonts.Get(8.25f, FontStyle.Italic), new Rectangle(6, HeaderHeight, Width - 12, RowHeight), BotUi.Muted, flags);
                    return;
                }
                Bitmap sword = Cached("Party", "infos");
                for (int index = 0; index < items.Count; index++)
                {
                    GuildMember member = items[index];
                    Rectangle row = RowBounds(index);
                    if (index == hover || (selectedId.HasValue && member.Id == selectedId.Value))
                        using (var light = new SolidBrush(BotUi.PaperLight)) graphics.FillRectangle(light, row);
                    Color ink = member.IsOnline ? BotUi.Ink : BotUi.Muted;
                    Bitmap alignment = member.Alignment >= 0 ? Cached("Alignments", "mini/" + member.Alignment.ToString(CultureInfo.InvariantCulture)) : null;
                    if (alignment != null) lock (alignment) ClientAssets.DrawFit(graphics, alignment, new RectangleF(4, row.Y + 4, 12, 12));
                    Bitmap mini = member.Gfx > 0 ? Cached("Artworks", "Mini/" + member.Gfx.ToString(CultureInfo.InvariantCulture)) : null;
                    if (mini != null) lock (mini) ClientAssets.DrawFit(graphics, mini, new RectangleF(19, row.Y + 2, 16, 16));
                    TextRenderer.DrawText(graphics, member.Name, BotFonts.Get(8.25f, member.Id == selfId ? FontStyle.Bold | FontStyle.Underline : FontStyle.Bold),
                        new Rectangle(TextLeft, row.Y, Math.Max(10, rankX - TextLeft - 2), RowHeight), ink, flags);
                    TextRenderer.DrawText(graphics, member.RankName, BotFonts.Get(8.25f), new Rectangle(rankX + 3, row.Y, RankWidth - 4, RowHeight), ink, flags);
                    TextRenderer.DrawText(graphics, member.Level.ToString(CultureInfo.CurrentCulture), BotFonts.Get(8.25f), new Rectangle(levelX + 3, row.Y, LevelWidth - 4, RowHeight), ink, flags);
                    TextRenderer.DrawText(graphics, member.XpPercent.ToString(CultureInfo.CurrentCulture) + " %", BotFonts.Get(8.25f), new Rectangle(percentX + 3, row.Y, PercentWidth - 4, RowHeight), ink, flags);
                    TextRenderer.DrawText(graphics, member.XpGiven.ToString("N0", CultureInfo.CurrentCulture), BotFonts.Get(8.25f), new Rectangle(xpX + 3, row.Y, XpWidth - 4, RowHeight), ink, flags);
                    foreach (int x in new[] { rankX, levelX, percentX, xpX, fightX }) graphics.DrawLine(rule, x, row.Y, x, row.Bottom - 1);
                    if (member.IsInFight)
                    {
                        var place = new RectangleF(fightX + (FightWidth - 14) / 2f, row.Y + 3, 14, 14);
                        if (sword != null) lock (sword) ClientAssets.DrawFit(graphics, sword, place);
                        else using (var olive = new SolidBrush(BotUi.Olive)) graphics.FillEllipse(olive, place);
                    }
                    graphics.DrawLine(rule, 2, row.Bottom - 1, Width - 3, row.Bottom - 1);
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = IndexAt(e.Location);
            if (index == hover) return;
            hover = index; Invalidate();
            if (index < 0) { tips.SetToolTip(this, null); return; }
            GuildMember member = items[index];
            string text = member.Name + "\n" + GuildTexts.Get("GUILD_RANK", "Rang") + " : " + member.RankName + "\n" + GuildTexts.Get("LEVEL", "Niveau") + " : " + member.Level.ToString(CultureInfo.CurrentCulture);
            text += "\n" + (member.IsInFight ? "En combat" : member.IsOnline ? GuildTexts.Get("ONLINE", "Connectés") : GuildTexts.Get("GUILD_LAST_CONNECTION", "Dernière connexion il y a {0}", member.HoursSinceConnection.ToString(CultureInfo.CurrentCulture) + " h"));
            text += "\n" + GuildRights.Describe(member.Rights);
            tips.SetToolTip(this, text);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover < 0) return;
            hover = -1; Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right) return;
            int index = IndexAt(e.Location);
            if (index < 0) return;
            RowClicked?.Invoke(this, new GuildMemberEventArgs(items[index], e.Location));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tips.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>Profil demandé depuis la fiche d'un membre (<c>gP</c>).</summary>
    public sealed class GuildProfileEventArgs : EventArgs
    {
        public long MemberId { get; }
        public string Name { get; }
        public int Rank { get; }
        public int XpPercent { get; }
        public int Rights { get; }
        internal GuildProfileEventArgs(long memberId, string name, int rank, int xpPercent, int rights) { MemberId = memberId; Name = name; Rank = rank; XpPercent = xpPercent; Rights = rights; }
    }

    /// <summary>
    /// Fiche d'un membre, comme <c>GuildMemberInfos</c> du client : rang (liste de <c>ranks_fr</c>, active avec le droit de gérer les
    /// rangs), pourcentage d'xp donné (0 à 90, actif avec le droit de gérer les répartitions ou la sienne), cases des droits (actives
    /// avec le droit de gérer les droits, jamais pour le meneur), « Appliquer » (<c>gP</c>) et × (<c>gK</c> : « Exclure », ou « Quitter »
    /// pour soi-même).
    /// </summary>
    public sealed class GuildMemberSheet : Panel
    {
        private sealed class RankItem
        {
            public int Id;
            public string Name;
            public override string ToString() => Name;
        }

        private readonly Label title;
        private readonly ComboBox rank;
        private readonly NumericUpDown xp;
        private readonly FlowLayoutPanel rights;
        private readonly Button apply, kick;
        private readonly List<CheckBox> boxes = new List<CheckBox>();
        private GuildMember current;
        /// <summary>Membre et valeurs (rang, % XP, droits) écrits dans les champs au dernier affichage ; <c>null</c> après <see cref="Clear"/>.</summary>
        private long? shownId;
        private int shownRank, shownXp, shownRights;

        public GuildMemberSheet()
        {
            BackColor = BotUi.PaperLight; Height = 206; Padding = new Padding(6, 4, 6, 4); Margin = new Padding(0);
            title = new Label { Dock = DockStyle.Top, Height = 20, Font = BotFonts.Get(9, FontStyle.Bold), ForeColor = BotUi.Ink, BackColor = BotUi.PaperLight, AutoEllipsis = true, Name = "guild-sheet-title" };
            var line = new TableLayoutPanel { Dock = DockStyle.Top, Height = 30, ColumnCount = 4, RowCount = 1, Margin = new Padding(0), BackColor = BotUi.PaperLight };
            line.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44)); line.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            line.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48)); line.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
            rank = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Font = BotFonts.Get(8.25f), BackColor = BotUi.Paper, ForeColor = BotUi.Ink, Name = "guild-sheet-rank", AccessibleName = GuildTexts.Get("GUILD_RANK", "Rang") };
            xp = new NumericUpDown { Minimum = 0, Maximum = GuildActions.MaxXpPercent, Value = 0, Dock = DockStyle.Fill, Font = BotFonts.Get(8.25f), BackColor = BotUi.Paper, ForeColor = BotUi.Ink, Name = "guild-sheet-xp", AccessibleName = GuildTexts.Get("PERCENT_XP", "% XP") };
            line.Controls.Add(Caption(GuildTexts.Get("GUILD_RANK", "Rang")), 0, 0); line.Controls.Add(rank, 1, 0);
            line.Controls.Add(Caption(GuildTexts.Get("PERCENT_XP", "% XP")), 2, 0); line.Controls.Add(xp, 3, 0);
            rights = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = true, AutoScroll = true, BackColor = BotUi.PaperLight, Margin = new Padding(0), Name = "guild-sheet-rights" };
            foreach (GuildRight right in GuildRights.Editable)
            {
                var box = new CheckBox { Text = GuildRights.Label(right), Tag = right, Font = BotFonts.Get(7.5f), ForeColor = BotUi.Ink, BackColor = BotUi.PaperLight, AutoSize = true,
                    FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, 8, 0), Name = "guild-right-" + ((int)right).ToString(CultureInfo.InvariantCulture) };
                box.FlatAppearance.BorderColor = BotUi.Gold; box.FlatAppearance.CheckedBackColor = BotUi.Paper;
                boxes.Add(box); rights.Controls.Add(box);
            }
            apply = (Button)BotUi.Button("Appliquer", (s, e) => RaiseApply(), true, 100); apply.Name = "guild-sheet-apply";
            kick = (Button)BotUi.Button(GuildTexts.Get("REMOVE", "Retirer"), (s, e) => { if (current != null) KickRequested?.Invoke(this, current); }, false, 100); kick.Name = "guild-sheet-kick";
            var actions = BotUi.Actions(apply, kick); actions.Dock = DockStyle.Bottom;
            Controls.Add(rights); Controls.Add(line); Controls.Add(title); Controls.Add(actions);
        }

        public event EventHandler<GuildProfileEventArgs> ApplyRequested;
        public event EventHandler<GuildMember> KickRequested;
        public GuildMember Current => current;
        public ComboBox RankBox => rank;
        public NumericUpDown XpBox => xp;
        public Button ApplyButton => apply;
        public Button KickButton => kick;
        public IReadOnlyList<CheckBox> RightBoxes => boxes;

        private static Label Caption(string text) => new Label { Dock = DockStyle.Fill, Text = text, Font = BotFonts.Get(8.25f), ForeColor = BotUi.Muted, BackColor = BotUi.PaperLight, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };

        /// <summary>
        /// Affiche la fiche d'un membre avec les droits du personnage (<paramref name="guild"/>.OwnRights) pour activer les champs.
        /// Le rang, le % XP et les cases ne sont réécrits que pour un autre membre ou quand le serveur a changé ces valeurs : un
        /// rafraîchissement du volet ne défait pas une saisie en cours.
        /// </summary>
        public void Show(GuildMember member, Guild guild, long selfId, bool connected)
        {
            if (member == null || guild == null) { Clear(); return; }
            current = member;
            bool self = member.Id == selfId;
            title.Text = member.Name + " — " + member.RankName + (member.IsBoss ? " (" + GuildTexts.RankName(1) + ")" : string.Empty);
            if (shownId != member.Id || shownRank != member.Rank || shownXp != member.XpPercent || shownRights != member.Rights || rank.Items.Count == 0)
                ShowValues(member);
            bool canRanks = guild.CanDo(GuildRight.ManageRanks) && !member.IsBoss;
            bool canRights = guild.CanDo(GuildRight.ManageRights) && !member.IsBoss;
            bool canXp = guild.CanDo(GuildRight.ManageAllXp) || (self && guild.CanDo(GuildRight.ManageOwnXp));
            rank.Enabled = connected && canRanks;
            xp.Enabled = connected && canXp;
            foreach (CheckBox box in boxes) box.Enabled = connected && canRights;
            apply.Enabled = connected && (canRanks || canRights || canXp);
            kick.Text = self ? "Quitter" : "Exclure";
            kick.Enabled = connected && (self ? !(guild.IsBoss && guild.Members.Count > 1) : guild.CanDo(GuildRight.Ban) && !member.IsBoss);
            Visible = true;
        }

        /// <summary>Rang (liste de <c>ranks_fr</c>), % XP et cases des droits du membre.</summary>
        private void ShowValues(GuildMember member)
        {
            shownId = member.Id; shownRank = member.Rank; shownXp = member.XpPercent; shownRights = member.Rights;
            rank.Items.Clear();
            IReadOnlyList<KeyValuePair<int, string>> ranks = GuildTexts.Ranks();
            bool listed = false;
            foreach (KeyValuePair<int, string> pair in ranks)
            {
                rank.Items.Add(new RankItem { Id = pair.Key, Name = pair.Value });
                if (pair.Key == member.Rank) { rank.SelectedIndex = rank.Items.Count - 1; listed = true; }
            }
            if (!listed) { rank.Items.Add(new RankItem { Id = member.Rank, Name = member.RankName }); rank.SelectedIndex = rank.Items.Count - 1; }
            xp.Value = Math.Max(xp.Minimum, Math.Min(xp.Maximum, member.XpPercent));
            foreach (CheckBox box in boxes) box.Checked = member.IsBoss || (member.Rights & (int)(GuildRight)box.Tag) != 0;
        }

        public void Clear() { current = null; shownId = null; Visible = false; }

        private void RaiseApply()
        {
            if (current == null) return;
            var item = rank.SelectedItem as RankItem;
            int rights = 0;
            foreach (CheckBox box in boxes) if (box.Checked) rights |= (int)(GuildRight)box.Tag;
            if (current.IsBoss) rights = (int)GuildRight.Boss;
            ApplyRequested?.Invoke(this, new GuildProfileEventArgs(current.Id, current.Name, item?.Id ?? current.Rank, (int)xp.Value, rights));
        }
    }
}
