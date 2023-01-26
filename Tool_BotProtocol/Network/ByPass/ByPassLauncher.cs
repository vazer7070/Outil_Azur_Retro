using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using JsonSerializer = System.Text.Json.JsonSerializer;
using Org.BouncyCastle.Ocsp;
using Ubiety.Dns.Core;
using System.Net.Sockets;

namespace Tool_BotProtocol.Network.ByPass
{
    public class ByPassLauncher
    {
        Socket socket { get; set; }
       
        public async void StartBypass(int port, int gameId, string login, string pass, bool runOnce)
        {
            var httpClient = new HttpClient();
            //var tcpListener = new TcpListener(IPAddress.Parse("127.0.0.1"), port);
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            byte[] buffer = new byte[1024];
            socket.BeginConnect(IPAddress.Parse("127.0.0.1"), port, new AsyncCallback(ConnectCallBack), socket);
            //tcpListener.Start();
            while (true)
            {
                try
                {
                    socket.BeginAccept(new AsyncCallback(ConnectAccept), socket);
                    socket.Receive(buffer, SocketFlags.None);
                    using (var http2Client = new HttpClient(new Http2CustomHandler()))
                    {
                        var req = new HttpRequestMessage
                        {
                            RequestUri = new Uri("https://haapi.ankama.com/json/Ankama/v5/Api/CreateApiKey"),
                            Method = HttpMethod.Post,
                            Headers = {
                { "user-agent", "Zaap 3.6.15" }
             },
                            Content = new StringContent($"login={login}&password={pass}&game_id={gameId}&long_life_token=true&shop_key=ZAAP&payment_mode=OK&lang=en", Encoding.UTF8, "text/plain")
                        };

                        var response = await httpClient.SendAsync(req);
                        var createKeyResponse = await JsonSerializer.DeserializeAsync<Dictionary<string, object>>(await response.Content.ReadAsStreamAsync());
                        var apiKey = createKeyResponse?["key"].ToString();

                        var req2 = new HttpRequestMessage
                        {
                            RequestUri = new Uri($"https://haapi.ankama.com/json/Ankama/v5/Account/CreateToken?game={gameId}"),
                            Method = HttpMethod.Get,
                            Headers =
            {
                { "apiKey", $"{apiKey}" },
                { "user-agent", "Zaap 3.6.15" }
            }
                        };
                        response = await httpClient.SendAsync(req);
                        var createTokenResponse = await JsonSerializer.DeserializeAsync<Dictionary<string, object>>(await response.Content.ReadAsStreamAsync());
                        var token = createTokenResponse?["token"].ToString();

                        byte[] bSend = Encoding.UTF8.GetBytes($"auth_getGameToken {token}\0");
                        socket.Send(bSend);
                        Console.WriteLine($"> auth_getGameToken {token}");
                    }
                }catch(Exception e)
                {
                    throw (e);
                }
            }
        }
        private void ConnectAccept(IAsyncResult ar)
        {
            try
            {
                socket = ar.AsyncState as Socket;
                socket.EndConnect(ar);
                Console.WriteLine("Le client est là");
            }
            catch
            {
                Console.WriteLine("eh ben non");
            }
        }
        private void ConnectCallBack(IAsyncResult ar)
        {
            try
            {
                socket = ar.AsyncState as Socket;
                socket.EndConnect(ar);
                Console.WriteLine("Socket connectée correctement");
            }
            catch
            {
                Console.WriteLine("Impossible de joindre le serveur hôte");
            }
        }

        public class Http2CustomHandler : WinHttpHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
            {
                request.Version = new Version("2.0");
                return base.SendAsync(request, cancellationToken);
            }
        }
    }
}
