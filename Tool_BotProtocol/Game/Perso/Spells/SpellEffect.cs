using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tool_BotProtocol.Game.Perso.Spells
{
    public class SpellEffect
    {
        public int Id { get; set; }
        public Zones ZoneEffet { get; set; }

        public SpellEffect(int ID, Zones Z) 
        { 
            Id = ID;
            ZoneEffet = Z;
        }
    }
}
