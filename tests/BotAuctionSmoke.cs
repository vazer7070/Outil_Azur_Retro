using System;
using System.Collections.Concurrent;
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
using Tool_BotProtocol.Game.Session;

// Hôtel de vente (lot F5) selon BigStoreBuy / BigStoreSell / Exchange du client 1.34 et GameClient.bigStore, request,
// movementItemOrKamas de StarLoco, sur un serveur fictif local avec des textes du client synthétiques : ouverture par le menu du PNJ
// (ER11|<pnj>, ER10|<pnj>), Im183, ECK11 (quantités, catégories, taxe, niveau et lots max, pnj -1, durée), EHT → EHL, EHP, EHl → lignes
// et prix x1/x10/x100, EHB avec ses refus locaux (ligne, lot, prix, kamas) et les réponses EHm-/EHm+/Im068/Im172, EHS/EHSK, EHM±,
// changement de mode ER10|-1 → ECK10 + EL, EMO+ avec ses refus locaux (type, niveau, équipé, quantité, prix, lots max, taxe) et les
// réponses EmK+ puis EL, Im058/Im176, EMO- → EmK-, paquets mal formés sans exception, EV, Clear ; puis les volets Hôtel de vente
// (achat et vente) dans une fenêtre de jeu invisible : catégories, modèles, lignes, boîtes DO_U_BUY_ITEM_BIGSTORE et
// DO_U_SELL_ITEM_BIGSTORE (Non n'envoie rien, Oui envoie EHB / EMO+), retrait, changement de mode, fermeture par EV.
internal static class BotAuctionSmoke
{
    private const int MapId = 900099, NpcTemplate = 100, Npc = -8;
    private const string SelfGm = "+0;1;0;42;Personnage de test;1;100^100;0";
    private const string NpcGm = "+5;1;0;-8;100;-4;1^100;0";
    private const string Shop = "1,10,100;12,6;5;200;127;-1;172800000";
    private const string Plain = "HhGaeaaaaa";
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
            Console.WriteLine("OK: hôtel de vente ER10/ER11, ECK10/ECK11, EHT/EHL, EHl/EHP, EHB/EHm/Im068/Im172, EHS, EHM, EL, EMO+/EmK/Im058/Im176, EMO-, EV, volets achat et vente");
        }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
        finally { LangData.Clear(); }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Check(bool value, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0) { if (!value) throw new Exception("Check failed at line " + line); }
    private static object Get(object target, string field) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static IEnumerable<Control> All(Control value) { yield return value; foreach (Control child in value.Controls) foreach (var nested in All(child)) yield return nested; }
    private static void PumpUntil(Func<bool> done, int seconds = 6)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(seconds);
        while (!done()) { if (DateTime.UtcNow > end) throw new TimeoutException("Auction loopback timed out"); Application.DoEvents(); Thread.Sleep(5); }
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
        Check(socket.Available == 0, message);
    }
    private static ListViewItem RowWithTag(ListView list, object tag) => list.Items.Cast<ListViewItem>().FirstOrDefault(row => Equals(row.Tag, tag));
    private static void Click(object button) => ((Button)button).PerformClick();
    private static void Answer(Form dialog, string choice) => All(dialog).OfType<Button>().First(button => button.Text == choice).PerformClick();
    private static string DialogMessage(Form dialog) => All(dialog).FirstOrDefault(control => control.Name == "dialog-message")?.Text ?? string.Empty;
    private static string Join<T>(IEnumerable<T> values) => string.Join(",", values);
    private static void WriteLang(string folder, string file, string family, string body) =>
        File.WriteAllText(Path.Combine(folder, file), "<?xml version='1.0' encoding='utf-8'?>\n<BotLang famille=\"" + family + "\" langue=\"fr\" version=\"1\" source=\"" + family
            + "_fr_1.swf\">\n" + body + "\n</BotLang>\n", new UTF8Encoding(false));

    private static void Run()
    {
        string previous = Environment.CurrentDirectory;
        string folder = Path.Combine(TestPaths.Work, "bot-auction-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); Environment.CurrentDirectory = folder;
        string lang = Path.Combine(folder, "lang"); Directory.CreateDirectory(lang);
        // Objets et types (I.u / I.t) : potion (12) et épée (6) acceptées par l'hôtel, anneau (9) et bois (35) refusés, potion de haut niveau.
        WriteLang(lang, "items.xml", "items",
            "<objet id=\"2001\" nom=\"Potion fictive\" type=\"12\" gfx=\"5\" niveau=\"1\" pods=\"1\" prix=\"10\" />\n"
            + "<objet id=\"2002\" nom=\"Épée fictive\" type=\"6\" gfx=\"7\" niveau=\"10\" pods=\"20\" prix=\"250\" />\n"
            + "<objet id=\"2003\" nom=\"Anneau fictif\" type=\"9\" gfx=\"3\" niveau=\"1\" pods=\"1\" />\n"
            + "<objet id=\"2005\" nom=\"Bois fictif\" type=\"35\" gfx=\"9\" niveau=\"1\" pods=\"2\" />\n"
            + "<objet id=\"2007\" nom=\"Potion rare fictive\" type=\"12\" gfx=\"5\" niveau=\"250\" pods=\"1\" />\n"
            + "<type id=\"12\" nom=\"Potion\" superType=\"6\" />\n<type id=\"6\" nom=\"Épée\" superType=\"2\" />\n<type id=\"9\" nom=\"Anneau\" superType=\"3\" />\n<type id=\"35\" nom=\"Bois\" superType=\"9\" />");
        WriteLang(lang, "lang.xml", "lang",
            "<texte cle=\"BIGSTORE\" valeur=\"Hôtel de vente\" />\n<texte cle=\"BIGSTORE_MODE_BUY\" valeur=\"Mode achat\" />\n<texte cle=\"BIGSTORE_MODE_SELL\" valeur=\"Mode vente\" />\n"
            + "<texte cle=\"BIGSTORE_BAD_TYPE\" valeur=\"Catégorie refusée par cet hôtel (texte de test).\" />\n<texte cle=\"BIGSTORE_BAD_LEVEL\" valeur=\"Niveau trop haut pour cet hôtel (texte de test).\" />\n"
            + "<texte cle=\"ITEM_NOT_IN_BIGSTORE\" valeur=\"Objet introuvable dans cet hôtel (texte de test).\" />\n<texte cle=\"NOT_ENOUGH_RICH\" valeur=\"Tu n'as pas assez de kamas (texte de test).\" />\n"
            + "<texte cle=\"DO_U_BUY_ITEM_BIGSTORE\" valeur=\"Confirmez-vous l'achat de '%1' au prix de %2 kamas ?\" />\n<texte cle=\"DO_U_SELL_ITEM_BIGSTORE\" valeur=\"Confirmez-vous la vente de '%1' au prix de %2 kamas ?\" />\n"
            + "<texte cle=\"BUY\" valeur=\"Acheter\" />\n<texte cle=\"CLOSE\" valeur=\"Fermer\" />\n<texte cle=\"REMOVE\" valeur=\"Retirer\" />\n<texte cle=\"SEARCH\" valeur=\"Rechercher\" />\n"
            + "<texte cle=\"PUT_ON_SELL\" valeur=\"Mettre en vente\" />\n<texte cle=\"BIGSTORE_TAX\" valeur=\"Taxe de mise en vente\" />\n<texte cle=\"INFOS_68\" valeur=\"Lot acheté (texte de test).\" />\n"
            + "<texte cle=\"ERROR_72\" valeur=\"Plus disponible à ce prix (texte de test).\" />\n<texte cle=\"INFOS_58\" valeur=\"Trop de lots en vente (texte de test).\" />\n"
            + "<texte cle=\"ERROR_76\" valeur=\"Pas assez de kamas pour la taxe (texte de test).\" />\n<texte cle=\"ERROR_83\" valeur=\"Déshonneur (texte de test).\" />");
        LangData.Clear();
        Check(LangData.Load(lang) == 2 && LangData.IsLoaded("items") && LangData.IsLoaded("lang"), "Synthetic lang files not loaded: " + string.Join(" / ", LangData.LoadWarnings));
        Func<int, string, string[], string> previousResolver = ServerMessages.Resolver;
        ServerMessages.Resolver = (type, id, args) => { string text = LangData.Text.Im(type, id, args); return string.IsNullOrEmpty(text) || (text.StartsWith("!") && text.EndsWith("!")) ? null : text; };

        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var logs = new ConcurrentQueue<string>();
        try
        {
            MessagesReception.Init();
            Check(new[] { ExchangeTypes.AuctionSell, ExchangeTypes.AuctionBuy }.All(ExchangeRegistry.IsRegistered), "Exchange types 10 and 11 are not registered");
            Map.AllBotMaps[MapId] = new Map { MapID = MapId, MapWidth = 3, MapHeight = 4, X = 4, Y = -18, MapData = string.Concat(Enumerable.Repeat(Plain, 18)) };
            PNJ.AllPNJ[NpcTemplate] = new PNJ(NpcTemplate, NpcTemplate, null) { Name = "Hôtelier fictif" };
            using (var account = new Accounts(new AccountConfig("synthetic-auction", "synthetic", "loopback")))
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
                    Feed(account, "GM|" + SelfGm + "|" + NpcGm);
                    account.AccountStates = AccountStates.CONNECTED_INACTIVE;
                    // Sac : 12 potions, une épée équipée, un anneau, une potion rare (niveau 250), 10 bois, une épée libre.
                    Feed(account, "OAKO3e8~7d1~c~~7d#5#0#0#;3e9~7d2~1~1~;3ea~7d3~1~~;3eb~7d7~1~~;3ec~7d5~a~~;3ed~7d2~1~~");
                    InventoryClass bag = account.Game.character.Inventory;
                    Check(bag.GetByInventoryId(1000)?.Qua == 12 && bag.GetByInventoryId(1000).Type == 12 && bag.GetByInventoryId(1001).IsEquipped() && bag.GetByInventoryId(1003)?.Level == 250
                        && account.Game.Map.GetActor(Npc) != null, "Synthetic inventory or NPC was not read");

                    Buying(account, peer, logs);
                    Selling(account, peer, logs);
                    Windows(account, peer, logs);
                    NoPacket(peer, "Unexpected packet at the end of the test");
                }
            }
        }
        finally { ServerMessages.Resolver = previousResolver; listener.Stop(); Environment.CurrentDirectory = previous; }
    }

    /// <summary>Achat : ER11 par le menu du PNJ, Im183, ECK11, EHT/EHL, EHP, EHl, EHB et ses réponses, EHS, EHM±, paquets mal formés.</summary>
    private static void Buying(Accounts account, Socket peer, ConcurrentQueue<string> logs)
    {
        AuctionHouse hdv = account.Game.Interactions.Auction;
        ExchangeRegistry registry = account.Game.Interactions.Exchanges;
        NpcDialog npc = account.Game.Interactions.Npc;
        Check(registry.For(ExchangeTypes.AuctionSell) == hdv && registry.For(ExchangeTypes.AuctionBuy) == hdv && registry.Get<AuctionHouse>() == hdv, "Registry does not map types 10 and 11 to the auction house");
        Check(!hdv.IsOpen && !hdv.IsBuying && !hdv.IsSelling && hdv.Info.Quantities.SequenceEqual(new[] { 1, 10, 100 }) && hdv.Info.Categories.Length == 0 && hdv.Info.NpcId == -1, "Initial auction state differs");
        Check(!Sent(hdv.SelectCategoryAsync(12)) && !Sent(hdv.SelectTemplateAsync(2001)) && !Sent(hdv.RequestAveragePriceAsync(2001)) && !Sent(hdv.SearchAsync(12, 2001))
            && !Sent(hdv.BuyAsync(1, 1, 1)) && !Sent(hdv.SellAsync(1000, 1, 1)) && !Sent(hdv.RemoveSaleAsync(1)) && !Sent(hdv.SwitchModeAsync()) && !Sent(hdv.LeaveAsync()),
            "Auction actions were accepted without an open window");
        NoPacket(peer, "Refused auction actions reached the server");
        Check(AuctionHouse.TaxFor(1000, 5) == 50 && AuctionHouse.TaxFor(999, 1) == 9 && AuctionHouse.TaxFor(0, 5) == 0 && AuctionHouse.TaxFor(10, 0) == 0, "TaxFor differs from (int)(prix * taxe / 100)");
        Check(AuctionHouse.CategoryName(12) == "Potion" && AuctionHouse.CategoryName(99) == "Catégorie n° 99", "CategoryName does not read I.t");
        KeyValuePair<int, int>? found = AuctionHouse.FindTemplate(" potion FICTIVE ");
        Check(found != null && found.Value.Key == 2001 && found.Value.Value == 12 && AuctionHouse.FindTemplate("Inconnu") == null && AuctionHouse.FindTemplate("") == null, "FindTemplate does not read the client texts");

        // Menu du PNJ (lot M5) : action 6 → ER11|<pnj>, action 5 → ER10|<pnj> ; puis Im183 et ECK11.
        Check(Sent(npc.RequestActionAsync(6, Npc))); Expect(peer, "ER11|-8", "NPC action 6 does not send ER11|<pnj>");
        Check(Sent(npc.RequestActionAsync(5, Npc))); Expect(peer, "ER10|-8", "NPC action 5 does not send ER10|<pnj>");
        Check(Sent(hdv.OpenBuyAsync(Npc)) && hdv.IsOpeningPending); Expect(peer, "ER11|-8", "OpenBuy does not send ER11|<pnj>");
        Feed(account, "Im183");
        Check(!hdv.IsOpeningPending && !hdv.IsOpen && hdv.LastMessage.Contains("Im183") && hdv.LastMessage.Contains("Déshonneur (texte de test)"), "Im183 did not release the opening");
        Check(Sent(hdv.OpenBuyAsync(Npc))); Expect(peer, "ER11|-8", "Second ER11 was not sent");
        Check(Sent(hdv.OpenSellAsync(Npc))); Expect(peer, "ER10|-8", "ER10 refused while ER11 is pending (StarLoco answers nothing on a map without an auction house)");
        Feed(account, "ECK11|" + Shop);
        AuctionHouseInfo info = hdv.Info;
        Check(hdv.IsOpen && hdv.IsBuying && !hdv.IsSelling && hdv.ExchangeType == 11 && !hdv.IsOpeningPending && registry.Current == hdv && account.AccountStates == AccountStates.BUYING, "ECK11 did not open the auction house in buy mode");
        Check(info.Quantities.SequenceEqual(new[] { 1, 10, 100 }) && info.Categories.SequenceEqual(new[] { 12, 6 }) && Math.Abs(info.Tax - 5) < 0.0001 && info.MaxLevel == 200 && info.MaxItems == 127 && info.NpcId == -1
            && info.SellTime == 172800000 && info.QuantityOf(2) == 10 && info.QuantityOf(4) == 0 && info.HasCategory(12) && !info.HasCategory(9), "ECK11 fields were not read");
        Check(hdv.CurrentCategory == -1 && hdv.CurrentTemplate == -1 && hdv.Templates.Count == 0 && hdv.Lines.Count == 0 && hdv.LastSearchFound == null, "Buy state not empty after ECK11");
        Check(!Sent(hdv.OpenBuyAsync(Npc)) && !Sent(hdv.OpenSellAsync(Npc)) && !Sent(hdv.SellAsync(1000, 1, 10)) && !Sent(hdv.RemoveSaleAsync(1)), "Opening again or selling was accepted in buy mode");

        // EHT : catégorie absente refusée (StarLoco ne répond rien) ; EHL avec un modèle illisible ignoré.
        Check(!Sent(hdv.SelectCategoryAsync(9)) && !Sent(hdv.SelectTemplateAsync(2001)), "Unknown category or template was requested");
        NoPacket(peer, "Refused EHT/EHl reached the server");
        Check(Sent(hdv.SelectCategoryAsync(12)) && hdv.CurrentCategory == 12); Expect(peer, "EHT12", "SelectCategory does not send EHT<catégorie>");
        int before = logs.Count;
        Feed(account, "EHL12|2001;2007;x;2001");
        Check(hdv.Templates.SequenceEqual(new[] { 2001, 2007 }) && hdv.CurrentCategory == 12 && logs.Count > before, "EHL was not read: " + Join(hdv.Templates));
        Check(Sent(hdv.RequestAveragePriceAsync(2001))); Expect(peer, "EHP2001", "RequestAveragePrice does not send EHP<modèle>");
        Feed(account, "EHP2001|145"); Check(hdv.AveragePrices[2001] == 145, "EHP was not read");
        Check(Sent(hdv.SelectTemplateAsync(2001)) && hdv.CurrentTemplate == 2001); Expect(peer, "EHl2001", "SelectTemplate does not send EHl<modèle>");
        before = logs.Count;
        Feed(account, "EHl2001|501;7d#5#0#0#;150;1400;|502;;;1500;14000|bad");
        IReadOnlyList<AuctionLine> lines = hdv.Lines;
        Check(lines.Count == 2 && lines[0].Id == 501 && lines[0].TemplateId == 2001 && lines[0].Effects == "7d#5#0#0#" && lines[0].PriceOf(1) == 150 && lines[0].PriceOf(2) == 1400 && lines[0].PriceOf(3) == null
            && lines[0].Name == "Potion fictive" && lines[1].Id == 502 && lines[1].PriceOf(1) == null && lines[1].PriceOf(2) == 1500 && lines[1].PriceOf(3) == 14000 && lines[1].PriceOf(4) == null && logs.Count > before,
            "EHl lines were not read");

        // EHB : refus locaux (ligne inconnue, lot absent, prix différent, kamas), puis EHm-, EHm+, Im068 et Im172.
        Check(!Sent(hdv.BuyAsync(999, 1, 150)) && !Sent(hdv.BuyAsync(501, 3, 1)) && !Sent(hdv.BuyAsync(501, 1, 149)) && !Sent(hdv.BuyAsync(501, 0, 150)), "Invalid purchases were accepted");
        account.Game.character.Kamas = 100;
        InteractionResult poor = Result(hdv.BuyAsync(501, 2, 1400));
        Check(!poor.Sent && poor.Message.StartsWith("Tu n'as pas assez de kamas (texte de test)") && !hdv.IsBuyPending, "Purchase above the kamas was accepted");
        NoPacket(peer, "Refused purchases reached the server");
        account.Game.character.Kamas = 20000;
        Check(Sent(hdv.BuyAsync(501, 2, 1400)) && hdv.IsBuyPending); Expect(peer, "EHB501|2|1400", "Buy does not send EHB<ligne>|<indice>|<prix>");
        Check(!Sent(hdv.BuyAsync(501, 1, 150)), "Second purchase accepted while pending");
        Feed(account, "EHm-501");
        Check(!hdv.IsBuyPending && hdv.Lines.Count == 1 && hdv.Lines[0].Id == 502, "EHm- did not remove the line");
        Feed(account, "EHm+501|2001|7d#5#0#0#|150||"); Feed(account, "Im068");
        lines = hdv.Lines;
        Check(lines.Count == 2 && lines[1].Id == 501 && lines[1].PriceOf(1) == 150 && lines[1].PriceOf(2) == null && lines[1].Effects == "7d#5#0#0#" && hdv.IsBuying, "EHm+ did not re-add the line");
        Check(Sent(hdv.BuyAsync(502, 3, 14000))); Expect(peer, "EHB502|3|14000", "Second buy was not sent");
        Feed(account, "Im068"); Check(!hdv.IsBuyPending && hdv.LastMessage == "Lot acheté (texte de test).", "Im068 did not release the purchase");
        Check(Sent(hdv.BuyAsync(502, 2, 1500))); Expect(peer, "EHB502|2|1500", "Third buy was not sent");
        Feed(account, "Im172"); Check(!hdv.IsBuyPending && hdv.LastMessage.Contains("Im172") && hdv.LastMessage.Contains("Plus disponible à ce prix (texte de test)"), "Im172 did not release the purchase");
        Feed(account, "Im068"); Check(hdv.LastMessage.Contains("Im172"), "Im068 without a pending purchase changed the message");
        Feed(account, "EHm+777|2007||5||"); Check(hdv.Lines.Count == 2, "EHm+ of another template was added to the current lines");

        // EHS : catégorie absente refusée ; EHS (introuvable) puis EHSK suivi de EHL, EHP, EHl ; EHM- retire un modèle, EHM+ l'ajoute.
        Check(!Sent(hdv.SearchAsync(9, 2003)) && !Sent(hdv.SearchAsync(12, -1)), "Search of a refused category was sent");
        Check(Sent(hdv.SearchAsync(12, 2007)) && hdv.LastSearchFound == null && hdv.CurrentTemplate == 2007 && hdv.Lines.Count == 0); Expect(peer, "EHS12|2007", "Search does not send EHS<catégorie>|<modèle>");
        Feed(account, "EHS"); Check(hdv.LastSearchFound == false && hdv.LastMessage == "Objet introuvable dans cet hôtel (texte de test).", "EHS (not found) was not read");
        Check(Sent(hdv.SearchAsync(12, 2001))); Expect(peer, "EHS12|2001", "Second search was not sent");
        Feed(account, "EHSK"); Check(hdv.LastSearchFound == true, "EHSK was not read");
        Feed(account, "EHL12|2001;2007"); Feed(account, "EHP2001|140"); Feed(account, "EHl2001|501;;150;;");
        Check(hdv.Templates.SequenceEqual(new[] { 2001, 2007 }) && hdv.AveragePrices[2001] == 140 && hdv.CurrentTemplate == 2001 && hdv.Lines.Count == 1 && hdv.Lines[0].PriceOf(1) == 150, "Search results were not read");
        Feed(account, "EHM-2007"); Check(hdv.Templates.SequenceEqual(new[] { 2001 }) && hdv.Lines.Count == 1, "EHM- did not remove the template");
        Feed(account, "EHM-2001"); Check(hdv.Templates.Count == 0 && hdv.CurrentTemplate == -1 && hdv.Lines.Count == 0, "EHM- of the current template did not clear the lines");
        Feed(account, "EHM+2001"); Check(hdv.Templates.SequenceEqual(new[] { 2001 }), "EHM+ was not read");
        Feed(account, "EHl2001|501;;150;;"); Feed(account, "EHl"); Check(hdv.Lines.Count == 0 && hdv.CurrentTemplate == 2001, "Empty EHl did not clear the lines");
        Feed(account, "EHl2001|501;;150;1400;");
        NoPacket(peer, "Buy receptions triggered a reply");

        // Paquets mal formés : journalisés, jamais propagés, état inchangé.
        before = logs.Count;
        foreach (string packet in new[] { "EHL", "EHLx|1", "EHM", "EHMx", "EHM+x", "EHM*2001", "EHlx|1;;1", "EHl2001|a;;1", "EHm", "EHm*1", "EHm+x", "EHm+1|x", "EHm+1|2001", "EHm+1|2001|x|a", "EHP", "EHP2001", "EHPx|1", "EHP2001|x", "EHSx", "EmK+1|1|2001||5|350", "EL1;1;2001;;5;350" })
            Feed(account, packet);
        lines = hdv.Lines;
        Check(hdv.IsBuying && hdv.Templates.SequenceEqual(new[] { 2001 }) && hdv.CurrentTemplate == 2001 && lines.Count == 1 && lines[0].PriceOf(2) == 1400 && hdv.AveragePrices[2001] == 140 && hdv.Sales.Count == 0
            && account.AccountStates == AccountStates.BUYING, "Malformed auction packets changed the state");
        Check(logs.Count >= before + 15 && !logs.Any(entry => entry.Contains("non appliqué")), "Malformed auction packets were not logged or raised an exception");
        NoPacket(peer, "Malformed auction packets triggered a reply");

        // Fermeture par EV ; EH… hors fenêtre ignoré ; ECK11 illisible ouvre quand même avec les valeurs par défaut.
        Check(Sent(hdv.LeaveAsync())); Expect(peer, "EV", "Leave does not send EV");
        Check(hdv.IsOpen, "Window closed before the server answered");
        Feed(account, "EV");
        Check(!hdv.IsOpen && !hdv.IsBuying && hdv.ExchangeType == -1 && hdv.Templates.Count == 0 && hdv.Lines.Count == 0 && hdv.AveragePrices.Count == 0 && hdv.Info.Categories.Length == 0
            && registry.Current == null && account.AccountStates == AccountStates.CONNECTED_INACTIVE, "EV did not close the auction house");
        Feed(account, "EHL12|2001"); Feed(account, "EHl2001|501;;150;;"); Check(hdv.Templates.Count == 0 && hdv.Lines.Count == 0, "EH packets were applied without a window");
        before = logs.Count;
        Feed(account, "ECK11|x");
        Check(hdv.IsBuying && hdv.Info.Categories.Length == 0 && hdv.Info.Quantities.SequenceEqual(new[] { 1, 10, 100 }) && logs.Skip(before).Any(entry => entry.Contains("illisible")), "Malformed ECK11 did not open with defaults");
        Check(!Sent(hdv.SelectCategoryAsync(12)), "Category accepted without categories");
        Feed(account, "EV"); Feed(account, "ECK11");
        Check(hdv.IsBuying && hdv.Info.MaxLevel == 0, "ECK11 without data did not open");
        Feed(account, "EV");
    }

    /// <summary>Vente : ER10, ECK10 + EL, EMO+ et ses refus locaux, EmK+ puis EL, Im058/Im176, EMO- → EmK-, changement de mode, Clear.</summary>
    private static void Selling(Accounts account, Socket peer, ConcurrentQueue<string> logs)
    {
        AuctionHouse hdv = account.Game.Interactions.Auction;
        ExchangeRegistry registry = account.Game.Interactions.Exchanges;
        InventoryClass bag = account.Game.character.Inventory;
        Check(Sent(hdv.OpenSellAsync(Npc)) && hdv.IsOpeningPending); Expect(peer, "ER10|-8", "OpenSell does not send ER10|<pnj>");
        Feed(account, "ECK10|1,10,100;12,6;5;200;1;-1;172800000");
        Check(hdv.IsOpen && hdv.IsSelling && !hdv.IsBuying && hdv.ExchangeType == 10 && !hdv.SalesReceived && hdv.Info.MaxItems == 1 && account.AccountStates == AccountStates.SELLING && registry.Current == hdv,
            "ECK10 did not open the auction house in sell mode");
        Check(!Sent(hdv.SelectCategoryAsync(12)) && !Sent(hdv.SearchAsync(12, 2001)) && !Sent(hdv.BuyAsync(501, 1, 150)), "Buy actions were accepted in sell mode");
        Feed(account, "EL"); Check(hdv.SalesReceived && hdv.Sales.Count == 0, "Empty EL was not read");

        // CanSell : équipé, catégorie, niveau ; QuantityIndexesFor ; refus locaux de EMO+ (quantité, prix, lots max, taxe).
        string reason;
        Check(!hdv.CanSell(null, out reason) && !hdv.CanSell(bag.GetByInventoryId(1001), out reason) && reason.StartsWith("Déséquipez"), "Equipped item was sellable");
        Check(!hdv.CanSell(bag.GetByInventoryId(1002), out reason) && reason == "Catégorie refusée par cet hôtel (texte de test)." && !hdv.CanSell(bag.GetByInventoryId(1004), out reason), "Ring or wood was sellable");
        Check(!hdv.CanSell(bag.GetByInventoryId(1003), out reason) && reason == "Niveau trop haut pour cet hôtel (texte de test).", "Level 250 potion was sellable");
        Check(hdv.CanSell(bag.GetByInventoryId(1000), out reason) && reason == null && hdv.CanSell(bag.GetByInventoryId(1005), out reason), "Potion or free sword was not sellable");
        Check(hdv.QuantityIndexesFor(12).SequenceEqual(new[] { 1, 2 }) && hdv.QuantityIndexesFor(1).SequenceEqual(new[] { 1 }) && hdv.QuantityIndexesFor(0).Length == 0 && hdv.QuantityIndexesFor(100).SequenceEqual(new[] { 1, 2, 3 }), "QuantityIndexesFor differs");
        Check(!Sent(hdv.SellAsync(9999, 1, 10)) && !Sent(hdv.SellAsync(1001, 1, 10)) && !Sent(hdv.SellAsync(1002, 1, 10)) && !Sent(hdv.SellAsync(1003, 1, 10)), "Unsellable items were sent");
        Check(!Sent(hdv.SellAsync(1000, 3, 10)) && !Sent(hdv.SellAsync(1000, 0, 10)) && !Sent(hdv.SellAsync(1000, 4, 10)) && !Sent(hdv.SellAsync(1000, 1, 0)) && !Sent(hdv.SellAsync(1000, 1, (long)int.MaxValue + 1)),
            "Invalid quantity or price was sent");
        Feed(account, "EL601;1;2001;;100;350");
        InteractionResult full = Result(hdv.SellAsync(1000, 1, 10));
        Check(!full.Sent && full.Message.StartsWith("Nombre maximal") && hdv.Sales.Count == 1, "Sale above the maximum was sent");
        Feed(account, "ECK10|" + Shop); Feed(account, "EL");
        Check(hdv.IsSelling && hdv.Info.MaxItems == 127 && hdv.Sales.Count == 0 && account.AccountStates == AccountStates.SELLING, "Second ECK10 did not reopen the sell mode");
        account.Game.character.Kamas = 10;
        InteractionResult poor = Result(hdv.SellAsync(1000, 1, 1000));
        Check(!poor.Sent && poor.Message.StartsWith("Tu n'as pas assez de kamas (texte de test)") && poor.Message.Contains("50"), "Sale without the kamas of the tax was sent");
        NoPacket(peer, "Refused sales reached the server");
        account.Game.character.Kamas = 20000;
        Check(Sent(hdv.SellAsync(1000, 1, 1000)) && hdv.IsSellPending); Expect(peer, "EMO+1000|1|1000", "Sell does not send EMO+<objet>|<indice>|<prix>");
        Check(!Sent(hdv.SellAsync(1000, 1, 1000)), "Second sale accepted while pending");
        Feed(account, "EmK+1000|1|2001|7d#5#0#0#|1000|350");
        Check(hdv.IsSellPending && hdv.Sales.Count == 0 && hdv.LastMessage.Contains("acceptée"), "EmK+ changed the sales before EL");
        Feed(account, "EL601;1;2001;7d#5#0#0#;1000;350");
        IReadOnlyList<AuctionSale> sales = hdv.Sales;
        Check(!hdv.IsSellPending && sales.Count == 1 && sales[0].LineId == 601 && sales[0].Quantity == 1 && sales[0].TemplateId == 2001 && sales[0].Effects == "7d#5#0#0#" && sales[0].Price == 1000
            && sales[0].RemainingHours == 350 && sales[0].Name == "Potion fictive", "Sales EL was not read");
        Check(Sent(hdv.SellAsync(1000, 2, 5000))); Expect(peer, "EMO+1000|2|5000", "Second sale was not sent");
        Feed(account, "Im058"); Check(!hdv.IsSellPending && hdv.LastMessage.Contains("Im058") && hdv.LastMessage.Contains("Trop de lots en vente (texte de test)"), "Im058 did not release the sale");
        Check(Sent(hdv.SellAsync(1005, 1, 300))); Expect(peer, "EMO+1005|1|300", "Sword sale was not sent");
        Feed(account, "Im176"); Check(!hdv.IsSellPending && hdv.LastMessage.Contains("Im176") && hdv.LastMessage.Contains("Pas assez de kamas pour la taxe (texte de test)"), "Im176 did not release the sale");
        Check(Sent(hdv.RequestAveragePriceAsync(2001))); Expect(peer, "EHP2001", "EHP was not sent in sell mode");
        Feed(account, "EHP2001|120"); Check(hdv.AveragePrices[2001] == 120, "EHP was not read in sell mode");
        Feed(account, "EHL12|2001"); Check(hdv.Templates.Count == 0, "EHL was applied in sell mode");

        // EMO- → EmK-<ligne> ; EmKO- du client lu aussi ; EL avec des lots illisibles ; EmK mal formés.
        Check(!Sent(hdv.RemoveSaleAsync(999)), "Removal of an unknown lot was sent");
        Check(Sent(hdv.RemoveSaleAsync(601))); Expect(peer, "EMO-601|1", "Remove does not send EMO-<ligne>|<quantité>");
        Feed(account, "EmK-601"); Check(hdv.Sales.Count == 0 && hdv.LastMessage.Contains("retiré"), "EmK- did not remove the lot");
        Feed(account, "EmK-601");
        Feed(account, "EL601;10;2001;;1000;350|602;1;2002;;300;350"); Check(hdv.Sales.Count == 2, "Two-lot EL was not read");
        Check(Sent(hdv.RemoveSaleAsync(601))); Expect(peer, "EMO-601|10", "Remove does not use the lot quantity");
        Feed(account, "EmKO-601"); Check(hdv.Sales.Count == 1 && hdv.Sales[0].LineId == 602, "EmKO- (client format) was not read");
        int before = logs.Count;
        Feed(account, "EL603;x;2001;;10|bad|604;2;2001;;7;350");
        Check(hdv.Sales.Count == 1 && hdv.Sales[0].LineId == 604 && logs.Count > before, "Unreadable EL lots were not skipped and logged");
        before = logs.Count;
        foreach (string packet in new[] { "EmK", "EmK*1", "EmK+x", "EmK+1|a|b|c|d", "EmK-x", "EmKO", "EHm-604", "EHl2001|1;;1;;", "EHS", "EHM-2001" }) Feed(account, packet);
        Check(hdv.IsSelling && hdv.Sales.Count == 1 && !logs.Any(entry => entry.Contains("non appliqué")) && logs.Count >= before + 5, "Malformed sell packets changed the state or raised");
        NoPacket(peer, "Sell receptions triggered a reply");

        // Changement de mode : ER11|-1 (npcID de l'ECK) → ECK11 sans EV ; puis ER10|-1 → ECK10 ; EV ; Clear.
        Check(Sent(hdv.SwitchModeAsync()) && hdv.IsOpeningPending); Expect(peer, "ER11|-1", "Switch does not send ER11|<npcID>");
        Check(!Sent(hdv.SwitchModeAsync()), "Second switch accepted while pending");
        Feed(account, "ECK11|" + Shop);
        Check(hdv.IsBuying && !hdv.IsSelling && hdv.Sales.Count == 0 && !hdv.IsOpeningPending && account.AccountStates == AccountStates.BUYING && registry.Current == hdv, "ECK11 after the switch did not reopen in buy mode");
        Check(Sent(hdv.SwitchModeAsync())); Expect(peer, "ER10|-1", "Switch back does not send ER10|<npcID>");
        Feed(account, "ECK10|" + Shop); Feed(account, "EL604;2;2001;;7;350");
        Check(hdv.IsSelling && hdv.Sales.Count == 1 && account.AccountStates == AccountStates.SELLING, "ECK10 after the switch did not reopen in sell mode");
        Check(Sent(hdv.LeaveAsync())); Expect(peer, "EV", "Leave does not send EV (sell mode)");
        Feed(account, "EV");
        Check(!hdv.IsOpen && hdv.Sales.Count == 0 && !hdv.SalesReceived && registry.Current == null && account.AccountStates == AccountStates.CONNECTED_INACTIVE, "EV did not close the sell mode");
        Feed(account, "ECK10|" + Shop); Feed(account, "EL604;2;2001;;7;350"); Check(Sent(hdv.SellAsync(1000, 1, 10))); Expect(peer, "EMO+1000|1|10", "Sale before Clear was not sent");
        account.Game.Interactions.Clear();
        Check(!hdv.IsOpen && hdv.Sales.Count == 0 && !hdv.IsSellPending && !hdv.IsOpeningPending && hdv.Info.Categories.Length == 0 && registry.Current == null && account.AccountStates == AccountStates.CONNECTED_INACTIVE,
            "Clear kept the auction house");
        NoPacket(peer, "Clear sent a packet");
    }

    /// <summary>Volets Hôtel de vente (achat et vente) dans une fenêtre de jeu invisible.</summary>
    private static void Windows(Accounts account, Socket peer, ConcurrentQueue<string> logs)
    {
        AuctionHouse hdv = account.Game.Interactions.Auction;
        using (var form = new GameClientFullform(account))
        {
            form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); Application.DoEvents();
            PanelHost drawer = form.Panels;
            var buyPanel = drawer.Get<AuctionBuyPanel>(); var sellPanel = drawer.Get<AuctionSellPanel>();
            Check(buyPanel != null && sellPanel != null && drawer.Current == null, "Auction panels are not registered in the drawer");
            NoPacket(peer, "Opening the game window sent a packet");

            // Achat : ECK11 depuis un fil réseau → volet ; catégories triées ; EHT ; EHL → modèles ; sélection → EHP puis EHl ; lignes et lots.
            FeedFromNetwork(account, "ECK11|" + Shop);
            PumpUntil(() => drawer.Current == buyPanel);
            var categoryBox = (ComboBox)Get(buyPanel, "categoryBox"); var quantityBox = (ComboBox)Get(buyPanel, "quantityBox"); var searchBox = (TextBox)Get(buyPanel, "searchBox");
            var templates = (ListView)Get(buyPanel, "templateList"); var lineList = (ListView)Get(buyPanel, "lineList");
            var buyButton = (Control)Get(buyPanel, "buyButton"); var searchButton = (Control)Get(buyPanel, "searchButton"); var switchButton = (Control)Get(buyPanel, "switchButton"); var leaveButton = (Control)Get(buyPanel, "leaveButton");
            Check(categoryBox.Items.Count == 2 && categoryBox.Items[0].ToString() == "Épée" && categoryBox.Items[1].ToString() == "Potion" && categoryBox.SelectedIndex == -1 && templates.Items.Count == 0
                && !buyButton.Enabled && switchButton.Enabled && leaveButton.Enabled && ((Label)Get(buyPanel, "heading")).Text.Contains("200") && ((Label)Get(buyPanel, "summary")).Text.Contains("5"),
                "Buy panel content differs: " + string.Join("|", categoryBox.Items.Cast<object>()));
            categoryBox.SelectedIndex = 1; Expect(peer, "EHT12", "Category choice does not send EHT<catégorie>");
            FeedFromNetwork(account, "EHL12|2001;2007");
            PumpUntil(() => templates.Items.Count == 2);
            Check(RowWithTag(templates, 2001) != null && RowWithTag(templates, 2001).Text == "Potion fictive" && RowWithTag(templates, 2001).SubItems[1].Text == "1" && RowWithTag(templates, 2007).SubItems[1].Text == "250",
                "Template rows differ");
            RowWithTag(templates, 2001).Selected = true; Application.DoEvents();
            Expect(peer, "EHP2001", "Template choice does not send EHP first"); Expect(peer, "EHl2001", "Template choice does not send EHl");
            FeedFromNetwork(account, "EHP2001|145"); FeedFromNetwork(account, "EHl2001|501;7d#5#0#0#;150;1400;|502;;;1500;14000");
            PumpUntil(() => lineList.Items.Count == 2);
            Check(((Label)Get(buyPanel, "averageLabel")).Text.Contains("145") && RowWithTag(lineList, 501u).SubItems[3].Text == "—" && RowWithTag(lineList, 502u).SubItems[1].Text == "—" && !buyButton.Enabled, "Line rows differ");
            RowWithTag(lineList, 501u).Selected = true; Application.DoEvents();
            Check(quantityBox.Items.Count == 2 && quantityBox.Items[0].ToString().StartsWith("x1 ") && quantityBox.Items[1].ToString().StartsWith("x10 ") && buyButton.Enabled, "Quantity choices differ: " + string.Join("|", quantityBox.Items.Cast<object>()));
            quantityBox.SelectedIndex = 1; Application.DoEvents();

            // Recherche : nom inconnu ou catégorie absente → rien ; nom connu → EHS.
            searchBox.Text = "Inconnu"; Click(searchButton); searchBox.Text = "Bois fictif"; Click(searchButton); Application.DoEvents();
            NoPacket(peer, "Search of an unknown item or refused category sent a packet");
            searchBox.Text = "Potion fictive"; Click(searchButton); Expect(peer, "EHS12|2001", "Search button does not send EHS<catégorie>|<modèle>");
            FeedFromNetwork(account, "EHSK"); FeedFromNetwork(account, "EHL12|2001;2007"); FeedFromNetwork(account, "EHP2001|145"); FeedFromNetwork(account, "EHl2001|501;7d#5#0#0#;150;1400;|502;;;1500;14000");
            PumpUntil(() => lineList.Items.Count == 2 && templates.Items.Count == 2);
            RowWithTag(lineList, 501u).Selected = true; Application.DoEvents(); quantityBox.SelectedIndex = 1; Application.DoEvents();
            Check(buyButton.Enabled, "Buy button disabled after the search");

            // DO_U_BUY_ITEM_BIGSTORE : kamas insuffisants → aucune boîte ; Non n'envoie rien ; Oui envoie EHB ; EHm- retire la ligne.
            account.Game.character.Kamas = 100;
            Click(buyButton); Application.DoEvents();
            Check(buyPanel.BuyDialog == null, "Buy dialog shown without the kamas");
            NoPacket(peer, "Buy without kamas sent a packet");
            account.Game.character.Kamas = 20000;
            Click(buyButton); PumpUntil(() => buyPanel.BuyDialog != null);
            Form ask = buyPanel.BuyDialog;
            Check(BotDialogs.OpenDialogs.Contains(ask) && ask.Text == "Hôtel de vente" && DialogMessage(ask) == "Confirmez-vous l'achat de 'x10 Potion fictive' au prix de 1400 kamas ?"
                && All(ask).OfType<Button>().Select(button => button.Text).OrderBy(text => text).SequenceEqual(new[] { "Non", "Oui" }), "Buy dialog differs: " + DialogMessage(ask));
            NoPacket(peer, "EHB was sent before the answer");
            Answer(ask, "Non"); PumpUntil(() => buyPanel.BuyDialog == null);
            NoPacket(peer, "« Non » sent a packet");
            Click(buyButton); PumpUntil(() => buyPanel.BuyDialog != null);
            Answer(buyPanel.BuyDialog, "Oui"); Expect(peer, "EHB501|2|1400", "« Oui » does not send EHB<ligne>|<indice>|<prix>");
            PumpUntil(() => hdv.IsBuyPending && buyPanel.BuyDialog == null);
            Check(!buyButton.Enabled, "Buy button enabled while pending");
            FeedFromNetwork(account, "EHm-501"); FeedFromNetwork(account, "Im068");
            PumpUntil(() => lineList.Items.Count == 1);
            Check(!hdv.IsBuyPending && RowWithTag(lineList, 502u) != null, "EHm- was not applied to the panel");

            // Fermeture demandée par l'utilisateur : EV envoyé, le volet attend le serveur ; puis réouverture et changement de mode → ER10|-1.
            drawer.RequestClose(buyPanel); Expect(peer, "EV", "User close does not send EV");
            Check(drawer.Current == buyPanel, "Buy panel closed before the server");
            Feed(account, "EV"); Application.DoEvents();
            Check(!drawer.IsOpen(buyPanel) && lineList.Items.Count == 0 && templates.Items.Count == 0, "Buy panel stayed open after EV");
            FeedFromNetwork(account, "ECK11|" + Shop);
            PumpUntil(() => drawer.Current == buyPanel);
            Click(switchButton); Expect(peer, "ER10|-1", "Switch button does not send ER10|-1");
            PumpUntil(() => !switchButton.Enabled);

            // Vente : ECK10 + EL → volet de vente, le volet d'achat se ferme ; sac avec les refus locaux et le filtre ; sélection → EHP ; lots.
            FeedFromNetwork(account, "ECK10|" + Shop); FeedFromNetwork(account, "EL");
            PumpUntil(() => drawer.Current == sellPanel);
            Check(!drawer.IsOpen(buyPanel) && hdv.IsSelling, "Buy panel stayed open after ECK10");
            var bagList = (ListView)Get(sellPanel, "bagList"); var stockList = (ListView)Get(sellPanel, "stockList"); var filterCheck = (CheckBox)Get(sellPanel, "filterCheck");
            var sellQuantity = (ComboBox)Get(sellPanel, "quantityBox"); var priceBox = (NumericUpDown)Get(sellPanel, "priceBox"); var taxLabel = (Label)Get(sellPanel, "taxLabel");
            var sellButton = (Control)Get(sellPanel, "sellButton"); var removeButton = (Control)Get(sellPanel, "removeButton"); var sellSwitch = (Control)Get(sellPanel, "switchButton"); var sellLeave = (Control)Get(sellPanel, "leaveButton");
            ListViewItem potion = RowWithTag(bagList, 1000u), ring = RowWithTag(bagList, 1002u), rare = RowWithTag(bagList, 1003u);
            Check(bagList.Items.Count == 5 && RowWithTag(bagList, 1001u) == null && potion != null && ring != null && rare != null && ring.ForeColor != potion.ForeColor
                && ring.ToolTipText == "Catégorie refusée par cet hôtel (texte de test)." && rare.ToolTipText == "Niveau trop haut pour cet hôtel (texte de test)." && potion.SubItems[1].Text == "12"
                && stockList.Items.Count == 0 && !sellButton.Enabled && !removeButton.Enabled && sellSwitch.Enabled && sellLeave.Enabled && ((Label)Get(sellPanel, "stockTitle")).Text.Contains("0/127"),
                "Sell panel content differs");
            filterCheck.Checked = true; Application.DoEvents();
            Check(bagList.Items.Count == 2 && RowWithTag(bagList, 1000u) != null && RowWithTag(bagList, 1005u) != null, "Filter did not hide the unsellable items");
            RowWithTag(bagList, 1000u).Selected = true; Application.DoEvents();
            Expect(peer, "EHP2001", "Bag choice does not send EHP<modèle>");
            FeedFromNetwork(account, "EHP2001|120"); PumpUntil(() => ((Label)Get(sellPanel, "averageLabel")).Text.Contains("120"));
            Check(sellQuantity.Items.Count == 2 && sellQuantity.Items[0].ToString() == "x1" && sellQuantity.Items[1].ToString() == "x10" && sellButton.Enabled && priceBox.Enabled, "Sell quantities differ: " + string.Join("|", sellQuantity.Items.Cast<object>()));
            priceBox.Value = 1000; Application.DoEvents();
            Check(taxLabel.Text.EndsWith(": 50"), "Tax label differs: " + taxLabel.Text);

            // DO_U_SELL_ITEM_BIGSTORE : taxe au-dessus des kamas → aucune boîte ; Non n'envoie rien ; Oui envoie EMO+ ; EmK+ puis EL → lot en vente.
            account.Game.character.Kamas = 10;
            Click(sellButton); Application.DoEvents();
            Check(sellPanel.SellDialog == null, "Sell dialog shown without the kamas of the tax");
            NoPacket(peer, "Sale without kamas sent a packet");
            account.Game.character.Kamas = 20000;
            Click(sellButton); PumpUntil(() => sellPanel.SellDialog != null);
            Form sale = sellPanel.SellDialog;
            Check(DialogMessage(sale).StartsWith("Confirmez-vous la vente de 'x1 Potion fictive' au prix de 1000 kamas ?") && DialogMessage(sale).Contains("50"), "Sell dialog differs: " + DialogMessage(sale));
            NoPacket(peer, "EMO+ was sent before the answer");
            Answer(sale, "Non"); PumpUntil(() => sellPanel.SellDialog == null);
            NoPacket(peer, "« Non » on the sale sent a packet");
            Click(sellButton); PumpUntil(() => sellPanel.SellDialog != null);
            Answer(sellPanel.SellDialog, "Oui"); Expect(peer, "EMO+1000|1|1000", "« Oui » does not send EMO+<objet>|<indice>|<prix>");
            PumpUntil(() => hdv.IsSellPending && sellPanel.SellDialog == null);
            Check(!sellButton.Enabled, "Sell button enabled while pending");
            FeedFromNetwork(account, "EmK+1000|1|2001|7d#5#0#0#|1000|350"); FeedFromNetwork(account, "EL601;1;2001;7d#5#0#0#;1000;350");
            PumpUntil(() => stockList.Items.Count == 1);
            Check(RowWithTag(stockList, 601u) != null && RowWithTag(stockList, 601u).SubItems[2].Text.Contains("1") && RowWithTag(stockList, 601u).SubItems[3].Text == "350 h" && ((Label)Get(sellPanel, "stockTitle")).Text.Contains("1/127"),
                "Stock rows differ");
            RowWithTag(stockList, 601u).Selected = true; Application.DoEvents();
            Check(removeButton.Enabled, "Remove button disabled for a selected lot");
            Click(removeButton); Expect(peer, "EMO-601|1", "Remove button does not send EMO-<ligne>|<quantité>");
            FeedFromNetwork(account, "EmK-601"); PumpUntil(() => stockList.Items.Count == 0);

            // Retour au mode achat depuis le volet de vente, puis Fermer → EV, le volet attend le serveur.
            Click(sellSwitch); Expect(peer, "ER11|-1", "Switch button (sell) does not send ER11|-1");
            FeedFromNetwork(account, "ECK11|" + Shop);
            PumpUntil(() => drawer.Current == buyPanel);
            Check(!drawer.IsOpen(sellPanel), "Sell panel stayed open after ECK11");
            Click(leaveButton); Expect(peer, "EV", "Close button does not send EV");
            Check(drawer.Current == buyPanel, "Buy panel closed before the server (close button)");
            Feed(account, "EV"); Application.DoEvents();
            Check(!drawer.IsOpen(buyPanel) && !hdv.IsOpen && account.AccountStates == AccountStates.CONNECTED_INACTIVE, "Buy panel stayed open after EV (close button)");
            drawer.Show(sellPanel); Application.DoEvents();
            Check(drawer.Current == sellPanel && bagList.Items.Count == 0 && !sellButton.Enabled && ((Label)Get(sellPanel, "status")).Text.StartsWith("Aucun hôtel de vente"), "Sell panel without a server window differs");
            drawer.RequestClose(sellPanel); Application.DoEvents();
            Check(!drawer.IsOpen(sellPanel), "Sell panel did not close without a server window");
            NoPacket(peer, "Closing the panels without a server window sent a packet");
            form.Close(); Application.DoEvents();
        }
    }
}
