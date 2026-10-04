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
using Tool_BotProtocol.Game.Groupes;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Session;

// Groupe selon dofus.aks.Party / dofus.aks.Infos (client 1.34) et GameClient.parseGroupPacket de StarLoco, sur un serveur
// fictif local : invitation reçue jamais acceptée seule (PR sans interface, PA sur acceptation, Ignorer), équipe de comptes du
// bot, PIE, PCK/PL/PM+~-, PV, PF±, PG±, PW → IH, IC, paquets mal formés ; puis volet Groupe (boîte Oui / Non / Ignorer posée
// depuis un fil réseau, cases des membres avec illustration et jauge de vie lues dans un dossier temporaire, menu d'un membre,
// invitation par nom, annulation, localisation, boussole, fermeture sur PV) et menu du personnage.
internal static class BotPartySmoke
{
    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            string path = Path.Combine(TestPaths.ApplicationBin, new AssemblyName(e.Name).Name + ".dll");
            if (!File.Exists(path)) path = Path.ChangeExtension(path, "exe");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        try { Application.EnableVisualStyles(); Run(); Console.WriteLine("OK: PIK sans PA automatique, PA/PR/Ignorer, équipe du bot, PIE, PCK/PL/PM, PV, PF/PG, PW/IH, IC, volet Groupe et menus"); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static object Get(object target, string field) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static IEnumerable<Control> All(Control value) { yield return value; foreach (Control child in value.Controls) foreach (var nested in All(child)) yield return nested; }
    private static void PumpUntil(Func<bool> done, int seconds = 6)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(seconds);
        while (!done()) { if (DateTime.UtcNow > end) throw new TimeoutException("Party loopback timed out"); Application.DoEvents(); Thread.Sleep(5); }
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
    private static void Answer(Form dialog, string choice) => All(dialog).OfType<Button>().First(button => button.Text == choice).PerformClick();
    private static void Click(object button) => ((Button)button).PerformClick();
    private static bool Near(Color a, Color b) => Math.Abs(a.R - b.R) <= 12 && Math.Abs(a.G - b.G) <= 12 && Math.Abs(a.B - b.B) <= 12;
    private static void Png(string folder, string name, Color color, int width, int height)
    {
        Directory.CreateDirectory(folder);
        using (var image = new Bitmap(width, height)) { using (var graphics = Graphics.FromImage(image)) graphics.Clear(color); image.Save(Path.Combine(folder, name + ".png"), ImageFormat.Png); }
    }
    private static IEnumerable<string> Texts(ToolStripItemCollection items) => items.Cast<ToolStripItem>().Select(item => item is ToolStripSeparator ? "-" : item.Text);
    private static ToolStripItem Item(ContextMenuStrip menu, string text) => menu.Items.Cast<ToolStripItem>().First(item => item.Text == text);

    // Membres synthétiques au format de Player.parseToPM : id;nom;gfx;c1;c2;c3;accessoires;pdv,pdvMax;niveau;initiative;prospection;côté.
    private const string Friend = "77;Ami fictif;10;-1;-1;-1;;300,400;50;120;100;0";
    private const string Self = "42;Personnage de test;10;-1;16777215;-1;,,,,;150,200;25;150;110;0";

    private static void Run()
    {
        string previous = Environment.CurrentDirectory;
        string folder = Path.Combine(TestPaths.Work, "bot-party"); Directory.CreateDirectory(folder); Environment.CurrentDirectory = folder;
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var logs = new ConcurrentQueue<string>();
        Accounts chief = null;
        try
        {
            LangData.Clear();
            MessagesReception.Init();
            Map.AllBotMaps[900092] = new Map { MapID = 900092, MapWidth = 3, MapHeight = 4, MapData = string.Concat(Enumerable.Repeat("HhGaeaaaaa", 18)) };

            using (var account = new Accounts(new AccountConfig("synthetic-party", "synthetic", "loopback")))
            {
                account.Logger.log_event += (entry, color) => logs.Enqueue(entry.message);
                Task<Socket> accept = listener.AcceptSocketAsync();
                Complete(account.Connexion.ConnectToServer(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port)); Complete(accept);
                using (Socket peer = accept.Result)
                {
                    peer.ReceiveTimeout = 6000;
                    account.Game.character.SetPerso_Data(42, "Personnage de test", 25, 0, 8);
                    Feed(account, "GDM|900092|date|key"); Check(Read(peer) == "GI", "GDM did not request GI");
                    Feed(account, "GM|+0;1;0;42;Personnage de test;1;100^100;0|+5;1;0;77;Ami fictif;1;10^100;0");
                    account.AccountStates = AccountStates.CONNECTED_INACTIVE;
                    PartyActions party = account.Game.Interactions.Party;
                    Groupe group = party.Group;
                    GameSession session = account.Game.Session;

                    // Invitation reçue sans interface : refus PR, jamais d'acceptation automatique ; message INFO_CHAT local.
                    Feed(account, "PIKAmi fictif|Personnage de test");
                    Check(Read(peer) == "PR" && party.PendingInviter == null, "Unanswered PIK was not refused with PR");
                    Check(account.Game.Chat.Messages.Any(message => message.Kind == ChatMessageKind.Info && message.Text.Contains("Ami fictif")), "Invitation was not announced in the chat");

                    // Invitation prise en charge par l'interface : aucun paquet avant la réponse, PA sur acceptation.
                    var invitations = new List<InvitationEventArgs>();
                    EventHandler<InvitationEventArgs> take = (s, e) => { invitations.Add(e); e.Handled = true; };
                    session.PartyInviteReceived += take;
                    Feed(account, "PIKAmi fictif|personnage DE TEST");
                    NoPacket(peer, "Handled PIK still sent a packet");
                    Check(invitations.Count == 1 && invitations[0].InviterName == "Ami fictif" && party.PendingInviter == "Ami fictif",
                        "PIK addressed to the character (case ignored like StarLoco) was not published");
                    Check(Sent(party.AcceptAsync()) && Read(peer) == "PA" && party.PendingInviter == null, "Accept does not send PA");
                    Check(!Sent(party.AcceptAsync()) && !Sent(party.RefuseAsync()), "Answering without a pending invitation was accepted");
                    NoPacket(peer, "Refused answers reached the server");

                    // Entrée dans le groupe : PCK, PL, PM+ (format parseToPM), tri par initiative, champs historiques synchronisés.
                    var changes = 0; Action counted = () => Interlocked.Increment(ref changes);
                    group.Changed += counted;
                    Feed(account, "PCKAmi fictif"); Feed(account, "PL77"); Feed(account, "PM+" + Friend + "|" + Self);
                    IReadOnlyList<PartyMember> members = group.Members;
                    Check(group.IsActive && members.Count == 2 && members[0].Id == 42 && members[1].Id == 77, "PM+ did not build a 2-member party sorted by initiative");
                    PartyMember friend = group.Find(77);
                    Check(friend.Name == "Ami fictif" && friend.Gfx == 10 && friend.Color1 == -1 && friend.Life == 300 && friend.MaxLife == 400 && friend.Level == 50
                        && friend.Initiative == 120 && friend.Prospection == 100 && friend.Side == 0 && friend.Breed == 1 && Math.Abs(friend.LifeRatio - 0.75) < 1e-9,
                        "PM member fields were not read");
                    Check(group.Find(42).Color2 == 16777215 && group.Find(42).Accessories == ",,,,", "Colours or accessories of PM were not read");
                    Check(group.LeaderId == 77 && group.Leader == friend && !group.IsLeader && group.LeaderName == "Ami fictif"
                        && group.TotalLevel == 75 && group.TotalProspection == 210, "Leader or totals are wrong");
                    Check(account.Game.character.InGroupe && account.Game.character.GroupMembers.Count == 2 && account.Game.character.EquipLeader == "Ami fictif",
                        "Legacy party fields used by /p are not synchronised");
                    Check(changes >= 3, "Group.Changed was not raised");

                    // PM~ met à jour un membre connu seulement ; PM+ d'un nouveau puis PM- le retire.
                    Feed(account, "PM~77;Ami fictif;10;-1;-1;-1;;350,400;51;120;100;0|99;Fantome;10;-1;-1;-1;;1,1;1;1;1;0");
                    Check(group.Find(77).Life == 350 && group.Find(77).Level == 51 && group.Count == 2, "PM~ did not update only known members");
                    Feed(account, "PM+78;Troisieme fictif;20;-1;-1;-1;;10,10;5;200;0;0");
                    Check(group.Count == 3 && group.Members[0].Id == 78, "PM+ did not add a third member by initiative");
                    Feed(account, "PM-78"); Check(group.Count == 2 && !group.Contains(78) && !account.Game.character.GroupMembers.ContainsKey(78), "PM- did not remove the member");

                    // Paquets mal formés : journalisés, aucune exception, aucun envoi (sauf PIK incomplet, refusé comme le client).
                    foreach (string packet in new[] { "PM+abc;x|", "PM*1", "PM", "PL", "PLx", "PF+x", "ICa|b", "IC4", "IHx;y|", "PIE", "PCE" })
                        Feed(account, packet);
                    NoPacket(peer, "Malformed party packets triggered a reply");
                    Check(group.Count == 2 && group.LeaderId == 77 && party.Compass == null, "Malformed packets changed the party");
                    Feed(account, "PIK"); Check(Read(peer) == "PR", "Incomplete PIK was not refused like the client");

                    // Suivi : PF+<id> → IC<x>|<y> puis PF+<id> ; PF-<id> → IC| puis PF-.
                    var compass = new List<Point?>(); party.CompassChanged += value => compass.Add(value);
                    Check(!Sent(party.FollowAsync(42)) && !Sent(party.FollowAsync(1234)), "Following oneself or a stranger was accepted");
                    Check(Sent(party.FollowAsync(77)) && Read(peer) == "PF+77", "Follow does not send PF+<id>");
                    Feed(account, "IC3|-5"); Feed(account, "PF+77");
                    Check(group.FollowedId == 77 && party.Compass == new Point(3, -5) && compass.Last() == new Point(3, -5), "IC/PF+ were not applied");
                    Check(Sent(party.StopFollowingAsync(77)) && Read(peer) == "PF-77", "Stop following does not send PF-<id>");
                    Feed(account, "IC|"); Feed(account, "PF-");
                    Check(group.FollowedId == null && party.Compass == null && compass.Last() == null, "IC|/PF- did not clear the follow");
                    Feed(account, "PFE"); Check(party.LastMessage.Length > 0 && group.FollowedId == null, "PFE was not reported");

                    // Actions de chef refusées localement tant que le personnage n'est pas chef.
                    Check(!Sent(party.KickAsync(77)) && !Sent(party.FollowAllAsync(77)) && !Sent(party.StopFollowAllAsync(77)), "Leader actions were accepted for a member");
                    NoPacket(peer, "Refused leader actions reached the server");

                    // Localisation : PW → IH<x;y;carte;2;id;nom>|…
                    var located = new List<IReadOnlyList<PartyLocation>>(); party.LocationsReceived += list => located.Add(list);
                    Check(Sent(party.LocateAsync()) && Read(peer) == "PW", "Locate does not send PW");
                    Feed(account, "IH1;2;1000;2;77;Ami fictif|-3;4;1001;2;42;Personnage de test");
                    Check(located.Count == 1 && located[0].Count == 2 && located[0][0].X == 1 && located[0][0].Y == 2 && located[0][0].MapId == 1000
                        && located[0][0].IsPartyMember && located[0][0].PlayerId == 77 && located[0][1].PlayerName == "Personnage de test",
                        "IH positions were not read");

                    // Invitations envoyées : contrôles locaux, PI<nom>, PIK<soi>|<invité>, PIE, annulation PR.
                    Check(!Sent(party.InviteAsync(" ")) && !Sent(party.InviteAsync("Ami fictif")) && !Sent(party.InviteAsync("Personnage de test"))
                        && !Sent(party.InviteAsync("a|b")), "Invalid invitations were accepted");
                    NoPacket(peer, "Invalid invitations reached the server");
                    Check(Sent(party.InviteAsync("Inconnu fictif")) && Read(peer) == "PIInconnu fictif", "Invite does not send PI<name>");
                    Feed(account, "PIEnInconnu fictif");
                    Check(party.OutgoingInvitee == null && party.LastMessage.Contains("Inconnu fictif"), "PIEn was not reported");
                    Feed(account, "PIEa"); Feed(account, "PIEf"); Check(party.LastMessage.Length > 0, "PIEa/PIEf were not reported");
                    Check(Sent(party.InviteAsync("Autre fictif")) && Read(peer) == "PIAutre fictif", "Second invitation not sent");
                    Feed(account, "PIKPersonnage de test|Autre fictif");
                    Check(party.OutgoingInvitee == "Autre fictif" && invitations.Count == 1, "Outgoing PIK was not recorded or was taken as a received invitation");
                    Check(Sent(party.CancelInvitationAsync()) && Read(peer) == "PR" && party.OutgoingInvitee == null, "Cancel does not send PR");

                    // Chef : PL<soi> ; PV<id>, PG±<id> (soi compris pour PG).
                    Feed(account, "PL42");
                    Check(group.IsLeader && group.Leader.Id == 42, "PL<self> did not make the character leader");
                    Check(Sent(party.FollowAllAsync(77)) && Read(peer) == "PG+77" && party.FollowAllTargetId == 77, "Follow all does not send PG+<id>");
                    Check(Sent(party.StopFollowAllAsync(77)) && Read(peer) == "PG-77" && party.FollowAllTargetId == null, "Stop follow all does not send PG-<id>");
                    Check(Sent(party.FollowAllAsync(42)) && Read(peer) == "PG+42", "Follow me all does not send PG+<self>");
                    Check(Sent(party.KickAsync(77)) && Read(peer) == "PV77", "Kick does not send PV<id>");
                    Check(!Sent(party.KickAsync(42)), "Kicking oneself was accepted");

                    // Ignorer : PR et invitant écarté pour la session.
                    Feed(account, "PIKImportun fictif|Personnage de test");
                    Check(invitations.Count == 2 && party.PendingInviter == "Importun fictif", "Second invitation not published");
                    Check(Sent(party.IgnoreAsync()) && Read(peer) == "PR" && party.IsIgnored("Importun fictif"), "Ignore does not send PR");
                    Feed(account, "PIKImportun fictif|Personnage de test");
                    Check(Read(peer) == "PR" && invitations.Count == 2, "Ignored inviter was asked again");
                    // PR du serveur : l'invitation en attente est close.
                    Feed(account, "PIKAutre fictif|Personnage de test"); Check(party.PendingInviter == "Autre fictif", "Third invitation not pending");
                    Feed(account, "PR"); Check(party.PendingInviter == null, "PR from the server did not close the invitation");
                    NoPacket(peer, "Server PR triggered a reply");

                    // Quitter : PV → PV puis IH vide ; plus de groupe, positions de groupe effacées.
                    Check(Sent(party.LeaveAsync()) && Read(peer) == "PV", "Leave does not send PV");
                    Feed(account, "PV"); Feed(account, "IH");
                    Check(!group.IsActive && group.Count == 0 && group.LeaderId == null && !account.Game.character.InGroupe && account.Game.character.GroupMembers.Count == 0
                        && party.Locations.Count == 0 && party.FollowAllTargetId == null, "PV did not clear the party");
                    Check(!Sent(party.LeaveAsync()) && !Sent(party.LocateAsync()), "Party actions accepted outside a party");
                    // Exclusion par le chef : PV<id du chef>.
                    Feed(account, "PCKAmi fictif"); Feed(account, "PL77"); Feed(account, "PM+" + Friend + "|" + Self);
                    Feed(account, "PV77"); Check(!group.IsActive && party.LastMessage.Contains("Ami fictif"), "Kick by the leader was not reported");
                    session.PartyInviteReceived -= take;
                    group.Changed -= counted;

                    // Équipe de comptes du bot : seul le personnage du chef est accepté sans question.
                    chief = new Accounts(new AccountConfig("synthetic-party-chief", "synthetic", "loopback"));
                    chief.Game.character.SetPerso_Data(50, "Chef fictif", 30, 0, 1);
                    using (var team = new Regroupement(chief))
                    {
                        Check(team.Add(account) && account.Regroupement == team && account.HasGroup && !account.IsGroupLeader && chief.IsGroupLeader, "Team membership is wrong");
                        Check(team.ShouldAutoAccept(account, "chef FICTIF") && !team.ShouldAutoAccept(account, "Ami fictif") && !team.ShouldAutoAccept(chief, "Chef fictif"),
                            "Team auto-accept rule is wrong");
                        Feed(account, "PIKChef fictif|Personnage de test"); Check(Read(peer) == "PA", "Team member did not accept its leader");
                        Feed(account, "PIKAmi fictif|Personnage de test"); Check(Read(peer) == "PR", "Team member accepted a stranger");
                    }
                    Check(account.Regroupement == null && chief.Regroupement == null, "Disposed team stayed attached");
                    Feed(account, "PIKChef fictif|Personnage de test"); Check(Read(peer) == "PR", "Former team leader is still accepted");

                    // Volet Groupe : textes du client et images synthétiques lus dans un dossier temporaire.
                    string lang = Path.Combine(folder, "BotLang"); Directory.CreateDirectory(lang);
                    File.WriteAllText(Path.Combine(lang, "lang.xml"), "<?xml version='1.0' encoding='utf-8'?>\n<BotLang famille=\"lang\" langue=\"fr\" version=\"1\" source=\"lang_fr_1.swf\">\n"
                        + "<texte cle=\"PARTY\" valeur=\"Groupe fictif\" />\n<texte cle=\"A_INVITE_YOU_IN_PARTY\" valeur=\"&lt;b&gt;%1&lt;/b&gt; propose son groupe (texte de test).\" />\n"
                        + "<texte cle=\"FOLLOW\" valeur=\"Suivre (test)\" />\n<texte cle=\"LEAVE_PARTY\" valeur=\"Quitter (test)\" />\n</BotLang>\n", new UTF8Encoding(false));
                    Check(LangData.Load(lang) == 1, "Synthetic texts were not loaded");
                    string images = Path.Combine(folder, "images");
                    Color miniColor = Color.FromArgb(255, 30, 160, 200);
                    Png(Path.Combine(images, "ressources", "Bot", "Artworks", "Mini"), "10", miniColor, 24, 30);
                    Png(Path.Combine(images, "ressources", "Bot", "Party"), "chef", Color.FromArgb(255, 250, 200, 0), 17, 14);
                    Png(Path.Combine(images, "ressources", "Bot", "Party"), "groupe", Color.FromArgb(255, 120, 90, 40), 36, 29);
                    File.WriteAllText(Path.Combine(images, "ressources", "Bot", "Artworks", "Mini", "11.png"), "pas une image");
                    PartyArtworks.Root = images;
                    Check(PartyArtworks.PathFor(PartyArtworks.MiniFamily, "10") != null && PartyArtworks.PathFor(PartyArtworks.MiniFamily, "12") == null
                        && PartyArtworks.PathFor(PartyArtworks.MiniFamily, "..\\x") == null, "Artwork paths are wrong");
                    Check(Result(PartyArtworks.MiniAsync(10)).Width == 24 && Result(PartyArtworks.MiniAsync(11)) == null && Result(PartyArtworks.MiniAsync(12)) == null
                        && ReferenceEquals(PartyArtworks.Mini(10), PartyArtworks.Mini(10)), "Mini artworks are not loaded once, or a broken PNG threw");
                    PumpUntil(() => PartyArtworks.Ui(PartyArtworks.Leader) != null && PartyArtworks.Ui(PartyArtworks.Party) != null);
                    Check(PartyArtworks.Ui(PartyArtworks.Follow) == null || PartyArtworks.LoadAsync(PartyArtworks.UiFamily, PartyArtworks.Follow).IsCompleted, "Missing UI element was not tolerated");

                    using (var form = new GameClientFullform(account))
                    {
                        form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); Application.DoEvents();
                        var drawer = form.Panels; var panel = drawer.Get<PartyPanel>();
                        Check(panel != null && drawer.Current == null && panel.Title == "Groupe fictif" && panel.Icon != null && panel.Icon.Width == 36,
                            "Party panel is not registered with its client title and icon");

                        // Invitation reçue sur un fil réseau, tiroir jamais affiché : boîte Oui / Non / Ignorer, aucun paquet avant le choix.
                        FeedFromNetwork(account, "PIKAmi fictif|Personnage de test");
                        PumpUntil(() => panel.InvitationDialog != null);
                        NoPacket(peer, "The party panel let the invitation be refused automatically");
                        Form box = panel.InvitationDialog;
                        Check(BotDialogs.OpenDialogs.Contains(box) && box.Text == "Groupe fictif"
                            && All(box).Any(control => control.Name == "dialog-message" && control.Text == "Ami fictif propose son groupe (texte de test).")
                            && All(box).OfType<Button>().Select(button => button.Text).OrderBy(text => text).SequenceEqual(new[] { "Ignorer", "Non", "Oui" }),
                            "Invitation dialog is missing or differs from CAUTION_YESNOIGNORE");
                        Answer(box, "Non"); Check(Read(peer) == "PR", "Non does not send PR");
                        PumpUntil(() => panel.InvitationDialog == null && party.PendingInviter == null);
                        // La boîte disparaît quand l'invitant annule (PR du serveur) ; rien n'est envoyé.
                        FeedFromNetwork(account, "PIKAmi fictif|Personnage de test"); PumpUntil(() => panel.InvitationDialog != null);
                        box = panel.InvitationDialog;
                        FeedFromNetwork(account, "PR"); PumpUntil(() => panel.InvitationDialog == null && !BotDialogs.OpenDialogs.Contains(box));
                        NoPacket(peer, "Closing the dialog of a cancelled invitation sent a packet");
                        FeedFromNetwork(account, "PIKAmi fictif|Personnage de test"); PumpUntil(() => panel.InvitationDialog != null);
                        Answer(panel.InvitationDialog, "Oui"); Check(Read(peer) == "PA", "Oui does not send PA");
                        Check(!drawer.IsOpen(panel), "Party panel opened before PCK");

                        // Entrée dans le groupe depuis le réseau : le volet s'ouvre, une case par membre.
                        FeedFromNetwork(account, "PCKAmi fictif"); FeedFromNetwork(account, "PL77"); FeedFromNetwork(account, "PM+" + Friend + "|" + Self);
                        PumpUntil(() => drawer.Current == panel && panel.MemberList.Members.Count == 2);
                        Check(drawer.Visible && ((Label)Get(panel, "heading")).Text.Contains("Ami fictif") && ((Label)Get(panel, "heading")).Text.Contains("2/8")
                            && ((Label)Get(panel, "summary")).Text.Contains("75") && ((Label)Get(panel, "summary")).Text.Contains("210"),
                            "Party panel heading or totals are wrong");
                        PartyMemberList list = panel.MemberList;
                        Check(list.LeaderId == 77 && list.Members[0].Id == 42 && list.Height == 2 * PartyMemberList.RowHeight + 1, "Member list is not filled");
                        using (var image = new Bitmap(list.Width, list.Height))
                        {
                            list.DrawToBitmap(image, new Rectangle(Point.Empty, list.Size));
                            Rectangle self = list.TileBounds(0), friendTile = list.TileBounds(1);
                            Check(Near(image.GetPixel(self.X + 12, self.Bottom - 6), miniColor), "Mini artwork is not drawn in the member tile");
                            Check(Near(image.GetPixel(friendTile.Right - 4, friendTile.Bottom - 4), PartyMemberList.HealthColor)
                                && !Near(image.GetPixel(friendTile.Right - 4, friendTile.Y + 3), PartyMemberList.HealthColor), "Life gauge does not follow 300/400");
                            Check(Near(image.GetPixel(friendTile.X + friendTile.Width / 2, friendTile.Y - 1), Color.FromArgb(250, 200, 0)), "Leader crown is not drawn");
                        }

                        // Menu d'un autre membre (non chef) : Groupe, Localiser, Suivre ; Suivre envoie PF+<id>.
                        ContextMenuStrip menu = panel.ShowMemberMenu(77);
                        Check(menu != null && string.Join("|", Texts(menu.Items)).StartsWith("Ami fictif|Groupe fictif|") && Texts(menu.Items).Contains("Suivre (test)")
                            && !Texts(menu.Items).Any(text => text.Contains("Exclure")), "Member menu differs: " + string.Join("|", Texts(menu.Items)));
                        Item(menu, "Suivre (test)").PerformClick(); Check(Read(peer) == "PF+77", "Suivre does not send PF+<id>");
                        menu.Close(); Application.DoEvents();
                        FeedFromNetwork(account, "IC7|-2"); FeedFromNetwork(account, "PF+77");
                        PumpUntil(() => ((Label)Get(panel, "compassLabel")).Text.Contains("[7,-2]") && list.FollowedId == 77);
                        menu = panel.ShowMemberMenu(77);
                        Check(Texts(menu.Items).Any(text => text == "Arrêter de suivre") && !Texts(menu.Items).Contains("Suivre (test)"), "Followed member menu does not offer to stop");
                        menu.Close(); Application.DoEvents();
                        // Menu du personnage : Quitter (PV).
                        menu = panel.ShowMemberMenu(42);
                        Check(Texts(menu.Items).Contains("Quitter (test)") && !Texts(menu.Items).Any(text => text.Contains("tous")), "Own menu differs: " + string.Join("|", Texts(menu.Items)));
                        menu.Close(); Application.DoEvents();
                        Check(panel.ShowMemberMenu(1234) == null, "A menu was built for a stranger");
                        // Chef : entrées « tous » et exclusion.
                        FeedFromNetwork(account, "PL42"); PumpUntil(() => list.LeaderId == 42);
                        menu = panel.ShowMemberMenu(77);
                        Check(Texts(menu.Items).Any(text => text == "Exclure") && Texts(menu.Items).Any(text => text == "Le suivre (tous)"), "Leader menu misses kick or follow all");
                        Item(menu, "Exclure").PerformClick(); Check(Read(peer) == "PV77", "Exclure does not send PV<id>");
                        menu.Close(); Application.DoEvents();

                        // Invitation par nom, invitation envoyée et annulation.
                        ((TextBox)Get(panel, "inviteName")).Text = "Nouveau fictif";
                        Click(Get(panel, "inviteButton")); Check(Read(peer) == "PINouveau fictif", "Invite button does not send PI<name>");
                        PumpUntil(() => ((TextBox)Get(panel, "inviteName")).Text.Length == 0);
                        FeedFromNetwork(account, "PIKPersonnage de test|Nouveau fictif");
                        PumpUntil(() => ((Panel)Get(panel, "invitationBar")).Visible);
                        Check(((Label)Get(panel, "invitation")).Text.Contains("Nouveau fictif"), "Outgoing invitation is not shown");
                        Click(Get(panel, "cancelInvitation")); Check(Read(peer) == "PR", "Cancel button does not send PR");
                        PumpUntil(() => !((Panel)Get(panel, "invitationBar")).Visible);

                        // Localisation : bouton PW, positions IH dans la liste.
                        Click(Get(panel, "locate")); Check(Read(peer) == "PW", "Locate button does not send PW");
                        FeedFromNetwork(account, "IH1;2;1000;2;77;Ami fictif|-3;4;1001;2;42;Personnage de test");
                        var places = (ListView)Get(panel, "locations");
                        PumpUntil(() => places.Items.Count == 2);
                        Check(places.Items[0].Text == "Ami fictif" && places.Items[0].SubItems[1].Text == "[1,2]" && places.Items[0].SubItems[2].Text == "1000", "IH positions are not listed");

                        // Menu du personnage sur la carte : « Groupe… » rouvre le volet ; « Inviter » reste au menu des joueurs (une seule entrée).
                        var provider = ActorMenuRegistry.Default.Providers.OfType<PartyMenuProvider>().Single();
                        drawer.CloseAll(); Application.DoEvents(); Check(!drawer.IsOpen(panel), "CloseAll kept the party panel");
                        MenuEntry show = provider.Entries(account.Game.character, account.Game).Single();
                        Check(show.Texte == "Groupe fictif…" && Result(show.Action(new ActorMenuContext(account.Game.character, null, -1, account, null, drawer, Keys.None))) == null
                            && drawer.Current == panel, "Own menu entry does not reopen the party panel");
                        var player = account.Game.Map.Entites[77];
                        Check(!provider.Entries(player, account.Game).Any()
                            && ActorMenuRegistry.Default.EntriesFor(player, account.Game).Count(entry => entry.Texte == MapActionTexts.InviteToParty) == 1,
                            "Party invitation is missing from or duplicated in the player menu");

                        // PV : le volet se ferme comme le bandeau du client ; le menu du personnage n'a plus d'entrée de groupe.
                        FeedFromNetwork(account, "PV"); PumpUntil(() => !drawer.IsOpen(panel));
                        Check(((Label)Get(panel, "heading")).Text == "Aucun groupe" && list.Members.Count == 0, "Party panel was not cleared by PV");
                        Check(!provider.Entries(account.Game.character, account.Game).Any(), "Own menu still offers the party panel");
                        form.Close();
                    }
                    Check(BotDialogs.OpenDialogs.Count == 0, "A dialog survived the game window");
                    Check(!logs.Any(entry => entry.Contains("abonné du groupe a échoué")), "A party subscriber failed: " + string.Join(" / ", logs.Where(entry => entry.Contains("abonné"))));
                }
            }
        }
        finally
        {
            chief?.Dispose();
            LangData.Clear();
            PartyArtworks.Root = null;
            listener.Stop(); Environment.CurrentDirectory = previous;
        }
    }
}
