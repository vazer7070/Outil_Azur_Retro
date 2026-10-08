using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using System.Threading;
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
    /// Atelier (interface <c>Craft</c> du client 1.34) : s'ouvre sur <c>ECK3|&lt;cases&gt;;&lt;compétence&gt;</c>, se ferme sur <c>EV</c>.
    /// Les cases de l'atelier montrent les ingrédients posés (<c>EMKO+</c>), la case de droite l'objet créé (<c>EmKO+</c>) ou la recette
    /// reconnue en transparence. Poser envoie <c>EMO+&lt;objet&gt;|&lt;quantité&gt;</c>, Retirer <c>EMO-</c>, Combiner <c>EK</c> (puis
    /// <c>EMR&lt;n-1&gt;</c> pour une série), Stop <c>EMr</c>, Mémoire <c>EL</c>. Sans recette reconnue, Combiner demande d'abord confirmation
    /// (<c>WRONG_CRAFT_CONFIRM</c>). Les recettes de la compétence sont lues hors du thread de l'interface. La forgemagie n'est pas assistée
    /// et l'artisanat sécurisé n'est pas proposé.
    /// </summary>
    public sealed class CraftPanel : GamePanel
    {
        public const int BagTab = 0, RecipesTab = 1;
        private const string IdleText = "Aucun atelier ouvert. Utilisez un atelier sur la carte : le volet s'ouvre lorsque le serveur l'annonce (ECK3).";
        private Label heading, subheading, expectedText, craftStatus, recipeDetail;
        private CraftSlotsView slotsView;
        private JobTabStrip tabs;
        private Panel bagPage, recipesPage;
        private ListView bagList, recipeList;
        private CheckBox usefulOnly;
        private NumericUpDown placeQuantity, repeatCount;
        private Control removeButton, combineButton, stopButton, replayButton, placeButton, placeRecipeButton, leaveButton;
        private CraftExchange bound;
        private InventoryClass inventory;
        private bool wasOpen, bagDirty = true, recipesDirty = true;
        private string recipesKey = string.Empty;
        private int recipesRequest;
        private bool recipesReady;
        private IReadOnlyList<Recipe> recipes = new Recipe[0];
        private HashSet<int> usefulTemplates = new HashSet<int>();
        private Recipe expectedRecipe;
        private Form combineDialog;

        public override string Title => "Atelier";
        public override Image Icon
        {
            get
            {
                // Icône du métier dès qu'elle est lue, sinon celle du volet des métiers (titre jamais sans icône).
                Jobs job = bound?.Job;
                return (job == null ? null : JobIcons.TryGet(job.ID)) ?? ClientAssets.Icon("icone-caracteristiques", 24);
            }
        }
        public override bool IsModal => true;
        public override bool IsServerWindowOpen => Game?.Interactions?.Jobs?.Craft?.IsOpen == true;

        /// <summary>Cases de l'atelier (tests, raccourcis).</summary>
        public CraftSlotsView SlotsView => slotsView;
        /// <summary>Recettes de la compétence de l'atelier qui tiennent dans ses cases, une fois lues.</summary>
        public IReadOnlyList<Recipe> KnownRecipes => recipes;
        /// <summary>Vrai quand les recettes de l'atelier ouvert ont été lues.</summary>
        public bool RecipesReady => recipesReady;
        /// <summary>Recette reconnue d'après les ingrédients posés, ou <c>null</c>.</summary>
        public Recipe ExpectedRecipe => expectedRecipe;
        /// <summary>Boîte <c>WRONG_CRAFT_CONFIRM</c> affichée, ou <c>null</c>.</summary>
        public Form CombineDialog => combineDialog;

        private static string Text(string key, string fallback, params string[] args) => JobCatalog.Text(key, fallback, args);

        protected override Control CreateView()
        {
            var page = Page();
            heading = MakeLabel("Atelier", 10, true); heading.Dock = DockStyle.Top; heading.Height = 21; heading.Name = "craft-heading";
            subheading = MakeLabel(string.Empty, 8); subheading.Dock = DockStyle.Top; subheading.Height = 18; subheading.ForeColor = BotUi.Muted;
            subheading.Name = "craft-job";
            slotsView = new CraftSlotsView { Dock = DockStyle.Top, Height = 2 * CraftSlotsView.CellSize + 3 * 4 + 2, Name = "craft-slots", AccessibleName = "Cases de l'atelier" };
            slotsView.SelectionChanged += (s, e) => UpdateButtons();
            slotsView.ItemActivated += async (s, e) => await RemoveSelected();
            expectedText = MakeLabel(string.Empty, 8.25f); expectedText.Dock = DockStyle.Top; expectedText.Height = 34; expectedText.Name = "craft-expected";
            expectedText.Padding = new Padding(0, 3, 0, 0);

            removeButton = MakeButton("Retirer", async (s, e) => await RemoveSelected(), false, 76); removeButton.Name = "craft-remove";
            combineButton = MakeButton(Text("COMBINE", "Combiner"), async (s, e) => await Combine(), true, 96); combineButton.Name = "craft-combine";
            var repeatLabel = InventoryPanel.QuantityLabel(); repeatLabel.Text = Text("QUANTITY_SMALL", "Qté"); repeatLabel.Width = 30;
            repeatCount = InventoryPanel.Quantity(); repeatCount.Width = 58; repeatCount.Name = "craft-repeat";
            stopButton = MakeButton(Text("STOP_WORD", "Stop"), async (s, e) => await StopRepeat(), false, 56); stopButton.Name = "craft-stop";
            replayButton = MakeButton("Mémoire", async (s, e) => await Replay(), false, 84); replayButton.Name = "craft-replay";
            var craftBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, MinimumSize = new Size(0, 40), WrapContents = true, Padding = new Padding(0, 3, 0, 3),
                BackColor = BotUi.Paper };
            craftBar.Controls.AddRange(new[] { combineButton, repeatLabel, repeatCount, stopButton, removeButton, replayButton });

            tabs = new JobTabStrip("Sac", Text("RECEIPTS", "Recettes")) { Dock = DockStyle.Top, Name = "craft-tabs" };
            tabs.TabClicked += (s, tab) => ShowTab(tab);
            var content = new Panel { Dock = DockStyle.Fill, BackColor = BotUi.Paper, Padding = new Padding(0, 3, 0, 0) };
            bagPage = BuildBagPage(); recipesPage = BuildRecipesPage(); recipesPage.Visible = false;
            content.Controls.Add(bagPage); content.Controls.Add(recipesPage);

            craftStatus = MakeStatus(IdleText); craftStatus.Name = "craft-status"; craftStatus.Height = 36;
            leaveButton = MakeButton("Fermer", async (s, e) => await Leave(), false, 100); leaveButton.Name = "craft-leave";

            // Ordre d'ancrage : le dernier ajouté prend le bord en premier.
            page.Controls.Add(content);
            page.Controls.Add(craftStatus); page.Controls.Add(BotUi.Actions(leaveButton));
            page.Controls.Add(tabs); page.Controls.Add(craftBar); page.Controls.Add(expectedText); page.Controls.Add(slotsView);
            page.Controls.Add(subheading); page.Controls.Add(heading);
            return page;
        }

        private Panel BuildBagPage()
        {
            var tabPage = new Panel { Dock = DockStyle.Fill, BackColor = BotUi.Paper };
            bagList = MakeList(8.25f, "Objet", "Qté");
            bagList.Name = "craft-bag"; bagList.Columns[0].Width = 300; bagList.Columns[1].Width = 60;
            bagList.SelectedIndexChanged += (s, e) => UpdateButtons();
            bagList.DoubleClick += async (s, e) => await PlaceSelected();
            // Texte propre au bot : CRAFT_SLOT_FILTER du client (« Affiche/Cache les recettes à %1 ingrédient(s) ») filtre les recettes, pas le sac.
            usefulOnly = new CheckBox { Name = "craft-useful", Text = "Ingrédients des recettes seulement", Checked = true, AutoSize = true,
                Dock = DockStyle.Top, Font = BotFonts.Get(8), ForeColor = BotUi.Ink, BackColor = BotUi.Paper, Padding = new Padding(0, 0, 0, 2) };
            usefulOnly.CheckedChanged += (s, e) => { bagDirty = true; RefreshView(); };
            placeQuantity = InventoryPanel.Quantity(); placeQuantity.Name = "craft-place-quantity";
            placeButton = MakeButton("Poser", async (s, e) => await PlaceSelected(), false, 90); placeButton.Name = "craft-place";
            tabPage.Controls.Add(bagList); tabPage.Controls.Add(usefulOnly);
            tabPage.Controls.Add(BotUi.Actions(placeButton, InventoryPanel.QuantityLabel(), placeQuantity));
            return tabPage;
        }

        private Panel BuildRecipesPage()
        {
            var tabPage = new Panel { Dock = DockStyle.Fill, BackColor = BotUi.Paper };
            recipeList = MakeList(8.25f, Text("CRAFTED_ITEM", "Objet"), "Ingr.", "Possible");
            recipeList.Name = "craft-recipes"; recipeList.Columns[0].Width = 240; recipeList.Columns[1].Width = 50; recipeList.Columns[2].Width = 70;
            recipeList.SelectedIndexChanged += (s, e) => { ShowRecipeDetail(); UpdateButtons(); };
            recipeList.DoubleClick += async (s, e) => await PlaceRecipe();
            recipeDetail = MakeLabel(string.Empty, 8); recipeDetail.Dock = DockStyle.Bottom; recipeDetail.Height = 40; recipeDetail.Name = "craft-recipe-detail";
            placeRecipeButton = MakeButton("Poser la recette", async (s, e) => await PlaceRecipe(), false, 150); placeRecipeButton.Name = "craft-place-recipe";
            tabPage.Controls.Add(recipeList); tabPage.Controls.Add(recipeDetail); tabPage.Controls.Add(BotUi.Actions(placeRecipeButton));
            return tabPage;
        }

        /// <summary>Affiche l'onglet du sac ou des recettes.</summary>
        public void ShowTab(int tab)
        {
            if (tabs == null || tab < BagTab || tab > RecipesTab) return;
            tabs.Selected = tab;
            bagPage.Visible = tab == BagTab;
            recipesPage.Visible = tab == RecipesTab;
            UpdateButtons();
        }

        protected override void OnBind(GameClass game)
        {
            bound = game.Interactions?.Jobs?.Craft;
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
            ForgetRecipes();
            CloseCombineDialog();
        }

        private void OnServerChanged() => Post(() =>
        {
            bool open = IsServerWindowOpen;
            if (open && !wasOpen) { bagDirty = true; RequestShow(); }
            else if (!open && wasOpen) { CloseCombineDialog(); RaiseClosed(); }
            wasOpen = open;
            bagDirty = true;
            RefreshView();
        });

        private void OnInventoryChanged(bool changed) { if (changed) Post(() => { bagDirty = true; recipesDirty = true; RefreshView(); }); }

        /// <summary>Thread de l'interface par la fenêtre de jeu (voir <see cref="ExchangePanel.PostToForm"/>).</summary>
        private bool Post(Action action) => ExchangePanel.PostToForm(Host, () => { if (!IsDisposed) action(); });

        private void ForgetRecipes()
        {
            Interlocked.Increment(ref recipesRequest);
            recipesKey = string.Empty; recipesReady = false; recipes = new Recipe[0]; usefulTemplates = new HashSet<int>(); expectedRecipe = null;
            recipesDirty = true;
        }

        /// <summary>Lit sur le pool de threads les recettes de la compétence ouverte (une fois par compétence et nombre de cases).</summary>
        private void RequestRecipes(CraftExchange craft)
        {
            string key = craft.IsOpen && craft.SkillId > 0 ? craft.SkillId.ToString(CultureInfo.InvariantCulture) + "/" + craft.Slots.ToString(CultureInfo.InvariantCulture) : string.Empty;
            if (key == recipesKey) return;
            ForgetRecipes();
            recipesKey = key;
            if (key.Length == 0) return;
            int request = Volatile.Read(ref recipesRequest);
            short skill = craft.SkillId;
            int slots = craft.Slots > 0 ? craft.Slots : int.MaxValue;
            Task.Run(() =>
            {
                IReadOnlyList<Recipe> read = JobCatalog.Recipes(skill, slots);
                var useful = new HashSet<int>(read.SelectMany(recipe => recipe.Ingredients).Select(ingredient => ingredient.TemplateId)) { JobCatalog.SigningRune };
                return Tuple.Create(read, useful);
            }).ContinueWith(task =>
            {
                if (task.IsFaulted) Account?.Logger?.LogError("ATELIER", "Recettes illisibles : " + task.Exception?.GetBaseException().Message);
                Tuple<IReadOnlyList<Recipe>, HashSet<int>> result = task.Status == TaskStatus.RanToCompletion ? task.Result
                    : Tuple.Create((IReadOnlyList<Recipe>)new Recipe[0], new HashSet<int>());
                Post(() =>
                {
                    if (request != Volatile.Read(ref recipesRequest)) return;
                    recipes = result.Item1; usefulTemplates = result.Item2; recipesReady = true;
                    recipesDirty = true; bagDirty = true;
                    RefreshView();
                });
            }, TaskScheduler.Default);
        }

        public override void RefreshView()
        {
            CraftExchange craft = Game?.Interactions?.Jobs?.Craft;
            if (craft == null || slotsView == null || IsDisposed) return;
            RequestRecipes(craft);
            IReadOnlyList<ExchangeItem> ingredients = craft.Ingredients;
            expectedRecipe = recipesReady && !craft.IsForgemagus ? JobCatalog.Match(craft.PlacedByTemplate(), recipes) : null;
            slotsView.SetState(craft.IsOpen ? craft.Slots : 0, craft.IsOpen ? ingredients : new ExchangeItem[0], craft.IsOpen ? craft.Result : null,
                craft.LastOutcome, expectedRecipe?.ResultTemplateId ?? 0);

            Jobs job = craft.IsOpen ? craft.Job : null;
            if (!craft.IsOpen)
            {
                heading.Text = "Atelier";
                subheading.Text = string.Empty;
                expectedText.Text = string.Empty;
            }
            else
            {
                heading.Text = (craft.IsForgemagus ? "Forgemagie" : "Atelier") + " : " + craft.SkillName
                    + (craft.Slots > 0 ? " · " + craft.Slots.ToString(CultureInfo.CurrentCulture) + " " + JobCatalog.SlotWord(craft.Slots) : string.Empty);
                JobSkills skill = job?.Skills?.ToArray().FirstOrDefault(entry => entry != null && entry.Id == craft.SkillId);
                subheading.Text = job == null ? "Compétence absente des métiers connus du personnage."
                    : Text("JOB", "Métier") + " : " + job + (skill != null && skill.Chance > 0 ? " · " + skill.Chance.ToString(CultureInfo.CurrentCulture) + " % de réussite" : string.Empty);
                expectedText.Text = ExpectedText(craft, ingredients, job);
            }
            if (bagDirty) FillBag(craft);
            if (recipesDirty) FillRecipes();
            int max = craft.IsOpen ? craft.MaxRepeat : 0;
            repeatCount.Maximum = Math.Max(1, max);
            if (repeatCount.Value > repeatCount.Maximum) repeatCount.Value = repeatCount.Maximum;
            craftStatus.Text = StatusText(craft);
            UpdateButtons();
        }

        private string ExpectedText(CraftExchange craft, IReadOnlyList<ExchangeItem> ingredients, Jobs job)
        {
            if (craft.IsForgemagus) return "Forgemagie : posez l'objet et les runes, le serveur calcule le résultat (le bot ne l'assiste pas).";
            if (ingredients.Count == 0) return "Posez les ingrédients d'une recette (onglet Sac ou Recettes).";
            if (!recipesReady) return "Lecture des recettes…";
            if (expectedRecipe != null)
                return "Recette reconnue : " + expectedRecipe.Name + (job != null && !job.CanCraft(craft.SkillId, expectedRecipe.ResultTemplateId)
                    ? " (le serveur peut la refuser à ce niveau)." : ".");
            return recipes.Count == 0 ? "Aucune recette connue pour cette compétence (textes du client absents ?)."
                : "Aucune recette connue ne correspond à ces ingrédients.";
        }

        private static string StatusText(CraftExchange craft)
        {
            string message = craft.LastMessage ?? string.Empty;
            if (!craft.IsOpen) return IdleText + (message.Length > 0 ? " " + message : string.Empty);
            if (craft.IsLooping)
            {
                int total = craft.LoopTotal + 1, remaining = craft.LoopRemaining;
                string progress = remaining < 0 ? "Série de " + total.ToString(CultureInfo.CurrentCulture) + " objets demandée."
                    : "Série : objet " + (total - remaining).ToString(CultureInfo.CurrentCulture) + " sur " + total.ToString(CultureInfo.CurrentCulture) + ".";
                return progress + (message.Length > 0 ? " " + message : string.Empty);
            }
            if (craft.IsCombinePending) return "Fabrication en cours…";
            return message.Length > 0 ? message : "Atelier ouvert : les ingrédients suivent les réponses du serveur (EMKO).";
        }

        private void FillBag(CraftExchange craft)
        {
            bagDirty = false;
            uint selected = bagList.SelectedItems.Count == 0 ? 0u : (uint)bagList.SelectedItems[0].Tag;
            bagList.BeginUpdate(); bagList.Items.Clear();
            InventoryObjects[] bag = Game?.character?.Inventory?.Objets?.ToArray() ?? new InventoryObjects[0];
            bool filter = usefulOnly.Checked && recipesReady && recipes.Count > 0 && !craft.IsForgemagus;
            foreach (InventoryObjects item in bag.Where(entry => entry != null && !entry.IsEquipped()).OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                if (filter && !usefulTemplates.Contains(item.ID)) continue;
                int available = item.Qua - (craft.IsOpen ? craft.PlacedQuantity(item.Inventory_ID) : 0);
                if (available <= 0) continue;
                var row = bagList.Items.Add(item.Name); row.SubItems.Add(available.ToString(CultureInfo.CurrentCulture)); row.Tag = item.Inventory_ID;
                if (item.Inventory_ID == selected) row.Selected = true;
            }
            bagList.EndUpdate();
        }

        private void FillRecipes()
        {
            recipesDirty = false;
            int selected = recipeList.SelectedItems.Count == 1 && recipeList.SelectedItems[0].Tag is Recipe current ? current.ResultTemplateId : 0;
            Dictionary<int, int> owned = OwnedByTemplate();
            recipeList.BeginUpdate(); recipeList.Items.Clear();
            foreach (Recipe recipe in recipes)
            {
                int possible = Possible(recipe, owned);
                var row = recipeList.Items.Add(recipe.Name);
                row.SubItems.Add(recipe.ItemsCount.ToString(CultureInfo.CurrentCulture));
                row.SubItems.Add(possible > 0 ? "× " + possible.ToString(CultureInfo.CurrentCulture) : "—");
                row.ForeColor = possible > 0 ? BotUi.Olive : BotUi.Ink;
                row.Tag = recipe;
                if (recipe.ResultTemplateId == selected) row.Selected = true;
            }
            recipeList.EndUpdate();
            tabs.SetLabel(RecipesTab, Text("RECEIPTS", "Recettes") + " (" + recipes.Count.ToString(CultureInfo.CurrentCulture) + ")");
            ShowRecipeDetail();
        }

        /// <summary>Quantités du sac (objets non équipés) par modèle.</summary>
        private Dictionary<int, int> OwnedByTemplate()
        {
            var owned = new Dictionary<int, int>();
            foreach (InventoryObjects item in Game?.character?.Inventory?.Objets?.ToArray() ?? new InventoryObjects[0])
            {
                if (item == null || item.IsEquipped() || item.Qua <= 0) continue;
                owned.TryGetValue(item.ID, out int quantity);
                owned[item.ID] = quantity + item.Qua;
            }
            return owned;
        }

        private static int Possible(Recipe recipe, Dictionary<int, int> owned)
        {
            int best = int.MaxValue;
            foreach (RecipeIngredient ingredient in recipe.Ingredients)
            {
                if (ingredient.Quantity <= 0) continue;
                owned.TryGetValue(ingredient.TemplateId, out int quantity);
                best = Math.Min(best, quantity / ingredient.Quantity);
            }
            return best == int.MaxValue ? 0 : best;
        }

        private Recipe SelectedRecipe() => recipeList != null && recipeList.SelectedItems.Count == 1 ? recipeList.SelectedItems[0].Tag as Recipe : null;

        private void ShowRecipeDetail()
        {
            Recipe recipe = SelectedRecipe();
            recipeDetail.Text = recipe == null ? "Choisissez une recette : « Poser la recette » pose ses ingrédients depuis le sac."
                : string.Join(", ", recipe.Ingredients.Select(ingredient => ingredient.ToString())) + ".";
        }

        private void UpdateButtons()
        {
            CraftExchange craft = Game?.Interactions?.Jobs?.Craft;
            if (craft == null || leaveButton == null || IsDisposed) return;
            bool open = Connected && craft.IsOpen;
            bool editable = open && !craft.IsLooping && !craft.IsCombinePending;
            int placed = slotsView.Items.Count;
            ExchangeItem ingredient = slotsView.SelectedItem;
            InventoryObjects item = InventoryPanel.SelectedItem(Game, bagList);
            if (item != null) placeQuantity.Maximum = Math.Max(1, item.Qua - craft.PlacedQuantity(item.Inventory_ID));
            removeButton.Enabled = editable && ingredient != null;
            placeButton.Enabled = editable && item != null;
            placeRecipeButton.Enabled = editable && placed == 0 && SelectedRecipe() != null;
            combineButton.Enabled = editable && placed > 0;
            repeatCount.Enabled = editable && placed > 0 && repeatCount.Maximum > 1;
            stopButton.Enabled = open && craft.IsLooping;
            replayButton.Enabled = editable && placed == 0;
            leaveButton.Enabled = open;
        }

        protected internal override bool OnUserClose()
        {
            if (!IsServerWindowOpen) return true;
            _ = Leave();
            return false;
        }

        private Task RemoveSelected()
        {
            CraftExchange craft = Game?.Interactions?.Jobs?.Craft;
            ExchangeItem ingredient = slotsView?.SelectedItem;
            if (craft == null || ingredient == null) return Task.CompletedTask;
            return ReportAsync(() => craft.RemoveIngredientAsync(ingredient.Id, ingredient.Quantity));
        }

        private Task PlaceSelected()
        {
            CraftExchange craft = Game?.Interactions?.Jobs?.Craft;
            InventoryObjects item = InventoryPanel.SelectedItem(Game, bagList);
            if (craft == null || item == null) return Task.CompletedTask;
            int available = item.Qua - craft.PlacedQuantity(item.Inventory_ID);
            int quantity = Math.Max(1, Math.Min((int)placeQuantity.Value, available));
            return ReportAsync(() => craft.AddIngredientAsync(item.Inventory_ID, quantity));
        }

        /// <summary>
        /// Pose les ingrédients de la recette choisie depuis le sac (piles non équipées, les plus grosses d'abord) : rien n'est envoyé s'il en manque
        /// ou si l'atelier n'a pas assez de cases ; sinon un <c>EMO+</c> par pile, l'un après l'autre.
        /// </summary>
        private async Task PlaceRecipe()
        {
            try
            {
                CraftExchange craft = Game?.Interactions?.Jobs?.Craft;
                Recipe recipe = SelectedRecipe();
                if (craft == null || recipe == null || !craft.IsOpen) return;
                if (craft.Ingredients.Count > 0) { Feedback("Retirez d'abord les ingrédients posés."); return; }
                InventoryObjects[] bag = Game.character?.Inventory?.Objets?.ToArray() ?? new InventoryObjects[0];
                var plan = new List<Tuple<uint, int>>();
                var missing = new List<string>();
                foreach (RecipeIngredient ingredient in recipe.Ingredients)
                {
                    int needed = ingredient.Quantity;
                    foreach (InventoryObjects stack in bag.Where(entry => entry != null && !entry.IsEquipped() && entry.ID == ingredient.TemplateId && entry.Qua > 0)
                        .OrderByDescending(entry => entry.Qua))
                    {
                        if (needed <= 0) break;
                        int take = Math.Min(needed, stack.Qua);
                        plan.Add(Tuple.Create(stack.Inventory_ID, take));
                        needed -= take;
                    }
                    if (needed > 0) missing.Add(needed.ToString(CultureInfo.CurrentCulture) + " × " + ingredient.Name);
                }
                if (missing.Count > 0) { Feedback(Text("CRAFT_NO_RESOURCE", "Ressources insuffisantes") + " : il manque " + string.Join(", ", missing) + "."); return; }
                if (craft.Slots > 0 && plan.Count > craft.Slots)
                {
                    Feedback(Text("NOT_ENOUGHT_CRAFT_SLOT", "Pas assez de cases dans cet atelier") + " (" + plan.Count + " / " + craft.Slots + ").");
                    return;
                }
                foreach (Tuple<uint, int> step in plan)
                {
                    InteractionResult result = await craft.AddIngredientAsync(step.Item1, step.Item2);
                    if (!result.Sent) { Feedback(result.Message); return; }
                }
                Feedback("Ingrédients de « " + recipe.Name + " » posés.");
            }
            catch (Exception error) { Account?.Logger?.LogException("ATELIER", error); }
        }

        /// <summary>« Combiner » : confirmation <c>WRONG_CRAFT_CONFIRM</c> si aucune recette connue ne correspond, puis <c>EK</c> (et <c>EMR</c>).</summary>
        private async Task Combine()
        {
            try
            {
                CraftExchange craft = Game?.Interactions?.Jobs?.Craft;
                if (craft == null || !craft.IsOpen) return;
                int count = (int)repeatCount.Value;
                if (!craft.IsForgemagus && recipesReady && recipes.Count > 0 && expectedRecipe == null)
                {
                    CloseCombineDialog();
                    Form[] before = BotDialogs.OpenDialogs.ToArray();
                    Task<BotDialogResult> question = BotDialogs.AskYesNoAsync(Host, Text("CRAFT", "Artisanat"),
                        Text("WRONG_CRAFT_CONFIRM", "Ces ingrédients ne correspondent à aucune recette connue. Combiner quand même ?"));
                    combineDialog = BotDialogs.OpenDialogs.Except(before).LastOrDefault();
                    BotDialogResult answer = await question;
                    combineDialog = null;
                    if (answer != BotDialogResult.Yes) { Feedback("Fabrication abandonnée : rien n'a été envoyé."); return; }
                    if (!craft.IsOpen) return;
                }
                if (IsDisposed) await craft.CombineAsync(count); else await ReportAsync(() => craft.CombineAsync(count));
            }
            catch (Exception error) { Account?.Logger?.LogException("ATELIER", error); }
        }

        private Task StopRepeat()
        {
            CraftExchange craft = Game?.Interactions?.Jobs?.Craft;
            return craft == null ? Task.CompletedTask : ReportAsync(craft.StopRepeatAsync);
        }

        private Task Replay()
        {
            CraftExchange craft = Game?.Interactions?.Jobs?.Craft;
            return craft == null ? Task.CompletedTask : ReportAsync(craft.ReplayAsync);
        }

        private Task Leave()
        {
            CraftExchange craft = Game?.Interactions?.Jobs?.Craft;
            return craft == null ? Task.CompletedTask : ReportAsync(craft.LeaveAsync);
        }

        private void CloseCombineDialog()
        {
            Form dialog = combineDialog;
            combineDialog = null;
            if (dialog != null && !dialog.IsDisposed) dialog.Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) CloseCombineDialog();
        }
    }

    /// <summary>
    /// Cases de l'atelier au style du client : autant de cases <c>case-inventaire</c> que l'atelier en a (<c>ECK3|&lt;cases&gt;;…</c>), ingrédients
    /// posés avec leur icône et leur quantité, flèche puis case du résultat (objet créé, recette reconnue en transparence, croix après un échec).
    /// Double-clic sur un ingrédient : retrait.
    /// </summary>
    public sealed class CraftSlotsView : Control
    {
        public const int CellSize = 40;
        private const int Gap = 4, ArrowWidth = 24;
        private readonly ToolTip tips = new ToolTip();
        private ExchangeItem[] items = new ExchangeItem[0];
        private ExchangeItem result;
        private CraftOutcome outcome;
        private int slotCount, previewTemplate, tipIndex = -2;
        private uint? selectedId;

        public CraftSlotsView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint | ControlStyles.Selectable, true);
            BackColor = BotUi.Paper; ForeColor = BotUi.Ink;
            ExchangeItemIcons.Loaded += OnIconLoaded;
        }

        public event EventHandler SelectionChanged;
        /// <summary>Double-clic (ou Entrée) sur un ingrédient.</summary>
        public event EventHandler ItemActivated;

        /// <summary>Nombre de cases dessinées pour les ingrédients (cases de l'atelier, ou ingrédients s'il y en a plus).</summary>
        public int CellCount => Math.Max(slotCount, items.Length);
        /// <summary>Cases annoncées par le serveur.</summary>
        public int SlotCount => slotCount;
        public IReadOnlyList<ExchangeItem> Items => items;
        public ExchangeItem Result => result;
        public CraftOutcome Outcome => outcome;
        /// <summary>Modèle de la recette reconnue, montré en transparence tant qu'aucun objet n'est créé ; 0 sinon.</summary>
        public int PreviewTemplateId => previewTemplate;
        public ExchangeItem SelectedItem => selectedId == null ? null : items.FirstOrDefault(item => item.Id == selectedId.Value);

        public void SetState(int slots, IEnumerable<ExchangeItem> ingredients, ExchangeItem created, CraftOutcome last, int preview)
        {
            slotCount = Math.Max(0, slots);
            items = (ingredients ?? Enumerable.Empty<ExchangeItem>()).Where(item => item != null).ToArray();
            result = created; outcome = last; previewTemplate = preview;
            if (selectedId != null && SelectedItem == null) { selectedId = null; SelectionChanged?.Invoke(this, EventArgs.Empty); }
            tipIndex = -2;
            AccessibleDescription = items.Length.ToString(CultureInfo.InvariantCulture) + " / " + slotCount.ToString(CultureInfo.InvariantCulture);
            Invalidate();
        }

        /// <summary>Sélectionne l'ingrédient (clavier, tests) ; faux s'il n'est pas posé.</summary>
        public bool SelectItem(uint id)
        {
            if (!items.Any(item => item.Id == id)) return false;
            SetSelection(id);
            return true;
        }

        private void SetSelection(uint? id)
        {
            if (selectedId == id) return;
            selectedId = id;
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Taille des cases : 40 px, réduite si toutes les cases ne tiennent pas dans la hauteur du contrôle.</summary>
        private int CellSide()
        {
            int count = Math.Max(1, CellCount);
            for (int side = CellSize; side > 18; side -= 2)
            {
                int columns = Math.Max(1, (SlotsWidth - Gap) / (side + Gap));
                int rows = (count + columns - 1) / columns;
                if (rows * (side + Gap) + Gap <= ClientSize.Height) return side;
            }
            return 18;
        }

        private int SlotsWidth => Math.Max(CellSize + 2 * Gap, ClientSize.Width - CellSize - ArrowWidth - 3 * Gap);

        /// <summary>Rectangle de la case d'ingrédient <paramref name="index"/>.</summary>
        public Rectangle CellBounds(int index)
        {
            int side = CellSide();
            int columns = Math.Max(1, (SlotsWidth - Gap) / (side + Gap));
            return new Rectangle(Gap + index % columns * (side + Gap), Gap + index / columns * (side + Gap), side, side);
        }

        /// <summary>Rectangle de la case du résultat.</summary>
        public Rectangle ResultBounds => new Rectangle(ClientSize.Width - CellSize - Gap - 1, Math.Max(Gap, (ClientSize.Height - CellSize) / 2), CellSize, CellSize);

        private int IndexAt(Point point)
        {
            for (int index = 0; index < items.Length; index++)
                if (CellBounds(index).Contains(point)) return index;
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.Clear(BackColor);
            Image cell = ClientAssets.Get("case-inventaire"), glow = ClientAssets.Get("case-surbrillance");
            if (CellCount == 0)
            {
                TextRenderer.DrawText(graphics, "Aucun atelier ouvert.", BotFonts.Get(8), new Rectangle(0, 0, SlotsWidth, ClientSize.Height), BotUi.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            for (int index = 0; index < CellCount; index++)
            {
                Rectangle bounds = CellBounds(index);
                DrawCell(graphics, cell, bounds);
                if (index >= items.Length) continue;
                ExchangeItem item = items[index];
                DrawItem(graphics, bounds, item.TemplateId, item.Name, item.Quantity, 255);
                if (selectedId == item.Id)
                {
                    if (glow != null) graphics.DrawImage(glow, Rectangle.Inflate(bounds, 1, 1));
                    else using (var pen = new Pen(BotUi.Olive, 2)) graphics.DrawRectangle(pen, Rectangle.Inflate(bounds, -1, -1));
                }
            }

            Rectangle target = ResultBounds;
            int middle = target.Top + target.Height / 2, arrowRight = target.Left - Gap, arrowLeft = arrowRight - ArrowWidth + 6;
            using (var brush = new SolidBrush(BotUi.FrameLight))
                graphics.FillPolygon(brush, new[] { new Point(arrowLeft, middle - 4), new Point(arrowRight - 8, middle - 4), new Point(arrowRight - 8, middle - 9),
                    new Point(arrowRight, middle), new Point(arrowRight - 8, middle + 9), new Point(arrowRight - 8, middle + 4), new Point(arrowLeft, middle + 4) });
            DrawCell(graphics, cell, target);
            if (result != null) DrawItem(graphics, target, result.TemplateId, result.Name, result.Quantity, 255);
            else if (previewTemplate > 0) DrawItem(graphics, target, previewTemplate, JobCatalog.ItemName(previewTemplate), 1, 110);
            if (result != null && outcome == CraftOutcome.Success)
                using (var pen = new Pen(BotUi.Olive, 2)) graphics.DrawRectangle(pen, Rectangle.Inflate(target, -1, -1));
            else if (result == null && (outcome == CraftOutcome.Failed || outcome == CraftOutcome.NoResult) && items.Length == 0)
            {
                Image cross = ClientAssets.Get("inventaire-croix");
                if (cross != null) ClientAssets.DrawFit(graphics, cross, Rectangle.Inflate(target, -8, -8));
                else using (var pen = new Pen(BotUi.Frame, 3)) { graphics.DrawLine(pen, target.Left + 9, target.Top + 9, target.Right - 9, target.Bottom - 9); graphics.DrawLine(pen, target.Right - 9, target.Top + 9, target.Left + 9, target.Bottom - 9); }
            }
        }

        private static void DrawCell(Graphics graphics, Image cell, Rectangle bounds)
        {
            if (cell != null) { graphics.DrawImage(cell, bounds); return; }
            using (var fill = new SolidBrush(BotUi.PaperLight)) graphics.FillRectangle(fill, bounds);
            using (var pen = new Pen(BotUi.Gold)) graphics.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
        }

        private void DrawItem(Graphics graphics, Rectangle bounds, int templateId, string name, int quantity, int alpha)
        {
            Image icon = ExchangeItemIcons.TryGet(templateId);
            Rectangle inner = Rectangle.Inflate(bounds, -5, -5);
            if (icon != null && alpha >= 255) ClientAssets.DrawFit(graphics, icon, inner);
            else if (icon != null)
                using (var attributes = new ImageAttributes())
                {
                    attributes.SetColorMatrix(new ColorMatrix { Matrix33 = alpha / 255f });
                    float ratio = Math.Min((float)inner.Width / icon.Width, (float)inner.Height / icon.Height);
                    int width = Math.Max(1, (int)(icon.Width * ratio)), height = Math.Max(1, (int)(icon.Height * ratio));
                    graphics.DrawImage(icon, new Rectangle(inner.X + (inner.Width - width) / 2, inner.Y + (inner.Height - height) / 2, width, height),
                        0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, attributes);
                }
            else TextRenderer.DrawText(graphics, Initials(name), BotFonts.Get(8, FontStyle.Bold), bounds, alpha >= 255 ? BotUi.Ink : BotUi.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if (quantity > 1) DrawQuantity(graphics, bounds, quantity);
        }

        private static void DrawQuantity(Graphics graphics, Rectangle bounds, int quantity)
        {
            string text = quantity.ToString(CultureInfo.InvariantCulture);
            Font font = BotFonts.Get(7, FontStyle.Bold);
            Size size = TextRenderer.MeasureText(graphics, text, font, Size.Empty, TextFormatFlags.NoPadding);
            var badge = new Rectangle(bounds.Right - size.Width - 5, bounds.Bottom - size.Height - 3, size.Width + 4, size.Height + 1);
            using (var fill = new SolidBrush(Color.FromArgb(200, BotUi.Frame))) graphics.FillRectangle(fill, badge);
            TextRenderer.DrawText(graphics, text, font, badge, BotUi.PaperLight,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        private static string Initials(string name)
        {
            string[] words = (name ?? string.Empty).Split(new[] { ' ', '\'', '-' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(word => char.IsLetterOrDigit(word[0])).ToArray();
            if (words.Length == 0) return "?";
            return (words.Length == 1 ? words[0].Substring(0, Math.Min(2, words[0].Length)) : words[0].Substring(0, 1) + words[1].Substring(0, 1)).ToUpper(CultureInfo.CurrentCulture);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            int index = IndexAt(e.Location);
            SetSelection(index < 0 ? (uint?)null : items[index].Id);
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            base.OnDoubleClick(e);
            if (SelectedItem != null) ItemActivated?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = IndexAt(e.Location);
            if (index < 0 && ResultBounds.Contains(e.Location)) index = -1; else if (index < 0) index = -3;
            if (index == tipIndex) return;
            tipIndex = index;
            string tip = null;
            if (index >= 0) tip = items[index].Name + " × " + items[index].Quantity.ToString(CultureInfo.CurrentCulture);
            else if (index == -1 && result != null) tip = result.Name;
            else if (index == -1 && previewTemplate > 0) tip = "Recette reconnue : " + JobCatalog.ItemName(previewTemplate);
            tips.SetToolTip(this, tip);
        }

        protected override bool IsInputKey(Keys keyData) =>
            keyData == Keys.Left || keyData == Keys.Right || keyData == Keys.Enter || keyData == Keys.Delete || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (items.Length == 0) return;
            if ((e.KeyCode == Keys.Enter || e.KeyCode == Keys.Delete) && SelectedItem != null) { ItemActivated?.Invoke(this, EventArgs.Empty); e.Handled = true; return; }
            int step = e.KeyCode == Keys.Left ? -1 : e.KeyCode == Keys.Right ? 1 : 0;
            if (step == 0) return;
            int index = selectedId == null ? -1 : Array.FindIndex(items, item => item.Id == selectedId.Value);
            index = Math.Max(0, Math.Min(items.Length - 1, index < 0 ? 0 : index + step));
            SetSelection(items[index].Id);
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
            if (disposing) { ExchangeItemIcons.Loaded -= OnIconLoaded; tips.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
