using System;
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
using Outil_Azur_complet.Bot.Interfaces;
using Outil_Azur_complet.Bot.Menus;
using Outil_Azur_complet.Bot.Panels;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Actions;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Managers.Mouvements;
using Tool_BotProtocol.Game.Managers.recoltes;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Interactives;
using Tool_BotProtocol.Game.Maps.Mouvements;
using Tool_BotProtocol.Utils.Crypto;

// Objets interactifs de la carte (lot M3) : modèle (GDF, gfx de repli), menu des compétences, GA500 et anti-spam de 800 ms,
// action 501 et GKK à la fin de la durée (horloge injectée), IQ/IO, file d'attente pendant une marche, marche jusqu'à
// l'objet, service de récolte, zaap reconnu par son gfx, zaapi (Wc/Wu/Wv et volet), code (KC/KK/KKE/KV et volet),
// documents (dCK/dV et volet), enclos (Rp), paquets malformés ; jamais de GA034 ni de WU pour un zaapi.
// Données synthétiques (carte, textes, documents), serveur fictif local, fenêtre hors de l'écran, aucune capture d'écran.
internal static class BotInteractivesSmoke
{
    private const int MapId = 900095, OtherMapId = 900096, Width = 6, Height = 6;
    private const short Self = 30, Tree = 24, Zaap = 25, Zaapi = 36, Chest = 47, Door = 52, Park = 8, FarTree = 58;
    private const string SelfGm = ";1;0;42;Personnage de test;1;10^100;0;1,0,0,32";
    private static readonly List<Exception> uiErrors = new List<Exception>();
    private static readonly List<string> sent = new List<string>();

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
            Console.WriteLine("OK: interactifs (GDF, menu, GA500 + anti-spam, 501 → GKK, IQ/IO, marche et file, récolte), zaap par gfx, zaapi Wc/Wu/Wv, code KC/KK/KV, documents dCK/dV, enclos, paquets malformés");
        }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
        finally { LangData.Clear(); }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void PumpUntil(Func<bool> done)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(6);
        while (!done()) { if (DateTime.UtcNow > end) throw new TimeoutException("Interactives loopback timed out"); Application.DoEvents(); Thread.Sleep(5); }
        Application.DoEvents();
    }
    private static void Complete(Task task) { PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static T Complete<T>(Task<T> task) { PumpUntil(() => task.IsCompleted); return task.GetAwaiter().GetResult(); }
    private static void Feed(Accounts account, string packet) { Complete(MessagesReception.ReceptionAsync(account.Connexion, packet)); }
    private static string Read(Socket socket)
    {
        PumpUntil(() => socket.Available > 0);
        using (var stream = new MemoryStream())
        {
            byte[] one = new byte[1];
            while (true)
            {
                if (socket.Receive(one) != 1) throw new IOException("Loopback disconnected");
                if (one[0] == 0) { string packet = Encoding.UTF8.GetString(stream.ToArray()).TrimEnd('\r', '\n'); sent.Add(packet); return packet; }
                stream.WriteByte(one[0]);
            }
        }
    }
    private static void NoPacket(Socket socket, string message)
    {
        DateTime end = DateTime.UtcNow.AddMilliseconds(250);
        while (DateTime.UtcNow < end) { Application.DoEvents(); Thread.Sleep(5); }
        Check(socket.Available == 0, message);
    }
    private static void Expect(Socket socket, string packet, string message)
    {
        string read = Read(socket);
        Check(read == packet, message + " (reçu « " + read + " »)");
    }
    private static object Call(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(target, args);
    private static object Property(object target, string name) =>
        target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(target);
    private static string[] Texts(ContextMenuStrip menu) => menu.Items.Cast<ToolStripItem>().Skip(1).Select(item => item is ToolStripSeparator ? "-" : item.Text).ToArray();
    private static ToolStripMenuItem Item(ContextMenuStrip menu, string text) =>
        menu.Items.OfType<ToolStripMenuItem>().FirstOrDefault(item => item.Text == text) ?? throw new Exception("Menu entry missing: " + text + " in " + string.Join("|", Texts(menu)));

    /// <summary>Horloge manuelle partagée par les objets interactifs (anti-spam, durée de l'action 501) et la marche.</summary>
    private sealed class ManualClock : InteractionClock, IMoveClock
    {
        private sealed class Waiter { public long Due; public TaskCompletionSource<bool> Source; }
        private readonly object sync = new object();
        private readonly List<Waiter> waiters = new List<Waiter>();
        private long now = 500000;

        public override long NowMs { get { lock (sync) return now; } }
        public long NowMilliseconds => NowMs;
        public int Pending { get { lock (sync) return waiters.Count; } }

        public override Task Delay(int milliseconds, CancellationToken token)
        {
            if (token.IsCancellationRequested) return Task.FromCanceled(token);
            var waiter = new Waiter { Source = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously) };
            lock (sync)
            {
                waiter.Due = now + Math.Max(0, milliseconds);
                if (waiter.Due <= now) return Task.CompletedTask;
                waiters.Add(waiter);
            }
            token.Register(() => { lock (sync) waiters.Remove(waiter); waiter.Source.TrySetCanceled(); });
            return waiter.Source.Task;
        }

        public void Advance(long milliseconds)
        {
            List<Waiter> due;
            lock (sync)
            {
                now += milliseconds;
                due = waiters.Where(waiter => waiter.Due <= now).OrderBy(waiter => waiter.Due).ToList();
                waiters.RemoveAll(waiter => waiter.Due <= now);
            }
            foreach (Waiter waiter in due) waiter.Source.TrySetResult(true);
        }
    }

    private static string Ch(int value) => Hash.caracteres_array[value].ToString();
    /// <summary>Cellule praticable sans objet (active, ligne de vue, niveau 7, déplacement 4, plate).</summary>
    private const string Plain = "HhGaeaaaaa";
    /// <summary>Cellule d'objet interactif : couche objet 2 = gfx, bit « interactif » (cellData[7] &amp; 2), déplacement 1.</summary>
    private static string InteractiveCell(int gfx) =>
        Ch(33 | ((gfx & 8192) != 0 ? 2 : 0)) + "h" + Ch(8) + "aeaa" + Ch(2 | ((gfx & 4096) != 0 ? 1 : 0)) + Ch((gfx >> 6) & 63) + Ch(gfx & 63);

    private static Map SyntheticMap()
    {
        var objects = new Dictionary<int, int> { { Tree, 7500 }, { Zaap, 7000 }, { Zaapi, 7030 }, { Chest, 7350 }, { Door, 6700 }, { Park, 6763 }, { FarTree, 7500 } };
        var data = new StringBuilder();
        for (int cell = 0; cell < (2 * Width - 1) * Height; cell++) data.Append(objects.TryGetValue(cell, out int gfx) ? InteractiveCell(gfx) : Plain);
        return new Map { MapID = MapId, MapWidth = Width, MapHeight = Height, X = 3, Y = -5, MapData = data.ToString() };
    }

    private static void WriteLang(string folder, string file, string family, string body) =>
        File.WriteAllText(Path.Combine(folder, file), "<?xml version='1.0' encoding='utf-8'?>\n<BotLang famille=\"" + family
            + "\" langue=\"fr\" version=\"1\" source=\"" + family + "_fr_1.swf\">\n" + body + "\n</BotLang>\n", new UTF8Encoding(false));

    private static void WriteDocument(string folder, string name, string type, string title, params string[] pages)
    {
        var xml = new StringBuilder("<?xml version='1.0' encoding='utf-8'?>\n<BotDoc id=\"" + name.Split('_')[0] + "\" date=\"" + name.Split('_')[1]
            + "\" type=\"" + type + "\" style=\"1\" source=\"test\">\n<titre>" + title + "</titre>\n<soustitre>Tome d'essai</soustitre>\n<auteur>Personne</auteur>\n");
        foreach (string page in pages) xml.Append("<page>" + System.Security.SecurityElement.Escape(page) + "</page>\n");
        xml.Append("<chapitre titre=\"Début fictif\" page=\"0\" droite=\"false\" titreVisible=\"true\" />\n<chapitre titre=\"Hors du livre\" page=\"9\" droite=\"true\" titreVisible=\"false\" />\n</BotDoc>\n");
        File.WriteAllText(Path.Combine(folder, name + ".xml"), xml.ToString(), new UTF8Encoding(false));
    }

    private static void Run()
    {
        string previous = Environment.CurrentDirectory;
        string folder = Path.Combine(TestPaths.Work, "bot-interactives-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); Environment.CurrentDirectory = folder;
        string lang = Path.Combine(folder, "lang"), docs = Path.Combine(folder, "docs");
        Directory.CreateDirectory(lang); Directory.CreateDirectory(docs);
        WriteLang(lang, "interactiveobjects.xml", "interactiveobjects", "<interactif id=\"3\" nom=\"Arbre fictif\" type=\"1\" competences=\"6,7\" />\n<gfx id=\"7500\" interactif=\"3\" />");
        WriteLang(lang, "skills.xml", "skills", "<competence id=\"6\" nom=\"Couper\" metier=\"2\" interactif=\"3\" condition=\"J?V:-\" />\n"
            + "<competence id=\"7\" nom=\"Scier fictif\" metier=\"2\" interactif=\"3\" condition=\"J?V:-\" />\n<competence id=\"104\" nom=\"Ouvrir\" metier=\"1\" />\n"
            + "<competence id=\"114\" nom=\"Utiliser\" metier=\"1\" />\n<competence id=\"44\" nom=\"Sauvegarder\" metier=\"1\" />\n<competence id=\"84\" nom=\"Entrer\" metier=\"1\" />\n"
            + "<competence id=\"175\" nom=\"Accéder\" metier=\"1\" condition=\"L?V:-\" />");
        WriteDocument(docs, "3_0102030405", "book", "Carnet d'essai", "<p class='t1'>Premier titre</p><p class='n'>Texte de la <b>première</b> page &amp; suite<br/>ligne</p><img src='##swf,1##'/>",
            "<p class='n'>Seconde page fictive</p>");
        WriteDocument(docs, "3_0001010101", "book", "Ancienne version", "<p>Ancienne</p>");
        WriteDocument(docs, "5_0102030405", "roadsignleft", "Pancarte fictive", "<p class='n'><b>Vers le nord</b></p>", "<p>Page ignorée</p>");
        Check(LangData.Load(lang) == 2, "Synthetic lang files not loaded: " + string.Join(" / ", LangData.LoadWarnings));

        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            MessagesReception.Init();
            Check(GameActionRouter.IsRegistered(501), "GA;501 handler was not discovered");
            Check(!Zaaps.Z.ContainsKey(MapId), "The synthetic map must not be in BotZaaps");
            Map.AllBotMaps[MapId] = SyntheticMap();
            Map.AllBotMaps[OtherMapId] = new Map { MapID = OtherMapId, MapWidth = 3, MapHeight = 4, X = 7, Y = 2, MapData = string.Concat(Enumerable.Repeat(Plain, 18)) };
            Documents();
            using (var account = new Accounts(new AccountConfig("synthetic-interactives", "synthetic", "loopback")))
            {
                Task<Socket> accept = listener.AcceptSocketAsync();
                Complete(account.Connexion.ConnectToServer(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port)); Complete(accept);
                using (Socket peer = accept.Result)
                {
                    peer.ReceiveTimeout = 6000;
                    account.Game.character.SetPerso_Data(42, "Personnage de test", 25, 0, 8);
                    Feed(account, "GDM|" + MapId + "|date|key"); Expect(peer, "GI", "GDM did not request GI");
                    Feed(account, "GM|+" + Self + SelfGm);
                    account.AccountStates = AccountStates.CONNECTED_INACTIVE;
                    var clock = new ManualClock();
                    InteractiveActions actions = account.Game.Interactions.Interactive;
                    actions.Clock = clock;
                    account.Game.Manager.Mouvements.Clock = clock;
                    actions.Document.DocumentsPath = docs;
                    Check(account.Game.character.Cell?.CellID == Self, "Self GM did not place the character");

                    Model(account);
                    Menus(account);
                    UseProtocol(account, peer, clock);
                    HarvestService(account, peer, clock);
                    Walking(account, peer, clock);
                    ZaapByGfx(account, peer, clock);
                    Malformed(account, peer);

                    using (var form = new GameClientFullform(account))
                    {
                        form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-4000, -4000);
                        form.ShowInTaskbar = false; form.Show(); Application.DoEvents();
                        PanelHost host = form.Panels;
                        var view = form.GetType().GetField("mapControl", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form) as MapControl;
                        Check(host != null && view != null && host.Get<ZaapiPanel>() != null && host.Get<KeyCodePanel>() != null && host.Get<DocumentPanel>() != null,
                            "Zaapi, code or document panel is not registered in the drawer");
                        NoPacket(peer, "Opening the game window sent a packet");

                        MapClicks(account, peer, clock, view);
                        ZaapiWindow(account, peer, clock, host);
                        CodeWindow(account, peer, host);
                        DocumentWindow(account, peer, host);

                        form.Close(); Application.DoEvents();
                    }
                    NoPacket(peer, "Unexpected packet at the end of the test");
                    Check(!sent.Any(packet => packet.StartsWith("GA034", StringComparison.Ordinal)), "GA034 was sent: " + string.Join(",", sent));
                    Check(!sent.Any(packet => packet.StartsWith("WU", StringComparison.Ordinal)), "WU (zaap) was sent for a zaapi: " + string.Join(",", sent));
                }
            }
        }
        finally { listener.Stop(); Environment.CurrentDirectory = previous; }
    }

    /// <summary>Objets lus dans la carte : textes du client, repli par gfx (zaap, zaapi, coffre, porte, enclos), GDF.</summary>
    private static void Model(Accounts account)
    {
        Map map = account.Game.Map;
        Check(map.Interactives.Count == 7, "Interactive cells were not decoded: " + map.Interactives.Count);
        Interactives tree = map.Interactives[Tree];
        Check(tree.gfx == 7500 && tree.ClientType == 1 && tree.Type == InteractiveType.Harvest && tree.Name == "Arbre fictif"
            && tree.Skills.SequenceEqual(new short[] { 6, 7 }) && tree.IsUsable && tree.State == InteractiveState.Unknown, "Tree read from the client texts differs");
        Interactives zaap = map.Interactives[Zaap];
        Check(zaap.gfx == 7000 && zaap.IsZaap && !zaap.IsZaapi && zaap.ClientType == 3 && zaap.ClientSkills.SequenceEqual(new short[] { 114, 44 }) && zaap.IsUsable,
            "Zaap gfx 7000 is not recognised without data");
        Interactives zaapi = map.Interactives[Zaapi];
        Check(zaapi.IsZaapi && zaapi.ClientType == 10 && zaapi.Skills.SequenceEqual(new short[] { 157 }), "Zaapi gfx 7030 is not recognised");
        Check(map.Interactives[Chest].ClientType == 6 && map.Interactives[Chest].Skills.SequenceEqual(new short[] { 104, 105 })
            && map.Interactives[Door].ClientType == 5 && map.Interactives[Door].Skills.SequenceEqual(new short[] { 84 })
            && map.Interactives[Park].ClientType == 13 && map.Interactives[Park].Skills.SequenceEqual(new short[] { 175 }), "Chest, door or park fallback differs: " + string.Join(" ", new[] { Chest, Door, Park }.Select(c => c + ":" + map.Interactives[c].gfx + "/" + map.Interactives[c].ClientType + "/" + string.Join(",", map.Interactives[c].Skills))));
        Check(InteractiveGfx.FallbackType(1234) == 0 && InteractiveGfx.FallbackSkills(1234).Length == 0 && InteractiveGfx.FallbackType(7352) == 6
            && InteractiveGfx.FallbackSkills(7352).SequenceEqual(new short[] { 153 }), "Unknown gfx or trash fallback differs");

        // GDF : tous les triplets ; image 3 = vide (non utilisable), puis image 1 = pleine ; un GDF sans 3e champ garde l'état utilisable.
        Feed(account, "GDF|" + Tree + ";3;0|" + Zaap + ";1;1");
        Check(tree.Frame == 3 && tree.State == InteractiveState.Empty && !tree.IsUsable && zaap.State == InteractiveState.Full, "GDF triplets were not applied");
        Feed(account, "GDF|" + Tree + ";1;1");
        Check(tree.State == InteractiveState.Full && tree.IsUsable, "GDF did not restore the tree");
        Feed(account, "GDF|" + Door + ";2");
        Check(map.Interactives[Door].State == InteractiveState.InUse && map.Interactives[Door].IsUsable, "A GDF without the interactive field changed IsUsable");
    }

    /// <summary>Menus des compétences : critère « J?V:- » (métier de l'outil équipé), zaap, porte, enclos (Rp).</summary>
    private static void Menus(Accounts account)
    {
        Map map = account.Game.Map;
        IReadOnlyList<MenuEntry> entries = InteractiveMenuProvider.Entries(map.Interactives[Tree], account.Game);
        Check(entries.Select(e => e.Texte).SequenceEqual(new[] { "Couper", "Scier fictif" }) && entries.All(e => !e.Activé),
            "Without a job tool, both skills must be listed and disabled: " + string.Join("|", entries.Select(e => e.Texte + ":" + e.Activé)));
        Feed(account, "JS|2;6~1~1~0~3000");
        Feed(account, "OT2");
        entries = InteractiveMenuProvider.Entries(map.Interactives[Tree], account.Game);
        Check(entries.Count == 2 && entries[0].Activé && !entries[1].Activé && InteractiveMenuProvider.ShortcutEntry(entries) == null,
            "Tree menu with the job tool differs (Couper on, Scier off, no Shift shortcut for a resource)");
        Check(account.Game.Manager.Harvest.SkillsFor(map.Interactives[Tree]).SequenceEqual(new short[] { 6 }), "Harvest skills ignore the JS job skills");

        entries = InteractiveMenuProvider.Entries(map.Interactives[Zaap], account.Game);
        Check(entries.Select(e => e.Texte).SequenceEqual(new[] { "Utiliser", "Sauvegarder" }) && entries.All(e => e.Activé)
            && InteractiveMenuProvider.ShortcutEntry(entries)?.Texte == "Utiliser", "Zaap menu differs");
        entries = InteractiveMenuProvider.Entries(map.Interactives[Door], account.Game);
        Check(entries.Select(e => e.Texte).SequenceEqual(new[] { "Entrer" }) && InteractiveMenuProvider.ShortcutEntry(entries) != null, "Door menu differs");
        Check(InteractiveMenuProvider.HasMenu(map.Interactives[Chest]) && InteractiveMenuProvider.HasMenu(map.Interactives[Park]), "Chest or park has no menu");

        // Enclos : sans Rp, « Accéder » est grisé (L faux) ; enclos public (propriétaire -1) : ligne d'information et entrée active.
        entries = InteractiveMenuProvider.Entries(map.Interactives[Park], account.Game);
        Check(entries.Count == 1 && !entries[0].Activé, "Park entry must be disabled before Rp");
        Check(!MountParkInfo.TryParse("x;1", out _), "A malformed Rp was accepted");
        Check(MountParkInfo.TryParse("-1;0;5;2;;", out MountParkInfo park) && park.IsPublic, "Rp parse differs");
        account.Game.Interactions.Interactive.SetMountPark(park);
        entries = InteractiveMenuProvider.Entries(map.Interactives[Park], account.Game);
        Check(entries.Count == 2 && entries[0].Texte == "Enclos public" && entries[0].Action == null && entries[1].Texte == "Accéder" && entries[1].Activé,
            "Public park menu differs: " + string.Join("|", entries.Select(e => e.Texte + ":" + e.Activé)));
        Check(MountParkInfo.TryParse("0;150000;6;3;;", out MountParkInfo forSale) && forSale.HasNoOwner
            && forSale.Describe() == "Enclos à vendre : 150000 kamas, 6 places, 3 objets", "Park for sale description differs");
    }

    /// <summary>GA500, anti-spam, action 501 (état « en cours », GATHERING), GKK à la fin de la durée, IQ et IO.</summary>
    private static void UseProtocol(Accounts account, Socket peer, ManualClock clock)
    {
        InteractiveActions actions = account.Game.Interactions.Interactive;
        Interactives tree = account.Game.Map.Interactives[Tree];
        var started = new List<InteractiveActionInfo>(); var finished = new List<InteractiveActionInfo>();
        var quantities = new List<string>(); var objects = new List<string>();
        actions.ActionStarted += started.Add; actions.ActionFinished += finished.Add;
        actions.QuantityReceived += (sprite, quantity) => quantities.Add(sprite + ":" + quantity);
        actions.ObjectResultReceived += (sprite, success, template) => objects.Add(sprite + ":" + success + ":" + template);

        Check(!Complete(actions.UseAsync(Tree, 104)).Sent, "A skill of another object was accepted");
        NoPacket(peer, "A refused skill sent a packet");
        InteractionResult result = Complete(actions.UseAsync(Tree, 6));
        Check(result.Sent, "Use(tree, 6) refused: " + result.Message);
        Expect(peer, "GA500" + Tree + ";6", "Use does not send GA500<cell>;<skill>");
        clock.Advance(799);
        Check(!Complete(actions.UseAsync(Tree, 6)).Sent, "A second use within 800 ms was accepted");
        NoPacket(peer, "The anti-spam let a second GA500 through");

        Feed(account, "GDF|" + Tree + ";2;0");
        Feed(account, "GA5;501;42;" + Tree + ",2000");
        InteractiveActionInfo current = actions.Current;
        Check(current != null && current.IsSelf && current.GameActionId == "5" && current.CellId == Tree && current.DurationMs == 2000 && current.SkillId == 6
            && started.Count == 1 && account.AccountStates == AccountStates.GATHERING && tree.State == InteractiveState.InUse && tree.Frame == 2,
            "GA5;501 did not start the gathering (state, frame 2, skill)");
        Check(!Complete(account.Game.Interactions.Zaapi.OpenAsync(Zaapi)).Sent, "Another window could open during the gathering");
        clock.Advance(1999);
        NoPacket(peer, "GKK5 was sent before the server duration");
        clock.Advance(1);
        Expect(peer, "GKK5", "GKK5 was not sent at the end of the duration");
        PumpUntil(() => finished.Count == 1);
        Check(actions.Current == null && account.AccountStates == AccountStates.CONNECTED_INACTIVE, "The gathering state was not released after GKK");
        Feed(account, "GDF|" + Tree + ";3;0");
        Feed(account, "IQ42|3");
        Feed(account, "IO42|+289");
        Feed(account, "IO42|-");
        Check(actions.LastQuantity == 3 && quantities.SequenceEqual(new[] { "42:3" }) && objects.SequenceEqual(new[] { "42:True:289", "42:False:0" })
            && !tree.IsUsable, "IQ/IO results differ: " + string.Join(",", objects));

        // Action 501 d'un autre joueur : événement seulement, jamais de GKK.
        Feed(account, "GA6;501;77;" + FarTree + ",1000");
        Check(started.Count == 2 && !started[1].IsSelf && started[1].ActorId == 77 && account.Game.Map.Interactives[FarTree].State == InteractiveState.InUse,
            "Another player's 501 was not reported");
        clock.Advance(5000);
        NoPacket(peer, "A GKK was sent for another player's action");
        Feed(account, "GDF|" + Tree + ";1;1|" + FarTree + ";1;1");
    }

    /// <summary>Service de récolte : ressource la plus proche avec une compétence du personnage, issue RECOLTÉ par IQ.</summary>
    private static void HarvestService(Accounts account, Socket peer, ManualClock clock)
    {
        Harvest harvest = account.Game.Manager.Harvest;
        var outcomes = new List<string>(); var starts = new List<short>();
        harvest.Finished += (outcome, cell, quantity) => outcomes.Add(outcome + ":" + cell + ":" + quantity);
        harvest.Started += starts.Add;
        IReadOnlyList<HarvestOption> options = harvest.Harvestables();
        Check(options.Count == 2 && options[0].Interactive.Cell.CellID == Tree && options[0].Skill == 6 && options[0].JobId == 2 && options[1].Interactive.Cell.CellID == FarTree,
            "Harvestable resources differ");
        Check(harvest.Harvestables(new short[] { 7 }).Count == 0, "A skill the character lacks was offered");
        clock.Advance(1000);
        InteractionResult result = Complete(harvest.HarvestNearestAsync());
        Check(result.Sent && harvest.Target?.Cell.CellID == Tree, "HarvestNearest refused: " + result.Message);
        Expect(peer, "GA500" + Tree + ";6", "Harvest did not send GA500 on the nearest resource");
        Feed(account, "GA8;501;42;" + Tree + ",1500");
        clock.Advance(1500);
        Expect(peer, "GKK8", "Harvest did not acknowledge with GKK8");
        Feed(account, "IQ42|2");
        Check(starts.SequenceEqual(new short[] { Tree }) && outcomes.SequenceEqual(new[] { "RECOLTÉ:" + Tree + ":2" }) && harvest.Target == null,
            "Harvest outcome differs: " + string.Join(",", outcomes));
        PumpUntil(() => account.AccountStates == AccountStates.CONNECTED_INACTIVE);
        Feed(account, "GDF|" + Tree + ";1;1");
    }

    /// <summary>Marche jusqu'à un objet hors de portée puis GA500 ; demande mise en file pendant une marche et rejouée à l'arrivée.</summary>
    private static void Walking(Accounts account, Socket peer, ManualClock clock)
    {
        Map map = account.Game.Map;
        Mouvement movement = account.Game.Manager.Mouvements;
        InteractiveActions actions = account.Game.Interactions.Interactive;
        clock.Advance(1000);
        InteractionResult result = Complete(actions.UseAsync(Chest, 104));
        Check(result.Sent && actions.WaitingUse?.Item1 == Chest, "Use of a far chest did not start a walk: " + result.Message);
        string walk = Read(peer);
        Check(walk.StartsWith("GA001", StringComparison.Ordinal), "Use of a far chest did not send GA001: " + walk);
        List<Cell> path = PathfinderUtils.DecodeServerPath(map, "a" + Ch((Self & 4032) >> 6) + Ch(Self & 63) + walk.Substring(5));
        Cell arrival = path[path.Count - 1];
        Check(arrival.GetDistanceBetweenCells(map.MapCells[Chest]) == 1 && !path.Contains(map.MapCells[Chest]), "The walk does not stop next to the chest");
        Feed(account, "GA3;1;42;a" + Ch((Self & 4032) >> 6) + Ch(Self & 63) + walk.Substring(5));
        NoPacket(peer, "GA500 left before the arrival");
        clock.Advance(AnimDuration.Compute(path, MoveProfile.Player()).Total);
        Expect(peer, "GKK3", "The walk was not acknowledged");
        Expect(peer, "GA500" + Chest + ";104", "GA500 was not sent at the arrival next to the chest");
        Check(actions.WaitingUse == null && account.Game.character.Cell == arrival, "The queued use was not consumed");

        // Demande pendant une marche déjà commencée : en file, envoyée après le GKK de cette marche.
        clock.Advance(1000);
        short start = arrival.CellID;
        Check(Complete(movement.MoveToAsync(map.MapCells[Self])) == MoveResults.EXIT, "Move back to the start refused");
        walk = Read(peer);
        string encoded = "a" + Ch((start & 4032) >> 6) + Ch(start & 63) + walk.Substring(5);
        path = PathfinderUtils.DecodeServerPath(map, encoded);
        Feed(account, "GA4;1;42;" + encoded);
        Check(account.IsMoving(), "The character is not walking");
        result = Complete(actions.UseAsync(Tree, 6));
        Check(result.Sent && actions.WaitingUse?.Item1 == Tree, "A use during a walk was not queued: " + result.Message);
        NoPacket(peer, "A queued use was sent during the walk");
        clock.Advance(AnimDuration.Compute(path, MoveProfile.Player()).Total);
        Expect(peer, "GKK4", "The second walk was not acknowledged");
        Expect(peer, "GA500" + Tree + ";6", "The queued use was not replayed after the walk");
        Feed(account, "GA;0");
    }

    /// <summary>Zaap reconnu par son gfx (7000) sans BotZaaps : GA500&lt;cellule&gt;;114.</summary>
    private static void ZaapByGfx(Accounts account, Socket peer, ManualClock clock)
    {
        ZaapDialog zaap = account.Game.Interactions.Zaap;
        Check(zaap.IsZaapCell(Zaap) && !zaap.IsZaapCell(Zaapi) && !zaap.IsZaapCell(Tree), "Zaap cell recognition by gfx differs");
        InteractionResult result = Complete(zaap.OpenAsync(Zaap));
        Check(result.Sent, "Zaap.OpenAsync refused: " + result.Message);
        Expect(peer, "GA500" + Zaap + ";114", "The zaap gfx 7000 is not used with GA500<cell>;114");
        clock.Advance(1000);
        result = Complete(account.Game.Interactions.Interactive.UseAsync(Zaap, 114));
        Check(result.Sent, "Use(zaap, 114) refused: " + result.Message);
        Expect(peer, "GA500" + Zaap + ";114", "Use(zaap, 114) differs");
    }

    /// <summary>Paquets malformés : journalisés, jamais d'exception ni de paquet envoyé.</summary>
    private static void Malformed(Accounts account, Socket peer)
    {
        InteractiveActions actions = account.Game.Interactions.Interactive;
        foreach (string packet in new[] { "GA9;501;42;abc", "GA9;501;42", "GA;501;42;" + Tree + ",100", "IQ", "IQabc|x", "IO42|*", "IOx|+3", "IO42|+abc",
            "KCK", "KCKx|y", "Wc", "Wcabc|1;2", "dCK", "GDF|abc;;|;", "Wu", "Wv", "KV", "KKE", "dV" })
            Feed(account, packet);
        Check(actions.Current == null && !actions.Code.IsOpen && !actions.Document.IsOpen && !account.Game.Interactions.Zaapi.IsOpen,
            "A malformed packet opened a window");
        Check(actions.Code.LastMessage.Length > 0 || actions.LastMessage.Length > 0, "Malformed packets were not reported");
        NoPacket(peer, "A malformed packet produced an answer");
        account.AccountStates = AccountStates.CONNECTED_INACTIVE;
    }

    /// <summary>Clics sur la carte : menu de l'objet (clic gauche), entrée du menu, Maj + clic sur le zaap.</summary>
    private static void MapClicks(Accounts account, Socket peer, ManualClock clock, MapControl view)
    {
        clock.Advance(1000);
        Complete(view.Router.RouteAsync(Tree, MouseButtons.Left));
        ContextMenuStrip menu = InteractiveMenuProvider.LastMenu(view);
        Check(menu != null && Texts(menu).SequenceEqual(new[] { "Couper", "Scier fictif" }) && Item(menu, "Couper").Enabled && !Item(menu, "Scier fictif").Enabled,
            "Left click on the tree did not open its skills menu");
        NoPacket(peer, "Opening the tree menu sent a packet or moved the character");
        Item(menu, "Couper").PerformClick();
        Expect(peer, "GA500" + Tree + ";6", "The « Couper » entry does not send GA500");
        clock.Advance(1000);
        Complete(view.Router.RouteAsync(Zaap, MouseButtons.Left, Keys.Shift));
        Expect(peer, "GA500" + Zaap + ";114", "Shift + click on the zaap does not use it");
        clock.Advance(1000);
    }

    /// <summary>Zaapi : GA500;157 (jamais WU), Wc → volet, Wu&lt;carte&gt;, Wu (erreur), Wv ferme ; fermeture par le volet (Wv).</summary>
    private static void ZaapiWindow(Accounts account, Socket peer, ManualClock clock, PanelHost host)
    {
        ZaapiDialog zaapi = account.Game.Interactions.Zaapi;
        ZaapiPanel panel = host.Get<ZaapiPanel>();
        Check(zaapi.IsZaapiCell(Zaapi) && !zaapi.IsZaapiCell(Zaap), "Zaapi recognition by gfx differs");
        Check(Complete(zaapi.OpenAsync(Zaapi)).Sent, "Zaapi.OpenAsync refused");
        Expect(peer, "GA500" + Zaapi + ";157", "The zaapi is not used with GA500<cell>;157");
        Feed(account, "Wc" + MapId + "|" + MapId + ";0|" + OtherMapId + ";20|");
        PumpUntil(() => host.Current == panel);
        Check(zaapi.IsOpen && zaapi.CurrentMapId == MapId && zaapi.Destinations.Count == 2 && zaapi.Destinations.Single(d => d.MapId == OtherMapId).Cost == 20
            && zaapi.Destinations.Single(d => d.MapId == MapId).IsCurrent && zaapi.Destinations.Single(d => d.MapId == OtherMapId).Coordinates == "[7,2]"
            && account.AccountStates == AccountStates.ZAAP, "Wc was not read like Subway.onCreate");
        account.Game.character.Kamas = 15;
        Check(!Complete(zaapi.TeleportAsync(OtherMapId)).Sent && !Complete(zaapi.TeleportAsync(MapId)).Sent && !Complete(zaapi.TeleportAsync(123)).Sent,
            "Unaffordable, current or unknown destinations were accepted");
        NoPacket(peer, "A refused zaapi destination sent a packet");
        account.Game.character.Kamas = 100;
        Check(Complete(zaapi.TeleportAsync(OtherMapId)).Sent, "Zaapi teleport refused");
        Expect(peer, "Wu" + OtherMapId, "The zaapi destination is not sent as Wu<map>");
        Feed(account, "Wu");
        Check(zaapi.IsOpen && zaapi.LastMessage.Length > 0, "Wu (error) closed the window or was not reported");
        Feed(account, "Wv");
        PumpUntil(() => host.Current != panel);
        Check(!zaapi.IsOpen && account.AccountStates == AccountStates.CONNECTED_INACTIVE, "Wv did not close the zaapi window");

        clock.Advance(1000);
        Check(Complete(zaapi.OpenAsync(Zaapi)).Sent, "Second zaapi use refused");
        Expect(peer, "GA500" + Zaapi + ";157", "Second zaapi use differs");
        Feed(account, "Wc" + MapId + "|" + OtherMapId + ";10");
        PumpUntil(() => host.Current == panel);
        host.CloseCurrent();
        Expect(peer, "Wv", "Closing the zaapi panel does not send Wv");
        Check(host.Current == panel, "The zaapi panel closed before the server answer");
        Feed(account, "Wv");
        PumpUntil(() => host.Current != panel);
    }

    /// <summary>Code : KCK0|8 → volet, chiffres + Entrée → KK0|1234 (comme le client), KKE, KV ; changement : KK1|-.</summary>
    private static void CodeWindow(Accounts account, Socket peer, PanelHost host)
    {
        KeyCodeDialog code = account.Game.Interactions.Interactive.Code;
        KeyCodePanel panel = host.Get<KeyCodePanel>();
        Check(KeyCodeDialog.BuildCode("1234", 8) == "1234" && KeyCodeDialog.BuildCode("1234____", 8) == "1234" && KeyCodeDialog.BuildCode("12_4", 8) == "12_4"
            && KeyCodeDialog.BuildCode("", 8) == "-" && KeyCodeDialog.BuildCode("________", 8) == "-" && KeyCodeDialog.BuildCode("123456789", 8) == null
            && KeyCodeDialog.BuildCode("12a", 8) == null, "BuildCode differs from KeyCode.validate");
        Feed(account, "KCK0|8");
        PumpUntil(() => host.Current == panel);
        Check(code.IsOpen && code.ChangeType == 0 && code.SlotCount == 8 && account.AccountStates == AccountStates.DIALOG, "KCK0|8 did not open the code window");
        foreach (Keys key in new[] { Keys.D1, Keys.D2, Keys.NumPad3, Keys.D4 }) Check((bool)Call(panel, "HandleKey", key), "Digit key ignored: " + key);
        Check((string)Property(panel, "TypedCode") == "1234____", "Typed slots differ: " + Property(panel, "TypedCode"));
        Check((bool)Call(panel, "HandleKey", Keys.Enter), "Enter ignored");
        Expect(peer, "KK0|1234", "Validating the code does not send KK0|1234");
        Feed(account, "KKE");
        Check(code.LastCodeRefused && code.IsOpen, "KKE was not reported");
        Feed(account, "KV");
        PumpUntil(() => host.Current != panel);
        Check(!code.IsOpen && account.AccountStates == AccountStates.CONNECTED_INACTIVE, "KV did not close the code window");

        Feed(account, "KCK1|8");
        PumpUntil(() => host.Current == panel);
        Check(code.IsChange, "KCK1 is not a code change");
        Check(Complete(code.NoCodeAsync()).Sent, "No code refused");
        Expect(peer, "KK1|-", "« Aucun code » does not send KK1|-");
        Feed(account, "KKK");
        Check(!code.LastCodeRefused && code.LastMessage == "Code modifié.", "KKK (code changed) was not reported");
        host.CloseCurrent();
        Expect(peer, "KV", "Closing the code panel does not send KV");
        Feed(account, "KV");
        PumpUntil(() => host.Current != panel);
    }

    /// <summary>Documents : dCK3 → volet (dernière version), pages, dV ; clé invalide jamais utilisée comme chemin.</summary>
    private static void DocumentWindow(Accounts account, Socket peer, PanelHost host)
    {
        DocumentDialog dialog = account.Game.Interactions.Interactive.Document;
        DocumentPanel panel = host.Get<DocumentPanel>();
        Feed(account, "dCK3");
        PumpUntil(() => host.Current == panel);
        Check(dialog.IsOpen && dialog.Current?.Title == "Carnet d'essai" && dialog.Current.Version == "0102030405" && dialog.Current.Pages.Count == 2
            && account.AccountStates == AccountStates.DIALOG, "dCK3 did not open the latest version of document 3");
        string text = (string)Property(panel, "PageText");
        Check(text.Contains("Début fictif") && text.Contains("Premier titre") && text.Contains("première page & suite") && text.Contains(DocumentText.ImagePlaceholder),
            "The first page text differs: " + text);
        Call(panel, "ShowPage", 1);
        Check((int)Property(panel, "PageIndex") == 1 && ((string)Property(panel, "PageText")).Contains("Seconde page fictive"), "The second page is not shown");
        Check(Complete(dialog.LeaveAsync()).Sent, "Document leave refused");
        Expect(peer, "dV", "Closing a document does not send dV");
        Feed(account, "dV");
        PumpUntil(() => host.Current != panel);
        Check(!dialog.IsOpen && account.AccountStates == AccountStates.CONNECTED_INACTIVE, "dV did not close the document");

        Feed(account, "dCK5_0102030405");
        PumpUntil(() => host.Current == panel);
        Check(dialog.Current?.Type == "roadsignleft" && !dialog.Current.IsBook && dialog.Current.Pages.Count == 1, "The road sign document differs");
        Feed(account, "dV");
        PumpUntil(() => host.Current != panel);

        Feed(account, "dCK..\\..\\secret");
        PumpUntil(() => host.Current == panel);
        Check(dialog.IsOpen && dialog.Current == null && dialog.LastMessage.StartsWith("Identifiant de document invalide", StringComparison.Ordinal),
            "An invalid document key was not refused");
        host.CloseCurrent();
        Expect(peer, "dV", "Closing an unknown document does not send dV");
        Feed(account, "dV");
        PumpUntil(() => host.Current != panel);
    }

    /// <summary>Lecture des documents exportés et HTML simplifié du client.</summary>
    private static void Documents()
    {
        IReadOnlyList<DocumentParagraph> paragraphs = DocumentText.Parse("<p class='t1'>Titre</p><p>Un <b>gras</b> &amp; <i>it</i><br/>suite<a href='asfunction:onHref,1'>lien</a></p><p></p><p><img src='x'/></p><u>fin");
        Check(paragraphs.Count == 4 && paragraphs[0].IsTitle && paragraphs[1].Runs.Any(r => r.Bold && r.Text == "gras") && paragraphs[1].Runs.Any(r => r.Italic)
            && paragraphs[1].Runs.Any(r => r.IsLink && r.Text == "lien") && paragraphs[1].PlainText.Contains("& ") && paragraphs[2].Runs[0].IsImage
            && paragraphs[3].Runs[0].Underline, "Simplified document HTML differs: " + string.Join(" / ", paragraphs.Select(p => p.Class + ":" + p.PlainText)));
        string docs = Path.Combine(Environment.CurrentDirectory, "docs");
        Check(DocumentDialog.Load(docs, "3_0001010101", out string message)?.Title == "Ancienne version" && message == null, "Exact document version not loaded");
        Check(DocumentDialog.Load(docs, "3_0909090909", out message)?.Version == "0102030405" && message != null, "Missing version did not fall back to the latest one");
        Check(DocumentDialog.Load(docs, "4", out message) == null && message.Contains("absent"), "A missing document was not reported");
        Check(DocumentDialog.Load(docs, "../3_0102030405", out message) == null && DocumentDialog.Load(docs, "3_01*", out _) == null, "A key with a path was accepted");
        BotDocument book = DocumentDialog.Load(docs, "3", out _);
        Check(book.IsBook && book.Chapters.Count == 2 && book.PageParagraphs(0)[0].Class == "chapter" && book.PageParagraphs(5).Count == 0, "Book chapters differ");
    }
}
