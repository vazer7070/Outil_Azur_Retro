using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Interfaces;
using Outil_Azur_complet.Bot.Controls;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Perso.Stats;
using Tool_BotProtocol.Game.Perso.Spells;
using Tool_BotProtocol.Utils.Logger;
namespace Outil_Azur_complet.Bot
{
    public partial class GameClientFullform : Form
    {
        public Accounts ActualCompte { get; set; }
        public Form FG;
        public List<string> DebugMessages = new List<string>();
        public bool GeneralTchat=true, RecruitTchat, MarchandTchat, AlignTchat, GuildeTchat, GroupTchat, TeamTchat, AdminTchat, PMTchat;
        public string WhoPM;
        private Panel mapArea;
        private Label summary, state, mapStatus;
        private RichTextBox chat, journal;
        private TextBox chatInput, recipient;
        private ComboBox channel;
        private TableLayoutPanel composer;
        private Panel privateRecipientBar;
        private Control send;
        private ProgressBar xp;
        private ListView stats, inventory, jobs, spells;
        private ComboBox boostStat;
        private Label boostHelp;
        private Control boost;
        private bool boosting;
        private Control upgradeSpell;
        private Label spellHelp;
        private bool upgradingSpell;
        private MapControl mapControl;
        private ClientPanel drawer;
        private TabControl panels;
        private Label zoomText, emptyMap;
        private LifeOrb life;
        private bool showGrid, showCellIds;
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
        public GameClientFullform(Accounts account)
        {
            ActualCompte=account;BuildLayout();_ = Handle;Subscribe();Load+=(s,e)=> { RefreshState();InGameMap();refresh.Start(); };FormClosed+=OnSessionClosed;refresh.Tick+=(s,e)=>RefreshState();
        }
        private void BuildLayout()
        {
            BotUi.Prepare(this, "AzurClientRetro · " + ActualCompte.accountConfig.Account, new Size(1100, 760));
            KeyPreview = true;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
                Padding = new Padding(3), BackColor = BotUi.Frame, Margin = new Padding(0) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
            Controls.Add(root);

            var menu = new MenuStrip { Dock = DockStyle.Fill, BackColor = BotUi.FrameLight,
                ForeColor = BotUi.PaperLight, Font = new Font("Tahoma", 9), GripStyle = ToolStripGripStyle.Hidden };
            var characterMenu = new ToolStripMenuItem("Personnage");
            characterMenu.DropDownItems.Add("Caractéristiques", null, (s,e) => ShowPanel(0));
            characterMenu.DropDownItems.Add("Inventaire", null, (s,e) => ShowPanel(1));
            characterMenu.DropDownItems.Add("Sorts", null, (s,e) => ShowPanel(2));
            characterMenu.DropDownItems.Add("Métiers", null, (s,e) => ShowPanel(3));
            var viewMenu = new ToolStripMenuItem("Affichage");
            var grid = new ToolStripMenuItem("Afficher la grille") { CheckOnClick = true };
            grid.CheckedChanged += (s,e) => { showGrid = grid.Checked; if (mapControl != null) mapControl.ShowGrid = showGrid; };
            var cellIds = new ToolStripMenuItem("Numéros des cellules") { CheckOnClick = true };
            cellIds.CheckedChanged += (s,e) => { showCellIds = cellIds.Checked; if (mapControl != null) mapControl.ShowCellIds = showCellIds; };
            viewMenu.DropDownItems.Add(grid); viewMenu.DropDownItems.Add(cellIds);
            viewMenu.DropDownItems.Add("Ajuster la carte", null, (s,e) => mapControl?.Fit());
            viewMenu.DropDownItems.Add("Plein écran (F11)", null, (s,e) => ToggleFullScreen());
            var diagnostic = new ToolStripMenuItem("Diagnostic");
            diagnostic.DropDownItems.Add("Journal de session", null, (s,e) => ShowPanel(4));
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
            ready = MiniButton("Prêt", async (s,e) => await ToggleReady(), 62);
            passTurn = MiniButton("Passer", async (s,e) => await PassTurn(), 67);
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

            var hud = new ClientPanel { Dock = DockStyle.Fill, BackColor = BotUi.FrameLight,
                Padding = new Padding(7), Margin = new Padding(0) };
            var hudLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0) };
            hudLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
            hudLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
            hudLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
            hud.Controls.Add(hudLayout); root.Controls.Add(hud, 0, 2);
            BuildChat(hudLayout);

            var lifeArea = new Panel { Dock = DockStyle.Fill, Margin = new Padding(3, 0, 3, 0) };
            life = new LifeOrb { Dock = DockStyle.Fill, Font = new Font("Tahoma", 8) };
            xp = new ProgressBar { Dock = DockStyle.Bottom, Height = 5, Maximum = 100, Style = ProgressBarStyle.Continuous };
            lifeArea.Controls.Add(life); lifeArea.Controls.Add(xp); hudLayout.Controls.Add(lifeArea, 1, 0);

            var shortcuts = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0) };
            shortcuts.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            shortcuts.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
            shortcuts.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var icons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            var resources = new System.ComponentModel.ComponentResourceManager(typeof(GameClientFullform));
            icons.Controls.Add(IconButton(resources, "roundedButton9.Image", "Caractéristiques (C)", (s,e) => ShowPanel(0)));
            icons.Controls.Add(IconButton(resources, "roundedButton1.Image", "Sorts (S)", (s,e) => ShowPanel(2)));
            icons.Controls.Add(IconButton(resources, "roundedButton2.Image", "Inventaire (I)", (s,e) => ShowPanel(1)));
            icons.Controls.Add(IconButton(resources, "roundedButton3.Image", "Quêtes : interface à compléter", null));
            icons.Controls.Add(IconButton(resources, "roundedButton4.Image", "Géoposition : ajuster la carte", (s,e) => mapControl?.Fit()));
            icons.Controls.Add(IconButton(resources, "roundedButton5.Image", "Amis : interface à compléter", null));
            icons.Controls.Add(IconButton(resources, "roundedButton6.Image", "Guilde : interface à compléter", null));
            icons.Controls.Add(IconButton(resources, "roundedButton7.Image", "Monture : interface à compléter", null));
            icons.Controls.Add(IconButton(resources, "roundedButton8.Image", "Conquête : interface à compléter", null));
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
            shortcuts.Controls.Add(slots, 0, 2); hudLayout.Controls.Add(shortcuts, 2, 0);

            KeyDown += (s,e) => {
                if (e.KeyCode == Keys.Escape) { mapControl?.SelectSpell(null); drawer.Visible = false; mapArea.Focus(); e.Handled = true; }
                else if (e.KeyCode == Keys.F11) { ToggleFullScreen(); e.Handled = true; }
                else if (!(ActiveControl is TextBoxBase) && !(ActiveControl is ComboBox)) {
                    if(e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D9) { SelectQuickSpell(quickSpells[(int)e.KeyCode-(int)Keys.D1]); e.Handled=e.SuppressKeyPress=true; }
                    else if(e.KeyCode == Keys.D0) { SelectQuickSpell(quickSpells[9]); e.Handled=e.SuppressKeyPress=true; }
                    else if(e.KeyCode == Keys.F1) { _ = ToggleReady(); e.Handled=true; }
                    else if(e.KeyCode == Keys.F2) { _ = PassTurn(); e.Handled=true; }
                    else if (e.KeyCode == Keys.C) ShowPanel(0);
                    else if (e.KeyCode == Keys.I) ShowPanel(1);
                    else if (e.KeyCode == Keys.S) ShowPanel(2);
                    else if (e.KeyCode == Keys.J) ShowPanel(3);
                }
            };
        }

        private void BuildChat(TableLayoutPanel hud)
        {
            var chatArea = new ClientPanel { Dock = DockStyle.Fill, BackColor = BotUi.Paper, Padding = new Padding(5),
                Margin = new Padding(0) };
            chat = BotUi.Journal(); chat.Font = new Font("Tahoma", 8);
            composer = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 29, ColumnCount = 4,
                RowCount = 1, Margin = new Padding(0), Padding = new Padding(0, 3, 0, 0) };
            composer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
            composer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0));
            composer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            composer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));
            channel = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Tahoma", 8), BackColor = BotUi.PaperLight, ForeColor = BotUi.Ink, Margin = new Padding(0,0,3,0) };
            channel.Items.AddRange(new object[] { "Général", "Recrutement", "Commerce", "Guilde", "Groupe", "Privé" });
            channel.SelectedIndex = 0;
            recipient = BotUi.Input("Destinataire"); recipient.Dock = DockStyle.Fill; recipient.Font = channel.Font;
            recipient.Margin = new Padding(0, 0, 3, 0);
            privateRecipientBar = new Panel { Dock = DockStyle.Bottom, Height = 25, Padding = new Padding(0,1,0,1) };
            var recipientLabel = BotUi.Label("À :", 8); recipientLabel.Dock = DockStyle.Left; recipientLabel.Width = 23;
            privateRecipientBar.Controls.Add(recipient); privateRecipientBar.Controls.Add(recipientLabel);
            chatInput = BotUi.Input("Message"); chatInput.Dock = DockStyle.Fill; chatInput.Font = channel.Font;
            chatInput.Margin = new Padding(0, 0, 3, 0); chatInput.MaxLength = 250;
            send = MiniButton("›", async (s,e) => await SpeakInTchat(), 32);
            var sendImage = new System.ComponentModel.ComponentResourceManager(typeof(GameClientFullform)).GetObject("iTalk_Button_21.Image") as Image;
            if (sendImage != null) { ((Button)send).Image = sendImage; send.Text = ""; }
            send.Dock = DockStyle.Fill; send.Margin = new Padding(0); send.AccessibleName = "Envoyer le message";
            toolTips.SetToolTip(send, "Envoyer (Entrée)"); toolTips.SetToolTip(recipient, "Nom du destinataire du message privé");
            channel.SelectedIndexChanged += (s,e) => { ChangeChannel(); UpdateRecipientVisibility(); };
            chatInput.KeyDown += async (s,e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await SpeakInTchat(); } };
            composer.Controls.Add(channel,0,0);
            composer.Controls.Add(chatInput,2,0); composer.Controls.Add(send,3,0);
            UpdateRecipientVisibility();
            chatArea.Controls.Add(chat); chatArea.Controls.Add(privateRecipientBar); chatArea.Controls.Add(composer); hud.Controls.Add(chatArea,0,0);
        }

        private void BuildDrawer(Panel host)
        {
            drawer = new ClientPanel { Width = 410, BackColor = BotUi.Paper, Padding = new Padding(9), Visible = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom };
            var top = new Panel { Dock = DockStyle.Top, Height = 28 };
            var title = BotUi.Label("Personnage", 11, true); title.Dock = DockStyle.Fill;
            var close = MiniButton("×", (s,e) => ClosePanel(), 28); close.Dock = DockStyle.Right;
            close.AccessibleName = "Fermer le panneau"; top.Controls.Add(title); top.Controls.Add(close);
            panels = new TabControl { Dock = DockStyle.Fill, Font = new Font("Tahoma", 9) };
            stats = AddList(panels, "Caractéristiques", "Caractéristique", "Valeur");
            inventory = AddList(panels, "Inventaire", "Objet", "Qté", "Position");
            spells = AddList(panels, "Sorts", "Sort", "Niveau");
            jobs = AddList(panels, "Métiers", "Métier", "Niveau");
            var logPage = new TabPage("Journal") { BackColor = BotUi.Paper, Padding = new Padding(7) };
            journal = BotUi.Journal(); logPage.Controls.Add(journal); panels.TabPages.Add(logPage);

            boostStat = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList,
                Font = Font, BackColor = BotUi.PaperLight, ForeColor = BotUi.Ink };
            boostStat.Items.AddRange(new object[] { "Vitalité", "Sagesse", "Force", "Intelligence", "Chance", "Agilité" });
            boostStat.SelectedIndex = 0; boostStat.SelectedIndexChanged += (s,e) => RefreshState();
            var allocation = new Panel { Dock = DockStyle.Bottom, Height = 142, Padding = new Padding(0,5,0,0), BackColor = BotUi.Paper };
            var selectLabel = BotUi.Label("Répartir les points de caractéristiques", 9, true); selectLabel.Dock = DockStyle.Top; selectLabel.Height = 23;
            boostHelp = BotUi.Status(""); boostHelp.Height = 40;
            boost = BotUi.Button("Augmenter", async (s,e) => await Boost(), true, 125);
            allocation.Controls.Add(boostStat); allocation.Controls.Add(selectLabel);
            allocation.Controls.Add(boostHelp); allocation.Controls.Add(BotUi.Actions(boost)); stats.Parent.Controls.Add(allocation);
            spellHelp = BotUi.Status("Sélectionnez un sort à améliorer."); spellHelp.Height = 66;
            upgradeSpell = BotUi.Button("Améliorer de 1 niveau", async (s,e) => await UpgradeSpell(), true, 210);
            spells.Parent.Controls.Add(spellHelp); spells.Parent.Controls.Add(BotUi.Actions(upgradeSpell));
            spells.SelectedIndexChanged += (s,e) => UpdateSpellSelection();
            BuildInteractionPanels();
            drawer.Controls.Add(panels); drawer.Controls.Add(top); host.Controls.Add(drawer); drawer.BringToFront();
            host.Resize += (s,e) => LayoutDrawer(); LayoutDrawer();
        }
        private ListView AddList(TabControl tabs, string title, params string[] columns)
        {
            var page = new TabPage(title) { BackColor = BotUi.Paper, Padding = new Padding(7) };
            var list = BotUi.List(columns); list.Font = new Font("Tahoma",9);
            list.Columns[0].Width = columns.Length > 2 ? 165 : 236;
            if (list.Columns.Count > 1) list.Columns[1].Width = columns.Length > 2 ? 48 : 94;
            if (list.Columns.Count > 2) list.Columns[2].Width = 120;
            page.Controls.Add(list); tabs.TabPages.Add(page); return list;
        }
        private void LayoutDrawer()
        {
            if (drawer == null) return;
            int width = Math.Min(410, Math.Max(330, mapArea.Width - 350));
            drawer.SetBounds(Math.Max(0, mapArea.Width - width - 6), 6, width, Math.Max(1, mapArea.Height - 12));
        }
        public void ShowPanel(int index)
        {
            if (index < 0 || index >= panels.TabPages.Count) return;
            panels.SelectedIndex = index; drawer.Visible = true; LayoutDrawer(); drawer.BringToFront(); RefreshState();
        }
        public void ClosePanel() { drawer.Visible = false; mapArea.Focus(); }
        private Button MiniButton(string title, EventHandler click, int width)
        {
            var button = (Button)BotUi.Button(title, click, false, width);
            button.Height = 24; button.Font = new Font("Tahoma",8); button.Tag = "client-icon"; button.Margin = new Padding(1,0,1,0);
            return button;
        }
        private Button IconButton(System.ComponentModel.ComponentResourceManager resources, string imageKey, string title, EventHandler click)
        {
            var button = new Controls.buttons.RoundedButton { Width = 33, Height = 32, FlatStyle = FlatStyle.Flat,
                Image = resources.GetObject(imageKey) as Image, BackColor = BotUi.Paper,
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
            mapControl.SelectSpell(id); ClosePanel(); mapControl.Focus(); UpdateQuickSpells();
        }
        private void OpenSpellDetails(Button slot)
        {
            short id; if (!quickSpellIds.TryGetValue(slot,out id)) return;
            ShowPanel(2);
            foreach (ListViewItem row in spells.Items) if ((short)row.Tag == id) { row.Selected = true; row.EnsureVisible(); break; }
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
            ActualCompte.Logger.log_event+=Log;ActualCompte.Logger.log_eventChat+=ChatLog;ActualCompte.AccountStateEvent+=RefreshState;ActualCompte.AccountDisconnectEvent+=RefreshState;ActualCompte.Game.character.SeeLifeRegen+=displaylife;ActualCompte.Game.character.ChatPrivate+=AddPrivateToList;ActualCompte.Game.character.RefreshCaracteristiques+=RefreshState;ActualCompte.Game.character.Spells_Refresh+=RefreshState;ActualCompte.Game.Map.RefreshMap+=MapChanged;ActualCompte.Game.Fight.CombatChanged+=RefreshState;
            SubscribeInteractions();
        }
        private void Log(LogsMessages message,string color) { BotUi.OnUi(this,()=>BotUi.Append(journal,BotPacketRedactor.Redact(message.ToString(),ActualCompte),color)); }
        private void ChatLog(LogsMessages message,string color) { BotUi.OnUi(this,()=>BotUi.Append(chat,BotPacketRedactor.Redact(message.ToString(),ActualCompte),color)); }
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
            mapArea.Controls.Add(mapControl); mapControl.SendToBack(); drawer.BringToFront(); UpdateMapDisplay();
        }
        public void displaylife() => RefreshState();
        private void RefreshState()
        {
            BotUi.OnUi(this,()=> {
                if(ActualCompte.Game==null)return;var c=ActualCompte.Game.character;var s=c.stats;
                summary.Text=(string.IsNullOrEmpty(c.Name)?"Personnage en cours de chargement":c.Name)+" · Niveau "+c.Level+" · Vie "+s.VitalityActual+"/"+s.MaxVitality+" · Kamas "+c.Kamas;
                life.UpdateLife(s.VitalityActual, s.MaxVitality, c.Level);
                toolTips.SetToolTip(life, "Vie : " + s.VitalityActual + "/" + s.MaxVitality + "\nNiveau : " + c.Level);
                string activity=StateName(ActualCompte.AccountStates);state.Text=ActualCompte.ConnectionStatus==activity?activity:ActualCompte.ConnectionStatus+" · "+activity;send.Enabled=ActualCompte.Connexion!=null&&ActualCompte.Connexion.IsConnected();
                var fight = ActualCompte.Game.Fight;
                combatTools.Visible = fight.IsInFight && !fight.IsSpectator; combatTools.Width = combatTools.Visible ? 135 : 0;
                ready.Visible = fight.IsPlacement; ready.Text = fight.IsReady ? "Annuler" : "Prêt";
                ready.Enabled = send.Enabled && fight.IsPlacement && !fight.IsActionPending;
                passTurn.Visible = fight.IsInFight && !fight.IsPlacement;
                passTurn.Enabled = send.Enabled && fight.IsMyTurn && !fight.IsActionPending;
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
                stats.BeginUpdate();stats.Items.Clear();Row(stats,"Vie",s.VitalityActual+" / "+s.MaxVitality);Row(stats,"Énergie",s.ActualEnergy+" / "+s.EnergyMax);Row(stats,"Expérience",s.ActualEXP+" / "+s.ExpNivNext);Row(stats,"Points de caractéristiques",c.Carac_Points);Row(stats,"Points d’action",s.PA.StatsTotal);Row(stats,"Points de mouvement",s.PM.StatsTotal);Row(stats,"Vitalité",s.Vita.StatsTotal);Row(stats,"Sagesse",s.Sagesse.StatsTotal);Row(stats,"Force",s.Force.StatsTotal);Row(stats,"Intelligence",s.Intell.StatsTotal);Row(stats,"Chance",s.Chance.StatsTotal);Row(stats,"Agilité",s.Agility.StatsTotal);Row(stats,"Pods",c.Inventory.Actual_pods+" / "+c.Inventory.Pods_Max);stats.EndUpdate();
                uint selectedItem=inventory.SelectedItems.Count==0?0u:(uint)inventory.SelectedItems[0].Tag;
                refreshingLists=true;
                try { inventory.BeginUpdate();inventory.Items.Clear();foreach(var item in c.Inventory.Objets.OrderBy(x=>x.IsEquipped()?0:1).ThenBy(x=>x.Name,StringComparer.CurrentCultureIgnoreCase)) { var row=inventory.Items.Add(string.IsNullOrEmpty(item.Name)?"Objet n° "+item.ID:item.Name);row.SubItems.Add(item.Qua.ToString());row.SubItems.Add(InventoryPosition(item.position.ToString()));row.Tag=item.Inventory_ID;if(item.Inventory_ID==selectedItem)row.Selected=true; }inventory.EndUpdate(); }
                finally { refreshingLists=false; }
                jobs.BeginUpdate();jobs.Items.Clear();foreach(var job in c.GetJobsSnapshot())Row(jobs,string.IsNullOrEmpty(job.name)?"Métier #"+job.ID:job.name,job.Level);jobs.EndUpdate();
                short selectedSpell=spells.SelectedItems.Count==0?(short)-1:(short)spells.SelectedItems[0].Tag;
                spells.BeginUpdate();spells.Items.Clear();foreach(var spell in c.Spells.OrderBy(x=>x.Value.Name,StringComparer.CurrentCultureIgnoreCase)) { var row=spells.Items.Add(string.IsNullOrEmpty(spell.Value.Name)?"Sort #"+spell.Key:spell.Value.Name);row.SubItems.Add(spell.Value.Level.ToString());row.Tag=spell.Key;if(spell.Key==selectedSpell)row.Selected=true; }spells.EndUpdate();UpdateSpellSelection();UpdateQuickSpells();
                int cost=BoostCost();boostHelp.Text=c.Carac_Points+" point(s) disponible(s). Coût estimé : "+cost+" point(s).";boost.Enabled=!boosting&&!fight.IsInFight&&send.Enabled&&c.id>0&&cost>0&&c.Carac_Points>=cost;
                RefreshInteractionPanels();
            });
        }
        private static string StateName(AccountStates value)
        {
            switch(value) { case AccountStates.DISCONNECTED:return "Déconnecté";case AccountStates.CONNECTED:return "Connexion en cours";case AccountStates.CONNECTED_INACTIVE:return "Disponible";case AccountStates.MOVING:return "Déplacement";case AccountStates.FIGHTING:return "Combat";case AccountStates.GATHERING:return "Récolte";case AccountStates.DIALOG:return "Dialogue";case AccountStates.STORAGE:return "Stockage";case AccountStates.EXCHANGE:return "Échange";case AccountStates.BUYING:return "Achat";case AccountStates.SELLING:return "Vente";case AccountStates.REGENERATION:return "Régénération";case AccountStates.ZAAP:return "Zaap";default:return value.ToString(); }
        }
        private static void Row(ListView list,string title,params object[] values) { var row=list.Items.Add(title);foreach(var value in values)row.SubItems.Add(Convert.ToString(value)); }
        private int BoostCost() { var stat=new[] { StatsEnum.VITALITE,StatsEnum.SAGESSE,StatsEnum.FORCE,StatsEnum.INTELLIGENCE,StatsEnum.CHANCE,StatsEnum.AGILITE }[boostStat.SelectedIndex];return ActualCompte.Game.character.stats.GetCapitalStatsBoost(ActualCompte.Game.character.Race_ID,stat); }
        private async Task Boost()
        {
            if(!boost.Enabled)return;boosting=true;boost.Enabled=false;
            try { await ActualCompte.Connexion.SendPacket("AB"+new[] { 11,12,10,15,13,14 }[boostStat.SelectedIndex],true);await Task.Delay(500); }
            catch(Exception ex) { BotUi.Append(journal,ex.Message); }
            finally { boosting=false;if(!IsDisposed)RefreshState(); }
        }
        private void UpdateSpellSelection()
        {
            upgradeSpell.Enabled=false;int points=ActualCompte.Game.character.SpellPoints;Spell spell;
            if(spells.SelectedItems.Count==0||!ActualCompte.Game.character.Spells.TryGetValue((short)spells.SelectedItems[0].Tag,out spell)) { spellHelp.Text=points+" point(s) de sort disponible(s). Sélectionnez un sort à améliorer.";return; }
            if(!spell.HasMetadata) { spellHelp.Text="Les données de ce sort sont absentes. Exportez les sorts depuis le parseur.";return; }
            if(spell.Level>=6) { spellHelp.Text="Ce sort est au niveau maximal (6).";return; }
            spellHelp.Text=points+" point(s) disponible(s). Niveau suivant : "+(spell.Level+1)+", coût : "+spell.Level+" point(s). Le serveur confirme l’amélioration.";
            upgradeSpell.Enabled=!upgradingSpell&&send.Enabled&&points>=spell.Level&&(ActualCompte.AccountStates==AccountStates.CONNECTED_INACTIVE||ActualCompte.AccountStates==AccountStates.REGENERATION);
        }
        private async Task UpgradeSpell()
        {
            if(!upgradeSpell.Enabled||spells.SelectedItems.Count==0)return;short id=(short)spells.SelectedItems[0].Tag;upgradingSpell=true;UpdateSpellSelection();
            try { await ActualCompte.Connexion.SendPacket("SB"+id,true);await Task.Delay(500); }
            catch(Exception ex) { BotUi.Append(journal,ex.Message); }
            finally { upgradingSpell=false;if(!IsDisposed)UpdateSpellSelection(); }
        }
        private static string InventoryPosition(string value) { switch(value) { case "NOT_EQUIPPED":return "Sac";case "NECKLACE":return "Amulette";case "WEAPON":return "Arme";case "LEFT_RING":return "Anneau gauche";case "RIGHT_RING":return "Anneau droit";case "BELT":return "Ceinture";case "BOOTS":return "Bottes";case "HAT":return "Coiffe";case "CAPE":return "Cape";case "PET":return "Familier";case "SHIELD":return "Bouclier";default:return value.Replace('_',' '); } }
        public void AddPrivateToList(string who) { BotUi.OnUi(this,()=> { WhoPM=who;recipient.Text=who; }); }
        private void ChangeChannel() { GeneralTchat=channel.SelectedIndex==0;RecruitTchat=channel.SelectedIndex==1;MarchandTchat=channel.SelectedIndex==2;GuildeTchat=channel.SelectedIndex==3;GroupTchat=channel.SelectedIndex==4;PMTchat=channel.SelectedIndex==5; }
        private void UpdateRecipientVisibility() { privateRecipientBar.Visible=recipient.Enabled=recipient.Visible=channel.SelectedIndex==5; }
        public async Task SpeakInTchat()
        {
            if(!send.Enabled||string.IsNullOrWhiteSpace(chatInput.Text))return;
            string message=chatInput.Text;
            if(message.IndexOfAny(new[] { '\r','\n','\0','|' })>=0) { BotUi.Append(chat,"Le message ne peut pas contenir de retour à la ligne ni de barre verticale.");return; }
            try
            {
                if(message.Equals("/MAPID",StringComparison.OrdinalIgnoreCase))BotUi.Append(chat,"Carte : "+ActualCompte.Game.Map.MapID);
                else if(message.Equals("/CELLID",StringComparison.OrdinalIgnoreCase))BotUi.Append(chat,"Cellule : "+ActualCompte.Game.character.Cell?.CellID);
                else if(message.Equals("/PING",StringComparison.OrdinalIgnoreCase))await ActualCompte.Connexion.SendPacket("ping",true);
                else
                {
                    if(channel.SelectedIndex==3&&!ActualCompte.Game.character.HasGuild) { BotUi.Append(chat,"Le personnage n’appartient pas à une guilde.");return; }
                    if(channel.SelectedIndex==4&&!ActualCompte.Game.character.InGroupe) { BotUi.Append(chat,"Le personnage n’appartient pas à un groupe.");return; }
                    string target=new[] { "*","?",":","%","$","" }[channel.SelectedIndex];
                    if(PMTchat) { target=recipient.Text.Trim();if(string.IsNullOrEmpty(target)||target.IndexOfAny(new[] { '\r','\n','\0','|' })>=0) { BotUi.Append(chat,"Renseignez le nom du destinataire.");return; } }
                    await ActualCompte.Connexion.SendPacket("BM"+target+"|"+message+"|",true);
                }
                chatInput.Clear();
            }
            catch(Exception ex) { BotUi.Append(chat,ex.Message); }
        }
        private void ShowPackets() { if(FG!=null&&!FG.IsDisposed) { FG.Activate();return; }FG=new FluxForm(ActualCompte);FG.Show(); }
        private void ChangeCharacter() { var config=ActualCompte.accountConfig;ActualCompte.Disconnect();new PersoSelection(config).Show();Close(); }
        private void Disconnect() { ActualCompte.Disconnect();new LoginForm().Show();Close(); }
        private void OnSessionClosed(object sender,FormClosedEventArgs args)
        {
            refresh.Stop();refresh.Dispose();toolTips.Dispose();
            if (mapControl != null) { mapControl.DisplayStateChanged -= UpdateMapDisplay; mapControl.SpellSelectionChanged -= SpellSelectionChanged; mapControl.ActionFeedback -= ShowActionFeedback; }
            foreach (var icon in spellIcons.Values) icon?.Dispose(); spellIcons.Clear();
            if(FG!=null&&!FG.IsDisposed)FG.Close();
            UnsubscribeInteractions();
            ActualCompte.Logger.log_event-=Log;ActualCompte.Logger.log_eventChat-=ChatLog;ActualCompte.AccountStateEvent-=RefreshState;ActualCompte.AccountDisconnectEvent-=RefreshState;ActualCompte.Game.character.SeeLifeRegen-=displaylife;ActualCompte.Game.character.ChatPrivate-=AddPrivateToList;ActualCompte.Game.character.RefreshCaracteristiques-=RefreshState;ActualCompte.Game.character.Spells_Refresh-=RefreshState;ActualCompte.Game.Map.RefreshMap-=MapChanged;ActualCompte.Game.Fight.CombatChanged-=RefreshState;ActualCompte.Dispose();
        }
    }
}
