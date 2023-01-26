using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tool_BotProtocol.Json
{
    public class AccountsJson
    {
        public class AccountEntries
        {
            [JsonProperty("Compte")]
            public string Compte { get; set; }

            [JsonProperty("MDP")]
            public string MDP { get; set; }

            [JsonProperty("Lieu")]
            public string Lieu { get; set; }
        }

        public class DataEntries
        {
            [JsonProperty("Comptes")]
            public AccountEntries accountEntries { get; set; }
        }
    }
}
