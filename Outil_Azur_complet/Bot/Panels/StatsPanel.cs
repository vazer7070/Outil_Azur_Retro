using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Alignement;
using Tool_BotProtocol.Game.Exchanges;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Perso;
using Tool_BotProtocol.Game.Perso.Stats;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>
    /// Fiche du personnage (<c>StatsJob</c> du client 1.34) : nom, niveau, alignement et grade (lot F9), jauge d'expérience,
    /// points de vie, PA, PM, initiative, prospection, énergie, puis les six caractéristiques « base (+équipement et dons) »
    /// avec leur bouton « + » (<c>AB&lt;code&gt;</c>, visible quand le capital couvre le coût de <c>classes.xml</c>, inactif en
    /// combat) et les métiers. Le second onglet reprend <c>StatsViewer</c> : les champs 9 à 50 de <c>As</c> par catégorie
    /// avec base, équipement, dons, boost et total. Tout se met à jour à la réception de <c>As</c>.
    /// </summary>
    public sealed class StatsPanel : GamePanel
    {
        public const int MainTab = 0, AdvancedTab = 1;
        private const string Reference = "CARACTÉRISTIQUES";

        /// <summary>Catégories de <c>StatsViewer</c> (<c>FULL_STATS_CAT0</c>…<c>3</c>) et champs dans l'ordre « o » de <c>Account.onStats</c>.</summary>
        private static readonly int[][] Categories =
        {
            new[] { 12, 13, 11, 16, 14, 15, 9, 10, 17, 18 },
            new[] { 19, 20, 21, 22, 24, 25, 23, 26, 27, 28, 29, 30 },
            new[] { 31, 32, 35, 36, 47, 48, 39, 40, 43, 44 },
            new[] { 33, 34, 37, 38, 49, 50, 41, 42, 45, 46 },
        };

        private Label nameLabel, levelLabel, alignmentLabel, capitalLabel, status;
        private PictureBox alignmentIcon;
        private XpGauge xp;
        private JobTabStrip tabs;
        private Panel mainPage, advancedPage;
        private ListView advanced;
        private JobStrip jobs;
        private ToolTip tips;
        private readonly Dictionary<string, Label> infoValues = new Dictionary<string, Label>(StringComparer.Ordinal);
        private readonly Dictionary<BoostableStat, Label> statValues = new Dictionary<BoostableStat, Label>();
        private readonly Dictionary<BoostableStat, Button> boostButtons = new Dictionary<BoostableStat, Button>();
        private CharacterClass character;
        private Accounts boundAccount;
        private string alignmentShown;
        private Controls.TitleBadge titleBadge; // titre porté (lot F10)

        public override string Title => "Caractéristiques";
        public override Image Icon => ClientAssets.Icon("icone-caracteristiques", 24);

        /// <summary>Onglet affiché (<see cref="MainTab"/> ou <see cref="AdvancedTab"/>).</summary>
        public int SelectedTab => tabs?.Selected ?? MainTab;
        /// <summary>Bouton « + » d'une caractéristique (null avant la création du contenu).</summary>
        public Button BoostButton(BoostableStat stat) => boostButtons.TryGetValue(stat, out Button button) ? button : null;
        /// <summary>Valeur affichée d'une caractéristique (« base (+x) »).</summary>
        public string ValueText(BoostableStat stat) => statValues.TryGetValue(stat, out Label label) ? label.Text : string.Empty;
        /// <summary>Valeur affichée d'une ligne d'information : life, energy, ap, mp, initiative, prospection.</summary>
        public string InfoText(string key) => infoValues.TryGetValue(key, out Label label) ? label.Text : string.Empty;
        /// <summary>Lignes de l'onglet avancé : nom, base, équipement, dons, boost, total (catégories comprises).</summary>
        public IReadOnlyList<string[]> AdvancedRows =>
            advanced == null ? new string[0][] : advanced.Items.Cast<ListViewItem>().Select(row => row.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(cell => cell.Text).ToArray()).ToArray();

        internal static string Text(string key, string fallback, params string[] args) => (ExchangeRegistry.Text(key, fallback, args) ?? fallback).Trim();

        public void ShowTab(int tab)
        {
            if (tabs == null) return;
            tabs.Selected = tab == AdvancedTab ? AdvancedTab : MainTab;
            mainPage.Visible = tabs.Selected == MainTab;
            advancedPage.Visible = tabs.Selected == AdvancedTab;
            RefreshView();
        }

        protected override Control CreateView()
        {
            var page = Page();
            tips = new ToolTip();

            var header = new Panel { Dock = DockStyle.Top, Height = 54, BackColor = BotUi.Paper, Name = "stats-header" };
            alignmentIcon = new PictureBox { Dock = DockStyle.Right, Width = 48, SizeMode = PictureBoxSizeMode.Zoom, BackColor = BotUi.Paper,
                Cursor = Cursors.Hand, Name = "stats-alignment", AccessibleName = Text("ALIGNMENT", "Alignement") };
            alignmentIcon.Click += (s, e) => OpenAlignment();
            alignmentLabel = MakeLabel(string.Empty, 8); alignmentLabel.Dock = DockStyle.Top; alignmentLabel.Height = 16; alignmentLabel.ForeColor = BotUi.Muted; alignmentLabel.Name = "stats-alignment-text";
            levelLabel = MakeLabel(string.Empty, 8.5f); levelLabel.Dock = DockStyle.Top; levelLabel.Height = 17; levelLabel.Name = "stats-level";
            nameLabel = MakeLabel(string.Empty, 11, true); nameLabel.Dock = DockStyle.Top; nameLabel.Height = 21; nameLabel.Name = "stats-name";
            header.Controls.Add(alignmentLabel); header.Controls.Add(levelLabel); header.Controls.Add(nameLabel); header.Controls.Add(alignmentIcon);

            xp = new XpGauge { Dock = DockStyle.Top, Height = 16, Name = "stats-xp" };
            tabs = new JobTabStrip(Text("CHARACTERISTICS", "Caractéristiques"), Text("ADVANCED_STATS", "Caractéristiques avancées")) { Dock = DockStyle.Top, Name = "stats-tabs" };
            tabs.TabClicked += (s, tab) => ShowTab(tab);

            var content = new Panel { Dock = DockStyle.Fill, BackColor = BotUi.Paper, Padding = new Padding(0, 4, 0, 0) };
            mainPage = BuildMainPage();
            advancedPage = BuildAdvancedPage();
            content.Controls.Add(mainPage); content.Controls.Add(advancedPage);
            advancedPage.Visible = false;

            status = MakeStatus(string.Empty); status.Name = "stats-status"; status.Height = 34;

            // Ordre d'ancrage : le dernier ajouté prend le bord en premier.
            page.Controls.Add(content); page.Controls.Add(status);
            // Titre porté (lot F10) sous le nom ; le volet peut être lié à la session avant la création du contenu.
            titleBadge = new Controls.TitleBadge { Dock = DockStyle.Top };
            titleBadge.Bind(Game);
            page.Controls.Add(tabs); page.Controls.Add(Spacer(4)); page.Controls.Add(xp); page.Controls.Add(titleBadge); page.Controls.Add(header);
            return page;
        }

        private Panel BuildMainPage()
        {
            var tabPage = new Panel { Dock = DockStyle.Fill, BackColor = BotUi.Paper, AutoScroll = true, Name = "stats-main" };
            var rows = new List<Control>
            {
                InfoRow("life", "Heart", Text("LIFEPOINTS", "Points de vie")),
                InfoRow("energy", null, Text("ENERGY", "Énergie")),
                InfoRow("ap", "Star", Text("ACTIONPOINTS", "Points d'action")),
                InfoRow("mp", "stat-pm", Text("MOVEPOINTS", "Points de mouvement")),
                InfoRow("initiative", "stat-initiative", Text("INITIATIVE", "Initiative")),
                InfoRow("prospection", "stat-prospection", Text("DISCERNMENT", "Prospection")),
                Caption(Text("CHARACTERISTICS", "Caractéristiques")),
            };
            foreach (BoostableStat stat in StatsCodes.SheetOrder) rows.Add(StatRow(stat));
            capitalLabel = MakeLabel(string.Empty, 9, true); capitalLabel.Dock = DockStyle.Top; capitalLabel.Height = 24; capitalLabel.Name = "stats-capital";
            capitalLabel.TextAlign = ContentAlignment.MiddleRight; capitalLabel.Padding = new Padding(0, 0, 6, 0);
            rows.Add(capitalLabel);
            rows.Add(Caption(Text("MY_JOBS", "Mes métiers")));
            jobs = new JobStrip { Dock = DockStyle.Top, Name = "stats-jobs", AccessibleName = Text("MY_JOBS", "Mes métiers") };
            // Clic sur une case : fiche du métier (JobViewer du client), comme les cases _ctrJob de StatsJob.
            jobs.MouseUp += (s, e) => { if (e.Button == MouseButtons.Left && jobs.SelectedJobId > 0) OpenJob(jobs.SelectedJobId); };
            jobs.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter && jobs.SelectedJobId > 0) OpenJob(jobs.SelectedJobId); };
            rows.Add(jobs);
            for (int index = rows.Count - 1; index >= 0; index--) tabPage.Controls.Add(rows[index]);
            return tabPage;
        }

        private Panel BuildAdvancedPage()
        {
            var tabPage = new Panel { Dock = DockStyle.Fill, BackColor = BotUi.Paper, Name = "stats-advanced-page" };
            advanced = MakeList(8, Text("STAT_WORD", "Caractéristique"), Text("BASE_WORD", "Base"), Text("STUFF_WORD", "Équipement"),
                Text("FEATS", "Dons"), Text("BOOST", "Boost"), Text("TOTAL_WORD", "Total"));
            advanced.Name = "stats-advanced";
            int[] widths = { 150, 42, 46, 40, 44, 46 };
            for (int index = 0; index < widths.Length; index++) advanced.Columns[index].Width = widths[index];
            for (int index = 1; index < widths.Length; index++) advanced.Columns[index].TextAlign = HorizontalAlignment.Right;
            advanced.ShowItemToolTips = true;
            tabPage.Controls.Add(advanced);
            return tabPage;
        }

        private static Control Spacer(int height) => new Panel { Dock = DockStyle.Top, Height = height, BackColor = BotUi.Paper };

        private static Label Caption(string text)
        {
            Label caption = MakeLabel(text, 8.5f, true);
            caption.Dock = DockStyle.Top; caption.Height = 24; caption.ForeColor = BotUi.Muted;
            caption.TextAlign = ContentAlignment.BottomLeft;
            return caption;
        }

        private Control InfoRow(string key, string icon, string title)
        {
            var row = new Panel { Dock = DockStyle.Top, Height = 22, BackColor = BotUi.Paper, Name = "stats-row-" + key };
            Label value = MakeLabel(string.Empty, 9, true); value.Dock = DockStyle.Right; value.Width = 130; value.TextAlign = ContentAlignment.MiddleRight;
            value.Name = "stats-value-" + key; value.Padding = new Padding(0, 0, 6, 0);
            Label name = MakeLabel(title, 9); name.Dock = DockStyle.Fill; name.TextAlign = ContentAlignment.MiddleLeft;
            row.Controls.Add(name); row.Controls.Add(value); row.Controls.Add(IconBox(icon, 20));
            row.Paint += PaintRule;
            infoValues[key] = value;
            return row;
        }

        private Control StatRow(BoostableStat stat)
        {
            string code = ((byte)stat).ToString(CultureInfo.InvariantCulture);
            var row = new Panel { Dock = DockStyle.Top, Height = 28, BackColor = BotUi.Paper, Name = "stats-row-" + code };
            var plus = new Button
            {
                Dock = DockStyle.Right, Width = 26, FlatStyle = FlatStyle.Flat, BackColor = BotUi.Paper, Name = "boost-" + code,
                Image = ClientAssets.Icon("plus", 18), Text = ClientAssets.Get("plus") == null ? "+" : string.Empty,
                Cursor = Cursors.Hand, TabStop = true, AccessibleName = "Augmenter " + StatsActions.Name(stat), Visible = false,
                Tag = "client-icon", // bouton d'icône du client (24 × 24 au moins), comme le bouton de fermeture des volets
            };
            plus.FlatAppearance.BorderSize = 0;
            plus.FlatAppearance.MouseOverBackColor = BotUi.PaperLight;
            plus.Click += async (s, e) => await BoostAsync(stat);
            Label value = MakeLabel(string.Empty, 9.5f, true); value.Dock = DockStyle.Right; value.Width = 110; value.TextAlign = ContentAlignment.MiddleRight;
            value.Name = "stats-value-" + code; value.Padding = new Padding(0, 0, 4, 0);
            Label name = MakeLabel(StatName(stat), 9.5f); name.Dock = DockStyle.Fill; name.TextAlign = ContentAlignment.MiddleLeft;
            row.Controls.Add(name); row.Controls.Add(value); row.Controls.Add(plus); row.Controls.Add(IconBox(StatIcon(stat), 26));
            row.Paint += PaintRule;
            statValues[stat] = value;
            boostButtons[stat] = plus;
            return row;
        }

        private static PictureBox IconBox(string icon, int width)
        {
            return new PictureBox { Dock = DockStyle.Left, Width = width + 4, SizeMode = PictureBoxSizeMode.CenterImage, BackColor = BotUi.Paper,
                Image = icon == null ? null : ClientAssets.Icon(icon, width - 4) };
        }

        private static void PaintRule(object sender, PaintEventArgs e)
        {
            var row = (Control)sender;
            using (var pen = new Pen(Color.FromArgb(110, BotUi.Gold))) e.Graphics.DrawLine(pen, 0, row.Height - 1, row.Width, row.Height - 1);
        }

        internal static string StatName(BoostableStat stat)
        {
            switch (stat)
            {
                case BoostableStat.Vitalite: return Text("VITALITY", "Vitalité");
                case BoostableStat.Sagesse: return Text("WISDOM", "Sagesse");
                case BoostableStat.Force: return Text("FORCE", "Force");
                case BoostableStat.Intelligence: return Text("INTELLIGENCE", "Intelligence");
                case BoostableStat.Chance: return Text("CHANCE", "Chance");
                case BoostableStat.Agilite: return Text("AGILITY", "Agilité");
                default: return StatsActions.Name(stat);
            }
        }

        private static string StatIcon(BoostableStat stat)
        {
            switch (stat)
            {
                case BoostableStat.Vitalite: return "stat-vitalite";
                case BoostableStat.Sagesse: return "stat-sagesse";
                case BoostableStat.Force: return "stat-force";
                case BoostableStat.Intelligence: return "stat-intelligence";
                case BoostableStat.Chance: return "stat-chance";
                case BoostableStat.Agilite: return "stat-agilite";
                default: return "stat-neutre";
            }
        }

        protected override void OnBind(GameClass game)
        {
            character = game.character;
            titleBadge?.Bind(game);
            if (character != null)
            {
                character.RefreshCaracteristiques += OnCharacterChanged;
                character.SeeLifeRegen += OnCharacterChanged;
                character.Jobs_Refresh += OnCharacterChanged;
            }
            boundAccount = Account;
            if (boundAccount != null) boundAccount.AccountStateEvent += OnStateChanged;
        }

        protected override void OnUnbind(GameClass game)
        {
            if (character != null)
            {
                character.RefreshCaracteristiques -= OnCharacterChanged;
                character.SeeLifeRegen -= OnCharacterChanged;
                character.Jobs_Refresh -= OnCharacterChanged;
            }
            if (boundAccount != null) boundAccount.AccountStateEvent -= OnStateChanged;
            character = null;
            boundAccount = null;
            titleBadge?.Bind(null);
        }

        private void OnCharacterChanged() => OnUi(RefreshView);
        // L'état du compte change souvent (déplacements, récoltes) : seuls les boutons « + » et le message en dépendent.
        private void OnStateChanged() => OnUi(RefreshButtons);

        public override void RefreshView()
        {
            if (Game == null || mainPage == null) return;
            CharacterClass c = Game.character;
            CharacterStats s = c?.stats;
            if (c == null || s == null) return;
            nameLabel.Text = string.IsNullOrEmpty(c.Name) ? "Personnage" : c.Name;
            levelLabel.Text = Text("LEVEL", "Niveau") + " " + c.Level.ToString(CultureInfo.CurrentCulture);
            RefreshAlignment(s);
            double span = s.ExpNivNext - s.MinExpNiv;
            xp.Set(span > 0 ? (s.ActualEXP - s.MinExpNiv) / span * 100 : 0, Number(s.ActualEXP) + " / " + Number(s.ExpNivNext));
            tips.SetToolTip(xp, Text("EXPERIMENT", "Expérience") + " : " + Number(s.ActualEXP) + " / " + Number(s.ExpNivNext));

            infoValues["life"].Text = Number(s.VitalityActual) + " / " + Number(s.MaxVitality);
            infoValues["energy"].Text = Number(s.ActualEnergy) + " / " + Number(s.EnergyMax);
            // Hors combat, le client affiche base + équipement + dons pour les PA et PM.
            infoValues["ap"].Text = Number(s.PA.Displayed);
            infoValues["mp"].Text = Number(s.PM.Displayed);
            infoValues["initiative"].Text = Number(s.Initiative.BasePerso);
            infoValues["prospection"].Text = Number(s.Propec.BasePerso);

            foreach (BoostableStat stat in StatsCodes.SheetOrder)
            {
                StatsBase value = s.Get(stat);
                int extra = value.equipement + value.cadeau;
                statValues[stat].Text = Number(value.BasePerso) + (extra != 0 ? " (" + (extra > 0 ? "+" : string.Empty) + Number(extra) + ")" : string.Empty);
            }
            capitalLabel.Text = Text("CHARACTERISTICS_POINTS", "Capital") + " : " + Number(c.Carac_Points);
            jobs.SetJobs(c.GetJobsSnapshot());
            if (tabs.Selected == AdvancedTab) RefreshAdvanced(s);
            RefreshButtons();
        }

        /// <summary>Boutons « + » : visibles si le capital couvre le coût, actifs hors combat et connecté (<c>StatsJob.updateBoostButtons</c>).</summary>
        private void RefreshButtons()
        {
            CharacterClass c = Game?.character;
            CharacterStats s = c?.stats;
            if (c == null || s == null || status == null) return;
            foreach (BoostableStat stat in StatsCodes.SheetOrder)
            {
                BoostCost cost = c.StatsActions.CostOf(stat);
                Button button = boostButtons[stat];
                button.Visible = s.FieldCount > 0 && cost.Cost <= c.Carac_Points;
                button.Enabled = c.StatsActions.CanBoost(stat);
                tips.SetToolTip(button, Text("COST", "Coût") + " : " + Number(cost.Cost) + " " + Text("POUR", "pour") + " " + Number(cost.Count));
            }
            bool inFight = (Account?.IsFighting() ?? false) || Game.Fight?.IsInFight == true;
            if (!Connected) status.Text = "Hors connexion : la fiche affiche le dernier paquet As reçu.";
            else if (s.FieldCount == 0) status.Text = "Fiche en attente du paquet As du serveur.";
            else if (inFight) status.Text = "En combat : le capital ne peut pas être réparti.";
            else status.Text = c.StatsActions.LastMessage;
        }

        private void RefreshAlignment(CharacterStats s)
        {
            Alignment alignment = Game?.Interactions?.Alignment?.Alignment;
            int side = alignment?.Side ?? s.Alignement;
            if (side == Alignment.Neutral) alignmentLabel.Text = AlignmentTexts.Name(Alignment.Neutral);
            else
                alignmentLabel.Text = AlignmentTexts.Name(side) + " · " + Text("RANK", "Grade") + " " + Number(alignment?.Grade ?? s.GradeAli)
                    + " · " + Number(alignment?.Honor ?? s.Honor) + " " + Text("HONOUR_POINTS", "points d'honneur").ToLower(CultureInfo.CurrentCulture);
            tips.SetToolTip(alignmentIcon, alignment?.ToString() ?? AlignmentTexts.Name(side));
            string name = side.ToString(CultureInfo.InvariantCulture);
            if (name == alignmentShown) return;
            if (ClientAssets.TryCached("Alignments", name, out Bitmap cached)) { alignmentShown = name; alignmentIcon.Image = cached; return; }
            // Image lue hors du fil de l'interface ; le volet se met à jour à l'arrivée.
            string wanted = name;
            ClientAssets.GetAsync("Alignments", name).ContinueWith(task =>
            {
                if (task.Status != TaskStatus.RanToCompletion) return;
                OnUi(() => { if (alignmentIcon != null && !alignmentIcon.IsDisposed && alignmentShown != wanted) { alignmentShown = wanted; alignmentIcon.Image = task.Result; } });
            }, TaskScheduler.Default);
        }

        private void RefreshAdvanced(CharacterStats s)
        {
            advanced.BeginUpdate();
            try
            {
                advanced.Items.Clear();
                for (int category = 0; category < Categories.Length; category++)
                {
                    var head = advanced.Items.Add(Text("FULL_STATS_CAT" + category.ToString(CultureInfo.InvariantCulture), CategoryName(category)));
                    head.Font = BotFonts.Get(8, FontStyle.Bold); head.BackColor = BotUi.PaperLight; head.ForeColor = BotUi.Muted;
                    foreach (int field in Categories[category])
                    {
                        StatsBase value = s.Get((AsField)field);
                        var row = advanced.Items.Add(FieldName(field));
                        row.SubItems.Add(Number(value.BasePerso)); row.SubItems.Add(Number(value.equipement)); row.SubItems.Add(Number(value.cadeau));
                        row.SubItems.Add(Number(value.Boost)); row.SubItems.Add(Number(value.StatsTotal));
                        row.Tag = field;
                        row.ToolTipText = row.Text;
                    }
                }
            }
            finally { advanced.EndUpdate(); }
        }

        private static string CategoryName(int category)
        {
            switch (category)
            {
                case 0: return "Caractéristiques primaires";
                case 1: return "Caractéristiques secondaires";
                case 2: return "Bonus et malus";
                default: return "Résistances (JcJ)";
            }
        }

        /// <summary>Nom d'un champ (<c>FULL_STATS_ID&lt;n&gt;</c>), sinon libellé du bot.</summary>
        internal static string FieldName(int field)
        {
            string fallback;
            switch ((AsField)field)
            {
                case AsField.PA: fallback = "Points d'action"; break;
                case AsField.PM: fallback = "Points de mouvement"; break;
                case AsField.Force: fallback = "Force"; break;
                case AsField.Vitalite: fallback = "Vitalité"; break;
                case AsField.Sagesse: fallback = "Sagesse"; break;
                case AsField.Chance: fallback = "Chance"; break;
                case AsField.Agilite: fallback = "Agilité"; break;
                case AsField.Intelligence: fallback = "Intelligence"; break;
                case AsField.Portee: fallback = "Portée"; break;
                case AsField.Invocations: fallback = "Invocations"; break;
                case AsField.Dommages: fallback = "Dommages"; break;
                case AsField.DommagesPhysiques: fallback = "Dommages physiques"; break;
                case AsField.MaitriseArme: fallback = "Maîtrise d'arme"; break;
                case AsField.DommagesPourcent: fallback = "Dommages (%)"; break;
                case AsField.Soins: fallback = "Soins"; break;
                case AsField.DommagesPieges: fallback = "Dommages des pièges"; break;
                case AsField.DommagesPiegesPourcent: fallback = "Dommages des pièges (%)"; break;
                case AsField.RenvoiDommages: fallback = "Renvoi de dommages"; break;
                case AsField.CoupsCritiques: fallback = "Coups critiques"; break;
                case AsField.EchecsCritiques: fallback = "Échecs critiques"; break;
                case AsField.EsquivePA: fallback = "Esquive PA"; break;
                case AsField.EsquivePM: fallback = "Esquive PM"; break;
                default: fallback = ResistanceName(field); break;
            }
            return Text("FULL_STATS_ID" + field.ToString(CultureInfo.InvariantCulture), fallback);
        }

        private static string ResistanceName(int field)
        {
            if (field < 31 || field > 50) return "Champ " + field.ToString(CultureInfo.InvariantCulture);
            string[] elements = { "neutre", "terre", "eau", "air", "feu" };
            string[] kinds = { string.Empty, " (%)", " JcJ", " JcJ (%)" };
            int offset = field - 31;
            return "Résistance " + elements[offset / 4] + kinds[offset % 4];
        }

        private static string Number(double value) => value.ToString("#,0", CultureInfo.CurrentCulture);
        private static string Number(int value) => value.ToString("#,0", CultureInfo.CurrentCulture);

        /// <summary>Clic sur « + » : <c>AB&lt;code&gt;</c> tout de suite ; la fiche change à la réception de <c>As</c>.</summary>
        public async Task BoostAsync(BoostableStat stat)
        {
            CharacterClass c = Game?.character;
            if (c == null) return;
            try
            {
                InteractionResult result = await c.StatsActions.BoostAsync(stat);
                if (!result.Sent) Feedback(result.Message);
                if (!IsDisposed) OnUi(RefreshView);
            }
            catch (Exception error) { Account?.Logger?.LogException(Reference, error); }
        }

        /// <summary>Clic sur l'alignement : fenêtre de conquête (lot F9), ou <c>NEED_ALIGNMENT</c> pour un personnage neutre.</summary>
        private void OpenAlignment()
        {
            int side = Game?.Interactions?.Alignment?.Alignment?.Side ?? Game?.character?.stats?.Alignement ?? 0;
            if (side == Alignment.Neutral)
            {
                Feedback(Text("NEED_ALIGNMENT", "Il faut un alignement autre que neutre pour ouvrir cette fenêtre."));
                return;
            }
            ConquestPanel conquest = Host?.Get<ConquestPanel>();
            if (conquest != null) Host.Show(conquest);
        }

        private void OpenJob(int jobId)
        {
            JobsPanel panel = Host?.Get<JobsPanel>();
            if (panel == null || jobId <= 0) return;
            Host.Show(panel);
            panel.SelectJob(jobId);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tips?.Dispose();
        }
    }
}
