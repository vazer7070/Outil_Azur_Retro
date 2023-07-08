using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tool_BotProtocol.Utils.Logger
{
    public enum LogTypes
    {
        ERROR = 0xd81a1a,
        WARNING = 0xd17e32,
        INFORMATION = 0x387a30,
        DEBUG = 0x4f5051,
        NORMAL = 0x000000,
        PRIVATE = 0x4b72c5,
        TCHATRECRUIT = 0xbfc9ca,
        TCHATCOMMERCE = 0xa04000,
        TCHATADMIN = 0x8e44ad,
        TCHATPRIVATE = 0x3498db,
        TCHATGUILD = 0x8e44ad,
        TCHATGROUP = 0x21618c,
        TCHATTEAM = 0xf4d03f

    }
}
