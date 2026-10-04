using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Auth;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Utils.Crypto;
using BotClient = Tool_BotProtocol.Network.TcpClient;

internal static class BotHandshakeSmoke
{
    private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            string path = Path.Combine(TestPaths.ApplicationBin, new AssemblyName(args.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        Run().GetAwaiter().GetResult();
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task Within(Task task)
    {
        if (await Task.WhenAny(task, Task.Delay(8000)) != task) throw new TimeoutException("Local protocol test timed out");
        await task;
    }
    private static async Task<T> Within<T>(Task<T> task) { await Within((Task)task); return await task; }
    private static async Task Eventually(Func<bool> condition, string message)
    {
        DateTime until = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < until) await Task.Delay(10);
        Check(condition(), message);
    }
    private static Dictionary<string, string> Configuration()
    {
        return (Dictionary<string, string>)typeof(GlobalConfig).GetField("ConfigDico", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    }
    private static void Send(Socket peer, string packets)
    {
        byte[] data = Encoding.UTF8.GetBytes(packets);
        int sent = 0;
        while (sent < data.Length) sent += peer.Send(data, sent, data.Length - sent, SocketFlags.None);
    }
    private static Task<string> Read(Socket peer)
    {
        return Task.Run(() =>
        {
            using (var packet = new MemoryStream())
            {
                byte[] one = new byte[1];
                while (true)
                {
                    if (peer.Receive(one) != 1) throw new IOException("Bot closed the simulated server connection");
                    if (one[0] == 0) return Encoding.UTF8.GetString(packet.ToArray()).TrimEnd('\r', '\n');
                    packet.WriteByte(one[0]);
                }
            }
        });
    }
    private static async Task Expect(Socket peer, string expected)
    {
        string packet = await Within(Read(peer));
        Check(packet == expected, "Expected local client packet " + expected + ", received " + packet);
    }
    private static Socket Prepare(Socket peer) { peer.ReceiveTimeout = 7000; peer.SendTimeout = 7000; return peer; }
    private static Accounts Account() { return new Accounts(new AccountConfig("synthetic-account", "Test123!", "loopback")); }
    private static string DecryptLikeStarLoco(string packet, string key)
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_";
        Check(packet.StartsWith("#1"), "Missing password prefix");
        var result = new StringBuilder();
        for (int index = 2; index < packet.Length; index += 2)
        {
            int first = alphabet.IndexOf(packet[index]) + 64 - key[(index - 2) / 2];
            int second = alphabet.IndexOf(packet[index + 1]) + 64 - key[(index - 2) / 2];
            if (first < 0) first += 64;
            if (second < 0) second += 64;
            result.Append((char)(first * 16 + second));
        }
        return result.ToString();
    }
    public sealed class DispatchProbe
    {
        public int ShortCount;
        public int LongCount;
        public bool Finished;
        public bool ShortAfterLong;
        public void Short(BotClient client, string message) { ShortCount++; ShortAfterLong = Finished; }
        public async Task Long(BotClient client, string message) { await Task.Delay(30); LongCount++; Finished = true; }
        public async Task Fault(BotClient client, string message) { await Task.Delay(10); throw new InvalidOperationException("synthetic asynchronous error"); }
    }
    private static async Task Run()
    {
        GlobalConfig.BYPASS = false;
        Configuration()["version"] = "1.34.1";
        Configuration()["IP"] = "127.0.0.1";
        MessagesReception.messagesDatas.Clear();
        MessagesReception.Init();
        int count = MessagesReception.messagesDatas.Count;
        MessagesReception.Init();
        Check(count > 30 && MessagesReception.messagesDatas.Count == count, "Registry initialization is not idempotent");
        var probe = new DispatchProbe();
        MessagesReception.messagesDatas.Insert(0, new MessagesData(probe, "zz", typeof(DispatchProbe).GetMethod("Short")));
        MessagesReception.messagesDatas.Add(new MessagesData(probe, "zzlong", typeof(DispatchProbe).GetMethod("Long")));
        MessagesReception.messagesDatas.Add(new MessagesData(probe, "zzfault", typeof(DispatchProbe).GetMethod("Fault")));
        using (var account = Account())
        {
            var errors = new ConcurrentQueue<string>();
            account.Logger.log_event += (entry, color) => errors.Enqueue(entry.message);
            await MessagesReception.ReceptionAsync(account.Connexion, "zzlongpayload");
            Check(probe.ShortCount == 0 && probe.LongCount == 1 && probe.Finished,
                "The longest prefix or awaiting of asynchronous handlers failed");
            await MessagesReception.ReceptionAsync(account.Connexion, "zzfault");
            Check(errors.Any(error => error.Contains("synthetic asynchronous error")), "Asynchronous handler errors escaped logging");
        }
        string host;
        int port;
        Check(AccountLoginFrame.TryParseEndpoint("localhost:5555", out host, out port) && host == "localhost" && port == 5555, "DNS endpoint rejected");
        Check(AccountLoginFrame.TryParseEndpoint("[::1]:5555", out host, out port) && host == "::1", "Bracketed IPv6 rejected");
        Check(!AccountLoginFrame.TryParseEndpoint("[::1]:70000", out host, out port), "Out-of-range port accepted");
        Check(!AccountLoginFrame.TryParseEndpoint("localhost", out host, out port), "Endpoint without port accepted");
        Check(!AccountLoginFrame.TryParseEndpoint("[::1]5555", out host, out port), "Malformed IPv6 accepted");
        Check(!AccountLoginFrame.TryParseEndpoint("bad:host:5555", out host, out port), "Invalid DNS hostname accepted");
        const string passwordKey = "abcdefghijklmnopqrstuvwxyzabcdef";
        foreach (string password in new[] { " Test123! ", "12345678901234567890123456789012" })
            Check(DecryptLikeStarLoco(Hash.Crypt_Password(password, passwordKey), passwordKey) == password,
                "Password spaces or a 32-character password were modified");
        await ReceiveOrdering(probe);
        await FullHandshake();
        await EncodedRedirect();
        if (Socket.OSSupportsIPv6) await Ipv6Redirect();
        await FailedRedirectAndReconnect();
        await Refusals();
        Console.WriteLine("OK: StarLoco loopback Login to Game handshake, encrypted credentials, long subscriptions, repeated server updates, character selection, async longest-prefix dispatch and malformed/refused packets");
    }
    private static async Task ReceiveOrdering(DispatchProbe probe)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            using (var account = Account())
            {
                Task<Socket> accepted = listener.AcceptSocketAsync();
                await account.Connexion.ConnectToServer(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
                using (Socket peer = Prepare(await Within(accepted)))
                {
                    probe.Finished = false;
                    Send(peer, "zzlongnetwork\0zzshortnetwork\0");
                    await Eventually(() => probe.ShortCount == 1, "Coalesced asynchronous packets were not dispatched");
                    Check(probe.LongCount == 2 && probe.ShortAfterLong, "Receive dispatched the next packet before the asynchronous handler completed");
                }
            }
        }
        finally { listener.Stop(); }
    }
    private static async Task FullHandshake()
    {
        var login = new TcpListener(IPAddress.Loopback, 0);
        var world = new TcpListener(IPAddress.Loopback, 0);
        login.Start(); world.Start();
        Configuration()["authport"] = ((IPEndPoint)login.LocalEndpoint).Port.ToString();
        try
        {
            using (var account = Account())
            {
                Task<Socket> loginAccepted = login.AcceptSocketAsync();
                await Within(account.ConnectAsync());
                using (Socket loginPeer = Prepare(await Within(loginAccepted)))
                {
                    const string key = "abcdefghijklmnopqrstuvwxyzabcdef";
                    Send(loginPeer, "HC" + key.Substring(0, 5));
                    await Task.Delay(25);
                    Send(loginPeer, key.Substring(5) + "\0");
                    await Expect(loginPeer, "1.34.1");
                    await Expect(loginPeer, "synthetic-account");
                    string crypted = await Within(Read(loginPeer));
                    Check(DecryptLikeStarLoco(crypted, key) == "Test123!", "Password does not match the StarLoco decoder");
                    await Expect(loginPeer, "Af");
                    Send(loginPeer, "Af12|345|0|1|-1\0AdTestPseudo\0AH601;1;110;1|602;0;110;1\0AQsynthetic%20question\0");
                    await Expect(loginPeer, "Ax");
                    Check(account.SecretQuestion == "synthetic question", "Secret question was not decoded for the character deletion form");
                    Send(loginPeer, "AxK8640000000|601,2\0AH601;1;110;1|602;1;110;1\0");
                    await Eventually(() => account.AboTime == 8640000000L && account.Game.Server.Servers[602] == Tool_BotProtocol.Network.Enums.ServerStates.ONLINE,
                        "Subscription overflowed or repeated AH update failed");
                    Check(account.Name == "TestPseudo" && account.accountConfig.Servers.SequenceEqual(new[] { "601,2" }), "Login information was parsed incorrectly");
                    Task<Socket> gameAccepted = world.AcceptSocketAsync();
                    Send(loginPeer, "AYKlocalhost:" + ((IPEndPoint)world.LocalEndpoint).Port + ";42\0AdStaleLoginName\0AxK1|999,0\0");
                    using (Socket gamePeer = Prepare(await Within(gameAccepted)))
                    {
                        Send(gamePeer, "HG\0");
                        await Expect(gamePeer, "AT42");
                        Check(account.Name == "TestPseudo" && account.AboTime == 8640000000L,
                            "Stale Login packets overwrote the Game session after redirection");
                        Send(gamePeer, "ATK0\0");
                        await Expect(gamePeer, "Ak0");
                        await Expect(gamePeer, "AV");
                        Send(gamePeer, "AV0\0");
                        await Expect(gamePeer, "Agfr");
                        await Expect(gamePeer, "AL");
                        await Expect(gamePeer, "Af");
                        Send(gamePeer, "ALK8640000000|2|7;Premier;50;10;ffffff;ffffff;ffffff;;0;601;0|8;Second;120;20;0;0;0;;0;601;0\0");
                        await Eventually(() => account.AccountCharactersInfo.Count == 2, "Characters did not populate after ALK");
                        await account.Connexion.SendPacket("AS8");
                        await Expect(gamePeer, "AS8");
                        Send(gamePeer, "ASK|8|Second|120|2|1|20|0|0|0|\0");
                        // Like the 1.34 client: GC1 only (BYA would mark the character away on StarLoco).
                        await Expect(gamePeer, "GC1");
                        Check(account.Game.character.id == 8 && account.Game.character.Name == "Second" && account.Game.character.Level == 120 && account.Game.character.Sex == 1,
                            "ASK did not apply the selected character");
                        Check(account.AccountStates == AccountStates.CONNECTED_INACTIVE, "The selected character was not connected");
                        Send(gamePeer, "ALK86400000|0\0");
                        await Eventually(() => account.AccountCharactersInfo.Count == 0, "Empty character list kept stale characters");
                    }
                }
            }
        }
        finally { login.Stop(); world.Stop(); }
    }
    private static async Task EncodedRedirect()
    {
        var login = new TcpListener(IPAddress.Loopback, 0);
        var world = new TcpListener(IPAddress.Loopback, 0);
        login.Start(); world.Start();
        Configuration()["authport"] = ((IPEndPoint)login.LocalEndpoint).Port.ToString();
        try
        {
            using (var account = Account())
            {
                Task<Socket> accepted = login.AcceptSocketAsync();
                await Within(account.ConnectAsync());
                using (Socket peer = Prepare(await Within(accepted)))
                {
                    int gamePort = ((IPEndPoint)world.LocalEndpoint).Port;
                    const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_";
                    string encodedPort = "" + alphabet[gamePort / 4096] + alphabet[(gamePort / 64) % 64] + alphabet[gamePort % 64];
                    Task<Socket> gameAccepted = world.AcceptSocketAsync();
                    Send(peer, "AXK7?000001" + encodedPort + "99\0");
                    using (Socket gamePeer = Prepare(await Within(gameAccepted)))
                    {
                        Send(gamePeer, "HG\0");
                        await Expect(gamePeer, "AT99");
                    }
                }
            }
        }
        finally { login.Stop(); world.Stop(); }
    }
    private static async Task Refusals()
    {
        foreach (string packet in new[] { "AYKbad", "AYKbad host:5555;42", "AXKshort", "ASK|broken", "ASK|8|Second|120|2|1|20|0|0|0|invalid-item", "ATE", "AlEf", "AlEb", "AlEk12|3|4", "AlEkbad", "AlEr", "AXEd", "AXEf" })
        {
            using (var account = Account())
            {
                var messages = new List<string>();
                account.Logger.log_event += (entry, color) => messages.Add(entry.message);
                await MessagesReception.ReceptionAsync(account.Connexion, packet);
                Check(account.Connexion == null && account.AccountStates == AccountStates.DISCONNECTED, "Refused/malformed packet did not disconnect: " + packet);
                Check(messages.Count > 0, "Refused/malformed packet lacked a diagnostic: " + packet);
                if (packet == "AlEk12|3|4") Check(messages.Any(message => message.Contains("12 jour(s)") && message.Contains("3 heure(s)") && message.Contains("4 minute(s)")), "Ban duration parsed incorrectly");
            }
        }
        using (var account = Account())
        {
            await MessagesReception.ReceptionAsync(account.Connexion, "Af");
            await MessagesReception.ReceptionAsync(account.Connexion, "AxKbad");
            await MessagesReception.ReceptionAsync(account.Connexion, "ALKbad");
            Check(account.Connexion != null, "Recoverable malformed informational packet destroyed the account");
            account.Game.Server.ExitCreationMenu = true;
            account.Game.Server.NameNewCharacter = "Missing";
            await MessagesReception.ReceptionAsync(account.Connexion, "ALK86400000|0");
            Check(!account.Game.Server.ExitCreationMenu, "Missing newly created character attempted AS0 or kept the creation flag");
        }
    }
    private static async Task Ipv6Redirect()
    {
        var login = new TcpListener(IPAddress.Loopback, 0);
        var world = new TcpListener(IPAddress.IPv6Loopback, 0);
        login.Start(); world.Start();
        Configuration()["authport"] = ((IPEndPoint)login.LocalEndpoint).Port.ToString();
        try
        {
            using (var account = Account())
            {
                Task<Socket> accepted = login.AcceptSocketAsync();
                await Within(account.ConnectAsync());
                using (Socket peer = Prepare(await Within(accepted)))
                {
                    Task<Socket> gameAccepted = world.AcceptSocketAsync();
                    Send(peer, "AYK[::1]:" + ((IPEndPoint)world.LocalEndpoint).Port + ";77\0");
                    using (Socket gamePeer = Prepare(await Within(gameAccepted)))
                    {
                        Send(gamePeer, "HG\0");
                        await Expect(gamePeer, "AT77");
                    }
                }
            }
        }
        finally { login.Stop(); world.Stop(); }
    }
    private static async Task FailedRedirectAndReconnect()
    {
        var login = new TcpListener(IPAddress.Loopback, 0);
        var closed = new TcpListener(IPAddress.Loopback, 0);
        login.Start(); closed.Start();
        int closedPort = ((IPEndPoint)closed.LocalEndpoint).Port;
        closed.Stop();
        Configuration()["authport"] = ((IPEndPoint)login.LocalEndpoint).Port.ToString();
        try
        {
            using (var account = Account())
            {
                Task<Socket> accepted = login.AcceptSocketAsync();
                await Within(account.ConnectAsync());
                BotClient original = account.Connexion;
                using (Socket peer = Prepare(await Within(accepted)))
                {
                    account.Name = "BeforeFailedRedirect";
                    Send(peer, "AYK127.0.0.1:" + closedPort + ";55\0AdStaleFailedLogin\0");
                    await Eventually(() => account.Connexion == null && account.GameTicket == string.Empty,
                        "Failed Game connect retained its transport or ticket after cleanup");
                    Check(account.AccountStates == AccountStates.DISCONNECTED && account.GameTicket == string.Empty,
                        "Failed Game connect retained connected state or ticket");
                    Check(account.ConnectionStatus.Contains("Connexion impossible") && account.Name == "BeforeFailedRedirect",
                        "Failed Game redirect lost its status or applied stale Login data");
                }
                Task<Socket> reaccepted = login.AcceptSocketAsync();
                await Within(account.ConnectAsync());
                using (Socket peer = Prepare(await Within(reaccepted)))
                {
                    Check(account.Connexion != null && account.Connexion != original && account.AccountStates == AccountStates.CONNECTED,
                        "Account could not reconnect after a refused Game port");
                    // The platform rejects whitespace in DNS hostnames locally; no DNS traffic is needed.
                    await Within(account.SwitchToGameServerAsync("bad host", 5555));
                    Check(account.Connexion == null && account.AccountStates == AccountStates.DISCONNECTED &&
                        account.ConnectionStatus.Contains("Connexion impossible"), "Invalid DNS endpoint retained a connected account");
                }
            }
        }
        finally { login.Stop(); }
    }
}
