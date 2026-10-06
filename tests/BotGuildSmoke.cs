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
using Tool_BotProtocol.Game.Chat;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Exchanges;
using Tool_BotProtocol.Game.Guildes;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Session;

// Guilde selon dofus.aks.Guild (client 1.34) et GameClient / SocketManager de StarLoco, sur un serveur fictif local (lot F3) :
// gS → modèle et HasGuild du personnage ; gJR<nom> et ses refus locaux ; gJr<id>|<nom>|<guilde> refusé par gJE<id> sans abonné,
// gJK<id> / gJE<id> avec l'identifiant reçu, invitant ignoré, gJC, gJKj ; gIG, gIM± (tri par rang de ranks_fr), gP et ses refus,
// gK / gKK / gKE, gIB et gB/gb, gITM/gITp/gITP, gH, gF, gT*, gA*, gHE*, gIF, gIH, gU*, création gn / gC / gCE / gCK / gV, paquets
// mal formés sans exception ; puis le volet Guilde (bouton du bandeau refusé sans guilde, gIG puis gIM à l'ouverture, en-tête,
// emblème composé hors du fil de l'interface, lignes au pixel témoin, fiche d'un membre → gP, exclusion → gK, boîte Oui / Non /
// Ignorer d'une invitation → gJK<id> / gJE<id>, onglets gIB / gIT / gIF / gIH, gITV à la fermeture, gUT) et les menus d'un
// joueur (gJR<nom>) et d'un percepteur (DC, ER8|<id> puis la fenêtre de collecte ECK8 / EL / EMO- / EMG- / EV, gF<id>, GA909,
// jamais gTJ / gTV).
internal static class BotGuildSmoke
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private const int Rights = 766; // boost 2 + droits 4 + inviter 8 + bannir 16 + xp 32 + rangs 64 + percepteur 128 + collecte 512
    private const int BackColor = 0x20B040, UpColor = 0xD02020;

    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            string path = Path.Combine(TestPaths.ApplicationBin, new AssemblyName(e.Name).Name + ".dll");
            if (!File.Exists(path)) path = Path.ChangeExtension(path, "exe");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        try { Application.EnableVisualStyles(); Run(); Console.WriteLine("OK: gS, gJR/gJr/gJE/gJK/gJC, gIG/gIM/gIB/gIT/gIF/gIH, gP, gK/gKK, gB/gb, gH/gF, gT/gA, gn/gC/gV, volet Guilde, boîte d'invitation, menus joueur et percepteur, collecte ER8"); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static object Get(object target, string field) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static IEnumerable<Control> All(Control value) { yield return value; foreach (Control child in value.Controls) foreach (var nested in All(child)) yield return nested; }
    private static Control Find(Control root, string name) => All(root).First(control => control.Name == name);
    private static void PumpUntil(Func<bool> done, int seconds = 6)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(seconds);
        while (!done()) { if (DateTime.UtcNow > end) throw new TimeoutException("Guild loopback timed out"); Application.DoEvents(); Thread.Sleep(5); }
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
    private static void Click(Control button) => ((Button)button).PerformClick();
    private static Form Dialog(int count = 1, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
    {
        try { PumpUntil(() => BotDialogs.OpenDialogs.Count == count); }
        catch (TimeoutException) { throw new Exception("No dialog opened (line " + line + ", open: " + BotDialogs.OpenDialogs.Count + ")"); }
        return BotDialogs.OpenDialogs[count - 1];
    }
    private static void Answer(Form dialog, string text) { All(dialog).OfType<Button>().Single(button => button.Text == text).PerformClick(); PumpUntil(() => !BotDialogs.OpenDialogs.Contains(dialog)); }
    private static string Message(Form dialog) => All(dialog).First(control => control.Name == "dialog-message").Text;
    private static bool Near(Color a, Color b) => Math.Abs(a.R - b.R) <= 12 && Math.Abs(a.G - b.G) <= 12 && Math.Abs(a.B - b.B) <= 12;
    private static void Png(string folder, string name, Color color, int width, int height)
    {
        Directory.CreateDirectory(folder);
        using (var image = new Bitmap(width, height)) { using (var graphics = Graphics.FromImage(image)) graphics.Clear(color); image.Save(Path.Combine(folder, name + ".png"), ImageFormat.Png); }
    }
    private static bool Chat(Accounts account, ChatMessageKind kind, string text) => account.Game.Chat.Messages.Any(message => message.Kind == kind && message.Text.Contains(text));
    private static string Base36(long value)
    {
        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        if (value == 0) return "0";
        var text = new StringBuilder();
        while (value > 0) { text.Insert(0, digits[(int)(value % 36)]); value /= 36; }
        return text.ToString();
    }
    private static void Lang(string folder, string file, string family, string body) =>
        File.WriteAllText(Path.Combine(folder, file), "<?xml version='1.0' encoding='utf-8'?>\n<BotLang famille=\"" + family + "\" langue=\"fr\" version=\"1\" source=\"" + family + "_fr_1.swf\">\n" + body + "\n</BotLang>\n", new UTF8Encoding(false));

    // ClientAssets est interne à l'application : sa racine (dossier temporaire du test) et son cache passent par la réflexion.
    private static Type Assets => typeof(LoginForm).Assembly.GetType("Outil_Azur_complet.Bot.ClientAssets");
    private static void AssetsRoot(string value) => Assets.GetProperty("Root", Any).SetValue(null, value, null);
    private static bool Cached(string family, string name)
    {
        var args = new object[] { family, name, null };
        return (bool)Assets.GetMethod("TryCached", Any).Invoke(null, args) && args[2] != null;
    }

    // gS<nom>|<fond>|<couleur>|<motif>|<couleur>|<droits>, base 36 (SocketManager.GAME_SEND_gS_PACKET).
    private static string Stats(int rights) => "gSGuilde fictive|3|" + Base36(BackColor) + "|5|" + Base36(UpColor) + "|" + Base36(rights);
    private const string General = "gIG1|12|1000|1500|-1";
    // gIM+<id;nom;niveau;gfx;rang;xp donnée;%;droits;état;alignement;heures>|… (Guild.parseMembersToGM) ; une ligne illisible.
    private static readonly string Members = "gIM+42;Personnage de test;25;8;0;500;10;" + Rights + ";1;0;0|43;Meneur fictif;80;9;1;9000;90;1;2;1;0|44;Absent fictif;10;10;3;0;0;0;0;-1;48|zz;cassé";
    private const string Boosts = "gIB10|2|300|3|500|2|1|250|12|1000|462;3|464;1";
    private const string Collectors = "gITM+1z;a,b,Poseur fictif,1700000000,Récolteur fictif,1700003600,1700007200;5d,2,-4;0;3600;7200;5|2a;c,d;5e,3,-5;1;0;7200;5";
    private const string Paddocks = "gIF3|900092;5;3;20,Dragodinde fictive,Propriétaire fictif|900093;0;0|x";
    private const string Houses = "gIH+7;Propriétaire fictif;2,-4;1,2;3|8;;0,0;;";
    private const string LeaveSelf = "gKKPersonnage de test|Personnage de test";

    private static void Run()
    {
        string previous = Environment.CurrentDirectory;
        string folder = Path.Combine(TestPaths.Work, "bot-guild"); Directory.CreateDirectory(folder); Environment.CurrentDirectory = folder;
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var logs = new ConcurrentQueue<string>();
        try
        {
            LangData.Clear();
            MessagesReception.Init();
            Map.AllBotMaps[900092] = new Map { MapID = 900092, MapWidth = 3, MapHeight = 4, MapData = string.Concat(Enumerable.Repeat("HhGaeaaaaa", 18)) };

            // Textes du client et rangs synthétiques (ranks_fr : n = nom, o = ordre d'affichage).
            string lang = Path.Combine(folder, "BotLang"); Directory.CreateDirectory(lang);
            Lang(lang, "lang.xml", "lang",
                "<texte cle=\"GUILD\" valeur=\"Guilde (test)\" />\n<texte cle=\"INVITE_IN_GUILD\" valeur=\"Inviter dans la guilde (test)\" />\n"
                + "<texte cle=\"COLLECT_TAX\" valeur=\"Collecter (test)\" />\n<texte cle=\"ATTACK\" valeur=\"Attaquer (test)\" />\n<texte cle=\"SPEAK\" valeur=\"Parler (test)\" />\n"
                + "<texte cle=\"REMOVE\" valeur=\"Retirer (test)\" />\n<texte cle=\"A_INVIT_YOU_IN_GUILD\" valeur=\"%1 t'invite dans %2 (test)\" />\n"
                + "<texte cle=\"YOU_INVIT_B_IN_GUILD\" valeur=\"Tu invites %1 (test)\" />\n<texte cle=\"GUILD_CREATE_ALLREADY_USE_NAME\" valeur=\"Nom déjà pris (test)\" />\n");
            Lang(lang, "ranks.xml", "ranks", "<entree table=\"R\" id=\"0\" n=\"A l'essai\" o=\"12\" />\n<entree table=\"R\" id=\"1\" n=\"Meneur\" o=\"0\" />\n<entree table=\"R\" id=\"3\" n=\"Trésorier\" o=\"3\" />");
            Check(LangData.Load(lang) == 2, "Synthetic texts were not loaded");
            Check(GuildTexts.RankName(3) == "Trésorier" && GuildTexts.RankOrder(1) == 0 && GuildTexts.RankName(9) == "Rang 9" && GuildTexts.Ranks().Count == 3
                && GuildTexts.Ranks()[0].Key == 1, "ranks_fr is not read");
            Check(GuildRights.CanDo(1, GuildRight.Collect) && GuildRights.CanDo(Rights, GuildRight.Ban) && !GuildRights.CanDo(Rights, GuildRight.UsePaddock) && !GuildRights.IsBoss(Rights),
                "Rights bitmask is wrong");

            using (var account = new Accounts(new AccountConfig("synthetic-guild", "synthetic", "loopback")))
            {
                account.Logger.log_event += (entry, color) => logs.Enqueue(entry.message);
                GuildActions guild = account.Game.Interactions.Guild;
                Check(guild != null && !guild.HasGuild && !account.Game.character.HasGuild, "Interactions.Guild is missing or starts with a guild");
                Check(!Sent(guild.OpenAsync()) && !Sent(guild.InviteAsync("Ami fictif")) && !Sent(guild.CollectAsync(70)), "Guild actions were accepted without a guild");

                Task<Socket> accept = listener.AcceptSocketAsync();
                Complete(account.Connexion.ConnectToServer(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port)); Complete(accept);
                using (Socket peer = accept.Result)
                {
                    peer.ReceiveTimeout = 6000;
                    account.Game.character.SetPerso_Data(42, "Personnage de test", 25, 0, 8);
                    Feed(account, "GDM|900092|date|key"); Check(Read(peer) == "GI", "GDM did not request GI");
                    // Soi, un joueur sans guilde et un percepteur de « Guilde fictive » (format GM du lot M4).
                    Feed(account, "GM|+0;1;0;42;Personnage de test;1;100^100;0|+5;1;0;77;Ami fictif;1;10^100;0|+12;1;0;70;a,b;-6;6000^100;3;Guilde fictive;1a,2b,3c,4d");
                    account.AccountStates = AccountStates.CONNECTED_INACTIVE;
                    int changes = 0; Action counted = () => Interlocked.Increment(ref changes);
                    var boxes = new List<string>(); Action<string, string> box = (title, text) => boxes.Add(title + "|" + text);
                    var tabs = new List<GuildTab>(); Action<GuildTab> tab = value => tabs.Add(value);
                    var alerts = new List<string>(); Action<string> alert = text => alerts.Add(text);
                    int closed = 0; Action closing = () => Interlocked.Increment(ref closed);
                    guild.Changed += counted; guild.ErrorBox += box; guild.OpenTabRequested += tab; guild.CollectorAlert += alert; guild.InvitationClosed += closing;

                    // gS : nom, emblème (base 36) et droits ; HasGuild du personnage pour le bouton du bandeau.
                    Feed(account, Stats(Rights));
                    Guild model = guild.Guild;
                    Check(guild.HasGuild && account.Game.character.HasGuild && model.Name == "Guilde fictive" && model.OwnRights == Rights && !model.IsBoss
                        && model.EmblemBackId == 3 && model.EmblemBackColor == BackColor && model.EmblemUpId == 5 && model.EmblemUpColor == UpColor
                        && model.Emblem == "3," + Base36(BackColor) + ",5," + Base36(UpColor) && model.CanDo(GuildRight.Invite) && !model.CanDo(GuildRight.UsePaddock),
                        "gS is not read like Guild.onStats");

                    // Invitation envoyée : gJR<nom> (menu du joueur) ; refus locaux sans paquet ; réponses gJR / gJE / gJK du serveur.
                    Check(Sent(guild.InviteAsync(" Ami fictif ")) && Read(peer) == "gJRAmi fictif", "Invite does not send gJR<name>");
                    Check(!Sent(guild.InviteAsync("")) && !Sent(guild.InviteAsync("a|b")) && !Sent(guild.InviteAsync("Personnage de test")), "Invalid invitations were accepted");
                    NoPacket(peer, "A refused invitation reached the server");
                    Feed(account, "gJRAmi fictif");
                    Check(guild.OutgoingInvitee == "Ami fictif" && Chat(account, ChatMessageKind.Info, "Tu invites Ami fictif (test)"), "gJR<name> is not announced");
                    Feed(account, "gJEa"); Check(Chat(account, ChatMessageKind.Error, "déjà dans une guilde"), "gJEa is not reported");
                    Feed(account, "gJEu"); Feed(account, "gJEo"); Feed(account, "gJEd");
                    Check(Chat(account, ChatMessageKind.Error, "inconnu ou non connecté") && Chat(account, ChatMessageKind.Error, "occupé") && Chat(account, ChatMessageKind.Error, "pas le droit d'inviter"),
                        "gJEu/o/d are not reported");
                    Feed(account, "gJErAmi fictif"); Check(guild.OutgoingInvitee == null && Chat(account, ChatMessageKind.Error, "Ami fictif refuse"), "gJEr<name> is not reported");
                    Feed(account, "gJRAmi fictif"); int wasClosed = closed; Feed(account, "gJEc");
                    Check(guild.OutgoingInvitee == null && closed == wasClosed + 1, "gJEc did not close the outgoing invitation");
                    Feed(account, "gJKaAmi fictif"); Check(Chat(account, ChatMessageKind.Info, "Ami fictif a rejoint"), "gJKa<name> is not announced");

                    // Invitation reçue sans abonné : refusée par gJE<id> (l'identifiant que StarLoco compare), jamais acceptée.
                    Feed(account, "gJr43|Invitant fictif|Autre guilde");
                    Check(Read(peer) == "gJE43" && guild.PendingInviterId == null && Chat(account, ChatMessageKind.Info, "Invitant fictif t'invite"), "Unhandled gJr was not refused with gJE<id>");
                    // Avec un abonné : la réponse attend le choix ; Oui → gJK<id>, Non → gJE<id>, Ignorer → gJE<id> puis refus automatique.
                    GameSession session = account.Game.Session;
                    var invitations = new List<InvitationEventArgs>();
                    EventHandler<InvitationEventArgs> capture = (s, e) => { invitations.Add(e); e.Handled = true; };
                    session.GuildInviteReceived += capture;
                    Feed(account, "gJr44|Invitant fictif|Autre guilde"); NoPacket(peer, "A handled invitation was answered automatically");
                    Check(invitations.Count == 1 && invitations[0].InviterId == "44" && invitations[0].GuildName == "Autre guilde" && guild.PendingInviterId == "44"
                        && guild.PendingInviterName == "Invitant fictif" && guild.PendingGuildName == "Autre guilde", "GuildInviteReceived or the pending invitation is wrong");
                    Check(Sent(guild.AcceptAsync()) && Read(peer) == "gJK44" && guild.PendingInviterId == null && !Sent(guild.AcceptAsync()), "Accept does not send gJK<id> once");
                    Feed(account, "gJr45|Invitant fictif|Autre guilde"); Check(Sent(guild.RefuseAsync()) && Read(peer) == "gJE45" && guild.PendingInviterId == null, "Refuse does not send gJE<id>");
                    Feed(account, "gJr46|Ignoré fictif|Autre guilde");
                    Check(Sent(guild.IgnoreAsync()) && Read(peer) == "gJE46" && guild.IsIgnored("Ignoré fictif") && guild.LastMessage.Contains("Ignoré fictif est maintenant ignoré"), "Ignore does not refuse and remember the inviter");
                    Feed(account, "gJr47|Ignoré fictif|Autre guilde"); Check(Read(peer) == "gJE47" && invitations.Count == 3, "An ignored inviter was asked again");
                    Feed(account, "gJrabc|Invitant fictif|Autre guilde"); Feed(account, "gJr"); NoPacket(peer, "gJE was sent without a numeric inviter id");
                    Feed(account, "gJr48|Invitant fictif|Autre guilde"); wasClosed = closed; Feed(account, "gJC");
                    Check(guild.PendingInviterId == null && closed == wasClosed + 1 && !Sent(guild.RefuseAsync()), "gJC did not cancel the pending invitation");
                    NoPacket(peer, "gJC was answered");
                    // gJKj après le gS de la nouvelle guilde (GameClient.invitationGuild).
                    Feed(account, "gSAutre guilde|1|0|1|0|0"); Feed(account, "gJKj");
                    Check(model.Name == "Autre guilde" && account.Game.character.HasGuild && Chat(account, ChatMessageKind.Info, "intégrer la guilde Autre guilde"), "gJKj is not announced with the guild of gS");
                    session.GuildInviteReceived -= capture;
                    Feed(account, Stats(Rights));

                    // gIG : niveau, xp, validité.
                    Feed(account, General);
                    Check(model.GeneralReceived && model.Level == 12 && model.XpMin == 1000 && model.Xp == 1500 && model.XpMax == -1 && model.IsValid, "gIG is not read like onInfosGeneral");
                    Feed(account, "gIG0|3|0|10|500"); Check(!model.IsValid && model.XpMax == 500, "Second gIG was not applied");

                    // gIM+ : membres triés par ordre de rang puis nom, ligne illisible ignorée ; gIM- retire par identifiant.
                    Feed(account, Members);
                    Check(model.MembersReceived && model.Members.Count == 3 && model.Members.Select(member => member.Name).SequenceEqual(new[] { "Meneur fictif", "Absent fictif", "Personnage de test" }),
                        "gIM+ rows are not sorted by ranks_fr order: " + string.Join("|", model.Members.Select(member => member.Name)));
                    GuildMember boss = model.FindMember(43), away = model.FindMember(44), self = model.FindMember("Personnage de test");
                    Check(boss != null && boss.IsBoss && boss.IsOnline && boss.IsInFight && boss.Level == 80 && boss.Gfx == 9 && boss.XpPercent == 90 && boss.Alignment == 1 && boss.RankName == "Meneur",
                        "Boss row is wrong");
                    Check(away != null && !away.IsOnline && away.HoursSinceConnection == 48 && away.Alignment == -1 && away.Rank == 3 && away.RankName == "Trésorier", "Offline row is wrong");
                    Check(self != null && self.Id == 42 && self.Rights == Rights && self.XpGiven == 500 && self.XpPercent == 10 && self.RankName == "A l'essai" && model.OnlineCount == 2, "Own row is wrong");
                    Feed(account, "gIM-44"); Check(model.Members.Count == 2 && model.FindMember(44) == null, "gIM-<id> did not remove the member");
                    Feed(account, "gIM+44;Absent fictif;10;10;3;0;0;0;0;-1;48|43;Meneur fictif;81;9;1;9000;90;1;1;1;0");
                    Check(model.Members.Count == 3 && model.FindMember(43).Level == 81 && !model.FindMember(43).IsInFight, "gIM+ did not update an existing member");

                    // gP<id>|<rang>|<xp>|<droits> (fiche d'un membre) ; refus locaux ; gKE<d|a>.
                    Check(Sent(guild.SetProfileAsync(42, 3, 50, 8)) && Read(peer) == "gP42|3|50|8", "SetProfile does not send gP<id>|<rank>|<xp>|<rights>");
                    Check(!Sent(guild.SetProfileAsync(999, 0, 0, 0)) && !Sent(guild.SetProfileAsync(42, 0, 91, 0)) && !Sent(guild.SetProfileAsync(42, -1, 0, 0)) && !Sent(guild.SetProfileAsync(42, 0, 0, -1)),
                        "Invalid profiles were accepted");
                    NoPacket(peer, "A refused profile reached the server");
                    Feed(account, "gKEd"); Feed(account, "gKEa");
                    Check(Chat(account, ChatMessageKind.Error, "droits suffisant") && Chat(account, ChatMessageKind.Error, "ne fait pas partie de la guilde"), "gKE codes are not reported");

                    // gK<nom> : exclusion (droit de bannir, jamais le meneur), départ par son propre nom ; gKK<a>|<b> et gKK<a>.
                    Check(!Sent(guild.KickAsync("Meneur fictif")) && !Sent(guild.KickAsync("")) && !Sent(guild.KickAsync("a|b")), "Invalid kicks were accepted");
                    Check(Sent(guild.KickAsync("Absent fictif")) && Read(peer) == "gKAbsent fictif", "Kick does not send gK<name>");
                    Feed(account, "gKKPersonnage de test|Absent fictif");
                    Check(model.FindMember(44) == null && model.Members.Count == 2 && guild.HasGuild && Chat(account, ChatMessageKind.Info, "Tu as banni Absent fictif"), "gKK<self>|<other> did not remove the member");
                    Check(Sent(guild.KickAsync("personnage de TEST")) && Read(peer) == "gKPersonnage de test", "Kicking oneself does not leave with gK<self>");
                    Check(Sent(guild.LeaveAsync()) && Read(peer) == "gKPersonnage de test", "Leave does not send gK<self>");
                    Feed(account, "gKKMeneur fictif");
                    Check(!guild.HasGuild && !account.Game.character.HasGuild && model.Members.Count == 0 && Chat(account, ChatMessageKind.Info, "Meneur fictif t'a banni"), "gKK<other> did not leave the guild");
                    Check(!Sent(guild.OpenAsync()) && !Sent(guild.KickAsync("x")) && !Sent(guild.SetProfileAsync(42, 0, 0, 0)), "Guild actions were accepted after the ban");
                    NoPacket(peer, "A refused action reached the server");
                    Feed(account, Stats(Rights)); Feed(account, General); Feed(account, Members);
                    Check(guild.HasGuild && model.Members.Count == 3, "The guild was not restored");

                    // gIB : personnalisation des percepteurs ; gB<p|x|o|k> et gb<sort> ; points insuffisants refusés ; gIB vide = aucun boost.
                    Feed(account, Boosts);
                    GuildBoosts boosts = model.Boosts;
                    Check(model.BoostsReceived && boosts != null && boosts.MaxCollectors == 10 && boosts.Collectors == 2 && boosts.Life == 300 && boosts.Damage == 3 && boosts.Pods == 500
                        && boosts.Prospecting == 2 && boosts.Wisdom == 1 && boosts.Population == 250 && boosts.Points == 12 && boosts.HireCost == 1000 && boosts.Spells.Count == 2
                        && boosts.Spells[0].Id == 462 && boosts.Spells[0].Level == 3 && boosts.Value(GuildBoostKind.Pods) == 500, "gIB is not read like onInfosBoosts");
                    Check(Sent(guild.BoostAsync(GuildBoostKind.Pods)) && Read(peer) == "gBo" && Sent(guild.BoostAsync(GuildBoostKind.Prospecting)) && Read(peer) == "gBp"
                        && Sent(guild.BoostAsync(GuildBoostKind.Wisdom)) && Read(peer) == "gBx" && Sent(guild.BoostAsync(GuildBoostKind.Collectors)) && Read(peer) == "gBk",
                        "Boosts do not send gB<o|p|x|k>");
                    Check(Sent(guild.BoostSpellAsync(462)) && Read(peer) == "gb462" && !Sent(guild.BoostSpellAsync(0)), "Spell boost does not send gb<id>");
                    Feed(account, "gIB10|2|300|3|500|2|1|250|0|1000");
                    Check(model.Boosts.Points == 0 && model.Boosts.Spells.Count == 0 && !Sent(guild.BoostAsync(GuildBoostKind.Pods)) && !Sent(guild.BoostSpellAsync(462)), "Boosts were accepted without points");
                    NoPacket(peer, "A refused boost reached the server");
                    Feed(account, "gIB"); Check(model.BoostsReceived && model.Boosts == null, "Empty gIB did not mean no boosts");
                    Feed(account, Boosts);

                    // gITM± : percepteurs (noms et carte en base 36, détails facultatifs), gITp± attaquants, gITP± défenseurs ; null = aucun.
                    Feed(account, Collectors);
                    GuildCollector first = model.FindCollector(71), second = model.FindCollector(82);
                    Check(model.CollectorsReceived && model.Collectors.Count == 2 && first != null && second != null, "gITM+ rows were not read");
                    Check(first.Name == "Percepteur 10-11" && first.HasDetails && first.CallerName == "Poseur fictif" && first.LastHarvesterName == "Récolteur fictif" && first.MapId == 193
                        && first.X == 2 && first.Y == -4 && first.Position.Contains("(2, -4)") && !first.IsInFight && first.MaxPlayers == 5 && first.Timer == 3600, "Detailed collector row is wrong");
                    Check(second.Name == "Percepteur 12-13" && !second.HasDetails && second.IsInFight && second.CallerName == "?" && second.StartDate == -1, "Short collector row is wrong");
                    Feed(account, "gITp+1z|2b;Attaquant fictif;50;0"); Feed(account, "gITP+1z|2c;Défenseur fictif;10;60;1;2;3");
                    first = model.FindCollector(71);
                    Check(first.Attackers.Count == 1 && first.Attackers[0].Name == "Attaquant fictif" && first.Attackers[0].Level == 50 && !first.Attackers[0].IsDefender
                        && first.Defenders.Count == 1 && first.Defenders[0].Gfx == 10 && first.Defenders[0].Level == 60 && first.Defenders[0].IsDefender, "gITp/gITP were not applied");
                    Feed(account, "gITp-1z|2b"); Feed(account, "gITP+zz|2c;x;1;1");
                    Check(model.FindCollector(71).Attackers.Count == 0 && model.FindCollector(71).Defenders.Count == 1, "gITp- did not remove the attacker");
                    Feed(account, "gITM-2a"); Check(model.Collectors.Count == 1 && model.FindCollector(82) == null, "gITM- did not remove the collector");
                    Feed(account, "gITMnull"); Check(model.Collectors.Count == 0 && model.CollectorsReceived, "gITMnull did not clear the collectors");
                    Feed(account, Collectors); Feed(account, "gITM"); Check(model.Collectors.Count == 0, "Empty gITM did not clear the collectors");
                    Check(Sent(guild.HireCollectorAsync()) && Read(peer) == "gH" && Sent(guild.RemoveCollectorAsync(71)) && Read(peer) == "gF71", "Hire or remove do not send gH / gF<id>");
                    // gT<S|R|G> et gA<A|S|D> dans le chat ; gTK / gTE ignorés (gTJ / gTV ne sont jamais émis) ; gHE<code>.
                    Feed(account, "gTSa,b|900092|2|-4|Poseur fictif"); Feed(account, "gTRa,b|900092|2|-4|Poseur fictif"); Feed(account, "gTGa,b|900092|2|-4|Récolteur fictif|500;9,3");
                    Check(Chat(account, ChatMessageKind.Info, "Percepteur 10-11 a été posé") && Chat(account, ChatMessageKind.Info, "a été retiré") && Chat(account, ChatMessageKind.Info, "500 Points d'expérience, 3 x objet n° 9"),
                        "gT packets are not written to the chat");
                    Feed(account, "gTK"); Feed(account, "gTE"); Feed(account, "gTKa,b|1|2|3|4");
                    Feed(account, "gAAa,b|50|2|-4"); Feed(account, "gASa,b|50|2|-4"); Feed(account, "gADa,b|50|2|-4");
                    Check(alerts.Count == 3 && alerts[0].Contains("est attaqué") && Chat(account, ChatMessageKind.Info, "a survécu") && Chat(account, ChatMessageKind.Info, "n'a pas survécu"), "gA packets are not reported");
                    Feed(account, "gHEd"); Feed(account, "gHEk"); Feed(account, "gHEz"); Feed(account, "gHK");
                    Check(Chat(account, ChatMessageKind.Error, "pas assez de kamas"), "gHEk is not reported");

                    // gIF enclos, gIH maisons ; onglets gIM / gIB / gIT / gIF / gIH, gIG, gITV ; gUT / gUF ouvrent un onglet.
                    Feed(account, Paddocks);
                    Check(model.PaddocksReceived && model.MaxPaddocks == 3 && model.Paddocks.Count == 2 && model.Paddocks[0].MapId == 900092 && model.Paddocks[0].Size == 5 && model.Paddocks[0].MaxObjects == 3
                        && model.Paddocks[0].Mounts.Count == 1 && model.Paddocks[0].Mounts[0].Name == "Dragodinde fictive" && model.Paddocks[0].Mounts[0].OwnerName == "Propriétaire fictif"
                        && model.Paddocks[1].Mounts.Count == 0, "gIF is not read like onInfosMountPark");
                    Feed(account, Houses);
                    Check(model.HousesReceived && model.Houses.Count == 2 && model.Houses[0].Id == 7 && model.Houses[0].OwnerName == "Propriétaire fictif" && model.Houses[0].X == 2 && model.Houses[0].Y == -4
                        && model.Houses[0].Skills.Count == 2 && model.Houses[0].Rights == 3 && model.Houses[1].OwnerName == "", "gIH is not read like onInfosHouses");
                    Feed(account, "gIH"); Check(model.Houses.Count == 0 && model.HousesReceived, "Empty gIH did not clear the houses");
                    Check(Sent(guild.OpenAsync()) && Read(peer) == "gIG" && Sent(guild.RequestAsync(GuildTab.Members)) && Read(peer) == "gIM" && Sent(guild.RequestAsync(GuildTab.Boosts)) && Read(peer) == "gIB"
                        && Sent(guild.RequestAsync(GuildTab.Collectors)) && Read(peer) == "gIT" && Sent(guild.RequestAsync(GuildTab.Paddocks)) && Read(peer) == "gIF"
                        && Sent(guild.RequestAsync(GuildTab.Houses)) && Read(peer) == "gIH" && Sent(guild.CloseAsync()) && Read(peer) == "gITV", "Window packets differ from the client");
                    Feed(account, "gUT"); Feed(account, "gUF"); Feed(account, "gUx");
                    Check(tabs.Count == 2 && tabs[0] == GuildTab.Houses && tabs[1] == GuildTab.Paddocks, "gUT / gUF do not open their tab");

                    // Création : seulement après gn (guildalogemme sur la carte 2196) ; gC décimal ; gCE → boîte ; gS + gCK → onglet Membres.
                    Check(!Sent(guild.CreateAsync(1, 0, 1, 0, "Test")), "Creation was accepted with a guild");
                    Feed(account, LeaveSelf);
                    Check(!guild.HasGuild && Chat(account, ChatMessageKind.Info, "Tu as quitté ta guilde"), "gKK<self>|<self> did not leave the guild");
                    Check(!Sent(guild.CreateAsync(1, 0, 1, 0, "Test")) && !Sent(guild.LeaveCreationAsync()), "Creation was accepted before gn");
                    NoPacket(peer, "A refused creation reached the server");
                    Feed(account, "gn"); Check(guild.CreationWindowOpen, "gn did not open the creation window");
                    Check(Sent(guild.CreateAsync(2, 255, 3, 65280, "Guilde-fictive")) && Read(peer) == "gC2|255|3|65280|Guilde-fictive", "Create does not send gC<b>|<bc>|<u>|<uc>|<name>");
                    // Règles de GameClient.createGuild : lettres sans accent, apostrophes, deux tirets au plus, vingt caractères.
                    Check(!Sent(guild.CreateAsync(2, 255, 3, 65280, "Nom avec espace")) && !Sent(guild.CreateAsync(2, 255, 3, 65280, "")) && !Sent(guild.CreateAsync(0, 255, 3, 65280, "Ok"))
                        && !Sent(guild.CreateAsync(2, 255, 3, 65280, new string('a', 21))) && !Sent(guild.CreateAsync(2, 255, 3, 65280, "a-b-c-d")) && !Sent(guild.CreateAsync(2, 255, 3, 65280, "Guilde é")),
                        "Invalid guild names or emblems were accepted");
                    NoPacket(peer, "A refused creation reached the server");
                    Feed(account, "gCEan");
                    Check(boxes.Count == 1 && boxes[0] == "Guilde (test)|Nom déjà pris (test)" && Chat(account, ChatMessageKind.Error, "Nom déjà pris (test)"), "gCEan does not raise the error box");
                    Check(Sent(guild.LeaveCreationAsync()) && Read(peer) == "gV", "Leaving the creation does not send gV");
                    Feed(account, "gV"); Check(!guild.CreationWindowOpen && !Sent(guild.LeaveCreationAsync()), "gV did not close the creation window");
                    Feed(account, "gn"); Feed(account, Stats(1)); Feed(account, "gCK");
                    Check(guild.HasGuild && model.IsBoss && !guild.CreationWindowOpen && tabs.Last() == GuildTab.Members && Chat(account, ChatMessageKind.Info, "Guilde créée"), "gCK is not handled");
                    // Meneur : départ possible seul, refusé avec d'autres membres (règle de StarLoco) ; tous les droits.
                    Check(Sent(guild.LeaveAsync()) && Read(peer) == "gKPersonnage de test", "A lone boss cannot leave");
                    Feed(account, Members);
                    Check(!Sent(guild.LeaveAsync()) && guild.LastMessage.Contains("Meneur"), "A boss with members could leave");
                    Check(Sent(guild.KickAsync("Absent fictif")) && Read(peer) == "gKAbsent fictif" && Sent(guild.SetProfileAsync(43, 1, 0, 1)) && Read(peer) == "gP43|1|0|1", "Boss actions were refused");
                    Feed(account, Stats(Rights));

                    // Paquets mal formés : journalisés comme illisibles au besoin, jamais propagés (le handler n'attrape aucune exception).
                    int before = logs.Count(entry => entry.Contains("illisible ignoré :"));
                    foreach (string broken in new[] { "gS", "gS|", "gIG", "gIG1|2", "gIM", "gIMx", "gIB1|2", "gIT", "gITM", "gITMx", "gITp", "gITp+zz", "gITP+", "gIF", "gIH", "gJ", "gJE", "gJEz",
                        "gJK", "gJr", "gJrx|y", "gK", "gKK", "gKE", "gC", "gCE", "gCEzz", "gH", "gHE", "gT", "gTS", "gTSa,b|x", "gA", "gAA", "gAAa,b", "gU", "gUx", "gn", "gV", "gX", "g" })
                        Feed(account, broken);
                    NoPacket(peer, "A malformed packet was answered");
                    Check(logs.Count(entry => entry.Contains("illisible ignoré :")) > before, "Malformed packets were not logged");
                    Check(!logs.Any(entry => entry.Contains("illisible ignoré (")), "A guild packet threw: " + string.Join(" / ", logs.Where(entry => entry.Contains("illisible ignoré ("))));
                    Check(guild.HasGuild && !guild.CreationWindowOpen && model.Members.Count == 3 && model.Name == "Guilde fictive" && changes > 30, "Malformed packets changed the state");
                    guild.Changed -= counted; guild.ErrorBox -= box; guild.OpenTabRequested -= tab; guild.CollectorAlert -= alert; guild.InvitationClosed -= closing;

                    // Volet Guilde : images synthétiques lues dans un dossier temporaire (classe, alignement, épée, emblème blanc recoloré).
                    string images = Path.Combine(folder, "images");
                    Color mini9 = Color.FromArgb(255, 30, 160, 200), mini8 = Color.FromArgb(255, 160, 90, 30), alignColor = Color.FromArgb(255, 200, 40, 160), swordColor = Color.FromArgb(255, 220, 120, 20);
                    Png(Path.Combine(images, "Artworks", "Mini"), "9", mini9, 16, 16);
                    Png(Path.Combine(images, "Artworks", "Mini"), "8", mini8, 16, 16);
                    Png(Path.Combine(images, "Alignments", "mini"), "1", alignColor, 12, 12);
                    Png(Path.Combine(images, "Party"), "infos", swordColor, 14, 14);
                    Png(Path.Combine(images, "Emblems", "back"), "3", Color.White, 80, 80);
                    Png(Path.Combine(images, "Emblems", "up"), "5", Color.White, 50, 50);
                    AssetsRoot(images);
                    Feed(account, LeaveSelf);

                    using (var form = new GameClientFullform(account))
                    {
                        form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); Application.DoEvents();
                        var drawer = form.Panels; var panel = drawer.Get<GuildPanel>(); var collectorPanel = drawer.Get<CollectorPanel>();
                        Check(panel != null && collectorPanel != null && drawer.Current == null && panel.Title == "Guilde (test)", "Guild panels are not registered with the client title");
                        NoPacket(peer, "The game window sent a guild packet before the panel was opened");
                        // Sans guilde, le bouton du bandeau refuse (UI_ONLY_FOR_GUILD) ; avec un gS, il ouvre le volet : gIG puis l'onglet courant (gIM).
                        Button icon = All(form).OfType<Button>().First(button => "client-icon".Equals(button.Tag) && button.AccessibleName == "Guilde");
                        Click(icon); Application.DoEvents();
                        Check(drawer.Current == null && !guild.WindowOpen, "The guild panel opened without a guild");
                        NoPacket(peer, "Opening without a guild sent a packet");
                        FeedFromNetwork(account, Stats(Rights)); PumpUntil(() => account.Game.character.HasGuild);
                        Click(icon); PumpUntil(() => drawer.Current == panel);
                        Check(Read(peer) == "gIG" && Read(peer) == "gIM" && guild.WindowOpen && panel.CurrentTab == GuildTab.Members, "Opening the panel does not send gIG then gIM");
                        FeedFromNetwork(account, General); FeedFromNetwork(account, Members);
                        GuildMemberList list = panel.MemberList;
                        PumpUntil(() => list.Members.Count == 3 && Find(form, "guild-count").Text.StartsWith("3 membre(s), 2 connecté(s)"));
                        Check(Find(form, "guild-name").Text == "Guilde fictive" && Find(form, "guild-level").Text.Contains("12"), "Header labels are wrong: " + Find(form, "guild-level").Text);

                        // Emblème composé sur le pool de fils (fond puis motif recolorés) et posé par la fenêtre de jeu.
                        var emblemBox = (PictureBox)Find(form, "guild-emblem");
                        PumpUntil(() => emblemBox.Image != null);
                        using (var emblem = new Bitmap(emblemBox.Image))
                            Check(emblem.Width == 48 && Near(emblem.GetPixel(24, 24), Color.FromArgb(0xD0, 0x20, 0x20)) && Near(emblem.GetPixel(4, 24), Color.FromArgb(0x20, 0xB0, 0x40)),
                                "Emblem is not composed from the recoloured back and up layers");

                        // Lignes au pixel témoin : alignement, illustration de classe, épée du combat, membre hors ligne sans icône d'alignement.
                        PumpUntil(() => Cached("Artworks", "Mini/9") && Cached("Artworks", "Mini/8") && Cached("Alignments", "mini/1") && Cached("Party", "infos"));
                        Check(list.Width > 150 && list.Height == GuildMemberList.HeaderHeight + 3 * GuildMemberList.RowHeight + 1, "Member rows are not laid out: " + list.Size);
                        using (var image = new Bitmap(list.Width, list.Height))
                        {
                            list.DrawToBitmap(image, new Rectangle(Point.Empty, list.Size));
                            Rectangle row0 = list.RowBounds(0), row2 = list.RowBounds(2);
                            int swordX = list.Width - 24 + 11;
                            Check(Near(image.GetPixel(10, row0.Y + 10), alignColor), "Alignment icon is not drawn");
                            Check(Near(image.GetPixel(27, row0.Y + 10), mini9), "Class artwork is not drawn");
                            Check(Near(image.GetPixel(swordX, row0.Y + 10), swordColor), "Fight sword is not drawn for a member in fight");
                            Check(Near(image.GetPixel(27, row2.Y + 10), mini8) && !Near(image.GetPixel(10, row2.Y + 10), alignColor), "Own row is wrong");
                            Check(!Near(image.GetPixel(swordX, list.RowBounds(1).Y + 10), swordColor), "Fight sword is drawn outside a fight");
                        }

                        // Fiche d'un membre : rang (ranks_fr), % XP, cases des droits → gP ; meneur non modifiable ; exclusion après la question.
                        GuildMemberSheet sheet = panel.MemberSheet;
                        Check(list.ClickRow("Personnage de test") && sheet.Visible && sheet.Current.Id == 42 && list.SelectedId == 42, "Clicking a row does not open the sheet");
                        Check(sheet.RankBox.Items.Count == 3 && sheet.RankBox.Enabled && sheet.XpBox.Value == 10 && sheet.KickButton.Text == "Quitter" && sheet.ApplyButton.Enabled
                            && sheet.RightBoxes.Single(check => (GuildRight)check.Tag == GuildRight.Ban).Checked && !sheet.RightBoxes.Single(check => (GuildRight)check.Tag == GuildRight.ManageOwnXp).Checked,
                            "Member sheet is not filled from the row");
                        sheet.RankBox.SelectedIndex = Enumerable.Range(0, sheet.RankBox.Items.Count).First(index => sheet.RankBox.Items[index].ToString() == "Trésorier");
                        sheet.XpBox.Value = 50; sheet.RightBoxes.Single(check => (GuildRight)check.Tag == GuildRight.Ban).Checked = false;
                        sheet.ApplyButton.PerformClick(); Check(Read(peer) == "gP42|3|50|750", "Apply does not send gP<id>|<rank>|<xp>|<rights>");
                        Check(list.ClickRow("Meneur fictif") && !sheet.KickButton.Enabled && !sheet.RankBox.Enabled && sheet.RightBoxes.All(check => check.Checked && !check.Enabled), "Boss sheet is editable");
                        Check(list.ClickRow("Absent fictif") && sheet.KickButton.Text == "Exclure" && sheet.KickButton.Enabled, "Kick button is not offered");
                        sheet.KickButton.PerformClick(); Form question = Dialog();
                        Check(Message(question).Contains("Absent fictif"), "Kick question differs: " + Message(question));
                        Answer(question, "Non"); NoPacket(peer, "Refusing the kick question sent a packet");
                        sheet.KickButton.PerformClick(); Answer(Dialog(), "Oui"); Check(Read(peer) == "gKAbsent fictif", "Kick does not send gK<name> after the question");

                        // Invitation reçue : boîte Oui / Non / Ignorer nommée, posée depuis le fil réseau, réponse avec l'identifiant reçu.
                        FeedFromNetwork(account, "gJr48|Invitant fictif|Autre guilde");
                        PumpUntil(() => panel.InvitationDialog != null);
                        Form invite = panel.InvitationDialog;
                        Check(invite.Name == GuildPanel.InviteDialogName && invite.Text == "Guilde (test)" && Message(invite) == "Invitant fictif t'invite dans Autre guilde (test)"
                            && All(invite).OfType<Button>().Select(button => button.Text).OrderBy(text => text).SequenceEqual(new[] { "Ignorer", "Non", "Oui" }), "Invitation box differs");
                        NoPacket(peer, "The invitation was answered before the choice");
                        Answer(invite, "Non"); Check(Read(peer) == "gJE48", "No does not send gJE<id>"); PumpUntil(() => panel.InvitationDialog == null);
                        FeedFromNetwork(account, "gJr49|Invitant fictif|Autre guilde"); PumpUntil(() => panel.InvitationDialog != null);
                        Answer(panel.InvitationDialog, "Oui"); Check(Read(peer) == "gJK49", "Yes does not send gJK<id>");
                        FeedFromNetwork(account, "gJr50|Invitant fictif|Autre guilde"); PumpUntil(() => panel.InvitationDialog != null);
                        FeedFromNetwork(account, "gJC"); PumpUntil(() => panel.InvitationDialog == null && BotDialogs.OpenDialogs.Count == 0);
                        NoPacket(peer, "A cancelled invitation (gJC) was answered");
                        FeedFromNetwork(account, "gJr51|Ignoré par le volet|Autre guilde"); PumpUntil(() => panel.InvitationDialog != null);
                        Answer(panel.InvitationDialog, "Ignorer"); Check(Read(peer) == "gJE51", "Ignore does not send gJE<id>");
                        FeedFromNetwork(account, "gJr52|Ignoré par le volet|Autre guilde"); Check(Read(peer) == "gJE52" && panel.InvitationDialog == null, "An ignored inviter opened a box");

                        // Onglets : gIB puis bouton + → gBo ; gIT puis Retirer → gF<id>, Poser → gH ; gIF ; gIH ; fermeture → gITV.
                        panel.Tabs.ClickTab(GuildTab.Boosts); Check(Read(peer) == "gIB" && panel.CurrentTab == GuildTab.Boosts && panel.Tabs.Selected == GuildTab.Boosts, "Boosts tab does not send gIB");
                        FeedFromNetwork(account, Boosts); PumpUntil(() => Find(form, "guild-boost-points").Text.Contains("12"));
                        Check(((ListView)Find(form, "guild-spells")).Items.Count == 2 && Find(form, "guild-boost-plus-pods").Enabled, "Boosts tab is not filled");
                        Click(Find(form, "guild-boost-plus-pods")); Check(Read(peer) == "gBo", "Pods + does not send gBo");
                        Click(Find(form, "guild-hire")); Answer(Dialog(), "Oui"); Check(Read(peer) == "gH", "Hire does not send gH after the question");
                        panel.Tabs.ClickTab(GuildTab.Collectors); Check(Read(peer) == "gIT", "Collectors tab does not send gIT");
                        FeedFromNetwork(account, Collectors); PumpUntil(() => panel.CollectorList.Items.Count == 2);
                        Check(Find(form, "guild-collectors-info").Text.Contains("n'est pas proposée") && !Find(form, "guild-collector-remove").Enabled, "Collectors tab is not filled");
                        panel.CollectorList.Items[0].Selected = true; Application.DoEvents();
                        Check(Find(form, "guild-collector-remove").Enabled, "Remove is not enabled for a selected collector");
                        Click(Find(form, "guild-collector-remove")); Answer(Dialog(), "Oui"); Check(Read(peer) == "gF71", "Remove does not send gF<id>");
                        panel.Tabs.ClickTab(GuildTab.Paddocks); Check(Read(peer) == "gIF", "Paddocks tab does not send gIF");
                        FeedFromNetwork(account, Paddocks); PumpUntil(() => ((ListView)Get(panel, "paddocks")).Items.Count == 2);
                        panel.Tabs.ClickTab(GuildTab.Houses); Check(Read(peer) == "gIH", "Houses tab does not send gIH");
                        FeedFromNetwork(account, Houses); PumpUntil(() => ((ListView)Get(panel, "houses")).Items.Count == 2);
                        drawer.CloseAll(); Check(Read(peer) == "gITV" && !guild.WindowOpen && !drawer.IsOpen(panel), "Closing the panel does not send gITV");
                        // gUT (objet de maison) : le volet s'ouvre sur l'onglet Maisons (gIG puis gIH) ; gCE depuis le réseau : boîte d'information.
                        FeedFromNetwork(account, "gUT"); PumpUntil(() => drawer.Current == panel);
                        Check(panel.CurrentTab == GuildTab.Houses && Read(peer) == "gIG" && Read(peer) == "gIH", "gUT does not open the Houses tab");
                        drawer.CloseAll(); Check(Read(peer) == "gITV", "Closing again does not send gITV");
                        FeedFromNetwork(account, "gCEan"); Form error = Dialog();
                        Check(Message(error) == "Nom déjà pris (test)", "gCEan box differs"); Answer(error, "OK");

                        // Menu d'un joueur : « Inviter dans la guilde » (gJR<nom>) seulement avec le droit d'inviter.
                        var guildProvider = ActorMenuRegistry.Default.Providers.OfType<GuildMenuProvider>().Single();
                        var player = account.Game.Map.GetActor(77) as PlayerActor;
                        Check(player != null && guildProvider.Handles(player) && !guildProvider.Handles(account.Game.character), "Guild menu provider accepts the wrong actors");
                        List<MenuEntry> entries = guildProvider.Entries(player, account.Game).ToList();
                        Check(entries.Count == 1 && entries[0].Texte == "Inviter dans la guilde (test)" && entries[0].Activé && entries[0].Infobulle.Contains("gJRAmi fictif"), "Player menu entry differs");
                        var context = new ActorMenuContext(player, null, -1, account, null, drawer, Keys.None);
                        Check(Result(entries[0].Action(context)) != null && Read(peer) == "gJRAmi fictif", "Invite entry does not send gJR<name>");
                        Check(ActorMenuRegistry.Default.EntriesFor(player, account.Game).Count(entry => entry.Texte == "Inviter dans la guilde (test)") == 1, "Invite entry is missing from the merged menu");
                        FeedFromNetwork(account, Stats(512)); PumpUntil(() => model.OwnRights == 512);
                        Check(!guildProvider.Entries(player, account.Game).Any(), "Invite is offered without the right");
                        FeedFromNetwork(account, Stats(Rights)); PumpUntil(() => model.OwnRights == Rights);

                        // Menu d'un percepteur de sa guilde : Parler (DC), Collecter (ER8|<id>), Attaquer grisé, Retirer (gF<id>) ; jamais gTJ / gTV.
                        var collectorProvider = ActorMenuRegistry.Default.Providers.OfType<CollectorMenuProvider>().Single();
                        var own = account.Game.Map.GetActor(70) as CollectorActor;
                        Check(own != null && collectorProvider.Handles(own) && !collectorProvider.Handles(player) && guild.IsOwnCollector(own) && guild.CanCollect(own), "Collector provider accepts the wrong actors");
                        entries = collectorProvider.Entries(own, account.Game).ToList();
                        Check(entries.Select(entry => entry.Texte).SequenceEqual(new[] { "Parler (test)", "Collecter (test)", "Attaquer (test)", "Retirer (test)" })
                            && entries[0].Activé && entries[1].Activé && !entries[2].Activé && entries[3].Activé, "Own collector menu differs: " + string.Join("|", entries.Select(entry => entry.Texte)));
                        IReadOnlyList<MenuEntry> merged = ActorMenuRegistry.Default.EntriesFor(own, account.Game);
                        Check(merged.Count(entry => entry.Texte == "Collecter (test)") == 1 && !merged.Any(entry => (entry.Texte + entry.Infobulle).Contains("gTJ") || (entry.Texte + entry.Infobulle).Contains("gTV")),
                            "Collector defence (gTJ / gTV) is offered");
                        var collectorContext = new ActorMenuContext(own, null, -1, account, null, drawer, Keys.None);
                        Check(Result(entries[0].Action(collectorContext)) != null && Read(peer) == "DC70", "Speak does not send DC<id>");
                        Check(Result(entries[1].Action(collectorContext)) != null && Read(peer) == "ER8|70", "Collect does not send ER8|<id>");
                        // Fenêtre de collecte : ECK8|<id> puis EL ; Récupérer → EMO- par objet puis EMG- ; contenu suivi par EsK ; Fermer → EV.
                        CollectorExchange window = guild.CollectorWindow;
                        FeedFromNetwork(account, "ECK8|70"); PumpUntil(() => drawer.Current == collectorPanel);
                        Check(window.IsOpen && window.CollectorId == 70 && !window.ContentReceived && collectorPanel.IsServerWindowOpen, "ECK8 did not open the collector window");
                        FeedFromNetwork(account, "ELO3eb~7d1~a~~;;G250"); PumpUntil(() => window.ContentReceived && Find(form, "collector-kamas").Text.Contains("250"));
                        Check(window.Items.Count == 1 && window.Items[0].Id == 1003 && window.Items[0].Quantity == 10 && window.Kamas == 250 && Find(form, "collector-withdraw-all").Enabled, "EL was not read");
                        Click(Find(form, "collector-withdraw-all")); Check(Read(peer) == "EMO-1003|10" && Read(peer) == "EMG-250", "Withdraw all does not send EMO- then EMG-");
                        Check(window.Items.Count == 1 && window.Kamas == 250, "The content changed before the server answer");
                        FeedFromNetwork(account, "EsKO-1003"); FeedFromNetwork(account, "EsKG0"); PumpUntil(() => window.Items.Count == 0 && window.Kamas == 0);
                        Check(!Find(form, "collector-withdraw-all").Enabled, "Withdraw all is enabled on an empty collector");
                        Click(Find(form, "collector-leave")); Check(Read(peer) == "EV" && window.IsOpen, "Leave does not send EV or closed before the server");
                        FeedFromNetwork(account, "EV"); PumpUntil(() => !window.IsOpen && drawer.Current != collectorPanel);
                        Check(account.AccountStates == AccountStates.CONNECTED_INACTIVE, "EV did not restore the state");
                        Task<string> removing = entries[3].Action(collectorContext);
                        Form remove = Dialog(); Check(Message(remove).Contains("Percepteur de Guilde fictive"), "Remove question differs: " + Message(remove));
                        Answer(remove, "Oui"); Check(Result(removing) != null && Read(peer) == "gF70", "Remove entry does not send gF<id> after the question");
                        // Percepteur d'une autre guilde : Parler et Attaquer (GA909<id>) seulement.
                        FeedFromNetwork(account, "GM|+13;1;0;78;c,d;-6;6000^100;3;Autre guilde;1a,2b,3c,4d");
                        PumpUntil(() => account.Game.Map.GetActor(78) is CollectorActor);
                        var other = (CollectorActor)account.Game.Map.GetActor(78);
                        entries = collectorProvider.Entries(other, account.Game).ToList();
                        Check(entries.Select(entry => entry.Texte).SequenceEqual(new[] { "Parler (test)", "Attaquer (test)" }) && entries[1].Activé && !guild.IsOwnCollector(other),
                            "Foreign collector menu differs: " + string.Join("|", entries.Select(entry => entry.Texte)));
                        Check(Result(entries[1].Action(new ActorMenuContext(other, null, -1, account, null, drawer, Keys.None))) != null && Read(peer) == "GA90978", "Attack does not send GA909<id>");
                        Check(!Sent(guild.CollectAsync(78)) && !Sent(guild.CollectAsync(999)), "Collecting a foreign collector was accepted");
                        // Sans guilde : plus de collecte ni de retrait, l'attaque reste possible.
                        FeedFromNetwork(account, LeaveSelf); PumpUntil(() => !guild.HasGuild);
                        entries = collectorProvider.Entries(own, account.Game).ToList();
                        Check(entries.Select(entry => entry.Texte).SequenceEqual(new[] { "Parler (test)", "Attaquer (test)" }) && entries[1].Activé && !guildProvider.Entries(player, account.Game).Any(),
                            "Menus still offer guild actions without a guild");
                        NoPacket(peer, "A refused action reached the server");
                        form.Close();
                    }
                    Check(BotDialogs.OpenDialogs.Count == 0, "A dialog survived the game window");
                    Check(!logs.Any(entry => entry.Contains("abonné de la guilde a échoué") || entry.Contains("illisible ignoré (")),
                        "A guild subscriber failed or a packet threw: " + string.Join(" / ", logs.Where(entry => entry.Contains("guilde"))));
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
