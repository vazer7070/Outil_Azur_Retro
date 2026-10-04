using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Chat;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>
    /// Volet « Commandes du serveur » (lot F14). Le client 1.34 n'a pas d'interface pour les commandes joueur de StarLoco :
    /// on les tape dans le canal général (« .infos »). Le volet liste celles de <see cref="ServerCommands"/> par famille,
    /// montre leur syntaxe et leurs conditions, et envoie la ligne choisie (<c>BM*|.nom [argument]|</c>) ; la réponse du
    /// serveur (<c>cs</c>) s'affiche dans le chat et sous la liste. En bas, les bascules « absent » (<c>BYA</c>) et
    /// « invisible » (<c>BYI</c>) du client : jamais envoyées seules, et chaque passage à l'état restreint demande une
    /// confirmation après l'avertissement (messages privés refusés par StarLoco). Les événements du chat et de l'état
    /// arrivent sur le fil réseau et repassent sur celui de l'interface.
    /// </summary>
    public sealed class CommandsHelpPanel : GamePanel
    {
        private const int ReplyWindowMilliseconds = 10000;
        private const string AllFamilies = "Toutes les familles";
        private enum Armed { None, Away, Invisible }

        private ComboBox family, argument;
        private ListView list;
        private Label details, status, presenceState, warning;
        private PictureBox warningIcon;
        private Control send, serverHelp, away, invisible;
        private Armed armed;
        private long replyUntil;
        private ChatService boundChat;
        private PlayerPresence boundPresence;
        /// <summary>Réactive les bascules quand une réponse du serveur n'arrive pas (fin de l'attente de <see cref="PlayerPresence"/>).</summary>
        private System.Windows.Forms.Timer pendingTimer;

        public override string Title => "Commandes du serveur";
        public override Image Icon => ClientAssets.Icon("UI_BannerChatCommandAll", 24);

        /// <summary>Liste des commandes (étiquette de chaque ligne : <see cref="ServerCommand"/>).</summary>
        public ListView CommandList => list;
        public ComboBox FamilyBox => family;
        public ComboBox ArgumentBox => argument;
        public Button SendButton => send as Button;
        public Button ServerHelpButton => serverHelp as Button;
        public Button AwayButton => away as Button;
        public Button InvisibleButton => invisible as Button;
        public string StatusText => status?.Text ?? string.Empty;
        public string DetailsText => details?.Text ?? string.Empty;
        public string PresenceText => presenceState?.Text ?? string.Empty;
        /// <summary>Avertissement affiché avant une bascule « absent » ou « invisible » (vide sinon).</summary>
        public string WarningText => warning?.Text ?? string.Empty;
        /// <summary>Commande choisie dans la liste, ou <c>null</c>.</summary>
        public ServerCommand Selected => list != null && list.SelectedItems.Count == 1 ? list.SelectedItems[0].Tag as ServerCommand : null;


        protected override Control CreateView()
        {
            var page = Page();
            var header = MakeLabel("Commandes joueur de StarLoco", 11, true);
            header.Dock = DockStyle.Top; header.Height = 24;
            var intro = MakeLabel("Elles s'écrivent dans le canal général (« . » suivi du nom) ; le serveur répond dans le chat.", 8.25f);
            intro.Dock = DockStyle.Top; intro.Height = 32; intro.ForeColor = BotUi.Muted;

            family = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, Font = BotFonts.Get(9),
                BackColor = BotUi.PaperLight, ForeColor = BotUi.Ink, Margin = new Padding(0), Name = "commands-family" };
            family.Items.Add(AllFamilies);
            foreach (ServerCommandCategory category in Enum.GetValues(typeof(ServerCommandCategory))) family.Items.Add(FamilyName(category));
            family.SelectedIndex = 0;
            family.SelectedIndexChanged += (s, e) => FillList();

            list = MakeList(9, "Commande", "Effet");
            list.Name = "commands-list"; list.AccessibleName = "Commandes du serveur";
            list.Columns[0].Width = 104; list.Columns[1].Width = 270;
            list.SelectedIndexChanged += (s, e) => { Disarm(); ShowSelected(); };
            list.DoubleClick += async (s, e) => { if (Selected != null && !Selected.ArgumentRequired) await SendSelected(); else if (argument.Enabled) argument.Focus(); };

            details = MakeLabel(string.Empty, 8.25f);
            details.Dock = DockStyle.Bottom; details.Height = 58; details.ForeColor = BotUi.Ink; details.Name = "commands-details";
            details.Padding = new Padding(0, 4, 0, 0);

            var argumentRow = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 30, ColumnCount = 2, RowCount = 1, Margin = new Padding(0),
                BackColor = BotUi.Paper };
            argumentRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74));
            argumentRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var argumentLabel = MakeLabel("Argument", 8.25f); argumentLabel.Dock = DockStyle.Fill; argumentLabel.TextAlign = ContentAlignment.MiddleLeft;
            argument = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown, Font = BotFonts.Get(9),
                BackColor = BotUi.PaperLight, ForeColor = BotUi.Ink, Margin = new Padding(0, 3, 0, 0), Name = "commands-argument",
                MaxLength = ChatService.MaxMessageLength };
            argument.KeyDown += async (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await SendSelected(); } };
            argumentRow.Controls.Add(argumentLabel, 0, 0); argumentRow.Controls.Add(argument, 1, 0);

            send = MakeButton("Envoyer", async (s, e) => await SendSelected(), true, 110);
            send.Name = "commands-send"; send.AccessibleName = "Envoyer la commande choisie";
            serverHelp = MakeButton("Aide du serveur", async (s, e) => await RequestServerHelp(), false, 140);
            serverHelp.Name = "commands-server-help"; serverHelp.AccessibleName = "Demander la liste du serveur (.commandes)";
            var actions = BotUi.Actions(send, serverHelp);

            status = MakeStatus("Choisissez une commande. Rien n'est envoyé sans clic.");
            status.Name = "commands-status";

            page.Controls.Add(list);
            page.Controls.Add(family); page.Controls.Add(intro); page.Controls.Add(header);
            page.Controls.Add(details); page.Controls.Add(argumentRow); page.Controls.Add(actions); page.Controls.Add(status);
            page.Controls.Add(BuildPresence());
            pendingTimer = new System.Windows.Forms.Timer { Interval = 500 };
            pendingTimer.Tick += (s, e) => UpdatePresence();
            FillList();
            return page;
        }

        /// <summary>Cadre « État du personnage » : bascules BYA/BYI avec l'avertissement du serveur.</summary>
        private Control BuildPresence()
        {
            var box = new RuledPanel { Dock = DockStyle.Bottom, Height = 136, BackColor = BotUi.PaperLight, Padding = new Padding(8, 6, 8, 4),
                Margin = new Padding(0, 6, 0, 0), Name = "commands-presence" };
            var title = MakeLabel("État du personnage", 9, true); title.Dock = DockStyle.Top; title.Height = 20;
            presenceState = MakeLabel(string.Empty, 8.25f); presenceState.Dock = DockStyle.Top; presenceState.Height = 18;
            presenceState.ForeColor = BotUi.Muted; presenceState.Name = "commands-presence-state";
            var warningRow = new Panel { Dock = DockStyle.Fill, BackColor = BotUi.PaperLight, Margin = new Padding(0) };
            warningIcon = new PictureBox { Dock = DockStyle.Left, Width = 24, SizeMode = PictureBoxSizeMode.Zoom, Visible = false, BackColor = BotUi.PaperLight };
            warning = MakeLabel(string.Empty, 8.25f); warning.Dock = DockStyle.Fill; warning.ForeColor = Color.FromArgb(150, 45, 30);
            warning.Name = "commands-warning";
            warningRow.Controls.Add(warning); warningRow.Controls.Add(warningIcon);
            away = MakeButton("Absent", async (s, e) => await Toggle(Armed.Away), false, 150);
            away.Name = "commands-away"; away.AccessibleName = "Basculer l'état absent (/away, BYA)";
            invisible = MakeButton("Invisible", async (s, e) => await Toggle(Armed.Invisible), false, 150);
            invisible.Name = "commands-invisible"; invisible.AccessibleName = "Basculer l'état invisible (/invisible, BYI)";
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 38, WrapContents = false, Margin = new Padding(0),
                Padding = new Padding(0, 3, 0, 0), BackColor = BotUi.PaperLight };
            buttons.Controls.Add(away); buttons.Controls.Add(invisible);
            box.Controls.Add(warningRow); box.Controls.Add(buttons); box.Controls.Add(presenceState); box.Controls.Add(title);
            return box;
        }

        protected override void OnBind(GameClass game)
        {
            armed = Armed.None;
            boundChat = game.Chat; boundPresence = game.Presence;
            if (boundChat != null) boundChat.MessageReceived += OnChatMessage;
            if (boundPresence != null) boundPresence.Changed += OnPresenceChanged;
            // Icône d'avertissement du client lue hors du fil de l'interface (cache partagé de ClientAssets, jamais libérée ici).
            Task.Run(() => ClientAssets.Get("alerte")).ContinueWith(task =>
            {
                Bitmap image = task.Status == TaskStatus.RanToCompletion ? task.Result : null;
                if (image != null) OnUi(() => { if (warningIcon != null && !warningIcon.IsDisposed) { warningIcon.Image = image; UpdatePresence(); } });
            }, TaskScheduler.Default);
        }

        protected override void OnUnbind(GameClass game)
        {
            if (boundChat != null) boundChat.MessageReceived -= OnChatMessage;
            if (boundPresence != null) boundPresence.Changed -= OnPresenceChanged;
            boundChat = null; boundPresence = null; armed = Armed.None;
            pendingTimer?.Stop();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { pendingTimer?.Dispose(); pendingTimer = null; }
        }

        protected internal override bool OnUserClose() { Disarm(); return true; }

        public override void RefreshView()
        {
            if (list == null) return;
            UpdateActions();
            UpdatePresence();
        }

        /// <summary>Choisit une commande par son nom (avec ou sans « . »), en affichant toutes les familles si besoin.</summary>
        public bool SelectCommand(string name)
        {
            if (list == null || !ServerCommands.TryGet(name, out ServerCommand command)) return false;
            ListViewItem row = list.Items.Cast<ListViewItem>().FirstOrDefault(item => ReferenceEquals(item.Tag, command));
            if (row == null) { family.SelectedIndex = 0; row = list.Items.Cast<ListViewItem>().FirstOrDefault(item => ReferenceEquals(item.Tag, command)); }
            if (row == null) return false;
            list.SelectedItems.Clear();
            row.Selected = true; row.Focused = true; row.EnsureVisible();
            ShowSelected();
            return true;
        }

        private void FillList()
        {
            ServerCommand previous = Selected;
            int index = family.SelectedIndex;
            list.BeginUpdate();
            try
            {
                list.Items.Clear();
                foreach (ServerCommand command in ServerCommands.All)
                {
                    if (index > 0 && (int)command.Category != index - 1) continue;
                    var row = list.Items.Add(command.Usage);
                    row.SubItems.Add(command.Help);
                    row.Tag = command; row.ToolTipText = command.Help;
                    if (ReferenceEquals(command, previous)) row.Selected = true;
                }
            }
            finally { list.EndUpdate(); }
            ShowSelected();
        }

        private void ShowSelected()
        {
            ServerCommand command = Selected;
            argument.Items.Clear();
            argument.Text = string.Empty;
            if (command == null)
            {
                details.Text = "Aucune commande choisie. « Aide du serveur » envoie .commandes : StarLoco répond par sa propre liste.";
                argument.Enabled = false;
            }
            else
            {
                details.Text = command.Usage + " — " + command.Help + (command.Note == null ? string.Empty : Environment.NewLine + "Conditions : " + command.Note);
                argument.Enabled = command.HasArgument;
                foreach (string choice in command.Choices) argument.Items.Add(choice);
            }
            UpdateActions();
        }

        private void UpdateActions()
        {
            if (send == null) return;
            ServerCommand command = Selected;
            bool fighting = Game?.Chat != null && ServerCommands.IsFighting(Game.Chat);
            send.Enabled = Connected && command != null && !(command.OutOfFight && fighting);
            serverHelp.Enabled = Connected;
        }

        private async Task SendSelected()
        {
            ServerCommand command = Selected;
            ChatService chat = Game?.Chat;
            if (command == null || chat == null || !send.Enabled) return;
            string value = argument.Text;
            try { Report(await ServerCommands.SendAsync(chat, command, value), command.BuildLine(value)); }
            catch (Exception error) { Fail(error); }
        }

        private async Task RequestServerHelp()
        {
            ChatService chat = Game?.Chat;
            if (chat == null || !serverHelp.Enabled) return;
            try { Report(await ServerCommands.RequestServerHelpAsync(chat), "." + ServerCommands.ServerHelp); }
            catch (Exception error) { Fail(error); }
        }

        /// <summary>Erreur inattendue d'un envoi lancé par un clic : journalisée et affichée, jamais propagée au gestionnaire.</summary>
        private void Fail(Exception error)
        {
            Account?.Logger?.LogError("INTERFACE", "Commande du serveur : " + error.Message);
            ShowError("Envoi impossible : " + error.Message);
        }

        private void Report(ChatResult result, string line)
        {
            if (status == null || status.IsDisposed) return;
            if (result.Sent)
            {
                Interlocked.Exchange(ref replyUntil, Stopwatch.GetTimestamp() + ReplyWindowMilliseconds * Stopwatch.Frequency / 1000);
                status.ForeColor = BotUi.Muted;
                status.Text = "Envoyé : " + line + " — la réponse du serveur s'affiche dans le chat.";
                Feedback("Commande envoyée : " + line);
            }
            else ShowError(result.Message);
        }

        private void ShowError(string message)
        {
            if (status == null || status.IsDisposed) return;
            status.ForeColor = Color.FromArgb(150, 45, 30);
            status.Text = message;
            Feedback(message);
        }

        /// <summary>Réponse <c>cs</c> reçue peu après un envoi (fil réseau) : recopiée sous la liste.</summary>
        private void OnChatMessage(ChatMessage message)
        {
            if (message == null || message.Kind != ChatMessageKind.Server || Stopwatch.GetTimestamp() > Interlocked.Read(ref replyUntil)) return;
            string text = message.Text.Replace("\r", " ").Replace("\n", " ");
            if (text.Length > 220) text = text.Substring(0, 219) + "…";
            OnUi(() => { if (status != null && !status.IsDisposed) { status.ForeColor = BotUi.Olive; status.Text = "Réponse du serveur : " + text; } });
        }

        private void OnPresenceChanged() => OnUi(UpdatePresence);

        /// <summary>
        /// Bascule demandée : vers « présent » ou « visible », envoi direct ; vers « absent » ou « invisible », premier clic =
        /// avertissement et bouton « Confirmer », second clic = envoi (un seul <c>BYA</c>/<c>BYI</c>).
        /// </summary>
        private async Task Toggle(Armed which)
        {
            PlayerPresence presence = Game?.Presence;
            if (presence == null || !Connected) return;
            bool restricted = which == Armed.Away ? presence.IsAway : presence.IsInvisible;
            if (!restricted && armed != which)
            {
                armed = which;
                UpdatePresence();
                return;
            }
            armed = Armed.None;
            UpdatePresence();
            try
            {
                ChatResult result = which == Armed.Away ? await presence.ToggleAwayAsync() : await presence.ToggleInvisibleAsync();
                Report(result, which == Armed.Away ? PlayerPresence.AwayPacket : PlayerPresence.InvisiblePacket);
            }
            catch (Exception error) { Fail(error); }
            UpdatePresence();
        }

        private void Disarm()
        {
            if (armed == Armed.None) return;
            armed = Armed.None;
            UpdatePresence();
        }

        private void UpdatePresence()
        {
            if (away == null || away.IsDisposed) return;
            PlayerPresence presence = Game?.Presence;
            bool isAway = presence?.IsAway == true, isInvisible = presence?.IsInvisible == true;
            bool awayPending = presence?.IsAwayPending == true, invisiblePending = presence?.IsInvisiblePending == true;
            away.Text = armed == Armed.Away ? "Confirmer : absent" : isAway ? "Revenir (BYA)" : "Absent (BYA)";
            invisible.Text = armed == Armed.Invisible ? "Confirmer : invisible" : isInvisible ? "Redevenir visible (BYI)" : "Invisible (BYI)";
            away.Enabled = Connected && presence != null && !awayPending;
            invisible.Enabled = Connected && presence != null && !invisiblePending;
            presenceState.Text = presence == null ? "Session de jeu indisponible."
                : (isAway ? "Absent" : "Présent") + " · " + (isInvisible ? "invisible" : "visible")
                    + (awayPending || invisiblePending ? " · réponse du serveur attendue" : " · état annoncé par Im037/038/050/051");
            warning.Text = armed == Armed.Away ? PlayerPresence.AwayWarning + " Cliquez de nouveau pour envoyer BYA."
                : armed == Armed.Invisible ? PlayerPresence.InvisibleWarning + " Cliquez de nouveau pour envoyer BYI."
                : "Jamais envoyé automatiquement : StarLoco remet les deux états à zéro à chaque connexion.";
            warningIcon.Visible = armed != Armed.None && warningIcon.Image != null;
            if (pendingTimer != null) pendingTimer.Enabled = awayPending || invisiblePending;
        }

        private static string FamilyName(ServerCommandCategory category)
        {
            switch (category)
            {
                case ServerCommandCategory.Information: return "Informations";
                case ServerCommandCategory.Teleportation: return "Téléportations";
                case ServerCommandCategory.Personnage: return "Personnage";
                case ServerCommandCategory.Objets: return "Objets équipés";
                case ServerCommandCategory.Groupe: return "Groupe et multicompte";
                case ServerCommandCategory.Banque: return "Banque";
                case ServerCommandCategory.Alignement: return "Alignement";
                default: return "Divers";
            }
        }

        /// <summary>Cadre parchemin clair bordé d'un filet doré, comme les encarts des fenêtres du client.</summary>
        private sealed class RuledPanel : Panel
        {
            internal RuledPanel() { SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true); }
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                using (var pen = new Pen(BotUi.Gold)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }
    }
}
