using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Tools_protocol.Network
{
    public sealed class CapturedPacket
    {
        public DateTime Timestamp { get; private set; }
        public int ConnectionId { get; private set; }
        public bool FromClient { get; private set; }
        public string Text { get; private set; }
        public bool Redacted { get; private set; }

        internal CapturedPacket(int connectionId, bool fromClient, string text, bool redacted)
        {
            Timestamp = DateTime.UtcNow;
            ConnectionId = connectionId;
            FromClient = fromClient;
            Text = text;
            Redacted = redacted;
        }
    }

    /// <summary>
    /// Explicit local TCP relay for a manually configured client. Binds IPv4 loopback,
    /// forwards bytes unchanged and inspects NUL-delimited UTF-8 only. It does not
    /// redirect destinations, inject packets or decrypt TLS.
    /// Client packets stay masked until an ATK/ALK server success is observed.
    /// Known credential prefixes and words remain masked afterwards, in both directions.
    /// This is conservative best-effort filtering, not a guarantee that unknown
    /// packets, chat or custom protocol fields cannot contain personal data or secrets.
    /// Event subscribers must return promptly; callbacks run on relay worker threads.
    /// </summary>
    public sealed class PacketCaptureProxy : IDisposable
    {
        private const int MaximumConnections = 16;
        private const int ConnectTimeoutMilliseconds = 10000;
        private const int IdleTimeoutSeconds = 120;
        private readonly object _gate = new object();
        private RunState _run;
        private Task _stopTask;
        private int _nextConnectionId;
        private bool _disposed;

        private sealed class RunState
        {
            internal TcpListener Listener;
            internal CancellationTokenSource Stop = new CancellationTokenSource();
            internal readonly Dictionary<int, ConnectionState> Connections = new Dictionary<int, ConnectionState>();
            internal Task AcceptTask;
            internal string UpstreamHost;
            internal int UpstreamPort;
            internal int Port;
            internal bool Stopping;
        }

        private sealed class ConnectionState
        {
            internal int Id;
            internal TcpClient Client;
            internal TcpClient Upstream = new TcpClient();
            internal CancellationTokenSource Stop;
            internal Task Work;
            internal int Authenticated;
            internal long LastActivity = Stopwatch.GetTimestamp();
            private int _closed;

            internal void Close()
            {
                if (Interlocked.Exchange(ref _closed, 1) != 0) return;
                try { Stop.Cancel(); } catch (ObjectDisposedException) { }
                TryClose(Client);
                TryClose(Upstream);
            }
        }

        public event Action<CapturedPacket> PacketCaptured;
        public event Action<string> StatusChanged;

        public int ListeningPort
        {
            get { lock (_gate) return _run == null ? 0 : _run.Port; }
        }

        public bool IsRunning
        {
            get { lock (_gate) return _run != null && !_run.Stopping; }
        }

        public void Start(int listenPort, string upstreamHost, int upstreamPort)
        {
            if (listenPort < 0 || listenPort > 65535) throw new ArgumentOutOfRangeException(nameof(listenPort));
            if (upstreamPort <= 0 || upstreamPort > 65535) throw new ArgumentOutOfRangeException(nameof(upstreamPort));
            if (string.IsNullOrWhiteSpace(upstreamHost) || upstreamHost.Any(char.IsControl))
                throw new ArgumentException("L'adresse du serveur est invalide.", nameof(upstreamHost));
            int port;
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(PacketCaptureProxy));
                if (_run != null) throw new InvalidOperationException("Le relais est déjà démarré ou en cours d'arrêt.");
                var run = new RunState
                {
                    Listener = new TcpListener(IPAddress.Loopback, listenPort),
                    UpstreamHost = upstreamHost.Trim(),
                    UpstreamPort = upstreamPort
                };
                try
                {
                    run.Listener.Start(MaximumConnections);
                    port = run.Port = ((IPEndPoint)run.Listener.LocalEndpoint).Port;
                    if (upstreamPort == port &&
                        (string.Equals(run.UpstreamHost.TrimEnd('.'), "localhost", StringComparison.OrdinalIgnoreCase) ||
                        IPAddress.TryParse(run.UpstreamHost, out IPAddress address) && IPAddress.IsLoopback(address)))
                        throw new InvalidOperationException("Le serveur distant ne peut pas être le relais lui-même.");
                    _run = run;
                    run.AcceptTask = Task.Run(() => AcceptConnectionsAsync(run));
                }
                catch
                {
                    run.Listener.Stop();
                    run.Stop.Dispose();
                    throw;
                }
            }
            EmitStatus("Relais démarré sur 127.0.0.1:" + port + ".");
        }

        /// <summary>Closes the listener and every socket, and waits for all relay workers.</summary>
        public Task StopAsync()
        {
            Task stopping;
            lock (_gate)
            {
                if (_run == null) return Task.CompletedTask;
                if (_stopTask != null) return _stopTask;
                RunState run = _run;
                run.Stopping = true;
                run.Stop.Cancel();
                run.Listener.Stop();
                foreach (var connection in run.Connections.Values.ToArray()) connection.Close();
                stopping = _stopTask = Task.Run(() => StopRunAsync(run));
            }
            EmitStatus("Arrêt du relais en cours.");
            return stopping;
        }

        private async Task StopRunAsync(RunState run)
        {
            try
            {
                await run.AcceptTask.ConfigureAwait(false);
                Task[] connections;
                lock (_gate) connections = run.Connections.Values.Select(connection => connection.Work).ToArray();
                await Task.WhenAll(connections).ConfigureAwait(false);
            }
            finally
            {
                run.Stop.Dispose();
                lock (_gate)
                {
                    if (ReferenceEquals(_run, run))
                    {
                        _run = null;
                        _stopTask = null;
                    }
                }
                EmitStatus("Relais arrêté.");
            }
        }

        private async Task AcceptConnectionsAsync(RunState run)
        {
            while (!run.Stop.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await run.Listener.AcceptTcpClientAsync().ConfigureAwait(false); }
                catch (Exception error) when (error is SocketException || error is ObjectDisposedException ||
                    error is InvalidOperationException)
                {
                    if (!run.Stop.IsCancellationRequested) EmitStatus("Le relais ne peut plus accepter de connexion.");
                    break;
                }
                ConnectionState state = null;
                bool limited = false;
                lock (_gate)
                {
                    var remote = client.Client.RemoteEndPoint as IPEndPoint;
                    if (!run.Stopping && remote != null && IPAddress.IsLoopback(remote.Address))
                    {
                        if (run.Connections.Count >= MaximumConnections) limited = true;
                        else
                        {
                            state = new ConnectionState
                            {
                                Id = Interlocked.Increment(ref _nextConnectionId),
                                Client = client,
                                Stop = CancellationTokenSource.CreateLinkedTokenSource(run.Stop.Token)
                            };
                            run.Connections.Add(state.Id, state);
                            state.Work = Task.Run(() => RelayConnectionAsync(run, state));
                        }
                    }
                }
                if (state == null)
                {
                    TryClose(client);
                    if (limited) EmitStatus("Connexion refusée : limite de 16 connexions simultanées atteinte.");
                }
            }
            if (!run.Stop.IsCancellationRequested) ObserveFault(StopAsync());
        }

        private async Task RelayConnectionAsync(RunState run, ConnectionState state)
        {
            bool connected = false;
            Task idle = null;
            try
            {
                state.Client.NoDelay = true;
                state.Upstream.NoDelay = true;
                await ConnectUpstreamAsync(run, state).ConfigureAwait(false);
                connected = true;
                EmitStatus("Connexion " + state.Id + " : relais actif.");
                idle = WatchIdleAsync(state);
                Task outgoing = RelayDirectionAsync(state, state.Client, state.Upstream, true);
                Task incoming = RelayDirectionAsync(state, state.Upstream, state.Client, false);
                try { await Task.WhenAll(outgoing, incoming).ConfigureAwait(false); }
                catch
                {
                    state.Close();
                    await ObserveAsync(outgoing).ConfigureAwait(false);
                    await ObserveAsync(incoming).ConfigureAwait(false);
                    throw;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                if (!run.Stop.IsCancellationRequested)
                    EmitStatus("Connexion " + state.Id + (connected ? " : connexion interrompue." :
                        " : connexion au serveur impossible."));
            }
            finally
            {
                state.Close();
                if (idle != null) await ObserveAsync(idle).ConfigureAwait(false);
                lock (_gate) run.Connections.Remove(state.Id);
                state.Stop.Dispose();
                if (connected && !run.Stop.IsCancellationRequested)
                    EmitStatus("Connexion " + state.Id + " : relais fermé.");
            }
        }

        private static async Task ConnectUpstreamAsync(RunState run, ConnectionState state)
        {
            Task connect = state.Upstream.ConnectAsync(run.UpstreamHost, run.UpstreamPort);
            Task timeout = Task.Delay(ConnectTimeoutMilliseconds, state.Stop.Token);
            if (await Task.WhenAny(connect, timeout).ConfigureAwait(false) != connect)
            {
                TryClose(state.Upstream);
                ObserveFault(connect);
                state.Stop.Token.ThrowIfCancellationRequested();
                throw new TimeoutException("Le serveur n'a pas répondu à temps.");
            }
            await connect.ConfigureAwait(false);
            state.Stop.Token.ThrowIfCancellationRequested();
        }

        private async Task RelayDirectionAsync(ConnectionState state, TcpClient source, TcpClient destination,
            bool fromClient)
        {
            var decoder = new NullTerminatedPacketDecoder();
            bool captureEnabled = true;
            bool redactPending = false;
            byte[] buffer = new byte[8192];
            NetworkStream input = source.GetStream(), output = destination.GetStream();
            try
            {
                while (!state.Stop.IsCancellationRequested)
                {
                    int count = await input.ReadAsync(buffer, 0, buffer.Length, state.Stop.Token).ConfigureAwait(false);
                    if (count == 0)
                    {
                        TryShutdownSend(destination);
                        return;
                    }
                    Interlocked.Exchange(ref state.LastActivity, Stopwatch.GetTimestamp());
                    if (captureEnabled)
                    {
                        try
                        {
                            if (fromClient && Volatile.Read(ref state.Authenticated) == 0)
                                redactPending = true;
                            foreach (string packet in decoder.Append(buffer, 0, count))
                                CapturePacket(state, fromClient, packet, redactPending);
                            if (buffer[count - 1] == 0) redactPending = false;
                        }
                        catch (Exception error) when (error is InvalidDataException || error is DecoderFallbackException)
                        {
                            captureEnabled = false;
                            decoder.Reset();
                            EmitStatus("Connexion " + state.Id + ": capture " +
                                (fromClient ? "client" : "serveur") +
                                " désactivée (trame UTF-8 invalide ou supérieure à 1 Mo) ; transfert maintenu.");
                        }
                    }
                    await output.WriteAsync(buffer, 0, count, state.Stop.Token).ConfigureAwait(false);
                }
            }
            catch
            {
                state.Close();
                throw;
            }
        }

        private async Task WatchIdleAsync(ConnectionState state)
        {
            while (!state.Stop.IsCancellationRequested)
            {
                await Task.Delay(1000, state.Stop.Token).ConfigureAwait(false);
                double idleSeconds = (Stopwatch.GetTimestamp() - Interlocked.Read(ref state.LastActivity)) /
                    (double)Stopwatch.Frequency;
                if (idleSeconds < IdleTimeoutSeconds) continue;
                EmitStatus("Connexion " + state.Id + " : fermée après 2 minutes d'inactivité.");
                state.Close();
                return;
            }
        }

        private void CapturePacket(ConnectionState state, bool fromClient, string packet, bool redactPending)
        {
            if (!fromClient)
            {
                if (packet.StartsWith("HC", StringComparison.Ordinal) ||
                    packet.StartsWith("HG", StringComparison.Ordinal) ||
                    packet.StartsWith("ATE", StringComparison.Ordinal))
                    Volatile.Write(ref state.Authenticated, 0);
                else if (packet.StartsWith("ATK", StringComparison.Ordinal) ||
                    packet.StartsWith("ALK", StringComparison.Ordinal))
                    Volatile.Write(ref state.Authenticated, 1);
            }
            bool redacted = fromClient && (redactPending || Volatile.Read(ref state.Authenticated) == 0) ||
                IsSensitive(packet, fromClient);
            var captured = new CapturedPacket(state.Id, fromClient,
                redacted ? "[Masqué : authentification]" : packet, redacted);
            Action<CapturedPacket> handlers = PacketCaptured;
            if (handlers == null) return;
            foreach (Action<CapturedPacket> handler in handlers.GetInvocationList())
                try { handler(captured); } catch (Exception) { }
        }

        private static bool IsSensitive(string packet, bool fromClient)
        {
            if (packet.StartsWith("AT", StringComparison.Ordinal) ||
                packet.StartsWith("Ai", StringComparison.Ordinal) ||
                packet.StartsWith("AP", StringComparison.Ordinal) ||
                packet.StartsWith("ALK", StringComparison.Ordinal) ||
                packet.StartsWith("AXK", StringComparison.Ordinal) ||
                packet.StartsWith("AYK", StringComparison.Ordinal) ||
                packet.StartsWith("#Z", StringComparison.Ordinal) ||
                packet.StartsWith("#1", StringComparison.Ordinal) ||
                fromClient && (packet.IndexOf('\n') >= 0 || packet.IndexOf('\r') >= 0)) return true;
            foreach (string word in new[] { "password", "passwd", "pwd", "token", "apikey", "api_key",
                "api-key", "authorization", "login", "credential", "secret" })
                if (packet.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private void EmitStatus(string status)
        {
            Action<string> handlers = StatusChanged;
            if (handlers == null) return;
            foreach (Action<string> handler in handlers.GetInvocationList())
                try { handler(status); } catch (Exception) { }
        }

        private static void TryClose(TcpClient client)
        {
            if (client == null) return;
            try { client.Close(); } catch (SocketException) { } catch (ObjectDisposedException) { }
        }

        private static void TryShutdownSend(TcpClient client)
        {
            try { client.Client.Shutdown(SocketShutdown.Send); }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
        }

        private static async Task ObserveAsync(Task task)
        {
            try { await task.ConfigureAwait(false); } catch (Exception) { }
        }

        private static void ObserveFault(Task task)
        {
            task.ContinueWith(failed => { var observed = failed.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        /// <summary>
        /// Immediately closes sockets. Await StopAsync before Dispose when completion
        /// of all worker callbacks is required; Dispose never blocks an event callback.
        /// </summary>
        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
            }
            ObserveFault(StopAsync());
        }
    }
}
