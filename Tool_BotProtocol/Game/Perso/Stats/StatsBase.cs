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
        /// <summary>Cinquième valeur d'un champ <c>As</c> quand le serveur l'envoie (total de PA/PM, boost répété ailleurs) ; ignorée par le client.</summary>
        public int? Extra { get; set; }
        /// <summary>Valeur affichée par la fiche du client : base, plus équipement et dons (<c>Xtra</c>), sans les boosts temporaires.</summary>
        public int Displayed => BasePerso + equipement + cadeau;

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
            Extra = null;
        }
    }
}
