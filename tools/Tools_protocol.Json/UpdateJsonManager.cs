using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Tools_protocol.Json
{
    public class UpdateJsonManager
    {
        public static bool NeedMaj(string path, string Tv, string Pv, string AZv)
        {
            try
            {
                using (WebClient wc = new WebClient())
                {
                    var json = wc.DownloadString(path);
                    UpdateEnums.UpdateEntries maj = JsonConvert.DeserializeObject<UpdateEnums.UpdateData>(json).UpdateEntries;
                    if (maj.ToolVersion != Tv)
                        return true;
                    if (maj.ProtocolVersion != Pv)
                        return true;
                    if (maj.AzurBotVersion != AZv)
                        return true;
                    else
                        return false;
                }

            }catch(Exception)
            {
                return false;
            }
        }
    }
}
