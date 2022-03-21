using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tool_BotProtocol.Game.Managers.Mouvements
{
    public enum MoveResults
    {
        EXIT,
        SAMECELL,
        FALL,
        PathfindingError,
        CellRangeError,
        CharacterBusyOrFull,
        CellNotWalkable,
        CellIsTypeOfInteractiveObject,
        MONSTER,
        PathfindingErrorCount

    }
}
