using System;
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
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.NPC;
using Tool_BotProtocol.Game.Perso.Inventory;
using Tool_BotProtocol.Game.Perso.Inventory.Enums;
using Tool_BotProtocol.Game.Session;

// Inventaire en grille (lot F13b) sur un serveur fictif local, avec des textes du client synthétiques (objets, types,
// emplacements, effets, panoplies, textes) écrits dans un dossier temporaire : OAK à six fiches lues sans BotObjets,
// catégories des filtres, plateau d'équipement, fiche d'objet (effets, conditions, caractéristiques d'arme, poids, prix,
// panoplie portée OS+), équiper/déséquiper OM<id>|<position> sans quantité, glisser-déposer vers un emplacement ou le sac,
// croix « deux mains », monture refusée localement, Entrée → OU, destruction Od<id>|<quantité> après la question
// DO_U_DESTROY (Non n'envoie rien), OdE / OAE / Im119|44 affichés sous l'objet, icône synthétique au pixel témoin et
// repli en initiales sans PNG, infobulle de la boutique. Aucune capture de fenêtre : passe sous Mono avec xvfb-run.
internal static class BotInventoryGridSmoke
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            string path = Path.Combine(TestPaths.ApplicationBin, new AssemblyName(e.Name).Name + ".dll");
            if (!File.Exists(path)) path = Path.ChangeExtension(path, "exe");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        var uiErrors = new List<Exception>();
        Application.ThreadException += (s, e) => uiErrors.Add(e.Exception);
        try
        {
            Application.EnableVisualStyles(); Run();
            Check(uiErrors.Count == 0, "Interface errors: " + string.Join(" / ", uiErrors.Select(e => e.GetType().Name + " " + e.Message)));
            Console.WriteLine("OK: grille du sac et plateau, filtres, fiche d'objet (effets, conditions, arme, panoplie OS+), OM sans quantité, glisser-déposer, Od après DO_U_DESTROY, OdE/OAE/Im, icône et repli, infobulle boutique");
        }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static object Get(object target, string field) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static void PumpUntil(Func<bool> done, string what, int seconds = 6)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(seconds);
        while (!done()) { if (DateTime.UtcNow > end) throw new TimeoutException("Inventory loopback timed out: " + what); Application.DoEvents(); Thread.Sleep(5); }
        Application.DoEvents();
    }
    private static void Complete(Task task) { PumpUntil(() => task.IsCompleted, "task"); task.GetAwaiter().GetResult(); }
    private static T Result<T>(Task<T> task) { Complete(task); return task.Result; }
    private static void Feed(Accounts account, string packet) { Complete(MessagesReception.ReceptionAsync(account.Connexion, packet)); }
    private static string Read(Socket socket)
    {
        PumpUntil(() => socket.Available > 0, "packet");
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
    private static IEnumerable<Control> All(Control value) { yield return value; foreach (Control child in value.Controls) foreach (var nested in All(child)) yield return nested; }
    private static void Click(object button) => ((Button)button).PerformClick();
    private static void Answer(Form dialog, string choice) => All(dialog).OfType<Button>().First(button => button.Text == choice).PerformClick();
    private static bool Near(Color a, Color b) => Math.Abs(a.R - b.R) <= 16 && Math.Abs(a.G - b.G) <= 16 && Math.Abs(a.B - b.B) <= 16;
    private static void Png(string path, Color color, int size)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using (var image = new Bitmap(size, size)) { using (var graphics = Graphics.FromImage(image)) graphics.Clear(color); image.Save(path, ImageFormat.Png); }
    }
    private static void Write(string folder, string file, string family, string body)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, file), "<?xml version='1.0' encoding='utf-8'?>\n<BotLang famille=\"" + family + "\" langue=\"fr\" version=\"1\" source=\"" + family + "_fr_1.swf\">\n" + body + "\n</BotLang>\n", Encoding.UTF8);
    }

    // ClientAssets, ItemAssets et ItemCellPainter sont internes à l'application : accès par réflexion.
    private static Assembly App => typeof(LoginForm).Assembly;
    private static void AssetsRoot(string value) => App.GetType("Outil_Azur_complet.Bot.ClientAssets").GetProperty("Root", Any).SetValue(null, value, null);
    private static void ResetItemAssets() => App.GetType("Outil_Azur_complet.Bot.Controls.ItemAssets").GetMethod("Reset", Any).Invoke(null, null);
    private static string Initials(string name) => (string)App.GetType("Outil_Azur_complet.Bot.Controls.ItemCellPainter").GetMethod("Initials", Any).Invoke(null, new object[] { name });
    private static ShortcutPayload Payload(uint inventoryId, int quantity, int fromPosition) => new ShortcutPayload(ShortcutSlotKind.Item, inventoryId, quantity, fromPosition);

    private static void Run()
    {
        string previous = Environment.CurrentDirectory;
        string folder = Path.Combine(TestPaths.Work, "bot-inventory-grid"); Directory.CreateDirectory(folder); Environment.CurrentDirectory = folder;
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        Func<int, string, string[], string> previousResolver = ServerMessages.Resolver;
        try
        {
            MessagesReception.Init();
            Map.AllBotMaps[900094] = new Map { MapID = 900094, MapWidth = 3, MapHeight = 4, MapData = string.Concat(Enumerable.Repeat("HhGaeaaaaa", 18)) };
            PNJ.AllPNJ[100] = new PNJ(100, 100, null) { Name = "Marchand fictif" };

            // Textes du client synthétiques : six objets, leurs types et super-types, emplacements, effets, panoplie, textes.
            string lang = Path.Combine(folder, "lang");
            Write(lang, "items.xml", "items",
                "<objet id=\"2001\" nom=\"Potion fictive\" type=\"12\" gfx=\"5\" niveau=\"1\" pods=\"1\" prix=\"10\" description=\"Soigne un peu (test).\" utilisable=\"true\" />\n"
                + "<objet id=\"2002\" nom=\"Épée fictive\" type=\"6\" gfx=\"7\" niveau=\"10\" pods=\"20\" prix=\"250\" conditions=\"CS&gt;4\" panoplie=\"12\" arme=\"5,4,1,1,50,30,false,false\" />\n"
                + "<objet id=\"2003\" nom=\"Anneau fictif\" type=\"9\" gfx=\"3\" niveau=\"1\" pods=\"1\" panoplie=\"12\" />\n"
                + "<objet id=\"2004\" nom=\"Arc fictif\" type=\"2\" gfx=\"1\" niveau=\"5\" pods=\"10\" deuxMains=\"true\" />\n"
                + "<objet id=\"2005\" nom=\"Bois fictif\" type=\"35\" gfx=\"9\" niveau=\"1\" pods=\"2\" />\n"
                + "<objet id=\"2006\" nom=\"Clé fictive\" type=\"18\" gfx=\"4\" niveau=\"1\" pods=\"0\" />\n"
                + "<type id=\"12\" nom=\"Potion\" superType=\"6\" />\n<type id=\"6\" nom=\"Épée\" superType=\"2\" />\n<type id=\"9\" nom=\"Anneau\" superType=\"3\" />\n"
                + "<type id=\"2\" nom=\"Arc\" superType=\"2\" />\n<type id=\"35\" nom=\"Bois\" superType=\"9\" />\n<type id=\"18\" nom=\"Objet de quête\" superType=\"14\" />\n"
                + "<emplacements id=\"2\" emplacements=\"1\" />\n<emplacements id=\"3\" emplacements=\"2,4\" />");
            Write(lang, "effects.xml", "effects",
                "<entree table=\"E\" id=\"125\" c=\"11\" d=\"+#1{~1~2 à }#2 en vitalité\" j=\"true\" o=\"+\" />\n"
                + "<entree table=\"E\" id=\"111\" c=\"1\" d=\"+#1{~1~2 à }#2 PA\" j=\"true\" o=\"+\" />\n"
                + "<entree table=\"E\" id=\"100\" c=\"-1\" d=\"NOTHING\" o=\"\" />");
            Write(lang, "itemsets.xml", "itemsets", "<entree table=\"IS\" id=\"12\" i=\"2002,2003\" n=\"Panoplie fictive\" />");
            Write(lang, "lang.xml", "lang",
                "<texte cle=\"DO_U_DESTROY\" valeur=\"Voulez-vous vraiment détruire %1 %2 ?\" />\n<texte cle=\"QUESTION\" valeur=\"Question\" />\n"
                + "<texte cle=\"EFFECTS\" valeur=\"Effets\" />\n<texte cle=\"NO_EFFECTS\" valeur=\"Aucun effet\" />\n<texte cle=\"CONDITIONS\" valeur=\"Conditions\" />\n"
                + "<texte cle=\"NO_CONDITIONS\" valeur=\"Aucune\" />\n<texte cle=\"CHARACTERISTICS\" valeur=\"Caractéristiques\" />\n<texte cle=\"ITEM_OR\" valeur=\"ou\" />\n"
                + "<texte cle=\"ITEM_CHARACTERISTICS\" valeur=\"Force,Force de base,Vitalité\" />\n<texte cle=\"ITEM_TYPE\" valeur=\"Catégorie\" />\n"
                + "<texte cle=\"ITEM_AP\" valeur=\"PA : %1\" />\n<texte cle=\"ITEM_RANGE\" valeur=\"Portée : %1\" />\n<texte cle=\"TO_RANGE\" valeur=\"à \" />\n"
                + "<texte cle=\"ITEM_CRITICAL_BONUS\" valeur=\"Bonus coups critiques : %1\" />\n<texte cle=\"ITEM_CRITICAL\" valeur=\"Critique : 1/%1\" />\n<texte cle=\"ITEM_MISS\" valeur=\"Échec : 1/%1\" />\n"
                + "<texte cle=\"PLAYER_WEIGHT\" valeur=\"%1 pods sur %2\" />\n<texte cle=\"KAMAS\" valeur=\"Kamas\" />\n<texte cle=\"LEVEL_SMALL\" valeur=\"Niv.\" />\n<texte cle=\"PODS\" valeur=\"pod{~ps}\" />\n"
                + "<texte cle=\"ITEMSET_EQUIPED_ITEMS\" valeur=\"Objets équipés\" />\n<texte cle=\"ITEMSET_EFFECTS\" valeur=\"Effets actuels\" />\n<texte cle=\"TWO_HANDS_WEAPON\" valeur=\"Arme à deux mains\" />\n"
                + "<texte cle=\"ALL\" valeur=\"Tout\" />\n<texte cle=\"EQUIPEMENT\" valeur=\"Équipement\" />\n<texte cle=\"NONEQUIPEMENT\" valeur=\"Divers\" />\n<texte cle=\"RESSOURECES\" valeur=\"Ressources\" />\n"
                + "<texte cle=\"ALREADY_EQUIPED\" valeur=\"Objet déjà équipé (test).\" />\n<texte cle=\"ERROR_19\" valeur=\"Conditions non remplies (test)\" />\n<texte cle=\"ERROR_44\" valeur=\"pour équiper cet objet (test).\" />");
            LangData.Clear();
            Check(LangData.Load(lang) == 4 && LangData.IsLoaded("items") && LangData.IsLoaded("effects") && LangData.IsLoaded("itemsets") && LangData.IsLoaded("lang"), "Synthetic lang XML not loaded");
            ServerMessages.Resolver = (type, id, args) => { string text = LangData.Text.Im(type, id, args); return string.IsNullOrEmpty(text) || (text.StartsWith("!") && text.EndsWith("!")) ? null : text; };

            // Images du client : dossier temporaire sans aucune icône d'objet pour commencer.
            string images = Path.Combine(folder, "images"); Directory.CreateDirectory(images);
            AssetsRoot(images); ResetItemAssets();

            using (var account = new Accounts(new AccountConfig("synthetic-inventory", "synthetic", "loopback")))
            {
                Task<Socket> accept = listener.AcceptSocketAsync();
                Complete(account.Connexion.ConnectToServer(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port)); Complete(accept);
                using (Socket peer = accept.Result)
                {
                    peer.ReceiveTimeout = 6000;
                    account.Game.character.SetPerso_Data(42, "Personnage de test", 25, 0, 8);
                    account.Game.character.Kamas = 1234;
                    Feed(account, "GDM|900094|date|key"); Check(Read(peer) == "GI", "GDM did not request GI");
                    Feed(account, "GM|+0;1;0;42;Personnage de test;1;100^100;0|+5;1;0;-8;100;-4;1^100;0");
                    account.AccountStates = AccountStates.CONNECTED_INACTIVE;
                    InventoryClass bag = account.Game.character.Inventory;

                    // Six fiches hexadécimales (Items.onAdd) lues avec les textes du client, sans BotObjets.
                    Feed(account, "OAKO3e8~7d1~5~~7d#5#0#0#;3e9~7d2~1~~6f#2#0#0#;3ea~7d3~1~~;3eb~7d4~1~~;3ec~7d5~a~~;3ed~7d6~1~~");
                    InventoryObjects potion = bag.GetByInventoryId(1000), sword = bag.GetByInventoryId(1001), ring = bag.GetByInventoryId(1002);
                    InventoryObjects bow = bag.GetByInventoryId(1003), wood = bag.GetByInventoryId(1004), key = bag.GetByInventoryId(1005);
                    Check(bag.Objets.Count() == 6 && potion.Name == "Potion fictive" && potion.Type == 12 && potion.Level == 1 && potion.pods == 1 && potion.HasMetadata
                        && sword.Conditions == "CS>4" && sword.Level == 10 && wood.Qua == 10, "OAK records were not completed from the client texts");
                    Check(ItemSlots.CategoryOf(potion.Type) == ItemCategory.Miscellaneous && ItemSlots.CategoryOf(sword.Type) == ItemCategory.Equipment
                        && ItemSlots.CategoryOf(ring.Type) == ItemCategory.Equipment && ItemSlots.CategoryOf(wood.Type) == ItemCategory.Resources
                        && ItemSlots.CategoryOf(key.Type) == ItemCategory.Quest, "Categories do not follow the super-types");
                    Check(ItemSlots.PositionsFor(sword.Type).SequenceEqual(new[] { 1 }) && ItemSlots.PositionsFor(ring.Type).SequenceEqual(new[] { 2, 4 })
                        && ItemSlots.PositionsFor(potion.Type).Count == 0 && ItemSlots.PositionsFor(key.Type).Count == 0, "Equipment slots do not follow I.ss");

                    // Fiche d'objet sans interface : effets substitués, conditions, caractéristiques d'arme, poids, prix, panoplie.
                    ItemSheet swordSheet = ItemSheet.From(sword);
                    Check(swordSheet.Known && swordSheet.Name == "Épée fictive" && swordSheet.LevelText == "Niv. 10" && swordSheet.TypeName == "Épée"
                        && swordSheet.Effects.SequenceEqual(new[] { "+2 PA" }) && swordSheet.ConditionLines.SequenceEqual(new[] { "Force > 4" })
                        && swordSheet.Characteristics.Count == 4 && swordSheet.Characteristics[0] == "PA : 4" && swordSheet.Characteristics[1] == "Portée : 1"
                        && swordSheet.Characteristics[3] == "Critique : 1/50 - Échec : 1/30" && swordSheet.WeightText == "20 pods" && swordSheet.Price == 250
                        && swordSheet.SetId == 12 && swordSheet.SetName == "Panoplie fictive" && !swordSheet.TwoHanded && swordSheet.CanDestroy,
                        "Sword sheet differs from the client texts: " + string.Join(" / ", swordSheet.ToLines()));
                    ItemSheet potionSheet = ItemSheet.From(potion);
                    Check(potionSheet.Effects.SequenceEqual(new[] { "+5 en vitalité" }) && potionSheet.Usable && potionSheet.ConditionLines.SequenceEqual(new[] { "Aucune" })
                        && potionSheet.WeightText == "1 pod" && potionSheet.Description == "Soigne un peu (test).", "Potion sheet is wrong: " + string.Join(" / ", potionSheet.ToLines()));
                    ItemSheet keySheet = ItemSheet.From(key), bowSheet = ItemSheet.From(bow);
                    Check(!keySheet.CanDestroy && !keySheet.CanDrop && bowSheet.TwoHanded && bowSheet.Effects.Count == 0, "Quest item must not be destroyable; bow must be two-handed");
                    Check(ItemEffects.DescribeAll("64#1#0#0#,zz#1,7d#3#7#0#").SequenceEqual(new[] { "+3 à 7 en vitalité" }), "NOTHING effects and unreadable fields must be skipped");
                    Check(ItemEffects.DescribeAll("1f4#1#0#0#").SequenceEqual(new[] { "Effet 500 : 1" }), "Unknown effect has no readable fallback");

                    // Envois du modèle : Od après contrôles locaux, OM sans quantité, quantité seulement quand elle est donnée.
                    Check(!Result(bag.Destroy(potion, 0)) && !Result(bag.Destroy(potion, 99)) && !Result(bag.Destroy(null, 1)), "Destroy accepted an invalid quantity or a null item");
                    Check(!Result(bag.MoveToSlot(ring, 16)) && !Result(bag.MoveToSlot(potion, 1)) && !Result(bag.MoveToSlot(sword, 20)), "MoveToSlot accepted a slot the type does not allow");
                    NoPacket(peer, "Refused inventory actions reached the server");
                    Check(Result(bag.MoveToSlot(potion, 36, 3)) && Read(peer) == "OM1000|36|3", "MoveToSlot with a quantity does not send OM<id>|<position>|<quantity>");
                    Check(Result(bag.Destroy(wood, 4)) && Read(peer) == "Od1004|4" && wood.Qua == 10, "Destroy does not send Od<id>|<quantity> or changed the quantity locally");
                    Feed(account, "OQ1004|6"); Check(wood.Qua == 6, "OQ after Od was not applied");
                    account.AccountStates = AccountStates.FIGHTING;
                    Check(!Result(bag.Destroy(wood, 1)) && !Result(bag.MoveToSlot(ring, 2)), "Inventory actions allowed during a fight");
                    account.AccountStates = AccountStates.CONNECTED_INACTIVE;
                    NoPacket(peer, "Inventory actions during a fight reached the server");

                    // Volet Inventaire de la fenêtre de jeu.
                    using (var form = new GameClientFullform(account))
                    {
                        form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); Application.DoEvents();
                        var drawer = form.Panels; var panel = drawer.Get<InventoryPanel>();
                        drawer.Show(panel); PumpUntil(() => drawer.Visible && drawer.Current == panel, "inventory panel");
                        var grid = (ItemGrid)Get(panel, "inventory"); var plateau = (EquipmentPlateau)Get(panel, "plateau"); var sheet = (ItemTooltip)Get(panel, "sheet");
                        var help = (Label)Get(panel, "inventoryHelp"); var kamas = (Label)Get(panel, "kamasLabel"); var pods = (Label)Get(panel, "podsLabel");
                        var quantity = (NumericUpDown)Get(panel, "inventoryQuantity");
                        var equip = (Control)Get(panel, "equipItem"); var unequip = (Control)Get(panel, "unequipItem"); var use = (Control)Get(panel, "useItem");
                        var drop = (Control)Get(panel, "dropItem"); var destroy = (Control)Get(panel, "destroyItem");
                        Check(grid.Items.Count == 6 && plateau.Slots.Count == 17 && plateau.Slots.All(slot => slot.Item == null), "Grid must show the six bag items and the plateau be empty");
                        Check(kamas.Text == "1 234 kamas" && pods.Text.Contains("Ow"), "Header kamas or pods are wrong: " + kamas.Text + " / " + pods.Text);
                        Feed(account, "Ow12|1000"); Application.DoEvents();
                        Check(pods.Text == "12 pods sur 1 000", "PLAYER_WEIGHT is not shown after Ow: " + pods.Text);
                        Check(!equip.Enabled && !unequip.Enabled && !use.Enabled && !drop.Enabled && !destroy.Enabled && sheet.Sheet == null, "Actions enabled without a selection");

                        // Filtres du client : équipement (épée, anneau, arc), divers (potion), ressources (bois), quête (clé).
                        panel.Filter = ItemCategory.Equipment; Application.DoEvents();
                        Check(grid.Items.Select(item => item.Name).SequenceEqual(new[] { "Anneau fictif", "Arc fictif", "Épée fictive" }), "Equipment filter is wrong: " + string.Join(", ", grid.Items.Select(item => item.Name)));
                        panel.Filter = ItemCategory.Miscellaneous; Check(grid.Items.Count == 1 && grid.Items[0].Inventory_ID == 1000u, "Miscellaneous filter is wrong");
                        panel.Filter = ItemCategory.Resources; Check(grid.Items.Count == 1 && grid.Items[0].Inventory_ID == 1004u, "Resources filter is wrong");
                        panel.Filter = ItemCategory.Quest; Check(grid.Items.Count == 1 && grid.Items[0].Inventory_ID == 1005u, "Quest filter is wrong");
                        panel.Filter = null; Check(grid.Items.Count == 6, "Clearing the filter did not restore the bag");

                        // Sélection de l'épée : fiche sous la grille, boutons, puis Équiper → OM<id>|1 sans quantité, état local inchangé avant OM.
                        Check(grid.SelectItem(1001u), "Sword cell cannot be selected"); Application.DoEvents();
                        Check(panel.CurrentItem == sword && sheet.Sheet != null && sheet.Sheet.TemplateId == 2002 && sheet.Set == null, "Selecting a cell did not show its sheet");
                        IReadOnlyList<string> lines = sheet.Lines;
                        Check(lines[0] == "Épée fictive (Niv. 10)" && lines[1].Contains("Épée") && lines[1].Contains("Panoplie fictive") && lines.Contains("Effets") && lines.Contains("+2 PA")
                            && lines.Contains("Conditions") && lines.Contains("Force > 4") && lines.Contains("Caractéristiques") && lines.Contains("PA : 4") && lines.Contains("20 pods · 250 kamas"),
                            "Sheet lines are wrong: " + string.Join(" / ", lines));
                        Check(sheet.PreferredHeightFor(300) > 40 && sheet.Height == sheet.PreferredHeightFor(sheet.Width), "Sheet height does not follow its content");
                        Check(equip.Enabled && !unequip.Enabled && drop.Enabled && destroy.Enabled && help.Text.Contains("Épée fictive") && help.Text.Contains("dans le sac"), "Bag sword actions are wrong: " + help.Text);
                        Click(equip); Check(Read(peer) == "OM1001|1", "Equip does not send OM<id>|<slot> without quantity");
                        Check(grid.Items.Count == 6 && plateau[1].Item == null, "Equip changed the local state before the server OM");
                        Feed(account, "OM1001|1"); PumpUntil(() => plateau[1].Item == sword, "server OM");
                        Check(grid.Items.Count == 5 && grid.IndexOf(1001u) < 0 && plateau[1].Selected && panel.CurrentItem == sword && unequip.Enabled && !equip.Enabled && !drop.Enabled && !destroy.Enabled
                            && help.Text.Contains("Arme"), "Server OM did not move the sword to slot 1 or lost the selection: " + help.Text);

                        // Panoplie portée annoncée par OS+ : nom, objets portés ou non, bonus actuels sous la fiche.
                        Feed(account, "OS+12|2002|6f#1#0#0#"); Application.DoEvents();
                        Check(sheet.Set != null && sheet.Set.Name == "Panoplie fictive" && sheet.Set.EquippedCount == 1 && sheet.Set.Items.Count == 2 && sheet.Set.Effects.SequenceEqual(new[] { "+1 PA" })
                            && sheet.Lines.Any(line => line.Contains("Objets équipés : 1 / 2")) && sheet.Lines.Contains("Effets actuels") && sheet.Lines.Contains("○ Anneau fictif"),
                            "OS+ set is not shown under the sheet: " + string.Join(" / ", sheet.Lines));
                        Feed(account, "OS-12"); Application.DoEvents(); Check(sheet.Set == null, "OS- did not remove the set from the sheet");

                        // Glisser-déposer : acceptation par type, dépôt sur un emplacement → OM<id>|<position>, sur le sac → OM<id>|-1.
                        Check(plateau[2].Accepts(ring) && plateau[4].Accepts(ring) && !plateau[1].Accepts(ring) && !plateau[2].Accepts(potion) && plateau[1].Accepts(bow) && !plateau[15].Accepts(bow),
                            "Slot acceptance does not follow the equipment slots of the type");
                        plateau[2].AcceptDrop(Payload(1002u, 1, -1)); Check(Read(peer) == "OM1002|2", "Dropping the ring on slot 2 does not send OM<id>|2");
                        Feed(account, "OM1002|2"); PumpUntil(() => plateau[2].Item == ring, "ring OM");
                        plateau[16].AcceptDrop(Payload(1000u, 5, -1)); NoPacket(peer, "Dropping a potion on the mount slot sent a packet");
                        grid.AcceptDrop(Payload(1000u, 5, -1)); NoPacket(peer, "Dropping a bag item on the grid sent a packet");
                        grid.AcceptDrop(Payload(1002u, 1, 2)); Check(Read(peer) == "OM1002|-1", "Dropping a worn item on the grid does not send OM<id>|-1");
                        Feed(account, "OM1002|"); PumpUntil(() => plateau[2].Item == null && grid.IndexOf(1002u) >= 0, "ring back to bag");

                        // Croix « deux mains » sur le bouclier quand l'arc est porté.
                        Feed(account, "OM1001|"); Feed(account, "OM1003|1"); PumpUntil(() => plateau[1].Item == bow, "bow OM");
                        Check(plateau[15].Crossed, "Two-handed bow does not cross the shield slot");
                        Feed(account, "OM1003|"); Feed(account, "OM1001|1"); PumpUntil(() => plateau[1].Item == sword, "sword back");
                        Check(!plateau[15].Crossed, "Shield cross stayed after the one-handed sword came back");

                        // Entrée sur la potion (non équipable) → OU<id>| ; Déséquiper depuis le plateau → OM<id>|-1.
                        Check(grid.SelectItem(1000u), "Potion cell cannot be selected"); Application.DoEvents();
                        Check(panel.CurrentItem == potion && use.Enabled && !equip.Enabled && use.Text == "Utiliser", "Potion actions are wrong");
                        typeof(ItemGrid).GetMethod("OnKeyDown", Any).Invoke(grid, new object[] { new KeyEventArgs(Keys.Enter) });
                        Check(Read(peer) == "OU1000|", "Enter on the potion does not send OU<id>|");
                        plateau[1].Activate(); Application.DoEvents();
                        Check(panel.CurrentItem == sword && grid.SelectedItem == null && unequip.Enabled, "Activating a slot did not select the worn item");
                        Click(unequip); Check(Read(peer) == "OM1001|-1", "Unequip does not send OM<id>|-1");
                        Feed(account, "OM1001|"); PumpUntil(() => grid.IndexOf(1001u) >= 0, "sword unequipped");
                        Check(grid.SelectedItem == sword && equip.Enabled, "Selection did not follow the sword back to the grid");

                        // Détruire : question DO_U_DESTROY, Non n'envoie rien, Oui envoie Od<id>|<quantité> ; OQ puis OdE.
                        Check(grid.SelectItem(1000u), "Potion cell cannot be selected again"); Application.DoEvents();
                        quantity.Value = 2; Click(destroy);
                        PumpUntil(() => BotDialogs.OpenDialogs.Count == 1, "destroy dialog");
                        Form box = BotDialogs.OpenDialogs[0];
                        Check(box.Text == "Question" && All(box).Any(control => control.Name == "dialog-message" && control.Text == "Voulez-vous vraiment détruire 2 Potion fictive ?"),
                            "DO_U_DESTROY question is wrong: " + string.Join(" / ", All(box).Select(control => control.Text)));
                        NoPacket(peer, "Od was sent before the answer");
                        Answer(box, "Non"); PumpUntil(() => BotDialogs.OpenDialogs.Count == 0, "dialog closed");
                        NoPacket(peer, "Od was sent after Non"); Check(potion.Qua == 5, "Non changed the quantity");
                        Click(destroy); PumpUntil(() => BotDialogs.OpenDialogs.Count == 1, "second destroy dialog");
                        Answer(BotDialogs.OpenDialogs[0], "Oui"); Check(Read(peer) == "Od1000|2", "Oui does not send Od<id>|<quantity>");
                        Check(potion.Qua == 5 && grid.IndexOf(1000u) >= 0, "Destroy changed the local quantity before the server answer");
                        Feed(account, "OQ1000|3"); PumpUntil(() => potion.Qua == 3, "OQ after Od");
                        // Refus du serveur gardés sous l'objet malgré les rafraîchissements de la fenêtre, effacés au changement de sélection.
                        Feed(account, "OdE"); PumpUntil(() => help.Text.Contains("détruire"), "OdE message");
                        Check(help.Text.StartsWith("Potion fictive · 3 · dans le sac"), "Server notice replaced the item line: " + help.Text);
                        Feed(account, "OAEA"); PumpUntil(() => help.Text.EndsWith("Objet déjà équipé (test)."), "OAEA message");
                        Feed(account, "Im119|44"); PumpUntil(() => help.Text.EndsWith("pour équiper cet objet (test)."), "Im119|44 message");
                        panel.RefreshView(); Check(help.Text.EndsWith("pour équiper cet objet (test)."), "RefreshView dropped the server notice");
                        Check(grid.SelectItem(1005u), "Key cell cannot be selected"); Application.DoEvents();
                        Check(!help.Text.Contains("(test)") && help.Text.StartsWith("Clé fictive"), "Changing the selection did not clear the server notice: " + help.Text);
                        Check(!drop.Enabled && !destroy.Enabled && equip.Enabled == false, "Quest item can be dropped or destroyed");

                        // Icônes : sans PNG, initiales ; avec un PNG synthétique Items/<type>/<gfx>.png lu hors du fil de l'interface, pixel témoin.
                        Check(Initials("Potion fictive") == "PF" && Initials("Bois") == "BO" && Initials("") == "?", "Initials fallback is wrong");
                        PumpUntil(() => !grid.HasIcon(1000u), "no icon");
                        Color iconColor = Color.FromArgb(255, 30, 200, 60);
                        Png(Path.Combine(images, "Items", "12", "5.png"), iconColor, 32);
                        AssetsRoot(images);
                        PumpUntil(() => grid.HasIcon(1000u), "icon loaded");
                        int index = grid.IndexOf(1000u); Rectangle cell = grid.CellBounds(index);
                        using (var image = new Bitmap(grid.Width, grid.Height))
                        {
                            grid.DrawToBitmap(image, new Rectangle(Point.Empty, grid.Size));
                            Check(Near(image.GetPixel(cell.X + cell.Width / 2, cell.Y + cell.Height / 2), iconColor), "Item icon is not drawn in its cell");
                        }

                        // Infobulle de la boutique : fiche d'un article de EL au survol.
                        var shopPanel = drawer.Get<ShopPanel>();
                        Feed(account, "ECK0|-8"); Feed(account, "EL2001;7d#5#0#0#;50|"); PumpUntil(() => drawer.Current == shopPanel, "shop panel");
                        var shopList = (ListView)Get(shopPanel, "shopList"); var tips = (ItemTooltipPopup)Get(shopPanel, "itemTips");
                        Check(shopList.Items.Count == 1 && tips != null && tips.Sheet == null, "Shop list or tooltip popup missing");
                        tips.Request(shopList, shopList.PointToScreen(new Point(10, 10)), "article:2001", () => ItemSheet.FromTemplate(2001, "7d#5#0#0#", 1, 50));
                        PumpUntil(() => tips.Sheet != null, "shop tooltip", 4);
                        Check(tips.Sheet.Price == 50 && tips.Sheet.Effects.SequenceEqual(new[] { "+5 en vitalité" }) && tips.Sheet.Name == "Potion fictive", "Shop tooltip sheet is wrong");
                        tips.Hide(); Application.DoEvents(); Check(tips.Sheet == null, "Shop tooltip did not close");
                        Feed(account, "EV"); PumpUntil(() => drawer.Current == panel, "shop closed");
                        form.Close(); Application.DoEvents();
                        Check(BotDialogs.OpenDialogs.Count == 0, "A dialog survived the game window");
                    }
                }
            }
        }
        finally { ServerMessages.Resolver = previousResolver; LangData.Clear(); listener.Stop(); Environment.CurrentDirectory = previous; }
    }
}
