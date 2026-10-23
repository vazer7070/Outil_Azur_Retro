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
using Outil_Azur_complet.Bot.Interfaces;
using Outil_Azur_complet.Bot.Menus;
using Outil_Azur_complet.Bot.Panels;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Maps;

// Dialogue PNJ avec les textes du client (lot M5) : LangData chargé depuis des XML synthétiques écrits dans le dossier
// de travail (aucun texte du client), DQ avec paramètres substitués, réponses en texte, choix de fin d'une question sans
// réponse, percepteur, AR « ne peut pas parler aux PNJ », menu des actions N.a (ordre du client, action 8 masquée, ER<type>),
// clic gauche → menu, Maj + clic → DC, illustration lue hors du fil de l'interface, repli sans XML. Serveur fictif local.
internal static class BotNpcDialogTextsSmoke
{
    private static readonly List<Exception> uiErrors = new List<Exception>();

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
            Console.WriteLine("OK: textes DQ/réponses avec paramètres, fin sans réponse, percepteur, AR 512, menu N.a (ordre, action 8 masquée, ER0/2/9/10/11/17), Maj + clic, illustration hors du fil UI, repli sans XML");
        }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Equal(string expected, string actual, string what) { if (expected != actual) throw new Exception(what + " : attendu « " + expected + " », obtenu « " + actual + " »"); }
    private static object Get(object target, string field) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static void PumpUntil(Func<bool> done)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(6);
        while (!done()) { if (DateTime.UtcNow > end) throw new TimeoutException("NPC dialog loopback timed out"); Application.DoEvents(); Thread.Sleep(5); }
        Application.DoEvents();
    }
    private static void Complete(Task task) { PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static T Result<T>(Task<T> task) { Complete(task); return task.Result; }
    private static void Feed(Accounts account, string packet) { Complete(MessagesReception.ReceptionAsync(account.Connexion, packet)); }
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
    private static string[] Entries(ContextMenuStrip menu) =>
        menu.Items.OfType<ToolStripMenuItem>().Select(item => item.Text).ToArray();
    private static Map SyntheticMap(int id) => new Map { MapID = id, MapWidth = 3, MapHeight = 4, X = 1, Y = 1, MapData = string.Concat(Enumerable.Repeat("HhGaeaaaaa", 18)) };

    private static void WriteLang(string folder, string file, string family, string body) =>
        File.WriteAllText(Path.Combine(folder, file), "<?xml version='1.0' encoding='utf-8'?>\n<BotLang famille=\"" + family + "\" langue=\"fr\" version=\"1\" source=\"" + family + "_fr_1.swf\">\n" + body + "\n</BotLang>\n", new UTF8Encoding(false));

    private static void WritePortrait(string path, Color color, int width, int height)
    {
        using (var image = new Bitmap(width, height, PixelFormat.Format32bppArgb))
        {
            using (var graphics = Graphics.FromImage(image)) graphics.Clear(color);
            image.Save(path, ImageFormat.Png);
        }
    }

    private static void Run()
    {
        string previous = Environment.CurrentDirectory;
        string folder = Path.Combine(TestPaths.Work, "bot-npc-texts-" + Guid.NewGuid().ToString("N"));
        string lang = Path.Combine(folder, "lang"), root = Path.Combine(folder, "app");
        string portraits = Path.Combine(root, "ressources", "Bot", "Portraits");
        Directory.CreateDirectory(lang); Directory.CreateDirectory(portraits); Environment.CurrentDirectory = folder;
        WriteLang(lang, "dialog.xml", "dialog",
            "<question id=\"12\" texte=\"Bonjour #1, tu as #2 kamas.\" />\n" +
            "<question id=\"13\" texte=\"Reviens plus tard.\" />\n" +
            "<question id=\"1\" texte=\"Percepteur n° #5 de #1, #2 pods.\" />\n" +
            "<reponse id=\"3\" texte=\"Oui, merci.\" />\n" +
            "<reponse id=\"4\" texte=\"Non, au revoir.\" />\n" +
            "<reponse id=\"6\" texte=\"Une réponse synthétique très longue, écrite pour vérifier que la ligne de réponse s'agrandit et renvoie son texte à la ligne au lieu de le couper.\" />");
        WriteLang(lang, "npc.xml", "npc",
            "<action id=\"1\" nom=\"Marchander\" />\n<action id=\"2\" nom=\"Troquer\" />\n<action id=\"3\" nom=\"Bavarder\" />\n" +
            "<action id=\"4\" nom=\"Familier fictif\" />\n<action id=\"5\" nom=\"Enchère fictive\" />\n<action id=\"8\" nom=\"Monture fictive\" />\n" +
            "<pnj id=\"900\" nom=\"Gardien synthétique\" actions=\"3,1,8\" />\n" +
            "<pnj id=\"901\" nom=\"Troqueur synthétique\" actions=\"2,5\" />\n" +
            "<pnj id=\"902\" nom=\"Muet synthétique\" />");
        WriteLang(lang, "lang.xml", "lang", "<texte cle=\"CONTINUE_TO_SPEAK\" valeur=\"Fin fictive de la discussion.\" />");
        WritePortrait(Path.Combine(portraits, "4321.png"), Color.FromArgb(255, 200, 30, 30), 8, 10);
        File.WriteAllBytes(Path.Combine(portraits, "122.png"), new byte[] { 1, 2, 3, 4, 5 });
        string previousRoot = NpcPortraits.Root;
        NpcPortraits.Root = root;
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            Check(LangData.Load(lang) == 3 && LangData.LoadWarnings.Length == 0, "Synthetic client texts were not loaded: " + string.Join(" | ", LangData.LoadWarnings));
            MessagesReception.Init();
            Map.AllBotMaps[900101] = SyntheticMap(900101);

            // Illustrations : lues hors du fil appelant, absentes ou corrompues sans exception.
            int caller = Thread.CurrentThread.ManagedThreadId;
            Task<int> loader = NpcPortraits.LoadAsync(4321).ContinueWith(t => { using (t.Result) return t.Result != null && t.Result.Width == 8 ? Thread.CurrentThread.ManagedThreadId : -1; },
                TaskContinuationOptions.ExecuteSynchronously);
            int loadThread = Result(loader);
            Check(loadThread > 0 && loadThread != caller, "Portrait was not read on a background thread or has the wrong size");
            Check(NpcPortraits.Load(122) == null && NpcPortraits.Load(121) == null && NpcPortraits.Load(0) == null && NpcPortraits.PathFor(-5) == null,
                "Corrupted, missing or invalid portraits did not return null");

            using (var account = new Accounts(new AccountConfig("synthetic-npc-texts", "synthetic", "loopback")))
            {
                Task<Socket> accept = listener.AcceptSocketAsync();
                Complete(account.Connexion.ConnectToServer(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port)); Complete(accept);
                using (Socket peer = accept.Result)
                {
                    peer.ReceiveTimeout = 6000;
                    account.Game.character.SetPerso_Data(42, "Personnage de test", 25, 0, 8);
                    Feed(account, "GDM|900101|date|key"); Check(Read(peer) == "GI", "GDM did not request GI");
                    Feed(account, "GM|+0;1;0;42;Personnage de test;1;100^100;0"
                        + "|+5;1;0;-8;900;-4;120^100;0;-1;-1;-1;;-1;4321"
                        + "|+6;1;0;-9;901;-4;121^100;1;-1;-1;-1;;-1;0"
                        + "|+2;1;0;-10;902;-4;122^100;0;-1;-1;-1;;-1;0"
                        + "|+4;1;0;-11;777;-4;123^100;0;-1;-1;-1;;-1;0"
                        + "|+8;1;0;-4;a,b;-6;6000^100;3;Guilde fictive;1a,2b,3c,4d");
                    account.AccountStates = AccountStates.CONNECTED_INACTIVE;
                    var map = account.Game.Map;
                    Check(map.GetActor(-8) != null && map.GetActor(-9) != null && map.GetActor(-4) != null, "Synthetic NPCs or collector were not added");

                    // Actions N.a : ordre du client, action 8 masquée, repli pour un modèle inconnu, aucune action pour un PNJ muet.
                    Check(NpcActions.ForTemplate(900, out bool known) .SequenceEqual(new[] { 3, 1, 8 }) && known, "N.a actions of template 900 differ");
                    Check(NpcMenuProvider.ActionsFor(map.GetActor(-8), out _).SequenceEqual(new[] { 3, 1 }), "Action 8 (ER18) is not hidden");
                    Check(NpcMenuProvider.ActionsFor(map.GetActor(-11), out bool fromTexts).SequenceEqual(new[] { 3, 1 }) && !fromTexts, "Unknown template does not fall back to Parler/Acheter-Vendre");
                    Check(NpcMenuProvider.ActionsFor(map.GetActor(-10), out bool mute).Count == 0 && mute, "NPC without actions got a menu");
                    Equal("Marchander", NpcActions.Label(1), "action name from npc.xml");
                    Equal("Hôtel de vente : acheter", NpcActions.Label(6), "fallback action label");
                    var provider = new NpcMenuProvider();
                    Check(string.Join("|", provider.Entries(map.GetActor(-8), account.Game).Select(e => e.Texte)) == "Bavarder|Marchander", "Menu entries of template 900 differ");
                    Check(string.Join("|", provider.Entries(map.GetActor(-9), account.Game).Select(e => e.Texte)) == "Troquer|Enchère fictive", "Menu entries of template 901 differ");

                    // Chaque action envoie le paquet du client ; ER18 n'est jamais envoyé.
                    NpcDialog npc = account.Game.Interactions.Npc;
                    var expected = new Dictionary<int, string> { { 1, "ER0|-9" }, { 2, "ER2|-9" }, { 3, "DC-9" }, { 4, "ER9|-9" }, { 5, "ER10|-9" }, { 6, "ER11|-9" }, { 7, "ER17|-9" } };
                    foreach (var pair in expected)
                        Check(Result(npc.RequestActionAsync(pair.Key, -9)).Sent && Read(peer) == pair.Value, "Action " + pair.Key + " does not send " + pair.Value);
                    Check(!Result(npc.RequestActionAsync(8, -9)).Sent && !Result(npc.RequestActionAsync(9, -9)).Sent, "ER18 or an unknown action was sent");
                    NoPacket(peer, "Refused NPC actions reached the server");

                    // AR 512 (cantSpeakNPC du client) : aucun envoi ; StarLoco envoie AR6bk (8192) qui ne bloque rien.
                    Feed(account, "ARe8");
                    Check(npc.CannotSpeakToNpc && !Result(npc.OpenAsync(-8)).Sent && !Result(npc.RequestActionAsync(1, -8)).Sent, "AR 512 did not block talking to NPCs");
                    Check(provider.Entries(map.GetActor(-8), account.Game).All(e => !e.Activé), "NPC menu stays enabled under AR 512");
                    NoPacket(peer, "Blocked NPC actions reached the server");
                    Feed(account, "AR6bk"); Check(!npc.CannotSpeakToNpc, "AR6bk still blocks NPCs");

                    // DC/DCK/DQ : nom, modèle et illustration du PNJ, question et réponses en texte du client.
                    Check(Result(npc.OpenAsync(-8)).Sent && Read(peer) == "DC-8", "Talking does not send DC<npc>");
                    Feed(account, "DCK-8");
                    Check(npc.IsOpen && npc.NpcName == "Gardien synthétique" && npc.NpcTemplateId == 900 && npc.PortraitId == 4321 && !npc.IsCollector,
                        "DCK did not resolve name/template/portrait: " + npc.NpcName + " " + npc.NpcTemplateId + " " + npc.PortraitId);
                    Feed(account, "DQ12;Nom,500|3;4");
                    Equal("Bonjour Nom, tu as 500 kamas.", npc.QuestionText, "question with parameters");
                    Check(npc.HasQuestionText && npc.Answers.Select(a => a.Id + "=" + a.Text).SequenceEqual(new[] { "3=Oui, merci.", "4=Non, au revoir." })
                        && npc.Answers.All(a => a.HasClientText), "Answers are not the client texts");
                    Check(Result(npc.AnswerAsync(4)).Sent && Read(peer) == "DR12|4", "Answer does not send DR<question>|<answer>");
                    Feed(account, "DP"); Check(npc.IsPaused && !Result(npc.AnswerAsync(3)).Sent, "Answer accepted during DP");
                    NoPacket(peer, "Answer during DP reached the server");
                    Feed(account, "DQ13"); Check(npc.QuestionText == "Reviens plus tard." && npc.Answers.Count == 0 && !npc.IsPaused, "Question without answers differs");
                    Equal("Fin fictive de la discussion.", NpcDialog.EndChoiceText, "end choice text (CONTINUE_TO_SPEAK)");
                    Feed(account, "DQ99|5");
                    Check(npc.QuestionText == "Question n° 99" && !npc.HasQuestionText && npc.Answers.Single().Text == "Réponse n° 5" && !npc.Answers[0].HasClientText,
                        "Unknown question/answer did not fall back to numbers");
                    foreach (string broken in new[] { "DQ", "DQ;|", "DQabc|x", "DQ|3;4", "DQ12;;|;;" }) Feed(account, broken);
                    Check(npc.IsOpen && (npc.QuestionId == 99 || npc.QuestionId == 12), "Malformed DQ crashed or closed the dialog");
                    Check(Result(npc.LeaveAsync()).Sent && Read(peer) == "DV", "Leaving does not send DV");
                    Feed(account, "DV"); Check(!npc.IsOpen && npc.PortraitId == 0 && npc.NpcTemplateId == -1 && npc.Answers.Count == 0, "DV did not reset the dialog");

                    // Percepteur : DCK puis DQ de Guild.parseQuestionTaxCollector, sans réponse.
                    Feed(account, "DCK-4"); Feed(account, "DQ1;Guilde fictive,10,20,30,2");
                    Check(npc.IsOpen && npc.IsCollector && npc.PortraitId == 6000 && npc.NpcName == "Percepteur de Guilde fictive", "Collector dialog not recognised");
                    Equal("Percepteur n° 2 de Guilde fictive, 10 pods.", npc.QuestionText, "collector question");
                    Feed(account, "DV"); Check(!npc.IsOpen, "Collector dialog did not close");

                    using (var form = new GameClientFullform(account))
                    {
                        form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-4000, -4000);
                        form.ShowInTaskbar = false; form.Show(); Application.DoEvents();
                        var view = (MapControl)Get(form, "mapControl"); Check(view != null, "Map view missing for a loaded map");
                        var feedback = new List<string>(); view.Router.Feedback += message => feedback.Add(message);
                        var drawer = form.Panels; var dialog = drawer.Get<DialoguePanel>();

                        // Clic gauche : menu des actions (pas de paquet) ; l'entrée Marchander envoie ER0 ; Maj + clic parle.
                        Complete(view.HandleCellActionAsync(5)); NoPacket(peer, "Left click on an NPC sent a packet");
                        ContextMenuStrip menu = view.Router.LastMenu;
                        Check(menu != null && Entries(menu).SequenceEqual(new[] { "Bavarder", "Marchander" }), "NPC menu differs: " + (menu == null ? "aucun" : string.Join("|", Entries(menu))));
                        Check(menu.Items[0].Text == "Gardien synthétique" || menu.Items[0].Text.StartsWith("PNJ"), "Menu header is missing");
                        menu.Items.OfType<ToolStripMenuItem>().First(item => item.Text == "Marchander").PerformClick();
                        Check(Read(peer) == "ER0|-8", "Marchander (action 1) does not send ER0|<npc>");
                        Complete(view.HandleCellActionAsync(6)); menu = view.Router.LastMenu;
                        Check(Entries(menu).SequenceEqual(new[] { "Troquer", "Enchère fictive" }), "Second NPC menu differs");
                        menu.Items.OfType<ToolStripMenuItem>().First(item => item.Text == "Troquer").PerformClick();
                        Check(Read(peer) == "ER2|-9", "Troquer (action 2) does not send ER2|<npc>");
                        Complete(view.HandleCellActionAsync(4)); menu = view.Router.LastMenu;
                        Check(Entries(menu).SequenceEqual(new[] { "Bavarder", "Marchander" }), "Unknown template has no fallback menu: " + string.Join("|", Entries(menu)));
                        Check((menu.Items.OfType<ToolStripMenuItem>().First(item => item.Text == "Bavarder").ToolTipText ?? "").Contains("menu de repli"), "Fallback menu does not say so in its tooltip");
                        ContextMenuStrip before = view.Router.LastMenu;
                        Complete(view.HandleCellActionAsync(2));
                        Check(view.Router.LastMenu == before && feedback.Last().Contains("aucune action"), "NPC without actions opened a menu");
                        Feed(account, "ARe8"); feedback.Clear();
                        Complete(view.HandleCellActionAsync(5, Keys.Shift));
                        Check(view.Router.LastMenu == before && feedback.Count == 1 && feedback[0].Contains("ne peut pas parler"), "AR 512 did not block the click");
                        NoPacket(peer, "Blocked click reached the server");
                        Feed(account, "AR6bk");
                        Complete(view.HandleCellActionAsync(5, Keys.Shift)); Check(Read(peer) == "DC-8", "Shift + click does not send DC");

                        // Volet Dialogue : titre, illustration, bulle et réponses en texte.
                        Feed(account, "DCK-8"); Feed(account, "DQ12;Nom,500|3;6"); Application.DoEvents();
                        Check(drawer.Visible && drawer.Current == dialog, "Dialog panel did not open on DCK/DQ");
                        Equal("Gardien synthétique", ((Label)Get(dialog, "dialogTitle")).Text, "dialog title");
                        Equal("Bonjour Nom, tu as 500 kamas.", ((Label)Get(dialog, "dialogQuestion")).Text, "question in the bubble");
                        PumpUntil(() => dialog.Portrait != null);
                        Check(dialog.Portrait.Width == 8 && dialog.Portrait.Height == 10, "Portrait PNG not displayed");
                        var answers = (FlowLayoutPanel)Get(dialog, "dialogAnswers");
                        Check(answers.Controls.Count == 2 && answers.Controls[0].Text == "Oui, merci." && answers.Controls[1].Text.StartsWith("Une réponse synthétique")
                            && answers.Controls.Cast<Control>().All(c => c.Enabled), "Answer buttons differ from DQ");
                        Check(answers.Controls[1].Height > answers.Controls[0].Height && answers.Controls[0].Height >= 30, "Long answer is not wrapped on several lines");
                        ((Button)answers.Controls[1]).PerformClick(); Check(Read(peer) == "DR12|6", "Answer button does not send DR");
                        Feed(account, "DP"); Application.DoEvents();
                        Check(answers.Controls.Cast<Control>().All(c => !c.Enabled), "Answers stay enabled during DP");
                        Feed(account, "DQ13"); Application.DoEvents();
                        Check(answers.Controls.Count == 1 && answers.Controls[0].Text == "Fin fictive de la discussion." && answers.Controls[0].Enabled
                            && (int)answers.Controls[0].Tag == DialoguePanel.EndChoiceId, "Question without answers lacks the end choice");
                        ((Button)answers.Controls[0]).PerformClick(); Check(Read(peer) == "DV", "End choice does not send DV");
                        Feed(account, "DV"); Application.DoEvents();
                        Check(!drawer.Visible && answers.Controls.Count == 0 && dialog.Portrait == null, "Dialog panel stayed open or kept its portrait after DV");

                        // PNJ sans illustration exportée (PNG corrompu) : volet sans image, sans exception.
                        Feed(account, "DCK-10"); Feed(account, "DQ13"); Application.DoEvents();
                        NoPacket(peer, "Opening a dialog without portrait sent a packet");
                        Check(drawer.Current == dialog && dialog.Portrait == null && npc.PortraitId == 122, "Corrupted portrait was displayed or crashed");
                        Feed(account, "DV"); Application.DoEvents();

                        // Sans les textes du client : numéros et menu de repli, comme avant ce lot.
                        LangData.Clear();
                        Feed(account, "DCK-8"); Feed(account, "DQ12;Nom,500|3;4"); Application.DoEvents();
                        Check(((Label)Get(dialog, "dialogQuestion")).Text.StartsWith("Question n° 12") && answers.Controls[0].Text == "Réponse n° 3", "No-XML fallback differs");
                        Check(npc.NpcName == "PNJ #900" || npc.NpcName.Length > 0, "NPC name lost without texts");
                        Feed(account, "DV"); Application.DoEvents();
                        Check(NpcMenuProvider.ActionsFor(map.GetActor(-8), out bool withTexts).SequenceEqual(new[] { 3, 1 }) && !withTexts, "No-XML menu is not Parler/Acheter-Vendre");
                        Check(string.Join("|", new NpcMenuProvider().Entries(map.GetActor(-8), account.Game).Select(e => e.Texte)) == "Parler|Acheter/Vendre", "No-XML menu labels differ");
                        form.Close();
                    }
                }
            }
        }
        finally
        {
            listener.Stop(); LangData.Clear(); NpcPortraits.Root = previousRoot; Environment.CurrentDirectory = previous;
        }
    }
}
