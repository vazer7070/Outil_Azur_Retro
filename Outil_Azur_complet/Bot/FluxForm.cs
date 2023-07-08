using Renci.SshNet.Messages.Transport;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Forms;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Utils.Logger;

namespace Outil_Azur_complet.Bot
{
    public partial class FluxForm : Form
    {
        Accounts Accounts { get; set; }
        public List<string> DebugMessages = new List<string>();
        public FluxForm(Accounts a)
        {
            Accounts= a;
            InitializeComponent();
        }

        private void iTalk_Button_11_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void FluxForm_Load(object sender, EventArgs e)
        {
            iTalk_Listview1.Columns.Add("Date", 100, HorizontalAlignment.Left);
            iTalk_Listview1.Columns.Add("Message", 240, HorizontalAlignment.Left);
            Accounts.Logger.log_event += (str, color) => WriteMessages(str.ToString(), color);
            Accounts.Logger.log_eventChat += (str, color) => WriteMessages(str.ToString(), color);
            Accounts.Connexion.packetReceivedEvent += PacketRecu;
            Accounts.Connexion.packetSendEvent += PacketSent;
            Accounts.Connexion.socketInformationEvent += GetSocketInfo;
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

                    System.Windows.Forms.ListViewItem packetdebug = iTalk_Listview1.Items.Add(DateTime.Now.ToString("HH:mm:ss"));
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
                Accounts.Logger.LogException("[GAMEFORM]", e);
            }
        }

        private void iTalk_Listview1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (iTalk_Listview1.FocusedItem?.Index == -1 || iTalk_Listview1.SelectedItems.Count == 0)
                return;
            string message = DebugMessages[iTalk_Listview1.FocusedItem.Index];
            treeView1.Nodes.Clear();

            if (MessagesReception.messagesDatas.Count == 0)
                return;

            foreach (MessagesData D in MessagesReception.messagesDatas)
            {
                if (message.StartsWith(D.MessageName))
                {
                    string j = message.Remove(0, D.MessageName.Length);
                    treeView1.Nodes.Add(D.MessageName);
                    treeView1.Nodes[0].Nodes.Add(j);
                    treeView1.Nodes[0].Expand();
                    break;
                }
            }
        }
    }
}
