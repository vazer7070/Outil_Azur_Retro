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
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using BotClient = Tool_BotProtocol.Network.TcpClient;

internal static class BotTransportSmoke
{
    private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            string name = new AssemblyName(args.Name).Name;
            string path = Path.Combine(TestPaths.ApplicationBin, name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        Run().GetAwaiter().GetResult();
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    private static async Task Within(Task task, string message)
    {
        if (await Task.WhenAny(task, Task.Delay(8000)) != task) throw new TimeoutException(message);
        await task;
    }

    private static async Task<T> Within<T>(Task<T> task, string message)
    {
        await Within((Task)task, message);
        return await task;
    }

    private static async Task Eventually(Func<bool> condition, string message)
    {
        DateTime limit = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < limit) await Task.Delay(10);
        Check(condition(), message);
    }

    private static async Task<Socket> OpenPeer(BotClient client)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            Task<Socket> accepted = listener.AcceptSocketAsync();
            await Within(client.ConnectToServer(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port),
                "Loopback connection did not complete");
            Socket peer = await Within(accepted, "Loopback peer was not accepted");
            peer.ReceiveTimeout = 5000;
            peer.SendTimeout = 5000;
            Check(client.IsConnected(), "The newly accepted bot socket is disconnected");
            return peer;
        }
        finally { listener.Stop(); }
    }

    private static Socket ActiveSocket(BotClient client)
    {
        object session = typeof(BotClient).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(client);
        return (Socket)session.GetType().GetField("Socket", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(session);
    }

    private static void SendAll(Socket peer, byte[] data, int offset, int count)
    {
        int end = offset + count;
        while (offset < end)
        {
            int sent = peer.Send(data, offset, end - offset, SocketFlags.None);
            Check(sent > 0, "Loopback server send made no progress");
            offset += sent;
        }
    }

    private static byte[] ReadExact(Socket peer, int length)
    {
        byte[] result = new byte[length];
        int offset = 0;
        while (offset < length)
        {
            int read = peer.Receive(result, offset, Math.Min(length - offset, 8192), SocketFlags.None);
            Check(read > 0, "The bot disconnected before the full packet arrived");
            offset += read;
        }
        return result;
    }

    private static async Task Run()
    {
        GlobalConfig.BYPASS = false;
        MessagesReception.messagesDatas.Clear();
        var unused = new BotClient(null);
        unused.DisconnectSocket();
        unused.Dispose();
        unused.Dispose();
        await Within(unused.SendPacketAsync("unused"), "Send after Dispose blocked");
        Check(!unused.IsConnected(), "Unused disposed bot remains connected");

        using (var client = new BotClient(null))
        {
            var received = new ConcurrentQueue<string>();
            int sentEvents = 0;
            client.packetReceivedEvent += received.Enqueue;
            client.packetSendEvent += packet => System.Threading.Interlocked.Increment(ref sentEvents);
            using (Socket first = await OpenPeer(client))
            {
                byte[] packets = Encoding.UTF8.GetBytes("zzé\nligne\r\n\0zzDeux\n\0zzTrois\0");
                // The first fragment ends inside the two-byte UTF-8 character.
                SendAll(first, packets, 0, 3);
                await Task.Delay(40);
                Check(received.IsEmpty, "An incomplete packet was dispatched before its NUL");
                SendAll(first, packets, 3, packets.Length - 3);
                await Eventually(() => received.Count == 3, "Coalesced packets were not all dispatched");
                Check(received.ToArray().SequenceEqual(new[] { "zzé\nligne", "zzDeux", "zzTrois" }),
                    "UTF-8 fragments, internal LF or terminal CRLF were changed");

                byte[] incomplete = Encoding.UTF8.GetBytes("zzancien-sans-zero");
                SendAll(first, incomplete, 0, incomplete.Length);
                await Task.Delay(40);
                Socket oldSocket = ActiveSocket(client);
                using (Socket second = await OpenPeer(client))
                {
                    bool oldClosed = false;
                    try { int ignored = oldSocket.Available; }
                    catch (ObjectDisposedException) { oldClosed = true; }
                    Check(oldClosed, "Reconnection retained the previous socket");
                    byte[] fresh = Encoding.UTF8.GetBytes("zzneuf\0");
                    SendAll(second, fresh, 0, fresh.Length);
                    await Eventually(() => received.Count == 4, "Reconnection stopped reception");
                    Check(received.ToArray()[3] == "zzneuf", "An old incomplete frame contaminated the new session");

                    // One packet per send (StarLoco splits each send on LF): multi-line or NUL packets never leave.
                    var rejected = new ConcurrentQueue<string>();
                    int sentPackets = 0;
                    client.PacketRejected += (packet, reason) => rejected.Enqueue(packet);
                    client.PacketSent += packet => System.Threading.Interlocked.Increment(ref sentPackets);
                    await Within(client.SendPacketAsync("zzune\nzzdeux"), "A rejected send blocked");
                    await Within(client.SendPacketAsync("zznul\0zz"), "A rejected send blocked");
                    await Within(client.SendPacketAsync(""), "A rejected send blocked");
                    await Task.Delay(40);
                    Check(rejected.ToArray().SequenceEqual(new[] { "zzune\nzzdeux", "zznul\0zz", "" }) && second.Available == 0 && sentEvents == 0,
                        "A multi-line, NUL or empty packet reached the server");

                    ActiveSocket(client).SendBufferSize = 2048;
                    string large = new string('x', 2 * 1024 * 1024) + "é fin";
                    byte[] expectedLarge = Encoding.UTF8.GetBytes(large + "\n\0");
                    byte[] expectedNext = Encoding.UTF8.GetBytes("zzapres\n\0");
                    Task<byte[]> readLarge = Task.Run(() => ReadExact(second, expectedLarge.Length));
                    Task sendLarge = client.SendPacketAsync(large);
                    Task sendNext = client.SendPacketAsync("zzapres");
                    byte[] actualLarge = await Within(readLarge, "A large bot send was partial");
                    Check(actualLarge.SequenceEqual(expectedLarge), "Large packet bytes were truncated or interleaved");
                    Check(ReadExact(second, expectedNext.Length).SequenceEqual(expectedNext),
                        "The next queued packet was truncated or interleaved");
                    await Within(Task.WhenAll(sendLarge, sendNext), "Serialized sends did not finish");
                    Check(sentEvents == 2 && sentPackets == 2, "Successful send events were duplicated or omitted");

                    string blocked = new string('y', 8 * 1024 * 1024);
                    Task failed = client.SendPacketAsync(blocked);
                    Task stale = client.SendPacketAsync("stale");
                    await Task.Delay(40);
                    second.LingerState = new LingerOption(true, 0);
                    second.Close();
                    using (Socket third = await OpenPeer(client))
                    {
                        await Within(Task.WhenAll(failed, stale), "A failed send retained the semaphore");
                        byte[] probe = Encoding.UTF8.GetBytes("zzapres-erreur\n\0");
                        await Within(client.SendPacketAsync("zzapres-erreur"), "A reconnect send blocked after an error");
                        Check(ReadExact(third, probe.Length).SequenceEqual(probe),
                            "A stale queued send reached the new connection");
                        Check(client.IsConnected(), "An old send/receive callback closed the new session");

                        Task disposing = client.SendPacketAsync(blocked);
                        Task queued = client.SendPacketAsync("queued");
                        await Task.Delay(40);
                        client.Dispose();
                        client.Dispose();
                        await Within(Task.WhenAll(disposing, queued), "Dispose stranded a send or its semaphore waiter");
                        Check(!client.IsConnected(), "Dispose kept the live socket connected");
                    }
                }
            }
        }

        using (var client = new BotClient(null))
        {
            var reservation = new TcpListener(IPAddress.Loopback, 0);
            reservation.Start();
            int closedPort = ((IPEndPoint)reservation.LocalEndpoint).Port;
            reservation.Stop();
            await Within(client.ConnectToServer(IPAddress.Loopback, closedPort), "Failed connect did not clean up");
            Check(!client.IsConnected(), "Failed connect reports a live connection");
            using (Socket peer = await OpenPeer(client))
            {
                byte[] invalid = { 0xc3, 0x28, 0 };
                SendAll(peer, invalid, 0, invalid.Length);
                await Eventually(() => !client.IsConnected(), "Malformed UTF-8 was accepted by the transport");
            }
            using (Socket peer = await OpenPeer(client))
            {
                TaskCompletionSource<string> packet = new TaskCompletionSource<string>();
                client.packetReceivedEvent += value => packet.TrySetResult(value);
                byte[] fresh = Encoding.UTF8.GetBytes("zzretour\0");
                SendAll(peer, fresh, 0, fresh.Length);
                Check(await Within(packet.Task, "Reception did not recover after malformed UTF-8") == "zzretour",
                    "Malformed-frame state leaked into the next session");
            }
        }
        await AccountLifecycle();
        await AccountSendFailure();
        await TicketRetryAfterEarlyGameClose();
        await TicketRetryAfterAte();
        await NoSessionOnDiscardedTransport();
        Console.WriteLine("OK: loopback bot TCP fragments/coalescing/UTF-8, complete serialized sends, stale callbacks, send errors, failed connections, concurrent Dispose, ticket resent after an early Game close or ATE, and no session opened on a discarded transport");
    }

    private static async Task AccountLifecycle()
    {
        var configuration = (Dictionary<string, string>)typeof(GlobalConfig)
            .GetField("ConfigDico", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        configuration["IP"] = "127.0.0.1";
        var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        int closedPort = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        configuration["authport"] = closedPort.ToString();
        using (var account = new Accounts(new AccountConfig("local-test", "synthetic-not-used", "test")))
        {
            TaskCompletionSource<bool> failed = new TaskCompletionSource<bool>();
            account.Connexion.socketInformationEvent += info => failed.TrySetResult(true);
            account.Connect();
            await Within(failed.Task, "Account connect failure was not reported");
            await Task.Delay(30);
            Check(account.AccountStates == AccountStates.DISCONNECTED,
                "Account Connect marked a failed connection as CONNECTED");
            account.Disconnect();
            Check(account.Connexion == null, "Account Disconnect retained its transport");

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                configuration["authport"] = ((IPEndPoint)listener.LocalEndpoint).Port.ToString();
                Task<Socket> accepted = listener.AcceptSocketAsync();
                account.Connect();
                using (Socket peer = await Within(accepted, "Account did not recreate its transport after Disconnect"))
                {
                    await Eventually(() => account.AccountStates == AccountStates.CONNECTED,
                        "Account did not report its successful local connection");
                    BotClient transport = account.Connexion;
                    TaskCompletionSource<bool> disconnected = new TaskCompletionSource<bool>();
                    account.AccountDisconnectEvent += () =>
                    {
                        if (!Task.Run(() => transport.IsConnected()).Wait(2000))
                            disconnected.TrySetException(new Exception("Account callback holds the transport lock"));
                        else disconnected.TrySetResult(true);
                    };
                    peer.Close();
                    await Within(disconnected.Task, "Account disconnect callback blocked");
                    Check(account.Connexion == null && account.AccountStates == AccountStates.DISCONNECTED,
                        "Remote close left the account connected");
                }
            }
            finally { listener.Stop(); }
        }
    }

    private static async Task AccountSendFailure()
    {
        var configuration = (Dictionary<string, string>)typeof(GlobalConfig)
            .GetField("ConfigDico", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        configuration["IP"] = "127.0.0.1";
        configuration["authport"] = ((IPEndPoint)listener.LocalEndpoint).Port.ToString();
        try
        {
            using (var account = new Accounts(new AccountConfig("local-send-test", "synthetic-not-used", "test")))
            using (var resume = new System.Threading.ManualResetEventSlim(false))
            {
                Task<Socket> accepted = listener.AcceptSocketAsync();
                account.Connect();
                using (Socket peer = await Within(accepted, "Account send-failure peer was not accepted"))
                {
                    await Eventually(() => account.AccountStates == AccountStates.CONNECTED,
                        "Account send-failure setup was not connected");
                    BotClient transport = account.Connexion;
                    TaskCompletionSource<bool> receiving = new TaskCompletionSource<bool>();
                    TaskCompletionSource<bool> finished = new TaskCompletionSource<bool>();
                    TaskCompletionSource<bool> disconnected = new TaskCompletionSource<bool>();
                    transport.packetReceivedEvent += packet =>
                    {
                        receiving.TrySetResult(true);
                        resume.Wait(5000);
                        finished.TrySetResult(true);
                    };
                    account.AccountDisconnectEvent += () => disconnected.TrySetResult(true);
                    byte[] block = Encoding.UTF8.GetBytes("zzpause\0");
                    SendAll(peer, block, 0, block.Length);
                    try
                    {
                        await Within(receiving.Task, "Receive callback did not enter the loopback barrier");
                        // No receive is pending while the packet subscriber is paused: force the send error path.
                        ActiveSocket(transport).Dispose();
                        await Within(transport.SendPacketAsync("zzfailed-send"), "The failed account send blocked");
                        await Within(disconnected.Task, "Send failure did not notify account disconnection");
                        Check(account.Connexion == null && account.AccountStates == AccountStates.DISCONNECTED,
                            "Send failure left the real account connected");
                    }
                    finally { resume.Set(); }
                    await Within(finished.Task, "Paused receive callback did not finish after failed send");
                }
            }
        }
        finally { listener.Stop(); }
    }
    private static async Task<string> ReadPacket(Socket peer)
    {
        return await Within(Task.Run(() =>
        {
            using (var packet = new MemoryStream())
            {
                byte[] one = new byte[1];
                while (true)
                {
                    if (peer.Receive(one) != 1) throw new IOException("The bot closed the simulated server connection");
                    if (one[0] == 0) return Encoding.UTF8.GetString(packet.ToArray()).TrimEnd('\r', '\n');
                    packet.WriteByte(one[0]);
                }
            }
        }), "No packet from the bot");
    }

    private static void SendText(Socket peer, string packets) { byte[] data = Encoding.UTF8.GetBytes(packets); SendAll(peer, data, 0, data.Length); }

    /// <summary>
    /// Serveur de jeu qui ferme la connexion après <c>AT</c> sans <c>ATK</c>, comme StarLoco quand le compte en attente
    /// (<c>WA</c> du Login) n'est pas encore enregistré : le bot se reconnecte au même serveur avec le même transport et
    /// renvoie le ticket ; une fois <c>ATK</c> reçu, une fermeture déconnecte le compte.
    /// </summary>
    private static async Task TicketRetryAfterEarlyGameClose()
    {
        MessagesReception.messagesDatas.Clear();
        MessagesReception.Init();
        var configuration = (Dictionary<string, string>)typeof(GlobalConfig)
            .GetField("ConfigDico", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        var login = new TcpListener(IPAddress.Loopback, 0);
        var world = new TcpListener(IPAddress.Loopback, 0);
        login.Start(); world.Start();
        configuration["IP"] = "127.0.0.1";
        configuration["authport"] = ((IPEndPoint)login.LocalEndpoint).Port.ToString();
        int previousDelay = Accounts.TicketRetryDelayMs;
        Accounts.TicketRetryDelayMs = 50;
        try
        {
            using (var account = new Accounts(new AccountConfig("local-ticket-test", "synthetic-not-used", "test")))
            {
                var log = new ConcurrentQueue<string>();
                account.Logger.log_event += (entry, color) => log.Enqueue(entry.message);
                Task<Socket> accepted = login.AcceptSocketAsync();
                await Within(account.ConnectAsync(), "Login connection did not complete");
                BotClient transport = account.Connexion;
                using (Socket peer = await Within(accepted, "Login peer was not accepted"))
                {
                    peer.ReceiveTimeout = 7000;
                    Task<Socket> first = world.AcceptSocketAsync();
                    SendText(peer, "AYK127.0.0.1:" + ((IPEndPoint)world.LocalEndpoint).Port + ";88\0");
                    using (Socket refused = await Within(first, "Game connection was not opened"))
                    {
                        refused.ReceiveTimeout = 7000;
                        Task<Socket> second = world.AcceptSocketAsync();
                        SendText(refused, "HG\0");
                        Check(await ReadPacket(refused) == "AT88", "The ticket was not sent on HG");
                        refused.Shutdown(SocketShutdown.Both);
                        refused.Close();
                        using (Socket game = await Within(second, "The bot did not reconnect after an early Game close"))
                        {
                            game.ReceiveTimeout = 7000;
                            Check(account.Connexion == transport && account.GameTicket == "88", "Early Game close dropped the account instead of retrying the ticket");
                            SendText(game, "HG\0");
                            Check(await ReadPacket(game) == "AT88", "The ticket was not resent after reconnection");
                            SendText(game, "ATK0\0");
                            Check(await ReadPacket(game) == "Ak0" && await ReadPacket(game) == "AV", "ATK0 after the retry was not answered");
                            Check(log.Any(line => line.Contains("nouvel essai 1/")), "The ticket retry was not logged");
                            // Ticket accepté : une fermeture du serveur de jeu déconnecte désormais le compte.
                            game.Shutdown(SocketShutdown.Both);
                            await Eventually(() => account.Connexion == null && account.AccountStates == AccountStates.DISCONNECTED,
                                "A Game close after ATK must disconnect the account");
                        }
                    }
                }
            }
        }
        finally { Accounts.TicketRetryDelayMs = previousDelay; login.Stop(); world.Stop(); }
    }

    /// <summary>
    /// StarLoco répond <c>ATE</c> puis ferme la session (<c>kick</c>) quand le compte en attente (<c>WA</c> du Login) n'est pas
    /// encore arrivé : tant qu'il reste des essais, le bot renvoie le ticket sur une seule nouvelle connexion au lieu d'échouer ;
    /// les essais épuisés, <c>ATE</c> fait échouer la connexion avec son message.
    /// </summary>
    private static async Task TicketRetryAfterAte()
    {
        MessagesReception.messagesDatas.Clear();
        MessagesReception.Init();
        var configuration = (Dictionary<string, string>)typeof(GlobalConfig)
            .GetField("ConfigDico", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        var login = new TcpListener(IPAddress.Loopback, 0);
        var world = new TcpListener(IPAddress.Loopback, 0);
        login.Start(); world.Start();
        configuration["IP"] = "127.0.0.1";
        configuration["authport"] = ((IPEndPoint)login.LocalEndpoint).Port.ToString();
        int previousDelay = Accounts.TicketRetryDelayMs;
        Accounts.TicketRetryDelayMs = 50;
        Socket game = null;
        try
        {
            using (var account = new Accounts(new AccountConfig("local-ate-test", "synthetic-not-used", "test")))
            {
                var log = new ConcurrentQueue<string>();
                account.Logger.log_event += (entry, color) => log.Enqueue(entry.message);
                Task<Socket> accepted = login.AcceptSocketAsync();
                await Within(account.ConnectAsync(), "Login connection did not complete");
                BotClient transport = account.Connexion;
                using (Socket peer = await Within(accepted, "Login peer was not accepted"))
                {
                    Task<Socket> next = world.AcceptSocketAsync();
                    SendText(peer, "AYK127.0.0.1:" + ((IPEndPoint)world.LocalEndpoint).Port + ";77\0");
                    game = await Within(next, "Game connection was not opened");
                    for (int attempt = 1; ; attempt++)
                    {
                        game.ReceiveTimeout = 7000;
                        next = world.AcceptSocketAsync();
                        await Task.Delay(250);
                        Check(!next.IsCompleted, "One ATE opened more than one new Game connection");
                        SendText(game, "HG\0");
                        Check(await ReadPacket(game) == "AT77", "The ticket was not sent on HG (connection " + attempt + ")");
                        SendText(game, "ATE\0");
                        if (attempt > Accounts.TicketRetryLimit) break;
                        game.Shutdown(SocketShutdown.Both); // kick de StarLoco juste après ATE
                        Socket reconnected = await Within(next, "ATE " + attempt + " did not resend the ticket on a new connection");
                        game.Dispose();
                        game = reconnected;
                        Check(account.Connexion == transport && account.GameTicket == "77"
                            && !account.ConnectionStatus.StartsWith("Connexion impossible", StringComparison.Ordinal),
                            "ATE during the WA/AT race failed the account instead of resending the ticket");
                        Check(log.Any(line => line.Contains("ATE") && line.Contains("nouvel essai " + attempt + "/" + Accounts.TicketRetryLimit)),
                            "The ticket retry after ATE was not logged (attempt " + attempt + ")");
                    }
                    await Eventually(() => account.Connexion == null && account.AccountStates == AccountStates.DISCONNECTED,
                        "ATE with no retry left did not fail the connection");
                    Check(account.ConnectionStatus.Contains("refusé le ticket"), "Final ATE lost its reason: " + account.ConnectionStatus);
                    await Task.Delay(300);
                    Check(!next.IsCompleted, "The bot retried the ticket beyond TicketRetryLimit");
                }
            }
        }
        finally { game?.Dispose(); Accounts.TicketRetryDelayMs = previousDelay; login.Stop(); world.Stop(); }
    }

    /// <summary>
    /// Fermeture distante sans ticket en cours : la décision (nouvel essai du ticket ou déconnexion du compte) est prise sous le
    /// verrou qui remet la session à zéro. Un <c>ConnectToServer</c> lancé pendant la fermeture (ici depuis le journal du transport,
    /// appelé juste après la remise à zéro) est refusé au lieu d'ouvrir une session sur le transport que le compte va jeter.
    /// </summary>
    private static async Task NoSessionOnDiscardedTransport()
    {
        var configuration = (Dictionary<string, string>)typeof(GlobalConfig)
            .GetField("ConfigDico", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        var login = new TcpListener(IPAddress.Loopback, 0);
        var stray = new TcpListener(IPAddress.Loopback, 0);
        login.Start(); stray.Start();
        configuration["IP"] = "127.0.0.1";
        configuration["authport"] = ((IPEndPoint)login.LocalEndpoint).Port.ToString();
        int strayPort = ((IPEndPoint)stray.LocalEndpoint).Port;
        try
        {
            using (var account = new Accounts(new AccountConfig("local-race-test", "synthetic-not-used", "test")))
            {
                Task<Socket> accepted = login.AcceptSocketAsync();
                await Within(account.ConnectAsync(), "Login connection did not complete");
                BotClient transport = account.Connexion;
                Task<Socket> strayAccepted = stray.AcceptSocketAsync();
                var concurrent = new TaskCompletionSource<Task>();
                transport.socketInformationEvent += info =>
                {
                    if (info == "Socket déconnecté de l'hôte")
                        concurrent.TrySetResult(transport.ConnectToServer(IPAddress.Loopback, strayPort));
                };
                using (Socket peer = await Within(accepted, "Login peer was not accepted"))
                {
                    peer.Close();
                    Task attempt = await Within(concurrent.Task, "The remote close was not reported by the transport");
                    await Eventually(() => account.Connexion == null && account.AccountStates == AccountStates.DISCONNECTED,
                        "Remote close without a ticket did not disconnect the account");
                    Check(attempt.IsFaulted && attempt.Exception.InnerException is ObjectDisposedException,
                        "A connection started during the account disconnection was not refused");
                    await Task.Delay(200);
                    Check(!strayAccepted.IsCompleted, "The discarded transport opened a session on another server");
                }
            }
        }
        finally { login.Stop(); stray.Stop(); }
    }
}
