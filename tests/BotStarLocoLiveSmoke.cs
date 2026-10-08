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
        while (!scenarios.IsCompleted)
        {
            while (uiQueue.TryDequeue(out Action action)) action();
            Application.DoEvents();
            Thread.Sleep(10);
        }
        while (uiQueue.TryDequeue(out Action last)) last();
        try { scenarios.GetAwaiter().GetResult(); }
        catch (Exception error) { results.Add(new ScenarioResult { Name = "déroulement", Status = "échec", Evidence = error.GetType().Name + " : " + error.Message }); Journal("!! " + error); }
        finally
        {
            if (window != null && !window.IsDisposed) { window.Close(); Application.DoEvents(); window.Dispose(); }
            a.Dispose(); b.Dispose();
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
    private static async Task Capture(string name)
    {
        if (string.IsNullOrWhiteSpace(captures)) return;
        // La fenêtre se construit sur le fil d'interface après ASK : l'attendre avant la première capture.
        DateTime until = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < until && !await OnUi(() => window != null && window.IsHandleCreated && window.Visible).ConfigureAwait(false))
            await Task.Delay(100).ConfigureAwait(false);
        if (window == null) { Say("capture impossible : fenêtre de jeu absente"); return; }
        Directory.CreateDirectory(captures);
        await Task.Delay(1200).ConfigureAwait(false);
        Rectangle area = await OnUi(() => window.RectangleToScreen(window.ClientRectangle)).ConfigureAwait(false);
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

    /// <summary>Joue un scénario avec un délai maximal ; une exception devient un échec avec sa raison.</summary>
    private static async Task<bool> Scenario(string name, int seconds, Func<Task<string>> body)
    {
        Say("---- scénario " + name + " (délai " + seconds + " s)");
        Task<string> run = body();
        Task finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(seconds))).ConfigureAwait(false);
        if (finished != run) { Record(name, "échec", "délai de " + seconds + " s dépassé"); await RecoverAsync().ConfigureAwait(false); return false; }
        try { Record(name, "ok", await run.ConfigureAwait(false)); return true; }
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
        await Scenario("b. même carte, déplacement, changement de carte et retour", 150, SameMapAsync).ConfigureAwait(false);
        await Scenario("c. chat général et message privé", 40, ChatAsync).ConfigureAwait(false);
        await Scenario("d1. invitation de groupe acceptée", 40, PartyAsync).ConfigureAwait(false);
        await Scenario("d2. échange de kamas validé des deux côtés", 60, ExchangeAsync).ConfigureAwait(false);
        await Scenario("d3. ajout en ami", 40, FriendAsync).ConfigureAwait(false);
        await Scenario("e. défi accepté : placement, tours, déplacement, sort, abandon, résultat", 240, DuelAsync).ConfigureAwait(false);
        await Scenario("f. combat contre un groupe de monstres", 300, MonstersAsync).ConfigureAwait(false);
        await Scenario("g1. dialogue avec un PNJ", 40, NpcDialogAsync).ConfigureAwait(false);
        await Scenario("g2. boutique d'un PNJ : achat", 60, NpcShopAsync).ConfigureAwait(false);
        await Scenario("g3. zaap", 90, ZaapAsync).ConfigureAwait(false);
    }
}

// Scénarios a à d : connexion et personnages, carte partagée et déplacements, chat, groupe, échange, amis.
internal static partial class Live
{
    private const string TestPrefix = "Essai-";
    private static readonly Random random = new Random();
    private static int startMap;
    private static readonly SemaphoreSlim creationGate = new SemaphoreSlim(1, 1);

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

        // Sélection comme SelectPlayerPerso.PlaySelected : AS<id> ; le bot répond GC1 à ASK.
        int selected = s.ReceivedCount;
        await s.Send("AS" + id).ConfigureAwait(false);
        string ask = await s.Expect(p => p.StartsWith("ASK") || p.StartsWith("ASE"), "ASK (personnage sélectionné)", 15, selected).ConfigureAwait(false);
        Check(ask.StartsWith("ASK|" + id + "|" + s.CharacterName + "|"), "Sélection refusée : " + ask);
        string gck = await s.Expect("GCK", "GCK (partie créée)", 15, selected).ConfigureAwait(false);
        string stats = await s.Expect("As", "As (caractéristiques)", 15, selected).ConfigureAwait(false);
        string gdm = await s.Expect("GDM", "GDM (carte)", 15, selected).ConfigureAwait(false);
        string gm = await s.Expect(p => p.StartsWith("GM|") && p.Contains(";" + id + ";" + s.CharacterName + ";"), "GM du personnage sur sa carte", 15, selected).ConfigureAwait(false);
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
        await b.Expect(p => p.StartsWith("EK1" + a.Id) || p.StartsWith("EK1"), "EK1 (A a validé, vu par B)", 10, markB).ConfigureAwait(false);
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
        Check(await a.Until(() => friends.Friends.Any(f => f.Name == b.CharacterName || f.Account == b.Login || (f.Account ?? "").Length > 0), 5).ConfigureAwait(false),
            "La liste d'amis du bot ne contient pas B : " + string.Join(", ", friends.Friends.Select(f => f.Name + "/" + f.Account)));
        string entry = string.Join(", ", friends.Friends.Select(f => f.Name + (f.IsOnline ? " (en ligne)" : "")));
        // Nettoyage : retrait de l'ami (FD), pour que le passage suivant rejoue l'ajout.
        FriendEntry added = friends.Friends.FirstOrDefault();
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

    /// <summary>Compare vie, PA et PM du bot aux derniers GTM (fin du tour précédent) ; renvoie le constat.</summary>
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
            lines.Add(fighter.Name + " " + gtm[0] + " PV/" + gtm[1] + " PA/" + gtm[2] + " PM");
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
            if (other > 0)
            {
                int before = s.ReceivedCount;
                var placed = await fight.PlaceAsync(other).ConfigureAwait(false);
                Check(placed.Sent, s.Label + " : placement refusé par le bot : " + placed.Message);
                await s.Expect(p => p.StartsWith("GIC|") && p.Contains(s.Id + ";" + other), "GIC (nouvelle cellule de placement)", 10, before).ConfigureAwait(false);
                Check(await s.Until(() => Self(s)?.CellId == other, 5).ConfigureAwait(false), s.Label + " : le bot n'a pas déplacé son combattant en placement");
            }
        }
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
                string pa = await s.Expect(p => p.StartsWith("GA;102;" + s.Id + ";"), "GA;102 (PA utilisés)", 10, castMark).ConfigureAwait(false);
                string damage = null;
                if (!launched.StartsWith("GA;302"))
                    damage = await s.Expect(p => p.StartsWith("GA;100;") && p.Contains(";" + target.Id + ","), "GA;100 (dégâts sur la cible)", 10, castMark).ConfigureAwait(false);
                await s.Until(() => !fight.IsActionPending, 5).ConfigureAwait(false);
                int used = -int.Parse(pa.Split(';')[3].Split(',')[1], CultureInfo.InvariantCulture);
                Check(await s.Until(() => fight.ActionPoints == paStart - used, 5).ConfigureAwait(false), s.Label + " : PA du bot " + fight.ActionPoints + ", attendu " + (paStart - used));
                if (damage != null)
                {
                    int delta = int.Parse(damage.Split(';')[3].Split(',')[1], CultureInfo.InvariantCulture);
                    // Fights.Fighters renvoie des copies : relire le combattant à chaque essai.
                    Check(await s.Until(() => LifeOf(s, target.Id) == lifeBefore + delta, 5).ConfigureAwait(false), s.Label + " : vie de la cible " + LifeOf(s, target.Id) + ", attendu " + (lifeBefore + delta));
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
        if (a.MapId != home && back != null && a.MapId == back.MapId) await WalkThroughTrigger(a, back).ConfigureAwait(false);
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
