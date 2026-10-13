using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Jobs;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Interfaces;
using Tool_BotProtocol.Game.Perso.Inventory;
using Tool_BotProtocol.Game.Perso.Spells;
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
        public int GFX { get; set; }
        public int Orientation { get; set; } = 2;
        public int GraphicsScaleX { get; set; } = 100;
        public int GraphicsScaleY { get; set; } = 100;
        public Cell Cell { get; set; }
        private Accounts.Accounts Accounts { get; set; }
        public CharacterStats stats { get; set; }
        /// <summary>Boutons « + » de la fiche : <c>AB&lt;code&gt;</c> (lot F13a).</summary>
        public StatsActions StatsActions { get; private set; }
        /// <summary>Amélioration, oubli, option « tous les sorts » et bonus d'objets : <c>SB</c>, <c>SF</c>, <c>SLo</c> (lot F13a).</summary>
        public SpellBook SpellBook { get; private set; }
        public InventoryClass Inventory { get; set; }
        public ConcurrentDictionary<short, Spell> Spells { get; set; }
        public int Carac_Points { get; set; } = 0;
        public int SpellPoints { get; set; }
        public int Kamas { get; set; }
        public Timer Regen_Timer { get; set; }
        public Timer AFK_Timer { get; set; }
        public string Canal { get; set; } = string.Empty;
        public bool InGroupe { get; set; }
        public string EquipLeader { get; set; }
        public List<Jobs.Jobs> Jobs { get; private set; }

        public bool HasGuild { get; set; }
        public ConcurrentDictionary<string, bool> InEquip;
        public ConcurrentDictionary<int, string> GroupMembers { get; } = new ConcurrentDictionary<int, string>();

        public bool UseMount { get; set; } = false;
        public sbyte NpcToSpeak_id { get; set; }
        /// <summary>Métier dont l'outil est équipé, annoncé par OT ; null lorsque OT arrive sans identifiant.</summary>
        public int? CurrentJobTool { get; set; }

        public event Action Server_Selection;
        public event Action Player_Selection;
        public event Action<string> ChatPrivate;
        public event Action RefreshCaracteristiques;
        public event Action PodsRefresh;
        public event Action Spells_Refresh;
        public event Action Jobs_Refresh;
        public event Action PNJ_receiveAnswer;
        public event Action PNJ_StopSpeaking;
        public event Action SeeLifeRegen;
        public event Action<List<Cell>> MoveMinimapPathfinding;

        public CharacterClass(Accounts.Accounts A)
        {
            InEquip = new ConcurrentDictionary<string, bool>();
            Accounts = A;
            Inventory = new InventoryClass(A);
            AFK_Timer = new Timer(No_AFK, null, Timeout.Infinite, Timeout.Infinite);
            Regen_Timer = new Timer(RegenCallback, null, Timeout.Infinite, Timeout.Infinite);
            Spells = new ConcurrentDictionary<short, Spell>();
            stats = new CharacterStats();
            StatsActions = new StatsActions(A, this);
            SpellBook = new SpellBook(A, this);
            Jobs = new List<Jobs.Jobs>();

        }
        public void DisplayRegen()
        {
            SeeLifeRegen?.Invoke();
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
            GFX = race * 10 + s;
        }
        public void PodsRefreshEvent() => PodsRefresh?.Invoke();
        public void JobsRefreshEvent() => Jobs_Refresh?.Invoke();
        public void SpellsRefreshEvent() => Spells_Refresh?.Invoke();
        public void ReceiveAnswerPNJ() => PNJ_receiveAnswer?.Invoke();
        public void AskPNJEvent() => PNJ_StopSpeaking?.Invoke();
        public void PersoSelectedEvent() => Player_Selection?.Invoke();
        public void ServerSelectedEvent() => Server_Selection?.Invoke();
        public void PathFindingMapPerso(List<Cell> Liste) => MoveMinimapPathfinding?.Invoke(Liste);
        public Jobs.Jobs[] GetJobsSnapshot() { lock (Jobs) return Jobs.ToArray(); }
        public IEnumerable<JobSkills> GetAvailableSkills()
        {
            lock (Jobs) return Jobs.SelectMany(job => job.Skills).ToArray();
        }

        private async void No_AFK(object state)
        {
            try
            {
                if (Accounts?.Connexion != null && Accounts.AccountStates != AccountStates.DISCONNECTED)
                    await Accounts.Connexion.SendPacket("ping");
            }catch (Exception e)
            {
                Accounts?.Logger?.LogError("[NO AFK TIMER]", $"{e.Message}");
            }
        }
        /// <summary>
        /// Paquet <c>As</c> complet (51 champs chez StarLoco) : la fiche est lue entièrement puis remplacée d'un coup ;
        /// un paquet illisible est journalisé et laisse la fiche précédente intacte. Renvoie vrai si la fiche a changé.
        /// </summary>
        public bool RefreshCaracs(string msg)
        {
            if (!CharacterStats.TryParse(msg, out CharacterStats parsed, out string error))
            {
                Accounts?.Logger?.LogDanger("CARACTÉRISTIQUES", "Paquet As illisible ignoré : " + error + ".");
                return false;
            }
            stats = parsed;
            Kamas = parsed.Kamas;
            Carac_Points = parsed.CapitalPoints;
            SpellPoints = parsed.SpellPoints;
            RefreshCaracteristiques?.Invoke();
            return true;
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
                Accounts?.Logger?.LogError("TIMER-REGEN", $"Problème avec la régenération {e.Message}");
            }
           
        }
        public void Clear()
        {
            AFK_Timer?.Change(Timeout.Infinite, Timeout.Infinite);
            Regen_Timer?.Change(Timeout.Infinite, Timeout.Infinite);
            id = 0;
            Name = null;
            Level = Sex = Race_ID = 0;
            GFX = 0; Orientation = 2; GraphicsScaleX = GraphicsScaleY = 100;
            Cell = null;
            stats = new CharacterStats();
            SpellBook.Clear();
            Inventory.Clear();
            Spells.Clear();
            lock (Jobs) Jobs.Clear();
            InEquip.Clear();
            GroupMembers.Clear();
            InGroupe = HasGuild = UseMount = false;
            CurrentJobTool = null;
            EquipLeader = null;
            Canal = string.Empty;
            Carac_Points = SpellPoints = Kamas = 0;
        }

        public void Dispose()
        {
            AFK_Timer?.Dispose();
            Regen_Timer?.Dispose();
            AFK_Timer = Regen_Timer = null;
            Inventory?.Dispose();
            Spells.Clear();
            lock (Jobs) Jobs.Clear();
            InEquip.Clear();
            GroupMembers.Clear();
            Accounts = null;
        }
    }
}
