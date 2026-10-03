using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game.Accounts;
namespace Outil_Azur_complet.Bot
{
    public partial class CreateCharacter : Form
    {
        public Accounts a;
        private TextBox name;
        private ComboBox race, sex;
        private TextBox[] colors;
        private Label status;
        private Control create;
        private CharacterPortrait portrait;
        private Label identity;
        private Panel[] swatches;
        private Label[] colorDescriptions;
        private bool transferred, pending;
        private readonly string[] classes = { "Féca","Osamodas","Enutrof","Sram","Xélor","Ecaflip","Eniripsa","Iop","Crâ","Sadida","Sacrieur","Pandawa" };
        public CreateCharacter(Accounts account)
        {
            a=account;BuildLayout();_ = Handle;a.Game.Server.RandomName+=DisplayRandom;a.Game.Server.FailCreatePerso+=FailCreate;a.Game.Server.UpdateCharacterMenu+=OnCharacterCreated;a.AccountDisconnectEvent+=Disconnected;FormClosed+=OnSessionClosed;
        }
        private void BuildLayout()
        {
            var root=BotUi.Window(this,"Bot · Créer un personnage","La création est confirmée par le serveur avant le retour à la liste des personnages.");
            var card=BotUi.Card("Identité et apparence","Choisissez votre classe et personnalisez ses trois couleurs, ou conservez celles du client.");
            var fields=BotUi.Fields();name=BotUi.Input("NomPersonnage");name.MaxLength=20;race=new ComboBox { Dock=DockStyle.Top,DropDownStyle=ComboBoxStyle.DropDownList,Font=Font };race.Items.AddRange(classes);race.SelectedIndex=7;
            sex=new ComboBox { Dock=DockStyle.Top,DropDownStyle=ComboBoxStyle.DropDownList,Font=Font };sex.Items.AddRange(new object[] { "Masculin","Féminin" });sex.SelectedIndex=0;
            BotUi.Field(fields,"Nom du personnage",name,"Le serveur contrôle la disponibilité et les règles de nommage.");BotUi.Field(fields,"Classe",race);BotUi.Field(fields,"Sexe",sex);
            colors=new TextBox[3];for(int i=0;i<3;i++) {
                int index=i;colors[i]=BotUi.Input("Couleur"+(i+1));colors[i].Text="-1";
                var line=new Panel { Dock=DockStyle.Top,Height=43,Margin=new Padding(0,0,0,4),Padding=new Padding(0,5,0,0),BackColor=BotUi.Paper };
                colors[i].Dock=DockStyle.Top;
                var colorInput=new Panel { Dock=DockStyle.Fill,Padding=new Padding(0,4,7,0) };colorInput.Controls.Add(colors[i]);
                var colorTitle=BotUi.Label("Couleur "+(i+1),9,true);colorTitle.Dock=DockStyle.Left;colorTitle.Width=86;colorTitle.TextAlign=ContentAlignment.MiddleLeft;
                var choose=BotUi.Button("Choisir…",(s,e)=>PickColor(index),false,110);choose.Dock=DockStyle.Right;((ClientButton)choose).Glyph=ClientAssets.Icon("de-couleur",22);
                line.Controls.Add(colorInput);line.Controls.Add(choose);line.Controls.Add(colorTitle);
                fields.Controls.Add(line,0,fields.RowCount++);
            }
            var scroll=new Panel { Dock=DockStyle.Fill,AutoScroll=true,Padding=new Padding(0,0,12,0) };scroll.Controls.Add(fields);
            // Le fond de l'aperçu reprend le parchemin de l'écran de création du client pour que le socle s'y fonde.
            var socle=ClientAssets.Get("socle");Color paper=socle==null?BotUi.PaperLight:Color.FromArgb(255,socle.GetPixel(socle.Width-30,socle.Height/2));
            var preview=new ClientPanel { Dock=DockStyle.Fill,BackColor=paper,Padding=new Padding(14),Margin=new Padding(0) };
            identity=BotUi.Label("",13,true);identity.Dock=DockStyle.Top;identity.Height=35;identity.TextAlign=ContentAlignment.MiddleCenter;
            var previewTitle=BotUi.Label("Aperçu de la classe",10,true);previewTitle.Dock=DockStyle.Top;previewTitle.Height=27;previewTitle.TextAlign=ContentAlignment.MiddleCenter;
            portrait=new CharacterPortrait { Dock=DockStyle.Fill,BackColor=paper,ForeColor=BotUi.Muted };
            var previewNote=BotUi.Label("Le portrait montre les couleurs d’origine. Vos couleurs choisies seront appliquées au personnage en jeu.",9);
            previewNote.Dock=DockStyle.Bottom;previewNote.Height=55;previewNote.ForeColor=BotUi.Muted;previewNote.TextAlign=ContentAlignment.MiddleCenter;
            var palette=new TableLayoutPanel { Dock=DockStyle.Bottom,Height=72,ColumnCount=3,RowCount=2,BackColor=paper };
            palette.RowStyles.Add(new RowStyle(SizeType.Absolute,27));palette.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            swatches=new Panel[3];colorDescriptions=new Label[3];
            for(int i=0;i<3;i++) {
                int index=i;palette.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/3));
                swatches[i]=new Panel { Dock=DockStyle.Fill,Margin=new Padding(5),BorderStyle=BorderStyle.FixedSingle,Cursor=Cursors.Hand };
                swatches[i].Click+=(s,e)=>PickColor(index);colorDescriptions[i]=BotUi.Label("Défaut",8);colorDescriptions[i].Dock=DockStyle.Fill;
                colorDescriptions[i].TextAlign=ContentAlignment.TopCenter;palette.Controls.Add(swatches[i],i,0);palette.Controls.Add(colorDescriptions[i],i,1);
                colors[i].TextChanged+=(s,e)=>UpdateColor(index);UpdateColor(i);
            }
            preview.Controls.Add(portrait);preview.Controls.Add(previewNote);preview.Controls.Add(palette);preview.Controls.Add(identity);preview.Controls.Add(previewTitle);
            var content=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=new Padding(0) };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,58));content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,42));
            content.Controls.Add(scroll,0,0);content.Controls.Add(preview,1,0);
            race.SelectedIndexChanged+=(s,e)=>UpdatePortrait();sex.SelectedIndexChanged+=(s,e)=>UpdatePortrait();UpdatePortrait();
            status=BotUi.Status("Renseignez le personnage à créer.");create=BotUi.Button("Créer le personnage",async(s,e)=>await Create(),true,190);
            BotUi.Body(card).Controls.Add(content);BotUi.Body(card).Controls.Add(status);BotUi.Body(card).Controls.Add(BotUi.Actions(create,BotUi.Button("Nom aléatoire",async(s,e)=>await RandomName(),false,150),BotUi.Button("Retour aux personnages",(s,e)=>Back(),false,220)));root.Controls.Add(card,0,1);
        }
        private void UpdatePortrait()
        {
            if(race.SelectedIndex<0||sex.SelectedIndex<0)return;
            identity.Text=classes[race.SelectedIndex]+" · "+sex.SelectedItem;
            portrait.SetAppearance((race.SelectedIndex+1)*10+sex.SelectedIndex);
        }
        private void UpdateColor(int index)
        {
            int value;bool valid=int.TryParse(colors[index].Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out value)&&value>=-1&&value<=0xFFFFFF;
            swatches[index].BackColor=!valid||value<0?BotUi.Paper:Color.FromArgb((value>>16)&255,(value>>8)&255,value&255);
            colorDescriptions[index].Text=!valid?"Valeur invalide":value<0?"Couleur "+(index+1)+"\nDéfaut":"Couleur "+(index+1)+"\n#"+value.ToString("X6");
        }
        private void PickColor(int index) { using(var dialog=new ColorDialog { FullOpen=true }) { int value;if(int.TryParse(colors[index].Text,out value)&&value>=0&&value<=0xFFFFFF)dialog.Color=Color.FromArgb((value>>16)&255,(value>>8)&255,value&255);if(dialog.ShowDialog(this)!=DialogResult.OK)return;colors[index].Text=(dialog.Color.ToArgb()&0xFFFFFF).ToString(CultureInfo.InvariantCulture); } }
        public void DisplayRandom(string value) { BotUi.OnUi(this,()=> { if(!string.IsNullOrEmpty(value))name.Text=value; }); }
        public void FailCreate() { BotUi.OnUi(this,()=> { pending=false;create.Enabled=true;status.Text="Création refusée. Vérifiez le nom, le nombre de personnages autorisé et les droits du compte."; }); }
        private async Task RandomName() { try { if(a.Connexion==null||!a.Connexion.IsConnected()) { status.Text="Connexion au serveur interrompue.";return; }await a.Connexion.SendPacket("AP",true); }catch(Exception ex) { status.Text=ex.Message; } }
        private async Task Create()
        {
            if(pending)return;
            if(!Regex.IsMatch(name.Text.Trim(),"^[A-Za-z][A-Za-z-]{2,19}$")) { status.Text="Le nom doit contenir 3 à 20 lettres ou tirets et commencer par une lettre.";return; }
            int[] values=new int[3];for(int i=0;i<3;i++)if(!int.TryParse(colors[i].Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out values[i])||values[i]<-1||values[i]>0xFFFFFF) { status.Text="La couleur "+(i+1)+" doit être comprise entre 0 et 16 777 215, ou valoir -1.";return; }
            pending=true;create.Enabled=false;status.Text="Demande de création envoyée au serveur…";
            try { a.Game.Server.ExitCreationMenu=false;a.Game.Server.NameNewCharacter=name.Text.Trim();await a.Connexion.SendPacket("AA"+name.Text.Trim()+"|"+(race.SelectedIndex+1)+"|"+sex.SelectedIndex+"|"+values[0]+"|"+values[1]+"|"+values[2],true); }
            catch(Exception ex) { pending=false;create.Enabled=true;status.Text=ex.Message; }
        }
        private void OnCharacterCreated() { if(!pending)return;BotUi.OnUi(this,()=> { pending=false;Back(); }); }
        private void Disconnected() { BotUi.OnUi(this,()=> { status.Text=a.ConnectionStatus;pending=false;create.Enabled=false; }); }
        private void Back() { transferred=true;new SelectPlayerPerso(a,true).Show();Close(); }
        public void NewBornInGame(Accounts account) { transferred=true;new GameClientFullform(account).Show();Close(); }
        private void OnSessionClosed(object sender,FormClosedEventArgs args) { a.Game.Server.RandomName-=DisplayRandom;a.Game.Server.FailCreatePerso-=FailCreate;a.Game.Server.UpdateCharacterMenu-=OnCharacterCreated;a.AccountDisconnectEvent-=Disconnected;if(!transferred)a.Dispose(); }

        private sealed class CharacterPortrait : Control
        {
            private Bitmap appearance;
            internal CharacterPortrait() { DoubleBuffered=true; }
            internal void SetAppearance(int gfx)
            {
                var previous=appearance;appearance=SelectPlayerPerso.LoadCharacterPortrait(gfx);previous?.Dispose();Invalidate();
            }
            protected override void OnPaint(PaintEventArgs args)
            {
                base.OnPaint(args);if(Width<30||Height<30)return;
                if(appearance==null) {
                    TextRenderer.DrawText(args.Graphics,"Portrait indisponible\npour cette apparence",Font,ClientRectangle,ForeColor,
                        TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.WordBreak);return;
                }
                // Socle et blason de l'écran de création du client fourni ; le personnage se tient dessus comme dans le client.
                var socle=ClientAssets.Get("socle");float socleHeight=socle==null?0:Math.Min(Height*0.34f,Width*0.5f*socle.Height/socle.Width);
                float scale=Math.Min(3f,Math.Min((Width-24f)/appearance.Width,(Height-38f-socleHeight*0.55f)/appearance.Height));
                int width=Math.Max(1,(int)(appearance.Width*scale)),height=Math.Max(1,(int)(appearance.Height*scale));
                var bounds=new Rectangle((Width-width)/2,(int)((Height-socleHeight*0.55f-height)/2),width,height);
                args.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
                if(socle!=null) {
                    float socleWidth=socleHeight*socle.Width/socle.Height;
                    // Le centre du socle (à 78 % de sa hauteur) reçoit les pieds du personnage.
                    ClientAssets.DrawFit(args.Graphics,socle,new RectangleF((Width-socleWidth)/2,bounds.Bottom-socleHeight*0.78f,socleWidth,socleHeight));
                } else using(var shadow=new SolidBrush(Color.FromArgb(38,BotUi.FrameLight)))
                    args.Graphics.FillEllipse(shadow,Width/2f-width*0.42f,bounds.Bottom-5,width*0.84f,12);
                args.Graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;args.Graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;
                args.Graphics.DrawImage(appearance,bounds);
            }
            protected override void Dispose(bool disposing) { if(disposing)appearance?.Dispose();base.Dispose(disposing); }
        }
    }
}
