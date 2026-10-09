using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Outil_Azur_complet.Bot;
using Outil_Azur_complet.Bot.Panels;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Perso.Spells;
using Tool_BotProtocol.Utils.Crypto;

internal static class BotUiSmoke
{
    [DllImport("user32.dll", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr window, IntPtr deviceContext, uint flags);
    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=> {
            string file=Path.Combine(TestPaths.ApplicationBin,new AssemblyName(e.Name).Name+".dll");
            if(!File.Exists(file))file=Path.ChangeExtension(file,"exe");return File.Exists(file)?Assembly.LoadFrom(file):null;
        };
        try { Application.EnableVisualStyles();Run();Console.WriteLine("OK: bot UI renders offline, gates actions, masks credentials and bounds packet journal"); }
        catch(Exception error) { Console.Error.WriteLine(error);Environment.ExitCode=1; }
    }
    private static void Check(bool ok,string message) { if(!ok)throw new Exception(message); }
    private static object Get(object target,string field) { return target.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target); }
    private static IEnumerable<Control> All(Control value) { yield return value;foreach(Control child in value.Controls)foreach(var nested in All(child))yield return nested; }
    private static void Layout(Control value) { value.PerformLayout();foreach(Control child in value.Controls)Layout(child); }
    // Cellule client sur dix caractères, codée comme la lisent Map.DecompressCell et BotMapArtwork : active, niveau 7, à plat,
    // avec un sol, un objet au sol (object1) et un objet en relief (object2) ; une cellule infranchissable bloque aussi la vue.
    private static string EncodedCell(int ground,int object1,int object2,bool walkable)
    {
        int[] value=new int[10];
        value[0]=32|(walkable?1:0)|((ground>>6)&24)|((object1>>11)&4)|((object2>>12)&2);
        value[1]=7;value[2]=(walkable?32:0)|((ground>>6)&7);value[3]=ground&63;
        value[4]=4|((object1>>12)&1);value[5]=(object1>>6)&63;value[6]=object1&63;
        value[7]=(object2>>12)&1;value[8]=(object2>>6)&63;value[9]=object2&63;
        return new string(value.Select(part=>Hash.caracteres_array[part]).ToArray());
    }
    // Carte 7411 synthétique de 15 × 17 (479 cellules), sans aucune donnée de carte du serveur ni du client : herbe (sol 39),
    // chemin pavé (sol 6) sur les lignes 18 à 21 où se tient le personnage (282), touffes et fleurs (objets 374, 528, 543),
    // arbres et menhirs infranchissables placés hors du chemin pour ne pas le masquer (objets 42, 57, 58, 59) et fond 114.
    // Ces visuels viennent du décor versionné (Resources/Bot/Decor) que le projet copie dans ressources/maps.
    private static Map SyntheticAstrub()
    {
        const int width=15,height=17,count=height*(2*width-1)-(width-1);
        int[] tufts={33,64,120,141,205,352,397,441},obstacles={46,98,149,198,230,364,419,450};
        int[] flats={374,528,543},reliefs={42,57,58,59};
        string data=string.Concat(Enumerable.Range(0,count).Select(id=>{
            int row=2*(id/(2*width-1))+(id%(2*width-1)<width?0:1),tuft=Array.IndexOf(tufts,id),obstacle=Array.IndexOf(obstacles,id);
            return EncodedCell(row>=18&&row<=21?6:39,tuft<0?0:flats[tuft%flats.Length],obstacle<0?0:reliefs[obstacle%reliefs.Length],obstacle<0);
        }));
        return new Map { MapID=7411,MapWidth=width,MapHeight=height,X=4,Y=-18,Back_ID=114,MapData=data };
    }
    private static void Render(Form form,string file,bool minimum=false)
    {
        form.ShowInTaskbar=false;form.Opacity=0;form.Show();if(minimum)form.Size=form.MinimumSize;Layout(form);Application.DoEvents();
        using(var bitmap=new Bitmap(form.Width,form.Height))
        {
            // DrawToBitmap reverses child z-order and hides the drawer behind the map.
            // Ask the native window to render its actual composition, without showing it on screen.
            using(var graphics=Graphics.FromImage(bitmap)) {
                IntPtr context=graphics.GetHdc();
                try { Check(PrintWindow(form.Handle,context,2),"Native bot UI capture failed: "+file); }
                finally { graphics.ReleaseHdc(context); }
            }
            bitmap.Save(Path.Combine(TestPaths.Work,file+".png"));
        }
        foreach(var button in All(form).OfType<Button>().Where(x=>x.Visible))
            Check((string)button.Tag=="client-icon" ? button.Width>=24&&button.Height>=24 : button.Width>=85&&button.Height>=30,"Bot button cannot be used comfortably: "+button.Text);
    }
    private static void Run()
    {
        string previous=Environment.CurrentDirectory;string work=Path.Combine(TestPaths.Work,"bot-ui");Directory.CreateDirectory(work);Environment.CurrentDirectory=work;
        try
        {
            AccountConfig.AccountsDico.Clear();GlobalConfig.InitializeConfig();
            var config=new AccountConfig("compte-fictif","secret de test","Privé");
            using(var login=new LoginForm())
            {
                Check(((TextBox)Get(login,"password")).UseSystemPasswordChar,"Password is visible");
                ((TextBox)Get(login,"password")).Text=" espace ";
                Check(((TextBox)Get(login,"password")).Text==" espace ","UI trimmed password");
                Render(login,"bot-connexion");var tabs=All(login).OfType<TabControl>().First();tabs.SelectedIndex=1;Render(login,"bot-options",true);login.Close();
            }
            string configPath=Path.Combine("ressources","Bot","BotConfig.json");File.WriteAllText(configPath,"{broken");
            using(var broken=new LoginForm()) { Check(((Label)Get(broken,"status")).Text.Contains("Configuration"),"Invalid config was silently ignored");Render(broken,"bot-options-invalides");broken.Close(); }
            Check(File.ReadAllText(configPath)=="{broken","UI silently replaced invalid configuration");GlobalConfig.writenewconfig("127.0.0.1","450","5555","1.34.1","2528660","2362079");
            using(var servers=new PersoSelection(config,false)) { Render(servers,"bot-serveurs");Check(!((Control)Get(servers,"connect")).Enabled,"Server selection enabled before login");servers.Close(); }
            using(var account=new Accounts(config))
            using(var selection=new SelectPlayerPerso(account))
            {
                Render(selection,"bot-personnages-attente");Check(!((Control)Get(selection,"create")).Enabled,"Creation enabled before ALK");Check(((Label)Get(selection,"status")).Text.Contains("attente"),"UI claims no characters before server reply");
                account.SecretQuestion="Question fictive de test";account.AboTime=30L*86400000L;
                int[] appearances={10,21,30,41,80,90,101,121};
                for(int i=1;i<=8;i++)account.AccountCharactersInfo[i]="Personnage-"+i+"|"+(i*10)+"|"+appearances[i-1]+"|";
                account.Game.Server.AddCharacterMenu();Application.DoEvents();Check(((ListView)Get(selection,"characters")).Items.Count==8,"Character UI is still limited to five");Check(((TextBox)Get(selection,"secretAnswer")).UseSystemPasswordChar,"Secret answer is visible");Check(((Label)Get(selection,"secretQuestion")).Text.Contains("Question fictive"),"Secret question is missing");
                Render(selection,"bot-personnages",true);
                object scene=Get(selection,"scene");var flags=BindingFlags.NonPublic|BindingFlags.Instance;
                Check((int)scene.GetType().GetProperty("PageCount",flags).GetValue(scene,null)==2,"Eight characters do not have two podium pages");
                scene.GetType().GetMethod("NextPage",flags).Invoke(scene,null);Application.DoEvents();
                Check((int)scene.GetType().GetProperty("CurrentPage",flags).GetValue(scene,null)==1,"Character pagination is not functional");
                Check(((int[])scene.GetType().GetProperty("VisibleCharacterIds",flags).GetValue(scene,null)).Length==3,"Second character page loses characters");
                var rows=(ListView)Get(selection,"characters");Check(rows.SelectedItems.Count==1&&(int)rows.SelectedItems[0].Tag==6,"Podium page did not select a stable character ID");
                account.Game.Server.AddCharacterMenu();Application.DoEvents();Check(rows.SelectedItems.Count==1&&(int)rows.SelectedItems[0].Tag==6,"Character refresh resets the selected ID");
                Render(selection,"bot-personnages-page-2",true);selection.Close();
            }
            using(var account=new Accounts(config))
            using(var creation=new CreateCharacter(account)) { Render(creation,"bot-creation");var colors=(TextBox[])Get(creation,"colors");Check(colors.All(x=>x.Text=="-1"),"Creation defaults alter colors");creation.Close(); }
            using(var account=new Accounts(config))
            using(var packets=new FluxForm(account))
            {
                Render(packets,"bot-journal");packets.PacketSent(config.Password);packets.PacketSent("AT123456");packets.PacketSent("AD34|reponse%20secrete");
                for(int i=0;i<400;i++)packets.PacketRecu("GDM|"+i+"|date|");Application.DoEvents();
                Check(packets.DebugMessages.Count==300,"Packet journal is unbounded");packets.PacketSent(config.Password);packets.PacketSent("AT123456");packets.PacketSent("AD34|reponse%20secrete");Application.DoEvents();
                Check(!packets.DebugMessages.Any(x=>x.Contains(config.Password)||x.Contains("123456")||x.Contains("reponse%20secrete")),"Journal leaked authentication data");Render(packets,"bot-journal-rempli",true);packets.Close();
            }
            using(var account=new Accounts(config))
            {
                var reference=Tool_Editor.maps.managers.MapSwfSerializer.Load(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),"..","Fixtures","10000_0612041200.swf"));
                var map=new Map { MapID=reference.ID,MapWidth=checked((byte)reference.Width),MapHeight=checked((byte)reference.Height),Back_ID=reference.BackGroundID,MapData=reference.MapData,X=-2,Y=4 };
                Map.AllBotMaps[map.MapID]=map;account.Game.Map.SetRefreshMap(map.MapID+"|date|");account.Game.character.SetPerso_Data(7,"Personnage fictif",25,0,8);account.Game.character.Cell=account.Game.Map.MapCells[200];
                account.Game.character.stats.MinExpNiv=100;account.Game.character.stats.ActualEXP=150;account.Game.character.stats.ExpNivNext=200;
                account.Game.character.stats.VitalityActual=250;account.Game.character.stats.MaxVitality=300;account.Game.character.Kamas=2500;
                Spell.AllSpells.Clear();var template=new Spell(10,"Sort fictif");template.GetSpellsStats(1,new SpellStats { PA=3 });template.GetSpellsStats(2,new SpellStats { PA=4 });account.Game.character.Spells[10]=template.CopyForCharacter(1);account.Game.character.SpellPoints=5;
                using(var dashboard=new GameClientFullform(account))
                {
                    Render(dashboard,"bot-session");Check(Math.Abs(((Outil_Azur_complet.Bot.Controls.Banner.CircleGauge)Get(dashboard,"xp")).Value-50)<0.5,"XP uses total XP instead of current level progress");Render(dashboard,"bot-session-minimum",true);Check(((Label)Get(dashboard,"state")).Height>=22,"Session status is clipped");
                    var area=(Panel)Get(dashboard,"mapArea");Check(area.Width>=dashboard.ClientSize.Width*0.95&&area.Height>=dashboard.ClientSize.Height*0.70,"Map is still a small navigation preview");Check(!((Control)Get(dashboard,"drawer")).Visible,"Character panel permanently covers map");
                    dashboard.Panels.Show(dashboard.Panels.Get<StatsPanel>());Check(((Control)Get(dashboard,"drawer")).Visible,"Stats panel failed to open");Render(dashboard,"bot-personnage-stats",true);
                    var spellPanel=dashboard.Panels.Get<SpellsPanel>();dashboard.Panels.Show(spellPanel);var spellList=(ListView)Get(spellPanel,"spells");Check(spellList.Items.Count==1&&spellList.Items[0].Text=="Sort fictif","Learned spell is absent from UI");spellList.Items[0].Selected=true;Check(!((Control)Get(spellPanel,"upgradeSpell")).Enabled,"Spell upgrade enabled without connection");Render(dashboard,"bot-sorts",true);
                    account.Game.character.Spells[10]=template.CopyForCharacter(6);account.Game.character.SpellsRefreshEvent();Application.DoEvents();Check(spellList.SelectedItems.Count==1&&spellList.SelectedItems[0].SubItems[1].Text=="6","Spell update lost the selection or level");Check(((Label)Get(spellPanel,"spellHelp")).Text.Contains("maximal"),"Maximal spell can still be upgraded");dashboard.Panels.CloseAll();Check(!((Control)Get(dashboard,"drawer")).Visible,"Drawer cannot be closed");
                    Map.AllBotMaps[7411]=SyntheticAstrub();
                    account.Game.Map.SetRefreshMap("7411|date|");account.Game.character.Cell=account.Game.Map.MapCells[282];
                    var mapView=(Outil_Azur_complet.Bot.Interfaces.MapControl)Get(dashboard,"mapControl");for(int wait=0;wait<200&&mapView.ArtworkStatus.StartsWith("Chargement du décor");wait++){Application.DoEvents();System.Threading.Thread.Sleep(50);}Check(mapView.ArtworkStatus.Contains("Décor chargé"),"Bundled map scenery is unavailable from the application directory");
                    Render(dashboard,"bot-astrub",true);mapView.ZoomIn();Render(dashboard,"bot-astrub-zoom",true);mapView.Fit();
                    var chat=(Outil_Azur_complet.Bot.Controls.Chat.ChatPanel)Get(dashboard,"chatPanel");chat.Prefill("/w Destinataire ");Render(dashboard,"bot-discussion-privee",true);Check(chat.Input.Width>=130&&chat.Input.Text=="/w Destinataire ","Private message entry is unusable");dashboard.Close();
                }
            }
        }
        finally { Environment.CurrentDirectory=previous; }
    }
}
