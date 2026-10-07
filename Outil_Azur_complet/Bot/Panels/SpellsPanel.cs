using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Controls;
using Outil_Azur_complet.Bot.Controls.Banner;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Perso;
using Tool_BotProtocol.Game.Perso.Spells;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>
    /// Fenêtre des sorts du client 1.34 (<c>Spells</c>, <c>SpellFullInfosViewer</c>, <c>SpellForget</c>) : filtre par type (classe par
    /// défaut), sorts appris (<c>SL</c>) avec leur icône (<c>sorts/&lt;id&gt;.png</c>, lue hors du thread de l'interface), fiche détaillée
    /// d'un niveau (effets normaux et critiques, PA, portée, critiques, relances, niveau requis, description) d'après <c>spells.xml</c>,
    /// « Améliorer » (<c>SB&lt;id&gt;</c> → <c>SUK</c> + <c>As</c> ou <c>SUE</c>) et « Oublier » (<c>SF&lt;id&gt;</c>), actif seulement pendant
    /// la fenêtre d'oubli ouverte par le serveur (<c>SF+</c>) ; « Annuler » ou la fermeture envoient <c>SF-1</c>. Glisser un sort sur la
    /// barre de raccourcis le place localement (<c>SM</c>, réponse <c>BN</c> seulement).
    /// </summary>
    public sealed class SpellsPanel : GamePanel
    {
        /// <summary>Types du filtre (<c>Spells.initTexts</c>) : -2 tous, 0 classe, 1 élémentaire, 2 invocation, 3 maîtrise, 4 spécial.</summary>
        private static readonly int[] FilterTypes = { -2, 0, 1, 2, 3, 4 };
        private const string Reference = "SORTS";
        private const int IconSize = 20;

        private ComboBox typeFilter;
        private CheckBox seeAll;
        private Label pointsLabel, forgetBanner, spellName, spellLevel, spellCharacteristics, spellEffects, spellDescription, spellModificators;
        private Panel forgetBar, detail;
        private PictureBox spellIcon;
        private ListView spells, otherCharacteristics;
        private ImageList icons;
        private JobTabStrip levelTabs, effectTabs;
        private Label spellHelp;
        private Control upgradeSpell, forgetSpell, cancelForget;
        private ToolTip tips;
        private CharacterClass character;
        private SpellBook book;
        private Form forgetDialog;
        private short detailSpell = -1;
        private int detailLevel = 1;
        private bool forgetShown;
        private readonly HashSet<short> iconRequests = new HashSet<short>();

        public override string Title => "Sorts";
        public override Image Icon => ClientAssets.Icon("icone-sorts", 24);
        /// <summary>La fenêtre d'oubli du serveur reste affichée tant qu'elle est ouverte (<c>SF+</c> sans <c>SF-</c>).</summary>
        public override bool IsServerWindowOpen => Game?.character?.SpellBook?.ForgetWindowOpen == true;

        /// <summary>Confirmation d'oubli affichée (Oui / Non), pour les tests.</summary>
        public Form ForgetDialog => forgetDialog;
        /// <summary>Sort et niveau de la fiche détaillée.</summary>
        public short DetailSpellId => detailSpell;
        public int DetailLevel => detailLevel;
        /// <summary>Effets affichés (onglet choisi), une ligne par effet.</summary>
        public string EffectsText => spellEffects?.Text ?? string.Empty;
        public string DescriptionText => spellDescription?.Text ?? string.Empty;
        /// <summary>Lignes « caractéristique / valeur » de la fiche (PA, portée, critiques…).</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Characteristics =>
            otherCharacteristics == null ? new KeyValuePair<string, string>[0]
                : otherCharacteristics.Items.Cast<ListViewItem>().Select(row => new KeyValuePair<string, string>(row.Text, row.SubItems.Count > 1 ? row.SubItems[1].Text : string.Empty)).ToArray();

        private static string Text(string key, string fallback, params string[] args) => StatsPanel.Text(key, fallback, args);

        protected override Control CreateView()
        {
            var page = Page();
            tips = new ToolTip();
            icons = new ImageList { ImageSize = new Size(IconSize, IconSize), ColorDepth = ColorDepth.Depth32Bit };

            var filterBar = new Panel { Dock = DockStyle.Top, Height = 28, BackColor = BotUi.Paper, Name = "spells-filter-bar" };
            var filterLabel = MakeLabel(Text("SPELL_TYPE", "Type de sort"), 8.5f); filterLabel.Dock = DockStyle.Left; filterLabel.Width = 84; filterLabel.TextAlign = ContentAlignment.MiddleLeft;
            typeFilter = new ComboBox { Dock = DockStyle.Left, Width = 130, DropDownStyle = ComboBoxStyle.DropDownList, Font = BotFonts.Get(9),
                BackColor = BotUi.PaperLight, ForeColor = BotUi.Ink, Name = "spells-type" };
            typeFilter.Items.AddRange(new object[] { Text("WITHOUT_TYPE_FILTER", "Tous types"), Text("SPELL_TAB_GUILD", "Classe"), Text("SPELL_TAB_WATER", "Élémentaire"),
                Text("SPELL_TAB_FIRE", "Invocation"), Text("SPELL_TAB_EARTH", "Maîtrise"), Text("SPELL_TAB_AIR", "Spécial") });
            typeFilter.SelectedIndex = 1; // le client ouvre la fenêtre sur les sorts de classe
            typeFilter.SelectedIndexChanged += (s, e) => RefreshView();
            pointsLabel = MakeLabel(string.Empty, 8.5f, true); pointsLabel.Dock = DockStyle.Fill; pointsLabel.TextAlign = ContentAlignment.MiddleRight; pointsLabel.Name = "spells-points";
            filterBar.Controls.Add(pointsLabel); filterBar.Controls.Add(typeFilter); filterBar.Controls.Add(filterLabel);

            forgetBar = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = BotUi.PaperLight, Padding = new Padding(4), Visible = false, Name = "spells-forget-bar" };
            forgetBanner = MakeLabel(Text("SPELL_FORGET", "Oublier un sort") + " : choisissez un sort de niveau 2 ou plus.", 8.5f, true);
            forgetBanner.Dock = DockStyle.Fill; forgetBanner.TextAlign = ContentAlignment.MiddleLeft; forgetBanner.ForeColor = BotUi.Olive;
            cancelForget = MakeButton(Text("CANCEL_SMALL", "Annuler"), async (s, e) => await CancelForget(), false, 86);
            cancelForget.Dock = DockStyle.Right; cancelForget.Name = "spells-forget-cancel";
            forgetBar.Controls.Add(forgetBanner); forgetBar.Controls.Add(cancelForget);

            spells = MakeList(9, Text("NAME_BIG", "Nom"), Text("LEVEL", "Niveau"));
            spells.Dock = DockStyle.Top; spells.Height = 150; spells.Name = "spells-list";
            spells.Columns[0].Width = 290; spells.Columns[1].Width = 60;
            spells.SmallImageList = icons;
            spells.SelectedIndexChanged += (s, e) => UpdateSelection();
            spells.ItemDrag += (s, e) => DragSpell(e.Item as ListViewItem);

            seeAll = new CheckBox { Dock = DockStyle.Top, Height = 22, Text = "Afficher les sorts de classe non appris", Font = BotFonts.Get(8.5f),
                ForeColor = BotUi.Ink, BackColor = BotUi.Paper, Visible = false, Name = "spells-see-all" };
            seeAll.CheckedChanged += (s, e) => RefreshView();

            detail = BuildDetail();

            spellHelp = MakeStatus("Sélectionnez un sort."); spellHelp.Height = 40; spellHelp.Name = "spells-help";
            upgradeSpell = MakeButton("Améliorer", async (s, e) => await UpgradeSpell(), true, 120);
            upgradeSpell.Name = "spells-upgrade";
            forgetSpell = MakeButton("Oublier", async (s, e) => await ForgetSpell(), false, 110);
            forgetSpell.Name = "spells-forget";
            forgetSpell.Enabled = false;

            // Ordre d'ancrage : le dernier ajouté prend le bord en premier.
            page.Controls.Add(detail);
            page.Controls.Add(spellHelp); page.Controls.Add(BotUi.Actions(upgradeSpell, forgetSpell));
            page.Controls.Add(seeAll); page.Controls.Add(spells); page.Controls.Add(forgetBar); page.Controls.Add(filterBar);
            return page;
        }

        private Panel BuildDetail()
        {
            var area = new Panel { Dock = DockStyle.Fill, BackColor = BotUi.Paper, AutoScroll = true, Padding = new Padding(0, 6, 0, 0), Name = "spell-detail" };
            var header = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = BotUi.Paper };
            spellIcon = new PictureBox { Dock = DockStyle.Left, Width = 52, SizeMode = PictureBoxSizeMode.Zoom, BackColor = BotUi.Paper, Name = "spell-icon" };
            spellLevel = MakeLabel(string.Empty, 8.5f); spellLevel.Dock = DockStyle.Top; spellLevel.Height = 18; spellLevel.ForeColor = BotUi.Muted; spellLevel.Name = "spell-level";
            spellName = MakeLabel(string.Empty, 10.5f, true); spellName.Dock = DockStyle.Top; spellName.Height = 24; spellName.Name = "spell-name";
            spellName.Padding = new Padding(6, 4, 0, 0); spellLevel.Padding = new Padding(6, 0, 0, 0);
            header.Controls.Add(spellLevel); header.Controls.Add(spellName); header.Controls.Add(spellIcon);

            levelTabs = new JobTabStrip("1", "2", "3", "4", "5", "6") { Dock = DockStyle.Top, Name = "spell-levels" };
            levelTabs.TabClicked += (s, index) => ShowLevel(index + 1);
            spellCharacteristics = MakeLabel(string.Empty, 9, true); spellCharacteristics.Dock = DockStyle.Top; spellCharacteristics.Height = 22; spellCharacteristics.Name = "spell-characteristics";
            spellCharacteristics.TextAlign = ContentAlignment.MiddleLeft;

            effectTabs = new JobTabStrip(Text("NORMAL_EFFECTS", "Normaux"), Text("CRITICAL_EFECTS", "Critiques")) { Dock = DockStyle.Top, Name = "spell-effect-tabs" };
            effectTabs.TabClicked += (s, index) => { effectTabs.Selected = index; ShowDetail(); };
            spellEffects = MakeLabel(string.Empty, 8.5f); spellEffects.Dock = DockStyle.Top; spellEffects.Name = "spell-effects"; spellEffects.Padding = new Padding(2, 4, 2, 4);

            otherCharacteristics = MakeList(8, Text("OTHER_CHARACTERISTICS", "Autres caractéristiques"), string.Empty);
            otherCharacteristics.Dock = DockStyle.Top; otherCharacteristics.Height = 190; otherCharacteristics.Name = "spell-other";
            otherCharacteristics.Columns[0].Width = 250; otherCharacteristics.Columns[1].Width = 100;
            otherCharacteristics.Columns[1].TextAlign = HorizontalAlignment.Right;

            spellModificators = MakeLabel(string.Empty, 8.5f); spellModificators.Dock = DockStyle.Top; spellModificators.ForeColor = BotUi.Olive; spellModificators.Name = "spell-modificators";
            spellDescription = MakeLabel(string.Empty, 8.5f); spellDescription.Dock = DockStyle.Top; spellDescription.ForeColor = BotUi.Muted; spellDescription.Name = "spell-description";
            spellDescription.Padding = new Padding(2, 6, 2, 6);

            var parts = new Control[] { header, levelTabs, spellCharacteristics, effectTabs, spellEffects, spellModificators, otherCharacteristics, spellDescription };
            for (int index = parts.Length - 1; index >= 0; index--) area.Controls.Add(parts[index]);
            return area;
        }

        protected override void OnBind(GameClass game)
        {
            character = game.character;
            book = character?.SpellBook;
            if (character != null)
            {
                character.Spells_Refresh += OnSpellsChanged;
                character.RefreshCaracteristiques += OnSpellsChanged;
            }
            if (book != null) { book.Changed += OnBookChanged; book.UpgradeRefused += OnUpgradeRefused; }
        }

        protected override void OnUnbind(GameClass game)
        {
            if (character != null)
            {
                character.Spells_Refresh -= OnSpellsChanged;
                character.RefreshCaracteristiques -= OnSpellsChanged;
            }
            if (book != null) { book.Changed -= OnBookChanged; book.UpgradeRefused -= OnUpgradeRefused; }
            character = null;
            book = null;
        }

        private void OnSpellsChanged() => OnUi(RefreshView);
        private void OnUpgradeRefused(string message) => OnUi(() => Feedback(message));

        /// <summary><c>SF+</c> affiche la fenêtre comme le client charge <c>SpellForget</c> ; <c>SF-</c> la referme.</summary>
        private void OnBookChanged()
        {
            OnUi(() =>
            {
                bool open = book?.ForgetWindowOpen == true;
                if (open && !forgetShown) { forgetShown = true; RequestShow(); }
                if (!open) { forgetShown = false; CloseForgetDialog(); }
                RefreshView();
            });
        }

        protected internal override bool OnUserClose()
        {
            // Fermer la fenêtre d'oubli revient à « Annuler » dans le client : SF-1.
            if (book?.ForgetWindowOpen == true) _ = CancelForget();
            return true;
        }

        public override void RefreshView()
        {
            if (Game == null || spells == null) return;
            CharacterClass c = Game.character;
            if (c == null) return;
            SpellBook spellBook = c.SpellBook;
            bool forgetting = spellBook.ForgetWindowOpen;
            short selected = spells.SelectedItems.Count == 0 ? (short)-1 : (short)spells.SelectedItems[0].Tag;
            int filter = FilterTypes[Math.Max(0, typeFilter.SelectedIndex)];

            pointsLabel.Text = Text("SPELL_BOOST_POINT", "Capital sorts") + " : " + c.SpellPoints.ToString(CultureInfo.CurrentCulture);
            forgetBar.Visible = forgetting;
            seeAll.Visible = spellBook.CanSeeAllSpells && !forgetting;
            typeFilter.Enabled = !forgetting;

            var rows = new List<Row>();
            foreach (Spell spell in c.Spells.Values.Where(s => s != null))
            {
                if (forgetting ? spell.Level <= 1 : !Matches(spell.ID, spell.Level, filter)) continue;
                rows.Add(new Row(spell.ID, SpellBook.NameOf(spell.ID, spell), spell.Level, true));
            }
            if (!forgetting && seeAll.Visible && seeAll.Checked)
                foreach (short id in ClassSpells(c.Race_ID))
                    if (!c.Spells.ContainsKey(id) && Matches(id, 1, filter)) rows.Add(new Row(id, SpellBook.NameOf(id), 1, false));

            spells.BeginUpdate();
            try
            {
                spells.Items.Clear();
                foreach (Row row in rows.OrderBy(r => r.MinLevel).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    var item = spells.Items.Add(row.Name);
                    item.SubItems.Add(row.Level.ToString(CultureInfo.CurrentCulture));
                    item.Tag = row.Id;
                    item.ImageKey = IconKey(row.Id);
                    if (!row.Learned) item.ForeColor = BotUi.Muted;
                    if (row.Id == selected) item.Selected = true;
                }
            }
            finally { spells.EndUpdate(); }
            UpdateSelection();
        }

        private sealed class Row
        {
            public Row(short id, string name, int level, bool learned)
            {
                Id = id; Name = name; Level = level; Learned = learned;
                MinLevel = SpellLevelInfo.Get(id, level)?.MinPlayerLevel ?? 0;
            }
            public short Id { get; }
            public string Name { get; }
            public int Level { get; }
            public bool Learned { get; }
            public int MinLevel { get; }
        }

        /// <summary>Type du sort (index 11 d'un niveau) ; un sort sans données compte comme sort de classe pour rester visible.</summary>
        private static bool Matches(short id, int level, int filter)
        {
            if (filter == -2) return true;
            int type = SpellLevelInfo.Get(id, Math.Max(1, level))?.SpellType ?? 0;
            return type == filter;
        }

        /// <summary>Sorts de la classe (<c>classes.xml</c>, champ <c>s</c>), pour l'option « voir tous les sorts » (<c>SLo+</c>).</summary>
        private static IEnumerable<short> ClassSpells(byte classId)
        {
            string list = null;
            try { IReadOnlyDictionary<string, string> row = LangData.Raw("classes", "G", classId.ToString(CultureInfo.InvariantCulture)); if (row != null) row.TryGetValue("s", out list); }
            catch (Exception) { list = null; }
            foreach (string part in (list ?? string.Empty).Split(','))
                if (short.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out short id) && id > 0) yield return id;
        }

        /// <summary>Clé de l'icône dans la liste ; l'image est lue sur le pool de threads puis ajoutée sur le thread de l'interface.</summary>
        private string IconKey(short id)
        {
            string key = id.ToString(CultureInfo.InvariantCulture);
            if (icons.Images.ContainsKey(key)) return key;
            if (ClientAssets.TryCached("Spells", key, out Bitmap cached))
            {
                if (cached != null) icons.Images.Add(key, cached);
                return key;
            }
            if (iconRequests.Add(id))
                ClientAssets.GetAsync("Spells", key).ContinueWith(task =>
                {
                    if (task.Status != TaskStatus.RanToCompletion || task.Result == null) return;
                    OnUi(() =>
                    {
                        if (icons == null || spells == null || spells.IsDisposed || icons.Images.ContainsKey(key)) return;
                        icons.Images.Add(key, task.Result);
                        spells.Invalidate();
                        if (detailSpell == id) spellIcon.Image = task.Result;
                    });
                }, TaskScheduler.Default);
            return key;
        }

        /// <summary>Sélectionne la ligne d'un sort (clic droit sur un raccourci du bandeau).</summary>
        public void SelectSpell(short id)
        {
            if (spells == null) return;
            foreach (ListViewItem row in spells.Items)
                if ((short)row.Tag == id) { row.Selected = true; row.EnsureVisible(); break; }
        }

        private Spell SelectedLearned(out short id)
        {
            id = spells.SelectedItems.Count == 0 ? (short)-1 : (short)spells.SelectedItems[0].Tag;
            return id > 0 && Game?.character?.Spells != null && Game.character.Spells.TryGetValue(id, out Spell spell) ? spell : null;
        }

        private void UpdateSelection()
        {
            if (Game?.character == null || upgradeSpell == null) return;
            CharacterClass c = Game.character;
            SpellBook spellBook = c.SpellBook;
            int points = c.SpellPoints;
            Spell spell = SelectedLearned(out short id);
            bool forgetting = spellBook.ForgetWindowOpen;
            upgradeSpell.Enabled = false;
            forgetSpell.Enabled = forgetting && spell != null && spellBook.CannotForget(id) == null && forgetDialog == null;
            tips.SetToolTip(forgetSpell, forgetting ? Text("SPELL_FORGET", "Oublier un sort")
                : "L'oubli n'est possible que dans la fenêtre ouverte par le serveur (PNJ ou objet d'oubli).");
            if (id <= 0)
            {
                ShowSpell(-1, 1);
                spellHelp.Text = forgetting ? "Choisissez le sort à oublier." : points + " point(s) de sort disponible(s). Sélectionnez un sort.";
                return;
            }
            if (spell == null)
            {
                ShowSpell(id, 1);
                spellHelp.Text = "Sort de classe non appris (affiché grâce à l'option du serveur).";
                return;
            }
            if (detailSpell != id) ShowSpell(id, spell.Level);
            else ShowDetail();
            if (forgetting)
            {
                spellHelp.Text = spellBook.CannotForget(id) ?? "« Oublier » rend les points de sort de ce sort ; le serveur le ramène au niveau 1.";
                return;
            }
            int max = SpellLevelInfo.MaxLevel(id);
            if (spell.Level >= max) { spellHelp.Text = "Ce sort est au niveau maximal (" + max.ToString(CultureInfo.CurrentCulture) + ")."; return; }
            string refusal = spellBook.CannotUpgrade(id);
            spellHelp.Text = "Niveau suivant : " + (spell.Level + 1).ToString(CultureInfo.CurrentCulture) + ", coût : " + SpellBook.UpgradeCost(spell.Level).ToString(CultureInfo.CurrentCulture)
                + " point(s) sur " + points.ToString(CultureInfo.CurrentCulture) + "." + (refusal == null ? string.Empty : " " + refusal);
            upgradeSpell.Enabled = refusal == null;
        }

        /// <summary>Affiche la fiche d'un sort au niveau donné (onglets 1 à 6 de <c>SpellFullInfosViewer</c>).</summary>
        private void ShowSpell(short id, int level)
        {
            detailSpell = id;
            detailLevel = Math.Max(1, Math.Min(SpellBook.MaxSpellLevel, level));
            if (spellIcon != null)
            {
                Bitmap image = null;
                if (id > 0 && !ClientAssets.TryCached("Spells", id.ToString(CultureInfo.InvariantCulture), out image)) IconKey(id);
                spellIcon.Image = image;
            }
            ShowDetail();
        }

        /// <summary>Change le niveau affiché par la fiche (clic sur un onglet de niveau).</summary>
        public void ShowLevel(int level)
        {
            if (detailSpell <= 0 || level < 1 || level > SpellLevelInfo.MaxLevel(detailSpell)) return;
            detailLevel = level;
            ShowDetail();
        }

        private void ShowDetail()
        {
            if (spellName == null) return;
            detail.Visible = detailSpell > 0;
            if (detailSpell <= 0) return;
            short id = detailSpell;
            Spell learned = Game?.character?.Spells != null && Game.character.Spells.TryGetValue(id, out Spell spell) ? spell : null;
            SpellLevelInfo info = SpellLevelInfo.Get(id, detailLevel);
            int max = SpellLevelInfo.MaxLevel(id);
            levelTabs.Selected = detailLevel - 1;
            for (int index = 0; index < SpellBook.MaxSpellLevel; index++)
                levelTabs.SetLabel(index, index < max ? (index + 1).ToString(CultureInfo.InvariantCulture) : "–");

            spellName.Text = SpellBook.NameOf(id, learned);
            spellLevel.Text = (learned != null ? Text("ACTUAL_SPELL_LEVEL", "Niveau du sort") + " : " + learned.Level.ToString(CultureInfo.CurrentCulture) : "Non appris")
                + (info != null && info.MinPlayerLevel > 0 ? " · " + Text("REQUIRED_SPELL_LEVEL", "Niveau requis") + " : " + info.MinPlayerLevel.ToString(CultureInfo.CurrentCulture) : string.Empty);

            if (info == null)
            {
                spellCharacteristics.Text = "Données du niveau " + detailLevel.ToString(CultureInfo.CurrentCulture) + " absentes (spells.xml et BotSorts).";
                SetLines(spellEffects, new string[0]);
                otherCharacteristics.Items.Clear();
            }
            else
            {
                string range = info.RangeMin == info.RangeMax ? info.RangeMax.ToString(CultureInfo.CurrentCulture)
                    : info.RangeMin.ToString(CultureInfo.CurrentCulture) + " " + Text("TO_RANGE", "à") + " " + info.RangeMax.ToString(CultureInfo.CurrentCulture);
                spellCharacteristics.Text = info.ApCost.ToString(CultureInfo.CurrentCulture) + " " + Text("AP", "PA") + "   ·   " + range + " " + Text("RANGE", "PO");
                IReadOnlyList<SpellEffectLine> effects = effectTabs.Selected == 1 ? info.CriticalEffects : info.Effects;
                var lines = effects.Select(effect => info.FromLang ? effect.Describe() : ItemEffectFallback(effect.Type)).Where(line => !string.IsNullOrEmpty(line)).ToList();
                if (lines.Count == 0) lines.Add(effectTabs.Selected == 1 ? "Aucun effet critique." : "Aucun effet.");
                SetLines(spellEffects, lines);
                FillCharacteristics(info);
            }
            IReadOnlyList<string> bonuses = Game?.character?.SpellBook?.DescribeModificators(id) ?? new string[0];
            SetLines(spellModificators, bonuses.Select(text => "Bonus d'équipement : " + text).ToArray());
            string description = SpellBook.DescriptionOf(id);
            SetLines(spellDescription, string.IsNullOrEmpty(description) ? new string[0] : new[] { description });
        }

        private static string ItemEffectFallback(int type)
        {
            string pattern = LangData.Effect.Pattern(type);
            return string.IsNullOrEmpty(pattern) ? "Effet " + type.ToString(CultureInfo.InvariantCulture) : LangData.Effect.Describe(type) ?? "Effet " + type.ToString(CultureInfo.InvariantCulture);
        }

        private void FillCharacteristics(SpellLevelInfo info)
        {
            string yes = "Oui", no = "Non";
            var rows = new List<KeyValuePair<string, string>>
            {
                Pair(Text("CRITICAL_HIT_PROBABILITY", "Probabilité de coup critique"), info.CriticalHit > 0 ? "1/" + info.CriticalHit.ToString(CultureInfo.CurrentCulture) : "-"),
                Pair(Text("CRITICAL_MISS_PROBABILITY", "Probabilité d'échec"), info.CriticalFailure > 0 ? "1/" + info.CriticalFailure.ToString(CultureInfo.CurrentCulture) : "-"),
                Pair(Text("COUNT_BY_TURN", "Lancers par tour"), info.PerTurn > 0 ? info.PerTurn.ToString(CultureInfo.CurrentCulture) : "-"),
                Pair(Text("COUNT_BY_TURN_BY_PLAYER", "Lancers par tour et par cible"), info.PerTarget > 0 ? info.PerTarget.ToString(CultureInfo.CurrentCulture) : "-"),
                Pair(Text("DELAY_RELAUNCH", "Tours entre deux lancers"), info.Delay >= 63 ? "inf." : info.Delay > 0 ? info.Delay.ToString(CultureInfo.CurrentCulture) : "-"),
                Pair(Text("RANGE_BOOST", "Portée modifiable"), info.RangeBoostable ? yes : no),
                Pair(Text("LINE_OF_SIGHT", "Ligne de vue"), info.LineOfSight ? yes : no),
                Pair(Text("LINE_ONLY", "Lancer en ligne"), info.LineOnly ? yes : no),
                Pair(Text("FREE_CELL", "Cellules libres"), info.FreeCell ? yes : no),
                Pair(Text("FAILURE_ENDS_THE_TURN", "Un échec critique finit le tour"), info.FailureEndsTurn ? yes : no),
            };
            if (info.MinPlayerLevel > 0) rows.Add(Pair(Text("REQUIRED_SPELL_LEVEL", "Niveau requis"), info.MinPlayerLevel.ToString(CultureInfo.CurrentCulture)));
            otherCharacteristics.BeginUpdate();
            try
            {
                otherCharacteristics.Items.Clear();
                foreach (KeyValuePair<string, string> row in rows) otherCharacteristics.Items.Add(row.Key).SubItems.Add(row.Value);
            }
            finally { otherCharacteristics.EndUpdate(); }
        }

        private static KeyValuePair<string, string> Pair(string key, string value) => new KeyValuePair<string, string>(key, value);

        /// <summary>Texte sur plusieurs lignes, hauteur ajustée à la largeur du volet (le texte complet reste lisible sans infobulle).</summary>
        private static void SetLines(Label label, IReadOnlyList<string> lines)
        {
            string text = string.Join(Environment.NewLine, lines);
            label.Text = text;
            if (text.Length == 0) { label.Height = 0; return; }
            int width = Math.Max(120, (label.Parent?.ClientSize.Width ?? 360) - label.Padding.Horizontal - 4);
            Size size = TextRenderer.MeasureText(text, label.Font, new Size(width, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            label.Height = size.Height + label.Padding.Vertical + 2;
        }

        /// <summary>Glisser un sort appris vers la barre de raccourcis (<c>MouseShortcuts</c>), comme <c>Spells.itemDrag</c>.</summary>
        private void DragSpell(ListViewItem item)
        {
            DataObject data = item?.Tag is short id ? CreateDragData(id) : null;
            if (data == null) return;
            try { spells.DoDragDrop(data, DragDropEffects.Move); }
            catch (Exception error) { Account?.Logger?.LogException(Reference, error); }
        }

        /// <summary>Contenu glissé pour un sort, ou null si le sort n'est pas appris ou demande un niveau supérieur au personnage.</summary>
        public DataObject CreateDragData(short id)
        {
            CharacterClass c = Game?.character;
            if (c == null || !c.Spells.TryGetValue(id, out Spell spell) || spell == null) return null;
            SpellLevelInfo info = SpellLevelInfo.Get(id, spell.Level);
            if (info != null && info.MinPlayerLevel > c.Level) return null;
            var data = new DataObject();
            data.SetData(ShortcutBar.DragFormat, new ShortcutPayload(ShortcutSlotKind.Spell, id, 1, 0));
            return data;
        }

        private async Task UpgradeSpell()
        {
            SpellBook spellBook = Game?.character?.SpellBook;
            Spell spell = SelectedLearned(out short id);
            if (spellBook == null || spell == null || !upgradeSpell.Enabled) return;
            await ReportAsync(() => spellBook.UpgradeAsync(id));
            if (!IsDisposed) UpdateSelection();
        }

        /// <summary>« Valider » de <c>SpellForget</c> : confirmation <c>SPELL_FORGET_CONFIRM</c>, puis <c>SF&lt;id&gt;</c>.</summary>
        private async Task ForgetSpell()
        {
            try
            {
                SpellBook spellBook = Game?.character?.SpellBook;
                Spell spell = SelectedLearned(out short id);
                if (spellBook == null || spell == null || !forgetSpell.Enabled) return;
                string refusal = spellBook.CannotForget(id);
                if (refusal != null) { Feedback(refusal); return; }
                CloseForgetDialog();
                Form[] before = BotDialogs.OpenDialogs.ToArray();
                string name = SpellBook.NameOf(id, spell);
                Task<BotDialogResult> question = BotDialogs.AskYesNoAsync(Host, Text("SPELL_FORGET", "Oublier un sort"),
                    Text("SPELL_FORGET_CONFIRM", "Le sort " + name + " va être oublié. Continuer ?", name));
                forgetDialog = BotDialogs.OpenDialogs.Except(before).LastOrDefault();
                UpdateSelection();
                BotDialogResult answer = await question;
                forgetDialog = null;
                if (!IsDisposed) UpdateSelection();
                if (answer != BotDialogResult.Yes) return;
                await ReportAsync(() => spellBook.ForgetAsync(id));
                if (!IsDisposed) RefreshView();
            }
            catch (Exception error) { Account?.Logger?.LogException(Reference, error); }
        }

        private async Task CancelForget()
        {
            SpellBook spellBook = Game?.character?.SpellBook;
            if (spellBook == null || !spellBook.ForgetWindowOpen) return;
            CloseForgetDialog();
            await ReportAsync(() => spellBook.CancelForgetAsync());
            if (!IsDisposed) RefreshView();
        }

        private void CloseForgetDialog()
        {
            Form dialog = forgetDialog;
            forgetDialog = null;
            if (dialog != null && !dialog.IsDisposed) dialog.Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing) return;
            CloseForgetDialog();
            tips?.Dispose();
            // Les images de l'ImageList sont des copies ; les originaux restent dans le cache de ClientAssets.
            icons?.Dispose();
        }
    }
}
