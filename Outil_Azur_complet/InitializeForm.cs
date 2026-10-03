using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Tools_protocol.Data;
using Tools_protocol.Emulators;
using Tools_protocol.Json;
using Tools_protocol.Kryone.Database;
using Tools_protocol.Managers;
using Tools_protocol.Query;

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
        #region Connexions
        public void DB_auth()
        {
            iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + "Connexion SQL auth...\n";
            if (DatabaseManager.IsServerConnected(JsonManager.Hôte, JsonManager.User, JsonManager.MDP, JsonManager.Aauth))
            {
                NoDB = false;
                // DatabaseManager.Connect(JsonManager.Hôte, JsonManager.User, JsonManager.MDP, JsonManager.Aauth);
                iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + "Connexion établie.!\n";
                if (EmulatorRegistry.Current.UsesWorldDatabase)
                    DB_world();
                else
                    Load_Misc();
            }
            else
            {
                DialogResult DR = MessageBox.Show($"Impossible de se connecter à la base {JsonManager.Aauth}, souhaitez-vous modifier ces informations.?", "Connexion impossible", MessageBoxButtons.YesNo, MessageBoxIcon.Error);
                if (DR == DialogResult.Yes)
                {
                    NoAuth = true;
                    NoDB = true;
                    SettingsForm SF = new SettingsForm();
                    SF.New(JsonManager.Hôte, JsonManager.User, JsonManager.MDP, JsonManager.Aauth, JsonManager.Aworld);
                    SF.ShowDialog();
                    VerifAssetsFolders();
                }
                else
                {
                    iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + "Rendez-vous dans les options pour activer la connexion aux bases de données et les outils reliés.\n";
                    NoDB = true;
                    VerifAssetsFolders();
                }
            }


        }
        private void DB_world()
        {
            iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + "Connexion SQL world...\n";
            if (DatabaseManager2.IsServerConnected(JsonManager.Hôte, JsonManager.User, JsonManager.MDP, JsonManager.Aworld) && !NoDB)
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
                    VerifAssetsFolders();
                }
                else
                {
                    iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + "Rendez-vous dans les options pour activer la connexion aux bases de données et les outils reliés.\n";
                    NoDB = true;
                    VerifAssetsFolders();
                }

            }

        }
        #endregion
        #region HimSelf
        public void RebootInit(string h, string u, string p, string a, string w, string e = null)
        {
            noWorld = false;
            NoAuth = false;
            NoDB = false;

            if (e != null && e != EMUSELECT)
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
            if (string.IsNullOrWhiteSpace(iTalk_RichTextBox1.Text))
                iTalk_RichTextBox1.Text = "Initialisation...\n";
            if (Properties.Settings.Default.OP_Hote != "" || Properties.Settings.Default.OP_Emu != "")
            {
                if (string.IsNullOrWhiteSpace(Properties.Settings.Default.OP_Hote) &&
                    !string.IsNullOrWhiteSpace(Properties.Settings.Default.OP_Emu))
                {
                    if (!JsonManager.LectureConfig(@".\config.json"))
                    {
                        iTalk_RichTextBox1.Text += $"Configuration invalide : {JsonManager.LastError}\n";
                        NoDB = true;
                        VerifAssetsFolders();
                        return;
                    }
                    Properties.Settings.Default.OP_Hote = JsonManager.Hôte;
                    Properties.Settings.Default.OP_User = JsonManager.User;
                    Properties.Settings.Default.OP_Pass = JsonManager.MDP;
                    Properties.Settings.Default.OP_auth = JsonManager.Aauth;
                    Properties.Settings.Default.OP_world = JsonManager.Aworld;
                }
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

        public void InitializeForm_Load(object sender, EventArgs e)
        {
            // The legacy updater launches an executable from MAJ without an
            // authenticated manifest. Portable releases are installed manually
            // until a signed update format and rollback are implemented.
            try { CheckPropertieConfig(); }
            catch (Exception error)
            {
                iTalk_RichTextBox1.Text += $"Mise à jour non installée : {error.Message}\n";
                CheckPropertieConfig();
            }

        }

        private void Init_Json()
        {

            try
            {
                iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + "Initialisation Json...\n";
                if (JsonManager.Initialize(@".\auth\auth_tables.json", @".\world\world_tables.json", @".\config.json"))
                {
                    iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + "Initialisation Json...OK\n";
                    EMUSELECT = JsonManager.SearchConfig("emu");
                    if (string.IsNullOrWhiteSpace(EMUSELECT))
                    {
                        EmulatorRegistry.Select(null);
                        iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + "Merci de choisir un émulateur.!\n";
                        NoDB = true;
                        VerifAssetsFolders();
                        return;
                    }
                    if (!EmulatorRegistry.Select(EMUSELECT))
                    {
                        iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + $"L'émulateur {EMUSELECT} n'est pas reconnu, merci de choisir un émulateur compatible.\n";
                        NoDB = true;
                        VerifAssetsFolders();
                        return;
                    }
                    EMUSELECT = EmulatorRegistry.Current.Id;
                    iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + $"Émulateur choisi: {EmulatorRegistry.Current.DisplayName}\n";
                }
                else
                {
                    iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + $"Problème lors de l'initialisation de la configuration : {JsonManager.LastError}\n";
                    NoDB = true;
                    VerifAssetsFolders();
                    return;
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
        public void VerifAssetsFolders()
        {
            iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + "Vérifications des dossiers pour l'éditeur de cartes...\n";
            if (!Directory.Exists(@".\ressources\maps"))
            {
                iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + "Dossier 'maps' introuvable.\n";
                MapEditorOK = false;
            }
            else if (!Directory.Exists(@".\ressources\maps\backgrounds"))
            {
                iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + "Dossier 'backgrounds' introuvable.\n";
                MapEditorOK = false;
            }
            else if (!Directory.Exists(@".\ressources\maps\sols"))
            {
                iTalk_RichTextBox1.Text = iTalk_RichTextBox1.Text + "Dossier 'sols' introuvable.\n";
                MapEditorOK = false;
            }
            else if (!Directory.Exists(@".\ressources\maps\objets"))
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
        #endregion
        private void iTalk_ThemeContainer1_Click(object sender, EventArgs e)
        {
        }


        private void Load_Misc()
        {

            try
            {
                EmulatorRegistry.Current.LoadCaches();
                /*ConditionsListing.ConditionsLoad(@".\ressources\conditions.txt");
                ItemTemplateList.Load_Item();
                ItemList.AddItemIdToList();
                SpellsList.Load_Spells();
                SpellsBrain SB = new SpellsBrain();
                //  await SB.TradAndUnderstandSpellsAsync(); il va revenir
                ItemSetList.LoadPano();
                EffectsListing.Load_effects(@".\ressources\effects.txt");
                EffectsListing.Load_ItemEffects(@".\ressources\itemeffects.txt");
                EffectsListing.Load_SpellsEffects(@".\ressources\spellseffects.txt");
                DropsList.Load_Drops();
                MonsterList.Load_Monster();
                GiftList.LoadAllgifts();
                MapsList.LoadAllMaps();
                JobsList.ANPE();
                NPCList.LoadAllPnj();
                NPCTemplateList.LoadAllPnjTemplate();
                NPCList.AddNameToPnj(@".\ressources\Bot\BotNPCs\NPC_name.txt");
                ZaapsList.LoadallZaaps();*/
                VerifAssetsFolders();

            }
            catch (Exception o)
            {
                MessageBox.Show(o.Message);
                NoDB = true;
                VerifAssetsFolders();
            }
        }



    }
}
