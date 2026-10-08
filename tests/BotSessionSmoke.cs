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
using Tool_BotProtocol.Game.Perso;
using Tool_BotProtocol.Game.Session;
using BotClient = Tool_BotProtocol.Network.TcpClient;

// Session layer of the bot against a local fake server (no real server, no UI): handler registry, unknown packets,
// one packet per send, GC1 alone, GCK/AR/Ac/BT/BN/AN, generic Im, invitations without auto-accept, split frames.
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
        try { Run(); Console.WriteLine("OK: registre strict, paquets inconnus journalisés, un paquet par envoi, GC1 seul, GCK/AR/Ac/BT/BN/AN, Im générique, PIK/gJr sans acceptation, frames éclatées"); }
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
                    Feed(account, "AR3K"); Check(session.RawRestrictions == 128 && session.Restrictions == PlayerRestrictions.Tomb, "AR3K (upper case) is not the tomb bit");
                    Feed(account, "AR6bK"); Check(session.RawRestrictions == 8192, "Upper-case base 36 was refused");
                    Feed(account, "AR!?"); Check(session.RawRestrictions == 8192, "Unreadable AR replaced the restrictions");
                    Check(new Personnages(5, "Autre", 0, null).Restrictions == PlayerRestrictions.None, "Other players do not default to no restriction");
                    Feed(account, "Ac0"); Feed(account, "Ac-1"); Check(session.CommunityId == 0, "Ac was not read like the client");
                    int level = 0; session.LevelUp += value => level = value;
                    Feed(account, "AN121"); Check(account.Game.character.Level == 121 && level == 121, "AN<level> did not update the level");
                    Feed(account, "ANxx"); Check(account.Game.character.Level == 121, "Unreadable AN changed the level");

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
        }
        finally { listener.Stop(); Environment.CurrentDirectory = previous; }
    }
}
