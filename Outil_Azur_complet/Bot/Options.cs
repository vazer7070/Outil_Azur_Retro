using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Config;

namespace Outil_Azur_complet.Bot
{
    public partial class Options : Form
    {
        static bool IsOfficial = false;
        public Options()
        {
            InitializeComponent();
            iTalk_TextBox_Small1.Text = GlobalConfig.IP;
            iTalk_TextBox_Small2.Text = GlobalConfig.AUTHPORT;
            iTalk_TextBox_Small3.Text = GlobalConfig.GAMEPORT;
            iTalk_TextBox_Small4.Text = GlobalConfig.VERSION;
            
        }

        private void iTalk_Button_11_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void iTalk_ThemeContainer1_Click(object sender, EventArgs e)
        {

        }

        private void iTalk_Label2_Click(object sender, EventArgs e)
        {

        }

        private void Options_Load(object sender, EventArgs e)
        {
           
        }

        private void iTalk_Button_21_Click(object sender, EventArgs e)
        {
            if (iTalk_ComboBox1.SelectedItem.ToString().Equals("Serveur officiel"))
                IsOfficial = GlobalConfig.BYPASS = true;
            else
                IsOfficial = GlobalConfig.BYPASS = false;

                bool isgood = false;
            if (!IPAddress.TryParse(iTalk_TextBox_Small1.Text, out IPAddress address))
            {
                isgood = false;
                iTalk_TextBox_Small1.BackColor = Color.Red;
                
            }
            if(!int.TryParse(iTalk_TextBox_Small2.Text, out int value))
            {
                iTalk_TextBox_Small2.BackColor = Color.Red;
                isgood = false;
            }
            if (!int.TryParse(iTalk_TextBox_Small3.Text, out int valu))
            {
                iTalk_TextBox_Small3.BackColor = Color.Red;
                isgood = false;
            }
            else
            {
                isgood = true;
            }
            

            if(isgood || IsOfficial)
                GlobalConfig.writenewconfig(iTalk_TextBox_Small1.Text, iTalk_TextBox_Small2.Text, iTalk_TextBox_Small3.Text, iTalk_TextBox_Small4.Text);
            Close();
        }

        private void iTalk_ComboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if(iTalk_ComboBox1.SelectedItem.ToString().Equals("Serveur officiel"))
            {
                IsOfficial = true;
                iTalk_TextBox_Small1.Text = "172.65.206.193";
                iTalk_TextBox_Small2.Text = "443";
                iTalk_TextBox_Small3.Text = "5555";
                iTalk_TextBox_Small4.Text = "1.38.7";
                GlobalConfig.BYPASS = true;
            }
            else
            {
                IsOfficial = false;
                GlobalConfig.BYPASS = false;
            }
                
        }
    }
}
