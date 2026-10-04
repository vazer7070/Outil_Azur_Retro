using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Interfaces;
using Outil_Azur_complet.Bot.Controls;
using Outil_Azur_complet.Bot.Controls.Chat;
using Outil_Azur_complet.Bot.Panels;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Perso.Spells;
using Tool_BotProtocol.Utils.Logger;
namespace Outil_Azur_complet.Bot
{
    /// <summary>
    /// Fenêtre de jeu : composition seulement. Barre de menus, carte (<see cref="MapControl"/> et son routeur de clics),
    /// tiroir des volets (<see cref="Panels"/>) et bandeau bas (<see cref="HudPanel"/> : discussion <see cref="ChatPanel"/>,
    /// vie, raccourcis).
    /// Chaque fonctionnalité vit dans son volet (<c>Bot/Panels</c>) ou son fournisseur de menu (<c>Bot/Menus</c>).
    /// </summary>
    public partial class GameClientFullform : Form
    {
        public Accounts ActualCompte { get; set; }
        public Form FG;
        public List<string> DebugMessages = new List<string>();
        private Panel mapArea;
        private Label summary, state, mapStatus;
        private ChatPanel chatPanel;
        private TableLayoutPanel root;
        private Control lifeArea, shortcutsArea;
        private ProgressBar xp;
        private MapControl mapControl;
        private PanelHost drawer;
        private HudPanel hud;
        private Label zoomText, emptyMap;
        private LifeOrb life;
        private bool showGrid, showCellIds, uiReleased;
        private readonly ToolTip toolTips = new ToolTip();
        private readonly List<Button> quickSpells = new List<Button>();
        private readonly Dictionary<Button, short> quickSpellIds = new Dictionary<Button, short>();
        private readonly Dictionary<short, Bitmap> spellIcons = new Dictionary<short, Bitmap>();
        private Button previousSpellPage, nextSpellPage, ready, passTurn;
        private FlowLayoutPanel combatTools;
        private int spellPage;
        private string actionFeedback;
        private DateTime actionFeedbackUntil;
        private readonly Timer refresh = new Timer { Interval=1000 };

        /// <summary>Tiroir des volets : <c>Panels.Show(volet)</c>, <c>Toggle</c>, <c>CloseAll</c>, <c>Get&lt;T&gt;()</c>.</summary>
        public PanelHost Panels => drawer;
        /// <summary>Bandeau bas en trois emplacements (discussion, vie, raccourcis).</summary>
        public HudPanel Hud => hud;
        /// <summary>Volet de discussion du bandeau.</summary>
        public ChatPanel Chat => chatPanel;
        /// <summary>Hauteur du bandeau bas quand le chat est réduit.</summary>
        public const int HudHeight = 112;
        /// <summary>Hauteur de carte gardée quand le chat est agrandi.</summary>
        private const int MinimumMapHeight = 160;

        public GameClientFullform(Accounts account)
        {
            ActualCompte=account;BuildLayout();_ = Handle;Subscribe();Load+=(s,e)=> { RefreshState();InGameMap();refresh.Start(); };FormClosed+=OnSessionClosed;refresh.Tick+=(s,e)=>RefreshState();
        }
        private void BuildLayout()
        {
            BotUi.Prepare(this, "AzurClientRetro · " + ActualCompte.accountConfig.Account, new Size(1100, 760));
            KeyPreview = true;
            root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
                Padding = new Padding(3), BackColor = BotUi.Frame, Margin = new Padding(0) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, HudHeight));
            Controls.Add(root);
            root.Resize += (s,e) => UpdateHudHeight();

            var menu = new MenuStrip { Dock = DockStyle.Fill, BackColor = BotUi.FrameLight,
                ForeColor = BotUi.PaperLight, Font = BotFonts.Get(9), GripStyle = ToolStripGripStyle.Hidden };
            var characterMenu = new ToolStripMenuItem("Personnage");
            characterMenu.DropDownItems.Add("Caractéristiques", null, (s,e) => ShowPanel<StatsPanel>());
            characterMenu.DropDownItems.Add("Inventaire", null, (s,e) => ShowPanel<InventoryPanel>());
            characterMenu.DropDownItems.Add("Sorts", null, (s,e) => ShowPanel<SpellsPanel>());
            characterMenu.DropDownItems.Add("Métiers", null, (s,e) => ShowPanel<JobsPanel>());
            var viewMenu = new ToolStripMenuItem("Affichage");
            var grid = new ToolStripMenuItem("Afficher la grille") { CheckOnClick = true };
            grid.CheckedChanged += (s,e) => { showGrid = grid.Checked; if (mapControl != null) mapControl.ShowGrid = showGrid; };
            var cellIds = new ToolStripMenuItem("Numéros des cellules") { CheckOnClick = true };
            cellIds.CheckedChanged += (s,e) => { showCellIds = cellIds.Checked; if (mapControl != null) mapControl.ShowCellIds = showCellIds; };
            viewMenu.DropDownItems.Add(grid); viewMenu.DropDownItems.Add(cellIds);
            viewMenu.DropDownItems.Add("Ajuster la carte", null, (s,e) => mapControl?.Fit());
            viewMenu.DropDownItems.Add("Plein écran (F11)", null, (s,e) => ToggleFullScreen());
            var diagnostic = new ToolStripMenuItem("Diagnostic");
            diagnostic.DropDownItems.Add("Journal de session", null, (s,e) => ShowPanel<JournalPanel>());
            diagnostic.DropDownItems.Add("Journal des paquets", null, (s,e) => ShowPackets());
            var session = new ToolStripMenuItem("Session");
            session.DropDownItems.Add("Changer de personnage", null, (s,e) => ChangeCharacter());
            session.DropDownItems.Add("Déconnexion", null, (s,e) => Disconnect());
            menu.Items.AddRange(new ToolStripItem[] { characterMenu, viewMenu, diagnostic, session });
            state = BotUi.Label("Déconnecté", 9); state.ForeColor = BotUi.Gold; state.Height = 25;
            var stateHost = new ToolStripControlHost(state) { Alignment = ToolStripItemAlignment.Right,
                AutoSize = false, Width = 300, Height = 25 };
            menu.Items.Add(stateHost); root.Controls.Add(menu, 0, 0); MainMenuStrip = menu;

            var world = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = BotUi.Frame };
            root.Controls.Add(world, 0, 1);
            var mapBar = new Panel { Dock = DockStyle.Top, Height = 24, BackColor = BotUi.FrameLight };
            mapStatus = BotUi.Label("En attente de la carte…", 9); mapStatus.Dock = DockStyle.Fill;
            mapStatus.ForeColor = BotUi.PaperLight; mapStatus.TextAlign = ContentAlignment.MiddleLeft;
            mapStatus.AutoEllipsis = true; mapStatus.Padding = new Padding(7, 0, 0, 0);
            var zoomTools = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 218, WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0), Margin = new Padding(0) };
            zoomText = BotUi.Label("100 %", 8); zoomText.ForeColor = BotUi.Gold; zoomText.Width = 52;
            zoomText.Height = 23; zoomText.TextAlign = ContentAlignment.MiddleCenter;
            zoomTools.Controls.Add(MiniButton("−", (s,e) => mapControl?.ZoomOut(), 30));
            zoomTools.Controls.Add(zoomText);
            zoomTools.Controls.Add(MiniButton("+", (s,e) => mapControl?.ZoomIn(), 30));
            zoomTools.Controls.Add(MiniButton("Adapter", (s,e) => mapControl?.Fit(), 78));
            mapBar.Controls.Add(mapStatus); mapBar.Controls.Add(zoomTools);
            combatTools = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 0, WrapContents = false, Visible = false, Margin = new Padding(0) };
            ready = MiniButton("Prêt", async (s,e) => await ToggleReady(), 62); ((ClientButton)ready).Primary = true;
            passTurn = MiniButton("Passer", async (s,e) => await PassTurn(), 67);
            ((ClientButton)passTurn).Glyph = ClientAssets.Icon("tour-suivant-haut", 18); // flèche de fin de tour du client
            ready.AccessibleName = "Prêt pour le combat (F1)"; passTurn.AccessibleName = "Passer le tour (F2)";
            toolTips.SetToolTip(ready, "Confirmer ou annuler votre préparation (F1)");
            toolTips.SetToolTip(passTurn, "Terminer votre tour (F2)");
            combatTools.Controls.Add(ready); combatTools.Controls.Add(passTurn); mapBar.Controls.Add(combatTools);
            mapArea = new Panel { Dock = DockStyle.Fill, BackColor = BotUi.Frame };
            emptyMap = BotUi.Label("La carte apparaît après la sélection du personnage.\nLes ressources absentes sont indiquées ici.", 11);
            emptyMap.Dock = DockStyle.Fill; emptyMap.ForeColor = BotUi.Gold;
            emptyMap.TextAlign = ContentAlignment.MiddleCenter; mapArea.Controls.Add(emptyMap);
            world.Controls.Add(mapArea); world.Controls.Add(mapBar);
            BuildDrawer(mapArea);

            hud = new HudPanel { Dock = DockStyle.Fill };
            root.Controls.Add(hud, 0, 2);
            BuildChat(hud);
            BuildLife(hud);
            BuildShortcuts(hud);

            KeyDown += (s,e) => {
                if (e.KeyCode == Keys.Escape) { mapControl?.SelectSpell(null); drawer.CloseCurrent(); mapArea.Focus(); e.Handled = true; }
                else if (e.KeyCode == Keys.F11) { ToggleFullScreen(); e.Handled = true; }
                else if (!(ActiveControl is TextBoxBase) && !(ActiveControl is ComboBox)) {
                    if(e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D9) { SelectQuickSpell(quickSpells[(int)e.KeyCode-(int)Keys.D1]); e.Handled=e.SuppressKeyPress=true; }
                    else if(e.KeyCode == Keys.D0) { SelectQuickSpell(quickSpells[9]); e.Handled=e.SuppressKeyPress=true; }
                    else if(e.KeyCode == Keys.F1) { _ = ToggleReady(); e.Handled=true; }
                    else if(e.KeyCode == Keys.F2) { _ = PassTurn(); e.Handled=true; }
                    else if (e.KeyCode == Keys.C) TogglePanel<StatsPanel>();
                    else if (e.KeyCode == Keys.I) TogglePanel<InventoryPanel>();
                    else if (e.KeyCode == Keys.S) TogglePanel<SpellsPanel>();
                    else if (e.KeyCode == Keys.J) TogglePanel<JobsPanel>();
                }
            };
        }

        private void BuildChat(HudPanel host)
        {
            chatPanel = new ChatPanel(ActualCompte, () => mapControl?.Router) { Dock = DockStyle.Fill };
            chatPanel.ExpandedChanged += OnChatExpanded;
            chatPanel.CompassRequested += OnCompassRequested;
            chatPanel.Feedback += ShowActionFeedback;
            host.SetSlot(HudSlot.Left, chatPanel);
        }

        /// <summary>Chat agrandi : le bandeau gagne jusqu'à 350 pixels (<c>OPEN_OFFSET</c>) pris sur la carte ; vie et raccourcis restent en bas.</summary>
        private void OnChatExpanded(object sender, EventArgs e) => UpdateHudHeight();
        private void UpdateHudHeight()
        {
            if (root == null || root.RowStyles.Count < 3 || chatPanel == null) return;
            int extra = 0;
            if (chatPanel.Expanded)
                extra = Math.Max(0, Math.Min(ChatPanel.ExpandOffset, root.ClientSize.Height - root.Padding.Vertical - 28 - HudHeight - MinimumMapHeight));
            if ((int)root.RowStyles[2].Height == HudHeight + extra) return;
            foreach (Control part in new[] { lifeArea, shortcutsArea }) {
                if (part == null) continue;
                if (extra > 0 && part.Dock == DockStyle.Fill) { int height = part.Height; part.Dock = DockStyle.Bottom; part.Height = height; }
                else if (extra == 0) part.Dock = DockStyle.Fill;
            }
            root.RowStyles[2].Height = HudHeight + extra;
        }
        private void OnCompassRequested(int x, int y) => ShowActionFeedback(ChatLinks.DescribeCompass(x, y, ActualCompte.Game?.Map));

        private void BuildLife(HudPanel host)
        {
            lifeArea = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            life = new LifeOrb { Dock = DockStyle.Fill, Font = new Font("Tahoma", 8) };
            xp = new ProgressBar { Dock = DockStyle.Bottom, Height = 5, Maximum = 100, Style = ProgressBarStyle.Continuous };
            lifeArea.Controls.Add(life); lifeArea.Controls.Add(xp);
            host.CenterSlot.Padding = new Padding(3, 0, 3, 0); // marge de l'ancienne cellule centrale
            host.SetSlot(HudSlot.Center, lifeArea);
        }

        private void BuildShortcuts(HudPanel host)
        {
            var shortcuts = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0) };
            shortcuts.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            shortcuts.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
            shortcuts.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var icons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            var resources = new System.ComponentModel.ComponentResourceManager(typeof(GameClientFullform));
            // Icônes du bandeau du client fourni (UI_Banner*Icon de core.swf) ; celles du .resx servent de repli.
            icons.Controls.Add(IconButton(resources, "roundedButton9.Image", "icone-caracteristiques", "Caractéristiques (C)", (s,e) => TogglePanel<StatsPanel>()));
            icons.Controls.Add(IconButton(resources, "roundedButton1.Image", "icone-sorts", "Sorts (S)", (s,e) => TogglePanel<SpellsPanel>()));
            icons.Controls.Add(IconButton(resources, "roundedButton2.Image", "icone-inventaire", "Inventaire (I)", (s,e) => TogglePanel<InventoryPanel>()));
            icons.Controls.Add(IconButton(resources, "roundedButton3.Image", "icone-quetes", "Quêtes : interface à compléter", null));
            icons.Controls.Add(IconButton(resources, "roundedButton4.Image", "icone-carte", "Géoposition : ajuster la carte", (s,e) => mapControl?.Fit()));
            icons.Controls.Add(IconButton(resources, "roundedButton5.Image", "icone-amis", "Amis", (s,e) => TogglePanel<FriendsPanel>()));
            icons.Controls.Add(IconButton(resources, "roundedButton6.Image", "icone-guilde", "Guilde : interface à compléter", null));
            icons.Controls.Add(IconButton(resources, "roundedButton7.Image", "icone-monture", "Monture : interface à compléter", null));
            icons.Controls.Add(IconButton(resources, "roundedButton8.Image", "icone-pvp", "Conquête : interface à compléter", null));
            shortcuts.Controls.Add(icons, 0, 0);
            summary = BotUi.Label("Personnage en cours de chargement", 8); summary.Dock = DockStyle.Fill;
            summary.ForeColor = BotUi.Gold; summary.AutoEllipsis = true; shortcuts.Controls.Add(summary, 0, 1);
            var slots = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0,2,0,0) };
            previousSpellPage = MiniButton("‹", (s,e) => ChangeSpellPage(-1), 24); previousSpellPage.Height = 30;
            previousSpellPage.AccessibleName = "Page précédente de sorts"; slots.Controls.Add(previousSpellPage);
            for (int i = 0; i < 10; i++) {
                var slot = new SpellShortcutButton { Font = new Font("Tahoma",7), Shortcut = i == 9 ? "0" : (i+1).ToString(), Margin = new Padding(1,0,1,0) };
                slot.Click += (s,e) => SelectQuickSpell((Button)s);
                slot.MouseUp += (s,e) => { if(e.Button == MouseButtons.Right) OpenSpellDetails((Button)s); };
                slot.Enabled = false; quickSpells.Add(slot); slots.Controls.Add(slot);
            }
            nextSpellPage = MiniButton("›", (s,e) => ChangeSpellPage(1), 24); nextSpellPage.Height = 30;
            nextSpellPage.AccessibleName = "Page suivante de sorts"; slots.Controls.Add(nextSpellPage);
            shortcuts.Controls.Add(slots, 0, 2);
            host.SetSlot(HudSlot.Right, shortcuts); shortcutsArea = shortcuts;
        }

        /// <summary>Tiroir des volets dans la zone de la carte ; les volets d'origine y sont enregistrés (masqués).</summary>
        private void BuildDrawer(Panel host)
        {
            drawer = new PanelHost { Width = 410, Anchor = AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom };
            drawer.Account = ActualCompte;
            drawer.Feedback += ShowActionFeedback;
            drawer.PanelShown += (s,e) => LayoutDrawer();
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
        private void TogglePanel<T>() where T : class, IGamePanel => drawer.Toggle(drawer.Get<T>());
        private Button MiniButton(string title, EventHandler click, int width)
        {
            var button = (Button)BotUi.Button(title, click, false, width);
            button.Height = 24; button.Font = new Font("Tahoma",8); button.Tag = "client-icon"; button.Margin = new Padding(1,0,1,0);
            return button;
        }
        private Button IconButton(System.ComponentModel.ComponentResourceManager resources, string imageKey, string clientIcon, string title, EventHandler click)
        {
            var button = new Controls.buttons.RoundedButton { Width = 33, Height = 32, FlatStyle = FlatStyle.Flat,
                Image = ClientAssets.Icon(clientIcon, 24) ?? resources.GetObject(imageKey) as Image, BackColor = BotUi.Paper,
                Margin = new Padding(2,0,2,0), Cursor = Cursors.Hand, Tag = "client-icon",
                AccessibleName = title, Enabled = click != null };
            button.FlatAppearance.BorderColor = BotUi.Gold;
            if (click != null) button.Click += click;
            toolTips.SetToolTip(button, title); return button;
        }
        private void UpdateQuickSpells()
        {
            var learned = ActualCompte.Game.character.Spells.Values.OrderBy(SpellPosition).ThenBy(x => x.ID).ToArray();
            int pages = Math.Max(1, (learned.Length + quickSpells.Count - 1) / quickSpells.Count);
            spellPage = Math.Max(0, Math.Min(pages - 1, spellPage));
            previousSpellPage.Visible = nextSpellPage.Visible = pages > 1;
            previousSpellPage.Enabled = spellPage > 0; nextSpellPage.Enabled = spellPage < pages - 1;
            toolTips.SetToolTip(previousSpellPage, "Page " + (spellPage + 1) + " / " + pages + " · page précédente");
            toolTips.SetToolTip(nextSpellPage, "Page " + (spellPage + 1) + " / " + pages + " · page suivante");
            for (int i=0;i<quickSpells.Count;i++) {
                var slot = (SpellShortcutButton)quickSpells[i];
                int index = spellPage * quickSpells.Count + i;
                slot.Occupied = index < learned.Length;
                if (!slot.Occupied) {
                    slot.Enabled = false; slot.Icon = null; slot.SelectedSpell = false; slot.Fallback = null;
                    quickSpellIds.Remove(slot); slot.AccessibleName = "Emplacement de sort vide";
                    toolTips.SetToolTip(slot, "Emplacement de sort vide"); slot.Invalidate(); continue;
                }
                Spell spell = learned[index]; quickSpellIds[slot] = spell.ID; slot.Enabled = true;
                slot.Icon = GetSpellIcon(spell.ID); slot.Fallback = spell.ID.ToString();
                slot.SelectedSpell = mapControl?.SelectedSpellId == spell.ID;
                string reason = ActualCompte.Game.Fight.GetSpellUnavailableReason(spell.ID);
                slot.Available = reason == null;
                slot.AccessibleName = (spell.Name ?? "Sort #" + spell.ID) + " · niveau " + spell.Level;
                SpellStats data = spell.GetStats();
                string cost = data == null ? "Données du sort absentes" : data.PA + " PA · portée " + data.Min_portee + "–" + data.Max_portee;
                toolTips.SetToolTip(slot, slot.AccessibleName + "\n" + cost + "\n" + (reason ?? "Cliquer puis choisir une cellule sur la carte") + "\nClic droit : fiche du sort · Échap : annuler");
                slot.Invalidate();
            }
        }
        private static int SpellPosition(Spell spell)
        {
            if (string.IsNullOrEmpty(spell.Position) || spell.Position == "_") return int.MaxValue;
            int index = Array.IndexOf(Tool_BotProtocol.Utils.Crypto.Hash.caracteres_array, spell.Position[0]);
            return index < 0 ? int.MaxValue : index;
        }
        private Bitmap GetSpellIcon(short id)
        {
            Bitmap bitmap = null;
            if (spellIcons.TryGetValue(id, out bitmap)) return bitmap;
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ressources", "Bot", "sorts", id + ".png");
            try {
                if (File.Exists(path)) using (var source = Image.FromFile(path)) bitmap = new Bitmap(source);
            }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is System.Runtime.InteropServices.ExternalException) { bitmap = null; }
            spellIcons[id] = bitmap; return bitmap;
        }
        private void SelectQuickSpell(Button slot)
        {
            short id; if (!quickSpellIds.TryGetValue(slot,out id)) return;
            string reason = ActualCompte.Game.Fight.GetSpellUnavailableReason(id);
            if (reason != null) { ShowActionFeedback(reason); return; }
            InGameMap();
            if (mapControl == null) { ShowActionFeedback("La carte n’est pas encore disponible."); return; }
            mapControl.SelectSpell(id); drawer.CloseAll(); mapControl.Focus(); UpdateQuickSpells();
        }
        private void OpenSpellDetails(Button slot)
        {
            short id; if (!quickSpellIds.TryGetValue(slot,out id)) return;
            var spells = drawer.Get<SpellsPanel>(); if (spells == null) return;
            drawer.Show(spells); spells.SelectSpell(id);
        }
        private void ChangeSpellPage(int direction)
        {
            mapControl?.SelectSpell(null); spellPage += direction; UpdateQuickSpells();
        }
        private void SpellSelectionChanged(short? id) => RefreshState();
        private void ShowActionFeedback(string message)
        {
            BotUi.OnUi(this, () => {
                actionFeedback = message; actionFeedbackUntil = DateTime.UtcNow.AddSeconds(5);
                RefreshState(); toolTips.SetToolTip(summary, message);
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
            toolTips.SetToolTip(mapStatus, mapControl.ArtworkStatus);
        }
        private void Subscribe()
        {
            ActualCompte.AccountStateEvent+=RefreshState;ActualCompte.AccountDisconnectEvent+=RefreshState;ActualCompte.Game.character.SeeLifeRegen+=displaylife;ActualCompte.Game.character.RefreshCaracteristiques+=RefreshState;ActualCompte.Game.character.Spells_Refresh+=RefreshState;ActualCompte.Game.Map.RefreshMap+=MapChanged;ActualCompte.Game.Fight.CombatChanged+=RefreshState;
        }
        private void MapChanged() { BotUi.OnUi(this,()=> { InGameMap();RefreshState(); }); }
        public void InGameMap()
        {
            if(mapControl!=null||ActualCompte.Game==null||!ActualCompte.Game.Map.HasMapData)return;
            emptyMap.Visible = false;
            mapControl = new MapControl(ActualCompte) { Dock = DockStyle.Fill };
            mapControl.ShowGrid = showGrid; mapControl.ShowCellIds = showCellIds;
            mapControl.DisplayStateChanged += UpdateMapDisplay;
            mapControl.SpellSelectionChanged += SpellSelectionChanged;
            mapControl.ActionFeedback += ShowActionFeedback;
            mapControl.Router.Panels = drawer;
            Outil_Azur_complet.Bot.Menus.InteractiveMenuProvider.Attach(mapControl);
            mapArea.Controls.Add(mapControl); mapControl.SendToBack(); drawer.BringToFront(); UpdateMapDisplay();
        }
        public void displaylife() => RefreshState();
        private void RefreshState()
        {
            BotUi.OnUi(this,()=> {
                if(ActualCompte.Game==null||uiReleased)return;var c=ActualCompte.Game.character;var s=c.stats;
                summary.Text=(string.IsNullOrEmpty(c.Name)?"Personnage en cours de chargement":c.Name)+" · Niveau "+c.Level+" · Vie "+s.VitalityActual+"/"+s.MaxVitality+" · Kamas "+c.Kamas;
                life.UpdateLife(s.VitalityActual, s.MaxVitality, c.Level);
                toolTips.SetToolTip(life, "Vie : " + s.VitalityActual + "/" + s.MaxVitality + "\nNiveau : " + c.Level);
                string activity=StateName(ActualCompte.AccountStates);state.Text=ActualCompte.ConnectionStatus==activity?activity:ActualCompte.ConnectionStatus+" · "+activity;bool connected=ActualCompte.Connexion!=null&&ActualCompte.Connexion.IsConnected();
                chatPanel?.RefreshState();
                var fight = ActualCompte.Game.Fight;
                combatTools.Visible = fight.IsInFight && !fight.IsSpectator; combatTools.Width = combatTools.Visible ? 135 : 0;
                ready.Visible = fight.IsPlacement; ready.Text = fight.IsReady ? "Annuler" : "Prêt";
                ready.Enabled = connected && fight.IsPlacement && !fight.IsActionPending;
                passTurn.Visible = fight.IsInFight && !fight.IsPlacement;
                passTurn.Enabled = connected && fight.IsMyTurn && !fight.IsActionPending;
                if (fight.IsInFight) summary.Text = fight.IsPlacement ? "Placement · choisissez votre cellule · F1 : prêt" :
                    (fight.IsMyTurn ? "Votre tour" : "Tour de " + fight.CurrentActorId) + " · " + fight.ActionPoints + " PA · " + fight.MovementPoints + " PM";
                if (mapControl?.SelectedSpellId != null) {
                    Spell selected; if (c.Spells.TryGetValue(mapControl.SelectedSpellId.Value, out selected)) summary.Text = selected.Name + " · choisissez une cible · Échap : annuler";
                }
                if (!string.IsNullOrEmpty(actionFeedback) && DateTime.UtcNow < actionFeedbackUntil) summary.Text = actionFeedback;
                toolTips.SetToolTip(summary, summary.Text);
                double span=s.ExpNivNext-s.MinExpNiv;xp.Value=span>0?Math.Max(0,Math.Min(100,(int)((s.ActualEXP-s.MinExpNiv)/span*100))):0;
                mapStatus.Text=ActualCompte.Game.Map.LoadError??("Carte "+ActualCompte.Game.Map.MapID+" · "+ActualCompte.Game.Map.GetCoordinates+" · Cellule "+(c.Cell==null?"?":c.Cell.CellID.ToString()));
                if (mapControl != null && mapControl.MissingAssetCount > 0) mapStatus.Text += " · " + mapControl.MissingAssetCount + " ressource(s) absente(s)";
                UpdateQuickSpells();
                drawer.RefreshVisible();
            });
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
        /// Détache l'interface de la session (minuterie, carte, volets, abonnements) ; appelée à la fermeture et à la
        /// libération du formulaire, une seule fois, toujours avant la libération du compte.
        /// </summary>
        private void ReleaseUi()
        {
            if (uiReleased) return;
            uiReleased = true;
            refresh.Stop(); refresh.Dispose(); toolTips.Dispose();
            if (mapControl != null) { mapControl.DisplayStateChanged -= UpdateMapDisplay; mapControl.SpellSelectionChanged -= SpellSelectionChanged; mapControl.ActionFeedback -= ShowActionFeedback; mapControl.Router.Panels = null; }
            foreach (var icon in spellIcons.Values) icon?.Dispose(); spellIcons.Clear();
            if (drawer != null) { drawer.Feedback -= ShowActionFeedback; drawer.ReleaseSession(); }
            if (chatPanel != null) { chatPanel.ExpandedChanged -= OnChatExpanded; chatPanel.CompassRequested -= OnCompassRequested; chatPanel.Feedback -= ShowActionFeedback; chatPanel.ReleaseSession(); }
            if (ActualCompte == null) return;
            ActualCompte.AccountStateEvent-=RefreshState;ActualCompte.AccountDisconnectEvent-=RefreshState;
            var game = ActualCompte.Game; if (game == null) return;
            if (game.character != null) { game.character.SeeLifeRegen-=displaylife;game.character.RefreshCaracteristiques-=RefreshState;game.character.Spells_Refresh-=RefreshState; }
            if (game.Map != null) game.Map.RefreshMap-=MapChanged;
            if (game.Fight != null) game.Fight.CombatChanged-=RefreshState;
        }
    }
}
