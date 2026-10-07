using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
using Outil_Azur_complet.Bot.Panels;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Exchanges;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Jobs;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Perso.Inventory;

// Métiers et artisanat (lot F6) selon dofus.aks.Job / Exchange et les interfaces StatsJob, JobOptionsViewer, Craft et CrafterList du
// client 1.34, lus comme StarLoco les envoie (JobStat, JobAction, GameClient.parseJobOption), sur un serveur fictif local avec des textes
// du client synthétiques (métiers, compétences, recettes, objets) : JS avec deux métiers → niveaux et compétences (récolte, atelier),
// JX, JN, JO (envoi par position, réponse JO0 de StarLoco attribuée au métier demandé, refus locaux), EW± (mode public et compétences
// d'un artisan), Ej±, puis l'atelier : ECK3|8;44, EMO+/EMKO+, EMO-/EMKO-, recette reconnue, EK, EmKO+ et EcK;<modèle>, série EK + EMR,
// EA, EMr, Ea, EcEF, EcEI, EL, forgemagie, livre des artisans ECK14/EJF/EJ±, paquets mal formés sans exception, EV, Clear ; enfin les
// volets Métiers, Atelier et Livre des artisans dans une fenêtre de jeu invisible.
internal static class BotCraftSmoke
{
    private const int MapId = 900098;
    private const string SelfGm = "+0;1;0;42;Personnage de test;1;100^100;0";
    private const string Plain = "HhGaeaaaaa";
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
            Application.EnableVisualStyles();
            Run();
            Check(uiErrors.Count == 0, "Interface errors: " + string.Join(" / ", uiErrors.Select(e => e.GetType().Name + " " + e.Message)));
            Console.WriteLine("OK: métiers JS/JX/JN/JR/JO, EW±, Ej±, atelier ECK3/EMO±/EMKO±/EK/EMR/EA/Ea/EMr/EcK/EcEI/EcEF/EL, livre ECK14/EJF/EJ±, volets Métiers, Atelier et Livre des artisans");
        }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
        finally { LangData.Clear(); }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Check(bool value, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0) { if (!value) throw new Exception("Check failed at line " + line); }
    private static object Get(object target, string field) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static IEnumerable<Control> All(Control value) { yield return value; foreach (Control child in value.Controls) foreach (var nested in All(child)) yield return nested; }
    private static void PumpUntil(Func<bool> done, int seconds = 6)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(seconds);
        while (!done()) { if (DateTime.UtcNow > end) throw new TimeoutException("Craft loopback timed out"); Application.DoEvents(); Thread.Sleep(5); }
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
    private static void Expect(Socket socket, string packet, string message)
    {
        string read = Read(socket);
        Check(read == packet, message + " (reçu « " + read + " », attendu « " + packet + " »)");
    }
    private static void NoPacket(Socket socket, string message)
    {
        DateTime end = DateTime.UtcNow.AddMilliseconds(250);
        while (DateTime.UtcNow < end) { Application.DoEvents(); Thread.Sleep(5); }
        Check(socket.Available == 0, message);
    }
    private static ListViewItem RowWithTag(ListView list, object tag) => list.Items.Cast<ListViewItem>().FirstOrDefault(row => Equals(row.Tag, tag));
    private static void Click(object button) => ((Button)button).PerformClick();
    private static void Answer(Form dialog, string choice) => All(dialog).OfType<Button>().First(button => button.Text == choice).PerformClick();
    private static string DialogMessage(Form dialog) => All(dialog).FirstOrDefault(control => control.Name == "dialog-message")?.Text ?? string.Empty;
    private static string Join<T>(IEnumerable<T> values) => string.Join(",", values);
    private static void WriteLang(string folder, string file, string family, string body) =>
        File.WriteAllText(Path.Combine(folder, file), "<?xml version='1.0' encoding='utf-8'?>\n<BotLang famille=\"" + family + "\" langue=\"fr\" version=\"1\" source=\"" + family
            + "_fr_1.swf\">\n" + body + "\n</BotLang>\n", new UTF8Encoding(false));

    private static void Run()
    {
        string previous = Environment.CurrentDirectory;
        string folder = Path.Combine(TestPaths.Work, "bot-craft-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); Environment.CurrentDirectory = folder;
        string lang = Path.Combine(folder, "lang"); Directory.CreateDirectory(lang);
        // Deux métiers de base et une spécialisation ; compétences : récolte (i), atelier (cl), forgemagie.
        WriteLang(lang, "jobs.xml", "jobs",
            "<metier id=\"2\" nom=\"Bûcheron fictif\" specialisation=\"0\" icone=\"2\" />\n<metier id=\"25\" nom=\"Boulanger fictif\" specialisation=\"0\" icone=\"25\" />\n"
            + "<metier id=\"62\" nom=\"Spécialité fictive\" specialisation=\"2\" icone=\"62\" />");
        WriteLang(lang, "skills.xml", "skills",
            "<competence id=\"6\" nom=\"Couper fictif\" metier=\"2\" interactif=\"1\" i=\"303\" />\n<competence id=\"101\" nom=\"Scier fictif\" metier=\"2\" cl=\"2101\" />\n"
            + "<competence id=\"44\" nom=\"Cuire fictif\" metier=\"25\" cl=\"2201,2202,2203\" />\n<competence id=\"1\" nom=\"Forgemager fictif\" metier=\"2\" forgemagie=\"1\" />");
        // Pain : 2 blés + 1 eau ; brioche : blé, sucre, eau ; gâteau : 9 ingrédients (ne tient pas dans 8 cases) ; planche : 2 bois.
        WriteLang(lang, "crafts.xml", "crafts",
            "<entree table=\"CR\" id=\"2201\" valeur=\"[[2301,2],[2302,1]]\" />\n<entree table=\"CR\" id=\"2202\" valeur=\"[[2301,1],[2303,1],[2302,1]]\" />\n"
            + "<entree table=\"CR\" id=\"2203\" valeur=\"[[2301,1],[2302,1],[2303,1],[2304,1],[2305,1],[2306,1],[2307,1],[2308,1],[2309,1]]\" />\n"
            + "<entree table=\"CR\" id=\"2101\" valeur=\"[[303,2]]\" />");
        WriteLang(lang, "items.xml", "items",
            "<objet id=\"2301\" nom=\"Blé fictif\" type=\"41\" gfx=\"1\" niveau=\"1\" pods=\"1\" />\n<objet id=\"2302\" nom=\"Eau fictive\" type=\"41\" gfx=\"2\" niveau=\"1\" pods=\"1\" />\n"
            + "<objet id=\"2303\" nom=\"Sucre fictif\" type=\"41\" gfx=\"3\" niveau=\"1\" pods=\"1\" />\n<objet id=\"2201\" nom=\"Pain fictif\" type=\"33\" gfx=\"4\" niveau=\"1\" pods=\"1\" />\n"
            + "<objet id=\"2202\" nom=\"Brioche fictive\" type=\"33\" gfx=\"5\" niveau=\"1\" pods=\"1\" />\n<objet id=\"2203\" nom=\"Gâteau fictif\" type=\"33\" gfx=\"6\" niveau=\"1\" pods=\"1\" />\n"
            + "<objet id=\"2101\" nom=\"Planche fictive\" type=\"38\" gfx=\"7\" niveau=\"1\" pods=\"1\" />\n<objet id=\"303\" nom=\"Bois fictif\" type=\"38\" gfx=\"8\" niveau=\"1\" pods=\"1\" />\n"
            + "<objet id=\"2401\" nom=\"Épée fictive\" type=\"6\" gfx=\"9\" niveau=\"1\" pods=\"10\" />");
        WriteLang(lang, "lang.xml", "lang",
            "<texte cle=\"NEW_JOB_LEVEL\" valeur=\"Ton métier %1 passe niveau %2 (texte de test).\" />\n<texte cle=\"REMOVE_JOB\" valeur=\"Métier %1 oublié (texte de test).\" />\n"
            + "<texte cle=\"CRAFT_SUCCESS_SELF\" valeur=\"Objet '%1' créé (texte de test).\" />\n<texte cle=\"CRAFT_FAILED\" valeur=\"Recette ratée (texte de test).\" />\n"
            + "<texte cle=\"NO_CRAFT_RESULT\" valeur=\"Recette vide (texte de test).\" />\n<texte cle=\"WRONG_CRAFT_CONFIRM\" valeur=\"Recette inconnue, combiner ? (texte de test)\" />\n"
            + "<texte cle=\"CRAFT\" valeur=\"Artisanat\" />\n<texte cle=\"CRAFT_LOOP_END_INTERRUPT\" valeur=\"Série interrompue (texte de test).\" />\n"
            + "<texte cle=\"CRAFT_LOOP_PROCESS\" valeur=\"Objet %1 sur %2 (texte de test).\" />\n<texte cle=\"CRAFTER_REFERENCE_ADD\" valeur=\"Référencé comme %1 (texte de test).\" />\n"
            + "<texte cle=\"CRAFTER_REFERENCE_REMOVE\" valeur=\"Plus référencé comme %1 (texte de test).\" />\n<texte cle=\"IN_WORKSHOP\" valeur=\"En atelier\" />\n"
            + "<texte cle=\"OUTSIDE_WORKSHOP\" valeur=\"Hors atelier\" />");
        LangData.Clear();
        Check(LangData.Load(lang) == 5 && new[] { "jobs", "skills", "crafts", "items", "lang" }.All(LangData.IsLoaded), "Synthetic lang files not loaded: " + string.Join(" / ", LangData.LoadWarnings));
        Check(JobCatalog.JobName(25) == "Boulanger fictif" && JobCatalog.SpecializationOf(62) == 2 && JobCatalog.IsCraftSkill(44) == true && JobCatalog.IsCraftSkill(6) == false
            && JobCatalog.IsForgemagusSkill(1) && !JobCatalog.IsForgemagusSkill(44) && JobCatalog.HarvestedItem(6) == 303, "JobCatalog does not read the client texts");
        Check(Join(JobCatalog.Recipes(44, 8).Select(recipe => recipe.ResultTemplateId)) == "2202,2201" && JobCatalog.Recipes(44, 2).Count == 1 && JobCatalog.Ingredients(2203).Count == 9,
            "Known recipes differ: " + Join(JobCatalog.Recipes(44, 8).Select(recipe => recipe.Name)));

        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var logs = new ConcurrentQueue<string>();
        try
        {
            MessagesReception.Init();
            Check(ExchangeRegistry.IsRegistered(ExchangeTypes.Craft) && ExchangeRegistry.IsRegistered(ExchangeTypes.CrafterList), "Exchange types 3 and 14 are not registered");
            Map.AllBotMaps[MapId] = new Map { MapID = MapId, MapWidth = 3, MapHeight = 4, X = 4, Y = -18, MapData = string.Concat(Enumerable.Repeat(Plain, 18)) };
            using (var account = new Accounts(new AccountConfig("synthetic-craft", "synthetic", "loopback")))
            {
                account.Logger.log_event += (entry, color) => logs.Enqueue(entry.message);
                Task<Socket> accept = listener.AcceptSocketAsync();
                Complete(account.Connexion.ConnectToServer(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port)); Complete(accept);
                using (Socket peer = accept.Result)
                {
                    peer.ReceiveTimeout = 6000;
                    account.Game.character.SetPerso_Data(42, "Personnage de test", 25, 0, 8);
                    Feed(account, "GDM|" + MapId + "|date|key"); Expect(peer, "GI", "GDM did not request GI");
                    Feed(account, "GM|" + SelfGm);
                    account.AccountStates = AccountStates.CONNECTED_INACTIVE;
                    // Sac : 10 blés, 5 eaux, 1 sucre, une épée équipée, 3 bois.
                    Feed(account, "OAKO3e8~8fd~a~~;3e9~8fe~5~~;3ea~8ff~1~~;3eb~961~1~1~;3ec~12f~3~~");
                    InventoryClass bag = account.Game.character.Inventory;
                    Check(bag.GetByInventoryId(1000)?.Qua == 10 && bag.GetByInventoryId(1000).ID == 2301 && bag.GetByInventoryId(1003).IsEquipped() && bag.GetByInventoryId(1004)?.Qua == 3,
                        "Synthetic inventory was not read");

                    Jobs(account, peer, logs);
                    Workshop(account, peer, logs);
                    Book(account, peer, logs);
                    Windows(account, peer, logs);
                    NoPacket(peer, "Unexpected packet at the end of the test");
                }
            }
        }
        finally { listener.Stop(); Environment.CurrentDirectory = previous; }
    }

    /// <summary>JS, JX, JN, JO (envoi, réponse JO0, refus), EW±, Ej±, paquets mal formés.</summary>
    private static void Jobs(Accounts account, Socket peer, ConcurrentQueue<string> logs)
    {
        JobsActions jobs = account.Game.Interactions.Jobs;
        ExchangeRegistry registry = account.Game.Interactions.Exchanges;
        Check(jobs != null && jobs.Craft != null && jobs.Book != null && registry.Get<CraftExchange>() == jobs.Craft && registry.For(ExchangeTypes.Craft) == jobs.Craft
            && registry.For(ExchangeTypes.CrafterList) == jobs.Book, "Interactions.Jobs is not wired to the exchange registry");
        Check(!Sent(jobs.SetPublicModeAsync(true)) && !Sent(jobs.SetOptionsAsync(25, 0, 2)), "Job actions accepted without any job");
        NoPacket(peer, "Refused job actions reached the server");

        // JS : deux métiers, compétences de récolte (durée, quantités) et d'atelier (cases, chance).
        Feed(account, "JS|2;6~1~2~0~3000,101~3~0~0~100|25;44~8~0~0~90");
        var list = account.Game.character.GetJobsSnapshot();
        Check(list.Length == 2 && list[0].ID == 2 && list[1].ID == 25 && list[0].name == "Bûcheron fictif" && list[0].Skills.Count == 2 && list[1].Skills.Count == 1, "JS did not create the two jobs");
        JobSkills cut = list[0].Skills.First(skill => skill.Id == 6), saw = list[0].Skills.First(skill => skill.Id == 101), bake = list[1].Skills[0];
        Check(!cut.CanCraft && cut.QuaMini == 1 && cut.QuaMax == 2 && cut.DurationMs == 3000 && cut.Slots == 0 && cut.Name == "Couper fictif"
            && saw.CanCraft && saw.Slots == 3 && saw.Chance == 100 && bake.CanCraft && bake.Slots == 8 && bake.Chance == 90 && bake.Describe().StartsWith("8 ")
            && list[0].IsCraftJob && list[0].MaxSkillSlots == 3 && list[1].MaxSkillSlots == 8, "JS skills differ: " + cut.Describe() + " / " + bake.Describe());
        Check(list[1].Recipes.Select(recipe => recipe.ResultTemplateId).SequenceEqual(new[] { 2202, 2201 }) && list[1].CanCraft(44, 2201) && !list[1].CanCraft(44, 2101), "Job recipes differ");
        Feed(account, "JS|25;44~8~0~0~100");
        Check(account.Game.character.GetJobsSnapshot()[1].Skills.Count == 1 && account.Game.character.GetJobsSnapshot()[1].Skills[0].Chance == 100, "JS did not replace the skill list");

        // JX : niveaux et expérience ; un métier inconnu (spécialisation) est ajouté.
        Feed(account, "JX|2;10;1000;1500;2000;|25;5;100;150;200;|62;1;0;0;20;");
        list = account.Game.character.GetJobsSnapshot();
        Check(list.Length == 3 && list[0].Level == 10 && list[0].BaseXP == 1000 && list[0].ActualXP == 1500 && list[0].NextXP == 2000 && list[0].GetXpPercentage == 50
            && list[1].Level == 5 && list[1].GetXpPercentage == 50 && list[2].ID == 62 && !list[2].IsCraftJob, "JX differs");
        Feed(account, "JX|62;100;0;5000;-1;");
        Check(list[2].Level == 100 && list[2].NextXP == 0 && list[2].GetXpPercentage == 100, "JX at level 100 differs");

        // JN : nouveau niveau, texte NEW_JOB_LEVEL dans le chat.
        int levelJob = 0, levelValue = 0;
        jobs.LevelUp += (job, level) => { levelJob = job; levelValue = level; };
        Feed(account, "JN25|6");
        Check(list[1].Level == 6 && levelJob == 25 && levelValue == 6 && jobs.LastMessage == "Ton métier Boulanger fictif passe niveau 6 (texte de test)."
            && account.Game.Chat.Messages.Any(message => message.Text == jobs.LastMessage), "JN differs: " + jobs.LastMessage);

        // JO : refus locaux, envoi par position, réponse JO0 de StarLoco attribuée au métier demandé, puis JO0 sans demande.
        Check(!Sent(jobs.SetOptionsAsync(99, 1, 2)) && !Sent(jobs.SetOptionsAsync(62, 1, 2)) && !Sent(jobs.SetOptionsAsync(25, 8, 2)) && !Sent(jobs.SetOptionsAsync(25, 1, 9))
            && !Sent(jobs.SetOptionsAsync(2, 0, 7)) && !Sent(jobs.SetOptionsAsync(25, 1, 1)), "Invalid JO options were accepted");
        NoPacket(peer, "Refused JO reached the server");
        Check(Sent(jobs.SetOptionsAsync(25, JobOptions.Compose(true, true, false), 5))); Expect(peer, "JO1|3|5", "SetOptions does not send JO<position>|<options>|<cases>");
        Feed(account, "JO0|3|5");
        Check(list[1].Options.Flags == 3 && list[1].Options.MinSlots == 5 && list[1].Options.IsNotFree && list[1].Options.IsFreeIfFailed && list[0].Options.Flags == 0,
            "JO0 answer was not attributed to the requested job");
        Feed(account, "JO0|4|2");
        Check(list[0].Options.Flags == 4 && list[0].Options.ResourcesNeeded && list[1].Options.Flags == 3, "JO0 without a request did not change the first job");

        // EW : mode public du personnage, compétences d'un artisan public.
        Check(Sent(jobs.SetPublicModeAsync(true))); Expect(peer, "EW+", "Public mode does not send EW+");
        Feed(account, "EW+");
        Check(jobs.PublicMode, "EW+ did not enable the public mode");
        Feed(account, "EW+77|44;101"); Check(jobs.PublicSkillsOf(77).SequenceEqual(new short[] { 44, 101 }), "EW+<id>|<skills> differs");
        Feed(account, "EW-77"); Check(jobs.PublicSkillsOf(77).Count == 0, "EW-<id> kept the skills");
        Check(Sent(jobs.SetPublicModeAsync(false))); Expect(peer, "EW-", "Public mode does not send EW-");
        Feed(account, "EW-"); Check(!jobs.PublicMode, "EW- did not disable the public mode");

        // Ej : référencement dans le livre des artisans.
        Feed(account, "Ej+25");
        Check(jobs.ReferencedJobs.SequenceEqual(new[] { 25 }) && jobs.LastMessage == "Référencé comme Boulanger fictif (texte de test).", "Ej+ differs: " + jobs.LastMessage);
        Feed(account, "Ej-25");
        Check(jobs.ReferencedJobs.Count == 0 && jobs.LastMessage == "Plus référencé comme Boulanger fictif (texte de test).", "Ej- differs");

        // Paquets mal formés : journalisés, jamais fatals, état inchangé.
        int before = logs.Count;
        foreach (string packet in new[] { "JS", "JS|x;6~1", "JS|2;zz", "JX|2;a", "JX", "JN", "JNx|2", "JR", "JRx", "JO", "JOa|b|c", "EW", "EWx", "EW+abc|1", "Ej", "Ej+x", "Ej?2" })
            Feed(account, packet);
        list = account.Game.character.GetJobsSnapshot();
        Check(list.Length == 3 && list[0].Level == 10 && list[0].Skills.Count == 2 && list[1].Options.Flags == 3 && logs.Skip(before).Count(entry => entry.Contains("illisible")) >= 10,
            "Malformed job packets changed the state or were not logged");
        NoPacket(peer, "Malformed job packets sent a packet");
    }

    /// <summary>Atelier : ECK3, EMO± / EMKO±, EK, EmKO+, EcK, série EMR / EA / EMr / Ea, EcEF, EcEI, EL, forgemagie, EV.</summary>
    private static void Workshop(Accounts account, Socket peer, ConcurrentQueue<string> logs)
    {
        CraftExchange craft = account.Game.Interactions.Jobs.Craft;
        ExchangeRegistry registry = account.Game.Interactions.Exchanges;
        Check(!craft.IsOpen && craft.Slots == 0 && craft.Ingredients.Count == 0 && craft.MaxRepeat == 0, "Initial workshop state differs");
        Check(!Sent(craft.AddIngredientAsync(1000, 1)) && !Sent(craft.RemoveIngredientAsync(1000, 1)) && !Sent(craft.CombineAsync()) && !Sent(craft.StopRepeatAsync())
            && !Sent(craft.ReplayAsync()) && !Sent(craft.LeaveAsync()), "Workshop actions accepted without an open workshop");
        Feed(account, "EcK;2201"); Feed(account, "EA1"); Feed(account, "Ea1");
        Check(craft.LastOutcome == CraftOutcome.None, "Ec without a workshop was applied");
        NoPacket(peer, "Refused workshop actions reached the server");

        // ECK3|8;44 : 8 cases, compétence 44 du Boulanger.
        Feed(account, "ECK3|8;44");
        Check(craft.IsOpen && craft.Slots == 8 && craft.SkillId == 44 && craft.SkillName == "Cuire fictif" && !craft.IsForgemagus && craft.Job?.ID == 25 && craft.ExchangeType == 3
            && registry.Current == craft && account.AccountStates == AccountStates.EXCHANGE && craft.Recipes.Select(recipe => recipe.ResultTemplateId).SequenceEqual(new[] { 2202, 2201 }),
            "ECK3 did not open the workshop");

        // Refus locaux : objet équipé, inconnu, quantité trop grande.
        Check(!Sent(craft.AddIngredientAsync(1003, 1)) && !Sent(craft.AddIngredientAsync(9999, 1)) && !Sent(craft.AddIngredientAsync(1000, 11)) && !Sent(craft.AddIngredientAsync(1000, 0))
            && !Sent(craft.RemoveIngredientAsync(1000, 1)) && !Sent(craft.CombineAsync()), "Invalid ingredients were accepted");
        NoPacket(peer, "Refused ingredients reached the server");

        // EMO+ → EMKO+ ; recette reconnue quand blé et eau sont posés ; EMO- → EMKO-.
        Check(Sent(craft.AddIngredientAsync(1000, 2))); Expect(peer, "EMO+1000|2", "AddIngredient does not send EMO+<objet>|<quantité>");
        Check(craft.Ingredients.Count == 0, "Ingredient placed before the server answer");
        Feed(account, "EMKO+1000|2");
        Check(craft.Ingredients.Count == 1 && craft.Ingredients[0].TemplateId == 2301 && craft.Ingredients[0].Quantity == 2 && craft.Ingredients[0].Name == "Blé fictif"
            && craft.ExpectedRecipe == null && craft.PlacedQuantity(1000) == 2, "EMKO+ was not read");
        Check(!Sent(craft.AddIngredientAsync(1000, 9)), "Ingredient quantity above the remaining stack was accepted");
        Check(Sent(craft.AddIngredientAsync(1001, 1))); Expect(peer, "EMO+1001|1", "Second EMO+ differs");
        Feed(account, "EMKO+1001|1");
        Check(craft.ExpectedRecipe?.ResultTemplateId == 2201 && craft.MaxRepeat == 5 && craft.PlacedByTemplate()[2301] == 2, "Recipe not recognised or MaxRepeat differs: " + craft.MaxRepeat);
        Check(Sent(craft.RemoveIngredientAsync(1001, 1))); Expect(peer, "EMO-1001|1", "RemoveIngredient does not send EMO-<objet>|<quantité>");
        Feed(account, "EMKO-1001");
        Check(craft.Ingredients.Count == 1 && craft.ExpectedRecipe == null, "EMKO- was not applied");
        Check(Sent(craft.AddIngredientAsync(1001, 1))); Expect(peer, "EMO+1001|1", "EMO+ after EMO- differs"); Feed(account, "EMKO+1001|1");

        // EK → EmKO+ (objet créé) et EcK;<modèle> : ingrédients oubliés, résultat gardé.
        Check(!Sent(craft.CombineAsync(6)), "Combine above MaxRepeat was accepted");
        Check(Sent(craft.CombineAsync())); Expect(peer, "EK", "Combine does not send EK");
        Check(craft.IsCombinePending && !Sent(craft.CombineAsync()), "Second combine accepted while pending");
        Feed(account, "EmKO+5000|1|2201|"); Feed(account, "EcK;2201");
        Check(!craft.IsCombinePending && craft.LastOutcome == CraftOutcome.Success && craft.LastResultTemplateId == 2201 && craft.Result?.TemplateId == 2201 && craft.Result.Name == "Pain fictif"
            && craft.Ingredients.Count == 0 && craft.LastMessage == "Objet 'Pain fictif' créé (texte de test)."
            && account.Game.Chat.Messages.Any(message => message.Text == craft.LastMessage), "EcK differs: " + craft.LastMessage);

        // Série de 3 : EK puis EMR2 ; EA ; EcK pendant la série garde les ingrédients ; EcEF ; EMr ; Ea2.
        Feed(account, "EMKO+1000|2"); Feed(account, "EMKO+1001|1");
        Check(Sent(craft.CombineAsync(3))); Expect(peer, "EK", "Loop does not start with EK"); Expect(peer, "EMR2", "Loop does not send EMR<n-1>");
        Check(craft.IsLooping && craft.LoopTotal == 2 && !Sent(craft.AddIngredientAsync(1002, 1)) && !Sent(craft.ReplayAsync()), "Loop state differs");
        Feed(account, "EA2");
        Check(craft.LoopRemaining == 2 && craft.LastMessage == "Objet 1 sur 3 (texte de test).", "EA differs: " + craft.LastMessage);
        Feed(account, "EcK;2201");
        Check(craft.Ingredients.Count == 2 && craft.IsLooping, "EcK during a loop forgot the ingredients");
        Feed(account, "EA1"); Feed(account, "EcEF");
        Check(craft.LastOutcome == CraftOutcome.Failed && craft.Result == null && craft.LastMessage == "Recette ratée (texte de test).", "EcEF differs");
        Check(Sent(craft.StopRepeatAsync())); Expect(peer, "EMr", "Stop does not send EMr");
        Feed(account, "Ea2");
        Check(!craft.IsLooping && craft.LastLoopEnd == 2 && craft.Ingredients.Count == 0 && craft.LastMessage == "Série interrompue (texte de test).", "Ea2 differs");
        Check(!Sent(craft.StopRepeatAsync()), "Stop accepted without a loop");

        // EcEI : sucre seul, recette inconnue.
        Check(Sent(craft.AddIngredientAsync(1002, 1))); Expect(peer, "EMO+1002|1", "EMO+ (sucre) differs"); Feed(account, "EMKO+1002|1");
        Check(craft.ExpectedRecipe == null && craft.MaxRepeat == 1, "Sugar alone is recognised");
        Check(Sent(craft.CombineAsync())); Expect(peer, "EK", "Combine (sugar) does not send EK");
        Feed(account, "EcEI");
        Check(craft.LastOutcome == CraftOutcome.NoResult && craft.Ingredients.Count == 0 && craft.LastMessage == "Recette vide (texte de test).", "EcEI differs");

        // EL : « Mémoire » ; refusé quand des ingrédients sont posés.
        Check(Sent(craft.ReplayAsync())); Expect(peer, "EL", "Replay does not send EL");
        Feed(account, "EMKO+1000|2"); Feed(account, "EMKO+1001|1");
        Check(craft.Ingredients.Count == 2 && !Sent(craft.ReplayAsync()), "EL ingredients differ");

        // Paquets mal formés pendant l'atelier.
        int before = logs.Count;
        foreach (string packet in new[] { "EMKO", "EMKOx", "EMKO+abc|2", "EMKO+1000|0", "EMKO+1000", "EmKO+1|0|0", "EmKOx", "EcK;abc", "EcK", "EcEZ", "EcE", "EAx", "EA-1", "Eax", "EJ?", "EJ+" })
            Feed(account, packet);
        Check(craft.IsOpen && craft.Ingredients.Count == 2 && logs.Skip(before).Any(entry => entry.Contains("illisible")), "Malformed workshop packets changed the state");
        NoPacket(peer, "Malformed workshop packets sent a packet");
        Check(Sent(craft.CombineAsync())); Expect(peer, "EK", "Combine before a malformed answer does not send EK");
        Feed(account, "EcK;abc");
        Check(!craft.IsCombinePending && craft.Ingredients.Count == 2 && craft.LastOutcome == CraftOutcome.NoResult, "Malformed Ec did not release the pending combine or changed the workshop");

        // EV : fermeture.
        Check(Sent(craft.LeaveAsync())); Expect(peer, "EV", "Leave does not send EV");
        Feed(account, "EV");
        Check(!craft.IsOpen && craft.Slots == 0 && craft.SkillId == 0 && craft.Ingredients.Count == 0 && craft.Result == null && registry.Current == null
            && account.AccountStates == AccountStates.CONNECTED_INACTIVE, "EV did not close the workshop");

        // Forgemagie : ouverte mais non assistée ; ECK3 illisible ouvre quand même.
        Feed(account, "ECK3|3;1");
        Check(craft.IsOpen && craft.IsForgemagus && craft.Recipes.Count == 0 && craft.Job == null, "Forgemagus workshop differs");
        Feed(account, "EMKO+1004|1");
        Check(craft.ExpectedRecipe == null && craft.Ingredients.Count == 1, "Forgemagus ingredient differs");
        Feed(account, "EV");
        Feed(account, "ECK3|x");
        Check(craft.IsOpen && craft.Slots == 0 && craft.SkillId == 0, "Unreadable ECK3 did not open the workshop");
        Feed(account, "EV");

        // Clear : l'atelier et le mode public sont oubliés sans rien envoyer.
        Feed(account, "ECK3|8;44"); Feed(account, "EMKO+1000|2"); Feed(account, "EW+"); Feed(account, "Ej+25");
        account.Game.Interactions.Clear();
        Check(!craft.IsOpen && craft.Ingredients.Count == 0 && !account.Game.Interactions.Jobs.PublicMode && account.Game.Interactions.Jobs.ReferencedJobs.Count == 0
            && registry.Current == null && account.AccountStates == AccountStates.CONNECTED_INACTIVE, "Clear kept the workshop");
        NoPacket(peer, "Clear sent a packet");
    }

    /// <summary>Livre des artisans : ECK14, EJF, EJ+ / EJ- (ajout, remplacement, retrait), EV.</summary>
    private static void Book(Accounts account, Socket peer, ConcurrentQueue<string> logs)
    {
        CrafterBook book = account.Game.Interactions.Jobs.Book;
        Check(!Sent(book.FindCraftersAsync(25)) && !Sent(book.LeaveAsync()), "Book actions accepted without an open book");
        Feed(account, "EJ+25;77;Artisan fictif;30;8731;1;1;0;ff,0,0;;3,5");
        Check(book.Crafters.Count == 0, "EJ+ without a book was applied");
        int before = logs.Count;
        Feed(account, "ECK14|25;2;x");
        Check(book.IsOpen && book.Jobs.SequenceEqual(new[] { 25, 2 }) && account.AccountStates == AccountStates.EXCHANGE && logs.Skip(before).Any(entry => entry.Contains("ECK14")),
            "ECK14 differs: " + Join(book.Jobs));
        Check(!Sent(book.FindCraftersAsync(0))); NoPacket(peer, "EJF0 reached the server");
        Check(Sent(book.FindCraftersAsync(25)) && book.SelectedJob == 25); Expect(peer, "EJF25", "FindCrafters does not send EJF<métier>");
        Feed(account, "EJ+25;77;Artisan fictif;30;8731;1;1;0;ff,0,0;;3,5");
        Feed(account, "EJ+25;78;Autre fictif;12;1000;0;8;1;-1,-1,-1;;0,2");
        Crafter first = book.Crafters.FirstOrDefault(crafter => crafter.Id == "77");
        Check(book.Crafters.Count == 2 && first != null && first.Name == "Artisan fictif" && first.JobLevel == 30 && first.MapId == 8731 && first.InWorkshop && first.Breed == 1
            && first.Options.Flags == 3 && first.Options.MinSlots == 5 && first.Colors.Length == 3 && first.Location == "carte 8731", "EJ+ differs");
        Feed(account, "EJ+25;77;Artisan fictif;31;8731;1;1;0;ff,0,0;;3,5");
        Check(book.Crafters.Count == 2 && book.Crafters.First(crafter => crafter.Id == "77").JobLevel == 31, "EJ+ did not replace the crafter");
        Feed(account, "EJ-25;78");
        Check(book.Crafters.Count == 1 && book.Crafters[0].Id == "77", "EJ- did not remove the crafter");
        Feed(account, "EJ+x;;"); Feed(account, "EJ-25"); Feed(account, "EJ*");
        Check(book.Crafters.Count == 1, "Malformed EJ changed the list");
        Check(Sent(book.FindCraftersAsync(2)) && book.Crafters.Count == 0); Expect(peer, "EJF2", "Second EJF differs");
        Check(Sent(book.LeaveAsync())); Expect(peer, "EV", "Book leave does not send EV");
        Feed(account, "EV");
        Check(!book.IsOpen && book.Jobs.Count == 0 && book.Crafters.Count == 0 && book.SelectedJob == 0 && account.AccountStates == AccountStates.CONNECTED_INACTIVE, "EV did not close the book");
    }

    /// <summary>Volets Métiers, Atelier et Livre des artisans dans une fenêtre de jeu invisible.</summary>
    private static void Windows(Accounts account, Socket peer, ConcurrentQueue<string> logs)
    {
        CraftExchange craft = account.Game.Interactions.Jobs.Craft;
        using (var form = new GameClientFullform(account))
        {
            form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); Application.DoEvents();
            PanelHost drawer = form.Panels;
            var jobsPanel = drawer.Get<JobsPanel>(); var craftPanel = drawer.Get<CraftPanel>(); var bookPanel = drawer.Get<CrafterListPanel>();
            Check(jobsPanel != null && craftPanel != null && bookPanel != null && drawer.Current == null, "Job panels are not registered in the drawer");
            NoPacket(peer, "Opening the game window sent a packet");

            // Métiers : cases (deux métiers puis la spécialisation dans le second groupe), niveau, jauge, compétences.
            drawer.Show(jobsPanel); Application.DoEvents();
            var strip = (JobStrip)Get(jobsPanel, "strip"); var gauge = (XpGauge)Get(jobsPanel, "gauge"); var skills = (ListView)Get(jobsPanel, "skillList");
            Check(strip.Slots.Count == 6 && strip.Slots[0]?.ID == 2 && strip.Slots[1]?.ID == 25 && strip.Slots[2] == null && strip.Slots[3]?.ID == 62 && strip.SelectedJobId == 2
                && ((Label)Get(jobsPanel, "jobName")).Text == "Bûcheron fictif" && ((Label)Get(jobsPanel, "jobLevel")).Text.Contains("10") && gauge.Percent == 50
                && skills.Items.Count == 2 && RowWithTag(skills, (short)6) != null && RowWithTag(skills, (short)101).SubItems[1].Text.StartsWith("3 "), "Jobs panel content differs");
            Check(jobsPanel.SelectJob(25) && !jobsPanel.SelectJob(99) && ((Label)Get(jobsPanel, "jobName")).Text == "Boulanger fictif" && skills.Items.Count == 1, "Job selection differs");
            jobsPanel.ShowTab(JobsPanel.RecipesTab);
            PumpUntil(() => jobsPanel.ShownRecipes.Count == 2);
            var recipes = (ListView)Get(jobsPanel, "recipeList");
            Check(recipes.Items.Count == 2 && recipes.Items[0].Text == "Brioche fictive" && recipes.Items[1].SubItems[1].Text == "2", "Recipe rows differ");

            // Options : cases cochées depuis JO, modification, Sauvegarder → JO1|4|4, réponse JO0 → cases rechargées.
            jobsPanel.ShowTab(JobsPanel.OptionsTab); Application.DoEvents();
            var notFree = (CheckBox)Get(jobsPanel, "notFree"); var freeIfFailed = (CheckBox)Get(jobsPanel, "freeIfFailed"); var resources = (CheckBox)Get(jobsPanel, "resourcesNeeded");
            var minSlots = (NumericUpDown)Get(jobsPanel, "minSlots"); var save = (Control)Get(jobsPanel, "saveOptions"); var toggle = (Control)Get(jobsPanel, "publicToggle");
            Check(notFree.Checked && freeIfFailed.Checked && freeIfFailed.Enabled && !resources.Checked && minSlots.Value == 5 && minSlots.Maximum == 8 && minSlots.Visible && save.Enabled,
                "Options tab differs");
            notFree.Checked = false; Application.DoEvents();
            Check(!freeIfFailed.Enabled, "FREE_IF_FAILED stays enabled without NOT_FREE");
            resources.Checked = true; minSlots.Value = 4;
            Click(save); Expect(peer, "JO1|4|4", "Save does not send JO<position>|<options>|<cases>");
            Application.DoEvents();
            Check(!notFree.Checked && resources.Checked && minSlots.Value == 4, "Options reverted before the server answer");
            FeedFromNetwork(account, "JO0|4|4");
            PumpUntil(() => account.Game.character.GetJobsSnapshot()[1].Options.Flags == 4);
            Application.DoEvents();
            Check(!notFree.Checked && resources.Checked && minSlots.Value == 4 && ((Label)Get(jobsPanel, "optionsState")).Text.Contains("Boulanger fictif")
                && !((Label)Get(jobsPanel, "optionsState")).Text.Contains("attente"), "Options tab after JO differs");
            jobsPanel.SelectJob(62); Application.DoEvents();
            Check(!save.Enabled && !notFree.Enabled && !minSlots.Visible, "Options enabled for a job without workshop");

            // Mode public : Activer → EW+ ; EW+ du serveur → bouton Désactiver.
            Check(toggle.Enabled && toggle.Text == "Activer", "Public toggle differs");
            Click(toggle); Expect(peer, "EW+", "Public toggle does not send EW+");
            FeedFromNetwork(account, "EW+");
            PumpUntil(() => toggle.Text == "Désactiver");
            Check(((Label)Get(jobsPanel, "publicState")).Text.Contains("Actif"), "Public state label differs");
            FeedFromNetwork(account, "Ej+25");
            PumpUntil(() => ((Label)Get(jobsPanel, "referenceState")).Text.Contains("Boulanger fictif"));

            // JN depuis le fil réseau : niveau dans la case et la fiche.
            jobsPanel.SelectJob(25);
            FeedFromNetwork(account, "JN25|7");
            PumpUntil(() => ((Label)Get(jobsPanel, "jobLevel")).Text.Contains("7"));
            Check(strip.Slots[1].Level == 7, "JN did not refresh the strip");

            // Atelier : ECK3|8;44 depuis le fil réseau → volet ; 8 cases ; recettes ; sac filtré.
            FeedFromNetwork(account, "ECK3|8;44");
            PumpUntil(() => drawer.Current == craftPanel);
            PumpUntil(() => craftPanel.RecipesReady);
            CraftSlotsView slots = craftPanel.SlotsView;
            var bagList = (ListView)Get(craftPanel, "bagList"); var recipeList = (ListView)Get(craftPanel, "recipeList"); var useful = (CheckBox)Get(craftPanel, "usefulOnly");
            var place = (Control)Get(craftPanel, "placeButton"); var placeQuantity = (NumericUpDown)Get(craftPanel, "placeQuantity"); var remove = (Control)Get(craftPanel, "removeButton");
            var combine = (Control)Get(craftPanel, "combineButton"); var repeat = (NumericUpDown)Get(craftPanel, "repeatCount"); var stop = (Control)Get(craftPanel, "stopButton");
            var replay = (Control)Get(craftPanel, "replayButton"); var placeRecipe = (Control)Get(craftPanel, "placeRecipeButton"); var leave = (Control)Get(craftPanel, "leaveButton");
            Check(slots.SlotCount == 8 && slots.CellCount == 8 && slots.Items.Count == 0 && ((Label)Get(craftPanel, "heading")).Text.Contains("Cuire fictif")
                && ((Label)Get(craftPanel, "heading")).Text.Contains("8") && ((Label)Get(craftPanel, "subheading")).Text.Contains("Boulanger fictif")
                && craftPanel.KnownRecipes.Count == 2 && recipeList.Items.Count == 2 && !combine.Enabled && replay.Enabled && leave.Enabled && !stop.Enabled, "Craft panel content differs");
            Check(slots.CellBounds(7).Bottom <= slots.ClientSize.Height && slots.CellBounds(7).Right < slots.ResultBounds.Left, "The 8 cells do not fit beside the result cell");
            Check(bagList.Items.Count == 3 && RowWithTag(bagList, 1000u) != null && RowWithTag(bagList, 1004u) == null && RowWithTag(bagList, 1003u) == null, "Useful filter differs");
            Check(recipeList.Items.Cast<ListViewItem>().First(row => row.Text == "Pain fictif").SubItems[2].Text == "× 5"
                && recipeList.Items.Cast<ListViewItem>().First(row => row.Text == "Brioche fictive").SubItems[2].Text == "× 1", "Possible counts differ");
            useful.Checked = false; Application.DoEvents();
            Check(bagList.Items.Count == 4 && RowWithTag(bagList, 1004u) != null && RowWithTag(bagList, 1003u) == null, "Unfiltered bag differs");

            // Poser → EMO+ ; ingrédient seul : aucune recette, Combiner demande confirmation (Non n'envoie rien).
            RowWithTag(bagList, 1000u).Selected = true; Application.DoEvents();
            placeQuantity.Value = 2; Click(place); Expect(peer, "EMO+1000|2", "Place button does not send EMO+<objet>|<quantité>");
            FeedFromNetwork(account, "EMKO+1000|2");
            PumpUntil(() => slots.Items.Count == 1);
            Check(((Label)Get(craftPanel, "expectedText")).Text.Contains("Aucune recette connue") && combine.Enabled && !replay.Enabled && slots.PreviewTemplateId == 0
                && RowWithTag(bagList, 1000u).SubItems[1].Text == "8", "Single ingredient state differs");
            Click(combine); PumpUntil(() => craftPanel.CombineDialog != null);
            Form ask = craftPanel.CombineDialog;
            Check(ask.Text == "Artisanat" && DialogMessage(ask) == "Recette inconnue, combiner ? (texte de test)", "WRONG_CRAFT_CONFIRM differs: " + DialogMessage(ask));
            Answer(ask, "Non"); PumpUntil(() => craftPanel.CombineDialog == null);
            NoPacket(peer, "« Non » sent EK");

            // Retirer → EMO- ; Poser la recette → un EMO+ par ingrédient ; recette reconnue en transparence.
            Check(slots.SelectItem(1000) && remove.Enabled); Click(remove); Expect(peer, "EMO-1000|2", "Remove button does not send EMO-<objet>|<quantité>");
            FeedFromNetwork(account, "EMKO-1000");
            PumpUntil(() => slots.Items.Count == 0);
            craftPanel.ShowTab(CraftPanel.RecipesTab);
            recipeList.Items.Cast<ListViewItem>().First(row => row.Text == "Pain fictif").Selected = true; Application.DoEvents();
            Check(placeRecipe.Enabled && ((Label)Get(craftPanel, "recipeDetail")).Text.Contains("2 × Blé fictif"), "Recipe detail differs");
            Click(placeRecipe); Expect(peer, "EMO+1000|2", "Place recipe does not send the first EMO+"); Expect(peer, "EMO+1001|1", "Place recipe does not send the second EMO+");
            FeedFromNetwork(account, "EMKO+1000|2"); FeedFromNetwork(account, "EMKO+1001|1");
            PumpUntil(() => craftPanel.ExpectedRecipe?.ResultTemplateId == 2201);
            Check(slots.Items.Count == 2 && slots.PreviewTemplateId == 2201 && ((Label)Get(craftPanel, "expectedText")).Text.Contains("Recette reconnue : Pain fictif") && repeat.Maximum == 5
                && repeat.Enabled && !placeRecipe.Enabled, "Recognised recipe state differs");

            // Série de 2 : EK + EMR1 sans confirmation ; Stop → EMr ; EA, EmKO+, EcK, Ea2 → résultat affiché.
            repeat.Value = 2; Click(combine); Expect(peer, "EK", "Combine button does not send EK"); Expect(peer, "EMR1", "Combine button does not send EMR<n-1>");
            Check(craftPanel.CombineDialog == null, "Confirmation asked for a recognised recipe");
            PumpUntil(() => stop.Enabled);
            Check(!combine.Enabled && !place.Enabled, "Buttons enabled during a loop");
            Click(stop); Expect(peer, "EMr", "Stop button does not send EMr");
            FeedFromNetwork(account, "EA1"); FeedFromNetwork(account, "EmKO+5001|1|2201|"); FeedFromNetwork(account, "EcK;2201"); FeedFromNetwork(account, "Ea2");
            PumpUntil(() => !craft.IsLooping && slots.Items.Count == 0);
            Check(slots.Result?.TemplateId == 2201 && slots.Outcome == CraftOutcome.Success && ((Label)Get(craftPanel, "craftStatus")).Text.Contains("Série interrompue (texte de test).")
                && !stop.Enabled && replay.Enabled, "Loop end state differs: " + ((Label)Get(craftPanel, "craftStatus")).Text);
            FeedFromNetwork(account, "EcEF");
            PumpUntil(() => slots.Outcome == CraftOutcome.Failed);
            Check(slots.Result == null, "Failure kept the result");
            Click(replay); Expect(peer, "EL", "Replay button does not send EL");

            // Fermeture : × → EV, le volet attend le serveur.
            drawer.RequestClose(craftPanel); Expect(peer, "EV", "User close does not send EV");
            Check(drawer.Current == craftPanel, "Craft panel closed before the server");
            Feed(account, "EV"); Application.DoEvents();
            Check(!drawer.IsOpen(craftPanel) && slots.SlotCount == 0, "Craft panel stayed open after EV");

            // Livre des artisans : ECK14 → volet ; choix du métier → EJF ; EJ+ → lignes ; Fermer → EV.
            FeedFromNetwork(account, "ECK14|25;2");
            PumpUntil(() => drawer.Current == bookPanel);
            var jobChoice = (ComboBox)Get(bookPanel, "jobChoice"); var crafters = (ListView)Get(bookPanel, "crafterList"); var bookLeave = (Control)Get(bookPanel, "leaveButton");
            Check(jobChoice.Items.Count == 2 && jobChoice.Items[0].ToString() == "Boulanger fictif" && jobChoice.SelectedIndex == -1 && crafters.Items.Count == 0 && bookLeave.Enabled,
                "Crafter list panel differs");
            NoPacket(peer, "Opening the book sent a packet");
            Check(bookPanel.ChooseJob(25)); Expect(peer, "EJF25", "Job choice does not send EJF<métier>");
            FeedFromNetwork(account, "EJ+25;78;Autre fictif;12;1000;0;8;1;-1,-1,-1;;0,2"); FeedFromNetwork(account, "EJ+25;77;Artisan fictif;30;8731;1;1;0;ff,0,0;;3,5");
            PumpUntil(() => crafters.Items.Count == 2);
            Check(crafters.Items[0].Text == "Artisan fictif" && crafters.Items[0].SubItems[3].Text == "En atelier" && crafters.Items[1].SubItems[3].Text == "Hors atelier"
                && crafters.Items[0].SubItems[2].Text == "carte 8731", "Crafter rows differ");
            crafters.Items[0].Selected = true; Application.DoEvents();
            Check(((Label)Get(bookPanel, "crafterDetail")).Text.Contains("5"), "Crafter detail differs");
            Click(bookLeave); Expect(peer, "EV", "Book close button does not send EV");
            Feed(account, "EV"); Application.DoEvents();
            Check(!drawer.IsOpen(bookPanel) && crafters.Items.Count == 0, "Book panel stayed open after EV");

            // JR : métier désappris, retiré des cases.
            drawer.Show(jobsPanel);
            FeedFromNetwork(account, "JR2");
            PumpUntil(() => strip.Slots[0]?.ID == 25);
            Check(account.Game.character.GetJobsSnapshot().Length == 2 && strip.Slots[3]?.ID == 62 && account.Game.Interactions.Jobs.LastMessage == "Métier Bûcheron fictif oublié (texte de test).",
                "JR differs");
            NoPacket(peer, "Job panel refreshes sent a packet");
            form.Close(); Application.DoEvents();
        }
    }
}
