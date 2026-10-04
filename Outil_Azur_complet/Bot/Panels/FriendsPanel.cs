using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Controls;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Actions;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Social;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>
    /// Volet Amis : la fenêtre <c>Friends</c> du client 1.34 (742 × 438) rendue dans le tiroir. Onglets Amis / Ennemis
    /// (l'ouverture et chaque changement d'onglet envoient <c>FL</c> / <c>iL</c>), fiche du conjoint (<c>FS</c> : buste, niveau,
    /// zone, « Rejoindre » <c>FJS</c>, « Suivre le déplacement » <c>FJC±</c>), lignes « Connectés » (alignement, classe,
    /// « Compte (nom) », niveau, épée en combat, × pour retirer) et « Non connectés » (compte, ×), champ « Ajouter »
    /// (<c>FA%&lt;nom&gt;</c> / <c>iA%&lt;nom&gt;</c> puis la liste, comme le client) et case « M'avertir lors de la connexion
    /// de l'un de mes amis » (<c>FO±</c>). Un clic sur un joueur connecté ouvre son menu (informations, message privé,
    /// invitation, retrait), un double clic prépare un message privé, comme <c>itemSelected</c> / <c>itemdblClick</c> du client.
    /// L'onglet « Ignorés » du client (liste noire locale du chat) n'existe pas dans le bot.
    /// </summary>
    public sealed class FriendsPanel : GamePanel
    {
        private const string Reference = "AMIS";
        private FriendListKind tab = FriendListKind.Friends;
        private FriendsTabStrip tabs;
        private Label info, onlineTitle, offlineTitle, addTitle, status;
        private SpouseCard spouse;
        private FriendRowList online, offline;
        private Panel onlineArea, offlineArea;
        private TextBox addName;
        private Control addButton;
        private CheckBox warn;
        private ToolTip tips;
        private FriendsActions bound;
        private PanelHost watched;
        private bool fillingWarn;

        public override string Title => FriendsTexts.Get("FRIENDS", "Amis");
        public override Image Icon => ClientAssets.Icon("icone-amis", 24);
        /// <summary>Onglet affiché.</summary>
        public FriendListKind CurrentTab => tab;
        public FriendRowList OnlineList => online;
        public FriendRowList OfflineList => offline;
        public SpouseCard SpouseView => spouse;
        /// <summary>Dernier menu de joueur construit (tests, diagnostic) ; il se libère à sa fermeture.</summary>
        public ContextMenuStrip LastMenu { get; private set; }

        protected override Control CreateView()
        {
            var page = Page();
            tips = new ToolTip { InitialDelay = 300, ReshowDelay = 100 };
            tabs = new FriendsTabStrip { Dock = DockStyle.Top, Name = "friends-tabs" };
            tabs.TabClicked += (s, kind) => SelectTab(kind);
            info = MakeLabel(string.Empty, 8.25f); info.Dock = DockStyle.Top; info.Height = 20; info.ForeColor = BotUi.Muted;
            info.TextAlign = ContentAlignment.MiddleLeft; info.AutoEllipsis = true; info.Name = "friends-info";

            spouse = new SpouseCard { Dock = DockStyle.Top, Visible = false, Name = "friends-spouse" };
            spouse.JoinClicked += async (s, e) => await Run(friends => friends.JoinSpouseAsync());
            spouse.FollowClicked += async (s, e) =>
            {
                bool follow = Game?.Interactions?.Friends?.Spouse?.IsFollowed != true;
                await Run(friends => friends.FollowSpouseAsync(follow));
            };

            onlineTitle = SectionTitle("friends-online-title");
            online = new FriendRowList(true) { Dock = DockStyle.Top, Name = "friends-online", AccessibleName = "Joueurs connectés" };
            online.RowClicked += (s, e) => ShowEntryMenu(e.Entry, e.Location);
            online.RowDoubleClicked += (s, e) => PrivateMessage(e.Entry);
            online.RemoveClicked += async (s, e) => await Remove(e.Entry);
            onlineArea = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = BotUi.Paper, Margin = new Padding(0) };
            onlineArea.Controls.Add(online);

            offlineTitle = SectionTitle("friends-offline-title");
            offline = new FriendRowList(false) { Dock = DockStyle.Top, Name = "friends-offline", AccessibleName = "Joueurs non connectés" };
            offline.RemoveClicked += async (s, e) => await Remove(e.Entry);
            offlineArea = new Panel { Dock = DockStyle.Bottom, Height = 4 * FriendRowList.RowHeight + 2, AutoScroll = true, BackColor = BotUi.Paper,
                Margin = new Padding(0) };
            offlineArea.Controls.Add(offline);

            addTitle = SectionTitle("friends-add-title"); addTitle.Dock = DockStyle.Bottom;
            var addBar = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 32, ColumnCount = 2, RowCount = 1, Margin = new Padding(0),
                Padding = new Padding(0, 3, 0, 2) };
            addBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); addBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            addName = new TextBox { Dock = DockStyle.Fill, Font = BotFonts.Get(9), BackColor = BotUi.PaperLight, ForeColor = BotUi.Ink,
                BorderStyle = BorderStyle.FixedSingle, MaxLength = 40, Name = "friends-add-name", AccessibleName = "Nom du personnage à ajouter" };
            addName.KeyDown += async (s, e) => { if (e.KeyCode == Keys.Enter) { e.Handled = e.SuppressKeyPress = true; await Add(); } };
            addButton = MakeButton(FriendsTexts.Get("ADD", "Ajouter"), async (s, e) => await Add(), true, 92);
            addBar.Controls.Add(addName, 0, 0); addBar.Controls.Add(addButton, 1, 0);

            warn = new CheckBox { Dock = DockStyle.Bottom, Height = 34, Font = BotFonts.Get(8.25f), ForeColor = BotUi.Ink, BackColor = BotUi.Paper,
                FlatStyle = FlatStyle.Flat, Name = "friends-warn", TextAlign = ContentAlignment.MiddleLeft, CheckAlign = ContentAlignment.MiddleLeft,
                Text = FriendsTexts.Get("WARNING_WHEN_FRIENDS_COME_ONLINE", "M'avertir lors de la connexion de l'un de mes amis.") };
            warn.FlatAppearance.BorderColor = BotUi.Gold; warn.FlatAppearance.CheckedBackColor = BotUi.PaperLight;
            warn.CheckedChanged += async (s, e) => { if (!fillingWarn) await Run(friends => friends.SetWarnOnFriendLoginAsync(warn.Checked)); };
            tips.SetToolTip(warn, FriendsTexts.Get("WARNING_WHEN_FRIENDS_COME_ONLINE_TOOLTIP",
                "Seules les personnes qui vous ont également dans leur liste d'amis vous signaleront leur présence en ligne."));

            status = MakeStatus(string.Empty); status.Name = "friends-status";
            offlineTitle.Dock = DockStyle.Bottom;
            // Ordre d'ancrage : le dernier ajouté prend le bord en premier. En haut : onglets, texte, conjoint, titre
            // « Connectés » ; en bas, de bas en haut : statut, case, champ, titre « Ajouter », non connectés et leur titre ;
            // la liste des connectés (ajoutée la première) remplit le reste.
            page.Controls.Add(onlineArea); page.Controls.Add(onlineTitle); page.Controls.Add(spouse); page.Controls.Add(info); page.Controls.Add(tabs);
            page.Controls.Add(offlineTitle); page.Controls.Add(offlineArea); page.Controls.Add(addTitle); page.Controls.Add(addBar);
            page.Controls.Add(warn); page.Controls.Add(status);
            return page;
        }

        private static Label SectionTitle(string name)
        {
            var label = MakeLabel(string.Empty, 9, true);
            label.Dock = DockStyle.Top; label.Height = 22; label.TextAlign = ContentAlignment.BottomLeft; label.AutoEllipsis = true; label.Name = name;
            return label;
        }

        protected override void OnBind(GameClass game)
        {
            bound = game.Interactions?.Friends;
            if (bound != null)
            {
                bound.Changed += OnFriendsChanged;
                bound.ErrorBox += OnErrorBox;
                bound.WindowOpen = Host != null && Host.IsOpen(this);
            }
            watched = Host;
            if (watched != null) { watched.PanelShown += OnPanelShown; watched.PanelClosed += OnPanelClosed; }
        }

        protected override void OnUnbind(GameClass game)
        {
            if (bound != null)
            {
                bound.Changed -= OnFriendsChanged;
                bound.ErrorBox -= OnErrorBox;
                bound.WindowOpen = false;
            }
            if (watched != null) { watched.PanelShown -= OnPanelShown; watched.PanelClosed -= OnPanelClosed; }
            bound = null; watched = null;
        }

        // ----- Ouverture, onglets (fil de l'interface) -----

        private void OnPanelShown(object sender, PanelEventArgs e)
        {
            if (!ReferenceEquals(e?.Panel, this) || bound == null || bound.WindowOpen) return;
            // Comme loadUIAutoHideComponent("Friends") : la liste de l'onglet courant est demandée à l'ouverture.
            bound.WindowOpen = true;
            _ = Run(friends => friends.RefreshAsync(tab));
        }

        private void OnPanelClosed(object sender, PanelEventArgs e)
        {
            if (ReferenceEquals(e?.Panel, this) && bound != null) bound.WindowOpen = false;
        }

        /// <summary>Change d'onglet et demande sa liste (<c>FL</c> / <c>iL</c>), comme <c>setCurrentTab</c> du client.</summary>
        public void SelectTab(FriendListKind kind)
        {
            if (tab == kind && tabs != null && tabs.Selected == kind) return;
            tab = kind;
            RefreshView();
            _ = Run(friends => friends.RefreshAsync(kind));
        }

        // ----- Événements du modèle (fil réseau) -----

        private void OnFriendsChanged() => Post(RefreshView);

        private void OnErrorBox(string title, string text) => Post(async () =>
        {
            try { await BotDialogs.InfoAsync(Host, title, text); }
            catch (Exception error) { Account?.Logger?.LogException(Reference, error); }
        });

        /// <summary>Passe par la fenêtre de jeu (le tiroir jamais affiché n'a pas de poignée) ; rien sans fenêtre.</summary>
        private bool Post(Action action) => ExchangePanel.PostToForm(Host, () => { if (!IsDisposed) action(); });

        // ----- Affichage -----

        public override void RefreshView()
        {
            FriendsActions friends = Game?.Interactions?.Friends;
            if (friends == null || online == null || IsDisposed) return;
            bool enemies = tab == FriendListKind.Enemies;
            tabs.Selected = tab;
            info.Text = enemies ? FriendsTexts.Get("FRIENDS_INFO_ENEMIES", "Liste de vos ennemis") : FriendsTexts.Get("FRIENDS_INFO_FRIENDS", "Liste de vos amis");
            IReadOnlyList<FriendEntry> list = friends.List(tab);
            FriendEntry[] connected = list.Where(entry => entry.IsOnline).ToArray(), away = list.Where(entry => !entry.IsOnline).ToArray();
            online.SetEntries(connected);
            offline.SetEntries(away);
            string empty = friends.HasReceived(tab) ? "Aucun joueur." : "Liste non reçue : ouvrez l'onglet une fois connecté.";
            online.EmptyText = offline.EmptyText = empty;
            onlineTitle.Text = FriendsTexts.Get("ONLINE", "Connectés") + " (" + connected.Length.ToString(CultureInfo.CurrentCulture) + ")";
            offlineTitle.Text = FriendsTexts.Get("OFFLINE", "Non connectés") + " (" + away.Length.ToString(CultureInfo.CurrentCulture) + ")";
            addTitle.Text = enemies ? FriendsTexts.Get("ADD_AN_ENEMY", "Ajouter un ennemi") : FriendsTexts.Get("ADD_A_FRIEND", "Ajouter un ami");

            SpouseInfo married = enemies ? null : friends.Spouse;
            spouse.Visible = married != null;
            spouse.SetSpouse(married, Connected, Account?.IsFighting() == true);

            fillingWarn = true;
            try { warn.Checked = friends.WarnOnFriendLogin == true; }
            finally { fillingWarn = false; }
            warn.Enabled = Connected;
            addButton.Enabled = Connected;
            status.Text = friends.LastMessage;
        }

        // ----- Actions -----

        private Task Add()
        {
            string name = addName.Text.Trim();
            FriendListKind kind = tab;
            return Run(async friends =>
            {
                InteractionResult result = await friends.AddAsync(kind, name, true);
                if (result.Sent && !addName.IsDisposed) addName.Clear();
                return result;
            });
        }

        private Task Remove(FriendEntry entry)
        {
            FriendListKind kind = tab;
            return Run(friends => friends.RemoveAsync(kind, entry));
        }

        private void PrivateMessage(FriendEntry entry)
        {
            MapActions actions = Game?.Interactions?.MapActions;
            if (entry == null || actions == null) return;
            InteractionResult result = actions.RequestPrivateMessage(entry.Name);
            Feedback(result.Message);
        }

        private Task Run(Func<FriendsActions, Task<InteractionResult>> action)
        {
            FriendsActions friends = Game?.Interactions?.Friends;
            return friends == null ? Task.CompletedTask : ReportAsync(() => action(friends));
        }

        /// <summary>
        /// Menu d'un joueur connecté de la liste, comme <c>showPlayerPopupMenu</c> appelé depuis la fenêtre du client (sans les
        /// entrées d'ajout) : nom en tête, « Informations » (<c>BW</c>), « Message privé », « Inviter dans le groupe »
        /// (<c>PI</c>), puis « Retirer de la liste ». Renvoie le menu affiché ; il se libère de lui-même une fois refermé.
        /// </summary>
        public ContextMenuStrip ShowEntryMenu(FriendEntry entry, Point? location = null)
        {
            GameClass game = Game;
            if (entry == null || game == null || online == null || online.IsDisposed) return null;
            string name = entry.Name;
            FriendListKind kind = tab;
            var strip = new ContextMenuStrip { Renderer = new RetroMenuRenderer(), Font = BotFonts.Get(8.25f), BackColor = BotUi.Paper,
                ForeColor = BotUi.Ink, ShowImageMargin = false, AccessibleName = "Menu de " + name, Name = "friends-entry-menu" };
            strip.Items.Add(new ToolStripLabel(name.Replace("&", "&&")) { Tag = ActorContextMenu.HeaderTag, Font = BotFonts.Get(8.25f, FontStyle.Bold),
                ForeColor = BotUi.Ink, AccessibleName = "Joueur : " + name });
            AddItem(strip, MapActionTexts.Whois, "Envoie BW" + name, () => game.Interactions.MapActions.WhoisAsync(name));
            AddItem(strip, MapActionTexts.PrivateMessage, "Prépare « /w " + name + " » dans la console",
                () => Task.FromResult(game.Interactions.MapActions.RequestPrivateMessage(name)));
            AddItem(strip, MapActionTexts.InviteToParty, "Envoie PI" + name, () => game.Interactions.Party.InviteAsync(name));
            strip.Items.Add(new ToolStripSeparator());
            AddItem(strip, "Retirer de la liste", "Envoie " + (kind == FriendListKind.Enemies ? "iD" : "FD") + entry.RemoveTarget,
                () => game.Interactions.Friends.RemoveAsync(kind, entry));
            LastMenu = strip;
            Point where = location ?? online.PointToClient(Cursor.Position);
            strip.Closed += (s, e) => DisposeMenuLater(strip);
            strip.Show(online, where);
            return strip;
        }

        private void AddItem(ContextMenuStrip strip, string text, string tooltip, Func<Task<InteractionResult>> action)
        {
            var item = new ToolStripMenuItem(text) { ToolTipText = tooltip, AccessibleName = text, Enabled = Connected };
            item.Click += async (s, e) => await ReportAsync(action);
            strip.Items.Add(item);
        }

        private void DisposeMenuLater(ContextMenuStrip strip)
        {
            // L'entrée cliquée reçoit son Click après la fermeture : la libération attend la fin du message en cours.
            Control owner = online;
            if (owner != null && !owner.IsDisposed && owner.IsHandleCreated)
                try { owner.BeginInvoke((Action)strip.Dispose); return; }
                catch (InvalidOperationException) { }
            strip.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing) return;
            tips?.Dispose();
            LastMenu = null;
        }
    }

    /// <summary>Onglets « Amis » / « Ennemis » de la fenêtre du client (<c>_btnTabFriends</c>, <c>_btnTabEnemies</c>).</summary>
    public sealed class FriendsTabStrip : Control
    {
        public const int TabHeight = 24;
        private static readonly FriendListKind[] Kinds = { FriendListKind.Friends, FriendListKind.Enemies };
        private FriendListKind selected = FriendListKind.Friends;

        public FriendsTabStrip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = BotUi.Paper; Height = TabHeight + 3; Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.PageTabList;
        }

        public event EventHandler<FriendListKind> TabClicked;

        public FriendListKind Selected
        {
            get => selected;
            set { if (selected == value) return; selected = value; Invalidate(); }
        }

        /// <summary>Rectangle de l'onglet (moitié de la largeur chacun).</summary>
        public Rectangle TabBounds(FriendListKind kind)
        {
            int width = Math.Max(40, (Width - 6) / 2);
            return new Rectangle(kind == FriendListKind.Friends ? 0 : width + 4, 0, width, TabHeight);
        }

        /// <summary>Simule un clic sur l'onglet (raccourci clavier, tests).</summary>
        public void ClickTab(FriendListKind kind) => TabClicked?.Invoke(this, kind);

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var paper = new SolidBrush(BackColor)) e.Graphics.FillRectangle(paper, ClientRectangle);
            using (var rule = new Pen(BotUi.Gold)) e.Graphics.DrawLine(rule, 0, TabHeight, Width, TabHeight);
            foreach (FriendListKind kind in Kinds)
            {
                Rectangle area = TabBounds(kind);
                bool active = kind == selected;
                using (var fill = new SolidBrush(active ? BotUi.PaperLight : BotUi.FrameLight)) e.Graphics.FillRectangle(fill, area);
                using (var border = new Pen(BotUi.Gold)) e.Graphics.DrawRectangle(border, area.X, area.Y, area.Width - 1, area.Height - (active ? 0 : 1));
                TextRenderer.DrawText(e.Graphics, FriendsTexts.Title(kind), BotFonts.Get(9, active ? FontStyle.Bold : FontStyle.Regular), area,
                    active ? BotUi.Ink : BotUi.PaperLight, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            foreach (FriendListKind kind in Kinds)
                if (TabBounds(kind).Contains(e.Location) && kind != selected) { TabClicked?.Invoke(this, kind); return; }
        }
    }

    /// <summary>Ligne cliquée dans <see cref="FriendRowList"/>.</summary>
    public sealed class FriendRowEventArgs : EventArgs
    {
        public FriendEntry Entry { get; }
        public Point Location { get; }
        internal FriendRowEventArgs(FriendEntry entry, Point location) { Entry = entry; Location = location; }
    }

    /// <summary>
    /// Lignes de la liste d'amis ou d'ennemis, comme <c>UI_FriendsConnectedItem</c> (321 × 20 : alignement 12 px, illustration de
    /// classe 16 px, « Compte (nom) », niveau, épée du combat, bouton ×) et <c>UI_FriendsDisconnectedItem</c> (compte et ×).
    /// Les images (<c>Alignments/mini</c>, <c>Artworks/Mini</c>, épée de <c>Party/infos</c>, croix du client) sont lues en tâche de
    /// fond par <see cref="ClientAssets.GetAsync"/> ; le dessin ne lit que le cache.
    /// </summary>
    public sealed class FriendRowList : Control
    {
        public const int RowHeight = 20, HeaderHeight = 18;
        private const int LevelWidth = 55, FightWidth = 28, RemoveSize = 14;
        private readonly bool detailed;
        private readonly ToolTip tips = new ToolTip { InitialDelay = 300, ReshowDelay = 100 };
        private IReadOnlyList<FriendEntry> items = new FriendEntry[0];
        private int hover = -1;
        private string emptyText = "Aucun joueur.";

        public FriendRowList(bool connectedRows)
        {
            detailed = connectedRows;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = BotUi.Paper; Cursor = connectedRows ? Cursors.Hand : Cursors.Default;
            Height = Top0 + RowHeight + 1;
            foreach (KeyValuePair<string, string> image in new[] { Pair("Party", "infos"), Pair("Client", "fermer-haut") }) Load(image.Key, image.Value);
        }

        public event EventHandler<FriendRowEventArgs> RowClicked;
        public event EventHandler<FriendRowEventArgs> RowDoubleClicked;
        public event EventHandler<FriendRowEventArgs> RemoveClicked;

        public IReadOnlyList<FriendEntry> Entries => items;
        public bool Detailed => detailed;
        public string EmptyText { get => emptyText; set { if (emptyText == value) return; emptyText = value ?? string.Empty; if (items.Count == 0) Invalidate(); } }

        private int Top0 => detailed ? HeaderHeight : 0;

        /// <summary>Remplace les lignes (fil de l'interface) et lance la lecture des images manquantes.</summary>
        public void SetEntries(IReadOnlyList<FriendEntry> entries)
        {
            entries = entries ?? new FriendEntry[0];
            // Mêmes lignes (instances immuables du modèle) : rien à redessiner, le survol est gardé.
            if (items.Count == entries.Count && items.Zip(entries, (shown, next) => ReferenceEquals(shown, next)).All(same => same)) return;
            items = entries;
            hover = -1;
            Height = Top0 + Math.Max(1, items.Count) * RowHeight + 1;
            AccessibleDescription = string.Join(", ", items.Select(entry => entry.Name));
            if (detailed)
                foreach (FriendEntry entry in items)
                {
                    if (entry.Gfx.HasValue) Load("Artworks", MiniName(entry.Gfx.Value));
                    if (entry.Alignment >= 0) Load("Alignments", AlignmentName(entry.Alignment));
                }
            Invalidate();
        }

        internal static string MiniName(int gfx) => "Mini/" + gfx.ToString(CultureInfo.InvariantCulture);
        internal static string AlignmentName(int alignment) => "mini/" + alignment.ToString(CultureInfo.InvariantCulture);

        public Rectangle RowBounds(int index) => new Rectangle(0, Top0 + index * RowHeight, Width, RowHeight);

        /// <summary>Bouton × de la ligne (<c>_btnRemove</c>).</summary>
        public Rectangle RemoveBounds(int index)
        {
            Rectangle row = RowBounds(index);
            return new Rectangle(row.Right - RemoveSize - 3, row.Y + (RowHeight - RemoveSize) / 2, RemoveSize, RemoveSize);
        }

        /// <summary>Ligne sous le point, ou -1.</summary>
        public int IndexAt(Point point)
        {
            if (point.Y < Top0) return -1;
            int index = (point.Y - Top0) / RowHeight;
            return index >= 0 && index < items.Count ? index : -1;
        }

        /// <summary>Simule un clic sur la ligne du joueur (tests, clavier) ; faux s'il n'est pas listé.</summary>
        public bool ClickRow(string name) => Raise(RowClicked, name, index => new Point(40, RowBounds(index).Y + 4));
        /// <summary>Simule un clic sur le × de la ligne du joueur.</summary>
        public bool ClickRemove(string name) => Raise(RemoveClicked, name, index => RemoveBounds(index).Location);

        private bool Raise(EventHandler<FriendRowEventArgs> handler, string name, Func<int, Point> where)
        {
            for (int index = 0; index < items.Count; index++)
                if (string.Equals(items[index].Name, name, StringComparison.Ordinal))
                {
                    handler?.Invoke(this, new FriendRowEventArgs(items[index], where(index)));
                    return true;
                }
            return false;
        }

        private void Load(string family, string name)
        {
            Task<Bitmap> load = ClientAssets.GetAsync(family, name);
            if (!load.IsCompleted) load.ContinueWith(_ => RedrawLater(), TaskScheduler.Default);
        }

        private static KeyValuePair<string, string> Pair(string family, string name) => new KeyValuePair<string, string>(family, name);

        private static Bitmap Cached(string family, string name) => ClientAssets.TryCached(family, name, out Bitmap image) ? image : null;

        private void RedrawLater()
        {
            try { if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)Invalidate); }
            catch (Exception error) when (error is InvalidOperationException || error is ObjectDisposedException) { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            using (var paper = new SolidBrush(BackColor)) graphics.FillRectangle(paper, ClientRectangle);
            int levelX = Width - RemoveSize - 6 - FightWidth - LevelWidth, fightX = Width - RemoveSize - 6 - FightWidth;
            using (var rule = new Pen(BotUi.Gold))
            {
                if (detailed)
                {
                    TextRenderer.DrawText(graphics, FriendsTexts.Get("ACCOUNT", "Compte") + " (" + FriendsTexts.Get("NAME", "nom") + ")", BotFonts.Get(8, FontStyle.Bold),
                        new Rectangle(36, 0, Math.Max(10, levelX - 40), HeaderHeight), BotUi.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    TextRenderer.DrawText(graphics, FriendsTexts.Get("LEVEL", "Niveau"), BotFonts.Get(8, FontStyle.Bold), new Rectangle(levelX + 3, 0, LevelWidth - 4, HeaderHeight),
                        BotUi.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    graphics.DrawLine(rule, 0, HeaderHeight - 1, Width, HeaderHeight - 1);
                }
                if (items.Count == 0)
                {
                    TextRenderer.DrawText(graphics, emptyText, BotFonts.Get(8.25f, FontStyle.Italic), new Rectangle(6, Top0, Width - 12, RowHeight), BotUi.Muted,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    return;
                }
                Bitmap sword = Cached("Party", "infos"), cross = Cached("Client", "fermer-haut");
                for (int index = 0; index < items.Count; index++)
                {
                    FriendEntry entry = items[index];
                    Rectangle row = RowBounds(index);
                    if (index == hover) using (var light = new SolidBrush(BotUi.PaperLight)) graphics.FillRectangle(light, row);
                    int left = 6;
                    if (detailed)
                    {
                        Bitmap alignment = entry.Alignment >= 0 ? Cached("Alignments", AlignmentName(entry.Alignment)) : null;
                        if (alignment != null) lock (alignment) ClientAssets.DrawFit(graphics, alignment, new RectangleF(4, row.Y + 4, 12, 12));
                        Bitmap mini = entry.Gfx.HasValue ? Cached("Artworks", MiniName(entry.Gfx.Value)) : null;
                        if (mini != null) lock (mini) ClientAssets.DrawFit(graphics, mini, new RectangleF(19, row.Y + 2, 16, 16));
                        left = 38;
                        string label = entry.Account != null && !string.Equals(entry.Account, entry.Name, StringComparison.Ordinal)
                            ? entry.Account + " (" + entry.Name + ")" : entry.Name;
                        TextRenderer.DrawText(graphics, label, BotFonts.Get(8.25f, FontStyle.Bold), new Rectangle(left, row.Y, Math.Max(10, levelX - left - 2), RowHeight),
                            BotUi.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                        TextRenderer.DrawText(graphics, entry.LevelText, BotFonts.Get(8.25f), new Rectangle(levelX + 3, row.Y, LevelWidth - 4, RowHeight), BotUi.Ink,
                            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                        graphics.DrawLine(rule, levelX, row.Y, levelX, row.Bottom - 1);
                        graphics.DrawLine(rule, fightX, row.Y, fightX, row.Bottom - 1);
                        if (entry.IsInFight)
                        {
                            var place = new RectangleF(fightX + (FightWidth - 14) / 2f, row.Y + 3, 14, 14);
                            if (sword != null) lock (sword) ClientAssets.DrawFit(graphics, sword, place);
                            else using (var olive = new SolidBrush(BotUi.Olive)) graphics.FillEllipse(olive, place);
                        }
                    }
                    else
                        TextRenderer.DrawText(graphics, entry.Account ?? entry.Name, BotFonts.Get(8.25f), new Rectangle(left, row.Y, Math.Max(10, Width - left - RemoveSize - 10), RowHeight),
                            BotUi.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    Rectangle remove = RemoveBounds(index);
                    if (cross != null) lock (cross) ClientAssets.DrawFit(graphics, cross, remove);
                    else
                        TextRenderer.DrawText(graphics, "×", BotFonts.Get(9, FontStyle.Bold), remove, BotUi.Muted,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    graphics.DrawLine(rule, 2, row.Bottom - 1, Width - 3, row.Bottom - 1);
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = IndexAt(e.Location);
            if (index == hover) return;
            hover = index; Invalidate();
            if (index < 0) { tips.SetToolTip(this, null); return; }
            FriendEntry entry = items[index];
            string text = entry.Name;
            if (entry.IsOnline)
                text += "\n" + FriendsTexts.Get("LEVEL", "Niveau") + " : " + (entry.LevelText.Length > 0 ? entry.LevelText : "?") + "\n" + FriendsTexts.State(entry.State);
            if (entry.IsPartial) text += "\n(détails à la prochaine liste)";
            tips.SetToolTip(this, text);
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
            if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right) return;
            int index = IndexAt(e.Location);
            if (index < 0) return;
            var args = new FriendRowEventArgs(items[index], e.Location);
            if (RemoveBounds(index).Contains(e.Location)) { if (e.Button == MouseButtons.Left) RemoveClicked?.Invoke(this, args); }
            else if (detailed) RowClicked?.Invoke(this, args);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            int index = IndexAt(e.Location);
            if (index < 0 || !detailed || RemoveBounds(index).Contains(e.Location)) return;
            RowDoubleClicked?.Invoke(this, new FriendRowEventArgs(items[index], e.Location));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tips.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Fiche du conjoint, comme <c>SpouseViewer</c> du client (190 × 244) : « Conjoint(e) », buste rond
    /// (<c>Artworks/Faces/&lt;gfx&gt;</c>), nom, niveau et zone (ou « Non connecté(e) »), épée en combat, boutons « Rejoindre »
    /// (grisé en combat) et « Suivre le déplacement… » / « Ne plus suivre le déplacement ».
    /// </summary>
    public sealed class SpouseCard : Panel
    {
        public const int CardHeight = 78, FaceSide = 60;
        private readonly SpouseFace face;
        private readonly Label title, name, detail;
        private readonly PictureBox fight;
        private readonly Button join, follow;

        public SpouseCard()
        {
            Height = CardHeight; BackColor = BotUi.PaperLight; Padding = new Padding(4); Margin = new Padding(0, 2, 0, 2);
            face = new SpouseFace { Location = new Point(6, (CardHeight - FaceSide) / 2), Size = new Size(FaceSide, FaceSide) };
            title = MakeText(BotFonts.Get(8, FontStyle.Bold), BotUi.Muted, new Point(74, 4), "spouse-title");
            name = MakeText(BotFonts.Get(9, FontStyle.Bold), BotUi.Ink, new Point(74, 19), "spouse-name");
            detail = MakeText(BotFonts.Get(8.25f), BotUi.Ink, new Point(74, 35), "spouse-detail");
            fight = new PictureBox { Size = new Size(14, 14), Location = new Point(54, 6), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent,
                Visible = false, Name = "spouse-fight" };
            join = SmallButton("spouse-join"); follow = SmallButton("spouse-follow");
            join.Click += (s, e) => JoinClicked?.Invoke(this, EventArgs.Empty);
            follow.Click += (s, e) => FollowClicked?.Invoke(this, EventArgs.Empty);
            Controls.Add(fight); Controls.Add(face); Controls.Add(title); Controls.Add(name); Controls.Add(detail); Controls.Add(join); Controls.Add(follow);
            fight.BringToFront();
            Task<Bitmap> sword = ClientAssets.GetAsync("Party", "infos");
            sword.ContinueWith(task => SetFightImage(task.Status == TaskStatus.RanToCompletion ? task.Result : null), TaskScheduler.Default);
        }

        public event EventHandler JoinClicked;
        public event EventHandler FollowClicked;
        public SpouseInfo Spouse { get; private set; }
        public Button JoinButton => join;
        public Button FollowButton => follow;
        public string DetailText => detail.Text;
        public SpouseFace Face => face;

        private static Label MakeText(Font font, Color color, Point location, string id) => new Label
        {
            Font = font, ForeColor = color, BackColor = Color.Transparent, Location = location, AutoSize = false, Height = 16,
            AutoEllipsis = true, UseMnemonic = false, Name = id, Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
        };

        private static Button SmallButton(string id)
        {
            var button = (Button)BotUi.Button(string.Empty, null, false, 90);
            Font previous = button.Font;
            button.Font = BotFonts.Get(8);
            if (previous != null && !BotFonts.IsShared(previous) && !ReferenceEquals(previous, Control.DefaultFont)) previous.Dispose();
            button.Height = 22; button.Name = id; button.Margin = new Padding(0);
            return button;
        }

        /// <summary>Recopie la fiche (fil de l'interface).</summary>
        public void SetSpouse(SpouseInfo spouse, bool connected, bool fighting)
        {
            Spouse = spouse;
            if (spouse == null) { face.SetGfx(null, string.Empty); return; }
            title.Text = FriendsTexts.Gendered("SPOUSE", "Conjoint{~fe}", spouse.Sex);
            name.Text = spouse.Name;
            if (spouse.IsConnected)
            {
                string level = spouse.Level.HasValue ? FriendsTexts.Get("LEVEL", "Niveau") + " " + spouse.Level.Value.ToString(CultureInfo.CurrentCulture) : string.Empty;
                string area = spouse.Area;
                detail.Text = level + (level.Length > 0 && area.Length > 0 ? " · " : string.Empty) + area;
            }
            else detail.Text = FriendsTexts.Gendered("SPOUSE_NOT_CONNECTED", "Non connecté{~fe}", spouse.Sex);
            fight.Visible = spouse.IsConnected && spouse.IsInFight;
            join.Text = FriendsTexts.Get("JOIN_SMALL", "Rejoindre");
            follow.Text = spouse.IsFollowed ? FriendsTexts.Get("STOP_FOLLOW", "Ne plus suivre le déplacement") : FriendsTexts.Get("FOLLOW", "Suivre le déplacement...");
            join.Enabled = connected && spouse.IsConnected && !fighting;
            follow.Enabled = connected && spouse.IsConnected;
            face.SetGfx(spouse.Gfx, spouse.Name);
            LayoutChildren();
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            LayoutChildren();
        }

        private void LayoutChildren()
        {
            if (title == null) return;
            int textWidth = Math.Max(40, Width - 80);
            title.Width = name.Width = detail.Width = textWidth;
            int buttonWidth = Math.Max(60, (textWidth - 6) / 2);
            join.SetBounds(74, CardHeight - 26, Math.Min(92, buttonWidth), 22);
            follow.SetBounds(join.Right + 6, CardHeight - 26, Math.Max(60, Width - join.Right - 12), 22);
        }

        private void SetFightImage(Bitmap image)
        {
            if (image == null) return;
            try { if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)(() => { if (!fight.IsDisposed) fight.Image = image; })); }
            catch (Exception error) when (error is InvalidOperationException || error is ObjectDisposedException) { }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (fight.Image == null && ClientAssets.TryCached("Party", "infos", out Bitmap sword) && sword != null) fight.Image = sword;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var border = new Pen(BotUi.Gold)) e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        }

        protected override void Dispose(bool disposing)
        {
            // L'épée est une image partagée de ClientAssets : elle ne doit pas être libérée avec la PictureBox.
            if (disposing && fight != null) fight.Image = null;
            base.Dispose(disposing);
        }
    }

    /// <summary>Buste rond du conjoint (<c>_ldrArtwork</c> du client) : disque brun, filet doré, image du cache ou initiale.</summary>
    public sealed class SpouseFace : Control
    {
        private int? gfx;
        private string initial = string.Empty;

        public SpouseFace()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent; AccessibleName = "Illustration du conjoint";
        }

        public int? Gfx => gfx;
        internal static string FaceName(int value) => "Faces/" + value.ToString(CultureInfo.InvariantCulture);

        public void SetGfx(int? value, string name)
        {
            string letter = string.IsNullOrEmpty(name) ? string.Empty : name.Substring(0, 1).ToUpperInvariant();
            if (gfx == value && initial == letter) return;
            gfx = value; initial = letter;
            if (value.HasValue && value.Value > 0)
            {
                Task<Bitmap> load = ClientAssets.GetAsync("Artworks", FaceName(value.Value));
                if (!load.IsCompleted) load.ContinueWith(_ => RedrawLater(), TaskScheduler.Default);
            }
            Invalidate();
        }

        private void RedrawLater()
        {
            try { if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)Invalidate); }
            catch (Exception error) when (error is InvalidOperationException || error is ObjectDisposedException) { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graphics = e.Graphics;
            int side = Math.Min(Width, Height) - 2;
            if (side <= 4) return;
            var disc = new RectangleF((Width - side) / 2f, (Height - side) / 2f, side, side);
            SmoothingMode previous = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            try
            {
                using (var fill = new SolidBrush(BotUi.Frame)) graphics.FillEllipse(fill, disc);
                Bitmap image = gfx.HasValue && gfx.Value > 0 && ClientAssets.TryCached("Artworks", FaceName(gfx.Value), out Bitmap cached) ? cached : null;
                if (image != null)
                {
                    using (var clip = new GraphicsPath())
                    {
                        clip.AddEllipse(disc);
                        GraphicsState state = graphics.Save();
                        graphics.SetClip(clip);
                        lock (image) ClientAssets.DrawFit(graphics, image, RectangleF.Inflate(disc, -2, -2), true);
                        graphics.Restore(state);
                    }
                }
                else if (initial.Length > 0)
                    TextRenderer.DrawText(graphics, initial, BotFonts.Get(16, FontStyle.Bold), Rectangle.Round(disc), BotUi.Paper,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                using (var ring = new Pen(BotUi.Gold, 2)) graphics.DrawEllipse(ring, disc);
            }
            finally { graphics.SmoothingMode = previous; }
        }
    }
}
