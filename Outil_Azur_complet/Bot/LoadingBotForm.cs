using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Jobs;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Interactives;
using Tool_BotProtocol.Game.Monstres;
using Tool_BotProtocol.Game.NPC;
using Tool_BotProtocol.Game.Perso.Inventory;

namespace Outil_Azur_complet.Bot
{
    public partial class LoadingBotForm : Form
    {
        public LoadingBotForm()
        {
            InitializeComponent();
            backgroundWorker1.DoWork += backgroundWorker1_DoWork;
            backgroundWorker1.WorkerReportsProgress = true;

            Directory.CreateDirectory(@".\ressources\Bot\BotMaps");
            Directory.CreateDirectory(@".\ressources\Bot\BotObjets");
            Directory.CreateDirectory(@".\ressources\Bot\BotJobs");
            Directory.CreateDirectory(@".\ressources\Bot\AccountSingle");
            Directory.CreateDirectory(@".\ressources\Bot\BotZaaps");
            Directory.CreateDirectory(@".\ressources\Bot\BotNPCs");
            Directory.CreateDirectory(@".\ressources\Bot\BotMonsters\");
        }

        private void LoadingBotForm_Load(object sender, EventArgs e)
        {
            backgroundWorker1.RunWorkerAsync();
        }
        private void backgroundWorker1_DoWork(object sender, System.ComponentModel.DoWorkEventArgs e)
        {
            MessagesReception.Init();
            Jobs.LoadAllJobs();
            Map.LoadAllMaps();
            Monstres.LoadAllMonstrers();
            PNJ.LoadAllNPC();
            Zaaps.LoadZaaps();
            InventoryClass.LoadAllObjects();
        }

        private void backgroundWorker1_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            LoginForm LF = new LoginForm();
            LF.Show();
            Close();
        }
    }
}
