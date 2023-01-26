
using Outil_Azur_complet.Bot;
using Outil_Azur_complet.Parser;
using System;
using System.IO;
using System.Windows.Forms;
using Tools_protocol;
using Tools_protocol.Json;

namespace Outil_Azur_complet
{
    public partial class Menu : Form
    {
        public Menu()
        {
            InitializeComponent();
        }

        private void iTalk_Button_11_Click(object sender, EventArgs e)
        {
            switch (iTalk_ComboBox1.SelectedItem)
            {
                case "Éditeur de compte":
                    if (InitializeForm.NoDB == false)
                    {
                        editeur_compte.editeurcompte ec = new editeur_compte.editeurcompte();
                        ec.Show();
                    }
                    break;
                case "Éditeur de personnage":
                    if (InitializeForm.NoDB == false)
                    {
                        editeur_perso.editeur_perso ep = new editeur_perso.editeur_perso();
                        ep.Show();
                    }
                    break;
                case "Outil de recherche":
                    if (InitializeForm.NoDB == false)
                    {
                        outil_recherche.Recherche search = new outil_recherche.Recherche();
                        search.Show();
                    }
                    break;
                case "Éditeur d'objets":
                    if (InitializeForm.NoDB == false)
                    {
                        editeur_items.itemeditor ie = new editeur_items.itemeditor();
                        ie.Show();
                    }
                    break;
                case "Éditeur de maps":
                     maps.MainEditeur map = new maps.MainEditeur();
                    if (InitializeForm.MapEditorOK)
                    {
                       map.Show();
                    }
                    else
                    {
                        MessageBox.Show("Il manque des dossiers pour une utilisation correcte de l'éditeur de cartes, merci de vérifier l'existence/chemins des dossiers pré-requis puis de relancer l'application afin de pouvoir lancer cette partie de l'application.", "Lancement impossible", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    break;
                case "Gestionnaire":
                        RessourceParser RP = new RessourceParser();
                    RP.Show();
                    break;
                case "AzurBot":
                    /* MenuP_Bot MP = new MenuP_Bot();
                     MP.Show();*/
                    LoginForm LF = new LoginForm();
                    LF.Show();
                    break;
            }
        }
       
        private void Menu_Load(object sender, EventArgs e)
        {
            foreach(string i in iTalk_ComboBox1.Items)
            {
                if (string.IsNullOrEmpty(i))
                {
                    iTalk_ComboBox1.Items.Remove(i);
                }
            }
        }

        private void iTalk_Button_12_Click(object sender, EventArgs e)
        {
            try
            {
                if (Application.OpenForms.Count == 0) Application.ExitThread();
                else
                {
                    foreach (Form F in Application.OpenForms)
                        F.Close();
                }
            }
            catch { }
        }

        private void iTalk_ThemeContainer1_Click(object sender, EventArgs e)
        {

        }

        private void iTalk_Button_13_Click(object sender, EventArgs e)
        {
            SettingsForm SF = new SettingsForm();
            SF.ShowDialog();
        }
    }
}
