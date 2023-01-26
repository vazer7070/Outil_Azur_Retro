using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Utils.Pics;

namespace Outil_Azur_complet.Bot
{
    public partial class SelectPlayerPerso : Form
    {
        Accounts A;
        private bool isgood = false;
        public SelectPlayerPerso(Accounts a)
        {
            A = a;
            InitializeComponent();
        }

        private void iTalk_Label1_Click(object sender, EventArgs e)
        {

        }

        private void iTalk_RadioButton1_CheckedChanged(object sender)
        {

        }
        private void UpdateCharactersInfo()
        {
            if(A == null)
                MessageBox.Show("null noob");
            else
            {
                int count = 1;
                iTalk_Label12.Text = $"{A.AboTime} jour(s)";
                foreach(string i in A.AccountCharactersInfo)
                {
                    UpdateCharacterSection(i, count);
                    count++;
                }
            }
        }
        private void ClearAll()
        {
            iTalk_Label6.Text = "";
            iTalk_Label1.Text = "";
            iTalk_Label7.Text = "";
            iTalk_Label2.Text = "";
            iTalk_Label8.Text = "";
            iTalk_Label3.Text = "";
            iTalk_Label9.Text = "";
            iTalk_Label4.Text = "";
            iTalk_Label5.Text = "";
            iTalk_Label10.Text = "";
            pictureBox1.Image = null;
            pictureBox2.Image= null;
            pictureBox3.Image = null;
            pictureBox4.Image = null;
            pictureBox5.Image = null;
            
        }
        private void UpdateCharacterSection(string perso, int count)
        {
            switch (count)
            {
                case 1:
                    iTalk_Label6.Text = $"{perso.Split('|')[0]}({perso.Split('|')[3]})";
                    iTalk_Label1.Text = $"Niveau:{perso.Split('|')[1]}";
                    pictureBox1.Image = PicturesManager.InteractivePicSprite(int.Parse(perso.Split('|')[2]), 2);
                    break;
                    case 2:
                    iTalk_Label7.Text = $"{perso.Split('|')[0]}({perso.Split('|')[3]})";
                    iTalk_Label2.Text = $"Niveau:{perso.Split('|')[1]}";
                    pictureBox2.Image = PicturesManager.InteractivePicSprite(int.Parse(perso.Split('|')[2]), 2);
                    break;
                    case 3:
                    iTalk_Label8.Text = $"{perso.Split('|')[0]}({perso.Split('|')[3]})";
                    iTalk_Label3.Text = $"Niveau:{perso.Split('|')[1]}";
                    pictureBox3.Image = PicturesManager.InteractivePicSprite(int.Parse(perso.Split('|')[2]), 2);
                    break;
                    case 4:
                    iTalk_Label9.Text = $"{perso.Split('|')[0]}({perso.Split('|')[3]})";
                    iTalk_Label4.Text = $"Niveau:{perso.Split('|')[1]}";
                    pictureBox4.Image = PicturesManager.InteractivePicSprite(int.Parse(perso.Split('|')[2]), 2);
                    break;
                    case 5:
                    iTalk_Label10.Text = $"{perso.Split('|')[0]}({perso.Split('|')[3]})";
                    iTalk_Label5.Text = $"Niveau:{perso.Split('|')[1]}";
                    pictureBox5.Image = PicturesManager.InteractivePicSprite(int.Parse(perso.Split('|')[2]), 2);
                    break;
            }
        }
        private void SelectPlayerPerso_Load(object sender, EventArgs e)
        {
            UpdateCharactersInfo();
        }

        private void iTalk_Button_21_Click(object sender, EventArgs e)
        {

        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            if(!isgood)
            {
               UpdateCharactersInfo();
                isgood= true;
            }
        }

        private void iTalk_LinkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {

            if (iTalk_Label6.Text.Contains("("))
            {
                A.Connexion.SendPacket($"AD{iTalk_Label6.Text.Split('(')[1].Split(')')[0]}|");
                isgood = false;
                ClearAll();
                UpdateCharactersInfo();
            }
        }

        private void iTalk_LinkLabel2_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            if (iTalk_Label7.Text.Contains("("))
            {
                A.Connexion.SendPacket($"AD{iTalk_Label7.Text.Split('(')[1].Split(')')[0]}|");
                isgood = false;
                ClearAll();
                UpdateCharactersInfo();
            }
        }

        private void iTalk_LinkLabel3_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            if (iTalk_Label8.Text.Contains("("))
            {
                A.Connexion.SendPacket($"AD{iTalk_Label8.Text.Split('(')[1].Split(')')[0]}|");
                isgood = false;
                ClearAll();
                UpdateCharactersInfo();
            }
        }

        private void iTalk_LinkLabel4_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            if (iTalk_Label9.Text.Contains("("))
            {
                A.Connexion.SendPacket($"AD{iTalk_Label9.Text.Split('(')[1].Split(')')[0]}|");
                isgood = false;
                ClearAll();
                UpdateCharactersInfo();
            }
        }

        private void iTalk_LinkLabel5_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
           if(iTalk_Label10.Text.Contains("("))
            {
                A.Connexion.SendPacket($"AD{iTalk_Label10.Text.Split('(')[1].Split(')')[0]}|");
                isgood = false;
                ClearAll();
                UpdateCharactersInfo();
            }
        }
    }
}
