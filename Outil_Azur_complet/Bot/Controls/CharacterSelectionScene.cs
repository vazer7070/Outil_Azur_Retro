using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Outil_Azur_complet.Bot.Controls
{
    internal sealed class SelectionCharacter
    {
        internal int Id, Gfx;
        internal string Name, Level, Appearance;
    }

    // Coordinates follow UI_ChooseCharacter (symbol 797) from the supplied client.
    internal sealed class CharacterSelectionScene : Control
    {
        private readonly List<SelectionCharacter> entries = new List<SelectionCharacter>();
        private readonly Dictionary<int, Bitmap> portraits = new Dictionary<int, Bitmap>();
        private readonly Bitmap backdrop, podium, selectedPodium, cartouche, unknown;
        private readonly OfficialSelectionPlayButton playButton;
        private int selectedId = -1, page;
        private bool listReceived;
        private const float CanvasWidth = 1490, CanvasHeight = 880;
        private readonly RectangleF[] slotBounds = new RectangleF[5];
        private RectangleF viewport;
        private float scale;
        internal event Action<int> CharacterChosen;
        internal event Action CharacterActivated;
        internal event Action EmptySlotActivated;
        internal Control PlayButton { get { return playButton; } }
        internal int PageCount { get { return Math.Max(1, (entries.Count + 4) / 5); } }
        internal int CurrentPage { get { return page; } }
        internal int[] VisibleCharacterIds { get { return entries.Skip(page * 5).Take(5).Select(x => x.Id).ToArray(); } }

        internal CharacterSelectionScene()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable |
                ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, true);
            Dock = DockStyle.Fill; BackColor = BotUi.Frame; TabStop = true;
            AccessibleName = "Sélection du personnage";
            backdrop = OfficialSelectionAssets.Load("scene"); podium = OfficialSelectionAssets.Load("podium");
            selectedPodium = OfficialSelectionAssets.Load("selected-podium");
            cartouche = OfficialSelectionAssets.Load("cartouche"); unknown = OfficialSelectionAssets.Load("unknown");
            playButton = new OfficialSelectionPlayButton { Text = "Jouer", Enabled = false };
            playButton.Click += (s, e) => CharacterActivated?.Invoke(); Controls.Add(playButton);
        }

        internal void SetCharacters(IEnumerable<SelectionCharacter> characters, bool received, int chosenId)
        {
            entries.Clear(); entries.AddRange(characters); listReceived = received;
            page = Math.Min(page, PageCount - 1); SetSelected(chosenId); Invalidate();
        }
        internal void SetSelected(int id)
        {
            selectedId = id; int index = entries.FindIndex(x => x.Id == id);
            if (index >= 0) page = index / 5;
            Invalidate();
        }
        internal void NextPage() { MovePage(1); }
        internal void PreviousPage() { MovePage(-1); }
        private void MovePage(int direction)
        {
            int next = Math.Max(0, Math.Min(PageCount - 1, page + direction));
            if (next == page) return; page = next;
            if (page * 5 < entries.Count) CharacterChosen?.Invoke(entries[page * 5].Id);
            Invalidate();
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e); UpdateViewport();
        }
        private void UpdateViewport()
        {
            scale = Math.Max(0.01f, Math.Min(Width / CanvasWidth, Height / CanvasHeight));
            viewport = new RectangleF((Width - CanvasWidth * scale) / 2, (Height - CanvasHeight * scale) / 2,
                CanvasWidth * scale, CanvasHeight * scale);
            if (playButton != null) {
                var bounds = ToScreen(new RectangleF(631, 778, 225, 62));
                bounds.Width = Math.Max(130, bounds.Width); bounds.Height = Math.Max(34, bounds.Height);
                bounds.X = (int)(viewport.X + 743.5f * scale) - bounds.Width / 2;
                playButton.Bounds = bounds;
            }
        }
        private Rectangle ToScreen(RectangleF value)
        {
            return Rectangle.Round(new RectangleF(viewport.X + value.X * scale, viewport.Y + value.Y * scale,
                value.Width * scale, value.Height * scale));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); UpdateViewport();
            var graphics = e.Graphics; graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            if (backdrop != null) graphics.DrawImage(backdrop, viewport, new RectangleF(44, 77, 1490, 880), GraphicsUnit.Pixel);
            else {
                using (var paper = new SolidBrush(BotUi.Paper)) graphics.FillRectangle(paper, viewport);
            }
            var state = graphics.Save(); graphics.TranslateTransform(viewport.X, viewport.Y); graphics.ScaleTransform(scale, scale);
            using (var font = new Font("Tahoma", 19, FontStyle.Bold))
            using (var small = new Font("Tahoma", 15))
            using (var ink = new SolidBrush(BotUi.Ink))
            using (var light = new SolidBrush(BotUi.PaperLight))
            using (var muted = new SolidBrush(BotUi.Muted))
            using (var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap }) {
                graphics.DrawString("Choisissez votre personnage", font, light, new RectangleF(450, 313, 930, 48), centered);
                for (int slot = 0; slot < 5; slot++) {
                    float center = 234 + 255 * slot;
                    slotBounds[slot] = new RectangleF(center - 114, 371, 228, 377);
                    int index = page * 5 + slot; var entry = index < entries.Count ? entries[index] : null;
                    DrawAsset(graphics, podium, new RectangleF(center - 110, 561, 220, 126));
                    if (entry != null && entry.Id == selectedId) {
                        DrawAsset(graphics, selectedPodium, new RectangleF(center - 105, 558, 210, 89));
                        using (var pen = new Pen(Color.FromArgb(220, 157, 37), 3))
                            graphics.DrawRectangle(pen, center - 113, 374, 226, 369);
                    }
                    if (entry != null) {
                        Bitmap portrait;
                        if (!portraits.TryGetValue(entry.Gfx, out portrait)) {
                            portrait = SelectPlayerPerso.LoadCharacterPortrait(entry.Gfx); portraits[entry.Gfx] = portrait;
                        }
                        if (portrait != null) DrawFit(graphics, portrait, new RectangleF(center - 103, 385, 206, 247));
                        else using (var multiline = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                            graphics.DrawString("Apparence\nindisponible", small, muted,
                                new RectangleF(center - 105, 438, 210, 128), multiline);
                    } else if (listReceived) DrawFit(graphics, unknown, new RectangleF(center - 38, 468, 76, 108));
                    DrawAsset(graphics, cartouche, new RectangleF(center - 110, 669, 220, 80));
                    DrawShrunk(graphics, entry == null ? (listReceived ? "Emplacement libre" : "En attente…") : entry.Name,
                        font, light, new RectangleF(center - 104, 671, 208, 37), centered);
                    string details = entry == null ? (listReceived ? "Créer un personnage" : "Liste du serveur") :
                        "Niv. " + entry.Level + " · " + entry.Appearance.Split('·')[0].Trim();
                    DrawShrunk(graphics, details, small, light, new RectangleF(center - 103, 708, 206, 34), centered);
                }
                if (PageCount > 1) graphics.DrawString("Page " + (page + 1) + " / " + PageCount,
                    small, light, new RectangleF(970, 798, 300, 36), centered);
                graphics.DrawString("Personnage", small, light, new RectangleF(95, 6, 460, 35), centered);
                graphics.DrawString("Serveur de jeu", small, light, new RectangleF(846, 6, 460, 35), centered);
            }
            graphics.Restore(state);
        }
        /// <summary>Texte d'une ligne réduit jusqu'à tenir dans le cartouche (les noms longs étaient coupés des deux côtés).</summary>
        private static void DrawShrunk(Graphics graphics, string text, Font font, Brush brush, RectangleF bounds, StringFormat format)
        {
            SizeF size = graphics.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic);
            if (size.Width <= bounds.Width - 4 || size.Width <= 0) { graphics.DrawString(text, font, brush, bounds, format); return; }
            using (var smaller = new Font(font.FontFamily, Math.Max(9f, font.Size * (bounds.Width - 4) / size.Width), font.Style))
                graphics.DrawString(text, smaller, brush, bounds, format);
        }
        private static void DrawAsset(Graphics graphics, Image asset, RectangleF bounds)
        {
            if (asset != null) graphics.DrawImage(asset, bounds);
        }
        private static void DrawFit(Graphics graphics, Image asset, RectangleF bounds)
        {
            if (asset == null) return;
            float ratio = Math.Min(bounds.Width / asset.Width, bounds.Height / asset.Height);
            float width = asset.Width * ratio, height = asset.Height * ratio;
            graphics.DrawImage(asset, new RectangleF(bounds.X + (bounds.Width - width) / 2, bounds.Bottom - height, width, height));
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); if (e.Button != MouseButtons.Left) return; Focus();
            var point = new PointF((e.X - viewport.X) / scale, (e.Y - viewport.Y) / scale);
            if (new RectangleF(85, 820, 80, 55).Contains(point)) { PreviousPage(); return; }
            if (new RectangleF(1310, 820, 100, 55).Contains(point)) { NextPage(); return; }
            for (int slot = 0; slot < 5; slot++) if (slotBounds[slot].Contains(point)) {
                int index = page * 5 + slot;
                if (index < entries.Count) CharacterChosen?.Invoke(entries[index].Id);
                else if (listReceived) EmptySlotActivated?.Invoke();
                return;
            }
        }
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e); if (e.Button != MouseButtons.Left) return;
            var point = new PointF((e.X - viewport.X) / scale, (e.Y - viewport.Y) / scale);
            for (int slot = 0; slot < 5; slot++) if (slotBounds[slot].Contains(point) && page * 5 + slot < entries.Count) {
                CharacterActivated?.Invoke(); return;
            }
        }
        protected override bool IsInputKey(Keys keyData)
        {
            var key = keyData & Keys.KeyCode;
            return key == Keys.Left || key == Keys.Right || key == Keys.PageUp || key == Keys.PageDown || base.IsInputKey(keyData);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.PageUp) PreviousPage();
            else if (e.KeyCode == Keys.PageDown) NextPage();
            else if (e.KeyCode == Keys.Enter) CharacterActivated?.Invoke();
            else if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right) {
                int index = entries.FindIndex(x => x.Id == selectedId);
                index = Math.Max(0, Math.Min(entries.Count - 1, index + (e.KeyCode == Keys.Left ? -1 : 1)));
                if (entries.Count > 0) CharacterChosen?.Invoke(entries[index].Id);
            } else return;
            e.Handled = true; e.SuppressKeyPress = true;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) {
                foreach (var asset in new[] { backdrop, podium, selectedPodium, cartouche, unknown }) asset?.Dispose();
                foreach (var portrait in portraits.Values) portrait?.Dispose(); portraits.Clear();
            }
            base.Dispose(disposing);
        }
    }

    internal static class OfficialSelectionAssets
    {
        internal static Bitmap Load(string name)
        {
            string root = Path.GetDirectoryName(typeof(OfficialSelectionAssets).Assembly.Location);
            foreach (string folder in new[] { Path.Combine("Resources", "Bot", "Selection"), Path.Combine("ressources", "Bot", "UI", "Selection") }) {
                string path = Path.Combine(root, folder, name + ".png");
                if (!File.Exists(path)) continue;
                try {
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var source = Image.FromStream(stream)) return new Bitmap(source);
                } catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException ||
                    error is System.Runtime.InteropServices.ExternalException || error is OutOfMemoryException) { }
            }
            return null;
        }
    }

    internal sealed class OfficialSelectionPlayButton : Button
    {
        private readonly Bitmap up = OfficialSelectionAssets.Load("play-up"), down = OfficialSelectionAssets.Load("play-down");
        private bool pressed;
        internal OfficialSelectionPlayButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Font = new Font("Tahoma", 13, FontStyle.Bold); Cursor = Cursors.Hand; AccessibleName = "Jouer";
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BotUi.FrameLight);
            // Coins de la pilule : la scène dessinée dessous, plutôt qu'un rectangle gris opaque autour de « Jouer ».
            if (Parent is CharacterSelectionScene scene)
            {
                GraphicsState state = e.Graphics.Save();
                e.Graphics.TranslateTransform(-Left, -Top);
                InvokePaint(scene, new PaintEventArgs(e.Graphics, Bounds));
                e.Graphics.Restore(state);
            }
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            var asset = pressed && down != null ? down : up;
            if (asset != null) e.Graphics.DrawImage(asset, ClientRectangle);
            else using (var brush = new SolidBrush(Color.FromArgb(208, 150, 28))) e.Graphics.FillRectangle(brush, ClientRectangle);
            if (!Enabled) using (var veil = new SolidBrush(Color.FromArgb(135, BotUi.FrameLight))) e.Graphics.FillRectangle(veil, ClientRectangle);
            using (var brush = new SolidBrush(Enabled ? Color.FromArgb(45, 35, 15) : BotUi.Paper))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                e.Graphics.DrawString(Text, Font, brush, ClientRectangle, format);
            if (Focused && Enabled) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -5, -5));
        }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void Dispose(bool disposing) { if (disposing) { up?.Dispose(); down?.Dispose(); } base.Dispose(disposing); }
    }
}
