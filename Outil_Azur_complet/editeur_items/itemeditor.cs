using Syncfusion.WinForms.ListView;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
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
        private readonly Dictionary<string, InventoryChange> _inventoryChanges =
            new Dictionary<string, InventoryChange>(StringComparer.Ordinal);
        private readonly Dictionary<string, InventoryTemplateChoice> _inventoryTemplates =
            new Dictionary<string, InventoryTemplateChoice>(StringComparer.Ordinal);
        private Label _inventoryStatus;
        private bool _inventoryAvailable;
        private bool _inventoryInitialized;
        private readonly string _initialCharacterName;
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

        public itemeditor() : this(null)
        {
        }

        public itemeditor(string initialCharacterName)
        {
            InitializeComponent();
            _initialCharacterName = initialCharacterName;
            BuildEditorLayout();
        }

       
        private void Itemeditor_Load(object sender, EventArgs e)
        {
            LoadStatic();
            LoadStaticInventory();
            BuildInventoryLayout();
            if (!string.IsNullOrWhiteSpace(_initialCharacterName) &&
                listBox4.Items.Contains(_initialCharacterName))
                listBox4.SelectedItem = _initialCharacterName;
        }
        #region éditeur d'items
        private void iTalk_Button_11_Click(object sender, EventArgs e)
        {
            Close();
        }
        public void ClearAllList()
        {
            EffectSelected.Clear();
            ConditionsSelected.Clear();
            RecipeContent.Clear();
            TradRecipe.Clear();
            CreateQuery.Clear();
            EditorManager.TradStat.Clear();
            InCondi.Clear();
            SB.Clear();
            ConditionsFinal = "";
            listBox1.Items.Clear();
            listBox3.Items.Clear();
            iTalk_NotificationNumber1.Value = 0;
        }
        public void LoadStatic()
        {
            iTalk_ComboBox2.Items.Clear();iTalk_ComboBox3.Items.Clear();iTalk_ComboBox5.Items.Clear();listBox2.Items.Clear();
            if(!InitializeForm.NoDB && !string.IsNullOrWhiteSpace(Tools_protocol.Query.DatabaseManager.ConnectionString))
            {try{ItemTemplateList.Load_Item();ItemSetList.LoadPano();}catch(Exception error){if(editorLayout!=null)editorLayout.Status.Text="Chargement des modèles incomplet : "+error.Message;}}
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
            if (listBox2.SelectedItem == null) return;
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
                string label = listBox1.SelectedItem.ToString();
                string ingredient = RecipeContent.FirstOrDefault(entry =>
                    $"{ItemTemplateList.GetItem(int.Parse(entry.Key), 1)} x {entry.Value}" == label).Key;
                if (ingredient != null) RecipeContent.Remove(ingredient);
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
            SB.Clear();
            ConditionsFinal = "";
            foreach (string condition in ConditionsSelected)
                SB.Append(EditorManager.ConditionsParse(condition, ConditionsSelected.Count));
            ConditionsFinal = SB.ToString().TrimEnd('&', '|');
        }
        public void PrepareCreateItem()
        {
            TradRecipe.Clear();
            EditorManager.TradStat.Clear();
            pano = -1;
            H = iTalk_CheckBox2.Checked;
            ECH = iTalk_CheckBox1.Checked;
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
        private async void ITalk_Button_21_Click(object sender, EventArgs e)
        {
            bool databaseSaved = false;
            var completedFiles = new List<string>();
            try
            {
                bool exportSwf = iTalk_RadioButton1.Checked || iTalk_RadioButton3.Checked;
                bool exportSql = iTalk_RadioButton2.Checked || iTalk_RadioButton3.Checked;
                if (!exportSwf && !exportSql)
                    throw new InvalidOperationException("Sélectionnez un moyen d'exportation.");
                if (exportSql && !iTalk_CheckBox6.Checked && !iTalk_CheckBox5.Checked)
                    throw new InvalidOperationException("Sélectionnez la création de fichiers SQL ou l'injection directe.");
                if (!int.TryParse(iTalk_TextBox_Small2.Text, out int templateId) || templateId <= 0)
                    throw new FormatException("L'identifiant du template doit être un entier positif.");
                if (string.IsNullOrWhiteSpace(iTalk_TextBox_Small1.Text))
                    throw new FormatException("Le nom de l'objet est obligatoire.");
                if (exportSql && ItemTemplateList.ItemFullDico.ContainsKey(templateId))
                    throw new InvalidOperationException($"Le template {templateId} existe déjà.");
                if (exportSql && ItemTemplateList.ItemFullDico.Values.Any(item =>
                    string.Equals(item.Name, iTalk_TextBox_Small1.Text.Trim(), StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Un objet porte déjà ce nom.");
                if (iTalk_ComboBox1.SelectedItem == null)
                    throw new FormatException("Sélectionnez le type d'objet.");
                var selectedType=iTalk_ComboBox1.SelectedItem as Outil_Azur_complet.Editors.EditorOption;
                int type = selectedType!=null?int.Parse(selectedType.Value):EditorManager.SwitchType(iTalk_ComboBox1.SelectedItem.ToString());
                if (type <= 0) throw new FormatException("Le type d'objet n'est pas pris en charge.");
                int itemGuid = 0;
                if (exportSql && (!int.TryParse(iTalk_TextBox_Small9.Text, out itemGuid) || itemGuid <= 0))
                    throw new FormatException("Le GUID de l'objet doit être un entier positif.");
                foreach (var field in new[] { iTalk_TextBox_Small3, iTalk_TextBox_Small4,
                    iTalk_TextBox_Small5, iTalk_TextBox_Small6, iTalk_TextBox_Small7, iTalk_TextBox_Small8 })
                {
                    if (string.IsNullOrWhiteSpace(field.Text)) field.Text = "0";
                    if (!int.TryParse(field.Text, out int value) || value < 0)
                        throw new FormatException("Les informations d'arme doivent être des entiers positifs ou nuls.");
                }
                if (string.IsNullOrWhiteSpace(iTalk_TextBox_Small10.Text)) iTalk_TextBox_Small10.Text = "0";
                if (!int.TryParse(iTalk_TextBox_Small10.Text, out int points) || points < 0)
                    throw new FormatException("Les points doivent être un entier positif ou nul.");
                if (iTalk_CheckBox4.Checked && RecipeContent.Count == 0)
                    throw new FormatException("Ajoutez au moins un ingrédient pour un objet fabricable.");

                int gfx = 0;
                if (exportSwf && (!int.TryParse(iTalk_TextBox_Small14.Text, out gfx) || gfx < 0))
                    throw new FormatException("Le GFX doit être un entier positif ou nul.");
                PrepareCreateItem();
                ClientExportPlan clientPlan = null;
                if (exportSwf)
                {
                    var definition = ItemClientDefinition.FromCreation(templateId, iTalk_TextBox_Small1.Text.Trim(),
                        iTalk_TextBox_Small13.Text, gfx.ToString(), type.ToString(),
                        iTalk_NumericUpDown1.Value.ToString(), iTalk_CheckBox7.Checked,
                        iTalk_NumericUpDown2.Value.ToString(), ArmeInfos, ConditionsFinal,
                        iTalk_NumericUpDown3.Value.ToString(), iTalk_CheckBox8.Checked, iTalk_CheckBox2.Checked);
                    clientPlan = PrepareClientExport(definition, EditorManager.CreateSwfLine(templateId, definition.Name,
                        definition.Description, gfx.ToString(), type.ToString(), definition.Level.ToString(),
                        definition.ForgeMagic, definition.Weight.ToString(), ArmeInfos, definition.Condition,
                        definition.Price.ToString(), definition.Usable, definition.TwoHands));
                    if (clientPlan == null) return;
                }
                iTalk_Button_21.Enabled = false;
                if (exportSql)
                {
                    BuildCreationQueries(templateId, itemGuid, type);
                    if (iTalk_CheckBox6.Checked)
                        EditorManager.CreateSQLEditor($"items_{EmuManager.EMUSELECTED}", CreateQuery);
                    if (iTalk_CheckBox5.Checked)
                    {
                        iTalk_Button_21.Enabled = false;
                        var snapshot = new Dictionary<string, string>(CreateQuery);
                        await Task.Run(() => ItemCreationService.Inject(snapshot, templateId, itemGuid));
                        databaseSaved = true;
                        ItemTemplateList.Load_Item();
                    }
                }
                if (exportSwf)
                {
                    clientPlan.Write();
                    completedFiles.Add(clientPlan.Destination);
                }
                MessageBox.Show($"La création de l'objet {iTalk_TextBox_Small1.Text} est terminée." +
                    (completedFiles.Count > 0 ? "\n\nSWF enregistré :\n" + string.Join("\n", completedFiles) : ""),
                    $"Template {templateId}", MessageBoxButtons.OK, MessageBoxIcon.Information);
                ClearAll();
            }
            catch (Exception error)
            {
                MessageBox.Show(error.Message + (databaseSaved ? "\n\nL'objet a déjà été enregistré en base. Vous pouvez relancer avec l'option SWF du client uniquement." : ""), "Création impossible", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                iTalk_Button_21.Enabled = true;
            }
        }

        private void BuildCreationQueries(int templateId, int itemGuid, int type)
        {
            CreateQuery.Clear();
            if (iTalk_CheckBox4.Checked)
                CreateQuery["craft"] = QueryBuilder.InsertIntoQuery(TableCraft,
                    new[] { "id", "craft" }, new[] { templateId.ToString(), h }, "");
            if (iTalk_ComboBox2.SelectedItem != null)
                CreateQuery["pano"] = ItemCreationService.BuildPanoplyQuery(TablePano, ColumsPano,
                    EmuManager.ReturnInfoCol("pano"), iTalk_ComboBox2.SelectedItem.ToString(), templateId);
            CreateQuery["item"] = ItemCreationService.BuildItemQuery(TableItems, templateId, itemGuid);
            CreateQuery["template"] = ItemCreationService.BuildTemplateQuery(TableItemsTemplate,
                new[] { templateId.ToString(), type.ToString(), iTalk_TextBox_Small1.Text.Trim(),
                    iTalk_NumericUpDown1.Value.ToString(), b, iTalk_NumericUpDown2.Value.ToString(),
                    pano.ToString(), iTalk_NumericUpDown3.Value.ToString(), ConditionsFinal, ArmeInfos,
                    "0", "0", iTalk_TextBox_Small10.Text }, iTalk_CheckBox1.Checked, iTalk_CheckBox3.Checked);
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
            else
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
            if (_inventoryInitialized) return;
            _inventoryInitialized = true;
            _inventoryStatus = new Label
            {
                Location = new Point(178, 477),
                Size = new Size(755, 30),
                AutoEllipsis = true,
                ForeColor = Color.DarkRed
            };
            tabPage5.Controls.Add(_inventoryStatus);
            listView1.DoubleClick += (sender, args) => RemoveSelectedPendingChange();
            listView1.SelectedIndexChanged -= listView1_SelectedIndexChanged;
            iTalk_ContextMenuStrip1.Enabled = false;
            iTalk_Button_12.Click -= iTalk_Button_12_Click;
            iTalk_Button_12.Click += StageAddItem;
            iTalk_Button_13.Click -= iTalk_Button_13_Click;
            iTalk_Button_13.Click += StageRemoveItems;
            iTalk_Button_23.Click -= iTalk_Button_23_Click;
            iTalk_Button_23.Click += ApplyPendingInventory;
            iTalk_Button_22.Enabled = false;

            string unavailable = InventoryUpdateService.UnavailableReason();
            try
            {
                foreach (var template in InventoryUpdateService.LoadTemplateChoices())
                {
                    string label = template.ToString();
                    _inventoryTemplates[label] = template;
                    list2.Add(label);
                    listBox5.Items.Add(label);
                }
            }
            catch (Exception error)
            {
                if (unavailable == null) unavailable = error.Message;
                foreach (int item in ItemTemplateList.ItemFullDico.Keys)
                {
                    string name = ItemTemplateList.GetItem(item, 1);
                    listBox5.Items.Add(name);
                    list2.Add(name);
                }
            }
            _inventoryAvailable = unavailable == null;
            _inventoryStatus.ForeColor = _inventoryAvailable ? Color.DarkGreen : Color.DarkRed;
            _inventoryStatus.Text = _inventoryAvailable
                ? "Les changements seront enregistrés ensemble après vérification des personnages et des objets."
                : unavailable;
            iTalk_Button_12.Enabled = _inventoryAvailable && _inventoryTemplates.Count > 0;
            iTalk_Button_13.Enabled = _inventoryAvailable;
            iTalk_Button_23.Enabled = false;
            iTalk_Button_23.Text = "Appliquer";
            foreach(string s in CharacterList.PersoAll.Keys)
            {
                listBox4.Items.Add(s);
                list.Add(s);
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
                    if (str.StartsWith(iTalk_TextBox_Small16.Text.Trim(), StringComparison.OrdinalIgnoreCase))

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
                    if (str.StartsWith(iTalk_TextBox_Small15.Text.Trim(), StringComparison.OrdinalIgnoreCase))

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

        private void listBox4_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listBox4.SelectedItem == null)
            {
                sfListView1.DataSource = null;
                iTalk_Label29.Text = "0";
                return;
            }
            string characterName = listBox4.SelectedItem.ToString();
            sfListView1.DataSource = null;
            // Reload on each selection: an earlier offline/failed read must not
            // remain cached after the world connection is restored.
            CharacterList.GetInventory(characterName);
            List<string> items = CharacterList.ItemsPerso.ToList();
            Persoinventory[characterName] = items;
            sfListView1.DataSource = items;
            sfListView1.Refresh();
            iTalk_Label29.Text = sfListView1.RowCount.ToString();
            if (CharacterList.InventoryLoadError != null)
            {
                _inventoryStatus.ForeColor = Color.DarkRed;
                _inventoryStatus.Text = CharacterList.InventoryLoadError;
            }
            else
            {
                int missing = items.Count(value => value.Contains("absent de la table world"));
                _inventoryStatus.ForeColor = missing > 0 ? Color.DarkRed : Color.DarkGreen;
                _inventoryStatus.Text = missing > 0
                    ? missing + " références d'objets ne figurent pas dans la table world configurée."
                    : items.Count + " objets chargés depuis la base world.";
            }


        }

        private void backgroundWorker1_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {

        }



        private void sfListView1_Click(object sender, EventArgs e)
        {

        }

        private void sfListView1_SelectionChanged(object sender, Syncfusion.WinForms.ListView.Events.ItemSelectionChangedEventArgs e)
        {
            if (sfListView1.SelectedItem != null)
            {
                var match = Regex.Match(sfListView1.SelectedItem.ToString(), @"\((\d+)\)");
                if (!match.Success || !int.TryParse(match.Groups[1].Value, out int itemGuid) ||
                    !ItemList.ItemsList.TryGetValue(itemGuid, out ItemList selectedItem))
                {
                    iTalk_Label35.Text = "-";
                    iTalk_Label36.Text = CharacterList.InventoryLoadError ?? "Objet absent de la table world";
                    iTalk_Label37.Text = "-";
                    iTalk_Label38.Text = "-";
                    return;
                }
                GUID = itemGuid;
                ID = selectedItem.Template;
                string TYPE = ItemTemplateList.GetItem(ID, 2);

                iTalk_Label35.Text = ID.ToString();
                iTalk_Label36.Text = sfListView1.SelectedItem.ToString().Split('(')[0].Trim().ToString();
                iTalk_Label37.Text = TYPE;
                iTalk_Label38.Text = selectedItem.Qua.ToString();
                try { pictureBox1.Image = Image.FromFile(SearchManager.Search_pictureItem(ID, Convert.ToInt32(TYPE))); } catch { };

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
                            New = newitem
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
        private void iTalk_Button_12_Click(object sender, EventArgs e)
        {
            StageAddItem(sender, e);
        }
        private void iTalk_Button_13_Click(object sender, EventArgs e)
        {
            StageRemoveItems(sender, e);
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

        private void iTalk_Button_23_Click(object sender, EventArgs e)
        {
            ApplyPendingInventory(sender, e);
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
            if (already != null)
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
            if (AC == false && IsNew == true)
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



        #endregion

    }
}

