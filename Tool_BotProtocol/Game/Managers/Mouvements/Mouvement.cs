using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Combats;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Maps.Mouvements;
using Tool_BotProtocol.Game.Perso;
using Tool_BotProtocol.Network;
using Tool_BotProtocol.Utils.Crypto;

namespace Tool_BotProtocol.Game.Managers.Mouvements
{
    /// <summary>Horloge des déplacements (temps écoulé, attentes) ; remplaçable pour rejouer un minutage sans attendre.</summary>
    public interface IMoveClock
    {
        long NowMilliseconds { get; }
        Task Delay(int milliseconds, CancellationToken token);
    }

    /// <summary>Horloge réelle : <see cref="Stopwatch"/> et <see cref="Task.Delay(int, CancellationToken)"/>.</summary>
    public sealed class SystemMoveClock : IMoveClock
    {
        private static readonly Stopwatch Watch = Stopwatch.StartNew();
        public static SystemMoveClock Instance { get; } = new SystemMoveClock();
        private SystemMoveClock() { }
        public long NowMilliseconds => Watch.ElapsedMilliseconds;
        public Task Delay(int milliseconds, CancellationToken token) => Task.Delay(Math.Max(0, milliseconds), token);
    }

    /// <summary>
    /// Déplacements hors combat du personnage, comme le client 1.34 (<c>GameActionsManager</c>, <c>InteractionsManager</c>) :
    /// <list type="bullet">
    /// <item><see cref="MoveToAsync"/> cherche le chemin hors du fil appelant (A* du client) et envoie <c>GA001&lt;chemin&gt;</c> ;</item>
    /// <item><see cref="OnServerMove"/> reçoit le <c>GA&lt;id&gt;;1</c> du serveur et programme un seul <c>GKK&lt;id&gt;</c> après la durée
    /// d'animation du client (un nouveau <c>GA</c> remplace l'acquittement en attente) ;</item>
    /// <item><see cref="Cancel"/> interrompt la marche par <c>GKE&lt;id&gt;|&lt;cellule&gt;</c>, la cellule estimée du sprite ;
    /// un nouveau <see cref="MoveToAsync"/> pendant la marche fait de même avant d'envoyer son <c>GA001</c> ;</item>
    /// <item><c>GA;0</c> (<see cref="AcutaliseMove"/>) et <c>GDM</c> (<see cref="CancelForMapChange"/>) effacent tout état en cours.</item>
    /// </list>
    /// Aucun refus lié aux pods (le serveur répond <c>GA;0</c> et <c>Im112</c>) ni aux groupes de monstres (le serveur lance le
    /// combat à l'arrivée). Les paquets de déplacement partent dans l'ordre des décisions ; aucun événement n'est levé sous verrou.
    /// </summary>
    public class Mouvement : IDisposable
    {
        /// <summary>Sans <c>GA;1</c> ni <c>GA;0</c> après ce délai, la demande est abandonnée localement (aucun paquet émis).</summary>
        public const int ServerResponseTimeout = 5000;

        private Accounts.Accounts Account;
        private CharacterClass Perso;
        private Map map;
        /// <summary>Chemin en cours, départ compris : celui demandé, puis celui du serveur ; null au repos.</summary>
        public List<Cell> ActualPath;

        /// <summary>Fin d'un déplacement : vrai à l'arrivée (après <c>GKK</c>), faux s'il est refusé, interrompu ou abandonné.</summary>
        public event Action<bool> FinalizeMove;
        private bool disposed;
        private int movementVersion;
        private readonly object movementSync = new object();
        private CancellationTokenSource movementCancellation = new CancellationTokenSource();
        private IMoveClock clock = SystemMoveClock.Instance;
        private ActiveMove current;
        private bool awaitingServer;
        private int awaitingToken;
        private Task sendChain = Task.CompletedTask;

        public Mouvement(Accounts.Accounts A, Map M, CharacterClass Character)
        {
            Account = A;
            map = M;
            Perso = Character;
        }

        /// <summary>Horloge des durées d'animation et du délai de réponse ; <see cref="SystemMoveClock"/> par défaut.</summary>
        public IMoveClock Clock
        {
            get { lock (movementSync) return clock; }
            set { lock (movementSync) clock = value ?? SystemMoveClock.Instance; }
        }

        /// <summary>Un <c>GA001</c> est parti et le serveur n'a encore répondu ni <c>GA;1</c> ni <c>GA;0</c>.</summary>
        public bool IsAwaitingServer { get { lock (movementSync) return awaitingServer; } }

        /// <summary>Identifiant du <c>GA</c> de déplacement en cours d'animation (avant son <c>GKK</c>), null sinon.</summary>
        public int? CurrentActionId { get { lock (movementSync) return current?.ActionId; } }

        /// <summary>
        /// Cellule du personnage telle que le client l'affiche : pendant la marche, la destination du pas en cours (le client
        /// met à jour la cellule du sprite au début de chaque pas) ; sinon la cellule connue du personnage.
        /// </summary>
        public Cell EstimatedCell { get { lock (movementSync) return StartCell_NoLock(); } }

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
            return map.TeleportCells != null && map.TeleportCells.TryGetValue(side, out List<short> ids) && ids.Contains(cell.CellID);
        }

        /// <summary>Marche vers la cellule de changement de carte <paramref name="cell"/> après une courte pause, sans bloquer de fil.</summary>
        public async Task<bool> GetMapChangeAsync(TeleportCellEnum direction, Cell cell, CancellationToken token = default(CancellationToken))
        {
            Accounts.Accounts account = Account;
            if (disposed || account == null || account.Isbusy() || !CanChangeMap(direction, cell)) return false;
            await Clock.Delay(Randomize.get_Random(650, 1500), token).ConfigureAwait(false);
            MoveResults result = await MoveToAsync(cell, token).ConfigureAwait(false);
            if (result == MoveResults.EXIT)
            {
                account.Logger?.LogInfo("MOUVEMENT", $"{map.GetCoordinates} changement de carte par le trigger de la cellule {cell.CellID}");
                return true;
            }
            account.Logger?.LogError("MOUVEMENT", $"Le chemin vers la cellule {cell.CellID} est bloqué [{result}]");
            return false;
        }

        /// <summary>Essaie, dans un ordre aléatoire, les cellules de changement de carte du côté <paramref name="direction"/>.</summary>
        public async Task<bool> GetMapChangesAsync(TeleportCellEnum direction, CancellationToken token = default(CancellationToken))
        {
            Accounts.Accounts account = Account;
            Cell[] cells = map?.MapCells;
            if (disposed || account == null || account.Isbusy() || cells == null)
                return false;

            List<Cell> teleportCells = cells.Where(x => x != null && x.C_Types == Maps.Enums.CellTypes.TELEPORT_CELL).ToList();
            while (teleportCells.Count > 0)
            {
                Cell candidate = teleportCells[Randomize.get_Random(0, teleportCells.Count)];
                if (await GetMapChangeAsync(direction, candidate, token).ConfigureAwait(false))
                    return true;
                teleportCells.Remove(candidate);
            }

            account.Logger?.LogDanger("MOUVEMENT", "Aucune cellule de destination.");
            return false;
        }

        /// <summary>Les déplacements en combat passent par <c>Fights</c> (<c>GA001</c> puis <c>GKK</c> sur <c>GAF</c>).</summary>
        public Task MoveInFight(KeyValuePair<short, FightMoveNode>? node) => Task.CompletedTask;

        /// <summary>
        /// Clic sur une cellule, comme <c>InteractionsManager.calculatePath</c> du client : recherche hors du fil appelant, un
        /// premier essai qui contourne les autres joueurs et les déclencheurs (refusé si la destination est occupée ou non
        /// atteinte), puis un second qui les ignore et accepte un chemin tronqué. Une destination occupée par un groupe de
        /// monstres est acceptée. Pendant une marche, envoie d'abord <c>GKE&lt;id&gt;|&lt;cellule estimée&gt;</c> puis repart de
        /// cette cellule. Renvoie <see cref="MoveResults.EXIT"/> une fois <c>GA001</c> envoyé. Toute la recherche et la décision
        /// s'exécutent sur le pool de fils : l'appelant (fil d'interface) ne fait que planifier.
        /// </summary>
        public Task<MoveResults> MoveToAsync(Cell destination, CancellationToken token = default(CancellationToken))
        {
            return Task.Run(() => MoveToCoreAsync(destination, token), token);
        }

        private async Task<MoveResults> MoveToCoreAsync(Cell destination, CancellationToken token)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                token.ThrowIfCancellationRequested();
                MoveResults refusal = TakeSnapshot(destination, SnapshotMode.Click, out MoveSnapshot snapshot);
                if (refusal != MoveResults.EXIT) return refusal;
                if (!destination.IsWalkable()) return MoveResults.CellNotWalkable;
                List<Cell> path = SearchClientPath(snapshot);
                if (path == null) return MoveResults.PathfindingError;
                Task sent = CommitMove(snapshot, path);
                // La marche a changé de pas ou l'état a changé pendant la recherche : nouvelle recherche.
                if (sent == null) continue;
                await sent.ConfigureAwait(false);
                return MoveResults.EXIT;
            }
            return MoveResults.CharacterBusyOrFull;
        }

        /// <summary>
        /// Chemin que suivrait un clic sur <paramref name="destination"/> (départ compris), sans rien envoyer : recherche
        /// synchrone bornée par le budget de nœuds de <see cref="PathRequest"/> ; null si aucun.
        /// </summary>
        public List<Cell> PreviewPath(Cell destination)
        {
            if (TakeSnapshot(destination, SnapshotMode.Preview, out MoveSnapshot snapshot) != MoveResults.EXIT) return null;
            if (!destination.IsWalkable()) return null;
            return SearchClientPath(snapshot);
        }

        /// <summary>
        /// Déplacement historique des gestionnaires (récolte, zaap…), synchrone : <paramref name="forbiddenCells"/> ne sont jamais
        /// traversées (la destination reste permise), <paramref name="D"/> force les huit directions, <paramref name="distance"/>
        /// s'arrête à cette distance. La recherche s'exécute sur le fil appelant : depuis l'interface, utiliser
        /// <see cref="MoveToAsync"/>.
        /// </summary>
        public MoveResults GetCellsMove(Cell destination, List<Cell> forbiddenCells, bool D = false, byte distance = 0)
        {
            MoveResults refusal = TakeSnapshot(destination, SnapshotMode.Legacy, out MoveSnapshot snapshot);
            if (refusal != MoveResults.EXIT) return refusal;

            if (!destination.IsWalkable() && distance == 0)
                return MoveResults.CellNotWalkable;

            if (destination.C_Types == Maps.Enums.CellTypes.INTERACTIVE_OBJECT && destination.Interactives == null)
                return MoveResults.CellIsTypeOfInteractiveObject;

            var request = new PathRequest
            {
                AllDirections = D || snapshot.AllDirections,
                CrossUnwalkable = false,
                StopDistance = distance,
                BlockedCells = forbiddenCells == null ? null : new HashSet<int>(forbiddenCells
                    .Where(cell => cell != null && cell.CellID != destination.CellID).Select(cell => (int)cell.CellID))
            };
            List<Cell> tempPath = Pathfinder.FindPath(snapshot.Cells, snapshot.Width, snapshot.Start, destination, request);

            if (tempPath == null || tempPath.Count == 0)
                return MoveResults.PathfindingErrorCount;

            if (!D && tempPath.Last().CellID != destination.CellID)
                return MoveResults.PathfindingError;

            if (tempPath.Count <= 1)
                return MoveResults.SAMECELL;

            return CommitMove(snapshot, tempPath) == null ? MoveResults.CharacterBusyOrFull : MoveResults.EXIT;
        }

        /// <summary>
        /// <c>GA&lt;id&gt;;1</c> du personnage (chemin décodé, départ compris) : la marche commence, et un seul <c>GKK&lt;id&gt;</c>
        /// part après la durée d'animation du client (<see cref="AnimDuration"/>). Un nouveau <c>GA</c> — même identique —
        /// remplace la marche en attente, comme le client qui vide alors la séquence du sprite : l'acquittement précédent
        /// n'est jamais envoyé.
        /// </summary>
        public void OnServerMove(int actionId, List<Cell> path)
        {
            if (path == null || path.Count == 0 || path.Any(cell => cell == null)) return;
            ActiveMove move;
            IMoveClock moveClock;
            CancellationToken token;
            Accounts.Accounts account;
            lock (movementSync)
            {
                account = Account;
                Cell[] cells = map?.MapCells;
                if (disposed || account == null || cells == null || !path.All(cell => BelongsTo(cells, cell))) return;
                moveClock = clock;
                token = movementCancellation.Token;
                List<Cell> copy = path.ToList();
                move = new ActiveMove(actionId, copy, AnimDuration.Compute(copy, OwnProfile_NoLock()), moveClock.NowMilliseconds, account.Connexion);
                current = move;
                awaitingServer = false;
                awaitingToken++;
                ActualPath = copy;
            }
            if (!account.Isbusy()) account.AccountStates = AccountStates.MOVING;
            _ = AcknowledgeAfterAsync(move, moveClock, token);
        }

        /// <summary>
        /// Compatibilité : <c>GA;1</c> du personnage vers <paramref name="destination"/> avec le chemin <see cref="ActualPath"/>
        /// (s'il y mène). Ne bloque pas : l'acquittement est programmé par <see cref="OnServerMove"/>.
        /// </summary>
        public Task EventMoveFisnish(Cell destination, int type, bool good)
        {
            if (!good)
            {
                AcutaliseMove(false);
                return Task.CompletedTask;
            }
            if (destination == null) return Task.CompletedTask;
            List<Cell> path = ActualPath;
            if (path == null || path.Count == 0 || !ReferenceEquals(path[path.Count - 1], destination)) path = new List<Cell> { destination };
            OnServerMove(type, path);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Interrompt la marche en cours : <c>GKE&lt;id&gt;|&lt;cellule&gt;</c> avec la cellule estimée (<see cref="EstimatedCell"/>),
        /// qui devient celle du personnage. Faux sans marche annulable (aucune, ou réponse du serveur encore attendue).
        /// </summary>
        public bool Cancel()
        {
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Accounts.Accounts account;
            Cell cell;
            int direction;
            // La barrière est levée quoi qu'il arrive : sinon tous les paquets de déplacement suivants resteraient en attente.
            try
            {
                lock (movementSync)
                {
                    ActiveMove move = current;
                    account = Account;
                    if (disposed || move == null || account == null) return false;
                    StopCurrent_NoLock(move, out cell, out direction);
                    if (move.Connection != null && ReferenceEquals(move.Connection, account.Connexion))
                        EnqueueSend_NoLock(move.Connection, CancelPacket(move.ActionId, cell), gate.Task);
                }
                if (account.IsMoving()) account.AccountStates = AccountStates.CONNECTED_INACTIVE;
            }
            finally { gate.TrySetResult(true); }
            UpdateSelfActor(cell, direction);
            FinalizeMove?.Invoke(false);
            return true;
        }

        /// <summary><c>GA;0</c> : refus du serveur, tout état de déplacement est effacé.</summary>
        public void AcutaliseMove(bool state)
        {
            Clear();
            if (Account != null && Account.IsMoving()) Account.AccountStates = AccountStates.CONNECTED_INACTIVE;
            FinalizeMove?.Invoke(state);
        }

        public void Clear()
        {
            CancellationTokenSource previous;
            lock (movementSync)
            {
                movementVersion++;
                current = null;
                awaitingServer = false;
                awaitingToken++;
                ActualPath = null;
                previous = movementCancellation;
                movementCancellation = disposed ? null : new CancellationTokenSource();
            }
            if (previous != null) { previous.Cancel(); previous.Dispose(); }
        }

        public void CancelForMapChange()
        {
            Clear();
            if (Account != null && Account.IsMoving()) Account.AccountStates = AccountStates.CONNECTED_INACTIVE;
        }

        private enum SnapshotMode { Click, Preview, Legacy }

        private sealed class ActiveMove
        {
            public ActiveMove(int actionId, List<Cell> path, AnimDuration timing, long start, TcpClient connection)
            {
                ActionId = actionId;
                Path = path;
                Timing = timing;
                Start = start;
                Connection = connection;
            }

            public readonly int ActionId;
            public readonly List<Cell> Path;
            public readonly AnimDuration Timing;
            public readonly long Start;
            public readonly TcpClient Connection;
        }

        private sealed class MoveSnapshot
        {
            public int Version;
            public ActiveMove Current;
            public Cell[] Cells;
            public int Width;
            public Cell Start;
            public Cell Destination;
            public TcpClient Connection;
            public bool AllDirections;
            public HashSet<int> PlayerCells;
            public bool TargetOccupied;
        }

        private MoveResults TakeSnapshot(Cell destination, SnapshotMode mode, out MoveSnapshot snapshot)
        {
            snapshot = null;
            Accounts.Accounts account;
            lock (movementSync)
            {
                account = Account;
                Cell[] cells = map?.MapCells;
                if (disposed || account == null || destination == null || cells == null || map.MapWidth < 2 || !BelongsTo(cells, destination))
                    return MoveResults.CellRangeError;
                ActiveMove moving = current;
                if (mode != SnapshotMode.Preview)
                {
                    if (account.IsFighting() || awaitingServer || account.Connexion == null) return MoveResults.CharacterBusyOrFull;
                    bool redirect = mode == SnapshotMode.Click && moving != null && account.IsMoving();
                    if ((moving != null && !redirect) || (account.Isbusy() && !redirect)) return MoveResults.CharacterBusyOrFull;
                }
                Cell start = StartCell_NoLock();
                if (start == null || !BelongsTo(cells, start)) return MoveResults.CellRangeError;
                if (start.CellID == destination.CellID) return MoveResults.SAMECELL;
                snapshot = new MoveSnapshot
                {
                    Version = movementVersion,
                    Current = moving,
                    Cells = cells,
                    Width = map.MapWidth,
                    Start = start,
                    Destination = destination,
                    Connection = account.Connexion
                };
            }
            snapshot.AllDirections = CanMoveInAllDirections(account);
            if (mode != SnapshotMode.Legacy)
            {
                // Hors combat, le client contourne les autres joueurs (sprites Character) et refuse au premier essai une
                // destination occupée par un sprite ; les autres sprites (PNJ, monstres…) ne bloquent pas le chemin.
                MapActor[] actors = map.AllActors.ToArray();
                snapshot.PlayerCells = new HashSet<int>(actors.OfType<PlayerActor>().Where(actor => !actor.IsSelf && actor.CellId >= 0).Select(actor => actor.CellId));
                snapshot.TargetOccupied = actors.Any(actor => !actor.IsSelf && actor.CellId == destination.CellID);
            }
            return MoveResults.EXIT;
        }

        private static List<Cell> SearchClientPath(MoveSnapshot snapshot)
        {
            if (!snapshot.TargetOccupied)
            {
                List<Cell> first = Pathfinder.FindPath(snapshot.Cells, snapshot.Width, snapshot.Start, snapshot.Destination, new PathRequest
                {
                    AllDirections = snapshot.AllDirections,
                    SpriteCells = snapshot.PlayerCells
                });
                if (first != null && first.Count >= 2 && ReferenceEquals(first[first.Count - 1], snapshot.Destination)) return first;
            }
            List<Cell> second = Pathfinder.FindPath(snapshot.Cells, snapshot.Width, snapshot.Start, snapshot.Destination, new PathRequest
            {
                AllDirections = snapshot.AllDirections,
                IgnoreSprites = true
            });
            return second != null && second.Count >= 2 ? second : null;
        }

        // Envoie GKE (marche interrompue) puis GA001 ; null si l'état a changé depuis l'instantané.
        private Task CommitMove(MoveSnapshot snapshot, List<Cell> path)
        {
            string encoded = PathfinderUtils.CompressPath(path);
            if (encoded.Length == 0) return null;
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Accounts.Accounts account;
            Task sent;
            ActiveMove replaced;
            Cell redirectCell = null;
            int redirectDirection = -1;
            int awaitingId;
            IMoveClock moveClock;
            CancellationToken token;
            try
            {
                lock (movementSync)
                {
                    account = Account;
                    if (disposed || account == null || snapshot.Version != movementVersion || !ReferenceEquals(current, snapshot.Current)
                        || awaitingServer || !ReferenceEquals(map?.MapCells, snapshot.Cells) || !ReferenceEquals(account.Connexion, snapshot.Connection)
                        || !ReferenceEquals(StartCell_NoLock(), snapshot.Start) || account.IsFighting()
                        || (account.Isbusy() && !(current != null && account.IsMoving())))
                        return null;
                    replaced = current;
                    if (replaced != null)
                    {
                        StopCurrent_NoLock(replaced, out redirectCell, out redirectDirection);
                        if (replaced.Connection != null && ReferenceEquals(replaced.Connection, snapshot.Connection))
                            EnqueueSend_NoLock(snapshot.Connection, CancelPacket(replaced.ActionId, redirectCell), gate.Task);
                    }
                    awaitingServer = true;
                    awaitingId = ++awaitingToken;
                    moveClock = clock;
                    token = movementCancellation.Token;
                    ActualPath = path;
                    sent = EnqueueSend_NoLock(snapshot.Connection, "GA001" + encoded, gate.Task);
                }
                // L'état est réservé avant que GA001 ne parte : la réponse du serveur le trouve déjà en déplacement.
                if (!account.Isbusy()) account.AccountStates = AccountStates.MOVING;
            }
            finally { gate.TrySetResult(true); }
            if (replaced != null)
            {
                UpdateSelfActor(redirectCell, redirectDirection);
                FinalizeMove?.Invoke(false);
            }
            Perso?.PathFindingMapPerso(path);
            _ = WatchServerResponseAsync(awaitingId, moveClock, token);
            return sent;
        }

        private async Task AcknowledgeAfterAsync(ActiveMove move, IMoveClock moveClock, CancellationToken token)
        {
            try
            {
                try { await moveClock.Delay(move.Timing.Total, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Task ack = null;
                try
                {
                    Accounts.Accounts account;
                    lock (movementSync)
                    {
                        account = Account;
                        if (disposed || account == null || !ReferenceEquals(current, move)) return;
                        current = null;
                        if (ReferenceEquals(ActualPath, move.Path)) ActualPath = null;
                        Perso.Cell = move.Path[move.Path.Count - 1];
                        if (move.Timing.FinalDirection >= 0) Perso.Orientation = move.Timing.FinalDirection;
                        if (move.Connection != null && ReferenceEquals(move.Connection, account.Connexion))
                            ack = EnqueueSend_NoLock(move.Connection, "GKK" + move.ActionId.ToString(CultureInfo.InvariantCulture), gate.Task);
                    }
                    // L'état est rendu avant que GKK ne parte : quiconque lit l'acquittement voit le personnage arrivé.
                    if (account.IsMoving()) account.AccountStates = AccountStates.CONNECTED_INACTIVE;
                }
                finally { gate.TrySetResult(true); }
                if (ack != null) await ack.ConfigureAwait(false);
                if (!disposed) FinalizeMove?.Invoke(true);
            }
            catch (Exception error)
            {
                Account?.Logger?.LogException("MOUVEMENT", error);
            }
        }

        private async Task WatchServerResponseAsync(int awaitingId, IMoveClock moveClock, CancellationToken token)
        {
            try
            {
                try { await moveClock.Delay(ServerResponseTimeout, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                Accounts.Accounts account;
                lock (movementSync)
                {
                    account = Account;
                    if (disposed || account == null || !awaitingServer || awaitingId != awaitingToken) return;
                    awaitingServer = false;
                    ActualPath = null;
                }
                if (account.IsMoving()) account.AccountStates = AccountStates.CONNECTED_INACTIVE;
                account.Logger?.LogDanger("MOUVEMENT", "Le serveur n'a pas répondu au déplacement demandé : demande abandonnée.");
                FinalizeMove?.Invoke(false);
            }
            catch (Exception error)
            {
                Account?.Logger?.LogException("MOUVEMENT", error);
            }
        }

        // Arrête la marche courante à la cellule estimée, qui devient celle du personnage.
        private void StopCurrent_NoLock(ActiveMove move, out Cell cell, out int direction)
        {
            double elapsed = clock.NowMilliseconds - move.Start;
            cell = move.Path[move.Timing.CellIndexAt(elapsed)];
            direction = move.Timing.DirectionAt(elapsed);
            current = null;
            if (ReferenceEquals(ActualPath, move.Path)) ActualPath = null;
            Perso.Cell = cell;
            if (direction >= 0) Perso.Orientation = direction;
        }

        private Cell StartCell_NoLock()
        {
            ActiveMove move = current;
            if (move == null) return Perso?.Cell;
            return move.Path[move.Timing.CellIndexAt(clock.NowMilliseconds - move.Start)];
        }

        private MoveProfile OwnProfile_NoLock()
        {
            PlayerActor self = map?.Self as PlayerActor;
            bool mounted = (Perso != null && Perso.UseMount) || (self != null && self.HasMount);
            return MoveProfile.Player(self?.RestrictionsRaw, mounted, false);
        }

        // Restriction 8192 (canMoveInAllDirections) reçue dans AR ; inconnue, le client reste sur quatre directions.
        private static bool CanMoveInAllDirections(Accounts.Accounts account)
        {
            int raw = account?.Game?.Session?.RawRestrictions ?? -1;
            return raw >= 0 && (raw & 8192) == 8192;
        }

        private void UpdateSelfActor(Cell cell, int direction)
        {
            MapActor self = map?.Self;
            if (self == null || cell == null) return;
            self.Cell = cell;
            if (direction >= 0) self.Orientation = direction;
            map.NotifyActorUpdated(self);
        }

        private static string CancelPacket(int actionId, Cell cell) =>
            "GKE" + actionId.ToString(CultureInfo.InvariantCulture) + "|" + cell.CellID.ToString(CultureInfo.InvariantCulture);

        private static bool BelongsTo(Cell[] cells, Cell cell) =>
            cell != null && cell.CellID >= 0 && cell.CellID < cells.Length && ReferenceEquals(cells[cell.CellID], cell);

        // Sous movementSync : chaque paquet attend le précédent et sa barrière, puis part sur le pool de fils (jamais sous verrou).
        private Task EnqueueSend_NoLock(TcpClient connection, string packet, Task gate)
        {
            Task previous = sendChain;
            Accounts.Accounts account = Account;
            Task sent = Task.WhenAll(previous, gate).ContinueWith(_ => SendSafelyAsync(connection, packet, account),
                CancellationToken.None, TaskContinuationOptions.DenyChildAttach, TaskScheduler.Default).Unwrap();
            sendChain = sent;
            return sent;
        }

        private static async Task SendSafelyAsync(TcpClient connection, string packet, Accounts.Accounts account)
        {
            try { await connection.SendPacket(packet).ConfigureAwait(false); }
            catch (Exception error) { account?.Logger?.LogException("MOUVEMENT", error); }
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
                ActualPath = null;
                Account = null;
                Perso = null;
                disposed = true;
                FinalizeMove = null;
            }
        }
    }
}
