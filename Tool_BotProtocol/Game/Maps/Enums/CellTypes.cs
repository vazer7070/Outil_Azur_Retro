using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tool_BotProtocol.Game.Maps.Enums
{
    public enum CellTypes
    {
        NOT_WALKABLE = 0,
        INTERACTIVE_OBJECT = 1,
        TELEPORT_CELL = 2,
        UNKNOWN1 = 3,
        WALKABLE = 4,
        UNKNOWN2 = 5,
        PATH_1 = 6,
        PATH_2 = 7
    }
}
