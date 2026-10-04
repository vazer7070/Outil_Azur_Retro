using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Managers.Mouvements;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Maps.Mouvements;

// Déplacements hors combat comme le client 1.34 : codage des chemins (compressPath / extractFullPath), allure et durées
// (WALK/RUN/MOUNT_SPEEDS), A* du client hors du fil appelant, GA001 → GA<id>;1 → un seul GKK<id> après la durée,
// GKE<id>|<cellule> à l'interruption, GA;0, destination sur un groupe de monstres et pods pleins acceptés.
// Serveur fictif local, cartes synthétiques en mémoire, horloge manuelle, aucune interface et aucune donnée réelle.
internal static class BotMovementSmoke
{
    private const int CorridorMapId = 991301;
    private const int OpenMapId = 991302;
    private const int Width = 15;
    private const int CellCount = 479;
    private const string Walkable = "HhGaeaaaaa";
    private const string Unwalkable = "Hhaaeaaaaa";
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_";
    // Chemin de 9 cellules en quatre directions (1, 1, 1, 3, 3, 1, 1, 3) : seul couloir praticable de la carte 991301.
    private static readonly int[] Corridor = { 90, 105, 120, 135, 149, 163, 178, 193, 207 };
    // Même départ en huit directions, avec deux pas verticaux (direction 2).
    private static readonly int[] EightDirections = { 90, 105, 120, 135, 149, 163, 178, 207, 236 };
    private const string SelfEntry = ";1;0;42;Moi;1;10^100;0;0,0,0,42;-1;-1;-1;;0;;;;;0;;0";
    private const string OtherPlayer = "+92;2;0;44;Bruno;2;20^100;1;0,0,0,0;-1;-1;-1;;0;;;;;0;;0";
    private const string Group = "+96;1;45;-2;101,102,103;-3;1001^100,1002^90,1003^110;5,6,7;-1,-1,-1;0,0,0,0;-1,-1,-1;0,0,0,0;-1,-1,-1;0,0,0,0";

    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            string path = Path.Combine(TestPaths.ApplicationBin, new AssemblyName(args.Name).Name + ".dll");
            if (!File.Exists(path)) path = Path.ChangeExtension(path, "exe");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        try
        {
            Encoding();
            Speeds();
            Search();
            Run().GetAwaiter().GetResult();
            Console.WriteLine("OK: compressPath/extractFullPath cross-check, walk/run/mount speeds, client A* under 50 ms off the caller thread, "
                + "GA001 of a 9-cell path, single GKK after the client duration, GKE on cancel and redirect, GA;0, monster group target, full pods");
        }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.Exit(1); }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    private static string MapData(Func<int, string> cell) => string.Concat(Enumerable.Range(0, CellCount).Select(cell));

    private static Map Build(Func<int, string> cell)
    {
        var map = new Map { MapWidth = Width, MapHeight = 17 };
        map.DecompressMap(MapData(cell));
        return map;
    }

    private static List<Cell> Cells(Map map, IEnumerable<int> ids) => ids.Select(id => map.MapCells[id]).ToList();

    // Ligne et colonne d'affichage d'une cellule (lignes paires de 15 cellules, impaires de 14).
    private static int Row(int id) => 2 * (id / (2 * Width - 1)) + (id % (2 * Width - 1) >= Width ? 1 : 0);
    private static int Column(int id) => id % (2 * Width - 1) - (id % (2 * Width - 1) >= Width ? Width : 0);

    // compressPath du client, écrit indépendamment du bot : direction d'après l'écart de numéros de cellule
    // (1, w, 2w−1, w−1, −1, −w, −2w+1, −w+1), puis makeLightPath sans la cellule de départ.
    private static string ClientCompress(IList<Cell> path)
    {
        int[] offsets = { 1, Width, Width * 2 - 1, Width - 1, -1, -Width, -Width * 2 + 1, -Width + 1 };
        var points = new List<int[]>();
        for (int i = 1; i < path.Count; i++)
        {
            int direction = Array.IndexOf(offsets, path[i].CellID - path[i - 1].CellID);
            Check(direction >= 0, "Synthetic path is not contiguous");
            if (points.Count > 0 && points[points.Count - 1][0] == direction) points[points.Count - 1][1] = path[i].CellID;
            else points.Add(new[] { direction, (int)path[i].CellID });
        }
        var text = new StringBuilder();
        foreach (int[] point in points)
            text.Append(Alphabet[point[0] & 7]).Append(Alphabet[(point[1] & 4032) >> 6]).Append(Alphabet[point[1] & 63]);
        return text.ToString();
    }

    private static string Start(int cellId) => "a" + Alphabet[(cellId & 4032) >> 6] + Alphabet[cellId & 63];

    private static void Encoding()
    {
        Map map = Build(id => Walkable);
        List<Cell> eight = Cells(map, EightDirections);
        List<Cell> corridor = Cells(map, Corridor);
        Check(PathfinderUtils.CompressPath(eight) == "bchdcJbcYcdS" && ClientCompress(eight) == "bchdcJbcYcdS",
            "Eight-direction path differs from compressPath");
        Check(PathfinderUtils.GetCleanRoad(corridor) == "bchdcJbdbddp" && ClientCompress(corridor) == "bchdcJbdbddp",
            "Four-direction path differs from compressPath");
        Check(PathfinderUtils.EncodePoint(0, 90) == "abA" && Start(90) == "abA", "Start cell code differs");
        Check(PathfinderUtils.DecodeServerPath(map, "abA" + "bchdcJbcYcdS").SequenceEqual(eight)
            && PathfinderUtils.DecodeServerPath(map, "abA" + "bchdcJbdbddp").SequenceEqual(corridor),
            "Server path a<start><path> was not expanded cell by cell");

        // Aller-retour sur des chemins aléatoires en huit directions : codage du bot = codage indépendant, décodage = chemin.
        var random = new Random(1234);
        int[] dx = { 1, 1, 1, 0, -1, -1, -1, 0 }, dy = { -1, 0, 1, 1, 1, 0, -1, -1 };
        for (int sample = 0; sample < 300; sample++)
        {
            var walk = new List<Cell> { map.MapCells[random.Next(CellCount)] };
            int length = random.Next(1, 30);
            while (walk.Count <= length)
            {
                int direction = random.Next(8);
                Cell next = PathfinderUtils.NeighbourOf(map.MapCells, Width, walk[walk.Count - 1], dx[direction], dy[direction]);
                if (next != null) walk.Add(next);
            }
            string compressed = PathfinderUtils.CompressPath(walk);
            Check(compressed == ClientCompress(walk), "Random path encoding differs from compressPath: " + compressed);
            List<Cell> decoded = PathfinderUtils.DecodeServerPath(map, Start(walk[0].CellID) + compressed);
            Check(decoded != null && decoded.SequenceEqual(walk), "Random path did not survive encode/decode: " + compressed);
        }

        // Chemin refusé par StarLoco : <orientation><cellule actuelle>, aucun pas.
        List<Cell> refused = PathfinderUtils.DecodeServerPath(map, "abA" + "cbA");
        Check(refused != null && refused.Count == 1 && refused[0] == map.MapCells[90], "Zero-step server path was not read as staying put");
        foreach (string malformed in new[] { null, "", "ab", "abAb", "a!A", "abAibA", "abAb__", "abAbbB", "abAbA" })
            Check(PathfinderUtils.DecodeServerPath(map, malformed) == null, "Malformed path was accepted: " + malformed);
        Check(PathfinderUtils.DecodeServerPath(null, "abA") == null, "Path decoded without a map");
        bool rejected = false;
        try { PathfinderUtils.CompressPath(new List<Cell> { map.MapCells[90], map.MapCells[92] }); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "Non-adjacent cells were encoded as one step");
        Check(PathfinderUtils.CompressPath(new List<Cell> { map.MapCells[90] }) == string.Empty, "Single-cell path produced a step");
    }

    private static void Speeds()
    {
        Check(MoveSpeeds.ChooseMode(3, MoveProfile.Player()) == MoveMode.Walk && MoveSpeeds.ChooseMode(4, MoveProfile.Player()) == MoveMode.Run,
            "Player does not start running beyond 3 cells");
        Check(MoveSpeeds.ChooseMode(4, MoveProfile.Player(true)) == MoveMode.Mount && MoveSpeeds.ChooseMode(3, MoveProfile.Player(true)) == MoveMode.Walk,
            "Mounted run or walk used the wrong speeds");
        Check(MoveSpeeds.ChooseMode(6, MoveProfile.Creature()) == MoveMode.Walk && MoveSpeeds.ChooseMode(7, MoveProfile.Creature()) == MoveMode.Run
            && MoveSpeeds.ChooseMode(4, MoveProfile.Creature(true)) == MoveMode.Walk && MoveSpeeds.ChooseMode(5, MoveProfile.Creature(true)) == MoveMode.Run,
            "Creature run limit differs from 6 (4 in fight)");
        Check(MoveSpeeds.ChooseMode(30, MoveProfile.MonsterGroup()) == MoveMode.Walk, "Monster group ran");
        Check(MoveSpeeds.ChooseMode(9, MoveProfile.Player("g", false, false)) == MoveMode.Walk, "Restriction 16 did not force walking");
        Check(MoveProfile.Player("w", false, false).EffectiveModerator == 0.5 && MoveProfile.Player("74", false, false).EffectiveModerator == 5
            && MoveProfile.Player("zzzzzzzz", false, false).EffectiveModerator == 1, "Slow/sonic restrictions or unreadable value misapplied");

        GmParseResult parsed = GmParser.Parse("GM|" + Group, false, 42, 0);
        MapActor group = parsed.Entries.Single().Actor;
        Check(MoveProfile.ForActor(group, false).ForceWalk && !MoveProfile.ForActor(group, false).IsPlayer, "Monster group profile is not forced to walk");

        Map map = Build(id => Walkable);
        Cell origin = map.MapCells[90];
        Check(MoveSpeeds.StepDuration(origin, map.MapCells[91], MoveMode.Walk, 1) == 758
            && MoveSpeeds.StepDuration(origin, map.MapCells[105], MoveMode.Walk, 1) == 496
            && MoveSpeeds.StepDuration(origin, map.MapCells[91], MoveMode.Run, 1) == 312,
            "Step durations differ from distance / WALK_SPEEDS or RUN_SPEEDS");
        List<Cell> path = Cells(map, Corridor);
        AnimDuration run = AnimDuration.Compute(path, MoveProfile.Player());
        AnimDuration walk = AnimDuration.Compute(path, MoveProfile.Player("g", false, false));
        Check(run.Mode == MoveMode.Run && walk.Mode == MoveMode.Walk && run.StepCount == 8 && run.Total == run.Steps.Sum()
            && walk.Total > run.Total && run.Steps.All(step => step == 199), "Run detection or per-step timing failed on the 9-cell path");
        Check(run.CellIndexAt(0) == 1 && run.CellIndexAt(199) == 2 && run.CellIndexAt(run.Total + 10) == 8 && run.FinalDirection == 3,
            "Current cell estimate does not follow the step in progress");
        Check(PathfinderUtils.GetTimeOnMap(path[0], path) == run.Total && PathfinderUtils.GetTimeOnMap(path[0], new List<Cell>()) == 20,
            "GetTimeOnMap no longer matches the client duration");
    }

    private static void Search()
    {
        // Grille 15 × 17 (479 cellules) : deux murs de deux lignes, ouverts à droite puis à gauche, imposent un lacet.
        Func<int, bool> wall = id => ((Row(id) == 8 || Row(id) == 9) && Column(id) < 12) || ((Row(id) == 20 || Row(id) == 21) && Column(id) > 1);
        Map map = Build(id => wall(id) ? Unwalkable : Walkable);
        Map closed = Build(id => Row(id) == 20 || Row(id) == 21 ? Unwalkable : Walkable);
        Cell start = map.MapCells[0], end = map.MapCells[CellCount - 1];
        var request = new PathRequest { AllDirections = true };
        Pathfinder.FindPath(map.MapCells, Width, start, end, request);
        long worst = 0;
        List<Cell> path = null;
        for (int run = 0; run < 20; run++)
        {
            var watch = Stopwatch.StartNew();
            path = Pathfinder.FindPath(map.MapCells, Width, start, end, request);
            List<Cell> none = Pathfinder.FindPath(closed.MapCells, Width, closed.MapCells[0], closed.MapCells[CellCount - 1],
                new PathRequest { AllDirections = true, CrossUnwalkable = false });
            watch.Stop();
            worst = Math.Max(worst, watch.ElapsedMilliseconds);
            Check(none == null, "A path crossed a closed wall");
        }
        Check(worst < 50, "Client A* took " + worst + " ms on a 15x17 grid");
        Check(path != null && path[0] == start && path[path.Count - 1] == end && path.All(cell => !wall(cell.CellID)),
            "A* did not go around the walls");
        for (int i = 1; i < path.Count; i++)
            Check(MoveSpeeds.DirectionBetween(path[i - 1], path[i]) >= 0, "A* returned non-adjacent cells");
        using (var finder = new Pathfinder())
        {
            finder.SetMap(map);
            Check(finder.GetAdjacenteCells(map.MapCells[240], false).Count == 4 && finder.GetAdjacenteCells(map.MapCells[240], true).Count == 8
                && finder.GetAdjacenteCells(map.MapCells[0], false).Count == 1 && finder.GetAdjacenteCells(map.MapCells[0], true).Count == 3,
                "Legacy neighbour lookup changed");
        }
        List<Cell> four = Pathfinder.FindPath(map.MapCells, Width, start, end, new PathRequest());
        Check(four != null && four[four.Count - 1] == end, "Four-direction search did not find the way through the gaps");
        for (int i = 1; i < four.Count; i++)
            Check(MoveSpeeds.DirectionBetween(four[i - 1], four[i]) % 2 == 1, "Four-direction search used a diagonal step");
    }

    private sealed class ManualClock : IMoveClock
    {
        private sealed class Waiter { public long Due; public TaskCompletionSource<bool> Source; }
        private readonly object sync = new object();
        private readonly List<Waiter> waiters = new List<Waiter>();
        private long now = 100000;

        public long NowMilliseconds { get { lock (sync) return now; } }

        public Task Delay(int milliseconds, CancellationToken token)
        {
            if (token.IsCancellationRequested) return Task.FromCanceled(token);
            var waiter = new Waiter { Source = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously) };
            lock (sync)
            {
                waiter.Due = now + Math.Max(0, milliseconds);
                if (waiter.Due <= now) return Task.CompletedTask;
                waiters.Add(waiter);
            }
            token.Register(() => { lock (sync) waiters.Remove(waiter); waiter.Source.TrySetCanceled(); });
            return waiter.Source.Task;
        }

        public void Advance(long milliseconds)
        {
            List<Waiter> due;
            lock (sync)
            {
                now += milliseconds;
                due = waiters.Where(waiter => waiter.Due <= now).OrderBy(waiter => waiter.Due).ToList();
                waiters.RemoveAll(waiter => waiter.Due <= now);
            }
            foreach (Waiter waiter in due) waiter.Source.TrySetResult(true);
        }
    }

    private sealed class RecordingContext : SynchronizationContext
    {
        public int Posts;
        public override void Post(SendOrPostCallback callback, object state) { Interlocked.Increment(ref Posts); base.Post(callback, state); }
        public override void Send(SendOrPostCallback callback, object state) { Interlocked.Increment(ref Posts); base.Send(callback, state); }
    }

    private static async Task Within(Task task)
    {
        if (await Task.WhenAny(task, Task.Delay(8000)) != task) throw new TimeoutException("Movement loopback timed out");
        await task;
    }

    private static async Task<T> Within<T>(Task<T> task) { await Within((Task)task); return await task; }

    private static Task<string> Read(Socket peer)
    {
        return Task.Run(() =>
        {
            using (var buffer = new MemoryStream())
            {
                byte[] one = new byte[1];
                while (true)
                {
                    if (peer.Receive(one) != 1) throw new IOException("Synthetic peer disconnected");
                    if (one[0] == 0) return System.Text.Encoding.UTF8.GetString(buffer.ToArray()).TrimEnd('\r', '\n');
                    buffer.WriteByte(one[0]);
                }
            }
        });
    }

    private static void NoPacket(Socket peer, string message)
    {
        Thread.Sleep(250);
        Check(peer.Available == 0, message);
    }

    private static void Until(Func<bool> condition, string message)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.ElapsedMilliseconds > 8000) throw new TimeoutException(message);
            Thread.Sleep(10);
        }
    }

    private static async Task Run()
    {
        Map.AllBotMaps[CorridorMapId] = new Map { MapID = CorridorMapId, MapWidth = Width, MapHeight = 17, X = 40, Y = 40, Back_ID = 0,
            MapData = MapData(id => Array.IndexOf(Corridor, id) >= 0 ? Walkable : Unwalkable) };
        Map.AllBotMaps[OpenMapId] = new Map { MapID = OpenMapId, MapWidth = Width, MapHeight = 17, X = 41, Y = 40, Back_ID = 0,
            MapData = MapData(id => Walkable) };
        MessagesReception.Init();
        GlobalConfig.BYPASS = false;
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            using (var account = new Accounts(new AccountConfig("synthetic-movement", "Synthetic123", "test")))
            {
                var protocolErrors = new List<string>();
                account.Logger.log_event += (log, color) => { if (log.reference == "PROTOCOLE") lock (protocolErrors) protocolErrors.Add(log.message); };
                Task<Socket> accepted = listener.AcceptSocketAsync();
                await account.Connexion.ConnectToServer(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
                using (Socket peer = await Within(accepted))
                {
                    peer.ReceiveTimeout = 6000;
                    account.Game.character.id = 42;
                    account.AccountStates = AccountStates.CONNECTED_INACTIVE;
                    Map map = account.Game.Map;
                    Mouvement movement = account.Game.Manager.Mouvements;
                    var clock = new ManualClock();
                    movement.Clock = clock;
                    var finished = new List<bool>();
                    movement.FinalizeMove += good => { lock (finished) finished.Add(good); };
                    int pathThread = 0;
                    account.Game.character.MoveMinimapPathfinding += cells => pathThread = Thread.CurrentThread.ManagedThreadId;
                    Func<string, Task> feed = packet => Within(MessagesReception.ReceptionAsync(account.Connexion, packet));

                    await feed("GDM|" + CorridorMapId + "|date|key");
                    Check(await Within(Read(peer)) == "GI" && map.MapCells.Length == CellCount, "GDM did not load the corridor map");
                    await feed("GM|+90" + SelfEntry);
                    Check(account.Game.character.Cell == map.MapCells[90] && map.Self != null, "Self GM did not place the character");
                    List<Cell> corridor = Cells(map, Corridor);
                    Check(movement.PreviewPath(map.MapCells[207]).SequenceEqual(corridor), "Preview differs from the only corridor");

                    // Clic depuis un fil « d'interface » bloqué : la recherche et l'envoi se font ailleurs, rien n'est posté sur ce fil.
                    var context = new RecordingContext();
                    int callerThread = 0;
                    MoveResults clicked = MoveResults.FALL;
                    Exception callerError = null;
                    var caller = new Thread(() =>
                    {
                        try
                        {
                            SynchronizationContext.SetSynchronizationContext(context);
                            callerThread = Thread.CurrentThread.ManagedThreadId;
                            clicked = movement.MoveToAsync(map.MapCells[207]).GetAwaiter().GetResult();
                        }
                        catch (Exception error) { callerError = error; }
                    });
                    caller.Start();
                    Check(caller.Join(8000) && callerError == null && clicked == MoveResults.EXIT, "Click from a blocked UI-like thread failed: " + callerError);
                    Check(pathThread != 0 && pathThread != callerThread && context.Posts == 0, "Path search ran on, or posted back to, the calling thread");
                    Check(await Within(Read(peer)) == "GA001" + ClientCompress(corridor) && ClientCompress(corridor) == "bchdcJbdbddp",
                        "GA001 of the 9-cell path differs from the client algorithm");
                    Check(movement.IsAwaitingServer && account.IsMoving() && movement.ActualPath.SequenceEqual(corridor), "Request did not reserve the character");
                    Check(await Within(movement.MoveToAsync(map.MapCells[90])) == MoveResults.CharacterBusyOrFull && !movement.Cancel(),
                        "A second move or a cancel was allowed before the server answered");
                    NoPacket(peer, "Refused move or cancel reached the server");

                    // GA7;1 (deux fois) : un seul GKK7, après la durée du client et pas avant.
                    await feed("GA7;1;42;abAbchdcJbdbddp");
                    await feed("GA7;1;42;abAbchdcJbdbddp");
                    AnimDuration timing = AnimDuration.Compute(corridor, MoveProfile.Player());
                    Check(!movement.IsAwaitingServer && movement.CurrentActionId == 7 && timing.Mode == MoveMode.Run && account.IsMoving(),
                        "GA;1 did not start the run");
                    clock.Advance(timing.Total - 1);
                    NoPacket(peer, "GKK was sent before the client animation ended");
                    Check(movement.EstimatedCell == map.MapCells[207], "Estimated cell did not reach the last step");
                    clock.Advance(1);
                    Check(await Within(Read(peer)) == "GKK7" && account.Game.character.Cell == map.MapCells[207]
                        && account.AccountStates == AccountStates.CONNECTED_INACTIVE && movement.CurrentActionId == null && movement.ActualPath == null,
                        "Arrival was not acknowledged by GKK7");
                    clock.Advance(60000);
                    NoPacket(peer, "Duplicate GA or the response timer produced a second packet");
                    lock (finished) Check(finished.Count == 1 && finished[0], "Arrival was not reported once");

                    // Interruption : GKE<id>|<destination du pas en cours>, sans GKK ensuite.
                    List<Cell> back = corridor.AsEnumerable().Reverse().ToList();
                    Check(await Within(movement.MoveToAsync(map.MapCells[90])) == MoveResults.EXIT
                        && await Within(Read(peer)) == "GA001" + ClientCompress(back) && ClientCompress(back) == "hdbfcJhchfbA", "Return path differs");
                    await feed("GA7;1;42;adphdbfcJhchfbA");
                    AnimDuration backTiming = AnimDuration.Compute(back, MoveProfile.Player());
                    clock.Advance(backTiming.Steps[0] + 1);
                    Check(movement.EstimatedCell == map.MapCells[178], "Estimated cell is not the destination of the second step");
                    Check(movement.Cancel(), "Cancel refused during the walk");
                    Check(await Within(Read(peer)) == "GKE7|178" && account.Game.character.Cell == map.MapCells[178] && map.Self.CellId == 178
                        && account.AccountStates == AccountStates.CONNECTED_INACTIVE && movement.CurrentActionId == null && !movement.Cancel(),
                        "Cancel did not send GKE7|178 or kept the walk");
                    clock.Advance(60000);
                    NoPacket(peer, "A cancelled walk was acknowledged");

                    // Nouveau clic pendant la marche : GKE puis GA001 depuis la cellule estimée.
                    List<Cell> shortPath = Cells(map, new[] { 178, 193, 207 });
                    Check(await Within(movement.MoveToAsync(map.MapCells[207])) == MoveResults.EXIT
                        && await Within(Read(peer)) == "GA001" + ClientCompress(shortPath), "Short path differs");
                    await feed("GA3;1;42;" + Start(178) + ClientCompress(shortPath));
                    Check(AnimDuration.Compute(shortPath, MoveProfile.Player()).Mode == MoveMode.Walk, "Three-cell path ran");
                    clock.Advance(1);
                    List<Cell> fromMiddle = Cells(map, new[] { 193, 178, 163, 149, 135, 120, 105, 90 });
                    Check(await Within(movement.MoveToAsync(map.MapCells[90])) == MoveResults.EXIT, "Redirect refused during the walk");
                    Check(await Within(Read(peer)) == "GKE3|193" && await Within(Read(peer)) == "GA001" + ClientCompress(fromMiddle)
                        && account.Game.character.Cell == map.MapCells[193] && movement.IsAwaitingServer, "Redirect did not send GKE then GA001 from the estimated cell");

                    // GA;0 : refus, état propre et nouveau déplacement possible.
                    await feed("GA;0");
                    Check(!movement.IsAwaitingServer && movement.ActualPath == null && movement.CurrentActionId == null
                        && account.AccountStates == AccountStates.CONNECTED_INACTIVE, "GA;0 left movement state behind");
                    clock.Advance(60000);
                    NoPacket(peer, "GA;0 was followed by a packet");
                    Check(await Within(movement.MoveToAsync(map.MapCells[207])) == MoveResults.EXIT
                        && (await Within(Read(peer))).StartsWith("GA001", StringComparison.Ordinal), "Move refused after GA;0");
                    // Chemin refusé par le serveur (aucun pas) : GKK immédiat, le personnage reste sur place.
                    await feed("GA5;1;42;" + Start(193) + "b" + Start(193).Substring(1));
                    Check(await Within(Read(peer)) == "GKK5" && account.Game.character.Cell == map.MapCells[193]
                        && account.AccountStates == AccountStates.CONNECTED_INACTIVE, "Zero-step server path was not acknowledged in place");
                    // Sans réponse du serveur : demande abandonnée localement, sans paquet.
                    Check(await Within(movement.MoveToAsync(map.MapCells[207])) == MoveResults.EXIT
                        && (await Within(Read(peer))).StartsWith("GA001", StringComparison.Ordinal), "Move refused after a zero-step answer");
                    clock.Advance(Mouvement.ServerResponseTimeout - 1);
                    Check(movement.IsAwaitingServer, "Request abandoned before the response timeout");
                    clock.Advance(1);
                    Until(() => !movement.IsAwaitingServer && account.AccountStates == AccountStates.CONNECTED_INACTIVE, "Unanswered request was never released");
                    NoPacket(peer, "Response timeout produced a packet");

                    // Carte ouverte, huit directions (AR6bk) : un autre joueur est contourné, un groupe de monstres est une destination.
                    await feed("AR6bk");
                    await feed("GDM|" + OpenMapId + "|date|key");
                    Check(await Within(Read(peer)) == "GI" && map.MapID == OpenMapId, "GDM did not load the open map");
                    await feed("GM|+90" + SelfEntry + "|" + OtherPlayer + "|" + Group);
                    Check(map.Players.Count == 1 && map.MonsterGroups.Count == 1 && account.Game.character.Cell == map.MapCells[90], "Open map actors missing");
                    List<Cell> preview = movement.PreviewPath(map.MapCells[94]);
                    Check(await Within(movement.MoveToAsync(map.MapCells[94])) == MoveResults.EXIT, "Move next to another player refused");
                    string around = (await Within(Read(peer))).Substring(5);
                    List<Cell> aroundPath = PathfinderUtils.DecodeServerPath(map, Start(90) + around);
                    Check(around == PathfinderUtils.CompressPath(preview) && aroundPath[aroundPath.Count - 1] == map.MapCells[94]
                        && !aroundPath.Contains(map.MapCells[92]), "Path crossed the other player's cell");
                    await feed("GA0;1;42;" + Start(90) + around);
                    clock.Advance(AnimDuration.Compute(aroundPath, MoveProfile.Player()).Total);
                    Check(await Within(Read(peer)) == "GKK0" && account.Game.character.Cell == map.MapCells[94], "Action id 0 was not acknowledged");

                    Check(movement.GetCellsMove(map.MapCells[96], map.CellsOccuped()) == MoveResults.EXIT
                        && (await Within(Read(peer))).StartsWith("GA001", StringComparison.Ordinal), "Legacy move refused the monster group cell");
                    await feed("GA;0");
                    Check(await Within(movement.MoveToAsync(map.MapCells[96])) == MoveResults.EXIT, "Click on the monster group cell refused");
                    string toGroup = (await Within(Read(peer))).Substring(5);
                    List<Cell> groupPath = PathfinderUtils.DecodeServerPath(map, Start(94) + toGroup);
                    Check(groupPath[groupPath.Count - 1] == map.MapCells[96], "Path does not end on the monster group");
                    await feed("GA0;1;42;" + Start(94) + toGroup);
                    clock.Advance(AnimDuration.Compute(groupPath, MoveProfile.Player()).Total);
                    Check(await Within(Read(peer)) == "GKK0" && account.Game.character.Cell == map.MapCells[96], "Arrival on the group was not acknowledged");

                    // Pods pleins et régénération : le déplacement part sans refus local ni eU1.
                    await feed("Ow2000|1000");
                    Check(account.Game.character.Inventory.Percent_Pods >= 100, "Pods were not updated");
                    account.AccountStates = AccountStates.REGENERATION;
                    Check(await Within(movement.MoveToAsync(map.MapCells[90])) == MoveResults.EXIT
                        && (await Within(Read(peer))).StartsWith("GA001", StringComparison.Ordinal), "Full pods or regeneration blocked the click");
                    await feed("GA;0");
                    Check(movement.GetCellsMove(map.MapCells[90], new List<Cell>()) == MoveResults.EXIT
                        && (await Within(Read(peer))).StartsWith("GA001", StringComparison.Ordinal), "Full pods blocked the legacy move");
                    await feed("GA;0");

                    foreach (string packet in new[] { "GA8;1;42;", "GA8;1;42;a!A", "GA8;1;42;abAibA", "GA8;1;42;ab_bbB", "GA8;1;42;abAbbB", "GA;1;42;abAbdbb" })
                        await feed(packet);
                    clock.Advance(60000);
                    NoPacket(peer, "Malformed server paths were acknowledged");
                    Check(protocolErrors.Count == 0, "Movement packets raised handler exceptions: " + string.Join(" / ", protocolErrors));
                }
            }
        }
        finally
        {
            Map.AllBotMaps.TryRemove(CorridorMapId, out Map ignored);
            Map.AllBotMaps.TryRemove(OpenMapId, out ignored);
            listener.Stop();
        }
    }
}
