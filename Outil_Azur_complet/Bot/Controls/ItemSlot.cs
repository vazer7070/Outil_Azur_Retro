using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Controls.Banner;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Perso.Inventory;

namespace Outil_Azur_complet.Bot.Controls
{
    /// <summary>
    /// Icônes d'objets de l'inventaire : <c>ressources/Bot/Items/&lt;type&gt;/&lt;gfx&gt;.png</c> (export du lot D3) lues par
    /// <see cref="ClientAssets"/> hors du thread de l'interface. Type et numéro d'image viennent des textes du client
    /// (lot D4) ; sans eux, ou sans fichier, la case montre le fond <c>case-inventaire</c> et les initiales du nom.
    /// </summary>
    internal static class ItemIcons
    {
        private static readonly object sync = new object();
        private static readonly HashSet<string> pending = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>« type/gfx » du modèle, ou null si les textes du client ne le connaissent pas.</summary>
        internal static string KeyOf(int templateId)
        {
            if (templateId <= 0) return null;
            int? type = LangData.Item.Type(templateId), gfx = LangData.Item.Gfx(templateId);
            if (type == null || gfx == null || type < 0 || gfx < 0) return null;
            return type.Value.ToString(CultureInfo.InvariantCulture) + "/" + gfx.Value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Icône déjà en cache, sinon null : la lecture part alors sur le pool de threads et <paramref name="owner"/>
        /// est redessiné quand elle aboutit. Ne touche jamais le disque depuis le thread appelant.
        /// </summary>
        internal static Bitmap TryGet(int templateId, Control owner)
        {
            string key = KeyOf(templateId);
            if (key == null) return null;
            if (ClientAssets.TryCached("Items", key, out Bitmap cached)) return cached;
            lock (sync) { if (!pending.Add(key)) return null; }
            Task<Bitmap> load = ClientAssets.GetAsync("Items", key);
            load.ContinueWith(task =>
            {
                lock (sync) pending.Remove(key);
                if (task.Status != TaskStatus.RanToCompletion || task.Result == null) return;
                Redraw(owner);
            }, TaskScheduler.Default);
            return null;
        }

        /// <summary>Vrai quand on sait si l'icône existe (lecture terminée ou modèle inconnu des textes).</summary>
        internal static bool IsResolved(int templateId)
        {
            string key = KeyOf(templateId);
            return key == null || ClientAssets.TryCached("Items", key, out Bitmap ignored);
        }

        private static void Redraw(Control owner)
        {
            if (owner == null || owner.IsDisposed || !owner.IsHandleCreated) return;
            try { owner.BeginInvoke((Action)(() => { if (!owner.IsDisposed) owner.Invalidate(); })); }
            catch (InvalidOperationException) { /* Poignée détruite entre-temps. */ }
        }
    }

    /// <summary>
    /// Dessin commun d'une case d'objet (grille du sac et emplacements d'équipement) : fond <c>case-inventaire</c>,
    /// icône ou initiales, quantité en pastille, surbrillance <c>case-surbrillance</c> pour la sélection ou le survol
    /// d'un glisser-déposer. Sans les images du client, la case est un carré parchemin cerné d'un filet doré.
    /// </summary>
    internal static class ItemCellPainter
    {
        internal static void Paint(Graphics graphics, Rectangle bounds, InventoryObjects item, bool selected, bool highlighted, Control owner, Font initialsFont)
        {
            Image cell = ClientAssets.Get("case-inventaire");
            if (cell != null) graphics.DrawImage(cell, bounds);
            else
            {
                using (var fill = new SolidBrush(BotUi.Paper)) graphics.FillRectangle(fill, bounds);
                using (var pen = new Pen(BotUi.Gold)) graphics.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
            }
            if (item != null)
            {
                Image icon = ItemIcons.TryGet(item.ID, owner);
                int inset = Math.Max(3, bounds.Width / 8);
                if (icon != null) ClientAssets.DrawFit(graphics, icon, Rectangle.Inflate(bounds, -inset, -inset));
                else TextRenderer.DrawText(graphics, Initials(item.Name), initialsFont, bounds, BotUi.Ink,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                if (item.Qua > 1) DrawQuantity(graphics, bounds, item.Qua);
            }
            if (selected || highlighted)
            {
                Image glow = ClientAssets.Get("case-surbrillance");
                if (glow != null) graphics.DrawImage(glow, Rectangle.Inflate(bounds, 1, 1));
                else using (var pen = new Pen(highlighted ? BotUi.Gold : BotUi.Olive, 2)) graphics.DrawRectangle(pen, Rectangle.Inflate(bounds, -1, -1));
            }
        }

        internal static void DrawQuantity(Graphics graphics, Rectangle bounds, int quantity)
        {
            string text = quantity.ToString(CultureInfo.InvariantCulture);
            Font font = BotFonts.Get(bounds.Width < 32 ? 6 : 7, FontStyle.Bold);
            Size size = TextRenderer.MeasureText(graphics, text, font, Size.Empty, TextFormatFlags.NoPadding);
            var badge = new Rectangle(bounds.Right - size.Width - 5, bounds.Bottom - size.Height - 3, size.Width + 4, size.Height + 1);
            using (var fill = new SolidBrush(Color.FromArgb(200, BotUi.Frame))) graphics.FillRectangle(fill, badge);
            TextRenderer.DrawText(graphics, text, font, badge, BotUi.PaperLight,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        /// <summary>Repli sans icône : deux lettres du nom (« PF » pour « Potion fictive »).</summary>
        internal static string Initials(string name)
        {
            string[] words = (name ?? string.Empty).Split(new[] { ' ', '\'', '-', '°' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(word => char.IsLetterOrDigit(word[0])).ToArray();
            if (words.Length == 0) return "?";
            string initials = words.Length == 1 ? words[0].Substring(0, Math.Min(2, words[0].Length)) : words[0].Substring(0, 1) + words[1].Substring(0, 1);
            return initials.ToUpper(CultureInfo.CurrentCulture);
        }

        /// <summary>Contenu glissé d'une case : même format que la barre de raccourcis, pour qu'elle l'accepte aussi.</summary>
        internal static DataObject DragData(InventoryObjects item, int quantity)
        {
            var data = new DataObject();
            data.SetData(ShortcutBar.DragFormat, new ShortcutPayload(ShortcutSlotKind.Item, item.Inventory_ID, Math.Max(1, Math.Min(item.Qua, quantity)), (int)item.position));
            return data;
        }

        internal static ShortcutPayload Payload(DragEventArgs e)
        {
            try { return e?.Data?.GetData(ShortcutBar.DragFormat) as ShortcutPayload; }
            catch (Exception) { return null; }
        }
    }

    /// <summary>
    /// Un emplacement d'équipement de la fenêtre <c>Inventory</c> du client (<c>_ctr0.._ctr16</c>) : accepte par glisser-déposer
    /// un objet dont le type convient (<see cref="ItemSlots.Accepts"/>) et se laisse glisser vers le sac. La croix
    /// <c>inventaire-croix</c> couvre le bouclier quand l'arme portée se tient à deux mains ; l'emplacement de la monture
    /// vide montre <c>UI_InventoryMountIcon</c>. Le serveur reste seul juge : l'objet ne bouge qu'à son paquet <c>OM</c>.
    /// </summary>
    internal sealed class ItemSlot : Control
    {
        private InventoryObjects item;
        private bool selected, highlighted, crossed;
        private Point dragOrigin;
        private bool dragArmed;

        public ItemSlot(int position)
        {
            Position = position;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            // Fond transparent : la silhouette du plateau d'équipement se voit sous la case (le parent est redessiné dessous).
            BackColor = Color.Transparent; Font = BotFonts.Get(7, FontStyle.Bold);
            AllowDrop = true; Cursor = Cursors.Hand;
            AccessibleName = ItemSlots.Name(position);
        }

        /// <summary>Position d'inventaire (0 à 16, 16 = monture).</summary>
        public int Position { get; }

        /// <summary>Objet porté à cet emplacement, ou null.</summary>
        public InventoryObjects Item
        {
            get { return item; }
            set { item = value; Invalidate(); }
        }

        public bool Selected { get { return selected; } set { if (selected == value) return; selected = value; Invalidate(); } }
        /// <summary>Vrai pour le bouclier quand l'arme portée est à deux mains (<c>_mcTwoHandedCrossLeft</c>).</summary>
        public bool Crossed { get { return crossed; } set { if (crossed == value) return; crossed = value; Invalidate(); } }

        /// <summary>Clic gauche sur l'emplacement.</summary>
        public event EventHandler Activated;
        /// <summary>Un objet glissé a été déposé ici (contenu du glisser, emplacement visé).</summary>
        public event Action<ShortcutPayload, ItemSlot> Dropped;
        /// <summary>Clic droit : menu contextuel demandé.</summary>
        public event Action<ItemSlot, Point> MenuRequested;

        /// <summary>Vrai si l'objet glissé peut être posé ici d'après son type (le serveur confirme ou refuse ensuite).</summary>
        public bool Accepts(InventoryObjects candidate) => candidate != null && ItemSlots.Accepts(Position, candidate.Type) && (int)candidate.position != Position;

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            Rectangle bounds = new Rectangle(0, 0, Width, Height);
            ItemCellPainter.Paint(graphics, bounds, item, selected, highlighted, this, Font);
            if (item == null && Position == ItemSlots.MountPosition)
            {
                Image mount = ClientAssets.Get("UI_InventoryMountIcon");
                if (mount != null) ClientAssets.DrawFit(graphics, mount, Rectangle.Inflate(bounds, -Math.Max(3, Width / 6), -Math.Max(3, Height / 6)));
            }
            if (crossed)
            {
                Image cross = ClientAssets.Get("inventaire-croix");
                Rectangle area = Rectangle.Inflate(bounds, -Width / 6, -Height / 6);
                if (cross != null) ClientAssets.DrawFit(graphics, cross, area);
                else using (var pen = new Pen(Color.FromArgb(200, 160, 40, 30), 3)) { graphics.DrawLine(pen, area.Left, area.Top, area.Right, area.Bottom); graphics.DrawLine(pen, area.Left, area.Bottom, area.Right, area.Top); }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) { dragArmed = item != null; dragOrigin = e.Location; Activated?.Invoke(this, EventArgs.Empty); }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragArmed || e.Button != MouseButtons.Left || item == null) return;
            Size threshold = SystemInformation.DragSize;
            if (Math.Abs(e.X - dragOrigin.X) < threshold.Width && Math.Abs(e.Y - dragOrigin.Y) < threshold.Height) return;
            dragArmed = false;
            DoDragDrop(ItemCellPainter.DragData(item, item.Qua), DragDropEffects.Move);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            dragArmed = false;
            if (e.Button == MouseButtons.Right && item != null) MenuRequested?.Invoke(this, e.Location);
        }

        protected override void OnDragEnter(DragEventArgs e) { base.OnDragEnter(e); Evaluate(e); }
        protected override void OnDragOver(DragEventArgs e) { base.OnDragOver(e); Evaluate(e); }
        protected override void OnDragLeave(EventArgs e) { base.OnDragLeave(e); SetHighlight(false); }

        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            SetHighlight(false);
            ShortcutPayload payload = ItemCellPainter.Payload(e);
            if (payload != null && payload.Kind == ShortcutSlotKind.Item) Dropped?.Invoke(payload, this);
        }

        private void Evaluate(DragEventArgs e)
        {
            ShortcutPayload payload = ItemCellPainter.Payload(e);
            bool fits = payload != null && payload.Kind == ShortcutSlotKind.Item && payload.FromPosition != Position;
            e.Effect = fits ? DragDropEffects.Move : DragDropEffects.None;
            SetHighlight(fits);
        }

        private void SetHighlight(bool value) { if (highlighted == value) return; highlighted = value; Invalidate(); }
    }
}
