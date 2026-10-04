using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Interfaces;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Actions;
using Tool_BotProtocol.Game.Chat;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Session;

namespace Outil_Azur_complet.Bot.Controls.Chat
{
    /// <summary>
    /// Volet de discussion du bandeau, comme le chat du client 1.34 : barre des filtres et des boutons
    /// (<see cref="ChatFilterBar"/>), zone de texte (<see cref="ChatView"/>), bouton des canaux (<see cref="ChannelMenu"/>),
    /// saisie (<see cref="ChatInput"/>) et volet des smileys (<see cref="SmileyPicker"/>). Les envois passent par le service
    /// de chat du compte (lot C1) : aucune trame n'est construite ici. Les lignes affichées viennent des messages du chat
    /// (<c>cMK</c>, <c>cME</c>, <c>cs</c>, <c>M1</c>, lignes locales), des messages <c>Im</c> (information, erreur, JcJ) et des
    /// notices des actions de carte (réponse « qui est »…). Les événements du réseau sont remis sur le thread de l'interface.
    /// </summary>
    public sealed class ChatPanel : Panel
    {
        /// <summary>Hauteur ajoutée quand le chat est agrandi (<c>Chat.OPEN_OFFSET</c>).</summary>
        public const int ExpandOffset = 350;

        private readonly Accounts account;
        private readonly SynchronizationContext ui;
        private readonly int uiThread;
        private readonly HashSet<string> ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly ToolStripDropDown smileyDrop;
        private readonly ToolStripControlHost smileyHost;
        private readonly ClientButton send;
        private readonly ChatService chat;
        private readonly GameSession session;
        private readonly MapActions actions;
        private string letters = string.Empty;
        private bool expanded, released;

        public ChatPanel(Accounts account, Func<InteractionRouter> router)
        {
            this.account = account ?? throw new ArgumentNullException(nameof(account));
            ui = SynchronizationContext.Current as WindowsFormsSynchronizationContext ?? new WindowsFormsSynchronizationContext();
            uiThread = Thread.CurrentThread.ManagedThreadId;
            ChatLangBridge.Install();
            DoubleBuffered = true; BackColor = BotUi.Paper; Padding = new Padding(5); Margin = new Padding(0);
            Name = "chatPanel"; AccessibleName = "Discussion";

            View = new ChatView { Dock = DockStyle.Fill, Name = "chatView" };
            Toolbar = new ChatFilterBar { Dock = DockStyle.Top, Name = "chatFilters" };
            Channels = new ChannelMenu(() => this.account);
            Input = new ChatInput { Dock = DockStyle.Fill, Margin = new Padding(0, 1, 3, 0), Name = "chatInput" };
            send = new ClientButton { Text = "›", Size = new Size(30, 24), Margin = new Padding(0), Tag = "client-icon", Name = "chatSend",
                Font = BotFonts.Get(10f, FontStyle.Bold), AccessibleName = "Envoyer le message" };
            Channels.Button.Margin = new Padding(0, 0, 3, 0);

            var row = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 27, ColumnCount = 3, RowCount = 1, Margin = new Padding(0),
                Padding = new Padding(0, 3, 0, 0), BackColor = BotUi.Paper };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 33));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            row.Controls.Add(Channels.Button, 0, 0); row.Controls.Add(Input, 1, 0); row.Controls.Add(send, 2, 0);
            Controls.Add(View); Controls.Add(Toolbar); Controls.Add(row);

            Smileys = new SmileyPicker();
            smileyHost = new ToolStripControlHost(Smileys) { AutoSize = false, Size = Smileys.Size, Margin = Padding.Empty, Padding = Padding.Empty };
            smileyDrop = new ToolStripDropDown { Padding = Padding.Empty, BackColor = BotUi.Paper, DropShadowEnabled = false, AutoSize = true,
                Name = "chatSmileys", AccessibleName = ChatUiText.Get("CHAT_SHOW_SMILEYS", "Smileys et attitudes") };
            smileyDrop.Items.Add(smileyHost);
            smileyDrop.Closing += OnSmileysClosing;
            smileyDrop.Closed += (s, e) => Toolbar.SmileysOpen = false;

            Links = new ChatLinks(() => this.account, router, Prefill, ReportFeedback) { IsIgnored = IsIgnored, SetIgnored = SetIgnoredName };

            Toolbar.FilterClicked += OnFilterClicked;
            Toolbar.SmileysClicked += (s, e) => ShowSmileys(!SmileysOpen);
            Toolbar.SitClicked += (s, e) => _ = RunAsync(() => chat.SitAsync());
            Toolbar.OpenCloseClicked += (s, e) => Expanded = !Expanded;
            Channels.PrefixChanged += prefix => { Input.Prefix = prefix; if (Input.CanFocus) Input.Focus(); };
            Channels.HelpRequested += (s, e) => _ = RunAsync(() => chat.ExecuteAsync("/help"));
            Input.Submitted += (s, e) => Submit(e.Mode);
            Input.Notice += (text, error) => View.Append(ChatLineBuilder.Local(error ? ChatFilter.Errors : ChatFilter.Infos, error ? ChatColors.Error : ChatColors.Info, text));
            Input.Names = PresentNames;
            send.Click += (s, e) => Submit(ChatSendMode.Normal);
            View.LinkClicked += OnLinkClicked;
            View.LineMenuRequested += OnLineMenu;
            Smileys.SmileySelected += OnSmileySelected;
            Smileys.EmoteSelected += OnEmoteSelected;

            chat = account.Game?.Chat;
            session = account.Game?.Session;
            actions = account.Game?.Interactions?.MapActions;
            if (chat != null)
            {
                Input.History = chat.InputHistory; Input.WhisperHistory = chat.WhisperHistory;
                foreach (ChatMessage message in chat.Messages) AddMessage(message);
                ApplySubscriptions(chat.SubscribedChannels);
                Smileys.SetEmotes(EmoteIds());
                chat.MessageReceived += OnMessage;
                chat.Cleared += OnCleared;
                chat.SubscriptionsChanged += OnSubscriptions;
                chat.EmotesChanged += OnEmotesChanged;
            }
            if (session != null) session.ServerMessageReceived += OnServerMessage;
            if (actions != null) { actions.Notice += OnNotice; actions.ChatInputRequested += OnChatInputRequested; }
            RefreshState();
        }

        public ChatView View { get; }
        public ChatFilterBar Toolbar { get; }
        public ChatInput Input { get; }
        public ChannelMenu Channels { get; }
        public Button SendButton => send;
        public SmileyPicker Smileys { get; }
        public ChatLinks Links { get; }
        /// <summary>Volet des smileys, hébergé dans une fenêtre surgissante au-dessus de la barre.</summary>
        public ToolStripDropDown SmileysDropDown => smileyDrop;

        /// <summary>Agrandi de <see cref="ExpandOffset"/> pixels (<c>_btnOpenClose</c>) ; la fenêtre ajuste le bandeau.</summary>
        public bool Expanded
        {
            get { return expanded; }
            set
            {
                if (expanded == value || released) return;
                expanded = value; Toolbar.Expanded = value;
                try { ExpandedChanged?.Invoke(this, EventArgs.Empty); }
                catch (Exception error) when (!(error is OutOfMemoryException)) { account.Logger?.LogException("CHAT", error); }
                View.ScrollToBottom();
            }
        }

        public event EventHandler ExpandedChanged;
        /// <summary>Clic sur des coordonnées <c>[x,y]</c> (<c>updateCompass</c> du client).</summary>
        public event Action<int, int> CompassRequested;
        /// <summary>Message court pour le bandeau (résultat d'une entrée de menu), sur le thread de l'interface.</summary>
        public event Action<string> Feedback;

        public bool SmileysOpen => smileyDrop.Visible;

        /// <summary>Noms ignorés pour la session (<c>BLACKLIST_TEMPORARLY</c>).</summary>
        public IReadOnlyCollection<string> IgnoredNames => ignored.ToArray();

        public bool IsIgnored(string name) =>
            !string.IsNullOrEmpty(name) && (ignored.Contains(name) || (actions?.IsIgnored(name) ?? false));

        /// <summary>Ouvre ou ferme le volet des smileys au-dessus de son bouton.</summary>
        public void ShowSmileys(bool open)
        {
            if (released) return;
            if (!open) { if (smileyDrop.Visible) smileyDrop.Close(ToolStripDropDownCloseReason.CloseCalled); Toolbar.SmileysOpen = false; return; }
            if (smileyDrop.Visible) return;
            Smileys.SetEmotes(EmoteIds());
            smileyHost.Size = Smileys.Size;
            Rectangle anchor = Toolbar.ButtonBounds(ChatBarButton.Smileys);
            try
            {
                smileyDrop.Show(Toolbar, new Point(anchor.Right, 0), ToolStripDropDownDirection.AboveLeft);
                Toolbar.SmileysOpen = true;
            }
            catch (Exception error) when (error is InvalidOperationException || error is ArgumentException || error is NotImplementedException)
            { account.Logger?.LogException("CHAT", error); }
        }

        /// <summary>Place un texte dans la saisie (« /w nom » d'un menu) et lui donne le focus.</summary>
        public void Prefill(string text)
        {
            OnUi(() =>
            {
                Input.SetText(text);
                if (Input.CanFocus) Input.Focus();
            });
        }

        /// <summary>Envoie le contenu de la saisie selon la touche du client.</summary>
        public void Submit(ChatSendMode mode)
        {
            if (released || chat == null) return;
            string line = ChatInput.BuildLine(Input.Text, Channels.Prefix, mode);
            if (line.Length == 0) { Input.Clear(); return; }
            string body = ChatInput.MessageBody(line);
            if (body.Length > ChatService.MaxMessageLength)
            {
                // Le client coupe le texte à 200 caractères ; le bot le garde dans la saisie pour qu'il soit raccourci.
                chat.AddLocal(ChatMessageKind.Error, "Message trop long : " + body.Length + " caractères, " + ChatService.MaxMessageLength +
                    " au plus. Rien n'est envoyé.");
                return;
            }
            Input.Clear();
            _ = RunAsync(() => chat.ExecuteAsync(line));
        }

        /// <summary>État de la connexion et du combat (bouton d'envoi, bouton « s'asseoir »).</summary>
        public void RefreshState()
        {
            if (released) return;
            ChatLangBridge.Install();
            bool connected = account.Connexion != null && account.Connexion.IsConnected();
            send.Enabled = connected;
            Toolbar.SitVisible = !(account.Game?.Fight?.IsInFight ?? false);
        }

        /// <summary>Détache le volet de la session (abonnements, menus) ; appelée avant la libération du compte. Idempotente.</summary>
        public void ReleaseSession()
        {
            if (released) return;
            released = true;
            if (chat != null)
            {
                chat.MessageReceived -= OnMessage; chat.Cleared -= OnCleared;
                chat.SubscriptionsChanged -= OnSubscriptions; chat.EmotesChanged -= OnEmotesChanged;
            }
            if (session != null) session.ServerMessageReceived -= OnServerMessage;
            if (actions != null) { actions.Notice -= OnNotice; actions.ChatInputRequested -= OnChatInputRequested; }
            try { if (smileyDrop.Visible) smileyDrop.Close(); } catch (InvalidOperationException) { }
            Links.ReleaseLastMenu();
            ExpandedChanged = null; CompassRequested = null; Feedback = null;
        }

        // ------------------------------------------------------------------ événements du protocole (fil réseau)

        private void OnMessage(ChatMessage message) => OnUi(() => AddMessage(message));
        private void OnCleared() => OnUi(() => View.Clear());
        private void OnSubscriptions(string current) => OnUi(() => ApplySubscriptions(current));
        private void OnEmotesChanged() => OnUi(() => { Smileys.SetEmotes(EmoteIds()); smileyHost.Size = Smileys.Size; });
        private void OnServerMessage(ServerMessage message) => OnUi(() => View.Append(ChatLineBuilder.FromServerMessage(message)));
        private void OnChatInputRequested(string text) => Prefill(text);

        private void OnNotice(MapActionNotice notice)
        {
            if (notice == null || notice.Kind == MapActionNoticeKind.Alert) return;
            bool whois = notice.Kind == MapActionNoticeKind.Info && string.Equals(actions?.LastWhois?.Text, notice.Text, StringComparison.Ordinal);
            OnUi(() => View.Append(ChatLineBuilder.FromNotice(notice, whois)));
        }

        private void AddMessage(ChatMessage message)
        {
            if (message == null) return;
            if ((message.Kind == ChatMessageKind.Channel || message.Kind == ChatMessageKind.WhisperReceived) && IsIgnored(message.Author)) return;
            View.Append(ChatLineBuilder.FromMessage(message, account.Game?.Fight?.IsSpectator ?? false));
        }

        /// <summary>Écho <c>cC±</c> : les filtres suivent les lettres ajoutées et retirées (<c>onSubscribeChannel</c>).</summary>
        private void ApplySubscriptions(string current)
        {
            current = current ?? string.Empty;
            if (current.Length == 0) { letters = string.Empty; return; } // remise à zéro de la session : affichage inchangé
            foreach (char letter in current) if (letters.IndexOf(letter) < 0) SetFilterState(ChatChannels.FilterOfLetter(letter), true);
            foreach (char letter in letters) if (current.IndexOf(letter) < 0) SetFilterState(ChatChannels.FilterOfLetter(letter), false);
            letters = current;
        }

        private void SetFilterState(ChatFilter? filter, bool on)
        {
            if (!filter.HasValue || (int)filter.Value >= ChatFilterBar.FilterCount) return;
            Toolbar.SetFilter((int)filter.Value, on);
            View.SetFilterVisible(filter.Value, on);
        }

        // ------------------------------------------------------------------ actions de l'interface

        private void OnFilterClicked(int filter, bool on)
        {
            View.SetFilterVisible((ChatFilter)filter, on);
            if (filter == (int)ChatFilter.Errors || chat == null) return; // filtre local, comme le client
            if (account.Connexion == null || !account.Connexion.IsConnected()) return; // hors connexion : affichage seul
            _ = RunAsync(() => chat.SetFilterAsync(filter, on));
        }

        private void OnSmileySelected(int id)
        {
            if (chat == null) return;
            if (account.IsMoving()) { ReportFeedback("Smiley ignoré pendant un déplacement, comme dans le client."); return; }
            _ = RunAsync(() => chat.SmileyAsync(id));
        }

        private void OnEmoteSelected(int id)
        {
            if (chat == null) return;
            if (account.IsMoving()) { ReportFeedback("Attitude ignorée pendant un déplacement, comme dans le client."); return; }
            _ = RunAsync(() => chat.EmoteAsync(id));
        }

        private void OnLinkClicked(object sender, ChatLinkEventArgs e)
        {
            if (e.Link.Kind == ChatLinkKind.Coordinates)
            {
                if (CompassRequested != null) CompassRequested(e.Link.X, e.Link.Y);
                else View.Append(ChatLineBuilder.Local(ChatFilter.Infos, ChatColors.Info, ChatLinks.DescribeCompass(e.Link.X, e.Link.Y, account.Game?.Map)));
                return;
            }
            Links.OnPlayerClicked(e.Link, View, e.Location, e.Modifiers);
        }

        /// <summary>Clic droit sur une ligne : « Nom >> » (menu du joueur), copier, heure des messages.</summary>
        private void OnLineMenu(object sender, ChatLineEventArgs e)
        {
            ChatLine line = e.Line;
            ContextMenuStrip strip = ChatLinks.NewMenu("chat-line-menu", "Menu du message");
            if (line.Message != null && ChatLinks.IsValidName(line.Author))
            {
                var player = new ToolStripMenuItem(line.Author + " >>") { AccessibleName = "Menu du joueur " + line.Author };
                player.DropDown.Renderer = new RetroMenuRenderer(); player.DropDown.BackColor = BotUi.Paper;
                foreach (ToolStripItem item in Links.PlayerItems(line.Author)) player.DropDownItems.Add(item);
                strip.Items.Add(player);
            }
            strip.Items.Add(ChatLinks.Item("Copier le message", "Copier la ligne dans le presse-papiers", () => Copy(View.DisplayText(line))));
            strip.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem times = ChatLinks.Item("Afficher l'heure des messages", "Heure de réception devant chaque ligne",
                () => View.ShowTimestamps = !View.ShowTimestamps);
            times.Checked = View.ShowTimestamps;
            strip.Items.Add(times);
            Links.Show(strip, View, e.Location);
        }

        private void Copy(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try { Clipboard.SetText(text); ReportFeedback("Message copié."); }
            catch (Exception error) when (error is ExternalException || error is ThreadStateException || error is ArgumentException || error is NotImplementedException)
            { ReportFeedback("Copie impossible : " + error.Message); }
        }

        private void SetIgnoredName(string name, bool value)
        {
            if (!ChatLinks.IsValidName(name)) return;
            OnUi(() =>
            {
                bool changed = value ? ignored.Add(name) : ignored.Remove(name);
                if (!changed) return;
                string text = value ? ChatUiText.Get("TEMPORARY_BLACKLISTED", "%1 est ignoré(e) jusqu'à la fin de la session.", name)
                    : ChatUiText.Get("TEMPORARY_NOMORE_BLACKLISTED", "%1 n'est plus ignoré(e).", name);
                chat?.AddLocal(ChatMessageKind.Info, text);
            });
        }

        private IEnumerable<int> EmoteIds()
        {
            // L'attitude « s'asseoir » (1) a son bouton dans le client et reste proposée ici.
            IEnumerable<int> known = chat?.AvailableEmotes ?? (IEnumerable<int>)new int[0];
            return known.Concat(new[] { 1 });
        }

        private IEnumerable<string> PresentNames()
        {
            var map = account.Game?.Map;
            if (map == null) return Enumerable.Empty<string>();
            var names = map.Players.Select(player => player.Name).ToList();
            if (map.Self is PlayerActor self) names.Add(self.Name);
            return names.Where(name => !string.IsNullOrEmpty(name));
        }

        private void ReportFeedback(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            OnUi(() =>
            {
                Action<string> handler = Feedback;
                if (handler != null) handler(text);
                else View.Append(ChatLineBuilder.Local(ChatFilter.Infos, ChatColors.Info, text));
            });
        }

        private void OnSmileysClosing(object sender, ToolStripDropDownClosingEventArgs e)
        {
            // Un clic sur le bouton des smileys referme le volet par ce bouton (sinon il se rouvrirait aussitôt).
            if (e.CloseReason != ToolStripDropDownCloseReason.AppClicked || Toolbar.IsDisposed || !Toolbar.IsHandleCreated) return;
            Point cursor = Toolbar.PointToClient(Cursor.Position);
            if (Toolbar.ButtonBounds(ChatBarButton.Smileys).Contains(cursor)) e.Cancel = true;
        }

        private async Task RunAsync(Func<Task<ChatResult>> action)
        {
            try { await action().ConfigureAwait(false); }
            catch (Exception error)
            {
                account.Logger?.LogException("CHAT", error);
                OnUi(() => View.Append(ChatLineBuilder.Local(ChatFilter.Errors, ChatColors.Error, "Action du chat impossible : " + error.Message)));
            }
        }

        private void OnUi(Action action)
        {
            if (released || IsDisposed || Disposing) return;
            if (Thread.CurrentThread.ManagedThreadId == uiThread) { Safe(action); return; }
            try { ui.Post(state => { if (!released && !IsDisposed) Safe(action); }, null); }
            catch (Exception error) when (error is InvalidOperationException || error is ObjectDisposedException) { /* Fenêtre fermée. */ }
        }

        private void Safe(Action action)
        {
            try { action(); }
            catch (Exception error) when (!(error is OutOfMemoryException))
            {
                try { account.Logger?.LogException("CHAT", error); } catch (Exception) { /* Journal fermé. */ }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Width < 3 || Height < 3) return;
            using (var dark = new Pen(Color.FromArgb(30, 29, 24)))
            using (var light = new Pen(BotUi.Gold))
            {
                e.Graphics.DrawRectangle(dark, 0, 0, Width - 1, Height - 1);
                e.Graphics.DrawRectangle(light, 1, 1, Width - 3, Height - 3);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ReleaseSession();
                smileyDrop.Dispose();
                Channels.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
