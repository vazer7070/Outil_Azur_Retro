using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Panels;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Combats;
using Tool_BotProtocol.Game.Data;

namespace Outil_Azur_complet.Bot.Controls.Banner
{
    /// <summary>
    /// Bouton rond du bandeau (<c>ButtonBannerRound*</c> 26 × 26 et icône <c>UI_Banner*Icon</c>) ou bouton à image seule
    /// (œil des combats <c>Eye2</c>, menu principal <c>ButtonMainMenu</c>). Un bouton dont le volet n'est pas encore livré
    /// reste cliquable mais grisé, avec l'infobulle « à venir » : il n'ouvre jamais de volet vide.
    /// </summary>
    public sealed class BannerButton : Button
    {
        public const int Side = 26;
        public const int IconSize = 18;
        private readonly string upName, downName;
        private Bitmap up, down;
        private bool pressed, hovered, available = true;

        internal BannerButton(string name, string iconName, string upImage = "ButtonBannerRoundUp", string downImage = "ButtonBannerRoundDown", int side = Side)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Name = name; IconName = iconName; upName = upImage; downName = downImage;
            Size = new Size(side, side); FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Margin = new Padding(0);
            Tag = "client-icon"; Text = string.Empty; TabStop = false; Cursor = Cursors.Hand; BackColor = BotUi.FrameLight;
            if (iconName != null) Image = ClientAssets.Icon(iconName, IconSize);
            if (upName != null) BannerArt.Request(this, upName, image => { up = image; Invalidate(); });
            if (downName != null) BannerArt.Request(this, downName, image => { down = image; Invalidate(); });
        }

        /// <summary>Nom du volet ouvert (« Stats » → <c>StatsPanel</c>), ou nom du bouton pour l'œil et le menu.</summary>
        public string PanelName => Name;
        public string IconName { get; }
        /// <summary>Faux : volet absent, bouton grisé (« à venir »).</summary>
        public bool Available { get => available; set { if (available == value) return; available = value; Invalidate(); } }
        /// <summary>Image dessinée à la place du disque (œil des combats).</summary>
        internal Bitmap Picture { get; set; }

        protected override void OnMouseDown(MouseEventArgs mevent) { pressed = mevent.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(mevent); }
        protected override void OnMouseUp(MouseEventArgs mevent) { pressed = false; Invalidate(); base.OnMouseUp(mevent); }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.Clear(BackColor);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            var box = new RectangleF(0, 0, Width - 1, Height - 1);
            bool grey = !available;
            if (Picture != null)
            {
                int width, height;
                lock (Picture) { width = Picture.Width; height = Picture.Height; }
                float scale = Math.Min(box.Width / width, box.Height / height);
                BannerArt.Draw(graphics, Picture, new RectangleF((Width - width * scale) / 2f, (Height - height * scale) / 2f, width * scale, height * scale), grey);
            }
            else
            {
                Bitmap disc = pressed && down != null ? down : up;
                if (disc != null) BannerArt.Draw(graphics, disc, box, grey);
                else if (upName != null)
                    using (var fill = new LinearGradientBrush(box, Color.FromArgb(255, 150, 40), Color.FromArgb(205, 85, 10), 90f))
                    using (var rule = new Pen(Color.FromArgb(120, 60, 10)))
                    {
                        graphics.FillEllipse(fill, box); graphics.DrawEllipse(rule, box);
                    }
            }
            Image icon = Image;
            if (icon != null)
            {
                int width, height;
                lock (icon) { width = icon.Width; height = icon.Height; }
                float scale = Math.Min(1f, Math.Min((Width - 6f) / width, (Height - 6f) / height));
                float offset = pressed ? 1 : 0;
                BannerArt.Draw(graphics, icon, new RectangleF((Width - width * scale) / 2f + offset, (Height - height * scale) / 2f + offset, width * scale, height * scale), grey);
            }
            if (hovered && available)
                using (var light = new SolidBrush(Color.FromArgb(50, 255, 255, 255))) graphics.FillEllipse(light, box);
        }
    }

    /// <summary>
    /// Bandeau du client (<c>Banner</c>) dans le bandeau bas de S3 : au centre le cœur et les PA / PM, à droite l'illustration
    /// ronde et sa jauge, les boutons ronds (caractéristiques, sorts, inventaire, quêtes, carte, amis, guilde, monture,
    /// conquête) répartis à intervalles égaux comme <c>hideEpisodicContent</c>, l'œil des combats (<c>fC</c> &gt; 0 hors
    /// combat), le menu principal et la barre de raccourcis. Le bouton des canaux (<c>_btnHelp</c>) est celui du chat (C2).
    /// Chaque bouton ouvre un volet par son nom (<see cref="PanelHost.Find"/>) ; sans volet livré, il est grisé « à venir ».
    /// Les événements réseau arrivent sur d'autres fils : tout passe par <see cref="RequestRefresh"/>.
    /// </summary>
    public sealed class BannerPanel : Panel
    {
        /// <summary>Boutons ronds dans l'ordre du client : nom du volet, icône, clé de l'infobulle, repli, raccourci.</summary>
        private static readonly string[,] Definitions =
        {
            { "Stats", "icone-caracteristiques", "YOUR_STATS_JOB", "Tes caractéristiques et métiers", "CHARAC" },
            { "Spells", "icone-sorts", "YOUR_SPELLS", "Tes sorts", "SPELLS" },
            { "Inventory", "icone-inventaire", "YOUR_INVENTORY", "Ton inventaire", "INVENTORY" },
            { "Quests", "icone-quetes", "YOUR_QUESTS", "Tes quêtes", "QUESTS" },
            { "WorldMap", "icone-carte", "YOUR_BOOK", "Ta géoposition", "MAP" },
            { "Friends", "icone-amis", "YOUR_FRIENDS", "Tes amis", "FRIENDS" },
            { "Guild", "icone-guilde", "YOUR_GUILD", "Ta guilde", "GUILD" },
            { "Mount", "icone-monture", "MY_MOUNT", "Ta monture", "MOUNT" },
            { "Conquest", "icone-pvp", "CONQUEST_WORD", "Conquête", "" },
        };
        public const string FightsPanelName = "FightsList";
        private const int RowGap = 5;
        private readonly Accounts account;
        private readonly PanelHost panels;
        private readonly BotOptions options;
        private readonly List<BannerButton> buttons = new List<BannerButton>();
        private readonly BannerButton fights, mainMenuButton;
        private readonly ToolTip tips = new ToolTip { ShowAlways = true };
        private readonly Timer ticker = new Timer { Interval = 1000 };
        private readonly Panel center;
        private bool refreshPending, bound, released;
        private int lastSubArea = -1, lastMapId = -1;
        private CombatPhase lastPhase = CombatPhase.None;
        private ContextMenuStrip xtraMenu;
        private ShortcutTable shortcutTable;

        public BannerPanel(Accounts account, PanelHost panels, BotOptions options)
        {
            this.account = account; this.panels = panels; this.options = options ?? BotOptions.Current;
            DoubleBuffered = true; BackColor = BotUi.FrameLight; Margin = new Padding(0); Padding = new Padding(0); Name = "banner";
            Gauge = new CircleGauge();
            Xtra = new XtraBox { Dock = DockStyle.Fill };
            Gauge.Controls.Add(Xtra);
            Xtra.Click += (s, e) => ShowXtraMenu();
            Controls.Add(Gauge);
            for (int i = 0; i < Definitions.GetLength(0); i++)
            {
                var button = new BannerButton(Definitions[i, 0], Definitions[i, 1]) { AccessibleName = BannerArt.Text(Definitions[i, 2], Definitions[i, 3]) };
                string name = Definitions[i, 0];
                button.Click += (s, e) => OpenPanel(name);
                buttons.Add(button); Controls.Add(button);
            }
            fights = new BannerButton("Fights", null, null, null) { Visible = false, AccessibleName = "Combats sur cette carte" };
            BannerArt.Request(fights, "Eye2", image => { fights.Picture = image; fights.Invalidate(); });
            fights.Click += (s, e) => OpenFights();
            mainMenuButton = new BannerButton("MainMenu", null, "ButtonMainMenuUp", "ButtonMainMenuDown", 24) { AccessibleName = BannerArt.Text("MENU", "Menu principal") };
            mainMenuButton.Click += (s, e) => MainMenu.Show(mainMenuButton);
            tips.SetToolTip(mainMenuButton, BannerArt.Text("MENU", "Menu principal"));
            Controls.Add(fights); Controls.Add(mainMenuButton);
            Shortcuts = new ShortcutBar(account, this.options);
            Shortcuts.Feedback += Report;
            Controls.Add(Shortcuts);
            MainMenu = new MainMenuPopup(this);

            Heart = new LifeHeart();
            ActionPoints = new PointsViewer(PointsKind.Action);
            MovementPoints = new PointsViewer(PointsKind.Movement);
            center = new Panel { BackColor = BotUi.FrameLight, Margin = new Padding(0), Name = "banner-center" };
            center.Controls.Add(Heart); center.Controls.Add(ActionPoints); center.Controls.Add(MovementPoints);
            center.Resize += (s, e) => LayoutCenter();
            tips.SetToolTip(ActionPoints, ActionPoints.AccessibleName);
            tips.SetToolTip(MovementPoints, MovementPoints.AccessibleName);

            CenterText = new CenterText();
            MapInfos = new MapInfosLabel();

            ticker.Tick += (s, e) => Tick();
            HandleCreated += (s, e) => { if (!released) ticker.Start(); RequestRefresh(); };
            Bind();
        }

        public LifeHeart Heart { get; }
        public PointsViewer ActionPoints { get; }
        public PointsViewer MovementPoints { get; }
        public CircleGauge Gauge { get; }
        public XtraBox Xtra { get; }
        public ShortcutBar Shortcuts { get; }
        public MainMenuPopup MainMenu { get; }
        /// <summary>Texte centré de la carte ; la fenêtre de jeu le pose sur la zone de la carte.</summary>
        public CenterText CenterText { get; }
        /// <summary>Nom et coordonnées de la carte (option <c>MapInfos</c>), posés sur la zone de la carte.</summary>
        public MapInfosLabel MapInfos { get; }
        /// <summary>Contenu de l'emplacement central du bandeau bas (cœur, PA, PM).</summary>
        public Control Center => center;
        public IReadOnlyList<BannerButton> Buttons => buttons;
        public BannerButton FightsButton => fights;
        public BannerButton MainMenuButton => mainMenuButton;
        /// <summary>Œil des combats affiché (<c>updateEye</c> : <c>fC</c> &gt; 0 et pas en combat).</summary>
        public bool FightsShown { get; private set; }
        public ContextMenuStrip XtraMenu => xtraMenu;
        /// <summary>Infobulles du bandeau (tests et accessibilité).</summary>
        public string TooltipOf(Control control) => control == null ? null : tips.GetToolTip(control);

        /// <summary>Retour d'action court (bouton « à venir », refus du client, envoi d'un paquet).</summary>
        public event Action<string> Feedback;

        public BannerButton this[string panelName] => buttons.FirstOrDefault(b => string.Equals(b.PanelName, panelName, StringComparison.Ordinal));

        /// <summary>Remplit le bandeau bas : centre = cœur et PA / PM, droite = ce bandeau.</summary>
        public void Fill(HudPanel hud)
        {
            if (hud == null) return;
            hud.SetSlot(HudSlot.Center, center);
            hud.SetSlot(HudSlot.Right, this);
        }

        /// <summary>
        /// Ouvre (ou referme, comme <c>loadUIAutoHideComponent</c>) le volet <paramref name="name"/>. Sans volet livré : rien
        /// n'est ouvert et le bandeau dit « à venir » ; guilde sans guilde : message <c>UI_ONLY_FOR_GUILD</c> du client.
        /// </summary>
        public bool OpenPanel(string name)
        {
            BannerButton button = this[name];
            string title = button?.AccessibleName ?? name;
            IGamePanel panel = panels?.Find(name);
            if (panel == null)
            {
                if (button != null) button.Available = false;
                Report(title + " : à venir (volet pas encore livré).");
                return false;
            }
            if (name == "Guild" && account?.Game?.character != null && !account.Game.character.HasGuild)
            {
                Report(BannerArt.Text("UI_ONLY_FOR_GUILD", "Pour accéder à cette interface, il faut que tu fasses partie d'une guilde."));
                return false;
            }
            panels.Toggle(panel);
            return true;
        }

        private void OpenFights()
        {
            if (account?.Game?.Fight?.IsInFight == true) return;
            IGamePanel panel = panels?.Find(FightsPanelName);
            if (panel == null) { Report("Combats de la carte : à venir (volet pas encore livré)."); return; }
            panels.Show(panel);
        }

        /// <summary>Rafraîchissement regroupé, depuis n'importe quel fil.</summary>
        public void RequestRefresh()
        {
            if (released || IsDisposed) return;
            lock (ticker) { if (refreshPending) return; refreshPending = true; }
            Control target = IsHandleCreated ? (Control)this : FindForm();
            if (target == null || !target.IsHandleCreated || target.IsDisposed)
            {
                lock (ticker) refreshPending = false;
                if (!InvokeRequired) RefreshAll();
                return;
            }
            try { target.BeginInvoke((Action)(() => { lock (ticker) refreshPending = false; if (!released && !IsDisposed) RefreshAll(); })); }
            catch (InvalidOperationException) { lock (ticker) refreshPending = false; }
        }

        /// <summary>Recopie tout l'état de la session dans le bandeau (thread de l'interface).</summary>
        public void RefreshAll()
        {
            if (released || IsDisposed || account?.Game == null) return;
            var game = account.Game;
            var character = game.character;
            var fight = game.Fight;
            var stats = character?.stats;
            bool inFight = fight != null && fight.IsInFight;

            // Vie, PA, PM : en combat, valeurs du combattant ; sinon celles du personnage (As).
            int life = stats?.VitalityActual ?? 0, maxLife = stats?.MaxVitality ?? 0;
            int ap = stats?.PA?.StatsTotal ?? -1, mp = stats?.PM?.StatsTotal ?? -1;
            if (inFight && character != null)
            {
                if (fight.Fighters.TryGetValue(character.id, out CombatFighter me) && me.Life >= 0) { life = me.Life; if (me.MaximumLife > 0) maxLife = me.MaximumLife; }
                if (fight.ActionPoints >= 0) ap = fight.ActionPoints;
                if (fight.MovementPoints >= 0) mp = fight.MovementPoints;
            }
            Heart.SetLife(life, maxLife);
            tips.SetToolTip(Heart, BannerArt.Text("HELP_LIFE", "Points de vie") + "\n" + life.ToString(CultureInfo.InvariantCulture) + " / " + maxLife.ToString(CultureInfo.InvariantCulture)
                + " (" + Heart.Percent.ToString(CultureInfo.InvariantCulture) + " %)");
            ActionPoints.Value = ap; MovementPoints.Value = mp;

            // Jauge et illustration.
            string gaugeTip;
            if (inFight) { RefreshChrono(); gaugeTip = "Temps du tour"; }
            else gaugeTip = Gauge.Show(options.BannerGaugeMode, character);
            if (character != null && character.GFX > 0) Xtra.SetArtwork(character.GFX);
            Xtra.SetTime(game.Session?.EstimatedServerTime);
            var map = game.Map;
            if (map != null && map.HasMapData) Xtra.SetCurrentCoordinates(new Point(map.X, map.Y));
            tips.SetToolTip(Xtra, Xtra.Tooltip + "\n" + gaugeTip);

            // Boutons : volet livré ou « à venir », infobulles du client (+ pods pour l'inventaire).
            for (int i = 0; i < buttons.Count; i++)
            {
                BannerButton button = buttons[i];
                button.Available = panels?.Find(button.PanelName) != null;
                string tip = button.AccessibleName;
                if (button.PanelName == "Inventory" && character?.Inventory != null && character.Inventory.Pods_Max > 0)
                    tip += "\n\n" + BannerArt.Text("PLAYER_WEIGHT", "%1 pods sur %2", BannerArt.Thousands(character.Inventory.Actual_pods), BannerArt.Thousands(character.Inventory.Pods_Max));
                string shortcut = Definitions[i, 4];
                ShortcutDefinition key = string.IsNullOrEmpty(shortcut) ? null : Table[shortcut];
                if (key != null && options.BannerShortcuts) tip += "\nRaccourci : " + key.Label;
                if (!button.Available) tip += "\nÀ venir : volet pas encore livré.";
                tips.SetToolTip(button, tip);
            }

            // Œil des combats (updateEye).
            int count = game.Interactions?.MapActions?.FightCount ?? 0;
            FightsShown = count != 0 && !inFight;
            fights.Visible = FightsShown;
            fights.Available = panels?.Find(FightsPanelName) != null;
            if (FightsShown)
                tips.SetToolTip(fights, LangData.Combine(BannerArt.Text("FIGHTS_ON_MAP", "%1 combat{~ps} sur cette carte", count.ToString(CultureInfo.InvariantCulture)), null, count < 2));

            // Texte centré : nouvelle sous-zone (onMapLoaded), début du combat (GAME_LAUNCH).
            if (map != null && map.HasMapData && map.MapID != lastMapId)
            {
                lastMapId = map.MapID;
                MapInfos.SetMap(map.MapID, map.X, map.Y);
                int subArea = LangData.Map.SubAreaId(map.MapID) ?? -1;
                if (subArea != lastSubArea && !inFight)
                {
                    lastSubArea = subArea;
                    string zone = CenterText.ZoneText(map.MapID);
                    if (zone != null) CenterText.Show(zone, 2000, false);
                }
            }
            MapInfos.Visible = options.MapInfos && map != null && map.HasMapData;
            CombatPhase phase = fight?.Phase ?? CombatPhase.None;
            if (phase == CombatPhase.Active && lastPhase == CombatPhase.Placement)
                CenterText.Show(BannerArt.Text("GAME_LAUNCH", "Le combat commence !"), 2000, true);
            lastPhase = phase;

            Shortcuts.RefreshContent();
        }

        /// <summary>Table des raccourcis en vigueur (reconstruite quand les touches changent).</summary>
        public ShortcutTable Table => shortcutTable ?? (shortcutTable = Bot.Shortcuts.Build(options));

        private void RefreshChrono()
        {
            var fight = account?.Game?.Fight;
            if (fight == null || !fight.IsInFight) return;
            FightTimeline timeline = fight.Timeline;
            int duration = timeline.TurnDurationMilliseconds;
            if (fight.IsPlacement || duration <= 0) { Gauge.ShowChrono(0, 0); return; }
            Gauge.ShowChrono(timeline.RemainingMilliseconds(DateTime.UtcNow), duration);
        }

        private void Tick()
        {
            if (released || account?.Game == null) return;
            if (account.Game.Fight?.IsInFight == true) RefreshChrono();
            Xtra.SetTime(account.Game.Session?.EstimatedServerTime);
        }

        private void ShowXtraMenu()
        {
            xtraMenu?.Dispose();
            var menu = new ContextMenuStrip { Renderer = new RetroMenuRenderer(), BackColor = BotUi.Paper, Font = BotFonts.Get(9), ShowItemToolTips = true,
                AccessibleName = "Illustration du bandeau" };
            var gauge = new ToolStripMenuItem(BannerArt.Text("SHOW", "Afficher") + " >>");
            string xp = BannerArt.Text("WORD_XP", "XP");
            foreach (var entry in new[] {
                Tuple.Create("none", BannerArt.Text("DISABLE", "Désactiver")), Tuple.Create("xp", xp),
                Tuple.Create("xpcurrentjob", xp + " " + BannerArt.Text("JOB", "Métier")), Tuple.Create("xpmount", xp + " " + BannerArt.Text("MOUNT", "Monture")),
                Tuple.Create("pods", BannerArt.Text("WEIGHT", "Pods")), Tuple.Create("energy", BannerArt.Text("ENERGY", "Energie")) })
            {
                string mode = entry.Item1;
                gauge.DropDownItems.Add(new ToolStripMenuItem(entry.Item2, null, (s, e) => options.BannerGaugeMode = mode) { Checked = options.BannerGaugeMode == mode });
            }
            menu.Items.Add(gauge);
            menu.Items.Add(new ToolStripSeparator());
            foreach (var entry in new[] {
                Tuple.Create("artwork", BannerArt.Text("BANNER_ARTWORK", "Portrait")), Tuple.Create("clock", BannerArt.Text("BANNER_CLOCK", "Horloge")),
                Tuple.Create("compass", BannerArt.Text("BANNER_COMPASS", "Boussole")), Tuple.Create("helper", BannerArt.Text("BANNER_HELPER", "Boune")),
                Tuple.Create("map", BannerArt.Text("BANNER_MAP", "Mini carte")) })
            {
                string mode = entry.Item1;
                bool delivered = mode != "helper" && mode != "map";
                var item = new ToolStripMenuItem(entry.Item2, null, (s, e) => { options.BannerIllustrationMode = mode; Xtra.Mode = mode; RequestRefresh(); })
                    { Checked = Xtra.DisplayedMode == mode, Enabled = delivered };
                if (!delivered) item.ToolTipText = "À venir : illustration pas encore livrée.";
                menu.Items.Add(item);
            }
            xtraMenu = menu;
            if (Xtra.IsHandleCreated) menu.Show(Xtra, new Point(Xtra.Width / 2, Xtra.Height / 2));
        }

        private void Bind()
        {
            if (bound || account?.Game == null) return;
            bound = true;
            var game = account.Game;
            Xtra.Mode = options.BannerIllustrationMode;
            if (game.character != null)
            {
                game.character.RefreshCaracteristiques += RequestRefresh; game.character.SeeLifeRegen += RequestRefresh;
                game.character.PodsRefresh += RequestRefresh; game.character.Jobs_Refresh += RequestRefresh; game.character.Spells_Refresh += RequestRefresh;
                if (game.character.Inventory != null) game.character.Inventory.RefreshInventory += OnInventory;
            }
            if (game.Fight != null) game.Fight.CombatChanged += RequestRefresh;
            if (game.Map != null) game.Map.RefreshMap += RequestRefresh;
            if (game.Interactions?.MapActions != null) game.Interactions.MapActions.FightsChanged += RequestRefresh;
            if (game.Interactions?.Party != null) game.Interactions.Party.CompassChanged += OnCompass;
            options.OptionChanged += OnOption;
        }

        /// <summary>Détache le bandeau de la session (avant la libération du compte) ; idempotent.</summary>
        public void Release()
        {
            if (released) return;
            released = true;
            ticker.Stop();
            if (!bound || account?.Game == null) { options.OptionChanged -= OnOption; return; }
            var game = account.Game;
            if (game.character != null)
            {
                game.character.RefreshCaracteristiques -= RequestRefresh; game.character.SeeLifeRegen -= RequestRefresh;
                game.character.PodsRefresh -= RequestRefresh; game.character.Jobs_Refresh -= RequestRefresh; game.character.Spells_Refresh -= RequestRefresh;
                if (game.character.Inventory != null) game.character.Inventory.RefreshInventory -= OnInventory;
            }
            if (game.Fight != null) game.Fight.CombatChanged -= RequestRefresh;
            if (game.Map != null) game.Map.RefreshMap -= RequestRefresh;
            if (game.Interactions?.MapActions != null) game.Interactions.MapActions.FightsChanged -= RequestRefresh;
            if (game.Interactions?.Party != null) game.Interactions.Party.CompassChanged -= OnCompass;
            options.OptionChanged -= OnOption;
        }

        private void OnInventory(bool changed) => RequestRefresh();

        /// <summary><c>IC</c> (fil réseau) : boussole vers la cible, ou portrait quand elle est effacée.</summary>
        private void OnCompass(Point? target)
        {
            Control marshal = IsHandleCreated ? (Control)this : FindForm();
            if (marshal == null || !marshal.IsHandleCreated) { if (!InvokeRequired) SetCompassTarget(target); return; }
            BotUi.OnUi(marshal, () => { if (!released && !IsDisposed) SetCompassTarget(target); });
        }

        /// <summary>Cible de la boussole (paquet <c>IC</c>, lien <c>[x,y]</c> du chat) : <c>GameManager.updateCompass</c>.</summary>
        public void SetCompassTarget(Point? target)
        {
            var map = account?.Game?.Map;
            if (map != null && map.HasMapData) Xtra.SetCurrentCoordinates(new Point(map.X, map.Y));
            Xtra.SetCompassTarget(target);
            tips.SetToolTip(Xtra, Xtra.Tooltip);
        }

        private void OnOption(object sender, BotOptionChangedEventArgs e)
        {
            if (e.Name == BotOptions.ShortcutsOption) shortcutTable = null;
            else if (e.Name == "BannerIllustrationMode") Xtra.Mode = options.BannerIllustrationMode;
            RequestRefresh();
        }

        private void Report(string message) { if (!string.IsNullOrEmpty(message)) Feedback?.Invoke(message); }

        private void LayoutCenter()
        {
            int width = center.ClientSize.Width, height = center.ClientSize.Height;
            int heartWidth = Math.Min(54, Math.Max(30, width - 2 * ActionPoints.Width - 2));
            Heart.Size = new Size(heartWidth, heartWidth * 40 / 44 + 2);
            int top = Math.Max(0, (height - Heart.Height - 4) / 2);
            Heart.Location = new Point((width - Heart.Width) / 2, top);
            int pointsTop = Math.Max(0, top + Heart.Height - ActionPoints.Height + 6);
            ActionPoints.Location = new Point(Math.Max(0, Heart.Left - ActionPoints.Width + 2), pointsTop);
            MovementPoints.Location = new Point(Math.Min(width - MovementPoints.Width, Heart.Right - 2), pointsTop);
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            int height = ClientSize.Height, width = ClientSize.Width;
            int side = Math.Max(40, Math.Min(height - 4, 92));
            Gauge.SetBounds(0, Math.Max(0, (height - side) / 2), side, side);
            int left = Gauge.Right + 6;
            int right = width - 2;
            mainMenuButton.Location = new Point(right - mainMenuButton.Width, 3);
            int eyeRight = mainMenuButton.Left - 4;
            fights.Location = new Point(eyeRight - fights.Width, 2);
            int available = Math.Max(0, fights.Left - 4 - left);
            int count = buttons.Count;
            // Intervalles égaux entre le premier et le dernier bouton (hideEpisodicContent), bornés pour rester groupés.
            int gap = count > 1 ? Math.Max(1, Math.Min(10, (available - count * BannerButton.Side) / (count - 1))) : 0;
            for (int i = 0; i < count; i++) buttons[i].Location = new Point(left + i * (BannerButton.Side + gap), 2);
            Shortcuts.Location = new Point(left, 2 + BannerButton.Side + RowGap);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Release();
                ticker.Dispose(); tips.Dispose(); xtraMenu?.Dispose(); MainMenu.Dispose();
                if (!center.IsDisposed && center.Parent == null) center.Dispose();
                if (!CenterText.IsDisposed && CenterText.Parent == null) CenterText.Dispose();
                if (!MapInfos.IsDisposed && MapInfos.Parent == null) MapInfos.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
