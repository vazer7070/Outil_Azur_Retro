using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
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
using System.Xml.Linq;
using Outil_Azur_complet.Bot;
using Outil_Azur_complet.Bot.Controls;
using Outil_Azur_complet.Bot.Panels;
using Tools_protocol.Parser.XML;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Chat;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Monstres;
using Tool_BotProtocol.Game.Perso.Inventory;
using BotMonster = Tool_BotProtocol.Game.Monstres.Monstres;

// Lot F14, sur un serveur fictif local : table des commandes joueur de StarLoco (syntaxe, refus locaux), volet
// « Commandes du serveur » (.infos → BM*|.infos|, argument, .commandes, réponse cs recopiée, canal général coupé, AR,
// combat), bascules /away et /invisible (BYA/BYI une seule fois, avec confirmation, jamais à l'entrée en jeu, Im037/038/050/051),
// ligne « capturable » de la surtête d'un groupe (colonne capturable exportée dans BotMonsters, arènes 10131 à 10138), pierre
// d'âme pleine (OU seulement dans une arène) et livre du sac (OU, volet Document sur dCK). Données toutes synthétiques,
// écrites dans un dossier temporaire.
internal static class BotServerCommandsSmoke
{
    private const int MapId = 900093, ArenaId = 10131;
    private const int StoneTemplate = 0x1B59, BookTemplate = 0x1B5A, EmptyStoneTemplate = 0x1B5B;
    private const uint Stone = 0x41a, Book = 0x41b, EmptyStone = 0x41c;
    private const string Group = "GM|+5;1;45;-2;101,102,103;-3;1001^100,1002^100,1003^100;5,7,6;-1,-1,-1;0,0,0,0;-1,-1,-1;0,0,0,0;-1,-1,-1;0,0,0,0";

    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            string path = Path.Combine(TestPaths.ApplicationBin, new AssemblyName(e.Name).Name + ".dll");
            if (!File.Exists(path)) path = Path.ChangeExtension(path, "exe");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        try { Application.EnableVisualStyles(); Run(); Console.WriteLine("OK: table des commandes, volet BM*|.cmd|, BYA/BYI confirmés et jamais à l'entrée, capture au survol, pierre d'âme et livre"); }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            foreach (string entry in logs.Reverse().Take(20).Reverse()) Console.Error.WriteLine("  journal : " + entry);
            Environment.ExitCode = 1;
        }
    }

    private static readonly ConcurrentQueue<string> logs = new ConcurrentQueue<string>();

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static object Get(object target, string field) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static IEnumerable<Control> All(Control value) { yield return value; foreach (Control child in value.Controls) foreach (var nested in All(child)) yield return nested; }
    private static IEnumerable<ToolStripItem> Items(ToolStripItemCollection items)
    {
        foreach (ToolStripItem item in items)
        {
            yield return item;
            if (item is ToolStripDropDownItem drop) foreach (var nested in Items(drop.DropDownItems)) yield return nested;
        }
    }
    private static void PumpUntil(Func<bool> done, int seconds = 6)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(seconds);
        while (!done()) { if (DateTime.UtcNow > end) throw new TimeoutException("Server commands loopback timed out"); Application.DoEvents(); Thread.Sleep(5); }
        Application.DoEvents();
    }
    private static void Complete(Task task) { PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static T Result<T>(Task<T> task) { Complete(task); return task.Result; }
    private static void Feed(Accounts account, string packet) { Complete(MessagesReception.ReceptionAsync(account.Connexion, packet)); }
    /// <summary>Paquet traité sur un fil du pool, comme la boucle de réception réseau.</summary>
    private static void FeedFromNetwork(Accounts account, string packet) { Complete(Task.Run(() => MessagesReception.ReceptionAsync(account.Connexion, packet))); }
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
        Check(socket.Available == 0, message + " (reçu : " + (socket.Available > 0 ? Read(socket) : "") + ")");
    }
    private static void Expect(Socket peer, ChatResult result, string packet, string message)
    {
        Check(result.Accepted && result.Sent && result.Packet == packet, message + " (résultat : " + result.Packet + " / " + result.Message + ")");
        string read = Read(peer);
        Check(read == packet, message + " (lu : " + read + ")");
    }
    private static void Refused(Socket peer, ChatResult result, string expected, string message)
    {
        Check(!result.Accepted && !result.Sent && result.Message.Contains(expected), message + " (résultat : " + result.Message + ")");
        NoPacket(peer, message);
    }
    private static void Click(Button button) { Check(button != null, "Missing button"); button.PerformClick(); }
    private static bool Chat(Accounts account, ChatMessageKind kind, string text) => account.Game.Chat.Messages.Any(message => message.Kind == kind && message.Text.Contains(text));
    private static Map Synthetic(int id) => new Map { MapID = id, MapWidth = 3, MapHeight = 4, MapData = string.Concat(Enumerable.Repeat("HhGaeaaaaa", 18)) };
    private static XElement Template(int id, string name, string capturable) =>
        XElement.Parse("<RECORD><ID>" + id + "</ID><NAME>" + name + "</NAME><GFX>1</GFX>" + (capturable == null ? "" : "<CAPTURABLE>" + capturable + "</CAPTURABLE>") + "</RECORD>");

    private static void Run()
    {
        string previous = Environment.CurrentDirectory;
        string folder = Path.Combine(TestPaths.Work, "bot-server-commands-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); Environment.CurrentDirectory = folder;
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            LangData.Clear();
            MessagesReception.Init();
            Table();
            Export(folder);
            Capture();
            Map.AllBotMaps[MapId] = Synthetic(MapId);
            Map.AllBotMaps[ArenaId] = Synthetic(ArenaId);
            InventoryObjects.FullInventory[StoneTemplate] = new InventoryObjects { ID = StoneTemplate, Name = "Pierre pleine fictive", Type = SoulStones.FullStoneType, Level = 1, pods = 1 };
            InventoryObjects.FullInventory[BookTemplate] = new InventoryObjects { ID = BookTemplate, Name = "Livre fictif", Type = SpecialItems.DocumentType, Level = 1, pods = 1 };
            InventoryObjects.FullInventory[EmptyStoneTemplate] = new InventoryObjects { ID = EmptyStoneTemplate, Name = "Pierre vide fictive", Type = SoulStones.EmptyStoneType, Level = 1, pods = 1 };

            using (var account = new Accounts(new AccountConfig("synthetic-commands", "synthetic", "loopback")))
            {
                account.Logger.log_event += (entry, color) => logs.Enqueue(entry.message);
                ChatService chat = account.Game.Chat;
                PlayerPresence presence = account.Game.Presence;
                Check(presence != null && !presence.IsAway && !presence.IsInvisible, "Game.Presence is missing or not reset");
                // Hors connexion : rien n'est envoyé, refus expliqué.
                Check(!Result(ServerCommands.SendLineAsync(chat, ".infos")).Accepted && !Result(presence.ToggleAwayAsync()).Accepted && !presence.IsAwayPending,
                    "A command or BYA was accepted while disconnected");

                Task<Socket> accept = listener.AcceptSocketAsync();
                Complete(account.Connexion.ConnectToServer(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port)); Complete(accept);
                using (Socket peer = accept.Result)
                {
                    peer.ReceiveTimeout = 6000;
                    // Entrée en jeu : GC1 seul, jamais BYA ni BYI.
                    Feed(account, "ASK|8|Second|120|2|1|20|0|0|0|");
                    Check(Read(peer) == "GC1", "ASK did not send GC1"); NoPacket(peer, "Game entry sent more than GC1 (BYA/BYI)");
                    Feed(account, "GCK|1|Second"); NoPacket(peer, "GCK was answered (BYA/BYI)");
                    Feed(account, "GDM|" + MapId + "|date|key"); Check(Read(peer) == "GI", "GDM did not request GI");
                    account.AccountStates = AccountStates.CONNECTED_INACTIVE;

                    Commands(account, chat, peer);
                    MapGroup(account);
                    Inventory(account, peer, false);

                    string docs = Path.Combine(folder, "docs"); Directory.CreateDirectory(docs);
                    File.WriteAllText(Path.Combine(docs, "7_0102030405.xml"), "<?xml version='1.0' encoding='utf-8'?>\n<BotDoc id=\"7\" date=\"0102030405\" type=\"book\" style=\"1\" source=\"test\">\n"
                        + "<titre>Livre fictif</titre>\n<page>&lt;p class='n'&gt;Page fictive du livre&lt;/p&gt;</page>\n</BotDoc>\n", new UTF8Encoding(false));
                    account.Game.Interactions.Interactive.Document.DocumentsPath = docs;

                    using (var form = new GameClientFullform(account))
                    {
                        form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); Application.DoEvents();
                        NoPacket(peer, "Opening the game window sent a packet (BYA/BYI)");
                        PanelHost drawer = form.Panels;
                        var panel = drawer.Get<CommandsHelpPanel>();
                        Check(panel != null && panel.Title == "Commandes du serveur", "Server commands panel is not registered");
                        ToolStripItem menu = All(form).OfType<MenuStrip>().SelectMany(strip => Items(strip.Items)).FirstOrDefault(item => item.Text == "Commandes du serveur");
                        Check(menu != null, "The character menu has no « Commandes du serveur » entry");
                        menu.PerformClick(); PumpUntil(() => drawer.Current == panel);
                        NoPacket(peer, "Opening the panel sent a packet");
                        CommandsPanel(account, peer, panel);
                        PresencePanel(account, peer, panel);
                        InventoryPanelChecks(account, peer, drawer);

                        // Carte d'arène : la pierre pleine part (OU), la surtête annonce l'arène.
                        Feed(account, "GDM|" + ArenaId + "|date|key"); Check(Read(peer) == "GI", "Arena GDM did not request GI");
                        Inventory(account, peer, true);
                        Feed(account, Group);
                        MonsterGroupActor arenaGroup = account.Game.Map.Entites.Values.OfType<MonsterGroupActor>().Single();
                        Check(OverheadLayer.Describe(arenaGroup, false, false, account.Game.Map.MapID).ExtraLines.SequenceEqual(new[] { "Arène : pas de capture" }),
                            "The arena group overhead does not say there is no capture");
                        NoPacket(peer, "An unexpected packet was sent at the end");
                        form.Close();
                    }
                }
            }
            Check(!logs.Any(entry => entry.Contains("absent/invisible a échoué")), "A presence subscriber failed");
        }
        finally
        {
            LangData.Clear();
            BotMonster ignored;
            foreach (int id in new[] { 101, 102, 103, 104, 105 }) BotMonster.AllMonstersTemplate.TryRemove(id, out ignored);
            InventoryObjects removed;
            foreach (int id in new[] { StoneTemplate, BookTemplate, EmptyStoneTemplate }) InventoryObjects.FullInventory.TryRemove(id, out removed);
            Map map;
            Map.AllBotMaps.TryRemove(MapId, out map); Map.AllBotMaps.TryRemove(ArenaId, out map);
            listener.Stop(); Environment.CurrentDirectory = previous;
        }
    }

    /// <summary>Table d'aide : commandes de la fiche, noms uniques, syntaxe et refus locaux.</summary>
    private static void Table()
    {
        foreach (string name in new[] { "infos", "banque", "start", "tp", "vie", "level", "pvp", "staff", "boost", "maitre", "transfert", "noall" })
            Check(ServerCommands.All.Any(command => command.Name == name), "Missing server command ." + name);
        Check(ServerCommands.All.Select(command => command.Name.ToLowerInvariant()).Distinct().Count() == ServerCommands.All.Count, "Duplicate server command");
        Check(ServerCommands.All.All(command => command.Usage.StartsWith("." + command.Name) && command.Help.Length > 0 && (command.Choices.Count == 0 || command.HasArgument)),
            "A server command has no usage or help");
        Check(ServerCommands.TryGet(".INFOS", out ServerCommand infos) && infos.Name == "infos" && ServerCommands.TryGet(" banque ", out ServerCommand bank) && bank.OutOfFight
            && !ServerCommands.TryGet("commandes", out ServerCommand unknown) && unknown == null, "TryGet does not find commands with or without the dot");
        Check(ServerCommands.All.Where(command => command.Category == ServerCommandCategory.Teleportation).All(command => command.OutOfFight),
            "Teleportation commands are not marked out of fight");

        Check(infos.BuildLine(null) == ".infos" && infos.Validate("") == null && infos.Validate("x") != null, ".infos syntax is wrong");
        ServerCommands.TryGet("level", out ServerCommand level);
        Check(level.Usage == ".level <niveau>" && level.Validate(" 120 ") == null && level.BuildLine(" 120 ") == ".level 120" && level.Validate("abc") != null
            && level.Validate("0") != null && level.Validate("") != null && level.Validate("-5") != null, ".level validation is wrong");
        ServerCommands.TryGet("boost", out ServerCommand boost);
        Check(boost.Validate("vita   10") == null && boost.BuildLine("vita   10") == ".boost vita 10" && boost.Validate("vita") != null
            && boost.Validate("pods 10") != null && boost.Choices.Contains("sagesse"), ".boost validation is wrong");
        ServerCommands.TryGet("exo", out ServerCommand exo);
        Check(exo.Validate("coiffe pa") == null && exo.Validate("cape PM") == null && exo.Validate("coiffe po") != null && exo.Validate("bras pa") != null, ".exo validation is wrong");
        ServerCommands.TryGet("jetmax", out ServerCommand jetmax);
        Check(jetmax.Validate("all") == null && jetmax.Validate("anneauG") == null && jetmax.Validate("bras") != null, ".jetmax validation is wrong");
        ServerCommands.TryGet("house", out ServerCommand house);
        Check(house.Usage == ".house [all]" && house.Validate("") == null && house.Validate("all") == null && house.Validate("tout") != null, ".house validation is wrong");
        ServerCommands.TryGet("maitre", out ServerCommand master);
        Check(master.Validate("") == null && master.Validate("Chef") == null && master.Validate("a|b") != null && master.Validate("C") != null
            && master.Validate("Deux mots") != null, ".maitre validation is wrong");
        ServerCommands.TryGet("all", out ServerCommand all);
        Check(all.FreeText && all.Validate("") != null && all.BuildLine("  bonjour   à  tous ") == ".all bonjour   à  tous", ".all does not keep its text");

        // Arènes : test de sous-chaîne de SoulStone.isInArenaMap reproduit tel quel.
        Check(SoulStones.ArenaMaps.SequenceEqual(Enumerable.Range(10131, 8)), "Arena maps are not 10131 to 10138");
        Check(SoulStones.IsArenaMap(10131) && SoulStones.IsArenaMap(10138) && !SoulStones.IsArenaMap(10139) && !SoulStones.IsArenaMap(MapId)
            && SoulStones.IsArenaMap(1013) && SoulStones.IsArenaMap(101), "IsArenaMap does not match the server substring test");
    }

    /// <summary>Export BotMonsters : colonne capturable lue comme MonsterData (1 seulement), absente sans colonne.</summary>
    private static void Export(string folder)
    {
        string output = Path.Combine(folder, "BotMonsters");
        var table = new DataTable("monsters");
        table.Columns.Add("id", typeof(int)); table.Columns.Add("name", typeof(string)); table.Columns.Add("gfxID", typeof(int)); table.Columns.Add("capturable", typeof(string));
        table.Rows.Add(101, "Monstre capturable", 11, "1");
        table.Rows.Add(104, "Monstre protégé", 14, "0");
        table.Rows.Add(105, "Monstre sans valeur", 15, DBNull.Value);
        table.Rows.Add(106, "Monstre à valeur inattendue", 16, "2");
        Check(XmlParser.ExportBotRecords(output, "Monstres", table.CreateDataReader(), new BotExportContext()) == 4, "Monster export count");
        var templates = Directory.GetFiles(output, "*.xml").Select(file => BotMonster.ParseTemplate(XElement.Load(file))).ToDictionary(template => template.TemplateID);
        Check(templates.Count == 4 && templates[101].Capturable == true && templates[101].GFX == 11 && templates[104].Capturable == false
            && templates[105].Capturable == null && templates[106].Capturable == false, "CAPTURABLE is not exported like MonsterData");

        string bare = Path.Combine(folder, "BotMonstersSansColonne");
        var old = new DataTable("monsters");
        old.Columns.Add("id", typeof(int)); old.Columns.Add("name", typeof(string)); old.Columns.Add("gfxID", typeof(int));
        old.Rows.Add(101, "Monstre ancien", 11);
        Check(XmlParser.ExportBotRecords(bare, "Monstres", old.CreateDataReader(), new BotExportContext()) == 1, "Bare monster export count");
        XElement record = XElement.Load(Directory.GetFiles(bare, "*.xml").Single());
        Check(record.Element("CAPTURABLE") == null && BotMonster.ParseTemplate(record).Capturable == null, "A table without capturable produced CAPTURABLE");
    }

    /// <summary>Surtête d'un groupe : ligne « capturable » selon les fiches BotMonsters et la carte.</summary>
    private static void Capture()
    {
        GmParseResult parsed = GmParser.Parse(Group, false, 42, -1);
        Check(parsed.Rejected.Count == 0 && parsed.Entries.Count == 1, "Synthetic group GM was rejected");
        var group = (MonsterGroupActor)parsed.Entries[0].Actor;

        // Sans fiche : rien n'est affiché (Unknown), la surtête reste celle du client.
        Check(SoulStones.Evaluate(group, MapId) == GroupCapture.Unknown && OverheadLayer.DescribeGroup(group, MapId).ExtraLines.Count == 0,
            "A group without templates shows a capture line");
        foreach (int id in new[] { 101, 102, 103 }) BotMonster.AllMonstersTemplate[id] = BotMonster.ParseTemplate(Template(id, "Monstre " + id, "1"));
        OverheadContent capturable = OverheadLayer.DescribeGroup(group, MapId);
        Check(SoulStones.Evaluate(group, MapId) == GroupCapture.Capturable && capturable.ExtraLines.SequenceEqual(new[] { "Capturable (pierre d'âme)" })
            && capturable.ExtraColor == OverheadLayer.TextExtra && capturable.ToString().Contains("Capturable"), "A capturable group has no « capturable » line: " + capturable);
        Check(OverheadLayer.Describe(group, false, false, MapId).ExtraLines.Contains("Capturable (pierre d'âme)"), "Describe does not forward the map to the group");
        Check(OverheadLayer.DescribeGroup(group, 0).ExtraLines.SequenceEqual(new[] { "Capturable (pierre d'âme)" }), "An unknown map hides the capture line");
        Check(OverheadLayer.DescribeGroup(group, ArenaId).ExtraLines.SequenceEqual(new[] { "Arène : pas de capture" }), "An arena group is announced as capturable");

        BotMonster.AllMonstersTemplate[102] = BotMonster.ParseTemplate(Template(102, "Monstre 102", "0"));
        Check(SoulStones.Evaluate(group, MapId) == GroupCapture.NotCapturable && OverheadLayer.DescribeGroup(group, MapId).ExtraLines.SequenceEqual(new[] { "Non capturable" }),
            "One non-capturable member does not make the group non-capturable");
        BotMonster.AllMonstersTemplate[102] = BotMonster.ParseTemplate(Template(102, "Monstre 102", null));
        Check(SoulStones.Evaluate(group, MapId) == GroupCapture.Unknown && OverheadLayer.DescribeGroup(group, MapId).ExtraLines.Count == 0,
            "A member without CAPTURABLE does not hide the line");
        BotMonster.AllMonstersTemplate[102] = BotMonster.ParseTemplate(Template(102, "Monstre 102", "1"));
        Check(SoulStones.Evaluate(null, MapId) == GroupCapture.Unknown && SoulStones.Describe(GroupCapture.Unknown) == null, "A null group is not unknown");

        // La ligne agrandit la surtête et se dessine sans erreur.
        OverheadContent plain = OverheadLayer.DescribeGroup(group, MapId);
        using (var bitmap = new Bitmap(240, 160))
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            Size with = OverheadLayer.Measure(graphics, plain);
            plain.ExtraLines.Clear();
            Size without = OverheadLayer.Measure(graphics, plain);
            Check(with.Height > without.Height && with.Width >= without.Width, "The capture line does not enlarge the overhead: " + with + " / " + without);
            OverheadLayer.Draw(graphics, OverheadLayer.DescribeGroup(group, MapId), new Rectangle(4, 4, with.Width, with.Height), null);
        }
    }

    /// <summary>Commandes envoyées par l'API : BM*|.cmd|, refus locaux sans paquet.</summary>
    private static void Commands(Accounts account, ChatService chat, Socket peer)
    {
        ServerCommands.TryGet("infos", out ServerCommand infos);
        Expect(peer, Result(ServerCommands.SendAsync(chat, infos)), "BM*|.infos|", ".infos is not BM*|.infos|");
        ServerCommands.TryGet("all", out ServerCommand all);
        Expect(peer, Result(ServerCommands.SendAsync(chat, all, " bonjour  à tous ")), "BM*|.all bonjour  à tous|", ".all lost its spacing");
        Expect(peer, Result(ServerCommands.RequestServerHelpAsync(chat)), "BM*|.commandes|", "Server help is not BM*|.commandes|");
        Expect(peer, Result(ServerCommands.SendLineAsync(chat, "  .vie ")), "BM*|.vie|", "A raw line is not trimmed");
        Refused(peer, Result(ServerCommands.SendLineAsync(chat, "..vie")), "commence", "A line starting with .. was sent");
        Refused(peer, Result(ServerCommands.SendLineAsync(chat, ". vie")), "commence", "A dot followed by a space was sent");
        Refused(peer, Result(ServerCommands.SendLineAsync(chat, "vie")), "commence", "A line without the dot was sent");
        Refused(peer, Result(ServerCommands.SendAsync(chat, infos, "inutile")), "ne prend pas d'argument", "An extra argument was sent");
        Refused(peer, Result(ServerCommands.SendAsync(chat, null)), "Choisissez", "A null command was sent");
        Check(Chat(account, ChatMessageKind.Error, "ne prend pas d'argument"), "A refused command is not explained in the chat");

        // Canal général coupé (cC-*), restrictions AR (bit 16), combat.
        Feed(account, "cC+*#"); Feed(account, "cC-*");
        Refused(peer, Result(ServerCommands.SendAsync(chat, infos)), "canal général est désactivé", "A command was sent with the general channel off");
        Feed(account, "cC+*");
        Feed(account, "ARg");
        Refused(peer, Result(ServerCommands.SendAsync(chat, infos)), "restrictions", "A command was sent while AR forbids the general channel");
        Feed(account, "AR6bk");
        ServerCommands.TryGet("start", out ServerCommand start);
        account.AccountStates = AccountStates.FIGHTING;
        Check(ServerCommands.IsFighting(chat), "IsFighting ignores the fight state");
        Refused(peer, Result(ServerCommands.SendAsync(chat, start)), "en combat", ".start was sent during a fight");
        Expect(peer, Result(ServerCommands.SendAsync(chat, infos)), "BM*|.infos|", ".infos is refused during a fight");
        account.AccountStates = AccountStates.CONNECTED_INACTIVE;
    }

    /// <summary>Groupe de la carte : même surtête que celle de la carte de jeu (identifiant de la carte transmis).</summary>
    private static void MapGroup(Accounts account)
    {
        Feed(account, Group);
        MonsterGroupActor group = account.Game.Map.Entites.Values.OfType<MonsterGroupActor>().SingleOrDefault();
        Check(group != null && account.Game.Map.MapID == MapId, "The map group was not added");
        Check(OverheadLayer.Describe(group, false, false, account.Game.Map.MapID).ExtraLines.SequenceEqual(new[] { "Capturable (pierre d'âme)" }),
            "The map group overhead has no « capturable » line");
        Feed(account, "GM|--2");
    }

    /// <summary>Pierre d'âme pleine, livre et pierre vide du sac (OAK synthétique).</summary>
    private static void Inventory(Accounts account, Socket peer, bool arena)
    {
        Feed(account, "OAKO" + Stone.ToString("x") + "~" + StoneTemplate.ToString("x") + "~1~~;" + Book.ToString("x") + "~" + BookTemplate.ToString("x") + "~1~~;"
            + EmptyStone.ToString("x") + "~" + EmptyStoneTemplate.ToString("x") + "~2~~");
        var bag = account.Game.character.Inventory;
        InventoryObjects stone = bag.GetByInventoryId(Stone), book = bag.GetByInventoryId(Book), empty = bag.GetByInventoryId(EmptyStone);
        Check(SpecialItems.IsFullSoulStone(stone) && SpecialItems.IsDocument(book) && SpecialItems.IsEmptySoulStone(empty) && !SpecialItems.IsDocument(stone),
            "Special items are not recognised by type");
        int map = account.Game.Map.MapID;
        Check(SpecialItems.Note(empty, map).Contains("vide") && SpecialItems.Note(book, map).Contains("dCK") && SpecialItems.Note(null, map) == null,
            "Item notes are wrong");
        if (!arena)
        {
            Check(SpecialItems.Note(stone, map).Contains("10131 à 10138"), "The soul stone note does not name the arenas");
            ItemUseResult refused = Result(SpecialItems.UseAsync(account.Game, stone));
            Check(!refused.Sent && refused.Message.Contains("10131") && refused.Message.Contains(map.ToString()), "A full soul stone outside an arena was not refused: " + refused.Message);
            NoPacket(peer, "A full soul stone outside an arena sent OU");
            ItemUseResult read = Result(SpecialItems.UseAsync(account.Game, book));
            Check(read.Sent && read.Message.Contains("OU") && Read(peer) == "OU" + Book + "|", "A book is not used with OU<id>|");
        }
        else
        {
            Check(SpecialItems.Note(stone, map).Contains("Im022"), "The arena note does not announce Im022");
            ItemUseResult summon = Result(SpecialItems.UseAsync(account.Game, stone));
            Check(summon.Sent && summon.Message.Contains("Im022") && Read(peer) == "OU" + Stone + "|", "A full soul stone in an arena is not used with OU<id>|");
            Check(!Result(SpecialItems.UseAsync(account.Game, null)).Sent, "A null item was used");
            NoPacket(peer, "A null item sent a packet");
        }
    }

    /// <summary>Volet : choix de .infos → BM*|.infos|, argument, aide du serveur, réponse cs, refus.</summary>
    private static void CommandsPanel(Accounts account, Socket peer, CommandsHelpPanel panel)
    {
        Check(panel.CommandList.Items.Count == ServerCommands.All.Count && panel.Selected == null && !panel.SendButton.Enabled && panel.ServerHelpButton.Enabled,
            "The panel does not list every command with nothing selected");
        Check(panel.StatusText.Contains("Rien n'est envoyé sans clic"), "The idle status is missing");
        panel.FamilyBox.SelectedIndex = 1 + (int)ServerCommandCategory.Banque; Application.DoEvents();
        Check(panel.CommandList.Items.Count == 2 && panel.CommandList.Items.Cast<ListViewItem>().All(row => ((ServerCommand)row.Tag).Category == ServerCommandCategory.Banque),
            "The family filter does not keep only the bank commands");

        // Choix de .infos (famille réinitialisée), puis Envoyer : BM*|.infos| lu.
        Check(panel.SelectCommand(".infos") && panel.FamilyBox.SelectedIndex == 0 && panel.Selected?.Name == "infos", "SelectCommand(.infos) failed");
        Check(panel.DetailsText.StartsWith(".infos — ") && !panel.ArgumentBox.Enabled && panel.SendButton.Enabled, "The .infos details or buttons are wrong");
        Click(panel.SendButton);
        Check(Read(peer) == "BM*|.infos|", "Choosing .infos did not send BM*|.infos|");
        PumpUntil(() => panel.StatusText.StartsWith("Envoyé : .infos"));
        FeedFromNetwork(account, "cs<font color='#009900'>Serveur fictif : 3 joueurs</font>");
        PumpUntil(() => panel.StatusText == "Réponse du serveur : Serveur fictif : 3 joueurs");
        NoPacket(peer, ".infos was sent twice");

        // Argument : refus local sans paquet, puis valeur normalisée.
        Check(panel.SelectCommand("level") && panel.ArgumentBox.Enabled && panel.DetailsText.Contains("Hors combat"), "The .level details are wrong");
        panel.ArgumentBox.Text = "abc"; Click(panel.SendButton);
        NoPacket(peer, "An invalid .level was sent");
        PumpUntil(() => panel.StatusText.StartsWith("Syntaxe : .level <niveau>"));
        panel.ArgumentBox.Text = " 120 "; Click(panel.SendButton);
        Check(Read(peer) == "BM*|.level 120|", "The .level argument was not sent trimmed");
        Check(panel.SelectCommand("boost") && panel.ArgumentBox.Items.Cast<string>().SequenceEqual(new[] { "vita", "sagesse", "force", "intel", "chance", "agi" }),
            "The .boost choices are not offered");
        panel.ArgumentBox.Text = "vita   10"; Click(panel.SendButton);
        Check(Read(peer) == "BM*|.boost vita 10|", "The .boost argument was not sent with single spaces");
        Click(panel.ServerHelpButton);
        Check(Read(peer) == "BM*|.commandes|", "The server help button does not send BM*|.commandes|");

        // Canal général coupé : refus affiché, rien n'est envoyé.
        Check(panel.SelectCommand("infos"), "SelectCommand(infos) failed");
        Feed(account, "cC-*");
        Click(panel.SendButton);
        NoPacket(peer, "The panel sent a command with the general channel off");
        PumpUntil(() => panel.StatusText.Contains("canal général est désactivé"));
        Feed(account, "cC+*");

        // Combat : les commandes « hors combat » sont grisées, les autres restent possibles.
        account.AccountStates = AccountStates.FIGHTING;
        Check(panel.SelectCommand("start") && !panel.SendButton.Enabled, "Send is enabled for .start during a fight");
        Click(panel.SendButton); NoPacket(peer, ".start was sent during a fight");
        Check(panel.SelectCommand("vie") && panel.SendButton.Enabled, "Send is disabled for .vie during a fight");
        account.AccountStates = AccountStates.CONNECTED_INACTIVE;
        Check(panel.SelectCommand("start") && panel.SendButton.Enabled, "Send stays disabled after the fight");
    }

    /// <summary>Volet : BYA/BYI une seule fois, avec confirmation avant l'état restreint ; Im037/038/050/051 ; remise à zéro.</summary>
    private static void PresencePanel(Accounts account, Socket peer, CommandsHelpPanel panel)
    {
        PlayerPresence presence = account.Game.Presence;
        Check(panel.PresenceText.StartsWith("Présent · visible") && panel.AwayButton.Text == "Absent (BYA)" && panel.AwayButton.Enabled
            && panel.WarningText.StartsWith("Jamais envoyé automatiquement"), "The idle presence box is wrong: " + panel.PresenceText + " / " + panel.WarningText);

        // Premier clic : avertissement et confirmation, aucun paquet.
        Click(panel.AwayButton);
        NoPacket(peer, "The first click on Absent sent BYA");
        Check(panel.AwayButton.Text == "Confirmer : absent" && panel.WarningText.Contains("messages privés") && panel.WarningText.Contains("BYA"),
            "The away warning is not shown before sending");
        // Changer de commande désarme la bascule.
        Check(panel.SelectCommand("tp") && panel.AwayButton.Text == "Absent (BYA)", "Selecting a command did not disarm the toggle");
        Click(panel.AwayButton); Check(panel.AwayButton.Text == "Confirmer : absent", "The toggle was not armed again");

        // Second clic : BYA lu une fois, puis bascule en attente (bouton grisé, nouvel envoi refusé).
        Click(panel.AwayButton);
        Check(Read(peer) == "BYA", "Confirming Absent did not send BYA");
        PumpUntil(() => !panel.AwayButton.Enabled && presence.IsAwayPending);
        Check(!Result(presence.ToggleAwayAsync()).Accepted, "A second BYA was accepted while the first one waits");
        NoPacket(peer, "BYA was sent more than once");
        Check(Chat(account, ChatMessageKind.Info, "BYA envoyé : le personnage sera absent."), "The BYA warning is not written in the chat");

        // Im037 : absent ; le retour (BYA) part sans confirmation ; Im038 : présent.
        FeedFromNetwork(account, "Im037");
        PumpUntil(() => presence.IsAway && panel.AwayButton.Enabled && panel.AwayButton.Text == "Revenir (BYA)" && panel.PresenceText.StartsWith("Absent"));
        Click(panel.AwayButton);
        Check(Read(peer) == "BYA", "Coming back does not send BYA");
        FeedFromNetwork(account, "Im038");
        PumpUntil(() => !presence.IsAway && panel.AwayButton.Text == "Absent (BYA)" && panel.AwayButton.Enabled);
        NoPacket(peer, "Coming back sent more than one BYA");

        // Invisible : avertissement, BYI une fois, Im050 puis Im051.
        Click(panel.InvisibleButton);
        NoPacket(peer, "The first click on Invisible sent BYI");
        Check(panel.InvisibleButton.Text == "Confirmer : invisible" && panel.WarningText.Contains("amis") && panel.WarningText.Contains("BYI"),
            "The invisible warning is not shown before sending");
        Click(panel.InvisibleButton);
        Check(Read(peer) == "BYI", "Confirming Invisible did not send BYI");
        FeedFromNetwork(account, "Im050");
        PumpUntil(() => presence.IsInvisible && panel.PresenceText.Contains("invisible") && panel.InvisibleButton.Text == "Redevenir visible (BYI)");
        NoPacket(peer, "BYI was sent more than once");
        Feed(account, "Im0151;x"); Check(presence.IsInvisible, "An unrelated Im changed the presence");

        // Nouvelle entrée en jeu : StarLoco a tout remis à zéro, rien n'est renvoyé.
        Feed(account, "GCK|1|Second");
        PumpUntil(() => !presence.IsInvisible && panel.InvisibleButton.Text == "Invisible (BYI)");
        NoPacket(peer, "Game entry sent BYA or BYI");
    }

    /// <summary>Volet Inventaire : « Lire » pour un livre (OU puis volet Document sur dCK), pierre pleine refusée hors arène.</summary>
    private static void InventoryPanelChecks(Accounts account, Socket peer, PanelHost drawer)
    {
        var panel = drawer.Get<InventoryPanel>();
        drawer.Show(panel); PumpUntil(() => drawer.Current == panel);
        var list = (ListView)Get(panel, "inventory");
        var use = (Button)Get(panel, "useItem");
        var help = (Label)Get(panel, "inventoryHelp");
        Func<uint, bool> select = id =>
        {
            ListViewItem row = list.Items.Cast<ListViewItem>().FirstOrDefault(item => (uint)item.Tag == id);
            if (row == null) return false;
            list.SelectedItems.Clear(); row.Selected = true; panel.RefreshView();
            return list.SelectedItems.Count == 1;
        };
        Check(select(Book) && use.Text == "Lire" && use.Enabled && help.Text.Contains("dCK"), "The book row does not offer « Lire »: " + use.Text + " / " + help.Text);
        Click(use);
        Check(Read(peer) == "OU" + Book + "|", "« Lire » does not send OU<id>|");
        // Un serveur qui répondrait dCK ouvre le volet Document du lot M3.
        Feed(account, "dCK7");
        DocumentPanel document = drawer.Get<DocumentPanel>();
        PumpUntil(() => drawer.Current == document);
        Check(account.Game.Interactions.Interactive.Document.Current?.Title == "Livre fictif", "dCK after a book did not open the document panel");
        Check(Result(account.Game.Interactions.Interactive.Document.LeaveAsync()).Sent && Read(peer) == "dV", "Leaving the document does not send dV");
        Feed(account, "dV");
        PumpUntil(() => drawer.Current != document);
        drawer.Show(panel); PumpUntil(() => drawer.Current == panel);

        Check(select(Stone) && use.Text == "Utiliser" && help.Text.Contains("10131 à 10138"), "The soul stone row does not name the arenas: " + help.Text);
        Click(use);
        NoPacket(peer, "« Utiliser » on a full soul stone outside an arena sent OU");
        Check(select(EmptyStone) && use.Text == "Utiliser" && help.Text.Contains("Pierre d'âme vide"), "The empty stone note is missing");
        drawer.CloseAll(); Application.DoEvents();
    }
}
