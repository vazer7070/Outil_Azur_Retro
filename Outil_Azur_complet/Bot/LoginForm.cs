using iTalk;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;

namespace Outil_Azur_complet.Bot
{
    public partial class LoginForm : Form
    {
        private bool selected;
        private string selectionned;

        public LoginForm()
        {
            InitializeComponent();

            MessagesReception.Init();
            GlobalConfig.InitializeConfig();
            AccountConfig.LoadAccount();
        }

        private void iTalk_Button_11_Click(object sender, EventArgs e)
        {
            Close();
        }
        public void UpdateListViews()
        {
            iTalk_Listview1.Items.Clear();
            if (AccountConfig.AccountsDico.Count > 0)
            {
                foreach (string h in AccountConfig.AccountsDico.Keys)
                {
                    iTalk_Listview1.Items.Add(h);
                }
            }
        }

        private void LoginForm_Load(object sender, EventArgs e)
        {
            iTalk_Listview1.Columns.Add("Compte", 170, HorizontalAlignment.Left);
            iTalk_Listview1.Columns.Add("Lieu", 170, HorizontalAlignment.Left);
            UpdateListViews();
            UpdateOptions();
            
        }
        private void UpdateOptions()
        {
            iTalk_TextBox_Small3.Text = GlobalConfig.IP;
            iTalk_TextBox_Small4.Text = GlobalConfig.AUTHPORT;
            iTalk_TextBox_Small5.Text = GlobalConfig.GAMEPORT;
            iTalk_TextBox_Small6.Text = GlobalConfig.VERSION;
        }
        private void iTalk_Button_12_Click_1(object sender, EventArgs e)
        {
            if (iTalk_RadioButton2.Checked == iTalk_RadioButton2.Checked == false)
                return;
            if (!string.IsNullOrWhiteSpace(iTalk_TextBox_Small1.Text))
            {
                if (!string.IsNullOrWhiteSpace(iTalk_TextBox_Small2.Text))
                {


                    if (AccountConfig.AccountsDico.ContainsKey(iTalk_TextBox_Small1.Text))
                    {
                        MessageBox.Show($"Le compte {iTalk_TextBox_Small1.Text} est déjà enregistré.", "Ajout impossible", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    else
                    {
                        string L = "";
                        if (GlobalConfig.BYPASS)
                            L = "Officiel";
                        else
                            L = "Privé";

                        if (GlobalConfig.BYPASS)
                        {

                        }
                        else
                        {
                            AccountConfig A = new AccountConfig(iTalk_TextBox_Small1.Text, iTalk_TextBox_Small2.Text, L);
                            AccountConfig.AccountsDico.Add(iTalk_TextBox_Small1.Text, A);
                            AccountConfig.WriteCompte(iTalk_TextBox_Small1.Text, iTalk_TextBox_Small2.Text, L);
                            UpdateListViews();
                            AccountConfig.AccountsActive.Add(A);
                            PersoSelection PS = new PersoSelection(A);
                            PS.Show();
                            this.Close();
                        }

                    }

                }
                else
                {
                    MessageBox.Show("Le mot de passe renseigné est incorrect", "Ajout impossible", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }
            else
            {
                MessageBox.Show("Information de compte incorrecte", "Ajout impossible", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }

       

        private void iTalk_Listview1_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                selected = iTalk_Listview1.SelectedItems[0].Selected;
                selectionned = iTalk_Listview1.SelectedItems[0].Text;
                if (selected)
                    iTalk_ContextMenuStrip1.Show(Cursor.Position.X, Cursor.Position.Y);
            }
            catch
            {
                selected = false;

            }
        }

        private void connexionToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (GlobalConfig.BYPASS)
            {

            }
            else
            {
                AccountConfig A = AccountConfig.ReturnAccountInfo(selectionned);
                AccountConfig.AccountsActive.Add(A);
                PersoSelection PS = new PersoSelection(A);
                PS.Show();
                this.Close();
            }
        }

        private void supprimerCompteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (selected)
            {
                AccountConfig.DeleteCompte(selectionned);
                UpdateListViews();
            }
        }

        private void iTalk_RadioButton2_CheckedChanged(object sender)
        {
            if(iTalk_RadioButton2.Checked == true)
                GlobalConfig.BYPASS = true;
               
        }

        private void nomDeCompteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string NewAccount = Interaction.InputBox("Merci de renseigner le nouveau nom de compte", "Modifier le compte", selectionned);
            if (!string.IsNullOrWhiteSpace(NewAccount) && !AccountConfig.AccountsDico.ContainsKey(NewAccount))
             {

                 AccountConfig H = AccountConfig.ReturnAccountInfo(selectionned);
                 AccountConfig.DeleteCompte(selectionned);
                 AccountConfig.AccountsDico.Add(NewAccount, H);
                 AccountConfig.WriteCompte(NewAccount, H.Password, H.Lieu);
                 UpdateListViews();
             }
             else
             {
                 MessageBox.Show("Merci de vérifier vos informations", "Modification impossible", MessageBoxButtons.OK, MessageBoxIcon.Error);
                 return;
             }
        }

        private void motDePasseToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AccountConfig T = AccountConfig.ReturnAccountInfo(selectionned);
             string newpass = Interaction.InputBox("Merci de renseigner le nouveau mot de passe", "Modifier le mot de passe", T.Password);
             if(!string.IsNullOrWhiteSpace(newpass) && !newpass.Equals(T.Password))
             {
                 AccountConfig.DeleteCompte(selectionned);
                 AccountConfig J = new AccountConfig(selectionned, newpass, T.Lieu);
                 AccountConfig.AccountsDico.Add(selectionned, J);
                 AccountConfig.WriteCompte(J.Account, newpass, T.Lieu);
             }
        }

        private void iTalk_TextBox_Small4_TextChanged(object sender, EventArgs e)
        {

        }

        private void iTalk_Button_21_Click(object sender, EventArgs e)
        {

            bool isgood = false;
            if (!IPAddress.TryParse(iTalk_TextBox_Small3.Text, out IPAddress address))
            {
                isgood = false;
                iTalk_TextBox_Small3.BackColor = Color.Red;

            }
            if (!int.TryParse(iTalk_TextBox_Small4.Text, out int value))
            {
                iTalk_TextBox_Small4.BackColor = Color.Red;
                isgood = false;
            }
            if (!int.TryParse(iTalk_TextBox_Small5.Text, out int valu))
            {
                iTalk_TextBox_Small5.BackColor = Color.Red;
                isgood = false;
            }
            else
            {
                isgood = true;
            }


            if (isgood)
            {
                GlobalConfig.writenewconfig(iTalk_TextBox_Small3.Text, iTalk_TextBox_Small4.Text, iTalk_TextBox_Small5.Text, iTalk_TextBox_Small6.Text);
                MessageBox.Show("Les changements ont été appliqués.", "Modifications effectuées", MessageBoxButtons.OK, MessageBoxIcon.Information);
                UpdateOptions();
                return;
            }
                
        }

        private void iTalk_RadioButton1_CheckedChanged(object sender)
        {
            if (iTalk_RadioButton1.Checked == true)
                GlobalConfig.BYPASS = false;
        }

        private void iTalk_ContextMenuStrip1_Opening(object sender, CancelEventArgs e)
        {

        }
    }
}
