using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tool_BotProtocol.Game.Maps.Interactives
{
    public class Interactives
    {
        public short gfx { get; set; }
        public Cell Cell { get; set; }
        public InteractivesParent Interactive { get; set; }
        public bool IsUsable { get; set; }

        public Interactives (short gfx_id, Cell cell)
        {
            gfx = gfx_id;
            Cell = cell;
            InteractivesParent M = InteractivesParent.ReturnByGFX(gfx_id);
            if(M != null)
            {
                Interactive = M;
                IsUsable = true;
            }
        }
    }
}
