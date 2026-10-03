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

                    ActiveSocket(client).SendBufferSize = 2048;
                    string large = new string('x', 2 * 1024 * 1024) + "é\nfin";
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
                    Check(sentEvents == 2, "Successful send events were duplicated or omitted");

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
        Console.WriteLine("OK: loopback bot TCP fragments/coalescing/UTF-8, complete serialized sends, stale callbacks, send errors, failed connections and concurrent Dispose");
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
}
