using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Outil_Azur_complet;
using Outil_Azur_complet.Parser;
using Tool_Editor.maps.data;
using Tool_Editor.maps.managers;
using Tools_protocol.Network;

internal static class ResourceManagerSmoke
{
    private static object Get(object target, string name) { return target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(target); }
    private static void Call(object target, string name) { target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null); }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    [STAThread] private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) => { string path = Path.Combine(TestPaths.ApplicationBin, new AssemblyName(e.Name).Name + ".dll"); if (!File.Exists(path)) path = Path.ChangeExtension(path, ".exe"); return File.Exists(path) ? Assembly.LoadFrom(path) : null; };
        Application.EnableVisualStyles(); Run();
    }
    private static void Run()
    {
        var original = new Map { ID = 100, Width = 15, Height = 17, Capabilities = 98, DateMap = "test", IsOutDoor = true, Musique = 12, Ambiance = 4, X = -3, Y = 7, HasProjectCells = true,
            Cells = Enumerable.Range(0, 479).Select(i => new CellsData { ID = i }).ToArray() };
        original.Cells[478].FightCell = 2; original.Cells[20].Trigger = true; original.Cells[20].TriggerName = "sortie";
        string plain = string.Concat(original.Cells.Select(BuilderClass.GetCellData)); string hexKey = "6B6579";
        var cipher = new StringBuilder(); for (int i = 0; i < plain.Length; i++) cipher.Append(((int)(plain[i] ^ "key"[(i + 18) % 3])).ToString("X2"));
        Check(ResourceMapConversion.Decrypt(cipher.ToString(), hexKey, 15, 17) == plain, "15x17 converter rejects official 479-cell data");
        try { ResourceMapConversion.Decrypt(cipher.ToString().Substring(0, cipher.Length - 20), hexKey, 15, 17); throw new Exception("478-cell input accepted"); } catch (FormatException) { }
        var converted = ResourceMapConversion.Prepare(original, 101, 15, 17, plain, "");
        Check(converted.ID == 101 && original.ID == 100 && !ReferenceEquals(converted.Cells, original.Cells), "converter mutates the imported map");
        Check(converted.Capabilities == 98 && converted.DateMap == "test" && converted.X == -3 && converted.Musique == 12 && converted.Cells[478].FightCell == 2 && converted.Cells[20].TriggerName == "sortie", "AME metadata lost during conversion");
        string project = Path.Combine(TestPaths.Work, "converted-project.ame"); MapProjectSerializer.Save(project, converted);
        var reopened = MapProjectSerializer.Load(project); Check(reopened.Cells[478].FightCell == 2 && reopened.Cells[20].Trigger && reopened.Capabilities == 98, "converted AME loses local cell properties");
        try { ResourceMapConversion.Prepare(original, 2, 15, 17, "bad", ""); throw new Exception("invalid data accepted"); } catch (FormatException) { }
        Check(original.ID == 100 && original.Cells[478].FightCell == 2 && original.HasProjectCells, "failed conversion damages the imported project");
        try { ResourceMapConversion.Prepare(original, 2, 17, 15, new string('a', Map.CellCount(17, 15) * 10), ""); throw new Exception("imported placements reshaped silently"); } catch (FormatException) { }
        Check(ResourceMapConversion.Prepare(null, 2, 15, 17, cipher.ToString(), hexKey).Cells.Length == 479, "encrypted export preparation");
        InitializeForm.NoDB = true;
        using (var form = new RessourceParser())
        {
            form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); Application.DoEvents();
            var tabs = (TabControl)Get(Get(form, "layout"), "Tabs"); tabs.SelectedTab = tabs.TabPages.Cast<TabPage>().First(p => p.Text == "Capture réseau"); Application.DoEvents();
            Check(!((PacketCaptureProxy)Get(form, "captureProxy")).IsRunning, "opening the manager starts a listener");
            // Synthetic immutable records; no authentication data is imported.
            var packets = (List<CapturedPacket>)Get(form, "capturedPackets");
            var packetConstructor = typeof(CapturedPacket).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(int), typeof(bool), typeof(string), typeof(bool) }, null);
            packets.Add((CapturedPacket)packetConstructor.Invoke(new object[] { 1, false, "GM|carte française", false }));
            packets.Add((CapturedPacket)packetConstructor.Invoke(new object[] { 1, true, "[authentification masquée]", true }));
            Call(form, "RefreshCaptureRows"); var grid = (DataGridView)Get(form, "captureGrid"); Check(grid.Rows.Count == 2, "capture records not displayed");
            Check(((TextBox)Get(form, "captureDetails")).Text == "[authentification masquée]", "selected packet and detail disagree");
            ((ComboBox)Get(form, "captureDirection")).SelectedIndex = 2; Check(grid.Rows.Count == 1 && grid.Rows[0].Cells[3].Value.ToString().Contains("française"), "direction filter");
            ((TextBox)Get(form, "captureFilter")).Text = "FRANÇAISE"; Check(grid.Rows.Count == 1, "case-insensitive capture search");
            ((TextBox)Get(form, "captureFilter")).Text = "absent"; Check(grid.Rows.Count == 0 && ((TextBox)Get(form, "captureDetails")).Text == "", "stale packet details after filtering");
            ((TextBox)Get(form, "captureFilter")).Clear(); ((ComboBox)Get(form, "captureDirection")).SelectedIndex = 0;
            using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.Combine(TestPaths.Work, "gestionnaire-capture-reseau.png")); }
            form.Size = form.MinimumSize; form.PerformLayout(); Application.DoEvents();
            var detail = (TextBox)Get(form, "captureDetails");
            using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.Combine(TestPaths.Work, "gestionnaire-capture-compact.png")); }
            Check(grid.Width > 300 && grid.Height > 90 && detail.Height > 50 && grid.RectangleToScreen(grid.ClientRectangle).Bottom <= tabs.SelectedTab.RectangleToScreen(tabs.SelectedTab.ClientRectangle).Bottom, "compact capture views inaccessible: grid=" + grid.Size + ", detail=" + detail.Size);
            var server = new TcpListener(IPAddress.Loopback, 0); server.Start();
            try
            {
                ((NumericUpDown)Get(form, "captureServerPort")).Value = ((IPEndPoint)server.LocalEndpoint).Port;
                // Port zero is reserved for tests, avoiding a race to claim a free port.
                var port = (NumericUpDown)Get(form, "captureListenPort"); port.Minimum = 0; port.Value = 0;
                Call(form, "StartNetworkCapture"); var proxy = (PacketCaptureProxy)Get(form, "captureProxy");
                int listeningPort = proxy.ListeningPort;
                Check(proxy.IsRunning && !port.Enabled && ((Control)Get(form, "captureStop")).Enabled, "capture start does not lock settings or enable stop");
                form.Close(); var watch = Stopwatch.StartNew();
                while (!form.IsDisposed && watch.ElapsedMilliseconds < 5000) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                Check(form.IsDisposed && !proxy.IsRunning, "closing the manager strands the active capture relay");
                var released = new TcpListener(IPAddress.Loopback, listeningPort); released.Start(); released.Stop();
            }
            finally { server.Stop(); }
        }
        Console.WriteLine("OK: resource map conversion preserves 479 cells and AME metadata; capture UI filters and compact view");
    }
}
