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
// depuis un dossier temporaire avec des textes LangData synthétiques). Lot X4 : échange de familiers (type 9) ouvert par
// ECK9 après ER9 (garde) ou ER17 (résurrection), EMO±/EMG/EK, proposition du PNJ EmKO+ et EK1 sans acteur, EVa ; ECK17
// (lot X4-CORR : le EREO que StarLoco envoie juste après le ECK9 d'un ER17 est ignoré une seule fois, celui d'un ER9 reste une erreur) ;
// reste refermé par EV ; volet Familiers.
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
        try { Application.EnableVisualStyles(); Run(); Console.WriteLine("OK: registre ECK, ERK/EA/EV, échange EMO/EMG/EK, coffre EL/EsK, type inconnu refermé, volets Échange et Coffre, familiers ER9/ER17 → ECK9 et volet Familiers"); }
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
    /// <summary>Seule boîte de quantité ouverte (lot X12).</summary>
    private static QuantityPopup Quantity()
    {
        PumpUntil(() => QuantityPopup.OpenPopups.Count == 1);
        return QuantityPopup.OpenPopups[0];
    }
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
        var debug = new ConcurrentQueue<string>();
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
                account.Logger.debug_event += entry => debug.Enqueue(entry.message);
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
                    Feed(account, "ERK43|42|8"); Check(Read(peer) == "EV" && asked.Count == 2, "Unsupported request type was not refused");
                    // Lot X5 : les demandes d'artisanat sécurisé (12 : un client sollicite l'artisan, 13 : un artisan invite) sont posées, jamais acceptées seules.
                    Feed(account, "ERK43|42|12"); NoPacket(peer, "Secure craft request (type 12) was answered automatically");
                    Check(asked.Count == 3 && asked[2].Type == 12 && asked[2].IsSecureCraft && !asked[2].Outgoing && asked[2].Text.Contains("Partenaire fictif")
                        && asked[2].Text.Contains("artisan") && ReferenceEquals(exchange.PendingRequest, asked[2]), "Secure craft request (type 12) differs: " + asked[2]?.Text);
                    Check(Sent(exchange.RefuseAsync()) && Read(peer) == "EV" && exchange.PendingRequest == null, "Refusing the secure craft request does not send EV");
                    Feed(account, "ERK43|42|13"); NoPacket(peer, "Secure craft request (type 13) was answered automatically");
                    Check(asked.Count == 4 && asked[3].Type == 13 && asked[3].IsSecureCraft && asked[3].Text.Contains("atelier"), "Secure craft request (type 13) differs: " + asked[3]?.Text);
                    Check(Sent(exchange.RefuseAsync()) && Read(peer) == "EV" && exchange.PendingRequest == null, "Refusing the secure craft invitation does not send EV");
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

                    // Familiers (lot X4) : seul le type 9 est enregistré ; rien ne part avant ECK9.
                    PetsExchange pets = registry.Get<PetsExchange>();
                    Check(pets != null && ExchangeRegistry.IsRegistered(ExchangeTypes.Pets) && !ExchangeRegistry.IsRegistered(ExchangeTypes.PetsResurrection) && registry.For(9) == pets,
                        "Pets exchange must be registered for type 9 only");
                    Check(!Sent(pets.AddItemAsync(3, 1)) && !Sent(pets.SetKamasAsync(1)) && !Sent(pets.ValidateAsync()) && !Sent(pets.LeaveAsync()), "Pets exchange actions accepted before ECK9");
                    NoPacket(peer, "Closed pets exchange sent a packet");
                    // Garde : action 4 du menu du PNJ → ER9|<pnj>, StarLoco répond ECK9|<pnj>.
                    Check(Sent(windows.Npc.RequestActionAsync(NpcActions.Pets, -8)) && Read(peer) == "ER9|-8", "Pet keeping does not send ER9|<npc>");
                    Feed(account, "ECK9|-8");
                    Check(pets.IsOpen && registry.Current == pets && pets.Service == PetsService.Keeping && pets.NpcId == -8 && pets.NpcName == "Marchand fictif"
                        && account.AccountStates == AccountStates.EXCHANGE && pets.ServiceHint.StartsWith("Garde"), "ECK9 after ER9 did not open the pet keeping: " + pets.Service + " / " + pets.NpcName);
                    // Lot X4-CORR : un EREO après le ECK9 d'un ER9 reste une erreur (StarLoco ne l'envoie que pour ER17).
                    int reported = logs.Count(entry => entry.Contains("(EREO)"));
                    Feed(account, "EREO"); Check(logs.Count(entry => entry.Contains("(EREO)")) == reported + 1 && pets.IsOpen, "EREO after the ECK9 of ER9 must still be reported");
                    Check(!Sent(pets.ValidateAsync()) && pets.ValidationWait > TimeSpan.Zero, "Validation allowed right after ECK9");
                    Check(!Sent(pets.AddItemAsync(1001, 1)) && !Sent(pets.AddItemAsync(3, 9)) && !Sent(pets.AddItemAsync(999, 1)) && !Sent(pets.RemoveItemAsync(3, 1)),
                        "Equipped, unknown, too many or unoffered items were accepted");
                    NoPacket(peer, "Refused pets actions reached the server");
                    Check(Sent(pets.AddItemAsync(3, 1)) && Read(peer) == "EMO+3|1", "Pets offer does not send EMO+<id>|<qty>");
                    Feed(account, "EMKO+3|1");
                    Check(pets.LocalItems.Count == 1 && pets.LocalItems[0].Name == "Potion fictive" && pets.LocalItems[0].Quantity == 1 && pets.LocalItems[0].TemplateId == 2001,
                        "EMKO+ was not applied to the local side");
                    // Certificat remis par le PNJ (EmKO+<modèle>|1|<modèle>|<effets>), puis EK1 sans acteur : le PNJ accepte.
                    Feed(account, "EmKO+2002|1|2002|"); Feed(account, "EK1");
                    Check(pets.DistantItems.Count == 1 && pets.DistantItems[0].TemplateId == 2002 && pets.DistantItems[0].Name == "Épée fictive" && pets.DistantReady && !pets.LocalReady,
                        "NPC proposal or EK1 without actor was not applied to the NPC side");
                    Check(Sent(pets.SetKamasAsync(5000)) && Read(peer) == "EMG1000", "Pets kamas are not bounded by the character kamas");
                    Feed(account, "EMKG1000"); Check(pets.LocalKamas == 1000, "EMKG not applied");
                    // Paquets illisibles : journalisés, état inchangé, jamais d'exception.
                    foreach (string packet in new[] { "EMKO+x|1", "EmKO+5|1", "EmKOx", "EKz", "EMKG-4" }) Feed(account, packet);
                    Check(pets.IsOpen && pets.LocalItems.Count == 1 && pets.DistantItems.Count == 1 && pets.LocalKamas == 1000 && logs.Any(entry => entry.Contains("Paquet EmK illisible"))
                        && logs.Any(entry => entry.Contains("Paquet EK illisible")), "Malformed pets packets were not contained");
                    Feed(account, "EL1;2"); Check(pets.RawList == "1;2" && pets.IsOpen, "EL for type 9 was not kept raw");
                    Check(Sent(pets.RemoveItemAsync(3, 1)) && Read(peer) == "EMO-3|1", "Pets withdrawal does not send EMO-<id>|<qty>");
                    Feed(account, "EMKO-3"); Check(pets.LocalItems.Count == 0, "EMKO- not applied");
                    Check(Sent(pets.AddItemAsync(3, 1)) && Read(peer) == "EMO+3|1", "Second pets offer was not sent"); Feed(account, "EMKO+3|1");
                    Check(!Sent(pets.ValidateAsync()), "Validation allowed right after a change");
                    PumpUntil(() => pets.CanValidate, 5);
                    Check(Sent(pets.ValidateAsync()) && Read(peer) == "EK", "Pets validation does not send EK");
                    Feed(account, "EK142"); Check(pets.LocalReady && pets.DistantReady, "EK1<self> not applied to the local side");
                    Feed(account, "EVa");
                    Check(!pets.IsOpen && registry.Current == null && account.AccountStates == AccountStates.CONNECTED_INACTIVE && pets.LastMessage == "Garde effectuée par Marchand fictif."
                        && pets.LocalItems.Count == 0 && pets.DistantItems.Count == 0 && pets.Service == PetsService.Unknown, "EVa did not close the pet keeping: " + pets.LastMessage);
                    // Résurrection : action 7 → ER17|<pnj>, StarLoco répond aussi ECK9 ; la demande notée fixe le service.
                    Check(Sent(windows.Npc.RequestActionAsync(NpcActions.PetResurrection, -8)) && Read(peer) == "ER17|-8", "Pet resurrection does not send ER17|<npc>");
                    Feed(account, "ECK9|-8");
                    Check(pets.IsOpen && pets.Service == PetsService.Resurrection && pets.ServiceHint.StartsWith("Résurrection"), "ECK9 after ER17 did not open the resurrection");
                    // Lot X4-CORR : StarLoco envoie EREO juste après ce ECK9 (GameClient.request, branche « 17 » sans return) : ignoré une fois,
                    // journalisé en débogage, LastMessage et échange inchangés ; un second EREO est de nouveau une erreur.
                    string beforeEreo = registry.LastMessage; reported = logs.Count(entry => entry.Contains("(EREO)"));
                    Feed(account, "EREO");
                    Check(pets.IsOpen && registry.Current == pets && registry.LastMessage == beforeEreo && logs.Count(entry => entry.Contains("(EREO)")) == reported
                        && debug.Any(entry => entry.Contains("EREO reçu juste après le ECK9")), "The EREO that follows the ECK9 of ER17 was not ignored");
                    Feed(account, "EREO"); Check(logs.Count(entry => entry.Contains("(EREO)")) == reported + 1 && pets.IsOpen, "A second EREO must be reported");
                    Check(Sent(pets.LeaveAsync()) && Read(peer) == "EV" && pets.IsOpen, "Leave does not send EV or closed before the server");
                    Feed(account, "EV"); Check(!pets.IsOpen && pets.LastMessage.Length > 0 && account.AccountStates == AccountStates.CONNECTED_INACTIVE, "EV did not close the resurrection");
                    // Un paquet d'échange reçu avant le EREO (serveur corrigé) abandonne l'exception : le EREO qui suit est une erreur.
                    Check(Sent(windows.Npc.RequestActionAsync(NpcActions.PetResurrection, -8)) && Read(peer) == "ER17|-8", "Second resurrection request was not sent");
                    Feed(account, "ECK9|-8"); Feed(account, "EK1"); reported = logs.Count(entry => entry.Contains("(EREO)"));
                    Feed(account, "EREO"); Check(logs.Count(entry => entry.Contains("(EREO)")) == reported + 1, "An EREO after another exchange packet must be reported");
                    Feed(account, "EV"); Check(!pets.IsOpen, "EV did not close the second resurrection");
                    // ECK9 sans demande notée : service inconnu (aucune action de PNJ lue) ; ECK17, que StarLoco n'envoie pas, refermé par EV.
                    Feed(account, "ECK9|-8"); Check(pets.IsOpen && pets.Service == PetsService.Unknown, "ECK9 without request must leave the service unknown");
                    reported = logs.Count(entry => entry.Contains("(EREO)"));
                    Feed(account, "EREO"); Check(logs.Count(entry => entry.Contains("(EREO)")) == reported + 1, "EREO without a noted ER17 must be reported");
                    Feed(account, "EV"); Check(!pets.IsOpen, "EV did not close the unknown pets exchange");
                    Feed(account, "ECK17|-8");
                    Check(Read(peer) == "EV" && !pets.IsOpen && registry.Current == null && registry.LastMessage.Contains("ECK17"), "ECK17 must stay unsupported and be closed with EV");
                    Feed(account, "EV"); NoPacket(peer, "Pets section sent extra packets");

                    // Textes du client et icônes d'objets synthétiques, dans un dossier temporaire.
                    string lang = Path.Combine(folder, "BotLang"); Directory.CreateDirectory(lang);
                    Write(lang, "items.xml", "items", "<objet id=\"2001\" nom=\"Potion fictive\" type=\"12\" gfx=\"7\" niveau=\"1\" pods=\"3\" description=\"\" />");
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
                        // Lot X12 : boîte de quantité du client (valeur 1, maximum la part libre) au lieu des champs fixes.
                        Check(!All(panel.View).OfType<NumericUpDown>().Any(), "Exchange panel still has fixed quantity fields");
                        Click(add); var quantityBox = Quantity();
                        Check(quantityBox.Maximum == 5 && quantityBox.Minimum == 1 && quantityBox.InputText == "1", "Offer box differs (max " + quantityBox.Maximum + ", value " + quantityBox.InputText + ")");
                        quantityBox.InputText = "2"; quantityBox.Confirm(); Check(Read(peer) == "EMO+3|2", "Offer button does not send EMO+<id>|<qty>");
                        Feed(account, "EMKO+3|2"); Application.DoEvents();
                        Check(localGrid.Items.Count == 1 && localGrid.Items[0].Id == 3 && RowWithTag(bagList, 3u).SubItems[1].Text == "3", "Local grid or remaining quantity not refreshed");
                        // Glisser-déposer (Exchange.dropItem) : un objet du sac sur la grille locale, jamais un objet équipé, une case de
                        // l'autre joueur ni une case déjà proposée ; la boîte est bornée à la part encore libre.
                        Check(!localGrid.AcceptDrop(new ShortcutPayload(ShortcutSlotKind.Item, 1001, 1, 1)), "An equipped item was accepted by the offer");
                        Check(!localGrid.AcceptDrop(new ExchangeItemDrag(distantGrid, 77, 2002, 1)) && !localGrid.AcceptDrop(new ExchangeItemDrag(localGrid, 3, 2001, 2)),
                            "An exchange cell was accepted back by the offer");
                        Check(!distantGrid.AcceptDrop(new ShortcutPayload(ShortcutSlotKind.Item, 3, 5, -1)), "The distant grid accepted a drop");
                        Check(!panel.DropOnBag(new ShortcutPayload(ShortcutSlotKind.Item, 3, 5, -1)) && !panel.DropOnBag(new ExchangeItemDrag(distantGrid, 77, 2002, 1)),
                            "The bag list accepted a bag item or a distant cell");
                        Check(localGrid.AcceptDrop(new ShortcutPayload(ShortcutSlotKind.Item, 3, 5, -1)), "Bag item drop on the offer was refused");
                        quantityBox = Quantity(); Check(quantityBox.Maximum == 3, "Dropped offer box is not bounded by the part still free");
                        typeof(QuantityPopup).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(quantityBox, new object[] { new KeyEventArgs(Keys.Escape) });
                        PumpUntil(() => QuantityPopup.OpenPopups.Count == 0); NoPacket(peer, "Escape on the drop box sent a packet");
                        Check(localGrid.AcceptDrop(new ShortcutPayload(ShortcutSlotKind.Item, 3, 5, -1)), "Second drop on the offer was refused");
                        quantityBox = Quantity(); quantityBox.InputText = "40"; quantityBox.Confirm(); Check(Read(peer) == "EMO+3|3", "Dropped offer is not clamped to the part still free");
                        Feed(account, "EMKO+3|5"); Application.DoEvents();
                        Check(RowWithTag(bagList, 3u) == null, "A fully offered item stayed in the bag list");
                        Check(localGrid.AcceptDrop(new ShortcutPayload(ShortcutSlotKind.Item, 3, 5, -1)), "Drop of a fully offered item was refused before the check");
                        NoPacket(peer, "A fully offered item was offered again"); Check(QuantityPopup.OpenPopups.Count == 0, "A box opened for a fully offered item");
                        Feed(account, "EmKO+77|1|2002|"); Feed(account, "EmKG40"); Application.DoEvents();
                        Check(distantGrid.Items.Count == 1 && ((Label)Get(panel, "distantKamas")).Text.StartsWith("40 ") && ((Label)Get(panel, "distantTitle")).Text.StartsWith("Partenaire fictif"),
                            "Distant column not refreshed");
                        // Kamas (Exchange.askKamaQuantity) : valeur la somme proposée, maximum sa bourse, minimum 0.
                        Click(Get(panel, "exchangeKamasSend")); quantityBox = Quantity();
                        Check(quantityBox.InputText == "0" && quantityBox.Maximum == 1000 && quantityBox.Minimum == 0, "Kamas box differs (value " + quantityBox.InputText + ", max " + quantityBox.Maximum + ")");
                        quantityBox.InputText = "150"; quantityBox.Confirm(); Check(Read(peer) == "EMG150", "Kamas button does not send EMG<kamas>");
                        Feed(account, "EMKG150"); Application.DoEvents();
                        Check(((Label)Get(panel, "localKamas")).Text.StartsWith("150 "), "Local kamas not shown");
                        Click(Get(panel, "exchangeKamasSend")); quantityBox = Quantity(); Check(quantityBox.InputText == "150", "Kamas box does not start from the offered sum");
                        quantityBox.InputText = "5000"; quantityBox.Confirm(); Check(Read(peer) == "EMG1000", "Kamas offer is not clamped to the purse");
                        Feed(account, "EMKG150"); Application.DoEvents();
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
                        Click(remove); quantityBox = Quantity(); Check(quantityBox.Maximum == 5, "Remove box maximum is not the offered stack");
                        quantityBox.InputText = "1"; quantityBox.Confirm(); Check(Read(peer) == "EMO-3|1", "Remove button does not send EMO-<id>|<qty>");
                        // Case proposée glissée sur le sac : EMO- avec la quantité choisie.
                        Check(panel.DropOnBag(new ExchangeItemDrag(localGrid, 3, 2001, 5)), "Offered cell drop on the bag was refused");
                        quantityBox = Quantity(); quantityBox.InputText = "2"; quantityBox.Confirm(); Check(Read(peer) == "EMO-3|2", "Dropped withdraw does not send EMO-<id>|<qty>");
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
                        Check(!All(chest.View).OfType<NumericUpDown>().Any(), "Storage panel still has fixed quantity fields");
                        RowWithTag(chestBag, 3u).Selected = true; Application.DoEvents();
                        Click(deposit); var chestBox = Quantity(); Check(chestBox.Maximum == 5 && chestBox.InputText == "1", "Deposit box differs");
                        chestBox.Confirm(); Check(Read(peer) == "EMO+3|1", "Deposit button does not send EMO+");
                        Check(chestGrid.SelectItem(1004), "Storage item cannot be selected"); Application.DoEvents();
                        // Un seul exemplaire : 1 sans boîte, comme dropItem.
                        Click(withdraw); Check(Read(peer) == "EMO-1004|1" && QuantityPopup.OpenPopups.Count == 0, "Withdraw button does not send EMO- without a box");
                        // Glisser-déposer : sac → coffre (EMO+), coffre → sac (EMO-) borné aux pods libres (Ow, poids de LangData).
                        Check(!chestGrid.AcceptDrop(new ShortcutPayload(ShortcutSlotKind.Item, 1001, 1, 1)) && !chest.DropOnBag(new ShortcutPayload(ShortcutSlotKind.Item, 3, 5, -1))
                            && !chestGrid.AcceptDrop(new ExchangeItemDrag(chestGrid, 1003, 2001, 10)), "Storage accepted an equipped item or a drop in the wrong direction");
                        Check(chestGrid.AcceptDrop(new ShortcutPayload(ShortcutSlotKind.Item, 3, 5, -1)), "Bag item drop on the storage was refused");
                        chestBox = Quantity(); chestBox.InputText = "2"; chestBox.Confirm(); Check(Read(peer) == "EMO+3|2", "Dropped deposit does not send EMO+<id>|<qty>");
                        FeedFromNetwork(account, "Ow100|106"); Application.DoEvents();
                        Check(chest.DropOnBag(new ExchangeItemDrag(chestGrid, 1003, 2001, 10)), "Storage cell drop on the bag was refused");
                        chestBox = Quantity(); Check(chestBox.Maximum == 2, "Withdraw box is not bounded by the free pods (max " + chestBox.Maximum + ")");
                        chestBox.InputText = "9"; chestBox.Confirm(); Check(Read(peer) == "EMO-1003|2", "Dropped withdraw is not clamped to the free pods");
                        FeedFromNetwork(account, "Ow106|106"); Application.DoEvents();
                        Check(chest.DropOnBag(new ExchangeItemDrag(chestGrid, 1003, 2001, 10)), "Drop with a full bag was refused before the check");
                        PumpUntil(() => BotDialogs.OpenDialogs.Count == 1); Form fullBag = BotDialogs.OpenDialogs[0];
                        Check(QuantityPopup.OpenPopups.Count == 0 && All(fullBag).Any(control => control.Name == "dialog-message" && control.Text.Contains("maximum")),
                            "A full bag did not show SRV_MSG_6");
                        Answer(fullBag, "OK"); PumpUntil(() => BotDialogs.OpenDialogs.Count == 0); NoPacket(peer, "A full bag still sent EMO-");
                        FeedFromNetwork(account, "Ow0|1000");
                        // Kamas (GridInventoryViewer.askKamaQuantity) : valeur et maximum tous les kamas du côté d'où ils partent.
                        Click(Get(chest, "storageKamasDeposit")); chestBox = Quantity();
                        Check(chestBox.InputText == "1000" && chestBox.Maximum == 1000, "Kamas deposit box differs (value " + chestBox.InputText + ")");
                        chestBox.InputText = "5"; chestBox.Confirm(); Check(Read(peer) == "EMG5", "Kamas deposit button does not send EMG");
                        Click(Get(chest, "storageKamasWithdraw")); chestBox = Quantity();
                        Check(chestBox.InputText == "250" && chestBox.Maximum == 250, "Kamas withdraw box differs (value " + chestBox.InputText + ")");
                        chestBox.InputText = "5"; chestBox.Confirm(); Check(Read(peer) == "EMG-5", "Kamas withdraw button does not send EMG-");
                        Click(Get(chest, "storageKamasWithdraw")); chestBox = Quantity(); chestBox.InputText = "999"; chestBox.Confirm();
                        Check(Read(peer) == "EMG-250", "Kamas withdraw is not clamped to the storage");
                        Click(Get(chest, "storageKamasDeposit")); chestBox = Quantity(); chestBox.InputText = "0"; chestBox.Confirm();
                        PumpUntil(() => QuantityPopup.OpenPopups.Count == 0); NoPacket(peer, "Zero kamas sent EMG");
                        Feed(account, "EsKO-1004"); Application.DoEvents(); Check(chestGrid.Items.Count == 1, "EsKO- not shown");
                        Click(Get(chest, "storageLeave")); Check(Read(peer) == "EV" && drawer.Current == chest, "Close does not send EV");
                        Feed(account, "EV"); Application.DoEvents();
                        Check(!drawer.IsOpen(chest) && chestGrid.Items.Count == 0, "Storage panel stayed open after EV");

                        // Volet Familiers (lot X4) : ER9 puis ECK9 l'ouvre ; Proposer → EMO+, certificat du PNJ et EK1, Valider après le
                        // délai → EK, EVa le ferme ; ER17 puis ECK9 → « Résurrection chez … », × envoie EV et attend la réponse.
                        var petsPanel = drawer.Get<PetsExchangePanel>();
                        Check(petsPanel != null && !drawer.IsOpen(petsPanel), "Pets panel is not registered in the drawer");
                        Check(Sent(windows.Npc.RequestActionAsync(NpcActions.Pets, -8)) && Read(peer) == "ER9|-8", "Pet keeping does not send ER9 with the form open");
                        FeedFromNetwork(account, "ECK9|-8"); PumpUntil(() => drawer.Current == petsPanel);
                        Check(drawer.Visible && ((Label)Get(petsPanel, "petsHeading")).Text == "Garde chez Marchand fictif" && ((Label)Get(petsPanel, "distantTitle")).Text == "Marchand fictif"
                            && ((Label)Get(petsPanel, "petsStatus")).Text.StartsWith("Garde :"), "Pets panel heading or hint is wrong: " + ((Label)Get(petsPanel, "petsHeading")).Text);
                        var petsBag = (ListView)Get(petsPanel, "petsBag");
                        var petsLocal = (ExchangeItemGrid)Get(petsPanel, "localGrid"); var petsDistant = (ExchangeItemGrid)Get(petsPanel, "distantGrid");
                        var petsAdd = (Control)Get(petsPanel, "petsAdd"); var petsRemove = (Control)Get(petsPanel, "petsRemove"); var petsValidate = (Control)Get(petsPanel, "petsValidate");
                        Check(RowWithTag(petsBag, 3u) != null && RowWithTag(petsBag, 1001u) == null && !petsAdd.Enabled && !petsRemove.Enabled, "Pets bag list or buttons are wrong");
                        RowWithTag(petsBag, 3u).Selected = true; Application.DoEvents(); Check(petsAdd.Enabled, "Proposer disabled for a bag item");
                        ((NumericUpDown)Get(petsPanel, "petsQuantity")).Value = 1;
                        Click(petsAdd); Check(Read(peer) == "EMO+3|1", "Proposer does not send EMO+<id>|<qty>");
                        FeedFromNetwork(account, "EMKO+3|1"); PumpUntil(() => petsLocal.Items.Count == 1);
                        Check(RowWithTag(petsBag, 3u).SubItems[1].Text == "4", "Remaining quantity not refreshed: " + RowWithTag(petsBag, 3u).SubItems[1].Text);
                        FeedFromNetwork(account, "EmKO+2002|1|2002|"); FeedFromNetwork(account, "EK1"); PumpUntil(() => petsDistant.Items.Count == 1 && petsDistant.Ready);
                        Check(((Label)Get(petsPanel, "distantTitle")).Text == "Marchand fictif · prêt" && ((Label)Get(petsPanel, "petsStatus")).Text.Contains("Marchand fictif accepte la proposition."),
                            "NPC readiness is not shown");
                        Check(!petsValidate.Enabled && petsValidate.Text.StartsWith("Valider ("), "Pets validation is not delayed after a change");
                        PumpUntil(() => petsValidate.Enabled, 5); Check(petsValidate.Text == "Valider", "Pets countdown text was not cleared");
                        Click(petsValidate); Check(Read(peer) == "EK", "Valider does not send EK");
                        FeedFromNetwork(account, "EK142"); PumpUntil(() => petsLocal.Ready && ((Label)Get(petsPanel, "localTitle")).Text == "Vous · prêt");
                        FeedFromNetwork(account, "EVa"); PumpUntil(() => !drawer.IsOpen(petsPanel));
                        Check(petsLocal.Items.Count == 0 && petsDistant.Items.Count == 0 && ((Label)Get(petsPanel, "petsStatus")).Text.EndsWith("Garde effectuée par Marchand fictif."),
                            "Pets panel was not reset after EVa: " + ((Label)Get(petsPanel, "petsStatus")).Text);
                        Check(Sent(windows.Npc.RequestActionAsync(NpcActions.PetResurrection, -8)) && Read(peer) == "ER17|-8", "Pet resurrection does not send ER17 with the form open");
                        FeedFromNetwork(account, "ECK9|-8"); PumpUntil(() => drawer.Current == petsPanel);
                        Check(((Label)Get(petsPanel, "petsHeading")).Text == "Résurrection chez Marchand fictif" && ((Label)Get(petsPanel, "petsStatus")).Text.StartsWith("Résurrection :"),
                            "Resurrection heading or hint is wrong");
                        drawer.RequestClose(petsPanel); Check(Read(peer) == "EV" && drawer.IsOpen(petsPanel), "Closing the pets panel does not send EV or closed before the server");
                        FeedFromNetwork(account, "EV"); PumpUntil(() => !drawer.IsOpen(petsPanel));
                        Feed(account, "ECK17|-8"); Check(Read(peer) == "EV", "ECK17 not closed with the form open");
                        Application.DoEvents(); Check(drawer.Current != petsPanel, "ECK17 opened the pets panel");

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
