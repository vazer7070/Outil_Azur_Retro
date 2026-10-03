using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using iTalk;

namespace Outil_Azur_complet.Editors
{
    // Reuses the bound controls: validation, pending changes and database guards
    // remain the responsibility of each editor, independently of its layout.
    internal sealed class BoundEditorLayout
    {
        internal readonly Panel Body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 6, 14, 10) };
        internal readonly FlowLayoutPanel Actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, Padding = new Padding(18, 10, 18, 8), WrapContents = false, BackColor = Color.White, AutoScroll = true };
        internal readonly TabControl Tabs = EditorUi.Tabs();
        internal readonly Label Status = EditorUi.Label("Sélectionnez une fiche pour afficher ses options.", 10);
        private readonly Form form;
        private readonly Dictionary<string, TableLayoutPanel> sheets = new Dictionary<string, TableLayoutPanel>();
        private readonly Dictionary<Control,Control> actionViews = new Dictionary<Control,Control>();
        private readonly ToolTip tips = new ToolTip { AutoPopDelay = 18000 };
        internal BoundEditorLayout(Form form, string title, string help, Control search = null, ListBox list = null)
        {
            this.form = form;
            foreach (Control old in form.Controls) old.Visible = false;
            form.SuspendLayout(); EditorUi.PrepareWindow(form,title,new Size(1220,820),new Size(1060,660));
            form.FormBorderStyle = FormBorderStyle.None; form.KeyPreview = true; form.TransparencyKey = Color.Fuchsia;
            var theme = new EditorTheme { Dock = DockStyle.Fill, Text = title, Padding = new Padding(4, 42, 4, 4), Sizable = true, SmartBounds = false, StartPosition = FormStartPosition.CenterParent };
            form.Controls.Add(theme); theme.BringToFront();
            form.Disposed+=(s,e)=>tips.Dispose();
            var header = new Panel { Dock = DockStyle.Top, Height = 100, Padding = new Padding(16, 10, 16, 2) };
            var heading = EditorUi.Label(title, 18, true); heading.Dock = DockStyle.Top; heading.Height = 34;
            var guide = EditorUi.Label(help); guide.Dock = DockStyle.Top; guide.Height = 28;
            Status.Dock = DockStyle.Bottom; Status.Height = 25; Status.ForeColor = EditorUi.Blue;
            header.Controls.Add(guide); header.Controls.Add(heading); header.Controls.Add(Status);
            theme.Controls.Add(Body); theme.Controls.Add(Actions); theme.Controls.Add(header);
            var close = EditorUi.Button("×", false, 34); close.SetBounds(form.Width - 42, 5, 34, 30); close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            close.AccessibleName = "Fermer la fenêtre"; close.Click += (s,e) => form.Close(); theme.Controls.Add(close); close.BringToFront();
            theme.SizeChanged += (s,e) => close.Left = theme.ClientSize.Width - 42;
            close.Left = theme.ClientSize.Width - 42;
            form.KeyDown += (s,e) => { if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; form.Close(); } };
            form.Disposed += (s,e) => tips.Dispose();
            if (list != null)
            {
                var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 290)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 14, 0) };
                var caption = EditorUi.Label("Rechercher par nom ou numéro", 9); caption.Dock = DockStyle.Top; caption.Height = 25;
                StyleList(list); left.Controls.Add(list);
                if (search != null) { search.Visible = true; search.Dock = DockStyle.Top; search.Height = 33; search.Font = form.Font; search.AccessibleName = caption.Text; left.Controls.Add(search); }
                left.Controls.Add(caption); layout.Controls.Add(left, 0, 0); layout.Controls.Add(Tabs, 1, 0); Body.Controls.Add(layout);
            }
            else Body.Controls.Add(Tabs);
            form.ResumeLayout(true);
        }
        internal static void StyleList(ListBox list)
        {
            list.Visible = true; list.Dock = DockStyle.Fill; list.Font = new Font("Segoe UI", 10); list.BackColor = Color.White; list.ForeColor = EditorUi.Ink;
            list.BorderStyle = BorderStyle.None; list.DrawMode = DrawMode.OwnerDrawFixed; list.ItemHeight = 42; list.IntegralHeight = false;
            list.DrawItem += (s,e) => {
                if (e.Index < 0) return;
                bool selected = (e.State & DrawItemState.Selected) != 0;
                using (var brush = new SolidBrush(selected ? Color.FromArgb(226,239,249) : Color.White)) e.Graphics.FillRectangle(brush, e.Bounds);
                TextRenderer.DrawText(e.Graphics, list.GetItemText(list.Items[e.Index]), list.Font, new Rectangle(e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 18, e.Bounds.Height), selected ? EditorUi.Blue : EditorUi.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                e.DrawFocusRectangle();
            };
        }
        internal TableLayoutPanel Sheet(string title, int columns = 2)
        {
            TableLayoutPanel sheet;
            if (!sheets.TryGetValue(title, out sheet)) { sheet = EditorUi.Sheet(columns); EditorUi.Page(Tabs, title, sheet); sheets.Add(title,sheet); }
            return sheet;
        }
        internal void Field(string section, Control bound, string label, string help = "", string[] choices = null, bool multiline = false)
        { EditorUi.AddField(Sheet(section), FieldPanel(bound, label, help, choices, multiline, tips)); }
        internal void ColorField(string section,Control bound,string label)
        {
            var panel=FieldPanel(bound,label,"Choisissez une couleur ; -1 utilise celle du client.",tips:tips);panel.Height=138;
            var edit=EditorUi.Button("Choisir la couleur…",false,180);edit.SetBounds(0,85,180,34);edit.Enabled=bound.Enabled;
            bound.EnabledChanged+=(s,e)=>edit.Enabled=bound.Enabled;
            if(bound is iTalk_TextBox_Small)((iTalk_TextBox_Small)bound).iTalkTB.ReadOnlyChanged+=(s,e)=>edit.Enabled=bound.Enabled&&!((iTalk_TextBox_Small)bound).ReadOnly;
            edit.Click+=(s,e)=>{string value=ColorValueForm.Edit(form,bound.Text);if(value!=null)bound.Text=value;};panel.Controls.Add(edit);EditorUi.AddField(Sheet(section),panel);
        }
        internal static Panel FieldPanel(Control bound, string label, string help, string[] choices = null, bool multiline = false, ToolTip tips = null)
        {
            var panel = new Panel { Width = 320, Height = multiline ? 140 : 102, MinimumSize = new Size(190, multiline ? 140 : 102), BackColor = Color.White };
            var heading = EditorUi.Label(label, 10, true); heading.SetBounds(0, 0, 320, 23); heading.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            var guide = EditorUi.Label(help, 9); guide.SetBounds(0, 23, 320, 24); guide.Anchor = heading.Anchor; guide.ForeColor = Color.FromArgb(105,111,118);
            panel.Controls.Add(heading); panel.Controls.Add(guide);
            bound.Visible = true; bound.Dock = DockStyle.None; bound.Anchor = heading.Anchor; bound.SetBounds(0, 50, 320, multiline ? 76 : 33); bound.Font = new Font("Segoe UI", 10); bound.AccessibleName = label; bound.TabStop = true;
            if (bound is iTalk_TextBox_Small) { var text = (iTalk_TextBox_Small)bound; text.Multiline = multiline; text.iTalkTB.BackColor = Color.White; text.iTalkTB.ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None; }
            panel.Controls.Add(bound);
            if(bound is iTalk_Button_1 || bound is iTalk_Button_2)
            {
                var view=ActionView(bound,label,320);view.SetBounds(0,50,320,34);view.Anchor=heading.Anchor;panel.Controls.Add(view);bound.Visible=false;
            }
            if(bound is ComboBox && choices==null)
            {
                var original=(ComboBox)bound;
                var combo=new EditorComboBox{Font=bound.Font,DropDownStyle=original.DropDownStyle,FormattingEnabled=true,Name=bound.Name+"_liste",AccessibleName=label};
                combo.SetBounds(0,50,320,33);combo.Anchor=heading.Anchor;
                combo.Format+=(s,e)=>e.Value=original.GetItemText(e.ListItem);
                bool syncing=false;
                Action sync=()=>{if(syncing)return;syncing=true;var values=original.Items.Cast<object>().ToArray();if(combo.Items.Count!=values.Length || !combo.Items.Cast<object>().SequenceEqual(values)){combo.Items.Clear();combo.Items.AddRange(values);}combo.SelectedIndex=original.SelectedIndex;combo.Enabled=original.Enabled;if(original.DropDownStyle!=ComboBoxStyle.DropDownList)combo.Text=original.Text;syncing=false;};
                combo.DropDown+=(s,e)=>sync();original.SelectedIndexChanged+=(s,e)=>sync();original.TextChanged+=(s,e)=>sync();original.EnabledChanged+=(s,e)=>sync();original.Invalidated+=(s,e)=>sync();
                combo.SelectedIndexChanged+=(s,e)=>{if(!syncing)original.SelectedItem=combo.SelectedItem;};sync();
                bound.Visible=false;panel.Controls.Add(combo);
            }
            if(bound is iTalk_NumericUpDown)
            {
                var original=(iTalk_NumericUpDown)bound;
                var number=new NumericUpDown{Font=bound.Font,Minimum=original.Minimum,Maximum=original.Maximum,Value=original.Value,ThousandsSeparator=true,Name=bound.Name+"_nombre",AccessibleName=label};
                number.SetBounds(0,50,320,33);number.Anchor=heading.Anchor;
                bool syncing=false;
                Action sync=()=>{if(syncing)return;syncing=true;number.Minimum=original.Minimum;number.Maximum=original.Maximum;number.Value=Math.Max(number.Minimum,Math.Min(number.Maximum,original.Value));number.Enabled=original.Enabled;syncing=false;};
                original.ValuesChanged+=(s,e)=>sync();original.EnabledChanged+=(s,e)=>sync();original.Invalidated+=(s,e)=>sync();
                number.ValueChanged+=(s,e)=>{if(syncing)return;syncing=true;original.Value=(long)number.Value;syncing=false;typeof(Control).GetMethod("OnMouseUp",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(original,new object[]{new MouseEventArgs(MouseButtons.Left,1,0,0,0)});};
                bound.Visible=false;panel.Controls.Add(number);
            }
            if (choices != null)
            {
                var combo = new EditorComboBox { Font = bound.Font, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = label, Name = bound.Name + "_choix" };
                combo.Items.AddRange(choices.Select(value => { int split = value.LastIndexOf('='); return split < 0 ? new EditorOption(value,value) : new EditorOption(value.Substring(0,split), value.Substring(split+1)); }).ToArray()); combo.SetBounds(0,50,320,33); combo.Anchor = heading.Anchor;
                bool syncing = false;
                Action sync = () => { syncing = true; string value = bound.Text; var option = combo.Items.Cast<EditorOption>().FirstOrDefault(item => item.Value == value); if (option == null) { option = new EditorOption(value,value == "" ? "Sélectionner…" : value); combo.Items.Add(option); } combo.SelectedItem = option; combo.Enabled = bound.Enabled && (!(bound is iTalk_TextBox_Small) || !((iTalk_TextBox_Small)bound).ReadOnly); syncing = false; };
                sync(); bound.TextChanged += (s,e) => sync(); bound.EnabledChanged += (s,e) => sync();
                if (bound is iTalk_TextBox_Small) ((iTalk_TextBox_Small)bound).iTalkTB.ReadOnlyChanged += (s,e) => sync();
                combo.SelectedIndexChanged += (s,e) => { if (!syncing && combo.SelectedItem != null) bound.Text = ((EditorOption)combo.SelectedItem).Value; };
                bound.Visible = false; panel.Controls.Add(combo);
            }
            if (tips != null) { tips.SetToolTip(guide, help); tips.SetToolTip(bound, help); }
            return panel;
        }
        internal void Action(Control control, string text, int width = 150)
        {
            var view=ActionView(control,text,width,control is iTalk_Button_2);
            actionViews[control]=view;Actions.Controls.Add(view);
        }
        internal static Control ActionView(Control control,string text,int width,bool primary=false)
        {
            control.Text=text;control.Visible=false;
            var view=EditorUi.Button(text,primary,width);view.Enabled=control.Enabled;
            view.Click+=(s,e)=>typeof(Control).GetMethod("OnClick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(control,new object[]{EventArgs.Empty});
            control.TextChanged+=(s,e)=>{view.Text=control.Text.Replace(" le compte du personnage"," le compte");view.AccessibleName=control.Text;};
            control.EnabledChanged+=(s,e)=>view.Enabled=control.Enabled;
            return view;
        }
        internal void ShowAction(Control control,bool visible) { Control view;if(actionViews.TryGetValue(control,out view))view.Visible=visible; }
        internal void Action(string text, Action action, bool primary = false, int width = 150)
        {
            var button = EditorUi.Button(text, primary, width); button.Click += (s,e) => { try { action(); } catch (Exception ex) { MessageBox.Show(form,ex.Message,"Action impossible",MessageBoxButtons.OK,MessageBoxIcon.Warning); } }; Actions.Controls.Add(button);
        }
        internal void Notice(string section, string title, string help, string action, Action run)
        {
            var sheet = Sheet(section, 1);
            var panel = new Panel { Height = 155,Padding=new Padding(16),BackColor=Color.White };
            panel.Paint+=(s,e)=>{using(var border=new Pen(Color.FromArgb(224,230,237)))e.Graphics.DrawRectangle(border,0,0,panel.Width-1,panel.Height-1);};
            var heading = EditorUi.Label(title,13,true); heading.Dock = DockStyle.Top; heading.Height = 30;
            var guide = EditorUi.Label(help); guide.Dock = DockStyle.Top; guide.Height = 50;guide.ForeColor=Color.FromArgb(100,113,131);
            var actions=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=36};
            var button = EditorUi.Button(action, false, action=="Ouvrir"?110:275);button.Click += (s,e) => {try{run();}catch(Exception error){MessageBox.Show(form,error.Message,"Action impossible",MessageBoxButtons.OK,MessageBoxIcon.Warning);}};actions.Controls.Add(button);
            panel.Controls.Add(guide); panel.Controls.Add(heading); panel.Controls.Add(actions); EditorUi.AddField(sheet,panel);
        }
        internal void MirrorStatus(Control source)
        { source.TextChanged += (s,e) => Status.Text = source.Text; Status.Text = source.Text; }
    }
}
