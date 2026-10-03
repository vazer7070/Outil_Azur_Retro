using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Outil_Azur_complet.Editors;
using Tool_Editor.items;

namespace Outil_Azur_complet.editeur_items
{
    public sealed class ItemClientEditorForm : AzurEditorWindow
    {
        private readonly ListBox list = EditorUi.List();
        private readonly iTalk.iTalk_TextBox_Small search = EditorUi.TextBox();
        private readonly TabControl tabs = EditorUi.Tabs();
        private readonly Dictionary<string, EditorField> fields = new Dictionary<string, EditorField>();
        private readonly Dictionary<string, Func<object>> readers = new Dictionary<string, Func<object>>();
        private readonly Dictionary<int, IDictionary<string, object>> drafts = new Dictionary<int, IDictionary<string, object>>();
        private ItemClientSwf document;
        private string openedPath;
        private int selectedId;
        private bool refreshing;
        private sealed class Choice
        {
            internal int Id; internal string Name;
            public override string ToString() { return "#" + Id + " · " + Name; }
        }

        public ItemClientEditorForm() : base("Objets du client", "Ouvrez un SWF, recherchez un objet et modifiez sa fiche. Enregistrer une copie conserve le fichier d'origine.", new Size(1220, 820))
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 290)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 14, 0) };
            var label = EditorUi.Label("Rechercher par nom ou identifiant", 10, true); label.Height = 30; label.Dock = DockStyle.Top;
            search.Dock = DockStyle.Top; left.Controls.Add(list); left.Controls.Add(search); left.Controls.Add(label);
            layout.Controls.Add(left, 0, 0); layout.Controls.Add(tabs, 1, 0); Body.Controls.Add(layout);
            ActionButton("Ouvrir un SWF…", Open, width:170); ActionButton("Enregistrer une copie…", Save, true, 220);
            ActionButton("Annuler les changements", Reset, width:225); ActionButton("Fermer", Close, width:110);
            search.TextChanged += (s,e) => { if (!refreshing) { try { Commit(); Filter(); } catch (Exception error) { ShowError(error); } } };
            list.SelectedIndexChanged += (s,e) => {
                if (refreshing) return;
                var choice = list.SelectedItem as Choice;
                try { Commit(); if (choice != null) SelectItem(choice.Id); }
                catch (Exception error) {
                    refreshing = true; list.SelectedItem = list.Items.Cast<Choice>().FirstOrDefault(item => item.Id == selectedId); refreshing = false; ShowError(error);
                }
            };
            FormClosing += (s,e) => { try { Commit(); if (drafts.Count > 0 && MessageBox.Show(this, "Fermer sans exporter les modifications en attente ?", "Objets du client", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) e.Cancel = true; } catch (Exception error) { e.Cancel = true; ShowError(error); } };
            SetStatus("Choisissez un fichier d'objets client SWF FWS ou CWS. Aucun fichier n'est exécuté.");
            ShowEmpty();
        }

        private void ShowEmpty()
        {
            EditorUi.Clear(tabs);
            var sheet = EditorUi.Sheet(); var hint = EditorUi.Label("Les fiches du fichier client apparaîtront ici.", 14, true); hint.Height = 70;
            EditorUi.AddField(sheet, hint); var help = EditorUi.Label("L'éditeur accepte les données littérales AVM1 de la table I.u sur une seule image. Les autres formats sont signalés à l'ouverture."); help.Height = 65;
            EditorUi.AddField(sheet, help); EditorUi.Page(tabs, "Fichier client", sheet);
        }

        private void Open()
        {
            Commit();
            if (drafts.Count > 0 && MessageBox.Show(this, "Abandonner les modifications du fichier actuel ?", "Objets du client", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            using (var dialog = new OpenFileDialog { Filter = "Fichier client SWF|*.swf", Title = "Ouvrir les objets du client" })
                if (dialog.ShowDialog(this) == DialogResult.OK) LoadFile(dialog.FileName);
        }

        private void LoadFile(string path)
        {
            // Failed reads leave the current document and its drafts intact.
            var loaded = ItemClientSwf.Load(path);
            document = loaded; openedPath = path; drafts.Clear(); selectedId = 0; fields.Clear(); readers.Clear();
            refreshing = true; search.Text = ""; refreshing = false; Filter();
            SetStatus(document.Count + " objets · " + Path.GetFileName(path));
        }

        private void Filter()
        {
            if (document == null) return;
            refreshing = true;
            try {
                list.BeginUpdate(); list.Items.Clear();
                string query = search.Text.Trim();
                foreach (var item in document.ItemNames) {
                    object renamed;
                    string name = drafts.ContainsKey(item.Key) && drafts[item.Key].TryGetValue("n", out renamed) ? Convert.ToString(renamed, CultureInfo.InvariantCulture) : item.Value;
                    if (query.Length == 0 || name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 || item.Key.ToString(CultureInfo.InvariantCulture).Contains(query))
                        list.Items.Add(new Choice { Id = item.Key, Name = name });
                }
                var choice = list.Items.Cast<Choice>().FirstOrDefault(item => item.Id == selectedId) ?? list.Items.Cast<Choice>().FirstOrDefault();
                list.SelectedItem = choice;
                if (choice != null) SelectItem(choice.Id);
                else { selectedId = 0; fields.Clear(); readers.Clear(); ShowEmpty(); }
            }
            finally { list.EndUpdate(); refreshing = false; }
        }

        private void SelectItem(int id)
        {
            var data = drafts.ContainsKey(id) ? drafts[id] : document.GetFields(id);
            selectedId = id; fields.Clear(); readers.Clear();
            string previous = tabs.SelectedTab == null ? null : tabs.SelectedTab.Text; EditorUi.Clear(tabs);
            var sheets = new Dictionary<string, TableLayoutPanel>();
            foreach (var pair in data.OrderBy(pair => Order(pair.Key)).ThenBy(pair => pair.Key, StringComparer.Ordinal)) {
                string section = Section(pair.Key);
                TableLayoutPanel sheet;
                if (!sheets.TryGetValue(section, out sheet)) { sheet = EditorUi.Sheet(2); sheets[section] = sheet; EditorUi.Page(tabs, section, sheet); }
                var array = pair.Value as object[];
                if (pair.Key == "e" && array != null) {
                    var weaponReaders = new List<Func<object>>();
                    for (int i = 0; i < array.Length; i++) {
                        string[] labels = { "Bonus aux coups critiques", "Coût en PA", "Portée minimale", "Portée maximale", "Coups critiques : 1 sur N", "Échecs critiques : 1 sur N" };
                        string label = i < labels.Length ? labels[i] : "Option technique de l'arme e[" + i + "]";
                        string help = i < labels.Length ? "Valeur utilisée dans la fiche d'arme du client." : "Sens non documenté pour ce client : la valeur d'origine est conservée jusqu'à modification.";
                        weaponReaders.Add(AddField(sheet, "e" + i, array[i], new FieldSpec(label, help)));
                    }
                    readers[pair.Key] = () => weaponReaders.Select(read => read()).ToArray();
                }
                else readers[pair.Key] = AddField(sheet, pair.Key, pair.Value, Spec(pair.Key));
            }
            if (previous != null) tabs.SelectedTab = tabs.TabPages.Cast<TabPage>().FirstOrDefault(page => page.Text == previous) ?? tabs.TabPages[0];
            SetStatus("Objet #" + id + " · " + data.Count + " propriétés · " + drafts.Count + " fiches modifiées");
        }

        private Func<object> AddField(TableLayoutPanel sheet, string key, object value, FieldSpec spec)
        {
            bool nested = value is object[] || value is IDictionary<string, object>;
            Type type = value == null ? typeof(string) : nested ? typeof(string) : value.GetType();
            object initial = value == null ? DBNull.Value : nested ? JsonConvert.SerializeObject(value, Formatting.Indented) : value;
            if (type == typeof(bool)) spec.Options = new[] { new EditorOption("true", "Oui"), new EditorOption("false", "Non") };
            if (nested) { spec.Multiline = true; spec.Help = "Données structurées JSON. Tous les éléments du client sont conservés ; modifiez uniquement ceux dont vous connaissez le sens."; }
            var field = new EditorField(new DataColumn(key, type) { AllowDBNull = value == null }, initial, spec);
            fields[key] = field; EditorUi.AddField(sheet, field);
            field.Changed += (s,e) => SetStatus("Modifications en attente · Enregistrer une copie exportera toutes les fiches modifiées.");
            return () => {
                object result = field.Read(); if (result == DBNull.Value) return null;
                if (!nested) return result;
                using (var reader = new JsonTextReader(new StringReader((string)result)) { DateParseHandling = DateParseHandling.None, MaxDepth = 64 }) {
                    var token = JToken.ReadFrom(reader);
                    if (reader.Read()) throw new FormatException("Une seule valeur JSON est attendue.");
                    return ReadLiteral(token);
                }
            };
        }

        private static object ReadLiteral(JToken token)
        {
            var obj = token as JObject; if (obj != null) return obj.Properties().ToDictionary(property => property.Name, property => ReadLiteral(property.Value));
            var array = token as JArray; if (array != null) return array.Select(ReadLiteral).ToArray();
            if (token.Type == JTokenType.Null) return null;
            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float || token.Type == JTokenType.Boolean || token.Type == JTokenType.String) return ((JValue)token).Value;
            throw new FormatException("Seuls les nombres, textes, booléens, tableaux, objets et valeurs nulles sont acceptés.");
        }

        private void Commit()
        {
            if (document == null || selectedId <= 0) return;
            var values = readers.ToDictionary(pair => pair.Key, pair => pair.Value());
            if (JsonConvert.SerializeObject(values) == JsonConvert.SerializeObject(document.GetFields(selectedId).OrderBy(pair => Order(pair.Key)).ThenBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value))) drafts.Remove(selectedId);
            else drafts[selectedId] = values;
            var choice = list.Items.Cast<Choice>().FirstOrDefault(item => item.Id == selectedId);
            object name; if (choice != null && values.TryGetValue("n", out name)) { choice.Name = Convert.ToString(name, CultureInfo.InvariantCulture); list.Invalidate(); }
        }

        private void Save()
        {
            if (document == null) throw new InvalidOperationException("Ouvrez d'abord un fichier SWF d'objets.");
            Commit(); byte[] result = document.WithChanges(drafts);
            using (var dialog = new SaveFileDialog { Filter = "Fichier client SWF|*.swf", AddExtension = true, DefaultExt = "swf", FileName = Path.GetFileNameWithoutExtension(openedPath) + "_azur.swf", Title = "Enregistrer les objets du client" }) {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                document.SaveCopy(dialog.FileName, result);
                int changed = drafts.Count;
                // The exported file becomes the new snapshot; another save still requires a copy.
                LoadFile(dialog.FileName); SetStatus(changed + " fiches exportées · " + dialog.FileName);
            }
        }

        private void Reset()
        {
            if (document == null) return;
            if (MessageBox.Show(this, "Annuler toutes les modifications non exportées ?", "Objets du client", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            drafts.Clear(); if (selectedId > 0) SelectItem(selectedId);
        }

        private static int Order(string key)
        { string[] keys = { "n", "d", "g", "t", "l", "w", "p", "c", "fm", "u", "tw", "e" }; int index = Array.IndexOf(keys, key); return index < 0 ? 100 : index; }
        private static string Section(string key)
        { return key == "e" || key == "tw" ? "Arme" : key == "c" || key == "fm" || key == "u" ? "Utilisation" : Order(key) < 100 ? "Fiche objet" : "Options du client"; }
        private static FieldSpec Spec(string key)
        {
            switch (key) {
                case "n": return new FieldSpec("Nom de l'objet", "Nom affiché dans le client.") { Validate = text => { if (string.IsNullOrWhiteSpace(text)) throw new FormatException("Le nom de l'objet est obligatoire."); } };
                case "d": return new FieldSpec("Description", "Texte affiché dans la fiche de l'objet.", multiline:true);
                case "g": return new FieldSpec("Apparence graphique", "Identifiant du visuel client.");
                case "t": return new FieldSpec("Type d'objet", "Catégorie du modèle.") { Options = ItemFieldCatalog.ItemTypes() };
                case "l": return new FieldSpec("Niveau requis", "Niveau minimal affiché pour l'objet.");
                case "w": return new FieldSpec("Poids (pods)", "Poids d'un exemplaire.");
                case "p": return new FieldSpec("Prix (kamas)", "Prix indiqué par le client.");
                case "c": return new FieldSpec("Conditions d'utilisation", "Expression de conditions du client.") { Edit = ConditionExpressionForm.Edit, Summary = text => string.IsNullOrEmpty(text) ? "Aucune condition" : text };
                case "fm": return new FieldSpec("Forge-magie autorisée", "Option de modification de l'objet.");
                case "u": return new FieldSpec("Objet utilisable", "Autoriser l'action Utiliser dans le client.");
                case "tw": return new FieldSpec("Arme à deux mains", "Option de maniement de l'arme.");
                default: return new FieldSpec("Paramètre client « " + key + " »", "Sens non documenté pour ce client. Sa valeur reste modifiable et est conservée jusqu'à modification.");
            }
        }
    }
}
