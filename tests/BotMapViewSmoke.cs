using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Controls;
using Outil_Azur_complet.Bot.Interfaces;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Combats;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Perso.Spells;
using Tool_BotProtocol.Utils.Crypto;

internal static class BotMapViewSmoke
{
    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            string file = Path.Combine(TestPaths.ApplicationBin, new AssemblyName(args.Name).Name + ".dll");
            if (!File.Exists(file)) file = Path.ChangeExtension(file, "exe");
            return File.Exists(file) ? Assembly.LoadFrom(file) : null;
        };
        try { Run(); Console.WriteLine("OK: client map PNG layers, asset diagnostics, GDC redrawn then restored (lot X7), zoom/pan/resize hit testing and click isolation, "
            + "spell range, pointer shapes, combat path selection and client cursors (lot X8), tactic mode on and back with the #29261F flat colour "
            + "and valid targets in #0066CC at 50 % (lot X33)"); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }

    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static void WaitFor(Func<bool> done, string what)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(10);
        while (!done()) { if (DateTime.UtcNow > end) throw new TimeoutException("Map view timed out: " + what); Application.DoEvents(); System.Threading.Thread.Sleep(10); }
    }
    /// <summary>Pixel au-dessus du point d'ancrage d'une cellule, sol et objets dessinés dans le repère de la carte.</summary>
    private static Color Pixel(BotMapArtwork scenery, int cellId)
    {
        using (var bitmap = new Bitmap(200, 160))
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            scenery.DrawGround(graphics); scenery.DrawObjects(graphics, null);
            PointF center = scenery.Cells[cellId].Center;
            return bitmap.GetPixel((int)center.X, (int)center.Y - 10);
        }
    }
    private static string EncodedCell(int ground, int object1, int object2, bool flip, int rotation)
    {
        int[] value = new int[10];
        value[0] = 33 | ((ground >> 6) & 24) | ((object1 >> 11) & 4) | ((object2 >> 12) & 2);
        value[1] = 7 | (rotation << 4); value[2] = 32 | ((ground >> 6) & 7); value[3] = ground & 63;
        value[4] = 4 | (flip ? 2 : 0) | ((object1 >> 12) & 1); value[5] = (object1 >> 6) & 63; value[6] = object1 & 63;
        value[7] = (flip ? 8 : 0) | ((object2 >> 12) & 1); value[8] = (object2 >> 6) & 63; value[9] = object2 & 63;
        return new string(value.Select(part => Hash.caracteres_array[part]).ToArray());
    }

    private sealed class TestView : UserMapControl
    {
        public void Down(Point point, MouseButtons button) { OnMouseDown(new MouseEventArgs(button, 1, point.X, point.Y, 0)); }
        public void Drag(Point point, MouseButtons button) { OnMouseMove(new MouseEventArgs(button, 0, point.X, point.Y, 0)); }
        public void Up(Point point, MouseButtons button) { OnMouseUp(new MouseEventArgs(button, 1, point.X, point.Y, 0)); }
        public bool Key(Keys keys) { var message = new Message(); return ProcessCmdKey(ref message, keys); }
    }

    private static void Image(string path, int width, int height, Color color)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using (var bitmap = new Bitmap(width, height))
        {
            using (Graphics graphics = Graphics.FromImage(bitmap)) graphics.Clear(color);
            bitmap.Save(path, ImageFormat.Png);
        }
    }

    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static void AssetsRoot(string value) =>
        typeof(UserMapControl).Assembly.GetType("Outil_Azur_complet.Bot.ClientAssets").GetProperty("Root", Any).SetValue(null, value, null);

    private static Bitmap Render(UserMapControl view)
    {
        var image = new Bitmap(Math.Max(1, view.Width), Math.Max(1, view.Height));
        using (Graphics graphics = Graphics.FromImage(image)) view.DrawCells(graphics);
        return image;
    }
    private static Color PixelAt(UserMapControl view, Point point) { using (Bitmap image = Render(view)) return image.GetPixel(point.X, point.Y); }
    /// <summary>Remplissage du client à <c>Zone.ALPHA</c> (30 %) de <paramref name="rgb"/> sur <paramref name="under"/>.</summary>
    private static Color Blend(Color under, int rgb)
    {
        double alpha = (CellZoneLayer.FillAlphaPercent * 255 / 100) / 255.0;
        Func<int, int, int> mix = (below, over) => (int)Math.Round(below * (1 - alpha) + over * alpha);
        return Color.FromArgb(mix(under.R, (rgb >> 16) & 255), mix(under.G, (rgb >> 8) & 255), mix(under.B, rgb & 255));
    }
    private static bool Near(Color actual, Color expected, int tolerance) =>
        Math.Abs(actual.R - expected.R) <= tolerance && Math.Abs(actual.G - expected.G) <= tolerance && Math.Abs(actual.B - expected.B) <= tolerance;
    /// <summary><paramref name="rgb"/> posé à l'alpha <paramref name="alpha"/> (0 à 255) sur <paramref name="under"/>.</summary>
    private static Color Over(Color under, int rgb, int alpha)
    {
        Func<int, int, int> mix = (below, over) => (int)Math.Round(below + (over - below) * alpha / 255.0);
        return Color.FromArgb(mix(under.R, (rgb >> 16) & 255), mix(under.G, (rgb >> 8) & 255), mix(under.B, rgb & 255));
    }

    /// <summary>
    /// Lot X8 : formes posées sous le pointeur (<c>PointerHandler.draw</c> du client) sur une carte 15 × 17 synthétique ; les
    /// cellules attendues sont tirées des coordonnées X et Y, sans passer par <see cref="SpellAim"/>.
    /// </summary>
    private static void CheckAimShapes()
    {
        string data = string.Concat(Enumerable.Repeat(EncodedCell(11, 0, 0, false, 0), 479));
        using (var map = new Map { MapID = 990003, MapWidth = 15, MapHeight = 17, MapData = data })
        {
            map.DecompressMap(data);
            Cell[] cells = map.MapCells;
            Check(cells.Length == 479, "Synthetic 15 x 17 map has " + cells.Length + " cells");
            Func<Cell, Cell, int> distance = (a, b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
            // Cible assez loin des bords pour que chaque forme soit entière.
            Cell target = cells.OrderBy(c => Math.Abs(c.CellID - 239)).First(c => cells.Count(o => distance(c, o) <= 4) == 41);
            Func<int, int, Cell> at = (x, y) => cells.Single(c => c.X == x && c.Y == y);
            Func<char, int, Cell, short[]> shape = (letter, size, caster) =>
                SpellAim.ShapeCells(new SpellZoneShape(letter, size), caster, target, cells, map.MapWidth).OrderBy(id => id).ToArray();
            Func<Func<Cell, bool>, short[]> expected = keep => cells.Where(keep).Select(c => c.CellID).OrderBy(id => id).ToArray();
            Action<char, int, Cell, short[], int> same = (letter, size, caster, wanted, count) =>
            {
                short[] actual = shape(letter, size, caster);
                Check(actual.Length == count && actual.SequenceEqual(wanted), "Pointer shape " + letter + size + " on cell " + target.CellID
                    + " drew [" + string.Join(",", actual) + "] instead of [" + string.Join(",", wanted) + "]");
            };
            same('C', 2, null, expected(c => distance(target, c) <= 2), 13);
            same('D', 3, null, expected(c => distance(target, c) == 1 || distance(target, c) == 3), 16);
            same('D', 2, null, expected(c => distance(target, c) == 2), 8);
            same('D', 0, null, new short[0], 0);
            same('O', 2, null, expected(c => distance(target, c) == 2), 8);
            same('X', 2, null, expected(c => (c.X == target.X || c.Y == target.Y) && distance(target, c) <= 2), 9);
            // Lanceur à deux cellules vers −X : la ligne prolonge vers +X, la ligne T s'étend de part et d'autre sur Y.
            Cell behind = at(target.X - 2, target.Y);
            same('L', 3, behind, expected(c => c.Y == target.Y && c.X >= target.X && c.X <= target.X + 3), 4);
            same('T', 2, behind, expected(c => c.X == target.X && Math.Abs(c.Y - target.Y) <= 2), 5);
            // Voisine d'identifiant −1 : direction paire (diagonale de l'écran), le client ne prolonge pas la ligne.
            same('L', 3, cells[target.CellID - 1], new[] { target.CellID }, 1);
            same('L', 3, null, new[] { target.CellID }, 1);
            same('P', 1, behind, new[] { target.CellID }, 1);
            same('R', 2, behind, new[] { target.CellID }, 1);
            Check(SpellAim.Direction(behind, target, map.MapWidth) == 1 && SpellAim.Direction(target, behind, map.MapWidth) == 5
                && SpellAim.Direction(target, cells[target.CellID + map.MapWidth], map.MapWidth) == 1
                && SpellAim.Direction(target, cells[target.CellID - 1], map.MapWidth) == 4, "Client getDirection mismatch");

            var stats = new SpellStats { PA = 3, Min_portee = 1, Max_portee = 6, portee_modifiable = true };
            stats.AddEffect(new SpellEffect(100, new Zones(SpellActionZone.CERCLE, 63)), false);
            stats.AddEffect(new SpellEffect(100, new Zones(SpellActionZone.LIGNE, 63)), false);
            stats.AddEffect(new SpellEffect(100, new Zones(SpellActionZone.CROIX, 1)), true);
            SpellAim aim = SpellAim.For(200, stats, 2);
            Check(aim.MaxRange == 8 && aim.EffectZones.Count == 3, "Spell aim lost its range bonus or a zone");
            var pointer = aim.PointerCells(behind, target, cells, map.MapWidth);
            Check(pointer.Count == 2 && pointer[0].Contains(target.CellID) && pointer[0].All(id => cells[id].Y == target.Y && cells[id].X >= target.X)
                && pointer[0].Count == cells.Count(c => c.Y == target.Y && c.X >= target.X) && pointer[1].Count == 5,
                "Pointer did not skip the C63 zone while keeping L63 and the critical cross");
            var parsed = SpellAim.ParseZones("CcXbL");
            Check(parsed.Count == 2 && parsed[0].Shape == 'C' && parsed[0].Size == 2 && parsed[1].Shape == 'X' && parsed[1].Size == 1,
                "Client zone string was misread: " + string.Join(",", parsed));
        }
    }

    /// <summary>Lot X8 : contour d'une zone (trait sur les seules arêtes sans voisine, trou compris pour un anneau).</summary>
    private static void CheckZoneLayer()
    {
        // Losanges de la vue : +X vers le bas à droite, +Y vers le bas à gauche, voisins par une arête.
        Func<int, int, PointF[]> diamond = (x, y) => BotMapArtwork.Diamond(new PointF(300 + (x - y) * BotMapArtwork.CellWidth / 2,
            100 + (x + y) * BotMapArtwork.CellHeight / 2));
        int[][] grid = { new[] { 0, 0 }, new[] { 1, 0 }, new[] { -1, 0 }, new[] { 0, 1 }, new[] { 0, -1 }, new[] { 1, 1 } };
        PointF[][] polygons = grid.Select(point => diamond(point[0], point[1])).ToArray();
        Func<short[], int> edges = ids => { using (CellZoneLayer layer = CellZoneLayer.Build(ids, polygons)) return layer.OutlineEdgeCount; };
        Check(edges(new short[] { 0 }) == 4, "A single cell is not outlined by its four edges");
        Check(edges(new short[] { 0, 1 }) == 6, "Two neighbours kept their shared edge in the outline");
        Check(edges(new short[] { 0, 1, 2, 3, 4 }) == 12, "Circle C1 outline is not its twelve outer edges");
        Check(edges(new short[] { 1, 2, 3, 4 }) == 16, "Ring O1 lost the edges around its hole");
        Check(edges(new short[] { 1, 3 }) == 8 && edges(new short[] { 0, 5 }) == 8, "Cells touching by a corner only were merged");
        Check(CellZoneLayer.Build(new short[0], polygons) == null && CellZoneLayer.Build(null, polygons) == null
            && CellZoneLayer.Build(new short[] { 0 }, null) == null, "Empty zone layer was built");
        using (CellZoneLayer outside = CellZoneLayer.Build(new short[] { 0, 40, -3 }, polygons))
            Check(outside.DrawnCount == 1 && outside.Cells.Count == 3 && outside.OutlineEdgeCount == 4 && outside.Source == polygons,
                "Cells outside the grid were drawn");
        using (var bitmap = new Bitmap(600, 300))
        using (Graphics graphics = Graphics.FromImage(bitmap))
        using (CellZoneLayer layer = CellZoneLayer.Build(new short[] { 0 }, polygons))
        {
            graphics.Clear(Color.White);
            layer.Draw(graphics, Color.FromArgb(UserMapControl.SpellRangeRgb), 1);
            Color inside = bitmap.GetPixel(300, 100);
            Check(Near(inside, Blend(Color.White, UserMapControl.SpellRangeRgb), 2), "Zone fill is not 30 % of #246696: " + inside);
            // Trait opaque d'un pixel : à peu près le périmètre du losange (4 × 29,7 pixels) dans la couleur pleine.
            int outline = 0;
            for (int y = 80; y < 120; y++)
                for (int x = 265; x < 335; x++)
                    if (Near(bitmap.GetPixel(x, y), Color.FromArgb(UserMapControl.SpellRangeRgb), 40)) outline++;
            Check(outline >= 80 && outline <= 260, "Zone outline is not drawn in the opaque colour: " + outline + " pixels");
        }
    }

    /// <summary>
    /// Lot X8 : formes de sélection <c>cellules/s&lt;pente&gt;.png</c> et leur ancre <c>selection</c>, lues hors du fil de
    /// l'interface puis teintes comme <c>setRGB</c> (alpha gardé) ; une forme d'une autre taille que son ancre est refusée.
    /// </summary>
    private static void CheckSelectionShapes()
    {
        string folder = Path.Combine(TestPaths.Work, "selection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, SelectionShapes.Folder));
        File.WriteAllLines(Path.Combine(folder, BotMapArtwork.AnchorFileName), new[]
        {
            "selection\t1\t-26\t-13\t52\t26\t1", "selection\t2\t-26\t-13\t52\t26\t1", "selection\tx\t1\t2", "selection\t3\t0\t0\t-5\t26\t1"
        });
        Image(Path.Combine(folder, SelectionShapes.Folder, "s1.png"), 52, 26, Color.FromArgb(254, 0, 153, 255));
        Image(Path.Combine(folder, SelectionShapes.Folder, "s2.png"), 40, 20, Color.White);
        Image(Path.Combine(folder, SelectionShapes.Folder, "s3.png"), 52, 26, Color.White);
        SelectionShapes shapes = SelectionShapes.For(folder);
        Check(SelectionShapes.For(folder + Path.DirectorySeparatorChar) == shapes && SelectionShapes.For("") == null,
            "Selection shapes are not shared per folder");
        int loaded = 0;
        shapes.Loaded += () => System.Threading.Interlocked.Increment(ref loaded);
        Check(!shapes.TryGet(1, UserMapControl.SpellEffectRgb, out Bitmap image, out RectangleF anchor) && image == null,
            "Selection shape was read on the calling thread");
        Check(!shapes.TryGet(2, UserMapControl.SpellEffectRgb, out image, out anchor) && !shapes.TryGet(3, UserMapControl.SpellEffectRgb, out image, out anchor),
            "Selection shape without a valid anchor was returned at once");
        WaitFor(() => shapes.Idle && loaded >= 3, "selection shapes loaded");
        Check(shapes.TryGet(1, UserMapControl.SpellEffectRgb, out image, out anchor) && anchor == new RectangleF(-26, -13, 52, 26)
            && image.Width == 52 && image.Height == 26, "Selection shape s1 or its anchor is wrong: " + anchor);
        Color tinted; lock (image) tinted = image.GetPixel(26, 13);
        // libgdiplus garde les pixels prémultipliés : une unité d'écart sur la couleur à l'alpha 254.
        Check(tinted.A == 254 && Near(tinted, Color.FromArgb(UserMapControl.SpellEffectRgb), 1), "Selection shape was not tinted like setRGB: " + tinted);
        Check(!shapes.TryGet(2, UserMapControl.SpellEffectRgb, out image, out anchor) && !shapes.TryGet(3, UserMapControl.SpellEffectRgb, out image, out anchor),
            "Selection shape with a size other than its anchor (or a malformed anchor) was used");
        Check(shapes.TryGet(99, UserMapControl.SpellEffectRgb, out image, out anchor) && anchor.Width == 52,
            "Unknown ground slope did not fall back to the flat shape");
        Check(!shapes.TryGet(1, UserMapControl.SpellRangeRgb, out image, out anchor), "Another colour reused the orange shape");
        WaitFor(() => shapes.Idle, "selection shape in another colour");
        Check(shapes.TryGet(1, UserMapControl.SpellRangeRgb, out image, out anchor), "Selection shape in #246696 was not built");
        lock (image) tinted = image.GetPixel(10, 10);
        Check(Near(tinted, Color.FromArgb(UserMapControl.SpellRangeRgb), 1), "Selection shape colour mismatch: " + tinted);
    }

    /// <summary>Cellule active ou non, avec ou sans ligne de vue et de mouvement <paramref name="movement"/> (bits de <c>Cell.decompress</c>).</summary>
    private static string TacticCell(int ground, int object1, int object2, bool active, bool lineOfSight, int movement)
    {
        char[] chars = EncodedCell(ground, object1, object2, false, 0).ToCharArray();
        int first = Hash.get_Hash(chars[0]), third = Hash.get_Hash(chars[2]);
        chars[0] = Hash.caracteres_array[(first & ~33) | (active ? 32 : 0) | (lineOfSight ? 1 : 0)];
        chars[2] = Hash.caracteres_array[(third & ~56) | ((movement & 7) << 3)];
        return new string(chars);
    }

    private static void Diamond(string path, int width, int height, Color color)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using (var bitmap = new Bitmap(width, height))
        {
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (var brush = new SolidBrush(color))
            {
                graphics.Clear(Color.Transparent);
                graphics.FillPolygon(brush, new[] { new PointF(width / 2f, 0), new PointF(width, height / 2f), new PointF(width / 2f, height), new PointF(0, height / 2f) });
            }
            bitmap.Save(path, ImageFormat.Png);
        }
    }

    /// <summary>Pixel au point d'enregistrement d'une cellule : fond, sols, objets 1 puis objets 2 du décor affiché.</summary>
    private static Color CentrePixel(BotMapArtwork scenery, int cellId)
    {
        using (var bitmap = new Bitmap(200, 160))
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            scenery.DrawGround(graphics); scenery.DrawObjects(graphics, null);
            PointF center = scenery.DisplayedCells[cellId].Center;
            return bitmap.GetPixel((int)center.X, (int)center.Y);
        }
    }

    /// <summary>
    /// Lot X33 : mode tactique du décor (<c>mapHandler.tacticMode</c> et <c>CellData.turnTactic</c> du client) sur une bibliothèque
    /// temporaire : objet 1 10000 sans ligne de vue, 10002 pour un mouvement 0 ou 1, 10001 sinon, objet 2 1030 sur une cellule de
    /// changement de carte, 25 gardé, autres objets 2 retirés, sol retiré, aplat #29261F à la place du fond ; cellule inactive
    /// inchangée ; un GDC de ligne de vue refait la cellule tactique ; retour au décor normal.
    /// </summary>
    private static void CheckTacticMode()
    {
        string folder = Path.Combine(TestPaths.Work, "tactic-" + Guid.NewGuid().ToString("N"));
        Color ground = Color.FromArgb(180, 150, 75), back = Color.FromArgb(240, 230, 140), trigger = Color.FromArgb(230, 200, 0);
        Color walkable = Color.FromArgb(142, 134, 94), obstacle = Color.FromArgb(153, 153, 153), hole = Color.FromArgb(51, 51, 51);
        Action<string, bool> library = (directory, withHole) =>
        {
            Diamond(Path.Combine(directory, "sols", "11.png"), 53, 27, ground);
            Image(Path.Combine(directory, "objets", "12.png"), 13, 35, Color.ForestGreen);
            Image(Path.Combine(directory, "objets", "13.png"), 15, 28, Color.SteelBlue);
            Image(Path.Combine(directory, "objets", "1029.png"), 20, 12, Color.Magenta);
            Image(Path.Combine(directory, "objets", "25.png"), 1, 1, Color.Transparent);
            Diamond(Path.Combine(directory, "objets", "10000.png"), 54, 36, obstacle);
            Diamond(Path.Combine(directory, "objets", "10001.png"), 54, 29, walkable);
            if (withHole) Diamond(Path.Combine(directory, "objets", "10002.png"), 54, 28, hole);
            Diamond(Path.Combine(directory, "objets", "1030.png"), 27, 15, trigger);
            Image(Path.Combine(directory, "backgrounds", "81.png"), 120, 70, back);
            File.WriteAllLines(Path.Combine(directory, BotMapArtwork.AnchorFileName), new[]
            {
                "sol\t11\t-27\t-14\t53\t27\t1", "objet\t12\t-6\t-20\t13\t35\t1", "objet\t13\t-7\t-20\t15\t28\t1", "objet\t1029\t-10\t-6\t20\t12\t1",
                "objet\t25\t0\t0\t1\t1\t1", "objet\t10000\t-27\t-21\t54\t36\t1", "objet\t10001\t-27\t-14\t54\t29\t1", "objet\t10002\t-27\t-14\t54\t28\t1",
                "objet\t1030\t-13\t-7\t27\t15\t1"
            });
        };
        library(folder, true);
        // 5 : arbre sans ligne de vue ; 6 et 13 : mouvement 1 et 0 ; 7 et 10 : objet 2 puis objet 1 de changement de carte (1029) ;
        // 8 : objet 2 ordinaire ; 9 : objet 25 ; 12 : cellule inactive.
        string[] cells = Enumerable.Range(0, 18).Select(id => TacticCell(11, 0, 0, true, true, 4)).ToArray();
        cells[5] = TacticCell(11, 12, 0, true, false, 0); cells[6] = TacticCell(11, 0, 0, true, true, 1); cells[7] = TacticCell(11, 0, 1029, true, true, 4);
        cells[8] = TacticCell(11, 0, 13, true, true, 4); cells[9] = TacticCell(11, 0, 25, true, true, 4); cells[10] = TacticCell(11, 1029, 0, true, true, 4);
        cells[12] = TacticCell(11, 12, 13, false, true, 4); cells[13] = TacticCell(11, 0, 0, true, true, 0);
        string data = string.Concat(cells);
        using (var map = new Map { MapID = 990003, MapWidth = 3, MapHeight = 4, MapData = data, Back_ID = 81 })
        {
            map.DecompressMap(data);
            using (var scenery = new BotMapArtwork(map, folder))
            {
                Check(scenery.WaitForAssets(10000), "Tactic fixture PNG loading did not finish");
                Check(!scenery.TacticMode && ReferenceEquals(scenery.DisplayedCells, scenery.Cells), "Map artwork started in tactic mode");
                Check(scenery.MissingAssetCount == 0 && scenery.LoadedAssetCount == 6 && scenery.LoadedTacticAssetCount == 5,
                    "Tactic pictures were not loaded apart from the scenery: " + scenery.LoadedAssetCount + " / " + scenery.LoadedTacticAssetCount + " · " + scenery.Status);
                Check(scenery.Status == "Décor chargé · 6 visuels", "Tactic pictures were counted as scenery: " + scenery.Status);
                Check(Near(CentrePixel(scenery, 0), ground, 1) && CentrePixel(scenery, 5).G == Color.ForestGreen.G && Near(CentrePixel(scenery, 12), back, 1),
                    "Normal scenery is not drawn before the tactic mode: " + CentrePixel(scenery, 0) + " " + CentrePixel(scenery, 12));

                scenery.TacticMode = true;
                BotMapArtwork.ArtworkCell[] shown = scenery.DisplayedCells;
                Check(scenery.TacticMode && !ReferenceEquals(shown, scenery.Cells) && shown.Length == 18, "Tactic mode did not switch the drawn cells");
                Check(shown.Where(cell => cell.Active).All(cell => cell.GroundId == 0 && cell.Slope == 1 && cell.Object1Rotation == 0),
                    "Tactic cells kept a ground, a slope or an object rotation");
                Check(shown[0].Object1Id == 10001 && shown[5].Object1Id == 10000 && shown[6].Object1Id == 10002 && shown[13].Object1Id == 10002
                    && shown[7].Object1Id == 10001 && shown[10].Object1Id == 10001, "turnTactic object 1 mismatch (line of sight, then movement 0 or 1)");
                Check(shown[7].Object2Id == 1030 && shown[10].Object2Id == 1030 && shown[8].Object2Id == 0 && shown[9].Object2Id == 25 && shown[5].Object2Id == 0,
                    "turnTactic object 2 mismatch (1030 on a map change cell, 25 kept, others removed)");
                Check(ReferenceEquals(shown[12], scenery.Cells[12]) && !shown[12].Active, "Inactive cell was rebuilt by the tactic mode");
                Check(scenery.Cells[0].GroundId == 11 && scenery.Cells[5].Object1Id == 12 && scenery.Cells[7].Object2Id == 1029, "Tactic mode changed the map cells");
                Check(Near(CentrePixel(scenery, 0), walkable, 1) && Near(CentrePixel(scenery, 8), walkable, 1) && Near(CentrePixel(scenery, 5), obstacle, 1)
                    && Near(CentrePixel(scenery, 6), hole, 1) && Near(CentrePixel(scenery, 13), hole, 1)
                    && Near(CentrePixel(scenery, 7), trigger, 1) && Near(CentrePixel(scenery, 10), trigger, 1),
                    "Tactic objects are not drawn on their cells: " + CentrePixel(scenery, 0) + " " + CentrePixel(scenery, 5) + " " + CentrePixel(scenery, 7));
                Check(Near(CentrePixel(scenery, 12), BotMapArtwork.TacticBackground, 1) && BotMapArtwork.TacticBackground.ToArgb() == Color.FromArgb(0x29, 0x26, 0x1F).ToArgb(),
                    "Tactic flat colour #29261F did not replace the background: " + CentrePixel(scenery, 12));
                Check(scenery.HasGroundArtwork(0) && scenery.HasGroundArtwork(12) && scenery.Status == "Mode tactique", "Tactic mode diagnostics mismatch: " + scenery.Status);
                Check(scenery.LoadedAssetCount == 6 && scenery.MissingAssetCount == 0, "Tactic mode changed the scenery counts");

                // GDC ne touchant que la ligne de vue (masque 0x1000) : le décor normal ne change pas, la cellule tactique devient 10000.
                var changed = new System.Collections.Concurrent.ConcurrentQueue<int>();
                scenery.CellChanged += id => changed.Enqueue(id);
                Check(map.ApplyCellUpdate(GmParser.ParseCellUpdates("0;" + TacticCell(11, 0, 0, true, false, 4) + "1000;0")[0]), "GDC0 line of sight was refused");
                Check(scenery.UpdateCell(0, map.CellDataOf(0)), "Line of sight GDC was not a tactic change");
                WaitFor(() => changed.Contains(0), "tactic cell 0 redrawn");
                Check(scenery.DisplayedCells[0].Object1Id == 10000 && scenery.Cells[0].GroundId == 11 && Near(CentrePixel(scenery, 0), obstacle, 1),
                    "Cell without line of sight is not 10000 in tactic mode: " + CentrePixel(scenery, 0));
                Check(map.ApplyCellUpdate(GmParser.ParseCellUpdates("0")[0]) && scenery.UpdateCell(0, map.CellDataOf(0)), "GDC0 did not restore the line of sight");
                WaitFor(() => scenery.DisplayedCells[0].Object1Id == 10001, "tactic cell 0 restored");

                scenery.TacticMode = false;
                Check(ReferenceEquals(scenery.DisplayedCells, scenery.Cells) && Near(CentrePixel(scenery, 0), ground, 1)
                    && CentrePixel(scenery, 5).G == Color.ForestGreen.G && Near(CentrePixel(scenery, 12), back, 1) && scenery.Status == "Décor chargé · 6 visuels",
                    "Normal scenery did not come back after the tactic mode: " + CentrePixel(scenery, 0) + " " + CentrePixel(scenery, 12));
            }
            // Visuel tactique absent : signalé en mode tactique seulement, jamais parmi les visuels du décor.
            string partial = Path.Combine(TestPaths.Work, "tactic-partial-" + Guid.NewGuid().ToString("N"));
            library(partial, false);
            using (var scenery = new BotMapArtwork(map, partial))
            {
                Check(scenery.WaitForAssets(10000) && scenery.MissingAssetCount == 0 && scenery.LoadedTacticAssetCount == 4, "Missing tactic picture polluted the scenery");
                scenery.TacticMode = true;
                Check(scenery.Status.StartsWith("Mode tactique · 1 visuel(s) absent(s)", StringComparison.Ordinal) && scenery.Status.Contains("objet 10002")
                    && scenery.HasGroundArtwork(0) && !scenery.HasGroundArtwork(6), "Missing tactic picture was not reported: " + scenery.Status);
                scenery.TacticMode = false;
                Check(scenery.Status == "Décor chargé · 6 visuels", "Tactic diagnostics stayed after the tactic mode: " + scenery.Status);
            }
        }
    }

    private static void Run()
    {
        CheckAimShapes();
        CheckZoneLayer();
        CheckSelectionShapes();
        CheckTacticMode();
        string directory = Path.Combine(TestPaths.Work, "map-view-assets");
        string groundPath = Path.Combine(directory, "sols", "fixtures", "11.png");
        Image(groundPath, 53, 27, Color.FromArgb(180, 150, 75));
        Image(Path.Combine(directory, "objets", "12.png"), 13, 35, Color.ForestGreen);
        Image(Path.Combine(directory, "objets", "13.png"), 15, 28, Color.SteelBlue);
        Image(Path.Combine(directory, "backgrounds", "81.png"), 120, 70, Color.Khaki);
        using (var margin = new Bitmap(600, 600))
        {
            using (Graphics graphics = Graphics.FromImage(margin))
            {
                graphics.Clear(Color.Transparent);
                graphics.FillRectangle(Brushes.ForestGreen, 285, 540, 30, 40);
            }
            margin.Save(Path.Combine(directory, "objets", "14.png"), ImageFormat.Png);
        }
        string data = string.Concat(Enumerable.Range(0, 18).Select(id => EncodedCell(11, id == 5 ? 12 : 0, id == 7 ? 13 : 0, id == 5, id == 6 ? 1 : 0)));
        using (var map = new Map { MapID = 990001, MapWidth = 3, MapHeight = 4, MapData = data, Back_ID = 81 })
        {
            map.DecompressMap(data);
            using (var scenery = new BotMapArtwork(map, directory))
            {
                Check(scenery.Cells.Length == 18 && scenery.Cells[5].Object1Id == 12 && scenery.Cells[7].Object2Id == 13,
                    "Map artwork lost an encoded scenery layer");
                Check(scenery.Cells[5].GroundFlip && scenery.Cells[5].Object1Flip && scenery.Cells[6].GroundRotation == 1,
                    "Map artwork ignored flips or rotations");
                Check(scenery.WaitForAssets(10000), "Map artwork PNG loading did not finish");
                Check(scenery.MissingAssetCount == 0 && scenery.LoadedAssetCount >= 5, "Valid fixture PNG library was reported missing");
                using (FileStream exclusive = File.Open(groundPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    Check(exclusive.Length > 0, "Scenery retained a file lock");
                using (var bitmap = new Bitmap(200, 160))
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    scenery.DrawGround(graphics); scenery.DrawObjects(graphics, null);
                    PointF center = scenery.Cells[5].Center;
                    Check(bitmap.GetPixel((int)center.X, (int)center.Y - 10).G == Color.ForestGreen.G,
                        "Object layer did not align its visible anchor to its cell");
                }

                // Lot X7 : GDC rejoué sur le décor. Masque 4 (object2Num) : l'objet 2 de la cellule 7 passe de 13 (SteelBlue) à 12
                // (ForestGreen) sans toucher au sol ; GDC7 seul remet l'image d'origine. Le masque 0x3005 du piège de StarLoco
                // (Haaaaaaaaz) ne touche que l'activité, la ligne de vue et l'objet 2 (25).
                string trap = Map.MergeCellData(data.Substring(70, 10), "Haaaaaaaaz", 0x3005);
                Check(map.DecompressCell(trap, 7).layer_object_2_num == 25 && map.DecompressCell(trap, 7).IsActive && map.DecompressCell(trap, 7).LineofSight
                    && trap.Substring(1, 6) == data.Substring(71, 6), "Trap GDC mask 0x3005 changed other fields than active, line of sight and object 2: " + trap);
                Check(Map.MergeCellData(data.Substring(70, 10), "Haaaaaaaaz", 0) == data.Substring(70, 10), "Empty GDC mask replaced a field");
                bool refused = false;
                try { Map.MergeCellData("abc", "Haaaaaaaaz", 1); } catch (ArgumentException) { refused = true; }
                Check(refused, "Malformed cell data was merged");
                var changed = new System.Collections.Concurrent.ConcurrentQueue<int>();
                scenery.CellChanged += id => changed.Enqueue(id);
                Check(!scenery.UpdateCell(7, map.CellDataOf(7)) && !scenery.UpdateCell(99, map.CellDataOf(7)) && !scenery.UpdateCell(7, "trop court"),
                    "Unchanged, out-of-map or malformed cell data was accepted as a visual change");
                Check(map.ApplyCellUpdate(GmParser.ParseCellUpdates("7;" + EncodedCell(11, 0, 12, false, 0) + "4;0")[0]), "GDC7 with the object2 mask was refused");
                string updated = map.CellDataOf(7);
                Check(updated != data.Substring(70, 10) && map.DecompressCell(updated, 7).layer_object_2_num == 12 && map.CellDataOf(5) == data.Substring(50, 10),
                    "GDC mask 4 did not replace the object 2 of cell 7 only");
                Check(scenery.UpdateCell(7, updated), "Artwork refused the GDC cell update");
                WaitFor(() => changed.Contains(7), "artwork cell 7 redrawn after GDC");
                Check(scenery.Cells[7].Object2Id == 12 && scenery.Cells[7].GroundId == 11 && scenery.MissingAssetCount == 0, "GDC cell kept its former object or lost its ground");
                Check(Pixel(scenery, 7).G == Color.ForestGreen.G && Pixel(scenery, 7).B == Color.ForestGreen.B, "Updated object 2 is not drawn on its cell: " + Pixel(scenery, 7));
                Check(map.ApplyCellUpdate(GmParser.ParseCellUpdates("7")[0]) && map.CellDataOf(7) == data.Substring(70, 10), "GDC7 alone did not restore the cell data");
                while (changed.TryDequeue(out int ignored)) { }
                Check(scenery.UpdateCell(7, map.CellDataOf(7)), "Artwork refused the restored cell");
                WaitFor(() => changed.Contains(7), "artwork cell 7 restored");
                Check(scenery.Cells[7].Object2Id == 13 && Pixel(scenery, 7).B == Color.SteelBlue.B && Pixel(scenery, 7).R == Color.SteelBlue.R, "Restored object 2 is not drawn again: " + Pixel(scenery, 7));
            }
            map.MapData = EncodedCell(1900, 0, 0, false, 0) + data.Substring(10);
            using (var missing = new BotMapArtwork(map, directory))
                Check(missing.WaitForAssets(10000) && missing.MissingAssetCount == 1 && missing.Status.Contains("sol 1900"), "Missing scenery silently disappeared");
            map.MapData = EncodedCell(11, 14, 0, false, 0) + data.Substring(10);
            using (var transparent = new BotMapArtwork(map, directory))
                Check(transparent.WaitForAssets(10000) && transparent.WorldBounds.Width < 220 && transparent.WorldBounds.Height < 200,
                    "Transparent PNG margins shrank the fitted map");
        }

        using (var account = new Accounts(new AccountConfig("synthetic-map-view", "Synthetic123", "test")))
        {
            Map map = account.Game.Map;
            map.MapID = 990002; map.MapWidth = 3; map.MapHeight = 4; map.MapData = data; map.DecompressMap(data);
            using (var view = new TestView())
            {
                view.SetAccount(account); view.W = 3; view.H = 4; view.Size = new Size(900, 580); view.SetCellNum(); view.DrawGrille();
                foreach (UserMapCell cell in view.Cells) Check(view.GetCell(cell.Centre).id == cell.id, "Fit hit target mismatch " + cell.id);
                Point anchor = view.Cells[10].Centre;
                view.ZoomAt(2.4, anchor);
                Check(view.GetCell(anchor).id == 10 && view.ZoomPercent == 240, "Wheel zoom did not preserve cell under cursor");
                Point beforePan = view.Cells[10].Centre;
                view.PanBy(new Point(31, -19));
                Point afterPan = view.Cells[10].Centre;
                Check(afterPan.X == beforePan.X + 31 && afterPan.Y == beforePan.Y - 19 && view.GetCell(afterPan).id == 10,
                    "Panned scenery diverged from clickable polygons");
                int clicks = 0;
                view.CellClicked += (cell, button, moved) => clicks++;
                view.Down(afterPan, MouseButtons.Middle); view.Drag(new Point(afterPan.X + 25, afterPan.Y + 12), MouseButtons.Middle);
                view.Up(new Point(afterPan.X + 25, afterPan.Y + 12), MouseButtons.Middle);
                Check(clicks == 0, "Panning accidentally issued a movement click");
                view.Fit(); view.Size = new Size(560, 370);
                Check(view.ZoomPercent == 100, "Fit did not restore full map");
                // + et - du pavé numérique restent aux raccourcis MAXI / MINI du chat ; ceux du clavier principal zooment.
                Check(!view.Key(Keys.Add) && !view.Key(Keys.Subtract) && view.ZoomPercent == 100, "Keypad + / - were taken by the map zoom");
                Check(view.Key(Keys.Oemplus) && view.ZoomPercent > 100 && view.Key(Keys.Home) && view.ZoomPercent == 100, "Main keyboard + or Home does not zoom the map");
                foreach (UserMapCell cell in view.Cells)
                    Check(view.ClientRectangle.Contains(cell.Centre) && view.GetCell(cell.Centre).id == cell.id, "Resize clipped or displaced cell " + cell.id);
                Point click = view.Cells[10].Centre;
                view.Down(click, MouseButtons.Left); view.Up(click, MouseButtons.Left);
                Check(clicks == 1, "Regular map movement click was swallowed");

                // ---------------------------------------------------------------- lot X8 : portée, pointeur, chemin, curseurs
                string cursors = Path.Combine(TestPaths.Work, "map-view-cursors-" + Guid.NewGuid().ToString("N"));
                Image(Path.Combine(cursors, "UI", "Client", UserMapControl.ForbiddenCursor + ".png"), 48, 48, Color.FromArgb(255, 220, 0, 0));
                Image(Path.Combine(cursors, "UI", "Client", UserMapControl.MoveCursor + ".png"), 40, 40, Color.FromArgb(255, 0, 0, 220));
                Image(Path.Combine(cursors, "UI", "Client", "DefaultCC.png"), 30, 30, Color.FromArgb(255, 0, 200, 0));
                AssetsRoot(cursors);
                // Dans une fenêtre, comme dans le bot : l'infobulle de la cellule visée a besoin d'une fenêtre parente.
                var host = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(0, 0), ClientSize = view.Size };
                try
                {
                    host.Controls.Add(view); host.Show(); Application.DoEvents();
                    (typeof(UserMapControl).GetField("artwork", Any).GetValue(view) as BotMapArtwork)?.WaitForAssets(10000);
                    Point target = view.Cells[10].Centre, refused = view.Cells[11].Centre, step = view.Cells[12].Centre;
                    Color ground = PixelAt(view, refused), targetGround = PixelAt(view, target);
                    // Portée et pointeur seuls : la mise en valeur des cibles (lot X33) est vérifiée plus bas, option activée.
                    var displayOptions = new BotOptions(Path.Combine(TestPaths.Work, "map-view-options", "BotOptions.json")) { AutoSave = false, AdvancedLineOfSight = false };
                    view.Options = displayOptions;
                    view.SetSpellTargets(new short[] { 10 });
                    view.SetSpellRange(new short[] { 10, 11 });
                    Check(view.IsAiming && view.Cursor == Cursors.Default && view.SpellRange.Count == 2, "Spell aim did not keep the arrow or lost its range");
                    Color range = PixelAt(view, refused);
                    Check(Near(range, Blend(ground, UserMapControl.SpellRangeRgb), 3), "Spell range is not 30 % of #246696: " + ground + " -> " + range);

                    view.PointerShapes = id => new[] { new short[] { id } };
                    view.Drag(target, MouseButtons.None);
                    Check(view.HoveredCell?.id == 10 && view.PointerCells.Count == 1 && view.PointerCells[0].Single() == 10 && !view.CursorForbidden,
                        "Pointer shape was not posed on the valid target");
                    Color pointer = PixelAt(view, target);
                    Check(Near(pointer, Blend(Blend(targetGround, UserMapControl.SpellRangeRgb), UserMapControl.SpellEffectRgb), 4),
                        "Pointer is not 30 % of #FF6600 over the range: " + pointer);
                    Rectangle box = view.CursorBounds(target);
                    Check(box == new Rectangle(target.X + 15, target.Y + 15, 25, 25), "Spell cursor box is not 25 x 25 at +15,+15: " + box);
                    view.SetAimIcon("Client", "DefaultCC");
                    Check(view.AimIcon == "Client/DefaultCC", "Aim icon was not kept");
                    var iconCentre = new Point(box.X + box.Width / 2, box.Y + box.Height / 2);
                    WaitFor(() => { Color c = PixelAt(view, iconCentre); return c.G > 180 && c.R < 30; }, "spell icon drawn beside the arrow");

                    view.Drag(refused, MouseButtons.None);
                    Check(view.HoveredCell?.id == 11 && view.CursorForbidden && view.PointerCells.Count == 0,
                        "Cell outside the targets kept the pointer or lost the forbidden cross");
                    Rectangle cross = view.CursorBounds(refused);
                    var crossCentre = new Point(cross.X + cross.Width / 2, cross.Y + cross.Height / 2);
                    WaitFor(() => { Color c = PixelAt(view, crossCentre); return c.R > 190 && c.G < 30 && c.B < 30; }, "forbidden cross drawn over the spell icon");
                    Check(Near(PixelAt(view, refused), range, 1), "Pointer was drawn on a cell outside the targets");

                    // Chemin de combat : forme de sélection de la pente, teinte opaque, au-dessus de la portée.
                    view.SetPathPreview(new short[] { 12 }, true);
                    Check(view.PathPreviewUsesSelection, "Combat path preview did not use the client selection");
                    Color path = PixelAt(view, step);
                    Check(path.R > 225 && path.G >= 80 && path.G <= 125 && path.B < 45, "Combat path cell is not opaque #FF6600: " + path);
                    view.SetPathPreview(new short[] { 12 });
                    Check(!view.PathPreviewUsesSelection, "Out-of-fight preview kept the combat selection");
                    view.SetPathPreview(null);

                    // Lot X33 : drawAllowedZone du client (option AdvancedLineOfSight) : forme de sélection #0066CC à 50 % sur
                    // chaque cible valide, au-dessus de la portée ; rien sur une cellule de la portée hors des cibles.
                    Check(!view.AllowedZoneShown && Near(PixelAt(view, target), Blend(targetGround, UserMapControl.SpellRangeRgb), 3),
                        "Valid target was highlighted with the option off: " + PixelAt(view, target));
                    displayOptions.AdvancedLineOfSight = true;
                    Check(view.AllowedZoneShown, "AdvancedLineOfSight did not show the valid targets");
                    BotMapArtwork viewArtwork = typeof(UserMapControl).GetField("artwork", Any).GetValue(view) as BotMapArtwork;
                    SelectionShapes viewShapes = SelectionShapes.For(viewArtwork.ResourceDirectory);
                    PixelAt(view, target);
                    WaitFor(() => viewShapes.Idle, "selection shape in #0066CC");
                    Check(viewShapes.TryGet(viewArtwork.DisplayedCells[10].Slope, UserMapControl.AllowedTargetRgb, out Bitmap allowedShape, out RectangleF allowedAnchor),
                        "Selection shape in #0066CC was not built from the scenery");
                    int shapeAlpha; lock (allowedShape) shapeAlpha = allowedShape.GetPixel(allowedShape.Width / 2, allowedShape.Height / 2).A;
                    Color expectedAllowed = Over(Blend(targetGround, UserMapControl.SpellRangeRgb), UserMapControl.AllowedTargetRgb, shapeAlpha * 127 / 255);
                    Color allowed = PixelAt(view, target);
                    Check(Near(allowed, expectedAllowed, 4), "Valid target is not #0066CC at 50 % over the range: " + allowed + ", expected " + expectedAllowed);
                    Check(Near(PixelAt(view, refused), range, 1), "Cell of the range outside the targets was highlighted");
                    displayOptions.AdvancedLineOfSight = false;
                    Check(!view.AllowedZoneShown && Near(PixelAt(view, target), Blend(targetGround, UserMapControl.SpellRangeRgb), 3), "Valid targets stayed highlighted");
                    displayOptions.AdvancedLineOfSight = true;

                    // Lot X33 : mode tactique (Fights.TacticMode, local) dessiné seulement en combat, puis retour au décor normal.
                    Fights fight = account.Game.Fight;
                    Check(!view.TacticView, "Tactic view outside a fight");
                    MessagesReception.Init();
                    MessagesReception.ReceptionAsync(account.Connexion, "GJK2|1|1|0|30000|0").GetAwaiter().GetResult();
                    Check(fight.IsInFight && !fight.TacticMode && !view.TacticView, "GJK did not open the fight or turned the tactic mode on");
                    Color normalRefused = PixelAt(view, refused), normalTarget = PixelAt(view, target);
                    fight.TacticMode = true;
                    Check(view.TacticView, "Fights.TacticMode is not followed by the map");
                    Color tacticRefused = PixelAt(view, refused);
                    Check(viewArtwork.TacticMode && !ReferenceEquals(viewArtwork.DisplayedCells, viewArtwork.Cells)
                        && viewArtwork.DisplayedCells[11].Object1Id == BotMapArtwork.TacticWalkableObject && viewArtwork.DisplayedCells[11].GroundId == 0,
                        "Map scenery was not drawn in tactic mode");
                    Check(!Near(tacticRefused, normalRefused, 6), "Tactic mode did not change the drawn cell: " + tacticRefused);
                    Check(viewArtwork.Status.StartsWith("Mode tactique", StringComparison.Ordinal), "Tactic mode status mismatch: " + viewArtwork.Status);
                    fight.TacticMode = false;
                    Check(!view.TacticView && Near(PixelAt(view, refused), normalRefused, 1) && Near(PixelAt(view, target), normalTarget, 1)
                        && !viewArtwork.TacticMode && ReferenceEquals(viewArtwork.DisplayedCells, viewArtwork.Cells), "Normal scenery did not come back after the tactic mode");
                    // Game.isTacticMode du client persiste d'un combat à l'autre : hors combat, le décor reste normal.
                    fight.TacticMode = true;
                    fight.Clear();
                    Check(fight.TacticMode && !fight.IsInFight && !view.TacticView && Near(PixelAt(view, refused), normalRefused, 1),
                        "Tactic mode was drawn outside a fight or was reset with it");
                    fight.TacticMode = false;

                    // Glisser de la vue : quatre flèches 40 × 40 centrées sur la souris (cadre par défaut des curseurs du client).
                    view.Down(step, MouseButtons.Middle);
                    var dragged = new Point(step.X + 6, step.Y + 4);
                    view.Drag(dragged, MouseButtons.Middle);
                    Check(view.CursorBounds(dragged) == new Rectangle(dragged.X - 20, dragged.Y - 20, 40, 40) && view.Cursor == Cursors.Default,
                        "Panning cursor box is not 40 x 40 centred on the mouse");
                    WaitFor(() => { Color c = PixelAt(view, dragged); return c.B > 190 && c.R < 30 && c.G < 30; }, "move cursor drawn while panning");
                    view.Up(dragged, MouseButtons.Middle);
                    Check(view.CursorBounds(dragged) == new Rectangle(dragged.X + 15, dragged.Y + 15, 25, 25) && clicks == 1,
                        "Spell cursor did not come back after panning, or panning clicked");
                    view.SetSpellTargets(null); view.SetSpellRange(null); view.PointerShapes = null; view.SetAimIcon(null, null);
                    Check(!view.IsAiming && view.SpellRange.Count == 0 && view.PointerCells.Count == 0 && view.AimIcon == null
                        && view.CursorBounds(dragged).IsEmpty && !view.CursorForbidden, "Spell aim was not cleared");
                    view.Fit();
                }
                finally { host.Controls.Remove(view); host.Dispose(); AssetsRoot(null); }
                view.ShowGrid = true; view.ShowCellId = true;
                Check(view.ShowGrid && view.ShowCellId, "Client map diagnostic toggles were ignored");
            }
            using (var container = new MapControl(account))
            {
                container.Size = new Size(900, 580); container.ZoomIn(); Check(container.ZoomPercent > 100, "Map zoom toolbar API is inert");
                container.Fit(); Check(container.ZoomPercent == 100, "Map fit toolbar API is inert");
            }
        }
    }
}
