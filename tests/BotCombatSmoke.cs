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
using Tool_BotProtocol.Game.Combats;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Mouvements;
using Tool_BotProtocol.Game.Perso.Spells;
using Tool_BotProtocol.Utils.Crypto;

internal static class BotCombatSmoke
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
    { if (await Task.WhenAny(task, Task.Delay(6000)) != task) throw new TimeoutException("Combat loopback timed out"); return await task; }
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
    private static void AddSpell(short id, SpellStats stats)
    { var spell = new Spell(id, "Sort test " + id); if (stats != null) spell.GetSpellsStats(1, stats); }
    private static async Task Run()
    {
        MessagesReception.Init(); Spell.AllSpells.Clear();
        // Lot X8 : effets de PO du client (caractéristique 19, signe dans « o ») dans un effects.xml synthétique.
        string lang = Path.Combine(TestPaths.Work, "combat-lang");
        Directory.CreateDirectory(lang);
        File.WriteAllText(Path.Combine(lang, "effects.xml"), "<?xml version='1.0' encoding='utf-8'?>\n<BotLang famille=\"effects\" langue=\"fr\" version=\"1\" source=\"effects_fr_1.swf\">\n"
            + "<entree table=\"E\" id=\"116\" c=\"19\" d=\"Portée réduite (test)\" o=\"-\" />\n<entree table=\"E\" id=\"117\" c=\"19\" d=\"Portée accrue (test)\" o=\"+\" />\n"
            + "<entree table=\"E\" id=\"118\" c=\"10\" d=\"Force (test)\" o=\"+\" />\n</BotLang>\n", new UTF8Encoding(false));
        LangData.Clear(); Check(LangData.Load(lang) == 1 && LangData.Effect.Characteristic(117) == 19 && LangData.Effect.Operator(116) == "-", "Synthetic effects.xml not loaded");
        AddSpell(10, new SpellStats { PA = 3, Min_portee = 1, Max_portee = 3, PerTurn = 1 });
        AddSpell(11, new SpellStats { PA = 2, Min_portee = 1, Max_portee = 3, Interval = 2 });
        AddSpell(12, new SpellStats { PA = 1, Min_portee = 0, Max_portee = 3, EmptyCell = true });
        AddSpell(13, new SpellStats { PA = 9, Min_portee = 1, Max_portee = 3 });
        AddSpell(14, new SpellStats { PA = 1, Min_portee = 1, Max_portee = 1 });
        AddSpell(15, new SpellStats { PA = 1, Min_portee = 1, Max_portee = 3, IsInLine = true });
        AddSpell(16, null);
        AddSpell(17, new SpellStats { PA = 1, Min_portee = 1, Max_portee = 1, portee_modifiable = true });
        Map.AllBotMaps[900090] = new Map { MapID = 900090, MapWidth = 3, MapHeight = 4,
            MapData = string.Concat(Enumerable.Repeat("HhGaeaaaaa", 18)) };
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            using (var account = new Accounts(new AccountConfig("synthetic-combat", "synthetic", "loopback")))
            {
                Task<Socket> accept = listener.AcceptSocketAsync();
                await account.Connexion.ConnectToServer(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
                using (Socket peer = await Within(accept))
                {
                    peer.ReceiveTimeout = 6000; account.Game.character.id = 42;
                    foreach (short id in Spell.AllSpells.Keys) account.Game.character.Spells[id] = Spell.ForCharacter(id, 1);
                    await Feed(account, "GDM|900090|date|"); Check(await Within(Read(peer)) == "GI", "GDM did not request GI");
                    await Feed(account, "GM|+0;1;0;42;Synthetic;1;10^100;0");
                    Fights fight = account.Game.Fight;
                    int changed = 0, joins = 0, finished = 0;
                    fight.CombatChanged += () => changed++; fight.CombatReady += () => joins++; fight.CombatFinished += () => finished++;
                    Check(!(await fight.CastSpellAsync(10, 3)).Sent, "A spell can be cast outside combat");
                    await Feed(account, "GJK2|1|1|0|30000|0");
                    await Feed(account, "GP" + Hash.Get_Cell_Char(0) + Hash.Get_Cell_Char(3) + "|" + Hash.Get_Cell_Char(9) + "|0");
                    Check(fight.IsPlacement && account.IsFighting() && fight.PlacementCells.SequenceEqual(new short[] { 0, 3 }), "Placement state/team cells not decoded");
                    Check(fight.TeamPlacementCells(0).SequenceEqual(new short[] { 0, 3 }) && fight.TeamPlacementCells(1).SequenceEqual(new short[] { 9 }),
                        "Both teams' placement cells are needed to draw them in red and blue");
                    Check(!(await fight.PlaceAsync(9)).Sent && peer.Available == 0, "Enemy placement cell is accepted");
                    Check((await fight.PlaceAsync(3)).Sent && await Within(Read(peer)) == "Gp3", "Wrong placement wire");
                    Check(account.Game.character.Cell.CellID == 0, "Placement position predicted before GIC");
                    await Feed(account, "GIC|42;3;1");
                    Check(account.Game.character.Cell.CellID == 3 && !fight.IsActionPending, "GIC did not confirm position");
                    Check((await fight.SetReadyAsync(true)).Sent && await Within(Read(peer)) == "GR1" && !fight.IsReady, "Ready state predicted or wrong wire");
                    await Feed(account, "GR142"); Check(fight.IsReady && !fight.IsActionPending, "GR did not confirm ready");
                    await Feed(account, "GS");
                    Check(fight.TeamPlacementCells(0).Length == 0 && fight.TeamPlacementCells(1).Length == 0, "Placement cells survive the fight start");
                    await Feed(account, "GM|+9;1;0;-7;101;-2;1100^100;1;-1;-1;-1;0,0,0,0;60;4;2;1");
                    Check(fight.Fighters[-7].ActionPoints == 4 && fight.Fighters[-7].Team == 1 && account.Game.Map.Entites.ContainsKey(-7), "Combat GM monster layout ignored");
                    await Feed(account, "GTM|42;0;100;8;3;0;;100|-7;0;60;4;2;9;;60|malformed");
                    await Feed(account, "GTL|42|-7"); await Feed(account, "GTR42");
                    Check(fight.TurnOrder.SequenceEqual(new[] { 42, -7 }) && await Within(Read(peer)) == "GT", "GTL order or GTR acknowledgement missing");
                    await Feed(account, "GTS43|30000");
                    Check(!(await fight.CastSpellAsync(10, 3)).Sent && !(await fight.PassTurnAsync()).Sent, "Another actor's turn allows actions");
                    await Feed(account, "GTF43"); await Feed(account, "GTS42|30000");
                    Check(fight.IsMyTurn && fight.ActionPoints == 8 && fight.MovementPoints == 3 && fight.TurnNumber == 1, "Turn/PA/PM state incorrect");
                    Check(!(await fight.CastSpellAsync(999, 3)).Sent && !(await fight.CastSpellAsync(10, -1)).Sent, "Unknown spell/cell accepted");
                    Check(!(await fight.CastSpellAsync(13, 3)).Sent && !(await fight.CastSpellAsync(14, 9)).Sent, "PA/range gate ignored");
                    Check(!(await fight.CastSpellAsync(15, 1)).Sent && !(await fight.CastSpellAsync(12, 0)).Sent, "Line/empty cell gate ignored");
                    Check(!(await fight.MoveAsync(9)).Sent && !(await fight.MoveAsync(17)).Sent && peer.Available == 0, "Occupied or distant movement accepted");
                    Check((await fight.CastSpellAsync(10, 3)).Sent && await Within(Read(peer)) == "GA30010;3", "Wrong GA300 wire or target cell");
                    Check(fight.ActionPoints == 8 && fight.IsActionPending && !(await fight.CastSpellAsync(10, 3)).Sent, "PA predicted or duplicate cast allowed");
                    await Feed(account, "GAS42"); await Feed(account, "GA;300;42;10,3,0,1"); await Feed(account, "GA;102;42;42,-3");
                    await Feed(account, "GAF0|42"); Check(await Within(Read(peer)) == "GKK0" && fight.IsActionPending, "GAF prematurely released StarLoco casting lock");
                    await Feed(account, "GA;102;42;42,-0");
                    Check(fight.ActionPoints == 5 && fight.Fighters[42].ActionPoints == 5 && !fight.IsActionPending, "Server PA/casting unlock ignored");
                    Check(!(await fight.CastSpellAsync(10, 3)).Sent && fight.GetSpellCastsThisTurn(10) == 1, "Confirmed per-turn limit ignored");
                    Check((await fight.CastSpellAsync(16, 3)).Sent && await Within(Read(peer)) == "GA30016;3", "Missing metadata prevents server validation");
                    await Feed(account, "Im1174"); await Feed(account, "GAF0|42"); Check(await Within(Read(peer)) == "GKK0", "Refusal ACK missing");
                    await Feed(account, "GA;102;42;42,-0");
                    Check(!fight.IsActionPending && fight.LastActionMessage.Contains("ligne de vue") && fight.GetSpellCastsThisTurn(16) == 0, "Refusal loses reason or counts as success");
                    Check((await fight.CastSpellAsync(11, 3)).Sent && await Within(Read(peer)) == "GA30011;3", "Cooldown spell wire incorrect");
                    await Feed(account, "GA;300;42;11,3,0,1"); await Feed(account, "GA;102;42;42,-2"); await Feed(account, "GA;102;42;42,-0");
                    Check(fight.GetSpellCooldownRemaining(11) == 2 && !(await fight.CastSpellAsync(11, 3)).Sent, "Confirmed cooldown ignored");
                    // Lot X8 : portée du client depuis la cellule 0 (GTM). Sort 17 modifiable 1–1 sans bonus : 1 (checkRange : max(min, max + bonus) ;
                    // StarLoco accepterait 2, min + 1). GA;117 +2 → 3, GA;116 −3 → max(1, 0) = 1 ; le sort 14 non modifiable reste à 1.
                    SpellAim aim = fight.GetSpellAim(17);
                    Check(account.Game.character.Cell.CellID == 0 && aim != null && aim.Boostable && aim.MinRange == 1 && aim.MaxRange == 1 && fight.RangeBonus == 0
                        && fight.GetSpellUnavailableReason(17, 3) == null && fight.GetSpellUnavailableReason(17, 6) == "Cible hors portée (1–1).",
                        "Boostable 1-1 spell without bonus is not limited to 1 like the client: " + fight.GetSpellUnavailableReason(17, 6));
                    await Feed(account, "GA;117;42;42,abc,1"); await Feed(account, "GA;117;42");
                    Check(fight.RangeBonus == 0 && fight.IsInFight, "Malformed GA;117 changed the range bonus or broke the fight");
                    await Feed(account, "GA;118;42;42,2,1"); Check(fight.RangeBonus == 0, "A strength effect (characteristic 10) counted as range");
                    await Feed(account, "GA;117;42;42,2,1");
                    aim = fight.GetSpellAim(17);
                    Check(fight.RangeBonus == 2 && aim.RangeBonus == 2 && aim.MaxRange == 3 && fight.GetSpellUnavailableReason(17, 9) == null
                        && fight.GetSpellUnavailableReason(17, 2) == "Cible hors portée (1–3)." && fight.GetSpellAim(14).MaxRange == 1
                        && fight.GetSpellUnavailableReason(14, 6) == "Cible hors portée (1–1).", "GA;117 range bonus not applied like checkRange");
                    Cell from = account.Game.character.Cell; Cell[] all = account.Game.Map.MapCells;
                    Check(aim.RangeCells(from, all).OrderBy(c => c).SequenceEqual(new short[] { 1, 3, 4, 5, 6, 8, 9 }),
                        "Range zone \"C\" (1 to 3 with bonus) differs: " + string.Join(",", aim.RangeCells(from, all)));
                    Check(fight.GetSpellAim(15).RangeCells(from, all).OrderBy(c => c).SequenceEqual(new short[] { 3, 6, 9 }),
                        "Line spell range zone \"X\" is not aligned");
                    await Feed(account, "GA;116;42;42,3,1");
                    Check(fight.RangeBonus == -1 && fight.GetSpellAim(17).MaxRange == 1 && fight.GetSpellUnavailableReason(17, 6) == "Cible hors portée (1–1).",
                        "GA;116 range malus not subtracted or range fell below the minimum");
                    Check((await fight.PassTurnAsync()).Sent && await Within(Read(peer)) == "Gt", "Pass turn wire incorrect");
                    await Feed(account, "GTF42"); Check(!fight.IsMyTurn && !(await fight.CastSpellAsync(12, 3)).Sent, "GTF leaves actions available");
                    await Feed(account, "GTM|42;0;100;8;3;0;;100"); await Feed(account, "GTS43|30000"); await Feed(account, "GTF43"); await Feed(account, "GTS42|30000");
                    Check(fight.ActionPoints == 8 && fight.MovementPoints == 3 && fight.GetSpellCooldownRemaining(11) == 1 && fight.GetSpellUnavailableReason(10, 3) == null,
                        "Next turn uses exhausted PA/PM or fails to clear per-turn history");
                    Check(fight.RangeBonus == 0 && fight.GetSpellAim(17).MaxRange == 1, "One-turn range effects did not end on the bearer's next turn");
                    Check((await fight.MoveAsync(3)).Sent && await Within(Read(peer)) == "GA001bad" && fight.MovementPoints == 3, "Movement wire or predicted PM incorrect");
                    await Feed(account, "GAS42"); await Feed(account, "GA301;1;42;aaabad");
                    Check(await Within(Read(peer)) == "GKK301" && account.Game.character.Cell.CellID == 3 && account.IsFighting(), "Combat move ACK/cell/account state incorrect");
                    await Feed(account, "GA;129;42;42,-1"); await Feed(account, "GAF2|42"); Check(await Within(Read(peer)) == "GKK2", "Combat movement finish ACK missing");
                    Check(fight.MovementPoints == 2 && fight.Fighters[42].MovementPoints == 2 && !fight.IsActionPending, "Confirmed movement points ignored");
                    await Feed(account, "GA;4;42;42,0"); Check(account.Game.character.Cell.CellID == 0, "Combat relocation loses cell zero");
                    await Feed(account, "GA;100;42;-7,-20"); Check(fight.Fighters[-7].Life == 40, "Damage is not reflected");
                    await Feed(account, "GA;103;42;-7"); Check(fight.Fighters[-7].IsDead && account.Game.Map.Entites[-7].Cell == null, "Dead combatant still occupies a cell");
                    await Feed(account, "GTM|42;0;100;8;3;-1;;100"); Check(account.Game.character.Cell == null && !string.IsNullOrEmpty(fight.GetSpellUnavailableReason(10, 3)), "Hidden position kept stale cell");
                    await Feed(account, "GIC|42;0;1");
                    List<Cell> expanded = PathfinderUtils.DecodeServerPath(account.Game.Map, "aaa" + "bag");
                    Check(expanded.Count == 3 && expanded[1].CellID == 3 && expanded[2].CellID == 6 && PathfinderUtils.DecodeServerPath(account.Game.Map, "aaazad") == null,
                        "Compressed server movement is not expanded/validated");
                    Task oldMovement = Feed(account, "GA302;1;42;aaabad");
                    await Feed(account, "GDM|900090|new-date|"); Check(await Within(Read(peer)) == "GI", "Map changed without requesting GI");
                    await oldMovement; Check(peer.Available == 0 && fight.IsInFight && account.IsFighting(), "Old combat map movement sends a stale ACK or clears fight");
                    await Feed(account, "GE0|0");
                    Check(await Within(Read(peer)) == "GC1", "GE is not followed by GC1 (client terminateFight → Game.create)");
                    Check(fight.Phase == CombatPhase.Finished && !fight.IsInFight && account.AccountStates == AccountStates.CONNECTED_INACTIVE && fight.Fighters.Count == 0
                        && fight.LastResult != null && fight.LastResult.DurationMilliseconds == 0 && fight.TurnOrder.Count == 0, "Fight end retains stale state or loses GE");
                    await Feed(account, "GJK3|0|0|1|0|4"); await Feed(account, "GTS42|30000");
                    Check(fight.IsSpectator && !fight.IsMyTurn && !(await fight.CastSpellAsync(10, 3)).Sent, "Spectator can cast");
                    await Feed(account, "GV"); Check(await Within(Read(peer)) == "GC1" && !fight.IsInFight, "Leaving spectator mode retains fight state");
                    account.Game.Clear(); Check(fight.Phase == CombatPhase.None && !fight.IsActionPending, "Reconnect retains combat state");
                    Check(changed > 15 && joins == 2 && finished == 2 && peer.Available == 0, "Lifecycle notifications missing or rejected actions leaked packets");
                }
            }
        }
        finally { listener.Stop(); }
        LangData.Clear();
        Console.WriteLine("OK: StarLoco placement/ready/turns, spell wire and targeting, client range with GA;117/116 bonus (lot X8), server PA/PM/refusal/ACK, confirmed limits, fight movement, spectator/end/reset");
    }
}
