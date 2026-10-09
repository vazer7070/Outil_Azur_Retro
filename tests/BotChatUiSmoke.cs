using System;
using System.Collections.Concurrent;
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
using Outil_Azur_complet.Bot.Controls.Chat;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Chat;
using Tool_BotProtocol.Game.Maps;

// Volet de discussion du bandeau (lot C2) contre un faux serveur local, fenêtre de jeu hors de l'écran, sans capture
// native : lignes cMK colorées par canal, filtres (cC±), saisie (/g, Maj + Entrée, Ctrl + Entrée, historique, 200
// caractères), menu des canaux, liens (menu du joueur, boussole), smileys (BS) et attitudes (eU), agrandissement.
// Les images du client sont des PNG synthétiques créés dans un dossier temporaire.
internal static class BotChatUiSmoke
{
    private static readonly List<Exception> uiErrors = new List<Exception>();
    private const BindingFlags Any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            string path = Path.Combine(TestPaths.ApplicationBin, new AssemblyName(e.Name).Name + ".dll");
            if (!File.Exists(path)) path = Path.ChangeExtension(path, "exe");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        Application.ThreadException += (s, e) => uiErrors.Add(e.Exception);
        try
        {
            Application.EnableVisualStyles(); Run();
            Check(uiErrors.Count == 0, "Interface errors: " + string.Join(" / ", uiErrors.Select(e => e.GetType().Name + " " + e.Message)));
            Console.WriteLine("OK: volet de discussion (couleurs, filtres cC±, /g, Maj/Ctrl + Entrée, historique, 200 caractères, menu des canaux, menu du joueur, boussole, BS, eU, agrandissement)");
        }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void PumpUntil(Func<bool> done, string what = "Chat UI loopback")
    {
        DateTime end = DateTime.UtcNow.AddSeconds(6);
        while (!done()) { if (DateTime.UtcNow > end) throw new TimeoutException(what + " timed out"); Application.DoEvents(); Thread.Sleep(5); }
        Application.DoEvents();
    }
    private static void Pump(int milliseconds)
    {
        DateTime end = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < end) { Application.DoEvents(); Thread.Sleep(5); }
    }
    private static void Complete(Task task) { PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void Feed(Accounts account, string packet) { Complete(MessagesReception.ReceptionAsync(account.Connexion, packet)); Application.DoEvents(); }
    private static string Read(Socket socket)
    {
        PumpUntil(() => socket.Available > 0, "Packet");
        using (var stream = new MemoryStream())
        {
            byte[] one = new byte[1];
            while (true) { if (socket.Receive(one) != 1) throw new IOException("Loopback disconnected"); if (one[0] == 0) return Encoding.UTF8.GetString(stream.ToArray()).TrimEnd('\r', '\n'); stream.WriteByte(one[0]); }
        }
    }
    private static void Expect(Socket socket, string packet, string message)
    {
        string read = Read(socket);
        Check(read == packet, message + " (lu : " + read + ")");
    }
    private static void NoPacket(Socket socket, string message)
    {
        Pump(300);
        Check(socket.Available == 0, message + (socket.Available > 0 ? " (reçu : " + Read(socket) + ")" : ""));
    }
    private static object Get(object target, string field) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static IEnumerable<Control> All(Control value) { yield return value; foreach (Control child in value.Controls) foreach (var nested in All(child)) yield return nested; }
    private static Point Center(Rectangle bounds) => new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
    private static Color Rgb(int value) => Color.FromArgb(255, (value >> 16) & 0xFF, (value >> 8) & 0xFF, value & 0xFF);
    private static ChatLine Line(ChatPanel chat, string text) => chat.View.DisplayedLines.LastOrDefault(line => line.Text == text);
    private static string Shown(ChatPanel chat) => string.Join(" / ", chat.View.DisplayedLines.Select(line => line.Text));

    private static void Png(string path, Color color, int width, int height)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using (var image = new Bitmap(width, height, PixelFormat.Format32bppArgb))
        {
            using (var graphics = Graphics.FromImage(image)) { graphics.Clear(Color.Transparent); using (var brush = new SolidBrush(color)) graphics.FillEllipse(brush, 1, 1, width - 2, height - 2); }
            image.Save(path, ImageFormat.Png);
        }
    }

    /// <summary>Image d'un contrôle et de ses enfants, chacun dessiné par son propre <c>OnPaint</c> (Mono ne compose pas les enfants).</summary>
    private static Bitmap Snapshot(Control root)
    {
        var result = new Bitmap(Math.Max(1, root.Width), Math.Max(1, root.Height));
        using (var graphics = Graphics.FromImage(result)) Compose(graphics, root, Point.Empty);
        return result;
    }
    private static void Compose(Graphics graphics, Control control, Point offset)
    {
        if (!control.Visible || control.Width < 1 || control.Height < 1) return;
        using (var own = new Bitmap(control.Width, control.Height))
        {
            control.DrawToBitmap(own, new Rectangle(0, 0, control.Width, control.Height));
            graphics.DrawImage(own, offset.X, offset.Y);
        }
        foreach (Control child in control.Controls.Cast<Control>().Reverse())
            Compose(graphics, child, new Point(offset.X + child.Left, offset.Y + child.Top));
    }

    /// <summary>Ouvre le menu d'un nom (clic gauche sur son lien) et renvoie le menu affiché.</summary>
    private static ContextMenuStrip OpenNameMenu(ChatPanel chat, string name, Keys modifiers = Keys.None)
    {
        ChatLinkArea area = chat.View.LinkAreas.LastOrDefault(a => a.Link.Kind == ChatLinkKind.Player && a.Link.Name == name);
        Check(area.Link != null, "No clickable name for " + name + " in: " + Shown(chat));
        Check(chat.View.ClickAt(Center(area.Bounds), MouseButtons.Left, modifiers), "Click on the name was not handled");
        Application.DoEvents();
        return chat.Links.LastMenu;
    }

    private static ToolStripItem Find(ContextMenuStrip menu, string text) =>
        menu.Items.Cast<ToolStripItem>().FirstOrDefault(item => (item.Text ?? string.Empty).IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);

    private static void Run()
    {
        string previous = Environment.CurrentDirectory;
        string folder = Path.Combine(TestPaths.Work, "bot-chat-ui"); Directory.CreateDirectory(folder); Environment.CurrentDirectory = folder;
        // Images du client synthétiques : jamais les ressources réelles ni un chemin « .\ressources ».
        string assetsRoot = Path.Combine(folder, "assets-" + Guid.NewGuid().ToString("N"));
        Png(Path.Combine(assetsRoot, "UI", "Client", "UI_BannerChatCommandAll.png"), Color.SteelBlue, 28, 28);
        Png(Path.Combine(assetsRoot, "UI", "Client", "ButtonEmoteUp.png"), Color.IndianRed, 44, 28);
        Png(Path.Combine(assetsRoot, "Smileys", "3.png"), Color.Gold, 44, 44);
        Png(Path.Combine(assetsRoot, "Emotes", "2.png"), Color.OliveDrab, 32, 32);
        Type assets = typeof(LoginForm).Assembly.GetType("Outil_Azur_complet.Bot.ClientAssets");
        Check(assets != null, "ClientAssets type is missing");
        assets.GetProperty("Root", Any).SetValue(null, assetsRoot, null);
        int uiThread = Thread.CurrentThread.ManagedThreadId;
        var reads = new ConcurrentQueue<KeyValuePair<string, int>>();
        EventInfo fileRead = assets.GetEvent("AssetFileRead", Any);
        Check(fileRead != null, "ClientAssets.AssetFileRead is missing");
        fileRead.GetAddMethod(true).Invoke(null, new object[] { new Action<string, int>((key, thread) => reads.Enqueue(new KeyValuePair<string, int>(key, thread))) });

        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            MessagesReception.Init();
            Map.AllBotMaps[900092] = new Map { MapID = 900092, MapWidth = 3, MapHeight = 4, X = 2, Y = -4, MapData = string.Concat(Enumerable.Repeat("HhGaeaaaaa", 18)) };
            using (var account = new Accounts(new AccountConfig("synthetic-chat-ui", "synthetic-secret", "loopback")))
            {
                Task<Socket> accept = listener.AcceptSocketAsync();
                Complete(account.Connexion.ConnectToServer(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port)); Complete(accept);
                using (Socket peer = accept.Result)
                {
                    peer.ReceiveTimeout = 6000;
                    account.Game.character.SetPerso_Data(42, "Testeuse", 25, 0, 8);
                    Feed(account, "GDM|900092|date|key"); Expect(peer, "GI", "GDM did not request GI");
                    Feed(account, "GM|+0;1;0;42;Testeuse;1;100^100;0|+5;1;0;77;Amicale;1;10^100;0");
                    account.AccountStates = AccountStates.CONNECTED_INACTIVE;

                    using (var form = new GameClientFullform(account))
                    {
                        form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-4000, -4000);
                        form.ShowInTaskbar = false; form.Show(); Application.DoEvents();
                        ChatPanel chat = form.Chat;
                        Check(chat != null && ReferenceEquals(Get(form, "chatPanel"), chat) && chat.Parent == form.Hud.LeftSlot, "The chat panel is not in the left slot of the HUD");
                        Check(!All(form.Hud).OfType<RichTextBox>().Any() && !All(form.Hud).OfType<ComboBox>().Any(), "The old RichTextBox / channel list chat is still in the HUD");
                        Check(chat.Input.MaxLength == ChatService.MaxInputLength && chat.Input.Width >= 130, "Chat input is too short or not limited like the client");
                        Check(chat.View.Height >= 3 * 13, "The chat text area shows fewer than three lines: " + chat.View.Height);
                        foreach (Button button in All(chat).OfType<Button>().Where(b => b.Visible))
                            Check((string)button.Tag == "client-icon" && button.Width >= 24 && button.Height >= 24, "Chat button too small: " + button.Name + " " + button.Size);

                        // cMK : ligne au format du client, couleur du canal, heure devant, nom et coordonnées cliquables.
                        Feed(account, "cC+*#$pi%:?!^");
                        for (int filter = 0; filter < ChatFilterBar.FilterCount; filter++) Check(chat.Toolbar.IsFilterOn(filter), "Initial cC+ list did not turn filter " + filter + " on");
                        Feed(account, "cMK|77|Amicale|Rendez-vous en [3,-5] !");
                        Feed(account, "cMK%|77|Amicale|salut la guilde");
                        Feed(account, "cMKF|77|Amicale|psst");
                        Feed(account, "cMK|77|Amicale|*danse*");
                        Feed(account, "cMK|77|Amicale|**12**");
                        Feed(account, "cMK|bad");
                        PumpUntil(() => chat.View.DisplayedLines.Count >= 4, "cMK lines");
                        ChatLine general = Line(chat, "Amicale : Rendez-vous en [3,-5] !"), guild = Line(chat, "(Guilde) Amicale : salut la guilde");
                        ChatLine whisper = Line(chat, "de Amicale : psst"), emote = Line(chat, "Amicale danse.");
                        Check(general != null && guild != null && whisper != null && emote != null, "cMK lines are not formatted like the client: " + Shown(chat));
                        Check(general.Color == Rgb(0x111111) && guild.Color == Rgb(0x663399) && whisper.Color == Rgb(0x0066FF) && emote.Color == Rgb(0x222222),
                            "Line colours differ from the client: " + general.Color + " " + guild.Color + " " + whisper.Color + " " + emote.Color);
                        Check(general.Filter == ChatFilter.Messages && guild.Filter == ChatFilter.Guild && whisper.Filter == ChatFilter.Whispers, "Lines are in the wrong filters");
                        Check(chat.View.DisplayText(general).StartsWith("[", StringComparison.Ordinal) && chat.View.DisplayText(general).Contains("] Amicale"), "No timestamp before the line");
                        Check(!chat.View.DisplayedLines.Any(line => line.Text.Contains("12")), "A speaking item message was shown");
                        Check(general.Segments.Any(s => s.Bold && s.Link != null && s.Link.Name == "Amicale" && s.Link.ActorId == 77), "The author is not a bold link with its sprite id");
                        Check(general.Links.Any(link => link.Kind == ChatLinkKind.Coordinates && link.X == 3 && link.Y == -5), "[3,-5] is not a link");
                        Check(account.Game.Chat.WhisperHistory.Items.Contains("/w Amicale "), "Received whisper not pushed to the whisper history");

                        // Rendu sans capture native : la zone de texte dessine des lignes sur le parchemin.
                        chat.View.Refresh();
                        using (Bitmap bitmap = Snapshot(chat))
                        {
                            bitmap.Save(Path.Combine(TestPaths.Work, "bot-chat-ui.png"), ImageFormat.Png);
                            Rectangle view = chat.View.Bounds;
                            int dark = 0;
                            for (int y = view.Top; y < view.Bottom; y += 1)
                                for (int x = view.Left; x < view.Right; x += 1) { Color c = bitmap.GetPixel(x, y); if (c.R + c.G + c.B < 300) dark++; }
                            Check(dark > 20, "The chat text area painted no text");
                        }

                        // Filtre 2 (canal général) : ligne cachée tout de suite, puis cC-* lu par le serveur.
                        Check(chat.Toolbar.ClickAt(Center(chat.Toolbar.FilterBounds(2))), "Filter 2 click not handled");
                        Check(Line(chat, general.Text) == null && Line(chat, guild.Text) != null, "Filter 2 off did not hide the general line only");
                        Expect(peer, "cC-*", "Filter 2 off is not cC-*");
                        Feed(account, "cC-*");
                        Check(!chat.Toolbar.IsFilterOn(2) && Line(chat, general.Text) == null, "Server echo cC-* changed filter 2");
                        Check(chat.Toolbar.ClickAt(Center(chat.Toolbar.FilterBounds(2))), "Filter 2 click not handled");
                        Expect(peer, "cC+*", "Filter 2 on is not cC+*");
                        Check(Line(chat, general.Text) != null, "Filter 2 on did not show the general line again");
                        Check(chat.Toolbar.ClickAt(Center(chat.Toolbar.FilterBounds(1))), "Filter 1 click not handled");
                        NoPacket(peer, "Filter 1 (errors) sent a packet");
                        chat.Toolbar.ClickAt(Center(chat.Toolbar.FilterBounds(1)));
                        Feed(account, "cC-%");
                        Check(!chat.Toolbar.IsFilterOn(4) && Line(chat, guild.Text) == null, "cC-% from the server did not turn the guild filter off");
                        Feed(account, "cC+%");
                        Check(chat.Toolbar.IsFilterOn(4) && Line(chat, guild.Text) != null, "cC+% did not turn the guild filter on");

                        // Saisie : commandes, Maj + Entrée (équipe), Ctrl + Entrée (guilde), préfixe du menu des canaux.
                        chat.Input.SetText("/g bonjour");
                        Check(chat.Input.HandleKey(Keys.Enter), "Enter not handled");
                        Expect(peer, "BM%|bonjour|", "« /g bonjour » + Entrée is not BM%|bonjour|");
                        Check(chat.Input.Text.Length == 0, "Input not cleared after sending");
                        chat.Input.SetText("à l'équipe");
                        chat.Input.HandleKey(Keys.Enter | Keys.Shift);
                        Expect(peer, "BM#|à l'équipe|", "Maj + Entrée is not BM#");
                        chat.Input.SetText("pour la guilde");
                        chat.Input.HandleKey(Keys.Enter | Keys.Control);
                        Expect(peer, "BM%|pour la guilde|", "Ctrl + Entrée is not BM%");
                        using (ContextMenuStrip menu = chat.Channels.BuildMenu())
                        {
                            string[] commands = menu.Items.OfType<ToolStripMenuItem>().Select(item => item.Tag as string).Where(tag => tag != null).ToArray();
                            Check(commands.SequenceEqual(new[] { "/s", "/t", "/p", "/g", "/a", "/r", "/b", "/i" }), "Channel menu entries differ from the client: " + string.Join(",", commands));
                            Check(!menu.Items.OfType<ToolStripMenuItem>().First(item => (string)item.Tag == "/t").Enabled, "Team channel enabled outside a fight");
                            Check(!menu.Items.OfType<ToolStripMenuItem>().First(item => (string)item.Tag == "/p").Enabled, "Party channel enabled outside a party");
                            menu.Items.OfType<ToolStripMenuItem>().First(item => (string)item.Tag == "/b").PerformClick();
                        }
                        Check(chat.Channels.Prefix == "/b" && chat.Input.Prefix == "/b" && chat.Channels.Button.Text == "/b", "Trade prefix not applied to the button and the input");
                        chat.Input.SetText("vends");
                        chat.Input.HandleKey(Keys.Enter);
                        Expect(peer, "BM:|vends|", "Message with the /b prefix is not BM:");
                        chat.Channels.Prefix = "/s";
                        PumpUntil(() => chat.Channels.Button.Image != null, "Channel icon");
                        Check(chat.Channels.Button.Text.Length == 0, "Default channel button does not show UI_BannerChatCommandAll");

                        // Historique : Haut rappelle la dernière ligne, puis la précédente ; Bas revient.
                        chat.Input.Clear();
                        chat.Input.HandleKey(Keys.Up);
                        Check(chat.Input.Text == "/b vends", "Up did not recall the last line: " + chat.Input.Text);
                        chat.Input.HandleKey(Keys.Up);
                        Check(chat.Input.Text == "/g pour la guilde", "Second Up did not recall the previous line: " + chat.Input.Text);
                        chat.Input.HandleKey(Keys.Down);
                        Check(chat.Input.Text == "/b vends", "Down did not come back: " + chat.Input.Text);
                        chat.Input.HandleKey(Keys.Up | Keys.Shift);
                        Check(chat.Input.Text == "/w Amicale ", "Shift + Up did not recall the received whisper: " + chat.Input.Text);
                        chat.Input.SetText("/w Ami");
                        chat.Input.HandleKey(Keys.Right | Keys.Control);
                        Check(chat.Input.Text == "/w Amicale ", "Ctrl + Right did not complete the present player's name: " + chat.Input.Text);

                        // 200 caractères au plus : 201 refusés (rien n'est envoyé, le texte reste), 200 envoyés tels quels.
                        string tooLong = new string('a', 201);
                        chat.Input.SetText(tooLong);
                        chat.Input.HandleKey(Keys.Enter);
                        NoPacket(peer, "A 201-character message was sent");
                        Check(chat.Input.Text == tooLong, "The refused text was not kept in the input");
                        Check(chat.View.DisplayedLines.Any(line => line.Filter == ChatFilter.Errors && line.Text.Contains("201")), "No error line for the 201-character message");
                        chat.Input.SetText(new string('b', 200));
                        chat.Input.HandleKey(Keys.Enter);
                        Expect(peer, "BM*|" + new string('b', 200) + "|", "A 200-character message was not sent whole");

                        // Clic sur un nom : menu du joueur (fournisseurs de la carte + entrées du chat), visible.
                        ContextMenuStrip playerMenu = OpenNameMenu(chat, "Amicale");
                        Check(playerMenu != null && playerMenu.Visible, "Clicking a name did not show a visible context menu");
                        Check(Find(playerMenu, "amis") != null && Find(playerMenu, "ennemis") != null && Find(playerMenu, "message privé") != null && Find(playerMenu, "Ignorer") != null,
                            "Player menu lacks the client entries: " + string.Join("|", playerMenu.Items.Cast<ToolStripItem>().Select(i => i.Text)));
                        // Les fournisseurs de la carte (actions M4, amis F2) et le chat ne doivent pas doubler une entrée.
                        foreach (string entry in new[] { "mes amis", "mes ennemis", "message privé", "groupe" })
                            Check(playerMenu.Items.Cast<ToolStripItem>().Count(item => (item.Text ?? string.Empty).IndexOf(entry, StringComparison.OrdinalIgnoreCase) >= 0) == 1,
                                "Player menu repeats « " + entry + " »: " + string.Join("|", playerMenu.Items.Cast<ToolStripItem>().Select(i => i.Text)));
                        Find(playerMenu, "amis").PerformClick();
                        Expect(peer, "FAAmicale", "« Ajouter à mes amis » is not FA<name>");
                        Find(OpenNameMenu(chat, "Amicale"), "ennemis").PerformClick();
                        Expect(peer, "iAAmicale", "« Ajouter à mes ennemis » is not iA<name>");
                        Find(OpenNameMenu(chat, "Amicale"), "message privé").PerformClick(); Application.DoEvents();
                        Check(chat.Input.Text == "/w Amicale ", "« Message privé » did not prepare /w <name>");
                        chat.Input.Clear();
                        Check(OpenNameMenu(chat, "Amicale", Keys.Shift) == null || !chat.Links.LastMenu.Visible, "Shift + click opened a menu");
                        Check(chat.Input.Text == "/w Amicale ", "Shift + click did not prepare /w <name>");
                        chat.Input.Clear();

                        // Joueur absent de la carte : menu du chat seul ; « Ignorer pour la session » masque ses messages suivants.
                        Feed(account, "cMK|88|Absente|bonjour");
                        PumpUntil(() => Line(chat, "Absente : bonjour") != null, "Absent player line");
                        ContextMenuStrip absent = OpenNameMenu(chat, "Absente");
                        Check(absent != null && absent.Visible && absent.Items[0].Text == "Absente", "Absent player menu missing or without the name header");
                        Find(absent, "Ignorer").PerformClick(); Application.DoEvents();
                        Check(chat.IsIgnored("Absente"), "Player not ignored for the session");
                        int before = chat.View.Lines.Count;
                        Feed(account, "cMK|88|Absente|spam");
                        Check(Line(chat, "Absente : spam") == null, "Message of an ignored player shown");
                        Find(OpenNameMenu(chat, "Absente"), "Ne plus ignorer").PerformClick(); Application.DoEvents();
                        Check(!chat.IsIgnored("Absente"), "Player still ignored");
                        Feed(account, "cMK|88|Absente|de retour");
                        PumpUntil(() => Line(chat, "Absente : de retour") != null, "Unignored line");

                        // Coordonnées : boussole dans le bandeau d'état.
                        Check(chat.View.EnsureVisible(Line(chat, general.Text)), "The general line is no longer displayed");
                        ChatLinkArea coordinates = chat.View.LinkAreas.Last(a => a.Link.Kind == ChatLinkKind.Coordinates);
                        Check(chat.View.ClickAt(Center(coordinates.Bounds), MouseButtons.Left, Keys.None), "Coordinates click not handled");
                        PumpUntil(() => ((Label)Get(form, "summary")).Text.Contains("[3,-5]"), "Compass feedback");

                        // Clic droit sur une ligne : copier, nom du joueur, heure.
                        ChatLinkArea name = chat.View.LinkAreas.Last(a => a.Link.Kind == ChatLinkKind.Player);
                        Point beyond = new Point(Math.Min(chat.View.Width - 12, name.Bounds.Right + 40), name.Bounds.Top + name.Bounds.Height / 2);
                        Check(chat.View.ClickAt(beyond, MouseButtons.Right, Keys.None), "Right click on a line not handled");
                        Check(chat.Links.LastMenu != null && chat.Links.LastMenu.Name == "chat-line-menu" && Find(chat.Links.LastMenu, "Copier") != null, "No line menu");
                        chat.Links.ReleaseLastMenu();

                        // Messages Im et réponses des actions de carte dans le chat.
                        int lines = chat.View.Lines.Count;
                        Feed(account, "Im1101");
                        Feed(account, "BWEInconnue");
                        PumpUntil(() => chat.View.Lines.Count >= lines + 2, "Im and BWE lines");
                        Check(chat.View.Lines.Skip(lines).Any(line => line.Filter == ChatFilter.Errors && line.Color == Rgb(0xC10000)), "Im error not shown in red");
                        Check(chat.View.Lines.Skip(lines).Any(line => line.Text.Contains("Inconnue")), "Whois error not shown in the chat");

                        // Smileys et attitudes : volet ouvert par son bouton, BS<id> et eU<id>.
                        Check(chat.Buttons.ClickAt(Center(chat.Buttons.ButtonBounds(ChatBarButton.Smileys))), "Smileys button not handled");
                        PumpUntil(() => chat.SmileysOpen, "Smileys panel");
                        Check(chat.Smileys.Smileys.Count == 15, "The client has 15 smileys");
                        Check(chat.Smileys.ClickAt(Center(chat.Smileys.SmileyBounds(3))), "Smiley click not handled");
                        Expect(peer, "BS3", "Chosen smiley is not BS3");
                        Feed(account, "eL7|0");
                        PumpUntil(() => chat.Smileys.Emotes.Contains(2), "Emote list");
                        Check(chat.Smileys.ClickAt(Center(chat.Smileys.EmoteBounds(2))), "Emote click not handled");
                        Expect(peer, "eU2", "Chosen attitude is not eU2");
                        chat.Smileys.Refresh();
                        chat.ShowSmileys(false);
                        Check(!chat.SmileysOpen, "Smileys panel did not close");

                        // Agrandir / réduire : +350 pixels au plus pour le bandeau, retour à 112.
                        int hud = form.Hud.Height;
                        Check(chat.Buttons.ClickAt(Center(chat.Buttons.ButtonBounds(ChatBarButton.OpenClose))), "Open/close button not handled");
                        Application.DoEvents(); form.PerformLayout(); Application.DoEvents();
                        Check(chat.Expanded && form.Hud.Height >= hud + 200 && form.Hud.Height <= hud + ChatPanel.ExpandOffset, "Expanding the chat did not grow the HUD: " + hud + " -> " + form.Hud.Height);
                        Check(chat.View.VisibleRowCount >= 12, "Expanded chat shows too few rows: " + chat.View.VisibleRowCount);
                        chat.Expanded = false; Application.DoEvents(); form.PerformLayout(); Application.DoEvents();
                        Check(form.Hud.Height == hud, "Collapsing the chat did not restore the HUD: " + form.Hud.Height);

                        // Images : lues hors du thread de l'interface, depuis le dossier temporaire.
                        Check(reads.Any(r => r.Key == "Client/UI_BannerChatCommandAll") && reads.Any(r => r.Key == "Smileys/3"), "Synthetic client images were not read");
                        Check(reads.All(r => r.Value != uiThread), "A PNG was decoded on the UI thread: " + string.Join(",", reads.Where(r => r.Value == uiThread).Select(r => r.Key)));
                        // Volet détaché de la session : les paquets suivants ne le touchent plus.
                        using (var detached = new ChatPanel(account, null))
                        {
                            int kept = detached.View.Lines.Count;
                            Check(kept > 0, "A new chat panel did not show the kept messages");
                            detached.ReleaseSession(); detached.ReleaseSession();
                            Feed(account, "cMK|77|Amicale|après détachement");
                            Feed(account, "Im0153");
                            Pump(100);
                            Check(detached.View.Lines.Count == kept, "A released chat panel still receives lines");
                            Check(Line(chat, "Amicale : après détachement") != null, "The live chat panel lost a line");
                        }
                        form.Close();
                    }
                    Application.DoEvents();
                }
            }
        }
        finally
        {
            listener.Stop();
            Environment.CurrentDirectory = previous;
            try { Directory.Delete(assetsRoot, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
