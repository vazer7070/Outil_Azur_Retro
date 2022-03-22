
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tools_protocol.Json;
using Tools_protocol.Query;

namespace Outil_Azur_complet
{
    public partial class SettingsForm : Form
    {
        string H;
        string U;
        string MDP;
        string baseA;
        string baseW;
        public SettingsForm()
        {
            InitializeComponent();
        }
       public void New(string h, string u, string pass, string a, string w)
        {
            H = h;
            U = u;
            MDP = pass;
            baseA = a;
            baseW = w;
            iTalk_TextBox_Small1.Text = H;
            iTalk_TextBox_Small2.Text = U;
            iTalk_TextBox_Small3.Text = MDP;
            iTalk_TextBox_Small4.Text = baseA;
            iTalk_TextBox_Small5.Text = baseW;
            if (InitializeForm.NoAuth)
                iTalk_TextBox_Small4.ForeColor = Color.Red;
            if(InitializeForm.noWorld)
                iTalk_TextBox_Small5.ForeColor = Color.Red;
        }
        private void iTalk_Panel2_Click(object sender, EventArgs e)
        {

        }

        private void iTalk_Button_11_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void iTalk_Button_21_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(iTalk_TextBox_Small1.Text) ||!string.IsNullOrEmpty(iTalk_TextBox_Small2.Text) || !string.IsNullOrEmpty(iTalk_TextBox_Small4.Text) || !string.IsNullOrEmpty(iTalk_TextBox_Small5.Text))
            {


                if (DatabaseManager.IsServerConnected(iTalk_TextBox_Small1.Text, iTalk_TextBox_Small2.Text, iTalk_TextBox_Small3.Text, iTalk_TextBox_Small4.Text) && DatabaseManager2.IsServerConnected(iTalk_TextBox_Small1.Text, iTalk_TextBox_Small2.Text, iTalk_TextBox_Small3.Text, iTalk_TextBox_Small5.Text))
                {
                    if (InitializeForm.NoAuth || InitializeForm.noWorld)
                    {
                        InitializeForm IF = new InitializeForm();
                        IF.RebootInit(iTalk_TextBox_Small1.Text, iTalk_TextBox_Small2.Text, iTalk_TextBox_Small3.Text, iTalk_TextBox_Small4.Text, iTalk_TextBox_Small5.Text, iTalk_ComboBox1.SelectedItem.ToString());
                        Hide();
                    }
                    else
                    {
                        DialogResult DR = MessageBox.Show("L'application doit redémarrer pour prendre en compte les informations. Avez-vous sauvegardé votre travail.?", "Redémarrage nécessaire", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                        if (DR == DialogResult.Yes)
                        {
                            InitializeForm IF = new InitializeForm();
                            IF.RebootInit(iTalk_TextBox_Small1.Text, iTalk_TextBox_Small2.Text, iTalk_TextBox_Small3.Text, iTalk_TextBox_Small4.Text, iTalk_TextBox_Small5.Text, iTalk_ComboBox1.SelectedItem.ToString());
                            Hide();
                        }
                        else
                        {
                            MessageBox.Show("Vos modifications seront prises en comptes lors du prochain démarrage de l'application.", "Informations enregistrés.", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            Properties.Settings.Default.OP_Hote = iTalk_TextBox_Small1.Text;
                            Properties.Settings.Default.OP_User = iTalk_TextBox_Small2.Text;
                            Properties.Settings.Default.OP_Pass = iTalk_TextBox_Small3.Text;
                            Properties.Settings.Default.OP_auth = iTalk_TextBox_Small4.Text;
                            Properties.Settings.Default.OP_world = iTalk_TextBox_Small5.Text;
                            Properties.Settings.Default.Save();
                        }
                    }


                }


            }
            else
            {
                MessageBox.Show("Merci de fournir des informations correctes.", "Connexion impossible", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }

        private void SettingsForm_Load(object sender, EventArgs e)
        {
            iTalk_TextBox_Small1.Text = JsonManager.Hôte;
            iTalk_TextBox_Small2.Text = JsonManager.User;
            iTalk_TextBox_Small3.Text = JsonManager.MDP;
            iTalk_TextBox_Small4.Text = JsonManager.Aauth;
            iTalk_TextBox_Small5.Text = JsonManager.Aworld;
            iTalk_Label10.Text = InitializeForm.ToolVersion;
            iTalk_Label11.Text = InitializeForm.ProtocolVersion;
            iTalk_Label14.Text = InitializeForm.AzurBotVersion;
            iTalk_Label16.Text = InitializeForm.EditorVersion;
        }

        private void iTalk_TextBox_Small3_TextChanged(object sender, EventArgs e)
        {

        }

        private void iTalk_Button_22_Click(object sender, EventArgs e)
        {
            if(iTalk_ComboBox1.SelectedItem.ToString() != InitializeForm.EMUSELECT)
            {
                Properties.Settings.Default.OP_Emu = iTalk_ComboBox1.SelectedItem.ToString();
                Application.Restart();
            }
        }

        private void iTalk_Button_12_Click(object sender, EventArgs e)
        {

        }

        private void iTalk_LinkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start(@"https://azurtoolretro.com/");
        }

        private void iTalk_Label13_Click(object sender, EventArgs e)
        {

        }

        private void iTalk_Label9_Click(object sender, EventArgs e)
        {

        }

        private void iTalk_Label8_Click(object sender, EventArgs e)
        {

        }
    }
}
