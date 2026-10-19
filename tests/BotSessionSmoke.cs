using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Actions;
using Tool_BotProtocol.Game.Chat;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Perso;
using Tool_BotProtocol.Game.Session;
using BotClient = Tool_BotProtocol.Network.TcpClient;

// Session layer of the bot against a local fake server (no real server, no UI): handler registry, unknown packets,
// one packet per send, GC1 alone, GCK/AR/Ac/BT/BN/AN, generic Im, invitations without auto-accept, split frames,
// M0 system messages and disconnect causes (lot X3), in-game name change and TC/TV mini-game (lot X32).
internal static class BotSessionSmoke
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
        try { Run(); Console.WriteLine("OK: registre strict, paquets inconnus journalisés, un paquet par envoi, GC1 seul, GCK/AR/Ac/BT/BN/AN, Im générique, PIK/gJr sans acceptation, frames éclatées, tombe/fantôme du sprite GM, GF et /release, ALK héroïque, GO sans boîte de perte ; X3 : ligne de chat du nouveau niveau, M04/M018 gardés jusqu’à la coupure, perte de connexion en jeu (DISCONNECT + SRV_MSG), déconnexion demandée sans boîte ; X32 : AlEr/AlEs en jeu → nom en ligne brute, GM|~ au nouveau nom clôt la demande, Annuler sans déconnexion, TC → TV<rang>|<cellule>|<orientation>"); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Complete(Task task)
    {
        if (!task.Wait(TimeSpan.FromSeconds(6))) throw new TimeoutException("Session loopback timed out");
    }
    private static void Feed(Accounts account, string packet) => Complete(MessagesReception.ReceptionAsync(account.Connexion, packet));
    private static string Read(Socket socket)
    {
        using (var stream = new MemoryStream())
        {
            byte[] one = new byte[1];
            while (true)
            {
                if (socket.Receive(one) != 1) throw new IOException("Loopback disconnected");
                if (one[0] == 0) return Encoding.UTF8.GetString(stream.ToArray()).TrimEnd('\r', '\n');
                stream.WriteByte(one[0]);
            }
        }
    }
    private static void NoPacket(Socket socket, string message)
    {
        Thread.Sleep(250);
        Check(socket.Available == 0, message + (socket.Available > 0 ? " (reçu : " + Read(socket) + ")" : ""));
    }
    private static void Throws<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception(message);
    }

    /// <summary>Builds an in-memory assembly whose methods carry [MessageAttribution]; nothing is written to disk.</summary>
    private static Assembly HandlerAssembly(string name, params string[] prefixes)
    {
        AssemblyBuilder assembly = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName(name), AssemblyBuilderAccess.Run);
        TypeBuilder type = assembly.DefineDynamicModule(name).DefineType(name + ".Frame", TypeAttributes.Public | TypeAttributes.Class);
        ConstructorInfo attribute = typeof(MessageAttribution).GetConstructor(new[] { typeof(string) });
        for (int i = 0; i < prefixes.Length; i++)
        {
            MethodBuilder method = type.DefineMethod("Handle" + i, MethodAttributes.Public, typeof(void), new[] { typeof(BotClient), typeof(string) });
            method.GetILGenerator().Emit(OpCodes.Ret);
            method.SetCustomAttribute(new CustomAttributeBuilder(attribute, new object[] { prefixes[i] }));
        }
        type.DefineDefaultConstructor(MethodAttributes.Public);
        type.CreateType();
        return assembly;
    }

    private static void Run()
    {
        string previous = Environment.CurrentDirectory;
        string folder = Path.Combine(TestPaths.Work, "bot-session"); Directory.CreateDirectory(folder); Environment.CurrentDirectory = folder;
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            // Registry: the bot's own handlers have no duplicate prefix; a duplicate is refused before anything is registered.
            MessagesReception.Init();
            MessagesReception.Init();
            int registered = MessagesReception.messagesDatas.Count;
            Check(registered > 100 && MessagesReception.messagesDatas.GroupBy(d => d.MessageName, StringComparer.Ordinal).All(g => g.Count() == 1),
                "Init registered a prefix twice or lost handlers");
            foreach (string prefix in new[] { "GCK", "AR", "Ac", "BT", "BN", "AN", "Im", "PIK", "gJR", "gJr", "OAK", "JS", "Re", "ECK", "eUK", "As", "pong", "Bp" })
                Check(MessagesReception.messagesDatas.Any(d => d.MessageName == prefix), "Prefix " + prefix + " has no handler after the split");
            Throws<InvalidOperationException>(() => MessagesReception.Init(HandlerAssembly("SessionDuplicate", "zzDouble", "zzDouble")),
                "Two handlers for one prefix were accepted");
            Throws<InvalidOperationException>(() => MessagesReception.Init(HandlerAssembly("SessionTaken", "GCK")),
                "A handler stealing an existing prefix was accepted");
            Check(MessagesReception.messagesDatas.Count == registered, "A refused Init registered part of its handlers");

            using (var account = new Accounts(new AccountConfig("synthetic-session", "synthetic", "loopback")))
            {
                var debug = new ConcurrentQueue<string>();
                var infos = new ConcurrentQueue<string>();
                account.Logger.debug_event += entry => debug.Enqueue(entry.message);
                account.Logger.log_event += (entry, color) => infos.Enqueue(entry.message);
                Task<Socket> accept = listener.AcceptSocketAsync();
                Complete(account.Connexion.ConnectToServer(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port)); Complete(accept);
                using (Socket peer = accept.Result)
                {
                    peer.ReceiveTimeout = 6000;
                    GameSession session = account.Game.Session;

                    // Unknown packet: logged at debug level, never fatal; the next packet is still handled.
                    Feed(account, "ZZ1|inconnu");
                    Check(debug.Any(m => m.Contains("ZZ1|inconnu")), "Unknown packet was not logged at debug level");
                    Feed(account, "BT1234");
                    Check(session.ServerReferenceTime == 1234 && session.EstimatedServerTime.HasValue, "BT<ms> was not consumed");
                    Check(!debug.Any(m => m.Contains("BT1234")), "BT is still reported as an unknown packet");
                    Feed(account, "BTabc"); Check(session.ServerReferenceTime == 1234, "Unreadable BT replaced the clock");
                    Feed(account, "BN"); Check(!debug.Any(m => m.Contains("(2 caractères)")), "BN is reported as unknown");
                    NoPacket(peer, "BT/BN triggered a reply (BD is never awaited)");

                    // One packet per send.
                    var sent = new ConcurrentQueue<string>(); var rejected = new ConcurrentQueue<string>();
                    account.Connexion.PacketSent += sent.Enqueue;
                    account.Connexion.PacketRejected += (packet, reason) => rejected.Enqueue(reason);
                    Complete(account.Connexion.SendPacketAsync("BM*|ligne\nGA001ab|"));
                    Check(rejected.Count == 1 && sent.IsEmpty, "A two-line packet was not refused");
                    NoPacket(peer, "A two-line packet reached the server");

                    // Character selection: GC1 alone (no BYA, no AF), like the 1.34 client.
                    Feed(account, "ASK|8|Second|120|2|1|20|0|0|0|");
                    Check(Read(peer) == "GC1", "ASK did not send GC1"); NoPacket(peer, "ASK sent more than GC1 (BYA/AF)");
                    Check(sent.SequenceEqual(new[] { "GC1" }), "PacketSent did not report GC1 alone");
                    Check(account.ConnectionStatus == Accounts.LoadingMapStatus, "ASK did not announce the map loading: " + account.ConnectionStatus);
                    account.Game.Server.ExitCreationMenu = true; account.Game.Server.NameNewCharacter = "Nouveau";
                    Feed(account, "ALK8640000000|1|9;Nouveau;1;10;0;0;0;;0;601;0");
                    Check(Read(peer) == "AS9", "A created character was not selected"); NoPacket(peer, "AF followed AS after a creation");

                    // GCK|1|<nom>, AR base 36, Ac, AN.
                    int created = 0; session.GameCreated += () => created++;
                    Feed(account, "GCK|2|Second"); Check(!session.IsGameCreated && created == 0, "A non-solo game type was accepted");
                    Feed(account, "GCK|1|Second");
                    Check(session.IsGameCreated && session.GameType == 1 && session.GameCharacterName == "Second" && created == 1, "GCK|1|<name> was not read");
                    Feed(account, "ARfx");
                    Check(session.RawRestrictions == 573 && session.Restrictions == (PlayerRestrictions.CannotBeAssaulted | PlayerRestrictions.CannotExchange |
                        PlayerRestrictions.CannotBeAttacked | PlayerRestrictions.ForceWalk | PlayerRestrictions.Slow), "ARfx was not read in base 36 with the client masks");
                    Feed(account, "AR6bk"); Check(session.RawRestrictions == 8192 && session.Restrictions == PlayerRestrictions.None, "AR6bk is not 'no restriction'");
                    Feed(account, "AR3K"); Check(session.RawRestrictions == 128 && session.Restrictions == PlayerRestrictions.Tomb, "AR3K (upper case) was not read as bit 128 (the tomb itself comes from the GM sprite)");
                    Feed(account, "AR6bK"); Check(session.RawRestrictions == 8192, "Upper-case base 36 was refused");
                    Feed(account, "AR!?"); Check(session.RawRestrictions == 8192, "Unreadable AR replaced the restrictions");
                    Check(new Personnages(5, "Autre", 0, null).Restrictions == PlayerRestrictions.None, "Other players do not default to no restriction");
                    Feed(account, "Ac0"); Feed(account, "Ac-1"); Check(session.CommunityId == 0, "Ac was not read like the client");
                    int level = 0, levelUps = 0; session.LevelUp += value => { level = value; levelUps++; };
                    Feed(account, "AN121"); Check(account.Game.character.Level == 121 && level == 121, "AN<level> did not update the level");
                    // Lot X3 : la boîte NEW_LEVEL suit LevelUp ; le chat garde une ligne d'information.
                    Check(account.Game.Chat.Messages.Count(m => m.Kind == ChatMessageKind.Info && m.Text == ConnectionTexts.NewLevelLine(121)) == 1,
                        "AN did not write the new level in the chat");
                    Check(ConnectionTexts.NewLevel(121).StartsWith("Tu passes niveau 121", StringComparison.Ordinal), "NEW_LEVEL fallback lost its level: " + ConnectionTexts.NewLevel(121));
                    // StarLoco (Player.addXp) envoie deux fois AN pour un même gain : le second ne rouvre rien.
                    Feed(account, "AN121");
                    Check(levelUps == 1 && account.Game.Chat.Messages.Count(m => m.Text == ConnectionTexts.NewLevelLine(121)) == 1,
                        "A repeated AN for the level already announced raised LevelUp again or wrote a second chat line");
                    Feed(account, "ANxx"); Check(account.Game.character.Level == 121, "Unreadable AN changed the level");
                    Check(account.Game.Chat.Messages.Count(m => m.Text.StartsWith("Nouveau niveau", StringComparison.Ordinal)) == 1, "Unreadable AN wrote a chat line");

                    // Generic Im: type, id, ~ arguments, several entries, fallback text, language resolver, bad subscriber.
                    var messages = new List<ServerMessage>();
                    session.ServerMessageReceived += message => { throw new InvalidOperationException("faulty subscriber"); };
                    session.ServerMessageReceived += messages.Add;
                    Feed(account, "Im0152;Nom");
                    Check(messages.Count == 1 && messages[0].Kind == ServerMessageKind.Info && messages[0].Id == "152" && messages[0].NumericId == 152 &&
                        messages[0].Args.SequenceEqual(new[] { "Nom" }) && messages[0].Text == "Im0152 : Nom" && !messages[0].Resolved && messages[0].LangKey == "INFOS_152",
                        "Im0152;Nom did not raise the message with its argument");
                    Check(infos.Any(m => m == "Im0152 : Nom") && infos.Any(m => m.Contains("faulty subscriber")), "Im was not logged or the faulty subscriber stopped it");
                    Feed(account, "Im0152;2026~10~04~06~49~127.0.0.1;ignoré|153;127.0.0.1");
                    Check(messages.Count == 3 && messages[1].Args.Count == 6 && messages[1].Args[5] == "127.0.0.1" && messages[2].Id == "153",
                        "Im entries or ~ arguments were not split like the client");
                    Feed(account, "Im189"); Check(messages.Last().Kind == ServerMessageKind.Error && messages.Last().LangKey == "ERROR_89", "Im1<id> is not an error message");
                    Feed(account, "Im241"); Check(messages.Last().Kind == ServerMessageKind.Pvp && messages.Last().LangKey == "PVP_41", "Im2<id> is not a PvP message");
                    ServerMessages.Resolver = (type, id, args) => type == 0 && id == "152" ? "Dernière connexion de " + args[0] : null;
                    try
                    {
                        Feed(account, "Im0152;Nom|037");
                        Check(messages[messages.Count - 2].Text == "Dernière connexion de Nom" && messages[messages.Count - 2].Resolved &&
                            messages.Last().Text == "Im0037" && infos.Any(m => m == "Dernière connexion de Nom Im0037"), "The language resolver was not used");
                        ServerMessages.Resolver = (type, id, args) => { throw new IOException("lang illisible"); };
                        Feed(account, "Im0152;Nom"); Check(messages.Last().Text == "Im0152 : Nom", "A failing resolver broke the Im handler");
                    }
                    finally { ServerMessages.Resolver = null; }
                    int count = messages.Count;
                    foreach (string bad in new[] { "Im", "Im0", "Im9123", "Im0|", "Im0;a~b" }) Feed(account, bad);
                    Check(messages.Count == count, "Malformed Im produced messages");
                    NoPacket(peer, "Im triggered an automatic reply (PI…)");

                    // Party invitation: never PA; refused with PR unless a subscriber takes it.
                    Feed(account, "PIKInvitant|Second"); Check(Read(peer) == "PR", "Unhandled party invitation was not refused with PR");
                    var invitations = new List<InvitationEventArgs>();
                    EventHandler<InvitationEventArgs> takeParty = (s, e) => { invitations.Add(e); e.Handled = true; };
                    session.PartyInviteReceived += takeParty;
                    Feed(account, "PIKAmi|Second");
                    Check(invitations.Count == 1 && invitations[0].InviterName == "Ami" && invitations[0].Target == "Second", "PartyInviteReceived was not raised");
                    NoPacket(peer, "A handled party invitation was answered by the bot");
                    Feed(account, "PIKSecond|Autre"); NoPacket(peer, "The bot answered its own invitation");
                    Check(invitations.Count == 1, "The bot's own invitation was offered as received");
                    session.PartyInviteReceived -= takeParty;
                    Feed(account, "PIKSeul"); Check(Read(peer) == "PR", "Incomplete PIK was not refused like the client");

                    // Guild invitation: gJE always carries the inviter id; gJR is only the inviter-side confirmation.
                    Feed(account, "gJRAutre"); NoPacket(peer, "gJR (own invitation) triggered gJE");
                    Feed(account, "gJr42|Invitant|Guilde fictive"); Check(Read(peer) == "gJE42", "Guild invitation was not refused with gJE<id>");
                    Feed(account, "gJrabc|Invitant|Guilde"); NoPacket(peer, "gJE was sent without a numeric inviter id");
                    session.GuildInviteReceived += (s, e) => { invitations.Add(e); e.Handled = true; };
                    Feed(account, "gJr43|Invitant|Guilde fictive");
                    Check(invitations.Last().InviterId == "43" && invitations.Last().GuildName == "Guilde fictive", "GuildInviteReceived was not raised");
                    NoPacket(peer, "A handled guild invitation was refused");

                    // Ping: Bp is no longer answered, pong measures the round trip of ping.
                    Feed(account, "Bp"); NoPacket(peer, "Bp was answered");
                    Complete(account.Connexion.SendPacketAsync("ping")); Check(Read(peer) == "ping", "ping was not sent");
                    Feed(account, "pong"); Check(account.Connexion.GetTotalPings() == 1, "pong did not record the ping round trip");
                    Feed(account, "pong"); Check(account.Connexion.GetTotalPings() == 1, "A pong without ping was recorded");

                    // Handlers moved out of CharacterFrame still answer like before.
                    Feed(account, "Re+"); Check(account.CanUseMount, "Re (MountFrame) no longer handled");
                    Feed(account, "PCKInvitant"); Check(account.Game.character.InGroupe, "PCK (PartyFrame) no longer handled");
                    Feed(account, "ERK1|8|1"); Check(Read(peer) == "EV", "ERK (ExchangeFrame) no longer answered");
                    Feed(account, "eUK8|1"); Check(account.AccountStates == AccountStates.REGENERATION, "eUK (EmoteFrame) no longer handled");
                    Feed(account, "JS|2;24~1~0~0~1000"); Check(account.Game.character.GetJobsSnapshot().Any(job => job.ID == 2), "JS (JobsFrame) no longer handled");
                    Feed(account, "Ow12|1000"); Check(account.Game.character.Inventory.Pods_Max == 1000, "Ow (CharacterFrame) no longer handled");
                    Feed(account, "ILSbad"); Feed(account, "ILFbad");
                    Check(!infos.Any(m => m.Contains("Input string")), "Unreadable ILS/ILF raised a parse exception");
                    NoPacket(peer, "Unexpected packet at the end of the session test");
                }
            }
            TombAndGameOver(listener);
            SystemMessagesAndLoss(listener);
            NameChangeAndMiniGame(listener);
        }
        finally { listener.Stop(); Environment.CurrentDirectory = previous; }
    }

    // Lot X32 : potion de changement de nom en jeu (AlEr → saisie → ligne brute ; AlEs → nouvel essai ; AlEr puis GM|~ au nouveau
    // nom → demande close ; jamais de déconnexion) et mini-jeu : TC<id>|<clé> → TV<rang>|<cellule>|<orientation> envoyé aussitôt.
    private static void NameChangeAndMiniGame(TcpListener listener)
    {
        const int mapId = 900096;
        Map.AllBotMaps[mapId] = new Map { MapID = mapId, MapWidth = 3, MapHeight = 4, X = 3, Y = -4, MapData = string.Concat(Enumerable.Repeat("HhGaeaaaaa", 18)) };
        Check(MessagesReception.messagesDatas.Any(d => d.MessageName == "TC"), "TC has no handler");
        foreach (string name in new[] { "Nouveau", "zed", "nom-compose", "Xa-b", "pin", "Nin-pong" }) Check(NameChange.IsReadByStarLoco(name), "Name refused as unreadable: " + name);
        foreach (string name in new[] { "Gaston", "Albert", "ping", "PINGUIN", "Ping-pong", "cedric", "Walter", "Bob", "Kim", "", null })
            Check(!NameChange.IsReadByStarLoco(name), "Name accepted although StarLoco would read it as a packet: " + name);
        Socket peer;
        using (var account = Connected(listener, "synthetic-rename", out peer))
        using (peer)
        {
            var infos = new ConcurrentQueue<string>();
            account.Logger.log_event += (entry, color) => infos.Enqueue(entry.message);
            int lost = 0; account.ConnectionLost += loss => lost++;
            GameSession session = account.Game.Session;
            NameChange change = session.NameChange;
            account.Game.character.SetPerso_Data(7, "Ancien", 30, 1, 1);
            account.AccountStates = AccountStates.CONNECTED_INACTIVE;
            Feed(account, "GDM|" + mapId + "|date|key"); Check(Read(peer) == "GI", "GDM did not request GI");
            Feed(account, "GM|+5;3;0;7;Ancien;1;10^100;1;0,0,0,37;-1;-1;-1;;0;;;;;0;;0");
            Check(account.Game.character.Cell != null && account.Game.character.Cell.CellID == 5 && account.Game.character.Orientation == 3, "The self GM did not place the character");

            // Sans abonné : AlEr en jeu est journalisé, la session continue, rien ne part ; le pseudo de connexion n'est pas concerné.
            int loginRequests = 0; account.Nickname.Requested += request => loginRequests++;
            Feed(account, "AlEr");
            Check(!change.IsPending && !account.Nickname.IsPending && loginRequests == 0 && account.Connexion != null && account.Connexion.IsConnected(),
                "AlEr in game without a subscriber opened a request or disconnected");
            Check(infos.Any(m => m.Contains("AlEr") && m.Contains("X32")), "AlEr in game without a subscriber was not logged");
            NoPacket(peer, "AlEr in game sent a packet");

            // Avec abonné : AlEr ouvre la demande ; contrôles locaux ; la ligne brute part ; une seule proposition à la fois.
            var requests = new List<NicknameRequest>(); var accepted = new List<string>(); var renamed = new List<string>();
            change.Requested += requests.Add; change.Accepted += accepted.Add; session.CharacterRenamed += renamed.Add;
            Feed(account, "AlEr");
            Check(requests.Count == 1 && requests[0].Attempt == 1 && requests[0].Refused == null && requests[0].Error == null && change.IsPending
                && ReferenceEquals(change.Pending, requests[0]), "AlEr in game did not open the first name request");
            Check(!account.Nickname.IsPending && loginRequests == 0, "AlEr in game reached the login nickname choice");
            NoPacket(peer, "The name request sent a packet by itself");
            InteractionResult tooShort = Await(change.ChooseAsync("ab"));
            Check(!tooShort.Sent && tooShort.Message == ConnectionTexts.NicknameTooShort, "A two-character name was not refused locally: " + tooShort.Message);
            InteractionResult login = Await(change.ChooseAsync("SYNTHETIC-RENAME"));
            Check(!login.Sent && login.Message == ConnectionTexts.NicknameEqualsLogin, "The account name was not refused locally: " + login.Message);
            InteractionResult unreadable = Await(change.ChooseAsync("Gaston"));
            Check(!unreadable.Sent && unreadable.Message == NameChange.UnreadableByServer("Gaston") && unreadable.Message.Contains("« G »"),
                "A name starting with a packet letter was not refused locally: " + unreadable.Message);
            InteractionResult twoLines = Await(change.ChooseAsync("Nou\nveau"));
            Check(!twoLines.Sent, "A two-line name was not refused locally");
            NoPacket(peer, "A locally refused name reached the server");
            InteractionResult first = Await(change.ChooseAsync("Nouveau"));
            Check(first.Sent && Read(peer) == "Nouveau" && change.LastSent == "Nouveau", "The name was not sent as a raw line");
            InteractionResult second = Await(change.ChooseAsync("Nouvel"));
            Check(!second.Sent, "A second name left before the server answered"); NoPacket(peer, "A second name reached the server before the answer");

            // AlEs : nom pris ou interdit, nouvel essai avec NICKNAME_ALREADY_USED et le nom refusé.
            Feed(account, "AlEs");
            Check(requests.Count == 2 && requests[1].Attempt == 2 && requests[1].Refused == "Nouveau" && requests[1].Error == ConnectionTexts.NicknameAlreadyUsed
                && change.IsPending && change.LastSent == null, "AlEs did not reopen the request with NICKNAME_ALREADY_USED");
            Check(account.Connexion != null && account.Connexion.IsConnected() && lost == 0, "AlEs disconnected the session");
            InteractionResult retry = Await(change.ChooseAsync("Nouvel"));
            Check(retry.Sent && Read(peer) == "Nouvel", "The second name was not sent as a raw line");

            // AlEr après un envoi : StarLoco le renvoie même quand il accepte (le GM|~ suit) ; saisie vierge, sans raison.
            Feed(account, "AlEr");
            Check(requests.Count == 3 && requests[2].Attempt == 3 && requests[2].Refused == "Nouvel" && requests[2].Error == null && change.IsPending,
                "AlEr after a sent name did not reopen a blank request");
            Feed(account, "GM|~5;3;0;7;Nouvel;1;10^100;1;0,0,0,37;-1;-1;-1;;0;;;;;0;;0");
            Check(accepted.SequenceEqual(new[] { "Nouvel" }) && renamed.SequenceEqual(new[] { "Nouvel" }) && !change.IsPending && change.LastSent == null,
                "The self GM|~ with the new name did not close the request");
            Check(account.Game.character.Name == "Nouvel" && account.Game.Map.Self != null && account.Game.Map.Self.Name == "Nouvel", "The character name was not updated from its sprite");
            Check(account.Connexion != null && account.Connexion.IsConnected() && lost == 0 && account.AccountStates == AccountStates.CONNECTED_INACTIVE, "The name change disconnected the session");
            NoPacket(peer, "The accepted name sent a packet");
            Check(infos.Any(m => m.Contains("« Ancien » en « Nouvel »")), "The rename was not logged");
            InteractionResult late = Await(change.ChooseAsync("Tard"));
            Check(!late.Sent, "A name was sent without a pending request"); NoPacket(peer, "A name reached the server without a request");
            // Un GM|~ du personnage au même nom ne lève rien ; un GM|~ d'un autre joueur non plus.
            Feed(account, "GM|~6;1;0;7;Nouvel;1;10^100;1;0,0,0,37;-1;-1;-1;;0;;;;;0;;0");
            Feed(account, "GM|~7;1;0;8;Voisin;1;10^100;1;0,0,0,37;-1;-1;-1;;0;;;;;0;;0");
            Check(renamed.Count == 1 && account.Game.character.Name == "Nouvel" && account.Game.character.Cell.CellID == 6, "An unchanged or foreign name raised CharacterRenamed, or the cell was not updated");

            // Annuler : la demande se ferme, la session continue, rien ne part (le client, lui, se déconnecte : écart documenté).
            Feed(account, "AlEr");
            Check(requests.Count == 4 && requests[3].Refused == null && requests[3].Error == null && change.IsPending, "A new AlEr did not open a new request");
            change.Cancel();
            Check(!change.IsPending && change.LastSent == null && account.Connexion != null && account.Connexion.IsConnected() && lost == 0 && account.LastDisconnect == null,
                "Cancel disconnected or kept the request");
            NoPacket(peer, "Cancel sent a packet");
            change.Cancel();
            Check(!change.IsPending, "A second Cancel changed something");
            // AlEs sans envoi (potion sans demande en cours) : nouvelle demande, sans nom refusé.
            Feed(account, "AlEs");
            Check(requests.Count == 5 && requests[4].Refused == null && requests[4].Error == null && change.IsPending, "AlEs without a sent name did not open a blank request");
            change.Cancel();

            // Mini-jeu : TC<id>|<clé> → TV<rang>|<cellule>|<orientation> aussitôt, Started pour l'interface, Last gardé.
            var started = new List<TutorialRequest>(); session.Tutorial.Started += started.Add;
            Feed(account, "TC30|7001010000");
            Check(Read(peer) == "TV0|6|1", "TC30 did not send TV0 with the cell and orientation");
            Check(started.Count == 1 && started[0].Id == 30 && started[0].Key == "7001010000" && started[0].Name == "Roulette du Bouftou" && started[0].Outcome == TutorialOutcome.Lost
                && started[0].Rank == 0 && started[0].EndPacket == "TV0|6|1" && ReferenceEquals(session.Tutorial.Last, started[0]), "TC30 did not describe the lost roulette");
            string box = started[0].Describe();
            Check(box.Contains("Roulette du Bouftou") && box.Contains("perdu") && box.Contains("TV0|6|1"), "The mini-game box text is wrong: " + box);
            Feed(account, "TC31|7001010000");
            Check(Read(peer) == "TV1|6|1" && started.Count == 2 && started[1].Outcome == TutorialOutcome.Won && started[1].Rank == 1 && started[1].Describe().Contains("gagné"),
                "TC31 did not send TV1 (won roulette)");
            Feed(account, "TC99|abc");
            Check(Read(peer) == "TV0|6|1" && started.Count == 3 && started[2].Outcome == TutorialOutcome.Unsupported && started[2].Name == null && started[2].Describe().Contains("inconnu"),
                "An unknown tutorial was not ended with TV0");
            foreach (string bad in new[] { "TC", "TCxx", "TC|", "TC-1|x", "TC30|a|b|c" }) Feed(account, bad);
            Check(Read(peer) == "TV0|6|1" && started.Count == 4 && started[3].Id == 30 && started[3].Key == "a|b|c", "A key with separators was not kept whole");
            Check(infos.Count(m => m.StartsWith("Mini-jeu illisible", StringComparison.Ordinal)) == 4, "Malformed TC were not all ignored with a log line");
            NoPacket(peer, "Malformed TC sent a packet");
            // EndAsync : renvoi manuel ; sans cellule connue, TV<rang> seul (forme du client quand cellule ou orientation manquent).
            InteractionResult manual = Await(session.Tutorial.EndAsync(2));
            Check(manual.Sent && Read(peer) == "TV2|6|1", "EndAsync did not send TV2 with the cell");
            account.Game.character.Cell = null;
            Feed(account, "TC30|7001010000");
            Check(Read(peer) == "TV0" && started[4].EndPacket == "TV0", "Without a cell the end was not TV<rank> alone");
            Check(account.Connexion != null && account.Connexion.IsConnected() && lost == 0, "The mini-game disconnected the session");
        }
        // Déconnexion pendant une demande de nom : Clear remet tout à zéro (plus rien en attente).
        using (var account = Connected(listener, "synthetic-rename-quit", out peer))
        using (peer)
        {
            account.Game.Session.NameChange.Requested += request => { };
            account.Game.character.SetPerso_Data(7, "Quitte", 30, 1, 1);
            Feed(account, "AlEr");
            Check(account.Game.Session.NameChange.IsPending, "AlEr did not open the request before the disconnection");
            account.Disconnect();
            Disconnected(peer, "Disconnect() did not close the socket");
            Check(!account.Game.Session.NameChange.IsPending && account.Game.Session.Tutorial.Last == null, "The pending name request survived the disconnection");
        }
    }

    private static void Disconnected(Socket socket, string message)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(6);
        while (DateTime.UtcNow < end)
        {
            if (socket.Poll(50000, SelectMode.SelectRead) && socket.Available == 0) return;
            if (socket.Available > 0) throw new Exception(message + " (reçu : " + Read(socket) + ")");
        }
        throw new Exception(message);
    }

    // Lot X2 : tombe et fantôme lus dans le sprite GM (restrictions signées « -10 » / « -40 » de StarLoco), jamais dans AR ;
    // GF et /release, ALK du mode héroïque (champs 10 et 11), GO puis déconnexion.
    private static void TombAndGameOver(TcpListener listener)
    {
        const int mapId = 900095;
        Map.AllBotMaps[mapId] = new Map { MapID = mapId, MapWidth = 3, MapHeight = 4, X = 2, Y = -4, MapData = string.Concat(Enumerable.Repeat("HhGaeaaaaa", 18)) };
        int raw; PlayerRestrictions known;
        Check(PlayerRestrictionsParser.TryParse("-10", out raw, out known) && raw == -36 && (known & PlayerRestrictions.Tomb) == PlayerRestrictions.Tomb,
            "-10 (tomb sprite) is not -36 with bit 128");
        Check(PlayerRestrictionsParser.TryParse("-40", out raw, out known) && raw == -144 && (known & PlayerRestrictions.Tomb) == 0 &&
            (known & PlayerRestrictions.Slow) == PlayerRestrictions.Slow, "-40 (ghost sprite) is not -144 with bit 32 and without bit 128");
        Check(PlayerRestrictionsParser.TryParse("+3K", out raw, out known) && raw == 128, "A leading + was refused");
        foreach (string bad in new[] { "-", "+", "", "--1", "-1-", "zzzzzzzz" })
            Check(!PlayerRestrictionsParser.TryParse(bad, out raw, out known), "Malformed restrictions accepted: '" + bad + "'");

        using (var account = new Accounts(new AccountConfig("synthetic-tomb", "synthetic", "loopback")))
        {
            Task<Socket> accept = listener.AcceptSocketAsync();
            Complete(account.Connexion.ConnectToServer(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port)); Complete(accept);
            using (Socket peer = accept.Result)
            {
                peer.ReceiveTimeout = 6000;
                GameSession session = account.Game.Session;
                MapActions actions = account.Game.Interactions.MapActions;

                // ALK du mode héroïque : champ 10 = 1 pour un mort, champ 11 = nombre de morts ; une liste sans ces champs les efface.
                Feed(account, "ALK8640000000|2|11;Vivant;12;10;0;0;0;;0;601;0;0|12;Tombe;30;11;0;0;0;;0;601;1;3");
                Check(account.Game.Server.DeadCharacters.Count == 1 && account.Game.Server.DeadCharacters[12] == 3, "ALK did not record the dead character and its deaths");
                Check(account.AccountCharactersInfo.ContainsKey(11) && account.AccountCharactersInfo.ContainsKey(12), "ALK lost a character of the heroic list");
                Feed(account, "ALK8640000000|1|12;Tombe;30;11;0;0;0;;0;601;0");
                Check(account.Game.Server.DeadCharacters.IsEmpty, "A revived character stayed dead after a new ALK");
                Feed(account, "ALK8640000000|1|12;Tombe;30;11;0;0;0;;0;601;1;x");
                Check(account.Game.Server.DeadCharacters.TryGetValue(12, out int deaths) && deaths == 0, "Unreadable death count was not ignored");
                NoPacket(peer, "ALK triggered a packet");

                account.Game.character.SetPerso_Data(7, "Personnage tombe", 30, 1, 1);
                account.AccountStates = AccountStates.CONNECTED_INACTIVE;
                Feed(account, "GDM|" + mapId + "|date|key"); Check(Read(peer) == "GI", "GDM did not request GI");
                Check(!session.IsTomb && !session.IsGhost && !actions.CanFreeSoul, "A character without sprite is a tomb or a ghost");
                InteractionResult refused = Await(actions.FreeSoulAsync());
                Check(!refused.Sent && refused.Message == MapActionTexts.NotDead, "GF was not refused before the sprite: " + refused.Message);

                // Tombe (StarLoco : « -1 » devant la vitesse 0). AR3K, envoyé à toute transformation, ne change rien au sprite.
                Feed(account, "GM|+0;1;0;7;Personnage tombe;1;10^100;1;0,0,0,37;-1;-1;-1;;0;;;;;-10;;1");
                Check(session.IsTomb && !session.IsGhost && actions.CanFreeSoul && !actions.CanChangeDirection, "The -10 sprite is not a tomb");
                Feed(account, "AR3K"); Check(session.IsTomb && !session.CanMoveInAllDirections, "AR3K changed the tomb state or allowed eight directions");
                Feed(account, "AR6bk"); Check(session.IsTomb && session.CanMoveInAllDirections && actions.AllowedDirections.Count == 8, "AR6bk was not read as eight directions");
                Feed(account, "AR3K"); Check(actions.AllowedDirections.SequenceEqual(new[] { 1, 3, 5, 7 }), "AR3K did not restrict to the diagonals");
                InteractionResult tombDirection = Await(actions.SetDirectionAsync(1));
                Check(!tombDirection.Sent, "eD was sent for a tomb"); NoPacket(peer, "A tomb sent eD");

                // /release puis GF : un seul GF, comme Game.freeMySoul.
                ChatResult release = Await(account.Game.Chat.ExecuteAsync("/release"));
                Check(release.Accepted && release.Packet == "GF", "/release did not send GF: " + release.Message);
                Check(Read(peer) == "GF", "/release did not reach the server as GF"); NoPacket(peer, "/release sent more than GF");
                InteractionResult freed = Await(actions.FreeSoulAsync());
                Check(freed.Sent && Read(peer) == "GF", "FreeSoulAsync did not send GF for a tomb");

                // Fantôme (gfx 8004, vitesse -40) : GF et /release refusés avec ERROR_ALREADY_A_GHOST, l'orientation redevient possible.
                Feed(account, "GM|~0;1;0;7;Personnage tombe;1;8004^100;1;0,0,0,37;-1;-1;-1;;0;;;;;-40;;1");
                Check(!session.IsTomb && session.IsGhost && !actions.CanFreeSoul && actions.CanChangeDirection, "The -40 sprite is not a ghost");
                InteractionResult ghost = Await(actions.FreeSoulAsync());
                Check(!ghost.Sent && ghost.Message == MapActionTexts.AlreadyGhost, "GF was not refused for a ghost: " + ghost.Message);
                ChatResult ghostRelease = Await(account.Game.Chat.ExecuteAsync("/release"));
                Check(!ghostRelease.Sent && ghostRelease.Message.Contains(MapActionTexts.AlreadyGhost), "/release was not refused for a ghost: " + ghostRelease.Message);
                NoPacket(peer, "A ghost sent GF");

                // Vivant : ni tombe ni fantôme ; « Se frapper » n'envoie rien sans SLAP_SENTENCE (lang.xml absent).
                Feed(account, "GM|~0;1;0;7;Personnage tombe;1;10^100;1;0,0,0,37;-1;-1;-1;;0;;;;;0;;0");
                Check(!session.IsTomb && !session.IsGhost, "A living sprite is still a tomb or a ghost");
                ChatResult slap = Await(account.Game.Chat.SlapAsync());
                Check(!slap.Sent && MapActionTexts.SlapSentence == null, "SlapAsync sent an invented sentence"); NoPacket(peer, "SlapAsync sent BM without lang.xml");

                // GO (mode héroïque) : GameOver levé puis déconnexion, comme Game.onGameOver (déconnexion douce).
                int gameOver = 0, lost = 0; bool connectedDuringEvent = false;
                session.GameOver += () => { gameOver++; connectedDuringEvent = account.Connexion != null && account.Connexion.IsConnected(); };
                account.ConnectionLost += loss => lost++;
                Feed(account, "GO");
                Check(gameOver == 1 && connectedDuringEvent, "GO did not raise GameOver before the disconnection");
                Disconnected(peer, "GO did not close the connection");
                // Lot X3 : la fin de partie a sa propre boîte (« Rejouer ») ; pas de boîte de perte de connexion en plus.
                Check(lost == 0 && account.LastDisconnect.Cause == DisconnectCause.GameOver && account.LastDisconnect.WasInGame,
                    "GO was reported as a lost connection: " + account.LastDisconnect.Cause);
            }
        }
    }

    private static Accounts Connected(TcpListener listener, string name, out Socket peer)
    {
        var account = new Accounts(new AccountConfig(name, "synthetic", "loopback"));
        Task<Socket> accept = listener.AcceptSocketAsync();
        Complete(account.Connexion.ConnectToServer(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port)); Complete(accept);
        peer = accept.Result; peer.ReceiveTimeout = 6000;
        return account;
    }

    private static void Until(Func<bool> condition, string message)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(6);
        while (!condition() && DateTime.UtcNow < end) Thread.Sleep(10);
        Check(condition(), message);
    }

    // Lot X3 : M0<id>|<a;b> gardé jusqu'à la coupure (Basics.onServerMessage, Aks.onClose) ; seule une coupure du serveur ou du
    // réseau lève ConnectionLost, avec DISCONNECT puis le texte SRV_MSG_<id>.
    private static void SystemMessagesAndLoss(TcpListener listener)
    {
        Check(MessagesReception.messagesDatas.Any(d => d.MessageName == "M0") && MessagesReception.messagesDatas.Any(d => d.MessageName == "M030"),
            "M0 and the M030 refusal do not have their own handlers");
        Socket peer;
        using (var account = Connected(listener, "synthetic-kick", out peer))
        using (peer)
        {
            var losses = new ConcurrentQueue<ConnectionLoss>();
            var debug = new ConcurrentQueue<string>();
            account.ConnectionLost += losses.Enqueue;
            account.Logger.debug_event += entry => debug.Enqueue(entry.message);
            account.Game.character.SetPerso_Data(7, "Expulse", 30, 1, 1);
            account.AccountStates = AccountStates.CONNECTED_INACTIVE;

            // Le client n'affiche rien à la réception : pas de déconnexion, pas de paquet, message gardé.
            Feed(account, "M04");
            SystemMessage maintenance = account.PendingSystemMessage;
            Check(maintenance != null && maintenance.Id == 4 && maintenance.Args.Count == 0
                && maintenance.Text == "Votre connexion a été coupée pour des raisons de maintenance.", "M04 was not kept with the SRV_MSG_4 text");
            Check(account.Connexion != null && account.Connexion.IsConnected() && losses.IsEmpty, "M04 disconnected the account by itself");
            NoPacket(peer, "M04 triggered a packet");
            // KICK de StarLoco : M018|<modérateur>;<br>raison ; le dernier M0 remplace le précédent, comme serverMessageID.
            Feed(account, "M018|Modo;<br>Triche");
            SystemMessage kick = account.PendingSystemMessage;
            Check(kick != null && kick.Id == 18 && kick.Args.SequenceEqual(new[] { "Modo", "<br>Triche" }), "M018 parameters were not read like the client");
            Check(kick.Text == "Vous avez été kické par Modo.\nTriche", "M018 text was not substituted without HTML: " + kick.Text);
            Feed(account, "M0xx|a");
            Check(ReferenceEquals(account.PendingSystemMessage, kick) && debug.Any(m => m.Contains("M0xx")), "An unreadable M0 replaced the kept message or was not logged");
            Check(new SystemMessage999Probe().Text == "Message du serveur n° 99 : a ; b", "Unknown SRV_MSG fallback changed: " + new SystemMessage999Probe().Text);

            // Le serveur ferme la connexion (kick) : perte de connexion en jeu, avec le message gardé.
            peer.Shutdown(SocketShutdown.Both); peer.Close();
            Until(() => losses.Count == 1, "The server closing the socket did not raise ConnectionLost");
            ConnectionLoss loss = losses.ToArray()[0];
            Check(loss.Cause == DisconnectCause.Lost && loss.WasInGame && ReferenceEquals(loss.Message, kick), "The lost connection lost its cause, game state or M0");
            // SIMPLE_AUTO_RECONNECT (vrai dans lang_fr_807, repli sans lang.xml) : la boîte finit par ATTEMPT_RECONNECT, en Oui / Non.
            Check(loss.Title == "Connexion" && loss.OffersReconnect && loss.Text == "Connexion interrompue avec le serveur.\n\nVous avez été kické par Modo.\nTriche"
                + "\n\nVoulez-vous tenter de vous reconnecter en jeu ?", "The lost-connection box text is not DISCONNECT, SRV_MSG_18 then ATTEMPT_RECONNECT: " + loss.Text);
            Check(account.PendingSystemMessage == null && account.ConnectionStatus == "Connexion perdue" && ReferenceEquals(account.LastDisconnect, loss),
                "The kept M0 or the status survived the disconnection: " + account.ConnectionStatus);
        }
        using (var account = Connected(listener, "synthetic-quit", out peer))
        using (peer)
        {
            int lost = 0;
            account.ConnectionLost += loss => lost++;
            account.Game.character.SetPerso_Data(7, "Quitte", 30, 1, 1);
            Feed(account, "M04");
            // Déconnexion demandée (bouton Déconnexion) : pas de boîte de perte, le M0 reste lisible dans LastDisconnect.
            account.Disconnect();
            Disconnected(peer, "Disconnect() did not close the socket");
            Check(lost == 0 && account.LastDisconnect.Cause == DisconnectCause.Requested && account.LastDisconnect.Message.Id == 4
                && account.PendingSystemMessage == null, "A requested disconnection raised ConnectionLost or kept the M0");
            Check(account.ConnectionStatus == "Déconnecté", "A requested disconnection changed the status: " + account.ConnectionStatus);
        }
        // SIMPLE_AUTO_RECONNECT lu dans lang.xml (Lang.getConfigText) : à faux, la boîte n'a plus de question (ERROR_BOX du client).
        string lang = Path.Combine(TestPaths.Work, "bot-session-lang"); Directory.CreateDirectory(lang);
        File.WriteAllText(Path.Combine(lang, "lang.xml"), "<?xml version='1.0' encoding='utf-8'?>\n<BotLang famille=\"lang\" langue=\"fr\" version=\"1\" source=\"lang_fr_1.swf\">\n"
            + "<texte cle=\"DISCONNECT\" valeur=\"Coupure (test)\" />\n<texte cle=\"ATTEMPT_RECONNECT\" valeur=\"Se reconnecter ? (test)\" />\n"
            + "<config cle=\"SIMPLE_AUTO_RECONNECT\" valeur=\"false\" />\n</BotLang>\n", new UTF8Encoding(false));
        try
        {
            Check(Tool_BotProtocol.Game.Data.LangData.Load(lang) == 1 && !ConnectionTexts.SimpleAutoReconnect && ConnectionTexts.AttemptReconnect == "Se reconnecter ? (test)",
                "SIMPLE_AUTO_RECONNECT or ATTEMPT_RECONNECT was not read from lang.xml");
            using (var account = Connected(listener, "synthetic-noquestion", out peer))
            using (peer)
            {
                var losses = new ConcurrentQueue<ConnectionLoss>();
                account.ConnectionLost += losses.Enqueue;
                account.Game.character.SetPerso_Data(7, "Sans question", 30, 1, 1);
                peer.Shutdown(SocketShutdown.Both);
                Until(() => losses.Count == 1, "The server closing the socket did not raise ConnectionLost");
                ConnectionLoss loss = losses.ToArray()[0];
                Check(!loss.OffersReconnect && loss.Text == "Coupure (test)", "SIMPLE_AUTO_RECONNECT=false still asked to reconnect: " + loss.Text);
            }
        }
        finally { Tool_BotProtocol.Game.Data.LangData.Clear(); }
    }

    /// <summary>Texte de repli d'un <c>SRV_MSG</c> inconnu, par le même chemin que <c>M0</c> (constructeur interne).</summary>
    private sealed class SystemMessage999Probe
    {
        internal string Text { get; }
        internal SystemMessage999Probe()
        {
            ConstructorInfo constructor = typeof(SystemMessage).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(int), typeof(string[]) }, null);
            Text = ((SystemMessage)constructor.Invoke(new object[] { 99, new[] { "a", "b" } })).Text;
        }
    }

    private static T Await<T>(Task<T> task) { Complete(task); return task.Result; }
}
