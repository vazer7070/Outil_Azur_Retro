using iTalk;
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
        public Form FG;
        ToolTip T = new ToolTip();
        public List<string> DebugMessages = new List<string>();


        public bool GeneralTchat = false;
        public bool RecruitTchat = false;
        public bool MarchandTchat = false;
        public bool AlignTchat = false;
        public bool GuildeTchat = false;
        public bool GroupTchat = false;
        public bool TeamTchat = false;
        public bool AdminTchat = false;
        public bool PMTchat = false;

        public string WhoPM;

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
        private void ChangeTchatChannel(int channel)
        {
            switch (channel)
            {
                case 0:
                    GeneralTchat = true;
                    RecruitTchat = false;
                    MarchandTchat = false;
                    AlignTchat = false;
                    GuildeTchat = false;
                    GroupTchat = false;
                    TeamTchat = false;
                    AdminTchat = false;
                    PMTchat = false;
                    break;

                    case 1:
                    GeneralTchat = false;
                    RecruitTchat = true;
                    MarchandTchat = false;
                    AlignTchat = false;
                    GuildeTchat = false;
                    GroupTchat = false;
                    TeamTchat = false;
                    AdminTchat = false;
                    PMTchat = false;
                    break;

                    case 2:
                    GeneralTchat = false;
                    RecruitTchat = false;
                    MarchandTchat = true;
                    AlignTchat = false;
                    GuildeTchat = false;
                    GroupTchat = false;
                    TeamTchat = false;
                    AdminTchat = false;
                    PMTchat = false;
                    break;

                    case 3:
                    GeneralTchat = false;
                    RecruitTchat = false;
                    MarchandTchat = false;
                    AlignTchat = true;
                    GuildeTchat = false;
                    GroupTchat = false;
                    TeamTchat = false;
                    AdminTchat = false;
                    PMTchat = false;
                    break;

                    case 4:
                    GeneralTchat = false;
                    RecruitTchat = false;
                    MarchandTchat = false;
                    AlignTchat = false;
                    GuildeTchat = true;
                    GroupTchat = false;
                    TeamTchat = false;
                    AdminTchat = false;
                    PMTchat = false;
                    break;

                    case 5:
                    GeneralTchat = false;
                    RecruitTchat = false;
                    MarchandTchat = false;
                    AlignTchat = false;
                    GuildeTchat = false;
                    GroupTchat = true;
                    TeamTchat = false;
                    AdminTchat = false;
                    PMTchat = false;
                    break;

                    case 6:
                    GeneralTchat = false;
                    RecruitTchat = false;
                    MarchandTchat = false;
                    AlignTchat = false;
                    GuildeTchat = false;
                    GroupTchat = false;
                    TeamTchat = true;
                    AdminTchat = false;
                    PMTchat = false;
                    break;

                    case 7:
                    GeneralTchat = false;
                    RecruitTchat = false;
                    MarchandTchat = false;
                    AlignTchat = false;
                    GuildeTchat = false;
                    GroupTchat = false;
                    TeamTchat = false;
                    AdminTchat = true;
                    PMTchat = false;
                    break;

                case 8:
                    GeneralTchat = false;
                    RecruitTchat = false;
                    MarchandTchat = false;
                    AlignTchat = false;
                    GuildeTchat = false;
                    GroupTchat = false;
                    TeamTchat = false;
                    AdminTchat = false;
                    PMTchat = true;
                    break;
            }
        }
        private void GameClientFullform_Load(object sender, EventArgs e)
        {
            InGameMap();
            ActualCompte.Logger.log_eventChat += (str, color) => WriteMessages(str.ToString(), color);
            ActualCompte.Game.character.ChatPrivate += AddPrivateToList;
            ActualCompte.Game.character.SeeLifeRegen += displaylife;

            GeneralTchat = true;
            généralToolStripMenuItem.Checked = true;
            généralToolStripMenuItem.CheckState = CheckState.Checked;

            if (ActualCompte.Game.character.InGroupe)
                groupeToolStripMenuItem.Enabled = true;
            else
                groupeToolStripMenuItem.Enabled = false;
            if (ActualCompte.Game.character.HasGuild)
                guildeToolStripMenuItem.Enabled = true;
            else
                guildeToolStripMenuItem.Enabled = false;
            if (ActualCompte.Game.character.stats.Alignement != 0)
                alignementToolStripMenuItem.Enabled = true;
            else
                alignementToolStripMenuItem.Enabled = false;

            SetXpInBar();
            displaylife();
            FluxForm FF = new FluxForm(ActualCompte);
        }
        public void displaylife()
        {
            iTalk_Label1.Text = ActualCompte.Game.character.stats.VitalityActual.ToString();
        }
        private void SetXpInBar()
        {
            iTalk_ProgressBar1.Value = (long)((ActualCompte.Game.character.stats.ActualEXP / ActualCompte.Game.character.stats.ExpNivNext) * 100);
            iTalk_ProgressBar1.Text = ActualCompte.Game.character.Level.ToString();
        }
        public void AddPrivateToList(string who)
        {
            try
            {
                BeginInvoke((Action)(() =>
                {
                    if (!toolStripComboBox1.Items.Contains(who))
                        toolStripComboBox1.Items.Add(who);
                    if(toolStripComboBox1.Items.Count > 1)
                        WhoPM = who;
                    else
                        WhoPM = toolStripComboBox1.Items[0].ToString();
                    if (toolStripComboBox1.Items.Count > 1)
                        toolStripComboBox1.SelectedItem = who;
                    else
                        toolStripComboBox1.SelectedItem = toolStripComboBox1.Items[0];
                }));
            }
            catch
            {

            }
        }
        private void iTalk_ThemeContainer1_Click(object sender, EventArgs e)
        {

        }

        private void déconnexionToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            ActualCompte.Disconnect();
            LoginForm loginForm= new LoginForm();
            loginForm.Show();
            if(FG != null)
                FG.Close();
            Close();
        }

        private void changerDePersoToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ActualCompte.Disconnect();
            PersoSelection PS = new PersoSelection(ActualCompte.accountConfig);
            if (FG != null)
                FG.Close();
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
            T.SetToolTip(roundedButton2, $"Inventaire\n {ActualCompte.Game.character.Inventory.Actual_pods} sur {ActualCompte.Game.character.Inventory.Pods_Max} pods");
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

        private void iTalk_TextBox_Small1_TextChanged(object sender, EventArgs e)
        {

        }

        private void iTalk_Button_11_Click(object sender, EventArgs e)
        {
            iTalk_ContextMenuStrip1.Show(Cursor.Position.X, Cursor.Position.Y);
        }
        private void WriteMessages(string message, string color)
        {
            try
            {

                if (!IsHandleCreated)
                    return;

                richTextBox1.BeginInvoke((Action)(() =>
                {
                    richTextBox1.Select(richTextBox1.TextLength, 0);
                    richTextBox1.SelectionColor = ColorTranslator.FromHtml("#" + color);
                    richTextBox1.AppendText(message + Environment.NewLine);
                    richTextBox1.ScrollToCaret();
                }));
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "erreur de tchat");
                return;
            }
        }
        public async Task SpeakInTchat()
        {
            switch (iTalk_TextBox_Small1.Text.ToUpper())
            {
                case "/MAPID":
                    WriteMessages(ActualCompte.Game.Map.MapID.ToString(), "0040FF");
                    break;
                case "/CELLID":
                    WriteMessages(ActualCompte.Game.character.Cell.CellID.ToString(), "0040FF");
                    break;
                case "/PING":
                    if (ActualCompte.Connexion != null)
                        await ActualCompte.Connexion.SendPacket("ping", true);
                    else
                        WriteMessages("Pas de compte connecté", "0040FF");
                    break;
                default:
                    if (GeneralTchat)
                        await ActualCompte.Connexion.SendPacket($"BM*|{iTalk_TextBox_Small1.Text}|", true);
                    if (MarchandTchat)
                        await ActualCompte.Connexion.SendPacket($"BM:|{iTalk_TextBox_Small1.Text}|", true);
                    if (RecruitTchat)
                        await ActualCompte.Connexion.SendPacket($"BM?|{iTalk_TextBox_Small1.Text}|", true);
                    if (GuildeTchat)
                        await ActualCompte.Connexion.SendPacket($"BM%|{iTalk_TextBox_Small1.Text}|", true);
                    if(AdminTchat)
                        await ActualCompte.Connexion.SendPacket($"BM@|{iTalk_TextBox_Small1.Text}|", true);
                    if (PMTchat && !string.IsNullOrEmpty(WhoPM))
                        await ActualCompte.Connexion.SendPacket($"BM{WhoPM}|{iTalk_TextBox_Small1.Text}|", true);
                    break;

            }
            iTalk_TextBox_Small1.Text = String.Empty;
        }
        private void iTalk_TextBox_Small1_KeyDown(object sender, KeyEventArgs e)
        {
            
        }

        private void toolStripMenuItem1_Click(object sender, EventArgs e)
        {

        }

        private void textBox1_KeyDown(object sender, KeyEventArgs e)
        {
        }

        private void iTalk_Button_21_Click(object sender, EventArgs e)
        {
            if (iTalk_TextBox_Small1.Text.Length > 0)
            {
                SpeakInTchat();
            }
        }

        private void recrutementToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (recrutementToolStripMenuItem.Checked)
            {
                adminToolStripMenuItem.Checked = false;
                adminToolStripMenuItem.CheckState = CheckState.Unchecked;
                murmuresToolStripMenuItem.Checked = false;
                murmuresToolStripMenuItem.CheckState = CheckState.Unchecked;
                généralToolStripMenuItem.Checked = false;
                généralToolStripMenuItem.CheckState = CheckState.Unchecked;
                commerceToolStripMenuItem.Checked = false;
                commerceToolStripMenuItem.CheckState = CheckState.Unchecked;
                alignementToolStripMenuItem.Checked = false;
                alignementToolStripMenuItem.CheckState = CheckState.Unchecked;
                groupeToolStripMenuItem.Checked = false;
                groupeToolStripMenuItem.CheckState = CheckState.Unchecked;
                guildeToolStripMenuItem.Checked = false;
                guildeToolStripMenuItem.CheckState = CheckState.Unchecked;
                recrutementToolStripMenuItem.Checked = true;
                recrutementToolStripMenuItem.CheckState = CheckState.Checked;
            }
        }

        private void guildeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (guildeToolStripMenuItem.Checked)
            {
                adminToolStripMenuItem.Checked = false;
                adminToolStripMenuItem.CheckState = CheckState.Unchecked;
                murmuresToolStripMenuItem.Checked = false;
                murmuresToolStripMenuItem.CheckState = CheckState.Unchecked;
                généralToolStripMenuItem.Checked = false;
                généralToolStripMenuItem.CheckState = CheckState.Unchecked;
                commerceToolStripMenuItem.Checked = false;
                commerceToolStripMenuItem.CheckState = CheckState.Unchecked;
                alignementToolStripMenuItem.Checked = false;
                alignementToolStripMenuItem.CheckState = CheckState.Unchecked;
                groupeToolStripMenuItem.Checked = false;
                groupeToolStripMenuItem.CheckState = CheckState.Unchecked;
                guildeToolStripMenuItem.Checked = true;
                guildeToolStripMenuItem.CheckState = CheckState.Checked;
                recrutementToolStripMenuItem.Checked = false;
                recrutementToolStripMenuItem.CheckState = CheckState.Unchecked;
            }
        }

        private void groupeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (groupeToolStripMenuItem.Checked)
            {
                adminToolStripMenuItem.Checked = false;
                adminToolStripMenuItem.CheckState = CheckState.Unchecked;
                murmuresToolStripMenuItem.Checked = false;
                murmuresToolStripMenuItem.CheckState = CheckState.Unchecked;
                généralToolStripMenuItem.Checked = false;
                généralToolStripMenuItem.CheckState = CheckState.Unchecked;
                commerceToolStripMenuItem.Checked = false;
                commerceToolStripMenuItem.CheckState = CheckState.Unchecked;
                alignementToolStripMenuItem.Checked = false;
                alignementToolStripMenuItem.CheckState = CheckState.Unchecked;
                groupeToolStripMenuItem.Checked = true;
                groupeToolStripMenuItem.CheckState = CheckState.Checked;
                guildeToolStripMenuItem.Checked = false;
                guildeToolStripMenuItem.CheckState = CheckState.Unchecked;
                recrutementToolStripMenuItem.Checked = false;
                recrutementToolStripMenuItem.CheckState = CheckState.Unchecked;
            }
        }

        private void alignementToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (alignementToolStripMenuItem.Checked)
            {
                adminToolStripMenuItem.Checked = false;
                adminToolStripMenuItem.CheckState = CheckState.Unchecked;
                murmuresToolStripMenuItem.Checked = false;
                murmuresToolStripMenuItem.CheckState = CheckState.Unchecked;
                généralToolStripMenuItem.Checked = false;
                généralToolStripMenuItem.CheckState = CheckState.Unchecked;
                commerceToolStripMenuItem.Checked = false;
                commerceToolStripMenuItem.CheckState = CheckState.Unchecked;
                alignementToolStripMenuItem.Checked = true;
                alignementToolStripMenuItem.CheckState = CheckState.Checked;
                groupeToolStripMenuItem.Checked = false;
                groupeToolStripMenuItem.CheckState = CheckState.Unchecked;
                guildeToolStripMenuItem.Checked = false;
                guildeToolStripMenuItem.CheckState = CheckState.Unchecked;
                recrutementToolStripMenuItem.Checked = false;
                recrutementToolStripMenuItem.CheckState = CheckState.Unchecked;
            }
        }

        private void commerceToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (commerceToolStripMenuItem.Checked)
            {
                adminToolStripMenuItem.Checked = false;
                adminToolStripMenuItem.CheckState = CheckState.Unchecked;
                murmuresToolStripMenuItem.Checked = false;
                murmuresToolStripMenuItem.CheckState = CheckState.Unchecked;
                généralToolStripMenuItem.Checked = false;
                généralToolStripMenuItem.CheckState = CheckState.Unchecked;
                commerceToolStripMenuItem.Checked = true;
                commerceToolStripMenuItem.CheckState = CheckState.Checked;
                alignementToolStripMenuItem.Checked = false;
                alignementToolStripMenuItem.CheckState = CheckState.Unchecked;
                groupeToolStripMenuItem.Checked = false;
                groupeToolStripMenuItem.CheckState = CheckState.Unchecked;
                guildeToolStripMenuItem.Checked = false;
                guildeToolStripMenuItem.CheckState = CheckState.Unchecked;
                recrutementToolStripMenuItem.Checked = false;
                recrutementToolStripMenuItem.CheckState = CheckState.Unchecked;
            }
        }

        private void généralToolStripMenuItem_Click(object sender, EventArgs e)
        {

            if (généralToolStripMenuItem.Checked)
            {
                adminToolStripMenuItem.Checked = false;
                adminToolStripMenuItem.CheckState = CheckState.Unchecked;
                murmuresToolStripMenuItem.Checked = false;
                murmuresToolStripMenuItem.CheckState = CheckState.Unchecked;
                généralToolStripMenuItem.Checked = true;
                généralToolStripMenuItem.CheckState = CheckState.Checked;
                commerceToolStripMenuItem.Checked = false;
                commerceToolStripMenuItem.CheckState = CheckState.Unchecked;
                alignementToolStripMenuItem.Checked = false;
                alignementToolStripMenuItem.CheckState = CheckState.Unchecked;
                groupeToolStripMenuItem.Checked = false;
                groupeToolStripMenuItem.CheckState = CheckState.Unchecked;
                guildeToolStripMenuItem.Checked = false;
                guildeToolStripMenuItem.CheckState = CheckState.Unchecked;
                recrutementToolStripMenuItem.Checked = false;
                recrutementToolStripMenuItem.CheckState = CheckState.Unchecked;
            }
        }

        private void murmuresToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (murmuresToolStripMenuItem.Checked)
            {
                adminToolStripMenuItem.Checked = false;
                adminToolStripMenuItem.CheckState = CheckState.Unchecked;
                murmuresToolStripMenuItem.Checked = true;
                murmuresToolStripMenuItem.CheckState = CheckState.Checked;
                généralToolStripMenuItem.Checked = false;
                généralToolStripMenuItem.CheckState = CheckState.Unchecked;
                commerceToolStripMenuItem.Checked = false;
                commerceToolStripMenuItem.CheckState = CheckState.Unchecked;
                alignementToolStripMenuItem.Checked = false;
                alignementToolStripMenuItem.CheckState = CheckState.Unchecked;
                groupeToolStripMenuItem.Checked = false;
                groupeToolStripMenuItem.CheckState = CheckState.Unchecked;
                guildeToolStripMenuItem.Checked = false;
                guildeToolStripMenuItem.CheckState = CheckState.Unchecked;
                recrutementToolStripMenuItem.Checked = false;
                recrutementToolStripMenuItem.CheckState = CheckState.Unchecked;
            }
        }

       

        private void toolStripComboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            WhoPM = toolStripComboBox1.SelectedItem.ToString();
        }

        

        private void murmuresToolStripMenuItem_CheckedChanged(object sender, EventArgs e)
        {
            if(murmuresToolStripMenuItem.Checked)
            {
                if (!string.IsNullOrEmpty(WhoPM))
                    ChangeTchatChannel(8);
            }
        }

        private void adminToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (adminToolStripMenuItem.Checked)
            {
                adminToolStripMenuItem.Checked = true;
                adminToolStripMenuItem.CheckState = CheckState.Checked;
                murmuresToolStripMenuItem.Checked = false;
                murmuresToolStripMenuItem.CheckState = CheckState.Unchecked;
                généralToolStripMenuItem.Checked = false;
                généralToolStripMenuItem.CheckState = CheckState.Unchecked;
                commerceToolStripMenuItem.Checked = false;
                commerceToolStripMenuItem.CheckState = CheckState.Unchecked;
                alignementToolStripMenuItem.Checked = false;
                alignementToolStripMenuItem.CheckState = CheckState.Unchecked;
                groupeToolStripMenuItem.Checked = false;
                groupeToolStripMenuItem.CheckState = CheckState.Unchecked;
                guildeToolStripMenuItem.Checked = false;
                guildeToolStripMenuItem.CheckState = CheckState.Unchecked;
                recrutementToolStripMenuItem.Checked = false;
                recrutementToolStripMenuItem.CheckState = CheckState.Unchecked;
            }
        }

        private void adminToolStripMenuItem_CheckedChanged(object sender, EventArgs e)
        {
            if (adminToolStripMenuItem.Checked)
                ChangeTchatChannel(7);
        }

        private void alignementToolStripMenuItem_CheckedChanged(object sender, EventArgs e)
        {
           if(alignementToolStripMenuItem.Checked)
                if (ActualCompte.Game.character.stats.Alignement != 0)
                    ChangeTchatChannel(3);
                else
                    ChangeTchatChannel(0);

        }

        private void groupeToolStripMenuItem_CheckedChanged(object sender, EventArgs e)
        {
            if(groupeToolStripMenuItem.Checked)
                if (ActualCompte.Game.character.InGroupe)
                    ChangeTchatChannel(5);
                else
                    ChangeTchatChannel(0);
        }

        private void guildeToolStripMenuItem_CheckedChanged(object sender, EventArgs e)
        {
            if(guildeToolStripMenuItem.Checked)
                if (ActualCompte.Game.character.HasGuild)
                    ChangeTchatChannel(4);
                else
                    ChangeTchatChannel(0);
        }

        private void recrutementToolStripMenuItem_CheckedChanged(object sender, EventArgs e)
        {
            if(recrutementToolStripMenuItem.Checked)
                ChangeTchatChannel(1);
        }

        private void commerceToolStripMenuItem_CheckedChanged(object sender, EventArgs e)
        {
            if(commerceToolStripMenuItem.Checked)
                ChangeTchatChannel(2);
        }

        private void généralToolStripMenuItem_CheckedChanged(object sender, EventArgs e)
        {
            if(généralToolStripMenuItem.Checked)
                ChangeTchatChannel(0);
        }

        private void iTalk_ProgressBar1_MouseEnter(object sender, EventArgs e)
        {
            T.SetToolTip(iTalk_ProgressBar1, $"{ActualCompte.Game.character.stats.ActualEXP}/{ActualCompte.Game.character.stats.ExpNivNext}");
        }

        private void fluxToolStripMenuItem_Click(object sender, EventArgs e)
        {
            FluxForm FF = new FluxForm(ActualCompte);
            FG = FF;
            FF.Show();
        }

        private void roundedButton9_Click(object sender, EventArgs e)
        {

        }
    }
}
