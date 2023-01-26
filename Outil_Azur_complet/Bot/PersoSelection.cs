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
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Game.Accounts;

namespace Outil_Azur_complet.Bot
{
    public partial class PersoSelection : Form
    {
        AccountConfig config;
        Accounts A;
        private bool selected;
        private string selectionned;
        private bool isgood =false;

        public PersoSelection(AccountConfig AC)
        {
            config = AC;
            A = new Accounts(AC);
            InitializeComponent();
        }

        private void PersoSelection_Load(object sender, EventArgs e)
        {
            A.Connect();
            iTalk_Listview1.Columns.Add("Serveurs", 170, HorizontalAlignment.Left);
            iTalk_Listview1.Columns.Add("Nombre de perso", 150, HorizontalAlignment.Left);

        }
        public void UpdateListViews()
        {
            iTalk_Listview1.Items.Clear();
            if (config.Servers.Count > 0)
            {
                foreach (string h in config.Servers)
                {
                    iTalk_Listview1.Items.Add(h.Split(',')[0]).SubItems.AddRange(new string[1] { h.Split(',')[1] });
                }
            }

        }

        private void iTalk_Button_21_Click(object sender, EventArgs e)
        {
            UpdateListViews();
        }

        private void iTalk_Listview1_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                selected = iTalk_Listview1.SelectedItems[0].Selected;
                selectionned = iTalk_Listview1.SelectedItems[0].Text;
                if (selected)
                {
                    A.Game.Server.ServerID = int.Parse(selectionned);
                    iTalk_ContextMenuStrip1.Show(Cursor.Position.X, Cursor.Position.Y);
                }
                    
            }
            catch
            {
                selected = false;

            }
        }

        private void connexionToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SelectPlayerPerso SPP = new SelectPlayerPerso(A);
            SPP.Show();
            A.Connexion.SendPacket($"AX{A.Game.Server.ServerID}", true);
            this.Close();
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            if(!isgood)
            {
                UpdateListViews();
                isgood = true;
            }

        }
    }
}
