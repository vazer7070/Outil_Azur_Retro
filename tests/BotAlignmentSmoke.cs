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
using Tool_BotProtocol.Game.Alignement;
using Tool_BotProtocol.Game.Chat;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Session;

// Alignement, prismes et conquête selon dofus.aks.Specialization / Subareas / Conquest / Subway / Game.onPVP (client 1.34) et
// GameClient / Player / SocketManager de StarLoco, sur un serveur fictif local (lot F9) : As → modèle (camp, grade, honneur, ailes,
// textes pvp_fr / alignment_fr), ZS / ZC, al / am / aM (annonces du canal d'alignement), GIP jamais répondu seul puis GP- / rien,
// GP+ / GP* et leurs refus, Cb, CB, CWJ / CW (formes alignée et neutre) / CWV, filtres capturable / vulnérable (voisines de maps_fr),
// CIJ / CP / Cp / CFJ / CIV (jamais après une erreur), CA / CS / CD, GA512<id> seulement pour un prisme de son camp, Wp → fenêtre,
// Wp<carte> et ses refus, Ww, paquets mal formés sans exception ; puis le volet Conquête dans la fenêtre de jeu (Cb puis CB à
// l'ouverture, boîtes ASK_ENABLED_PVP → GP+ et GIP → ASK_DISABLE_PVP → GP-, onglets CWJ / CWV / CIJ / CIV, prismes de CW vers la
// carte du monde, Rejoindre → CFJ, CIV ferme le volet), le volet Liste des prismes et le menu d'un prisme (Utiliser → GA512,
// Attaquer → GA912 du lot M4).
internal static class BotAlignmentSmoke
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private const int OwnPrism = 72, EnemyPrism = 71;

    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            string path = Path.Combine(TestPaths.ApplicationBin, new AssemblyName(e.Name).Name + ".dll");
            if (!File.Exists(path)) path = Path.ChangeExtension(path, "exe");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        try { Application.EnableVisualStyles(); Run(); Console.WriteLine("OK: As, ZS/ZC, al/am/aM, GIP, GP+/GP-/GP*, Cb, CB, CWJ/CW/CWV, CIJ/CP/Cp/CFJ/CIV, CA/CS/CD, GA512, Wp/Ww, volet Conquête, liste des prismes, menu d'un prisme"); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static object Get(object target, string field) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static IEnumerable<Control> All(Control value) { yield return value; foreach (Control child in value.Controls) foreach (var nested in All(child)) yield return nested; }
    private static Control Find(Control root, string name) => All(root).First(control => control.Name == name);
    private static void PumpUntil(Func<bool> done, int seconds = 6, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(seconds);
        while (!done()) { if (DateTime.UtcNow > end) throw new TimeoutException("Alignment loopback timed out (line " + line + ")"); Application.DoEvents(); Thread.Sleep(5); }
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
    /// <summary>Deux paquets envoyés sans ordre garanti (deux envois lancés à la suite).</summary>
    private static void ReadPair(Socket socket, string first, string second, string message)
    {
        string[] received = { Read(socket), Read(socket) };
        Check(received.OrderBy(text => text, StringComparer.Ordinal).SequenceEqual(new[] { first, second }.OrderBy(text => text, StringComparer.Ordinal)), message + " (" + string.Join(", ", received) + ")");
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
    private static void Png(string folder, string name, Color color, int width, int height)
    {
        Directory.CreateDirectory(folder);
        using (var image = new Bitmap(width, height)) { using (var graphics = Graphics.FromImage(image)) graphics.Clear(color); image.Save(Path.Combine(folder, name + ".png"), ImageFormat.Png); }
    }
    private static bool Chat(Accounts account, ChatMessageKind kind, string text) => account.Game.Chat.Messages.Any(message => message.Kind == kind && message.Text.Contains(text));
    private static void Lang(string folder, string file, string family, string body) =>
        File.WriteAllText(Path.Combine(folder, file), "<?xml version='1.0' encoding='utf-8'?>\n<BotLang famille=\"" + family + "\" langue=\"fr\" version=\"1\" source=\"" + family + "_fr_1.swf\">\n" + body + "\n</BotLang>\n", new UTF8Encoding(false));
    private static string Text(string key, string value) => "<texte cle=\"" + key + "\" valeur=\"" + value + "\" />\n";

    // ClientAssets est interne à l'application : sa racine (dossier temporaire du test) passe par la réflexion.
    private static Type Assets => typeof(LoginForm).Assembly.GetType("Outil_Azur_complet.Bot.ClientAssets");
    private static void AssetsRoot(string value) => Assets.GetProperty("Root", Any).SetValue(null, value, null);

    /// <summary><c>As</c> de <c>Player.getAsPacket</c> : xp | kamas | points | sorts | camp~camp,valeur,grade,honneur,déshonneur,ailes | vie | énergie | initiative | prospection | dix caractéristiques.</summary>
    private static string As(int side, int grade, int honor, int dishonor, int wings, int kamas = 1000) =>
        "As0,0,100|" + kamas + "|0|0|" + side + "~" + side + ",10," + grade + "," + honor + "," + dishonor + "," + wings + "|50,50|10000,10000|100|100|"
        + string.Join("|", Enumerable.Repeat("10,0,0,0", 10));
    // CW de SocketManager.GAME_SEND_CW : possédées|total|possibles|sous-zones (id,camp,combat,carte du prisme,1)|villages possédés|total|villages (id,camp,1,prisme).
    private const string WorldData = "CW1|3|2|7,1,0,900092,1;8,-1,0,0,1;9,2,1,900094,1|0|1|3,2,1,1";
    private const string Defenders = "CP+1z|16;Défenseur fictif;10;60;1;2;3;0|17;Réserviste fictif;10;20;1;2;3;1";
    private const string Attackers = "Cp+1z|18;Attaquant fictif;50";
    private const string PrismList = "Wp900092|900092;0|900093;150|900094;*";

    private static void Run()
    {
        string previous = Environment.CurrentDirectory;
        string folder = Path.Combine(TestPaths.Work, "bot-alignment"); Directory.CreateDirectory(folder); Environment.CurrentDirectory = folder;
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var logs = new ConcurrentQueue<string>();
        try
        {
            LangData.Clear();
            MessagesReception.Init();
            Map.AllBotMaps[900092] = new Map { MapID = 900092, MapWidth = 3, MapHeight = 4, MapData = string.Concat(Enumerable.Repeat("HhGaeaaaaa", 18)) };

            // Textes du client, alignements (A.a, A.s), grades et bornes d'honneur (PP de pvp_fr), cartes et sous-zones voisines (maps_fr) synthétiques.
            string lang = Path.Combine(folder, "BotLang"); Directory.CreateDirectory(lang);
            Lang(lang, "lang.xml", "lang",
                Text("CONQUEST_WORD", "Conquête (test)") + Text("PVP_MODE", "Mode PvP (test)") + Text("ACTIVE", "Actif (test)") + Text("INACTIVE", "Inactif (test)")
                + Text("ENABLE_PVP_SHORT", "Activer (test)") + Text("DISABLE_PVP_SHORT", "Désactiver (test)") + Text("ASK_ENABLED_PVP", "Activer le PvP ? (test)")
                + Text("ASK_DISABLE_PVP", "Tu perdras %1 points d'honneur (test)") + Text("PRISM_ATTACKED", "Prisme de %1 attaqué en %2 (test)")
                + Text("PRISM_ATTACKED_SUVIVED", "Prisme de %1 en %2 a survécu (test)") + Text("PRISM_ATTACKED_DIED", "Prisme de %1 en %2 détruit (test)")
                + Text("SUBAREA_ALIGNMENT_IS", "%1 appartient à %2 (test)") + Text("SUBAREA_ALIGNMENT_PRISM_REMOVED", "Prisme de %1 retiré (test)")
                + Text("AREA_ALIGNMENT_IS", "Zone %1 appartient à %2 (test)") + Text("AREA_ALIGNMENT_PRISM_REMOVED", "Prisme de la zone %1 retiré (test)")
                + Text("USE_WORD", "Utiliser (test)") + Text("ATTACK", "Attaquer (test)") + Text("PRISM_LIST", "Prismes (test)") + Text("NOT_ENOUGH_RICH", "Pas assez de kamas (test)")
                + Text("CONQUEST_JOIN_FIGHT_NONE", "Aucun prisme (test)") + Text("CONQUEST_JOIN_FIGHT_NOFIGHT", "Aucun combat (test)") + Text("CONQUEST_JOIN_FIGHT_INFIGHT", "Déjà en combat (test)")
                + Text("YOUR_SPECIALIZATION_CHANGED", "Spécialisation : %1 (test)") + Text("CONQUEST_ALL_AREAS", "Toutes (test)") + Text("CONQUEST_ALIGNED_AREAS", "Zones de %1 (test)")
                + Text("STATS", "Stats (test)") + Text("ZONES_WORD", "Zones (test)") + Text("DEFEND", "Défendre (test)") + Text("RANK", "Grade (test)"));
            Lang(lang, "alignment.xml", "alignment",
                "<entree table=\"A.a\" id=\"0\" c=\"true\" n=\"Neutre (test)\" />\n<entree table=\"A.a\" id=\"1\" c=\"true\" n=\"Bonta (test)\" />\n"
                + "<entree table=\"A.a\" id=\"2\" c=\"true\" n=\"Brâkmar (test)\" />\n<entree table=\"A.a\" id=\"3\" c=\"false\" n=\"Mercenaire (test)\" />\n"
                + "<entree table=\"A.s\" id=\"1\" n=\"Spécialisation fictive\" />");
            Lang(lang, "pvp.xml", "pvp",
                "<entree table=\"PP\" id=\"grds\" valeur=\"[[{&quot;nc&quot;:&quot; &quot;,&quot;nl&quot;:&quot;Neutre&quot;}],[{&quot;nc&quot;:&quot; &quot;,&quot;nl&quot;:&quot; &quot;},{&quot;nc&quot;:&quot;RCR&quot;,&quot;nl&quot;:&quot;Recrue (test)&quot;},{&quot;nc&quot;:&quot;ASP&quot;,&quot;nl&quot;:&quot;Aspirant (test)&quot;}],[{&quot;nc&quot;:&quot; &quot;,&quot;nl&quot;:&quot; &quot;},{&quot;nc&quot;:&quot;RCR&quot;,&quot;nl&quot;:&quot;Recrue B&quot;}]]\" />\n"
                + "<entree table=\"PP\" id=\"hp\" valeur=\"0,500,1500,3000,5000,7500,10000,12500,15000,17500,18000\" />\n<entree table=\"PP\" id=\"maxdp\" valeur=\"500\" />");
            Lang(lang, "maps.xml", "maps",
                "<carte id=\"900092\" x=\"2\" y=\"-4\" sousZone=\"7\" />\n<carte id=\"900093\" x=\"3\" y=\"-4\" sousZone=\"8\" />\n<carte id=\"900094\" x=\"5\" y=\"-4\" sousZone=\"9\" />\n"
                + "<sousZone id=\"7\" nom=\"Sous-zone fictive\" zone=\"3\" voisines=\"8,9\" />\n<sousZone id=\"8\" nom=\"//Voisine fictive\" zone=\"3\" voisines=\"7\" />\n"
                + "<sousZone id=\"9\" nom=\"Lointaine fictive\" zone=\"4\" voisines=\"7\" />\n<zone id=\"3\" nom=\"Zone fictive\" superZone=\"0\" />\n<zone id=\"4\" nom=\"Autre zone\" superZone=\"0\" />");
            Check(LangData.Load(lang) == 4, "Synthetic texts were not loaded");
            Check(AlignmentTexts.Name(1) == "Bonta (test)" && AlignmentTexts.Name(9).StartsWith("Alignement 9") && AlignmentTexts.Sides().SequenceEqual(new[] { 0, 1, 2, 3 })
                && AlignmentTexts.IsConqueror(2) && !AlignmentTexts.IsConqueror(3), "alignment_fr is not read");
            Check(AlignmentTexts.GradeName(1, 2) == "Aspirant (test)" && AlignmentTexts.GradeName(2, 1) == "Recrue B" && AlignmentTexts.GradeName(1, 9) == "Grade 9"
                && AlignmentTexts.HonorBounds(2).Item1 == 500 && AlignmentTexts.HonorBounds(2).Item2 == 1500 && AlignmentTexts.HonorBounds(10).Item2 == 18000 && AlignmentTexts.MaxDishonor() == 500,
                "pvp_fr grades or honour bounds are not read");
            Check(AlignmentTexts.ZoneName(8) == "Voisine fictive" && AlignmentTexts.ZoneName(99) == "Sous-zone 99" && AlignmentTexts.AreaName(3) == "Zone fictive"
                && AlignmentTexts.Neighbours(7).SequenceEqual(new[] { 8, 9 }) && AlignmentTexts.Neighbours(99).Length == 0 && AlignmentTexts.AreaOfZone(9) == 4
                && AlignmentTexts.MapLabel(900093).Contains("[3,-4]") && AlignmentTexts.SpecializationName(1) == "Spécialisation fictive", "maps_fr helpers are wrong");

            using (var account = new Accounts(new AccountConfig("synthetic-alignment", "synthetic", "loopback")))
            {
                account.Logger.log_event += (entry, color) => logs.Enqueue(entry.message);
                AlignmentActions alignment = account.Game.Interactions.Alignment;
                Alignment me = alignment.Alignment;
                Check(alignment != null && !me.IsAligned && me.Side == 0 && me.Grade == 0 && !me.WingsEnabled && alignment.Prism != null && !alignment.Prism.IsOpen, "Interactions.Alignment is missing or not neutral");
                Check(!Sent(alignment.EnableWingsAsync()) && !Sent(alignment.RequestBalanceAsync()) && !Sent(alignment.Prism.TeleportAsync(1)), "Actions were accepted before the connection");

                Task<Socket> accept = listener.AcceptSocketAsync();
                Complete(account.Connexion.ConnectToServer(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port)); Complete(accept);
                using (Socket peer = accept.Result)
                {
                    peer.ReceiveTimeout = 6000;
                    account.Game.character.SetPerso_Data(42, "Personnage de test", 25, 0, 8);
                    Feed(account, "GDM|900092|date|key"); Check(Read(peer) == "GI", "GDM did not request GI");
                    // Soi (sprite GM aligné Bonta, lu par le lot M4 pour « Attaquer »), un prisme de Brâkmar (71) et un prisme de Bonta (72) : format GM de Prism.getGMPrisme.
                    Feed(account, "GM|+0;1;0;42;Personnage de test;1;100^100;0;1,10,2,32|+13;1;0;71;1112;-10;8101^100;4;2;2|+14;1;0;72;1111;-10;8100^100;4;2;1");
                    account.AccountStates = AccountStates.CONNECTED_INACTIVE;
                    var ownPrism = account.Game.Map.GetActor(OwnPrism) as PrismActor; var enemyPrism = account.Game.Map.GetActor(EnemyPrism) as PrismActor;
                    Check(ownPrism != null && enemyPrism != null && ownPrism.AlignmentSide == 1 && enemyPrism.AlignmentSide == 2, "Synthetic prisms are missing");
                    int changes = 0; Action counted = () => Interlocked.Increment(ref changes);
                    var wingsQuestions = new List<int>(); Action<int> asked = wingsQuestions.Add;
                    var alerts = new List<string>(); Action<string> alert = alerts.Add;
                    var specializations = new List<string>(); Action<string> specialized = specializations.Add;
                    int zoneChanges = 0, worldChanges = 0, defenseChanges = 0, defenseClosed = 0, balanceChanges = 0, bonusChanges = 0;
                    Action zonesChanged = () => Interlocked.Increment(ref zoneChanges), worldChanged = () => Interlocked.Increment(ref worldChanges), defenseChanged = () => Interlocked.Increment(ref defenseChanges);
                    Action closed = () => Interlocked.Increment(ref defenseClosed), balanced = () => Interlocked.Increment(ref balanceChanges), bonused = () => Interlocked.Increment(ref bonusChanges);
                    alignment.Changed += counted; alignment.WingsDisableAsked += asked; alignment.Alert += alert; alignment.SpecializationChanged += specialized;
                    alignment.ZonesChanged += zonesChanged; alignment.WorldDataChanged += worldChanged; alignment.DefenseChanged += defenseChanged; alignment.DefenseClosed += closed;
                    alignment.BalanceChanged += balanced; alignment.BonusChanged += bonused;
                    var pvp = new List<ServerMessage>(); Action<ServerMessage> onMessage = message => { if (message.Kind == ServerMessageKind.Pvp) pvp.Add(message); };
                    account.Game.Session.ServerMessageReceived += onMessage;

                    // Neutre : rien n'est envoyé (le client grise les entrées, StarLoco ignore GP et GA512 d'un neutre).
                    Check(!alignment.CanUsePrism(ownPrism) && !Sent(alignment.UsePrismAsync(OwnPrism)) && !Sent(alignment.EnableWingsAsync()) && !Sent(alignment.AskDisableWingsAsync())
                        && !Sent(alignment.DisableWingsAsync()) && !Sent(alignment.JoinPrismFightAsync()), "A neutral character sent alignment packets");
                    NoPacket(peer, "A refused action reached the server");

                    // As : camp, grade, honneur, déshonneur, ailes lus comme Player.getAsPacket ; textes de pvp_fr.
                    Feed(account, As(1, 2, 600, 1, 1));
                    Check(me.IsAligned && me.Side == 1 && me.Value == 10 && me.Grade == 2 && me.Honor == 600 && me.Dishonor == 1 && me.WingsEnabled && me.Name == "Bonta (test)"
                        && me.GradeName == "Aspirant (test)" && me.HonorMin == 500 && me.HonorMax == 1500 && me.MaxDishonor == 500 && me.HonorLossOnDisable == 30, "As is not read into the alignment model: " + me);

                    // ZS<id> / ZC<id> (StarLoco y met l'identifiant du camp) : spécialisation ; ZC annoncé.
                    Feed(account, "ZS1"); Check(me.SpecializationId == 1 && me.SpecializationName == "Spécialisation fictive" && specializations.Count == 0, "ZS is not read silently");
                    Feed(account, "ZS"); Check(me.SpecializationId == 0 && me.SpecializationName == "", "Empty ZS did not mean no specialization");
                    Feed(account, "ZC1");
                    Check(me.SpecializationId == 1 && specializations.Count == 1 && specializations[0] == "Spécialisation : Spécialisation fictive (test)" && Chat(account, ChatMessageKind.Info, "Spécialisation fictive"),
                        "ZC is not announced");

                    // al|<id>;<camp>|… puis am<id>|<camp>|<1 silencieux|0 annoncé> et aM<zone>|<camp> dans le canal d'alignement (PVP_CHAT).
                    Feed(account, "al|7;1|8;-1|9;2");
                    Check(alignment.ZoneSides.Count == 3 && alignment.ZoneSide(7) == 1 && alignment.ZoneSide(8) == 0 && alignment.ZoneSide(9) == 2 && alignment.ZoneSide(99) == 0 && zoneChanges == 1, "al is not read like Subareas.onList");
                    Feed(account, "am8|2|1"); Check(alignment.ZoneSide(8) == 2 && pvp.Count == 0 && alerts.Count == 0 && zoneChanges == 2, "Silent am was announced");
                    Feed(account, "am8|-1|0");
                    Check(alignment.ZoneSide(8) == 0 && pvp.Count == 1 && pvp[0].Text == "Prisme de Voisine fictive retiré (test)" && pvp[0].Resolved && alerts.Count == 1
                        && ReferenceEquals(account.Game.Session.LastServerMessage, pvp[0]), "am -1 is not announced as SUBAREA_ALIGNMENT_PRISM_REMOVED");
                    Feed(account, "am9|1|0"); Check(pvp.Count == 2 && pvp[1].Text == "Lointaine fictive appartient à Bonta (test) (test)", "am is not announced as SUBAREA_ALIGNMENT_IS");
                    Feed(account, "aM3|2"); Check(alignment.AreaSides[3] == 2 && pvp.Count == 3 && pvp[2].Text == "Zone Zone fictive appartient à Brâkmar (test) (test)", "aM is not announced as AREA_ALIGNMENT_IS");
                    Feed(account, "aM3|-1"); Check(pvp.Count == 4 && pvp[3].Text == "Prisme de la zone Zone fictive retiré (test)", "aM -1 is not announced");

                    // Ailes : GP* (demande du serveur) ; GIP<honneur> n'est jamais répondu seul ; Oui → GP-, Non → rien ; GP+ après l'activation.
                    Check(!Sent(alignment.EnableWingsAsync()) && Sent(alignment.AskDisableWingsAsync()) && Read(peer) == "GP*", "Disable does not ask the server with GP*");
                    Feed(account, "GIP120");
                    Check(alignment.PendingWingsLoss == 120 && wingsQuestions.Count == 1 && wingsQuestions[0] == 120 && alignment.LastMessage == "Tu perdras 120 points d'honneur (test)", "GIP is not raised as a question");
                    NoPacket(peer, "GIP was answered automatically");
                    Check(Sent(alignment.DisableWingsAsync()) && Read(peer) == "GP-" && alignment.PendingWingsLoss == null, "Yes does not send GP-");
                    Feed(account, "GIP30"); alignment.CancelWingsDisable(); Check(alignment.PendingWingsLoss == null, "No did not forget the question"); NoPacket(peer, "No sent a packet");
                    Feed(account, As(1, 2, 600, 1, 0));
                    Check(!me.WingsEnabled && !Sent(alignment.AskDisableWingsAsync()) && !Sent(alignment.DisableWingsAsync()) && Sent(alignment.EnableWingsAsync()) && Read(peer) == "GP+", "Enable does not send GP+ once the wings are down");
                    NoPacket(peer, "A refused wings action reached the server");

                    // Cb<monde>;<zone> et CB<bonus>;<multiplicateur>;<malus>.
                    Check(Sent(alignment.RequestBalanceAsync()) && Read(peer) == "Cb" && Sent(alignment.RequestBonusAsync()) && Read(peer) == "CB", "Balance or bonus requests differ");
                    Feed(account, "Cb2.5;-1"); Check(alignment.WorldBalance == 2.5 && alignment.AreaBalance == -1 && balanceChanges == 1, "Cb is not read");
                    Feed(account, "CB10,20,30;1.5,2,2.5;5,6,7");
                    ConquestBonus bonus = alignment.Bonus;
                    Check(bonus != null && bonus.AlignBonus.Xp == 10 && bonus.AlignBonus.Collect == 30 && bonus.RankMultiplier.Drop == 2 && bonus.AlignMalus.Collect == 7 && bonusChanges == 1, "CB is not read like onConquestBonus");

                    // CWJ → CW (forme alignée) : compteurs, sous-zones triées par nom, villages ; filtres du client ; forme neutre courte ; CWV.
                    Check(Sent(alignment.JoinWorldInfosAsync()) && Read(peer) == "CWJ", "Join world infos does not send CWJ"); PumpUntil(() => alignment.WorldInfosJoined);
                    Feed(account, WorldData);
                    ConquestWorld world = alignment.World;
                    Check(worldChanges == 1 && !world.CountsUnknown && world.OwnedAreas == 1 && world.TotalAreas == 3 && world.PossibleAreas == 2 && world.OwnedVillages == 0 && world.TotalVillages == 1
                        && world.Zones.Count == 3 && world.Zones[0].Id == 9 && world.FindZone(7).Side == 1 && world.FindZone(7).PrismMapId == 900092 && world.FindZone(8).Side == 0 && !world.FindZone(8).HasPrism
                        && world.FindZone(9).Fighting && world.FindZone(9).Attackable && world.FindZone(9).AreaId == 4 && world.Villages.Count == 1 && world.FindVillage(3).Side == 2 && world.FindVillage(3).HasPrism,
                        "CW is not read like Conquest.onWorldData");
                    ConquestZone zone7 = world.FindZone(7), zone9 = world.FindZone(9);
                    Check(zone9.IsCapturable(world, 1) && !zone7.IsCapturable(world, 1) && zone7.IsVulnerable(world, 1) && !zone9.IsVulnerable(world, 1) && !zone9.IsCapturable(world, 0), "isCapturable / isVulnerable differ from the client");
                    Check(ConquestPanel.Matches(zone7, world, ConquestPanel.FilterVulnerable, 1) && ConquestPanel.Matches(zone9, world, ConquestPanel.FilterCapturable, 1)
                        && ConquestPanel.Matches(zone9, world, ConquestPanel.FilterHostile, 1) && !ConquestPanel.Matches(zone7, world, ConquestPanel.FilterHostile, 1)
                        && ConquestPanel.Matches(zone7, world, 1, 1) && !ConquestPanel.Matches(zone7, world, 2, 1) && ConquestPanel.Matches(zone7, world, ConquestPanel.FilterAll, 1), "Zone filters differ");
                    IReadOnlyList<WorldMapHint> hints = ConquestPanel.PrismHints(world);
                    Check(hints.Count == 2 && hints.All(hint => hint.Category == ConquestPanel.PrismHintCategory) && hints.Any(hint => hint.Gfx == 420 && hint.X == 2 && hint.Y == -4)
                        && hints.Any(hint => hint.Gfx == 421 && hint.X == 5 && hint.Y == -4), "Prism hints for the world map differ");
                    Feed(account, "CW7,1,0,900092,1|3|3,2,1,1");
                    Check(alignment.World.CountsUnknown && alignment.World.TotalAreas == 3 && alignment.World.Zones.Count == 1 && alignment.World.Villages.Count == 1, "Neutral CW form is not read");
                    Feed(account, "CW"); Check(alignment.World.Zones.Count == 1, "Malformed CW replaced the data");
                    Check(Sent(alignment.LeaveWorldInfosAsync()) && Read(peer) == "CWV", "Leave world infos does not send CWV"); PumpUntil(() => !alignment.WorldInfosJoined);

                    // CIJ : CP / Cp (base 36) avant CIJ<0;délai;durée;places> ; CFJ ; CP- ; CIV seulement quand CIJ a répondu 0 ; CIJ-3 ; CIV reçu.
                    Check(Sent(alignment.JoinPrismInfosAsync()) && Read(peer) == "CIJ", "Join prism infos does not send CIJ"); PumpUntil(() => alignment.PrismInfosJoined);
                    Check(!Sent(alignment.JoinPrismFightAsync()), "Join was accepted before CIJ");
                    Feed(account, Defenders); Feed(account, Attackers);
                    PrismDefense defense = alignment.Defense;
                    Check(defense.Defenders.Count == 2 && defense.Defenders[0].Id == 42 && defense.Defenders[0].Name == "Défenseur fictif" && defense.Defenders[0].Gfx == 10 && defense.Defenders[0].Level == 60
                        && defense.Defenders[0].Color1 == 1 && defense.Defenders[0].Color3 == 3 && !defense.Defenders[0].Reservist && defense.Defenders[1].Id == 43 && defense.Defenders[1].Reservist
                        && defense.Attackers.Count == 1 && defense.Attackers[0].Id == 44 && defense.Attackers[0].Level == 50 && !defense.Attackers[0].IsDefender, "CP / Cp are not read like onPrismFightAddPlayer / Enemy");
                    Feed(account, "CIJ0;30000;45000;7");
                    Check(defense.Error == 0 && defense.IsJoinable && defense.Timer == 30000 && defense.MaxTimer == 45000 && defense.MaxTeamPositions == 7 && defense.RemainingMilliseconds > 20000 && defense.RemainingMilliseconds <= 30000,
                        "CIJ0 is not read");
                    Check(Sent(alignment.JoinPrismFightAsync()) && Read(peer) == "CFJ", "Join does not send CFJ");
                    Feed(account, "CP-1z|16"); Check(defense.Defenders.Count == 1 && defense.Defenders[0].Id == 43, "CP- did not remove the defender");
                    Feed(account, "Cp-1z|18"); Check(defense.Attackers.Count == 0, "Cp- did not remove the attacker");
                    Check(Sent(alignment.LeavePrismInfosAsync()) && Read(peer) == "CIV" && !alignment.PrismInfosJoined && defense.Error == null && defense.Defenders.Count == 0, "Leave does not send CIV");
                    Check(Sent(alignment.JoinPrismInfosAsync()) && Read(peer) == "CIJ", "Join prism infos does not send CIJ"); PumpUntil(() => alignment.PrismInfosJoined);
                    Feed(account, "CIJ-3");
                    Check(defense.Error == -3 && !defense.IsJoinable && alignment.LastMessage == "Aucun prisme (test)" && !Sent(alignment.JoinPrismFightAsync()), "CIJ-3 is not read");
                    Check(!Sent(alignment.LeavePrismInfosAsync()) && !alignment.PrismInfosJoined, "Leaving after an error still sent CIV");
                    NoPacket(peer, "CIV was sent after a CIJ error (the client does not unsubscribe)");
                    Feed(account, "CIJ-1"); Feed(account, "CIJ-2");
                    Check(alignment.LastMessage == "Déjà en combat (test)" && AlignmentActions.JoinErrorText(-1) == "Aucun combat (test)", "CIJ error texts differ");
                    Feed(account, "CIV"); Check(defenseClosed == 1 && defense.Error == null, "CIV did not close the defence");

                    // CA / CS / CD <carte>|<x>|<y> : sous-zone de la carte (maps_fr), « // » retiré, PVP_CHAT.
                    int pvpBefore = pvp.Count;
                    Feed(account, "CA900092|2|-4"); Feed(account, "CS900093|3|-4"); Feed(account, "CD900094|5|-4");
                    Check(pvp.Count == pvpBefore + 3 && pvp[pvpBefore].Text == "Prisme de Sous-zone fictive attaqué en [2, -4] (test)" && pvp[pvpBefore + 1].Text == "Prisme de Voisine fictive en [3, -4] a survécu (test)"
                        && pvp[pvpBefore + 2].Text == "Prisme de Lointaine fictive en [5, -4] détruit (test)" && alerts.Count == pvpBefore + 3, "CA / CS / CD are not announced like the client");
                    Feed(account, "CA123456|1|2"); Check(pvp.Last().Text.Contains("Carte 123456"), "Unknown map alert has no fallback");

                    // GA512<id> : seulement un prisme de son camp ; Wp → fenêtre (état ZAAP) ; Wp<carte> et ses refus ; Ww.
                    PrismTravel prism = alignment.Prism;
                    Check(alignment.CanUsePrism(ownPrism) && !alignment.CanUsePrism(enemyPrism) && !Sent(alignment.UsePrismAsync(EnemyPrism)) && !Sent(alignment.UsePrismAsync(999)), "A foreign or missing prism was used");
                    NoPacket(peer, "A refused prism use reached the server");
                    Check(Sent(alignment.UsePrismAsync(OwnPrism)) && Read(peer) == "GA51272" && !prism.IsOpen, "Use does not send GA512<id>");
                    Check(!Sent(prism.TeleportAsync(900093)) && !Sent(prism.LeaveAsync()), "The prism list acted before Wp");
                    Feed(account, PrismList);
                    Check(prism.IsOpen && prism.CurrentMapId == 900092 && prism.Destinations.Count == 3 && prism.Destinations[0].IsCurrent && prism.Destinations[0].Cost == 0
                        && prism.Destinations[1].Cost == 150 && prism.Destinations[1].Label.Contains("[3,-4]") && prism.Destinations[2].InFight && account.AccountStates == AccountStates.ZAAP, "Wp is not read like Subway.onPrismCreate");
                    Check(!Sent(prism.TeleportAsync(900092)) && !Sent(prism.TeleportAsync(900094)) && !Sent(prism.TeleportAsync(123)), "Current, fighting or unknown prisms were accepted");
                    account.Game.character.Kamas = 100;
                    Check(!Sent(prism.TeleportAsync(900093)) && Result(prism.TeleportAsync(900093)).Message.StartsWith("Pas assez de kamas (test)"), "Insufficient kamas were accepted");
                    account.Game.character.Kamas = 1000;
                    NoPacket(peer, "A refused teleport reached the server");
                    Check(Sent(prism.TeleportAsync(900093)) && Read(peer) == "Wp900093" && prism.IsOpen, "Teleport does not send Wp<map>");
                    Check(Sent(prism.LeaveAsync()) && Read(peer) == "Ww" && prism.IsOpen, "Leave does not send Ww or closed before the server");
                    Feed(account, "Ww"); Check(!prism.IsOpen && prism.Destinations.Count == 0 && account.AccountStates == AccountStates.CONNECTED_INACTIVE, "Ww did not close the list");
                    Feed(account, "Ww"); Check(!prism.IsOpen, "Second Ww broke the state");
                    // Liste sans entrée lisible : fenêtre vide, comme Subway.onPrismCreate ; Ww la ferme.
                    Feed(account, "Wp900092|x;y"); Check(prism.IsOpen && prism.Destinations.Count == 0 && !Sent(prism.TeleportAsync(900093)), "An unreadable prism list was not opened empty");
                    Feed(account, "Ww"); Check(!prism.IsOpen, "Ww did not close the empty list");

                    // Paquets mal formés : journalisés comme illisibles au besoin, jamais propagés.
                    int before = logs.Count(entry => entry.Contains("illisible ignoré :"));
                    foreach (string broken in new[] { "ZSx", "ZCx", "al|x", "al|7", "al|7;x", "am", "am7", "amx|1|0", "aM", "aMx|1", "GIP", "GIPx", "Cb", "Cbx;y", "CB", "CB1,2;3", "CB1,2,3;4,5,6;x,y,z",
                        "CW", "CW1|2", "Cp", "Cpx", "CP", "CP+", "CP+zz|", "CIJ", "CIJx", "CIJ0;1", "CA", "CAx|1|2", "CD1", "CS", "Wp", "Wpx", "C", "a", "Z", "W" })
                        Feed(account, broken);
                    NoPacket(peer, "A malformed packet was answered");
                    Check(logs.Count(entry => entry.Contains("illisible ignoré :")) > before, "Malformed packets were not logged");
                    Check(!logs.Any(entry => entry.Contains("illisible ignoré (")), "An alignment packet threw: " + string.Join(" / ", logs.Where(entry => entry.Contains("illisible ignoré ("))));
                    Check(me.Side == 1 && alignment.ZoneSides.Count == 3 && !prism.IsOpen && changes > 40, "Malformed packets changed the state");
                    alignment.Changed -= counted; alignment.WingsDisableAsked -= asked; alignment.Alert -= alert; alignment.SpecializationChanged -= specialized;
                    alignment.ZonesChanged -= zonesChanged; alignment.WorldDataChanged -= worldChanged; alignment.DefenseChanged -= defenseChanged; alignment.DefenseClosed -= closed;
                    alignment.BalanceChanged -= balanced; alignment.BonusChanged -= bonused;
                    account.Game.Session.ServerMessageReceived -= onMessage;

                    // Volet Conquête : icône d'alignement synthétique lue hors du fil de l'interface.
                    string images = Path.Combine(folder, "images");
                    Color sideColor = Color.FromArgb(255, 200, 40, 160);
                    Png(Path.Combine(images, "Alignments"), "1", sideColor, 66, 66);
                    AssetsRoot(images);

                    using (var form = new GameClientFullform(account))
                    {
                        form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); Application.DoEvents();
                        var drawer = form.Panels; var panel = drawer.Get<ConquestPanel>(); var prismPanel = drawer.Get<PrismTravelPanel>(); var worldMap = drawer.Get<WorldMapPanel>();
                        Check(panel != null && prismPanel != null && worldMap != null && drawer.Current == null && panel.Title == "Conquête (test)" && prismPanel.Title == "Prismes (test)",
                            "Conquest panels are not registered with the client titles");
                        NoPacket(peer, "The game window sent an alignment packet before the panel was opened");
                        Control worldMapView = worldMap.View; // la carte du monde reçoit les prismes de CW même fermée
                        // Ouverture (initData) : Cb puis l'onglet Statistiques (CB) ; en-tête ; ailes.
                        Check(drawer.Open("Conquest"), "Find(\"Conquest\") does not resolve the panel"); PumpUntil(() => drawer.Current == panel);
                        ReadPair(peer, "Cb", "CB", "Opening the panel does not send Cb and CB");
                        Check(panel.CurrentTab == ConquestTab.Stats && Find(form, "conquest-name").Text.StartsWith("Bonta (test)") && Find(form, "conquest-grade").Text.Contains("Aspirant (test)")
                            && Find(form, "conquest-pvp").Text.Contains("Inactif (test)") && Find(form, "conquest-wings").Text == "Activer (test)" && Find(form, "conquest-honor").Text.Contains("600"),
                            "Header or stats labels are wrong: " + Find(form, "conquest-grade").Text + " / " + Find(form, "conquest-pvp").Text);
                        var sideBox = (PictureBox)Find(form, "conquest-side"); PumpUntil(() => sideBox.Image != null);
                        using (var image = new Bitmap(sideBox.Image)) Check(image.Width == 66 && image.GetPixel(33, 33).ToArgb() == sideColor.ToArgb(), "Alignment icon is not the synthetic image");
                        FeedFromNetwork(account, "Cb2.5;-1"); PumpUntil(() => Find(form, "conquest-balance").Text.Contains("2.5") || Find(form, "conquest-balance").Text.Contains("2,5"));
                        FeedFromNetwork(account, "CB10,20,30;1.5,2,2.5;5,6,7"); PumpUntil(() => Find(form, "conquest-bonus-0").Text.StartsWith("+15"));
                        Check(Find(form, "conquest-bonus-0").Text.Contains("10") && Find(form, "conquest-malus-2").Text.StartsWith("7"), "Bonus table differs: " + Find(form, "conquest-bonus-0").Text);
                        Click(Find(form, "conquest-wings")); Form ask = Dialog();
                        Check(Message(ask) == "Activer le PvP ? (test)", "ASK_ENABLED_PVP box differs: " + Message(ask));
                        Answer(ask, "Non"); NoPacket(peer, "Refusing to enable the wings sent a packet");
                        Click(Find(form, "conquest-wings")); Answer(Dialog(), "Oui"); Check(Read(peer) == "GP+", "Yes does not send GP+");
                        FeedFromNetwork(account, As(1, 2, 600, 1, 1)); PumpUntil(() => Find(form, "conquest-wings").Text == "Désactiver (test)");
                        Check(Find(form, "conquest-pvp").Text.Contains("Actif (test)"), "As did not refresh the PvP state");
                        Click(Find(form, "conquest-wings")); Check(Read(peer) == "GP*", "Disable does not send GP*");
                        NoPacket(peer, "GP- was sent before GIP");
                        FeedFromNetwork(account, "GIP30"); PumpUntil(() => panel.WingsDialog != null);
                        Check(Message(panel.WingsDialog) == "Tu perdras 30 points d'honneur (test)", "ASK_DISABLE_PVP box differs: " + Message(panel.WingsDialog));
                        Answer(panel.WingsDialog, "Non"); PumpUntil(() => panel.WingsDialog == null && alignment.PendingWingsLoss == null);
                        NoPacket(peer, "Refusing the honour loss sent a packet");
                        Click(Find(form, "conquest-wings")); Check(Read(peer) == "GP*", "Second disable does not send GP*");
                        FeedFromNetwork(account, "GIP30"); PumpUntil(() => panel.WingsDialog != null);
                        Answer(panel.WingsDialog, "Oui"); Check(Read(peer) == "GP-", "Yes does not send GP- after GIP");

                        // Onglet Zones : CWJ ; CW remplit les listes (filtre du camp puis toutes) et la carte du monde ; Défendre : CWV puis CIJ.
                        panel.Tabs.ClickTab(ConquestTab.Zones); Check(Read(peer) == "CWJ" && panel.CurrentTab == ConquestTab.Zones, "Zones tab does not send CWJ");
                        PumpUntil(() => alignment.WorldInfosJoined);
                        FeedFromNetwork(account, WorldData); PumpUntil(() => panel.ZoneList.Items.Count == 1 && panel.VillageList.Items.Count == 1);
                        Check(panel.ZoneList.Items[0].Text == "Sous-zone fictive" && Find(form, "conquest-zone-counts").Text.Contains("1 / 2 / 3") && Find(form, "conquest-village-counts").Text.Contains("0 / 1"),
                            "Zones tab is not filled: " + Find(form, "conquest-zone-counts").Text);
                        panel.Filter.SelectedIndex = panel.Filter.Items.Count - 1; Application.DoEvents();
                        Check(panel.ZoneList.Items.Count == 3 && panel.ZoneList.Items[0].Text == "Lointaine fictive", "The « all » filter does not list every zone");
                        var mapView = (WorldMapView)Get(worldMap, "view"); PumpUntil(() => mapView.Prisms.Count == 2);
                        panel.Tabs.ClickTab(ConquestTab.Join); ReadPair(peer, "CWV", "CIJ", "Join tab does not leave the zones (CWV) and ask CIJ");
                        PumpUntil(() => alignment.PrismInfosJoined);
                        FeedFromNetwork(account, Defenders); FeedFromNetwork(account, Attackers); FeedFromNetwork(account, "CIJ0;30000;45000;7");
                        PumpUntil(() => panel.DefenderList.Items.Count == 2 && panel.AttackerList.Items.Count == 1 && Find(form, "conquest-join").Enabled);
                        Check(Find(form, "conquest-join-state").Text.Contains("7") && Find(form, "conquest-join-timer").Text.Contains("min"), "Join tab is not filled: " + Find(form, "conquest-join-state").Text);
                        Click(Find(form, "conquest-join")); Check(Read(peer) == "CFJ", "Rejoindre does not send CFJ");
                        drawer.CloseAll(); Check(Read(peer) == "CIV" && !drawer.IsOpen(panel), "Closing the panel does not send CIV");
                        // Réouverture sur Défendre : Cb puis CIJ ; CIJ-3 → pas de CIV à la fermeture ; CIV reçu ferme le volet.
                        drawer.Show(panel); PumpUntil(() => drawer.Current == panel); ReadPair(peer, "Cb", "CIJ", "Reopening does not send Cb and CIJ");
                        PumpUntil(() => alignment.PrismInfosJoined);
                        FeedFromNetwork(account, "CIJ-3"); PumpUntil(() => Find(form, "conquest-join-state").Text == "Aucun prisme (test)");
                        Check(!Find(form, "conquest-join").Enabled, "Join is enabled without a prism");
                        drawer.CloseAll(); NoPacket(peer, "CIV was sent after a CIJ error");
                        drawer.Show(panel); PumpUntil(() => drawer.Current == panel); ReadPair(peer, "Cb", "CIJ", "Third opening differs");
                        FeedFromNetwork(account, "CIV"); PumpUntil(() => !drawer.IsOpen(panel)); NoPacket(peer, "CIV from the server was answered");

                        // Menu d'un prisme : Utiliser (GA512, par défaut) pour le sien, Attaquer (GA912) pour l'autre ; Wp ouvre la liste → Wp<carte> ; Ww la ferme.
                        var provider = ActorMenuRegistry.Default.Providers.OfType<PrismMenuProvider>().Single();
                        Check(provider.Handles(ownPrism) && !provider.Handles(account.Game.character), "Prism menu provider accepts the wrong actors");
                        List<MenuEntry> entries = provider.Entries(ownPrism, account.Game).ToList();
                        Check(entries.Select(entry => entry.Texte).SequenceEqual(new[] { "Utiliser (test)", "Attaquer (test)" }) && entries[0].Activé && entries[0].ParDéfaut && !entries[1].Activé
                            && entries[0].Infobulle.Contains("GA51272"), "Own prism menu differs: " + string.Join("|", entries.Select(entry => entry.Texte)));
                        List<MenuEntry> enemyEntries = provider.Entries(enemyPrism, account.Game).ToList();
                        Check(!enemyEntries[0].Activé && !enemyEntries[0].ParDéfaut && enemyEntries[1].Activé && enemyEntries[1].Infobulle.Contains("GA91271"), "Enemy prism menu differs");
                        Check(ActorMenuRegistry.Default.EntriesFor(ownPrism, account.Game).Count(entry => entry.Texte == "Utiliser (test)") == 1, "Use entry is missing from the merged menu");
                        var context = new ActorMenuContext(ownPrism, null, -1, account, null, drawer, Keys.None);
                        Check(Result(entries[0].Action(context)) != null && Read(peer) == "GA51272", "Use entry does not send GA512<id>");
                        FeedFromNetwork(account, PrismList); PumpUntil(() => drawer.Current == prismPanel && prismPanel.List.Items.Count == 3);
                        Check(prismPanel.IsServerWindowOpen && !Find(form, "prism-use").Enabled && Find(form, "prism-leave").Enabled, "Prism list did not open");
                        prismPanel.List.Items[2].Selected = true; Application.DoEvents(); Check(!Find(form, "prism-use").Enabled, "A fighting prism can be used");
                        prismPanel.List.Items[1].Selected = true; Application.DoEvents(); Check(Find(form, "prism-use").Enabled, "Use is not enabled for a valid prism");
                        Click(Find(form, "prism-use")); Check(Read(peer) == "Wp900093", "Use does not send Wp<map>");
                        FeedFromNetwork(account, "Ww"); PumpUntil(() => !prism.IsOpen && drawer.Current != prismPanel);
                        Check(Result(enemyEntries[1].Action(new ActorMenuContext(enemyPrism, null, -1, account, null, drawer, Keys.None))) != null && Read(peer) == "GA91271", "Attack entry does not send GA912<id>");
                        Check(Result(entries[1].Action(context)) != null, "Attack on own prism threw"); NoPacket(peer, "Attacking the own prism sent a packet");
                        // Carte du monde ouverte après le CW : ses prismes sont reposés à l'ouverture (le volet carte envoie lui-même CWJ / CWV).
                        mapView.SetPrisms(null); Check(mapView.Prisms.Count == 0, "SetPrisms(null) did not clear the prisms");
                        drawer.Show(worldMap); PumpUntil(() => drawer.Current == worldMap);
                        Check(Read(peer) == "CWJ" && mapView.Prisms.Count == 2, "Opening the world map does not restore the prisms of the last CW");
                        drawer.CloseAll(); Check(Read(peer) == "CWV", "Closing the world map does not send CWV");
                        form.Close();
                    }
                    Check(BotDialogs.OpenDialogs.Count == 0, "A dialog survived the game window");
                    Check(!logs.Any(entry => entry.Contains("illisible ignoré (")), "An alignment packet threw in the window: " + string.Join(" / ", logs.Where(entry => entry.Contains("illisible ignoré ("))));
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
