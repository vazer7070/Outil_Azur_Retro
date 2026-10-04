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
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Interfaces;
using Tool_BotProtocol.Game.NPC;

// Socle de l'interface de jeu : tiroir des volets (pile, bascule, un seul volet modal), registre des menus d'acteurs,
// menu contextuel d'un acteur synthétique (clic droit, Ctrl + clic droit), routeur des clics, bandeau bas et boîtes
// de dialogue Retro. Serveur fictif local, fenêtre hors de l'écran, aucune capture d'écran.
internal static class BotPanelsSmoke
{
    private static readonly List<string> executed = new List<string>();
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
        try { Application.EnableVisualStyles(); Run(); Check(uiErrors.Count == 0, "Interface errors: " + string.Join(" / ", uiErrors.Select(e => e.GetType().Name + " " + e.Message))); Console.WriteLine("OK: tiroir des volets (pile, bascule, modal unique), registre et menu contextuel des acteurs, routeur des clics, bandeau bas, boîtes Retro"); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void PumpUntil(Func<bool> done)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(6);
        while (!done()) { if (DateTime.UtcNow > end) throw new TimeoutException("Panels loopback timed out"); Application.DoEvents(); Thread.Sleep(5); }
        Application.DoEvents();
    }
    private static void Complete(Task task) { PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
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
    private static IEnumerable<Control> All(Control value) { yield return value; foreach (Control child in value.Controls) foreach (var nested in All(child)) yield return nested; }
    private static string[] Texts(ToolStripItemCollection items) => items.Cast<ToolStripItem>().Select(item => item is ToolStripSeparator ? "-" : item.Text).ToArray();
    private static Map SyntheticMap(int id) => new Map { MapID = id, MapWidth = 3, MapHeight = 4, X = 2, Y = -4, MapData = string.Concat(Enumerable.Repeat("HhGaeaaaaa", 18)) };

    /// <summary>Fournisseur de test : n'accepte qu'une famille d'acteurs ; la première entrée affiche un raccourci.</summary>
    private sealed class KindProvider : IActorMenuProvider
    {
        private readonly MenuActorKind kind;
        private readonly string[] texts;
        public KindProvider(MenuActorKind kind, params string[] texts) { this.kind = kind; this.texts = texts; }
        public bool Handles(Entites a) => ActorClassifier.Of(a) == kind;
        public IEnumerable<MenuEntry> Entries(Entites a, GameClass g) => texts.Select((text, index) => new MenuEntry(text, context =>
        {
            executed.Add(text + ":" + context.Actor.id + (context.Shift ? ":maj" : string.Empty));
            return Task.FromResult(text + " exécuté");
        }) { Raccourci = index == 0 ? "Maj + clic" : null });
    }

    private sealed class BrokenProvider : IActorMenuProvider
    {
        public bool Handles(Entites a) => true;
        public IEnumerable<MenuEntry> Entries(Entites a, GameClass g) { throw new InvalidOperationException("fournisseur cassé"); }
    }

    /// <summary>Volet minimal d'un autre lot : il demande lui-même à quitter le tiroir.</summary>
    private sealed class ProbePanel : GamePanel
    {
        public int Refreshes;
        public override string Title => "Sonde";
        protected override Control CreateView() => new Label { Text = "Volet de test", Dock = DockStyle.Fill };
        public override void RefreshView() { Refreshes++; }
        public void Finish() => RaiseClosed();
    }

    private static void Run()
    {
        string previous = Environment.CurrentDirectory;
        string folder = Path.Combine(TestPaths.Work, "bot-panels"); Directory.CreateDirectory(folder); Environment.CurrentDirectory = folder;
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            MessagesReception.Init();
            Map.AllBotMaps[900091] = SyntheticMap(900091);
            PNJ.AllPNJ[101] = new PNJ(101, 101, null) { Name = "Aubergiste fictif" };
            using (var account = new Accounts(new AccountConfig("synthetic-panels", "synthetic", "loopback")))
            {
                Task<Socket> accept = listener.AcceptSocketAsync();
                Complete(account.Connexion.ConnectToServer(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port)); Complete(accept);
                using (Socket peer = accept.Result)
                {
                    peer.ReceiveTimeout = 6000;
                    account.Game.character.SetPerso_Data(42, "Personnage de test", 25, 0, 8);
                    Feed(account, "GDM|900091|date|key"); Check(Read(peer) == "GI", "GDM did not request GI");
                    Feed(account, "GM|+0;1;0;42;Personnage de test;1;100^100;0|+5;1;0;-8;101;-4;1^100;0|+5;1;0;77;Joueur fictif;1;10^100;0");
                    account.AccountStates = AccountStates.CONNECTED_INACTIVE;
                    Entites npc = account.Game.Map.Entites[-8], player = account.Game.Map.Entites[77];
                    Check(ActorClassifier.Of(npc) == MenuActorKind.Npc && ActorClassifier.Of(player) == MenuActorKind.Player, "Synthetic actors are misclassified");

                    // Registre : deux fournisseurs filtrés par famille, fusion dans l'ordre d'enregistrement.
                    var registry = new ActorMenuRegistry();
                    registry.Add(new KindProvider(MenuActorKind.Npc, "PNJ-A", "PNJ-B"));
                    registry.Add(new KindProvider(MenuActorKind.Player, "Joueur-A"));
                    registry.Add(new KindProvider(MenuActorKind.Npc, "PNJ-C"));
                    Check(string.Join("|", registry.EntriesFor(npc, account.Game).Select(e => e.EstSéparateur ? "-" : e.Texte)) == "PNJ-A|PNJ-B|-|PNJ-C",
                        "NPC entries are not merged in registration order with a separator between providers");
                    Check(string.Join("|", registry.EntriesFor(player, account.Game).Select(e => e.Texte)) == "Joueur-A", "Player entries ignore Handles");
                    var errors = new List<string>();
                    registry.Add(new BrokenProvider());
                    Check(registry.EntriesFor(npc, account.Game, (p, e) => errors.Add(e.Message)).Count(e => !e.EstSéparateur) == 3 && errors.SequenceEqual(new[] { "fournisseur cassé" }),
                        "A failing provider broke the menu or was not reported");
                    Check(ActorMenuRegistry.Default.Providers.OfType<NpcMenuProvider>().Count() == 1, "Application providers are not discovered");

                    using (var form = new GameClientFullform(account))
                    {
                        form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-4000, -4000);
                        form.ShowInTaskbar = false; form.Show(); Application.DoEvents();
                        var host = form.Panels; var view = form.GetType().GetField("mapControl", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form) as MapControl;
                        Check(host != null && view != null && view.Router.Panels == host, "Panels host or map router missing");
                        Check(!host.Visible && host.Current == null && host.Registered.Count == 8, "Drawer is open at start or the original panels are missing");

                        // Tiroir : Show, Toggle, pile, un seul volet modal, fermeture demandée par le volet.
                        var shown = new List<string>(); host.PanelShown += (s, e) => shown.Add(e.Panel.Title);
                        var extra = new DialoguePanel();
                        host.Show(extra); Application.DoEvents();
                        Check(host.Visible && host.Current == extra && host.Stack.Count == 1 && extra.View.Visible && extra.View.Parent != null, "Show did not display the panel");
                        var stats = host.Get<StatsPanel>();
                        host.Show(stats); Application.DoEvents();
                        Check(host.Current == stats && host.Stack.SequenceEqual(new IGamePanel[] { extra, stats }) && !extra.View.Visible && stats.View.Visible, "Panels are not stacked");
                        host.Toggle(stats); Application.DoEvents();
                        Check(host.Visible && host.Current == extra && !host.IsOpen(stats), "Toggle did not close the displayed panel");
                        host.Toggle(stats); Application.DoEvents();
                        Check(host.Current == stats && host.Stack.Count == 2, "Toggle did not reopen the panel");
                        host.Show(host.Get<ZaapsPanel>()); Application.DoEvents();
                        Check(!host.IsOpen(extra) && host.Current == host.Get<ZaapsPanel>() && host.Stack.Count == 2, "Two modal panels are open at once");
                        Check(shown.SequenceEqual(new[] { "Dialogue", "Caractéristiques", "Caractéristiques", "Zaaps" }), "PanelShown events differ: " + string.Join(",", shown));
                        host.CloseAll(); Application.DoEvents();
                        Check(!host.Visible && host.Stack.Count == 0 && host.Current == null, "CloseAll left panels open");
                        host.Toggle(extra); Check(host.Current == extra && host.Visible, "Toggle did not open a closed panel");
                        host.Toggle(extra); Check(!host.Visible && host.Stack.Count == 0, "Toggle did not close a panel without server window");
                        NoPacket(peer, "Closing panels without a server window sent packets");
                        var probe = new ProbePanel();
                        host.Show(probe); Check(host.Current == probe && probe.Host == host && probe.Refreshes > 0, "A panel from another lot cannot be shown");
                        probe.Finish(); Application.DoEvents();
                        Check(!host.IsOpen(probe) && !host.Visible, "Closed event did not remove the panel");

                        // Clic droit sur un acteur synthétique : en-tête au nom de l'acteur puis entrées du registre.
                        view.Router.Registry = registry;
                        Complete(view.Router.RouteAsync(5, MouseButtons.Right));
                        ContextMenuStrip menu = view.Router.LastMenu;
                        Check(menu != null && menu.Items.Count == 5, "Right-click on an actor did not build its context menu");
                        Check(menu.Items[0].Text == "Aubergiste fictif" && (menu.Items[0].Tag as string) == ActorContextMenu.HeaderTag
                            && menu.Items[1].Text == "PNJ-A" && menu.Items[2].Text == "PNJ-B" && menu.Items[3] is ToolStripSeparator && menu.Items[4].Text == "PNJ-C",
                            "Context menu items differ: " + string.Join("|", Texts(menu.Items)));
                        Check(((ToolStripMenuItem)menu.Items[1]).ShortcutKeyDisplayString == "Maj + clic" && ((ToolStripMenuItem)menu.Items[1]).ShowShortcutKeys,
                            "Menu shortcut is not displayed");
                        NoPacket(peer, "Opening an actor menu sent a packet");
                        menu.Items[2].PerformClick(); PumpUntil(() => executed.Count == 1);
                        Check(executed[0] == "PNJ-B:-8", "Menu entry did not run for the clicked actor");

                        // Ctrl + clic droit : un sous-menu par acteur de la cellule, PNJ d'abord.
                        Complete(view.Router.RouteAsync(5, MouseButtons.Right, Keys.Control));
                        menu = view.Router.LastMenu;
                        Check(menu != null && string.Join("|", Texts(menu.Items)) == "Aubergiste fictif|Joueur fictif", "Ctrl + right-click does not list one sub-menu per actor");
                        var playerItems = ((ToolStripMenuItem)menu.Items[1]).DropDownItems;
                        Check(string.Join("|", Texts(playerItems)) == "Joueur fictif|Joueur-A", "Player sub-menu differs: " + string.Join("|", Texts(playerItems)));
                        ContextMenuStrip perActor = menu;
                        Complete(view.Router.RouteAsync(4, MouseButtons.Right));
                        Check(view.Router.LastMenu == perActor, "Right-click on an empty cell built a menu");
                        NoPacket(peer, "Right-click on an empty cell sent a packet");

                        // Menu des PNJ sans textes du client (repli) : Parler (DC, Maj + clic) et Acheter/Vendre (ER0).
                        view.Router.Registry = ActorMenuRegistry.Default;
                        Complete(view.Router.RouteAsync(5, MouseButtons.Right));
                        menu = view.Router.LastMenu;
                        Check(string.Join("|", Texts(menu.Items)) == "Aubergiste fictif|Parler|Acheter/Vendre", "NPC menu differs: " + string.Join("|", Texts(menu.Items)));
                        var talk = (ToolStripMenuItem)menu.Items[1];
                        Check(talk.Font.Bold && talk.ShortcutKeyDisplayString == "Maj + clic" && perActor.IsDisposed, "Default NPC entry is not marked or the previous menu leaked");
                        talk.PerformClick(); Check(Read(peer) == "DC-8", "Parler does not send DC<npc>");

                        // Volet de fenêtre serveur : CloseAll le garde, × envoie DV et le volet attend la réponse du serveur.
                        Feed(account, "DCK-8"); Application.DoEvents();
                        Check(host.Visible && host.Current is DialoguePanel && host.Stack.Count == 1, "Dialog panel did not open on DCK");
                        host.CloseAll(); Check(host.Current is DialoguePanel, "CloseAll closed a panel whose server window is open");
                        host.CloseCurrent(); Check(Read(peer) == "DV" && host.Current is DialoguePanel, "Closing the dialog panel does not send DV or closed before the server answer");
                        Feed(account, "DV"); Application.DoEvents();
                        Check(!host.Visible && host.Stack.Count == 0, "DV did not close the dialog panel");

                        // Clic gauche : cellule → MoveRequested (Maj transmis), acteur → ActorClicked avant l'action par défaut.
                        var moves = new List<string>(); var clicks = new List<string>();
                        MapClickHandler move = (s, e) => { moves.Add(e.CellId + (e.Shift ? ":maj" : string.Empty)); e.Handled = true; return Task.CompletedTask; };
                        MapClickHandler click = (s, e) => { clicks.Add(e.Actor.id + "/" + e.Actors.Count + (e.Ctrl ? ":ctrl" : string.Empty)); e.Handled = true; return Task.CompletedTask; };
                        view.Router.MoveRequested += move; view.Router.ActorClicked += click;
                        Complete(view.HandleCellActionAsync(3, Keys.Shift));
                        Complete(view.Router.RouteAsync(5, MouseButtons.Left, Keys.Control));
                        Check(moves.SequenceEqual(new[] { "3:maj" }) && clicks.SequenceEqual(new[] { "-8/2:ctrl" }), "Left clicks were not routed with their modifiers");
                        NoPacket(peer, "Handled clicks still sent packets");
                        view.Router.MoveRequested -= move; view.Router.ActorClicked -= click;

                        // Bandeau bas en trois emplacements.
                        Check(form.Hud.LeftSlot.Controls.Count == 1 && All(form.Hud.LeftSlot).OfType<RichTextBox>().Any(), "Chat is not in the left HUD slot");
                        Check(form.Hud[HudSlot.Center].Controls.Count == 1 && All(form.Hud.CenterSlot).OfType<ProgressBar>().Any(), "Life and XP are not in the centre HUD slot");
                        Check(All(form.Hud.RightSlot).Count(c => (c.Tag as string) == "client-icon" && c.Width == 33) == 9, "Banner icons are not in the right HUD slot");
                        using (var hud = new HudPanel())
                        {
                            var first = new Label(); var second = new Label();
                            hud.SetSlot(HudSlot.Left, first); hud.SetSlot(HudSlot.Left, second);
                            Check(first.IsDisposed && hud.LeftSlot.Controls.Count == 1 && hud.LeftSlot.Controls[0] == second && second.Dock == DockStyle.Fill, "SetSlot did not replace the slot content");
                        }

                        // Boîtes de dialogue Retro : Oui/Non, Oui/Non/Ignorer, information, appel hors du thread de l'interface.
                        Task<BotDialogResult> question = BotDialogs.AskYesNoAsync(form, "Invitation", "Rejoindre le groupe de Joueur fictif ?");
                        Application.DoEvents();
                        Check(BotDialogs.OpenDialogs.Count == 1, "Yes/no dialog is not shown");
                        Form box = BotDialogs.OpenDialogs[0];
                        Check(box.FormBorderStyle == FormBorderStyle.None && box.BackColor == Color.FromArgb(41, 38, 31), "Dialog does not use the Retro frame");
                        Check(string.Join("|", All(box).OfType<Button>().Select(b => b.Text).OrderBy(t => t)) == "Non|Oui", "Yes/no buttons differ");
                        All(box).OfType<Button>().First(b => b.Text == "Oui").PerformClick();
                        Complete(question); Check(question.Result == BotDialogResult.Yes && BotDialogs.OpenDialogs.Count == 0, "Oui did not answer Yes");
                        question = BotDialogs.AskYesNoIgnoreAsync(form, "Invitation", "Échanger avec Joueur fictif ?"); Application.DoEvents();
                        box = BotDialogs.OpenDialogs.Single();
                        Check(All(box).OfType<Button>().Count() == 3, "Yes/no/ignore dialog lacks a button");
                        All(box).OfType<Button>().First(b => b.Text == "Ignorer").PerformClick();
                        Complete(question); Check(question.Result == BotDialogResult.Ignore, "Ignorer did not answer Ignore");
                        Task<BotDialogResult> info = null;
                        Complete(Task.Run(() => { info = BotDialogs.InfoAsync(form, "Information", "Message reçu hors du thread de l'interface."); }));
                        PumpUntil(() => BotDialogs.OpenDialogs.Count == 1);
                        All(BotDialogs.OpenDialogs[0]).OfType<Button>().Single().PerformClick();
                        Complete(info); Check(info.Result == BotDialogResult.Ok, "Information dialog did not answer OK");

                        form.Close(); Application.DoEvents();
                        Check(host.IsDisposed && extra.View.IsDisposed && stats.View.IsDisposed && probe.View.IsDisposed, "Drawer and panels were not released with the form");
                    }
                }
            }
        }
        finally { listener.Stop(); Environment.CurrentDirectory = previous; }
    }
}
