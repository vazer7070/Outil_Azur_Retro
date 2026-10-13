using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Controls.Chat;

namespace Outil_Azur_complet.Bot.Controls
{
    /// <summary>
    /// Boîte de quantité du client (<c>PopupQuantity</c>) au style Retro, lot X4 : champ prérempli qui n'accepte que des
    /// chiffres (<c>restrict = "0-9"</c>), boutons « min » et « max » (<c>MIN_WORD</c>, <c>MAX_WORD</c> ; « max » valide quand le
    /// champ vaut déjà le maximum) et « OK ». Entrée valide (<c>ACCEPT_CURRENT_DIALOG</c>), un clic hors de la boîte l'abandonne
    /// (<c>_bgHidder</c>), Échap aussi. Elle s'ouvre au pointeur, coin inférieur droit sous la souris, ramenée dans l'écran
    /// (<c>placeWindow</c>). Non modale : la fenêtre de jeu continue de recevoir les paquets. La valeur rendue est le nombre
    /// lu (<c>parseInt</c>), <c>null</c> pour un champ vide ou une boîte abandonnée ; l'appelant la borne comme le client
    /// (<c>value &gt; 0</c>, puis <c>Math.min(value, quantité)</c>).
    /// </summary>
    public sealed class QuantityPopup : Form
    {
        private static readonly List<QuantityPopup> open = new List<QuantityPopup>();
        private readonly TextBox input;
        private readonly TaskCompletionSource<int?> completion = new TaskCompletionSource<int?>();
        private bool activated, answered;

        /// <summary>Boîtes affichées (tests et diagnostic).</summary>
        public static IReadOnlyList<QuantityPopup> OpenPopups { get { lock (open) return open.ToArray(); } }

        /// <summary>Borne du bouton « min » (le client ne la fixe pas pour l'inventaire : 1, plus petite quantité acceptée).</summary>
        public int Minimum { get; }
        /// <summary>Borne du bouton « max » (quantité de la pile glissée).</summary>
        public int Maximum { get; }
        /// <summary>Texte du champ, tel que le joueur le voit.</summary>
        public string InputText { get => input.Text; set => input.Text = Digits(value); }
        /// <summary>Réponse : nombre lu à la validation, <c>null</c> si la boîte est abandonnée.</summary>
        public Task<int?> Result => completion.Task;

        private QuantityPopup(int value, int minimum, int maximum)
        {
            Minimum = minimum; Maximum = maximum;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None; KeyPreview = true; DoubleBuffered = true;
            BackColor = BotUi.Frame; ForeColor = BotUi.Ink; Font = BotFonts.Get(9); Padding = new Padding(2);
            Text = "Quantité"; AccessibleName = Text; Name = "quantity-popup";

            var frame = new ClientPanel { Dock = DockStyle.Fill, BackColor = BotUi.Paper, Padding = new Padding(8, 7, 8, 7) };
            input = new TextBox { Dock = DockStyle.Top, Height = 24, Font = BotFonts.Get(10), BorderStyle = BorderStyle.FixedSingle,
                BackColor = BotUi.PaperLight, ForeColor = BotUi.Ink, MaxLength = 9, TextAlign = HorizontalAlignment.Right, Name = "quantity-input",
                Text = Math.Max(0, value).ToString(CultureInfo.InvariantCulture) };
            input.KeyPress += (s, e) => { if (!char.IsControl(e.KeyChar) && (e.KeyChar < '0' || e.KeyChar > '9')) e.Handled = true; };
            input.TextChanged += (s, e) => { string digits = Digits(input.Text); if (digits != input.Text) { input.Text = digits; input.SelectionStart = digits.Length; } };
            var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 32, FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
                Padding = new Padding(0, 5, 0, 0), BackColor = Color.Transparent, Name = "quantity-actions" };
            actions.Controls.Add(Small(ChatUiText.Get("MIN_WORD", "min"), (s, e) => PressMin(), false, "quantity-min"));
            actions.Controls.Add(Small(ChatUiText.Get("MAX_WORD", "max"), (s, e) => PressMax(), false, "quantity-max"));
            var ok = Small("OK", (s, e) => Confirm(), true, "quantity-ok");
            actions.Controls.Add(ok);
            AcceptButton = (IButtonControl)ok;
            frame.Controls.Add(input); frame.Controls.Add(actions);
            Controls.Add(frame);
            ClientSize = new Size(184, 74);
            Activated += (s, e) => { activated = true; input.Focus(); input.SelectAll(); };
            // Clic hors de la boîte (_bgHidder du client) : abandon, seulement après une première activation, car certains
            // gestionnaires de fenêtres sous Mono désactivent une fenêtre qui n'a jamais été active.
            Deactivate += (s, e) => { if (activated && !answered) Cancel(); };
            FormClosed += (s, e) => { lock (open) open.Remove(this); completion.TrySetResult(null); };
        }

        private static Control Small(string text, EventHandler action, bool primary, string name)
        {
            var button = (Button)BotUi.Button(text, action, primary, 52);
            Font own = button.Font; button.Font = BotFonts.Get(8, primary ? FontStyle.Bold : FontStyle.Regular); own.Dispose();
            button.Height = 24; button.Margin = new Padding(0, 0, 6, 0); button.Name = name; button.AccessibleName = text;
            return button;
        }

        /// <summary>
        /// Ouvre la boîte sur le fil de l'interface de <paramref name="owner"/> avec <paramref name="value"/> dans le champ ;
        /// <paramref name="screenPoint"/> (par défaut la souris) donne le coin inférieur droit. Rend le nombre validé ou <c>null</c>.
        /// </summary>
        public static Task<int?> AskAsync(Control owner, int value, int maximum, int minimum = 1, Point? screenPoint = null)
        {
            var relay = new TaskCompletionSource<int?>();
            Action show = () =>
            {
                try
                {
                    Form parent = owner == null || owner.IsDisposed ? null : owner.FindForm();
                    var popup = new QuantityPopup(value, Math.Max(0, minimum), Math.Max(Math.Max(0, minimum), maximum));
                    lock (open) open.Add(popup);
                    popup.Result.ContinueWith(task => relay.TrySetResult(task.Result), TaskScheduler.Default);
                    popup.FormClosed += (s, e) => popup.BeginDispose();
                    popup.Place(screenPoint ?? Cursor.Position, parent);
                    if (parent != null && parent.Visible) popup.Show(parent); else popup.Show();
                    popup.input.Focus(); popup.input.SelectAll();
                }
                catch (Exception error) when (error is InvalidOperationException || error is ArgumentException || error is ObjectDisposedException)
                {
                    relay.TrySetResult(null);
                }
            };
            if (owner != null && !owner.IsDisposed && owner.InvokeRequired)
            {
                try { owner.BeginInvoke(show); }
                catch (InvalidOperationException) { relay.TrySetResult(null); }
            }
            else show();
            return relay.Task;
        }

        /// <summary>« min » : le champ prend la borne basse.</summary>
        public void PressMin() { input.Text = Minimum.ToString(CultureInfo.InvariantCulture); input.Focus(); }

        /// <summary>« max » : le champ prend la borne haute, ou valide s'il la contient déjà (comme le client).</summary>
        public void PressMax()
        {
            string top = Maximum.ToString(CultureInfo.InvariantCulture);
            if (input.Text == top) { Confirm(); return; }
            input.Text = top; input.Focus();
        }

        /// <summary>« OK » ou Entrée : rend le nombre lu (<c>parseInt</c> en base 10), <c>null</c> pour un champ vide, puis ferme.</summary>
        public void Confirm()
        {
            if (answered) return;
            answered = true;
            completion.TrySetResult(int.TryParse(input.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : (int?)null);
            Close();
        }

        /// <summary>Clic hors de la boîte ou Échap : abandon sans réponse.</summary>
        public void Cancel()
        {
            if (answered) return;
            answered = true;
            completion.TrySetResult(null);
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { Cancel(); e.Handled = true; return; }
            if (e.KeyCode == Keys.Enter) { Confirm(); e.Handled = true; return; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var gold = new Pen(BotUi.Gold)) e.Graphics.DrawRectangle(gold, 0, 0, Width - 1, Height - 1);
        }

        /// <summary><c>placeWindow</c> : coin inférieur droit au point donné, puis ramené dans la zone visible.</summary>
        private void Place(Point corner, Form parent)
        {
            Rectangle area = parent != null && !parent.IsDisposed && parent.Visible && parent.WindowState != FormWindowState.Minimized
                ? Screen.FromControl(parent).WorkingArea : Screen.PrimaryScreen.WorkingArea;
            int x = Math.Max(area.Left, Math.Min(corner.X - Width, area.Right - Width));
            int y = Math.Max(area.Top, Math.Min(corner.Y - Height, area.Bottom - Height));
            Location = new Point(x, y);
        }

        private void BeginDispose()
        {
            if (IsDisposed) return;
            try { if (IsHandleCreated) { BeginInvoke((Action)Dispose); return; } }
            catch (InvalidOperationException) { }
            Dispose();
        }

        private static string Digits(string text) => new string((text ?? string.Empty).Where(c => c >= '0' && c <= '9').ToArray());
    }
}
