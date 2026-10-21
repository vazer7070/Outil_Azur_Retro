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
            + "spell range, pointer shapes, combat path selection and client cursors (lot X8)"); }
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

    private static void Run()
    {
        CheckAimShapes();
        CheckZoneLayer();
        CheckSelectionShapes();
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
