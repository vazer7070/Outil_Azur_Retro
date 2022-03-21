using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Interfaces;
using Tool_BotProtocol.Game.Perso.Inventory;
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
        public void PersoSelectedEvent() => Player_Selection?.Invoke();
        public void ServerSelectedEvent() => Server_Selection?.Invoke();
        public void PathFindingMapPerso(List<Cell> Liste) => MoveMinimapPathfinding?.Invoke(Liste);
        public IEnumerable<short> GetSkillsForRecolte() => Jobs.SelectMany(x => x.Skills.Where(y => !y.CanCraft).Select(y => y.Id));
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
        public void Clear()
        {
            
        }

        public void Dispose()
        {
            
        }
    }
}
