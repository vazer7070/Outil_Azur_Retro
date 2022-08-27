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

namespace Outil_Azur_complet.Bot
{
    public partial class PersoForm : Form
    {
        Accounts ACC;
        ToolTip T_T = new ToolTip();
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
    }
}
