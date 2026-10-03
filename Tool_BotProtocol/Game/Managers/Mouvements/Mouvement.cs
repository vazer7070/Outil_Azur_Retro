using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
        private Pathfinder Pathfinder;
        public List<Cell> ActualPath;

        public event Action<bool> FinalizeMove;
        private bool disposed;
        private int movementVersion;
        private readonly object movementSync = new object();
        private System.Threading.CancellationTokenSource movementCancellation = new System.Threading.CancellationTokenSource();

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
            if (cell == null) return false;
            Maps.Enums.TeleportCellsEnum side;
            switch (Direction)
            {
                case TeleportCellEnum.LEFT:
                    side = Maps.Enums.TeleportCellsEnum.LEFT; break;
                case TeleportCellEnum.RIGHT:
                    side = Maps.Enums.TeleportCellsEnum.RIGHT; break;
                case TeleportCellEnum.BOTTOM:
                    side = Maps.Enums.TeleportCellsEnum.BOTTOM; break;
                case TeleportCellEnum.TOP:
                    side = Maps.Enums.TeleportCellsEnum.TOP; break;
                default:
                    return true;
            }
            return map.TeleportCells.TryGetValue(side, out List<short> ids) && ids.Contains(cell.CellID);
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
            if (Account.Isbusy() || map.MapCells == null)
                return false;

            List<Cell> teleportCells = Account.Game.Map.MapCells
                .Where(x => x.C_Types == Maps.Enums.CellTypes.TELEPORT_CELL)
                .ToList();

            while (teleportCells.Count > 0)
            {
                Cell C = teleportCells[Randomize.get_Random(0, teleportCells.Count)];

                if (GetMapChange(direction, C))
                    return true;

                teleportCells.Remove(C);
            }

            Account.Logger.LogDanger("MOUVEMENT", "Aucune cellule de destination.");
            return false;
        }

        private bool MouvementForChangeMap(Cell cell, bool ignore = false)
        {
            var cellsForbiddens = map.CellsOccuped()
                .Where(x => x.C_Types != Maps.Enums.CellTypes.TELEPORT_CELL)
                .ToList();

            if (ignore)
                cellsForbiddens.Clear();

            int t = new Random().Next(650, 1500);
            Task.Delay(t).Wait();

            MoveResults result = GetCellsMove(cell, cellsForbiddens);

            switch (result)
            {
                case MoveResults.EXIT:
                    Account.Logger.LogInfo("MOUVEMENT", $"{map.GetCoordinates} changement de carte par le trigger de la cellule {cell.CellID}");
                    return true;
                default:
                    Account.Logger.LogError("MOUVEMENT", $"Le chemin vers la cellule {cell.CellID} est bloqué [{result}]");
                    return false;
            }
        }

        public async Task MoveInFight(KeyValuePair<short, FightMoveNode>? node)
        {
            if (!Account.IsFighting() || node == null || node.Value.Value.Marche.CellsAccessibles.Count == 0)
                return;

            // Code pour le déplacement pendant un combat
        }

        public MoveResults GetCellsMove(Cell destination, List<Cell> forbiddenCells, bool D = false, byte distance = 0)
        {
            if (disposed || destination == null || map.MapCells == null || Perso.Cell == null
                || destination.CellID < 0 || destination.CellID >= map.MapCells.Length)
                return MoveResults.CellRangeError;

            if (Account.Isbusy() || Perso.Inventory.Percent_Pods >= 100)
                return MoveResults.CharacterBusyOrFull;

            if (destination.CellID == Perso.Cell.CellID)
                return MoveResults.SAMECELL;

            if (!destination.IsWalkable() && distance == 0)
                return MoveResults.CellNotWalkable;

            if (destination.C_Types == Maps.Enums.CellTypes.INTERACTIVE_OBJECT && destination.Interactives == null)
                return MoveResults.CellIsTypeOfInteractiveObject;

            if (forbiddenCells != null && forbiddenCells.Contains(destination) && distance == 0)
                return MoveResults.MONSTER;

            List<Cell> tempPath = Pathfinder.GetPath(Perso.Cell, destination, forbiddenCells, D, distance, Account.Game.Map);

            if (tempPath == null || tempPath.Count == 0)
                return MoveResults.PathfindingErrorCount;

            if (!D && tempPath.Last().CellID != destination.CellID)
                return MoveResults.PathfindingError;

            if (D && tempPath.Count <= 1 && tempPath[0].CellID == Perso.Cell.CellID)
                return MoveResults.SAMECELL;

            ActualPath = tempPath;
            System.Threading.Interlocked.Increment(ref movementVersion);
            bool leaveRegeneration = Account.AccountStates == AccountStates.REGENERATION;
            Account.AccountStates = AccountStates.MOVING;
            SendMoveMessage(leaveRegeneration);
            return MoveResults.EXIT;
        }

        private async void SendMoveMessage(bool leaveRegeneration)
        {
            Accounts.Accounts account = Account;
            var connection = account?.Connexion;
            List<Cell> pathCells = ActualPath;
            int version = movementVersion;
            try
            {
                if (connection == null || pathCells == null) return;
                string path = PathfinderUtils.GetCleanRoad(pathCells);
                if (path.Length == 0) return;
                if (leaveRegeneration) await connection.SendPacket("eU1", true);
                if (disposed || version != movementVersion || !ReferenceEquals(pathCells, ActualPath)) return;
                await connection.SendPacket($"GA001{path}", true);
                if (!disposed && version == movementVersion && ReferenceEquals(pathCells, ActualPath)
                    && ReferenceEquals(connection, account.Connexion)) Perso.PathFindingMapPerso(pathCells);
            }
            catch (Exception error)
            {
                account?.Logger?.LogException("MOUVEMENT", error);
                if (!disposed && version == movementVersion) AcutaliseMove(false);
            }
        }

        public async Task EventMoveFisnish(Cell destination, int type, bool good)
        {
            Accounts.Accounts account = Account;
            var connection = account?.Connexion;
            int version;
            System.Threading.CancellationToken cancellation;
            lock (movementSync)
            {
                if (disposed || movementCancellation == null) return;
                version = movementVersion;
                cancellation = movementCancellation.Token;
            }
            if (disposed || connection == null || destination == null
                || map.GetCellFromId(destination.CellID) != destination) return;
            account.AccountStates = AccountStates.MOVING;
            if (good)
            {
                try
                {
                    await Task.Delay(PathfinderUtils.GetTimeOnMap(Perso.Cell, ActualPath, Perso.UseMount), cancellation);
                }
                catch (OperationCanceledException) { return; }
                if (disposed || version != movementVersion || account.AccountStates == AccountStates.DISCONNECTED
                    || !ReferenceEquals(connection, account.Connexion) || map.GetCellFromId(destination.CellID) != destination)
                    return;
                await connection.SendPacket($"GKK{type}");
                if (disposed || version != movementVersion || account.AccountStates == AccountStates.DISCONNECTED) return;
                Perso.Cell = destination;
            }

            ActualPath = null;
            account.AccountStates = AccountStates.CONNECTED_INACTIVE;
            FinalizeMove?.Invoke(good);
        }

        private void ActualiseMap()
        {
            Pathfinder.SetMap(Account.Game.Map);
        }

        public void AcutaliseMove(bool state)
        {
            Clear();
            if (Account != null && Account.IsMoving()) Account.AccountStates = AccountStates.CONNECTED_INACTIVE;
            FinalizeMove?.Invoke(state);
        }

        public void Clear()
        {
            System.Threading.CancellationTokenSource previous;
            lock (movementSync)
            {
                System.Threading.Interlocked.Increment(ref movementVersion);
                ActualPath = null;
                previous = movementCancellation;
                movementCancellation = disposed ? null : new System.Threading.CancellationTokenSource();
            }
            if (previous != null) { previous.Cancel(); previous.Dispose(); }
        }

        public void CancelForMapChange()
        {
            Clear();
            if (Account != null && Account.IsMoving()) Account.AccountStates = AccountStates.CONNECTED_INACTIVE;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                lock (movementSync) disposed = true;
                Clear();
                if (disposing)
                {
                    map.RefreshMap -= ActualiseMap;
                    Pathfinder.Dispose();
                }

                ActualPath?.Clear();
                ActualPath = null;
                Pathfinder = null;
                Account = null;
                Perso = null;
                disposed = true;
                FinalizeMove = null;
            }
        }
    }
}
