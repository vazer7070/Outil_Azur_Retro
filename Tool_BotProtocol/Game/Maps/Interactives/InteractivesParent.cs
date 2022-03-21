using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tool_BotProtocol.Game.Maps.Interactives
{
    public class InteractivesParent
    {
        public short[] GFX_Arrays { get; private set; }
        public bool Walkable { get; private set; }
        public short[] Capacities { get; private set; }
        public string Name { get; private set; }
        public bool Recoltable { get; private set; }
        
        static List<InteractivesParent> interactivesParents = new List<InteractivesParent>();
        public InteractivesParent(string Nam, string GFX, bool walkable, string capa, bool CanRecolte)
        {
            Name = Nam;
            if(!GFX.Equals("-1") && !string.IsNullOrEmpty(GFX))
            {
                string[] str = GFX.Split(',');
                GFX_Arrays = new short[str.Length];
                for(byte i = 0; i < GFX.Length; i++)
                {
                    GFX_Arrays[i] = short.Parse(str[i]);
                }
            }
            Walkable = walkable;
            if(!capa.Equals("-1") && !string.IsNullOrEmpty(capa))
            {
                string[] str = capa.Split(',');
                Capacities = new short[str.Length];
                for(byte j = 0; j < Capacities.Length; ++j)
                {
                    Capacities[j] = short.Parse(str[j]);
                }
            }
            Recoltable = CanRecolte;
            interactivesParents.Add(this);
        }
        public static InteractivesParent ReturnByGFX(short gfx_id)
        {
            return interactivesParents.FirstOrDefault(x => x.GFX_Arrays.Contains(gfx_id));
        }
        public static InteractivesParent GetInteractiveBySkill(short skillID)
        {
            IEnumerable<InteractivesParent> Interactivelist = interactivesParents.Where(x => x.Capacities != null);
            foreach(InteractivesParent iP in Interactivelist)
            {
                if(iP.Capacities.Contains(skillID))
                    return iP;
            }
            return null;
        } 
        public static List<InteractivesParent> GetInteractives() => interactivesParents;
    }
}
