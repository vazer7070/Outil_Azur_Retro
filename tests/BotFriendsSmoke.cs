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
using Outil_Azur_complet.Bot.Controls;
using Outil_Azur_complet.Bot.Menus;
using Outil_Azur_complet.Bot.Panels;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Actions;
using Tool_BotProtocol.Game.Chat;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Social;

// Amis, ennemis et conjoint selon dofus.aks.Friends / dofus.aks.Enemies (client 1.34) et GameClient de StarLoco, sur un
// serveur fictif local : FA<nom> et ses refus locaux, FAK, FAEf/y/a/m, FL synthétique (connecté, en combat, hors ligne, ligne
// illisible) écrit dans le chat fenêtre fermée, FD*compte → FDK → FL, iA, iAK tronqué de StarLoco sans exception, iAEA.,
// iL, iD → iDK → iL, FS (conjoint connecté, hors ligne, introuvable), FJS, FJC±, FO±, paquets mal formés ; puis le volet
// Amis (bouton du bandeau, FL à l'ouverture, iL au changement d'onglet, lignes au pixel témoin avec des images synthétiques
// lues dans un dossier temporaire, menu d'une ligne, ×, ajout FA%/iA% puis la liste, case FO±, fiche du conjoint, boîte de
// liste pleine) et le menu d'un joueur de la carte (FA<nom>, iA<nom>).
internal static class BotFriendsSmoke
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
        try { Application.EnableVisualStyles(); Run(); Console.WriteLine("OK: FA/FAK/FAE, FL, FD, iA/iAK partiel/iAE, iL, iD, FS, FJS, FJC, FO, volet Amis et menu du joueur"); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static object Get(object target, string field) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static IEnumerable<Control> All(Control value) { yield return value; foreach (Control child in value.Controls) foreach (var nested in All(child)) yield return nested; }
    private static void PumpUntil(Func<bool> done, int seconds = 6)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(seconds);
        while (!done()) { if (DateTime.UtcNow > end) throw new TimeoutException("Friends loopback timed out"); Application.DoEvents(); Thread.Sleep(5); }
        Application.DoEvents();
    }
    private static void Complete(Task task) { PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static T Result<T>(Task<T> task) { Complete(task); return task.Result; }
    private static void Feed(Accounts account, string packet) { Complete(MessagesReception.ReceptionAsync(account.Connexion, packet)); }
    /// <summary>Paquet traité sur un fil du pool, comme la boucle de réception réseau.</summary>
    private static void FeedFromNetwork(Accounts account, string packet) { Complete(Task.Run(() => MessagesReception.ReceptionAsync(account.Connexion, packet))); }
    private static bool Sent(Task<InteractionResult> task) => Result(task).Sent;
    private static string Read(Socket socket)
    {
        PumpUntil(() => socket.Available > 0);
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
    private static void Click(object button) => ((Button)button).PerformClick();
    private static bool Near(Color a, Color b) => Math.Abs(a.R - b.R) <= 12 && Math.Abs(a.G - b.G) <= 12 && Math.Abs(a.B - b.B) <= 12;
    private static void Png(string folder, string name, Color color, int width, int height)
    {
        Directory.CreateDirectory(folder);
        using (var image = new Bitmap(width, height)) { using (var graphics = Graphics.FromImage(image)) graphics.Clear(color); image.Save(Path.Combine(folder, name + ".png"), ImageFormat.Png); }
    }
    private static IEnumerable<string> Texts(ToolStripItemCollection items) => items.Cast<ToolStripItem>().Select(item => item is ToolStripSeparator ? "-" : item.Text);
    private static ToolStripItem Item(ContextMenuStrip menu, string text) => menu.Items.Cast<ToolStripItem>().First(item => item.Text == text);
    private static bool Chat(Accounts account, ChatMessageKind kind, string text) => account.Game.Chat.Messages.Any(message => message.Kind == kind && message.Text.Contains(text));
    private static int ChatCount(Accounts account, string text) => account.Game.Chat.Messages.Count(message => message.Text.Contains(text));

    // ClientAssets est interne à l'application : sa racine (dossier temporaire du test) et son cache passent par la réflexion.
    private static Type Assets => typeof(LoginForm).Assembly.GetType("Outil_Azur_complet.Bot.ClientAssets");
    private static void AssetsRoot(string value) => Assets.GetProperty("Root", Any).SetValue(null, value, null);
    private static bool Cached(string family, string name)
    {
        var args = new object[] { family, name, null };
        return (bool)Assets.GetMethod("TryCached", Any).Invoke(null, args) && args[2] != null;
    }

    // Lignes synthétiques au format de Account.parseFriendList : compte;état;nom;niveau;alignement;classe;sexe;gfx.
    private const string Online = "CompteAmi;2;Ami fictif;50;1;9;0;90";
    private const string Away = "CompteAbsent";

    private static void Run()
    {
        string previous = Environment.CurrentDirectory;
        string folder = Path.Combine(TestPaths.Work, "bot-friends"); Directory.CreateDirectory(folder); Environment.CurrentDirectory = folder;
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var logs = new ConcurrentQueue<string>();
        try
        {
            LangData.Clear();
            MessagesReception.Init();
            Map.AllBotMaps[900092] = new Map { MapID = 900092, MapWidth = 3, MapHeight = 4, MapData = string.Concat(Enumerable.Repeat("HhGaeaaaaa", 18)) };

            using (var account = new Accounts(new AccountConfig("synthetic-friends", "synthetic", "loopback")))
            {
                account.Logger.log_event += (entry, color) => logs.Enqueue(entry.message);
                FriendsActions friends = account.Game.Interactions.Friends;
                Check(friends != null, "Interactions.Friends is missing");
                // Hors connexion : rien n'est envoyé.
                Check(!Sent(friends.AddFriendAsync("Ami fictif")) && !Sent(friends.RefreshAsync(FriendListKind.Friends)), "Friends actions were accepted while disconnected");

                Task<Socket> accept = listener.AcceptSocketAsync();
                Complete(account.Connexion.ConnectToServer(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port)); Complete(accept);
                using (Socket peer = accept.Result)
                {
                    peer.ReceiveTimeout = 6000;
                    account.Game.character.SetPerso_Data(42, "Personnage de test", 25, 0, 8);
                    Feed(account, "GDM|900092|date|key"); Check(Read(peer) == "GI", "GDM did not request GI");
                    Feed(account, "GM|+0;1;0;42;Personnage de test;1;100^100;0|+5;1;0;77;Ami fictif;1;10^100;0");
                    account.AccountStates = AccountStates.CONNECTED_INACTIVE;
                    int changes = 0; Action counted = () => Interlocked.Increment(ref changes);
                    friends.Changed += counted;

                    // Ajout : FA<nom> (menu, /f A) ; nom vide, « * » et séparateurs refusés sans paquet.
                    Check(Sent(friends.AddFriendAsync(" Ami fictif ")) && Read(peer) == "FAAmi fictif", "Add does not send FA<name>");
                    Check(!Sent(friends.AddFriendAsync("")) && !Sent(friends.AddFriendAsync("*")) && !Sent(friends.AddEnemyAsync("a|b"))
                        && !Sent(friends.RemoveAsync(FriendListKind.Friends, (FriendEntry)null)), "Invalid names were accepted");
                    NoPacket(peer, "A refused add reached the server");
                    Check(Sent(friends.RefreshAllAsync()) && Read(peer) == "FL" && Read(peer) == "iL", "Refresh does not send FL then iL");

                    // FAK : ami ajouté (ligne de StarLoco, niveau « ? » si l'amitié n'est pas réciproque).
                    Feed(account, "FAKCompteAmi;?;Ami fictif;?;-1;9;0;90");
                    FriendEntry added = friends.Friends.Single();
                    Check(added.Account == "CompteAmi" && added.Name == "Ami fictif" && added.IsOnline && added.State == FriendState.Unknown && added.Level == null
                        && added.LevelText == "?" && added.Alignment == -1 && added.Sex == 0 && added.Gfx == 90 && added.RemoveTarget == "*CompteAmi" && !added.IsPartial,
                        "FAK line is not read like getFriendObjectFromData");
                    Check(Chat(account, ChatMessageKind.Info, "Ami fictif a été ajouté") && friends.LastMessage.Contains("Ami fictif"), "FAK was not announced in the chat");
                    Feed(account, "FAKcompteami;?;Ami fictif;50;1;9;0;90");
                    Check(friends.Friends.Count == 1 && friends.Friends[0].Level == 50, "A second FAK for the same account duplicated the line");

                    // FAE : erreurs du client (ERROR_CHAT), boîte pour la liste pleine, code inconnu ignoré.
                    var boxes = new List<string>();
                    Action<string, string> box = (title, text) => boxes.Add(title + "|" + text);
                    friends.ErrorBox += box;
                    Feed(account, "FAEf"); Check(Chat(account, ChatMessageKind.Error, "n'existe pas") && friends.LastMessage.Contains("n'existe pas"), "FAEf is not reported");
                    Feed(account, "FAEy"); Check(Chat(account, ChatMessageKind.Error, "t'ajouter en ami"), "FAEy is not reported");
                    Feed(account, "FAEa"); Check(Chat(account, ChatMessageKind.Error, "Déjà dans ta liste d'amis"), "FAEa is not reported");
                    Check(boxes.Count == 0, "An error box was raised before FAEm");
                    Feed(account, "FAEm"); Check(boxes.Count == 1 && boxes[0] == "Amis|Ta liste d'amis est pleine.", "FAEm does not raise the error box");
                    int errors = account.Game.Chat.Messages.Count(message => message.Kind == ChatMessageKind.Error);
                    Feed(account, "FAEz"); Feed(account, "FAE"); Feed(account, "FAK"); Feed(account, "FAK;?;x");
                    Check(account.Game.Chat.Messages.Count(message => message.Kind == ChatMessageKind.Error) == errors && friends.Friends.Count == 1,
                        "Unknown or empty FA answers changed the state");

                    // FL synthétique, fenêtre fermée : deux lignes (connecté en combat, hors ligne), une illisible ignorée, liste écrite dans le chat.
                    Feed(account, "FL|" + Online + "|;?;Sans compte|" + Away);
                    IReadOnlyList<FriendEntry> list = friends.Friends;
                    Check(list.Count == 2 && friends.HasReceived(FriendListKind.Friends), "FL with two entries was not read");
                    Check(list[0].Name == "Ami fictif" && list[0].IsInFight && list[0].Level == 50 && list[0].Alignment == 1 && list[0].Gfx == 90 && list[0].Guild == "9",
                        "Connected FL line is wrong");
                    Check(list[1].Account == "CompteAbsent" && list[1].Name == "CompteAbsent" && !list[1].IsOnline && list[1].LevelText == "" && list[1].Gfx == null
                        && list[1].RemoveTarget == "*CompteAbsent", "Disconnected FL line is wrong");
                    Check(Chat(account, ChatMessageKind.Info, "Ta liste d'amis :") && Chat(account, ChatMessageKind.Info, " - CompteAmi (Ami fictif) Niveau:50, dans un combat.")
                        && Chat(account, ChatMessageKind.Info, " - CompteAbsent"), "Closed-window FL was not written to the chat like onFriendsList");
                    Feed(account, "FL|A;1;Nom en solo;10;0;;1;11|B;x;Nom inconnu;?;z;;7;-4");
                    Check(friends.Friends[0].State == FriendState.Solo && friends.Friends[0].Sex == 1 && friends.Friends[1].State == FriendState.Unknown
                        && friends.Friends[1].Level == null && friends.Friends[1].Alignment == -1 && friends.Friends[1].Sex == null && friends.Friends[1].Gfx == null,
                        "FL states or unreadable fields are wrong");
                    Feed(account, "FL"); Check(friends.Friends.Count == 0 && Chat(account, ChatMessageKind.Info, "Ta liste d'amis est vide."), "Empty FL is not reported");
                    Feed(account, "FL|" + Online + "|" + Away);

                    // FD*compte (× de la ligne), FDK → liste redemandée ; FDEf.
                    Check(Sent(friends.RemoveAsync(FriendListKind.Friends, friends.Friends[1])) && Read(peer) == "FD*CompteAbsent", "Remove does not send FD*<account>");
                    Check(Sent(friends.RemoveAsync(FriendListKind.Friends, "Ami fictif")) && Read(peer) == "FDAmi fictif", "Remove by name does not send FD<name>");
                    Feed(account, "FDK"); Check(Read(peer) == "FL" && Chat(account, ChatMessageKind.Info, "Tu viens de perdre un ami."), "FDK does not request FL again");
                    Feed(account, "FDEf"); Check(friends.LastMessage.Contains("n'existe pas"), "FDEf is not reported");
                    Feed(account, "FDEq"); NoPacket(peer, "An unknown FD error sent a packet");

                    // Ennemis : iA<nom>, iAK tronqué ou au format partiel de StarLoco, iAEA., iL, iD → iDK → iL.
                    Check(Sent(friends.AddEnemyAsync("Ennemi fictif")) && Read(peer) == "iAEnnemi fictif", "Add enemy does not send iA<name>");
                    Feed(account, "iAKlogin-fictif;2;Ennemi fictif;36;10;0;100.FL.");
                    FriendEntry enemy = friends.Enemies.Single();
                    Check(enemy.IsPartial && enemy.Account == null && enemy.Name == "Ennemi fictif" && enemy.RemoveTarget == "Ennemi fictif" && enemy.Level == null,
                        "StarLoco iAK was not kept as a partial entry");
                    Check(Chat(account, ChatMessageKind.Info, "Ennemi fictif a été ajouté à ta liste d'ennemis."), "iAK was not announced");
                    foreach (string truncated in new[] { "iAK", "iAKlogin-fictif;2", "iAK;;", "iAKlogin;2;", "iA", "iAE" }) Feed(account, truncated);
                    Check(friends.Enemies.Count == 1, "Truncated iAK changed the enemies");
                    Feed(account, "iAEA."); Check(Chat(account, ChatMessageKind.Error, "Déjà dans ta liste d'ennemis"), "iAEA. is not read as already an enemy");
                    Feed(account, "iL|CompteEnnemi;?;Ennemi fictif;30;2;8;1;81");
                    Check(friends.Enemies.Single().Account == "CompteEnnemi" && !friends.Enemies[0].IsPartial && friends.Enemies[0].Gfx == 81, "iL did not replace the partial entry");
                    Check(Chat(account, ChatMessageKind.Info, "Ta liste d'ennemis :"), "Closed-window iL was not written to the chat");
                    Check(Sent(friends.RemoveAsync(FriendListKind.Enemies, friends.Enemies[0])) && Read(peer) == "iD*CompteEnnemi", "Remove enemy does not send iD*<account>");
                    Feed(account, "iDK"); Check(Read(peer) == "iL" && Chat(account, ChatMessageKind.Info, "L'ennemi a été effacé."), "iDK does not request iL again");

                    // Conjoint : FS connecté (sexe inverse du personnage), FJS, FJC± gardé d'un FS à l'autre, hors ligne, introuvable.
                    Check(!Sent(friends.JoinSpouseAsync()) && !Sent(friends.FollowSpouseAsync(true)), "Spouse actions were accepted without a spouse");
                    Feed(account, "FSConjointe fictive|81|-1|16777215|-1|900092|60|0|");
                    SpouseInfo spouse = friends.Spouse;
                    Check(spouse != null && spouse.Name == "Conjointe fictive" && spouse.Gfx == 81 && spouse.IsConnected && spouse.MapId == 900092 && spouse.Level == 60
                        && !spouse.IsInFight && !spouse.IsFollowed && spouse.Sex == "f" && spouse.Colors[1] == 16777215, "FS is not read like onSpouse");
                    Check(Sent(friends.JoinSpouseAsync()) && Read(peer) == "FJS", "Join does not send FJS");
                    Check(Sent(friends.FollowSpouseAsync(true)) && Read(peer) == "FJC+" && friends.Spouse.IsFollowed, "Follow does not send FJC+");
                    Feed(account, "FSConjointe fictive|81|-1|-1|-1|900093|60|1|");
                    Check(friends.Spouse.IsFollowed && friends.Spouse.IsInFight && friends.Spouse.MapId == 900093, "Follow state was lost on the next FS");
                    Check(Sent(friends.FollowSpouseAsync(false)) && Read(peer) == "FJC-" && !friends.Spouse.IsFollowed, "Stop follow does not send FJC-");
                    Feed(account, "FSConjointe fictive|81|-1|-1|-1||");
                    Check(!friends.Spouse.IsConnected && !Sent(friends.JoinSpouseAsync()) && !Sent(friends.FollowSpouseAsync(true)), "Disconnected spouse actions were accepted");
                    Feed(account, "FS|"); Check(friends.Spouse == null, "FS| did not clear the spouse");
                    NoPacket(peer, "Refused spouse actions reached the server");

                    // FO± : reçu à l'entrée en jeu, posé à l'envoi (StarLoco ne répond que BN).
                    Feed(account, "FO+"); Check(friends.WarnOnFriendLogin == true, "FO+ is not read");
                    Feed(account, "FO?"); Feed(account, "FO"); Check(friends.WarnOnFriendLogin == true, "Malformed FO changed the state");
                    Check(Sent(friends.SetWarnOnFriendLoginAsync(false)) && Read(peer) == "FO-" && friends.WarnOnFriendLogin == false, "Warn does not send FO-");

                    // Paquets mal formés : journalisés au besoin, jamais propagés. Comme le client (troisième lettre « E » ou non),
                    // un FD ou iD nu vaut un retrait réussi et redemande la liste.
                    var replies = new Dictionary<string, string> { { "FD", "FL" }, { "iD", "iL" }, { "FDK", "FL" } };
                    foreach (string broken in new[] { "FL|", "FL||;|", "FA", "FD", "FS", "FSx|y|z", "iL", "iD", "iDE", "FDK" })
                    {
                        Feed(account, broken);
                        if (replies.ContainsKey(broken)) Check(Read(peer) == replies[broken], broken + " did not request " + replies[broken]);
                    }
                    NoPacket(peer, "A malformed packet was answered");
                    Check(!logs.Any(entry => entry.Contains("Paquet d'amis illisible")), "A friends packet threw: " + string.Join(" / ", logs.Where(entry => entry.Contains("illisible"))));
                    Check(changes > 10, "Changed was not raised");
                    friends.Changed -= counted; friends.ErrorBox -= box;
                    friends.Clear();
                    Check(friends.Friends.Count == 0 && friends.Spouse == null && friends.WarnOnFriendLogin == null && !friends.HasReceived(FriendListKind.Friends), "Clear kept the state");

                    // Volet Amis : textes du client et images synthétiques lus dans un dossier temporaire.
                    string lang = Path.Combine(folder, "BotLang"); Directory.CreateDirectory(lang);
                    File.WriteAllText(Path.Combine(lang, "lang.xml"), "<?xml version='1.0' encoding='utf-8'?>\n<BotLang famille=\"lang\" langue=\"fr\" version=\"1\" source=\"lang_fr_1.swf\">\n"
                        + "<texte cle=\"FRIENDS\" valeur=\"Amis fictifs\" />\n<texte cle=\"ENEMIES\" valeur=\"Ennemis fictifs\" />\n"
                        + "<texte cle=\"ADD_TO_FRIENDS\" valeur=\"Ajouter aux amis (test)\" />\n<texte cle=\"ADD_TO_ENEMY\" valeur=\"Ajouter aux ennemis (test)\" />\n"
                        + "<texte cle=\"SPOUSE\" valeur=\"Conjoint{~fe} (test)\" />\n<texte cle=\"YOUR_FRIEND_LIST\" valeur=\"Liste d'amis (test)\" />\n"
                        + "<texte cle=\"FRIENDS_LIST_FULL\" valeur=\"Liste pleine (test)\" />\n<texte cle=\"ADD_TO_FRIEND_LIST\" valeur=\"%1 ajouté (test).\" />\n</BotLang>\n",
                        new UTF8Encoding(false));
                    Check(LangData.Load(lang) == 1, "Synthetic texts were not loaded");
                    Check(FriendsTexts.Gendered("SPOUSE", "x", "f") == "Conjointe (test)" && FriendsTexts.Get("ADD_TO_FRIEND_LIST", "{0}", "Nom") == "Nom ajouté (test).",
                        "Client texts are not combined");
                    string images = Path.Combine(folder, "images");
                    Color miniColor = Color.FromArgb(255, 30, 160, 200), alignColor = Color.FromArgb(255, 200, 40, 160), faceColor = Color.FromArgb(255, 60, 170, 70);
                    Color swordColor = Color.FromArgb(255, 220, 120, 20), crossColor = Color.FromArgb(255, 150, 20, 20);
                    Png(Path.Combine(images, "Artworks", "Mini"), "90", miniColor, 16, 16);
                    Png(Path.Combine(images, "Artworks", "Faces"), "81", faceColor, 40, 40);
                    Png(Path.Combine(images, "Alignments", "mini"), "1", alignColor, 12, 12);
                    Png(Path.Combine(images, "Party"), "infos", swordColor, 14, 14);
                    Png(Path.Combine(images, "UI", "Client"), "fermer-haut", crossColor, 14, 14);
                    File.WriteAllText(Path.Combine(images, "Artworks", "Mini", "91.png"), "pas une image");
                    AssetsRoot(images);

                    using (var form = new GameClientFullform(account))
                    {
                        form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); Application.DoEvents();
                        var drawer = form.Panels; var panel = drawer.Get<FriendsPanel>();
                        Check(panel != null && drawer.Current == null && panel.Title == "Amis fictifs", "Friends panel is not registered with its client title");
                        NoPacket(peer, "The game window requested a list before the panel was opened");
                        // Icône Amis du bandeau : ouvre le volet, qui demande la liste de son onglet (FL).
                        Button icon = All(form).OfType<Button>().First(button => "client-icon".Equals(button.Tag) && button.AccessibleName == "Amis");
                        Check(icon.Enabled, "Friends banner icon is disabled");
                        Click(icon); PumpUntil(() => drawer.Current == panel);
                        Check(Read(peer) == "FL" && friends.WindowOpen && panel.CurrentTab == FriendListKind.Friends, "Opening the panel does not send FL");

                        int printed = ChatCount(account, "Liste d'amis (test)");
                        FeedFromNetwork(account, "FL|" + Online + "|" + Away + "|CompteCasse;?;Nom cassé;3;0;1;0;91");
                        FeedFromNetwork(account, "FSConjointe fictive|81|-1|-1|-1|900092|60|0|");
                        FriendRowList online = panel.OnlineList, offline = panel.OfflineList;
                        PumpUntil(() => online.Entries.Count == 2 && offline.Entries.Count == 1 && panel.SpouseView.Spouse != null);
                        Check(ChatCount(account, "Liste d'amis (test)") == printed, "Open-window FL was also written to the chat");
                        Check(((Label)Get(panel, "onlineTitle")).Text.EndsWith("(2)") && ((Label)Get(panel, "offlineTitle")).Text.EndsWith("(1)"), "Section counts are wrong");
                        Check(online.Detailed && !offline.Detailed && offline.Entries[0].Name == "CompteAbsent", "Rows are not split by state");

                        // Lignes au pixel témoin : alignement, illustration de classe, épée du combat, croix ; PNG illisible toléré.
                        PumpUntil(() => Cached("Artworks", "Mini/90") && Cached("Alignments", "mini/1") && Cached("Party", "infos") && Cached("Client", "fermer-haut"));
                        Check(!Cached("Artworks", "Mini/91"), "A broken PNG was cached as an image");
                        Check(online.Width > 150 && online.Height == FriendRowList.HeaderHeight + 2 * FriendRowList.RowHeight + 1, "Online rows are not laid out: " + online.Size);
                        using (var image = new Bitmap(online.Width, online.Height))
                        {
                            online.DrawToBitmap(image, new Rectangle(Point.Empty, online.Size));
                            Rectangle row = online.RowBounds(0), remove = online.RemoveBounds(0);
                            Check(Near(image.GetPixel(10, row.Y + 10), alignColor), "Alignment icon is not drawn");
                            Check(Near(image.GetPixel(27, row.Y + 10), miniColor), "Class artwork is not drawn");
                            Check(Near(image.GetPixel(remove.X + remove.Width / 2, remove.Y + remove.Height / 2), crossColor), "Remove cross is not drawn");
                            int fightX = remove.X - 3 - 28;
                            Check(Near(image.GetPixel(fightX + 14, row.Y + 10), swordColor), "Fight sword is not drawn for IN_MULTI");
                            Check(!Near(image.GetPixel(fightX + 14, online.RowBounds(1).Y + 10), swordColor), "Fight sword is drawn outside a fight");
                        }
                        using (var image = new Bitmap(offline.Width, Math.Max(1, offline.Height))) offline.DrawToBitmap(image, new Rectangle(Point.Empty, offline.Size));

                        // Fiche du conjoint : titre accordé, buste rond, Rejoindre (FJS), Suivre (FJC+) puis Ne plus suivre (FJC-).
                        SpouseCard card = panel.SpouseView;
                        Check(card.Visible && ((Label)Get(card, "title")).Text == "Conjointe (test)" && card.DetailText.Contains("60"), "Spouse card is not filled");
                        PumpUntil(() => Cached("Artworks", "Faces/81"));
                        using (var image = new Bitmap(card.Face.Width, card.Face.Height))
                        {
                            card.Face.DrawToBitmap(image, new Rectangle(Point.Empty, card.Face.Size));
                            Check(Near(image.GetPixel(card.Face.Width / 2, card.Face.Height / 2), faceColor), "Spouse bust is not drawn");
                        }
                        Check(card.JoinButton.Enabled && card.FollowButton.Enabled, "Spouse buttons are disabled");
                        card.JoinButton.PerformClick(); Check(Read(peer) == "FJS", "Join button does not send FJS");
                        string followText = card.FollowButton.Text;
                        card.FollowButton.PerformClick(); Check(Read(peer) == "FJC+", "Follow button does not send FJC+");
                        PumpUntil(() => card.FollowButton.Text != followText);
                        card.FollowButton.PerformClick(); Check(Read(peer) == "FJC-", "Second follow click does not send FJC-");

                        // Menu d'une ligne connectée : nom, Informations, Message privé, Inviter, Retirer (FD*compte).
                        Check(online.ClickRow("Ami fictif"), "Row was not found");
                        ContextMenuStrip menu = panel.LastMenu;
                        Check(menu != null && string.Join("|", Texts(menu.Items)) == "Ami fictif|" + MapActionTexts.Whois + "|" + MapActionTexts.PrivateMessage + "|"
                            + MapActionTexts.InviteToParty + "|-|Retirer de la liste", "Row menu differs: " + string.Join("|", Texts(menu?.Items ?? new ContextMenuStrip().Items)));
                        Item(menu, MapActionTexts.Whois).PerformClick(); Check(Read(peer) == "BWAmi fictif", "Row menu Whois does not send BW<name>");
                        menu.Close(); Application.DoEvents();
                        Check(online.ClickRow("Ami fictif"), "Row was not found again");
                        menu = panel.LastMenu;
                        Item(menu, "Retirer de la liste").PerformClick(); Check(Read(peer) == "FD*CompteAmi", "Row menu remove does not send FD*<account>");
                        menu.Close(); Application.DoEvents();
                        // Double clic : message privé dans la console.
                        string whisper = null; Action<string> input = text => whisper = text;
                        account.Game.Interactions.MapActions.ChatInputRequested += input;
                        Check(online.DoubleClickRow("Ami fictif") && whisper == "/w Ami fictif ", "Double click does not prepare a private message");
                        account.Game.Interactions.MapActions.ChatInputRequested -= input;
                        Check(!offline.DoubleClickRow("CompteAbsent"), "Disconnected rows open a private message");
                        // Boutons × : FD*compte, connectés comme hors ligne.
                        Check(offline.ClickRemove("CompteAbsent") && Read(peer) == "FD*CompteAbsent", "Offline × does not send FD*<account>");
                        Check(online.ClickRemove("Nom cassé") && Read(peer) == "FD*CompteCasse", "Online × does not send FD*<account>");

                        // Ajout depuis le volet : FA%<nom> puis FL, comme le bouton du client ; le champ est vidé.
                        var addName = (TextBox)Get(panel, "addName");
                        addName.Text = "Nouveau fictif"; Click(Get(panel, "addButton"));
                        Check(Read(peer) == "FA%Nouveau fictif" && Read(peer) == "FL", "Add button does not send FA%<name> then FL");
                        PumpUntil(() => addName.Text.Length == 0);
                        Click(Get(panel, "addButton")); NoPacket(peer, "An empty name was sent");

                        // Case d'avertissement : FO+ ; un FO- du serveur la décoche sans rien renvoyer.
                        var warn = (CheckBox)Get(panel, "warn");
                        warn.Checked = true; Check(Read(peer) == "FO+", "Warn checkbox does not send FO+");
                        FeedFromNetwork(account, "FO-"); PumpUntil(() => !warn.Checked);
                        NoPacket(peer, "Server FO- was echoed");

                        // Liste pleine : boîte du client, posée depuis un fil réseau.
                        FeedFromNetwork(account, "FAEm");
                        PumpUntil(() => BotDialogs.OpenDialogs.Count == 1);
                        Form dialog = BotDialogs.OpenDialogs[0];
                        Check(dialog.Text == "Amis fictifs" && All(dialog).Any(control => control.Name == "dialog-message" && control.Text == "Liste pleine (test)"), "FAEm box differs");
                        All(dialog).OfType<Button>().Single().PerformClick(); PumpUntil(() => BotDialogs.OpenDialogs.Count == 0);

                        // Onglet Ennemis : iL, pas de fiche du conjoint, ajout iA% puis iL.
                        var tabs = (FriendsTabStrip)Get(panel, "tabs");
                        tabs.ClickTab(FriendListKind.Enemies);
                        Check(Read(peer) == "iL" && panel.CurrentTab == FriendListKind.Enemies && tabs.Selected == FriendListKind.Enemies, "Enemies tab does not send iL");
                        FeedFromNetwork(account, "iL|CompteEnnemi;?;Ennemi fictif;30;2;8;1;81");
                        PumpUntil(() => online.Entries.Count == 1 && online.Entries[0].Name == "Ennemi fictif");
                        Check(!card.Visible && offline.Entries.Count == 0, "Enemies tab shows the spouse or stale rows");
                        addName.Text = "Rival fictif"; Click(Get(panel, "addButton"));
                        Check(Read(peer) == "iA%Rival fictif" && Read(peer) == "iL", "Enemy add button does not send iA%<name> then iL");
                        Check(online.ClickRemove("Ennemi fictif") && Read(peer) == "iD*CompteEnnemi", "Enemy × does not send iD*<account>");
                        tabs.ClickTab(FriendListKind.Friends); Check(Read(peer) == "FL", "Friends tab does not send FL");
                        PumpUntil(() => card.Visible);

                        // Volet fermé : une liste reçue est de nouveau écrite dans le chat ; réouverture → FL.
                        drawer.CloseAll(); Application.DoEvents();
                        Check(!drawer.IsOpen(panel) && !friends.WindowOpen, "Closing the panel kept WindowOpen");
                        FeedFromNetwork(account, "FL|" + Online);
                        PumpUntil(() => ChatCount(account, "Liste d'amis (test)") == printed + 1);
                        drawer.Show(panel); Check(Read(peer) == "FL" && friends.WindowOpen, "Reopening the panel does not send FL");

                        // Menu d'un joueur de la carte : « Ajouter aux amis » (FA<nom>) et « Ajouter aux ennemis » (iA<nom>), après Informations.
                        var provider = ActorMenuRegistry.Default.Providers.OfType<FriendsMenuProvider>().Single();
                        var player = account.Game.Map.Entites[77];
                        Check(provider.Handles(player) && !provider.Handles(account.Game.character), "Friends menu provider accepts the wrong actors");
                        List<MenuEntry> entries = provider.Entries(player, account.Game).ToList();
                        Check(entries.Select(entry => entry.Texte).SequenceEqual(new[] { "Ajouter aux amis (test)", "Ajouter aux ennemis (test)" })
                            && entries.All(entry => entry.Activé && entry.Action != null && entry.Infobulle.Contains("Ami fictif")), "Player menu entries differ");
                        var context = new ActorMenuContext(player, null, -1, account, null, drawer, Keys.None);
                        Check(Result(entries[0].Action(context)) != null && Read(peer) == "FAAmi fictif", "Add to friends does not send FA<name>");
                        Check(Result(entries[1].Action(context)) != null && Read(peer) == "iAAmi fictif", "Add to enemies does not send iA<name>");
                        IReadOnlyList<MenuEntry> merged = ActorMenuRegistry.Default.EntriesFor(player, account.Game);
                        int whois = merged.ToList().FindIndex(entry => entry.Texte == MapActionTexts.Whois);
                        int add = merged.ToList().FindIndex(entry => entry.Texte == "Ajouter aux amis (test)");
                        Check(whois >= 0 && add > whois && merged.Count(entry => entry.Texte == "Ajouter aux amis (test)") == 1, "Friends entries are missing from the player menu");
                        Check(!merged.Any(entry => entry.Infobulle != null && entry.Infobulle.Contains("FJ")), "Joining a friend (FJF) is offered");
                        form.Close();
                    }
                    Check(BotDialogs.OpenDialogs.Count == 0, "A dialog survived the game window");
                    Check(!logs.Any(entry => entry.Contains("abonné des amis a échoué") || entry.Contains("Paquet d'amis illisible")),
                        "A friends subscriber failed: " + string.Join(" / ", logs.Where(entry => entry.Contains("amis"))));
                }
            }
        }
        finally
        {
            LangData.Clear();
            try { AssetsRoot(null); } catch (Exception) { }
            listener.Stop(); Environment.CurrentDirectory = previous;
        }
    }
}
