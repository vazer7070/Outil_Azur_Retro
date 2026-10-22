using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot;
using Outil_Azur_complet.Bot.Controls;
using Outil_Azur_complet.Bot.Interfaces;
using Outil_Azur_complet.Bot.Menus;
using Outil_Azur_complet.Parser;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Actions;
using Tool_BotProtocol.Game.Chat;
using Tool_BotProtocol.Game.Combats;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Exchanges;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Maps.Interactives;
using Tool_BotProtocol.Game.Monstres;
using Tool_BotProtocol.Game.NPC;
using Tool_BotProtocol.Game.Perso.Spells;
using Tool_BotProtocol.Game.Session;
using Tool_BotProtocol.Game.Social;
using Tool_BotProtocol.Network.Enums;
using Tool_BotProtocol.Utils.Logger;
using Tools_protocol.Emulators;
using Tools_protocol.Parser.XML;
using Tools_protocol.Query;

// Bot d'Azur contre un VRAI serveur StarLoco (Login + Game), avec deux comptes de test en même temps.
// Sans AZUR_STARLOCO_LOGIN, le test ne contacte rien et réussit (« IGNORÉ »). Variables :
//   AZUR_STARLOCO_LOGIN     hôte:port du Login (obligatoire pour jouer)
//   AZUR_STARLOCO_ACCOUNTS  deux comptes de test « compte:motdepasse;compte:motdepasse » (comptes jetables : le test crée,
//                           puis supprime au passage suivant, des personnages « Essai-… » de niveau 1)
//   AZUR_STARLOCO_SERVER    identifiant du serveur de jeu (sinon le premier serveur en ligne de AH)
//   AZUR_STARLOCO_GAME_DB   chaîne de connexion MySQL de la base du serveur de jeu : cartes, déclencheurs, objets
//                           interactifs et zaapis exportés pour le bot par le parseur de l'outil (sinon : ressources du bot)
//   AZUR_STARLOCO_CLIENT    dossier du client 1.34 (fond des cartes lu dans data/maps, comme le parseur)
//   AZUR_STARLOCO_LOGS      journaux du serveur à joindre (fichiers ou dossiers séparés par « ; »)
//   AZUR_STARLOCO_CAPTURES  dossier où photographier la fenêtre de jeu (import d'ImageMagick, écran Xvfb)
//   AZUR_STARLOCO_GAME_STOP / AZUR_STARLOCO_GAME_START  commandes (sh -c, cmd /c sous Windows) qui arrêtent le serveur de jeu par
//                           son crochet d'arrêt (SIGTERM) puis le relancent ; sans elles, le scénario x3c n'est pas joué
// Chaque scénario a un délai maximal ; les paquets reçus et envoyés par les deux bots (identifiants masqués) et les
// lignes ajoutées aux journaux du serveur sont écrits dans live-journal.log du dossier de travail.
internal static class BotStarLocoLiveSmoke
{
    [STAThread]
    private static int Main()
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        string login = Environment.GetEnvironmentVariable("AZUR_STARLOCO_LOGIN");
        if (string.IsNullOrWhiteSpace(login))
        {
            Console.WriteLine("IGNORÉ : AZUR_STARLOCO_LOGIN (hôte:port du Login StarLoco) n'est pas défini ; aucun serveur contacté.");
            return 0;
        }
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            string path = Path.Combine(TestPaths.ApplicationBin, new AssemblyName(e.Name).Name + ".dll");
            if (!File.Exists(path)) path = Path.ChangeExtension(path, "exe");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        CultureInfo french = CultureInfo.GetCultureInfo("fr-FR");
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = french;
        Thread.CurrentThread.CurrentCulture = Thread.CurrentThread.CurrentUICulture = french;
        try { Application.EnableVisualStyles(); return Live.Run(login.Trim()); }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}

/// <summary>Résultat d'un scénario : ok, échec ou non joué, avec la preuve (paquet ou ligne de journal).</summary>
internal sealed class ScenarioResult
{
    public string Name, Status, Evidence;
    public override string ToString() => Status.PadRight(8) + " " + Name + " : " + Evidence;
}

/// <summary>Un compte du bot connecté au vrai serveur, avec son journal de paquets.</summary>
internal sealed class LiveSession : IDisposable
{
    private readonly List<string> received = new List<string>(), sent = new List<string>();
    public readonly string Label, Login, Password;
    public Accounts Account;
    public string CharacterName;
    public int ClassId;

    public LiveSession(string label, string login, string password) { Label = label; Login = login; Password = password; }

    public int Id => Account.Game.character.id;
    public Map Map => Account.Game.Map;
    public int MapId => Account.Game.Map.MapID;
    public int ReceivedCount { get { lock (received) return received.Count; } }
    public int SentCount { get { lock (sent) return sent.Count; } }
    public string[] ReceivedSince(int mark) { lock (received) return received.Skip(Math.Max(0, mark)).ToArray(); }
    public string[] SentSince(int mark) { lock (sent) return sent.Skip(Math.Max(0, mark)).ToArray(); }

    public void Start()
    {
        Account = new Accounts(new AccountConfig(Login, Password, "Live"));
        Account.Connexion.packetReceivedEvent += packet => { lock (received) received.Add(packet); Live.Journal(Label + " << " + Redact(packet)); };
        Account.Connexion.packetSendEvent += packet => { lock (sent) sent.Add(packet); Live.Journal(Label + " >> " + Redact(packet)); };
        Account.Connexion.PacketRejected += (packet, why) => Live.Journal(Label + " !! paquet refusé par le bot : " + why + " : " + Redact(packet));
        Account.Logger.log_event += (entry, color) => Live.Journal(Label + " [journal du bot] " + entry.message);
        Account.AccountDisconnectEvent += () => Live.Journal(Label + " [bot] déconnecté : " + Account?.ConnectionStatus);
    }

    /// <summary>Paquet journalisable : authentification et ticket masqués (BotPacketRedactor), mais le préfixe reste lisible.</summary>
    private string Redact(string packet)
    {
        string safe = BotPacketRedactor.Redact(packet, Account);
        if (safe.StartsWith("[", StringComparison.Ordinal) && packet.Length >= 2) safe = packet.Substring(0, Math.Min(3, packet.Length)) + "… " + safe;
        if (!string.IsNullOrEmpty(Password) && safe.Contains(Password)) safe = safe.Replace(Password, "<mot de passe>");
        return safe.Length > 400 ? safe.Substring(0, 400) + "…(" + safe.Length + " car.)" : safe;
    }

    public Task Send(string packet) => Account.Connexion.SendPacket(packet);

    public async Task<string> Expect(Func<string, bool> match, string what, int seconds, int from)
    {
        DateTime until = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < until)
        {
            string hit = ReceivedSince(from).FirstOrDefault(match);
            if (hit != null) return hit;
            await Task.Delay(25).ConfigureAwait(false);
        }
        throw new TimeoutException(Label + " : " + what + " non reçu en " + seconds + " s");
    }

    public Task<string> Expect(string prefix, string what, int seconds, int from) => Expect(p => p.StartsWith(prefix, StringComparison.Ordinal), what, seconds, from);

    public async Task<bool> Until(Func<bool> condition, int seconds)
    {
        DateTime until = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < until) { if (condition()) return true; await Task.Delay(25).ConfigureAwait(false); }
        return condition();
    }

    public void Dispose() { try { Account?.Disconnect(); } catch (Exception) { } try { Account?.Dispose(); } catch (Exception) { } }
}

internal static partial class Live
{
    private static readonly object journalSync = new object();
    private static readonly Stopwatch clock = Stopwatch.StartNew();
    private static StreamWriter journal;
    private static readonly List<ScenarioResult> results = new List<ScenarioResult>();
    private static readonly ConcurrentQueue<Action> uiQueue = new ConcurrentQueue<Action>();
    private static readonly Dictionary<string, long> logOffsets = new Dictionary<string, long>();
    private static readonly List<Exception> uiErrors = new List<Exception>();
    private static string host, work, captures;
    private static int port, serverId;
    private static bool mapsAvailable;
    private static LiveSession a, b;
    private static GameClientFullform window;

    internal static void Journal(string line)
    {
        string stamped = (clock.Elapsed.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture)).PadLeft(8) + " " + line;
        lock (journalSync) journal?.WriteLine(stamped);
    }

    private static void Say(string line) { Console.WriteLine(line); Journal("== " + line); }

    /// <summary>Exécute sur le fil d'interface les actions en attente d'<see cref="OnUi"/>.</summary>
    private static void DrainUi() { while (uiQueue.TryDequeue(out Action action)) action(); }

    internal static int Run(string login)
    {
        Accounts.ParseEndpoint(login, out host, out port);
        string[] accounts = (Environment.GetEnvironmentVariable("AZUR_STARLOCO_ACCOUNTS") ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        if (accounts.Length < 2 || accounts.Take(2).Any(entry => entry.IndexOf(':') <= 0))
            throw new ArgumentException("AZUR_STARLOCO_ACCOUNTS doit donner deux comptes de test : « compte:motdepasse;compte:motdepasse ».");
        string server = Environment.GetEnvironmentVariable("AZUR_STARLOCO_SERVER");
        serverId = string.IsNullOrWhiteSpace(server) ? -1 : int.Parse(server.Trim(), CultureInfo.InvariantCulture);
        captures = Environment.GetEnvironmentVariable("AZUR_STARLOCO_CAPTURES");

        work = Path.Combine(TestPaths.Work, "starloco-live-" + Process.GetCurrentProcess().Id);
        Directory.CreateDirectory(work);
        Environment.CurrentDirectory = work;
        journal = new StreamWriter(Path.Combine(work, "live-journal.log"), false, new UTF8Encoding(false)) { AutoFlush = true };
        Application.ThreadException += (s, e) => { lock (uiErrors) uiErrors.Add(e.Exception); Journal("!! erreur d'interface : " + e.Exception); };
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        MarkServerLogs();
        Say("Serveur : Login " + host + ":" + port + " ; journal : " + Path.Combine(work, "live-journal.log"));

        LoadResources();
        GlobalConfig.writenewconfig(host, port.ToString(CultureInfo.InvariantCulture), "5555", "1.34.1", "2528660", "2362079");
        MessagesReception.Init();

        a = new LiveSession("A", accounts[0].Substring(0, accounts[0].IndexOf(':')), accounts[0].Substring(accounts[0].IndexOf(':') + 1));
        b = new LiveSession("B", accounts[1].Substring(0, accounts[1].IndexOf(':')), accounts[1].Substring(accounts[1].IndexOf(':') + 1));
        Task scenarios = Task.Run(ScenariosAsync);
        // Fil d'interface : la fenêtre de jeu du compte A vit ici, les scénarios tournent sur le pool de fils.
        // Application.DoEvents ne rend la main qu'une fois la file de messages vide ; tant que la carte s'anime (minuterie
        // de 33 ms, dessin plus lent que cela sous Xvfb), elle ne l'est jamais : une minuterie WinForms vide donc aussi
        // uiQueue depuis l'intérieur de DoEvents, sinon OnUi ne rend jamais la main pendant une émote tenue.
        using (var pump = new System.Windows.Forms.Timer { Interval = 10 })
        {
            pump.Tick += (sender, e) => DrainUi();
            pump.Start();
            while (!scenarios.IsCompleted)
            {
                DrainUi();
                Application.DoEvents();
                Thread.Sleep(10);
            }
            pump.Stop();
        }
        DrainUi();
        try { scenarios.GetAwaiter().GetResult(); }
        catch (Exception error) { results.Add(new ScenarioResult { Name = "déroulement", Status = "échec", Evidence = error.GetType().Name + " : " + error.Message }); Journal("!! " + error); }
        finally
        {
            if (window != null && !window.IsDisposed) { window.Close(); Application.DoEvents(); window.Dispose(); }
            // Une déconnexion après l'autre : deux retraits simultanés de la même cellule corrompent sa liste de joueurs chez StarLoco
            // jusqu'au redémarrage du Game, donc aussi le passage suivant (voir SeparateAsync).
            a.Dispose();
            Thread.Sleep(1000);
            b.Dispose();
            Thread.Sleep(500);
            AppendServerLogs();
        }

        Console.WriteLine();
        foreach (ScenarioResult result in results) Console.WriteLine(result);
        lock (uiErrors) foreach (Exception error in uiErrors) Console.WriteLine("erreur d'interface : " + error.GetType().Name + " " + error.Message);
        int failed = results.Count(r => r.Status == "échec") + uiErrors.Count;
        int ok = results.Count(r => r.Status == "ok");
        Console.WriteLine((failed == 0 ? "OK: " : "ÉCHEC: ") + "StarLoco réel " + host + ":" + port + ", " + ok + " scénario(s) réussi(s), "
            + failed + " échec(s), " + results.Count(r => r.Status == "non joué") + " non joué(s) ; journal " + Path.Combine(work, "live-journal.log"));
        journal.Dispose();
        return failed == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------------------------------------ ressources

    private static void LoadResources()
    {
        string bin = TestPaths.ApplicationBin;
        string lang = Path.Combine(bin, "ressources", "Bot", "BotLang");
        if (Directory.Exists(lang)) Say("Textes du client : " + LangData.Load(lang) + " fichier(s)");
        ServerMessages.Resolver = (type, id, args) => { string text = LangData.Text.Im(type, id, args); return string.IsNullOrEmpty(text) || (text.StartsWith("!") && text.EndsWith("!")) ? null : text; };
        PNJ.ClientNameResolver = id => LangData.Npc.Has(id) ? LangData.Npc.Name(id) : null;
        Monstres.ClientNameResolver = id => LangData.Monster.Has(id) ? LangData.Monster.Name(id) : null;
        string spells = Path.Combine(bin, "ressources", "Bot", "BotSorts");
        if (Directory.Exists(spells)) { Spell.LoadAllSpells(spells); Say("Sorts du bot : " + Spell.AllSpells.Count); }

        string database = Environment.GetEnvironmentVariable("AZUR_STARLOCO_GAME_DB");
        string folder = Path.Combine(work, "ressources");
        if (!string.IsNullOrWhiteSpace(database))
        {
            // Même chemin que le parseur de l'outil (Parseur → ressources du bot) : profil StarLoco, base du serveur de jeu.
            EmulatorRegistry.Select("StarLoco");
            DatabaseManager2.ConnectionString = database;
            DatabaseManager.ConnectionString = database;
            var watch = Stopwatch.StartNew();
            var context = BotExportContext.ForCurrentEmulator();
            string client = Environment.GetEnvironmentVariable("AZUR_STARLOCO_CLIENT");
            string clientMaps = string.IsNullOrWhiteSpace(client) ? null : ResourceMapConversion.ClientMapsDirectory(client);
            if (clientMaps != null) context.MapBackground = request => ResourceMapConversion.ReadClientBackground(clientMaps, request);
            int maps = XmlParser.ParseSQLToXML(Path.Combine(folder, "BotMaps"), "Maps", true, context).GetAwaiter().GetResult();
            int triggers = XmlParser.ParseSQLToXML(Path.Combine(folder, "BotTriggers"), "Déclencheurs", true, context).GetAwaiter().GetResult();
            int interactives = XmlParser.ParseSQLToXML(Path.Combine(folder, "BotInteractives"), "Interactifs", true, context).GetAwaiter().GetResult();
            Say("Export du parseur depuis la base du jeu : " + maps + " cartes, " + triggers + " déclencheurs, " + interactives
                + " interactifs en " + watch.ElapsedMilliseconds + " ms (" + context.WarningCount + " avertissement(s))");
            Map.LoadAllMapsAsync(Path.Combine(folder, "BotMaps")).GetAwaiter().GetResult();
            Triggers.LoadAllTriggersAsync(Path.Combine(folder, "BotTriggers")).GetAwaiter().GetResult();
            InteractivesParent.LoadAllInteractivesAsync(Path.Combine(folder, "BotInteractives")).GetAwaiter().GetResult();
        }
        else if (Directory.Exists(Map.MapPath)) Map.LoadAllMapsAsync().GetAwaiter().GetResult();
        mapsAvailable = Map.AllBotMaps.Count > 0;
        Say("Cartes du bot : " + Map.AllBotMaps.Count + ", déclencheurs : " + Triggers.Count + ", interactifs : " + InteractivesParent.Count
            + (mapsAvailable ? "" : " (sans cartes : déplacements et combats non joués ; définissez AZUR_STARLOCO_GAME_DB)"));
        try { Outil_Azur_complet.Bot.Controls.BotMapArtwork.WarmupAsync().GetAwaiter().GetResult(); } catch (Exception error) { Say("Décors : " + error.Message); }
    }

    // ------------------------------------------------------------------------------------------------ journaux du serveur

    private static IEnumerable<string> ServerLogFiles()
    {
        string value = Environment.GetEnvironmentVariable("AZUR_STARLOCO_LOGS");
        if (string.IsNullOrWhiteSpace(value)) yield break;
        foreach (string entry in value.Split(';').Select(x => x.Trim()).Where(x => x.Length > 0))
        {
            if (File.Exists(entry)) yield return entry;
            else if (Directory.Exists(entry)) foreach (string file in Directory.GetFiles(entry, "*", SearchOption.AllDirectories)) yield return file;
        }
    }

    private static void MarkServerLogs()
    {
        foreach (string file in ServerLogFiles()) logOffsets[file] = new FileInfo(file).Length;
    }

    private static void AppendServerLogs()
    {
        foreach (string file in ServerLogFiles())
        {
            long from = logOffsets.TryGetValue(file, out long offset) ? offset : 0;
            long length = new FileInfo(file).Length;
            if (length <= from) continue;
            try
            {
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    stream.Seek(from, SeekOrigin.Begin);
                    string text = new StreamReader(stream, Encoding.UTF8).ReadToEnd();
                    var lines = text.Split('\n').Where(l => l.Trim().Length > 0 && !l.Contains("ExchangeClient - F")).ToList();
                    if (lines.Count == 0) continue;
                    Journal("---- journal du serveur " + file + " (" + lines.Count + " ligne(s) ajoutée(s))");
                    foreach (string line in lines.Take(400)) Journal("   | " + line.TrimEnd('\r'));
                    if (file.Contains("Error")) Console.WriteLine("journal d'erreurs du serveur : " + file + " : " + lines.First().Trim());
                }
            }
            catch (IOException error) { Journal("journal illisible " + file + " : " + error.Message); }
        }
    }

    // ------------------------------------------------------------------------------------------------ interface

    private static Task OnUi(Action action)
    {
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        uiQueue.Enqueue(() => { try { action(); done.SetResult(true); } catch (Exception error) { done.SetException(error); } });
        return done.Task;
    }

    private static Task<T> OnUi<T>(Func<T> function)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        uiQueue.Enqueue(() => { try { done.SetResult(function()); } catch (Exception error) { done.SetException(error); } });
        return done.Task;
    }

    /// <summary>Photographie la fenêtre de jeu (zone cliente) sur l'écran Xvfb, comme tools/captures.</summary>
    private static async Task Capture(string name, Form form = null)
    {
        if (string.IsNullOrWhiteSpace(captures)) return;
        // La fenêtre se construit sur le fil d'interface après ASK : l'attendre avant la première capture.
        DateTime until = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < until && !await OnUi(() => (form ?? window) != null && (form ?? window).IsHandleCreated && (form ?? window).Visible).ConfigureAwait(false))
            await Task.Delay(100).ConfigureAwait(false);
        Form target = form ?? window;
        if (target == null) { Say("capture impossible : fenêtre de jeu absente"); return; }
        Directory.CreateDirectory(captures);
        // Lot X7 : plusieurs fenêtres de jeu se chevauchent ; celle qui est photographiée passe au premier plan puis a le temps de se redessiner.
        await OnUi(() => { target.BringToFront(); target.Refresh(); return true; }).ConfigureAwait(false);
        await Task.Delay(1200).ConfigureAwait(false);
        Rectangle area = await OnUi(() => target.RectangleToScreen(target.ClientRectangle)).ConfigureAwait(false);
        Journal("capture " + name + " : acteurs dessinés " + await OnUi(DrawnActors).ConfigureAwait(false));
        string file = Path.Combine(captures, name + ".png");
        var start = new ProcessStartInfo("import", "-window root -crop " + area.Width + "x" + area.Height + "+" + area.X + "+" + area.Y + " +repage \"" + file + "\"")
        { UseShellExecute = false, RedirectStandardError = true };
        try
        {
            using (Process process = Process.Start(start))
            {
                while (!process.HasExited) await Task.Delay(20).ConfigureAwait(false);
                Say(process.ExitCode == 0 ? "capture " + file : "capture impossible : " + process.StandardError.ReadToEnd().Trim());
            }
        }
        catch (Exception error) { Say("capture impossible : " + error.Message); }
    }

    /// <summary>États visuels de la carte de la fenêtre (acteur, apparence, cellule, sprite chargé), pour le journal des captures.</summary>
    private static string DrawnActors()
    {
        var user = window?.Map == null ? null : typeof(MapControl).GetField("UserMap", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window.Map) as UserMapControl;
        if (user == null) return "(pas de carte)";
        return string.Join(", ", user.GetActorVisualStates().Where(state => state.MemberIndex < 0).Select(state =>
            state.ActorId + (state.IsSelf ? " (soi)" : "") + " apparence " + state.GFX + " cellule " + state.CellId
            + (state.IsVisible ? "" : " invisible") + (state.HasSprite ? "" : " sans sprite")
            + " @" + (int)state.ScreenPosition.X + "," + (int)state.ScreenPosition.Y));
    }

    // ------------------------------------------------------------------------------------------------ scénarios

    private static void Record(string name, string status, string evidence)
    {
        var result = new ScenarioResult { Name = name, Status = status, Evidence = evidence };
        lock (results) results.Add(result);
        Say("scénario " + result);
    }

    /// <summary>
    /// <c>AZUR_STARLOCO_SCENARIOS</c> (facultatif) : repères des scénarios à jouer, séparés par des virgules (« h », « e,g2 ») ; le scénario a
    /// (connexion) est toujours joué. Sert à rejouer un scénario seul sous la limite de 240 s du lanceur, le scénario f pouvant durer 200 s.
    /// </summary>
    private static readonly string[] onlyScenarios = (Environment.GetEnvironmentVariable("AZUR_STARLOCO_SCENARIOS") ?? "")
        .Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Joue un scénario avec un délai maximal ; une exception devient un échec avec sa raison.</summary>
    private static async Task<bool> Scenario(string name, int seconds, Func<Task<string>> body)
    {
        string letter = name.Substring(0, Math.Max(0, name.IndexOf('.')));
        if (onlyScenarios.Length > 0 && letter != "a" && !onlyScenarios.Contains(letter)) { Record(name, "non joué", "absent de AZUR_STARLOCO_SCENARIOS"); return false; }
        Say("---- scénario " + name + " (délai " + seconds + " s)");
        // Remise en état (lot CORR-LIVE), hors du délai du scénario : chacun part de la carte de départ, vie pleine, sans combat ni fenêtre ouverte.
        string prepared = null;
        if (letter != "a")
        {
            Task<string> prepare = PrepareAsync();
            Task ready = await Task.WhenAny(prepare, Task.Delay(TimeSpan.FromSeconds(PrepareSeconds))).ConfigureAwait(false);
            try
            {
                if (ready != prepare) throw new TimeoutException("délai de " + PrepareSeconds + " s dépassé");
                prepared = await prepare.ConfigureAwait(false);
            }
            catch (Exception error)
            {
                Record(name, "échec", "remise en état avant le scénario : " + error.GetType().Name + " : " + error.Message);
                Journal("!! " + error);
                await RecoverAsync().ConfigureAwait(false);
                return false;
            }
        }
        Task<string> run = body();
        Task finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(seconds))).ConfigureAwait(false);
        if (finished != run) { Record(name, "échec", "délai de " + seconds + " s dépassé"); await RecoverAsync().ConfigureAwait(false); return false; }
        try { string evidence = await run.ConfigureAwait(false); Record(name, "ok", prepared == null ? evidence : "remise en état : " + prepared + " ; " + evidence); return true; }
        catch (SkipScenario skip) { Record(name, "non joué", skip.Message); return false; }
        catch (Exception error) { Record(name, "échec", error.GetType().Name + " : " + error.Message); Journal("!! " + error); }
        await RecoverAsync().ConfigureAwait(false);
        return false;
    }

    /// <summary>Après un échec : abandonne un combat resté ouvert pour que les scénarios suivants partent de la carte.</summary>
    private static async Task RecoverAsync()
    {
        foreach (LiveSession s in new[] { a, b })
        {
            PlayerExchange exchange = s?.Account?.Game?.Interactions?.Exchange;
            if (exchange != null && (exchange.IsOpen || exchange.PendingRequest != null))
            {
                var left = await exchange.LeaveAsync().ConfigureAwait(false);
                Journal(s.Label + " [test] fermeture de l'échange resté ouvert : " + left.Message);
                await s.Until(() => !exchange.IsOpen, 10).ConfigureAwait(false);
            }
            if (s?.Account?.Game?.Fight == null || !s.Account.Game.Fight.IsInFight) continue;
            try
            {
                var giveUp = await s.Account.Game.Fight.GiveUpAsync().ConfigureAwait(false);
                Journal(s.Label + " [test] abandon du combat resté ouvert : " + giveUp.Message);
                await s.Until(() => !s.Account.Game.Fight.IsInFight && !s.Account.IsFighting(), 20).ConfigureAwait(false);
                await Task.Delay(800).ConfigureAwait(false);
            }
            catch (Exception error) { Journal(s.Label + " [test] abandon impossible : " + error.Message); }
        }
    }

    internal sealed class SkipScenario : Exception { public SkipScenario(string message) : base(message) { } }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    private static async Task ScenariosAsync()
    {
        bool inGame = await Scenario("a. connexion, création et entrée en jeu des deux comptes", 120, ConnectBothAsync).ConfigureAwait(false);
        if (!inGame) return;
        // Lot X4-CORR : x4b et x4c quittent la carte 164 par son zaap ; ils passent avant tout scénario qui fait marcher, combattre ou
        // téléporter A et B depuis cette carte, car un null laissé dans GameCase.players (voir SeparateAsync) y interdit ensuite toute
        // téléportation jusqu'au redémarrage du Game. Les trois scénarios ne touchent ni A ni B.
        await Scenario("x4a. familier et monture nourris depuis l'inventaire (lot X4)", 60, PetsFeedingAsync).ConfigureAwait(false);
        await Scenario("x4b. familier confié à un gardien puis repris (lot X4)", 100, PetsKeepingAsync).ConfigureAwait(false);
        await Scenario("x4c. familier mort de repas rapprochés, ressuscité chez un PNJ (lot X4)", 140, PetsResurrectionAsync).ConfigureAwait(false);
        await Scenario("b. même carte, déplacement, changement de carte et retour", 150, SameMapAsync).ConfigureAwait(false);
        await Scenario("c. chat général et message privé", 40, ChatAsync).ConfigureAwait(false);
        await Scenario("c2. émote assise de B jouée en bande dans la fenêtre de A (lot AN8)", 40, EmoteAsync).ConfigureAwait(false);
        await Scenario("x2a. orientation et « Se frapper » depuis le menu de A, vus par B (lot X2)", 40, DirectionAsync).ConfigureAwait(false);
        await Scenario("x2b. tombe, âme libérée au cimetière, fantôme ramené à la vie par le phénix (lot X2)", 120, TombAsync).ConfigureAwait(false);
        await Scenario("x2c. mariage devant le prêtre : Non puis Oui des deux époux (lot X2)", 120, MarriageAsync).ConfigureAwait(false);
        await Scenario("d1. invitation de groupe acceptée", 40, PartyAsync).ConfigureAwait(false);
        await Scenario("d2. échange de kamas validé des deux côtés", 60, ExchangeAsync).ConfigureAwait(false);
        await Scenario("d3. ajout en ami", 40, FriendAsync).ConfigureAwait(false);
        await Scenario("e. défi accepté : placement, tours, déplacement, sort, abandon, résultat", 240, DuelAsync).ConfigureAwait(false);
        await Scenario("f. combat contre un groupe de monstres", 300, MonstersAsync).ConfigureAwait(false);
        await Scenario("g1. dialogue avec un PNJ", 40, NpcDialogAsync).ConfigureAwait(false);
        await Scenario("g2. boutique d'un PNJ : achat", 60, NpcShopAsync).ConfigureAwait(false);
        await Scenario("g3. zaap", 90, ZaapAsync).ConfigureAwait(false);
        await Scenario("h. corps à corps : épée et arc contre des monstres, poings en défi (lot X1)", 150, CloseCombatAsync).ConfigureAwait(false);
        await Scenario("x3a. compte neuf sans pseudo : AlEr, pseudo pris (AlEs) puis accepté, par le bot puis par sa fenêtre ChooseNickName (lot X3)", 90, NewAccountNicknameAsync).ConfigureAwait(false);
        await Scenario("x3b. parchemin d'expérience : AN2, boîte « Nouveau niveau » dans la fenêtre de jeu (lot X3)", 90, LevelUpAsync).ConfigureAwait(false);
        await Scenario("x32a. potion de changement de nom : AlEr, fenêtre du nom sur la fenêtre de jeu, refus, AlEs, nom accepté par GM|~ (lot X32)", 90, RenameAsync).ConfigureAwait(false);
        await Scenario("x32b. roulette du Bouftou : 100 kamas, TC → TV envoyé, boîte « mini-jeu », joueur libéré (lot X32)", 90, RouletteAsync).ConfigureAwait(false);
        await Scenario("x5. artisanat sécurisé : ER13 du client, ERK 12, ECK12 / ECK13, ingrédients, paiement, EK des deux, EcK, EV (lot X5)", 150, SecureCraftAsync).ConfigureAwait(false);
        await Scenario("x6. artisanat sécurisé depuis les menus : EW+ à l'arrivée, clic gauche sur l'artisan, « Demander à » → ER13, ERET (artisan éloigné), ERK 12 → ECK, « Inviter à » → ER12 → ERK 13 → ECK, livre des artisans : Message privé, Localiser (lot X6)", 200, SecureCraftMenuAsync).ConfigureAwait(false);
        await Scenario("x6b. marchand hors ligne : ER6|, EMO+, Eq → Eq1, EQ → marchand -5 ; clic simple → menu « Acheter » sans paquet, Maj + clic → ER4 → ECK4, EB → EBK, GM|- (lot X6)", 150, OfflineMerchantClickAsync).ConfigureAwait(false);
        await Scenario("x7. Sram contre A : prêt et option d'équipe vus par l'autre, pièges GDZ/GDC, invisibilité GA;150, journal dans le chat (lot X7)", 180, FightEventsAsync).ConfigureAwait(false);
        await Scenario("x12. glisser-déposer et boîte de quantité : échange entre deux comptes (EMO+, EMO-, EMG), banque (EMO±, EMG±), hôtel de vente réfuté (lot X12)", 200, TransferDragAsync).ConfigureAwait(false);
        await Scenario("x8. Iop contre A : portée en cercle et en croix, pointeur en croix et en ligne, croix d'interdiction, GA300 sur la cible, chemin de combat survolé puis GA001 (lot X8)", 180, SpellAimAsync).ConfigureAwait(false);
        await Scenario("x13. objet vivant de la base nourri, apparence changée et dissocié par le volet de l'inventaire ; pain donné à l'autre joueur par la visée de la carte (lot X13)", 150, LivingItemAsync).ConfigureAwait(false);
        await Scenario("x33. Flèche Magique contre A : mode tactique au placement sans paquet, cible derrière un mur exclue et refusée, cible visible lancée (GA;300), retour au décor normal (lot X33)", 180, TacticSightAsync).ConfigureAwait(false);
        await Scenario("x9. défis d'un combat contre des monstres : Gd lus et dans la colonne de la fenêtre, « Voir la cible » → Gdi → Gf en drapeau, GdOK / GdKO dans le chat, bonus de challenges du résultat (lot X9)", 600, ChallengeFightsAsync).ConfigureAwait(false);
        // Toujours en dernier : arrête puis redémarre le serveur de jeu (A et B restent déconnectés).
        await Scenario("x3c. serveur de jeu arrêté en pleine partie : boîte de perte de connexion, « Oui » reconnecte A (lot X3)", 360, GameStopAsync).ConfigureAwait(false);
    }
}

// Scénarios a à d : connexion et personnages, carte partagée et déplacements, chat, groupe, échange, amis.
internal static partial class Live
{
    private const string TestPrefix = "Essai-";
    private static readonly Random random = new Random();
    private static int startMap;
    private static readonly SemaphoreSlim creationGate = new SemaphoreSlim(1, 1), worldEntryGate = new SemaphoreSlim(1, 1);

    /// <summary>Nom accepté par GameClient.addCharacter de StarLoco : lettres et un tiret, jamais trois fois la même lettre.</summary>
    private static string NewCharacterName()
    {
        const string consonants = "bcdfgklmnprstvz", vowels = "aeiou";
        var name = new System.Text.StringBuilder(TestPrefix);
        name.Append(char.ToUpperInvariant(consonants[random.Next(consonants.Length)]));
        for (int i = 0; i < 4; i++) name.Append(i % 2 == 0 ? vowels[random.Next(vowels.Length)] : consonants[random.Next(consonants.Length)]);
        return name.ToString();
    }

    private static async Task<string> ConnectBothAsync()
    {
        // A : Crâ (9) féminin, avec la fenêtre de jeu ; B : Iop (8) masculin, sans interface.
        Task<string> first = ConnectOneAsync(a, 9, 1, true), second = ConnectOneAsync(b, 8, 0, false);
        string[] both = await Task.WhenAll(first, second).ConfigureAwait(false);
        Check(a.Account.Game.character.Race_ID != b.Account.Game.character.Race_ID, "Les deux personnages ont la même classe.");
        startMap = a.MapId;
        return both[0] + " | " + both[1];
    }

    private static async Task<string> ConnectOneAsync(LiveSession s, int classId, int sex, bool withWindow)
    {
        s.Start();
        s.ClassId = classId;
        if (withWindow)
            // Comme SelectPlayerPerso.CharacterSelected : la fenêtre de jeu s'ouvre à la sélection du personnage (ASK).
            s.Account.Game.character.Player_Selection += () => OnUi(() =>
            {
                if (window != null) return;
                window = new GameClientFullform(s.Account) { StartPosition = FormStartPosition.Manual, Location = Point.Empty, Size = new Size(1200, 800), ShowInTaskbar = false };
                window.Show();
            });
        await s.Account.ConnectAsync().ConfigureAwait(false);
        await s.Expect("HC", "clé HC du Login", 15, 0).ConfigureAwait(false);
        string ah = await s.Expect("AH", "liste des serveurs AH", 15, 0).ConfigureAwait(false);
        await s.Expect("AxK", "AxK (serveurs du compte)", 15, 0).ConfigureAwait(false);
        int server = serverId > 0 ? serverId : s.Account.Game.Server.Servers.Where(x => x.Value == ServerStates.ONLINE).Select(x => x.Key).DefaultIfEmpty(-1).Min();
        Check(server > 0, "Aucun serveur en ligne dans " + ah);
        Check(s.Account.Game.Server.Servers.TryGetValue(server, out ServerStates state) && state == ServerStates.ONLINE, "Serveur " + server + " absent ou hors ligne : " + ah);
        // Comme la fenêtre de choix du serveur (PersoSelection) : AX<id>.
        await s.Send("AX" + server).ConfigureAwait(false);
        string redirect = await s.Expect(p => p.StartsWith("AYK") || p.StartsWith("AXK") || p.StartsWith("AXE"), "redirection AYK", 15, 0).ConfigureAwait(false);
        Check(!redirect.StartsWith("AXE"), "Sélection du serveur refusée : " + redirect);
        await s.Expect("HG", "HG du serveur de jeu", 15, 0).ConfigureAwait(false);
        await s.Expect("ATK", "ATK (ticket accepté)", 15, 0).ConfigureAwait(false);
        await s.Expect("ALK", "ALK (liste des personnages)", 15, 0).ConfigureAwait(false);

        // Personnages d'essai laissés par un passage précédent : AD<id>| (niveau < 20, StarLoco ne demande pas la réponse).
        foreach (var leftover in s.Account.AccountCharactersInfo.Where(c => c.Value.StartsWith(TestPrefix, StringComparison.Ordinal)).ToList())
        {
            int mark = s.ReceivedCount;
            await s.Send("AD" + leftover.Key + "|").ConfigureAwait(false);
            string answer = await s.Expect(p => p.StartsWith("ALK") || p.StartsWith("ADE"), "réponse à la suppression AD", 15, mark).ConfigureAwait(false);
            Check(answer.StartsWith("ALK") && !s.Account.AccountCharactersInfo.ContainsKey(leftover.Key), "Suppression du personnage d'essai " + leftover.Key + " refusée : " + answer);
            Journal(s.Label + " [test] personnage d'essai supprimé : " + leftover.Value.Split('|')[0]);
        }

        // Création comme la fenêtre CreateCharacter : AA<nom>|<classe>|<sexe>|<c1>|<c2>|<c3>, puis retour à la liste.
        // Une création à la fois : deux AA traités dans la même milliseconde reçoivent le même identifiant chez StarLoco
        // (le second personnage n'est pas enregistré et AS<id> répond ASE au premier compte).
        s.CharacterName = NewCharacterName();
        int created = s.ReceivedCount;
        await creationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            s.Account.Game.Server.ExitCreationMenu = false;
            s.Account.Game.Server.NameNewCharacter = s.CharacterName;
            await s.Send("AA" + s.CharacterName + "|" + classId + "|" + sex + "|-1|-1|-1").ConfigureAwait(false);
            string aa = await s.Expect(p => p.StartsWith("AAK") || p.StartsWith("AAE"), "réponse à la création AA", 15, created).ConfigureAwait(false);
            Check(aa.StartsWith("AAK"), "Création refusée : " + aa);
            await s.Expect(p => p.StartsWith("ALK") && p.Contains(";" + s.CharacterName + ";"), "ALK avec le nouveau personnage", 15, created).ConfigureAwait(false);
        }
        finally { creationGate.Release(); }
        Check(await s.Until(() => s.Account.AccountCharactersInfo.Values.Any(v => v.StartsWith(s.CharacterName + "|")), 5).ConfigureAwait(false), "Le bot n'a pas lu le personnage créé dans ALK.");
        int id = s.Account.AccountCharactersInfo.First(c => c.Value.StartsWith(s.CharacterName + "|")).Key;

        // Sélection comme SelectPlayerPerso.PlaySelected : AS<id> ; le bot répond GC1 à ASK. Une entrée en jeu à la fois (lot CORR-LIVE) :
        // les deux personnages neufs arrivent sur la même cellule, et deux ajouts simultanés à sa liste de joueurs peuvent en perdre un
        // chez StarLoco (voir SeparateAsync).
        int selected = s.ReceivedCount;
        string ask, gck, stats, gdm, gm;
        await worldEntryGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await s.Send("AS" + id).ConfigureAwait(false);
            ask = await s.Expect(p => p.StartsWith("ASK") || p.StartsWith("ASE"), "ASK (personnage sélectionné)", 15, selected).ConfigureAwait(false);
            Check(ask.StartsWith("ASK|" + id + "|" + s.CharacterName + "|"), "Sélection refusée : " + ask);
            gck = await s.Expect("GCK", "GCK (partie créée)", 15, selected).ConfigureAwait(false);
            stats = await s.Expect("As", "As (caractéristiques)", 15, selected).ConfigureAwait(false);
            gdm = await s.Expect("GDM", "GDM (carte)", 15, selected).ConfigureAwait(false);
            gm = await s.Expect(p => p.StartsWith("GM|") && p.Contains(";" + id + ";" + s.CharacterName + ";"), "GM du personnage sur sa carte", 15, selected).ConfigureAwait(false);
        }
        finally { worldEntryGate.Release(); }
        Check(s.Account.Game.character.id == id && s.Account.Game.character.Name == s.CharacterName && s.Account.Game.character.Race_ID == classId,
            "Le bot n'a pas le personnage sélectionné : " + s.Account.Game.character.Name + " classe " + s.Account.Game.character.Race_ID);
        Check(await s.Until(() => s.Map.MapID > 0 && s.Map.Self != null && s.Map.Self.CellId >= 0, 10).ConfigureAwait(false), "Le bot n'a pas placé son personnage sur la carte " + gdm);
        Check(!mapsAvailable || s.Map.HasMapData, "Carte " + s.MapId + " sans cellules alors que les cartes sont exportées : " + s.Map.LoadError);
        return s.Label + " " + s.CharacterName + " (classe " + classId + ") : " + Short(ah) + ", AAK, " + Short(ask, 40) + ", " + Short(gck) + ", " + Short(stats, 30)
            + ", " + Short(gdm, 24) + ", GM cellule " + s.Map.Self.CellId;
    }

    private static string Short(string packet, int length = 60) => packet == null ? "" : packet.Length > length ? packet.Substring(0, length) + "…" : packet;

    // ------------------------------------------------------------------------------------------------ b. carte partagée

    private static MapActor ActorOf(LiveSession viewer, LiveSession other) => viewer.Map.GetActor(other.Id) as MapActor;

    private static void RequireMaps() { if (!mapsAvailable) throw new SkipScenario("cartes du bot absentes (AZUR_STARLOCO_GAME_DB non défini)"); }

    /// <summary>Cellule libre et praticable à quelques pas du personnage (ni déclencheur, ni acteur, ni bord de carte).</summary>
    private static Cell FreeCellNear(LiveSession s, int minimum, int maximum, Func<Cell, bool> extra = null)
    {
        Cell origin = s.Map.GetCellFromId((short)s.Map.Self.CellId);
        var occupied = new HashSet<int>(s.Map.AllActors.Select(x => x.CellId));
        var triggers = Triggers.ForMap(s.MapId);
        return s.Map.MapCells.Where(c => c != null && c.IsWalkable() && c.C_Types != Tool_BotProtocol.Game.Maps.Enums.CellTypes.TELEPORT_CELL
                && !occupied.Contains(c.CellID) && !triggers.ContainsKey(c.CellID) && (extra == null || extra(c)))
            .Select(c => new { c, d = origin.GetDistanceBetweenCells(c) })
            .Where(x => x.d >= minimum && x.d <= maximum && s.Account.Game.Manager.Mouvements.PreviewPath(x.c) != null)
            .OrderBy(x => x.d).Select(x => x.c).FirstOrDefault();
    }

    private static async Task<string> SameMapAsync()
    {
        RequireMaps();
        Check(a.MapId == b.MapId, "Les deux personnages ne sont pas sur la même carte : " + a.MapId + " / " + b.MapId);
        Check(await a.Until(() => ActorOf(a, b) != null && ActorOf(b, a) != null, 10).ConfigureAwait(false), "Les deux bots ne se voient pas (GM).");
        string seen = "A voit " + ActorOf(a, b) + ", B voit " + ActorOf(b, a);

        // Déplacement de A : GA001<chemin> → GA0;1;<A>;<chemin> reçu par A et B → GKK0 de A.
        Cell target = FreeCellNear(a, 3, 7);
        Check(target != null, "Aucune cellule libre près de A");
        int markA = a.ReceivedCount, markB = b.ReceivedCount, sentA = a.SentCount;
        var result = await a.Account.Game.Manager.Mouvements.MoveToAsync(target).ConfigureAwait(false);
        Check(result == Tool_BotProtocol.Game.Managers.Mouvements.MoveResults.EXIT, "Déplacement refusé par le bot : " + result);
        string sentMove = a.SentSince(sentA).FirstOrDefault(p => p.StartsWith("GA001"));
        Check(sentMove != null, "GA001 non envoyé");
        string moveA = await a.Expect(p => p.StartsWith("GA") && p.Contains(";1;" + a.Id + ";"), "GA;1 du déplacement (A)", 10, markA).ConfigureAwait(false);
        string moveB = await b.Expect(p => p.StartsWith("GA") && p.Contains(";1;" + a.Id + ";"), "GA;1 du déplacement de A (B)", 10, markB).ConfigureAwait(false);
        Check(await a.Until(() => a.SentSince(sentA).Any(p => p.StartsWith("GKK")), 15).ConfigureAwait(false), "GKK non envoyé après la marche");
        Check(await a.Until(() => a.Map.Self.CellId == target.CellID && ActorOf(b, a)?.CellId == target.CellID, 10).ConfigureAwait(false),
            "Cellule de A après la marche : A pense " + a.Map.Self.CellId + ", B voit " + ActorOf(b, a)?.CellId + ", attendu " + target.CellID);
        string gkk = a.SentSince(sentA).First(p => p.StartsWith("GKK"));
        // Les deux personnages, côte à côte après la marche de A, dans la fenêtre de jeu de A.
        await Capture("19-reel-carte").ConfigureAwait(false);

        // Changement de carte par un déclencheur de téléportation, puis retour par celui de la carte voisine.
        string travel = await ChangeMapAndBackAsync(a, b).ConfigureAwait(false);
        return seen + " ; " + Short(sentMove, 30) + " → A " + Short(moveA, 34) + ", B " + Short(moveB, 34) + ", " + gkk + " ; " + travel;
    }

    /// <summary>Déclencheur de la carte courante qui mène à une carte d'où un déclencheur ramène ici.</summary>
    private static Trigger RoundTripTrigger(int mapId, out Trigger back)
    {
        back = null;
        foreach (Trigger go in Triggers.ForMap(mapId).Values.Where(t => t.IsTeleport && t.TargetMapId != mapId && Map.AllBotMaps.ContainsKey(t.TargetMapId)).OrderBy(t => t.CellId))
        {
            back = Triggers.ForMap(go.TargetMapId).Values.FirstOrDefault(t => t.IsTeleport && t.TargetMapId == mapId);
            if (back != null) return go;
        }
        return null;
    }

    private static async Task<int> WalkThroughTrigger(LiveSession s, Trigger trigger)
    {
        int mark = s.ReceivedCount;
        Cell cell = s.Map.GetCellFromId((short)trigger.CellId);
        Check(cell != null, "Cellule " + trigger.CellId + " absente de la carte " + s.MapId);
        var result = await s.Account.Game.Manager.Mouvements.MoveToAsync(cell).ConfigureAwait(false);
        Check(result == Tool_BotProtocol.Game.Managers.Mouvements.MoveResults.EXIT, "Marche vers le déclencheur " + trigger.CellId + " refusée : " + result);
        await s.Expect("GDM|" + trigger.TargetMapId, "GDM|" + trigger.TargetMapId + " après le déclencheur " + trigger.CellId, 30, mark).ConfigureAwait(false);
        Check(await s.Until(() => s.MapId == trigger.TargetMapId && s.Map.Self != null && s.Map.Self.CellId >= 0, 10).ConfigureAwait(false), "Le bot n'est pas placé sur la carte " + trigger.TargetMapId);
        await Task.Delay(400).ConfigureAwait(false);
        return mark;
    }

    private static async Task<string> ChangeMapAndBackAsync(LiveSession s, LiveSession observer)
    {
        int home = s.MapId;
        Trigger go = RoundTripTrigger(home, out Trigger back);
        if (go == null) throw new SkipScenario("aucun déclencheur aller-retour sur la carte " + home);
        int markObserver = observer.ReceivedCount;
        await WalkThroughTrigger(s, go).ConfigureAwait(false);
        string left = await observer.Expect("GM|-" + s.Id, "GM|-" + s.Id + " (départ vu par l'autre)", 10, markObserver).ConfigureAwait(false);
        await WalkThroughTrigger(s, back).ConfigureAwait(false);
        string returned = await observer.Expect(p => p.StartsWith("GM|+") && p.Contains(";" + s.Id + ";"), "GM|+ au retour", 10, markObserver).ConfigureAwait(false);
        Check(await observer.Until(() => ActorOf(observer, s) != null, 5).ConfigureAwait(false), "L'autre bot ne revoit pas le personnage revenu.");
        return "carte " + home + " → " + go.TargetMapId + " (cellule " + go.CellId + ") → " + home + " (cellule " + back.CellId + "), vu par l'autre : " + left + " puis GM|+ cellule " + ActorOf(observer, s).CellId;
    }

    // ------------------------------------------------------------------------------------------------ c. chat

    private static async Task<string> ChatAsync()
    {
        string general = "Bonjour depuis le bot " + DateTime.UtcNow.ToString("HHmmss", CultureInfo.InvariantCulture);
        int markB = b.ReceivedCount;
        var sent = await a.Account.Game.Chat.SendAsync('*', general).ConfigureAwait(false);
        Check(sent.Sent, "Message général refusé par le bot : " + sent.Message);
        string heard = await b.Expect(p => p.StartsWith("cMK|") && p.Contains("|" + a.Id + "|") && p.Contains(general), "cMK| du message général", 10, markB).ConfigureAwait(false);
        Check(await b.Until(() => b.Account.Game.Chat.Messages.Any(m => m.Text.Contains(general) && m.Author == a.CharacterName), 5).ConfigureAwait(false), "Le chat de B n'affiche pas le message général de A.");

        string whisper = "Message privé " + DateTime.UtcNow.ToString("HHmmss", CultureInfo.InvariantCulture);
        int markA = a.ReceivedCount; markB = b.ReceivedCount;
        var privateSent = await a.Account.Game.Chat.WhisperAsync(b.CharacterName, whisper).ConfigureAwait(false);
        Check(privateSent.Sent, "Message privé refusé par le bot : " + privateSent.Message);
        string got = await b.Expect(p => p.StartsWith("cMKF|") && p.Contains(whisper), "cMKF| du message privé", 10, markB).ConfigureAwait(false);
        string echo = await a.Expect(p => p.StartsWith("cMKT|") && p.Contains(whisper), "cMKT| (copie du message privé)", 10, markA).ConfigureAwait(false);
        Check(await b.Until(() => b.Account.Game.Chat.Messages.Any(m => m.Text.Contains(whisper)), 5).ConfigureAwait(false), "Le chat de B n'affiche pas le message privé.");
        return "B reçoit " + Short(heard, 70) + " ; B reçoit " + Short(got, 60) + " ; A reçoit " + Short(echo, 60);
    }

    // ------------------------------------------------------------------------------------------------ c2. émote (lot AN8)

    /// <summary>État visuel d'un acteur dans la carte de la fenêtre de A (calculé sur le fil de l'interface).</summary>
    private static Task<UserMapControl.ActorVisualState> StateOf(long actorId) => OnUi(() =>
    {
        var user = window?.Map == null ? null : typeof(MapControl).GetField("UserMap", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window.Map) as UserMapControl;
        return user?.GetActorVisualState(unchecked((int)actorId));
    });

    /// <summary>Attend que la file d'animations de la fenêtre de A joue (ou non) <paramref name="animation"/> sur l'acteur.</summary>
    private static async Task<bool> UntilPlaying(long actorId, string animation, bool expected, int seconds)
    {
        DateTime limit = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < limit)
        {
            bool playing = await OnUi(() => window.Map.AnimationQueue.IsPlaying(actorId, animation)).ConfigureAwait(false);
            if (playing == expected) return true;
            await Task.Delay(100).ConfigureAwait(false);
        }
        return false;
    }

    /// <summary>
    /// B (Iop, gfx 80 ou 81 : bande <c>emote1</c> livrée) s'assoit : StarLoco diffuse <c>eUK&lt;B&gt;|1</c> sans durée, la carte de A
    /// joue la bande <c>emote1</c> tenue sur B, sans icône ; une seconde émote 1 fait renvoyer <c>eUK&lt;B&gt;|0</c>
    /// (<c>emoteActive</c> remis à 0 par <c>useEmote</c>) et B revient en <c>static</c>. Aucun clip annexe hors combat sans aura.
    /// </summary>
    private static async Task<string> EmoteAsync()
    {
        if (window?.Map == null) throw new SkipScenario("pas de fenêtre de jeu");
        Check(await a.Until(() => a.Account.Game.Map.GetActor(b.Id) != null, 5).ConfigureAwait(false), "A ne voit pas B sur sa carte.");
        int markA = a.ReceivedCount;
        var sit = await b.Account.Game.Chat.EmoteAsync(1).ConfigureAwait(false);
        Check(sit.Sent, "Émote refusée par le bot : " + sit.Message);
        string euk = await a.Expect(p => p.StartsWith("eUK" + b.Id + "|1", StringComparison.Ordinal), "eUK" + b.Id + "|1 (B s'assoit)", 10, markA).ConfigureAwait(false);
        Check(await UntilPlaying(b.Id, "emote1", true, 5).ConfigureAwait(false), "La carte de A ne joue pas emote1 sur B après " + euk + ".");
        UserMapControl.ActorVisualState shown = null;
        DateTime limit = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < limit)
        {
            shown = await StateOf(b.Id).ConfigureAwait(false);
            if (shown != null && shown.HasSprite && shown.AnimationName.StartsWith("emote1", StringComparison.Ordinal)) break;
            await Task.Delay(100).ConfigureAwait(false);
        }
        Check(shown != null && shown.HasSprite && shown.AnimationName.StartsWith("emote1", StringComparison.Ordinal),
            "B n'est pas dessiné avec la bande emote1 : " + (shown == null ? "aucun état" : shown.AnimationName + (shown.HasSprite ? "" : " sans sprite")));
        Check(shown.EmoteIcon == 0, "L'icône d'émote reste affichée alors que la bande est dessinée.");
        int clips = await OnUi(() => window.Map.ExtraClips.Count).ConfigureAwait(false);
        Check(clips == 0, "Clips annexes inattendus hors combat et sans aura : " + clips);
        await Task.Delay(1000).ConfigureAwait(false); // CLICK_MIN_DELAY des émotes (800 ms)
        markA = a.ReceivedCount;
        var stand = await b.Account.Game.Chat.EmoteAsync(1).ConfigureAwait(false);
        Check(stand.Sent, "Seconde émote refusée par le bot : " + stand.Message);
        string zero = await a.Expect(p => p == "eUK" + b.Id + "|0", "eUK" + b.Id + "|0 (B se relève)", 10, markA).ConfigureAwait(false);
        Check(await UntilPlaying(b.Id, "emote1", false, 5).ConfigureAwait(false), "La bande emote1 reste jouée sur B après " + zero + ".");
        UserMapControl.ActorVisualState back = await StateOf(b.Id).ConfigureAwait(false);
        Check(back != null && back.AnimationName.StartsWith("static", StringComparison.Ordinal), "B n'est pas revenu en static : " + back?.AnimationName);
        return "A reçoit " + euk + ", dessine B en " + shown.AnimationName + " (image " + shown.Frame + ", sans icône) ; " + zero + " → " + back.AnimationName;
    }

    // ------------------------------------------------------------------------------------------------ d. groupe, échange, amis

    private static async Task<string> PartyAsync()
    {
        int markA = a.ReceivedCount, markB = b.ReceivedCount;
        // B n'a pas de fenêtre : le test tient le rôle du joueur qui garde l'invitation pour y répondre (sinon le bot refuse par PR).
        EventHandler<InvitationEventArgs> keep = (sender, e) => e.Handled = true;
        b.Account.Game.Session.PartyInviteReceived += keep;
        try { return await PartyCoreAsync(markA, markB).ConfigureAwait(false); }
        finally { b.Account.Game.Session.PartyInviteReceived -= keep; }
    }

    private static async Task<string> PartyCoreAsync(int markA, int markB)
    {
        var invite = await a.Account.Game.Interactions.Party.InviteAsync(b.CharacterName).ConfigureAwait(false);
        Check(invite.Sent, "Invitation refusée par le bot : " + invite.Message);
        string pik = await b.Expect("PIK", "PIK (invitation reçue)", 10, markB).ConfigureAwait(false);
        Check(await b.Until(() => b.Account.Game.Interactions.Party.PendingInviter == a.CharacterName, 5).ConfigureAwait(false), "B n'a pas d'invitation en attente.");
        var accept = await b.Account.Game.Interactions.Party.AcceptAsync().ConfigureAwait(false);
        Check(accept.Sent, "Acceptation refusée par le bot : " + accept.Message);
        string pcka = await a.Expect("PCK", "PCK (groupe créé, A)", 10, markA).ConfigureAwait(false);
        await b.Expect("PCK", "PCK (groupe rejoint, B)", 10, markB).ConfigureAwait(false);
        Check(await a.Until(() => a.Account.Game.Interactions.Party.Group.Members.Count == 2 && b.Account.Game.Interactions.Party.Group.Members.Count == 2, 10).ConfigureAwait(false),
            "Groupe incomplet : A voit " + a.Account.Game.Interactions.Party.Group.Members.Count + ", B voit " + b.Account.Game.Interactions.Party.Group.Members.Count);
        string members = string.Join(", ", a.Account.Game.Interactions.Party.Group.Members.Select(m => m.Name));

        // Quitter le groupe (PV) : les scénarios suivants (duel) se jouent hors groupe.
        markA = a.ReceivedCount; markB = b.ReceivedCount;
        var leave = await b.Account.Game.Interactions.Party.LeaveAsync().ConfigureAwait(false);
        Check(leave.Sent, "Départ du groupe refusé par le bot : " + leave.Message);
        string pv = await b.Expect("PV", "PV (groupe quitté)", 10, markB).ConfigureAwait(false);
        Check(await b.Until(() => b.Account.Game.Interactions.Party.Group.Members.Count == 0, 5).ConfigureAwait(false), "B se croit encore dans le groupe.");
        return "B reçoit " + pik + ", A reçoit " + Short(pcka) + ", membres : " + members + " ; départ de B : " + pv;
    }

    private static async Task<string> ExchangeAsync()
    {
        long kamasA = a.Account.Game.character.Kamas, kamasB = b.Account.Game.character.Kamas;
        // Comme pour le groupe : le test garde la demande d'échange de B pour l'accepter lui-même.
        EventHandler<ExchangeRequestEventArgs> keep = (sender, e) => e.Handled = true;
        b.Account.Game.Interactions.Exchange.RequestReceived += keep;
        try { return await ExchangeCoreAsync(kamasA, kamasB).ConfigureAwait(false); }
        finally { b.Account.Game.Interactions.Exchange.RequestReceived -= keep; }
    }

    private static async Task<string> ExchangeCoreAsync(long kamasA, long kamasB)
    {
        int markA = a.ReceivedCount, markB = b.ReceivedCount;
        var request = await a.Account.Game.Interactions.Exchange.RequestAsync(b.Id).ConfigureAwait(false);
        Check(request.Sent, "Demande d'échange refusée par le bot : " + request.Message);
        string erk = await b.Expect("ERK", "ERK (demande d'échange reçue)", 10, markB).ConfigureAwait(false);
        Check(await b.Until(() => b.Account.Game.Interactions.Exchange.PendingRequest != null, 5).ConfigureAwait(false), "B n'a pas de demande d'échange en attente.");
        var accept = await b.Account.Game.Interactions.Exchange.AcceptAsync().ConfigureAwait(false);
        Check(accept.Sent, "Acceptation de l'échange refusée par le bot : " + accept.Message);
        string ecka = await a.Expect("ECK", "ECK (échange ouvert, A)", 10, markA).ConfigureAwait(false);
        await b.Expect("ECK", "ECK (échange ouvert, B)", 10, markB).ConfigureAwait(false);

        const long amount = 25;
        var kamas = await a.Account.Game.Interactions.Exchange.SetKamasAsync(amount).ConfigureAwait(false);
        Check(kamas.Sent, "Kamas refusés par le bot : " + kamas.Message);
        string distant = await b.Expect(p => p.StartsWith("EmKG") || p.StartsWith("EMKG"), "EmKG (kamas proposés par A)", 10, markB).ConfigureAwait(false);
        Check(await b.Until(() => b.Account.Game.Interactions.Exchange.DistantKamas == amount, 5).ConfigureAwait(false), "B ne voit pas les kamas proposés : " + b.Account.Game.Interactions.Exchange.DistantKamas);
        // Le client grise « Valider » 3 s après une modification (DELAY_BEFORE_VALIDATE) ; le bot applique la même règle.
        Check(await a.Until(() => a.Account.Game.Interactions.Exchange.CanValidate, 6).ConfigureAwait(false), "A ne peut pas valider l'échange (délai du bouton)");
        var validateA = await a.Account.Game.Interactions.Exchange.ValidateAsync().ConfigureAwait(false);
        Check(validateA.Sent, "Validation de A refusée par le bot : " + validateA.Message);
        // PlayerExchange.toogleOk de StarLoco : « EK1 » suivi de l'id de celui qui valide, envoyé aux deux joueurs.
        await b.Expect(p => p == "EK1" + a.Id, "EK1" + a.Id + " (A a validé, vu par B)", 10, markB).ConfigureAwait(false);
        Check(await b.Until(() => b.Account.Game.Interactions.Exchange.DistantReady && !b.Account.Game.Interactions.Exchange.LocalReady, 5).ConfigureAwait(false),
            "B ne voit pas la validation de A (EK1" + a.Id + ")");
        Check(await b.Until(() => b.Account.Game.Interactions.Exchange.CanValidate, 6).ConfigureAwait(false), "B ne peut pas valider l'échange (délai du bouton)");
        var validateB = await b.Account.Game.Interactions.Exchange.ValidateAsync().ConfigureAwait(false);
        Check(validateB.Sent, "Validation de B refusée par le bot : " + validateB.Message);
        string evA = await a.Expect(p => p.StartsWith("EVa") || p == "EV" || p.StartsWith("EV"), "EV (fin de l'échange, A)", 10, markA).ConfigureAwait(false);
        await b.Expect(p => p.StartsWith("EV"), "EV (fin de l'échange, B)", 10, markB).ConfigureAwait(false);
        Check(await a.Until(() => a.Account.Game.character.Kamas == kamasA - amount && b.Account.Game.character.Kamas == kamasB + amount, 10).ConfigureAwait(false),
            "Kamas après l'échange : A " + kamasA + " → " + a.Account.Game.character.Kamas + ", B " + kamasB + " → " + b.Account.Game.character.Kamas);
        return "B reçoit " + erk + ", A reçoit " + ecka + ", B voit " + distant + ", EK des deux côtés, " + evA + " ; kamas A " + kamasA + " → " + a.Account.Game.character.Kamas
            + ", B " + kamasB + " → " + b.Account.Game.character.Kamas;
    }

    private static async Task<string> FriendAsync()
    {
        FriendsActions friends = a.Account.Game.Interactions.Friends;
        int markA = a.ReceivedCount;
        var add = await friends.AddFriendAsync(b.CharacterName).ConfigureAwait(false);
        Check(add.Sent, "Ajout d'ami refusé par le bot : " + add.Message);
        string fak = await a.Expect(p => p.StartsWith("FAK") || p.StartsWith("FAE"), "FAK (ami ajouté)", 10, markA).ConfigureAwait(false);
        Check(fak.StartsWith("FAK"), "Ajout refusé par le serveur : " + fak);
        var refresh = await friends.RefreshAsync(FriendListKind.Friends).ConfigureAwait(false);
        Check(refresh.Sent, "Liste d'amis refusée par le bot : " + refresh.Message);
        string fl = await a.Expect("FL", "FL (liste d'amis)", 10, markA).ConfigureAwait(false);
        // Account.parseFriendList de StarLoco : pseudo du compte (celui de « Ad »), puis le personnage connecté.
        Func<FriendEntry, bool> isB = f => f != null && (f.Name == b.CharacterName || (!string.IsNullOrEmpty(b.Account.Name) && f.Account == b.Account.Name));
        Check(await a.Until(() => friends.Friends.Any(isB), 5).ConfigureAwait(false),
            "La liste d'amis du bot ne contient ni le personnage de B (" + b.CharacterName + ") ni le pseudo de son compte : " + string.Join(", ", friends.Friends.Select(f => f.Name + "/" + f.Account)));
        string entry = string.Join(", ", friends.Friends.Select(f => f.Name + (f.IsOnline ? " (en ligne)" : "")));
        // Nettoyage : retrait de l'ami (FD), pour que le passage suivant rejoue l'ajout.
        FriendEntry added = friends.Friends.FirstOrDefault(isB);
        if (added != null) { var removed = await friends.RemoveAsync(FriendListKind.Friends, added).ConfigureAwait(false); Journal("A [test] retrait de l'ami : " + removed.Message); }
        return fak + ", " + Short(fl, 80) + " → " + entry;
    }
}

// Scénarios e à g : défi entre les deux personnages, combat contre des monstres, PNJ (dialogue, boutique) et zaap.
internal static partial class Live
{
    // ------------------------------------------------------------------------------------------------ outils de combat

    private static int LifeOf(LiveSession s, int id) => s.Account.Game.Fight.Fighters.TryGetValue(id, out CombatFighter f) ? f.Life : int.MinValue;
    private static int CellOf(LiveSession s, int id) => s.Account.Game.Fight.Fighters.TryGetValue(id, out CombatFighter f) ? f.CellId : int.MinValue;

    private static CombatFighter Self(LiveSession s) => s.Account.Game.Fight.Fighters.TryGetValue(s.Id, out CombatFighter f) ? f : null;

    private static IEnumerable<CombatFighter> Enemies(LiveSession s)
    {
        CombatFighter self = Self(s);
        return s.Account.Game.Fight.Fighters.Values.Where(f => !f.IsDead && f.Id != s.Id && self != null && f.Team >= 0 && f.Team != self.Team);
    }

    /// <summary>Sorts de dégâts (effets 91 à 100) que le bot accepte de lancer sur la cellule visée.</summary>
    private static short? DamageSpellOn(LiveSession s, short cell)
    {
        foreach (Spell spell in s.Account.Game.character.Spells.Values.OrderBy(x => x.ID))
        {
            SpellStats stats = spell.GetStats();
            if (stats == null || !stats.NormalEffect.Any(effect => effect.Id >= 91 && effect.Id <= 100)) continue;
            if (s.Account.Game.Fight.GetSpellUnavailableReason(spell.ID, cell) == null) return spell.ID;
        }
        return null;
    }

    /// <summary>Dernière ligne <c>GTM</c> reçue pour ce combattant : vie, PA, PM, cellule.</summary>
    private static int[] LastGtm(LiveSession s, int fighterId, int mark)
    {
        foreach (string packet in s.ReceivedSince(mark).Reverse().Where(p => p.StartsWith("GTM|")))
            foreach (string entry in packet.Substring(4).Split('|'))
            {
                string[] f = entry.Split(';');
                if (f.Length >= 6 && f[0] == fighterId.ToString(CultureInfo.InvariantCulture) && f[1] == "0"
                    && int.TryParse(f[2], out int life) && int.TryParse(f[3], out int pa) && int.TryParse(f[4], out int pm) && int.TryParse(f[5], out int cell))
                    return new[] { life, pa, pm, cell };
            }
        return null;
    }

    private static string PointsOf(LiveSession s, int id) =>
        s.Account.Game.Fight.Fighters.TryGetValue(id, out CombatFighter f) ? f.ActionPoints + " PA/" + f.MovementPoints + " PM" : "absent";

    /// <summary>
    /// Au début d'un tour, avant toute action : compare vie, cellule, PA et PM de chaque combattant du bot aux derniers GTM
    /// (envoyés par StarLoco à la fin du tour précédent, PA et PM totaux), et les PA/PM du compte quand c'est son tour ; renvoie le constat.
    /// </summary>
    private static async Task<string> CheckFightersMatchGtm(LiveSession s, int mark)
    {
        var lines = new List<string>();
        // Le tour qui commence peut être vu d'abord par l'autre compte : attendre le GTM qui suit le dernier GTF de ce compte.
        bool fresh = await s.Until(() =>
        {
            string[] seen = s.ReceivedSince(mark);
            return Array.FindLastIndex(seen, p => p.StartsWith("GTM|")) > Array.FindLastIndex(seen, p => p.StartsWith("GTF"));
        }, 5).ConfigureAwait(false);
        Check(fresh, s.Label + " : pas de GTM après la fin du tour précédent");
        foreach (CombatFighter fighter in s.Account.Game.Fight.Fighters.Values.ToList())
        {
            int[] gtm = LastGtm(s, fighter.Id, mark);
            if (gtm == null) continue;
            bool same = await s.Until(() => LifeOf(s, fighter.Id) == gtm[0], 3).ConfigureAwait(false);
            Check(same, s.Label + " : vie de " + fighter.Name + " = " + LifeOf(s, fighter.Id) + " dans le bot, " + gtm[0] + " dans GTM");
            Check(await s.Until(() => CellOf(s, fighter.Id) == gtm[3], 3).ConfigureAwait(false), s.Label + " : cellule de " + fighter.Name + " = " + CellOf(s, fighter.Id) + " dans le bot, " + gtm[3] + " dans GTM");
            string expected = gtm[1] + " PA/" + gtm[2] + " PM";
            Check(await s.Until(() => PointsOf(s, fighter.Id) == expected, 3).ConfigureAwait(false), s.Label + " : " + fighter.Name + " a " + PointsOf(s, fighter.Id) + " dans le bot, " + expected + " dans GTM");
            lines.Add(fighter.Name + " " + gtm[0] + " PV/" + expected);
        }
        Fights fight = s.Account.Game.Fight;
        int[] own = LastGtm(s, s.Id, mark);
        if (own != null && fight.IsMyTurn)
        {
            Check(await s.Until(() => fight.ActionPoints == own[1] && fight.MovementPoints == own[2], 3).ConfigureAwait(false),
                s.Label + " : tour du compte avec " + fight.ActionPoints + " PA/" + fight.MovementPoints + " PM dans le bot, " + own[1] + "/" + own[2] + " dans GTM");
            lines.Add("tour de " + s.Label + " : " + fight.ActionPoints + " PA/" + fight.MovementPoints + " PM");
        }
        return string.Join(", ", lines);
    }

    private static async Task PlaceAndReady(LiveSession s, int mark, bool move)
    {
        Fights fight = s.Account.Game.Fight;
        Check(await s.Until(() => fight.IsPlacement && fight.PlacementCells.Length > 0, 10).ConfigureAwait(false), s.Label + " : pas de cellules de placement (GP)");
        if (move)
        {
            short current = (short)(Self(s)?.CellId ?? -1);
            short other = fight.PlacementCells.FirstOrDefault(c => c != current && !fight.Fighters.Values.Any(f => f.CellId == c));
            if (other > 0) await MoveInPlacement(s, other).ConfigureAwait(false);
        }
        await ReadyAsync(s, mark).ConfigureAwait(false);
    }

    /// <summary>Change de cellule de placement : <c>Gp&lt;cellule&gt;</c>, puis <c>GIC</c> du serveur appliqué par le bot.</summary>
    private static async Task MoveInPlacement(LiveSession s, short cell)
    {
        Fights fight = s.Account.Game.Fight;
        int before = s.ReceivedCount;
        var placed = await fight.PlaceAsync(cell).ConfigureAwait(false);
        Check(placed.Sent, s.Label + " : placement refusé par le bot : " + placed.Message);
        await s.Expect(p => p.StartsWith("GIC|") && p.Contains(s.Id + ";" + cell), "GIC (nouvelle cellule de placement)", 10, before).ConfigureAwait(false);
        Check(await s.Until(() => Self(s)?.CellId == cell, 5).ConfigureAwait(false), s.Label + " : le bot n'a pas déplacé son combattant en placement");
    }

    private static async Task ReadyAsync(LiveSession s, int mark)
    {
        Fights fight = s.Account.Game.Fight;
        var ready = await fight.SetReadyAsync(true).ConfigureAwait(false);
        Check(ready.Sent, s.Label + " : « prêt » refusé par le bot : " + ready.Message);
        // StarLoco lance le combat dès que le dernier joueur est prêt : son GR1 n'est alors pas envoyé (état déjà « combat »), GS le remplace.
        await s.Expect(p => p == "GR1" + s.Id || p == "GS", "GR1 (prêt) ou GS (début du combat)", 10, mark).ConfigureAwait(false);
    }

    /// <summary>Tour du personnage : déplacement (facultatif) vers la cible, sort de dégâts s'il est possible, fin de tour.</summary>
    private static async Task<string> PlayTurn(LiveSession s, bool move, bool cast, List<string> evidence)
    {
        Fights fight = s.Account.Game.Fight;
        int mark = s.ReceivedCount;
        CombatFighter target = Enemies(s).OrderBy(e => DistanceTo(s, e)).FirstOrDefault();
        if (target == null) return "aucune cible";
        int paBefore = fight.ActionPoints, pmBefore = fight.MovementPoints, origin = Self(s)?.CellId ?? -1;
        if (move && fight.MovementPoints > 0)
        {
            short? destination = StepToward(s, target, DamageSpellOn(s, (short)target.CellId) == null);
            if (destination.HasValue)
            {
                var moved = await fight.MoveAsync(destination.Value).ConfigureAwait(false);
                if (moved.Sent)
                {
                    // GA;104 : le combattant est tacle en quittant le contact d'un ennemi (esquive ratée) : ni chemin ni GA;1.
                    string ga = await s.Expect(p => p.StartsWith("GA") && (p.Contains(";1;" + s.Id + ";") || p.Contains(";104;" + s.Id + ";")),
                        "GA;1 du déplacement en combat (ou GA;104, tacle)", 10, mark).ConfigureAwait(false);
                    if (ga.Contains(";104;"))
                    {
                        string lostPm = await s.Expect(p => p.StartsWith("GA") && p.Contains(";129;" + s.Id + ";"), "GA;129 (PM perdus au tacle)", 10, mark).ConfigureAwait(false);
                        int lost = -int.Parse(lostPm.Split(';')[3].Split(',')[1], CultureInfo.InvariantCulture);
                        Check(await s.Until(() => !fight.IsActionPending && fight.MovementPoints == pmBefore - lost && Self(s)?.CellId == origin, 10).ConfigureAwait(false),
                            s.Label + " : après le tacle, PM du bot " + fight.MovementPoints + " (attendu " + (pmBefore - lost) + "), cellule " + Self(s)?.CellId + " (attendu " + origin + ")");
                        evidence.Add(s.Label + " est taclé : " + ga + ", " + lostPm + " → PM " + pmBefore + "→" + fight.MovementPoints + ", PA " + paBefore + "→" + fight.ActionPoints);
                        goto afterMove;
                    }
                    string pm = await s.Expect(p => p.StartsWith("GA;129;" + s.Id + ";"), "GA;129 (PM utilisés)", 10, mark).ConfigureAwait(false);
                    Check(await s.Until(() => Self(s)?.CellId == destination.Value && !fight.IsActionPending, 10).ConfigureAwait(false),
                        s.Label + " : cellule après le déplacement en combat = " + Self(s)?.CellId + ", attendu " + destination);
                    int used = -int.Parse(pm.Split(';')[3].Split(',')[1], CultureInfo.InvariantCulture);
                    Check(await s.Until(() => fight.MovementPoints == pmBefore - used, 5).ConfigureAwait(false), s.Label + " : PM du bot " + fight.MovementPoints + ", attendu " + (pmBefore - used));
                    evidence.Add(s.Label + " se déplace : " + Short(ga, 40) + ", " + pm + " → PM " + pmBefore + "→" + fight.MovementPoints);
                }
                else Journal(s.Label + " [test] déplacement en combat refusé par le bot : " + moved.Message);
            }
        }
        afterMove:
        if (cast)
        {
            target = Enemies(s).OrderBy(e => DistanceTo(s, e)).FirstOrDefault();
            short? spell = target == null ? null : DamageSpellOn(s, (short)target.CellId);
            if (spell.HasValue)
            {
                int lifeBefore = target.Life, paStart = fight.ActionPoints, castMark = s.ReceivedCount;
                var sent = await fight.CastSpellAsync(spell.Value, (short)target.CellId).ConfigureAwait(false);
                Check(sent.Sent, s.Label + " : sort refusé par le bot : " + sent.Message);
                string launched = await s.Expect(p => p.StartsWith("GA;300;" + s.Id + ";") || p.StartsWith("GA;301;" + s.Id + ";") || p.StartsWith("GA;302;" + s.Id + ";")
                    || p.StartsWith("GA;0") || p.StartsWith("GAE"), "GA;300 (sort lancé)", 10, castMark).ConfigureAwait(false);
                Check(launched.StartsWith("GA;30"), s.Label + " : le serveur refuse le sort " + spell + " : " + launched);
                // Dernier ennemi tué : StarLoco envoie GE (fin du combat) avant le GA;102 du sort ; le bot quitte alors le combat (PA à -1,
                // combattants effacés) et ne lit plus ce GA;102, comme le constate CloseCombatTurnAsync pour le corps à corps.
                Func<bool> over = () => s.ReceivedSince(castMark).Any(p => p.StartsWith("GE", StringComparison.Ordinal));
                string pa = await s.Expect(p => p.StartsWith("GA;102;" + s.Id + ";") || p.StartsWith("GE", StringComparison.Ordinal), "GA;102 (PA utilisés)", 10, castMark).ConfigureAwait(false);
                string damage = null;
                if (!launched.StartsWith("GA;302"))
                    damage = await s.Expect(p => p.StartsWith("GA;100;") && p.Contains(";" + target.Id + ","), "GA;100 (dégâts sur la cible)", 10, castMark).ConfigureAwait(false);
                await s.Until(() => !fight.IsActionPending || over(), 5).ConfigureAwait(false);
                if (!pa.StartsWith("GE", StringComparison.Ordinal))
                {
                    int used = -int.Parse(pa.Split(';')[3].Split(',')[1], CultureInfo.InvariantCulture);
                    Check(await s.Until(() => fight.ActionPoints == paStart - used || over(), 5).ConfigureAwait(false), s.Label + " : PA du bot " + fight.ActionPoints + ", attendu " + (paStart - used));
                }
                if (damage != null)
                {
                    int delta = int.Parse(damage.Split(';')[3].Split(',')[1], CultureInfo.InvariantCulture);
                    // Fights.Fighters renvoie des copies : relire le combattant à chaque essai.
                    Check(await s.Until(() => LifeOf(s, target.Id) == lifeBefore + delta || over(), 5).ConfigureAwait(false), s.Label + " : vie de la cible " + LifeOf(s, target.Id) + ", attendu " + (lifeBefore + delta));
                }
                if (over())
                {
                    string ge = s.ReceivedSince(castMark).First(p => p.StartsWith("GE", StringComparison.Ordinal));
                    Check(await s.Until(() => !fight.IsInFight, 5).ConfigureAwait(false), s.Label + " : GE reçu après le sort, le bot reste en combat");
                    evidence.Add(s.Label + " lance le sort " + spell + " (" + LangDataSpell(spell.Value) + ") : " + Short(launched, 36) + (damage != null ? ", " + damage : " (échec critique)")
                        + " → combat terminé par ce sort (" + Short(ge, 24) + ")");
                    return "fin";
                }
                evidence.Add(s.Label + " lance le sort " + spell + " (" + LangDataSpell(spell.Value) + ") : " + Short(launched, 36) + ", " + pa + (damage != null ? ", " + damage : " (échec critique)")
                    + " → PA " + paStart + "→" + fight.ActionPoints + ", PV de " + target.Name + " " + lifeBefore + "→" + LifeOf(s, target.Id));
            }
            else Journal(s.Label + " [test] aucun sort de dégâts possible sur " + target?.Name + " (cellule " + target?.CellId + ", distance " + (target == null ? -1 : DistanceTo(s, target))
                + ", PA " + fight.ActionPoints + ") : " + string.Join(" ; ", s.Account.Game.character.Spells.Values.Select(x => x.ID + " niv. " + x.Level + " "
                + (x.GetStats() == null ? "sans caractéristiques" : "PA " + x.GetStats().PA + ", portée " + x.GetStats().Min_portee + "-" + x.GetStats().Max_portee
                    + ", effets " + string.Join(",", x.GetStats().NormalEffect.Select(e => e.Id)))
                + " → " + (target == null ? "?" : s.Account.Game.Fight.GetSpellUnavailableReason(x.ID, (short)target.CellId) ?? "possible"))));
        }
        if (!fight.IsInFight || fight.Phase == CombatPhase.Finished) return "fin";
        int passMark = s.ReceivedCount;
        var pass = await fight.PassTurnAsync().ConfigureAwait(false);
        if (pass.Sent) await s.Expect(p => p.StartsWith("GTF" + s.Id) || p.StartsWith("GE"), "GTF (fin du tour)", 15, passMark).ConfigureAwait(false);
        return "tour joué";
    }

    private static string LangDataSpell(short id) => LangData.IsLoaded("spells") && LangData.Spell.Has(id) ? LangData.Spell.Name(id) : "sort " + id;

    private static int DistanceTo(LiveSession s, CombatFighter other)
    {
        Cell from = s.Map.GetCellFromId((short)(Self(s)?.CellId ?? -1)), to = s.Map.GetCellFromId((short)other.CellId);
        return from == null || to == null ? int.MaxValue : from.GetDistanceBetweenCells(to);
    }

    /// <summary>Cellule libre à portée de PM : la plus proche de la cible si <paramref name="approach"/>, sinon un pas de côté.</summary>
    private static short? StepToward(LiveSession s, CombatFighter target, bool approach)
    {
        Fights fight = s.Account.Game.Fight;
        Cell origin = s.Map.GetCellFromId((short)(Self(s)?.CellId ?? -1)), goal = s.Map.GetCellFromId((short)target.CellId);
        if (origin == null || goal == null) return null;
        var occupied = new HashSet<int>(fight.Fighters.Values.Where(f => !f.IsDead).Select(f => (int)f.CellId));
        var candidates = s.Map.MapCells.Where(c => c != null && c.IsActive && c.IsWalkable() && !occupied.Contains(c.CellID))
            .Where(c => { int d = origin.GetDistanceBetweenCells(c); return d >= 1 && d <= fight.MovementPoints; }).ToList();
        IEnumerable<Cell> ordered = approach ? candidates.OrderBy(c => c.GetDistanceBetweenCells(goal)).ThenBy(c => origin.GetDistanceBetweenCells(c))
            : candidates.Where(c => origin.GetDistanceBetweenCells(c) == 1).OrderBy(c => Math.Abs(c.GetDistanceBetweenCells(goal) - origin.GetDistanceBetweenCells(goal)));
        foreach (Cell cell in ordered.Take(12))
        {
            return cell.CellID;
        }
        return null;
    }

    // ------------------------------------------------------------------------------------------------ e. défi

    private static async Task<string> DuelAsync()
    {
        RequireMaps();
        Check(a.MapId == b.MapId, "Les deux personnages ne sont pas sur la même carte");
        var evidence = new List<string>();
        int markA = a.ReceivedCount, markB = b.ReceivedCount;
        var challenge = await a.Account.Game.Interactions.MapActions.ChallengeAsync(b.Id).ConfigureAwait(false);
        Check(challenge.Sent, "Défi refusé par le bot : " + challenge.Message);
        string asked = await b.Expect(p => p.StartsWith("GA;900;" + a.Id + ";"), "GA;900 (défi reçu)", 10, markB).ConfigureAwait(false);
        Check(await b.Until(() => b.Account.Game.Interactions.MapActions.PendingChallenge?.ChallengerId == a.Id, 5).ConfigureAwait(false), "B n'a pas de défi en attente.");
        var accept = await b.Account.Game.Interactions.MapActions.AcceptChallengeAsync(a.Id).ConfigureAwait(false);
        Check(accept.Sent, "Acceptation du défi refusée par le bot : " + accept.Message);
        string gjk = await a.Expect("GJK", "GJK (entrée en combat, A)", 15, markA).ConfigureAwait(false);
        await b.Expect("GJK", "GJK (entrée en combat, B)", 15, markB).ConfigureAwait(false);
        string gp = await a.Expect("GP", "GP (cellules de placement)", 10, markA).ConfigureAwait(false);
        evidence.Add("B reçoit " + asked + ", A reçoit " + gjk + " et " + Short(gp, 30));
        Check(await a.Until(() => Self(a) != null && Self(b) != null && Enemies(a).Any(e => e.Id == b.Id) && Enemies(b).Any(e => e.Id == a.Id), 10).ConfigureAwait(false),
            "Les combattants ne sont pas dans deux équipes : " + string.Join(", ", a.Account.Game.Fight.Fighters.Values.Select(f => f.Name + " équipe " + f.Team)));
        await PlaceAndReady(a, markA, true).ConfigureAwait(false);
        await PlaceAndReady(b, markB, false).ConfigureAwait(false);
        string gs = await a.Expect("GS", "GS (début du combat)", 30, markA).ConfigureAwait(false);
        await a.Expect("GTL", "GTL (ordre de jeu)", 10, markA).ConfigureAwait(false);
        bool attacked = false;
        DateTime until = DateTime.UtcNow.AddSeconds(150);
        int turns = 0;
        while (DateTime.UtcNow < until && a.Account.Game.Fight.IsInFight && !attacked)
        {
            int actor = await WaitForTurnOf(new[] { a, b }, 40).ConfigureAwait(false);
            turns++;
            evidence.Add("vérification GTM (A) : " + await CheckFightersMatchGtm(a, markA).ConfigureAwait(false));
            if (actor == a.Id)
            {
                await PlayTurn(a, true, true, evidence).ConfigureAwait(false);
                attacked = evidence.Any(e => e.StartsWith("A lance"));
            }
            else if (actor == b.Id) await PlayTurn(b, true, false, evidence).ConfigureAwait(false);
        }
        Check(attacked, "A n'a pas pu lancer de sort sur B en " + turns + " tour(s)");
        // Combat en cours, bannière « Le combat commence » retombée : ligne de temps, PV entamés de B, tour suivant.
        await Capture("19-reel-combat").ConfigureAwait(false);
        // Abandon de B (GQ) : StarLoco le compte comme mort, le combat se termine (GE).
        int endA = a.ReceivedCount, endB = b.ReceivedCount;
        var giveUp = await b.Account.Game.Fight.GiveUpAsync().ConfigureAwait(false);
        Check(giveUp.Sent, "Abandon refusé par le bot : " + giveUp.Message);
        string ge = await a.Expect("GE", "GE (résultat du combat, A)", 20, endA).ConfigureAwait(false);
        Check(await a.Until(() => a.Account.Game.Fight.LastResult != null && !a.Account.Game.Fight.IsInFight, 10).ConfigureAwait(false), "Le bot A n'a pas lu le résultat GE");
        FightResultEntry winner = a.Account.Game.Fight.LastResult.Find(a.Id);
        Check(winner != null && winner.Kind == FightResultKind.Winner, "A devrait gagner après l'abandon de B : " + winner?.Kind);
        await b.Expect(p => p.StartsWith("GE") || p.StartsWith("GV"), "GE ou GV (fin du combat, B)", 20, endB).ConfigureAwait(false);
        await WaitBackOnMap(a).ConfigureAwait(false); await WaitBackOnMap(b).ConfigureAwait(false);
        return string.Join(" ; ", evidence) + " ; " + gs + ", " + turns + " tour(s), abandon de B (GQ) → " + Short(ge, 50) + " (A vainqueur)";
    }

    private static async Task<int> WaitForTurnOf(LiveSession[] sessions, int seconds)
    {
        DateTime until = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < until)
        {
            foreach (LiveSession s in sessions)
            {
                Fights fight = s.Account.Game.Fight;
                if (!fight.IsInFight) return 0;
                if (fight.IsMyTurn && !fight.IsActionPending) return s.Id;
            }
            await Task.Delay(50).ConfigureAwait(false);
        }
        throw new TimeoutException("aucun tour de joueur en " + seconds + " s");
    }

    private static async Task WaitBackOnMap(LiveSession s)
    {
        Check(await s.Until(() => !s.Account.Game.Fight.IsInFight && s.Map.Self != null && s.Map.Self.CellId >= 0 && !s.Account.IsFighting(), 20).ConfigureAwait(false),
            s.Label + " : pas revenu sur la carte après le combat (état " + s.Account.AccountStates + ")");
        await Task.Delay(800).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------------------------------------ f. monstres

    private static async Task<string> MonstersAsync()
    {
        RequireMaps();
        int home = a.MapId;
        var visited = new List<string>();
        Trigger back = null;
        if (a.Map.MonsterGroups.Count == 0)
        {
            foreach (Trigger go in Triggers.ForMap(home).Values.Where(t => t.IsTeleport && t.TargetMapId != home && Map.AllBotMaps.ContainsKey(t.TargetMapId)).OrderBy(t => t.CellId))
            {
                Trigger reverse = Triggers.ForMap(go.TargetMapId).Values.FirstOrDefault(t => t.IsTeleport && t.TargetMapId == home);
                if (reverse == null) continue;
                await WalkThroughTrigger(a, go).ConfigureAwait(false);
                await a.Until(() => a.Map.MonsterGroups.Count > 0, 3).ConfigureAwait(false);
                visited.Add(go.TargetMapId + " (" + a.Map.MonsterGroups.Count + " groupe(s))");
                if (a.Map.MonsterGroups.Count > 0) { back = reverse; break; }
                await WalkThroughTrigger(a, reverse).ConfigureAwait(false);
            }
        }
        if (a.Map.MonsterGroups.Count == 0) throw new SkipScenario("aucun groupe de monstres sur la carte " + home + " ni sur ses voisines (" + string.Join(", ", visited) + ")");
        var evidence = new List<string>();
        int mark = a.ReceivedCount;
        string gjk = null;
        for (int attempt = 0; attempt < 4 && gjk == null; attempt++)
        {
            MonsterGroupActor group = a.Map.MonsterGroups.OrderBy(g => g.TotalLevel).First();
            evidence.Add("carte " + a.MapId + " : groupe " + group.Id + " niveau " + group.TotalLevel + " (" + string.Join(", ", group.Members.Select(m => m.TemplateId + " niv. " + m.Level)) + ") cellule " + group.CellId);
            int before = a.ReceivedCount;
            var attack = await a.Account.Game.Interactions.MapActions.AttackGroupAsync(group.Id, async cell =>
            {
                var moved = await a.Account.Game.Manager.Mouvements.MoveToAsync(a.Map.GetCellFromId(cell)).ConfigureAwait(false);
                Journal("A [test] marche vers le groupe : " + moved);
            }).ConfigureAwait(false);
            Check(attack.Sent, "Attaque refusée par le bot : " + attack.Message);
            try { gjk = await a.Expect("GJK", "GJK (combat lancé)", 15, before).ConfigureAwait(false); }
            catch (TimeoutException) { Journal("A [test] le groupe a bougé, nouvel essai"); await Task.Delay(500).ConfigureAwait(false); }
        }
        Check(gjk != null, "Aucun combat lancé contre les monstres");
        await PlaceAndReady(a, mark, false).ConfigureAwait(false);
        string gs = await a.Expect("GS", "GS (début du combat)", 30, mark).ConfigureAwait(false);
        evidence.Add(gjk + ", " + gs + ", ennemis : " + string.Join(", ", Enemies(a).Select(e => e.Name + " " + e.Life + " PV")));
        DateTime until = DateTime.UtcNow.AddSeconds(200);
        int turns = 0;
        while (DateTime.UtcNow < until && a.Account.Game.Fight.IsInFight)
        {
            int actor;
            try { actor = await WaitForTurnOf(new[] { a }, 60).ConfigureAwait(false); }
            catch (TimeoutException) { break; }
            if (actor == 0) break;
            turns++;
            if (turns == 1) evidence.Add("vérification GTM : " + await CheckFightersMatchGtm(a, mark).ConfigureAwait(false));
            await PlayTurn(a, true, true, evidence).ConfigureAwait(false);
        }
        if (a.Account.Game.Fight.IsInFight)
        {
            evidence.Add("abandon après " + turns + " tour(s)");
            await a.Account.Game.Fight.GiveUpAsync().ConfigureAwait(false);
        }
        string ge = await a.Expect("GE", "GE (résultat du combat)", 30, mark).ConfigureAwait(false);
        Check(await a.Until(() => a.Account.Game.Fight.LastResult != null, 10).ConfigureAwait(false), "Le bot n'a pas lu le résultat GE");
        FightResultEntry own = a.Account.Game.Fight.LastResult.Find(a.Id);
        evidence.Add(turns + " tour(s), " + Short(ge, 60) + " → " + (own == null ? "personnage absent du résultat" : own.Kind.ToString()));
        await WaitBackOnMap(a).ConfigureAwait(false);
        // Retour sur la carte de départ (défaite : le serveur renvoie au point de sauvegarde).
        string returned = await ReturnAfterFightAsync(a, home, back).ConfigureAwait(false);
        if (returned != null) evidence.Add(returned);
        return string.Join(" ; ", evidence) + " ; carte finale " + a.MapId;
    }

    // ------------------------------------------------------------------------------------------------ g. PNJ et zaap

    private static async Task GoHome(LiveSession s)
    {
        if (s.MapId == startMap) return;
        Trigger direct = Triggers.ForMap(s.MapId).Values.FirstOrDefault(t => t.IsTeleport && t.TargetMapId == startMap);
        if (direct != null) await WalkThroughTrigger(s, direct).ConfigureAwait(false);
    }

    private static async Task<string> NpcDialogAsync()
    {
        await GoHome(a).ConfigureAwait(false);
        var npcs = a.Map.Npcs.ToList();
        if (npcs.Count == 0) throw new SkipScenario("aucun PNJ sur la carte " + a.MapId);
        NpcDialog dialog = a.Account.Game.Interactions.Npc;
        foreach (NpcActor npc in npcs.OrderBy(n => n.Id))
        {
            int[] actions = LangData.IsLoaded("npc") ? LangData.Npc.Actions(npc.TemplateId) : new int[0];
            if (actions.Length > 0 && !actions.Contains(NpcActions.Talk)) continue;
            int mark = a.ReceivedCount;
            var open = await dialog.OpenAsync((int)npc.Id).ConfigureAwait(false);
            Check(open.Sent, "Dialogue refusé par le bot : " + open.Message);
            string answer = await a.Expect(p => p.StartsWith("DCK") || p.StartsWith("DCE") || p.StartsWith("BN"), "DCK (dialogue ouvert)", 10, mark).ConfigureAwait(false);
            if (!answer.StartsWith("DCK")) { Journal("A [test] " + npc.DisplayName + " : " + answer); continue; }
            string question = await a.Expect("DQ", "DQ (question du PNJ)", 10, mark).ConfigureAwait(false);
            Check(await a.Until(() => dialog.IsOpen && dialog.QuestionId >= 0, 5).ConfigureAwait(false), "Le bot n'affiche pas la question du PNJ");
            string text = dialog.QuestionText;
            int answers = dialog.Answers.Count;
            int closeMark = a.ReceivedCount;
            var leave = await dialog.LeaveAsync().ConfigureAwait(false);
            Check(leave.Sent, "Fin du dialogue refusée par le bot : " + leave.Message);
            string dv = await a.Expect("DV", "DV (dialogue fermé)", 10, closeMark).ConfigureAwait(false);
            Check(await a.Until(() => !dialog.IsOpen, 5).ConfigureAwait(false), "Le dialogue reste ouvert dans le bot");
            return npc.DisplayName + " (modèle " + npc.TemplateId + ") : " + answer + ", " + Short(question, 40) + " « " + Short(text, 60) + " » (" + answers + " réponse(s)), " + dv;
        }
        throw new SkipScenario("aucun PNJ de la carte " + a.MapId + " n'accepte le dialogue");
    }

    private static async Task<string> NpcShopAsync()
    {
        await GoHome(a).ConfigureAwait(false);
        NpcShop shop = a.Account.Game.Interactions.Shop;
        foreach (NpcActor npc in a.Map.Npcs.OrderBy(n => n.Id).ToList())
        {
            int[] actions = LangData.IsLoaded("npc") ? LangData.Npc.Actions(npc.TemplateId) : new int[0];
            if (!actions.Contains(NpcActions.BuySell)) continue;
            int mark = a.ReceivedCount;
            var open = await shop.OpenAsync((int)npc.Id).ConfigureAwait(false);
            Check(open.Sent, "Boutique refusée par le bot : " + open.Message);
            string eck = await a.Expect(p => p.StartsWith("ECK") || p.StartsWith("ERE") || p.StartsWith("EV"), "ECK0 (boutique ouverte)", 10, mark).ConfigureAwait(false);
            if (!eck.StartsWith("ECK")) { Journal("A [test] boutique de " + npc.DisplayName + " : " + eck); continue; }
            string list = await a.Expect("EL", "EL (articles)", 10, mark).ConfigureAwait(false);
            Check(await a.Until(() => shop.IsOpen && shop.Articles.Count > 0, 5).ConfigureAwait(false), "Le bot n'a lu aucun article");
            int articles = shop.Articles.Count;
            int kamas = a.Account.Game.character.Kamas;
            ShopArticle article = shop.Articles.Where(x => x.Price.HasValue && x.Price.Value > 0 && x.Price.Value <= kamas).OrderBy(x => x.Price.Value).FirstOrDefault()
                ?? shop.Articles.First();
            int buyMark = a.ReceivedCount;
            int itemsBefore = a.Account.Game.character.Inventory.Objets.Count();
            var buy = await shop.BuyAsync(article.TemplateId, 1).ConfigureAwait(false);
            Check(buy.Sent, "Achat refusé par le bot : " + buy.Message);
            string ebk = await a.Expect(p => p.StartsWith("EBK") || p.StartsWith("EBE"), "EBK (achat accepté)", 10, buyMark).ConfigureAwait(false);
            Check(ebk.StartsWith("EBK"), "Achat refusé par le serveur : " + ebk);
            string oak = await a.Expect(p => p.StartsWith("OAKO") || p.StartsWith("OQ"), "OAKO (objet ajouté)", 10, buyMark).ConfigureAwait(false);
            Check(await a.Until(() => a.Account.Game.character.Kamas < kamas, 5).ConfigureAwait(false), "Kamas inchangés après l'achat");
            // Le prix affiché (règle du client : prix du modèle × BUY_PRICE_MULTIPLICATOR) doit être celui que StarLoco débite.
            Check(!article.Price.HasValue || await a.Until(() => kamas - a.Account.Game.character.Kamas == article.Price.Value, 5).ConfigureAwait(false),
                "Prix affiché " + article.Price + " k, débit réel " + (kamas - a.Account.Game.character.Kamas) + " k");
            int closeMark = a.ReceivedCount;
            var leave = await shop.LeaveAsync().ConfigureAwait(false);
            Check(leave.Sent, "Fermeture refusée par le bot : " + leave.Message);
            await a.Expect("EV", "EV (boutique fermée)", 10, closeMark).ConfigureAwait(false);
            return npc.DisplayName + " : " + Short(eck, 20) + ", " + articles + " article(s) (" + Short(list, 50) + ") ; achat de " + article.TemplateId + " (" + article.Name + ", "
                + article.Price + " k) : " + ebk + ", " + Short(oak, 40) + ", kamas " + kamas + " → " + a.Account.Game.character.Kamas + ", objets " + itemsBefore + " → " + a.Account.Game.character.Inventory.Objets.Count();
        }
        throw new SkipScenario("aucun PNJ marchand sur la carte " + a.MapId);
    }

    private static async Task<string> ZaapAsync()
    {
        RequireMaps();
        await GoHome(a).ConfigureAwait(false);
        ZaapDialog zaap = a.Account.Game.Interactions.Zaap;
        short cell = a.Map.MapCells.Where(c => c != null && zaap.IsZaapCell(c.CellID)).Select(c => c.CellID).DefaultIfEmpty((short)-1).First();
        if (cell < 0) throw new SkipScenario("aucun zaap connu sur la carte " + a.MapId);
        int mark = a.ReceivedCount, home = a.MapId;
        var open = await zaap.OpenAsync(cell).ConfigureAwait(false);
        Check(open.Sent, "Zaap refusé par le bot : " + open.Message);
        string wc = await a.Expect(p => p.StartsWith("WC") || p.StartsWith("GA;0") || p.StartsWith("Im"), "WC (destinations du zaap)", 20, mark).ConfigureAwait(false);
        Check(wc.StartsWith("WC"), "Le serveur n'ouvre pas le zaap : " + wc);
        Check(await a.Until(() => zaap.IsOpen && zaap.Destinations.Count > 0, 5).ConfigureAwait(false), "Le bot n'a lu aucune destination");
        int destinations = zaap.Destinations.Count;
        int kamas = a.Account.Game.character.Kamas;
        ZaapDestination destination = zaap.Destinations.Where(d => !d.IsCurrent && d.Cost <= kamas && Map.AllBotMaps.ContainsKey(d.MapId)).OrderBy(d => d.Cost).FirstOrDefault();
        Check(destination != null, "Aucune destination abordable");
        int travel = a.ReceivedCount;
        var go = await zaap.TeleportAsync(destination.MapId).ConfigureAwait(false);
        Check(go.Sent, "Téléportation refusée par le bot : " + go.Message);
        await a.Expect("GDM|" + destination.MapId, "GDM|" + destination.MapId + " (arrivée)", 20, travel).ConfigureAwait(false);
        Check(await a.Until(() => a.MapId == destination.MapId && a.Map.Self != null, 10).ConfigureAwait(false), "Le bot n'est pas sur la carte d'arrivée");
        string cost = "kamas " + kamas + " → " + a.Account.Game.character.Kamas;
        // Retour par le zaap d'arrivée (ALL_ZAAP : tous les zaaps sont connus).
        string back = "";
        short there = a.Map.MapCells.Where(c => c != null && zaap.IsZaapCell(c.CellID)).Select(c => c.CellID).DefaultIfEmpty((short)-1).First();
        if (there >= 0)
        {
            int again = a.ReceivedCount;
            var reopen = await zaap.OpenAsync(there).ConfigureAwait(false);
            if (reopen.Sent && (await a.Expect(p => p.StartsWith("WC") || p.StartsWith("GA;0") || p.StartsWith("Im"), "WC au retour", 20, again).ConfigureAwait(false)).StartsWith("WC")
                && await a.Until(() => zaap.Destinations.Any(d => d.MapId == home), 5).ConfigureAwait(false))
            {
                int returning = a.ReceivedCount;
                var home2 = await zaap.TeleportAsync(home).ConfigureAwait(false);
                if (home2.Sent) { await a.Expect("GDM|" + home, "GDM|" + home + " (retour)", 20, returning).ConfigureAwait(false); back = ", retour par le zaap " + there + " → carte " + home; }
            }
            else if (zaap.IsOpen) await zaap.LeaveAsync().ConfigureAwait(false);
        }
        return "zaap cellule " + cell + " : " + Short(wc, 60) + " (" + destinations + " destination(s)) ; WU" + destination.MapId + " (" + destination.Cost + " k) → GDM|" + destination.MapId + ", " + cost + back;
    }
}

// Scénario h (lot X1) : corps à corps. B (épée) et A (arc) contre des monstres dans le même combat, puis A à mains nues contre B en défi.
internal static partial class Live
{
    /// <summary>Armes de niveau 1 sans condition du PNJ 816 (carte 164) : épée 2170 (5 PA, portée 1) et arc 6783 (4 PA, portée 2 à 6).</summary>
    private const int SwordTemplate = 2170, BowTemplate = 6783, WeaponSellerTemplate = 816;

    private static Tool_BotProtocol.Game.Perso.Inventory.InventoryObjects Owned(LiveSession s, int template) =>
        s.Account.Game.character.Inventory.Objets.FirstOrDefault(x => x.ID == template && x.Qua > 0);

    /// <summary>
    /// Trois combats de monstres à la suite ne tiennent pas dans la limite de 240 s du lanceur : StarLoco remet la vie à 1 après
    /// chaque défaite et n'en rend qu'un point par seconde assis. L'épée et l'arc (<c>GA;303</c>) sont donc joués dans un même
    /// combat de monstres, B lançant le combat et A le rejoignant ; les poings (sort 0) sont joués en défi, qui ne coûte pas de vie.
    /// </summary>
    private static async Task<string> CloseCombatAsync()
    {
        RequireMaps();
        if (!LangData.IsLoaded("items") || !LangData.IsLoaded("classes")) throw new SkipScenario("textes du client absents : le bot ne connaît ni les PA ni la portée du corps à corps");
        await GoHome(a).ConfigureAwait(false);
        await GoHome(b).ConfigureAwait(false);
        Check(a.MapId == b.MapId, "A et B ne sont pas sur la même carte : " + a.MapId + " et " + b.MapId);
        var evidence = new List<string>();
        // Achats en même temps chez le même marchand : l'arc pour A (Crâ, fenêtre de jeu), l'épée pour B (Iop).
        Task<string> bow = BuyAndWieldAsync(a, BowTemplate), sword = BuyAndWieldAsync(b, SwordTemplate);
        evidence.AddRange(await Task.WhenAll(bow, sword).ConfigureAwait(false));
        // A et B ont toute leur vie (remise en état : « .vie » après la défaite du scénario f).
        evidence.Add(await TeamMonsterFightAsync(evidence).ConfigureAwait(false));
        evidence.Add(await WieldAsync(a, 0).ConfigureAwait(false));
        // Après le combat, A et B peuvent partager une cellule (point de sauvegarde après la défaite, ou même déclencheur au retour) :
        // le défi se joue depuis deux cellules, ses deux fins de combat ajoutant chacun à sa cellule en même temps (voir SeparateAsync).
        string apart = await SeparateAsync().ConfigureAwait(false);
        if (apart != null) evidence.Add(apart);
        evidence.Add(await FistsDuelAsync(evidence).ConfigureAwait(false));
        return string.Join(" ; ", evidence);
    }

    private static async Task<string> BuyAndWieldAsync(LiveSession s, int template)
    {
        string bought = await BuyWeaponsAsync(s, template).ConfigureAwait(false);
        return bought + " ; " + s.Label + " " + await WieldAsync(s, template).ConfigureAwait(false);
    }

    /// <summary>Boutique du marchand d'armes : <c>ER0|id</c>, puis <c>EB&lt;modèle&gt;|1</c> pour chaque arme absente de l'inventaire.</summary>
    private static async Task<string> BuyWeaponsAsync(LiveSession s, params int[] templates)
    {
        // GoHome rend la main dès GDM : les PNJ (GM) arrivent après GI.
        await s.Until(() => s.Map.Npcs.Any(n => n.TemplateId == WeaponSellerTemplate), 10).ConfigureAwait(false);
        NpcActor seller = s.Map.Npcs.FirstOrDefault(n => n.TemplateId == WeaponSellerTemplate);
        if (seller == null) throw new SkipScenario("pas de marchand d'armes (PNJ " + WeaponSellerTemplate + ") sur la carte " + s.MapId);
        NpcShop shop = s.Account.Game.Interactions.Shop;
        int mark = s.ReceivedCount;
        var open = await shop.OpenAsync((int)seller.Id).ConfigureAwait(false);
        Check(open.Sent, s.Label + " : boutique refusée par le bot : " + open.Message);
        string eck = await s.Expect(p => p.StartsWith("ECK") || p.StartsWith("ERE") || p.StartsWith("EV"), "ECK0 (boutique ouverte)", 10, mark).ConfigureAwait(false);
        Check(eck.StartsWith("ECK"), s.Label + " : le serveur n'ouvre pas la boutique du PNJ " + WeaponSellerTemplate + " : " + eck);
        Check(await s.Until(() => shop.IsOpen && templates.All(t => shop.Articles.Any(x => x.TemplateId == t)), 10).ConfigureAwait(false),
            "Armes " + string.Join(", ", templates) + " absentes de la boutique : " + string.Join(",", shop.Articles.Select(x => x.TemplateId)));
        var bought = new List<string>();
        foreach (int template in templates)
        {
            if (Owned(s, template) != null) { bought.Add(template + " déjà dans l'inventaire"); continue; }
            int buyMark = s.ReceivedCount;
            var buy = await shop.BuyAsync(template, 1).ConfigureAwait(false);
            Check(buy.Sent, s.Label + " : achat de " + template + " refusé par le bot : " + buy.Message);
            string ebk = await s.Expect(p => p.StartsWith("EBK") || p.StartsWith("EBE"), "EBK (achat accepté)", 10, buyMark).ConfigureAwait(false);
            Check(ebk.StartsWith("EBK"), s.Label + " : achat de " + template + " refusé par le serveur : " + ebk);
            Check(await s.Until(() => Owned(s, template) != null && !shop.IsPending, 10).ConfigureAwait(false), s.Label + " : l'arme " + template + " n'arrive pas dans l'inventaire (OAKO)");
            bought.Add(template + " : " + ebk + ", " + Short(s.ReceivedSince(buyMark).FirstOrDefault(p => p.StartsWith("OAKO")), 30));
        }
        int closeMark = s.ReceivedCount;
        var leave = await shop.LeaveAsync().ConfigureAwait(false);
        Check(leave.Sent, s.Label + " : fermeture de la boutique refusée par le bot : " + leave.Message);
        await s.Expect("EV", "EV (boutique fermée)", 10, closeMark).ConfigureAwait(false);
        return s.Label + " achète au PNJ " + WeaponSellerTemplate + " " + seller.DisplayName + " : " + string.Join(", ", bought);
    }

    /// <summary>Arme <paramref name="template"/> en position 1 (<c>OM&lt;objet&gt;|1</c>), ou mains nues (<c>OM&lt;objet&gt;|-1</c>) pour 0.</summary>
    private static async Task<string> WieldAsync(LiveSession s, int template)
    {
        var inventory = s.Account.Game.character.Inventory;
        Fights fight = s.Account.Game.Fight;
        var held = inventory.GetObjetsPosition(Tool_BotProtocol.Game.Perso.Inventory.Enums.InventorySlots.WEAPON);
        int mark = s.ReceivedCount;
        string om = "aucune arme en main";
        if (template == 0 && held != null)
        {
            Check(await inventory.Desequip_Item(held).ConfigureAwait(false), s.Label + " : retrait de l'arme refusé par le bot");
            om = await s.Expect(p => p == "OM" + held.Inventory_ID + "|", "OM" + held.Inventory_ID + "| (arme rangée)", 10, mark).ConfigureAwait(false);
        }
        else if (template != 0)
        {
            var item = Owned(s, template);
            Check(item != null, s.Label + " : arme " + template + " absente de l'inventaire");
            Check(await inventory.Equip_item(item).ConfigureAwait(false), s.Label + " : équipement de l'arme " + template + " refusé par le bot");
            om = await s.Expect(p => p == "OM" + item.Inventory_ID + "|1", "OM" + item.Inventory_ID + "|1 (arme en main)", 10, mark).ConfigureAwait(false);
        }
        Check(await s.Until(() => fight.CloseCombat.WeaponTemplateId == template, 5).ConfigureAwait(false),
            s.Label + " : le bot garde l'arme " + fight.CloseCombat.WeaponTemplateId + " en position 1 après " + om);
        CloseCombatStats stats = fight.CloseCombat;
        Check(stats.HasData && stats.Source == (template == 0 ? "classe" : "arme"), s.Label + " : PA et portée de " + stats.Name + " non lus dans les textes du client (" + stats.Source + ")");
        return om + " → " + stats.Summary + " (" + stats.Source + ")";
    }

    /// <summary>
    /// B lance un combat contre le groupe le plus faible de la carte ou d'une voisine, A le rejoint pendant le placement
    /// (<c>GA903&lt;combat&gt;;&lt;id de B&gt;</c>, comme le clic sur l'épée du combat) ; chacun se met à portée et frappe à son tour
    /// (<see cref="CloseCombatTurnAsync"/>), puis les deux abandonnent.
    /// </summary>
    private static async Task<string> TeamMonsterFightAsync(List<string> evidence)
    {
        int home = b.MapId;
        Trigger back = null;
        if (b.Map.MonsterGroups.Count == 0)
            foreach (Trigger go in Triggers.ForMap(home).Values.Where(t => t.IsTeleport && t.TargetMapId != home && Map.AllBotMaps.ContainsKey(t.TargetMapId)).OrderBy(t => t.CellId))
            {
                Trigger reverse = Triggers.ForMap(go.TargetMapId).Values.FirstOrDefault(t => t.IsTeleport && t.TargetMapId == home);
                if (reverse == null) continue;
                // L'un après l'autre : deux téléportations simultanées depuis la cellule du déclencheur corrompent sa liste de joueurs
                // chez StarLoco (voir SeparateAsync), puis toute téléportation depuis la carte échoue (x4b et x4c).
                await WalkThroughTrigger(b, go).ConfigureAwait(false);
                await WalkThroughTrigger(a, go).ConfigureAwait(false);
                if (await b.Until(() => b.Map.MonsterGroups.Count > 0, 3).ConfigureAwait(false)) { back = reverse; break; }
                await WalkThroughTrigger(b, reverse).ConfigureAwait(false);
                await WalkThroughTrigger(a, reverse).ConfigureAwait(false);
            }
        if (b.Map.MonsterGroups.Count == 0) throw new SkipScenario("aucun groupe de monstres sur la carte " + home + " ni sur ses voisines");
        Check(a.MapId == b.MapId, "A n'a pas suivi B sur la carte " + b.MapId);
        int markA = a.ReceivedCount, markB = b.ReceivedCount;
        string gjk = null, group = null;
        for (int attempt = 0; attempt < 4 && gjk == null; attempt++)
        {
            MonsterGroupActor target = b.Map.MonsterGroups.OrderBy(g => g.TotalLevel).First();
            group = "carte " + b.MapId + ", groupe niveau " + target.TotalLevel + " (" + target.Members.Count + " monstres)";
            int before = b.ReceivedCount;
            var attack = await b.Account.Game.Interactions.MapActions.AttackGroupAsync(target.Id, async cell =>
            {
                var moved = await b.Account.Game.Manager.Mouvements.MoveToAsync(b.Map.GetCellFromId(cell)).ConfigureAwait(false);
                Journal("B [test] marche vers le groupe : " + moved);
            }).ConfigureAwait(false);
            Check(attack.Sent, "Attaque du groupe refusée par le bot : " + attack.Message);
            try { gjk = await b.Expect("GJK", "GJK (combat lancé)", 15, before).ConfigureAwait(false); }
            catch (TimeoutException) { Journal("B [test] le groupe a bougé, nouvel essai"); await Task.Delay(500).ConfigureAwait(false); }
        }
        Check(gjk != null, "Aucun combat lancé contre les monstres");
        // A voit le drapeau du combat (Gc+, équipe de B) et rejoint cette équipe.
        FightSwordsActor swords = null;
        Check(await a.Until(() => (swords = a.Map.FightSwords.Values.FirstOrDefault(f => f.Teams.Any(t => t.TeamId == b.Id))) != null, 10).ConfigureAwait(false),
            "A ne voit pas le drapeau du combat de B (Gc+)");
        int joinMark = a.ReceivedCount;
        var join = await a.Account.Game.Interactions.MapActions.JoinFightAsync(swords.FightId, b.Id).ConfigureAwait(false);
        Check(join.Sent, "A ne peut pas rejoindre le combat : " + join.Message);
        string joined = await a.Expect(p => p.StartsWith("GJK") || p.StartsWith("GA;903;") || p.StartsWith("Im191"), "GJK (A rejoint le combat de B)", 10, joinMark).ConfigureAwait(false);
        Check(joined.StartsWith("GJK"), "StarLoco refuse que A rejoigne le combat : " + joined);
        evidence.Add(await PlaceForWeaponAndReady(a, joinMark).ConfigureAwait(false));
        await PlaceAndReady(b, markB, false).ConfigureAwait(false);
        await b.Expect("GS", "GS (début du combat)", 30, markB).ConfigureAwait(false);
        var proofs = new Dictionary<int, string>();
        int turns = 0;
        DateTime until = DateTime.UtcNow.AddSeconds(90);
        while (proofs.Count < 2 && DateTime.UtcNow < until && b.Account.Game.Fight.IsInFight)
        {
            int actor;
            try { actor = await WaitForTurnOf(new[] { a, b }, 40).ConfigureAwait(false); }
            catch (TimeoutException) { break; }
            if (actor == 0) break;
            LiveSession s = actor == a.Id ? a : b;
            turns++;
            if (!proofs.ContainsKey(actor))
            {
                string proof = await CloseCombatTurnAsync(s, evidence).ConfigureAwait(false);
                if (proof != null) proofs[actor] = proof;
            }
            if (proofs.Count == 2) break;
            await PassIfStillMyTurn(s).ConfigureAwait(false);
        }
        // Abandon (GQ) des deux personnages : StarLoco les compte comme morts et termine le combat. L'un après l'autre, A revenu sur
        // la carte avant l'abandon de B : leurs retours (GC1) ajoutent sinon les deux joueurs en même temps à la même cellule.
        if (a.Account.Game.Fight.IsInFight)
        {
            await a.Account.Game.Fight.GiveUpAsync().ConfigureAwait(false);
            await a.Expect(p => p.StartsWith("GE") || p.StartsWith("GV"), "GE ou GV (fin du combat, A)", 30, joinMark).ConfigureAwait(false);
            await WaitBackOnMap(a).ConfigureAwait(false);
        }
        if (b.Account.Game.Fight.IsInFight) await b.Account.Game.Fight.GiveUpAsync().ConfigureAwait(false);
        string ge = await b.Expect(p => p.StartsWith("GE") || p.StartsWith("GV"), "GE (fin du combat, B)", 30, markB).ConfigureAwait(false);
        await a.Expect(p => p.StartsWith("GE") || p.StartsWith("GV"), "GE ou GV (fin du combat, A)", 30, joinMark).ConfigureAwait(false);
        await WaitBackOnMap(a).ConfigureAwait(false);
        await WaitBackOnMap(b).ConfigureAwait(false);
        // Défaite : StarLoco renvoie au point de sauvegarde ; sinon revenir par le déclencheur (ou « .start » depuis une cellule non praticable).
        foreach (LiveSession s in new[] { a, b })
        {
            string returned = await ReturnAfterFightAsync(s, home, back).ConfigureAwait(false);
            if (returned != null) evidence.Add(returned);
        }
        Check(proofs.ContainsKey(b.Id), "B n'a pas frappé avec l'épée en " + turns + " tour(s) de joueur");
        Check(proofs.ContainsKey(a.Id), "A n'a pas tiré à l'arc en " + turns + " tour(s) de joueur");
        return group + " : " + gjk + ", A rejoint B (" + Short(joined, 20) + "), " + turns + " tour(s) de joueur ; " + proofs[b.Id] + " ; " + proofs[a.Id] + " ; " + Short(ge, 30);
    }

    /// <summary>
    /// Défi de A, mains nues, contre B (<c>GA900</c>, comme le scénario e) : B s'approche, A frappe du poing (sort 0 chez StarLoco).
    /// Un défi ne coûte pas de vie : les PV d'avant le combat sont rendus à la fin.
    /// </summary>
    private static async Task<string> FistsDuelAsync(List<string> evidence)
    {
        Check(a.MapId == b.MapId, "A et B ne sont pas sur la même carte pour le défi : " + a.MapId + " et " + b.MapId);
        int markA = a.ReceivedCount, markB = b.ReceivedCount;
        var challenge = await a.Account.Game.Interactions.MapActions.ChallengeAsync(b.Id).ConfigureAwait(false);
        Check(challenge.Sent, "Défi refusé par le bot : " + challenge.Message);
        await b.Expect(p => p.StartsWith("GA;900;" + a.Id + ";"), "GA;900 (défi reçu)", 10, markB).ConfigureAwait(false);
        Check(await b.Until(() => b.Account.Game.Interactions.MapActions.PendingChallenge?.ChallengerId == a.Id, 5).ConfigureAwait(false), "B n'a pas de défi en attente.");
        var accept = await b.Account.Game.Interactions.MapActions.AcceptChallengeAsync(a.Id).ConfigureAwait(false);
        Check(accept.Sent, "Acceptation du défi refusée par le bot : " + accept.Message);
        await a.Expect("GJK", "GJK (entrée en défi, A)", 15, markA).ConfigureAwait(false);
        await b.Expect("GJK", "GJK (entrée en défi, B)", 15, markB).ConfigureAwait(false);
        await PlaceAndReady(a, markA, false).ConfigureAwait(false);
        await PlaceAndReady(b, markB, false).ConfigureAwait(false);
        await a.Expect("GS", "GS (début du défi)", 30, markA).ConfigureAwait(false);
        string proof = null;
        int turns = 0;
        DateTime until = DateTime.UtcNow.AddSeconds(60);
        while (proof == null && DateTime.UtcNow < until && a.Account.Game.Fight.IsInFight)
        {
            int actor;
            try { actor = await WaitForTurnOf(new[] { a, b }, 40).ConfigureAwait(false); }
            catch (TimeoutException) { break; }
            if (actor == 0) break;
            turns++;
            LiveSession s = actor == a.Id ? a : b;
            if (s == a) proof = await CloseCombatTurnAsync(a, evidence).ConfigureAwait(false);
            else
            {
                Fights fight = b.Account.Game.Fight;
                CombatFighter foe = Enemies(b).FirstOrDefault();
                short? step = foe == null || DistanceTo(b, foe) <= 1 ? null : StepToward(b, foe, true);
                int pm = fight.MovementPoints;
                if (step.HasValue && (await fight.MoveAsync(step.Value).ConfigureAwait(false)).Sent)
                    await b.Until(() => !fight.IsActionPending && fight.MovementPoints < pm, 10).ConfigureAwait(false);
            }
            if (proof == null) await PassIfStillMyTurn(s).ConfigureAwait(false);
        }
        if (b.Account.Game.Fight.IsInFight) await b.Account.Game.Fight.GiveUpAsync().ConfigureAwait(false);
        string ge = await a.Expect("GE", "GE (fin du défi)", 20, markA).ConfigureAwait(false);
        await b.Expect(p => p.StartsWith("GE") || p.StartsWith("GV"), "GE ou GV (fin du défi, B)", 20, markB).ConfigureAwait(false);
        await WaitBackOnMap(a).ConfigureAwait(false);
        await WaitBackOnMap(b).ConfigureAwait(false);
        Check(proof != null, "A n'a pas frappé B du poing en " + turns + " tour(s)");
        return "défi A contre B, " + turns + " tour(s) : " + proof + " ; " + Short(ge, 30);
    }

    private static async Task PassIfStillMyTurn(LiveSession s)
    {
        Fights fight = s.Account.Game.Fight;
        if (!fight.IsInFight || fight.Phase == CombatPhase.Finished || !fight.IsMyTurn) return;
        int passMark = s.ReceivedCount;
        var pass = await fight.PassTurnAsync().ConfigureAwait(false);
        if (pass.Sent) await s.Expect(p => p.StartsWith("GTF" + s.Id) || p.StartsWith("GE"), "GTF (fin du tour)", 15, passMark).ConfigureAwait(false);
    }

    /// <summary>Ennemi que le bot accepte d'attaquer depuis la cellule actuelle, le plus faible d'abord.</summary>
    private static CombatFighter InCloseCombatReach(LiveSession s) =>
        Enemies(s).Where(e => s.Account.Game.Fight.GetCloseCombatUnavailableReason((short)e.CellId) == null).OrderBy(e => e.Life).FirstOrDefault();

    /// <summary>
    /// Placement de A, qui rejoint le combat avec l'arc (portée 2 à 6) : cellule de placement libre sans ennemi au contact (sinon A,
    /// taclé en s'écartant, perd ses PM et ne peut pas tirer à une case), avec un ennemi à portée de l'arme si possible, puis la plus
    /// éloignée des ennemis ; enfin « prêt ». Renvoie le placement choisi.
    /// </summary>
    private static async Task<string> PlaceForWeaponAndReady(LiveSession s, int mark)
    {
        Fights fight = s.Account.Game.Fight;
        CloseCombatStats weapon = fight.CloseCombat;
        Check(await s.Until(() => fight.IsPlacement && fight.PlacementCells.Length > 0 && Self(s) != null && Enemies(s).Any(), 10).ConfigureAwait(false),
            s.Label + " : pas de cellules de placement (GP) ou pas d'ennemi en placement");
        short current = (short)Self(s).CellId;
        var foes = Enemies(s).Select(e => s.Map.GetCellFromId((short)e.CellId)).Where(c => c != null).ToList();
        var best = fight.PlacementCells.Where(c => c == current || !fight.Fighters.Values.Any(f => f.CellId == c))
            .Select(c => s.Map.GetCellFromId(c)).Where(c => c != null)
            .Select(c => new
            {
                Cell = c,
                Nearest = foes.Count == 0 ? int.MaxValue : foes.Min(f => c.GetDistanceBetweenCells(f)),
                InReach = foes.Count(f => { int d = c.GetDistanceBetweenCells(f); return d >= weapon.MinRange && d <= weapon.MaxRange && (!weapon.LineOnly || c.AreCellsOnline(f)); })
            })
            .OrderByDescending(x => x.Nearest > 1).ThenByDescending(x => x.InReach > 0).ThenByDescending(x => x.Nearest).ThenBy(x => x.Cell.CellID != current)
            .FirstOrDefault();
        Check(best != null, s.Label + " : aucune cellule de placement libre");
        if (best.Cell.CellID != current) await MoveInPlacement(s, best.Cell.CellID).ConfigureAwait(false);
        await ReadyAsync(s, mark).ConfigureAwait(false);
        return s.Label + " se place en " + best.Cell.CellID + (best.Cell.CellID != current ? " (au lieu de " + current + ")" : "") + " pour " + weapon.Summary
            + " : ennemi le plus proche à " + best.Nearest + " case(s), " + best.InReach + " à portée";
    }

    /// <summary>Cellules libres à portée de PM (en cases, comme <see cref="StepToward"/>) d'où un ennemi est à portée de l'arme, les plus proches d'abord.</summary>
    private static List<short> CellsInCloseCombatReach(LiveSession s, CloseCombatStats weapon)
    {
        Fights fight = s.Account.Game.Fight;
        Cell origin = s.Map.GetCellFromId((short)(Self(s)?.CellId ?? -1));
        if (origin == null) return new List<short>();
        var foes = Enemies(s).Select(e => s.Map.GetCellFromId((short)e.CellId)).Where(c => c != null).ToList();
        var occupied = new HashSet<int>(fight.Fighters.Values.Where(f => !f.IsDead).Select(f => (int)f.CellId));
        return s.Map.MapCells.Where(c => c != null && c.IsActive && c.IsWalkable() && !occupied.Contains(c.CellID))
            .Where(c => { int d = origin.GetDistanceBetweenCells(c); return d >= 1 && d <= fight.MovementPoints; })
            .Where(c => foes.Any(f => { int d = c.GetDistanceBetweenCells(f); return d >= weapon.MinRange && d <= weapon.MaxRange && (!weapon.LineOnly || c.AreCellsOnline(f)); }))
            .OrderBy(c => origin.GetDistanceBetweenCells(c)).Select(c => c.CellID).Take(3).ToList();
    }

    /// <summary>
    /// Tour du personnage : déplacement à portée si besoin, puis <c>GA303&lt;cellule&gt;</c> sur l'ennemi. Avec une arme, StarLoco
    /// répond <c>GA;303;id;cellule</c> (ou <c>GA;305</c>, échec critique qui finit le tour), les dégâts <c>GA;100</c>, <c>GA;102</c>
    /// et <c>GAF</c> ; à mains nues il lance le sort 0 (<c>GA;300;id;0,cellule,…</c>) et libère le lancer par <c>GA;102;id;id,-0</c>.
    /// Renvoie la preuve, ou null si aucune attaque n'a été vérifiée ce tour.
    /// </summary>
    private static async Task<string> CloseCombatTurnAsync(LiveSession s, List<string> evidence)
    {
        Fights fight = s.Account.Game.Fight;
        CloseCombatStats weapon = fight.CloseCombat;
        CombatFighter target = InCloseCombatReach(s);
        if (target == null && fight.MovementPoints > 0)
        {
            List<short> destinations = CellsInCloseCombatReach(s, weapon);
            CombatFighter nearest = Enemies(s).OrderBy(e => DistanceTo(s, e)).FirstOrDefault();
            short? closer = nearest == null ? null : StepToward(s, nearest, true);
            if (destinations.Count == 0 && closer.HasValue) destinations.Add(closer.Value);
            foreach (short destination in destinations)
            {
                int pm = fight.MovementPoints, from = Self(s)?.CellId ?? -1;
                var moved = await fight.MoveAsync(destination).ConfigureAwait(false);
                if (!moved.Sent) { Journal(s.Label + " [test] déplacement vers " + destination + " refusé par le bot : " + moved.Message); continue; }
                // GA;1 puis GA;129, ou GA;104 et GA;129 si le personnage est taclé : les PM baissent dans les deux cas.
                await s.Until(() => !fight.IsActionPending && fight.MovementPoints < pm, 10).ConfigureAwait(false);
                Journal(s.Label + " [test] pour " + weapon.Name + " : cellule " + from + " → " + (Self(s)?.CellId ?? -1) + ", PM " + pm + " → " + fight.MovementPoints);
                break;
            }
            target = InCloseCombatReach(s);
        }
        if (target == null)
        {
            Journal(s.Label + " [test] aucun ennemi à portée de " + weapon.Summary + " : " + string.Join(" ; ", Enemies(s).Select(e => e.Name + " cellule " + e.CellId
                + ", distance " + DistanceTo(s, e) + " → " + (fight.GetCloseCombatUnavailableReason((short)e.CellId) ?? "possible"))));
            return null;
        }
        if (s == a && window != null)
        {
            // La case ² de la fenêtre de A décrit l'arme réellement en main (ou les poings de la classe).
            string tip = await OnUi(() => { window.Banner.Shortcuts.RefreshContent(); return window.Banner.Shortcuts.CloseCombatTooltip; }).ConfigureAwait(false);
            Check(tip != null && tip.Contains(weapon.Summary), "A : la case ² de la fenêtre ne décrit pas " + weapon.Summary + " : " + tip);
        }
        short cell = (short)target.CellId;
        int lifeBefore = target.Life, paStart = fight.ActionPoints, distance = DistanceTo(s, target), mark = s.ReceivedCount, sentMark = s.SentCount;
        var sent = await fight.AttackAsync(cell).ConfigureAwait(false);
        Check(sent.Sent, s.Label + " : attaque refusée par le bot : " + sent.Message);
        Check(s.SentSince(sentMark).Any(p => p.TrimEnd('\0', '\n', '\r') == "GA303" + cell), s.Label + " : GA303" + cell + " absent des envois : " + string.Join(", ", s.SentSince(sentMark)));
        string id = s.Id.ToString(CultureInfo.InvariantCulture), answer;
        if (weapon.IsArmed)
        {
            // Échec critique : StarLoco envoie GA;305;<id> sans argument (vu le 21 octobre 2026, Épée de Mercenaire de B).
            answer = await s.Expect(p => p.StartsWith("GA;303;" + id + ";") || p == "GA;305;" + id || p.StartsWith("GA;305;" + id + ";") || p.StartsWith("Im1170") || p.StartsWith("Im1171"),
                "GA;303 (attaque au corps à corps)", 10, mark).ConfigureAwait(false);
            Check(!answer.StartsWith("Im"), s.Label + " : StarLoco refuse une attaque que le bot autorisait (" + weapon.Summary + ", distance " + distance + ", " + paStart + " PA) : " + answer);
            if (answer.StartsWith("GA;305"))
            {
                await s.Until(() => !fight.IsActionPending, 5).ConfigureAwait(false);
                evidence.Add(s.Label + " : échec critique avec " + weapon.Name + " (" + answer + "), StarLoco finit le tour");
                return null;
            }
            Check(answer == "GA;303;" + id + ";" + cell, s.Label + " : GA;303 sur une autre cellule que " + cell + " : " + answer);
        }
        else
        {
            answer = await s.Expect(p => p.StartsWith("GA;300;" + id + ";0,") || p.StartsWith("GA;302;" + id + ";") || p == "GA;102;" + id + ";" + id + ",-0",
                "GA;300 (sort 0, coup de poing)", 10, mark).ConfigureAwait(false);
            Check(!answer.StartsWith("GA;102"), s.Label + " : StarLoco refuse le coup de poing que le bot autorisait (distance " + distance + ", " + paStart + " PA) : " + answer);
            if (answer.StartsWith("GA;302"))
            {
                await s.Until(() => !fight.IsActionPending, 5).ConfigureAwait(false);
                evidence.Add(s.Label + " : échec critique au coup de poing (" + answer + ")");
                return null;
            }
            Check(answer.StartsWith("GA;300;" + id + ";0," + cell + ","), s.Label + " : coup de poing sur une autre cellule que " + cell + " : " + answer);
        }
        string damage = await s.Expect(p => p.StartsWith("GA;100;") && p.Contains(";" + target.Id + ","), "GA;100 (dégâts sur la cible)", 10, mark).ConfigureAwait(false);
        int delta = int.Parse(damage.Split(';')[3].Split(',')[1], CultureInfo.InvariantCulture);
        Check(await s.Until(() => LifeOf(s, target.Id) == lifeBefore + delta || !fight.IsInFight, 5).ConfigureAwait(false),
            s.Label + " : vie de " + target.Name + " " + LifeOf(s, target.Id) + ", attendu " + (lifeBefore + delta));
        string proof = s.Label + " frappe avec " + weapon.Summary + " à distance " + distance + " : GA303" + cell + " → " + Short(answer, 40) + ", " + damage
            + " (PV de " + target.Name + " " + lifeBefore + "→" + (lifeBefore + delta) + ")";
        // Cible tuée et combat fini (dernier adversaire) : StarLoco envoie GE avant le reste.
        string paid = await s.Expect(p => (p.StartsWith("GA;102;" + id + ";" + id + ",-") && p != "GA;102;" + id + ";" + id + ",-0") || p.StartsWith("GE"),
            "GA;102 (PA du corps à corps)", 10, mark).ConfigureAwait(false);
        if (paid.StartsWith("GE")) return proof + ", combat terminé (" + Short(paid, 24) + ")";
        int cost = -int.Parse(paid.Split(';')[3].Split(',')[1], CultureInfo.InvariantCulture);
        Check(cost == weapon.ActionPoints, s.Label + " : StarLoco retire " + cost + " PA, le bot en prévoyait " + weapon.ActionPoints + " (" + weapon.Summary + ")");
        // Arme : l'attente se libère sur GAF (GKK renvoyé) ; poings : sur GA;102;id;id,-0, une seconde plus tard.
        Check(await s.Until(() => !fight.IsActionPending || !fight.IsInFight, 5).ConfigureAwait(false), s.Label + " : l'attaque reste en attente dans le bot");
        if (!fight.IsInFight || s.ReceivedSince(mark).Any(p => p.StartsWith("GE"))) return proof + ", " + paid + ", combat terminé par ce coup";
        string gaf = s.ReceivedSince(mark).FirstOrDefault(p => p.StartsWith("GAF"));
        Check(gaf != null && s.SentSince(sentMark).Any(p => p.StartsWith("GKK")), s.Label + " : GAF du corps à corps sans GKK du bot (" + gaf + ")");
        Check(await s.Until(() => fight.ActionPoints == paStart - cost, 5).ConfigureAwait(false), s.Label + " : PA du bot " + fight.ActionPoints + ", attendu " + (paStart - cost));
        return proof + ", " + paid + ", " + gaf + " → GKK, PA " + paStart + "→" + fight.ActionPoints;
    }
}

// Scénarios x2 (lot X2) : orientation et gifle de A vues par B ; tombe, âme libérée, fantôme ramené à la vie par la statue du
// phénix ; mariage devant le prêtre. Les personnages de x2b et x2c sont écrits dans la base de StarLoco puis supprimés.
internal static partial class Live
{
    /// <summary>
    /// Carte d'Astrub (sous-zone 95, zone 18) où la tombe est posée : <c>Constant.tpCim</c> de StarLoco envoie les zones 3, 4, 6,
    /// 18, 25, 27 et 41 au cimetière <see cref="CemeteryMap"/> (cellule 196), dont la cellule 252 porte la statue du phénix (objet 542).
    /// </summary>
    private const int TombMap = 7411, TombCell = 300, CemeteryMap = 8534, GhostGfx = 8004;
    /// <summary>Objets interactifs « Statue Phoenix » de <c>InteractiveObject.getActionIO</c> (StarLoco).</summary>
    private static readonly int[] PhoenixObjects = { 542, 1524 };

    /// <summary>
    /// A ouvre le menu de son personnage dans la fenêtre de jeu, choisit « Changer son orientation » puis une flèche du sélecteur :
    /// <c>eD&lt;dir&gt;</c> part (<c>Emotes.setDirection</c>), StarLoco renvoie <c>eD&lt;A&gt;|&lt;dir&gt;</c> à toute la carte et B voit A tourné.
    /// </summary>
    private static async Task<string> DirectionAsync()
    {
        await GoHome(a).ConfigureAwait(false);
        if (a.MapId != b.MapId) await GoHome(b).ConfigureAwait(false);
        if (a.MapId != b.MapId) throw new SkipScenario("A et B ne sont pas sur la même carte (" + a.MapId + " / " + b.MapId + ")");
        Check(await a.Until(() => a.Map.Self != null && ActorOf(b, a) != null, 10).ConfigureAwait(false), "B ne voit pas A sur la carte " + a.MapId);
        MapActions actions = a.Account.Game.Interactions.MapActions;
        Check(actions.CanChangeDirection && !actions.IsTomb && !actions.IsGhost, "« Changer son orientation » indisponible pour A vivant hors combat");
        int seenBefore = ActorOf(b, a).Orientation;
        int direction = actions.AllowedDirections.Where(d => d != seenBefore && d != a.Map.Self.Orientation).DefaultIfEmpty(-1).First();
        Check(direction >= 0, "Aucune orientation différente de " + seenBefore + " parmi " + string.Join(",", actions.AllowedDirections));
        // La fenêtre de jeu de A se construit sur le fil d'interface après ASK : l'attendre, comme Capture.
        DateTime opened = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < opened && !await OnUi(() => window != null && window.IsHandleCreated && window.Visible && window.Map != null).ConfigureAwait(false))
            await Task.Delay(100).ConfigureAwait(false);
        int markA = a.ReceivedCount, markB = b.ReceivedCount, sentA = a.SentCount;
        string how;
        if (window?.Map != null)
        {
            string[] entries = await OnUi(() => ClickSelfMenu(MapActionTexts.ChangeDirection)).ConfigureAwait(false);
            DateTime limit = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < limit && !await OnUi(() => DirectionChooser.Current != null && DirectionChooser.Current.IsOpen).ConfigureAwait(false))
                await Task.Delay(50).ConfigureAwait(false);
            int[] arrows = await OnUi(() => DirectionChooser.Current != null && DirectionChooser.Current.IsOpen ? DirectionChooser.Current.Directions.ToArray() : null).ConfigureAwait(false);
            Check(arrows != null, "Le sélecteur d'orientation ne s'ouvre pas");
            Check(arrows.SequenceEqual(actions.AllowedDirections), "Flèches du sélecteur " + string.Join(",", arrows) + " au lieu de " + string.Join(",", actions.AllowedDirections));
            Task<string> chosen = await OnUi(() => DirectionChooser.Current.ChooseAsync(direction)).ConfigureAwait(false);
            string report = await chosen.ConfigureAwait(false);
            Check(!await OnUi(() => DirectionChooser.Current != null && DirectionChooser.Current.IsOpen).ConfigureAwait(false), "Le sélecteur reste ouvert après le choix");
            how = "menu « " + string.Join(" | ", entries) + " », sélecteur " + arrows.Length + " flèche(s) → « " + report + " »";
        }
        else
        {
            var sent = await actions.SetDirectionAsync(direction).ConfigureAwait(false);
            Check(sent.Sent, "Orientation refusée par le bot : " + sent.Message);
            how = "sans fenêtre : " + sent.Message;
        }
        Check(await a.Until(() => a.SentSince(sentA).Contains("eD" + direction), 5).ConfigureAwait(false), "eD" + direction + " non envoyé par A");
        string echoA = await a.Expect(p => p == "eD" + a.Id + "|" + direction, "eD" + a.Id + "|" + direction + " (A)", 10, markA).ConfigureAwait(false);
        string echoB = await b.Expect(p => p == "eD" + a.Id + "|" + direction, "eD" + a.Id + "|" + direction + " (B)", 10, markB).ConfigureAwait(false);
        Check(await b.Until(() => ActorOf(b, a)?.Orientation == direction && a.Map.Self?.Orientation == direction, 5).ConfigureAwait(false),
            "Orientation après l'écho : B voit " + ActorOf(b, a)?.Orientation + ", A " + a.Map.Self?.Orientation + ", attendu " + direction);
        UserMapControl.ActorVisualState drawn = window?.Map == null ? null : await StateOf(a.Id).ConfigureAwait(false);
        string result = how + " ; A envoie eD" + direction + " (" + MapActions.DirectionName(direction) + "), A reçoit " + echoA + ", B reçoit " + echoB
            + ", B voit A orienté " + seenBefore + " → " + ActorOf(b, a).Orientation + (drawn == null ? "" : ", fenêtre de A : " + drawn.AnimationName);

        // « Se frapper » (HIT_HIMSELF) : SLAP_SENTENCE du client dans le canal général, entendu par B.
        if (window?.Map == null || MapActionTexts.SlapSentence == null) return result + " ; Se frapper non joué (" + (window?.Map == null ? "pas de fenêtre" : "lang.xml absent") + ")";
        int slapMark = b.ReceivedCount, slapSent = a.SentCount;
        await OnUi(() => ClickSelfMenu(MapActionTexts.HitHimself)).ConfigureAwait(false);
        Check(await a.Until(() => a.SentSince(slapSent).Any(p => p.StartsWith("BM*|" + MapActionTexts.SlapSentence + "|", StringComparison.Ordinal)), 5).ConfigureAwait(false),
            "BM*|" + MapActionTexts.SlapSentence + "| non envoyé par « " + MapActionTexts.HitHimself + " »");
        string heard = await b.Expect(p => p.StartsWith("cMK|") && p.Contains("|" + a.Id + "|") && p.Contains(MapActionTexts.SlapSentence), "cMK| de « " + MapActionTexts.SlapSentence + " »", 10, slapMark).ConfigureAwait(false);
        return result + " ; « " + MapActionTexts.HitHimself + " » → BM*|" + MapActionTexts.SlapSentence + "|, B reçoit " + Short(heard, 60);
    }

    /// <summary>Ouvre le menu du personnage de A dans la fenêtre (fil d'interface), clique l'entrée <paramref name="text"/> et rend les entrées vues.</summary>
    private static string[] ClickSelfMenu(string text)
    {
        ContextMenuStrip menu = window.Map.Router.ShowActorMenu(new Tool_BotProtocol.Game.Maps.Interfaces.Entites[] { a.Map.Self }, (short)a.Map.Self.CellId, Keys.None, false);
        Check(menu != null, "Pas de menu sur le personnage de A");
        string[] texts = menu.Items.OfType<ToolStripItem>().Where(i => !(i is ToolStripSeparator)).Select(i => i.Text).ToArray();
        ToolStripItem item = menu.Items.OfType<ToolStripMenuItem>().FirstOrDefault(i => i.Text == text);
        Check(item != null && item.Enabled, "Menu de A sans « " + text + " » actif : " + string.Join(" | ", texts));
        item.PerformClick();
        menu.Close();
        return texts;
    }

    /// <summary>Compte et personnage inventés, écrits dans la base de StarLoco le temps d'un scénario puis supprimés.</summary>
    private sealed class InventedCharacter
    {
        public string Login, Password, Name;
        public long Guid = -1;
        public int Id = -1, Sex;
    }

    /// <summary>
    /// Écrit un compte inventé (mot de passe <c>SHA2(MD5)</c> du Login, pseudo non vide) et son personnage : mêmes colonnes que
    /// <c>PlayerData.add</c> de StarLoco, classe, sexe, couleurs, sorts et serveur repris du personnage <paramref name="sourceId"/>,
    /// sans objets, posé sur <paramref name="mapId"/> / <paramref name="cellId"/> avec l'énergie donnée.
    /// </summary>
    private static InventedCharacter Invent(MySql.Data.MySqlClient.MySqlConnection db, string schema, int sourceId, int mapId, int cellId, int energy, List<InventedCharacter> created)
    {
        var invented = new InventedCharacter
        {
            Login = "azurx2" + random.Next(10000, 99999).ToString(CultureInfo.InvariantCulture),
            Password = Guid.NewGuid().ToString("N").Substring(0, 12),
            Name = NewCharacterName()
        };
        created.Add(invented);
        invented.Sex = Convert.ToInt32(Scalar(db, "SELECT sexe FROM " + schema + ".players WHERE id = @source", "@source", sourceId), CultureInfo.InvariantCulture);
        Sql(db, "INSERT INTO " + schema + ".accounts (account, pass, pseudo, question, reponse, lastConnectionDate, lastIP, friends, enemy, heurevote, lastConnectDay)"
            + " VALUES (@login, SHA2(MD5(@password), 512), @pseudo, 'DELETE?', 'DELETE', '', '', '', '', 0, '')",
            "@login", invented.Login, "@password", invented.Password, "@pseudo", "X2" + invented.Login.Substring(6));
        invented.Guid = Convert.ToInt64(Scalar(db, "SELECT guid FROM " + schema + ".accounts WHERE account = @login", "@login", invented.Login), CultureInfo.InvariantCulture);
        invented.Id = Convert.ToInt32(Scalar(db, "SELECT IFNULL(MAX(id), 0) + 1 FROM " + schema + ".players"), CultureInfo.InvariantCulture);
        Sql(db, "INSERT INTO " + schema + ".players (id, name, sexe, class, color1, color2, color3, kamas, spellboost, capital, energy, level, xp, size, gfx, account, cell, map, spells, objets, storeObjets, morphMode, server)"
            + " SELECT @id, @name, sexe, class, color1, color2, color3, 0, spellboost, capital, @energy, level, xp, size, gfx, @account, @cell, @map, spells, '', '', '0', server FROM "
            + schema + ".players WHERE id = @source",
            "@id", invented.Id, "@name", invented.Name, "@energy", energy, "@account", invented.Guid, "@cell", cellId, "@map", mapId, "@source", sourceId);
        Journal("[test] compte inventé " + invented.Login + " (" + invented.Guid + "), personnage " + invented.Name + " (" + invented.Id + ") sur la carte " + mapId
            + " cellule " + cellId + ", énergie " + energy);
        return invented;
    }

    /// <summary>Ferme les sessions, puis supprime de la base les comptes et personnages inventés (StarLoco les enregistre à la déconnexion).</summary>
    private static async Task ForgetAsync(MySql.Data.MySqlClient.MySqlConnection db, string schema, IEnumerable<LiveSession> sessions, IEnumerable<InventedCharacter> created)
    {
        // Une déconnexion après l'autre (voir SeparateAsync).
        foreach (LiveSession s in sessions) { s?.Dispose(); await Task.Delay(500).ConfigureAwait(false); }
        await Task.Delay(1000).ConfigureAwait(false);
        foreach (InventedCharacter invented in created)
        {
            try
            {
                if (invented.Id > 0) Sql(db, "DELETE FROM " + schema + ".players WHERE id = @id AND account = @account", "@id", invented.Id, "@account", invented.Guid);
                if (invented.Guid > 0) Sql(db, "DELETE FROM " + schema + ".accounts WHERE guid = @guid AND account = @login", "@guid", invented.Guid, "@login", invented.Login);
                Journal("[test] compte " + invented.Login + " et personnage " + invented.Id + " supprimés de la base");
            }
            catch (Exception error) { Journal("!! [test] nettoyage de la base impossible pour " + invented.Login + " : " + error.Message); }
        }
    }

    /// <summary>Connexion MySQL de <c>AZUR_STARLOCO_GAME_DB</c> et base des comptes, ou scénario non joué.</summary>
    private static MySql.Data.MySqlClient.MySqlConnection OpenAccountsDatabase(out string schema)
    {
        RequireMaps();
        string database = Environment.GetEnvironmentVariable("AZUR_STARLOCO_GAME_DB");
        if (string.IsNullOrWhiteSpace(database)) throw new SkipScenario("AZUR_STARLOCO_GAME_DB non défini : pas de base où écrire les personnages du scénario");
        var db = new MySql.Data.MySqlClient.MySqlConnection(database);
        try
        {
            db.Open();
            schema = PlayersSchema(db, a.Id, a.CharacterName);
            if (schema == null) throw new SkipScenario("table players du personnage " + a.CharacterName + " introuvable sur ce serveur MySQL");
            return db;
        }
        catch { db.Dispose(); throw; }
    }

    /// <summary>
    /// Personnage d'un compte inventé, posé en tombe par la base (énergie -1 : <c>Player</c> appelle <c>setFuneral</c> au
    /// chargement, comme <c>Fight</c> après une défaite à 0 d'énergie) : sprite « -10 » (bit 128), <c>GF</c> → fantôme 8004 « -40 »
    /// au cimetière avec la liste des phénix (<c>IH</c>), marche sur la statue → <c>setAlive</c> (<c>AR6bk</c>, sprite vivant).
    /// Le compte et le personnage sont supprimés de la base à la fin.
    /// </summary>
    private static async Task<string> TombAsync()
    {
        if (!Map.AllBotMaps.ContainsKey(TombMap) || !Map.AllBotMaps.ContainsKey(CemeteryMap)) throw new SkipScenario("cartes " + TombMap + " et " + CemeteryMap + " absentes des cartes du bot");
        using (MySql.Data.MySqlClient.MySqlConnection db = OpenAccountsDatabase(out string schema))
        {
            var created = new List<InventedCharacter>();
            LiveSession c = null;
            try
            {
                InventedCharacter tomb = Invent(db, schema, a.Id, TombMap, TombCell, -1, created);
                c = new LiveSession("C", tomb.Login, tomb.Password);
                string entered = await EnterExistingAsync(c, tomb.Id, tomb.Name, TombMap).ConfigureAwait(false);
                GameSession session = c.Account.Game.Session;
                MapActions actions = c.Account.Game.Interactions.MapActions;
                // setFuneral : gfx classe + "3" ; setAlive : classe + sexe (Integer.parseInt de StarLoco).
                int tombGfx = a.ClassId * 10 + 3, aliveGfx = a.ClassId * 10 + tomb.Sex;
                Check(await c.Until(() => session.IsTomb && c.Map.Self != null, 10).ConfigureAwait(false), "Le bot ne lit pas la tombe dans son sprite : " + Restrictions(c));
                Check(!session.IsGhost && actions.CanFreeSoul && !actions.CanChangeDirection && c.Map.Self.Gfx == tombGfx,
                    "Tombe mal lue : fantôme " + session.IsGhost + ", GF " + actions.CanFreeSoul + ", orientation " + actions.CanChangeDirection + ", gfx " + c.Map.Self.Gfx + " au lieu de " + tombGfx);
                string tombState = "sprite " + Restrictions(c) + ", gfx " + c.Map.Self.Gfx;
                int sentBefore = c.SentCount;
                var turn = await actions.SetDirectionAsync(1).ConfigureAwait(false);
                Check(!turn.Sent && !c.SentSince(sentBefore).Any(p => p.StartsWith("eD", StringComparison.Ordinal)), "Une tombe a envoyé eD");

                int mark = c.ReceivedCount, sent = c.SentCount;
                var freed = await actions.FreeSoulAsync().ConfigureAwait(false);
                Check(freed.Sent, "GF refusé par le bot : " + freed.Message);
                Check(c.SentSince(sent).Contains("GF"), "GF non envoyé");
                string gdm = await c.Expect("GDM|" + CemeteryMap, "GDM|" + CemeteryMap + " (cimetière)", 20, mark).ConfigureAwait(false);
                string ih = await c.Expect(p => p.StartsWith("IH", StringComparison.Ordinal) && p.Length > 2, "IH (statues de phénix)", 10, mark).ConfigureAwait(false);
                Check(await c.Until(() => c.MapId == CemeteryMap && c.Map.Self != null && session.IsGhost && !session.IsTomb, 15).ConfigureAwait(false),
                    "Le bot n'est pas un fantôme au cimetière : carte " + c.MapId + ", " + Restrictions(c) + ", tombe " + session.IsTomb);
                Check(c.Map.Self.Gfx == GhostGfx && !actions.CanFreeSoul && actions.CanChangeDirection, "Fantôme mal lu : gfx " + c.Map.Self.Gfx + ", GF " + actions.CanFreeSoul);
                string ghost = "cellule " + c.Map.Self.CellId + ", sprite " + Restrictions(c) + ", gfx " + c.Map.Self.Gfx;
                var again = await actions.FreeSoulAsync().ConfigureAwait(false);
                Check(!again.Sent, "Un fantôme a renvoyé GF");

                Tool_BotProtocol.Game.Maps.Interactives.Interactives phoenix = c.Map.Interactives.Values.FirstOrDefault(i => PhoenixObjects.Contains((int)i.gfx));
                Check(phoenix?.Cell != null, "Aucune statue de phénix (objet 542 ou 1524) sur la carte " + c.MapId);
                int walk = c.ReceivedCount;
                var moved = await c.Account.Game.Manager.Mouvements.MoveToAsync(phoenix.Cell).ConfigureAwait(false);
                Check(moved == Tool_BotProtocol.Game.Managers.Mouvements.MoveResults.EXIT, "Marche vers la statue " + phoenix.Cell.CellID + " refusée : " + moved);
                string ar = await c.Expect("AR6bk", "AR6bk (résurrection)", 40, walk).ConfigureAwait(false);
                Check(await c.Until(() => !session.IsGhost && !session.IsTomb && c.Map.Self != null && c.Map.Self.Gfx == aliveGfx, 10).ConfigureAwait(false),
                    "Le bot n'est pas revenu à la vie : " + Restrictions(c) + ", gfx " + c.Map.Self?.Gfx + " au lieu de " + aliveGfx);
                Check(session.CanMoveInAllDirections && actions.AllowedDirections.Count == 8 && actions.CanChangeDirection, "AR6bk ne rend pas les huit orientations");
                return entered + " ; tombe : " + tombState + ", eD refusé ; GF → " + Short(gdm, 16) + ", " + Short(ih, 24) + ", fantôme " + ghost
                    + " ; marche vers la statue " + phoenix.gfx + " (cellule " + phoenix.Cell.CellID + ", arrêt cellule " + c.Map.Self.CellId + ") → " + ar
                    + ", sprite " + Restrictions(c) + ", gfx " + c.Map.Self.Gfx;
            }
            finally { await ForgetAsync(db, schema, new[] { c }, created).ConfigureAwait(false); }
        }
    }

    /// <summary>Carte de la chapelle d'Amakna : le prêtre (PNJ 163, question 613, réponse 518 « action 102 ») y marie l'homme de la
    /// cellule 282 et la femme de la cellule 297 (<c>Action</c> 102 et <c>World.priestRequest</c> de StarLoco).</summary>
    private const int ChapelMap = 2019, GroomCell = 282, BrideCell = 297, PriestTemplate = 163, PriestQuestion = 613, MarryAnswer = 518;

    /// <summary>
    /// Mariage devant le prêtre, avec deux comptes inventés posés par la base sur les cellules des époux (femme copiée de A, homme de B).
    /// La mariée choisit « Nous voulons nous marier » : StarLoco envoie à l'interrogé seul <c>GA;617;&lt;interrogé&gt;;&lt;interrogé&gt;,&lt;conjoint&gt;,-1</c>.
    /// Non → <c>GA619&lt;réf&gt;</c> (cMK « Non », <c>Im048</c>) ; nouvelle demande, Oui → <c>GA618&lt;réf&gt;</c>, le conjoint est interrogé à son
    /// tour, Oui → le prêtre déclare l'union. StarLoco n'envoie jamais <c>GA;618</c> ni <c>GA;619</c>.
    /// </summary>
    private static async Task<string> MarriageAsync()
    {
        if (!Map.AllBotMaps.ContainsKey(ChapelMap)) throw new SkipScenario("carte " + ChapelMap + " absente des cartes du bot");
        using (MySql.Data.MySqlClient.MySqlConnection db = OpenAccountsDatabase(out string schema))
        {
            var created = new List<InventedCharacter>();
            LiveSession bride = null, groom = null;
            try
            {
                InventedCharacter her = Invent(db, schema, a.Id, ChapelMap, BrideCell, 10000, created);
                InventedCharacter him = Invent(db, schema, b.Id, ChapelMap, GroomCell, 10000, created);
                if (her.Sex != 1 || him.Sex != 0) throw new SkipScenario("A doit être une femme et B un homme (priestRequest) : sexes " + her.Sex + " et " + him.Sex);
                bride = new LiveSession("E", her.Login, her.Password);
                groom = new LiveSession("F", him.Login, him.Password);
                string entered = await EnterExistingAsync(bride, her.Id, her.Name, ChapelMap).ConfigureAwait(false) + " | "
                    + await EnterExistingAsync(groom, him.Id, him.Name, ChapelMap).ConfigureAwait(false);
                Check(await bride.Until(() => bride.Map.GetActor(him.Id) != null && groom.Map.GetActor(her.Id) != null
                    && bride.Map.Self?.CellId == BrideCell && groom.Map.Self?.CellId == GroomCell, 10).ConfigureAwait(false),
                    "Les époux ne se voient pas sur leurs cellules : " + bride.Map.Self?.CellId + " / " + groom.Map.Self?.CellId);
                NpcActor priest = bride.Map.Npcs.FirstOrDefault(n => n.TemplateId == PriestTemplate);
                if (priest == null) throw new SkipScenario("pas de prêtre (PNJ " + PriestTemplate + ") sur la carte " + ChapelMap);
                MapActions herActions = bride.Account.Game.Interactions.MapActions, hisActions = groom.Account.Game.Interactions.MapActions;
                MarriageRequest heard = null;
                herActions.MarriageAsked += request => heard = request;

                // Première demande, refusée.
                string asked = await AskPriestAsync(bride, priest, her.Id, him.Id).ConfigureAwait(false);
                MarriageRequest first = herActions.PendingMarriage;
                Check(first != null && first.AskedId == her.Id && first.PartnerId == him.Id && first.Reference == her.Id && heard == first,
                    "Demande de mariage mal lue : " + (first == null ? "aucune" : first.AskedId + "/" + first.PartnerId + " réf. " + first.Reference));
                Check(hisActions.PendingMarriage == null, "Le conjoint a reçu une boîte alors que StarLoco n'interroge que la mariée");
                int refusedMark = groom.ReceivedCount, herSent = bride.SentCount;
                var no = await herActions.RefuseMarriageAsync().ConfigureAwait(false);
                Check(no.Sent && await bride.Until(() => bride.SentSince(herSent).Contains("GA619" + her.Id), 5).ConfigureAwait(false), "GA619" + her.Id + " non envoyé : " + no.Message);
                Check(herActions.PendingMarriage == null, "La demande reste ouverte après Non");
                string saidNo = await groom.Expect(p => p.StartsWith("cMK|" + her.Id + "|") && (p.EndsWith("|Non") || p.Contains("|Non|")), "cMK « Non » de la mariée", 10, refusedMark).ConfigureAwait(false);
                string im048 = await groom.Expect(p => p.StartsWith("Im048;"), "Im048 (mariage annulé)", 10, refusedMark).ConfigureAwait(false);

                // Seconde demande, acceptée des deux côtés.
                await AskPriestAsync(bride, priest, her.Id, him.Id).ConfigureAwait(false);
                int hisMark = groom.ReceivedCount;
                herSent = bride.SentCount;
                var yes = await herActions.AcceptMarriageAsync().ConfigureAwait(false);
                Check(yes.Sent && await bride.Until(() => bride.SentSince(herSent).Contains("GA618" + her.Id), 5).ConfigureAwait(false), "GA618" + her.Id + " non envoyé : " + yes.Message);
                string hisQuestion = await groom.Expect(p => p == "GA;617;" + him.Id + ";" + him.Id + "," + her.Id + ",-1", "GA;617 adressé au marié", 10, hisMark).ConfigureAwait(false);
                Check(await groom.Until(() => hisActions.PendingMarriage?.AskedId == him.Id && hisActions.PendingMarriage.PartnerId == her.Id, 5).ConfigureAwait(false), "Le marié n'a pas de demande en attente");
                int hisSent = groom.SentCount, herMark = bride.ReceivedCount;
                var hisYes = await hisActions.AcceptMarriageAsync().ConfigureAwait(false);
                Check(hisYes.Sent && await groom.Until(() => groom.SentSince(hisSent).Contains("GA618" + him.Id), 5).ConfigureAwait(false), "GA618" + him.Id + " non envoyé : " + hisYes.Message);
                string union = await bride.Expect(p => p.StartsWith("cMK|") && p.Contains("|-1|") && p.Contains(her.Name) && p.Contains(him.Name), "déclaration du prêtre", 10, herMark).ConfigureAwait(false);
                Check(herActions.PendingMarriage == null && hisActions.PendingMarriage == null, "Une demande de mariage reste ouverte après l'union");
                return entered + " ; DR" + PriestQuestion + "|" + MarryAnswer + " → " + asked + " (mariée seule) ; Non → GA619" + her.Id + ", " + Short(saidNo, 50) + ", " + Short(im048, 40)
                    + " ; Oui → GA618" + her.Id + ", marié : " + hisQuestion + ", Oui → GA618" + him.Id + ", " + Short(union, 90);
            }
            finally { await ForgetAsync(db, schema, new[] { bride, groom }, created).ConfigureAwait(false); }
        }
    }

    /// <summary>Dialogue de la mariée avec le prêtre : question 613, réponse 518 (action 102), puis <c>GA;617</c> qui lui est adressé.</summary>
    private static async Task<string> AskPriestAsync(LiveSession s, NpcActor priest, int askedId, int partnerId)
    {
        NpcDialog dialog = s.Account.Game.Interactions.Npc;
        if (dialog.IsOpen) { await dialog.LeaveAsync().ConfigureAwait(false); await s.Until(() => !dialog.IsOpen, 5).ConfigureAwait(false); }
        int mark = s.ReceivedCount;
        var open = await dialog.OpenAsync((int)priest.Id).ConfigureAwait(false);
        Check(open.Sent, "Dialogue avec le prêtre refusé par le bot : " + open.Message);
        await s.Expect("DCK", "DCK (dialogue du prêtre)", 10, mark).ConfigureAwait(false);
        await s.Expect("DQ" + PriestQuestion + "|", "DQ" + PriestQuestion, 10, mark).ConfigureAwait(false);
        Check(await s.Until(() => dialog.IsOpen && dialog.AnswerIds.Contains(MarryAnswer), 5).ConfigureAwait(false), "Réponse " + MarryAnswer + " absente : " + string.Join(",", dialog.AnswerIds));
        int answered = s.ReceivedCount;
        var answer = await dialog.AnswerAsync(MarryAnswer).ConfigureAwait(false);
        Check(answer.Sent, "Réponse refusée par le bot : " + answer.Message);
        string ga = await s.Expect(p => p == "GA;617;" + askedId + ";" + askedId + "," + partnerId + ",-1", "GA;617 adressé à la mariée", 10, answered).ConfigureAwait(false);
        Check(await s.Until(() => s.Account.Game.Interactions.MapActions.PendingMarriage != null, 5).ConfigureAwait(false), "Le bot n'a pas ouvert la demande de mariage");
        return ga;
    }

    /// <summary>Restrictions brutes et décodées du sprite de soi (champ 18 du GM), pour les messages.</summary>
    private static string Restrictions(LiveSession s)
    {
        var self = s.Map?.Self as PlayerActor;
        return self == null ? "(pas de sprite)" : "« " + self.RestrictionsRaw + " » (" + (self.RestrictionsValue?.ToString(CultureInfo.InvariantCulture) ?? "illisible") + ")";
    }

    /// <summary>
    /// Base qui porte les comptes et les personnages (<c>accounts</c> et <c>players</c>) : celle dont la table <c>players</c> contient
    /// le personnage de A, cherchée dans <c>information_schema</c> du serveur MySQL du jeu.
    /// </summary>
    private static string PlayersSchema(MySql.Data.MySqlClient.MySqlConnection db, int characterId, string characterName)
    {
        var schemas = new List<string>();
        using (var command = new MySql.Data.MySqlClient.MySqlCommand("SELECT p.TABLE_SCHEMA FROM information_schema.TABLES p JOIN information_schema.TABLES c"
            + " ON c.TABLE_SCHEMA = p.TABLE_SCHEMA AND c.TABLE_NAME = 'accounts' WHERE p.TABLE_NAME = 'players' ORDER BY p.TABLE_SCHEMA", db))
        using (var reader = command.ExecuteReader())
            while (reader.Read()) schemas.Add(reader.GetString(0));
        foreach (string schema in schemas)
        {
            string quoted = "`" + schema.Replace("`", "``") + "`";
            if (Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM " + quoted + ".players WHERE id = @id AND name = @name", "@id", characterId, "@name", characterName), CultureInfo.InvariantCulture) == 1)
                return quoted;
        }
        return null;
    }

    private static MySql.Data.MySqlClient.MySqlCommand Command(MySql.Data.MySqlClient.MySqlConnection db, string sql, object[] parameters)
    {
        var command = new MySql.Data.MySqlClient.MySqlCommand(sql, db);
        for (int i = 0; i + 1 < parameters.Length; i += 2) command.Parameters.AddWithValue((string)parameters[i], parameters[i + 1]);
        return command;
    }

    private static int Sql(MySql.Data.MySqlClient.MySqlConnection db, string sql, params object[] parameters)
    {
        using (var command = Command(db, sql, parameters)) return command.ExecuteNonQuery();
    }

    private static object Scalar(MySql.Data.MySqlClient.MySqlConnection db, string sql, params object[] parameters)
    {
        using (var command = Command(db, sql, parameters)) return command.ExecuteScalar();
    }

    /// <summary>
    /// Entrée en jeu d'un personnage existant, comme PersoSelection puis SelectPlayerPerso : AX, ticket, ALK, AS&lt;id&gt;.
    /// <paramref name="started"/> (facultatif) est appelé juste après <see cref="LiveSession.Start"/>, avant la connexion.
    /// </summary>
    private static async Task<string> EnterExistingAsync(LiveSession s, int characterId, string name, int mapId, Action<LiveSession> started = null)
    {
        s.Start();
        started?.Invoke(s);
        await s.Account.ConnectAsync().ConfigureAwait(false);
        await s.Expect("HC", "clé HC du Login", 15, 0).ConfigureAwait(false);
        await s.Expect("AH", "liste des serveurs AH", 15, 0).ConfigureAwait(false);
        string axk = await s.Expect("AxK", "AxK (serveurs du compte)", 15, 0).ConfigureAwait(false);
        int server = serverId > 0 ? serverId : s.Account.Game.Server.Servers.Where(x => x.Value == ServerStates.ONLINE).Select(x => x.Key).DefaultIfEmpty(-1).Min();
        Check(server > 0, "Aucun serveur en ligne pour le compte " + s.Label);
        await s.Send("AX" + server).ConfigureAwait(false);
        string redirect = await s.Expect(p => p.StartsWith("AYK") || p.StartsWith("AXK") || p.StartsWith("AXE"), "redirection AYK", 15, 0).ConfigureAwait(false);
        Check(!redirect.StartsWith("AXE"), "Sélection du serveur refusée : " + redirect);
        await s.Expect("ATK", "ATK (ticket accepté)", 15, 0).ConfigureAwait(false);
        string alk = await s.Expect("ALK", "ALK (liste des personnages)", 15, 0).ConfigureAwait(false);
        Check(await s.Until(() => s.Account.AccountCharactersInfo.ContainsKey(characterId), 5).ConfigureAwait(false), "ALK sans le personnage écrit dans la base : " + Short(alk, 80));
        int selected = s.ReceivedCount;
        await s.Send("AS" + characterId).ConfigureAwait(false);
        string ask = await s.Expect(p => p.StartsWith("ASK") || p.StartsWith("ASE"), "ASK (personnage sélectionné)", 15, selected).ConfigureAwait(false);
        Check(ask.StartsWith("ASK|" + characterId + "|" + name + "|"), "Sélection refusée : " + ask);
        string gdm = await s.Expect("GDM|" + mapId, "GDM|" + mapId, 15, selected).ConfigureAwait(false);
        await s.Expect(p => p.StartsWith("GM|") && p.Contains(";" + characterId + ";" + name + ";"), "GM du personnage", 15, selected).ConfigureAwait(false);
        Check(await s.Until(() => s.MapId == mapId && s.Map.Self != null && s.Map.Self.CellId >= 0, 10).ConfigureAwait(false), "Le bot n'a pas placé " + name + " sur la carte " + mapId);
        return s.Label + " " + name + " : " + Short(axk, 20) + ", " + Short(ask, 40) + ", " + Short(gdm, 16) + ", GM cellule " + s.Map.Self.CellId;
    }
}

// Scénarios x3a à x3c (lot X3) : pseudo d'un compte neuf, nouveau niveau, coupure du serveur de jeu en pleine partie.
internal static partial class Live
{
    /// <summary>Parchemin Doré de StarLoco : effet 605 (expérience), action d'objet de type 3 (<c>ObjectAction</c> : <c>player.addXp</c>).</summary>
    private const int XpScrollTemplate = 680;
    /// <summary>
    /// Effet du parchemin écrit dans la base : jet « 0d0+111 », soit 111 points d'expérience à coup sûr (110 mènent au niveau 2,
    /// table <c>experience</c>), au lieu du « 1d9999+1 » du modèle qui ferait sauter plusieurs niveaux. Champ 3 à 0 : StarLoco lit
    /// sinon l'effet comme un texte (<c>GameObject.parseStringToStats</c>) et le parchemin ne donnerait rien.
    /// </summary>
    private const string XpScrollStats = "25d#6f#0#0#0d0+111";

    /// <summary>Compte inventé SANS pseudo (colonne vide, comme un compte créé hors du jeu) : le Login répond <c>AlEr</c> à <c>Af</c>.</summary>
    private static InventedCharacter InventAccount(MySql.Data.MySqlClient.MySqlConnection db, string schema, List<InventedCharacter> created)
    {
        var invented = new InventedCharacter
        {
            Login = "azurx3" + random.Next(10000, 99999).ToString(CultureInfo.InvariantCulture),
            Password = Guid.NewGuid().ToString("N").Substring(0, 12)
        };
        created.Add(invented);
        Sql(db, "INSERT INTO " + schema + ".accounts (account, pass, pseudo, question, reponse, lastConnectionDate, lastIP, friends, enemy, heurevote, lastConnectDay)"
            + " VALUES (@login, SHA2(MD5(@password), 512), '', 'DELETE?', 'DELETE', '', '', '', '', 0, '')",
            "@login", invented.Login, "@password", invented.Password);
        invented.Guid = Convert.ToInt64(Scalar(db, "SELECT guid FROM " + schema + ".accounts WHERE account = @login", "@login", invented.Login), CultureInfo.InvariantCulture);
        Journal("[test] compte inventé sans pseudo " + invented.Login + " (" + invented.Guid + ")");
        return invented;
    }

    /// <summary>Pseudo absent de la base, fait de lettres et de chiffres (StarLoco refuse espaces, tirets, accents et ponctuation).</summary>
    private static string FreeNickname(MySql.Data.MySqlClient.MySqlConnection db, string schema, string prefix)
    {
        for (int i = 0; i < 20; i++)
        {
            string name = prefix + random.Next(10000, 99999).ToString(CultureInfo.InvariantCulture);
            if (Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM " + schema + ".accounts WHERE pseudo = @pseudo", "@pseudo", name), CultureInfo.InvariantCulture) == 0) return name;
        }
        throw new Exception("aucun pseudo libre trouvé dans la base");
    }

    private static string StoredNickname(MySql.Data.MySqlClient.MySqlConnection db, string schema, string login) =>
        Scalar(db, "SELECT pseudo FROM " + schema + ".accounts WHERE account = @login", "@login", login) as string;

    private static int Count<T>(List<T> list) { lock (list) return list.Count; }

    private static T Last<T>(List<T> list) { lock (list) return list.Count == 0 ? default(T) : list[list.Count - 1]; }

    private static object Field(object target, string name) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

    private static object Member(object target, string name) =>
        target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(target, null);

    private static IEnumerable<Control> AllControls(Control root)
    {
        yield return root;
        foreach (Control child in root.Controls) foreach (Control nested in AllControls(child)) yield return nested;
    }

    /// <summary>Boîte du bot (<see cref="BotDialogs"/>) ouverte sous ce nom, ou <c>null</c> (fil d'interface).</summary>
    private static Form OpenDialog(string name) => BotDialogs.OpenDialogs.FirstOrDefault(x => x.Name == name && !x.IsDisposed && x.Visible);

    private static string DialogMessage(Form dialog) => AllControls(dialog).OfType<Label>().First(x => x.Name == "dialog-message").Text;

    private static async Task<T> WaitOnUi<T>(Func<T> probe, int seconds) where T : class
    {
        DateTime until = DateTime.UtcNow.AddSeconds(seconds);
        T value;
        while ((value = await OnUi(probe).ConfigureAwait(false)) == null && DateTime.UtcNow < until) await Task.Delay(100).ConfigureAwait(false);
        return value;
    }

    private static string Describe(NicknameRequest request) =>
        request == null ? "(aucune)" : "n° " + request.Attempt + ", refusé « " + request.Refused + " », erreur « " + request.Error + " »";

    /// <summary>
    /// Compte neuf sans pseudo, deux fois. D'abord par le bot seul (<see cref="NicknameChoice"/>) : <c>AlEr</c> répond à <c>Af</c>, rien
    /// d'autre ne part pendant l'attente (le Login lirait n'importe quelle ligne comme le pseudo) ; le nom du compte est refusé sur place
    /// (<c>NICKNAME_EQUALS_LOGIN</c>) ; le pseudo de A part et revient <c>AlEs</c> (pris) ; un pseudo libre part et la connexion reprend
    /// (<c>Ad&lt;pseudo&gt;</c>, <c>AlK</c>, puis <c>AxK</c> après <c>Ax</c>) ; StarLoco l'a écrit dans la base. Puis un deuxième compte
    /// passe par l'écran de choix du serveur (<see cref="PersoSelection"/>) : la fenêtre <see cref="ChooseNickNameForm"/> s'ouvre sur
    /// <c>AlEr</c>, refuse le nom du compte sous la saisie, « OK » mène à la confirmation <c>DO_CHOOSE_NICKNAME</c>, le second « OK »
    /// envoie le pseudo ; la fenêtre se ferme et la liste des serveurs s'affiche. Les deux comptes sont supprimés de la base à la fin.
    /// </summary>
    private static async Task<string> NewAccountNicknameAsync()
    {
        using (MySql.Data.MySqlClient.MySqlConnection db = OpenAccountsDatabase(out string schema))
        {
            var created = new List<InventedCharacter>();
            LiveSession n = null;
            PersoSelection screen = null;
            try
            {
                string taken = StoredNickname(db, schema, a.Login);
                Check(!string.IsNullOrEmpty(taken), "Le compte de A n'a pas de pseudo dans la base");

                // 1. Le bot seul.
                InventedCharacter fresh = InventAccount(db, schema, created);
                string chosen = FreeNickname(db, schema, "Neuf");
                n = new LiveSession("N", fresh.Login, fresh.Password);
                n.Start();
                NicknameChoice choice = n.Account.Nickname;
                var requests = new List<NicknameRequest>();
                var accepted = new List<string>();
                choice.Requested += request => { lock (requests) requests.Add(request); };
                choice.Accepted += name => { lock (accepted) accepted.Add(name); };
                await n.Account.ConnectAsync().ConfigureAwait(false);
                await n.Expect("HC", "clé HC du Login", 15, 0).ConfigureAwait(false);
                await n.Expect(p => p == "AlEr", "AlEr (compte sans pseudo)", 15, 0).ConfigureAwait(false);
                Check(await n.Until(() => choice.IsPending && Count(requests) == 1, 5).ConfigureAwait(false), "AlEr n'a pas ouvert de demande de pseudo");
                Check(requests[0].Attempt == 1 && requests[0].Refused == null && requests[0].Error == null, "Première demande mal lue : " + Describe(requests[0]));
                await Task.Delay(300).ConfigureAwait(false);
                string[] beforeChoice = n.SentSince(0);
                Check(beforeChoice.LastOrDefault() == "Af" && n.Account.Connexion != null && n.Account.Connexion.IsConnected(),
                    "Après AlEr, le bot a envoyé « " + Short(beforeChoice.LastOrDefault(), 20) + " » ou s'est déconnecté (le Login lirait la ligne comme le pseudo)");

                int sentBefore = n.SentCount;
                var sameAsLogin = await choice.ChooseAsync(fresh.Login.ToUpperInvariant()).ConfigureAwait(false);
                var tooShort = await choice.ChooseAsync("ab").ConfigureAwait(false);
                Check(!sameAsLogin.Sent && sameAsLogin.Message == ConnectionTexts.NicknameEqualsLogin && !tooShort.Sent && n.SentCount == sentBefore,
                    "Refus locaux : nom du compte « " + sameAsLogin.Message + " », trop court « " + tooShort.Message + " », " + (n.SentCount - sentBefore) + " paquet(s) envoyé(s)");

                int mark = n.ReceivedCount;
                // Deuxième proposition faite aussitôt, avant d'attendre la fin du premier envoi : le Login local répond en quelques
                // millisecondes, et un AlEs déjà reçu autoriserait légitimement la proposition suivante.
                var sending = choice.ChooseAsync(taken);
                var twice = await choice.ChooseAsync(chosen).ConfigureAwait(false);
                var first = await sending.ConfigureAwait(false);
                Check(first.Sent && n.SentSince(sentBefore).SequenceEqual(new[] { taken }), "Le pseudo pris n'est pas parti seul, en ligne brute : " + first.Message);
                Check(!twice.Sent && n.SentCount == sentBefore + 1, "Un deuxième pseudo est parti avant la réponse du Login : " + twice.Message);
                string alEs = await n.Expect(p => p == "AlEs" || p.StartsWith("Ad", StringComparison.Ordinal), "AlEs (pseudo pris)", 15, mark).ConfigureAwait(false);
                Check(alEs == "AlEs", "Le Login a accepté le pseudo déjà pris « " + taken + " » : " + alEs);
                Check(await n.Until(() => Count(requests) == 2, 5).ConfigureAwait(false) && Last(requests).Attempt == 2 && Last(requests).Refused == taken
                    && Last(requests).Error == ConnectionTexts.NicknameAlreadyUsed && choice.IsPending, "AlEs mal lu : " + Describe(Last(requests)));

                mark = n.ReceivedCount;
                int sentChosen = n.SentCount;
                var second = await choice.ChooseAsync(chosen).ConfigureAwait(false);
                Check(second.Sent && n.SentSince(sentChosen).FirstOrDefault() == chosen, "Pseudo libre non envoyé : " + second.Message);
                string ad = await n.Expect(p => p.StartsWith("Ad", StringComparison.Ordinal) || p.StartsWith("AlE", StringComparison.Ordinal), "Ad<pseudo> (pseudo accepté)", 15, mark).ConfigureAwait(false);
                Check(ad == "Ad" + chosen, "Pseudo libre refusé ou mal repris : " + ad);
                string alk = await n.Expect("AlK", "AlK (connexion acceptée)", 15, mark).ConfigureAwait(false);
                string axk = await n.Expect("AxK", "AxK (serveurs du compte, après Ax)", 15, mark).ConfigureAwait(false);
                Check(await n.Until(() => Count(accepted) >= 1 && !choice.IsPending, 5).ConfigureAwait(false) && accepted[0] == chosen && n.Account.Name == chosen,
                    "Reprise mal lue : accepté « " + Last(accepted) + " », nom du compte « " + n.Account.Name + " », demande en cours " + choice.IsPending);
                string stored = StoredNickname(db, schema, fresh.Login);
                Check(stored == chosen || string.IsNullOrEmpty(stored), "StarLoco a écrit un autre pseudo dans la base : « " + stored + " »");
                string botPart = "N " + fresh.Login + " : AlEr, nom du compte et « ab » refusés sur place, « " + taken + " » → AlEs (" + Last(requests).Error + "), deuxième envoi refusé avant la réponse, « "
                    + chosen + " » → " + ad + ", " + alk + ", " + Short(axk, 16) + ", " + Persisted(stored);
                n.Dispose();

                // 2. Par la fenêtre du bot.
                InventedCharacter other = InventAccount(db, schema, created);
                string typed = FreeNickname(db, schema, "Fenetre");
                screen = await OnUi(() =>
                {
                    var form = new PersoSelection(new AccountConfig(other.Login, other.Password, "Live"), true)
                    { StartPosition = FormStartPosition.Manual, Location = new Point(40, 40), ShowInTaskbar = false };
                    form.Show();
                    return form;
                }).ConfigureAwait(false);
                var uiAccount = (Accounts)Field(screen, "A");
                uiAccount.Logger.log_event += (entry, color) => Journal("U [journal du bot] " + entry.message);
                PropertyInfo nicknameForm = typeof(PersoSelection).GetProperty("NicknameForm", BindingFlags.Instance | BindingFlags.NonPublic);
                var dialog = await WaitOnUi(() => nicknameForm.GetValue(screen, null) as ChooseNickNameForm, 20).ConfigureAwait(false);
                Check(dialog != null, "AlEr n'a pas ouvert la fenêtre du choix du pseudo au-dessus de l'écran des serveurs (" + uiAccount.ConnectionStatus + ")");
                var uiReceived = new List<string>();
                var uiConnection = uiAccount.Connexion;
                Check(uiConnection != null, "La connexion du compte de la fenêtre est fermée");
                uiConnection.packetReceivedEvent += p => { lock (uiReceived) uiReceived.Add(p); Journal("U << " + BotPacketRedactor.Redact(p, uiAccount)); };
                uiConnection.packetSendEvent += p => Journal("U >> " + BotPacketRedactor.Redact(p, uiAccount));
                string[] opened = await OnUi(() => new[] { Member(dialog, "Step").ToString(), (string)Member(dialog, "HelpText"), ReferenceEquals(dialog.Owner, screen).ToString(), dialog.Visible.ToString() }).ConfigureAwait(false);
                Check(opened[0] == "Input" && opened[1] == ConnectionTexts.NicknameHelp && opened[2] == "True" && opened[3] == "True",
                    "Fenêtre du pseudo mal ouverte : étape " + opened[0] + ", aide « " + opened[1] + " », au-dessus de l'écran " + opened[2]);

                await OnUi(() => dialog.GetType().GetProperty("Nickname", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(dialog, other.Login, null)).ConfigureAwait(false);
                await (await OnUi(() => (Task)dialog.GetType().GetMethod("OkAsync", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(dialog, null)).ConfigureAwait(false)).ConfigureAwait(false);
                string[] refusedLocally = await OnUi(() => new[] { Member(dialog, "Step").ToString(), (string)Member(dialog, "ErrorText") }).ConfigureAwait(false);
                Check(refusedLocally[0] == "Input" && refusedLocally[1] == ConnectionTexts.NicknameEqualsLogin, "Nom du compte saisi : étape " + refusedLocally[0] + ", erreur « " + refusedLocally[1] + " »");

                await OnUi(() => dialog.GetType().GetProperty("Nickname", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(dialog, typed, null)).ConfigureAwait(false);
                await (await OnUi(() => (Task)dialog.GetType().GetMethod("OkAsync", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(dialog, null)).ConfigureAwait(false)).ConfigureAwait(false);
                string[] confirm = await OnUi(() => new[] { Member(dialog, "Step").ToString(), (string)Member(dialog, "HelpText") }).ConfigureAwait(false);
                Check(confirm[0] == "Confirm" && confirm[1] == ConnectionTexts.NicknameConfirm(typed), "Confirmation : étape " + confirm[0] + ", texte « " + confirm[1] + " »");
                await (await OnUi(() => (Task)dialog.GetType().GetMethod("OkAsync", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(dialog, null)).ConfigureAwait(false)).ConfigureAwait(false);
                Check(await n.Until(() => { lock (uiReceived) return uiReceived.Contains("Ad" + typed) && uiReceived.Any(p => p.StartsWith("AlK", StringComparison.Ordinal)); }, 15).ConfigureAwait(false),
                    "Le Login n'a pas repris la connexion après le pseudo de la fenêtre : " + string.Join(" ", Snapshot(uiReceived).Select(p => Short(p, 12))));
                Check(await WaitOnUi(() => nicknameForm.GetValue(screen, null) == null && dialog.IsDisposed && screen.Alreadyhere ? "ok" : null, 15).ConfigureAwait(false) != null,
                    "Après Ad/AlK, la fenêtre du pseudo reste ouverte ou la liste des serveurs est vide");
                string storedUi = StoredNickname(db, schema, other.Login);
                Check(storedUi == typed || string.IsNullOrEmpty(storedUi), "StarLoco a écrit un autre pseudo dans la base : « " + storedUi + " »");
                string status = await OnUi(() => ((Label)Field(screen, "status")).Text).ConfigureAwait(false);
                return botPart + " | fenêtre " + other.Login + " : ChooseNickName ouverte sur AlEr, nom du compte refusé sous la saisie, confirmation « " + confirm[1].Replace("\n", " ") + " », OK → Ad" + typed
                    + ", AlK, fenêtre fermée, serveurs affichés (« " + status + " »), " + Persisted(storedUi);
            }
            finally
            {
                if (screen != null) await OnUi(() => { if (!screen.IsDisposed) screen.Close(); }).ConfigureAwait(false);
                await ForgetAsync(db, schema, new[] { n }, created).ConfigureAwait(false);
            }
        }
    }

    private static string[] Snapshot(List<string> list) { lock (list) return list.ToArray(); }

    /// <summary>
    /// Pseudo relu dans la base après sa reprise par le Login. Vide : ChooseNickName.verify l'a gardé en mémoire mais
    /// AccountData.update a échoué (le StarLoco de test sur MySQL 8 : Connector/J 5.1 rétablit STRICT_TRANS_TABLES et
    /// « banned = 'false' » est refusé) ; le Login redemandera alors le pseudo à la connexion suivante.
    /// </summary>
    private static string Persisted(string stored) =>
        string.IsNullOrEmpty(stored) ? "pseudo NON écrit dans la base par ce StarLoco (AccountData.update refusé)" : "base « " + stored + " »";

    /// <summary>
    /// Nouveau niveau en jeu : un compte et un personnage inventés (niveau 1, 0 point d'expérience) reçoivent par la base un parchemin
    /// d'expérience (<see cref="XpScrollTemplate"/>, 111 points) et ont leur fenêtre de jeu, ouverte à la sélection comme
    /// <c>SelectPlayerPerso</c>. « Utiliser » (<c>OU</c>) → <c>AN2</c> (<c>Player.addXp</c>), <c>Im08;111</c> ; la fenêtre ouvre la boîte
    /// <c>INFORMATIONS</c> / <c>NEW_LEVEL</c> et la discussion écrit « Nouveau niveau : 2. ». Tout est supprimé de la base à la fin.
    /// </summary>
    private static async Task<string> LevelUpAsync()
    {
        if (!Map.AllBotMaps.ContainsKey(TombMap)) throw new SkipScenario("carte " + TombMap + " absente des cartes du bot");
        using (MySql.Data.MySqlClient.MySqlConnection db = OpenAccountsDatabase(out string schema))
        {
            var created = new List<InventedCharacter>();
            LiveSession c = null;
            GameClientFullform screen = null;
            int scrollId = 1900000000 + random.Next(0, 99999999);
            bool scrollWritten = false;
            try
            {
                InventedCharacter hero = Invent(db, schema, a.Id, TombMap, TombCell, 10000, created);
                Sql(db, "INSERT INTO " + schema + ".`world.entity.objects` (id, template, quantity, position, stats, puit) VALUES (@id, @template, 1, -1, @stats, 0)",
                    "@id", scrollId, "@template", XpScrollTemplate, "@stats", XpScrollStats);
                scrollWritten = true;
                Sql(db, "UPDATE " + schema + ".players SET level = 1, xp = 0, objets = @objets WHERE id = @id AND account = @account",
                    "@objets", scrollId.ToString(CultureInfo.InvariantCulture) + "|", "@id", hero.Id, "@account", hero.Guid);
                Journal("[test] parchemin " + XpScrollTemplate + " (objet " + scrollId + ", " + XpScrollStats + ") dans le sac de " + hero.Name + ", niveau 1, 0 point d'expérience");
                c = new LiveSession("C", hero.Login, hero.Password);
                var levels = new List<int>();
                string entered = await EnterExistingAsync(c, hero.Id, hero.Name, TombMap, s =>
                {
                    s.Account.Game.Session.LevelUp += level => { lock (levels) levels.Add(level); };
                    s.Account.Game.character.Player_Selection += () => OnUi(() =>
                    {
                        if (screen != null) return;
                        screen = new GameClientFullform(s.Account) { StartPosition = FormStartPosition.Manual, Location = new Point(60, 60), Size = new Size(1000, 700), ShowInTaskbar = false };
                        screen.Show();
                    });
                }).ConfigureAwait(false);
                Tool_BotProtocol.Game.Perso.Inventory.InventoryObjects scroll = null;
                Check(await c.Until(() => (scroll = Owned(c, XpScrollTemplate)) != null, 10).ConfigureAwait(false), "Le parchemin " + XpScrollTemplate + " manque à l'inventaire lu par le bot (ASK)");
                Check(scroll.Inventory_ID == scrollId, "Parchemin lu avec l'identifiant " + scroll.Inventory_ID + " au lieu de " + scrollId);
                Check(await WaitOnUi(() => screen != null && screen.IsHandleCreated && screen.Visible ? screen : null, 20).ConfigureAwait(false) != null, "La fenêtre de jeu de C ne s'est pas ouverte");
                string levelName = "nouveau-niveau/" + hero.Login;
                Check(await OnUi(() => OpenDialog(levelName) == null).ConfigureAwait(false), "Une boîte « Nouveau niveau » est ouverte avant le parchemin");

                int mark = c.ReceivedCount, sent = c.SentCount;
                var used = await Tool_BotProtocol.Game.Perso.Inventory.SpecialItems.UseAsync(c.Account.Game, scroll).ConfigureAwait(false);
                Check(used.Sent, "« Utiliser » refusé par le bot : " + used.Message);
                Check(await c.Until(() => c.SentSince(sent).Any(p => p.StartsWith("OU" + scrollId + "|", StringComparison.Ordinal)), 5).ConfigureAwait(false), "OU" + scrollId + "| non envoyé");
                string an = await c.Expect(p => p.StartsWith("AN", StringComparison.Ordinal), "AN (nouveau niveau)", 15, mark).ConfigureAwait(false);
                Check(an == "AN2", "Niveau annoncé " + an + " au lieu de AN2");
                string im = await c.Expect(p => p.StartsWith("Im08;", StringComparison.Ordinal), "Im08 (expérience gagnée)", 10, mark).ConfigureAwait(false);
                // StarLoco envoie AN2 deux fois pour ce gain (Player.addXp) : le bot ne lève LevelUp et n'ouvre la boîte qu'une fois.
                await Task.Delay(500).ConfigureAwait(false);
                int announced = c.ReceivedSince(mark).Count(p => p == "AN2");
                Check(Count(levels) == 1 && levels[0] == 2, "LevelUp levé " + Count(levels) + " fois pour " + announced + " AN2");
                Form box = await WaitOnUi(() => OpenDialog(levelName), 10).ConfigureAwait(false);
                Check(box != null, "AN2 n'a pas ouvert la boîte « Nouveau niveau » dans la fenêtre de jeu de C");
                string[] shown = await OnUi(() => new[] { box.Text, DialogMessage(box), ReferenceEquals(box.Owner, screen).ToString() }).ConfigureAwait(false);
                Check(shown[0] == ConnectionTexts.InformationsTitle && shown[1] == ConnectionTexts.NewLevel(2) && shown[2] == "True",
                    "Boîte « " + shown[0] + " » : « " + shown[1] + " », au-dessus de la fenêtre de C " + shown[2]);
                int boxes = await OnUi(() => BotDialogs.OpenDialogs.Count(x => x.Name == levelName && !x.IsDisposed && x.Visible)).ConfigureAwait(false);
                int lines = c.Account.Game.Chat.Messages.Count(m => m.Text == ConnectionTexts.NewLevelLine(2));
                Check(boxes == 1 && lines == 1, boxes + " boîte(s) « Nouveau niveau » et " + lines + " ligne(s) « " + ConnectionTexts.NewLevelLine(2) + " » pour " + announced + " AN2");
                bool consumed = await c.Until(() => Owned(c, XpScrollTemplate) == null, 5).ConfigureAwait(false);
                await OnUi(() => AllControls(box).OfType<Button>().First(x => x.Visible).PerformClick()).ConfigureAwait(false);
                Check(await WaitOnUi(() => OpenDialog(levelName) == null ? "fermée" : null, 5).ConfigureAwait(false) != null, "« OK » ne ferme pas la boîte « Nouveau niveau »");
                return entered + " ; " + Short(c.SentSince(sent).First(p => p.StartsWith("OU", StringComparison.Ordinal)), 20) + " → " + announced + " × " + an + ", " + Short(im, 16)
                    + ", boîte « " + shown[0] + " » : « " + shown[1].Replace("\n", " ") + " », discussion « " + ConnectionTexts.NewLevelLine(2) + " », parchemin "
                    + (consumed ? "retiré du sac" : "encore dans le sac") + ", OK ferme la boîte";
            }
            finally
            {
                if (screen != null) await OnUi(() => { if (!screen.IsDisposed) { screen.Close(); screen.Dispose(); } }).ConfigureAwait(false);
                await ForgetAsync(db, schema, new[] { c }, created).ConfigureAwait(false);
                if (scrollWritten)
                    try { Sql(db, "DELETE FROM " + schema + ".`world.entity.objects` WHERE id = @id AND template = @template", "@id", scrollId, "@template", XpScrollTemplate); }
                    catch (Exception error) { Journal("!! [test] parchemin " + scrollId + " non supprimé de la base : " + error.Message); }
            }
        }
    }

    /// <summary>Lance une commande de la machine (sh -c, cmd /c sous Windows) ; sa sortie va au journal. Code de sortie, -1 après le délai.</summary>
    /// <summary>
    /// Sous Mono, SIGQUIT au processus du test : le runtime écrit les piles de tous les fils dans la sortie standard, sans s'arrêter.
    /// Rien sous Windows (.NET Framework n'a pas d'équivalent).
    /// </summary>
    private static void DumpThreads(string reason)
    {
        if (Type.GetType("Mono.Runtime") == null) return;
        Journal("!! [test] piles des fils (SIGQUIT, sortie du test) : " + reason);
        try
        {
            using (var kill = Process.Start(new ProcessStartInfo("kill", "-QUIT " + Process.GetCurrentProcess().Id) { UseShellExecute = false, CreateNoWindow = true }))
                kill?.WaitForExit(5000);
            Thread.Sleep(2000);
        }
        catch (Exception error) { Journal("!! [test] SIGQUIT impossible : " + error.Message); }
    }

    private static async Task<int> RunCommandAsync(string label, string command, int seconds)
    {
        bool windows = Environment.OSVersion.Platform == PlatformID.Win32NT;
        var start = new ProcessStartInfo(windows ? "cmd.exe" : "/bin/sh", windows ? "/c " + command : "-c \"" + command.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"")
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        using (var process = new Process { StartInfo = start })
        {
            process.OutputDataReceived += (s, e) => { if (e.Data != null) Journal("[" + label + "] " + e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) Journal("[" + label + "] " + e.Data); };
            Journal("[test] " + label + " : " + command);
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            DateTime until = DateTime.UtcNow.AddSeconds(seconds);
            while (!process.HasExited && DateTime.UtcNow < until) await Task.Delay(200).ConfigureAwait(false);
            if (!process.HasExited) { try { process.Kill(); } catch (Exception) { } Journal("!! [test] " + label + " : délai de " + seconds + " s dépassé"); return -1; }
            Journal("[test] " + label + " : code " + process.ExitCode);
            return process.ExitCode;
        }
    }

    /// <summary>
    /// Coupure du serveur de jeu en pleine partie (toujours le dernier scénario) : <c>AZUR_STARLOCO_GAME_STOP</c> arrête le Game par son
    /// crochet d'arrêt (<c>Main</c> : sauvegarde du monde, puis <c>kickAll</c> écrit <c>M04</c> à chaque joueur et ferme sa connexion
    /// aussitôt). A et B constatent une perte de connexion (<see cref="DisconnectCause.Lost"/>, en jeu, <c>M04</c> gardé s'il est arrivé) ;
    /// la fenêtre de A ouvre la boîte <c>CONNECTION</c> (<c>DISCONNECT</c>, <c>SRV_MSG_4</c> s'il est arrivé, puis <c>ATTEMPT_RECONNECT</c>
    /// en Oui / Non). <c>AZUR_STARLOCO_GAME_START</c> relance le Game (même après un échec) ; une fois le Game de nouveau enregistré
    /// auprès du Login, « Oui » ferme la fenêtre et reconnecte le compte de A jusqu'à la liste des serveurs, comme <c>Login.autoLogin</c>.
    /// </summary>
    private static async Task<string> GameStopAsync()
    {
        string stop = Environment.GetEnvironmentVariable("AZUR_STARLOCO_GAME_STOP"), restart = Environment.GetEnvironmentVariable("AZUR_STARLOCO_GAME_START");
        if (string.IsNullOrWhiteSpace(stop) || string.IsNullOrWhiteSpace(restart))
            throw new SkipScenario("AZUR_STARLOCO_GAME_STOP et AZUR_STARLOCO_GAME_START non définis : le serveur de jeu n'est pas arrêté");
        Check(a.Account.Connexion != null && a.Account.Connexion.IsConnected() && a.Account.Game.character.id > 0, "A n'est plus en jeu");
        Check(b.Account.Connexion != null && b.Account.Connexion.IsConnected(), "B n'est plus en jeu");
        Check(await WaitOnUi(() => window != null && !window.IsDisposed && window.IsHandleCreated && window.Visible ? window : null, 20).ConfigureAwait(false) != null, "Pas de fenêtre de jeu pour A");
        string lostName = "connexion-perdue/" + a.Login;
        Check(await OnUi(() => OpenDialog(lostName) == null).ConfigureAwait(false), "Une boîte de perte de connexion est ouverte avant l'arrêt");
        var lostA = new TaskCompletionSource<ConnectionLoss>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lostB = new TaskCompletionSource<ConnectionLoss>(TaskCreationOptions.RunContinuationsAsynchronously);
        Action<ConnectionLoss> onA = x => lostA.TrySetResult(x), onB = x => lostB.TrySetResult(x);
        a.Account.ConnectionLost += onA;
        b.Account.ConnectionLost += onB;
        int markA = a.ReceivedCount, markB = b.ReceivedCount;
        Accounts accountA = a.Account, accountB = b.Account;
        Task<int> stopping = RunCommandAsync("arrêt du jeu", stop, 120);
        string evidence;
        int restarted = -1;
        bool restartRun = false;
        PersoSelection servers = null;
        try
        {
            Check(await Task.WhenAny(lostA.Task, Task.Delay(TimeSpan.FromSeconds(90))).ConfigureAwait(false) == lostA.Task, "A n'a pas constaté la coupure du serveur de jeu");
            ConnectionLoss loss = lostA.Task.Result;
            bool m04 = a.ReceivedSince(markA).Contains("M04");
            Check(loss.Cause == DisconnectCause.Lost && loss.WasInGame && accountA.LastDisconnect == loss, "Perte de connexion de A mal lue : " + loss.Cause + ", en jeu " + loss.WasInGame);
            Check(m04 ? loss.Message != null && loss.Message.Id == 4 : loss.Message == null, "M04 " + (m04 ? "reçu mais absent de" : "non reçu mais présent dans") + " la perte de connexion : " + loss.Message);
            Check(loss.OffersReconnect && loss.Text == ConnectionTexts.Disconnected + (m04 ? "\n\n" + loss.Message.Text : "") + "\n\n" + ConnectionTexts.AttemptReconnect,
                "Texte de la perte de connexion : " + loss.Text);
            Form box = await WaitOnUi(() => OpenDialog(lostName), 15).ConfigureAwait(false);
            Check(box != null, "La boîte de perte de connexion ne s'est pas ouverte dans la fenêtre de A");
            string[] shown = await OnUi(() => new[] { box.Text, DialogMessage(box), ReferenceEquals(box.Owner, window).ToString(), window.IsDisposed.ToString(),
                string.Join("/", AllControls(box).OfType<Button>().Where(x => x.Visible).Select(x => x.Tag).OfType<BotDialogResult>().OrderBy(x => x)) }).ConfigureAwait(false);
            Check(shown[0] == loss.Title && shown[1] == loss.Text && shown[2] == "True" && shown[3] == "False" && shown[4] == "Yes/No",
                "Boîte « " + shown[0] + " » : « " + shown[1] + " », au-dessus de la fenêtre de A " + shown[2] + ", fenêtre fermée " + shown[3] + ", boutons " + shown[4]);

            Check(await Task.WhenAny(lostB.Task, Task.Delay(TimeSpan.FromSeconds(15))).ConfigureAwait(false) == lostB.Task, "B n'a pas constaté la coupure du serveur de jeu");
            ConnectionLoss lossB = lostB.Task.Result;
            bool m04B = b.ReceivedSince(markB).Contains("M04");
            Check(lossB.Cause == DisconnectCause.Lost && lossB.WasInGame && (m04B ? lossB.Message?.Id == 4 : lossB.Message == null),
                "Perte de connexion de B mal lue : " + lossB.Cause + ", en jeu " + lossB.WasInGame + ", message " + lossB.Message);
            int stopped = await stopping.ConfigureAwait(false);
            Check(stopped == 0, "La commande d'arrêt du serveur de jeu a échoué (code " + stopped + ")");

            // Le Game relancé et enregistré auprès du Login, « Oui » reconnecte le compte de A (Login.autoLogin du client).
            restartRun = true;
            restarted = await RunCommandAsync("redémarrage du jeu", restart, 240).ConfigureAwait(false);
            Check(restarted == 0, "Le serveur de jeu n'a pas redémarré (code " + restarted + ")");
            Check(await OnUi(() => ReferenceEquals(OpenDialog(lostName), box)).ConfigureAwait(false), "La boîte de perte de connexion s'est refermée sans réponse");
            await OnUi(() => AllControls(box).OfType<Button>().First(x => Equals(x.Tag, BotDialogResult.Yes)).PerformClick()).ConfigureAwait(false);
            servers = await WaitOnUi(() => window.IsDisposed ? Application.OpenForms.OfType<PersoSelection>().FirstOrDefault() : null, 15).ConfigureAwait(false);
            Check(servers != null, "« Oui » n'a pas fermé la fenêtre de jeu de A ni ouvert l'écran du choix du serveur");
            Check(await OnUi(() => !Application.OpenForms.OfType<LoginForm>().Any()).ConfigureAwait(false), "« Oui » a rouvert l'écran de connexion au lieu de reconnecter A");
            PersoSelection screen = servers;
            var again = (Accounts)Field(screen, "A");
            again.Logger.log_event += (entry, color) => Journal("R [journal du bot] " + entry.message);
            Check(again.accountConfig.Account == a.Login && again.accountConfig.Password == a.Password, "« Oui » a reconnecté un autre compte que A");
            if (await WaitOnUi(() => screen.Alreadyhere && again.Game.Server.Servers.Values.Any(x => x == ServerStates.ONLINE) ? "ok" : null, 60).ConfigureAwait(false) == null)
            {
                // Journal de l'écran du choix du serveur (abonné avant la connexion) : dit jusqu'où la connexion est allée.
                string shownLog = await OnUi(() => ((RichTextBox)Field(screen, "journal")).Text.Replace("\n", " | ")).ConfigureAwait(false);
                // Paquets du Login déjà traités par le bot : Ad → Name, AH → Servers, AQ → SecretQuestion (puis Ax). Vu le 21 octobre 2026 :
                // Af traité, rien après, Ax jamais envoyé ; sous Mono, les piles de tous les fils vont dans la sortie du test (SIGQUIT).
                string read = "Name « " + again.Name + " », serveurs " + string.Join(",", again.Game.Server.Servers.Select(x => x.Key + " " + x.Value))
                    + ", question « " + again.SecretQuestion + " », connecté " + (again.Connexion != null && again.Connexion.IsConnected());
                DumpThreads("x3c : reconnexion bloquée après « Oui »");
                throw new Exception("Après « Oui », la liste des serveurs n'a pas de serveur en ligne : " + again.ConnectionStatus + " ; " + read + " ; journal de l'écran : "
                    + (shownLog.Length > 800 ? "…" + shownLog.Substring(shownLog.Length - 800) : shownLog));
            }
            string[] listed = await OnUi(() => new[] { ((Label)Field(screen, "status")).Text,
                string.Join(", ", again.Game.Server.Servers.OrderBy(x => x.Key).Select(x => x.Key + " " + x.Value)) }).ConfigureAwait(false);
            evidence = "A : " + (m04 ? "M04 reçu, " : "M04 non reçu avant la fermeture, ") + "perte " + loss.Cause + " en jeu, boîte « " + shown[0] + " » : « " + shown[1].Replace("\n", " ")
                + " » (Oui / Non) ; B : " + (m04B ? "M04, " : "") + "perte " + lossB.Cause + " (" + accountB.ConnectionStatus + ") ; serveur de jeu redémarré ; « Oui » → fenêtre fermée, "
                + a.Login + " reconnecté au Login : serveurs " + listed[1] + " (« " + listed[0] + " »)";
        }
        finally
        {
            accountA.ConnectionLost -= onA;
            accountB.ConnectionLost -= onB;
            if (servers != null) await OnUi(() => { if (!servers.IsDisposed) servers.Close(); }).ConfigureAwait(false);
            if (!restartRun)
            {
                if (!stopping.IsCompleted) await Task.WhenAny(stopping, Task.Delay(TimeSpan.FromSeconds(120))).ConfigureAwait(false);
                restarted = await RunCommandAsync("redémarrage du jeu", restart, 240).ConfigureAwait(false);
            }
        }
        Check(restarted == 0, "Le serveur de jeu n'a pas redémarré (code " + restarted + ")");
        return evidence;
    }
}

// Scénarios x4a à x4c (lot X4) : familier et monture nourris, garde chez un gardien de familiers, résurrection chez un PNJ qui la propose.
internal static partial class Live
{
    /// <summary>
    /// Carte de départ et PNJ 816 qui y vend le Chacha (familier 1728, type 2 de la table <c>pets</c> : il mange le Greu-Vette 598,
    /// un poisson de type 41) ; poudre de résurrection 8012 (type 48, super-type 9 du client) ; certificat de garde 1735
    /// (<c>Constant.getParchoByIdPets</c>) et fantôme 7537 (<c>pets.DeadTemplate</c>) du Chacha, d'après StarLoco et <c>game.sql</c>.
    /// Les gardiens (action 4) et les ressusciteurs (action 7) sont cherchés dans <c>npc.xml</c> et dans la table <c>npcs</c> du jeu ;
    /// ils sont joints par un zaap puis à pied, au plus <see cref="MaxTriggerWalk"/> cartes.
    /// </summary>
    private const int PetsMap = 164, PetsCell = 298, PetSellerTemplate = 816, ChachaTemplate = 1728, FishTemplate = 598, PowderTemplate = 8012,
        ChachaCertificate = 1735, ChachaGhost = 7537, FishStack = 20, PetsKamas = 1000, MaxTriggerWalk = 10;

    /// <summary>Personnage inventé d'un scénario x4 : session, objets écrits dans la base et objets vus, tous supprimés à la fin.</summary>
    private sealed class PetOwner
    {
        public string Label;
        public InventedCharacter Character;
        public LiveSession Session;
        public readonly List<InventedCharacter> Created = new List<InventedCharacter>();
        public readonly HashSet<uint> Seen = new HashSet<uint>();
        public readonly ConcurrentQueue<ServerMessage> Heard = new ConcurrentQueue<ServerMessage>();
        public int FishId = -1, PowderId = -1, MountId;
        public NpcActor Seller;
        public Tool_BotProtocol.Game.Perso.Inventory.InventoryClass Bag => Session.Account.Game.character.Inventory;
        public Tool_BotProtocol.Game.Perso.Inventory.InventoryObjects Fish => FishId > 0 ? Bag.GetByInventoryId((uint)FishId) : null;
        public Tool_BotProtocol.Game.Perso.Inventory.InventoryObjects Powder => PowderId > 0 ? Bag.GetByInventoryId((uint)PowderId) : null;
        public void Remember() { if (Session?.Account?.Game?.character?.Inventory != null) Seen.UnionWith(Bag.Objets.Select(x => x.Inventory_ID)); }
    }

    private static void RequirePetsData()
    {
        if (!Map.AllBotMaps.ContainsKey(PetsMap)) throw new SkipScenario("carte " + PetsMap + " absente des cartes du bot");
        if (!LangData.IsLoaded("items")) throw new SkipScenario("textes du client absents : le bot ne connaît ni les ressources (super-type 9) ni la case du familier");
    }

    /// <summary>
    /// x4a. Compte inventé avec 20 Greu-Vette, 2 poudres et une monture écrits dans la base, Chacha acheté au PNJ 816 puis porté
    /// (<c>OM|8</c>) et nourri (<c>OM&lt;poisson&gt;|8</c> → OQ, Im du repas, OCO) ; poudre refusée par le familier (<c>Im153</c>) et par la
    /// monture (<c>OM&lt;poudre&gt;|16|1</c> → <c>Im190</c>) ; poisson donné à la monture (<c>OM&lt;poisson&gt;|16|2</c> → Re+, <c>Im0105</c>).
    /// </summary>
    private static async Task<string> PetsFeedingAsync()
    {
        RequirePetsData();
        using (MySql.Data.MySqlClient.MySqlConnection db = OpenAccountsDatabase(out string schema))
        {
            var owner = new PetOwner { Label = "C" };
            try
            {
                var evidence = new List<string>();
                await OpenPetOwnerAsync(db, schema, owner, FishStack, 2, true, evidence).ConfigureAwait(false);
                LiveSession c = owner.Session;
                Tool_BotProtocol.Game.Perso.Inventory.InventoryClass bag = owner.Bag;
                Tool_BotProtocol.Game.Perso.Inventory.InventoryObjects pet = Owned(c, ChachaTemplate);

                // Familier porté puis nourri : OM<poisson>|8 sans quantité ; StarLoco répond OQ, un Im du repas (INFOS_26 à 32) et OCO.
                evidence.Add(await WearPetAsync(c, pet).ConfigureAwait(false));
                evidence.Add(await FeedAsync(c, owner.Heard, owner.Fish, 8, null, p => p.StartsWith("Im0", StringComparison.Ordinal), "Im du repas du familier",
                    ServerMessageKind.Info, id => id >= 25 && id <= 32).ConfigureAwait(false));
                Check(await c.Until(() => owner.Fish?.Qua == FishStack - 1, 5).ConfigureAwait(false), "OQ du poisson non appliqué : " + owner.Fish?.Qua);
                evidence.Add(await FeedAsync(c, owner.Heard, owner.Powder, 8, null, p => p.StartsWith("Im153", StringComparison.Ordinal), "Im153 (le familier refuse la poudre)",
                    ServerMessageKind.Error, id => id == 53).ConfigureAwait(false));

                // Monture : la poudre (super-type 9) est proposée comme le client le permet, StarLoco la refuse (Im190) ; poisson ×2 → Re+, Im0105.
                evidence.Add(await FeedAsync(c, owner.Heard, owner.Powder, 16, 1, p => p.StartsWith("Im190", StringComparison.Ordinal), "Im190 (la monture refuse la poudre)",
                    ServerMessageKind.Error, id => id == 90).ConfigureAwait(false));
                int energy = c.Account.Game.Interactions.Mount.Current?.Energy ?? -1, rePlus = c.ReceivedCount;
                evidence.Add(await FeedAsync(c, owner.Heard, owner.Fish, 16, 2, p => p.StartsWith("Im0105", StringComparison.Ordinal), "Im0105 (la monture apprécie)",
                    ServerMessageKind.Info, id => id == 105).ConfigureAwait(false));
                Check(c.ReceivedSince(rePlus).Any(p => p.StartsWith("Re+", StringComparison.Ordinal))
                    && await c.Until(() => owner.Fish?.Qua == FishStack - 3 && (c.Account.Game.Interactions.Mount.Current?.Energy ?? -1) > energy, 5).ConfigureAwait(false),
                    "Repas de la monture non appliqué : poisson " + owner.Fish?.Qua + ", énergie " + energy + " → " + c.Account.Game.Interactions.Mount.Current?.Energy);
                evidence.Add("énergie de la monture " + energy + " → " + c.Account.Game.Interactions.Mount.Current?.Energy);
                owner.Remember();
                return string.Join(" ; ", evidence);
            }
            finally { await ClosePetOwnerAsync(db, schema, owner).ConfigureAwait(false); }
        }
    }

    /// <summary>
    /// x4b. Chacha acheté au PNJ 816, puis zaap et marche jusqu'à un gardien (PNJ dont <c>npc.xml</c> déclare l'action 4, placé dans la
    /// table <c>npcs</c>) ; garde depuis l'action du menu (<c>ER9|&lt;pnj&gt;</c> → <c>ECK9</c>) : familier contre certificat 1735, puis
    /// certificat contre un nouveau Chacha (<c>createNewFamilier</c>).
    /// </summary>
    private static async Task<string> PetsKeepingAsync()
    {
        RequirePetsData();
        using (MySql.Data.MySqlClient.MySqlConnection db = OpenAccountsDatabase(out string schema))
        {
            var owner = new PetOwner { Label = "D" };
            try
            {
                List<int[]> keepers = NpcsWithAction(db, NpcActions.Pets);
                if (keepers.Count == 0) throw new SkipScenario("aucun PNJ placé dont npc.xml déclare la garde (action 4)");
                var evidence = new List<string>();
                await OpenPetOwnerAsync(db, schema, owner, 0, 0, false, evidence).ConfigureAwait(false);
                LiveSession s = owner.Session;
                Tool_BotProtocol.Game.Perso.Inventory.InventoryObjects pet = Owned(s, ChachaTemplate);
                evidence.Add(await TravelAsync(s, keepers.Select(k => k[1])).ConfigureAwait(false));
                NpcActor keeper = await FindNpcAsync(s, keepers.Where(k => k[1] == s.MapId).Select(k => k[0])).ConfigureAwait(false);

                PetsExchange pets = s.Account.Game.Interactions.Exchanges.Get<PetsExchange>();
                evidence.Add(await PetsTradeAsync(s, pets, NpcActions.Pets, keeper, new[] { pet.Inventory_ID }, ChachaCertificate, PetsService.Keeping).ConfigureAwait(false));
                owner.Remember();
                Tool_BotProtocol.Game.Perso.Inventory.InventoryObjects certificate = Owned(s, ChachaCertificate);
                Check(certificate != null && Owned(s, ChachaTemplate) == null, "Après la garde, le bot devrait avoir le certificat et plus de Chacha");
                evidence.Add(await PetsTradeAsync(s, pets, NpcActions.Pets, keeper, new[] { certificate.Inventory_ID }, ChachaTemplate, PetsService.Keeping).ConfigureAwait(false));
                owner.Remember();
                Check(Owned(s, ChachaTemplate) != null && Owned(s, ChachaCertificate) == null, "Après le retrait, le bot devrait avoir un Chacha et plus de certificat");
                return string.Join(" ; ", evidence);
            }
            finally { await ClosePetOwnerAsync(db, schema, owner).ConfigureAwait(false); }
        }
    }

    /// <summary>
    /// x4c. Chacha acheté au PNJ 816, porté et nourri à la suite : StarLoco retire un point de vie à chaque repas trop rapproché (Im027)
    /// jusqu'au fantôme 7537 (<c>Im154</c>, OM vers le sac) ; un fantôme ne se nourrit plus. Puis zaap et marche jusqu'à un PNJ dont
    /// <c>npc.xml</c> déclare la résurrection (action 7) : <c>ER17|&lt;pnj&gt;</c> → <c>ECK9</c>, fantôme et poudre contre le Chacha.
    /// </summary>
    private static async Task<string> PetsResurrectionAsync()
    {
        RequirePetsData();
        using (MySql.Data.MySqlClient.MySqlConnection db = OpenAccountsDatabase(out string schema))
        {
            var owner = new PetOwner { Label = "E" };
            try
            {
                List<int[]> healers = NpcsWithAction(db, NpcActions.PetResurrection);
                if (healers.Count == 0) throw new SkipScenario("aucun PNJ placé dont npc.xml déclare la résurrection des familiers (action 7)");
                var evidence = new List<string>();
                await OpenPetOwnerAsync(db, schema, owner, FishStack, 1, false, evidence).ConfigureAwait(false);
                LiveSession s = owner.Session;
                Tool_BotProtocol.Game.Perso.Inventory.InventoryClass bag = owner.Bag;
                Tool_BotProtocol.Game.Perso.Inventory.InventoryObjects pet = Owned(s, ChachaTemplate);
                evidence.Add(await WearPetAsync(s, pet).ConfigureAwait(false));

                int meals = 0;
                string death = null;
                while (death == null && meals < FishStack)
                {
                    Tool_BotProtocol.Game.Perso.Inventory.InventoryObjects food = owner.Fish;
                    Check(food != null && food.Qua > 0, "Plus de poisson après " + meals + " repas");
                    int mark = s.ReceivedCount;
                    Check(await bag.MoveToSlot(food, Tool_BotProtocol.Game.Perso.Inventory.ItemSlots.PetPosition).ConfigureAwait(false), "Repas " + (meals + 1) + " refusé par le bot");
                    meals++;
                    await s.Expect(p => p.StartsWith("OCO", StringComparison.Ordinal), "OCO après le repas " + meals, 10, mark).ConfigureAwait(false);
                    death = s.ReceivedSince(mark).FirstOrDefault(p => p.StartsWith("Im154", StringComparison.Ordinal));
                }
                Check(death != null, "Pas de fantôme après " + meals + " repas");
                Tool_BotProtocol.Game.Perso.Inventory.InventoryObjects ghost = null;
                Check(await s.Until(() => (ghost = bag.GetByInventoryId(pet.Inventory_ID)) != null && ghost.ID == ChachaGhost && (int)ghost.position == -1 && bag.EquippedPet == null, 5).ConfigureAwait(false),
                    "Le bot n'a pas lu le fantôme : modèle " + ghost?.ID + ", position " + (int?)ghost?.position);
                Check(owner.Heard.Any(m => m.Kind == ServerMessageKind.Error && m.NumericId == 54 && !string.IsNullOrEmpty(m.Text)), "Im154 non transmis par la session");
                evidence.Add(meals + " repas rapprochés → " + death + ", " + ghost.Name + " (" + ghost.ID + ") rangé dans le sac");
                int sentMark = s.SentCount;
                Check(!await bag.MoveToSlot(owner.Fish, Tool_BotProtocol.Game.Perso.Inventory.ItemSlots.PetPosition).ConfigureAwait(false)
                    && !s.SentSince(sentMark).Any(p => p.StartsWith("OM", StringComparison.Ordinal)), "Le bot nourrit encore un familier mort");
                owner.Remember();

                evidence.Add(await TravelAsync(s, healers.Select(k => k[1])).ConfigureAwait(false));
                NpcActor healer = await FindNpcAsync(s, healers.Where(k => k[1] == s.MapId).Select(k => k[0])).ConfigureAwait(false);
                PetsExchange pets = s.Account.Game.Interactions.Exchanges.Get<PetsExchange>();
                evidence.Add(await PetsTradeAsync(s, pets, NpcActions.PetResurrection, healer, new[] { ghost.Inventory_ID, (uint)owner.PowderId }, ChachaTemplate, PetsService.Resurrection).ConfigureAwait(false));
                Check(await s.Until(() => bag.GetByInventoryId(ghost.Inventory_ID)?.ID == ChachaTemplate && owner.Powder == null, 5).ConfigureAwait(false),
                    "Le Chacha ressuscité ou la poudre consommée ne sont pas lus : modèle " + bag.GetByInventoryId(ghost.Inventory_ID)?.ID + ", poudre " + owner.Powder?.Qua);
                owner.Remember();
                return string.Join(" ; ", evidence);
            }
            finally { await ClosePetOwnerAsync(db, schema, owner).ConfigureAwait(false); }
        }
    }

    /// <summary>
    /// Compte inventé posé sur la carte 164 avec des kamas, <paramref name="fish"/> Greu-Vette, <paramref name="powder"/> poudres de
    /// résurrection et, au besoin, une monture écrits dans la base (identifiants hors de la plage de StarLoco), connecté, puis un Chacha
    /// acheté au PNJ 816 : les entrées de familier (<c>world.entity.pets</c>) ne se chargent qu'au démarrage du Game ou à l'achat.
    /// </summary>
    private static async Task OpenPetOwnerAsync(MySql.Data.MySqlClient.MySqlConnection db, string schema, PetOwner owner, int fish, int powder, bool mount, List<string> evidence)
    {
        int baseId = 500000000 + random.Next(1, 100000) * 10;
        owner.Character = Invent(db, schema, a.Id, PetsMap, PetsCell, 10000, owner.Created);
        var objets = new StringBuilder();
        if (fish > 0)
        {
            owner.FishId = baseId;
            Sql(db, "INSERT INTO " + schema + ".`world.entity.objects` (id, template, quantity, position, stats, puit) VALUES (@id, @template, @quantity, -1, '', 0)",
                "@id", owner.FishId, "@template", FishTemplate, "@quantity", fish);
            objets.Append(owner.FishId).Append('|');
        }
        if (powder > 0)
        {
            owner.PowderId = baseId + 1;
            Sql(db, "INSERT INTO " + schema + ".`world.entity.objects` (id, template, quantity, position, stats, puit) VALUES (@id, @template, @quantity, -1, '', 0)",
                "@id", owner.PowderId, "@template", PowderTemplate, "@quantity", powder);
            objets.Append(owner.PowderId).Append('|');
        }
        if (mount)
        {
            owner.MountId = -baseId;
            Sql(db, "INSERT INTO " + schema + ".`world.entity.mounts` (id, color, sex, name, xp, level, endurance, amour, maturity, serenity, reproductions, fatigue, energy,"
                + " objects, ancestors, capacitys, size, map, cell, owner, orientation, fecundatedDate, couple, savage)"
                + " VALUES (@id, 10, 0, 'Essai', 0, 1, 0, 0, 1000, 0, 0, 0, 1000, '', ',,,,,,,,,,,,,', '0', 100, @map, @cell, @owner, 1, -1, -1, 0)",
                "@id", owner.MountId, "@map", PetsMap, "@cell", PetsCell, "@owner", owner.Character.Id);
        }
        Sql(db, "UPDATE " + schema + ".players SET kamas = @kamas, objets = @objets, mount = @mount WHERE id = @id",
            "@kamas", PetsKamas, "@objets", objets.ToString(), "@mount", mount ? owner.MountId : -1, "@id", owner.Character.Id);
        // Zaaps connus : ceux qu'un personnage créé par StarLoco reçoit avec ALL_ZAAP (CREATE_PERSONNAGE : toutes les cartes de la
        // table zaaps du jeu) ; le personnage de A ne sert pas de modèle, sa colonne n'est écrite qu'à sa première sauvegarde.
        object zaaps = Scalar(db, "SELECT GROUP_CONCAT(mapID ORDER BY mapID SEPARATOR ',') FROM zaaps");
        Sql(db, "UPDATE " + schema + ".players SET zaaps = @zaaps WHERE id = @id", "@zaaps", Convert.ToString(zaaps, CultureInfo.InvariantCulture) ?? string.Empty, "@id", owner.Character.Id);
        Journal("[test] " + owner.Character.Name + " : " + PetsKamas + " kamas, objets « " + objets + " »" + (mount ? ", monture " + owner.MountId : string.Empty) + " écrits dans la base");

        LiveSession s = owner.Session = new LiveSession(owner.Label, owner.Character.Login, owner.Character.Password);
        evidence.Add(await EnterExistingAsync(s, owner.Character.Id, owner.Character.Name, PetsMap).ConfigureAwait(false));
        s.Account.Game.Session.ServerMessageReceived += owner.Heard.Enqueue;
        Check(await s.Until(() => (!mount || owner.Bag.HasMount) && (fish == 0 || owner.Fish?.Qua == fish) && (powder == 0 || owner.Powder?.Qua == powder), 10).ConfigureAwait(false),
            "Monture (Re+) ou objets écrits dans la base absents : monture " + owner.Bag.HasMount + ", poisson " + owner.Fish?.Qua + ", poudre " + owner.Powder?.Qua);
        if (fish > 0) Check(Tool_BotProtocol.Game.Perso.Inventory.ItemSlots.IsFood(owner.Fish.Type), "Poisson (type " + owner.Fish.Type + ") hors du super-type 9 des textes du client");
        if (powder > 0) Check(Tool_BotProtocol.Game.Perso.Inventory.ItemSlots.IsFood(owner.Powder.Type), "Poudre (type " + owner.Powder.Type + ") hors du super-type 9 des textes du client");
        if (mount) evidence.Add("Re+ à l'entrée : monture " + owner.MountId + ", énergie " + s.Account.Game.Interactions.Mount.Current?.Energy);
        if (fish > 0)
        {
            // Sans familier porté, la case 8 refuse la nourriture sans rien envoyer.
            int sentMark = s.SentCount;
            Check(!await owner.Bag.MoveToSlot(owner.Fish, Tool_BotProtocol.Game.Perso.Inventory.ItemSlots.PetPosition).ConfigureAwait(false)
                && !s.SentSince(sentMark).Any(p => p.StartsWith("OM", StringComparison.Ordinal)), "Le bot nourrit un familier absent");
        }

        // Achat du Chacha : ER0|<pnj>, EB1728|1 ; StarLoco crée le familier et son entrée (pets).
        await s.Until(() => s.Map.Npcs.Any(n => n.TemplateId == PetSellerTemplate), 10).ConfigureAwait(false);
        owner.Seller = s.Map.Npcs.FirstOrDefault(n => n.TemplateId == PetSellerTemplate);
        if (owner.Seller == null) throw new SkipScenario("pas de PNJ " + PetSellerTemplate + " sur la carte " + PetsMap);
        evidence.Add(await BuyPetAsync(s, owner.Seller).ConfigureAwait(false));
        owner.Remember();
    }

    /// <summary>
    /// Lot X4-CORR : avant la déconnexion, détruit en jeu tout ce que le sac contient encore (<c>Od&lt;id&gt;|&lt;quantité&gt;</c>, confirmé
    /// par <c>OR</c>). Un objet créé par StarLoco pendant le scénario (Chacha acheté, certificat, fantôme, Chacha rendu ou ressuscité) ne
    /// vit qu'en mémoire du Game (<c>modification = 0</c>) jusqu'à la sauvegarde du monde : un <c>DELETE</c> SQL ne l'atteint pas et la
    /// sauvegarde suivante l'écrit (<c>REPLACE INTO</c>) sans propriétaire ; <c>destroyObject</c> le retire de la mémoire et de la base
    /// (<c>World.removeGameObject</c>). Un échange resté ouvert est d'abord refermé (<c>EV</c>), un objet porté d'abord rangé
    /// (<c>OM&lt;id&gt;|-1</c>), StarLoco ignorant <c>Od</c> sur un objet équipé. Rend les identifiants détruits, ceux que StarLoco avait déjà
    /// retirés (<c>OR</c> reçu pendant le scénario) et les échecs, sans rien supposer.
    /// </summary>
    private static async Task DestroyBagAsync(PetOwner owner, List<uint> destroyed, List<uint> gone, List<string> failed)
    {
        LiveSession s = owner.Session;
        if (s == null || !InGame(s)) { failed.Add("session absente ou hors jeu : rien détruit en jeu"); return; }
        owner.Remember();
        Tool_BotProtocol.Game.Perso.Inventory.InventoryClass bag = owner.Bag;
        IExchange exchange = s.Account.Game.Interactions.Exchanges.Current;
        if (exchange != null && exchange.IsOpen)
        {
            await s.Send("EV").ConfigureAwait(false);
            if (!await s.Until(() => !exchange.IsOpen, 5).ConfigureAwait(false)) failed.Add("échange " + exchange.ExchangeType + " toujours ouvert après EV");
        }
        foreach (Tool_BotProtocol.Game.Perso.Inventory.InventoryObjects item in bag.Objets.Where(x => x != null && x.Qua > 0).OrderBy(x => x.Inventory_ID).ToList())
        {
            uint id = item.Inventory_ID;
            try
            {
                if ((int)item.position != -1)
                {
                    Check(await bag.MoveToSlot(item, -1).ConfigureAwait(false), "rangement dans le sac refusé par le bot");
                    Check(await s.Until(() => (int)item.position == -1, 5).ConfigureAwait(false), "le serveur ne range pas l'objet (position " + (int)item.position + ")");
                }
                int mark = s.ReceivedCount;
                Check(await bag.Destroy(item, item.Qua).ConfigureAwait(false), "destruction refusée par le bot");
                Check(await s.Until(() => bag.GetByInventoryId(id) == null, 5).ConfigureAwait(false) && s.ReceivedSince(mark).Contains("OR" + id), "pas de OR" + id + " du serveur");
                destroyed.Add(id);
            }
            catch (Exception error) { failed.Add(id + " (" + item.Name + ") : " + error.Message); }
        }
        string[] received = s.ReceivedSince(0);
        gone.AddRange(owner.Seen.Where(id => !destroyed.Contains(id) && received.Contains("OR" + id)).OrderBy(x => x));
    }

    /// <summary>
    /// Détruit en jeu ce que le sac contient (<see cref="DestroyBagAsync"/>), ferme la session, relit les objets enregistrés par StarLoco,
    /// supprime les lignes restantes (objets, entrées de familier, monture), relit la base et journalise exactement ce qui a été fait ;
    /// puis le compte et le personnage inventés.
    /// </summary>
    private static async Task ClosePetOwnerAsync(MySql.Data.MySqlClient.MySqlConnection db, string schema, PetOwner owner, Func<Task> beforeLogout = null)
    {
        var destroyed = new List<uint>(); var gone = new List<uint>(); var failed = new List<string>();
        try { await DestroyBagAsync(owner, destroyed, gone, failed).ConfigureAwait(false); }
        catch (Exception error) { failed.Add("destruction en jeu interrompue : " + error.Message); }
        // Fenêtre de jeu du compte (lot X6) : fermée après la destruction en jeu, sa fermeture libérant le compte.
        if (beforeLogout != null)
            try { await beforeLogout().ConfigureAwait(false); }
            catch (Exception error) { failed.Add("fermeture avant la déconnexion : " + error.Message); }
        owner.Session?.Dispose();
        await Task.Delay(1500).ConfigureAwait(false);
        try
        {
            if (owner.Character != null && owner.Character.Id > 0)
            {
                object saved = Scalar(db, "SELECT objets FROM " + schema + ".players WHERE id = @id AND account = @account", "@id", owner.Character.Id, "@account", owner.Character.Guid);
                foreach (string part in Convert.ToString(saved, CultureInfo.InvariantCulture).Split('|'))
                    if (uint.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out uint id)) owner.Seen.Add(id);
            }
            if (owner.FishId > 0) owner.Seen.Add((uint)owner.FishId);
            if (owner.PowderId > 0) owner.Seen.Add((uint)owner.PowderId);
            var objectRows = new List<uint>(); var petRows = new List<uint>();
            foreach (uint id in owner.Seen.OrderBy(x => x))
            {
                if (Sql(db, "DELETE FROM " + schema + ".`world.entity.objects` WHERE id = @id", "@id", (long)id) > 0) objectRows.Add(id);
                if (Sql(db, "DELETE FROM " + schema + ".`world.entity.pets` WHERE id = @id", "@id", (long)id) > 0) petRows.Add(id);
            }
            bool mountRow = owner.MountId != 0 && Sql(db, "DELETE FROM " + schema + ".`world.entity.mounts` WHERE id = @id", "@id", owner.MountId) > 0;
            // Ni détruit en jeu, ni retiré par StarLoco, ni trouvé en base : encore en mémoire du Game, la prochaine sauvegarde du monde l'écrira.
            List<uint> unknown = owner.Seen.Where(id => !destroyed.Contains(id) && !gone.Contains(id) && !objectRows.Contains(id)).OrderBy(x => x).ToList();
            string ids = string.Join(",", owner.Seen.Select(id => id.ToString(CultureInfo.InvariantCulture)));
            long leftObjects = ids.Length == 0 ? 0 : Convert.ToInt64(Scalar(db, "SELECT COUNT(*) FROM " + schema + ".`world.entity.objects` WHERE id IN (" + ids + ")"), CultureInfo.InvariantCulture);
            long leftPets = ids.Length == 0 ? 0 : Convert.ToInt64(Scalar(db, "SELECT COUNT(*) FROM " + schema + ".`world.entity.pets` WHERE id IN (" + ids + ")"), CultureInfo.InvariantCulture);
            long leftMounts = owner.MountId == 0 ? 0 : Convert.ToInt64(Scalar(db, "SELECT COUNT(*) FROM " + schema + ".`world.entity.mounts` WHERE id = @id", "@id", owner.MountId), CultureInfo.InvariantCulture);
            Journal("[test] nettoyage x4 : objets détruits en jeu (Od → OR) " + Ids(destroyed) + " ; déjà retirés par StarLoco (OR) " + Ids(gone)
                + " ; lignes d'objets supprimées de la base " + Ids(objectRows) + " ; entrées de familier supprimées " + Ids(petRows)
                + " ; monture " + (owner.MountId == 0 ? "aucune" : owner.MountId + (mountRow ? " supprimée de la base" : " absente de la base"))
                + " ; relecture : " + leftObjects + " objet(s), " + leftPets + " familier(s), " + leftMounts + " monture(s) du scénario encore en base");
            if (unknown.Count > 0) Journal("!! [test] objets " + Ids(unknown) + " ni détruits en jeu, ni retirés par StarLoco, ni trouvés en base : la prochaine sauvegarde du monde peut les écrire sans propriétaire");
            if (failed.Count > 0) Journal("!! [test] destruction en jeu manquée : " + string.Join(" / ", failed));
            if (leftObjects + leftPets + leftMounts > 0) Journal("!! [test] des lignes du scénario x4 restent en base (objets " + ids + (owner.MountId != 0 ? ", monture " + owner.MountId : string.Empty) + ")");
        }
        catch (Exception error) { Journal("!! [test] nettoyage des objets du scénario x4 impossible : " + error.Message); }
        await ForgetAsync(db, schema, new LiveSession[0], owner.Created).ConfigureAwait(false);
    }

    private static string Ids(IEnumerable<uint> ids) { string text = string.Join(",", ids.Select(id => id.ToString(CultureInfo.InvariantCulture))); return text.Length == 0 ? "aucun" : text; }

    /// <summary>
    /// PNJ placés par StarLoco (table <c>npcs</c> de la base du jeu) dont <c>npc.xml</c> déclare <paramref name="action"/>, sur une carte
    /// du bot : paires { modèle, carte }. Vide sans <c>npc.xml</c>, le menu de repli ne proposant ni la garde ni la résurrection.
    /// </summary>
    private static List<int[]> NpcsWithAction(MySql.Data.MySqlClient.MySqlConnection db, int action)
    {
        var found = new List<int[]>();
        if (!LangData.IsLoaded("npc")) return found;
        using (var command = new MySql.Data.MySqlClient.MySqlCommand("SELECT npcid, mapid FROM npcs ORDER BY mapid, npcid", db))
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                int template = Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture), map = Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture);
                if (Map.AllBotMaps.ContainsKey(map) && NpcActions.ForTemplate(template, out bool fromClient).Contains(action) && fromClient) found.Add(new[] { template, map });
            }
        return found;
    }

    /// <summary>PNJ de la carte courante dont le modèle est l'un de <paramref name="templates"/>.</summary>
    private static async Task<NpcActor> FindNpcAsync(LiveSession s, IEnumerable<int> templates)
    {
        var wanted = new HashSet<int>(templates);
        await s.Until(() => s.Map.Npcs.Any(n => wanted.Contains(n.TemplateId)), 10).ConfigureAwait(false);
        NpcActor npc = s.Map.Npcs.FirstOrDefault(n => wanted.Contains(n.TemplateId));
        Check(npc != null, "PNJ " + string.Join(",", wanted) + " absent de la carte " + s.MapId);
        return npc;
    }

    /// <summary>
    /// Chemin le plus court de <paramref name="from"/> à <paramref name="to"/> par les déclencheurs de téléportation sans condition
    /// (sorties de carte de <c>scripted_cells</c>) entre cartes du bot, ou null au-delà de <see cref="MaxTriggerWalk"/> cartes.
    /// </summary>
    private static List<Trigger> TriggerRoute(int from, int to)
    {
        var previous = new Dictionary<int, Trigger> { [from] = null };
        var depth = new Dictionary<int, int> { [from] = 0 };
        var queue = new Queue<int>();
        queue.Enqueue(from);
        while (queue.Count > 0 && !previous.ContainsKey(to))
        {
            int map = queue.Dequeue();
            if (depth[map] >= MaxTriggerWalk) continue;
            foreach (Trigger exit in Triggers.ForMap(map).Values
                .Where(t => t.IsTeleport && (string.IsNullOrEmpty(t.Conditions) || t.Conditions == "-1") && Map.AllBotMaps.ContainsKey(t.TargetMapId))
                .OrderBy(t => t.CellId))
            {
                if (previous.ContainsKey(exit.TargetMapId)) continue;
                previous[exit.TargetMapId] = exit;
                depth[exit.TargetMapId] = depth[map] + 1;
                queue.Enqueue(exit.TargetMapId);
            }
        }
        if (!previous.ContainsKey(to)) return null;
        var route = new List<Trigger>();
        for (int map = to; previous[map] != null; map = previous[map].MapId) route.Insert(0, previous[map]);
        return route;
    }

    /// <summary>
    /// Va de la carte courante à l'une des cartes <paramref name="targets"/> : zaap de la carte (<c>WC</c>), destination d'où le chemin à
    /// pied est le plus court (<c>WU&lt;carte&gt;</c> ; ALL_ZAAP du kit : tous les zaaps sont connus), puis les déclencheurs un à un.
    /// </summary>
    private static async Task<string> TravelAsync(LiveSession s, IEnumerable<int> targets)
    {
        int[] wanted = targets.Distinct().ToArray();
        ZaapDialog zaap = s.Account.Game.Interactions.Zaap;
        short cell = s.Map.MapCells.Where(c => c != null && zaap.IsZaapCell(c.CellID)).Select(c => c.CellID).DefaultIfEmpty((short)-1).First();
        if (cell < 0) throw new SkipScenario("aucun zaap sur la carte " + s.MapId);
        int mark = s.ReceivedCount, start = s.MapId;
        var open = await zaap.OpenAsync(cell).ConfigureAwait(false);
        Check(open.Sent, "Zaap refusé par le bot : " + open.Message);
        // Un zaap jamais utilisé est d'abord mémorisé (Im024, « nouveau zaap »), puis StarLoco envoie WC.
        string wc = await s.Expect(p => p.StartsWith("WC") || p.StartsWith("GA;0") || (p.StartsWith("Im") && !p.StartsWith("Im024")), "WC (destinations du zaap)", 20, mark).ConfigureAwait(false);
        Check(wc.StartsWith("WC"), "Le serveur n'ouvre pas le zaap : " + wc);
        Check(await s.Until(() => zaap.IsOpen && zaap.Destinations.Count > 0, 5).ConfigureAwait(false), "Le bot n'a lu aucune destination");
        int kamas = s.Account.Game.character.Kamas;
        var best = zaap.Destinations.Where(d => !d.IsCurrent && d.Cost <= kamas && Map.AllBotMaps.ContainsKey(d.MapId))
            .SelectMany(d => wanted.Select(t => new { Destination = d, Target = t, Route = d.MapId == t ? new List<Trigger>() : TriggerRoute(d.MapId, t) }))
            .Where(x => x.Route != null).OrderBy(x => x.Route.Count).ThenBy(x => x.Destination.Cost).FirstOrDefault();
        if (best == null)
        {
            await zaap.LeaveAsync().ConfigureAwait(false);
            throw new SkipScenario("cartes " + string.Join(",", wanted) + " injoignables depuis les zaaps (" + MaxTriggerWalk + " cartes à pied au plus)");
        }
        int travel = s.ReceivedCount;
        var go = await zaap.TeleportAsync(best.Destination.MapId).ConfigureAwait(false);
        Check(go.Sent, "Téléportation refusée par le bot : " + go.Message);
        try { await s.Expect("GDM|" + best.Destination.MapId, "GDM|" + best.Destination.MapId + " (arrivée au zaap)", 20, travel).ConfigureAwait(false); }
        catch (TimeoutException) when (s.ReceivedSince(travel).Any(p => p.StartsWith("GA;2;", StringComparison.Ordinal)))
        {
            // Lot X4-CORR : ce n'est pas le bot. StarLoco a commencé la téléportation (GA;2, GM|-) puis s'est interrompu : un null laissé
            // dans GameCase.players de la carte de départ par deux départs simultanés de la même cellule (voir SeparateAsync) fait lever
            // une NullPointerException dans GAME_SEND_ERASE_ON_MAP_TO_MAP avant le GDM. L'état du serveur est en cause, pas un lot du bot.
            throw new Exception("GA;2 reçu sans GDM|" + best.Destination.MapId + " : téléportation interrompue par StarLoco (NullPointerException dans"
                + " GAME_SEND_ERASE_ON_MAP_TO_MAP, entrée nulle laissée dans GameCase.players de la carte " + start + " par deux départs simultanés"
                + " de la même cellule) ; le Game doit être redémarré");
        }
        Check(await s.Until(() => s.MapId == best.Destination.MapId && s.Map.Self != null && s.Map.Self.CellId >= 0, 10).ConfigureAwait(false), "Le bot n'est pas sur la carte du zaap d'arrivée");
        await Task.Delay(400).ConfigureAwait(false);
        foreach (Trigger step in best.Route) await WalkThroughTrigger(s, step).ConfigureAwait(false);
        Check(s.MapId == best.Target, "Le bot n'est pas arrivé sur la carte " + best.Target + " : " + s.MapId);
        return "zaap de la carte " + start + " → WU" + best.Destination.MapId + " (" + best.Destination.Cost + " k), puis " + best.Route.Count + " carte(s) à pied ("
            + string.Join(" → ", best.Route.Select(t => t.MapId + ":" + t.CellId)) + ") → carte " + best.Target;
    }

    /// <summary>Boutique du PNJ 816 : <c>ER0|&lt;pnj&gt;</c>, <c>EB1728|1</c>, puis <c>EV</c>.</summary>
    private static async Task<string> BuyPetAsync(LiveSession s, NpcActor seller)
    {
        NpcShop shop = s.Account.Game.Interactions.Shop;
        int mark = s.ReceivedCount;
        var open = await shop.OpenAsync((int)seller.Id).ConfigureAwait(false);
        Check(open.Sent, "Boutique refusée par le bot : " + open.Message);
        string eck = await s.Expect(p => p.StartsWith("ECK") || p.StartsWith("ERE") || p.StartsWith("EV"), "ECK0 (boutique ouverte)", 10, mark).ConfigureAwait(false);
        Check(eck.StartsWith("ECK"), "Le serveur n'ouvre pas la boutique du PNJ " + PetSellerTemplate + " : " + eck);
        Check(await s.Until(() => shop.IsOpen && shop.Articles.Any(x => x.TemplateId == ChachaTemplate), 10).ConfigureAwait(false), "Chacha absent de la boutique du PNJ " + PetSellerTemplate);
        int buyMark = s.ReceivedCount;
        var buy = await shop.BuyAsync(ChachaTemplate, 1).ConfigureAwait(false);
        Check(buy.Sent, "Achat du Chacha refusé par le bot : " + buy.Message);
        string ebk = await s.Expect(p => p.StartsWith("EBK") || p.StartsWith("EBE"), "EBK (achat accepté)", 10, buyMark).ConfigureAwait(false);
        Check(ebk.StartsWith("EBK"), "Achat du Chacha refusé par le serveur : " + ebk);
        Check(await s.Until(() => Owned(s, ChachaTemplate) != null && !shop.IsPending, 10).ConfigureAwait(false), "Le Chacha n'arrive pas dans l'inventaire (OAKO)");
        int closeMark = s.ReceivedCount;
        Check((await shop.LeaveAsync().ConfigureAwait(false)).Sent, "Fermeture de la boutique refusée par le bot");
        await s.Expect("EV", "EV (boutique fermée)", 10, closeMark).ConfigureAwait(false);
        var pet = Owned(s, ChachaTemplate);
        return "achat au PNJ " + seller.DisplayName + " : " + ebk + ", " + pet.Name + " (" + pet.Inventory_ID + ", type " + pet.Type + ")";
    }

    /// <summary>Familier porté : <c>OM&lt;familier&gt;|8</c>, confirmé par le même OM.</summary>
    private static async Task<string> WearPetAsync(LiveSession s, Tool_BotProtocol.Game.Perso.Inventory.InventoryObjects pet)
    {
        var bag = s.Account.Game.character.Inventory;
        int mark = s.ReceivedCount;
        Check(await bag.MoveToSlot(pet, Tool_BotProtocol.Game.Perso.Inventory.ItemSlots.PetPosition).ConfigureAwait(false), "Le bot refuse de porter " + pet.Name);
        string om = await s.Expect("OM" + pet.Inventory_ID + "|8", "OM|8 (familier porté)", 10, mark).ConfigureAwait(false);
        Check(await s.Until(() => bag.EquippedPet?.Inventory_ID == pet.Inventory_ID, 5).ConfigureAwait(false), "Le bot ne voit pas le familier porté");
        return pet.Name + " porté : " + om;
    }

    /// <summary>
    /// Nourriture posée sur la case <paramref name="position"/> : paquet du client (<c>OM&lt;objet&gt;|&lt;case&gt;[|&lt;quantité&gt;]</c>),
    /// réponse <c>Im</c> attendue, puis le même message transmis par la session du bot avec son texte du client.
    /// </summary>
    private static async Task<string> FeedAsync(LiveSession s, ConcurrentQueue<ServerMessage> heard, Tool_BotProtocol.Game.Perso.Inventory.InventoryObjects food, int position, int? quantity,
        Func<string, bool> answer, string what, ServerMessageKind kind, Func<int, bool> numeric)
    {
        Check(food != null, "Objet à donner absent de l'inventaire");
        int mark = s.ReceivedCount, sent = s.SentCount, before = heard.Count;
        Check(await s.Account.Game.character.Inventory.MoveToSlot(food, position, quantity).ConfigureAwait(false), "Le bot refuse de poser " + food.Name + " sur la case " + position);
        string packet = "OM" + food.Inventory_ID + "|" + position + (quantity.HasValue ? "|" + quantity.Value : string.Empty);
        Check(s.SentSince(sent).Contains(packet), packet + " non envoyé : " + string.Join(", ", s.SentSince(sent)));
        string im = await s.Expect(answer, what, 10, mark).ConfigureAwait(false);
        Check(await s.Until(() => heard.Skip(before).Any(m => m.Kind == kind && m.NumericId.HasValue && numeric(m.NumericId.Value) && !string.IsNullOrEmpty(m.Text)), 5).ConfigureAwait(false),
            "La session du bot ne transmet pas " + im + " avec son texte");
        ServerMessage message = heard.Skip(before).First(m => m.Kind == kind && m.NumericId.HasValue && numeric(m.NumericId.Value));
        string others = string.Join(", ", s.ReceivedSince(mark).Where(p => p.StartsWith("OQ") || p.StartsWith("OR") || p.StartsWith("OCO") || p.StartsWith("Re+")).Select(p => Short(p, 18)));
        return packet + " → " + im + " « " + Short(message.Text, 50) + " »" + (others.Length > 0 ? " (" + others + ")" : string.Empty);
    }

    /// <summary>
    /// Échange de familiers chez un PNJ depuis l'action de son menu (<c>npc.xml</c>) : <c>ER9|&lt;pnj&gt;</c> (garde) ou
    /// <c>ER17|&lt;pnj&gt;</c> (résurrection), <c>ECK9|&lt;pnj&gt;</c> dans les deux cas ; <c>EMO+&lt;objet&gt;|1</c> par objet, proposition du
    /// PNJ <c>EmKO+&lt;modèle&gt;|1|&lt;modèle&gt;|…</c> et <c>EK1</c> ; validation refusée pendant le délai du client, puis <c>EK</c> → <c>EVa</c>.
    /// </summary>
    private static async Task<string> PetsTradeAsync(LiveSession s, PetsExchange pets, int action, NpcActor npc, uint[] offered, int expected, PetsService service)
    {
        int[] actions = NpcActions.ForTemplate(npc.TemplateId, out bool fromClient);
        Check(fromClient && actions.Contains(action), "Le menu du PNJ " + npc.TemplateId + " ne propose pas l'action " + action + " : " + string.Join(",", actions));
        int mark = s.ReceivedCount, sent = s.SentCount;
        var request = await s.Account.Game.Interactions.Npc.RequestActionAsync(action, (int)npc.Id).ConfigureAwait(false);
        Check(request.Sent, "Demande d'échange de familiers refusée par le bot : " + request.Message);
        string er = "ER" + (action == NpcActions.Pets ? ExchangeTypes.Pets : ExchangeTypes.PetsResurrection) + "|" + npc.Id;
        Check(s.SentSince(sent).Contains(er), er + " non envoyé : " + string.Join(", ", s.SentSince(sent)));
        string eck = await s.Expect("ECK", "ECK9 (échange de familiers)", 10, mark).ConfigureAwait(false);
        Check(eck == "ECK9|" + npc.Id, "Réponse inattendue à " + er + " : " + eck);
        string ereo = null;
        if (service == PetsService.Resurrection)
        {
            // Lot X4-CORR : StarLoco répond ECK9 puis EREO à tout ER17 (GameClient.request, branche « 17 » sans return) ; le bot
            // ignore ce EREO et l'échange reste ouvert.
            ereo = await s.Expect(p => p.StartsWith("ERE", StringComparison.Ordinal), "EREO (écart StarLoco après ER17)", 10, mark).ConfigureAwait(false);
            Check(ereo == "EREO", "Réponse inattendue après le ECK9 de " + er + " : " + ereo);
        }
        Check(await s.Until(() => pets.IsOpen && pets.Service == service && pets.NpcId == (int)npc.Id, 5).ConfigureAwait(false),
            "Le bot n'ouvre pas l'échange de familiers : ouvert " + pets.IsOpen + ", service " + pets.Service + ", PNJ " + pets.NpcId);
        foreach (uint id in offered)
        {
            int offerMark = s.ReceivedCount;
            var add = await pets.AddItemAsync(id, 1).ConfigureAwait(false);
            Check(add.Sent, "Proposition de " + id + " refusée par le bot : " + add.Message);
            await s.Expect("EMKO+" + id + "|1", "EMKO+" + id + "|1", 10, offerMark).ConfigureAwait(false);
        }
        string offer = await s.Expect(p => p.StartsWith("EmKO+" + expected + "|1|" + expected + "|", StringComparison.Ordinal), "EmKO+" + expected + " (proposition du PNJ)", 10, mark).ConfigureAwait(false);
        Check(await s.Until(() => pets.DistantReady && pets.DistantItems.Any(x => x.TemplateId == expected) && pets.LocalItems.Count == offered.Length, 5).ConfigureAwait(false),
            "Proposition du PNJ mal lue : prêt " + pets.DistantReady + ", objets " + string.Join(",", pets.DistantItems.Select(x => x.TemplateId)));
        var early = await pets.ValidateAsync().ConfigureAwait(false);
        Check(!early.Sent, "Validation acceptée avant le délai du client");
        Check(await s.Until(() => pets.CanValidate, 6).ConfigureAwait(false), "Validation toujours bloquée après le délai");
        int validated = s.ReceivedCount;
        var ok = await pets.ValidateAsync().ConfigureAwait(false);
        Check(ok.Sent && s.SentSince(sent).Contains("EK"), "EK non envoyé : " + ok.Message);
        string done = await s.Expect("EV", "EVa (échange effectué)", 10, validated).ConfigureAwait(false);
        Check(done == "EVa", "Échange de familiers refermé sans être effectué : " + done);
        string expectedLog = service == PetsService.Resurrection ? "Résurrection effectuée par " : "Garde effectuée par ";
        Check(await s.Until(() => !pets.IsOpen && pets.LastMessage.StartsWith(expectedLog, StringComparison.Ordinal), 5).ConfigureAwait(false),
            "Le bot n'a pas refermé l'échange : " + pets.IsOpen + " / " + pets.LastMessage);
        string received = string.Join(", ", s.ReceivedSince(validated).Where(p => p.StartsWith("EK") || p.StartsWith("OR") || p.StartsWith("OQ") || p.StartsWith("OAKO") || p.StartsWith("OCO") || p.StartsWith("Im021"))
            .Select(p => Short(p, 22)));
        return npc.DisplayName + " (modèle " + npc.TemplateId + ", actions " + string.Join(",", actions) + ") : " + er + " → " + eck + (ereo != null ? " puis " + ereo + " (ignoré)" : string.Empty) + ", "
            + string.Join(" + ", offered.Select(id => "EMO+" + id + "|1")) + " → " + Short(offer, 30) + " ; EK → " + received + ", " + done + " (« " + pets.LastMessage + " »)";
    }
}

// Lot CORR-LIVE : chaque scénario part d'un état connu (carte de départ, vie pleine, ni combat ni fenêtre ouverte, A et B sur
// deux cellules), quel que soit le résultat du précédent, et la suite évite deux courses de StarLoco décrites ci-dessous.
internal static partial class Live
{
    /// <summary>Délai de la remise en état qui précède chaque scénario (hors du délai du scénario lui-même).</summary>
    private const int PrepareSeconds = 60;

    /// <summary>
    /// Commandes de joueur de StarLoco (<c>CommandPlayer.analyse</c>, appelée pour tout message du canal général qui commence par un
    /// point) : « .start » téléporte hors combat en <see cref="StartCommandMap"/> cellule <see cref="StartCommandCell"/>, « .vie » rend
    /// toute la vie (<c>As</c>). Le client officiel les envoie de la même façon (<c>BM*|.start|</c>). Le canal général refuse un
    /// message moins de 500 ms après le précédent (<c>M10</c>) : <see cref="CommandGapMs"/> avant et après chaque commande.
    /// </summary>
    private const string StartCommand = ".start", LifeCommand = ".vie";
    private const int StartCommandMap = 164, StartCommandCell = 298, CommandGapMs = 700;

    private static bool InGame(LiveSession s) =>
        s?.Account?.Connexion != null && s.Account.Connexion.IsConnected() && s.Account.Game?.character != null && s.Account.Game.Map?.Self != null;

    private static Cell OwnCell(LiveSession s) => s.Map.Self == null ? null : s.Map.GetCellFromId((short)s.Map.Self.CellId);

    /// <summary>
    /// Remise en état de A et B avant un scénario : échange et combat abandonnés (<see cref="RecoverAsync"/>), dialogue, boutique ou
    /// zaap refermés, retour sur la carte de départ et sur une cellule praticable (<see cref="ReturnToStartAsync"/>), vie pleine
    /// (<see cref="RestoreLifeAsync"/>), puis A et B sur deux cellules (<see cref="SeparateAsync"/>). Renvoie ce qui a été fait, ou null.
    /// </summary>
    private static async Task<string> PrepareAsync()
    {
        var done = new List<string>();
        await RecoverAsync().ConfigureAwait(false);
        foreach (LiveSession s in new[] { a, b })
        {
            if (!InGame(s)) continue;
            string closed = await CloseWindowsAsync(s).ConfigureAwait(false);
            if (closed != null) done.Add(closed);
            if (mapsAvailable && startMap > 0)
            {
                Cell own = OwnCell(s);
                if (s.MapId != startMap) done.Add(await ReturnToStartAsync(s, "sur la carte " + s.MapId).ConfigureAwait(false));
                else if (own == null || !own.IsWalkable()) done.Add(await ReturnToStartAsync(s, "cellule " + (own?.CellID ?? -1) + " non praticable").ConfigureAwait(false));
            }
            string life = await RestoreLifeAsync(s).ConfigureAwait(false);
            if (life != null) done.Add(life);
        }
        string apart = await SeparateAsync().ConfigureAwait(false);
        if (apart != null) done.Add(apart);
        if (done.Count == 0) return null;
        Journal("[test] remise en état : " + string.Join(" ; ", done));
        return string.Join(", ", done);
    }

    /// <summary>Dialogue de PNJ, boutique ou zaap restés ouverts par un scénario en échec : <c>LeaveAsync</c> de chacun.</summary>
    private static async Task<string> CloseWindowsAsync(LiveSession s)
    {
        var closed = new List<string>();
        InteractionsClass interactions = s.Account.Game.Interactions;
        if (interactions.Npc.IsOpen && (await interactions.Npc.LeaveAsync().ConfigureAwait(false)).Sent) closed.Add("dialogue");
        if (interactions.Shop.IsOpen && (await interactions.Shop.LeaveAsync().ConfigureAwait(false)).Sent) closed.Add("boutique");
        if (interactions.Zaap.IsOpen && (await interactions.Zaap.LeaveAsync().ConfigureAwait(false)).Sent) closed.Add("zaap");
        if (closed.Count == 0) return null;
        Check(await s.Until(() => !interactions.Npc.IsOpen && !interactions.Shop.IsOpen && !interactions.Zaap.IsOpen, 10).ConfigureAwait(false),
            s.Label + " : fenêtre restée ouverte après LeaveAsync (" + string.Join(", ", closed) + ")");
        return s.Label + " ferme " + string.Join(", ", closed);
    }

    /// <summary>Message du canal général qui est une commande de joueur de StarLoco, espacé des autres messages du canal.</summary>
    private static async Task CommandAsync(LiveSession s, string command)
    {
        await Task.Delay(CommandGapMs).ConfigureAwait(false);
        var sent = await s.Account.Game.Chat.SendAsync('*', command).ConfigureAwait(false);
        Check(sent.Sent, s.Label + " : « " + command + " » refusée par le bot : " + sent.Message);
    }

    /// <summary>
    /// Retour sur la carte de départ : « .start » quand elle est la carte de cette commande (sinon à pied par les déclencheurs).
    /// Seul moyen de quitter une cellule non praticable où StarLoco a posé le personnage (voir <see cref="ReturnAfterFightAsync"/>).
    /// </summary>
    private static async Task<string> ReturnToStartAsync(LiveSession s, string reason)
    {
        int from = s.MapId, cell = s.Map.Self?.CellId ?? -1;
        if (startMap != StartCommandMap)
        {
            List<Trigger> route = TriggerRoute(s.MapId, startMap);
            Check(route != null, s.Label + " : carte de départ " + startMap + " injoignable à pied depuis la carte " + s.MapId + " (" + reason + ")");
            foreach (Trigger step in route) await WalkThroughTrigger(s, step).ConfigureAwait(false);
            return s.Label + " revient à pied de la carte " + from + " (" + reason + ")";
        }
        int mark = s.ReceivedCount;
        await CommandAsync(s, StartCommand).ConfigureAwait(false);
        if (from != StartCommandMap)
            await s.Expect("GDM|" + StartCommandMap, "GDM|" + StartCommandMap + " après « " + StartCommand + " »", 20, mark).ConfigureAwait(false);
        Check(await s.Until(() => s.MapId == StartCommandMap && s.Map.Self != null && s.Map.Self.CellId == StartCommandCell, 10).ConfigureAwait(false),
            s.Label + " : « " + StartCommand + " » ne ramène pas en " + StartCommandMap + " cellule " + StartCommandCell + " (carte " + s.MapId + ", cellule " + s.Map.Self?.CellId + ")");
        await Task.Delay(CommandGapMs).ConfigureAwait(false);
        return s.Label + " « " + StartCommand + " » (" + reason + ") : carte " + from + " cellule " + cell + " → carte " + StartCommandMap + " cellule " + StartCommandCell;
    }

    /// <summary>« .vie » si le personnage n'a pas toute sa vie (défaite : StarLoco la remet à 1 ; assis, il n'en rend qu'un point par seconde).</summary>
    private static async Task<string> RestoreLifeAsync(LiveSession s)
    {
        Func<int> life = () => s.Account.Game.character.stats.VitalityActual;
        int max = s.Account.Game.character.stats.MaxVitality, before = life();
        if (max <= 0 || before >= max) return null;
        await CommandAsync(s, LifeCommand).ConfigureAwait(false);
        Check(await s.Until(() => life() >= max, 10).ConfigureAwait(false), s.Label + " : « " + LifeCommand + " » sans effet : " + life() + "/" + max + " PV");
        await Task.Delay(CommandGapMs).ConfigureAwait(false);
        return s.Label + " « " + LifeCommand + " » : " + before + " → " + life() + "/" + max + " PV";
    }

    /// <summary>
    /// A et B sur la même cellule : B fait quelques pas. StarLoco range les joueurs de chaque cellule dans une <c>ArrayList</c> non
    /// synchronisée (<c>GameCase.players</c>), modifiée par le fil de session de chaque joueur : deux retraits simultanés sur la même
    /// cellule (deux marches qui en partent, deux téléportations depuis le même déclencheur, deux déconnexions) peuvent y laisser un
    /// <c>null</c>, deux ajouts simultanés (fins de combat) y perdre un joueur. Avec un <c>null</c>, toute téléportation depuis la carte
    /// lève une <c>NullPointerException</c> dans <c>GAME_SEND_ERASE_ON_MAP_TO_MAP</c> après <c>GA;2</c> et <c>GM|-</c>, sans <c>GDM</c>,
    /// jusqu'au redémarrage du Game. Deux clients officiels qui agissent dans la même milliseconde subiraient la même course.
    /// </summary>
    private static async Task<string> SeparateAsync()
    {
        if (!mapsAvailable || !InGame(a) || !InGame(b) || a.MapId != b.MapId || a.Map.Self.CellId != b.Map.Self.CellId) return null;
        int shared = b.Map.Self.CellId;
        Cell target = FreeCellNear(b, 2, 6);
        Check(target != null, "Aucune cellule libre près de B pour quitter la cellule " + shared + " de A");
        int sentMark = b.SentCount;
        var moved = await b.Account.Game.Manager.Mouvements.MoveToAsync(target).ConfigureAwait(false);
        Check(moved == Tool_BotProtocol.Game.Managers.Mouvements.MoveResults.EXIT, "B : pas de côté refusé par le bot : " + moved);
        Check(await b.Until(() => b.SentSince(sentMark).Any(p => p.StartsWith("GKK", StringComparison.Ordinal)) && b.Map.Self?.CellId == target.CellID, 15).ConfigureAwait(false),
            "B n'a pas quitté la cellule " + shared + " (cellule " + b.Map.Self?.CellId + ", attendu " + target.CellID + ")");
        await Task.Delay(400).ConfigureAwait(false);
        return "B quitte la cellule " + shared + " de A pour la cellule " + target.CellID;
    }

    /// <summary>
    /// Retour sur la carte <paramref name="home"/> après un combat sur une carte voisine : à pied par <paramref name="back"/>, sauf si
    /// StarLoco a posé le personnage sur une cellule non praticable. <c>GameClient.actionAck</c> ne retire jamais de la liste des actions
    /// du joueur les déplacements faits en combat (il rend la main avant <c>removeAction</c>) ; quand le sort ou le coup qui finit le
    /// combat est suivi de <c>GE</c> puis de <c>GAF0</c>, le <c>GKK0</c> de réponse est traité hors combat comme la fin du vieux
    /// déplacement 0 d'un combat précédent : le personnage est mis sur la dernière cellule de ce chemin, sur la carte courante (cellule
    /// 181 de la carte 765 après le défi du scénario e, non praticable et isolée). Le client officiel envoie le même <c>GKK</c> (il reste
    /// « en combat » jusqu'à l'affichage du résultat) ; un joueur bloqué ainsi tape « .start ».
    /// </summary>
    private static async Task<string> ReturnAfterFightAsync(LiveSession s, int home, Trigger back)
    {
        if (s.MapId == home) return null;
        Cell own = OwnCell(s);
        if (own != null && !own.IsWalkable())
            return await ReturnToStartAsync(s, "cellule " + own.CellID + " de la carte " + s.MapId + " non praticable après le combat").ConfigureAwait(false);
        if (back != null && s.MapId == back.MapId)
        {
            await WalkThroughTrigger(s, back).ConfigureAwait(false);
            return s.Label + " revient par le déclencheur " + back.CellId + " de la carte " + back.MapId;
        }
        await GoHome(s).ConfigureAwait(false);
        return s.Label + " sur la carte " + s.MapId + " après le combat";
    }
}

// Scénarios x32a et x32b (lot X32) : potion de changement de nom en jeu (manque B-07) et roulette du Bouftou (mini-jeu TC/TV, manque V-45).
internal static partial class Live
{
    /// <summary>
    /// Potion de changement de nom : objet 10860 (type 12), action d'objet 12 de StarLoco (<c>ObjectAction</c> : <c>setChangeName(true)</c>
    /// → <c>AlEr</c>, potion gardée, hors combat). Roulette du Bouftou : PNJ 408 de la carte 3336 (cellule 198), question 1646, réponse
    /// 1273 « Payer 100 kamas » → action 36 « 100;30 » (<c>Action.java</c>) : 100 kamas retirés (<c>As</c>, <c>Im046;100</c>), dialogue
    /// clos (<c>DV</c>), puis <c>TC30|7001010000</c> 1,5 s plus tard (<c>TC31</c> une fois sur 200) avec le joueur « absent » et
    /// <c>IN_TUTORIAL</c> jusqu'à son <c>TV</c> ; un <c>DC</c> pendant ce temps recevrait <c>ERES</c>.
    /// </summary>
    private const int NamePotionTemplate = 10860, RouletteMap = 3336, RouletteNpcTemplate = 408, RouletteNpcCell = 198, RouletteQuestion = 1646,
        RouletteAnswer = 1273, RoulettePrice = 100, RouletteKamas = 1000;

    /// <summary>
    /// Nom de personnage absent de la base, lu par StarLoco (première lettre « N », hors des préfixes de paquets) et valide pour lui
    /// (lettres seules, voyelles et consonnes alternées : aucune lettre triplée).
    /// </summary>
    private static string FreeCharacterName(MySql.Data.MySqlClient.MySqlConnection db, string schema)
    {
        const string consonants = "lmnrstvz", vowels = "aeiou";
        for (int i = 0; i < 20; i++)
        {
            var name = new System.Text.StringBuilder("N");
            for (int k = 0; k < 6; k++) name.Append(k % 2 == 0 ? vowels[random.Next(vowels.Length)] : consonants[random.Next(consonants.Length)]);
            string candidate = name.ToString();
            if (!NameChange.IsReadByStarLoco(candidate)) continue;
            if (Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM " + schema + ".players WHERE name = @name", "@name", candidate), CultureInfo.InvariantCulture) == 0) return candidate;
        }
        throw new Exception("aucun nom de personnage libre trouvé dans la base");
    }

    private static Task TypeNameAsync(ChooseNickNameForm dialog, string name) =>
        OnUi(() => dialog.GetType().GetProperty("Nickname", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(dialog, name, null));

    private static async Task PressOkAsync(ChooseNickNameForm dialog) =>
        await (await OnUi(() => (Task)dialog.GetType().GetMethod("OkAsync", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(dialog, null)).ConfigureAwait(false)).ConfigureAwait(false);

    private static GameClientFullform OpenScreen(LiveSession s) =>
        new GameClientFullform(s.Account) { StartPosition = FormStartPosition.Manual, Location = new Point(60, 60), Size = new Size(1000, 700), ShowInTaskbar = false };

    /// <summary>
    /// Changement de nom en jeu : un compte et un personnage inventés reçoivent par la base la potion 10860 et ont leur fenêtre de jeu.
    /// « Utiliser » (<c>OU</c>) → <c>AlEr</c> : la fenêtre <c>ChooseNickName</c> s'ouvre au-dessus de la fenêtre de jeu, la potion reste
    /// dans le sac. Le nom de A (préfixe « Essai- » : « E » est un préfixe de paquet de StarLoco) est refusé sur place ; « Nom1 » (chiffre)
    /// part en ligne brute et revient <c>AlEs</c>, la saisie rouvre avec <c>NICKNAME_ALREADY_USED</c> ; un nom libre part et revient
    /// <c>AlEr</c> (StarLoco le renvoie malgré la réussite), la potion est retirée (<c>OR</c>) et le <c>GM|~</c> du personnage porte le
    /// nouveau nom : la fenêtre se ferme, le bot connaît le nouveau nom, la session n'est jamais coupée. Tout est supprimé de la base.
    /// </summary>
    private static async Task<string> RenameAsync()
    {
        if (!Map.AllBotMaps.ContainsKey(TombMap)) throw new SkipScenario("carte " + TombMap + " absente des cartes du bot");
        using (MySql.Data.MySqlClient.MySqlConnection db = OpenAccountsDatabase(out string schema))
        {
            var created = new List<InventedCharacter>();
            LiveSession c = null;
            GameClientFullform screen = null;
            int potionId = 1900000000 + random.Next(0, 99999999);
            bool potionWritten = false;
            try
            {
                InventedCharacter hero = Invent(db, schema, a.Id, TombMap, TombCell, 10000, created);
                Sql(db, "INSERT INTO " + schema + ".`world.entity.objects` (id, template, quantity, position, stats, puit) VALUES (@id, @template, 1, -1, '', 0)",
                    "@id", potionId, "@template", NamePotionTemplate);
                potionWritten = true;
                Sql(db, "UPDATE " + schema + ".players SET objets = @objets WHERE id = @id AND account = @account",
                    "@objets", potionId.ToString(CultureInfo.InvariantCulture) + "|", "@id", hero.Id, "@account", hero.Guid);
                Journal("[test] potion de changement de nom " + NamePotionTemplate + " (objet " + potionId + ") dans le sac de " + hero.Name);
                c = new LiveSession("C", hero.Login, hero.Password);
                var requests = new List<NicknameRequest>(); var accepted = new List<string>(); var renamed = new List<string>();
                int lost = 0;
                string entered = await EnterExistingAsync(c, hero.Id, hero.Name, TombMap, s =>
                {
                    s.Account.Game.Session.NameChange.Requested += request => { lock (requests) requests.Add(request); };
                    s.Account.Game.Session.NameChange.Accepted += name => { lock (accepted) accepted.Add(name); };
                    s.Account.Game.Session.CharacterRenamed += name => { lock (renamed) renamed.Add(name); };
                    s.Account.ConnectionLost += loss => Interlocked.Increment(ref lost);
                    s.Account.Game.character.Player_Selection += () => OnUi(() => { if (screen == null) { screen = OpenScreen(s); screen.Show(); } });
                }).ConfigureAwait(false);
                Tool_BotProtocol.Game.Perso.Inventory.InventoryObjects potion = null;
                Check(await c.Until(() => (potion = Owned(c, NamePotionTemplate)) != null, 10).ConfigureAwait(false), "La potion " + NamePotionTemplate + " manque à l'inventaire lu par le bot (ASK)");
                Check(await WaitOnUi(() => screen != null && screen.IsHandleCreated && screen.Visible ? screen : null, 20).ConfigureAwait(false) != null, "La fenêtre de jeu de C ne s'est pas ouverte");
                object prompts = await OnUi(() => Field(screen, "connectionPrompts")).ConfigureAwait(false);
                Check(prompts != null, "La fenêtre de jeu n'a pas de ConnectionPrompts");
                Check(await OnUi(() => Member(prompts, "NameForm") == null).ConfigureAwait(false), "Une fenêtre du nom est ouverte avant la potion");
                NameChange change = c.Account.Game.Session.NameChange;

                // 1. « Utiliser » : OU<objet>| → AlEr (Player.setChangeName) ; la potion reste dans le sac ; fenêtre du nom sur la fenêtre de jeu.
                int mark = c.ReceivedCount, sent = c.SentCount;
                var used = await Tool_BotProtocol.Game.Perso.Inventory.SpecialItems.UseAsync(c.Account.Game, potion).ConfigureAwait(false);
                Check(used.Sent, "« Utiliser » refusé par le bot : " + used.Message);
                Check(await c.Until(() => c.SentSince(sent).Any(p => p.StartsWith("OU" + potionId + "|", StringComparison.Ordinal)), 5).ConfigureAwait(false), "OU" + potionId + "| non envoyé");
                await c.Expect(p => p == "AlEr", "AlEr (potion de changement de nom)", 15, mark).ConfigureAwait(false);
                Check(await c.Until(() => Count(requests) == 1 && change.IsPending, 5).ConfigureAwait(false), "AlEr n'a pas ouvert la demande de nom du bot");
                Check(Last(requests).Attempt == 1 && Last(requests).Refused == null && Last(requests).Error == null, "Première demande : " + Describe(Last(requests)));
                var dialog = await WaitOnUi(() => Member(prompts, "NameForm") as ChooseNickNameForm, 15).ConfigureAwait(false);
                Check(dialog != null, "AlEr n'a pas ouvert la fenêtre du nom au-dessus de la fenêtre de jeu");
                string[] opened = await OnUi(() => new[] { Member(dialog, "Step").ToString(), (string)Member(dialog, "HelpText"), ReferenceEquals(dialog.Owner, screen).ToString(), dialog.Visible.ToString() }).ConfigureAwait(false);
                Check(opened[0] == "Input" && opened[1] == ConnectionTexts.NicknameHelp && opened[2] == "True" && opened[3] == "True",
                    "Fenêtre du nom mal ouverte : étape " + opened[0] + ", aide « " + opened[1] + " », au-dessus de la fenêtre " + opened[2] + ", visible " + opened[3]);
                Check(Owned(c, NamePotionTemplate) != null, "La potion a disparu du sac avant le nom");

                // 2. Refus local : le nom de A commence par une lettre que StarLoco lit comme un préfixe de paquet (« E » de « Essai- »).
                string unreadable = a.Account.Game.character.Name;
                if (string.IsNullOrEmpty(unreadable) || NameChange.IsReadByStarLoco(unreadable)) unreadable = "Gaston";
                await TypeNameAsync(dialog, unreadable).ConfigureAwait(false);
                await PressOkAsync(dialog).ConfigureAwait(false);
                string[] refusedLocally = await OnUi(() => new[] { Member(dialog, "Step").ToString(), (string)Member(dialog, "ErrorText") }).ConfigureAwait(false);
                Check(refusedLocally[0] == "Input" && refusedLocally[1] == NameChange.UnreadableByServer(unreadable),
                    "Nom « " + unreadable + " » : étape " + refusedLocally[0] + ", erreur « " + refusedLocally[1] + " »");
                Check(!c.SentSince(sent).Any(p => p == unreadable), "Le nom refusé localement est parti au serveur");

                // 3. Nom invalide pour StarLoco (chiffre) : OK, confirmation, OK → ligne brute → AlEs → saisie rouverte avec NICKNAME_ALREADY_USED.
                const string invalid = "Nom1";
                await TypeNameAsync(dialog, invalid).ConfigureAwait(false);
                await PressOkAsync(dialog).ConfigureAwait(false);
                string[] confirm = await OnUi(() => new[] { Member(dialog, "Step").ToString(), (string)Member(dialog, "HelpText") }).ConfigureAwait(false);
                Check(confirm[0] == "Confirm" && confirm[1] == ConnectionTexts.NicknameConfirm(invalid), "Confirmation : étape " + confirm[0] + ", texte « " + confirm[1] + " »");
                int before = c.ReceivedCount;
                await PressOkAsync(dialog).ConfigureAwait(false);
                Check(await c.Until(() => c.SentSince(sent).Any(p => p == invalid), 5).ConfigureAwait(false), "Le nom « " + invalid + " » n'est pas parti en ligne brute");
                string refusal = await c.Expect(p => p == "AlEs" || p == "AlEr", "AlEs (nom refusé)", 15, before).ConfigureAwait(false);
                Check(refusal == "AlEs", "StarLoco a répondu " + refusal + " au lieu de AlEs pour « " + invalid + " »");
                Check(await c.Until(() => Count(requests) == 2, 5).ConfigureAwait(false) && Last(requests).Refused == invalid && Last(requests).Error == ConnectionTexts.NicknameAlreadyUsed,
                    "Deuxième demande : " + Describe(Last(requests)));
                string reopened = await WaitOnUi(() => Member(dialog, "Step").ToString() == "Input" && (string)Member(dialog, "ErrorText") == ConnectionTexts.NicknameAlreadyUsed
                    ? (string)Member(dialog, "Nickname") : null, 10).ConfigureAwait(false);
                Check(reopened == invalid, "AlEs n'a pas rouvert la saisie avec NICKNAME_ALREADY_USED et le nom refusé (« " + reopened + " »)");
                Check(Owned(c, NamePotionTemplate) != null, "La potion a été consommée par un nom refusé");

                // 4. Nom libre : ligne brute → AlEr (renvoyé malgré la réussite), OR (potion retirée), GM|~ au nouveau nom → fenêtre fermée.
                string chosen = FreeCharacterName(db, schema);
                await TypeNameAsync(dialog, chosen).ConfigureAwait(false);
                await PressOkAsync(dialog).ConfigureAwait(false);
                before = c.ReceivedCount;
                await PressOkAsync(dialog).ConfigureAwait(false);
                Check(await c.Until(() => c.SentSince(sent).Any(p => p == chosen), 5).ConfigureAwait(false), "Le nom « " + chosen + " » n'est pas parti en ligne brute");
                string answer = await c.Expect(p => p == "AlEr" || p == "AlEs", "AlEr (nom accepté)", 15, before).ConfigureAwait(false);
                Check(answer == "AlEr", "StarLoco a répondu " + answer + " pour le nom libre « " + chosen + " »");
                await c.Expect(p => p.StartsWith("GM|~", StringComparison.Ordinal) && p.Contains(";" + hero.Id + ";" + chosen + ";"), "GM|~ au nouveau nom", 15, before).ConfigureAwait(false);
                Check(await c.Until(() => Count(accepted) == 1 && !change.IsPending && c.Account.Game.character.Name == chosen, 5).ConfigureAwait(false),
                    "Le GM|~ au nouveau nom n'a pas clos la demande (accepté " + Count(accepted) + ", en attente " + change.IsPending + ", nom « " + c.Account.Game.character.Name + " »)");
                Check(Count(renamed) == 1 && Last(renamed) == chosen && Last(accepted) == chosen, "CharacterRenamed / Accepted : " + string.Join(",", Snapshot(renamed)) + " / " + string.Join(",", Snapshot(accepted)));
                Check(Count(requests) == 3 && Last(requests).Refused == chosen && Last(requests).Error == null, "AlEr de réussite : " + Describe(Last(requests)));
                Check(await WaitOnUi(() => dialog.IsDisposed && Member(prompts, "NameForm") == null ? "fermée" : null, 10).ConfigureAwait(false) != null,
                    "La fenêtre du nom reste ouverte après le GM|~ au nouveau nom");
                bool removed = await c.Until(() => Owned(c, NamePotionTemplate) == null, 5).ConfigureAwait(false);
                string removal = c.ReceivedSince(before).FirstOrDefault(p => p.StartsWith("OR", StringComparison.Ordinal) || p.StartsWith("OQ", StringComparison.Ordinal));
                Check(lost == 0 && c.Account.Connexion != null && c.Account.Connexion.IsConnected(), "Le changement de nom a déconnecté C");
                string stored = Scalar(db, "SELECT name FROM " + schema + ".players WHERE id = @id", "@id", hero.Id) as string;
                return entered + " ; OU → AlEr, fenêtre du nom sur la fenêtre de jeu ; « " + unreadable + " » refusé sur place ; « " + invalid + " » → AlEs, saisie rouverte ; « "
                    + chosen + " » → AlEr, " + (removal == null ? "(ni OR ni OQ)" : Short(removal, 16)) + ", GM|~ au nouveau nom, fenêtre fermée, potion "
                    + (removed ? "retirée du sac" : "encore dans le sac") + ", jamais déconnecté ; base « " + stored + " »";
            }
            finally
            {
                if (screen != null) await OnUi(() => { if (!screen.IsDisposed) { screen.Close(); screen.Dispose(); } }).ConfigureAwait(false);
                await ForgetAsync(db, schema, new[] { c }, created).ConfigureAwait(false);
                if (potionWritten)
                    try { Sql(db, "DELETE FROM " + schema + ".`world.entity.objects` WHERE id = @id AND template = @template", "@id", potionId, "@template", NamePotionTemplate); }
                    catch (Exception error) { Journal("!! [test] potion " + potionId + " non supprimée de la base : " + error.Message); }
            }
        }
    }

    /// <summary>
    /// Roulette du Bouftou : un compte et un personnage inventés (1000 kamas) posés sur la carte 3336 avec leur fenêtre de jeu. <c>DC</c>
    /// au PNJ 408 → <c>DCK</c>, <c>DQ1646</c> ; réponse 1273 → <c>Im046;100</c>, 100 kamas de moins, <c>DV</c>, puis <c>TC30|7001010000</c>
    /// (ou <c>TC31</c>) : le bot envoie aussitôt <c>TV&lt;rang&gt;|&lt;cellule&gt;|&lt;orientation&gt;</c> et sa fenêtre ouvre la boîte
    /// « mini-jeu » ; un second <c>DC</c> obtient <c>DCK</c> (joueur libéré du mini-jeu) et non <c>ERES</c>. Tout est supprimé de la base.
    /// </summary>
    private static async Task<string> RouletteAsync()
    {
        if (!Map.AllBotMaps.TryGetValue(RouletteMap, out Map map)) throw new SkipScenario("carte " + RouletteMap + " absente des cartes du bot");
        if (!map.HasMapData)
            try { map.DecompressMap(map.MapData); }
            catch (Exception error) { throw new SkipScenario("carte " + RouletteMap + " sans cellules lisibles : " + error.Message); }
        Cell npcCell = map.GetCellFromId((short)RouletteNpcCell);
        Cell start = map.MapCells.Where(cell => cell != null && cell.IsWalkable() && cell.CellID != RouletteNpcCell)
            .OrderBy(cell => npcCell == null ? 0 : cell.GetDistanceBetweenCells(npcCell)).ThenBy(cell => cell.CellID).FirstOrDefault();
        Check(start != null, "aucune cellule praticable sur la carte " + RouletteMap);
        using (MySql.Data.MySqlClient.MySqlConnection db = OpenAccountsDatabase(out string schema))
        {
            var created = new List<InventedCharacter>();
            LiveSession c = null;
            GameClientFullform screen = null;
            try
            {
                InventedCharacter player = Invent(db, schema, a.Id, RouletteMap, start.CellID, 10000, created);
                Sql(db, "UPDATE " + schema + ".players SET kamas = @kamas WHERE id = @id AND account = @account", "@kamas", RouletteKamas, "@id", player.Id, "@account", player.Guid);
                c = new LiveSession("C", player.Login, player.Password);
                var started = new List<TutorialRequest>();
                int lost = 0;
                string entered = await EnterExistingAsync(c, player.Id, player.Name, RouletteMap, s =>
                {
                    s.Account.Game.Session.Tutorial.Started += request => { lock (started) started.Add(request); };
                    s.Account.ConnectionLost += loss => Interlocked.Increment(ref lost);
                    s.Account.Game.character.Player_Selection += () => OnUi(() => { if (screen == null) { screen = OpenScreen(s); screen.Show(); } });
                }).ConfigureAwait(false);
                Check(await WaitOnUi(() => screen != null && screen.IsHandleCreated && screen.Visible ? screen : null, 20).ConfigureAwait(false) != null, "La fenêtre de jeu de C ne s'est pas ouverte");
                string miniGameName = "mini-jeu/" + player.Login;
                Check(await OnUi(() => OpenDialog(miniGameName) == null).ConfigureAwait(false), "Une boîte « mini-jeu » est ouverte avant la roulette");
                Check(await c.Until(() => c.Account.Game.character.Kamas == RouletteKamas, 10).ConfigureAwait(false), "Kamas lus " + c.Account.Game.character.Kamas + " au lieu de " + RouletteKamas);
                NpcActor npc = await FindNpcAsync(c, new[] { RouletteNpcTemplate }).ConfigureAwait(false);
                NpcDialog dialog = c.Account.Game.Interactions.Npc;

                // DC → DCK, DQ1646 ; DR1646|1273 → Im046;100, DV, puis TC 1,5 s plus tard.
                int mark = c.ReceivedCount, sent = c.SentCount;
                var open = await dialog.OpenAsync((int)npc.Id).ConfigureAwait(false);
                Check(open.Sent, "DC refusé par le bot : " + open.Message);
                string dck = await c.Expect(p => p.StartsWith("DCK", StringComparison.Ordinal) || p.StartsWith("ERE", StringComparison.Ordinal), "DCK", 10, mark).ConfigureAwait(false);
                Check(dck.StartsWith("DCK", StringComparison.Ordinal), "Le PNJ " + RouletteNpcTemplate + " répond " + dck);
                string dq = await c.Expect("DQ", "DQ (question du PNJ)", 10, mark).ConfigureAwait(false);
                Check(await c.Until(() => dialog.IsOpen && dialog.QuestionId == RouletteQuestion && dialog.AnswerIds.Contains(RouletteAnswer), 5).ConfigureAwait(false),
                    "Question " + dialog.QuestionId + " (" + Short(dq, 40) + ") au lieu de " + RouletteQuestion + " avec la réponse " + RouletteAnswer);
                int answered = c.ReceivedCount;
                var answer = await dialog.AnswerAsync(RouletteAnswer).ConfigureAwait(false);
                Check(answer.Sent, "DR refusé par le bot : " + answer.Message);
                string im = await c.Expect(p => p.StartsWith("Im046;", StringComparison.Ordinal), "Im046 (kamas payés)", 10, answered).ConfigureAwait(false);
                string tc = await c.Expect(p => p.StartsWith("TC", StringComparison.Ordinal), "TC (mini-jeu)", 15, answered).ConfigureAwait(false);
                Check(tc == "TC30|7001010000" || tc == "TC31|7001010000", "TC inattendu : " + tc);
                int rank = tc.StartsWith("TC31", StringComparison.Ordinal) ? 1 : 0;
                Check(await c.Until(() => c.SentSince(sent).Any(p => p.StartsWith("TV", StringComparison.Ordinal)), 5).ConfigureAwait(false), "TV non envoyé après " + tc);
                string tv = c.SentSince(sent).First(p => p.StartsWith("TV", StringComparison.Ordinal));
                string expectedEnd = "TV" + rank + "|" + c.Account.Game.character.Cell.CellID + "|" + c.Account.Game.character.Orientation;
                Check(tv == expectedEnd, "Fin envoyée " + tv + " au lieu de " + expectedEnd);
                Check(Count(started) == 1 && Last(started).Id == 30 + rank && Last(started).EndPacket == tv && Last(started).Name == "Roulette du Bouftou",
                    "Started : " + Count(started) + " mini-jeu(x), dernier " + (Last(started) == null ? "(aucun)" : Last(started).Id + " " + Last(started).EndPacket));
                Form box = await WaitOnUi(() => OpenDialog(miniGameName), 10).ConfigureAwait(false);
                Check(box != null, "TC n'a pas ouvert la boîte « mini-jeu » dans la fenêtre de C");
                string[] shown = await OnUi(() => new[] { box.Text, DialogMessage(box), ReferenceEquals(box.Owner, screen).ToString() }).ConfigureAwait(false);
                Check(shown[0] == ConnectionTexts.InformationsTitle && shown[1] == Last(started).Describe() && shown[2] == "True",
                    "Boîte « " + shown[0] + " » : « " + shown[1] + " », au-dessus de la fenêtre de C " + shown[2]);
                bool paid = await c.Until(() => c.Account.Game.character.Kamas == RouletteKamas - RoulettePrice, 5).ConfigureAwait(false);
                // L'action 36 rend « quitter » : StarLoco clôt le dialogue (DV) avant TC ; sinon le bot le quitte lui-même.
                if (!await c.Until(() => !dialog.IsOpen, 5).ConfigureAwait(false)) { await dialog.LeaveAsync().ConfigureAwait(false); await c.Until(() => !dialog.IsOpen, 5).ConfigureAwait(false); }

                // Joueur libéré par TV : un nouveau DC obtient DCK (ERES tant que l'action IN_TUTORIAL reste posée).
                int again = c.ReceivedCount;
                var reopen = await dialog.OpenAsync((int)npc.Id).ConfigureAwait(false);
                Check(reopen.Sent, "Second DC refusé par le bot : " + reopen.Message);
                string freed = await c.Expect(p => p.StartsWith("DCK", StringComparison.Ordinal) || p.StartsWith("ERE", StringComparison.Ordinal), "DCK (joueur libéré)", 10, again).ConfigureAwait(false);
                Check(freed.StartsWith("DCK", StringComparison.Ordinal), "Après " + tv + ", le PNJ répond " + freed + " : le joueur est resté dans le mini-jeu");
                await c.Expect("DQ", "DQ (seconde question)", 10, again).ConfigureAwait(false);
                Check(await c.Until(() => dialog.IsOpen, 5).ConfigureAwait(false), "Le second dialogue ne s'est pas ouvert chez le bot");
                var leave = await dialog.LeaveAsync().ConfigureAwait(false);
                Check(leave.Sent, "DV refusé par le bot : " + leave.Message);
                await c.Expect(p => p == "DV", "DV (fin du second dialogue)", 10, again).ConfigureAwait(false);
                await OnUi(() => AllControls(box).OfType<Button>().First(x => x.Visible).PerformClick()).ConfigureAwait(false);
                Check(await WaitOnUi(() => OpenDialog(miniGameName) == null ? "fermée" : null, 5).ConfigureAwait(false) != null, "« OK » ne ferme pas la boîte « mini-jeu »");
                Check(lost == 0 && c.Account.Connexion != null && c.Account.Connexion.IsConnected(), "La roulette a déconnecté C");
                return entered + " ; DC → " + Short(dck, 8) + ", DQ" + RouletteQuestion + ", DR → " + Short(im, 12) + ", kamas " + (paid ? (RouletteKamas - RoulettePrice) + " (As)" : c.Account.Game.character.Kamas + " (pas de As lu)")
                    + ", " + tc + " → " + tv + " (" + (rank == 1 ? "gagné" : "perdu") + "), boîte « " + shown[0] + " » : « " + shown[1].Replace("\n", " ") + " », second DC → " + Short(freed, 8)
                    + " (joueur libéré), DV, OK ferme la boîte";
            }
            finally
            {
                if (screen != null) await OnUi(() => { if (!screen.IsDisposed) { screen.Close(); screen.Dispose(); } }).ConfigureAwait(false);
                await ForgetAsync(db, schema, new[] { c }, created).ConfigureAwait(false);
            }
        }
    }
}

// Scénario x5 (lot X5) : artisanat sécurisé entre deux comptes inventés, l'artisan à côté d'une forge.
internal static partial class Live
{
    /// <summary>
    /// Forge : objet interactif <c>gfx</c> 7012 des cartes (StarLoco lit l'identifiant de l'objet de la couche 2 marqué interactif,
    /// <c>CryptManager.decompileMapData</c>) ; <c>jobs_data.crafts</c> du forgeur d'épées 11 vaut « 7012;20,145 » (compétence 20 à cette forge),
    /// son outil est le marteau 494 et la recette de la petite épée de Boisaille 40 (table <c>crafts</c>) est « 303*2;473*2 ».
    /// </summary>
    private const int ForgeGfx = 7012, SwordSmithJob = 11, ForgeSwordSkill = 20, SwordSmithHammer = 494, AshWood = 303, ChestnutWood = 473, SmallWoodSword = 40;
    /// <summary>
    /// Expérience écrite dans la colonne <c>jobs</c> (« 11,8100 ») : niveau 20 du métier (table <c>experience</c>, colonne <c>metier</c> :
    /// 8097 au niveau 20), soit quatre cases (<c>JobConstant.getTotalCaseByJobLevel</c>) ; une recette à deux ingrédients réussit alors
    /// à coup sûr (<c>getChanceByNbrCaseByLvl</c> : 100 % jusqu'à cases − 2 ingrédients), ce qui rend le scénario déterministe.
    /// </summary>
    private const long SwordSmithXp = 8100;
    /// <summary>Paiement, prime si réussite et kamas du client ; StarLoco exige l'artisan à moins de 4 cases de la forge (<c>getDistanceBetween</c> &lt; 4).</summary>
    private const int CraftPayKamas = 150, CraftBonusKamas = 50, CraftClientKamas = 1000, ForgeReach = 3;

    private static Tool_BotProtocol.Game.Jobs.Jobs SwordJob(LiveSession s) => s.Account.Game.character.GetJobsSnapshot().FirstOrDefault(job => job?.ID == SwordSmithJob);

    private static CraftSecureExchange SecureCraft(LiveSession s) => s.Account.Game.Interactions.Exchanges.Get<CraftSecureExchange>();

    /// <summary>
    /// Cellules d'une carte du bot décodées à la demande : <c>Map.AllBotMaps</c> ne garde que <c>MapData</c>, <c>DecompressMap</c> remplit
    /// <c>MapCells</c> et <c>Interactives</c> comme à l'entrée sur la carte (<c>SetRefreshMap</c>) ; <c>null</c> si les données sont illisibles.
    /// </summary>
    private static Map DecodeCells(Map info)
    {
        var decoded = new Map { MapID = info.MapID, MapWidth = info.MapWidth, MapHeight = info.MapHeight, X = info.X, Y = info.Y, Back_ID = info.Back_ID, MapData = info.MapData };
        try { decoded.DecompressMap(info.MapData); return decoded; }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// Vrai si une cellule de <paramref name="mapData"/> peut porter l'objet <paramref name="gfx"/> en couche 2 : ses douze bits bas sont
    /// les deux derniers caractères du bloc de dix (<c>DecompressCell</c> : <c>(c8 &lt;&lt; 6) + c9</c>), lus avant de décoder toute la carte.
    /// </summary>
    private static bool MayHoldObject(string mapData, int gfx)
    {
        char high = Tool_BotProtocol.Utils.Crypto.Hash.caracteres_array[(gfx & 0xFFF) >> 6], low = Tool_BotProtocol.Utils.Crypto.Hash.caracteres_array[gfx & 63];
        for (int i = 8; i + 1 < mapData.Length; i += 10)
            if (mapData[i] == high && mapData[i + 1] == low) return true;
        return false;
    }

    /// <summary>
    /// Première carte du bot (identifiant croissant, cellules décodées à la demande) qui a une forge, une cellule praticable hors déclencheur
    /// à moins de 4 cases d'elle pour l'artisan (même distance que <c>PathFinding.getDistanceBetween</c> : |ΔX| + |ΔY| des coordonnées de
    /// <c>GameCase</c>, calculées comme <c>Cell</c>) et une autre cellule, à deux cases au moins de l'artisan, pour le client.
    /// </summary>
    private static bool FindForge(out Map map, out Cell forge, out Cell artisanCell, out Cell clientCell)
    {
        map = null; forge = null; artisanCell = null; clientCell = null;
        foreach (Map info in Map.AllBotMaps.Values.Where(m => m != null && !string.IsNullOrEmpty(m.MapData) && MayHoldObject(m.MapData, ForgeGfx)).OrderBy(m => m.MapID))
        {
            Map candidate = DecodeCells(info);
            if (candidate == null || candidate.MapCells == null) continue;
            IReadOnlyDictionary<int, Trigger> triggers = Triggers.ForMap(candidate.MapID);
            Func<Cell, bool> free = c => c != null && c.IsWalkable() && c.C_Types != Tool_BotProtocol.Game.Maps.Enums.CellTypes.TELEPORT_CELL && !triggers.ContainsKey(c.CellID);
            foreach (int cellId in candidate.Interactives.Where(p => p.Value?.gfx == ForgeGfx).Select(p => p.Key).OrderBy(id => id))
            {
                Cell at = candidate.GetCellFromId((short)cellId);
                if (at == null) continue;
                Cell near = candidate.MapCells.Where(c => free(c) && c.CellID != at.CellID)
                    .Select(c => new { c, d = at.GetDistanceBetweenCells(c) }).Where(x => x.d <= ForgeReach)
                    .OrderBy(x => x.d).ThenBy(x => x.c.CellID).Select(x => x.c).FirstOrDefault();
                if (near == null) continue;
                Cell apart = candidate.MapCells.Where(c => free(c) && c.CellID != at.CellID && c.CellID != near.CellID)
                    .Select(c => new { c, d = near.GetDistanceBetweenCells(c) }).Where(x => x.d >= 2)
                    .OrderBy(x => x.d).ThenBy(x => x.c.CellID).Select(x => x.c).FirstOrDefault();
                if (apart == null) continue;
                map = candidate; forge = at; artisanCell = near; clientCell = apart;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Deux comptes inventés sur une carte à forge : l'artisan (métier 11 au niveau 20 écrit dans la base, marteau 494 en position 1) et
    /// le client (deux bois de frêne, deux bois de châtaignier, 1000 kamas). Le client envoie <c>ER13|&lt;artisan&gt;|20</c> (ligne brute :
    /// l'entrée du menu est le lot X6) ; StarLoco répond <c>ERK&lt;client&gt;|&lt;artisan&gt;|12</c> aux deux (<c>GameClient.request</c>) ; la
    /// demande est posée à l'artisan (<c>A_WANT_CRAFT_CLIENT</c>) et attendue par le client (<c>WAIT_FOR_CRAFT_CLIENT</c>) ; <c>EA</c> ouvre
    /// <c>ECK12|4;20</c> chez l'artisan et <c>ECK13|4;20</c> chez le client ; le client pose les ingrédients (<c>EMO+</c> → <c>EMKO+</c> /
    /// <c>EmKO+…|&lt;modèle&gt;|</c>, recette 40 reconnue des deux côtés), propose 150 kamas de paiement et 50 de prime (<c>EP1G</c>,
    /// <c>EP2G</c> → <c>Ep1;G</c>, <c>Ep2;G</c> aux deux) ; <c>EK</c> du client puis de l'artisan (<c>EK1&lt;id&gt;</c> aux deux) ; StarLoco
    /// fabrique : <c>EcK;40;T&lt;client&gt;;…</c> à l'artisan, <c>EcK;40;B&lt;artisan&gt;;…</c> au client, <c>OAKO</c> de l'épée et <c>OR</c>
    /// des bois chez le client, kamas 800 / 200 (<c>As</c>), <c>JX</c> de l'artisan, <c>EK0</c> ; « Fermer » de l'artisan → <c>EV</c> aux deux.
    /// Les objets restants sont détruits en jeu puis les comptes supprimés (même nettoyage que x4).
    /// </summary>
    private static async Task<string> SecureCraftAsync()
    {
        RequireMaps();
        foreach (string family in new[] { "jobs", "skills", "crafts", "items", "lang" })
            if (!LangData.IsLoaded(family)) throw new SkipScenario("textes du client « " + family + " » absents : le bot ne reconnaît ni le métier, ni la recette, ni les textes de la demande");
        if (!FindForge(out Map forgeMap, out Cell forge, out Cell artisanCell, out Cell clientCell))
            throw new SkipScenario("aucune carte du bot n'a de forge (objet " + ForgeGfx + ") avec une cellule praticable à moins de 4 cases");
        using (MySql.Data.MySqlClient.MySqlConnection db = OpenAccountsDatabase(out string schema))
        {
            var artisan = new PetOwner { Label = "Art" }; var client = new PetOwner { Label = "Cli" };
            var evidence = new List<string>();
            EventHandler<ExchangeRequestEventArgs> keep = (s, e) => e.Handled = true;
            PlayerExchange artisanRequests = null;
            try
            {
                // Base : artisan et client posés sur la carte, objets à identifiants hors de la plage de StarLoco.
                int baseId = 600000000 + random.Next(1, 100000) * 10;
                int hammerId = baseId, ashId = baseId + 1, chestnutId = baseId + 2;
                artisan.Character = Invent(db, schema, a.Id, forgeMap.MapID, artisanCell.CellID, 10000, artisan.Created);
                Sql(db, "INSERT INTO " + schema + ".`world.entity.objects` (id, template, quantity, position, stats, puit) VALUES (@id, @template, 1, 1, '', 0)",
                    "@id", hammerId, "@template", SwordSmithHammer);
                Sql(db, "UPDATE " + schema + ".players SET kamas = 0, objets = @objets, jobs = @jobs WHERE id = @id",
                    "@objets", hammerId.ToString(CultureInfo.InvariantCulture) + "|", "@jobs", SwordSmithJob + "," + SwordSmithXp.ToString(CultureInfo.InvariantCulture), "@id", artisan.Character.Id);
                client.Character = Invent(db, schema, b.Id, forgeMap.MapID, clientCell.CellID, 10000, client.Created);
                Sql(db, "INSERT INTO " + schema + ".`world.entity.objects` (id, template, quantity, position, stats, puit) VALUES (@id, @template, 2, -1, '', 0)", "@id", ashId, "@template", AshWood);
                Sql(db, "INSERT INTO " + schema + ".`world.entity.objects` (id, template, quantity, position, stats, puit) VALUES (@id, @template, 2, -1, '', 0)", "@id", chestnutId, "@template", ChestnutWood);
                Sql(db, "UPDATE " + schema + ".players SET kamas = @kamas, objets = @objets WHERE id = @id",
                    "@kamas", CraftClientKamas, "@objets", ashId.ToString(CultureInfo.InvariantCulture) + "|" + chestnutId.ToString(CultureInfo.InvariantCulture) + "|", "@id", client.Character.Id);
                foreach (uint id in new[] { (uint)hammerId }) artisan.Seen.Add(id);
                foreach (uint id in new[] { (uint)ashId, (uint)chestnutId }) client.Seen.Add(id);
                Journal("[test] carte " + forgeMap.MapID + " : forge en " + forge.CellID + ", artisan " + artisan.Character.Name + " en " + artisanCell.CellID + " (distance " + forge.GetDistanceBetweenCells(artisanCell)
                    + "), client " + client.Character.Name + " en " + clientCell.CellID + " ; marteau " + hammerId + ", bois " + ashId + " et " + chestnutId + " écrits dans la base");

                // Entrées en jeu l'une après l'autre (lot CORR-LIVE) ; métier, outil, sac et kamas lus par le bot.
                LiveSession art = artisan.Session = new LiveSession(artisan.Label, artisan.Character.Login, artisan.Character.Password);
                evidence.Add(await EnterExistingAsync(art, artisan.Character.Id, artisan.Character.Name, forgeMap.MapID).ConfigureAwait(false));
                Check(await art.Until(() => SwordJob(art) != null && SwordJob(art).Level >= 20 && SwordJob(art).Skills.Any(skill => skill != null && skill.Id == ForgeSwordSkill), 10).ConfigureAwait(false),
                    "Métier 11 niveau 20 avec la compétence 20 non lu (JS/JX) : " + (SwordJob(art) == null ? "absent" : "niveau " + SwordJob(art).Level + ", " + SwordJob(art).Skills.Count + " compétence(s)"));
                Tool_BotProtocol.Game.Perso.Inventory.InventoryObjects hammer = art.Account.Game.character.Inventory.GetByInventoryId((uint)hammerId);
                Check(hammer != null && (int)hammer.position == 1, "Marteau " + hammerId + " absent ou non équipé (position " + (hammer == null ? "?" : ((int)hammer.position).ToString(CultureInfo.InvariantCulture)) + ")");
                artisan.Remember();
                LiveSession cli = client.Session = new LiveSession(client.Label, client.Character.Login, client.Character.Password);
                evidence.Add(await EnterExistingAsync(cli, client.Character.Id, client.Character.Name, forgeMap.MapID).ConfigureAwait(false));
                Tool_BotProtocol.Game.Perso.Inventory.InventoryClass bag = cli.Account.Game.character.Inventory;
                Check(await cli.Until(() => bag.GetByInventoryId((uint)ashId)?.Qua == 2 && bag.GetByInventoryId((uint)chestnutId)?.Qua == 2 && cli.Account.Game.character.Kamas == CraftClientKamas, 10).ConfigureAwait(false),
                    "Bois ou kamas du client absents : frêne " + bag.GetByInventoryId((uint)ashId)?.Qua + ", châtaignier " + bag.GetByInventoryId((uint)chestnutId)?.Qua + ", kamas " + cli.Account.Game.character.Kamas);
                client.Remember();
                Check(await art.Until(() => art.Map.GetActor(cli.Id) != null, 10).ConfigureAwait(false) && await cli.Until(() => cli.Map.GetActor(art.Id) != null, 10).ConfigureAwait(false),
                    "Les deux personnages ne se voient pas sur la carte " + forgeMap.MapID);
                string artName = art.Account.Game.character.Name, cliName = cli.Account.Game.character.Name;
                evidence.Add("métier " + Tool_BotProtocol.Game.Jobs.JobCatalog.JobName(SwordSmithJob) + " niveau " + SwordJob(art).Level + ", compétence " + Tool_BotProtocol.Game.Jobs.JobCatalog.SkillName(ForgeSwordSkill));

                // ER13 du client (ligne brute) : ERK<client>|<artisan>|12 aux deux ; demande posée à l'artisan, attendue par le client.
                artisanRequests = art.Account.Game.Interactions.Exchange;
                artisanRequests.RequestReceived += keep;
                PlayerExchange clientRequests = cli.Account.Game.Interactions.Exchange;
                int artMark = art.ReceivedCount, cliMark = cli.ReceivedCount;
                await cli.Send("ER13|" + art.Id.ToString(CultureInfo.InvariantCulture) + "|" + ForgeSwordSkill.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
                string erkClient = await cli.Expect(p => p.StartsWith("ERK", StringComparison.Ordinal) || p.StartsWith("ERE", StringComparison.Ordinal) || p == "BN", "ERK (demande d'artisanat sécurisé) chez le client", 15, cliMark).ConfigureAwait(false);
                string expectedErk = "ERK" + cli.Id + "|" + art.Id + "|12";
                Check(erkClient == expectedErk, "Réponse de StarLoco à ER13 : " + erkClient + " (attendu " + expectedErk + " ; ERET = artisan trop loin de la forge, BN = pas d'outil équipé)");
                string erkArtisan = await art.Expect("ERK", "ERK chez l'artisan", 15, artMark).ConfigureAwait(false);
                Check(erkArtisan == expectedErk, "ERK de l'artisan : " + erkArtisan);
                Check(await art.Until(() => artisanRequests.PendingRequest != null, 5).ConfigureAwait(false) && await cli.Until(() => clientRequests.PendingRequest != null, 5).ConfigureAwait(false), "Demande non posée");
                ExchangeRequest asked = artisanRequests.PendingRequest, waiting = clientRequests.PendingRequest;
                Check(asked.Type == 12 && asked.IsSecureCraft && !asked.Outgoing && asked.PartnerId == cli.Id && asked.PartnerName == cliName && asked.Text.Contains(cliName) && asked.Text.Contains("artisan"),
                    "Demande reçue par l'artisan : type " + asked.Type + ", sortante " + asked.Outgoing + ", partenaire " + asked.PartnerName + ", texte « " + asked.Text + " »");
                Check(waiting.Type == 12 && waiting.IsSecureCraft && waiting.Outgoing && waiting.PartnerId == art.Id && waiting.Text.Contains(artName),
                    "Demande envoyée par le client : type " + waiting.Type + ", sortante " + waiting.Outgoing + ", texte « " + waiting.Text + " »");
                evidence.Add(erkClient + " aux deux ; artisan : « " + asked.Text.Replace("\n", " ") + " » ; client : « " + waiting.Text.Replace("\n", " ") + " »");

                // EA de l'artisan : ECK12|4;20 chez lui, ECK13|4;20 chez le client ; fenêtres ouvertes, demandes consommées.
                artMark = art.ReceivedCount; cliMark = cli.ReceivedCount;
                var accepted = await artisanRequests.AcceptAsync().ConfigureAwait(false);
                Check(accepted.Sent, "EA refusé par le bot : " + accepted.Message);
                string eckArtisan = await art.Expect("ECK", "ECK12 chez l'artisan", 15, artMark).ConfigureAwait(false);
                string eckClient = await cli.Expect("ECK", "ECK13 chez le client", 15, cliMark).ConfigureAwait(false);
                Check(eckArtisan == "ECK12|4;20" && eckClient == "ECK13|4;20", "ECK reçus : " + eckArtisan + " / " + eckClient + " (attendu ECK12|4;20 / ECK13|4;20)");
                CraftSecureExchange secureArt = SecureCraft(art), secureCli = SecureCraft(cli);
                Check(await art.Until(() => secureArt.IsOpen, 5).ConfigureAwait(false) && await cli.Until(() => secureCli.IsOpen, 5).ConfigureAwait(false), "Fenêtres de l'artisanat sécurisé non ouvertes");
                Check(secureArt.IsArtisan && !secureArt.IsClient && secureArt.Slots == 4 && secureArt.SkillId == ForgeSwordSkill && secureArt.PartnerId == cli.Id && secureArt.Job?.ID == SwordSmithJob
                    && artisanRequests.PendingRequest == null && art.Account.AccountStates == AccountStates.EXCHANGE,
                    "Fenêtre de l'artisan : artisan " + secureArt.IsArtisan + ", cases " + secureArt.Slots + ", compétence " + secureArt.SkillId + ", partenaire " + secureArt.PartnerId + ", métier " + secureArt.Job?.ID);
                Check(secureCli.IsClient && !secureCli.IsArtisan && secureCli.Slots == 4 && secureCli.SkillId == ForgeSwordSkill && secureCli.PartnerId == art.Id && secureCli.PartnerName == artName
                    && clientRequests.PendingRequest == null && cli.Account.AccountStates == AccountStates.EXCHANGE,
                    "Fenêtre du client : client " + secureCli.IsClient + ", cases " + secureCli.Slots + ", compétence " + secureCli.SkillId + ", partenaire " + secureCli.PartnerName);
                evidence.Add(eckArtisan + " / " + eckClient + " : « " + secureArt.SkillName + " », " + secureArt.Slots + " cases, artisan " + artName + " ↔ client " + cliName);

                // Ingrédients posés par le client : EMKO+ chez lui, EmKO+…|<modèle>| chez l'artisan ; recette 40 reconnue des deux côtés.
                artMark = art.ReceivedCount; cliMark = cli.ReceivedCount;
                var placed = await secureCli.AddIngredientAsync((uint)ashId, 2).ConfigureAwait(false);
                Check(placed.Sent, "EMO+ du frêne refusé : " + placed.Message);
                await cli.Expect("EMKO+" + ashId + "|2", "EMKO+ du frêne", 15, cliMark).ConfigureAwait(false);
                string emk = await art.Expect("EmKO+" + ashId + "|2|" + AshWood, "EmKO+ du frêne chez l'artisan", 15, artMark).ConfigureAwait(false);
                artMark = art.ReceivedCount; cliMark = cli.ReceivedCount;
                placed = await secureCli.AddIngredientAsync((uint)chestnutId, 2).ConfigureAwait(false);
                Check(placed.Sent, "EMO+ du châtaignier refusé : " + placed.Message);
                await cli.Expect("EMKO+" + chestnutId + "|2", "EMKO+ du châtaignier", 15, cliMark).ConfigureAwait(false);
                await art.Expect("EmKO+" + chestnutId + "|2|" + ChestnutWood, "EmKO+ du châtaignier chez l'artisan", 15, artMark).ConfigureAwait(false);
                Check(await cli.Until(() => secureCli.IngredientCount == 2 && secureCli.LocalIngredients.Count == 2, 5).ConfigureAwait(false)
                    && await art.Until(() => secureArt.IngredientCount == 2 && secureArt.DistantIngredients.Count == 2, 5).ConfigureAwait(false),
                    "Ingrédients : client " + secureCli.IngredientCount + ", artisan " + secureArt.IngredientCount);
                Check(secureCli.ExpectedRecipe?.ResultTemplateId == SmallWoodSword && secureArt.ExpectedRecipe?.ResultTemplateId == SmallWoodSword,
                    "Recette 40 non reconnue : client " + secureCli.ExpectedRecipe?.ResultTemplateId + ", artisan " + secureArt.ExpectedRecipe?.ResultTemplateId);
                Check(!secureCli.LocalReady && !secureArt.DistantReady, "Validation posée sans EK");
                evidence.Add("EMKO+ ×2 chez le client, " + Short(emk, 40) + " chez l'artisan, recette " + Tool_BotProtocol.Game.Jobs.JobCatalog.ItemName(SmallWoodSword) + " reconnue des deux côtés");

                // Paiement et prime du client (EP1G, EP2G → Ep1;G, Ep2;G aux deux) ; l'artisan ne peut rien proposer.
                var artisanPays = await secureArt.SetPayKamasAsync(10, CraftPayment.Pay).ConfigureAwait(false);
                Check(!artisanPays.Sent, "L'artisan a pu proposer un paiement");
                artMark = art.ReceivedCount; cliMark = cli.ReceivedCount;
                var pay = await secureCli.SetPayKamasAsync(CraftPayKamas, CraftPayment.Pay).ConfigureAwait(false);
                Check(pay.Sent, "EP1G refusé : " + pay.Message);
                await cli.Expect("Ep1;G" + CraftPayKamas, "Ep1;G chez le client", 15, cliMark).ConfigureAwait(false);
                await art.Expect("Ep1;G" + CraftPayKamas, "Ep1;G chez l'artisan", 15, artMark).ConfigureAwait(false);
                artMark = art.ReceivedCount; cliMark = cli.ReceivedCount;
                pay = await secureCli.SetPayKamasAsync(CraftBonusKamas, CraftPayment.PayIfSuccess).ConfigureAwait(false);
                Check(pay.Sent, "EP2G refusé : " + pay.Message);
                await cli.Expect("Ep2;G" + CraftBonusKamas, "Ep2;G chez le client", 15, cliMark).ConfigureAwait(false);
                await art.Expect("Ep2;G" + CraftBonusKamas, "Ep2;G chez l'artisan", 15, artMark).ConfigureAwait(false);
                Check(await art.Until(() => secureArt.PayKamas == CraftPayKamas && secureArt.PayIfSuccessKamas == CraftBonusKamas, 5).ConfigureAwait(false)
                    && secureCli.PayKamas == CraftPayKamas && secureCli.PayIfSuccessKamas == CraftBonusKamas,
                    "Paiement lu : client " + secureCli.PayKamas + "/" + secureCli.PayIfSuccessKamas + ", artisan " + secureArt.PayKamas + "/" + secureArt.PayIfSuccessKamas);
                evidence.Add("Ep1;G" + CraftPayKamas + " et Ep2;G" + CraftBonusKamas + " aux deux");

                // Combiner : EK du client après le délai de 3 s (EK1<client> aux deux), puis EK de l'artisan → fabrication.
                Check(await cli.Until(() => secureCli.CanValidate, 6).ConfigureAwait(false), "« Combiner » du client toujours grisé après 6 s");
                artMark = art.ReceivedCount; cliMark = cli.ReceivedCount;
                var combine = await secureCli.CombineAsync().ConfigureAwait(false);
                Check(combine.Sent, "EK du client refusé : " + combine.Message);
                await cli.Expect("EK1" + cli.Id, "EK1<client> chez le client", 15, cliMark).ConfigureAwait(false);
                await art.Expect("EK1" + cli.Id, "EK1<client> chez l'artisan", 15, artMark).ConfigureAwait(false);
                Check(await cli.Until(() => secureCli.LocalReady && !secureCli.IsValidatePending, 5).ConfigureAwait(false) && await art.Until(() => secureArt.DistantReady, 5).ConfigureAwait(false)
                    && !secureArt.IsCombinePending && !secureCli.IsCombinePending, "Validation du client non lue : client prêt " + secureCli.LocalReady + ", vu par l'artisan " + secureArt.DistantReady);
                Check(await art.Until(() => secureArt.CanValidate, 6).ConfigureAwait(false), "« Combiner » de l'artisan toujours grisé après 6 s");
                int kamasClientBefore = cli.Account.Game.character.Kamas, kamasArtisanBefore = art.Account.Game.character.Kamas;
                uint xpBefore = SwordJob(art).ActualXP;
                artMark = art.ReceivedCount; cliMark = cli.ReceivedCount;
                combine = await secureArt.CombineAsync().ConfigureAwait(false);
                Check(combine.Sent, "EK de l'artisan refusé : " + combine.Message);
                string craftArtisan = await art.Expect(p => p.StartsWith("Ec", StringComparison.Ordinal), "EcK chez l'artisan", 20, artMark).ConfigureAwait(false);
                string craftClient = await cli.Expect(p => p.StartsWith("Ec", StringComparison.Ordinal), "EcK chez le client", 20, cliMark).ConfigureAwait(false);
                Check(craftArtisan.StartsWith("EcK;" + SmallWoodSword + ";T" + cliName + ";", StringComparison.Ordinal) && craftClient.StartsWith("EcK;" + SmallWoodSword + ";B" + artName + ";", StringComparison.Ordinal),
                    "Résultat de la fabrication : artisan " + craftArtisan + ", client " + craftClient + " (attendu EcK;40;T<client>;… / EcK;40;B<artisan>;…)");
                Check(await cli.Until(() => bag.Objets.Any(x => x != null && x.ID == SmallWoodSword) && bag.GetByInventoryId((uint)ashId) == null && bag.GetByInventoryId((uint)chestnutId) == null, 10).ConfigureAwait(false),
                    "Épée (OAKO) ou bois consommés (OR) non lus chez le client : épée " + bag.Objets.Any(x => x != null && x.ID == SmallWoodSword) + ", frêne " + bag.GetByInventoryId((uint)ashId)?.Qua + ", châtaignier " + bag.GetByInventoryId((uint)chestnutId)?.Qua);
                Check(await cli.Until(() => cli.Account.Game.character.Kamas == kamasClientBefore - CraftPayKamas - CraftBonusKamas, 10).ConfigureAwait(false)
                    && await art.Until(() => art.Account.Game.character.Kamas == kamasArtisanBefore + CraftPayKamas + CraftBonusKamas, 10).ConfigureAwait(false),
                    "Kamas après la fabrication : client " + cli.Account.Game.character.Kamas + " (attendu " + (kamasClientBefore - CraftPayKamas - CraftBonusKamas) + "), artisan " + art.Account.Game.character.Kamas + " (attendu " + (kamasArtisanBefore + CraftPayKamas + CraftBonusKamas) + ")");
                Check(await art.Until(() => secureArt.LastOutcome == CraftOutcome.Success && secureArt.LastResultTemplateId == SmallWoodSword && secureArt.IngredientCount == 0 && secureArt.PayKamas == 0 && !secureArt.LocalReady && !secureArt.DistantReady && !secureArt.IsCombinePending, 10).ConfigureAwait(false)
                    && await cli.Until(() => secureCli.LastOutcome == CraftOutcome.Success && secureCli.LastResultTemplateId == SmallWoodSword && secureCli.IngredientCount == 0 && secureCli.PayKamas == 0 && !secureCli.LocalReady && !secureCli.DistantReady && !secureCli.IsCombinePending, 10).ConfigureAwait(false),
                    "État après EcK et EK0 : artisan " + secureArt.LastOutcome + "/" + secureArt.IngredientCount + "/" + secureArt.LocalReady + ", client " + secureCli.LastOutcome + "/" + secureCli.IngredientCount + "/" + secureCli.LocalReady);
                Check(secureArt.IsOpen && secureCli.IsOpen, "La fenêtre s'est fermée après la fabrication (StarLoco la garde ouverte)");
                Check(secureArt.LastMessage.Contains(cliName) && secureCli.LastMessage.Contains(artName) && secureCli.LastMessage.Contains(Tool_BotProtocol.Game.Jobs.JobCatalog.ItemName(SmallWoodSword)),
                    "Messages de la fabrication : artisan « " + secureArt.LastMessage + " », client « " + secureCli.LastMessage + " »");
                bool gainedXp = await art.Until(() => SwordJob(art)?.ActualXP > xpBefore, 5).ConfigureAwait(false);
                bool shown = art.ReceivedSince(artMark).Any(p => p.StartsWith("IO" + art.Id + "|+" + SmallWoodSword, StringComparison.Ordinal));
                evidence.Add("EK1 ×2 puis " + Short(craftArtisan, 40) + " / " + Short(craftClient, 40) + " ; épée " + SmallWoodSword + " dans le sac du client, bois consommés, kamas " + kamasClientBefore + " → " + cli.Account.Game.character.Kamas
                    + " et " + kamasArtisanBefore + " → " + art.Account.Game.character.Kamas + (gainedXp ? ", JX " + xpBefore + " → " + SwordJob(art).ActualXP : ", sans JX") + (shown ? ", IO+" + SmallWoodSword : ", sans IO") + " ; « " + secureCli.LastMessage + " »");

                // Fermeture par l'artisan : EV aux deux.
                artMark = art.ReceivedCount; cliMark = cli.ReceivedCount;
                var left = await secureArt.LeaveAsync().ConfigureAwait(false);
                Check(left.Sent, "EV refusé : " + left.Message);
                await art.Expect(p => p == "EV" || p == "EVa", "EV chez l'artisan", 15, artMark).ConfigureAwait(false);
                await cli.Expect(p => p == "EV" || p == "EVa", "EV chez le client", 15, cliMark).ConfigureAwait(false);
                Check(await art.Until(() => !secureArt.IsOpen && art.Account.AccountStates == AccountStates.CONNECTED_INACTIVE, 5).ConfigureAwait(false)
                    && await cli.Until(() => !secureCli.IsOpen && cli.Account.AccountStates == AccountStates.CONNECTED_INACTIVE, 5).ConfigureAwait(false),
                    "Fenêtres encore ouvertes après EV : artisan " + secureArt.IsOpen + ", client " + secureCli.IsOpen);
                evidence.Add("EV aux deux, fenêtres fermées");
                artisan.Remember(); client.Remember();
                return string.Join(" ; ", evidence);
            }
            finally
            {
                if (artisanRequests != null) artisanRequests.RequestReceived -= keep;
                // Nettoyage comme x4 (objets détruits en jeu, lignes de la base, comptes), un compte après l'autre.
                await ClosePetOwnerAsync(db, schema, client).ConfigureAwait(false);
                await ClosePetOwnerAsync(db, schema, artisan).ConfigureAwait(false);
            }
        }
    }
}

// Scénario x6 (lot X6) : artisanat sécurisé depuis les menus du bot, EW+ à l'arrivée, ERET, invitation de l'artisan, clic gauche sur un joueur.
internal static partial class Live
{
    /// <summary>Distance forge → artisan éloigné : StarLoco répond <c>ERET</c> dès que <c>getDistanceBetween</c> ≥ 4 ; une case de marge.</summary>
    private const int FarFromForge = 5;

    /// <summary>
    /// Mêmes comptes inventés que x5 (artisan au marteau 494, métier 11 au niveau 20, posé à moins de 4 cases de la forge ; client avec
    /// les bois), mais tout passe par les menus du bot (lot X6). À l'arrivée de chaque joueur, StarLoco annonce <c>EW+&lt;artisan&gt;|20</c>
    /// à l'arrivant et à l'artisan (<c>GameClient</c>, boucle des joueurs de la carte) : dans la fenêtre de jeu du client, un clic gauche
    /// sur l'artisan ouvre son menu avec « Demander à &lt;compétence&gt; », sans <c>GA001</c>. L'artisan s'éloigne de la forge (≥ 5 cases) :
    /// « Demander à » envoie <c>ER13|&lt;artisan&gt;|20</c>, StarLoco répond <c>ERET</c> (<c>NOT_NEAR_CRAFT_TABLE</c> dans le chat du client,
    /// aucune demande en attente). L'artisan revient : <c>ER13</c> → <c>ERK&lt;client&gt;|&lt;artisan&gt;|12</c> aux deux, <c>EA</c> de l'artisan →
    /// <c>ECK12</c> / <c>ECK13</c>, <c>EV</c>. Puis « Inviter à &lt;compétence&gt; » de l'artisan (d'après ses propres <c>EW+</c>) : <c>ER12|&lt;client&gt;|20</c>
    /// → <c>ERK&lt;artisan&gt;|&lt;client&gt;|12</c> à l'artisan et <c>…|13</c> au client (<c>A_WANT_CRAFT_ARTISAN</c>), <c>EA</c> du client → <c>ECK13</c> /
    /// <c>ECK12</c>, <c>EV</c> du client. Enfin le client consulte le livre des artisans de la carte (<see cref="CrafterBookAsync"/>). Le marchand
    /// hors ligne est le scénario x6b ; un percepteur demande une guilde de dix membres, absente de ce serveur de test : ce clic reste couvert
    /// par BotMapActionsSmoke.
    /// </summary>
    private static async Task<string> SecureCraftMenuAsync()
    {
        RequireMaps();
        foreach (string family in new[] { "jobs", "skills", "crafts", "items", "lang" })
            if (!LangData.IsLoaded(family)) throw new SkipScenario("textes du client « " + family + " » absents : le bot ne reconnaît ni le métier, ni la compétence, ni les textes du menu");
        if (!FindForge(out Map forgeMap, out Cell forge, out Cell artisanCell, out Cell clientCell))
            throw new SkipScenario("aucune carte du bot n'a de forge (objet " + ForgeGfx + ") avec une cellule praticable à moins de 4 cases");
        IReadOnlyDictionary<int, Trigger> triggers = Triggers.ForMap(forgeMap.MapID);
        Func<Cell, bool> free = c => c != null && c.IsWalkable() && c.C_Types != Tool_BotProtocol.Game.Maps.Enums.CellTypes.TELEPORT_CELL && !triggers.ContainsKey(c.CellID);
        // StarLoco parcourt toutes les cases de la carte : la demande est acceptée dès qu'UNE forge est à moins de 4 cases de l'artisan.
        List<Cell> forges = forgeMap.Interactives.Where(p => p.Value?.gfx == ForgeGfx).Select(p => forgeMap.GetCellFromId((short)p.Key)).Where(c => c != null).ToList();
        Func<Cell, int> forgeDistance = c => forges.Min(f => f.GetDistanceBetweenCells(c));
        List<Cell> farCells = forgeMap.MapCells.Where(c => free(c) && c.CellID != clientCell.CellID && forgeDistance(c) >= FarFromForge)
            .OrderBy(c => artisanCell.GetDistanceBetweenCells(c)).ThenBy(c => c.CellID).Take(12).ToList();
        if (farCells.Count == 0) throw new SkipScenario("aucune cellule praticable à " + FarFromForge + " cases ou plus de toutes les forges (" + string.Join(",", forges.Select(f => f.CellID)) + ") sur la carte " + forgeMap.MapID);
        string askText = MapActionTexts.AskTo(Tool_BotProtocol.Game.Jobs.JobCatalog.SkillName(ForgeSwordSkill));
        string inviteText = MapActionTexts.InviteTo(Tool_BotProtocol.Game.Jobs.JobCatalog.SkillName(ForgeSwordSkill));
        using (MySql.Data.MySqlClient.MySqlConnection db = OpenAccountsDatabase(out string schema))
        {
            var artisan = new PetOwner { Label = "Art" }; var client = new PetOwner { Label = "Cli" };
            var evidence = new List<string>();
            EventHandler<ExchangeRequestEventArgs> keep = (s, e) => e.Handled = true;
            PlayerExchange artisanRequests = null, clientRequests = null;
            GameClientFullform screen = null;
            try
            {
                // Base : mêmes lignes que x5 (identifiants d'objets hors de la plage de StarLoco).
                int baseId = 610000000 + random.Next(1, 100000) * 10;
                int hammerId = baseId, ashId = baseId + 1, chestnutId = baseId + 2;
                artisan.Character = Invent(db, schema, a.Id, forgeMap.MapID, artisanCell.CellID, 10000, artisan.Created);
                Sql(db, "INSERT INTO " + schema + ".`world.entity.objects` (id, template, quantity, position, stats, puit) VALUES (@id, @template, 1, 1, '', 0)",
                    "@id", hammerId, "@template", SwordSmithHammer);
                Sql(db, "UPDATE " + schema + ".players SET kamas = 0, objets = @objets, jobs = @jobs WHERE id = @id",
                    "@objets", hammerId.ToString(CultureInfo.InvariantCulture) + "|", "@jobs", SwordSmithJob + "," + SwordSmithXp.ToString(CultureInfo.InvariantCulture), "@id", artisan.Character.Id);
                client.Character = Invent(db, schema, b.Id, forgeMap.MapID, clientCell.CellID, 10000, client.Created);
                Sql(db, "INSERT INTO " + schema + ".`world.entity.objects` (id, template, quantity, position, stats, puit) VALUES (@id, @template, 2, -1, '', 0)", "@id", ashId, "@template", AshWood);
                Sql(db, "INSERT INTO " + schema + ".`world.entity.objects` (id, template, quantity, position, stats, puit) VALUES (@id, @template, 2, -1, '', 0)", "@id", chestnutId, "@template", ChestnutWood);
                Sql(db, "UPDATE " + schema + ".players SET kamas = @kamas, objets = @objets WHERE id = @id",
                    "@kamas", CraftClientKamas, "@objets", ashId.ToString(CultureInfo.InvariantCulture) + "|" + chestnutId.ToString(CultureInfo.InvariantCulture) + "|", "@id", client.Character.Id);
                artisan.Seen.Add((uint)hammerId); client.Seen.Add((uint)ashId); client.Seen.Add((uint)chestnutId);
                Journal("[test] carte " + forgeMap.MapID + " : forge en " + forge.CellID + ", artisan " + artisan.Character.Name + " en " + artisanCell.CellID + " (distance " + forge.GetDistanceBetweenCells(artisanCell)
                    + "), client " + client.Character.Name + " en " + clientCell.CellID + ", forges " + string.Join(",", forges.Select(f => f.CellID)) + ", cellules éloignées " + string.Join(",", farCells.Select(c => c.CellID)));

                // Artisan d'abord : EW+<artisan>|20 à lui-même à l'arrivée (marteau en main, forge à moins de 4 cases) ; son bot retient la compétence.
                LiveSession art = artisan.Session = new LiveSession(artisan.Label, artisan.Character.Login, artisan.Character.Password);
                evidence.Add(await EnterExistingAsync(art, artisan.Character.Id, artisan.Character.Name, forgeMap.MapID).ConfigureAwait(false));
                Check(await art.Until(() => SwordJob(art) != null && SwordJob(art).Skills.Any(skill => skill != null && skill.Id == ForgeSwordSkill), 10).ConfigureAwait(false), "Métier 11 avec la compétence 20 non lu (JS)");
                string ewSelf = await art.Expect(p => p.StartsWith("EW+" + art.Id + "|", StringComparison.Ordinal), "EW+<artisan>|… à l'arrivée de l'artisan", 10, 0).ConfigureAwait(false);
                // jobs_data.crafts du forgeur d'épées : « 7012;20,145 » → deux compétences annoncées à la forge (20 et 145).
                Check(ewSelf.Substring(ewSelf.IndexOf('|') + 1).Split(';').Contains(ForgeSwordSkill.ToString(CultureInfo.InvariantCulture)), "EW+ de l'artisan sans la compétence " + ForgeSwordSkill + " : " + ewSelf);
                Tool_BotProtocol.Game.Jobs.JobsActions artJobs = art.Account.Game.Interactions.Jobs;
                Check(await art.Until(() => artJobs.PublicSkillsOf(art.Id).Contains((short)ForgeSwordSkill), 5).ConfigureAwait(false), "Compétence publique de l'artisan non retenue par son bot");
                artisan.Remember();

                // Client ensuite, avec sa fenêtre de jeu : EW+<artisan>|20 à l'arrivée ; aucune compétence pour lui-même (pas d'outil).
                LiveSession cli = client.Session = new LiveSession(client.Label, client.Character.Login, client.Character.Password);
                evidence.Add(await EnterExistingAsync(cli, client.Character.Id, client.Character.Name, forgeMap.MapID, s =>
                    s.Account.Game.character.Player_Selection += () => OnUi(() =>
                    {
                        if (screen != null) return;
                        screen = new GameClientFullform(s.Account) { StartPosition = FormStartPosition.Manual, Location = new Point(60, 60), Size = new Size(1000, 700), ShowInTaskbar = false };
                        screen.Show();
                    })).ConfigureAwait(false));
                client.Remember();
                string ewClient = await cli.Expect(p => p.StartsWith("EW+" + art.Id + "|", StringComparison.Ordinal), "EW+<artisan>|… à l'arrivée du client", 10, 0).ConfigureAwait(false);
                Check(ewClient == ewSelf, "EW+ reçu par le client : " + ewClient + " (l'artisan avait reçu " + ewSelf + ")");
                Tool_BotProtocol.Game.Jobs.JobsActions cliJobs = cli.Account.Game.Interactions.Jobs;
                Check(await cli.Until(() => cliJobs.PublicSkillsOf(art.Id).Contains((short)ForgeSwordSkill) && cliJobs.PublicSkillsOf(cli.Id).Count == 0, 5).ConfigureAwait(false),
                    "Compétences publiques lues par le client : artisan " + string.Join(";", cliJobs.PublicSkillsOf(art.Id)) + ", soi " + string.Join(";", cliJobs.PublicSkillsOf(cli.Id)));
                Check(await art.Until(() => art.Map.GetActor(cli.Id) != null, 10).ConfigureAwait(false) && await cli.Until(() => cli.Map.GetActor(art.Id) != null, 10).ConfigureAwait(false),
                    "Les deux personnages ne se voient pas sur la carte " + forgeMap.MapID);
                Check(await WaitOnUi(() => screen != null && screen.IsHandleCreated && screen.Visible && screen.Map?.Router != null ? screen : null, 20).ConfigureAwait(false) != null,
                    "La fenêtre de jeu du client ne s'est pas ouverte");
                string artName = art.Account.Game.character.Name;
                evidence.Add(ewSelf + " à l'artisan, " + ewClient + " au client");

                // Clic gauche sur l'artisan dans la fenêtre du client : son menu avec « Demander à <compétence> », aucun GA001.
                artisanRequests = art.Account.Game.Interactions.Exchange; clientRequests = cli.Account.Game.Interactions.Exchange;
                artisanRequests.RequestReceived += keep; clientRequests.RequestReceived += keep;
                int sentMark = cli.SentCount;
                string[] menu = await ClickActorAsync(screen, cli.Map.GetActor(art.Id), null).ConfigureAwait(false);
                Check(menu.Contains(MapActionTexts.Whois) && menu.Contains(askText) && !menu.Contains(inviteText), "Menu de l'artisan au clic gauche : " + string.Join(" | ", menu));
                await Task.Delay(300).ConfigureAwait(false);
                Check(!cli.SentSince(sentMark).Any(p => p.StartsWith("GA001", StringComparison.Ordinal)), "Le clic gauche sur l'artisan a envoyé un déplacement : " + string.Join(" ", cli.SentSince(sentMark)));
                evidence.Add("clic gauche sur l'artisan → menu « " + string.Join(" | ", menu.Take(3)) + " … | " + askText + " » sans GA001");

                // L'artisan s'éloigne de la forge : « Demander à » → ER13, StarLoco répond ERET ; NOT_NEAR_CRAFT_TABLE dans le chat du client.
                Cell far = null;
                foreach (Cell candidate in farCells)
                    if (await WalkAsync(art, cli, candidate.CellID).ConfigureAwait(false)) { far = candidate; break; }
                Check(far != null, "L'artisan n'a pu rejoindre aucune cellule éloignée de la forge (" + string.Join(",", farCells.Select(c => c.CellID)) + ")");
                int cliMark = cli.ReceivedCount, chatMark = cli.Account.Game.Chat.Messages.Count;
                sentMark = cli.SentCount;
                await ClickActorAsync(screen, cli.Map.GetActor(art.Id), askText).ConfigureAwait(false);
                string er13 = "ER13|" + art.Id.ToString(CultureInfo.InvariantCulture) + "|" + ForgeSwordSkill.ToString(CultureInfo.InvariantCulture);
                Check(await cli.Until(() => cli.SentSince(sentMark).Contains(er13), 5).ConfigureAwait(false), "« " + askText + " » n'a pas envoyé " + er13 + " : " + string.Join(" ", cli.SentSince(sentMark)));
                string answer = await cli.Expect(p => p.StartsWith("ERE", StringComparison.Ordinal) || p.StartsWith("ERK", StringComparison.Ordinal) || p == "BN", "réponse de StarLoco à ER13 (ERET attendu)", 15, cliMark).ConfigureAwait(false);
                Check(answer == "ERET", "Réponse de StarLoco à ER13 avec l'artisan à " + forgeDistance(far) + " cases de la forge la plus proche : " + answer + " (attendu ERET)");
                ExchangeRegistry cliRegistry = cli.Account.Game.Interactions.Exchanges;
                Check(await cli.Until(() => !string.IsNullOrEmpty(cliRegistry.LastMessage) && cli.Account.Game.Chat.Messages.Skip(chatMark).Any(line => line.Kind == ChatMessageKind.Error && line.Text == cliRegistry.LastMessage), 5).ConfigureAwait(false),
                    "ERET non relu dans le chat du client : « " + cliRegistry.LastMessage + " »");
                Check(clientRequests.PendingRequest == null && artisanRequests.PendingRequest == null && cli.Account.AccountStates == AccountStates.CONNECTED_INACTIVE, "ERET a laissé une demande en attente");
                evidence.Add("artisan en " + far.CellID + ", à " + forgeDistance(far) + " cases de la forge la plus proche : « " + askText + " » → " + er13 + " → ERET, chat « " + cliRegistry.LastMessage + " »");

                // Retour de l'artisan près de la forge : « Demander à » → ER13 → ERK<client>|<artisan>|12 aux deux ; EA de l'artisan → ECK12 / ECK13 ; EV.
                Check(await WalkAsync(art, cli, artisanCell.CellID).ConfigureAwait(false), "Retour de l'artisan en " + artisanCell.CellID + " impossible (artisan en " + art.Map.Self.CellId + ")");
                int artMark = art.ReceivedCount; cliMark = cli.ReceivedCount; sentMark = cli.SentCount;
                await ClickActorAsync(screen, cli.Map.GetActor(art.Id), askText).ConfigureAwait(false);
                Check(await cli.Until(() => cli.SentSince(sentMark).Contains(er13), 5).ConfigureAwait(false), "Second « " + askText + " » sans " + er13);
                string expectedErk = "ERK" + cli.Id + "|" + art.Id + "|12";
                string erkClient = await cli.Expect(p => p.StartsWith("ERK", StringComparison.Ordinal) || p.StartsWith("ERE", StringComparison.Ordinal) || p == "BN", "ERK chez le client", 15, cliMark).ConfigureAwait(false);
                Check(erkClient == expectedErk, "Réponse de StarLoco au second ER13 : " + erkClient + " (attendu " + expectedErk + ")");
                string erkArtisan = await art.Expect("ERK", "ERK chez l'artisan", 15, artMark).ConfigureAwait(false);
                Check(erkArtisan == expectedErk, "ERK de l'artisan : " + erkArtisan);
                Check(await art.Until(() => artisanRequests.PendingRequest != null && !artisanRequests.PendingRequest.Outgoing, 5).ConfigureAwait(false)
                    && await cli.Until(() => clientRequests.PendingRequest != null && clientRequests.PendingRequest.Outgoing, 5).ConfigureAwait(false), "Demande non posée après ERK");
                artMark = art.ReceivedCount; cliMark = cli.ReceivedCount;
                var accepted = await artisanRequests.AcceptAsync().ConfigureAwait(false);
                Check(accepted.Sent, "EA de l'artisan refusé par le bot : " + accepted.Message);
                string eckArtisan = await art.Expect("ECK", "ECK12 chez l'artisan", 15, artMark).ConfigureAwait(false);
                string eckClient = await cli.Expect("ECK", "ECK13 chez le client", 15, cliMark).ConfigureAwait(false);
                Check(eckArtisan == "ECK12|4;20" && eckClient == "ECK13|4;20", "ECK reçus : " + eckArtisan + " / " + eckClient + " (attendu ECK12|4;20 / ECK13|4;20)");
                CraftSecureExchange secureArt = SecureCraft(art), secureCli = SecureCraft(cli);
                Check(await art.Until(() => secureArt.IsOpen && secureArt.IsArtisan, 5).ConfigureAwait(false) && await cli.Until(() => secureCli.IsOpen && secureCli.IsClient, 5).ConfigureAwait(false),
                    "Fenêtres de l'artisanat sécurisé non ouvertes après l'acceptation");
                evidence.Add("artisan revenu : « " + askText + " » → " + er13 + " → " + expectedErk + " aux deux, EA → " + eckArtisan + " / " + eckClient);
                artMark = art.ReceivedCount; cliMark = cli.ReceivedCount;
                var left = await secureArt.LeaveAsync().ConfigureAwait(false);
                Check(left.Sent, "EV de l'artisan refusé : " + left.Message);
                await art.Expect(p => p == "EV" || p == "EVa", "EV chez l'artisan", 15, artMark).ConfigureAwait(false);
                await cli.Expect(p => p == "EV" || p == "EVa", "EV chez le client", 15, cliMark).ConfigureAwait(false);
                Check(await art.Until(() => !secureArt.IsOpen && art.Account.AccountStates == AccountStates.CONNECTED_INACTIVE, 5).ConfigureAwait(false)
                    && await cli.Until(() => !secureCli.IsOpen && cli.Account.AccountStates == AccountStates.CONNECTED_INACTIVE, 5).ConfigureAwait(false), "Fenêtres encore ouvertes après EV");

                // « Inviter à <compétence> » de l'artisan (ses propres EW+) : ER12|<client>|20 → ERK<artisan>|<client>|12 à l'artisan, |13 au client ; EA du client → ECK13 / ECK12 ; EV du client.
                MapActions artActions = art.Account.Game.Interactions.MapActions;
                var cliActor = art.Map.GetActor(cli.Id) as PlayerActor;
                Check(cliActor != null, "Client absent de la carte de l'artisan");
                MenuEntry[] invites = PlayerMenuProvider.SecureCraftEntries(cliActor, art.Account.Game, artActions).ToArray();
                MenuEntry invite = invites.FirstOrDefault(e => e.Texte == inviteText);
                Check(invite != null && invite.Activé && invites.All(e => e.Texte.StartsWith(MapActionTexts.InviteTo(string.Empty).TrimEnd(), StringComparison.Ordinal)),
                    "Entrées d'artisanat de l'artisan sur le client : " + string.Join(" | ", invites.Select(e => e.Texte)));
                artMark = art.ReceivedCount; cliMark = cli.ReceivedCount; int artSent = art.SentCount;
                string said = await invite.Action(new ActorMenuContext(cliActor, new Tool_BotProtocol.Game.Maps.Interfaces.Entites[] { cliActor }, (short)cliActor.CellId, art.Account, null, null, Keys.None)).ConfigureAwait(false);
                string er12 = "ER12|" + cli.Id.ToString(CultureInfo.InvariantCulture) + "|" + ForgeSwordSkill.ToString(CultureInfo.InvariantCulture);
                Check(await art.Until(() => art.SentSince(artSent).Contains(er12), 5).ConfigureAwait(false), "« " + inviteText + " » n'a pas envoyé " + er12 + " : " + said);
                string erkArt2 = await art.Expect(p => p.StartsWith("ERK", StringComparison.Ordinal) || p.StartsWith("ERE", StringComparison.Ordinal), "ERK 12 chez l'artisan", 15, artMark).ConfigureAwait(false);
                string erkCli2 = await cli.Expect(p => p.StartsWith("ERK", StringComparison.Ordinal) || p.StartsWith("ERE", StringComparison.Ordinal), "ERK 13 chez le client", 15, cliMark).ConfigureAwait(false);
                Check(erkArt2 == "ERK" + art.Id + "|" + cli.Id + "|12" && erkCli2 == "ERK" + art.Id + "|" + cli.Id + "|13",
                    "Réponses de StarLoco à ER12 : artisan " + erkArt2 + ", client " + erkCli2 + " (attendu ERK<artisan>|<client>|12 / |13)");
                Check(await cli.Until(() => clientRequests.PendingRequest != null && !clientRequests.PendingRequest.Outgoing && clientRequests.PendingRequest.Type == 13, 5).ConfigureAwait(false)
                    && await art.Until(() => artisanRequests.PendingRequest != null && artisanRequests.PendingRequest.Outgoing, 5).ConfigureAwait(false), "Invitation non posée après ERK");
                ExchangeRequest invited = clientRequests.PendingRequest;
                Check(invited.IsSecureCraft && invited.PartnerId == art.Id && invited.Text.Contains(artName), "Invitation reçue par le client : partenaire " + invited.PartnerName + ", texte « " + invited.Text + " »");
                artMark = art.ReceivedCount; cliMark = cli.ReceivedCount;
                accepted = await clientRequests.AcceptAsync().ConfigureAwait(false);
                Check(accepted.Sent, "EA du client refusé par le bot : " + accepted.Message);
                eckClient = await cli.Expect("ECK", "ECK13 chez le client", 15, cliMark).ConfigureAwait(false);
                eckArtisan = await art.Expect("ECK", "ECK12 chez l'artisan", 15, artMark).ConfigureAwait(false);
                Check(eckClient == "ECK13|4;20" && eckArtisan == "ECK12|4;20", "ECK après l'invitation : client " + eckClient + ", artisan " + eckArtisan);
                Check(await cli.Until(() => secureCli.IsOpen && secureCli.IsClient, 5).ConfigureAwait(false) && await art.Until(() => secureArt.IsOpen && secureArt.IsArtisan, 5).ConfigureAwait(false),
                    "Fenêtres non ouvertes après l'invitation");
                evidence.Add("« " + inviteText + " » → " + er12 + " → " + erkArt2 + " / " + erkCli2 + ", « " + invited.Text.Replace("\n", " ") + " », EA du client → " + eckClient + " / " + eckArtisan);
                artMark = art.ReceivedCount; cliMark = cli.ReceivedCount;
                left = await secureCli.LeaveAsync().ConfigureAwait(false);
                Check(left.Sent, "EV du client refusé : " + left.Message);
                await cli.Expect(p => p == "EV" || p == "EVa", "EV chez le client", 15, cliMark).ConfigureAwait(false);
                await art.Expect(p => p == "EV" || p == "EVa", "EV chez l'artisan", 15, artMark).ConfigureAwait(false);
                Check(await cli.Until(() => !secureCli.IsOpen && cli.Account.AccountStates == AccountStates.CONNECTED_INACTIVE, 5).ConfigureAwait(false)
                    && await art.Until(() => !secureArt.IsOpen && art.Account.AccountStates == AccountStates.CONNECTED_INACTIVE, 5).ConfigureAwait(false), "Fenêtres encore ouvertes après le second EV");
                evidence.Add("EV du client, fenêtres fermées");

                // Livre des artisans de la carte dans la fenêtre du client : la carte de l'artisan (Message privé, Localiser), puis Fermer → EV.
                evidence.Add(await CrafterBookAsync(cli, screen, art.Id, artName, forgeMap.MapID).ConfigureAwait(false));
                artisan.Remember(); client.Remember();
                return string.Join(" ; ", evidence);
            }
            finally
            {
                if (artisanRequests != null) artisanRequests.RequestReceived -= keep;
                if (clientRequests != null) clientRequests.RequestReceived -= keep;
                // Nettoyage comme x4 et x5 (objets détruits en jeu, lignes de la base, comptes), un compte après l'autre ; la fenêtre de jeu du
                // client se ferme après la destruction de son sac, sa fermeture libérant le compte.
                await ClosePetOwnerAsync(db, schema, client, () => CloseScreenAsync(screen)).ConfigureAwait(false);
                await ClosePetOwnerAsync(db, schema, artisan).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Prix unitaire du lot du marchand : taxe du mode marchand = somme des prix ÷ 1 000 (<c>storeAllBuy() / 1000</c>), donc 0.</summary>
    private const int MerchantPrice = 1, MerchantBuyerKamas = 10;

    /// <summary>
    /// Marchand hors ligne (lot X6, <c>onSpriteRelease</c> du client) sur la carte de la forge de x5/x6 (aucune interdiction de marchand dans
    /// <c>forbidden</c> chez StarLoco). Un vendeur inventé organise son magasin (<c>ER6|</c> → <c>ECK6</c>, <c>EMO+&lt;bois&gt;|1|1</c> → <c>EL</c>,
    /// <c>EV</c>), demande la taxe (<c>Eq</c> → <c>Eq1|1|0</c>) puis passe en mode marchand (<c>EQ</c> : StarLoco le déconnecte et le garde sur
    /// sa cellule). L'acheteur arrive ensuite avec sa fenêtre de jeu : <c>GM|~…;-5;…</c> (<c>GAME_SEND_MERCHANT_LIST</c>) pose le marchand ;
    /// un clic gauche simple ouvre son menu (« Acheter » par défaut, « Maj + clic ») sans rien envoyer ; Maj + clic envoie
    /// <c>ER4|&lt;marchand&gt;|&lt;cellule&gt;</c> → <c>ECK4</c> + <c>EL</c> ; <c>EB&lt;lot&gt;|1</c> → <c>EBK</c>, magasin vide : <c>GM|-&lt;marchand&gt;</c>
    /// et <c>EV</c>. Le vendeur se reconnecte enfin (StarLoco retire alors tout marchand restant), puis les deux comptes sont nettoyés.
    /// </summary>
    private static async Task<string> OfflineMerchantClickAsync()
    {
        RequireMaps();
        if (!FindForge(out Map shopMap, out Cell forge, out Cell sellerCell, out Cell buyerCell))
            throw new SkipScenario("aucune carte du bot n'a de forge (objet " + ForgeGfx + ") : pas de carte connue des deux côtés pour le marchand");
        using (MySql.Data.MySqlClient.MySqlConnection db = OpenAccountsDatabase(out string schema))
        {
            var seller = new PetOwner { Label = "Mar" }; var buyer = new PetOwner { Label = "Ach" };
            var evidence = new List<string>();
            GameClientFullform screen = null;
            bool merchantMode = false;
            try
            {
                int woodId = 610000000 + random.Next(1, 100000) * 10 + 5;
                seller.Character = Invent(db, schema, a.Id, shopMap.MapID, sellerCell.CellID, 10000, seller.Created);
                Sql(db, "INSERT INTO " + schema + ".`world.entity.objects` (id, template, quantity, position, stats, puit) VALUES (@id, @template, 1, -1, '', 0)", "@id", woodId, "@template", AshWood);
                Sql(db, "UPDATE " + schema + ".players SET kamas = 0, objets = @objets WHERE id = @id", "@objets", woodId.ToString(CultureInfo.InvariantCulture) + "|", "@id", seller.Character.Id);
                seller.Seen.Add((uint)woodId);
                buyer.Character = Invent(db, schema, b.Id, shopMap.MapID, buyerCell.CellID, 10000, buyer.Created);
                Sql(db, "UPDATE " + schema + ".players SET kamas = @kamas WHERE id = @id", "@kamas", MerchantBuyerKamas, "@id", buyer.Character.Id);
                Journal("[test] carte " + shopMap.MapID + " : vendeur " + seller.Character.Name + " en " + sellerCell.CellID + " avec le bois " + woodId + ", acheteur " + buyer.Character.Name + " en " + buyerCell.CellID);

                // Vendeur : magasin organisé (ER6| → ECK6, EMO+ → EL, EV), taxe (Eq → Eq1|1|0), mode marchand (EQ → déconnexion).
                LiveSession sel = seller.Session = new LiveSession(seller.Label, seller.Character.Login, seller.Character.Password);
                evidence.Add(await EnterExistingAsync(sel, seller.Character.Id, seller.Character.Name, shopMap.MapID).ConfigureAwait(false));
                MerchantExchange shop = sel.Account.Game.Interactions.Merchant;
                Check(await sel.Until(() => seller.Bag.GetByInventoryId((uint)woodId) != null, 10).ConfigureAwait(false), "Bois " + woodId + " absent du sac du vendeur");
                int mark = sel.ReceivedCount;
                var sent = await shop.OrganizeAsync().ConfigureAwait(false);
                Check(sent.Sent, "ER6| refusé par le bot : " + sent.Message);
                string eck6 = await sel.Expect("ECK6", "ECK6 (son magasin)", 10, mark).ConfigureAwait(false);
                Check(await sel.Until(() => shop.IsOrganizing && shop.ContentReceived, 5).ConfigureAwait(false), "Magasin du vendeur non ouvert après " + eck6);
                mark = sel.ReceivedCount;
                sent = await shop.AddToShopAsync((uint)woodId, 1, MerchantPrice).ConfigureAwait(false);
                Check(sent.Sent, "EMO+ refusé par le bot : " + sent.Message);
                string listed = await sel.Expect(p => p.StartsWith("EL" + woodId.ToString(CultureInfo.InvariantCulture) + ";", StringComparison.Ordinal), "EL avec le bois mis en vente", 10, mark).ConfigureAwait(false);
                mark = sel.ReceivedCount;
                sent = await shop.LeaveAsync().ConfigureAwait(false);
                Check(sent.Sent, "EV du magasin refusé : " + sent.Message);
                await sel.Expect(p => p == "EV" || p == "EVa", "EV du magasin", 10, mark).ConfigureAwait(false);
                Check(await sel.Until(() => !shop.IsOpen && sel.Account.AccountStates == AccountStates.CONNECTED_INACTIVE, 5).ConfigureAwait(false), "Magasin encore ouvert après EV");
                seller.Remember();
                mark = sel.ReceivedCount;
                sent = await shop.AskMerchantModeAsync().ConfigureAwait(false);
                Check(sent.Sent, "Eq refusé par le bot : " + sent.Message);
                string tax = await sel.Expect(p => p.StartsWith("Eq", StringComparison.Ordinal) || p == "Im113" || p == "Im123" || p == "Im176" || p.StartsWith("Im125", StringComparison.Ordinal),
                    "Eq1 (taxe) ou refus Im", 10, mark).ConfigureAwait(false);
                if (tax == "Im113") throw new SkipScenario("StarLoco interdit les marchands sur la carte " + shopMap.MapID + " (Im113)");
                if (tax.StartsWith("Im125", StringComparison.Ordinal)) throw new SkipScenario("trop de marchands sur la carte " + shopMap.MapID + " (" + tax + ")");
                Check(tax == "Eq1|1|0", "Taxe du mode marchand : " + tax + " (attendu Eq1|1|0, prix " + MerchantPrice + " ÷ 1 000)");
                Check(await sel.Until(() => shop.PendingTax != null, 5).ConfigureAwait(false), "Eq1 non retenu par le bot");
                int sentMark = sel.SentCount;
                sent = await shop.ConfirmMerchantModeAsync().ConfigureAwait(false);
                Check(sent.Sent, "EQ refusé par le bot : " + sent.Message);
                merchantMode = true;
                Check(sel.SentSince(sentMark).Contains("EQ"), "EQ non envoyé");
                Check(await sel.Until(() => sel.Account.Connexion == null || !sel.Account.Connexion.IsConnected(), 15).ConfigureAwait(false), "StarLoco n'a pas déconnecté le vendeur après EQ");
                evidence.Add("vendeur : ER6| → " + eck6 + ", EMO+" + woodId + "|1|" + MerchantPrice + " → " + Short(listed, 40) + ", EV, Eq → " + tax + ", EQ → déconnecté");
                await Task.Delay(2000).ConfigureAwait(false);

                // Acheteur, avec sa fenêtre de jeu : le marchand arrive par GM|~ (liste des marchands de la carte, après GDK).
                LiveSession ach = buyer.Session = new LiveSession(buyer.Label, buyer.Character.Login, buyer.Character.Password);
                evidence.Add(await EnterExistingAsync(ach, buyer.Character.Id, buyer.Character.Name, shopMap.MapID, s =>
                    s.Account.Game.character.Player_Selection += () => OnUi(() =>
                    {
                        if (screen != null) return;
                        screen = new GameClientFullform(s.Account) { StartPosition = FormStartPosition.Manual, Location = new Point(60, 60), Size = new Size(1000, 700), ShowInTaskbar = false };
                        screen.Show();
                    })).ConfigureAwait(false));
                string sellerKey = ";" + seller.Character.Id.ToString(CultureInfo.InvariantCulture) + ";" + seller.Character.Name + ";-5;";
                string gm = await ach.Expect(p => p.StartsWith("GM|", StringComparison.Ordinal) && p.Contains("~") && p.Contains(sellerKey), "GM|~ du marchand", 10, 0).ConfigureAwait(false);
                Check(await ach.Until(() => ach.Map.GetActor(seller.Character.Id) is MerchantActor, 5).ConfigureAwait(false),
                    "Marchand non posé sur la carte de l'acheteur : " + (ach.Map.GetActor(seller.Character.Id)?.GetType().Name ?? "absent"));
                MapActor merchant = ach.Map.GetActor(seller.Character.Id);
                Check(merchant.CellId == sellerCell.CellID, "Marchand en " + merchant.CellId + " au lieu de " + sellerCell.CellID);
                Check(await WaitOnUi(() => screen != null && screen.IsHandleCreated && screen.Visible && screen.Map?.Router != null ? screen : null, 20).ConfigureAwait(false) != null,
                    "La fenêtre de jeu de l'acheteur ne s'est pas ouverte");
                evidence.Add("acheteur : " + Short(gm.Substring(gm.IndexOf('~')), 60) + " → marchand en " + merchant.CellId);

                // Clic gauche simple : menu du marchand, rien envoyé (ni ER4, ni GA001).
                string buyText = Tool_BotProtocol.Game.Habitat.HouseTexts.Text("BUY", "Acheter");
                sentMark = ach.SentCount;
                string[] menu = await ClickActorAsync(screen, merchant, null).ConfigureAwait(false);
                await Task.Delay(300).ConfigureAwait(false);
                Check(menu.Contains(buyText), "Menu du marchand au clic gauche : " + string.Join(" | ", menu));
                Check(!ach.SentSince(sentMark).Any(p => p.StartsWith("ER", StringComparison.Ordinal) || p.StartsWith("GA001", StringComparison.Ordinal)),
                    "Le clic simple sur le marchand a envoyé : " + string.Join(" ", ach.SentSince(sentMark)));
                evidence.Add("clic simple → menu « " + string.Join(" | ", menu) + " », aucun paquet");

                // Maj + clic : l'entrée par défaut (Acheter) → ER4|<marchand>|<cellule> → ECK4 + EL.
                MerchantExchange store = ach.Account.Game.Interactions.Merchant;
                mark = ach.ReceivedCount; sentMark = ach.SentCount;
                Task routed = await OnUi(() => screen.Map.Router.RouteActorAsync(merchant, (short)merchant.CellId, MouseButtons.Left, Keys.Shift)).ConfigureAwait(false);
                await routed.ConfigureAwait(false);
                string er4 = "ER4|" + seller.Character.Id.ToString(CultureInfo.InvariantCulture) + "|" + merchant.CellId.ToString(CultureInfo.InvariantCulture);
                Check(await ach.Until(() => ach.SentSince(sentMark).Contains(er4), 5).ConfigureAwait(false), "Maj + clic sans " + er4 + " : " + string.Join(" ", ach.SentSince(sentMark)));
                Check(!ach.SentSince(sentMark).Any(p => p.StartsWith("GA001", StringComparison.Ordinal)), "Maj + clic a envoyé un déplacement");
                string eck4 = await ach.Expect("ECK4", "ECK4 (magasin du marchand)", 10, mark).ConfigureAwait(false);
                string el = await ach.Expect("EL", "EL du marchand", 10, mark).ConfigureAwait(false);
                Check(await ach.Until(() => store.IsBuying && store.MerchantId == seller.Character.Id && store.Items.Any(i => i.Id == (uint)woodId), 5).ConfigureAwait(false),
                    "Magasin du marchand non lu : " + eck4 + " / " + el);
                MerchantItem lot = store.Items.First(i => i.Id == (uint)woodId);
                Check(lot.Quantity == 1 && lot.Price == MerchantPrice && lot.TemplateId == AshWood, "Lot du marchand : " + lot.Quantity + " × " + lot.TemplateId + " à " + lot.Price);
                evidence.Add("Maj + clic → " + er4 + " → " + eck4 + ", " + Short(el, 40));

                // Achat du lot entier : EB → EBK ; magasin vide, StarLoco retire le marchand (GM|-) et ferme la fenêtre (EV).
                mark = ach.ReceivedCount;
                sent = await store.BuyAsync((uint)woodId, 1).ConfigureAwait(false);
                Check(sent.Sent, "EB refusé par le bot : " + sent.Message);
                string ebk = await ach.Expect(p => p == "EBK" || p.StartsWith("EBE", StringComparison.Ordinal), "EBK", 10, mark).ConfigureAwait(false);
                Check(ebk == "EBK", "Achat refusé par StarLoco : " + ebk);
                string erased = await ach.Expect("GM|-" + seller.Character.Id.ToString(CultureInfo.InvariantCulture), "GM|- du marchand vidé", 10, mark).ConfigureAwait(false);
                await ach.Expect(p => p == "EV" || p == "EVa", "EV après l'achat", 10, mark).ConfigureAwait(false);
                Check(await ach.Until(() => ach.Map.GetActor(seller.Character.Id) == null && !store.IsOpen && buyer.Bag.GetByInventoryId((uint)woodId) != null, 5).ConfigureAwait(false),
                    "Après l'achat : marchand " + (ach.Map.GetActor(seller.Character.Id)?.GetType().Name ?? "retiré") + ", magasin ouvert " + store.IsOpen + ", bois dans le sac " + (buyer.Bag.GetByInventoryId((uint)woodId) != null));
                buyer.Remember();
                seller.Seen.Remove((uint)woodId); // passé dans le sac de l'acheteur : nettoyé avec lui
                evidence.Add("EB" + woodId + "|1 → " + ebk + ", " + erased + ", EV, bois dans le sac de l'acheteur");
                return string.Join(" ; ", evidence);
            }
            finally
            {
                // Vendeur passé en mode marchand : reconnecté pour que StarLoco retire tout marchand restant (Player.OnJoinGame) et que son sac soit vidé en jeu.
                if (merchantMode && seller.Character != null)
                {
                    try
                    {
                        seller.Session?.Dispose();
                        await Task.Delay(1000).ConfigureAwait(false);
                        seller.Session = new LiveSession(seller.Label + "2", seller.Character.Login, seller.Character.Password);
                        Journal("[test] reconnexion du vendeur : " + await EnterExistingAsync(seller.Session, seller.Character.Id, seller.Character.Name, shopMap.MapID).ConfigureAwait(false));
                        if (buyer.Session != null && InGame(buyer.Session))
                            Journal("[test] vendeur vu par l'acheteur après sa reconnexion : " + (await buyer.Session.Until(() => buyer.Session.Map.GetActor(seller.Character.Id) is PlayerActor, 10).ConfigureAwait(false) ? "joueur" : "pas en joueur"));
                    }
                    catch (Exception error) { Journal("!! [test] reconnexion du vendeur impossible : " + error.Message); }
                }
                await ClosePetOwnerAsync(db, schema, buyer, () => CloseScreenAsync(screen)).ConfigureAwait(false);
                await ClosePetOwnerAsync(db, schema, seller).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Livre des artisans (gfx 7035 de la carte 1064 de StarLoco) et sa compétence « Consulter » (170, <c>ECK14</c>).</summary>
    private const int CrafterBookGfx = 7035;
    private const short CrafterBookSkill = 170;

    /// <summary>
    /// Livre des artisans de la carte, utilisé par le client dans sa fenêtre de jeu (lot X6, <c>CrafterCard</c> du client) : <c>GA500&lt;cellule&gt;;170</c>
    /// → <c>ECK14|&lt;métiers&gt;</c> et volet « Livre des artisans » ; le choix du métier 11 envoie <c>EJF11</c> et StarLoco répond
    /// <c>EJ+11;&lt;artisan&gt;;&lt;nom&gt;;…</c> (tous les joueurs connectés qui exercent le métier). La ligne de l'artisan sélectionnée,
    /// « Message privé » prépare « /w &lt;nom&gt; » dans la console du chat et « Localiser » pose la boussole du bandeau sur les coordonnées de
    /// sa carte (<c>maps.xml</c>), sans paquet ; « Fermer » envoie <c>EV</c>. Sans livre sur la carte, rien n'est rejoué et la preuve le dit.
    /// </summary>
    private static async Task<string> CrafterBookAsync(LiveSession cli, GameClientFullform screen, long artisanId, string artisanName, int mapId)
    {
        var book = cli.Map.Interactives.FirstOrDefault(p => p.Value != null && p.Value.gfx == CrafterBookGfx && p.Value.HasSkill(CrafterBookSkill));
        if (book.Value == null) return "pas de livre des artisans (gfx " + CrafterBookGfx + ") sur la carte " + mapId + " : carte de l'artisan non rejouée";
        Point? expectedCompass = null;
        try { expectedCompass = LangData.Map.Coords(mapId); }
        catch (Exception error) when (!(error is OutOfMemoryException)) { Journal("[test] coordonnées de la carte " + mapId + " illisibles : " + error.Message); }
        Outil_Azur_complet.Bot.Panels.CrafterListPanel panel = await OnUi(() => screen.Panels.Get<Outil_Azur_complet.Bot.Panels.CrafterListPanel>()).ConfigureAwait(false);
        Check(panel != null, "Volet « Livre des artisans » absent de la fenêtre de jeu");
        int mark = cli.ReceivedCount, sent = cli.SentCount;
        var used = await cli.Account.Game.Interactions.Interactive.UseAsync((short)book.Key, CrafterBookSkill).ConfigureAwait(false);
        Check(used.Sent, "Livre des artisans en " + book.Key + " refusé par le bot : " + used.Message);
        string use = "GA500" + book.Key.ToString(CultureInfo.InvariantCulture) + ";" + CrafterBookSkill.ToString(CultureInfo.InvariantCulture);
        Check(await cli.Until(() => cli.SentSince(sent).Contains(use), 25).ConfigureAwait(false), "Livre utilisé sans " + use + " : " + string.Join(" ", cli.SentSince(sent)));
        string eck = await cli.Expect(p => p.StartsWith("ECK14", StringComparison.Ordinal), "ECK14 du livre des artisans", 20, mark).ConfigureAwait(false);
        Check(eck.Substring(eck.IndexOf('|') + 1).Split(';').Contains(SwordSmithJob.ToString(CultureInfo.InvariantCulture)), "ECK14 sans le métier " + SwordSmithJob + " : " + eck);
        Check(await WaitOnUi(() => screen.Panels.Current == panel ? panel : null, 10).ConfigureAwait(false) != null, "Le volet « Livre des artisans » ne s'est pas ouvert sur " + eck);

        // Choix du métier : EJF11, puis EJ+11;<artisan>;… ; la ligne de l'artisan apparaît dans la liste.
        mark = cli.ReceivedCount; sent = cli.SentCount;
        Check(await OnUi(() => panel.ChooseJob(SwordSmithJob)).ConfigureAwait(false), "Métier " + SwordSmithJob + " absent du choix du livre");
        string ejf = "EJF" + SwordSmithJob.ToString(CultureInfo.InvariantCulture);
        Check(await cli.Until(() => cli.SentSince(sent).Contains(ejf), 5).ConfigureAwait(false), "Choix du métier sans " + ejf + " : " + string.Join(" ", cli.SentSince(sent)));
        string prefix = "EJ+" + SwordSmithJob.ToString(CultureInfo.InvariantCulture) + ";" + artisanId.ToString(CultureInfo.InvariantCulture) + ";";
        string listed = await cli.Expect(p => p.StartsWith(prefix, StringComparison.Ordinal), "EJ+ de l'artisan", 10, mark).ConfigureAwait(false);
        string id = artisanId.ToString(CultureInfo.InvariantCulture);
        ListView rows = await WaitOnUi(() => AllControls(panel.View).OfType<ListView>().FirstOrDefault(x => x.Name == "crafters-list"
            && x.Items.Cast<ListViewItem>().Any(row => (row.Tag as Crafter)?.Id == id)), 10).ConfigureAwait(false);
        Check(rows != null, "Ligne de l'artisan absente du livre après " + listed);

        // Carte de l'artisan : « Message privé » → « /w <nom> » ; « Localiser » → boussole ; aucun paquet.
        sent = cli.SentCount;
        string card = await OnUi(() =>
        {
            ListViewItem row = rows.Items.Cast<ListViewItem>().First(item => (item.Tag as Crafter)?.Id == id);
            foreach (ListViewItem other in rows.Items) other.Selected = false;
            row.Selected = true;
            Check(panel.SelectedCrafter?.Id == id, "Artisan non sélectionné dans le livre");
            Button whisper = AllControls(panel.View).OfType<Button>().First(x => x.Name == "crafters-whisper");
            Button locate = AllControls(panel.View).OfType<Button>().First(x => x.Name == "crafters-locate");
            Check(whisper.Enabled && locate.Enabled == (expectedCompass != null), "Boutons de la carte de l'artisan : Message privé " + whisper.Enabled + ", Localiser " + locate.Enabled);
            screen.Chat.Input.Clear();
            whisper.PerformClick();
            if (locate.Enabled) locate.PerformClick();
            return row.SubItems.Count > 2 ? row.Text + " " + row.SubItems[1].Text + " " + row.SubItems[2].Text : row.Text;
        }).ConfigureAwait(false);
        string whisperText = "/w " + artisanName + " ";
        Check(await WaitOnUi(() => screen.Chat.Input.Text == whisperText ? screen.Chat.Input.Text : null, 5).ConfigureAwait(false) != null,
            "« Message privé » n'a pas préparé « " + whisperText + " » : « " + await OnUi(() => screen.Chat.Input.Text).ConfigureAwait(false) + " »");
        Point? compass = await OnUi(() => panel.LastCompass).ConfigureAwait(false), banner = await OnUi(() => screen.Banner.Xtra.CompassTarget).ConfigureAwait(false);
        Check(expectedCompass == null || (compass == expectedCompass && banner == expectedCompass), "« Localiser » : boussole " + compass + " / bandeau " + banner + " (attendu " + expectedCompass + ")");
        await Task.Delay(300).ConfigureAwait(false);
        string[] unexpected = cli.SentSince(sent).Where(p => !p.StartsWith("BD", StringComparison.Ordinal) && p != "qping" && !p.StartsWith("ping", StringComparison.Ordinal)).ToArray();
        Check(unexpected.Length == 0, "La carte de l'artisan a envoyé des paquets : " + string.Join(" ", unexpected));
        await OnUi(() => screen.Chat.Input.Clear()).ConfigureAwait(false);

        // Fermer → EV ; le volet attend l'EV du serveur.
        mark = cli.ReceivedCount; sent = cli.SentCount;
        await OnUi(() => AllControls(panel.View).OfType<Button>().First(x => x.Name == "crafters-leave").PerformClick()).ConfigureAwait(false);
        Check(await cli.Until(() => cli.SentSince(sent).Contains("EV"), 5).ConfigureAwait(false), "« Fermer » du livre sans EV : " + string.Join(" ", cli.SentSince(sent)));
        await cli.Expect(p => p == "EV" || p == "EVa", "EV du livre", 10, mark).ConfigureAwait(false);
        Check(await WaitOnUi(() => !screen.Panels.IsOpen(panel) ? panel : null, 10).ConfigureAwait(false) != null && await cli.Until(() => cli.Account.AccountStates == AccountStates.CONNECTED_INACTIVE, 5).ConfigureAwait(false),
            "Livre encore ouvert après EV");
        return "livre en " + book.Key + " : " + use + " → " + eck + ", " + ejf + " → " + listed.Split(';').Take(5).Aggregate((x, y) => x + ";" + y) + "…, ligne « " + card + " », Message privé → « " + whisperText.TrimEnd()
            + " », Localiser → boussole " + (expectedCompass == null ? "(carte absente de maps.xml)" : expectedCompass.Value.X + "," + expectedCompass.Value.Y) + ", Fermer → EV";
    }

    /// <summary>Ferme et libère une fenêtre de jeu ouverte par un scénario (fil d'interface) ; rien si elle n'a pas été créée.</summary>
    private static Task CloseScreenAsync(GameClientFullform screen) =>
        screen == null ? Task.FromResult(true) : OnUi(() => { if (!screen.IsDisposed) { screen.Close(); screen.Dispose(); } });

    /// <summary>
    /// Marche de <paramref name="walker"/> jusqu'à <paramref name="cellId"/>, attendue jusqu'au bout : GA001 puis GKK envoyés, cellule atteinte vue
    /// par <paramref name="watcher"/>, puis un court délai pour que StarLoco traite le GKK (<c>setAway(false)</c> : une demande d'échange pendant
    /// la marche reçoit <c>EREO</c>). Faux si le bot refuse la marche ou n'arrive pas sur la cellule (l'appelant essaie une autre cellule).
    /// </summary>
    private static async Task<bool> WalkAsync(LiveSession walker, LiveSession watcher, int cellId)
    {
        int sent = walker.SentCount;
        var moved = await walker.Account.Game.Manager.Mouvements.MoveToAsync(walker.Map.GetCellFromId((short)cellId)).ConfigureAwait(false);
        if (moved != Tool_BotProtocol.Game.Managers.Mouvements.MoveResults.EXIT) { Journal(walker.Label + " [test] marche vers " + cellId + " refusée : " + moved); return false; }
        if (!await walker.Until(() => walker.SentSince(sent).Any(p => p.StartsWith("GA001", StringComparison.Ordinal)), 5).ConfigureAwait(false)) { Journal(walker.Label + " [test] marche vers " + cellId + " sans GA001"); return false; }
        if (!await walker.Until(() => walker.SentSince(sent).Any(p => p.StartsWith("GKK", StringComparison.Ordinal)), 20).ConfigureAwait(false)) { Journal(walker.Label + " [test] marche vers " + cellId + " sans GKK"); return false; }
        bool arrived = await walker.Until(() => walker.Map.Self.CellId == cellId, 5).ConfigureAwait(false)
            && await watcher.Until(() => watcher.Map.GetActor(walker.Id)?.CellId == cellId, 10).ConfigureAwait(false);
        if (!arrived) { Journal(walker.Label + " [test] marche vers " + cellId + " arrivée en " + walker.Map.Self.CellId + " (vu en " + watcher.Map.GetActor(walker.Id)?.CellId + ")"); return false; }
        await Task.Delay(600).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Clic gauche sur <paramref name="actor"/> dans la fenêtre <paramref name="form"/> (fil d'interface, <c>RouteActorAsync</c> comme un clic sur le
    /// sprite) ; rend les entrées du menu ouvert (en-tête compris) et, si <paramref name="click"/> est donné, clique cette entrée.
    /// </summary>
    private static async Task<string[]> ClickActorAsync(GameClientFullform form, MapActor actor, string click)
    {
        Check(actor != null, "Acteur absent de la carte");
        Task routed = await OnUi(() => form.Map.Router.RouteActorAsync(actor, (short)actor.CellId, MouseButtons.Left)).ConfigureAwait(false);
        await routed.ConfigureAwait(false);
        return await OnUi(() =>
        {
            ContextMenuStrip menu = form.Map.Router.LastMenu;
            Check(menu != null && !menu.IsDisposed, "Le clic gauche sur " + ActorClassifier.DisplayName(actor) + " n'a pas ouvert de menu");
            string[] texts = menu.Items.OfType<ToolStripItem>().Where(i => !(i is ToolStripSeparator)).Select(i => i.Text).ToArray();
            if (click != null)
            {
                ToolStripItem item = menu.Items.OfType<ToolStripMenuItem>().FirstOrDefault(i => i.Text == click);
                Check(item != null && item.Enabled, "Menu sans « " + click + " » actif : " + string.Join(" | ", texts));
                item.PerformClick();
            }
            menu.Close();
            return texts;
        }).ConfigureAwait(false);
    }
}

// Scénario x7 (lot X7) : événements de combat visibles (prêt, options d'équipe, pièges GDZ/GDC, invisibilité, journal dans le chat).
internal static partial class Live
{
    /// <summary>
    /// Sram de StarLoco (<c>Constant.CLASS_SRAM</c>) et ses trois sorts de départ (<c>Constant.getStartSortsPlaces</c>), le Piège Sournois
    /// au niveau 2 : la table <c>sorts</c> de StarLoco et <c>spells.xml</c> n'en permettent qu'un lancer par tour au niveau 1, deux au niveau 2.
    /// </summary>
    private const int SramClass = 4;
    private const string SramSpells = "61;1;b,72;1;c,65;2;d";
    /// <summary>Piège Sournois (taille 0 : portée « Ca ») et Invisibilité (3 tours : <c>GA;150</c> porte 3 - 1 = 2).</summary>
    private const short TrapSpell = 65, InvisibilitySpell = 72;
    /// <summary>
    /// <c>Constant.getTrapsColor(65)</c> de StarLoco ; <c>SpellEffect.applyEffect_400</c> envoie ensuite à l'équipe du lanceur
    /// <c>GDC&lt;cellule&gt;;Haaaaaaaaz3005;</c> : objet 2 numéro 25, actif, ligne de vue, masque 0x3005.
    /// </summary>
    private const int TrapColor = 7, TrapObject = 25, InvisibleTurns = 2;
    private const string TrapCell = "Haaaaaaaaz3005";

    private static UserMapControl UserMapOf(GameClientFullform form) =>
        form?.Map == null ? null : typeof(MapControl).GetField("UserMap", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form.Map) as UserMapControl;

    /// <summary>Objet 2 dessiné par le décor de la fenêtre sur la cellule (fil d'interface) ; -1 sans décor.</summary>
    private static int DrawnObject2(GameClientFullform form, int cellId)
    {
        UserMapControl user = UserMapOf(form);
        var artwork = user == null ? null : typeof(UserMapControl).GetField("artwork", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(user) as BotMapArtwork;
        return artwork == null || cellId < 0 || cellId >= artwork.Cells.Length ? -1 : artwork.Cells[cellId].Object2Id;
    }

    /// <summary>Objet 2 de la cellule d'après ses dix caractères effectifs (<see cref="Map.CellDataOf"/>) ; -1 hors carte.</summary>
    private static int Object2Of(LiveSession s, int cellId)
    {
        string data = s.Map.CellDataOf(cellId);
        return data == null ? -1 : s.Map.DecompressCell(data, (short)cellId).layer_object_2_num;
    }

    private static string OriginalCell(LiveSession s, int cellId) =>
        s.Map.MapData != null && (cellId + 1) * 10 <= s.Map.MapData.Length ? s.Map.MapData.Substring(cellId * 10, 10) : null;

    /// <summary>Clip « prêt » au-dessus de l'acteur et dessiné au dernier passage de la vue (bande lue, acteur visible).</summary>
    private static bool HasReadyClip(GameClientFullform form, long actorId) =>
        form.Map.ExtraClips.Active.Any(clip => clip.Family == ActorExtraClips.ReadyFamily && clip.ActorId == actorId && clip.Above && clip.Visible);

    /// <summary>
    /// Un Sram inventé (classe 4 et ses sorts de départ écrits dans la base) entre sur la carte de A avec sa propre fenêtre de jeu ; A le
    /// défie. Placement : A coche « aide » (<c>fH</c> → <c>Go+H&lt;A&gt;</c> aux joueurs de la carte d'origine seulement : icône au-dessus
    /// de l'épée de l'équipe de A dans la fenêtre de B, rien chez le Sram) ;
    /// le Sram se dit prêt (<c>GR1&lt;Sram&gt;</c> aux deux équipes : clip « prêt » au-dessus de lui dans les deux fenêtres), puis A
    /// (<c>GS</c>) ; le premier <c>GTS</c> retire les clips. Au tour du Sram : deux Pièges Sournois (<c>GA;999;…;GDZ+…;0;7</c> et
    /// <c>GA;999;…;GDC…;Haaaaaaaaz3005;</c> à sa seule équipe : zone gardée, objet 25 redessiné dans sa fenêtre, rien chez A) ;
    /// Invisibilité (<c>GA;150;&lt;Sram&gt;;&lt;Sram&gt;,2</c> aux deux : masqué chez A, 40 % chez lui, ligne « … devient invisible. » dans
    /// le chat de A) ; un pas sur le premier piège le déclenche (<c>GDZ-</c> et <c>GDC&lt;cellule&gt;</c> seul : zone effacée, cellule
    /// d'origine redessinée). L'abandon du Sram termine le combat ; après <c>GE</c>, <c>GC1</c> et <c>GDM</c>, le second piège a disparu
    /// de la carte du Sram (cellule d'origine) et il est de nouveau visible chez A. Compte et personnage supprimés de la base.
    /// </summary>
    private static async Task<string> FightEventsAsync()
    {
        RequireMaps();
        if (!LangData.IsLoaded("spells")) throw new SkipScenario("textes du client « spells » absents : le bot ne connaît ni les PA ni la portée du piège et de l'invisibilité");
        using (MySql.Data.MySqlClient.MySqlConnection db = OpenAccountsDatabase(out string schema))
        {
            var created = new List<InventedCharacter>();
            var evidence = new List<string>();
            LiveSession s = null;
            GameClientFullform screen = null, viewer = null;
            try
            {
                Cell place = FreeCellNear(a, 2, 6);
                Check(place != null, "Aucune cellule libre près de A pour le Sram");
                InventedCharacter sram = Invent(db, schema, a.Id, a.MapId, place.CellID, 10000, created);
                Sql(db, "UPDATE " + schema + ".players SET class = @class, gfx = @gfx, spells = @spells WHERE id = @id AND account = @account",
                    "@class", SramClass, "@gfx", SramClass * 10 + sram.Sex, "@spells", SramSpells, "@id", sram.Id, "@account", sram.Guid);
                s = new LiveSession("S", sram.Login, sram.Password);
                evidence.Add(await EnterExistingAsync(s, sram.Id, sram.Name, a.MapId, started =>
                    started.Account.Game.character.Player_Selection += () => OnUi(() => { if (screen == null) { screen = OpenScreen(started); screen.Show(); } })).ConfigureAwait(false));
                LiveSession sr = s;
                Check(sr.Account.Game.character.Race_ID == SramClass && await sr.Until(() => sr.Account.Game.character.Spells.Values.Any(x => x.ID == TrapSpell)
                    && sr.Account.Game.character.Spells.Values.Any(x => x.ID == InvisibilitySpell), 10).ConfigureAwait(false),
                    "Le bot ne voit pas un Sram avec le Piège Sournois et l'Invisibilité : classe " + sr.Account.Game.character.Race_ID + ", sorts "
                    + string.Join(",", sr.Account.Game.character.Spells.Values.Select(x => x.ID)));
                Check(await WaitOnUi(() => screen != null && screen.IsHandleCreated && screen.Visible ? screen : null, 20).ConfigureAwait(false) != null, "La fenêtre de jeu du Sram ne s'est pas ouverte");
                Check(await a.Until(() => a.Map.GetActor(sr.Id) != null, 10).ConfigureAwait(false) && await sr.Until(() => sr.Map.GetActor(a.Id) != null, 10).ConfigureAwait(false),
                    "A et le Sram ne se voient pas sur la carte " + a.MapId);

                // Fenêtre de B, qui reste hors du combat sur la carte : il voit les épées et les options d'équipe.
                viewer = await OnUi(() => { GameClientFullform opened = OpenScreen(b); opened.Location = new Point(120, 120); opened.Show(); return opened; }).ConfigureAwait(false);
                Check(await WaitOnUi(() => viewer.IsHandleCreated && viewer.Visible && UserMapOf(viewer) != null ? viewer : null, 20).ConfigureAwait(false) != null, "La fenêtre de B ne s'est pas ouverte");

                // Défi de A accepté par le Sram (GA;900 → GA901) : deux équipes, placement.
                int markA = a.ReceivedCount, markS = sr.ReceivedCount;
                var challenge = await a.Account.Game.Interactions.MapActions.ChallengeAsync(sr.Id).ConfigureAwait(false);
                Check(challenge.Sent, "Défi refusé par le bot : " + challenge.Message);
                await sr.Expect(p => p.StartsWith("GA;900;" + a.Id + ";", StringComparison.Ordinal), "GA;900 (défi reçu par le Sram)", 10, markS).ConfigureAwait(false);
                Check(await sr.Until(() => sr.Account.Game.Interactions.MapActions.PendingChallenge?.ChallengerId == a.Id, 5).ConfigureAwait(false), "Le Sram n'a pas de défi en attente");
                var accept = await sr.Account.Game.Interactions.MapActions.AcceptChallengeAsync(a.Id).ConfigureAwait(false);
                Check(accept.Sent, "Acceptation du défi refusée par le bot : " + accept.Message);
                await a.Expect("GJK", "GJK (entrée en combat, A)", 15, markA).ConfigureAwait(false);
                await sr.Expect("GJK", "GJK (entrée en combat, Sram)", 15, markS).ConfigureAwait(false);
                Fights fa = a.Account.Game.Fight, fs = sr.Account.Game.Fight;
                Check(await a.Until(() => fa.IsPlacement && fs.IsPlacement && Self(a) != null && Self(sr) != null && Enemies(a).Any(e => e.Id == sr.Id), 10).ConfigureAwait(false),
                    "Placement incomplet : " + string.Join(", ", fa.Fighters.Values.Select(f => f.Name + " équipe " + f.Team)));

                // Option « aide » de l'équipe de A : fH → Go+H<A> aux seuls joueurs de la carte d'origine (getCurMap de l'initiateur) ;
                // B, resté sur la carte, voit l'icône NeedHelp au-dessus de l'épée de l'équipe de A (sprite qui porte l'identifiant de l'équipe).
                Check(await b.Until(() => b.Map.FightSwords.Values.Any(swords => swords.Teams.Any(team => team.TeamId == a.Id)), 10).ConfigureAwait(false),
                    "B ne voit pas l'épée de l'équipe de A (Gc+)");
                int markB = b.ReceivedCount; markS = sr.ReceivedCount;
                var help = await fa.ToggleOptionAsync(FightOptions.NeedHelp).ConfigureAwait(false);
                Check(help.Sent, "« Aide » refusée par le bot : " + help.Message);
                string go = await b.Expect(p => p == "Go+H" + a.Id, "Go+H<A> chez B", 10, markB).ConfigureAwait(false);
                string[] icons = await WaitOnUi(() => { string[] files = UserMapOf(viewer)?.OptionIconsOf(a.Id); return files != null && files.Length > 0 ? files : null; }, 10).ConfigureAwait(false);
                Check(icons != null && icons.SequenceEqual(new[] { "UI/Client/UI_FightOptionNeedHelpUp.png" }),
                    "Icônes d'options au-dessus de l'épée de A dans la fenêtre de B : " + (icons == null ? "aucune" : string.Join(",", icons)));
                Check(!sr.ReceivedSince(markS).Any(p => p.StartsWith("Go", StringComparison.Ordinal)), "Le Sram, en combat, a reçu Go");
                await Capture("22-reel-x7-options", viewer).ConfigureAwait(false);
                evidence.Add("fH → " + go + " à B seul : icône « aide » au-dessus de l'épée de l'équipe de A dans sa fenêtre");

                // Prêt du Sram : GR1<Sram> aux deux équipes → clip « prêt » au-dessus de lui dans les deux fenêtres.
                markA = a.ReceivedCount; markS = sr.ReceivedCount;
                var ready = await fs.SetReadyAsync(true).ConfigureAwait(false);
                Check(ready.Sent, "« Prêt » du Sram refusé par le bot : " + ready.Message);
                string gr = await a.Expect(p => p == "GR1" + sr.Id, "GR1<Sram> chez A", 10, markA).ConfigureAwait(false);
                Check(await a.Until(() => fa.IsFighterReady(sr.Id) && !fa.IsFighterReady(a.Id), 5).ConfigureAwait(false), "A ne garde pas l'état « prêt » du Sram seul");
                Check(await WaitOnUi(() => HasReadyClip(window, sr.Id) && HasReadyClip(screen, sr.Id) && !HasReadyClip(window, a.Id) ? "prêt" : null, 10).ConfigureAwait(false) != null,
                    "Clip « prêt » absent au-dessus du Sram (fenêtre de A " + await OnUi(() => HasReadyClip(window, sr.Id)).ConfigureAwait(false)
                    + ", fenêtre du Sram " + await OnUi(() => HasReadyClip(screen, sr.Id)).ConfigureAwait(false) + ")");
                await Capture("22-reel-x7-pret").ConfigureAwait(false);
                evidence.Add(gr + " : clip « prêt » au-dessus du Sram dans les deux fenêtres");

                // Prêt de A : GS ; le premier GTS retire les clips (Game.onTurnStart, isFirstTurn).
                await ReadyAsync(a, markA).ConfigureAwait(false);
                await a.Expect("GS", "GS (début du combat)", 30, markA).ConfigureAwait(false);
                await a.Expect("GTS", "GTS (premier tour)", 15, markA).ConfigureAwait(false);
                Check(await a.Until(() => !fa.IsFighterReady(sr.Id) && !fs.IsFighterReady(sr.Id), 5).ConfigureAwait(false), "L'état « prêt » survit au premier tour");
                Check(await WaitOnUi(() => !window.Map.ExtraClips.Active.Any(clip => clip.Family == ActorExtraClips.ReadyFamily)
                    && !screen.Map.ExtraClips.Active.Any(clip => clip.Family == ActorExtraClips.ReadyFamily) ? "retirés" : null, 10).ConfigureAwait(false) != null,
                    "Clips « prêt » encore affichés après le premier GTS");
                evidence.Add("GS puis GTS : clips « prêt » retirés");

                // Tours : A passe, le Sram joue le sien.
                DateTime until = DateTime.UtcNow.AddSeconds(80);
                string played = null;
                int turns = 0;
                while (played == null && DateTime.UtcNow < until && fa.IsInFight)
                {
                    int actor = await WaitForTurnOf(new[] { a, sr }, 40).ConfigureAwait(false);
                    turns++;
                    if (actor == a.Id)
                    {
                        int mark = a.ReceivedCount;
                        var passed = await fa.PassTurnAsync().ConfigureAwait(false);
                        Check(passed.Sent, "Fin de tour de A refusée par le bot : " + passed.Message);
                        await a.Expect("GTF" + a.Id, "GTF (fin du tour de A)", 10, mark).ConfigureAwait(false);
                        await a.Until(() => !fa.IsMyTurn, 5).ConfigureAwait(false);
                    }
                    else if (actor == sr.Id) played = await SramTurnAsync(sr, screen, evidence).ConfigureAwait(false);
                }
                Check(played != null, "Le Sram n'a pas eu son tour en " + turns + " tour(s)");

                // Abandon du Sram : GE, GC1 → GCK, GDM : carte rechargée, second piège disparu, Sram visible chez A.
                int endA = a.ReceivedCount, endS = sr.ReceivedCount;
                var giveUp = await fs.GiveUpAsync().ConfigureAwait(false);
                Check(giveUp.Sent, "Abandon refusé par le bot : " + giveUp.Message);
                await a.Expect("GE", "GE (fin du combat, A)", 20, endA).ConfigureAwait(false);
                await sr.Expect(p => p.StartsWith("GE", StringComparison.Ordinal) || p.StartsWith("GV", StringComparison.Ordinal), "GE ou GV (fin du combat, Sram)", 20, endS).ConfigureAwait(false);
                await WaitBackOnMap(a).ConfigureAwait(false); await WaitBackOnMap(sr).ConfigureAwait(false);
                await sr.Expect("GDM", "GDM (carte rechargée après le combat, Sram)", 15, endS).ConfigureAwait(false);
                int second = int.Parse(played, CultureInfo.InvariantCulture);
                // StarLoco retire les pièges d'un combattant mort ou parti (Fight.onFighterDie → Trap.desappear) avant GE ; sinon GDM recharge la carte.
                string[] ending = sr.ReceivedSince(endS);
                int removedAt = Array.FindIndex(ending, p => p == "GA;999;" + sr.Id + ";GDC" + second), endedAt = Array.FindIndex(ending, p => p.StartsWith("GE", StringComparison.Ordinal));
                Check(await sr.Until(() => sr.Map.CellDataOf(second) == OriginalCell(sr, second) && Object2Of(sr, second) != TrapObject, 10).ConfigureAwait(false),
                    "Le second piège reste sur la carte du Sram après le combat : cellule " + second + " = " + sr.Map.CellDataOf(second));
                Check(await WaitOnUi(() => DrawnObject2(screen, second) != TrapObject && DrawnObject2(screen, second) >= 0 ? "restauré" : null, 15).ConfigureAwait(false) != null,
                    "La fenêtre du Sram dessine encore le piège en " + second + " après le combat (objet " + await OnUi(() => DrawnObject2(screen, second)).ConfigureAwait(false) + ")");
                Check(await WaitOnUi(() => { UserMapControl.ActorVisualState state = UserMapOf(window)?.GetActorVisualState(sr.Id); return state != null && state.IsVisible && state.Alpha == 1f ? state : null; }, 15).ConfigureAwait(false) != null,
                    "Le Sram n'est pas redevenu visible dans la fenêtre de A après le combat");
                evidence.Add("abandon du Sram → " + (removedAt >= 0 && (endedAt < 0 || removedAt < endedAt) ? "GDZ-/GDC" + second + " (piège retiré), " : "") + "GE, GDM : cellule " + second
                    + " d'origine sur sa carte et dans sa fenêtre, Sram visible chez A");
                return string.Join(" ; ", evidence);
            }
            finally
            {
                if (screen != null) await OnUi(() => { if (!screen.IsDisposed) { screen.Close(); screen.Dispose(); } }).ConfigureAwait(false);
                // Fenêtre de B, session partagée par les scénarios suivants : la fermer (Close → FormClosed → OnSessionClosed)
                // libérerait le compte de B et le déconnecterait (x3c échouait sur « B n'est plus en jeu ») ; Dispose seul ne
                // libère que l'interface (ReleaseUi), sans FormClosed.
                if (viewer != null) await OnUi(() => { if (!viewer.IsDisposed) { viewer.Hide(); viewer.Dispose(); } }).ConfigureAwait(false);
                if (viewer != null && (b.Account.Connexion == null || !b.Account.Connexion.IsConnected())) Journal("!! [test] B n'est plus connecté après la fermeture de sa fenêtre");
                await ForgetAsync(db, schema, s == null ? new LiveSession[0] : new[] { s }, created).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Tour du Sram : deux Pièges Sournois (le premier à un pas, le second plus loin), Invisibilité sur lui-même, un pas sur le premier
    /// piège pour le déclencher. Rend le numéro de la cellule du second piège, laissé en place jusqu'à la fin du combat.
    /// </summary>
    private static async Task<string> SramTurnAsync(LiveSession sr, GameClientFullform screen, List<string> evidence)
    {
        Fights fa = a.Account.Game.Fight, fs = sr.Account.Game.Fight;
        CombatFighter self = Self(sr);
        Check(self != null && self.CellId >= 0, "Cellule du Sram inconnue");
        Cell own = sr.Map.GetCellFromId((short)self.CellId);
        var occupied = new HashSet<int>(fs.Fighters.Values.Where(f => !f.IsDead).Select(f => (int)f.CellId));
        Func<Cell, bool> nearEnemy = c => Enemies(sr).Any(e => sr.Map.GetCellFromId((short)e.CellId)?.GetDistanceBetweenCells(c) <= 1);
        Check(!nearEnemy(own), "Le Sram commence son tour au contact de A : un pas serait taclé");
        List<Cell> traps = sr.Map.MapCells.Where(c => c != null && c.IsWalkable() && c.C_Types != Tool_BotProtocol.Game.Maps.Enums.CellTypes.TELEPORT_CELL
                && !occupied.Contains(c.CellID) && !nearEnemy(c) && fs.GetSpellUnavailableReason(TrapSpell, c.CellID) == null)
            .OrderBy(c => own.GetDistanceBetweenCells(c)).ThenBy(c => c.CellID).ToList();
        Cell first = traps.FirstOrDefault(c => own.GetDistanceBetweenCells(c) == 1);
        Cell second = first == null ? null : traps.FirstOrDefault(c => own.GetDistanceBetweenCells(c) >= 2 && first.GetDistanceBetweenCells(c) >= 2);
        Check(first != null && second != null, "Pas deux cellules où poser un Piège Sournois autour de " + own.CellID + " (" + traps.Count + " possibles)");
        string firstOriginal = OriginalCell(sr, first.CellID), secondOriginal = OriginalCell(sr, second.CellID);
        int firstObject = Object2Of(sr, first.CellID);
        Check(firstObject != TrapObject && Object2Of(sr, second.CellID) != TrapObject, "Objet 25 déjà posé avant les pièges");

        foreach (Cell trap in new[] { first, second })
        {
            int markS = sr.ReceivedCount, markA = a.ReceivedCount, paBefore = fs.ActionPoints;
            var cast = await fs.CastSpellAsync(TrapSpell, trap.CellID).ConfigureAwait(false);
            Check(cast.Sent, "Piège Sournois refusé par le bot en " + trap.CellID + " : " + cast.Message);
            string launched = await sr.Expect(p => p.StartsWith("GA;300;" + sr.Id + ";", StringComparison.Ordinal) || p.StartsWith("GA;302;" + sr.Id + ";", StringComparison.Ordinal)
                || p.StartsWith("GA;0", StringComparison.Ordinal) || p.StartsWith("GAE", StringComparison.Ordinal), "GA;300 du Piège Sournois", 10, markS).ConfigureAwait(false);
            Check(launched.StartsWith("GA;300;", StringComparison.Ordinal), "StarLoco ne lance pas le Piège Sournois en " + trap.CellID + " : " + launched);
            string gdz = "GA;999;" + sr.Id + ";GDZ+" + trap.CellID + ";0;" + TrapColor, gdc = "GA;999;" + sr.Id + ";GDC" + trap.CellID + ";" + TrapCell + ";";
            await sr.Expect(p => p == gdz, gdz, 10, markS).ConfigureAwait(false);
            await sr.Expect(p => p == gdc, gdc, 10, markS).ConfigureAwait(false);
            Check(await sr.Until(() => !fs.IsActionPending && fs.ActionPoints < paBefore, 10).ConfigureAwait(false), "Le Sram attend encore la fin du piège (PA " + fs.ActionPoints + ")");
            Check(await sr.Until(() => fs.Zones.Any(z => z.CellId == trap.CellID && z.Size == 0 && z.Color == TrapColor) && Object2Of(sr, trap.CellID) == TrapObject, 5).ConfigureAwait(false),
                "Le Sram ne garde pas la zone ou l'objet 25 du piège en " + trap.CellID + " : zones " + string.Join(",", fs.Zones.Select(z => z.CellId + "/" + z.Size + "/" + z.Color))
                + ", objet " + Object2Of(sr, trap.CellID));
            Check(FightZoneLayer.CellsOf(fs.Zones.First(z => z.CellId == trap.CellID), sr.Map.MapCells).SequenceEqual(new[] { (int)trap.CellID }), "La zone de taille 0 ne couvre pas sa seule cellule");
            Check(await WaitOnUi(() => DrawnObject2(screen, trap.CellID) == TrapObject ? "redessinée" : null, 10).ConfigureAwait(false) != null,
                "La fenêtre du Sram ne dessine pas l'objet 25 en " + trap.CellID + " (objet " + await OnUi(() => DrawnObject2(screen, trap.CellID)).ConfigureAwait(false) + ")");
            // Équipe adverse : StarLoco n'envoie ni GDZ ni GDC ; A ne voit ni zone ni objet.
            Check(!a.ReceivedSince(markA).Any(p => p.Contains("GDZ") || p.Contains(";GDC")), "A a reçu le piège de l'équipe adverse");
            Check(fa.Zones.Count == 0 && Object2Of(a, trap.CellID) != TrapObject, "A voit le piège de l'équipe adverse en " + trap.CellID);
            evidence.Add("piège en " + trap.CellID + " : " + Short(gdz, 50) + " et " + Short(gdc, 50) + " au Sram seul, zone et objet 25 dans sa fenêtre, rien chez A");
        }
        await Capture("22-reel-x7-pieges", screen).ConfigureAwait(false);

        // Invisibilité sur soi : GA;150;<Sram>;<Sram>,2 aux deux équipes.
        {
            int markS = sr.ReceivedCount, markA = a.ReceivedCount;
            var cast = await fs.CastSpellAsync(InvisibilitySpell, own.CellID).ConfigureAwait(false);
            Check(cast.Sent, "Invisibilité refusée par le bot : " + cast.Message);
            string expected = "GA;150;" + sr.Id + ";" + sr.Id + "," + InvisibleTurns;
            string launched = await sr.Expect(p => p.StartsWith("GA;300;" + sr.Id + ";", StringComparison.Ordinal) || p.StartsWith("GA;302;" + sr.Id + ";", StringComparison.Ordinal)
                || p.StartsWith("GA;0", StringComparison.Ordinal) || p.StartsWith("GAE", StringComparison.Ordinal), "GA;300 de l'Invisibilité", 10, markS).ConfigureAwait(false);
            Check(launched.StartsWith("GA;300;", StringComparison.Ordinal), "StarLoco ne lance pas l'Invisibilité : " + launched);
            await sr.Expect(p => p == expected, expected + " (Sram)", 10, markS).ConfigureAwait(false);
            await a.Expect(p => p == expected, expected + " (A)", 10, markA).ConfigureAwait(false);
            Check(await sr.Until(() => !fs.IsActionPending, 10).ConfigureAwait(false), "Le Sram attend encore la fin de l'Invisibilité");
            Check(await a.Until(() => fa.IsFighterInvisible(sr.Id), 5).ConfigureAwait(false) && fs.IsFighterInvisible(sr.Id), "Invisibilité non retenue par les deux bots");
            UserMapControl.ActorVisualState hidden = await WaitOnUi(() => { UserMapControl.ActorVisualState state = UserMapOf(window)?.GetActorVisualState(sr.Id); return state != null && !state.IsVisible ? state : null; }, 10).ConfigureAwait(false);
            Check(hidden != null && (hidden.SpriteReason ?? string.Empty).Contains("invisible"), "Le Sram reste dessiné dans la fenêtre de A après GA;150");
            UserMapControl.ActorVisualState ghost = await WaitOnUi(() => { UserMapControl.ActorVisualState state = UserMapOf(screen)?.GetActorVisualState(sr.Id); return state != null && state.IsVisible && Math.Abs(state.Alpha - UserMapControl.InvisibleSelfAlpha) < .01f ? state : null; }, 10).ConfigureAwait(false);
            Check(ghost != null, "Le Sram n'est pas dessiné à 40 % dans sa propre fenêtre");
            string line = sr.Account.Game.character.Name + " devient invisible.";
            Check(await WaitOnUi(() => window.Chat.FightInfos && window.Chat.View.DisplayedLines.Any(l => l.Filter == ChatFilter.Infos && l.Text == line) ? "chat" : null, 10).ConfigureAwait(false) != null,
                "Ligne « " + line + " » absente du chat de A (infos de combat " + await OnUi(() => window.Chat.FightInfos).ConfigureAwait(false) + ")");
            await Capture("22-reel-x7-invisible").ConfigureAwait(false);
            evidence.Add(expected + " aux deux : masqué chez A, 40 % chez lui, « " + line + " » dans le chat de A");
        }

        // Un pas sur le premier piège : StarLoco le déclenche (verifyTraps) → GDZ- puis GDC<cellule> seul à l'équipe du Sram.
        {
            int markS = sr.ReceivedCount;
            var moved = await fs.MoveAsync(first.CellID).ConfigureAwait(false);
            Check(moved.Sent, "Pas vers le piège refusé par le bot : " + moved.Message);
            string gdz = "GA;999;" + sr.Id + ";GDZ-" + first.CellID + ";0;" + TrapColor, gdc = "GA;999;" + sr.Id + ";GDC" + first.CellID;
            await sr.Expect(p => p == gdz, gdz, 15, markS).ConfigureAwait(false);
            await sr.Expect(p => p == gdc, gdc, 10, markS).ConfigureAwait(false);
            string trapped = await sr.Expect(p => p.StartsWith("GA;307;" + sr.Id + ";", StringComparison.Ordinal), "GA;307 (piège déclenché)", 10, markS).ConfigureAwait(false);
            Check(await sr.Until(() => !fs.Zones.Any(z => z.CellId == first.CellID) && fs.Zones.Any(z => z.CellId == second.CellID)
                && sr.Map.CellDataOf(first.CellID) == firstOriginal && sr.Map.CellDataOf(second.CellID) != secondOriginal, 10).ConfigureAwait(false),
                "Après le déclenchement : zones " + string.Join(",", fs.Zones.Select(z => z.CellId)) + ", cellule " + first.CellID + " = " + sr.Map.CellDataOf(first.CellID) + " (origine " + firstOriginal + ")");
            Check(await WaitOnUi(() => DrawnObject2(screen, first.CellID) == firstObject && DrawnObject2(screen, second.CellID) == TrapObject ? "restaurée" : null, 10).ConfigureAwait(false) != null,
                "La fenêtre du Sram ne redessine pas la cellule " + first.CellID + " d'origine (objet " + await OnUi(() => DrawnObject2(screen, first.CellID)).ConfigureAwait(false) + ", attendu " + firstObject + ")");
            evidence.Add("pas sur " + first.CellID + " : " + gdz + ", " + gdc + ", " + Short(trapped, 40) + " ; zone effacée, cellule d'origine redessinée, second piège en place");
        }
        return second.CellID.ToString(CultureInfo.InvariantCulture);
    }

    // ------------------------------------------------------------------------------------------------ lot X12

    private const int TransferStack = 5, TransferOffered = 3, TransferTaken = 1, TransferKamas = 10, BankDeposit = 2;

    /// <summary>
    /// x12. Glisser-déposer et boîte de quantité (lot X12), avec la fenêtre de jeu du donneur : deux comptes inventés sur la carte 164, le
    /// donneur avec 5 Greu-Vette et 1000 kamas écrits dans la base. Échange : <c>ER1</c>, <c>EA</c> du receveur, <c>ECK1</c> ; dans le volet
    /// d'échange du donneur, les poissons glissés du sac sur sa grille ouvrent la boîte (valeur 1, maximum 5) : 3 → <c>EMO+&lt;poisson&gt;|3</c>
    /// (<c>EMKO+</c> chez lui, <c>EmKO+…|3|598|</c> chez le receveur) ; la case glissée sur le sac : 1 → <c>EMO-&lt;poisson&gt;|1</c> ;
    /// « Proposer des kamas » : 10 → <c>EMG10</c> ; <c>EK</c> des deux, puis 2 poissons et 10 kamas chez le receveur. Banque ensuite
    /// (<c>.banque</c>, commande de StarLoco : <c>ECK5</c> et <c>EL</c>) : 2 poissons glissés du sac sur le coffre (<c>EMO+</c> → <c>EsKO+</c>,
    /// <c>OQ</c>), la case glissée sur le sac reprend tout (<c>EMO-</c> → <c>EsKO-</c>), 10 kamas déposés puis retirés par la boîte
    /// (<c>EMG10</c>, <c>EMG-10</c> → <c>EsKG</c>), « Fermer » → <c>EV</c>. Le coffre d'une maison ouvre la même fenêtre (<c>ECK5</c>,
    /// <c>Trunk.open</c>) : aucun personnage de test n'ayant de maison, la banque la représente. Hôtel de vente : l'interface
    /// <c>BigStoreSell</c> du client n'accepte aucun dépôt glissé, rien n'est joué.
    /// </summary>
    private static async Task<string> TransferDragAsync()
    {
        RequirePetsData();
        using (MySql.Data.MySqlClient.MySqlConnection db = OpenAccountsDatabase(out string schema))
        {
            var giver = new PetOwner { Label = "Don" }; var taker = new PetOwner { Label = "Rec" };
            var evidence = new List<string>();
            EventHandler<ExchangeRequestEventArgs> keep = (s, e) => e.Handled = true;
            PlayerExchange takerRequests = null;
            GameClientFullform screen = null;
            try
            {
                // Base : poissons du donneur à un identifiant hors de la plage de StarLoco ; le receveur n'a rien.
                int fishId = 620000000 + random.Next(1, 100000) * 10;
                giver.Character = Invent(db, schema, a.Id, PetsMap, PetsCell, 10000, giver.Created);
                giver.FishId = fishId;
                Sql(db, "INSERT INTO " + schema + ".`world.entity.objects` (id, template, quantity, position, stats, puit) VALUES (@id, @template, @quantity, -1, '', 0)",
                    "@id", fishId, "@template", FishTemplate, "@quantity", TransferStack);
                Sql(db, "UPDATE " + schema + ".players SET kamas = @kamas, objets = @objets WHERE id = @id",
                    "@kamas", PetsKamas, "@objets", fishId.ToString(CultureInfo.InvariantCulture) + "|", "@id", giver.Character.Id);
                taker.Character = Invent(db, schema, b.Id, PetsMap, PetsCell, 10000, taker.Created);
                Journal("[test] " + giver.Character.Name + " : " + TransferStack + " poissons (" + fishId + ") et " + PetsKamas + " kamas écrits dans la base ; " + taker.Character.Name + " sans objet");

                // Entrées en jeu l'une après l'autre ; le donneur a sa fenêtre de jeu.
                LiveSession don = giver.Session = new LiveSession(giver.Label, giver.Character.Login, giver.Character.Password);
                evidence.Add(await EnterExistingAsync(don, giver.Character.Id, giver.Character.Name, PetsMap, s =>
                    s.Account.Game.character.Player_Selection += () => OnUi(() =>
                    {
                        if (screen != null) return;
                        screen = new GameClientFullform(s.Account) { StartPosition = FormStartPosition.Manual, Location = new Point(60, 60), Size = new Size(1000, 700), ShowInTaskbar = false };
                        screen.Show();
                    })).ConfigureAwait(false));
                Check(await don.Until(() => giver.Fish?.Qua == TransferStack && don.Account.Game.character.Kamas == PetsKamas, 10).ConfigureAwait(false),
                    "Poissons ou kamas du donneur absents : poissons " + giver.Fish?.Qua + ", kamas " + don.Account.Game.character.Kamas);
                giver.Remember();
                LiveSession rec = taker.Session = new LiveSession(taker.Label, taker.Character.Login, taker.Character.Password);
                evidence.Add(await EnterExistingAsync(rec, taker.Character.Id, taker.Character.Name, PetsMap).ConfigureAwait(false));
                taker.Remember();
                Check(await don.Until(() => don.Map.GetActor(rec.Id) != null, 10).ConfigureAwait(false) && await rec.Until(() => rec.Map.GetActor(don.Id) != null, 10).ConfigureAwait(false),
                    "Les deux personnages ne se voient pas sur la carte " + PetsMap);
                Check(await WaitOnUi(() => screen != null && screen.IsHandleCreated && screen.Visible ? screen : null, 20).ConfigureAwait(false) != null, "La fenêtre de jeu du donneur ne s'est pas ouverte");

                // Échange ouvert (ER1, EA du receveur, ECK1 aux deux) : le volet d'échange du donneur s'affiche.
                takerRequests = rec.Account.Game.Interactions.Exchange; takerRequests.RequestReceived += keep;
                PlayerExchange giverExchange = don.Account.Game.Interactions.Exchange;
                int donMark = don.ReceivedCount, recMark = rec.ReceivedCount;
                var request = await giverExchange.RequestAsync(rec.Id).ConfigureAwait(false);
                Check(request.Sent, "Demande d'échange refusée par le bot : " + request.Message);
                await rec.Expect("ERK", "ERK chez le receveur", 10, recMark).ConfigureAwait(false);
                Check(await rec.Until(() => takerRequests.PendingRequest != null, 5).ConfigureAwait(false), "Le receveur n'a pas de demande en attente");
                var accept = await takerRequests.AcceptAsync().ConfigureAwait(false);
                Check(accept.Sent, "EA refusé par le bot : " + accept.Message);
                string eck = await don.Expect("ECK", "ECK1 chez le donneur", 10, donMark).ConfigureAwait(false);
                await rec.Expect("ECK", "ECK1 chez le receveur", 10, recMark).ConfigureAwait(false);
                Outil_Azur_complet.Bot.Panels.ExchangePanel exchangePanel = await WaitOnUi(() =>
                {
                    var panel = screen.Panels.Get<Outil_Azur_complet.Bot.Panels.ExchangePanel>();
                    return panel != null && ReferenceEquals(screen.Panels.Current, panel) && giverExchange.IsOpen ? panel : null;
                }, 10).ConfigureAwait(false);
                Check(exchangePanel != null, "Le volet d'échange du donneur ne s'est pas ouvert sur " + eck);
                var localGrid = await OnUi(() => (Outil_Azur_complet.Bot.Panels.ExchangeItemGrid)Field(exchangePanel, "localGrid")).ConfigureAwait(false);

                // Poissons glissés du sac sur la grille : boîte (maximum la pile), 3 → EMO+<poisson>|3.
                int sentMark = don.SentCount; donMark = don.ReceivedCount; recMark = rec.ReceivedCount;
                var bagItem = new Outil_Azur_complet.Bot.Controls.Banner.ShortcutPayload(ShortcutSlotKind.Item, fishId, TransferStack, -1);
                Check(await OnUi(() => localGrid.AcceptDrop(bagItem)).ConfigureAwait(false), "Le glisser des poissons sur la grille de l'échange est refusé");
                int maximum = await AnswerQuantityAsync(TransferOffered.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
                Check(maximum == TransferStack, "Maximum de la boîte de l'échange : " + maximum + " (attendu la pile, " + TransferStack + ")");
                string offer = "EMO+" + fishId + "|" + TransferOffered;
                Check(await don.Until(() => don.SentSince(sentMark).Contains(offer), 5).ConfigureAwait(false), "Le dépôt glissé n'a pas envoyé " + offer + " : " + string.Join(" ", don.SentSince(sentMark)));
                string local = await don.Expect("EMKO+" + fishId + "|" + TransferOffered, "EMKO+ chez le donneur", 10, donMark).ConfigureAwait(false);
                string distant = await rec.Expect("EmKO+" + fishId + "|" + TransferOffered + "|" + FishTemplate + "|", "EmKO+ chez le receveur", 10, recMark).ConfigureAwait(false);
                evidence.Add("glisser du sac → boîte (max " + maximum + ") « " + TransferOffered + " » → " + offer + " → " + local + " / " + Short(distant, 40));

                // La case proposée glissée sur le sac : 1 → EMO-<poisson>|1, l'offre passe à 2.
                sentMark = don.SentCount; donMark = don.ReceivedCount; recMark = rec.ReceivedCount;
                var offered = new Outil_Azur_complet.Bot.Panels.ExchangeItemDrag(localGrid, (uint)fishId, FishTemplate, TransferOffered);
                Check(await OnUi(() => exchangePanel.DropOnBag(offered)).ConfigureAwait(false), "Le glisser de la case proposée sur le sac est refusé");
                maximum = await AnswerQuantityAsync(TransferTaken.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
                Check(maximum == TransferOffered, "Maximum de la boîte du retrait : " + maximum + " (attendu " + TransferOffered + ")");
                string back = "EMO-" + fishId + "|" + TransferTaken;
                Check(await don.Until(() => don.SentSince(sentMark).Contains(back), 5).ConfigureAwait(false), "Le retrait glissé n'a pas envoyé " + back + " : " + string.Join(" ", don.SentSince(sentMark)));
                int kept = TransferOffered - TransferTaken;
                string after = await don.Expect("EMKO+" + fishId + "|" + kept, "EMKO+ de l'offre réduite chez le donneur", 10, donMark).ConfigureAwait(false);
                await rec.Expect("EmKO+" + fishId + "|" + kept + "|", "EmKO+ de l'offre réduite chez le receveur", 10, recMark).ConfigureAwait(false);
                evidence.Add("case glissée sur le sac → « " + TransferTaken + " » → " + back + " → " + after);

                // « Proposer des kamas » : boîte (valeur 0, maximum la bourse), 10 → EMG10.
                sentMark = don.SentCount; recMark = rec.ReceivedCount;
                await OnUi(() => ((Button)Field(exchangePanel, "exchangeKamasSend")).PerformClick()).ConfigureAwait(false);
                QuantityPopup kamasBox = await QuantityBoxAsync().ConfigureAwait(false);
                Check(kamasBox != null, "« Proposer des kamas » n'a pas ouvert la boîte");
                string kamasValue = await OnUi(() => kamasBox.InputText + "/" + kamasBox.Minimum + "/" + kamasBox.Maximum).ConfigureAwait(false);
                Check(kamasValue == "0/0/" + PetsKamas, "Boîte des kamas (valeur/min/max) : " + kamasValue + " (attendu 0/0/" + PetsKamas + ")");
                await AnswerQuantityAsync(TransferKamas.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
                Check(await don.Until(() => don.SentSince(sentMark).Contains("EMG" + TransferKamas), 5).ConfigureAwait(false), "La boîte des kamas n'a pas envoyé EMG" + TransferKamas);
                await rec.Expect("EmKG" + TransferKamas, "EmKG chez le receveur", 10, recMark).ConfigureAwait(false);
                evidence.Add("« Proposer des kamas » → boîte " + kamasValue + " → EMG" + TransferKamas);

                // EK des deux après le délai du bouton : poissons et kamas chez le receveur.
                Check(await don.Until(() => giverExchange.CanValidate, 6).ConfigureAwait(false), "Le donneur ne peut pas valider (délai)");
                donMark = don.ReceivedCount; recMark = rec.ReceivedCount;
                Check((await giverExchange.ValidateAsync().ConfigureAwait(false)).Sent, "EK du donneur refusé");
                await rec.Expect("EK1" + don.Id, "EK1<donneur> chez le receveur", 10, recMark).ConfigureAwait(false);
                Check(await rec.Until(() => takerRequests.CanValidate, 6).ConfigureAwait(false), "Le receveur ne peut pas valider (délai)");
                Check((await takerRequests.ValidateAsync().ConfigureAwait(false)).Sent, "EK du receveur refusé");
                string ev = await don.Expect("EV", "EV (échange conclu) chez le donneur", 10, donMark).ConfigureAwait(false);
                Check(await rec.Until(() => rec.Account.Game.character.Inventory.Objets.Any(x => x != null && x.ID == FishTemplate && x.Qua == kept) && rec.Account.Game.character.Kamas == TransferKamas, 10).ConfigureAwait(false)
                    && await don.Until(() => giver.Fish?.Qua == TransferStack - kept && don.Account.Game.character.Kamas == PetsKamas - TransferKamas, 10).ConfigureAwait(false),
                    "Après l'échange : donneur " + giver.Fish?.Qua + " poissons et " + don.Account.Game.character.Kamas + " kamas, receveur "
                    + rec.Account.Game.character.Inventory.Objets.Where(x => x != null && x.ID == FishTemplate).Sum(x => x.Qua) + " poissons et " + rec.Account.Game.character.Kamas + " kamas");
                Check(await WaitOnUi(() => screen.Panels.IsOpen(exchangePanel) ? null : exchangePanel, 10).ConfigureAwait(false) != null, "Le volet d'échange est resté ouvert après " + ev);
                giver.Remember(); taker.Remember();
                evidence.Add("EK des deux → " + ev + " ; donneur " + giver.Fish?.Qua + " poissons, " + don.Account.Game.character.Kamas + " kamas ; receveur " + kept + " poissons, " + rec.Account.Game.character.Kamas + " kamas");

                // Banque (.banque) : ECK5 et EL ; le volet du coffre s'affiche.
                donMark = don.ReceivedCount;
                await don.Send("BM*|.banque|").ConfigureAwait(false);
                string eck5 = await don.Expect(p => p.StartsWith("ECK5", StringComparison.Ordinal), "ECK5 (banque ouverte par .banque)", 10, donMark).ConfigureAwait(false);
                StorageExchange bank = don.Account.Game.Interactions.Storage;
                Outil_Azur_complet.Bot.Panels.StoragePanel chest = await WaitOnUi(() =>
                {
                    var panel = screen.Panels.Get<Outil_Azur_complet.Bot.Panels.StoragePanel>();
                    return panel != null && ReferenceEquals(screen.Panels.Current, panel) && bank.IsOpen && bank.ContentReceived ? panel : null;
                }, 10).ConfigureAwait(false);
                Check(chest != null, "Le volet du coffre ne s'est pas ouvert sur " + eck5);
                Check(bank.Items.Count == 0 && bank.Kamas == 0, "Banque du compte inventé non vide : " + bank.Items.Count + " objet(s), " + bank.Kamas + " kamas");
                var storageGrid = await OnUi(() => (Outil_Azur_complet.Bot.Panels.ExchangeItemGrid)Field(chest, "storageGrid")).ConfigureAwait(false);

                // Dépôt glissé : boîte (maximum la pile du sac), 2 → EMO+<poisson>|2 → EsKO+<objet de banque>|2|598|, OQ du sac.
                int bagLeft = giver.Fish.Qua;
                sentMark = don.SentCount; donMark = don.ReceivedCount;
                Check(await OnUi(() => storageGrid.AcceptDrop(new Outil_Azur_complet.Bot.Controls.Banner.ShortcutPayload(ShortcutSlotKind.Item, fishId, bagLeft, -1))).ConfigureAwait(false),
                    "Le glisser des poissons sur le coffre est refusé");
                maximum = await AnswerQuantityAsync(BankDeposit.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
                Check(maximum == bagLeft, "Maximum de la boîte du dépôt : " + maximum + " (attendu " + bagLeft + ")");
                string deposit = "EMO+" + fishId + "|" + BankDeposit;
                Check(await don.Until(() => don.SentSince(sentMark).Contains(deposit), 5).ConfigureAwait(false), "Le dépôt glissé n'a pas envoyé " + deposit);
                string stored = await don.Expect(p => p.StartsWith("EsKO+", StringComparison.Ordinal) && p.Contains("|" + BankDeposit + "|" + FishTemplate + "|"), "EsKO+ du dépôt", 10, donMark).ConfigureAwait(false);
                Check(await don.Until(() => bank.Items.Count == 1 && bank.Items[0].Quantity == BankDeposit && giver.Fish?.Qua == bagLeft - BankDeposit, 10).ConfigureAwait(false),
                    "Après le dépôt : banque " + string.Join(",", bank.Items.Select(x => x.Id + "×" + x.Quantity)) + ", sac " + giver.Fish?.Qua);
                ExchangeItem banked = bank.Items[0];
                giver.Seen.Add(banked.Id);
                evidence.Add(eck5 + " ; glisser sur le coffre → boîte (max " + maximum + ") « " + BankDeposit + " » → " + deposit + " → " + Short(stored, 40));

                // Retrait glissé : la case du coffre sur le sac, boîte bornée aux pods libres, tout → EMO-<objet>|2 → EsKO-, poissons revenus.
                sentMark = don.SentCount; donMark = don.ReceivedCount;
                var bankCell = new Outil_Azur_complet.Bot.Panels.ExchangeItemDrag(storageGrid, banked.Id, banked.TemplateId, banked.Quantity);
                Check(await OnUi(() => chest.DropOnBag(bankCell)).ConfigureAwait(false), "Le glisser de la case du coffre sur le sac est refusé");
                maximum = await AnswerQuantityAsync(BankDeposit.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
                Check(maximum == BankDeposit, "Maximum de la boîte du retrait : " + maximum + " (attendu " + BankDeposit + ", pods libres " + (giver.Bag.Pods_Max - giver.Bag.Actual_pods) + ")");
                string withdraw = "EMO-" + banked.Id + "|" + BankDeposit;
                Check(await don.Until(() => don.SentSince(sentMark).Contains(withdraw), 5).ConfigureAwait(false), "Le retrait glissé n'a pas envoyé " + withdraw);
                string emptied = await don.Expect("EsKO-" + banked.Id, "EsKO- du retrait", 10, donMark).ConfigureAwait(false);
                Check(await don.Until(() => bank.Items.Count == 0 && giver.Fish?.Qua == bagLeft, 10).ConfigureAwait(false), "Après le retrait : banque " + bank.Items.Count + " objet(s), sac " + giver.Fish?.Qua);
                // Player.removeFromBank : la pile revenue s'ajoute à celle du sac et l'objet de banque quitte le monde (World.removeGameObject) ;
                // il n'a plus rien à nettoyer (gardé jusque-là au cas où le scénario échouerait objet en banque).
                giver.Seen.Remove(banked.Id);
                evidence.Add("case du coffre glissée sur le sac → « " + BankDeposit + " » → " + withdraw + " → " + emptied);

                // Kamas : dépôt par la boîte (valeur et maximum la bourse), 10 → EMG10 ; retrait (valeur et maximum le coffre) → EMG-10.
                long purse = don.Account.Game.character.Kamas;
                sentMark = don.SentCount; donMark = don.ReceivedCount;
                await OnUi(() => ((Button)Field(chest, "storageKamasDeposit")).PerformClick()).ConfigureAwait(false);
                QuantityPopup depositBox = await QuantityBoxAsync().ConfigureAwait(false);
                Check(depositBox != null, "« Déposer des kamas » n'a pas ouvert la boîte");
                string depositValue = await OnUi(() => depositBox.InputText + "/" + depositBox.Maximum).ConfigureAwait(false);
                Check(depositValue == purse + "/" + purse, "Boîte du dépôt de kamas (valeur/max) : " + depositValue + " (attendu " + purse + "/" + purse + ")");
                await AnswerQuantityAsync(TransferKamas.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
                Check(await don.Until(() => don.SentSince(sentMark).Contains("EMG" + TransferKamas), 5).ConfigureAwait(false), "Le dépôt de kamas n'a pas envoyé EMG" + TransferKamas);
                await don.Expect("EsKG" + TransferKamas, "EsKG du dépôt de kamas", 10, donMark).ConfigureAwait(false);
                Check(await don.Until(() => bank.Kamas == TransferKamas, 5).ConfigureAwait(false), "Kamas du coffre après le dépôt : " + bank.Kamas);
                sentMark = don.SentCount; donMark = don.ReceivedCount;
                await OnUi(() => ((Button)Field(chest, "storageKamasWithdraw")).PerformClick()).ConfigureAwait(false);
                QuantityPopup withdrawBox = await QuantityBoxAsync().ConfigureAwait(false);
                Check(withdrawBox != null, "« Retirer des kamas » n'a pas ouvert la boîte");
                string withdrawValue = await OnUi(() => withdrawBox.InputText + "/" + withdrawBox.Maximum).ConfigureAwait(false);
                Check(withdrawValue == TransferKamas + "/" + TransferKamas, "Boîte du retrait de kamas (valeur/max) : " + withdrawValue);
                await OnUi(() => withdrawBox.Confirm()).ConfigureAwait(false);
                Check(await don.Until(() => don.SentSince(sentMark).Contains("EMG-" + TransferKamas), 5).ConfigureAwait(false), "Le retrait de kamas n'a pas envoyé EMG-" + TransferKamas);
                await don.Expect("EsKG0", "EsKG0 du retrait de kamas", 10, donMark).ConfigureAwait(false);
                Check(await don.Until(() => bank.Kamas == 0 && don.Account.Game.character.Kamas == purse, 5).ConfigureAwait(false),
                    "Kamas après le retrait : coffre " + bank.Kamas + ", bourse " + don.Account.Game.character.Kamas + " (attendu " + purse + ")");
                evidence.Add("kamas : boîte " + depositValue + " → EMG" + TransferKamas + " → EsKG" + TransferKamas + ", boîte " + withdrawValue + " → EMG-" + TransferKamas + " → EsKG0");

                // « Fermer » → EV ; le volet se ferme.
                donMark = don.ReceivedCount;
                await OnUi(() => ((Button)Field(chest, "storageLeave")).PerformClick()).ConfigureAwait(false);
                await don.Expect("EV", "EV de la banque", 10, donMark).ConfigureAwait(false);
                Check(await WaitOnUi(() => !bank.IsOpen && !screen.Panels.IsOpen(chest) ? chest : null, 10).ConfigureAwait(false) != null, "La banque ou son volet sont restés ouverts après EV");
                giver.Remember();
                evidence.Add("« Fermer » → EV, volet fermé ; hôtel de vente : aucun dépôt glissé chez le client (BigStoreSell), non joué");
                return string.Join(" ; ", evidence);
            }
            finally
            {
                if (takerRequests != null) takerRequests.RequestReceived -= keep;
                await OnUi(() => { foreach (QuantityPopup open in QuantityPopup.OpenPopups) open.Cancel(); }).ConfigureAwait(false);
                // Nettoyage comme x4 à x6 : sac détruit en jeu, lignes de la base, comptes ; la fenêtre du donneur se ferme après son sac.
                await ClosePetOwnerAsync(db, schema, giver, () => CloseScreenAsync(screen)).ConfigureAwait(false);
                await ClosePetOwnerAsync(db, schema, taker).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Seule boîte de quantité ouverte dans le bot, attendue (fil d'interface), ou <c>null</c>.</summary>
    private static Task<QuantityPopup> QuantityBoxAsync(int seconds = 10) =>
        WaitOnUi(() => QuantityPopup.OpenPopups.Count == 1 ? QuantityPopup.OpenPopups[0] : null, seconds);

    /// <summary>Saisit <paramref name="text"/> dans la boîte de quantité ouverte et la valide ; rend son maximum.</summary>
    private static async Task<int> AnswerQuantityAsync(string text)
    {
        QuantityPopup box = await QuantityBoxAsync().ConfigureAwait(false);
        Check(box != null, "La boîte de quantité ne s'est pas ouverte");
        return await OnUi(() => { int maximum = box.Maximum; box.InputText = text; box.Confirm(); return maximum; }).ConfigureAwait(false);
    }
}

// Scénario x8 (lot X8) : visée des sorts du client (portée en cercle ou en croix, zones sous le pointeur) et chemin de combat survolé.
internal static partial class Live
{
    /// <summary>
    /// Sorts écrits dans la base pour l'Iop inventé (<c>id;niveau;position</c> de <c>players.spells</c>) : Pression (portée 1 à 2 en
    /// cercle), Épée de Iop (lancer en ligne de 1 à 3, zone en croix « Xd ») et Flèche Enflammée (lancer en ligne de 1 à 3, zone en
    /// ligne « Le », 4 PA) ; données de niveau 1 de <c>spells.xml</c> du client.
    /// </summary>
    private const short CircleSpell = 141, CrossSpell = 160, LineSpell = 165;
    private const string AimSpells = "141;1;b,160;1;c,165;1;d";
    private static readonly MethodInfo MapMouseMove = typeof(UserMapControl).GetMethod("OnMouseMove", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>La souris passe sur la cellule de la vue de la fenêtre (fil d'interface), comme un survol réel.</summary>
    private static Task HoverAsync(GameClientFullform form, short cellId) => OnUi(() =>
    {
        UserMapControl map = UserMapOf(form);
        Point centre = map.Cells[cellId].Centre;
        MapMouseMove.Invoke(map, new object[] { new MouseEventArgs(MouseButtons.None, 0, centre.X, centre.Y, 0) });
    });

    private static short[] Sorted(IEnumerable<short> cells) => cells.OrderBy(id => id).ToArray();
    private static string CellList(IEnumerable<short> cells) => "[" + string.Join(",", cells) + "]";

    /// <summary>
    /// Un Iop inventé (Pression, Épée de Iop, Flèche Enflammée écrits dans la base) entre sur la carte de A avec sa fenêtre de jeu ; A le
    /// défie. Pendant le tour de A, survoler une cellule dans la fenêtre de l'Iop ne montre aucun chemin. À son tour : Pression dessine sa
    /// portée en cercle (distance 1 à 2), l'Épée de Iop en croix (cellules alignées de 1 à 3, bonus de PO compris) avec son icône à côté
    /// de la flèche ; sur une cible valable le pointeur pose la croix « Xd » ou, pour la Flèche Enflammée, la ligne « Le » prolongée du
    /// lanceur vers la cible, chaque forme tirée des coordonnées X et Y ; hors des cibles, croix d'interdiction et aucun pointeur, et un
    /// clic n'envoie rien. Un clic sur la cible lance la Flèche Enflammée (<c>GA300165;cellule</c> du client → <c>GA;300</c> de
    /// StarLoco). Sans sort, le survol d'une cellule à deux pas dessine le chemin de combat (formes de sélection) et un clic envoie ce
    /// chemin (<c>GA001…</c>), accepté par <c>GA;1</c>. L'Iop abandonne ; compte et personnage supprimés de la base.
    /// </summary>
    private static async Task<string> SpellAimAsync()
    {
        RequireMaps();
        if (!LangData.IsLoaded("spells")) throw new SkipScenario("textes du client « spells » absents : le bot ne connaît ni la portée ni les zones des sorts");
        using (MySql.Data.MySqlClient.MySqlConnection db = OpenAccountsDatabase(out string schema))
        {
            var created = new List<InventedCharacter>();
            var evidence = new List<string>();
            LiveSession s = null;
            GameClientFullform screen = null;
            try
            {
                Cell place = FreeCellNear(a, 2, 6);
                Check(place != null, "Aucune cellule libre près de A pour l'Iop");
                InventedCharacter iop = Invent(db, schema, a.Id, a.MapId, place.CellID, 10000, created);
                Sql(db, "UPDATE " + schema + ".players SET spells = @spells WHERE id = @id AND account = @account",
                    "@spells", AimSpells, "@id", iop.Id, "@account", iop.Guid);
                s = new LiveSession("I", iop.Login, iop.Password);
                evidence.Add(await EnterExistingAsync(s, iop.Id, iop.Name, a.MapId, started =>
                    started.Account.Game.character.Player_Selection += () => OnUi(() => { if (screen == null) { screen = OpenScreen(started); screen.Show(); } })).ConfigureAwait(false));
                LiveSession si = s;
                Check(await si.Until(() => new[] { CircleSpell, CrossSpell, LineSpell }.All(id => si.Account.Game.character.Spells.Values.Any(x => x.ID == id)), 10).ConfigureAwait(false),
                    "Le bot ne voit pas les trois sorts de l'Iop : " + string.Join(",", si.Account.Game.character.Spells.Values.Select(x => x.ID)));
                Check(await WaitOnUi(() => screen != null && screen.IsHandleCreated && screen.Visible && UserMapOf(screen) != null ? screen : null, 20).ConfigureAwait(false) != null,
                    "La fenêtre de jeu de l'Iop ne s'est pas ouverte");
                Check(await a.Until(() => a.Map.GetActor(si.Id) != null, 10).ConfigureAwait(false) && await si.Until(() => si.Map.GetActor(a.Id) != null, 10).ConfigureAwait(false),
                    "A et l'Iop ne se voient pas sur la carte " + a.MapId);

                int markA = a.ReceivedCount, markI = si.ReceivedCount;
                var challenge = await a.Account.Game.Interactions.MapActions.ChallengeAsync(si.Id).ConfigureAwait(false);
                Check(challenge.Sent, "Défi refusé par le bot : " + challenge.Message);
                await si.Expect(p => p.StartsWith("GA;900;" + a.Id + ";", StringComparison.Ordinal), "GA;900 (défi reçu par l'Iop)", 10, markI).ConfigureAwait(false);
                Check(await si.Until(() => si.Account.Game.Interactions.MapActions.PendingChallenge?.ChallengerId == a.Id, 5).ConfigureAwait(false), "L'Iop n'a pas de défi en attente");
                var accept = await si.Account.Game.Interactions.MapActions.AcceptChallengeAsync(a.Id).ConfigureAwait(false);
                Check(accept.Sent, "Acceptation du défi refusée par le bot : " + accept.Message);
                await a.Expect("GJK", "GJK (entrée en combat, A)", 15, markA).ConfigureAwait(false);
                await si.Expect("GJK", "GJK (entrée en combat, Iop)", 15, markI).ConfigureAwait(false);
                Fights fa = a.Account.Game.Fight, fi = si.Account.Game.Fight;
                Check(await a.Until(() => fa.IsPlacement && fi.IsPlacement && Self(a) != null && Self(si) != null && Enemies(si).Any(e => e.Id == a.Id), 10).ConfigureAwait(false),
                    "Placement incomplet : " + string.Join(", ", fi.Fighters.Values.Select(f => f.Name + " équipe " + f.Team)));
                await ReadyAsync(si, markI).ConfigureAwait(false);
                await ReadyAsync(a, markA).ConfigureAwait(false);
                await a.Expect("GS", "GS (début du combat)", 30, markA).ConfigureAwait(false);

                bool otherTurnChecked = false, played = false;
                DateTime until = DateTime.UtcNow.AddSeconds(90);
                while ((!played || !otherTurnChecked) && DateTime.UtcNow < until && fi.IsInFight)
                {
                    int actor = await WaitForTurnOf(new[] { a, si }, 40).ConfigureAwait(false);
                    if (actor == a.Id)
                    {
                        if (!otherTurnChecked) { evidence.Add(await OtherTurnHoverAsync(si, screen).ConfigureAwait(false)); otherTurnChecked = true; }
                        int mark = a.ReceivedCount;
                        var passed = await fa.PassTurnAsync().ConfigureAwait(false);
                        Check(passed.Sent, "Fin de tour de A refusée par le bot : " + passed.Message);
                        await a.Expect("GTF" + a.Id, "GTF (fin du tour de A)", 10, mark).ConfigureAwait(false);
                        await a.Until(() => !fa.IsMyTurn, 5).ConfigureAwait(false);
                    }
                    else if (actor == si.Id)
                    {
                        if (!played) { evidence.AddRange(await AimTurnAsync(si, screen).ConfigureAwait(false)); played = true; }
                        int mark = si.ReceivedCount;
                        var passed = await fi.PassTurnAsync().ConfigureAwait(false);
                        Check(passed.Sent, "Fin de tour de l'Iop refusée par le bot : " + passed.Message);
                        await si.Expect("GTF" + si.Id, "GTF (fin du tour de l'Iop)", 10, mark).ConfigureAwait(false);
                        await si.Until(() => !fi.IsMyTurn, 5).ConfigureAwait(false);
                    }
                    else break;
                }
                Check(played && otherTurnChecked, "Tours incomplets : tour de l'Iop " + (played ? "joué" : "absent") + ", survol pendant le tour de A " + (otherTurnChecked ? "vérifié" : "absent"));

                int endA = a.ReceivedCount, endI = si.ReceivedCount;
                var giveUp = await fi.GiveUpAsync().ConfigureAwait(false);
                Check(giveUp.Sent, "Abandon refusé par le bot : " + giveUp.Message);
                await a.Expect("GE", "GE (fin du combat, A)", 20, endA).ConfigureAwait(false);
                await si.Expect(p => p.StartsWith("GE", StringComparison.Ordinal) || p.StartsWith("GV", StringComparison.Ordinal), "GE ou GV (fin du combat, Iop)", 20, endI).ConfigureAwait(false);
                await WaitBackOnMap(a).ConfigureAwait(false); await WaitBackOnMap(si).ConfigureAwait(false);
                Check(await WaitOnUi(() => !UserMapOf(screen).IsAiming && UserMapOf(screen).SpellRange.Count == 0 && !UserMapOf(screen).PathPreviewUsesSelection ? "rendue" : null, 10).ConfigureAwait(false) != null,
                    "La visée ou le chemin de combat survit à la fin du combat dans la fenêtre de l'Iop");
                evidence.Add("abandon de l'Iop → GE : visée et chemin de combat effacés");
                return string.Join(" ; ", evidence);
            }
            finally
            {
                if (screen != null) await OnUi(() => { if (!screen.IsDisposed) { screen.Close(); screen.Dispose(); } }).ConfigureAwait(false);
                await ForgetAsync(db, schema, s == null ? new LiveSession[0] : new[] { s }, created).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Cellules de l'aperçu du chemin dessiné par la vue (fil d'interface).</summary>
    private static short[] PreviewOf(GameClientFullform form)
    {
        UserMapControl map = UserMapOf(form);
        var shown = typeof(UserMapControl).GetField("pathPreview", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(map) as IEnumerable<short>;
        return shown == null ? new short[0] : Sorted(shown);
    }

    /// <summary>Tour de A : le survol d'une cellule accessible dans la fenêtre de l'Iop ne dessine aucun chemin (pas son tour).</summary>
    private static async Task<string> OtherTurnHoverAsync(LiveSession si, GameClientFullform screen)
    {
        Fights fi = si.Account.Game.Fight;
        Cell own = si.Map.GetCellFromId((short)Self(si).CellId);
        var occupied = new HashSet<int>(fi.Fighters.Values.Where(f => !f.IsDead).Select(f => (int)f.CellId));
        Cell near = si.Map.MapCells.FirstOrDefault(c => c != null && c.IsWalkable() && !occupied.Contains(c.CellID) && own.GetDistanceBetweenCells(c) == 2);
        Check(near != null, "Aucune cellule libre à deux pas de l'Iop");
        Check(!fi.IsMyTurn && fi.PreviewMovePath(near.CellID) == null, "Le bot calcule un chemin de combat hors du tour de l'Iop");
        await HoverAsync(screen, own.CellID).ConfigureAwait(false);
        await HoverAsync(screen, near.CellID).ConfigureAwait(false);
        await Task.Delay(700).ConfigureAwait(false);
        bool empty = await OnUi(() => UserMapOf(screen).HoveredCell?.id == near.CellID && PreviewOf(screen).Length == 0).ConfigureAwait(false);
        Check(empty, "Un chemin est dessiné dans la fenêtre de l'Iop pendant le tour de A : " + CellList(await OnUi(() => PreviewOf(screen)).ConfigureAwait(false)));
        return "tour de A : survol de " + near.CellID + " dans la fenêtre de l'Iop sans chemin dessiné";
    }

    /// <summary>
    /// Tour de l'Iop : portée en cercle puis en croix, pointeur en croix et en ligne, croix d'interdiction, Flèche Enflammée lancée par un
    /// clic sur la cible, puis chemin de combat survolé et suivi par un clic.
    /// </summary>
    private static async Task<List<string>> AimTurnAsync(LiveSession si, GameClientFullform screen)
    {
        var evidence = new List<string>();
        Fights fi = si.Account.Game.Fight;
        Cell[] cells = si.Map.MapCells;
        Cell own = si.Map.GetCellFromId((short)Self(si).CellId);
        Check(own != null && fi.ActionPoints >= 4, "Tour de l'Iop sans cellule ni PA : PA " + fi.ActionPoints);
        int bonus = fi.RangeBonus;
        var occupied = new HashSet<int>(fi.Fighters.Values.Where(f => !f.IsDead).Select(f => (int)f.CellId));
        Func<Cell, Cell, int> distance = (x, y) => Math.Abs(x.X - y.X) + Math.Abs(x.Y - y.Y);
        Func<Func<Cell, bool>, short[]> where = keep => Sorted(cells.Where(c => c != null && keep(c)).Select(c => c.CellID));

        // Pression : portée « C » de 1 à 2 (non modifiable) ; Épée de Iop : « X » de 1 à 3 + bonus de PO (modifiable), alignée.
        int crossMax = Math.Max(1, 3 + bonus);
        var ranges = new[]
        {
            new { Spell = CircleSpell, Wanted = where(c => distance(own, c) >= 1 && distance(own, c) <= 2), Shape = "cercle 1-2" },
            new { Spell = CrossSpell, Wanted = where(c => (c.X == own.X || c.Y == own.Y) && distance(own, c) >= 1 && distance(own, c) <= crossMax), Shape = "croix 1-" + crossMax },
        };
        foreach (var range in ranges)
        {
            await OnUi(() => screen.Map.SelectSpell(range.Spell)).ConfigureAwait(false);
            short[] drawn = await WaitOnUi(() =>
            {
                UserMapControl map = UserMapOf(screen);
                return map.IsAiming && map.AimIcon == "Spells/" + range.Spell ? Sorted(map.SpellRange) : null;
            }, 10).ConfigureAwait(false);
            Check(drawn != null && drawn.SequenceEqual(range.Wanted), "Portée du sort " + range.Spell + " (" + range.Shape + ") dessinée "
                + (drawn == null ? "nulle part" : CellList(drawn)) + " au lieu de " + CellList(range.Wanted));
            evidence.Add("sort " + range.Spell + " : portée en " + range.Shape + " (" + drawn.Length + " cellules), icône Spells/" + range.Spell + " à côté de la flèche");
        }

        // Pointeur de l'Épée de Iop : croix « Xd » (taille 3) autour d'une cible alignée, deux fois (effets normaux et critiques).
        Cell cross = cells.Where(c => c != null && (c.X == own.X || c.Y == own.Y) && distance(own, c) >= 2 && distance(own, c) <= crossMax
                && !occupied.Contains(c.CellID) && fi.GetSpellUnavailableReason(CrossSpell, c.CellID) == null)
            .OrderBy(c => distance(own, c)).ThenBy(c => c.CellID).FirstOrDefault();
        Check(cross != null, "Aucune cible valable alignée à deux ou trois pas pour l'Épée de Iop");
        short[] crossCells = where(c => (c.X == cross.X || c.Y == cross.Y) && distance(cross, c) <= 3);
        await HoverAsync(screen, cross.CellID).ConfigureAwait(false);
        IReadOnlyList<IReadOnlyCollection<short>> pointer = await WaitOnUi(() => UserMapOf(screen).PointerCells.Count > 0 ? UserMapOf(screen).PointerCells : null, 10).ConfigureAwait(false);
        bool forbidden = await OnUi(() => UserMapOf(screen).CursorForbidden).ConfigureAwait(false);
        Check(pointer != null && pointer.Count == 2 && pointer.All(shape => Sorted(shape).SequenceEqual(crossCells)) && !forbidden,
            "Pointeur de l'Épée de Iop sur " + cross.CellID + " : " + (pointer == null ? "aucun" : string.Join(" + ", pointer.Select(shape => CellList(Sorted(shape)))))
            + " au lieu de " + CellList(crossCells) + " (deux fois), interdiction " + forbidden);
        await Capture("23-reel-x8-croix", screen).ConfigureAwait(false);
        evidence.Add("Épée de Iop sur " + cross.CellID + " : croix Xd de " + crossCells.Length + " cellules sous le pointeur (effets normaux et critiques)");

        // Hors des cibles (non alignée) : croix d'interdiction, aucun pointeur, un clic n'envoie rien.
        Cell refused = cells.Where(c => c != null && c.X != own.X && c.Y != own.Y && distance(own, c) == 2 && !occupied.Contains(c.CellID))
            .OrderBy(c => c.CellID).FirstOrDefault();
        Check(refused != null && fi.GetSpellUnavailableReason(CrossSpell, refused.CellID) != null, "Aucune cellule non alignée refusée par le bot près de l'Iop");
        await HoverAsync(screen, refused.CellID).ConfigureAwait(false);
        Check(await WaitOnUi(() => UserMapOf(screen).CursorForbidden && UserMapOf(screen).PointerCells.Count == 0 ? "interdit" : null, 10).ConfigureAwait(false) != null,
            "Cellule non alignée " + refused.CellID + " sans croix d'interdiction ou avec un pointeur");
        int sentMark = si.SentCount, paBefore = fi.ActionPoints;
        await (await OnUi(() => screen.Map.HandleCellActionAsync(refused.CellID)).ConfigureAwait(false)).ConfigureAwait(false);
        await Task.Delay(500).ConfigureAwait(false);
        bool stillAiming = await OnUi(() => screen.Map.SelectedSpellId == CrossSpell).ConfigureAwait(false);
        Check(!si.SentSince(sentMark).Any(p => p.StartsWith("GA300", StringComparison.Ordinal)) && fi.ActionPoints == paBefore && stillAiming,
            "Un clic hors portée a envoyé le sort ou désarmé la visée : " + string.Join(" | ", si.SentSince(sentMark)));
        evidence.Add("cellule non alignée " + refused.CellID + " : croix d'interdiction, pas de pointeur, clic sans paquet");

        // Flèche Enflammée : ligne « Le » prolongée du lanceur vers la cible, puis lancée par un clic sur la cible.
        await OnUi(() => screen.Map.SelectSpell(LineSpell)).ConfigureAwait(false);
        Cell line = cells.Where(c => c != null && (c.X == own.X || c.Y == own.Y) && distance(own, c) >= 1 && distance(own, c) <= crossMax
                && !occupied.Contains(c.CellID) && fi.GetSpellUnavailableReason(LineSpell, c.CellID) == null)
            .OrderByDescending(c => distance(own, c) == 2).ThenBy(c => c.CellID).FirstOrDefault();
        Check(line != null, "Aucune cible valable pour la Flèche Enflammée");
        int stepX = Math.Sign(line.X - own.X), stepY = Math.Sign(line.Y - own.Y);
        short[] lineCells = where(c => c.CellID == line.CellID
            || (stepX != 0 && c.Y == line.Y && (c.X - line.X) * stepX >= 1 && (c.X - line.X) * stepX <= 4)
            || (stepY != 0 && c.X == line.X && (c.Y - line.Y) * stepY >= 1 && (c.Y - line.Y) * stepY <= 4));
        await HoverAsync(screen, own.CellID).ConfigureAwait(false);
        await HoverAsync(screen, line.CellID).ConfigureAwait(false);
        pointer = await WaitOnUi(() => UserMapOf(screen).AimIcon == "Spells/" + LineSpell && UserMapOf(screen).PointerCells.Count > 0 ? UserMapOf(screen).PointerCells : null, 10).ConfigureAwait(false);
        Check(pointer != null && pointer.Count == 4 && pointer.All(shape => Sorted(shape).SequenceEqual(lineCells)),
            "Pointeur de la Flèche Enflammée sur " + line.CellID + " : " + (pointer == null ? "aucun" : string.Join(" + ", pointer.Select(shape => CellList(Sorted(shape)))))
            + " au lieu de " + CellList(lineCells) + " (quatre fois)");
        await Capture("23-reel-x8-ligne", screen).ConfigureAwait(false);
        sentMark = si.SentCount; int markI = si.ReceivedCount; paBefore = fi.ActionPoints;
        await (await OnUi(() => screen.Map.HandleCellActionAsync(line.CellID)).ConfigureAwait(false)).ConfigureAwait(false);
        string cast = "GA300" + LineSpell + ";" + line.CellID;
        Check(await si.Until(() => si.SentSince(sentMark).Contains(cast), 5).ConfigureAwait(false), "Le clic sur la cible n'a pas envoyé " + cast + " : " + string.Join(" | ", si.SentSince(sentMark)));
        string launched = await si.Expect(p => p.StartsWith("GA;300;" + si.Id + ";", StringComparison.Ordinal) || p.StartsWith("GA;302;" + si.Id + ";", StringComparison.Ordinal)
            || p.StartsWith("GA;0", StringComparison.Ordinal) || p.StartsWith("GAE", StringComparison.Ordinal), "GA;300 de la Flèche Enflammée", 10, markI).ConfigureAwait(false);
        Check(launched.StartsWith("GA;300;", StringComparison.Ordinal), "StarLoco ne lance pas la Flèche Enflammée en " + line.CellID + " : " + launched);
        Check(await si.Until(() => !fi.IsActionPending && fi.ActionPoints < paBefore, 10).ConfigureAwait(false), "L'Iop attend encore la fin de la Flèche Enflammée");
        Check(await WaitOnUi(() => !UserMapOf(screen).IsAiming && UserMapOf(screen).PointerCells.Count == 0 ? "terminée" : null, 10).ConfigureAwait(false) != null,
            "La visée reste armée après le lancer");
        evidence.Add("Flèche Enflammée sur " + line.CellID + " : ligne Le de " + lineCells.Length + " cellules, clic → " + cast + " → " + Short(launched, 40));

        // Sans sort : chemin de combat vers une cellule à deux pas, dessiné avec les formes de sélection, puis suivi par un clic.
        Check(await si.Until(() => fi.MovementPoints >= 2, 5).ConfigureAwait(false), "L'Iop n'a plus deux PM : " + fi.MovementPoints);
        occupied = new HashSet<int>(fi.Fighters.Values.Where(f => !f.IsDead).Select(f => (int)f.CellId));
        Func<Cell, bool> nearEnemy = c => Enemies(si).Any(e => si.Map.GetCellFromId((short)e.CellId)?.GetDistanceBetweenCells(c) <= 1);
        own = si.Map.GetCellFromId((short)Self(si).CellId);
        Check(!nearEnemy(own), "L'Iop finit son lancer au contact de A : un pas serait taclé");
        List<Cell> path = null;
        Cell step = null;
        foreach (Cell c in cells.Where(c => c != null && c.IsWalkable() && !occupied.Contains(c.CellID) && own.GetDistanceBetweenCells(c) == 2 && !nearEnemy(c)).OrderBy(c => c.CellID))
        {
            List<Cell> found = fi.PreviewMovePath(c.CellID);
            if (found != null && found.Count == 3 && !found.Any(nearEnemy)) { path = found; step = c; break; }
        }
        Check(step != null, "Aucun pas de deux cellules loin de A pour l'Iop");
        short[] expectedPath = path.Skip(1).Select(c => c.CellID).ToArray();
        await HoverAsync(screen, own.CellID).ConfigureAwait(false);
        await HoverAsync(screen, step.CellID).ConfigureAwait(false);
        short[] previewed = await WaitOnUi(() => UserMapOf(screen).PathPreviewUsesSelection && PreviewOf(screen).Length > 0 ? PreviewOf(screen) : null, 10).ConfigureAwait(false);
        Check(previewed != null && previewed.SequenceEqual(Sorted(expectedPath)), "Chemin de combat survolé : " + (previewed == null ? "aucun" : CellList(previewed))
            + " au lieu de " + CellList(expectedPath));
        await Capture("23-reel-x8-chemin", screen).ConfigureAwait(false);
        sentMark = si.SentCount; markI = si.ReceivedCount; int pmBefore = fi.MovementPoints;
        await (await OnUi(() => screen.Map.HandleCellActionAsync(step.CellID)).ConfigureAwait(false)).ConfigureAwait(false);
        string road = Tool_BotProtocol.Game.Maps.Mouvements.PathfinderUtils.GetCleanRoad(path), move = "GA001" + road;
        Check(await si.Until(() => si.SentSince(sentMark).Contains(move), 5).ConfigureAwait(false), "Le clic n'a pas envoyé le chemin survolé " + move + " : "
            + string.Join(" | ", si.SentSince(sentMark)));
        // StarLoco renvoie GA<action>;1;<acteur>;<cellule de départ et direction><chemin du client>, ou GA;0 s'il refuse.
        string moved = await si.Expect(p => (p.StartsWith("GA", StringComparison.Ordinal) && p.Contains(";1;" + si.Id + ";")) || p.StartsWith("GA;0", StringComparison.Ordinal),
            "GA;1 (déplacement de l'Iop)", 10, markI).ConfigureAwait(false);
        Check(!moved.StartsWith("GA;0", StringComparison.Ordinal) && moved.EndsWith(road, StringComparison.Ordinal), "StarLoco refuse ou modifie le chemin survolé " + road + " : " + moved);
        Check(await si.Until(() => Self(si)?.CellId == step.CellID && fi.MovementPoints == pmBefore - 2 && !fi.IsActionPending, 10).ConfigureAwait(false),
            "L'Iop n'est pas arrivé en " + step.CellID + " avec deux PM de moins (cellule " + Self(si)?.CellId + ", PM " + fi.MovementPoints + ")");
        evidence.Add("chemin survolé " + CellList(expectedPath) + " en formes de sélection, clic → " + move + " → " + Short(moved, 40));
        return evidence;
    }
}

// Scénario x13 (lot X13) : objet vivant écrit dans la base, nourri, changé d'apparence et dissocié par le volet de l'inventaire ;
// pain donné à l'autre joueur par la visée d'objet de la carte.
internal static partial class Live
{
    /// <summary>
    /// Chapeau de l'Aventurier (2474, type 16, niveau 1) porté avec l'objet vivant 9234 (Chapeau Obvijevan, recréé par StarLoco à la
    /// dissociation d'un chapeau sans ligne <c>world.entity.obvijevans</c>) ; Grand Bandeau de Vitalité (949, type 16, niveau 3 :
    /// <c>obvijevanNourir</c> ajoute le tiers du niveau, un point) ; Pain d'Amakna (468, type 33, ciblable, action 3 sur l'effet 110).
    /// La cible entre en jeu à la moitié de sa vie (<c>pdvper</c>), sinon le pain ne rend rien et reste dans le sac.
    /// </summary>
    private const int LivingHatTemplate = 2474, LivingObviTemplate = 9234, LivingFoodTemplate = 949, LivingBreadTemplate = 468, LivingHatSlot = 6, LivingTargetLife = 50;

    /// <summary>
    /// Effets du chapeau écrits dans la base comme <c>GameObject.parseStringToStats</c> les lit (valeur au deuxième champ, hexadécimal) :
    /// +1 en force, 970 = 0x2412 (9234), humeur 1, apparence 1, type 10 (StarLoco le renvoie en décimal, « 3cd#0#0#10 », que le client
    /// lit 0x10 = 16) et 9 points d'expérience, un de moins que le niveau 2 (<c>LEVEL_STEP</c>).
    /// </summary>
    private const string LivingHatStats = "76#1#0#0#0d0+1,3ca#2412#0#0#0d0+9234,3cb#1#0#0#0d0+1,3cc#1#0#0#0d0+1,3cd#a#0#0#0d0+10,3ce#9#0#0#0d0+9";

    /// <summary>Jet fixe du pain, celui de son modèle : « 0d0+10 », 10 points de vie (<c>getRandomValue</c>).</summary>
    private const string LivingBreadStats = "6e#a#0#0#0d0+10";

    /// <summary>Début exact du <c>OCO</c> de l'objet porté (<c>obvijevanOCO_Packet</c>) : identifiant, modèle et quantité en hexadécimal, position décimale.</summary>
    private static string LivingOco(int id, int template, int position) =>
        "OCO" + id.ToString("x", CultureInfo.InvariantCulture) + "~" + template.ToString("x", CultureInfo.InvariantCulture) + "~1~" + position.ToString(CultureInfo.InvariantCulture) + "~";

    /// <summary>
    /// Cellule praticable de la carte (cellules du bot décodées), hors téléporteur et déclencheur, à 2 à 4 cases de <paramref name="fromCell"/> :
    /// la cible du pain n'est pas sur la cellule du lanceur, le clic sur son sprite reste sans ambiguïté.
    /// </summary>
    private static Cell LivingTargetCell(int mapId, int fromCell)
    {
        if (!Map.AllBotMaps.TryGetValue(mapId, out Map info) || info == null || string.IsNullOrEmpty(info.MapData)) return null;
        Map decoded = DecodeCells(info);
        Cell from = decoded?.GetCellFromId((short)fromCell);
        if (from == null || decoded.MapCells == null) return null;
        IReadOnlyDictionary<int, Trigger> triggers = Triggers.ForMap(mapId);
        return decoded.MapCells.Where(c => c != null && c.CellID != fromCell && c.IsWalkable() && c.C_Types != Tool_BotProtocol.Game.Maps.Enums.CellTypes.TELEPORT_CELL && !triggers.ContainsKey(c.CellID))
            .Select(c => new { c, d = from.GetDistanceBetweenCells(c) }).Where(x => x.d >= 2 && x.d <= 4)
            .OrderBy(x => x.d).ThenBy(x => x.c.CellID).Select(x => x.c).FirstOrDefault();
    }

    /// <summary>Rangée des objets vivants (visible/Nourrir/Apparence/Dissocier) et lignes de la fiche du volet de l'inventaire (fil d'interface).</summary>
    private static string[] LivingState(Outil_Azur_complet.Bot.Panels.InventoryPanel panel) => new[]
    {
        ((Control)Field(panel, "livingActions")).Visible + "/" + ((Control)Field(panel, "feedItem")).Enabled + "/" + ((Control)Field(panel, "skinItem")).Enabled
            + "/" + ((Control)Field(panel, "dissociateItem")).Enabled,
        string.Join(" | ", ((ItemTooltip)Field(panel, "sheet")).Lines),
        LivingLayout(panel)
    };

    /// <summary>
    /// Place de la rangée des objets vivants dans le volet : « dégagée » quand elle est entièrement sous la grille du sac et au-dessus
    /// de l'aide (rien ne la recouvre), avec les hauteurs du volet, de la grille et de la fiche pour le journal.
    /// </summary>
    private static string LivingLayout(Outil_Azur_complet.Bot.Panels.InventoryPanel panel)
    {
        var row = (Control)Field(panel, "livingActions"); var grid = (Control)Field(panel, "inventory");
        var help = (Control)Field(panel, "inventoryHelp"); var sheetArea = (Control)Field(panel, "sheetArea");
        bool clear = row.Visible && row.Top >= grid.Bottom && row.Bottom <= help.Top && (!sheetArea.Visible || sheetArea.Bottom <= row.Top);
        return (clear ? "dégagée" : "recouverte") + " (volet " + row.Parent.ClientSize.Height + " px, grille " + grid.Top + "-" + grid.Bottom + ", fiche "
            + (sheetArea.Visible ? sheetArea.Height + " px" : "cachée") + ", rangée " + row.Top + "-" + row.Bottom + ", aide " + help.Top + ")";
    }

    /// <summary>
    /// x13. Objet vivant et objet à cible (lot X13), avec la fenêtre de jeu du porteur. Deux comptes inventés sur la carte 164 : le porteur
    /// a dans la base un chapeau porté (case 6) avec un objet vivant (<see cref="LivingHatStats"/>), un chapeau qui lui sert de repas et un
    /// pain ; la cible est à deux cases au moins, à la moitié de sa vie. Le bot lit l'objet vivant de l'<c>ASK</c> (associé, type 16, 9 points,
    /// niveau 1) et refuse sur place l'apparence 2. Dans le volet de l'inventaire : « Nourrir » ouvre <c>FEED_ITEM</c> avec le seul chapeau
    /// échangeable du sac, « Valider » puis « Oui » à <c>CONFIRM_FOOD_LIVING_ITEM</c> → <c>Of&lt;chapeau&gt;|6|&lt;repas&gt;</c> → <c>OR</c> du repas,
    /// <c>OCO</c> à 10 points (niveau 2) ; « Choix de l'apparence » ouvre deux cases, la 2 → <c>Os&lt;chapeau&gt;|6|2</c> → <c>OCO</c> et
    /// <c>Oa</c> « 2412~16~2 » chez l'autre joueur ; « Dissocier » → <c>Ox&lt;chapeau&gt;|6</c> → <c>OAKO</c> d'un objet vivant 9234 libre (10 points,
    /// apparence 2), <c>OCO</c> du chapeau sans 970-974, <c>Oa</c> du chapeau nu. Enfin « Cibler avec cet objet » sur le pain ferme le volet et
    /// arme la visée ; le clic sur le sprite de la cible → <c>OU&lt;pain&gt;|&lt;cible&gt;|&lt;cellule&gt;</c> → <c>eUK&lt;cible&gt;|17</c> aux deux,
    /// <c>Im01;10</c> et la vie rendue chez la cible, <c>OR</c> du pain chez le porteur. Objets détruits en jeu puis comptes supprimés (x4).
    /// </summary>
    private static async Task<string> LivingItemAsync()
    {
        RequirePetsData();
        Cell targetCell = LivingTargetCell(PetsMap, PetsCell);
        if (targetCell == null) throw new SkipScenario("aucune cellule praticable à 2 à 4 cases de la cellule " + PetsCell + " sur la carte " + PetsMap);
        using (MySql.Data.MySqlClient.MySqlConnection db = OpenAccountsDatabase(out string schema))
        {
            var holder = new PetOwner { Label = "Viv" }; var other = new PetOwner { Label = "Cib" };
            var evidence = new List<string>();
            GameClientFullform screen = null;
            try
            {
                // Base : objets du porteur à des identifiants hors de la plage de StarLoco, retenus avant d'être écrits (nettoyage).
                int baseId = 630000000 + random.Next(1, 100000) * 10;
                int hatId = baseId, foodId = baseId + 1, breadId = baseId + 2;
                foreach (int id in new[] { hatId, foodId, breadId }) holder.Seen.Add((uint)id);
                holder.Character = Invent(db, schema, a.Id, PetsMap, PetsCell, 10000, holder.Created);
                string insert = "INSERT INTO " + schema + ".`world.entity.objects` (id, template, quantity, position, stats, puit) VALUES (@id, @template, 1, @position, @stats, 0)";
                Sql(db, insert, "@id", hatId, "@template", LivingHatTemplate, "@position", LivingHatSlot, "@stats", LivingHatStats);
                Sql(db, insert, "@id", foodId, "@template", LivingFoodTemplate, "@position", -1, "@stats", string.Empty);
                Sql(db, insert, "@id", breadId, "@template", LivingBreadTemplate, "@position", -1, "@stats", LivingBreadStats);
                Sql(db, "UPDATE " + schema + ".players SET objets = @objets WHERE id = @id AND account = @account",
                    "@objets", string.Join("|", new[] { hatId, foodId, breadId }.Select(id => id.ToString(CultureInfo.InvariantCulture))) + "|", "@id", holder.Character.Id, "@account", holder.Character.Guid);
                other.Character = Invent(db, schema, b.Id, PetsMap, targetCell.CellID, 10000, other.Created);
                Sql(db, "UPDATE " + schema + ".players SET pdvper = @life WHERE id = @id AND account = @account", "@life", LivingTargetLife, "@id", other.Character.Id, "@account", other.Character.Guid);
                Journal("[test] " + holder.Character.Name + " : chapeau vivant " + hatId + " (case " + LivingHatSlot + ", " + LivingHatStats + "), repas " + foodId + ", pain " + breadId
                    + " ; " + other.Character.Name + " en " + targetCell.CellID + " à " + LivingTargetLife + " % de sa vie");

                // Entrées en jeu l'une après l'autre ; le porteur a sa fenêtre de jeu.
                LiveSession v = holder.Session = new LiveSession(holder.Label, holder.Character.Login, holder.Character.Password);
                evidence.Add(await EnterExistingAsync(v, holder.Character.Id, holder.Character.Name, PetsMap, s =>
                    s.Account.Game.character.Player_Selection += () => OnUi(() =>
                    {
                        if (screen != null) return;
                        // Taille par défaut de la fenêtre de jeu (1100 × 760) : la rangée des objets vivants doit y rester dégagée.
                        screen = new GameClientFullform(s.Account) { StartPosition = FormStartPosition.Manual, Location = new Point(60, 60), ShowInTaskbar = false };
                        screen.Show();
                    })).ConfigureAwait(false));
                Tool_BotProtocol.Game.Perso.Inventory.InventoryClass bag = holder.Bag;
                Func<Tool_BotProtocol.Game.Perso.Inventory.LivingItem> hatLiving = () => Tool_BotProtocol.Game.Perso.Inventory.LivingItem.From(bag.GetByInventoryId((uint)hatId));
                Check(await v.Until(() => bag.GetByInventoryId((uint)hatId) != null && bag.GetByInventoryId((uint)foodId) != null && bag.GetByInventoryId((uint)breadId) != null, 10).ConfigureAwait(false),
                    "Chapeau, repas ou pain absents de l'inventaire lu par le bot (ASK)");
                holder.Remember();
                Tool_BotProtocol.Game.Perso.Inventory.InventoryObjects hat = bag.GetByInventoryId((uint)hatId);
                Tool_BotProtocol.Game.Perso.Inventory.LivingItem living = hatLiving();
                Check((int)hat.position == LivingHatSlot && living != null && living.IsAssociated && living.AssociatedTemplate == LivingObviTemplate && living.ItemType == 16
                    && living.Mood == 1 && living.Skin == 0 && living.Xp == 9 && living.Level == 1, "Chapeau vivant mal lu (case " + (int)hat.position + ") : " + Short(hat.Stats, 140));
                Check(Tool_BotProtocol.Game.Perso.Inventory.LivingItem.FoodsFor(bag.Objets, hat).Select(x => x.Inventory_ID).SequenceEqual(new[] { (uint)foodId }),
                    "Repas proposés : " + string.Join(",", Tool_BotProtocol.Game.Perso.Inventory.LivingItem.FoodsFor(bag.Objets, hat).Select(x => x.Inventory_ID)));
                int sent = v.SentCount;
                var early = await bag.SetSkinAsync(hat, 2).ConfigureAwait(false);
                Check(!early.Sent && v.SentCount == sent, "Apparence 2 acceptée au niveau 1 : " + early.Message);
                evidence.Add("ASK : " + Short(hat.Stats, 90) + " → associé à " + living.AssociatedTemplate + ", type " + living.ItemType + ", " + living.Xp + " points, niveau " + living.Level
                    + " ; apparence 2 refusée sur place (« " + early.Message + " »)");

                LiveSession w = other.Session = new LiveSession(other.Label, other.Character.Login, other.Character.Password);
                evidence.Add(await EnterExistingAsync(w, other.Character.Id, other.Character.Name, PetsMap).ConfigureAwait(false));
                other.Remember();
                Check(await v.Until(() => v.Map.GetActor(w.Id) != null, 10).ConfigureAwait(false) && await w.Until(() => w.Map.GetActor(v.Id) != null, 10).ConfigureAwait(false),
                    "Les deux personnages ne se voient pas sur la carte " + PetsMap);
                Func<int> life = () => w.Account.Game.character.stats?.VitalityActual ?? -1, maxLife = () => w.Account.Game.character.stats?.MaxVitality ?? -1;
                Check(await w.Until(() => maxLife() > 0 && life() >= 0 && life() < maxLife(), 10).ConfigureAwait(false), "La cible n'est pas blessée : vie " + life() + " / " + maxLife());
                Check(await WaitOnUi(() => screen != null && screen.IsHandleCreated && screen.Visible ? screen : null, 20).ConfigureAwait(false) != null, "La fenêtre de jeu du porteur ne s'est pas ouverte");

                // Volet de l'inventaire, chapeau porté sélectionné : rangée des objets vivants et section de la fiche.
                var panel = await OnUi(() =>
                {
                    var found = screen.Panels.Get<Outil_Azur_complet.Bot.Panels.InventoryPanel>();
                    if (found != null) screen.Panels.Show(found);
                    return found;
                }).ConfigureAwait(false);
                Check(panel != null, "Pas de volet d'inventaire dans la fenêtre de jeu");
                await OnUi(() => ((EquipmentPlateau)Field(panel, "plateau"))[LivingHatSlot].Activate()).ConfigureAwait(false);
                string[] shown = await WaitOnUi(() =>
                {
                    string[] state = LivingState(panel);
                    return panel.CurrentItem?.Inventory_ID == (uint)hatId && state[0] == "True/True/True/True" && state[1].Contains("Apparence 1 sur 1")
                        && state[2].StartsWith("dégagée", StringComparison.Ordinal) ? state : null;
                }, 10).ConfigureAwait(false);
                Check(shown != null, "Rangée ou fiche de l'objet vivant absente ou recouverte : " + string.Join(" ; ", await OnUi(() => LivingState(panel)).ConfigureAwait(false)));
                evidence.Add("volet de l'inventaire, chapeau porté : rangée " + shown[2]);

                // « Nourrir » : FEED_ITEM avec le seul repas, Valider, « Oui » → Of ; OR du repas et OCO à 10 points.
                int vMark = v.ReceivedCount; sent = v.SentCount;
                await OnUi(() => ((Button)Field(panel, "feedItem")).PerformClick()).ConfigureAwait(false);
                Form choice = await WaitOnUi(() => OpenDialog(BotDialogs.FeedDialogName), 10).ConfigureAwait(false);
                Check(choice != null, "« Nourrir » n'a pas ouvert le choix du repas");
                string[] offered = await OnUi(() => new[] { choice.Text, string.Join(",", AllControls(choice).OfType<ItemGrid>().First(x => x.Name == "living-feed-grid").Items.Select(x => x.Inventory_ID)) }).ConfigureAwait(false);
                Check(offered[1] == foodId.ToString(CultureInfo.InvariantCulture), "Repas de la grille FEED_ITEM : " + offered[1]);
                await OnUi(() =>
                {
                    AllControls(choice).OfType<ItemGrid>().First(x => x.Name == "living-feed-grid").SelectItem((uint)foodId);
                    AllControls(choice).OfType<Button>().First(x => x.Name == "dialog-validate").PerformClick();
                }).ConfigureAwait(false);
                Form caution = await WaitOnUi(() => OpenDialog(BotDialogs.FeedDialogName + "-confirm"), 10).ConfigureAwait(false);
                Check(caution != null, "Pas de confirmation CONFIRM_FOOD_LIVING_ITEM après le choix du repas");
                Check(!v.SentSince(sent).Any(p => p.StartsWith("Of", StringComparison.Ordinal)), "Of est parti avant la confirmation");
                string question = await OnUi(() => DialogMessage(caution)).ConfigureAwait(false);
                await OnUi(() => AllControls(caution).OfType<Button>().First(x => Equals(x.Tag, BotDialogResult.Yes)).PerformClick()).ConfigureAwait(false);
                string feed = "Of" + hatId + "|" + LivingHatSlot + "|" + foodId;
                Check(await v.Until(() => v.SentSince(sent).Contains(feed), 5).ConfigureAwait(false), "« Oui » n'a pas envoyé " + feed + " : " + string.Join(" ", v.SentSince(sent)));
                string eaten = await v.Expect(p => p == "OR" + foodId || p.StartsWith("OQ" + foodId + "|", StringComparison.Ordinal), "OR du repas", 10, vMark).ConfigureAwait(false);
                string fedOco = await v.Expect(p => p.StartsWith(LivingOco(hatId, LivingHatTemplate, LivingHatSlot), StringComparison.Ordinal), "OCO du chapeau nourri", 10, vMark).ConfigureAwait(false);
                Check(fedOco.Contains("3ce#0#0#a"), "OCO sans les 10 points d'expérience : " + fedOco);
                Check(await v.Until(() => hatLiving()?.Xp == 10 && hatLiving().Level == 2 && bag.GetByInventoryId((uint)foodId) == null, 5).ConfigureAwait(false),
                    "OCO ou OR non relus : " + hatLiving()?.Xp + " points, repas " + (bag.GetByInventoryId((uint)foodId) == null ? "retiré" : "encore là"));
                string[] fed = await WaitOnUi(() => { string[] state = LivingState(panel); return state[0] == "True/True/True/True" && state[1].Contains("Apparence 1 sur 2") ? state : null; }, 10).ConfigureAwait(false);
                Check(fed != null, "La fiche ne suit pas l'OCO du repas : " + string.Join(" ; ", await OnUi(() => LivingState(panel)).ConfigureAwait(false)));
                evidence.Add("« " + offered[0] + " » : " + offered[1] + ", « " + question.Replace("\n", " ") + " » Oui → " + feed + " → " + eaten + ", " + Short(fedOco, 150) + " ; fiche « " + Short(fed[1], 160) + " »");
                await Capture("23-reel-x13-objet-vivant", screen).ConfigureAwait(false);

                // « Choix de l'apparence » : deux cases, la 2 → Os ; OCO et Oa de l'apparence chez l'autre joueur.
                string obviHex = LivingObviTemplate.ToString("x", CultureInfo.InvariantCulture);
                vMark = v.ReceivedCount; sent = v.SentCount; int wMark = w.ReceivedCount;
                await OnUi(() => ((Button)Field(panel, "skinItem")).PerformClick()).ConfigureAwait(false);
                Form skinChoice = await WaitOnUi(() => OpenDialog(BotDialogs.SkinDialogName), 10).ConfigureAwait(false);
                Check(skinChoice != null, "« Choix de l'apparence » n'a pas ouvert la boîte");
                string skins = await OnUi(() =>
                {
                    LivingSkinGrid grid = AllControls(skinChoice).OfType<LivingSkinGrid>().First(x => x.Name == "living-skin-grid");
                    return grid.SkinCount + "/" + grid.CurrentSkin + "/" + grid.SelectSkin(2) + "/" + grid.SelectedSkin;
                }).ConfigureAwait(false);
                Check(skins == "2/1/True/2", "Choix de l'apparence (cases/portée/choix/choisie) : " + skins);
                await OnUi(() => AllControls(skinChoice).OfType<Button>().First(x => x.Name == "dialog-validate").PerformClick()).ConfigureAwait(false);
                string skin = "Os" + hatId + "|" + LivingHatSlot + "|2";
                Check(await v.Until(() => v.SentSince(sent).Contains(skin), 5).ConfigureAwait(false), "« Valider » n'a pas envoyé " + skin + " : " + string.Join(" ", v.SentSince(sent)));
                string skinOco = await v.Expect(p => p.StartsWith(LivingOco(hatId, LivingHatTemplate, LivingHatSlot), StringComparison.Ordinal) && p.Contains("3cc#0#0#2"), "OCO de l'apparence 2", 10, vMark).ConfigureAwait(false);
                string look = await w.Expect(p => p.StartsWith("Oa" + v.Id + "|", StringComparison.Ordinal) && p.Contains(obviHex + "~16~2"), "Oa de l'apparence 2 chez l'autre joueur", 10, wMark).ConfigureAwait(false);
                Check(await v.Until(() => hatLiving()?.Skin == 1, 5).ConfigureAwait(false), "Apparence de l'OCO non relue : " + hatLiving()?.Skin);
                string[] skinned = await WaitOnUi(() => { string[] state = LivingState(panel); return state[0] == "True/True/True/True" && state[1].Contains("Apparence 2 sur 2") ? state : null; }, 10).ConfigureAwait(false);
                Check(skinned != null, "La fiche ne suit pas l'OCO de l'apparence : " + string.Join(" ; ", await OnUi(() => LivingState(panel)).ConfigureAwait(false)));
                evidence.Add("apparence (" + skins + ") → " + skin + " → " + Short(skinOco, 120) + " ; l'autre joueur : " + Short(look, 60));

                // « Dissocier » : Ox ; OAKO de l'objet vivant libre, OCO du chapeau sans 970-974, Oa du chapeau nu.
                vMark = v.ReceivedCount; sent = v.SentCount; wMark = w.ReceivedCount;
                await OnUi(() => ((Button)Field(panel, "dissociateItem")).PerformClick()).ConfigureAwait(false);
                string dissociate = "Ox" + hatId + "|" + LivingHatSlot;
                Check(await v.Until(() => v.SentSince(sent).Contains(dissociate), 5).ConfigureAwait(false), "« Dissocier » n'a pas envoyé " + dissociate + " : " + string.Join(" ", v.SentSince(sent)));
                string born = await v.Expect(p => p.StartsWith("OAKO", StringComparison.Ordinal) && p.Contains("~" + obviHex + "~"), "OAKO de l'objet vivant recréé", 10, vMark).ConfigureAwait(false);
                string bare = await v.Expect(p => p.StartsWith(LivingOco(hatId, LivingHatTemplate, LivingHatSlot), StringComparison.Ordinal) && !p.Contains("3ca#"), "OCO du chapeau sans objet vivant", 10, vMark).ConfigureAwait(false);
                string plain = await w.Expect(p => p.StartsWith("Oa" + v.Id + "|", StringComparison.Ordinal) && !p.Contains("~16~"), "Oa du chapeau nu chez l'autre joueur", 10, wMark).ConfigureAwait(false);
                Tool_BotProtocol.Game.Perso.Inventory.InventoryObjects freed = null;
                Check(await v.Until(() => (freed = Owned(v, LivingObviTemplate)) != null && hatLiving() == null, 5).ConfigureAwait(false), "OAKO ou OCO de la dissociation non relus");
                holder.Remember();
                Tool_BotProtocol.Game.Perso.Inventory.LivingItem loose = Tool_BotProtocol.Game.Perso.Inventory.LivingItem.From(freed);
                Check(loose != null && loose.IsLiving && !loose.IsAssociated && loose.Xp == 10 && loose.Skin == 1 && (int)freed.position == -1,
                    "Objet vivant libre mal lu : " + Short(freed.Stats, 120));
                Check(await WaitOnUi(() => LivingState(panel)[0].StartsWith("False/", StringComparison.Ordinal) ? "cachée" : null, 10).ConfigureAwait(false) != null,
                    "La rangée des objets vivants reste affichée pour le chapeau nu");
                evidence.Add(dissociate + " → " + Short(born, 90) + " (objet vivant " + freed.Inventory_ID + " libre, " + loose.Xp + " points), " + Short(bare, 60) + " ; l'autre joueur : " + Short(plain, 40));

                // « Cibler avec cet objet » sur le pain : volet fermé, visée armée ; clic sur le sprite de la cible → OU<pain>|<cible>|<cellule>.
                Tool_BotProtocol.Game.Perso.Inventory.InventoryObjects bread = bag.GetByInventoryId((uint)breadId);
                string[] entry = await OnUi(() =>
                {
                    var grid = (ItemGrid)Field(panel, "inventory");
                    grid.SelectItem((uint)breadId);
                    typeof(Outil_Azur_complet.Bot.Panels.InventoryPanel).GetMethod("ShowMenu", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(panel, new object[] { bread, grid, new Point(4, 4) });
                    var strip = (ContextMenuStrip)Field(panel, "itemMenu");
                    ToolStripItem target = strip.Items.Cast<ToolStripItem>().FirstOrDefault(x => x.Name == "inventory-menu-target");
                    string[] seen = { target?.Text ?? "(absente)", (target != null && target.Enabled).ToString() };
                    if (target != null && target.Enabled) target.PerformClick();
                    strip.Close();
                    return seen;
                }).ConfigureAwait(false);
                Check(entry[1] == "True", "Pas d'entrée « Cibler avec cet objet » active pour le pain : " + entry[0]);
                Check(await WaitOnUi(() => screen.Map.ItemTargetMode && screen.Map.TargetedItem?.Inventory_ID == (uint)breadId && !screen.Panels.IsOpen(panel) ? "visée" : null, 10).ConfigureAwait(false) != null,
                    "« " + entry[0] + " » n'a pas fermé le volet ni armé la visée du pain");
                MapActor aimed = v.Map.GetActor(w.Id);
                Check(aimed != null && aimed.CellId == targetCell.CellID, "Sprite de la cible absent ou ailleurs qu'en " + targetCell.CellID + " : " + aimed);
                int lifeBefore = life();
                vMark = v.ReceivedCount; sent = v.SentCount; wMark = w.ReceivedCount;
                await (await OnUi(() => screen.Map.Router.RouteActorAsync(aimed, (short)aimed.CellId, MouseButtons.Left)).ConfigureAwait(false)).ConfigureAwait(false);
                string use = "OU" + breadId + "|" + w.Id + "|" + targetCell.CellID;
                Check(await v.Until(() => v.SentSince(sent).Contains(use), 5).ConfigureAwait(false), "Le clic sur la cible n'a pas envoyé " + use + " : " + string.Join(" ", v.SentSince(sent)));
                Check(await OnUi(() => !screen.Map.ItemTargetMode).ConfigureAwait(false), "La visée reste armée après le clic");
                string emote = await v.Expect("eUK" + w.Id + "|17", "eUK<cible>|17 chez le porteur", 10, vMark).ConfigureAwait(false);
                await w.Expect("eUK" + w.Id + "|17", "eUK<cible>|17 chez la cible", 10, wMark).ConfigureAwait(false);
                string healed = await w.Expect(p => p.StartsWith("Im01;", StringComparison.Ordinal), "Im01 (vie rendue) chez la cible", 10, wMark).ConfigureAwait(false);
                string used = await v.Expect(p => p == "OR" + breadId || p.StartsWith("OQ" + breadId + "|", StringComparison.Ordinal), "OR du pain chez le porteur", 10, vMark).ConfigureAwait(false);
                Check(int.TryParse(healed.Substring(5), NumberStyles.None, CultureInfo.InvariantCulture, out int gained) && gained > 0 && gained <= 10, "Im01 illisible : " + healed);
                // StarLoco régénère la vie hors combat entre deux As : la cible a au moins ce que le pain rend en plus.
                Check(await w.Until(() => life() >= lifeBefore + gained, 5).ConfigureAwait(false), "Vie de la cible " + life() + " au lieu d'au moins " + lifeBefore + " + " + gained + " (As)");
                Check(await v.Until(() => bag.GetByInventoryId((uint)breadId) == null, 5).ConfigureAwait(false), "Le pain reste dans le sac du porteur après " + used);
                evidence.Add("« " + entry[0] + " » → visée, clic sur " + other.Character.Name + " → " + use + " → " + emote + " (aux deux), cible : " + healed + ", vie " + lifeBefore + " → " + life()
                    + " ; porteur : " + used);
                return string.Join(" | ", evidence);
            }
            finally
            {
                await ClosePetOwnerAsync(db, schema, holder, () => CloseScreenAsync(screen)).ConfigureAwait(false);
                await ClosePetOwnerAsync(db, schema, other).ConfigureAwait(false);
            }
        }
    }
}

// Scénario x33 (lot X33) : ligne de vue vérifiée par le bot avant un sort (cible derrière un mur) et mode tactique pendant un combat.
internal static partial class Live
{
    /// <summary>
    /// Sort écrit dans la base pour le personnage inventé : Flèche Magique (lancer de 1 à 7 avec ligne de vue, 4 PA, données de niveau 1
    /// de <c>spells.xml</c> du client).
    /// </summary>
    private const short SightSpell = 161;
    private const string SightSpells = "161;1;b";
    private const int SightRange = 7;

    /// <summary>Paquets envoyés depuis la marque, sans les réponses automatiques du combat (<c>GT</c> à <c>GTR</c>, <c>GKK</c> à <c>GAF</c>).</summary>
    private static string[] OwnPacketsSince(LiveSession s, int mark) =>
        s.SentSince(mark).Where(p => p != "GT" && !p.StartsWith("GKK", StringComparison.Ordinal)).ToArray();

    /// <summary>Cibles valables de la visée dessinée par la vue (fil d'interface).</summary>
    private static short[] SpellTargetsOf(GameClientFullform form)
    {
        UserMapControl map = UserMapOf(form);
        var targets = typeof(UserMapControl).GetField("spellTargets", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(map) as IEnumerable<short>;
        return targets == null ? new short[0] : Sorted(targets);
    }

    /// <summary>Décor dessiné par la vue de la fenêtre (fil d'interface).</summary>
    private static BotMapArtwork ArtworkOf(GameClientFullform form) =>
        typeof(UserMapControl).GetField("artwork", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(UserMapOf(form)) as BotMapArtwork;

    /// <summary>
    /// Cellule praticable de la carte, à portée de <paramref name="from"/>, cachée par le seul décor (<c>checkView</c> sans aucun
    /// combattant) : la cible « derrière un mur ».
    /// </summary>
    private static Cell WallHidden(Map map, Cell from) =>
        map.MapCells.Where(c => c != null && c.IsWalkable() && c.LineofSight && c.CellID != from.CellID
                && from.GetDistanceBetweenCells(c) >= 1 && from.GetDistanceBetweenCells(c) <= SightRange
                && !LineOfSight.Check(map, from.CellID, c.CellID, null))
            .OrderBy(c => from.GetDistanceBetweenCells(c)).ThenBy(c => c.CellID).FirstOrDefault();

    /// <summary>
    /// Un personnage inventé (Flèche Magique écrite dans la base) entre sur la carte de A avec sa fenêtre de jeu ; A le défie. Au
    /// placement, il prend une cellule de son équipe d'où une cellule à portée est cachée par le décor, puis le bouton « Mode Tactique »
    /// de sa fenêtre passe la carte en mode tactique sans envoyer de paquet (aplat, objets 10000 à 10002). Le mode reste actif quand le
    /// combat commence. À son tour, la Flèche Magique dessine sa portée : la cellule cachée y est, mais pas parmi les cibles mises en
    /// valeur (#0066CC) ; le bot la refuse (« hors de la ligne de vue »), le survol montre la croix d'interdiction et un clic n'envoie
    /// rien. Un clic sur une cellule visible lance le sort (<c>GA300161;cellule</c> → <c>GA;300</c>). Un second clic sur le bouton rend
    /// le décor normal, toujours sans paquet. Le personnage abandonne ; compte et personnage supprimés de la base.
    /// </summary>
    private static async Task<string> TacticSightAsync()
    {
        RequireMaps();
        if (!LangData.IsLoaded("spells")) throw new SkipScenario("textes du client « spells » absents : le bot ne connaît ni la portée ni la ligne de vue des sorts");
        using (MySql.Data.MySqlClient.MySqlConnection db = OpenAccountsDatabase(out string schema))
        {
            var created = new List<InventedCharacter>();
            var evidence = new List<string>();
            LiveSession s = null;
            GameClientFullform screen = null;
            try
            {
                Cell place = FreeCellNear(a, 2, 6);
                Check(place != null, "Aucune cellule libre près de A pour le personnage inventé");
                InventedCharacter archer = Invent(db, schema, a.Id, a.MapId, place.CellID, 10000, created);
                Sql(db, "UPDATE " + schema + ".players SET spells = @spells WHERE id = @id AND account = @account",
                    "@spells", SightSpells, "@id", archer.Id, "@account", archer.Guid);
                s = new LiveSession("V", archer.Login, archer.Password);
                evidence.Add(await EnterExistingAsync(s, archer.Id, archer.Name, a.MapId, started =>
                    started.Account.Game.character.Player_Selection += () => OnUi(() => { if (screen == null) { screen = OpenScreen(started); screen.Show(); } })).ConfigureAwait(false));
                LiveSession sv = s;
                Check(await sv.Until(() => sv.Account.Game.character.Spells.Values.Any(x => x.ID == SightSpell), 10).ConfigureAwait(false),
                    "Le bot ne voit pas la Flèche Magique : " + string.Join(",", sv.Account.Game.character.Spells.Values.Select(x => x.ID)));
                Check(await WaitOnUi(() => screen != null && screen.IsHandleCreated && screen.Visible && UserMapOf(screen) != null ? screen : null, 20).ConfigureAwait(false) != null,
                    "La fenêtre de jeu du personnage inventé ne s'est pas ouverte");
                Check(await a.Until(() => a.Map.GetActor(sv.Id) != null, 10).ConfigureAwait(false) && await sv.Until(() => sv.Map.GetActor(a.Id) != null, 10).ConfigureAwait(false),
                    "A et le personnage inventé ne se voient pas sur la carte " + a.MapId);
                Fights fa = a.Account.Game.Fight, fv = sv.Account.Game.Fight;
                Check(!fv.TacticMode, "Mode tactique déjà actif avant le combat");

                int markA = a.ReceivedCount, markV = sv.ReceivedCount;
                var challenge = await a.Account.Game.Interactions.MapActions.ChallengeAsync(sv.Id).ConfigureAwait(false);
                Check(challenge.Sent, "Défi refusé par le bot : " + challenge.Message);
                await sv.Expect(p => p.StartsWith("GA;900;" + a.Id + ";", StringComparison.Ordinal), "GA;900 (défi reçu)", 10, markV).ConfigureAwait(false);
                Check(await sv.Until(() => sv.Account.Game.Interactions.MapActions.PendingChallenge?.ChallengerId == a.Id, 5).ConfigureAwait(false), "Pas de défi en attente");
                var accept = await sv.Account.Game.Interactions.MapActions.AcceptChallengeAsync(a.Id).ConfigureAwait(false);
                Check(accept.Sent, "Acceptation du défi refusée par le bot : " + accept.Message);
                await a.Expect("GJK", "GJK (entrée en combat, A)", 15, markA).ConfigureAwait(false);
                await sv.Expect("GJK", "GJK (entrée en combat, personnage inventé)", 15, markV).ConfigureAwait(false);
                Check(await a.Until(() => fa.IsPlacement && fv.IsPlacement && Self(a) != null && Self(sv) != null && fv.PlacementCells.Length > 0, 10).ConfigureAwait(false),
                    "Placement incomplet");

                // Placement : une cellule de l'équipe d'où une cellule à portée est cachée par le décor.
                Map map = sv.Map;
                var taken = new HashSet<int>(fv.Fighters.Values.Select(f => (int)f.CellId));
                Cell current = map.GetCellFromId((short)Self(sv).CellId);
                Cell start = new[] { current }.Concat(fv.PlacementCells.Where(id => !taken.Contains(id)).Select(id => map.GetCellFromId(id)))
                    .FirstOrDefault(c => c != null && WallHidden(map, c) != null);
                if (start == null) throw new SkipScenario("aucune cellule de placement de l'équipe n'a de cellule cachée par le décor à " + SightRange + " cellules au plus sur la carte " + a.MapId);
                if (start.CellID != current.CellID)
                {
                    int markPlace = sv.ReceivedCount;
                    var placed = await fv.PlaceAsync(start.CellID).ConfigureAwait(false);
                    Check(placed.Sent, "Changement de placement refusé par le bot : " + placed.Message);
                    Check(await sv.Until(() => Self(sv)?.CellId == start.CellID, 10).ConfigureAwait(false),
                        "Le personnage n'est pas arrivé en " + start.CellID + " au placement (cellule " + Self(sv)?.CellId + ")");
                }
                Cell hidden = WallHidden(map, start);
                evidence.Add("placement en " + start.CellID + " : cellule " + hidden.CellID + " à " + start.GetDistanceBetweenCells(hidden) + " cellules, cachée par le décor");

                // Mode tactique au placement : bouton de la fenêtre, aucun paquet, décor tactique dessiné.
                int sentMark = sv.SentCount;
                await OnUi(() => screen.FightOptions.TacticButton.PerformClick()).ConfigureAwait(false);
                string drawn = await WaitOnUi(() =>
                {
                    BotMapArtwork artwork = ArtworkOf(screen);
                    return fv.TacticMode && screen.FightOptions.TacticButton.Checked && UserMapOf(screen).TacticView && artwork != null && artwork.TacticMode
                        && !ReferenceEquals(artwork.DisplayedCells, artwork.Cells) ? artwork.Status : null;
                }, 10).ConfigureAwait(false);
                Check(drawn != null, "Le bouton « Mode Tactique » n'a pas passé la carte en mode tactique");
                Check(drawn == "Mode tactique", "Visuels tactiques absents du décor exporté : " + drawn);
                int[] tacticObjects = await OnUi(() => ArtworkOf(screen).DisplayedCells.Where(c => c.Active).Select(c => c.Object1Id).Distinct().OrderBy(id => id).ToArray()).ConfigureAwait(false);
                Check(tacticObjects.Length > 0 && tacticObjects.All(id => id == BotMapArtwork.TacticObstacleObject || id == BotMapArtwork.TacticWalkableObject || id == BotMapArtwork.TacticHoleObject),
                    "Objets 1 du décor tactique : " + string.Join(",", tacticObjects));
                await Task.Delay(500).ConfigureAwait(false);
                Check(OwnPacketsSince(sv, sentMark).Length == 0, "Le mode tactique a envoyé un paquet : " + string.Join(" | ", OwnPacketsSince(sv, sentMark)));
                evidence.Add("bouton « Mode Tactique » au placement : aucun paquet, aplat et objets " + string.Join("/", tacticObjects));

                await ReadyAsync(sv, markV).ConfigureAwait(false);
                await ReadyAsync(a, markA).ConfigureAwait(false);
                await a.Expect("GS", "GS (début du combat)", 30, markA).ConfigureAwait(false);

                bool played = false;
                DateTime until = DateTime.UtcNow.AddSeconds(90);
                while (!played && DateTime.UtcNow < until && fv.IsInFight)
                {
                    int actor = await WaitForTurnOf(new[] { a, sv }, 40).ConfigureAwait(false);
                    if (actor == a.Id)
                    {
                        int mark = a.ReceivedCount;
                        var passed = await fa.PassTurnAsync().ConfigureAwait(false);
                        Check(passed.Sent, "Fin de tour de A refusée par le bot : " + passed.Message);
                        await a.Expect("GTF" + a.Id, "GTF (fin du tour de A)", 10, mark).ConfigureAwait(false);
                        await a.Until(() => !fa.IsMyTurn, 5).ConfigureAwait(false);
                    }
                    else if (actor == sv.Id)
                    {
                        evidence.AddRange(await SightTurnAsync(sv, screen, hidden).ConfigureAwait(false));
                        played = true;
                    }
                    else break;
                }
                Check(played, "Le personnage inventé n'a pas joué son tour");

                // Second clic : décor normal, toujours sans paquet, combat en cours.
                sentMark = sv.SentCount;
                await OnUi(() => screen.FightOptions.TacticButton.PerformClick()).ConfigureAwait(false);
                Check(await WaitOnUi(() => !fv.TacticMode && !screen.FightOptions.TacticButton.Checked && !UserMapOf(screen).TacticView
                    && ArtworkOf(screen) != null && !ArtworkOf(screen).TacticMode && ReferenceEquals(ArtworkOf(screen).DisplayedCells, ArtworkOf(screen).Cells) ? "normal" : null, 10).ConfigureAwait(false) != null,
                    "Le second clic n'a pas rendu le décor normal");
                await Task.Delay(500).ConfigureAwait(false);
                Check(OwnPacketsSince(sv, sentMark).Length == 0, "Le retour au décor normal a envoyé un paquet : " + string.Join(" | ", OwnPacketsSince(sv, sentMark)));
                evidence.Add("second clic pendant le combat : décor normal, aucun paquet");

                int endA = a.ReceivedCount, endV = sv.ReceivedCount;
                var giveUp = await fv.GiveUpAsync().ConfigureAwait(false);
                Check(giveUp.Sent, "Abandon refusé par le bot : " + giveUp.Message);
                await a.Expect("GE", "GE (fin du combat, A)", 20, endA).ConfigureAwait(false);
                await sv.Expect(p => p.StartsWith("GE", StringComparison.Ordinal) || p.StartsWith("GV", StringComparison.Ordinal), "GE ou GV (fin du combat)", 20, endV).ConfigureAwait(false);
                await WaitBackOnMap(a).ConfigureAwait(false); await WaitBackOnMap(sv).ConfigureAwait(false);
                Check(await WaitOnUi(() => !UserMapOf(screen).IsAiming && !UserMapOf(screen).TacticView ? "rendue" : null, 10).ConfigureAwait(false) != null,
                    "La visée ou le mode tactique survit à la fin du combat");
                evidence.Add("abandon → GE : visée effacée, décor normal");
                return string.Join(" ; ", evidence);
            }
            finally
            {
                if (s?.Account?.Game?.Fight != null) s.Account.Game.Fight.TacticMode = false;
                if (screen != null) await OnUi(() => { if (!screen.IsDisposed) { screen.Close(); screen.Dispose(); } }).ConfigureAwait(false);
                await ForgetAsync(db, schema, s == null ? new LiveSession[0] : new[] { s }, created).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Tour du personnage inventé, en mode tactique : la cellule cachée est dans la portée mais hors des cibles, refusée par le bot, sous
    /// la croix d'interdiction, et un clic n'envoie rien ; un clic sur une cellule visible lance la Flèche Magique.
    /// </summary>
    private static async Task<List<string>> SightTurnAsync(LiveSession sv, GameClientFullform screen, Cell hidden)
    {
        var evidence = new List<string>();
        Fights fv = sv.Account.Game.Fight;
        Check(fv.TacticMode && await OnUi(() => UserMapOf(screen).TacticView).ConfigureAwait(false), "Le mode tactique n'a pas survécu au début du combat");
        Cell own = sv.Map.GetCellFromId((short)Self(sv).CellId);
        Check(own != null && fv.ActionPoints >= 4, "Tour sans cellule ni PA : PA " + fv.ActionPoints);
        await OnUi(() => screen.Map.SelectSpell(SightSpell)).ConfigureAwait(false);
        short[] range = await WaitOnUi(() =>
        {
            UserMapControl map = UserMapOf(screen);
            return map.IsAiming && map.AimIcon == "Spells/" + SightSpell ? Sorted(map.SpellRange) : null;
        }, 10).ConfigureAwait(false);
        Check(range != null && range.Contains(hidden.CellID), "Portée de la Flèche Magique sans la cellule cachée " + hidden.CellID + " : " + (range == null ? "aucune" : CellList(range)));
        short[] targets = await OnUi(() => SpellTargetsOf(screen)).ConfigureAwait(false);
        bool shown = await OnUi(() => UserMapOf(screen).AllowedZoneShown).ConfigureAwait(false);
        string reason = fv.GetSpellUnavailableReason(SightSpell, hidden.CellID);
        Check(shown && targets.Length > 0 && !targets.Contains(hidden.CellID) && reason == "Cible hors de la ligne de vue.",
            "Cellule cachée " + hidden.CellID + " : mise en valeur " + shown + ", cibles " + CellList(targets) + ", raison du bot « " + reason + " »");
        evidence.Add("Flèche Magique depuis " + own.CellID + " : portée de " + range.Length + " cellules, " + targets.Length + " cibles en #0066CC, "
            + hidden.CellID + " exclue (« " + reason + " »)");

        await HoverAsync(screen, own.CellID).ConfigureAwait(false);
        await HoverAsync(screen, hidden.CellID).ConfigureAwait(false);
        Check(await WaitOnUi(() => UserMapOf(screen).HoveredCell?.id == hidden.CellID && UserMapOf(screen).CursorForbidden ? "interdit" : null, 10).ConfigureAwait(false) != null,
            "Cellule cachée " + hidden.CellID + " sans croix d'interdiction");
        await Capture("24-reel-x33-tactique", screen).ConfigureAwait(false);
        int sentMark = sv.SentCount, paBefore = fv.ActionPoints;
        await (await OnUi(() => screen.Map.HandleCellActionAsync(hidden.CellID)).ConfigureAwait(false)).ConfigureAwait(false);
        await Task.Delay(500).ConfigureAwait(false);
        Check(!sv.SentSince(sentMark).Any(p => p.StartsWith("GA300", StringComparison.Ordinal)) && fv.ActionPoints == paBefore
            && await OnUi(() => screen.Map.SelectedSpellId == SightSpell).ConfigureAwait(false),
            "Un clic sur la cellule cachée a envoyé le sort ou désarmé la visée : " + string.Join(" | ", sv.SentSince(sentMark)));
        evidence.Add("cellule cachée " + hidden.CellID + " : croix d'interdiction, clic sans paquet");

        // Contrôle : une cellule visible et libre à portée est lancée et acceptée par StarLoco.
        var occupied = new HashSet<int>(fv.Fighters.Values.Where(f => !f.IsDead).Select(f => (int)f.CellId));
        Cell visible = targets.Select(id => sv.Map.GetCellFromId(id))
            .Where(c => c != null && !occupied.Contains(c.CellID) && own.GetDistanceBetweenCells(c) >= 2 && own.GetDistanceBetweenCells(c) <= 5)
            .OrderBy(c => own.GetDistanceBetweenCells(c)).ThenBy(c => c.CellID).FirstOrDefault();
        Check(visible != null, "Aucune cible visible et libre de 2 à 5 cellules pour la Flèche Magique");
        await HoverAsync(screen, visible.CellID).ConfigureAwait(false);
        sentMark = sv.SentCount; int markV = sv.ReceivedCount; paBefore = fv.ActionPoints;
        await (await OnUi(() => screen.Map.HandleCellActionAsync(visible.CellID)).ConfigureAwait(false)).ConfigureAwait(false);
        string cast = "GA300" + SightSpell + ";" + visible.CellID;
        Check(await sv.Until(() => sv.SentSince(sentMark).Contains(cast), 5).ConfigureAwait(false), "Le clic sur la cible visible n'a pas envoyé " + cast + " : " + string.Join(" | ", sv.SentSince(sentMark)));
        string launched = await sv.Expect(p => p.StartsWith("GA;300;" + sv.Id + ";", StringComparison.Ordinal) || p.StartsWith("GA;302;" + sv.Id + ";", StringComparison.Ordinal)
            || p.StartsWith("GA;0", StringComparison.Ordinal) || p.StartsWith("GAE", StringComparison.Ordinal), "GA;300 de la Flèche Magique", 10, markV).ConfigureAwait(false);
        Check(launched.StartsWith("GA;300;", StringComparison.Ordinal), "StarLoco ne lance pas la Flèche Magique en " + visible.CellID + " : " + launched);
        Check(await sv.Until(() => !fv.IsActionPending && fv.ActionPoints < paBefore, 10).ConfigureAwait(false), "Le personnage attend encore la fin de la Flèche Magique");
        evidence.Add("cible visible " + visible.CellID + " : clic → " + cast + " → " + Short(launched, 40));

        int mark = sv.ReceivedCount;
        var passed = await fv.PassTurnAsync().ConfigureAwait(false);
        Check(passed.Sent, "Fin de tour refusée par le bot : " + passed.Message);
        await sv.Expect("GTF" + sv.Id, "GTF (fin du tour)", 10, mark).ConfigureAwait(false);
        await sv.Until(() => !fv.IsMyTurn, 5).ConfigureAwait(false);
        return evidence;
    }
}

// Scénario x9 (lot X9) : défis d'un combat contre des monstres (Gd), cible du défi montrée (Gdi → Gf), réussite et échec (GdOK / GdKO),
// bonus de challenges du résultat, dans la fenêtre de jeu de A.
internal static partial class Live
{
    /// <summary>
    /// Combats tentés au plus pour obtenir un défi qui désigne une cible : StarLoco tire les défis au hasard parmi ceux que le combat
    /// permet (<c>World.getChallengeFromConditions</c> puis <c>getRandomChallenge</c>), un ou deux par combat. Pour un personnage seul
    /// contre plusieurs monstres, environ un défi permis sur quatre désigne une cible (3, 4, 10, 25, 32, 35 avec une cible choisie au
    /// début), soit un sur cinq : dix combats en donnent un dans près de neuf passages sur dix. Une fois la réussite et l'échec vus, ou
    /// après trois combats, A abandonne aussitôt chaque combat sans défi à cible.
    /// </summary>
    private const int ChallengeAttempts = 10;

    private sealed class ChallengeFightReport
    {
        public bool Target, Succeeded, Failed, Natural;
    }

    /// <summary><c>Gd&lt;type&gt;;…</c> : défi ajouté (le client aiguille sur le quatrième caractère, <c>K</c> et <c>O</c> sont des mises à jour).</summary>
    private static bool IsChallengeAdd(string packet) => packet.StartsWith("Gd", StringComparison.Ordinal) && packet.Length > 2 && char.IsDigit(packet[2]);

    private static bool IsChallengeUpdate(string packet) =>
        packet.StartsWith("GdOK", StringComparison.Ordinal) || packet.StartsWith("GdKO", StringComparison.Ordinal);

    /// <summary>
    /// A attaque le groupe de monstres le plus faible de sa carte ou d'une voisine (de préférence un groupe de deux monstres ou plus, seul
    /// cas où StarLoco tire les défis à cible). Après <c>GS</c>, chaque <c>Gd</c> de StarLoco (<c>Challenge.parseToPacket</c>) doit se
    /// retrouver dans <see cref="Fights.Challenges"/> (type, cible, bonus d'xp et de butin, nom et icône de <c>fightChallenge.xml</c>) et
    /// dans la colonne de la fenêtre de A (icônes exportées, infobulles). Si un défi désigne une cible, le bouton « Voir la cible » du
    /// détail envoie <c>Gdi&lt;type&gt;</c> et StarLoco répond <c>Gf&lt;cible&gt;|&lt;cellule&gt;</c> (<c>showCibleToPerso</c>), dessiné en
    /// drapeau ; sinon un autre combat est tenté (au plus <see cref="ChallengeAttempts"/>). A joue ses tours jusqu'à la fin du combat :
    /// StarLoco envoie <c>GdKO</c> au premier manquement et <c>GdOK</c> pour chaque défi encore en cours à la fin (<c>Challenge.fightEnd</c>,
    /// défaite comprise), chacun lu par le bot et écrit dans le chat ; le résultat <c>GE</c> montre « Bonus de challenges » et les icônes.
    /// </summary>
    private static async Task<string> ChallengeFightsAsync()
    {
        RequireMaps();
        if (window?.Map == null || await OnUi(() => window.Challenges == null).ConfigureAwait(false)) throw new SkipScenario("pas de fenêtre de jeu pour A");
        var evidence = new List<string>();
        var total = new ChallengeFightReport();
        int attempt = 0;
        while (attempt < ChallengeAttempts && !total.Target)
        {
            attempt++;
            if (attempt > 1)
            {
                string prepared = await PrepareAsync().ConfigureAwait(false);
                evidence.Add("combat " + attempt + " : " + (prepared == null ? "A prêt" : "remise en état " + prepared));
            }
            // Au-delà de trois combats, seul le défi à cible est encore cherché (délai du scénario).
            bool targetOnly = (total.Succeeded && total.Failed) || attempt > 3;
            ChallengeFightReport report = await ChallengeFightAsync(attempt, evidence, targetOnly).ConfigureAwait(false);
            total.Target |= report.Target; total.Succeeded |= report.Succeeded; total.Failed |= report.Failed; total.Natural |= report.Natural;
        }
        if (total.Natural) Check(total.Succeeded || total.Failed, "Aucun GdOK ni GdKO reçu avant GE alors qu'un combat s'est terminé sans abandon");
        evidence.Add("bilan en " + attempt + " combat(s) : cible " + (total.Target ? "montrée (Gdi → Gf, drapeau)" : "jamais tirée par StarLoco, Gdi non rejoué")
            + ", réussite " + (total.Succeeded ? "vue (GdOK)" : "non vue") + ", échec " + (total.Failed ? "vu (GdKO)" : "non vu (tirage et combat)"));
        return string.Join(" ; ", evidence);
    }

    /// <summary>Carte de A ou voisine avec un groupe de monstres (comme le scénario f) ; renvoie le déclencheur du retour, ou null.</summary>
    private static async Task<Trigger> MonstersMapAsync(int home)
    {
        Trigger back = null;
        var visited = new List<string>();
        if (a.Map.MonsterGroups.Count == 0)
            foreach (Trigger go in Triggers.ForMap(home).Values.Where(t => t.IsTeleport && t.TargetMapId != home && Map.AllBotMaps.ContainsKey(t.TargetMapId)).OrderBy(t => t.CellId))
            {
                Trigger reverse = Triggers.ForMap(go.TargetMapId).Values.FirstOrDefault(t => t.IsTeleport && t.TargetMapId == home);
                if (reverse == null) continue;
                await WalkThroughTrigger(a, go).ConfigureAwait(false);
                await a.Until(() => a.Map.MonsterGroups.Count > 0, 3).ConfigureAwait(false);
                visited.Add(go.TargetMapId + " (" + a.Map.MonsterGroups.Count + " groupe(s))");
                if (a.Map.MonsterGroups.Count > 0) { back = reverse; break; }
                await WalkThroughTrigger(a, reverse).ConfigureAwait(false);
            }
        if (a.Map.MonsterGroups.Count == 0) throw new SkipScenario("aucun groupe de monstres sur la carte " + home + " ni sur ses voisines (" + string.Join(", ", visited) + ")");
        return back;
    }

    /// <summary>Un combat contre des monstres avec ses défis : voir <see cref="ChallengeFightsAsync"/>.</summary>
    /// <param name="targetOnly">Réussite et échec déjà vus, ou trois combats déjà tentés : sans défi à cible, A abandonne aussitôt pour un
    /// nouveau tirage.</param>
    private static async Task<ChallengeFightReport> ChallengeFightAsync(int attempt, List<string> evidence, bool targetOnly)
    {
        var report = new ChallengeFightReport();
        Fights fight = a.Account.Game.Fight;
        Outil_Azur_complet.Bot.Controls.Fight.ChallengePanel column = await OnUi(() => window.Challenges).ConfigureAwait(false);
        int home = a.MapId;
        Trigger back = await MonstersMapAsync(home).ConfigureAwait(false);
        int mark = a.ReceivedCount;
        string gjk = null, group = null;
        for (int tries = 0; tries < 4 && gjk == null; tries++)
        {
            MonsterGroupActor target = a.Map.MonsterGroups.OrderBy(g => g.Members.Count >= 2 ? 0 : 1).ThenBy(g => g.TotalLevel).First();
            group = "carte " + a.MapId + ", groupe niveau " + target.TotalLevel + " (" + target.Members.Count + " monstre(s))";
            int before = a.ReceivedCount;
            var attack = await a.Account.Game.Interactions.MapActions.AttackGroupAsync(target.Id, async cell =>
            {
                var moved = await a.Account.Game.Manager.Mouvements.MoveToAsync(a.Map.GetCellFromId(cell)).ConfigureAwait(false);
                Journal("A [test] marche vers le groupe : " + moved);
            }).ConfigureAwait(false);
            Check(attack.Sent, "Attaque refusée par le bot : " + attack.Message);
            try { gjk = await a.Expect("GJK", "GJK (combat lancé)", 15, before).ConfigureAwait(false); }
            catch (TimeoutException) { Journal("A [test] le groupe a bougé, nouvel essai"); await Task.Delay(500).ConfigureAwait(false); }
        }
        Check(gjk != null, "Aucun combat lancé contre les monstres");
        await PlaceAndReady(a, mark, false).ConfigureAwait(false);
        await a.Expect("GS", "GS (début du combat)", 30, mark).ConfigureAwait(false);

        // Gd : un ou deux défis juste après GS (Fight.startFight, avant le premier tour).
        await a.Expect(IsChallengeAdd, "Gd (défi du combat)", 15, mark).ConfigureAwait(false);
        await Task.Delay(500).ConfigureAwait(false);
        string[] adds = a.ReceivedSince(mark).Where(IsChallengeAdd).ToArray();
        int[] ids = adds.Select(p => int.Parse(p.Substring(2).Split(';')[0], CultureInfo.InvariantCulture)).Distinct().ToArray();
        Check(await a.Until(() => fight.Challenges.Select(c => c.Id).SequenceEqual(ids), 5).ConfigureAwait(false),
            "Défis du bot " + string.Join(",", fight.Challenges.Select(c => c.Id)) + " au lieu de " + string.Join(" | ", adds));
        bool texts = LangData.IsLoaded("fightChallenge");
        var described = new List<string>();
        foreach (string raw in adds)
        {
            string[] f = raw.Substring(2).Split(';');
            int id = int.Parse(f[0], CultureInfo.InvariantCulture);
            FightChallenge c = fight.Challenges.First(x => x.Id == id);
            int? targetId = int.TryParse(f[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int t) && t != 0 ? t : (int?)null;
            Check(c.ShowTarget == (f[1] == "1") && c.TargetId == targetId && c.BasicXpBonus == int.Parse(f[3], CultureInfo.InvariantCulture)
                && c.BasicDropBonus == int.Parse(f[5], CultureInfo.InvariantCulture), "Défi " + raw + " mal lu : cible " + c.TargetId + ", xp " + c.XpBonus + ", butin " + c.DropBonus);
            if (texts && LangData.Challenge.Has(id))
                Check(c.Name == LangData.Challenge.Name(id) && c.Gfx == LangData.Challenge.Gfx(id) && !c.Description.Contains("%1"),
                    "Textes du défi " + id + " : « " + c.Name + " », « " + c.Description + " », icône " + c.Gfx);
            described.Add(Short(raw, 40) + " → « " + c.Name + " »" + (c.TargetId.HasValue ? " (" + c.Description + ")" : "") + ", XP +" + c.XpBonus + " %, butin +" + c.DropBonus + " %");
        }
        evidence.Add("combat " + attempt + ", " + group + " : " + string.Join(" ; ", described));
        // Challenge.fightStart de StarLoco montre déjà la cible à l'équipe (showCibleToFight : Gf avant Gd).
        string early = a.ReceivedSince(mark).TakeWhile(p => !IsChallengeAdd(p)).LastOrDefault(p => p.StartsWith("Gf", StringComparison.Ordinal));
        if (early != null) evidence.Add(early + " reçu avant Gd (cible montrée à l'équipe au début du combat)");

        // Colonne de la fenêtre de A : une icône exportée par défi, infobulle du client.
        string shown = await WaitOnUi(() => column.Visible && column.Icons.Count == ids.Length
            && column.Icons.All(icon => icon.Challenge != null && (icon.HasIcon || !icon.Challenge.Gfx.HasValue))
            && column.Icons.All(icon => column.TooltipOf(icon).StartsWith(icon.Challenge.Name + "\n", StringComparison.Ordinal))
                ? string.Join(", ", column.Icons.Select(icon => icon.Challenge.Id + (icon.HasIcon ? " (icône Challenges/" + icon.Challenge.Gfx + ".png)" : " (sans icône)"))) : null, 10).ConfigureAwait(false);
        Check(shown != null, "Colonne des défis de la fenêtre de A : visible " + await OnUi(() => column.Visible).ConfigureAwait(false) + ", "
            + await OnUi(() => column.Icons.Count).ConfigureAwait(false) + " icône(s) pour " + ids.Length + " défi(s)");
        evidence.Add("colonne de la fenêtre : " + shown);

        // Détail et « Voir la cible » : Gdi<type> → Gf<cible>|<cellule> de StarLoco, dessiné en drapeau.
        FightChallenge aimed = fight.Challenges.FirstOrDefault(c => c.ShowTarget && c.TargetId.HasValue);
        FightChallenge opened = aimed ?? fight.Challenges.First();
        string viewer = await OnUi(() => { column.OpenViewer(opened); return column.ViewerOpen ? column.Viewer.NameText + " / " + column.Viewer.StateText : null; }).ConfigureAwait(false);
        Check(viewer != null && viewer.StartsWith(opened.Name, StringComparison.Ordinal), "Détail du défi " + opened.Id + " non ouvert : " + viewer);
        Check(await OnUi(() => column.Viewer.ViewButton.Visible).ConfigureAwait(false) == opened.ShowTarget, "Bouton « Voir la cible » du défi " + opened.Id + " : affiché " + !opened.ShowTarget);
        if (attempt == 1 || aimed != null) await Capture(aimed != null ? "25-reel-x9-defi-cible" : "25-reel-x9-defis").ConfigureAwait(false);
        if (aimed != null)
        {
            int sentMark = a.SentCount, flagMark = a.ReceivedCount, targetId = aimed.TargetId.Value;
            DateTime clicked = DateTime.UtcNow;
            await OnUi(() => { column.Viewer.ViewButton.PerformClick(); return true; }).ConfigureAwait(false);
            string ask = "Gdi" + aimed.Id.ToString(CultureInfo.InvariantCulture);
            Check(await a.Until(() => a.SentSince(sentMark).Contains(ask), 5).ConfigureAwait(false), "« Voir la cible » n'a pas envoyé " + ask + " : " + string.Join(" | ", a.SentSince(sentMark)));
            string gf = await a.Expect("Gf" + targetId.ToString(CultureInfo.InvariantCulture) + "|", "Gf (cible du défi)", 10, flagMark).ConfigureAwait(false);
            int cell = int.Parse(gf.Split('|')[1], CultureInfo.InvariantCulture);
            Check(fight.Fighters.TryGetValue(targetId, out CombatFighter aimedFighter) && aimedFighter.CellId == cell, "Gf désigne la cellule " + cell + ", le bot place la cible en " + aimedFighter?.CellId);
            // Le drapeau doit venir de la réponse à Gdi, pas du Gf reçu au début du combat.
            Check(await WaitOnUi(() => window.Map.DisplayedFlag != null && window.Map.DisplayedFlag.ActorId == targetId && window.Map.DisplayedFlag.CellId == cell
                && window.Map.DisplayedFlag.ReceivedUtc >= clicked && !column.ViewerOpen ? "drapeau" : null, 10).ConfigureAwait(false) != null,
                "Drapeau de la cible (réponse à " + ask + ") absent de la carte de A, ou détail resté ouvert");
            evidence.Add("« Voir la cible » du défi " + aimed.Id + " : " + ask + " → " + gf + " (" + aimedFighter.Name + "), drapeau dessiné, détail fermé");
            report.Target = true;
        }
        else
        {
            await OnUi(() => { column.Viewer.CloseButton.PerformClick(); return true; }).ConfigureAwait(false);
            Check(await WaitOnUi(() => column.ViewerOpen ? null : "fermé", 5).ConfigureAwait(false) != null, "Détail du défi resté ouvert après « × »");
            evidence.Add("aucun défi à cible dans ce combat : détail de « " + opened.Name + " » sans « Voir la cible », fermé par « × »");
        }

        // Tours de A jusqu'à la fin du combat (abandon au-delà de 150 s, ou tout de suite s'il ne reste que la cible à voir).
        DateTime until = DateTime.UtcNow.AddSeconds(targetOnly && !report.Target ? 0 : 150);
        int turns = 0;
        var played = new List<string>();
        while (DateTime.UtcNow < until && fight.IsInFight)
        {
            int actor;
            try { actor = await WaitForTurnOf(new[] { a }, 60).ConfigureAwait(false); }
            catch (TimeoutException) { break; }
            if (actor == 0) break;
            turns++;
            await PlayTurn(a, true, true, played).ConfigureAwait(false);
        }
        bool gaveUp = fight.IsInFight;
        if (gaveUp) await fight.GiveUpAsync().ConfigureAwait(false);
        string ge = await a.Expect("GE", "GE (résultat du combat)", 30, mark).ConfigureAwait(false);
        Check(await a.Until(() => fight.LastResult != null, 10).ConfigureAwait(false), "Le bot n'a pas lu le résultat GE");
        FightResult result = fight.LastResult;
        report.Natural = !gaveUp;

        // GdOK / GdKO : état du défi dans le résultat et ligne du chat de la fenêtre de A.
        string[] updates = a.ReceivedSince(mark).Where(IsChallengeUpdate).ToArray();
        Check(result.Challenges.Select(c => c.Id).SequenceEqual(ids), "Défis du résultat " + string.Join(",", result.Challenges.Select(c => c.Id)) + " au lieu de " + string.Join(",", ids));
        var lines = new List<string>();
        foreach (FightChallenge c in result.Challenges)
        {
            string last = updates.LastOrDefault(p => p.Substring(4) == c.Id.ToString(CultureInfo.InvariantCulture));
            FightChallengeState expected = last == null ? FightChallengeState.Running : last.StartsWith("GdOK", StringComparison.Ordinal) ? FightChallengeState.Succeeded : FightChallengeState.Failed;
            Check(c.State == expected, "Défi " + c.Id + " : état " + c.State + " dans le résultat, attendu " + expected + " (" + (last ?? "aucune mise à jour") + ")");
            if (last == null) continue;
            string line = c.ResultMessage;
            Check(await WaitOnUi(() => window.Chat.View.DisplayedLines.Any(l => l.Filter == ChatFilter.Infos && l.Text == line) ? "chat" : null, 10).ConfigureAwait(false) != null,
                "Ligne « " + line + " » absente du chat de A");
            lines.Add(last + " → « " + line + " »");
            report.Succeeded |= expected == FightChallengeState.Succeeded;
            report.Failed |= expected == FightChallengeState.Failed;
        }
        if (report.Failed)
            Check(a.ReceivedSince(mark).Any(p => p.StartsWith("Im0188", StringComparison.Ordinal)), "GdKO sans le message Im0188 de StarLoco");

        // Résultat : « Bonus de challenges : » suivi des icônes 17 × 17 et du résumé ; colonne du combat cachée.
        string bonus = await WaitOnUi(() => window.Panels.Current is Outil_Azur_complet.Bot.Panels.FightResultPanel panel && ReferenceEquals(panel.Result, result)
            && panel.ChallengeIcons.Count == ids.Length && result.Challenges.All(c => panel.ChallengesText.Contains(c.Name)) ? panel.ChallengesText : null, 10).ConfigureAwait(false);
        Check(bonus != null, "Volet du résultat sans les défis");
        Check(await WaitOnUi(() => column.Visible || column.Icons.Count > 0 ? null : "cachée", 10).ConfigureAwait(false) != null, "Colonne des défis restée après GE");
        if (attempt == 1 || report.Target) await Capture("26-reel-x9-resultat").ConfigureAwait(false);
        await OnUi(() => { if (window.Panels.Current is Outil_Azur_complet.Bot.Panels.FightResultPanel panel) ((Button)panel.CloseButton).PerformClick(); return true; }).ConfigureAwait(false);
        // Après GQ, StarLoco saute qui a quitté (hasLeft), sauf si son départ termine le combat (dernier de l'équipe : onFighterDie).
        evidence.Add(turns + " tour(s)" + (gaveUp ? ", abandon (GQ)" : "") + ", " + Short(ge, 40)
            + (lines.Count == 0 ? " ; aucun GdOK/GdKO" : " ; " + string.Join(" ; ", lines)) + " ; résultat : « " + bonus + " »");

        await WaitBackOnMap(a).ConfigureAwait(false);
        string returned = await ReturnAfterFightAsync(a, home, back).ConfigureAwait(false);
        if (returned != null) evidence.Add(returned);
        return report;
    }
}
