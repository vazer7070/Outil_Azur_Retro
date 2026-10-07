using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Jobs;
using Tool_BotProtocol.Game.Perso;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>
    /// Onglet « Métiers » de la fiche du personnage (<c>StatsJob</c>, <c>JobViewer</c> et <c>JobOptionsViewer</c> du client 1.34) :
    /// cases des métiers et des spécialisations avec leur icône (<c>Jobs/&lt;g&gt;.png</c>), niveau et jauge d'expérience (<c>JS</c>, <c>JX</c>,
    /// <c>JN</c>), puis trois onglets : compétences, recettes connues (textes du client, calculées hors du thread de l'interface) et options
    /// d'artisan (<c>JO</c>) avec le mode public (<c>EW+</c> / <c>EW-</c>). Les options ne changent qu'à la réponse <c>JO</c> du serveur.
    /// </summary>
    public sealed class JobsPanel : GamePanel
    {
        public const int SkillsTab = 0, RecipesTab = 1, OptionsTab = 2;
        private JobStrip strip;
        private Label jobName, jobLevel, jobTool, recipesState, recipeDetail, optionsState, minSlotsLabel, publicState, publicInfo, referenceState, panelState;
        private XpGauge gauge;
        private JobTabStrip tabs;
        private Panel[] pages;
        private ListView skillList, recipeList;
        private CheckBox notFree, freeIfFailed, resourcesNeeded;
        private NumericUpDown minSlots;
        private Control saveOptions, publicToggle;
        private CharacterClass character;
        private JobsActions actions;
        private int optionsJob = -1;
        private bool optionsDirty, loadingOptions, refreshing;
        private string recipesKey = string.Empty;
        private int recipesRequest;
        private IReadOnlyList<Recipe> shownRecipes = new Recipe[0];

        public override string Title => "Métiers";
        public override Image Icon => ClientAssets.Icon("icone-caracteristiques", 24);

        /// <summary>Métier affiché, 0 sans métier.</summary>
        public int SelectedJobId => strip?.SelectedJobId ?? 0;
        /// <summary>Onglet affiché (<see cref="SkillsTab"/>, <see cref="RecipesTab"/>, <see cref="OptionsTab"/>).</summary>
        public int SelectedTab => tabs?.Selected ?? SkillsTab;
        /// <summary>Recettes affichées pour le métier choisi (vide tant que le calcul n'est pas terminé).</summary>
        public IReadOnlyList<Recipe> ShownRecipes => shownRecipes;

        /// <summary>Affiche ce métier (clic sur sa case) ; faux s'il n'est pas dans la liste.</summary>
        public bool SelectJob(int jobId)
        {
            if (strip == null || !strip.SelectJob(jobId)) return false;
            return true;
        }

        /// <summary>Affiche un onglet (clic sur l'onglet).</summary>
        public void ShowTab(int tab)
        {
            if (tabs == null || tab < 0 || tab >= pages.Length) return;
            tabs.Selected = tab;
            for (int index = 0; index < pages.Length; index++) pages[index].Visible = index == tab;
            RefreshView();
        }

        private static string Text(string key, string fallback, params string[] args) => JobCatalog.Text(key, fallback, args);

        protected override Control CreateView()
        {
            var page = Page();
            strip = new JobStrip { Dock = DockStyle.Top, Name = "jobs-strip", AccessibleName = "Métiers du personnage" };
            strip.SelectionChanged += (s, e) => { if (refreshing) return; optionsDirty = false; RefreshView(); };

            jobName = MakeLabel(string.Empty, 10, true); jobName.Dock = DockStyle.Top; jobName.Height = 21; jobName.Name = "jobs-name";
            jobLevel = MakeLabel(string.Empty, 8.25f); jobLevel.Dock = DockStyle.Top; jobLevel.Height = 18; jobLevel.Name = "jobs-level";
            gauge = new XpGauge { Dock = DockStyle.Top, Height = 14, Name = "jobs-xp" };
            jobTool = MakeLabel(string.Empty, 8); jobTool.Dock = DockStyle.Top; jobTool.Height = 20; jobTool.ForeColor = BotUi.Muted; jobTool.Name = "jobs-tool";
            jobTool.TextAlign = ContentAlignment.MiddleLeft;

            tabs = new JobTabStrip(Text("SKILLS", "Compétences"), Text("RECEIPTS", "Recettes"), Text("JOB_OPTIONS", "Options")) { Dock = DockStyle.Top, Name = "jobs-tabs" };
            tabs.TabClicked += (s, tab) => ShowTab(tab);

            var content = new Panel { Dock = DockStyle.Fill, BackColor = BotUi.Paper, Padding = new Padding(0, 4, 0, 0) };
            pages = new[] { BuildSkillsPage(), BuildRecipesPage(), BuildOptionsPage() };
            foreach (Panel tabPage in pages) content.Controls.Add(tabPage);
            for (int index = 0; index < pages.Length; index++) pages[index].Visible = index == SkillsTab;

            panelState = MakeStatus("Aucun métier connu : la liste arrive avec JS et JX après la connexion.");
            panelState.Name = "jobs-state";

            // Ordre d'ancrage : le dernier ajouté prend le bord en premier.
            page.Controls.Add(content); page.Controls.Add(panelState);
            page.Controls.Add(tabs); page.Controls.Add(jobTool); page.Controls.Add(gauge); page.Controls.Add(jobLevel); page.Controls.Add(jobName);
            page.Controls.Add(strip);
            return page;
        }

        private Panel BuildSkillsPage()
        {
            var tabPage = new Panel { Dock = DockStyle.Fill, BackColor = BotUi.Paper };
            skillList = MakeList(8.25f, Text("SKILL", "Compétence"), "Détail");
            skillList.Name = "jobs-skills"; skillList.Columns[0].Width = 205; skillList.Columns[1].Width = 170; skillList.ShowItemToolTips = true;
            var help = MakeLabel("Récolte : durée et quantité récoltée. Atelier : cases et chance de réussite.", 8);
            help.Dock = DockStyle.Bottom; help.Height = 34; help.ForeColor = BotUi.Muted;
            tabPage.Controls.Add(skillList); tabPage.Controls.Add(help);
            return tabPage;
        }

        private Panel BuildRecipesPage()
        {
            var tabPage = new Panel { Dock = DockStyle.Fill, BackColor = BotUi.Paper };
            recipeList = MakeList(8.25f, Text("CRAFTED_ITEM", "Objet"), "Ingr.");
            recipeList.Name = "jobs-recipes"; recipeList.Columns[0].Width = 300; recipeList.Columns[1].Width = 60;
            recipeList.SelectedIndexChanged += (s, e) => ShowRecipeDetail();
            recipeDetail = MakeLabel(string.Empty, 8); recipeDetail.Dock = DockStyle.Bottom; recipeDetail.Height = 52; recipeDetail.Name = "jobs-recipe-detail";
            recipesState = MakeLabel(string.Empty, 8); recipesState.Dock = DockStyle.Bottom; recipesState.Height = 20; recipesState.ForeColor = BotUi.Muted;
            recipesState.Name = "jobs-recipes-state";
            tabPage.Controls.Add(recipeList); tabPage.Controls.Add(recipesState); tabPage.Controls.Add(recipeDetail);
            return tabPage;
        }

        private Panel BuildOptionsPage()
        {
            var tabPage = new Panel { Dock = DockStyle.Fill, BackColor = BotUi.Paper };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = BotUi.Paper };
            optionsState = MakeLabel(string.Empty, 8); optionsState.AutoSize = false; optionsState.Width = 380; optionsState.Height = 32; optionsState.ForeColor = BotUi.Muted;
            optionsState.Name = "jobs-options-state";
            notFree = Check("jobs-not-free", Text("NOT_FREE", "Payant"));
            freeIfFailed = Check("jobs-free-if-failed", Text("FREE_IF_FAILED", "Gratuit en cas d'échec"));
            resourcesNeeded = Check("jobs-resources-needed", Text("CRAFT_RESSOURCES_NEEDED", "Ressources fournies par le client"));
            var slotsRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = BotUi.Paper, Margin = new Padding(0, 2, 0, 2) };
            minSlotsLabel = MakeLabel(Text("MIN_ITEM_IN_RECEIPT", "Ingrédients minimum"), 8.25f); minSlotsLabel.AutoSize = false; minSlotsLabel.Width = 190;
            minSlotsLabel.Height = 28; minSlotsLabel.TextAlign = ContentAlignment.MiddleLeft;
            minSlots = InventoryPanel.Quantity(); minSlots.Minimum = 2; minSlots.Maximum = 8; minSlots.Value = 2; minSlots.Name = "jobs-min-slots";
            minSlots.ValueChanged += (s, e) => MarkOptionsDirty();
            slotsRow.Controls.Add(minSlotsLabel); slotsRow.Controls.Add(minSlots);
            saveOptions = MakeButton(Text("SAVE", "Sauvegarder"), async (s, e) => await SaveOptions(), true, 140); saveOptions.Name = "jobs-save";
            saveOptions.Margin = new Padding(0, 4, 0, 8);

            var rule = new Panel { Height = 1, Width = 380, BackColor = BotUi.Gold, Margin = new Padding(0, 4, 0, 6) };
            publicState = MakeLabel(string.Empty, 9, true); publicState.AutoSize = false; publicState.Width = 380; publicState.Height = 22; publicState.Name = "jobs-public-state";
            publicToggle = MakeButton(Text("ENABLE", "Activer"), async (s, e) => await TogglePublicMode(), false, 140); publicToggle.Name = "jobs-public-toggle";
            publicToggle.Margin = new Padding(0, 2, 0, 4);
            publicInfo = MakeLabel(Text("PUBLIC_MODE_INFOS", "En mode public, les autres joueurs voient vos métiers et peuvent vous demander de fabriquer leurs objets."), 8);
            publicInfo.AutoSize = false; publicInfo.Width = 380; publicInfo.Height = 46; publicInfo.ForeColor = BotUi.Muted;
            referenceState = MakeLabel(string.Empty, 8); referenceState.AutoSize = false; referenceState.Width = 380; referenceState.Height = 34;
            referenceState.Name = "jobs-reference";

            flow.Controls.AddRange(new Control[] { optionsState, notFree, freeIfFailed, resourcesNeeded, slotsRow, saveOptions, rule, publicState, publicToggle, publicInfo, referenceState });
            tabPage.Controls.Add(flow);
            return tabPage;
        }

        private CheckBox Check(string name, string text)
        {
            var box = new CheckBox { Name = name, Text = text, AutoSize = true, Font = BotFonts.Get(8.25f), ForeColor = BotUi.Ink, BackColor = BotUi.Paper,
                Margin = new Padding(0, 2, 0, 2) };
            box.CheckedChanged += (s, e) => MarkOptionsDirty();
            return box;
        }

        private void MarkOptionsDirty()
        {
            if (loadingOptions) return;
            optionsDirty = true;
            UpdateOptionControls(SelectedJob());
        }

        protected override void OnBind(GameClass game)
        {
            character = game.character;
            if (character != null) character.Jobs_Refresh += OnJobsChanged;
            actions = game.Interactions?.Jobs;
            if (actions != null) actions.Changed += OnJobsChanged;
        }

        protected override void OnUnbind(GameClass game)
        {
            if (character != null) character.Jobs_Refresh -= OnJobsChanged;
            character = null;
            if (actions != null) actions.Changed -= OnJobsChanged;
            actions = null;
            optionsJob = -1; optionsDirty = false; recipesKey = string.Empty; shownRecipes = new Recipe[0];
            Interlocked.Increment(ref recipesRequest);
        }

        private void OnJobsChanged() => OnUi(RefreshView);

        private Jobs SelectedJob()
        {
            int id = SelectedJobId;
            return id == 0 ? null : Game?.character?.GetJobsSnapshot().FirstOrDefault(job => job != null && job.ID == id);
        }

        public override void RefreshView()
        {
            if (Game == null || strip == null || IsDisposed) return;
            Jobs[] jobs = Game.character?.GetJobsSnapshot().Where(entry => entry != null).ToArray() ?? new Jobs[0];
            refreshing = true;
            try { strip.SetJobs(jobs); }
            finally { refreshing = false; }
            Jobs job = SelectedJob();
            panelState.Visible = jobs.Length == 0;
            if (job == null)
            {
                jobName.Text = Text("MY_JOBS", "Mes métiers");
                jobLevel.Text = "Aucun métier sélectionné.";
                gauge.Set(0, string.Empty);
                jobTool.Text = string.Empty;
            }
            else
            {
                jobName.Text = JobDisplayName(job);
                string xp = job.Level >= 100 || job.NextXP <= job.BaseXP
                    ? job.ActualXP.ToString("N0", CultureInfo.CurrentCulture) + " xp"
                    : job.ActualXP.ToString("N0", CultureInfo.CurrentCulture) + " / " + job.NextXP.ToString("N0", CultureInfo.CurrentCulture) + " xp";
                jobLevel.Text = Text("LEVEL", "Niveau") + " " + job.Level.ToString(CultureInfo.CurrentCulture) + " · " + xp;
                gauge.Set(job.GetXpPercentage, job.GetXpPercentage.ToString("0.#", CultureInfo.CurrentCulture) + " %");
                int? tool = Game.character?.CurrentJobTool;
                jobTool.Text = Text("TOOL", "Outil") + " : " + (tool == null ? "aucun outil de métier équipé"
                    : tool.Value == job.ID ? "outil de ce métier équipé" : "outil de " + JobCatalog.JobName(tool.Value) + " équipé");
            }
            FillSkills(job);
            RequestRecipes(job);
            LoadOptions(job);
            UpdateOptionControls(job);
            UpdatePublicMode(jobs);
        }

        private static string JobDisplayName(Jobs job) => string.IsNullOrEmpty(job.name) ? JobCatalog.JobName(job.ID) : job.name;

        private void FillSkills(Jobs job)
        {
            skillList.BeginUpdate(); skillList.Items.Clear();
            if (job?.Skills != null)
                foreach (JobSkills skill in job.Skills.ToArray().Where(entry => entry != null).OrderBy(entry => entry.CanCraft).ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    var row = skillList.Items.Add(skill.Name);
                    row.SubItems.Add(skill.Describe());
                    row.Tag = skill.Id;
                    row.ToolTipText = skill.CanCraft ? "Atelier" : skill.Source;
                }
            skillList.EndUpdate();
        }

        /// <summary>Recettes du métier calculées sur le pool de threads (lecture des textes du client), puis affichées sur le thread de l'interface.</summary>
        private void RequestRecipes(Jobs job)
        {
            string key = job == null ? string.Empty : job.ID.ToString(CultureInfo.InvariantCulture) + ":"
                + string.Join(",", job.CraftSkills.Select(skill => skill.Id.ToString(CultureInfo.InvariantCulture) + "/" + skill.Slots.ToString(CultureInfo.InvariantCulture)));
            if (key == recipesKey) return;
            recipesKey = key;
            int request = Interlocked.Increment(ref recipesRequest);
            shownRecipes = new Recipe[0];
            FillRecipes();
            if (job == null) { recipesState.Text = string.Empty; return; }
            if (!job.IsCraftJob) { recipesState.Text = "Métier de récolte : aucune recette d'atelier."; return; }
            recipesState.Text = "Lecture des recettes…";
            Task.Run(() => job.Recipes).ContinueWith(task =>
            {
                IReadOnlyList<Recipe> result = task.Status == TaskStatus.RanToCompletion ? task.Result : new Recipe[0];
                if (task.IsFaulted) Account?.Logger?.LogError("MÉTIERS", "Recettes illisibles : " + task.Exception?.GetBaseException().Message);
                OnUi(() =>
                {
                    if (request != Volatile.Read(ref recipesRequest)) return;
                    shownRecipes = result;
                    FillRecipes();
                    recipesState.Text = result.Count == 0 ? Text("NO_CRAFT_AVAILABLE", "Aucune recette connue (textes du client absents ?).")
                        : result.Count.ToString(CultureInfo.CurrentCulture) + " recette(s) connue(s) d'après les textes du client.";
                });
            }, TaskScheduler.Default);
        }

        private void FillRecipes()
        {
            recipeList.BeginUpdate(); recipeList.Items.Clear();
            foreach (Recipe recipe in shownRecipes)
            {
                var row = recipeList.Items.Add(recipe.Name);
                row.SubItems.Add(recipe.ItemsCount.ToString(CultureInfo.CurrentCulture));
                row.Tag = recipe;
            }
            recipeList.EndUpdate();
            ShowRecipeDetail();
        }

        private void ShowRecipeDetail()
        {
            if (recipeDetail == null) return;
            Recipe recipe = recipeList.SelectedItems.Count == 1 ? recipeList.SelectedItems[0].Tag as Recipe : null;
            recipeDetail.Text = recipe == null ? "Choisissez une recette pour voir ses ingrédients."
                : recipe.Name + " : " + string.Join(", ", recipe.Ingredients.Select(ingredient => ingredient.ToString())) + ".";
        }

        private void LoadOptions(Jobs job)
        {
            int id = job?.ID ?? 0;
            if (optionsDirty && id == optionsJob) return;
            optionsJob = id;
            optionsDirty = false;
            JobOptions options = job?.Options ?? JobOptions.Default;
            loadingOptions = true;
            try
            {
                notFree.Checked = options.IsNotFree;
                freeIfFailed.Checked = options.IsFreeIfFailed;
                resourcesNeeded.Checked = options.ResourcesNeeded;
                int max = Math.Max(2, Math.Min(8, job?.MaxSkillSlots ?? 2));
                minSlots.Maximum = max;
                minSlots.Value = Math.Max(2, Math.Min(max, options.MinSlots));
            }
            finally { loadingOptions = false; }
        }

        private void UpdateOptionControls(Jobs job)
        {
            bool craft = job != null && job.IsCraftJob;
            bool enabled = craft && Connected;
            notFree.Enabled = enabled;
            freeIfFailed.Enabled = enabled && notFree.Checked;
            resourcesNeeded.Enabled = enabled;
            int max = Math.Min(8, job?.MaxSkillSlots ?? 0);
            minSlotsLabel.Visible = minSlots.Visible = craft && max > 2;
            minSlots.Enabled = enabled;
            saveOptions.Enabled = enabled;
            if (job == null) optionsState.Text = "Choisissez un métier.";
            else if (!craft) optionsState.Text = "Métier de récolte : pas d'options d'artisan.";
            else optionsState.Text = Text("JOB_OPTIONS", "Options du métier") + " « " + JobDisplayName(job) + " »" + (optionsDirty ? " (modifiées, non sauvegardées)" : string.Empty);
        }

        private void UpdatePublicMode(Jobs[] jobs)
        {
            bool enabled = actions?.PublicMode == true;
            publicState.Text = Text("PUBLIC_MODE", "Mode public") + " : " + (enabled ? Text("ACTIVE", "Actif") : Text("INACTIVE", "Inactif"));
            publicState.ForeColor = enabled ? BotUi.Olive : BotUi.Ink;
            publicToggle.Text = enabled ? Text("DISABLE", "Désactiver") : Text("ENABLE", "Activer");
            publicToggle.Enabled = Connected && jobs.Length > 0;
            int[] referenced = actions?.ReferencedJobs.ToArray() ?? new int[0];
            referenceState.Text = referenced.Length == 0 ? "Non référencé dans le livre des artisans."
                : Text("CRAFTERS_LIST", "Livre des artisans") + " : " + string.Join(", ", referenced.Select(JobCatalog.JobName)) + ".";
        }

        private Task SaveOptions()
        {
            Jobs job = SelectedJob();
            JobsActions jobs = Game?.Interactions?.Jobs;
            if (job == null || jobs == null) return Task.CompletedTask;
            int flags = JobOptions.Compose(notFree.Checked, notFree.Checked && freeIfFailed.Checked, resourcesNeeded.Checked);
            int slots = (int)minSlots.Value;
            optionsDirty = false;
            return ReportAsync(() => jobs.SetOptionsAsync(job.ID, flags, slots));
        }

        private Task TogglePublicMode()
        {
            JobsActions jobs = Game?.Interactions?.Jobs;
            return jobs == null ? Task.CompletedTask : ReportAsync(() => jobs.SetPublicModeAsync(!jobs.PublicMode));
        }
    }

    /// <summary>Icônes des métiers (<c>Jobs/&lt;g&gt;.png</c>), lues sur le pool de threads ; <see cref="Loaded"/> prévient les contrôles qui les dessinent.</summary>
    internal static class JobIcons
    {
        private static readonly HashSet<string> requested = new HashSet<string>(StringComparer.Ordinal);
        private static readonly object sync = new object();

        /// <summary>Levé (pool de threads) quand une icône vient d'être lue.</summary>
        internal static event Action Loaded;

        /// <summary>Icône du métier si elle est déjà lue ; sinon demande sa lecture et renvoie <c>null</c>.</summary>
        internal static Image TryGet(int jobId)
        {
            int? icon = JobCatalog.JobIcon(jobId);
            if (icon == null) return null;
            string name = icon.Value.ToString(CultureInfo.InvariantCulture);
            if (ClientAssets.TryCached("Jobs", name, out Bitmap image)) return image;
            lock (sync) if (!requested.Add(name)) return null;
            ClientAssets.GetAsync("Jobs", name).ContinueWith(task =>
            {
                if (task.Status != TaskStatus.RanToCompletion || task.Result == null) return;
                Action handlers = Loaded;
                if (handlers == null) return;
                foreach (Action handler in handlers.GetInvocationList())
                {
                    try { handler(); }
                    catch (Exception) { /* Un contrôle déjà libéré ne doit pas priver les autres de l'icône. */ }
                }
            }, TaskScheduler.Default);
            return null;
        }
    }

    /// <summary>
    /// Cases des métiers (trois métiers puis trois spécialisations, comme <c>StatsJob</c>) : icône du métier dans une case du client,
    /// niveau en pastille, surbrillance sur le métier choisi. Les métiers supplémentaires s'ajoutent après.
    /// </summary>
    public sealed class JobStrip : Control
    {
        public const int CellSize = 44;
        private const int Gap = 6, GroupGap = 16, CaptionHeight = 16;
        private readonly ToolTip tips = new ToolTip();
        private Jobs[] slots = new Jobs[6];
        private int selected;
        private int tipIndex = -1;

        public JobStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint | ControlStyles.Selectable, true);
            BackColor = BotUi.Paper; ForeColor = BotUi.Ink; Height = CaptionHeight + CellSize + 8; Cursor = Cursors.Hand;
            JobIcons.Loaded += OnIconLoaded;
        }

        public event EventHandler SelectionChanged;

        /// <summary>Métiers dans l'ordre des cases (cases vides comprises : <c>null</c>).</summary>
        public IReadOnlyList<Jobs> Slots => slots;
        /// <summary>Métier choisi, 0 sans métier.</summary>
        public int SelectedJobId => selected;

        /// <summary>Remplace les métiers ; le choix est gardé s'il existe encore, sinon le premier métier est choisi.</summary>
        public void SetJobs(IEnumerable<Jobs> jobs)
        {
            Jobs[] all = (jobs ?? Enumerable.Empty<Jobs>()).Where(job => job != null).ToArray();
            Jobs[] basic = all.Where(job => JobCatalog.SpecializationOf(job.ID) <= 0).ToArray();
            Jobs[] special = all.Where(job => JobCatalog.SpecializationOf(job.ID) > 0).ToArray();
            var cells = new List<Jobs>();
            for (int index = 0; index < 3; index++) cells.Add(index < basic.Length ? basic[index] : null);
            for (int index = 0; index < 3; index++) cells.Add(index < special.Length ? special[index] : null);
            cells.AddRange(basic.Skip(3)); cells.AddRange(special.Skip(3));
            slots = cells.ToArray();
            int previous = selected;
            if (!all.Any(job => job.ID == selected)) selected = all.Length == 0 ? 0 : (basic.FirstOrDefault() ?? all[0]).ID;
            tipIndex = -1;
            Invalidate();
            if (previous != selected) SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Choisit le métier (clic, clavier, tests) ; faux s'il n'a pas de case.</summary>
        public bool SelectJob(int jobId)
        {
            if (jobId <= 0 || !slots.Any(job => job != null && job.ID == jobId)) return false;
            if (selected != jobId) { selected = jobId; Invalidate(); SelectionChanged?.Invoke(this, EventArgs.Empty); }
            return true;
        }

        /// <summary>Rectangle de la case <paramref name="index"/>.</summary>
        public Rectangle CellBounds(int index)
        {
            int x = Gap + index * (CellSize + Gap) + (index >= 3 ? GroupGap : 0) + (index >= 6 ? GroupGap : 0);
            return new Rectangle(x, CaptionHeight, CellSize, CellSize);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.Clear(BackColor);
            TextFormatFlags caption = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
            Rectangle first = CellBounds(0), fourth = CellBounds(3);
            TextRenderer.DrawText(graphics, "Métiers", BotFonts.Get(7.5f, FontStyle.Bold), new Rectangle(first.X, 0, fourth.X - first.X, CaptionHeight), BotUi.Muted, caption);
            TextRenderer.DrawText(graphics, "Spécialisations", BotFonts.Get(7.5f, FontStyle.Bold), new Rectangle(fourth.X, 0, Math.Max(0, Width - fourth.X), CaptionHeight), BotUi.Muted, caption);
            Image cell = ClientAssets.Get("case-inventaire"), glow = ClientAssets.Get("case-surbrillance");
            for (int index = 0; index < slots.Length; index++)
            {
                Rectangle bounds = CellBounds(index);
                if (bounds.Left >= Width) break;
                Jobs job = slots[index];
                if (cell != null) graphics.DrawImage(cell, bounds);
                else
                {
                    using (var fill = new SolidBrush(BotUi.PaperLight)) graphics.FillRectangle(fill, bounds);
                    using (var pen = new Pen(BotUi.Gold)) graphics.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
                }
                if (job == null) continue;
                Image icon = JobIcons.TryGet(job.ID);
                if (icon != null) ClientAssets.DrawFit(graphics, icon, Rectangle.Inflate(bounds, -5, -5));
                else TextRenderer.DrawText(graphics, Initials(JobCatalog.JobName(job.ID)), BotFonts.Get(8, FontStyle.Bold), bounds, BotUi.Ink,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                DrawLevel(graphics, bounds, job.Level);
                if (job.ID == selected)
                {
                    if (glow != null) graphics.DrawImage(glow, Rectangle.Inflate(bounds, 1, 1));
                    else using (var pen = new Pen(BotUi.Olive, 2)) graphics.DrawRectangle(pen, Rectangle.Inflate(bounds, -1, -1));
                }
            }
        }

        private static void DrawLevel(Graphics graphics, Rectangle bounds, int level)
        {
            string text = level.ToString(CultureInfo.InvariantCulture);
            Font font = BotFonts.Get(7, FontStyle.Bold);
            Size size = TextRenderer.MeasureText(graphics, text, font, Size.Empty, TextFormatFlags.NoPadding);
            var badge = new Rectangle(bounds.Right - size.Width - 5, bounds.Bottom - size.Height - 3, size.Width + 4, size.Height + 1);
            using (var fill = new SolidBrush(Color.FromArgb(200, BotUi.Frame))) graphics.FillRectangle(fill, badge);
            TextRenderer.DrawText(graphics, text, font, badge, BotUi.PaperLight,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        private static string Initials(string name)
        {
            string[] words = (name ?? string.Empty).Split(new[] { ' ', '\'', '-' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return "?";
            return (words.Length == 1 ? words[0].Substring(0, Math.Min(2, words[0].Length)) : words[0].Substring(0, 1) + words[1].Substring(0, 1)).ToUpper(CultureInfo.CurrentCulture);
        }

        private int IndexAt(Point point)
        {
            for (int index = 0; index < slots.Length; index++)
                if (CellBounds(index).Contains(point)) return index;
            return -1;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            int index = IndexAt(e.Location);
            if (index >= 0 && slots[index] != null) SelectJob(slots[index].ID);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = IndexAt(e.Location);
            if (index == tipIndex) return;
            tipIndex = index;
            Jobs job = index < 0 ? null : slots[index];
            tips.SetToolTip(this, job == null ? null : JobCatalog.JobName(job.ID) + " (" + job.Level.ToString(CultureInfo.CurrentCulture) + ")");
        }

        protected override bool IsInputKey(Keys keyData) => keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int step = e.KeyCode == Keys.Left ? -1 : e.KeyCode == Keys.Right ? 1 : 0;
            if (step == 0) return;
            Jobs[] jobs = slots.Where(job => job != null).ToArray();
            if (jobs.Length == 0) return;
            int index = Array.FindIndex(jobs, job => job.ID == selected);
            index = Math.Max(0, Math.Min(jobs.Length - 1, index < 0 ? 0 : index + step));
            SelectJob(jobs[index].ID);
            e.Handled = true;
        }

        private void OnIconLoaded()
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke((Action)(() => { if (!IsDisposed) Invalidate(); })); }
            catch (InvalidOperationException) { /* Poignée détruite entre-temps. */ }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { JobIcons.Loaded -= OnIconLoaded; tips.Dispose(); }
            base.Dispose(disposing);
        }
    }

    /// <summary>Jauge d'expérience du métier : parchemin clair, remplissage olive, filet doré, pourcentage au centre.</summary>
    public sealed class XpGauge : Control
    {
        private double percent;
        private string caption = string.Empty;

        public XpGauge()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = BotUi.Paper;
        }

        /// <summary>Pourcentage affiché (0 à 100).</summary>
        public double Percent => percent;

        public void Set(double value, string text)
        {
            double clamped = double.IsNaN(value) ? 0 : Math.Max(0, Math.Min(100, value));
            if (clamped == percent && caption == (text ?? string.Empty)) return;
            percent = clamped; caption = text ?? string.Empty;
            AccessibleDescription = caption;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.Clear(BackColor);
            var bar = new Rectangle(0, 1, Math.Max(1, Width - 1), Math.Max(1, Height - 3));
            using (var fill = new SolidBrush(BotUi.PaperLight)) graphics.FillRectangle(fill, bar);
            int filled = (int)Math.Round((bar.Width - 1) * percent / 100.0);
            if (filled > 0) using (var olive = new SolidBrush(BotUi.Olive)) graphics.FillRectangle(olive, bar.X + 1, bar.Y + 1, filled - 1 > 0 ? filled - 1 : 1, bar.Height - 1);
            using (var pen = new Pen(BotUi.Gold)) graphics.DrawRectangle(pen, bar);
            if (caption.Length > 0)
                TextRenderer.DrawText(graphics, caption, BotFonts.Get(7, FontStyle.Bold), bar, percent >= 50 ? BotUi.PaperLight : BotUi.Ink,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>Onglets au style du client (onglet actif parchemin clair, inactifs brun clair, filets dorés), largeur partagée.</summary>
    public sealed class JobTabStrip : Control
    {
        public const int TabHeight = 24;
        private readonly string[] labels;
        private int selected;

        public JobTabStrip(params string[] tabs)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            labels = tabs == null || tabs.Length == 0 ? new[] { string.Empty } : tabs.ToArray();
            BackColor = BotUi.Paper; Height = TabHeight + 3; Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.PageTabList;
        }

        public event EventHandler<int> TabClicked;

        public IReadOnlyList<string> Labels => labels;

        public int Selected
        {
            get => selected;
            set { int next = Math.Max(0, Math.Min(labels.Length - 1, value)); if (selected == next) return; selected = next; Invalidate(); }
        }

        /// <summary>Change le libellé d'un onglet (compteur, état).</summary>
        public void SetLabel(int index, string text)
        {
            if (index < 0 || index >= labels.Length || labels[index] == (text ?? string.Empty)) return;
            labels[index] = text ?? string.Empty;
            Invalidate();
        }

        public Rectangle TabBounds(int index)
        {
            int width = Math.Max(30, (Width - 3 * (labels.Length - 1)) / labels.Length);
            return new Rectangle(index * (width + 3), 0, width, TabHeight);
        }

        /// <summary>Simule un clic sur l'onglet (raccourci clavier, tests).</summary>
        public void ClickTab(int index) { if (index >= 0 && index < labels.Length) TabClicked?.Invoke(this, index); }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var paper = new SolidBrush(BackColor)) e.Graphics.FillRectangle(paper, ClientRectangle);
            using (var rule = new Pen(BotUi.Gold)) e.Graphics.DrawLine(rule, 0, TabHeight, Width, TabHeight);
            for (int index = 0; index < labels.Length; index++)
            {
                Rectangle area = TabBounds(index);
                bool active = index == selected;
                using (var fill = new SolidBrush(active ? BotUi.PaperLight : BotUi.FrameLight)) e.Graphics.FillRectangle(fill, area);
                using (var border = new Pen(BotUi.Gold)) e.Graphics.DrawRectangle(border, area.X, area.Y, area.Width - 1, area.Height - (active ? 0 : 1));
                TextRenderer.DrawText(e.Graphics, labels[index], BotFonts.Get(8, active ? FontStyle.Bold : FontStyle.Regular), area,
                    active ? BotUi.Ink : BotUi.PaperLight, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            for (int index = 0; index < labels.Length; index++)
                if (TabBounds(index).Contains(e.Location) && index != selected) { TabClicked?.Invoke(this, index); return; }
        }
    }
}
