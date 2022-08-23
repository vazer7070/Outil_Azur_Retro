using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tool_BotProtocol.Utils.Interfaces;

namespace Tool_BotProtocol.Game.Perso.Stats
{
    public class StatsBase : IEliminable
    {
        public int BasePerso { get; set; }
        public int equipement { get; set; }
        public int cadeau { get; set; }
        public int Boost { get; set; }

        public StatsBase(int PersoBase) => BasePerso = PersoBase;
        public int StatsTotal => BasePerso + equipement + cadeau + Boost;
        public StatsBase(int PersoBase, int equipement, int gift, int boost) => RefreshStats(PersoBase, equipement, gift, boost);

        public void RefreshStats(int baseP, int stuff, int gift, int boost)
        {
            BasePerso = baseP;
            equipement = stuff;
            cadeau = gift;
            Boost = boost;
        }

        public void Clear()
        {
            BasePerso = 0;
            equipement = 0;
            cadeau = 0;
            Boost = 0;
        }
    }
}
