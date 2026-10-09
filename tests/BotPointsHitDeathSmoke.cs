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
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Controls;
using Outil_Azur_complet.Bot.Interfaces;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Combats;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Utils.Crypto;

// Chiffres, coups reçus et morts (lot AN2). Formules du clip Points (montée, échelle, 16 images), bandes hit/die livrées
// lues par ActorSprites, puis un combat sur un faux serveur en boucle locale (paquets injectés comme BotCombatUiSmoke et
// BotAnimationQueueSmoke) vu par un MapControl à horloge injectée et carte déclarée affichée, avec des sprites synthétiques
// (900 : static, hit de 4 images, die de 5 images ; 901 : static seul) : pixels témoins à t, t + 50, t + 100, t + 450 et
// t + 1 600 ms, second chiffre à t + 400 ms, option PointsOverHead, PA/PM du joueur du tour, IQ, carte cachée, GA 104,
// mort de 1 500 ms exactement, mort sans bande die, coup fatal reçu après la mise à jour du modèle. Un PNG par instant
// dans TestPaths.Work ; dessin par Graphics.FromImage : passe sous Mono.
internal static class BotPointsHitDeathSmoke
{
    private const int MapWidth = 8, MapHeight = 8;
    private const short SelfCell = 46, MonsterCell = 49, PlainCell = 52;
    private static readonly Color StaticColor = Color.FromArgb(120, 120, 120);
    private static readonly Color PlainColor = Color.FromArgb(90, 130, 160);
    private static readonly Color[] HitColors = { Color.FromArgb(230, 230, 40), Color.FromArgb(40, 200, 200), Color.FromArgb(150, 60, 220), Color.FromArgb(250, 250, 250) };
    private static readonly Color[] DieColors = { Color.FromArgb(20, 20, 20), Color.FromArgb(60, 60, 110), Color.FromArgb(100, 60, 140), Color.FromArgb(140, 140, 60), Color.FromArgb(200, 160, 120) };

    private static double clock = 10000;

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
            Formulas();
            ShippedStrips();
            Fight();
            Console.WriteLine("OK: Points clip (rise, scale, 16 frames, colors), shipped hit/die strips, red number at t+100 gone at t+450, "
                + "second number at t+400, PointsOverHead, PA/PM of the current player, IQ, hidden map, hit frames then static, GA 104, "
                + "die 1500 ms exactly, death without die strip, fatal blow after the model update");
        }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    // ------------------------------------------------------------------ formules du clip

    private static void Formulas()
    {
        Check(PointsLayer.Frames == 16 && PointsLayer.DurationMs == 400 && PointsLayer.FramesPerSecond == 40, "Points clip lasts 16 frames at 40 fps (400 ms)");
        Check(PointsLayer.LifeColor.ToArgb() == unchecked((int)0xFFFF0000) && PointsLayer.ActionPointsColor.ToArgb() == unchecked((int)0xFF0000FF)
            && PointsLayer.MovementPointsColor.ToArgb() == unchecked((int)0xFF006600) && PointsLayer.QuantityColor.ToArgb() == unchecked((int)0xFFB04600),
            "Client colors 0xFF0000, 0x0000FF, 0x006600, 0xB04600");
        // Montée : vy = -20 × 0,7^k cumulée, vers -46,7 px.
        Check(PointsLayer.RiseAt(0) == 0 && Math.Abs(PointsLayer.RiseAt(1) + 14) < 0.001 && Math.Abs(PointsLayer.RiseAt(2) + 23.8) < 0.001
            && Math.Abs(PointsLayer.RiseAt(15) + 140.0 / 3 * (1 - Math.Pow(0.7, 15))) < 0.001 && PointsLayer.RiseAt(15) > -46.7f, "Rise of the Points clip");
        // Échelle : 100 % aux images 0 et 1 (valeur encore indéfinie), puis 100 + 200·0,95^(k-2)·sin(0,25·(k-1)).
        Check(PointsLayer.ScaleAt(0) == 100 && PointsLayer.ScaleAt(1) == 100, "Scale is applied one frame late");
        for (int k = 2; k < 16; k++)
            Check(Math.Abs(PointsLayer.ScaleAt(k) - (100 + 200 * Math.Pow(0.95, k - 2) * Math.Sin(0.25 * (k - 1)))) < 0.01, "Scale at frame " + k);
        Check(PointsLayer.ScaleAt(6) > 254 && PointsLayer.ScaleAt(6) < 255 && PointsLayer.ScaleAt(15) < 70, "Scale peak (254 %) and end (64 %)");

        // File sans vue : un acteur introuvable abandonne son chiffre après 300 ms ; file bornée ; texte borné.
        using (var layer = new PointsLayer())
        {
            Check(layer.Layer == EffectLayer.Screen, "Points are not drawn in screen space after the overheads");
            Check(layer.Add(5, "-12", PointsLayer.LifeColor, 0) && !layer.Add(5, "", PointsLayer.LifeColor, 0), "Empty text accepted");
            Check(layer.Update(300, null) && layer.Count == 1 && !layer.Update(301, null) && layer.Count == 0, "Number of an absent actor kept beyond 300 ms");
            for (int i = 0; i < PointsLayer.MaxQueuedPerActor; i++) layer.Add(6, "1", PointsLayer.LifeColor, 0);
            Check(!layer.Add(6, "1", PointsLayer.LifeColor, 0) && layer.Count == PointsLayer.MaxQueuedPerActor, "Queue of one actor not bounded");
            layer.Add(7, new string('9', 40), PointsLayer.LifeColor, 0);
            Check(layer.Numbers.Single(n => n.ActorId == 7).Text.Length == PointsLayer.MaxTextLength, "Text not bounded");
            layer.Dispose();
            Check(layer.IsDisposed && layer.Count == 0 && !layer.Add(5, "1", PointsLayer.LifeColor, 0) && !layer.Update(0, null), "Disposed layer still works");
        }
    }

    /// <summary>Bandes hit/die livrées (classes) lues par le chargeur du bot : 23 images (durée swfsvg 0.2.5, lot AN6), fin static ; die tenu (arret).</summary>
    private static void ShippedStrips()
    {
        string shipped = Path.Combine(TestPaths.ApplicationBin, "ressources", "Bot", "sprites");
        Check(File.Exists(Path.Combine(shipped, "10_hitR.png")), "Shipped hit strips missing next to the executable: " + shipped);
        using (var library = new ActorSprites(shipped))
        {
            foreach (int direction in new[] { 1, 2, 5 }) { library.Resolve(10, direction, false, "hit"); library.Resolve(10, direction, false, "die"); }
            Check(library.WaitForPending(20000), "Shipped strips decoding did not finish");
            SpritePose hit = library.Resolve(10, 1, false, "hit"), hitF = library.Resolve(10, 2, false, "hit"), hitL = library.Resolve(10, 5, false, "hit");
            SpritePose die = library.Resolve(10, 1, false, "die");
            Check(hit.State == SpriteLoadState.Ready && hit.FullName == "hitR" && hit.Sheet.Frames == 23 && hit.Sheet.End == SpriteEnd.Static
                && hit.Sheet.DurationMs == 575, "Shipped 10_hitR: 23 frames, 575 ms, fin static");
            Check(hitF.FullName == "hitR" && hitF.Mirrored && hitL.FullName == "hitL" && !hitL.Mirrored, "Direction 2 plays hitR mirrored (d|1), direction 5 hitL");
            Check(die.State == SpriteLoadState.Ready && die.Sheet.Frames == 79 && die.Sheet.End == SpriteEnd.Stop && library.Duration(10, "die", 1) == 1975,
                "Shipped 10_dieR: 79 frames (1 975 ms, cut at 1 500 ms by the Die step), fin arret");
        }
    }

    // ------------------------------------------------------------------ sprites synthétiques

    private static void Strip(string path, Color[] colors, int width, int height)
    {
        using (var image = new Bitmap(colors.Length * width, height, PixelFormat.Format32bppArgb))
        {
            using (Graphics graphics = Graphics.FromImage(image))
            {
                graphics.Clear(Color.Transparent);
                for (int k = 0; k < colors.Length; k++)
                    using (var brush = new SolidBrush(colors[k])) graphics.FillRectangle(brush, k * width, 0, width, height);
            }
            image.Save(path, ImageFormat.Png);
        }
    }

    private static string Assets()
    {
        string sprites = Path.Combine(TestPaths.Work, "points-hit-death", "sprites");
        if (Directory.Exists(sprites)) Directory.Delete(sprites, true);
        Directory.CreateDirectory(sprites);
        foreach (string o in new[] { "R", "L" })
        {
            Strip(Path.Combine(sprites, "900_static" + o + ".png"), new[] { StaticColor }, 20, 60);
            Strip(Path.Combine(sprites, "901_static" + o + ".png"), new[] { PlainColor }, 20, 60);
        }
        Strip(Path.Combine(sprites, "900_hitR.png"), HitColors, 20, 60);
        Strip(Path.Combine(sprites, "900_dieR.png"), DieColors, 20, 60);
        File.WriteAllLines(Path.Combine(sprites, "ancres.tsv"), new[]
        {
            "gfx\tanim\txmin\tymin\tlargeur\thauteur\timages\tips\tfin",
            "900\tdieR\t-10\t-60\t20\t60\t5\t40\tarret",
            "900\thitR\t-10\t-60\t20\t60\t4\t40\tstatic",
            "900\tstaticL\t-10\t-60\t20\t60\t1\t40\tarret",
            "900\tstaticR\t-10\t-60\t20\t60\t1\t40\tarret",
            "901\tstaticL\t-10\t-60\t20\t60\t1\t40\tarret",
            "901\tstaticR\t-10\t-60\t20\t60\t1\t40\tarret"
        });
        return sprites;
    }

    // ------------------------------------------------------------------ outils

    private static void PumpUntil(Func<bool> done)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(10);
        while (!done()) { if (DateTime.UtcNow > end) throw new TimeoutException("Points loopback timed out"); Application.DoEvents(); Thread.Sleep(5); }
        Application.DoEvents();
    }

    private static void Complete(Task task) { PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }

    private static void Feed(Accounts account, string packet) { Complete(MessagesReception.ReceptionAsync(account.Connexion, packet)); }

    private static string Read(Socket socket)
    {
        PumpUntil(() => socket.Available > 0);
        using (var stream = new MemoryStream())
        {
            byte[] one = new byte[1];
            while (true)
            {
                if (socket.Receive(one) != 1) throw new IOException("Loopback disconnected");
                if (one[0] == 0) return Encoding.UTF8.GetString(stream.ToArray()).TrimEnd('\r', '\n');
                stream.WriteByte(one[0]);
            }
        }
    }

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

    private static bool Near(Color pixel, Color expected) =>
        Math.Abs(pixel.R - expected.R) <= 12 && Math.Abs(pixel.G - expected.G) <= 12 && Math.Abs(pixel.B - expected.B) <= 12;

    private static Color Center(Bitmap image, RectangleF bounds) =>
        image.GetPixel((int)(bounds.Left + bounds.Width / 2), (int)(bounds.Top + bounds.Height / 2));

    private static bool IsRed(Color c) => c.R >= 200 && c.G <= 60 && c.B <= 60;
    private static bool IsBlue(Color c) => c.B >= 200 && c.R <= 60 && c.G <= 60;
    private static bool IsGreen(Color c) => c.G >= 80 && c.G <= 140 && c.R <= 40 && c.B <= 40;
    private static bool IsOrange(Color c) => c.R >= 140 && c.R <= 210 && c.G >= 40 && c.G <= 100 && c.B <= 40;

    /// <summary>Pixels de <paramref name="image"/> qui vérifient <paramref name="match"/> dans un rectangle du repère de la carte.</summary>
    private static int Count(Bitmap image, UserMapControl view, RectangleF world, Func<Color, bool> match)
    {
        RectangleF screen = view.EffectView.ToScreen(world);
        int count = 0;
        for (int y = Math.Max(0, (int)screen.Top); y < Math.Min(image.Height, (int)Math.Ceiling(screen.Bottom)); y++)
            for (int x = Math.Max(0, (int)screen.Left); x < Math.Min(image.Width, (int)Math.Ceiling(screen.Right)); x++)
                if (match(image.GetPixel(x, y))) count++;
        return count;
    }

    /// <summary>Bande au-dessus de la tête : [pied − 50 − 47, pied − 50] sur 100 px de large (§ 3.1 du plan).</summary>
    private static RectangleF Above(PointF foot) => new RectangleF(foot.X - 50, foot.Y - 50 - 47, 100, 47);

    /// <summary>Tout l'espace où un chiffre peut apparaître : du pied − 110 au pied − 25.</summary>
    private static RectangleF Overhead(PointF foot) => new RectangleF(foot.X - 60, foot.Y - 110, 120, 85);

    private static PointF Foot(UserMapControl view, long id)
    {
        ActorAnchor anchor;
        Check(view.TryGetActorAnchor(id, out anchor), "Actor " + id + " is not drawn");
        return anchor.WorldFoot;
    }

    private static UserMapControl.ActorVisualState State(UserMapControl view, int id) => view.GetActorVisualState(id);

    private static void At(MapControl control, double time)
    {
        clock = time;
        control.MapSurface.TickAnimations();
    }

    // ------------------------------------------------------------------ combat en boucle locale

    private static void Fight()
    {
        string sprites = Assets();
        int count = MapHeight * (2 * MapWidth - 1) - (MapWidth - 1);
        MessagesReception.Init();
        Map.AllBotMaps[900095] = new Map { MapID = 900095, MapWidth = MapWidth, MapHeight = MapHeight, MapData = string.Concat(Enumerable.Repeat(EncodedCell(), count)) };
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            using (var account = new Accounts(new AccountConfig("synthetic-points", "synthetic", "loopback")))
            {
                Task<Socket> accept = listener.AcceptSocketAsync();
                Complete(account.Connexion.ConnectToServer(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port)); Complete(accept);
                using (Socket peer = accept.Result)
                {
                    peer.ReceiveTimeout = 6000; account.Game.character.id = 42;
                    Feed(account, "GDM|900095|date|"); Check(Read(peer) == "GI", "GDM did not request GI");
                    using (var control = new MapControl(account, () => clock))
                    {
                        UserMapControl view = control.MapSurface;
                        // Sprites synthétiques à la place de ressources/Bot/sprites (seul le champ de la vue change).
                        FieldInfo field = typeof(UserMapControl).GetField("sprites", BindingFlags.Instance | BindingFlags.NonPublic);
                        var shipped = (ActorSprites)field.GetValue(view);
                        field.SetValue(view, new ActorSprites(sprites));
                        shipped.Dispose();
                        var options = new BotOptions(Path.Combine(TestPaths.Work, "points-hit-death", "BotOptions.json")) { AutoSave = false };
                        control.Options = options;
                        control.MapShown = () => true;
                        control.Size = new Size(640, 400); view.Size = new Size(640, 400);
                        // Abonnements de MapControl_Load (la carte n'est pas dans une fenêtre affichée).
                        typeof(UserControl).GetMethod("OnLoad", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(control, new object[] { EventArgs.Empty });

                        Feed(account, "GJK2|1|1|0|30000|0");
                        Feed(account, "GP" + Hash.Get_Cell_Char(SelfCell) + "|" + Hash.Get_Cell_Char(MonsterCell) + Hash.Get_Cell_Char(PlainCell) + "|0");
                        Feed(account, "GM|+" + SelfCell + ";1;0;42;Synthetic;1;900^100;0");
                        Feed(account, "GS");
                        Feed(account, "GM|+" + MonsterCell + ";1;0;-7;101;-2;900^100;1;-1;-1;-1;0,0,0,0;60;4;2;1"
                            + "|+" + PlainCell + ";1;0;-8;102;-2;901^100;1;-1;-1;-1;0,0,0,0;60;4;2;1");
                        Feed(account, "GTM|42;0;100;8;3;" + SelfCell + ";;100|-7;0;60;4;2;" + MonsterCell + ";;60|-8;0;60;4;2;" + PlainCell + ";;60");
                        Feed(account, "GTS42|30000");
                        Check(account.Game.Fight.IsInFight && account.Game.Fight.CurrentActorId == 42, "Synthetic fight did not start on the player's turn");
                        Scenario(account, control, view);
                    }
                }
            }
        }
        finally { listener.Stop(); }
    }

    private static void Scenario(Accounts account, MapControl control, UserMapControl view)
    {
        At(control, 10000);
        using (Bitmap baseline = Render(view, "bot-points-0-base"))
        {
            Check(State(view, -7).HasSprite && State(view, -7).AnimationName == "staticR" && State(view, -8).AnimationName == "staticR",
                "Synthetic monsters not drawn with their static pose");
            foreach (long id in new long[] { 42, -7, -8 })
            {
                RectangleF area = Overhead(Foot(view, id));
                Check(Count(baseline, view, area, c => IsRed(c) || IsBlue(c) || IsGreen(c) || IsOrange(c)) == 0,
                    "The map already has number-colored pixels above actor " + id);
            }
        }
        PointF monster = Foot(view, -7), plain = Foot(view, -8), self = Foot(view, 42);

        // GA;100 : chiffre rouge et hit ; image 0 à t, image 2 à t + 50, static à t + 101 ; chiffre parti à t + 450.
        var failures = new List<string>();
        control.Sequencer.StepFailed += (step, error) => failures.Add(step.Name + ": " + error);
        control.Effects.EffectFailed += (effect, error) => failures.Add(effect.GetType().Name + ": " + error);
        var routed = new List<string>();
        control.VisualRouted += (family, visual, shown) => routed.Add(family + (shown ? "" : " (cachée)"));
        Feed(account, "GA;100;42;-7,-12");
        Check(routed.Count == 1 && routed[0] == "Points", "GA;100 not routed to Points on a shown map: " + string.Join(",", routed));
        Check(failures.Count == 0, "Visual step or effect failed: " + string.Join(" | ", failures));
        Check(control.Points != null && control.Points.Numbers.Count == 1, "GA;100 did not add a number (steps " + control.Sequencer.TotalCount
            + ", effects " + control.Effects.Count + ")");
        PointsNumber number = control.Points.Numbers[0];
        Check(number.ActorId == -7 && number.Text == "-12" && number.Color == PointsLayer.LifeColor && number.Added == 10000,
            "Number of GA;100 (text String(v), red, added at once): " + number.Text);
        view.TickAnimations();
        Check(number.Start == 10000 && number.FrameAt(10000) == 0, "Number did not start at once on its actor's empty queue");
        Check(number.World.HasValue && Math.Abs(number.World.Value.X - monster.X) < 0.01 && Math.Abs(number.World.Value.Y - (monster.Y - 50)) < 0.01,
            "Number does not start 50 px above the foot");
        using (Bitmap image = Render(view, "bot-points-1-t"))
        {
            UserMapControl.ActorVisualState hit = State(view, -7);
            Check(hit.Animation == "hit" && hit.AnimationName == "hitR" && hit.Frame == 0 && Near(Center(image, hit.SpriteBounds), HitColors[0]),
                "hit frame 0 not drawn at t: " + hit.AnimationName + " " + hit.Frame);
            Check(Count(image, view, new RectangleF(monster.X - 30, monster.Y - 62, 60, 24), IsRed) > 0, "Number not drawn at frame 0 (foot - 50)");
        }
        At(control, 10050);
        using (Bitmap image = Render(view, "bot-points-2-t50"))
            Check(State(view, -7).Frame == 2 && Near(Center(image, State(view, -7).SpriteBounds), HitColors[2]), "hit frame 2 not drawn at t + 50");
        At(control, 10100);
        using (Bitmap image = Render(view, "bot-points-3-t100"))
        {
            int red = Count(image, view, Above(monster), IsRed);
            Check(red >= 20, "Too few red pixels above the actor at t + 100: " + red);
            Check(Count(image, view, Overhead(self), IsRed) == 0 && Count(image, view, Overhead(plain), IsRed) == 0, "Number drawn above another actor");
        }
        At(control, 10101);
        Check(State(view, -7).AnimationName == "staticR", "hit (fin static) did not return to static after 100 ms");
        At(control, 10450);
        using (Bitmap image = Render(view, "bot-points-4-t450"))
            Check(Count(image, view, Overhead(monster), IsRed) == 0 && control.Points == null, "Number still drawn at t + 450 (16 frames = 400 ms)");

        // Deux GA;100 de suite : le second part à t + 400, toujours au même endroit.
        At(control, 11000);
        Feed(account, "GA;100;42;-7,-3");
        Feed(account, "GA;100;42;-7,-4");
        PointsNumber first = control.Points.Numbers[0], second = control.Points.Numbers[1];
        Check(first.Text == "-3" && second.Text == "-4" && first.Start == 11000 && !second.Start.HasValue, "Second number did not wait for the first");
        At(control, 11399);
        Check(control.Points.Numbers.Count == 2 && !second.Start.HasValue && first.FrameAt(clock) == 15, "First number ended before 400 ms");
        At(control, 11400);
        Check(control.Points.Numbers.Count == 1 && second.Start == 11400 && second.FrameAt(clock) == 0, "Second number did not start at t + 400");
        At(control, 11410);
        using (Bitmap image = Render(view, "bot-points-5-second"))
            Check(Count(image, view, new RectangleF(monster.X - 30, monster.Y - 62, 60, 24), IsRed) > 0
                && Count(image, view, new RectangleF(monster.X - 60, monster.Y - 110, 120, 40), IsRed) == 0, "Second number not drawn at its start position");
        At(control, 12000);
        Check(control.Points == null, "Numbers left after both ended");

        // PointsOverHead décoché : aucun chiffre, le coup reçu reste joué.
        control.Options.PointsOverHead = false;
        Feed(account, "GA;100;42;-7,-9");
        Check(control.Points == null && State(view, -7).AnimationName == "hitR", "PointsOverHead off: a number was added or hit was skipped");
        At(control, 12100);
        using (Bitmap image = Render(view, "bot-points-6-option"))
            Check(Count(image, view, Overhead(monster), IsRed) == 0, "Red pixels with PointsOverHead off");
        control.Options.PointsOverHead = true;

        // PA, PM, IQ : couleurs, joueur du tour pour 102 et 129, variation nulle ignorée, une file par acteur.
        At(control, 13000);
        foreach (string packet in new[] { "GA;101;42;-7,-2", "GA;102;42;-7,-3", "GA;102;42;42,-3", "GA;102;42;42,-0", "GA;129;42;-7,-1",
            "GA;129;42;42,-1", "GA;127;42;-8,-1", "GA;100;42;-8,0" })
            Feed(account, packet);
        Feed(account, "IQ-8|3");
        Func<long, string> queue = id => string.Join(" ", control.Points.Numbers.Where(n => n.ActorId == id).Select(n => n.Text + "/" + n.Color.ToArgb().ToString("X8")));
        Check(queue(-7) == "-2/FF0000FF" && queue(42) == "-3/FF0000FF -1/FF006600" && queue(-8) == "-1/FF006600 3/FFB04600",
            "Numbers per actor: -7 [" + queue(-7) + "] 42 [" + queue(42) + "] -8 [" + queue(-8) + "]");
        At(control, 13100);
        using (Bitmap image = Render(view, "bot-points-7-pa-pm"))
            Check(Count(image, view, Above(monster), IsBlue) >= 20 && Count(image, view, Above(self), IsBlue) >= 20 && Count(image, view, Above(plain), IsGreen) >= 20,
                "PA (blue) or PM (green) numbers not drawn");
        At(control, 13500);
        using (Bitmap image = Render(view, "bot-points-8-iq"))
            Check(Count(image, view, Above(self), IsGreen) >= 20 && Count(image, view, Above(plain), IsOrange) >= 20, "Queued PM or IQ (0xB04600) number not drawn");

        // Carte cachée : rien n'est retenu.
        At(control, 14000);
        control.MapShown = () => false;
        Feed(account, "GA;100;42;-7,-5");
        Check(control.Points == null && control.Sequencer.IsIdle && control.AnimationQueue.Count == 0, "Hidden map kept a number or a hit");
        control.MapShown = () => true;

        // GA 104 : hit sur l'acteur ; gfx sans bande hit : pose inchangée, le chiffre reste.
        At(control, 14500);
        Feed(account, "GA;104;-7");
        Check(State(view, -7).AnimationName == "hitR" && State(view, -7).Frame == 0, "GA;104 did not play hit");
        Feed(account, "GA;100;42;-8,-5");
        Check(State(view, -8).AnimationName == "staticR" && !control.AnimationQueue.IsPlaying(-8), "hit invented for a gfx without hit strip");
        Check(control.Points.Numbers.Any(n => n.ActorId == -8 && n.Text == "-5"), "Number missing for the gfx without hit strip");
        At(control, 15000);

        // Mort : IsDead dès le paquet, die visible à t + 100 sur l'ancienne cellule, 1 500 ms exactement, puis le sol.
        At(control, 16000);
        Feed(account, "GA;103;42;-7");
        Check(account.Game.Fight.Fighters[-7].IsDead && account.Game.Fight.Fighters[-7].CellId == -1, "Model does not mark the fighter dead at once");
        Check(control.Sequencer.Count(42) == 1 && control.AnimationQueue.GhostCount == 1, "Die step or ghost missing");
        // Première image du fantôme : quand la bande die est prête (lue ici à l'heure du paquet).
        Check(view.WaitForActorSprites(10000) && State(view, -7).AnimationName == "dieR" && State(view, -7).Frame == 0, "die does not start on frame 0");
        At(control, 16100);
        using (Bitmap image = Render(view, "bot-points-9-die-t100"))
        {
            UserMapControl.ActorVisualState ghost = State(view, -7);
            Check(ghost != null && ghost.IsGhost && ghost.AnimationName == "dieR" && ghost.Frame == 4 && ghost.CellId == MonsterCell
                && Near(Center(image, ghost.SpriteBounds), DieColors[4]), "die frame 4 not drawn at the old cell at t + 100");
        }
        At(control, 17499);
        Check(State(view, -7) != null && State(view, -7).Frame == 4 && control.Sequencer.Count(42) == 1, "Die ended before 1 500 ms");
        At(control, 17500);
        Check(State(view, -7) == null && control.Sequencer.IsIdle && control.AnimationQueue.GhostCount == 0, "Die not over at exactly 1 500 ms");
        At(control, 17600);
        using (Bitmap image = Render(view, "bot-points-10-die-t1600"))
        {
            RectangleF body = view.EffectView.ToScreen(new RectangleF(monster.X - 10, monster.Y - 60, 20, 60));
            Color at = Center(image, body), ground = Center(image, new RectangleF(body.X - (plain.X - monster.X) / 3 * view.EffectView.Scale, body.Y, body.Width, body.Height));
            Check(!Near(at, DieColors[4]) && !Near(at, StaticColor), "Something still drawn at the dead actor's cell at t + 1 600");
            Check(Near(at, ground), "Dead actor's cell is not back to the ground color: " + at + " vs " + ground);
        }

        // Coup fatal : GA;100 puis GA;103 traités sur le fil réseau avant que la carte ne les voie (cas réel) ;
        // le chiffre apparaît tout de même au-dessus du corps, et le gfx sans bande die garde static 300 ms.
        At(control, 18000);
        Task.Run(() =>
        {
            MessagesReception.ReceptionAsync(account.Connexion, "GA;100;42;-8,-60").GetAwaiter().GetResult();
            MessagesReception.ReceptionAsync(account.Connexion, "GA;103;42;-8").GetAwaiter().GetResult();
        }).GetAwaiter().GetResult();
        Check(account.Game.Fight.Fighters[-8].IsDead, "Fatal blow not applied by the model");
        PumpUntil(() => control.Points != null && control.AnimationQueue.GhostCount == 1 && control.Sequencer.Count(42) == 1);
        PointsNumber fatal = control.Points.Numbers.Single();
        view.TickAnimations();
        Check(fatal.Text == "-60" && fatal.World.HasValue && Math.Abs(fatal.World.Value.Y - (plain.Y - 50)) < 0.01 && Math.Abs(fatal.World.Value.X - plain.X) < 0.01,
            "Fatal blow number lost or misplaced");
        At(control, 18100);
        using (Bitmap image = Render(view, "bot-points-11-fatal"))
        {
            UserMapControl.ActorVisualState ghost = State(view, -8);
            Check(ghost != null && ghost.IsGhost && ghost.AnimationName == "staticR" && Near(Center(image, ghost.SpriteBounds), PlainColor),
                "Death without die strip does not keep static");
            Check(Count(image, view, Above(plain), IsRed) >= 20, "Fatal blow number not drawn above the body");
        }
        At(control, 18300);
        Check(State(view, -8) == null && control.Sequencer.Count(42) == 1, "Static ghost outlived 300 ms or the Die step was shortened");
        At(control, 19500);
        Check(control.Sequencer.IsIdle && control.Points == null, "Die step without strip did not last 1 500 ms");

        // Événements mal formés ou inconnus : rien ne casse.
        Feed(account, "GA;100;42;x,y");
        Feed(account, "GA;103;42;");
        control.ShowVisual(new VisualEvent(VisualSource.GameAction, 100, 42, 999, -1, null, int.MinValue));
        control.ShowVisual(new VisualEvent(VisualSource.Quantity, 0, 999, 999, -1, new[] { "999" }, 0));
        control.ShowVisual(new VisualEvent(VisualSource.GameAction, 103, 42, 999, -1, null));
        Check(control.Points != null && control.Points.Numbers.All(n => n.ActorId == 999) && control.Points.Numbers.First().Text == int.MinValue.ToString(),
            "Number of an unknown actor not queued as text");
        At(control, 19801);
        Check(control.Points == null && control.AnimationQueue.GhostCount == 0, "Number of an unknown actor not dropped after 300 ms");
    }
}
