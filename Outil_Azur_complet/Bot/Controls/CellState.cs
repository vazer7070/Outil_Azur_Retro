using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Outil_Azur_complet.Bot.Controls
{
    public enum CellState
    {
        WALKABLE,
        NO_WALKABLE,
        FIGHT_TEAM_BLUE,
        FIGHT_TEAM_RED,
        TRIGGER,
        INTERACTIVE,
        OBSTACLE,
        PATHFINDER
    }
}
