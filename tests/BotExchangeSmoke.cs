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
using Outil_Azur_complet.Bot.Panels;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Exchanges;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.NPC;
using Tool_BotProtocol.Game.Perso.Inventory;

// Échanges selon dofus.aks.Exchange (client 1.34) et StarLoco, sur un serveur fictif local : aiguillage ECK par type
// (registre, type inconnu refermé par EV), demandes ERK (plus d'EV automatique quand une interface répond, EA/EV),
// échange entre joueurs (EMO±/EMG/EK, EMK/EmK/EK<0|1><id>, délai de validation), échange PNJ, coffre (ECK5, EL, EMO±,
// EMG±, EsK), paquets mal formés ; puis volets Échange et Coffre (boîte Oui / Non / Ignorer, grilles, icônes d'objets lues
// depuis un dossier temporaire avec des textes LangData synthétiques).
internal static class BotExchangeSmoke
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
        try { Application.EnableVisualStyles(); Run(); Console.WriteLine("OK: registre ECK, ERK/EA/EV, échange EMO/EMG/EK, coffre EL/EsK, type inconnu refermé, volets Échange et Coffre"); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }

    /// <summary>Fenêtre d'un autre lot enregistrée sans attribut, comme le ferait une bibliothèque extérieure.</summary>
    private sealed class ProbeExchange : IExchange
    {
        public readonly List<string> Calls = new List<string>();
        public int ExchangeType { get; private set; } = -1;
        public bool IsOpen { get; private set; }
        public void OnCreated(int type, string data) { ExchangeType = type; IsOpen = true; Calls.Add("C" + type + ":" + data); }
        public void OnList(string payload) => Calls.Add("L" + payload);
        public void OnLocalMovement(string data) => Calls.Add("M" + data);
        public void OnDistantMovement(string data) => Calls.Add("m" + data);
        public void OnStorageMovement(string data) => Calls.Add("s" + data);
        public void OnReady(string data) => Calls.Add("K" + data);
        public void OnPods(string data) => Calls.Add("w" + data);
        public void OnLeave(string suffix) { IsOpen = false; ExchangeType = -1; Calls.Add("V" + suffix); }
        public void Clear() { IsOpen = false; ExchangeType = -1; Calls.Add("clear"); }
    }

    /// <summary>Fenêtre extérieure qui échoue sur EL : le registre doit la protéger comme les siennes.</summary>
    private sealed class FaultyExchange : IExchange
    {
        public int ExchangeType { get; private set; } = -1;
        public bool IsOpen { get; private set; }
        public void OnCreated(int type, string data) { ExchangeType = type; IsOpen = true; }
        public void OnList(string payload) { throw new InvalidOperationException("liste refusée"); }
        public void OnLocalMovement(string data) { }
        public void OnDistantMovement(string data) { }
        public void OnStorageMovement(string data) { }
        public void OnReady(string data) { }
        public void OnPods(string data) { }
        public void OnLeave(string suffix) { IsOpen = false; ExchangeType = -1; }
        public void Clear() { IsOpen = false; ExchangeType = -1; }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static object Get(object target, string field) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static IEnumerable<Control> All(Control value) { yield return value; foreach (Control child in value.Controls) foreach (var nested in All(child)) yield return nested; }
    private static void PumpUntil(Func<bool> done, int seconds = 6)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(seconds);
        while (!done()) { if (DateTime.UtcNow > end) throw new TimeoutException("Exchange loopback timed out"); Application.DoEvents(); Thread.Sleep(5); }
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
    private static void NoPacket(Socket socket, string message)
    {
        DateTime end = DateTime.UtcNow.AddMilliseconds(250);
        while (DateTime.UtcNow < end) { Application.DoEvents(); Thread.Sleep(5); }
        Check(socket.Available == 0, message);
    }
    private static ListViewItem RowWithTag(ListView list, object tag) => list.Items.Cast<ListViewItem>().FirstOrDefault(row => Equals(row.Tag, tag));
    private static void Click(object button) => ((Button)button).PerformClick();
    private static void Answer(Form dialog, string choice) => All(dialog).OfType<Button>().First(button => button.Text == choice).PerformClick();
    private static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; }
        catch (T) { return true; }
    }
    private static bool Near(Color a, Color b) => Math.Abs(a.R - b.R) <= 12 && Math.Abs(a.G - b.G) <= 12 && Math.Abs(a.B - b.B) <= 12;
    private static void Write(string folder, string file, string family, string body) =>
        File.WriteAllText(Path.Combine(folder, file), "<?xml version='1.0' encoding='utf-8'?>\n<BotLang famille=\"" + family + "\" langue=\"fr\" version=\"1\" source=\"" + family
            + "_fr_1.swf\">\n" + body + "\n</BotLang>\n", new UTF8Encoding(false));

    private static void Run()
    {
        string previous = Environment.CurrentDirectory;
        string folder = Path.Combine(TestPaths.Work, "bot-exchange"); Directory.CreateDirectory(folder); Environment.CurrentDirectory = folder;
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var logs = new ConcurrentQueue<string>();
        try
        {
            LangData.Clear();
            MessagesReception.Init();
            Map.AllBotMaps[900091] = new Map { MapID = 900091, MapWidth = 3, MapHeight = 4, MapData = string.Concat(Enumerable.Repeat("HhGaeaaaaa", 18)) };
            InventoryObjects.FullInventory[2001] = new InventoryObjects { ID = 2001, Name = "Potion fictive", Type = 12, Level = 1, pods = 1 };
            InventoryObjects.FullInventory[2002] = new InventoryObjects { ID = 2002, Name = "Épée fictive", Type = 6, Level = 10, pods = 10 };
            InventoryObjects.FullInventory[2003] = new InventoryObjects { ID = 2003, Name = "Anneau fictif", Type = 9, Level = 1, pods = 1 };
            PNJ.AllPNJ[100] = new PNJ(100, 100, null) { Name = "Marchand fictif" };

            // Registre : types découverts par attribut, enregistrement extérieur, doublons refusés.
            Check(new[] { 0, 1, 2, 5 }.All(ExchangeRegistry.IsRegistered) && !ExchangeRegistry.IsRegistered(99), "Discovered exchange types are wrong");
            var probes = new List<ProbeExchange>();
            ExchangeRegistry.Register(77, owner => { var probe = new ProbeExchange(); probes.Add(probe); return probe; });
            Check(ExchangeRegistry.IsRegistered(77) && ExchangeRegistry.RegisteredTypes.Contains(77), "External registration is not listed");
            Check(Throws<InvalidOperationException>(() => ExchangeRegistry.Register(77, owner => new ProbeExchange()))
                && Throws<InvalidOperationException>(() => ExchangeRegistry.Register(1, owner => new ProbeExchange()))
                && Throws<ArgumentNullException>(() => ExchangeRegistry.Register(78, null)), "Duplicate or empty registrations were accepted");
            ExchangeRegistry.Register(76, owner => { throw new InvalidOperationException("fabrique en échec"); });
            ExchangeRegistry.Register(75, owner => new FaultyExchange());

            using (var account = new Accounts(new AccountConfig("synthetic-exchange", "synthetic", "loopback")))
            {
                account.Logger.log_event += (entry, color) => logs.Enqueue(entry.message);
                Task<Socket> accept = listener.AcceptSocketAsync();
                Complete(account.Connexion.ConnectToServer(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port)); Complete(accept);
                using (Socket peer = accept.Result)
                {
                    peer.ReceiveTimeout = 6000;
                    account.Game.character.SetPerso_Data(42, "Personnage de test", 25, 0, 8);
                    account.Game.character.Kamas = 1000;
                    Feed(account, "GDM|900091|date|key"); Check(Read(peer) == "GI", "GDM did not request GI");
                    Feed(account, "GM|+0;1;0;42;Personnage de test;1;100^100;0|+5;1;0;-8;100;-4;1^100;0|+3;1;0;43;Partenaire fictif;1;10^100;0|+4;1;0;44;Importun fictif;1;10^100;0|+6;1;0;45;Curieux fictif;1;10^100;0");
                    account.AccountStates = AccountStates.CONNECTED_INACTIVE;
                    Feed(account, "OAKO3~7d1~5~~7d#5#0#0#;3e9~7d2~1~1~;3ea~7d3~2~~");
                    InventoryClass bag = account.Game.character.Inventory;
                    Check(bag.GetByInventoryId(3)?.Qua == 5 && bag.GetByInventoryId(1001)?.IsEquipped() == true, "Synthetic inventory was not read");

                    InteractionsClass windows = account.Game.Interactions;
                    ExchangeRegistry registry = windows.Exchanges;
                    PlayerExchange exchange = windows.Exchange;
                    StorageExchange storage = windows.Storage;
                    Check(registry.For(0) == windows.Shop && registry.For(1) == exchange && registry.For(2) == exchange && registry.For(5) == storage
                        && registry.Get<StorageExchange>() == storage && registry.For(99) == null, "Registry does not map the F4 windows");

                    // Demande reçue sans interface : refus poli, jamais d'acceptation automatique.
                    Feed(account, "ERK43|42|1");
                    Check(Read(peer) == "EV" && exchange.PendingRequest == null, "Unanswered ERK was not refused with EV");

                    // Demande reçue prise en charge : plus d'EV automatique ; EA sur acceptation, ECK1 sans données ouvre l'échange.
                    var asked = new List<ExchangeRequest>();
                    EventHandler<ExchangeRequestEventArgs> answer = (s, e) => { asked.Add(e.Request); e.Handled = true; };
                    exchange.RequestReceived += answer;
                    Feed(account, "ERK43|42|1");
                    NoPacket(peer, "Handled ERK still sent an automatic EV");
                    ExchangeRequest incoming = exchange.PendingRequest;
                    Check(asked.Count == 1 && incoming != null && !incoming.Outgoing && incoming.PartnerId == 43 && incoming.PartnerName == "Partenaire fictif",
                        "ERK did not publish the incoming request");
                    Check(!Sent(exchange.AddItemAsync(3, 1)) && !Sent(exchange.ValidateAsync()), "Exchange actions accepted before ECK");
                    Check(Sent(exchange.AcceptAsync()) && Read(peer) == "EA" && !exchange.IsOpen, "Accept does not send EA or opened locally");
                    Feed(account, "ECK1");
                    Check(exchange.IsOpen && exchange.ExchangeType == 1 && exchange.PartnerId == 43 && exchange.PartnerName == "Partenaire fictif"
                        && exchange.PendingRequest == null && registry.Current == exchange && account.AccountStates == AccountStates.EXCHANGE,
                        "ECK1 did not open the player exchange with the requester");

                    // Mouvements : envois EMO±/EMG, colonnes mises à jour seulement par EMK/EmK.
                    Check(Sent(exchange.AddItemAsync(3, 2)) && Read(peer) == "EMO+3|2" && exchange.LocalItems.Count == 0, "Adding does not send EMO+<id>|<qty> or changed the column locally");
                    Check(!Sent(exchange.AddItemAsync(3, 9)) && !Sent(exchange.AddItemAsync(1001, 1)) && !Sent(exchange.AddItemAsync(4242, 1)), "Invalid offers were accepted");
                    NoPacket(peer, "Refused offers reached the server");
                    Feed(account, "EMKO+3|2");
                    ExchangeItem offered = exchange.LocalItems.SingleOrDefault();
                    Check(offered != null && offered.Id == 3 && offered.Quantity == 2 && offered.TemplateId == 2001 && offered.Name == "Potion fictive", "EMKO+ was not read into the local column");
                    Check(!Sent(exchange.AddItemAsync(3, 4)), "Offer above the remaining quantity was accepted");
                    Check(Sent(exchange.AddItemAsync(3, 3)) && Read(peer) == "EMO+3|3", "Second offer was not sent");
                    Feed(account, "EMKO+3|5"); Check(exchange.LocalItems.Single().Quantity == 5, "EMKO+ total quantity was not applied");
                    Check(Sent(exchange.RemoveItemAsync(3, 1)) && Read(peer) == "EMO-3|1" && !Sent(exchange.RemoveItemAsync(1002, 1)), "Removing does not send EMO-<id>|<qty>");
                    Feed(account, "EMKO+3|4"); Check(exchange.LocalItems.Single().Quantity == 4, "Removal confirmation was not applied");
                    Feed(account, "EmKO+77|3|2002|7d#5#0#0#");
                    ExchangeItem distant = exchange.DistantItems.SingleOrDefault();
                    Check(distant != null && distant.Id == 77 && distant.Quantity == 3 && distant.TemplateId == 2002 && distant.Name == "Épée fictive" && distant.Effects == "7d#5#0#0#",
                        "EmKO+ was not read into the distant column");
                    Feed(account, "EmKG150"); Check(exchange.DistantKamas == 150, "EmKG was not read");
                    Feed(account, "EmKO-77"); Check(exchange.DistantItems.Count == 0, "EmKO- did not remove the distant item");
                    Check(Sent(exchange.SetKamasAsync(5000)) && Read(peer) == "EMG1000", "Kamas offer is not capped to the character kamas");
                    Check(!Sent(exchange.SetKamasAsync(-1)), "Negative kamas offer accepted");
                    Feed(account, "EMKG1000"); Check(exchange.LocalKamas == 1000, "EMKG was not read");
                    Check(Sent(exchange.SetKamasAsync(200)) && Read(peer) == "EMG200", "Second kamas offer was not sent");
                    Feed(account, "EMKG200"); Check(exchange.LocalKamas == 200, "EMKG update was not read");

                    // Validation : refusée pendant 3 s après une modification, puis EK ; l'état « prêt » attend EK1<id>.
                    Check(!exchange.CanValidate && exchange.ValidationWait > TimeSpan.Zero && !Sent(exchange.ValidateAsync()), "Validation allowed right after a change");
                    NoPacket(peer, "Early validation reached the server");
                    PumpUntil(() => exchange.CanValidate, 5);
                    Check(Sent(exchange.ValidateAsync()) && Read(peer) == "EK" && !exchange.LocalReady, "EK is not sent or the ready state changed locally");
                    Feed(account, "EK142"); Check(exchange.LocalReady && !exchange.DistantReady, "EK1<self> was not read");
                    Feed(account, "EK143"); Check(exchange.DistantReady, "EK1<partner> was not read");
                    Feed(account, "EK042"); Check(!exchange.LocalReady && exchange.DistantReady, "EK0<self> was not read");

                    // Paquets mal formés : journalisés, jamais propagés, état inchangé.
                    int before = logs.Count;
                    foreach (string packet in new[] { "EMKO+abc|2", "EMKOx", "EMK", "EmKO+5", "EmKO+5|2", "EmKGx", "EMKG-5", "EKx", "EK", "ERKbad", "ERK1|2" })
                        Feed(account, packet);
                    Check(exchange.IsOpen && exchange.LocalItems.Single().Quantity == 4 && exchange.LocalKamas == 200 && exchange.DistantKamas == 150 && exchange.DistantReady,
                        "Malformed packets changed the exchange");
                    Check(logs.Count >= before + 9 && !logs.Any(entry => entry.Contains("non appliqué")), "Malformed packets were not logged or raised an exception");
                    NoPacket(peer, "Malformed packets triggered a reply");

                    Feed(account, "EVa");
                    Check(!exchange.IsOpen && exchange.ExchangeType == -1 && exchange.LocalItems.Count == 0 && exchange.LocalKamas == 0 && registry.Current == null
                        && account.AccountStates == AccountStates.CONNECTED_INACTIVE && exchange.LastMessage.Contains("effectué"), "EVa did not close the exchange");

                    // Demande envoyée : ER1, attente, refus de l'autre (EV), puis ECK1|<id>.
                    Check(!Sent(exchange.RequestAsync(42)) && !Sent(exchange.RequestAsync(0)), "Request to self or nobody accepted");
                    Check(Sent(exchange.RequestAsync(43)) && Read(peer) == "ER1|43", "Request does not send ER1|<player>");
                    Feed(account, "ERK42|43|1");
                    Check(exchange.PendingRequest != null && exchange.PendingRequest.Outgoing && exchange.PendingRequest.PartnerId == 43 && asked.Count == 1,
                        "Own ERK was not stored as an outgoing request");
                    Check(!Sent(exchange.RequestAsync(43)), "Second request accepted while one is pending");
                    Feed(account, "EV"); Check(exchange.PendingRequest == null && !exchange.IsOpen && exchange.LastMessage.Contains("refusée"), "EV did not end the outgoing request");
                    Check(Sent(exchange.RequestAsync(43)) && Read(peer) == "ER1|43", "Request after a refusal was not sent");
                    Feed(account, "ERK42|43|1"); Feed(account, "ECK1|43");
                    Check(exchange.IsOpen && exchange.PartnerId == 43 && exchange.PendingRequest == null, "ECK1|<id> did not open the requested exchange");
                    Check(Sent(exchange.LeaveAsync()) && Read(peer) == "EV" && exchange.IsOpen, "Leaving does not send EV or closed before the server");
                    Feed(account, "EV"); Check(!exchange.IsOpen && exchange.LastMessage.Contains("annulé"), "EV did not cancel the exchange");

                    // Annulation d'une demande envoyée, refus du serveur (ERE), joueur ignoré, type non pris en charge.
                    Check(Sent(exchange.RequestAsync(43)) && Read(peer) == "ER1|43", "Request was not sent");
                    Feed(account, "ERK42|43|1");
                    Check(Sent(exchange.LeaveAsync()) && Read(peer) == "EV" && exchange.PendingRequest == null, "Cancelling a pending request does not send EV and forget it");
                    Check(Sent(exchange.RequestAsync(43)) && Read(peer) == "ER1|43", "Request was not sent");
                    Feed(account, "EREO"); Check(registry.LastMessage.Length > 0 && exchange.PendingRequest == null && !exchange.IsOpen, "ERE was not reported");
                    Feed(account, "ERK44|42|1"); NoPacket(peer, "Handled request answered automatically");
                    Check(Sent(exchange.IgnoreAsync()) && Read(peer) == "EV" && exchange.IgnoredPlayers.Contains("Importun fictif"), "Ignore does not refuse and remember the player");
                    Feed(account, "ERK44|42|1"); Check(Read(peer) == "EV" && asked.Count == 2 && exchange.PendingRequest == null, "Ignored player was asked again");
                    Feed(account, "ERK43|42|12"); Check(Read(peer) == "EV" && asked.Count == 2, "Unsupported request type was not refused");
                    Feed(account, "ERK43|44|1"); NoPacket(peer, "Request between two other players was answered");
                    Check(exchange.PendingRequest == null, "Request between two other players was stored");
                    Feed(account, "ERK43|42|1"); Check(Sent(exchange.RefuseAsync()) && Read(peer) == "EV" && exchange.PendingRequest == null, "Refuse does not send EV");
                    exchange.RequestReceived -= answer;

                    // Échange avec un PNJ (type 2) : même fenêtre, l'acteur de EK peut manquer.
                    Check(Sent(exchange.RequestNpcAsync(-8)) && Read(peer) == "ER2|-8", "NPC exchange does not send ER2|<npc>");
                    Feed(account, "ECK2|-8");
                    Check(exchange.IsOpen && exchange.ExchangeType == 2 && exchange.PartnerIsNpc && exchange.PartnerName == "Marchand fictif", "ECK2 did not open the NPC exchange");
                    Feed(account, "EK1"); Check(exchange.DistantReady && !exchange.LocalReady, "EK1 without actor was not read as the NPC side");
                    Feed(account, "EV"); Check(!exchange.IsOpen && account.AccountStates == AccountStates.CONNECTED_INACTIVE, "EV did not close the NPC exchange");

                    // Coffre : ECK5 puis EL (fiches hexadécimales O…;; et G), EMO±/EMG± envoyés, EsK appliqués.
                    Feed(account, "ECK5|");
                    Check(storage.IsOpen && !storage.ContentReceived && registry.Current == storage && account.AccountStates == AccountStates.STORAGE && !exchange.IsOpen && !windows.Shop.IsOpen,
                        "ECK5 did not open the storage");
                    Feed(account, "ELO3eb~7d1~a~~7d#5#0#0#;;O3ec~7d3~1~~;;Xjunk;G250");
                    Check(storage.ContentReceived && storage.Items.Count == 2 && storage.Kamas == 250 && storage.Items[0].Id == 1003 && storage.Items[0].Quantity == 10
                        && storage.Items[0].Name == "Potion fictive" && storage.Items[1].TemplateId == 2003, "Storage EL was not read");
                    Check(Sent(storage.DepositAsync(3, 2)) && Read(peer) == "EMO+3|2", "Deposit does not send EMO+<id>|<qty>");
                    Check(!Sent(storage.DepositAsync(3, 9)) && !Sent(storage.DepositAsync(1001, 1)) && !Sent(storage.WithdrawAsync(4242, 1)) && !Sent(storage.WithdrawAsync(1004, 2)),
                        "Invalid storage moves were accepted");
                    Check(Sent(storage.WithdrawAsync(1004, 1)) && Read(peer) == "EMO-1004|1", "Withdraw does not send EMO-<id>|<qty>");
                    Check(Sent(storage.DepositKamasAsync(100)) && Read(peer) == "EMG100" && Sent(storage.WithdrawKamasAsync(50)) && Read(peer) == "EMG-50", "Kamas moves do not send EMG±");
                    Check(!Sent(storage.DepositKamasAsync(0)) && !Sent(storage.DepositKamasAsync(5000)) && !Sent(storage.WithdrawKamasAsync(251)), "Invalid kamas moves were accepted");
                    NoPacket(peer, "Refused storage moves reached the server");
                    Check(storage.Items.Count == 2 && storage.Kamas == 250, "Storage changed before the server answer");
                    Feed(account, "EsKO+1005|2|2001|"); Feed(account, "EsKO+1003|12|2001|7d#5#0#0#"); Feed(account, "EsKO-1004"); Feed(account, "EsKG300");
                    Check(storage.Items.Count == 2 && storage.Items.Single(item => item.Id == 1003).Quantity == 12 && storage.Items.Any(item => item.Id == 1005 && item.Quantity == 2)
                        && storage.Kamas == 300, "EsK movements were not applied");
                    foreach (string packet in new[] { "EsKO+zz|1|1|", "EsKO+9|x|1|", "EsKO+9|1", "EsKG-4", "EsKX", "EsK", "EMKO+3|1" }) Feed(account, packet);
                    Check(storage.Items.Count == 2 && storage.Kamas == 300 && !logs.Any(entry => entry.Contains("non appliqué")), "Malformed storage packets changed the storage");
                    Feed(account, "EV"); Check(!storage.IsOpen && storage.Items.Count == 0 && account.AccountStates == AccountStates.CONNECTED_INACTIVE && registry.Current == null,
                        "EV did not close the storage");

                    // Type sans fenêtre : journal et EV propre ; paquets hors échange ignorés.
                    Feed(account, "ECK99|x");
                    Check(Read(peer) == "EV" && registry.Current == null && registry.LastMessage.Contains("ECK99") && logs.Any(entry => entry.Contains("ECK99"))
                        && account.AccountStates == AccountStates.CONNECTED_INACTIVE, "Unsupported ECK type was not logged and closed");
                    Feed(account, "EV"); Feed(account, "ECKabc"); Check(Read(peer) == "EV", "Unreadable ECK was not closed");
                    foreach (string packet in new[] { "EMKO+3|2", "EmKG5", "EsKG5", "EK143", "Ew10;100", "EL2001;;5|", "EV" }) Feed(account, packet);
                    NoPacket(peer, "Packets outside an exchange triggered a reply");
                    Check(!exchange.IsOpen && !storage.IsOpen && !windows.Shop.IsOpen && windows.Shop.Articles.Count == 0, "Packets outside an exchange opened a window");

                    // Fenêtre extérieure : ECK77 la crée pour ce compte et lui transmet les paquets E jusqu'à EV.
                    Feed(account, "ECK77|abc"); Feed(account, "EL1;2"); Feed(account, "EsKG9"); Feed(account, "Ew10;100"); Feed(account, "EVa");
                    Check(probes.Count == 1 && string.Join(",", probes[0].Calls) == "C77:abc,L1;2,sG9,w10;100,Va" && registry.Current == null, "External exchange window was not dispatched");

                    // Fenêtres extérieures défaillantes : fabrique en échec (le compte a quand même été créé) → EV ; exception sur EL journalisée.
                    Feed(account, "ECK76"); Check(Read(peer) == "EV" && registry.Current == null && logs.Any(entry => entry.Contains("fabrique en échec")), "Failing factory was not refused");
                    Feed(account, "ECK75"); Feed(account, "ELx"); Check(registry.Current is FaultyExchange && registry.Current.IsOpen && logs.Any(entry => entry.Contains("liste refusée")),
                        "Exception of an external window was not contained");
                    Feed(account, "EV"); Check(registry.Current == null, "EV did not close the external window");
                    NoPacket(peer, "External window failures sent extra packets");

                    // Clear (déconnexion) : la fenêtre ouverte est oubliée sans paquet.
                    Feed(account, "ECK5|"); windows.Clear();
                    Check(!storage.IsOpen && registry.Current == null && account.AccountStates == AccountStates.CONNECTED_INACTIVE && probes[0].Calls.Last() == "clear", "Clear kept an open exchange");
                    NoPacket(peer, "Clear sent a packet");

                    // Textes du client et icônes d'objets synthétiques, dans un dossier temporaire.
                    string lang = Path.Combine(folder, "BotLang"); Directory.CreateDirectory(lang);
                    Write(lang, "items.xml", "items", "<objet id=\"2001\" nom=\"Potion fictive\" type=\"12\" gfx=\"7\" niveau=\"1\" description=\"\" />");
                    Write(lang, "lang.xml", "lang", "<texte cle=\"EXCHANGE\" valeur=\"Échange fictif\" />\n<texte cle=\"A_WANT_EXCHANGE\" valeur=\"%1 te propose un échange (texte de test).\" />");
                    Check(LangData.Load(lang) == 2, "Synthetic texts were not loaded");
                    string icons = Path.Combine(folder, "Items"); Directory.CreateDirectory(Path.Combine(icons, "12"));
                    Color iconColor = Color.FromArgb(255, 200, 30, 160);
                    using (var icon = new Bitmap(32, 32)) { using (var graphics = Graphics.FromImage(icon)) graphics.Clear(iconColor); icon.Save(Path.Combine(icons, "12", "7.png"), ImageFormat.Png); }
                    ExchangeItemIcons.Folder = icons;
                    Check(ExchangeItemIcons.PathOf(2001) == Path.Combine(icons, "12", "7.png") && ExchangeItemIcons.PathOf(2003) == null && ExchangeItemIcons.TryGet(2003) == null,
                        "Item icon paths do not follow Items/<type>/<gfx>.png");
                    PumpUntil(() => ExchangeItemIcons.TryGet(2001) != null);
                    Check(ExchangeItemIcons.TryGet(2001).Width == 32, "Item icon was not loaded");

                    using (var form = new GameClientFullform(account))
                    {
                        form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); Application.DoEvents();
                        var drawer = form.Panels; var panel = drawer.Get<ExchangePanel>(); var chest = drawer.Get<StoragePanel>();
                        Check(panel != null && chest != null && drawer.Current == null, "Exchange panels are not registered in the drawer");

                        // Demande reçue sur un fil du réseau, tiroir jamais affiché : boîte Oui / Non / Ignorer avec le texte du client,
                        // aucune réponse avant le choix.
                        FeedFromNetwork(account, "ERK43|42|1");
                        PumpUntil(() => panel.RequestDialog != null);
                        NoPacket(peer, "The exchange panel let the request be refused automatically");
                        Form box = panel.RequestDialog;
                        Check(box != null && BotDialogs.OpenDialogs.Contains(box) && box.Text == "Échange fictif"
                            && All(box).Any(control => control.Name == "dialog-message" && control.Text == "Partenaire fictif te propose un échange (texte de test).")
                            && All(box).OfType<Button>().Select(button => button.Text).OrderBy(text => text).SequenceEqual(new[] { "Ignorer", "Non", "Oui" }),
                            "Request dialog is missing or differs from CAUTION_YESNOIGNORE");
                        Answer(box, "Non"); Check(Read(peer) == "EV", "Non does not send EV");
                        PumpUntil(() => exchange.PendingRequest == null && panel.RequestDialog == null);
                        Feed(account, "ERK43|42|1"); Application.DoEvents();
                        Answer(panel.RequestDialog, "Oui"); Check(Read(peer) == "EA", "Oui does not send EA");
                        Check(drawer.Current != panel, "Exchange panel opened before ECK1");
                        FeedFromNetwork(account, "ECK1"); PumpUntil(() => drawer.Current == panel);
                        Check(drawer.Visible && drawer.Current == panel && ((Label)Get(panel, "exchangeHeading")).Text.Contains("Partenaire fictif"), "Exchange panel did not open on ECK1");

                        var bagList = (ListView)Get(panel, "exchangeBag");
                        var localGrid = (ExchangeItemGrid)Get(panel, "localGrid"); var distantGrid = (ExchangeItemGrid)Get(panel, "distantGrid");
                        var add = (Control)Get(panel, "exchangeAdd"); var remove = (Control)Get(panel, "exchangeRemove");
                        var validate = (Control)Get(panel, "exchangeValidate");
                        Check(bagList.Items.Count == 2 && RowWithTag(bagList, 3u) != null && RowWithTag(bagList, 1001u) == null, "Bag list must show only unequipped items");
                        Check(!add.Enabled && !remove.Enabled, "Offer buttons enabled without a selection");
                        RowWithTag(bagList, 3u).Selected = true; Application.DoEvents(); Check(add.Enabled, "Offer button disabled for a bag item");
                        ((NumericUpDown)Get(panel, "exchangeQuantity")).Value = 2;
                        Click(add); Check(Read(peer) == "EMO+3|2", "Offer button does not send EMO+<id>|<qty>");
                        Feed(account, "EMKO+3|2"); Application.DoEvents();
                        Check(localGrid.Items.Count == 1 && localGrid.Items[0].Id == 3 && RowWithTag(bagList, 3u).SubItems[1].Text == "3", "Local grid or remaining quantity not refreshed");
                        Feed(account, "EmKO+77|1|2002|"); Feed(account, "EmKG40"); Application.DoEvents();
                        Check(distantGrid.Items.Count == 1 && ((Label)Get(panel, "distantKamas")).Text.StartsWith("40 ") && ((Label)Get(panel, "distantTitle")).Text.StartsWith("Partenaire fictif"),
                            "Distant column not refreshed");
                        ((NumericUpDown)Get(panel, "exchangeKamas")).Value = 150;
                        Click(Get(panel, "exchangeKamasSend")); Check(Read(peer) == "EMG150", "Kamas button does not send EMG<kamas>");
                        Feed(account, "EMKG150"); Application.DoEvents();
                        Check(((Label)Get(panel, "localKamas")).Text.StartsWith("150 "), "Local kamas not shown");
                        Check(!validate.Enabled && validate.Text.StartsWith("Valider ("), "Validate button not delayed after a change");
                        PumpUntil(() => validate.Enabled, 5); Check(validate.Text == "Valider", "Countdown text was not cleared");
                        Click(validate); Check(Read(peer) == "EK", "Validate button does not send EK");
                        Feed(account, "EK142"); Application.DoEvents();
                        Check(localGrid.Ready && ((Label)Get(panel, "localTitle")).Text.Contains("prêt"), "EK1<self> not shown");
                        using (var image = new Bitmap(localGrid.Width, localGrid.Height))
                        {
                            localGrid.DrawToBitmap(image, new Rectangle(Point.Empty, localGrid.Size));
                            Check(Near(image.GetPixel(1, localGrid.Height / 2), Color.FromArgb(106, 118, 67)), "Ready border is not olive");
                            Check(Near(image.GetPixel(4 + ExchangeItemGrid.CellSize / 2, 4 + ExchangeItemGrid.CellSize / 2), iconColor), "Item icon is not drawn in its cell");
                        }
                        Check(localGrid.SelectItem(3) && remove.Enabled, "Selecting an offered item does not enable Retirer");
                        ((NumericUpDown)Get(panel, "exchangeQuantity")).Value = 1;
                        Click(remove); Check(Read(peer) == "EMO-3|1", "Remove button does not send EMO-<id>|<qty>");
                        Click(Get(panel, "exchangeCancel")); Check(Read(peer) == "EV" && drawer.Current == panel, "Cancel does not send EV or closed before the server");
                        Feed(account, "EV"); Application.DoEvents();
                        Check(!drawer.IsOpen(panel) && localGrid.Items.Count == 0 && distantGrid.Items.Count == 0, "Exchange panel stayed open after EV");

                        // La boîte disparaît quand la demande est annulée ; Ignorer refuse puis écarte le joueur.
                        Feed(account, "ERK43|42|1"); Application.DoEvents();
                        box = panel.RequestDialog; Check(box != null, "Second request dialog missing");
                        Feed(account, "EV"); PumpUntil(() => panel.RequestDialog == null && !BotDialogs.OpenDialogs.Contains(box));
                        NoPacket(peer, "Closing the dialog of a cancelled request sent a packet");
                        Feed(account, "ERK44|42|1"); Check(Read(peer) == "EV" && panel.RequestDialog == null, "Player ignored in the protocol section was asked again");
                        Feed(account, "ERK45|42|1"); Application.DoEvents();
                        Check(panel.RequestDialog != null, "Request dialog missing for a new player");
                        Answer(panel.RequestDialog, "Ignorer"); Check(Read(peer) == "EV", "Ignorer does not send EV");
                        PumpUntil(() => panel.RequestDialog == null && exchange.IgnoredPlayers.Contains("Curieux fictif"));
                        Feed(account, "ERK45|42|1"); Check(Read(peer) == "EV" && panel.RequestDialog == null, "Player ignored from the dialog was asked again");

                        // Demande envoyée : le volet attend la réponse ; × annule (EV) et le ferme.
                        Check(Sent(exchange.RequestAsync(43)) && Read(peer) == "ER1|43", "Request was not sent");
                        Feed(account, "ERK42|43|1"); Application.DoEvents();
                        Check(drawer.Current == panel && ((Label)Get(panel, "exchangeHeading")).Text.Contains("Partenaire fictif"), "Outgoing request is not shown");
                        drawer.RequestClose(panel); Check(Read(peer) == "EV", "Closing the waiting panel does not cancel the request");
                        PumpUntil(() => !drawer.IsOpen(panel) && exchange.PendingRequest == null);

                        // Volet Coffre : ECK5 + EL, dépôt, retrait, kamas, fermeture.
                        Feed(account, "ECK5|"); Feed(account, "ELO3eb~7d1~a~~;;O3ec~7d3~1~~;;G250"); Application.DoEvents();
                        Check(drawer.Current == chest, "Storage panel did not open on ECK5/EL");
                        var chestGrid = (ExchangeItemGrid)Get(chest, "storageGrid"); var chestBag = (ListView)Get(chest, "storageBag");
                        Check(chestGrid.Items.Count == 2 && ((Label)Get(chest, "storageKamas")).Text.StartsWith("250 ") && RowWithTag(chestBag, 3u) != null && RowWithTag(chestBag, 1001u) == null,
                            "Storage panel does not show the storage and the bag");
                        var withdraw = (Control)Get(chest, "storageWithdraw"); var deposit = (Control)Get(chest, "storageDeposit");
                        Check(!withdraw.Enabled && !deposit.Enabled, "Storage buttons enabled without a selection");
                        RowWithTag(chestBag, 3u).Selected = true; Application.DoEvents();
                        Click(deposit); Check(Read(peer) == "EMO+3|1", "Deposit button does not send EMO+");
                        Check(chestGrid.SelectItem(1004), "Storage item cannot be selected"); Application.DoEvents();
                        Click(withdraw); Check(Read(peer) == "EMO-1004|1", "Withdraw button does not send EMO-");
                        ((NumericUpDown)Get(chest, "storageKamasAmount")).Value = 5;
                        Click(Get(chest, "storageKamasDeposit")); Check(Read(peer) == "EMG5", "Kamas deposit button does not send EMG");
                        Click(Get(chest, "storageKamasWithdraw")); Check(Read(peer) == "EMG-5", "Kamas withdraw button does not send EMG-");
                        Feed(account, "EsKO-1004"); Application.DoEvents(); Check(chestGrid.Items.Count == 1, "EsKO- not shown");
                        Click(Get(chest, "storageLeave")); Check(Read(peer) == "EV" && drawer.Current == chest, "Close does not send EV");
                        Feed(account, "EV"); Application.DoEvents();
                        Check(!drawer.IsOpen(chest) && chestGrid.Items.Count == 0, "Storage panel stayed open after EV");

                        Feed(account, "ECK99|x"); Check(Read(peer) == "EV", "Unsupported type not closed with the form open");
                        Application.DoEvents(); Check(drawer.Current == null || !(drawer.Current is ExchangePanel || drawer.Current is StoragePanel), "Unsupported type opened a panel");
                        form.Close();
                    }
                    Check(BotDialogs.OpenDialogs.Count == 0, "A dialog survived the game window");
                }
            }
        }
        finally
        {
            LangData.Clear();
            ExchangeItemIcons.Folder = null;
            listener.Stop(); Environment.CurrentDirectory = previous;
        }
    }
}
