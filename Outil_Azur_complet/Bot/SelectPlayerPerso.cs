using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game.Accounts;
using Outil_Azur_complet.Bot.Controls;
namespace Outil_Azur_complet.Bot
{
    public partial class SelectPlayerPerso : Form
    {
        private readonly Accounts A;
        private ListView characters;
        private Label status, subscription;
        private Control play, create, remove;
        private TextBox secretAnswer;
        private Label secretQuestion;
        private CharacterSelectionScene scene;
        private Panel deletionFields;
        private bool refreshing;
        private bool transferred, pending, receivedCharacters;
        public string LABEL;
        public SelectPlayerPerso(Accounts account) : this(account,false) { }
        public SelectPlayerPerso(Accounts account,bool listReceived)
        {
            A=account;receivedCharacters=listReceived||A.AccountCharactersInfo.Count>0;BuildLayout();_ = Handle;A.Game.Server.UpdateCharacterMenu+=UpdateCharactersInfo;A.Game.Server.CharacterDeleteFail+=DisplayFailDelete;A.Game.Server.FailSelectPerso+=DisplayNoEnterInGame;A.Game.character.Player_Selection+=CharacterSelected;A.AccountStateEvent+=StateChanged;A.AccountDisconnectEvent+=StateChanged;FormClosed+=OnSessionClosed;if(receivedCharacters)UpdateCharactersInfo();
        }
        internal void ReleaseAccount() { transferred=true; }
        private void BuildLayout()
        {
            BotUi.Prepare(this,"Azur · Choisir le personnage",new Size(1180,820));
            var root=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,
                Padding=new Padding(12,8,12,10),BackColor=BotUi.Frame };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,25));
            root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var account=BotUi.Label("Compte : "+A.accountConfig.Account+" · Serveur "+A.Game.Server.ServerID,9);
            account.ForeColor=BotUi.Gold;account.Dock=DockStyle.Fill;root.Controls.Add(account,0,0);
            // Keep this private model for the existing protocol and stable character IDs.
            // The visible selection is the supplied client's five-podium scene.
            characters=BotUi.List("Nom","Niveau","Classe et sexe","Identifiant");
            characters.Visible=false;characters.Dock=DockStyle.None;characters.Size=new Size(1,1);
            Controls.Add(characters);_ = characters.Handle;
            characters.SelectedIndexChanged+=(s,e)=> { if(!refreshing)UpdateSelection(); };
            scene=new CharacterSelectionScene { Margin=new Padding(0) };
            scene.CharacterChosen+=ChooseCharacter;scene.CharacterActivated+=async()=>await PlaySelected();
            scene.EmptySlotActivated+=Create;play=scene.PlayButton;root.Controls.Add(scene,0,1);
            create=BotUi.Button("Créer un personnage",(s,e)=>Create(),false,195);
            remove=BotUi.Button("Supprimer le personnage",async(s,e)=>await DeleteSelected(),false,195);
            status=BotUi.Status("Connexion au serveur de jeu, attente de la liste des personnages…");status.Height=24;
            subscription=BotUi.Status("");subscription.Height=22;
            var footer=new Panel { Dock=DockStyle.Top,AutoSize=true,BackColor=BotUi.Paper,Padding=new Padding(12,5,12,4),Margin=new Padding(0) };
            var answerFields=BotUi.Fields();secretAnswer=BotUi.Input("RéponseSecrète",true);BotUi.Field(answerFields,"Réponse secrète pour la suppression (si le serveur la demande)",secretAnswer);secretQuestion=answerFields.Controls[0].Controls.OfType<Label>().First();
            deletionFields=new Panel { Dock=DockStyle.Bottom,Height=76,Visible=false,BackColor=BotUi.Paper };
            deletionFields.Controls.Add(answerFields);
            var deletionToggle=new CheckBox { Text="Réponse secrète pour la suppression",Dock=DockStyle.Bottom,Height=29,
                ForeColor=BotUi.Ink,BackColor=BotUi.Paper,Cursor=Cursors.Hand };
            deletionToggle.CheckedChanged+=(s,e)=> { deletionFields.Visible=deletionToggle.Checked;root.PerformLayout(); };
            footer.Controls.Add(deletionFields);footer.Controls.Add(deletionToggle);footer.Controls.Add(subscription);footer.Controls.Add(status);
            footer.Controls.Add(BotUi.Actions(create,remove,BotUi.Button("Retour aux serveurs",(s,e)=>Back(),false,180)));
            root.Controls.Add(footer,0,2);Controls.Add(root);root.BringToFront();UpdateSelection();
        }
        private void ChooseCharacter(int id)
        {
            refreshing=true;
            foreach(ListViewItem row in characters.Items)row.Selected=(int)row.Tag==id;
            refreshing=false;UpdateSelection();
        }
        private void UpdateSelection()
        {
            bool connected=receivedCharacters&&A.Connexion!=null&&A.Connexion.IsConnected()&&!pending;
            play.Enabled=remove.Enabled=connected&&characters.SelectedItems.Count>0;create.Enabled=connected;
            if(characters.SelectedItems.Count>0) {
                var selected=characters.SelectedItems[0];LABEL=selected.Text+"("+selected.Tag+")";
                scene.SetSelected((int)selected.Tag);
            }
            else { LABEL=null;scene.SetSelected(-1); }
        }
        private void UpdateCharactersInfo()
        {
            BotUi.OnUi(this,()=> {
                receivedCharacters=true;
                secretQuestion.Text=string.IsNullOrWhiteSpace(A.SecretQuestion)?"Réponse secrète pour la suppression (si le serveur la demande)":"Réponse à : "+A.SecretQuestion;
                int chosen=characters.SelectedItems.Count==0?-1:(int)characters.SelectedItems[0].Tag;
                refreshing=true;characters.BeginUpdate();characters.Items.Clear();
                var displayed=new System.Collections.Generic.List<SelectionCharacter>();
                foreach(var pair in A.AccountCharactersInfo.OrderBy(x=>x.Value,StringComparer.CurrentCultureIgnoreCase))
                {
                    var fields=pair.Value.Split('|');string displayName=fields.Length>0?fields[0]:"Personnage "+pair.Key;
                    string level=fields.Length>1?fields[1]:"?",appearance=fields.Length>2?CharacterAppearance(fields[2]):"Apparence spéciale";
                    int gfx;int.TryParse(fields.Length>2?fields[2]:"",out gfx);
                    displayed.Add(new SelectionCharacter { Id=pair.Key,Name=displayName,Level=level,Gfx=gfx,Appearance=appearance });
                    var row=characters.Items.Add(displayName);row.SubItems.Add(level);row.SubItems.Add(appearance);
                    row.SubItems.Add(pair.Key.ToString());row.Tag=pair.Key;row.ToolTipText=displayName+"\nNiveau "+level+" · "+appearance;
                    if(chosen==pair.Key)row.Selected=true;
                }
                characters.EndUpdate();refreshing=false;
                if(characters.Items.Count>0&&characters.SelectedItems.Count==0)characters.Items[0].Selected=true;
                scene.SetCharacters(displayed,true,characters.SelectedItems.Count>0?(int)characters.SelectedItems[0].Tag:-1);
                subscription.Text=A.AboTime>0?"Abonnement annoncé : "+Math.Floor(A.AboTime/86400000.0).ToString(CultureInfo.CurrentCulture)+" jour(s).":"Aucun abonnement annoncé par le serveur.";
                status.Text=characters.Items.Count==0?"Aucun personnage sur ce serveur. Vous pouvez en créer un.":"Choisissez le personnage à connecter.";pending=false;UpdateSelection();
            });
        }
        private async Task PlaySelected()
        {
            if(!play.Enabled)return;int id=(int)characters.SelectedItems[0].Tag;pending=true;UpdateSelection();status.Text="Sélection du personnage…";
            try { await A.Connexion.SendPacket("AS"+id,true); }catch(Exception ex) { status.Text=ex.Message;pending=false;UpdateSelection(); }
        }
        private static string CharacterAppearance(string value) { int gfx;if(!int.TryParse(value,out gfx)||gfx<10||gfx>121||gfx%10>1)return "Apparence spéciale";string[] classes={ "Féca","Osamodas","Enutrof","Sram","Xélor","Ecaflip","Eniripsa","Iop","Crâ","Sadida","Sacrieur","Pandawa" };return classes[gfx/10-1]+" · "+(gfx%10==0?"Masculin":"Féminin"); }
        internal static Bitmap LoadCharacterPortrait(int gfx)
        {
            var artwork=OfficialSelectionAssets.Load("artwork-"+gfx);if(artwork!=null)return artwork;
            string resourceRoot=Path.Combine(Path.GetDirectoryName(typeof(SelectPlayerPerso).Assembly.Location),"ressources","Bot","sprites");
            foreach(string suffix in new[] { "F","S","R","" }) {
                string path=Path.Combine(resourceRoot,gfx+suffix+".png");
                if(!File.Exists(path))continue;
                try { using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
                    using(var source=Image.FromStream(stream))return new Bitmap(source); }
                catch(Exception error) when(error is IOException||error is UnauthorizedAccessException||error is ArgumentException
                    ||error is System.Runtime.InteropServices.ExternalException||error is OutOfMemoryException) { }
            }
            return null;
        }
        public async Task enterInGame(string label)
        {
            int open=label.LastIndexOf('('),close=label.LastIndexOf(')');int id;if(open<0||close<=open||!int.TryParse(label.Substring(open+1,close-open-1),out id))throw new ArgumentException("Identifiant du personnage invalide.");
            await A.Connexion.SendPacket("AS"+id,true);
        }
        private void CharacterSelected() { BotUi.OnUi(this,()=> { if(transferred)return;transferred=true;new GameClientFullform(A).Show();Close(); }); }
        public void DisplayNoEnterInGame() { BotUi.OnUi(this,()=> { status.Text="Le serveur a refusé ce personnage. Réessayez ou choisissez un autre personnage.";pending=false;UpdateSelection(); }); }
        public void DisplayFailDelete() { BotUi.OnUi(this,()=> { status.Text="Suppression refusée par le serveur. Vérifiez la réponse secrète si elle est requise.";pending=false;UpdateSelection(); }); }
        private void StateChanged() { BotUi.OnUi(this,()=> { if(A.Connexion==null||!A.Connexion.IsConnected())status.Text=A.ConnectionStatus;UpdateSelection(); }); }
        private async Task DeleteSelected()
        {
            if(!remove.Enabled)return;var row=characters.SelectedItems[0];
            if(MessageBox.Show(this,"Supprimer définitivement le personnage « "+row.Text+" » ?","Suppression du personnage",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;
            await DeleteCharacter((int)row.Tag);
        }
        public async Task DeleteCharacter(int id)
        {
            pending=true;UpdateSelection();status.Text="Demande de suppression envoyée au serveur…";
            try { string answer=secretAnswer.Text;if(answer.IndexOfAny(new[] { '\r','\n','\0','|' })>=0)throw new ArgumentException("La réponse secrète ne doit pas contenir de retour à la ligne ni de barre verticale.");await A.Connexion.SendPacket("AD"+id+"|"+answer.Replace(" ","%20"),true);secretAnswer.Clear(); }catch(Exception ex) { status.Text=ex.Message;pending=false;UpdateSelection(); }
        }
        private void Create() { if(!create.Enabled)return;transferred=true;new CreateCharacter(A).Show();Close(); }
        private void Back() { var config=A.accountConfig;new PersoSelection(config).Show();Close(); }
        private void OnSessionClosed(object sender,FormClosedEventArgs args)
        {
            A.Game.Server.UpdateCharacterMenu-=UpdateCharactersInfo;A.Game.Server.CharacterDeleteFail-=DisplayFailDelete;A.Game.Server.FailSelectPerso-=DisplayNoEnterInGame;A.Game.character.Player_Selection-=CharacterSelected;A.AccountStateEvent-=StateChanged;A.AccountDisconnectEvent-=StateChanged;
            if(!transferred)A.Dispose();
        }
    }
}
