using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot;
using Outil_Azur_complet.Bot.Controls;
using Outil_Azur_complet.Bot.Interfaces;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Perso.Spells;
using Tool_BotProtocol.Utils.Crypto;

internal static class BotCombatUiSmoke
{
    [DllImport("user32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr window,IntPtr context,uint flags);
    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (s,e) => {
            string path = Path.Combine(TestPaths.ApplicationBin, new AssemblyName(e.Name).Name + ".dll");
            if (!File.Exists(path)) path = Path.ChangeExtension(path,"exe");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        try { Application.EnableVisualStyles(); Run(); Console.WriteLine("OK: compact spell pages, real icon, click-to-target GA300, combat placement/ready/pass buttons, invalid turns and outside-combat gating"); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode=1; }
    }
    private static void Check(bool value,string message) { if(!value)throw new Exception(message); }
    private static object Get(object target,string field) { return target.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target); }
    private static void PumpUntil(Func<bool> done)
    {
        DateTime end=DateTime.UtcNow.AddSeconds(6);
        while(!done()) { if(DateTime.UtcNow>end)throw new TimeoutException("Combat UI loopback timed out"); Application.DoEvents();Thread.Sleep(5); }
        Application.DoEvents();
    }
    private static void Complete(Task task) { PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult(); }
    private static void Feed(Accounts account,string packet) { Complete(MessagesReception.ReceptionAsync(account.Connexion,packet)); }
    private static string Read(Socket socket)
    {
        PumpUntil(()=>socket.Available>0);
        using(var stream=new MemoryStream()) {
            byte[] one=new byte[1];
            while(true) { if(socket.Receive(one)!=1)throw new IOException("Loopback disconnected");if(one[0]==0)return Encoding.UTF8.GetString(stream.ToArray()).TrimEnd('\r','\n');stream.WriteByte(one[0]); }
        }
    }
    private static void ClickCell(UserMapControl map,short id)
    {
        var point=map.Cells[id].Centre;
        Check(map.GetCell(point)!=null&&map.GetCell(point).id==id,"Mouse projection points at another cell");
        var e=new MouseEventArgs(MouseButtons.Left,1,point.X,point.Y,0);
        typeof(UserMapControl).GetMethod("OnMouseDown",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(map,new object[]{e});
        typeof(UserMapControl).GetMethod("OnMouseUp",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(map,new object[]{e});
        Application.DoEvents();
    }
    private static void Capture(Form form,string name)
    {
        Application.DoEvents();
        using(var bitmap=new Bitmap(form.Width,form.Height)) {
            using(var graphics=Graphics.FromImage(bitmap)) {
                IntPtr context=graphics.GetHdc();
                try {Check(PrintWindow(form.Handle,context,2),"Combat UI capture failed");}
                finally {graphics.ReleaseHdc(context);}
            }
            bitmap.Save(Path.Combine(TestPaths.Work,name+".png"));
        }
    }
    private static void Run()
    {
        string previous=Environment.CurrentDirectory;
        string folder=Path.Combine(TestPaths.Work,"combat-ui");Directory.CreateDirectory(folder);Environment.CurrentDirectory=folder;
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        try {
            MessagesReception.Init(); Spell.AllSpells.Clear();
            for(short i=10;i<26;i++) { var spell=new Spell(i,"Sort test "+i);spell.GetSpellsStats(1,new SpellStats {PA=3,Min_portee=1,Max_portee=6}); }
            Map.AllBotMaps[900091]=new Map {MapID=900091,MapWidth=3,MapHeight=4,MapData=string.Concat(Enumerable.Repeat("HhGaeaaaaa",18))};
            using(var account=new Accounts(new AccountConfig("synthetic-combat-ui","synthetic","loopback"))) {
                Task<Socket> accept=listener.AcceptSocketAsync();
                Complete(account.Connexion.ConnectToServer(IPAddress.Loopback,((IPEndPoint)listener.LocalEndpoint).Port));Complete(accept);
                using(Socket peer=accept.Result) {
                    peer.ReceiveTimeout=6000; account.Game.character.SetPerso_Data(42,"Personnage de test",25,0,8);
                    account.Game.Map.SetRefreshMap("900091|date|");account.Game.character.Cell=account.Game.Map.MapCells[0];
                    for(short i=10;i<26;i++)account.Game.character.Spells[i]=Spell.ForCharacter(i,1);
                    using(var form=new GameClientFullform(account)) {
                        form.ShowInTaskbar=false;form.Opacity=0;form.Show();Application.DoEvents();
                        var slots=(List<Button>)Get(form,"quickSpells");var ids=(Dictionary<Button,short>)Get(form,"quickSpellIds");
                        var view=(MapControl)Get(form,"mapControl");var map=(UserMapControl)Get(view,"UserMap");
                        Check(slots.Count==14&&slots.All(slot=>slot.Width==25&&slot.Height==25),"Spell icons stretch with the HUD");
                        PumpUntil(()=>slots[0].GetType().GetProperty("Icon").GetValue(slots[0],null)!=null);var icon=slots[0].GetType().GetProperty("Icon").GetValue(slots[0],null) as Image;
                        Check(icon!=null&&icon.Width>30,"A real spell icon was not loaded");
                        ((Button)Get(form,"nextSpellPage")).PerformClick();Check(ids[slots[0]]==24,"Second page does not reach all learned spells");
                        ((Button)Get(form,"previousSpellPage")).PerformClick();Check(ids[slots[0]]==10,"First page lost its order");
                        slots[0].PerformClick();Check(view.SelectedSpellId==null&&peer.Available==0,"Spell cast outside combat or opened target mode");
                        Feed(account,"GJK2|1|1|0|30000|0");Feed(account,"GP"+Hash.Get_Cell_Char(0)+Hash.Get_Cell_Char(3)+"|"+Hash.Get_Cell_Char(9)+"|0");
                        ClickCell(map,3);Check(Read(peer)=="Gp3","Placement click sends movement instead of Gp");
                        Feed(account,"GIC|42;3");((Button)Get(form,"ready")).PerformClick();Check(Read(peer)=="GR1","Ready button is not functional");
                        Feed(account,"GR142");Check(account.Game.Fight.IsReady,"Ready state is predicted or not confirmed");
                        Feed(account,"GS");Feed(account,"GTM|42;0;250;6;3;0;;300");Feed(account,"GTS42|30000");
                        slots[0].PerformClick();Check(view.SelectedSpellId==10&&!((Control)Get(form,"drawer")).Visible,"Spell click still opens the large details panel");
                        ClickCell(map,3);Check(Read(peer)=="GA30010;3","Target mouse click did not cast the selected spell");
                        Check(view.SelectedSpellId==null&&account.Game.Fight.IsActionPending,"Cast target persists or action is accepted locally");
                        Check(account.Game.Fight.ActionPoints==6,"UI spends PA before server reply");
                        Feed(account,"GA;300;42;10,3,0");Feed(account,"GA;102;42;42,-3");Feed(account,"GA;102;42;42,-0");
                        Check(account.Game.Fight.ActionPoints==3&&!account.Game.Fight.IsActionPending,"Server PA and casting unlock are ignored");
                        ((Button)Get(form,"passTurn")).PerformClick();Check(Read(peer)=="Gt","Pass-turn button does not send Gt");
                        Feed(account,"GTF42");Feed(account,"GTS43|30000");slots[0].PerformClick();
                        Check(view.SelectedSpellId==null&&peer.Available==0,"Another actor's turn allows casting");
                        Feed(account,"GE0|0");Check(!((Control)Get(form,"combatTools")).Visible,"Combat actions stay visible after GE");
                        // Real supplied Astrub scenery, with explicitly synthetic actors and combat state.
                        var xml=System.Xml.Linq.XElement.Load(Path.Combine(TestPaths.ApplicationBin,"ressources","Bot","BotMaps","7411.xml"));
                        Map.AllBotMaps[7411]=new Map {MapID=7411,MapWidth=byte.Parse(xml.Element("LARGEUR").Value),MapHeight=byte.Parse(xml.Element("LONGUEUR").Value),MapData=xml.Element("MAP_DATA").Value,Back_ID=int.Parse(xml.Element("BACK").Value)};
                        account.Game.Map.SetRefreshMap("7411|date|");account.Game.character.Cell=account.Game.Map.MapCells[282];
                        Feed(account,"GM|+282;2;0;42;Personnage de test;8;80^100;0|+300;2;0;43;Autre joueur;2;20^100;0|+310;2;0;-8;100;-4;40^100;0|+299;2;20;-7;101,102;-3;1566^100,1069^100;5,6");
                        Capture(form,"bot-entites-astrub");
                        Feed(account,"GJK2|1|1|0|30000|0");Feed(account,"GS");Feed(account,"GTM|42;0;250;6;3;282;;300");Feed(account,"GTS42|30000");
                        Capture(form,"bot-combat-compact");slots[0].PerformClick();Capture(form,"bot-combat-ciblage");form.Close();
                    }
                }
            }
        }
        finally {listener.Stop();Environment.CurrentDirectory=previous;}
    }
}
