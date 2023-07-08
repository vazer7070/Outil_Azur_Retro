using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tools_protocol.Parser.XML;

namespace Outil_Azur_complet.Parser
{
    public partial class RessourceParser : Form
    {
        public RessourceParser()
        {
            InitializeComponent();
        }

        private void iTalk_Button_11_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void iTalk_RadioButton2_CheckedChanged(object sender)
        {
            if(iTalk_RadioButton2.Checked == true)
            {
                iTalk_TextBox_Small1.Enabled = false;
            }
            else
            {
                iTalk_TextBox_Small1.Enabled = true;
            }
        }

        private void iTalk_Button_21_Click(object sender, EventArgs e)
        {

            if (!InitializeForm.NoDB)
            {
               if(iTalk_RadioButton2.Checked  == true)
                {

                    if(iTalk_ComboBox1.SelectedItem.ToString() == "Maps")
                    {
                        XmlParser.ParseSQLToXML(@".\ressources\Bot\BotMaps\", "Maps", true);

                    }else if(iTalk_ComboBox1.SelectedItem.ToString() == "Objets")
                    {
                        XmlParser.ParseSQLToXML(@".\ressources\Bot\BotObjets\", "Objets", true);
                    }
                    else if (iTalk_ComboBox1.SelectedItem.ToString() == "Métiers")
                    {
                        XmlParser.ParseSQLToXML(@".\ressources\Bot\BotJobs\", "Métiers", true);
                    }
                    else if (iTalk_ComboBox1.SelectedItem.ToString() == "PNJs")
                    {
                        XmlParser.ParseSQLToXML(@".\ressources\Bot\BotNPCs\", "PNJs", true);
                    }
                    else if (iTalk_ComboBox1.SelectedItem.ToString() == "Zaaps")
                    {
                        XmlParser.ParseSQLToXML(@".\ressources\Bot\BotZaaps\", "Zaaps", true);

                    }else if(iTalk_ComboBox1.SelectedItem.ToString() == "Monstres")
                    {
                        XmlParser.ParseSQLToXML(@".\ressources\Bot\BotMonsters\", "Monstres", true);

                    }else if(iTalk_ComboBox1.SelectedItem.ToString() == "Sorts")
                    {
                        XmlParser.ParseSQLToXML(@".\ressources\Bot\BotSorts\", "Sorts", true);
                    }
                    else
                    {
                        MessageBox.Show($"Le bot n'utilise pas de fichiers XML pour {iTalk_ComboBox1.SelectedItem.ToString()}", "Convertion impossible", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                        return;
                    }
                }
                else if(iTalk_RadioButton1.Checked == true && !string.IsNullOrWhiteSpace(iTalk_TextBox_Small1.Text))
                {
                    XmlParser.ParseSQLToXML(iTalk_TextBox_Small1.Text, iTalk_ComboBox1.SelectedItem.ToString());

                }else if(iTalk_RadioButton1.Checked == true && string.IsNullOrWhiteSpace(iTalk_TextBox_Small1.Text))
                {
                    MessageBox.Show("Merci de sélectionner un dossier pour la sortie des fichiers XML.", "Convertion impossible", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }
        }

        private void iTalk_TextBox_Small1_TextChanged(object sender, EventArgs e)
        {
           
        }

        private void iTalk_TextBox_Small1_Click(object sender, EventArgs e)
        {
            
        }

        private void iTalk_LinkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                DialogResult result = fbd.ShowDialog();
                if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
                {
                    iTalk_TextBox_Small1.Text = fbd.SelectedPath;
                }
            }
        }

        private void iTalk_ComboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {

        }
    }
}
