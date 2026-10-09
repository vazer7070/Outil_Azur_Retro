using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Outil_Azur_complet.Bot.Controls;
using Outil_Azur_complet.Bot.Interfaces;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Combats;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Utils.Crypto;

// Socle des animations (lot AN1) : séquenceur visuel (étape bloquante de 500 ms, étape sans fin libérée à 1 000 ms, Die de
// 1 500 ms exactement, rattrapage au-delà de 2 s ou de 32 étapes), animations ponctuelles (hit : image 0 puis retour à
// static, enchaînement suite:<anim>, orientation d|1), fantôme die dessiné d'après l'instantané puis purgé, bande en grille
// de deux lignes, effets accrochés (condition xtraClipTopAnimations) et bandes d'effet, paquets GA bruts injectés dans
// Fights (événements, file du joueur du tour, instantané de mort) et carte cachée (aucune file ne grossit). Horloge
// injectée, sprites et effets synthétiques dans TestPaths.Work, dessin par Graphics.FromImage : passe sous Mono.
internal static class BotAnimationQueueSmoke
{
    private const int MapWidth = 8, MapHeight = 8;

    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            string file = Path.Combine(TestPaths.ApplicationBin, new AssemblyName(args.Name).Name + ".dll");
            if (!File.Exists(file)) file = Path.ChangeExtension(file, "exe");
            return File.Exists(file) ? Assembly.LoadFrom(file) : null;
        };
        try
        {
            Sequencer();
            Families();
            string root = Path.Combine(TestPaths.Work, "animation-queue");
            if (Directory.Exists(root)) Directory.Delete(root, true);
            string sprites = Assets(root);
            Sheets(sprites);
            View(root, sprites);
            // Paquets sur le pool, sans contexte de synchronisation WinForms (aucune boucle de messages ici).
            List<VisualEvent> events = Task.Run(() => FightEvents()).GetAwaiter().GetResult();
            HiddenMap(events);
            Console.WriteLine("OK: blocking 500 ms step, endless step released at 1000 ms, Die 1500 ms, catch-up after 2 s and 32 steps, "
                + "hit frame 0 then static, suite chain, d|1, die ghost drawn then purged, two-row grid, attached and strip effects, "
                + "raw GA packets in Fights (events, turn queue, death snapshot), hidden map keeps every queue empty");
        }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    // ------------------------------------------------------------------ séquenceur seul

    private static void Sequencer()
    {
        var log = new List<string>();
        Action<double> Note(string name) => now => log.Add(name + "@" + now);
        var sequencer = new FightVisualSequencer();

        // Étape bloquante de 500 ms : la suivante ne part qu'à t ≥ 500 ; une autre file n'attend pas.
        sequencer.Enqueue(1, VisualStep.Timed("b500", Note("b500"), 500));
        sequencer.Enqueue(1, VisualStep.Instant("next", Note("next")));
        sequencer.Enqueue(2, VisualStep.Instant("other", Note("other")));
        Check(sequencer.Pump(0) && log.Contains("b500@0") && log.Contains("other@0") && log.Count == 2, "Blocking step or independent queue did not start at 0");
        sequencer.Pump(499);
        Check(!log.Any(entry => entry.StartsWith("next")) && sequencer.Count(1) == 2, "Step after a 500 ms blocking step started before 500 ms");
        Check(!sequencer.Pump(500) && log.Last() == "next@500" && sequencer.IsIdle, "Step after a 500 ms blocking step did not start at 500 ms");

        // Étape bloquante sans fin (ni Done ni forceTimeout) : libérée par le délai maximal de 1 000 ms.
        log.Clear();
        sequencer.Enqueue(3, VisualStep.Waiting("endless", null));
        sequencer.Enqueue(3, VisualStep.Instant("after", Note("after")));
        sequencer.Pump(0); sequencer.Pump(999);
        Check(log.Count == 0, "Endless blocking step released before 1000 ms");
        sequencer.Pump(1000);
        Check(log.SequenceEqual(new[] { "after@1000" }), "Endless blocking step not released at 1000 ms");

        // Fin anticipée par Done ; forceTimeout ignore Done.
        log.Clear();
        bool done = false;
        sequencer.Enqueue(4, VisualStep.Waiting("until", null, now => done));
        sequencer.Enqueue(4, VisualStep.Instant("released", Note("released")));
        sequencer.Pump(0); sequencer.Pump(200);
        Check(log.Count == 0, "Waiting step released before its Done");
        done = true; sequencer.Pump(201);
        Check(log.SequenceEqual(new[] { "released@201" }), "Waiting step not released by Done");
        log.Clear();
        sequencer.Enqueue(5, new VisualStep("forced", null, true, 300, true, now => true));
        sequencer.Enqueue(5, VisualStep.Instant("afterForced", Note("afterForced")));
        sequencer.Pump(0); sequencer.Pump(299);
        Check(log.Count == 0, "forceTimeout step released early by Done");
        sequencer.Pump(300);
        Check(log.SequenceEqual(new[] { "afterForced@300" }), "forceTimeout step not released at its delay");

        // Die : exactement 1 500 ms.
        log.Clear();
        VisualStep die = VisualStep.Die(Note("die"));
        Check(die.Blocking && die.ForceTimeout && die.MaxDelay == 1500 && FightVisualSequencer.DieDuration == 1500, "Die is not a 1500 ms forced step");
        sequencer.Enqueue(6, die);
        sequencer.Enqueue(6, VisualStep.Instant("afterDie", Note("afterDie")));
        sequencer.Pump(10000); sequencer.Pump(11499);
        Check(log.SequenceEqual(new[] { "die@10000" }) && die.StartedAt == 10000, "Die released before 1500 ms");
        sequencer.Pump(11500);
        Check(log.Last() == "afterDie@11500", "Die not released at exactly 1500 ms");

        // Rattrapage : tête bloquante échue depuis plus de 2 s → tout part d'un coup ; à 2 s pile, avance normale.
        log.Clear();
        var late = new FightVisualSequencer();
        late.Enqueue(7, VisualStep.Waiting("A", Note("A")));
        late.Enqueue(7, VisualStep.Waiting("B", Note("B")));
        late.Enqueue(7, VisualStep.Instant("C", Note("C")));
        late.Pump(0);
        Check(!late.Pump(3001) && log.SequenceEqual(new[] { "A@0", "B@3001", "C@3001" }) && late.CatchUps == 1, "No catch-up 2 s past the head deadline");
        log.Clear();
        late.Enqueue(8, VisualStep.Waiting("A", Note("A")));
        late.Enqueue(8, VisualStep.Waiting("B", Note("B")));
        late.Enqueue(8, VisualStep.Instant("C", Note("C")));
        late.Pump(0); late.Pump(3000);
        Check(log.SequenceEqual(new[] { "A@0", "B@3000" }) && late.CatchUps == 1 && late.Count(8) == 2, "Catch-up triggered at exactly 2 s");
        late.Pump(4000);
        Check(log.Last() == "C@4000" && late.IsIdle, "Queue did not resume after the second blocking step");

        // Plus de 32 étapes en file : rattrapage immédiat ; 32 exactement : avance normale.
        int started = 0;
        var crowded = new FightVisualSequencer();
        crowded.Enqueue(9, VisualStep.Waiting("head", now => started++));
        for (int i = 0; i < FightVisualSequencer.MaxQueuedSteps; i++) crowded.Enqueue(9, VisualStep.Instant("n" + i, now => started++));
        crowded.Pump(0);
        Check(started == 33 && crowded.IsIdle && crowded.CatchUps == 1, "Queue of 33 steps was not caught up");
        started = 0;
        crowded.Enqueue(10, VisualStep.Waiting("head", now => started++));
        for (int i = 0; i < FightVisualSequencer.MaxQueuedSteps - 1; i++) crowded.Enqueue(10, VisualStep.Instant("n" + i, now => started++));
        crowded.Pump(0);
        Check(started == 1 && crowded.Count(10) == 32 && crowded.CatchUps == 1, "Queue of 32 steps was caught up");
        crowded.Clear();
        Check(crowded.IsIdle && crowded.QueueIds.Count == 0, "Clear kept steps");

        // Une étape qui lève une exception est retirée, signalée, et la file continue.
        Exception failed = null;
        bool survivor = false;
        var faulty = new FightVisualSequencer();
        faulty.StepFailed += (step, error) => failed = error;
        faulty.Enqueue(11, VisualStep.Instant("boom", now => { throw new InvalidOperationException("étape défaillante"); }));
        faulty.Enqueue(11, VisualStep.Instant("survivor", now => survivor = true));
        faulty.Pump(0);
        Check(failed is InvalidOperationException && survivor && faulty.IsIdle, "A failing step stopped its queue");
        faulty.Enqueue(12, VisualStep.Waiting("held", null));
        faulty.Enqueue(12, VisualStep.Instant("flushed", now => survivor = false));
        faulty.Flush(5);
        Check(!survivor && faulty.IsIdle, "Flush did not run the waiting steps");
    }

    /// <summary>Aiguillage par famille et condition xtraClipTopAnimations du clip du dessus.</summary>
    private static void Families()
    {
        Func<int, VisualFamily> ga = id => MapControl.FamilyOf(new VisualEvent(VisualSource.GameAction, id, 1, 1, -1, null));
        Check(new[] { 100, 108, 110, 101, 102, 111, 120, 168, 78, 127, 128, 129, 169 }.All(id => ga(id) == VisualFamily.Points), "PV/PA/PM actions are not Points");
        Check(ga(104) == VisualFamily.Hit && ga(103) == VisualFamily.Death && ga(501) == VisualFamily.Harvest && ga(165) == VisualFamily.None,
            "GA 104, 103, 501 or 165 routed to the wrong family");
        Check(new[] { 300, 303, 306, 307 }.All(id => ga(id) == VisualFamily.Spell) && new[] { 301, 302, 304, 305 }.All(id => ga(id) == VisualFamily.Critical)
            && ga(208) == VisualFamily.MapEffect && ga(228) == VisualFamily.MapEffect, "Spell, critical or map effect actions routed to the wrong family");
        Check(MapControl.FamilyOf(new VisualEvent(VisualSource.Quantity, 0, 5, 5, -1, null, 3)) == VisualFamily.Points
            && MapControl.FamilyOf(new VisualEvent(VisualSource.Emote, 0, 5, 5, -1, null, 7)) == VisualFamily.Emote
            && MapControl.FamilyOf(null) == VisualFamily.None, "IQ or eUK routed to the wrong family");

        Check(ActorAttachedEffect.TopClipVisible(ActorKind.Player, "staticF") && !ActorAttachedEffect.TopClipVisible(ActorKind.Player, "staticR")
            && !ActorAttachedEffect.TopClipVisible(ActorKind.Player, "walkF"), "Player top clip is not staticF only");
        Check(new[] { "staticL", "staticF", "staticR" }.All(name => ActorAttachedEffect.TopClipVisible(ActorKind.Merchant, name))
            && !ActorAttachedEffect.TopClipVisible(ActorKind.Merchant, "staticB"), "Merchant top clip is not staticL/F/R");
        Check(ActorAttachedEffect.TopClipVisible(ActorKind.Npc, "walkB") && ActorAttachedEffect.TopClipVisible(ActorKind.FightMonster, "hitR"),
            "Classes without xtraClipTopAnimations must always show the top clip");
    }

    // ------------------------------------------------------------------ sprites synthétiques

    private static Color FrameColor(int frame) => Color.FromArgb(255, 40 + frame * 50, 200, 40);

    private static bool Near(Color pixel, Color expected) =>
        Math.Abs(pixel.R - expected.R) < 24 && Math.Abs(pixel.G - expected.G) < 24 && Math.Abs(pixel.B - expected.B) < 24;

    /// <summary>Bande de <paramref name="frames"/> images <paramref name="width"/>×<paramref name="height"/>, <paramref name="columns"/> par ligne.</summary>
    private static void Strip(string path, int frames, int columns, int width, int height)
    {
        int lines = (frames + columns - 1) / columns;
        using (var bitmap = new Bitmap(width * columns, height * lines, PixelFormat.Format32bppArgb))
        {
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                for (int frame = 0; frame < frames; frame++)
                    using (var brush = new SolidBrush(FrameColor(frame)))
                        graphics.FillRectangle(brush, frame % columns * width + 2, frame / columns * height, width - 4, height);
            }
            bitmap.Save(path, ImageFormat.Png);
        }
    }

    /// <summary>Pose fixe (ancien format <c>&lt;gfx&gt;&lt;O&gt;.png</c>, ancrée sur ses pixels).</summary>
    private static void Pose(string path, Color color)
    {
        using (var bitmap = new Bitmap(20, 60, PixelFormat.Format32bppArgb))
        {
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                using (var brush = new SolidBrush(color)) graphics.FillRectangle(brush, 2, 0, 16, 60);
            }
            bitmap.Save(path, ImageFormat.Png);
        }
    }

    private static string Assets(string root)
    {
        string sprites = Path.Combine(root, "sprites"), family = Path.Combine(root, "Effets", "Test");
        Directory.CreateDirectory(sprites); Directory.CreateDirectory(family); Directory.CreateDirectory(Path.Combine(root, "overheads"));
        Pose(Path.Combine(sprites, "10R.png"), Color.Red);
        Pose(Path.Combine(sprites, "10F.png"), Color.Blue);
        Strip(Path.Combine(sprites, "10_hitR.png"), 4, 4, 20, 60);
        Strip(Path.Combine(sprites, "10_dieR.png"), 5, 3, 20, 60); // grille : 3 colonnes, 2 lignes, dernière incomplète
        Strip(Path.Combine(sprites, "10_anim1R.png"), 2, 2, 20, 60);
        Strip(Path.Combine(sprites, "10_anim2R.png"), 2, 2, 20, 60);
        File.WriteAllLines(Path.Combine(sprites, "ancres.tsv"), new[]
        {
            "gfx\tanim\txmin\tymin\twidth\theight\timages\tips\tfin",
            "10\thitR\t-10\t-60\t20\t60\t4\t40\tstatic",
            "10\tdieR\t-10\t-60\t20\t60\t5\t40\tarret",
            "10\tanim1R\t-10\t-60\t20\t60\t2\t40\tsuite:anim2",
            "10\tanim2R\t-10\t-60\t20\t60\t2\t40\tstatic"
        });
        Strip(Path.Combine(family, "5_aura.png"), 3, 3, 20, 20);
        Strip(Path.Combine(family, "5_boom.png"), 2, 2, 20, 20);
        File.WriteAllLines(Path.Combine(family, "effets.tsv"), new[]
        {
            "id\tanim\txmin\tymin\twidth\theight\timages\tips\tfin",
            "5\taura\t-10\t-20\t20\t20\t3\t40\tboucle",
            "5\tboom\t-10\t-20\t20\t20\t2\t40\tstatic"
        });
        return sprites;
    }

    /// <summary>Bandes lues hors de la vue : grille de deux lignes, cadence, fin, d|1 ; file d'animations sans vue.</summary>
    private static void Sheets(string sprites)
    {
        int width, height;
        Check(ActorSprites.TryReadPngSize(Path.Combine(sprites, "10_dieR.png"), out width, out height) && width == 60 && height == 120,
            "PNG header of the grid strip not read");
        using (var library = new ActorSprites(sprites))
        {
            library.Resolve(10, 1, false, "die"); library.Resolve(10, 2, false, "hit");
            Check(library.WaitForPending(10000), "Strip decoding did not finish");
            SpritePose die = library.Resolve(10, 1, false, "die"), hit = library.Resolve(10, 2, false, "hit");
            Check(die.State == SpriteLoadState.Ready && die.FullName == "dieR" && hit.State == SpriteLoadState.Ready, "Synthetic strips not ready");
            SpriteSheet grid = die.Sheet;
            Check(grid.Frames == 5 && grid.Columns == 3 && grid.Rows == 2 && grid.Source(4) == new Rectangle(20, 60, 20, 60)
                && grid.Source(2) == new Rectangle(40, 0, 20, 60), "Two-row grid strip read as " + grid.Frames + " frames, " + grid.Columns + " columns");
            Rectangle last = grid.Source(4);
            Check(Near(grid.Image.GetPixel(last.X + 10, last.Y + 30), FrameColor(4)), "Frame 4 of the grid is not on the second row");
            Check(grid.End == SpriteEnd.Stop && grid.FramesPerSecond == 40 && grid.DurationMs == 125 && grid.FrameAt(1000, false) == 4,
                "Grid strip end (arret), rate or held last frame");
            // d|1 : orientation 2 (F) prend la bande R retournée, comme les autres animations ponctuelles.
            Check(hit.FullName == "hitR" && hit.Mirrored && hit.Sheet.Frames == 4 && hit.Sheet.Rows == 1 && hit.Sheet.End == SpriteEnd.Static
                && hit.Sheet.DurationMs == 100 && library.Duration(10, "hit", 2) == 100, "hit strip (d|1, four frames at 40 fps, fin static)");

            var queue = new ActorAnimationQueue();
            string animation; double elapsed; bool loop;
            queue.Play(1, "hit", 0, ActorAnimationMode.Once, null, hit.Sheet);
            Check(queue.Override(1, 50, out animation, out elapsed, out loop) && animation == "hit" && elapsed == 50 && !loop, "Once animation at 50 ms");
            Check(!queue.Override(1, 100, out animation, out elapsed, out loop) && queue.Count == 0, "Once animation with fin static outlived its pass");
            queue.Play(2, "die", 0, ActorAnimationMode.Hold, null, grid);
            Check(queue.Override(2, 60000, out animation, out elapsed, out loop) && !loop && grid.FrameAt(elapsed, loop) == 4,
                "Held animation with fin arret did not keep its last frame");
            Check(!queue.Purge(60000) && queue.Count == 1, "A frozen last frame keeps the animation timer running");
            queue.Play(3, "hit", 0, ActorAnimationMode.Loop, 250, hit.Sheet);
            Check(queue.Override(3, 249, out animation, out elapsed, out loop) && loop && !queue.Override(3, 250, out animation, out elapsed, out loop),
                "Loop animation not bounded by its duration");
            queue.Play(4, "hit", 0, ActorAnimationMode.Once);
            Check(queue.Override(4, 300, out animation, out elapsed, out loop) && elapsed == 0 && !queue.Override(4, 301, out animation, out elapsed, out loop),
                "Animation whose strip never arrived not abandoned after 300 ms");
            var snapshot = new ActorSnapshot(44, ActorKind.Player, 10, 63, 3, 100, 100, false, null, null, "Synthétique", false);
            queue.AddGhost(new ActorSnapshot(45, ActorKind.Player, 10, -1, 1, 100, 100, false, null, null, "Sans cellule", false), "die", 0, 1500);
            queue.AddGhost(snapshot, "die", 0, 0);
            Check(queue.GhostCount == 0, "Ghost without a cell or already expired accepted");
            queue.AddGhost(snapshot, "die", 0, 1500);
            Check(queue.GhostsAt(1499).Count() == 1 && !queue.GhostsAt(1500).Any(), "Ghost visibility window");
            queue.Purge(1500);
            Check(queue.GhostCount == 0, "Expired ghost not purged");
            queue.Clear();
            Check(queue.Count == 0, "Clear kept animations");
        }
    }

    // ------------------------------------------------------------------ vue de la carte

    private static string EncodedCell()
    {
        // Cellule active, praticable, en ligne de vue, sans sol ni objet (aucun PNG de décor demandé).
        int[] value = { 33, 7, 32, 0, 4, 0, 0, 0, 0, 0 };
        return new string(value.Select(part => Hash.caracteres_array[part]).ToArray());
    }

    private static Bitmap Render(UserMapControl view, string name)
    {
        Check(view.WaitForActorSprites(10000), "Sprite decoding did not finish");
        var bitmap = new Bitmap(view.Width, view.Height);
        using (Graphics graphics = Graphics.FromImage(bitmap)) view.DrawCells(graphics);
        bitmap.Save(Path.Combine(TestPaths.Work, name + ".png"), ImageFormat.Png);
        return bitmap;
    }

    private static Color Center(Bitmap image, RectangleF bounds) =>
        image.GetPixel((int)(bounds.Left + bounds.Width / 2), (int)(bounds.Top + bounds.Height / 2));

    private static void View(string root, string sprites)
    {
        int count = MapHeight * (2 * MapWidth - 1) - (MapWidth - 1);
        string data = string.Concat(Enumerable.Repeat(EncodedCell(), count));
        using (var account = new Accounts(new AccountConfig("synthetic-animations", "Synthetic123", "test")))
        {
            Map map = account.Game.Map;
            map.MapID = 991302; map.MapWidth = MapWidth; map.MapHeight = MapHeight; map.MapData = data; map.DecompressMap(data);
            GmParseResult parsed = GmParser.Parse("GM"
                + "|+61;1;0;43;Alice;1;10^100;0;0,0,0,0;-1;-1;-1;;0;;;;;0;;0"
                + "|+63;3;0;44;Bruno;1;10^100;0;0,0,0,0;-1;-1;-1;;0;;;;;0;;0"
                + "|+77;2;0;45;Chloe;1;10^100;0;0,0,0,0;-1;-1;-1;;0;;;;;0;;0", false, 42, -1);
            Check(parsed.Rejected.Count == 0 && parsed.Entries.Count == 3, "Synthetic GM entries were rejected");
            foreach (GmEntry entry in parsed.Entries)
            {
                entry.Actor.Cell = map.GetCellFromId((short)entry.Actor.CellId);
                map.AddActor(entry.Actor);
            }

            double clock = 1000;
            using (var view = new UserMapControl(() => clock, sprites, Path.Combine(root, "overheads")))
            {
                view.SetAccount(account); view.W = MapWidth; view.H = MapHeight; view.Size = new Size(640, 400);
                view.SetCellNum(); view.DrawGrille(); view.RefreshMap();
                Check(view.WaitForActorSprites(10000), "Static poses not decoded");
                Check(view.Now == 1000 && view.EffectView.Now == 1000, "Injected clock not used by the view");
                UserMapControl.ActorVisualState alice = view.GetActorVisualState(43), chloe = view.GetActorVisualState(45);
                Check(alice.HasSprite && alice.Animation == "static" && alice.AnimationName == "staticR" && chloe.AnimationName == "staticF",
                    "Static poses or their full names (staticR, staticF)");

                // hit demandé avant lecture de la bande : première image quand elle est prête, puis retour à static.
                Check(!view.PlayActorAnimation(9999, "hit", ActorAnimationMode.Once), "Animation accepted for an absent actor");
                Check(view.PlayActorAnimation(43, "hit", ActorAnimationMode.Once), "hit refused for a drawn actor");
                Check(view.WaitForActorSprites(10000), "hit strip decoding did not finish");
                alice = view.GetActorVisualState(43);
                Check(alice.Animation == "hit" && alice.AnimationName == "hitR" && alice.Frame == 0 && !alice.IsMirrored && alice.HasSprite,
                    "hit does not start on frame 0: " + alice.AnimationName + " " + alice.Frame);
                clock = 1050;
                Check(view.GetActorVisualState(43).Frame == 2, "hit frame at +50 ms is not 2 (40 images/s)");
                clock = 1099;
                alice = view.GetActorVisualState(43);
                Check(alice.Animation == "hit" && alice.Frame == 3, "hit ended before its last frame");
                clock = 1101;
                alice = view.GetActorVisualState(43);
                Check(alice.Animation == "static" && alice.AnimationName == "staticR" && alice.Frame == 0 && !view.AnimationQueue.IsPlaying(43),
                    "hit did not return to static after its pass (fin static)");
                Check(!view.TickAnimations() && view.AnimationQueue.Count == 0, "Finished hit keeps the animation timer busy");

                // Bande déjà prête : première image à l'heure de la demande.
                clock = 2000;
                Check(view.PlayActorAnimation(43, "hit", ActorAnimationMode.Once) && view.AnimationQueue.IsPlaying(43, "hit"), "Second hit refused");
                Check(view.GetActorVisualState(43).Frame == 0, "Ready hit does not start on frame 0");
                clock = 2025;
                Check(view.GetActorVisualState(43).Frame == 1, "Ready hit frame at +25 ms");
                clock = 2100;
                Check(view.GetActorVisualState(43).Animation == "static", "Ready hit outlived 100 ms");

                // Orientation 2 : bande R retournée (d|1), puis staticF.
                clock = 3000;
                Check(view.PlayActorAnimation(45, "hit", ActorAnimationMode.Once), "hit refused for orientation 2");
                chloe = view.GetActorVisualState(45);
                Check(chloe.AnimationName == "hitR" && chloe.IsMirrored && chloe.Orientation == 2, "Orientation 2 does not play the mirrored R strip");
                clock = 3101;
                chloe = view.GetActorVisualState(45);
                Check(chloe.AnimationName == "staticF" && !chloe.IsMirrored, "Orientation 2 did not return to staticF");

                // Bande absente : pose ordinaire, rien n'est inventé ; refusée ensuite.
                clock = 3500;
                view.PlayActorAnimation(43, "anim7", ActorAnimationMode.Once);
                Check(view.WaitForActorSprites(10000), "anim7 lookup did not finish");
                Check(view.GetActorVisualState(43).Animation == "static" && !view.AnimationQueue.IsPlaying(43), "Missing strip did not fall back to static");
                Check(!view.PlayActorAnimation(43, "anim7", ActorAnimationMode.Once), "Known missing strip accepted");

                // suite:anim2 : la bande suivante part à la fin de la première, puis fin static.
                clock = 3600;
                Check(view.PlayActorAnimation(43, "anim1", ActorAnimationMode.Once), "anim1 refused");
                Check(view.WaitForActorSprites(10000), "anim1 decoding did not finish");
                Check(view.GetActorVisualState(43).Animation == "anim1", "anim1 not shown");
                clock = 3650;
                view.GetActorVisualState(43);
                Check(view.WaitForActorSprites(10000), "anim2 decoding did not finish");
                alice = view.GetActorVisualState(43);
                Check(alice.Animation == "anim2" && alice.Frame == 0, "suite:anim2 did not chain at the end of anim1: " + alice.Animation);
                clock = 3700;
                Check(view.GetActorVisualState(43).Animation == "static", "anim2 (fin static) did not return to static");

                Effects(view, ref clock);
                Ghost(view, map, account, ref clock);

                // Fermeture de la carte, changement de carte : tout est oublié.
                view.EnqueueVisual(1, VisualStep.Waiting("held", null));
                view.PlayActorAnimation(43, "hit", ActorAnimationMode.Loop, 5000);
                view.AddEffect(new StripEffect(view.EffectView.Sprites.ResolveFixed("Test", 5, "aura"), PointF.Empty, clock, StripPlay.Loop, 5000));
                Check(view.Sequencer.TotalCount == 1 && view.AnimationQueue.Count == 1 && view.Effects.Count == 1, "Visual work not queued");
                view.ClearVisuals();
                Check(view.Sequencer.IsIdle && view.AnimationQueue.Count == 0 && view.Effects.Count == 0, "ClearVisuals kept queued visuals");
            }
        }
    }

    private static void Effects(UserMapControl view, ref double clock)
    {
        clock = 4000;
        ActorSprites sprites = view.EffectView.Sprites;
        var onChloe = new ActorAttachedEffect(45, sprites.ResolveFixed("Test", 5, "aura"), true, clock, 1000);
        var onAlice = new ActorAttachedEffect(43, sprites.ResolveFixed("Test", 5, "aura"), true, clock, 1000);
        PointF cell;
        Check(view.EffectView.TryGetCellCenter(30, out cell) && !view.EffectView.TryGetCellCenter(5000, out cell) && view.EffectView.TryGetCellCenter(30, out cell),
            "Cell centre of the effect view");
        var boom = new StripEffect(sprites.ResolveFixed("Test", 5, "boom"), cell, clock);
        var missing = new StripEffect(sprites.ResolveFixed("Test", 6, "boom"), cell, clock);
        var invalid = sprites.ResolveFixed("../Test", 5, "boom");
        Check(invalid.State == SpriteLoadState.Missing && invalid.Reason.Contains("invalide"), "Effect family with a path accepted");
        invalid.Dispose();
        foreach (IMapEffect effect in new IMapEffect[] { onChloe, onAlice, boom, missing }) view.AddEffect(effect);
        Check(view.WaitForActorSprites(10000), "Effect strips decoding did not finish");
        Check(view.TickAnimations() && view.Effects.Count == 3 && missing.Skipped && missing.Finished, "Missing effect strip not skipped");
        Check(boom.ShownAt == 4000 && boom.Frame == 0 && onChloe.ShownAt == 4000, "Effect strips did not start when ready");
        ActorAnchor foot;
        Check(view.TryGetActorAnchor(45, out foot) && foot.Animation == "staticF" && !foot.IsGhost, "Anchor of the actor carrying the effect");
        using (Bitmap shown = Render(view, "bot-animations-effets"))
        {
            // Une seule case de chaque bande : rien de l'image suivante à droite de la case affichée.
            Func<PointF, Color> at = world => { PointF p = view.EffectView.ToScreen(world); return shown.GetPixel((int)p.X, (int)p.Y); };
            Check(Near(at(new PointF(foot.WorldFoot.X, foot.WorldFoot.Y - 10)), FrameColor(0)), "Attached effect frame 0 not drawn on the actor");
            Check(!Near(at(new PointF(foot.WorldFoot.X + 20, foot.WorldFoot.Y - 10)), FrameColor(1)), "Attached effect drew the next frames of its strip");
            Check(Near(at(new PointF(cell.X, cell.Y - 10)), FrameColor(0)) && !Near(at(new PointF(cell.X + 20, cell.Y - 10)), FrameColor(1)),
                "Strip effect not drawn as a single frame at its cell");
        }
        Check(onChloe.Visible && !onAlice.Visible, "Top clip ignores xtraClipTopAnimations (player: staticF only)");
        Check(onChloe.Order == 4 && onChloe.Depth == 77 * 100 + 30, "Attached effect not layered with its actor");
        clock = 4049;
        Check(view.TickAnimations() && boom.Frame == 1 && view.Effects.Count == 3, "Effect strip frame at +49 ms");
        clock = 4050;
        view.TickAnimations();
        Check(boom.Finished && view.Effects.Count == 2, "Once effect strip outlived its pass");
        clock = 5000;
        Check(!view.TickAnimations() && view.Effects.Count == 0 && onChloe.Finished, "Attached effect outlived its duration");
    }

    private static void Ghost(UserMapControl view, Map map, Accounts account, ref double clock)
    {
        clock = 6000;
        ActorSnapshot snapshot = ActorSnapshot.Capture(map, account.Game.character, 44);
        Check(snapshot != null && snapshot.CellId == 63 && snapshot.Gfx == 10 && snapshot.Direction == 3 && snapshot.Kind == ActorKind.Player,
            "Snapshot of the actor before its death");
        Check(map.RemoveActor(44) && view.GetActorVisualState(44) == null, "Removed actor still drawn");
        bool next = false;
        view.EnqueueVisual(44, VisualStep.Die(now => view.AddGhost(snapshot, "die", now + FightVisualSequencer.DieDuration)));
        view.EnqueueVisual(44, VisualStep.Instant("after", now => next = true));
        Check(view.AnimationQueue.GhostCount == 1 && !next && view.Sequencer.Count(44) == 2, "Die step did not start at once");
        Check(view.WaitForActorSprites(10000), "die strip decoding did not finish");
        UserMapControl.ActorVisualState ghost = view.GetActorVisualState(44);
        Check(ghost != null && ghost.IsGhost && ghost.Animation == "die" && ghost.AnimationName == "dieR" && ghost.IsMirrored && ghost.Frame == 0
            && ghost.CellId == 63 && ghost.HasSprite, "Ghost not drawn from the snapshot with its die strip");
        Check(view.HitTest(Point.Round(new PointF(ghost.SpriteBounds.Left + ghost.SpriteBounds.Width / 2, ghost.SpriteBounds.Top + ghost.SpriteBounds.Height / 2))) == null,
            "Ghost can be clicked");
        using (Bitmap first = Render(view, "bot-animations-fantome-0"))
            Check(Near(Center(first, ghost.SpriteBounds), FrameColor(0)), "Ghost frame 0 not drawn");
        // Ancre telle qu'elle a été dessinée au dernier passage (les effets accrochés suivent le fantôme).
        ActorAnchor anchor;
        Check(view.TryGetActorAnchor(44, out anchor) && anchor.IsGhost && anchor.Animation == "dieR" && anchor.CellId == 63, "Ghost anchor");
        clock = 6060;
        Check(view.GetActorVisualState(44).Frame == 2, "Ghost frame at +60 ms");
        clock = 6100;
        ghost = view.GetActorVisualState(44);
        Check(ghost.Frame == 4, "Ghost does not reach the last frame of the grid");
        using (Bitmap last = Render(view, "bot-animations-fantome-4"))
            Check(Near(Center(last, ghost.SpriteBounds), FrameColor(4)), "Second row of the grid not drawn for frame 4");
        clock = 7000;
        Check(view.GetActorVisualState(44).Frame == 4 && view.TickAnimations() && !next, "die (fin arret) did not hold its last frame");
        clock = 7499;
        view.TickAnimations();
        Check(!next && view.GetActorVisualState(44) != null, "Die step released or ghost purged before 1500 ms");
        clock = 7500;
        Check(view.GetActorVisualState(44) == null, "Ghost visible after its end");
        view.TickAnimations();
        Check(next && view.AnimationQueue.GhostCount == 0 && view.Sequencer.IsIdle, "Ghost not purged or Die not released at 1500 ms");
    }

    // ------------------------------------------------------------------ paquets GA bruts dans Fights

    private static async Task<T> Within<T>(Task<T> task)
    { if (await Task.WhenAny(task, Task.Delay(6000)) != task) throw new TimeoutException("Visual events loopback timed out"); return await task; }

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

    private static int Count(List<VisualEvent> events) { lock (events) return events.Count; }

    private static async Task<List<VisualEvent>> FightEvents()
    {
        MessagesReception.Init();
        Map.AllBotMaps[900093] = new Map { MapID = 900093, MapWidth = 3, MapHeight = 4, MapData = string.Concat(Enumerable.Repeat("HhGaeaaaaa", 18)) };
        var events = new List<VisualEvent>();
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            using (var account = new Accounts(new AccountConfig("synthetic-visual-events", "synthetic", "loopback")))
            {
                Task<Socket> accept = listener.AcceptSocketAsync();
                await account.Connexion.ConnectToServer(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
                using (Socket peer = await Within(accept))
                {
                    peer.ReceiveTimeout = 6000; account.Game.character.id = 42;
                    Fights fight = account.Game.Fight;
                    // Un abonné défaillant n'empêche ni les autres abonnés ni la lecture des paquets suivants.
                    fight.VisualEvent += visual => { throw new InvalidOperationException("abonné défaillant"); };
                    fight.VisualEvent += visual => { lock (events) events.Add(visual); };
                    await Feed(account, "GDM|900093|date|"); Check(await Within(Read(peer)) == "GI", "GDM did not request GI");
                    await Feed(account, "GM|+0;1;0;42;Synthetic;1;10^100;0");
                    await Feed(account, "GA;100;42;42,-5");
                    Check(Count(events) == 0, "Fight visual event raised outside a fight");

                    await Feed(account, "GJK2|1|1|0|30000|0");
                    await Feed(account, "GP" + Hash.Get_Cell_Char(0) + Hash.Get_Cell_Char(3) + "|" + Hash.Get_Cell_Char(9) + "|0");
                    await Feed(account, "GM|+0;1;0;42;Synthetic;1;10^100;0");
                    await Feed(account, "GS");
                    await Feed(account, "GM|+9;1;0;-7;101;-2;1100^100;1;-1;-1;-1;0,0,0,0;60;4;2;1");
                    await Feed(account, "GTM|42;0;100;8;3;0;;100|-7;0;60;4;2;9;;60");
                    await Feed(account, "GTS42|30000");
                    await Feed(account, "GA;100;42;-7,-15");
                    await Feed(account, "GA;100;-7;42,-3");
                    await Feed(account, "GA;100;42;x,y");
                    Check(Count(events) == 2 && fight.Fighters[-7].Life == 45, "Life GA events (or a malformed GA raised one)");
                    await Feed(account, "GTF42"); await Feed(account, "GTS-7|30000");
                    await Feed(account, "GA;300;-7;10,3,0,1");
                    await Feed(account, "GA;103;42;-7");
                    Check(Count(events) == 4 && fight.Fighters[-7].IsDead && fight.Fighters[-7].CellId == -1, "Spell or death GA not raised");
                }
            }
        }
        finally { listener.Stop(); }

        VisualEvent damage = events[0], reflected = events[1], spell = events[2], death = events[3];
        Check(damage.Source == VisualSource.GameAction && damage.ActionId == 100 && damage.ActorId == 42 && damage.TargetId == -7 && damage.Value == -15
            && damage.InFight && damage.QueueId == 42 && damage.Snapshot == null, "GA;100 event: " + damage);
        Check(reflected.ActorId == -7 && reflected.TargetId == 42 && reflected.Value == -3 && reflected.QueueId == 42,
            "Action of another actor is not queued on the player whose turn it is");
        int field;
        Check(spell.ActionId == 300 && spell.ActorId == -7 && spell.CellId == 3 && spell.Value == 10 && spell.QueueId == -7
            && spell.TryField(0, out field) && field == 10, "GA;300 event (spell, cell, turn queue)");
        ActorSnapshot body = death.Snapshot;
        Check(death.ActionId == 103 && death.TargetId == -7 && death.CellId == 9 && death.QueueId == -7 && body != null && body.Id == -7
            && body.CellId == 9 && body.Gfx == 1100 && body.Kind == ActorKind.FightMonster, "GA;103 death snapshot taken after the fighter left its cell");
        return events;
    }

    // ------------------------------------------------------------------ carte cachée

    private sealed class CountingEffect : IMapEffect, IDisposable
    {
        public bool Disposed;
        public EffectLayer Layer => EffectLayer.Screen;
        public int Depth => 0;
        public int Order => 0;
        public bool Update(double now, MapView view) => !Disposed;
        public void Draw(Graphics graphics, MapView view) { }
        public void Dispose() => Disposed = true;
    }

    private static void HiddenMap(List<VisualEvent> events)
    {
        double clock = 9000;
        using (var account = new Accounts(new AccountConfig("synthetic-hidden-map", "Synthetic123", "test")))
        using (var control = new MapControl(account, () => clock))
        {
            Check(control.MapSurface.Now == 9000, "MapControl(account, clock) did not hand its clock to the map");
            Check(!control.IsMapShown, "A map control never shown counts as shown");
            var routed = new List<Tuple<VisualFamily, bool>>();
            control.VisualRouted += (family, visual, shown) => routed.Add(Tuple.Create(family, shown));
            ActorSnapshot body = events[3].Snapshot;

            control.MapShown = () => false;
            bool started = false;
            var refused = new CountingEffect();
            Check(!control.TryEnqueueVisual(42, VisualStep.Instant("hidden", now => started = true)) && !started, "Step queued on a hidden map");
            Check(!control.TryAddEffect(refused) && refused.Disposed, "Effect added on a hidden map (or not released)");
            Check(!control.TryPlayAnimation(42, "hit", ActorAnimationMode.Once) && !control.TryAddGhost(body, "die", 1500), "Animation or ghost on a hidden map");
            foreach (VisualEvent visual in events) control.ShowVisual(visual);
            Check(control.Sequencer.TotalCount == 0 && control.Effects.Count == 0 && control.AnimationQueue.Count == 0, "A queue grew while the map is hidden");
            Check(routed.Select(item => item.Item1).SequenceEqual(new[] { VisualFamily.Points, VisualFamily.Points, VisualFamily.Spell, VisualFamily.Death })
                && routed.All(item => !item.Item2), "Hidden map routing: " + string.Join(",", routed.Select(item => item.Item1)));

            // Carte affichée : les files grossissent ; cachée à nouveau : un événement les vide.
            control.MapShown = () => true;
            var kept = new CountingEffect();
            Check(control.TryEnqueueVisual(42, VisualStep.Waiting("shown", null)) && control.Sequencer.TotalCount == 1, "Step refused on a shown map");
            Check(control.TryAddEffect(kept) && control.Effects.Count == 1 && control.TryAddGhost(body, "die", 1500) && control.AnimationQueue.GhostCount == 1,
                "Effect or ghost refused on a shown map");
            control.ShowVisual(events[0]);
            Check(routed.Last().Item1 == VisualFamily.Points && routed.Last().Item2, "Shown map routing");
            control.MapShown = () => false;
            control.ShowVisual(events[1]);
            Check(control.Sequencer.TotalCount == 0 && control.Effects.Count == 0 && control.AnimationQueue.Count == 0 && kept.Disposed,
                "Hiding the map did not empty its queues");
            control.MapShown = () => { throw new InvalidOperationException("condition défaillante"); };
            Check(!control.IsMapShown && !control.TryEnqueueVisual(42, VisualStep.Instant("x", null)), "A failing MapShown counts as shown");
            control.MapShown = null;
            Check(!control.IsMapShown, "Default MapShown counts a never shown control as shown");
        }
    }
}
