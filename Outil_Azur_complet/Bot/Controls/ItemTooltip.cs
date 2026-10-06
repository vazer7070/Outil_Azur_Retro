using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Controls.Chat;
using Tool_BotProtocol.Game.Perso.Inventory;

namespace Outil_Azur_complet.Bot.Controls
{
    /// <summary>
    /// Fiche d'objet du client (<c>ItemViewer</c>, 323 × 214) rendue sur parchemin : nom et niveau, catégorie et panoplie,
    /// description, effets, conditions, caractéristiques d'arme, poids et prix, symboles « deux mains », « utilisable »,
    /// « ciblable » et « destructible » exportés du client. Les textes viennent d'<see cref="ItemSheet"/> (lot F13b) :
    /// le contrôle ne lit ni fichier ni réseau. Sa hauteur suit son contenu (<see cref="PreferredHeightFor"/>).
    /// </summary>
    public sealed class ItemTooltip : Control
    {
        private const int Inset = 8, SymbolSize = 24, SymbolStep = 26;
        private readonly List<Line> lines = new List<Line>();
        private ItemSheet sheet;
        private ItemSetSheet set;

        private enum Style { Title, Muted, Body, Header, Effect, Condition }
        private struct Line { public string Text; public Style Style; }

        public ItemTooltip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = BotUi.Paper; ForeColor = BotUi.Ink; Font = BotFonts.Get(9);
            MinimumSize = new Size(120, 40);
            Height = 40;
            ItemAssets.Ensure(this);
        }

        /// <summary>Fiche affichée, ou null (contrôle vide avec <see cref="EmptyText"/>).</summary>
        public ItemSheet Sheet => sheet;
        /// <summary>Panoplie portée affichée sous la fiche, ou null.</summary>
        public ItemSetSheet Set => set;
        /// <summary>Texte affiché quand aucune fiche n'est posée.</summary>
        public string EmptyText { get; set; } = "Sélectionnez un objet pour voir sa fiche.";
        /// <summary>Vrai quand la hauteur du contrôle suit son contenu (fiche posée dans un volet) ; faux dans une infobulle dimensionnée par l'appelant.</summary>
        public bool AutoHeight { get; set; } = true;

        /// <summary>Lignes de texte dans l'ordre d'affichage (titres de section inclus).</summary>
        public IReadOnlyList<string> Lines => lines.Select(line => line.Text).ToArray();

        /// <summary>Pose une fiche et, s'il y a lieu, la panoplie portée qui la concerne ; null vide le contrôle.</summary>
        public void Show(ItemSheet value, ItemSetSheet wornSet = null)
        {
            sheet = value; set = value == null ? null : wornSet;
            Rebuild();
        }

        public void Clear() => Show(null);

        private void Rebuild()
        {
            lines.Clear();
            if (sheet != null)
            {
                string title = sheet.Name + " (" + sheet.LevelText + ")" + (sheet.Quantity > 1 ? " × " + sheet.Quantity.ToString(CultureInfo.InvariantCulture) : string.Empty);
                Add(title, Style.Title);
                string category = sheet.TypeName.Length > 0 ? sheet.TypeName : Lang("ITEM_TYPE", "Catégorie") + " " + sheet.Type.ToString(CultureInfo.InvariantCulture);
                if (sheet.SetName != null) category += " · " + sheet.SetName;
                if (sheet.IsEquipped) category += " · " + ItemSlots.Name(sheet.Position);
                Add(category, Style.Muted);
                if (sheet.Description.Length > 0) Add(sheet.Description, Style.Body);
                Add(Lang("EFFECTS", "Effets"), Style.Header);
                if (sheet.Effects.Count == 0) Add(Lang("NO_EFFECTS", "Aucun effet"), Style.Muted);
                foreach (string effect in sheet.Effects) Add(effect, Style.Effect);
                if (sheet.Conditions.Length > 0 && sheet.ConditionLines.Count > 0)
                {
                    Add(Lang("CONDITIONS", "Conditions"), Style.Header);
                    foreach (string condition in sheet.ConditionLines) Add(condition, Style.Condition);
                }
                if (sheet.Characteristics.Count > 0)
                {
                    Add(Lang("CHARACTERISTICS", "Caractéristiques"), Style.Header);
                    foreach (string line in sheet.Characteristics) Add(line, Style.Body);
                }
                var footer = new List<string>();
                if (sheet.WeightText.Length > 0) footer.Add(sheet.WeightText);
                if (sheet.Price.HasValue) footer.Add(sheet.Price.Value.ToString("#,0", Spaced) + " " + Lang("KAMAS", "Kamas").ToLowerInvariant());
                if (sheet.TwoHanded) footer.Add(Lang("TWO_HANDS_WEAPON", "Arme à deux mains"));
                if (sheet.Cursed) footer.Add("Objet maudit");
                if (sheet.Ethereal) footer.Add("Objet éthéré");
                if (footer.Count > 0) Add(string.Join(" · ", footer), Style.Muted);
                if (set != null)
                {
                    Add(set.Name + " — " + Lang("ITEMSET_EQUIPED_ITEMS", "Objets équipés") + " : " + set.EquippedCount.ToString(CultureInfo.InvariantCulture)
                        + " / " + set.Items.Count.ToString(CultureInfo.InvariantCulture), Style.Header);
                    foreach (KeyValuePair<int, bool> entry in set.Items)
                        Add((entry.Value ? "● " : "○ ") + ItemEffects.ItemName(entry.Key), entry.Value ? Style.Body : Style.Muted);
                    if (set.Effects.Count > 0)
                    {
                        Add(Lang("ITEMSET_EFFECTS", "Effets actuels"), Style.Header);
                        foreach (string effect in set.Effects) Add(effect, Style.Effect);
                    }
                }
            }
            if (AutoHeight && IsHandleCreated) Height = PreferredHeightFor(Width);
            Invalidate();
        }

        private void Add(string text, Style style) { if (!string.IsNullOrWhiteSpace(text)) lines.Add(new Line { Text = text.Trim(), Style = style }); }

        private static readonly NumberFormatInfo Spaced = new NumberFormatInfo { NumberGroupSeparator = " ", NumberGroupSizes = new[] { 3 } };
        private static string Lang(string key, string fallback) => ChatUiText.Get(key, fallback);

        private static Font FontOf(Style style)
        {
            switch (style)
            {
                case Style.Title: return BotFonts.Get(10, FontStyle.Bold);
                case Style.Header: return BotFonts.Get(8, FontStyle.Bold);
                case Style.Muted: return BotFonts.Get(8, FontStyle.Italic);
                default: return BotFonts.Get(9);
            }
        }

        private static Color ColorOf(Style style)
        {
            switch (style)
            {
                case Style.Muted: case Style.Header: return BotUi.Muted;
                case Style.Effect: return BotUi.Olive;
                case Style.Condition: return Color.FromArgb(140, 70, 30);
                default: return BotUi.Ink;
            }
        }

        private int SymbolCount() => sheet == null ? 0 : (sheet.TwoHanded ? 1 : 0) + (sheet.Usable ? 1 : 0) + (sheet.Targetable ? 1 : 0) + (sheet.CanDestroy ? 1 : 0);

        /// <summary>Hauteur nécessaire pour afficher toute la fiche à la largeur donnée.</summary>
        public int PreferredHeightFor(int width)
        {
            if (lines.Count == 0) return Math.Max(MinimumSize.Height, 40);
            int total = Inset, textWidth = Math.Max(40, width - 2 * Inset);
            using (Graphics graphics = CreateGraphics())
            {
                for (int i = 0; i < lines.Count; i++)
                {
                    int available = i == 0 ? Math.Max(40, textWidth - SymbolCount() * SymbolStep) : textWidth;
                    total += MeasureLine(graphics, lines[i], available) + (lines[i].Style == Style.Header && i > 0 ? 4 : 1);
                }
            }
            return total + Inset;
        }

        private static int MeasureLine(Graphics graphics, Line line, int width) =>
            TextRenderer.MeasureText(graphics, line.Text, FontOf(line.Style), new Size(width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (AutoHeight && lines.Count > 0) Height = PreferredHeightFor(Width);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (AutoHeight && lines.Count > 0 && IsHandleCreated)
            {
                int wanted = PreferredHeightFor(Width);
                if (wanted != Height) Height = wanted;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.Clear(BackColor);
            Rectangle frame = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var pen = new Pen(BotUi.Gold)) graphics.DrawRectangle(pen, frame);
            if (lines.Count == 0)
            {
                TextRenderer.DrawText(graphics, EmptyText, BotFonts.Get(8, FontStyle.Italic), Rectangle.Inflate(ClientRectangle, -Inset, -Inset), BotUi.Muted,
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
                return;
            }
            int textWidth = Math.Max(40, Width - 2 * Inset), y = Inset;
            int symbols = DrawSymbols(graphics);
            for (int i = 0; i < lines.Count; i++)
            {
                Line line = lines[i];
                int available = i == 0 ? Math.Max(40, textWidth - symbols) : textWidth;
                int height = MeasureLine(graphics, line, available);
                if (line.Style == Style.Header && i > 0)
                {
                    using (var pen = new Pen(BotUi.Gold)) graphics.DrawLine(pen, Inset, y + 1, Width - Inset, y + 1);
                    y += 3;
                }
                TextRenderer.DrawText(graphics, line.Text, FontOf(line.Style), new Rectangle(Inset, y, available, height), ColorOf(line.Style),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                y += height + 1;
            }
        }

        /// <summary>Symboles du client en haut à droite ; renvoie la largeur occupée.</summary>
        private int DrawSymbols(Graphics graphics)
        {
            if (sheet == null) return 0;
            var names = new List<string>();
            if (sheet.TwoHanded) names.Add("ItemViewerTwoHand");
            if (sheet.Usable) names.Add("ItemViewerUseHand");
            if (sheet.Targetable) names.Add("ItemViewerTarget");
            if (sheet.CanDestroy) names.Add("ItemViewerDestroy");
            int x = Width - Inset;
            foreach (string name in names)
            {
                // Images lues en tâche de fond (ItemAssets) : jamais de lecture sur le fil de l'interface.
                Bitmap image = ItemAssets.Cached(name);
                x -= SymbolStep;
                var box = new Rectangle(x, Inset - 2, SymbolSize, SymbolSize);
                if (image != null) ClientAssets.DrawFit(graphics, image, box);
                else
                {
                    using (var pen = new Pen(BotUi.Gold)) graphics.DrawEllipse(pen, Rectangle.Inflate(box, -3, -3));
                    TextRenderer.DrawText(graphics, name.Substring("ItemViewer".Length, 1), BotFonts.Get(7, FontStyle.Bold), box, BotUi.Muted,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }
            }
            return names.Count * SymbolStep;
        }
    }

    /// <summary>
    /// Infobulle flottante portant un <see cref="ItemTooltip"/> : la boutique et l'échange l'ouvrent au survol d'une ligne,
    /// après un court délai, et la ferment quand la souris quitte la liste. Un seul exemplaire par volet ; ne prend pas le focus.
    /// </summary>
    public sealed class ItemTooltipPopup : IDisposable
    {
        private const int PopupWidth = 300, MaxHeight = 420;
        private readonly ItemTooltip viewer = new ItemTooltip { AutoHeight = false, Width = PopupWidth };
        private readonly ToolStripDropDown drop;
        private readonly ToolStripControlHost host;
        private readonly Timer delay = new Timer { Interval = 350 };
        private Control owner; private Point location; private ItemSheet pendingSheet; private ItemSetSheet pendingSet;
        private object key;

        public ItemTooltipPopup()
        {
            host = new ToolStripControlHost(viewer) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false };
            drop = new ToolStripDropDown { AutoSize = false, Padding = Padding.Empty, Margin = Padding.Empty, DropShadowEnabled = false, AutoClose = false, BackColor = BotUi.Paper };
            drop.Items.Add(host);
            delay.Tick += (s, e) => { delay.Stop(); Open(); };
        }

        /// <summary>Fiche affichée en ce moment (null quand l'infobulle est fermée).</summary>
        public ItemSheet Sheet => drop.Visible ? viewer.Sheet : null;
        /// <summary>Clé de la ligne survolée dont la fiche est affichée ou attendue.</summary>
        public object Key => key;

        /// <summary>Demande l'affichage d'une fiche pour la clé donnée (ligne survolée) ; une même clé ne relance rien.</summary>
        public void Request(Control over, Point screenLocation, object itemKey, Func<ItemSheet> build, Func<ItemSetSheet> buildSet = null)
        {
            if (over == null || itemKey == null) { Hide(); return; }
            if (Equals(key, itemKey) && (drop.Visible || delay.Enabled)) { location = screenLocation; return; }
            key = itemKey; owner = over; location = screenLocation;
            try { pendingSheet = build?.Invoke(); pendingSet = buildSet?.Invoke(); }
            catch (Exception) { pendingSheet = null; pendingSet = null; }
            if (pendingSheet == null) { Hide(); return; }
            if (drop.Visible) Open(); else { delay.Stop(); delay.Start(); }
        }

        public void Hide()
        {
            delay.Stop(); key = null;
            if (drop.Visible) drop.Close();
        }

        private void Open()
        {
            if (owner == null || owner.IsDisposed || !owner.IsHandleCreated || pendingSheet == null) return;
            viewer.Show(pendingSheet, pendingSet);
            int height = Math.Min(MaxHeight, viewer.PreferredHeightFor(PopupWidth));
            viewer.Size = new Size(PopupWidth, height); host.Size = viewer.Size; drop.Size = viewer.Size;
            Point target = new Point(location.X + 16, location.Y + 12);
            Rectangle screen = Screen.FromControl(owner).WorkingArea;
            if (target.X + PopupWidth > screen.Right) target.X = Math.Max(screen.Left, location.X - PopupWidth - 8);
            if (target.Y + height > screen.Bottom) target.Y = Math.Max(screen.Top, screen.Bottom - height);
            try { if (drop.Visible) drop.Location = target; else drop.Show(target); }
            catch (Exception error) when (error is InvalidOperationException || error is ArgumentException) { /* Fenêtre en cours de fermeture. */ }
        }

        public void Dispose()
        {
            delay.Dispose();
            drop.Dispose(); viewer.Dispose();
        }
    }
}
