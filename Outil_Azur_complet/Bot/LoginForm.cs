using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Tool_BotProtocol.Config;
namespace Outil_Azur_complet.Bot
{
    public partial class LoginForm : Form
    {
        private ListView accounts;
        private TextBox login, password, host, authPort, gamePort, version, core, loader;
        private CheckBox remember;
        private Label status;
        private bool unsupportedProfile, updatingFields;
        public LoginForm() { string error=null;try { GlobalConfig.InitializeConfig(); }catch(Exception ex) { error="Configuration à corriger : "+ex.Message; }try { AccountConfig.LoadAccount();if(AccountConfig.LoadWarnings.Length>0)error=AccountConfig.LoadWarnings.Length+" compte(s) illisible(s) ou protégé(s) ignoré(s). Renseignez de nouveau leurs identifiants pour les enregistrer ici."; }catch(Exception ex) { error="Lecture des comptes : "+ex.Message; }BuildLayout();UpdateListViews();if(error!=null)status.Text=error; }
        private void BuildLayout()
        {
            var root = BotUi.Window(this,"Bot · Connexion","Connectez un compte à votre serveur, puis choisissez le serveur de jeu et le personnage.");
            var tabs=new TabControl { Dock=DockStyle.Fill, Font=Font }; root.Controls.Add(tabs,0,1);
            var connect=new TabPage("Comptes et connexion") { BackColor=BackColor,Padding=new Padding(12) };
            var optionsPage=new TabPage("Serveur et protocole") { BackColor=BackColor,Padding=new Padding(12) }; tabs.TabPages.Add(connect);tabs.TabPages.Add(optionsPage);
            var split=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2 };split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,52));split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,48));connect.Controls.Add(split);
            // Composition de l'écran de connexion du client : logo et illustration en haut, œufs de classe en bas.
            var eggs=BotUi.Banner("oeufs",92);if(eggs!=null) { connect.Controls.Add(eggs);split.BringToFront(); }
            var header=BotUi.Banner("bandeau-connexion",70,"logo",true);if(header!=null) { header.Dock=DockStyle.Top;connect.Controls.Add(header);split.BringToFront(); }
            var saved=BotUi.Card("Comptes enregistrés","Sélectionnez un compte pour renseigner les champs ou vous connecter."); accounts=BotUi.List("Compte","Destination");accounts.Columns[0].Width=230;accounts.Columns[1].Width=170;
            accounts.SelectedIndexChanged+=(s,e)=> { if(accounts.SelectedItems.Count==0)return;var a=(AccountConfig)accounts.SelectedItems[0].Tag;updatingFields=true;login.Text=a.Account;password.Text=a.Lieu=="Officiel"?"":a.Password;remember.Checked=a.Lieu!="Officiel";unsupportedProfile=a.Lieu=="Officiel";updatingFields=false;if(unsupportedProfile)status.Text="Profil officiel historique : connexion via launcher non validée. Renseignez un compte du serveur privé."; };
            accounts.DoubleClick+=(s,e)=>ConnectSelected();BotUi.Body(saved).Controls.Add(accounts);BotUi.Body(saved).Controls.Add(BotUi.Actions(BotUi.Button("Se connecter",(s,e)=>ConnectSelected(),true),BotUi.Button("Supprimer",(s,e)=>DeleteSelected(),false,110)));split.Controls.Add(saved,0,0);
            var add=BotUi.Card("Connexion au serveur");var fields=BotUi.Fields();login=BotUi.Input("Compte");password=BotUi.Input("MotDePasse",true);BotUi.Field(fields,"Nom du compte",login);BotUi.Field(fields,"Mot de passe",password,"Les espaces du mot de passe sont conservés.");
            remember=new CheckBox { Text="Enregistrer ce compte sur cet ordinateur",Dock=DockStyle.Top,AutoSize=true,ForeColor=BotUi.Ink,BackColor=BotUi.Paper };fields.Controls.Add(remember,0,fields.RowCount++);
            status=BotUi.Status("Vérifiez l’adresse dans « Serveur et protocole ».");BotUi.Body(add).Controls.Add(fields);BotUi.Body(add).Controls.Add(status);BotUi.Body(add).Controls.Add(BotUi.Actions(BotUi.Button("Connexion",(s,e)=>ConnectEntered(),true),BotUi.Button("Fermer",(s,e)=>Close(),false,95)));split.Controls.Add(add,1,0);
            var options=BotUi.Card("Adresse et version du client","Le bot utilise TCP directement. Le serveur n’a pas besoin de répondre au ping réseau.");
            var sheet=BotUi.Fields();host=BotUi.Input("Adresse");authPort=BotUi.Input("PortLogin");gamePort=BotUi.Input("PortJeu");version=BotUi.Input("VersionClient");core=BotUi.Input("TailleCore");loader=BotUi.Input("TailleLoader");
            BotUi.Field(sheet,"Adresse du serveur",host,"Adresse IP ou nom DNS, par exemple 127.0.0.1 ou localhost.");BotUi.Field(sheet,"Port de connexion (Login)",authPort);BotUi.Field(sheet,"Port de jeu par défaut",gamePort,"L’adresse annoncée par le serveur Login est utilisée pour rejoindre le jeu.");BotUi.Field(sheet,"Version du client",version);BotUi.Field(sheet,"Taille du core en octets",core);BotUi.Field(sheet,"Taille du loader en octets",loader);
            var scroll=new Panel { Dock=DockStyle.Fill,AutoScroll=true };scroll.Controls.Add(sheet);BotUi.Body(options).Controls.Add(scroll);BotUi.Body(options).Controls.Add(BotUi.Actions(BotUi.Button("Appliquer",(s,e)=>SaveOptions(true),true),BotUi.Button("Préréglage StarLoco local",(s,e)=>LocalPreset(),false,225)));optionsPage.Controls.Add(options);UpdateOptions();
            password.KeyDown+=(s,e)=> { if(e.KeyCode==Keys.Enter) { e.SuppressKeyPress=true;ConnectEntered(); } };
            login.TextChanged+=(s,e)=> { if(!updatingFields)unsupportedProfile=false; };password.TextChanged+=(s,e)=> { if(!updatingFields)unsupportedProfile=false; };
        }
        public void UpdateListViews() { accounts.Items.Clear();foreach(var a in AccountConfig.AccountsDico.Values.OrderBy(x=>x.Account,StringComparer.CurrentCultureIgnoreCase)) { var row=accounts.Items.Add(a.Account);row.SubItems.Add(a.Lieu=="Officiel"?"Officiel":"Serveur privé");row.Tag=a; } }
        private void UpdateOptions() { host.Text=GlobalConfig.IP??"127.0.0.1";authPort.Text=GlobalConfig.AUTHPORT??"450";gamePort.Text=GlobalConfig.GAMEPORT??"5555";version.Text=GlobalConfig.VERSION??"1.34.1";core.Text=GlobalConfig.CORESIZE??"2528660";loader.Text=GlobalConfig.LOADERSIZE??"2362079"; }
        private void LocalPreset() { host.Text="127.0.0.1";authPort.Text="450";gamePort.Text="5555";version.Text="1.34.1";core.Text="2528660";loader.Text="2362079";status.Text="Préréglage StarLoco renseigné. Appliquez les options pour l’utiliser."; }
        private bool SaveOptions(bool show) { try { GlobalConfig.Validate(host.Text,authPort.Text,gamePort.Text,version.Text,core.Text,loader.Text);GlobalConfig.writenewconfig(host.Text.Trim(),authPort.Text.Trim(),gamePort.Text.Trim(),version.Text.Trim(),core.Text.Trim(),loader.Text.Trim());if(show)MessageBox.Show(this,"Options de connexion enregistrées.","Bot",MessageBoxButtons.OK,MessageBoxIcon.Information);return true; } catch(Exception ex) { MessageBox.Show(this,ex.Message,"Options à corriger",MessageBoxButtons.OK,MessageBoxIcon.Warning);return false; } }
        public bool CheckConnexion() { try { GlobalConfig.Validate(host.Text,authPort.Text,gamePort.Text,version.Text,core.Text,loader.Text);return true; } catch { return false; } }
        private void ConnectSelected() { if(accounts.SelectedItems.Count==0) { status.Text="Sélectionnez d’abord un compte.";return; }var a=(AccountConfig)accounts.SelectedItems[0].Tag;if(a.Lieu=="Officiel") { status.Text="Profil officiel historique : connexion via launcher non validée. Utilisez un compte du serveur privé.";return; }login.Text=a.Account;password.Text=a.Password;remember.Checked=true;ConnectEntered(); }
        private void ConnectEntered() { if(unsupportedProfile) { status.Text="Ce profil officiel n’est pas pris en charge. Renseignez les identifiants d’un compte du serveur privé.";return; }if(string.IsNullOrWhiteSpace(login.Text)||string.IsNullOrEmpty(password.Text)) { status.Text="Renseignez le compte et son mot de passe.";return; }if(!SaveOptions(false))return;try { GlobalConfig.BYPASS=false;var a=new AccountConfig(login.Text.Trim(),password.Text,"Privé");if(remember.Checked) { AccountConfig.WriteCompte(a.Account,a.Password,a.Lieu);AccountConfig.AccountsDico[a.Account]=a; }GoToServerPage(a); } catch(Exception ex) { status.Text=ex.Message; } }
        public void GoToServerPage(AccountConfig a) { new PersoSelection(a).Show();Close(); }
        private void DeleteSelected() { if(accounts.SelectedItems.Count==0)return;var a=(AccountConfig)accounts.SelectedItems[0].Tag;if(MessageBox.Show(this,"Supprimer les informations enregistrées pour « "+a.Account+" » ?","Supprimer le compte enregistré",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;AccountConfig.DeleteCompte(a.Account);password.Clear();UpdateListViews(); }
    }
}
