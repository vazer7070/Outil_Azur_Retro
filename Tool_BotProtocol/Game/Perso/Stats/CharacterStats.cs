using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tool_BotProtocol.Utils.Interfaces;

namespace Tool_BotProtocol.Game.Perso.Stats
{
    public class CharacterStats : IEliminable
    {
        public double ActualEXP { get; set; }
        public double MinExpNiv { get; set; }
        public double ExpNivNext { get; set; }
        public int ActualEnergy { get; set; }
        public int EnergyMax { get; set; }
        public int VitalityActual { get; set; }
        public int MaxVitality { get; set; }
        public StatsBase Initiative { get; set; }
        public StatsBase Propec { get; set;}
        public StatsBase PA { get; set; }
        public StatsBase PM { get; set; }
        public StatsBase Vita { get; set; }
        public StatsBase Sagesse { get; set; }
        public StatsBase Force {get; set;}
        public StatsBase Intell { get; set; }
        public StatsBase Chance { get; set; }
        public StatsBase Agility { get; set; }
        public StatsBase Atteignable { get; set; }
        public StatsBase Invoc { get; set; }
        public int Alignement { get; set; }
        public int AlignLVL { get; set; }
        public int Honor { get; set; }
        public int Dishonor { get; set; }
        public int GradeAli { get; set; }
        public bool HasWings { get; set; }
        public int LifePercent => MaxVitality == 0?0 : (int)((double)VitalityActual / MaxVitality * 100);

        public CharacterStats()
        {
            Initiative = new StatsBase(0, 0, 0, 0);
            Propec = new StatsBase(0, 0, 0, 0);
            PA = new StatsBase(0, 0, 0, 0);
            PM = new StatsBase(0, 0, 0, 0);
            Vita = new StatsBase(0, 0, 0, 0);
            Sagesse = new StatsBase(0, 0, 0, 0);
            Force = new StatsBase(0, 0, 0, 0);
            Intell = new StatsBase(0, 0, 0, 0);
            Chance = new StatsBase(0, 0, 0, 0);
            Agility = new StatsBase(0, 0, 0, 0);
            Atteignable = new StatsBase(0, 0, 0, 0);
            Invoc = new StatsBase(0, 0, 0, 0);
        }
        public int GetCapitalStatsBoost(byte id, StatsEnum id_stat)
        {
            switch (id_stat)
            {
                case StatsEnum.VITALITE:
                    return 1;
                case StatsEnum.SAGESSE:
                    return 3;
                case StatsEnum.FORCE:
                    switch (id)
                    {
                        case 1:
                            if (Force.BasePerso < 50)
                                return 2;
                            if (Force.BasePerso < 150)
                                return 3;
                            if (Force.BasePerso < 250)
                                return 4;
                            return 5;

                        case 11:
                            return 3;

                        case 5:
                            if (Force.BasePerso < 50)
                                return 2;
                            if (Force.BasePerso < 150)
                                return 3;
                            if (Force.BasePerso < 250)
                                return 4;
                            return 5;

                        case 4:
                            if (Force.BasePerso < 100)
                                return 1;
                            if (Force.BasePerso < 200)
                                return 2;
                            if (Force.BasePerso < 300)
                                return 3;
                            if (Force.BasePerso < 400)
                                return 4;
                            return 5;

                        case 2:
                            if (Force.BasePerso < 50)
                                return 2;
                            if (Force.BasePerso < 150)
                                return 3;
                            if (Force.BasePerso < 250)
                                return 4;
                            return 5;

                        case 7:
                            if (Force.BasePerso < 50)
                                return 2;
                            if (Force.BasePerso < 150)
                                return 3;
                            if (Force.BasePerso < 250)
                                return 4;
                            return 5;

                        case 12:
                            if (Force.BasePerso < 50)
                                return 1;
                            if (Force.BasePerso < 200)
                                return 2;
                            return 3;

                        case 10:
                            if (Force.BasePerso < 50)
                                return 1;
                            if (Force.BasePerso < 250)
                                return 2;
                            if (Force.BasePerso < 300)
                                return 3;
                            if (Force.BasePerso < 400)
                                return 4;
                            return 5;

                        case 9:
                            if (Force.BasePerso < 50)
                                return 1;
                            if (Force.BasePerso < 150)
                                return 2;
                            if (Force.BasePerso < 250)
                                return 3;
                            if (Force.BasePerso < 350)
                                return 4;
                            return 5;

                        case 3:
                            if (Force.BasePerso < 50)
                                return 1;
                            if (Force.BasePerso < 150)
                                return 2;
                            if (Force.BasePerso < 250)
                                return 3;
                            if (Force.BasePerso < 350)
                                return 4;
                            return 5;

                        case 6:
                            if (Force.BasePerso < 100)
                                return 1;
                            if (Force.BasePerso < 200)
                                return 2;
                            if (Force.BasePerso < 300)
                                return 3;
                            if (Force.BasePerso < 400)
                                return 4;
                            return 5;

                        case 8:
                            if (Force.BasePerso < 100)
                                return 1;
                            if (Force.BasePerso < 200)
                                return 2;
                            if (Force.BasePerso < 300)
                                return 3;
                            if (Force.BasePerso < 400)
                                return 4;
                            return 5;
                    }
                    break;
                case StatsEnum.AGILITE:
                    switch (id)
                    {
                        case 1:
                            if (Agility.BasePerso < 20)
                                return 1;
                            if (Agility.BasePerso < 40)
                                return 2;
                            if (Agility.BasePerso < 60)
                                return 3;
                            if (Agility.BasePerso < 80)
                                return 4;
                            return 5;

                        case 5:
                            if (Agility.BasePerso < 20)
                                return 1;
                            if (Agility.BasePerso < 40)
                                return 2;
                            if (Agility.BasePerso < 60)
                                return 3;
                            if (Agility.BasePerso < 80)
                                return 4;
                            return 5;

                        case 11:
                            return 3;

                        case 4:
                            if (Agility.BasePerso < 100)
                                return 1;
                            if (Agility.BasePerso < 200)
                                return 2;
                            if (Agility.BasePerso < 300)
                                return 3;
                            if (Agility.BasePerso < 400)
                                return 4;
                            return 5;

                        case 10:
                            if (Agility.BasePerso < 20)
                                return 1;
                            if (Agility.BasePerso < 40)
                                return 2;
                            if (Agility.BasePerso < 60)
                                return 3;
                            if (Agility.BasePerso < 80)
                                return 4;
                            return 5;

                        case 12:
                            if (Agility.BasePerso < 50)
                                return 1;
                            if (Agility.BasePerso < 200)
                                return 2;
                            return 3;

                        case 7:
                            if (Agility.BasePerso < 20)
                                return 1;
                            if (Agility.BasePerso < 40)
                                return 2;
                            if (Agility.BasePerso < 60)
                                return 3;
                            if (Agility.BasePerso < 80)
                                return 4;
                            return 5;

                        case 8:
                            if (Agility.BasePerso < 20)
                                return 1;
                            if (Agility.BasePerso < 40)
                                return 2;
                            if (Agility.BasePerso < 60)
                                return 3;
                            if (Agility.BasePerso < 80)
                                return 4;
                            return 5;

                        

                        case 6:
                            if (Agility.BasePerso < 50)
                                return 1;
                            if (Agility.BasePerso < 100)
                                return 2;
                            if (Agility.BasePerso < 150)
                                return 3;
                            if (Agility.BasePerso < 200)
                                return 4;
                            return 5;

                        case 9:
                            if (Agility.BasePerso < 50)
                                return 1;
                            if (Agility.BasePerso < 100)
                                return 2;
                            if (Agility.BasePerso < 150)
                                return 3;
                            if (Agility.BasePerso < 200)
                                return 4;
                            return 5;

                        case 2:
                            if (Agility.BasePerso < 20)
                                return 1;
                            if (Agility.BasePerso < 40)
                                return 2;
                            if (Agility.BasePerso < 60)
                                return 3;
                            if (Agility.BasePerso < 80)
                                return 4;
                            return 5;
                    }
                    break;

                case StatsEnum.INTELLIGENCE:
                    switch (id)
                    {
                        case 5:
                            if (Intell.BasePerso < 100)
                                return 1;
                            if (Intell.BasePerso < 200)
                                return 2;
                            if (Intell.BasePerso < 300)
                                return 3;
                            if (Intell.BasePerso < 400)
                                return 4;
                            return 5;

                        case 1:
                            if (Intell.BasePerso < 100)
                                return 1;
                            if (Intell.BasePerso < 200)
                                return 2;
                            if (Intell.BasePerso < 300)
                                return 3;
                            if (Intell.BasePerso < 400)
                                return 4;
                            return 5;

                        case 11:
                            return 3;

                        case 10:
                            if (Intell.BasePerso < 100)
                                return 1;
                            if (Intell.BasePerso < 200)
                                return 2;
                            if (Intell.BasePerso < 300)
                                return 3;
                            if (Intell.BasePerso < 400)
                                return 4;
                            return 5;

                        case 4:
                            if (Intell.BasePerso < 50)
                                return 2;
                            if (Intell.BasePerso < 150)
                                return 3;
                            if (Intell.BasePerso < 250)
                                return 4;
                            return 5;

                        case 3:
                            if (Intell.BasePerso < 20)
                                return 1;
                            if (Intell.BasePerso < 60)
                                return 2;
                            if (Intell.BasePerso < 100)
                                return 3;
                            if (Intell.BasePerso < 140)
                                return 4;
                            return 5;

                        case 12:
                            if (Intell.BasePerso < 50)
                                return 1;
                            if (Intell.BasePerso < 200)
                                return 2;
                            return 3;

                        case 8:
                            if (Intell.BasePerso < 20)
                                return 1;
                            if (Intell.BasePerso < 40)
                                return 2;
                            if (Intell.BasePerso < 60)
                                return 3;
                            if (Intell.BasePerso < 80)
                                return 4;
                            return 5;

                        case 7:
                            if (Intell.BasePerso < 100)
                                return 1;
                            if (Intell.BasePerso < 200)
                                return 2;
                            if (Intell.BasePerso < 300)
                                return 3;
                            if (Intell.BasePerso < 400)
                                return 4;
                            return 5;

                        case 2:
                            if (Intell.BasePerso < 100)
                                return 1;
                            if (Intell.BasePerso < 200)
                                return 2;
                            if (Intell.BasePerso < 300)
                                return 3;
                            if (Intell.BasePerso < 400)
                                return 4;
                            return 5;

                        case 9:
                            if (Intell.BasePerso < 50)
                                return 1;
                            if (Intell.BasePerso < 150)
                                return 2;
                            if (Intell.BasePerso < 250)
                                return 3;
                            if (Intell.BasePerso < 350)
                                return 4;
                            return 5;

                        case 6:
                            if (Intell.BasePerso < 20)
                                return 1;
                            if (Intell.BasePerso < 40)
                                return 2;
                            if (Intell.BasePerso < 60)
                                return 3;
                            if (Intell.BasePerso < 80)
                                return 4;
                            return 5;

                    }
                    break;
                case StatsEnum.CHANCE:
                    switch (id)
                    {
                        case 1:
                            if (Chance.BasePerso < 20)
                                return 1;
                            if (Chance.BasePerso < 40)
                                return 2;
                            if (Chance.BasePerso < 60)
                                return 3;
                            if (Chance.BasePerso < 80)
                                return 4;
                            return 5;

                        case 3:
                            if (Chance.BasePerso < 20)
                                return 1;
                            if (Chance.BasePerso < 40)
                                return 2;
                            if (Chance.BasePerso < 60)
                                return 3;
                            if (Chance.BasePerso < 80)
                                return 4;
                            return 5;

                        case 5:
                            if (Chance.BasePerso < 20)
                                return 1;
                            if (Chance.BasePerso < 40)
                                return 2;
                            if (Chance.BasePerso < 60)
                                return 3;
                            if (Chance.BasePerso < 80)
                                return 4;
                            return 5;

                        case 11:
                            return 3;

                        case 4:
                            if (Chance.BasePerso < 20)
                                return 1;
                            if (Chance.BasePerso < 40)
                                return 2;
                            if (Chance.BasePerso < 60)
                                return 3;
                            if (Chance.BasePerso < 80)
                                return 4;
                            return 5;

                        case 10:
                            if (Chance.BasePerso < 100)
                                return 1;
                            if (Chance.BasePerso < 200)
                                return 2;
                            if (Chance.BasePerso < 300)
                                return 3;
                            if (Chance.BasePerso < 400)
                                return 4;
                            return 5;

                        case 12:
                            if (Chance.BasePerso < 50)
                                return 1;
                            if (Chance.BasePerso < 200)
                                return 2;
                            return 3;

                        case 8:
                            if (Chance.BasePerso < 20)
                                return 1;
                            if (Chance.BasePerso < 40)
                                return 2;
                            if (Chance.BasePerso < 60)
                                return 3;
                            if (Chance.BasePerso < 80)
                                return 4;
                            return 5;

                        case 2:
                            if (Chance.BasePerso < 100)
                                return 1;
                            if (Chance.BasePerso < 200)
                                return 2;
                            if (Chance.BasePerso < 300)
                                return 3;
                            if (Chance.BasePerso < 400)
                                return 4;
                            return 5;

                        case 6:
                            if (Chance.BasePerso < 20)
                                return 1;
                            if (Chance.BasePerso < 40)
                                return 2;
                            if (Chance.BasePerso < 60)
                                return 3;
                            if (Chance.BasePerso < 80)
                                return 4;
                            return 5;

                        case 7:
                            if (Chance.BasePerso < 20)
                                return 1;
                            if (Chance.BasePerso < 40)
                                return 2;
                            if (Chance.BasePerso < 60)
                                return 3;
                            if (Chance.BasePerso < 80)
                                return 4;
                            return 5;

                        case 9:
                            if (Chance.BasePerso < 20)
                                return 1;
                            if (Chance.BasePerso < 40)
                                return 2;
                            if (Chance.BasePerso < 60)
                                return 3;
                            if (Chance.BasePerso < 80)
                                return 4;
                            return 5;
                    }
                    break;
            }
            return 5;
        }

        public void Clear()
        {
            ActualEXP = 0;
            MinExpNiv = 0;
            ExpNivNext = 0;
            ActualEnergy = 0;
            EnergyMax = 0;
            VitalityActual = 0;
            MaxVitality = 0;
            Alignement = 0;
            AlignLVL = 0;
            Honor = 0;
            Dishonor = 0;
            GradeAli = 0;
            HasWings = false;

            Initiative.Clear();
            Propec.Clear();
            PA.Clear();
            PM.Clear();
            Vita.Clear();
            Sagesse.Clear();
            Force.Clear();
            Intell.Clear();
            Chance.Clear();
            Agility.Clear();
            Atteignable.Clear();
            Invoc.Clear();
        }
    }
}
