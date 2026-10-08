using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Network.ByPass;
using Tool_BotProtocol.Utils.Crypto;
using Tool_BotProtocol.Utils.Logger;
using Tools_protocol.Network;

namespace Tool_BotProtocol.Network
{
    public class TcpClient : IDisposable
    {
        private sealed class SocketSession
        {
            internal readonly Socket Socket;
            internal readonly byte[] Buffer;
            internal readonly NullTerminatedPacketDecoder Decoder = new NullTerminatedPacketDecoder();

            internal SocketSession(IPAddress address)
            {
                Socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                Buffer = new byte[Socket.ReceiveBufferSize];
            }
        }

        private readonly object _sync = new object();
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private const int MaxPingSamples = 50;
        private readonly List<int> _pings = new List<int>(MaxPingSamples);
        // Instants d'envoi des « ping » encore sans « pong » : StarLoco répond dans l'ordre.
        private readonly Queue<long> _pendingPings = new Queue<long>();
        private SocketSession _session;
        private int _sendUsers;
        private bool _semaphoreDisposed;
        private bool _disposed;
        private bool _disconnectingAccount;

        public Accounts account;
        public event Action<string> packetReceivedEvent;
        public event Action<string> packetSendEvent;
        /// <summary>Paquet entièrement écrit sur le socket (un paquet = un envoi terminé par <c>\n\0</c>).</summary>
        public event Action<string> PacketSent;
        /// <summary>Paquet refusé avant l'envoi (paquet, raison) : vide ou contenant un NUL ou un retour à la ligne.</summary>
        public event Action<string, string> PacketRejected;
        public event Action<string> socketInformationEvent;
        public string apikey;
        public string Token;

        public TcpClient(Accounts Account)
        {
            account = Account;
        }

        public async Task ConnectToServer(IPAddress ip, int port)
        {
            if (ip == null) throw new ArgumentNullException(nameof(ip));
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            SocketSession current, previous;
            lock (_sync)
            {
                if (_disposed || _disconnectingAccount) throw new ObjectDisposedException(nameof(TcpClient));
                current = new SocketSession(ip);
                previous = _session;
                _session = current;
                previous?.Decoder.Reset();
            }
            CloseSession(previous);
            lock (_pings) _pendingPings.Clear();
            try
            {
                if (GlobalConfig.BYPASS)
                    await ConnexionZaap().ConfigureAwait(false);
                if (!IsCurrent(current)) return;
                Task connecting = current.Socket.ConnectAsync(ip, port);
                if (await Task.WhenAny(connecting, Task.Delay(10000)).ConfigureAwait(false) != connecting)
                {
                    CloseSession(current);
                    try { await connecting.ConfigureAwait(false); } catch { }
                    throw new TimeoutException("Le serveur n’a pas répondu dans le délai de connexion.");
                }
                await connecting.ConfigureAwait(false);
                if (IsCurrent(current)) BeginReceive(current);
            }
            catch (Exception error)
            {
                if (IsCurrent(current))
                {
                    ReportInformation(error.ToString());
                    DisconnectSession(current, false);
                }
            }
        }

        private async Task<bool> ConnexionZaap()
        {
            apikey = await ByPassLauncher.GetAPI_key(account.accountConfig.Account, account.accountConfig.Password);

            await Task.Delay(Randomize.get_Random(500, 1500));
            Token = await ByPassLauncher.Get_Token(apikey);
            return true;
        }

        private bool IsCurrent(SocketSession session)
        {
            lock (_sync) return !_disposed && ReferenceEquals(_session, session);
        }

        private void BeginReceive(SocketSession session)
        {
            if (!IsCurrent(session)) return;
            session.Socket.BeginReceive(session.Buffer, 0, session.Buffer.Length,
                SocketFlags.None, ReceptionCallBack, session);
        }

        private async void ReceptionCallBack(IAsyncResult ar)
        {
            var session = (SocketSession)ar.AsyncState;
            try
            {
                int count = session.Socket.EndReceive(ar, out SocketError response);
                if (!IsCurrent(session)) return;
                if (count <= 0 || response != SocketError.Success)
                {
                    DisconnectSession(session, true);
                    return;
                }
                await ProcessReceivedDataAsync(session, count).ConfigureAwait(false);
                if (IsCurrent(session)) BeginReceive(session);
            }
            catch (Exception error)
            {
                if (IsCurrent(session))
                {
                    ReportInformation(error.ToString());
                    DisconnectSession(session, true);
                }
            }
        }

        private async Task ProcessReceivedDataAsync(SocketSession session, int count)
        {
            IList<string> packets;
            lock (_sync)
            {
                if (_disposed || !ReferenceEquals(_session, session)) return;
                packets = session.Decoder.Append(session.Buffer, 0, count);
            }
            foreach (string packet in packets)
            {
                if (!IsCurrent(session)) return;
                packetReceivedEvent?.Invoke(packet);
                if (!IsCurrent(session)) return;
                await MessagesReception.ReceptionAsync(this, packet).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Raison du refus d'un paquet, ou null s'il peut partir seul : StarLoco découpe chaque envoi sur le NUL puis
        /// sur <c>\n</c> et ne traite que les premières lignes (<c>GameHandler.messageReceived</c>). Un paquet contenant
        /// un NUL, un <c>\n</c> ou un <c>\r</c> deviendrait plusieurs paquets ou serait tronqué.
        /// </summary>
        public static string SinglePacketViolation(string packet)
        {
            if (string.IsNullOrEmpty(packet)) return "paquet vide";
            if (packet.IndexOf('\0') >= 0) return "caractère NUL dans le paquet";
            if (packet.IndexOf('\n') >= 0 || packet.IndexOf('\r') >= 0) return "retour à la ligne dans le paquet";
            return null;
        }

        public async Task SendPacketAsync(string packet)
        {
            string violation = SinglePacketViolation(packet);
            if (violation != null)
            {
                RejectPacket(packet, violation);
                return;
            }
            SocketSession session;
            lock (_sync)
            {
                if (_disposed || _session == null) return;
                session = _session;
                _sendUsers++;
            }
            bool entered = false;
            try
            {
                await _semaphore.WaitAsync().ConfigureAwait(false);
                entered = true;
                if (!IsCurrent(session) || !session.Socket.Connected) return;
                // Enregistré avant l'écriture : la réponse « pong » peut être traitée avant la fin de SendAsync.
                if (packet == "ping") MarkPingSent();
                byte[] data = Encoding.UTF8.GetBytes(packet + "\n\0");
                int offset = 0;
                while (offset < data.Length)
                {
                    if (!IsCurrent(session)) return;
                    int sent = await session.Socket.SendAsync(
                        new ArraySegment<byte>(data, offset, data.Length - offset), SocketFlags.None).ConfigureAwait(false);
                    if (sent <= 0) throw new SocketException((int)SocketError.ConnectionReset);
                    offset += sent;
                }
                if (IsCurrent(session))
                {
                    packetSendEvent?.Invoke(packet);
                    PacketSent?.Invoke(packet);
                }
            }
            catch (Exception error)
            {
                if (IsCurrent(session))
                {
                    ReportInformation(error.ToString());
                    DisconnectSession(session, true);
                }
            }
            finally
            {
                if (entered) _semaphore.Release();
                bool disposeSemaphore;
                lock (_sync)
                {
                    _sendUsers--;
                    disposeSemaphore = _disposed && _sendUsers == 0 && !_semaphoreDisposed;
                    if (disposeSemaphore) _semaphoreDisposed = true;
                }
                if (disposeSemaphore) _semaphore.Dispose();
            }
        }

        public async Task SendPacket(string packet, bool reponse = false)
        {
            await SendPacketAsync(packet).ConfigureAwait(false);
        }

        private void RejectPacket(string packet, string reason)
        {
            Accounts owner;
            Action<string, string> rejected;
            lock (_sync) { owner = account; rejected = PacketRejected; }
            string shown = BotPacketRedactor.Redact(packet, owner);
            ReportInformation("Paquet refusé avant l'envoi (" + reason + ") : " + shown);
            try { owner?.Logger?.LogError("PROTOCOLE", "Paquet refusé avant l'envoi (" + reason + ") : un envoi ne doit contenir qu'un seul paquet."); }
            catch { /* Journal fermé : le refus reste signalé par l'événement. */ }
            try { rejected?.Invoke(packet, reason); }
            catch { /* A diagnostic subscriber must not break the caller. */ }
        }

        private void MarkPingSent()
        {
            lock (_pings)
            {
                if (_pendingPings.Count >= MaxPingSamples) _pendingPings.Dequeue();
                _pendingPings.Enqueue(Stopwatch.GetTimestamp());
            }
        }

        /// <summary>
        /// À appeler sur la réponse « pong » de StarLoco (réponse à « ping ») : mesure l'aller-retour du plus ancien
        /// « ping » en attente et l'ajoute aux 50 dernières mesures. Renvoie la durée en ms, ou -1 sans « ping » en attente.
        /// </summary>
        public int NotifyPong()
        {
            lock (_pings)
            {
                if (_pendingPings.Count == 0) return -1;
                long elapsed = Stopwatch.GetTimestamp() - _pendingPings.Dequeue();
                int milliseconds = (int)Math.Min(int.MaxValue, Math.Max(0L, elapsed * 1000L / Stopwatch.Frequency));
                if (_pings.Count >= MaxPingSamples) _pings.RemoveAt(0);
                _pings.Add(milliseconds);
                return milliseconds;
            }
        }

        public void DisconnectSocket()
        {
            SocketSession current;
            lock (_sync) current = _session;
            if (current != null) DisconnectSession(current, false);
        }

        /// <summary>
        /// <c>ATE</c> reçu du serveur de jeu : si un ticket est en cours et qu'il reste des essais, ferme la session comme le fait
        /// StarLoco juste après (<c>kick</c>) et renvoie le ticket sur une nouvelle connexion. Faux, sans rien fermer, si le refus
        /// est définitif : l'appelant fait alors échouer la connexion.
        /// </summary>
        internal bool RetryTicketAfterRefusal()
        {
            SocketSession current;
            lock (_sync) current = _session;
            return current != null && EndSession(current, true, true);
        }

        private void DisconnectSession(SocketSession session, bool notifyAccount) => EndSession(session, notifyAccount, false);

        /// <summary>Ferme la session ; vrai si un nouvel essai du ticket a été lancé à la place de la déconnexion du compte.</summary>
        private bool EndSession(SocketSession session, bool notifyAccount, bool ticketRefused)
        {
            Accounts currentAccount;
            bool disconnectAccount, retry = false;
            string host = null;
            int port = 0, attempt = 0;
            lock (_sync)
            {
                if (!ReferenceEquals(_session, session)) return false;
                currentAccount = account;
                disconnectAccount = notifyAccount && currentAccount != null &&
                    ReferenceEquals(currentAccount.Connexion, this);
                // Ticket éconduit par le serveur de jeu avant ATK : le compte se reconnecte avec ce même client. La décision est
                // prise sous le verrou qui remet la session à zéro : aucun ConnectToServer concurrent ne peut ouvrir une session
                // entre les deux sur un transport que la déconnexion du compte va jeter.
                if (disconnectAccount) retry = currentAccount.ReserveTicketRetry(this, out host, out port, out attempt);
                if (ticketRefused && !retry) return false;
                _session = null;
                session.Decoder.Reset();
                if (disconnectAccount && !retry) _disconnectingAccount = true;
            }
            CloseSession(session);
            ReportInformation("Socket déconnecté de l'hôte");
            if (retry)
            {
                currentAccount.StartTicketRetry(this, host, port, attempt, ticketRefused);
                return true;
            }
            if (disconnectAccount)
            {
                // Identity is checked by Accounts; its UI events run outside transport locks.
                try { currentAccount.Disconnect(this); }
                finally { Dispose(); }
            }
            return false;
        }

        private static void CloseSession(SocketSession session)
        {
            if (session == null) return;
            try { session.Socket.Shutdown(SocketShutdown.Both); }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
            finally { session.Socket.Dispose(); }
        }

        private void ReportInformation(string message)
        {
            try { socketInformationEvent?.Invoke(message); }
            catch { /* A logging subscriber must not interrupt socket cleanup. */ }
        }

        public bool IsConnected()
        {
            lock (_sync)
            {
                if (_disposed || _session == null) return false;
                try { return _session.Socket.Connected; }
                catch (SocketException) { return false; }
                catch (ObjectDisposedException) { return false; }
            }
        }

        public int GetTotalPings() { lock (_pings) return _pings.Count; }
        public int GetPingAverage() { lock (_pings) return _pings.Count == 0 ? 0 : (int)_pings.Average(); }
        ~TcpClient() => Dispose(false);

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            SocketSession current;
            bool disposeSemaphore;
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                current = _session;
                _session = null;
                current?.Decoder.Reset();
                disposeSemaphore = disposing && _sendUsers == 0 && !_semaphoreDisposed;
                if (disposeSemaphore) _semaphoreDisposed = true;
                account = null;
                packetReceivedEvent = null;
                packetSendEvent = null;
                PacketSent = null;
                PacketRejected = null;
                socketInformationEvent = null;
            }
            CloseSession(current);
            if (disposeSemaphore) _semaphore.Dispose();
        }
    }

}
