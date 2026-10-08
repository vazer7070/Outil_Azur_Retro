using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Controls.Chat;
using Tool_BotProtocol.Config;

namespace Outil_Azur_complet.Bot
{
    /// <summary>
    /// Fenêtre « Options » du client (<c>Options</c> : onglets <c>OPTIONS_GENERAL</c>, <c>OPTIONS_DISPLAY</c>, et la liste
    /// <c>KEYBORD_SHORTCUT</c>), dans la palette Retro. Chaque case écrit directement dans <see cref="BotOptions"/> : le fichier
    /// JSON est enregistré et la carte, le chat et le bandeau suivent l'événement <see cref="BotOptions.OptionChanged"/>.
    /// L'onglet Audio du client n'existe pas : le bot ne joue aucun son. Les options que le bot enregistre sans encore les
    /// dessiner (transparence, infos au survol, portées) sont marquées « à venir ».
    /// </summary>
    public sealed class OptionsForm : Form
    {
        /// <summary>Options gardées et rendues, mais pas encore dessinées par la carte du bot.</summary>
        public static readonly IReadOnlyCollection<string> PendingOptions = new HashSet<string>(StringComparer.Ordinal)
            { "Transparency", "SpriteInfos", "SpriteMove" };

        private static readonly string[] GeneralOptions = { "BannerShortcuts", "ChatEffects", "TimestampInChat", "CensorshipFilter", "ViewAllMonsterInGroup" };
        private static readonly string[] DisplayOptions = { "Grid", "MapInfos", "Transparency", "SpriteInfos", "SpriteMove", "PointsOverHead" };

        private readonly BotOptions options;
        private readonly Dictionary<string, CheckBox> checks = new Dictionary<string, CheckBox>(StringComparer.Ordinal);
        private readonly Dictionary<string, ComboBox> combos = new Dictionary<string, ComboBox>(StringComparer.Ordinal);
        private readonly List<Panel> pages = new List<Panel>();
        private readonly List<ClientButton> tabs = new List<ClientButton>();
        private readonly ToolTip tips = new ToolTip();
        private readonly ListView shortcutList;
        private readonly Label status;
        private string capturing;
        private bool updating;

        public OptionsForm(BotOptions options)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            Text = Lang("OPTIONS", "Options"); Name = "options"; AccessibleName = Text;
            Font = BotFonts.Get(9); BackColor = BotUi.Frame; ForeColor = BotUi.PaperLight; AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(560, 500); KeyPreview = true; Padding = new Padding(10);

            var heading = new Label { Dock = DockStyle.Top, Height = 28, Text = Text, Font = BotFonts.Get(12, FontStyle.Bold), ForeColor = BotUi.PaperLight,
                BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft };
            var tabBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, WrapContents = false, BackColor = Color.Transparent, Padding = new Padding(0, 3, 0, 3) };
            var frame = new ClientPanel { Dock = DockStyle.Fill, BackColor = BotUi.Paper, Padding = new Padding(12) };
            status = new Label { Dock = DockStyle.Bottom, Height = 34, ForeColor = BotUi.Gold, BackColor = Color.Transparent, AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft, Name = "options-status" };
            var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft, WrapContents = false,
                BackColor = Color.Transparent, Padding = new Padding(0, 6, 0, 0) };
            var close = (Button)BotUi.Button(Lang("CLOSE", "Fermer"), (s, e) => Close(), true, 120);
            var defaults = (Button)BotUi.Button(Lang("DEFAUT", "Défaut"), (s, e) => ResetPage(), false, 120);
            tips.SetToolTip(defaults, "Remet les options de l'onglet affiché à leur valeur du client.");
            actions.Controls.Add(close); actions.Controls.Add(defaults);
            CancelButton = close;

            Panel general = Page(), display = Page(), keys = Page();
            AddChecks(general, GeneralOptions);
            AddChoice(general, "BannerGaugeMode", "Jauge du bandeau", GaugeChoices());
            AddChoice(general, "BannerIllustrationMode", "Illustration du bandeau", IllustrationChoices());
            AddChecks(display, DisplayOptions);
            AddChoice(display, "DefaultQuality", Lang("OPTION_DEFAULTQUALITY", "Qualité Flash"), QualityChoices());

            shortcutList = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false,
                BorderStyle = BorderStyle.None, Font = BotFonts.Get(9), BackColor = BotUi.PaperLight, ForeColor = BotUi.Ink,
                HeaderStyle = ColumnHeaderStyle.Nonclickable, Name = "shortcuts", AccessibleName = Lang("KEYBORD_SHORTCUT", "Raccourcis clavier") };
            shortcutList.Columns.Add(Lang("SHORTCUTS_DESCRIPTION", "Description"), 250);
            shortcutList.Columns.Add(Lang("SHORTCUTS_KEYS", "Touches"), 110);
            shortcutList.Columns.Add("Catégorie", 130);
            shortcutList.DoubleClick += (s, e) => BeginCapture(SelectedShortcut);
            var keyActions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, WrapContents = false, BackColor = Color.Transparent, Padding = new Padding(0, 6, 0, 0) };
            keyActions.Controls.Add(BotUi.Button("Changer la touche…", (s, e) => BeginCapture(SelectedShortcut), true, 150));
            keyActions.Controls.Add(BotUi.Button("Touche du client", (s, e) => RestoreClientKey(SelectedShortcut), false, 130));
            keyActions.Controls.Add(BotUi.Button(Lang("SHORTCUTS_APPLY_DEFAULT", "Raccourcis par défaut"), (s, e) => ResetShortcuts(), false, 170));
            keys.Controls.Add(shortcutList); keys.Controls.Add(keyActions);

            foreach (Panel page in pages) frame.Controls.Add(page);
            AddTab(tabBar, Lang("OPTIONS_GENERAL", "Général"), 0);
            AddTab(tabBar, Lang("OPTIONS_DISPLAY", "Affichage"), 1);
            AddTab(tabBar, Lang("KEYBORD_SHORTCUT", "Raccourcis clavier"), 2);

            Controls.Add(frame); Controls.Add(status); Controls.Add(actions); Controls.Add(tabBar); Controls.Add(heading);
            options.OptionChanged += OnOptionChanged;
            RefreshValues(); RefreshShortcuts();
            ShowPage(0);
            Report(options.LastError != null ? "Options illisibles, valeurs du client utilisées : " + options.LastError
                : "Fichier : " + (options.FilePath ?? "options non enregistrées"));
        }

        /// <summary>Case d'une option booléenne (<c>Grid</c>, <c>MapInfos</c>…).</summary>
        public CheckBox CheckOf(string name) => name != null && checks.TryGetValue(name, out CheckBox box) ? box : null;
        /// <summary>Liste d'une option à choix (<c>DefaultQuality</c>, <c>BannerGaugeMode</c>, <c>BannerIllustrationMode</c>).</summary>
        public ComboBox ChoiceOf(string name) => name != null && combos.TryGetValue(name, out ComboBox box) ? box : null;
        public ListView ShortcutList => shortcutList;
        /// <summary>Onglet affiché (0 général, 1 affichage, 2 raccourcis).</summary>
        public int CurrentPage { get; private set; }
        /// <summary>Raccourci en attente de sa nouvelle touche, ou <c>null</c>.</summary>
        public string Capturing => capturing;
        public string StatusText => status.Text;

        public void ShowPage(int index)
        {
            if (index < 0 || index >= pages.Count) return;
            CurrentPage = index;
            for (int i = 0; i < pages.Count; i++) { pages[i].Visible = i == index; tabs[i].Primary = i == index; tabs[i].Invalidate(); }
            pages[index].BringToFront();
        }

        /// <summary>Attend la prochaine touche pour le raccourci <paramref name="name"/> (Échap annule, Suppr retire la touche).</summary>
        public bool BeginCapture(string name)
        {
            if (string.IsNullOrEmpty(name)) { Report("Choisissez d'abord un raccourci dans la liste."); return false; }
            capturing = name;
            Report(Lang("SHORTCUTS_CUSTOM_HELP", "Saisissez un raccourci pour l'action '%1' !", Description(name)) + " (Échap : annuler, Suppr : aucune touche)");
            return true;
        }

        /// <summary>
        /// Donne la touche <paramref name="keys"/> au raccourci <paramref name="name"/>. Refusée si un autre raccourci exécuté par
        /// le bot l'utilise déjà ou si elle contient Alt (aucun raccourci du client n'en a).
        /// </summary>
        public bool AssignShortcut(string name, Keys keys)
        {
            capturing = null;
            ShortcutKey key = Shortcuts.FromKeys(keys);
            if (key.IsEmpty) { Report("Touche refusée : Alt et les touches de modification seules ne sont pas des raccourcis du client."); return false; }
            ShortcutDefinition clash = Shortcuts.Build(options).Find(key).FirstOrDefault(d => d.Name != name);
            if (clash != null) { Report(Shortcuts.Describe(key) + " est déjà la touche de « " + clash.Description + " »."); return false; }
            options.SetShortcut(name, key);
            Report(Description(name) + " : " + Shortcuts.Describe(key) + ".");
            return true;
        }

        private string SelectedShortcut => shortcutList.SelectedItems.Count == 1 ? shortcutList.SelectedItems[0].Tag as string : null;

        private void RestoreClientKey(string name)
        {
            if (string.IsNullOrEmpty(name)) { Report("Choisissez d'abord un raccourci dans la liste."); return; }
            capturing = null; options.SetShortcut(name, null);
            Report(Description(name) + " : touche du client rétablie.");
        }

        private void ResetShortcuts()
        {
            capturing = null; options.ResetShortcuts();
            Report(Lang("SHORTCUTS_APPLY_DEFAULT", "Raccourcis par défaut") + " : touches du client rétablies.");
        }

        private void ResetPage()
        {
            if (CurrentPage == 2) { ResetShortcuts(); return; }
            IEnumerable<string> names = CurrentPage == 0 ? GeneralOptions.Concat(new[] { "BannerGaugeMode", "BannerIllustrationMode" }) : DisplayOptions.Concat(new[] { "DefaultQuality" });
            foreach (string name in names) options.Set(name, BotOptions.DefaultValue(name));
            Report("Valeurs du client rétablies pour cet onglet.");
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (capturing != null)
            {
                Keys code = keyData & Keys.KeyCode;
                if (code == Keys.ShiftKey || code == Keys.ControlKey || code == Keys.Menu) return true;
                string name = capturing;
                if (keyData == Keys.Escape) { capturing = null; Report("Changement de touche annulé."); return true; }
                if (keyData == Keys.Delete || keyData == Keys.Back)
                {
                    capturing = null; options.SetShortcut(name, new ShortcutKey(0, 0)); Report(Description(name) + " : aucune touche.");
                    return true;
                }
                AssignShortcut(name, keyData);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void OnOptionChanged(object sender, BotOptionChangedEventArgs e)
        {
            BotUi.OnUi(this, () => { if (e.Name == BotOptions.ShortcutsOption) RefreshShortcuts(); else if (e.Name != BotOptions.SpellBarOption) RefreshValues(); });
        }

        private void RefreshValues()
        {
            updating = true;
            try
            {
                foreach (var pair in checks) pair.Value.Checked = options.GetBool(pair.Key);
                foreach (var pair in combos)
                {
                    string value = options.GetString(pair.Key);
                    Choice match = pair.Value.Items.OfType<Choice>().FirstOrDefault(c => c.Value == value);
                    if (match == null && value != null) { match = new Choice(value, value + " (à venir)"); pair.Value.Items.Add(match); }
                    pair.Value.SelectedItem = match;
                }
            }
            finally { updating = false; }
        }

        private void RefreshShortcuts()
        {
            string selected = SelectedShortcut;
            ShortcutTable table = Shortcuts.Build(options);
            shortcutList.BeginUpdate();
            try
            {
                shortcutList.Items.Clear();
                foreach (ShortcutDefinition definition in table.Definitions)
                {
                    string label = definition.Handled ? definition.Description : definition.Description + " (non exécuté par le bot)";
                    var item = new ListViewItem(new[] { label, definition.Label + (definition.IsCustom ? " *" : string.Empty), definition.Category })
                        { Tag = definition.Name, ForeColor = definition.Handled ? BotUi.Ink : BotUi.Muted, Selected = definition.Name == selected };
                    shortcutList.Items.Add(item);
                }
            }
            finally { shortcutList.EndUpdate(); }
        }

        private string Description(string name) => Shortcuts.Build(options)[name]?.Description ?? name;

        private Panel Page()
        {
            var page = new Panel { Dock = DockStyle.Fill, BackColor = BotUi.Paper, Visible = false, AutoScroll = true };
            pages.Add(page);
            return page;
        }

        private void AddTab(FlowLayoutPanel bar, string text, int index)
        {
            var tab = (ClientButton)BotUi.Button(text, (s, e) => ShowPage(index), false, index == 2 ? 170 : 120);
            tab.AccessibleName = "Onglet " + text; tab.Margin = new Padding(0, 0, 6, 0);
            tabs.Add(tab); bar.Controls.Add(tab);
        }

        private void AddChecks(Panel page, IEnumerable<string> names)
        {
            int top = page.Controls.Count == 0 ? 4 : page.Controls.Cast<Control>().Max(c => c.Bottom) + 4;
            foreach (string name in names)
            {
                bool pending = PendingOptions.Contains(name);
                string text = OptionLabel(name) + (pending ? " · à venir" : string.Empty);
                var box = new CheckBox { Text = text, AutoSize = false, Width = 500, Height = 26, Location = new Point(4, top), BackColor = Color.Transparent,
                    ForeColor = pending ? BotUi.Muted : BotUi.Ink, Font = BotFonts.Get(9), Name = "option-" + name, AccessibleName = OptionLabel(name) };
                string option = name;
                box.CheckedChanged += (s, e) => { if (!updating) options.Set(option, box.Checked); };
                if (pending) tips.SetToolTip(box, "Option enregistrée comme dans le client ; la carte du bot ne la dessine pas encore.");
                checks[name] = box; page.Controls.Add(box);
                top += 28;
            }
        }

        private void AddChoice(Panel page, string name, string title, IEnumerable<Choice> choices)
        {
            int top = page.Controls.Count == 0 ? 4 : page.Controls.Cast<Control>().Max(c => c.Bottom) + 10;
            var label = new Label { Text = title, Location = new Point(4, top + 4), Size = new Size(200, 22), ForeColor = BotUi.Ink, BackColor = Color.Transparent,
                Font = BotFonts.Get(9, FontStyle.Bold) };
            var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(210, top), Width = 220, BackColor = BotUi.PaperLight,
                ForeColor = BotUi.Ink, Font = BotFonts.Get(9), Name = "option-" + name, AccessibleName = title };
            box.Items.AddRange(choices.Cast<object>().ToArray());
            string option = name;
            box.SelectedIndexChanged += (s, e) => { if (!updating && box.SelectedItem is Choice choice) options.Set(option, choice.Value); };
            combos[name] = box; page.Controls.Add(label); page.Controls.Add(box);
        }

        private static IEnumerable<Choice> GaugeChoices()
        {
            string xp = Lang("WORD_XP", "XP");
            yield return new Choice("none", Lang("DISABLE", "Désactiver"));
            yield return new Choice("xp", xp);
            yield return new Choice("xpcurrentjob", xp + " " + Lang("JOB", "Métier"));
            yield return new Choice("xpmount", xp + " " + Lang("MOUNT", "Monture"));
            yield return new Choice("pods", Lang("WEIGHT", "Pods"));
            yield return new Choice("energy", Lang("ENERGY", "Energie"));
        }

        private static IEnumerable<Choice> IllustrationChoices()
        {
            yield return new Choice("artwork", Lang("BANNER_ARTWORK", "Portrait"));
            yield return new Choice("clock", Lang("BANNER_CLOCK", "Horloge"));
            yield return new Choice("compass", Lang("BANNER_COMPASS", "Boussole"));
        }

        internal static IEnumerable<Choice> QualityChoices()
        {
            yield return new Choice("low", Lang("QUALITY_LOW", "Faible"));
            yield return new Choice("medium", Lang("QUALITY_MEDIUM", "Moyenne"));
            yield return new Choice("high", Lang("QUALITY_HIGH", "Haute"));
            yield return new Choice("best", Lang("QUALITY_BEST", "Meilleure"));
        }

        /// <summary>Libellé du client pour une option (<c>OPTION_GRID</c>…), avec un repli quand le texte manque.</summary>
        public static string OptionLabel(string name)
        {
            switch (name)
            {
                case "Grid": return Lang("OPTION_GRID", "Afficher la grille");
                case "Transparency": return Lang("OPTION_TRANSPARENCY", "Afficher les joueurs en transparence");
                case "SpriteInfos": return Lang("OPTION_SPRITEINFOS", "Afficher les infos au survol d'un joueur en combat");
                case "SpriteMove": return Lang("OPTION_SPRITEMOVE", "Afficher les portées de déplacement");
                case "MapInfos": return Lang("OPTION_MAPINFOS", "Afficher les coordonnées de la carte");
                case "ChatEffects": return Lang("OPTION_CHATEFFECTS", "Afficher les infos de combat dans le chat");
                case "PointsOverHead": return Lang("OPTION_POINTSOVERHEAD", "Afficher les valeurs au dessus des persos");
                case "ViewAllMonsterInGroup": return Lang("OPTION_VIEWALLMONSTERINGROUP", "Afficher tous les monstres d'un groupe");
                case "TimestampInChat": return Lang("OPTION_USE_CHATTIMESTAMP", "Afficher l'heure de réception des messages dans le chat");
                case "BannerShortcuts": return Lang("OPTION_BANNERSHORTCUTS", "Activer les raccourcis (caractéristiques, inventaire, ...)");
                case "CensorshipFilter": return Lang("OPTION_CENSORSHIP_FILTER", "Filtrer les mots insultants dans le chat");
                case "DefaultQuality": return Lang("OPTION_DEFAULTQUALITY", "Qualité Flash");
                default: return name;
            }
        }

        private static string Lang(string key, string fallback, params string[] args) => ChatUiText.Get(key, fallback, args);

        private void Report(string message) { status.Text = message ?? string.Empty; tips.SetToolTip(status, status.Text); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { options.OptionChanged -= OnOptionChanged; tips.Dispose(); }
            base.Dispose(disposing);
        }

        /// <summary>Valeur d'une option à choix et son libellé affiché.</summary>
        internal sealed class Choice
        {
            internal Choice(string value, string text) { Value = value; Text = text; }
            internal string Value { get; }
            internal string Text { get; }
            public override string ToString() => Text;
        }
    }
}
