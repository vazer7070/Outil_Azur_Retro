using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using iTalk;

namespace Outil_Azur_complet.Editors
{
    // Use the same theme and controls as the existing Azur windows.
    public class AzurEditorWindow : Form
    {
        protected readonly Panel Body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 6, 14, 10) };
        protected readonly FlowLayoutPanel Actions = EditorUi.ActionBar();
        protected readonly Label Status = EditorUi.Label("Chargement…", 10);
        protected readonly Label Heading = EditorUi.Title("");
        private readonly iTalk_ThemeContainer theme;

        public AzurEditorWindow(string title, string help, Size size)
        {
            EditorUi.PrepareWindow(this,title,size,new Size(850,600)); KeyPreview = true;
            FormBorderStyle = FormBorderStyle.None; TransparencyKey = Color.Fuchsia;
            theme = new EditorTheme { Dock = DockStyle.Fill, Text = title, Padding = new Padding(4, 42, 4, 4), Sizable = true, SmartBounds = false, StartPosition = FormStartPosition.CenterParent };
            Controls.Add(theme);
            var header = EditorUi.Header(Heading, Status, title, help);
            theme.Controls.Add(Body); theme.Controls.Add(Actions); theme.Controls.Add(header);
            var close = EditorUi.Button("×", false, 34); close.Location = new Point(size.Width - 42, 5); close.Anchor = AnchorStyles.Top | AnchorStyles.Right; close.Height = 30;
            close.AccessibleName = "Fermer la fenêtre"; close.Click += (s, e) => Close(); theme.Controls.Add(close); close.BringToFront();
            theme.SizeChanged += (s,e) => close.Left = theme.ClientSize.Width - 42;
            close.Left = theme.ClientSize.Width - 42;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Close(); } };
        }
        protected Control ActionButton(string text, Action action, bool primary = false, int width = 145)
        {
            var button = EditorUi.Button(text, primary, width);
            button.Click += (s, e) => { try { action(); } catch (Exception error) { ShowError(error); } };
            Actions.Controls.Add(button); return button;
        }
        protected void ShowError(Exception error) { Status.Text = error.Message; Status.ForeColor = EditorUi.Error; MessageBox.Show(this, error.Message, "Action impossible", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        protected void SetStatus(string text) { Status.Text = text; Status.ForeColor = EditorUi.Muted; }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); if (theme != null) theme.Text = Text; }
    }

    internal static class EditorUi
    {
        // Palette du client Dofus Retro (relevée dans core.swf), partagée avec le bot.
        internal static readonly Color Frame = Color.FromArgb(41, 38, 31), FrameLight = Color.FromArgb(81, 74, 60), FrameLine = Color.FromArgb(67, 60, 43);
        internal static readonly Color Background = Color.FromArgb(222, 213, 186), Surface = Color.FromArgb(245, 239, 219), Paper = Color.FromArgb(235, 227, 203);
        internal static readonly Color Border = Color.FromArgb(180, 172, 141), Gold = Color.FromArgb(216, 204, 158), Selection = Color.FromArgb(226, 214, 168);
        internal static readonly Color Ink = Color.FromArgb(50, 48, 35), Muted = Color.FromArgb(108, 100, 74), Accent = Color.FromArgb(106, 118, 67), AccentDark = Color.FromArgb(84, 94, 52);
        internal static readonly Color Error = Color.FromArgb(150, 52, 32);
        // Ancien nom de l'accent, conservé pour les écrans qui l'utilisent encore.
        internal static Color Blue => Accent;
        internal const int FieldInputTop = 26;
        internal static Font TitleFont(float size) => new Font("Georgia", size, FontStyle.Bold);

        internal static Panel Header(Label heading, Label status, string title, string help)
        {
            var header = new Panel { Dock = DockStyle.Top, Height = 66, Padding = new Padding(18, 10, 18, 4) };
            heading.Text = title; heading.Dock = DockStyle.Top; heading.Height = 32;
            status.Dock = DockStyle.Bottom; status.Height = 22; status.ForeColor = Muted;
            header.Controls.Add(heading); header.Controls.Add(status);
            if (!string.IsNullOrWhiteSpace(help))
            {
                var tips = new ToolTip { AutoPopDelay = 15000 };
                tips.SetToolTip(heading, help); header.Disposed += (s, e) => tips.Dispose();
            }
            return header;
        }
        internal static Label Title(string text) { var title = Label(text, 16, true); title.Font = TitleFont(16); return title; }
        internal static FlowLayoutPanel ActionBar()
        {
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, Padding = new Padding(18, 9, 18, 8), WrapContents = false, BackColor = Paper, AutoScroll = true };
            bar.Paint += (s, e) => { using (var line = new Pen(Border)) e.Graphics.DrawLine(line, 0, 0, bar.Width, 0); };
            return bar;
        }
        internal static void PrepareWindow(Form form,string title,Size size,Size minimum)
        {
            // Legacy designers use font scaling. Disable it before replacing the
            // font, otherwise both the font and DPI scaling enlarge the window.
            form.AutoScaleMode = AutoScaleMode.None;
            form.AutoScaleDimensions = new SizeF(96,96);
            form.Font = new Font("Segoe UI",10);
            form.Text = title; form.BackColor = Background; form.ForeColor = Ink;
            form.MinimumSize = minimum;
            var area = Screen.FromControl(form).WorkingArea;
            form.Size = new Size(Math.Max(minimum.Width,Math.Min(size.Width,area.Width)),Math.Max(minimum.Height,Math.Min(size.Height,area.Height)));
            form.AutoScaleMode = AutoScaleMode.Dpi;
        }
        internal static Label Label(string text, float size = 10, bool bold = false)
        { return new Label { Text = text, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = Ink, BackColor = Color.Transparent, AutoEllipsis = true }; }
        internal static Control Button(string text, bool primary = false, int width = 145)
        {
            Control button = new EditorButton(primary);
            button.Text = text; button.Size = new Size(width, 34); button.Font = new Font("Segoe UI", 10); button.Margin = new Padding(0, 0, 8, 0); button.Cursor = Cursors.Hand;
            button.ForeColor = primary ? Surface : Ink;
            button.AccessibleName = text; button.TabStop = true;
            return button;
        }
        internal static iTalk_TextBox_Small TextBox(string text = "", bool multiline = false)
        {
            var box = new iTalk_TextBox_Small { Text = text, Font = new Font("Segoe UI", 10), Multiline = multiline, Height = multiline ? 70 : 33, MaxLength = 200000 };
            box.iTalkTB.ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None; box.iTalkTB.AcceptsReturn = multiline; box.iTalkTB.BackColor = Color.White; box.iTalkTB.ForeColor = Ink; return box;
        }
        internal static ListBox List()
        {
            var list = new ListBox { Dock = DockStyle.Fill };
            StyleList(list); return list;
        }
        /// <summary>Liste à lignes compactes : sélection dorée et filet séparateur.</summary>
        internal static void StyleList(ListBox list)
        {
            list.BorderStyle = BorderStyle.None; list.BackColor = Surface; list.ForeColor = Ink; list.DrawMode = DrawMode.OwnerDrawFixed;
            list.ItemHeight = 32; list.IntegralHeight = false; list.Font = new Font("Segoe UI", 10);
            list.DrawItem += (s, e) => {
                if (e.Index < 0) return;
                bool selected = (e.State & DrawItemState.Selected) != 0;
                using (var brush = new SolidBrush(selected ? Selection : Surface)) e.Graphics.FillRectangle(brush, e.Bounds);
                if (selected) using (var mark = new SolidBrush(Accent)) e.Graphics.FillRectangle(mark, e.Bounds.X, e.Bounds.Y, 3, e.Bounds.Height);
                using (var line = new Pen(Paper)) e.Graphics.DrawLine(line, e.Bounds.X + 8, e.Bounds.Bottom - 1, e.Bounds.Right - 8, e.Bounds.Bottom - 1);
                TextRenderer.DrawText(e.Graphics, list.GetItemText(list.Items[e.Index]), list.Font, new Rectangle(e.Bounds.X + 12, e.Bounds.Y, e.Bounds.Width - 20, e.Bounds.Height), Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            };
        }
        internal static iTalk_TabControl Tabs() { return new EditorTabs { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10), ItemSize = new Size(40, 160) }; }
        internal static TableLayoutPanel Sheet(int columns = 1)
        {
            var sheet = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = columns, Padding = new Padding(20, 16, 20, 8), BackColor = Surface };
            for (int i = 0; i < columns; i++) sheet.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns)); return sheet;
        }
        internal static TabPage Page(TabControl tabs, string title, Control content)
        { var page = new TabPage(title) { BackColor = Surface, AutoScroll = true, Padding = new Padding(8) }; page.Controls.Add(content); tabs.TabPages.Add(page); page.BackColor = Surface; return page; }
        internal static void AddField(TableLayoutPanel sheet, Control field)
        { int index = sheet.Controls.Count; field.Dock = DockStyle.Top; field.Margin = new Padding(0, 0, index % sheet.ColumnCount == 0 && sheet.ColumnCount > 1 ? 18 : 0, 8); sheet.Controls.Add(field, index % sheet.ColumnCount, index / sheet.ColumnCount); sheet.RowCount = (sheet.Controls.Count + sheet.ColumnCount - 1) / sheet.ColumnCount; }
        internal static void Clear(Control parent) { foreach (Control child in parent.Controls.Cast<Control>().ToArray()) child.Dispose(); parent.Controls.Clear(); }
    }

    internal sealed class EditorTheme : iTalk_ThemeContainer
    {
        internal EditorTheme() { BackColor=EditorUi.Background;ForeColor=EditorUi.Ink;Font=new Font("Segoe UI",10); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(EditorUi.Background);
            using(var chrome=new SolidBrush(EditorUi.Frame))e.Graphics.FillRectangle(chrome,0,0,Width,40);
            using(var rule=new Pen(EditorUi.Border))e.Graphics.DrawLine(rule,0,39,Width,39);
            using(var border=new Pen(EditorUi.FrameLine))e.Graphics.DrawRectangle(border,0,0,Width-1,Height-1);
            using(var brand=EditorUi.TitleFont(10))TextRenderer.DrawText(e.Graphics,"Azur",brand,new Rectangle(16,0,52,40),EditorUi.Gold,TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
            using(var font=new Font("Segoe UI",9.5f))TextRenderer.DrawText(e.Graphics,Text,font,new Rectangle(70,0,Math.Max(1,Width-130),40),EditorUi.Paper,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
        }
    }
    internal sealed class EditorButton : Button
    {
        private readonly bool primary;
        internal EditorButton(bool primary)
        {
            this.primary=primary;
            AutoEllipsis=true;
            FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=1;FlatAppearance.BorderColor=primary?EditorUi.AccentDark:EditorUi.Border;
            BackColor=primary?EditorUi.Accent:EditorUi.Surface;UseVisualStyleBackColor=false;
            FlatAppearance.MouseOverBackColor=primary?EditorUi.AccentDark:EditorUi.Selection;
            FlatAppearance.MouseDownBackColor=primary?EditorUi.FrameLight:EditorUi.Gold;
        }
        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            BackColor=Enabled?(primary?EditorUi.Accent:EditorUi.Surface):EditorUi.Paper;
            ForeColor=Enabled?(primary?EditorUi.Surface:EditorUi.Ink):EditorUi.Border;
            FlatAppearance.BorderColor=Enabled&&primary?EditorUi.AccentDark:EditorUi.Border;
        }
    }

    internal sealed class PropertyPages : TabControl
    {
        internal PropertyPages() { SizeMode=TabSizeMode.Fixed;ItemSize=new Size(1,1);Appearance=TabAppearance.FlatButtons; }
        public override Rectangle DisplayRectangle { get { return new Rectangle(0,0,ClientSize.Width,ClientSize.Height); } }
        protected override void WndProc(ref Message message)
        { if(message.Msg==0x1328 && !DesignMode){message.Result=(IntPtr)1;return;}base.WndProc(ref message); }
    }

    internal sealed class EditorTabs : iTalk_TabControl
    {
        private void FitNavigation()
        {
            int rowHeight=Math.Min(40,Math.Max(32,(ClientSize.Height-8)/Math.Max(1,TabCount)));
            if(ItemSize.Width!=rowHeight)ItemSize=new Size(rowHeight,160);
        }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e);FitNavigation(); }
        // Le contrôle iTalk force un gris sur chaque page ajoutée : on remet la surface parchemin.
        protected override void OnControlAdded(ControlEventArgs e) { base.OnControlAdded(e);if(e.Control is TabPage)e.Control.BackColor=EditorUi.Surface;FitNavigation(); }
        protected override void OnControlRemoved(ControlEventArgs e) { base.OnControlRemoved(e);FitNavigation(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            if(Width<=0 || Height<=0)return;
            e.Graphics.Clear(EditorUi.Background);
            using(var background=new SolidBrush(EditorUi.Paper))e.Graphics.FillRectangle(background,0,0,ItemSize.Height,Height);
            using(var rule=new Pen(EditorUi.Border))e.Graphics.DrawLine(rule,ItemSize.Height-1,0,ItemSize.Height-1,Height);
            using(var regular=new Font(Font.FontFamily,Math.Min(Font.Size,10)))
            using(var bold=new Font(Font.FontFamily,Math.Min(Font.Size,10),FontStyle.Bold))
            for(int index=0;index<TabCount;index++)
            {
                var rect=GetTabRect(index);rect.X=0;rect.Width=ItemSize.Height-1;
                bool selected=index==SelectedIndex;
                if(selected)
                {
                    using(var fill=new SolidBrush(EditorUi.Surface))e.Graphics.FillRectangle(fill,rect);
                    using(var mark=new SolidBrush(EditorUi.Accent))e.Graphics.FillRectangle(mark,0,rect.Y,3,rect.Height);
                }
                rect.Inflate(-14,-2);
                TextRenderer.DrawText(e.Graphics,TabPages[index].Text,selected?bold:regular,rect,selected?EditorUi.Ink:EditorUi.Muted,TextFormatFlags.VerticalCenter|TextFormatFlags.WordBreak|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
            }
        }
    }

    internal sealed class EditorComboBox : ComboBox
    {
        internal EditorComboBox() { SetStyle(ControlStyles.Selectable, true); TabStop = true; }
    }

    internal sealed class EditorOption
    {
        internal string Value, Text;
        internal EditorOption(string value, string text) { Value = value; Text = text; }
        public override string ToString() { return Text; }
    }
    internal sealed class FieldSpec
    {
        internal string Label, Help, Section;
        internal bool Multiline;
        internal EditorOption[] Options;
        internal Dictionary<string, string> Suggestions;
        internal Func<IWin32Window, string, string> Edit;
        internal Func<string, string> Summary;
        internal Action<string> Validate;
        internal string DefaultText;
        internal Func<string,string> FormatInput, ParseInput;
        internal FieldSpec(string label, string help = "", string section = "Général", bool multiline = false)
        { Label = label; Help = help; Section = section; Multiline = multiline; }
        internal static FieldSpec Number(string label, string help = "", bool optional = false)
        { return new FieldSpec(label, help) { Validate = value => { int number; if (!(optional && value == "") && !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) throw new FormatException("Saisissez un nombre entier entre -2 147 483 648 et 2 147 483 647."); } }; }
    }

    // A field always retains its exact initial value until the user changes it.
    internal sealed class EditorField : Panel
    {
        internal event EventHandler Changed;
        private readonly DataColumn column;
        private readonly FieldSpec spec;
        private readonly object initial;
        private readonly string initialText;
        private readonly iTalk_TextBox_Small input;
        private readonly ComboBox choice;
        private readonly CheckBox absent;
        private readonly Label error;
        private string structured;
        internal EditorField(DataColumn column, object value, FieldSpec spec, bool readOnly = false)
        {
            this.column = column; this.spec = spec; initial = value; Name = column.ColumnName; AccessibleName = spec.Label;
            initialText = Format(value); structured = initialText;
            const int top = EditorUi.FieldInputTop;
            BackColor = EditorUi.Surface; Width = 620; Height = (spec.Multiline ? 114 : 76); MinimumSize = new Size(220, Height);
            var label = EditorUi.Label(spec.Label, 9.5f, true); label.SetBounds(0, 2, 620, 22); label.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; Controls.Add(label);
            if (spec.Options != null)
            {
                choice = new EditorComboBox { Name = column.ColumnName + "_choix", Font = new Font("Segoe UI", 10), DropDownStyle = ComboBoxStyle.DropDownList, Enabled = !readOnly };
                choice.Items.AddRange(spec.Options);
                var selected = spec.Options.FirstOrDefault(item => item.Value == initialText);
                if (selected == null) {
                    bool existingFlag;
                    var flagOption = bool.TryParse(initialText, out existingFlag) ? spec.Options.FirstOrDefault(item => { bool optionFlag; return bool.TryParse(item.Value, out optionFlag) ? optionFlag == existingFlag : (item.Value == "0" || item.Value == "1") && (item.Value == "1") == existingFlag; }) : null;
                    selected = new EditorOption(initialText, flagOption?.Text ?? "Valeur existante : " + initialText); choice.Items.Add(selected);
                }
                choice.SelectedItem = selected; choice.SetBounds(0, top, 480, 32); choice.Anchor = label.Anchor; Controls.Add(choice);
            }
            else
            {
                input = EditorUi.TextBox(spec.Summary != null ? spec.Summary(initialText) : spec.FormatInput != null ? spec.FormatInput(initialText) : initialText, spec.Multiline);
                input.Name = column.ColumnName + "_valeur"; input.ReadOnly = readOnly || spec.Edit != null;
                input.iTalkTB.BackColor = Color.White;
                input.SetBounds(0, top, 480, spec.Multiline ? 70 : 33); input.Anchor = label.Anchor; Controls.Add(input);
            }
            if (column.AllowDBNull)
            {
                absent = new CheckBox { Text = "Non défini", Checked = value == DBNull.Value, AutoSize = true, Location = new Point(490, top + 7), Anchor = AnchorStyles.Top | AnchorStyles.Right, Enabled = !readOnly, ForeColor = EditorUi.Muted };
                absent.CheckedChanged += (s, e) => { if (input != null) input.Enabled = !absent.Checked; if (choice != null) choice.Enabled = !absent.Checked; };
                Controls.Add(absent); if (input != null) input.Enabled = !absent.Checked; if (choice != null) choice.Enabled = !absent.Checked;
            }
            if (spec.Edit != null)
            {
                var edit = EditorUi.Button("Modifier…", false, 112); edit.SetBounds(490, top - 1, 112, 34); edit.Anchor = AnchorStyles.Top | AnchorStyles.Right; edit.Enabled = !readOnly;
                // Nullable structured values need their own row for the NULL choice.
                if (absent != null) { absent.Anchor = AnchorStyles.Top | AnchorStyles.Left; absent.Location = new Point(0, Height - 8); Height += 26; }
                edit.Click += (s, e) => { try { string result = spec.Edit(FindForm(), structured); if (result != null) { structured = result; input.Text = spec.Summary != null ? spec.Summary(result) : result; if (absent != null) absent.Checked = false; Changed?.Invoke(this, EventArgs.Empty); } } catch (Exception ex) { MessageBox.Show(FindForm(), ex.Message, "Valeur à corriger", MessageBoxButtons.OK, MessageBoxIcon.Warning); } };
                Controls.Add(edit);
            }
            error = EditorUi.Label("", 9); error.ForeColor = EditorUi.Error; error.SetBounds(0, Height - 14, 620, 18); error.Anchor = label.Anchor; Controls.Add(error);
            if (spec.Suggestions != null && spec.Suggestions.Count > 0 && input != null && spec.Edit == null)
            {
                Height += 38;
                var names = new EditorComboBox { Font = new Font("Segoe UI", 10), Name = column.ColumnName + "_noms" };
                names.Items.Add(new EditorOption("", "Choisir dans la liste des noms…"));
                foreach (var pair in spec.Suggestions.OrderBy(pair => pair.Value, StringComparer.CurrentCultureIgnoreCase)) names.Items.Add(new EditorOption(pair.Key, pair.Value + " · #" + pair.Key));
                names.SetBounds(0, top + 35, 620, 30); names.Anchor = label.Anchor; names.Enabled = !readOnly;
                bool syncing = false;
                Action syncName = () => { syncing = true; names.SelectedItem = names.Items.Cast<EditorOption>().FirstOrDefault(item => item.Value == input.Text) ?? names.Items[0]; syncing = false; };
                syncName(); input.TextChanged += (s, e) => syncName();
                names.SelectedIndexChanged += (s, e) => { if (!syncing && names.SelectedIndex > 0) input.Text = ((EditorOption)names.SelectedItem).Value; };
                Controls.Add(names); error.Top = Height - 14;
            }
            var tips = new ToolTip { AutoPopDelay = 15000 }; string hint = string.IsNullOrWhiteSpace(spec.Help) ? spec.Label : spec.Help;
            tips.SetToolTip(label, hint); if (input != null) tips.SetToolTip(input, hint); if (choice != null) tips.SetToolTip(choice, hint); Disposed += (s, e) => tips.Dispose();
            Resize += (s, e) => { int reserve = absent != null || spec.Edit != null ? 132 : 0; if (input != null) input.Width = Math.Max(100, Width - reserve); if (choice != null) choice.Width = Math.Max(100, Width - reserve); };
            if (input != null) input.TextChanged += (s, e) => Changed?.Invoke(this, EventArgs.Empty);
            if (choice != null) choice.SelectedIndexChanged += (s, e) => Changed?.Invoke(this, EventArgs.Empty);
            if (absent != null) absent.CheckedChanged += (s, e) => Changed?.Invoke(this, EventArgs.Empty);
        }
        private static string Format(object value)
        { if (value == null || value == DBNull.Value) return ""; if (value is byte[]) return BitConverter.ToString((byte[])value).Replace("-", ""); if (value is DateTime) return ((DateTime)value).ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture); return Convert.ToString(value, CultureInfo.InvariantCulture); }
        internal object Read()
        {
            error.Text = "";
            if (absent != null && absent.Checked) return DBNull.Value;
            string text = choice != null ? ((EditorOption)choice.SelectedItem).Value : spec.Edit != null ? structured : input.Text;
            try
            {
                if (spec.ParseInput != null && input != null && spec.Edit == null) text = text == spec.FormatInput(initialText) ? initialText : spec.ParseInput(text);
                spec.Validate?.Invoke(text);
                if (text == initialText && initial != DBNull.Value) return initial;
                if (column.DataType == typeof(string)) { if (column.MaxLength >= 0 && text.Length > column.MaxLength) throw new FormatException("Texte trop long (maximum " + column.MaxLength + " caractères)."); return text; }
                if (column.DataType == typeof(byte[])) { if (text.Length % 2 != 0) throw new FormatException("Le code hexadécimal doit contenir des paires de caractères."); return Enumerable.Range(0, text.Length / 2).Select(i => Convert.ToByte(text.Substring(i * 2, 2), 16)).ToArray(); }
                if (column.DataType == typeof(TimeSpan)) return TimeSpan.Parse(text, CultureInfo.InvariantCulture);
                if (column.DataType == typeof(Guid)) return Guid.Parse(text);
                if (column.DataType == typeof(bool) && (text == "0" || text == "1")) return text == "1";
                if (column.DataType == typeof(decimal)) { decimal number; var style = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite; if (decimal.TryParse(text,style,CultureInfo.CurrentCulture,out number) || decimal.TryParse(text,style,CultureInfo.InvariantCulture,out number)) return number; throw new FormatException("Saisissez un nombre décimal valide."); }
                return Convert.ChangeType(text, column.DataType, CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is FormatException || ex is OverflowException || ex is ArgumentException)
            { error.Text = ex is FormatException && spec.Validate != null ? ex.Message : "Saisissez une valeur valide pour ce champ."; throw new FormatException(spec.Label + " : " + error.Text); }
        }
        internal void FocusInput() { if (input != null) input.iTalkTB.Focus(); else if (choice != null) choice.Focus(); }
    }
}
