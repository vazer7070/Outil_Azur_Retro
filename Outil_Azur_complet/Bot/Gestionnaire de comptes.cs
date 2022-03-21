using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Config;

namespace Outil_Azur_complet.Bot
{
    public partial class Gestionnaire_de_comptes : Form
    {
        bool selected = false;
        string selectionned = "";
        public Gestionnaire_de_comptes()
        {
            InitializeComponent();

        }

        private void iTalk_Button_11_Click(object sender, EventArgs e)
        {
             Close();
            
        }

        private void Gestionnaire_de_comptes_Load(object sender, EventArgs e)
        {
            iTalk_Listview1.Columns.Add("Compte", 150, HorizontalAlignment.Left);
            iTalk_Listview1.Columns.Add("Personnage", 150, HorizontalAlignment.Left);
            iTalk_Listview1.Columns.Add("ID serveur", 150, HorizontalAlignment.Left);
            iTalk_TextBox_Small4.Enabled = false;
            iTalk_ComboBox1.SelectedItem = iTalk_ComboBox1.Items[0];
            UpdateListViews();
        }
        public void UpdateListViews()
        {
            iTalk_Listview1.Items.Clear();
            if (AccountConfig.AccountsDico.Count > 0)
            {
                foreach (string h in AccountConfig.AccountsDico.Keys)
                {
                    iTalk_Listview1.Items.Add(h).SubItems.AddRange(new string[2] { AccountConfig.ReturnAccountInfo(h).player, AccountConfig.ReturnAccountInfo(h).Server });
                }
            }
        }
        private void iTalk_CheckBox1_CheckedChanged(object sender)
        {
            if(iTalk_CheckBox1.Checked == true)
            {
                iTalk_ComboBox1.Enabled = false;
                iTalk_TextBox_Small4.Enabled = true;
            }
            else
            {
                iTalk_ComboBox1.Enabled = true;
                iTalk_TextBox_Small4.Enabled = false;
            }
        }

        private void iTalk_Button_21_Click(object sender, EventArgs e) // connexion direct après la création du compte
        {
            if (!string.IsNullOrWhiteSpace(iTalk_TextBox_Small1.Text))
            {
                if (!string.IsNullOrWhiteSpace(iTalk_TextBox_Small2.Text))
                {
                    if(iTalk_CheckBox1.Checked == true && string.IsNullOrWhiteSpace(iTalk_TextBox_Small4.Text))
                    {
                        MessageBox.Show("Merci de renseigner un ID de serveur ou de choisir un ID par les serveurs officiels", "Ajout impossible", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    else
                    {
                        if (AccountConfig.AccountsDico.ContainsKey(iTalk_TextBox_Small1.Text))
                        {
                            MessageBox.Show($"Le compte {iTalk_TextBox_Small1.Text} est déjà enregistré.", "Ajout impossible", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }
                        else
                        {
                            string server = "";
                            if(iTalk_CheckBox1.Checked == true)
                            {
                                server = iTalk_TextBox_Small4.Text;
                            }
                            else
                            {
                                server = iTalk_ComboBox1.SelectedItem.ToString().Split('(')[1].Split(')')[0];
                            }
                            AccountConfig A = new AccountConfig(iTalk_TextBox_Small1.Text, iTalk_TextBox_Small2.Text, server, iTalk_TextBox_Small3.Text);
                            AccountConfig.AccountsDico.Add(iTalk_TextBox_Small1.Text, A);
                            AccountConfig.WriteCompte(iTalk_TextBox_Small1.Text, iTalk_TextBox_Small2.Text, server, iTalk_TextBox_Small3.Text);
                            UpdateListViews();
                            AccountConfig.AccountsActive.Add(A);
                            DialogResult = DialogResult.OK;
                            Close();
                           
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

        private void supprimerCompteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (selected)
            {
                AccountConfig.DeleteCompte(selectionned);
                UpdateListViews();
            }

        }
        private void connexionToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AccountConfig A = AccountConfig.ReturnAccountInfo(selectionned);
            AccountConfig.AccountsActive.Add(A);
            DialogResult = DialogResult.OK;
            Close();
        }
        private void serveurToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AccountConfig M = AccountConfig.ReturnAccountInfo(selectionned);
            string newserver = Interaction.InputBox("Merci de renseigner le nouveau serveur", "Modifier le serveur", M.Server);
            if(!string.IsNullOrWhiteSpace(newserver) && !newserver.Equals(M.Server))
            {
                AccountConfig.DeleteCompte(selectionned);
                AccountConfig O = new AccountConfig(selectionned, M.Password, newserver, M.player);
                AccountConfig.AccountsDico.Add(selectionned, O);
                AccountConfig.WriteCompte(selectionned, O.Password, newserver, O.player);
                UpdateListViews();
            }
        }

        private void personnageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AccountConfig F = AccountConfig.ReturnAccountInfo(selectionned);
            string newplayer = Interaction.InputBox("Merci de renseigner le nouveau personnage", "Modifier le personnage", F.player);
            if(!string.IsNullOrWhiteSpace(newplayer) && !newplayer.Equals(F.player))
            {
                AccountConfig.DeleteCompte(selectionned);
                AccountConfig B = new AccountConfig(selectionned, F.Password, F.Server, newplayer);
                AccountConfig.AccountsDico.Add(selectionned, B);
                AccountConfig.WriteCompte(selectionned, B.Password, B.Server, newplayer);
                UpdateListViews();
            }
        }

        private void motDePasseToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AccountConfig T = AccountConfig.ReturnAccountInfo(selectionned);
            string newpass = Interaction.InputBox("Merci de renseigner le nouveau mot de passe", "Modifier le mot de passe", T.Password);
            if(!string.IsNullOrWhiteSpace(newpass) && !newpass.Equals(T.Password))
            {
                AccountConfig.DeleteCompte(selectionned);
                AccountConfig J = new AccountConfig(selectionned, newpass, T.Server, T.player);
                AccountConfig.AccountsDico.Add(selectionned, J);
                AccountConfig.WriteCompte(J.Account, newpass, J.Server, J.player);
            }
        }

        private void compteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string NewAccount = Interaction.InputBox("Merci de renseigner le nouveau nom de compte", "Modifier le compte", selectionned);
            if (!string.IsNullOrWhiteSpace(NewAccount) && !AccountConfig.AccountsDico.ContainsKey(NewAccount))
            {
                AccountConfig H = AccountConfig.ReturnAccountInfo(selectionned);
                AccountConfig.DeleteCompte(selectionned);
                AccountConfig.AccountsDico.Add(NewAccount, H);
                AccountConfig.WriteCompte(NewAccount, H.Password, H.Server, H.player);
                UpdateListViews();
            }
            else
            {
                MessageBox.Show("Merci de vérifier vos informations", "Modification impossible", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }
    }
}
