using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Outil_Azur_complet.Bot.Controls.Banner
{
    /// <summary>Réponse de la boîte « Menu principal » (<c>AskMainMenu</c>).</summary>
    public enum MainMenuChoice { None, ChangeCharacter, Logoff, Quit, Cancel }

    /// <summary>
    /// Menu principal du bandeau (<c>MainMenu</c> : bouton <c>ButtonMainMenu</c>, bande <c>UI_MainMenu</c>) : « Changer de
    /// personnage / Déconnecter / Quitter » ouvre la boîte <c>AskMainMenu</c>, « Menu d'options » la fenêtre d'options. Le
    /// manuel, le rapport de bug et l'abonnement du client sont des liens du site officiel, sans objet pour un serveur
    /// privé : ils restent grisés avec leur explication. Échap sans volet ouvert affiche aussi <c>AskMainMenu</c>.
    /// </summary>
    public sealed class MainMenuPopup : IDisposable
    {
        private readonly Control owner;
        private ContextMenuStrip menu;
        private MainMenuDialog dialog;
        private TaskCompletionSource<MainMenuChoice> pending;

        public MainMenuPopup(Control owner) { this.owner = owner; }

        public event EventHandler OptionsRequested;
        public event EventHandler ChangeCharacterRequested;
        public event EventHandler LogoffRequested;
        public event EventHandler QuitRequested;

        /// <summary>Dernier menu affiché (tests).</summary>
        public ContextMenuStrip LastMenu => menu;
        /// <summary>Boîte « Menu principal » ouverte, ou <c>null</c>.</summary>
        public Form Dialog => dialog != null && !dialog.IsDisposed ? dialog : null;

        /// <summary>Entrées du menu principal, dans l'ordre du client (<c>_btnQuit</c>, <c>_btnOptions</c>, <c>_btnHelp</c>, <c>_btnBugs</c>, <c>_btnSubscribe</c>).</summary>
        public ContextMenuStrip Build()
        {
            menu?.Dispose();
            menu = new ContextMenuStrip { Renderer = new RetroMenuRenderer(), BackColor = BotUi.Paper, Font = BotFonts.Get(9), ShowItemToolTips = true,
                AccessibleName = BannerArt.Text("MENU", "Menu principal") };
            menu.Items.Add(Entry("UI_MainMenuCross", BannerArt.Text("MAIN_MENU_QUIT", "Changer de personnage / Déconnecter / Quitter"), null, () => _ = AskAsync()));
            menu.Items.Add(Entry("UI_MainMenuOptions", BannerArt.Text("MAIN_MENU_OPTIONS", "Menu d'options"), null, () => OptionsRequested?.Invoke(this, EventArgs.Empty)));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Entry("UI_MainMenuHelp", BannerArt.Text("MAIN_MENU_HELP", "Manuel du joueur"), "Lien du site officiel du client : sans objet pour un serveur privé.", null));
            menu.Items.Add(Entry("UI_MainMenuBugs", BannerArt.Text("MAIN_MENU_BUGS", "Rapporter un bug"), "Lien du site officiel du client : sans objet pour un serveur privé.", null));
            menu.Items.Add(Entry("UI_MainMenuSubscribe", "S'abonner", "Lien du site officiel du client : sans objet pour un serveur privé.", null));
            return menu;
        }

        /// <summary>Affiche le menu sous le bouton du bandeau.</summary>
        public ContextMenuStrip Show(Control anchor)
        {
            ContextMenuStrip strip = Build();
            if (anchor != null && !anchor.IsDisposed && anchor.IsHandleCreated) strip.Show(anchor, new Point(0, anchor.Height), ToolStripDropDownDirection.AboveRight);
            return strip;
        }

        /// <summary>
        /// Boîte <c>AskMainMenu</c> : « Changer de perso », « Déconnecter », « Quitter », « Annuler ». Le choix lève l'événement
        /// correspondant ; une boîte déjà ouverte est simplement remise au premier plan.
        /// </summary>
        public Task<MainMenuChoice> AskAsync()
        {
            if (Dialog != null) { dialog.Activate(); return pending.Task; }
            pending = new TaskCompletionSource<MainMenuChoice>();
            TaskCompletionSource<MainMenuChoice> completion = pending;
            dialog = new MainMenuDialog();
            dialog.FormClosed += (s, e) =>
            {
                MainMenuChoice choice = ((MainMenuDialog)s).Choice;
                dialog = null;
                completion.TrySetResult(choice);
                switch (choice)
                {
                    case MainMenuChoice.ChangeCharacter: ChangeCharacterRequested?.Invoke(this, EventArgs.Empty); break;
                    case MainMenuChoice.Logoff: LogoffRequested?.Invoke(this, EventArgs.Empty); break;
                    case MainMenuChoice.Quit: QuitRequested?.Invoke(this, EventArgs.Empty); break;
                }
                var closed = (Form)s;
                try { if (closed.IsHandleCreated) closed.BeginInvoke((Action)closed.Dispose); else closed.Dispose(); }
                catch (InvalidOperationException) { closed.Dispose(); }
            };
            Form parent = owner?.FindForm();
            dialog.PlaceOver(parent);
            if (parent != null && parent.Visible) dialog.Show(parent); else dialog.Show();
            return completion.Task;
        }

        /// <summary>Répond à la boîte ouverte comme son bouton (tests et raccourcis).</summary>
        public bool Answer(MainMenuChoice choice)
        {
            if (Dialog == null) return false;
            dialog.Answer(choice);
            return true;
        }

        private ToolStripMenuItem Entry(string icon, string text, string disabledReason, Action click)
        {
            var item = new ToolStripMenuItem(text) { Enabled = click != null, AccessibleName = text };
            if (disabledReason != null) item.ToolTipText = disabledReason;
            if (click != null) item.Click += (s, e) => click();
            // Lecture hors du thread de l'interface ; la fenêtre de jeu ramène l'image sur ce thread.
            if (owner != null) BannerArt.Request(owner, icon, image => { if (!item.IsDisposed && (owner.IsDisposed || !owner.InvokeRequired)) item.Image = image; });
            return item;
        }

        public void Dispose()
        {
            menu?.Dispose(); menu = null;
            if (Dialog != null) dialog.Close();
        }

        /// <summary>Boîte « Menu principal » dans la palette Retro (cadre brun, parchemin, filets dorés).</summary>
        private sealed class MainMenuDialog : Form
        {
            internal MainMenuChoice Choice { get; private set; } = MainMenuChoice.None;

            internal MainMenuDialog()
            {
                FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; KeyPreview = true;
                AutoScaleMode = AutoScaleMode.None; BackColor = BotUi.Frame; Padding = new Padding(3); Font = BotFonts.Get(9);
                Text = BannerArt.Text("MENU", "Menu principal"); AccessibleName = Text; Name = "main-menu";
                var frame = new Panel { Dock = DockStyle.Fill, BackColor = BotUi.Paper, Padding = new Padding(14, 8, 14, 12) };
                var title = new Label { Dock = DockStyle.Top, Height = 26, Text = Text, Font = BotFonts.Get(10, FontStyle.Bold), ForeColor = BotUi.Ink,
                    TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent };
                var rule = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = BotUi.Gold };
                var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                    Padding = new Padding(0, 10, 0, 0), BackColor = Color.Transparent };
                foreach (var entry in new[] {
                    Tuple.Create(MainMenuChoice.ChangeCharacter, BannerArt.Text("CHANGE_CHARACTER", "Changer de perso")),
                    Tuple.Create(MainMenuChoice.Logoff, BannerArt.Text("LOGOFF", "Déconnecter")),
                    Tuple.Create(MainMenuChoice.Quit, "Quitter AzurClientRetro"),
                    Tuple.Create(MainMenuChoice.Cancel, BannerArt.Text("CANCEL_SMALL", "Annuler")) })
                {
                    MainMenuChoice value = entry.Item1;
                    var button = (Button)BotUi.Button(entry.Item2, (s, e) => Answer(value), value == MainMenuChoice.Cancel, 190);
                    Font own = button.Font; button.Font = BotFonts.Get(9, value == MainMenuChoice.Cancel ? FontStyle.Bold : FontStyle.Regular); own.Dispose();
                    button.Margin = new Padding(0, 0, 0, 7); button.Tag = value; button.AccessibleName = entry.Item2;
                    buttons.Controls.Add(button);
                    if (value == MainMenuChoice.Cancel) CancelButton = button;
                }
                frame.Controls.Add(buttons); frame.Controls.Add(rule); frame.Controls.Add(title);
                Controls.Add(frame);
                ClientSize = new Size(190 + 34, 26 + 1 + 10 + 4 * 44 + 26);
            }

            internal void Answer(MainMenuChoice value) { Choice = value; Close(); }

            internal void PlaceOver(Form parent)
            {
                Rectangle area = parent != null && !parent.IsDisposed && parent.Visible && parent.WindowState != FormWindowState.Minimized
                    ? parent.Bounds : Screen.PrimaryScreen.WorkingArea;
                Location = new Point(area.Left + Math.Max(0, (area.Width - Width) / 2), area.Top + Math.Max(0, (area.Height - Height) / 2));
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { Answer(MainMenuChoice.Cancel); e.Handled = true; return; }
                base.OnKeyDown(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                using (var gold = new Pen(BotUi.Gold)) e.Graphics.DrawRectangle(gold, 0, 0, Width - 1, Height - 1);
            }
        }
    }
}
