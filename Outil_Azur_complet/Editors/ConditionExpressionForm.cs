using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Tools_protocol.Data;

namespace Outil_Azur_complet.Editors
{
    internal sealed class ConditionExpressionForm : AzurEditorWindow
    {
        private readonly iTalk.iTalk_TextBox_Small expression = EditorUi.TextBox("",true);
        private readonly iTalk.iTalk_TextBox_Small value = EditorUi.TextBox();
        private readonly EditorComboBox condition = new EditorComboBox(), comparison = new EditorComboBox(), connector = new EditorComboBox();
        private string result;
        private ConditionExpressionForm(string data) : base("Conditions d'utilisation","Choisissez une condition et sa valeur, puis ajoutez-la à l'expression. L'expression complète reste modifiable.",new Size(1080,760))
        {
            var tabs=EditorUi.Tabs(); Body.Controls.Add(tabs);
            var sheet=EditorUi.Sheet(2); EditorUi.Page(tabs,"Construire",sheet);
            foreach(var pair in ConditionsListing.ConditionsDico) condition.Items.Add(new EditorOption(pair.Key,pair.Value+" · "+pair.Key));
            comparison.Items.AddRange(new[]{new EditorOption(">","Supérieur à"),new EditorOption("<","Inférieur à"),new EditorOption("=","Égal à"),new EditorOption("!","Différent de"),new EditorOption("~","Correspond à")});
            connector.Items.AddRange(new[]{new EditorOption("&","ET : toutes les conditions"),new EditorOption("|","OU : au moins une condition")});
            foreach(var combo in new[]{condition,comparison,connector}) { combo.DropDownStyle=ComboBoxStyle.DropDownList; combo.Font=Font; if(combo.Items.Count>0)combo.SelectedIndex=0; }
            EditorUi.AddField(sheet,BoundEditorLayout.FieldPanel(condition,"Condition","Caractéristique ou état à vérifier."));
            EditorUi.AddField(sheet,BoundEditorLayout.FieldPanel(comparison,"Comparaison","Choisissez comment comparer la valeur."));
            EditorUi.AddField(sheet,BoundEditorLayout.FieldPanel(value,"Valeur attendue","Nombre, identifiant ou texte selon la condition."));
            EditorUi.AddField(sheet,BoundEditorLayout.FieldPanel(connector,"Relation avec les conditions précédentes","La relation est utilisée lorsque l'expression contient déjà une condition."));
            var add=EditorUi.Button("Ajouter cette condition",true,240); add.Click+=(s,e)=>AddCondition(); EditorUi.AddField(sheet,add);
            var raw=EditorUi.Sheet(); EditorUi.Page(tabs,"Expression",raw);
            expression.Text=data??""; EditorUi.AddField(raw,BoundEditorLayout.FieldPanel(expression,"Expression complète","Vous pouvez conserver ou modifier les parenthèses et les variantes propres à votre serveur.",multiline:true));
            // The preview is always visible, including while using the guided builder.
            var preview=EditorUi.Label("Expression : "+expression.Text,10); preview.Dock=DockStyle.Bottom; preview.Height=55; Body.Controls.Add(preview);
            expression.TextChanged+=(s,e)=>{preview.Text="Expression : "+(expression.Text==""?"Aucune condition":expression.Text);SetStatus("Expression modifiée. Cliquez sur Appliquer pour la conserver.");};
            ActionButton("Appliquer",()=>{if(expression.Text.Any(char.IsControl))throw new FormatException("L'expression doit tenir sur une seule ligne.");result=expression.Text;DialogResult=DialogResult.OK;Close();},true);
            ActionButton("Effacer les conditions",()=>expression.Text="",width:190);
            ActionButton("Annuler",()=>{DialogResult=DialogResult.Cancel;Close();}); SetStatus("L'expression existante reste identique tant que vous ne la modifiez pas.");
        }
        private void AddCondition()
        {
            if(condition.SelectedItem==null || comparison.SelectedItem==null || connector.SelectedItem==null || value.Text.Trim()=="") { SetStatus("Choisissez une condition, une comparaison et sa valeur.");return; }
            string text=value.Text.Trim(); if(text.IndexOfAny(new[]{'&','|','(',')','\r','\n'})>=0) {SetStatus("La valeur contient un séparateur. Modifiez directement l'onglet Expression pour cette variante.");return;}
            expression.Text+=(expression.Text==""?"":((EditorOption)connector.SelectedItem).Value)+((EditorOption)condition.SelectedItem).Value+((EditorOption)comparison.SelectedItem).Value+text;
        }
        internal static string Edit(IWin32Window owner,string data)
        { using(var form=new ConditionExpressionForm(data))return form.ShowDialog(owner)==DialogResult.OK?form.result:null; }
    }
}
