using iTalk;
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
        public string LABEL;
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
            if (iTalk_RadioButton1.Checked == true)
            {
                LABEL = iTalk_Label6.Text;
            }
        }
        private void UpdateCharactersInfo()
        {
            try
            {
                BeginInvoke((Action)(() =>
                {
                    if (A == null)
                        MessageBox.Show("null noob");
                    else 
                    {
                        int count = 1;
                        iTalk_Label12.Text = $"{A.AboTime} jour(s).";
                        foreach (int i in A.AccountCharactersInfo.Keys)
                        {
                            if (A.AccountCharactersInfo.ContainsKey(i))
                            {
                                UpdateCharacterSection(i, A.AccountCharactersInfo[i], count);
                                count++;
                            }
                                
                        }
                        
                    }

                }));
            }
            catch { }
            
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
        private void UpdateCharacterSection(int id, string perso, int count)
        {
            switch (count)
            {
                case 1:
                    iTalk_Label6.Text = $"{perso.Split('|')[0]}({id})";
                    iTalk_Label1.Text = $"Niveau:{perso.Split('|')[1]}";
                    pictureBox1.Image = PicturesManager.InteractivePicSprite(int.Parse(perso.Split('|')[2]), 2);
                    break;
                    case 2:
                    iTalk_Label7.Text = $"{perso.Split('|')[0]}({id})";
                    iTalk_Label2.Text = $"Niveau:{perso.Split('|')[1]}";
                    pictureBox2.Image = PicturesManager.InteractivePicSprite(int.Parse(perso.Split('|')[2]), 2);
                    break;
                    case 3:
                    iTalk_Label8.Text = $"{perso.Split('|')[0]}({id})";
                    iTalk_Label3.Text = $"Niveau:{perso.Split('|')[1]}";
                    pictureBox3.Image = PicturesManager.InteractivePicSprite(int.Parse(perso.Split('|')[2]), 2);
                    break;
                    case 4:
                    iTalk_Label9.Text = $"{perso.Split('|')[0]}({id})";
                    iTalk_Label4.Text = $"Niveau:{perso.Split('|')[1]}";
                    pictureBox4.Image = PicturesManager.InteractivePicSprite(int.Parse(perso.Split('|')[2]), 2);
                    break;
                    case 5:
                    iTalk_Label10.Text = $"{perso.Split('|')[0]}({id})";
                    iTalk_Label5.Text = $"Niveau:{perso.Split('|')[1]}";
                    pictureBox5.Image = PicturesManager.InteractivePicSprite(int.Parse(perso.Split('|')[2]), 2);
                    break;
            }
        }
        private void SelectPlayerPerso_Load(object sender, EventArgs e)
        {
            A.Game.Server.UpdateCharacterMenu+= UpdateCharactersInfo;
            A.Game.Server.CharacterDeleteFail += DisplayFailDelete;
            A.Game.Server.FailSelectPerso += DisplayNoEnterInGame;

        }
        public void DisplayNoEnterInGame()
        {
            MessageBox.Show("Impossible de jouer avec le personnage selectionné, veuillez effectuer une déco/reco puis recommencer.", "Impossible d'entrer en jeu", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            return;
        }
        public void enterInGame(string label)
        {
           int id = int.Parse(label.Split('(')[1].Split(')')[0]);
            A.Connexion.SendPacket($"AS{id}", true);
            A.Connexion.SendPacket("AF");
            GameClientFullform GCFF = new GameClientFullform(A);
            GCFF.Show();
            Close();
        }
        private void iTalk_Button_21_Click(object sender, EventArgs e)
        {
            if(!string.IsNullOrEmpty(LABEL))
                enterInGame(LABEL);
        }
        public void DeleteCharacter(int id)
        {
            A.Connexion.SendPacket($"AD{id}|", true);
            ClearAll();
            UpdateCharactersInfo();
        }
        public void DisplayFailDelete()
        {
            MessageBox.Show("Impossible de supprimer le personnage, veuillez effectuer une déco/reco puis recommencer.", "Suppression impossible", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            return;
        }
        private void iTalk_LinkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {

            if (iTalk_Label6.Text.Contains("("))
            {
                int id = int.Parse(iTalk_Label6.Text.Split('(')[1].Split(')')[0]);
                DeleteCharacter(id); 
            }
        }

        private void iTalk_LinkLabel2_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            if (iTalk_Label7.Text.Contains("("))
            {
                int id = int.Parse(iTalk_Label7.Text.Split('(')[1].Split(')')[0]);
                DeleteCharacter(id);
            }
        }

        private void iTalk_LinkLabel3_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            if (iTalk_Label8.Text.Contains("("))
            {
                int id = int.Parse(iTalk_Label8.Text.Split('(')[1].Split(')')[0]);
                DeleteCharacter(id);
            }
        }

        private void iTalk_LinkLabel4_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            if (iTalk_Label9.Text.Contains("("))
            {
               int id = int.Parse(iTalk_Label9.Text.Split('(')[1].Split(')')[0]);
               DeleteCharacter(id);
            }
        }

        private void iTalk_LinkLabel5_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
           if(iTalk_Label10.Text.Contains("("))
            {
                int id = int.Parse(iTalk_Label10.Text.Split('(')[1].Split(')')[0]);
                DeleteCharacter(id);
            }
        }

        private void iTalk_Button_12_Click(object sender, EventArgs e)
        {
            if (iTalk_Label10.Text.Contains("("))
            {
                MessageBox.Show("Vous avez déjà le nombre maximum de personnage.", "Création impossible", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }
            else
            {
                CreateCharacter CC = new CreateCharacter(A);
                CC.Show();
                Close();
            }
        }

        private void iTalk_RadioButton2_CheckedChanged(object sender)
        {
            if (iTalk_RadioButton2.Checked == true)
            {
                LABEL = iTalk_Label7.Text;
            }
        }

        private void iTalk_RadioButton3_CheckedChanged(object sender)
        {
           if(iTalk_RadioButton3.Checked ==true)
            {
                LABEL = iTalk_Label8.Text;
            }
        }

        private void iTalk_RadioButton4_CheckedChanged(object sender)
        {
            if (iTalk_RadioButton4.Checked == true)
            {
                LABEL = iTalk_Label9.Text;
            }
        }

        private void iTalk_RadioButton5_CheckedChanged(object sender)
        {
            if (iTalk_RadioButton5.Checked == true)
            {
                LABEL = iTalk_Label10.Text;
            }
        }

        private void iTalk_Button_11_Click(object sender, EventArgs e)
        {
            PersoSelection PS = new PersoSelection(A.accountConfig);
            PS.Show();
            Close();
        }
    }
}
