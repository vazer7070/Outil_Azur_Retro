using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Accounts;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>Volet concerné par un changement du tiroir.</summary>
    public sealed class PanelEventArgs : EventArgs
    {
        public IGamePanel Panel { get; }
        internal PanelEventArgs(IGamePanel panel) { Panel = panel; }
    }

    /// <summary>
    /// Tiroir latéral de la fenêtre de jeu : pile de volets (le dernier affiché est au-dessus), un seul volet de fenêtre
    /// serveur (<see cref="GamePanel.IsModal"/>) à la fois, onglets des volets ouverts, en-tête avec icône du client et
    /// bouton de fermeture. Palette du client Retro : parchemin, filets dorés, cadre brun. Toutes les méthodes s'appellent
    /// sur le thread de l'interface ; les volets y reviennent eux-mêmes avant d'appeler le tiroir.
    /// </summary>
    public sealed class PanelHost : Panel
    {
        /// <summary>Nombre maximal de volets empilés ; au-delà, le plus ancien volet ordinaire est retiré.</summary>
        public const int MaxDepth = 6;
        private readonly List<IGamePanel> registered = new List<IGamePanel>();
        private readonly List<IGamePanel> stack = new List<IGamePanel>();
        private readonly Panel header, content;
        private readonly FlowLayoutPanel tabs;
        private readonly PictureBox icon;
        private readonly Label title;
        private readonly ClientButton close;
        private readonly ToolTip tips = new ToolTip();
        private Accounts account;

        public PanelHost()
        {
            SetStyle(ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true; BackColor = BotUi.Paper; Padding = new Padding(9); Visible = false;
            content = new Panel { Dock = DockStyle.Fill, BackColor = BotUi.Paper, Margin = new Padding(0) };
            tabs = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 25, WrapContents = false, AutoScroll = false, Visible = false,
                BackColor = BotUi.Paper, Margin = new Padding(0), Padding = new Padding(0, 2, 0, 0) };
            header = new Panel { Dock = DockStyle.Top, Height = 28, BackColor = BotUi.Paper };
            icon = new PictureBox { Dock = DockStyle.Left, Width = 0, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent,
                Padding = new Padding(0, 2, 6, 4) };
            title = new Label { Dock = DockStyle.Fill, Font = BotFonts.Get(11, FontStyle.Bold), ForeColor = BotUi.Ink,
                BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            close = new ClientButton { Text = "×", Width = 28, Height = 24, Dock = DockStyle.Right, Font = BotFonts.Get(8),
                Tag = "client-icon", AccessibleName = "Fermer le volet", Margin = new Padding(0) };
            close.Click += (s, e) => CloseCurrent();
            tips.SetToolTip(close, "Fermer le volet (Échap)");
            header.Controls.Add(title); header.Controls.Add(icon); header.Controls.Add(close);
            Controls.Add(content); Controls.Add(tabs); Controls.Add(header);
        }

        /// <summary>Compte de la session ; le remplacer associe tous les volets à la nouvelle partie.</summary>
        public Accounts Account
        {
            get => account;
            set { account = value; foreach (IGamePanel panel in registered.ToArray()) panel.Bind(value?.Game); }
        }
        public GameClass Game => account?.Game;
        public bool IsConnected => account?.Connexion != null && account.Connexion.IsConnected();
        /// <summary>Volet au-dessus de la pile (celui qui est affiché), ou <c>null</c>.</summary>
        public IGamePanel Current => stack.Count == 0 ? null : stack[stack.Count - 1];
        /// <summary>Volets ouverts, du plus ancien au plus récent.</summary>
        public IReadOnlyList<IGamePanel> Stack => stack.ToArray();
        public IReadOnlyList<IGamePanel> Registered => registered.ToArray();
        /// <summary>Message court destiné au bandeau (retour d'action d'un volet).</summary>
        public event Action<string> Feedback;
        public event EventHandler<PanelEventArgs> PanelShown;
        public event EventHandler<PanelEventArgs> PanelClosed;

        public bool IsOpen(IGamePanel panel) => panel != null && stack.Contains(panel);
        /// <summary>Premier volet enregistré du type demandé.</summary>
        public T Get<T>() where T : class, IGamePanel => registered.OfType<T>().FirstOrDefault();

        /// <summary>
        /// Volet enregistré sous le nom d'un bouton du bandeau : type <c>&lt;nom&gt;Panel</c> (« Stats » → <c>StatsPanel</c>)
        /// ou type du même nom ; <c>null</c> si aucun volet de ce nom n'est livré.
        /// </summary>
        public IGamePanel Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return registered.FirstOrDefault(panel => string.Equals(panel.GetType().Name, name + "Panel", StringComparison.Ordinal))
                ?? registered.FirstOrDefault(panel => string.Equals(panel.GetType().Name, name, StringComparison.Ordinal));
        }

        /// <summary>Affiche le volet nommé (<see cref="Find"/>) ; <c>false</c> s'il n'existe pas.</summary>
        public bool Open(string name)
        {
            IGamePanel panel = Find(name);
            if (panel == null) return false;
            Show(panel);
            return true;
        }

        /// <summary>Bascule le volet nommé (<see cref="Find"/>) ; <c>false</c> s'il n'existe pas.</summary>
        public bool Toggle(string name)
        {
            IGamePanel panel = Find(name);
            if (panel == null) return false;
            Toggle(panel);
            return true;
        }

        /// <summary>Insère le volet dans le tiroir sans l'afficher : il est associé à la session et reçoit ses événements.</summary>
        public void Register(IGamePanel panel)
        {
            if (panel == null) throw new ArgumentNullException(nameof(panel));
            if (registered.Contains(panel)) return;
            registered.Add(panel);
            (panel as GamePanel)?.Attach(this);
            panel.Closed += OnPanelClosed;
            Control view = panel.View;
            view.Dock = DockStyle.Fill; view.Visible = false;
            content.Controls.Add(view);
            panel.Bind(Game);
        }

        /// <summary>Affiche le volet au-dessus de la pile (l'enregistre au besoin).</summary>
        public void Show(IGamePanel panel)
        {
            if (panel == null || IsDisposed) return;
            Register(panel);
            if (IsModal(panel))
                foreach (IGamePanel other in stack.Where(entry => !ReferenceEquals(entry, panel) && IsModal(entry)).ToArray()) Close(other);
            stack.Remove(panel); stack.Add(panel);
            while (stack.Count > MaxDepth)
            {
                IGamePanel oldest = stack.FirstOrDefault(entry => !ReferenceEquals(entry, panel) && !IsModal(entry)) ?? stack[0];
                Close(oldest);
            }
            Activate();
            (panel as GamePanel)?.RefreshView();
            PanelShown?.Invoke(this, new PanelEventArgs(panel));
        }

        /// <summary>Raccourci du client : ferme le volet s'il est affiché, l'affiche sinon.</summary>
        public void Toggle(IGamePanel panel)
        {
            if (panel == null) return;
            if (ReferenceEquals(Current, panel)) RequestClose(panel);
            else Show(panel);
        }

        /// <summary>Retire le volet de la pile sans rien envoyer au serveur. Renvoie <c>false</c> s'il n'était pas ouvert.</summary>
        public bool Close(IGamePanel panel)
        {
            if (panel == null || !stack.Remove(panel)) return false;
            if (!panel.View.IsDisposed) panel.View.Visible = false;
            Activate();
            PanelClosed?.Invoke(this, new PanelEventArgs(panel));
            return true;
        }

        /// <summary>
        /// Fermeture demandée par l'utilisateur : un volet de fenêtre serveur encore ouverte envoie la sortie du client
        /// et reste affiché jusqu'à la réponse ; les autres volets sont retirés.
        /// </summary>
        public void RequestClose(IGamePanel panel)
        {
            if (panel == null || !stack.Contains(panel)) return;
            var game = panel as GamePanel;
            if (game != null && !game.OnUserClose()) return;
            Close(panel);
        }

        /// <summary>Bouton × et touche Échap : ferme le volet affiché.</summary>
        public void CloseCurrent() => RequestClose(Current);

        /// <summary>Retire tous les volets, sauf ceux dont la fenêtre est encore ouverte côté serveur.</summary>
        public void CloseAll()
        {
            foreach (IGamePanel panel in stack.ToArray())
                if (!(panel is GamePanel game && game.IsServerWindowOpen)) Close(panel);
        }

        /// <summary>Rafraîchit le volet affiché (les autres se rafraîchissent sur leurs propres événements ou à l'affichage).</summary>
        public void RefreshVisible()
        {
            if (Current is GamePanel game) game.RefreshView();
        }

        /// <summary>Détache tous les volets de la session (avant la libération du compte).</summary>
        public void ReleaseSession()
        {
            foreach (IGamePanel panel in registered.ToArray()) panel.Bind(null);
            account = null;
        }

        internal void Report(string message) => Feedback?.Invoke(message);

        private static bool IsModal(IGamePanel panel) => panel is GamePanel game && game.IsModal;

        private void OnPanelClosed(object sender, EventArgs e)
        {
            var panel = sender as IGamePanel; if (panel == null) return;
            BotUi.OnUi(this, () => Close(panel));
        }

        private void Activate()
        {
            IGamePanel top = Current;
            foreach (IGamePanel panel in registered)
                if (!panel.View.IsDisposed) panel.View.Visible = ReferenceEquals(panel, top);
            if (top == null) { Visible = false; RebuildTabs(); return; }
            title.Text = top.Title ?? string.Empty;
            Image image = top.Icon;
            icon.Image = image; icon.Width = image == null ? 0 : 30;
            top.View.BringToFront();
            RebuildTabs();
            Visible = true; BringToFront();
        }

        private void RebuildTabs()
        {
            tabs.SuspendLayout();
            foreach (Control old in tabs.Controls.Cast<Control>().ToArray()) { tips.SetToolTip(old, null); tabs.Controls.Remove(old); old.Dispose(); }
            foreach (IGamePanel panel in stack)
            {
                var tab = new PanelTab(panel, ReferenceEquals(panel, Current));
                tab.Click += (s, e) => Show(((PanelTab)s).Panel);
                tips.SetToolTip(tab, panel.Title);
                tabs.Controls.Add(tab);
            }
            tabs.Visible = stack.Count > 1;
            tabs.ResumeLayout();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); if (Width < 3 || Height < 3) return;
            using (var dark = new Pen(Color.FromArgb(30, 29, 24)))
            using (var light = new Pen(BotUi.Gold)) {
                e.Graphics.DrawRectangle(dark, 0, 0, Width - 1, Height - 1);
                e.Graphics.DrawRectangle(light, 1, 1, Width - 3, Height - 3);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (IGamePanel panel in registered.ToArray())
                {
                    panel.Closed -= OnPanelClosed;
                    try { panel.Dispose(); }
                    catch (ObjectDisposedException) { }
                }
                registered.Clear(); stack.Clear(); tips.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>Onglet d'un volet ouvert : parchemin pour le volet affiché, brun du cadre pour les autres.</summary>
        private sealed class PanelTab : Control
        {
            internal IGamePanel Panel { get; }
            private readonly bool active;

            internal PanelTab(IGamePanel panel, bool isActive)
            {
                Panel = panel; active = isActive;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
                Font = BotFonts.Get(8, isActive ? FontStyle.Bold : FontStyle.Regular);
                Text = panel.Title ?? string.Empty; Cursor = Cursors.Hand; Height = 22; Margin = new Padding(0, 0, 3, 0);
                AccessibleName = "Volet " + Text; AccessibleRole = AccessibleRole.PageTab;
                Width = Math.Min(150, TextRenderer.MeasureText(Text, Font).Width + (panel.Icon == null ? 14 : 32));
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var area = new Rectangle(0, 0, Width - 1, Height - 1);
                using (var fill = new SolidBrush(active ? BotUi.PaperLight : BotUi.FrameLight)) e.Graphics.FillRectangle(fill, area);
                using (var border = new Pen(BotUi.Gold)) e.Graphics.DrawRectangle(border, area);
                int left = 6;
                if (Panel.Icon != null) { ClientAssets.DrawFit(e.Graphics, Panel.Icon, new RectangleF(5, 3, 16, Height - 6)); left = 24; }
                TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(left, 0, Width - left - 4, Height), active ? BotUi.Ink : BotUi.PaperLight,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }
    }
}
