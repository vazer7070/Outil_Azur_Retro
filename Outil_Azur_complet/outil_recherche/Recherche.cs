using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tools_protocol.Data;
using Tools_protocol.Kryone.Database;
using Tools_protocol.Query;

namespace Outil_Azur_complet.outil_recherche
{
    public partial class Recherche : Form
    {
        public List<string> list = new List<string>();
        public List<string> list2 = new List<string>();
        public List<string> list3 = new List<string>();
        public int IDPANO;
        public uint k;
        public Recherche()
        {
            InitializeComponent();
        }

        private void iTalk_RadioButton1_CheckedChanged(object sender)
        {

        }

        private void iTalk_Button_11_Click(object sender, EventArgs e)
        {
            Hide();
        }

        private void iTalk_TextBox_Small2_TextChanged(object sender, EventArgs e)
        {

        }

        private void iTalk_Label1_Click(object sender, EventArgs e)
        {

        }

        private void iTalk_Button_21_Click(object sender, EventArgs e)
        {
            if (listBox1.Items != null)
                listBox1.Items.Clear();
            if (ItemTemplateList.U != null)
                ItemTemplateList.U.Clear();
            if (iTalk_Listview2.Items != null)
                iTalk_Listview2.Items.Clear();
            if (ItemTemplateList.StatsItems != null)
                ItemTemplateList.StatsItems.Clear();

            if (string.IsNullOrEmpty(iTalk_TextBox_Small1.Text) && string.IsNullOrEmpty(iTalk_TextBox_Small2.Text) && string.IsNullOrEmpty(iTalk_TextBox_Small3.Text))
            {
                MessageBox.Show("Merci de remplir au moins une information avant de lancer la recherche", "recherche impossible", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            else 
            {
                try
                {
                    int type;
                    int id = 0;
                    if(iTalk_TextBox_Small1.Text != "")
                    {
                        id = Convert.ToInt32(iTalk_TextBox_Small1.Text);
                    }
                    else if(iTalk_TextBox_Small1.Text == "" && iTalk_TextBox_Small2.Text != "")
                    {
                        id = ItemTemplateList.ReturnItemId(iTalk_TextBox_Small2.Text);

                    }else if(iTalk_TextBox_Small1.Text == "" && iTalk_TextBox_Small2.Text == "" && iTalk_TextBox_Small3.Text != "")
                    {
                        id = ItemTemplateList.ReturnItemType(Convert.ToInt32(iTalk_TextBox_Small3.Text));
                      
                    }
                    type = Convert.ToInt32(ItemTemplateList.GetItem(id, 2));
                    if (ItemTemplateList.ItemFullDico.ContainsKey(id))
                    {
                        ItemTemplateList.AddNameByData(type);
                        if(SearchManager.Search_pictureItem(id, type) != "")
                        {
                            pictureBox1.Image = Image.FromFile(SearchManager.Search_pictureItem(id, type));
                            ReturnInBox();
                            ListingInSearch(id);
                            SeeStats(id);
                            iTalk_Label18.Text = $"{listBox1.Items.Count} items similaires trouvés";
                        }
                        else
                        {
                            pictureBox1.Image = Image.FromFile(SearchManager.Search_pictureItem(0, 0));
                            ReturnInBox();
                            ListingInSearch(id);
                            SeeStats(id);
                            iTalk_Label18.Text = $"{listBox1.Items.Count} items similaires trouvés";
                        }
                    }
                    else
                    {
                        MessageBox.Show("L'ID renseigné n'est pas valide, merci de re-vérifier vos informations", "ID introuvable", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                        

                }
                catch(Exception o)
                {
                    MessageBox.Show(o.Message);
                    return;
                }
            }
        }
        public void SeeStats(int id)
        {
            ItemTemplateList.ParseTemplate(id);
            foreach(string st in ItemTemplateList.StatsItems)
            {
                ListViewItem LVI = new ListViewItem(st);
                iTalk_Listview2.Items.Add(LVI);
            }
        }
        public void SayNumberItems(int numberofitem)
        {
            switch (numberofitem)
            {
                case 2:
                    iTalk_ComboBox1.Items.Add("Avec 2 objet(s)");
                    break;
                case 3:
                    iTalk_ComboBox1.Items.Add("Avec 2 objet(s)");
                    iTalk_ComboBox1.Items.Add("Avec 3 objet(s)");
                    break;
                case 4:
                    iTalk_ComboBox1.Items.Add("Avec 2 objet(s)");
                    iTalk_ComboBox1.Items.Add("Avec 3 objet(s)");
                    iTalk_ComboBox1.Items.Add("Avec 4 objet(s)");
                    break;
                case 5:
                    iTalk_ComboBox1.Items.Add("Avec 2 objet(s)");
                    iTalk_ComboBox1.Items.Add("Avec 3 objet(s)");
                    iTalk_ComboBox1.Items.Add("Avec 4 objet(s)");
                    iTalk_ComboBox1.Items.Add("Avec 5 objet(s)");
                    break;
                case 6:
                    iTalk_ComboBox1.Items.Add("Avec 2 objet(s)");
                    iTalk_ComboBox1.Items.Add("Avec 3 objet(s)");
                    iTalk_ComboBox1.Items.Add("Avec 4 objet(s)");
                    iTalk_ComboBox1.Items.Add("Avec 5 objet(s)");
                    iTalk_ComboBox1.Items.Add("Avec 6 objet(s)");
                    break;
                case 7:
                    iTalk_ComboBox1.Items.Add("Avec 2 objet(s)");
                    iTalk_ComboBox1.Items.Add("Avec 3 objet(s)");
                    iTalk_ComboBox1.Items.Add("Avec 4 objet(s)");
                    iTalk_ComboBox1.Items.Add("Avec 5 objet(s)");
                    iTalk_ComboBox1.Items.Add("Avec 6 objet(s)");
                    iTalk_ComboBox1.Items.Add("Avec 7 objet(s)");
                    break;
                case 8:
                    iTalk_ComboBox1.Items.Add("Avec 2 objet(s)");
                    iTalk_ComboBox1.Items.Add("Avec 3 objet(s)");
                    iTalk_ComboBox1.Items.Add("Avec 4 objet(s)");
                    iTalk_ComboBox1.Items.Add("Avec 5 objet(s)");
                    iTalk_ComboBox1.Items.Add("Avec 6 objet(s)");
                    iTalk_ComboBox1.Items.Add("Avec 7 objet(s)");
                    iTalk_ComboBox1.Items.Add("Avec 8 objet(s)");
                    break;

            }
        }
        public void ReturnInBox()
        {
            foreach(string y in ItemTemplateList.U)
            {
                listBox1.Items.Add(y);
            }
        }
        private void ListingInSearch(int id)
        {
            iTalk_Label11.Text = id.ToString();
            iTalk_Label12.Text = ItemTemplateList.GetItem(id, 1);
            iTalk_Label13.Text = ItemTemplateList.GetItem(id, 3);
            iTalk_Label14.Text = ItemTemplateList.GetItem(id, 4);
            iTalk_Label15.Text = ItemTemplateList.GetItem(id, 5);
            iTalk_Label16.Text = ItemTemplateList.GetItem(id, 6);
            iTalk_Label24.Text = ItemTemplateList.GetItem(id, 7).Split(';')[0];
            iTalk_Label30.Text = ItemTemplateList.GetItem(id, 7).Split(';')[1];
            iTalk_Label26.Text = ItemTemplateList.GetItem(id, 7).Split(';')[2];
            iTalk_Label32.Text = ItemTemplateList.GetItem(id, 7).Split(';')[3];
            iTalk_Label34.Text = ItemTemplateList.GetItem(id, 7).Split(';')[4];
            iTalk_Label36.Text = ItemTemplateList.GetItem(id, 7).Split(';')[5];
            if (ItemTemplateList.GetItem(id, 7).Split(';')[6] == "1")
            {
                iTalk_Label37.Text = "oui";
            }
            else
            {
                iTalk_Label37.Text = "non";
            }
            iTalk_Label143.Text = ConditionsListing.TradConditions(ItemTemplateList.GetItem(id, 9));

        }
       private void ChoiceInList(string name)
        {
            int id = ItemTemplateList.ReturnItemId(name);
            int type = Convert.ToInt32(ItemTemplateList.GetItem(id, 2));
            string path = SearchManager.Search_pictureItem(id, type);
            if (!path.Equals(""))
            {
                pictureBox1.Image = Image.FromFile(path);
                SeeStats(id);
                ListingInSearch(id);
            }
            else
            {
                pictureBox1.Image = Image.FromFile(SearchManager.Search_pictureItem(0, 0));
                SeeStats(id);
                ListingInSearch(id);
            }
        }
        private void tabPage2_Click(object sender, EventArgs e)
        {

        }

        private void listBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listBox1.SelectedItem == null)
                return;
            if (iTalk_Listview2.Items != null)
                iTalk_Listview2.Items.Clear();
            if (ItemTemplateList.StatsItems != null)
                ItemTemplateList.StatsItems.Clear();
            ChoiceInList(listBox1.SelectedItem.ToString());

        }

        private void iTalk_Button_23_Click(object sender, EventArgs e)
        {
            if (listBox1.SelectedItem == null)
                return;
            ItemTemplateList.DeleteItem(listBox1.SelectedItem.ToString());
            ItemTemplateList.U.Remove(listBox1.SelectedItem.ToString());
            listBox1.Items.Remove(listBox1.SelectedItem);
            listBox1.Refresh();
        }

        private void iTalk_CheckBox1_CheckedChanged(object sender)
        {

        }

        private void Recherche_Load(object sender, EventArgs e)
        {
            foreach (string n in ItemSetList.SetName)
            {
                listBox2.Items.Add(n);
            }
            foreach(string b in DropsList.DropsName)
            {
                listBox4.Items.Add(b);
            }
            foreach(string vv in SpellsList.SpellsName)
            {
                listBox6.Items.Add(vv);
            }
            foreach(string str in listBox2.Items)
            {
                list.Add(str.ToLower());
            }
            foreach (string str in listBox4.Items)
            {
                list2.Add(str);
            }
            foreach(string str in listBox6.Items)
            {
                list3.Add(str);
            }
            iTalk_Label19.Text = $"{listBox2.Items.Count} panoplies chargées.";
            iTalk_Label38.Text = $"{listBox4.Items.Count} drops chargés.";
            iTalk_Label122.Text = $"{listBox6.Items.Count}";
        }

        private void listBox2_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (iTalk_Listview1.Items != null)
                iTalk_Listview1.Items.Clear();
            if (listBox2.SelectedItem == null)
                return;
            if (listBox3.Items != null)
                listBox3.Items.Clear();
            iTalk_ComboBox1.Items.Clear();
            int h = ItemSetList.ReturnPanoId(listBox2.SelectedItem.ToString());
            iTalk_Label21.Text = $"ID panoplie: {h}";
            IDPANO = h;
            string j = ItemSetList.ReturnItems(h,1);
            foreach(string o in j.Split(','))
            {
                int m = Convert.ToInt32(o);
                listBox3.Items.Add(ItemTemplateList.GetItem(m, 1));
            }
            SayNumberItems(listBox3.Items.Count);
            iTalk_Label20.Text = $"Items de la panoplie: {listBox3.Items.Count}";
        }

        private void listBox3_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listBox3.SelectedItem == null)
                return;
            int a = ItemTemplateList.ReturnItemId(listBox3.SelectedItem.ToString());
            int t = Convert.ToInt32(ItemTemplateList.GetItem(a, 2));
            string path = SearchManager.Search_pictureItem(a, t);
            if (!path.Equals(""))
            {
                pictureBox2.Image = Image.FromFile(path);
            }
            else
            {
                pictureBox2.Image = Image.FromFile(SearchManager.Search_pictureItem(0,0));
            }
        }

        private void iTalk_Listview1_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void iTalk_TextBox_Small4_TextChanged(object sender, EventArgs e)
        {
            if (String.IsNullOrEmpty(iTalk_TextBox_Small4.Text.Trim()) == false)
            {
                listBox2.Items.Clear();
                foreach (string str in list)
                {
                    if (str.StartsWith(iTalk_TextBox_Small4.Text.Trim()))

                    {
                        listBox2.Items.Add(str);
                    }
                }
            }

            else if (iTalk_TextBox_Small4.Text.Trim().Equals(""))
            {
                listBox2.Items.Clear();

                foreach (string str in list)
                {
                    listBox2.Items.Add(str);
                }
            }
        }

        private void iTalk_Label26_Click(object sender, EventArgs e)
        {

        }

        private void iTalk_ComboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (ItemSetList.Name_Effects != null)
                ItemSetList.Name_Effects.Clear();
            if (iTalk_Listview1.Items != null)
                iTalk_Listview1.Items.Clear();
            if (iTalk_ComboBox1.Items != null || IDPANO != 0)
            {
                string PO = iTalk_ComboBox1.SelectedItem.ToString();
                if (PO.Contains("2"))
                {
                    ItemSetList.ReturnEffect(IDPANO, 0);
                }
                else if (PO.Contains("3"))
                { 
                    ItemSetList.ReturnEffect(IDPANO, 1);
                }
                else if (PO.Contains("4"))
                {
                    ItemSetList.ReturnEffect(IDPANO, 2);
                }
                else if (PO.Contains("5"))
                {
                    ItemSetList.ReturnEffect(IDPANO, 3);
                }
                else if (PO.Contains("6"))
                {
                    ItemSetList.ReturnEffect(IDPANO, 4);
                }
                else if (PO.Contains("7"))
                {
                    ItemSetList.ReturnEffect(IDPANO, 5);
                }
                else if (PO.Contains("8"))
                {
                    ItemSetList.ReturnEffect(IDPANO, 6);
                }

            }
            foreach (string L in ItemSetList.Name_Effects)
            {
                ListViewItem LVI = new ListViewItem(L);
                iTalk_Listview1.Items.Add(LVI);
            }


        }
        public void MonsterDropInfo(uint id)
        {
            iTalk_Label62.Text = $"{id}";
            iTalk_Label63.Text = listBox5.SelectedItem.ToString();
        }
        private void tabPage8_Click(object sender, EventArgs e)
        {

        }

        private void iTalk_TextBox_Small5_TextChanged(object sender, EventArgs e)
        {
            if (String.IsNullOrEmpty(iTalk_TextBox_Small5.Text.Trim()) == false)
            {
                listBox4.Items.Clear();
                foreach (string str in list2)
                {
                    if (str.StartsWith(iTalk_TextBox_Small5.Text.Trim()))

                    {
                        listBox4.Items.Add(str);
                    }
                }
            }

            else if (iTalk_TextBox_Small5.Text.Trim().Equals(""))
            {
                listBox4.Items.Clear();

                foreach (string str in list2)
                {
                    listBox4.Items.Add(str);
                }
            }
        }
        public void SetDropInfo(uint id)
        {
            iTalk_Label53.Text = $"{id}";
            iTalk_Label54.Text = listBox4.SelectedItem.ToString();
            iTalk_Label55.Text = DropsList.DropInfo(id, 4);
            iTalk_Label56.Text = DropsList.DropInfo(id, 5);
            iTalk_Label57.Text = DropsList.DropInfo(id, 6);
            iTalk_Label58.Text = DropsList.DropInfo(id, 7);
            iTalk_Label59.Text = DropsList.DropInfo(id, 8);
            iTalk_Label60.Text = DropsList.DropInfo(id, 9);
            if(DropsList.DropInfo(id, 11) != "1")
            {
                iTalk_Label61.Text = "Objet scripté";
            }
            else
            {
                iTalk_Label61.Text = DropsList.DropInfo(id, 11);
            }
            iTalk_Label66.Text = DropsList.DropInfo(id, 10);
            iTalk_Label62.Text = "";
            iTalk_Label63.Text = "";
            if(pictureBox3.Image != null)
                pictureBox3.Image = null;


        }
        private void listBox4_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listBox4.SelectedItem == null)
                return;
            if (listBox5.Items != null)
                listBox5.Items.Clear();
            if (ItemTemplateList.StatsItems != null)
                ItemTemplateList.StatsItems.Clear();
            if (iTalk_Listview3.Items != null)
                iTalk_Listview3.Items.Clear();
             k = DropsList.DropId(listBox4.SelectedItem.ToString());
            string N = DropsList.DropInfo(k, 1);
            int id = Convert.ToInt32(DropsList.DropInfo(k, 3));
            int type = Convert.ToInt32(ItemTemplateList.GetItem(id, 2));
            int statid = Convert.ToInt32(DropsList.DropInfo(k, 3));
            ItemTemplateList.ParseTemplate(statid);
            foreach(string a in ItemTemplateList.StatsItems)
            {
                ListViewItem LVI = new ListViewItem(a);
                iTalk_Listview3.Items.Add(LVI);
            }
            string path = SearchManager.Search_pictureItem(id, type);
            SetDropInfo(k);
            if (path.Equals(""))
            {
                pictureBox4.Image = Image.FromFile(SearchManager.Search_pictureItem(0, 0));
            }
            else
            {
                pictureBox4.Image = Image.FromFile(SearchManager.Search_pictureItem(id,type));
            }
            if (string.IsNullOrEmpty(N))
            {
                listBox5.Items.Add("N/A mob");
                iTalk_Label39.Text = "aucun monstre trouvé";
            }
            else
            {
                listBox5.Items.Add(N);
                iTalk_Label39.Text = $"{listBox5.Items.Count} monstre(s) trouvé(s)";
            }
            
            
        }

        private void listBox5_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listBox5.SelectedItem == null)
                return;

            string cont = listBox5.SelectedItem.ToString();
            string b = DropsList.DropInfo(k, 2);
            if (cont == "N/A mob")
            {
                pictureBox3.Image = Image.FromFile(SearchManager.Search_pictureItem(0, 0));
            }
            else
            {
                pictureBox3.Image = Image.FromFile(SearchManager.Search_MonsterPicture(b));
                MonsterDropInfo(Convert.ToUInt32(b));
            }
        }

        private void iTalk_Button_28_Click(object sender, EventArgs e)
        {
            if (MonsterList.MonsterGrade != null)
                MonsterList.MonsterGrade.Clear();
            if (listBox7.Items != null)
                listBox7.Items.Clear();
            if (iTalk_Listview5.Items != null)
                iTalk_Listview5.Items.Clear();
            if (iTalk_Listview4.Items != null)
                iTalk_Listview4.Items.Clear();
            if (iTalk_Listview6.Items != null)
                iTalk_Listview6.Items.Clear();
            if (iTalk_TextBox_Small6.Text.Equals("") && iTalk_TextBox_Small8.Text.Equals(""))
            {
                MessageBox.Show("Merci de fournir l'ID ou le nom du monstre à rechercher.", "recherche impossible", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            else
            {
                if(!iTalk_TextBox_Small6.Text.Equals("") && string.IsNullOrEmpty(iTalk_TextBox_Small8.Text))
                {
                    int id = Convert.ToInt32(iTalk_TextBox_Small6.Text);
                    if (MonsterList.AllMonster.ContainsKey(id))
                    {
                        try
                        {
                            MonsterPicture(id);
                            MonsterLabelInfos(id);
                            GradeInfos(id);
                            MonsterDrop(id);
                       }
                        catch(Exception o)
                        {
                            MessageBox.Show(o.Message);
                            return;
                       }
                    }
                    else
                    {
                        MessageBox.Show("L'ID renseignée n'est pas valide, merci de vérifier vos informations.", "Monstre introuvable", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                        return;
                    }

                }else if(string.IsNullOrEmpty(iTalk_TextBox_Small6.Text) && !string.IsNullOrEmpty(iTalk_TextBox_Small8.Text))
                {
                    int id = MonsterList.ReturnMonsterByName(iTalk_TextBox_Small8.Text);
                    if (MonsterList.AllMonster.ContainsKey(id))
                    {
                        try
                        {
                            MonsterPicture(id);
                            MonsterLabelInfos(id);
                            GradeInfos(id);
                            MonsterDrop(id);

                        }
                        catch(Exception p)
                        {
                            MessageBox.Show(p.Message);
                            return;
                        }
                    }
                    else
                    {
                        MessageBox.Show("Le nom du monstre n'est pas correct, veuillez vérifier vos informations.", "Recherche impossible", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                        return;
                    }
                }
            }
        }
        public void MonsterDrop(int id)
        {
            uint D = DropsList.DropIdByMonster(id);
            foreach (var query in from cust in DropsList.AllDrops
                                  where cust.Value.MonsterId == Convert.ToUInt32(id)
                                  select cust.Value.ObjectName)
                iTalk_Listview4.Items.Add(query);
        }
        public void MonsterPicture(int id)
        {
            string P = SearchManager.Search_MonsterPicture(id.ToString());
            if (P == "")
            {
                pictureBox5.Image = Image.FromFile(SearchManager.Search_pictureItem(0, 0));
            }
            else
            {
                pictureBox5.Image = Image.FromFile(P);
            }
        }
        public void MonsterLabelInfos(int id)
        {
            iTalk_Label80.Text = id.ToString();
            iTalk_Label81.Text = MonsterList.ReturnMonstersInfos(id, 1);
            iTalk_Label82.Text = MonsterList.ReturnMonstersInfos(id, 2);
            iTalk_Label92.Text = MonsterList.ReturnAlignMonster(id);
            iTalk_Label86.Text = MonsterList.ReturnMonstersInfos(id, 3);
            iTalk_Label84.Text = MonsterList.CapturableOrNot(id);
            iTalk_Label89.Text = MonsterList.ReturnKamas(id);

        }
        public void GradeInfos(int id)
        {
            MonsterList.ParseGrade(id);
           
            foreach (string h in MonsterList.MonsterGrade.Keys)
            {
                listBox7.Items.Add(h);
            }
            
        }

        private void iTalk_Label91_Click(object sender, EventArgs e)
        {

        }

        private void iTalk_Label79_Click(object sender, EventArgs e)
        {

        }

        private void iTalk_Label97_Click(object sender, EventArgs e)
        {

        }

        private void listBox7_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (iTalk_Listview5.Items != null)
                iTalk_Listview5.Items.Clear();
            if (iTalk_Listview6.Items != null)
                iTalk_Listview6.Items.Clear();

            if (listBox7.SelectedItem.ToString().Equals(""))
            {

            }
            else
            {
                string u = MonsterList.MonsterGrade.FirstOrDefault(x => x.Key == listBox7.SelectedItem.ToString()).Value;
                string v = MonsterList.MonsterGrade.FirstOrDefault(x => x.Key == listBox7.SelectedItem.ToString()).Value.Split('/')[1];
                string w = MonsterList.MonsterGrade.FirstOrDefault(x => x.Key == listBox7.SelectedItem.ToString()).Value.Split('/')[2];

                string neutre = u.Split(';')[0];
                string resistanceEau = u.Split(';')[3];
                string resistanceTerre = u.Split(';')[1];
                string resistanceFeu = u.Split(';')[2];
                string resistanceAir = u.Split(';')[4];
                iTalk_Label94.Text = u.Split(';')[5];
                iTalk_Label98.Text = u.Split(';')[6].Split('/')[0];

                iTalk_Label106.Text = v.Split(',')[0];
                iTalk_Label108.Text = v.Split(',')[4];
                iTalk_Label110.Text = v.Split(',')[2];
                iTalk_Label112.Text = v.Split(',')[1];
                iTalk_Label114.Text = v.Split(',')[3];
                iTalk_Label104.Text = w.Split(';')[0];
                iTalk_Label72.Text = w.Split(';')[1];
                iTalk_Label96.Text = u.Split('/')[3];
                iTalk_Label90.Text = u.Split('/')[4].Split('%')[0];
                
                iTalk_Listview5.Items.Add($"Résistance Neutre: {neutre}");
                iTalk_Listview5.Items.Add($"Résistance Eau: {resistanceEau}");
                iTalk_Listview5.Items.Add($"Résistance Terre: {resistanceTerre}");
                iTalk_Listview5.Items.Add($"Résistance Feu: {resistanceFeu}");
                iTalk_Listview5.Items.Add($"Résistance Air: {resistanceAir}");
                if (u.Split('/').Count() >= 6)
                {
                    iTalk_Listview6.Items.Add(u.Split('/')[5]);
                }
                else
                {
                    string Mspells = u.Split('%')[1];
                    foreach(string SpellsStats in Mspells.Split(';'))
                    {
                        iTalk_Listview6.Items.Add($"{SpellsList.Return_spells(Convert.ToInt32(SpellsStats.Split('@')[0]))} (lvl:{SpellsStats.Split('@')[1]})");
                    }
                }
                
            }
        }

        private void tabPage3_Click(object sender, EventArgs e)
        {

        }

        private void iTalk_Label72_Click(object sender, EventArgs e)
        {

        }

        private void iTalk_Label104_Click(object sender, EventArgs e)
        {

        }

        private void listBox6_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (iTalk_Listview7.Items != null)
                iTalk_Listview7.Items.Clear();
            if (iTalk_Listview8.Items != null)
                iTalk_Listview8.Items.Clear();
            if (SpellsList.EffectLvl1 != null)
                SpellsList.EffectLvl1.Clear();
            if (SpellsList.EffectLvl2 != null)
                SpellsList.EffectLvl2.Clear();
            if (SpellsList.EffectLvl3 != null)
                SpellsList.EffectLvl3.Clear();
            if (SpellsList.EffectLvl4 != null)
                SpellsList.EffectLvl4.Clear();
            if (SpellsList.EffectLvl5 != null)
                SpellsList.EffectLvl5.Clear();
            if (SpellsList.EffectLvl6 != null)
                SpellsList.EffectLvl6.Clear();
            if (listBox6.SelectedItem == null)
            {

            }
            else
            {
                string pics = SearchManager.Search_SpellsPicture(SpellsList.ReturnSpellsIDByName(listBox6.SelectedItem.ToString()).ToString());
                if (pics.Equals(""))
                {
                    pictureBox6.Image = Image.FromFile(SearchManager.Search_pictureItem(0,0));
                }
                else
                {
                    pictureBox6.Image = Image.FromFile(pics);
                }
                iTalk_Label118.Text = SpellsList.ReturnSpellsIDByName(listBox6.SelectedItem.ToString()).ToString();
                iTalk_Label119.Text = listBox6.SelectedItem.ToString();
                SpellsList.ParseLevel(SpellsList.ReturnSpellsIDByName(listBox6.SelectedItem.ToString()));
                iTalk_Label117.Text = SpellsList.ReturnSpellSprite(SpellsList.ReturnSpellsIDByName(listBox6.SelectedItem.ToString())).ToString();
                DetailsLvl(1);
                


            }
        }
        public void DetailsLvl(int level)
        {

            iTalk_Label100.Text = "";
            iTalk_Label102.Text = "";
            iTalk_Label123.Text = "";
            iTalk_Label125.Text = "";
            iTalk_Label138.Text = "";
            iTalk_Label142.Text = "";
            iTalk_Label141.Text = "";
            iTalk_Label137.Text = "";
            iTalk_Label128.Text = "";
            iTalk_Label130.Text = "";
            iTalk_Label144.Text = "";
            iTalk_Label140.Text = "";
            iTalk_Label139.Text = "";
            iTalk_Listview7.Items.Clear();
            iTalk_Listview8.Items.Clear();
            SpellsList.NORM = 0;
            SpellsList.CRIT = 0;

            string spellname = iTalk_Label119.Text;
            if (level.Equals(1))
            {
                foreach(string lvl in SpellsList.EffectLvl1)
                {
                    if (lvl.Contains("?"))
                    {
                        
                            iTalk_Label100.Text = lvl.Split('?')[1]; //PA
                            iTalk_Label102.Text = lvl.Split('?')[2]; //PO
                            iTalk_Label123.Text = $"1/{lvl.Split('?')[3]}";//CC
                            if (lvl.Split('?')[4] == "0") //EC
                            {
                                iTalk_Label125.Text = "0";
                            }
                            else
                            {
                                iTalk_Label125.Text = $"1/{lvl.Split('?')[4]}";
                            }
                        string L_l = lvl.Split('?')[5];
                        string l_v = lvl.Split('?')[6];
                        string c_l = lvl.Split('?')[7];
                        string p_m = lvl.Split('?')[8];
                        string e_c = lvl.Split('?')[14];
                        if (L_l.Contains("true"))
                        {
                            iTalk_Label138.Text = "oui";
                        }
                        else
                        {
                            iTalk_Label138.Text = "non";
                        }
                        if (l_v.Contains("true"))
                        {
                            iTalk_Label142.Text = "oui";
                        }
                        else
                        {
                            iTalk_Label142.Text = "non";
                        }
                        if (c_l.Contains("true"))
                        {
                            iTalk_Label141.Text = "oui";
                        }
                        else
                        {
                            iTalk_Label141.Text = "non";
                        }
                        if (p_m.Contains("true"))
                        {
                            iTalk_Label137.Text = "oui";
                        }
                        else
                        {
                            iTalk_Label137.Text = "non";
                        }
                        if (e_c.Contains("true"))
                        {
                            iTalk_Label139.Text = "oui";
                        }
                        else
                        {
                            iTalk_Label139.Text = "non";
                        }
                        
                        iTalk_Label128.Text = lvl.Split('?')[9];//nb de fois par tour
                            iTalk_Label130.Text = $"{lvl.Split('?')[10]}"; //intervalle de relance
                            iTalk_Label144.Text = lvl.Split('?')[12]; //zone
                            iTalk_Label140.Text = lvl.Split('?')[13]; //niveau
                        
                    }
                    if (lvl.Contains("~"))
                    {
                        iTalk_Listview7.Items.Add(lvl.Split('@')[0]);
                        iTalk_Listview8.Items.Add(lvl.Split('~')[1].Split('§')[0]);
                    }else if(lvl.Contains("@") && !lvl.Contains("~"))
                    {
                        //iTalk_Listview7.Items.Add(lvl.Split('@')[0]);
                        iTalk_Listview7.Items.Add($"{lvl.Split('@')[0].Trim()} ({lvl.Split('@')[1].Trim()})");

                    }
                    else if(lvl.Contains("§") && !lvl.Contains("~"))
                    {
                        iTalk_Listview8.Items.Add(lvl.Split('§')[0]);

                    }

                  //MessageBox.Show($"{lvl}");
                }

            }else if (level.Equals(2))
            {
                foreach (string lvl in SpellsList.EffectLvl2)
                {
                    if (lvl.Contains("?"))
                    {

                        iTalk_Label100.Text = lvl.Split('?')[1]; //PA
                        iTalk_Label102.Text = lvl.Split('?')[2]; //PO
                        iTalk_Label123.Text = $"1/{lvl.Split('?')[3]}";//CC
                        if (lvl.Split('?')[4] == "0") //EC
                        {
                            iTalk_Label125.Text = "0";
                        }
                        else
                        {
                            iTalk_Label125.Text = $"1/{lvl.Split('?')[4]}";
                        }
                        string L_l = lvl.Split('?')[5];
                        string l_v = lvl.Split('?')[6];
                        string c_l = lvl.Split('?')[7];
                        string p_m = lvl.Split('?')[8];
                        string e_c = lvl.Split('?')[14];
                        if (L_l.Contains("true"))
                        {
                            iTalk_Label138.Text = "oui";
                        }
                        else
                        {
                            iTalk_Label138.Text = "non";
                        }
                        if (l_v.Contains("true"))
                        {
                            iTalk_Label142.Text = "oui";
                        }
                        else
                        {
                            iTalk_Label142.Text = "non";
                        }
                        if (c_l.Contains("true"))
                        {
                            iTalk_Label141.Text = "oui";
                        }
                        else
                        {
                            iTalk_Label141.Text = "non";
                        }
                        if (p_m.Contains("true"))
                        {
                            iTalk_Label137.Text = "oui";
                        }
                        else
                        {
                            iTalk_Label137.Text = "non";
                        }
                        if (e_c.Contains("true"))
                        {
                            iTalk_Label139.Text = "oui";
                        }
                        else
                        {
                            iTalk_Label139.Text = "non";
                        }

                        iTalk_Label128.Text = lvl.Split('?')[9];//nb de fois par tour
                        iTalk_Label130.Text = $"{lvl.Split('?')[10]}"; //intervalle de relance
                        iTalk_Label144.Text = lvl.Split('?')[12]; //zone
                        iTalk_Label140.Text = lvl.Split('?')[13]; //niveau
                        

                    }
                    if (lvl.Contains("~"))
                    {
                        iTalk_Listview7.Items.Add(lvl.Split('@')[0]);
                        iTalk_Listview8.Items.Add(lvl.Split('~')[1].Split('§')[0]);
                    }
                    else if (lvl.Contains("@") && !lvl.Contains("~"))
                    {
                        // iTalk_Listview7.Items.Add(lvl.Split('@')[0]);
                        iTalk_Listview7.Items.Add($"{lvl.Split('@')[0].Trim()} ({lvl.Split('@')[1].Trim()})");
                        MessageBox.Show($"ic");

                    }
                    else if (lvl.Contains("§") && !lvl.Contains("~"))
                    {
                        iTalk_Listview8.Items.Add(lvl.Split('§')[0]);
                       

                    }
                    

                    
                }
            }
            else if (level.Equals(3))
            {
                foreach (string lvl in SpellsList.EffectLvl3)
                {
                    if (lvl.Contains("?"))
                    {

                        iTalk_Label100.Text = lvl.Split('?')[1]; //PA
                        iTalk_Label102.Text = lvl.Split('?')[2]; //PO
                        iTalk_Label123.Text = $"1/{lvl.Split('?')[3]}";//CC
                        if (lvl.Split('?')[4] == "0") //EC
                        {
                            iTalk_Label125.Text = "0";
                        }
                        else
                        {
                            iTalk_Label125.Text = $"1/{lvl.Split('?')[4]}";
                        }
                        string L_l = lvl.Split('?')[5];
                        string l_v = lvl.Split('?')[6];
                        string c_l = lvl.Split('?')[7];
                        string p_m = lvl.Split('?')[8];
                        string e_c = lvl.Split('?')[14];
                        if (L_l.Contains("true"))
                        {
                            iTalk_Label138.Text = "oui";
                        }
                        else
                        {
                            iTalk_Label138.Text = "non";
                        }
                        if (l_v.Contains("true"))
                        {
                            iTalk_Label142.Text = "oui";
                        }
                        else
                        {
                            iTalk_Label142.Text = "non";
                        }
                        if (c_l.Contains("true"))
                        {
                            iTalk_Label141.Text = "oui";
                        }
                        else
                        {
                            iTalk_Label141.Text = "non";
                        }
                        if (p_m.Contains("true"))
                        {
                            iTalk_Label137.Text = "oui";
                        }
                        else
                        {
                            iTalk_Label137.Text = "non";
                        }
                        if (e_c.Contains("true"))
                        {
                            iTalk_Label139.Text = "oui";
                        }
                        else
                        {
                            iTalk_Label139.Text = "non";
                        }

                        iTalk_Label128.Text = lvl.Split('?')[9];//nb de fois par tour
                        iTalk_Label130.Text = $"{lvl.Split('?')[10]}"; //intervalle de relance
                        iTalk_Label144.Text = lvl.Split('?')[12]; //zone
                        iTalk_Label140.Text = lvl.Split('?')[13]; //niveau

                    }
                    if (lvl.Contains("~"))
                    {
                        iTalk_Listview7.Items.Add(lvl.Split('@')[0]);
                        iTalk_Listview8.Items.Add(lvl.Split('~')[1].Split('§')[0]);
                    }
                    else if (lvl.Contains("@") && !lvl.Contains("~"))
                    {
                        iTalk_Listview7.Items.Add(lvl.Split('@')[0]);
                        iTalk_Listview7.Items.Add($"{lvl.Split('@')[0].Trim()} ({lvl.Split('@')[1].Trim()})");

                    }
                    else if (lvl.Contains("§") && !lvl.Contains("~"))
                    {
                        iTalk_Listview8.Items.Add(lvl.Split('§')[0]);

                    }
                    
                }
            }
            else if (level.Equals(4))
            {
                foreach (string lvl in SpellsList.EffectLvl4)
                {
                    if (lvl.Contains("?"))
                    {

                        iTalk_Label100.Text = lvl.Split('?')[1]; //PA
                        iTalk_Label102.Text = lvl.Split('?')[2]; //PO
                        iTalk_Label123.Text = $"1/{lvl.Split('?')[3]}";//CC
                        if (lvl.Split('?')[4] == "0") //EC
                        {
                            iTalk_Label125.Text = "0";
                        }
                        else
                        {
                            iTalk_Label125.Text = $"1/{lvl.Split('?')[4]}";
                        }
                        string L_l = lvl.Split('?')[5];
                        string l_v = lvl.Split('?')[6];
                        string c_l = lvl.Split('?')[7];
                        string p_m = lvl.Split('?')[8];
                        string e_c = lvl.Split('?')[14];
                        if (L_l.Contains("true"))
                        {
                            iTalk_Label138.Text = "oui";
                        }
                        else
                        {
                            iTalk_Label138.Text = "non";
                        }
                        if (l_v.Contains("true"))
                        {
                            iTalk_Label142.Text = "oui";
                        }
                        else
                        {
                            iTalk_Label142.Text = "non";
                        }
                        if (c_l.Contains("true"))
                        {
                            iTalk_Label141.Text = "oui";
                        }
                        else
                        {
                            iTalk_Label141.Text = "non";
                        }
                        if (p_m.Contains("true"))
                        {
                            iTalk_Label137.Text = "oui";
                        }
                        else
                        {
                            iTalk_Label137.Text = "non";
                        }
                        if (e_c.Contains("true"))
                        {
                            iTalk_Label139.Text = "oui";
                        }
                        else
                        {
                            iTalk_Label139.Text = "non";
                        }

                        iTalk_Label128.Text = lvl.Split('?')[9];//nb de fois par tour
                        iTalk_Label130.Text = $"{lvl.Split('?')[10]}"; //intervalle de relance
                        iTalk_Label144.Text = lvl.Split('?')[12]; //zone
                        iTalk_Label140.Text = lvl.Split('?')[13]; //niveau

                    }
                    if (lvl.Contains("~"))
                    {
                        iTalk_Listview7.Items.Add(lvl.Split('@')[0]);
                        iTalk_Listview8.Items.Add(lvl.Split('~')[1].Split('§')[0]);
                    }
                    else if (lvl.Contains("@") && !lvl.Contains("~"))
                    {
                        iTalk_Listview7.Items.Add(lvl.Split('@')[0]);
                        iTalk_Listview7.Items.Add($"{lvl.Split('@')[0].Trim()} ({lvl.Split('@')[1].Trim()})");

                    }
                    else if (lvl.Contains("§") && !lvl.Contains("~"))
                    {
                        iTalk_Listview8.Items.Add(lvl.Split('§')[0]);

                    }
                    
                }
            }
            else if (level.Equals(5))
            {
                foreach (string lvl in SpellsList.EffectLvl5)
                {
                    if (lvl.Contains("?"))
                    {

                        iTalk_Label100.Text = lvl.Split('?')[1]; //PA
                        iTalk_Label102.Text = lvl.Split('?')[2]; //PO
                        iTalk_Label123.Text = $"1/{lvl.Split('?')[3]}";//CC
                        if (lvl.Split('?')[4] == "0") //EC
                        {
                            iTalk_Label125.Text = "0";
                        }
                        else
                        {
                            iTalk_Label125.Text = $"1/{lvl.Split('?')[4]}";
                        }
                        string L_l = lvl.Split('?')[5];
                        string l_v = lvl.Split('?')[6];
                        string c_l = lvl.Split('?')[7];
                        string p_m = lvl.Split('?')[8];
                        string e_c = lvl.Split('?')[14];
                        if (L_l.Contains("true"))
                        {
                            iTalk_Label138.Text = "oui";
                        }
                        else
                        {
                            iTalk_Label138.Text = "non";
                        }
                        if (l_v.Contains("true"))
                        {
                            iTalk_Label142.Text = "oui";
                        }
                        else
                        {
                            iTalk_Label142.Text = "non";
                        }
                        if (c_l.Contains("true"))
                        {
                            iTalk_Label141.Text = "oui";
                        }
                        else
                        {
                            iTalk_Label141.Text = "non";
                        }
                        if (p_m.Contains("true"))
                        {
                            iTalk_Label137.Text = "oui";
                        }
                        else
                        {
                            iTalk_Label137.Text = "non";
                        }
                        if (e_c.Contains("true"))
                        {
                            iTalk_Label139.Text = "oui";
                        }
                        else
                        {
                            iTalk_Label139.Text = "non";
                        }

                        iTalk_Label128.Text = lvl.Split('?')[9];//nb de fois par tour
                        iTalk_Label130.Text = $"{lvl.Split('?')[10]}"; //intervalle de relance
                        iTalk_Label144.Text = lvl.Split('?')[12]; //zone
                        iTalk_Label140.Text = lvl.Split('?')[13]; //niveau

                    }
                    if (lvl.Contains("~"))
                    {
                        iTalk_Listview7.Items.Add(lvl.Split('@')[0]);
                        iTalk_Listview8.Items.Add(lvl.Split('~')[1].Split('§')[0]);
                    }
                    else if (lvl.Contains("@") && !lvl.Contains("~"))
                    {
                        iTalk_Listview7.Items.Add(lvl.Split('@')[0]);
                        iTalk_Listview7.Items.Add($"{lvl.Split('@')[0].Trim()} ({lvl.Split('@')[1].Trim()})");

                    }
                    else if (lvl.Contains("§") && !lvl.Contains("~"))
                    {
                        iTalk_Listview8.Items.Add(lvl.Split('§')[0]);

                    }
                    
                }
            }
            else if (level.Equals(6))
            {
                foreach (string lvl in SpellsList.EffectLvl6)
                {
                    if (lvl.Contains("?"))
                    {

                        iTalk_Label100.Text = lvl.Split('?')[1]; //PA
                        iTalk_Label102.Text = lvl.Split('?')[2]; //PO
                        iTalk_Label123.Text = $"1/{lvl.Split('?')[3]}";//CC
                        if (lvl.Split('?')[4] == "0") //EC
                        {
                            iTalk_Label125.Text = "0";
                        }
                        else
                        {
                            iTalk_Label125.Text = $"1/{lvl.Split('?')[4]}";
                        }
                        string L_l = lvl.Split('?')[5];
                        string l_v = lvl.Split('?')[6];
                        string c_l = lvl.Split('?')[7];
                        string p_m = lvl.Split('?')[8];
                        string e_c = lvl.Split('?')[14];
                        if (L_l.Contains("true"))
                        {
                            iTalk_Label138.Text = "oui";
                        }
                        else
                        {
                            iTalk_Label138.Text = "non";
                        }
                        if (l_v.Contains("true"))
                        {
                            iTalk_Label142.Text = "oui";
                        }
                        else
                        {
                            iTalk_Label142.Text = "non";
                        }
                        if (c_l.Contains("true"))
                        {
                            iTalk_Label141.Text = "oui";
                        }
                        else
                        {
                            iTalk_Label141.Text = "non";
                        }
                        if (p_m.Contains("true"))
                        {
                            iTalk_Label137.Text = "oui";
                        }
                        else
                        {
                            iTalk_Label137.Text = "non";
                        }
                        if (e_c.Contains("true"))
                        {
                            iTalk_Label139.Text = "oui";
                        }
                        else
                        {
                            iTalk_Label139.Text = "non";
                        }

                        iTalk_Label128.Text = lvl.Split('?')[9];//nb de fois par tour
                        iTalk_Label130.Text = $"{lvl.Split('?')[10]}"; //intervalle de relance
                        iTalk_Label144.Text = lvl.Split('?')[12]; //zone
                        iTalk_Label140.Text = lvl.Split('?')[13]; //niveau

                    }
                    if (lvl.Contains("~"))
                    {
                        iTalk_Listview7.Items.Add(lvl.Split('@')[0]);
                        iTalk_Listview8.Items.Add(lvl.Split('~')[1].Split('§')[0]);
                    }
                    else if (lvl.Contains("@") && !lvl.Contains("~"))
                    {
                        iTalk_Listview7.Items.Add(lvl.Split('@')[0]);
                        iTalk_Listview7.Items.Add($"{lvl.Split('@')[0].Trim()} ({lvl.Split('@')[1].Trim()})");

                    }
                    else if (lvl.Contains("§") && !lvl.Contains("~"))
                    {
                        iTalk_Listview8.Items.Add(lvl.Split('§')[0]);

                    }
                    

                    
                }
            }
        }
        private void iTalk_TextBox_Small7_TextChanged(object sender, EventArgs e)
        {
            if (String.IsNullOrEmpty(iTalk_TextBox_Small7.Text.Trim()) == false)
            {
                listBox6.Items.Clear();
                foreach (string str in list3)
                {
                    if (str.StartsWith(iTalk_TextBox_Small7.Text.Trim()))

                    {
                        listBox6.Items.Add(str);
                    }
                }
            }
        }

        private void iTalk_ComboBox2_SelectedIndexChanged(object sender, EventArgs e)
        {
         
            if(iTalk_ComboBox2.SelectedItem == null)
            {

            }
            else if(iTalk_ComboBox2.SelectedItem.Equals("Niveau 1"))
            {
                DetailsLvl(1);

            }else if(iTalk_ComboBox2.SelectedItem.Equals("Niveau 2"))
            {
                DetailsLvl(2);
            }else if(iTalk_ComboBox2.SelectedItem.Equals("Niveau 3"))
            {
                DetailsLvl(3);
            }else if(iTalk_ComboBox2.SelectedItem.Equals("Niveau 4"))
            {
                DetailsLvl(4);
            }else if(iTalk_ComboBox2.SelectedItem.Equals("Niveau 5"))
            {
                DetailsLvl(5);
            }else if(iTalk_ComboBox2.SelectedItem.Equals("Niveau 6"))
            {
                DetailsLvl(6);
            }
        }

        private void iTalk_Label131_Click(object sender, EventArgs e)
        {

        }

        private void iTalk_Listview7_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void iTalk_ThemeContainer1_Click(object sender, EventArgs e)
        {

        }
    }
}
