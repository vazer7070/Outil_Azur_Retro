using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Outil_Azur_complet.Editors;
using Tool_Editor.maps.data;

namespace Outil_Azur_complet.maps
{
    public partial class MainEditeur
    {
        private ComboBox propertySection;
        private FlowLayoutPanel documentTabs;
        private Label workspaceStatus;
        private Panel emptyWorkspace;
        private readonly CheckBox flipGround=new CheckBox{Text="Retourner le sol"},flipFirst=new CheckBox{Text="Retourner le premier calque"},flipSecond=new CheckBox{Text="Retourner le second calque"};
        private readonly NumericUpDown capabilitiesMask=new NumericUpDown{Name="capabilitiesMask",Minimum=0,Maximum=int.MaxValue,ThousandsSeparator=true};
        private readonly ComboBox rotateGround=new EditorComboBox{DropDownStyle=ComboBoxStyle.DropDownList},rotateFirst=new EditorComboBox{DropDownStyle=ComboBoxStyle.DropDownList},fightTeam=new EditorComboBox{DropDownStyle=ComboBoxStyle.DropDownList};
        private void BuildEditorLayout()
        {
            EditorUi.PrepareWindow(this,"Azur · Éditeur de cartes",new Size(1540,960),new Size(1180,700));
            tabControlAdv1.Visible=false;
            var propertyTabs=new PropertyPages{Dock=DockStyle.Fill};
            var properties=new Panel{Dock=DockStyle.Right,Width=360,Padding=new Padding(12),BackColor=EditorUi.Background};Controls.Add(properties);
            propertySection=new EditorComboBox{Dock=DockStyle.Top,DropDownStyle=ComboBoxStyle.DropDownList,Font=Font,Height=34};
            propertySection.Items.AddRange(new object[]{"Carte · identité et position","Zones et sous-zones","Ambiance et arrière-plan","Groupes de monstres","Autorisations de la carte","Cellule · déplacement","Tuiles · calques","Orientation et combat"});
            propertySection.SelectedIndexChanged+=(s,e)=>{if(propertySection.SelectedIndex>=0)propertyTabs.SelectedIndex=propertySection.SelectedIndex;};
            properties.Controls.Add(propertyTabs);properties.Controls.Add(new Panel{Dock=DockStyle.Top,Height=12});properties.Controls.Add(propertySection);
            var propertyHeading=EditorUi.Label("PROPRIÉTÉS",9,true);propertyHeading.Dock=DockStyle.Top;propertyHeading.Height=28;properties.Controls.Add(propertyHeading);
            var title=new EditorTheme{Dock=DockStyle.Top,Height=42,Text=Text,Sizable=true,SmartBounds=false,Padding=new Padding(0)};Controls.Add(title);
            var close=EditorUi.Button("×",false,34);close.SetBounds(Width-42,5,34,30);close.Anchor=AnchorStyles.Top|AnchorStyles.Right;close.Click+=(s,e)=>Close();title.Controls.Add(close);
            title.SizeChanged+=(s,e)=>close.Left=title.ClientSize.Width-42;close.Left=title.ClientSize.Width-42;
            var palette=new Panel {Dock=DockStyle.Bottom,Height=190,Padding=new Padding(12),BackColor=Color.White};Controls.Add(palette);
            var libraryHeading=EditorUi.Label("BIBLIOTHÈQUE DE TUILES     Choisissez une catégorie, puis une tuile pour la peindre sur la carte.",9,true);libraryHeading.Dock=DockStyle.Top;libraryHeading.Height=30;palette.Controls.Add(libraryHeading);
            var library=new Panel{Dock=DockStyle.Fill};palette.Controls.Add(library);library.BringToFront();
            treeView1.Dock=DockStyle.Left;treeView1.Width=175;treeView1.Font=Font;treeView1.BorderStyle=BorderStyle.None;library.Controls.Add(treeView1);
            listView1.Dock=DockStyle.Fill;listView1.Font=Font;listView1.BorderStyle=BorderStyle.None;library.Controls.Add(listView1);listView1.BringToFront();
            foreach(var strip in new[]{toolStrip1,toolStrip2}) { strip.BackColor=Color.White;strip.ForeColor=EditorUi.Ink;strip.Font=Font;strip.Renderer=new EditorToolStripRenderer();strip.GripStyle=ToolStripGripStyle.Hidden;foreach(ToolStripItem item in strip.Items){item.DisplayStyle=ToolStripItemDisplayStyle.Text;item.ToolTipText=item.Text;item.Margin=new Padding(4,4,4,4);item.Padding=new Padding(8,4,8,4);} }
            toolStrip1.AutoSize=false;toolStrip1.Height=46;toolStrip2.AutoSize=false;toolStrip2.Width=174;
            foreach(ToolStripItem item in toolStrip2.Items)if(!(item is ToolStripSeparator)){item.AutoSize=false;item.Size=new Size(164,36);item.TextAlign=ContentAlignment.MiddleLeft;}
            toolStripButton5.Text="Peindre";toolStripButton6.Text="Sélectionner";toolStripButton7.Text="Bloquer le passage";toolStripButton15.Text="Retourner";toolStripButton11.Text="Combat · bleu";toolStripButton12.Text="Combat · rouge";
            afficherCellIDToolStripMenuItem.Text="Numéros des cellules";afficherFightCellsToolStripMenuItem.Text="Placements de combat";
            toolStripButton3.Text="Préférences";triggersToolStripMenuItem.Text="Créer une téléportation";donjonsToolStripMenuItem.Text="Créer une sortie de combat";
            géopositionToolStripMenuItem.Text="Position de la carte";géopositionToolStripMenuItem.Click+=(s,e)=>propertyTabs.SelectedIndex=0;
            puzzlesToolStripMenuItem.Text="Portes et mécanismes";puzzlesToolStripMenuItem.Click+=(s,e)=>new ServerDataForm(ServerResourceKind.InteractiveDoors,"Portes et mécanismes").Show(this);
            gFXToolStripMenuItem.Text="Bibliothèque de tuiles";gFXToolStripMenuItem.Click+=(s,e)=>treeView1.Focus();
            var triggers=new ToolStripMenuItem("Éditer les téléportations existantes");triggers.Click+=(s,e)=>OpenServerPlacement(ServerResourceKind.MapTriggers,"Téléportations de la carte");
            var endfight=new ToolStripMenuItem("Éditer les actions de fin de combat");endfight.Click+=(s,e)=>OpenServerPlacement(ServerResourceKind.EndFightActions,"Actions de fin de combat");
            var dungeons=new ToolStripMenuItem("Entrées de donjons");dungeons.Click+=(s,e)=>OpenServerPlacement(ServerResourceKind.Dungeons,"Entrées de donjons");
            toolStripDropDownButton2.DropDownItems.AddRange(new ToolStripItem[]{triggers,endfight,dungeons});
            var formats=new ToolStripButton("AME / SWF / SQL"){DisplayStyle=ToolStripItemDisplayStyle.Text};formats.Click+=(s,e)=>{var selected=ActiveMdiChild as MapForm ?? MapSelected;if(selected!=null)using(var dialog=new SaveForm(selected))dialog.ShowDialog(this);};toolStrip1.Items.Add(formats);
            documentTabs=new FlowLayoutPanel{Dock=DockStyle.Top,Height=44,Padding=new Padding(8,5,8,4),WrapContents=false,AutoScroll=true,BackColor=EditorUi.Background};Controls.Add(documentTabs);
            workspaceStatus=EditorUi.Label("Créez ou ouvrez une carte pour commencer.",9);workspaceStatus.Dock=DockStyle.Bottom;workspaceStatus.Height=28;workspaceStatus.Padding=new Padding(14,4,0,0);Controls.Add(workspaceStatus);
            MdiChildActivate+=(s,e)=>UpdateWorkspace();
            foreach(Control old in tabPageAdv1.Controls)old.Visible=false;
            foreach(Control old in tabPageAdv2.Controls)old.Visible=false;
            tabPageAdv1.Text="Carte";tabPageAdv1.BackColor=EditorUi.Background;tabPageAdv2.BackColor=EditorUi.Background;
            var map=EditorUi.Sheet();EditorUi.Page(propertyTabs,"Carte",map);
            var zones=EditorUi.Sheet();EditorUi.Page(propertyTabs,"Zones",zones);
            var appearance=EditorUi.Sheet();EditorUi.Page(propertyTabs,"Ambiance",appearance);
            var groups=EditorUi.Sheet();EditorUi.Page(propertyTabs,"Groupes",groups);
            var rules=EditorUi.Sheet();EditorUi.Page(propertyTabs,"Autorisations",rules);
            MapField(map,iTalk_NumericUpDown1,"Identifiant de la carte","Numéro unique de la carte.");
            MapField(map,iTalk_TextBox_Small2,"Version de la carte","Identifiant de version utilisé dans le fichier client.");
            MapField(map,iTalk_NumericUpDown9,"Position X","Coordonnée horizontale sur la carte du monde.");
            MapField(map,iTalk_NumericUpDown10,"Position Y","Coordonnée verticale sur la carte du monde.");
            MapField(zones,iTalk_NumericUpDown8,"Région principale","Identifiant de la grande région.");
            MapField(zones,sfComboBox1,"Zone","Identifiant de la zone.");
            MapField(zones,sfComboBox2,"Sous-zone","Identifiant de la sous-zone.");
            MapField(appearance,iTalk_NumericUpDown5,"Ambiance","Identifiant de l'ambiance du client.");
            MapField(appearance,iTalk_ComboBox2,"Musique","Musique associée à la carte.");
            MapField(groups,iTalk_NumericUpDown6,"Nombre de groupes","Nombre maximal de groupes de monstres.");
            MapField(groups,iTalk_NumericUpDown7,"Taille maximale des groupes","Nombre maximal de monstres par groupe.");
            MapField(rules,capabilitiesMask,"Valeur complète des autorisations","Valeur numérique du SWF ; 98 sur la plupart des cartes Nowel. Les cases ci-dessous règlent les quatre premiers bits.");
            foreach(var check in new[]{iTalk_CheckBox1,iTalk_CheckBox2,iTalk_CheckBox3,iTalk_CheckBox4,iTalk_CheckBox5})MapField(rules,check,check==iTalk_CheckBox1?"Carte en extérieur":check.Text,"Activez les possibilités offertes sur cette carte.");
            capabilitiesMask.ValueChanged+=(s,e)=>{int mask=(int)capabilitiesMask.Value;iTalk_CheckBox2.Checked=(mask&1)==0;iTalk_CheckBox3.Checked=(mask&2)==0;iTalk_CheckBox4.Checked=(mask&4)==0;iTalk_CheckBox5.Checked=(mask&8)==0;};
            MapField(appearance,pictureBox1,"Arrière-plan","Aperçu du fond de la carte.",true);
            MapField(appearance,iTalk_Button_21,"Choisir un arrière-plan","Ouvrir la bibliothèque des fonds.");
            MapField(appearance,iTalk_Button_22,"Retirer l'arrière-plan","Supprimer le fond de cette carte.");
            MapField(map,iTalk_Button_23,"Fermer cette carte","Fermer la carte active après vérification des changements.");
            var cell=EditorUi.Sheet();EditorUi.Page(propertyTabs,"Cellule",cell);
            var tiles=EditorUi.Sheet();EditorUi.Page(propertyTabs,"Tuiles",tiles);
            var orientation=EditorUi.Sheet();EditorUi.Page(propertyTabs,"Orientation",orientation);
            MapField(cell,iTalk_Label19,"Cellule sélectionnée","Cliquez sur une cellule avec l'outil Sélectionner.");
            MapField(cell,activeCellCheckBox,"Cellule active","Activer cette cellule dans les données du client.");
            MapField(cell,movementComboBox,"Déplacement","Type de passage autorisé par le serveur.");
            MapField(cell,iTalk_NumericUpDown3,"Niveau du sol","Hauteur comprise entre 0 et 15.");
            MapField(cell,iTalk_NumericUpDown4,"Pente du sol","Inclinaison comprise entre 0 et 15.");
            foreach(var check in new[]{iTalk_CheckBox9,iTalk_CheckBox12})MapField(cell,check,check==iTalk_CheckBox12?"Objet interactif":"Ligne de vue","Modification appliquée immédiatement à la cellule sélectionnée.");
            // Movement flags are alternative representations of the same value.
            // Keep their bound controls hidden, and expose the complete movement choice.
            foreach(var tuple in new[]{Tuple.Create((Control)pictureBox3,(Control)iTalk_Label21,(Control)sfButton1,"Sol"),Tuple.Create((Control)pictureBox4,(Control)iTalk_Label23,(Control)sfButton2,"Premier calque"),Tuple.Create((Control)pictureBox5,(Control)iTalk_Label25,(Control)sfButton3,"Second calque")})
            {MapField(tiles,tuple.Item1,tuple.Item4,"Aperçu de la tuile.",true);MapField(tiles,tuple.Item2,"Identifiant — "+tuple.Item4,"Identifiant de la tuile placée.");MapField(tiles,tuple.Item3,"Retirer — "+tuple.Item4,"Retirer cette tuile de la cellule.");}
            foreach(var combo in new[]{rotateGround,rotateFirst}){combo.Items.AddRange(new object[]{"0°","90°","180°","270°"});combo.SelectedIndex=0;combo.SelectedIndexChanged+=(s,e)=>ApplyOrientation();}
            fightTeam.Items.AddRange(new object[]{"Aucune équipe","Équipe bleue","Équipe rouge"});fightTeam.SelectedIndex=0;fightTeam.SelectedIndexChanged+=(s,e)=>ApplyOrientation();
            foreach(var check in new[]{flipGround,flipFirst,flipSecond}){check.CheckedChanged+=(s,e)=>ApplyOrientation();MapField(orientation,check,check.Text,"Modification de la cellule sélectionnée.");}
            MapField(orientation,rotateGround,"Rotation du sol","Orientation de la tuile de sol.");MapField(orientation,rotateFirst,"Rotation du premier calque","Orientation de la tuile du premier calque.");MapField(orientation,fightTeam,"Placement de combat","Équipe pouvant commencer sur cette cellule.");
            propertySection.SelectedIndex=0;
            foreach(MdiClient client in Controls.OfType<MdiClient>())
            {
                client.BackColor=EditorUi.Background;
                emptyWorkspace=new Panel{BackColor=EditorUi.Background};Controls.Add(emptyWorkspace);
                Layout+=(s,e)=>emptyWorkspace.Bounds=client.Bounds;
                var welcome=new Panel{Size=new Size(480,210),BackColor=Color.White,Padding=new Padding(24)};
                var heading=EditorUi.Label("Votre prochaine carte commence ici",17,true);heading.Dock=DockStyle.Top;heading.Height=46;
                var help=EditorUi.Label("Créez une carte ou ouvrez un projet AME / SWF.\nLes tuiles restent en bas, les propriétés à droite.");help.Dock=DockStyle.Top;help.Height=64;
                var actions=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=44};var create=EditorUi.Button("Créer une carte",true,180);create.Click+=(s,e)=>OpenNewMap(15,17);var open=EditorUi.Button("Ouvrir un projet",false,180);open.Click+=(s,e)=>sWFToolStripMenuItem_Click(s,e);actions.Controls.Add(create);actions.Controls.Add(open);
                welcome.Controls.Add(help);welcome.Controls.Add(heading);welcome.Controls.Add(actions);emptyWorkspace.Controls.Add(welcome);
                emptyWorkspace.Resize+=(s,e)=>welcome.Location=new Point(Math.Max(12,(emptyWorkspace.Width-welcome.Width)/2),Math.Max(12,(emptyWorkspace.Height-welcome.Height)/2));
                client.Resize+=(s,e)=>{var active=ActiveMdiChild as MapForm;if(active!=null)active.Bounds=new Rectangle(Point.Empty,client.ClientSize);};
            }
            Controls.SetChildIndex(title,Controls.Count-1);Controls.SetChildIndex(toolStrip1,Controls.Count-2);Controls.SetChildIndex(documentTabs,Controls.Count-3);
            UpdateWorkspace();
        }
        private void UpdateWorkspace()
        {
            if(documentTabs==null)return;
            var active=ActiveMdiChild as MapForm;
            emptyWorkspace.Visible=active==null;toolStrip2.Enabled=active!=null;
            serverPlacementsButton.Enabled=active!=null;
            serverPlacementsButton.Checked=active?.ShowServerPlacements==true;
            foreach(Control old in documentTabs.Controls.Cast<Control>().ToArray())old.Dispose();
            foreach(var map in MdiChildren.OfType<MapForm>().Where(form=>!form.IsDisposed))
            {
                var button=EditorUi.Button("Carte "+map.ID+(map.Edited?" •":""),map==active,150);button.Height=30;button.Click+=(s,e)=>map.Activate();documentTabs.Controls.Add(button);
                var close=EditorUi.Button("×",false,30);close.Height=30;close.AccessibleName="Fermer la carte "+map.ID;close.Click+=(s,e)=>map.Close();documentTabs.Controls.Add(close);
            }
            if(active!=null)
            {
                active.FormBorderStyle=FormBorderStyle.None;active.Dock=DockStyle.Fill;
                var client=Controls.OfType<MdiClient>().First();active.Bounds=new Rectangle(Point.Empty,client.ClientSize);
                workspaceStatus.Text=active.ShowServerPlacements && active.ServerPlacements!=null
                    ? "Carte "+active.ID+" · "+active.ServerPlacements.Summary+" · survolez un repère pour le détail ; éditez les fiches dans Placements."
                    : "Carte "+active.ID+" · "+active.W+" × "+active.H+" · clic gauche : appliquer · clic droit : retirer · sélectionnez une cellule pour modifier ses propriétés.";
                active.FitCanvas();
            }
        }
        private void PopulateOrientation(CellsData cell)
        {flipGround.Checked=cell.FlipGFX1;flipFirst.Checked=cell.FlipGFX2;flipSecond.Checked=cell.FlipGFX3;rotateGround.SelectedIndex=cell.RotaGFX1;rotateFirst.SelectedIndex=cell.RotaGFX2;fightTeam.SelectedIndex=cell.FightCell;}
        private void ApplyOrientation()
        {
            if(loadingCellProperties || selectedCellMap?.MyMap?.Cells==null || selectedCellId<0)return;
            var cell=selectedCellMap.MyMap.Cells[selectedCellId];if(fightTeam.SelectedIndex>0 && cell.UnWalk){fightTeam.SelectedIndex=0;return;}
            cell.FlipGFX1=flipGround.Checked;cell.FlipGFX2=flipFirst.Checked;cell.FlipGFX3=flipSecond.Checked;cell.RotaGFX1=Math.Max(0,rotateGround.SelectedIndex);cell.RotaGFX2=Math.Max(0,rotateFirst.SelectedIndex);cell.FightCell=Math.Max(0,fightTeam.SelectedIndex);selectedCellMap.Edited=true;selectedCellMap.DrawAll();
        }
        private static void MapField(TableLayoutPanel sheet,Control control,string label,string help,bool large=false)
        { var panel=BoundEditorLayout.FieldPanel(control,label,help,multiline:large);EditorUi.AddField(sheet,panel); }
    }
    internal sealed class EditorToolStripRenderer : ToolStripProfessionalRenderer
    {
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            var button=e.Item as ToolStripButton;
            using(var background=new SolidBrush(button!=null&&button.Checked?Color.FromArgb(222,237,252):e.Item.Selected?Color.FromArgb(235,242,250):Color.White))e.Graphics.FillRectangle(background,new Rectangle(Point.Empty,e.Item.Size));
        }
    }
    internal sealed class MapPreferencesForm : AzurEditorWindow
    {
        private readonly CheckBox fixedSize;
        internal bool LockSize { get { return fixedSize.Checked; } }
        internal MapPreferencesForm(bool locked):base("Affichage des cartes","Choisissez comment la carte utilise la zone de travail.",new Size(780,480))
        {
            MinimumSize=new Size(640,420);Size=new Size(780,480);
            var sheet=EditorUi.Sheet();fixedSize=new CheckBox{Text="Conserver la taille actuelle des cellules",Checked=locked,AutoSize=true,Font=Font};
            EditorUi.AddField(sheet,BoundEditorLayout.FieldPanel(fixedSize,"Taille des cellules","Désactivé : la carte s'ajuste à la zone de travail."));Body.Controls.Add(sheet);
            SetStatus("La grille et les calques se règlent dans le menu Affichage.");
            ActionButton("Appliquer",()=>{DialogResult=DialogResult.OK;Close();},true);
            ActionButton("Annuler",()=>{DialogResult=DialogResult.Cancel;Close();});
        }
    }
    public partial class OtherSizeForm
    {
        private void BuildEditorLayout()
        {
            var layout=new BoundEditorLayout(this,"Nouvelle carte","Choisissez une largeur et une hauteur entre 2 et 100.");
            layout.Field("Dimensions",iTalk_TextBox_Small1,"Largeur","Nombre de cellules sur la largeur.");
            layout.Field("Dimensions",iTalk_TextBox_Small2,"Hauteur","Nombre de rangées de la carte.");
            layout.Status.Text="Une carte standard utilise une largeur de 15 et une hauteur de 17.";
            layout.Action(iTalk_Button_21,"Créer la carte",180);layout.Action(iTalk_Button_11,"Annuler",120);
            MinimumSize=new Size(640,420);Size=new Size(780,480);
        }
    }
    public partial class BG_Select
    {
        private void BuildEditorLayout()
        {
            var layout=new BoundEditorLayout(this,"Arrière-plan de la carte","Choisissez un fond dans la bibliothèque, puis appliquez-le à la carte.");
            layout.Body.Controls.Clear();iTalk_Listview1.Dock=DockStyle.Fill;iTalk_Listview1.Visible=true;iTalk_Listview1.Font=Font;layout.Body.Controls.Add(iTalk_Listview1);
            layout.Status.Text="Les images disponibles proviennent des ressources de cartes d'Azur.";
            layout.Action(iTalk_Button_21,"Appliquer le fond",185);layout.Action(iTalk_Button_11,"Annuler",120);
        }
    }
}
