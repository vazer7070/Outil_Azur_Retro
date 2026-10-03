using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Network.Enums;
using Tool_BotProtocol.Utils.Logger;
namespace Outil_Azur_complet.Bot
{
    public partial class PersoSelection : Form
    {
        private readonly Accounts A;
        private readonly AccountConfig config;
        private ListView servers;
        private Label status;
        private RichTextBox journal;
        private Control connect, retry;
        private bool transferred, subscribed;
        private readonly bool autoConnect;
        public bool Alreadyhere { get; private set; }
        public PersoSelection(AccountConfig account) : this(account,true) { }
        public PersoSelection(AccountConfig account,bool connectAutomatically) { config=account;A=new Accounts(account);autoConnect=connectAutomatically;BuildLayout();Load+=Loaded;FormClosed+=OnSessionClosed; }
        private void BuildLayout()
        {
            var root=BotUi.Window(this,"Bot · Choisir le serveur","Compte : "+config.Account+" · Connexion à "+GlobalConfig.IP+":"+GlobalConfig.AUTHPORT);
            var split=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2 };split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,62));split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,38));root.Controls.Add(split,0,1);
            var card=BotUi.Card("Serveurs de jeu","Les serveurs disponibles sont annoncés par le serveur Login. Double-cliquez sur une ligne pour continuer.");
            servers=BotUi.List("Serveur","Personnages","État");servers.Columns[0].Width=170;servers.Columns[1].Width=120;servers.Columns[2].Width=170;servers.SelectedIndexChanged+=(s,e)=>UpdateSelection();servers.DoubleClick+=async(s,e)=>await SelectServer();
            connect=BotUi.Button("Choisir ce serveur",async(s,e)=>await SelectServer(),true,175);connect.Enabled=false;retry=BotUi.Button("Retenter",async(s,e)=>await Reconnect(),false,110);status=BotUi.Status("Connexion au serveur Login…");
            BotUi.Body(card).Controls.Add(servers);BotUi.Body(card).Controls.Add(status);BotUi.Body(card).Controls.Add(BotUi.Actions(connect,retry,BotUi.Button("Retour aux comptes",(s,e)=>Back(),false,180)));split.Controls.Add(card,0,0);
            // Bannière de l'écran de choix du serveur du client (UI_ChooseServer), au-dessus de la liste.
            var banner=BotUi.Banner("bandeau-serveurs",64,null,DockStyle.Left);if(banner!=null) { banner.Dock=DockStyle.Top;BotUi.Body(card).Controls.Add(banner);servers.BringToFront(); }
            var log=BotUi.Card("État de la connexion","Les erreurs sont affichées ici. Aucun mot de passe n’est affiché.");journal=BotUi.Journal();BotUi.Body(log).Controls.Add(journal);split.Controls.Add(log,1,0);
        }
        private async void Loaded(object sender,EventArgs args) { A.Game.Server.AddServerInMenu+=UpdateListViews;A.Game.Server.WrongCredential+=DisplayWrong;A.Game.Server.WrongVersion+=DisplayWrongVersion;A.Game.Server.IsBanned+=DisplayBantime;A.Game.Server.AlreadyConnected+=AlreadyConnected;A.AccountStateEvent+=StateChanged;A.AccountDisconnectEvent+=StateChanged;A.Logger.log_event+=Log;subscribed=true;if(autoConnect)await Reconnect();else status.Text="Sélectionnez Retenter pour démarrer la connexion."; }
        private async Task Reconnect() { retry.Enabled=false;connect.Enabled=false;servers.Items.Clear();A.Disconnect();try { await A.ConnectAsync(); }catch(Exception ex) { status.Text=ex.Message; }finally { retry.Enabled=true;StateChanged(); } }
        private void Log(LogsMessages message,string color) { BotUi.OnUi(this,()=>BotUi.Append(journal,BotPacketRedactor.Redact(message.ToString(),A),color)); }
        private void StateChanged() { BotUi.OnUi(this,()=> { status.Text=A.ConnectionStatus;UpdateSelection(); }); }
        public void UpdateListViews()
        {
            BotUi.OnUi(this,()=> {
                int chosen=servers.SelectedItems.Count==0?-1:(int)servers.SelectedItems[0].Tag;servers.BeginUpdate();servers.Items.Clear();
                string[] counts;lock(config.Servers)counts=config.Servers.ToArray();
                foreach(var pair in A.Game.Server.Servers.OrderBy(x=>x.Key))
                {
                    string count="0";foreach(var saved in counts) { var parts=saved.Split(',');if(parts.Length>1&&parts[0]==pair.Key.ToString()) { count=parts[1];break; } }
                    var row=servers.Items.Add("Serveur "+pair.Key);row.SubItems.Add(count);row.SubItems.Add(A.Game.Server.GetState(pair.Value));row.Tag=pair.Key;if(pair.Key==chosen)row.Selected=true;
                }
                servers.EndUpdate();Alreadyhere=servers.Items.Count>0;status.Text=Alreadyhere?"Choisissez un serveur en ligne.":A.ConnectionStatus;UpdateSelection();
            });
        }
        private void UpdateSelection() { ServerStates value;connect.Enabled=A.Connexion!=null&&A.Connexion.IsConnected()&&servers.SelectedItems.Count>0&&A.Game.Server.Servers.TryGetValue((int)servers.SelectedItems[0].Tag,out value)&&value==ServerStates.ONLINE; }
        private async Task SelectServer()
        {
            if(!connect.Enabled)return;int server=(int)servers.SelectedItems[0].Tag;A.Game.Server.ServerID=server;connect.Enabled=false;var next=new SelectPlayerPerso(A);next.Show();
            try { await A.Connexion.SendPacket("AX"+server,true);transferred=true;Close(); }catch(Exception ex) { next.ReleaseAccount();next.Close();status.Text=ex.Message;UpdateSelection(); }
        }
        private void Back() { new LoginForm().Show();Close(); }
        public void DisplayWrong() => Error("Le serveur a refusé le compte ou le mot de passe.");
        public void DisplayWrongVersion(string value) => Error("Version client refusée. Version attendue : "+value+". Corrigez les options de connexion.");
        public void DisplayBantime(string value) => Error("Le serveur a indiqué un bannissement : "+value+".");
        public void AlreadyConnected() => Error("Ce compte est déjà connecté. Fermez sa session précédente, puis retentez.");
        private void Error(string text) { BotUi.OnUi(this,()=> { status.Text=text;BotUi.Append(journal,text);connect.Enabled=false;retry.Enabled=true; }); }
        private void OnSessionClosed(object sender,FormClosedEventArgs args)
        {
            if(subscribed) { A.Game.Server.AddServerInMenu-=UpdateListViews;A.Game.Server.WrongCredential-=DisplayWrong;A.Game.Server.WrongVersion-=DisplayWrongVersion;A.Game.Server.IsBanned-=DisplayBantime;A.Game.Server.AlreadyConnected-=AlreadyConnected;A.AccountStateEvent-=StateChanged;A.AccountDisconnectEvent-=StateChanged;A.Logger.log_event-=Log; }
            if(!transferred)A.Dispose();
        }
    }
}
