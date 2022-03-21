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
        PRIVATE = 0x4b72c5
    }
}
