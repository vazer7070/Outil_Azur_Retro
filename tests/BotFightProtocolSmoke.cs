using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Actions;
using Tool_BotProtocol.Game.Combats;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Perso.Spells;
using Tool_BotProtocol.Utils.Crypto;

/// <summary>
/// Protocole de combat F12a sur paquets synthétiques (format StarLoco / client 1.34) : ordre des tours (GTL, GTR→GT, GTS à deux
/// ou trois champs), effets (GIE, GIe), zones (GDZ), options (Go, fN/fS/fP/fH), drapeau (Gf), abandon et exclusion (GQ),
/// table GA de combat (vie, PA/PM, états, invisibilité, porter/lancer, invocations, GA;999) et résultat GE (PvM, PvP, mal formé).
/// </summary>
internal static class BotFightProtocolSmoke
{
    private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            string path = Path.Combine(TestPaths.ApplicationBin, new AssemblyName(args.Name).Name + ".dll");
            if (!File.Exists(path)) path = Path.ChangeExtension(path, "exe");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        try { Run().GetAwaiter().GetResult(); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    private static async Task<T> Within<T>(Task<T> task)
    { if (await Task.WhenAny(task, Task.Delay(6000)) != task) throw new TimeoutException("Fight protocol loopback timed out"); return await task; }

    private static Task<string> Read(Socket peer)
    {
        return Task.Run(() =>
        {
            using (var stream = new MemoryStream())
            {
                byte[] one = new byte[1];
                while (true)
                {
                    if (peer.Receive(one) != 1) throw new IOException("Loopback disconnected");
                    if (one[0] == 0) return Encoding.UTF8.GetString(stream.ToArray()).TrimEnd('\r', '\n');
                    stream.WriteByte(one[0]);
                }
            }
        });
    }

    private static Task Feed(Accounts account, string packet) { return MessagesReception.ReceptionAsync(account.Connexion, packet); }

    private static string LastJournal(Fights fight) { FightLogEntry entry = fight.Journal.LastOrDefault(); return entry == null ? string.Empty : entry.Text; }

    private static void CheckParsers()
    {
        List<int> order; int actor, duration; int? table;
        Check(FightTurnPackets.TryParseTurnList("|1|2|3", out order) && order.SequenceEqual(new[] { 1, 2, 3 }), "GTL with leading separator");
        Check(FightTurnPackets.TryParseTurnList("1|2|3", out order) && order.SequenceEqual(new[] { 1, 2, 3 }), "GTL1|2|3 form");
        Check(!FightTurnPackets.TryParseTurnList("|1|x", out order) && order.Count == 0 && !FightTurnPackets.TryParseTurnList(null, out order), "Malformed GTL accepted");
        Check(FightTurnPackets.TryParseTurnStart("1|30000", out actor, out duration, out table) && actor == 1 && duration == 30000 && table == null, "Two-field GTS");
        Check(FightTurnPackets.TryParseTurnStart("1|30000|4", out actor, out duration, out table) && table == 4, "Three-field GTS");
        Check(!FightTurnPackets.TryParseTurnStart("1", out actor, out duration, out table) && !FightTurnPackets.TryParseTurnStart("1|-5", out actor, out duration, out table),
            "Malformed GTS accepted");
        Check(FightEffectPackets.ParseEffect("111;1,2;3;;;;2;10").Count == 2 && FightEffectPackets.ParseEffect("111;1;3").Count == 0
            && FightEffectPackets.ParseEffect("x;1;3;;;;2;10").Count == 0, "GIE parser");
        Check(FightEffectPackets.ParseZones("+1;0;3|-2;1;4|+bad|?").Count == 2, "GDZ parser");
        bool enabled; FightOptions option; long team;
        Check(FightEffectPackets.TryParseOption("+H42", out enabled, out option, out team) && enabled && option == FightOptions.NeedHelp && team == 42, "Go+H parser");
        Check(!FightEffectPackets.TryParseOption("+h42", out enabled, out option, out team) && !FightEffectPackets.TryParseOption("*A42", out enabled, out option, out team),
            "Go accepts an option letter the client ignores");
        Check(FightEffectPackets.RequestFor(FightOptions.BlockJoiner) == "fN" && FightEffectPackets.RequestFor(FightOptions.BlockSpectators) == "fS"
            && FightEffectPackets.RequestFor(FightOptions.PartyOnly) == "fP" && FightEffectPackets.RequestFor(FightOptions.NeedHelp) == "fH"
            && FightEffectPackets.RequestFor(FightOptions.BlockJoiner | FightOptions.NeedHelp) == null, "Option request packets");
        Check(FightResult.Parse("") == null && FightResult.Parse("abc|1|0") == null, "Unreadable GE duration accepted");
        FightResult partial = FightResult.Parse("100|7|0|zz;1|2;7;Name;1;0");
        Check(partial != null && partial.Rejected.Count == 1 && partial.Entries.Count == 1 && partial.StarBonus == null, "Malformed GE line stops parsing");
        FightResult pool = FightResult.Parse("100;15|7|0|6;311~2,0~4,abc~1,312~1;40");
        Check(pool.StarBonus == 15 && pool.Pools.Count == 1 && pool.Pools[0].Items.Count == 1 && pool.Pools[0].Kamas == 40, "GE pool line or item stop rule");
    }

    private static async Task Run()
    {
        CheckParsers();
        MessagesReception.Init(); Spell.AllSpells.Clear();
        new Spell(10, "Sort test 10");
        int[] required = { 4, 5, 11, 50, 51, 52, 100, 101, 102, 103, 104, 105, 106, 107, 108, 110, 150, 151, 152, 180, 181, 200, 300, 302, 303, 305, 309, 501, 950, 999 };
        Check(required.All(FightActionTable.IsRegistered), "Combat GA table misses: " + string.Join(",", required.Where(id => !FightActionTable.IsRegistered(id))));
        Check(!FightActionTable.IsRegistered(0) && !FightActionTable.IsRegistered(1), "Refusal/movement must stay in Fights");
        Map.AllBotMaps[900091] = new Map { MapID = 900091, MapWidth = 3, MapHeight = 4, MapData = string.Concat(Enumerable.Repeat("HhGaeaaaaa", 18)) };
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            using (var account = new Accounts(new AccountConfig("synthetic-fight", "synthetic", "loopback")))
            {
                Task<Socket> accept = listener.AcceptSocketAsync();
                await account.Connexion.ConnectToServer(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
                using (Socket peer = await Within(accept))
                {
                    peer.ReceiveTimeout = 6000; account.Game.character.id = 42;
                    await Feed(account, "GDM|900091|date|"); Check(await Within(Read(peer)) == "GI", "GDM did not request GI");
                    await Feed(account, "GM|+0;1;0;42;Synthetic;1;10^100;0");
                    Fights fight = account.Game.Fight;
                    int journalEvents = 0, flags = 0, optionEvents = 0, results = 0;
                    fight.JournalEntryAdded += entry => journalEvents++;
                    fight.FlagReceived += flag => flags++;
                    fight.FightOptionChanged += (teamId, value) => optionEvents++;
                    fight.CombatResultReceived += result => results++;

                    // Go is broadcast to the whole map: it is kept outside a fight too (swords of other fights).
                    await Feed(account, "Go+A42"); await Feed(account, "Go+S42");
                    Check(fight.GetTeamOptions(42) == (FightOptions.BlockJoiner | FightOptions.BlockSpectators), "Go+ not merged");
                    await Feed(account, "Go-A42"); await Feed(account, "Go+Z42"); await Feed(account, "Go+A"); await Feed(account, "Go");
                    Check(fight.GetTeamOptions(42) == FightOptions.BlockSpectators && optionEvents == 3, "Go- ignored or malformed Go applied");
                    Check(!(await fight.ToggleOptionAsync(FightOptions.BlockJoiner)).Sent && !(await fight.GiveUpAsync()).Sent
                        && !(await fight.SetFlagAsync(5)).Sent && !(await fight.KickAsync(43)).Sent, "Fight requests accepted outside a fight");
                    await Feed(account, "GTL|1|2"); await Feed(account, "GTR42");
                    Check(fight.TurnOrder.Count == 0 && peer.Available == 0, "Turn packets applied or answered outside a fight");

                    // Placement: options, kick, flag. GJK empties the map like Game.onJoin (cleanMap(1)): only the fighters' GM come back.
                    await Feed(account, "GM|+2;4;0;-1;555;-4;9001^80x120;1;ff;-1;-1;2a,,,;2;3|+6;1;0;77;Passerby;1;10^100;0");
                    await Feed(account, "Gc+88;0|43;6;0;-1|-1;7;1;-1"); await Feed(account, "GDO+5;7655;0");
                    Check(account.Game.Map.GetActor(-1) != null && account.Game.Map.GetActor(77) != null && account.Game.Map.FightSwords.Count == 1
                        && account.Game.Map.GroundObjects.Count == 1, "Map actors before GJK not known");
                    await Feed(account, "GJK2|1|1|0|30000|0");
                    Check(!account.Game.Map.AllActors.Any() && account.Game.Map.Self == null && account.Game.Map.FightSwords.Count == 0
                        && account.Game.Map.GroundObjects.Count == 0, "GJK kept the actors of the map (NPC, players, swords, ground objects)");
                    await Feed(account, "GP" + Hash.Get_Cell_Char(0) + Hash.Get_Cell_Char(3) + "|" + Hash.Get_Cell_Char(9) + "|0");
                    Check(fight.IsPlacement && fight.TeamOptions.Count == 0, "Join keeps options of another fight");
                    await Feed(account, "GM|+0;1;0;42;Synthetic;1;10^100;0|+3;1;0;43;Ally;1;10^100;0"); await Feed(account, "GIC|42;3;1");
                    Check(account.Game.Map.AllActors.Select(actor => actor.Id).OrderBy(id => id).SequenceEqual(new long[] { 43 })
                        && account.Game.Map.Self != null && account.Game.Map.Self.Id == 42, "Fighters' GM after GJK did not rebuild the map actors");
                    Check(fight.Fighters.ContainsKey(43) && account.Game.character.Cell.CellID == 3, "Placement fighters not known");
                    Check((await fight.ToggleOptionAsync(FightOptions.BlockJoiner)).Sent && await Within(Read(peer)) == "fN", "fN wire");
                    Check((await fight.ToggleOptionAsync(FightOptions.BlockSpectators)).Sent && await Within(Read(peer)) == "fS", "fS wire");
                    Check((await fight.ToggleOptionAsync(FightOptions.PartyOnly)).Sent && await Within(Read(peer)) == "fP", "fP wire");
                    Check((await fight.ToggleOptionAsync(FightOptions.NeedHelp)).Sent && await Within(Read(peer)) == "fH", "fH wire");
                    Check(!(await fight.ToggleOptionAsync(FightOptions.BlockJoiner | FightOptions.NeedHelp)).Sent && peer.Available == 0, "Combined option sent");
                    await Feed(account, "Go+A42"); await Feed(account, "Go+H42");
                    Check(fight.OwnTeamOptions == (FightOptions.BlockJoiner | FightOptions.NeedHelp), "Own team options not read from Go");
                    Check(!(await fight.KickAsync(42)).Sent && !(await fight.KickAsync(77)).Sent && !(await fight.KickAsync(0)).Sent && peer.Available == 0,
                        "Kick of self/unknown/non-player accepted");
                    Check((await fight.KickAsync(43)).Sent && await Within(Read(peer)) == "GQ43", "Kick wire");
                    Check(!(await fight.SetFlagAsync(500)).Sent && peer.Available == 0, "Flag on a missing cell accepted");
                    Check((await fight.SetFlagAsync(5)).Sent && await Within(Read(peer)) == "Gf5", "Flag wire");
                    await Feed(account, "Gf43|7");
                    Check(fight.LastFlag != null && fight.LastFlag.ActorId == 43 && fight.LastFlag.CellId == 7 && flags == 1 && LastJournal(fight).Contains("7"),
                        "Gf not recorded");
                    await Feed(account, "Gf43"); await Feed(account, "Gfx|y"); await Feed(account, "Gf43|-1");
                    Check(fight.LastFlag.CellId == 7 && flags == 1, "Malformed Gf applied");

                    // Turn order and timeline.
                    await Feed(account, "GS");
                    // A monster fighter (type -2) is named from monsters.xml like the client, not by its template id.
                    Tool_BotProtocol.Game.Monstres.Monstres.ClientNameResolver = template => template == 101 ? "Bouftou synthétique" : null;
                    await Feed(account, "GM|+9;1;0;-7;101;-2;1100^100;1;-1;-1;-1;0,0,0,0;60;4;2;1");
                    Tool_BotProtocol.Game.Monstres.Monstres.ClientNameResolver = null;
                    Check(fight.Fighters[-7].Name == "Bouftou synthétique", "Monster fighter not named from the client texts: " + fight.Fighters[-7].Name);
                    await Feed(account, "GTL|1|2|3"); Check(fight.TurnOrder.SequenceEqual(new[] { 1, 2, 3 }), "GTL|1|2|3 order");
                    await Feed(account, "GTL1|2|3"); Check(fight.TurnOrder.SequenceEqual(new[] { 1, 2, 3 }), "GTL1|2|3 order");
                    await Feed(account, "GTL|42|-7|43"); await Feed(account, "GTL|42|x");
                    Check(fight.TurnOrder.SequenceEqual(new[] { 42, -7, 43 }) && fight.Timeline.IndexOf(-7) == 1, "GTL order or malformed GTL");
                    await Feed(account, "GTR42"); Check(await Within(Read(peer)) == "GT" && fight.Timeline.ReadyActorId == 42, "GTR not answered with GT");
                    await Feed(account, "GTR"); await Feed(account, "GTRabc"); Check(peer.Available == 0, "Malformed GTR answered");
                    await Feed(account, "GTS1|30000");
                    FightTimeline timeline = fight.Timeline;
                    Check(fight.CurrentActorId == 1 && fight.TurnDurationMilliseconds == 30000 && timeline.TableTurn == null
                        && timeline.RemainingMilliseconds(DateTime.UtcNow) > 0 && timeline.RemainingMilliseconds(DateTime.UtcNow) <= 30000, "Two-field GTS");
                    await Feed(account, "GTS"); await Feed(account, "GTSabc|1"); await Feed(account, "GTS5|-1");
                    Check(fight.CurrentActorId == 1, "Malformed GTS applied");
                    await Feed(account, "GTF1"); Check(fight.Timeline.LastActorId == 1 && fight.CurrentActorId == 0, "GTF timeline");
                    await Feed(account, "GTM|42;0;100;8;3;3;;100|-7;0;60;4;2;9;;60|43;0;50;6;3;6;;50");
                    await Feed(account, "GTS42|30000|3"); Check(fight.IsMyTurn && fight.Timeline.TableTurn == 3, "Three-field GTS");

                    // GIE effects and their expiry.
                    await Feed(account, "GIE111;-7;2;;;;3;10");
                    FightEffect enemyEffect = fight.GetEffects(-7).Single();
                    Check(enemyEffect.EffectId == 111 && enemyEffect.Param1 == 2 && enemyEffect.RemainingTurns == 3 && enemyEffect.SpellId == 10
                        && enemyEffect.Source == FightEffectSource.EffectPacket, "GIE effect state");
                    await Feed(account, "GIE112;42;5;;;;2;10"); await Feed(account, "GIE112;42;5;;;;2;10");
                    FightEffect ownEffect = fight.GetEffects(42).Single();
                    Check(ownEffect.Param1 == 10 && ownEffect.RemainingTurns == 3, "GIE on the current actor: merge or extra turn");
                    await Feed(account, "GIE150;43;1;;;;-1;0"); await Feed(account, "GIEabc"); await Feed(account, "GIE111;x;2;;;;3;0"); await Feed(account, "GIE111");
                    Check(fight.Effects.Count == 3, "Malformed GIE applied");
                    await Feed(account, "GTF42"); await Feed(account, "GTS-7|30000");
                    Check(fight.GetEffects(42).Single().RemainingTurns == 2 && fight.GetEffects(-7).Single().RemainingTurns == 3, "Effects of the previous actor not decremented");
                    await Feed(account, "GTF-7"); await Feed(account, "GTS43|30000"); await Feed(account, "GTF43"); await Feed(account, "GTS42|30000");
                    Check(fight.GetEffects(-7).Single().RemainingTurns == 2 && fight.GetEffects(43).Single().RemainingTurns == -1, "Infinite effect expired");
                    await Feed(account, "GIe"); Check(fight.Effects.Count == 0, "GIe ignored");

                    // States, invisibility, life.
                    await Feed(account, "GA;950;42;-7,8,1");
                    Check(fight.HasState(-7, 8) && fight.GetStates(-7).SequenceEqual(new[] { 8 }) && LastJournal(fight).Contains("état 8"), "GA;950 enter state");
                    await Feed(account, "GA;950;42;-7,3,1"); await Feed(account, "GA;950;42;-7,8,0"); await Feed(account, "GA;950;42;-7");
                    Check(fight.GetStates(-7).SequenceEqual(new[] { 3 }), "GA;950 exit state or malformed state");
                    await Feed(account, "GA;150;-7;-7,2"); Check(fight.Fighters[-7].IsInvisible && LastJournal(fight).Contains("invisible"), "GA;150 invisibility");
                    await Feed(account, "GA;150;-7;-7,0"); Check(!fight.Fighters[-7].IsInvisible, "GA;150 visibility");
                    await Feed(account, "GA;100;42;-7,-15"); Check(fight.Fighters[-7].Life == 45 && LastJournal(fight).Contains("perd 15 PV"), "GA;100 damage");
                    await Feed(account, "GA;108;42;-7,10"); Check(fight.Fighters[-7].Life == 55, "GA;108 heal");
                    await Feed(account, "GA;100;42;-7,0"); Check(fight.Fighters[-7].Life == 55 && LastJournal(fight).Contains("aucun PV"), "GA;100 without change");

                    // Characteristic buffs, AP/MP.
                    await Feed(account, "GA;117;42;42,2,3"); await Feed(account, "GA;117;42;42,2");
                    FightEffect buff = fight.GetEffects(42).Single();
                    Check(buff.EffectId == 117 && buff.Source == FightEffectSource.GameAction && buff.Param1 == 2 && buff.RemainingTurns == 3 && buff.CasterId == "42",
                        "Characteristic GA buff");
                    await Feed(account, "GA;116;42;-7,-1,-1"); Check(fight.GetEffects(-7).Single().RemainingTurns == -1, "Infinite GA buff");
                    await Feed(account, "GA;101;42;-7,-2,1"); Check(fight.Fighters[-7].ActionPoints == 2 && LastJournal(fight).Contains("perd 2 PA"), "GA;101 AP loss");
                    int movement = fight.MovementPoints;
                    await Feed(account, "GA;128;42;42,1,1"); Check(fight.MovementPoints == movement + 1 && fight.Fighters[42].MovementPoints == movement + 1, "GA;128 MP gain");

                    // Zones, including the GA;999 envelope.
                    await Feed(account, "GDZ+5;0;3|+6;1;4"); Check(fight.Zones.Count == 2, "GDZ+");
                    await Feed(account, "GDZ-5;0;3"); await Feed(account, "GDZ"); await Feed(account, "GDZ+x;1;2");
                    Check(fight.Zones.Count == 1 && fight.Zones[0].CellId == 6 && fight.Zones[0].Size == 1 && fight.Zones[0].Color == 4, "GDZ- or malformed GDZ");
                    await Feed(account, "GA;999;42;GDZ+7;0;3"); Check(fight.Zones.Any(zone => zone.CellId == 7), "GDZ inside GA;999");
                    await Feed(account, "GA;999;42;GTL|43|42|-7"); Check(fight.TurnOrder.SequenceEqual(new[] { 43, 42, -7 }), "GTL inside GA;999");
                    await Feed(account, "GA;999;42;GA;999;42;GTL|1"); await Feed(account, "GA;999;42;");
                    Check(fight.TurnOrder.SequenceEqual(new[] { 43, 42, -7 }), "Nested GA;999 executed");

                    // Summon (GM entry re-dispatched) then carry/throw/drop.
                    await Feed(account, "GA;181;42;+11;1;0;-8;102;-2;1200^100;1;-1;-1;-1;0,0,0,0;30;3;3;0");
                    Check(fight.Fighters.ContainsKey(-8) && fight.Fighters[-8].Life == 30 && fight.Fighters[-8].CellId == 11 && account.Game.Map.Entites.ContainsKey(-8)
                        && LastJournal(fight).Contains("invoque"), "GA;181 summon");
                    await Feed(account, "GA;181;42;bad"); await Feed(account, "GA;181;42;+");
                    await Feed(account, "GA;50;42;-8");
                    Check(fight.Fighters[42].CarryingId == -8 && fight.Fighters[-8].CarriedById == 42 && fight.Fighters[-8].CellId == 3, "GA;50 carry");
                    await Feed(account, "GA;51;42;8");
                    Check(fight.Fighters[-8].CellId == 8 && fight.Fighters[42].CellId == 3 && account.Game.character.Cell.CellID == 3
                        && fight.Fighters[42].CarryingId == 0 && fight.Fighters[-8].CarriedById == 0 && account.Game.Map.Entites[-8].Cell.CellID == 8,
                        "GA;51 must move the carried fighter, not the carrier");
                    await Feed(account, "GA;50;42;-8"); await Feed(account, "GA;52;42;-8,9");
                    Check(fight.Fighters[-8].CellId == 9 && fight.Fighters[42].CarryingId == 0, "GA;52 drop");

                    // Relocation, direction, interactive frame.
                    await Feed(account, "GA;4;42;-7,10"); Check(fight.Fighters[-7].CellId == 10 && account.Game.Map.Entites[-7].Cell.CellID == 10, "GA;4");
                    await Feed(account, "GA;5;42;-7,13"); Check(fight.Fighters[-7].CellId == 13, "GA;5");
                    await Feed(account, "GA;11;42;-7,3"); Check(fight.Fighters[-7].Orientation == 3, "GA;11");
                    await Feed(account, "GA;200;42;5,3"); Check(account.Game.Map.ObjectStates[5].State == 3, "GA;200");

                    // Dispel and death.
                    await Feed(account, "GA;117;-8;-7,1,2"); Check(fight.GetEffects(-7).Count == 2, "Buff cast by the summon");
                    await Feed(account, "GA;132;42;42"); Check(fight.GetEffects(42).Count == 0 && LastJournal(fight).Contains("retire"), "GA;132");
                    await Feed(account, "GA;103;42;-8");
                    Check(fight.Fighters[-8].IsDead && fight.Fighters[-8].CellId == -1 && fight.GetEffects(-7).All(effect => effect.CasterId != "-8")
                        && fight.GetEffects(-7).Count == 1, "GA;103 death or caster effects kept");

                    // Spells and journal-only actions.
                    await Feed(account, "GA;300;-7;10,3,0,1"); Check(LastJournal(fight).Contains("Sort test 10") && fight.GetSpellCastsThisTurn(10) == 0, "GA;300 of another actor");
                    await Feed(account, "GA;302;-7;10"); Check(LastJournal(fight).Contains("échec critique"), "GA;302");
                    await Feed(account, "GA;303;-7;3"); Check(LastJournal(fight).Contains("corps à corps"), "GA;303");
                    await Feed(account, "GA;305;-7;"); Check(LastJournal(fight).Contains("échec critique au corps"), "GA;305");
                    await Feed(account, "GA;309;42;-7,1"); Check(LastJournal(fight).Contains("esquive la perte de 1 PM"), "GA;309");
                    int lines = fight.Journal.Count;
                    await Feed(account, "GA;501;42;5,1000"); await Feed(account, "GA;309;42;-7"); Check(fight.Journal.Count == lines, "Animation or malformed GA logged");

                    // Router fallback, external registration, unknown and malformed actions.
                    int routed = 0, tabled = 0;
                    GameActionRouter.Register(4242, context => { routed++; return Task.CompletedTask; });
                    FightActionTable.Register(4243, context => { tabled++; return Task.CompletedTask; });
                    try
                    {
                        await Feed(account, "GA;4242;42;x"); await Feed(account, "GA;4243;42;y");
                        Check(routed == 1 && tabled == 1, "In-fight GA not routed to the combat table then GameActionRouter");
                    }
                    finally { GameActionRouter.Unregister(4242); FightActionTable.Unregister(4243); }
                    FightActionTable.Register(4244, context => { throw new InvalidOperationException("synthetic handler failure"); });
                    try { await Feed(account, "GA;4244;42;z"); }
                    finally { FightActionTable.Unregister(4244); }
                    foreach (string malformed in new[] { "GA;1039;42;foo", "GA;100;42;abc", "GA;100;abc;-7,5", "GA;4;42;-7", "GA;4;42;-7,99999", "GA;50;42;",
                        "GA;51;42;x", "GA;52;42;-8", "GA;11;42;-7,9", "GA;101;42;", "GA;149;42;-7,1", "GA;200;42;-1,3", "GA", "GA;", "GA;x;42;1" })
                        await Feed(account, malformed);
                    Check(fight.IsInFight && fight.Fighters[-7].Life == 55 && fight.Fighters[-7].CellId == 13 && fight.Fighters[-7].Orientation == 3,
                        "Malformed GA changed the state or stopped the fight");

                    // Abandon (GQ) then PvM result.
                    CombatActionResult giveUp = await fight.GiveUpAsync();
                    Check(giveUp.Sent && await Within(Read(peer)) == "GQ" && giveUp.Message.Contains("mort"), "GQ in an active fight");
                    int journalBeforeEnd = fight.Journal.Count;
                    await Feed(account, "GE12000;1|42|0|2;42;Synthetic;10;0;1000;1500;2000;500;0;0;311~2,312~1;150|0;-7;101;5;1;;;;;;;;");
                    // Comme GameManager.terminateFight du client : GE → Game.onLeave → create() ; StarLoco renvoie alors la carte.
                    Check(await Within(Read(peer)) == "GC1", "GE is not followed by GC1");
                    FightResult pvm = fight.LastResult;
                    Check(pvm != null && pvm.DurationMilliseconds == 12000 && pvm.StarBonus == 1 && pvm.InitiatorId == 42 && pvm.FightType == 0
                        && pvm.Winners.Count == 1 && pvm.Losers.Count == 1 && pvm.IsWinner(42) && !pvm.IsWinner(-7), "GE header/categories");
                    FightResultEntry own = pvm.Find(42), monster = pvm.Find(-7);
                    Check(own.WonExperience == 500 && own.Experience == 1500 && own.Items.Count == 2 && own.Items[0].TemplateId == 311 && own.Items[0].Quantity == 2
                        && own.Kamas == 150 && monster.MonsterTemplateId == 101 && monster.IsDead && monster.WonExperience == null, "GE PvM line");
                    Check(fight.Phase == CombatPhase.Finished && !fight.IsInFight && fight.LastActionMessage.Contains("victoire") && results == 1
                        && fight.Journal.Count == journalBeforeEnd && journalBeforeEnd > 20, "GE end state, result event or kept journal");

                    // PvP result (type 1: honour then experience), no star bonus.
                    await Feed(account, "GJK2|1|1|0|30000|1"); Check(fight.LastResult == null && fight.Journal.Count == 0, "New fight keeps the previous result");
                    await Feed(account, "GE5000|42|1|2;42;Synthetic;10;0;0;100;200;10;1;0;0;;0;1000;1500;2000;50");
                    Check(await Within(Read(peer)) == "GC1", "PvP GE is not followed by GC1");
                    FightResult pvp = fight.LastResult; FightResultEntry pvpOwn = pvp?.Find(42);
                    Check(pvp != null && pvp.FightType == 1 && pvp.StarBonus == null && pvpOwn.WonHonour == 10 && pvpOwn.Rank == 1 && pvpOwn.Honour == 100
                        && pvpOwn.WonExperience == 50 && pvpOwn.Items.Count == 0 && pvpOwn.Kamas == 0, "GE PvP line");

                    // A malformed GE still ends the fight (the client always leaves the fight screen).
                    await Feed(account, "GJK2|1|1|0|30000|0"); await Feed(account, "GEabc");
                    Check(!fight.IsInFight && fight.Phase == CombatPhase.Finished && fight.LastResult == null && results == 2, "Malformed GE");
                    Check(await Within(Read(peer)) == "GC1", "Malformed GE is not followed by GC1");
                    await Feed(account, "GE5000|42|1|"); Check(peer.Available == 0, "GE outside a fight sent GC1");

                    // GQ during placement, then GV.
                    await Feed(account, "GJK2|1|1|0|30000|0");
                    CombatActionResult leave = await fight.GiveUpAsync();
                    Check(leave.Sent && await Within(Read(peer)) == "GQ" && leave.Message.Contains("sortie"), "GQ in placement");
                    await Feed(account, "GV"); Check(await Within(Read(peer)) == "GC1" && !fight.IsInFight, "GV after GQ");
                    Check(peer.Available == 0 && journalEvents > 20 && flags == 1, "Stray packet or missing notifications");
                }
            }
        }
        finally { listener.Stop(); }
        Console.WriteLine("OK: GTL/GTR/GTS(2-3)/GTF timeline, GIE/GIe effects and expiry, GDZ, Go/fN/fS/fP/fH, Gf, GQ (give up, kick), " +
            "combat GA table (life, AP/MP, states, invisibility, buffs, carry/throw, summon, GA;999, router fallback), GE PvM/PvP/malformed");
    }
}
