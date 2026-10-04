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
using Tool_BotProtocol.Game.NPC;
using Tool_BotProtocol.Game.Perso.Inventory;
using Tool_BotProtocol.Game.Perso.Inventory.Enums;

// Inventaire selon le client 1.34 (OAK/OAE/OQ/OR/OM/OC/OS/OT/OK, envois OM/OU/OD) et boutique PNJ
// (ER0/ECK0/EL/EB/EBK/EBE/ES/ESK/ESE/EV) sur un serveur fictif local, puis les volets Inventaire et Boutique.
internal static class BotShopSmoke
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
        try { Application.EnableVisualStyles(); Run(); Console.WriteLine("OK: inventaire OAK/OAE/OQ/OR/OM/OC/OS/OT/OK hexadécimal, envois OM/OU/OD, boutique ER0/ECK0/EL/EB/ES/EV, volets Inventaire et Boutique"); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static object Get(object target, string field) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static void PumpUntil(Func<bool> done)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(6);
        while (!done()) { if (DateTime.UtcNow > end) throw new TimeoutException("Shop loopback timed out"); Application.DoEvents(); Thread.Sleep(5); }
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
    private static ListViewItem RowWithTag(ListView list, object tag) => list.Items.Cast<ListViewItem>().FirstOrDefault(row => Equals(row.Tag, tag));

    private static void Run()
    {
        string previous = Environment.CurrentDirectory;
        string folder = Path.Combine(TestPaths.Work, "bot-shop"); Directory.CreateDirectory(folder); Environment.CurrentDirectory = folder;
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            MessagesReception.Init();
            Map.AllBotMaps[900091] = new Map { MapID = 900091, MapWidth = 3, MapHeight = 4, MapData = string.Concat(Enumerable.Repeat("HhGaeaaaaa", 18)) };
            InventoryObjects.FullInventory[2001] = new InventoryObjects { ID = 2001, Name = "Potion fictive", Type = 12, Level = 1, pods = 1 };
            InventoryObjects.FullInventory[2002] = new InventoryObjects { ID = 2002, Name = "Épée fictive", Type = 6, Level = 10, pods = 10 };
            InventoryObjects.FullInventory[2003] = new InventoryObjects { ID = 2003, Name = "Anneau fictif", Type = 9, Level = 1, pods = 1 };
            PNJ.AllPNJ[100] = new PNJ(100, 100, null) { Name = "Marchand fictif" };
            using (var account = new Accounts(new AccountConfig("synthetic-shop", "synthetic", "loopback")))
            {
                Task<Socket> accept = listener.AcceptSocketAsync();
                Complete(account.Connexion.ConnectToServer(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port)); Complete(accept);
                using (Socket peer = accept.Result)
                {
                    peer.ReceiveTimeout = 6000;
                    account.Game.character.SetPerso_Data(42, "Personnage de test", 25, 0, 8);
                    account.Game.character.Kamas = 1000;
                    Feed(account, "GDM|900091|date|key"); Check(Read(peer) == "GI", "GDM did not request GI");
                    Feed(account, "GM|+0;1;0;42;Personnage de test;1;100^100;0|+5;1;0;-8;100;-4;1^100;0");
                    account.AccountStates = AccountStates.CONNECTED_INACTIVE;
                    InventoryClass bag = account.Game.character.Inventory;

                    // Réception selon dofus.aks.Items : fiches id~modèle~quantité~position~effets en hexadécimal.
                    Feed(account, "OAKO3e8~7d1~5~~7d#5#0#0#;");
                    InventoryObjects potion = bag.GetByInventoryId(1000);
                    Check(bag.Objets.Count() == 1 && potion != null && potion.ID == 2001 && potion.Name == "Potion fictive" && potion.Qua == 5
                        && !potion.IsEquipped() && potion.HasMetadata && potion.Type == 12, "OAKO record was not read in hexadecimal with BotObjets metadata");
                    Feed(account, "OAK*O3e9~7d2~1~1~;3ea~7d3~2~~*G12*Xbad");
                    InventoryObjects sword = bag.GetByInventoryId(1001), ring = bag.GetByInventoryId(1002);
                    Check(bag.Objets.Count() == 3 && sword != null && sword.position == InventorySlots.WEAPON && ring != null && ring.Qua == 2,
                        "OAK with several * records and G/unknown types was not read");
                    Feed(account, "OAKOzz~bad;3eb~9c4~1~~;");
                    InventoryObjects unknown = bag.GetByInventoryId(1003);
                    Check(bag.Objets.Count() == 4 && unknown != null && unknown.Name == "Objet n° 2500" && !unknown.HasMetadata, "Unreadable record blocked the valid one or unknown template has no fallback name");
                    Feed(account, "OAEF"); Check(bag.LastServerMessage.Contains("plein"), "OAEF was not reported");
                    Feed(account, "OAEL"); Check(bag.LastServerMessage.Contains("niveau"), "OAEL was not reported");
                    Feed(account, "OQ1002|7"); Check(ring.Qua == 7, "OQ did not update the quantity");
                    Feed(account, "OM1001|"); Check(!sword.IsEquipped(), "OM with empty slot did not unequip");
                    Feed(account, "OM1001|1"); Check(sword.position == InventorySlots.WEAPON, "OM with slot did not equip");
                    Feed(account, "OC|3e9~7d2~1~6~"); Check(bag.GetByInventoryId(1001).position == InventorySlots.HAT, "OC| (StarLoco) did not replace the record");
                    Feed(account, "OCO3e9~7d2~1~1~;3ea~7d3~3~~");
                    sword = bag.GetByInventoryId(1001); ring = bag.GetByInventoryId(1002);
                    Check(sword.position == InventorySlots.WEAPON && ring.Qua == 3 && bag.Objets.Count() == 4, "OCO with two records was not applied");
                    Feed(account, "OS+12|2002;2003|7d#5#0#0#");
                    Check(bag.ItemSets.ContainsKey(12) && bag.ItemSets[12].ItemIds.SequenceEqual(new[] { 2002, 2003 }) && bag.ItemSets[12].Bonus == "7d#5#0#0#", "OS+ was not stored");
                    Feed(account, "OS-12"); Check(!bag.ItemSets.ContainsKey(12), "OS- did not remove the set");
                    Feed(account, "OT14"); Check(account.Game.character.CurrentJobTool == 14, "OT<job> was not stored");
                    Feed(account, "OT"); Check(account.Game.character.CurrentJobTool == null, "OT without job did not clear the tool");
                    Feed(account, "OKU1000|42|5|2001"); Check(bag.LastServerMessage.Contains("confirmation"), "OK use condition was not logged");
                    Feed(account, "OKG1000|42|5|250"); Check(bag.LastServerMessage.Contains("250 kamas"), "OK gold condition was not logged");
                    NoPacket(peer, "OK condition triggered an automatic reply");
                    Feed(account, "OR1003"); Check(bag.GetByInventoryId(1003) == null && bag.Objets.Count() == 3, "OR did not remove the whole object");
                    Feed(account, "ORbad"); Check(bag.Objets.Count() == 3, "Unreadable OR removed an object");

                    // Envois selon Items.movement/use/drop ; l'état local attend la confirmation du serveur.
                    Check(Result(bag.Use_Item(potion)) && Read(peer) == "OU1000|" && potion.Qua == 5, "Use does not send OU<id>| or changed the quantity locally");
                    Check(Result(bag.Desequip_Item(sword)) && Read(peer) == "OM1001|-1|1" && sword.IsEquipped(), "Unequip does not send OM<id>|-1|1 or changed the slot locally");
                    Check(!Result(bag.Desequip_Item(ring)), "Unequip accepted for an item in the bag");
                    Feed(account, "OM1001|"); Check(!sword.IsEquipped(), "Server OM did not update the sword");
                    Check(Result(bag.Equip_item(sword)) && Read(peer) == "OM1001|1|1", "Equip does not send the numeric slot");
                    Feed(account, "OM1001|1"); Check(sword.IsEquipped(), "Server OM after the equip request did not equip the sword");
                    Check(Result(bag.Equip_item(ring)) && Read(peer) == "OM1002|2|1", "Ring is not equipped on the first free ring slot");
                    Check(!Result(bag.Equip_item(potion)), "Non-equipable item was equipped");
                    Check(!Result(bag.Drop_Item(potion, 9)) && !Result(bag.Drop_Item(potion, 0)), "Drop accepted an invalid quantity");
                    NoPacket(peer, "Refused inventory actions reached the server");
                    Check(Result(bag.Drop_Item(potion, 2)) && Read(peer) == "OD1000|2" && potion.Qua == 5, "Drop does not send OD<id>|<quantity> or changed the quantity locally");
                    account.AccountStates = AccountStates.FIGHTING;
                    Check(!Result(bag.Use_Item(potion)) && !Result(bag.Drop_Item(potion, 1)), "Inventory actions allowed during a fight");
                    account.AccountStates = AccountStates.CONNECTED_INACTIVE;
                    NoPacket(peer, "Inventory actions during a fight reached the server");

                    // Boutique PNJ selon dofus.aks.Exchange et StarLoco (ER0 → ECK0 + EL ; prix nul omis).
                    NpcShop shop = account.Game.Interactions.Shop;
                    Check(!Result(shop.BuyAsync(2001, 1)).Sent && !Result(shop.SellAsync(1000, 1)).Sent && !Result(shop.LeaveAsync()).Sent, "Shop actions accepted before ECK");
                    Check(Result(shop.OpenAsync(-8)).Sent && Read(peer) == "ER0|-8", "Opening the shop does not send ER0|<npc>");
                    Check(!shop.IsOpen, "Shop opened locally before ECK");
                    Feed(account, "ECK0|-8");
                    Check(shop.IsOpen && shop.NpcId == -8 && shop.NpcName == "Marchand fictif" && account.AccountStates == AccountStates.BUYING, "ECK0 did not open the shop");
                    Feed(account, "EL2001;7d#5#0#0#;50|2002;;|2004;;30|bad|");
                    Check(shop.Articles.Count == 3 && shop.Articles[0].Price == 50 && shop.Articles[0].Name == "Potion fictive" && shop.Articles[0].Stats == "7d#5#0#0#",
                        "EL article with price was not read");
                    Check(shop.Articles[1].Price == null && shop.Articles[1].Name == "Épée fictive" && shop.Articles[2].Name == "Objet n° 2004" && shop.Articles[2].Price == 30,
                        "EL without price or without BotObjets metadata was not read");
                    Check(!Result(shop.BuyAsync(9999, 1)).Sent && !Result(shop.BuyAsync(2001, 0)).Sent && !Result(shop.BuyAsync(2001, 100)).Sent,
                        "Buy accepted an unlisted article, an empty quantity or more than the character's kamas");
                    NoPacket(peer, "Refused purchases reached the server");
                    Check(Result(shop.BuyAsync(2001, 2)).Sent && Read(peer) == "EB2001|2" && shop.IsPending, "Buy does not send EB<template>|<quantity>");
                    Check(!Result(shop.BuyAsync(2002, 1)).Sent, "Second purchase sent while the first awaits the server");
                    Feed(account, "EBK"); Check(!shop.IsPending && shop.LastMessage.Contains("accepté"), "EBK did not release the pending purchase");
                    Check(Result(shop.BuyAsync(2002, 1)).Sent && Read(peer) == "EB2002|1", "Article without transmitted price cannot be bought");
                    Feed(account, "EBE"); Check(!shop.IsPending && shop.LastMessage.Contains("refusé"), "EBE was not reported");
                    Check(!Result(shop.SellAsync(1002, 50)).Sent && !Result(shop.SellAsync(9999, 1)).Sent, "Sell accepted an unknown item or too many units");
                    Check(Result(shop.SellAsync(1002, 3)).Sent && Read(peer) == "ES1002|3", "Sell does not send ES<item>|<quantity>");
                    Feed(account, "ESK"); Feed(account, "OQ1002|4"); Check(!shop.IsPending && ring.Qua == 4, "ESK/OQ after a sale were not applied");
                    Check(Result(shop.SellAsync(1002, 4)).Sent && Read(peer) == "ES1002|4", "Selling the whole stack was refused");
                    Feed(account, "ESE"); Check(!shop.IsPending && shop.LastMessage.Contains("refusé"), "ESE was not reported");
                    Check(Result(shop.LeaveAsync()).Sent && Read(peer) == "EV", "Leaving does not send EV");
                    Check(shop.IsOpen, "Shop closed locally before the server EV");
                    Feed(account, "EVa"); Check(!shop.IsOpen && shop.Articles.Count == 0 && account.AccountStates == AccountStates.CONNECTED_INACTIVE, "EVa did not close the shop");
                    Feed(account, "EL2001;;5|"); Check(shop.Articles.Count == 0, "EL outside a shop filled the article list");
                    Feed(account, "ECK1|43"); Check(!shop.IsOpen && account.AccountStates == AccountStates.STORAGE, "Non-NPC exchange lost the storage state");
                    Feed(account, "EV"); Check(account.AccountStates == AccountStates.CONNECTED_INACTIVE, "EV after a storage exchange kept the state");

                    // Volets Inventaire et Boutique de la fenêtre de jeu.
                    using (var form = new GameClientFullform(account))
                    {
                        form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); Application.DoEvents();
                        var drawer = (Control)Get(form, "drawer"); var panels = (TabControl)Get(form, "panels");
                        form.ShowPanel(1); Application.DoEvents();
                        var inventory = (ListView)Get(form, "inventory");
                        Check(inventory.Items.Count == 3 && RowWithTag(inventory, 1000u) != null, "Inventory rows lack their inventory identifiers");
                        var equip = (Control)Get(form, "equipItem"); var unequip = (Control)Get(form, "unequipItem");
                        var use = (Control)Get(form, "useItem"); var drop = (Control)Get(form, "dropItem");
                        Check(!equip.Enabled && !unequip.Enabled && !use.Enabled && !drop.Enabled, "Inventory actions enabled without a selection");
                        RowWithTag(inventory, 1000u).Selected = true; Application.DoEvents();
                        Check(!equip.Enabled && !unequip.Enabled && use.Enabled && drop.Enabled, "Potion actions are wrong (equip/unequip must stay disabled)");
                        ((Button)use).PerformClick(); Check(Read(peer) == "OU1000|", "Use button does not send OU");
                        ((NumericUpDown)Get(form, "inventoryQuantity")).Value = 2;
                        ((Button)drop).PerformClick(); Application.DoEvents();
                        NoPacket(peer, "Drop sent without confirmation"); Check(drop.Text == "Confirmer", "Drop does not ask for confirmation");
                        ((Button)drop).PerformClick(); Check(Read(peer) == "OD1000|2" && drop.Text == "Jeter", "Confirmed drop does not send OD<id>|<quantity>");
                        RowWithTag(inventory, 1000u).Selected = false; RowWithTag(inventory, 1001u).Selected = true; Application.DoEvents();
                        Check(unequip.Enabled && !equip.Enabled && !drop.Enabled, "Equipped sword actions are wrong");
                        ((Button)unequip).PerformClick(); Check(Read(peer) == "OM1001|-1|1", "Unequip button does not send OM");
                        Feed(account, "OM1001|"); Application.DoEvents();
                        Check(RowWithTag(inventory, 1001u).Selected && equip.Enabled && !unequip.Enabled, "Server OM lost the selection or the buttons");
                        ((Button)equip).PerformClick(); Check(Read(peer) == "OM1001|1|1", "Equip button does not send OM with the slot");
                        Feed(account, "OM1001|1"); Application.DoEvents();
                        var view = (MapControl)Get(form, "mapControl"); Check(view != null, "Map view missing for a loaded map");
                        Complete(view.Router.RouteAsync(9, MouseButtons.Right)); NoPacket(peer, "Right-click on an empty cell sent a packet");
                        Check(view.Router.LastMenu == null, "Right-click on an empty cell opened an actor menu");
                        Complete(view.Router.RouteAsync(5, MouseButtons.Right)); NoPacket(peer, "Right-click on an NPC sent a packet before a menu choice");
                        var trade = view.Router.LastMenu?.Items.OfType<ToolStripMenuItem>().FirstOrDefault(item => item.Text == "Acheter/Vendre");
                        Check(trade != null && trade.Enabled, "NPC context menu lacks Acheter/Vendre");
                        trade.PerformClick(); Check(Read(peer) == "ER0|-8", "Acheter/Vendre does not send ER0|<npc>");
                        Check(!shop.IsOpen && panels.SelectedIndex != GameClientFullform.ShopPanel, "Shop panel opened before the server ECK");
                        Feed(account, "ECK0|-8"); Feed(account, "EL2001;;50|2004;;|"); Application.DoEvents();
                        Check(drawer.Visible && panels.SelectedIndex == GameClientFullform.ShopPanel, "Shop panel did not open on ECK0/EL");
                        var shopList = (ListView)Get(form, "shopList");
                        Check(shopList.Items.Count == 2 && shopList.Items[0].Text == "Potion fictive" && shopList.Items[0].SubItems[1].Text == "50 kamas"
                            && shopList.Items[1].Text == "Objet n° 2004" && shopList.Items[1].SubItems[1].Text == "prix non transmis", "Shop list differs from EL");
                        var buy = (Control)Get(form, "shopBuy"); Check(!buy.Enabled, "Buy enabled without a selection");
                        shopList.Items[0].Selected = true; Application.DoEvents(); Check(buy.Enabled, "Buy disabled for a selected article");
                        ((NumericUpDown)Get(form, "shopQuantity")).Value = 3;
                        ((Button)buy).PerformClick(); Check(Read(peer) == "EB2001|3", "Buy button does not send EB<template>|<quantity>");
                        Application.DoEvents(); Check(!buy.Enabled, "Buy stays enabled while the server answers");
                        Feed(account, "EBK"); Application.DoEvents(); Check(buy.Enabled, "EBK did not re-enable purchases");
                        var sales = (ListView)Get(form, "shopInventory");
                        Check(sales.Items.Count == 2 && RowWithTag(sales, 1001u) == null && RowWithTag(sales, 1002u) != null, "Sale list must list only the bag (unequipped) items");
                        var sell = (Control)Get(form, "shopSell"); Check(!sell.Enabled, "Sell enabled without a selection");
                        RowWithTag(sales, 1002u).Selected = true; Application.DoEvents(); Check(sell.Enabled, "Sell disabled for a bag item");
                        ((NumericUpDown)Get(form, "sellQuantity")).Value = 2;
                        ((Button)sell).PerformClick(); Check(Read(peer) == "ES1002|2", "Sell button does not send ES<item>|<quantity>");
                        Feed(account, "ESK"); Feed(account, "OQ1002|2"); Application.DoEvents();
                        Check(RowWithTag(sales, 1002u).SubItems[1].Text == "2", "OQ after the sale is not shown");
                        ((Button)Get(form, "shopLeave")).PerformClick(); Check(Read(peer) == "EV", "Close button does not send EV");
                        Feed(account, "EV"); Application.DoEvents();
                        Check(!drawer.Visible && shopList.Items.Count == 0 && !buy.Enabled, "Shop panel stayed open after EV");
                        form.Close();
                    }
                }
            }
        }
        finally { listener.Stop(); Environment.CurrentDirectory = previous; }
    }
}
