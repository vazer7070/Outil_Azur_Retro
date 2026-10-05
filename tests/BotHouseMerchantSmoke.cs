using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
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
using Outil_Azur_complet.Bot.Menus;
using Outil_Azur_complet.Bot.Panels;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Exchanges;
using Tool_BotProtocol.Game.Habitat;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Maps.Interfaces;
using Tool_BotProtocol.Game.Maps.Interactives;
using Tool_BotProtocol.Game.Perso.Inventory;
using Tool_BotProtocol.Utils.Crypto;

// Maisons, coffres et mode marchand (lot F8) selon dofus.aks.Houses / Exchange du client 1.34 et House.java / GameClient.java de
// StarLoco, sur un serveur fictif local avec des textes du client synthétiques : hP, hL±, hCK, hSK, hG, hV et paquets mal formés ;
// menu de la porte (titre « <objet> <maison> », ligne Chez X / Chez moi !, états O/S/L des compétences) ; hB<prix> après le contrôle
// des kamas, hS<prix>, GA507<compétence> chez soi, hG±, hQ ; magasin d'un marchand (ER4|id|cellule, ECK4, EL, EB, EBK/EBE), son
// propre magasin (ER6, ECK6, EMO+ avec prix, modification du prix, EMO-), Eq → Eq1|1|<taxe> → EQ, restriction AR bit 32 ; puis les
// volets Magasin (boîte DO_U_OFFLINEEXCHANGE : Non n'envoie rien, Oui envoie EQ) et Maison (vente, DO_U_BUY_HOUSE, menu intérieur)
// dans une fenêtre de jeu invisible, et les menus des acteurs (marchand, soi-même).
internal static class BotHouseMerchantSmoke
{
    private const int DoorMapId = 900097, IndoorMapId = 900098, Width = 6, Height = 6;
    private const short Self = 30, Door = 52, Chest = 47, MerchantCell = 7;
    private const int OwnHouse = 66, OtherHouse = 67;
    private const string SelfGm = "+30;1;0;42;Personnage de test;1;10^100;0;1,0,0,32";
    private const string MerchantGm = "+7;1;0;-3;Vendeur fictif;-5;30^100;ff;-1;-1;2a,,,,;Guilde fictive;1a,2b,3c,4d;0";
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
            Console.WriteLine("OK: maisons hP/hL/hCK/hSK/hG/hV, hB/hS/hQ/GA507, menu de la porte, magasin ER4/ECK4/EL/EB, ER6/EMO±, Eq/Eq1/EQ, volets Magasin et Maison, menus marchand et soi-même");
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
        while (!done()) { if (DateTime.UtcNow > end) throw new TimeoutException("House/merchant loopback timed out"); Application.DoEvents(); Thread.Sleep(5); }
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
    private static string Ch(int value) => Hash.caracteres_array[value].ToString();
    private const string Plain = "HhGaeaaaaa";
    private static string InteractiveCell(int gfx) =>
        Ch(33 | ((gfx & 8192) != 0 ? 2 : 0)) + "h" + Ch(8) + "aeaa" + Ch(2 | ((gfx & 4096) != 0 ? 1 : 0)) + Ch((gfx >> 6) & 63) + Ch(gfx & 63);
    private static Map DoorMap()
    {
        var objects = new Dictionary<int, int> { { Door, 6700 }, { Chest, 7350 } };
        var data = new StringBuilder();
        for (int cell = 0; cell < (2 * Width - 1) * Height; cell++) data.Append(objects.TryGetValue(cell, out int gfx) ? InteractiveCell(gfx) : Plain);
        return new Map { MapID = DoorMapId, MapWidth = Width, MapHeight = Height, X = 3, Y = -5, MapData = data.ToString() };
    }
    private static void WriteLang(string folder, string file, string family, string body) =>
        File.WriteAllText(Path.Combine(folder, file), "<?xml version='1.0' encoding='utf-8'?>\n<BotLang famille=\"" + family + "\" langue=\"fr\" version=\"1\" source=\"" + family
            + "_fr_1.swf\">\n" + body + "\n</BotLang>\n", new UTF8Encoding(false));

    private static void Run()
    {
        string previous = Environment.CurrentDirectory;
        string folder = Path.Combine(TestPaths.Work, "bot-house-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); Environment.CurrentDirectory = folder;
        string lang = Path.Combine(folder, "lang"); Directory.CreateDirectory(lang);
        WriteLang(lang, "houses.xml", "houses",
            "<entree table=\"H.h\" id=\"" + OwnHouse + "\" n=\"Petite maison fictive\" d=\"Une maison d'essai au bord de l'eau.\" />\n"
            + "<entree table=\"H.h\" id=\"" + OtherHouse + "\" n=\"Grande maison fictive\" d=\"\" />\n"
            + "<entree table=\"H.d\" id=\"" + DoorMapId + "\" c" + Door + "=\"" + OwnHouse + "\" />\n"
            + "<entree table=\"H.m\" id=\"" + IndoorMapId + "\" valeur=\"" + OwnHouse + "\" />\n"
            + "<entree table=\"H.ids\" id=\"0\" valeur=\"81\" />\n<entree table=\"H.ids\" id=\"1\" valeur=\"97\" />\n<entree table=\"H.ids\" id=\"2\" valeur=\"98\" />\n"
            + "<entree table=\"H.ids\" id=\"3\" valeur=\"100\" />\n<entree table=\"H.ids\" id=\"4\" valeur=\"108\" />");
        WriteLang(lang, "interactiveobjects.xml", "interactiveobjects",
            "<interactif id=\"20\" nom=\"Porte fictive\" type=\"5\" competences=\"84,97,98,108,81\" />\n<gfx id=\"6700\" interactif=\"20\" />\n"
            + "<interactif id=\"21\" nom=\"Coffre fictif\" type=\"6\" competences=\"104,105\" />\n<gfx id=\"7350\" interactif=\"21\" />");
        WriteLang(lang, "skills.xml", "skills",
            "<competence id=\"84\" nom=\"Entrer\" metier=\"1\" condition=\"!L?V:-\" />\n<competence id=\"97\" nom=\"Acheter\" metier=\"1\" condition=\"!O&amp;S?V:X\" />\n"
            + "<competence id=\"98\" nom=\"Vendre\" metier=\"1\" condition=\"O&amp;!S?V:X\" />\n<competence id=\"108\" nom=\"Modifier le prix de vente\" metier=\"1\" condition=\"O&amp;S?V:X\" />\n"
            + "<competence id=\"81\" nom=\"Verrouiller\" metier=\"1\" condition=\"O?V:X\" />\n<competence id=\"100\" nom=\"Déménager fictif\" metier=\"1\" condition=\"O?V:X\" />\n"
            + "<competence id=\"104\" nom=\"Ouvrir\" metier=\"1\" condition=\"O?V:-\" />\n<competence id=\"105\" nom=\"Verrouiller le coffre\" metier=\"1\" condition=\"O?V:X\" />");
        WriteLang(lang, "lang.xml", "lang",
            "<texte cle=\"MY_HOME\" valeur=\"Chez moi !\" />\n<texte cle=\"HOME_OF\" valeur=\"Chez %1\" />\n<texte cle=\"HOUSE_WITH_NO_OWNER\" valeur=\"Maison abandonnée\" />\n"
            + "<texte cle=\"HOUSE_WORD\" valeur=\"Maison\" />\n<texte cle=\"HOUSE_SALE\" valeur=\"Mise en vente de la maison\" />\n<texte cle=\"CANCEL_THE_SALE\" valeur=\"Annuler la vente\" />\n"
            + "<texte cle=\"DO_U_BUY_HOUSE\" valeur=\"Confirmez-vous l'achat de '%1' au prix de %2 kamas ?\" />\n<texte cle=\"NOT_ENOUGH_RICH\" valeur=\"Tu n'as pas assez de kamas (texte de test).\" />\n"
            + "<texte cle=\"BUY\" valeur=\"Acheter\" />\n<texte cle=\"SELL\" valeur=\"Vendre\" />\n<texte cle=\"CLOSE\" valeur=\"Fermer\" />\n<texte cle=\"PRICE\" valeur=\"Prix\" />\n"
            + "<texte cle=\"ORGANIZE_SHOP\" valeur=\"Organiser mon magasin\" />\n<texte cle=\"MERCHANT_MODE\" valeur=\"Passer en mode 'marchand'\" />\n"
            + "<texte cle=\"DO_U_OFFLINEEXCHANGE\" valeur=\"Taxe de %2% soit %3 kamas (texte de test).\" />\n<texte cle=\"GUILD_HOUSE_CONFIGURATION\" valeur=\"Paramètres de cette maison de guilde\" />\n"
            + "<texte cle=\"GUILD_HOUSE_RIGHT_8\" valeur=\"Accès sans code pour la guilde\" />");
        Check(LangData.Load(lang) == 4, "Synthetic lang files not loaded: " + string.Join(" / ", LangData.LoadWarnings));
        Check(HouseTexts.Name(OwnHouse) == "Petite maison fictive" && HouseTexts.Name(999) == "Maison n° 999" && HouseTexts.HouseForDoor(DoorMapId, Door) == OwnHouse
            && HouseTexts.HouseForDoor(DoorMapId, 3) == null && HouseTexts.HouseForIndoorMap(IndoorMapId) == OwnHouse && HouseTexts.HouseForIndoorMap(DoorMapId) == null
            && HouseTexts.IndoorSkills().SequenceEqual(new short[] { 81, 97, 98, 100, 108 }) && HouseTexts.RightName(HouseGuildRights.GuildEntersWithoutCode) == "Accès sans code pour la guilde",
            "House texts (H.h, H.d, H.m, H.ids, GUILD_HOUSE_RIGHT) are not read");

        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var logs = new ConcurrentQueue<string>();
        try
        {
            MessagesReception.Init();
            Check(new[] { ExchangeTypes.OfflineMerchant, ExchangeTypes.MyShop }.All(ExchangeRegistry.IsRegistered), "Exchange types 4 and 6 are not registered");
            Map.AllBotMaps[DoorMapId] = DoorMap();
            Map.AllBotMaps[IndoorMapId] = new Map { MapID = IndoorMapId, MapWidth = 3, MapHeight = 4, X = 7, Y = 2, MapData = string.Concat(Enumerable.Repeat(Plain, 18)) };
            InventoryObjects.FullInventory[2001] = new InventoryObjects { ID = 2001, Name = "Potion fictive", Type = 12, Level = 1, pods = 1 };
            InventoryObjects.FullInventory[2002] = new InventoryObjects { ID = 2002, Name = "Épée fictive", Type = 6, Level = 10, pods = 10 };
            InventoryObjects.FullInventory[2003] = new InventoryObjects { ID = 2003, Name = "Anneau fictif", Type = 9, Level = 1, pods = 1 };
            using (var account = new Accounts(new AccountConfig("synthetic-house", "synthetic", "loopback")))
            {
                account.Logger.log_event += (entry, color) => logs.Enqueue(entry.message);
                Task<Socket> accept = listener.AcceptSocketAsync();
                Complete(account.Connexion.ConnectToServer(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port)); Complete(accept);
                using (Socket peer = accept.Result)
                {
                    peer.ReceiveTimeout = 6000;
                    account.Game.character.SetPerso_Data(42, "Personnage de test", 25, 0, 8);
                    account.Game.character.Kamas = 5000;
                    Feed(account, "GDM|" + DoorMapId + "|date|key"); Expect(peer, "GI", "GDM did not request GI");
                    Feed(account, "GM|" + SelfGm + "|" + MerchantGm);
                    account.AccountStates = AccountStates.CONNECTED_INACTIVE;
                    Feed(account, "OAKO3~7d1~5~~7d#5#0#0#;3e9~7d2~1~1~;3ea~7d3~2~~");
                    Check(account.Game.character.Inventory.GetByInventoryId(3)?.Qua == 5 && account.Game.Map.GetActor(-3) is MerchantActor, "Synthetic inventory or merchant was not read");

                    Houses(account, peer, logs);
                    Merchant(account, peer, logs);
                    Windows(account, peer, logs);
                    NoPacket(peer, "Unexpected packet at the end of the test");
                }
            }
        }
        finally { listener.Stop(); Environment.CurrentDirectory = previous; }
    }

    /// <summary>Maisons : hP, hL±, menu de la porte, fenêtre de vente côté acheteur et propriétaire, menu intérieur, guilde, paquets mal formés.</summary>
    private static void Houses(Accounts account, Socket peer, ConcurrentQueue<string> logs)
    {
        HouseActions houses = account.Game.Interactions.House;
        var changes = 0; houses.Changed += () => changes++;
        Feed(account, "hP" + OwnHouse + "|Voisin fictif;1");
        Feed(account, "hP" + OtherHouse + "|;0;Guilde fictive;1,2,3,4");
        HouseInfo house = houses.Get(OwnHouse), other = houses.Get(OtherHouse);
        Check(house != null && house.OwnerName == "Voisin fictif" && house.HasOwner && house.IsForSale && !house.LocalOwner && !house.IsGuildHouse && house.Name == "Petite maison fictive"
            && house.OwnerLine() == "Chez Voisin fictif", "hP with an owner was not read");
        Check(other != null && !other.HasOwner && !other.IsForSale && other.IsGuildHouse && other.GuildName == "Guilde fictive" && other.GuildEmblem == "1,2,3,4"
            && other.OwnerLine() == "Maison abandonnée" && changes == 2, "hP of a guild house without owner was not read");
        Check(houses.Houses.Select(entry => entry.Id).SequenceEqual(new[] { OwnHouse, OtherHouse }) && houses.Get(999) == null, "Houses list differs");
        Check(houses.ForDoor(DoorMapId, Door)?.Id == OwnHouse && houses.ForDoor(DoorMapId, 3) == null && houses.CurrentHouse == null && !houses.IsAtHome, "ForDoor / CurrentHouse differ on the door map");

        // Menu de la porte : maison d'un autre joueur, en vente → Entrer et Acheter ; titre « Porte fictive Petite maison fictive », ligne « Chez Voisin fictif ».
        Map map = account.Game.Map;
        Interactives door = map.Interactives[Door], chest = map.Interactives[Chest];
        Check(door.ClientType == 5 && door.ClientSkills.SequenceEqual(new short[] { 84, 97, 98, 108, 81 }) && chest.ClientType == 6, "Synthetic door or chest differs");
        IReadOnlyList<MenuEntry> entries = InteractiveMenuProvider.Entries(door, account.Game);
        Check(entries.Select(e => e.Texte + ":" + e.Activé).SequenceEqual(new[] { "Entrer:True", "Acheter:True" }), "Door menu of a house for sale differs: " + string.Join("|", entries.Select(e => e.Texte + ":" + e.Activé)));
        using (ContextMenuStrip strip = InteractiveMenuProvider.BuildMenu(door, entries, null, new ActorMenuContext(null, null, Door, account, null, null, Keys.None)))
            Check(strip.Items[0].Text == "Porte fictive Petite maison fictive" && strip.Items[1].Name == "house-owner" && strip.Items[1].Text == "Chez Voisin fictif" && strip.Items.Count == 4,
                "Door menu header differs: " + string.Join("|", strip.Items.Cast<ToolStripItem>().Select(item => item.Text)));
        entries = InteractiveMenuProvider.Entries(chest, account.Game);
        Check(entries.Select(e => e.Texte + ":" + e.Activé).SequenceEqual(new[] { "Ouvrir:False" }), "Chest menu away from home differs: " + string.Join("|", entries.Select(e => e.Texte + ":" + e.Activé)));

        // hL+ : la maison devient la sienne (verrouillée, en vente à 15 000) ; menu du propriétaire.
        Feed(account, "hL+|" + OwnHouse + ";1;1;15000");
        house = houses.Get(OwnHouse);
        Check(house.LocalOwner && house.IsLocked && house.IsForSale && house.Price == 15000 && house.OwnerLine() == "Chez moi !", "hL+ was not read");
        entries = InteractiveMenuProvider.Entries(door, account.Game);
        Check(entries.Select(e => e.Texte + ":" + e.Activé).SequenceEqual(new[] { "Entrer:False", "Modifier le prix de vente:True", "Verrouiller:True" }),
            "Door menu of the own locked house differs: " + string.Join("|", entries.Select(e => e.Texte + ":" + e.Activé)));
        using (ContextMenuStrip strip = InteractiveMenuProvider.BuildMenu(door, entries, null, new ActorMenuContext(null, null, Door, account, null, null, Keys.None)))
            Check(strip.Items[1].Text == "Chez moi !", "Owner line of the own house differs");
        Feed(account, "hL-|" + OwnHouse);
        Check(!houses.Get(OwnHouse).LocalOwner && houses.Get(OwnHouse).IsForSale, "hL- did not drop the ownership");
        Feed(account, "hL+|" + OwnHouse + ";0;1;15000"); Feed(account, "hL");
        Check(!houses.Get(OwnHouse).LocalOwner, "Empty hL did not clear the owned houses");

        // Fenêtre de vente côté acheteur : hCK, kamas insuffisants, hB<prix>, hV.
        Check(!Sent(houses.BuyAsync()) && !Sent(houses.SellAsync(10)) && !Sent(houses.LeaveAsync()), "Sale actions were accepted without hCK");
        Feed(account, "hCK" + OwnHouse + "|15000");
        Check(houses.IsOpen && houses.SaleHouseId == OwnHouse && houses.SalePrice == 15000 && houses.SaleHouse?.Name == "Petite maison fictive" && account.AccountStates == AccountStates.DIALOG,
            "hCK did not open the sale window");
        InteractionResult poor = Result(houses.BuyAsync());
        Check(!poor.Sent && poor.Message.StartsWith("Tu n'as pas assez de kamas") && !Sent(houses.SellAsync(100)), "Buying without kamas or selling someone else's house was accepted");
        NoPacket(peer, "Refused house actions reached the server");
        account.Game.character.Kamas = 20000;
        Check(Sent(houses.BuyAsync()) && houses.IsPending, "hB was not sent"); Expect(peer, "hB15000", "Buy does not send hB<prix>");
        Check(!Sent(houses.BuyAsync()), "Second hB accepted while pending");
        Feed(account, "hV");
        Check(!houses.IsOpen && !houses.IsPending && houses.SaleHouseId == -1 && account.AccountStates == AccountStates.CONNECTED_INACTIVE, "hV did not close the sale window");
        Feed(account, "hP" + OwnHouse + "|Personnage de test;0"); Feed(account, "hL+|" + OwnHouse + ";0;0;0");
        house = houses.Get(OwnHouse);
        Check(house.LocalOwner && !house.IsForSale && house.Price == 0 && house.OwnerName == "Personnage de test", "Reload after the purchase was not read");

        // Côté propriétaire : hCK avec prix 0, hS<prix>, hV puis hSK ; annulation hS0 ; hSE.
        Feed(account, "hCK" + OwnHouse + "|0");
        Check(houses.IsOpen && houses.SalePrice == 0 && !Sent(houses.BuyAsync()) && !Sent(houses.SellAsync(-1)), "Owner window accepted a purchase or a negative price");
        Check(Sent(houses.SellAsync(12000))); Expect(peer, "hS12000", "Sell does not send hS<prix>");
        Feed(account, "hV"); Feed(account, "hSK" + OwnHouse + "|12000");
        Check(!houses.IsOpen && houses.Get(OwnHouse).IsForSale && houses.Get(OwnHouse).Price == 12000 && houses.LastMessage.Contains("12000"), "hSK was not read");
        Feed(account, "hCK" + OwnHouse + "|12000");
        Check(Sent(houses.CancelSaleAsync())); Expect(peer, "hS0", "Cancel does not send hS0");
        Feed(account, "hSE"); Check(!houses.IsPending && houses.LastMessage.Contains("refusée"), "hSE was not read");
        Check(Sent(houses.LeaveAsync())); Expect(peer, "hV", "Leave does not send hV");
        Feed(account, "hV"); Feed(account, "hSK" + OwnHouse + "|0");
        Check(!houses.Get(OwnHouse).IsForSale && houses.Get(OwnHouse).Price == 0, "hSK with price 0 did not cancel the sale");

        // Menu intérieur : refusé hors d'une maison, puis GA507<compétence> chez soi ; hG ; hQ.
        Check(!Sent(houses.UseIndoorSkillAsync(97)) && !Sent(houses.KickAsync(43)) && !Sent(houses.RequestGuildStateAsync()), "Indoor actions were accepted outside a house");
        Feed(account, "GDM|" + IndoorMapId + "|date|key"); Expect(peer, "GI", "Indoor GDM did not request GI");
        Feed(account, SelfGmLine()); account.AccountStates = AccountStates.CONNECTED_INACTIVE;
        Check(houses.CurrentHouseId == OwnHouse && houses.CurrentHouse.LocalOwner && houses.IsAtHome, "CurrentHouse / IsAtHome differ inside the own house");
        entries = InteractiveMenuProvider.Entries(chest, account.Game);
        Check(entries.Select(e => e.Texte + ":" + e.Activé).SequenceEqual(new[] { "Ouvrir:True", "Verrouiller le coffre:True" }), "Chest menu at home differs: " + string.Join("|", entries.Select(e => e.Texte + ":" + e.Activé)));
        Check(Sent(houses.UseIndoorSkillAsync(98))); Expect(peer, "GA50798", "Indoor skill does not send GA507<compétence>");
        Check(!Sent(houses.UseIndoorSkillAsync(5)), "Unknown indoor skill was accepted");
        InteractionResult moved = Result(houses.UseIndoorSkillAsync(100));
        Check(moved.Sent && moved.Message.Contains("StarLoco ne traite pas")); Expect(peer, "GA507100", "Skill 100 was not sent as the client does");
        Check(Sent(houses.KickAsync(43))); Expect(peer, "hQ43", "Kick does not send hQ<joueur>");
        Check(!Sent(houses.KickAsync(42)) && !Sent(houses.RequestGuildStateAsync()), "Kick of self or hG without a guild was accepted");
        Feed(account, "hG" + OwnHouse + ";Guilde fictive;1,2,3,4;9");
        house = houses.Get(OwnHouse);
        Check(house.GuildRightsKnown && house.IsGuildHouse && house.GuildRights == 9 && house.GuildName == "Guilde fictive", "hG with a guild was not read");
        Feed(account, "hG" + OwnHouse);
        house = houses.Get(OwnHouse);
        Check(house.GuildRightsKnown && !house.IsGuildHouse && house.GuildRights == 0, "hG without a guild was not read");
        NoPacket(peer, "House receptions triggered a reply");

        // Paquets mal formés : journalisés, jamais propagés, état inchangé.
        int before = logs.Count;
        foreach (string packet in new[] { "hPx", "hP" + OwnHouse, "hP|a;1", "hL*|a", "hL+|x;1;1", "hCK" + OwnHouse, "hCKx|5", "hCK" + OwnHouse + "|y", "hG", "hGx;a;b;c", "hG" + OwnHouse + ";a;b;z", "hSK" + OwnHouse, "hSKa|b" })
            Feed(account, packet);
        house = houses.Get(OwnHouse);
        Check(!houses.IsOpen && house.LocalOwner && house.OwnerName == "Personnage de test" && !house.IsForSale && houses.Houses.Count == 2 && account.AccountStates == AccountStates.CONNECTED_INACTIVE,
            "Malformed house packets changed the state");
        Check(logs.Count >= before + 11 && !logs.Any(entry => entry.Contains("non appliqué")), "Malformed house packets were not logged or raised an exception");
        NoPacket(peer, "Malformed house packets triggered a reply");
        Feed(account, "GDM|" + DoorMapId + "|date|key"); Expect(peer, "GI", "Return GDM did not request GI");
        Feed(account, "GM|" + SelfGm + "|" + MerchantGm); account.AccountStates = AccountStates.CONNECTED_INACTIVE;
        Check(houses.CurrentHouse == null && !houses.IsAtHome, "CurrentHouse stayed set after leaving the house");
    }

    private static string SelfGmLine() => "GM|" + SelfGm;

    /// <summary>Magasins : ER4|id|cellule, ECK4, EL, EB, EBK/EBE, EV ; ER6, ECK6, EMO+ avec prix, prix modifié, EMO- ; Eq, Eq1, EQ ; AR bit 32 ; Ei ; Clear.</summary>
    private static void Merchant(Accounts account, Socket peer, ConcurrentQueue<string> logs)
    {
        MerchantExchange shop = account.Game.Interactions.Merchant;
        ExchangeRegistry registry = account.Game.Interactions.Exchanges;
        Check(registry.For(4) == shop && registry.For(6) == shop && registry.Get<MerchantExchange>() == shop && shop.CanBeMerchant, "Registry does not map types 4 and 6 to the merchant window");
        Check(!Sent(shop.OpenAsync(999, 1)) && !Sent(shop.BuyAsync(1, 1)) && !Sent(shop.AddToShopAsync(3, 1, 10)) && !Sent(shop.LeaveAsync()) && !Sent(shop.ConfirmMerchantModeAsync()),
            "Merchant actions accepted without an open shop");
        NoPacket(peer, "Refused merchant actions reached the server");
        Check(Sent(shop.OpenAsync(-3, MerchantCell))); Expect(peer, "ER4|-3|7", "Open does not send ER4|<marchand>|<cellule>");
        Feed(account, "ECK4|-3");
        Check(shop.IsOpen && shop.IsBuying && !shop.IsOrganizing && shop.ExchangeType == 4 && shop.MerchantId == -3 && shop.MerchantName == "Vendeur fictif"
            && registry.Current == shop && account.AccountStates == AccountStates.BUYING && !shop.ContentReceived, "ECK4 did not open the merchant shop");
        Feed(account, "EL5001;3;2001;7d#5#0#0#;150|5002;1;2002;;2500");
        IReadOnlyList<MerchantItem> items = shop.Items;
        Check(shop.ContentReceived && items.Count == 2 && items[0].Id == 5001 && items[0].Quantity == 3 && items[0].TemplateId == 2001 && items[0].Effects == "7d#5#0#0#" && items[0].Price == 150
            && items[0].Name == "Potion fictive" && items[1].Id == 5002 && items[1].Price == 2500 && shop.TotalPrice == 2950, "EL of a merchant was not read");
        Check(!Sent(shop.BuyAsync(5001, 5)) && !Sent(shop.BuyAsync(5003, 1)) && !Sent(shop.AddToShopAsync(3, 1, 10)), "Invalid purchases were accepted");
        account.Game.character.Kamas = 200;
        InteractionResult poor = Result(shop.BuyAsync(5002, 1));
        Check(!poor.Sent && poor.Message.StartsWith("Tu n'as pas assez de kamas"), "Purchase above the kamas was accepted");
        NoPacket(peer, "Refused purchases reached the server");
        account.Game.character.Kamas = 20000;
        Check(Sent(shop.BuyAsync(5001, 2)) && shop.IsPending); Expect(peer, "EB5001|2", "Buy does not send EB<lot>|<quantité>");
        Check(!Sent(shop.BuyAsync(5001, 1)), "Second purchase accepted while pending");
        Feed(account, "EBK"); Check(!shop.IsPending && shop.LastMessage.Contains("accepté") && !account.Game.Interactions.Shop.IsOpen, "EBK was not relayed to the merchant shop");
        Feed(account, "EL5001;1;2001;7d#5#0#0#;150|5002;1;2002;;2500"); Check(shop.Items[0].Quantity == 1, "Relisted EL was not applied");
        Check(Sent(shop.BuyAsync(5002, 1))); Expect(peer, "EB5002|1", "Second buy was not sent");
        Feed(account, "EBE"); Check(!shop.IsPending && shop.LastMessage.Contains("refusé"), "EBE was not relayed to the merchant shop");
        int before = logs.Count;
        Feed(account, "EL5003;x;2001;;10|bad|5004;1;2003;;7");
        Check(shop.Items.Count == 1 && shop.Items[0].Id == 5004 && logs.Count > before, "Unreadable EL lots were not skipped and logged");
        Check(Sent(shop.LeaveAsync())); Expect(peer, "EV", "Leave does not send EV");
        Feed(account, "EV");
        Check(!shop.IsOpen && shop.ExchangeType == -1 && shop.Items.Count == 0 && shop.MerchantId == -1 && registry.Current == null && account.AccountStates == AccountStates.CONNECTED_INACTIVE,
            "EV did not close the merchant shop");

        // Son propre magasin : ER6, ECK6 + EL vide, EMO+ avec prix, prix modifié avec la quantité du lot, EMO-, Eq/Eq1/EQ.
        Check(Sent(shop.OrganizeAsync())); Expect(peer, "ER6", "Organize does not send ER6");
        Feed(account, "ECK6"); Feed(account, "EL");
        Check(shop.IsOrganizing && !shop.IsBuying && shop.MerchantId == 42 && shop.ContentReceived && shop.Items.Count == 0 && account.AccountStates == AccountStates.SELLING, "ECK6 + empty EL did not open the own shop");
        Check(!Sent(shop.AddToShopAsync(3, 6, 10)) && !Sent(shop.AddToShopAsync(3, 1, 0)) && !Sent(shop.AddToShopAsync(1001, 1, 10)) && !Sent(shop.AddToShopAsync(4242, 1, 10)) && !Sent(shop.BuyAsync(3, 1)),
            "Invalid shop additions were accepted");
        InteractionResult empty = Result(shop.AskMerchantModeAsync());
        Check(!empty.Sent && empty.Message.Contains("Im123"), "Merchant mode with an empty shop was requested");
        NoPacket(peer, "Refused shop additions reached the server");
        Check(Sent(shop.AddToShopAsync(3, 2, 500))); Expect(peer, "EMO+3|2|500", "Adding does not send EMO+<objet>|<quantité>|<prix>");
        Feed(account, "EL3;2;2001;;500"); Check(shop.Items.Single().Price == 500 && shop.TotalPrice == 1000, "Own shop EL was not read");
        Check(Sent(shop.ChangePriceAsync(3, 700))); Expect(peer, "EMO+3|2|700", "Price change does not send EMO+<lot>|<quantité du lot>|<prix>");
        Check(!Sent(shop.ChangePriceAsync(3, 0)) && !Sent(shop.RemoveFromShopAsync(3, 3)) && !Sent(shop.RemoveFromShopAsync(9, 1)), "Invalid price change or removal accepted");
        Check(Sent(shop.RemoveFromShopAsync(3, 1))); Expect(peer, "EMO-3|1", "Removal does not send EMO-<lot>|<quantité>");
        Feed(account, "EiK+77|1|2003||7"); Check(shop.Items.Count == 2 && shop.Items[1].Id == 77 && shop.Items[1].Price == 7, "EiK+ was not read");
        Feed(account, "EiK-77"); Check(shop.Items.Count == 1, "EiK- was not read");
        Feed(account, "EiKx"); Feed(account, "EiK+a|1|2|3|4"); Feed(account, "EiE"); Check(shop.Items.Count == 1 && shop.LastMessage.Contains("EiE"), "Malformed Ei changed the shop");
        Check(Sent(shop.AskMerchantModeAsync()) && shop.TaxRequested); Expect(peer, "Eq", "Merchant mode does not send Eq");
        Check(!Sent(shop.AskMerchantModeAsync()) && !Sent(shop.ConfirmMerchantModeAsync()), "Eq sent twice or EQ before Eq1");
        Feed(account, "Eq1|1|50");
        MerchantTax tax = shop.PendingTax;
        Check(tax != null && !shop.TaxRequested && tax.Type == 1 && Math.Abs(tax.Rate - 0.1) < 0.0001 && tax.Tax == 50 && shop.LastMessage.Contains("50 kamas"), "Eq1 was not read");
        shop.DeclineMerchantMode(); Check(shop.PendingTax == null && !shop.MerchantModeRequested, "Decline did not drop the tax");
        NoPacket(peer, "Decline sent a packet");
        Check(Sent(shop.AskMerchantModeAsync())); Expect(peer, "Eq", "Second Eq was not sent");
        Feed(account, "Eq1|1|50"); account.Game.character.Kamas = 10;
        InteractionResult poorTax = Result(shop.ConfirmMerchantModeAsync());
        Check(!poorTax.Sent && poorTax.Message.Contains("Im176") && shop.PendingTax != null, "EQ was sent without the kamas of the tax");
        account.Game.character.Kamas = 20000;
        Check(Sent(shop.ConfirmMerchantModeAsync()) && shop.PendingTax == null && shop.MerchantModeRequested); Expect(peer, "EQ", "Confirm does not send EQ");
        foreach (string packet in new[] { "Eq1", "Eq1|a|b|c", "Eq1|1|-5" }) Feed(account, packet);
        Check(shop.PendingTax == null && !logs.Any(entry => entry.Contains("non appliqué")), "Malformed Eq1 was applied or raised");
        Feed(account, "EV"); Check(!shop.IsOpen && account.AccountStates == AccountStates.CONNECTED_INACTIVE, "EV did not close the own shop");

        // Restriction AR (bit 32) : plus de mode marchand ; puis Clear.
        Feed(account, "ARw"); Check(!shop.CanBeMerchant && !Sent(shop.OrganizeAsync()) && !Sent(shop.AskMerchantModeAsync()), "AR bit 32 did not forbid the merchant mode");
        Feed(account, "AR6bk"); Check(shop.CanBeMerchant, "AR without the bit kept the restriction");
        Feed(account, "ECK6"); Feed(account, "EL3;2;2001;;500"); Feed(account, "Eq1|1|1");
        account.Game.Interactions.Clear();
        Check(!shop.IsOpen && shop.Items.Count == 0 && shop.PendingTax == null && !shop.MerchantModeRequested && account.Game.Interactions.House.Houses.Count == 0
            && account.AccountStates == AccountStates.CONNECTED_INACTIVE, "Clear kept the shop or the houses");
        NoPacket(peer, "Clear sent a packet");
    }

    /// <summary>Volets Magasin et Maison dans une fenêtre de jeu invisible, menus du marchand et de soi-même, clic droit global.</summary>
    private static void Windows(Accounts account, Socket peer, ConcurrentQueue<string> logs)
    {
        MerchantExchange shop = account.Game.Interactions.Merchant;
        HouseActions houses = account.Game.Interactions.House;
        var merchantActor = account.Game.Map.GetActor(-3) as MerchantActor;
        var selfActor = account.Game.Map.Self;
        Check(merchantActor != null && selfActor != null, "Merchant or self actor missing");
        using (var form = new GameClientFullform(account))
        {
            form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); Application.DoEvents();
            PanelHost drawer = form.Panels;
            var panel = drawer.Get<MerchantPanel>(); var housePanel = drawer.Get<HouseIndoorPanel>();
            Check(panel != null && housePanel != null && drawer.Current == null, "Merchant or house panel is not registered in the drawer");
            ContextMenuStrip global = form.GlobalMenu;
            Check(global.Items.Find("house", false).Length == 1 && global.Items.Find("merchant", false).Length == 1, "Global menu lacks the house or merchant entries");
            NoPacket(peer, "Opening the game window sent a packet");

            // Menus des acteurs : marchand → Acheter (ER4) ; soi-même → organiser / mode marchand, rien sous restriction.
            var context = new ActorMenuContext(merchantActor, null, MerchantCell, account, null, drawer, Keys.None);
            IReadOnlyList<MenuEntry> entries = ActorMenuRegistry.Default.EntriesFor(merchantActor, account.Game);
            MenuEntry buy = entries.FirstOrDefault(entry => entry.Texte == "Acheter");
            Check(buy != null && buy.ParDéfaut && buy.Activé, "Merchant menu lacks « Acheter »: " + string.Join("|", entries.Select(entry => entry.Texte)));
            Complete(buy.Action(context)); Expect(peer, "ER4|-3|7", "Merchant « Acheter » does not send ER4");
            entries = ActorMenuRegistry.Default.EntriesFor(selfActor, account.Game);
            Check(entries.Any(entry => entry.Texte == "Organiser mon magasin") && entries.Any(entry => entry.Texte == "Passer en mode 'marchand'"), "Self menu lacks the shop entries: " + string.Join("|", entries.Select(entry => entry.Texte)));
            Feed(account, "ARw");
            entries = ActorMenuRegistry.Default.EntriesFor(selfActor, account.Game);
            Check(!entries.Any(entry => entry.Texte == "Organiser mon magasin"), "Self menu kept the shop entries under the restriction");
            Feed(account, "AR6bk");

            // Volet Magasin, achat : ECK4 + EL depuis un fil réseau, lignes, EB, EBK, Fermer → EV.
            FeedFromNetwork(account, "ECK4|-3"); FeedFromNetwork(account, "EL5001;3;2001;7d#5#0#0#;150|5002;1;2002;;2500");
            PumpUntil(() => drawer.Current == panel);
            var lots = (ListView)Get(panel, "merchantList"); var bag = (ListView)Get(panel, "merchantBag");
            var buyButton = (Control)Get(panel, "merchantBuy"); var removeButton = (Control)Get(panel, "merchantRemove"); var repriceButton = (Control)Get(panel, "merchantReprice");
            var addButton = (Control)Get(panel, "merchantAdd"); var modeButton = (Control)Get(panel, "merchantMode"); var leaveButton = (Control)Get(panel, "merchantLeave");
            Check(((Label)Get(panel, "merchantHeading")).Text == "Magasin de Vendeur fictif" && lots.Items.Count == 2 && RowWithTag(lots, 5001u) != null && !((Panel)Get(panel, "bagArea")).Visible
                && buyButton.Visible && !removeButton.Visible && !buyButton.Enabled, "Merchant panel content differs");
            RowWithTag(lots, 5001u).Selected = true; Application.DoEvents();
            ((NumericUpDown)Get(panel, "merchantQuantity")).Value = 2;
            Check(buyButton.Enabled, "Buy button disabled for a selected lot");
            Click(buyButton); Expect(peer, "EB5001|2", "Buy button does not send EB<lot>|<quantité>");
            Check(!buyButton.Enabled, "Buy button enabled while pending");
            Feed(account, "EBK"); Feed(account, "EL5001;1;2001;7d#5#0#0#;150|5002;1;2002;;2500"); Application.DoEvents();
            Check(RowWithTag(lots, 5001u).SubItems[1].Text == "1", "Relisted quantity not shown");
            Click(leaveButton); Expect(peer, "EV", "Close button does not send EV");
            Check(drawer.Current == panel, "Merchant panel closed before the server");
            Feed(account, "EV"); Application.DoEvents();
            Check(!drawer.IsOpen(panel) && lots.Items.Count == 0, "Merchant panel stayed open after EV");

            // Volet Magasin, organisation : ECK6 + EL vide, sac, EMO+ avec prix, prix modifié, Mode marchand → Eq → Eq1 → boîte ; Non n'envoie rien, Oui envoie EQ.
            FeedFromNetwork(account, "ECK6"); FeedFromNetwork(account, "EL");
            PumpUntil(() => drawer.Current == panel);
            Check(((Panel)Get(panel, "bagArea")).Visible && bag.Items.Count == 2 && RowWithTag(bag, 3u) != null && RowWithTag(bag, 1001u) == null && !buyButton.Visible && removeButton.Visible,
                "Own shop panel content differs");
            RowWithTag(bag, 3u).Selected = true; Application.DoEvents();
            ((NumericUpDown)Get(panel, "bagQuantity")).Value = 1; ((NumericUpDown)Get(panel, "bagPrice")).Value = 250;
            Check(addButton.Enabled, "Add button disabled for a bag item");
            Click(addButton); Expect(peer, "EMO+3|1|250", "Add button does not send EMO+<objet>|<quantité>|<prix>");
            Feed(account, "EL3;1;2001;;250"); Application.DoEvents();
            Check(lots.Items.Count == 1 && ((Label)Get(panel, "merchantTotal")).Text.StartsWith("1 lot(s)"), "Own shop list not refreshed");
            RowWithTag(lots, 3u).Selected = true; Application.DoEvents();
            ((NumericUpDown)Get(panel, "merchantPrice")).Value = 300;
            Click(repriceButton); Expect(peer, "EMO+3|1|300", "Price button does not send EMO+<lot>|<quantité du lot>|<prix>");
            ((NumericUpDown)Get(panel, "merchantQuantity")).Value = 1;
            Click(removeButton); Expect(peer, "EMO-3|1", "Remove button does not send EMO-<lot>|<quantité>");
            Check(modeButton.Enabled, "Merchant mode button disabled");
            Click(modeButton); Expect(peer, "Eq", "Merchant mode button does not send Eq");
            Check(!modeButton.Enabled, "Merchant mode button enabled while waiting for Eq1");
            FeedFromNetwork(account, "Eq1|1|50");
            PumpUntil(() => panel.TaxDialog != null);
            Form box = panel.TaxDialog;
            Check(box != null && BotDialogs.OpenDialogs.Contains(box) && box.Text == "Passer en mode 'marchand'" && DialogMessage(box).StartsWith("Taxe de 0.1% soit 50 kamas")
                && DialogMessage(box).Contains("StarLoco déconnecte") && All(box).OfType<Button>().Select(button => button.Text).OrderBy(text => text).SequenceEqual(new[] { "Non", "Oui" }),
                "Tax dialog is missing or differs from DO_U_OFFLINEEXCHANGE: " + (box == null ? "null" : DialogMessage(box)));
            NoPacket(peer, "EQ was sent before the answer");
            Answer(box, "Non"); PumpUntil(() => panel.TaxDialog == null && shop.PendingTax == null);
            NoPacket(peer, "« Non » sent a packet");
            Check(modeButton.Enabled, "Merchant mode button not re-enabled after « Non »");
            Click(modeButton); Expect(peer, "Eq", "Second Eq was not sent");
            FeedFromNetwork(account, "Eq1|1|50"); PumpUntil(() => panel.TaxDialog != null);
            Answer(panel.TaxDialog, "Oui"); Expect(peer, "EQ", "« Oui » does not send EQ");
            PumpUntil(() => panel.TaxDialog == null && shop.MerchantModeRequested);
            Check(!modeButton.Enabled && ((Label)Get(panel, "merchantStatus")).Text.Contains("EQ envoyé"), "Status after EQ differs");
            Feed(account, "EV"); Application.DoEvents(); Check(!drawer.IsOpen(panel), "Own shop panel stayed open after EV");

            // Volet Maison, propriétaire : hL+ puis hCK|0 depuis un fil réseau → vente à 12 000 → hS12000 ; hV ferme.
            Feed(account, "hP" + OwnHouse + "|Personnage de test;0"); Feed(account, "hL+|" + OwnHouse + ";0;0;0");
            FeedFromNetwork(account, "hCK" + OwnHouse + "|0");
            PumpUntil(() => drawer.Current == housePanel);
            var saleArea = (Panel)Get(housePanel, "saleArea"); var salePrice = (NumericUpDown)Get(housePanel, "salePrice");
            var sellButton = (Control)Get(housePanel, "saleSell"); var cancelButton = (Control)Get(housePanel, "saleCancel"); var buyHouse = (Control)Get(housePanel, "saleBuy");
            Check(saleArea.Visible && sellButton.Visible && sellButton.Enabled && !cancelButton.Visible && !buyHouse.Visible && ((Label)Get(housePanel, "houseTitle")).Text == "Petite maison fictive"
                && ((Label)Get(housePanel, "houseOwner")).Text.StartsWith("Chez moi !"), "House panel (owner) differs");
            salePrice.Value = 12000; Click(sellButton); Expect(peer, "hS12000", "Sell button does not send hS<prix>");
            Feed(account, "hV"); Feed(account, "hSK" + OwnHouse + "|12000"); Application.DoEvents();
            Check(!drawer.IsOpen(housePanel), "House panel stayed open after hV");

            // Volet Maison, acheteur : DO_U_BUY_HOUSE ; Non n'envoie rien, Oui envoie hB<prix> ; kamas insuffisants → aucune boîte.
            Feed(account, "hL-|" + OwnHouse); Feed(account, "hP" + OwnHouse + "|Voisin fictif;1");
            FeedFromNetwork(account, "hCK" + OwnHouse + "|15000");
            PumpUntil(() => drawer.Current == housePanel);
            Check(buyHouse.Visible && buyHouse.Enabled && !sellButton.Visible && ((Label)Get(housePanel, "houseOwner")).Text.StartsWith("Chez Voisin fictif"), "House panel (buyer) differs");
            account.Game.character.Kamas = 100;
            Click(buyHouse); Application.DoEvents();
            Check(housePanel.BuyDialog == null, "Buy dialog shown without the kamas");
            NoPacket(peer, "Buy without kamas sent a packet");
            account.Game.character.Kamas = 20000;
            Click(buyHouse); PumpUntil(() => housePanel.BuyDialog != null);
            Form ask = housePanel.BuyDialog;
            Check(DialogMessage(ask) == "Confirmez-vous l'achat de 'Petite maison fictive' au prix de 15000 kamas ?", "DO_U_BUY_HOUSE text differs: " + DialogMessage(ask));
            Answer(ask, "Non"); PumpUntil(() => housePanel.BuyDialog == null);
            NoPacket(peer, "« Non » on the house purchase sent a packet");
            Click(buyHouse); PumpUntil(() => housePanel.BuyDialog != null);
            Answer(housePanel.BuyDialog, "Oui"); Expect(peer, "hB15000", "« Oui » does not send hB<prix>");
            Feed(account, "hV"); Application.DoEvents(); Check(!drawer.IsOpen(housePanel), "House panel stayed open after hV (buyer)");

            // Menu intérieur chez soi : boutons des compétences visibles selon leur critère, clic → GA507 ; paramètres de guilde absents sans guilde.
            Feed(account, "hP" + OwnHouse + "|Personnage de test;0"); Feed(account, "hL+|" + OwnHouse + ";0;0;0");
            Feed(account, "GDM|" + IndoorMapId + "|date|key"); Expect(peer, "GI", "Indoor GDM did not request GI");
            Feed(account, SelfGmLine()); account.AccountStates = AccountStates.CONNECTED_INACTIVE;
            drawer.Show(housePanel); Application.DoEvents();
            Control sell98 = All(housePanel.View).First(control => control.Name == "house-skill-98"), buy97 = All(housePanel.View).First(control => control.Name == "house-skill-97");
            Check(sell98.Visible && sell98.Enabled && !buy97.Visible && !((Panel)Get(housePanel, "guildArea")).Visible, "Indoor menu buttons differ");
            Click(sell98); Expect(peer, "GA50798", "Indoor button does not send GA507<compétence>");
            drawer.RequestClose(housePanel); Application.DoEvents();
            Check(!drawer.IsOpen(housePanel), "House panel did not close without a server window");
            form.Close(); Application.DoEvents();
        }
    }
}
