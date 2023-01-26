using Outil_Azur_complet.Bot.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Jobs;

namespace Outil_Azur_complet.Bot
{
    public partial class PersoForm : Form
    {
        Accounts ACC;
        ToolTip T_T = new ToolTip();
        private List<int> PrimaryJobs = new List<int> {2, 15, 16, 24, 25, 26, 27, 28, 36, 41, 58, 65 };
        public PersoForm(Accounts A)
        {
            InitializeComponent();
            ACC = A;
            T_T.SetToolTip(button2, "Coût: 1 pour 1");
            T_T.SetToolTip(button3, "Coût: 3 pour 1");
            T_T.SetToolTip(button4, "Coût: 2 pour 1");
            T_T.SetToolTip(button5, "Coût: 1 pour 1");
            T_T.SetToolTip(button7, "Coût: 1 pour 1");
            
        }
        private void SetSelectionnedPerso()
        {
            BeginInvoke((Action)(() =>
            {
                iTalk_Label1.Text = ACC.Game.character.Name;
                iTalk_Label2.Text = $"Niveau: {ACC.Game.character.Level}";
            }));
        }
        private void SetJobsToButton(Button B, int jobid)
        {
            Jobs J = ACC.Game.character.Jobs.Find(x => x.ID == jobid);
            T_T.SetToolTip(B, J.name);
            B.BackgroundImage = Image.FromFile($@".\ressources\Bot\BotJobs\pics\{jobid}.png");
            B.BackgroundImageLayout = ImageLayout.Stretch;
        }
        private void SetAlignToButton(Button B, int align)
        {
            string align_text = "";
            if (align == 0)
                align_text = "neutre";
            else if (align == 1)
                align_text = "Bontarien";
            else if (align == 2)
                align_text = "Brakmarien";
            T_T.SetToolTip(B, align_text);
            B.BackgroundImage = Image.FromFile($@".\ressources\Bot\UI\{align}.png");
            B.BackgroundImageLayout = ImageLayout.Stretch;
        }
        private void SetAlignPerso()
        {
            BeginInvoke((Action)((() =>
            {

                int ali = ACC.Game.character.stats.Alignement;
                    SetAlignToButton(button1, ali);

            })));



        }
        private void setPersoJobs()
        {
            BeginInvoke((Action)((() =>
            {
                if(ACC.Game.character.Jobs != null)
                {
                    foreach(Jobs J in ACC.Game.character.Jobs)
                    {
                        if (PrimaryJobs.Contains(J.ID))
                        {
                            if (button8.BackgroundImage == null)
                                SetJobsToButton(button8, J.ID);
                            else if (button9.BackgroundImage == null)
                                SetJobsToButton(button9, J.ID);
                            else
                                SetJobsToButton(button10, J.ID);
                        }
                        else
                        {
                            if (button11.BackgroundImage == null)
                                SetJobsToButton(button11, J.ID);
                            else if (button12.BackgroundImage == null)
                                SetJobsToButton(button12, J.ID);
                            else
                                SetJobsToButton(button13, J.ID);

                        }
                    }
                }


            })));
        }
        private void SetCaracPerso()
        {
            BeginInvoke((Action)((() =>
            {

                iTalk_Label6.Text = $"{ACC.Game.character.stats.VitalityActual}/{ACC.Game.character.stats.MaxVitality}";
                iTalk_Label7.Text = $"{ACC.Game.character.stats.ActualEnergy}/{ACC.Game.character.stats.EnergyMax}";
                iTalk_Label9.Text = $"{ACC.Game.character.stats.PA.StatsTotal}";
                iTalk_Label11.Text = $"{ACC.Game.character.stats.PM.StatsTotal}";
                iTalk_Label4.Text = $"{ACC.Game.character.stats.ActualEXP}/{ACC.Game.character.stats.ExpNivNext}";

                iTalk_Label13.Text = $"{ACC.Game.character.stats.Vita.StatsTotal} ({ACC.Game.character.stats.Vita.equipement})";
                iTalk_Label15.Text = $"{ACC.Game.character.stats.Sagesse.StatsTotal} ({ACC.Game.character.stats.Sagesse.equipement})";
                iTalk_Label17.Text = $"{ACC.Game.character.stats.Force.StatsTotal} ({ACC.Game.character.stats.Force.equipement})";
                iTalk_Label19.Text = $"{ACC.Game.character.stats.Intell.StatsTotal} ({ACC.Game.character.stats.Intell.equipement})";
                iTalk_Label21.Text = $"{ACC.Game.character.stats.Chance.StatsTotal} ({ACC.Game.character.stats.Chance.equipement})";
                iTalk_Label23.Text = $"{ACC.Game.character.stats.Agility.StatsTotal} ({ACC.Game.character.stats.Agility.equipement})";

                iTalk_Label25.Text = $"{ACC.Game.character.stats.Initiative.StatsTotal}";
                iTalk_Label27.Text = $"{ACC.Game.character.stats.Propec.StatsTotal}";
                iTalk_Label30.Text = $"{ACC.Game.character.Carac_Points}";
                iTalk_Label33.Text = $"{ACC.Game.character.stats.Invoc.StatsTotal}";
                iTalk_Label34.Text = $"{ACC.Game.character.stats.Atteignable.StatsTotal}";



            })));
        }
        private void iTalk_Button_11_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void PersoForm_Load(object sender, EventArgs e)
        {
            if (ACC != null)
            {
                SetSelectionnedPerso();
                SetCaracPerso();
                setPersoJobs();
                SetAlignPerso();
            }
        }
        private void SendBoost(string message, int cost)
        {
            if (ACC != null && ACC.Game.character.Carac_Points >= cost)
            {
                ACC.Connexion.SendPacket(message, true);
                SetCaracPerso();
                SetSelectionnedPerso();
                Thread.Sleep(700);
                ACC.Connexion.SendPacket("BD");
            }
        }
        private void button2_Click(object sender, EventArgs e)
        {
            SendBoost("AB11", 1);
        }

        private void button3_Click(object sender, EventArgs e)
        {
            SendBoost("AB12", 3);
        }

        private void button4_Click(object sender, EventArgs e)
        {
            SendBoost("AB10", 2);
        }

        private void button5_Click(object sender, EventArgs e)
        {
            SendBoost("AB15", 1);
        }

        private void button6_Click(object sender, EventArgs e)
        {
            SendBoost("AB13", 1);
        }

        private void button7_Click(object sender, EventArgs e)
        {
            SendBoost("AB14", 1);
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if(ACC.Game.character.stats.Alignement == 0)
            {
                MessageBox.Show("Vous devez avoir un alignement autre que neutre pour pouvoir accéder à cette interface.!", "Alignement incorrect", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }
            else
            {
                AboutAli AALI = new AboutAli(ACC);
                AALI.ShowDialog();
            }
        }
    }
}
