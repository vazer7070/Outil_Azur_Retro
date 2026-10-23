using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Interactions;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>
    /// Saisie du code d'un coffre ou d'une maison (interface <c>KeyCode</c> du client, <c>dofus.aks.Key</c>) : s'ouvre sur
    /// <c>KCK&lt;type&gt;|&lt;cases&gt;</c>, se ferme sur <c>KV</c>. Comme le client, un chiffre (bouton du pavé ou clavier) remplit
    /// la case choisie puis passe à la suivante ; une case vide vaut « _ ». « Déverrouiller »/« Changer » envoie
    /// <c>KK&lt;type&gt;|&lt;code&gt;</c>, « Aucun code » (changement seulement) <c>KK1|-</c>, Fermer (ou ×/Échap) <c>KV</c>.
    /// </summary>
    public sealed class KeyCodePanel : GamePanel
    {
        private const string NoCodeText = "La saisie s'ouvre quand le serveur demande le code d'un coffre ou d'une porte.";
        private Label codeTitle, codeDescription, codeStatus;
        private CodeSlotsView codeSlots;
        private FlowLayoutPanel codeKeys;
        private Control codeValidate, codeNoCode, codeClear, codeLeave;
        private bool wasOpen;
        private int openedType = -1, openedSlots;

        public override string Title => Text("CODE", "Code");
        public override Image Icon => ClientAssets.Icon(Path.Combine("Interactifs", "code-0"), 24);
        public override bool IsModal => true;
        public override bool IsServerWindowOpen => Game?.Interactions?.Interactive?.Code?.IsOpen == true;

        protected override Control CreateView()
        {
            var page = new KeyCodePage(this) { Dock = DockStyle.Fill, BackColor = BotUi.Paper, Padding = new Padding(7), Margin = new Padding(0) };
            codeTitle = MakeLabel(Text("TYPE_CODE", "Saisie du code"), 11, true);
            codeTitle.Dock = DockStyle.Top; codeTitle.Height = 26;
            codeDescription = MakeLabel(string.Empty, 9);
            codeDescription.Dock = DockStyle.Top; codeDescription.Height = 52; codeDescription.ForeColor = BotUi.Muted;
            codeSlots = new CodeSlotsView { Dock = DockStyle.Top, Height = 54, AccessibleName = "Cases du code", TabStop = true };
            codeSlots.SlotClicked += index => { codeSlots.Current = index; if (codeSlots.CanFocus) codeSlots.Focus(); };
            codeKeys = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 118, WrapContents = true, Padding = new Padding(0, 6, 0, 0),
                BackColor = BotUi.Paper, AccessibleName = "Pavé numérique" };
            for (int digit = 1; digit <= 10; digit++)
            {
                int value = digit % 10;
                var key = (ClientButton)MakeButton(string.Empty, (s, e) => TypeDigit(value), false, 54);
                key.Height = 50; key.Margin = new Padding(0, 0, 6, 6); key.Name = "code-key-" + value; key.AccessibleName = "Chiffre " + value;
                key.Image = ClientAssets.Get(Path.Combine("Interactifs", "code-" + value));
                if (key.Image == null) key.Text = value.ToString();
                codeKeys.Controls.Add(key);
            }
            codeStatus = MakeStatus(NoCodeText);
            codeValidate = MakeButton(Text("UNLOCK", "Déverrouiller"), async (s, e) => await Validate(), true, 130);
            codeNoCode = MakeButton(Text("NO_CODE", "Aucun code"), async (s, e) => await SendNoCode(), false, 110);
            codeClear = MakeButton("Effacer", (s, e) => ClearSlot(), false, 80);
            codeLeave = MakeButton(Text("CLOSE", "Fermer"), async (s, e) => await Leave(), false, 80);
            page.Controls.Add(codeKeys); page.Controls.Add(codeSlots); page.Controls.Add(codeDescription); page.Controls.Add(codeTitle);
            page.Controls.Add(codeStatus); page.Controls.Add(BotUi.Actions(codeValidate, codeNoCode, codeClear, codeLeave));
            return page;
        }

        protected override void OnBind(GameClass game) { wasOpen = false; game.Interactions.Interactive.Code.Changed += OnServerChanged; OnServerChanged(); }
        protected override void OnUnbind(GameClass game) { wasOpen = false; if (game.Interactions != null) game.Interactions.Interactive.Code.Changed -= OnServerChanged; }

        private void OnServerChanged() => OnUi(() =>
        {
            bool open = IsServerWindowOpen;
            if (open && !wasOpen) RequestShow();
            else if (!open && wasOpen) RaiseClosed();
            wasOpen = open;
            RefreshView();
            if (open && codeSlots != null && codeSlots.CanFocus) codeSlots.Focus();
        });

        public override void RefreshView()
        {
            if (Game == null || codeSlots == null) return;
            KeyCodeDialog code = Game.Interactions.Interactive.Code;
            if (!code.IsOpen) { openedType = -1; openedSlots = 0; codeSlots.Reset(0); }
            else if (code.ChangeType != openedType || code.SlotCount != openedSlots)
            {
                // Nouvelle saisie : cases vides, première case choisie (KeyCode.initData).
                openedType = code.ChangeType; openedSlots = code.SlotCount;
                codeSlots.Reset(code.SlotCount);
            }
            codeValidate.Text = code.IsChange ? Text("CHANGE", "Changer") : Text("UNLOCK", "Déverrouiller");
            codeDescription.Text = !code.IsOpen ? string.Empty : code.IsChange
                ? Text("LOCK_INFOS", "Choisissez le nouveau code avec le pavé ou le clavier, puis validez.")
                : Text("UNLOCK_INFOS", "Composez le code avec le pavé ou le clavier, puis validez.");
            codeNoCode.Visible = code.IsOpen && code.IsChange;
            codeStatus.Text = code.IsOpen ? (code.LastMessage.Length > 0 ? code.LastMessage : code.SlotCount + " case(s) à remplir.")
                : NoCodeText + (code.LastMessage.Length > 0 ? " " + code.LastMessage : string.Empty);
            codeStatus.ForeColor = code.LastCodeRefused ? Color.FromArgb(150, 45, 30) : BotUi.Muted;
            bool usable = Connected && code.IsOpen;
            codeValidate.Enabled = codeLeave.Enabled = codeNoCode.Enabled = usable;
            codeClear.Enabled = usable && codeSlots.SlotCount > 0;
            foreach (Control key in codeKeys.Controls) key.Enabled = usable;
            codeSlots.Enabled = code.IsOpen;
            codeSlots.Invalidate();
        }

        /// <summary>Code tel qu'il sera envoyé (« _ » pour une case vide).</summary>
        internal string TypedCode => codeSlots?.Code ?? string.Empty;

        private void TypeDigit(int digit)
        {
            if (!IsServerWindowOpen || codeSlots == null || codeSlots.SlotCount == 0) return;
            codeSlots.Set((char)('0' + digit));
        }

        private void ClearSlot()
        {
            if (codeSlots == null || codeSlots.SlotCount == 0) return;
            codeSlots.Set(KeyCodeDialog.EmptySlot);
        }

        /// <summary>Clavier du panneau : chiffres, ← →, Retour arrière/Suppr (case vidée) et Entrée (valider).</summary>
        internal bool HandleKey(Keys keys)
        {
            if (!IsServerWindowOpen || codeSlots == null || codeSlots.SlotCount == 0) return false;
            if ((keys & (Keys.Control | Keys.Alt)) != 0) return false;
            Keys key = keys & Keys.KeyCode;
            if (key >= Keys.D0 && key <= Keys.D9 && (keys & Keys.Shift) == 0) { TypeDigit(key - Keys.D0); return true; }
            if (key >= Keys.NumPad0 && key <= Keys.NumPad9) { TypeDigit(key - Keys.NumPad0); return true; }
            switch (key)
            {
                case Keys.Left: codeSlots.Previous(); return true;
                case Keys.Right: codeSlots.Next(); return true;
                case Keys.Back: case Keys.Delete: ClearSlot(); return true;
                case Keys.Enter: _ = Validate(); return true;
                default: return false;
            }
        }

        protected internal override bool OnUserClose()
        {
            if (!IsServerWindowOpen) return true;
            _ = Leave();
            return false;
        }

        private Task Validate()
        {
            if (codeValidate == null || !codeValidate.Enabled) return Task.CompletedTask;
            string typed = TypedCode;
            return ReportAsync(() => Game.Interactions.Interactive.Code.SendCodeAsync(typed));
        }

        private Task SendNoCode() => codeNoCode.Enabled ? ReportAsync(() => Game.Interactions.Interactive.Code.NoCodeAsync()) : Task.CompletedTask;
        private Task Leave() => ReportAsync(() => Game.Interactions.Interactive.Code.LeaveAsync());

        private static string Text(string key, string fallback) => LangData.Text.Has(key) ? LangData.Text.Get(key).Trim() : fallback;

        /// <summary>Page du panneau : les touches arrivent ici tant que le focus est dans la saisie.</summary>
        private sealed class KeyCodePage : Panel
        {
            private readonly KeyCodePanel owner;
            internal KeyCodePage(KeyCodePanel owner) { this.owner = owner; }
            protected override bool ProcessCmdKey(ref Message msg, Keys keyData) => owner.HandleKey(keyData) || base.ProcessCmdKey(ref msg, keyData);
        }
    }

    /// <summary>
    /// Cases du code dessinées comme <c>UI_KeyCodeContainer</c> : chiffre exporté du client dans la case, case choisie
    /// encadrée d'olive, « _ » pour une case vide. Le choix boucle d'une extrémité à l'autre (<c>selectNextSlot</c>).
    /// </summary>
    internal sealed class CodeSlotsView : Control
    {
        private const int SlotSize = 38, Gap = 6;
        private char[] slots = new char[0];
        private int current;

        internal event Action<int> SlotClicked;

        internal CodeSlotsView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            BackColor = BotUi.Paper; Cursor = Cursors.Hand;
        }

        internal int SlotCount => slots.Length;
        internal string Code => new string(slots);
        internal int Current
        {
            get => current;
            set { if (slots.Length == 0) return; current = ((value % slots.Length) + slots.Length) % slots.Length; Invalidate(); }
        }

        internal void Reset(int count)
        {
            slots = Enumerable.Repeat(KeyCodeDialog.EmptySlot, Math.Max(0, Math.Min(KeyCodeDialog.MaxSlots, count))).ToArray();
            current = 0; Invalidate();
        }

        /// <summary>Remplit la case choisie puis passe à la suivante (<c>setKeyInCurrentSlot</c>).</summary>
        internal void Set(char value)
        {
            if (slots.Length == 0) return;
            slots[current] = value;
            Current = current + 1;
        }

        internal void Next() => Current = current + 1;
        internal void Previous() => Current = current - 1;

        private Rectangle SlotBounds(int index)
        {
            int total = slots.Length * SlotSize + Math.Max(0, slots.Length - 1) * Gap;
            int left = Math.Max(0, (Width - total) / 2);
            return new Rectangle(left + index * (SlotSize + Gap), Math.Max(0, (Height - SlotSize) / 2), SlotSize, SlotSize);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            for (int index = 0; index < slots.Length; index++)
                if (SlotBounds(index).Contains(e.Location)) { SlotClicked?.Invoke(index); return; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            using (var paper = new SolidBrush(BackColor)) graphics.FillRectangle(paper, ClientRectangle);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                for (int index = 0; index < slots.Length; index++)
                {
                    Rectangle box = SlotBounds(index);
                    using (var fill = new SolidBrush(BotUi.PaperLight)) graphics.FillRectangle(fill, box);
                    bool selected = index == current && Enabled;
                    using (var border = new Pen(selected ? BotUi.Olive : BotUi.Frame, selected ? 3 : 1)) graphics.DrawRectangle(border, box);
                    using (var rule = new Pen(BotUi.Gold)) graphics.DrawRectangle(rule, Rectangle.Inflate(box, -4, -4));
                    char value = slots[index];
                    Image digit = value >= '0' && value <= '9' ? ClientAssets.Get(Path.Combine("Interactifs", "code-" + value)) : null;
                    if (digit != null) ClientAssets.DrawFit(graphics, digit, Rectangle.Inflate(box, -6, -5));
                    else using (var ink = new SolidBrush(value == KeyCodeDialog.EmptySlot ? BotUi.Muted : BotUi.Ink))
                        graphics.DrawString(value.ToString(), BotFonts.Get(13, FontStyle.Bold), ink, box, format);
                }
                if (slots.Length == 0)
                    using (var ink = new SolidBrush(BotUi.Muted)) graphics.DrawString("Aucune saisie en cours", BotFonts.Get(9), ink, ClientRectangle, format);
            }
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }
    }
}
