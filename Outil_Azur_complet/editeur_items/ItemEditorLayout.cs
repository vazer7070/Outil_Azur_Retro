using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Outil_Azur_complet.Editors;

namespace Outil_Azur_complet.editeur_items
{
    public partial class itemeditor
    {
        private BoundEditorLayout editorLayout;
        private void BuildEditorLayout()
        {
            editorLayout = new BoundEditorLayout(this,"Objets et inventaires","Créez un objet ou ouvrez les éditeurs de modèles, de recettes et de panoplies. Les changements sont enregistrés explicitement.");
            editorLayout.Status.Text = "Complétez une fiche de création, ou choisissez un autre éditeur dans les rubriques.";
            iTalk_ComboBox1.Items.Clear();iTalk_ComboBox1.Items.AddRange(ItemFieldCatalog.ItemTypes());
            editorLayout.Field("Nouvel objet",iTalk_TextBox_Small1,"Nom de l'objet","Nom du nouvel objet.");
            editorLayout.Field("Nouvel objet",iTalk_TextBox_Small2,"Identifiant du modèle","Numéro libre, supérieur à zéro.");
            editorLayout.Field("Nouvel objet",iTalk_ComboBox1,"Type d'objet","Catégorie de l'objet.");
            editorLayout.Field("Nouvel objet",iTalk_ComboBox2,"Panoplie","Panoplie existante ; laissez vide si aucune.");
            editorLayout.Field("Nouvel objet",iTalk_NumericUpDown1,"Niveau requis","Niveau minimal du personnage.");
            editorLayout.Field("Nouvel objet",iTalk_NumericUpDown2,"Poids (pods)","Poids d'un exemplaire.");
            editorLayout.Field("Nouvel objet",iTalk_NumericUpDown3,"Prix (kamas)","Prix de vente du PNJ.");
            editorLayout.Field("Nouvel objet",iTalk_TextBox_Small10,"Prix en points","Prix dans les boutiques utilisant des points.");
            editorLayout.Field("Nouvel objet",iTalk_TextBox_Small14,"Apparence graphique","Identifiant graphique utilisé pour l'export client.");
            editorLayout.Field("Nouvel objet",iTalk_TextBox_Small13,"Description","Description utilisée pour l'export client.",multiline:true);
            editorLayout.Field("Arme",iTalk_TextBox_Small4,"Coût en PA","Points d'action pour utiliser l'arme.");
            editorLayout.Field("Arme",iTalk_TextBox_Small5,"Portée minimale","Distance minimale de la cible.");
            editorLayout.Field("Arme",iTalk_TextBox_Small8,"Portée maximale","Distance maximale de la cible.");
            editorLayout.Field("Arme",iTalk_TextBox_Small3,"Coups critiques : 1 sur N","0 pour la valeur par défaut du serveur.");
            editorLayout.Field("Arme",iTalk_TextBox_Small6,"Échecs critiques : 1 sur N","0 pour la valeur par défaut du serveur.");
            editorLayout.Field("Arme",iTalk_TextBox_Small7,"Bonus aux coups critiques","Bonus appliqué en cas de coup critique.");
            editorLayout.Field("Arme",iTalk_CheckBox2,"Arme à deux mains","Activer le maniement à deux mains.");
            editorLayout.Field("Conditions",iTalk_ComboBox3,"Condition","Caractéristique ou état à vérifier.");
            editorLayout.Field("Conditions",iTalk_ComboBox4,"Comparaison","Choisissez la comparaison.",new[]{"<=Inférieur à",">=Supérieur à","==Égal à","!=Différent de","~=Correspond à"});
            editorLayout.Field("Conditions",iTalk_NumericUpDown4,"Valeur attendue","Valeur numérique de la condition.");
            editorLayout.Field("Conditions",iTalk_ComboBox6,"Relation entre les conditions","ET demande toutes les conditions ; OU en demande au moins une.",new[]{"&=ET","|=OU"});
            editorLayout.Field("Conditions",iTalk_LinkLabel1,"Ajouter la condition","La condition sera incluse dans l'objet créé.");
            var conditions=EditorUi.List();conditions.Height=180;conditions.Dock=DockStyle.Top;
            EditorUi.AddField(editorLayout.Sheet("Conditions"),conditions);
            iTalk_LinkLabel1.LinkClicked+=(s,e)=>{conditions.Items.Clear();foreach(string rule in ConditionsSelected)conditions.Items.Add(rule.Replace("%&"," · ET").Replace("%|"," · OU"));};
            conditions.DoubleClick+=(s,e)=>{if(conditions.SelectedIndex<0)return;int index=conditions.SelectedIndex;string rule=ConditionsSelected[index];ConditionsSelected.RemoveAt(index);string code=InCondi.FirstOrDefault(entry=>rule.StartsWith(entry,StringComparison.Ordinal));if(code!=null)InCondi.Remove(code);conditions.Items.RemoveAt(index);iTalk_NotificationNumber1.Value=ConditionsSelected.Count;};
            editorLayout.Field("Effets",listBox2,"Effets disponibles","Choisissez l'effet à ajouter.",multiline:true);BoundEditorLayout.StyleList(listBox2);listBox2.Dock=DockStyle.None;listBox2.Height=180;listBox2.Parent.Height=240;
            editorLayout.Field("Effets",listBox3,"Effets de cet objet","Sélectionnez un effet pour le retirer.",multiline:true);BoundEditorLayout.StyleList(listBox3);listBox3.Dock=DockStyle.None;listBox3.Height=180;listBox3.Parent.Height=240;
            editorLayout.Field("Effets",iTalk_TextBox_Small11,"Valeur minimale / première valeur","Valeur demandée par l'effet choisi.");
            editorLayout.Field("Effets",iTalk_TextBox_Small12,"Valeur maximale / deuxième valeur","Renseignez-la uniquement si l'effet possède deux valeurs.");
            editorLayout.Field("Effets",iTalk_LinkLabel3,"Ajouter cet effet","L'effet est ajouté à la fiche de création.");
            editorLayout.Field("Effets",iTalk_LinkLabel4,"Retirer l'effet sélectionné","Retirer un effet avant de créer l'objet.");
            editorLayout.Field("Recette",iTalk_CheckBox4,"Objet fabricable","Activer une recette de fabrication.");
            editorLayout.Field("Recette",iTalk_ComboBox5,"Ingrédient","Modèle d'objet utilisé comme ingrédient.");
            editorLayout.Field("Recette",iTalk_NumericUpDown5,"Quantité de l'ingrédient","Quantité nécessaire pour fabriquer un objet.");
            editorLayout.Field("Recette",iTalk_LinkLabel2,"Ajouter l'ingrédient","Chaque ingrédient apparaît une seule fois.");
            editorLayout.Field("Recette",listBox1,"Ingrédients nécessaires","Composition de la recette.",multiline:true);BoundEditorLayout.StyleList(listBox1);listBox1.Dock=DockStyle.None;listBox1.Height=180;listBox1.Parent.Height=240;
            editorLayout.Field("Recette",iTalk_LinkLabel5,"Retirer l'ingrédient","Retirer l'ingrédient sélectionné.");
            editorLayout.Field("Exportation",iTalk_TextBox_Small9,"Identifiant de l'exemplaire (GUID)","Numéro libre de l'objet créé dans la base world.");
            editorLayout.Field("Exportation",iTalk_CheckBox1,"Échange autorisé","Disponible si le schéma du serveur expose cette option.");
            editorLayout.Field("Exportation",iTalk_CheckBox3,"Disponible en boutique","Disponible si le schéma du serveur expose cette option.");
            editorLayout.Field("Exportation",iTalk_CheckBox7,"Forge-magie autorisée","Option utilisée dans l'export client.");
            editorLayout.Field("Exportation",iTalk_CheckBox8,"Objet utilisable","Option utilisée dans l'export client.");
            // This historical checkbox was never included in either output.
            // Keep it hidden until a supported server/client representation exists.
            iTalk_CheckBox9.Enabled=false;iTalk_CheckBox9.Visible=false;
            iTalk_RadioButton1.Text="SWF du client";iTalk_RadioButton3.Text="SQL et SWF du client";
            editorLayout.Field("Exportation",iTalk_RadioButton2,"Exporter SQL","Générer les requêtes pour le serveur.");
            editorLayout.Field("Exportation",iTalk_RadioButton1,"Exporter le SWF du client","Produit un SWF binaire. Réglez l'intégration dans Fichier client.");
            editorLayout.Field("Exportation",iTalk_RadioButton3,"Exporter les deux formats","Requêtes SQL et SWF binaire du client.");
            editorLayout.Field("Exportation",iTalk_CheckBox6,"Créer les fichiers SQL","Exporter les requêtes pour les appliquer ultérieurement.");
            editorLayout.Field("Exportation",iTalk_CheckBox5,"Enregistrer directement en base","Enregistrement groupé après validation du schéma et des identifiants.");
            BuildClientExportOptions();
            editorLayout.Notice("Autres éditeurs","Modifier les modèles d'objets","Recherchez un objet existant, modifiez ses effets, son arme, ses conditions ou ses prix. Vous pouvez aussi créer un modèle.","Ouvrir les modèles d'objets",()=>new ServerDataForm(ServerResourceKind.ItemTemplates,"Modèles d'objets").Show(this));
            editorLayout.Notice("Autres éditeurs","Créer ou modifier une panoplie","Gérez le nom, la composition et les bonus de chaque palier d'équipement.","Ouvrir les panoplies",()=>new ServerDataForm(ServerResourceKind.ItemSets,"Panoplies").Show(this));
            editorLayout.Notice("Autres éditeurs","Modifier les recettes","Sélectionnez l'objet fabriqué, puis ajoutez ou retirez ses ingrédients et leurs quantités.","Ouvrir les recettes",()=>new ServerDataForm(ServerResourceKind.Crafts,"Recettes de fabrication").Show(this));
            editorLayout.Action(iTalk_Button_21,"Créer l'objet",180);
            foreach(var radio in new[]{iTalk_RadioButton1,iTalk_RadioButton2,iTalk_RadioButton3})radio.CheckedChanged+=s=>{if(!radio.Checked)return;foreach(var other in new[]{iTalk_RadioButton1,iTalk_RadioButton2,iTalk_RadioButton3})if(other!=radio)other.Checked=false;};
            editorLayout.Action("Fermer",Close,width:110);
            foreach(var box in new[]{iTalk_TextBox_Small3,iTalk_TextBox_Small4,iTalk_TextBox_Small5,iTalk_TextBox_Small6,iTalk_TextBox_Small7,iTalk_TextBox_Small8,iTalk_TextBox_Small10}) if(box.Text=="")box.Text="0";
            iTalk_NumericUpDown1.Minimum=1;iTalk_NumericUpDown1.Value=1;
            foreach(var number in new[]{iTalk_NumericUpDown1,iTalk_NumericUpDown2,iTalk_NumericUpDown3,iTalk_NumericUpDown4,iTalk_NumericUpDown5,iTalk_NumericUpDown6})number.Maximum=int.MaxValue;
            FormClosing+=(s,e)=>{if(_inventoryChanges.Count>0 && MessageBox.Show(this,"Abandonner les modifications d'inventaire en attente ?","Inventaire",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)e.Cancel=true;};
        }
        private void BuildInventoryLayout()
        {
            foreach(Control old in tabPage5.Controls)old.Visible=false;
            tabPage5.Text="Inventaire";tabPage5.BackColor=EditorUi.Background;tabPage5.Padding=new Padding(14);editorLayout.Tabs.TabPages.Add(tabPage5);
            var columns=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=3,RowCount=1 };
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,230));columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,55));columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,45));tabPage5.Controls.Add(columns);
            var players=new Panel { Dock=DockStyle.Fill,Padding=new Padding(0,0,14,0) };BoundEditorLayout.StyleList(listBox4);players.Controls.Add(listBox4);
            var playerSearch=BoundEditorLayout.FieldPanel(iTalk_TextBox_Small15,"Personnage","Rechercher un personnage.");playerSearch.Dock=DockStyle.Top;players.Controls.Add(playerSearch);columns.Controls.Add(players,0,0);
            var owned=new Panel { Dock=DockStyle.Fill,Padding=new Padding(0,0,14,0) };sfListView1.Visible=true;sfListView1.Dock=DockStyle.Fill;sfListView1.Font=Font;owned.Controls.Add(sfListView1);
            var title=EditorUi.Label("Objets du personnage — cochez pour retirer",10,true);title.Dock=DockStyle.Top;title.Height=35;owned.Controls.Add(title);
            var edit=EditorUi.Button("Modifier l'objet sélectionné",false,270);edit.Dock=DockStyle.Bottom;edit.Click+=(s,e)=>StageItemEdit();owned.Controls.Add(edit);columns.Controls.Add(owned,1,0);
            var models=new Panel { Dock=DockStyle.Fill };BoundEditorLayout.StyleList(listBox5);models.Controls.Add(listBox5);
            var search=BoundEditorLayout.FieldPanel(iTalk_TextBox_Small16,"Modèles à ajouter","Rechercher un modèle d'objet.");search.Dock=DockStyle.Top;models.Controls.Add(search);
            var quantity=BoundEditorLayout.FieldPanel(iTalk_NumericUpDown6,"Quantité","Pour plusieurs objets cochés, Retirer enlève tous leurs exemplaires.");quantity.Dock=DockStyle.Bottom;models.Controls.Add(quantity);
            iTalk_NumericUpDown6.Minimum=1;iTalk_NumericUpDown6.Value=1;
            var buttons=new FlowLayoutPanel { Dock=DockStyle.Bottom,Height=80,FlowDirection=FlowDirection.TopDown,WrapContents=false };
            var add=BoundEditorLayout.ActionView(iTalk_Button_12,"Ajouter à l'inventaire",245,true);var remove=BoundEditorLayout.ActionView(iTalk_Button_13,"Retirer les objets cochés",245);add.Margin=remove.Margin=new Padding(0,0,0,6);buttons.Controls.Add(add);buttons.Controls.Add(remove);models.Controls.Add(buttons);columns.Controls.Add(models,2,0);
            _inventoryStatus.Visible=true;_inventoryStatus.Dock=DockStyle.Bottom;_inventoryStatus.Height=38;_inventoryStatus.Font=Font;tabPage5.Controls.Add(_inventoryStatus);
            var pending=new TabPage("Changements d'inventaire") {BackColor=EditorUi.Background,Padding=new Padding(14)};editorLayout.Tabs.TabPages.Add(pending);
            listView1.Visible=true;listView1.Dock=DockStyle.Fill;listView1.Font=Font;listView1.FullRowSelect=true;listView1.GridLines=false;listView1.CheckBoxes=false;pending.Controls.Add(listView1);
            listView1.Columns[0].Width=360;listView1.Columns[1].Width=110;listView1.Columns[2].Width=120;listView1.Columns[3].Width=200;
            var guide=EditorUi.Label("Double-cliquez sur une ligne pour l'annuler. Enregistrer applique tous les changements ensemble.");guide.Dock=DockStyle.Top;guide.Height=40;pending.Controls.Add(guide);
            editorLayout.Action(iTalk_Button_23,"Enregistrer l'inventaire",205);
            editorLayout.Tabs.SelectedIndexChanged+=(s,e)=>{bool inventory=editorLayout.Tabs.SelectedTab==tabPage5 || editorLayout.Tabs.SelectedTab==pending;editorLayout.ShowAction(iTalk_Button_21,!inventory);editorLayout.ShowAction(iTalk_Button_23,inventory);};
            editorLayout.ShowAction(iTalk_Button_23,false);
            if(!string.IsNullOrWhiteSpace(_initialCharacterName))editorLayout.Tabs.SelectedTab=tabPage5;
        }
    }
}
