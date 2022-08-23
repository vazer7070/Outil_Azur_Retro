using System.IO.Compression;
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Windows.Forms;
using Tools_protocol.Data;
using Tools_protocol.Json;
using Tools_protocol.Kryone.Database;
using Tools_protocol.Managers;
using Tools_protocol.Query;
using System.Diagnostics;

namespace Outil_Azur_complet
{
    public partial class InitializeForm : Form
    {
        Menu menu = new Menu();

        public static string EMUSELECT;
        public static bool MapEditorOK;
        public static bool NoDB;
        public static bool Reboot;
        public static bool NoAuth;
        public static bool noWorld;
        public static readonly string ToolVersion = "0.5";
        public static readonly string EditorVersion = update.Version.VERSION;
        public static readonly string ProtocolVersion = Tools_protocol.Json.Version.VERSION;
        public static readonly string AzurBotVersion = Tool_BotProtocol.Config.GlobalConfig.BOTVERSION;

        public InitializeForm()
        {
            InitializeComponent();
        }

        private void iTalk_ThemeContainer1_Click(object sender, EventArgs e)
        {
        }
        public void RebootInit(string h, string u, string p, string a, string w, string e = null)
        {
            noWorld = false;
            NoAuth = false;
            NoDB = false;

            if(e != null && e != EMUSELECT)
            {
                EMUSELECT = e;
                JsonManager.RewriteConfig(h, u, p, a, w, e);
                Application.Restart();
            }
            else
            {
                JsonManager.RewriteConfig(h, u, p, a, w, null);
                Application.Restart();
            }
                
        }
        private void CheckPropertieConfig()
        {
            iTalk_RichTextBox1.Text ="Le logiciel est à jour.\n";
            if (Properties.Settings.Default.OP_Hote != "" || Properties.Settings.Default.OP_Emu != "")
            {
                if (Properties.Settings.Default.OP_Emu != "")
                {
                    JsonManager.RewriteConfig(Properties.Settings.Default.OP_Hote, Properties.Settings.Default.OP_User, Properties.Settings.Default.OP_Pass, Properties.Settings.Default.OP_auth, Properties.Settings.Default.OP_world, Properties.Settings.Default.OP_Emu);

                    Properties.Settings.Default.OP_Hote = "";
                    Properties.Settings.Default.OP_User = "";
                    Properties.Settings.Default.OP_Pass = "";
                    Properties.Settings.Default.OP_auth = "";
                    Properties.Settings.Default.OP_world = "";
                    Properties.Settings.Default.OP_Emu = "";
                    Properties.Settings.Default.Save();

                    Init_Json();
                }
                else
                {
                    JsonManager.RewriteConfig(Properties.Settings.Default.OP_Hote, Properties.Settings.Default.OP_User, Properties.Settings.Default.OP_Pass, Properties.Settings.Default.OP_auth, Properties.Settings.Default.OP_world);

                    Properties.Settings.Default.OP_Hote = "";
                    Properties.Settings.Default.OP_User = "";
                    Properties.Settings.Default.OP_Pass = "";
                    Properties.Settings.Default.OP_auth = "";
                    Properties.Settings.Default.OP_world = "";
                    Properties.Settings.Default.Save();

                    Init_Json();
                }
            }
            else
            {
                Init_Json();
            }
        }
        
        private void CheckAndInstallMaj()
        {
            if (Directory.Exists($@"{Application.StartupPath}\MAJ"))
            {
                string[] DirFiles = Directory.GetFiles($@"{Application.StartupPath}\MAJ");
                string[] AppFiles = Directory.GetFiles(Application.StartupPath);
                foreach (string DirFile in DirFiles)
                {
                    if (DirFile.Contains(".dll"))
                    {
                        string file = DirFile.Split('\\').Last();
                        File.Delete(AppFiles.FirstOrDefault(x => x.Contains(file)));
                        File.Move(DirFile, $@"{Application.StartupPath}\{file}");

                    }else if (DirFile.Contains(".exe"))
                    {
                        Process.Start(DirFile);
                        Application.Exit();
                    }
                }
                Directory.Delete(UpdateJsonManager.ZipPath, true);
                iTalk_RichTextBox1.Text = "Installation de la mise à jour terminée\n";
                
                CheckAndInstallMaj();
            }
            else
            {
                if (UpdateJsonManager.NeedMaj(ToolVersion, ProtocolVersion, AzurBotVersion, EditorVersion))
                {
                    iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + "Mise à jour nécessaire.\n";
                    backgroundWorker1.RunWorkerAsync();
                }
                else
                {
                    CheckPropertieConfig();
                }
            }
            
        }
        public void InitializeForm_Load(object sender, EventArgs e)
        {
            CheckAndInstallMaj();
            
        }
        private void Init_Json()
        {
            try
            {
                iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + "Initialisation Json...\n";
                if(JsonManager.Initialize(@".\auth\auth_tables.json", @".\world\world_tables.json", @".\config.json"))
                {
                    iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + "Initialisation Json...OK\n";
                    EMUSELECT = JsonManager.SearchConfig("emu");
                    EmuManager.EMUSELECTED = JsonManager.SearchConfig("emu");
                    EmuManager.InitEmu(EMUSELECT);
                    if (string.IsNullOrEmpty(EMUSELECT) || string.IsNullOrWhiteSpace(EMUSELECT))
                    {
                        iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + $"Merci de choisir un émulateur.!\n";
                        return;
                    }
                    else if (EmuManager.NOEMU)
                    {
                        iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + $"L'émulateur {EMUSELECT} n'est pas reconnu, merci de choisir un émulateur compatible.\n";
                        return;
                    }
                    else
                    {
                        iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + $"Émulateur choisi: {EMUSELECT}\n";
                    }
                }
                else
                {
                    iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + $"Problème lors de l'initialisation de la configuration nominale.\n";
                }
                DB_auth();
            }
            catch 
            {
                
                iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + "Aucun accès aux bases de données.\n";
                noWorld = true;
                NoAuth = true;
                NoDB = true;
                VerifAssetsFolders();
            }
        }
        public  void VerifAssetsFolders()
        {
            iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + "Vérifications des dossiers pour l'éditeur de cartes...\n";
            if (!Directory.Exists(@".\ressources\maps"))
            {
                iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + "Dossier 'maps' introuvable.\n";
                MapEditorOK = false;
            }else if (!Directory.Exists(@".\ressources\maps\backgrounds"))
            {
                iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + "Dossier 'backgrounds' introuvable.\n";
                MapEditorOK = false;
            }else if (!Directory.Exists(@".\ressources\maps\sols"))
            {
                iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + "Dossier 'sols' introuvable.\n";
                MapEditorOK = false;
            }else if (!Directory.Exists(@".\ressources\maps\objets"))
            {
                iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + "Dossier 'objets' introuvable.\n";
                MapEditorOK = false;
            }
            else if (!Directory.Exists(@".\ressources\maps\musics"))
            {
                iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + "Dossier 'musics' introuvable.\n";
                MapEditorOK = false;
            }
            else
            {
                iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + "Les dossiers pour l'édition de cartes sont présents.\n";
                MapEditorOK = true;
            }
            menu.Show();
        }
        private void Load_Misc()
        {
            
            try
            {

                    AccountList.AllAccounts();
                    iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + $"{AccountList.AccountListCount} compte(s) chargé(s).\n";
                    GroupesList.groupe();
                    CharacterList.AllPerso();
                    iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + $"{CharacterList.PersoCount} personnage(s) chargé(s).\n";
                    ConditionsListing.ConditionsLoad(@".\ressources\conditions.txt");
                    iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + $"{ConditionsListing.ConditionsDico.Count} conditions chargées.\n";
                    ItemTemplateList.Load_Item();
                    iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + $"{ItemTemplateList.CountItems} items chargés.\n";
                    ItemList.AddItemIdToList();
                    iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + $"Mise en cache des GUID...({ItemList.ItemsId.Count})\n";
                    SpellsList.Load_Spells();
                    iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + $"{SpellsList.CountSpells} sorts chargés.\n";
                    ItemSetList.LoadPano();
                    iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + $"{ItemSetList.Count_Pano} panoplies chargées.\n";
                    EffectsListing.Load_effects(@".\ressources\effects.txt");
                    EffectsListing.Load_ItemEffects(@".\ressources\itemeffects.txt");
                    EffectsListing.Load_SpellsEffects(@".\ressources\spellseffects.txt");
                    iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + $"{EffectsListing.Effects_count} + {EffectsListing.ItemEffects_count} + {EffectsListing.SpellsEffects_count} effets chargés.\n";
                    DropsList.Load_Drops();
                    iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + $"{DropsList.Drops_Count} drops chargés.\n";
                    MonsterList.Load_Monster();
                    iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + $"{MonsterList.Monsters_count} monstres chargés.\n";
                    GiftList.LoadAllgifts();
                    iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + $"{GiftList.GiftsCount} gift(s) chargé(s).\n";
                    MapsList.LoadAllMaps();
                    iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + $"{MapsList.MapsCount} cartes chargées.\n";
                    JobsList.ANPE();
                    iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + $"{JobsList.JobsCount} métiers chargés.\n";
                    NPCList.LoadAllPnj();
                    iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + $"{NPCList.NpcCount} PNJs chargés.\n";
                    NPCTemplateList.LoadAllPnjTemplate();
                    NPCList.AddNameToPnj(@".\ressources\Bot\BotNPCs\NPC_name.txt");
                    iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + $"{NPCTemplateList.PNJcount} templates de PNJs chargés.\n";
                    ZaapsList.LoadallZaaps();
                    iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + $"{ZaapsList.ZaapsCount} zaaps chargés.\n";
                VerifAssetsFolders();

            }
            catch (Exception o)
            {
                iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + $"Misc: [{o.Message}]. \n";
                NoDB = true;
                VerifAssetsFolders();
            }
        }
        private void DB_auth()
        {
            iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + "Connexion SQL auth...\n";
            if(DatabaseManager.IsServerConnected(JsonManager.Hôte, JsonManager.User, JsonManager.MDP, JsonManager.Aauth))
            {
                NoDB = false;
               // DatabaseManager.Connect(JsonManager.Hôte, JsonManager.User, JsonManager.MDP, JsonManager.Aauth);
                iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + "Connexion établie.!\n";
                DB_world();
            }
            else
            {
                DialogResult DR = MessageBox.Show($"Impossible de se connecter à la base {JsonManager.Aauth}, souhaitez-vous modifier ces informations.?", "Connexion impossible", MessageBoxButtons.YesNo, MessageBoxIcon.Error);
                if(DR == DialogResult.Yes)
                {
                    NoAuth = true;
                    NoDB = true;
                    SettingsForm SF = new SettingsForm();
                    SF.New(JsonManager.Hôte, JsonManager.User, JsonManager.MDP, JsonManager.Aauth, JsonManager.Aworld);
                    SF.ShowDialog();
                }
                else
                {
                    NoDB = true;
                }
            }
               
                
        }
        private void DB_world()
        {
                iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + "Connexion SQL world...\n";
                if(DatabaseManager2.IsServerConnected(JsonManager.Hôte, JsonManager.User, JsonManager.MDP, JsonManager.Aworld) && !NoDB)
                {
                    iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + "Connexion établie.!...\n";
                    NoDB = false;
                    Load_Misc();
                }
                else
                {
                   DialogResult DR = MessageBox.Show($"Impossible de se connecter à la base {JsonManager.Aworld}, souhaitez-vous modifier ces informations.?", "Connexion impossible", MessageBoxButtons.YesNo, MessageBoxIcon.Error);
                    if (DR == DialogResult.Yes)
                    {
                       noWorld = true;
                       NoDB = true;
                       SettingsForm SF = new SettingsForm();
                       SF.New(JsonManager.Hôte, JsonManager.User, JsonManager.MDP, JsonManager.Aauth, JsonManager.Aworld);
                       SF.ShowDialog();
                    }
                    else
                    {
                      iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + "Rendez-vous dans les options pour activer la connexion aux bases de données et les outils reliés.\n";
                       NoDB = true;
                      VerifAssetsFolders();
                    }

                }
            
        }

        private void backgroundWorker1_DoWork(object sender, System.ComponentModel.DoWorkEventArgs e)
        {
            using (var client = new WebClient())
            {
                client.DownloadFile(UpdateJsonManager.GetNewVersion(), $"{UpdateJsonManager.ZipPath}{UpdateJsonManager.GetFile()}");
            }
        }

        private void backgroundWorker1_ProgressChanged(object sender, System.ComponentModel.ProgressChangedEventArgs e)
        {
            iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + $"téléchargement en cours...{e.ProgressPercentage}%";
        }

        private void backgroundWorker1_RunWorkerCompleted(object sender, System.ComponentModel.RunWorkerCompletedEventArgs e)
        {
            iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + "Téléchargement terminé, installation en cours...\n";
            ZipFile.ExtractToDirectory($"{UpdateJsonManager.ZipPath}{UpdateJsonManager.GetFile()}", $@"{Application.StartupPath}\MAJ");
            iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + "Installation terminée.\n";

            Application.Restart();
        }
    }
}
