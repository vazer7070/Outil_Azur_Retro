using System;
using System.Collections.Generic;
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
using Tool_BotProtocol.Game.Actions;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Maps.Enums;
using Tool_BotProtocol.Game.Maps.Mouvements;
using Tool_BotProtocol.Game.NPC;
using Tool_BotProtocol.Game.Perso;
using Tool_BotProtocol.Utils.Logger;

// Modèle d'acteurs de carte : GM par type (joueurs, PNJ, groupes, marchands, percepteurs, prismes, montures d'enclos,
// monstres de combat), GA hors combat (déplacement sans GAF, GA;2, routeur), GDM/GI, GDF, GDO, GDC, eD, Oa et Gc.
// Serveur fictif local, carte synthétique en mémoire, aucune interface et aucune donnée réelle.
internal static class BotActorsModelSmoke
{
    private const int MapId = 991201;
    private const int MissingMapId = 991299;

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
            Parser();
            Run().GetAwaiter().GetResult();
            Console.WriteLine("OK: GM by type (3 concatenated players with titles, NPC, group stars, merchant, collector, prism, park mount, fight monster), "
                + "GM|-, GA move without GAF, GA;2, GA router, GDM without map still sends GI, GDF triplets, GDO, GDC, eD, Oa, Gc and malformed packets");
        }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.Exit(1); }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    private static async Task Within(Task task)
    {
        if (await Task.WhenAny(task, Task.Delay(8000)) != task) throw new TimeoutException("Actor loopback timed out");
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
                    if (one[0] == 0) return Encoding.UTF8.GetString(buffer.ToArray()).TrimEnd('\r', '\n');
                    buffer.WriteByte(one[0]);
                }
            }
        });
    }

    private static void NoPacket(Socket peer, string message)
    {
        Thread.Sleep(200);
        Check(peer.Available == 0, message);
    }

    // Entrées GM synthétiques au format StarLoco (hors combat sauf mention).
    private const string Alice = "+0;1;0;43;Alice;1,7;10^100;0;1,0,3,6043;ff0000;00ff00;-1;2a,3b~16~3,,,;0;;;Guilde fictive;1a,2b,3c,4d;8;;0";
    private const string Bruno = "+1;2;0;44;Bruno;2;20^90x110;1;0,0,0,0;-1;-1;-1;;0;;;;;0;;0";
    private const string Carla = "+3;3;0;45;Carla;9,12;90^100,50^80;1;2,15,1,5045,1;1;2;3;;1;7;300;;;24;1,2;0";
    private const string Self = "+0;1;0;42;Moi;1;10^100;0;0,0,0,42;-1;-1;-1;;0;;;;;0;;0";
    private const string Npc = "+2;4;0;-1;555;-4;9001^80x120;1;ff;-1;-1;2a,,,;2;3";
    private const string OtherNpc = "+4;0;0;-9;556;-4;9002^100;0;-1;-1;-1;;-1;";
    private const string Group = "+6;1;45;-2;101,102,103;-3;1001^100,1002^90,1003^110;5,6,7;-1,-1,-1;0,0,0,0;-1,-1,-1;0,0,0,0;-1,-1,-1;0,0,0,0";
    private const string Merchant = "+7;1;0;-3;Vendeur;-5;30^100;ff;-1;-1;2a,,,,;Guilde fictive;1a,2b,3c,4d;1";
    private const string Collector = "+8;1;0;-4;a,b;-6;6000^100;3;Guilde fictive;1a,2b,3c,4d";
    private const string Prism = "+9;1;0;-5;1111;-10;8101^100;4;2;1";
    private const string ParkMount = "+10;1;0;-6;Rapide;-9;7002^100;Proprio;5;18";

    private static void Parser()
    {
        GmParseResult players = GmParser.Parse("GM|" + Alice + "\0GM|" + Bruno + "\0GM|" + Carla, false, 42, 0);
        Check(players.Entries.Count == 3 && players.Rejected.Count == 0 && players.Entries.All(entry => entry.Actor is PlayerActor),
            "NUL-concatenated GM packets did not give three players");
        var alice = (PlayerActor)players.Entries[0].Actor;
        var bruno = (PlayerActor)players.Entries[1].Actor;
        var carla = (PlayerActor)players.Entries[2].Actor;
        Check(alice.Kind == ActorKind.Player && alice.Id == 43 && alice.ClassId == 1 && alice.TitleId == 7 && alice.Name == "Alice"
            && alice.SpriteType == 1 && alice.Race_ID == 1, "Title was not separated from the class");
        Check(alice.Alignment.Side == 1 && alice.Alignment.Grade == 3 && alice.Alignment.LevelCode == 6043 && !alice.Alignment.FallenAngelDemon,
            "Player alignment fields were misread");
        Check(alice.Color1 == "ff0000" && alice.Color2 == "00ff00" && alice.Color3 == "-1" && alice.GuildName == "Guilde fictive"
            && alice.GuildEmblem == "1a,2b,3c,4d" && alice.Restrictions == 8 && !alice.HasMount, "Player colours, guild or restrictions shifted");
        Check(alice.Accessories.Count == 2 && alice.Accessories[0].TemplateId == 0x2a && alice.Accessories[1].Slot == 1
            && alice.Accessories[1].Type == 16 && alice.Accessories[1].Frame == 2, "Player accessories (living item frame) were misread");
        Check(bruno.TitleId == 0 && bruno.Gfx == 20 && bruno.ScaleX == 90 && bruno.ScaleY == 110 && bruno.Sexe == 1 && bruno.Orientation == 2,
            "Player without title or with WxH scale was misread");
        Check(carla.TitleId == 12 && carla.Alignment.FallenAngelDemon && carla.Aura == 1 && carla.Emote == "7" && carla.EmoteTimer == "300"
            && carla.MountModelId == 1 && carla.HasMount && carla.LinkedShape == "circle" && carla.LinkedSprites.Count == 1
            && carla.LinkedSprites[0].Gfx == 50 && carla.Gfx == 90, "Player emote, mount or follower sprite was misread");

        GmParseResult self = GmParser.Parse("GM|" + Self, false, 42, 0);
        Check(self.Entries.Single().Actor.IsSelf && !alice.IsSelf, "Self entry was not flagged");
        GmParseResult fallbackGfx = GmParser.Parse("GM|+2;9;0;46;Dan;8;abc;1;0,0,0,0;-1;-1;-1;;0;;;;;0;;0", false);
        Check(((PlayerActor)fallbackGfx.Entries.Single().Actor).Gfx == 81 && fallbackGfx.Entries[0].Actor.Orientation == 2,
            "Unreadable player graphics did not fall back to class and sex");

        GmParseResult fight = GmParser.Parse("GM|+5;1;0;43;Alice;1;10^100;0;12;0,0,0,0;-1;-1;-1;;100;6;3;1;2;3;4;5;6;7;0;"
            + "|+6;1;0;-8;31;-2;1563^100;3;-1;-1;-1;0,0,0,0;50;6;3;1"
            + "|+7;1;0;-10;31;-2;1563^100;3;-1;-1;-1;0,0,0,0;60;7;4;10;11;12;13;14;15;16;1"
            + "|+8;1;0;-11;a,b;-6;6000^100;3;80;6;5;0;0;0;0;0;0;0;1", true);
        Check(fight.Entries.Count == 4 && fight.Rejected.Count == 0, "Fight-layout GM entries were rejected");
        var fighter = (PlayerActor)fight.Entries[0].Actor;
        Check(fighter.InFight && fighter.Level == 12 && fighter.Life == 100 && fighter.ActionPoints == 6 && fighter.MovementPoints == 3
            && fighter.Resistances != null && fighter.Resistances[6] == 7 && fighter.Team == 0, "Player fight fields were misread");
        var monster = (FightMonsterActor)fight.Entries[1].Actor;
        Check(monster.Kind == ActorKind.FightMonster && monster.Grade == 3 && monster.Life == 50 && monster.Team == 1
            && monster.Gfx == 1563 && monster.TemplateID == 31 && monster.Id == -8, "Short fight monster entry was misread");
        var resistant = (FightMonsterActor)fight.Entries[2].Actor;
        Check(resistant.Resistances != null && resistant.Resistances[0] == 10 && resistant.Team == 1 && resistant.MovementPoints == 4,
            "Fight monster with resistances was misread");
        var fightingCollector = (CollectorActor)fight.Entries[3].Actor;
        Check(fightingCollector.Life == 80 && fightingCollector.ActionPoints == 6 && fightingCollector.Team == 1,
            "Collector fight fields were misread");

        GmParseResult broken = GmParser.Parse("GM|+bad;entry|-zz|?x|+1;1;0;43|+1;1;0;-2;101,102;-3;1^100;5|+1;1;0;-1;tpl;-4;1^100|"
            + "+x;1;0;43;A;1|+1;1;0;43;A;z|+1;1;0;-12;mystery;-8;1^100", false);
        Check(broken.Entries.Count == 1 && broken.Entries[0].Actor.Kind == ActorKind.Unknown && broken.Rejected.Count == 8,
            "Malformed GM entries were not rejected one by one");
        Check(GmParser.Parse(null, false).Entries.Count == 0 && GmParser.Parse("GM", false).Entries.Count == 0
            && GmParser.Parse("GM|\0\0GM|", false).Rejected.Count == 0, "Empty GM packets were not ignored");

        Check(GmParser.TryParseMovePath("aaabag", out ServerMovePath move) && move.StartCellId == 0 && move.Path == "bag"
            && move.FinalOrientation == 1 && move.Encoded == "aaabag", "Server path prefix was not separated");
        Check(!GmParser.TryParseMovePath("aaab", out move) && !GmParser.TryParseMovePath("aa!bag", out move)
            && !GmParser.TryParseMovePath("aaa9ag", out move) && !GmParser.TryParseMovePath(null, out move), "Invalid server paths were accepted");
        Check(GameActionPacket.Parse("GA12;1;43;aaabag;x").Parameters == "aaabag;x" && GameActionPacket.Parse("GA;2;42;").ActorId == 42
            && GameActionPacket.Parse("GA;x") == null && GameActionPacket.Parse("GA;0").Actor == "", "GA split differs from the client");
        Check(GmParser.ParseFightSwords("x") == null && GmParser.ParseFightSwords("5;0|bad|x;2").Teams.Count == 0,
            "Malformed Gc entries were not ignored");
        Check(GmParser.ParseCellUpdates("4;aaaaaaaaa|x;aaaaaaaaaa801|5;aaaaa!aaaa801|6").Count == 1
            && GmParser.ParseObjectStates("|1;x|;|2;3;|").Count == 1, "Malformed GDC/GDF entries were not ignored");
    }

    private static async Task Run()
    {
        var definition = new Map { MapID = MapId, MapWidth = 3, MapHeight = 4, X = 3, Y = -4,
            MapData = string.Concat(Enumerable.Repeat("HhGaeaaaaa", 18)), Back_ID = 0 };
        Map.AllBotMaps[MapId] = definition;
        Map.AllBotMaps.TryRemove(MissingMapId, out Map ignoredMap);
        PNJ.ClientNameResolver = template => template == 555 ? "Marchande synthétique" : null;
        MessagesReception.Init();
        GlobalConfig.BYPASS = false;
        var registeredActions = new List<int>();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            using (var account = new Accounts(new AccountConfig("synthetic-actors-model", "Synthetic123", "test")))
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
                    var added = new List<MapActor>(); var removed = new List<MapActor>(); var updated = new List<MapActor>();
                    var cells = new List<int>(); var grounds = new List<int>(); var states = new List<int>(); var changing = new List<string>();
                    var moves = new List<List<Cell>>();
                    int cleared = 0;
                    map.ActorAdded += added.Add; map.ActorRemoved += removed.Add; map.ActorUpdated += updated.Add;
                    map.CellUpdated += cells.Add; map.GroundObjectChanged += grounds.Add; map.ObjectStateChanged += states.Add;
                    map.MapChanging += changing.Add; map.ActorsCleared += () => cleared++;
                    map.EntityMovement += (id, path, duration) => moves.Add(path);
                    Func<string, Task> feed = packet => Within(MessagesReception.ReceptionAsync(account.Connexion, packet));

                    await feed("GDM|" + MapId + "|date|key");
                    Check(await Within(Read(peer)) == "GI" && map.HasMapData && map.MapCells.Length == 18, "GDM did not load the synthetic map before GI");

                    await feed("GM|" + Alice + "\0GM|" + Bruno + "\0GM|" + Carla);
                    Check(map.Players.Count == 3 && map.Actors.Count == 3 && added.Count == 3 && added.All(actor => actor.Kind == ActorKind.Player),
                        "Three concatenated GM players did not reach Map.Actors with ActorAdded");
                    Check(map.PersoList().Count == 3 && map.GetActor(43).Cell == map.MapCells[0] && account.Game.PersoInWorld.ContainsKey(45),
                        "Legacy player views or positions were not fed");
                    await feed("GM|" + Self);
                    Check(map.Self != null && map.Self.IsSelf && map.GetActor(42) == map.Self && !map.Actors.ContainsKey(42)
                        && account.Game.character.Cell == map.MapCells[0], "Self entry was mixed with other actors");

                    await feed("GM|" + Npc + "|" + OtherNpc + "|" + Group + "|" + Merchant + "\0GM|GM|" + Collector + "|" + Prism + "|" + ParkMount);
                    Check(map.Actors.Count == 10 && protocolErrors.Count == 0, "Typed GM entries were not all stored");
                    var npc = (NpcActor)map.GetActor(-1);
                    Check(npc.Kind == ActorKind.Npc && npc.TemplateId == 555 && npc.Id == -1 && npc.Name == "Marchande synthétique"
                        && npc.Gfx == 9001 && npc.ScaleX == 80 && npc.ScaleY == 120 && npc.Sexe == 1 && npc.Color1 == "ff"
                        && npc.Accessories.Count == 1 && npc.ExtraClip == 2 && npc.Artwork == 3 && npc.Orientation == 4,
                        "NPC entry was misread or its id was taken for its template");
                    var otherNpc = (NpcActor)map.GetActor(-9);
                    Check(otherNpc.Name == "PNJ #556" && otherNpc.ExtraClip == -1 && otherNpc.Artwork == 0 && map.NPC_List().Count == 2,
                        "NPC name fallback or legacy NPC view failed");
                    var group = (MonsterGroupActor)map.GetActor(-2);
                    Check(group.Kind == ActorKind.MonsterGroup && group.Stars == 45 && group.Members.Count == 3 && group.Leader.TemplateId == 101
                        && group.Members[1].Gfx == 1002 && group.Members[1].ScaleX == 90 && group.Members[2].Level == 7 && group.TotalLevel == 18
                        && group.Name == "Monstre #101" && group.Gfx == 1001, "Monster group stars or members were misread");
                    Check(group.GetAllMonster == 3 && group.MobsGroupelevel == 18 && group.GroupSize(102) == 1
                        && group.MobsInGroupe.All(member => member.Cell == map.MapCells[6]) && map.MonsterList().Count == 1,
                        "Legacy monster group view lost members or their cell");
                    var merchant = (MerchantActor)map.GetActor(-3);
                    Check(merchant.Kind == ActorKind.Merchant && merchant.Name == "Vendeur" && merchant.Color1 == "ff" && merchant.Stuff == "2a,,,,"
                        && merchant.GuildName == "Guilde fictive" && merchant.OfflineType == 1, "Merchant entry was misread");
                    var collector = (CollectorActor)map.GetActor(-4);
                    Check(collector.Kind == ActorKind.Collector && collector.FirstNameId == 10 && collector.LastNameId == 11 && collector.Level == 3
                        && collector.Gfx == 6000 && collector.DisplayName == "Percepteur de Guilde fictive", "Collector entry (doubled GM prefix) was misread");
                    var prism = (PrismActor)map.GetActor(-5);
                    Check(prism.Kind == ActorKind.Prism && prism.Level == 4 && prism.AlignmentValue == 2 && prism.AlignmentSide == 1 && prism.Gfx == 8101,
                        "Prism entry was misread");
                    var mount = (ParkMountActor)map.GetActor(-6);
                    Check(mount.Kind == ActorKind.ParkMount && mount.Name == "Rapide" && mount.OwnerName == "Proprio" && mount.Level == 5 && mount.ModelId == 18,
                        "Park mount entry was misread");
                    Check(map.ActorsOnCell(0).Count == 1 && map.ActorsOfKind(ActorKind.Npc).Count == 2 && npc.GetKind() == ActorKind.Npc
                        && account.Game.character.GetKind() == ActorKind.Player, "Actor views or Entites kind lookup failed");

                    int updatesBefore = updated.Count;
                    await feed("GM|~1;5;0;44;Bruno;2;20^100;1;0,0,0,0;-1;-1;-1;;0;;;;;0;;0|--3|-77777");
                    Check(map.GetActor(44).Orientation == 5 && updated.Count == updatesBefore + 1 && map.GetActor(-3) == null
                        && removed.Count == 1 && removed[0].Id == -3 && map.Actors.Count == 9, "GM update/removal did not raise the right events");

                    // Chemin serveur a<départ><chemin> : un seul triplet après le préfixe, deux pas décodés ; aucun GAF n'est envoyé.
                    await feed("GA12;1;43;aaabag");
                    var alice = (PlayerActor)map.GetActor(43);
                    Check(alice.Cell == map.MapCells[6] && alice.CellId == 6 && alice.Orientation == 1 && moves.Count == 1 && moves[0].Count == 3
                        && moves[0][0].CellID == 0 && moves[0][1].CellID == 3 && moves[0][2].CellID == 6, "Other actor path was not decoded from its start cell");
                    NoPacket(peer, "Another actor's movement was acknowledged");
                    await feed("GA13;1;42;aaabag");
                    Check(await Within(Read(peer)) == "GKK13" && account.Game.character.Cell == map.MapCells[6] && map.Self.CellId == 6
                        && account.AccountStates == AccountStates.CONNECTED_INACTIVE, "Own movement was not acknowledged once without GAF");
                    NoPacket(peer, "Own movement was acknowledged twice");
                    await feed("GA14;1;43;aa!bag");
                    await feed("GA15;1;43;aaab");
                    await feed("GA;0");
                    Check(alice.CellId == 6 && moves.Count == 2, "Invalid paths moved an actor");

                    await feed("eD43|5");
                    await feed("eD43|9");
                    await feed("eDxx|1");
                    await feed("eD42|3");
                    Check(alice.Orientation == 5 && map.Self.Orientation == 3 && account.Game.character.Orientation == 3, "eD orientation was not applied");
                    await feed("Oa43|2c,,3d~17~1,,");
                    Check(alice.Stuff == "2c,,3d~17~1,," && alice.Accessories.Count == 2 && alice.Accessories[1].Slot == 2
                        && alice.Accessories[1].Frame == 0, "Oa accessories were not applied");
                    await feed("Oa-1|2b,,,");
                    Check(npc.AccessoriesRaw == "2b,,," && npc.Accessories.Count == 1, "Oa on an NPC was ignored");

                    await feed("Gc+77;0|43;6;0;-1|-1;7;1;-1");
                    Check(map.FightSwords.TryGetValue(77, out FightSwordsActor swords) && swords.Teams.Count == 2 && swords.Teams[0].Cell == map.MapCells[6]
                        && swords.Teams[1].Cell == map.MapCells[7] && swords.Teams[1].TeamType == 1 && swords.Kind == ActorKind.FightSwords
                        && !map.Actors.ContainsKey(77), "Gc+ fight swords were not placed on both team cells");
                    await feed("Gc-77");
                    await feed("Gc-x");
                    await feed("Gc+");
                    Check(map.FightSwords.Count == 0 && removed.Any(actor => actor.Id == 77), "Gc- did not remove the fight swords");

                    await feed("GDO+5;7655;0");
                    await feed("GDO+8;1234;1;5;10");
                    Check(map.GroundObjects[5].ItemTemplateId == 7655 && map.GroundObjects[8].Durability == 5 && map.GroundObjects[8].DurabilityMax == 10,
                        "GDO+ ground objects were misread");
                    await feed("GDO-5;0;0");
                    await feed("GDOx");
                    Check(!map.GroundObjects.ContainsKey(5) && map.GroundObjects.Count == 1 && grounds.Count == 3, "GDO- did not remove the object");

                    Cell door = map.MapCells[4];
                    Check(door.C_Types == CellTypes.WALKABLE && door.LineofSight, "Synthetic cell is not walkable");
                    await feed("GDC4;aaaaaaaaaa1801;1");
                    Check(door.C_Types == CellTypes.NOT_WALKABLE && !door.LineofSight && cells.Contains(4), "GDC did not close the cell");
                    await feed("GDC4;aaGaaaaaaa801;1");
                    Check(door.C_Types == CellTypes.WALKABLE && !door.LineofSight, "GDC mask did not limit the replaced fields");
                    await feed("GDC4");
                    Check(door.C_Types == CellTypes.WALKABLE && door.LineofSight, "GDC without data did not restore the cell");
                    await feed("GDC;");
                    await feed("GDC999;aaaaaaaaaa801;1");

                    await feed("GDF|0;2;0|1;1;1|2;3|");
                    Check(map.ObjectStates.Count == 3 && map.ObjectStates[0].State == 2 && map.ObjectStates[0].Interactive == false
                        && map.ObjectStates[1].Interactive == true && map.ObjectStates[2].State == 3 && map.ObjectStates[2].Interactive == null
                        && states.Count == 3, "GDF did not keep every triplet");

                    // Routeur GA : gestionnaire découvert par attribut (GA;4) et gestionnaire enregistré par une autre fonction.
                    Check(GameActionRouter.IsRegistered(4), "GA;4 handler was not discovered");
                    var seen = new List<string>();
                    GameActionRouter.Register(950, context => { seen.Add(context.Packet.Actor + "|" + context.Parameters); return Task.CompletedTask; });
                    registeredActions.Add(950);
                    bool duplicate = false;
                    try { GameActionRouter.Register(950, context => Task.CompletedTask); }
                    catch (InvalidOperationException) { duplicate = true; }
                    bool discovered = false;
                    try { GameActionRouter.Register(4, context => Task.CompletedTask); }
                    catch (InvalidOperationException) { discovered = true; }
                    Check(duplicate && discovered, "A GA action accepted two handlers");
                    var unhandled = new List<int>();
                    Action<GameActionContext> onUnhandled = context => unhandled.Add(context.ActionId);
                    GameActionRouter.Unhandled += onUnhandled;
                    try
                    {
                        await feed("GA;950;43;1;2");
                        await feed("GA;951;43;");
                        await feed("GA;4;42;42,7");
                    }
                    finally { GameActionRouter.Unhandled -= onUnhandled; }
                    Check(seen.SequenceEqual(new[] { "43|1;2" }) && unhandled.SequenceEqual(new[] { 951 }), "GA router dispatch failed");
                    Check(account.Game.character.Cell == map.MapCells[7] && map.Self.CellId == 7, "GA;4 relocation through the router failed");

                    // GA;2 : un autre acteur quitte la carte, puis le personnage du compte (la carte est vidée avant GDM).
                    await feed("GA;2;44;");
                    Check(map.GetActor(44) == null && removed.Any(actor => actor.Id == 44) && !account.Game.PersoInWorld.ContainsKey(44),
                        "GA;2 did not remove the actor");
                    await feed("GA;2;42;7");
                    Check(map.Actors.Count == 0 && map.Self == null && map.GroundObjects.Count == 0 && map.ObjectStates.Count == 0
                        && cleared == 1 && changing.SequenceEqual(new[] { "7" }), "GA;2 for the account did not clear the map");

                    await feed("GDM|" + MissingMapId + "|date|key");
                    Check(await Within(Read(peer)) == "GI" && !map.HasMapData && map.MapID == MissingMapId, "GDM of a missing map did not send GI");
                    await feed("GM|" + Alice);
                    Check(map.Actors.Count == 1 && map.GetActor(43).Cell == null && map.GetActor(43).CellId == 0 && map.PersoList().Count == 0,
                        "Actor of a missing map lost its announced cell");
                    await feed("GDM|abc");
                    Check(await Within(Read(peer)) == "GI" && map.Actors.Count == 0, "Malformed GDM did not send GI or kept actors");

                    foreach (string packet in new[] { "GM", "GM|", "GM|+;;;", "GM|~", "GA", "GA;x", "GA1;1", "GA1;1;;", "GA;2", "GA;4;42;x",
                        "eD", "eD|", "Oa", "Oa|", "Gc", "GDO", "GDC", "GDF", "GDF|;;;" })
                        await feed(packet);
                    Check(protocolErrors.Count == 0, "Malformed packets raised handler exceptions: " + string.Join(" / ", protocolErrors));
                    NoPacket(peer, "Malformed packets produced a reply");
                }
            }
        }
        finally
        {
            foreach (int action in registeredActions) GameActionRouter.Unregister(action);
            PNJ.ClientNameResolver = null;
            Map.AllBotMaps.TryRemove(MapId, out Map ignored);
            listener.Stop();
        }
    }
}
