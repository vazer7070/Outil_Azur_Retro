using System;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Tool_Editor.items;
using Outil_Azur_complet.Editors;

namespace Outil_Azur_complet.editeur_items
{
    public partial class itemeditor
    {
        private readonly CheckBox integrateClient = new CheckBox { Text = "Intégrer dans une copie du SWF existant", AutoSize = true };
        private readonly CheckBox includeClientSource = new CheckBox { Text = "Joindre la source ActionScript", Checked = true, AutoSize = true };
        private readonly iTalk.iTalk_TextBox_Small clientSourcePath = EditorUi.TextBox();

        private void BuildClientExportOptions()
        {
            editorLayout.Field("Fichier client", integrateClient, "Conserver les autres objets", "Choisissez le fichier de langue qui contient la table I.u.");
            clientSourcePath.ReadOnly = true;
            editorLayout.Field("Fichier client", clientSourcePath, "SWF d'origine", "Le fichier d'origine est conservé ; le résultat est enregistré ailleurs.");
            var choose = EditorUi.Button("Choisir le SWF…", false, 220);
            choose.Click += (s,e) => {
                using (var dialog = new OpenFileDialog { Filter = "Fichier client SWF|*.swf", Title = "Choisir le fichier d'objets client" })
                    if (dialog.ShowDialog(this) == DialogResult.OK) { clientSourcePath.Text = dialog.FileName; integrateClient.Checked = true; }
            };
            EditorUi.AddField(editorLayout.Sheet("Fichier client"), choose);
            editorLayout.Field("Fichier client", includeClientSource, "Source lisible facultative", "Le fichier .txt accompagne le SWF binaire ; aucune compilation externe n'est nécessaire.");
            editorLayout.Notice("Fichier client", "Modifier les objets d'un fichier client", "Ouvrez un SWF d'objets, recherchez une fiche et modifiez ses options. Enregistrez toutes les modifications dans une copie.", "Ouvrir l'éditeur client", () => new ItemClientEditorForm().Show(this));
        }

        private sealed class ClientExportPlan
        {
            internal byte[] Movie;
            internal string Destination, SourceText;
            internal ItemClientSwf Original;
            internal void Write()
            {
                if (Original != null) Original.SaveCopy(Destination, Movie);
                else ItemClientSwf.SaveCompiled(Destination, Movie);
                if (SourceText != null) File.WriteAllText(Path.ChangeExtension(Destination, ".txt"), SourceText, new UTF8Encoding(false));
            }
        }

        private ClientExportPlan PrepareClientExport(ItemClientDefinition definition, string sourceText)
        {
            // Decode, compile and choose the destination before any SQL injection.
            var plan = new ClientExportPlan { SourceText = includeClientSource.Checked ? sourceText : null };
            if (integrateClient.Checked) {
                if (string.IsNullOrWhiteSpace(clientSourcePath.Text)) throw new InvalidOperationException("Choisissez le SWF d'origine dans la rubrique Fichier client.");
                plan.Original = ItemClientSwf.Load(clientSourcePath.Text);
                plan.Movie = plan.Original.WithDefinition(definition);
            }
            else plan.Movie = ItemClientSwf.Compile(definition);
            using (var dialog = new SaveFileDialog {
                Filter = "Fichier client SWF|*.swf", DefaultExt = "swf", AddExtension = true,
                FileName = plan.Original != null ? Path.GetFileNameWithoutExtension(clientSourcePath.Text) + "_azur.swf" : "objet_" + definition.Id + ".swf",
                Title = plan.Original != null ? "Enregistrer le fichier client modifié" : "Enregistrer le SWF de cet objet"
            }) {
                if (dialog.ShowDialog(this) != DialogResult.OK) return null;
                plan.Destination = dialog.FileName;
            }
            if (plan.Original != null && string.Equals(Path.GetFullPath(plan.Destination), Path.GetFullPath(clientSourcePath.Text), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Choisissez une destination différente pour conserver le fichier d'origine.");
            // Create the directory now and check access before the SQL transaction.
            string directory = Path.GetDirectoryName(Path.GetFullPath(plan.Destination));
            string probe = Path.Combine(directory, ".azur-access-" + Guid.NewGuid().ToString("N"));
            try { using (var file = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { } }
            finally { if (File.Exists(probe)) File.Delete(probe); }
            return plan;
        }
    }
}
