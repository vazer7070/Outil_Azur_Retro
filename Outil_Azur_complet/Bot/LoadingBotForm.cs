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
        }

        private async void LoadingBotForm_Load(object sender, EventArgs e)
        {
            await LoadDataAsync();
            OpenLoginForm();
        }

        private async Task LoadDataAsync()
        {
            await Task.Run(async () =>
            {
                Directory.CreateDirectory(@".\ressources\Bot\BotMaps");
                Directory.CreateDirectory(@".\ressources\Bot\BotObjets");
                Directory.CreateDirectory(@".\ressources\Bot\BotJobs");
                Directory.CreateDirectory(@".\ressources\Bot\AccountSingle");
                Directory.CreateDirectory(@".\ressources\Bot\BotZaaps");
                Directory.CreateDirectory(@".\ressources\Bot\BotNPCs");
                Directory.CreateDirectory(@".\ressources\Bot\BotMonsters\");

                MessagesReception.Init();
                await Jobs.LoadAllJobsAsync();
                await Map.LoadAllMapsAsync();
                await Monstres.LoadAllMonstersAsync();
                await PNJ.LoadAllNPCAsync();
                await Zaaps.LoadZaapsAsync();
                await InventoryClass.LoadAllObjectsAsync();
            });
        }

        private void OpenLoginForm()
        {
            LoginForm LF = new LoginForm();
            LF.Show();
            Close();
        }
    }

}
