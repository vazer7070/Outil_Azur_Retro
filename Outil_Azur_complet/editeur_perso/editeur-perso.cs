using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Threading;
using System.Windows.Forms;
using Tools_protocol.Kryone.Database;

namespace Outil_Azur_complet.editeur_perso
{
    public partial class editeur_perso : Form
    {
        public editeur_perso()
        {
            InitializeComponent();
        }
        List<string> list = new List<string>();
        public string race;
        public string HoF;
        public string pv;
        public string jail;
        public string connect;
        public string groupe;
        private void iTalk_Button_23_Click(object sender, EventArgs e)
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

        private void editeur_perso_Load(object sender, EventArgs e)
        {
       
         CharacterList.PersoAll.Clear();
          CharacterList.AllPerso();
          AccountList.Accounts.Clear();
         AccountList.AllAccounts();
           GroupesList.groupe();
           foreach(string name in CharacterList.PersoAll)
            {
              listBox1.Items.Add(name);
          }
            foreach (String str in listBox1.Items)
            {
                list.Add(str.ToLower());
            }
            iTalk_TextBox_Small4.Enabled = false;
            iTalk_TextBox_Small16.Enabled = false;
            iTalk_TextBox_Small20.Enabled = false;
            iTalk_TextBox_Small1.Enabled = false;
            iTalk_TextBox_Small2.Enabled = false;
            iTalk_TextBox_Small3.Enabled = false;
            iTalk_Label1.Text = $"Nombre de personnages: {listBox1.Items.Count}";
        }

        private void listBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            iTalk_ComboBox1.Items.Clear();
            iTalk_ComboBox2.Items.Clear();
            iTalk_ComboBox3.Items.Clear();
            ItemList.ItemsId.Clear();
            iTalk_TextBox_Small4.Text = CharacterList.Listing(listBox1.SelectedItem.ToString()).Id.ToString();
            iTalk_TextBox_Small5.Text = CharacterList.Listing(listBox1.SelectedItem.ToString()).Name;
            iTalk_TextBox_Small8.Text = CharacterList.Listing(listBox1.SelectedItem.ToString()).Level.ToString();
            iTalk_TextBox_Small9.Text = CharacterList.Listing(listBox1.SelectedItem.ToString()).Xp.ToString();
            iTalk_TextBox_Small12.Text = CharacterList.Listing(listBox1.SelectedItem.ToString()).Kamas.ToString();
            iTalk_TextBox_Small13.Text = CharacterList.Listing(listBox1.SelectedItem.ToString()).Capital.ToString();
            iTalk_TextBox_Small14.Text = CharacterList.Listing(listBox1.SelectedItem.ToString()).Energy.ToString();
            iTalk_TextBox_Small17.Text = CharacterList.Listing(listBox1.SelectedItem.ToString()).Size.ToString();
            iTalk_TextBox_Small18.Text = CharacterList.Listing(listBox1.SelectedItem.ToString()).Map.ToString();
            iTalk_TextBox_Small19.Text = CharacterList.Listing(listBox1.SelectedItem.ToString()).Cell.ToString();
            iTalk_TextBox_Small20.Text = CharacterList.Listing(listBox1.SelectedItem.ToString()).Savepos;
            iTalk_TextBox_Small21.Text = CharacterList.Listing(listBox1.SelectedItem.ToString()).Deshonor.ToString();
            iTalk_TextBox_Small22.Text = CharacterList.Listing(listBox1.SelectedItem.ToString()).Honor.ToString();
            iTalk_TextBox_Small27.Text = CharacterList.Listing(listBox1.SelectedItem.ToString()).Intelligence.ToString();
            iTalk_TextBox_Small28.Text = CharacterList.Listing(listBox1.SelectedItem.ToString()).Chance.ToString();
            iTalk_TextBox_Small29.Text = CharacterList.Listing(listBox1.SelectedItem.ToString()).Agilite.ToString();
            iTalk_TextBox_Small30.Text = CharacterList.Listing(listBox1.SelectedItem.ToString()).Sagesse.ToString();
            iTalk_TextBox_Small31.Text = CharacterList.Listing(listBox1.SelectedItem.ToString()).Force.ToString();
            iTalk_TextBox_Small32.Text = CharacterList.Listing(listBox1.SelectedItem.ToString()).Vitalite.ToString();
            iTalk_TextBox_Small25.Text = CharacterList.Listing(listBox1.SelectedItem.ToString()).Gfx.ToString();
            iTalk_TextBox_Small2.Text = CharacterList.Listing(listBox1.SelectedItem.ToString()).Color1.ToString();
            iTalk_TextBox_Small1.Text = CharacterList.Listing(listBox1.SelectedItem.ToString()).Color2.ToString();
            iTalk_TextBox_Small3.Text = CharacterList.Listing(listBox1.SelectedItem.ToString()).Color3.ToString();
            iTalk_TextBox_Small11.Text = PersoClasse(CharacterList.Listing(listBox1.SelectedItem.ToString()).Class);
            iTalk_TextBox_Small10.Text = SexePerso(CharacterList.Listing(listBox1.SelectedItem.ToString()).Sexe);
            iTalk_TextBox_Small24.Text = AlignPerso(CharacterList.Listing(listBox1.SelectedItem.ToString()).Alignement);
            iTalk_TextBox_Small16.Text = PrisonPerso(CharacterList.Listing(listBox1.SelectedItem.ToString()).Prison);
            iTalk_Label35.Text = ConnectPerso(CharacterList.Listing(listBox1.SelectedItem.ToString()).Logged);
            iTalk_TextBox_Small23.Text = Wife(CharacterList.Listing(listBox1.SelectedItem.ToString()).Wife);
            iTalk_TextBox_Small6.Text = OwnerP(CharacterList.Listing(listBox1.SelectedItem.ToString()).Account);
            iTalk_TextBox_Small7.Text = Grades(CharacterList.Listing(listBox1.SelectedItem.ToString()).Groupe);
            CharacterList.GetInventory(listBox1.SelectedItem.ToString());
            CharacterList.GetSpells(listBox1.SelectedItem.ToString());
            backgroundWorker1.RunWorkerAsync();
            #region paramètre
            Properties.Settings.Default.EP_name = iTalk_TextBox_Small5.Text;

            #endregion

        }
        private void Action_editeur()
        {
           /* foreach(int id in ItemList.ItemsId)
            {
                iTalk_ComboBox1.Items.Add(ItemTemplateList.GetItem(id, 1));
            }*/
            foreach(string S in SpellsList.SpellsShow)
            {
                iTalk_ComboBox2.Items.Add(S);
            }
            string T = JobsList.LookJobs(listBox1.SelectedItem.ToString());
            if(T != null)
             {
                if (T.Contains(";"))
                {
                    foreach (string o in T.Split(';'))
                    {
                        string h = o.Split(',')[0];
                        int m = Convert.ToInt32(o.Split(',')[1]);
                       iTalk_ComboBox3.Items.Add($"{JobsList.Name_Jobs(h)} (XP: {m})");

                    }
                }
                else
                {
                    try
                    {
                        string h = T.Split(',')[0];
                        int m = Convert.ToInt32(T.Split(',')[1]);
                        iTalk_ComboBox3.Items.Add($"{JobsList.Name_Jobs(h)} (XP: {m})");
                    }
                    catch
                    {
                        iTalk_ComboBox3.Items.Add($"aucun métier");
                    }
                }
            }
           


        }
        public string PersoClasse(int classe)
        {
            switch (classe)
            {
                case 1:
                    race = "Féca";
                    break;
                case 2:
                    race = "Osamoda";;
                    break;
                case 3:
                    race = "Énutrof";
                    break;
                case 4:
                    race = "Sram";
                    break;
                case 5:
                    race = "Xélor";
                    break;
                case 6:
                    race = "Écaflip";
                    break;
                case 7:
                    race = "Éniripsa";
                    break;
                case 8:
                    race = "Iop";
                    break;
                case 9:
                    race = "Crâ";
                    break;
                case 10:
                    race = "Sadida";
                    break;
                case 11:
                    race = "Sacrieur";
                    break;
                case 12:
                    race = "Pandawa";
                    break;
                 
            }
            return race;
        }
        public string SexePerso(int sexe)
        {
            switch (sexe)
            {
                case 0:
                    HoF = "Mâle";
                    break;
                case 1:
                    HoF = "Femelle";
                    break;
            }
            return HoF;
        }
        
        public string AlignPerso(int pvp)
        {
            switch (pvp)
            {
                case 0:
                    pv = "Neutre";
                    break;
                case 1:
                    pv = "Ange";
                    break;
                case 2:
                    pv = "Démon";
                    break;
                case 3:
                    pv = "Sérianne";
                    break;

            }
            return pv;
        }
        public string PrisonPerso(long prison)
        {
            switch (prison)
            {
                case 1:
                    jail = "Prisonnier";
                    break;
                case 0:
                   jail = "Libre";
                    break;
            }
            return jail;
        }
        public string Wife(int id)
        {
            CharacterList.IdAccount.TryGetValue(id, out string wife);
            if (!string.IsNullOrEmpty(wife))
                return wife;
            return "Non marié";
        }
        public string OwnerP(int id)
        {
            CharacterList.IdCompte.TryGetValue(id, out string Owner);
            if (!string.IsNullOrEmpty(Owner))
                return Owner;
            return "";
        }
        public string ConnectPerso(int co)
        {
            switch (co)
            {
                case 0:
                    connect = "Hors-ligne";
                    iTalk_Label35.ForeColor = System.Drawing.Color.Red;
                    break;
                case 1:
                    connect = "En ligne";
                    iTalk_Label35.ForeColor = System.Drawing.Color.Green;
                    break;
            }
            return connect;
        }
        public string Grades(int value)
        {
            GroupesList.Grades.TryGetValue(value, out string rang);
            if (!string.IsNullOrEmpty(rang))
                return rang;
            return "Joueur";
        }
        
        private void iTalk_TextBox_Small11_TextChanged(object sender, EventArgs e)
        {

        }

        private void iTalk_Button_11_Click(object sender, EventArgs e)
        {

        }

        private void iTalk_ThemeContainer1_Click(object sender, EventArgs e)
        {

        }

        

        private void iTalk_LinkLabel19_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {

        }

        private void iTalk_LinkLabel5_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            iTalk_TextBox_Small5.Text = ModifyManager.ModifyAccepted("name", 9, "name", iTalk_TextBox_Small5.Text, iTalk_TextBox_Small4.Text);
        }

        private void iTalk_LinkLabel6_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {

        }

        private void backgroundWorker1_DoWork(object sender, DoWorkEventArgs e)
        {

        }

        private void backgroundWorker1_RunWorkerCompleted_1(object sender, RunWorkerCompletedEventArgs e)
        {
            Action_editeur();
        }
    }
}
