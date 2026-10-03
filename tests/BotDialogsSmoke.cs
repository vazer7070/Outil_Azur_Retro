using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot;
using Outil_Azur_complet.Bot.Interfaces;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Interactives;
using Tool_BotProtocol.Game.NPC;

// Dialogue PNJ (DC/DCK/DCE/DQ/DP/DR/DV) et zaaps (GA500;114/WC/WU/WUE/WV) sur un serveur fictif local,
// puis les volets Dialogue et Zaaps de la fenêtre de jeu. Aucun serveur réel n'est contacté.
internal static class BotDialogsSmoke
{
    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            string path = Path.Combine(TestPaths.ApplicationBin, new AssemblyName(e.Name).Name + ".dll");
            if (!File.Exists(path)) path = Path.ChangeExtension(path, "exe");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        try { Application.EnableVisualStyles(); Run(); Console.WriteLine("OK: dialogue PNJ DC/DCK/DQ/DR/DP/DV, zaaps GA500;114/WC/WU/WUE/WV, refus locaux, volets Dialogue et Zaaps"); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static object Get(object target, string field) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static void PumpUntil(Func<bool> done)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(6);
        while (!done()) { if (DateTime.UtcNow > end) throw new TimeoutException("Dialog loopback timed out"); Application.DoEvents(); Thread.Sleep(5); }
        Application.DoEvents();
    }
    private static void Complete(Task task) { PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static T Result<T>(Task<T> task) { Complete(task); return task.Result; }
    private static void Feed(Accounts account, string packet) { Complete(MessagesReception.ReceptionAsync(account.Connexion, packet)); }
    private static string Read(Socket socket)
    {
        PumpUntil(() => socket.Available > 0);
        using (var stream = new MemoryStream())
        {
            byte[] one = new byte[1];
            while (true) { if (socket.Receive(one) != 1) throw new IOException("Loopback disconnected"); if (one[0] == 0) return Encoding.UTF8.GetString(stream.ToArray()).TrimEnd('\r', '\n'); stream.WriteByte(one[0]); }
        }
    }
    private static void NoPacket(Socket socket, string message)
    {
        DateTime end = DateTime.UtcNow.AddMilliseconds(250);
        while (DateTime.UtcNow < end) { Application.DoEvents(); Thread.Sleep(5); }
        Check(socket.Available == 0, message);
    }
    private static Map SyntheticMap(int id, int x, int y) => new Map { MapID = id, MapWidth = 3, MapHeight = 4, X = x, Y = y, MapData = string.Concat(Enumerable.Repeat("HhGaeaaaaa", 18)) };

    private static void Run()
    {
        string previous = Environment.CurrentDirectory;
        string folder = Path.Combine(TestPaths.Work, "bot-dialogs"); Directory.CreateDirectory(folder); Environment.CurrentDirectory = folder;
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            MessagesReception.Init();
            Map.AllBotMaps[900081] = SyntheticMap(900081, 5, -7);
            Map.AllBotMaps[900082] = SyntheticMap(900082, 12, 3);
            Zaaps.Z[900081] = 7;
            PNJ.AllPNJ[100] = new PNJ(100, 100, null) { Name = "Marchand fictif" };
            using (var account = new Accounts(new AccountConfig("synthetic-dialogs", "synthetic", "loopback")))
            {
                Task<Socket> accept = listener.AcceptSocketAsync();
                Complete(account.Connexion.ConnectToServer(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port)); Complete(accept);
                using (Socket peer = accept.Result)
                {
                    peer.ReceiveTimeout = 6000;
                    account.Game.character.SetPerso_Data(42, "Personnage de test", 25, 0, 8);
                    account.Game.character.Kamas = 500;
                    Feed(account, "GDM|900081|date|key"); Check(Read(peer) == "GI", "GDM did not request GI");
                    Feed(account, "GM|+0;1;0;42;Personnage de test;1;100^100;0|+5;1;0;-8;100;-4;1^100;0");
                    Check(account.Game.Map.NPC_List().Count == 1 && account.Game.Map.NPC_List()[0].Name == "Marchand fictif", "NPC from GM lost its BotNPCs name");
                    account.AccountStates = AccountStates.CONNECTED_INACTIVE;

                    // Dialogue PNJ, formats de dofus.aks.Dialog
                    NpcDialog npc = account.Game.Interactions.Npc;
                    Check(!npc.IsOpen && !Result(npc.AnswerAsync(1)).Sent && !Result(npc.LeaveAsync()).Sent, "Dialog actions were accepted before DCK");
                    NoPacket(peer, "Closed dialog sent packets");
                    InteractionResult open = Result(npc.OpenAsync(-8));
                    Check(open.Sent && Read(peer) == "DC-8", "Talking to an NPC does not send DC<npc>");
                    Check(!npc.IsOpen, "Dialog opened locally before DCK");
                    Feed(account, "DCK-8");
                    Check(npc.IsOpen && npc.NpcId == -8 && npc.NpcName == "Marchand fictif" && account.AccountStates == AccountStates.DIALOG, "DCK did not open the dialog");
                    Check(!Result(npc.OpenAsync(-8)).Sent, "Second dialog allowed while one is open");
                    Feed(account, "DQ123;5,Astrub|45;46");
                    Check(npc.QuestionId == 123 && npc.Parameters.SequenceEqual(new[] { "5", "Astrub" }) && npc.AnswerIds.SequenceEqual(new[] { 45, 46 }),
                        "DQ question/parameters/answers were not read like Dialog.onQuestion");
                    Check(!Result(npc.AnswerAsync(99)).Sent, "Answer outside the proposed list was sent");
                    NoPacket(peer, "Refused answer reached the server");
                    Check(Result(npc.AnswerAsync(46)).Sent && Read(peer) == "DR123|46", "Answer does not send DR<question>|<answer>");
                    Feed(account, "DP"); Check(npc.IsPaused, "DP did not pause the dialog");
                    Feed(account, "DQ124|"); Check(npc.QuestionId == 124 && npc.AnswerIds.Length == 0 && !npc.IsPaused, "Question without answers was rejected");
                    Feed(account, "DQbad|1"); Check(npc.QuestionId == 124 && npc.LastMessage.Contains("illisible"), "Unreadable DQ replaced the current question");
                    Check(Result(npc.LeaveAsync()).Sent && Read(peer) == "DV", "Leaving does not send DV");
                    Check(npc.IsOpen, "Dialog closed locally before the server DV");
                    Feed(account, "DV");
                    Check(!npc.IsOpen && npc.QuestionId == -1 && npc.AnswerIds.Length == 0 && account.AccountStates == AccountStates.CONNECTED_INACTIVE, "DV did not close the dialog");
                    Feed(account, "DCE"); Check(!npc.IsOpen && npc.LastMessage.Contains("refusé"), "DCE was not reported");
                    Feed(account, "DQ200|1"); Check(npc.IsOpen && account.AccountStates == AccountStates.DIALOG, "Standalone DQ (collector) did not open a dialog");
                    Feed(account, "DV"); Check(!npc.IsOpen, "DV after standalone DQ did not close");
                    account.AccountStates = AccountStates.FIGHTING;
                    Check(!Result(npc.OpenAsync(-8)).Sent, "Dialog allowed during a fight");
                    account.AccountStates = AccountStates.MOVING;
                    Check(!Result(npc.OpenAsync(-8)).Sent, "Dialog allowed during a movement");
                    account.AccountStates = AccountStates.CONNECTED_INACTIVE;
                    NoPacket(peer, "Refused dialog openings reached the server");

                    // Zaaps, formats de dofus.aks.Waypoints
                    ZaapDialog zaap = account.Game.Interactions.Zaap;
                    Check(zaap.IsZaapCell(7) && !zaap.IsZaapCell(6), "BotZaaps cell is not recognised");
                    Check(!Result(zaap.OpenAsync(6)).Sent && !Result(zaap.TeleportAsync(900082)).Sent, "Zaap actions accepted without a zaap");
                    Check(Result(zaap.OpenAsync(7)).Sent && Read(peer) == "GA5007;114", "Using the zaap does not send GA500<cell>;114");
                    Check(!zaap.IsOpen, "Zaap list opened before WC");
                    Feed(account, "WC900081|900081;0|900082;120|900083;9000|bad|1;x");
                    Check(zaap.IsOpen && account.AccountStates == AccountStates.ZAAP && zaap.SavedMapId == 900081 && zaap.Destinations.Count == 3, "WC list was not read like Waypoints.onCreate");
                    Check(zaap.Destinations[0].IsCurrent && zaap.Destinations[0].IsSaved && zaap.Destinations[0].Cost == 0, "Current/saved zaap flags are wrong");
                    Check(zaap.Destinations[1].Label == "[12,3] · carte 900082" && zaap.Destinations[2].Label == "Carte 900083", "Destination labels ignore BotMaps coordinates");
                    Check(!Result(zaap.TeleportAsync(900081)).Sent, "Teleport to the current map was sent");
                    Check(!Result(zaap.TeleportAsync(900083)).Sent, "Teleport above the character's kamas was sent");
                    Check(!Result(zaap.TeleportAsync(777)).Sent, "Teleport to an unlisted map was sent");
                    NoPacket(peer, "Refused teleports reached the server");
                    Check(Result(zaap.TeleportAsync(900082)).Sent && Read(peer) == "WU900082", "Teleport does not send WU<map>");
                    Feed(account, "WUE"); Check(zaap.IsOpen && zaap.LastMessage.Contains("refuse"), "WUE closed the window or was silent");
                    Check(Result(zaap.LeaveAsync()).Sent && Read(peer) == "WV", "Leaving does not send WV");
                    Feed(account, "WV");
                    Check(!zaap.IsOpen && zaap.Destinations.Count == 0 && account.AccountStates == AccountStates.CONNECTED_INACTIVE, "WV did not close the zaap window");
                    Feed(account, "WCbad"); Check(!zaap.IsOpen && zaap.LastMessage.Contains("illisible"), "Unreadable WC opened the window");

                    // Clics sur la carte et volets de la fenêtre de jeu
                    using (var form = new GameClientFullform(account))
                    {
                        form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); Application.DoEvents();
                        var view = (MapControl)Get(form, "mapControl"); Check(view != null, "Map view missing for a loaded map");
                        var drawer = (Control)Get(form, "drawer"); var panels = (TabControl)Get(form, "panels");
                        Check(panels.TabPages[GameClientFullform.DialogPanel].Text == "Dialogue" && panels.TabPages[GameClientFullform.ZaapPanel].Text == "Zaaps"
                            && panels.TabPages[GameClientFullform.ShopPanel].Text == "Boutique", "Interaction tabs are missing or misnumbered");
                        Complete(view.HandleCellActionAsync(5)); Check(Read(peer) == "DC-8", "Clicking an NPC cell does not send DC");
                        Check(!drawer.Visible, "Dialog panel opened before the server answer");
                        Feed(account, "DCK-8"); Feed(account, "DQ300;x|7;8"); Application.DoEvents();
                        Check(drawer.Visible && panels.SelectedIndex == GameClientFullform.DialogPanel, "Dialog panel did not open on DCK/DQ");
                        Check(((Label)Get(form, "dialogTitle")).Text.Contains("Marchand fictif") && ((Label)Get(form, "dialogQuestion")).Text.Contains("Question n° 300"), "Dialog panel does not show NPC and question number");
                        var answers = (FlowLayoutPanel)Get(form, "dialogAnswers");
                        Check(answers.Controls.Count == 2 && answers.Controls[1].Text == "Réponse n° 8" && answers.Controls[1].Enabled, "Answer buttons differ from DQ");
                        ((Button)answers.Controls[1]).PerformClick(); Check(Read(peer) == "DR300|8", "Answer button does not send DR");
                        ((Button)Get(form, "dialogLeave")).PerformClick(); Check(Read(peer) == "DV", "Quit button does not send DV");
                        Feed(account, "DV"); Application.DoEvents();
                        Check(!drawer.Visible && answers.Controls.Count == 0, "Dialog panel stayed open after DV");
                        Complete(view.HandleCellActionAsync(7)); Check(Read(peer) == "GA5007;114", "Clicking the zaap cell does not use the zaap");
                        Feed(account, "WC900081|900081;0|900082;120"); Application.DoEvents();
                        Check(drawer.Visible && panels.SelectedIndex == GameClientFullform.ZaapPanel, "Zaap panel did not open on WC");
                        var list = (ListView)Get(form, "zaapList");
                        Check(list.Items.Count == 2 && list.Items[1].Text.StartsWith("[12,3]") && list.Items[1].SubItems[1].Text == "120 kamas", "Zaap list differs from WC");
                        var teleport = (Control)Get(form, "zaapTeleport"); Check(!teleport.Enabled, "Teleport enabled without a selection");
                        list.Items[0].Selected = true; Application.DoEvents(); Check(!teleport.Enabled, "Teleport enabled for the current map");
                        list.Items[0].Selected = false; list.Items[1].Selected = true; Application.DoEvents(); Check(teleport.Enabled, "Teleport disabled for an affordable destination");
                        ((Button)teleport).PerformClick(); Check(Read(peer) == "WU900082", "Teleport button does not send WU");
                        ((Button)Get(form, "zaapLeave")).PerformClick(); Check(Read(peer) == "WV", "Close button does not send WV");
                        Feed(account, "WV"); Application.DoEvents();
                        Check(!drawer.Visible && !teleport.Enabled && list.Items.Count == 0, "Zaap panel stayed open after WV");
                        Complete(view.HandleCellActionAsync(3)); Check(Read(peer).StartsWith("GA001"), "Ordinary cell click no longer moves");
                        Feed(account, "GA;0"); Check(account.AccountStates == AccountStates.CONNECTED_INACTIVE, "Cancelled movement kept the busy state");
                        form.Close();
                    }
                }
            }
        }
        finally { listener.Stop(); Environment.CurrentDirectory = previous; }
    }
}
