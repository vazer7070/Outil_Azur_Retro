using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tool_BotProtocol.Game.Maps.Mouvements
{
    internal class AnimDuration
    {
        public int Line { get; set; }
        public int Horizontal { get; set; }
        public int Vertical { get; set; }

        public AnimDuration(int L, int H, int V)
        {
            Line = L;
            Horizontal = H;
            Vertical = V;
        }
    }
}
