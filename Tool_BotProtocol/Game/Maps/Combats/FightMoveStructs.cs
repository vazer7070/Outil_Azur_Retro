using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tool_BotProtocol.Game.Maps.Combats
{
    public class FightMoveStructs
    {
        public List<short> CellsAccessibles { get; set; }
        public List<short> CellsNoAccessible { get; set; }
        public Dictionary<short, int> CellsAccessibleMap { get; set; }
        public Dictionary<short, int> CellsNonAccessibleMap { get; set; }
    }
}
