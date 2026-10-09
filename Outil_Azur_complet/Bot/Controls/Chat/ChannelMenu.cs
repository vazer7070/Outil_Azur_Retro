using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Chat;
using Tool_BotProtocol.Game.Maps.Entities;

namespace Outil_Azur_complet.Bot.Controls.Chat
{
    /// <summary>
    /// Bouton et menu des canaux (<c>_btnHelp</c> du bandeau du client) : en-tête « Destinataires des messages »
    /// (<c>CHAT_PREFIX</c>), puis Défaut (/s), Équipe (/t, en combat), Groupe (/p, dans un groupe), Guilde (/g, avec une
    /// guilde), Alignement (/a, aligné), Recrutement (/r), Commerce (/b), Incarnam (/i) et Aide (<c>/help</c>). Le canal
    /// admin (/q) n'apparaît que pour un compte autorisé dans le client : le bot n'a pas cette information et ne le propose
    /// pas. Le bouton montre le préfixe choisi, ou l'icône <c>UI_BannerChatCommandAll</c> pour le canal général
    /// (<c>setChatPrefix</c>).
    /// </summary>
    public sealed class ChannelMenu : IDisposable
    {
        private const string AllChannelsIcon = "UI_BannerChatCommandAll";
        private readonly Func<Accounts> account;
        private readonly ClientButton button;
        private readonly ToolTip tips = new ToolTip();
        private string prefix = string.Empty;
        private bool disposed;

        public ChannelMenu(Func<Accounts> account)
        {
            this.account = account ?? (() => null);
            button = new ClientButton { Width = 30, Height = 24, Tag = "client-icon", Margin = new Padding(0), Font = BotFonts.Get(8.25f, FontStyle.Bold),
                Name = "chatChannel", AccessibleName = ChatUiText.Get("CHAT_MENU", "Menu du chat") };
            button.Click += (s, e) => Show();
            button.HandleCreated += (s, e) => { if (prefix.Length == 0 && button.Image == null) ApplyPrefix(); };
            tips.SetToolTip(button, ChatUiText.Get("CHAT_PREFIX", "Destinataires des messages"));
            ApplyPrefix();
        }

        /// <summary>Bouton à placer devant la saisie.</summary>
        public Button Button => button;
        /// <summary>Dernier menu affiché (diagnostic et tests).</summary>
        public ContextMenuStrip LastMenu { get; private set; }

        /// <summary>Préfixe courant (« », « /g »…) ; « /s » équivaut au canal général.</summary>
        public string Prefix
        {
            get { return prefix; }
            set
            {
                string next = value == "/s" ? string.Empty : value ?? string.Empty;
                if (next == prefix) return;
                prefix = next; ApplyPrefix();
                PrefixChanged?.Invoke(prefix);
            }
        }

        public event Action<string> PrefixChanged;
        /// <summary>Entrée « Aide » : le volet exécute <c>/help</c>.</summary>
        public event EventHandler HelpRequested;

        /// <summary>Construit le menu selon l'état du personnage ; l'appelant l'affiche ou le libère.</summary>
        public ContextMenuStrip BuildMenu()
        {
            var game = account()?.Game;
            bool inFight = game?.Fight?.IsInFight ?? false;
            bool inParty = game?.character?.InGroupe ?? false;
            var self = game?.Map?.Self as PlayerActor;
            // Sans entrée GM du personnage, la guilde et l'alignement sont inconnus : les entrées restent permises.
            bool guild = self == null || self.HasGuild || (game?.character?.HasGuild ?? false);
            bool aligned = self == null || (self.Alignment?.Side ?? 0) != 0;

            ContextMenuStrip strip = ChatLinks.NewMenu("chat-channel-menu", ChatUiText.Get("CHAT_MENU", "Menu du chat"));
            strip.Items.Add(ChatLinks.Header(ChatUiText.Get("CHAT_PREFIX", "Destinataires des messages")));
            strip.Items.Add(Entry(ChatUiText.Get("DEFAUT", "Défaut"), "/s", true));
            strip.Items.Add(Entry(ChatUiText.Get("TEAM", "Équipe"), "/t", inFight));
            strip.Items.Add(Entry(ChatUiText.Get("PARTY", "Groupe"), "/p", inParty));
            strip.Items.Add(Entry(ChatUiText.Get("GUILD", "Guilde"), "/g", guild));
            strip.Items.Add(Entry(ChatUiText.Get("ALIGNMENT", "Alignement"), "/a", aligned));
            strip.Items.Add(Entry(ChatUiText.Get("RECRUITMENT", "Recrutement"), "/r", true));
            strip.Items.Add(Entry(ChatUiText.Get("TRADE", "Commerce"), "/b", true));
            strip.Items.Add(Entry(ChatUiText.Get("MEETIC", "Incarnam"), "/i", true));
            strip.Items.Add(ChatLinks.Item(ChatUiText.Get("HELP", "Aide"), "Liste des commandes de la console (/help)",
                () => HelpRequested?.Invoke(this, EventArgs.Empty)));
            return strip;
        }

        /// <summary>Affiche le menu au-dessus du bouton.</summary>
        public ContextMenuStrip Show()
        {
            if (disposed || button.IsDisposed) return null;
            Release();
            ContextMenuStrip strip = BuildMenu();
            LastMenu = strip;
            strip.Closed += (s, e) =>
            {
                if (button.IsHandleCreated && !button.IsDisposed)
                    try { button.BeginInvoke((Action)(() => { if (ReferenceEquals(LastMenu, strip)) LastMenu = null; strip.Dispose(); })); return; }
                    catch (InvalidOperationException) { }
                if (ReferenceEquals(LastMenu, strip)) LastMenu = null;
                strip.Dispose();
            };
            try { strip.Show(button, new Point(0, 0), ToolStripDropDownDirection.AboveRight); }
            catch (Exception error) when (error is InvalidOperationException || error is ArgumentException || error is NotImplementedException)
            { account()?.Logger?.LogException("CHAT", error); }
            return strip;
        }

        private ToolStripMenuItem Entry(string label, string command, bool enabled)
        {
            string current = prefix.Length == 0 ? "/s" : prefix;
            ToolStripMenuItem item = ChatLinks.Item(label + " (" + command + ")", enabled ? "Écrire par défaut dans ce canal" : "Canal indisponible pour le personnage",
                () => Prefix = command);
            item.Enabled = enabled;
            item.Checked = string.Equals(current, command, StringComparison.Ordinal);
            item.Tag = command;
            return item;
        }

        private void ApplyPrefix()
        {
            if (prefix.Length == 0)
            {
                Bitmap icon;
                bool known = ClientAssets.TryCached("Client", AllChannelsIcon, out icon);
                button.Image = icon;
                button.Text = icon == null ? "/s" : string.Empty;
                // Lecture du PNG hors du thread de l'interface ; une absence connue n'est pas redemandée.
                if (!known) ClientAssets.GetAsync("Client", AllChannelsIcon).ContinueWith(task => Refresh(), System.Threading.Tasks.TaskScheduler.Default);
            }
            else { button.Image = null; button.Text = prefix; }
            tips.SetToolTip(button, ChatUiText.Get("CHAT_PREFIX", "Destinataires des messages") + " : " + (prefix.Length == 0 ? "/s" : prefix));
        }

        private void Refresh()
        {
            if (disposed || button.IsDisposed || !button.IsHandleCreated) return;
            try { button.BeginInvoke((Action)(() => { if (!disposed && prefix.Length == 0) ApplyPrefix(); })); }
            catch (InvalidOperationException) { }
        }

        private void Release()
        {
            ContextMenuStrip previous = LastMenu; LastMenu = null;
            if (previous == null || previous.IsDisposed) return;
            if (previous.Visible) previous.Close();
            previous.Dispose();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Release();
            tips.Dispose();
            PrefixChanged = null; HelpRequested = null;
        }
    }
}
