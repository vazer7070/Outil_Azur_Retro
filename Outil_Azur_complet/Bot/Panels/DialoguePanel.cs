using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Interactions;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>
    /// Dialogue avec un PNJ, composé comme <c>UI_NpcDialog</c> du client (762 × 432) ramené à la largeur du tiroir :
    /// nom du PNJ dans la barre de titre brune, illustration (<c>artworks/big</c>, voir <see cref="NpcPortraits"/>),
    /// question dans une bulle de parchemin, réponses en lignes à puce comme <c>QuestionViewerAnswerItem</c>.
    /// S'ouvre sur <c>DCK</c>/<c>DQ</c>, se ferme sur <c>DV</c> ; une réponse envoie <c>DR&lt;question&gt;|&lt;réponse&gt;</c>,
    /// Quitter (ou ×/Échap) et le choix « fin du dialogue » d'une question sans réponse envoient <c>DV</c>.
    /// </summary>
    public sealed class DialoguePanel : GamePanel
    {
        private const string NoDialogText = "Cliquez sur un personnage non joueur de la carte pour ouvrir le menu de ses actions, puis « Parler » (Maj + clic : parler directement).";
        /// <summary>Identifiant du choix « fin du dialogue » (le client lui donne -1 et envoie <c>DV</c>).</summary>
        public const int EndChoiceId = -1;
        private Label dialogTitle, dialogQuestion, dialogStatus;
        private FlowLayoutPanel dialogContent, dialogAnswers;
        private PortraitView dialogPortrait;
        private SpeechBubble dialogBubble;
        private Control dialogLeave;
        private string dialogAnswersKey = string.Empty;
        private bool wasOpen, layingOut;
        private int portraitRequested;
        private Bitmap portraitImage;

        public override string Title => "Dialogue";
        public override bool IsModal => true;
        public override bool IsServerWindowOpen => Game?.Interactions?.Npc?.IsOpen == true;
        /// <summary>Illustration affichée (diagnostic et tests) ; <c>null</c> quand le PNG manque.</summary>
        public Image Portrait => portraitImage;

        protected override Control CreateView()
        {
            var page = Page();
            var band = new Panel { Dock = DockStyle.Top, Height = 28, BackColor = BotUi.Frame, Padding = new Padding(8, 0, 8, 0) };
            dialogTitle = MakeLabel("Aucun dialogue en cours", 10, true);
            dialogTitle.Dock = DockStyle.Fill; dialogTitle.AutoEllipsis = true; dialogTitle.ForeColor = BotUi.Paper;
            dialogTitle.TextAlign = ContentAlignment.MiddleLeft; dialogTitle.UseMnemonic = false;
            band.Controls.Add(dialogTitle);

            dialogPortrait = new PortraitView { Margin = new Padding(0, 6, 0, 0), Visible = false, AccessibleName = "Illustration du PNJ" };
            dialogBubble = new SpeechBubble { Margin = new Padding(0, 6, 0, 0) };
            dialogQuestion = MakeLabel(NoDialogText, 9);
            dialogQuestion.Dock = DockStyle.Fill; dialogQuestion.ForeColor = BotUi.Muted; dialogQuestion.BackColor = BotUi.PaperLight;
            dialogQuestion.UseMnemonic = false; dialogQuestion.UseCompatibleTextRendering = true;
            dialogBubble.Controls.Add(dialogQuestion);
            dialogAnswers = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = false,
                BackColor = BotUi.Paper, Margin = new Padding(0, 8, 0, 0), Padding = new Padding(0) };
            dialogContent = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                AutoScroll = true, BackColor = BotUi.Paper, Padding = new Padding(0, 0, 0, 4) };
            dialogContent.Controls.Add(dialogPortrait); dialogContent.Controls.Add(dialogBubble); dialogContent.Controls.Add(dialogAnswers);
            dialogContent.Resize += (s, e) => LayoutContent();

            dialogStatus = MakeStatus(string.Empty);
            dialogLeave = MakeButton("Quitter", async (s, e) => await LeaveDialog(), false, 110);
            page.Controls.Add(dialogContent); page.Controls.Add(band);
            page.Controls.Add(dialogStatus); page.Controls.Add(BotUi.Actions(dialogLeave));
            return page;
        }

        protected override void OnBind(GameClass game) { game.Interactions.Npc.Changed += OnServerChanged; }
        protected override void OnUnbind(GameClass game) { if (game.Interactions != null) game.Interactions.Npc.Changed -= OnServerChanged; }

        private void OnServerChanged() => OnUi(() =>
        {
            bool open = IsServerWindowOpen;
            if (open && !wasOpen) RequestShow();
            else if (!open && wasOpen) RaiseClosed();
            wasOpen = open;
            RefreshView();
        });

        public override void RefreshView()
        {
            if (Game == null || dialogTitle == null) return;
            NpcDialog npc = Game.Interactions.Npc;
            dialogTitle.Text = npc.IsOpen ? npc.NpcName : "Aucun dialogue en cours";
            dialogQuestion.Text = QuestionView(npc);
            dialogQuestion.ForeColor = npc.IsOpen && npc.QuestionId >= 0 ? BotUi.Ink : BotUi.Muted;
            dialogBubble.ShowTail = npc.IsOpen;
            RequestPortrait(npc.IsOpen ? npc.PortraitId : 0);

            string key = npc.IsOpen && npc.QuestionId >= 0 ? npc.QuestionId + ":" + string.Join(",", npc.AnswerIds) : string.Empty;
            if (key != dialogAnswersKey)
            {
                dialogAnswersKey = key;
                dialogAnswers.SuspendLayout();
                foreach (Control old in dialogAnswers.Controls.Cast<Control>().ToArray()) { dialogAnswers.Controls.Remove(old); old.Dispose(); }
                if (key.Length > 0)
                {
                    foreach (DialogAnswer answer in npc.Answers)
                    {
                        int id = answer.Id;
                        dialogAnswers.Controls.Add(MakeAnswer(answer.Text, id, async (s, e) => await AnswerDialog(id)));
                    }
                    // Question sans réponse : le client propose un seul choix (CONTINUE_TO_SPEAK, réponse -1) qui envoie DV.
                    if (npc.Answers.Count == 0) dialogAnswers.Controls.Add(MakeAnswer(NpcDialog.EndChoiceText, EndChoiceId, async (s, e) => await LeaveDialog()));
                }
                dialogAnswers.ResumeLayout();
            }
            dialogStatus.Text = npc.LastMessage;
            bool connected = Connected;
            dialogLeave.Enabled = connected && npc.IsOpen;
            foreach (Control answer in dialogAnswers.Controls) answer.Enabled = connected && npc.IsOpen && !npc.IsPaused;
            LayoutContent();
        }

        private static string QuestionView(NpcDialog npc)
        {
            if (!npc.IsOpen) return NoDialogText;
            if (npc.QuestionId < 0) return "En attente de la question du serveur…";
            string text = npc.QuestionText;
            if (!npc.HasQuestionText)
            {
                if (npc.Parameters.Length > 0) text += " · paramètres : " + string.Join(", ", npc.Parameters);
                text += LangData.IsLoaded("dialog") ? "\nCette question est absente des textes du client." : "\nTextes du client absents (dialog.xml) : seuls les numéros sont affichés.";
            }
            if (npc.IsPaused) text += "\n\nLe serveur demande d'attendre.";
            return text;
        }

        private static AnswerButton MakeAnswer(string text, int id, EventHandler click)
        {
            var button = new AnswerButton { Text = text, Tag = id, Font = BotFonts.Get(9), Margin = new Padding(0, 0, 0, 4),
                AccessibleName = "Réponse : " + text };
            button.Click += click;
            return button;
        }

        /// <summary>Largeurs et hauteurs des blocs selon la largeur du tiroir (textes renvoyés à la ligne, défilement vertical).</summary>
        private void LayoutContent()
        {
            if (dialogContent == null || layingOut || dialogContent.IsDisposed) return;
            layingOut = true;
            try
            {
                int width = Math.Max(120, dialogContent.ClientSize.Width - dialogContent.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 2);
                dialogContent.SuspendLayout();
                dialogPortrait.Size = new Size(width, Math.Max(110, Math.Min(190, width * 9 / 16)));
                int textWidth = width - dialogBubble.Padding.Horizontal - 2;
                dialogBubble.Size = new Size(width, dialogBubble.Padding.Vertical + Math.Max(18, DialogTextMeasure.Height(dialogQuestion.Text, dialogQuestion.Font, textWidth)) + 4);
                int answersHeight = 0;
                foreach (Control control in dialogAnswers.Controls)
                {
                    if (control is AnswerButton answer) answer.Size = new Size(width, answer.PreferredHeightFor(width));
                    answersHeight += control.Height + control.Margin.Vertical;
                }
                dialogAnswers.Size = new Size(width, Math.Max(0, answersHeight));
                dialogContent.ResumeLayout(true);
            }
            finally { layingOut = false; }
        }

        /// <summary>Charge l'illustration hors du thread de l'interface ; une réponse périmée (autre PNJ, volet libéré) est libérée.</summary>
        private void RequestPortrait(int artworkId)
        {
            if (artworkId == portraitRequested) return;
            portraitRequested = artworkId;
            SetPortrait(null);
            if (artworkId <= 0) return;
            NpcPortraits.LoadAsync(artworkId).ContinueWith(task =>
            {
                Bitmap loaded = task.Status == TaskStatus.RanToCompletion ? task.Result : null;
                if (loaded != null) PostPortrait(loaded, artworkId);
            }, TaskScheduler.Default);
        }

        /// <summary>Remet l'image sur le thread de l'interface ; si plus personne ne peut la recevoir, elle est libérée aussitôt.</summary>
        private void PostPortrait(Bitmap loaded, int artworkId)
        {
            Control target = Host != null && !Host.IsDisposed ? (Control)Host : dialogContent;
            if (IsDisposed || target == null || target.IsDisposed || !target.IsHandleCreated)
            {
                loaded.Dispose();
                return;
            }
            Action apply = () =>
            {
                if (IsDisposed || portraitRequested != artworkId) loaded.Dispose();
                else SetPortrait(loaded);
            };
            try { target.BeginInvoke(apply); }
            catch (InvalidOperationException) { loaded.Dispose(); }
        }

        private void SetPortrait(Bitmap image)
        {
            Bitmap previous = portraitImage;
            portraitImage = image;
            if (dialogPortrait != null) { dialogPortrait.Image = image; dialogPortrait.Visible = image != null; }
            if (previous != null && !ReferenceEquals(previous, image)) previous.Dispose();
            LayoutContent();
        }

        protected internal override bool OnUserClose()
        {
            if (!IsServerWindowOpen) return true;
            _ = LeaveDialog();
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing) return;
            if (dialogPortrait != null) dialogPortrait.Image = null;
            portraitImage?.Dispose(); portraitImage = null;
            base.Dispose(disposing);
        }

        private Task AnswerDialog(int answerId) => ReportAsync(() => Game.Interactions.Npc.AnswerAsync(answerId));
        private Task LeaveDialog() => ReportAsync(() => Game.Interactions.Npc.LeaveAsync());

        /// <summary>Bulle de la question : parchemin clair, filet doré et pointe vers l'illustration (triangle de <c>UI_NpcDialog</c>).</summary>
        private sealed class SpeechBubble : Panel
        {
            private const int Tail = 9;
            private bool showTail;

            internal SpeechBubble()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                BackColor = BotUi.Paper; Padding = new Padding(11, Tail + 8, 11, 8);
            }

            internal bool ShowTail { get => showTail; set { if (showTail == value) return; showTail = value; Invalidate(); } }

            protected override void OnPaint(PaintEventArgs e)
            {
                var graphics = e.Graphics;
                using (var paper = new SolidBrush(BotUi.Paper)) graphics.FillRectangle(paper, ClientRectangle);
                if (Width < 48 || Height < Tail + 14) return;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var body = new Rectangle(0, Tail, Width - 1, Height - Tail - 1);
                const int d = 12;
                using (var shape = new GraphicsPath())
                {
                    shape.AddArc(body.X, body.Y, d, d, 180, 90);
                    if (showTail) { shape.AddLine(body.X + 22, body.Y, body.X + 30, body.Y - Tail); shape.AddLine(body.X + 30, body.Y - Tail, body.X + 42, body.Y); }
                    shape.AddArc(body.Right - d, body.Y, d, d, 270, 90);
                    shape.AddArc(body.Right - d, body.Bottom - d, d, d, 0, 90);
                    shape.AddArc(body.X, body.Bottom - d, d, d, 90, 90);
                    shape.CloseFigure();
                    using (var fill = new SolidBrush(BotUi.PaperLight)) graphics.FillPath(fill, shape);
                    using (var border = new Pen(BotUi.Gold)) graphics.DrawPath(border, shape);
                }
            }
        }

        /// <summary>Cadre de l'illustration : parchemin, filet doré, image entière posée en bas comme le chargeur du client.</summary>
        private sealed class PortraitView : Control
        {
            private Image image;

            internal PortraitView()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                BackColor = BotUi.Paper; TabStop = false;
            }

            /// <summary>Image affichée ; le volet en reste propriétaire et la libère.</summary>
            internal Image Image { get => image; set { image = value; Invalidate(); } }

            protected override void OnPaint(PaintEventArgs e)
            {
                var graphics = e.Graphics; var area = ClientRectangle;
                using (var paper = new SolidBrush(BotUi.Paper)) graphics.FillRectangle(paper, area);
                if (area.Width < 16 || area.Height < 16) return;
                var frame = new Rectangle(0, 0, area.Width - 1, area.Height - 1);
                using (var light = new SolidBrush(BotUi.PaperLight)) graphics.FillRectangle(light, Rectangle.Inflate(frame, -2, -2));
                using (var gold = new Pen(BotUi.Gold)) graphics.DrawRectangle(gold, frame);
                if (image != null) ClientAssets.DrawFit(graphics, image, new RectangleF(6, 6, area.Width - 12, area.Height - 8), true);
            }
        }
    }

    /// <summary>Mesure du texte renvoyé à la ligne, avec la fonction GDI+ qui dessine la bulle et les réponses.</summary>
    internal static class DialogTextMeasure
    {
        private static readonly Bitmap surface = new Bitmap(1, 1);
        private static readonly Graphics graphics = Graphics.FromImage(surface);
        private static readonly object sync = new object();

        internal static StringFormat Format() => new StringFormat(StringFormatFlags.LineLimit)
        { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.Word,
          HotkeyPrefix = System.Drawing.Text.HotkeyPrefix.None };

        internal static int Height(string text, Font font, int width)
        {
            if (string.IsNullOrEmpty(text) || font == null || width < 1) return 0;
            lock (sync)
            {
                using (StringFormat format = Format())
                    return (int)Math.Ceiling(graphics.MeasureString(text, font, width, format).Height);
            }
        }
    }

    /// <summary>
    /// Ligne de réponse du dialogue (<c>QuestionViewerAnswerItem</c> du client) : puce ronde olive, texte renvoyé à la ligne,
    /// surbrillance olive au survol. Dessinée par GDI+ (aucun thème système), mesurée par <see cref="PreferredHeightFor"/>.
    /// </summary>
    internal sealed class AnswerButton : Button
    {
        private static readonly Color Highlight = Color.FromArgb(214, 205, 170);
        private const int TextLeft = 24, PaddingV = 7;
        private bool hover, pressed;

        internal AnswerButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat; UseVisualStyleBackColor = false; Cursor = Cursors.Hand;
            BackColor = BotUi.PaperLight; ForeColor = BotUi.Ink; UseMnemonic = false; Height = 30;
        }

        /// <summary>Hauteur nécessaire pour afficher tout le texte sur <paramref name="width"/> pixels (30 au minimum, comme le client).</summary>
        internal int PreferredHeightFor(int width) =>
            Math.Max(30, DialogTextMeasure.Height(Text ?? string.Empty, Font, Math.Max(20, width - TextLeft - 8)) + 2 * PaddingV);

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics; var area = ClientRectangle;
            using (var parent = new SolidBrush(Parent?.BackColor ?? BotUi.Paper)) graphics.FillRectangle(parent, area);
            if (area.Width < 4 || area.Height < 4) return;
            var row = new Rectangle(0, 0, area.Width - 1, area.Height - 1);
            bool active = Enabled && (hover || Focused);
            using (var fill = new SolidBrush(pressed && Enabled ? BotUi.Gold : active ? Highlight : BotUi.PaperLight)) graphics.FillRectangle(fill, row);
            using (var border = new Pen(active ? BotUi.Olive : BotUi.Gold)) graphics.DrawRectangle(border, row);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bullet = new Rectangle(9, area.Height / 2 - 4, 8, 8);
            using (var dot = new SolidBrush(Enabled ? BotUi.Olive : BotUi.Gold)) graphics.FillEllipse(dot, bullet);
            using (var ring = new Pen(BotUi.Frame)) graphics.DrawEllipse(ring, bullet);
            graphics.SmoothingMode = SmoothingMode.Default;
            using (var brush = new SolidBrush(Enabled ? BotUi.Ink : BotUi.Muted))
            using (StringFormat format = DialogTextMeasure.Format())
                graphics.DrawString(Text ?? string.Empty, Font, brush, new RectangleF(TextLeft, PaddingV, area.Width - TextLeft - 8, area.Height - 2 * PaddingV), format);
            if (Focused && Enabled && ShowFocusCues) ControlPaint.DrawFocusRectangle(graphics, Rectangle.Inflate(row, -2, -2));
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    }
}
