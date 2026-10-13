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
using Outil_Azur_complet.Bot.Panels;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Actions;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Maps.Interfaces;

// Actions sur les joueurs, groupes et combats de la carte (lot M4) : menus d'un joueur, d'un groupe de monstres et des
// épées d'un combat, duels (GA900/901/902 et boîtes Oui/Non/Ignorer, Annuler), refus GA;903, agression GA;906,
// percepteur et prismes (GA909, GA912, GA;909), combats de la carte (fC, fL, fD, volet), « qui est » (BW, BWK),
// paquets malformés ; lot X2 : menu de son personnage (Se frapper, Libérer son âme, sélecteur d'orientation eD et écho),
// mariage GA;617 → GA618 / GA619, personnage mort (AR<id>), fin de partie GO. Données synthétiques, serveur fictif local,
// fenêtre hors de l'écran, aucune capture d'écran.
internal static class BotMapActionsSmoke
{
    private const int MapId = 900094;
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
            Console.WriteLine("OK: menus joueur/groupe/épées, duels GA900-902 et boîtes, GA;903, GA;906, GA909/GA912, fC/fL/fD et volet des combats, BW/BWK, textes du client, menu de soi, eD, GF, mariage, AR<id>, GO");
        }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
        finally { LangData.Clear(); }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void PumpUntil(Func<bool> done)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(6);
        while (!done()) { if (DateTime.UtcNow > end) throw new TimeoutException("Map actions loopback timed out"); Application.DoEvents(); Thread.Sleep(5); }
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
            while (true) { if (socket.Receive(one) != 1) throw new IOException("Loopback disconnected"); if (one[0] == 0) return Encoding.UTF8.GetString(stream.ToArray()).TrimEnd('\r', '\n'); stream.WriteByte(one[0]); }
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
    private static IEnumerable<Control> All(Control value) { yield return value; foreach (Control child in value.Controls) foreach (var nested in All(child)) yield return nested; }
    private static string[] Texts(ContextMenuStrip menu) => menu.Items.Cast<ToolStripItem>().Skip(1).Select(item => item is ToolStripSeparator ? "-" : item.Text).ToArray();
    private static ToolStripMenuItem Item(ContextMenuStrip menu, string text) =>
        menu.Items.OfType<ToolStripMenuItem>().FirstOrDefault(item => item.Text == text) ?? throw new Exception("Menu entry missing: " + text + " in " + string.Join("|", Texts(menu)));
    private static Form Dialog(string name)
    {
        PumpUntil(() => BotDialogs.OpenDialogs.Any(form => form.Name == name));
        return BotDialogs.OpenDialogs.Single(form => form.Name == name);
    }
    private static void Answer(Form dialog, string button) => All(dialog).OfType<Button>().Single(b => b.Text == button).PerformClick();
    private static Map SyntheticMap(int id) => new Map { MapID = id, MapWidth = 3, MapHeight = 4, X = 2, Y = -4, MapData = string.Concat(Enumerable.Repeat("HhGaeaaaaa", 18)) };

    private static void Run()
    {
        string previous = Environment.CurrentDirectory;
        string folder = Path.Combine(TestPaths.Work, "bot-map-actions"); Directory.CreateDirectory(folder); Environment.CurrentDirectory = folder;
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            MessagesReception.Init();
            LangData.Clear();
            foreach (int action in new[] { 900, 901, 902, 903, 906, 909 })
                Check(GameActionRouter.IsRegistered(action), "GA;" + action + " handler was not discovered");
            Check(!GameActionRouter.IsRegistered(905) && !GameActionRouter.IsRegistered(912), "Unexpected GA handler registered");
            Map.AllBotMaps[MapId] = SyntheticMap(MapId);
            using (var account = new Accounts(new AccountConfig("synthetic-actions", "synthetic", "loopback")))
            {
                Task<Socket> accept = listener.AcceptSocketAsync();
                Complete(account.Connexion.ConnectToServer(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port)); Complete(accept);
                using (Socket peer = accept.Result)
                {
                    peer.ReceiveTimeout = 6000;
                    account.Game.character.SetPerso_Data(7, "Personnage de test", 25, 0, 8);
                    Feed(account, "GDM|" + MapId + "|date|key"); Expect(peer, "GI", "GDM did not request GI");
                    // Soi (Bonta), joueur aligné Brakmar avec grade, joueur protégé (restrictions 6 = duel et échange interdits), groupe de monstres.
                    Feed(account, "GM|+0;1;0;7;Personnage de test;1;10^100;0;1,0,0,32"
                        + "|+5;1;0;42;Joueur fictif;1;10^100;0;2,0,3,52;-1;-1;-1;;0;;;;;0"
                        + "|+6;1;0;43;Joueur protégé;1;10^100;0;2,0,0,53;-1;-1;-1;;0;;;;;6"
                        + "|+4;1;30;-7;101,102;-3;1^100,2^100;5,6");
                    Feed(account, "Gc+11;0|11;8;0;-1|12;9;0;-1");
                    account.AccountStates = AccountStates.CONNECTED_INACTIVE;
                    Map map = account.Game.Map;
                    MapActions actions = account.Game.Interactions.MapActions;
                    var player = map.GetActor(42) as PlayerActor; var guarded = map.GetActor(43) as PlayerActor;
                    var group = map.GetActor(-7) as MonsterGroupActor;
                    Check(player != null && guarded != null && group != null && map.FightSwords.ContainsKey(11), "Synthetic actors are missing");
                    Check(actions.OwnAlignment == 1 && map.CanAttack && map.CanChallenge && map.Capabilities == null, "Own alignment or map rights differ");

                    ChallengeProtocol(account, peer, actions);
                    FightsProtocol(account, peer, actions);
                    Malformed(account, peer, actions);
                    CollectorAndPrisms(account, peer, actions);

                    using (var form = new GameClientFullform(account))
                    {
                        form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-4000, -4000);
                        form.ShowInTaskbar = false; form.Show(); Application.DoEvents();
                        PanelHost host = form.Panels;
                        var view = form.GetType().GetField("mapControl", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form) as MapControl;
                        Check(host != null && view != null && host.Get<FightsListPanel>() != null, "Fights panel is not registered in the drawer");
                        NoPacket(peer, "Opening the game window sent a packet");
                        var feedback = new List<string>(); host.Feedback += feedback.Add;

                        PlayerMenu(account, peer, actions, view, player, guarded);
                        ChallengeDialogs(account, peer, actions, feedback);
                        GroupMenu(account, peer, view, group);
                        SwordsMenu(account, peer, view, host);
                        FightsPanel(account, peer, actions, host);
                        SelfMenu(folder, account, peer, actions, view);
                        MarriageDialogs(account, peer, actions);
                        ResetDeadCharacter(account, peer);
                        GameOverDialog(account, form); // en dernier : GO ferme la connexion

                        form.Close(); Application.DoEvents();
                        Check(BotDialogs.OpenDialogs.Count == 0, "Duel dialogs stayed open after the window closed");
                    }
                    LangTexts(folder, actions);
                    NoPacket(peer, "Unexpected packet at the end of the test");
                }
            }
        }
        finally { listener.Stop(); Environment.CurrentDirectory = previous; }
    }

    /// <summary>Envois et lectures des duels sans interface : GA900, GA;900 (événement), GA901, GA;903, GA;906, ignorés.</summary>
    private static void ChallengeProtocol(Accounts account, Socket peer, MapActions actions)
    {
        var asked = new List<ChallengeInfo>(); var sent = new List<ChallengeInfo>(); var closed = new List<string>(); var notices = new List<MapActionNotice>();
        actions.ChallengeAsked += asked.Add; actions.ChallengeSent += sent.Add; actions.Notice += notices.Add;
        Action<ChallengeInfo, bool> onClosed = (info, accepted) => closed.Add(info.OpponentId + ":" + accepted);
        actions.ChallengeClosed += onClosed;
        try
        {
            InteractionResult result = Complete(actions.ChallengeAsync(42));
            Check(result.Sent, "Challenge(42) refused: " + result.Message);
            Expect(peer, "GA90042", "Challenge(42) does not send GA90042");
            Feed(account, "GA;903;7;o");
            Check(actions.LastJoinError == "o" && actions.LastMessage == MapActionTexts.JoinError("o")
                && notices.Any(n => n.Kind == MapActionNoticeKind.Error && n.Text == MapActionTexts.JoinError("o")), "GA;903;7;o did not produce its message");
            Check(actions.PendingChallenge == null, "A refused challenge stayed pending");

            Feed(account, "GA;900;42;7");
            Check(asked.Count == 1 && asked[0].ChallengerId == 42 && asked[0].ChallengerName == "Joueur fictif" && asked[0].Direction == ChallengeDirection.Incoming,
                "GA;900;42;7 did not raise ChallengeAsked");
            Check(notices.Any(n => n.Kind == MapActionNoticeKind.Info && n.Text == MapActionTexts.AChallengesB("Joueur fictif", "Personnage de test")), "A_CHALENGE_B info missing");
            NoPacket(peer, "An incoming challenge was answered without the player");
            Check(!Complete(actions.AcceptChallengeAsync(43)).Sent, "Accepting another player's challenge was allowed");
            result = Complete(actions.AcceptChallengeAsync(42));
            Check(result.Sent && actions.PendingChallenge == null, "AcceptChallenge(42) failed: " + result.Message);
            Expect(peer, "GA90142", "AcceptChallenge(42) does not send GA90142");
            Feed(account, "GA;901;42;7");
            Check(closed.SequenceEqual(new[] { "42:True" }), "GA;901 did not close the challenge: " + string.Join(",", closed));

            // Duel proposé : écho GA;900;7;42, annulation GA902<soi>, puis GA;902 du serveur.
            Complete(actions.ChallengeAsync(42)); Expect(peer, "GA90042", "Second challenge not sent");
            Feed(account, "GA;900;7;42");
            Check(sent.Count == 1 && actions.PendingChallenge?.Direction == ChallengeDirection.Outgoing && actions.PendingChallenge.TargetId == 42, "Outgoing challenge not tracked");
            Check(!Complete(actions.ChallengeAsync(42)).Sent, "A second challenge was sent while one is pending");
            Complete(actions.CancelChallengeAsync()); Expect(peer, "GA9027", "Cancelling an outgoing challenge does not send GA902<self>");
            Feed(account, "GA;902;7;42");
            Check(closed.Last() == "42:False" && actions.PendingChallenge == null, "GA;902 did not close the outgoing challenge");

            // Duel d'autres joueurs : simple information, aucun envoi.
            int before = asked.Count;
            Feed(account, "GA;900;43;42");
            Check(asked.Count == before && actions.PendingChallenge == null, "A challenge between other players was taken for ours");
            NoPacket(peer, "A challenge between other players was answered");

            // Agression subie : texte centré du client ; agression observée : information seulement.
            notices.Clear();
            Feed(account, "GA;906;42;7");
            Check(notices.Any(n => n.Kind == MapActionNoticeKind.Alert && n.Text == MapActionTexts.YouAreAttacked)
                && notices.Any(n => n.Text == MapActionTexts.AAttacksB("Joueur fictif", "Personnage de test")), "GA;906 on the character did not announce YOU_ARE_ATTAC");
            notices.Clear();
            Feed(account, "GA;906;42;43");
            Check(notices.Count == 1 && notices[0].Kind == MapActionNoticeKind.Info, "GA;906 between other players raised an alert");
            Feed(account, "GA;903;7;q");
            Check(actions.LastJoinError == "q" && actions.LastMessage.Contains("« q »"), "Unknown GA;903 code is not reported");
            Check(MapActionTexts.JoinErrorCodes.Count == 17 && MapActionTexts.JoinErrorCodes.All(code => MapActionTexts.JoinErrorKey(code) != null), "GA;903 codes of the client are incomplete");
            NoPacket(peer, "Server notices triggered packets");
        }
        finally
        {
            actions.ChallengeAsked -= asked.Add; actions.ChallengeSent -= sent.Add; actions.Notice -= notices.Add; actions.ChallengeClosed -= onClosed;
        }
    }

    /// <summary>fC, fL, fD et BW/BWK sans interface.</summary>
    private static void FightsProtocol(Accounts account, Socket peer, MapActions actions)
    {
        int changes = 0; Action counter = () => changes++;
        actions.FightsChanged += counter;
        try
        {
            Feed(account, "fC2");
            Check(actions.FightCount == 2 && changes == 1, "fC2 did not set the fight count");
            Feed(account, "fL11;-1;0,0,1;0,0,1;|12;1700000000000;0,0,2;1,0,3;|bad;entry");
            Check(actions.Fights.Count == 2 && actions.GetFight(11).StartTime == null && actions.GetFight(12).Team2.Type == 1
                && actions.GetFight(12).Team2.Count == 3 && !actions.GetFight(12).HasDetails, "fL was not read");
            Feed(account, "fD12|Alpha~10;Beta~20;|101~5;102~6;103~7;");
            MapFightInfo fight = actions.GetFight(12);
            Check(fight.HasDetails && fight.Team1.TotalLevel == 30 && fight.Team2.TotalLevel == 18
                && fight.Team1.Members.Select(m => m.Name).SequenceEqual(new[] { "Alpha", "Beta" })
                && fight.Team2.Members.All(m => m.Kind == MapFightMemberKind.Monster), "fD12 was not read");
            Feed(account, "fL11;-1;0,0,1;0,0,1;|12;1700000000000;0,0,2;1,0,3;");
            Check(actions.GetFight(12).HasDetails, "Refreshing fL lost the details of a fight");
            Feed(account, "fD1|a1b2,c3d4~40;|1111~50;");
            Check(actions.GetFight(1).Team1.Members[0].Kind == MapFightMemberKind.Collector && actions.GetFight(1).Team2.Members[0].Kind == MapFightMemberKind.Monster,
                "Tax collector and prism names are not recognised like the client");
            DateTime start = DateTimeOffset.FromUnixTimeMilliseconds(1700000000000).UtcDateTime;
            Check(actions.GetFight(12).Elapsed(start.AddSeconds(65)) == TimeSpan.FromSeconds(65) && actions.GetFight(12).Elapsed(start.AddSeconds(-5)) == null
                && actions.GetFight(11).Elapsed(start) == null, "Fight duration differs from getDiffDate");
            Feed(account, "fC-1");
            Check(actions.FightCount == 1, "fC-1 did not remove a fight");

            InteractionResult result = Complete(actions.RequestFightListAsync()); Check(result.Sent, result.Message); Expect(peer, "fL", "fL not sent");
            Complete(actions.RequestFightDetailsAsync(12)); Expect(peer, "fD12", "fD12 not sent");

            var answers = new List<WhoisInfo>(); actions.WhoisReceived += answers.Add;
            Complete(actions.WhoisAsync("Joueur fictif")); Expect(peer, "BWJoueur fictif", "Whois does not send BW<name>");
            Feed(account, "BWKpseudo-fictif|1|Joueur fictif|-1");
            WhoisInfo info = actions.LastWhois;
            Check(answers.Count == 1 && info.Pseudo == "pseudo-fictif" && info.State == 1 && info.CharacterName == "Joueur fictif" && info.AreaId == null
                && info.Text == MapActionTexts.WhoisAnswer(info, false), "BWK was not read");
            Feed(account, "BWKSynthetic-Actions|2|Personnage de test|-1");
            Check(actions.LastWhois.Text == MapActionTexts.WhoisAnswer(actions.LastWhois, true), "BWK about the account itself is not recognised");
            Complete(actions.WhoisAsync("Inconnu")); Expect(peer, "BWInconnu", "Second whois not sent");
            Feed(account, "BWEInconnu");
            Check(actions.LastMessage == MapActionTexts.WhoisNotFound("Inconnu"), "BWE was not reported");
            Check(!actions.ReportWhoisNotFound("Inconnu"), "A whois error was reported twice");
            Feed(account, "BWEAutre");
            Check(actions.LastMessage == MapActionTexts.WhoisNotFound("Autre"), "BWE after a console /whois was not reported like the client");
            Check(!Complete(actions.WhoisAsync("bad|name")).Sent, "A name with '|' was sent");
            actions.WhoisReceived -= answers.Add;
            NoPacket(peer, "Fight list packets triggered other packets");
        }
        finally { actions.FightsChanged -= counter; }
    }

    /// <summary>Paquets incomplets ou illisibles : journalisés, jamais d'exception ni d'envoi.</summary>
    private static void Malformed(Accounts account, Socket peer, MapActions actions)
    {
        int count = actions.FightCount;
        foreach (string packet in new[] { "fCx", "fL", "fLgarbage|;;;", "fD", "fDx|", "fD5", "BWK", "BWKa|b", "BWKa|x|c|d", "BWE",
            "GA;900", "GA;900;x;7", "GA;900;42", "GA;901;;", "GA;902;a;b", "GA;903", "GA;906;42;x", "GA;909;;" })
            Feed(account, packet);
        Check(actions.FightCount == count && actions.PendingChallenge == null, "Malformed packets changed the state");
        NoPacket(peer, "Malformed packets triggered packets");
    }

    /// <summary>Percepteur et prismes : GA909/GA912 avec les règles du clic du client, refus locaux, annonce GA;909 du serveur.</summary>
    private static void CollectorAndPrisms(Accounts account, Socket peer, MapActions actions)
    {
        Map map = account.Game.Map;
        // Formats de Collector.parseGM et Prism.getGMPrisme : prisme de Brâkmar (2) et prisme de Bonta (1, le camp du personnage).
        Feed(account, "GM|+12;1;0;70;a,b;-6;6000^100;3;Guilde fictive;1a,2b,3c,4d|+13;1;0;71;1112;-10;8101^100;4;2;2|+14;1;0;72;1111;-10;8100^100;4;2;1");
        var collector = map.GetActor(70) as CollectorActor; var enemyPrism = map.GetActor(71) as PrismActor; var ownPrism = map.GetActor(72) as PrismActor;
        Check(collector != null && enemyPrism != null && ownPrism != null && enemyPrism.AlignmentSide == 2 && ownPrism.AlignmentSide == 1,
            "Synthetic collector or prisms are missing");
        Check(actions.CanAttackCollector(collector) && actions.CanAttackPrism(enemyPrism) && !actions.CanAttackPrism(ownPrism),
            "Collector or prism attack rules differ from the client");
        InteractionResult result = Complete(actions.AttackCollectorAsync(70));
        Check(result.Sent, "AttackCollector(70) refused: " + result.Message);
        Expect(peer, "GA90970", "AttackCollector does not send GA909<id>");
        result = Complete(actions.AttackPrismAsync(71));
        Check(result.Sent, "AttackPrism(71) refused: " + result.Message);
        Expect(peer, "GA91271", "AttackPrism does not send GA912<id>");
        Check(!Complete(actions.AttackPrismAsync(72)).Sent, "A prism of the character's own side was attacked");
        Check(!Complete(actions.AttackPrismAsync(70)).Sent && !Complete(actions.AttackCollectorAsync(71)).Sent && !Complete(actions.AttackCollectorAsync(999)).Sent,
            "An attack on a wrong or missing actor was sent");
        Feed(account, "AR3k"); // 128 en base 36 : cantInteractWithTaxCollector
        Check(!actions.CanAttackCollector(collector) && !Complete(actions.AttackCollectorAsync(70)).Sent, "AR 128 does not forbid attacking a collector");
        Feed(account, "AR0");
        var notices = new List<MapActionNotice>(); actions.Notice += notices.Add;
        try
        {
            Feed(account, "GA;909;42;70");
            Check(notices.Count == 1 && notices[0].Kind == MapActionNoticeKind.Info && notices[0].Text == MapActionTexts.AAttacksB("Joueur fictif", collector.DisplayName),
                "GA;909 is not announced like A_ATTACK_B");
        }
        finally { actions.Notice -= notices.Add; }
        Feed(account, "GM|-70|-71|-72");
        Check(map.GetActor(70) == null && map.GetActor(71) == null && map.GetActor(72) == null, "Synthetic collector or prisms stayed on the map");
        NoPacket(peer, "Collector and prism checks sent extra packets");
    }

    /// <summary>Menu d'un autre joueur : entrées du client, droits de la carte et restrictions, envois.</summary>
    private static void PlayerMenu(Accounts account, Socket peer, MapActions actions, MapControl view, PlayerActor player, PlayerActor guarded)
    {
        Map map = account.Game.Map;
        ContextMenuStrip menu = view.Router.ShowActorMenu(new Entites[] { player }, 5, Keys.None, false);
        string[] expected = { MapActionTexts.Whois, MapActionTexts.PrivateMessage, MapActionTexts.InviteToParty, MapActionTexts.Exchange, MapActionTexts.Challenge, MapActionTexts.Assault };
        // Seul le premier groupe est celui des actions de la carte : les fournisseurs suivants (amis, lot F2) ajoutent le leur après un séparateur.
        Check(Texts(menu).TakeWhile(text => text != "-").SequenceEqual(expected), "Player menu differs: " + string.Join("|", Texts(menu)));
        Check(Item(menu, MapActionTexts.Challenge).Enabled && Item(menu, MapActionTexts.Assault).Enabled, "Challenge or assault disabled on an open map");
        NoPacket(peer, "Opening the player menu sent a packet");

        map.Capabilities = Map.CapabilityNoAttack;
        menu = view.Router.ShowActorMenu(new Entites[] { player }, 5, Keys.None, false);
        Check(!Item(menu, MapActionTexts.Assault).Enabled && Item(menu, MapActionTexts.Challenge).Enabled, "ASSAULT is not disabled when CanAttack is false");
        Check(!Complete(actions.AssaultAsync(42)).Sent, "Assault was sent on a map that forbids it");
        map.Capabilities = Map.CapabilityNoChallenge;
        menu = view.Router.ShowActorMenu(new Entites[] { player }, 5, Keys.None, false);
        Check(!Item(menu, MapActionTexts.Challenge).Enabled && Item(menu, MapActionTexts.Assault).Enabled, "CHALLENGE is not disabled when CanChallenge is false");
        map.Capabilities = null;

        menu = view.Router.ShowActorMenu(new Entites[] { guarded }, 6, Keys.None, false);
        Check(Texts(menu).TakeWhile(text => text != "-").SequenceEqual(new[] { MapActionTexts.Whois, MapActionTexts.PrivateMessage, MapActionTexts.InviteToParty, MapActionTexts.Assault }),
            "Restricted player menu differs: " + string.Join("|", Texts(menu)));
        Feed(account, "AR1");
        menu = view.Router.ShowActorMenu(new Entites[] { player }, 5, Keys.None, false);
        Check(!Texts(menu).Contains(MapActionTexts.Assault), "ASSAULT is offered although AR forbids assaults");
        Feed(account, "AR0");

        menu = view.Router.ShowActorMenu(new Entites[] { player }, 5, Keys.None, false);
        Item(menu, MapActionTexts.Whois).PerformClick(); Expect(peer, "BWJoueur fictif", "WHOIS does not send BW<name>");
        menu = view.Router.ShowActorMenu(new Entites[] { player }, 5, Keys.None, false);
        Item(menu, MapActionTexts.InviteToParty).PerformClick(); Expect(peer, "PIJoueur fictif", "ADD_TO_PARTY does not send PI<name>");
        menu = view.Router.ShowActorMenu(new Entites[] { player }, 5, Keys.None, false);
        Item(menu, MapActionTexts.Exchange).PerformClick(); Expect(peer, "ER1|42", "EXCHANGE does not send ER1|<id>");
        // La fenêtre de jeu relie désormais sa console de discussion (lot C2) : « Message privé » remplit la saisie du chat.
        var chatInput = ((GameClientFullform)view.FindForm()).Chat.Input;
        Check(actions.RequestPrivateMessage("Joueur fictif").Sent && chatInput.Text == "/w Joueur fictif ", "Private message did not reach the chat console of the game window");
        chatInput.Clear();
        var console = new List<string>(); Action<string> chat = console.Add;
        actions.ChatInputRequested += chat;
        menu = view.Router.ShowActorMenu(new Entites[] { player }, 5, Keys.None, false);
        Item(menu, MapActionTexts.PrivateMessage).PerformClick(); PumpUntil(() => console.Count == 1);
        Check(console[0] == "/w Joueur fictif ", "WISPER_MESSAGE does not prepare /w <name>");
        actions.ChatInputRequested -= chat;
        NoPacket(peer, "WISPER_MESSAGE sent a packet");

        menu = view.Router.ShowActorMenu(new Entites[] { player }, 5, Keys.None, false);
        Item(menu, MapActionTexts.Assault).PerformClick();
        PumpUntil(() => BotDialogs.OpenDialogs.Count == 1);
        Form ask = BotDialogs.OpenDialogs[0];
        Check(All(ask).OfType<Label>().Any(label => label.Text.Contains(MapActionTexts.AskAttack("Joueur fictif", true, false))), "Assault confirmation text differs");
        Answer(ask, "Non"); PumpUntil(() => BotDialogs.OpenDialogs.Count == 0);
        NoPacket(peer, "Assault was sent without confirmation");
        menu = view.Router.ShowActorMenu(new Entites[] { player }, 5, Keys.None, false);
        Item(menu, MapActionTexts.Assault).PerformClick();
        PumpUntil(() => BotDialogs.OpenDialogs.Count == 1); Answer(BotDialogs.OpenDialogs[0], "Oui");
        Expect(peer, "GA90642", "Confirmed ASSAULT does not send GA906<id>");
        Check(!view.Router.Registry.EntriesFor(account.Game.Map.Self, account.Game).Any(entry => entry.Texte == MapActionTexts.Challenge),
            "The character's own menu offers a challenge");
    }

    /// <summary>Boîtes des duels : Oui, Ignorer (puis refus d'office), Annuler, fermeture par le serveur, messages au bandeau.</summary>
    private static void ChallengeDialogs(Accounts account, Socket peer, MapActions actions, List<string> feedback)
    {
        Feed(account, "GA;900;42;7");
        Form box = Dialog("duel-recu");
        Check(All(box).OfType<Button>().Select(b => b.Text).OrderBy(t => t).SequenceEqual(new[] { "Ignorer", "Non", "Oui" }), "Incoming duel dialog lacks Oui/Non/Ignorer");
        Check(All(box).OfType<Label>().Any(label => label.Text == MapActionTexts.ChallengeYou("Joueur fictif")), "A_CHALENGE_YOU text missing");
        Answer(box, "Oui"); Expect(peer, "GA90142", "Oui does not send GA901<challenger>");
        Feed(account, "GA;901;42;7");
        Check(BotDialogs.OpenDialogs.Count == 0, "Dialog left open after GA;901");

        Feed(account, "GA;900;42;7");
        Answer(Dialog("duel-recu"), "Non"); Expect(peer, "GA90242", "Non does not send GA902<challenger>");
        Feed(account, "GA;902;42;7");

        Feed(account, "GA;900;42;7");
        Dialog("duel-recu");
        Feed(account, "GA;902;42;7");
        PumpUntil(() => BotDialogs.OpenDialogs.Count == 0);
        NoPacket(peer, "A duel closed by the server was answered");

        Feed(account, "GA;900;42;7");
        Answer(Dialog("duel-recu"), "Ignorer"); Expect(peer, "GA90242", "Ignorer does not refuse the duel");
        Check(actions.IsIgnored("Joueur fictif"), "Ignored player not remembered");
        Feed(account, "GA;902;42;7");
        Feed(account, "GA;900;42;7");
        Expect(peer, "GA90242", "A duel from an ignored player is not refused automatically");
        Application.DoEvents();
        Check(BotDialogs.OpenDialogs.Count == 0 && actions.PendingChallenge == null, "A duel from an ignored player opened a dialog");
        Feed(account, "GA;902;42;7");

        InteractionResult refused = Complete(actions.ChallengeAsync(43));
        Check(!refused.Sent && refused.Message.Contains("Joueur protégé"), "Challenging a protected player is not refused locally");
        NoPacket(peer, "A protected player was challenged");
        account.Game.Interactions.Clear();
        Check(!actions.IsIgnored("Joueur fictif"), "Clear kept the ignore list");
        Complete(actions.ChallengeAsync(42)); Expect(peer, "GA90042", "Challenge after Clear not sent");
        Feed(account, "GA;900;7;42");
        Form cancel = Dialog("duel-propose");
        Check(All(cancel).OfType<Button>().Single().Text == "Annuler", "Outgoing duel dialog is not a single Annuler");
        Answer(cancel, "Annuler"); Expect(peer, "GA9027", "Annuler does not send GA902<self>");
        Feed(account, "GA;902;7;42");

        feedback.Clear();
        Feed(account, "GA;906;42;7"); Application.DoEvents();
        Check(feedback.Contains(MapActionTexts.YouAreAttacked), "YOU_ARE_ATTAC is not shown in the banner");
        Feed(account, "GA;903;7;z"); Application.DoEvents();
        Check(feedback.Contains(MapActionTexts.JoinError("z")), "GA;903 error is not shown in the banner");
        NoPacket(peer, "Banner messages triggered packets");
    }

    /// <summary>Groupe de monstres : « Attaquer » → MoveRequested sur la cellule du groupe ; Maj + clic attaque, clic simple ouvre le menu.</summary>
    private static void GroupMenu(Accounts account, Socket peer, MapControl view, MonsterGroupActor group)
    {
        var moves = new List<short>();
        MapClickHandler move = (s, e) => { moves.Add(e.CellId); e.Handled = true; return Task.CompletedTask; };
        view.Router.MoveRequested += move;
        try
        {
            ContextMenuStrip menu = view.Router.ShowActorMenu(new Entites[] { group }, 4, Keys.None, false);
            string[] texts = Texts(menu);
            Check(texts[0] == MapActionTexts.Attack && texts[1] == "-" && texts[2] == MapActionTexts.Level + " 11" && texts.Length == 5
                && texts[3].StartsWith(group.Members[1].Name, StringComparison.Ordinal), "Monster group menu differs: " + string.Join("|", texts));
            Item(menu, MapActionTexts.Attack).PerformClick(); PumpUntil(() => moves.Count == 1);
            Check(moves[0] == 4, "ATTACK did not request a move to the group cell");
            Complete(view.Router.RouteAsync(4, MouseButtons.Left, Keys.Shift));
            Check(moves.Count == 2 && moves[1] == 4, "Shift + click on a group does not attack it");
            ContextMenuStrip before = view.Router.LastMenu;
            Complete(view.Router.RouteAsync(4, MouseButtons.Left));
            Check(moves.Count == 2 && view.Router.LastMenu != null && view.Router.LastMenu != before
                && Texts(view.Router.LastMenu).Contains(MapActionTexts.Attack), "A plain click on a group does not open its menu");
            NoPacket(peer, "Handled group moves sent packets");

            // Sans abonné, l'action par défaut de la carte marche jusqu'au groupe (Mouvement.MoveToAsync) : GA001 vers sa cellule.
            view.Router.MoveRequested -= move;
            menu = view.Router.ShowActorMenu(new Entites[] { group }, 4, Keys.None, false);
            Item(menu, MapActionTexts.Attack).PerformClick();
            string walk = Read(peer);
            Check(walk.StartsWith("GA001", StringComparison.Ordinal) && walk.EndsWith("ae", StringComparison.Ordinal),
                "ATTACK did not walk to the group cell (cell 4 = « ae »): " + walk);
            Feed(account, "GA;0");
            PumpUntil(() => !account.Game.Manager.Mouvements.IsAwaitingServer);
            NoPacket(peer, "The cancelled walk sent extra packets");
        }
        finally { view.Router.MoveRequested -= move; }
    }

    /// <summary>Épées d'un duel : rejoindre par équipe (GA903&lt;combat&gt;;&lt;équipe&gt;), Maj + clic, spectateur.</summary>
    private static void SwordsMenu(Accounts account, Socket peer, MapControl view, PanelHost host)
    {
        Check(view.ActorsAt(8).OfType<FightSwordsActor>().Count() == 1 && view.ActorsAt(9).OfType<FightSwordsActor>().Count() == 1, "Fight swords are not on their team cells");
        Complete(view.Router.RouteAsync(8, MouseButtons.Right));
        ContextMenuStrip menu = view.Router.LastMenu;
        string join1 = MapActionTexts.Join + " · " + MapActionTexts.Team + " 1 (joueurs)", join2 = MapActionTexts.Join + " · " + MapActionTexts.Team + " 2 (joueurs)";
        Check(Texts(menu).SequenceEqual(new[] { "Duel", join1, join2, MapActionTexts.Spectator, MapActionTexts.CurrentFights + "…" }), "Swords menu differs: " + string.Join("|", Texts(menu)));
        Item(menu, join1).PerformClick(); Expect(peer, "GA90311;11", "JOIN_SMALL does not send GA903<fight>;<team>");
        Complete(view.Router.RouteAsync(9, MouseButtons.Left, Keys.Shift)); Expect(peer, "GA90311;12", "Shift + click on a sword does not join its team");
        Complete(view.Router.RouteAsync(8, MouseButtons.Right));
        Item(view.Router.LastMenu, MapActionTexts.Spectator).PerformClick(); Expect(peer, "GA90311", "Spectator does not send GA903<fight>");
        Check(!Complete(account.Game.Interactions.MapActions.JoinFightAsync(11, 99)).Sent, "Joining an unknown team was allowed");
        Feed(account, "Gc+13;4|13;10;0;-1|-14;11;1;-1");
        var pvm = account.Game.Map.FightSwords[13];
        Check(account.Game.Interactions.MapActions.CanJoinTeam(pvm, pvm.Teams[0]) && !account.Game.Interactions.MapActions.CanJoinTeam(pvm, pvm.Teams[1]),
            "Monster team of a PvM fight is joinable");
        Feed(account, "Gc-13");
        NoPacket(peer, "Swords menu sent extra packets");
    }

    /// <summary>Volet des combats : fL à l'ouverture, fD à la sélection, équipes affichées, spectateur puis fermeture.</summary>
    private static void FightsPanel(Accounts account, Socket peer, MapActions actions, PanelHost host)
    {
        var panel = host.Get<FightsListPanel>();
        host.Show(panel); Expect(peer, "fL", "Opening the fights panel does not send fL");
        Feed(account, "fC2");
        Feed(account, "fL11;-1;0,0,1;0,0,1;|12;1700000000000;0,0,2;1,0,3;");
        ListView[] lists = All(panel.View).OfType<ListView>().ToArray();
        Check(lists.Length == 3 && lists[0].Items.Count == 2 && lists[0].Items[1].SubItems[1].Text == "2 contre 3" && lists[0].Items[0].SubItems[2].Text == "placement",
            "Fights list is not shown");
        lists[0].Items[1].Selected = true; Expect(peer, "fD12", "Selecting a fight does not send fD<id>");
        Feed(account, "fD12|Alpha~10;Beta~20;|101~5;102~6;103~7;");
        Check(lists[1].Items.Count == 2 && lists[2].Items.Count == 3 && lists[1].Items[1].SubItems[1].Text == "20", "Fight teams are not shown");
        Check(All(panel.View).OfType<Label>().Any(label => label.Text.EndsWith(MapActionTexts.Level + " 30", StringComparison.Ordinal)), "Team level sum is not shown");
        All(panel.View).OfType<Button>().Single(b => b.Text == MapActionTexts.Spectator).PerformClick();
        Expect(peer, "GA90312", "Spectator button does not send GA903<fight>");
        PumpUntil(() => !host.IsOpen(panel));
        Feed(account, "fC0");
        Check(actions.Fights.Count == 0 && actions.FightCount == 0, "fC0 kept old fights");
    }

    // Lot X2 : sprite de soi vivant, tombe (« -10 ») et fantôme (« -40 », gfx 8004), comme Player.parseToGM de StarLoco.
    private const string AliveSprite = "GM|~0;1;0;7;Personnage de test;1;10^100;0;1,0,0,32;-1;-1;-1;;0;;;;;0;;0";
    private const string TombSprite = "GM|~0;1;0;7;Personnage de test;1;10^100;0;1,0,0,32;-1;-1;-1;;0;;;;;-10;;1";
    private const string GhostSprite = "GM|~0;1;0;7;Personnage de test;1;8004^100;0;1,0,0,32;-1;-1;-1;;0;;;;;-40;;0";

    private static ContextMenuStrip SelfMenuOf(Accounts account, MapControl view) =>
        view.Router.ShowActorMenu(new Entites[] { account.Game.Map.Self }, (short)account.Game.character.Cell.CellID, Keys.None, false);

    private static DirectionChooser OpenChooser(Accounts account, MapControl view)
    {
        Item(SelfMenuOf(account, view), MapActionTexts.ChangeDirection).PerformClick();
        PumpUntil(() => DirectionChooser.Current != null && DirectionChooser.Current.IsOpen);
        return DirectionChooser.Current;
    }

    /// <summary>
    /// Menu de son personnage (getPlayerPopupMenu, branche « soi ») : Se frapper (BM*|SLAP_SENTENCE|, grisé sans lang),
    /// Libérer son âme (GF, tombe seulement, sans confirmation), Changer son orientation (sélecteur, eD), clic gauche sur soi,
    /// écho eD&lt;id&gt;|&lt;dir&gt; et refus locaux (combat, tombe, diagonales, hors de 0 à 7).
    /// </summary>
    private static void SelfMenu(string folder, Accounts account, Socket peer, MapActions actions, MapControl view)
    {
        Map map = account.Game.Map;
        Feed(account, AliveSprite); Feed(account, "AR6bk");
        Check(map.Self != null && !actions.IsTomb && !actions.IsGhost && actions.CanChangeDirection && actions.AllowedDirections.Count == 8, "Living sprite of the character not read");
        short cell = (short)account.Game.character.Cell.CellID;

        string[] texts = Texts(SelfMenuOf(account, view));
        Check(texts[0] == MapActionTexts.HitHimself && texts.Contains(MapActionTexts.ChangeDirection) && !texts.Contains(MapActionTexts.FreeMySoul)
            && Array.IndexOf(texts, MapActionTexts.ChangeDirection) > 0, "Own menu differs: " + string.Join("|", texts));
        Check(!Item(view.Router.LastMenu, MapActionTexts.HitHimself).Enabled, "HIT_HIMSELF is enabled without SLAP_SENTENCE");
        Check(!view.Router.Registry.EntriesFor(map.Self, account.Game).Any(entry => entry.Texte == MapActionTexts.Challenge || entry.Texte == MapActionTexts.Exchange),
            "The character's own menu offers another player's actions");

        // Clic gauche sur son personnage (onSpriteRelease) : son menu, sans déplacement.
        ContextMenuStrip before = view.Router.LastMenu;
        Complete(view.Router.RouteAsync(cell, MouseButtons.Left));
        Check(view.Router.LastMenu != null && view.Router.LastMenu != before && Texts(view.Router.LastMenu)[0] == MapActionTexts.HitHimself,
            "A left click on the character does not open its menu");
        NoPacket(peer, "A left click on the character sent a packet");

        // Sélecteur : huit flèches avec AR6bk, chacune touchée à sa place ; un choix envoie eD<dir> et referme.
        DirectionChooser chooser = OpenChooser(account, view);
        Check(chooser.Directions.SequenceEqual(Enumerable.Range(0, 8)) && chooser.CurrentDirection == account.Game.character.Orientation,
            "Chooser directions or current orientation differ");
        for (int direction = 0; direction < 8; direction++)
            Check(chooser.DirectionAt(chooser.ArrowLocation(direction)) == direction, "Arrow " + direction + " is not hit at its place");
        Check(chooser.DirectionAt(new Point(chooser.Width / 2, chooser.Height / 2)) == -1, "The centre of the chooser is an arrow");
        string message = Complete(chooser.ChooseAsync(3));
        Expect(peer, "eD3", "Choosing an arrow does not send eD<dir>");
        Check(message != null && message.Contains("eD3") && DirectionChooser.Current == null, "The chooser stayed open or reported nothing: " + message);

        // Écho eD<id>|<dir> : appliqué au personnage et aux autres joueurs ; malformé ignoré.
        Feed(account, "eD7|3");
        Check(account.Game.character.Orientation == 3 && map.Self.Orientation == 3, "eD<self>|<dir> was not applied to the character");
        Feed(account, "eD42|5");
        Check(map.GetActor(42).Orientation == 5, "eD<id>|<dir> was not applied to another player");
        foreach (string bad in new[] { "eD42|9", "eD42|-1", "eDx|1", "eD42", "eD|2" }) Feed(account, bad);
        Check(map.GetActor(42).Orientation == 5 && account.Game.character.Orientation == 3, "Malformed eD changed an orientation");

        // AR3K (toute transformation) : les quatre diagonales seulement.
        Feed(account, "AR3K");
        chooser = OpenChooser(account, view);
        Check(chooser.Directions.SequenceEqual(new[] { 1, 3, 5, 7 }) && chooser.CurrentDirection == 3, "Without bit 8192 the chooser is not limited to the diagonals");
        Check(chooser.DirectionAt(chooser.ArrowLocation(2)) == -1, "A missing arrow can still be hit");
        Check(Complete(chooser.ChooseAsync(2)).Contains("absente") && chooser.IsOpen, "A missing direction was accepted by the chooser");
        chooser.Close(); PumpUntil(() => DirectionChooser.Current == null);
        Check(!Complete(actions.SetDirectionAsync(2)).Sent && !Complete(actions.SetDirectionAsync(8)).Sent && !Complete(actions.SetDirectionAsync(-1)).Sent,
            "An orientation outside the chooser was sent");
        account.AccountStates = AccountStates.FIGHTING;
        Check(!Complete(actions.SetDirectionAsync(1)).Sent, "eD was sent during a fight");
        account.AccountStates = AccountStates.CONNECTED_INACTIVE;
        NoPacket(peer, "Refused orientations reached the server");

        // Tombe : « Libérer son âme » à la place de l'orientation, GF sans boîte de confirmation (Game.freeMySoul).
        Feed(account, TombSprite);
        texts = Texts(SelfMenuOf(account, view));
        Check(actions.IsTomb && texts[0] == MapActionTexts.HitHimself && texts.Contains(MapActionTexts.FreeMySoul) && !texts.Contains(MapActionTexts.ChangeDirection),
            "Tomb menu differs: " + string.Join("|", texts));
        Check(!Complete(actions.SetDirectionAsync(1)).Sent, "A tomb sent eD");
        Item(view.Router.LastMenu, MapActionTexts.FreeMySoul).PerformClick();
        Expect(peer, "GF", "FREE_MY_SOUL does not send GF");
        Application.DoEvents();
        Check(BotDialogs.OpenDialogs.Count == 0, "FREE_MY_SOUL asked for a confirmation the client does not ask");

        // Fantôme : plus de « Libérer son âme », l'orientation revient (StarLoco remet dead à 0).
        Feed(account, GhostSprite);
        texts = Texts(SelfMenuOf(account, view));
        Check(actions.IsGhost && !texts.Contains(MapActionTexts.FreeMySoul) && texts.Contains(MapActionTexts.ChangeDirection), "Ghost menu differs: " + string.Join("|", texts));
        Check(!Complete(actions.FreeSoulAsync()).Sent, "A ghost sent GF");
        Feed(account, AliveSprite);
        NoPacket(peer, "Tomb and ghost menus sent extra packets");

        // Avec la phrase du client (lang synthétique) : « Se frapper » parle dans le canal général.
        string lang = Path.Combine(folder, "lang-self"); Directory.CreateDirectory(lang);
        File.WriteAllText(Path.Combine(lang, "lang.xml"), "<?xml version='1.0' encoding='utf-8'?>\n<BotLang famille=\"lang\" langue=\"fr\" version=\"1\" source=\"lang_fr_1.swf\">\n"
            + "<texte cle=\"HIT_HIMSELF\" valeur=\"Gifle factice\" />\n<texte cle=\"SLAP_SENTENCE\" valeur=\"Phrase factice\" />\n</BotLang>\n", new UTF8Encoding(false));
        try
        {
            Check(LangData.Load(lang) == 1 && MapActionTexts.HitHimself == "Gifle factice" && MapActionTexts.SlapSentence == "Phrase factice", "Synthetic SLAP texts not loaded");
            ToolStripMenuItem slap = Item(SelfMenuOf(account, view), "Gifle factice");
            Check(slap.Enabled, "HIT_HIMSELF stays disabled with SLAP_SENTENCE");
            slap.PerformClick();
            Expect(peer, "BM*|Phrase factice|", "HIT_HIMSELF does not say SLAP_SENTENCE in the general channel");
        }
        finally { LangData.Clear(); }
        NoPacket(peer, "HIT_HIMSELF sent more than one message");
    }

    /// <summary>
    /// GA;617;&lt;interrogé&gt;;&lt;interrogé&gt;,&lt;conjoint&gt;,-1 (SocketManager.GAME_SEND_WEDDING de StarLoco) : boîte Oui / Non
    /// « mariage » pour soi, GA618 / GA619&lt;ref&gt;, GA;618 / GA;619 du client, demande d'un autre joueur, paquets malformés.
    /// </summary>
    private static void MarriageDialogs(Accounts account, Socket peer, MapActions actions)
    {
        var notices = new List<MapActionNotice>(); actions.Notice += notices.Add;
        Func<bool> noBox = () => BotDialogs.OpenDialogs.All(form => form.Name != "mariage");
        try
        {
            Check(GameActionRouter.IsRegistered(617) && GameActionRouter.IsRegistered(618) && GameActionRouter.IsRegistered(619), "GA;617/618/619 handlers were not discovered");
            Feed(account, "GA;617;7;7,42,-1");
            Form box = Dialog("mariage");
            Check(All(box).OfType<Button>().Select(b => b.Text).OrderBy(t => t).SequenceEqual(new[] { "Non", "Oui" }), "Marriage dialog lacks Oui/Non");
            Check(All(box).OfType<Label>().Any(label => label.Text == MapActionTexts.AskMarriage("Personnage de test", "Joueur fictif")), "A_ASK_MARRIAGE_B text missing");
            Check(actions.PendingMarriage != null && actions.PendingMarriage.Reference == 7 && actions.PendingMarriage.PartnerId == 42, "Marriage request not tracked");
            Answer(box, "Oui"); Expect(peer, "GA6187", "Oui does not send GA618<ref>");
            PumpUntil(noBox);
            Check(actions.PendingMarriage == null, "An answered marriage stayed pending");
            Feed(account, "GA;618;7;7,42");
            Check(notices.Any(n => n.Text == MapActionTexts.Married("Personnage de test", "Joueur fictif")), "GA;618 is not announced like A_MARRIED_B");

            Feed(account, "GA;617;7;7,42,-1");
            Answer(Dialog("mariage"), "Non"); Expect(peer, "GA6197", "Non does not send GA619<ref>");
            Feed(account, "GA;619;7;7,42");
            Check(notices.Any(n => n.Text == MapActionTexts.NotMarried("Personnage de test", "Joueur fictif")), "GA;619 is not announced like A_NOT_MARRIED_B");

            // Refermée par la réponse de l'autre (GA;619) : aucune réponse envoyée.
            Feed(account, "GA;617;7;7,42,-1"); Dialog("mariage");
            Feed(account, "GA;619;42;42,7");
            PumpUntil(noBox);
            Check(actions.PendingMarriage == null, "A marriage closed by the server stayed pending");
            NoPacket(peer, "A marriage closed by the server was answered");

            // Demande adressée à un autre joueur : bulle seulement. Paquets malformés : journalisés, sans boîte.
            notices.Clear();
            Feed(account, "GA;617;42;42,43,-1"); Application.DoEvents();
            Check(noBox() && actions.PendingMarriage == null && notices.Any(n => n.Text == MapActionTexts.AskMarriage("Joueur fictif", "Joueur protégé")),
                "Another player's marriage opened a dialog or was not announced");
            foreach (string bad in new[] { "GA;617;7;abc", "GA;617;7;7", "GA;617;;7,42,-1", "GA;617;7;", "GA;618;7;x,y", "GA;619" }) Feed(account, bad);
            Application.DoEvents();
            Check(noBox() && actions.PendingMarriage == null, "A malformed marriage packet opened a dialog");
            Check(!Complete(actions.AcceptMarriageAsync()).Sent && !Complete(actions.RefuseMarriageAsync()).Sent, "A marriage answer was sent without a request");
            NoPacket(peer, "Malformed marriage packets triggered a reply");
        }
        finally { actions.Notice -= notices.Add; }
    }

    /// <summary>Choix du personnage (ChooseCharacter du client) : un mort du mode héroïque ne se joue pas, RESET_SHORTCUT envoie AR&lt;id&gt;.</summary>
    private static void ResetDeadCharacter(Accounts account, Socket peer)
    {
        Feed(account, "ALK8640000000|2|7;Personnage de test;25;80;0;0;0;;0;601;0;0|12;Tombe fictive;30;11;0;0;0;;0;601;1;2");
        Check(account.Game.Server.DeadCharacters.Count == 1 && account.Game.Server.DeadCharacters[12] == 2, "ALK did not mark the dead character");
        using (var selection = new SelectPlayerPerso(account, true))
        {
            selection.StartPosition = FormStartPosition.Manual; selection.Location = new Point(-4000, -4000); selection.ShowInTaskbar = false;
            // La fenêtre de jeu garde le compte : l'écran de choix ne doit pas le libérer en se fermant.
            typeof(SelectPlayerPerso).GetMethod("ReleaseAccount", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(selection, null);
            selection.Show(); Application.DoEvents();
            var play = (Control)typeof(SelectPlayerPerso).GetField("play", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(selection);
            Button reset = All(selection).OfType<Button>().Single(b => b.Text == MapActionTexts.ResetCharacter);
            ListView list = All(selection).OfType<ListView>().First(view => view.Items.Count == 2);
            Action<int> select = id => { foreach (ListViewItem row in list.Items) row.Selected = (int)row.Tag == id; Application.DoEvents(); };
            select(12);
            Check(reset.Visible && reset.Enabled && !play.Enabled, "A dead character can be played or cannot be reset");
            select(7);
            Check(!reset.Enabled && play.Enabled, "A living character cannot be played or can be reset");
            select(12);
            reset.PerformClick(); PumpUntil(() => BotDialogs.OpenDialogs.Count == 1);
            Check(All(BotDialogs.OpenDialogs[0]).OfType<Label>().Any(label => label.Text == MapActionTexts.ResetCharacterQuestion), "DO_U_RESET_CHARACTER text missing");
            Answer(BotDialogs.OpenDialogs[0], "Non"); PumpUntil(() => BotDialogs.OpenDialogs.Count == 0);
            NoPacket(peer, "AR was sent without confirmation");
            reset.PerformClick(); PumpUntil(() => BotDialogs.OpenDialogs.Count == 1);
            Answer(BotDialogs.OpenDialogs[0], "Oui");
            Expect(peer, "AR12", "RESET_SHORTCUT does not send AR<id>");
            Complete(selection.ResetCharacter(7));
            NoPacket(peer, "AR was sent for a living character");
            selection.Close(); Application.DoEvents();
        }
        Check(account.Connexion != null && account.Connexion.IsConnected(), "Closing the selection screen released the account");
        Feed(account, "ALK8640000000|1|7;Personnage de test;25;80;0;0;0;;0;601;0;0");
        Check(account.Game.Server.DeadCharacters.IsEmpty, "A revived list kept the dead character");
    }

    /// <summary>GO : boîte « fin-de-partie » à un seul bouton Rejouer (REPLAY_WORD), connexion fermée, retour au choix du personnage.</summary>
    private static void GameOverDialog(Accounts account, GameClientFullform form)
    {
        FieldInfo hook = typeof(BotDialogs).Assembly.GetType("Outil_Azur_complet.Bot.MapActionPrompts", true)
            .GetField("ReplayAction", BindingFlags.Static | BindingFlags.NonPublic);
        object original = hook.GetValue(null);
        var replays = new List<Form>();
        hook.SetValue(null, (Action<Form, AccountConfig>)((window, config) => { if (config == account.accountConfig) replays.Add(window); }));
        try
        {
            Feed(account, "GO");
            Form box = Dialog("fin-de-partie");
            Check(All(box).OfType<Button>().Select(b => b.Text).SequenceEqual(new[] { MapActionTexts.Replay }), "Game over dialog is not a single Rejouer button");
            Check(account.Connexion == null || !account.Connexion.IsConnected(), "GO did not close the connection");
            Answer(box, MapActionTexts.Replay);
            PumpUntil(() => replays.Count == 1);
            Check(ReferenceEquals(replays[0], form), "Rejouer did not leave the game window for the character selection");
        }
        finally { hook.SetValue(null, original); }
    }

    /// <summary>Textes et tables d'alignement du client lus dans un lang synthétique.</summary>
    private static void LangTexts(string folder, MapActions actions)
    {
        string lang = Path.Combine(folder, "lang"); Directory.CreateDirectory(lang);
        File.WriteAllText(Path.Combine(lang, "lang.xml"), "<?xml version='1.0' encoding='utf-8'?>\n<BotLang famille=\"lang\" langue=\"fr\" version=\"1\" source=\"lang_fr_1.swf\">\n"
            + "<texte cle=\"CHALLENGE\" valeur=\"Défi factice\" />\n<texte cle=\"A_CHALENGE_YOU\" valeur=\"&lt;b&gt;%1&lt;/b&gt; propose un essai\" />\n"
            + "<texte cle=\"CANT_YOU_R_OCCUPED\" valeur=\"Occupé (texte factice)\" />\n</BotLang>\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(lang, "alignment.xml"), "<?xml version='1.0' encoding='utf-8'?>\n<BotLang famille=\"alignment\" langue=\"fr\" version=\"1\" source=\"alignment_fr_1.swf\">\n"
            + "<entree table=\"A.at\" id=\"1\" valeur=\"false,false,true,false\" />\n<entree table=\"A.jo\" id=\"1\" valeur=\"false,true,false,true\" />\n</BotLang>\n", new UTF8Encoding(false));
        Check(LangData.Load(lang) == 2, "Synthetic lang files not loaded");
        Check(MapActionTexts.Challenge == "Défi factice" && MapActionTexts.ChallengeYou("Joueur fictif") == "Joueur fictif propose un essai"
            && MapActionTexts.JoinError("o") == "Occupé (texte factice)", "Client texts are not used when lang is loaded");
        Check(MapActionTexts.Assault == "Agresser (JcJ)", "Missing key does not fall back to the bot text");
        Check(MapActions.CanAttackAlignment(1, 2) && !MapActions.CanAttackAlignment(1, 0) && !MapActions.CanAttackAlignment(1, -1) && !MapActions.CanAttackAlignment(1, 9)
            && MapActions.CanJoinAlignment(1, 1) && !MapActions.CanJoinAlignment(1, 2), "Alignment tables A.at/A.jo are not applied");
        Check(MapActions.CanAttackAlignment(2, 1) && !MapActions.CanAttackAlignment(0, 1), "Fallback alignment rule differs when a row is missing");
        LangData.Clear();
    }
}
