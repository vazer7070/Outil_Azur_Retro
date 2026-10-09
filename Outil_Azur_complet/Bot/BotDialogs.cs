using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Outil_Azur_complet.Bot
{
    /// <summary>Réponse d'une boîte de dialogue du bot ; <see cref="None"/> quand elle est fermée sans réponse.</summary>
    public enum BotDialogResult { None, Yes, No, Ignore, Ok, Cancel }

    /// <summary>
    /// Boîtes de dialogue au style du client Retro (cadre brun, parchemin, filets dorés, boutons du client), à la place
    /// de <c>MessageBox</c> : question oui/non, oui/non/ignorer (invitations) et simple information. Les variantes
    /// asynchrones ne bloquent pas la fenêtre de jeu (les paquets continuent d'arriver) ; Échap répond « Non »
    /// (ou « OK » pour une information). Un appel hors du thread de l'interface y est ramené par le contrôle propriétaire,
    /// obligatoire dans ce cas.
    /// </summary>
    public static class BotDialogs
    {
        private static readonly List<Form> open = new List<Form>();

        /// <summary>Boîtes actuellement affichées (diagnostic et tests).</summary>
        public static IReadOnlyList<Form> OpenDialogs { get { lock (open) return open.ToArray(); } }

        public static Task<BotDialogResult> AskYesNoAsync(Control owner, string title, string message) =>
            ShowAsync(owner, title, message, BotDialogResult.No, BotDialogResult.Yes, BotDialogResult.No);
        public static Task<BotDialogResult> AskYesNoIgnoreAsync(Control owner, string title, string message) =>
            ShowAsync(owner, title, message, BotDialogResult.No, BotDialogResult.Yes, BotDialogResult.No, BotDialogResult.Ignore);
        public static Task<BotDialogResult> InfoAsync(Control owner, string title, string message) =>
            ShowAsync(owner, title, message, BotDialogResult.Ok, BotDialogResult.Ok);

        // Duels (lot M4) : boîtes nommées, refermées par Dismiss quand le serveur clôt le duel (GA;901, GA;902).
        /// <summary>Oui / Non / Ignorer d'un duel reçu (<c>CAUTION_YESNOIGNORE</c> du client), retrouvable par son nom.</summary>
        public static Task<BotDialogResult> AskYesNoIgnoreAsync(Control owner, string title, string message, string name) =>
            ShowAsync(owner, title, message, name, BotDialogResult.No, BotDialogResult.Yes, BotDialogResult.No, BotDialogResult.Ignore);
        /// <summary>Information avec un seul bouton « Annuler » (<c>INFO_CANCEL</c> du client, duel proposé en attente).</summary>
        public static Task<BotDialogResult> CancelAsync(Control owner, string title, string message, string name) =>
            ShowAsync(owner, title, message, name, BotDialogResult.Cancel, BotDialogResult.Cancel);

        /// <summary>Referme sans réponse (<see cref="BotDialogResult.None"/>) les boîtes portant ce nom ; renvoie leur nombre.</summary>
        public static int Dismiss(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            int count = 0;
            foreach (Form dialog in OpenDialogs.Where(form => string.Equals(form.Name, name, StringComparison.Ordinal)))
            {
                count++;
                Action close = () => { if (!dialog.IsDisposed && !dialog.Modal) dialog.Close(); };
                try { if (dialog.InvokeRequired) dialog.BeginInvoke(close); else close(); }
                catch (InvalidOperationException) { }
            }
            return count;
        }

        /// <summary>Variante modale : bloque jusqu'à la réponse (à réserver aux écrans sans réseau actif).</summary>
        public static BotDialogResult AskYesNo(IWin32Window owner, string title, string message) =>
            ShowModal(owner, title, message, BotDialogResult.No, BotDialogResult.Yes, BotDialogResult.No);
        public static BotDialogResult AskYesNoIgnore(IWin32Window owner, string title, string message) =>
            ShowModal(owner, title, message, BotDialogResult.No, BotDialogResult.Yes, BotDialogResult.No, BotDialogResult.Ignore);
        public static void Info(IWin32Window owner, string title, string message) =>
            ShowModal(owner, title, message, BotDialogResult.Ok, BotDialogResult.Ok);

        private static Task<BotDialogResult> ShowAsync(Control owner, string title, string message, BotDialogResult cancel, params BotDialogResult[] choices) =>
            ShowAsync(owner, title, message, null, cancel, choices);

        private static Task<BotDialogResult> ShowAsync(Control owner, string title, string message, string name, BotDialogResult cancel, params BotDialogResult[] choices)
        {
            var completion = new TaskCompletionSource<BotDialogResult>();
            Action show = () =>
            {
                try
                {
                    Form parent = owner == null || owner.IsDisposed ? null : owner.FindForm();
                    var dialog = new BotDialogForm(title, message, cancel, choices) { Name = name ?? string.Empty };
                    dialog.FormClosed += (s, e) => { Forget(dialog); completion.TrySetResult(dialog.Result); dialog.BeginDispose(); };
                    Remember(dialog);
                    dialog.PlaceOver(parent);
                    if (parent != null && parent.Visible) dialog.Show(parent); else dialog.Show();
                }
                catch (Exception error) { completion.TrySetException(error); }
            };
            if (owner != null && !owner.IsDisposed && owner.InvokeRequired)
            {
                try { owner.BeginInvoke(show); }
                catch (InvalidOperationException) { completion.TrySetResult(BotDialogResult.None); }
            }
            else show();
            return completion.Task;
        }

        private static BotDialogResult ShowModal(IWin32Window owner, string title, string message, BotDialogResult cancel, params BotDialogResult[] choices)
        {
            using (var dialog = new BotDialogForm(title, message, cancel, choices))
            {
                Remember(dialog);
                try { dialog.PlaceOver(owner as Form ?? (owner as Control)?.FindForm()); dialog.ShowDialog(owner); }
                finally { Forget(dialog); }
                return dialog.Result;
            }
        }

        private static void Remember(Form dialog) { lock (open) open.Add(dialog); }
        private static void Forget(Form dialog) { lock (open) open.Remove(dialog); }

        internal static string Text(BotDialogResult result)
        {
            switch (result)
            {
                case BotDialogResult.Yes: return "Oui";
                case BotDialogResult.No: return "Non";
                case BotDialogResult.Ignore: return "Ignorer";
                case BotDialogResult.Cancel: return "Annuler";
                default: return "OK";
            }
        }
    }

    /// <summary>Fenêtre sans bordure système des boîtes du bot ; le bandeau de titre sert à la déplacer.</summary>
    internal sealed class BotDialogForm : Form
    {
        private const int ContentWidth = 360;
        private readonly BotDialogResult cancel;
        private Point dragStart;
        private bool dragging;

        internal BotDialogForm(string title, string message, BotDialogResult cancelResult, BotDialogResult[] choices)
        {
            cancel = cancelResult;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None; KeyPreview = true; DoubleBuffered = true;
            BackColor = BotUi.Frame; ForeColor = BotUi.Ink; Font = BotFonts.Get(9); Padding = new Padding(3);
            Text = string.IsNullOrEmpty(title) ? "AzurClientRetro" : title; AccessibleName = Text;

            var frame = new ClientPanel { Dock = DockStyle.Fill, BackColor = BotUi.Paper, Padding = new Padding(12, 8, 12, 10) };
            var heading = new Label { Dock = DockStyle.Top, Height = 26, Text = Text, Font = BotFonts.Get(10, FontStyle.Bold),
                ForeColor = BotUi.Ink, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Name = "dialog-title" };
            var rule = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = BotUi.Gold };
            int textHeight = TextRenderer.MeasureText(message ?? string.Empty, Font, new Size(ContentWidth, 0),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
            textHeight = Math.Max(36, Math.Min(260, textHeight + 8));
            var body = new Label { Dock = DockStyle.Fill, Text = message ?? string.Empty, ForeColor = BotUi.Ink, BackColor = Color.Transparent,
                Padding = new Padding(0, 8, 0, 4), UseMnemonic = false, Name = "dialog-message" };
            var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false, Padding = new Padding(0, 6, 0, 0), BackColor = Color.Transparent, Name = "dialog-actions" };
            foreach (BotDialogResult choice in choices.Reverse())
            {
                BotDialogResult value = choice;
                var button = (Button)BotUi.Button(BotDialogs.Text(value), (s, e) => Answer(value), value == choices[0], 100);
                Font own = button.Font; button.Font = BotFonts.Get(9, value == choices[0] ? FontStyle.Bold : FontStyle.Regular); own.Dispose();
                button.Margin = new Padding(7, 0, 0, 0); button.Tag = value; button.AccessibleName = button.Text;
                actions.Controls.Add(button);
                if (value == choices[0]) AcceptButton = button;
                if (value == cancelResult) CancelButton = button;
            }
            frame.Controls.Add(body); frame.Controls.Add(actions); frame.Controls.Add(rule); frame.Controls.Add(heading);
            Controls.Add(frame);
            ClientSize = new Size(ContentWidth + 30, 26 + 1 + textHeight + 12 + 42 + 24);
            heading.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { dragging = true; dragStart = e.Location; } };
            heading.MouseMove += (s, e) => { if (dragging) Location = new Point(Left + e.X - dragStart.X, Top + e.Y - dragStart.Y); };
            heading.MouseUp += (s, e) => dragging = false;
        }

        internal BotDialogResult Result { get; private set; } = BotDialogResult.None;

        /// <summary>Répond comme le bouton correspondant et ferme la boîte (tests et raccourcis).</summary>
        internal void Answer(BotDialogResult value)
        {
            Result = value;
            if (Modal) DialogResult = DialogResult.OK; else Close();
        }

        internal void PlaceOver(Form parent)
        {
            Rectangle area = parent != null && !parent.IsDisposed && parent.Visible && parent.WindowState != FormWindowState.Minimized
                ? parent.Bounds : Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Left + Math.Max(0, (area.Width - Width) / 2), area.Top + Math.Max(0, (area.Height - Height) / 2));
        }

        /// <summary>Libère la boîte non modale après la fin du message de fermeture.</summary>
        internal void BeginDispose()
        {
            if (IsDisposed) return;
            try { if (IsHandleCreated) { BeginInvoke((Action)Dispose); return; } }
            catch (InvalidOperationException) { }
            Dispose();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { Answer(cancel); e.Handled = true; return; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var gold = new Pen(BotUi.Gold)) e.Graphics.DrawRectangle(gold, 0, 0, Width - 1, Height - 1);
        }
    }
}
