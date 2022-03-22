using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tools_protocol.Json
{
    public class UpdateEnums
    {
        public class UpdateEntries
        {
            [JsonProperty("ToolVersion")]
            public string ToolVersion { get; set; }
            [JsonProperty("ProtocolVersion")]
            public string ProtocolVersion { get; set; }
            [JsonProperty("AzurBotVersion")]
            public string AzurBotVersion { get; set; }
            [JsonProperty("EditeursVersion")]
            public string EditorVersion { get; set; }
        }
        public class UpdateData
        {
            [JsonProperty("UpdateInfo")]
            public UpdateEntries UpdateEntries { get; set; }
        }
    }
}
