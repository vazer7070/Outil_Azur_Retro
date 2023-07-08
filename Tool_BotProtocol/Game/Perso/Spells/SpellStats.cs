using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tool_BotProtocol.Game.Perso.Spells
{
    public class SpellStats
    {
        public byte PA { get; set; }
        public byte Min_portee { get; set; }
        public byte Max_portee { get; set; }

        public bool IsInLine { get; set; }
        public bool AvecLigneDeVue { get; set; }
        public bool EmptyCell { get; set; }
        public bool portee_modifiable { get; set; }

        public byte PerTurn { get; set; }
        public byte PerObjective { get; set; }
        public byte Interval { get; set; }

        public List<SpellEffect>NormalEffect { get; set; }
        public List<SpellEffect>CriticalEffect { get; set; }

        public SpellStats()
        {
            NormalEffect= new List<SpellEffect>();
            CriticalEffect= new List<SpellEffect>();
        }
        public void AddEffect(SpellEffect effect, bool critical)
        {
            if(critical)
                CriticalEffect.Add(effect);
            else
                NormalEffect.Add(effect);
        }
    }
}
