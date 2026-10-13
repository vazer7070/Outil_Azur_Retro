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
using Outil_Azur_complet.Bot.Panels;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Perso;
using Tool_BotProtocol.Game.Quetes;

// Quêtes et titres selon dofus.aks.Quests (client 1.34) et Quest.getGmQuestDataPacket / Player.getQuestGmPacket de StarLoco, sur un
// serveur fictif local : refus hors connexion, QL envoyé, QL+1;0|2;1 (StarLoco) et QL1;0|2;1 (forme lue par le client) → deux quêtes,
// QS<id> sans décalage, refus d'une quête absente, QS reçu (étape, objectifs, hiérarchie, question du PNJ), textes Q.q/Q.s/Q.o/Q.t et
// récompenses lus dans des XML synthétiques, Im054/055/056 sans paquet, paquets mal formés sans exception ; titres (Titles.Name/Color
// avec repli) ; puis le volet Quêtes (bouton du bandeau, QL à l'ouverture, case des quêtes terminées, QS au clic, onglets, pixels
// témoins des icônes lues dans un dossier temporaire, boussole d'un objectif, dialogue de l'étape, bandeau « à actualiser ») et le
// titre porté dans la fiche des caractéristiques.
internal static class BotQuestsSmoke
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
        try { Application.EnableVisualStyles(); Run(); Console.WriteLine("OK: QL, QS, Im054/055/056, textes des quêtes, titres, volet Quêtes et titre de la fiche"); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static IEnumerable<Control> All(Control value) { yield return value; foreach (Control child in value.Controls) foreach (var nested in All(child)) yield return nested; }
    private static void PumpUntil(Func<bool> done, int seconds = 6)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(seconds);
        while (!done()) { if (DateTime.UtcNow > end) throw new TimeoutException("Quests loopback timed out"); Application.DoEvents(); Thread.Sleep(5); }
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
    private static bool Near(Color a, Color b) => Math.Abs(a.R - b.R) <= 12 && Math.Abs(a.G - b.G) <= 12 && Math.Abs(a.B - b.B) <= 12;
    private static void Png(string folder, string name, Color color, int width = 16, int height = 16)
    {
        Directory.CreateDirectory(folder);
        using (var image = new Bitmap(width, height)) { using (var graphics = Graphics.FromImage(image)) graphics.Clear(color); image.Save(Path.Combine(folder, name + ".png"), ImageFormat.Png); }
    }
    private static Bitmap Snapshot(Control control)
    {
        var image = new Bitmap(Math.Max(1, control.Width), Math.Max(1, control.Height));
        control.DrawToBitmap(image, new Rectangle(Point.Empty, control.Size));
        return image;
    }
    /// <summary>Couleur au centre de l'icône de la ligne (colonne de 30 px, image de 16 px à x = 7).</summary>
    private static Color IconPixel(QuestRowList list, int index)
    {
        using (Bitmap image = Snapshot(list)) return image.GetPixel(15, list.RowBounds(index).Y + Math.Min(list.RowBounds(index).Height, QuestRowList.MinRowHeight) / 2);
    }

    // ClientAssets est interne à l'application : sa racine (dossier temporaire du test) et son cache passent par la réflexion.
    private static Type Assets => typeof(LoginForm).Assembly.GetType("Outil_Azur_complet.Bot.ClientAssets");
    private static void AssetsRoot(string value) => Assets.GetProperty("Root", Any).SetValue(null, value, null);
    private static bool Cached(string family, string name)
    {
        var args = new object[] { family, name, null };
        return (bool)Assets.GetMethod("TryCached", Any).Invoke(null, args) && args[2] != null;
    }

    private static void Xml(string folder, string family, string body) =>
        File.WriteAllText(Path.Combine(folder, family + ".xml"), "<?xml version='1.0' encoding='utf-8'?>\n<BotLang famille=\"" + family + "\" langue=\"fr\" version=\"1\" source=\""
            + family + "_fr_1.swf\">\n" + body + "</BotLang>\n", new UTF8Encoding(false));

    /// <summary>Textes synthétiques au format des exports BotLang (aucun texte du client).</summary>
    private static void WriteTexts(string folder)
    {
        Directory.CreateDirectory(folder);
        Xml(folder, "lang", "<texte cle=\"QUESTS_LIST\" valeur=\"Liste des quêtes (test)\" />\n<texte cle=\"PENDING_QUEST\" valeur=\"%1 quête{~ps} en cours (test)\" />\n"
            + "<texte cle=\"INFOS_54\" valeur=\"Nouvelle quête (test) : &lt;b&gt;%1&lt;/b&gt;\" />\n<texte cle=\"INFOS_55\" valeur=\"Quête mise à jour (test) : &lt;b&gt;%1&lt;/b&gt;\" />\n"
            + "<texte cle=\"INFOS_56\" valeur=\"Quête terminée (test) : &lt;b&gt;%1&lt;/b&gt;\" />\n<texte cle=\"STEP\" valeur=\"Étape (test)\" />\n"
            + "<texte cle=\"STEP_DIALOG\" valeur=\"Dialogue de l'étape (test)\" />\n<texte cle=\"DISPLAY_FINISHED_QUESTS\" valeur=\"Terminées aussi (test)\" />\n"
            + "<texte cle=\"QUESTS_CURRENT_STEP\" valeur=\"Courante (test)\" />\n<texte cle=\"QUESTS_STEPS_LIST\" valeur=\"Hiérarchie (test)\" />\n");
        Xml(folder, "quests", "<quete id=\"1\" nom=\"Quête fictive un\" />\n<quete id=\"2\" nom=\"Quête fictive deux\" />\n<quete id=\"3\" nom=\"Quête fictive trois\" />\n"
            + "<etape id=\"9\" nom=\"Étape fictive neuf\" description=\"Description fictive neuf.\" recompenses=\"[100,null,null,null,null,null]\" />\n"
            + "<etape id=\"10\" nom=\"Étape fictive dix\" description=\"Description fictive dix.\" recompenses=\"[1500,250,[[500,2]],[3],[2],[17]]\" />\n"
            + "<etape id=\"11\" nom=\"Étape fictive onze\" description=\"Description fictive onze.\" recompenses=\"[null,null,null,null,null,null]\" />\n"
            + "<etape id=\"12\" nom=\"Étape fictive douze\" description=\"\" recompenses=\"pas du json\" />\n"
            + "<objectif id=\"100\" type=\"0\" parametres=\"[&quot;Parler au garde fictif&quot;]\" x=\"4\" y=\"-12\" />\n"
            + "<objectif id=\"101\" type=\"4\" parametres=\"[&quot;Lieu fictif&quot;]\" />\n"
            + "<modele id=\"0\" texte=\"#1\" />\n<modele id=\"4\" texte=\"Explorer #1\" />\n");
        Xml(folder, "titles", "<titre id=\"2\" texte=\"Ami de %1\" couleur=\"16777215\" typeParametre=\"0\" />\n"
            + "<titre id=\"3\" texte=\"Héros fictif\" couleur=\"16711680\" typeParametre=\"0\" />\n<titre id=\"4\" texte=\"Sans couleur\" typeParametre=\"0\" />\n");
        Xml(folder, "items", "<objet id=\"500\" nom=\"Objet fictif\" type=\"9\" gfx=\"77\" niveau=\"1\" />\n");
        Xml(folder, "emotes", "<emote id=\"3\" nom=\"Émote fictive\" commande=\"test\" />\n");
        Xml(folder, "jobs", "<metier id=\"2\" nom=\"Métier fictif\" specialisation=\"0\" icone=\"5\" />\n");
        Xml(folder, "spells", "<sort id=\"17\" nom=\"Sort fictif\" description=\"\" />\n");
        Xml(folder, "dialog", "<question id=\"7\" texte=\"Question fictive pour #1.\" />\n");
    }

    private static void Run()
    {
        string previous = Environment.CurrentDirectory;
        string folder = Path.Combine(TestPaths.Work, "bot-quests"); Directory.CreateDirectory(folder); Environment.CurrentDirectory = folder;
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var logs = new ConcurrentQueue<string>();
        try
        {
            LangData.Clear();
            MessagesReception.Init();
            Map.AllBotMaps[900093] = new Map { MapID = 900093, MapWidth = 3, MapHeight = 4, MapData = string.Concat(Enumerable.Repeat("HhGaeaaaaa", 18)) };

            // Replis sans textes du client.
            Check(QuestTexts.QuestName(1) == "Quête n° 1" && QuestTexts.StepName(10) == "Étape n° 10" && QuestTexts.ObjectiveText(100) == "Objectif n° 100"
                && QuestTexts.Dialog(7, new[] { "x" }) == "Question n° 7" && QuestTexts.Rewards(10).Count == 0, "Quest fallbacks differ");
            Check(Titles.Name(3) == "« Titre n° 3 »" && Titles.Color(3) == Titles.DefaultColor && Titles.Name(0) == string.Empty && !Titles.IsKnown(3)
                && Titles.ParameterType(3) == null, "Title fallbacks differ");

            using (var account = new Accounts(new AccountConfig("synthetic-quests", "synthetic", "loopback")))
            {
                account.Logger.log_event += (entry, color) => logs.Enqueue(entry.message);
                QuestsActions quests = account.Game.Interactions.Quests;
                Check(quests != null && !quests.HasReceived && quests.Quests.Count == 0, "Interactions.Quests is missing or not empty");
                // Hors connexion : rien n'est envoyé.
                Check(!Sent(quests.RefreshAsync()) && !Sent(quests.SelectAsync(1)), "Quest actions were accepted while disconnected");

                Task<Socket> accept = listener.AcceptSocketAsync();
                Complete(account.Connexion.ConnectToServer(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port)); Complete(accept);
                using (Socket peer = accept.Result)
                {
                    peer.ReceiveTimeout = 6000;
                    account.Game.character.SetPerso_Data(42, "Personnage de test", 25, 0, 8);
                    Feed(account, "GDM|900093|date|key"); Check(Read(peer) == "GI", "GDM did not request GI");
                    // Champ type « classe,titre » de StarLoco : le personnage porte le titre 3.
                    Feed(account, "GM|+0;1;0;42;Personnage de test;1,3;100^100;0");
                    Check((account.Game.Map.Self as PlayerActor)?.TitleId == 3, "Self GM title was not read");
                    account.AccountStates = AccountStates.CONNECTED_INACTIVE;
                    int changes = 0; Action counted = () => Interlocked.Increment(ref changes);
                    quests.Changed += counted;
                    var updates = new List<QuestUpdate>(); Action<QuestUpdate> progress = update => { lock (updates) updates.Add(update); };
                    quests.ProgressReceived += progress;

                    // QS refusé tant que la liste n'est pas reçue ; QL envoyé tel quel.
                    Check(!Sent(quests.SelectAsync(1)), "QS was accepted before QL");
                    Check(Sent(quests.RefreshAsync()) && Read(peer) == "QL", "Refresh does not send QL");

                    // QL de StarLoco (« + » de tête) et forme lue par le client (substr(3) : id;fini[;ordre]).
                    Feed(account, "QL+1;0|2;1");
                    Check(quests.HasReceived && quests.Quests.Count == 2 && quests.Quests[0].Id == 1 && !quests.Quests[0].IsFinished && quests.Quests[1].IsFinished
                        && quests.PendingCount == 1, "QL+1;0|2;1 is not two quests");
                    Check(quests.VisibleQuests.Select(entry => entry.Id).SequenceEqual(new[] { 1 }), "Finished quests are shown by default");
                    Feed(account, "QL1;0|2;1");
                    Check(quests.Quests.Count == 2 && quests.Quests[1].Id == 2 && quests.Quests[1].IsFinished, "QL1;0|2;1 is not two quests");
                    Feed(account, "QL+3;0;9|1;0;2|2;1|1;1");
                    Check(quests.Quests.Count == 3 && quests.VisibleQuests.Select(entry => entry.Id).SequenceEqual(new[] { 1, 3 }), "Sort order or duplicate ids are wrong");
                    Feed(account, "QL+"); Check(quests.HasReceived && quests.Quests.Count == 0 && quests.PendingCount == 0, "Empty QL+ is not an empty list");
                    Feed(account, "QL+1;0|2;1");

                    // QS<id> sans décalage ; quête absente refusée localement (StarLoco lèverait une exception).
                    Check(!Sent(quests.SelectAsync(99)) && quests.SelectedQuestId == null, "QS for an unknown quest was accepted");
                    NoPacket(peer, "A refused QS reached the server");
                    Check(Sent(quests.SelectAsync(1)) && Read(peer) == "QS1" && quests.SelectedQuestId == 1 && quests.SelectedStep == null, "Select does not send QS1");

                    // QS de StarLoco : quête|étape courante|objectif,fini;…|précédente|suivante|question du PNJ|.
                    Feed(account, "QS1|10|100,0;101,1|9|11;12|7;Bob,Alice|");
                    QuestStep step = quests.StepOf(1);
                    Check(step != null && ReferenceEquals(step, quests.SelectedStep) && step.StepId == 10 && step.HasCurrentStep, "QS was not stored");
                    Check(step.Objectives.Count == 2 && step.Objectives[0].Id == 100 && !step.Objectives[0].IsFinished && step.Objectives[1].IsFinished,
                        "QS objectives are wrong");
                    Check(step.PreviousSteps.SequenceEqual(new[] { 9 }) && step.NextSteps.SequenceEqual(new[] { 11, 12 }) && step.DialogId == 7
                        && step.DialogParameters.SequenceEqual(new[] { "Bob", "Alice" }), "QS hierarchy or question are wrong");
                    Check(step.AllSteps.Select(line => line.Id + ":" + line.State).SequenceEqual(new[] { "9:Finished", "10:Current", "11:NotDone", "12:NotDone" }),
                        "Step hierarchy differs");
                    Feed(account, "QS1|10|100,0;101,1|9;8||");
                    Check(quests.StepOf(1).PreviousSteps.SequenceEqual(new[] { 8, 9 }) && quests.StepOf(1).NextSteps.Count == 0 && quests.StepOf(1).DialogId == null,
                        "Previous steps are not reversed like the client");
                    Feed(account, "QS1||||");
                    Check(!quests.StepOf(1).HasCurrentStep && quests.StepOf(1).Objectives.Count == 0 && quests.StepOf(1).Rewards.Count == 0, "QS without current step differs");
                    Feed(account, "QS1|0|||"); Check(quests.StepOf(1).StepId == 0 && !quests.StepOf(1).HasCurrentStep, "QS with step 0 is not read as no current step");
                    Feed(account, "QS99|5|1,0||"); Check(quests.StepOf(99) == null && quests.LastMessage.Contains("absente"), "QS for a quest outside QL was stored");

                    // Paquets mal formés : journalisés, jamais propagés, état inchangé.
                    Feed(account, "QS1|10|100,0;101,1|9|11;12|7;Bob,Alice|");
                    QuestStep kept = quests.StepOf(1);
                    foreach (string broken in new[] { "QL+x;0", "QL+1;0|;1", "QL+-4;0", "QL+99999999999;0", "QS", "QSabc", "QS1", "QS1|x|", "QS1|10|zz,0",
                        "QS-1|1|", "QS1|10||9;a|", "QS1|10|||b", "QL|", "QL+1;0||2;1" })
                        Feed(account, broken);
                    Check(quests.Quests.Count == 2 && ReferenceEquals(quests.StepOf(1), kept), "A malformed packet changed the state");
                    Check(logs.Count(entry => entry.Contains("illisible ignorée")) >= 10, "Malformed packets were not logged");
                    Check(!logs.Any(entry => entry.Contains("Paquet de quêtes illisible")), "A quests packet threw: " + string.Join(" / ", logs.Where(entry => entry.Contains("illisible"))));
                    NoPacket(peer, "A malformed packet was answered");

                    // Im054/055/056 : progression gardée, liste « à actualiser », rien n'est envoyé (le client ne redemande pas QL).
                    Feed(account, "Im054;3");
                    Check(quests.LastUpdate?.Kind == QuestUpdateKind.Started && quests.LastUpdate.QuestId == 3 && quests.NeedsRefresh, "Im054 is not kept");
                    Feed(account, "Im055;1"); Check(quests.LastUpdate.Kind == QuestUpdateKind.Updated && quests.LastUpdate.QuestId == 1, "Im055 is not kept");
                    Feed(account, "Im056;2"); Check(quests.LastUpdate.Kind == QuestUpdateKind.Finished && quests.LastUpdate.QuestId == 2, "Im056 is not kept");
                    Feed(account, "Im054;x"); Feed(account, "Im054"); Feed(account, "Im0153;1"); Feed(account, "Im154;1");
                    Check(quests.LastUpdate.Kind == QuestUpdateKind.Finished && quests.LastUpdate.QuestId == 2, "Unrelated Im changed the progress");
                    lock (updates) Check(updates.Select(update => (int)update.Kind).SequenceEqual(new[] { 54, 55, 56 }), "ProgressReceived differs");
                    NoPacket(peer, "A progress message sent a packet");

                    // Nouvelle liste : étapes oubliées comme le client ; fenêtre fermée, la quête choisie n'est pas redemandée.
                    Check(Sent(quests.RefreshAsync()) && Read(peer) == "QL", "Refresh after progress does not send QL");
                    Feed(account, "QL+1;0|2;1");
                    Check(!quests.NeedsRefresh && quests.StepOf(1) == null && quests.SelectedQuestId == 1, "QL kept the steps or the progress flag");
                    NoPacket(peer, "QL re-selected a quest while the window is closed");
                    // Fenêtre ouverte : case décochée, QL, puis QS de la dernière quête choisie (modelChanged).
                    Check(Sent(quests.SetShowFinishedAsync(true)) && Read(peer) == "QS1" && quests.ShowFinished && quests.VisibleQuests.Count == 2,
                        "Showing finished quests does not re-select the last quest");
                    Check(Sent(quests.OpenWindowAsync()) && Read(peer) == "QL" && quests.WindowOpen && !quests.ShowFinished, "Opening the window differs");
                    Feed(account, "QL+1;0|2;1"); Check(Read(peer) == "QS1", "QL in an open window does not re-select the last quest");
                    Check(Sent(quests.SelectAsync(2)) && Read(peer) == "QS2", "A finished quest of the list cannot be selected");
                    Check(!Sent(quests.SetShowFinishedAsync(false)) && quests.VisibleQuests.Count == 1, "A hidden quest was re-selected");
                    NoPacket(peer, "A hidden quest was requested");
                    quests.Deselect(); quests.CloseWindow();
                    Check(quests.SelectedQuestId == null && !quests.WindowOpen, "Deselect or CloseWindow differs");
                    NoPacket(peer, "Deselect sent a packet");
                    Check(changes > 10, "Changed was not raised");
                    quests.Changed -= counted; quests.ProgressReceived -= progress;
                    quests.Clear();
                    Check(!quests.HasReceived && quests.Quests.Count == 0 && quests.LastUpdate == null && !quests.NeedsRefresh && quests.SelectedQuestId == null,
                        "Clear kept the state");

                    // Textes synthétiques : noms, étapes, objectifs (modèles Q.t), coordonnées, récompenses, question, titres.
                    string lang = Path.Combine(folder, "BotLang");
                    WriteTexts(lang);
                    Check(LangData.Load(lang) == 8, "Synthetic texts were not loaded: " + string.Join(" / ", LangData.LoadWarnings));
                    Check(QuestTexts.QuestName(1) == "Quête fictive un" && QuestTexts.StepName(10) == "Étape fictive dix"
                        && QuestTexts.StepDescription(10) == "Description fictive dix." && QuestTexts.ObjectiveText(100) == "Parler au garde fictif"
                        && QuestTexts.ObjectiveText(101) == "Explorer Lieu fictif" && QuestTexts.ObjectiveCoordinates(100) == new Point(4, -12)
                        && QuestTexts.ObjectiveCoordinates(101) == null, "Quest texts are not read from the XML");
                    Check(QuestTexts.PendingCount(1) == "1 quête en cours (test)" && QuestTexts.PendingCount(3) == "3 quêtes en cours (test)", "PENDING_QUEST is not combined");
                    Check(QuestTexts.Dialog(7, new[] { "Bob" }) == "Question fictive pour Bob." && QuestTexts.Dialog(8, null) == "Question n° 8", "Step question differs");
                    IReadOnlyList<QuestReward> loot = QuestTexts.Rewards(10);
                    Check(string.Join("|", loot.Select(reward => reward.Kind + ":" + reward.Id + ":" + reward.Label)) == "Experience:0:" + 1500.ToString(System.Globalization.CultureInfo.CurrentCulture)
                        + "|Kamas:0:" + 250.ToString(System.Globalization.CultureInfo.CurrentCulture) + "|Item:500:x2 Objet fictif|Emote:3:Émote fictive|Job:2:Métier fictif|Spell:17:Sort fictif",
                        "Rewards differ: " + string.Join("|", loot.Select(reward => reward.Kind + ":" + reward.Id + ":" + reward.Label)));
                    Check(QuestTexts.Rewards(11).Count == 0 && QuestTexts.Rewards(12).Count == 0 && QuestTexts.StepDescription(12) == string.Empty, "Null or broken rewards differ");
                    Check(Titles.Name(3) == "« Héros fictif »" && Titles.Color(3) == 0xFF0000 && Titles.Name(2, "Bob") == "« Ami de Bob »" && Titles.Color(2) == 0xFFFFFF
                        && Titles.Color(4) == Titles.DefaultColor && Titles.Name(77) == "« Titre n° 77 »" && Titles.Color(77) == Titles.DefaultColor
                        && Titles.ParameterType(3) == 0 && Titles.IsKnown(3) && !Titles.IsKnown(77), "Titles differ");
                    PlayerTitle worn = Titles.Of(account.Game.Map.Self as PlayerActor);
                    Check(worn != null && worn.Id == 3 && worn.Name == "« Héros fictif »" && worn.Color == 0xFF0000 && Titles.Of(null) == null, "Worn title differs");
                    Feed(account, "Im054;3");
                    Check(quests.LastUpdate.Text == "Nouvelle quête (test) : Quête fictive trois" && !quests.NeedsRefresh, "Im054 text is not resolved: " + quests.LastUpdate.Text);

                    // Volet Quêtes : images synthétiques lues dans un dossier temporaire.
                    string images = Path.Combine(folder, "images");
                    Color current = Color.FromArgb(255, 30, 90, 200), done = Color.FromArgb(255, 40, 170, 60), arrow = Color.FromArgb(255, 230, 130, 20);
                    Color compass = Color.FromArgb(255, 150, 40, 170), xp = Color.FromArgb(255, 200, 40, 40), kamas = Color.FromArgb(255, 210, 190, 30);
                    Color item = Color.FromArgb(255, 20, 160, 160), emote = Color.FromArgb(255, 110, 60, 20), job = Color.FromArgb(255, 90, 90, 220), spell = Color.FromArgb(255, 220, 60, 160);
                    string client = Path.Combine(images, "UI", "Client");
                    Png(client, "quete-en-cours", current); Png(client, "quete-terminee", done); Png(client, "etape-courante", arrow);
                    Png(client, "objectif-boussole", compass); Png(client, "UI_QuestXP", xp); Png(client, "kamas", kamas);
                    Png(Path.Combine(images, "Items", "9"), "77", item); Png(Path.Combine(images, "Emotes"), "3", emote);
                    Png(Path.Combine(images, "Jobs"), "5", job); Png(Path.Combine(images, "sorts"), "17", spell);
                    AssetsRoot(images);

                    using (var form = new GameClientFullform(account))
                    {
                        form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); Application.DoEvents();
                        var drawer = form.Panels; var panel = drawer.Get<QuestsPanel>();
                        Check(panel != null && drawer.Current == null && panel.Title == "Liste des quêtes (test)" && drawer.Find("Quests") == panel,
                            "Quests panel is not registered under the banner name");
                        NoPacket(peer, "The game window requested quests before the panel was opened");
                        // Icône Quêtes du bandeau : ouvre le volet, qui demande la liste (QL), case décochée.
                        Button icon = All(form).OfType<Button>().First(button => "client-icon".Equals(button.Tag) && button.AccessibleName == "Quêtes");
                        Check(icon.Enabled, "Quests banner icon is disabled");
                        icon.PerformClick(); PumpUntil(() => drawer.Current == panel);
                        Check(Read(peer) == "QL" && quests.WindowOpen && !panel.FinishedBox.Checked, "Opening the panel does not send QL");
                        Check(panel.CountText.StartsWith("Liste des quêtes non reçue") && !panel.ViewerShowsQuest, "Panel before QL differs");

                        FeedFromNetwork(account, "QL+1;0|2;1");
                        PumpUntil(() => panel.QuestList.Rows.Count == 1);
                        Check(panel.CountText == "1 quête en cours (test)" && panel.QuestList.Rows[0].Text == "Quête fictive un" && panel.QuestList.SelectedIndex == -1,
                            "Quest rows differ");
                        NoPacket(peer, "QL selected a quest without a previous choice");
                        // Case « terminées » : la quête 2 apparaît avec la coche ; aucune quête choisie, rien n'est envoyé.
                        panel.FinishedBox.Checked = true;
                        PumpUntil(() => panel.QuestList.Rows.Count == 2);
                        NoPacket(peer, "The finished checkbox sent a packet without a chosen quest");
                        using (Snapshot(panel.QuestList)) { }
                        PumpUntil(() => Cached("Client", "quete-en-cours") && Cached("Client", "quete-terminee"));
                        Check(Near(IconPixel(panel.QuestList, 0), current) && Near(IconPixel(panel.QuestList, 1), done), "Quest state icons are not drawn");

                        // Clic sur une quête : QS<id>, vue des étapes titrée du nom, en attente de la réponse.
                        Check(panel.ClickQuest(1) && Read(peer) == "QS1", "Clicking a quest does not send QS1");
                        PumpUntil(() => panel.ViewerShowsQuest);
                        Check(panel.ViewerTitle == "Quête fictive un" && panel.QuestList.SelectedIndex == 0, "Viewer title differs: " + panel.ViewerTitle);
                        FeedFromNetwork(account, "QS1|10|100,0;101,1|9|11;12|7;Bob|");
                        PumpUntil(() => panel.StepTitleText.Length > 0 && panel.ObjectiveList.Rows.Count == 2);
                        Check(panel.CurrentTab == QuestsPanel.CurrentStepTab && panel.StepTitleText == "Étape (test) : Étape fictive dix"
                            && panel.StepDescriptionText == "Description fictive dix.", "Current step differs: " + panel.StepTitleText);
                        Check(panel.ObjectiveList.Rows[0].Text == "Parler au garde fictif" && panel.ObjectiveList.Rows[0].Clickable && !panel.ObjectiveList.Rows[0].Muted
                            && panel.ObjectiveList.Rows[1].Text == "Explorer Lieu fictif" && panel.ObjectiveList.Rows[1].Muted && !panel.ObjectiveList.Rows[1].Clickable,
                            "Objective rows differ");
                        Check(panel.RewardList.Rows.Count == 6 && panel.RewardList.Rows[2].Text == "x2 Objet fictif" && panel.DialogButton.Parent.Visible, "Reward rows or dialog button differ");
                        using (Snapshot(panel.ObjectiveList)) { }
                        using (Snapshot(panel.RewardList)) { }
                        PumpUntil(() => Cached("Client", "objectif-boussole") && Cached("Client", "UI_QuestXP") && Cached("Client", "kamas") && Cached("Items", "9/77")
                            && Cached("Emotes", "3") && Cached("Jobs", "5") && Cached("Spells", "17"));
                        Check(Near(IconPixel(panel.ObjectiveList, 0), compass) && Near(IconPixel(panel.ObjectiveList, 1), done), "Objective icons are not drawn");
                        Color[] expected = { xp, kamas, item, emote, job, spell };
                        for (int index = 0; index < expected.Length; index++)
                            Check(Near(IconPixel(panel.RewardList, index), expected[index]), "Reward icon " + index + " is not drawn: " + IconPixel(panel.RewardList, index));

                        // Objectif localisé : boussole du bandeau, aucun paquet ; objectif sans coordonnées sans effet.
                        Check(panel.ClickObjective(100) && panel.LastCompass == new Point(4, -12), "Objective click does not point the compass");
                        Check(panel.ClickObjective(101) && panel.LastCompass == new Point(4, -12), "An objective without coordinates moved the compass");
                        NoPacket(peer, "The compass sent a packet");

                        // Dialogue de l'étape : boîte d'information avec la question du PNJ.
                        Task dialog = panel.ShowDialogAsync();
                        PumpUntil(() => BotDialogs.OpenDialogs.Count == 1);
                        Form box = BotDialogs.OpenDialogs[0];
                        Check(box.Text == "Dialogue de l'étape (test)" && All(box).Any(control => control.Name == "dialog-message" && control.Text == "Question fictive pour Bob."),
                            "Step dialog box differs");
                        All(box).OfType<Button>().Single().PerformClick(); PumpUntil(() => BotDialogs.OpenDialogs.Count == 0); Complete(dialog);

                        // Onglet « Hiérarchie » : passée cochée, courante fléchée, à venir grisées, description de l'étape choisie.
                        panel.SelectTab(QuestsPanel.AllStepsTab);
                        Check(panel.CurrentTab == QuestsPanel.AllStepsTab && panel.StepList.Rows.Select(row => row.Text)
                            .SequenceEqual(new[] { "Étape fictive neuf", "Étape fictive dix", "Étape fictive onze", "Étape fictive douze" }), "Step rows differ");
                        Check(!panel.StepList.Rows[0].Muted && !panel.StepList.Rows[1].Muted && panel.StepList.Rows[2].Muted && panel.StepList.Rows[3].Muted
                            && panel.AllStepsDescriptionText == "Description fictive neuf.", "Step styles or description differ");
                        using (Snapshot(panel.StepList)) { }
                        PumpUntil(() => Cached("Client", "etape-courante"));
                        Check(Near(IconPixel(panel.StepList, 0), done) && Near(IconPixel(panel.StepList, 1), arrow), "Step icons are not drawn");
                        Check(panel.StepList.ClickRow(row => ((QuestStepLine)row.Tag).Id == 11) && panel.AllStepsDescriptionText == "Description fictive onze."
                            && panel.StepList.SelectedIndex == 2, "Choosing a step does not show its description");
                        NoPacket(peer, "Tabs or steps sent a packet");

                        // Progression : bandeau « à actualiser », Actualiser → QL, puis la quête choisie est redemandée.
                        FeedFromNetwork(account, "Im055;1");
                        PumpUntil(() => panel.NoticeVisible);
                        Check(panel.NoticeText.StartsWith("Quête mise à jour (test) : Quête fictive un"), "Progress notice differs: " + panel.NoticeText);
                        NoPacket(peer, "Im055 sent a packet");
                        ((Button)panel.RefreshButton).PerformClick();
                        Check(Read(peer) == "QL", "Refresh button does not send QL");
                        FeedFromNetwork(account, "QL+1;0|2;1");
                        Check(Read(peer) == "QS1", "QL in the open panel does not request the chosen quest");
                        PumpUntil(() => !panel.NoticeVisible && panel.ObjectiveList.Rows.Count == 2);
                        FeedFromNetwork(account, "QS1|10|100,1;101,1|9|11;12|");
                        PumpUntil(() => panel.CurrentTab == QuestsPanel.CurrentStepTab && panel.ObjectiveList.Rows[0].Muted);
                        Check(!panel.DialogButton.Parent.Visible && panel.ObjectiveList.Rows[0].Clickable, "Step without question still shows the dialog button");

                        // Quête terminée choisie puis masquée par la case : la vue se referme sans paquet ; recochée, elle est redemandée.
                        Check(panel.ClickQuest(2) && Read(peer) == "QS2", "Clicking a finished quest does not send QS2");
                        panel.FinishedBox.Checked = false;
                        PumpUntil(() => !panel.ViewerShowsQuest && panel.QuestList.Rows.Count == 1);
                        Check(quests.SelectedQuestId == 2 && panel.QuestList.SelectedIndex == -1, "Hiding the chosen quest lost the choice or kept the row");
                        NoPacket(peer, "Hiding the chosen quest sent a packet");
                        panel.FinishedBox.Checked = true;
                        Check(Read(peer) == "QS2", "Showing the chosen quest again does not request it");
                        PumpUntil(() => panel.ViewerShowsQuest && panel.ViewerTitle == "Quête fictive deux");

                        // × de la vue des étapes : plus de quête choisie, rien n'est envoyé.
                        panel.CloseStep();
                        Check(quests.SelectedQuestId == null && !panel.ViewerShowsQuest && panel.QuestList.SelectedIndex == -1, "Closing the step view kept the quest");
                        NoPacket(peer, "Closing the step view sent a packet");

                        // Volet fermé puis rouvert : QL, case décochée.
                        drawer.CloseAll(); Application.DoEvents();
                        Check(!drawer.IsOpen(panel) && !quests.WindowOpen, "Closing the panel kept WindowOpen");
                        drawer.Show(panel);
                        Check(Read(peer) == "QL" && quests.WindowOpen && !panel.FinishedBox.Checked, "Reopening the panel does not send QL");

                        // Titre porté dans la fiche des caractéristiques : pastille brune, texte du titre ; GM sans titre → « Aucun titre ».
                        StatsPanel stats = drawer.Get<StatsPanel>();
                        drawer.Show(stats); Application.DoEvents();
                        TitleBadge badge = All(stats.View).OfType<TitleBadge>().Single();
                        Check(badge.DisplayText == "« Héros fictif »" && badge.Current.Color == 0xFF0000, "Title badge differs: " + badge.DisplayText);
                        using (Bitmap image = Snapshot(badge))
                            Check(Near(image.GetPixel(18, 3 + TitleBadge.BadgeHeight / 2), Color.FromArgb(41, 38, 31)), "Title badge is not drawn in the client brown");
                        FeedFromNetwork(account, "GM|+0;1;0;42;Personnage de test;1;100^100;0");
                        PumpUntil(() => badge.AccessibleName == "Aucun titre");
                        Check(badge.DisplayText == "Aucun titre" && badge.Current == null, "Title badge kept a removed title");
                        NoPacket(peer, "The title badge sent a packet");
                        form.Close();
                    }
                    Check(BotDialogs.OpenDialogs.Count == 0, "A dialog survived the game window");
                    Check(!logs.Any(entry => entry.Contains("abonné des quêtes a échoué") || entry.Contains("Paquet de quêtes illisible")),
                        "A quests subscriber failed: " + string.Join(" / ", logs.Where(entry => entry.Contains("quêtes"))));
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
