using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Controls;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Managers.Mouvements;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Mouvements;
using Tool_BotProtocol.Game.Monstres;
using Tool_BotProtocol.Game.NPC;
using Tool_BotProtocol.Game.Perso;
using Tool_BotProtocol.Utils.Crypto;

internal static class BotEntitiesSmoke
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
        try { Run(); Console.WriteLine("OK: loopback GM metadata, player/NPC/monster sprites, deterministic sprite motion, depth, zoom continuity, ACK/rejection and missing-art reasons"); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Until(Func<bool> condition, string reason)
    {
        Stopwatch timeout = Stopwatch.StartNew();
        while (!condition())
        {
            if (timeout.ElapsedMilliseconds > 5000) throw new Exception("Timed out: " + reason);
            Application.DoEvents(); System.Threading.Thread.Sleep(5);
        }
        Application.DoEvents();
    }

    private static void Send(Socket socket, string packet)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(packet + "\0");
        int offset = 0; while (offset < bytes.Length) offset += socket.Send(bytes, offset, bytes.Length - offset, SocketFlags.None);
    }

    private static string Read(Socket socket)
    {
        using (var data = new MemoryStream())
        {
            byte[] one = new byte[1];
            while (socket.Receive(one) != 0)
            {
                if (one[0] == 0) return Encoding.UTF8.GetString(data.ToArray());
                data.WriteByte(one[0]);
            }
            throw new Exception("Loopback peer closed before a full packet");
        }
    }

    private static void Sprite(string directory, string name, Color color)
    {
        using (var bitmap = new Bitmap(18, 38))
        {
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                using (var brush = new SolidBrush(color)) graphics.FillRectangle(brush, 3, 3, 12, 32);
                graphics.FillRectangle(Brushes.White, 5, 8, 8, 5);
                graphics.FillRectangle(Brushes.Black, 4, 31, 4, 4);
                graphics.FillRectangle(Brushes.Black, 10, 31, 4, 4);
            }
            bitmap.Save(Path.Combine(directory, name + ".png"), ImageFormat.Png);
        }
    }

    private static Bitmap Render(UserMapControl view, string file)
    {
        view.WaitForActorSprites(5000); // PNG lus hors du fil de l'interface (lot M1)
        var bitmap = new Bitmap(view.Width, view.Height);
        using (Graphics graphics = Graphics.FromImage(bitmap)) view.DrawCells(graphics);
        bitmap.Save(Path.Combine(TestPaths.Work, file + ".png"), ImageFormat.Png);
        return bitmap;
    }

    private static int Pixels(Bitmap image, Color color)
    {
        int count = 0;
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
            {
                Color pixel = image.GetPixel(x, y);
                if (Math.Abs(pixel.R - color.R) < 8 && Math.Abs(pixel.G - color.G) < 8 && Math.Abs(pixel.B - color.B) < 8) count++;
            }
        return count;
    }

    private static void Run()
    {
        string sprites = Path.Combine(TestPaths.Work, "entity-sprites"); Directory.CreateDirectory(sprites);
        Sprite(sprites, "10F", Color.Magenta); Sprite(sprites, "10R", Color.Cyan); Sprite(sprites, "10L", Color.Cyan);
        Sprite(sprites, "20F", Color.Gold); Sprite(sprites, "20R", Color.Gold);
        Sprite(sprites, "1001F", Color.ForestGreen); Sprite(sprites, "1001L", Color.ForestGreen);
        Sprite(sprites, "101F", Color.Crimson); Sprite(sprites, "102F", Color.OrangeRed);
        MessagesReception.Init(); GlobalConfig.BYPASS = false;
        var definition = new Map { MapID = 991001, MapWidth = 3, MapHeight = 4,
            MapData = string.Concat(Enumerable.Repeat("HhGleaaaaa", 18)), Back_ID = 0 };
        Map.AllBotMaps[definition.MapID] = definition;
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            using (var account = new Accounts(new AccountConfig("synthetic-actors", "Synthetic123", "test")))
            {
                account.Game.character.SetPerso_Data(42, "Personnage fictif", 25, 0, 1);
                var accepted = listener.AcceptSocketAsync();
                account.Connexion.ConnectToServer(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port).GetAwaiter().GetResult();
                using (Socket peer = accepted.GetAwaiter().GetResult())
                {
                    peer.ReceiveTimeout = 5000; account.AccountStates = AccountStates.CONNECTED_INACTIVE;
                    Send(peer, "GDM|991001|synthetic|key"); Check(Read(peer) == "GI", "GDM did not request map actors");
                    Send(peer, "GM|+3;2;0;42;Personnage fictif;1;10^100;0|+12;2;0;43;Autre joueur;2;20^100;0"
                        + "|+13;2;0;-7;101,102;-3;101^100,102^110;5,6|+10;3;0;-8;100;-4;1001^125x80;0");
                    Until(() => account.Game.Map.Entites.Count == 3 && account.Game.character.Cell != null, "GM actors");
                    Check(account.Game.character.GFX == 10 && account.Game.character.Orientation == 2, "Self GM graphic metadata was discarded");
                    var npc = (PNJ)account.Game.Map.Entites[-8];
                    var monster = (Monstres)account.Game.Map.Entites[-7];
                    Check(npc.GFX == 1001 && npc.Orientation == 3 && npc.GraphicsScaleX == 125 && npc.GraphicsScaleY == 80,
                        "NPC wire orientation/anisotropic size was replaced by template defaults");
                    Check(monster.MobsInGroupe.Count == 2 && monster.MobsInGroupe[0].GFX == 101 && monster.MobsInGroupe[1].GFX == 102,
                        "Monster group discarded member graphics");
                    double clock = 0;
                    using (var view = new UserMapControl(() => clock, sprites))
                    {
                        view.SetAccount(account); view.W = 3; view.H = 4; view.Size = new Size(720, 470);
                        view.SetCellNum(); view.DrawGrille(); view.RefreshMap();
                        IntPtr handle = view.Handle;
                        Action<Action> ui = action => { if (view.InvokeRequired) view.BeginInvoke(action); else action(); };
                        account.Game.character.MoveMinimapPathfinding += path => ui(() => view.AddAnimations(42, path,
                            PathfinderUtils.GetTimeOnMap(path[0], path), AnimationType.PERSONNAGE));
                        account.Game.Map.EntityMovement += (id, path, duration) => ui(() => view.AddAnimations(id, path, duration, AnimationType.ENTITES));
                        account.Game.Manager.Mouvements.FinalizeMove += good => { if (!good) ui(() => view.CancelAnimation(42)); };
                        using (Bitmap before = Render(view, "bot-acteurs-avant"))
                        {
                            UserMapControl.ActorVisualState[] states = view.GetActorVisualStates();
                            Check(states.Length == 5 && states.All(actor => actor.HasSprite), "A player, NPC or monster group still renders as a dot");
                            Check(Pixels(before, Color.Magenta) > 50 && Pixels(before, Color.ForestGreen) > 50 && Pixels(before, Color.Crimson) > 50,
                                "Actor PNG silhouettes were not rasterized");
                        }
                        PointF source = view.GetActorVisualState(42).WorldPosition;
                        Check(account.Game.Manager.Mouvements.GetCellsMove(account.Game.Map.MapCells[6], new List<Cell>()) == MoveResults.EXIT,
                            "Click movement path failed");
                        Check(Read(peer) == "GA001bag", "Click movement did not produce the StarLoco path");
                        Until(() => view.GetActorVisualState(42).IsMoving, "visual movement start");
                        List<Cell> path = account.Game.Manager.Mouvements.ActualPath.ToList();
                        int duration = PathfinderUtils.GetTimeOnMap(path[0], path);
                        PointF destination = new PointF(source.X + BotMapArtwork.CellWidth / 2, source.Y + BotMapArtwork.CellHeight / 2);
                        clock = duration / 2.0;
                        using (Bitmap middle = Render(view, "bot-acteurs-mouvement"))
                        {
                            var state = view.GetActorVisualState(42);
                            Check(state.HasSprite && state.IsMoving && Math.Abs(state.WorldPosition.X - (source.X + destination.X) / 2) < .02,
                                "Character sprite did not interpolate along the movement path");
                            Check(Pixels(middle, Color.Cyan) > 50, "Movement substituted a colored dot for the sprite PNG");
                            PointF world = state.WorldPosition;
                            view.ZoomAt(1.4, Point.Round(state.ScreenPosition)); view.PanBy(new Point(21, -9));
                            state = view.GetActorVisualState(42);
                            Check(state.IsMoving && state.WorldPosition == world, "Zoom/pan cancelled or reset the character's movement");
                        }
                        clock = duration + 30;
                        Check(view.GetActorVisualState(42).WorldPosition == destination && account.Game.character.Cell.CellID == 3,
                            "Completed animation snapped back before the server ACK");
                        Send(peer, "GA300;1;42;aadbag"); Check(Read(peer) == "GKK300", "Movement was not acknowledged after animation duration");
                        Until(() => account.Game.character.Cell.CellID == 6, "authoritative movement end");
                        using (Bitmap after = Render(view, "bot-acteurs-apres"))
                        {
                            var state = view.GetActorVisualState(42);
                            Check(!state.IsMoving && state.WorldPosition == destination && state.HasSprite,
                                "Final authoritative character state lost the sprite or position");
                        }
                        view.Fit();
                        clock += 100;
                        Check(account.Game.Manager.Mouvements.GetCellsMove(account.Game.Map.MapCells[9], new List<Cell>()) == MoveResults.EXIT,
                            "Second movement path failed");
                        Read(peer); Until(() => view.GetActorVisualState(42).IsMoving, "second visual movement");
                        Send(peer, "GA;0"); Until(() => !account.IsMoving(), "movement rejection");
                        Until(() => !view.GetActorVisualState(42).IsMoving, "rejected visual movement cleanup");
                        Check(account.Game.character.Cell.CellID == 6, "Rejected movement changed the authoritative cell");
                        Send(peer, "GM|~10;4;0;-8;100;-4;1001^125x80;0|+15;2;0;44;Visuel absent;1;999999^100;0");
                        Until(() => account.Game.Map.Entites.ContainsKey(44) && ((PNJ)account.Game.Map.Entites[-8]).Orientation == 4, "GM visual update");
                        view.WaitForActorSprites(5000);
                        var missing = view.GetActorVisualState(44);
                        Check(!missing.HasSprite && missing.SpriteReason.Contains("999999") && view.ArtworkStatus.Contains("999999"),
                            "Missing actor graphic has no explicit diagnostic reason");
                        view.SetSpellTargets(new short[] { 3, 6 }); Check(view.Cursor == Cursors.Cross, "Spell target mode has no aim cursor");
                        view.SetSpellTargets(null); Check(view.Cursor == Cursors.Default, "Spell cancellation kept the aim cursor");
                        Send(peer, "GM|--8"); Until(() => !account.Game.Map.Entites.ContainsKey(-8), "NPC removal");
                        Check(view.GetActorVisualState(-8) == null, "Removed NPC remains visible");
                    }
                }
            }
        }
        finally { listener.Stop(); Map ignored; Map.AllBotMaps.TryRemove(definition.MapID, out ignored); definition.Dispose(); }
    }
}
