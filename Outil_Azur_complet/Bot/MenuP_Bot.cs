﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Jobs;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Interactives;
using Tool_BotProtocol.Game.Monstres;
using Tool_BotProtocol.Game.NPC;
using Tool_BotProtocol.Game.Perso.Inventory;

namespace Outil_Azur_complet.Bot
{
    public partial class MenuP_Bot : Form
    {
        

        public MenuP_Bot()
        {
            InitializeComponent();
            Directory.CreateDirectory(@".\ressources\Bot\BotMaps");
            Directory.CreateDirectory(@".\ressources\Bot\BotObjets");
            Directory.CreateDirectory(@".\ressources\Bot\BotJobs");
            Directory.CreateDirectory(@".\ressources\Bot\AccountSingle");
            Directory.CreateDirectory(@".\ressources\Bot\BotZaaps");
            Directory.CreateDirectory(@".\ressources\Bot\BotNPCs");
            Directory.CreateDirectory(@".\ressources\Bot\BotMonsters\");
            GlobalConfig.InitializeConfig();
            AccountConfig.LoadAccount();
            MessagesReception.Init();
            Map.LoadAllMaps();
            InventoryClass.LoadAllObjects();
            Jobs.LoadAllJobs();
            Zaaps.LoadZaaps();
            PNJ.LoadAllNPC();
            Monstres.LoadAllMonstrers();
        }

        private void fileMenu_Click(object sender, EventArgs e)
        {
            using (Gestionnaire_de_comptes GC = new Gestionnaire_de_comptes())
            {
                if (GC.ShowDialog() == DialogResult.OK)
                {
                    AccountConfig AccountConf = AccountConfig.AccountsActive[0];
                    GameForm GF = new GameForm();
                    GF.ActualCompte = new Accounts(AccountConf);
                    GF.MdiParent = this;
                    GF.Show();
                }
            }
        }

        private void editMenu_Click(object sender, EventArgs e)
        {
            Options O = new Options();
            O.ShowDialog();
        }
    }
}