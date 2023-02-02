using Outil_Azur_complet.Bot.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Utils.Logger;

namespace Outil_Azur_complet.Bot
{
    public partial class GameForm : Form
    {
        public Accounts ActualCompte { get; set; }
        public List<string> DebugMessages = new List<string>();
        public GameForm(Accounts A)
        {
            ActualCompte = A;
            InitializeComponent();
        }

        private void GameForm_Load(object sender, EventArgs e)
        {

            iTalk_Listview1.Columns.Add("Date", 100, HorizontalAlignment.Left);
            iTalk_Listview1.Columns.Add("Message", 240, HorizontalAlignment.Left);
            ActualCompte.Logger.log_event += (str, color) => WriteMessages(str.ToString(), color);
            ActualCompte.Connexion.packetReceivedEvent += PacketRecu;
            ActualCompte.Connexion.packetSendEvent += PacketSent;
            ActualCompte.Connexion.socketInformationEvent += GetSocketInfo;
            ActualCompte.Game.character.ChatPrivate += AddPrivateToList;
            PlayerSelection();
            iTalk_RadioButton2.Checked = true;


        }
        private void PlayerSelection()
        {
            AddNewTab("Map", new MapControl(ActualCompte));
        }
        private void AddNewTab(string name, UserControl C)
        {
            iTalk_TabControl1.BeginInvoke((Action)(() =>
            {
                C.Dock = DockStyle.Fill;
                TabPage tab = new TabPage(name);
                tab.Controls.Add(C);
                iTalk_TabControl1.TabPages.Add(tab);
            }));
        }
        private void iTalk_Button_11_MouseEnter(object sender, EventArgs e)
        {
            toolTip1 = new ToolTip();
            toolTip1.SetToolTip(iTalk_Button_11, "Déconnexion");
        }
        public void AddPrivateToList(string who)
        {
            try
            {
                BeginInvoke((Action)(() =>
                {
                    if (!iTalk_ComboBox1.Items.Contains(who))
                        iTalk_ComboBox1.Items.Add(who);
                }));
            }
            catch
            {

            }
        }
        private void iTalk_TextBox_Small2_TextChanged(object sender, EventArgs e)
        {

        }
        private void GetSocketInfo(object Error) => WriteMessages("[" + DateTime.Now.ToString("HH:mm:ss") + "] [Connexion] " + Error, LogTypes.WARNING.ToString("X"));
        public void PacketRecu(string p)
         {
             WritePackets(p, false);
         }
         public void PacketSent(string p)
         {
             WritePackets(p, true);
         }
        private void WritePackets(string p, bool issent)
         {
             try
             {
                 BeginInvoke((Action)(() =>
                 {
                     if (DebugMessages.Count == 200)
                     {
                         DebugMessages.RemoveAt(0);
                         iTalk_Listview1.Items.RemoveAt(0);
                     }

                     DebugMessages.Add(p);

                     ListViewItem packetdebug = iTalk_Listview1.Items.Add(DateTime.Now.ToString("HH:mm:ss"));
                     packetdebug.BackColor = issent ? Color.FromArgb(242, 174, 138) : Color.FromArgb(170, 196, 237);
                     packetdebug.SubItems.Add(p);
                 }));
             }
             catch { }
         }
         private void WriteMessages(string message, string color)
         {
             try
             {

                 if (!IsHandleCreated)
                     return;

                richTextBox1.BeginInvoke((Action)(() =>
                 {
                     richTextBox1.Select(richTextBox1.TextLength, 0);
                     richTextBox1.SelectionColor = ColorTranslator.FromHtml("#" + color);
                     richTextBox1.AppendText(message + Environment.NewLine);
                     richTextBox1.ScrollToCaret();
                 }));
             }
             catch (Exception e)
             {
                 ActualCompte.Logger.LogException("[GAMEFORM]", e);
             }
         }
        private void Tchat()
        {
            switch (iTalk_TextBox_Small2.Text.ToUpper())
            {
                case "/MAPID":
                    WriteMessages(ActualCompte.Game.Map.MapID.ToString(), "0040FF");
                    break;
                case "/CELLID":
                    WriteMessages(ActualCompte.Game.character.Cell.CellID.ToString(), "0040FF");
                    break;
                case "/PING":
                    if (ActualCompte.Connexion != null)
                        ActualCompte.Connexion.SendPacket("ping", true);
                    else
                        WriteMessages("Pas de compte connecté", "0040FF");
                    break;
                default:
                    if (iTalk_RadioButton2.Checked == true)
                        ActualCompte.Connexion.SendPacket($"BM*|{iTalk_TextBox_Small2.Text}|", true);
                    if (iTalk_RadioButton3.Checked == true)
                        ActualCompte.Connexion.SendPacket($"BM:|{iTalk_TextBox_Small2.Text}|", true);
                    if (iTalk_RadioButton4.Checked == true)
                        ActualCompte.Connexion.SendPacket($"BM%|{iTalk_TextBox_Small2.Text}|", true);
                    if (iTalk_RadioButton1.Checked == true)
                        ActualCompte.Connexion.SendPacket($"BM?|{iTalk_TextBox_Small2.Text}|", true);
                    if (iTalk_RadioButton5.Checked == true)
                        ActualCompte.Connexion.SendPacket($"BM{iTalk_ComboBox1.SelectedItem.ToString()}|{iTalk_TextBox_Small2.Text}|", true);
                    break;

            }
            iTalk_TextBox_Small2.Text = String.Empty;
        }
        private void iTalk_Button_22_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(iTalk_TextBox_Small2.Text))
            {
                Tchat();
            }
        }

        private void iTalk_TextBox_Small2_KeyDown(object sender, KeyEventArgs e)
        {
            if(e.KeyCode == Keys.Enter && iTalk_TextBox_Small2.Text.Length > 0)
            {
                Tchat();
            }
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private void iTalk_RadioButton5_CheckedChanged(object sender)
        {
            if (iTalk_RadioButton5.Checked == true)
                iTalk_ComboBox1.Enabled = true;
            else
                iTalk_ComboBox1.Enabled=false;
        }

        private void iTalk_Listview1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (iTalk_Listview1.FocusedItem?.Index == -1 || iTalk_Listview1.SelectedItems.Count == 0)
                return;
            string message = DebugMessages[iTalk_Listview1.FocusedItem.Index];
            treeView1.Nodes.Clear();

            if (MessagesReception.messagesDatas.Count == 0)
                return;

            foreach(MessagesData D in MessagesReception.messagesDatas)
            {
                if (message.StartsWith(D.MessageName))
                {
                    treeView1.Nodes.Add(D.MessageName);
                    treeView1.Nodes[0].Nodes.Add(message.Remove(0, D.MessageName.Length));
                    treeView1.Nodes[0].Expand();
                    break;
                }
            }
        }

        private void iTalk_Button_12_Click(object sender, EventArgs e)
        {
            ActualCompte.Disconnect();
            Close();
        }

        private void iTalk_Button_23_Click(object sender, EventArgs e)
        {
            PersoForm P = new PersoForm(ActualCompte);
            P.ShowDialog();
        }
    }
}
