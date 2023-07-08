using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tool_BotProtocol.Utils.Crypto;

namespace Tool_BotProtocol.Game.Perso.Spells
{
    public class Zones
    {
        public SpellActionZone Type { get; set; }
        public int taille { get; set; }

        public Zones(SpellActionZone SpellType, int SpellSize)
        {
            Type = SpellType;
            taille= SpellSize;
        }

        public static Zones Parse(string zone)
        {
            if (zone.Length != 2)
                throw new ArgumentException("Zone invalide");

            SpellActionZone type;

            switch(zone[0])
            {
                case 'P':
                    type = SpellActionZone.SOLO;
                    break;

                case 'C':
                    type = SpellActionZone.CERCLE;
                        break;

                case 'L':
                    type = SpellActionZone.LIGNE;
                    break;

                case 'X':
                    type = SpellActionZone.CROIX;
                    break;

                case 'O':
                    type = SpellActionZone.ANNEAU;
                    break;

                case 'R':
                    type = SpellActionZone.RECTANGLE;
                    break;

                case 'T':
                    type = SpellActionZone.TLIGNE;
                    break;

                default:
                    type = SpellActionZone.SOLO;
                    break;
            }
            return new Zones(type, Hash.get_Hash(zone[1]));
        }
    }
}
