using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Outil_Azur_complet.Bot
{
    internal static class BotUi
    {
        // Colours read from the supplied Retro client's core.swf.
        internal static readonly Color Frame = Color.FromArgb(41, 38, 31);
        internal static readonly Color FrameLight = Color.FromArgb(81, 74, 60);
        internal static readonly Color Paper = Color.FromArgb(235, 227, 203);
        internal static readonly Color PaperLight = Color.FromArgb(245, 239, 219);
        internal static readonly Color Ink = Color.FromArgb(50, 48, 35);
        internal static readonly Color Muted = Color.FromArgb(108, 100, 74);
        internal static readonly Color Gold = Color.FromArgb(180, 172, 141);
        internal static readonly Color Olive = Color.FromArgb(106, 118, 67);

        internal static void Prepare(Form form, string title, Size? size = null)
        {
            form.Text = title; form.Font = new Font("Tahoma", 10);
            form.BackColor = Frame; form.ForeColor = PaperLight;
            form.Size = size ?? new Size(1000, 700);
            form.MinimumSize = new Size(850, 620);
            form.StartPosition = FormStartPosition.CenterScreen;
            form.AutoScaleMode = AutoScaleMode.None;
        }

        internal static TableLayoutPanel Window(Form form, string title, string description, Size? size = null)
        {
            Prepare(form, title, size);
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
                Padding = new Padding(16), BackColor = Frame };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var header = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            var heading = Label(title.Replace("Bot · ", "Azur · "), 17, true);
            heading.ForeColor = PaperLight; heading.Dock = DockStyle.Top; heading.Height = 29;
            var help = Label(description, 9); help.ForeColor = Gold; help.Dock = DockStyle.Fill;
            header.Controls.Add(help); header.Controls.Add(heading);
            root.Controls.Add(header, 0, 0); form.Controls.Add(root); return root;
        }

        /// <summary>
        /// Bandeau pris dans un écran du client fourni : une image principale (par exemple les œufs de classe de l'écran
        /// de connexion) et, à gauche, une image d'accompagnement (le logo). Renvoie <c>null</c> sans ces fichiers.
        /// </summary>
        internal static Control Banner(string asset, int height, string leftAsset = null)
        {
            var image = ClientAssets.Get(asset); if (image == null) return null;
            // Le fond reprend la couleur du coin de l'image pour que le bandeau se fonde dans le décor du client.
            Color back = image.GetPixel(Math.Min(3, image.Width - 1), image.Height - Math.Min(4, image.Height));
            if (back.A < 250) back = Frame;
            var banner = new Panel { Dock = DockStyle.Bottom, Height = height, BackColor = back, Margin = new Padding(0),
                Padding = new Padding(8, 4, 8, 4), Name = "client-" + asset, AccessibleName = "Décor du client : " + asset };
            banner.Controls.Add(new PictureBox { Dock = DockStyle.Fill, Image = image, SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent, Margin = new Padding(0) });
            var left = leftAsset == null ? null : ClientAssets.Get(leftAsset);
            if (left != null) {
                var side = new PictureBox { Dock = DockStyle.Left, Width = (int)(height * 1.1), Image = left, SizeMode = PictureBoxSizeMode.Zoom,
                    BackColor = Color.Transparent, Margin = new Padding(0), Name = "client-" + leftAsset, AccessibleName = "Décor du client : " + leftAsset };
                banner.Controls.Add(side);
            }
            return banner;
        }

        internal static Label Label(string text, float size = 10, bool bold = false) => new Label {
            Text = text, Font = new Font("Tahoma", size, bold ? FontStyle.Bold : FontStyle.Regular),
            ForeColor = Ink, BackColor = Color.Transparent };

        internal static Panel Card(string title, string help = null)
        {
            var panel = new ClientPanel { Dock = DockStyle.Fill, BackColor = Paper,
                Padding = new Padding(12), Margin = new Padding(0, 0, 10, 0) };
            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 0), BackColor = Paper };
            panel.Tag = body; panel.Controls.Add(body);
            var heading = Label(title, 12, true); heading.Dock = DockStyle.Top; heading.Height = 27;
            if (!string.IsNullOrEmpty(help)) {
                var note = Label(help, 9); note.Dock = DockStyle.Top; note.Height = 38;
                note.ForeColor = Muted; panel.Controls.Add(note);
            }
            panel.Controls.Add(heading); return panel;
        }
        internal static Panel Body(Panel card) => (Panel)card.Tag;
        internal static TextBox Input(string name, bool password = false) => new TextBox {
            Name = name, Font = new Font("Tahoma", 10), BorderStyle = BorderStyle.FixedSingle,
            UseSystemPasswordChar = password, Dock = DockStyle.Top, Height = 29,
            BackColor = PaperLight, ForeColor = Ink, MaxLength = password ? 256 : 200 };
        internal static TableLayoutPanel Fields()
        {
            var sheet = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1,
                Padding = new Padding(0, 4, 0, 0), BackColor = Paper };
            sheet.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); return sheet;
        }
        internal static void Field(TableLayoutPanel sheet, string title, Control control, string help = null)
        {
            var field = new Panel { Dock = DockStyle.Top, Height = help == null ? 63 : 82,
                Margin = new Padding(0, 0, 0, 5), BackColor = Paper };
            var label = Label(title, 9, true); label.Dock = DockStyle.Top; label.Height = 23;
            var value = new Panel { Dock = DockStyle.Top, Height = 32 }; value.Controls.Add(control);
            if (control is ComboBox) { control.BackColor = PaperLight; control.ForeColor = Ink; }
            if (help != null) {
                var note = Label(help, 8); note.Dock = DockStyle.Bottom; note.Height = 24;
                note.ForeColor = Muted; field.Controls.Add(note);
            }
            field.Controls.Add(value); field.Controls.Add(label); sheet.Controls.Add(field, 0, sheet.RowCount++);
        }
        internal static FlowLayoutPanel Actions(params Control[] controls)
        {
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true,
                MinimumSize = new Size(0, 43), WrapContents = true, Padding = new Padding(0, 7, 0, 0) };
            bar.Controls.AddRange(controls); return bar;
        }
        internal static Control Button(string title, EventHandler action, bool primary = false, int width = 150)
        {
            // Pilules du client fourni (orange pour l'action principale, parchemin sinon) ; rendu plat si elles manquent.
            var button = new ClientButton { Text = title, Width = width, Height = 34, Font = new Font("Tahoma", 9, primary ? FontStyle.Bold : FontStyle.Regular),
                Primary = primary, BackColor = primary ? Olive : PaperLight, ForeColor = primary ? Color.White : Ink, Margin = new Padding(0, 0, 7, 0) };
            button.Click += action; return button;
        }
        internal static Label Status(string text)
        {
            var label = Label(text, 9); label.Dock = DockStyle.Bottom; label.Height = 42;
            label.ForeColor = Muted; return label;
        }
        internal static ListView List(params string[] columns)
        {
            var list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
                MultiSelect = false, HideSelection = false, BorderStyle = BorderStyle.None,
                Font = new Font("Tahoma", 10), BackColor = PaperLight, ForeColor = Ink,
                HeaderStyle = ColumnHeaderStyle.Nonclickable };
            foreach (var column in columns) list.Columns.Add(column, 160); return list;
        }
        internal static void OnUi(Control control, Action action)
        {
            if (control.IsDisposed || control.Disposing) return;
            if (control.InvokeRequired) {
                if (control.IsHandleCreated)
                    try { control.BeginInvoke((Action)(() => { if (!control.IsDisposed) action(); })); }
                    catch (InvalidOperationException) { }
            } else action();
        }
        internal static void Append(RichTextBox box, string message, string color = null)
        {
            if (box.TextLength > 100000) { box.Select(0, Math.Min(25000, box.TextLength)); box.SelectedText = ""; }
            box.SelectionStart = box.TextLength; box.SelectionLength = 0;
            try { box.SelectionColor = color == null ? Ink : ColorTranslator.FromHtml("#" + color); }
            catch { box.SelectionColor = Ink; }
            box.AppendText(message + Environment.NewLine); box.ScrollToCaret();
        }
        internal static RichTextBox Journal() => new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true,
            BackColor = PaperLight, ForeColor = Ink, BorderStyle = BorderStyle.None,
            Font = new Font("Tahoma", 9), DetectUrls = false };
    }

    internal sealed class ClientPanel : Panel
    {
        internal ClientPanel() { DoubleBuffered = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); if (Width < 3 || Height < 3) return;
            using (var dark = new Pen(Color.FromArgb(30, 29, 24)))
            using (var light = new Pen(BotUi.Gold)) {
                e.Graphics.DrawRectangle(dark, 0, 0, Width - 1, Height - 1);
                e.Graphics.DrawRectangle(light, 1, 1, Width - 3, Height - 3);
            }
        }
    }

    internal sealed class LifeOrb : Control
    {
        private int current, maximum, level;
        internal LifeOrb() { DoubleBuffered = true; ForeColor = BotUi.PaperLight; }
        internal void UpdateLife(int actual, int max, int characterLevel)
        { current = actual; maximum = max; level = characterLevel; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int size = Math.Min(Width - 8, Height - 27); if (size < 20) return;
            var orb = new Rectangle((Width - size) / 2, 3, size, size);
            using (var background = new LinearGradientBrush(orb, Color.FromArgb(83, 67, 53), Color.FromArgb(25, 24, 21), 90))
                e.Graphics.FillEllipse(background, orb);
            var inside = Rectangle.Inflate(orb, -6, -6);
            double ratio = maximum > 0 ? Math.Max(0, Math.Min(1, (double)current / maximum)) : 0;
            using (var shape = new GraphicsPath()) {
                shape.AddEllipse(inside);
                var saved = e.Graphics.Save(); e.Graphics.SetClip(shape);
                var liquid = new Rectangle(inside.Left, inside.Bottom - (int)(inside.Height * ratio),
                    inside.Width, Math.Max(1, (int)(inside.Height * ratio)));
                using (var red = new LinearGradientBrush(inside, Color.FromArgb(202, 67, 57), Color.FromArgb(95, 19, 20), 90))
                    e.Graphics.FillRectangle(red, liquid);
                e.Graphics.Restore(saved);
            }
            using (var rim = new Pen(BotUi.Gold, 3)) e.Graphics.DrawEllipse(rim, orb);
            using (var font = new Font("Tahoma", 15, FontStyle.Bold))
                TextRenderer.DrawText(e.Graphics, current.ToString(), font, orb, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(e.Graphics, "Niv. " + level + " · " + current + "/" + maximum,
                Font, new Rectangle(0, orb.Bottom + 5, Width, 20), BotUi.PaperLight,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
