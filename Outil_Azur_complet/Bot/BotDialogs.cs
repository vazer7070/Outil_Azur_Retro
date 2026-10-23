using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Controls;
using Outil_Azur_complet.Bot.Controls.Chat;
using Tool_BotProtocol.Game.Perso.Inventory;

namespace Outil_Azur_complet.Bot
{
    /// <summary>Réponse d'une boîte de dialogue du bot ; <see cref="None"/> quand elle est fermée sans réponse.</summary>
    public enum BotDialogResult { None, Yes, No, Ignore, Ok, Cancel, Replay }

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

        // Lot X2 : demande en mariage (GA;617) et fin de partie (GO).
        /// <summary>Oui / Non retrouvable par son nom (mariage <c>CAUTION_YESNO</c>), refermé par <see cref="Dismiss"/>.</summary>
        public static Task<BotDialogResult> AskYesNoAsync(Control owner, string title, string message, string name) =>
            ShowAsync(owner, title, message, name, BotDialogResult.No, BotDialogResult.Yes, BotDialogResult.No);
        /// <summary>
        /// Fin de partie (<c>GO</c>, interface <c>GameOver</c> du client) : un seul bouton « Rejouer » (<c>REPLAY_WORD</c>),
        /// qui ramène au choix du personnage ; Échap vaut « Rejouer », la connexion étant déjà fermée.
        /// </summary>
        public static Task<BotDialogResult> GameOverAsync(Control owner, string title, string message, string name) =>
            ShowAsync(owner, title, message, name, BotDialogResult.Replay, BotDialogResult.Replay);

        // Lot X3 : nouveau niveau (AN) et perte de connexion.
        /// <summary>Information « OK » retrouvable par son nom (<c>ERROR_BOX</c> du client : <c>NEW_LEVEL</c>, <c>DISCONNECT</c>).</summary>
        public static Task<BotDialogResult> InfoAsync(Control owner, string title, string message, string name) =>
            ShowAsync(owner, title, message, name, BotDialogResult.Ok, BotDialogResult.Ok);

        // Lot X12 : boîte de quantité du client pour jeter, détruire, proposer, déposer et retirer.
        /// <summary>
        /// <c>PopupQuantity</c> du client (<see cref="Controls.QuantityPopup"/>, au pointeur, Entrée valide, Échap abandonne) avec
        /// <paramref name="value"/> dans le champ et <paramref name="maximum"/> pour « max », puis la validation commune du client :
        /// une valeur nulle, vide ou abandonnée rend <c>null</c> (rien n'est envoyé), sinon <c>Math.min(valeur, maximum)</c>.
        /// L'appelant borne encore à la pile qu'il relit après la réponse, celle-ci ayant pu changer pendant que la boîte était ouverte.
        /// </summary>
        public static async Task<int?> AskQuantityAsync(Control owner, int value, int maximum)
        {
            int? chosen = await Controls.QuantityPopup.AskAsync(owner, value, maximum);
            if (chosen == null || chosen.Value <= 0) return null;
            return Math.Min(chosen.Value, Math.Max(1, maximum));
        }

        // Lot X13 : objets vivants (fenêtres ChooseFeed et ChooseItemSkin du client).
        /// <summary>Nom de la fenêtre du choix de l'aliment (tests, <see cref="Dismiss"/>).</summary>
        public const string FeedDialogName = "living-feed";
        /// <summary>Nom de la fenêtre du choix de l'apparence.</summary>
        public const string SkinDialogName = "living-skin";

        /// <summary>
        /// <c>ChooseFeed</c> du client : sans aliment possible, <c>ERROR_NO_FOOD_LIVING_ITEM</c> (« Tu ne possèdes pas d'objet
        /// capable de nourrir %1 ») et rien d'autre ; sinon la grille des aliments (titre <c>FEED_ITEM</c>, texte
        /// <c>SELECT_ITEM</c>, bouton <c>VALIDATE</c>, double-clic pour valider), puis la question <c>CONFIRM_FOOD_LIVING_ITEM</c>
        /// (<c>CAUTION_YESNO</c>). Rend l'aliment confirmé, ou null (fermé, Échap, « Non »). Rien n'est envoyé ici.
        /// </summary>
        public static async Task<InventoryObjects> ChooseFoodAsync(Control owner, InventoryObjects item, IReadOnlyList<InventoryObjects> foods)
        {
            if (item == null) return null;
            if (foods == null || foods.Count == 0)
            {
                await InfoAsync(owner, ChatUiText.Get("ERROR_WORD", "Erreur"),
                    ChatUiText.Get("ERROR_NO_FOOD_LIVING_ITEM", "Tu ne possèdes pas d'objet capable de nourrir %1", item.Name), FeedDialogName + "-none");
                return null;
            }
            InventoryObjects[] choices = foods.Where(food => food != null).ToArray();
            object chosen = await ShowChoiceAsync(owner, () =>
            {
                var grid = new ItemGrid { Size = new Size(6 * (ItemGrid.CellSize + 4) + 4, 3 * (ItemGrid.CellSize + 4) + 4), Name = "living-feed-grid" };
                grid.SetItems(choices);
                var form = new BotChoiceForm(ChatUiText.Get("FEED_ITEM", "Nourrir un objet"), ChatUiText.Get("SELECT_ITEM", "Sélectionne un objet"), grid, () => grid.SelectedItem)
                    { Name = FeedDialogName };
                grid.ItemActivated += food => form.Confirm();
                return form;
            });
            if (!(chosen is InventoryObjects picked)) return null;
            BotDialogResult answer = await AskYesNoAsync(owner, ChatUiText.Get("CAUTION", "Attention"),
                ChatUiText.Get("CONFIRM_FOOD_LIVING_ITEM", "Êtes-vous certain de vouloir utiliser cet objet comme nourriture ?"), FeedDialogName + "-confirm");
            return answer == BotDialogResult.Yes ? picked : null;
        }

        /// <summary>
        /// <c>ChooseItemSkin</c> du client : une case par apparence débloquée (1 au niveau de l'objet, icône du modèle de l'objet
        /// vivant : le bot n'a qu'une image par modèle, pas une par apparence), l'apparence portée cerclée, titre
        /// <c>CHOOSE_SKIN</c>, bouton <c>VALIDATE</c> ou double-clic. Rend l'apparence choisie (1 à niveau) ou null.
        /// </summary>
        public static async Task<int?> ChooseSkinAsync(Control owner, InventoryObjects item, LivingItem living)
        {
            if (item == null || living == null || !living.Skineable) return null;
            int template = living.SkinTemplate(item.ID), count = living.Level, current = living.Skin + 1;
            object chosen = await ShowChoiceAsync(owner, () =>
            {
                var grid = new LivingSkinGrid(template, count, current) { Name = "living-skin-grid" };
                var form = new BotChoiceForm(ChatUiText.Get("CHOOSE_SKIN", "Choix de l'apparence"), item.Name, grid, () => grid.SelectedSkin) { Name = SkinDialogName };
                grid.SkinActivated += skin => form.Confirm();
                return form;
            });
            return chosen is int value ? value : (int?)null;
        }

        private static Task<object> ShowChoiceAsync(Control owner, Func<BotChoiceForm> create)
        {
            var completion = new TaskCompletionSource<object>();
            Action show = () =>
            {
                try
                {
                    Form parent = owner == null || owner.IsDisposed ? null : owner.FindForm();
                    BotChoiceForm dialog = create();
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
                catch (InvalidOperationException) { completion.TrySetResult(null); }
            }
            else show();
            return completion.Task;
        }

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
                case BotDialogResult.Replay: return Tool_BotProtocol.Game.Actions.MapActionTexts.Replay;
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

    /// <summary>
    /// Fenêtre de choix du bot (lot X13) au style des boîtes du client : titre, texte, une grille, « Valider » et « Annuler ».
    /// Sans sélection, « Valider » ne ferme rien et met le texte en évidence (<c>SELECT_ITEM</c> du client) ; Échap annule.
    /// </summary>
    internal sealed class BotChoiceForm : Form
    {
        private readonly Label prompt;
        private readonly Func<object> selected;
        private Point dragStart;
        private bool dragging;

        internal BotChoiceForm(string title, string promptText, Control content, Func<object> selection)
        {
            selected = selection ?? (() => null);
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None; KeyPreview = true; DoubleBuffered = true;
            BackColor = BotUi.Frame; ForeColor = BotUi.Ink; Font = BotFonts.Get(9); Padding = new Padding(3);
            Text = string.IsNullOrEmpty(title) ? "AzurClientRetro" : title; AccessibleName = Text;

            var frame = new ClientPanel { Dock = DockStyle.Fill, BackColor = BotUi.Paper, Padding = new Padding(12, 8, 12, 10) };
            var heading = new Label { Dock = DockStyle.Top, Height = 26, Text = Text, Font = BotFonts.Get(10, FontStyle.Bold),
                ForeColor = BotUi.Ink, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Name = "dialog-title" };
            var rule = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = BotUi.Gold };
            prompt = new Label { Dock = DockStyle.Top, Height = 26, Text = promptText ?? string.Empty, ForeColor = BotUi.Muted, BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, UseMnemonic = false, Name = "dialog-message" };
            Size wanted = content.Size;
            var holder = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(0, 2, 0, 2) };
            content.Dock = DockStyle.Fill;
            holder.Controls.Add(content);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false, Padding = new Padding(0, 6, 0, 0), BackColor = Color.Transparent, Name = "dialog-actions" };
            var validate = (Button)BotUi.Button(LivingItem.Text("VALIDATE", "Valider"), (s, e) => Confirm(), true, 100);
            var cancel = (Button)BotUi.Button(BotDialogs.Text(BotDialogResult.Cancel), (s, e) => Close(), false, 100);
            Font own = validate.Font; validate.Font = BotFonts.Get(9, FontStyle.Bold); own.Dispose();
            foreach (Button button in new[] { validate, cancel }) { button.Margin = new Padding(7, 0, 0, 0); button.AccessibleName = button.Text; }
            validate.Name = "dialog-validate"; cancel.Name = "dialog-cancel";
            actions.Controls.Add(validate); actions.Controls.Add(cancel);
            CancelButton = cancel;
            frame.Controls.Add(holder); frame.Controls.Add(actions); frame.Controls.Add(prompt); frame.Controls.Add(rule); frame.Controls.Add(heading);
            Controls.Add(frame);
            ClientSize = new Size(Math.Max(300, wanted.Width + 4) + 30, 26 + 1 + 26 + wanted.Height + 4 + 42 + 24);
            heading.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { dragging = true; dragStart = e.Location; } };
            heading.MouseMove += (s, e) => { if (dragging) Location = new Point(Left + e.X - dragStart.X, Top + e.Y - dragStart.Y); };
            heading.MouseUp += (s, e) => dragging = false;
        }

        /// <summary>Valeur validée (objet ou apparence), null si la fenêtre a été fermée sans choix.</summary>
        internal object Result { get; private set; }

        /// <summary>« Valider » : ferme avec la sélection, ou met le texte en évidence s'il n'y en a pas.</summary>
        internal void Confirm()
        {
            object value = selected();
            if (value == null)
            {
                prompt.ForeColor = Color.FromArgb(140, 70, 30); prompt.Font = BotFonts.Get(9, FontStyle.Bold);
                return;
            }
            Result = value;
            Close();
        }

        internal void PlaceOver(Form parent)
        {
            Rectangle area = parent != null && !parent.IsDisposed && parent.Visible && parent.WindowState != FormWindowState.Minimized
                ? parent.Bounds : Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Left + Math.Max(0, (area.Width - Width) / 2), area.Top + Math.Max(0, (area.Height - Height) / 2));
        }

        /// <summary>Libère la fenêtre non modale après la fin du message de fermeture.</summary>
        internal void BeginDispose()
        {
            if (IsDisposed) return;
            try { if (IsHandleCreated) { BeginInvoke((Action)Dispose); return; } }
            catch (InvalidOperationException) { }
            Dispose();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; return; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var gold = new Pen(BotUi.Gold)) e.Graphics.DrawRectangle(gold, 0, 0, Width - 1, Height - 1);
        }
    }

    /// <summary>
    /// Cases des apparences d'un objet vivant (<c>ChooseItemSkin</c>) : fond <c>case-inventaire</c>, icône du modèle, numéro de
    /// l'apparence, surbrillance de la sélection ; l'apparence portée est cerclée d'olive. Clic : choisir ; double-clic ou
    /// Entrée : valider. Rien n'est lu sur le disque pendant un dessin (icône par <c>ItemIcons</c>, en tâche de fond).
    /// </summary>
    public sealed class LivingSkinGrid : Control
    {
        private const int Gap = 4, Columns = 5;
        private readonly int template, count, current;
        private int selected;

        public LivingSkinGrid(int templateId, int skins, int currentSkin)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint | ControlStyles.Selectable, true);
            template = templateId; count = Math.Max(1, Math.Min(LivingItem.LevelSteps.Count, skins)); current = currentSkin;
            selected = current >= 1 && current <= count ? current : 0;
            BackColor = BotUi.PaperLight; Font = BotFonts.Get(8, FontStyle.Bold);
            int rows = (count + Columns - 1) / Columns;
            Size = new Size(Columns * (ItemGrid.CellSize + Gap) + Gap, rows * (ItemGrid.CellSize + Gap) + Gap);
            ItemAssets.Ensure(this);
        }

        /// <summary>Nombre d'apparences proposées (niveau de l'objet).</summary>
        public int SkinCount => count;
        /// <summary>Apparence portée (1 à 20), cerclée dans la grille.</summary>
        public int CurrentSkin => current;
        /// <summary>Apparence choisie (1 à <see cref="SkinCount"/>), ou null.</summary>
        public int? SelectedSkin => selected >= 1 ? selected : (int?)null;
        /// <summary>Double-clic ou Entrée sur une apparence.</summary>
        public event Action<int> SkinActivated;

        /// <summary>Choisit une apparence (clavier, tests) ; faux hors de 1 à <see cref="SkinCount"/>.</summary>
        public bool SelectSkin(int skin)
        {
            if (skin < 1 || skin > count) return false;
            selected = skin; Invalidate();
            return true;
        }

        private Rectangle CellBounds(int skin)
        {
            int index = skin - 1;
            return new Rectangle(Gap + index % Columns * (ItemGrid.CellSize + Gap), Gap + index / Columns * (ItemGrid.CellSize + Gap), ItemGrid.CellSize, ItemGrid.CellSize);
        }

        private int SkinAt(Point point)
        {
            for (int skin = 1; skin <= count; skin++) if (CellBounds(skin).Contains(point)) return skin;
            return 0;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.Clear(BackColor);
            using (var pen = new Pen(BotUi.Gold)) graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            Image cell = ItemAssets.Cached("case-inventaire"), glow = ItemAssets.Cached("case-surbrillance");
            Image icon = ItemIcons.TryGet(template, this);
            for (int skin = 1; skin <= count; skin++)
            {
                Rectangle bounds = CellBounds(skin);
                if (cell != null) graphics.DrawImage(cell, bounds);
                else
                {
                    using (var fill = new SolidBrush(BotUi.Paper)) graphics.FillRectangle(fill, bounds);
                    using (var pen = new Pen(BotUi.Gold)) graphics.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
                }
                if (icon != null) lock (icon) ClientAssets.DrawFit(graphics, icon, Rectangle.Inflate(bounds, -6, -6));
                ItemCellPainter.DrawQuantity(graphics, bounds, skin);
                if (skin == current) using (var pen = new Pen(BotUi.Olive, 2)) graphics.DrawEllipse(pen, bounds.X + 3, bounds.Y + 3, 9, 9);
                if (skin == selected)
                {
                    if (glow != null) graphics.DrawImage(glow, Rectangle.Inflate(bounds, 1, 1));
                    else using (var pen = new Pen(BotUi.Olive, 2)) graphics.DrawRectangle(pen, Rectangle.Inflate(bounds, -1, -1));
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            int skin = SkinAt(e.Location);
            if (skin > 0) SelectSkin(skin);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            int skin = SkinAt(e.Location);
            if (skin > 0 && e.Button == MouseButtons.Left) { SelectSkin(skin); SkinActivated?.Invoke(skin); }
        }

        protected override bool IsInputKey(Keys keyData) =>
            keyData == Keys.Left || keyData == Keys.Right || keyData == Keys.Up || keyData == Keys.Down || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Enter && selected > 0) { SkinActivated?.Invoke(selected); e.Handled = true; return; }
            int step = e.KeyCode == Keys.Left ? -1 : e.KeyCode == Keys.Right ? 1 : e.KeyCode == Keys.Up ? -Columns : e.KeyCode == Keys.Down ? Columns : 0;
            if (step == 0) return;
            SelectSkin(Math.Max(1, Math.Min(count, selected < 1 ? 1 : selected + step)));
            e.Handled = true;
        }
    }
}
