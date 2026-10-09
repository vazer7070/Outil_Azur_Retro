using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
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
using Outil_Azur_complet.Bot.Controls;
using Outil_Azur_complet.Bot.Controls.Banner;
using Outil_Azur_complet.Bot.Interfaces;
using Outil_Azur_complet.Bot.Menus;
using Outil_Azur_complet.Bot.Panels;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Exchanges;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Maps.Interfaces;
using Tool_BotProtocol.Game.Montures;
using Tool_BotProtocol.Game.Perso.Inventory;

// Montures, étable et enclos (lot F7) selon dofus.aks.Mount / Exchange (client 1.34) et GameClient / Mount / SocketManager de
// StarLoco, sur un serveur fictif local avec des textes du client synthétiques (lang, rides) : fiche Re+ (champs « : », modèle RI,
// capacités RIA, fiches illisibles rejetées), Rr et ses refus (pas de monture, niveau 60), Rr± , Rn et les noms refusés, Rx 0 à 90
// (Rx95 refusé localement), Rc, Rf (refusé sacoches pleines), ER15| puis ECK15|<id> + EL + Ew → sacoches (EMO±, EsKO±, EV), Re- / ReE,
// action PNJ 8 (ER18) jamais émise, Rd → fiche consultée, Rp<id> (identifiants négatifs de StarLoco), certificat → Rd<p1>|<p2>,
// Rp<enclos> → InteractiveActions.SetMountPark (oublié sur une autre carte), objets d'élevage GDO → Ro<cellule>, RD / Rs / Rb / Rv,
// ECK16 + Ee / Ef → étable et enclos (Erg, Erp, Erc, ErC, Efp, Efg enchaînés comme MountStorage), paquets mal formés sans exception ;
// puis la jauge « XP Monture », les volets Monture (sacoches, fiche Rd, boîtes Castrer) et Enclos (étable, vente, achat) dans une
// fenêtre de jeu invisible, le menu d'une monture d'enclos (acteur -9, droit de guilde, Maj + clic) et celui d'un objet d'élevage.
internal static class BotMountSmoke
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private const int MapId = 900121, OtherMapId = 900122, Width = 6, Height = 6;
    private const short SelfCell = 30, ParkMountCell = 20, TroughCell = 24, PlainObjectCell = 26;
    private const int ManageOtherMounts = 16384;
    private const string Plain = "HhGaeaaaaa";
    private const string SelfGm = "+30;1;0;42;Personnage de test;1;10^100;0;1,0,0,32";
    private const string ParkMountGm = "+20;1;0;-6;Rapide;-9;7002^100;Proprio fictif;5;18";
    private const string Equipped = "-12:18:?,?,?,?,?,?,?,?,?,?,?,?,?,?:,,9:Fulgurante:1:1500,1000,2000:5:1:250:0:2000,10000:1000,1000:5000,7500:-2500,-10000,10000:3000,10000:-1:0::10,240:2,20:";
    private const string ShedOne = "-13:18:?:,,0:Brise:0:0,0,500:1:0:100:0:0,10000:0,1000:1000,7500:0,-10000,10000:0,10000:-1:0::0,240:-1,20:";
    private const string ShedTwo = "-14:18:?:,,0::1:600,500,1000:2:1:150:0:0,10000:1000,1000:7500,7500:100,-10000,10000:0,10000:3:0::0,240:20,20:";
    private const string ParkMount = "-6:18:?:,,0:Rapide:0:0,0,500:5:1:100:0:0,10000:1000,1000:1000,7500:0,-10000,10000:0,10000:-1:0::0,240:0,20:";
    private const string NewBorn = "-15:18:?:,,0::0:0,0,100:1:0:50:0:0,10000:0,1000:0,7500:0,-10000,10000:0,10000:-1:0::0,240:0,20:";
    private const string OwnPark = "Rp42;0;5;3;Guilde fictive;1a,2b,3c,4d";
    private const string OtherPark = "Rp77;8000;5;3;Autre guilde;1,2,3,4";
    private static readonly List<Exception> uiErrors = new List<Exception>();

    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            string path = Path.Combine(TestPaths.ApplicationBin, new AssemblyName(e.Name).Name + ".dll");
            if (!File.Exists(path)) path = Path.ChangeExtension(path, "exe");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        Application.ThreadException += (s, e) => uiErrors.Add(e.Exception);
        try
        {
            Application.EnableVisualStyles();
            Run();
            Check(uiErrors.Count == 0, "Interface errors: " + string.Join(" / ", uiErrors.Select(e => e.GetType().Name + " " + e.Message)));
            Console.WriteLine("OK: Re±/ReE, Rr/Rr±, Rn, Rx (Rx95 refusé), Rc, Rf, ER15|/ECK15/EL/Ew/EMO±/EsKO±/EV, ER18 jamais émis, Rd, Rp<id>, certificat Rd<p1>|<p2>, Rp → SetMountPark, Ro, RD/Rs/Rb/Rv, ECK16/Ee/Ef/Er*/Ef*, jauge XP Monture, volets Monture et Enclos, menus -9 et objet d'élevage");
        }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
        finally { LangData.Clear(); }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Check(bool value, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0) { if (!value) throw new Exception("Check failed at line " + line); }
    private static object Get(object target, string field) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static IEnumerable<Control> All(Control value) { yield return value; foreach (Control child in value.Controls) foreach (var nested in All(child)) yield return nested; }
    private static void PumpUntil(Func<bool> done, int seconds = 6, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(seconds);
        while (!done()) { if (DateTime.UtcNow > end) throw new TimeoutException("Mount loopback timed out (line " + line + ")"); Application.DoEvents(); Thread.Sleep(5); }
        Application.DoEvents();
    }
    private static void Complete(Task task) { PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static T Result<T>(Task<T> task) { Complete(task); return task.Result; }
    private static void Feed(Accounts account, string packet) { Complete(MessagesReception.ReceptionAsync(account.Connexion, packet)); }
    /// <summary>Paquet traité sur un fil du pool, comme la boucle de réception réseau.</summary>
    private static void FeedFromNetwork(Accounts account, string packet) { Complete(Task.Run(() => MessagesReception.ReceptionAsync(account.Connexion, packet))); }
    private static bool Sent(Task<InteractionResult> task) => Result(task).Sent;
    private static string Read(Socket socket)
    {
        PumpUntil(() => socket.Available > 0);
        using (var stream = new MemoryStream())
        {
            byte[] one = new byte[1];
            while (true) { if (socket.Receive(one) != 1) throw new IOException("Loopback disconnected"); if (one[0] == 0) return Encoding.UTF8.GetString(stream.ToArray()).TrimEnd('\r', '\n'); stream.WriteByte(one[0]); }
        }
    }
    private static void Expect(Socket socket, string packet, string message)
    {
        string read = Read(socket);
        Check(read == packet, message + " (reçu « " + read + " », attendu « " + packet + " »)");
    }
    private static void NoPacket(Socket socket, string message)
    {
        DateTime end = DateTime.UtcNow.AddMilliseconds(250);
        while (DateTime.UtcNow < end) { Application.DoEvents(); Thread.Sleep(5); }
        Check(socket.Available == 0, message + " (reçu « " + (socket.Available > 0 ? Read(socket) : string.Empty) + " »)");
    }
    private static ListViewItem RowWithTag(ListView list, object tag) => list.Items.Cast<ListViewItem>().FirstOrDefault(row => Equals(row.Tag, tag));
    private static void Click(object button) => ((Button)button).PerformClick();
    private static void Answer(Form dialog, string choice) => All(dialog).OfType<Button>().First(button => button.Text == choice).PerformClick();
    private static string DialogMessage(Form dialog) => All(dialog).FirstOrDefault(control => control.Name == "dialog-message")?.Text ?? string.Empty;
    private static IEnumerable<string> Texts(ToolStrip strip) => strip.Items.Cast<ToolStripItem>().Select(item => item.Text);
    private static ToolStripItem Item(ToolStrip strip, string text) => strip.Items.Cast<ToolStripItem>().FirstOrDefault(item => item.Text == text);
    private static string Base36(long value)
    {
        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        if (value == 0) return "0";
        var text = new StringBuilder();
        while (value > 0) { text.Insert(0, digits[(int)(value % 36)]); value /= 36; }
        return text.ToString();
    }
    private static string GuildStats(int rights) => "gSGuilde fictive|3|0|5|0|" + Base36(rights);
    private static void WriteLang(string folder, string file, string family, string body) =>
        File.WriteAllText(Path.Combine(folder, file), "<?xml version='1.0' encoding='utf-8'?>\n<BotLang famille=\"" + family + "\" langue=\"fr\" version=\"1\" source=\"" + family
            + "_fr_1.swf\">\n" + body + "\n</BotLang>\n", new UTF8Encoding(false));
    private static void Png(string folder, string name, Color color, int width, int height)
    {
        Directory.CreateDirectory(folder);
        using (var image = new Bitmap(width, height)) { using (var graphics = Graphics.FromImage(image)) graphics.Clear(color); image.Save(Path.Combine(folder, name + ".png"), ImageFormat.Png); }
    }
    private static Map PlainMap(int id, int x, int y) =>
        new Map { MapID = id, MapWidth = Width, MapHeight = Height, X = x, Y = y, MapData = string.Concat(Enumerable.Repeat(Plain, (2 * Width - 1) * Height)) };

    // ClientAssets est interne à l'application : sa racine (dossier temporaire du test) et son cache passent par la réflexion.
    private static Type Assets => typeof(GameClientFullform).Assembly.GetType("Outil_Azur_complet.Bot.ClientAssets");
    private static bool Cached(string family, string name)
    {
        var args = new object[] { family, name, null };
        return (bool)Assets.GetMethod("TryCached", Any).Invoke(null, args) && args[2] != null;
    }

    private static void Run()
    {
        string previous = Environment.CurrentDirectory;
        string folder = Path.Combine(TestPaths.Work, "bot-mount-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); Environment.CurrentDirectory = folder;
        string lang = Path.Combine(folder, "lang"); Directory.CreateDirectory(lang);
        WriteLang(lang, "rides.xml", "rides",
            "<entree table=\"RI\" id=\"18\" c1=\"16747520\" c2=\"-1\" c3=\"255\" g=\"7002\" n=\"Dragodinde fictive\" />\n"
            + "<entree table=\"RIA\" id=\"9\" n=\"Caméléone fictive\" d=\"Change de couleur (test).\" e=\"\" />");
        WriteLang(lang, "lang.xml", "lang",
            "<texte cle=\"MOUNT\" valeur=\"Monture\" />\n<texte cle=\"WORD_XP\" valeur=\"XP\" />\n<texte cle=\"NO_NAME\" valeur=\"Sans nom (test)\" />\n"
            + "<texte cle=\"MOUNT_NO_EQUIP\" valeur=\"Aucune monture équipée (test).\" />\n<texte cle=\"MOUNT_ERROR_ALREADY_HAVE_ONE\" valeur=\"Déjà une monture (test).\" />\n"
            + "<texte cle=\"MOUNT_OF\" valeur=\"Monture de %1 (test)\" />\n<texte cle=\"VIEW_MOUNT_DETAILS\" valeur=\"Consulter la fiche (test)\" />\n"
            + "<texte cle=\"REMOVE\" valeur=\"Retirer (test)\" />\n<texte cle=\"DO_U_CASTRATE_YOUR_MOUNT\" valeur=\"Castrer la monture ? (test)\" />\n"
            + "<texte cle=\"DO_U_KILL_YOUR_MOUNT\" valeur=\"Libérer la monture ? (test)\" />\n<texte cle=\"DO_U_BUY_MOUNTPARK\" valeur=\"Acheter cet enclos pour %1 kamas ? (test)\" />\n"
            + "<texte cle=\"NOT_ENOUGH_RICH\" valeur=\"Pas assez de kamas (test).\" />\n<texte cle=\"ANIMAL_WOMEN\" valeur=\"Femelle\" />\n<texte cle=\"ANIMAL_MEN\" valeur=\"Mâle\" />\n"
            + "<texte cle=\"CASTRATED\" valeur=\"Castrée (test)\" />\n<texte cle=\"MOUNTPARK_PRIVATE\" valeur=\"Enclos privé (test)\" />");
        Check(LangData.Load(lang) == 2, "Synthetic lang files not loaded: " + string.Join(" / ", LangData.LoadWarnings));
        Model();

        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var logs = new ConcurrentQueue<string>();
        try
        {
            MessagesReception.Init();
            Check(ExchangeRegistry.IsRegistered(ExchangeTypes.MountStorage) && ExchangeRegistry.IsRegistered(ExchangeTypes.Shed) && ExchangeTypes.MountStorage == 15 && ExchangeTypes.Shed == 16,
                "Exchange types 15 and 16 are not registered");
            Map.AllBotMaps[MapId] = PlainMap(MapId, 3, -5);
            Map.AllBotMaps[OtherMapId] = PlainMap(OtherMapId, 4, -5);
            InventoryObjects.FullInventory[2001] = new InventoryObjects { ID = 2001, Name = "Potion fictive", Type = 12, Level = 1, pods = 1 };
            InventoryObjects.FullInventory[2003] = new InventoryObjects { ID = 2003, Name = "Ressource fictive", Type = 15, Level = 1, pods = 1 };
            InventoryObjects.FullInventory[2004] = new InventoryObjects { ID = 2004, Name = "Certificat fictif", Type = ShedExchange.CertificateType, Level = 1, pods = 0 };
            InventoryObjects.FullInventory[7655] = new InventoryObjects { ID = 7655, Name = "Abreuvoir fictif", Type = 93, Level = 1, pods = 10 };
            using (var account = new Accounts(new AccountConfig("synthetic-mount", "synthetic", "loopback")))
            {
                account.Logger.log_event += (entry, color) => logs.Enqueue(entry.message);
                Task<Socket> accept = listener.AcceptSocketAsync();
                Complete(account.Connexion.ConnectToServer(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port)); Complete(accept);
                using (Socket peer = accept.Result)
                {
                    peer.ReceiveTimeout = 6000;
                    account.Game.character.SetPerso_Data(42, "Personnage de test", 25, 0, 8);
                    account.Game.character.Kamas = 5000;
                    Feed(account, "GDM|" + MapId + "|date|key"); Expect(peer, "GI", "GDM did not request GI");
                    Feed(account, "GM|" + SelfGm + "|" + ParkMountGm);
                    account.AccountStates = AccountStates.CONNECTED_INACTIVE;
                    Feed(account, "OAKO3~7d1~5~~;3eb~7d4~1~~3e3#c#0#0#0d0+12");
                    Check(account.Game.character.Inventory.GetByInventoryId(3)?.Qua == 5 && account.Game.character.Inventory.GetByInventoryId(1003)?.Type == ShedExchange.CertificateType
                        && account.Game.Map.GetActor(-6) is ParkMountActor, "Synthetic inventory or park mount was not read");

                    OwnMount(account, peer, logs);
                    Bags(account, peer, logs);
                    ParkAndSale(account, peer, logs);
                    Shed(account, peer, logs);
                    Malformed(account, peer, logs);
                    Windows(account, peer, folder);
                    NoPacket(peer, "Unexpected packet at the end of the test");

                    account.Game.Interactions.Clear();
                    MountActions mount = account.Game.Interactions.Mount;
                    Check(mount.Current == null && !mount.HasMount && !mount.IsOpen && mount.Park == null && mount.Viewed == null && mount.XpPercent == null
                        && !account.Game.character.UseMount && !mount.Shed.IsOpen && !mount.Inventory.IsOpen, "Clear kept the mount state");
                    NoPacket(peer, "Clear sent a packet");
                }
            }
        }
        finally { listener.Stop(); Environment.CurrentDirectory = previous; }
    }

    /// <summary>Fiches (Mount.parse de StarLoco, createMount du client), textes rides_fr, déplacements de MountStorage, certificat, jauge.</summary>
    private static void Model()
    {
        Mount mount = Mount.Parse(Equipped);
        Check(mount != null && mount.Id == -12 && mount.ModelId == 18 && mount.Capacities.SequenceEqual(new[] { 9 }) && mount.IsChameleon && mount.Name == "Fulgurante" && mount.IsFemale
            && mount.Xp == 1500 && mount.XpMin == 1000 && mount.XpMax == 2000 && mount.XpProgress == 50 && mount.Level == 5 && mount.Mountable && mount.PodsMax == 250 && !mount.Wild
            && mount.Stamina == 2000 && mount.StaminaMax == 10000 && mount.Maturity == 1000 && mount.MaturityMax == 1000 && mount.Energy == 5000 && mount.EnergyMax == 7500
            && mount.Serenity == -2500 && mount.SerenityMin == -10000 && mount.SerenityMax == 10000 && mount.Love == 3000 && mount.LoveMax == 10000 && mount.Fecondation == -1
            && !mount.IsPregnant && !mount.Fecondable && mount.Tired == 10 && mount.TiredMax == 240 && mount.Reproductions == 2 && mount.ReproductionsMax == 20 && !mount.IsCastrated
            && !mount.IsSterile && mount.Pods == null && !mount.NewBorn && mount.Ancestors.Count == 14, "Re+ record (Mount.parse) was not read");
        Check(mount.ModelName == "Dragodinde fictive" && mount.Gfx == 7002 && mount.Colors.SequenceEqual(new[] { 16747520, -1, 255 }) && mount.CapacityNames.SequenceEqual(new[] { "Caméléone fictive" })
            && Mount.CapacityDescription(9) == "Change de couleur (test)." && mount.SexText == "Femelle" && mount.ReproductionText == "Reproductions : 2 / 20" && mount.DisplayName == "Fulgurante",
            "rides_fr texts differ: " + mount.ModelName + " / " + mount.ReproductionText);
        Mount castrated = Mount.Parse(ShedOne), pregnant = Mount.Parse(ShedTwo), minimal = Mount.Parse("-7:99");
        Check(castrated.IsCastrated && castrated.ReproductionText == "Castrée (test)" && castrated.SexText == "Mâle" && pregnant.IsPregnant && pregnant.Fecondation == 3
            && pregnant.DisplayName == "Sans nom (test)" && pregnant.ReproductionText.Contains("3"), "Reproduction states differ");
        Check(minimal != null && minimal.Id == -7 && minimal.ModelName == "Monture n° 99" && minimal.Gfx == Mount.DefaultGfx && minimal.Colors.All(c => c == -1) && minimal.Capacities.Count == 0
            && Mount.CapacityName(4) == "Capacité n° 4", "Minimal record or missing rides_fr entries differ");
        foreach (string broken in new[] { null, "", "x:18", "-1:y", "-1", "1:18:a:,,0:n:z", "1:18:a:,,0:n:0:1,x,3", "1:18:a:,,0:n:0:1,2,3:99999999999" })
            Check(Mount.Parse(broken) == null, "Unreadable record accepted: " + broken);
        Mount renamed = mount.WithName("Autre"), loaded = mount.WithPods(40, 250);
        Check(renamed.Name == "Autre" && mount.Name == "Fulgurante" && loaded.Pods == 40 && mount.Pods == null, "WithName / WithPods modified the original record");

        // Déplacements de MountStorage.click (étable, enclos, monture équipée, certificats).
        Check(ShedExchange.MovePackets(MountLocation.Shed, MountLocation.Certificate, -13).SequenceEqual(new[] { "Erc-13" })
            && ShedExchange.MovePackets(MountLocation.Park, MountLocation.Certificate, -6).SequenceEqual(new[] { "Efg-6", "Erc-6" })
            && ShedExchange.MovePackets(MountLocation.Equipped, MountLocation.Certificate, -12).SequenceEqual(new[] { "Erp-12", "Erc-12" })
            && ShedExchange.MovePackets(MountLocation.Shed, MountLocation.Park, -13).SequenceEqual(new[] { "Efp-13" })
            && ShedExchange.MovePackets(MountLocation.Equipped, MountLocation.Park, -12).SequenceEqual(new[] { "Erp-12", "Efp-12" })
            && ShedExchange.MovePackets(MountLocation.Shed, MountLocation.Equipped, -13).SequenceEqual(new[] { "Erg-13" })
            && ShedExchange.MovePackets(MountLocation.Park, MountLocation.Equipped, -6).SequenceEqual(new[] { "Efg-6", "Erg-6" })
            && ShedExchange.MovePackets(MountLocation.Certificate, MountLocation.Shed, 1003).SequenceEqual(new[] { "ErC1003" })
            && ShedExchange.MovePackets(MountLocation.Park, MountLocation.Shed, -6).SequenceEqual(new[] { "Efg-6" })
            && ShedExchange.MovePackets(MountLocation.Equipped, MountLocation.Shed, -12).SequenceEqual(new[] { "Erp-12" })
            && ShedExchange.MovePackets(MountLocation.Certificate, MountLocation.Park, 1003).Count == 0
            && ShedExchange.MovePackets(MountLocation.Certificate, MountLocation.Equipped, 1003).Count == 0
            && ShedExchange.MovePackets(MountLocation.Shed, MountLocation.Shed, -13).Count == 0, "MountStorage move chains differ");

        // Certificat : effet 995 (0x3e3), paramètres hexadécimaux 1 et 2 comme Mount.data(param1, param2).
        Check(MountActions.TryCertificateParameters("3e3#c#0#0#0d0+12", out long first, out long second) && first == 12 && second == 0
            && MountActions.TryCertificateParameters("7d#5#0#0#,3e3#ff#1#0#", out first, out second) && first == 255 && second == 1
            && !MountActions.TryCertificateParameters("7d#5#0#0#", out first, out second) && !MountActions.TryCertificateParameters(string.Empty, out first, out second)
            && MountActions.CertificatePacket(12, 0) == "Rd12|0", "Certificate parameters differ");

        // Jauge « XP Monture » (showGaugeMode du client) : floor((xp - xpMin) / (xpMax - xpMin) * 100), couleur de l'XP.
        string tip = CircleGauge.Compute("xpmount", null, mount, out double percent, out Color color);
        Check(percent == 50 && color == CircleGauge.XpColor && tip.Contains("50 %"), "xpmount gauge differs: " + tip + " / " + percent);
        tip = CircleGauge.Compute("xpmount", null, (Mount)null, out percent, out color);
        Check(percent == 0 && tip.Contains("aucune monture"), "xpmount gauge without a mount differs: " + tip);
        CircleGauge.Compute("xpmount", null, out percent, out color);
        Check(percent == 0, "Former xpmount overload changed");
    }

    /// <summary>Monture équipée : Re±, ReE, Rr et ses refus, Rr±, Rn, Rx, Rc, ER18 jamais émis.</summary>
    private static void OwnMount(Accounts account, Socket peer, ConcurrentQueue<string> logs)
    {
        MountActions mount = account.Game.Interactions.Mount;
        var character = account.Game.character;
        int changes = 0; Action counted = () => Interlocked.Increment(ref changes);
        mount.Changed += counted;
        Check(mount.Current == null && !mount.HasMount && !account.CanUseMount, "Mount service starts with a mount");
        InteractionResult outOfRange = Result(mount.SetXpRatioAsync(95));
        Check(!outOfRange.Sent && outOfRange.Message.Contains("90"), "Rx95 was not refused locally: " + outOfRange.Message);
        InteractionResult noMount = Result(mount.RideAsync());
        Check(!noMount.Sent && noMount.Message == "Aucune monture équipée (test).", "Ride without a mount was not refused with MOUNT_NO_EQUIP: " + noMount.Message);
        Check(!Sent(mount.RenameAsync("Nom")) && !Sent(mount.SetXpRatioAsync(10)) && !Sent(mount.CastrateAsync()) && !Sent(mount.FreeAsync()) && !Sent(mount.OpenInventoryAsync()),
            "Mount actions were accepted without a mount");
        NoPacket(peer, "Refused mount actions reached the server");

        // Re+ : fiche lue, monture annoncée (CanUseMount) ; Rr refusé sous le niveau 60 (toogleOnMount de StarLoco).
        Feed(account, "Re+" + Equipped);
        Check(mount.Current?.Id == -12 && mount.Current.Name == "Fulgurante" && mount.HasMount && account.CanUseMount && changes == 1 && mount.LastMessage.Contains("Fulgurante"),
            "Re+ did not equip the mount");
        InteractionResult tooLow = Result(mount.RideAsync());
        Check(!tooLow.Sent && tooLow.Message.Contains("60"), "Ride under level 60 was not refused: " + tooLow.Message);
        NoPacket(peer, "Ride under level 60 reached the server");
        character.Level = 60;
        Check(Sent(mount.RideAsync())); Expect(peer, "Rr", "Ride does not send Rr");
        Feed(account, "Rr+");
        Check(mount.IsRiding && character.UseMount, "Rr+ was not read");
        character.Level = 25;
        Check(Sent(mount.RideAsync())); Expect(peer, "Rr", "Getting off the mount under level 60 was refused");
        Feed(account, "Rr-");
        Check(!mount.IsRiding && !character.UseMount, "Rr- was not read");
        character.Level = 60;

        // Rn : nom vide ou coupant les fiches refusé ; Rn reçu → copie renommée.
        foreach (string bad in new[] { "", "a:b", "a;b", "a|b", "a~b", "a,b", "a\nb" })
            Check(!Sent(mount.RenameAsync(bad)), "Invalid name accepted: " + bad);
        NoPacket(peer, "Refused names reached the server");
        Check(Sent(mount.RenameAsync("Éclair"))); Expect(peer, "RnÉclair", "Rename does not send Rn<nom>");
        Feed(account, "RnÉclair");
        Check(mount.Current.Name == "Éclair" && mount.Current.Id == -12 && mount.Current.Level == 5, "Rn was not read");

        // Rx : 0 à 90 (PopupQuantity max 90), Rx reçu.
        Check(!Sent(mount.SetXpRatioAsync(95)) && !Sent(mount.SetXpRatioAsync(-1)), "Out of range XP ratio accepted");
        NoPacket(peer, "Rx95 or Rx-1 reached the server");
        Check(Sent(mount.SetXpRatioAsync(40))); Expect(peer, "Rx40", "SetXpRatio does not send Rx<n>");
        Feed(account, "Rx40"); Check(mount.XpPercent == 40, "Rx was not read");
        Check(Sent(mount.SetXpRatioAsync(90))); Expect(peer, "Rx90", "Rx90 was refused");
        Check(Sent(mount.CastrateAsync())); Expect(peer, "Rc", "Castrate does not send Rc");

        // Re- / ReE : plus de monture, erreurs de Mount.equipError.
        Feed(account, "Re-");
        Check(mount.Current == null && !mount.HasMount && !account.CanUseMount && !mount.IsRiding, "Re- did not unequip the mount");
        Feed(account, "ReE+");
        Check(mount.LastMessage == "Déjà une monture (test).", "ReE+ is not MOUNT_ERROR_ALREADY_HAVE_ONE: " + mount.LastMessage);
        Feed(account, "Re+" + Equipped);
        Check(mount.Current?.Id == -12 && mount.Current.Name == "Fulgurante", "Second Re+ was not read");

        // Action PNJ 8 (ER18) : StarLoco n'a aucune branche pour ce type, le bot ne l'émet jamais.
        Check(!NpcActions.ServerHandles(NpcActions.MountExchange) && NpcActions.ExchangeType(NpcActions.MountExchange) == 18, "NPC action 8 is handled");
        Check(!Sent(account.Game.Interactions.Npc.RequestActionAsync(NpcActions.MountExchange, -1)), "NPC action 8 was accepted");
        NoPacket(peer, "ER18 was sent");

        // Rd : fiche consultée ; Rp<id> avec l'identifiant négatif du sprite -9 ; certificat → Rd<p1>|<p2>.
        Feed(account, "Rd" + ParkMount);
        Check(mount.Viewed?.Id == -6 && mount.Viewed.Name == "Rapide" && mount.ViewedSerial == 1, "Rd was not read");
        Check(Sent(mount.ViewParkMountAsync(-6))); Expect(peer, "Rp-6", "ViewParkMount does not send Rp<id>");
        Check(!Sent(mount.ViewParkMountAsync(0)), "Rp0 was accepted");
        InventoryObjects certificate = character.Inventory.GetByInventoryId(1003), potion = character.Inventory.GetByInventoryId(3);
        Check(Sent(mount.ViewCertificateAsync(certificate))); Expect(peer, "Rd12|0", "Certificate does not send Rd<p1>|<p2>");
        Check(!Sent(mount.ViewCertificateAsync(potion)) && !Sent(mount.ViewCertificateAsync(null)), "A non-certificate was accepted");
        NoPacket(peer, "Refused certificate reached the server");
        mount.Changed -= counted;
    }

    /// <summary>Sacoches : ER15|, ECK15|&lt;id&gt;, EL O…;, Ew, EMO±, EsKO±, Rf refusé sacoches pleines, EV.</summary>
    private static void Bags(Accounts account, Socket peer, ConcurrentQueue<string> logs)
    {
        MountActions mount = account.Game.Interactions.Mount;
        MountExchange bags = mount.Inventory;
        ExchangeRegistry registry = account.Game.Interactions.Exchanges;
        Check(bags != null && registry.For(15) == bags && registry.For(16) == mount.Shed && registry.Get<MountExchange>() == bags, "Registry does not map types 15 and 16");
        Check(!Sent(bags.DepositAsync(3, 1)) && !Sent(bags.WithdrawAsync(1002, 1)) && !Sent(bags.LeaveAsync()), "Bag actions accepted while closed");
        Check(Sent(mount.OpenInventoryAsync())); Expect(peer, "ER15|", "OpenInventory does not send ER15|");
        Feed(account, "ECK15|-12");
        Check(bags.IsOpen && bags.MountId == -12 && registry.Current == bags && account.AccountStates == AccountStates.STORAGE && !bags.ContentReceived, "ECK15 did not open the bags");
        Check(!Sent(mount.OpenInventoryAsync()), "ER15 accepted while a window is open");
        Feed(account, "ELO3ea~7d3~2~~;");
        Check(bags.ContentReceived && bags.Items.Count == 1 && bags.Items[0].Id == 1002 && bags.Items[0].TemplateId == 2003 && bags.Items[0].Quantity == 2 && bags.Items[0].Name == "Ressource fictive",
            "EL of the bags was not read");
        Feed(account, "Ew40;250");
        Check(bags.Pods == 40 && bags.PodsMax == 250 && mount.Current.Pods == 40 && mount.Current.PodsMax == 250, "Ew was not read");
        InteractionResult full = Result(mount.FreeAsync());
        Check(!full.Sent && full.Message.Contains("sacoches"), "Rf accepted with full bags: " + full.Message);
        Check(!Sent(bags.DepositAsync(3, 6)) && !Sent(bags.DepositAsync(999, 1)) && !Sent(bags.WithdrawAsync(1002, 3)) && !Sent(bags.WithdrawAsync(5, 1)), "Invalid bag movements accepted");
        NoPacket(peer, "Refused bag actions reached the server");
        Check(Sent(bags.DepositAsync(3, 2))); Expect(peer, "EMO+3|2", "Deposit does not send EMO+<objet>|<quantité>");
        Check(Sent(bags.WithdrawAsync(1002, 1))); Expect(peer, "EMO-1002|1", "Withdraw does not send EMO-<objet>|<quantité>");
        Feed(account, "EsKO-1002"); Check(bags.Items.Count == 0, "EsKO- was not read");
        Feed(account, "EsKO+1004|1|2001|"); Check(bags.Items.Count == 1 && bags.Items[0].Id == 1004 && bags.Items[0].Name == "Potion fictive", "EsKO+ was not read");
        int before = logs.Count;
        Feed(account, "EsKOx"); Feed(account, "EsKO+a|b|c"); Feed(account, "Ewx;y");
        Check(bags.Items.Count == 1 && bags.Pods == 40 && logs.Count > before, "Malformed bag packets changed the state or were not logged");
        Check(Sent(bags.LeaveAsync())); Expect(peer, "EV", "Leave does not send EV");
        Feed(account, "EV");
        Check(!bags.IsOpen && bags.Items.Count == 0 && registry.Current == null && account.AccountStates == AccountStates.CONNECTED_INACTIVE, "EV did not close the bags");
        Check(Sent(mount.FreeAsync())); Expect(peer, "Rf", "Free does not send Rf");
        NoPacket(peer, "Bags sent an extra packet");
    }

    /// <summary>Enclos : Rp → SetMountPark, guilde (gS), objets d'élevage → Ro, RD / Rs / Rb / Rv, enclos oublié sur une autre carte.</summary>
    private static void ParkAndSale(Accounts account, Socket peer, ConcurrentQueue<string> logs)
    {
        MountActions mount = account.Game.Interactions.Mount;
        InteractiveActions interactive = account.Game.Interactions.Interactive;
        Check(mount.Park == null && !mount.IsParkMine && !Sent(mount.RemoveParkObjectAsync(TroughCell)) && !Sent(mount.SellParkAsync(10)) && !Sent(mount.CloseSaleAsync()),
            "Park actions accepted without a park");
        Feed(account, OwnPark);
        MountParkInfo park = mount.Park;
        Check(park != null && ReferenceEquals(park, interactive.MountPark) && park.OwnerId == 42 && park.Price == 0 && park.Size == 5 && park.Items == 3 && park.GuildName == "Guilde fictive"
            && !mount.IsParkMine, "Rp was not handed to InteractiveActions.SetMountPark");
        Feed(account, GuildStats(0));
        Check(account.Game.Interactions.Guild.HasGuild && mount.OwnGuild == "Guilde fictive" && mount.IsParkMine && !mount.CanManageParkMounts, "Guild park ownership differs");
        Feed(account, GuildStats(ManageOtherMounts));
        Check(mount.CanManageParkMounts, "ManageOtherMounts right was not read");
        Feed(account, GuildStats(0));

        // Objets d'élevage : GDO avec durabilité → Ro<cellule> dans un enclos de sa guilde.
        Feed(account, "GDO+" + TroughCell + ";7655;1;15;20"); Feed(account, "GDO+" + PlainObjectCell + ";7655;0");
        Check(mount.ParkObjectAt(TroughCell)?.Durability == 15 && mount.ParkObjectAt(PlainObjectCell) == null && mount.ParkObjectAt(25) == null, "Breeding objects differ");
        Check(PaddockMenuProvider.HasMenu(account.Game, TroughCell) && !PaddockMenuProvider.HasMenu(account.Game, PlainObjectCell) && PaddockMenuProvider.Title(account.Game, TroughCell) == "Abreuvoir fictif"
            && PaddockMenuProvider.Entries(account.Game, TroughCell).Select(entry => entry.Texte).SequenceEqual(new[] { "Retirer (test)" }), "Breeding object menu differs");
        Check(!Sent(mount.RemoveParkObjectAsync(25)) && !Sent(mount.RemoveParkObjectAsync(PlainObjectCell)), "Ro accepted without a breeding object");
        Check(Sent(mount.RemoveParkObjectAsync(TroughCell))); Expect(peer, "Ro" + TroughCell, "Remove does not send Ro<cellule>");

        // Vente de son enclos : RD<prix>|<prix> → fenêtre ; Rs<prix>, Rs0, Rb refusé ; Rv.
        Feed(account, "RD0|0");
        Check(mount.IsOpen && mount.SaleDefaultPrice == 0 && account.AccountStates == AccountStates.DIALOG, "RD did not open the sale window");
        Check(!Sent(mount.BuyParkAsync()) && !Sent(mount.SellParkAsync(-1)), "Buying the own park or a negative price was accepted");
        Check(Sent(mount.SellParkAsync(5000))); Expect(peer, "Rs5000", "Sell does not send Rs<prix>");
        Check(Sent(mount.SellParkAsync(0))); Expect(peer, "Rs0", "Cancel does not send Rs0");
        Check(Sent(mount.CloseSaleAsync())); Expect(peer, "Rv", "Close does not send Rv");
        Feed(account, "Rv");
        Check(!mount.IsOpen && account.AccountStates == AccountStates.CONNECTED_INACTIVE, "Rv did not close the sale window");

        // Enclos d'une autre guilde en vente : Ro refusé, Rb<prix> après le contrôle des kamas.
        Feed(account, OtherPark);
        Check(!mount.IsParkMine && mount.Park.Price == 8000 && !PaddockMenuProvider.HasMenu(account.Game, TroughCell) && !Sent(mount.RemoveParkObjectAsync(TroughCell)), "Another guild's park was treated as own");
        Feed(account, "RD8000|8000");
        Check(mount.IsOpen && mount.SaleDefaultPrice == 8000 && !Sent(mount.SellParkAsync(100)), "Selling another guild's park was accepted");
        InteractionResult poor = Result(mount.BuyParkAsync());
        Check(!poor.Sent && poor.Message == "Pas assez de kamas (test).", "Buying without kamas was accepted: " + poor.Message);
        NoPacket(peer, "Refused park actions reached the server");
        account.Game.character.Kamas = 20000;
        Check(Sent(mount.BuyParkAsync())); Expect(peer, "Rb8000", "Buy does not send Rb<prix>");
        Feed(account, "Rv"); Check(!mount.IsOpen, "Rv after Rb did not close the window");

        // Autre carte : l'enclos annoncé n'est plus celui de la carte ; retour sans Rp : toujours aucun.
        Feed(account, "GDM|" + OtherMapId + "|date|key"); Expect(peer, "GI", "GDM did not request GI (other map)");
        Feed(account, "GM|" + SelfGm); account.AccountStates = AccountStates.CONNECTED_INACTIVE;
        Check(mount.Park == null && !mount.IsParkMine && !Sent(mount.RemoveParkObjectAsync(TroughCell)), "The park stayed known on another map");
        Feed(account, "GDM|" + MapId + "|date|key"); Expect(peer, "GI", "GDM did not request GI (return)");
        Feed(account, "GM|" + SelfGm + "|" + ParkMountGm); account.AccountStates = AccountStates.CONNECTED_INACTIVE;
        Check(mount.Park == null, "The park came back without Rp");
        Feed(account, OwnPark); Feed(account, "GDO+" + TroughCell + ";7655;1;15;20");
        Check(mount.IsParkMine && mount.ParkObjectAt(TroughCell) != null, "Rp after the return was not read");
        NoPacket(peer, "Park receptions triggered a reply");
    }

    /// <summary>Étable : ECK16|&lt;étable&gt;~&lt;enclos&gt;, déplacements Er* / Ef* enchaînés, Ee~ / Ee± / Ef± / EeE, EV.</summary>
    private static void Shed(Accounts account, Socket peer, ConcurrentQueue<string> logs)
    {
        MountActions mount = account.Game.Interactions.Mount;
        ShedExchange shed = mount.Shed;
        Check(!Sent(shed.MoveAsync(MountLocation.Shed, MountLocation.Park, -13)) && !Sent(shed.LeaveAsync()), "Shed actions accepted while closed");
        Feed(account, "ECK16|" + ShedOne + ";;" + ShedTwo + "~" + ParkMount);
        Check(shed.IsOpen && account.AccountStates == AccountStates.STORAGE && shed.ShedMounts.Select(m => m.Id).SequenceEqual(new[] { -13, -14 })
            && shed.ParkMounts.Select(m => m.Id).SequenceEqual(new[] { -6 }) && shed.Certificates.Count == 1 && shed.Certificates[0].Inventory_ID == 1003, "ECK16 was not read");
        InteractionResult already = Result(shed.MoveAsync(MountLocation.Shed, MountLocation.Equipped, -13));
        Check(!already.Sent && already.Message == "Déjà une monture (test).", "Equipping a second mount was accepted: " + already.Message);
        Check(!Sent(shed.MoveAsync(MountLocation.Shed, MountLocation.Park, -99)) && !Sent(shed.MoveAsync(MountLocation.Park, MountLocation.Shed, -13))
            && !Sent(shed.MoveAsync(MountLocation.Equipped, MountLocation.Shed, -13)) && !Sent(shed.MoveAsync(MountLocation.Certificate, MountLocation.Shed, 3))
            && !Sent(shed.MoveAsync(MountLocation.Certificate, MountLocation.Park, 1003)) && !Sent(shed.MoveAsync(MountLocation.Shed, MountLocation.Shed, -13)), "Invalid moves accepted");
        NoPacket(peer, "Refused moves reached the server");
        Check(Sent(shed.MoveAsync(MountLocation.Shed, MountLocation.Park, -13))); Expect(peer, "Efp-13", "Shed → park does not send Efp");
        Check(Sent(shed.MoveAsync(MountLocation.Park, MountLocation.Shed, -6))); Expect(peer, "Efg-6", "Park → shed does not send Efg");
        Check(Sent(shed.MoveAsync(MountLocation.Park, MountLocation.Certificate, -6))); Expect(peer, "Efg-6", "Park → certificate does not start with Efg"); Expect(peer, "Erc-6", "Park → certificate does not end with Erc");
        Check(Sent(shed.MoveAsync(MountLocation.Equipped, MountLocation.Park, -12))); Expect(peer, "Erp-12", "Equipped → park does not start with Erp"); Expect(peer, "Efp-12", "Equipped → park does not end with Efp");
        Check(Sent(shed.MoveAsync(MountLocation.Certificate, MountLocation.Shed, 1003))); Expect(peer, "ErC1003", "Certificate → shed does not send ErC");

        // Ee / Ef : naissance, ajout, retrait par identifiant (négatif), erreur.
        Feed(account, "Ee--13"); Check(shed.ShedMounts.Select(m => m.Id).SequenceEqual(new[] { -14 }), "Ee- was not read");
        Feed(account, "Ef+" + ShedOne); Check(shed.ParkMounts.Select(m => m.Id).SequenceEqual(new[] { -6, -13 }), "Ef+ was not read");
        Feed(account, "Ee~" + NewBorn); Check(shed.ShedMounts.Any(m => m.Id == -15 && m.NewBorn), "Ee~ (birth) was not read");
        Feed(account, "Ef--6"); Check(shed.ParkMounts.Select(m => m.Id).SequenceEqual(new[] { -13 }), "Ef- was not read");
        Feed(account, "Ef+" + ShedOne); Check(shed.ParkMounts.Count == 1, "Ef+ of a known mount duplicated it");
        int before = logs.Count;
        Feed(account, "EeE"); Feed(account, "Ee*x"); Feed(account, "Ee+bad"); Feed(account, "Ef~" + NewBorn); Feed(account, "Ee-x"); Feed(account, "Ee"); Feed(account, "Ef");
        Check(shed.ShedMounts.Count == 2 && shed.ParkMounts.Count == 1 && logs.Count >= before + 7 && !logs.Any(entry => entry.Contains("non appliqué")),
            "Malformed Ee/Ef changed the shed or threw");

        // Sans monture équipée : étable → équipée (Erg).
        Feed(account, "Re-");
        Check(Sent(shed.MoveAsync(MountLocation.Shed, MountLocation.Equipped, -14))); Expect(peer, "Erg-14", "Shed → equipped does not send Erg");
        Feed(account, "Ee--14"); Feed(account, "Re+" + ShedTwo);
        Check(mount.Current?.Id == -14 && shed.ShedMounts.Select(m => m.Id).SequenceEqual(new[] { -15 }), "Equipping from the shed was not applied");
        Check(Sent(shed.LeaveAsync())); Expect(peer, "EV", "Leave does not send EV (shed)");
        Feed(account, "EV");
        Check(!shed.IsOpen && shed.ShedMounts.Count == 0 && account.AccountStates == AccountStates.CONNECTED_INACTIVE, "EV did not close the shed");
        Feed(account, "Ee+" + ShedOne); Feed(account, "Ef+" + ParkMount);
        Check(shed.ShedMounts.Count == 0 && shed.ParkMounts.Count == 0 && !shed.IsOpen, "Ee / Ef while the shed is closed were applied");
        Feed(account, "Re+" + Equipped);
        NoPacket(peer, "Shed receptions triggered a reply");
    }

    /// <summary>Paquets mal formés : journalisés, jamais propagés, état inchangé.</summary>
    private static void Malformed(Accounts account, Socket peer, ConcurrentQueue<string> logs)
    {
        MountActions mount = account.Game.Interactions.Mount;
        int before = logs.Count;
        foreach (string packet in new[] { "Re", "Re*", "Rx", "Rxabc", "Rd", "Rdx:y", "Rp", "Rpa;b;c;d", "RDx|y", "ReEz" })
            Feed(account, packet);
        Check(mount.Current?.Id == -12 && mount.HasMount && mount.XpPercent == 40 && mount.Viewed?.Id == -6 && !mount.IsOpen && mount.IsParkMine
            && account.AccountStates == AccountStates.CONNECTED_INACTIVE, "Malformed mount packets changed the state");
        Check(logs.Count >= before + 10 && !logs.Any(entry => entry.Contains("Paquet de monture illisible ignoré (")), "Malformed mount packets were not logged or threw: "
            + string.Join(" / ", logs.Skip(before)));
        Feed(account, "Re+bad");
        Check(mount.Current == null && mount.HasMount && logs.Any(entry => entry.Contains("illisible")), "Unreadable Re+ record did not keep the server announcement");
        Feed(account, "Re+" + Equipped);
        NoPacket(peer, "Malformed packets triggered a reply");
    }

    /// <summary>Jauge, volets Monture et Enclos, menus d'une monture d'enclos et d'un objet d'élevage, dans une fenêtre de jeu invisible.</summary>
    private static void Windows(Accounts account, Socket peer, string folder)
    {
        MountActions mount = account.Game.Interactions.Mount;
        Feed(account, "Rx40");
        string images = Path.Combine(folder, "images");
        Png(Path.Combine(images, "sprites"), "7002_staticS", Color.FromArgb(255, 200, 120, 40), 40, 40);
        Assets.GetProperty("Root", Any).SetValue(null, images, null);
        var reads = new List<KeyValuePair<string, int>>();
        Action<string, int> observer = (key, thread) => { lock (reads) reads.Add(new KeyValuePair<string, int>(key, thread)); };
        EventInfo readEvent = Assets.GetEvent("AssetFileRead", Any);
        readEvent.GetAddMethod(true).Invoke(null, new object[] { observer });
        try
        {
            using (var form = new GameClientFullform(account))
            {
                form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-4000, -4000);
                form.ShowInTaskbar = false; form.Show(); Application.DoEvents();
                int uiThread = Thread.CurrentThread.ManagedThreadId;
                PanelHost drawer = form.Panels;
                var panel = drawer.Get<MountPanel>(); var paddock = drawer.Get<PaddockPanel>();
                MapControl view = form.Map;
                Check(panel != null && paddock != null && drawer.Find("Mount") == panel && drawer.Current == null && view != null, "Mount or paddock panel is not registered in the drawer");
                form.Banner.RefreshAll();
                Check(form.Banner["Mount"] != null && form.Banner["Mount"].Available, "Banner « Monture » button stays greyed");
                form.Banner.Gauge.Show("xpmount", account.Game.character, mount.Current);
                Check(form.Banner.Gauge.Value == 50 && form.Banner.Gauge.GaugeColor == CircleGauge.XpColor, "Banner xpmount gauge differs: " + form.Banner.Gauge.Value);
                NoPacket(peer, "Opening the game window sent a packet");

                OwnPanel(account, peer, drawer, panel, uiThread, reads);
                BagsPanel(account, peer, drawer, panel);
                ParkMenus(account, peer, view);
                PaddockWindows(account, peer, drawer, paddock);
                form.Close(); Application.DoEvents();
            }
        }
        finally { readEvent.GetRemoveMethod(true).Invoke(null, new object[] { observer }); }
    }

    /// <summary>Volet Monture : fiche dessinée (sprite lu hors du fil d'interface), Monter (Rr), XP (Rx), nom (Rn), Castrer (boîte puis Rc).</summary>
    private static void OwnPanel(Accounts account, Socket peer, PanelHost drawer, MountPanel panel, int uiThread, List<KeyValuePair<string, int>> reads)
    {
        MountActions mount = account.Game.Interactions.Mount;
        drawer.Show(panel); Application.DoEvents();
        Check(drawer.Current == panel && panel.CurrentTab == MountPanel.OwnTab && panel.OwnSheet.Mount?.Id == -12, "Mount panel does not show the equipped mount");
        IReadOnlyList<string> lines = panel.OwnSheet.Lines;
        Check(lines[0] == "Dragodinde fictive · Niveau 5" && lines.Any(line => line.Contains("Fulgurante")) && lines.Any(line => line.Contains("40 %"))
            && lines.Any(line => line.Contains("Caméléone fictive")) && panel.OwnSheet.Gauges.Count == 7 && panel.OwnSheet.Gauges.Any(g => g.Item3 == -10000 && g.Item2 == -2500),
            "Mount sheet lines differ: " + string.Join(" | ", lines));
        PumpUntil(() => Cached("Sprites", "7002_staticS"));
        lock (reads) Check(reads.Any(entry => entry.Key.EndsWith("7002_staticS", StringComparison.Ordinal) && entry.Value != uiThread), "The mount sprite was not read off the UI thread");
        panel.View.Refresh(); Application.DoEvents();

        var ride = (Control)Get(panel, "ride");
        Check(ride.Text == "Monter" && ride.Enabled, "Ride button differs");
        Click(ride); Expect(peer, "Rr", "Ride button does not send Rr");
        FeedFromNetwork(account, "Rr+"); PumpUntil(() => ride.Text == "Descendre");
        ((NumericUpDown)Get(panel, "xpInput")).Value = 30;
        Click(Get(panel, "applyXp")); Expect(peer, "Rx30", "XP button does not send Rx<n>");
        var nameInput = (TextBox)Get(panel, "nameInput");
        nameInput.Text = "Tonnerre"; Application.DoEvents();
        Click(Get(panel, "rename")); Expect(peer, "RnTonnerre", "Rename button does not send Rn<nom>");
        Feed(account, "Rr-"); Application.DoEvents();

        var castrate = (Control)Get(panel, "castrate");
        Click(castrate); PumpUntil(() => panel.ConfirmDialog != null);
        Check(DialogMessage(panel.ConfirmDialog) == "Castrer la monture ? (test)" && BotDialogs.OpenDialogs.Contains(panel.ConfirmDialog) && !castrate.Enabled,
            "Castrate dialog differs from DO_U_CASTRATE_YOUR_MOUNT: " + DialogMessage(panel.ConfirmDialog));
        NoPacket(peer, "Rc was sent before the answer");
        Answer(panel.ConfirmDialog, "Non"); PumpUntil(() => panel.ConfirmDialog == null);
        NoPacket(peer, "« Non » sent a packet");
        Click(castrate); PumpUntil(() => panel.ConfirmDialog != null);
        Answer(panel.ConfirmDialog, "Oui"); Expect(peer, "Rc", "« Oui » does not send Rc");
        PumpUntil(() => panel.ConfirmDialog == null);
        drawer.RequestClose(panel); Application.DoEvents();
        Check(!drawer.IsOpen(panel), "Mount panel did not close without a server window");
    }

    /// <summary>Sacoches depuis le réseau (volet fermé) : ouverture sur ECK15, contenu EL / Ew, EMO±, fermeture par le volet → EV, EV ferme ; fiche Rd.</summary>
    private static void BagsPanel(Accounts account, Socket peer, PanelHost drawer, MountPanel panel)
    {
        FeedFromNetwork(account, "ECK15|-12"); FeedFromNetwork(account, "ELO3ea~7d3~2~~;"); FeedFromNetwork(account, "Ew40;250");
        PumpUntil(() => drawer.Current == panel && panel.CurrentTab == MountPanel.BagsTab && panel.BagsGrid.Items.Count == 1);
        Check(panel.IsServerWindowOpen && panel.BagsGrid.Items[0].Id == 1002 && ((Label)Get(panel, "bagsPods")).Text.Contains("40"), "Bags tab content differs");
        var bag = (ListView)Get(panel, "bagsBag");
        Check(RowWithTag(bag, 3u) != null && RowWithTag(bag, 1003u) != null, "Bag list of the bags tab differs");
        RowWithTag(bag, 3u).Selected = true; Application.DoEvents();
        ((NumericUpDown)Get(panel, "depositQuantity")).Value = 1;
        Click(Get(panel, "bagsDeposit")); Expect(peer, "EMO+3|1", "Deposit button does not send EMO+");
        Check(panel.BagsGrid.SelectItem(1002), "Stored item cannot be selected"); Application.DoEvents();
        Click(Get(panel, "bagsWithdraw")); Expect(peer, "EMO-1002|1", "Withdraw button does not send EMO-");
        drawer.RequestClose(panel); Expect(peer, "EV", "Closing the panel does not send EV");
        Check(drawer.IsOpen(panel), "Mount panel closed before the server");
        FeedFromNetwork(account, "EV");
        PumpUntil(() => !drawer.IsOpen(panel));
        Check(panel.CurrentTab == MountPanel.OwnTab, "Bags tab stayed selected after EV");

        FeedFromNetwork(account, "Rd" + ParkMount);
        PumpUntil(() => drawer.Current == panel && panel.CurrentTab == MountPanel.ViewedTab);
        Check(panel.ViewedSheet.Mount?.Id == -6 && panel.ViewedSheet.Lines.Any(line => line.Contains("Rapide")), "Viewed tab does not show the Rd record");
        drawer.RequestClose(panel); Application.DoEvents();
        Check(!drawer.IsOpen(panel), "Mount panel did not close after Rd");
        NoPacket(peer, "Bags panel sent an extra packet");
    }

    /// <summary>Menus de la carte : monture d'enclos (-9) selon le droit de guilde, Maj + clic → Rp&lt;id&gt; ; objet d'élevage → Ro.</summary>
    private static void ParkMenus(Accounts account, Socket peer, MapControl view)
    {
        var actor = account.Game.Map.GetActor(-6) as ParkMountActor;
        Check(actor != null && actor.OwnerName == "Proprio fictif" && actor.Id == -6, "Park mount actor differs");
        var messages = new List<string>(); Action<string> feedback = messages.Add;
        view.Router.Feedback += feedback;
        try
        {
            IReadOnlyList<MenuEntry> entries = ActorMenuRegistry.Default.EntriesFor(actor, account.Game);
            MenuEntry header = entries.FirstOrDefault(entry => entry.Texte == "Monture de Proprio fictif (test)");
            MenuEntry details = entries.FirstOrDefault(entry => entry.Texte == "Consulter la fiche (test)");
            Check(header != null && !header.Activé && details != null && !details.Activé && details.Raccourci == "Maj + clic", "Park mount menu without the right differs: "
                + string.Join("|", entries.Select(entry => entry.Texte + ":" + entry.Activé)));
            Complete(view.Router.RouteAsync(ParkMountCell, MouseButtons.Left));
            Complete(view.Router.RouteAsync(ParkMountCell, MouseButtons.Left, Keys.Shift));
            NoPacket(peer, "A click on a park mount without the right sent a packet or moved the character");
            Check(messages.Any(message => message.Contains("Proprio fictif") && message.Contains("Rapide")), "Click without the right gave no feedback: " + string.Join(" / ", messages));

            Feed(account, GuildStats(ManageOtherMounts));
            details = ActorMenuRegistry.Default.EntriesFor(actor, account.Game).First(entry => entry.Texte == "Consulter la fiche (test)");
            Check(details.Activé, "Park mount menu entry stays disabled with the right");
            Complete(view.Router.RouteAsync(ParkMountCell, MouseButtons.Left, Keys.Shift)); Expect(peer, "Rp-6", "Shift + click does not send Rp<id>");
            Complete(view.Router.RouteAsync(ParkMountCell, MouseButtons.Left));
            ContextMenuStrip menu = view.Router.LastMenu;
            NoPacket(peer, "A plain click on a park mount sent a packet");
            Check(menu != null && Item(menu, "Consulter la fiche (test)") != null && Item(menu, "Consulter la fiche (test)").Enabled, "Plain click did not open the park mount menu: "
                + (menu == null ? "null" : string.Join("|", Texts(menu))));
            Item(menu, "Consulter la fiche (test)").PerformClick(); Expect(peer, "Rp-6", "Menu entry does not send Rp<id>");
            Feed(account, GuildStats(0));

            // Objet d'élevage d'un enclos de sa guilde : clic gauche → menu (titre, Retirer) au lieu de déplacer le personnage.
            Complete(view.Router.RouteAsync(TroughCell, MouseButtons.Left));
            ContextMenuStrip objectMenu = PaddockMenuProvider.LastMenu(view);
            NoPacket(peer, "A click on a breeding object sent a packet or moved the character");
            Check(objectMenu != null && objectMenu.Name == PaddockMenuProvider.MenuName && Texts(objectMenu).SequenceEqual(new[] { "Abreuvoir fictif", "Retirer (test)" }),
                "Breeding object menu differs: " + (objectMenu == null ? "null" : string.Join("|", Texts(objectMenu))));
            Item(objectMenu, "Retirer (test)").PerformClick(); Expect(peer, "Ro" + TroughCell, "« Retirer » does not send Ro<cellule>");
        }
        finally { view.Router.Feedback -= feedback; }
    }

    /// <summary>Volet Enclos : étable ECK16 depuis le réseau (Elever → Efp, certificat → Rd), objets d'élevage, vente Rs / Rv, achat DO_U_BUY_MOUNTPARK → Rb.</summary>
    private static void PaddockWindows(Accounts account, Socket peer, PanelHost drawer, PaddockPanel paddock)
    {
        MountActions mount = account.Game.Interactions.Mount;
        FeedFromNetwork(account, "ECK16|" + ShedOne + ";" + ShedTwo + "~" + ParkMount);
        PumpUntil(() => drawer.Current == paddock && paddock.ShedOpen && paddock.MountList.Items.Count == 2);
        Check(paddock.CurrentShedTab == PaddockPanel.ShedTab && RowWithTag(paddock.MountList, -13L) != null && RowWithTag(paddock.ObjectList, (int)TroughCell) != null,
            "Paddock shed tab differs");
        RowWithTag(paddock.MountList, -13L).Selected = true; Application.DoEvents();
        Check(paddock.SelectedSheet.Mount?.Id == -13 && ((Control)Get(paddock, "toPark")).Enabled && !((Control)Get(paddock, "toEquipped")).Enabled && ((Control)Get(paddock, "toCertificate")).Enabled,
            "Shed buttons differ for a shed mount with an equipped mount");
        Click(Get(paddock, "toPark")); Expect(peer, "Efp-13", "« Elever » does not send Efp<id>");
        var tabs = (MountTabStrip)Get(paddock, "shedTabs");
        tabs.ClickTab(PaddockPanel.CertificatesTab); Application.DoEvents();
        Check(paddock.CurrentShedTab == PaddockPanel.CertificatesTab && paddock.MountList.Items.Count == 1 && RowWithTag(paddock.MountList, 1003L) != null, "Certificates tab differs");
        RowWithTag(paddock.MountList, 1003L).Selected = true; Application.DoEvents();
        Check(((Control)Get(paddock, "toShed")).Enabled && !((Control)Get(paddock, "toPark")).Enabled, "Certificate buttons differ");
        Click(Get(paddock, "viewCertificate")); Expect(peer, "Rd12|0", "« Voir la fiche » does not send Rd<p1>|<p2>");
        Click(Get(paddock, "shedClose")); Expect(peer, "EV", "Shed close button does not send EV");
        FeedFromNetwork(account, "EV");
        PumpUntil(() => !drawer.IsOpen(paddock));

        // Vente de son enclos : RD → volet, prix, Valider → Rs<prix>, Fermer → Rv.
        FeedFromNetwork(account, "RD0|0");
        PumpUntil(() => drawer.Current == paddock && paddock.SaleOpen);
        Check(((Panel)Get(paddock, "saleArea")).Visible && ((Control)Get(paddock, "saleValidate")).Visible && !((Control)Get(paddock, "saleBuy")).Visible, "Own park sale area differs");
        ((NumericUpDown)Get(paddock, "salePrice")).Value = 7000;
        Click(Get(paddock, "saleValidate")); Expect(peer, "Rs7000", "« Valider » does not send Rs<prix>");
        Click(Get(paddock, "saleClose")); Expect(peer, "Rv", "« Fermer » does not send Rv");
        FeedFromNetwork(account, "Rv");
        PumpUntil(() => !drawer.IsOpen(paddock));

        // Achat d'un enclos d'une autre guilde : DO_U_BUY_MOUNTPARK ; Non n'envoie rien, Oui envoie Rb<prix>.
        Feed(account, OtherPark);
        FeedFromNetwork(account, "RD8000|8000");
        PumpUntil(() => drawer.Current == paddock && paddock.SaleOpen);
        var buy = (Control)Get(paddock, "saleBuy");
        Check(buy.Visible && buy.Enabled && !((Control)Get(paddock, "saleValidate")).Visible, "Buyer sale area differs");
        Click(buy); PumpUntil(() => paddock.BuyDialog != null);
        Check(DialogMessage(paddock.BuyDialog).StartsWith("Acheter cet enclos pour 8") && DialogMessage(paddock.BuyDialog).EndsWith("kamas ? (test)"),
            "DO_U_BUY_MOUNTPARK text differs: " + DialogMessage(paddock.BuyDialog));
        Answer(paddock.BuyDialog, "Non"); PumpUntil(() => paddock.BuyDialog == null);
        NoPacket(peer, "« Non » on the park purchase sent a packet");
        Click(buy); PumpUntil(() => paddock.BuyDialog != null);
        Answer(paddock.BuyDialog, "Oui"); Expect(peer, "Rb8000", "« Oui » does not send Rb<prix>");
        PumpUntil(() => paddock.BuyDialog == null);
        FeedFromNetwork(account, "Rv");
        PumpUntil(() => !drawer.IsOpen(paddock));
        Feed(account, OwnPark);
        Check(mount.IsParkMine, "Own park was not restored");
    }
}
