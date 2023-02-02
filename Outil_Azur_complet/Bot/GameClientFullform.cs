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
using Tool_BotProtocol.Game.Accounts;

namespace Outil_Azur_complet.Bot
{
    public partial class GameClientFullform : Form
    {
        public Accounts ActualCompte { get; set; }
        ToolTip T = new ToolTip();
        public List<string> DebugMessages = new List<string>();

        public GameClientFullform(Accounts A)
        {
            ActualCompte= A;
            InitializeComponent();
        }
        public void InGameMap()
        { 
            MapControl MC = new MapControl(ActualCompte);
            MC.Size = tableLayoutPanel2.Size;

            tableLayoutPanel2.Controls.Add(MC, 0, 0);
        }

        private void GameClientFullform_Load(object sender, EventArgs e)
        {
            InGameMap();
        }

        private void iTalk_ThemeContainer1_Click(object sender, EventArgs e)
        {

        }

        private void déconnexionToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            ActualCompte.Disconnect();
            LoginForm loginForm= new LoginForm();
            loginForm.Show();
            Close();
        }

        private void changerDePersoToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ActualCompte.Disconnect();
            PersoSelection PS = new PersoSelection(ActualCompte.accountConfig);
            PS.Show();
            Close();
        }

        private void iTalk_ContextMenuStrip1_Opening(object sender, CancelEventArgs e)
        {

        }

        private void roundedButton9_MouseEnter(object sender, EventArgs e)
        {
            T.SetToolTip(roundedButton9, "Stats et métiers");
        }

        private void roundedButton1_Click(object sender, EventArgs e)
        {

        }

        private void roundedButton1_MouseEnter(object sender, EventArgs e)
        {
            T.SetToolTip(roundedButton1, "Sorts");
        }

        private void roundedButton2_MouseEnter(object sender, EventArgs e)
        {
            T.SetToolTip(roundedButton2, "Inventaire");
        }

        private void roundedButton3_MouseEnter(object sender, EventArgs e)
        {
            T.SetToolTip(roundedButton3, "Quêtes");
        }

        private void roundedButton4_MouseEnter(object sender, EventArgs e)
        {
            T.SetToolTip(roundedButton4, "Géoposition");
        }

        private void roundedButton5_MouseEnter(object sender, EventArgs e)
        {
            T.SetToolTip(roundedButton5, "Amis");
        }

        private void roundedButton6_MouseEnter(object sender, EventArgs e)
        {
            T.SetToolTip(roundedButton6, "Guilde");
        }

        private void roundedButton7_MouseEnter(object sender, EventArgs e)
        {
            T.SetToolTip(roundedButton7, "Monture");
        }

        private void roundedButton8_MouseEnter(object sender, EventArgs e)
        {
            T.SetToolTip(roundedButton8, "Conquête");
        }
    }
}
