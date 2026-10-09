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
using Tool_BotProtocol.Utils.Crypto;

// Projectiles des sorts (lot AN5). Géométrie pure comparée aux formules du client 1.34 (onLoadInit) : parabole des types
// 30 et 31 pour (dx, dy) = (106, −27) à ±0,5 px image par image, appel d'arrivée, retournements, même position ; traînée
// des types 40 et 41 (5 duplicate pour D = 100 px, fin au 11e appel, D < 10, D = 0) ; angle de rotate (45°). Puis des
// clips dessinés dans un Bitmap avec des bandes synthétiques (dossier temporaire) : move à mi-course (pixel témoin à
// ±2 px), shoot à l'arrivée et fin du clip, clip retourné, rotate tourné de 45° autour du centre de son cadre, traînée de
// 41 (shoot à la fin, à la place du duplicate 10), 40 sans shoot, 50 et 51 à la cellule, gfx absent (l'étape suit quand
// même la géométrie), index partagé avec les scènes. Enfin une carte sur un faux serveur en boucle locale (horloge
// injectée) : GA300 de type 30 dessiné à mi-course sur la carte et dont l'étape bloquante retarde un GA100 jusqu'à
// l'arrivée, type 20 non bloquant, carte cachée. Dessin par Graphics.FromImage : passe sous Mono.
internal static class BotSpellProjectilesSmoke
{
    private const int MapWidth = 8, MapHeight = 8;
    private const short SelfCell = 46, MonsterCell = 49;

    private static readonly Color SelfColor = Color.FromArgb(120, 120, 120), MonsterColor = Color.FromArgb(120, 60, 30);
    private static readonly Color MoveColor = Color.FromArgb(0, 200, 255), ShootColor = Color.FromArgb(255, 60, 60);
    private static readonly Color RotateColor = Color.FromArgb(40, 220, 40), TurnedShootColor = Color.FromArgb(250, 210, 0);
    private static readonly Color DuplicateColor = Color.FromArgb(200, 0, 200), TrailShootColor = Color.FromArgb(255, 150, 0);
    private static readonly Color OnceDuplicateColor = Color.FromArgb(0, 120, 255), SceneColor = Color.FromArgb(0, 160, 120);
    private static readonly Color CellShootColor = Color.FromArgb(150, 255, 150);

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
            Geometry();
            ShippedProjectiles();
            string sprites = Assets();
            Clips(sprites);
            RunMap(sprites);
            Console.WriteLine("OK: arc of types 30/31 for (106,-27) within 0.5 px (arrival at call 15 and 7), mirrors, same position, trail of 40/41 "
                + "(5 duplicates for D = 100, end at call 11), rotate at 45 degrees, move drawn mid-flight within 2 px, shoot at arrival, clip lifetime, "
                + "rotate turned around its frame centre, shoot replacing duplicate 10, types 40/50/51, missing gfx, shared index, GA300 type 30 "
                + "on the map holding GA100 until arrival, type 20 not blocking, hidden map");
        }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    private static bool Close(double a, double b, double tolerance) => Math.Abs(a - b) <= tolerance;

    // ------------------------------------------------------------------ géométrie pure

    /// <summary>Formules du plan (§ 3.6), en forme fermée : état de move après n appels (t = (n − 1) · 0,675 / 2).</summary>
    private static void Reference(double dx, double dy, int type, int n, out double x, out double y, out double rotation, out bool arrived)
    {
        double f = type == 31 ? 0.9 : 0.5, step = 0.675 / 2, g = 9.81;
        double a = (Math.Atan2(dy, Math.Abs(dx)) + Math.PI / 2) * f - Math.PI / 2;
        double destX = Math.Abs(dx), destY = dx == 0 && dy < 0 ? -dy : dy;
        double vx = Math.Sqrt(Math.Abs(g / 2 * destX * destX / Math.Abs(destY - Math.Tan(a) * destX))), vy = Math.Tan(a) * vx;
        double t = (n - 1) * step;
        x = t * vx; y = g / 2 * t * t + vy * t;
        rotation = Math.Atan((vy + g * t) / vx) * 180 / Math.PI;
        arrived = (Math.Abs(y) >= Math.Abs(destY) && x >= destX) || x > destX;
    }

    private static int ReferenceArrival(double dx, double dy, int type)
    {
        for (int n = 1; n <= 800; n++)
        {
            Reference(dx, dy, type, n, out double _, out double _, out double _, out bool arrived);
            if (arrived) return n;
        }
        return 0;
    }

    private static void Geometry()
    {
        foreach (int type in new[] { 20, 21, 30, 31, 40, 41, 50, 51 }) Check(SpellProjectiles.IsProjectile(type) && SpellProjectiles.Animations(type).Count > 0, "Projectile type " + type);
        foreach (int type in new[] { 0, 10, 11, 12, 33, 52 }) Check(!SpellProjectiles.IsProjectile(type) && SpellProjectiles.Animations(type).Count == 0, "Type " + type + " is not a projectile");
        Check(SpellProjectiles.Animations(21).SequenceEqual(new[] { "scene", "rotate", "shoot" }) && SpellProjectiles.Animations(30).SequenceEqual(new[] { "scene", "move", "shoot" })
            && SpellProjectiles.Animations(40).SequenceEqual(new[] { "scene", "duplicate" }) && SpellProjectiles.Animations(41).SequenceEqual(new[] { "scene", "duplicate", "shoot" }),
            "Strips used by projectile types");
        Check(SpellProjectiles.Calls(0) == 0 && SpellProjectiles.Calls(24.9) == 0 && SpellProjectiles.Calls(25) == 1 && SpellProjectiles.Calls(375) == 15
            && SpellProjectiles.Calls(-5) == 0 && SpellProjectiles.Calls(double.NaN) == 0 && SpellProjectiles.Calls(1e9) == 800, "onEnterFrame calls at 40 per second");

        // Parabole du type 30 vers (106, −27) : position et rotation de move à chaque image, comparées aux formules.
        var arc = new ProjectileArc(106, -27, 30);
        Check(arc.Factor == 0.5 && Close(arc.StartAngle, (Math.Atan2(-27, 106) + Math.PI / 2) * 0.5 - Math.PI / 2, 1e-12) && !arc.MirrorX && !arc.MirrorY
            && arc.DestX == 106 && arc.DestY == -27, "Type 30 start angle or destination");
        Check(arc.TryMove(0, out double x0, out double y0, out double r0) && x0 == 0 && y0 == 0 && r0 == 0, "move must sit at the clip origin before the first call");
        int arrival = ReferenceArrival(106, -27, 30);
        Check(arrival == 15 && arc.ArrivalCall == 15, "Type 30 to (106,-27) must arrive at call 15 (375 ms): " + arc.ArrivalCall + " / " + arrival);
        for (int n = 1; n < arc.ArrivalCall; n++)
        {
            Reference(106, -27, 30, n, out double rx, out double ry, out double rr, out bool _);
            Check(arc.TryMove(n, out double ax, out double ay, out double ar) && Close(ax, rx, 0.5) && Close(ay, ry, 0.5) && Close(ar, rr, 0.5),
                "move at frame " + n + ": (" + ax + ", " + ay + ", " + ar + "°) instead of (" + rx + ", " + ry + ", " + rr + "°)");
        }
        Check(arc.TryMove(10, out double x10, out double y10, out double _) && Close(x10, 68.181, 0.5) && Close(y10, -42.469, 0.5), "move at frame 10: " + x10 + ", " + y10);
        Check(!arc.TryMove(15, out double _, out double _, out double _) && Close(arc.ArrivalRotation, 37.896, 0.5), "move not replaced by shoot at call 15, or shoot rotation " + arc.ArrivalRotation);
        var flat = new ProjectileArc(106, -27, 31);
        Check(flat.Factor == 0.9 && flat.ArrivalCall == ReferenceArrival(106, -27, 31) && flat.ArrivalCall == 7, "Type 31 to (106,-27) must arrive at call 7: " + flat.ArrivalCall);
        Reference(106, -27, 31, 5, out double fx, out double fy, out double _, out bool _);
        Check(flat.TryMove(5, out double ox, out double oy, out double _) && Close(ox, fx, 0.5) && Close(oy, fy, 0.5), "Type 31 move at frame 5");

        // dx ≤ 0 : clip retourné horizontalement, même trajectoire ; dx = 0 et dy < 0 : retourné aussi verticalement.
        var back = new ProjectileArc(-106, -27, 30);
        Check(back.MirrorX && !back.MirrorY && back.DestX == 106 && back.ArrivalCall == 15, "Arc towards dx < 0 is not mirrored");
        var up = new ProjectileArc(0, -50, 30);
        Check(up.MirrorX && up.MirrorY && up.DestY == 50 && up.ArrivalCall == ReferenceArrival(0, -50, 30) && up.ArrivalCall > 0, "Vertical arc (dx = 0, dy < 0): " + up.ArrivalCall);
        // Même position : NaN dans le client ; ses comparaisons (!(NaN < y)) donnent l'arrivée au premier appel.
        var same = new ProjectileArc(0, 0, 30);
        Check(same.ArrivalCall == 1 && double.IsNaN(same.ArrivalRotation) && same.MirrorX, "Arc to the caster's own position: " + same.ArrivalCall);

        // Traînée : D = 100 px, interval 10, duplicate aux appels 1, 3, 5, 7, 9 (10, 30, 50, 70, 90 px), fin au 11e.
        var trail = new ProjectileTrail(60, 80);
        Check(trail.Distance == 100 && trail.Interval == 10 && trail.Count == 5 && trail.EndCall == 11, "Trail for D = 100: " + trail.Count + " duplicates, end at " + trail.EndCall);
        for (int i = 0; i < 5; i++)
        {
            double dist = 10 + 20 * i;
            Check(Close(trail.Offset(i).X, dist * 0.6, 0.01) && Close(trail.Offset(i).Y, dist * 0.8, 0.01) && ProjectileTrail.AttachCall(i) == 2 * i + 1, "Duplicate " + i + " at " + trail.Offset(i));
        }
        Check(trail.AttachedAfter(0) == 0 && trail.AttachedAfter(1) == 1 && trail.AttachedAfter(2) == 1 && trail.AttachedAfter(3) == 2 && trail.AttachedAfter(9) == 5
            && trail.AttachedAfter(40) == 5, "Duplicates attached after n calls");
        Check(new ProjectileTrail(100, 0).Count == 5 && new ProjectileTrail(-100, 0).Count == 5, "D = 100 along the x axis");
        var shortTrail = new ProjectileTrail(5, 0);
        Check(shortTrail.Count == 0 && shortTrail.EndCall == 1 && double.IsPositiveInfinity(shortTrail.Interval), "D < 10: no duplicate, end at the first call");
        var none = new ProjectileTrail(0, 0);
        Check(none.Count == 0 && none.EndCall == 0 && double.IsNaN(none.Interval), "D = 0: no duplicate and no end (NaN in the client)");
        var longTrail = new ProjectileTrail(250, 0);
        Check(longTrail.Count == 13 && longTrail.EndCall == 27, "D = 250: " + longTrail.Count + " duplicates, end at " + longTrail.EndCall);

        // Angle du clip (rotate des types 20 et 21).
        Check(SpellProjectiles.AngleDegrees(50, 50) == 45 && SpellProjectiles.AngleDegrees(-50, 0) == 180 && SpellProjectiles.AngleDegrees(0, -10) == -90,
            "atan2 angle in degrees");
    }

    // ------------------------------------------------------------------ données livrées

    /// <summary>Pivots attendus des bandes rotate : translation de l'instance rotate dans la scène, arrondie (Effets/PROVENANCE.md).</summary>
    private static readonly Dictionary<string, Point> RotatePivots = new Dictionary<string, Point>
    {
        { "306", new Point(0, -17) }, { "403", new Point(0, -26) }, { "405", new Point(4, -25) }, { "406", new Point(4, -25) },
        { "510", new Point(0, -18) }, { "804", new Point(0, -23) }, { "806", new Point(0, -26) }, { "809", new Point(1, -25) },
        { "903", new Point(0, -36) }, { "906", new Point(0, -28) }, { "2003", new Point(0, -5) }, { "2050", new Point(0, -30) }
    };

    /// <summary>
    /// Effets/sorts livrés : une bande rotate centrée sur son pivot pour chacun des 12 gfx affichés seulement en types 20 et 21,
    /// sans scène (exclusion « symboles » qui nomme rotate), moins de 1 Mo ; lecture réelle d'une bande rotate et d'un
    /// projectile de type 31 (gfx 902, la Flèche Magique de StarLoco).
    /// </summary>
    private static void ShippedProjectiles()
    {
        string source = Path.GetFullPath(Path.Combine(TestPaths.ApplicationBin, "..", "..", "Resources", "Bot", "Effets", "sorts"));
        string list = Path.GetFullPath(Path.Combine(TestPaths.ApplicationBin, "..", "..", "..", "tools", "client-analysis", "sorts_utilises.txt"));
        Check(File.Exists(Path.Combine(source, "effets.tsv")) && File.Exists(list), "Effets/sorts or sorts_utilises.txt missing: " + source);
        var types = new Dictionary<string, int[]>();
        foreach (string line in File.ReadAllLines(list, Encoding.UTF8).Where(l => l.Length > 0 && !l.StartsWith("#", StringComparison.Ordinal)))
        {
            string[] f = line.Split(' ');
            types[f[0]] = f[1].Split(',').Select(int.Parse).ToArray();
        }
        string[][] rows = File.ReadAllLines(Path.Combine(source, "effets.tsv"), Encoding.UTF8).Skip(1).Select(l => l.Split('\t')).ToArray();
        string[][] exclusions = File.ReadAllLines(Path.Combine(source, "exclusions.tsv"), Encoding.UTF8).Skip(1).Select(l => l.Split('\t')).ToArray();
        string[][] rotates = rows.Where(r => r[1] == "rotate").ToArray();
        Check(rotates.Select(r => r[0]).OrderBy(g => g).SequenceEqual(RotatePivots.Keys.OrderBy(g => g)), "rotate strips: " + string.Join(",", rotates.Select(r => r[0])));
        long bytes = 0;
        foreach (string[] r in rotates)
        {
            string gfx = r[0];
            int xmin = int.Parse(r[2]), ymin = int.Parse(r[3]), width = int.Parse(r[4]), height = int.Parse(r[5]);
            Check(types.ContainsKey(gfx) && types[gfx].All(t => t == 20 || t == 21), "rotate exported for a gfx not shown in types 20/21 only: " + gfx);
            Check(width % 2 == 0 && height % 2 == 0 && xmin + width / 2 == RotatePivots[gfx].X && ymin + height / 2 == RotatePivots[gfx].Y && r[7] == "20",
                "rotate of " + gfx + " not centred on its pivot " + RotatePivots[gfx] + ": " + string.Join(" ", r));
            Check(!rows.Any(o => o[0] == gfx && o[1] == "scene") && exclusions.Any(e => e[0] == gfx && e[1] == "symboles" && e[2].Split(',').Contains("rotate")),
                "Gfx " + gfx + " must have no scene and a 'symboles' exclusion naming rotate");
            bytes += new FileInfo(Path.Combine(source, gfx + "_rotate.png")).Length;
        }
        Check(bytes < 1000000, "rotate strips weigh " + bytes + " bytes (AN5 budget: under 1 MB)");
        Check(types.Where(t => t.Value.All(v => v == 20 || v == 21)).All(t => RotatePivots.ContainsKey(t.Key)), "A type 20/21 gfx has no rotate strip");

        using (var library = new ActorSprites(Path.Combine(TestPaths.ApplicationBin, "ressources", "Bot", "sprites")))
        using (ActorSprites.FixedStrip rotate = library.ResolveFixed(SpellEffects.SpellFamily, 903, SpellProjectiles.RotateAnimation))
        {
            SpellEffects.Prefetch(library, 902, 31);
            var arrow = new ProjectileEffect(library, 902, 31, new PointF(100, 100), new PointF(259, 100), 0, 0, 4);
            try
            {
                Check(library.WaitForPending(20000), "Shipped projectile strips decoding did not finish");
                Check(rotate.State == SpriteLoadState.Ready && rotate.Sheet.Frames == 25 && rotate.Sheet.FramesPerSecond == 20, "Shipped 903_rotate not read: " + rotate.Reason);
                arrow.Update(1, null);
                Check(arrow.StartedAt.HasValue && !arrow.Skipped && arrow.Arc.ArrivalCall > 0
                    && arrow.Lifetime == arrow.Arc.ArrivalCall * SpellProjectiles.FrameMs + 1100, "Shipped type 31 projectile (902: move, shoot of 22 images at 20 ips): " + arrow.Lifetime);
            }
            finally { arrow.Dispose(); }
        }
        Console.WriteLine("Effets/sorts: " + rotates.Length + " rotate strips, " + bytes + " bytes");
    }

    // ------------------------------------------------------------------ bandes synthétiques

    private static void Frames(string path, int width, int height, int count, Action<Graphics, int> paint)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using (var image = new Bitmap(count * width, height, PixelFormat.Format32bppArgb))
        {
            using (Graphics graphics = Graphics.FromImage(image))
            {
                graphics.Clear(Color.Transparent);
                for (int k = 0; k < count; k++) paint(graphics, k * width);
            }
            image.Save(path, ImageFormat.Png);
        }
    }

    private static void Solid(string folder, List<string> rows, int gfx, string anim, Color color, int size, int count, string end)
    {
        Frames(Path.Combine(folder, gfx + "_" + anim + ".png"), size, size, count, (graphics, left) =>
        {
            using (var brush = new SolidBrush(color)) graphics.FillRectangle(brush, left, 0, size, size);
        });
        rows.Add(gfx + "\t" + anim + "\t" + (-size / 2) + "\t" + (-size / 2) + "\t" + size + "\t" + size + "\t" + count + "\t40\t" + end);
    }

    private static string Assets()
    {
        string root = Path.Combine(TestPaths.Work, "spell-projectiles");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        string sprites = Path.Combine(root, "sprites"), spells = Path.Combine(root, "Effets", "sorts");
        Directory.CreateDirectory(sprites);
        var anchors = new List<string> { "gfx\tanim\txmin\tymin\tlargeur\thauteur\timages\tips\tfin" };
        foreach (string o in new[] { "R", "L" })
            foreach (KeyValuePair<int, Color> body in new[] { new KeyValuePair<int, Color>(900, SelfColor), new KeyValuePair<int, Color>(902, MonsterColor) })
            {
                Frames(Path.Combine(sprites, body.Key + "_static" + o + ".png"), 20, 60, 1, (graphics, left) =>
                {
                    using (var brush = new SolidBrush(body.Value)) graphics.FillRectangle(brush, left, 0, 20, 60);
                });
                anchors.Add(body.Key + "\tstatic" + o + "\t-10\t-60\t20\t60\t1\t40\tarret");
            }
        File.WriteAllLines(Path.Combine(sprites, "ancres.tsv"), anchors);

        var rows = new List<string> { "gfx\tanim\txmin\tymin\tlargeur\thauteur\timages\tips\tfin" };
        // 9101 (30, 31) : move 6 x 6 en boucle, shoot 10 x 10 de 4 images qui retire le clip.
        Solid(spells, rows, 9101, "move", MoveColor, 6, 2, "boucle");
        Solid(spells, rows, 9101, "shoot", ShootColor, 10, 4, "static");
        // 9102 (20, 21) : rotate 40 x 8 dont seule la moitié droite est dessinée, cadre centré sur (0, −30) ; shoot 8 x 8.
        Frames(Path.Combine(spells, "9102_rotate.png"), 40, 8, 2, (graphics, left) =>
        {
            using (var brush = new SolidBrush(RotateColor)) graphics.FillRectangle(brush, left + 20, 0, 20, 8);
        });
        rows.Add("9102\trotate\t-20\t-34\t40\t8\t2\t40\tstatic");
        Solid(spells, rows, 9102, "shoot", TurnedShootColor, 8, 3, "static");
        // 9103 (41) : duplicate 4 x 4 qui tient sa dernière image, shoot 8 x 8 ; 9107 (40) : duplicate joué une fois.
        Solid(spells, rows, 9103, "duplicate", DuplicateColor, 4, 2, "arret");
        Solid(spells, rows, 9103, "shoot", TrailShootColor, 8, 2, "static");
        Solid(spells, rows, 9107, "duplicate", OnceDuplicateColor, 4, 2, "static");
        // 9104 (51) : scène 10 x 10 de 3 images ; 9105 (50) : shoot seul.
        Solid(spells, rows, 9104, "scene", SceneColor, 10, 3, "static");
        Solid(spells, rows, 9105, "shoot", CellShootColor, 8, 2, "static");
        File.WriteAllLines(Path.Combine(spells, "effets.tsv"), rows.OrderBy(r => r).ToArray());
        return sprites;
    }

    // ------------------------------------------------------------------ clips dessinés dans un Bitmap

    /// <summary>Centre des pixels de la couleur (à 12 près par canal, presque opaques), ou null.</summary>
    private static PointF? Centroid(Bitmap image, Color color)
    {
        double sx = 0, sy = 0; int count = 0;
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
            {
                Color pixel = image.GetPixel(x, y);
                if (pixel.A > 200 && Near(pixel, color)) { sx += x + 0.5; sy += y + 0.5; count++; }
            }
        return count == 0 ? (PointF?)null : new PointF((float)(sx / count), (float)(sy / count));
    }

    private static bool Near(Color pixel, Color expected) =>
        Math.Abs(pixel.R - expected.R) <= 12 && Math.Abs(pixel.G - expected.G) <= 12 && Math.Abs(pixel.B - expected.B) <= 12;

    private static bool Within(PointF? found, double x, double y, double tolerance) =>
        found.HasValue && Math.Abs(found.Value.X - x) <= tolerance && Math.Abs(found.Value.Y - y) <= tolerance;

    private static Bitmap Draw(ProjectileEffect effect, double now, string name)
    {
        effect.Update(now, null);
        var image = new Bitmap(400, 300, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(image))
        {
            graphics.Clear(Color.White);
            effect.Draw(graphics, null);
        }
        image.Save(Path.Combine(TestPaths.Work, name + ".png"), ImageFormat.Png);
        return image;
    }

    private static string Where(PointF? point) => point.HasValue ? point.Value.X.ToString("0.0") + ", " + point.Value.Y.ToString("0.0") : "absent";

    private static void Clips(string spritesDirectory)
    {
        using (var library = new ActorSprites(spritesDirectory))
        {
            var held = new List<ActorSprites.FixedStrip>();
            foreach (int gfx in new[] { 9101, 9102, 9103, 9104, 9105, 9106, 9107 })
                foreach (string anim in new[] { "scene", "rotate", "shoot", "move", "duplicate" }) held.Add(library.ResolveFixed(SpellEffects.SpellFamily, gfx, anim));
            Check(library.WaitForPending(10000), "Synthetic projectile strips decoding did not finish");
            try
            {
                Thrown(library);
                Turned(library);
                Trails(library);
                AtCell(library);
                Dispatch(library);
            }
            finally { foreach (ActorSprites.FixedStrip strip in held) strip.Dispose(); }
        }
    }

    /// <summary>Type 30 vers (106, −27) : move à mi-course, shoot à l'arrivée, fin du clip ; retourné vers (−106, −27).</summary>
    private static void Thrown(ActorSprites library)
    {
        var caster = new PointF(150, 200);
        using (var effect = new ProjectileEffect(library, 9101, 30, caster, new PointF(256, 173), 0, 0, 4))
        {
            Check(effect.StartedAt == 0 && effect.Origin == new PointF(150, 190) && effect.Arc.ArrivalCall == 15 && !effect.Skipped, "Type 30 clip not posed at once, 10 px above the caster");
            Check(!effect.IsReleased(0) && !effect.IsReleased(374.9) && effect.IsReleased(375), "Type 30 step must be released at the arrival (call 15, 375 ms)");
            Reference(106, -27, 30, 10, out double x10, out double y10, out double _, out bool _);
            using (Bitmap image = Draw(effect, 250, "bot-projectiles-1-arc-mid"))
            {
                PointF? found = Centroid(image, MoveColor);
                Check(effect.CallCount == 10 && Within(found, 150 + x10, 190 + y10, 2),
                    "move at frame 10 drawn at " + Where(found) + " instead of " + (150 + x10).ToString("0.0") + ", " + (190 + y10).ToString("0.0"));
                Check(Centroid(image, ShootColor) == null, "shoot drawn before the arrival");
            }
            using (Bitmap image = Draw(effect, 375, "bot-projectiles-2-arc-arrival"))
            {
                PointF? found = Centroid(image, ShootColor);
                Check(Within(found, 256, 163, 2) && Centroid(image, MoveColor) == null, "shoot not at the destination (256, 163) at the arrival: " + Where(found));
            }
            Check(effect.Lifetime == 475 && effect.Update(474, null) && !effect.Update(475, null) && effect.Finished, "Type 30 clip must end with its shoot (375 + 100 ms): " + effect.Lifetime);
            Check(effect.IsReleased(500), "Finished type 30 clip not released");
        }
        using (var effect = new ProjectileEffect(library, 9101, 30, caster, new PointF(44, 173), 0, 0, 4))
        {
            Reference(-106, -27, 30, 10, out double x10, out double y10, out double _, out bool _);
            using (Bitmap image = Draw(effect, 250, "bot-projectiles-3-arc-mirrored"))
            {
                PointF? found = Centroid(image, MoveColor);
                Check(effect.Arc.MirrorX && Within(found, 150 - x10, 190 + y10, 2), "Mirrored move at frame 10 drawn at " + Where(found));
            }
        }
        // Gfx sans aucune bande : rien n'est dessiné, mais l'étape suit la géométrie (end() à l'arrivée).
        using (var effect = new ProjectileEffect(library, 9106, 31, caster, new PointF(256, 173), 0, 0, 4))
        {
            Check(effect.Skipped && !effect.Update(0, null) && effect.Finished, "Missing projectile strips must skip the clip");
            Check(!effect.IsReleased(174) && effect.IsReleased(175), "Skipped type 31 clip must still release the step at the arrival (call 7)");
        }
    }

    /// <summary>Type 20 vers (+50, +50) : rotate tourné de 45° autour du centre de son cadre, shoot à (dx, dy), non bloquant.</summary>
    private static void Turned(ActorSprites library)
    {
        var caster = new PointF(100, 150);
        using (var effect = new ProjectileEffect(library, 9102, 20, caster, new PointF(150, 200), 0, 0, 4))
        {
            Check(effect.Angle == 45 && effect.IsReleased(0), "Type 20 angle or blocking");
            using (Bitmap image = Draw(effect, 10, "bot-projectiles-4-rotate-45"))
            {
                // Pivot : centre du cadre (0, −30) ; la moitié dessinée part du pivot vers la cible.
                double c = Math.Cos(Math.PI / 4);
                Color along = image.GetPixel((int)Math.Round(100 + 12 * c), (int)Math.Round(120 + 12 * c));
                Color flat = image.GetPixel(112, 120), behind = image.GetPixel((int)Math.Round(100 - 12 * c), (int)Math.Round(120 - 12 * c));
                Check(Near(along, RotateColor) && !Near(flat, RotateColor) && !Near(behind, RotateColor),
                    "rotate not turned 45 degrees around its frame centre: " + along + " / " + flat + " / " + behind);
                PointF? shoot = Centroid(image, TurnedShootColor);
                Check(Within(shoot, 150, 200, 2), "Type 20 shoot not at (dx, dy): " + Where(shoot));
            }
            // rotate (static, 2 images) retire le clip avant la fin de shoot (3 images).
            Check(effect.Lifetime == 50 && effect.Update(49, null) && !effect.Update(50, null), "Type 20 clip must end with its rotate (50 ms): " + effect.Lifetime);
        }
    }

    /// <summary>Type 41 sur 250 px : duplicate aux appels impairs, shoot à la fin (27e appel) à la place du duplicate 10 ; type 40.</summary>
    private static void Trails(ActorSprites library)
    {
        using (var effect = new ProjectileEffect(library, 9103, 41, new PointF(50, 100), new PointF(300, 100), 0, 0, 4))
        {
            Check(effect.Trail.Count == 13 && effect.Trail.EndCall == 27, "Type 41 trail over 250 px");
            using (Bitmap image = Draw(effect, 225, "bot-projectiles-5-trail"))
            {
                for (int i = 0; i < 5; i++) Check(Near(image.GetPixel(60 + 20 * i, 100), DuplicateColor), "Duplicate " + i + " missing at call 9");
                Check(!Near(image.GetPixel(160, 100), DuplicateColor) && Centroid(image, TrailShootColor) == null, "Duplicate 5 or shoot drawn too early");
            }
            Check(!effect.IsReleased(674) && effect.IsReleased(675), "Type 41 step must be released at the end call (27, 675 ms)");
            using (Bitmap image = Draw(effect, 675, "bot-projectiles-6-trail-end"))
            {
                Check(Within(Centroid(image, TrailShootColor), 300, 100, 2), "Type 41 shoot not at the target at the end call");
                Check(!Near(image.GetPixel(240, 100), DuplicateColor) && Near(image.GetPixel(260, 100), DuplicateColor) && Near(image.GetPixel(220, 100), DuplicateColor),
                    "shoot must replace duplicate 10 (depth 10) and keep the others");
            }
            Check(effect.Lifetime == 725 && !effect.Update(725, null), "Type 41 clip must end with its shoot: " + effect.Lifetime);
        }
        // Type 40 sans shoot : la traînée finit au 11e appel, le dernier duplicate (appel 9, 50 ms) à 275 ms.
        using (var effect = new ProjectileEffect(library, 9107, 40, new PointF(50, 100), new PointF(150, 100), 0, 0, 4))
        {
            Check(effect.Lifetime == 275 && !effect.IsReleased(274) && effect.IsReleased(275), "Type 40 lifetime or release: " + effect.Lifetime);
            using (Bitmap image = Draw(effect, 230, "bot-projectiles-7-trail40"))
                Check(Near(image.GetPixel(140, 100), OnceDuplicateColor) && !Near(image.GetPixel(60, 100), OnceDuplicateColor), "Type 40 duplicates must play once each");
        }
    }

    /// <summary>Types 50 et 51 (approximation) : scène, ou shoot, au centre de la cellule visée, une passe ; 51 libère l'étape à sa fin.</summary>
    private static void AtCell(ActorSprites library)
    {
        using (var effect = new ProjectileEffect(library, 9104, 51, new PointF(50, 100), new PointF(200, 120), 0, 0, 4))
        {
            Check(effect.Origin == new PointF(200, 120) && !effect.IsReleased(74) && effect.IsReleased(75) && effect.Lifetime == 75, "Type 51 release or lifetime");
            using (Bitmap image = Draw(effect, 30, "bot-projectiles-8-type51"))
                Check(Within(Centroid(image, SceneColor), 200, 120, 2), "Type 51 scene not at the target cell");
        }
        using (var effect = new ProjectileEffect(library, 9105, 50, new PointF(50, 100), new PointF(200, 120), 0, 0, 4))
        {
            Check(effect.IsReleased(0) && effect.Lifetime == 50, "Type 50 must not block");
            using (Bitmap image = Draw(effect, 10, "bot-projectiles-9-type50"))
                Check(Within(Centroid(image, CellShootColor), 200, 120, 2), "Type 50 shoot fallback not at the target cell");
        }
    }

    /// <summary>SpellEffects : aiguillage par type, index partagé avec les scènes, fin d'étape.</summary>
    private static void Dispatch(ActorSprites library)
    {
        using (var set = new MapEffectSet())
        {
            var effects = new SpellEffects();
            Func<IMapEffect, bool> accept = effect => { set.Add(effect); return true; };
            var foot = new PointF(10, 20); var cell = new PointF(116, -7);
            IMapEffect scene = effects.Launch(library, set, accept, 9104, 11, foot, cell, 30, true, 0);
            IMapEffect projectile = effects.Launch(library, set, accept, 9101, 30, foot, cell, 30, false, 0);
            Check(scene is StripEffect && ((StripEffect)scene).World == cell && projectile is ProjectileEffect, "Launch does not dispatch type 11 and type 30");
            var thrown = (ProjectileEffect)projectile;
            Check(thrown.Depth == SpellEffects.Depth(30, 2, false) && thrown.Order == 2 && thrown.Origin == new PointF(10, 10) && effects.LastIndex == 2,
                "Projectile depth must follow the shared effect index: " + thrown.Depth);
            Check(effects.Launch(library, set, accept, 9101, 30, null, cell, 30, true, 0) == null && effects.Launch(library, set, accept, 9101, 30, foot, null, 30, true, 0) == null,
                "Projectile launched without caster or cell");
            Check(effects.Launch(library, set, accept, 9104, 51, null, cell, 30, true, 0) is ProjectileEffect, "Type 51 needs only the target cell");
            Check(effects.Launch(library, set, accept, 9101, 99, foot, cell, 30, true, 0) == null && effects.AddProjectile(library, set, accept, 9101, 12, foot, cell, 30, true, 0) == null
                && effects.AddProjectile(library, set, accept, -1, 30, foot, cell, 30, true, 0) == null, "Invalid projectile accepted");
            Check(SpellEffects.Released(null, 0) && !SpellEffects.Released(projectile, 0) && SpellEffects.Released(projectile, 2000), "Released rule");
            SpellEffects.Prefetch(library, 9101, 0); SpellEffects.Prefetch(null, 9101, 30); SpellEffects.Prefetch(library, -1, 30);
            Check(set.Count == 3, "Effects kept by the set: " + set.Count);
        }
    }

    // ------------------------------------------------------------------ carte en boucle locale

    private static void PumpUntil(Func<bool> done)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(10);
        while (!done()) { if (DateTime.UtcNow > end) throw new TimeoutException("Spell projectiles loopback timed out"); Application.DoEvents(); Thread.Sleep(5); }
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

    private static void At(MapControl control, double time)
    {
        clock = time;
        control.MapSurface.TickAnimations();
    }

    private static ProjectileEffect[] Projectiles(MapControl control) => control.Effects.Active.OfType<ProjectileEffect>().ToArray();

    private static void RunMap(string sprites)
    {
        int count = MapHeight * (2 * MapWidth - 1) - (MapWidth - 1);
        MessagesReception.Init();
        Tool_BotProtocol.Game.Maps.Map.AllBotMaps[900097] = new Tool_BotProtocol.Game.Maps.Map
        {
            MapID = 900097, MapWidth = MapWidth, MapHeight = MapHeight, MapData = string.Concat(Enumerable.Repeat(EncodedCell(), count))
        };
        // Bandes tenues pendant tout le test : prêtes dès leur demande par la carte.
        using (var keeper = new ActorSprites(sprites))
        {
            foreach (int direction in new[] { 1, 3, 5, 7 })
            {
                keeper.Resolve(900, direction, false, "static");
                keeper.Resolve(902, direction, false, "static");
            }
            var held = new List<ActorSprites.FixedStrip>();
            foreach (int gfx in new[] { 9101, 9102 })
                foreach (string anim in new[] { "scene", "rotate", "shoot", "move" }) held.Add(keeper.ResolveFixed(SpellEffects.SpellFamily, gfx, anim));
            Check(keeper.WaitForPending(10000), "Synthetic strips decoding did not finish");

            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            try
            {
                using (var account = new Accounts(new AccountConfig("synthetic-projectiles", "synthetic", "loopback")))
                {
                    Task<Socket> accept = listener.AcceptSocketAsync();
                    Complete(account.Connexion.ConnectToServer(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port)); Complete(accept);
                    using (Socket peer = accept.Result)
                    {
                        peer.ReceiveTimeout = 6000; account.Game.character.id = 42;
                        Feed(account, "GDM|900097|date|"); Check(Read(peer) == "GI", "GDM did not request GI");
                        using (var control = new MapControl(account, () => clock))
                        {
                            UserMapControl view = control.MapSurface;
                            FieldInfo field = typeof(UserMapControl).GetField("sprites", BindingFlags.Instance | BindingFlags.NonPublic);
                            var shipped = (ActorSprites)field.GetValue(view);
                            field.SetValue(view, new ActorSprites(sprites));
                            shipped.Dispose();
                            control.Options = new BotOptions(Path.Combine(TestPaths.Work, "spell-projectiles", "BotOptions.json")) { AutoSave = false };
                            control.MapShown = () => true;
                            control.Size = new Size(640, 400); view.Size = new Size(640, 400);
                            typeof(UserControl).GetMethod("OnLoad", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(control, new object[] { EventArgs.Empty });

                            var failures = new List<string>();
                            control.Sequencer.StepFailed += (step, error) => failures.Add(step.Name + ": " + error);
                            control.Effects.EffectFailed += (effect, error) => failures.Add(effect.GetType().Name + ": " + error);

                            Feed(account, "GJK2|1|1|0|30000|0");
                            Feed(account, "GP" + Hash.Get_Cell_Char(SelfCell) + "|" + Hash.Get_Cell_Char(MonsterCell) + "|0");
                            Feed(account, "GM|+" + SelfCell + ";1;0;42;Synthetic;1;900^100;0;1;0,0,0,0;-1;-1;-1;,,,,;100;8;3");
                            Feed(account, "GS");
                            Feed(account, "GM|+" + MonsterCell + ";1;0;-7;101;-2;902^100;1;-1;-1;-1;0,0,0,0;60;4;2;1");
                            Feed(account, "GTM|42;0;100;8;3;" + SelfCell + ";;100|-7;0;60;4;2;" + MonsterCell + ";;60");
                            Feed(account, "GTS42|30000");
                            Check(account.Game.Fight.IsInFight && account.Game.Fight.CurrentActorId == 42, "Synthetic fight did not start on the player's turn");
                            InFight(account, control, view);
                            Check(failures.Count == 0, "Visual step or effect failed: " + string.Join(" | ", failures));
                        }
                    }
                }
            }
            finally
            {
                listener.Stop();
                foreach (ActorSprites.FixedStrip strip in held) strip.Dispose();
            }
        }
    }

    private static void InFight(Accounts account, MapControl control, UserMapControl view)
    {
        At(control, 30000);
        bool drawn = view.TryGetActorAnchor(42, out ActorAnchor self);
        PointF target;
        Check(view.EffectView.TryGetCellCenter(MonsterCell, out target) && drawn, "Caster or target cell not drawn");
        double dx = target.X - self.WorldFoot.X, dy = target.Y - self.WorldFoot.Y;
        int arrival = ReferenceArrival(dx, dy, 30);
        Check(arrival > 10 && arrival * 25 < 1000, "Unexpected map geometry: arrival at call " + arrival + " for " + dx + ", " + dy);

        // GA300 type 30 sans animation (-2), puis GA100 : la file du lanceur attend l'arrivée du projectile.
        const double f1 = 40000;
        At(control, f1);
        Feed(account, "GA;300;42;161," + MonsterCell + ",9101,1,30,-2,1");
        Feed(account, "GA;100;42;-7,-5");
        At(control, f1);
        ProjectileEffect thrown = Projectiles(control).SingleOrDefault();
        Check(thrown != null && thrown.StartedAt == f1 && thrown.Arc.ArrivalCall == arrival && control.Points == null, "GA300 type 30 did not start its projectile at once, or GA100 did not wait");
        Check(thrown.Depth == SpellEffects.Depth(MonsterCell, control.SpellEffects.LastIndex, true) && thrown.Order == 4
            && thrown.Origin == new PointF(self.WorldFoot.X, self.WorldFoot.Y - 10), "Projectile depth or origin on the map");
        int middle = arrival / 2;
        At(control, f1 + middle * 25);
        Reference(dx, dy, 30, middle, out double mx, out double my, out double _, out bool _);
        using (Bitmap image = Render(view, "bot-projectiles-10-map-mid"))
        {
            PointF expected = view.EffectView.ToScreen(new PointF((float)(self.WorldFoot.X + mx), (float)(self.WorldFoot.Y - 10 + my)));
            PointF? found = Centroid(image, MoveColor);
            Check(Within(found, expected.X, expected.Y, 2), "move drawn on the map at " + Where(found) + " instead of " + expected.X.ToString("0.0") + ", " + expected.Y.ToString("0.0"));
        }
        At(control, f1 + arrival * 25 - 1);
        Check(control.Points == null && Projectiles(control).Length == 1, "GA100 shown before the projectile's arrival");
        At(control, f1 + arrival * 25);
        Check(control.Points != null && control.Points.Numbers.Any(n => n.ActorId == -7 && n.Text == "-5"), "GA100 not shown at the projectile's arrival");
        using (Bitmap image = Render(view, "bot-projectiles-11-map-arrival"))
        {
            PointF expected = view.EffectView.ToScreen(new PointF(target.X, target.Y - 10));
            Check(Within(Centroid(image, ShootColor), expected.X, expected.Y, 2), "shoot not drawn at the target on the map: " + Where(Centroid(image, ShootColor)));
        }
        At(control, f1 + arrival * 25 + 101);
        Check(Projectiles(control).Length == 0, "Type 30 clip not removed after its shoot");
        At(control, f1 + 3000);

        // Type 20 : non bloquant, le GA100 suit aussitôt.
        const double f2 = 45000;
        At(control, f2);
        Feed(account, "GA;300;42;161," + MonsterCell + ",9102,1,20,-2,1");
        Feed(account, "GA;100;42;-7,-3");
        At(control, f2);
        Check(Projectiles(control).Length == 1 && Math.Abs(Projectiles(control)[0].Angle - SpellProjectiles.AngleDegrees(dx, dy)) < 1e-9
            && control.Points != null && control.Points.Numbers.Any(n => n.Text == "-3"), "Type 20 must not hold GA100");
        At(control, f2 + 1000);

        // Carte cachée : ni étape ni projectile.
        control.MapShown = () => false;
        Feed(account, "GA;300;42;161," + MonsterCell + ",9101,1,30,-2,1");
        Check(control.Sequencer.IsIdle && Projectiles(control).Length == 0, "Hidden map kept a projectile");
        control.MapShown = () => true;
        At(control, f2 + 2000);
        Check(control.Sequencer.IsIdle && control.Effects.Count == 0, "Steps or effects left over");
    }
}
