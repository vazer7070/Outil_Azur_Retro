using System;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Outil_Azur_complet.Editors;

namespace Outil_Azur_complet.editeur_items
{
    internal sealed class InventoryItemForm : AzurEditorWindow
    {
        private readonly EditorField quantity,position,stats,puit;
        private readonly InventoryChange draft;
        internal InventoryChange Result {get;private set;}
        internal InventoryItemForm(InventoryChange draft,string name) : base("Modifier un objet d'inventaire",name+" · Les changements seront placés en attente dans l'inventaire.",new Size(1080,780))
        {
            this.draft=draft;
            var tabs=EditorUi.Tabs();Body.Controls.Add(tabs);var general=EditorUi.Sheet(2);EditorUi.Page(tabs,"Objet",general);
            EditorUi.AddField(general,new EditorField(Column("guid",typeof(int)),draft.ItemGuid,new FieldSpec("Identifiant de l'exemplaire","Identifiant unique ; lecture seule."),true));
            EditorUi.AddField(general,new EditorField(Column("template",typeof(int)),draft.OriginalTemplate,new FieldSpec("Modèle d'objet","Modèle de cet objet ; lecture seule."),true));
            quantity=new EditorField(Column("quantity",typeof(int)),draft.Quantity,new FieldSpec("Quantité","Supérieure à zéro. Un objet équipé doit avoir une quantité de 1."));EditorUi.AddField(general,quantity);
            string[] slots={"Inventaire","Amulette","Arme","Anneau gauche","Ceinture","Anneau droit","Bottes","Chapeau","Cape","Familier","Dofus 1","Dofus 2","Dofus 3","Dofus 4","Dofus 5","Dofus 6","Bouclier"};
            position=new EditorField(Column("position",typeof(int)),draft.Position,new FieldSpec("Emplacement","L'enregistrement vérifie la compatibilité et la disponibilité de l'emplacement.") {Options=slots.Select((label,index)=>new EditorOption((index-1).ToString(),label)).ToArray()});EditorUi.AddField(general,position);
            puit=new EditorField(Column("puit",typeof(int)),draft.Puit,new FieldSpec("Puits de forge-magie","Réserve de forge-magie conservée par le serveur."));EditorUi.AddField(general,puit);
            var effects=EditorUi.Sheet();EditorUi.Page(tabs,"Effets",effects);stats=new EditorField(Column("stats",typeof(string)),draft.Stats,new FieldSpec("Effets de cet exemplaire","Modifiez les valeurs de cet objet sans changer son modèle.") {Edit=ItemFieldCatalog.EditEffects,Summary=ItemFieldCatalog.CountEffects});EditorUi.AddField(effects,stats);
            ActionButton("Mettre en attente",Apply,true,180);ActionButton("Annuler",()=>{DialogResult=DialogResult.Cancel;Close();});SetStatus("Aucune écriture ne sera faite avant Enregistrer l'inventaire.");
        }
        private static DataColumn Column(string name,Type type){return new DataColumn(name,type){AllowDBNull=false};}
        private void Apply()
        {
            int count=(int)quantity.Read(),slot=(int)position.Read(),well=(int)puit.Read();string effects=(string)stats.Read();
            if(count<=0 || well<0)throw new FormatException("La quantité doit être positive et le puits positif ou nul.");ItemFieldCatalog.ValidateEffects(effects);
            Result=new InventoryChange{CharacterId=draft.CharacterId,CharacterName=draft.CharacterName,Kind=InventoryChangeKind.Update,ItemGuid=draft.ItemGuid,Quantity=count,Position=slot,Puit=well,Stats=effects,OriginalTemplate=draft.OriginalTemplate,OriginalQuantity=draft.OriginalQuantity,OriginalPosition=draft.OriginalPosition,OriginalPuit=draft.OriginalPuit,OriginalStats=draft.OriginalStats};
            DialogResult=DialogResult.OK;Close();
        }
    }
    public partial class itemeditor
    {
        private void StageItemEdit()
        {
            if(!_inventoryAvailable || listBox4.SelectedItem==null || sfListView1.SelectedItem==null)return;
            var player=Tools_protocol.Kryone.Database.CharacterList.Listing(listBox4.SelectedItem.ToString());
            var match=System.Text.RegularExpressions.Regex.Match(sfListView1.SelectedItem.ToString(),@"\((\d+)\)");
            if(player==null || player.Logged!=0 || !match.Success || !int.TryParse(match.Groups[1].Value,out int guid) || !Tools_protocol.Kryone.Database.ItemList.ItemsList.TryGetValue(guid,out var item))return;
            if(_inventoryChanges.ContainsKey(player.Id+":remove:"+guid)){MessageBox.Show(this,"Annulez d'abord la suppression de cet objet dans Changements d'inventaire.","Objet en attente");return;}
            string key=player.Id+":update:"+guid;
            if(!_inventoryChanges.TryGetValue(key,out var draft))draft=new InventoryChange{CharacterId=player.Id,CharacterName=player.Name,Kind=InventoryChangeKind.Update,ItemGuid=guid,Quantity=item.Qua,Position=item.Pos,Puit=item.Puit,Stats=item.Stat,OriginalTemplate=item.Template,OriginalQuantity=item.Qua,OriginalPosition=item.Pos,OriginalPuit=item.Puit,OriginalStats=item.Stat};
            using(var form=new InventoryItemForm(draft,sfListView1.SelectedItem.ToString()))if(form.ShowDialog(this)==DialogResult.OK){_inventoryChanges[key]=form.Result;RefreshPendingInventory();editorLayout.Status.Text="Modification de l'objet en attente. Ouvrez Changements d'inventaire pour l'enregistrer.";}
        }
    }
}
