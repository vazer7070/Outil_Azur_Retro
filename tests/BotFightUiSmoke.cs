using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot;
using Outil_Azur_complet.Bot.Controls;
using Outil_Azur_complet.Bot.Controls.Fight;
using Outil_Azur_complet.Bot.Interfaces;
using Outil_Azur_complet.Bot.Panels;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Combats;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Perso.Spells;
using Tool_BotProtocol.Utils.Crypto;

// Interface de combat (lot F12b) dans une fenêtre de jeu hors de l'écran, contre un serveur fictif local et une carte
// synthétique, sans capture de fenêtre : menu de placement (GJK champs 2 et 3, Prêt → GR1, Annuler → GQ), options d'équipe
// (fN / fH / fS émis, état suivi par Go±, fP masqué sans groupe, boutons réduits après GS), drapeau (Gf43|7 dessiné, bouton
// puis clic → Gf5, Échap), ligne de temps (GTL → 3 portraits, GTR → GT et cadre, GTS → tour et chrono, GTF, mort retirée,
// GTL mal formé ignoré, dessin hors écran), « Abandonner » (boîte Non puis Oui → GQ, spectateur → GQ direct), GV, résultat
// GE (PvM avec bonus d'étoiles et objets, PvP avec les colonnes d'honneur, GE illisible sans volet).
internal static class BotFightUiSmoke
{
    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            string path = Path.Combine(TestPaths.ApplicationBin, new AssemblyName(e.Name).Name + ".dll");
            if (!File.Exists(path)) path = Path.ChangeExtension(path, "exe");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        var uiErrors = new List<Exception>();
        Application.ThreadException += (s, e) => uiErrors.Add(e.Exception);
        try
        {
            Application.EnableVisualStyles(); Run();
            Check(uiErrors.Count == 0, "Interface errors: " + string.Join(" / ", uiErrors.Select(e => e.GetType().Name + " " + e.Message)));
            Console.WriteLine("OK: menu de placement GR1/GQ, options fN/fH/fS et Go±, drapeau Gf, ligne de temps GTL/GTR/GTS/GTF, abandon confirmé → GQ, spectateur, résultat GE PvM/PvP");
        }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static object Get(object target, string field) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static void PumpUntil(Func<bool> done, string what, int seconds = 6)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(seconds);
        while (!done()) { if (DateTime.UtcNow > end) throw new TimeoutException("Fight UI loopback timed out: " + what); Application.DoEvents(); Thread.Sleep(5); }
        Application.DoEvents();
    }
    private static void Complete(Task task) { PumpUntil(() => task.IsCompleted, "task"); task.GetAwaiter().GetResult(); }
    private static void Feed(Accounts account, string packet) { Complete(MessagesReception.ReceptionAsync(account.Connexion, packet)); }
    private static string Read(Socket socket)
    {
        PumpUntil(() => socket.Available > 0, "packet");
        using (var stream = new MemoryStream())
        {
            byte[] one = new byte[1];
            while (true) { if (socket.Receive(one) != 1) throw new IOException("Loopback disconnected"); if (one[0] == 0) return Encoding.UTF8.GetString(stream.ToArray()).TrimEnd('\r', '\n'); stream.WriteByte(one[0]); }
        }
    }
    private static void Expect(Socket socket, string packet, string message)
    {
        string read = Read(socket);
        Check(read == packet, message + " (reçu « " + read + " »)");
    }
    private static void NoPacket(Socket socket, string message)
    {
        DateTime end = DateTime.UtcNow.AddMilliseconds(250);
        while (DateTime.UtcNow < end) { Application.DoEvents(); Thread.Sleep(5); }
        Check(socket.Available == 0, message);
    }
    private static IEnumerable<Control> All(Control value) { yield return value; foreach (Control child in value.Controls) foreach (var nested in All(child)) yield return nested; }
    private static void Answer(Form dialog, string button) => All(dialog).OfType<Button>().Single(b => b.Text == button).PerformClick();
    private static void ClickCell(UserMapControl map, short id)
    {
        var point = map.Cells[id].Centre;
        Check(map.GetCell(point) != null && map.GetCell(point).id == id, "Mouse projection points at another cell");
        var e = new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0);
        typeof(UserMapControl).GetMethod("OnMouseDown", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(map, new object[] { e });
        typeof(UserMapControl).GetMethod("OnMouseUp", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(map, new object[] { e });
        Application.DoEvents();
    }
    private static string Cell(this ListViewItem item, int column) => item.SubItems[column].Text;

    private static void Run()
    {
        string previous = Environment.CurrentDirectory;
        string folder = Path.Combine(TestPaths.Work, "fight-ui"); Directory.CreateDirectory(folder); Environment.CurrentDirectory = folder;
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            MessagesReception.Init(); Spell.AllSpells.Clear();
            var spell = new Spell(10, "Sort test 10"); spell.GetSpellsStats(1, new SpellStats { PA = 3, Min_portee = 1, Max_portee = 6 });
            Map.AllBotMaps[900094] = new Map { MapID = 900094, MapWidth = 3, MapHeight = 4, MapData = string.Concat(Enumerable.Repeat("HhGaeaaaaa", 18)) };
            using (var account = new Accounts(new AccountConfig("synthetic-fight-ui", "synthetic", "loopback")))
            {
                Task<Socket> accept = listener.AcceptSocketAsync();
                Complete(account.Connexion.ConnectToServer(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port)); Complete(accept);
                using (Socket peer = accept.Result)
                {
                    peer.ReceiveTimeout = 6000;
                    account.Game.character.SetPerso_Data(42, "Personnage de test", 25, 0, 8);
                    account.Game.Map.SetRefreshMap("900094|date|"); account.Game.character.Cell = account.Game.Map.MapCells[0];
                    account.Game.character.Spells[10] = Spell.ForCharacter(10, 1);
                    account.AccountStates = AccountStates.CONNECTED_INACTIVE;
                    using (var form = new GameClientFullform(account))
                    {
                        form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); Application.DoEvents();
                        Fights fight = account.Game.Fight;
                        TimelineControl timeline = form.Timeline; FightOptionButtons options = form.FightOptions; ChallengeMenu menu = form.PlacementMenu;
                        var giveUp = (Button)Get(form, "giveUp"); var combatTools = (Control)Get(form, "combatTools");
                        var map = (UserMapControl)Get(form.Map, "UserMap");
                        Check(timeline != null && options != null && menu != null && !timeline.Visible && !options.Visible && !menu.Visible && !combatTools.Visible,
                            "Fight controls are shown outside a fight");
                        Check(form.Panels.Get<FightResultPanel>() != null && !form.Panels.Visible, "Result panel is not registered or the drawer is open");

                        // ---------------------------------------------------------------- placement : menu, options, drapeau
                        Feed(account, "GJK2|1|1|0|30000|0");
                        Feed(account, "GP" + Hash.Get_Cell_Char(0) + Hash.Get_Cell_Char(3) + "|" + Hash.Get_Cell_Char(9) + "|0");
                        Feed(account, "GM|+0;1;0;42;Personnage de test;1;10^100;0|+3;1;0;43;Allié de test;1;10^100;0"); Feed(account, "GIC|42;3;1");
                        PumpUntil(() => menu.Visible && options.Visible && combatTools.Visible, "placement controls");
                        Check(fight.CanCancel && fight.HasChallengeMenu && fight.FightType == 0, "GJK fields 2, 3 and 6 were not read");
                        Check(menu.CancelButton.Visible && menu.ReadyButton.Text == "Prêt" && menu.ReadyButton.Enabled, "Placement menu does not show Prêt and Annuler");
                        Check(giveUp.Visible && giveUp.Enabled && giveUp.Text == "Abandonner", "Give-up button missing during placement");
                        Check(options.ButtonFor(FightOptions.BlockJoiner).Visible && options.ButtonFor(FightOptions.NeedHelp).Visible && options.ButtonFor(FightOptions.BlockSpectators).Visible
                            && !options.ButtonFor(FightOptions.PartyOnly).Visible && options.FlagButton.Visible && !options.TacticButton.Visible,
                            "Placement options differ from the client (fN, fH, fS, flag; fP hidden without party; no tactic mode)");
                        Check(options.TooltipOf(options.ButtonFor(FightOptions.BlockJoiner)).Contains("rejoindre"), "Option tooltip missing");
                        options.ButtonFor(FightOptions.BlockJoiner).PerformClick(); Expect(peer, "fN", "Block joiner button does not send fN");
                        Check(!options.ButtonFor(FightOptions.BlockJoiner).Checked, "Option state changed before the server answered");
                        Feed(account, "Go+A42"); PumpUntil(() => options.ButtonFor(FightOptions.BlockJoiner).Checked, "Go+A42 checked");
                        Feed(account, "Go-A42"); PumpUntil(() => !options.ButtonFor(FightOptions.BlockJoiner).Checked, "Go-A42 unchecked");
                        options.ButtonFor(FightOptions.NeedHelp).PerformClick(); Expect(peer, "fH", "Help button does not send fH");
                        options.ButtonFor(FightOptions.BlockSpectators).PerformClick(); Expect(peer, "fS", "Spectators button does not send fS");
                        Feed(account, "Go+S42"); PumpUntil(() => options.ButtonFor(FightOptions.BlockSpectators).Checked, "Go+S42 checked");

                        menu.ReadyButton.PerformClick(); Expect(peer, "GR1", "Placement menu Prêt does not send GR1");
                        Check(!fight.IsReady, "Ready state predicted locally");
                        Feed(account, "GR142"); PumpUntil(() => menu.ReadyButton.Text == "Annuler", "ready tick");
                        menu.ReadyButton.PerformClick(); Expect(peer, "GR0", "Second click does not send GR0");
                        Feed(account, "GR042"); PumpUntil(() => menu.ReadyButton.Text == "Prêt", "ready reset");

                        Feed(account, "Gf43|7"); PumpUntil(() => form.Map.DisplayedFlag != null && form.Map.DisplayedFlag.CellId == 7 && form.Map.DisplayedFlag.ActorId == 43, "Gf43|7 flag");
                        Feed(account, "Gf43"); Feed(account, "Gfx|y"); Application.DoEvents();
                        Check(form.Map.DisplayedFlag.CellId == 7, "Malformed Gf replaced the flag");
                        options.FlagButton.PerformClick(); Check(form.Map.FlagMode, "Flag button did not arm the flag mode");
                        ClickCell(map, 5); Expect(peer, "Gf5", "Click in flag mode does not send Gf<cell>");
                        Check(!form.Map.FlagMode && fight.IsPlacement, "Flag mode stays armed after the click");
                        options.FlagButton.PerformClick(); Check(form.Map.FlagMode, "Flag mode not re-armed");
                        Check(form.ProcessShortcut(Keys.Escape) && !form.Map.FlagMode, "Escape does not cancel the flag mode");
                        NoPacket(peer, "Escape in flag mode sent a packet");

                        // ---------------------------------------------------------------- combat lancé : ligne de temps
                        Feed(account, "GS");
                        Feed(account, "GM|+9;1;0;-7;101;-2;1100^100;1;-1;-1;-1;0,0,0,0;60;4;2;1");
                        Feed(account, "GTM|42;0;100;8;3;3;;100|-7;0;60;4;2;9;;60|43;0;50;6;3;6;;50");
                        PumpUntil(() => !menu.Visible && !options.ButtonFor(FightOptions.BlockJoiner).Visible, "options after GS");
                        Check(!options.ButtonFor(FightOptions.NeedHelp).Visible && options.ButtonFor(FightOptions.BlockSpectators).Visible && options.FlagButton.Visible
                            && options.TacticButton.Visible && !options.TacticButton.Enabled, "Running fight keeps only spectators, flag and (disabled) tactic mode");
                        Check(!timeline.Visible && timeline.Entries.Count == 0, "Timeline shown before GTL");
                        Feed(account, "GTL|42|43|-7");
                        PumpUntil(() => timeline.Visible && timeline.Entries.Count == 3, "GTL portraits");
                        IReadOnlyList<TimelineEntry> entries = timeline.Entries;
                        Check(entries[0].FighterId == 42 && entries[1].FighterId == 43 && entries[2].FighterId == -7, "Timeline order differs from GTL");
                        Check(entries[0].Name == "Personnage de test" && entries[1].Name == "Allié de test" && entries[2].Life == 60 && entries[2].Team == 1,
                            "Timeline entries do not carry the fighters' data: " + string.Join(" / ", entries.Select(entry => entry.Name + " " + entry.Life + " " + entry.Team)));
                        Check(entries[0].Bounds.X < entries[1].Bounds.X && entries[1].Bounds.X < entries[2].Bounds.X && entries[1].Bounds.X - entries[0].Bounds.X == TimelineControl.ItemWidth,
                            "Portraits are not laid out at ITEM_WIDTH");
                        Check(timeline.Width >= 3 * TimelineControl.ItemWidth && timeline.Top >= 0 && timeline.Left > 0, "Timeline is not centred on top of the map");
                        Check(timeline.CurrentFighterId == 0 && timeline.HighlightedFighterId == 0 && entries.All(entry => !entry.IsCurrent && !entry.IsHighlighted), "Highlight without GTR or GTS");
                        Feed(account, "GTR43"); Expect(peer, "GT", "GTR43 not answered by GT");
                        PumpUntil(() => timeline.HighlightedFighterId == 43, "GTR highlight");
                        entries = timeline.Entries;
                        Check(entries[1].IsHighlighted && !entries[1].IsCurrent && !entries[0].IsHighlighted, "GTR did not frame the announced fighter");
                        Feed(account, "GTS43|30000");
                        PumpUntil(() => timeline.CurrentFighterId == 43, "GTS current");
                        entries = timeline.Entries;
                        Check(entries[1].IsCurrent && entries[1].IsHighlighted && timeline.RemainingMilliseconds > 20000 && timeline.RemainingMilliseconds <= 30000,
                            "GTS43|30000 (two fields) did not start the chrono");
                        Check(timeline.Describe(entries[1]).Contains("tour en cours") && timeline.Describe(entries[2]).Contains("60 PV") && timeline.Describe(entries[2]).Contains("4 PA"),
                            "Portrait tooltip lacks the turn or the points");
                        Check(timeline.EntryAt(new Point(entries[1].Bounds.X + 5, entries[1].Bounds.Y + 5)) == entries[1], "EntryAt does not find the portrait");
                        using (var image = new Bitmap(timeline.Width, timeline.Height))
                        {
                            timeline.DrawToBitmap(image, new Rectangle(0, 0, image.Width, image.Height));
                            Color frame = image.GetPixel(0, 0);
                            Check(Math.Abs(frame.R - 180) <= 16 && Math.Abs(frame.G - 172) <= 16 && Math.Abs(frame.B - 141) <= 16, "Timeline frame is not the gold rule: " + frame);
                        }
                        Feed(account, "GTF43"); PumpUntil(() => timeline.CurrentFighterId == 0, "GTF");
                        Check(timeline.RemainingMilliseconds == 0 && timeline.HighlightedFighterId == 43, "GTF kept the chrono or lost the GTR frame");
                        Feed(account, "GTS42|30000"); PumpUntil(() => timeline.CurrentFighterId == 42, "own turn");
                        Feed(account, "GA;103;42;-7"); PumpUntil(() => timeline.Entries.Count == 2, "dead fighter removed");
                        Feed(account, "GTL|x"); Feed(account, "GTL|42|zz"); Application.DoEvents();
                        Check(timeline.Entries.Count == 2 && timeline.Visible, "Malformed GTL changed the timeline");

                        // ---------------------------------------------------------------- abandon : boîte puis GQ
                        giveUp.PerformClick(); PumpUntil(() => BotDialogs.OpenDialogs.Count == 1, "give-up dialog");
                        Form ask = BotDialogs.OpenDialogs[0];
                        Check(All(ask).OfType<Label>().Any(label => label.Text.Contains("abandonner") && label.Text.Contains("mort")), "Give-up question lacks the client text or the death warning");
                        Answer(ask, "Non"); PumpUntil(() => BotDialogs.OpenDialogs.Count == 0, "dialog closed");
                        NoPacket(peer, "GQ sent after answering Non");
                        Check(fight.IsInFight, "Fight state changed locally after Non");
                        giveUp.PerformClick(); PumpUntil(() => BotDialogs.OpenDialogs.Count == 1, "second give-up dialog");
                        Answer(BotDialogs.OpenDialogs[0], "Oui"); Expect(peer, "GQ", "Give-up confirmation does not send GQ");
                        Check(fight.IsInFight, "Fight ended locally before GV");
                        Feed(account, "GV"); Expect(peer, "GC1", "GV is not answered by GC1");
                        PumpUntil(() => !combatTools.Visible && !timeline.Visible && !options.Visible && !fight.IsInFight, "GV hides the fight controls");
                        Check(!(form.Panels.Current is FightResultPanel), "Result panel opened on GV");

                        // ---------------------------------------------------------------- résultat PvM : GE avec bonus d'étoiles
                        Feed(account, "GJK2|0|1|0|30000|4"); Feed(account, "GIC|42;3;1");
                        PumpUntil(() => menu.Visible, "second placement");
                        Check(!menu.CancelButton.Visible && menu.Width < 200, "Cancel button shown although the second GJK field is 0");
                        Feed(account, "GS"); Feed(account, "GTS42|30000"); Feed(account, "GTF42"); Feed(account, "GTS42|30000"); Feed(account, "GTF42");
                        // Drapeau armé quand le combat se termine : le mode est désarmé, le clic suivant n'est pas avalé.
                        PumpUntil(() => options.FlagButton.Visible && options.FlagButton.Enabled, "flag button after GS");
                        options.FlagButton.PerformClick(); Check(form.Map.FlagMode, "Flag mode not armed before GE");
                        Feed(account, "GE120000;15|42|0|2;42;Personnage de test;25;0;100;150;200;320;0;0;311~2,312~1;40|0;-7;101;4;1;;;;;;;;|zz;1");
                        Expect(peer, "GC1", "GE is not followed by GC1");
                        PumpUntil(() => form.Panels.Current is FightResultPanel, "result panel");
                        var result = (FightResultPanel)form.Panels.Current;
                        Check(result.Result != null && result.Result.StarBonus == 15 && result.Result.Rejected.Count == 1, "Result not handed to the panel");
                        Check(result.HeadingText.Contains("Résultat du combat") && result.HeadingText.EndsWith(" : 2"), "Heading lacks the turn count: " + result.HeadingText);
                        Check(result.DurationText.Contains("2 min 00 s") && result.BonusText.Contains("15"), "Duration or star bonus differs: " + result.DurationText + " / " + result.BonusText);
                        Check(result.Winners.Items.Count == 1 && result.Losers.Items.Count == 1 && !result.Collectors.Visible, "Winners / losers rows differ");
                        ListViewItem winner = result.Winners.Items[0], loser = result.Losers.Items[0];
                        Check(result.Winners.Columns.Count == 7 && result.Winners.Columns[6].Text == "Objets gagnés", "PvM columns differ from GameResultTeam");
                        Check(winner.Text == "Personnage de test" && winner.Cell(1) == "25" && winner.Cell(2) == "40" && winner.Cell(3) == "320" && winner.Cell(4) == "" && winner.Cell(6) == "objet #311 ×2, objet #312",
                            "Winner row differs: " + string.Join("|", winner.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(cell => cell.Text)));
                        Check(loser.Text.EndsWith("Monstre #101") && ((FightResultEntry)loser.Tag).IsDead && loser.Cell(1) == "4", "Loser row differs: " + loser.Text);
                        Check(!combatTools.Visible && !fight.IsInFight, "Fight controls stay after GE");
                        PumpUntil(() => !form.Map.FlagMode, "flag mode disarmed after GE");
                        Check(map.Cursor == Cursors.Default, "Flag cursor stays after the end of the fight");
                        ((Button)result.CloseButton).PerformClick(); PumpUntil(() => !form.Panels.IsOpen(result), "result closed");

                        // ---------------------------------------------------------------- résultat PvP : colonnes d'honneur
                        Feed(account, "GJK2|0|1|0|30000|1"); Feed(account, "GS");
                        Feed(account, "GE5000|42|1|2;42;Personnage de test;25;0;0;120;500;20;3;0;0;;10;100;150;200;80|0;43;Allié de test;30;1;0;50;500;-20;2;5;5;;0;0;0;0;0");
                        Expect(peer, "GC1", "PvP GE is not followed by GC1");
                        PumpUntil(() => form.Panels.Current is FightResultPanel, "pvp result panel");
                        result = (FightResultPanel)form.Panels.Current; winner = result.Winners.Items[0]; loser = result.Losers.Items[0];
                        Check(result.Winners.Columns.Count == 8 && result.Winners.Columns[3].Text == "Points d'honneur" && result.Winners.Columns[6].Text == "Points de déshonneur",
                            "PvP columns differ from GameResultTeamPVP");
                        Check(winner.Cell(2) == "10" && winner.Cell(3) == "+20 (120)" && winner.Cell(4) == "3" && winner.Cell(7) == "80", "PvP winner row differs: " + winner.Cell(3));
                        Check(loser.Cell(3) == "-20 (50)" && loser.Cell(6) == "+5 (5)" && result.HeadingText.EndsWith("Résultat du combat") && result.BonusText == "",
                            "PvP loser row or heading differs: " + loser.Cell(3) + " / " + result.HeadingText);
                        ((Button)result.CloseButton).PerformClick(); PumpUntil(() => !form.Panels.IsOpen(result), "pvp result closed");

                        // ---------------------------------------------------------------- spectateur : GQ sans question
                        Feed(account, "GJK3|0|1|1|30000|4");
                        PumpUntil(() => combatTools.Visible && giveUp.Text == "Quitter", "spectator button");
                        Check(!options.Visible && !menu.Visible && !((Control)Get(form, "ready")).Visible && !((Control)Get(form, "passTurn")).Visible, "Spectator sees fighter controls");
                        giveUp.PerformClick(); Expect(peer, "GQ", "Spectator leave does not send GQ");
                        Check(BotDialogs.OpenDialogs.Count == 0, "Spectator leave asked a confirmation");
                        Feed(account, "GV"); Expect(peer, "GC1", "Spectator GV is not answered by GC1"); PumpUntil(() => !combatTools.Visible, "spectator GV");

                        // ---------------------------------------------------------------- GE illisible : fin sans volet
                        Feed(account, "GJK2|0|1|0|30000|0"); Feed(account, "GS"); Feed(account, "GEabc|1|0"); Application.DoEvents();
                        Expect(peer, "GC1", "Unreadable GE is not followed by GC1");
                        Check(!fight.IsInFight && !(form.Panels.Current is FightResultPanel), "Unreadable GE opened the result panel or kept the fight");
                        form.Close();
                        Check(BotDialogs.OpenDialogs.Count == 0, "Dialogs stayed open after the window closed");
                    }
                }
            }
        }
        finally { listener.Stop(); Environment.CurrentDirectory = previous; }
    }
}
