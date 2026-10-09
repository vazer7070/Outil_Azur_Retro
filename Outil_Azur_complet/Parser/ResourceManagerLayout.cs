using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Editors;
using Tool_Editor.maps.data;
using Tool_Editor.maps.managers;

namespace Outil_Azur_complet.Parser
{
    public partial class RessourceParser
    {
        private BoundEditorLayout layout;
        private iTalk.iTalk_TextBox_Small clientFolder;
        private bool working;
        private void BuildEditorLayout()
        {
            layout=new BoundEditorLayout(this,"Gestionnaire de ressources","Exportez les ressources, convertissez vos cartes ou analysez les échanges réseau du client de test.");
            layout.Field("Exporter XML",iTalk_ComboBox1,"Données à exporter","Choisissez le type de ressources.");
            layout.Field("Exporter XML",iTalk_TextBox_Small1,"Dossier de sortie","Dossier utilisé pour l'export complet.");
            layout.Field("Exporter XML",iTalk_RadioButton1,"Export complet","Conserver toutes les colonnes de la base.");
            layout.Field("Exporter XML",iTalk_RadioButton2,"Ressources du bot","Utiliser les formats et les dossiers attendus par AzurBot.");
            layout.Field("Exporter XML",iTalk_LinkLabel1,"Choisir le dossier","Parcourir les dossiers de votre ordinateur.");
            // Exports du serveur ajoutés pour le bot : interactifs, cellules déclencheurs (scripted_cells), zaapis ; fond des cartes lu dans le client.
            iTalk_ComboBox1.Items.AddRange(new object[]{"Interactifs","Déclencheurs","Zaapis"});
            clientFolder=EditorUi.TextBox();layout.Field("Exporter XML",clientFolder,"Dossier du client Dofus (facultatif)","Cartes du bot : lit le fond (backgroundNum) dans data/maps quand la base n'en a pas, comme StarLoco.");
            AddToolButton("Exporter XML","Choisir le dossier du client",()=>{using(var dialog=new FolderBrowserDialog())if(dialog.ShowDialog(this)==DialogResult.OK)clientFolder.Text=dialog.SelectedPath;});
            layout.Field("Exporter XML",iTalk_Button_21,"Générer XML","Exporter les ressources sélectionnées.");
            foreach(var radio in new[]{iTalk_RadioButton1,iTalk_RadioButton2})radio.CheckedChanged+=s=>{if(radio.Checked){if(radio==iTalk_RadioButton1)iTalk_RadioButton2.Checked=false;else iTalk_RadioButton1.Checked=false;}};
            var sql=EditorUi.Button("Exporter les données SQL",true,245);sql.Click+=async(s,e)=>await ExportSql();EditorUi.AddField(layout.Sheet("Exporter XML"),sql);
            BuildMapConverter();BuildImageTool();BuildMessageReader();BuildNetworkCapture();layout.Action(iTalk_Button_11,"Fermer",120);layout.Status.Text="Choisissez une rubrique. Chaque opération indique son résultat.";
            FormClosing+=(s,e)=>{if(working)e.Cancel=true;};
        }
        private async Task ExportSql()
        {
            var keys=new Dictionary<string,string>{{"Maps","cartes"},{"Objets","Template"},{"Sorts","sort"},{"Panoplies","panoplies"},{"Joueurs","perso"},{"Métiers","metiers"},{"Maisons","maisons"},{"Zaaps","zaaps"},{"PNJs","npc_template"},{"Monstres","monstres"},{"Interactifs","interactions"},{"Déclencheurs","cellule"},{"Zaapis","zaapi"}};
            string selection=iTalk_ComboBox1.SelectedItem as string;if(selection==null)return;
            using(var dialog=new SaveFileDialog{Filter="Données SQL (*.sql)|*.sql",DefaultExt="sql",FileName=selection+".sql"})
            if(dialog.ShowDialog(this)==DialogResult.OK)
            {
                working=true;layout.Body.Enabled=false;layout.Status.Text="Export SQL en cours…";
                try{int count=await Task.Run(()=>ServerDataService.ExportAllSql(keys[selection],dialog.FileName));layout.Status.Text=count+" ligne(s) exportée(s) en SQL. Ce fichier contient des INSERT pour une table vide.";}
                catch(Exception error){MessageBox.Show(this,error.Message,"Export impossible");layout.Status.Text=error.Message;}
                finally{working=false;layout.Body.Enabled=true;}
            }
        }
        private void BuildMapConverter()
        {
            iTalk.iTalk_TextBox_Small id=EditorUi.TextBox("1"),width=EditorUi.TextBox("15"),height=EditorUi.TextBox("17"),key=EditorUi.TextBox(),data=EditorUi.TextBox("",true);
            data.MaxLength=400000;
            layout.Field("Cartes SWF / AME",id,"Identifiant de carte","Numéro positif de la carte.");layout.Field("Cartes SWF / AME",width,"Largeur","Entre 2 et 100.");layout.Field("Cartes SWF / AME",height,"Hauteur","Entre 2 et 100.");layout.Field("Cartes SWF / AME",key,"Clef de déchiffrement","Clef hexadécimale du serveur ; vide si les données sont en clair.");layout.Field("Cartes SWF / AME",data,"Données de la carte","Données du client ; 10 caractères par cellule en clair.",multiline:true);
            Tool_Editor.maps.data.Map imported=null;
            AddToolButton("Cartes SWF / AME","Ouvrir SWF ou AME",()=>{using(var dialog=new OpenFileDialog{Filter="Cartes (*.swf;*.ame)|*.swf;*.ame"})if(dialog.ShowDialog(this)==DialogResult.OK){var candidate=Path.GetExtension(dialog.FileName).Equals(".ame",StringComparison.OrdinalIgnoreCase)?MapProjectSerializer.Load(dialog.FileName):MapSwfSerializer.Load(dialog.FileName);string candidateData=candidate.HasProjectCells?string.Concat(candidate.Cells.Select(BuilderClass.GetCellData)):candidate.MapData;imported=candidate;id.Text=imported.ID.ToString();width.Text=imported.Width.ToString();height.Text=imported.Height.ToString();key.Text=imported.HasProjectCells?"":imported.Key;data.Text=candidateData;layout.Status.Text="Carte ouverte. Les fichiers SWF sont lus sans exécuter leurs scripts.";}});
            Func<Tool_Editor.maps.data.Map> read=()=>{int mapId,w,h;if(!int.TryParse(id.Text,out mapId)||!int.TryParse(width.Text,out w)||!int.TryParse(height.Text,out h))throw new FormatException("Identifiant ou dimensions invalides.");return ResourceMapConversion.Prepare(imported,mapId,w,h,data.Text,key.Text);};
            AddToolButton("Cartes SWF / AME","Déchiffrer les données",()=>{int w,h;if(!int.TryParse(width.Text,out w)||!int.TryParse(height.Text,out h))throw new FormatException("Dimensions invalides.");string clear=ResourceMapConversion.Decrypt(data.Text,key.Text,w,h);data.Text=clear;key.Text="";layout.Status.Text="Données déchiffrées et vérifiées : "+Tool_Editor.maps.data.Map.CellCount(w,h)+" cellules.";});
            AddToolButton("Cartes SWF / AME","Exporter la carte SWF",()=>{var map=read();using(var dialog=new SaveFileDialog{Filter="Carte SWF (*.swf)|*.swf",DefaultExt="swf",FileName=map.ID+"_"+map.DateMap+".swf"})if(dialog.ShowDialog(this)==DialogResult.OK){MapSwfSerializer.Save(dialog.FileName,map);layout.Status.Text="Carte SWF exportée.";}});
            AddToolButton("Cartes SWF / AME","Enregistrer le projet AME",()=>{var map=read();using(var dialog=new SaveFileDialog{Filter="Projet AME (*.ame)|*.ame",DefaultExt="ame",FileName=map.ID+".ame"})if(dialog.ShowDialog(this)==DialogResult.OK){MapProjectSerializer.Save(dialog.FileName,map);layout.Status.Text="Projet AME enregistré.";}});
        }
        private void BuildImageTool()
        {
            var preview=new PictureBox{SizeMode=PictureBoxSizeMode.Zoom,BackColor=Color.White};layout.Field("Images",preview,"Aperçu de l'image","Choisissez une image locale à ajouter à votre bibliothèque.",multiline:true);preview.Parent.Height=270;preview.Height=210;
            iTalk.iTalk_TextBox_Small number=EditorUi.TextBox(),folder=EditorUi.TextBox(Path.GetFullPath(@".\ressources"));layout.Field("Images",number,"Identifiant de l'image","Nom numérique utilisé par les ressources d'Azur.");layout.Field("Images",folder,"Dossier de la bibliothèque","Choisissez le dossier correspondant au type de ressource.");
            AddToolButton("Images","Choisir une image",()=>{using(var dialog=new OpenFileDialog{Filter="Images (*.png;*.jpg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp"})if(dialog.ShowDialog(this)==DialogResult.OK){using(var source=Image.FromFile(dialog.FileName)){if(source.Width>10000||source.Height>10000)throw new FormatException("Image trop grande.");var old=preview.Image;preview.Image=new Bitmap(source);old?.Dispose();}}});
            AddToolButton("Images","Choisir le dossier",()=>{using(var dialog=new FolderBrowserDialog())if(dialog.ShowDialog(this)==DialogResult.OK)folder.Text=dialog.SelectedPath;});
            AddToolButton("Images","Enregistrer en PNG",()=>{int imageId;if(preview.Image==null||!int.TryParse(number.Text,out imageId)||imageId<0)throw new FormatException("Choisissez une image et un identifiant numérique.");string target=Path.Combine(Path.GetFullPath(folder.Text),imageId+".png");Directory.CreateDirectory(Path.GetDirectoryName(target));if(File.Exists(target)&&MessageBox.Show(this,"Remplacer l'image existante "+target+" ?","Image existante",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;string temp=target+".tmp-"+Guid.NewGuid().ToString("N");try{preview.Image.Save(temp,System.Drawing.Imaging.ImageFormat.Png);if(File.Exists(target))File.Replace(temp,target,null);else File.Move(temp,target);}finally{if(File.Exists(temp))File.Delete(temp);}layout.Status.Text="Image enregistrée : "+target;});
            Disposed+=(s,e)=>preview.Image?.Dispose();
        }
        private void BuildMessageReader()
        {
            iTalk.iTalk_TextBox_Small filter=EditorUi.TextBox(),output=EditorUi.TextBox("",true);output.ReadOnly=true;layout.Field("Messages",filter,"Filtrer les messages","Texte à rechercher dans un fichier de messages ou un journal.");layout.Field("Messages",output,"Messages du fichier","Affichage limité aux 5 000 premières lignes correspondantes.",multiline:true);output.Parent.Height=470;output.Height=410;
            string path=null;Action refresh=()=>{if(path!=null)output.Text=string.Join(Environment.NewLine,File.ReadLines(path).Where(line=>line.IndexOf(filter.Text,StringComparison.OrdinalIgnoreCase)>=0).Take(5000));};
            AddToolButton("Messages","Ouvrir un journal",()=>{using(var dialog=new OpenFileDialog{Filter="Journaux et textes (*.log;*.txt)|*.log;*.txt|Tous les fichiers|*.*"})if(dialog.ShowDialog(this)==DialogResult.OK){if(new FileInfo(dialog.FileName).Length>20*1024*1024)throw new FormatException("Le journal dépasse 20 Mo.");path=dialog.FileName;refresh();layout.Status.Text="Messages ouverts depuis "+path;}});AddToolButton("Messages","Appliquer le filtre",refresh);
        }
        private void AddToolButton(string section,string text,Action action)
        {var button=EditorUi.Button(text,true,260);button.Click+=(s,e)=>{try{action();}catch(Exception error){layout.Status.Text=error.Message;MessageBox.Show(this,error.Message,"Action impossible",MessageBoxButtons.OK,MessageBoxIcon.Warning);}};EditorUi.AddField(layout.Sheet(section),button);}
    }
}
