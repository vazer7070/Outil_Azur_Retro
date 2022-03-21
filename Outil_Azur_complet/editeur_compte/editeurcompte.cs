using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Windows.Forms;
using Tools_protocol.Codebreak.Database;
using Tools_protocol.Json;
using Tools_protocol.Kryone.Database;
using Tools_protocol.Managers;
using Tools_protocol.Query;

namespace Outil_Azur_complet.editeur_compte
{
    public partial class editeurcompte : Form
    {
        public editeurcompte()
        {
            InitializeComponent();
        }
        public string TableCompte => EmuManager.ReturnTable("comptes", InitializeForm.EMUSELECT);
        public string TablePerso => EmuManager.ReturnTable("perso", InitializeForm.EMUSELECT);
        List<string> list = new List<string>();
        private void iTalk_ThemeContainer1_Click(object sender, EventArgs e)
        {

        }

        private void editeurcompte_Load(object sender, EventArgs e)
        {

            Load_editeur(InitializeForm.EMUSELECT);
        }
        private void Load_editeur(string emu)
        {
            listBox1.Items.Clear();
            if (emu.Equals("kryone"))
            {
                AccountList.Accounts.Clear();
                AccountList.AllAccounts();
                foreach (string word in AccountList.Accounts)
                {
                    listBox1.Items.Add(word);
                }
                foreach (String str in listBox1.Items)
                {
                    list.Add(str);
                }
            }else if (emu.Equals("codebreak"))
            {
                AccountListi.AccountsName.Clear();
                AccountListi.LoadAccounts();
                foreach (string word in AccountListi.AccountsName)
                {
                    listBox1.Items.Add(word);
                }
                foreach (String str in listBox1.Items)
                {
                    list.Add(str);
                }
            }
            iTalk_Label14.Text = $"{listBox1.Items.Count} comptes chargés.";
            iTalk_TextBox_Small1.Enabled = false;
            iTalk_TextBox_Small5.Enabled = false;
            iTalk_TextBox_Small11.Enabled = false;
        }
        private void listBox1_SelectedIndexChanged(object sender, EventArgs e)
        {

             try
             {
                iTalk_TextBox_Small1.Text = EmuManager.ReturnAccountsInfo(InitializeForm.EMUSELECT, listBox1.SelectedItem.ToString(), 1);
                 iTalk_TextBox_Small2.Text = EmuManager.ReturnAccountsInfo(InitializeForm.EMUSELECT, listBox1.SelectedItem.ToString(), 2);
                iTalk_TextBox_Small3.Text = EmuManager.ReturnAccountsInfo(InitializeForm.EMUSELECT, listBox1.SelectedItem.ToString(), 3);
                iTalk_TextBox_Small4.Text = EmuManager.ReturnAccountsInfo(InitializeForm.EMUSELECT, listBox1.SelectedItem.ToString(), 4);
                iTalk_TextBox_Small5.Text = EmuManager.ReturnAccountsInfo(InitializeForm.EMUSELECT, listBox1.SelectedItem.ToString(), 5);
                iTalk_TextBox_Small6.Text = EmuManager.ReturnAccountsInfo(InitializeForm.EMUSELECT, listBox1.SelectedItem.ToString(), 6);
                iTalk_TextBox_Small7.Text = EmuManager.ReturnAccountsInfo(InitializeForm.EMUSELECT, listBox1.SelectedItem.ToString(), 7);
                iTalk_TextBox_Small9.Text = EmuManager.ReturnAccountsInfo(InitializeForm.EMUSELECT, listBox1.SelectedItem.ToString(), 8);
                iTalk_TextBox_Small10.Text = EmuManager.ReturnAccountsInfo(InitializeForm.EMUSELECT, listBox1.SelectedItem.ToString(), 9);
                iTalk_TextBox_Small11.Text = EmuManager.ReturnAccountsInfo(InitializeForm.EMUSELECT, listBox1.SelectedItem.ToString(), 10);
                CharacterList.Nameperso.Clear();
                iTalk_ComboBox1.Items.Clear();
                CharacterList.Informations(iTalk_TextBox_Small1.Text);
                foreach(string word in CharacterList.Nameperso)
                {
                    iTalk_ComboBox1.Items.Add(word);
                }
                if (AccountList.Informations(listBox1.SelectedItem.ToString()).Logged.Equals(1))
                 {
                     iTalk_Label9.Text = "Connecté";
                     iTalk_Label9.ForeColor = System.Drawing.Color.Green;
                 }
                 else
                 {
                     iTalk_Label9.Text = "Non connecté";
                     iTalk_Label9.ForeColor = System.Drawing.Color.Red;
                 }


             }
             catch(Exception o)
             {
                MessageBox.Show(o.Message + "\n" + o.Source);
                 return;
             }
            
        }

        private void iTalk_Button_12_Click(object sender, EventArgs e)
        {
            AccountList.UpdateBan(listBox1.SelectedItem.ToString(), Convert.ToInt32(iTalk_TextBox_Small5.Text));
            iTalk_TextBox_Small5.Text = AccountList.Informations(listBox1.SelectedItem.ToString()).Banned.ToString();
        }

        private void iTalk_Button_13_Click(object sender, EventArgs e)
        {
            AccountList.UpdateVip(listBox1.SelectedItem.ToString(), Convert.ToInt32(iTalk_TextBox_Small11.Text));
            iTalk_TextBox_Small11.Text = AccountList.Informations(listBox1.SelectedItem.ToString()).Vip.ToString();
        }
        private void iTalk_Button_14_Click(object sender, EventArgs e)
        {
           
        }

        private void iTalk_Button_11_Click(object sender, EventArgs e)
        {
            DatabaseManager.UpdateQuery(QueryBuilder.DeleteFromQuery(TableCompte, "account", listBox1.SelectedItem.ToString()));
            DatabaseManager.UpdateQuery(QueryBuilder.DeleteFromQuery(TablePerso, "account", iTalk_TextBox_Small1.Text));
            MessageBox.Show($"Le compte {iTalk_TextBox_Small2.Text} a bien été supprimé", "Action réussie", MessageBoxButtons.OK, MessageBoxIcon.Information);
            AccountList.Accounts.Remove(iTalk_TextBox_Small2.Text);
            listBox1.Items.Remove(listBox1.SelectedItem.ToString());
            iTalk_TextBox_Small1.Text = "";
            iTalk_TextBox_Small2.Text = "";
            iTalk_TextBox_Small3.Text = "";
            iTalk_TextBox_Small4.Text = "";
            iTalk_TextBox_Small5.Text = "";
            iTalk_TextBox_Small6.Text = "";
            iTalk_TextBox_Small7.Text = "";
            iTalk_TextBox_Small9.Text = "";
            iTalk_TextBox_Small10.Text = "";
            iTalk_TextBox_Small11.Text = "";
            iTalk_Label14.Text = $"{listBox1.Items.Count} comptes chargés.";

        }

        private void iTalk_LinkLabel2_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            try
            {

                if (!iTalk_TextBox_Small2.Text.Equals(""))
                {
                    DatabaseManager.UpdateQuery(QueryBuilder.UpdateFromQuery(TableCompte, "account", 1, iTalk_TextBox_Small2.Text, "guid", iTalk_TextBox_Small1.Text));
                    listBox1.Items.Clear();
                    iTalk_Label14.Text = $"{listBox1.Items.Count} comptes chargés.";
                    Thread.Sleep(10);
                    Load_editeur(InitializeForm.EMUSELECT);
                }
                

            }
            catch(Exception fdfdf)
            {
                iTalk_LinkLabel2.Text = fdfdf.Message;
                iTalk_LinkLabel2.ForeColor = System.Drawing.Color.Red;
                iTalk_LinkLabel2.Enabled = false;
                
            }
        }

        private void editeurcompte_Leave(object sender, EventArgs e)
        {
            
        }

        private void iTalk_ControlBox1_Click(object sender, EventArgs e)
        {
            
        }

        private void iTalk_Button_21_Click(object sender, EventArgs e)
        {

        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {
            if (String.IsNullOrEmpty(textBox1.Text.Trim()) == false)
            {
                listBox1.Items.Clear();
                foreach (string str in list)
                {
                    if (str.StartsWith(textBox1.Text.Trim()))

                    {
                        listBox1.Items.Add(str);
                    }
                }
            }

            else if (textBox1.Text.Trim().Equals(""))
            {
                listBox1.Items.Clear();

                foreach (string str in list)
                {
                    listBox1.Items.Add(str);
                }
            }
        }

        private void iTalk_LinkLabel3_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            try
            {

                if (!iTalk_TextBox_Small3.Text.Equals(""))
                {
                    DatabaseManager.UpdateQuery(QueryBuilder.UpdateFromQuery(TableCompte, "pseudo", 1, iTalk_TextBox_Small3.Text, "guid", iTalk_TextBox_Small1.Text));
                }


            }
            catch (Exception fdfdf)
            {
                iTalk_LinkLabel3.Text = fdfdf.Message;
                iTalk_LinkLabel3.ForeColor = System.Drawing.Color.Red;
                iTalk_LinkLabel3.Enabled = false;
            }
        }

        private void iTalk_LinkLabel4_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            try
            {

                if (!iTalk_TextBox_Small4.Text.Equals(""))
                {
                    DatabaseManager.UpdateQuery(QueryBuilder.UpdateFromQuery(TableCompte, "pass", 1, iTalk_TextBox_Small4.Text, "guid", iTalk_TextBox_Small1.Text));
                }


            }
            catch (Exception fdfdf)
            {
                iTalk_LinkLabel4.Text = fdfdf.Message;
                iTalk_LinkLabel4.ForeColor = System.Drawing.Color.Red;
                iTalk_LinkLabel4.Enabled = false;
            }
        }

        private void iTalk_LinkLabel6_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            try
            {

                if (!iTalk_TextBox_Small6.Text.Equals(""))
                {
                    DatabaseManager.UpdateQuery(QueryBuilder.UpdateFromQuery(TableCompte, "question", 1, iTalk_TextBox_Small6.Text, "guid", iTalk_TextBox_Small1.Text));
                }


            }
            catch (Exception fdfdf)
            {
                iTalk_LinkLabel6.Text = fdfdf.Message;
                iTalk_LinkLabel6.ForeColor = System.Drawing.Color.Red;
                iTalk_LinkLabel6.Enabled = false;
            }
        }

        private void iTalk_LinkLabel7_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            try
            {

                if (!iTalk_TextBox_Small7.Text.Equals(""))
                {
                    DatabaseManager.UpdateQuery(QueryBuilder.UpdateFromQuery(TableCompte, "reponse", 1, iTalk_TextBox_Small7.Text, "guid", iTalk_TextBox_Small1.Text));
                }


            }
            catch (Exception fdfdf)
            {
                iTalk_LinkLabel7.Text = fdfdf.Message;
                iTalk_LinkLabel7.ForeColor = System.Drawing.Color.Red;
                iTalk_LinkLabel7.Enabled = false;
            }
        }

        private void iTalk_LinkLabel9_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            try
            {

                if (!iTalk_TextBox_Small9.Text.Equals(""))
                {
                    DatabaseManager.UpdateQuery(QueryBuilder.UpdateFromQuery(TableCompte, "points", 1, iTalk_TextBox_Small9.Text, "guid", iTalk_TextBox_Small1.Text));
                }


            }
            catch (Exception fdfdf)
            {
                iTalk_LinkLabel9.Text = fdfdf.Message;
                iTalk_LinkLabel9.ForeColor = System.Drawing.Color.Red;
                iTalk_LinkLabel9.Enabled = false;
            }
        }

        private void iTalk_LinkLabel10_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            try
            {

                if (!iTalk_TextBox_Small10.Text.Equals(""))
                {
                    DatabaseManager.UpdateQuery(QueryBuilder.UpdateFromQuery(TableCompte, "lastIp", 1, iTalk_TextBox_Small10.Text, "guid", iTalk_TextBox_Small1.Text));
                }


            }
            catch (Exception fdfdf)
            {
                iTalk_LinkLabel10.Text = fdfdf.Message;
                iTalk_LinkLabel10.ForeColor = System.Drawing.Color.Red;
                iTalk_LinkLabel10.Enabled = false;
            }
        }

        private void iTalk_Button_14_Click_1(object sender, EventArgs e)
        {
            CreateForm CF = new CreateForm();
            CF.ShowDialog();
        }
    }
}
