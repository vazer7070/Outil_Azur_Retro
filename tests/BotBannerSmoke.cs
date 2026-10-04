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
using Outil_Azur_complet.Bot.Interfaces;
using Outil_Azur_complet.Bot.Panels;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Perso.Spells;

// Bandeau du client (lot C3) sur un serveur fictif local : As synthétique → cœur à 50 %, PA / PM, jauges xp / pods / énergie ;
// nom de zone centré et coordonnées ; IC12|-3 → boussole, IC| → portrait ; BT → horloge ; fC1 → œil des combats (fL à
// l'ouverture) ; bouton « Caractéristiques » sans volet → grisé « à venir », avec un volet factice → ouvert ; BotOptions
// écrit et relu dans un dossier temporaire (grille appliquée à la carte, touche personnalisée) ; raccourcis c, Maj+&,
// Maj+', Maj+d (Rr), & (SH1), Échap ; glisser un sort en case 3 → seul SM<id>|3 part et l'ordre local est gardé ; objets
// de la barre (OU, OM case + 34) ; filtre des mots et heure du chat ; clic droit global, fenêtre d'options, menu principal.
internal static class BotBannerSmoke
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

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
            Console.WriteLine("OK: cœur, PA/PM, jauges, zone, boussole IC, horloge BT, œil fC, boutons « à venir », options JSON, raccourcis, SM<id>|3 local, OU/OM, chat, menus");
        }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static object Get(object target, string field) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static void PumpUntil(Func<bool> done, string what, int seconds = 6)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(seconds);
        while (!done()) { if (DateTime.UtcNow > end) throw new TimeoutException("Banner loopback timed out: " + what); Application.DoEvents(); Thread.Sleep(5); }
        Application.DoEvents();
    }
    private static void Complete(Task task) { PumpUntil(() => task.IsCompleted, "task"); task.GetAwaiter().GetResult(); }
    private static T Result<T>(Task<T> task) { Complete(task); return task.Result; }
    private static void Feed(Accounts account, string packet) { Complete(MessagesReception.ReceptionAsync(account.Connexion, packet)); }
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
    private static bool Near(Color a, Color b) => Math.Abs(a.R - b.R) <= 16 && Math.Abs(a.G - b.G) <= 16 && Math.Abs(a.B - b.B) <= 16;

    // ClientAssets est interne à l'application : sa racine (dossier temporaire du test) passe par la réflexion.
    private static Type Assets => typeof(LoginForm).Assembly.GetType("Outil_Azur_complet.Bot.ClientAssets");
    private static void AssetsRoot(string value) => Assets.GetProperty("Root", Any).SetValue(null, value, null);

    /// <summary>Volet factice nommé comme le volet des caractéristiques : <c>PanelHost.Find("Stats")</c> le retrouve.</summary>
    private sealed class StatsPanel : IGamePanel
    {
        private readonly Panel view = new Panel { Name = "fake-stats" };
        public string Title => "Caractéristiques (test)";
        public Image Icon => null;
        public Control View => view;
        public GameClass Game { get; private set; }
        public void Bind(GameClass game) { Game = game; }
        public event EventHandler Closed { add { } remove { } }
        public void Dispose() => view.Dispose();
    }

    private const string Lang = "<?xml version='1.0' encoding='utf-8'?>\n<BotLang famille=\"lang\" langue=\"fr\" version=\"1\" source=\"lang_fr_1.swf\">\n"
        + "<config cle=\"CENSORSHIP_ENABLE_INPUT\" valeur=\"true\" />\n<entree table=\"CSR\" id=\"1\" c=\"zorglub\" l=\"0\" />\n</BotLang>\n";
    private const string Items = "<?xml version='1.0' encoding='utf-8'?>\n<BotLang famille=\"items\" langue=\"fr\" version=\"1\" source=\"items_fr_1.swf\">\n"
        + "<objet id=\"2001\" nom=\"Potion fictive\" type=\"12\" gfx=\"5\" utilisable=\"true\" />\n"
        + "<objet id=\"2002\" nom=\"Pain fictif\" type=\"33\" gfx=\"6\" utilisable=\"true\" />\n"
        + "<objet id=\"2003\" nom=\"Pierre fictive\" type=\"15\" gfx=\"7\" />\n</BotLang>\n";
    private const string Maps = "<?xml version='1.0' encoding='utf-8'?>\n<BotLang famille=\"maps\" langue=\"fr\" version=\"1\" source=\"maps_fr_1.swf\">\n"
        + "<carte id=\"900093\" x=\"10\" y=\"-3\" sousZone=\"5000\" />\n<sousZone id=\"5000\" nom=\"Plaine fictive\" zone=\"600\" />\n"
        + "<zone id=\"600\" nom=\"Zone fictive\" superZone=\"0\" />\n</BotLang>\n";
    // As synthétique : xp 150 entre 100 et 200, 2 500 kamas, vie 125 / 250, énergie 8 000 / 10 000, PA 6, PM 3.
    private const string Stats = "As150,100,200|2500|5|3|0~0,0,0,0,0,0|125,250|8000,10000|100|120|6,0,0,0|3,0,0,0|0,0,0,0|0,0,0,0|0,0,0,0|0,0,0,0|0,0,0,0|0,0,0,0|0,0,0,0|0,0,0,0";

    private static void Run()
    {
        string previous = Environment.CurrentDirectory;
        string folder = Path.Combine(TestPaths.Work, "bot-banner"); Directory.CreateDirectory(folder); Environment.CurrentDirectory = folder;
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            LangData.Clear(); MessagesReception.Init(); Spell.AllSpells.Clear();
            for (short i = 10; i <= 12; i++) { var spell = new Spell(i, "Sort test " + i); spell.GetSpellsStats(1, new SpellStats { PA = 3, Min_portee = 1, Max_portee = 6 }); }
            Map.AllBotMaps[900093] = new Map { MapID = 900093, MapWidth = 3, MapHeight = 4, MapData = string.Concat(Enumerable.Repeat("HhGaeaaaaa", 18)), X = 10, Y = -3 };

            // Images synthétiques du client (dossier temporaire) : cœur plein rouge, cœur vide gris, icônes de sorts.
            string images = Path.Combine(folder, "images");
            Color red = Color.FromArgb(255, 200, 30, 30), grey = Color.FromArgb(255, 120, 120, 120);
            Png(Path.Combine(images, "UI", "Client"), "Heart", red, 88, 80);
            Png(Path.Combine(images, "UI", "Client"), "Heart_vide", grey, 88, 80);
            for (int i = 10; i <= 12; i++) Png(Path.Combine(images, "sorts"), i.ToString(), Color.FromArgb(255, 40, 80 + i, 160), 40, 40);
            AssetsRoot(images);

            // Options : fichier JSON du dossier temporaire, valeurs du client par défaut.
            string optionsPath = Path.Combine(folder, "config", "BotOptions.json"); Directory.CreateDirectory(Path.GetDirectoryName(optionsPath));
            BotOptions options = BotOptions.Load(optionsPath);
            Check(options.LastError == null && !options.Grid && options.MapInfos && options.BannerShortcuts && options.TimestampInChat && options.CensorshipFilter
                && options.DefaultQuality == "high" && options.BannerGaugeMode == "xp" && options.BannerIllustrationMode == "artwork", "Client default options differ");

            // Table des raccourcis : intégrée sans shortcuts.xml, puis celle du client (jeu 1).
            ShortcutTable builtIn = Shortcuts.Build(options);
            Check(!builtIn.FromClientData && builtIn["CHARAC"].Key.Equals(new ShortcutKey(67, 0)) && builtIn.Find(Keys.C).Any(d => d.Name == "CHARAC"), "Built-in shortcut table is wrong");
            string lang = Path.Combine(folder, "BotLang"); Directory.CreateDirectory(lang);
            File.WriteAllText(Path.Combine(lang, "lang.xml"), Lang, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(lang, "items.xml"), Items, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(lang, "maps.xml"), Maps, new UTF8Encoding(false));
            File.Copy(Path.Combine(TestPaths.ApplicationBin, "ressources", "Bot", "BotLang", "shortcuts.xml"), Path.Combine(lang, "shortcuts.xml"), true);
            Check(LangData.Load(lang) == 4, "Synthetic texts were not loaded: " + string.Join(" / ", LangData.LoadWarnings));
            ShortcutTable table = Shortcuts.Build(options);
            Check(table.FromClientData && table["GRID"].Label == "Maj+&" && table["COORDS"].Label == "Maj+'" && table["MOUNTING"].Label == "Maj+d"
                && table["FULLSCREEN"].Label == "Ctrl+f" && table["NEXTTURN"].Label == "Ctrl+Fin" && table["SH3"].Key.Equals(new ShortcutKey(51, 0)),
                "Client shortcut table (set 1) is wrong");
            Check(table["CHARAC"].OutsideChatOnly && !table["ESCAPE"].OutsideChatOnly && !table["NEXTTURN"].OutsideChatOnly, "Shortcut o flag is wrong");
            ChatCensorship censorship = ChatCensorship.FromLang(new Random(1));
            Check(censorship != null && censorship.Count == 1, "CSR dictionary was not read");
            string censored = censorship.Apply("vilain Zorglub !");
            Check(censored.StartsWith("vilain ", StringComparison.Ordinal) && censored.EndsWith(" !", StringComparison.Ordinal) && !censored.Contains("orglub")
                && censored.Substring(7, 7).All(c => "%&§@?".IndexOf(c) >= 0), "Censorship output differs: " + censored);
            for (int i = 8; i < 14; i++) Check(censored[i] != censored[i - 1], "Censorship repeats a character");

            // Horloge et boussole : formules du client.
            using (var clock = new XtraBox())
            {
                clock.SetTime(new DateTime(2026, 1, 1, 3, 30, 10));
                Check(Math.Abs(clock.HoursAngle - 15) < 1e-9 && Math.Abs(clock.MinutesAngle - 90) < 1e-9, "Clock angles differ from the client");
                clock.SetCurrentCoordinates(new Point(0, 0)); clock.SetCompassTarget(new Point(0, 5));
                Check(clock.DisplayedMode == "compass" && Math.Abs(clock.CompassAngle.Value - 90) < 1e-9, "Compass angle differs");
                clock.SetCompassTarget(new Point(0, 0)); Check(clock.CompassAngle == null, "Compass on the target map still has an arrow");
                clock.SetCompassTarget(null); Check(clock.DisplayedMode == "artwork", "Cleared compass does not return to the artwork");
            }

            // Cœur à 50 % avec les images du client : haut vide (gris), bas plein (rouge).
            using (var heart = new LifeHeart { Size = new Size(88, 80) })
            using (var holder = new Form { ShowInTaskbar = false, Opacity = 0 })
            {
                holder.Controls.Add(heart); holder.Show(); heart.SetLife(125, 250);
                PumpUntil(() => heart.HasClientImages, "heart images");
                Check(heart.Percent == 50, "Heart ratio is not 50 %");
                using (var image = new Bitmap(88, 80))
                {
                    heart.DrawToBitmap(image, new Rectangle(0, 0, 88, 80));
                    Check(Near(image.GetPixel(20, 6), grey) && Near(image.GetPixel(20, 70), red), "Heart is not filled up to half: " + image.GetPixel(20, 6) + " / " + image.GetPixel(20, 70));
                }
                holder.Close();
            }

            using (var account = new Accounts(new AccountConfig("synthetic-banner", "synthetic", "loopback")))
            {
                Task<Socket> accept = listener.AcceptSocketAsync();
                Complete(account.Connexion.ConnectToServer(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port)); Complete(accept);
                using (Socket peer = accept.Result)
                {
                    peer.ReceiveTimeout = 6000;
                    var character = account.Game.character;
                    character.SetPerso_Data(42, "Personnage de test", 25, 0, 8);
                    account.Game.Map.SetRefreshMap("900093|date|"); character.Cell = account.Game.Map.MapCells[0];
                    account.AccountStates = AccountStates.CONNECTED_INACTIVE;
                    for (short i = 10; i <= 12; i++) character.Spells[i] = Spell.ForCharacter(i, 1);
                    Feed(account, Stats); Feed(account, "Ow120|1000");
                    Feed(account, "OAKO3e9~7d1~3~23~;3ea~7d2~4~~;3eb~7d3~1~~;");
                    Check(character.Inventory.GetByInventoryId(1001) != null && (int)character.Inventory.GetByInventoryId(1001).position == 35, "OAKO item at position 35 was not read");

                    // Bouton sans volet : grisé « à venir », rien n'est ouvert ; avec un volet du même nom : ouvert.
                    using (var shell = new Form { ShowInTaskbar = false, Opacity = 0, Size = new Size(900, 300) })
                    {
                        var host = new PanelHost { Dock = DockStyle.Top, Height = 150 };
                        host.Account = account;
                        var alone = new BannerPanel(account, host, options) { Dock = DockStyle.Bottom, Height = 98 };
                        var messages = new List<string>(); alone.Feedback += messages.Add;
                        shell.Controls.Add(host); shell.Controls.Add(alone); shell.Show(); Application.DoEvents();
                        BannerButton stats = alone["Stats"];
                        Check(stats != null && stats.Width == BannerButton.Side && stats.Height == BannerButton.Side && stats.Image != null && stats.AccessibleName == "Caractéristiques",
                            "Characteristics banner button is not a 26 × 26 client button");
                        stats.PerformClick();
                        Check(!stats.Available && host.Current == null && messages.Any(m => m.Contains("à venir")), "Missing panel did not grey the button");
                        alone.RefreshAll(); Check(alone.TooltipOf(stats).Contains("À venir"), "Greyed button has no « à venir » tooltip");
                        Check(alone.Buttons.Count == 9 && !alone.Buttons.Any(b => b.Available), "Buttons without panels are not greyed");
                        var fake = new StatsPanel(); host.Register(fake);
                        Check(host.Find("Stats") == fake && host.Find("Inconnu") == null && !host.Open("Inconnu"), "PanelHost name registry is wrong");
                        alone.RefreshAll(); Check(stats.Available && !alone.TooltipOf(stats).Contains("À venir"), "Delivered panel keeps the button greyed");
                        stats.PerformClick(); Check(host.Current == fake && fake.Game == account.Game, "Fake Stats panel was not opened");
                        stats.PerformClick(); Check(host.Current == null, "Second click does not close the panel");
                        shell.Close();
                    }
                    NoPacket(peer, "The standalone banner sent a packet");

                    using (var form = new GameClientFullform(account, options))
                    {
                        form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); Application.DoEvents();
                        BannerPanel banner = form.Banner;
                        var map = (MapControl)Get(form, "mapControl");
                        Check(map != null && banner != null && ReferenceEquals(banner.Parent, form.Hud.RightSlot) && ReferenceEquals(banner.Center.Parent, form.Hud.CenterSlot),
                            "Banner is not composed in the HUD");
                        Check(form.MainMenuStrip == null && !form.Controls.OfType<MenuStrip>().Any(), "The menu strip is still there");

                        // As : cœur, PA / PM, jauge d'xp.
                        PumpUntil(() => banner.Heart.MaximumLife == 250, "As in the banner");
                        Check(banner.Heart.Life == 125 && banner.Heart.Percent == 50, "Heart is not at 50 %");
                        Check(banner.ActionPoints.Value == 6 && banner.MovementPoints.Value == 3, "PA / PM differ from As");
                        Check(banner.Gauge.Mode == "xp" && Math.Abs(banner.Gauge.Value - 50) < 0.01 && banner.Gauge.GaugeColor == CircleGauge.XpColor, "XP gauge is not at 50 %");
                        options.BannerGaugeMode = "pods";
                        PumpUntil(() => banner.Gauge.Mode == "pods", "pods gauge");
                        Check(Math.Abs(banner.Gauge.Value - 12) < 0.01 && banner.Gauge.GaugeColor == CircleGauge.PodsColor, "Pods gauge differs from Ow");
                        options.BannerGaugeMode = "energy";
                        PumpUntil(() => banner.Gauge.Mode == "energy", "energy gauge");
                        Check(Math.Abs(banner.Gauge.Value - 80) < 0.01 && banner.Gauge.GaugeColor == CircleGauge.EnergyColor, "Energy gauge differs from As");
                        options.BannerGaugeMode = "xp";
                        Check(banner.TooltipOf(banner["Inventory"]).Contains("120 pods sur 1 000"), "Inventory tooltip lacks the pods: " + banner.TooltipOf(banner["Inventory"]));

                        // Zone : texte centré à l'arrivée, coordonnées en haut à gauche.
                        PumpUntil(() => banner.CenterText.Message == "Zone fictive\n(Plaine fictive)", "zone name");
                        Check(!banner.CenterText.HasBackground && ReferenceEquals(banner.CenterText.Parent, Get(form, "mapArea")), "Zone text is not on the map");
                        Check(banner.MapInfos.Visible && banner.MapInfos.AreaText == "Zone fictive (Plaine fictive)" && banner.MapInfos.CoordinatesText == "10,-3", "Map infos differ");

                        // IC : boussole vers la cible, puis portrait ; paquets mal formés ignorés.
                        Feed(account, "IC12|-3");
                        PumpUntil(() => banner.Xtra.CompassTarget == new Point(12, -3), "compass target");
                        Check(banner.Xtra.DisplayedMode == "compass" && banner.Xtra.CurrentCoordinates == new Point(10, -3) && Math.Abs(banner.Xtra.CompassAngle.Value) < 1e-9,
                            "Compass does not point east to [12,-3]");
                        Check(banner.TooltipOf(banner.Xtra).StartsWith("12, -3", StringComparison.Ordinal), "Compass tooltip lacks the target");
                        Feed(account, "IC|"); PumpUntil(() => banner.Xtra.CompassTarget == null, "compass cleared");
                        Check(banner.Xtra.DisplayedMode == "artwork", "Cleared compass does not show the artwork");
                        Feed(account, "ICabc|x"); Feed(account, "IC"); Application.DoEvents();
                        Check(banner.Xtra.CompassTarget == null, "Malformed IC set a target");

                        // BT : horloge du serveur.
                        options.BannerIllustrationMode = "clock";
                        long reference = new DateTimeOffset(2026, 1, 1, 3, 30, 10, TimeSpan.Zero).ToUnixTimeMilliseconds();
                        Feed(account, "BT" + reference);
                        PumpUntil(() => banner.Xtra.Time == new DateTime(2026, 1, 1, 3, 30, 0), "server clock");
                        Check(banner.Xtra.DisplayedMode == "clock" && Math.Abs(banner.Xtra.HoursAngle - 15) < 1e-9 && banner.TooltipOf(banner.Xtra).StartsWith("03:30", StringComparison.Ordinal),
                            "Clock does not show the BT time");
                        options.BannerIllustrationMode = "artwork";
                        Check(banner.Xtra.DisplayedMode == "artwork", "Illustration option is not applied");

                        // fC : œil des combats, liste demandée à l'ouverture ; fC0 et paquet illisible.
                        Check(!banner.FightsShown && !banner.FightsButton.Visible, "Fights eye shown without fights");
                        Feed(account, "fC1");
                        PumpUntil(() => banner.FightsShown && banner.FightsButton.Visible, "fights eye");
                        Check(banner.TooltipOf(banner.FightsButton).Contains("1 combat sur cette carte"), "Eye tooltip differs: " + banner.TooltipOf(banner.FightsButton));
                        banner.FightsButton.PerformClick();
                        Check(form.Panels.Current is FightsListPanel && Read(peer) == "fL", "Eye does not open the fights list (fL)");
                        Check(form.ProcessShortcut(Keys.Escape) && form.Panels.Current == null, "Escape does not close the panel");
                        Feed(account, "fC0"); PumpUntil(() => !banner.FightsShown, "eye hidden");
                        Feed(account, "fCabc"); Application.DoEvents(); Check(!banner.FightsShown, "Malformed fC showed the eye");

                        // Options : écrites dans le fichier, relues, grille appliquée à la carte.
                        options.Grid = true;
                        Check(map.ShowGrid && BotOptions.Load(optionsPath).Grid, "Grid option is not applied or not saved");
                        Check(form.ProcessShortcut(Keys.D1 | Keys.Shift) && !options.Grid && !map.ShowGrid && !BotOptions.Load(optionsPath).Grid, "Maj+& does not toggle the grid");
                        Check(form.ProcessShortcut(Keys.D4 | Keys.Shift) && !options.MapInfos, "Maj+' does not toggle the map infos");
                        PumpUntil(() => !banner.MapInfos.Visible, "map infos hidden");
                        options.MapInfos = true; PumpUntil(() => banner.MapInfos.Visible, "map infos shown");
                        options.ViewAllMonsterInGroup = false; Check(!map.ViewAllMonsterInGroup, "ViewAllMonsterInGroup is not applied");
                        options.ViewAllMonsterInGroup = true;

                        // Raccourcis : c ouvre les caractéristiques ; option BannerShortcuts ; touche personnalisée relue.
                        Check(form.ProcessShortcut(Keys.C) && form.Panels.Current is Outil_Azur_complet.Bot.Panels.StatsPanel, "Shortcut c does not open the Stats panel");
                        Check(form.ProcessShortcut(Keys.C) && form.Panels.Current == null, "Shortcut c does not close the Stats panel");
                        options.BannerShortcuts = false;
                        Check(!form.ProcessShortcut(Keys.C) && form.Panels.Current == null, "Banner shortcuts still work when disabled");
                        options.BannerShortcuts = true;
                        options.SetShortcut("CHARAC", new ShortcutKey((int)Keys.K, 0));
                        Check(new ShortcutKey(75, 0).Equals(BotOptions.Load(optionsPath).GetShortcut("CHARAC")), "Custom shortcut is not saved");
                        Check(!form.ProcessShortcut(Keys.C) && form.ProcessShortcut(Keys.K) && form.Panels.Current is Outil_Azur_complet.Bot.Panels.StatsPanel, "Custom shortcut is not used");
                        form.Panels.CloseAll(); options.SetShortcut("CHARAC", null);
                        Check(form.ProcessShortcut(Keys.C), "Client shortcut is not restored"); form.Panels.CloseAll();
                        form.ActiveControl = form.Chat.Input;
                        if (ReferenceEquals(form.ActiveControl, form.Chat.Input)) Check(!form.ProcessShortcut(Keys.C) && form.Panels.Current == null, "Shortcut c acts while typing");
                        form.ActiveControl = banner.Shortcuts.SlotButtons[0];
                        Check(!(form.ActiveControl is TextBoxBase), "The chat input keeps the focus");
                        // & (SH1) hors combat : le sort est refusé localement, rien ne part.
                        Check(form.ProcessShortcut(Keys.D1), "SH1 is not handled"); NoPacket(peer, "SH1 outside a fight sent a packet");
                        // Maj+d : Mount.ride → Rr.
                        Check(form.ProcessShortcut(Keys.D | Keys.Shift) && Read(peer) == "Rr", "Maj+d does not send Rr");

                        // Barre de sorts : 14 cases de 25 × 25 et le corps à corps ; glisser le sort 10 en case 3.
                        ShortcutBar bar = banner.Shortcuts;
                        Check(bar.SlotButtons.Count == 14 && bar.SlotButtons.All(b => b.Width == 25 && b.Height == 25) && bar.CloseCombatSlot.Width == 25, "Shortcut bar is not 14 + 1 cells of 25 × 25");
                        Check(bar.SpellAt(1) == 10 && bar.SpellAt(2) == 11 && bar.SpellAt(3) == 12 && bar.SpellAt(4) == null, "Learned spells are not laid out in order");
                        PumpUntil(() => bar.SlotButtons[0].GetType().GetProperty("Icon").GetValue(bar.SlotButtons[0], null) != null, "spell icon");
                        string key = bar.CharacterKey;
                        Check(key == "42|Personnage de test", "Character key differs");
                        Check(!Result(bar.DropSpellAsync(10, 0)) && !Result(bar.DropSpellAsync(10, 15)) && !Result(bar.DropSpellAsync(99, 3)), "Invalid drops were accepted");
                        NoPacket(peer, "An invalid drop sent SM");
                        Check(Result(bar.DropSpellAsync(10, 3)) && Read(peer) == "SM10|3", "Drop does not send SM10|3");
                        NoPacket(peer, "A packet other than SM10|3 was sent");
                        Feed(account, "BN");
                        Check(bar.SpellAt(3) == 10 && BotOptions.Load(optionsPath).SpellBar(key)[3] == 10, "Spell order is not kept locally");
                        Check(!Result(bar.DropSpellAsync(10, 3)), "Dropping a spell on its own cell was accepted"); NoPacket(peer, "Same-cell drop sent SM");
                        // Le même dépôt par le gestionnaire de glisser-déposer de la case : sort 11 (case 1) vers la case 3.
                        var data = new DataObject(); data.SetData(ShortcutBar.DragFormat, new ShortcutPayload(ShortcutSlotKind.Spell, 11, 1, 1));
                        typeof(ShortcutBar).GetMethod("OnSlotDragDrop", Any).Invoke(bar, new object[] { bar.SlotButtons[2], new DragEventArgs(data, 0, 0, 0, DragDropEffects.Move, DragDropEffects.Move) });
                        Check(Read(peer) == "SM11|3", "Drag and drop does not send SM11|3");
                        PumpUntil(() => bar.SpellAt(3) == 11, "drag result");
                        Check(BotOptions.Load(optionsPath).SpellBar(key).Count == 1 && BotOptions.Load(optionsPath).SpellBar(key)[3] == 11, "Previous occupant kept its local cell");
                        NoPacket(peer, "Drag and drop sent more than SM11|3");

                        // Objets : case 1 = objet en position 35 ; clic → OU ; dépôt → OM<id>|<case + 34>|<quantité>.
                        bar.Tab = ShortcutTab.Items;
                        Check(bar.ItemAt(1) == 1001 && bar.KindAt(1) == ShortcutSlotKind.Item && bar.ItemAt(2) == null, "Shortcut item at position 35 is not in cell 1");
                        Check(bar.Activate(1), "Item cell is empty"); Check(Read(peer) == "OU1001|", "Item cell does not send OU1001|");
                        Check(Result(bar.DropItemAsync(1002, 2, 4)) && Read(peer) == "OM1002|36|4", "Item drop does not send OM1002|36|4");
                        Check(!Result(bar.DropItemAsync(1003, 3, 1)), "A non usable item was accepted"); NoPacket(peer, "A non usable item sent OM");
                        Check(form.ProcessShortcut(Keys.OemBackslash) && bar.Tab == ShortcutTab.Spells, "< does not swap the bar tabs");

                        // Chat : filtre des mots (option CensorshipFilter) et heure des messages (option TimestampInChat).
                        var chat = form.Chat;
                        Feed(account, "cMK|77|Amicale|vilain zorglub !");
                        PumpUntil(() => chat.View.DisplayedLines.Any(l => l.Text.Contains("vilain")), "censored line");
                        Check(!chat.View.DisplayedLines.Any(l => l.Text.Contains("zorglub")), "Censorship filter is not applied to the chat");
                        options.CensorshipFilter = false;
                        Feed(account, "cMK|77|Amicale|encore zorglub");
                        PumpUntil(() => chat.View.DisplayedLines.Any(l => l.Text.Contains("encore zorglub")), "uncensored line");
                        options.TimestampInChat = false; Check(!chat.View.ShowTimestamps, "TimestampInChat is not applied");
                        chat.View.ShowTimestamps = true; Check(options.TimestampInChat && BotOptions.Load(optionsPath).TimestampInChat, "Chat timestamps toggle is not saved");

                        // Clic droit global : version, qualité, options, barre déplaçable à venir, diagnostic.
                        ContextMenuStrip global = form.GlobalMenu;
                        string[] names = global.Items.Cast<ToolStripItem>().Select(i => i.Name).ToArray();
                        Check(new[] { "version", "quality", "options", "movable-bar", "diagnostic" }.All(names.Contains) && !global.Items["version"].Enabled && !global.Items["movable-bar"].Enabled,
                            "Global menu entries differ: " + string.Join(",", names));
                        ((ToolStripMenuItem)global.Items["quality"]).DropDownItems["quality-low"].PerformClick();
                        Check(options.DefaultQuality == "low" && map.Quality == MapQuality.BAS, "Quality is not applied to the map");
                        options.DefaultQuality = "high"; Check(map.Quality == MapQuality.HAUT, "High quality is not applied");
                        global.Items["options"].PerformClick();
                        OptionsForm window = form.OptionsWindow;
                        Check(window != null && window.CheckOf("Grid") != null && window.ChoiceOf("DefaultQuality") != null && window.ShortcutList.Items.Count > 50, "Options window is incomplete");
                        window.CheckOf("Grid").Checked = true;
                        Check(options.Grid && map.ShowGrid, "Options window does not apply the grid");
                        options.Grid = false; Check(!window.CheckOf("Grid").Checked, "Options window does not follow the options");
                        Check(!window.AssignShortcut("CHARAC", Keys.S) && options.GetShortcut("CHARAC") == null, "A shortcut conflict was accepted");
                        Check(window.AssignShortcut("CHARAC", Keys.K) && new ShortcutKey(75, 0).Equals(options.GetShortcut("CHARAC")), "Options window does not change a shortcut");
                        options.ResetShortcuts();
                        window.Close(); PumpUntil(() => form.OptionsWindow == null, "options window closed");

                        // Menu principal : entrées du client, boîte AskMainMenu par Échap sans volet ouvert.
                        ContextMenuStrip main = banner.MainMenu.Build();
                        Check(main.Items.Count == 6 && main.Items[0].Enabled && main.Items[1].Enabled && !main.Items[3].Enabled, "Main menu entries differ");
                        Check(form.ProcessShortcut(Keys.Escape) && banner.MainMenu.Dialog != null, "Escape without panel does not open AskMainMenu");
                        Check(banner.MainMenu.Answer(MainMenuChoice.Cancel), "AskMainMenu cannot be cancelled");
                        PumpUntil(() => banner.MainMenu.Dialog == null, "main menu closed");
                        Check(form.Visible, "Cancel closed the game window");

                        NoPacket(peer, "The banner sent an unexpected packet");
                        form.Close();
                    }
                }
            }
        }
        finally
        {
            listener.Stop(); Environment.CurrentDirectory = previous;
            try { AssetsRoot(null); } catch (Exception) { }
            LangData.Clear();
        }
    }
}
