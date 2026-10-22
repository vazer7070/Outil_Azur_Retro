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
using Outil_Azur_complet.Bot.Controls.Banner;
using Outil_Azur_complet.Bot.Controls.Fight;
using Outil_Azur_complet.Bot.Interfaces;
using Outil_Azur_complet.Bot.Panels;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Chat;
using Tool_BotProtocol.Game.Combats;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Perso.Spells;
using Tool_BotProtocol.Utils.Crypto;

// Interface de combat (lot F12b) dans une fenêtre de jeu hors de l'écran, contre un serveur fictif local et une carte
// synthétique, sans capture de fenêtre : menu de placement (GJK champs 2 et 3, Prêt → GR1, Annuler → GQ), options d'équipe
// (fN / fH / fS émis, état suivi par Go±, fP masqué sans groupe, boutons réduits après GS), drapeau (Gf43|7 dessiné, bouton
// puis clic → Gf5, Échap), ligne de temps (GTL → 3 portraits, GTR → GT et cadre, GTS → tour et chrono, GTF, mort retirée,
// GTL mal formé ignoré, dessin hors écran), « Abandonner » (boîte Non puis Oui → GQ, spectateur → GQ direct), GV, résultat
// GE (PvM avec bonus d'étoiles et objets, PvP avec les colonnes d'honneur, GE illisible sans volet). Lot X1 : case ² du corps à corps
// (poing DefaultCC, visée, cellules à portée, GA303 au clic, Échap, SH0) et clic sur un ennemi adjacent sans sort choisi → GA303.
// Lot X9 : défis (Gd → colonne d'icônes du client avec infobulles, détail au clic, « voir la cible » → Gdi3 limité à une demande
// par seconde, bouton affiche / cache, GdOK / GdKO → symboles d'état et lignes du chat), puis « Bonus de challenges » du résultat GE.
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
            Console.WriteLine("OK: menu de placement GR1/GQ, clip « prêt » et icônes d'options par combattant, zones GDZ, invisibilité GA;150 (lot X7), options fN/fH/fS et Go±, drapeau Gf, ligne de temps GTL/GTR/GTS/GTF, corps à corps (case ², SH0, clic sur un ennemi adjacent) → GA303, abandon confirmé → GQ, spectateur, résultat GE PvM/PvP, bouton du mode tactique local (lot X33), défis Gd/GdOK/GdKO (colonne, détail, Gdi, chat, bonus du résultat ; lot X9)");
        }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static object Get(object target, string field) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    /// <summary>Propriété d'une case de la barre (<c>SpellShortcutButton</c> est interne à l'application).</summary>
    private static bool Flag(Button slot, string property) => (bool)slot.GetType().GetProperty(property).GetValue(slot);
    private static string Badge(Button slot) => (string)slot.GetType().GetProperty("Badge").GetValue(slot);
    private static void PumpUntil(Func<bool> done, string what, int seconds = 6)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(seconds);
        while (!done()) { if (DateTime.UtcNow > end) throw new TimeoutException("Fight UI loopback timed out: " + what); Application.DoEvents(); Thread.Sleep(5); }
        Application.DoEvents();
    }
    private static void Click(Control control) => typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(control, new object[] { EventArgs.Empty });
    private static void Complete(Task task) { PumpUntil(() => task.IsCompleted, "task"); task.GetAwaiter().GetResult(); }
    private static void Feed(Accounts account, string packet) { Complete(MessagesReception.ReceptionAsync(account.Connexion, packet)); }
    /// <summary>Paquet traité sur un autre fil, comme ceux du serveur : les contrôles créés masqués doivent quand même se mettre à jour.</summary>
    private static void FeedFromNetwork(Accounts account, string packet) { Complete(Task.Run(() => MessagesReception.ReceptionAsync(account.Connexion, packet))); }
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
    /// <summary>La souris passe au centre d'une cellule (lot X8 : pointeur, chemin de combat, croix d'interdiction).</summary>
    private static void Hover(UserMapControl map, short id)
    {
        var point = map.Cells[id].Centre;
        typeof(UserMapControl).GetMethod("OnMouseMove", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(map, new object[] { new MouseEventArgs(MouseButtons.None, 0, point.X, point.Y, 0) });
        Application.DoEvents();
    }
    private static string Cell(this ListViewItem item, int column) => item.SubItems[column].Text;
    /// <summary>Pixel du centre d'une cellule après un dessin complet de la vue hors écran (lot X7 : zones GDZ).</summary>
    private static Color PixelOfCell(UserMapControl map, int cellId)
    {
        using (var image = new Bitmap(Math.Max(1, map.Width), Math.Max(1, map.Height)))
        using (Graphics graphics = Graphics.FromImage(image))
        {
            map.DrawCells(graphics);
            Point centre = map.Cells[cellId].Centre;
            return image.GetPixel(centre.X, centre.Y);
        }
    }

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
                    // Lot X12 (V-26) : 3 PA et 2 tours de relance, pour voiler la case faute de PA ou pendant le délai.
                    account.Game.character.Spells[10].Stats[1] = new SpellStats { PA = 3, Interval = 2, Max_portee = 1 };
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

                        // Lot X7 : hors combat, Go accroche ses icônes à l'épée de l'équipe (onFightOption : sprite qui porte l'identifiant de
                        // l'équipe) ; StarLoco ne l'envoie qu'à la carte d'origine. Gc- retire l'épée et ses options, Gc+ recrée l'épée sans option.
                        Feed(account, "Gc+77;0|77;5;0;-1|78;6;0;-1"); Feed(account, "Go+H77");
                        PumpUntil(() => map.OptionIconsOf(77).Length > 0, "Go+H77 icon above the team sword");
                        Check(map.OptionIconsOf(77).SequenceEqual(new[] { "UI/Client/UI_FightOptionNeedHelpUp.png" }) && map.OptionIconsOf(78).Length == 0 && map.OptionIconsOf(79).Length == 0,
                            "Go+H77 outside a fight: " + string.Join(",", map.OptionIconsOf(77)) + " / " + string.Join(",", map.OptionIconsOf(78)));
                        PixelOfCell(map, 5); // DrawCells → DrawOverheads dessine les icônes au-dessus de l'épée
                        Feed(account, "Gc-77"); PumpUntil(() => map.OptionIconsOf(77).Length == 0, "Gc-77 removes the sword icons");
                        Check(fight.GetTeamOptions(77) == FightOptions.None, "Gc- kept the options of the ended fight");
                        Feed(account, "Go+A77"); Feed(account, "Gc+77;0|77;5;0;-1|78;6;0;-1");
                        PumpUntil(() => account.Game.Map.FightSwords.ContainsKey(77), "Gc+77 again");
                        Check(map.OptionIconsOf(77).Length == 0 && fight.GetTeamOptions(77) == FightOptions.None, "Gc+ kept options sent before the swords");
                        Feed(account, "Gc-77"); PumpUntil(() => !account.Game.Map.FightSwords.ContainsKey(77), "Gc-77 again");

                        // ---------------------------------------------------------------- placement : menu, options, drapeau
                        FeedFromNetwork(account, "GJK2|1|1|0|30000|0");
                        PumpUntil(() => menu.Visible && options.Visible, "placement menu and options after GJK received on the network thread");
                        // Checked on the UI thread: InvokeRequired is true when the handle was created by the network thread
                        // (the former BotUi.OnUi ran the action there for controls created hidden, without a handle).
                        Check(menu.IsHandleCreated && !menu.InvokeRequired && options.IsHandleCreated && !options.InvokeRequired,
                            "Placement menu or options got their window handle on the network thread");
                        Feed(account, "GP" + Hash.Get_Cell_Char(0) + Hash.Get_Cell_Char(3) + "|" + Hash.Get_Cell_Char(9) + "|0");
                        Feed(account, "GM|+0;1;0;42;Personnage de test;1;10^100;0|+3;1;0;43;Allié de test;1;10^100;0"); Feed(account, "GIC|42;3;1");
                        PumpUntil(() => menu.Visible && options.Visible && combatTools.Visible, "placement controls");
                        Check(fight.CanCancel && fight.HasChallengeMenu && fight.FightType == 0, "GJK fields 2, 3 and 6 were not read");
                        Check(menu.CancelButton.Visible && menu.ReadyButton.Text == "Prêt" && menu.ReadyButton.Enabled, "Placement menu does not show Prêt and Annuler");
                        Check(giveUp.Visible && giveUp.Enabled && giveUp.Text == "Abandonner", "Give-up button missing during placement");
                        Check(options.ButtonFor(FightOptions.BlockJoiner).Visible && options.ButtonFor(FightOptions.NeedHelp).Visible && options.ButtonFor(FightOptions.BlockSpectators).Visible
                            && !options.ButtonFor(FightOptions.PartyOnly).Visible && options.FlagButton.Visible && options.TacticButton.Visible && options.TacticButton.Enabled,
                            "Placement options differ from the client (fN, fH, fS, flag, tactic mode; fP hidden without party)");
                        Check(options.TooltipOf(options.ButtonFor(FightOptions.BlockJoiner)).Contains("rejoindre"), "Option tooltip missing");
                        options.ButtonFor(FightOptions.BlockJoiner).PerformClick(); Expect(peer, "fN", "Block joiner button does not send fN");
                        Check(!options.ButtonFor(FightOptions.BlockJoiner).Checked, "Option state changed before the server answered");
                        Feed(account, "Go+A42"); PumpUntil(() => options.ButtonFor(FightOptions.BlockJoiner).Checked, "Go+A42 checked");
                        Feed(account, "Go-A42"); PumpUntil(() => !options.ButtonFor(FightOptions.BlockJoiner).Checked, "Go-A42 unchecked");
                        options.ButtonFor(FightOptions.NeedHelp).PerformClick(); Expect(peer, "fH", "Help button does not send fH");
                        options.ButtonFor(FightOptions.BlockSpectators).PerformClick(); Expect(peer, "fS", "Spectators button does not send fS");
                        Feed(account, "Go+S42"); PumpUntil(() => options.ButtonFor(FightOptions.BlockSpectators).Checked, "Go+S42 checked");
                        // Lot X7 : icônes d'options au-dessus du chef de l'équipe (addSpriteOverHeadItem("FightOptions") du client), dans l'ordre A, S, P, H.
                        Check(map.OptionIconsOf(42).SequenceEqual(new[] { "UI/Client/UI_FightOptionBlockSpectatorUp.png" }) && map.OptionIconsOf(43).Length == 0,
                            "Go+S42 did not give the team leader its spectator icon: " + string.Join(",", map.OptionIconsOf(42)));
                        Check(UserMapControl.FightOptionIconFiles(FightOptions.NeedHelp | FightOptions.BlockJoiner)
                            .SequenceEqual(new[] { "UI/Client/UI_FightOptionBlockJoinerUp.png", "UI/Client/UI_FightOptionNeedHelpUp.png" })
                            && UserMapControl.FightOptionIconFiles(FightOptions.None).Length == 0, "Option icons are not in the client order (A, S, P, H)");

                        menu.ReadyButton.PerformClick(); Expect(peer, "GR1", "Placement menu Prêt does not send GR1");
                        Check(!fight.IsReady, "Ready state predicted locally");
                        Feed(account, "GR142"); PumpUntil(() => menu.ReadyButton.Text == "Annuler", "ready tick");
                        // Lot X7 : GR1 de chaque combattant → clip « prêt » (ready.swf) au-dessus du sprite, à la place de l'aura ; GR0 le retire.
                        Feed(account, "GR143"); Feed(account, "GRx"); Feed(account, "GR199");
                        PumpUntil(() => fight.IsFighterReady(42) && fight.IsFighterReady(43) && !fight.IsFighterReady(99), "GR1 per fighter");
                        // Sprites en staticR (direction 1) : le clip « prêt » ignore xtraClipTopAnimations, comme addExtraClip du client.
                        PumpUntil(() => form.Map.ExtraClips.Active.Count(clip => clip.Family == ActorExtraClips.ReadyFamily && clip.Above && !clip.TopClipRule
                            && (clip.ActorId == 42 || clip.ActorId == 43)) == 2, "ready clips of both fighters");
                        menu.ReadyButton.PerformClick(); Expect(peer, "GR0", "Second click does not send GR0");
                        Feed(account, "GR042"); PumpUntil(() => menu.ReadyButton.Text == "Prêt", "ready reset");
                        PumpUntil(() => !fight.IsFighterReady(42) && fight.IsFighterReady(43)
                            && form.Map.ExtraClips.Active.All(clip => clip.Family != ActorExtraClips.ReadyFamily || clip.ActorId == 43), "GR042 removed the clip of 42 only");

                        Feed(account, "Gf43|7"); PumpUntil(() => form.Map.DisplayedFlag != null && form.Map.DisplayedFlag.CellId == 7 && form.Map.DisplayedFlag.ActorId == 43, "Gf43|7 flag");
                        Feed(account, "Gf43"); Feed(account, "Gfx|y"); Application.DoEvents();
                        Check(form.Map.DisplayedFlag.CellId == 7, "Malformed Gf replaced the flag");
                        options.FlagButton.PerformClick(); Check(form.Map.FlagMode, "Flag button did not arm the flag mode");
                        // Lot X8 : switchToFlagSet du client, la flèche reste et la cellule survolée reçoit le pointeur « C0 » ; pas de chemin.
                        Hover(map, 5);
                        Check(map.Cursor == Cursors.Default && !map.IsAiming && map.PointerCells.Count == 1 && map.PointerCells[0].Count == 1
                            && map.PointerCells[0].Contains((short)5) && map.PathPreview.Count == 0, "Flag mode pointer is not the hovered cell alone");
                        ClickCell(map, 5); Expect(peer, "Gf5", "Click in flag mode does not send Gf<cell>");
                        Check(!form.Map.FlagMode && fight.IsPlacement && map.PointerCells.Count == 0, "Flag mode stays armed (or keeps its pointer) after the click");
                        options.FlagButton.PerformClick(); Check(form.Map.FlagMode, "Flag mode not re-armed");
                        Check(form.ProcessShortcut(Keys.Escape) && !form.Map.FlagMode, "Escape does not cancel the flag mode");
                        NoPacket(peer, "Escape in flag mode sent a packet");
                        // Lot X12 (V-26, règle du client) : avant GS, placement compris, les cases de sort sont voilées (NOT_IN_FIGHT).
                        ShortcutBar spellBar = form.Banner.Shortcuts; Button spellSlot = spellBar.SlotButtons[0];
                        spellBar.RefreshContent();
                        Check(spellBar.SpellAt(1) == 10 && !Flag(spellSlot, "Available") && !Flag(spellBar.CloseCombatSlot, "Available"), "Spell cells are not dimmed during the placement");

                        // ---------------------------------------------------------------- combat lancé : ligne de temps
                        Feed(account, "GS");
                        Feed(account, "GM|+9;1;0;-7;101;-2;1100^100;1;-1;-1;-1;0,0,0,0;60;4;2;1");
                        Feed(account, "GTM|42;0;100;8;3;3;;100|-7;0;60;4;2;9;;60|43;0;50;6;3;6;;50");
                        PumpUntil(() => !menu.Visible && !options.ButtonFor(FightOptions.BlockJoiner).Visible, "options after GS");
                        Check(!options.ButtonFor(FightOptions.NeedHelp).Visible && options.ButtonFor(FightOptions.BlockSpectators).Visible && options.FlagButton.Visible
                            && options.TacticButton.Visible && options.TacticButton.Enabled, "Running fight keeps only spectators, flag and tactic mode");
                        // Lot X33 : mode tactique purement local (Game.isTacticMode du client) ; le bouton reste enfoncé, la carte le suit.
                        options.TacticButton.PerformClick();
                        PumpUntil(() => fight.TacticMode && options.TacticButton.Checked && map.TacticView, "tactic mode on");
                        NoPacket(peer, "Tactic mode button sent a packet");
                        options.TacticButton.PerformClick();
                        PumpUntil(() => !fight.TacticMode && !options.TacticButton.Checked && !map.TacticView, "tactic mode off");
                        Check(!timeline.Visible && timeline.Entries.Count == 0, "Timeline shown before GTL");
                        FeedFromNetwork(account, "GTL|42|43|-7");
                        PumpUntil(() => timeline.Visible && timeline.Entries.Count == 3, "GTL portraits (packet received on the network thread)");
                        Check(timeline.IsHandleCreated && !timeline.InvokeRequired, "Timeline got its window handle on the network thread");
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
                        PumpUntil(() => ((Label)Get(form, "summary")).Text.StartsWith("Tour de Allié de test"), "status bar names the fighter whose turn it is");
                        // Another fighter's turn: its own PA and PM (GTM 43 : 6 PA, 3 PM), never those of the account (8 PA).
                        Check(((Label)Get(form, "summary")).Text == "Tour de Allié de test · 6 PA · 3 PM",
                            "status bar shows other points than the current fighter's: " + ((Label)Get(form, "summary")).Text);
                        // Le tour d'un autre ne voile pas la case : le client refuse seulement le clic (NOT_YOUR_TURN n'est pas testé par la barre).
                        spellBar.RefreshContent();
                        Check(Flag(spellSlot, "Available"), "Another fighter's turn dimmed the spell cell");
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
                        // ---------------------------------------------------------------- lot X7 : premier tour, zones GDZ, invisibilité GA;150
                        // Le premier tour retire l'état « prêt » (onTurnStart isFirstTurn : clips du dessus retirés de tous les sprites).
                        Check(!fight.IsFighterReady(43) && !form.Map.ExtraClips.Active.Any(clip => clip.Family == ActorExtraClips.ReadyFamily), "Ready state survived the first turn");
                        // GDZ+ : zone de la couleur ZONE_COLOR[3] fondue à 30 % (Zone.ALPHA) sur la cellule ; GDZ- l'efface.
                        Color before = PixelOfCell(map, 5);
                        Feed(account, "GDZ+5;0;3"); Feed(account, "GDZx"); Feed(account, "GDZ+a;b;c");
                        Check(fight.Zones.Count == 1 && fight.Zones[0].CellId == 5 && fight.Zones[0].Size == 0 && fight.Zones[0].Color == 3, "GDZ+5;0;3 not kept as the only zone");
                        int zoneRgb = FightZoneLayer.Rgb(3);
                        Color zoned = PixelOfCell(map, 5);
                        Func<int, int, int> blend = (under, over) => (int)Math.Round(under * (100 - FightZoneLayer.FillAlphaPercent) / 100.0 + over * FightZoneLayer.FillAlphaPercent / 100.0);
                        Check(Math.Abs(zoned.R - blend(before.R, (zoneRgb >> 16) & 0xFF)) <= 10 && Math.Abs(zoned.G - blend(before.G, (zoneRgb >> 8) & 0xFF)) <= 10
                            && Math.Abs(zoned.B - blend(before.B, zoneRgb & 0xFF)) <= 10, "GDZ zone is not blended at 30 % on its cell: " + before + " -> " + zoned + " with " + zoneRgb.ToString("X6"));
                        Feed(account, "GDZ-5;0;3");
                        Check(fight.Zones.Count == 0 && PixelOfCell(map, 5) == before, "GDZ- did not erase the zone");
                        Feed(account, "GDZ+5;1;3");
                        Check(fight.Zones.Count == 1 && FightZoneLayer.CellsOf(fight.Zones[0], account.Game.Map.MapCells).OrderBy(id => id).SequenceEqual(
                            account.Game.Map.MapCells.Where(c => c != null && c.IsActive && account.Game.Map.MapCells[5].GetDistanceBetweenCells(c) <= 1).Select(c => (int)c.CellID).OrderBy(id => id)),
                            "Zone of size 1 does not cover the cells at distance 1");
                        Feed(account, "GDZ-5;1;3"); Check(fight.Zones.Count == 0, "GDZ- of the size 1 zone ignored");
                        // GA;150 : l'autre combattant disparaît (setVisible(false)), le sien passe à 40 % (setAlpha(40)), journal dans le chat (INFO_FIGHT_CHAT).
                        PumpUntil(() => map.GetActorVisualState(43) != null && map.GetActorVisualState(43).IsVisible && map.GetActorVisualState(42) != null, "fighters drawn before GA;150");
                        Feed(account, "GA;150;43;43,2"); Feed(account, "GA;150;43;zz");
                        PumpUntil(() => fight.IsFighterInvisible(43) && !map.GetActorVisualState(43).IsVisible, "GA;150 did not hide the other fighter");
                        Check((map.GetActorVisualState(43).SpriteReason ?? string.Empty).Contains("invisible") && fight.InvisibleFighterIds.SequenceEqual(new[] { 43 }), "Hidden fighter lacks its reason or is not listed");
                        Feed(account, "GA;150;42;42,2");
                        PumpUntil(() => map.GetActorVisualState(42).IsVisible && Math.Abs(map.GetActorVisualState(42).Alpha - .4f) < .01f, "own invisible fighter is not drawn at 40 %");
                        PumpUntil(() => form.Chat.View.DisplayedLines.Any(line => line.Filter == ChatFilter.Infos && line.Text == "Allié de test devient invisible."), "journal line of GA;150 missing in the chat");
                        Feed(account, "GA;150;43;43,0"); Feed(account, "GA;150;42;42,0");
                        PumpUntil(() => !fight.IsFighterInvisible(43) && map.GetActorVisualState(43).IsVisible && map.GetActorVisualState(42).Alpha == 1f, "GA;150 with 0 turn did not show the fighters again");
                        Check(form.Chat.View.DisplayedLines.Any(line => line.Text == "Personnage de test redevient visible."), "journal line of the visibility return missing");

                        Feed(account, "GTF43"); PumpUntil(() => timeline.CurrentFighterId == 0, "GTF");
                        Check(timeline.RemainingMilliseconds == 0 && timeline.HighlightedFighterId == 43, "GTF kept the chrono or lost the GTR frame");
                        Feed(account, "GTS42|30000"); PumpUntil(() => timeline.CurrentFighterId == 42, "own turn");
                        PumpUntil(() => ((Label)Get(form, "summary")).Text == "Votre tour · 8 PA · 3 PM", "status bar shows the account's points on its turn");
                        // V-26 : PA insuffisants puis délai de relance (tours restants dans le coin, showLabel) voilent la case.
                        spellBar.RefreshContent();
                        Check(Flag(spellSlot, "Available") && Badge(spellSlot) == null, "Spell cell dimmed on the own turn");
                        Feed(account, "GTM|42;0;100;2;3;3;;100"); spellBar.RefreshContent();
                        Check(!Flag(spellSlot, "Available") && Badge(spellSlot) == null, "Spell cell is not dimmed without enough AP (NOT_ENOUGH_AP)");
                        Feed(account, "GTM|42;0;100;8;3;3;;100"); spellBar.RefreshContent(); Check(Flag(spellSlot, "Available"), "Spell cell stayed dimmed with AP back");
                        Feed(account, "GAS42"); Feed(account, "GA;300;42;10,5,,1"); Feed(account, "GAF0|42");
                        Expect(peer, "GKK0", "GAF of the spell cast not acknowledged");
                        Check(fight.GetSpellCooldownRemaining(10) == 2, "Spell cooldown not recorded: " + fight.GetSpellCooldownRemaining(10));
                        spellBar.RefreshContent();
                        Check(!Flag(spellSlot, "Available") && Badge(spellSlot) == "2" && !Flag(spellSlot, "WrongState"), "Spell cell in cooldown is not dimmed with its turns (CANT_LAUNCH_BEFORE)");

                        // ---------------------------------------------------------------- corps à corps (lot X1) : case ², clic adjacent
                        Cell origin = account.Game.character.Cell;
                        Cell near = account.Game.Map.MapCells.FirstOrDefault(c => c != null && c.IsActive && c.CellID != 6 && origin.GetDistanceBetweenCells(c) == 1);
                        Cell far = account.Game.Map.MapCells.FirstOrDefault(c => c != null && c.IsActive && c.CellID != 6 && origin.GetDistanceBetweenCells(c) == 2);
                        Check(origin.CellID == 3 && near != null && far != null, "No free cell at distance 1 and 2 of the character");
                        Feed(account, "GA;4;42;-7," + near.CellID);
                        // GM complet d'un joueur en combat (StarLoco) : l'équipe est le 25e champ.
                        Feed(account, "GM|+3;1;0;42;Personnage de test;8;80^100;0;25;0,0,0,0;-1;-1;-1;,,,,;100;8;3;0;0;0;0;0;0;0;0");
                        Check(fight.Fighters[42].Team >= 0 && fight.Fighters[-7].Team >= 0 && fight.Fighters[42].Team != fight.Fighters[-7].Team, "Teams of GM not known");
                        ShortcutBar bar = form.Banner.Shortcuts; Button slot = bar.CloseCombatSlot;
                        // Sans arme, la case montre le poing DefaultCC (clips/defaultcc.swf, 32 × 32), lu hors du fil de l'interface.
                        PumpUntil(() => { bar.RefreshContent(); return slot.GetType().GetProperty("Icon").GetValue(slot) is Image icon && icon.Width == 32 && icon.Height == 32; },
                            "DefaultCC icon of the ² slot");
                        Check(Flag(slot, "Available") && !Flag(slot, "SelectedSpell") && slot.AccessibleName.Contains("Poings")
                            && bar.CloseCombatTooltip.Contains("(²)")
                            && bar.CloseCombatTooltip.Contains("4 PA") && !bar.CloseCombatTooltip.Contains("à venir"), "Close combat slot is not active: " + bar.CloseCombatTooltip);
                        slot.PerformClick();
                        PumpUntil(() => form.Map.SelectedSpellId == Fights.CloseCombatSpellId, "² selects the close combat");
                        var targets = (HashSet<short>)Get(map, "spellTargets");
                        Check(targets != null && targets.Contains(near.CellID) && !targets.Contains(far.CellID) && !targets.Contains(origin.CellID),
                            "Close combat targets are not the adjacent cells");
                        // Lot X8 : portée du poing (1–1) en zone #246696, poing DefaultCC sur le curseur, pointeur « P » sur une cible valable,
                        // croix d'interdiction ailleurs ; ni chemin ni pointeur pendant la visée hors cible.
                        Check(map.IsAiming && map.AimIcon == "Client/DefaultCC" && map.SpellRange.Count > 0
                            && map.SpellRange.All(id => origin.GetDistanceBetweenCells(account.Game.Map.MapCells[id]) == 1), "Close combat range zone is not the adjacent ring");
                        Hover(map, near.CellID);
                        Check(!map.CursorForbidden && map.PointerCells.Count == 1 && map.PointerCells[0].Count == 1 && map.PointerCells[0].Contains(near.CellID),
                            "Pointer of the fists is not the hovered target cell");
                        Hover(map, far.CellID);
                        Check(map.CursorForbidden && map.PointerCells.Count == 0 && map.PathPreview.Count == 0, "Invalid target shows a pointer, a path or no forbidden cross");
                        bar.RefreshContent();
                        Check(Flag(slot, "SelectedSpell") && ((Label)Get(form, "summary")).Text.Contains("Corps à corps"), "Selected ² slot or status text missing");
                        ClickCell(map, far.CellID); NoPacket(peer, "Close combat on a cell at distance 2 was sent");
                        Check(form.Map.SelectedSpellId == Fights.CloseCombatSpellId && ((Label)Get(form, "summary")).Text.Contains("adjacente"),
                            "Refused target did not explain the adjacency");
                        ClickCell(map, near.CellID); Expect(peer, "GA303" + near.CellID, "Click in close combat mode does not send GA303<cell>");
                        PumpUntil(() => form.Map.SelectedSpellId == null, "close combat aim released after sending");
                        Feed(account, "GAS42"); Feed(account, "GA;300;42;0," + near.CellID + ",0,1"); Feed(account, "GA;102;42;42,-4"); Feed(account, "GAF0|42");
                        Expect(peer, "GKK0", "GAF of the fists attack not acknowledged");
                        Feed(account, "GA;102;42;42,-0"); Check(!fight.IsActionPending && fight.ActionPoints == 4, "Fists casting lock not released");
                        // Ajout du bot : clic sur l'ennemi adjacent sans sort choisi.
                        ClickCell(map, near.CellID); Expect(peer, "GA303" + near.CellID, "Click on an adjacent enemy does not attack");
                        Feed(account, "GAS42"); Feed(account, "GA;300;42;0," + near.CellID + ",0,1"); Feed(account, "GA;102;42;42,-4"); Feed(account, "GAF0|42");
                        Expect(peer, "GKK0", "GAF of the second attack not acknowledged"); Feed(account, "GA;102;42;42,-0");
                        ClickCell(map, near.CellID); NoPacket(peer, "Attack sent without action points");
                        Check(((Label)Get(form, "summary")).Text.Contains("PA insuffisants"), "Missing AP not reported: " + ((Label)Get(form, "summary")).Text);
                        bar.RefreshContent(); Check(!Flag(slot, "Available") && bar.CloseCombatTooltip.Contains("PA insuffisants"), "Slot active without AP");
                        slot.PerformClick(); Application.DoEvents();
                        Check(form.Map.SelectedSpellId == null, "² selected the close combat without AP");
                        // SH0 (touche ²), puis Échap annule la visée sans rien envoyer.
                        Feed(account, "GTM|42;0;100;8;3;3;;100");
                        Check(form.ProcessShortcut(Keys.Oem7) && form.Map.SelectedSpellId == Fights.CloseCombatSpellId, "SH0 does not select the close combat");
                        Check(form.ProcessShortcut(Keys.Escape) && form.Map.SelectedSpellId == null, "Escape does not cancel the close combat aim");
                        NoPacket(peer, "Escape in close combat mode sent a packet");
                        // Lot X8 : pendant son tour, le survol montre le chemin de combat du client (calculatePath : au plus PM cases, quatre
                        // directions, combattants évités) en formes de sélection opaques #FF6600 ; rien vers une cellule occupée.
                        Cell walk = account.Game.Map.MapCells.FirstOrDefault(c => c != null && c.IsWalkable() && origin.GetDistanceBetweenCells(c) == 2
                            && !fight.Fighters.Values.Any(f => !f.IsDead && f.CellId == c.CellID) && fight.PreviewMovePath(c.CellID) != null);
                        Check(walk != null, "No free cell at distance 2 reachable in combat");
                        var expectedPath = new HashSet<short>(fight.PreviewMovePath(walk.CellID).Skip(1).Select(c => c.CellID));
                        Check(expectedPath.Count == 2 && expectedPath.Contains(walk.CellID), "Combat path preview is not two steps long");
                        // Comme le client, le chemin se calcule au survol d'une nouvelle cellule : passer d'abord sur la cellule du personnage.
                        Hover(map, origin.CellID); PumpUntil(() => map.PathPreview.Count == 0, "no combat path on the own cell");
                        Hover(map, walk.CellID);
                        PumpUntil(() => map.PathPreviewUsesSelection && new HashSet<short>(map.PathPreview).SetEquals(expectedPath), "combat path preview on hover");
                        Color path = PixelOfCell(map, walk.CellID);
                        Check(path.R > 225 && path.G > 80 && path.G < 125 && path.B < 45, "Combat path cell is not the opaque CELL_PATH_OVER_COLOR: " + path);
                        Hover(map, near.CellID); PumpUntil(() => map.PathPreview.Count == 0, "no combat path towards an occupied cell");
                        Hover(map, walk.CellID); PumpUntil(() => map.PathPreview.Count == 2, "combat path back on a free cell");
                        Feed(account, "GTF42"); PumpUntil(() => map.PathPreview.Count == 0, "combat path kept after the end of the turn");
                        Feed(account, "GTS42|30000"); PumpUntil(() => fight.IsMyTurn, "own turn again");
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
                        // ---------------------------------------------------------------- défis (lot X9) : colonne, détail, Gdi, chat
                        ChallengePanel challenges = form.Challenges;
                        Check(challenges != null && !challenges.Visible && challenges.Icons.Count == 0, "Challenge column shown before Gd");
                        string challengeLang = Path.Combine(TestPaths.Work, "x9-ui-lang"); Directory.CreateDirectory(challengeLang);
                        foreach (string old in Directory.GetFiles(challengeLang, "*.xml")) File.Delete(old);
                        File.WriteAllText(Path.Combine(challengeLang, "fightChallenge.xml"), "<?xml version='1.0' encoding='utf-8'?>\n"
                            + "<BotLang famille=\"fightChallenge\" langue=\"fr\" version=\"1\" source=\"fightChallenge_fr_1.swf\">\n"
                            + "<entree table=\"FC\" id=\"3\" d=\"Abattre %1 en premier (texte de test).\" g=\"4\" n=\"Volontaire de test\" />\n"
                            + "<entree table=\"FC\" id=\"17\" d=\"Ne perdre aucun point de vie (texte de test).\" g=\"17\" n=\"Intact de test\" />\n</BotLang>\n",
                            new UTF8Encoding(false));
                        Check(LangData.Load(challengeLang) == 1, "Synthetic fightChallenge.xml not loaded: " + string.Join(" / ", LangData.LoadWarnings));
                        Feed(account, "GM|+9;1;0;-7;101;-2;1100^100;1;-1;-1;-1;0,0,0,0;60;4;2;1");
                        // Le GM de combat ne porte que le grade : niveau posé ici comme s'il venait de monsters.xml.
                        ((Tool_BotProtocol.Game.Monstres.Monstres)account.Game.Map.GetActor(-7)).Level = 4;
                        string monsterName = fight.Fighters[-7].Name;
                        FeedFromNetwork(account, "Gd3;1;-7;20;0;15;0"); FeedFromNetwork(account, "Gd17;0;;10;0;5;0");
                        PumpUntil(() => challenges.Visible && challenges.Icons.Count == 2 && challenges.Icons.All(icon => icon.HasIcon),
                            "challenge column with the exported client icons (Challenges/4.png, Challenges/17.png) after Gd");
                        Check(challenges.IsHandleCreated && !challenges.InvokeRequired, "Challenge column got its handle on the network thread");
                        ChallengeIcon first = challenges.Icons[0], second = challenges.Icons[1];
                        Check(challenges.Location == new Point(6, 92) && first.Size == new Size(ChallengeIcon.ColumnSize, ChallengeIcon.ColumnSize) && first.Top == ChallengePanel.IconTop
                            && second.Top - first.Top == 44 && challenges.Height >= second.Bottom && first.Challenge.Id == 3 && second.Challenge.Id == 17,
                            "Challenge column layout differs from UI_FightChallenge (38 x 38, (6 + height) x rank)");
                        string firstTip = challenges.TooltipOf(first);
                        Check(firstTip == "Volontaire de test\nAbattre " + monsterName + " (Niv. 4) en premier (texte de test).\nButin : +15%\nXP : +20%\nEtat : Challenge en cours",
                            "Challenge icon tooltip: " + firstTip);
                        Check(challenges.TooltipOf(challenges.ToggleButton) == "Affiche/Cache le groupe", "Toggle tooltip is not PARTY_OPEN_CLOSE");
                        // Clic sur l'icône → détail à droite de la colonne ; « voir la cible » → Gdi3 puis fermeture, comme FightChallengeViewer.
                        Click(first); PumpUntil(() => challenges.ViewerOpen, "challenge viewer after a click on the icon");
                        ChallengeViewer viewer = challenges.Viewer;
                        Check(viewer.Parent == challenges.Parent && viewer.Left == challenges.Right + 4 && viewer.Top == challenges.Top, "Challenge viewer is not beside the column");
                        Check(viewer.TitleText == "Challenge en cours" && viewer.NameText == "Volontaire de test" && viewer.DescriptionText.Contains(monsterName + " (Niv. 4)")
                            && viewer.DropText == "+15%" && viewer.XpText == "+20%" && viewer.StateText == "Etat : Challenge en cours" && viewer.ViewButton.Visible,
                            "Challenge viewer texts: " + viewer.NameText + " / " + viewer.DescriptionText + " / " + viewer.StateText);
                        Check(viewer.TooltipOf(viewer.Controls["challenge-viewer-xp"]) == "Bonus de base : 20%\nBonus de groupe : 0%", "Bonus tooltip of the viewer");
                        DateTime asked = DateTime.UtcNow;
                        viewer.ViewButton.PerformClick(); Expect(peer, "Gdi3", "« Voir la cible » does not send Gdi<type>");
                        PumpUntil(() => !challenges.ViewerOpen, "viewer closed after the target request");
                        Click(first); PumpUntil(() => challenges.ViewerOpen, "viewer opened again");
                        // _lastShowAsk : une seule demande par seconde (une machine lente qui dépasse la seconde reçoit la seconde demande).
                        bool quick = (DateTime.UtcNow - asked).TotalMilliseconds < Fights.ChallengeTargetDelayMilliseconds - 100;
                        viewer.ViewButton.PerformClick();
                        if (quick) { NoPacket(peer, "Second Gdi sent within one second"); Check(challenges.ViewerOpen, "Throttled click closed the viewer"); }
                        else Expect(peer, "Gdi3", "Gdi not sent again after one second");
                        // Le serveur répond par Gf<cible>|<cellule>, dessiné en drapeau.
                        Feed(account, "Gf-7|5"); PumpUntil(() => form.Map.DisplayedFlag != null && form.Map.DisplayedFlag.ActorId == -7 && form.Map.DisplayedFlag.CellId == 5, "Gf answer drawn as a flag");
                        Click(second); PumpUntil(() => challenges.ViewerOpen && viewer.NameText == "Intact de test", "viewer of the second challenge");
                        Check(!viewer.ViewButton.Visible && viewer.DescriptionText == "Ne perdre aucun point de vie (texte de test).", "Viewer of a challenge without target");
                        viewer.CloseButton.PerformClick(); PumpUntil(() => !challenges.ViewerOpen, "viewer closed by its button");
                        // Bouton affiche / cache (_btnOpenClose).
                        Click(challenges.ToggleButton);
                        Check(challenges.Collapsed && !first.Visible && !second.Visible && challenges.Height == ChallengePanel.IconTop, "Toggle does not hide the icons");
                        Click(challenges.ToggleButton);
                        Check(!challenges.Collapsed && first.Visible && second.Visible && challenges.Height >= second.Bottom, "Toggle does not show the icons again");
                        // GdOK / GdKO : symboles ChallengeOK / ChallengeKO sur les icônes et lignes du chat (INFO_CHAT).
                        FeedFromNetwork(account, "GdOK3"); FeedFromNetwork(account, "GdKO17");
                        PumpUntil(() => form.Chat.View.DisplayedLines.Any(line => line.Text.Contains("Challenge réussi : Volontaire de test"))
                            && form.Chat.View.DisplayedLines.Any(line => line.Text.Contains("Challenge raté : Intact de test")), "challenge lines in the chat");
                        PumpUntil(() => first.HasStateMark && second.HasStateMark && first.Challenge.State == FightChallengeState.Succeeded
                            && second.Challenge.State == FightChallengeState.Failed, "ChallengeOK / ChallengeKO marks on the icons");
                        Check(challenges.TooltipOf(first).EndsWith("Etat : Challenge réussi") && challenges.TooltipOf(second).EndsWith("Etat : Challenge raté"), "Tooltip state after GdOK/GdKO");
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
                        // Lot X9 : « Bonus de challenges : » et les icônes 17 x 17 du résultat ; la colonne du combat disparaît.
                        Check(result.ChallengesText == "Bonus de challenges : Volontaire de test (Challenge réussi) : XP +20%, Butin +15% · Intact de test (Challenge raté) : XP +10%, Butin +5%",
                            "Challenge bonus of the result: " + result.ChallengesText);
                        PumpUntil(() => result.ChallengeIcons.Count == 2 && result.ChallengeIcons.All(icon => icon.HasIcon && icon.HasStateMark), "result challenge icons");
                        Check(result.ChallengeIcons[0].Size == new Size(ChallengeIcon.ResultSize, ChallengeIcon.ResultSize)
                            && result.TooltipOf(result.ChallengeIcons[1]).StartsWith("Intact de test\n"), "Result challenge icon size or tooltip");
                        PumpUntil(() => !challenges.Visible && challenges.Icons.Count == 0 && !challenges.ViewerOpen, "challenge column hidden after GE");
                        LangData.Clear();
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
                        Check(result.ChallengesText == "" && result.ChallengeIcons.Count == 0, "PvP result shows the challenges of the previous fight");
                        ((Button)result.CloseButton).PerformClick(); PumpUntil(() => !form.Panels.IsOpen(result), "pvp result closed");

                        // ---------------------------------------------------------------- spectateur : GQ sans question
                        Feed(account, "GJK3|0|1|1|30000|4");
                        PumpUntil(() => combatTools.Visible && giveUp.Text == "Quitter", "spectator button");
                        // Lot X33 : le spectateur ne garde que le mode tactique (initData du client), ni drapeau ni option d'équipe.
                        PumpUntil(() => options.Visible && options.TacticButton.Visible, "spectator tactic button");
                        Check(options.Buttons.Count(button => button.Visible) == 1 && !options.FlagButton.Visible
                            && !menu.Visible && !((Control)Get(form, "ready")).Visible && !((Control)Get(form, "passTurn")).Visible, "Spectator sees fighter controls");
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
        finally { listener.Stop(); Environment.CurrentDirectory = previous; LangData.Clear(); }
    }
}
