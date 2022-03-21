using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tool_BotProtocol.Game.Maps.Combats
{
    public class FightMoveNode
    {
        public short InitCell { get; set; }
         public bool CanAccess { get; set; }

        public FightMoveStructs Marche { get; set; }
        public FightMoveNode(short cell, bool A)
        {
            InitCell = cell;
            CanAccess = A;
        }
    }
}
