using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Exchanges;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>
    /// Icônes d'objets du client : <c>clips/items/&lt;type&gt;/&lt;gfx&gt;.swf</c> exportés en PNG par le lot D3 sous
    /// <c>ressources/Bot/Items/&lt;type&gt;/&lt;gfx&gt;.png</c>, type et numéro d'image lus dans <c>LangData.Item</c> (D4).
    /// Les fichiers sont lus hors du thread de l'interface ; tant qu'une icône manque (dossier absent, textes non chargés),
    /// les cases affichent les initiales de l'objet.
    /// </summary>
    public static class ExchangeItemIcons
    {
        private static readonly object sync = new object();
        private static readonly Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>(StringComparer.Ordinal);
        private static readonly HashSet<string> loading = new HashSet<string>(StringComparer.Ordinal);
        private static string folder = DefaultFolder;
        private static int generation;

        /// <summary><c>ressources/Bot/Items</c> à côté de l'exécutable.</summary>
        public static string DefaultFolder =>
            Path.Combine(Path.GetDirectoryName(typeof(ExchangeItemIcons).Assembly.Location) ?? string.Empty, "ressources", "Bot", "Items");

        /// <summary>Dossier des icônes ; le changer oublie les icônes déjà lues (tests, autre installation).</summary>
        public static string Folder
        {
            get { lock (sync) return folder; }
            set
            {
                // Les images oubliées ne sont pas libérées ici : une case peut être en train de les dessiner.
                lock (sync) { folder = string.IsNullOrEmpty(value) ? DefaultFolder : value; generation++; cache.Clear(); loading.Clear(); }
            }
        }

        /// <summary>Levé (hors du thread de l'interface) quand une icône vient d'être lue.</summary>
        public static event Action Loaded;

        /// <summary>Chemin attendu de l'icône d'un modèle, ou <c>null</c> si <c>LangData</c> ne connaît pas son type ou son image.</summary>
        public static string PathOf(int templateId)
        {
            if (templateId <= 0) return null;
            int? type = LangData.Item.Type(templateId), gfx = LangData.Item.Gfx(templateId);
            if (type == null || gfx == null) return null;
            return Path.Combine(Folder, type.Value.ToString(CultureInfo.InvariantCulture), gfx.Value.ToString(CultureInfo.InvariantCulture) + ".png");
        }

        /// <summary>Icône déjà lue, ou <c>null</c> : la lecture est alors lancée en tâche de fond et <see cref="Loaded"/> suivra.</summary>
        public static Image TryGet(int templateId)
        {
            string path = PathOf(templateId);
            if (path == null) return null;
            int expected;
            lock (sync)
            {
                if (cache.TryGetValue(path, out Bitmap image)) return image;
                if (!loading.Add(path)) return null;
                expected = generation;
            }
            Task.Run(() => Load(path, expected));
            return null;
        }

        /// <summary>Vrai quand la lecture de l'icône du modèle est terminée (image trouvée ou non).</summary>
        public static bool IsResolved(int templateId)
        {
            string path = PathOf(templateId);
            if (path == null) return true;
            lock (sync) return cache.ContainsKey(path);
        }

        private static void Load(string path, int expected)
        {
            Bitmap image = null;
            try
            {
                if (File.Exists(path))
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (Image source = Image.FromStream(stream)) image = new Bitmap(source);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException ||
                error is System.Runtime.InteropServices.ExternalException || error is OutOfMemoryException)
            {
                image = null;
            }
            bool stored;
            lock (sync)
            {
                stored = expected == generation;
                if (stored) { loading.Remove(path); cache[path] = image; }
            }
            if (!stored) { image?.Dispose(); return; }
            if (image == null) return;
            Action handlers = Loaded;
            if (handlers == null) return;
            foreach (Action handler in handlers.GetInvocationList())
            {
                try { handler(); }
                catch (Exception) { /* Une case déjà libérée ne doit pas empêcher les autres d'être prévenues. */ }
            }
        }
    }

    /// <summary>
    /// Grille d'objets au style du client (cases <c>case-inventaire</c>, surbrillance <c>case-surbrillance</c>) : icône
    /// ou initiales, quantité en pastille, cadre olive quand le côté est « prêt ». La molette fait défiler les lignes.
    /// </summary>
    public sealed class ExchangeItemGrid : Control
    {
        public const int CellSize = 40;
        private const int Gap = 4;
        private readonly ToolTip tips = new ToolTip();
        private IReadOnlyList<ExchangeItem> items = new ExchangeItem[0];
        private uint? selectedId;
        private int scrollRow, tipIndex = -1;
        private bool ready;
        private string emptyText = string.Empty;

        public ExchangeItemGrid()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint | ControlStyles.Selectable, true);
            BackColor = BotUi.PaperLight; ForeColor = BotUi.Ink; Font = BotFonts.Get(8, FontStyle.Bold);
            MinimumSize = new Size(CellSize + 2 * Gap, CellSize + 2 * Gap);
            ExchangeItemIcons.Loaded += OnIconLoaded;
        }

        /// <summary>Objets affichés, dans l'ordre des cases.</summary>
        public IReadOnlyList<ExchangeItem> Items => items;
        public ExchangeItem SelectedItem => selectedId == null ? null : items.FirstOrDefault(item => item.Id == selectedId.Value);
        public event EventHandler SelectionChanged;

        /// <summary>Cadre olive épais quand le côté a validé (<c>EK1</c>), filet doré sinon.</summary>
        public bool Ready { get => ready; set { if (ready == value) return; ready = value; Invalidate(); } }
        /// <summary>Texte affiché dans une grille vide.</summary>
        public string EmptyText { get => emptyText; set { emptyText = value ?? string.Empty; if (items.Count == 0) Invalidate(); } }

        /// <summary>Remplace les objets ; la sélection est conservée si l'objet est toujours là.</summary>
        public void SetItems(IEnumerable<ExchangeItem> values)
        {
            items = (values ?? Enumerable.Empty<ExchangeItem>()).Where(item => item != null).ToArray();
            if (selectedId != null && SelectedItem == null) { selectedId = null; SelectionChanged?.Invoke(this, EventArgs.Empty); }
            scrollRow = Math.Max(0, Math.Min(scrollRow, MaxScroll()));
            tipIndex = -1;
            Invalidate();
        }

        /// <summary>Sélectionne l'objet d'identifiant donné (clavier, tests) ; renvoie faux s'il n'est pas dans la grille.</summary>
        public bool SelectItem(uint id)
        {
            if (!items.Any(item => item.Id == id)) return false;
            SetSelection(id);
            return true;
        }

        public void ClearSelection() => SetSelection(null);

        private void SetSelection(uint? id)
        {
            if (selectedId == id) return;
            selectedId = id;
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        private int Columns => Math.Max(1, (ClientSize.Width - Gap) / (CellSize + Gap));
        private int VisibleRows => Math.Max(1, (ClientSize.Height - Gap) / (CellSize + Gap));
        private int MaxScroll() => Math.Max(0, (items.Count + Columns - 1) / Columns - VisibleRows);

        private Rectangle CellBounds(int index)
        {
            int column = index % Columns, row = index / Columns - scrollRow;
            return new Rectangle(Gap + column * (CellSize + Gap), Gap + row * (CellSize + Gap), CellSize, CellSize);
        }

        private int IndexAt(Point point)
        {
            for (int index = scrollRow * Columns; index < items.Count; index++)
                if (CellBounds(index).Contains(point)) return index;
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.Clear(BackColor);
            Rectangle frame = new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            using (var pen = new Pen(ready ? BotUi.Olive : BotUi.Gold, ready ? 3 : 1))
                graphics.DrawRectangle(pen, ready ? Rectangle.Inflate(frame, -1, -1) : frame);
            if (items.Count == 0)
            {
                TextRenderer.DrawText(graphics, emptyText, BotFonts.Get(8), Rectangle.Inflate(ClientRectangle, -6, -6), BotUi.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                return;
            }
            Image cell = ClientAssets.Get("case-inventaire"), glow = ClientAssets.Get("case-surbrillance");
            for (int index = scrollRow * Columns; index < items.Count; index++)
            {
                Rectangle bounds = CellBounds(index);
                if (bounds.Top >= ClientSize.Height) break;
                if (!bounds.IntersectsWith(e.ClipRectangle)) continue;
                ExchangeItem item = items[index];
                if (cell != null) graphics.DrawImage(cell, bounds);
                else
                {
                    using (var fill = new SolidBrush(BotUi.Paper)) graphics.FillRectangle(fill, bounds);
                    using (var pen = new Pen(BotUi.Gold)) graphics.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
                }
                Image icon = ExchangeItemIcons.TryGet(item.TemplateId);
                if (icon != null) ClientAssets.DrawFit(graphics, icon, Rectangle.Inflate(bounds, -5, -5));
                else TextRenderer.DrawText(graphics, Initials(item.Name), Font, bounds, BotUi.Ink,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                if (item.Quantity > 1) DrawQuantity(graphics, bounds, item.Quantity);
                if (selectedId == item.Id)
                {
                    if (glow != null) graphics.DrawImage(glow, Rectangle.Inflate(bounds, 1, 1));
                    else using (var pen = new Pen(BotUi.Olive, 2)) graphics.DrawRectangle(pen, Rectangle.Inflate(bounds, -1, -1));
                }
            }
        }

        private void DrawQuantity(Graphics graphics, Rectangle bounds, int quantity)
        {
            string text = quantity.ToString(CultureInfo.InvariantCulture);
            Font font = BotFonts.Get(7, FontStyle.Bold);
            Size size = TextRenderer.MeasureText(graphics, text, font, Size.Empty, TextFormatFlags.NoPadding);
            var badge = new Rectangle(bounds.Right - size.Width - 5, bounds.Bottom - size.Height - 3, size.Width + 4, size.Height + 1);
            using (var fill = new SolidBrush(Color.FromArgb(200, BotUi.Frame))) graphics.FillRectangle(fill, badge);
            TextRenderer.DrawText(graphics, text, font, badge, BotUi.PaperLight,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        private static string Initials(string name)
        {
            string[] words = (name ?? string.Empty).Split(new[] { ' ', '\'', '-' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(word => char.IsLetterOrDigit(word[0])).ToArray();
            if (words.Length == 0) return "?";
            string initials = words.Length == 1 ? words[0].Substring(0, Math.Min(2, words[0].Length)) : words[0].Substring(0, 1) + words[1].Substring(0, 1);
            return initials.ToUpper(CultureInfo.CurrentCulture);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            int index = IndexAt(e.Location);
            SetSelection(index < 0 ? (uint?)null : items[index].Id);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = IndexAt(e.Location);
            if (index == tipIndex) return;
            tipIndex = index;
            tips.SetToolTip(this, index < 0 ? null : items[index].Name + " × " + items[index].Quantity.ToString(CultureInfo.CurrentCulture));
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int next = Math.Max(0, Math.Min(MaxScroll(), scrollRow - Math.Sign(e.Delta)));
            if (next != scrollRow) { scrollRow = next; tipIndex = -1; Invalidate(); }
        }

        protected override bool IsInputKey(Keys keyData) =>
            keyData == Keys.Left || keyData == Keys.Right || keyData == Keys.Up || keyData == Keys.Down || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (items.Count == 0) return;
            int index = selectedId == null ? -1 : items.ToList().FindIndex(item => item.Id == selectedId.Value);
            int step = e.KeyCode == Keys.Left ? -1 : e.KeyCode == Keys.Right ? 1 : e.KeyCode == Keys.Up ? -Columns : e.KeyCode == Keys.Down ? Columns : 0;
            if (step == 0) return;
            index = Math.Max(0, Math.Min(items.Count - 1, index < 0 ? 0 : index + step));
            int row = index / Columns;
            if (row < scrollRow) scrollRow = row;
            else if (row >= scrollRow + VisibleRows) scrollRow = row - VisibleRows + 1;
            SetSelection(items[index].Id);
            e.Handled = true;
        }

        private void OnIconLoaded()
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke((Action)(() => { if (!IsDisposed) Invalidate(); })); }
            catch (InvalidOperationException) { /* Poignée détruite entre-temps. */ }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ExchangeItemIcons.Loaded -= OnIconLoaded;
                tips.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
