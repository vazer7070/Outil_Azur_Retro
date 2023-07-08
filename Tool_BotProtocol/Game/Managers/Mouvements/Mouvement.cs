using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Combats;
using Tool_BotProtocol.Game.Maps.Mouvements;
using Tool_BotProtocol.Game.Perso;
using Tool_BotProtocol.Utils.Crypto;

namespace Tool_BotProtocol.Game.Managers.Mouvements
{
    public class Mouvement : IDisposable
    {
        private Accounts.Accounts Account;
        private CharacterClass Perso;
        private Map map;
        public Pathfinder Pathfinder;
        public List<Cell> ActualPath;

        public event Action<bool> FinalizeMove;
        private bool disposed;

        public Mouvement(Accounts.Accounts A, Map M, CharacterClass Character)
        {
            Account = A;
            map = M;
            Perso = Character;

            Pathfinder = new Pathfinder();
            map.RefreshMap += ActualiseMap;
        }
        public bool CanChangeMap(TeleportCellEnum Direction, Cell cell)
        {
            switch (Direction)
            {
                case TeleportCellEnum.LEFT:
                    return (cell.X - 1) == cell.Y;
               case TeleportCellEnum.RIGHT:
                    return (cell.X - 27) == cell.Y;
                case TeleportCellEnum.BOTTOM:
                    return (cell.X + cell.Y) == 31;
               case TeleportCellEnum.TOP:
                    return cell.Y < 0 && (cell.X - Math.Abs(cell.Y)) == 1;
            }
            return true;
        }
        public bool GetMapChange(TeleportCellEnum direction, Cell cell, bool ignore = false)
        {
            if (Account.Isbusy() || Perso.Inventory.Percent_Pods >= 100)
                return false;
            if (!CanChangeMap(direction, cell))
                return false;
            return MouvementForChangeMap(cell, ignore);
        }
        public bool GetMapchanges(TeleportCellEnum direction)
        {
            if (Account.Isbusy())
                return false;
            List<Cell> TeleportCells = Account.Game.Map.MapCells.Where(x => x.C_Types == Maps.Enums.CellTypes.TELEPORT_CELL).Select(x => x).ToList();
            while(TeleportCells.Count > 0)
            {
                Cell C = TeleportCells[Randomize.get_Random(0, TeleportCells.Count)];
                if (GetMapChange(direction, C))
                    return true;
                TeleportCells.Remove(C);
            }
            Account.Logger.LogDanger("MOUVEMENT", "Aucune cellule de destination.");
            return false;
        }
        private bool MouvementForChangeMap(Cell cell, bool ignore  = false)
        {
            var CellsForbiddens = map.CellsOccuped().Where(x => x.C_Types != Maps.Enums.CellTypes.TELEPORT_CELL).ToList();
            if(ignore)
                CellsForbiddens = new List<Cell>();
            var t = new Random().Next(650, 1500);
            Task.Delay(t);

            MoveResults result = GetCellsMove(cell, CellsForbiddens);
            switch (result)
            {
                case MoveResults.EXIT:
                    Account.Logger.LogInfo("MOUVEMENT", $"{map.GetCoordinates} changement de carte par le trigger de la cellule {cell.CellID}");
                    return true;
                default:
                    Account.Logger.LogError("MOUVEMENT", $"Le chemin vers la cellule {cell.CellID} est bloqué [{result}]");
                    Task.Delay(4600);
                    this.MouvementForChangeMap(cell, ignore);
                    return false;
            }

        }
        public async Task MoveInFight(KeyValuePair<short, FightMoveNode>? node)
        {
            if (!Account.IsFighting())
                return;
            if (node == null || node.Value.Value.Marche.CellsAccessibles.Count == 0)
                return;
           //if(node.Value.Key == Account.Game.)
        }
        public MoveResults GetCellsMove(Cell destination, List<Cell> ForbidenCells, bool D = false, byte distance = 0)
        {
            if (destination.CellID < 0 || destination.CellID > map.MapCells.Length)
                return MoveResults.CellRangeError;
            if (Account.Isbusy() || Perso.Inventory.Percent_Pods >= 100)
                return MoveResults.CharacterBusyOrFull;
            if (destination.CellID == Perso.Cell.CellID)
                return MoveResults.SAMECELL;
            if (destination.C_Types == Maps.Enums.CellTypes.NOT_WALKABLE && destination.Interactives == null)
                return MoveResults.CellNotWalkable;
            if (destination.C_Types == Maps.Enums.CellTypes.INTERACTIVE_OBJECT && destination.Interactives == null)
                return MoveResults.CellIsTypeOfInteractiveObject;
            if (ForbidenCells.Contains(destination))
                return MoveResults.MONSTER;

            List<Cell> TempPath = Pathfinder.GetPath(Perso.Cell, destination, ForbidenCells, D, distance, Account.Game.Map);

             if (TempPath == null || TempPath.Count == 0)
                return MoveResults.PathfindingErrorCount;
            if (!D && TempPath.Last().CellID != destination.CellID)
                return MoveResults.PathfindingError;
            if (D && TempPath.Count == 2 && TempPath[0].CellID == Perso.Cell.CellID && TempPath[1].CellID == destination.CellID)
                return MoveResults.SAMECELL;
            if (D && TempPath.Count == 1 && TempPath[0].CellID == Perso.Cell.CellID)
                return MoveResults.SAMECELL;


            ActualPath = TempPath;
            SendMoveMessage();
            return MoveResults.EXIT;

        }
        private async void SendMoveMessage()
        {
            
            if(Account.AccountStates == AccountStates.REGENERATION)
                await Account.Connexion.SendPacket("eU1", true);

            string path = PathfinderUtils.GetCleanRoad(ActualPath);
            await Account.Connexion.SendPacket($"GA001{path}", true);
            Perso.PathFindingMapPerso(ActualPath);
        }
        public async Task EventMoveFisnish(Cell destination, byte type, bool good)
        {
            Account.AccountStates = AccountStates.MOVING;
            if (good)
            {
                await Task.Delay(PathfinderUtils.GetTimeOnMap(Perso.Cell, ActualPath, Perso.UseMount));
                if (Account == null || Account.AccountStates == AccountStates.DISCONNECTED)
                    return;
                await Account.Connexion.SendPacket($"GKK{type}");
                Perso.Cell = destination;
            }
            ActualPath = null;
            Account.AccountStates = AccountStates.CONNECTED_INACTIVE;
            FinalizeMove?.Invoke(good);
        }
        private void ActualiseMap() => Pathfinder.SetMap(Account.Game.Map);
        public void AcutaliseMove(bool state) => FinalizeMove?.Invoke(state);

        ~Mouvement() => Dispose(true);
        public void Dispose() => Dispose(true);

        public void Clear()
        {
            ActualPath = null;
        }
        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    Pathfinder.Dispose();
                }
                ActualPath?.Clear();
                ActualPath = null;
                Pathfinder = null;
                Account = null;
                Perso = null;
                disposed = true;
            }
        }
    }
}
