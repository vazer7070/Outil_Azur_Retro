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
        try { Run(); Console.WriteLine("OK: client map PNG layers, asset diagnostics, zoom/pan/resize hit testing and click isolation"); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }

    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
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
