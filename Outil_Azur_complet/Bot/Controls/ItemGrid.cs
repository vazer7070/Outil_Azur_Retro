using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Controls.Banner;
using Tool_BotProtocol.Game.Perso.Inventory;

namespace Outil_Azur_complet.Bot.Controls
{
    /// <summary>
    /// Grille du sac de la fenêtre <c>Inventory</c> du client : cases <c>case-inventaire</c> avec icône (ou initiales) et
    /// quantité, sélection par clic ou clavier, molette pour défiler, glisser vers un emplacement d'équipement ou la barre
    /// de raccourcis (même format de glisser que celle-ci), dépôt d'un objet porté pour le ranger dans le sac.
    /// Les objets sont ceux donnés par <see cref="SetItems"/> ; la grille ne parle jamais au serveur.
    /// </summary>
    internal sealed class ItemGrid : Control
    {
        public const int CellSize = 40;
        private const int Gap = 4;
        private readonly ToolTip tips = new ToolTip();
        private IReadOnlyList<InventoryObjects> items = new InventoryObjects[0];
        private uint? selectedId;
        private int scrollRow, tipIndex = -1;
        private bool dropHighlight, dragArmed;
        private Point dragOrigin;
        private string emptyText = string.Empty;

        public ItemGrid()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint | ControlStyles.Selectable, true);
            BackColor = BotUi.PaperLight; ForeColor = BotUi.Ink; Font = BotFonts.Get(8, FontStyle.Bold);
            MinimumSize = new Size(CellSize + 2 * Gap, CellSize + 2 * Gap);
            AllowDrop = true;
        }

        /// <summary>Objets affichés, dans l'ordre des cases.</summary>
        public IReadOnlyList<InventoryObjects> Items => items;
        public InventoryObjects SelectedItem => selectedId == null ? null : items.FirstOrDefault(item => item.Inventory_ID == selectedId.Value);
        /// <summary>Quantité à glisser ou à proposer ; 0 = toute la pile.</summary>
        public int DragQuantity { get; set; }

        public event EventHandler SelectionChanged;
        /// <summary>Double-clic sur un objet (le client utilise l'objet).</summary>
        public event Action<InventoryObjects> ItemActivated;
        /// <summary>Clic droit sur un objet : menu contextuel demandé.</summary>
        public event Action<InventoryObjects, Point> MenuRequested;
        /// <summary>Contenu glissé déposé sur la grille (objet porté ou case de la barre à ranger dans le sac).</summary>
        public event Action<ShortcutPayload> Dropped;

        /// <summary>Texte affiché dans une grille vide.</summary>
        public string EmptyText { get => emptyText; set { emptyText = value ?? string.Empty; if (items.Count == 0) Invalidate(); } }

        /// <summary>Remplace les objets ; la sélection est conservée si l'objet est toujours là.</summary>
        public void SetItems(IEnumerable<InventoryObjects> values)
        {
            items = (values ?? Enumerable.Empty<InventoryObjects>()).Where(item => item != null).ToArray();
            if (selectedId != null && SelectedItem == null) { selectedId = null; SelectionChanged?.Invoke(this, EventArgs.Empty); }
            scrollRow = Math.Max(0, Math.Min(scrollRow, MaxScroll()));
            tipIndex = -1;
            Invalidate();
        }

        /// <summary>Sélectionne l'objet d'identifiant d'inventaire donné (clavier, tests) ; faux s'il n'est pas dans la grille.</summary>
        public bool SelectItem(uint inventoryId)
        {
            if (!items.Any(item => item.Inventory_ID == inventoryId)) return false;
            SetSelection(inventoryId);
            return true;
        }

        public void ClearSelection() => SetSelection(null);

        /// <summary>Vrai quand l'icône du modèle est disponible maintenant (lecture terminée et fichier trouvé).</summary>
        public bool HasIcon(uint inventoryId)
        {
            InventoryObjects item = items.FirstOrDefault(entry => entry.Inventory_ID == inventoryId);
            return item != null && ItemIcons.TryGet(item.ID, this) != null;
        }

        /// <summary>Index de la case d'un objet, -1 s'il n'est pas affiché.</summary>
        public int IndexOf(uint inventoryId)
        {
            for (int index = 0; index < items.Count; index++) if (items[index].Inventory_ID == inventoryId) return index;
            return -1;
        }

        /// <summary>Rectangle d'une case (coordonnées de la grille), même hors de la zone visible.</summary>
        public Rectangle CellBounds(int index)
        {
            int column = index % Columns, row = index / Columns - scrollRow;
            return new Rectangle(Gap + column * (CellSize + Gap), Gap + row * (CellSize + Gap), CellSize, CellSize);
        }

        private void SetSelection(uint? id)
        {
            if (selectedId == id) return;
            selectedId = id;
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        public int Columns => Math.Max(1, (ClientSize.Width - Gap) / (CellSize + Gap));
        private int VisibleRows => Math.Max(1, (ClientSize.Height - Gap) / (CellSize + Gap));
        private int MaxScroll() => Math.Max(0, (items.Count + Columns - 1) / Columns - VisibleRows);

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
            using (var pen = new Pen(dropHighlight ? BotUi.Olive : BotUi.Gold, dropHighlight ? 3 : 1))
                graphics.DrawRectangle(pen, dropHighlight ? Rectangle.Inflate(frame, -1, -1) : frame);
            if (items.Count == 0)
            {
                TextRenderer.DrawText(graphics, emptyText, BotFonts.Get(8), Rectangle.Inflate(ClientRectangle, -6, -6), BotUi.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                return;
            }
            for (int index = scrollRow * Columns; index < items.Count; index++)
            {
                Rectangle bounds = CellBounds(index);
                if (bounds.Top >= ClientSize.Height) break;
                if (!bounds.IntersectsWith(e.ClipRectangle)) continue;
                InventoryObjects item = items[index];
                ItemCellPainter.Paint(graphics, bounds, item, selectedId == item.Inventory_ID, false, this, Font);
            }
            if (MaxScroll() > 0)
            {
                // Témoin de défilement : barre fine à droite, proportionnelle aux lignes visibles.
                int rows = (items.Count + Columns - 1) / Columns;
                int track = ClientSize.Height - 2 * Gap, thumb = Math.Max(12, track * VisibleRows / rows), top = Gap + (track - thumb) * scrollRow / Math.Max(1, MaxScroll());
                using (var fill = new SolidBrush(BotUi.Gold)) graphics.FillRectangle(fill, ClientSize.Width - 4, top, 2, thumb);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            int index = IndexAt(e.Location);
            SetSelection(index < 0 ? (uint?)null : items[index].Inventory_ID);
            dragArmed = e.Button == MouseButtons.Left && index >= 0; dragOrigin = e.Location;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            dragArmed = false;
            if (e.Button != MouseButtons.Right) return;
            int index = IndexAt(e.Location);
            if (index >= 0) MenuRequested?.Invoke(items[index], e.Location);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            dragArmed = false;
            int index = IndexAt(e.Location);
            if (index >= 0 && e.Button == MouseButtons.Left) ItemActivated?.Invoke(items[index]);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = IndexAt(e.Location);
            if (dragArmed && e.Button == MouseButtons.Left && SelectedItem != null)
            {
                Size threshold = SystemInformation.DragSize;
                if (Math.Abs(e.X - dragOrigin.X) >= threshold.Width || Math.Abs(e.Y - dragOrigin.Y) >= threshold.Height)
                {
                    dragArmed = false;
                    InventoryObjects dragged = SelectedItem;
                    DoDragDrop(ItemCellPainter.DragData(dragged, DragQuantity <= 0 ? dragged.Qua : DragQuantity), DragDropEffects.Move);
                    return;
                }
            }
            if (index == tipIndex) return;
            tipIndex = index;
            tips.SetToolTip(this, index < 0 ? null : items[index].Name + " × " + items[index].Qua.ToString(CultureInfo.CurrentCulture));
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
            if (e.KeyCode == Keys.Enter && SelectedItem != null) { ItemActivated?.Invoke(SelectedItem); e.Handled = true; return; }
            int index = selectedId == null ? -1 : IndexOf(selectedId.Value);
            int step = e.KeyCode == Keys.Left ? -1 : e.KeyCode == Keys.Right ? 1 : e.KeyCode == Keys.Up ? -Columns : e.KeyCode == Keys.Down ? Columns : 0;
            if (step == 0) return;
            index = Math.Max(0, Math.Min(items.Count - 1, index < 0 ? 0 : index + step));
            int row = index / Columns;
            if (row < scrollRow) scrollRow = row;
            else if (row >= scrollRow + VisibleRows) scrollRow = row - VisibleRows + 1;
            SetSelection(items[index].Inventory_ID);
            e.Handled = true;
        }

        protected override void OnDragEnter(DragEventArgs e) { base.OnDragEnter(e); Evaluate(e); }
        protected override void OnDragOver(DragEventArgs e) { base.OnDragOver(e); Evaluate(e); }
        protected override void OnDragLeave(EventArgs e) { base.OnDragLeave(e); SetHighlight(false); }

        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            SetHighlight(false);
            ShortcutPayload payload = ItemCellPainter.Payload(e);
            if (payload != null && payload.Kind == ShortcutSlotKind.Item && payload.FromPosition >= 0) Dropped?.Invoke(payload);
        }

        private void Evaluate(DragEventArgs e)
        {
            ShortcutPayload payload = ItemCellPainter.Payload(e);
            // Seul un objet porté (ou posé dans la barre) a un sens à ranger ici ; un objet déjà dans le sac ne bouge pas.
            bool fits = payload != null && payload.Kind == ShortcutSlotKind.Item && payload.FromPosition >= 0;
            e.Effect = fits ? DragDropEffects.Move : DragDropEffects.None;
            SetHighlight(fits);
        }

        private void SetHighlight(bool value) { if (dropHighlight == value) return; dropHighlight = value; Invalidate(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tips.Dispose();
            base.Dispose(disposing);
        }
    }
}
