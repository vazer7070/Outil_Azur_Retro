using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game.Managers.Mouvements;
using Tool_BotProtocol.Utils.Crypto;

namespace Tool_BotProtocol.Game.Maps.Mouvements
{
    public class PathfinderUtils
    {
        private static readonly Dictionary<AnimType, AnimDuration> AnimTimeType = new Dictionary<AnimType, AnimDuration>()
        {
            {AnimType.MONTURE, new AnimDuration(135, 200, 120) },
            {AnimType.RAPIDE, new AnimDuration(170, 255, 150) },
            {AnimType.MARCHE, new AnimDuration(480, 510, 425) },
            {AnimType.GHOST, new AnimDuration(57, 58, 50) }
        };

        public static int GetTimeOnMap(Cell actualCell, List<Cell> CellsHover, bool monture = false)
        {
            int time = 20;
            AnimDuration animDuration;
            if (monture)
                animDuration = AnimTimeType[AnimType.MONTURE];
            else
                animDuration = CellsHover.Count > 6 ? AnimTimeType[AnimType.RAPIDE] : AnimTimeType[AnimType.MARCHE];
            Cell C;
            for(int i = 1; i < CellsHover.Count; i++)
            {
                C = CellsHover[i];
                if (actualCell.Y == C.Y)
                    time += animDuration.Horizontal;
                else if (actualCell.X == C.Y)
                    time += animDuration.Vertical;
                else
                    time += animDuration.Line;

                if (actualCell.layer_ground_Level < C.layer_ground_Level)
                    time += 100;
                else if(C.layer_ground_Level > actualCell.layer_ground_Level)
                    time -= 100;
                else if(actualCell.layer_ground_slope != C.layer_ground_slope)
                {
                    if (actualCell.layer_ground_slope == 1)
                        time += 100;
                    else if(C.layer_ground_slope == 1)
                        time -= 100;
                }
                actualCell = C;
            }
            return time;
        }
        public static string GetCleanRoad(List<Cell> Road)
        {
            Cell destination = Road.Last();

            if (Road.Count <= 2)
                return destination.GetCharDirection(Road.First()) + Hash.Get_Cell_Char(destination.CellID);

            StringBuilder pathfinder = new StringBuilder();
            char LastDirection = Road[1].GetCharDirection(Road.First()), actualDirection;

            for (int i = 2; i < Road.Count; i++)
            {
                Cell Actual = Road[i];
                Cell Back = Road[i - 1];
                actualDirection = Actual.GetCharDirection(Back);
                if (LastDirection != actualDirection)
                {
                    pathfinder.Append(LastDirection);
                    pathfinder.Append(Hash.Get_Cell_Char(Back.CellID));

                    LastDirection = actualDirection;
                }
            }
            pathfinder.Append(LastDirection);
            pathfinder.Append(Hash.Get_Cell_Char(destination.CellID));
            return pathfinder.ToString();
        }
    }
}
