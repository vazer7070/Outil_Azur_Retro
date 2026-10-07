using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Controls;
using Outil_Azur_complet.Bot.Controls.Chat;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Quetes;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>
    /// Volet Quêtes : la fenêtre <c>Quests</c> du client 1.34 (742 × 442, liste à gauche et étapes à droite) ramenée à la largeur du
    /// tiroir, la liste au-dessus des étapes. L'ouverture envoie <c>QL</c> (<c>initData</c>) et décoche « Afficher les quêtes
    /// terminées » ; la liste montre l'état (coche ou marque en cours) et le nom, avec le nombre de quêtes en cours
    /// (<c>PENDING_QUEST</c>). Un clic sur une quête envoie <c>QS&lt;id&gt;</c> ; la vue des étapes (titre : nom de la quête, × pour la
    /// fermer) a deux onglets : « Étape courante » (étape, description, objectifs avec coche ou boussole, récompenses avec leurs
    /// icônes, bouton « Dialogue relatif à l'étape ») et « Hiérarchie des étapes » (étapes passées cochées, courante fléchée, à
    /// venir grisées, description de l'étape choisie). Un objectif localisé oriente la boussole du bandeau, sans rien envoyer,
    /// comme <c>updateCompass</c>. Les messages <c>Im054/055/056</c> affichent un bandeau « liste à actualiser » et son bouton.
    /// </summary>
    public sealed class QuestsPanel : GamePanel
    {
        private const string Reference = "QUÊTES";
        public const int CurrentStepTab = 0, AllStepsTab = 1;
        private Label count, status, notice;
        private CheckBox finished;
        private Panel noticeBar, listArea, viewer, currentArea, allArea;
        private QuestViewerHeader header;
        private MountTabStrip tabs;
        private QuestTextBlock viewerEmpty, stepTitle, stepDescription, allDescription;
        private Label objectivesTitle, rewardsTitle, allTitle;
        private QuestRowList quests, objectives, rewards, steps;
        private FlowLayoutPanel dialogBar;
        private Control refresh, dialog;
        private ToolTip tips;
        private QuestsActions bound;
        private PanelHost watched;
        private QuestStep shownStep;
        private int tab = CurrentStepTab, stepIndex;
        private bool filling, jobIconsHooked;

        public override string Title => QuestTexts.Get("QUESTS_LIST", "Liste des quêtes");
        public override Image Icon => ClientAssets.Icon("icone-quetes", 24);

        /// <summary>Onglet affiché (<see cref="CurrentStepTab"/> ou <see cref="AllStepsTab"/>).</summary>
        public int CurrentTab => tab;
        public QuestRowList QuestList => quests;
        public QuestRowList ObjectiveList => objectives;
        public QuestRowList RewardList => rewards;
        public QuestRowList StepList => steps;
        public CheckBox FinishedBox => finished;
        public Control RefreshButton => refresh;
        public Control DialogButton => dialog;
        public bool NoticeVisible => noticeBar != null && noticeBar.Visible;
        public string NoticeText => notice?.Text ?? string.Empty;
        public string CountText => count?.Text ?? string.Empty;
        public string StatusText => status?.Text ?? string.Empty;
        /// <summary>Titre de la vue des étapes (nom de la quête choisie).</summary>
        public string ViewerTitle => header?.Text ?? string.Empty;
        public bool ViewerShowsQuest => tabs != null && tabs.Visible;
        public string StepTitleText => stepTitle?.Text ?? string.Empty;
        public string StepDescriptionText => stepDescription?.Text ?? string.Empty;
        public string AllStepsDescriptionText => allDescription?.Text ?? string.Empty;
        /// <summary>Dernière cible de boussole donnée par un objectif (tests, diagnostic).</summary>
        public Point? LastCompass { get; private set; }
        /// <summary>Dernier texte de dialogue affiché par « Dialogue relatif à l'étape ».</summary>
        public string LastDialogText { get; private set; }

        protected override Control CreateView()
        {
            var page = Page();
            tips = new ToolTip { InitialDelay = 300, ReshowDelay = 100 };

            count = MakeLabel(string.Empty, 9, true); count.Dock = DockStyle.Top; count.Height = 22; count.TextAlign = ContentAlignment.MiddleLeft;
            count.AutoEllipsis = true; count.Name = "quests-count";
            finished = new CheckBox { Dock = DockStyle.Top, Height = 24, Font = BotFonts.Get(8.25f), ForeColor = BotUi.Ink, BackColor = BotUi.Paper,
                FlatStyle = FlatStyle.Flat, Name = "quests-finished", TextAlign = ContentAlignment.MiddleLeft, CheckAlign = ContentAlignment.MiddleLeft,
                Text = QuestTexts.Get("DISPLAY_FINISHED_QUESTS", "Afficher les quêtes terminées") };
            finished.FlatAppearance.BorderColor = BotUi.Gold; finished.FlatAppearance.CheckedBackColor = BotUi.PaperLight;
            finished.CheckedChanged += async (s, e) => { if (!filling) await Run(q => q.SetShowFinishedAsync(finished.Checked)); };

            noticeBar = new Panel { Dock = DockStyle.Top, Height = 32, BackColor = BotUi.PaperLight, Padding = new Padding(4, 3, 3, 3), Visible = false,
                Name = "quests-notice" };
            notice = MakeLabel(string.Empty, 8.25f); notice.Dock = DockStyle.Fill; notice.TextAlign = ContentAlignment.MiddleLeft; notice.AutoEllipsis = true;
            refresh = MakeButton("Actualiser", async (s, e) => await Run(q => q.RefreshAsync()), false, 96);
            refresh.Dock = DockStyle.Right; refresh.Name = "quests-refresh";
            noticeBar.Controls.Add(notice); noticeBar.Controls.Add(refresh);
            noticeBar.Paint += (s, e) => { using (var rule = new Pen(BotUi.Gold)) e.Graphics.DrawRectangle(rule, 0, 0, noticeBar.Width - 1, noticeBar.Height - 1); };

            quests = new QuestRowList { Dock = DockStyle.Top, Name = "quests-list", AccessibleName = "Quêtes",
                Columns = new[] { QuestTexts.Get("STATE", "État"), QuestTexts.Get("NAME_BIG", "Nom") } };
            quests.RowClicked += async (s, e) => { if (e.Row.Tag is QuestEntry entry) await Run(q => q.SelectAsync(entry.Id)); };
            listArea = Scroller(quests, "quests-list-area"); listArea.Dock = DockStyle.Top; listArea.Height = 6 * QuestRowList.MinRowHeight + QuestRowList.HeaderHeight + 4;

            viewer = new Panel { Dock = DockStyle.Fill, BackColor = BotUi.Paper, Padding = new Padding(0, 6, 0, 0), Name = "quests-viewer" };
            header = new QuestViewerHeader { Dock = DockStyle.Top, Name = "quests-viewer-header" };
            header.CloseClicked += (s, e) => CloseStep();
            tabs = new MountTabStrip(QuestTexts.Get("QUESTS_CURRENT_STEP", "Étape courante"), QuestTexts.Get("QUESTS_STEPS_LIST", "Hiérarchie des étapes"))
                { Dock = DockStyle.Top, Name = "quests-tabs" };
            tabs.TabClicked += (s, index) => SelectTab(index);
            viewerEmpty = new QuestTextBlock { Dock = DockStyle.Top, ForeColor = BotUi.Muted, Font = BotFonts.Get(8.25f, FontStyle.Italic), Name = "quests-viewer-empty" };
            currentArea = BuildCurrentStep();
            allArea = BuildAllSteps();
            viewer.Controls.Add(currentArea); viewer.Controls.Add(allArea); viewer.Controls.Add(viewerEmpty);
            viewer.Controls.Add(tabs); viewer.Controls.Add(header);

            status = MakeStatus(string.Empty); status.Name = "quests-status";
            // Ancrage : le dernier ajouté prend le bord en premier. En haut : compte, case, bandeau de progression, liste ;
            // en bas : statut ; la vue des étapes remplit le reste.
            page.Controls.Add(viewer); page.Controls.Add(listArea); page.Controls.Add(noticeBar); page.Controls.Add(finished); page.Controls.Add(count);
            page.Controls.Add(status);
            return page;
        }

        private Panel BuildCurrentStep()
        {
            var area = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = BotUi.Paper, Name = "quests-current", Padding = new Padding(2, 4, 2, 0) };
            stepTitle = new QuestTextBlock { Dock = DockStyle.Top, Font = BotFonts.Get(9, FontStyle.Bold), Name = "quests-step-title" };
            stepDescription = new QuestTextBlock { Dock = DockStyle.Top, Font = BotFonts.Get(8.25f), Name = "quests-step-description", Padding = new Padding(0, 2, 0, 4) };
            objectivesTitle = SectionTitle(QuestTexts.Get("QUESTS_OBJECTIVES", "Objectifs"), "quests-objectives-title");
            objectives = new QuestRowList { Dock = DockStyle.Top, Name = "quests-objectives", AccessibleName = "Objectifs", EmptyText = "Aucun objectif." };
            objectives.RowClicked += (s, e) => { if (e.Row.Tag is QuestObjective objective) PointCompass(objective); };
            rewardsTitle = SectionTitle(QuestTexts.Get("QUESTS_REWARDS", "Récompenses"), "quests-rewards-title");
            rewards = new QuestRowList { Dock = DockStyle.Top, Name = "quests-rewards", AccessibleName = "Récompenses", EmptyText = "Aucune récompense." };
            dialog = MakeButton(QuestTexts.Get("STEP_DIALOG", "Dialogue relatif à l'étape"), async (s, e) => await ShowDialogAsync(), false, 220);
            dialog.Name = "quests-dialog";
            dialogBar = BotUi.Actions(dialog); dialogBar.Dock = DockStyle.Top; dialogBar.Visible = false;
            foreach (Control part in new Control[] { dialogBar, rewards, rewardsTitle, objectives, objectivesTitle, stepDescription, stepTitle }) area.Controls.Add(part);
            return area;
        }

        private Panel BuildAllSteps()
        {
            var area = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = BotUi.Paper, Name = "quests-all", Padding = new Padding(2, 4, 2, 0), Visible = false };
            allTitle = SectionTitle(QuestTexts.Get("QUESTS_ALL_STEPS", "Liste des étapes"), "quests-all-title");
            steps = new QuestRowList { Dock = DockStyle.Top, Name = "quests-steps", AccessibleName = "Étapes", EmptyText = "Aucune étape." };
            steps.RowClicked += (s, e) => { stepIndex = e.Index; ShowStepDescription(); };
            allDescription = new QuestTextBlock { Dock = DockStyle.Top, Font = BotFonts.Get(8.25f), Name = "quests-all-description", Padding = new Padding(0, 6, 0, 4) };
            foreach (Control part in new Control[] { allDescription, steps, allTitle }) area.Controls.Add(part);
            return area;
        }

        private static Panel Scroller(Control content, string name)
        {
            var area = new Panel { AutoScroll = true, BackColor = BotUi.Paper, Name = name, Margin = new Padding(0) };
            area.Controls.Add(content);
            return area;
        }

        private static Label SectionTitle(string text, string name)
        {
            var label = MakeLabel(text, 9, true);
            label.Dock = DockStyle.Top; label.Height = 24; label.TextAlign = ContentAlignment.BottomLeft; label.AutoEllipsis = true; label.Name = name;
            return label;
        }

        protected override void OnBind(GameClass game)
        {
            bound = game.Interactions?.Quests;
            if (bound != null)
            {
                bound.TrackServerMessages();
                bound.Changed += OnQuestsChanged;
                if (Host != null && Host.IsOpen(this)) bound.WindowOpen = true;
            }
            watched = Host;
            if (watched != null) { watched.PanelShown += OnPanelShown; watched.PanelClosed += OnPanelClosed; }
            if (!jobIconsHooked) { JobIcons.Loaded += OnJobIconLoaded; jobIconsHooked = true; }
        }

        protected override void OnUnbind(GameClass game)
        {
            if (bound != null) { bound.Changed -= OnQuestsChanged; bound.CloseWindow(); }
            if (watched != null) { watched.PanelShown -= OnPanelShown; watched.PanelClosed -= OnPanelClosed; }
            bound = null; watched = null;
        }

        // ----- Ouverture (fil de l'interface) -----

        private void OnPanelShown(object sender, PanelEventArgs e)
        {
            if (!ReferenceEquals(e?.Panel, this) || bound == null || bound.WindowOpen) return;
            // Comme Quests.initData : la liste est redemandée à chaque ouverture, case « terminées » décochée.
            _ = Run(q => q.OpenWindowAsync());
        }

        private void OnPanelClosed(object sender, PanelEventArgs e)
        {
            if (ReferenceEquals(e?.Panel, this)) bound?.CloseWindow();
        }

        // ----- Événements du modèle (fil réseau) -----

        private void OnQuestsChanged() => Post(RefreshView);

        private void OnJobIconLoaded() => ItemAssets.Redraw(rewards);

        /// <summary>Passe par la fenêtre de jeu (le tiroir jamais affiché n'a pas de poignée) ; rien sans fenêtre.</summary>
        private bool Post(Action action) => ExchangePanel.PostToForm(Host, () => { if (!IsDisposed) action(); });

        // ----- Affichage -----

        public override void RefreshView()
        {
            QuestsActions q = Game?.Interactions?.Quests;
            if (q == null || quests == null || IsDisposed) return;
            bool received = q.HasReceived;
            count.Text = received ? QuestTexts.PendingCount(q.PendingCount) : "Liste des quêtes non reçue : elle est demandée à l'ouverture (QL).";
            filling = true;
            try { finished.Checked = q.ShowFinished; }
            finally { filling = false; }
            finished.Enabled = received;

            QuestUpdate update = q.LastUpdate;
            noticeBar.Visible = q.NeedsRefresh;
            notice.Text = (update != null ? update.Text + " · " : string.Empty) + "liste à actualiser.";
            tips.SetToolTip(notice, notice.Text);
            refresh.Enabled = Connected;

            IReadOnlyList<QuestEntry> visible = q.VisibleQuests;
            int? selectedId = q.SelectedQuestId;
            quests.EmptyText = !received ? "Liste non reçue." : q.ShowFinished ? "Aucune quête." : "Aucune quête en cours.";
            quests.SetRows(visible.Select(entry => new QuestRow(entry, entry.Name,
                owner => QuestIcons.Client(entry.IsFinished ? "quete-terminee" : "quete-en-cours", owner),
                tooltip: entry.Name + "\n" + (entry.IsFinished ? "Terminée" : "En cours") + " · n° " + entry.Id.ToString(CultureInfo.InvariantCulture),
                clickable: true)).ToArray(),
                selectedId.HasValue ? IndexOf(visible, selectedId.Value) : -1);

            QuestEntry selected = q.SelectedQuest;
            QuestStep step = q.SelectedStep;
            if (!ReferenceEquals(step, shownStep))
            {
                // Nouvelle étape reçue (QS) : onglet « Étape courante », première étape de la hiérarchie choisie, comme setStep.
                shownStep = step; stepIndex = 0;
                if (step != null) tab = CurrentStepTab;
            }
            header.Text = selected != null ? selected.Name : QuestTexts.Get("STEPS", "Étapes");
            header.CloseVisible = selected != null;
            tabs.Visible = selected != null;
            tabs.Selected = tab;
            if (selected == null)
            {
                viewerEmpty.Text = !received ? string.Empty : visible.Count == 0 ? string.Empty : "Choisissez une quête pour afficher ses étapes (QS).";
                viewerEmpty.Visible = viewerEmpty.Text.Length > 0;
                currentArea.Visible = allArea.Visible = false;
            }
            else if (step == null)
            {
                viewerEmpty.Text = "Étapes de « " + selected.Name + " » demandées au serveur…";
                viewerEmpty.Visible = true;
                currentArea.Visible = allArea.Visible = false;
            }
            else
            {
                viewerEmpty.Visible = false;
                FillCurrentStep(step);
                FillAllSteps(step);
                currentArea.Visible = tab == CurrentStepTab;
                allArea.Visible = tab == AllStepsTab;
            }
            status.Text = q.LastMessage;
        }

        private void FillCurrentStep(QuestStep step)
        {
            stepTitle.Text = step.HasCurrentStep
                ? QuestTexts.Get("STEP", "Étape") + " : " + step.Name
                : "Aucune étape en cours.";
            stepDescription.Text = step.Description;
            stepDescription.Visible = step.Description.Length > 0;
            objectives.SetRows(step.Objectives.Select(objective =>
            {
                Point? where = objective.Coordinates;
                string icon = objective.IsFinished ? "quete-terminee" : where.HasValue ? "objectif-boussole" : null;
                string tip = where.HasValue
                    ? "[" + where.Value.X.ToString(CultureInfo.InvariantCulture) + "," + where.Value.Y.ToString(CultureInfo.InvariantCulture) + "] : clic pour orienter la boussole."
                    : null;
                return new QuestRow(objective, objective.Description, icon == null ? (Func<Control, Image>)null : owner => QuestIcons.Client(icon, owner),
                    muted: objective.IsFinished, clickable: where.HasValue, tooltip: tip);
            }).ToArray());
            rewards.SetRows(step.Rewards.Select(reward => new QuestRow(reward, reward.Label, owner => QuestIcons.Reward(reward, owner),
                tooltip: QuestIcons.Describe(reward))).ToArray());
            rewardsTitle.Visible = rewards.Visible = step.HasCurrentStep;
            dialogBar.Visible = step.DialogId.HasValue;
            if (step.DialogId.HasValue) tips.SetToolTip(dialog, QuestTexts.Get("STEP_DIALOG", "Dialogue relatif à l'étape") + " :\n\n" + step.DialogText);
        }

        private void FillAllSteps(QuestStep step)
        {
            IReadOnlyList<QuestStepLine> lines = step.AllSteps;
            if (stepIndex >= lines.Count) stepIndex = 0;
            steps.SetRows(lines.Select(line => new QuestRow(line, line.Name,
                line.State == QuestStepState.Current ? owner => QuestIcons.Client("etape-courante", owner)
                    : line.State == QuestStepState.Finished ? owner => QuestIcons.Client("quete-terminee", owner) : (Func<Control, Image>)null,
                muted: line.State == QuestStepState.NotDone, clickable: true)).ToArray(), lines.Count == 0 ? -1 : stepIndex);
            ShowStepDescription();
        }

        private void ShowStepDescription()
        {
            IReadOnlyList<QuestStepLine> lines = shownStep?.AllSteps ?? new QuestStepLine[0];
            if (steps != null && lines.Count > 0) steps.SelectedIndex = Math.Max(0, Math.Min(stepIndex, lines.Count - 1));
            allDescription.Text = lines.Count == 0 ? string.Empty : lines[Math.Max(0, Math.Min(stepIndex, lines.Count - 1))].Description;
        }

        private static int IndexOf(IReadOnlyList<QuestEntry> list, int id)
        {
            for (int index = 0; index < list.Count; index++) if (list[index].Id == id) return index;
            return -1;
        }

        // ----- Actions -----

        /// <summary>Onglet « Étape courante » ou « Hiérarchie des étapes » (<c>setCurrentTab</c>) ; rien n'est envoyé.</summary>
        public void SelectTab(int index)
        {
            if (index != CurrentStepTab && index != AllStepsTab) return;
            tab = index;
            RefreshView();
        }

        /// <summary>Simule un clic sur la quête (tests, clavier) ; faux si elle n'est pas affichée.</summary>
        public bool ClickQuest(int questId) => quests != null && quests.ClickRow(row => row.Tag is QuestEntry entry && entry.Id == questId);

        /// <summary>Simule un clic sur l'objectif (boussole) ; faux s'il n'est pas affiché.</summary>
        public bool ClickObjective(int objectiveId) => objectives != null && objectives.ClickRow(row => row.Tag is QuestObjective objective && objective.Id == objectiveId);

        /// <summary>Bouton × de la vue des étapes (<c>_btnCloseStep</c>) : plus de quête choisie, rien n'est envoyé.</summary>
        public void CloseStep()
        {
            Game?.Interactions?.Quests?.Deselect();
            RefreshView();
        }

        /// <summary>Objectif localisé (<c>QuestStepViewer.itemSelected</c>) : boussole du bandeau, sans paquet.</summary>
        private void PointCompass(QuestObjective objective)
        {
            Point? where = objective?.Coordinates;
            if (!where.HasValue) return;
            LastCompass = where;
            (Host?.FindForm() as GameClientFullform)?.Banner?.SetCompassTarget(where);
            Feedback(ChatLinks.DescribeCompass(where.Value.X, where.Value.Y, Game?.Map));
        }

        /// <summary>« Dialogue relatif à l'étape » : la question du PNJ dans une boîte d'information, comme <c>showMessage</c>.</summary>
        public Task ShowDialogAsync()
        {
            QuestStep step = shownStep;
            if (step?.DialogId == null) return Task.CompletedTask;
            LastDialogText = step.DialogText;
            return ShowInfo(QuestTexts.Get("STEP_DIALOG", "Dialogue relatif à l'étape"), LastDialogText);
        }

        private async Task ShowInfo(string title, string text)
        {
            try { await BotDialogs.InfoAsync(Host, title, text); }
            catch (Exception error) { Account?.Logger?.LogException(Reference, error); }
        }

        private Task Run(Func<QuestsActions, Task<InteractionResult>> action)
        {
            QuestsActions q = Game?.Interactions?.Quests;
            return q == null ? Task.CompletedTask : ReportAsync(() => action(q));
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing) return;
            if (jobIconsHooked) { JobIcons.Loaded -= OnJobIconLoaded; jobIconsHooked = false; }
            tips?.Dispose();
        }
    }

    /// <summary>Ligne de <see cref="QuestRowList"/> : objet du modèle, texte, icône lue dans le cache, style.</summary>
    public sealed class QuestRow
    {
        public object Tag { get; }
        public string Text { get; }
        /// <summary>Icône déjà lue, ou <c>null</c> (la lecture part alors en tâche de fond et redessine la liste).</summary>
        public Func<Control, Image> Icon { get; }
        /// <summary>Texte gris (<c>GreyLeftSmallLabel</c> du client) : étape à venir, objectif atteint.</summary>
        public bool Muted { get; }
        public bool Clickable { get; }
        public string ToolTip { get; }

        public QuestRow(object tag, string text, Func<Control, Image> icon = null, bool muted = false, bool clickable = false, string tooltip = null)
        {
            Tag = tag; Text = text ?? string.Empty; Icon = icon; Muted = muted; Clickable = clickable; ToolTip = tooltip;
        }
    }

    public sealed class QuestRowEventArgs : EventArgs
    {
        public int Index { get; }
        public QuestRow Row { get; }
        internal QuestRowEventArgs(int index, QuestRow row) { Index = index; Row = row; }
    }

    /// <summary>
    /// Liste dessinée des fenêtres de quêtes (<c>UI_QuestsQuestItem</c>, <c>UI_QuestsObjectivetItem</c>, <c>UI_QuestsStepItem</c>,
    /// récompenses) : colonne d'icône, texte replié sur plusieurs lignes au besoin, ligne choisie surlignée. La hauteur suit le
    /// contenu ; le contrôle se place dans un panneau défilant. Les icônes ne sont lues que dans le cache.
    /// </summary>
    public sealed class QuestRowList : Control
    {
        public const int MinRowHeight = 22, HeaderHeight = 18, IconColumn = 30;
        private readonly ToolTip tips = new ToolTip { InitialDelay = 300, ReshowDelay = 100 };
        private QuestRow[] rows = new QuestRow[0];
        private int[] tops = new int[0], heights = new int[0];
        private int selected = -1, hover = -1;
        private string[] columns;
        private string emptyText = string.Empty;

        public QuestRowList()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = BotUi.Paper; Height = MinRowHeight + 1;
        }

        public event EventHandler<QuestRowEventArgs> RowClicked;

        public IReadOnlyList<QuestRow> Rows => rows;
        /// <summary>En-têtes des colonnes (icône, texte) comme <c>columnsNames</c> ; <c>null</c> sans en-tête.</summary>
        public string[] Columns { get => columns; set { columns = value; Relayout(); } }
        public string EmptyText { get => emptyText; set { if (emptyText == value) return; emptyText = value ?? string.Empty; if (rows.Length == 0) Invalidate(); } }
        public int SelectedIndex { get => selected; set { int next = value >= 0 && value < rows.Length ? value : -1; if (next == selected) return; selected = next; Invalidate(); } }
        private int Top0 => columns != null ? HeaderHeight : 0;

        /// <summary>Remplace les lignes (fil de l'interface) ; <paramref name="selectedIndex"/> -1 pour aucune.</summary>
        public void SetRows(IReadOnlyList<QuestRow> next, int selectedIndex = -1)
        {
            rows = (next ?? new QuestRow[0]).ToArray();
            selected = selectedIndex >= 0 && selectedIndex < rows.Length ? selectedIndex : -1;
            hover = -1;
            AccessibleDescription = string.Join(", ", rows.Select(row => row.Text));
            Relayout();
        }

        public Rectangle RowBounds(int index) =>
            index >= 0 && index < rows.Length ? new Rectangle(0, tops[index], Width, heights[index]) : Rectangle.Empty;

        public int IndexAt(Point point)
        {
            for (int index = 0; index < rows.Length; index++) if (point.Y >= tops[index] && point.Y < tops[index] + heights[index]) return index;
            return -1;
        }

        /// <summary>Simule un clic sur la première ligne qui répond au critère (tests, clavier) ; faux si aucune.</summary>
        public bool ClickRow(Func<QuestRow, bool> match)
        {
            for (int index = 0; index < rows.Length; index++)
                if (match(rows[index])) { Raise(index); return true; }
            return false;
        }

        private void Raise(int index)
        {
            QuestRow row = rows[index];
            if (!row.Clickable) return;
            RowClicked?.Invoke(this, new QuestRowEventArgs(index, row));
        }

        private int TextWidth => Math.Max(20, Width - IconColumn - 8);

        private void Relayout()
        {
            tops = new int[rows.Length]; heights = new int[rows.Length];
            int y = Top0;
            Font font = BotFonts.Get(8.25f);
            for (int index = 0; index < rows.Length; index++)
            {
                Size measured = TextRenderer.MeasureText(rows[index].Text.Length == 0 ? " " : rows[index].Text, font, new Size(TextWidth, int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl);
                tops[index] = y; heights[index] = Math.Max(MinRowHeight, measured.Height + 6);
                y += heights[index];
            }
            int height = Math.Max(Top0 + MinRowHeight, y) + 1;
            if (Height != height) Height = height;
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (rows.Length > 0) Relayout();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            using (var paper = new SolidBrush(BackColor)) graphics.FillRectangle(paper, ClientRectangle);
            using (var rule = new Pen(BotUi.Gold))
            {
                if (columns != null)
                {
                    Font bold = BotFonts.Get(8, FontStyle.Bold);
                    if (columns.Length > 0)
                        TextRenderer.DrawText(graphics, columns[0], bold, new Rectangle(2, 0, IconColumn + 4, HeaderHeight), BotUi.Muted,
                            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    if (columns.Length > 1)
                        TextRenderer.DrawText(graphics, columns[1], bold, new Rectangle(IconColumn + 8, 0, TextWidth, HeaderHeight), BotUi.Muted,
                            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    graphics.DrawLine(rule, 0, HeaderHeight - 1, Width, HeaderHeight - 1);
                }
                if (rows.Length == 0)
                {
                    TextRenderer.DrawText(graphics, emptyText, BotFonts.Get(8.25f, FontStyle.Italic), new Rectangle(6, Top0, Math.Max(10, Width - 12), MinRowHeight),
                        BotUi.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    return;
                }
                Font font = BotFonts.Get(8.25f);
                for (int index = 0; index < rows.Length; index++)
                {
                    QuestRow row = rows[index];
                    var area = new Rectangle(0, tops[index], Width, heights[index]);
                    if (index == selected)
                    {
                        using (var light = new SolidBrush(BotUi.PaperLight)) graphics.FillRectangle(light, area);
                        graphics.DrawRectangle(rule, area.X, area.Y, area.Width - 1, area.Height - 1);
                    }
                    else if (index == hover && row.Clickable)
                        using (var light = new SolidBrush(Color.FromArgb(120, BotUi.PaperLight))) graphics.FillRectangle(light, area);
                    Image icon = null;
                    try { icon = row.Icon?.Invoke(this); }
                    catch (Exception) { /* Icône illisible : la ligne reste sans image. */ }
                    if (icon != null)
                    {
                        var place = new RectangleF(7, area.Y + (Math.Min(area.Height, MinRowHeight) - 16) / 2f, 16, 16);
                        lock (icon) ClientAssets.DrawFit(graphics, icon, place);
                    }
                    TextRenderer.DrawText(graphics, row.Text, font, new Rectangle(IconColumn + 8, area.Y + 3, TextWidth, area.Height - 4),
                        row.Muted ? BotUi.Muted : BotUi.Ink, TextFormatFlags.WordBreak | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl);
                    graphics.DrawLine(rule, 2, area.Bottom - 1, Width - 3, area.Bottom - 1);
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = IndexAt(e.Location);
            if (index == hover) return;
            hover = index;
            Cursor = index >= 0 && rows[index].Clickable ? Cursors.Hand : Cursors.Default;
            tips.SetToolTip(this, index >= 0 ? rows[index].ToolTip : null);
            Invalidate();
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
            if (e.Button != MouseButtons.Left) return;
            int index = IndexAt(e.Location);
            if (index >= 0) Raise(index);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tips.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>Texte replié sur la largeur du parent, hauteur ajustée au contenu (description d'étape, titres longs).</summary>
    public sealed class QuestTextBlock : Control
    {
        public QuestTextBlock()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = BotUi.Paper; ForeColor = BotUi.Ink; Font = BotFonts.Get(8.25f); Height = 18;
        }

        private void Fit()
        {
            int width = Math.Max(20, Width - Padding.Horizontal);
            int height = Text.Length == 0 ? 0 : TextRenderer.MeasureText(Text, Font, new Size(width, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl).Height;
            int next = Math.Max(4, height + Padding.Vertical + 2);
            if (Height != next) Height = next;
            Invalidate();
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); AccessibleName = Text; Fit(); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Fit(); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); Fit(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var paper = new SolidBrush(BackColor)) e.Graphics.FillRectangle(paper, ClientRectangle);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(Padding.Left, Padding.Top, Math.Max(1, Width - Padding.Horizontal), Math.Max(1, Height - Padding.Vertical)),
                ForeColor, TextFormatFlags.WordBreak | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl);
        }
    }

    /// <summary>Barre de titre de la vue des étapes (<c>_winBgViewer</c>, bouton <c>_btnCloseStep</c>) : brun du client, × à droite.</summary>
    public sealed class QuestViewerHeader : Control
    {
        public const int BarHeight = 26;
        private bool closeVisible;

        public QuestViewerHeader()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = BotUi.Frame; ForeColor = BotUi.PaperLight; Height = BarHeight;
            QuestIcons.Client("fermer-haut", this);
        }

        public event EventHandler CloseClicked;

        public bool CloseVisible { get => closeVisible; set { if (closeVisible == value) return; closeVisible = value; Invalidate(); } }
        public Rectangle CloseBounds => new Rectangle(Width - BarHeight + 4, 4, BarHeight - 8, BarHeight - 8);

        /// <summary>Simule un clic sur × (tests, clavier).</summary>
        public void ClickClose() { if (closeVisible) CloseClicked?.Invoke(this, EventArgs.Empty); }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); AccessibleName = Text; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            using (var frame = new SolidBrush(BackColor)) graphics.FillRectangle(frame, ClientRectangle);
            using (var rule = new Pen(BotUi.Gold)) graphics.DrawLine(rule, 0, Height - 1, Width, Height - 1);
            int right = closeVisible ? BarHeight : 4;
            TextRenderer.DrawText(graphics, Text, BotFonts.Get(9, FontStyle.Bold), new Rectangle(8, 0, Math.Max(10, Width - 8 - right), Height), ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (!closeVisible) return;
            Image cross = QuestIcons.Client("fermer-haut", this);
            if (cross != null) lock (cross) ClientAssets.DrawFit(graphics, cross, CloseBounds);
            else
                TextRenderer.DrawText(graphics, "×", BotFonts.Get(10, FontStyle.Bold), CloseBounds, ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && closeVisible && CloseBounds.Contains(e.Location)) CloseClicked?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Icônes des quêtes : pièces de la fenêtre (<c>Client/quete-*.png</c>, <c>etape-courante</c>, <c>objectif-boussole</c>,
    /// <c>UI_QuestXP</c>, <c>kamas</c>) et récompenses (objets, émotes, métiers, sorts). Jamais de lecture de disque sur le fil
    /// de l'interface : l'image manquante est demandée à <see cref="ClientAssets.GetAsync"/> et le contrôle redessiné à son arrivée.
    /// </summary>
    internal static class QuestIcons
    {
        private static readonly object sync = new object();
        private static readonly Dictionary<string, List<WeakReference<Control>>> pending = new Dictionary<string, List<WeakReference<Control>>>(StringComparer.Ordinal);

        internal static Image Client(string name, Control owner) => Cached("Client", name, owner);

        internal static Image Reward(QuestReward reward, Control owner)
        {
            if (reward == null) return null;
            string id = reward.Id.ToString(CultureInfo.InvariantCulture);
            switch (reward.Kind)
            {
                case QuestRewardKind.Experience: return Client("UI_QuestXP", owner);
                case QuestRewardKind.Kamas: return Client("kamas", owner);
                case QuestRewardKind.Item: return ItemIcons.TryGet(reward.Id, owner);
                case QuestRewardKind.Emote: return Cached("Emotes", id, owner);
                case QuestRewardKind.Job: return JobIcons.TryGet(reward.Id);
                case QuestRewardKind.Spell: return Cached("Spells", id, owner);
                default: return null;
            }
        }

        internal static string Describe(QuestReward reward)
        {
            switch (reward?.Kind)
            {
                case QuestRewardKind.Experience: return "Expérience : " + reward.Label;
                case QuestRewardKind.Kamas: return "Kamas : " + reward.Label;
                case QuestRewardKind.Item: return "Objet : " + reward.Label;
                case QuestRewardKind.Emote: return "Émote : " + reward.Label;
                case QuestRewardKind.Job: return "Métier : " + reward.Label;
                case QuestRewardKind.Spell: return "Sort : " + reward.Label;
                default: return null;
            }
        }

        private static Image Cached(string family, string name, Control owner)
        {
            if (ClientAssets.TryCached(family, name, out Bitmap image)) return image;
            string key = family + "/" + name;
            lock (sync)
            {
                // Une seule lecture par image ; chaque contrôle qui l'attend est redessiné à son arrivée.
                bool reading = pending.TryGetValue(key, out List<WeakReference<Control>> waiting);
                if (!reading) pending[key] = waiting = new List<WeakReference<Control>>();
                if (owner != null && !waiting.Any(entry => entry.TryGetTarget(out Control known) && ReferenceEquals(known, owner)))
                    waiting.Add(new WeakReference<Control>(owner));
                if (reading) return null;
            }
            ClientAssets.GetAsync(family, name).ContinueWith(task =>
            {
                List<WeakReference<Control>> owners;
                lock (sync) { pending.TryGetValue(key, out owners); pending.Remove(key); }
                if (task.Status != TaskStatus.RanToCompletion || task.Result == null || owners == null) return;
                foreach (WeakReference<Control> entry in owners)
                    if (entry.TryGetTarget(out Control control)) ItemAssets.Redraw(control);
            }, TaskScheduler.Default);
            return null;
        }
    }
}
