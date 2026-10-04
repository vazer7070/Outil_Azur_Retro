using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Interfaces;
using Outil_Azur_complet.Bot.Controls;
using Outil_Azur_complet.Bot.Controls.Banner;
using Outil_Azur_complet.Bot.Controls.Chat;
using Outil_Azur_complet.Bot.Panels;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Perso.Spells;
namespace Outil_Azur_complet.Bot
{
    /// <summary>
    /// Fenêtre de jeu : composition seulement. Barre d'état en haut (état de la session, retour d'action, zoom, actions de
    /// combat), carte (<see cref="MapControl"/> et son routeur de clics) avec le tiroir des volets (<see cref="Panels"/>), le
    /// nom de zone centré et les coordonnées, puis le bandeau bas (<see cref="HudPanel"/> : discussion <see cref="ChatPanel"/>,
    /// cœur et PA / PM, bandeau du client <see cref="BannerPanel"/>). Le clic droit sur la barre ou le bandeau ouvre le menu
    /// global du client (version, qualité, options) et le diagnostic ; les touches suivent la table <see cref="Shortcuts"/>.
    /// Chaque fonctionnalité vit dans son volet (<c>Bot/Panels</c>), son contrôle (<c>Bot/Controls</c>) ou son fournisseur
    /// de menu (<c>Bot/Menus</c>).
    /// </summary>
    public partial class GameClientFullform : Form
    {
        public Accounts ActualCompte { get; set; }
        public Form FG;
        public List<string> DebugMessages = new List<string>();
        private Panel mapArea;
        private Label summary, state;
        private ChatPanel chatPanel;
        private TableLayoutPanel root;
        private CircleGauge xp;
        private MapControl mapControl;
        private PanelHost drawer;
        private HudPanel hud;
        private BannerPanel banner;
        private Label zoomText, emptyMap;
        private bool showCellIds, uiReleased, censorshipLoaded;
        private ChatCensorship censorship;
        private readonly BotOptions options;
        private readonly ToolTip toolTips = new ToolTip();
        private readonly ContextMenuStrip globalMenu = new ContextMenuStrip();
        private List<Button> quickSpells;
        private Dictionary<Button, short> quickSpellIds;
        private Button previousSpellPage, nextSpellPage, ready, passTurn;
        private FlowLayoutPanel combatTools;
        private OptionsForm optionsWindow;
        private string actionFeedback;
        private DateTime actionFeedbackUntil;
        private readonly Timer refresh = new Timer { Interval=1000 };

        /// <summary>Tiroir des volets : <c>Panels.Show(volet)</c>, <c>Toggle</c>, <c>Open("Stats")</c>, <c>Get&lt;T&gt;()</c>.</summary>
        public PanelHost Panels => drawer;
        /// <summary>Bandeau bas en trois emplacements (discussion, cœur et PA / PM, bandeau du client).</summary>
        public HudPanel Hud => hud;
        /// <summary>Volet de discussion du bandeau.</summary>
        public ChatPanel Chat => chatPanel;
        /// <summary>Bandeau du client : boutons ronds, jauge, illustration, barre de raccourcis, menu principal.</summary>
        public BannerPanel Banner => banner;
        /// <summary>Options du client appliquées à cette fenêtre.</summary>
        public BotOptions Options => options;
        /// <summary>Fenêtre « Options » ouverte, ou <c>null</c>.</summary>
        public OptionsForm OptionsWindow => optionsWindow != null && !optionsWindow.IsDisposed ? optionsWindow : null;
        /// <summary>Menu du clic droit global (reconstruit à chaque ouverture).</summary>
        public ContextMenuStrip GlobalMenu { get { if (!uiReleased) FillGlobalMenu(); return globalMenu; } }
        /// <summary>Hauteur du bandeau bas quand le chat est réduit.</summary>
        public const int HudHeight = 112;
        /// <summary>Hauteur de la barre d'état en haut de la fenêtre.</summary>
        public const int TopBarHeight = 26;
        /// <summary>Hauteur de carte gardée quand le chat est agrandi.</summary>
        private const int MinimumMapHeight = 160;

        public GameClientFullform(Accounts account) : this(account, null) { }

        /// <param name="options">Options du client ; <c>null</c> = <see cref="BotOptions.Current"/> (fichier du dossier de configuration).</param>
        public GameClientFullform(Accounts account, BotOptions options)
        {
            ActualCompte=account; this.options = options ?? BotOptions.Current;
            BuildLayout();_ = Handle;Subscribe();
            Load+=(s,e)=> { RefreshState();InGameMap();refresh.Start(); };FormClosed+=OnSessionClosed;refresh.Tick+=(s,e)=>RefreshStatus();
        }
        private void BuildLayout()
        {
            BotUi.Prepare(this, "AzurClientRetro · " + ActualCompte.accountConfig.Account, new Size(1100, 760));
            KeyPreview = true;
            root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
                Padding = new Padding(3), BackColor = BotUi.Frame, Margin = new Padding(0) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, TopBarHeight));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, HudHeight));
            Controls.Add(root);
            root.Resize += (s,e) => UpdateHudHeight();
            globalMenu.Renderer = new RetroMenuRenderer(); globalMenu.BackColor = BotUi.Paper; globalMenu.Font = BotFonts.Get(9);
            globalMenu.ShowItemToolTips = true; globalMenu.AccessibleName = "Menu du client"; globalMenu.Name = "global-menu";
            globalMenu.Opening += (s,e) => { if (uiReleased) e.Cancel = true; else FillGlobalMenu(); };

            root.Controls.Add(BuildTopBar(), 0, 0);
            mapArea = new Panel { Dock = DockStyle.Fill, BackColor = BotUi.Frame, Margin = new Padding(0) };
            emptyMap = BotUi.Label("La carte apparaît après la sélection du personnage.\nLes ressources absentes sont indiquées ici.", 11);
            emptyMap.Dock = DockStyle.Fill; emptyMap.ForeColor = BotUi.Gold;
            emptyMap.TextAlign = ContentAlignment.MiddleCenter; mapArea.Controls.Add(emptyMap);
            root.Controls.Add(mapArea, 0, 1);
            BuildDrawer(mapArea);

            hud = new HudPanel { Dock = DockStyle.Fill };
            root.Controls.Add(hud, 0, 2);
            BuildChat(hud);
            BuildBanner(hud);
            ApplyOptions();
            options.OptionChanged += OnOptionChanged;

            KeyDown += (s,e) => { if (ProcessShortcut(e.KeyData)) { e.Handled = true; e.SuppressKeyPress = true; } };
        }

        /// <summary>Barre d'état : session à gauche, retour d'action (ou carte et cellule), zoom, actions de combat.</summary>
        private Control BuildTopBar()
        {
            var bar = new Panel { Dock = DockStyle.Fill, BackColor = BotUi.FrameLight, Margin = new Padding(0), Name = "top-bar", ContextMenuStrip = globalMenu };
            summary = BotUi.Label("Personnage en cours de chargement", 9); summary.Dock = DockStyle.Fill;
            summary.ForeColor = BotUi.PaperLight; summary.TextAlign = ContentAlignment.MiddleLeft;
            summary.AutoEllipsis = true; summary.Padding = new Padding(7, 0, 0, 0);
            state = BotUi.Label("Déconnecté", 9); state.ForeColor = BotUi.Gold; state.Dock = DockStyle.Left; state.Width = 250; state.Height = 24;
            state.TextAlign = ContentAlignment.MiddleLeft; state.AutoEllipsis = true; state.Padding = new Padding(6, 0, 0, 0);
            var zoomTools = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 218, WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0), Margin = new Padding(0) };
            zoomText = BotUi.Label("100 %", 8); zoomText.ForeColor = BotUi.Gold; zoomText.Width = 52;
            zoomText.Height = 23; zoomText.TextAlign = ContentAlignment.MiddleCenter;
            zoomTools.Controls.Add(MiniButton("−", (s,e) => mapControl?.ZoomOut(), 30));
            zoomTools.Controls.Add(zoomText);
            zoomTools.Controls.Add(MiniButton("+", (s,e) => mapControl?.ZoomIn(), 30));
            zoomTools.Controls.Add(MiniButton("Adapter", (s,e) => mapControl?.Fit(), 78));
            combatTools = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 0, WrapContents = false, Visible = false, Margin = new Padding(0) };
            ready = MiniButton("Prêt", async (s,e) => await ToggleReady(), 62); ((ClientButton)ready).Primary = true;
            passTurn = MiniButton("Passer", async (s,e) => await PassTurn(), 67);
            ((ClientButton)passTurn).Glyph = ClientAssets.Icon("tour-suivant-haut", 18); // flèche de fin de tour du client
            ready.AccessibleName = "Prêt pour le combat"; passTurn.AccessibleName = "Passer le tour";
            combatTools.Controls.Add(ready); combatTools.Controls.Add(passTurn);
            bar.Controls.Add(summary); bar.Controls.Add(state); bar.Controls.Add(zoomTools); bar.Controls.Add(combatTools);
            return bar;
        }

        private void BuildChat(HudPanel host)
        {
            chatPanel = new ChatPanel(ActualCompte, () => mapControl?.Router) { Dock = DockStyle.Fill };
            chatPanel.SetSendImage(new System.ComponentModel.ComponentResourceManager(typeof(GameClientFullform)).GetObject("iTalk_Button_21.Image") as Image);
            chatPanel.ExpandedChanged += OnChatExpanded;
            chatPanel.CompassRequested += OnCompassRequested;
            chatPanel.Feedback += ShowActionFeedback;
            chatPanel.View.ShowTimestampsChanged += OnTimestampsToggled;
            host.SetSlot(HudSlot.Left, chatPanel);
        }

        /// <summary>Bandeau du client : centre = cœur et PA / PM, droite = boutons, jauge, illustration et barre de raccourcis.</summary>
        private void BuildBanner(HudPanel host)
        {
            banner = new BannerPanel(ActualCompte, drawer, options) { ContextMenuStrip = globalMenu };
            banner.Center.ContextMenuStrip = globalMenu;
            banner.Feedback += ShowActionFeedback;
            banner.Fill(host);
            xp = banner.Gauge;
            ShortcutBar bar = banner.Shortcuts;
            quickSpells = bar.SlotList; quickSpellIds = bar.SpellIds;
            previousSpellPage = bar.PreviousPage; nextSpellPage = bar.NextPage;
            bar.SelectedSpell = () => mapControl?.SelectedSpellId;
            bar.SpellClicked += SelectQuickSpell;
            bar.SpellDetailsRequested += OpenSpellDetails;
            banner.MainMenu.OptionsRequested += (s,e) => ShowOptions();
            banner.MainMenu.ChangeCharacterRequested += (s,e) => ChangeCharacter();
            banner.MainMenu.LogoffRequested += (s,e) => Disconnect();
            banner.MainMenu.QuitRequested += (s,e) => Close();
            mapArea.Controls.Add(banner.MapInfos); mapArea.Controls.Add(banner.CenterText);
            banner.MapInfos.BringToFront();
            mapArea.Resize += (s,e) => banner.CenterText.Center();
        }

        /// <summary>Chat agrandi : le bandeau gagne jusqu'à 350 pixels (<c>OPEN_OFFSET</c>) pris sur la carte ; le bandeau du client reste en bas.</summary>
        private void OnChatExpanded(object sender, EventArgs e) => UpdateHudHeight();
        private void UpdateHudHeight()
        {
            if (root == null || root.RowStyles.Count < 3 || chatPanel == null) return;
            int extra = 0;
            if (chatPanel.Expanded)
                extra = Math.Max(0, Math.Min(ChatPanel.ExpandOffset, root.ClientSize.Height - root.Padding.Vertical - TopBarHeight - HudHeight - MinimumMapHeight));
            if ((int)root.RowStyles[2].Height == HudHeight + extra) return;
            foreach (Control part in new[] { banner?.Center, (Control)banner }) {
                if (part == null) continue;
                if (extra > 0 && part.Dock == DockStyle.Fill) { int height = part.Height; part.Dock = DockStyle.Bottom; part.Height = height; }
                else if (extra == 0) part.Dock = DockStyle.Fill;
            }
            root.RowStyles[2].Height = HudHeight + extra;
        }
        /// <summary>Lien <c>[x,y]</c> du chat : boussole du bandeau vers ces coordonnées (<c>updateCompass</c>) et retour d'action.</summary>
        private void OnCompassRequested(int x, int y)
        {
            banner?.SetCompassTarget(new Point(x, y));
            ShowActionFeedback(ChatLinks.DescribeCompass(x, y, ActualCompte.Game?.Map));
        }

        /// <summary>Tiroir des volets dans la zone de la carte ; les volets d'origine y sont enregistrés (masqués).</summary>
        private void BuildDrawer(Panel host)
        {
            drawer = new PanelHost { Width = 410, Anchor = AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom };
            drawer.Account = ActualCompte;
            drawer.Feedback += ShowActionFeedback;
            drawer.PanelShown += (s,e) => { LayoutDrawer(); banner?.RequestRefresh(); };
            drawer.PanelClosed += (s,e) => banner?.RequestRefresh();
            foreach (IGamePanel panel in new IGamePanel[] { new StatsPanel(), new InventoryPanel(), new SpellsPanel(), new JobsPanel(),
                new JournalPanel(), new DialoguePanel(), new ZaapsPanel(), new ShopPanel(), new ExchangePanel(), new StoragePanel(), new FightsListPanel(), new ZaapiPanel(), new KeyCodePanel(), new DocumentPanel(), new PartyPanel(), new FriendsPanel() })
                drawer.Register(panel);
            host.Controls.Add(drawer); drawer.BringToFront();
            host.Resize += (s,e) => LayoutDrawer(); LayoutDrawer();
        }
        private void LayoutDrawer()
        {
            if (drawer == null) return;
            int width = Math.Min(410, Math.Max(330, mapArea.Width - 350));
            drawer.SetBounds(Math.Max(0, mapArea.Width - width - 6), 6, width, Math.Max(1, mapArea.Height - 12));
        }
        private void ShowPanel<T>() where T : class, IGamePanel => drawer.Show(drawer.Get<T>());
        private Button MiniButton(string title, EventHandler click, int width)
        {
            var button = (Button)BotUi.Button(title, click, false, width);
            button.Height = 24; button.Font = new Font("Tahoma",8); button.Tag = "client-icon"; button.Margin = new Padding(1,0,1,0);
            return button;
        }

        // ------------------------------------------------------------------ menu global (clic droit du client)

        /// <summary>
        /// Clic droit global du client : version (<c>Client v…</c>), « Qualité Flash » et ses quatre niveaux, « Options », barre
        /// déplaçable (à venir) ; puis les entrées propres au bot : carte, métiers et diagnostic (journal de session, paquets).
        /// </summary>
        private void FillGlobalMenu()
        {
            foreach (ToolStripItem old in globalMenu.Items.Cast<ToolStripItem>().ToArray()) { globalMenu.Items.Remove(old); old.Dispose(); }
            string version = GlobalConfig.VERSION;
            globalMenu.Items.Add(new ToolStripMenuItem("AzurClientRetro · client " + (string.IsNullOrEmpty(version) ? "1.34" : version)) { Enabled = false, Name = "version" });
            var quality = new ToolStripMenuItem(Lang("OPTION_DEFAULTQUALITY", "Qualité Flash") + " >>") { Name = "quality" };
            foreach (OptionsForm.Choice choice in OptionsForm.QualityChoices())
            {
                string value = choice.Value;
                quality.DropDownItems.Add(new ToolStripMenuItem(choice.Text, null, (s,e) => options.DefaultQuality = value) { Checked = options.DefaultQuality == value, Name = "quality-" + value });
            }
            quality.DropDown.Renderer = new RetroMenuRenderer(); quality.DropDown.BackColor = BotUi.Paper;
            globalMenu.Items.Add(quality);
            globalMenu.Items.Add(new ToolStripMenuItem(Lang("OPTIONS", "Options"), null, (s,e) => ShowOptions()) { Name = "options" });
            globalMenu.Items.Add(new ToolStripMenuItem(Lang("OPTION_MOVABLEBAR", "Afficher la barre de raccourci déplaçable"))
                { Enabled = false, Name = "movable-bar", ToolTipText = "À venir : la barre de raccourcis déplaçable n'est pas encore livrée." });
            globalMenu.Items.Add(new ToolStripSeparator());
            var cellIds = new ToolStripMenuItem("Numéros des cellules", null, (s,e) => { showCellIds = !showCellIds; if (mapControl != null) mapControl.ShowCellIds = showCellIds; })
                { Checked = showCellIds, Name = "cell-ids" };
            globalMenu.Items.Add(cellIds);
            globalMenu.Items.Add(new ToolStripMenuItem("Ajuster la carte", null, (s,e) => mapControl?.Fit()) { Name = "fit" });
            globalMenu.Items.Add(new ToolStripMenuItem("Plein écran (" + ShortcutLabel("FULLSCREEN") + ")", null, (s,e) => ToggleFullScreen()) { Name = "fullscreen" });
            globalMenu.Items.Add(new ToolStripMenuItem("Métiers", null, (s,e) => ShowPanel<JobsPanel>()) { Name = "jobs" });
            var diagnostic = new ToolStripMenuItem("Diagnostic >>") { Name = "diagnostic" };
            diagnostic.DropDown.Renderer = new RetroMenuRenderer(); diagnostic.DropDown.BackColor = BotUi.Paper;
            diagnostic.DropDownItems.Add(new ToolStripMenuItem("Journal de session", null, (s,e) => ShowPanel<JournalPanel>()) { Name = "journal" });
            diagnostic.DropDownItems.Add(new ToolStripMenuItem("Journal des paquets", null, (s,e) => ShowPackets()) { Name = "packets" });
            globalMenu.Items.Add(diagnostic);
        }

        private static string Lang(string key, string fallback) => ChatUiText.Get(key, fallback);
        private string ShortcutLabel(string name) => banner?.Table[name]?.Label ?? name;

        /// <summary>Fenêtre « Options » (une seule à la fois).</summary>
        public OptionsForm ShowOptions()
        {
            if (OptionsWindow != null) { optionsWindow.Activate(); return optionsWindow; }
            optionsWindow = new OptionsForm(options);
            optionsWindow.FormClosed += (s,e) => { var closed = (Form)s; if (ReferenceEquals(optionsWindow, closed)) optionsWindow = null; BeginDispose(closed); };
            if (Visible) optionsWindow.Show(this); else optionsWindow.Show();
            return optionsWindow;
        }

        private static void BeginDispose(Form form)
        {
            try { if (form.IsHandleCreated) form.BeginInvoke((Action)form.Dispose); else form.Dispose(); }
            catch (InvalidOperationException) { form.Dispose(); }
        }

        // ------------------------------------------------------------------ options du client

        private void OnOptionChanged(object sender, BotOptionChangedEventArgs e) => BotUi.OnUi(this, () => { if (!uiReleased) ApplyOptions(); });

        /// <summary>Recopie les options sur la carte (grille, monstres du groupe, bulles, qualité) et le chat (heure, filtre des mots).</summary>
        private void ApplyOptions()
        {
            if (mapControl != null)
            {
                mapControl.ShowGrid = options.Grid;
                mapControl.ViewAllMonsterInGroup = options.ViewAllMonsterInGroup;
                mapControl.ChatEffects = options.ChatEffects;
                mapControl.Quality = QualityOf(options.DefaultQuality);
            }
            if (chatPanel != null)
            {
                chatPanel.View.ShowTimestamps = options.TimestampInChat;
                chatPanel.Censor = options.CensorshipFilter ? (Func<string, string>)CensorText : null;
            }
        }

        /// <summary>Qualité Flash → rendu de la carte : faible = basse, moyenne = moyenne, haute et meilleure = haute.</summary>
        public static MapQuality QualityOf(string value)
        {
            switch (value)
            {
                case "low": return MapQuality.BAS;
                case "medium": return MapQuality.MOYEN;
                default: return MapQuality.HAUT;
            }
        }

        private void OnTimestampsToggled(object sender, EventArgs e)
        {
            if (chatPanel != null && !uiReleased) options.TimestampInChat = chatPanel.View.ShowTimestamps;
        }

        private string CensorText(string text)
        {
            if (!censorshipLoaded && LangData.IsLoaded("lang")) { censorship = ChatCensorship.FromLang(); censorshipLoaded = true; }
            return censorship?.Apply(text) ?? text;
        }

        // ------------------------------------------------------------------ raccourcis clavier

        /// <summary>
        /// Touche reçue par la fenêtre : action du raccourci de la table en vigueur (<c>KeyManager.onShortcut</c>,
        /// <c>Banner.onShortcut</c>). Pendant la saisie d'un texte, seuls les raccourcis marqués « à tout moment »
        /// (<c>o="false"</c> : Échap, Ctrl+F) agissent. Renvoie vrai si la touche a été prise.
        /// </summary>
        public bool ProcessShortcut(Keys keyData)
        {
            if (uiReleased || banner == null) return false;
            bool typing = IsTyping();
            foreach (ShortcutDefinition definition in banner.Table.Find(keyData).ToArray())
            {
                if (typing && definition.OutsideChatOnly) continue;
                if (RunShortcut(definition.Name, typing)) return true;
            }
            return false;
        }

        private bool IsTyping()
        {
            Control focused = ActiveControl;
            while (focused is ContainerControl container && container.ActiveControl != null) focused = container.ActiveControl;
            return focused is TextBoxBase || focused is ComboBox;
        }

        private static readonly Dictionary<string, string> BannerPanels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "CHARAC", "Stats" }, { "SPELLS", "Spells" }, { "INVENTORY", "Inventory" }, { "QUESTS", "Quests" },
            { "MAP", "WorldMap" }, { "FRIENDS", "Friends" }, { "GUILD", "Guild" }, { "MOUNT", "Mount" },
        };

        private bool RunShortcut(string name, bool typing)
        {
            if (BannerPanels.TryGetValue(name, out string panel))
            {
                if (!options.BannerShortcuts) return false;
                banner.OpenPanel(panel); return true;
            }
            if (name.StartsWith("SH", StringComparison.Ordinal) && int.TryParse(name.Substring(2), NumberStyles.None, CultureInfo.InvariantCulture, out int slot))
            { banner.Shortcuts.Activate(slot); return true; }
            switch (name)
            {
                case "SWAP": banner.Shortcuts.Tab = banner.Shortcuts.Tab == ShortcutTab.Spells ? ShortcutTab.Items : ShortcutTab.Spells; return true;
                case "GRID": options.Toggle("Grid"); return true;
                case "TRANSPARENCY": options.Toggle("Transparency"); return true;
                case "SPRITEINFOS": options.Toggle("SpriteInfos"); return true;
                case "COORDS": options.Toggle("MapInfos"); return true;
                case "TOGGLE_FIGHT_INFOS": options.Toggle("ChatEffects"); return true;
                case "MAXI": if (chatPanel != null) chatPanel.Expanded = true; return true;
                case "MINI": if (chatPanel != null) chatPanel.Expanded = false; return true;
                case "FULLSCREEN": ToggleFullScreen(); return true;
                case "MOUNTING": _ = RideAsync(); return true;
                case "NEXTTURN":
                {
                    // Banner.onShortcut : hors saisie et en combat ; placement → prêt, sinon fin du tour.
                    var fight = ActualCompte.Game?.Fight;
                    if (typing || fight == null || !fight.IsInFight || fight.IsSpectator) return false;
                    if (fight.IsPlacement) _ = ToggleReady(); else _ = PassTurn();
                    return true;
                }
                case "ESCAPE": Escape(); return true;
            }
            return false;
        }

        /// <summary>Échap : annule la visée (<c>removeCursor</c>), sinon ferme le dernier volet, sinon ouvre <c>AskMainMenu</c>.</summary>
        private void Escape()
        {
            if (mapControl?.SelectedSpellId != null) { mapControl.SelectSpell(null); return; }
            if (drawer.Current != null) { drawer.CloseCurrent(); mapArea.Focus(); return; }
            _ = banner.MainMenu.AskAsync();
        }

        /// <summary>Maj + d : monter ou descendre de sa monture (<c>Mount.ride</c> : <c>Rr</c>).</summary>
        private async Task RideAsync()
        {
            var connexion = ActualCompte.Connexion;
            if (connexion == null || !connexion.IsConnected()) { ShowActionFeedback("Connectez le personnage avant de monter sur sa monture."); return; }
            try { await connexion.SendPacket("Rr"); ShowActionFeedback("Monture : demande envoyée (Rr)."); }
            catch (Exception error) when (error is InvalidOperationException || error is ObjectDisposedException || error is System.IO.IOException || error is System.Net.Sockets.SocketException)
            { ShowActionFeedback("Envoi impossible : " + error.Message); }
        }

        // ------------------------------------------------------------------ sorts et combat

        private void SelectQuickSpell(short id)
        {
            string reason = ActualCompte.Game.Fight.GetSpellUnavailableReason(id);
            if (reason != null) { ShowActionFeedback(reason); return; }
            InGameMap();
            if (mapControl == null) { ShowActionFeedback("La carte n’est pas encore disponible."); return; }
            mapControl.SelectSpell(id); drawer.CloseAll(); mapControl.Focus(); banner.Shortcuts.RefreshContent();
        }
        private void OpenSpellDetails(short id)
        {
            var spells = drawer.Get<SpellsPanel>(); if (spells == null) return;
            drawer.Show(spells); spells.SelectSpell(id);
        }
        private void SpellSelectionChanged(short? id) { banner?.Shortcuts.RefreshContent(); RefreshStatus(); }
        private void ShowActionFeedback(string message)
        {
            BotUi.OnUi(this, () => {
                if (uiReleased) return;
                actionFeedback = message; actionFeedbackUntil = DateTime.UtcNow.AddSeconds(5);
                RefreshStatus(); toolTips.SetToolTip(summary, message);
            });
        }
        private async Task ToggleReady()
        {
            try { var result = await ActualCompte.Game.Fight.SetReadyAsync(!ActualCompte.Game.Fight.IsReady); ShowActionFeedback(result.Message); }
            catch (Exception ex) { ShowActionFeedback(ex.Message); }
        }
        private async Task PassTurn()
        {
            mapControl?.SelectSpell(null);
            try { var result = await ActualCompte.Game.Fight.PassTurnAsync(); ShowActionFeedback(result.Message); }
            catch (Exception ex) { ShowActionFeedback(ex.Message); }
        }
        private void ToggleFullScreen()
        {
            if (WindowState == FormWindowState.Maximized && FormBorderStyle == FormBorderStyle.None) {
                FormBorderStyle = FormBorderStyle.Sizable; WindowState = FormWindowState.Normal;
            } else { FormBorderStyle = FormBorderStyle.None; WindowState = FormWindowState.Maximized; }
        }
        private void UpdateMapDisplay()
        {
            if (mapControl == null) return;
            zoomText.Text = mapControl.ZoomPercent + " %";
            toolTips.SetToolTip(zoomText, mapControl.ArtworkStatus);
        }
        private void Subscribe()
        {
            ActualCompte.AccountStateEvent+=RefreshState;ActualCompte.AccountDisconnectEvent+=RefreshState;ActualCompte.Game.character.RefreshCaracteristiques+=RefreshState;ActualCompte.Game.character.Spells_Refresh+=RefreshState;ActualCompte.Game.Map.RefreshMap+=MapChanged;ActualCompte.Game.Fight.CombatChanged+=RefreshState;
        }
        private void MapChanged() { BotUi.OnUi(this,()=> { InGameMap();RefreshState(); }); }
        public void InGameMap()
        {
            if(mapControl!=null||ActualCompte.Game==null||!ActualCompte.Game.Map.HasMapData)return;
            emptyMap.Visible = false;
            mapControl = new MapControl(ActualCompte) { Dock = DockStyle.Fill };
            mapControl.ShowCellIds = showCellIds;
            mapControl.DisplayStateChanged += UpdateMapDisplay;
            mapControl.SpellSelectionChanged += SpellSelectionChanged;
            mapControl.ActionFeedback += ShowActionFeedback;
            mapControl.Router.Panels = drawer;
            Outil_Azur_complet.Bot.Menus.InteractiveMenuProvider.Attach(mapControl);
            mapArea.Controls.Add(mapControl); mapControl.SendToBack();
            if (banner != null) { banner.MapInfos.BringToFront(); banner.CenterText.BringToFront(); }
            drawer.BringToFront(); ApplyOptions(); UpdateMapDisplay();
        }
        public void displaylife() => RefreshState();

        /// <summary>État complet après un événement de la session : barre d'état, chat, volet affiché, cases de raccourcis.</summary>
        private void RefreshState()
        {
            BotUi.OnUi(this,()=> {
                if(ActualCompte.Game==null||uiReleased)return;
                RefreshStatus();
                chatPanel?.RefreshState();
                banner?.Shortcuts.RefreshContent();
            });
        }

        /// <summary>Barre d'état (chaque seconde) : session, retour d'action, visée, combat ou carte et cellule.</summary>
        private void RefreshStatus()
        {
            if(ActualCompte.Game==null||uiReleased||InvokeRequired)return;
            var c=ActualCompte.Game.character;
            string activity=StateName(ActualCompte.AccountStates);state.Text=ActualCompte.ConnectionStatus==activity?activity:ActualCompte.ConnectionStatus+" · "+activity;bool connected=ActualCompte.Connexion!=null&&ActualCompte.Connexion.IsConnected();
            var fight = ActualCompte.Game.Fight;
            combatTools.Visible = fight.IsInFight && !fight.IsSpectator; combatTools.Width = combatTools.Visible ? 135 : 0;
            ready.Visible = fight.IsPlacement; ready.Text = fight.IsReady ? "Annuler" : "Prêt";
            ready.Enabled = connected && fight.IsPlacement && !fight.IsActionPending;
            passTurn.Visible = fight.IsInFight && !fight.IsPlacement;
            passTurn.Enabled = connected && fight.IsMyTurn && !fight.IsActionPending;
            string nextTurn = ShortcutLabel("NEXTTURN");
            toolTips.SetToolTip(ready, "Confirmer ou annuler votre préparation (" + nextTurn + ")");
            toolTips.SetToolTip(passTurn, "Terminer votre tour (" + nextTurn + ")");
            var map = ActualCompte.Game.Map;
            string text = (string.IsNullOrEmpty(c.Name) ? "Personnage en cours de chargement" : c.Name + " · Niveau " + c.Level + " · " + BannerArt.Thousands(c.Kamas) + " kamas")
                + " · " + (map.LoadError ?? ("Carte " + map.MapID + " " + map.GetCoordinates + " · Cellule " + (c.Cell == null ? "?" : c.Cell.CellID.ToString(CultureInfo.InvariantCulture))));
            if (mapControl != null && mapControl.MissingAssetCount > 0) text += " · " + mapControl.MissingAssetCount + " ressource(s) absente(s)";
            if (fight.IsInFight) text = fight.IsPlacement ? "Placement · choisissez votre cellule · " + nextTurn + " : prêt" :
                (fight.IsMyTurn ? "Votre tour" : "Tour de " + fight.CurrentActorId) + " · " + fight.ActionPoints + " PA · " + fight.MovementPoints + " PM";
            if (mapControl?.SelectedSpellId != null) {
                Spell selected; if (c.Spells.TryGetValue(mapControl.SelectedSpellId.Value, out selected)) text = selected.Name + " · choisissez une cible · Échap : annuler";
            }
            if (!string.IsNullOrEmpty(actionFeedback) && DateTime.UtcNow < actionFeedbackUntil) text = actionFeedback;
            summary.Text = text;
            toolTips.SetToolTip(summary, summary.Text);
            drawer.RefreshVisible();
        }
        private static string StateName(AccountStates value)
        {
            switch(value) { case AccountStates.DISCONNECTED:return "Déconnecté";case AccountStates.CONNECTED:return "Connexion en cours";case AccountStates.CONNECTED_INACTIVE:return "Disponible";case AccountStates.MOVING:return "Déplacement";case AccountStates.FIGHTING:return "Combat";case AccountStates.GATHERING:return "Récolte";case AccountStates.DIALOG:return "Dialogue";case AccountStates.STORAGE:return "Stockage";case AccountStates.EXCHANGE:return "Échange";case AccountStates.BUYING:return "Achat";case AccountStates.SELLING:return "Vente";case AccountStates.REGENERATION:return "Régénération";case AccountStates.ZAAP:return "Zaap";default:return value.ToString(); }
        }
        private void ShowPackets() { if(FG!=null&&!FG.IsDisposed) { FG.Activate();return; }FG=new FluxForm(ActualCompte);FG.Show(); }
        private void ChangeCharacter() { var config=ActualCompte.accountConfig;ActualCompte.Disconnect();new PersoSelection(config).Show();Close(); }
        private void Disconnect() { ActualCompte.Disconnect();new LoginForm().Show();Close(); }
        private void OnSessionClosed(object sender,FormClosedEventArgs args)
        {
            if(FG!=null&&!FG.IsDisposed)FG.Close();
            ReleaseUi();
            ActualCompte.Dispose();
        }

        /// <summary>
        /// Détache l'interface de la session (minuterie, carte, bandeau, volets, options, abonnements) ; appelée à la fermeture
        /// et à la libération du formulaire, une seule fois, toujours avant la libération du compte.
        /// </summary>
        private void ReleaseUi()
        {
            if (uiReleased) return;
            uiReleased = true;
            refresh.Stop(); refresh.Dispose(); toolTips.Dispose();
            globalMenu.Dispose(); // pas dans components : la barre et le bandeau ne font que le référencer
            options.OptionChanged -= OnOptionChanged;
            if (OptionsWindow != null) optionsWindow.Close();
            if (mapControl != null) { mapControl.DisplayStateChanged -= UpdateMapDisplay; mapControl.SpellSelectionChanged -= SpellSelectionChanged; mapControl.ActionFeedback -= ShowActionFeedback; mapControl.Router.Panels = null; }
            if (banner != null)
            {
                banner.Feedback -= ShowActionFeedback; banner.Shortcuts.SpellClicked -= SelectQuickSpell; banner.Shortcuts.SpellDetailsRequested -= OpenSpellDetails;
                banner.Release();
            }
            if (drawer != null) { drawer.Feedback -= ShowActionFeedback; drawer.ReleaseSession(); }
            if (chatPanel != null) { chatPanel.ExpandedChanged -= OnChatExpanded; chatPanel.CompassRequested -= OnCompassRequested; chatPanel.Feedback -= ShowActionFeedback; chatPanel.View.ShowTimestampsChanged -= OnTimestampsToggled; chatPanel.Censor = null; chatPanel.ReleaseSession(); }
            if (ActualCompte == null) return;
            ActualCompte.AccountStateEvent-=RefreshState;ActualCompte.AccountDisconnectEvent-=RefreshState;
            var game = ActualCompte.Game; if (game == null) return;
            if (game.character != null) { game.character.RefreshCaracteristiques-=RefreshState;game.character.Spells_Refresh-=RefreshState; }
            if (game.Map != null) game.Map.RefreshMap-=MapChanged;
            if (game.Fight != null) game.Fight.CombatChanged-=RefreshState;
        }
    }
}
