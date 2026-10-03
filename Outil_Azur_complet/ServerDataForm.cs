using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Editors;

namespace Outil_Azur_complet
{
    public sealed class ServerDataForm : AzurEditorWindow
    {
        public event EventHandler Saved;
        private readonly ServerResourceKind kind;
        private readonly int? mapId, cellCount;
        private readonly int selectedCell;
        private readonly ListBox list = EditorUi.List();
        private readonly iTalk.iTalk_TextBox_Small search = EditorUi.TextBox();
        private readonly TabControl tabs = EditorUi.Tabs();
        private readonly List<EditorField> fields = new List<EditorField>();
        private readonly Label count = EditorUi.Label("", 9);
        private readonly Panel workspace = new Panel { Dock = DockStyle.Fill };
        private ServerDataSnapshot snapshot;
        private DataRow current;
        private bool busy, selecting;
        private readonly string title;
        public ServerDataForm(ServerResourceKind kind, string title, int? mapId = null, int? cellCount = null, int selectedCell = 0)
            : base(title, "Recherchez une fiche, modifiez ses options, puis enregistrez. Les changements restent en attente jusqu'à l'enregistrement.", new Size(1220, 820))
        {
            this.kind = kind; this.mapId = mapId; this.cellCount = cellCount; this.selectedCell = selectedCell; this.title = title;
            MinimumSize = new Size(1060, 660);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 290)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 14, 0) };
            var label = EditorUi.Label("Rechercher par nom ou numéro", 9); label.Dock = DockStyle.Top; label.Height = 24;
            search.Dock = DockStyle.Top; search.AccessibleName = label.Text;
            count.Dock = DockStyle.Bottom; count.Height = 25;
            left.Controls.Add(list); left.Controls.Add(count); left.Controls.Add(search); left.Controls.Add(label);
            layout.Controls.Add(left, 0, 0); layout.Controls.Add(tabs, 1, 0); workspace.Controls.Add(layout); Body.Controls.Add(workspace);
            ActionButton("Enregistrer en base", async () => await SaveAsync(), true, 172);
            ActionButton("Ajouter une fiche", Add, width: 152); ActionButton("Retirer la fiche", Remove, width: 140);
            ActionButton("Exporter SQL", ExportSql, width: 130);
            ActionButton("Recharger", async () => { if (CanDiscard()) await LoadAsync(); }, width: 120);
            ActionButton("Fermer", Close, width: 105);
            var storage=EditorUi.Button("Activer l'enregistrement…",false,250);storage.Dock=DockStyle.Bottom;storage.Height=34;storage.Click+=(s,e)=>{if(snapshot!=null)using(var dialog=new StoragePreparationForm(snapshot))dialog.ShowDialog(this);};left.Controls.Add(storage);
            list.SelectedIndexChanged += (s, e) => ChangeSelection();
            search.TextChanged += (s, e) => { if (!selecting && CommitCurrent()) RefreshList(current); };
            KeyDown += async (s, e) => { if (e.Control && e.KeyCode == Keys.S) { e.SuppressKeyPress = true; await SaveAsync(); } };
            Shown += async (s, e) => await LoadAsync();
            Actions.Enabled = false;
        }
        private async Task LoadAsync()
        {
            SetBusy(true); SetStatus("Chargement des fiches…");
            try { snapshot = await Task.Run(() => ServerDataService.Load(kind, mapId, cellCount)); current = null; RefreshList(); SetStatus(snapshot.ReferenceNotice ?? (mapId.HasValue ? "Carte " + mapId + " · nouvelle fiche sur la cellule " + selectedCell + "." : "Choisissez une fiche à gauche. Les options sont regroupées à droite.")); }
            catch (Exception error) { snapshot = null; ShowError(error); }
            finally { SetBusy(false); }
        }
        private void SetBusy(bool value) { busy = value; Actions.Enabled = !value; workspace.Enabled = !value && snapshot != null; foreach (Control action in Actions.Controls) action.Enabled = !value && (snapshot != null || action.Text == "Fermer" || action.Text == "Recharger"); }
        private sealed class RowItem
        {
            internal DataRow Row;
            internal string Text;
            public override string ToString() { return Text; }
        }
        private string Describe(DataRow row)
        {
            string name = "";
            foreach (string column in new[] { "nom", "name", "Name IO" }) if (row.Table.Columns.Contains(column)) { name = Convert.ToString(row[column]); break; }
            string prefix = row.RowState == DataRowState.Added ? "+ " : row.RowState == DataRowState.Modified ? "• " : "";
            if(kind==ServerResourceKind.ObjectActions)return prefix+"Objet #"+row["template"]+" · action "+row["type"];
            if(kind==ServerResourceKind.InteractiveDoors)return prefix+"Carte "+row["maps"]+" · bouton "+row["button"];
            if (row.Table.Columns.Contains("id")) return prefix + (name == "" ? title : name) + " · #" + row["id"];
            if (kind == ServerResourceKind.Npcs) return prefix + "PNJ #" + row["npcid"] + " · cellule " + row["cellid"];
            if (row.Table.Columns.Contains("cellid")) return prefix + "Cellule " + row["cellid"] + (mapId.HasValue ? "" : " · carte " + row[snapshot.MapColumn]);
            if (snapshot.MapColumn != null) return prefix + "Carte " + row[snapshot.MapColumn];
            return prefix + string.Join(" · ", snapshot.Keys.Select(key => Convert.ToString(row[key])));
        }
        private void RefreshList(DataRow prefer = null)
        {
            if (snapshot == null) return;
            selecting = true; list.BeginUpdate(); list.Items.Clear();
            string filter = search.Text.Trim();
            foreach (DataRow row in snapshot.Data.Rows)
            {
                if (row.RowState == DataRowState.Deleted) continue;
                string text = Describe(row);
                if (filter == "" || text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) list.Items.Add(new RowItem { Row = row, Text = text });
            }
            int index = -1;
            for (int i = 0; i < list.Items.Count; i++) if (((RowItem)list.Items[i]).Row == prefer) { index = i; break; }
            if (index < 0 && list.Items.Count > 0) index = 0;
            list.SelectedIndex = index; list.EndUpdate(); selecting = false;
            int pending = snapshot.Data.Rows.Cast<DataRow>().Count(row => row.RowState != DataRowState.Unchanged);
            count.Text = list.Items.Count + " fiche(s) affichée(s) · " + pending + " en attente";
            ShowRow(index < 0 ? null : ((RowItem)list.Items[index]).Row);
        }
        private void ChangeSelection()
        {
            if (selecting) return;
            var next = list.SelectedItem as RowItem;
            if (!CommitCurrent()) { selecting = true; list.SelectedItem = list.Items.Cast<RowItem>().FirstOrDefault(item => item.Row == current); selecting = false; return; }
            if (current != null) { var previous = list.Items.Cast<RowItem>().FirstOrDefault(item => item.Row == current); if (previous != null) previous.Text = Describe(current); list.Invalidate(); }
            ShowRow(next?.Row);
        }
        private void ShowRow(DataRow row)
        {
            string section=tabs.SelectedTab?.Text;
            current = row; fields.Clear(); EditorUi.Clear(tabs);
            Heading.Text = row == null ? title : Describe(row).TrimStart('+', '•', ' ');
            if (row == null) { var sheet = EditorUi.Sheet(); var empty = EditorUi.Label(list.Items.Count == 0 && search.Text != "" ? "Aucune fiche ne correspond à votre recherche." : "Aucune fiche. Cliquez sur Ajouter une fiche pour commencer."); empty.Height = 55; EditorUi.AddField(sheet, empty); EditorUi.Page(tabs, "Fiche", sheet); return; }
            var groups = row.Table.Columns.Cast<DataColumn>().Select(column => new { Column = column, Spec = ResourceFieldCatalog.Get(kind, column, cellCount, snapshot.ReferenceNames) }).GroupBy(item => item.Spec.Section).OrderBy(group => group.Key == "Général" ? 0 : group.Key == "Avancé" ? 2 : 1);
            foreach (var group in groups)
            {
                var sheet = EditorUi.Sheet(group.Count()>=4?2:1); EditorUi.Page(tabs, group.Key, sheet);
                foreach (var item in group)
                {
                    Dictionary<string,string> choices;
                    if(snapshot.ReferenceLists.TryGetValue(item.Column.ColumnName,out choices) && item.Spec.Edit==null)item.Spec.Suggestions=choices;
                    bool fixedMap = mapId.HasValue && item.Column.ColumnName == snapshot.MapColumn;
                    bool fixedId = row.RowState != DataRowState.Added && item.Column.ColumnName.Equals("id",StringComparison.OrdinalIgnoreCase);
                    var field = new EditorField(item.Column, row[item.Column], item.Spec, item.Column.ReadOnly || fixedMap || fixedId); fields.Add(field); EditorUi.AddField(sheet, field);
                    field.Changed += (s, e) => SetStatus("Valeurs modifiées dans la fiche. Enregistrez pour les appliquer.");
                }
            }
            var selectedPage=tabs.TabPages.Cast<TabPage>().FirstOrDefault(page=>page.Text==section);
            if(selectedPage!=null)tabs.SelectedTab=selectedPage;
        }
        private bool CommitCurrent()
        {
            if (current == null || current.RowState == DataRowState.Deleted || snapshot == null) return true;
            try
            {
                var values = fields.ToDictionary(field => field.Name, field => field.Read());
                var changed = values.Where(pair => {
                    object old = current[pair.Key], value = pair.Value;
                    return !current.Table.Columns[pair.Key].ReadOnly && !(old is byte[] && value is byte[] ? ((byte[])old).SequenceEqual((byte[])value) : Equals(old, value));
                }).ToArray();
                if (changed.Length == 0) { ClearValidationMessage(); return true; }
                current.BeginEdit();
                foreach (var pair in changed) {
                    if (current.Table.Columns[pair.Key].ReadOnly) continue;
                    object old = current[pair.Key], value = pair.Value;
                    bool same = old is byte[] && value is byte[] ? ((byte[])old).SequenceEqual((byte[])value) : Equals(old, value);
                    if (!same) current[pair.Key] = value;
                }
                current.EndEdit(); count.Text = list.Items.Count + " fiche(s) affichée(s) · " + snapshot.Data.Rows.Cast<DataRow>().Count(row => row.RowState != DataRowState.Unchanged) + " en attente"; ClearValidationMessage(); return true;
            }
            catch (Exception error) { current.CancelEdit(); Status.Text = error.Message; Status.ForeColor = Color.Firebrick; return false; }
        }
        private void ClearValidationMessage() { if (Status.ForeColor == Color.Firebrick) SetStatus("Saisie corrigée. Enregistrez pour appliquer les changements en attente."); }
        private void Add()
        {
            if (snapshot == null || !CommitCurrent()) return;
            var row = ServerDataService.Add(snapshot, selectedCell); selecting = true; search.Text = ""; selecting = false; RefreshList(row); SetStatus("Nouvelle fiche : complétez ses options, puis enregistrez.");
        }
        private void Remove()
        {
            if (current == null) return;
            if (MessageBox.Show(this, "Retirer cette fiche ? La suppression sera appliquée lors de l'enregistrement.", "Retirer la fiche", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            current.Delete(); current = null; RefreshList(); SetStatus("Suppression en attente. Enregistrez pour l'appliquer.");
        }
        private async Task SaveAsync()
        {
            if (snapshot == null || busy || !CommitCurrent()) return;
            SetBusy(true);
            try { int changed = await Task.Run(() => ServerDataService.Save(snapshot)); RefreshList(current); SetStatus(changed + " fiche(s) enregistrée(s). Rechargez les ressources du serveur pour les appliquer en jeu."); if(changed>0)Saved?.Invoke(this,EventArgs.Empty); }
            catch (Exception error) { ShowError(error); }
            finally { SetBusy(false); }
        }
        private void ExportSql()
        {
            if (snapshot == null || !CommitCurrent()) return;
            using (var dialog = new SaveFileDialog { Filter = "Modifications SQL (*.sql)|*.sql", DefaultExt = "sql", FileName = kind + (mapId.HasValue ? "_" + mapId : "") + ".sql" })
                if (dialog.ShowDialog(this) == DialogResult.OK) { ServerDataService.ExportSql(snapshot, dialog.FileName); SetStatus("SQL exporté. Les modifications restent en attente dans l'éditeur."); }
        }
        private bool CanDiscard()
        {
            bool valid = CommitCurrent();
            return valid && snapshot?.Data.GetChanges() == null || MessageBox.Show(this, "Abandonner les modifications en attente ?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }
        protected override void OnFormClosing(FormClosingEventArgs e) { if (busy || !CanDiscard()) e.Cancel = true; base.OnFormClosing(e); }
    }
}
