using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using Outil_Azur_complet.Editors;
using Tool_Editor.maps.managers;

namespace Outil_Azur_complet.maps
{
    public partial class SaveForm : Form
    {
        private readonly MapForm map;
        private readonly BoundEditorLayout layout;
        private bool busy;
        public SaveForm() : this(null) { }
        public SaveForm(MapForm map)
        {
            InitializeComponent();
            this.map=map;
            layout=new BoundEditorLayout(this,"Enregistrer et exporter la carte","Le projet AME conserve tous les paramètres. Ajoutez les exports client SWF ou serveur SQL selon vos besoins.");
            layout.Field("Formats",iTalk_CheckBox1,"Carte du client (SWF)","Exporter un fichier SWF binaire en plus du projet AME.");
            iTalk_CheckBox2.Text="Exporter SQL";layout.Field("Formats",iTalk_CheckBox2,"Carte du serveur (SQL)","Exporter les changements de la carte selon le schéma de votre base.");
            layout.Action(iTalk_Button_21,"Enregistrer et exporter",225);layout.Action(iTalk_Button_11,"Annuler",120);
            layout.Status.Text=map==null?"Ouvrez cet écran depuis une carte pour enregistrer ses données.":"Carte #"+map.ID+" · Le projet AME sera toujours enregistré.";
            EditorUi.PrepareWindow(this,Text,new Size(800,510),new Size(720,470));
            iTalk_Button_21.Enabled=map!=null;FormClosing+=(s,e)=>{if(busy)e.Cancel=true;};
        }

        private void iTalk_Button_11_Click(object sender, EventArgs e)
        {
            Close();
        }

        private async void iTalk_Button_21_Click(object sender, EventArgs e)
        {
            if(map==null)return;
            if(map.MdiParent is MainEditeur owner && !owner.CommitPropertiesBeforeClose(map))return;
            using(var dialog=new SaveFileDialog{Filter="Projet Azur (*.ame)|*.ame",DefaultExt="ame",FileName=map.ID+"_"+map.MyMap.DateMap+".ame"})
            {
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                bool swf=iTalk_CheckBox1.Checked,sql=iTalk_CheckBox2.Checked;string path=dialog.FileName;busy=true;layout.Body.Enabled=false;iTalk_Button_21.Enabled=false;
                try
                {
                    if(sql)await Task.Run(()=>MapSqlExport.Export(map.MyMap,Path.ChangeExtension(path,"sql")));
                    if(swf)MapSwfSerializer.Save(Path.ChangeExtension(path,"swf"),map.MyMap);
                    MapProjectSerializer.Save(path,map.MyMap);map.Edited=false;layout.Status.Text="Projet AME enregistré"+(swf?" · SWF exporté":"")+(sql?" · SQL exporté":"")+".";
                }
                catch(Exception error){layout.Status.Text=error.Message;MessageBox.Show(this,error.Message,"Export interrompu",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
                finally{busy=false;layout.Body.Enabled=true;iTalk_Button_21.Enabled=true;}
            }
        }
    }
}
