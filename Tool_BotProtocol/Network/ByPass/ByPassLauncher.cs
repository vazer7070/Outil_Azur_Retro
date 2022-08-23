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

namespace Tool_BotProtocol.Network.ByPass
{
    public class ByPassLauncher
    {
        public static async Task<string> GetApiKey(HttpClient client, string login, string password)
        {
            var req = new HttpRequestMessage
            {
                RequestUri = new Uri("https://haapi.ankama.com/json/Ankama/v5/Api/CreateApiKey"),
                Method = HttpMethod.Post,
                Version = HttpVersion.Version10,
                Headers =
            {
                { "user-agent", "Zaap 3.6.15" }
            },
                Content = new StringContent($"login={login}&password={password}&long_life_token=true", Encoding.UTF8, "text/plain")
            };

            var response = await client.SendAsync(req);
            var createKeyResponse = await JsonSerializer.DeserializeAsync<Dictionary<string, object>>(await response.Content.ReadAsStreamAsync());
            var apiKey = createKeyResponse?["key"].ToString();

            return apiKey;
        }

        public static async Task<string> GetToken(HttpClient client, int gameId, string apiKey)
        {
            var req = new HttpRequestMessage
            {
                RequestUri = new Uri($"https://haapi.ankama.com/json/Ankama/v5/Account/CreateToken?game={gameId}"),
                Method = HttpMethod.Get,
                Version = HttpVersion.Version10,
                Headers =
            {
                { "apiKey", $"{apiKey}" },
                { "user-agent", "Zaap 3.6.15" }
            }
            };


            var response = await client.SendAsync(req);
            var createTokenResponse = await JsonSerializer.DeserializeAsync<Dictionary<string, object>>(await response.Content.ReadAsStreamAsync());

            return createTokenResponse?["token"]?.ToString();
        }



    }
}
