using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Controls;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Alignement;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Perso;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>Onglets de la fenêtre <c>Conquest</c> du client (<c>_btnTabStats</c>, <c>_btnTabZones</c>, <c>_btnTabJoin</c>).</summary>
    public enum ConquestTab { Stats = 0, Zones = 1, Join = 2 }

    /// <summary>
    /// Volet Conquête : la fenêtre <c>Conquest</c> du client 1.34 (742 × 445) rendue dans le tiroir. En-tête : icône et nom de
    /// l'alignement, grade (<c>RANK</c>) et balance (<c>BALANCE_WORD</c>, <c>Cb</c> demandé à l'ouverture comme <c>initData</c>).
    /// Onglet Statistiques (<c>ConquestStatsViewer</c>) : mode joueur contre joueur et son bouton (<c>ASK_ENABLED_PVP</c> → <c>GP+</c> ;
    /// <c>GP*</c> → <c>GIP</c> → <c>ASK_DISABLE_PVP</c> → <c>GP-</c>, jamais sans le « Oui » du joueur), jauges d'honneur et de déshonneur,
    /// bonus des zones alignées (<c>CB</c>), <c>RANK_SYSTEM_INFO</c>. Onglet Zones (<c>ConquestZonesViewer</c>) : <c>CWJ</c> à l'ouverture,
    /// <c>CWV</c> à la fermeture, filtre du client, sous-zones et villages, compteurs. Onglet Défendre (<c>ConquestJoinViewer</c>) :
    /// <c>CIJ</c> / <c>CIV</c>, délai, défenseurs (<c>CP</c>) et attaquants (<c>Cp</c>), « Rejoindre » → <c>CFJ</c> ; <c>CFS</c> / <c>CFV</c> ne
    /// sont pas proposés (StarLoco les ignore). Les prismes de <c>CW</c> sont transmis à la carte du monde (catégorie 5, gfx 420 / 421).
    /// </summary>
    public sealed class ConquestPanel : GamePanel
    {
        private const string Reference = "ALIGNEMENT";
        /// <summary>Catégorie d'indices « Territoires de conquête » (<c>HIC</c> 5) et icônes des prismes du client (<c>MapExplorer.getConquestAreaList</c>).</summary>
        public const int PrismHintCategory = 5, BontaPrismGfx = 420, BrakmarPrismGfx = 421;
        /// <summary>Valeurs du filtre de l'onglet Zones (<c>ConquestZonesViewer.FILTER_*</c>) ; un camp est sa propre valeur.</summary>
        public const int FilterHostile = -1, FilterCapturable = -2, FilterVulnerable = -3, FilterAll = -4;
        private const int SideImageSize = 48;

        private ConquestTab tab = ConquestTab.Stats;
        private ConquestTabStrip tabs;
        private PictureBox sideBox;
        private Label nameLabel, gradeLabel, balanceLabel, status;
        private Panel[] pages;
        private Label pvpState, honorLabel, dishonorLabel, rankInfo;
        private ConquestGauge honorGauge, dishonorGauge;
        private Control wingsButton;
        private readonly Label[,] bonusCells = new Label[3, 2];
        private ComboBox filter;
        private Label zoneCounts, villageCounts;
        private ListView zones, villages;
        private Label joinState, joinTimer;
        private ListView defenders, attackers;
        private Control joinButton;
        private Timer clock;
        private AlignmentActions bound;
        private CharacterClass character;
        private PanelHost watched;
        private bool open;
        private ConquestTab? joinedTab;
        private string sideShown;
        private Form wingsDialog;
        private int wingsQuestion;
        private IReadOnlyList<ConquestZone> shownZones;
        private IReadOnlyList<ConquestVillage> shownVillages;
        private int shownFilter = int.MinValue, shownSide = -1;

        public override string Title => AlignmentTexts.Get("CONQUEST_WORD", "Conquête");
        public override Image Icon => ClientAssets.Icon("icone-pvp", 24);
        public ConquestTab CurrentTab => tab;
        public ConquestTabStrip Tabs => tabs;
        /// <summary>Boîte <c>ASK_DISABLE_PVP</c> (après <c>GIP</c>) encore affichée, sinon <c>null</c>.</summary>
        public Form WingsDialog => wingsDialog;
        public ListView ZoneList => zones;
        public ListView VillageList => villages;
        public ListView DefenderList => defenders;
        public ListView AttackerList => attackers;
        public ComboBox Filter => filter;

        protected override Control CreateView()
        {
            var page = Page();
            var header = new Panel { Dock = DockStyle.Top, Height = SideImageSize + 8, BackColor = BotUi.Paper, Margin = new Padding(0), Padding = new Padding(0, 2, 0, 2) };
            sideBox = new PictureBox { Dock = DockStyle.Left, Width = SideImageSize + 8, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent, Name = "conquest-side", Padding = new Padding(2) };
            var texts = new Panel { Dock = DockStyle.Fill, BackColor = BotUi.Paper, Padding = new Padding(4, 0, 0, 0), Margin = new Padding(0) };
            nameLabel = MakeLabel(string.Empty, 11, true); nameLabel.Dock = DockStyle.Top; nameLabel.Height = 22; nameLabel.AutoEllipsis = true; nameLabel.Name = "conquest-name";
            gradeLabel = MakeLabel(string.Empty, 8.25f); gradeLabel.Dock = DockStyle.Top; gradeLabel.Height = 16; gradeLabel.ForeColor = BotUi.Muted; gradeLabel.AutoEllipsis = true; gradeLabel.Name = "conquest-grade";
            balanceLabel = MakeLabel(string.Empty, 8.25f); balanceLabel.Dock = DockStyle.Top; balanceLabel.Height = 16; balanceLabel.ForeColor = BotUi.Muted; balanceLabel.AutoEllipsis = true; balanceLabel.Name = "conquest-balance";
            texts.Controls.Add(balanceLabel); texts.Controls.Add(gradeLabel); texts.Controls.Add(nameLabel);
            header.Controls.Add(texts); header.Controls.Add(sideBox);

            tabs = new ConquestTabStrip { Dock = DockStyle.Top, Name = "conquest-tabs" };
            tabs.TabClicked += (s, e) => SelectTab(e);

            pages = new Panel[3];
            pages[(int)ConquestTab.Stats] = BuildStats();
            pages[(int)ConquestTab.Zones] = BuildZones();
            pages[(int)ConquestTab.Join] = BuildJoin();
            var content = new Panel { Dock = DockStyle.Fill, BackColor = BotUi.Paper, Margin = new Padding(0) };
            foreach (Panel item in pages) { item.Visible = false; content.Controls.Add(item); }

            status = MakeStatus(string.Empty); status.Name = "conquest-status";
            page.Controls.Add(content); page.Controls.Add(tabs); page.Controls.Add(header); page.Controls.Add(status);
            clock = new Timer { Interval = 1000 };
            clock.Tick += (s, e) => RefreshTimer();
            return page;
        }

        private Panel BuildStats()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = BotUi.Paper, Margin = new Padding(0), AutoScroll = true };
            var pvpRow = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = BotUi.Paper, Margin = new Padding(0) };
            pvpState = MakeLabel(string.Empty, 9, true); pvpState.Dock = DockStyle.Fill; pvpState.TextAlign = ContentAlignment.MiddleLeft; pvpState.Name = "conquest-pvp";
            wingsButton = MakeButton(AlignmentTexts.Get("ENABLE_PVP_SHORT", "Activer"), async (s, e) => await ToggleWings(), true, 120); wingsButton.Dock = DockStyle.Right; wingsButton.Name = "conquest-wings";
            pvpRow.Controls.Add(pvpState); pvpRow.Controls.Add(wingsButton);

            honorLabel = MakeLabel(string.Empty, 8.25f); honorLabel.Dock = DockStyle.Top; honorLabel.Height = 18; honorLabel.Name = "conquest-honor";
            honorGauge = new ConquestGauge { Dock = DockStyle.Top, Name = "conquest-honor-gauge", Fill = BotUi.Olive };
            dishonorLabel = MakeLabel(string.Empty, 8.25f); dishonorLabel.Dock = DockStyle.Top; dishonorLabel.Height = 18; dishonorLabel.Name = "conquest-dishonor";
            dishonorGauge = new ConquestGauge { Dock = DockStyle.Top, Name = "conquest-dishonor-gauge", Fill = Color.FromArgb(150, 60, 40) };

            var bonusTitle = MakeLabel(AlignmentTexts.Get("ALIGNED_AREA_MODIFICATORS", "Modificateurs des zones alignées"), 9, true); bonusTitle.Dock = DockStyle.Top; bonusTitle.Height = 24; bonusTitle.TextAlign = ContentAlignment.BottomLeft;
            var table = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 3, RowCount = 4, AutoSize = true, Margin = new Padding(0), Padding = new Padding(0, 2, 0, 2), BackColor = BotUi.Paper, CellBorderStyle = TableLayoutPanelCellBorderStyle.Single };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            string[] headers = { AlignmentTexts.Get("TYPE", "Type"), AlignmentTexts.Get("BONUS", "Bonus"), AlignmentTexts.Get("MALUS", "Malus") };
            for (int column = 0; column < 3; column++) table.Controls.Add(Cell(headers[column], true), column, 0);
            string[] rows = { AlignmentTexts.Get("EXPERIMENT", "Expérience"), AlignmentTexts.Get("LOOT", "Butin"), AlignmentTexts.Get("COLLECT", "Récolte") };
            for (int row = 0; row < 3; row++)
            {
                table.Controls.Add(Cell(rows[row], false), 0, row + 1);
                bonusCells[row, 0] = Cell("-", false); bonusCells[row, 0].Name = "conquest-bonus-" + row; table.Controls.Add(bonusCells[row, 0], 1, row + 1);
                bonusCells[row, 1] = Cell("-", false); bonusCells[row, 1].Name = "conquest-malus-" + row; table.Controls.Add(bonusCells[row, 1], 2, row + 1);
            }
            rankInfo = MakeLabel(AlignmentTexts.Get("RANK_SYSTEM_INFO", "Les grades dépendent des points d'honneur gagnés en combat contre les joueurs de l'alignement adverse."), 8.25f);
            rankInfo.Dock = DockStyle.Top; rankInfo.AutoSize = false; rankInfo.Height = 96; rankInfo.ForeColor = BotUi.Muted; rankInfo.Name = "conquest-rank-info"; rankInfo.Padding = new Padding(0, 6, 0, 0);
            // Ordre d'ancrage : le dernier ajouté prend le bord en premier.
            panel.Controls.Add(rankInfo); panel.Controls.Add(table); panel.Controls.Add(bonusTitle);
            panel.Controls.Add(dishonorGauge); panel.Controls.Add(dishonorLabel); panel.Controls.Add(honorGauge); panel.Controls.Add(honorLabel); panel.Controls.Add(pvpRow);
            return panel;
        }

        private static Label Cell(string text, bool bold)
        {
            Label label = MakeLabel(text, 8.25f, bold);
            label.Dock = DockStyle.Fill; label.AutoSize = false; label.Height = 18; label.TextAlign = ContentAlignment.MiddleLeft; label.Margin = new Padding(2, 0, 2, 0); label.AutoEllipsis = true;
            return label;
        }

        private Panel BuildZones()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = BotUi.Paper, Margin = new Padding(0) };
            filter = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, Name = "conquest-filter", FlatStyle = FlatStyle.Flat, BackColor = BotUi.PaperLight, ForeColor = BotUi.Ink };
            WithFont(filter, BotFonts.Get(8.25f));
            filter.SelectedIndexChanged += (s, e) => RefreshZones();
            zoneCounts = MakeLabel(string.Empty, 8.25f); zoneCounts.Dock = DockStyle.Top; zoneCounts.Height = 18; zoneCounts.Name = "conquest-zone-counts"; zoneCounts.ForeColor = BotUi.Muted;
            villageCounts = MakeLabel(string.Empty, 8.25f); villageCounts.Dock = DockStyle.Top; villageCounts.Height = 18; villageCounts.Name = "conquest-village-counts"; villageCounts.ForeColor = BotUi.Muted;
            zones = MakeList(8.25f, AlignmentTexts.Get("CONQUEST_AREA_WORD", "Sous-zone"), AlignmentTexts.Get("ALIGNMENT", "Alignement"), AlignmentTexts.Get("CONQUEST_STATE_WORD", "État"));
            zones.Columns[0].Width = 170; zones.Columns[1].Width = 90; zones.Columns[2].Width = 90; zones.Name = "conquest-zones";
            var villagesTitle = MakeLabel(AlignmentTexts.Get("CONQUEST_VILLAGE_WORD", "Villages"), 9, true); villagesTitle.Dock = DockStyle.Top; villagesTitle.Height = 20;
            villages = MakeList(8.25f, AlignmentTexts.Get("CONQUEST_VILLAGE_WORD", "Village"), AlignmentTexts.Get("ALIGNMENT", "Alignement"), AlignmentTexts.Get("CONQUEST_PRISM_WORD", "Prisme"));
            villages.Columns[0].Width = 170; villages.Columns[1].Width = 90; villages.Columns[2].Width = 90; villages.Dock = DockStyle.Bottom; villages.Height = 96; villages.Name = "conquest-villages";
            panel.Controls.Add(zones); panel.Controls.Add(villagesTitle); panel.Controls.Add(villages);
            panel.Controls.Add(villageCounts); panel.Controls.Add(zoneCounts); panel.Controls.Add(filter);
            return panel;
        }

        private Panel BuildJoin()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = BotUi.Paper, Margin = new Padding(0) };
            joinState = MakeLabel(string.Empty, 9, true); joinState.Dock = DockStyle.Top; joinState.Height = 22; joinState.AutoEllipsis = true; joinState.Name = "conquest-join-state";
            joinTimer = MakeLabel(string.Empty, 8.25f); joinTimer.Dock = DockStyle.Top; joinTimer.Height = 18; joinTimer.ForeColor = BotUi.Muted; joinTimer.Name = "conquest-join-timer";
            var defendersTitle = MakeLabel(AlignmentTexts.Get("CONQUEST_JOIN_FIGHTERS", "Défenseurs du Prisme"), 9, true); defendersTitle.Dock = DockStyle.Top; defendersTitle.Height = 20;
            defenders = MakeList(8.25f, AlignmentTexts.Get("DEFENDERS", "Défenseurs"), AlignmentTexts.Get("LEVEL_SMALL", "Niv."), AlignmentTexts.Get("CONQUEST_JOIN_RESERVISTS", "Réservistes"));
            defenders.Columns[0].Width = 170; defenders.Columns[1].Width = 50; defenders.Columns[2].Width = 110; defenders.Name = "conquest-defenders";
            var attackersTitle = MakeLabel(AlignmentTexts.Get("ATTACKERS", "Attaquants"), 9, true); attackersTitle.Dock = DockStyle.Top; attackersTitle.Height = 20;
            attackers = MakeList(8.25f, AlignmentTexts.Get("ATTACKERS", "Attaquants"), AlignmentTexts.Get("LEVEL_SMALL", "Niv."));
            attackers.Columns[0].Width = 220; attackers.Columns[1].Width = 60; attackers.Dock = DockStyle.Bottom; attackers.Height = 96; attackers.Name = "conquest-attackers";
            joinButton = MakeButton(AlignmentTexts.Get("JOIN_SMALL", "Rejoindre"), async (s, e) => await JoinFight(), true, 140); joinButton.Name = "conquest-join";
            var note = MakeStatus("Changer de place (CFS) ou quitter la défense (CFV) n'est pas proposé : StarLoco ignore ces paquets."); note.Name = "conquest-join-note";
            var bar = BotUi.Actions(joinButton); bar.Dock = DockStyle.Bottom;
            panel.Controls.Add(defenders); panel.Controls.Add(defendersTitle); panel.Controls.Add(attackersTitle); panel.Controls.Add(attackers); panel.Controls.Add(bar); panel.Controls.Add(note);
            panel.Controls.Add(joinTimer); panel.Controls.Add(joinState);
            // Dock : titres et listes du haut, puis attaquants, boutons et note en bas.
            panel.Controls.SetChildIndex(attackersTitle, 0);
            return panel;
        }

        // ----- Session -----

        protected override void OnBind(GameClass game)
        {
            bound = game.Interactions?.Alignment;
            if (bound != null)
            {
                bound.Changed += OnAlignmentChanged;
                bound.WingsDisableAsked += OnWingsDisableAsked;
                bound.DefenseClosed += OnDefenseClosed;
                bound.WorldDataChanged += OnWorldDataChanged;
            }
            character = game.character;
            if (character != null) character.RefreshCaracteristiques += OnAlignmentChanged;
            watched = Host;
            if (watched != null) { watched.PanelShown += OnPanelShown; watched.PanelClosed += OnPanelClosed; }
        }

        protected override void OnUnbind(GameClass game)
        {
            if (bound != null)
            {
                bound.Changed -= OnAlignmentChanged;
                bound.WingsDisableAsked -= OnWingsDisableAsked;
                bound.DefenseClosed -= OnDefenseClosed;
                bound.WorldDataChanged -= OnWorldDataChanged;
            }
            if (character != null) character.RefreshCaracteristiques -= OnAlignmentChanged;
            if (watched != null) { watched.PanelShown -= OnPanelShown; watched.PanelClosed -= OnPanelClosed; }
            bound = null; character = null; watched = null; open = false; joinedTab = null;
            clock?.Stop();
            CloseWingsDialog();
        }

        private void OnPanelShown(object sender, PanelEventArgs e)
        {
            if (!ReferenceEquals(e?.Panel, this) || bound == null || open) return;
            open = true;
            // initData du client : la balance, puis l'onglet courant.
            _ = Run(a => a.RequestBalanceAsync());
            EnterTab(tab);
            RefreshView();
        }

        private void OnPanelClosed(object sender, PanelEventArgs e)
        {
            if (!ReferenceEquals(e?.Panel, this) || !open) return;
            open = false;
            LeaveTab();
            clock?.Stop();
        }

        /// <summary>Change d'onglet : quitte l'abonnement de l'ancien (<c>CWV</c>, <c>CIV</c>) et demande le nouveau (<c>CB</c>, <c>CWJ</c>, <c>CIJ</c>).</summary>
        public void SelectTab(ConquestTab value)
        {
            if (tab == value && tabs != null && tabs.Selected == value) return;
            if (open) LeaveTab();
            tab = value;
            if (open) EnterTab(value);
            RefreshView();
        }

        private void EnterTab(ConquestTab value)
        {
            joinedTab = value;
            switch (value)
            {
                case ConquestTab.Stats: _ = Run(a => a.RequestBonusAsync()); break;
                case ConquestTab.Zones: _ = Run(a => a.JoinWorldInfosAsync()); break;
                case ConquestTab.Join: _ = Run(a => a.JoinPrismInfosAsync()); clock?.Start(); break;
            }
        }

        private void LeaveTab()
        {
            ConquestTab? previous = joinedTab;
            joinedTab = null;
            if (previous == ConquestTab.Zones && bound != null && bound.WorldInfosJoined) _ = Run(a => a.LeaveWorldInfosAsync());
            else if (previous == ConquestTab.Join)
            {
                clock?.Stop();
                if (bound != null && bound.PrismInfosJoined) _ = Run(a => a.LeavePrismInfosAsync());
            }
        }

        // ----- Événements du modèle (fil réseau) -----

        private void OnAlignmentChanged() => Post(RefreshView);

        private void OnWorldDataChanged() => Post(() =>
        {
            RefreshView();
            Host?.Get<WorldMapPanel>()?.SetPrisms(PrismHints(bound?.World));
        });

        /// <summary><c>CIV</c> : le client décharge la fenêtre de conquête.</summary>
        private void OnDefenseClosed() => Post(() => { if (open) RaiseClosed(); });

        /// <summary><c>GIP&lt;honneur&gt;</c> : boîte <c>ASK_DISABLE_PVP</c> ; « Oui » envoie <c>GP-</c>, « Non » n'envoie rien.</summary>
        private void OnWingsDisableAsked(int loss) => Post(() => AskDisableWings(loss));

        private async void AskDisableWings(int loss)
        {
            AlignmentActions alignment = bound;
            if (alignment == null || IsDisposed) return;
            int question = ++wingsQuestion;
            try
            {
                CloseWingsDialog();
                Form[] before = BotDialogs.OpenDialogs.ToArray();
                Task<BotDialogResult> answer = BotDialogs.AskYesNoAsync(Host, AlignmentTexts.Get("PVP_MODE", "Mode joueur contre joueur"),
                    AlignmentTexts.Get("ASK_DISABLE_PVP", "Si tu désactives le mode joueur contre joueur, tu perdras {0} points d'honneur. Continuer ?", loss.ToString(CultureInfo.InvariantCulture)));
                wingsDialog = BotDialogs.OpenDialogs.Except(before).LastOrDefault();
                BotDialogResult result = await answer;
                if (question == wingsQuestion) wingsDialog = null;
                if (result == BotDialogResult.Yes) await ReportAsync(alignment.DisableWingsAsync);
                else alignment.CancelWingsDisable();
            }
            catch (Exception error) { Account?.Logger?.LogException(Reference, error); }
        }

        private void CloseWingsDialog()
        {
            Form dialog = wingsDialog; wingsDialog = null;
            if (dialog != null && !dialog.IsDisposed) { try { dialog.Close(); } catch (Exception) { /* déjà fermée */ } }
        }

        /// <summary>Bouton Activer / Désactiver (<c>ConquestStatsViewer</c>) : <c>ASK_ENABLED_PVP</c> → <c>GP+</c>, ou <c>GP*</c> (le serveur répond <c>GIP</c>).</summary>
        private async Task ToggleWings()
        {
            AlignmentActions alignment = bound;
            if (alignment == null) return;
            if (!alignment.Alignment.IsAligned) { Feedback("Un personnage neutre n'a pas d'ailes."); return; }
            if (alignment.Alignment.WingsEnabled) { await ReportAsync(alignment.AskDisableWingsAsync); return; }
            BotDialogResult answer = await BotDialogs.AskYesNoAsync(Host, AlignmentTexts.Get("PVP_MODE", "Mode joueur contre joueur"),
                AlignmentTexts.Get("ASK_ENABLED_PVP", "Veux-tu vraiment activer le mode joueur contre joueur ?"));
            if (answer != BotDialogResult.Yes) { Feedback("Mode joueur contre joueur inchangé."); return; }
            await ReportAsync(alignment.EnableWingsAsync);
        }

        private Task JoinFight() => Run(a => a.JoinPrismFightAsync());

        private Task Run(Func<AlignmentActions, Task<InteractionResult>> action)
        {
            AlignmentActions alignment = bound ?? Game?.Interactions?.Alignment;
            return alignment == null ? Task.CompletedTask : ReportAsync(() => action(alignment));
        }

        private bool Post(Action action) => ExchangePanel.PostToForm(Host, () => { if (!IsDisposed) action(); });

        // ----- Affichage -----

        public override void RefreshView()
        {
            AlignmentActions alignment = Game?.Interactions?.Alignment;
            if (alignment == null || nameLabel == null) return;
            Alignment me = alignment.Alignment;
            if (tabs.Selected != tab) tabs.Selected = tab;
            for (int index = 0; index < pages.Length; index++) pages[index].Visible = index == (int)tab;

            nameLabel.Text = me.Name + (me.SpecializationId > 0 && me.SpecializationName.Length > 0 ? " · " + me.SpecializationName : string.Empty);
            gradeLabel.Text = me.IsAligned
                ? AlignmentTexts.Get("RANK", "Grade") + " " + me.Grade.ToString(CultureInfo.InvariantCulture) + " : " + me.GradeName
                : AlignmentTexts.Get("YOU_HAVE_NO_SPECIALIZATION", "Aucune spécialisation.");
            balanceLabel.Text = AlignmentTexts.Get("BALANCE_WORD", "Balance") + " : " + (alignment.WorldBalance.HasValue
                ? Number(alignment.WorldBalance.Value) + " (monde) · " + Number(alignment.AreaBalance ?? 0) + " (zone)" : "en attente du serveur");
            RefreshSideImage(me.Side);

            RefreshStats(alignment, me);
            RefreshZones();
            RefreshJoin(alignment, me);
            string message = alignment.LastMessage;
            status.Text = Connected ? message : "Connectez le personnage pour consulter la conquête." + (message.Length > 0 ? " " + message : string.Empty);
        }

        private void RefreshStats(AlignmentActions alignment, Alignment me)
        {
            bool wings = me.WingsEnabled;
            pvpState.Text = AlignmentTexts.Get("PVP_MODE", "Mode joueur contre joueur") + " : " + (wings ? AlignmentTexts.Get("ACTIVE", "Actif") : AlignmentTexts.Get("INACTIVE", "Inactif"))
                + (alignment.PendingWingsLoss.HasValue ? " · " + alignment.PendingWingsLoss.Value + " honneur à perdre" : string.Empty);
            wingsButton.Text = wings ? AlignmentTexts.Get("DISABLE_PVP_SHORT", "Désactiver") : AlignmentTexts.Get("ENABLE_PVP_SHORT", "Activer");
            wingsButton.Enabled = Connected && me.IsAligned;
            int min = me.HonorMin, max = me.HonorMax;
            honorLabel.Text = AlignmentTexts.Get("HONOUR_POINTS", "Points d'honneur") + " : " + me.Honor.ToString(CultureInfo.InvariantCulture) + " (" + min.ToString(CultureInfo.InvariantCulture) + " – " + max.ToString(CultureInfo.InvariantCulture) + ")";
            honorGauge.Ratio = max > min ? (double)(me.Honor - min) / (max - min) : me.Honor >= max ? 1 : 0;
            int maxDishonor = me.MaxDishonor;
            dishonorLabel.Text = AlignmentTexts.Get("DISGRACE_POINTS", "Points de déshonneur") + " : " + me.Dishonor.ToString(CultureInfo.InvariantCulture) + " / " + maxDishonor.ToString(CultureInfo.InvariantCulture);
            dishonorGauge.Ratio = maxDishonor > 0 ? (double)me.Dishonor / maxDishonor : 0;
            ConquestBonus bonus = alignment.Bonus;
            ConquestModifier[] bonusRows = bonus == null ? null : new[] { bonus.AlignBonus, bonus.AlignBonus, bonus.AlignBonus };
            for (int row = 0; row < 3; row++)
            {
                if (bonus == null) { bonusCells[row, 0].Text = "-"; bonusCells[row, 1].Text = "-"; continue; }
                double value = Pick(bonus.AlignBonus, row), multiplier = Pick(bonus.RankMultiplier, row), malus = Pick(bonus.AlignMalus, row);
                // ConquestStatsViewer : « +(b×m)% (b% x m) » et « m% ».
                bonusCells[row, 0].Text = "+" + Number(value * multiplier) + "% (" + Number(value) + "% x " + Number(multiplier) + ")";
                bonusCells[row, 1].Text = Number(malus) + "%";
            }
        }

        private static double Pick(ConquestModifier modifier, int row) => row == 0 ? modifier.Xp : row == 1 ? modifier.Drop : modifier.Collect;

        private void RefreshZones()
        {
            AlignmentActions alignment = Game?.Interactions?.Alignment;
            if (alignment == null || zones == null) return;
            int mySide = alignment.Alignment.Side;
            if (filter.Items.Count == 0 || shownSide != mySide) FillFilter(mySide);
            ConquestWorld world = alignment.World;
            int selected = filter.SelectedItem is FilterItem item ? item.Value : FilterAll;
            if (world.CountsUnknown)
            {
                zoneCounts.Text = AlignmentTexts.Get("CONQUEST_POSSESSED_WORD", "Possédées") + " : " + world.Zones.Count(zone => zone.Side == mySide && mySide > 0) + " / " + world.TotalAreas + " (compteurs absents pour un personnage neutre)";
                villageCounts.Text = AlignmentTexts.Get("CONQUEST_POSSESSED_WORD", "Possédés") + " : " + world.Villages.Count(village => village.Side == mySide && mySide > 0) + " / " + world.Villages.Count;
            }
            else
            {
                zoneCounts.Text = AlignmentTexts.Get("CONQUEST_POSSESSED_WORD", "Possédées") + " : " + world.OwnedAreas + " / " + world.PossibleAreas + " / " + world.TotalAreas;
                villageCounts.Text = AlignmentTexts.Get("CONQUEST_POSSESSED_WORD", "Possédés") + " : " + world.OwnedVillages + " / " + world.TotalVillages;
            }
            if (ReferenceEquals(shownZones, world.Zones) && ReferenceEquals(shownVillages, world.Villages) && shownFilter == selected) return;
            shownZones = world.Zones; shownVillages = world.Villages; shownFilter = selected;
            zones.BeginUpdate(); zones.Items.Clear();
            foreach (ConquestZone zone in world.Zones.Where(zone => Matches(zone, world, selected, mySide)))
            {
                var row = zones.Items.Add(zone.Name);
                row.SubItems.Add(zone.Side > 0 ? zone.SideName : "-");
                row.SubItems.Add(zone.Fighting ? "En combat" : zone.HasPrism ? AlignmentTexts.Get("CONQUEST_PRISM_WORD", "Prisme") : string.Empty);
                row.Tag = zone.Id;
                row.ForeColor = zone.Side == 0 ? BotUi.Muted : zone.Side == mySide ? BotUi.Olive : BotUi.Ink;
            }
            zones.EndUpdate();
            villages.BeginUpdate(); villages.Items.Clear();
            foreach (ConquestVillage village in world.Villages)
            {
                var row = villages.Items.Add(village.Name);
                row.SubItems.Add(village.Side > 0 ? village.SideName : "-");
                row.SubItems.Add(village.HasPrism ? "Oui" : "Non");
                row.Tag = village.Id;
                row.ForeColor = village.Side == 0 ? BotUi.Muted : village.Side == mySide ? BotUi.Olive : BotUi.Ink;
            }
            villages.EndUpdate();
        }

        /// <summary>Filtre de <c>ConquestZonesViewer</c> : un camp, zones en combat (« hostiles »), capturables, vulnérables, toutes.</summary>
        public static bool Matches(ConquestZone zone, ConquestWorld world, int filterValue, int mySide)
        {
            if (zone == null) return false;
            switch (filterValue)
            {
                case FilterHostile: return zone.Fighting;
                case FilterCapturable: return zone.IsCapturable(world, mySide);
                case FilterVulnerable: return zone.IsVulnerable(world, mySide);
                case FilterAll: return true;
                default: return filterValue >= 0 && zone.Side == filterValue;
            }
        }

        private void FillFilter(int mySide)
        {
            shownSide = mySide;
            int previous = filter.SelectedItem is FilterItem current ? current.Value : int.MinValue;
            filter.BeginUpdate(); filter.Items.Clear();
            foreach (int side in AlignmentTexts.Sides().Where(AlignmentTexts.IsConqueror))
                filter.Items.Add(new FilterItem(AlignmentTexts.Get("CONQUEST_ALIGNED_AREAS", "Zones de l'alignement {0}", AlignmentTexts.Name(side)), side));
            filter.Items.Add(new FilterItem(AlignmentTexts.Get("CONQUEST_HOSTILE_AREAS", "Zones hostiles"), FilterHostile));
            filter.Items.Add(new FilterItem(AlignmentTexts.Get("CONQUEST_CAPTURABLE_AREAS", "Zones capturables"), FilterCapturable));
            filter.Items.Add(new FilterItem(AlignmentTexts.Get("CONQUEST_VULNERALE_AREAS", "Zones vulnérables"), FilterVulnerable));
            filter.Items.Add(new FilterItem(AlignmentTexts.Get("CONQUEST_ALL_AREAS", "Toutes les zones"), FilterAll));
            filter.EndUpdate();
            int wanted = previous != int.MinValue ? previous : mySide > 0 && AlignmentTexts.IsConqueror(mySide) ? mySide : FilterAll;
            for (int index = 0; index < filter.Items.Count; index++)
                if (((FilterItem)filter.Items[index]).Value == wanted) { filter.SelectedIndex = index; return; }
            filter.SelectedIndex = filter.Items.Count - 1;
        }

        private void RefreshJoin(AlignmentActions alignment, Alignment me)
        {
            PrismDefense defense = alignment.Defense;
            if (!defense.Error.HasValue) joinState.Text = alignment.PrismInfosJoined ? "En attente du serveur (CIJ)…" : "Ouvrez l'onglet pour interroger le prisme de la sous-zone.";
            else if (defense.Error == 0) joinState.Text = AlignmentTexts.Get("CONQUEST_JOIN_FIGHT", "Rejoindre un combat de prisme") + " : " + defense.MaxTeamPositions.ToString(CultureInfo.InvariantCulture) + " place(s)";
            else joinState.Text = AlignmentActions.JoinErrorText(defense.Error.Value);
            RefreshTimer();
            joinButton.Enabled = Connected && me.IsAligned && defense.IsJoinable;
            IReadOnlyList<PrismFighter> defenderList = defense.Defenders, attackerList = defense.Attackers;
            defenders.BeginUpdate(); defenders.Items.Clear();
            foreach (PrismFighter fighter in defenderList.OrderBy(entry => entry.Reservist).ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var row = defenders.Items.Add(fighter.Name);
                row.SubItems.Add(fighter.Level.ToString(CultureInfo.InvariantCulture));
                row.SubItems.Add(fighter.Reservist ? AlignmentTexts.Get("CONQUEST_JOIN_RESERVISTS", "Réserviste") : string.Empty);
                row.Tag = fighter.Id;
                if (fighter.Reservist) row.ForeColor = BotUi.Muted;
            }
            defenders.EndUpdate();
            attackers.BeginUpdate(); attackers.Items.Clear();
            foreach (PrismFighter fighter in attackerList.OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var row = attackers.Items.Add(fighter.Name);
                row.SubItems.Add(fighter.Level.ToString(CultureInfo.InvariantCulture));
                row.Tag = fighter.Id;
            }
            attackers.EndUpdate();
        }

        private void RefreshTimer()
        {
            PrismDefense defense = bound?.Defense ?? Game?.Interactions?.Alignment?.Defense;
            if (joinTimer == null || defense == null) return;
            if (defense.Error != 0) { joinTimer.Text = string.Empty; return; }
            int remaining = defense.RemainingMilliseconds / 1000;
            joinTimer.Text = "Début du combat dans " + (remaining / 60).ToString(CultureInfo.InvariantCulture) + " min " + (remaining % 60).ToString("00", CultureInfo.InvariantCulture) + " s";
        }

        private void RefreshSideImage(int side)
        {
            string name = side.ToString(CultureInfo.InvariantCulture);
            if (name == sideShown) return;
            if (ClientAssets.TryCached("Alignments", name, out Bitmap cached)) { sideShown = name; sideBox.Image = cached; return; }
            // Image lue hors du fil de l'interface ; le volet se rafraîchit à l'arrivée.
            string wanted = name;
            ClientAssets.GetAsync("Alignments", name).ContinueWith(task =>
            {
                if (task.Status != TaskStatus.RanToCompletion || task.Result == null) return;
                OnUi(() => { if (sideShown != wanted && sideBox != null && !sideBox.IsDisposed) { sideShown = wanted; sideBox.Image = task.Result; } });
            });
        }

        /// <summary>Prismes des sous-zones de <c>CW</c> pour la carte du monde (<c>MapExplorer.getConquestAreaList</c>) : gfx 420 Bonta, 421 Brâkmar.</summary>
        public static IReadOnlyList<WorldMapHint> PrismHints(ConquestWorld world)
        {
            var result = new List<WorldMapHint>();
            if (world == null) return result;
            foreach (ConquestZone zone in world.Zones)
            {
                if (!zone.HasPrism || zone.Side <= 0) continue;
                Point? coords = null;
                try { if (LangData.IsLoaded("maps")) coords = LangData.Map.Coords(zone.PrismMapId); } catch (Exception) { /* textes absents */ }
                if (coords == null) continue;
                WorldMapAreas areas = WorldMapAreas.Current;
                int superArea = areas.SuperAreaOf(zone.PrismMapId) ?? areas.SuperAreaOfSubArea(zone.Id) ?? 0;
                string name = zone.Side == Alignment.Bonta ? AlignmentTexts.Get("BONTARIAN_PRISM", "Prisme bontarien") : AlignmentTexts.Get("BRAKMARIAN_PRISM", "Prisme brâkmarien");
                result.Add(new WorldMapHint(PrismHintCategory, zone.Side == Alignment.Bonta ? BontaPrismGfx : BrakmarPrismGfx, coords.Value.X, coords.Value.Y, name, superArea));
            }
            return result;
        }

        private static string Number(double value) => value.ToString(Math.Abs(value - Math.Round(value)) < 0.0001 ? "0" : "0.##", CultureInfo.CurrentCulture);

        protected override void Dispose(bool disposing)
        {
            clock?.Stop(); clock?.Dispose(); clock = null;
            if (sideBox != null && !sideBox.IsDisposed) sideBox.Image = null; // image partagée de ClientAssets
        }

        private sealed class FilterItem
        {
            public FilterItem(string label, int value) { Label = label; Value = value; }
            public string Label { get; }
            public int Value { get; }
            public override string ToString() => Label;
        }
    }

    /// <summary>Onglets de la fenêtre <c>Conquest</c> du client (<c>STATS</c>, <c>ZONES_WORD</c>, <c>DEFEND</c>), même rendu que ceux de la guilde.</summary>
    public sealed class ConquestTabStrip : Control
    {
        public const int TabHeight = 24;
        private static readonly ConquestTab[] Kinds = { ConquestTab.Stats, ConquestTab.Zones, ConquestTab.Join };
        private ConquestTab selected = ConquestTab.Stats;

        public ConquestTabStrip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = BotUi.Paper; Height = TabHeight + 3; Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.PageTabList;
        }

        public event EventHandler<ConquestTab> TabClicked;

        public ConquestTab Selected
        {
            get => selected;
            set { if (selected == value) return; selected = value; Invalidate(); }
        }

        public static string TabText(ConquestTab tab)
        {
            switch (tab)
            {
                case ConquestTab.Zones: return AlignmentTexts.Get("ZONES_WORD", "Zones");
                case ConquestTab.Join: return AlignmentTexts.Get("DEFEND", "Défendre");
                default: return AlignmentTexts.Get("STATS", "Statistiques");
            }
        }

        /// <summary>Rectangle de l'onglet (un tiers de la largeur chacun).</summary>
        public Rectangle TabBounds(ConquestTab tab)
        {
            int width = Math.Max(30, (Width - 2 * 3) / 3);
            return new Rectangle((int)tab * (width + 3), 0, width, TabHeight);
        }

        /// <summary>Simule un clic sur l'onglet (raccourci clavier, tests).</summary>
        public void ClickTab(ConquestTab tab) => TabClicked?.Invoke(this, tab);

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var paper = new SolidBrush(BackColor)) e.Graphics.FillRectangle(paper, ClientRectangle);
            using (var rule = new Pen(BotUi.Gold)) e.Graphics.DrawLine(rule, 0, TabHeight, Width, TabHeight);
            foreach (ConquestTab tab in Kinds)
            {
                Rectangle area = TabBounds(tab);
                bool active = tab == selected;
                using (var fill = new SolidBrush(active ? BotUi.PaperLight : BotUi.FrameLight)) e.Graphics.FillRectangle(fill, area);
                using (var border = new Pen(BotUi.Gold)) e.Graphics.DrawRectangle(border, area.X, area.Y, area.Width - 1, area.Height - (active ? 0 : 1));
                TextRenderer.DrawText(e.Graphics, TabText(tab), BotFonts.Get(8, active ? FontStyle.Bold : FontStyle.Regular), area,
                    active ? BotUi.Ink : BotUi.PaperLight, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            foreach (ConquestTab tab in Kinds)
                if (TabBounds(tab).Contains(e.Location) && tab != selected) { TabClicked?.Invoke(this, tab); return; }
        }
    }

    /// <summary>Jauge d'honneur ou de déshonneur (<c>ConquestStatsViewer</c>) : barre parchemin clair, filet doré, remplissage proportionnel.</summary>
    public sealed class ConquestGauge : Control
    {
        private double ratio;

        public ConquestGauge()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = BotUi.Paper; Height = 14; Margin = new Padding(0); Fill = BotUi.Olive;
        }

        public Color Fill { get; set; }
        /// <summary>Part remplie, bornée entre 0 et 1.</summary>
        public double Ratio
        {
            get => ratio;
            set { double bounded = double.IsNaN(value) ? 0 : Math.Max(0, Math.Min(1, value)); if (Math.Abs(bounded - ratio) < 0.0001) return; ratio = bounded; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var paper = new SolidBrush(BackColor)) e.Graphics.FillRectangle(paper, ClientRectangle);
            var bar = new Rectangle(1, 2, Math.Max(2, Width - 3), Math.Max(2, Height - 5));
            using (var light = new SolidBrush(BotUi.PaperLight)) e.Graphics.FillRectangle(light, bar);
            int filled = (int)Math.Round(bar.Width * ratio);
            if (filled > 0) using (var fill = new SolidBrush(Fill)) e.Graphics.FillRectangle(fill, bar.X, bar.Y, filled, bar.Height);
            using (var border = new Pen(BotUi.Gold)) e.Graphics.DrawRectangle(border, bar);
        }
    }
}
