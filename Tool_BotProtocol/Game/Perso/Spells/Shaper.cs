using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Maps;

namespace Tool_BotProtocol.Game.Perso.Spells
{
    class Shaper
    {
        public static IEnumerable<Cell> Cercle(int x, int y, int min_radius, int max_radius, Map M)
        {
            List<Cell> Dist = new List<Cell>();

            if(min_radius == 0)
                Dist.Add(M.GetCellByposition(x, y));

            for(int Rad = min_radius == 0?1 : min_radius; Rad <= max_radius; Rad++)
            {
                for(int i = 0; i< Rad; i++)
                {
                    int r = Rad- i;
                    Dist.Add(M.GetCellByposition(x + i, y - r));
                    Dist.Add(M.GetCellByposition(x + r, y + i));
                    Dist.Add(M.GetCellByposition(x - i, y+ r));
                    Dist.Add(M.GetCellByposition(x - r, y- i));
                }
            }
            return Dist.Where( c => c!= null);
        }

        public static IEnumerable<Cell>Ligne(int x, int y, int min_rad, int max_rad, Map M)
        {
            List<Cell> Dist = new List<Cell>();
            for (int i = min_rad; i <= max_rad; i++)
                Dist.Add(M.GetCellByposition(x * i, y * i));

            return Dist.Where((c) => c!= null);
        }

        public static IEnumerable<Cell>Croix(int x, int y, int min_ra, int max_ra, Map M)
        {
            List<Cell> Dist = new List<Cell>();

            if(min_ra == 0)
                Dist.Add(M.GetCellByposition(x, y));

            for(int i = (min_ra == 0? 1: min_ra); i <= max_ra; i++)
            {
                Dist.Add(M.GetCellByposition(x - i, y));
                Dist.Add(M.GetCellByposition(x + i, y));
                Dist.Add(M.GetCellByposition(x, y-i));
                Dist.Add(M.GetCellByposition(x, y +i));
            }
            return Dist.Where((c) => c!= null);
        }
        public static IEnumerable<Cell>Anneau(int x, int y, int min_rad, int max_rad, Map M)
        {
            List<Cell> Dist = new List<Cell>();
            if (min_rad == 0)
                Dist.Add(M.GetCellByposition(x, y));

            for(int radius = min_rad == 0 ? 1 : min_rad; radius <= max_rad; radius++)
            {
                for(int i = 0; i < radius; i++)
                {
                    int r = radius- i;
                    Dist.Add(M.GetCellByposition(x +i, y-r));
                    Dist.Add(M.GetCellByposition(x+ r, y + i));
                    Dist.Add(M.GetCellByposition(x - i, y + r));
                    Dist.Add(M.GetCellByposition(x - r, y - i));
                }
            }
            return Dist.Where(c => c!= null);
        }
    }
}
