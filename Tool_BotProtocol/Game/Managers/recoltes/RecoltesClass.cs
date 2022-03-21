using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Managers.Mouvements;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Interactives;
using Tool_BotProtocol.Game.Maps.Mouvements;
using Tool_BotProtocol.Game.Perso;
using Tool_BotProtocol.Game.Perso.Inventory;
using Tool_BotProtocol.Game.Perso.Inventory.Enums;

namespace Tool_BotProtocol.Game.Managers.recoltes
{
    public class RecoltesClass : IDisposable
    {
        private Accounts.Accounts Account;
        private Map Map;
        public Interactives Interactive_recoltable;
        private List<int> NoUsable;
        private Pathfinder Pathfinder;
        private bool Disposed;
        private bool R;

        public event Action InitRecoltes;
        public event Action<RecolteEnum> RecolteFinish;

        public static readonly int[] FishingTool = { 8541, 6661, 596, 1866, 1865, 1864, 1867, 2188, 1863, 1862, 1868, 1861, 1860, 2366 };

        public RecoltesClass (Accounts.Accounts A, Mouvement M, Map map)
        {
            Account = A;
            Map = map;
            NoUsable = new List<int>();
            Pathfinder = new Pathfinder ();

            M.FinalizeMove += GetEndMove;
            map.RefreshMap += ActualiseMap;

        }
        private void ActualiseMap()
        {
            Pathfinder.SetMap(Account.Game.Map);
            NoUsable.Clear();
        }
        private bool GetMoveIntention(KeyValuePair<short, Interactives> In)
        {
            Interactive_recoltable = In.Value;
            byte Distance = 1;
            InventoryObjects weapon = Account.Game.character.Inventory.GetObjetsPosition(InventorySlots.WEAPON);
            if (weapon != null)
                Distance = GetToolDistance(weapon.ID);
            switch(Account.Game.Manager.Mouvements.GetCellsMove(Interactive_recoltable.Cell, Map.CellsOccuped(), true, Distance))
            {
                case MoveResults.EXIT:
                case MoveResults.SAMECELL:
                    GetRecolteIntention();
                    return true;
                default:
                    CancelRecolte();
                    return false;
                        
            }
        }
        public async Task InitRecolte(int id_perso, int delay, short cellid, short type)
        {
            if (Interactive_recoltable == null || Interactive_recoltable.Cell.CellID != cellid)
                return;
            if(Account.Game.character.id != id_perso)
            {
                R = true;
                Account.Logger.LogInfo("RÉCOLTE", $"Le personnage {id_perso} à volé votre ressource.");
                EventEndRecolte(RecolteEnum.VOLÉ, Interactive_recoltable.Cell.CellID);
            }
            else
            {
                Account.AccountStates = Accounts.AccountStates.GATHERING;
                InitRecoltes?.Invoke();
                await Task.Delay(delay);
                Account.Connexion.SendPacket($"GKK{type}");
            }
        }
        private void GetRecolteIntention()
        {
            if (!R)
            {
                foreach(short capa in Interactive_recoltable.Interactive.Capacities)
                {
                    if (Account.Game.character.GetSkillsForRecolte().Contains(capa))
                        Account.Connexion.SendPacket($"GA500{Interactive_recoltable.Cell.CellID};{capa}");
                }
            }
            else 
                EventEndRecolte(RecolteEnum.VOLÉ, Interactive_recoltable.Cell.CellID);

        }
        public Dictionary<short, Interactives>GetInteractivesCultivable(List<short> ElementsID)
        {
            Dictionary<short, Interactives>UsableElements = new Dictionary<short, Interactives>();
            CharacterClass perso = Account.Game.character;

            InventoryObjects Weapon = perso.Inventory.GetObjetsPosition(InventorySlots.WEAPON);
            byte W_distance = 1;
            bool isFishingTool = false;
            if(Weapon != null)
            {
                W_distance = GetToolDistance(Weapon.ID);
                isFishingTool = FishingTool.Contains(Weapon.ID);
            }
            foreach (Interactives I in Map.Interactives.Values.OrderBy(x => x.Cell.GetDistanceBetweenCells(perso.Cell)))
            {
                if(!I.IsUsable || !I.Interactive.Recoltable)
                    continue;
                List<Cell> path = Pathfinder.GetPath(perso.Cell, I.Cell, Map.CellsOccuped(), true, W_distance);
                if (path == null || path.Count == 0)
                    continue;
                foreach(short H in I.Interactive.Capacities)
                {
                    if (!ElementsID.Contains(H))
                        continue;
                    if (!isFishingTool && path.Last().GetDistanceBetweenCells(I.Cell) > 1)
                        continue;
                    if (isFishingTool && path.Last().GetDistanceBetweenCells(I.Cell) > W_distance)
                        continue;
                    UsableElements.Add(I.Cell.CellID, I);
                }
            }
            return UsableElements;
        }
        public byte GetToolDistance(int id)
        {
            switch (id)
            {
                case 8541://Canne d'apprenti pêcheur
                case 6661://Canne pour kuakuas
                case 596://canne à pêche courte
                    return 2;

                case 1866://canne à pêche standard
                    return 3;

                case 1865://canne cubique
                case 1864://l'aiguille à tricoter
                    return 4;

                case 1867://super canne à pêche
                case 2188://tige pour pischis
                    return 5;

                case 1863://roseau Jillo
                case 1862://Le bâton de l'amour
                    return 6;

                case 1868: //canne à pêche téléscopique
                    return 7;

                case 1861://la grande perche
                case 1860://tige de harpon
                    return 8;

                case 2366://Canne Kralamar
                    return 9;
            }

            return 1;
        }
        public bool GetRecolte(List<short> Elements)
        {
            if(Account.Isbusy()|| Interactive_recoltable != null)
                return false;
            foreach(KeyValuePair<short, Interactives> U in GetInteractivesCultivable(Elements))
            {
                if(GetMoveIntention(U))
                    return true;
            }
            Account.Logger.LogDanger("RÉCOLTE", "Aucunes ressources à cet endroit");
            return false;
        }
        public bool CanCollect(List<short>RecoltElements) => GetInteractivesCultivable(RecoltElements).Count > 0;
        public void CancelRecolte() => Interactive_recoltable = null;

        private void GetEndMove(bool correct)
        {
            if (Interactive_recoltable == null)
                return;
            if(!correct && Account.Game.Manager.Mouvements.ActualPath != null)
                EventEndRecolte(RecolteEnum.FALL, Interactive_recoltable.Cell.CellID);
        }
        public void EventEndRecolte(RecolteEnum result, short cellid)
        {
            if (Interactive_recoltable == null || Interactive_recoltable.Cell.CellID != cellid)
                return;
            R = false;
            Interactive_recoltable = null;
            Account.AccountStates = Accounts.AccountStates.CONNECTED_INACTIVE;
            RecolteFinish?.Invoke(result);
        }
        public void Clear()
        {
            Interactive_recoltable = null;
            NoUsable.Clear();
            R = false;
        }
        ~RecoltesClass() => Dispose(true);
        public void Dispose() => Dispose(true);

        protected virtual void Dispose(bool d)
        {
            if (!Disposed)
            {
                if (d)
                {
                    Pathfinder.Dispose();
                }
                NoUsable.Clear();
                NoUsable = null;
                Interactive_recoltable = null;
                Pathfinder = null;
                Account = null;
                Disposed = true;
            }
        }
    }
}
