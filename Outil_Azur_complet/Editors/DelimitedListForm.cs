using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Tools_protocol.Data;

namespace Outil_Azur_complet.Editors
{
    internal sealed class DelimitedListForm : AzurEditorWindow
    {
        private readonly ListBox list = EditorUi.List();
        private readonly TableLayoutPanel sheet = EditorUi.Sheet(2);
        private readonly List<string> records;
        private readonly List<EditorField> fields = new List<EditorField>();
        private readonly FieldSpec[] specs;
        private readonly char recordSeparator, fieldSeparator;
        private readonly string original;
        private readonly bool trailing;
        private readonly int minimumFields;
        private int selected = -1;
        private bool selecting;
        private string[] parts;
        private readonly Func<string, string> summary;
        internal string Result { get; private set; }
        private DelimitedListForm(string title, string help, string data, char recordsDelimiter, char fieldsDelimiter, FieldSpec[] specs, Func<string, string> summary, int minimumFields)
            : base(title, help, new Size(1040, 750))
        {
            original = data; this.specs = specs; recordSeparator = recordsDelimiter; fieldSeparator = fieldsDelimiter; this.summary = summary; this.minimumFields = minimumFields;
            trailing = data.EndsWith(recordsDelimiter.ToString(), StringComparison.Ordinal);
            records = data == "" || data == "-1" ? new List<string>() : data.Split(recordsDelimiter).ToList();
            if (trailing) records.RemoveAt(records.Count - 1);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 }; layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 275)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 12, 0) }; left.Controls.Add(list);
            var detail = new Panel { Dock = DockStyle.Fill, AutoScroll = true }; detail.Controls.Add(sheet); layout.Controls.Add(left, 0, 0); layout.Controls.Add(detail, 1, 0); Body.Controls.Add(layout);
            ActionButton("Appliquer", Apply, true); ActionButton("Ajouter une ligne", Add); ActionButton("Retirer la ligne", Remove); ActionButton("Annuler", () => { DialogResult = DialogResult.Cancel; Close(); });
            list.SelectedIndexChanged += (s, e) => { if (selecting) return; int next = list.SelectedIndex; if (!Commit()) { selecting = true; list.SelectedIndex = selected; selecting = false; return; } Select(next); };
            RefreshList(records.Count > 0 ? 0 : -1);
        }
        internal static string Edit(IWin32Window owner, string title, string help, string data, char recordSeparator, char fieldSeparator, FieldSpec[] specs, Func<string, string> summary, int minimumFields = 1)
        { using (var form = new DelimitedListForm(title, help, data ?? "", recordSeparator, fieldSeparator, specs, summary, minimumFields)) return form.ShowDialog(owner) == DialogResult.OK ? form.Result : null; }
        private void RefreshList(int index)
        { selecting = true; list.BeginUpdate(); list.Items.Clear(); for(int item=0;item<records.Count;item++)list.Items.Add(Describe(item)); list.EndUpdate(); list.SelectedIndex = index; selecting = false; Select(index); SetStatus(records.Count + " ligne(s). Appliquer reporte ces valeurs dans la fiche ; l'enregistrement se fait dans l'éditeur principal."); }
        private string Describe(int index) { return (Text.StartsWith("Paliers de bonus",StringComparison.Ordinal)?(index+2)+" objets · ":(index+1)+". ")+summary(records[index]); }
        private void Select(int index)
        {
            selected = index; fields.Clear(); EditorUi.Clear(sheet); sheet.RowCount = 0;
            if (index < 0) { var empty = EditorUi.Label("Ajoutez une ligne pour définir ses options."); empty.Height = 50; EditorUi.AddField(sheet, empty); return; }
            parts = records[index].Split(fieldSeparator);
            for (int i = 0; i < specs.Length; i++)
            { var field = new EditorField(new DataColumn("part" + i, typeof(string)) { AllowDBNull = false }, i < parts.Length ? parts[i] : "", specs[i]); fields.Add(field); EditorUi.AddField(sheet, field); }
            string tail=string.Join(fieldSeparator.ToString(), parts.Skip(specs.Length));
            var extra = new EditorField(new DataColumn("suite", typeof(string)) { AllowDBNull = false }, tail, new FieldSpec("Autres paramètres du serveur", "Options supplémentaires conservées dans leur ordre. Séparateur : « " + fieldSeparator + " ».", multiline: true)); fields.Add(extra);
            var advanced=new CheckBox{Text="Afficher les paramètres supplémentaires du serveur",AutoSize=true,Checked=tail!="",Height=34,Font=Font};
            int advancedRow=(specs.Length+1)/2;sheet.Controls.Add(advanced,0,advancedRow);sheet.SetColumnSpan(advanced,2);sheet.Controls.Add(extra,0,advancedRow+1);sheet.SetColumnSpan(extra,2);sheet.RowCount=advancedRow+2;extra.Dock=DockStyle.Top;extra.Visible=advanced.Checked;advanced.CheckedChanged+=(s,e)=>extra.Visible=advanced.Checked;
        }
        private bool Commit()
        {
            if (selected < 0) return true;
            try
            {
                string[] values = fields.Take(specs.Length).Select(field => (string)field.Read()).ToArray();
                if (values.Any(value => value.IndexOf(recordSeparator) >= 0 || value.IndexOf(fieldSeparator) >= 0 || value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0)) throw new FormatException("Un champ contient un séparateur réservé au format serveur.");
                string extra = (string)fields.Last().Read();
                if (extra.IndexOf(recordSeparator) >= 0 || extra.IndexOf('\n') >= 0 || extra.IndexOf('\r') >= 0) throw new FormatException("Les autres paramètres contiennent un séparateur de ligne réservé.");
                bool unchanged = values.Select((value, i) => value == (i < parts.Length ? parts[i] : "")).All(same => same) && extra == string.Join(fieldSeparator.ToString(), parts.Skip(specs.Length));
                if (unchanged) { ClearValidationMessage(); return true; }
                int count = Math.Max(minimumFields, Math.Min(specs.Length, parts.Length));
                for (int i = specs.Length - 1; i >= count; i--) if (values[i] != "") { count = i + 1; break; }
                records[selected] = string.Join(fieldSeparator.ToString(), values.Take(count)) + (extra == "" ? "" : fieldSeparator + extra);
                selecting = true; list.Items[selected] = Describe(selected); selecting = false;
                ClearValidationMessage();
                return true;
            }
            catch (Exception error) { Status.Text = error.Message; Status.ForeColor = Color.Firebrick; return false; }
        }
        private void ClearValidationMessage() { if (Status.ForeColor == Color.Firebrick) SetStatus("Options valides. Appliquer reporte ces valeurs dans la fiche principale."); }
        private void Add() { if (!Commit()) return; records.Add(string.Join(fieldSeparator.ToString(), specs.Take(minimumFields).Select(spec => spec.DefaultText ?? "0"))); RefreshList(records.Count - 1); }
        private void Remove() { if (selected < 0) return; records.RemoveAt(selected); RefreshList(Math.Min(selected, records.Count - 1)); }
        private void Apply()
        {
            if (!Commit()) return;
            string joined = string.Join(recordSeparator.ToString(), records) + (trailing && records.Count > 0 ? recordSeparator.ToString() : "");
            if (records.Count == 0) joined = original == "-1" ? "-1" : "";
            Result = joined; DialogResult = DialogResult.OK; Close();
        }
        internal static string EffectName(string data)
        {
            string[] values = data.Split(';'); string id = values[0], name;
            name = EffectsListing.SpellsEffectList != null && EffectsListing.SpellsEffectList.TryGetValue(id, out name) ? name : "Effet";
            return "#" + id + " · " + name + (values.Length > 2 ? " · " + values[1] + " à " + values[2] : "");
        }
        internal static string EditEffects(IWin32Window owner, string data)
        {
            return Edit(owner, "Effets du sort", "Chaque ligne correspond à un effet. Les valeurs et paramètres dépendent du type d'effet.", data, '|', ';', new[] {
                EffectType(),
                FieldSpec.Number("Première valeur / minimum", "Souvent la valeur minimale ; certains effets utilisent un identifiant de cible ou d'objet."),
                FieldSpec.Number("Deuxième valeur / maximum", "Souvent la valeur maximale. Le rôle exact dépend de l'effet."),
                FieldSpec.Number("Troisième paramètre", "Paramètre additionnel de l'effet. La valeur -1 est courante."),
                FieldSpec.Number("Durée de l'effet (tours)", "0 pour un effet immédiat, selon le comportement du serveur."),
                FieldSpec.Number("Probabilité de l'effet", "Probabilité stockée par le serveur ; 0 est la valeur habituelle des effets systématiques.", true),
                new FieldSpec("Jet de dés", "Exemple : 1d6+10. Conservez une valeur compatible avec le type d'effet.")
            }, EffectName, 5);
        }
        private static FieldSpec EffectType()
        { var spec = FieldSpec.Number("Type de l'effet", "Choisissez son nom dans la liste, ou saisissez directement un identifiant."); spec.Suggestions = EffectsListing.SpellsEffectList; return spec; }
    }
}
