using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Interactions;

namespace Outil_Azur_complet.Bot
{
    /// <summary>
    /// Choix du pseudo d'un compte neuf (lot X3, manque B-05), comme l'interface <c>ChooseNickName</c> du client 1.34 : titre
    /// <c>CHOOSE_NICKNAME</c>, aide <c>CHOOSE_NICKNAME_HELP</c>, saisie, « OK » et « Annuler ». Le premier « OK » contrôle le pseudo
    /// (plus de deux caractères, différent du nom du compte : <c>NICKNAME_EQUALS_LOGIN</c>) puis demande la confirmation
    /// (<c>DO_CHOOSE_NICKNAME</c>) ; le second l'envoie. « Annuler » revient à la saisie depuis la confirmation, sinon déconnecte
    /// (retour à l'écran de connexion du client). Un refus du serveur (<c>AlEs</c>) rouvre la saisie avec <c>NICKNAME_ALREADY_USED</c>.
    /// Ouverte et refermée par <see cref="PersoSelection"/> selon <see cref="NicknameChoice"/>. En jeu (potion de changement de nom,
    /// lot X32), la même fenêtre sert au nom du personnage au-dessus de la fenêtre de jeu (<see cref="ConnectionPrompts"/>, sur
    /// <see cref="Tool_BotProtocol.Game.Session.NameChange"/>) : « Annuler » la ferme alors sans déconnecter.
    /// </summary>
    public sealed class ChooseNickNameForm : Form
    {
        internal enum NicknameStep { Input, Confirm, Waiting }

        private const int ContentWidth = 380;
        private static readonly Color ErrorInk = Color.FromArgb(158, 38, 24);
        private readonly NicknamePrompt choice;
        private readonly Label help, error;
        private readonly TextBox input;
        private readonly Button ok, cancel;
        private bool settled, dragging;
        private Point dragStart;

        public ChooseNickNameForm(NicknamePrompt choice)
        {
            this.choice = choice ?? throw new ArgumentNullException(nameof(choice));
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None; KeyPreview = true; DoubleBuffered = true;
            BackColor = BotUi.Frame; ForeColor = BotUi.Ink; Font = BotFonts.Get(9); Padding = new Padding(3);
            Text = ConnectionTexts.NicknameTitle; AccessibleName = Text; Name = "choix-du-pseudo";

            var frame = new ClientPanel { Dock = DockStyle.Fill, BackColor = BotUi.Paper, Padding = new Padding(12, 8, 12, 10) };
            var heading = new Label { Dock = DockStyle.Top, Height = 26, Text = Text, Font = BotFonts.Get(10, FontStyle.Bold),
                ForeColor = BotUi.Ink, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Name = "nickname-title" };
            var rule = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = BotUi.Gold };
            help = new Label { Dock = DockStyle.Top, Height = 92, ForeColor = BotUi.Ink, BackColor = Color.Transparent,
                Padding = new Padding(0, 8, 0, 4), UseMnemonic = false, Name = "nickname-help" };
            input = new TextBox { Dock = DockStyle.Top, Font = BotFonts.Get(10), BorderStyle = BorderStyle.FixedSingle,
                BackColor = BotUi.PaperLight, ForeColor = BotUi.Ink, MaxLength = 64, Name = "nickname-input", AccessibleName = Text };
            error = new Label { Dock = DockStyle.Top, Height = 34, ForeColor = ErrorInk, BackColor = Color.Transparent,
                Padding = new Padding(0, 4, 0, 0), UseMnemonic = false, Visible = false, Name = "nickname-error" };
            var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false, Padding = new Padding(0, 6, 0, 0), BackColor = Color.Transparent, Name = "nickname-actions" };
            cancel = (Button)BotUi.Button(ConnectionTexts.Cancel, (s, e) => CancelStep(), false, 100);
            ok = (Button)BotUi.Button(ConnectionTexts.Ok, async (s, e) => await OkAsync(), true, 100);
            cancel.Name = "nickname-cancel"; ok.Name = "nickname-ok";
            foreach (Button button in new[] { cancel, ok }) { button.Margin = new Padding(7, 0, 0, 0); button.AccessibleName = button.Text; }
            actions.Controls.Add(cancel); actions.Controls.Add(ok);
            // Entrée et Échap sont lus ici : AcceptButton enverrait le pseudo deux fois si l'utilisateur répète la touche.
            frame.Controls.Add(error); frame.Controls.Add(input); frame.Controls.Add(help);
            frame.Controls.Add(actions); frame.Controls.Add(rule); frame.Controls.Add(heading);
            Controls.Add(frame);
            ClientSize = new Size(ContentWidth + 30, 26 + 1 + 92 + 26 + 34 + 42 + 30);
            heading.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { dragging = true; dragStart = e.Location; } };
            heading.MouseMove += (s, e) => { if (dragging) Location = new Point(Left + e.X - dragStart.X, Top + e.Y - dragStart.Y); };
            heading.MouseUp += (s, e) => dragging = false;
            SetStep(NicknameStep.Input);
        }

        internal NicknameStep Step { get; private set; }
        internal string HelpText => help.Text;
        /// <summary>Texte d'erreur affiché sous la saisie, ou <c>null</c>.</summary>
        internal string ErrorText => error.Visible ? error.Text : null;
        internal string Nickname { get => input.Text; set => input.Text = value ?? string.Empty; }

        /// <summary>Demande du serveur (première, ou refus du pseudo envoyé) : retour à la saisie, avec la raison du refus.</summary>
        internal void ShowRequest(NicknameRequest request)
        {
            SetStep(NicknameStep.Input);
            ShowError(request?.Error);
            if (request?.Refused != null) input.Text = request.Refused;
            input.SelectAll();
        }

        /// <summary>« OK » : contrôle puis confirmation, ou envoi depuis la confirmation.</summary>
        internal async Task OkAsync()
        {
            switch (Step)
            {
                case NicknameStep.Input:
                    string refusal = choice.Validate(input.Text);
                    if (refusal != null) { ShowError(refusal); input.Focus(); return; }
                    SetStep(NicknameStep.Confirm);
                    return;
                case NicknameStep.Confirm:
                    SetStep(NicknameStep.Waiting);
                    InteractionResult result;
                    try { result = await choice.ChooseAsync(input.Text); }
                    catch (Exception failure) { result = null; ShowFailure(failure.Message); }
                    if (IsDisposed || result == null) return;
                    if (!result.Sent) ShowFailure(result.Message);
                    // La réponse du Login (AlEs, AlEr) a pu revenir avant cette suite et remettre la saisie : ne pas l'écraser.
                    else if (Step == NicknameStep.Waiting) help.Text = result.Message;
                    return;
            }
        }

        /// <summary>« Annuler » : retour à la saisie depuis la confirmation ; sinon déconnexion comme le client (en jeu : simple fermeture).</summary>
        internal void CancelStep()
        {
            if (Step == NicknameStep.Confirm) { SetStep(NicknameStep.Input); input.SelectAll(); return; }
            settled = true;
            Close();
            choice.Cancel();
        }

        /// <summary>Pseudo accepté ou connexion fermée : la fenêtre se referme sans rien envoyer.</summary>
        internal void CloseSettled()
        {
            settled = true;
            if (!IsDisposed) Close();
        }

        internal void PlaceOver(Form parent)
        {
            Rectangle area = parent != null && !parent.IsDisposed && parent.Visible && parent.WindowState != FormWindowState.Minimized
                ? parent.Bounds : Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Left + Math.Max(0, (area.Width - Width) / 2), area.Top + Math.Max(0, (area.Height - Height) / 2));
        }

        private void SetStep(NicknameStep step)
        {
            Step = step;
            bool typing = step == NicknameStep.Input;
            input.Visible = typing;
            input.ReadOnly = !typing;
            if (!typing) error.Visible = false;
            help.Text = step == NicknameStep.Input ? ConnectionTexts.NicknameHelp
                : step == NicknameStep.Confirm ? ConnectionTexts.NicknameConfirm(input.Text)
                : "Pseudo « " + input.Text + " » : envoi au serveur…";
            ok.Enabled = step != NicknameStep.Waiting;
            if (typing && Visible) input.Focus();
        }

        private void ShowFailure(string message)
        {
            if (IsDisposed) return;
            SetStep(NicknameStep.Input);
            ShowError(message);
        }

        private void ShowError(string message)
        {
            error.Text = message ?? string.Empty;
            error.Visible = !string.IsNullOrEmpty(message);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (Step == NicknameStep.Input) input.Focus();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { e.Handled = true; e.SuppressKeyPress = true; CancelStep(); return; }
            if (e.KeyCode == Keys.Enter) { e.Handled = true; e.SuppressKeyPress = true; _ = OkAsync(); return; }
            base.OnKeyDown(e);
        }

        /// <summary>Fermeture par le système (Alt+F4) pendant la demande : vaut « Annuler » depuis la saisie.</summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.Cancel || settled || e.CloseReason != CloseReason.UserClosing) return;
            settled = true;
            choice.Cancel();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var gold = new Pen(BotUi.Gold)) e.Graphics.DrawRectangle(gold, 0, 0, Width - 1, Height - 1);
        }
    }
}
