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
       public static async Task<string>GetAPI_key(string login, string pass)
        {
            WinHttpHandler handler = new WinHttpHandler();
            using(HttpClient client = new HttpClient(handler)) 
            {
                HttpRequestMessage req = new HttpRequestMessage
                {
                    RequestUri = new Uri("https://haapi.ankama.com/json/Ankama/v5/Api/CreateApiKey"),
                    Method = HttpMethod.Post,
                    Headers =
                    {
                        { "user-agent", "Zaap 3.7.4" }
                    },
                    Content = new StringContent($"login={login}&password={pass}", Encoding.UTF8, "text/plain")
                };
                HttpResponseMessage reponse = await client.SendAsync(req);
                if(reponse.StatusCode == HttpStatusCode.OK) 
                {
                    Dictionary<string, object> Get_APIreponse = await JsonSerializer.DeserializeAsync<Dictionary<string, object>>(await reponse.Content.ReadAsStreamAsync());
                    string key = Get_APIreponse["key"].ToString();
                    return key;
                }
                return null;
            }
        }

        public static async Task<string>Get_Token(string apikey)
        {
            WinHttpHandler handler = new WinHttpHandler();
            using(HttpClient client = new HttpClient(handler))
            {
                HttpRequestMessage req = new HttpRequestMessage
                {
                    RequestUri = new Uri($"https://haapi.ankama.com/json/Ankama/v5/Account/CreateToken?game=101"),
                    Method = HttpMethod.Get,
                    Headers =
                    {
                        { "apiKey", $"{apikey}" },
                        { "user-agent", "Zaap 3.7.4" }
                    }
                };
                HttpResponseMessage reponse = await client.SendAsync(req);

                if(reponse?.StatusCode == HttpStatusCode.OK)
                {
                    Dictionary<string, object> Get_APIreponse = await JsonSerializer.DeserializeAsync<Dictionary<string, object>>(await reponse.Content.ReadAsStreamAsync());
                    string token = Get_APIreponse["token"].ToString();
                    return token;
                }
                return null;
            }
        }
    }
}
