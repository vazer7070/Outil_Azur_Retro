using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Network.ByPass;
using Tool_BotProtocol.Utils.Crypto;

namespace Tool_BotProtocol.Network
{
    public class TcpClient :IDisposable
    {
        private Socket socket { get; set; }
        private byte[] buffer { get; set; }
        public Accounts account;
        private SemaphoreSlim _semaphore;
        private bool _disposed;

        public event Action<string> packetReceivedEvent;
        public event Action<string> packetSendEvent;
        public event Action<string> socketInformationEvent;

        public string apikey { get; set; }
        public string Token { get; set; }
        private bool ByPassOK = false;

        private List<int> _pings;

        public TcpClient(Accounts Account)
        {
            account = Account;
            _semaphore = new SemaphoreSlim(1);
            _pings = new List<int>(50);
        }
        public async Task ConnectToServer(IPAddress ip, int port)
        {
            try
            {
                if(GlobalConfig.BYPASS)
                    ByPassOK = await ConnexionZaap();

                socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                buffer = new byte[socket.ReceiveBufferSize];

                await socket.ConnectAsync(ip, port);
                if(ByPassOK && GlobalConfig.BYPASS)
                    socket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, ReceptionCallBack, socket);
                else
                    socket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, ReceptionCallBack, socket);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                DisconnectSocket();
            }
        }
        public async Task<bool> ConnexionZaap()
        {
            apikey = await ByPassLauncher.GetAPI_key(account.accountConfig.Account, account.accountConfig.Password);

           await Task.Delay(Randomize.get_Random(500, 1500));
            Token = await ByPassLauncher.Get_Token(apikey);
            return true;
        }
        private void ConnectCallBack(IAsyncResult ar)
        {
            try
            {
                if (IsConnected())
                {
                    socket = ar.AsyncState as Socket;
                    socket.EndConnect(ar);

                    socket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, new AsyncCallback(ReceptionCallBack), socket);
                    socketInformationEvent?.Invoke("Socket connectée correctement");
                }
                else
                {
                    DisconnectSocket();
                    socketInformationEvent?.Invoke("Impossible de joindre le serveur hôte");
                }
            }
            catch (Exception ex)
            {
                socketInformationEvent?.Invoke(ex.ToString());
                DisconnectSocket();
            }
        }

        public void ReceptionCallBack(IAsyncResult ar)
        {
            try
            {
                if (!IsConnected() || _disposed)
                {
                    DisconnectSocket();
                    return;
                }

                int byteData = socket.EndReceive(ar, out SocketError reponse);
                byte[] buff = new byte[byteData];
                Array.Copy(buffer, buff, buff.Length);

                if (byteData < 1 || reponse != SocketError.Success)
                {
                    account.Disconnect();
                    return;
                }
                string data = Encoding.UTF8.GetString(buff, 0, byteData);
                foreach (string p in data.Replace("\x0a", string.Empty).Split('\0').Where(x => x != string.Empty))
                {
                    packetReceivedEvent?.Invoke(p);
                    MessagesReception.Reception(this, p);
                }
                socket?.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, ReceptionCallBack, socket);
            }
            catch(Exception e)
            {
                socketInformationEvent?.Invoke(e.ToString());
                return;
            }

        }

        public async Task SendPacketAsync(string packet)
        {
            try
            {
                if (!IsConnected())
                    return;

                
                byte[] byte_packet = Encoding.UTF8.GetBytes(string.Format($"{packet}\n\x00"));

                await _semaphore.WaitAsync().ConfigureAwait(false);

                socket.Send(byte_packet);


                packetSendEvent?.Invoke(packet);
                _semaphore.Release();
            }
            catch (Exception ex)
            {
                socketInformationEvent?.Invoke(ex.ToString());
                DisconnectSocket();
            };
        }

        public async Task SendPacket(string packet, bool reponse = false)
        {
            await SendPacketAsync(packet);
        }
        public void DisconnectSocket()
        {
            if (IsConnected())
            {
                if (socket != null && socket.Connected)
                {
                    socket.Shutdown(SocketShutdown.Both);
                    socket.Disconnect(false);
                    socket.Close();
                }

                socketInformationEvent?.Invoke("Socket deconnecté de l'hôte");
            }
        }

        public bool IsConnected()
        {
            try
            {
                return !(_disposed || socket == null || !socket.Connected && socket.Available == 0);
            }
            catch (SocketException)
            {
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        public int GetTotalPings() => _pings.Count();
        public int GetPingAverage() => (int)_pings.Average();

        
        ~TcpClient() => Dispose(false);
        public void Dispose() => Dispose(true);

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (socket != null && socket.Connected)
                {
                    socket.Shutdown(SocketShutdown.Both);
                    socket.Disconnect(false);
                    socket.Close();
                }

                if (disposing)
                {
                    socket.Dispose();
                    _semaphore.Dispose();
                }

                _semaphore = null;
                account = null;
                socket = null;
                buffer = null;
                packetReceivedEvent = null;
                packetSendEvent = null;
                _disposed = true;
            }
        }
    }
}
