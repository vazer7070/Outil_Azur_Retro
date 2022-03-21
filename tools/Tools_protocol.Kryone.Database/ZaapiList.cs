using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools_protocol.Json;

namespace Tools_protocol.Kryone.Database
{
    public  class ZaapiList
    {
        public int MapID { get; set; }
        public int Align { get; set; }

        public string TableZaapi = JsonManager.SearchAuth("zaapi");
        public static Dictionary<int, ZaapiList> AllZaapi = new Dictionary<int, ZaapiList>();

    }
}
