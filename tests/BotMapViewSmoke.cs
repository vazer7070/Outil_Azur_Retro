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
using Tool_BotProtocol.Game.Maps;
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
        try { Run(); Console.WriteLine("OK: client map PNG layers, asset diagnostics, GDC redrawn then restored (lot X7), zoom/pan/resize hit testing and click isolation"); }
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

    private static void Run()
    {
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
