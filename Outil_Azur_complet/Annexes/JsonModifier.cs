using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tools_protocol.Json;

namespace Outil_Azur_complet.Annexes
{
    public partial class JsonModifier : Form
    {
        public JsonModifier()
        {
            InitializeComponent();
        }

        private bool IsAuth;
        private bool IsNull;
        private Dictionary<string, string> JsonModifiersAuth = new Dictionary<string, string>();
        private Dictionary<string, string> JsonModifiersWorld = new Dictionary<string, string>();
        private string TempName;
        private void iTalk_Button_11_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void JsonModifier_Load(object sender, EventArgs e)
        {
            LoadName();
        }
        private void CheckAndDispatch(bool world)
        {
            if (!world)
            {
                if (!string.IsNullOrEmpty(TempName))
                {
                    if (TempName.Split('|')[0] == "auth")
                    {
                        if (JsonManager.SearchAuth(TempName.Split('|')[1]) != TempName.Split('|')[2])
                        {
                            if (JsonModifiersAuth.ContainsKey(TempName.Split('|')[1]))
                            {
                                JsonModifiersAuth.Remove(TempName.Split('|')[1]);
                                JsonModifiersAuth.Add(TempName.Split('|')[1], TempName.Split('|')[2]);
                            }
                            else
                            {
                                JsonModifiersAuth.Add(TempName.Split('|')[1], TempName.Split('|')[2]);
                            }
                            iTalk_TextBox_Small1.Text = JsonManager.SearchAuth(iTalk_ComboBox2.SelectedItem.ToString());
                            TempName = $"{iTalk_ComboBox1.SelectedItem.ToString()}|{iTalk_ComboBox2.SelectedItem.ToString()}|{iTalk_TextBox_Small1.Text}";
                        }
                        else
                        {
                            iTalk_TextBox_Small1.Text = JsonManager.SearchAuth(iTalk_ComboBox2.SelectedItem.ToString());
                            TempName = $"{iTalk_ComboBox1.SelectedItem.ToString()}|{iTalk_ComboBox2.SelectedItem.ToString()}|{iTalk_TextBox_Small1.Text}";
                        }

                    }
                    else if (TempName.Split('|')[0] == "world")
                    {
                        if (JsonManager.SearchWorld(TempName.Split('|')[1]) != TempName.Split('|')[2])
                        {
                            if (JsonModifiersWorld.ContainsKey(TempName.Split('|')[1]))
                            {
                                JsonModifiersWorld.Remove(TempName.Split('|')[1]);
                                JsonModifiersWorld.Add(TempName.Split('|')[1], TempName.Split('|')[2]);
                            }
                            else
                            {
                                JsonModifiersWorld.Add(TempName.Split('|')[1], TempName.Split('|')[2]);
                            }
                            iTalk_TextBox_Small1.Text = JsonManager.SearchAuth(iTalk_ComboBox2.SelectedItem.ToString());
                            TempName = $"{iTalk_ComboBox1.SelectedItem.ToString()}|{iTalk_ComboBox2.SelectedItem.ToString()}|{iTalk_TextBox_Small1.Text}";
                        }
                        else
                        {
                            iTalk_TextBox_Small1.Text = JsonManager.SearchAuth(iTalk_ComboBox2.SelectedItem.ToString());
                            TempName = $"{iTalk_ComboBox1.SelectedItem.ToString()}|{iTalk_ComboBox2.SelectedItem.ToString()}|{iTalk_TextBox_Small1.Text}";
                        }
                    }
                }
                else
                {
                    iTalk_TextBox_Small1.Text = JsonManager.SearchAuth(iTalk_ComboBox2.SelectedItem.ToString());
                    TempName = $"{iTalk_ComboBox1.SelectedItem.ToString()}|{iTalk_ComboBox2.SelectedItem.ToString()}|{iTalk_TextBox_Small1.Text}";
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(TempName))
                {
                    if (TempName.Split('|')[0] == "auth")
                    {
                        if (JsonManager.SearchAuth(TempName.Split('|')[1]) != TempName.Split('|')[2])
                        {
                            if (JsonModifiersAuth.ContainsKey(TempName.Split('|')[1]))
                            {
                                JsonModifiersAuth.Remove(TempName.Split('|')[1]);
                                JsonModifiersAuth.Add(TempName.Split('|')[1], TempName.Split('|')[2]);
                            }
                            else
                            {
                                JsonModifiersAuth.Add(TempName.Split('|')[1], TempName.Split('|')[2]);
                            }
                            iTalk_TextBox_Small1.Text = JsonManager.SearchWorld(iTalk_ComboBox2.SelectedItem.ToString());
                            TempName = $"{iTalk_ComboBox1.SelectedItem.ToString()}|{iTalk_ComboBox2.SelectedItem.ToString()}|{iTalk_TextBox_Small1.Text}";
                        }
                        else
                        {
                            iTalk_TextBox_Small1.Text = JsonManager.SearchWorld(iTalk_ComboBox2.SelectedItem.ToString());
                            TempName = $"{iTalk_ComboBox1.SelectedItem.ToString()}|{iTalk_ComboBox2.SelectedItem.ToString()}|{iTalk_TextBox_Small1.Text}";
                        }

                    }
                    else if (TempName.Split('|')[0] == "world")
                    {
                        if (JsonManager.SearchWorld(TempName.Split('|')[1]) != TempName.Split('|')[2])
                        {
                            if (JsonModifiersWorld.ContainsKey(TempName.Split('|')[1]))
                            {
                                JsonModifiersWorld.Remove(TempName.Split('|')[1]);
                                JsonModifiersWorld.Add(TempName.Split('|')[1], TempName.Split('|')[2]);
                            }
                            else
                            {
                                JsonModifiersWorld.Add(TempName.Split('|')[1], TempName.Split('|')[2]);
                            }
                            iTalk_TextBox_Small1.Text = JsonManager.SearchWorld(iTalk_ComboBox2.SelectedItem.ToString());
                            TempName = $"{iTalk_ComboBox1.SelectedItem.ToString()}|{iTalk_ComboBox2.SelectedItem.ToString()}|{iTalk_TextBox_Small1.Text}";
                        }
                        else
                        {
                            iTalk_TextBox_Small1.Text = JsonManager.SearchWorld(iTalk_ComboBox2.SelectedItem.ToString());
                            TempName = $"{iTalk_ComboBox1.SelectedItem.ToString()}|{iTalk_ComboBox2.SelectedItem.ToString()}|{iTalk_TextBox_Small1.Text}";
                        }
                    }
                }
                else
                {
                    iTalk_TextBox_Small1.Text = JsonManager.SearchWorld(iTalk_ComboBox2.SelectedItem.ToString());
                    TempName = $"{iTalk_ComboBox1.SelectedItem.ToString()}|{iTalk_ComboBox2.SelectedItem.ToString()}|{iTalk_TextBox_Small1.Text}";
                }
            }
        }
        private void iTalk_ComboBox2_SelectedIndexChanged(object sender, EventArgs e)
        {
            if(!IsNull && IsAuth)
            {
                CheckAndDispatch(false);
                
            }
            else if(!IsNull && !IsAuth)
            {
                CheckAndDispatch(true);
            }

        }
        private void LoadName()
        {
            if (iTalk_ComboBox1.SelectedItem.ToString() != null)
            {
                if (iTalk_ComboBox1.SelectedItem.ToString() == "auth")
                {
                    if (iTalk_ComboBox2.Items.Count > 0)
                        iTalk_ComboBox2.Items.Clear();
                    IsAuth = true;
                    foreach (string k in JsonManager.Auth_dico.Keys)
                    {
                        iTalk_ComboBox2.Items.Add(k);
                    }
                    iTalk_ComboBox2.SelectedItem = iTalk_ComboBox2.Items[0];
                    iTalk_TextBox_Small1.Text = JsonManager.SearchAuth(iTalk_ComboBox2.Items[0].ToString());
                    TempName = $"{iTalk_ComboBox1.SelectedItem.ToString()}|{iTalk_ComboBox2.SelectedItem.ToString()}|{iTalk_TextBox_Small1.Text}";
                }
                else
                {
                    if (iTalk_ComboBox2.Items.Count > 0)
                        iTalk_ComboBox2.Items.Clear();
                    IsAuth = false;
                    foreach (string k in JsonManager.World_dico.Keys)
                        iTalk_ComboBox2.Items.Add(k);
                    iTalk_ComboBox2.SelectedItem = iTalk_ComboBox2.Items[0];
                    iTalk_TextBox_Small1.Text = JsonManager.SearchWorld(iTalk_ComboBox2.Items[0].ToString());
                    TempName = $"{iTalk_ComboBox1.SelectedItem.ToString()}|{iTalk_ComboBox2.SelectedItem.ToString()}|{iTalk_TextBox_Small1.Text}";
                }

            }
            else
                IsNull = true;
        }
        private void iTalk_ComboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            LoadName();
        }

        private void iTalk_TextBox_Small1_TextChanged(object sender, EventArgs e)
        {

        }

        private void iTalk_Button_21_Click(object sender, EventArgs e)
        {
            if(JsonModifiersAuth.Count > 0)
            {
                foreach (string h in JsonModifiersAuth.Keys)
                {
                    JsonManager.Auth_dico.Remove(h);
                    JsonManager.Auth_dico.Add(h, JsonModifiersAuth[h]);
                }
                string AuthJson = JsonConvert.SerializeObject(JsonManager.Auth_dico, Formatting.Indented);
                JsonManager.RewriteTableJson(AuthJson, true);
            }

            if(JsonModifiersWorld.Count > 0)
            {
                foreach (string k in JsonModifiersWorld.Keys)
                {
                    JsonManager.World_dico.Remove(k);
                    JsonManager.World_dico.Add(k, JsonModifiersWorld[k]);
                }
                string WorldJson = JsonConvert.SerializeObject(JsonManager.World_dico, Formatting.Indented);
                JsonManager.RewriteTableJson(WorldJson, false);
            }

            if (JsonModifiersAuth.Count > 0 || JsonModifiersWorld.Count > 0)
            {
                MessageBox.Show("Les modifications demandées ont été faites, l'application va redémarrer pour les prendres en compte.", "Redémarrage requis", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Application.Restart();
            }
           
        }
    }
}
