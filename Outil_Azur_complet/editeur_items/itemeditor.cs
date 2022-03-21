using Syncfusion.WinForms.ListView;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_Editor.items;
using Tools_protocol.Data;
using Tools_protocol.Kryone.Database;
using Tools_protocol.Managers;
using Tools_protocol.Query;

namespace Outil_Azur_complet.editeur_items
{
    public partial class itemeditor : Form
    {
        #region itemeditor valeurs
        public Dictionary<string, string> EffectSelected = new Dictionary<string, string>();
        public List<string> ConditionsSelected = new List<string>();
        public Dictionary<string, string> RecipeContent = new Dictionary<string, string>();
        public string k;
        public string CP = "";
        public string requetecraft = "";
        public string requeteitem = "";
        public string AddInPano = "";
        public int pano = -1;
        public string[] kk = null;
        public string[] a = null;
        public string requeteTemplate = "";
        public string h;
        public string b;
        public string ArmeInfos = "";
        public bool H = false;
        public bool ECH = false;
        public StringBuilder SB = new StringBuilder();
        public string ConditionsFinal = "";
        public string TableCraft = EmuManager.ReturnTable("crafts", EmuManager.EMUSELECTED);
        public string TableItems = EmuManager.ReturnTable("items", EmuManager.EMUSELECTED);
        public string TableItemsTemplate = EmuManager.ReturnTable("Template", EmuManager.EMUSELECTED);
        public string TablePano = EmuManager.ReturnTable("panoplies", EmuManager.EMUSELECTED);
        public List<string> TradRecipe = new List<string>();
        public List<string> InCondi = new List<string>();
        public Dictionary<string, string> CreateQuery = new Dictionary<string, string>();
        string ColumsPano = EmuManager.ReturnPanoCol();
        #endregion
        #region inventory valeurs
        public List<string> list = new List<string>();
        public List<string> list2 = new List<string>();
        public List<string> listChecked = new List<string>();
        public Dictionary<string, ListViewItem> Items = new Dictionary<string, ListViewItem>();
        public Dictionary<string, List<string>> Persoinventory = new Dictionary<string, List<string>>();
        public Dictionary<string, SelectionClass> Selection = new Dictionary<string, SelectionClass>();
        public int GUID;
        public int ID;
        public bool selected = false;
        public string selectionned;
        public string fullitem;
        public int CountForAdd = 0;
        public class SelectionClass
        {
            public string id;
            public string Template;
            public string stats;
            public string name;
            public int Count;
            public bool action;
            public string player_name;
            public bool New;

        }
        #endregion

        public itemeditor()
        {
            InitializeComponent();
        }

       
        private void Itemeditor_Load(object sender, EventArgs e)
        {
            LoadStatic();
            LoadStaticInventory();
        }
        #region éditeur d'items
        private void iTalk_Button_11_Click(object sender, EventArgs e)
        {
            ClearAllList();
            Hide();
        }
        public void ClearAllList()
        {
            EffectSelected.Clear();
            ConditionsSelected.Clear();
            RecipeContent.Clear();
            TradRecipe.Clear();
            CreateQuery.Clear();
            EditorManager.TradStat.Clear();
        }
        public void LoadStatic()
        {
            foreach (string v in ConditionsListing.ConditionsDico.Values)
            {
                iTalk_ComboBox3.Items.Add(v);
            }
            foreach (ItemSetList I in ItemSetList.AllItemsInSet.Values)
            {
                iTalk_ComboBox2.Items.Add(I.Name);
            }
            iTalk_ComboBox5.Enabled = false;
            iTalk_NumericUpDown5.Enabled = false;
            iTalk_LinkLabel2.Enabled = false;
            listBox1.Enabled = false;
            foreach (string id in EffectsListing.ItemEffectList.Values)
            {
                listBox2.Items.Add(id);
            }
            foreach (ItemTemplateList IT in ItemTemplateList.ItemFullDico.Values)
            {
                int T = IT.Type;
                if (T.Equals(38) || T.Equals(39) || T.Equals(40) || T.Equals(41) || T.Equals(36) || T.Equals(35) || T.Equals(34) || T.Equals(47) || T.Equals(48) || T.Equals(52) || T.Equals(53) || T.Equals(54) || T.Equals(55) || T.Equals(56) || T.Equals(57) || T.Equals(58) || T.Equals(59) || T.Equals(60) || T.Equals(62) || T.Equals(63) || T.Equals(64) || T.Equals(65) || T.Equals(70) || T.Equals(95) || T.Equals(96) || T.Equals(98) || T.Equals(103) || T.Equals(104) || T.Equals(105) || T.Equals(106) || T.Equals(107) || T.Equals(108) || T.Equals(109) || T.Equals(110) || T.Equals(111))
                {
                    iTalk_ComboBox5.Items.Add(IT.Name);
                }
            }
        }

        private void ITalk_CheckBox4_CheckedChanged(object sender)
        {
            if (iTalk_CheckBox4.Checked)
            {
                iTalk_ComboBox5.Enabled = true;
                iTalk_NumericUpDown5.Enabled = true;
                iTalk_LinkLabel2.Enabled = true;
                iTalk_LinkLabel5.Enabled = true;
                listBox1.Enabled = true;
            }
            else
            {
                iTalk_LinkLabel5.Enabled = false;
                iTalk_ComboBox5.Enabled = false;
                iTalk_NumericUpDown5.Enabled = false;
                iTalk_LinkLabel2.Enabled = false;
                listBox1.Enabled = false;
            }
        }

        private void ListBox2_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (!listBox2.SelectedItem.ToString().Contains("$1") && !listBox2.SelectedItem.ToString().Contains("$2"))
            {
                iTalk_TextBox_Small11.Enabled = false;
                iTalk_TextBox_Small12.Enabled = false;

            }
            else if (listBox2.SelectedItem.ToString().Contains("$1") && !listBox2.SelectedItem.ToString().Contains("$2"))
            {
                iTalk_TextBox_Small11.Enabled = true;
                iTalk_TextBox_Small12.Enabled = false;
            }
            else if (listBox2.SelectedItem.ToString().Contains("$1") && listBox2.SelectedItem.ToString().Contains("$2"))
            {
                iTalk_TextBox_Small11.Enabled = true;
                iTalk_TextBox_Small12.Enabled = true;
            }
        }

        private void ITalk_LinkLabel3_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            if (listBox2.SelectedItem != null)
            {
                try
                {
                    string E = listBox2.SelectedItem.ToString();
                    if (E.Contains("$1") && !E.Contains("$2"))
                    {
                        if (!String.IsNullOrEmpty(iTalk_TextBox_Small11.Text))
                        {
                            string T = E.Replace("$1", iTalk_TextBox_Small11.Text);
                            EffectSelected.Add($"{T}!{EffectsListing.ReturnIdItemEffect(E)}", iTalk_TextBox_Small11.Text);
                            listBox3.Items.Add(T);
                        }
                    }
                    else if (E.Contains("$1") && E.Contains("$2"))
                    {
                        if (Convert.ToInt32(iTalk_TextBox_Small11.Text) > Convert.ToInt32(iTalk_TextBox_Small12.Text))
                        {
                            if (!String.IsNullOrEmpty(iTalk_TextBox_Small11.Text) && !String.IsNullOrEmpty(iTalk_TextBox_Small12.Text))
                            {
                                string H = E.Replace("$1", iTalk_TextBox_Small12.Text).Replace("$2", iTalk_TextBox_Small11.Text);
                                EffectSelected.Add($"{H}!{EffectsListing.ReturnIdItemEffect(E)}", $"{iTalk_TextBox_Small12.Text}|{iTalk_TextBox_Small11.Text}");
                                listBox3.Items.Add(H);
                            }
                        }
                        else
                        {
                            if (!String.IsNullOrEmpty(iTalk_TextBox_Small11.Text) && !String.IsNullOrEmpty(iTalk_TextBox_Small12.Text))
                            {
                                string H = E.Replace("$1", iTalk_TextBox_Small11.Text).Replace("$2", iTalk_TextBox_Small12.Text);
                                EffectSelected.Add($"{H}!{EffectsListing.ReturnIdItemEffect(E)}", $"{iTalk_TextBox_Small11.Text}|{iTalk_TextBox_Small12.Text}");
                                listBox3.Items.Add(H);
                            }
                        }
                    }
                    else if (!E.Contains("$1") && !E.Contains("$2"))
                    {
                        EffectSelected.Add($"{E}!{EffectsListing.ReturnIdItemEffect(E)}", "@");
                        listBox3.Items.Add(E);
                    }
                }
                catch (Exception p)
                {
                    MessageBox.Show(p.Message, "Impossible d'effectuer l'action voulue", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }
            iTalk_TextBox_Small11.ResetText();
            iTalk_TextBox_Small12.ResetText();
        }

        private void ITalk_LinkLabel4_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            if (listBox3.SelectedItem != null)
            {

                string l = EffectSelected.FirstOrDefault(x => x.Key.Split('!')[0] == listBox3.SelectedItem.ToString()).Key;
                EffectSelected.Remove(l);
                listBox3.Items.Remove(listBox3.SelectedItem.ToString());
                listBox3.Update();

            }
        }

        private void ITalk_LinkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            if (iTalk_ComboBox3.SelectedItem != null)
            {
                if (iTalk_ComboBox6.SelectedItem != null)
                {
                    string o = ConditionsListing.ReturnConditionIdByName(iTalk_ComboBox3.SelectedItem.ToString());
                    string u = $"{o}{iTalk_ComboBox4.SelectedItem.ToString()}{iTalk_NumericUpDown4.Value}";
                    if (!InCondi.Contains(o))
                    {
                        InCondi.Add(o);
                        ConditionsSelected.Add($"{u}%{ iTalk_ComboBox6.SelectedItem.ToString()}");
                    }
                    else
                    {
                        MessageBox.Show("La condition voulue est déjà en attente de validation.", "Erreur de conditions", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                        return;
                    }
                }
                else
                {
                    MessageBox.Show("Merci de selectionner ce qu'il doit y avoir entre chaques conditions", "Erreur de conditions", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                    return;
                }
            }
            iTalk_NotificationNumber1.Value = ConditionsSelected.Count;
        }

        private void ITalk_LinkLabel2_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            if (iTalk_ComboBox5.SelectedItem != null)
            {
                int Y = ItemTemplateList.ReturnItemId(iTalk_ComboBox5.SelectedItem.ToString());
                if (!RecipeContent.ContainsKey(Y.ToString()))
                {

                    if (iTalk_NumericUpDown5.Value > 0)
                    {
                        RecipeContent.Add(Y.ToString(), iTalk_NumericUpDown5.Value.ToString());
                        listBox1.Items.Add($"{iTalk_ComboBox5.SelectedItem.ToString()} x {iTalk_NumericUpDown5.Value}");
                    }
                    else
                    {
                        MessageBox.Show("Merci de rentrer une quantitée supérieur à 0", "Erreur de composition", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                        return;
                    }
                }
                else
                {
                    MessageBox.Show("La recette contient déjà l'ingrédient sélectionné.", "Erreur de composition", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                    return;
                }
            }
            else
            {
                MessageBox.Show("Merci de rentrer une recette correcte.", "Erreur de composition", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }
        }
        private void ITalk_LinkLabel5_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            if (listBox1.SelectedItem != null)
            {
                int O = ItemTemplateList.ReturnItemId(listBox1.SelectedItem.ToString());
                RecipeContent.Remove(O.ToString());
                listBox1.Items.Remove(listBox1.SelectedItem);
                listBox1.Update();
            }
        }
        public void TradRecipeList()
        {
            foreach (string h in RecipeContent.Keys)
            {
                string j = RecipeContent.FirstOrDefault(x => x.Key == h).Value;
                TradRecipe.Add($"{h}*{j}");
            }

        }
        public void AddInTradItem()
        {
            if (EffectSelected != null)
            {
                foreach (string y in EffectSelected.Keys)
                {
                    string l = EffectSelected.FirstOrDefault(x => x.Key == y).Value;
                    EditorManager.CreateStatItems($"{y.Split('!')[1]}%{l}");
                }
            }
        }
        public void PrepareConditions()
        {

            if (ConditionsSelected != null)
            {
                foreach (string u in ConditionsSelected)
                {
                    SB.Append(EditorManager.ConditionsParse(u, ConditionsSelected.Count));
                }
                if (SB.ToString().EndsWith(iTalk_ComboBox6.SelectedItem.ToString()))
                    ConditionsFinal = SB.ToString().Substring(0, SB.ToString().Length - 1);
            }

        }
        public void PrepareCreateItem()
        {
            PrepareConditions();
            TradRecipeList();
            AddInTradItem();
            kk = EditorManager.TradStat.ToArray();
            a = TradRecipe.ToArray();
            h = string.Join(";", a);
            b = string.Join(",", kk);
            if (iTalk_ComboBox2.SelectedItem != null)
                pano = ItemSetList.ReturnPanoId(iTalk_ComboBox2.SelectedItem.ToString());
            ArmeInfos = $"{iTalk_TextBox_Small4.Text};{iTalk_TextBox_Small5.Text};{iTalk_TextBox_Small8.Text};{iTalk_TextBox_Small3.Text};{iTalk_TextBox_Small6.Text};{iTalk_TextBox_Small7.Text};{Convert.ToInt32(H)}";
        }
        private void ITalk_Button_21_Click(object sender, EventArgs e)
        {

            try
            {
                //if (!ItemList.ItemsId.Contains(Convert.ToInt32(iTalk_TextBox_Small9.Text)))
                // {
                    if (String.IsNullOrEmpty(iTalk_TextBox_Small9.Text) || iTalk_TextBox_Small9.Text == "0")
                    {
                        return;
                    }
                    if (!ItemTemplateList.ItemFullDico.Keys.Contains(Convert.ToInt32(iTalk_TextBox_Small2.Text)))
                    {

                        if (!ItemTemplateList.ItemFullDico.ContainsKey(ItemTemplateList.ReturnItemId(iTalk_TextBox_Small1.Text)))
                        {
                            if (!String.IsNullOrEmpty(iTalk_TextBox_Small3.Text) && !String.IsNullOrEmpty(iTalk_TextBox_Small4.Text) && !String.IsNullOrEmpty(iTalk_TextBox_Small5.Text) && !String.IsNullOrEmpty(iTalk_TextBox_Small6.Text) && !String.IsNullOrEmpty(iTalk_TextBox_Small7.Text) && !String.IsNullOrEmpty(iTalk_TextBox_Small8.Text))
                            {

                                if (iTalk_RadioButton1.Checked) //swf uniquement
                                {
                                    PrepareCreateItem();
                                    EditorManager.CreateSwf($@".\swf\{EmuManager.EMUSELECTED}_{iTalk_TextBox_Small2.Text}.txt", EditorManager.CreateSwfLine(Convert.ToInt32(iTalk_TextBox_Small2.Text), iTalk_TextBox_Small1.Text, iTalk_TextBox_Small13.Text, iTalk_TextBox_Small14.Text, iTalk_ComboBox1.SelectedItem.ToString(), iTalk_NumericUpDown1.Value.ToString(), iTalk_CheckBox7.Checked, iTalk_NumericUpDown2.Value.ToString(), $"{iTalk_TextBox_Small4.Text};{iTalk_TextBox_Small5.Text};{iTalk_TextBox_Small8.Text};{iTalk_TextBox_Small3.Text};{iTalk_TextBox_Small6.Text};{iTalk_TextBox_Small7.Text}", ConditionsFinal, iTalk_NumericUpDown3.Value.ToString(), iTalk_CheckBox8.Checked, iTalk_CheckBox2.Checked));
                                }
                                else if (iTalk_RadioButton2.Checked) //sql uniquement
                                {
                                    PrepareCreateItem();
                                    if (iTalk_CheckBox6.Checked)
                                    {
                                        if (iTalk_CheckBox4.Checked && RecipeContent != null)
                                        {
                                            requetecraft = QueryBuilder.InsertIntoQuery(TableCraft, new string[] { }, new string[] { iTalk_TextBox_Small2.Text, h }, "");
                                            CreateQuery.Add("craft", requetecraft);
                                        }
                                        if (iTalk_ComboBox6.SelectedItem != null)
                                        {
                                            string rowP = EmuManager.UpdateRowPano(EmuManager.RecupPanoRow(ColumsPano, iTalk_ComboBox2.SelectedItem.ToString()), iTalk_TextBox_Small2.ToString());
                                            AddInPano = QueryBuilder.UpdateFromQuery(TablePano, ColumsPano, 1, rowP, EmuManager.ReturnInfoCol("pano"), iTalk_ComboBox2.SelectedItem.ToString());
                                            CreateQuery.Add("pano", AddInPano);
                                        }
                                        requeteitem = QueryBuilder.InsertIntoQuery(TableItems, new string[] { }, new string[] { iTalk_TextBox_Small9.Text, iTalk_TextBox_Small2.Text, "0", "-1", "", "0" }, "");
                                        requeteTemplate = QueryBuilder.InsertIntoQuery(TableItemsTemplate, new string[] { }, new string[] { iTalk_TextBox_Small2.Text, $"{EditorManager.SwitchType(iTalk_ComboBox1.SelectedItem.ToString()).ToString()}", iTalk_TextBox_Small1.Text, iTalk_NumericUpDown1.Value.ToString(), b, iTalk_NumericUpDown2.Value.ToString(), pano.ToString(), iTalk_NumericUpDown3.Value.ToString(), ConditionsFinal, ArmeInfos, "0", "0", iTalk_TextBox_Small10.Text, "0", $"{Convert.ToInt32(ECH)}", "0" }, "");
                                        CreateQuery.Add("item", requeteitem);
                                        CreateQuery.Add("template", requeteTemplate);
                                        EditorManager.CreateSQLEditor($"items_{EmuManager.EMUSELECTED}", CreateQuery);

                                    }
                                    if (iTalk_CheckBox5.Checked) //injection directe
                                    {
                                        if (iTalk_CheckBox4.Checked && RecipeContent != null)
                                        {
                                            requetecraft = QueryBuilder.InsertIntoQuery(TableCraft, new string[] { }, new string[] { iTalk_TextBox_Small2.Text, h }, "");
                                            CreateQuery.Add("craft", requetecraft);

                                        }
                                        if (iTalk_ComboBox6.SelectedItem != null)
                                        {
                                            string rowP = EmuManager.UpdateRowPano(EmuManager.RecupPanoRow(ColumsPano, iTalk_ComboBox2.SelectedItem.ToString()), iTalk_TextBox_Small2.Text);
                                            AddInPano = QueryBuilder.UpdateFromQuery(TablePano, ColumsPano, 1, rowP, EmuManager.ReturnInfoCol("pano"), iTalk_ComboBox2.SelectedItem.ToString());
                                            CreateQuery.Add("pano", AddInPano);
                                        }
                                        requeteitem = QueryBuilder.InsertIntoQuery(TableItems, new string[] { }, new string[] { iTalk_TextBox_Small9.Text, iTalk_TextBox_Small2.Text, "0", "-1", "", "0" }, "");
                                        requeteTemplate = QueryBuilder.InsertIntoQuery(TableItemsTemplate, new string[] { }, new string[] { iTalk_TextBox_Small2.Text, $"{EditorManager.SwitchType(iTalk_ComboBox1.SelectedItem.ToString()).ToString()}", iTalk_TextBox_Small1.Text, iTalk_NumericUpDown1.Value.ToString(), b, iTalk_NumericUpDown2.Value.ToString(), pano.ToString(), iTalk_NumericUpDown3.Value.ToString(), ConditionsFinal, ArmeInfos, "0", "0", iTalk_TextBox_Small10.Text, "0", $"{Convert.ToInt32(ECH)}", "0" }, "");
                                        CreateQuery.Add("item", requeteitem);
                                        CreateQuery.Add("template", requeteTemplate);
                                        EditorManager.InjectSql(CreateQuery);
                                    }


                                }
                                else if (iTalk_RadioButton3.Checked) // swf et sql
                                {
                                    PrepareCreateItem();
                                    if (iTalk_CheckBox6.Checked)
                                    {
                                        if (iTalk_CheckBox4.Checked && RecipeContent != null)
                                        {
                                            requetecraft = QueryBuilder.InsertIntoQuery(TableCraft, new string[] { }, new string[] { iTalk_TextBox_Small2.Text, h }, "");
                                            CreateQuery.Add("craft", requetecraft);
                                        }
                                        if (iTalk_ComboBox6.SelectedItem != null)
                                        {
                                            string rowP = EmuManager.UpdateRowPano(EmuManager.RecupPanoRow(ColumsPano, iTalk_ComboBox2.SelectedItem.ToString()), iTalk_TextBox_Small2.Text);
                                            AddInPano = QueryBuilder.UpdateFromQuery(TablePano, ColumsPano, 1, rowP, EmuManager.ReturnInfoCol("pano"), iTalk_ComboBox2.SelectedItem.ToString());
                                            CreateQuery.Add("pano", AddInPano);
                                        }
                                        requeteitem = QueryBuilder.InsertIntoQuery(TableItems, new string[] { }, new string[] { iTalk_TextBox_Small9.Text, iTalk_TextBox_Small2.Text, "0", "-1", "", "0" }, "");
                                        requeteTemplate = QueryBuilder.InsertIntoQuery(TableItemsTemplate, new string[] { }, new string[] { iTalk_TextBox_Small2.Text, $"{EditorManager.SwitchType(iTalk_ComboBox1.SelectedItem.ToString()).ToString()}", iTalk_TextBox_Small1.Text, iTalk_NumericUpDown1.Value.ToString(), b, iTalk_NumericUpDown2.Value.ToString(), pano.ToString(), iTalk_NumericUpDown3.Value.ToString(), ConditionsFinal, ArmeInfos, "0", "0", iTalk_TextBox_Small10.Text, "0", $"{Convert.ToInt32(ECH)}", "0" }, "");
                                        CreateQuery.Add("item", requeteitem);
                                        CreateQuery.Add("template", requeteTemplate);
                                        EditorManager.CreateSQLEditor($"items_{EmuManager.EMUSELECTED}", CreateQuery);
                                        if (iTalk_CheckBox5.Checked)
                                        {
                                            EditorManager.InjectSql(CreateQuery);
                                        }
                                    }
                                    EditorManager.CreateSwf($@".\swf\{EmuManager.EMUSELECTED}_{iTalk_TextBox_Small2.Text}.txt", EditorManager.CreateSwfLine(Convert.ToInt32(iTalk_TextBox_Small2.Text), iTalk_TextBox_Small1.Text, iTalk_TextBox_Small13.Text, iTalk_TextBox_Small14.Text, $"{EditorManager.SwitchType(iTalk_ComboBox1.SelectedItem.ToString())}", iTalk_NumericUpDown1.Value.ToString(), iTalk_CheckBox7.Checked, iTalk_NumericUpDown2.Value.ToString(), $"{iTalk_TextBox_Small4.Text};{iTalk_TextBox_Small5.Text};{iTalk_TextBox_Small8.Text};{iTalk_TextBox_Small3.Text};{iTalk_TextBox_Small6.Text};{iTalk_TextBox_Small7.Text}", ConditionsFinal, iTalk_NumericUpDown3.Value.ToString(), iTalk_CheckBox8.Checked, iTalk_CheckBox2.Checked));

                                }
                                else
                                {
                                    MessageBox.Show("Merci de sélectionner un moyen d'exportation.", "Création impossible", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                                    return;
                                }
                                MessageBox.Show($"La création de l'objet {iTalk_TextBox_Small1.Text} est réussie.", $"ID: {iTalk_TextBox_Small2.Text}", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                                ClearAllList();
                            }
                            else
                            {
                                MessageBox.Show("Merci de remplir corrrectement toutes les informations requises.", "Création impossible", MessageBoxButtons.OK, MessageBoxIcon.Information);
                                return;
                            }
                        }
                        else
                        {
                            MessageBox.Show($"Le nom {iTalk_TextBox_Small1.Text} existe déjà.", "Création impossible", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                            return;
                        }

                    }
                    else
                    {
                        MessageBox.Show($"Le template '{iTalk_TextBox_Small2.Text}' existe déjà.", "Création impossible", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                        return;
                    }
              //  }
               // else
               // {
               //     MessageBox.Show($"L'objet avec le GUID: '{iTalk_TextBox_Small9.Text}' existe déjà.", "Créatioon impossible", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                //    return;
                //}
            }
            catch (Exception a)
            {
                MessageBox.Show(a.Message);
                return;
            }
            ClearAll();
        }
        private void ClearAll()
        {
            CreateQuery.Clear();
            ClearAllList();


        }
        private void ITalk_CheckBox2_CheckedChanged(object sender)
        {
            if (iTalk_CheckBox2.Checked)
            {
                H = true;
            }
            else
            {
                H = false;
            }
        }

        private void ITalk_CheckBox1_CheckedChanged(object sender)
        {
            if (iTalk_CheckBox1.Checked)
                ECH = true;
            ECH = false;
        }

        private void ITalk_ComboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void ITalk_ComboBox2_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void ITalk_Button_12_Click(object sender, EventArgs e)
        {

        }

        private void ITalk_RadioButton1_CheckedChanged(object sender)
        {
            if (iTalk_RadioButton1.Checked)
            {
                iTalk_CheckBox5.Enabled = false;
            }
            else
            {
                iTalk_CheckBox5.Enabled = true;
            }


        }

        private void TabPage1_Click(object sender, EventArgs e)
        {

        }

        private void iTalk_CheckBox5_CheckedChanged(object sender)
        {
            if(iTalk_CheckBox5.Checked == true)
            {
                iTalk_RadioButton1.Enabled = false;
            }
            else
            {
                iTalk_RadioButton1.Enabled=true;
            }
        }

        private void iTalk_Label26_Click(object sender, EventArgs e)
        {

        }

        private void iTalk_ComboBox5_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        #endregion
        #region editeur d'inventaire
        public void LoadStaticInventory()
        {
           
            CharacterList.AllPerso();
            foreach(string s in CharacterList.PersoAll)
            {
                listBox4.Items.Add(s);
                list.Add(s);
            }
            foreach(int item in ItemTemplateList.ItemFullDico.Keys)
            {
                listBox5.Items.Add(ItemTemplateList.GetItem(item, 1));
                list2.Add(listBox5.Items.ToString());
            }
            iTalk_Label44.Text = listBox5.Items.Count.ToString();
            iTalk_Label27.Text = listBox4.Items.Count.ToString();
            listView1.View = View.Details;
            listView1.Columns.Add("Objet", 140, HorizontalAlignment.Left);
            listView1.Columns.Add("Quantité", 110, HorizontalAlignment.Left);
            listView1.Columns.Add("Action", 110, HorizontalAlignment.Left);
            listView1.Columns.Add("Joueur", 120, HorizontalAlignment.Left);
        }
        private void iTalk_TextBox_Small16_TextChanged(object sender, EventArgs e)
        {
            if (String.IsNullOrEmpty(iTalk_TextBox_Small16.Text.Trim()) == false)
            {
                listBox5.Items.Clear();
                foreach (string str in list2)
                {
                    if (str.StartsWith(iTalk_TextBox_Small16.Text.Trim()))

                    {
                        listBox5.Items.Add(str);
                    }
                }
            }

            else if (iTalk_TextBox_Small16.Text.Trim().Equals(""))
            {
                listBox5.Items.Clear();

                foreach (string str in list2)
                {
                    listBox5.Items.Add(str);
                }
            }
        }
        private void iTalk_TextBox_Small15_TextChanged(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(iTalk_TextBox_Small15.Text.Trim()) == false)
            {
                listBox4.Items.Clear();
                foreach (string str in list)
                {
                    if (str.StartsWith(iTalk_TextBox_Small15.Text.Trim()))

                    {
                        listBox4.Items.Add(str);
                    }
                }
            }

            else if (iTalk_TextBox_Small15.Text.Trim().Equals(""))
            {
                listBox4.Items.Clear();

                foreach (string str in list)
                {
                    listBox4.Items.Add(str);
                }
            }
        }



        #endregion

        private void listBox4_SelectedIndexChanged(object sender, EventArgs e)
        {
            List<string> str = new List<string>();
            sfListView1.DataSource = null;
            CharacterList.ItemsPerso.Clear();
            CharacterList.preinventory.Clear();
            Persoinventory.TryGetValue(listBox4.SelectedItems.ToString(), out List<string> listItem);
            if (listItem != null)
            {
                sfListView1.DataSource = listItem;
                sfListView1.Refresh();
            }
            else
            {
                CharacterList.GetInventory(listBox4.SelectedItem.ToString());
                str = CharacterList.ItemsPerso;
                if(!Persoinventory.ContainsKey(listBox4.SelectedItem.ToString()))
                    Persoinventory.Add(listBox4.SelectedItem.ToString(), str);
                sfListView1.DataSource = str;
                sfListView1.Refresh();
                str.Clear();
            }
            iTalk_Label29.Text = sfListView1.RowCount.ToString();
            

        }

        private void backgroundWorker1_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
           
        }

        

        private void sfListView1_Click(object sender, EventArgs e)
        {

        }

        private void sfListView1_SelectionChanged(object sender, Syncfusion.WinForms.ListView.Events.ItemSelectionChangedEventArgs e)
        {
            if(sfListView1.SelectedItem != null)
            {
                GUID = Convert.ToInt32(sfListView1.SelectedItem.ToString().Split('(')[1].Split(')')[0].Trim());
                ID = ItemList.ItemsList.FirstOrDefault(x => x.Key == GUID).Value.Template;
                string TYPE = ItemTemplateList.GetItem(ID, 2);

                iTalk_Label35.Text = ID.ToString();
                iTalk_Label36.Text = sfListView1.SelectedItem.ToString().Split('(')[0].Trim().ToString();
                iTalk_Label37.Text = TYPE;
                iTalk_Label38.Text = ItemList.ItemsList.FirstOrDefault(x => x.Key == GUID).Value.Qua.ToString();
                pictureBox1.Image = Image.FromFile(SearchManager.Search_pictureItem(ID, Convert.ToInt32(TYPE)));

            }
        }
        private void addToListForselection(bool remove, string id, int qua, string name, bool multiple = false, bool alrealdyIn = false, bool newitem = false)
        {
            bool canparse = int.TryParse(qua.ToString(), out int Nqua);
            if (Items.ContainsKey(name))
                alrealdyIn = true;
            if (canparse)
            {
                if (remove)
                {
                    if (multiple)
                        qua = Nqua;
                    if (Selection.ContainsKey(name))
                    {
                        int count = Selection.FirstOrDefault(x => x.Key == name).Value.Count;
                        bool action = Selection.FirstOrDefault(x => x.Key == name).Value.action;
                        string player = Selection.FirstOrDefault(x => x.Key == name).Value.player_name;
                        if (!action)
                            action = true;
                        if (count > Nqua || count == Nqua)
                        {
                            MessageBox.Show("Vous ne pouvez pas plus en retirer.", "Supression impossible", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                            return;
                        }
                        else
                        {
                            if ((count + qua) > Nqua)
                            {
                                qua = (count + qua) - Nqua;

                            }
                            else
                            {
                                qua = (count + qua);
                            }
                        }
                        Items.Remove($"{name}");
                        Selection.Remove(name);
                        listView1.Items.Clear();
                        foreach (ListViewItem i in Items.Values)
                        {
                            listView1.Items.Add(i);
                        }
                        ListViewItem item = new ListViewItem(name.Trim());
                        item.SubItems.Add(qua.ToString());
                        item.SubItems.Add("A retirer");
                        item.SubItems.Add(listBox4.SelectedItem.ToString());
                        listView1.Items.Add(item);
                        SelectionClass SC = new SelectionClass
                        {
                            id = id,
                            name = name,
                            action = action,
                            Count = count,
                            player_name = listBox4.SelectedItem.ToString(),
                            New = false
                        };
                        Selection.Add(name, SC);
                        Items.Add($"{name}", item);

                    }
                    else
                    {
                        ListViewItem item = new ListViewItem(name);
                        item.SubItems.Add(qua.ToString());
                        item.SubItems.Add("A retirer");
                        item.SubItems.Add(listBox4.SelectedItem.ToString());
                        listView1.Items.Add(item);
                        Items.Add($"{name}", item);
                        SelectionClass SC = new SelectionClass
                        {
                            id = id,
                            name = name,
                            action = true,
                            Count = qua,
                            player_name = listBox4.SelectedItem.ToString(),
                            New = false
                        };
                        Selection.Add(name, SC);
                    }
                }
                else
                {
                    if (alrealdyIn)
                    {
                        
                            Items.Remove(name);
                            Selection.Remove(name);
                            listView1.Items.Clear();
                            ListViewItem item = new ListViewItem(name);
                            item.SubItems.Add(qua.ToString());
                            item.SubItems.Add("Ajout");
                            item.SubItems.Add(listBox4.SelectedItem.ToString());
                            Items.Add(name, item);
                        SelectionClass SC = new SelectionClass
                        {
                            id = id,
                            Template = ItemTemplateList.ReturnItemId(name.Split('(')[0]).ToString(),
                            stats = ItemTemplateList.ReturnItemName(ItemTemplateList.ReturnItemId(name.Split('(')[0])).StatsTemplate,
                            name = name,
                                action = false,
                                Count = qua,
                                player_name = listBox4.SelectedItem.ToString(),
                                New = newitem
                            };
                            Selection.Add(name, SC);
                            foreach (ListViewItem i in Items.Values)
                            {
                                listView1.Items.Add(i);
                            }
                        
                    }
                    else
                    {
                        ListViewItem item = new ListViewItem(name);
                        item.SubItems.Add(qua.ToString());
                        item.SubItems.Add("Ajout");
                        item.SubItems.Add(listBox4.SelectedItem.ToString());
                        listView1.Items.Add(item);
                        Items.Add(name, item);
                        SelectionClass SC = new SelectionClass
                        {
                            id = id,
                            Template = ItemTemplateList.ReturnItemId(name.Split('(')[0]).ToString(),
                            stats = ItemTemplateList.ReturnItemName(ItemTemplateList.ReturnItemId(name.Split('(')[0])).StatsTemplate,
                            name = name,
                            action = false,
                            Count = qua,
                            player_name = listBox4.SelectedItem.ToString(),
                            New=newitem
                        };
                        Selection.Add(name, SC);
                    }
                }
            }
            else
            {
                MessageBox.Show(Nqua.ToString());
            }
        }
        private int GenerateGUID(int CountAdd)
        {
            int g = ItemList.ItemsList.Max(x => x.Key);
            return g + CountAdd;
        }
        private void iTalk_Button_12_Click(object sender, EventArgs e) // ajouter
        {
            if(listBox5.SelectedItem != null)
            {
                if(listBox4.SelectedItem != null)
                {
                   if(iTalk_NumericUpDown6.Value >= 1)
                    {
                        if (Selection.Keys.Contains(listBox5.SelectedItem.ToString()))
                        {
                            string n = Selection.FirstOrDefault(x => x.Key.Contains(listBox5.SelectedItem.ToString())).Key;
                            string MP = Selection.FirstOrDefault(x => x.Key == n).Value.player_name;
                            int GU = Convert.ToInt32(n.Split('(')[1].Split(')')[0].Trim());
                            string id = ItemList.ItemsList.FirstOrDefault(x => x.Key == GU).Value.Template.ToString();
                            int qua = Selection.FirstOrDefault(x => x.Key == n).Value.Count;

                            if (MP == listBox5.SelectedItem.ToString()) // + 1 qua
                            {
                                CountForAdd++;
                                addToListForselection(false, id, qua + (int)iTalk_NumericUpDown6.Value, n, false, true);
                                listChecked.Add(n);
                                listBox5.SelectedItem = null;
                                iTalk_NumericUpDown6.Value = 0;
                            }
                            else
                            {
                                CountForAdd++;
                                int newguid = GenerateGUID(CountForAdd);
                                addToListForselection(false, id, qua + (int)iTalk_NumericUpDown6.Value, $"{listBox5.SelectedItem}({newguid})", false, true, true);
                                listChecked.Add($"{listBox5.SelectedItem}({newguid})");
                                listBox5.SelectedItem = null;
                                iTalk_NumericUpDown6.Value = 0;
                            }
                        }
                        else
                        {
                            int id = ItemTemplateList.ReturnItemId(listBox5.SelectedItem.ToString());
                            CountForAdd++;
                            int newguid = GenerateGUID(CountForAdd);
                            addToListForselection(false, id.ToString(), (int)iTalk_NumericUpDown6.Value, $"{listBox5.SelectedItem}({newguid})", false, false, true);
                            listChecked.Add($"{listBox5.SelectedItem}({newguid})");
                            listBox5.SelectedItem = null;
                            iTalk_NumericUpDown6.Value = 0;
                        }
                    }
                    else
                    {
                        MessageBox.Show("Merci d'indiquer une valeur supérieur à 0", "Ajout impossible", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
                else
                {
                    MessageBox.Show($"Merci de sélectionner un joueur à qui ajouter l'objet [{listBox5.SelectedItem}]", "Ajout impossible", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            else
            {
                MessageBox.Show("Merci de sélectionner un objet à mettre dans l'inventaire.", "Ajout impossible", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }
        private void iTalk_Button_13_Click(object sender, EventArgs e) // retirer
        {
            if (iTalk_NumericUpDown6.Value <= 0 && sfListView1.CheckedItems.Count == 1)
            {
                MessageBox.Show($"La valeur {iTalk_NumericUpDown6.Value} n'est pas une valeur correcte.", "Quantité incorrecte.", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            else
            {
                if (sfListView1.CheckedItems.Count == 1)
                {
                    if (Selection.ContainsKey(sfListView1.CheckedItems[0].ToString()))
                    {
                        UpdateViaContextMenu(1, true, sfListView1.CheckedItems[0].ToString());
                    }
                    else
                    {
                        GUID = Convert.ToInt32(sfListView1.CheckedItems[0].ToString().Split('(')[1].Split(')')[0].Trim());
                        ID = ItemList.ItemsId.FirstOrDefault(x => x.Key == GUID).Value;
                        listChecked.Add(sfListView1.CheckedItems[0].ToString());
                        addToListForselection(true, ID.ToString(), (int)iTalk_NumericUpDown6.Value, sfListView1.CheckedItems[0].ToString());
                        listBox5.SelectedItem = null;
                        iTalk_NumericUpDown6.Value = 0;
                    }
                }
                else if (sfListView1.CheckedItems.Count >= 2)
                {
                    for (int i = 0; i < sfListView1.CheckedItems.Count; i++)
                    {
                       if(Selection.ContainsKey(sfListView1.CheckedItems[i].ToString()))
                        {
                            string P = Selection.FirstOrDefault(x => x.Key == sfListView1.CheckedItems[i].ToString()).Value.player_name;
                            MessageBox.Show($"L'objet {sfListView1.CheckedItems[i]} du joueur {P} est déjà en attente.", "Impossible d'effectuer l'action", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }
                        else
                        {
                            listChecked.Add(sfListView1.CheckedItems[i].ToString());
                            GUID = Convert.ToInt32(sfListView1.CheckedItems[i].ToString().Split('(')[1].Split(')')[0].Trim());
                            ID = ItemList.ItemsId.FirstOrDefault(x => x.Key == GUID).Value;
                            int Q = ItemList.ItemsList.FirstOrDefault(x => x.Key == GUID).Value.Qua;

                            addToListForselection(true, ID.ToString(), Q, sfListView1.CheckedItems[i].ToString(), true);
                            listBox5.SelectedItem = null;
                            iTalk_NumericUpDown6.Value = 0;
                        }
                    }
                }
            }
        }

        private void menuStrip1_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {

        }

        private void listView1_Click(object sender, EventArgs e)
        {
            
        }

        private void toolStripMenuItem1_Click(object sender, EventArgs e)
        {
           selectionned = "";
            try
            {
               selected = listView1.SelectedItems[0].Selected;
               selectionned = listView1.SelectedItems[0].Text;
            }
            catch
            {
                selected = false;
               
            }
            if (selected)
            {
                listView1.Items.Clear();
                Selection.Remove(selectionned);
                Items.Remove(selectionned);
                foreach (ListViewItem I in Items.Values)
                {
                    listView1.Items.Add(I);
                }
            }
            
        }

        private void iTalk_Button_23_Click(object sender, EventArgs e) // application
        {
            List<SelectionClass> SE_delete = new List<SelectionClass>();
            List<SelectionClass> SE_ADD = new List<SelectionClass>();
            List<string> ObjectBeforeDelete = new List<string>();
            Dictionary<string, string> DicoBeforeDeletequery = new Dictionary<string, string>();
            List<string>ObjectBeforeAdd = new List<string>();
            Dictionary<string, string> DicoBeforeAddquery = new Dictionary<string, string>();
            Dictionary<string, int> ItemAndQuaDelete = new Dictionary<string, int>();
            Dictionary<string, int> ItemAndQuaADD = new Dictionary<string, int>();

            if (Selection.Count > 0)
            {
                foreach(SelectionClass S in Selection.Values)
                {
                    if(S.action == true)
                    {
                        SE_delete.Add(S);
                    }
                    else
                    {
                        SE_ADD.Add(S);
                    }
                }
                if(SE_delete.Count > 0)
                {
                    foreach (SelectionClass SE in SE_delete)
                    {
                        int quant = ItemList.ReturnItemQua(SE.name.Split('(')[1].Split(')')[0]);
                        if (DicoBeforeDeletequery.ContainsKey(SE.player_name))
                        {
                            string inv = DicoBeforeDeletequery.FirstOrDefault(x => x.Key == SE.player_name).Value;
                            foreach (string h in inv.Split('|'))
                            {

                                if (!string.IsNullOrEmpty(h) && !h.Equals(SE.name.Split('(')[1].Split(')')[0]))
                                {
                                    ObjectBeforeDelete.Add(h);
                                }
                            }
                            if (!SE.New)
                            {
                                if (quant == SE.Count)
                                    ObjectBeforeDelete.Remove(SE.name.Split('(')[1].Split(')')[0]);
                                DicoBeforeDeletequery.Remove(SE.player_name);
                                ItemAndQuaDelete.Add(SE.name.Split('(')[1].Split(')')[0], SE.Count);
                            }
                            DicoBeforeDeletequery.Add(SE.player_name, string.Join("|", ObjectBeforeDelete));

                        }
                        else
                        {
                            string I = CharacterList.GetObjectFromInventory(SE.player_name);
                            if (I != null)
                            {
                                foreach (string o in I.Split('|'))
                                {
                                    if (!string.IsNullOrEmpty(o))
                                    {
                                        ObjectBeforeDelete.Add(o);
                                    }
                                    
                                }
                                if (!SE.New)
                                {
                                    if (quant == SE.Count)
                                        ObjectBeforeDelete.Remove(SE.name.Split('(')[1].Split(')')[0]);
                                    ItemAndQuaDelete.Add(SE.name.Split('(')[1].Split(')')[0], SE.Count);
                                }
                                DicoBeforeDeletequery.Add(SE.player_name, string.Join("|", ObjectBeforeDelete));
                            }
                        }

                    } ObjectBeforeDelete.Clear();
                }

            }
                if(SE_ADD.Count > 0)
                {
                    foreach (SelectionClass SE in SE_ADD)
                    {
                        if (DicoBeforeAddquery.ContainsKey(SE.player_name))
                        {
                            string inv = DicoBeforeAddquery[SE.player_name];
                            foreach(string b in inv.Split('|'))
                            {
                                if (!string.IsNullOrEmpty(b))
                                {
                                    ObjectBeforeAdd.Add(b);
                                }
                            }
                        if (SE.New)
                        {
                            ObjectBeforeAdd.Add(SE.name.Split('(')[1].Split(')')[0]);
                            ItemAndQuaADD.Add($"{SE.name.Split('(')[1].Split(')')[0]}@{SE.Template}@{SE.stats}", SE.Count);
                        }
                        else
                        {
                            ItemAndQuaADD.Add($"{SE.name.Split('(')[1].Split(')')[0]}", SE.Count);
                        }
                            DicoBeforeAddquery.Remove(SE.player_name);
                            DicoBeforeAddquery.Add(SE.player_name, string.Join("|", ObjectBeforeAdd));
                        }
                        else
                        {
                            string U = CharacterList.GetObjectFromInventory(SE.player_name);
                            if (U != null)
                            {
                                foreach (string o in U.Split('|'))
                                {
                                    if (!string.IsNullOrEmpty(o))
                                    {
                                        ObjectBeforeAdd.Add(o);
                                    }
                                }
                            if(SE.New)
                                {
                                ObjectBeforeAdd.Add(SE.name.Split('(')[1].Split(')')[0]);
                                ItemAndQuaADD.Add($"{SE.name.Split('(')[1].Split(')')[0]}@{SE.Template}@{SE.stats}", SE.Count);
                            }
                                else
                            {
                                ItemAndQuaADD.Add($"{SE.name.Split('(')[1].Split(')')[0]}", SE.Count);
                            }
                            ItemAndQuaADD.Add(SE.name.Split('(')[1].Split(')')[0], SE.Count);
                                DicoBeforeAddquery.Add(SE.player_name, string.Join("|", ObjectBeforeAdd));
                            }
                        }
                        ObjectBeforeAdd.Clear();
                    }
                }
            try
            {
                if (DicoBeforeDeletequery.Count > 0)
                {
                    foreach (string s in DicoBeforeDeletequery.Keys)
                    {
                        DatabaseManager.UpdateQuery(QueryBuilder.UpdateFromQuery(CharacterList.TablePerso, "objets", 1, DicoBeforeDeletequery[s], "name", s));
                    }
                    foreach (string g in ItemAndQuaDelete.Keys)
                    {
                        if (ItemAndQuaDelete[g] == ItemList.ReturnItemQua(g))
                        {
                            DatabaseManager2.UpdateQuery(QueryBuilder.DeleteFromQuery(ItemList.TableItems, "guid", g));
                        }
                        else
                        {
                            DatabaseManager2.UpdateQuery(QueryBuilder.UpdateFromQuery(ItemList.TableItems, "qua", 3, $"{ItemAndQuaDelete[g]}", "guid", g));
                        }
                    }
                }
                if (DicoBeforeAddquery.Count > 0)
                {
                    foreach (string j in DicoBeforeAddquery.Keys)
                    {
                        DatabaseManager.UpdateQuery(QueryBuilder.UpdateFromQuery(CharacterList.TablePerso, "objets", 1, DicoBeforeAddquery[j], "name", j));
                    }
                    foreach (string f in ItemAndQuaADD.Keys)
                    {
                        if (f.Contains("@"))
                        {
                            DatabaseManager2.UpdateQuery(QueryBuilder.InsertIntoQuery(ItemList.TableItems, new string[] { "guid", "template", "qua", "pos", "stats", "puit" }, new string[] { f.Split('@')[0], f.Split('@')[1], ItemAndQuaADD[f].ToString(), "-1", f.Split('@')[2], "0" }, ""));
                        }
                        else
                        {
                            DatabaseManager2.UpdateQuery(QueryBuilder.UpdateFromQuery(ItemList.TableItems, "qua", 2, $"{ItemAndQuaADD[f]}", "guid", f));
                        }
                    }
                }
                MessageBox.Show("Les modifications ont été effectuées.", "Modifications réussies", MessageBoxButtons.OK, MessageBoxIcon.Information);

            }catch(Exception ex)
            {
                MessageBox.Show(ex.Message);
                return;
            }
            

            Selection.Clear();
            Items.Clear();
            listChecked.Clear();
            listView1.Items.Clear();
            SE_ADD.Clear();
            SE_delete.Clear();
            DicoBeforeAddquery.Clear();
            DicoBeforeDeletequery.Clear();
            ObjectBeforeAdd.Clear();
            ObjectBeforeDelete.Clear();
            ItemAndQuaADD.Clear();
            ItemAndQuaDelete.Clear();
        }

        private void listView1_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                selected = listView1.SelectedItems[0].Selected;
                selectionned = listView1.SelectedItems[0].Text;
                if (selected)
                    iTalk_ContextMenuStrip1.Show(Cursor.Position.X, Cursor.Position.Y);
            }
            catch
            {
                selected = false;

            }
            
            
        }

        private void ajouterX1ToolStripMenuItem_Click(object sender, EventArgs e)
        {

        }

        private void toolStripMenuItem3_Click(object sender, EventArgs e)
        {
            UpdateViaContextMenu(1, false);
        }
        private void toolStripMenuItem4_Click(object sender, EventArgs e)
        {
            UpdateViaContextMenu(10, false);
        }
        private void toolStripMenuItem5_Click(object sender, EventArgs e)
        {
            UpdateViaContextMenu(100, false);
        }
        private void UpdateViaContextMenu(int qua, bool remove, string already = null)
        {
            if(already != null)
                selectionned = already;

            string IDitem = Selection.FirstOrDefault(x => x.Key == selectionned).Value.id;
            bool Actionitem = Selection.FirstOrDefault(x => x.Key == selectionned).Value.action;
            string Playeritem = Selection.FirstOrDefault(x => x.Key == selectionned).Value.player_name;
            int quaItem = Selection.FirstOrDefault(x => x.Key == selectionned).Value.Count;
            string GUID = listChecked.FirstOrDefault(x => x.Contains(selectionned)).Split('(')[1].Split(')')[0].Trim();
            string template = Selection.FirstOrDefault(x => x.Key == selectionned).Value.Template;
            string stat = Selection.FirstOrDefault(x => x.Key == selectionned).Value.stats;
            bool isnew = Selection.FirstOrDefault(x => x.Key == selectionned).Value.New;

            int ItemQua = 0;
            if (ItemList.ItemsList.ContainsKey(Convert.ToInt32(GUID)))
            {
                 ItemQua = ItemList.ItemsList.FirstOrDefault(x => x.Key == Convert.ToInt32(GUID)).Value.Qua;
            }
            else
            {
                ItemQua = Selection.First(x => x.Key == selectionned).Value.Count;
            }

            if (remove)
            {
                if (quaItem - qua <= 0)
                {
                    listView1.Items.Clear();
                    Selection.Remove(selectionned);
                    Items.Remove(selectionned);
                    foreach (ListViewItem I in Items.Values)
                    {
                        listView1.Items.Add(I);
                    }
                }
                else
                {

                    SelectionClass SC = new SelectionClass
                    {
                        id = IDitem,
                        name = selectionned,
                        action = Actionitem,
                        Count = quaItem - qua,
                        player_name = Playeritem,
                        Template = template,
                        stats = stat,
                        New = isnew


                    };
                    listView1.Items.Clear();
                    Selection.Remove(selectionned);
                    Items.Remove(selectionned);

                    ListViewItem item = new ListViewItem(selectionned);
                    item.SubItems.Add((quaItem - qua).ToString());
                    if (Actionitem)
                    {
                        item.SubItems.Add("A retirer");
                    }
                    else
                    {
                        item.SubItems.Add("Ajout");
                    }
                    item.SubItems.Add(Playeritem);
                    Items.Add(selectionned, item);
                    Selection.Add(selectionned, SC);
                    foreach (ListViewItem I in Items.Values)
                    {
                        listView1.Items.Add(I);
                    }

                }

            }
            else
            {
                SelectionClass SC = new SelectionClass
                {
                    id = IDitem,
                    name = selectionned,
                    action = Actionitem,
                    Count = quaItem + qua,
                    player_name = Playeritem,
                    Template = template,
                    stats = stat,
                    New = isnew

                };
                listView1.Items.Clear();
                Selection.Remove(selectionned);
                Items.Remove(selectionned);

                ListViewItem item = new ListViewItem(selectionned);
                item.SubItems.Add((quaItem + qua).ToString());
                if (Actionitem)
                {
                    item.SubItems.Add("A retirer");
                }
                else
                {
                    item.SubItems.Add("Ajout");
                }
                item.SubItems.Add(Playeritem);
                Items.Add(selectionned, item);
                Selection.Add(selectionned, SC);
                foreach (ListViewItem I in Items.Values)
                {
                    listView1.Items.Add(I);
                }
            }
        }

        private void toolStripMenuItem6_Click(object sender, EventArgs e)
        {
            UpdateViaContextMenu(1, true);
        }

        private void toolStripMenuItem7_Click(object sender, EventArgs e)
        {
            UpdateViaContextMenu(10, true);
        }

        private void toolStripMenuItem8_Click(object sender, EventArgs e)
        {
            UpdateViaContextMenu(100, true);
        }

        private void changerActionToolStripMenuItem_Click(object sender, EventArgs e)
        {
            bool AC = Selection.FirstOrDefault(x => x.Key == selectionned).Value.action;
            bool IsNew = Selection.FirstOrDefault(x => x.Key == selectionned).Value.New;
            if(AC == false && IsNew == true)
            {
                listView1.Items.Clear();
                Selection.Remove(selectionned);
                Items.Remove(selectionned);
                foreach (ListViewItem I in Items.Values)
                {
                    listView1.Items.Add(I);
                }
            }
            else
            {
                SelectionClass NSC = new SelectionClass
                {
                    id = Selection.FirstOrDefault(x => x.Key == selectionned).Value.id,
                    name = Selection.FirstOrDefault(x => x.Key == selectionned).Value.name,
                    action = !AC,
                    Count = Selection.FirstOrDefault(x => x.Key == selectionned).Value.Count,
                    player_name = Selection.FirstOrDefault(x => x.Key == selectionned).Value.player_name,
                    Template = Selection.FirstOrDefault(x => x.Key == selectionned).Value.Template,
                    stats = Selection.FirstOrDefault(x => x.Key == selectionned).Value.stats,
                    New = Selection.FirstOrDefault(x => x.Key == selectionned).Value.New
                };
                listView1.Items.Clear();
                Selection.Remove(selectionned);
                Items.Remove(selectionned);
                Selection.Add(selectionned, NSC);
                ListViewItem item = new ListViewItem(selectionned);
                item.SubItems.Add((Selection.FirstOrDefault(x => x.Key == selectionned).Value.Count).ToString());
                if (!AC)
                {
                    item.SubItems.Add("A retirer");
                }
                else
                {
                    item.SubItems.Add("Ajout");
                }
                item.SubItems.Add(Selection.FirstOrDefault(x => x.Key == selectionned).Value.player_name);
                Items.Add(selectionned, item);
                foreach (ListViewItem I in Items.Values)
                {
                    listView1.Items.Add(I);
                }
            }
        }
    }
}

