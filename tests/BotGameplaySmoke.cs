using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Interactives;
using Tool_BotProtocol.Game.Maps.Mouvements;
using Tool_BotProtocol.Game.Managers.Mouvements;
using Tool_BotProtocol.Game.Monstres;
using Tool_BotProtocol.Utils.Crypto;
using Tool_BotProtocol.Utils.Pics;
using Outil_Azur_complet.Bot.Controls;
using Outil_Azur_complet.Bot.Interfaces;

internal static class BotGameplaySmoke
{
    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            string path = Path.Combine(TestPaths.ApplicationBin, new AssemblyName(args.Name).Name + ".dll");
            if (!File.Exists(path)) path = Path.Combine(TestPaths.ApplicationBin, new AssemblyName(args.Name).Name + ".exe");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        try { Run().GetAwaiter().GetResult(); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.Exit(1); }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task Within(Task task)
    {
        if (await Task.WhenAny(task, Task.Delay(6000)) != task) throw new TimeoutException("Gameplay loopback timed out");
        await task;
    }
    private static async Task<T> Within<T>(Task<T> task) { await Within((Task)task); return await task; }
    private static Task<string> Read(Socket peer)
    {
        return Task.Run(() =>
        {
            using (var buffer = new MemoryStream())
            {
                byte[] one = new byte[1];
                while (true)
                {
                    if (peer.Receive(one) != 1) throw new IOException("Synthetic peer disconnected");
                    if (one[0] == 0) return Encoding.UTF8.GetString(buffer.ToArray()).TrimEnd('\r', '\n');
                    buffer.WriteByte(one[0]);
                }
            }
        });
    }

    private sealed class ArtworkCell : UserMapCell
    {
        public Bitmap DrawnImage;
        public ArtworkCell(short id) : base(id) { }
        public override void DrawZaapi(Bitmap image, Graphics graphics)
        {
            DrawnImage = image;
            base.DrawZaapi(image, graphics);
        }
    }

    private static void CheckArtworkOwnership(Accounts account)
    {
        string previousDirectory = Directory.GetCurrentDirectory();
        string work = Path.Combine(TestPaths.Work, "bot-artwork");
        string graphicsFolder = Path.Combine(work, "ressources", "Bot", "gfx");
        Directory.CreateDirectory(graphicsFolder);
        try
        {
            Directory.SetCurrentDirectory(work);
            string path = Path.Combine(graphicsFolder, "1.png");
            using (var source = new Bitmap(12, 12))
            {
                source.SetPixel(0, 0, Color.Magenta);
                source.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
            using (Bitmap owned = PicturesManager.InteractivePicGfx(1, true))
            {
                Check(owned != null && owned.GetPixel(0, 0).ToArgb() == Color.Magenta.ToArgb(), "Missing reversed image did not use regular artwork");
                using (FileStream exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    Check(exclusive.Length > 0, "Returned bitmap retains its PNG file lock");
                File.Delete(path);
                Check(owned.GetPixel(0, 0).ToArgb() == Color.Magenta.ToArgb(), "Owned bitmap depends on a deleted file stream");
                owned.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
            File.WriteAllText(Path.Combine(graphicsFolder, "1R.png"), "invalid PNG");
            for (int index = 0; index < 40; index++)
            {
                using (Bitmap image = PicturesManager.InteractivePicGfx(1, true))
                    Check(image != null && image.Width == 12, "Invalid reversed optional artwork broke repeated redraws");
            }
            using (var grid = new UserMapControl())
            using (var canvas = new Bitmap(400, 320))
            using (Graphics graphics = Graphics.FromImage(canvas))
            {
                grid.SetAccount(account); grid.W = 3; grid.H = 4; grid.Size = canvas.Size;
                grid.SetCellNum(); grid.DrawGrille();
                var cell = new ArtworkCell(0) { Points = grid.Cells[0].Points, State = CellState.INTERACTIVE };
                grid.DrawUniqueCell(graphics, cell);
                Check(cell.DrawnImage != null, "Artwork ownership probe did not reach the draw caller");
                bool disposed = false;
                try { int ignored = cell.DrawnImage.Width; }
                catch (ArgumentException) { disposed = true; }
                Check(disposed, "Map repaint failed to dispose its owned bitmap");
            }
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            System.Threading.SynchronizationContext.SetSynchronizationContext(null);
        }
    }

    private static async Task Run()
    {
        string directory = Path.Combine(TestPaths.Work, "bot-gameplay-maps");
        Directory.CreateDirectory(directory);
        string data = "HhGaeaacabHhGaeaacab" + string.Concat(Enumerable.Repeat("HhGaeaaaaa", 16));
        File.WriteAllText(Path.Combine(directory, "900001.xml"), "<RECORD><ID>900001</ID><LARGEUR>3</LARGEUR>"
            + "<LONGUEUR>4</LONGUEUR><X>150</X><Y>-151</Y><MAP_DATA>" + data + "</MAP_DATA><BACK>0</BACK></RECORD>");
        File.WriteAllText(Path.Combine(directory, "broken.xml"), "<RECORD>");
        File.WriteAllText(Path.Combine(directory, "notes.txt"), "This is not map XML.");
        await Map.LoadAllMapsAsync(directory);
        Check(Map.AllBotMaps.Count == 1 && Map.LoadWarnings.Length == 1, "Bad XML discarded valid map resources");
        using (var map = new Map())
        {
            map.SetRefreshMap("900001|date|key");
            Check(map.HasMapData && map.X == 150 && map.Y == -151 && map.MapCells.Length == 18,
                "GDM refresh failed without subscribers or truncated coordinates");
            Check(map.MapCells[0].LineofSight, "Map line of sight differs from StarLoco");
            Check(map.Interactives.ContainsKey(0) && map.Interactives.ContainsKey(1), "Identical object graphics lost a cell");
            map.SetRefreshMap("900002|date|key");
            Check(map.MapID == 900002 && !map.HasMapData && !string.IsNullOrEmpty(map.LoadError), "Missing map kept stale geometry");
        }

        MessagesReception.Init();
        GlobalConfig.BYPASS = false;
        var configuration = (Dictionary<string, string>)typeof(GlobalConfig).GetField("ConfigDico",
            BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        configuration["coresize"] = "2528660";
        configuration["loadersize"] = "2362079";
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            using (var account = new Accounts(new AccountConfig("synthetic-game", "Synthetic123", "test")))
            {
                Task<Socket> accepted = listener.AcceptSocketAsync();
                await account.Connexion.ConnectToServer(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
                using (Socket peer = await Within(accepted))
                {
                    peer.ReceiveTimeout = 5000;
                    account.Game.character.id = 42;
                    await MessagesReception.ReceptionAsync(account.Connexion, "GDM|900001|date|key");
                    Check(await Within(Read(peer)) == "GI" && account.Game.Map.HasMapData, "GDM did not load before requesting GI");
                    await MessagesReception.ReceptionAsync(account.Connexion, "GM|+0;1;0;42;Synthetic;1;100^100;0"
                        + "|+3;1;0;43;Other;2;200^100;1|+4;1;30;-7;101,102;-3;1^100,2^100;5,6"
                        + "|+5;1;0;-8;100;-4;1^100;0|+bad;entry");
                    Check(account.Game.character.Cell.CellID == 0 && account.Game.Map.Entites.Count == 3,
                        "GM lost entity zero or mixed valid entries");
                    CheckArtworkOwnership(account);
                    var monsters = (Monstres)account.Game.Map.Entites[-7];
                    Check(monsters.GetAllMonster == 2 && monsters.GroupSize(101) == 1 && monsters.MobsGroupelevel == 11,
                        "Monster leader is counted twice");
                    await MessagesReception.ReceptionAsync(account.Connexion, "GM|~6;1;0;43;Renamed;2;200^100;1|--8");
                    Check(account.Game.Map.Entites[43].Name == "Renamed" && !account.Game.Map.Entites.ContainsKey(-8),
                        "GM entity update/removal was ignored");
                    await MessagesReception.ReceptionAsync(account.Connexion, "GDF|0;2;0|1;1;1|4095;2|bad");
                    Check(!account.Game.Map.Interactives[0].IsUsable && account.Game.Map.Interactives[1].IsUsable,
                        "GDF identifies objects by graphics rather than cell");

                    var pathfinder = new Pathfinder();
                    pathfinder.SetMap(account.Game.Map);
                    var forbidden = new List<Cell> { account.Game.Map.MapCells[4] };
                    List<Cell> first = pathfinder.GetPath(account.Game.Map.MapCells[0], account.Game.Map.MapCells[3], forbidden, false, 0);
                    Check(first.Count == 2 && PathfinderUtils.GetCleanRoad(first) == "bad" && forbidden.Count == 1,
                        "Pathfinder mutated caller cells or encoded the wrong direction");
                    List<Cell> second = pathfinder.GetPath(account.Game.Map.MapCells[3], account.Game.Map.MapCells[0], forbidden, false, 0);
                    Check(second.Count == 2 && Hash.Get_Cell_From_Hash(PathfinderUtils.GetCleanRoad(second).Substring(1)) == 0,
                        "Previous path state contaminated reverse path or excluded cell zero");

                    using (var grid = new UserMapControl())
                    {
                        grid.SetAccount(account);
                        grid.W = 3; grid.H = 4;
                        grid.Size = new Size(520, 340);
                        grid.SetCellNum(); grid.DrawGrille();
                        Check(grid.Cells.Length == 18 && grid.Cells.All(cell => cell.Points != null), "Grid allocated phantom cells");
                        foreach (var cell in grid.Cells)
                            Check(grid.GetCell(cell.Centre).id == cell.id, "Grid click differs from drawn cell " + cell.id);
                        grid.Size = new Size(330, 520);
                        foreach (var cell in grid.Cells)
                            Check(grid.GetCell(cell.Centre).id == cell.id, "Resize moved hit targets " + cell.id);
                    }

                    using (var view = new MapControl(account))
                    {
                        view.Size = new Size(460, 220);
                        view.PerformLayout();
                        var grid = view.Controls.OfType<UserMapControl>().Single();
                        Check(grid.Dock == System.Windows.Forms.DockStyle.Fill && grid.ClientSize == view.ClientSize,
                            "Dashboard clips map instead of fitting its panel");
                        view.Size = new Size(290, 470);
                        view.PerformLayout();
                        Check(grid.ClientSize == view.ClientSize, "Dashboard map ignored parent resize");
                        grid.SetCellNum(); grid.DrawGrille();
                        foreach (var cell in grid.Cells)
                        {
                            Check(view.ClientRectangle.Contains(cell.Centre) && grid.GetCell(cell.Centre).id == cell.id,
                                "Dashboard resize clipped the drawn or clicked cell " + cell.id);
                        }
                        using (var bitmap = new Bitmap(view.Width, view.Height))
                        {
                            view.DrawToBitmap(bitmap, view.ClientRectangle);
                            Check(bitmap.GetPixel(grid.Cells[3].Centre.X, grid.Cells[3].Centre.Y).A == 255,
                                "Map rasterization left empty cell pixels");
                        }
                    }

                    // This console harness has no message loop; a control may install a WinForms context.
                    System.Threading.SynchronizationContext.SetSynchronizationContext(null);
                    account.AccountStates = AccountStates.CONNECTED_INACTIVE;
                    MoveResults move = account.Game.Manager.Mouvements.GetCellsMove(account.Game.Map.MapCells[3], new List<Cell>());
                    Check(move == MoveResults.EXIT && account.IsMoving(), "Movement did not reserve character state");
                    Check(await Within(Read(peer)) == "GA001bad", "Movement is incompatible with StarLoco wire format");
                    await MessagesReception.ReceptionAsync(account.Connexion, "GA300;1;42;aaabad");
                    Check(await Within(Read(peer)) == "GKK300" && account.Game.character.Cell.CellID == 3
                        && account.AccountStates == AccountStates.CONNECTED_INACTIVE, "Action ids above 255 were truncated or movement was not acknowledged");
                    await MessagesReception.ReceptionAsync(account.Connexion, "GA;0");
                    await MessagesReception.ReceptionAsync(account.Connexion, "GA;4;42;42,0");
                    Check(account.Game.character.Cell.CellID == 0, "GA relocation omitted destination cell zero");
                    Check(account.Game.Manager.Mouvements.GetCellsMove(account.Game.Map.MapCells[3], new List<Cell>()) == MoveResults.EXIT,
                        "Interrupted movement setup failed");
                    Check(await Within(Read(peer)) == "GA001bad", "Interrupted movement did not send its path");
                    await MessagesReception.ReceptionAsync(account.Connexion, "GDM|900001|next-date|key");
                    Check(await Within(Read(peer)) == "GI" && account.AccountStates == AccountStates.CONNECTED_INACTIVE
                        && account.Game.Manager.Mouvements.ActualPath == null, "GDM retained pending movement and blocked the new map");
                    await MessagesReception.ReceptionAsync(account.Connexion, "GM|+0;1;0;42;Synthetic;1;100^100;0");
                    Check(account.Game.Manager.Mouvements.GetCellsMove(account.Game.Map.MapCells[3], new List<Cell>()) == MoveResults.EXIT,
                        "New map remained busy after interrupted movement");
                    Check(await Within(Read(peer)) == "GA001bad", "New map movement did not use its fresh path");
                    Task pendingAck = account.Game.Manager.Mouvements.EventMoveFisnish(account.Game.Map.MapCells[3], 301, true);
                    await MessagesReception.ReceptionAsync(account.Connexion, "GDM|900001|third-date|key");
                    await Within(pendingAck);
                    Check(await Within(Read(peer)) == "GI" && peer.Available == 0
                        && account.Game.Manager.Mouvements.ActualPath == null && account.AccountStates == AccountStates.CONNECTED_INACTIVE,
                        "Map change emitted an obsolete movement ACK or kept its timer/path");
                    foreach (AccountStates preserved in new[] { AccountStates.FIGHTING, AccountStates.DIALOG })
                    {
                        account.AccountStates = preserved;
                        await MessagesReception.ReceptionAsync(account.Connexion, "GDM|900001|date|key");
                        Check(await Within(Read(peer)) == "GI" && account.AccountStates == preserved,
                            "Map refresh overwrote active state " + preserved);
                    }
                    account.AccountStates = AccountStates.CONNECTED_INACTIVE;
                    await MessagesReception.ReceptionAsync(account.Connexion, "BC7;core.swf");
                    Check(await Within(Read(peer)) == "BC7;2528660", "Client file size was parsed as a byte");
                    await MessagesReception.ReceptionAsync(account.Connexion, "Ow50000|100000");
                    Check(account.Game.character.Inventory.Percent_Pods == 50, "Weight overflowed or divided by zero");
                    await MessagesReception.ReceptionAsync(account.Connexion, "JX|14;99;100;150;200|15;100;200;200;");
                    Check(account.Game.character.Jobs.Count == 2 && account.Game.character.Jobs[1].GetXpPercentage == 100,
                        "Job XP before skills caused a missing-resource exception");
                    await MessagesReception.ReceptionAsync(account.Connexion, "JS|14;100~1~2~0~1500,");
                    Check(account.Game.character.Jobs[0].Skills.Count == 1, "Job skills failed on trailing empty entry");
                    await MessagesReception.ReceptionAsync(account.Connexion, "PCKSynthetic");
                    await MessagesReception.ReceptionAsync(account.Connexion, "PM+10;Alice;1|11;Bob;2");
                    await MessagesReception.ReceptionAsync(account.Connexion, "PM-10");
                    Check(account.Game.character.InEquip.Count == 1 && account.Game.character.InEquip.ContainsKey("Bob"),
                        "Group member removal retained obsolete names");
                    account.Game.Clear();
                    Check(account.Game.character.id == 0 && account.Game.character.Jobs.Count == 0
                        && account.Game.character.Inventory.Percent_Pods == 0 && !account.Game.Map.HasMapData
                        && account.Game.PersoInWorld.Count == 0, "Reconnect retained previous character/map state");
                }
            }
        }
        finally { listener.Stop(); }
        Console.WriteLine("OK: synthetic StarLoco maps/entities, object states, path encoding, cell-zero clicks, resize geometry, movement ACK above 255, jobs/weight/groups and reconnect cleanup");
    }
}
