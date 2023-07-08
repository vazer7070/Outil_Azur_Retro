using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Maps;

namespace Tool_BotProtocol.Game.Perso.Spells
{
    public class SpellShape
    {
        public static IEnumerable<Cell>ListOfCellForSpell(Cell cell, SpellStats spellLevel, Map M, int additionalRange = 0)
        {
            int MaxRange = spellLevel.Max_portee + (spellLevel.portee_modifiable ? additionalRange : 0);

            if (spellLevel.IsInLine)
                return Shaper.Croix(cell.X, cell.Y, spellLevel.Min_portee, MaxRange, M);
            else
                return Shaper.Anneau(cell.X, cell.Y, spellLevel.Min_portee, MaxRange, M);
        }
    }
}
