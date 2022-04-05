using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Net;
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
        private Dictionary<string, string> ModifiedQuery = new Dictionary<string, string>();
        static List<string> PersoByAccount = new List<string>();
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
            if (emu.Equals("Kryone"))
            {
                foreach (string word in AccountList.AllAccount.Keys)
                {
                    listBox1.Items.Add(word);
                }
                foreach (String str in listBox1.Items)
                {
                    list.Add(str);
                }
            }else if (emu.Equals("Codebreak"))
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
                iTalk_ComboBox1.Items.Clear();
                PersoByAccount = CharacterList.Informations(int.Parse(iTalk_TextBox_Small1.Text));
                foreach(string word in PersoByAccount)
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

        private void iTalk_Button_12_Click(object sender, EventArgs e) //Bannis ou pas
        {
            AccountList.ModifyAccount(AccountList.ReturnById(int.Parse(iTalk_TextBox_Small1.Text)), AccountList.ReturnById(int.Parse(iTalk_TextBox_Small1.Text)).Account, iTalk_TextBox_Small5.Text, 9);
            if (ModifiedQuery.ContainsKey($"BanControl{iTalk_TextBox_Small1.Text}"))
            {
                ModifiedQuery.Remove($"BanControl{iTalk_TextBox_Small1.Text}");
                ModifiedQuery.Add($"BanControl{iTalk_TextBox_Small1.Text}", QueryBuilder.UpdateFromQuery(TableCompte, "banned", 1, AccountList.ReturnById(int.Parse(iTalk_TextBox_Small1.Text)).Banned.ToString(), "guid", iTalk_TextBox_Small1.Text));
            }
            else
            {
                UpdateNotifNumber();
                ModifiedQuery.Remove($"BanControl{iTalk_TextBox_Small1.Text}");
                ModifiedQuery.Add($"BanControl{iTalk_TextBox_Small1.Text}", QueryBuilder.UpdateFromQuery(TableCompte, "banned", 1, AccountList.ReturnById(int.Parse(iTalk_TextBox_Small1.Text)).Banned.ToString(), "guid", iTalk_TextBox_Small1.Text));
            }
            iTalk_TextBox_Small5.Text = AccountList.ReturnById(int.Parse(iTalk_TextBox_Small1.Text)).Banned.ToString();
        }

        private void iTalk_Button_13_Click(object sender, EventArgs e) //VIP
        {
            AccountList.ModifyAccount(AccountList.ReturnById(int.Parse(iTalk_TextBox_Small1.Text)), AccountList.ReturnById(int.Parse(iTalk_TextBox_Small1.Text)).Account, iTalk_TextBox_Small11.Text, 8);
            if (ModifiedQuery.ContainsKey($"VIPControl{iTalk_TextBox_Small1.Text}"))
            {
                ModifiedQuery.Remove($"VIPControl{iTalk_TextBox_Small1.Text}");
                ModifiedQuery.Add($"VIPControl{iTalk_TextBox_Small1.Text}", QueryBuilder.UpdateFromQuery(TableCompte, "vip", 1, AccountList.ReturnById(int.Parse(iTalk_TextBox_Small1.Text)).Vip.ToString(), "guid", iTalk_TextBox_Small1.Text));
            }
            else
            {
                UpdateNotifNumber();
                ModifiedQuery.Remove($"VIPControl{iTalk_TextBox_Small1.Text}");
                ModifiedQuery.Add($"VIPControl{iTalk_TextBox_Small1.Text}", QueryBuilder.UpdateFromQuery(TableCompte, "vip", 1, AccountList.ReturnById(int.Parse(iTalk_TextBox_Small1.Text)).Vip.ToString(), "guid", iTalk_TextBox_Small1.Text));
            }
            iTalk_TextBox_Small11.Text = AccountList.ReturnById(int.Parse(iTalk_TextBox_Small1.Text)).Vip.ToString();
        }
        private void iTalk_Button_14_Click(object sender, EventArgs e)
        {
           
        }

        private void iTalk_Button_11_Click(object sender, EventArgs e)
        {
            DatabaseManager.UpdateQuery(QueryBuilder.DeleteFromQuery(TableCompte, "account", listBox1.SelectedItem.ToString()));
            DatabaseManager.UpdateQuery(QueryBuilder.DeleteFromQuery(TablePerso, "account", iTalk_TextBox_Small1.Text));
            MessageBox.Show($"Le compte {iTalk_TextBox_Small2.Text} a bien été supprimé", "Action réussie", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
        private void UpdateNotifNumber(bool erase = false)
        {
            if (erase)
                iTalk_NotificationNumber1.Value = 0;
            else
                iTalk_NotificationNumber1.Value = iTalk_NotificationNumber1.Value + 1;

        }
        private void iTalk_LinkLabel2_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e) // modifier le nom du compte
        {
            try
            {
                if (!string.IsNullOrEmpty(iTalk_TextBox_Small2.Text))
                {
                    string name = iTalk_TextBox_Small2.Text;
                    string firstname = AccountList.ReturnById(int.Parse(iTalk_TextBox_Small1.Text)).Account;
                    if (name != firstname)
                    {
                        AccountList.ModifyAccount(AccountList.Informations(firstname),firstname, name, 1);
                        if (ModifiedQuery.ContainsKey($"NameControl{iTalk_TextBox_Small1.Text}"))
                        {
                            ModifiedQuery.Remove($"NameControl{iTalk_TextBox_Small1.Text}");
                            ModifiedQuery.Add($"NameControl{iTalk_TextBox_Small1.Text}", QueryBuilder.UpdateFromQuery(TableCompte, "account", 1, iTalk_TextBox_Small2.Text, "guid", iTalk_TextBox_Small1.Text));
                        }
                        else
                        {
                            UpdateNotifNumber();
                            ModifiedQuery.Add($"NameControl{iTalk_TextBox_Small1.Text}", QueryBuilder.UpdateFromQuery(TableCompte, "account", 1, iTalk_TextBox_Small2.Text, "guid", iTalk_TextBox_Small1.Text));
                        }
                            

                    }
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

        private void iTalk_LinkLabel3_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e) //modifier le pseudo
        {
            try
            {
                if (!string.IsNullOrEmpty(iTalk_TextBox_Small3.Text))
                {
                    string pseudo = iTalk_TextBox_Small3.Text;
                    string firstpseudo = AccountList.ReturnById(int.Parse(iTalk_TextBox_Small1.Text)).Pseudo;
                    if (pseudo != firstpseudo)
                    {
                        AccountList.ModifyAccount(AccountList.ReturnById(int.Parse(iTalk_TextBox_Small1.Text)), AccountList.ReturnById(int.Parse(iTalk_TextBox_Small1.Text)).Account, pseudo, 2);
                        if (ModifiedQuery.ContainsKey($"PseudoControl{iTalk_TextBox_Small1.Text}"))
                        {
                            ModifiedQuery.Remove($"PseudoControl{iTalk_TextBox_Small1.Text}");
                            ModifiedQuery.Add($"PseudoControl{iTalk_TextBox_Small1.Text}", QueryBuilder.UpdateFromQuery(TableCompte, "pseudo", 1, iTalk_TextBox_Small3.Text, "guid", iTalk_TextBox_Small1.Text));
                        }
                        else
                        {
                            UpdateNotifNumber();
                            ModifiedQuery.Remove($"PseudoControl{iTalk_TextBox_Small1.Text}");
                            ModifiedQuery.Add($"PseudoControl{iTalk_TextBox_Small1.Text}", QueryBuilder.UpdateFromQuery(TableCompte, "pseudo", 1, iTalk_TextBox_Small3.Text, "guid", iTalk_TextBox_Small1.Text));
                        }
                           

                    }
                }


            }
            catch (Exception fdfdf)
            {
                iTalk_LinkLabel3.Text = fdfdf.Message;
                iTalk_LinkLabel3.ForeColor = System.Drawing.Color.Red;
                iTalk_LinkLabel3.Enabled = false;
            }
        }

        private void iTalk_LinkLabel4_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e) //modifier le mot de passe
        {
            try
            {
                if (!string.IsNullOrEmpty(iTalk_TextBox_Small4.Text))
                {
                    string pass = iTalk_TextBox_Small4.Text;
                    string firstpass = AccountList.ReturnById(int.Parse(iTalk_TextBox_Small1.Text)).Pass;
                    if (pass != firstpass)
                    {
                        AccountList.ModifyAccount(AccountList.ReturnById(int.Parse(iTalk_TextBox_Small1.Text)), AccountList.ReturnById(int.Parse(iTalk_TextBox_Small1.Text)).Account, pass, 3);
                        
                        if (ModifiedQuery.ContainsKey($"PassControl{iTalk_TextBox_Small1.Text}"))
                        {
                            ModifiedQuery.Remove($"PassControl{iTalk_TextBox_Small1.Text}");
                            ModifiedQuery.Add($"PassControl{iTalk_TextBox_Small1.Text}", QueryBuilder.UpdateFromQuery(TableCompte, "pass", 1, iTalk_TextBox_Small4.Text, "guid", iTalk_TextBox_Small1.Text));
                        }
                        else
                        {
                            UpdateNotifNumber();
                            ModifiedQuery.Add($"PassControl{iTalk_TextBox_Small1.Text}", QueryBuilder.UpdateFromQuery(TableCompte, "pass", 1, iTalk_TextBox_Small4.Text, "guid", iTalk_TextBox_Small1.Text));
                        }
                            

                    }
                }

            }
            catch (Exception fdfdf)
            {
                iTalk_LinkLabel4.Text = fdfdf.Message;
                iTalk_LinkLabel4.ForeColor = System.Drawing.Color.Red;
                iTalk_LinkLabel4.Enabled = false;
            }
        }

        private void iTalk_LinkLabel6_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e) //modifier la question
        {
            try
            {

                if (!string.IsNullOrEmpty(iTalk_TextBox_Small6.Text))
                {
                    string ask = iTalk_TextBox_Small6.Text;
                    string firstask = AccountList.ReturnById(int.Parse(iTalk_TextBox_Small1.Text)).Question;
                    if (ask != firstask)
                    {
                        AccountList.ModifyAccount(AccountList.ReturnById(int.Parse(iTalk_TextBox_Small1.Text)), AccountList.ReturnById(int.Parse(iTalk_TextBox_Small1.Text)).Account, ask, 4);
                        
                        if (ModifiedQuery.ContainsKey($"AskControl{iTalk_TextBox_Small1.Text}"))
                        {
                            ModifiedQuery.Remove($"AskControl{iTalk_TextBox_Small1.Text}");
                            ModifiedQuery.Add($"AskControl{iTalk_TextBox_Small1.Text}", QueryBuilder.UpdateFromQuery(TableCompte, "question", 1, iTalk_TextBox_Small6.Text, "guid", iTalk_TextBox_Small1.Text));
                        }
                        else
                        {
                            UpdateNotifNumber();
                            ModifiedQuery.Add($"AskControl{iTalk_TextBox_Small1.Text}", QueryBuilder.UpdateFromQuery(TableCompte, "question", 1, iTalk_TextBox_Small6.Text, "guid", iTalk_TextBox_Small1.Text));
                        }

                    }
                }


            }
            catch (Exception fdfdf)
            {
                iTalk_LinkLabel6.Text = fdfdf.Message;
                iTalk_LinkLabel6.ForeColor = System.Drawing.Color.Red;
                iTalk_LinkLabel6.Enabled = false;
            }
        }

        private void iTalk_LinkLabel7_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e) //modifier la réponse
        {
            try
            {

                if (!string.IsNullOrEmpty(iTalk_TextBox_Small7.Text))
                {
                    string Answer = iTalk_TextBox_Small7.Text;
                    string firstanswer = AccountList.ReturnById(int.Parse(iTalk_TextBox_Small1.Text)).Reponse;
                    if (Answer != firstanswer)
                    {
                        AccountList.ModifyAccount(AccountList.ReturnById(int.Parse(iTalk_TextBox_Small1.Text)), AccountList.ReturnById(int.Parse(iTalk_TextBox_Small1.Text)).Account, Answer, 5);
                        
                        if (ModifiedQuery.ContainsKey($"AnswerControl{iTalk_TextBox_Small1.Text}"))
                        {
                            ModifiedQuery.Remove($"AnswerControl{iTalk_TextBox_Small1.Text}");
                            ModifiedQuery.Add($"AnswerControl{iTalk_TextBox_Small1.Text}", QueryBuilder.UpdateFromQuery(TableCompte, "reponse", 1, iTalk_TextBox_Small7.Text, "guid", iTalk_TextBox_Small1.Text));
                        }
                        else
                        {
                            UpdateNotifNumber();
                            ModifiedQuery.Add($"AnswerControl{iTalk_TextBox_Small1.Text}", QueryBuilder.UpdateFromQuery(TableCompte, "reponse", 1, iTalk_TextBox_Small7.Text, "guid", iTalk_TextBox_Small1.Text));
                        }

                    }
                }


            }
            catch (Exception fdfdf)
            {
                iTalk_LinkLabel7.Text = fdfdf.Message;
                iTalk_LinkLabel7.ForeColor = System.Drawing.Color.Red;
                iTalk_LinkLabel7.Enabled = false;
            }
        }

        private void iTalk_LinkLabel9_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e) //modifier les points
        {
            try
            {
                if (!string.IsNullOrEmpty(iTalk_TextBox_Small9.Text))
                {
                    string points = iTalk_TextBox_Small9.Text;
                    string firstpoints = AccountList.ReturnById(int.Parse(iTalk_TextBox_Small1.Text)).Points.ToString();
                    if (points != firstpoints)
                    {
                        AccountList.ModifyAccount(AccountList.ReturnById(int.Parse(iTalk_TextBox_Small1.Text)), AccountList.ReturnById(int.Parse(iTalk_TextBox_Small1.Text)).Account, points, 6);
                        
                        if (ModifiedQuery.ContainsKey($"PointsControl{iTalk_TextBox_Small1.Text}"))
                        {
                            ModifiedQuery.Remove($"PointsControl{iTalk_TextBox_Small1.Text}");
                            ModifiedQuery.Add($"PointsControl{iTalk_TextBox_Small1.Text}", QueryBuilder.UpdateFromQuery(TableCompte, "points", 1, iTalk_TextBox_Small9.Text, "guid", iTalk_TextBox_Small1.Text));
                        }
                        else
                        {
                            UpdateNotifNumber();
                            ModifiedQuery.Add($"PointsControl{iTalk_TextBox_Small1.Text}", QueryBuilder.UpdateFromQuery(TableCompte, "points", 1, iTalk_TextBox_Small9.Text, "guid", iTalk_TextBox_Small1.Text));
                        }

                    }
                }


            }
            catch (Exception fdfdf)
            {
                iTalk_LinkLabel9.Text = fdfdf.Message;
                iTalk_LinkLabel9.ForeColor = System.Drawing.Color.Red;
                iTalk_LinkLabel9.Enabled = false;
            }
        }

        private void iTalk_LinkLabel10_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e) // modifier ip
        {
            try
            {

                if (!string.IsNullOrEmpty(iTalk_TextBox_Small10.Text))
                {
                    string ip = iTalk_TextBox_Small10.Text;
                    if(IPAddress.TryParse(ip, out IPAddress address))
                    {
                        string firstip = AccountList.ReturnById(int.Parse(iTalk_TextBox_Small1.Text)).lastIp;
                        if (ip != firstip)
                        {
                            AccountList.ModifyAccount(AccountList.ReturnById(int.Parse(iTalk_TextBox_Small1.Text)), AccountList.ReturnById(int.Parse(iTalk_TextBox_Small1.Text)).Account, ip, 7);
                            
                            if (ModifiedQuery.ContainsKey($"IpControl{iTalk_TextBox_Small1.Text}"))
                            {
                                ModifiedQuery.Remove($"IpControl{iTalk_TextBox_Small1.Text}");
                                ModifiedQuery.Add($"IpControl{iTalk_TextBox_Small1.Text}", QueryBuilder.UpdateFromQuery(TableCompte, "lastIP", 1, iTalk_TextBox_Small10.Text, "guid", iTalk_TextBox_Small1.Text));
                            }
                            else
                            {
                                UpdateNotifNumber();
                                ModifiedQuery.Add($"IpControl{iTalk_TextBox_Small1.Text}", QueryBuilder.UpdateFromQuery(TableCompte, "lastIP", 1, iTalk_TextBox_Small10.Text, "guid", iTalk_TextBox_Small1.Text));
                            }
                        }
                    }
                    else
                    {
                        MessageBox.Show($"Merci de vérifier le format de l'adresse ip renseignée [{ip}].", "Adresse IP incorrecte", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
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

        private void iTalk_Button_15_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void iTalk_Button_22_Click(object sender, EventArgs e)
        {
           if(ModifiedQuery.Count > 0)
            {
                foreach(string v in ModifiedQuery.Values)
                {
                    DatabaseManager.UpdateQuery(v);
                }
            }
           UpdateNotifNumber(true);
           MessageBox.Show("Modifications effectuées.", "Applications des modifications", MessageBoxButtons.OK, MessageBoxIcon.Information);
           ModifiedQuery.Clear();
           listBox1.Items.Clear();
           iTalk_Label14.Text = $"{listBox1.Items.Count} comptes chargés.";
           Thread.Sleep(10);
           Load_editeur(InitializeForm.EMUSELECT);
        }
    }
}
