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
using Outil_Azur_complet.Bot;
using Outil_Azur_complet.Bot.Controls;
using Outil_Azur_complet.Bot.Controls.Banner;
using Outil_Azur_complet.Bot.Panels;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Maps;

// Carte du monde (lot F11) avec des tuiles, des contours, des icônes et des textes synthétiques écrits dans un dossier
// temporaire : composition des tuiles 2 × 2 au zoom 100 (pixel témoin par quadrant), indice dessiné puis filtré, pastille
// pour une icône absente, repère rouge de la carte actuelle, fond uni sans tuiles, survol → « Zone (Sous-zone) », sous-zone
// « // » et contour teinté, drapeaux de la boussole et du groupe, géométrie MapAt / CellBounds, bornes du zoom, modes de clic ;
// puis le volet dans la fenêtre de jeu contre un serveur fictif local : bouton du bandeau, CWJ / CWV, IC, IH, repère et
// super-zone sur GDM, mode Repère → boussole du bandeau sans paquet, prismes posés par SetPrisms. Aucune capture de fenêtre.
internal static class BotWorldMapSmoke
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static readonly Color TileNW = Color.FromArgb(255, 200, 30, 30), TileNE = Color.FromArgb(255, 30, 160, 30),
        TileSW = Color.FromArgb(255, 30, 30, 200), TileSE = Color.FromArgb(255, 220, 200, 40), HintColor = Color.FromArgb(255, 255, 0, 200);

    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            string path = Path.Combine(TestPaths.ApplicationBin, new AssemblyName(e.Name).Name + ".dll");
            if (!File.Exists(path)) path = Path.ChangeExtension(path, "exe");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        var uiErrors = new List<Exception>();
        Application.ThreadException += (s, e) => uiErrors.Add(e.Exception);
        try
        {
            Application.EnableVisualStyles(); Run();
            Check(uiErrors.Count == 0, "Interface errors: " + string.Join(" / ", uiErrors.Select(e => e.GetType().Name + " " + e.Message)));
            Console.WriteLine("OK: tuiles 2 × 2, indices et filtres, repère, fond uni, survol des zones, drapeaux, géométrie et zoom, volet (CWJ/CWV, IC, IH, GDM, Repère, prismes)");
        }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void PumpUntil(Func<bool> done, string what, int seconds = 8)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(seconds);
        while (!done()) { if (DateTime.UtcNow > end) throw new TimeoutException("World map loopback timed out: " + what); Application.DoEvents(); Thread.Sleep(5); }
        Application.DoEvents();
    }
    private static void Complete(Task task) { PumpUntil(() => task.IsCompleted, "task"); task.GetAwaiter().GetResult(); }
    private static void Feed(Accounts account, string packet) { Complete(MessagesReception.ReceptionAsync(account.Connexion, packet)); }
    private static void FeedFromNetwork(Accounts account, string packet) { Complete(Task.Run(() => MessagesReception.ReceptionAsync(account.Connexion, packet))); }
    private static string Read(Socket socket)
    {
        PumpUntil(() => socket.Available > 0, "packet");
        using (var stream = new MemoryStream())
        {
            byte[] one = new byte[1];
            while (true) { if (socket.Receive(one) != 1) throw new IOException("Loopback disconnected"); if (one[0] == 0) return Encoding.UTF8.GetString(stream.ToArray()).TrimEnd('\r', '\n'); stream.WriteByte(one[0]); }
        }
    }
    private static void NoPacket(Socket socket, string message)
    {
        DateTime end = DateTime.UtcNow.AddMilliseconds(250);
        while (DateTime.UtcNow < end) { Application.DoEvents(); Thread.Sleep(5); }
        Check(socket.Available == 0, message);
    }
    private static void Png(string folder, string name, Color color, int width, int height)
    {
        Directory.CreateDirectory(folder);
        using (var image = new Bitmap(width, height)) { using (var graphics = Graphics.FromImage(image)) graphics.Clear(color); image.Save(Path.Combine(folder, name + ".png"), ImageFormat.Png); }
    }
    private static bool Near(Color a, Color b, int tolerance = 12) => Math.Abs(a.R - b.R) <= tolerance && Math.Abs(a.G - b.G) <= tolerance && Math.Abs(a.B - b.B) <= tolerance;
    private static string Describe(Color c) => "(" + c.R + "," + c.G + "," + c.B + ")";
    private static bool Same(Color a, Color b) => a.ToArgb() == b.ToArgb();

    // ClientAssets est interne à l'application : sa racine (dossier temporaire du test) passe par la réflexion.
    private static Type Assets => typeof(LoginForm).Assembly.GetType("Outil_Azur_complet.Bot.ClientAssets");
    private static void AssetsRoot(string value) => Assets.GetProperty("Root", Any).SetValue(null, value, null);

    private static readonly Size ViewSize = new Size(742, 433);

    private static Bitmap Render(WorldMapView view)
    {
        var image = new Bitmap(ViewSize.Width, ViewSize.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(image)) view.Render(graphics, ViewSize);
        return image;
    }

    /// <summary>Dessine jusqu'à ce que les tuiles demandées par le dessin soient décodées (le dessin ne lit jamais le disque).</summary>
    private static void RenderUntilLoaded(WorldMapView view, int tiles)
    {
        PumpUntil(() => { using (Render(view)) { } return view.LoadedTileCount >= tiles; }, tiles + " tuiles décodées (" + view.LoadedTileCount + ")");
    }

    private static Color PixelAt(WorldMapView view, int x, int y) { using (Bitmap image = Render(view)) return image.GetPixel(x, y); }

    /// <summary>Vrai si un pixel de la couleur exacte existe dans le carré de côté 2 × radius + 1 autour du point.</summary>
    private static bool Around(WorldMapView view, int x, int y, Color color, int radius = 2)
    {
        using (Bitmap image = Render(view))
            for (int dx = -radius; dx <= radius; dx++)
                for (int dy = -radius; dy <= radius; dy++)
                    if (Same(image.GetPixel(x + dx, y + dy), color)) return true;
        return false;
    }

    private static Point Centre(WorldMapView view, int x, int y)
    {
        RectangleF cell = view.CellBounds(x, y);
        return new Point((int)Math.Floor(cell.X + cell.Width / 2), (int)Math.Floor(cell.Y + cell.Height / 2));
    }

    private const string Maps = "<?xml version='1.0' encoding='utf-8'?>\n<BotLang famille=\"maps\" langue=\"fr\" version=\"1\" source=\"maps_fr_1.swf\">\n"
        + "<carte id=\"900093\" x=\"10\" y=\"-3\" sousZone=\"5000\" />\n<carte id=\"900094\" x=\"0\" y=\"0\" sousZone=\"5000\" />\n"
        + "<carte id=\"900095\" x=\"1\" y=\"0\" sousZone=\"5001\" />\n<carte id=\"900096\" x=\"5\" y=\"5\" sousZone=\"5002\" />\n"
        + "<sousZone id=\"5000\" nom=\"Plaine fictive\" zone=\"600\" />\n<sousZone id=\"5001\" nom=\"//Cachée\" zone=\"600\" />\n<sousZone id=\"5002\" nom=\"Île fictive\" zone=\"601\" />\n"
        + "<zone id=\"600\" nom=\"Zone fictive\" superZone=\"0\" />\n<zone id=\"601\" nom=\"Autre monde\" superZone=\"3\" />\n</BotLang>\n";
    private const string Hints = "<?xml version='1.0' encoding='utf-8'?>\n<BotLang famille=\"hints\" langue=\"fr\" version=\"1\" source=\"hints_fr_1.swf\">\n"
        + "<entree table=\"HI\" id=\"0\" c=\"1\" g=\"10\" m=\"900094\" n=\"Temple fictif\" />\n<entree table=\"HI\" id=\"1\" c=\"2\" g=\"11\" m=\"900093\" n=\"Hôtel fictif\" />\n"
        + "<entree table=\"HI\" id=\"2\" c=\"1\" g=\"10\" m=\"999999\" n=\"Carte inconnue\" />\n"
        + "<entree table=\"HIC\" id=\"1\" c=\"Orange\" n=\"Lieux fictifs\" />\n<entree table=\"HIC\" id=\"2\" c=\"Blue\" n=\"Hôtels fictifs\" />\n</BotLang>\n";
    private const string Lang = "<?xml version='1.0' encoding='utf-8'?>\n<BotLang famille=\"lang\" langue=\"fr\" version=\"1\" source=\"lang_fr_1.swf\">\n"
        + "<texte cle=\"WORLD_MAP\" valeur=\"Carte du monde (test)\" />\n<texte cle=\"AREA\" valeur=\"Région\" />\n<texte cle=\"OPTION_GRID\" valeur=\"Grille (test)\" />\n</BotLang>\n";

    private static Map SyntheticMap(int id, int x, int y) => new Map { MapID = id, MapWidth = 3, MapHeight = 4, X = x, Y = y, MapData = string.Concat(Enumerable.Repeat("HhGaeaaaaa", 18)) };

    private static void Run()
    {
        string previous = Environment.CurrentDirectory;
        string folder = Path.Combine(TestPaths.Work, "bot-worldmap"); Directory.CreateDirectory(folder); Environment.CurrentDirectory = folder;
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            LangData.Clear(); MessagesReception.Init();

            // Tuiles synthétiques 2 × 2 de la super-zone 0 (600 × 345, une couleur par quadrant), contour de la sous-zone 5000,
            // icône d'indice 10 ; la super-zone 3 n'a pas de table de tuiles (fond uni).
            string images = Path.Combine(folder, "images");
            string zone = Path.Combine(images, "WorldMap", "0");
            Png(zone, "-1_-1", TileNW, 600, 345); Png(zone, "0_-1", TileNE, 600, 345); Png(zone, "-1_0", TileSW, 600, 345); Png(zone, "0_0", TileSE, 600, 345);
            File.WriteAllText(Path.Combine(zone, "tuiles.tsv"), "nom\tx\ty\tlargeur\thauteur\n-1_-1\t0\t0\t600\t345\n0_-1\t0\t0\t600\t345\n-1_0\t0\t0\t600\t345\n0_0\t0\t0\t600\t345\nmal formée\n", new UTF8Encoding(false));
            Png(Path.Combine(zone, "sous-zones"), "5000", Color.Black, 80, 46);
            File.WriteAllText(Path.Combine(zone, "sous-zones", "sous-zones.tsv"), "nom\tx\ty\tlargeur\thauteur\n5000\t0\t0\t80\t46\n", new UTF8Encoding(false));
            Png(Path.Combine(images, "WorldMap", "hints"), "10", HintColor, 20, 20);
            AssetsRoot(images);

            string lang = Path.Combine(folder, "BotLang"); Directory.CreateDirectory(lang);
            File.WriteAllText(Path.Combine(lang, "maps.xml"), Maps, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(lang, "hints.xml"), Hints, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(lang, "lang.xml"), Lang, new UTF8Encoding(false));
            Check(LangData.Load(lang) == 3, "Synthetic texts were not loaded: " + string.Join(" / ", LangData.LoadWarnings));

            // Index des zones comme AreasManager : super-zone par carte, zone et sous-zone par coordonnées.
            WorldMapAreas areas = null;
            Complete(WorldMapAreas.GetAsync().ContinueWith(t => areas = t.Result));
            Check(areas.Count == 4 && areas.SuperAreaOf(900096) == 3 && areas.SuperAreaOf(900093) == 0 && areas.SuperAreaOf(1) == null, "Area index differs");
            Check(areas.TryGet(0, 10, -3, out int areaId, out int subAreaId) && areaId == 600 && subAreaId == 5000 && !areas.TryGet(3, 10, -3, out _, out _), "Area lookup by coordinates differs");
            Check(areas.Hints.Count == 2 && areas.Hints[0].Name == "Temple fictif" && areas.Hints[0].X == 0 && areas.Hints[0].SuperArea == 0 && areas.Categories.Count == 2
                && areas.Categories[1].Color == "Blue", "Hints were not read from HI / HIC (unknown map skipped)");
            Check(ReferenceEquals(WorldMapAreas.Current, areas), "Built index is not reused");

            using (var shell = new Form { ShowInTaskbar = false, Opacity = 0, Size = new Size(780, 480), StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000) })
            using (var view = new WorldMapView { Size = ViewSize, Location = new Point(0, 0) })
            {
                shell.Controls.Add(view); shell.Show(); Application.DoEvents();
                int loads = 0; view.DataLoaded += (s, e) => loads++;
                // Les PNG sont demandés dès le premier dessin (fenêtre affichée) : l'observateur des lectures est posé avant.
                int uiThread = Thread.CurrentThread.ManagedThreadId; var readers = new List<int>();
                Action<string, int> onRead = (key, thread) => { lock (readers) readers.Add(thread); };
                Assets.GetEvent("AssetFileRead", Any).GetAddMethod(true).Invoke(null, new object[] { onRead });
                try
                {
                    view.SetSuperArea(0);
                    PumpUntil(() => view.TilesReady, "tile table");
                    Check(view.TileCount == 4 && view.SuperArea == 0, "tuiles.tsv was not read (malformed line ignored): " + view.TileCount);
                    view.Zoom = 100; view.SetMapPosition(0, 0);
                    Check(view.Zoom == 100 && view.Current == new Point(0, 0) && view.CellWidth == 40 && view.CellHeight == 23, "Zoom or position differ");
                    RenderUntilLoaded(view, 4);
                }
                finally { Assets.GetEvent("AssetFileRead", Any).GetRemoveMethod(true).Invoke(null, new object[] { onRead }); }
                lock (readers) Check(readers.Count >= 4 && readers.All(t => t != uiThread), "Tile PNG were decoded on the calling thread: " + readers.Count);
                Check(loads >= 1, "DataLoaded was not raised after the table");

                // Au zoom 100 centré sur (0,0) : la carte (0,0) est en haut à gauche de la tuile 0_0, au centre de la vue ;
                // les cartes (-1,0), (0,-1) et (-1,-1) viennent des trois autres tuiles.
                RectangleF cell = view.CellBounds(0, 0);
                Check(Math.Abs(cell.X - 351) < 0.01 && Math.Abs(cell.Y - 205) < 0.01 && cell.Width == 40 && cell.Height == 23, "Cell (0,0) bounds differ: " + cell);
                Check(Same(PixelAt(view, 355, 208), TileSE), "Tile 0_0 is not drawn at map (0,0): " + Describe(PixelAt(view, 355, 208)));
                Check(Same(PixelAt(view, 346, 208), TileSW), "Tile -1_0 is not drawn at map (-1,0): " + Describe(PixelAt(view, 346, 208)));
                Check(Same(PixelAt(view, 355, 200), TileNE), "Tile 0_-1 is not drawn at map (0,-1): " + Describe(PixelAt(view, 355, 200)));
                Check(Same(PixelAt(view, 346, 200), TileNW), "Tile -1_-1 is not drawn at map (-1,-1): " + Describe(PixelAt(view, 346, 200)));
                Check(view.MapAt(new Point(355, 208)) == new Point(0, 0) && view.MapAt(new Point(346, 200)) == new Point(-1, -1) && view.MapAt(new Point(391, 208)) == new Point(1, 0),
                    "MapAt does not invert CellBounds");
                foreach (int zoom in new[] { 10, 35, 50, 100 })
                {
                    view.Zoom = zoom;
                    foreach (Point p in new[] { new Point(0, 0), new Point(-7, 3), new Point(12, -9) })
                        Check(view.MapAt(Centre(view, p.X, p.Y)) == p, "MapAt(CellBounds centre) differs at zoom " + zoom + " for " + p);
                }
                view.Zoom = 100;

                // Indice « Temple fictif » (icône 10, 20 × 20 à l'échelle 2 → 10 × 10) au centre de la carte (0,0), puis filtré.
                Point centre = Centre(view, 0, 0);
                PumpUntil(() => { using (Render(view)) { } return Same(PixelAt(view, centre.X, centre.Y), HintColor); }, "hint icon");
                Check(view.VisibleHints.Count == 2 && view.HintCategories.Count == 2, "Visible hints differ: " + view.VisibleHints.Count);
                view.SetHintCategoryVisible(1, false);
                Check(!view.IsHintCategoryVisible(1) && view.VisibleHints.Count == 1 && Same(PixelAt(view, centre.X, centre.Y), TileSE), "Hidden category is still drawn");
                view.SetHintCategoryVisible(1, true);
                Check(Same(PixelAt(view, centre.X, centre.Y), HintColor), "Category was not shown again");
                // Icône 11 absente : pastille de la couleur de la catégorie 2 (Blue) sur la carte (10,-3).
                view.SetMapPosition(10, -3);
                Point hotel = Centre(view, 10, -3);
                PumpUntil(() => { using (Render(view)) { } return Same(PixelAt(view, hotel.X, hotel.Y), WorldMapView.CategoryColor(2)); }, "missing icon fallback");

                // Repère rouge de la carte actuelle (rectangle MAP_CURRENT_POSITION) sur son bord gauche ; aucune lecture de disque.
                view.SetCurrentMap(new Point(10, -3));
                RectangleF current = view.CellBounds(10, -3);
                Check(Around(view, (int)current.X + 2, (int)(current.Y + current.Height / 2), Color.FromArgb(255, 255, 0, 0)), "Current map rectangle is not drawn in red");
                view.SetWaypoint(new Point(11, -3));
                RectangleF way = view.CellBounds(11, -3);
                Check(Around(view, (int)way.X + 2, (int)(way.Y + way.Height / 2), Color.FromArgb(255, 0, 0, 255)), "Waypoint rectangle is not drawn in blue");
                view.SetWaypoint(null);

                // Survol : « Zone (Sous-zone) », zone seule pour une sous-zone « // », rien hors des cartes connues, contour teinté.
                view.SetMapPosition(0, 0);
                int hovers = 0; view.HoverChanged += (s, e) => hovers++;
                Check(view.HoverAt(Centre(view, 0, 0)) == "Zone fictive (Plaine fictive)" && view.Hovered == new Point(0, 0) && hovers == 1, "Hover text differs: " + view.HoveredText);
                Color plain = TileSE;
                PumpUntil(() => { using (Render(view)) { } return !Same(PixelAt(view, 355, 208), plain); }, "sub-area overlay");
                Color tinted = PixelAt(view, 355, 208);
                Check(tinted.R >= plain.R && tinted.B > plain.B && !Same(tinted, plain), "Sub-area contour is not blended over the tile: " + Describe(tinted));
                Check(view.HoverAt(Centre(view, 1, 0)) == "Zone fictive" && hovers == 2, "Hidden sub-area (//) still named: " + view.HoveredText);
                Check(view.HoverAt(Centre(view, 1, 0)) == "Zone fictive" && hovers == 2, "Same map raised HoverChanged again");
                Check(view.HoverAt(Centre(view, 3, 3)) == string.Empty && view.Hovered == new Point(3, 3), "Unknown map has a zone text: " + view.HoveredText);
                view.ClearHover();
                Check(view.Hovered == null && view.HoveredText == string.Empty && Same(PixelAt(view, 355, 208), plain), "ClearHover left the overlay or the text");

                // Drapeaux : boussole (mât sombre) et membre du groupe (fanion FLAG_MAP_GROUP).
                view.SetCompassTarget(new Point(2, 1));
                Point flag = Centre(view, 2, 1);
                Check(Around(view, flag.X, flag.Y - 8, Color.FromArgb(255, 50, 48, 35), 3), "Compass flag pole is not drawn on its map");
                view.SetHighlights(new[] { new WorldMapFlag(-2, -1, WorldMapView.FlagGroupColor, "-2,-1 (Ami)") });
                Point group = Centre(view, -2, -1);
                Check(Around(view, group.X + 4, group.Y - 20, Color.FromArgb(255, 0, 102, 153), 4), "Group flag pennant is not tinted FLAG_MAP_GROUP");
                Check(view.Highlights.Count == 1 && view.Highlights[0].Label == "-2,-1 (Ami)", "Highlights differ");
                view.SetHighlights(null); view.SetCompassTarget(null);
                Check(view.Highlights.Count == 0 && view.CompassTarget == null, "Flags were not cleared");

                // Zoom borné, grille, modes de clic : repère, zoom ±, flèche nord en mode déplacement.
                int zooms = 0; view.ZoomChanged += (s, e) => zooms++;
                view.Zoom = 200; Check(view.Zoom == 100 && zooms == 0, "Zoom above 100 is not clamped");
                view.Zoom = 3; Check(view.Zoom == 10 && zooms == 1, "Zoom below 10 is not clamped");
                view.Zoom = 50;
                view.ShowGrid = true; Check(view.ShowGrid, "Grid toggle"); view.ShowGrid = false;
                Point selected = Point.Empty; view.CoordinateSelected += (s, e) => selected = e.Coordinates;
                view.Mode = WorldMapMode.Select;
                Check(view.ClickAt(Centre(view, 3, -2)) && selected == new Point(3, -2), "Select mode did not raise the coordinates");
                view.Mode = WorldMapMode.ZoomIn; view.ClickAt(Centre(view, 0, 0)); Check(view.Zoom == 55, "Zoom+ click");
                view.Mode = WorldMapMode.ZoomOut; view.ClickAt(Centre(view, 0, 0)); Check(view.Zoom == 50, "Zoom- click");
                view.Mode = WorldMapMode.Move;
                Check(!view.ClickAt(Centre(view, 0, 0)) && view.Current == new Point(0, 0), "Move mode click on the map moved it");
                Check(view.ClickAt(new Point(ViewSize.Width / 2, 10)) && view.Current == new Point(0, -1), "North arrow did not move the map by one");
                view.MoveMap(2, 1); Check(view.Current == new Point(2, 0), "MoveMap differs");

                // Sans tuiles (super-zone 3) : fond uni et repère toujours dessiné.
                view.SetSuperArea(3); view.Zoom = 100; view.SetMapPosition(5, 5); view.SetCurrentMap(new Point(5, 5));
                PumpUntil(() => view.TilesReady, "empty tile table");
                Check(view.TileCount == 0 && view.LoadedTileCount == 0 && view.VisibleHints.Count == 0, "Super-area 3 should have no tiles nor hints");
                Check(Same(PixelAt(view, 355, 208), view.BackColor), "Background without tiles is not the parchment: " + Describe(PixelAt(view, 355, 208)));
                RectangleF island = view.CellBounds(5, 5);
                Check(Around(view, (int)island.X + 2, (int)(island.Y + island.Height / 2), Color.FromArgb(255, 255, 0, 0)), "Current map rectangle is missing without tiles");
                Check(view.HoverAt(Centre(view, 5, 5)) == "Autre monde (Île fictive)", "Hover in super-area 3 differs: " + view.HoveredText);
                view.SetPrisms(new[] { new WorldMapHint(5, 420, 5, 5, "Prisme fictif", 3), new WorldMapHint(5, 421, 1, 1, "Ailleurs", 0) });
                Check(view.VisibleHints.Count == 1 && view.VisibleHints[0].Tooltip == "5,5 (Prisme fictif)", "Prisms are not filtered by super-area");
                shell.Close();
            }

            // Volet dans la fenêtre de jeu : bouton du bandeau, CWJ / CWV, IC, IH, GDM, mode Repère, prismes.
            Map.AllBotMaps[900093] = SyntheticMap(900093, 10, -3);
            Map.AllBotMaps[900096] = SyntheticMap(900096, 5, 5);
            using (var account = new Accounts(new AccountConfig("synthetic-worldmap", "synthetic", "loopback")))
            {
                Task<Socket> accept = listener.AcceptSocketAsync();
                Complete(account.Connexion.ConnectToServer(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port)); Complete(accept);
                using (Socket peer = accept.Result)
                {
                    peer.ReceiveTimeout = 6000;
                    account.Game.character.SetPerso_Data(42, "Personnage de test", 25, 0, 8);
                    account.Game.Map.SetRefreshMap("900093|date|"); account.Game.character.Cell = account.Game.Map.MapCells[0];
                    account.AccountStates = AccountStates.CONNECTED_INACTIVE;
                    using (var form = new GameClientFullform(account))
                    {
                        form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-4000, -4000);
                        form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); Application.DoEvents();
                        PanelHost host = form.Panels; BannerPanel banner = form.Banner;
                        var panel = host.Find("WorldMap") as WorldMapPanel;
                        Check(panel != null && host.Get<WorldMapPanel>() == panel && panel.Title == "Carte du monde (test)", "WorldMap panel is not registered under the banner name");
                        BannerButton button = banner["WorldMap"];
                        Check(button != null && button.Available, "World map banner button is greyed although the panel exists");
                        NoPacket(peer, "Registering the panel sent a packet");

                        // Ouverture par le bouton : CWJ, repère sur la carte du personnage, vue centrée, super-zone 0.
                        button.PerformClick(); Application.DoEvents();
                        Check(host.Current == panel && Read(peer) == "CWJ" && panel.IsConquestJoined, "Opening the panel did not send CWJ");
                        WorldMapView view = panel.MapView;
                        Check(view.CurrentMap == new Point(10, -3) && view.Current == new Point(10, -3) && view.SuperArea == 0, "Panel did not place the current map marker");
                        PumpUntil(() => view.TilesReady, "panel tiles");
                        Check(view.TileCount == 4, "Panel view did not load the super-area 0 tiles");
                        var filters = panel.View.Controls.Find("worldmap-filters", true).FirstOrDefault() as FlowLayoutPanel;
                        Check(filters != null && filters.Controls.Count == 3 && filters.Controls[0].Text == "Grille (test)" && filters.Controls[2].Text == "Hôtels fictifs", "Hint filters differ");
                        ((CheckBox)filters.Controls[2]).Checked = false;
                        Check(!view.IsHintCategoryVisible(2), "Filter box does not drive the view");
                        ((CheckBox)filters.Controls[0]).Checked = true; Check(view.ShowGrid, "Grid box does not drive the view");

                        // IC depuis le fil réseau : drapeau de la boussole ; IC| l'efface.
                        FeedFromNetwork(account, "IC12|-3");
                        PumpUntil(() => view.CompassTarget == new Point(12, -3), "IC flag");
                        FeedFromNetwork(account, "IC|");
                        PumpUntil(() => view.CompassTarget == null, "IC| clears the flag");

                        // IH : membre du groupe, phénix, puis effacement ; une entrée illisible est ignorée.
                        FeedFromNetwork(account, "IH1;2;1000;2;77;Ami fictif|3;4;1000;1|x;y;1;1");
                        PumpUntil(() => view.Highlights.Count == 2, "IH flags");
                        Check(view.Highlights[0].Rgb == WorldMapView.FlagGroupColor && view.Highlights[0].Label == "1,2 (Ami fictif)" && view.Highlights[1].Rgb == WorldMapView.FlagPhoenixColor
                            && view.Highlights[1].Label == "3,4", "IH flags differ: " + string.Join(" / ", view.Highlights.Select(f => f.Label)));
                        FeedFromNetwork(account, "IH");
                        PumpUntil(() => view.Highlights.Count == 0, "IH clears the flags");

                        // Mode Repère : clic → boussole du bandeau, drapeau, aucun paquet.
                        ((Button)panel.View.Controls.Find("worldmap-select", true).Single()).PerformClick();
                        Check(view.Mode == WorldMapMode.Select && !panel.View.Controls.Find("worldmap-select", true).Single().Enabled, "Select button did not switch the mode");
                        view.ClickAt(Centre(view, 12, -4));
                        Check(view.CompassTarget == new Point(12, -4) && banner.Xtra.CompassTarget == new Point(12, -4), "Select did not aim the banner compass");
                        NoPacket(peer, "Placing a flag sent a packet (IM or BaM)");
                        ((Button)panel.View.Controls.Find("worldmap-move", true).Single()).PerformClick();
                        Check(view.Mode == WorldMapMode.Move, "Move button did not switch the mode");

                        // Survol dans le volet : libellés de zone et de coordonnées.
                        view.HoverAt(Centre(view, 10, -3));
                        Check(panel.AreaText == "Région : Zone fictive (Plaine fictive)" && panel.View.Controls.Find("worldmap-coords", true).Single().Text == "10, -3", "Panel labels differ: " + panel.AreaText);

                        // Changement de carte (GDM → GI) vers une autre super-zone : repère, tuiles de la super-zone 3 (absentes) et recentrage.
                        view.SetMapPosition(0, 0);
                        FeedFromNetwork(account, "GDM|900096|date|key"); Check(Read(peer) == "GI", "GDM did not request GI");
                        PumpUntil(() => view.CurrentMap == new Point(5, 5) && view.SuperArea == 3 && view.Current == new Point(5, 5), "marker after GDM");
                        PumpUntil(() => view.TilesReady, "super-area 3 table");
                        Check(view.TileCount == 0, "Super-area 3 has tiles");
                        panel.SetPrisms(new[] { new WorldMapHint(5, 420, 5, 5, "Prisme fictif", 3) });
                        PumpUntil(() => view.Prisms.Count == 1, "prisms");
                        Check(view.VisibleHints.Count == 1 && view.VisibleHints[0].Gfx == 420, "Prism is not a visible hint");

                        // Fermeture : CWV ; réouverture : CWJ et position conservée (le client garde mapExplorer_coord).
                        view.SetMapPosition(7, 7);
                        host.CloseCurrent(); Application.DoEvents();
                        Check(host.Current == null && Read(peer) == "CWV" && !panel.IsConquestJoined, "Closing the panel did not send CWV");
                        host.Toggle("WorldMap"); Application.DoEvents();
                        Check(host.Current == panel && Read(peer) == "CWJ" && view.Current == new Point(7, 7) && view.CurrentMap == new Point(5, 5), "Reopening lost the position or did not send CWJ");
                        host.CloseCurrent(); Check(Read(peer) == "CWV", "Second close did not send CWV");
                        NoPacket(peer, "The world map sent an unexpected packet");

                        form.Close(); Application.DoEvents();
                        Check(view.IsDisposed, "Panel view was not released with the form");
                    }
                }
            }
        }
        finally { listener.Stop(); AssetsRoot(null); LangData.Clear(); Environment.CurrentDirectory = previous; }
    }
}
