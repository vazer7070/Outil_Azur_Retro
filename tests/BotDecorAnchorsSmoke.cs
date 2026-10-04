using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Controls;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Utils.Crypto;

// Décor du client exporté par tools/client-analysis/exporter_decor.py : PNG + ancres.tsv.
// Toutes les images, ancres et cartes sont synthétiques et créées dans un dossier temporaire.
internal static class BotDecorAnchorsSmoke
{
    private static readonly Color Red = Color.FromArgb(220, 30, 30), Blue = Color.FromArgb(30, 60, 220), Green = Color.FromArgb(30, 170, 60),
        Orange = Color.FromArgb(240, 150, 20), Yellow = Color.FromArgb(235, 220, 40), Cyan = Color.FromArgb(30, 200, 210), Brown = Color.FromArgb(120, 70, 30);

    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            string file = Path.Combine(TestPaths.ApplicationBin, new AssemblyName(args.Name).Name + ".dll");
            if (!File.Exists(file)) file = Path.ChangeExtension(file, "exe");
            return File.Exists(file) ? Assembly.LoadFrom(file) : null;
        };
        try { Run(); Console.WriteLine("OK: decor PNG drawn at cell + ancres.tsv offset, client flips/quarter turns/slope frames, background origin, missing PNG, off-thread loading and shared cache"); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }

    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }

    private static string Cell(int ground, int object1 = 0, int object2 = 0, bool groundFlip = false, int groundRotation = 0,
        bool object2Flip = false, int slope = 1, bool active = true)
    {
        int[] value = new int[10];
        value[0] = (active ? 32 : 0) | 1 | ((ground >> 6) & 24) | ((object1 >> 11) & 4) | ((object2 >> 12) & 2);
        value[1] = 7 | (groundRotation << 4); value[2] = 32 | ((ground >> 6) & 7); value[3] = ground & 63;
        value[4] = (slope << 2) | (groundFlip ? 2 : 0) | ((object1 >> 12) & 1); value[5] = (object1 >> 6) & 63; value[6] = object1 & 63;
        value[7] = (object2Flip ? 4 : 0) | ((object2 >> 12) & 1); value[8] = (object2 >> 6) & 63; value[9] = object2 & 63;
        return new string(value.Select(part => Hash.caracteres_array[part]).ToArray());
    }

    private static void Png(string path, int width, int height, Action<Graphics> paint)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
        {
            using (Graphics graphics = Graphics.FromImage(bitmap)) { graphics.Clear(Color.Transparent); paint(graphics); }
            bitmap.Save(path, ImageFormat.Png);
        }
    }

    private static void Solid(string path, int width, int height, Color color) =>
        Png(path, width, height, graphics => { using (var brush = new SolidBrush(color)) graphics.FillRectangle(brush, 0, 0, width, height); });

    private static bool Near(Color pixel, Color expected) => pixel.A > 200
        && Math.Abs(pixel.R - expected.R) < 40 && Math.Abs(pixel.G - expected.G) < 40 && Math.Abs(pixel.B - expected.B) < 40;

    // Renders the scene with a margin: world (x, y) lands on pixel (x + 40, y + 50).
    private static Bitmap Render(BotMapArtwork artwork)
    {
        var bitmap = new Bitmap(200, 120, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.TranslateTransform(40, 50);
            artwork.DrawGround(graphics);
            artwork.DrawDepthScene(graphics, null);
        }
        return bitmap;
    }

    private static Color At(Bitmap bitmap, PointF world, float dx, float dy) =>
        bitmap.GetPixel((int)Math.Floor(world.X + dx + 40), (int)Math.Floor(world.Y + dy + 50));

    private static Map MapOf(int id, string data, int background = 0) =>
        new Map { MapID = id, MapWidth = 2, MapHeight = 1, MapData = data, Back_ID = background };

    private static void Run()
    {
        string directory = Path.Combine(TestPaths.Work, "decor-anchors-" + Guid.NewGuid().ToString("N"));
        Solid(Path.Combine(directory, "sols", "7.png"), 4, 4, Red);
        Solid(Path.Combine(directory, "sols", "7_3.png"), 4, 4, Green);
        Png(Path.Combine(directory, "sols", "20.png"), 40, 20, graphics =>
        {
            using (var left = new SolidBrush(Yellow)) graphics.FillRectangle(left, 0, 0, 20, 20);
            using (var right = new SolidBrush(Cyan)) graphics.FillRectangle(right, 20, 0, 20, 20);
        });
        Solid(Path.Combine(directory, "objets", "9.png"), 4, 4, Blue);
        Solid(Path.Combine(directory, "objets", "31.png"), 4, 4, Brown);
        Solid(Path.Combine(directory, "backgrounds", "5.png"), 6, 6, Orange);
        File.WriteAllLines(Path.Combine(directory, BotMapArtwork.AnchorFileName), new[]
        {
            "# type\tid\txmin\tymin\tlargeur\thauteur\timage",
            "sol\t7\t-2\t-2\t4\t4\t1",
            "sol\t7\t-2\t-2\t4\t4\t3",
            "sol\t20\t-20\t-10\t40\t20",
            "objet\t9\t10\t-12\t4\t4\t1",
            "objet\t31\t10\t-12\t8\t8\t1",       // wrong size: the PNG was replaced, the anchor must be ignored
            "objet\t31\tNaN\t1e30\t4\t4\t1",      // not a position: rejected, it must not replace the line above
            "fond\t5\t-1\t-1\t6\t6\t1",
            "selection\t1\t-27\t-14\t54\t28\t1",  // cell.swf shapes are listed but not used by the map scenery
            "objet\tx\t1\t2\t3\t4",                // malformed
            "inconnu\t1\t2\t3\t4\t5"              // unknown type
        });

        int caller = Thread.CurrentThread.ManagedThreadId;
        // Map A: anchored ground and object2 (plain and mirrored), anchored background, one missing ground.
        using (Map map = MapOf(992001, Cell(7, object2: 9) + Cell(8, object2: 9, object2Flip: true), background: 5))
        using (var artwork = new BotMapArtwork(map, directory))
        {
            bool readyBeforeSubscription = artwork.AssetsReady;
            using (var loaded = new ManualResetEventSlim(false))
            {
                artwork.AssetsLoaded += (sender, e) => loaded.Set();
                Check(artwork.Cells.Length == 2 && artwork.Cells[0].GroundId == 7 && artwork.Cells[1].Object2Id == 9 && artwork.Cells[1].Object2Flip,
                    "Synthetic map cells were not decoded");
                Check(artwork.WaitForAssets(10000), "Decor PNG loading did not finish");
                Check(readyBeforeSubscription || loaded.Wait(5000), "AssetsLoaded was not raised");
            }
            Check(artwork.LoadThreadId != 0 && artwork.LoadThreadId != caller, "Decor PNG were read on the calling thread");
            Check(artwork.MissingAssetCount == 1 && artwork.Status.Contains("sol 8"), "Missing ground PNG was not reported: " + artwork.Status);
            Check(artwork.LoadedAssetCount == 4 && artwork.AnchoredAssetCount == 4,
                "ancres.tsv was not applied (" + artwork.LoadedAssetCount + " loaded, " + artwork.AnchoredAssetCount + " anchored)");
            Check(artwork.RejectedAnchorLines == 3, "Malformed ancres.tsv lines were not counted: " + artwork.RejectedAnchorLines);
            PointF first = artwork.Cells[0].Center, second = artwork.Cells[1].Center;
            Check(first == new PointF(BotMapArtwork.CellWidth / 2, BotMapArtwork.CellHeight / 2) && BotMapArtwork.CellHeight == 27f,
                "Cell 0 is not at the client's origin (CELL_HALF_HEIGHT 13.5)");
            using (Bitmap scene = Render(artwork))
            {
                Check(Near(At(scene, first, 0, 0), Red), "Ground was not centred on its registration point");
                Check(Near(At(scene, first, 12, -10), Blue), "Object2 was not drawn at cell + (xmin, ymin)");
                Check(!Near(At(scene, first, 0, -2), Blue), "Object2 used the bottom-centre heuristic instead of its anchor");
                Check(Near(At(scene, second, -12, -10), Blue) && !Near(At(scene, second, 12, -10), Blue),
                    "Mirrored object2 did not flip around its registration point");
                PointF origin = BotMapArtwork.ClientOrigin;
                Check(Near(At(scene, origin, 3.5f, 3.5f), Orange), "Background was not attached at the client's map origin");
                Check(!Near(At(scene, second, 0, 0), Red), "Missing ground was replaced by another picture");
            }
            Check(artwork.WorldBounds.Contains(new RectangleF(first.X + 10, first.Y - 12, 4, 4)), "Anchored objects are outside the fitted bounds");

            // A second view of the same map shares the cached pictures; disposing the first must not free them.
            using (var again = new BotMapArtwork(map, directory))
            {
                Check(again.WaitForAssets(10000) && again.LoadedAssetCount == 4, "Shared decor cache lost pictures");
                artwork.Dispose();
                using (Bitmap scene = Render(again))
                    Check(Near(At(scene, first, 12, -10), Blue), "Disposing one map freed pictures still used by another");
                using (Bitmap disposed = Render(artwork))
                    Check(disposed.GetPixel(0, 0).A == 0, "A disposed artwork still drew");
            }
        }

        // Map B: slope frame (sols/7_3.png, never turned) and the client's quarter turn (rotation × 51.85 % / 192.86 %).
        using (Map map = MapOf(992002, Cell(7, groundRotation: 1, slope: 3) + Cell(20, groundRotation: 1)))
        using (var artwork = new BotMapArtwork(map, directory))
        {
            Check(artwork.WaitForAssets(10000) && artwork.MissingAssetCount == 0, "Slope or rotated ground failed to load: " + artwork.Status);
            PointF sloped = artwork.Cells[0].Center, turned = artwork.Cells[1].Center;
            using (Bitmap scene = Render(artwork))
            {
                Check(Near(At(scene, sloped, 0, 0), Green), "Sloped cell did not use the ground's frame n (groundSlope)");
                Check(Near(At(scene, turned, 0, 6), Cyan) && Near(At(scene, turned, 0, -6), Yellow),
                    "Quarter turn did not rotate the ground clockwise around its registration point");
                // Unscaled, the 40 × 20 tile would reach y = ±20 and x = ±10 once turned; the client squeezes it to ±10.4 × ±19.3.
                Check(At(scene, turned, 0, 15).A == 0 && Near(At(scene, turned, 17, 3), Cyan) && Near(At(scene, turned, 17, -3), Yellow),
                    "Quarter turn was not rescaled like the client (51.85 % / 192.86 %)");
            }
        }

        // Map C: inactive cell (the client draws nothing) and an anchor whose size no longer matches its PNG.
        using (Map map = MapOf(992003, Cell(7, active: false) + Cell(0, object1: 31)))
        using (var artwork = new BotMapArtwork(map, directory))
        {
            Check(artwork.WaitForAssets(10000) && artwork.LoadedAssetCount == 1 && artwork.AnchoredAssetCount == 0 && artwork.MissingAssetCount == 0,
                "Inactive cell or stale anchor was not handled: " + artwork.Status);
            PointF cell = artwork.Cells[1].Center;
            using (Bitmap scene = Render(artwork))
            {
                Check(!Near(At(scene, artwork.Cells[0].Center, 0, 0), Red), "An inactive cell showed its ground");
                Check(Near(At(scene, cell, 0, -2), Brown) && !Near(At(scene, cell, 12, -10), Brown),
                    "A stale anchor was applied to a PNG of another size");
            }
        }

        // No library at all: flat cells, no exception.
        using (Map map = MapOf(992004, Cell(7, object2: 9) + Cell(8)))
        using (var artwork = new BotMapArtwork(map, Path.Combine(directory, "absent")))
        {
            Check(artwork.WaitForAssets(10000) && artwork.MissingAssetCount == 3 && artwork.LoadedAssetCount == 0, "Missing library broke the map: " + artwork.Status);
            using (Bitmap scene = Render(artwork)) Check(scene.GetPixel(66, 63).A == 0, "Missing library drew something");
            Check(!artwork.HasGroundArtwork(0), "Missing ground claimed scenery");
        }

        // With a UI synchronization context, the result is published on the creator's thread; waiting there cannot deadlock.
        SynchronizationContext previous = SynchronizationContext.Current;
        using (var context = new WindowsFormsSynchronizationContext())
        {
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                using (Map map = MapOf(992005, Cell(7) + Cell(20)))
                using (var artwork = new BotMapArtwork(map, directory))
                {
                    int raisedOn = 0;
                    artwork.AssetsLoaded += (sender, e) => raisedOn = Thread.CurrentThread.ManagedThreadId;
                    Check(artwork.WaitForAssets(10000), "Waiting on the UI thread deadlocked");
                    Application.DoEvents();
                    Check(raisedOn == caller && artwork.LoadThreadId != caller, "AssetsLoaded was not raised on the creator's thread");
                }
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }
        try { Directory.Delete(directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
