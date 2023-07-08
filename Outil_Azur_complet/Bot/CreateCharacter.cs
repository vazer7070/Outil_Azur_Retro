using Outil_Azur_complet.Bot;
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
    public partial class CreateCharacter : Form
    {
        public Accounts a;
        private bool H;
        private int Race;
        public CreateCharacter(Accounts A)
        {
            a = A;
            InitializeComponent();
        }

        private void iTalk_RadioButton2_CheckedChanged(object sender)
        {
            if (iTalk_RadioButton2.Checked)
                H = false;
            PictureForCharacter(iTalk_ComboBox1.SelectedItem.ToString());
        }
        public void DisplayRandom(string name)
        {
            if (!string.IsNullOrEmpty(name)) 
            {
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        iTalk_TextBox_Small1.Text = name;

                    }));
                }
                catch { }

            }
        }
        private void CreateCharacter_Load(object sender, EventArgs e)
        {
            iTalk_RadioButton1.Checked = true;
            H = true;
            PictureForCharacter(iTalk_ComboBox1.SelectedItem.ToString());
            a.Game.Server.RandomName += DisplayRandom;
            a.Game.Server.FailCreatePerso += FailCreate;
        }
        public void FailCreate()
        {
            MessageBox.Show("Impossible de créer le personnage, veuillez effectuer une déco/reco puis recommencer.", "Création impossible", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            return;
        }
        private void iTalk_Button_12_Click(object sender, EventArgs e)
        {
            colorDialog1.ShowDialog();
            iTalk_TextBox_Small2.BackColor = colorDialog1.Color;
            iTalk_TextBox_Small2.Text = colorDialog1.Color.ToArgb().ToString();
        }

        private async void iTalk_Button_11_Click(object sender, EventArgs e)
        {
            await a.Connexion.SendPacket("AP", true);
        }

        private void iTalk_Button_13_Click(object sender, EventArgs e)
        {
            colorDialog1.ShowDialog();
            iTalk_TextBox_Small3.BackColor = colorDialog1.Color;
            iTalk_TextBox_Small3.Text = colorDialog1.Color.ToArgb().ToString();
        }

        private void iTalk_Button_14_Click(object sender, EventArgs e)
        {
            colorDialog1.ShowDialog();
            iTalk_TextBox_Small4.BackColor = colorDialog1.Color;
            iTalk_TextBox_Small4.Text = colorDialog1.Color.ToArgb().ToString();
        }

        public void PictureForCharacter(string race)
        {
            switch(race)
            {
                case "Ecaflip":
                    if (H)
                        pictureBox1.Image = PicturesManager.InteractivePicSprite(60, 2);
                    else
                        pictureBox1.Image = PicturesManager.InteractivePicSprite(61, 2);
                    Race = 6;
                    break;
                case "Pandawa":
                    if(H)
                        pictureBox1.Image = PicturesManager.InteractivePicSprite(120, 2);
                    else
                        pictureBox1.Image = PicturesManager.InteractivePicSprite(121, 2);
                    Race = 12;
                    break;
                case "Xélor":
                    if(H)
                        pictureBox1.Image = PicturesManager.InteractivePicSprite(50, 2);
                    else
                        pictureBox1.Image = PicturesManager.InteractivePicSprite(51, 2);
                    Race = 5;
                    break;
                case "Sram":
                    if(H)
                        pictureBox1.Image = PicturesManager.InteractivePicSprite(40, 2);
                    else
                        pictureBox1.Image = PicturesManager.InteractivePicSprite(41, 2);
                    Race = 4;
                    break;
                case "Enutrof":
                    if(H)
                        pictureBox1.Image = PicturesManager.InteractivePicSprite(30, 2);
                    else
                        pictureBox1.Image = PicturesManager.InteractivePicSprite(31, 2);
                    Race = 3;
                    break;
                case "Osamodas":
                    if(H)
                        pictureBox1.Image = PicturesManager.InteractivePicSprite(20, 2);
                    else
                        pictureBox1.Image = PicturesManager.InteractivePicSprite(21, 2);
                    Race = 2;
                    break;
                case "Sadida":
                    if(H)
                        pictureBox1.Image = PicturesManager.InteractivePicSprite(100, 2);
                    else
                        pictureBox1.Image = PicturesManager.InteractivePicSprite(101, 2);
                    Race = 10;
                    break;
                case "Sacrieur":
                    if(H)
                        pictureBox1.Image = PicturesManager.InteractivePicSprite(110, 2);
                    else
                        pictureBox1.Image = PicturesManager.InteractivePicSprite(111, 2);
                    Race = 11;
                    break;
                case "Féca":
                    if(H)
                        pictureBox1.Image = PicturesManager.InteractivePicSprite(10, 2);
                    else
                        pictureBox1.Image = PicturesManager.InteractivePicSprite(11, 2);
                    Race = 1;
                    break;
                case "Crâ":
                    if(H)
                        pictureBox1.Image = PicturesManager.InteractivePicSprite(90, 2);
                    else
                        pictureBox1.Image = PicturesManager.InteractivePicSprite(91, 2);
                    Race = 9;
                    break;
                case "Iop":
                    if(H)
                        pictureBox1.Image = PicturesManager.InteractivePicSprite(80, 2);
                    else
                        pictureBox1.Image = PicturesManager.InteractivePicSprite(81, 2);
                    Race = 8;
                    break;
                case "Eniripsa":
                    if(H)
                        pictureBox1.Image = PicturesManager.InteractivePicSprite(70, 2);
                    else
                        pictureBox1.Image = PicturesManager.InteractivePicSprite(71, 2);
                    Race = 7;
                    break;
            }
        }

        private void iTalk_ComboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            PictureForCharacter(iTalk_ComboBox1.SelectedItem.ToString());
        }
        public void NewBornInGame(Accounts A)
        {
            GameClientFullform GCFF = new GameClientFullform(A);
            GCFF.Show();
        }
        private void iTalk_RadioButton1_CheckedChanged(object sender)
        {
            if (iTalk_RadioButton1.Checked)
                H = true;
            PictureForCharacter(iTalk_ComboBox1.SelectedItem.ToString());
        }

        private void iTalk_Button_21_Click(object sender, EventArgs e)
        {
            string color1;
            string color2;
            string color3;
            if(string.IsNullOrEmpty(iTalk_TextBox_Small2.Text))
                color1 = "-1";
            else
                color1 = iTalk_TextBox_Small2.Text;

            if (string.IsNullOrEmpty(iTalk_TextBox_Small3.Text))
                color2 = "-1";
            else
                color2 = iTalk_TextBox_Small3.Text;

            if (string.IsNullOrEmpty(iTalk_TextBox_Small4.Text))
                color3 = "-1";
            else
                color3 = iTalk_TextBox_Small4.Text;

            if (!string.IsNullOrWhiteSpace(iTalk_TextBox_Small1.Text))
            {
                a.Game.Server.NameNewCharacter = iTalk_TextBox_Small1.Text;
                a.Game.Server.ExitCreationMenu = true;
                a.Connexion.SendPacket($"AA{iTalk_TextBox_Small1.Text}|{Race}|{(H ? 1 : 0)}|{color1}|{color2}|{color3}");
                NewBornInGame(a);
                Close();

            }
        }

        private void iTalk_Button_22_Click(object sender, EventArgs e)
        {
            PersoSelection PS = new PersoSelection(a.accountConfig);
            PS.Show();
            Close();
        }
    }
}
