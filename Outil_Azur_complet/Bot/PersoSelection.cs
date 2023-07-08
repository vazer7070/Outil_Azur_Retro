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
        List<string> list = new List<string>();
        private bool selected;
        private string selectionned;

        public bool Alreadyhere { get; private set; }

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
            A.Game.Server.WrongCredential += DisplayWrong;
            A.Game.Server.WrongVersion += DisplayWrongVersion;
            A.Game.Server.IsBanned += DisplayBantime;
            iTalk_Listview1.Columns.Add("Serveurs", 170, HorizontalAlignment.Left);
            iTalk_Listview1.Columns.Add("Nombre de perso", 150, HorizontalAlignment.Left);
            iTalk_Listview1.Columns.Add("État", 200, HorizontalAlignment.Left);


        }
        public void DisplayBantime(string time)
        {
            MessageBox.Show($"Votre compte a été banni pendant {time}.", "Connexion impossible", MessageBoxButtons.OK, MessageBoxIcon.Information);
            LoginForm LF = new LoginForm();
            LF.Show();
            Close();
            return;
        }
        public void DisplayWrongVersion(string version)
        {
            MessageBox.Show($"La version {version} n'est pas une version acceptée par le serveur, merci de corriger cela dans les options.", "Connexion impossible", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            LoginForm LF = new LoginForm();
            LF.Show();
            Close();
            return;
        }
        public void DisplayWrong()
        {
            MessageBox.Show("Impossible de se connecter au serveur, 2 raisons possibles:\n - Si c'est sur un serveur privé, merci de vérifier vos informations de connexion.\n - Si c'est sur les serveurs officiels, soit les informations de connexions sont mauvaises soit le BY-PASS ne fonctionne pas, merci de vérifier vos informations et si celles-ci sont correctes, contactez un administrateur AzurBot.", "Connexion impossible", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            LoginForm LF = new LoginForm();
            LF.Show();
            Close();
            return;
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

                            if (!list.Contains(h))
                            {
                                list.Add(h);
                                iTalk_Listview1.Items.Add(h.Split(',')[0]).SubItems.AddRange(new string[2] { h.Split(',')[1], A.Game.Server.GetState(A.Game.Server.Servers.FirstOrDefault(x => x.Key == int.Parse(h.Split(',')[0])).Value)});
                                Alreadyhere = true;
                            }
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

        private async void connexionToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SelectPlayerPerso SPP = new SelectPlayerPerso(A);
            SPP.Show();
            await A.Connexion.SendPacket($"AX{A.Game.Server.ServerID}", true);
            list.Clear();
            this.Close();
        }

        private async void iTalk_Button_11_Click(object sender, EventArgs e)
        {
            if(!string.IsNullOrWhiteSpace(iTalk_TextBox_Small1.Text))
            {
                await A.Connexion.SendPacket($"AF{iTalk_TextBox_Small1.Text}", true);
            }
        }

        private void timer2_Tick(object sender, EventArgs e)
        {
            if(!Alreadyhere)
                UpdateListViews();
        }
    }
}
