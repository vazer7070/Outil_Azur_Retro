using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Jobs;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Interfaces;
using Tool_BotProtocol.Game.Perso.Inventory;
using Tool_BotProtocol.Game.Perso.Stats;
using Tool_BotProtocol.Utils.Interfaces;

namespace Tool_BotProtocol.Game.Perso
{
    public class CharacterClass: Entites, IEliminable
    {
        public int id { get; set; } = 0;
        public string Name { get; set; }
        public byte Level { get; set; }
        public byte Sex { get; set; }
        public byte Race_ID { get; set; }
        public Cell Cell { get; set; }
        private Accounts.Accounts Accounts { get; set; }
        public CharacterStats stats { get; set; }
        public InventoryClass Inventory { get; set; }
        public int Carac_Points { get; set; } = 0;
        public int Kamas { get; set; }
        public Timer Regen_Timer { get; set; }
        public Timer AFK_Timer { get; set; }
        public string Canal { get; set; } = string.Empty;
        public bool InGroupe { get; set; }
        public string EquipLeader { get; set; }
        public List<Jobs.Jobs> Jobs { get; private set; }

        private bool disposed;
        public bool HasGuild { get; set; }
        public ConcurrentDictionary<string, bool> InEquip;

        public bool UseMount { get; set; } = false;
        public sbyte NpcToSpeak_id { get; set; }

        public event Action Server_Selection;
        public event Action Player_Selection;
        public event Action<string> ChatPrivate;
        public event Action RefreshCaracteristiques;
        public event Action PodsRefresh;
        public event Action Spells_Refresh;
        public event Action Jobs_Refresh;
        public event Action PNJ_receiveAnswer;
        public event Action PNJ_StopSpeaking;
        public event Action<List<Cell>> MoveMinimapPathfinding;

        public CharacterClass(Accounts.Accounts A)
        {
            InEquip = new ConcurrentDictionary<string, bool>();
            Accounts = A;
            Inventory = new InventoryClass(A);
            AFK_Timer = new Timer(No_AFK, null, Timeout.Infinite, Timeout.Infinite);
            Regen_Timer = new Timer(RegenCallback, null, Timeout.Infinite, Timeout.Infinite);

            stats = new CharacterStats();
            Jobs = new List<Jobs.Jobs>();

        }
        public void CheckWhoSpeak(string who)
        {
            ChatPrivate?.Invoke(who);
        }
        public void AddCanalPlayer(string canal)
        {
            if (canal.Length <= 1)
                Canal += canal;
            else
                Canal = canal;
        }
        public void DeleteCanalPlayer(string symbole) => Canal = Canal.Replace(symbole, string.Empty);
        public void SetPerso_Data(int i, string n, byte l, byte s, byte race)
        {
            id = i;
            Name = n;
            Level = l;
            Sex = s;
            Race_ID = race;
        }
        public void PodsRefreshEvent() => PodsRefresh?.Invoke();
        public void JobsRefreshEvent() => Jobs_Refresh?.Invoke();
        public void ReceiveAnswerPNJ() => PNJ_receiveAnswer?.Invoke();
        public void AskPNJEvent() => PNJ_StopSpeaking?.Invoke();
        public void PersoSelectedEvent() => Player_Selection?.Invoke();
        public void ServerSelectedEvent() => Server_Selection?.Invoke();
        public void PathFindingMapPerso(List<Cell> Liste) => MoveMinimapPathfinding?.Invoke(Liste);
        public IEnumerable<short> GetSkillsForRecolte() => Jobs.SelectMany(x => x.Skills.Where(y => !y.CanCraft).Select(y => y.Id));
        public IEnumerable<JobSkills> GetAvailableSkills() => Jobs.SelectMany(Jobs => Jobs.Skills.Select(s => s));

        private void No_AFK(object state)
        {
            try
            {
                if (Accounts.AccountStates != AccountStates.DISCONNECTED)
                    Accounts.Connexion.SendPacket("ping");
            }catch (Exception e)
            {
                Accounts.Logger.LogError("[NO AFK TIMER]", $"{e.Message}");
            }
        }
        public void RefreshCaracs(string msg)
        {
            string[] loc = msg.Substring(2).Split('|');
            string[] loc2 = loc[0].Split(',');
            string[] loc3 = loc[5].Split(',');
            string[] loc4 = loc[6].Split(',');

            stats.ActualEXP = double.Parse(loc2[0]);
            stats.MinExpNiv = double.Parse(loc2[1]);
            stats.ExpNivNext = double.Parse(loc2[2]);
            Kamas = int.Parse(loc[1]);
            Carac_Points = int.Parse(loc[2]);

            stats.VitalityActual = int.Parse(loc3[0]);
            stats.MaxVitality = int.Parse(loc3[1]);

            stats.ActualEnergy = int.Parse(loc4[0]);
            stats.EnergyMax = int.Parse(loc4[1]);

            if (stats.Initiative != null)
                stats.Initiative.BasePerso = int.Parse(loc[7]);
            else
                stats.Initiative = new StatsBase(int.Parse(loc[7]));

            if (stats.Propec != null)
                stats.Propec.BasePerso = int.Parse(loc[8]);
            else
                stats.Propec = new StatsBase(int.Parse(loc[8]));

            for(int i = 9; i <= 18; i++)
            {
                loc2 = loc[i].Split(',');
                int BP = int.Parse(loc2[0]);
                int Stuff = int.Parse(loc2[1]);
                int gift = int.Parse(loc2[2]);
                int boost = int.Parse(loc2[3]);

                switch (i)
                {
                    case 9:
                        stats.PA.RefreshStats(BP, Stuff, gift, boost);
                        break;

                   case 10:
                        stats.PM.RefreshStats(BP, Stuff, gift, boost);
                        break;
                    
                    case 11:
                        stats.Force.RefreshStats(BP, Stuff, gift, boost);
                        break;

                    case 12:
                        stats.Vita.RefreshStats(BP, Stuff, gift, boost);
                        break;

                    case 13:
                        stats.Sagesse.RefreshStats(BP, Stuff, gift, boost);
                        break;

                    case 14:
                        stats.Chance.RefreshStats(BP, Stuff, gift, boost);
                        break;

                    case 15:
                        stats.Agility.RefreshStats(BP, Stuff, gift, boost);
                        break;

                    case 16:
                        stats.Intell.RefreshStats(BP, Stuff, gift, boost);
                        break;

                    case 17:
                        stats.Atteignable.RefreshStats(BP, Stuff, gift, boost);
                        break;

                    case 18:
                        stats.Invoc.RefreshStats(BP, Stuff, gift, boost);
                        break;

                }
            }
            RefreshCaracteristiques?.Invoke();
        }
        private void RegenCallback(object state)
        {
            try
            {
                if(stats?.VitalityActual >= stats?.MaxVitality)
                {
                    Regen_Timer.Change(Timeout.Infinite, Timeout.Infinite);
                    return;
                }
                stats.VitalityActual++;
                RefreshCaracteristiques?.Invoke();
            }catch(Exception e)
            {
                Accounts.Logger.LogError("TIMER-REGEN", $"Problème avec la régenération {e}");
            }
           
        }
        public void Clear()
        {
            
        }

        public void Dispose()
        {
            
        }
    }
}
