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

        public PersoSelection(AccountConfig AC)
        {
            config = AC;
            A = new Accounts(AC);
            InitializeComponent();
        }

        private void PersoSelection_Load(object sender, EventArgs e)
        {
            iTalk_Listview1.Items.Clear();
            A.Connect();
            A.Game.Server.AlreadyConnected += AlreadyConnected;
            A.Game.Server.FoundOrNotFriend += HaveFoundFriend;
            A.Game.Server.AddServerInMenu += UpdateListViews;
            iTalk_Listview1.Columns.Add("Serveurs", 170, HorizontalAlignment.Left);
            iTalk_Listview1.Columns.Add("Nombre de perso", 150, HorizontalAlignment.Left);
            iTalk_Listview1.Columns.Add("État", 200, HorizontalAlignment.Left);

        }
        public void AlreadyConnected()
        {
            try
            {
                BeginInvoke((Action)(() =>
                {
                    MessageBox.Show("Vous êtes déjà en jeu, déconnexion de votre compte.", "Connexion impossible", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    LoginForm LF = new LoginForm();
                    LF.Show();
                    Close();
                }));
            }
            catch
            {

            }
        }
        public void HaveFoundFriend(string serveur)
        {
            try
            {
                BeginInvoke((Action)(() =>
                {
                    if (!string.IsNullOrEmpty(serveur))
                    {
                        iTalk_Label2.Text = $"{iTalk_TextBox_Small1.Text} possède {serveur.Split(',')[1].Split(';')[0]} personnage(s) sur le serveur {serveur.Split(',')[0]}";

                    }
                }));
            }
            catch
            {

            }
        }
        public void UpdateListViews()
        {
            try
            {
                BeginInvoke((Action)(() =>
                {
                    iTalk_Listview1.Items.Clear();
                    if (config.Servers.Count > 0)
                    {
                        foreach (string h in config.Servers)
                        {
                            iTalk_Listview1.Items.Add(h.Split(',')[0]).SubItems.AddRange(new string[2] { h.Split(',')[1], A.Game.Server.ServerStates.ToString() });
                        }
                    }
                }));
            }
            catch
            {

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

        private void iTalk_Button_11_Click(object sender, EventArgs e)
        {
            if(!string.IsNullOrWhiteSpace(iTalk_TextBox_Small1.Text))
            {
                A.Connexion.SendPacket($"AF{iTalk_TextBox_Small1.Text}", true);
            }
        }
    }
}
