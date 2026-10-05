using System;
using System.Collections.Concurrent;
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
    /// Images du client utilisées par les cases d'objets (fond <c>case-inventaire</c>, surbrillance, silhouette du plateau,
    /// croix « deux mains », icône de la monture, symboles de la fiche) lues une seule fois sur le pool de threads : le
    /// dessin n'emploie que <see cref="Cached"/>, qui ne touche jamais le disque, et les contrôles inscrits sont redessinés
    /// quand la lecture aboutit. Sans ces fichiers, chaque contrôle garde son rendu de repli (parchemin et filets).
    /// </summary>
    internal static class ItemAssets
    {
        internal static readonly string[] Names =
        {
            "case-inventaire", "case-surbrillance", "inventaire-silhouette", "inventaire-croix", "UI_InventoryMountIcon",
            "ItemViewerDestroy", "ItemViewerTarget", "ItemViewerTwoHand", "ItemViewerUseHand", "ItemSetViewerItemBorder", "kamas"
        };
        private static readonly ConcurrentDictionary<string, Bitmap> ready = new ConcurrentDictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<WeakReference<Control>> waiting = new List<WeakReference<Control>>();
        private static readonly object sync = new object();
        private static bool loading, loaded;

        /// <summary>Image déjà lue, sinon null (jamais de lecture sur le thread appelant).</summary>
        internal static Bitmap Cached(string name) => name != null && ready.TryGetValue(name, out Bitmap image) ? image : null;

        /// <summary>Vrai quand la lecture de toutes les images est terminée (présentes ou absentes).</summary>
        internal static bool Loaded { get { lock (sync) return loaded; } }

        /// <summary>Lance la lecture en tâche de fond si elle n'a pas eu lieu et redessine <paramref name="owner"/> à la fin.</summary>
        internal static void Ensure(Control owner)
        {
            lock (sync)
            {
                if (loaded) return;
                if (owner != null) waiting.Add(new WeakReference<Control>(owner));
                if (loading) return;
                loading = true;
            }
            Task.Factory.StartNew(() =>
            {
                foreach (string name in Names)
                {
                    try { Bitmap image = ClientAssets.Get(name); if (image != null) ready[name] = image; }
                    catch (Exception) { /* Fichier illisible : repli dessiné par les contrôles. */ }
                }
                List<WeakReference<Control>> owners;
                lock (sync) { loaded = true; loading = false; owners = waiting.ToList(); waiting.Clear(); }
                foreach (WeakReference<Control> reference in owners)
                    if (reference.TryGetTarget(out Control control)) Redraw(control);
            }, System.Threading.CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
        }

        /// <summary>Oublie les images lues (tests : changement de dossier des ressources) ; les contrôles se redessinent à la demande suivante.</summary>
        internal static void Reset() { lock (sync) { loaded = false; } ready.Clear(); }

        internal static void Redraw(Control owner)
        {
            if (owner == null || owner.IsDisposed || !owner.IsHandleCreated) return;
            try { owner.BeginInvoke((Action)(() => { if (!owner.IsDisposed) owner.Invalidate(true); })); }
            catch (InvalidOperationException) { /* Poignée détruite entre-temps. */ }
        }
    }

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
                if (task.Status != TaskStatus.RanToCompletion) return;
                ItemAssets.Redraw(owner);
            }, TaskScheduler.Default);
            return null;
        }

        /// <summary>Vrai quand on sait si l'icône existe (lecture terminée ou modèle inconnu des textes).</summary>
        internal static bool IsResolved(int templateId)
        {
            string key = KeyOf(templateId);
            return key == null || ClientAssets.TryCached("Items", key, out Bitmap ignored);
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
            Image cell = ItemAssets.Cached("case-inventaire");
            if (cell != null) graphics.DrawImage(cell, bounds);
            else
            {
                using (var fill = new SolidBrush(BotUi.PaperLight)) graphics.FillRectangle(fill, bounds);
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
                Image glow = ItemAssets.Cached("case-surbrillance");
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
    public sealed class ItemSlot : Control
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
            ItemAssets.Ensure(this);
        }

        /// <summary>Position d'inventaire (0 à 16, 16 = monture).</summary>
        public int Position { get; }

        /// <summary>Objet porté à cet emplacement, ou null.</summary>
        public InventoryObjects Item
        {
            get { return item; }
            set { item = value; Invalidate(); }
        }

        /// <summary>Retrouve l'objet d'un identifiant d'inventaire glissé (fourni par le volet) pour vérifier son type avant le dépôt.</summary>
        public Func<long, InventoryObjects> Resolve { get; set; }

        public bool Selected { get { return selected; } set { if (selected == value) return; selected = value; Invalidate(); } }
        /// <summary>Vrai pour le bouclier quand l'arme portée est à deux mains (<c>_mcTwoHandedCrossLeft</c>).</summary>
        public bool Crossed { get { return crossed; } set { if (crossed == value) return; crossed = value; Invalidate(); } }

        /// <summary>Clic gauche sur l'emplacement.</summary>
        public event EventHandler Activated;
        /// <summary>Double-clic sur un objet porté (le client le range dans le sac).</summary>
        public event EventHandler DoubleActivated;
        /// <summary>Un objet glissé a été déposé ici (contenu du glisser, emplacement visé).</summary>
        public event Action<ShortcutPayload, ItemSlot> Dropped;
        /// <summary>Clic droit : menu contextuel demandé.</summary>
        public event Action<ItemSlot, Point> MenuRequested;

        /// <summary>Sélectionne l'emplacement par programme (clavier, tests) : lève <see cref="Activated"/> comme un clic gauche.</summary>
        public void Activate() => Activated?.Invoke(this, EventArgs.Empty);

        /// <summary>Vrai si l'objet glissé peut être posé ici d'après son type (le serveur confirme ou refuse ensuite).</summary>
        public bool Accepts(InventoryObjects candidate) => candidate != null && ItemSlots.Accepts(Position, candidate.Type) && (int)candidate.position != Position;

        /// <summary>Dépôt d'un contenu glissé (gestionnaire de glisser-déposer, menus, tests) : lève <see cref="Dropped"/> pour un objet.</summary>
        public void AcceptDrop(ShortcutPayload payload)
        {
            SetHighlight(false);
            if (payload != null && payload.Kind == ShortcutSlotKind.Item && payload.FromPosition != Position) Dropped?.Invoke(payload, this);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            Rectangle bounds = new Rectangle(0, 0, Width, Height);
            ItemCellPainter.Paint(graphics, bounds, item, selected, highlighted, this, Font);
            if (item == null && Position == ItemSlots.MountPosition)
            {
                Image mount = ItemAssets.Cached("UI_InventoryMountIcon");
                if (mount != null) ClientAssets.DrawFit(graphics, mount, Rectangle.Inflate(bounds, -Math.Max(3, Width / 6), -Math.Max(3, Height / 6)));
            }
            if (crossed)
            {
                Image cross = ItemAssets.Cached("inventaire-croix");
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

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            dragArmed = false;
            if (e.Button == MouseButtons.Left && item != null) DoubleActivated?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragArmed || e.Button != MouseButtons.Left || item == null) return;
            Size threshold = SystemInformation.DragSize;
            if (Math.Abs(e.X - dragOrigin.X) < threshold.Width && Math.Abs(e.Y - dragOrigin.Y) < threshold.Height) return;
            dragArmed = false;
            try { DoDragDrop(ItemCellPainter.DragData(item, item.Qua), DragDropEffects.Move); }
            catch (Exception error) when (error is InvalidOperationException || error is System.Runtime.InteropServices.ExternalException) { /* Glisser refusé par le système : rien à envoyer. */ }
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
            AcceptDrop(ItemCellPainter.Payload(e));
        }

        private void Evaluate(DragEventArgs e)
        {
            ShortcutPayload payload = ItemCellPainter.Payload(e);
            bool fits = payload != null && payload.Kind == ShortcutSlotKind.Item && payload.FromPosition != Position;
            if (fits && Resolve != null)
            {
                InventoryObjects candidate = Resolve(payload.Id);
                if (candidate != null && !Accepts(candidate)) fits = false;
            }
            e.Effect = fits ? DragDropEffects.Move : DragDropEffects.None;
            SetHighlight(fits);
        }

        private void SetHighlight(bool value) { if (highlighted == value) return; highlighted = value; Invalidate(); }
    }

    /// <summary>
    /// Plateau d'équipement de la fenêtre <c>Inventory</c> du client : la silhouette (<c>inventaire-silhouette</c>) et les
    /// dix-sept emplacements <c>_ctr0.._ctr16</c> aux positions relatives lues dans <c>core.swf</c> (conteneurs de 50 pixels
    /// à l'échelle 0,5 à 0,8), mis à l'échelle de la largeur disponible. Il ne parle jamais au serveur : il relaie les clics,
    /// menus et dépôts de ses emplacements au volet.
    /// </summary>
    public sealed class EquipmentPlateau : Control
    {
        /// <summary>Position, abscisse, ordonnée et échelle de chaque conteneur dans <c>UI_Inventory</c> (pixels du client).</summary>
        private static readonly float[][] Placements =
        {
            new[] { 0f, 388f, 78f, 0.6f }, new[] { 1f, 450f, 67f, 0.8f }, new[] { 2f, 324f, 118f, 0.6f }, new[] { 3f, 383f, 116f, 0.8f },
            new[] { 4f, 456f, 118f, 0.6f }, new[] { 5f, 383f, 177f, 0.8f }, new[] { 6f, 516f, 67f, 0.7f }, new[] { 7f, 516f, 108f, 0.7f },
            new[] { 8f, 516f, 149f, 0.7f }, new[] { 9f, 270f, 67f, 0.5f }, new[] { 10f, 270f, 94f, 0.5f }, new[] { 11f, 270f, 121f, 0.5f },
            new[] { 12f, 270f, 148f, 0.5f }, new[] { 13f, 270f, 175f, 0.5f }, new[] { 14f, 270f, 202f, 0.5f }, new[] { 15f, 320f, 67f, 0.8f },
            new[] { 16f, 516f, 190f, 0.7f }
        };
        private const float ContainerSize = 50f, OriginX = 268f, OriginY = 61f, ClientWidth = 285f, ClientHeight = 168f;
        private const float SilhouetteX = 329f, SilhouetteY = 63f, SilhouetteWidth = 148.5f, SilhouetteHeight = 159.5f;
        private const float MaxScale = 1.3f;
        private readonly ItemSlot[] slots = new ItemSlot[ItemSlots.MountPosition + 1];
        private uint? selected;

        public EquipmentPlateau()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = BotUi.Paper;
            for (int position = 0; position < slots.Length; position++)
            {
                var slot = new ItemSlot(position);
                slot.Activated += (s, e) => SlotActivated?.Invoke((ItemSlot)s);
                slot.DoubleActivated += (s, e) => SlotDoubleActivated?.Invoke((ItemSlot)s);
                slot.Dropped += (payload, target) => Dropped?.Invoke(payload, target);
                slot.MenuRequested += (target, location) => MenuRequested?.Invoke(target, location);
                slots[position] = slot;
                Controls.Add(slot);
            }
            ItemAssets.Ensure(this);
        }

        /// <summary>Hauteur suivant la largeur quand le plateau est ancré en haut d'un volet.</summary>
        public bool AutoHeight { get; set; } = true;

        public IReadOnlyList<ItemSlot> Slots => slots;
        public ItemSlot this[int position] => position >= 0 && position < slots.Length ? slots[position] : null;

        /// <summary>Retrouve un objet glissé par son identifiant d'inventaire (transmis à chaque emplacement).</summary>
        public Func<long, InventoryObjects> Resolve
        {
            get { return slots[0].Resolve; }
            set { foreach (ItemSlot slot in slots) slot.Resolve = value; }
        }

        /// <summary>Identifiant d'inventaire de l'objet mis en évidence, ou null.</summary>
        public uint? Selected
        {
            get { return selected; }
            set { selected = value; foreach (ItemSlot slot in slots) slot.Selected = value.HasValue && slot.Item != null && slot.Item.Inventory_ID == value.Value; }
        }

        public event Action<ItemSlot> SlotActivated;
        public event Action<ItemSlot> SlotDoubleActivated;
        public event Action<ShortcutPayload, ItemSlot> Dropped;
        public event Action<ItemSlot, Point> MenuRequested;

        /// <summary>Pose les objets portés sur leurs emplacements (position 0 à 16) ; les autres emplacements sont vidés.</summary>
        public void SetItems(IEnumerable<InventoryObjects> equipped)
        {
            var byPosition = new InventoryObjects[slots.Length];
            foreach (InventoryObjects item in equipped ?? Enumerable.Empty<InventoryObjects>())
            {
                int position = (int)item.position;
                if (position >= 0 && position < slots.Length) byPosition[position] = item;
            }
            for (int position = 0; position < slots.Length; position++) slots[position].Item = byPosition[position];
            InventoryObjects weapon = byPosition[1];
            slots[15].Crossed = weapon != null && LangData.Item.IsTwoHanded(weapon.ID);
            Selected = selected;
        }

        /// <summary>Hauteur nécessaire à la largeur donnée (échelle plafonnée à 1,3 fois le client).</summary>
        public int PreferredHeightFor(int width) => (int)Math.Ceiling(ClientHeight * ScaleFor(width)) + 2;

        private static float ScaleFor(int width) => Math.Max(0.5f, Math.Min(MaxScale, width / ClientWidth));

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (AutoHeight && Dock == DockStyle.Top)
            {
                int wanted = PreferredHeightFor(Width);
                if (wanted != Height) { Height = wanted; return; }
            }
            Place();
        }

        private void Place()
        {
            float scale = ScaleFor(Width);
            int left = Math.Max(0, (Width - (int)(ClientWidth * scale)) / 2);
            foreach (float[] entry in Placements)
            {
                ItemSlot slot = slots[(int)entry[0]];
                int size = Math.Max(16, (int)Math.Round(ContainerSize * entry[3] * scale));
                slot.SetBounds(left + (int)Math.Round((entry[1] - OriginX) * scale), (int)Math.Round((entry[2] - OriginY) * scale), size, size);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.Clear(BackColor);
            float scale = ScaleFor(Width);
            int left = Math.Max(0, (Width - (int)(ClientWidth * scale)) / 2);
            var area = new RectangleF(left + (SilhouetteX - OriginX) * scale, (SilhouetteY - OriginY) * scale, SilhouetteWidth * scale, SilhouetteHeight * scale);
            Image silhouette = ItemAssets.Cached("inventaire-silhouette");
            if (silhouette != null)
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(silhouette, area);
            }
            else
            {
                // Sans l'image du client : une ellipse discrète rappelle la place du personnage.
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(BotUi.Gold) { DashStyle = DashStyle.Dot }) graphics.DrawEllipse(pen, RectangleF.Inflate(area, -area.Width / 4, -4));
            }
        }
    }
}
