using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tool_BotProtocol.Json
{
    public class Config
    {
        public class ConfigEntries
        {
            [JsonProperty("IP")]
            public string IP { get; set; }

            [JsonProperty("AuthPort")]
            public string AuthPort { get; set; }

            [JsonProperty("GamePort")]
            public string GamePort { get; set; }

            [JsonProperty("Version")]
            public string Version { get; set; }

            public class DataEntries
            {
                [JsonProperty("Config")]
                public ConfigEntries ConfigEntries { get; set; }
            }
        }
    }
}
