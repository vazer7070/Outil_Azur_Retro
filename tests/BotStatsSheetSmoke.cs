using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot;
using Outil_Azur_complet.Bot.Controls;
using Outil_Azur_complet.Bot.Controls.Banner;
using Outil_Azur_complet.Bot.Panels;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Perso;
using Tool_BotProtocol.Game.Perso.Spells;
using Tool_BotProtocol.Game.Perso.Stats;

// Fiches caractéristiques et sorts (lot F13a) selon Account.onStats, StatsJob, Spells, SpellFullInfosViewer, SpellForget et
// dofus.aks.Spells (client 1.34), Player.getAsPacket / boostStat / boostSpell / forgetSpell et GameClient de StarLoco, sur un
// serveur fictif local avec des textes du client synthétiques (lang, classes, spells, effects) : As à 51 champs (PR #10 : champs
// 19 à 50 lus, cinquième valeur, alignement « a~b »), As illisibles ignorés sans exception (fiche intacte), coût du capital selon
// classes.xml (paliers b10…b15, Sacrieur 1 pour 2) et repli sans fichier, AB<code> envoyé sans attente et refusé hors connexion, en
// combat ou sans capital ; SLo± séparé de SL (PR #15), SB<effet;sort;valeur>, SB<id> → SUK + As / SUE (refus locaux : niveau
// maximal, points, niveau requis), SF+/SF- et SF<id>/SF-1 seulement pendant la fenêtre d'oubli ; puis les volets Caractéristiques
// (valeurs « base (+x) », boutons « + », onglet avancé, alignement) et Sorts (filtre, icônes lues hors du fil d'interface, fiche par
// niveau, Améliorer, Oublier grisé hors fenêtre d'oubli puis confirmé, Annuler, fermeture → SF-1, glisser vers la barre → SM).
internal static class BotStatsSheetSmoke
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
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
            Console.WriteLine("OK: As 51 champs (19 à 50, 5e valeur, a~b), As illisibles ignorés, coût classes.xml et repli, AB sans délai et refus, SLo séparé de SL, SB modificateurs, SB → SUK/SUE et refus locaux, SF+/SF-/SF<id>/SF-1, volets Caractéristiques et Sorts (Oublier grisé hors SF+, confirmation, glisser → SM)");
        }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
        finally { LangData.Clear(); }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static object Get(object target, string field) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static IEnumerable<Control> All(Control value) { yield return value; foreach (Control child in value.Controls) foreach (var nested in All(child)) yield return nested; }
    private static void PumpUntil(Func<bool> done, int seconds = 6, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(seconds);
        while (!done()) { if (DateTime.UtcNow > end) throw new TimeoutException("Stats sheet loopback timed out (line " + line + ")"); Application.DoEvents(); Thread.Sleep(5); }
        Application.DoEvents();
    }
    private static void Complete(Task task) { PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static T Result<T>(Task<T> task) { Complete(task); return task.Result; }
    private static void Feed(Accounts account, string packet) { Complete(MessagesReception.ReceptionAsync(account.Connexion, packet)); }
    /// <summary>Paquet traité sur un fil du pool, comme la boucle de réception réseau.</summary>
    private static void FeedFromNetwork(Accounts account, string packet) { Complete(Task.Run(() => MessagesReception.ReceptionAsync(account.Connexion, packet))); }
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
        Check(socket.Available == 0, message + " (reçu « " + (socket.Available > 0 ? Read(socket) : string.Empty) + " »)");
    }
    private static void Click(object button) => ((Button)button).PerformClick();
    private static void Answer(Form dialog, string choice) => All(dialog).OfType<Button>().First(button => button.Text == choice).PerformClick();
    private static string DialogMessage(Form dialog) => All(dialog).FirstOrDefault(control => control.Name == "dialog-message")?.Text ?? string.Empty;
    private static void WriteLang(string folder, string file, string family, string body) =>
        File.WriteAllText(Path.Combine(folder, file), "<?xml version='1.0' encoding='utf-8'?>\n<BotLang famille=\"" + family + "\" langue=\"fr\" version=\"1\" source=\"" + family
            + "_fr_1.swf\">\n" + body + "\n</BotLang>\n", new UTF8Encoding(false));
    private static void Png(string folder, string name, Color color, int width, int height)
    {
        Directory.CreateDirectory(folder);
        using (var image = new Bitmap(width, height)) { using (var graphics = Graphics.FromImage(image)) graphics.Clear(color); image.Save(Path.Combine(folder, name + ".png"), ImageFormat.Png); }
    }

    private static Type Assets => typeof(GameClientFullform).Assembly.GetType("Outil_Azur_complet.Bot.ClientAssets");
    private static bool Cached(string family, string name)
    {
        var args = new object[] { family, name, null };
        return (bool)Assets.GetMethod("TryCached", Any).Invoke(null, args) && args[2] != null;
    }

    /// <summary>
    /// Paquet As au format de Player.getAsPacket : en-tête puis, pour le champ i (9 à 50), base i, équipement i+100, dons i+200,
    /// boost i+300 et, pour PA, PM et les champs 29 à 50, une cinquième valeur i+400 ; « | » final comme StarLoco.
    /// </summary>
    private static string FullAs(int capital, int spellPoints, string alignment = "1~1,40,3,750,0,1", int life = 300)
    {
        var packet = new StringBuilder("As5000,4000,6000|1234|" + capital + "|" + spellPoints + "|" + alignment + "|" + life + ",350|8000,10000|120|130|");
        for (int i = 9; i <= 50; i++)
        {
            packet.Append(i).Append(',').Append(i + 100).Append(',').Append(i + 200).Append(',').Append(i + 300);
            if (i <= 10 || i >= 29) packet.Append(',').Append(i + 400);
            packet.Append('|');
        }
        return packet.ToString();
    }

    private static string Level(int level, int minPlayerLevel, int type = 0) =>
        "[[[100," + level + "," + (level + 4) + ",null,0,0,\"1d5+" + level + "\"],[108,5,null,null,3,25,\"0d0+5\"]],[[100," + (level + 5) + "," + (level + 9)
        + ",null,0,0,\"\"]],4,1," + (level + 2) + ",50,100,false,true,false,true," + type + ",2,1,0,\"PaPa\",[],[],"
        + minPlayerLevel + ",true]";

    private static string Sort(int id, string name, string description, int type, params int[] minLevels)
    {
        var attributes = new StringBuilder("<sort id=\"" + id + "\" nom=\"" + name + "\" description=\"" + description + "\"");
        for (int index = 0; index < minLevels.Length; index++)
            attributes.Append(" niveau" + (index + 1) + "=\"" + SecurityElement.Escape(Level(index + 1, minLevels[index], type)) + "\"");
        return attributes.Append(" />").ToString();
    }

    private static void Run()
    {
        string previous = Environment.CurrentDirectory;
        string folder = Path.Combine(TestPaths.Work, "bot-stats-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); Environment.CurrentDirectory = folder;
        try
        {
            Fallbacks();
            string lang = Path.Combine(folder, "lang"); Directory.CreateDirectory(lang);
            WriteLang(lang, "lang.xml", "lang",
                "<texte cle=\"CANT_BOOST_SPELL\" valeur=\"Amélioration impossible (test)\" />\n<texte cle=\"LEVEL_NEED_TO_BOOST\" valeur=\"Niveau %1 requis (test)\" />\n"
                + "<texte cle=\"SPELL_FORGET\" valeur=\"Oubli (test)\" />\n<texte cle=\"SPELL_FORGET_CONFIRM\" valeur=\"Oublier %1 ? (test)\" />\n"
                + "<texte cle=\"IN_CASE_PERCENT\" valeur=\"Dans %1% des cas (test)\" />\n<texte cle=\"TURNS\" valeur=\"tours\" />\n<texte cle=\"TURN\" valeur=\"tour\" />\n"
                + "<texte cle=\"COST\" valeur=\"Coût\" />\n<texte cle=\"POUR\" valeur=\"pour\" />\n<texte cle=\"FULL_STATS_ID33\" valeur=\"Neutre JcJ fixe (test)\" />\n"
                + "<texte cle=\"FULL_STATS_CAT3\" valeur=\"Résistances JcJ (test)\" />\n<texte cle=\"NEED_ALIGNMENT\" valeur=\"Alignement requis (test)\" />");
            WriteLang(lang, "classes.xml", "classes",
                "<entree table=\"G\" id=\"8\" b10=\"[[0,1],[100,2],[200,3]]\" b11=\"[[0,1]]\" b12=\"[[0,3]]\" b13=\"[[0,1],[20,2]]\" b14=\"[[0,1],[20,2],[40,3]]\" b15=\"[[0,1],[20,2]]\" s=\"3,17,18\" sn=\"Classe fictive\" />\n"
                + "<entree table=\"G\" id=\"11\" b10=\"[[0,3],[100,4]]\" b11=\"[[0,1,2]]\" b12=\"[[0,3]]\" b13=\"[[0,3]]\" b14=\"[[0,3]]\" b15=\"[[0,3]]\" s=\"\" sn=\"Autre classe\" />");
            WriteLang(lang, "spells.xml", "spells",
                Sort(3, "Frappe fictive", "Frappe de test.", 0, 1, 1, 1, 30, 30)
                + "\n" + Sort(17, "Glyphe fictif", "Glyphe de test.", 1, 1, 1)
                + "\n" + Sort(18, "Sort non appris", "Sort de classe non appris.", 0, 1)
                + "\n" + Sort(19, "Sort maximal", "Sort à deux niveaux.", 0, 1, 1));
            WriteLang(lang, "effects.xml", "effects",
                "<entree table=\"E\" id=\"100\" d=\"Dommages : #1{~1~2 à }#2 (test)\" />\n<entree table=\"E\" id=\"108\" d=\"Soins : #1{~1~2 à }#2 (test)\" />\n"
                + "<entree table=\"E\" id=\"283\" d=\"+#3 de dommages sur le sort #1 (test)\" />");
            Check(LangData.Load(lang) == 4, "Synthetic lang files not loaded: " + string.Join(" / ", LangData.LoadWarnings));
            Model();

            MessagesReception.Init();
            foreach (string prefix in new[] { "As", "AB", "SL", "SLo", "SB", "SF", "SUK", "SUE" })
                Check(MessagesReception.messagesDatas.Count(data => data.MessageName == prefix) == (prefix == "AB" ? 0 : 1), "Handler registration differs for " + prefix);
            Network(folder);
        }
        finally { Environment.CurrentDirectory = previous; }
    }

    /// <summary>Lecture de As, coûts du capital et niveaux de sorts sans fichiers de langue (repli du bot).</summary>
    private static void Fallbacks()
    {
        LangData.Clear();
        Check(CharacterStats.TryParse(FullAs(2, 5), out CharacterStats stats, out string error) && error == null, "Full As rejected: " + error);
        BoostCost legacy = stats.BoostCost(8, BoostableStat.Force);
        Check(!legacy.FromLang && legacy.Cost == stats.GetCapitalStatsBoost(8, StatsEnum.FORCE) && legacy.Count == 1, "Fallback cost differs");
        BoostCost sacrifice = stats.BoostCost(11, BoostableStat.Vitalite);
        Check(!sacrifice.FromLang && sacrifice.Cost == 1 && sacrifice.Count == 2, "Fallback Sacrieur vitality differs");
        Check(SpellLevelInfo.Get(999, 1) == null && SpellLevelInfo.MaxLevel(999) == SpellBook.MaxSpellLevel && SpellBook.NameOf(999) == "Sort #999",
            "Unknown spell fallback differs");
    }

    private static void Model()
    {
        // As de StarLoco : 51 champs, « | » final, cinquième valeur pour PA, PM et 29 à 50.
        Check(CharacterStats.TryParse(FullAs(2, 5), out CharacterStats stats, out string error), "Full As rejected: " + error);
        Check(stats.FieldCount == 51 && stats.ActualEXP == 5000 && stats.MinExpNiv == 4000 && stats.ExpNivNext == 6000 && stats.Kamas == 1234 && stats.CapitalPoints == 2
            && stats.SpellPoints == 5 && stats.Alignement == 1 && stats.FakeAlignment == 1 && !stats.HasFakeAlignment && stats.AlignLVL == 40 && stats.GradeAli == 3
            && stats.Honor == 750 && stats.Dishonor == 0 && stats.HasWings && stats.VitalityActual == 300 && stats.MaxVitality == 350 && stats.ActualEnergy == 8000
            && stats.EnergyMax == 10000 && stats.Initiative.BasePerso == 120 && stats.Propec.BasePerso == 130, "As header differs");
        for (int i = 9; i <= 50; i++)
        {
            StatsBase field = stats.Get((AsField)i);
            int? extra = i <= 10 || i >= 29 ? i + 400 : (int?)null;
            Check(field.BasePerso == i && field.equipement == i + 100 && field.cadeau == i + 200 && field.Boost == i + 300 && field.Extra == extra && field.StatsTotal == 4 * i + 600,
                "As field " + i + " differs");
        }
        Check(ReferenceEquals(stats.PA, stats.Get(AsField.PA)) && stats.Force.BasePerso == 11 && stats.Vita.BasePerso == 12 && stats.Sagesse.BasePerso == 13 && stats.Chance.BasePerso == 14
            && stats.Agility.BasePerso == 15 && stats.Intell.BasePerso == 16 && stats.Atteignable.BasePerso == 17 && stats.Invoc.BasePerso == 18 && stats.PA.Displayed == 9 + 109 + 209
            && stats.Get(BoostableStat.Intelligence).BasePerso == 16 && StatsCodes.FieldOf(BoostableStat.Force) == AsField.Force, "Named characteristics differ");
        Check(stats.Get(AsField.ResistanceNeutrePvp).BasePerso == 33 && stats.Get(AsField.ResistanceFeuPourcentPvp).Boost == 350 && stats.Get(AsField.Dommages).equipement == 119,
            "Fields 19 to 50 (PR #10) were not read");

        // Alignement simulé, serveur plus ancien (10 champs), champs au-delà de 50 ignorés.
        Check(CharacterStats.TryParse("As1,0,10|0|0|0|2~1,5,1,10,2,0|10,20|100,10000|1|2|6,0,0,0|3,0,0,0", out CharacterStats old, out error)
            && old.FieldCount == 11 && old.Alignement == 2 && old.FakeAlignment == 1 && old.HasFakeAlignment && !old.HasWings && old.Dishonor == 2 && old.PA.BasePerso == 6
            && old.Get(AsField.Dommages).StatsTotal == 0, "Short As or fake alignment differs: " + error);
        Check(CharacterStats.TryParse(FullAs(0, 0) + "9,9,9,9|8,8,8,8|", out CharacterStats longer, out error) && longer.FieldCount == 51, "Extra fields after 50 were not ignored");

        // As illisibles : rejet sans exception, la fiche reste celle d'avant (int.Parse sans TryParse, PR #10).
        foreach (string broken in new[] { null, "", "Ax", "As", "As1,2|3", "Asx,0,0|0|0|0|0~0,0,0,0,0,0|1,1|1,1|1|1|", "As1,0,10|0|0|0|0~0,0,0,0,0,0|1,1|1,1|1|1|a,0,0,0|",
            "As1,0,10|0|0|0|0~0,0,0,0,0,0|1,1|1,1|1|1|1,2|", "As1,0,10|0|0|0|0,0|1,1|1,1|1|1|", "As1,0,10|99999999999|0|0|0~0,0,0,0,0,0|1,1|1,1|1|1|" })
            Check(!CharacterStats.TryParse(broken, out CharacterStats rejected, out error) && rejected == null && !string.IsNullOrEmpty(error), "Unreadable As accepted: " + broken);

        // Coût du capital comme Player.getBoostCostAndCountForCharacteristic (classes.xml).
        BoostCost force = stats.BoostCost(8, BoostableStat.Force);
        Check(force.FromLang && force.Cost == 1 && force.Count == 1, "Force cost differs");
        stats.Force.BasePerso = 150;
        Check(stats.BoostCost(8, BoostableStat.Force).Cost == 2, "Force tier 100 not applied");
        stats.Force.BasePerso = 250;
        Check(stats.BoostCost(8, BoostableStat.Force).Cost == 3, "Force tier 200 not applied");
        Check(stats.BoostCost(8, BoostableStat.Sagesse).Cost == 3 && stats.BoostCost(8, BoostableStat.Agilite).Cost == 1, "Wisdom or agility cost differs");
        BoostCost sacrifice = stats.BoostCost(11, BoostableStat.Vitalite);
        Check(sacrifice.FromLang && sacrifice.Cost == 1 && sacrifice.Count == 2, "Sacrieur vitality (1 for 2) differs");
        Check(!stats.BoostCost(12, BoostableStat.Force).FromLang, "Missing class did not use the fallback table");

        // Niveaux des sorts (spells.xml) et effets décrits avec effects.xml, comme Effect.description.
        SpellLevelInfo first = SpellLevelInfo.Get(3, 2);
        Check(first != null && first.FromLang && first.ApCost == 4 && first.RangeMin == 1 && first.RangeMax == 4 && first.CriticalHit == 50 && first.CriticalFailure == 100
            && !first.LineOnly && first.LineOfSight && !first.FreeCell && first.RangeBoostable && first.SpellType == 0 && first.PerTurn == 2 && first.PerTarget == 1
            && first.Delay == 0 && first.Zones == "PaPa" && first.MinPlayerLevel == 1 && first.FailureEndsTurn && first.Effects.Count == 2 && first.CriticalEffects.Count == 1,
            "Spell level 2 differs");
        Check(first.Effects[0].Describe() == "Dommages : 2 à 6 (test)" && first.Effects[0].Dice == "1d5+2"
            && first.Effects[1].Describe() == "Dans 25% des cas (test): Soins : 5 (test) (3 tours)" && first.CriticalEffects[0].Describe() == "Dommages : 7 à 11 (test)",
            "Spell effect texts differ: " + first.Effects[1].Describe());
        Check(SpellLevelInfo.MaxLevel(3) == 5 && SpellLevelInfo.MaxLevel(17) == 2 && SpellLevelInfo.Get(3, 4).MinPlayerLevel == 30 && SpellLevelInfo.Get(3, 6) == null
            && SpellLevelInfo.Get(17, 1).SpellType == 1 && SpellBook.NameOf(3) == "Frappe fictive" && SpellBook.DescriptionOf(3) == "Frappe de test.", "Spell levels differ");
    }

    private static void Network(string folder)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var logs = new ConcurrentQueue<string>();
        try
        {
            using (var account = new Accounts(new AccountConfig("synthetic-stats", "synthetic", "loopback")))
            {
                account.Logger.log_event += (entry, color) => logs.Enqueue(entry.message);
                Task<Socket> accept = listener.AcceptSocketAsync();
                Complete(account.Connexion.ConnectToServer(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port)); Complete(accept);
                using (Socket peer = accept.Result)
                {
                    peer.ReceiveTimeout = 6000;
                    CharacterClass character = account.Game.character;
                    character.SetPerso_Data(42, "Personnage de test", 25, 0, 8);
                    account.AccountStates = AccountStates.CONNECTED_INACTIVE;
                    Stats(account, peer, logs);
                    Spells(account, peer, logs);
                    Windows(account, peer, folder);
                    NoPacket(peer, "Unexpected packet at the end of the test");
                }
            }
        }
        finally { listener.Stop(); }
    }

    /// <summary>As reçu, As illisible ignoré, AB sans attente et ses refus locaux.</summary>
    private static void Stats(Accounts account, Socket peer, ConcurrentQueue<string> logs)
    {
        CharacterClass character = account.Game.character;
        StatsActions actions = character.StatsActions;
        Check(actions.CannotBoost(BoostableStat.Force) != null && !Result(actions.BoostAsync(BoostableStat.Force)).Sent, "Boost allowed before As");
        NoPacket(peer, "AB sent before As");
        int refreshes = 0;
        Action counter = () => Interlocked.Increment(ref refreshes);
        character.RefreshCaracteristiques += counter;
        FeedFromNetwork(account, FullAs(2, 5));
        CharacterStats received = character.stats;
        Check(refreshes == 1 && received.FieldCount == 51 && character.Kamas == 1234 && character.Carac_Points == 2 && character.SpellPoints == 5
            && received.Get(AsField.EsquivePM).Extra == 430, "As was not applied to the character");
        Feed(account, "As1,0,10|0|0|0|0~0,0,0,0,0,0|1,1|1,1|1|1|a,0,0,0|");
        Check(ReferenceEquals(character.stats, received) && refreshes == 1 && character.Carac_Points == 2 && logs.Any(log => log.Contains("Paquet As illisible")),
            "Unreadable As changed the sheet or was not logged");
        character.RefreshCaracteristiques -= counter;

        // AB<code> envoyé tout de suite (plus de Task.Delay) ; le serveur répond par As.
        var watch = Stopwatch.StartNew();
        Task<InteractionResult> boost = actions.BoostAsync(BoostableStat.Force);
        Expect(peer, "AB10", "Force boost does not send AB10");
        InteractionResult result = Result(boost);
        watch.Stop();
        Check(result.Sent && watch.ElapsedMilliseconds < 450, "Boost waited after sending: " + watch.ElapsedMilliseconds + " ms");
        Check(Result(actions.BoostAsync(BoostableStat.Intelligence)).Sent, "Intelligence boost refused"); Expect(peer, "AB15", "Intelligence boost does not send AB15");
        Check(actions.CannotBoost(BoostableStat.Sagesse).Contains("Capital insuffisant") && !Result(actions.BoostAsync(BoostableStat.Sagesse)).Sent && !actions.IsAffordable(BoostableStat.Sagesse),
            "Wisdom boost allowed with 2 points for a cost of 3");
        Check(!Result(actions.BoostAsync((BoostableStat)7)).Sent, "Unknown characteristic accepted");
        account.AccountStates = AccountStates.FIGHTING;
        Check(actions.CannotBoost(BoostableStat.Force) == "Action impossible pendant un combat." && !Result(actions.BoostAsync(BoostableStat.Force)).Sent, "Boost allowed in fight");
        account.AccountStates = AccountStates.CONNECTED_INACTIVE;
        NoPacket(peer, "A refused boost sent a packet");
    }

    /// <summary>SL, SLo séparé, SB modificateurs, SB → SUK/SUE, SF+/SF-/SF&lt;id&gt;/SF-1.</summary>
    private static void Spells(Accounts account, Socket peer, ConcurrentQueue<string> logs)
    {
        CharacterClass character = account.Game.character;
        SpellBook book = character.SpellBook;
        int changes = 0;
        Action counter = () => Interlocked.Increment(ref changes);
        book.Changed += counter;
        Feed(account, "SL3~2~b;17~1~c;19~2~d;");
        var learned = character.Spells;
        Check(learned.Count == 3 && learned[3].Level == 2 && learned[19].Level == 2, "SL was not read");

        // PR #15 : SLo ne passe plus par SL (qui vidait la liste).
        FeedFromNetwork(account, "SLo+");
        Check(ReferenceEquals(character.Spells, learned) && learned.Count == 3 && book.CanSeeAllSpells && changes == 1 && !logs.Any(log => log.Contains("mal formé")),
            "SLo+ was routed to SL or not applied");
        Feed(account, "SLo-");
        Check(!book.CanSeeAllSpells && character.Spells.Count == 3, "SLo- not applied");

        // Bonus d'objets de classe : SB<effet>;<sort>;<valeur>, valeur 0 = retrait.
        Feed(account, "SB283;3;10");
        Check(book.ModificatorsOf(3).Single().Key == 283 && book.ModificatorsOf(3).Single().Value == 10
            && book.DescribeModificators(3).Single() == "+10 de dommages sur le sort Frappe fictive (test)", "SB modificator not read");
        Feed(account, "SB283;3;0");
        Check(book.ModificatorsOf(3).Count == 0, "SB value 0 did not remove the modificator");
        foreach (string broken in new[] { "SB", "SBx;y;z", "SB283;3", "SF", "SFx", "SLo" }) Feed(account, broken);
        Check(book.ModificatorsOf(3).Count == 0 && !book.ForgetWindowOpen && !book.CanSeeAllSpells && character.Spells.Count == 3, "Malformed spell packets changed the state");

        // SB<id> : contrôles de Spells.boostSpell, puis SUK + As ou SUE.
        Check(Result(book.UpgradeAsync(3)).Sent, "Upgrade of spell 3 refused"); Expect(peer, "SB3", "Upgrade does not send SB3");
        FeedFromNetwork(account, "SUK3~3");
        FeedFromNetwork(account, FullAs(2, 3));
        Check(character.Spells[3].Level == 3 && character.SpellPoints == 3 && book.LastMessage.Contains("niveau 3"), "SUK3~3 + As not applied");
        Check(book.CannotUpgrade(3) == "Niveau 30 requis (test)" && !Result(book.UpgradeAsync(3)).Sent, "Required level (LEVEL_NEED_TO_BOOST) not checked");
        Check(book.CannotUpgrade(19) == "Ce sort est déjà à son niveau maximal.", "Maximal level not checked");
        Check(book.CannotUpgrade(17) == null && book.CannotUpgrade(999) == "Ce sort n'est pas appris.", "Spell 17 or unknown spell check differs");
        FeedFromNetwork(account, FullAs(2, 0));
        Check(book.CannotUpgrade(17).Contains("Points de sort insuffisants"), "Spell points not checked");
        FeedFromNetwork(account, FullAs(2, 5));
        string refused = null;
        Action<string> onRefused = message => refused = message;
        book.UpgradeRefused += onRefused;
        Check(Result(book.UpgradeAsync(17)).Sent, "Upgrade of spell 17 refused"); Expect(peer, "SB17", "Upgrade does not send SB17");
        FeedFromNetwork(account, "SUE");
        Check(refused == "Amélioration impossible (test)" && character.Spells[17].Level == 1 && logs.Any(log => log.Contains("Amélioration du sort refusée")), "SUE not reported");
        book.UpgradeRefused -= onRefused;
        NoPacket(peer, "A refused upgrade sent a packet");

        // Oubli : refusé hors fenêtre (StarLoco ignore SF hors FORGETTING_SPELL), SF+ ouvre, SF<id> ou SF-1 ferment.
        Check(!book.ForgetWindowOpen && book.CannotForget(3).Contains("fenêtre") && !Result(book.ForgetAsync(3)).Sent && !Result(book.CancelForgetAsync()).Sent,
            "Forget allowed outside the forget window");
        NoPacket(peer, "SF sent outside the forget window");
        FeedFromNetwork(account, "SF+");
        Check(book.ForgetWindowOpen && book.ForgettableSpells().Select(s => s.ID).SequenceEqual(new short[] { 3, 19 }) && book.CannotForget(17) == "Un sort de niveau 1 ne peut pas être oublié."
            && !Result(book.ForgetAsync(17)).Sent, "SF+ not applied or level 1 spell accepted");
        Check(Result(book.ForgetAsync(19)).Sent && !book.ForgetWindowOpen, "Forget of spell 19 refused or window kept open"); Expect(peer, "SF19", "Forget does not send SF19");
        FeedFromNetwork(account, "SUK19~1");
        Check(character.Spells[19].Level == 1 && book.LastMessage.Contains("ramené au niveau 1"), "SUK after forget not applied");
        FeedFromNetwork(account, "SF+");
        Check(Result(book.CancelForgetAsync()).Sent && !book.ForgetWindowOpen, "Cancel refused"); Expect(peer, "SF-1", "Cancel does not send SF-1");
        FeedFromNetwork(account, "SF+"); FeedFromNetwork(account, "SF-");
        Check(!book.ForgetWindowOpen, "SF- did not close the forget window");
        NoPacket(peer, "SF- sent a packet");
        book.Changed -= counter;
    }

    private static void Windows(Accounts account, Socket peer, string folder)
    {
        CharacterClass character = account.Game.character;
        string images = Path.Combine(folder, "images");
        Png(Path.Combine(images, "sorts"), "3", Color.FromArgb(255, 160, 90, 40), 80, 80);
        Png(Path.Combine(images, "Alignments"), "1", Color.FromArgb(255, 70, 120, 200), 40, 40);
        Assets.GetProperty("Root", Any).SetValue(null, images, null);
        var reads = new List<KeyValuePair<string, int>>();
        Action<string, int> observer = (key, thread) => { lock (reads) reads.Add(new KeyValuePair<string, int>(key, thread)); };
        EventInfo readEvent = Assets.GetEvent("AssetFileRead", Any);
        readEvent.GetAddMethod(true).Invoke(null, new object[] { observer });
        string optionsPath = Path.Combine(folder, "config", "BotOptions.json"); Directory.CreateDirectory(Path.GetDirectoryName(optionsPath));
        try
        {
            using (var form = new GameClientFullform(account, BotOptions.Load(optionsPath)))
            {
                form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-4000, -4000);
                form.ShowInTaskbar = false; form.Show(); Application.DoEvents();
                int uiThread = Thread.CurrentThread.ManagedThreadId;
                PanelHost drawer = form.Panels;
                NoPacket(peer, "Opening the game window sent a packet");
                StatsWindow(account, peer, drawer, uiThread, reads);
                SpellsWindow(account, peer, drawer, form, uiThread, reads);
                NoPacket(peer, "Unexpected packet after the windows");
                // Fermer la fenêtre libère le compte : la remise à zéro se vérifie avant.
                character.Clear();
                Check(!character.SpellBook.ForgetWindowOpen && !character.SpellBook.CanSeeAllSpells && character.SpellBook.ModificatorsOf(3).Count == 0
                    && character.stats.FieldCount == 0 && character.Carac_Points == 0, "Clear kept the sheet state");
                form.Close(); Application.DoEvents();
            }
        }
        finally { readEvent.GetRemoveMethod(true).Invoke(null, new object[] { observer }); }
    }

    private static void StatsWindow(Accounts account, Socket peer, PanelHost drawer, int uiThread, List<KeyValuePair<string, int>> reads)
    {
        var panel = drawer.Get<StatsPanel>();
        drawer.Show(panel); Application.DoEvents();
        Check(drawer.Current == panel && panel.Title == "Caractéristiques" && panel.SelectedTab == StatsPanel.MainTab, "Stats panel did not open");
        Check(panel.ValueText(BoostableStat.Force) == "11 (+322)" && panel.ValueText(BoostableStat.Intelligence) == "16 (+332)" && panel.InfoText("ap") == "327"
            && panel.InfoText("mp") == "330" && panel.InfoText("initiative") == "120" && panel.InfoText("prospection") == "130" && panel.InfoText("life") == "300 / 350",
            "Sheet values differ: " + panel.ValueText(BoostableStat.Force) + " / " + panel.ValueText(BoostableStat.Intelligence) + " / " + panel.InfoText("ap") + " / " + panel.InfoText("mp")
            + " / " + panel.InfoText("initiative") + " / " + panel.InfoText("prospection") + " / " + panel.InfoText("life"));
        Button force = panel.BoostButton(BoostableStat.Force), wisdom = panel.BoostButton(BoostableStat.Sagesse);
        Check(force.Visible && force.Enabled && !wisdom.Visible, "Boost buttons visibility differs (capital 2: wisdom costs 3)");
        var xp = All(panel.View).OfType<XpGauge>().Single(gauge => gauge.Name == "stats-xp");
        var alignment = All(panel.View).OfType<Label>().Single(label => label.Name == "stats-alignment-text");
        Check(Math.Abs(xp.Percent - 50) < 0.01 && alignment.Text.Contains("3") && alignment.Text.Contains("750"), "XP gauge or alignment line differs: " + alignment.Text);
        PumpUntil(() => Cached("Alignments", "1"));
        lock (reads) Check(reads.Any(entry => entry.Key.EndsWith("Alignments/1", StringComparison.Ordinal) && entry.Value != uiThread), "Alignment image was not read off the UI thread");

        // « + » : AB<code> tout de suite, la fiche suit le As du serveur.
        Click(force); Expect(peer, "AB10", "Force button does not send AB10");
        NoPacket(peer, "Force button sent more than one packet");
        FeedFromNetwork(account, FullAs(1, 5).Replace("|11,111,211,311|", "|12,111,211,311|"));
        PumpUntil(() => panel.ValueText(BoostableStat.Force) == "12 (+322)");
        Check(force.Visible && !wisdom.Visible && account.Game.character.Carac_Points == 1, "Sheet did not follow As");
        account.AccountStates = AccountStates.FIGHTING;
        PumpUntil(() => !force.Enabled);
        Click(force); NoPacket(peer, "A disabled boost button sent AB in fight");
        account.AccountStates = AccountStates.CONNECTED_INACTIVE;
        PumpUntil(() => force.Enabled);
        FeedFromNetwork(account, FullAs(0, 5));
        PumpUntil(() => !force.Visible);

        // Onglet des caractéristiques avancées (StatsViewer) : 4 catégories, champs 9 à 50.
        panel.ShowTab(StatsPanel.AdvancedTab); Application.DoEvents();
        IReadOnlyList<string[]> rows = panel.AdvancedRows;
        Check(panel.SelectedTab == StatsPanel.AdvancedTab && rows.Count == 46 && rows.Any(row => row[0] == "Résistances JcJ (test)")
            && rows.Any(row => row[0] == "Neutre JcJ fixe (test)" && row[1] == "33" && row[2] == "133" && row[3] == "233" && row[4] == "333" && row[5] == "732")
            && rows.Count(row => row.Length == 6) == 42, "Advanced characteristics differ (" + rows.Count + " rows)");
        panel.ShowTab(StatsPanel.MainTab);
        drawer.RequestClose(panel); Application.DoEvents();
        NoPacket(peer, "Closing the stats panel sent a packet");
    }

    private static void SpellsWindow(Accounts account, Socket peer, PanelHost drawer, GameClientFullform form, int uiThread, List<KeyValuePair<string, int>> reads)
    {
        CharacterClass character = account.Game.character;
        SpellBook book = character.SpellBook;
        Feed(account, "SL3~2~b;17~1~c;19~2~d;");
        var panel = drawer.Get<SpellsPanel>();
        drawer.Show(panel); Application.DoEvents();
        var list = (ListView)Get(panel, "spells");
        var type = (ComboBox)Get(panel, "typeFilter");
        var help = (Label)Get(panel, "spellHelp");
        var upgrade = (Control)Get(panel, "upgradeSpell");
        var forget = (Control)Get(panel, "forgetSpell");
        Check(type.SelectedIndex == 1 && list.Items.Cast<ListViewItem>().Select(row => row.Text).SequenceEqual(new[] { "Frappe fictive", "Sort maximal" }),
            "Class filter differs: " + string.Join(", ", list.Items.Cast<ListViewItem>().Select(row => row.Text)));
        type.SelectedIndex = 0; Application.DoEvents();
        Check(list.Items.Count == 3 && list.Items.Cast<ListViewItem>().Any(row => row.Text == "Glyphe fictif"), "« Tous types » filter differs");
        PumpUntil(() => Cached("Spells", "3"));
        lock (reads) Check(reads.Any(entry => entry.Key.EndsWith("Spells/3", StringComparison.Ordinal) && entry.Value != uiThread), "Spell icon was not read off the UI thread");

        // Fiche détaillée : niveau courant, onglets de niveau et d'effets critiques, autres caractéristiques, description.
        panel.SelectSpell(3); Application.DoEvents();
        Check(panel.DetailSpellId == 3 && panel.DetailLevel == 2 && panel.EffectsText.Contains("Dommages : 2 à 6 (test)")
            && panel.EffectsText.Contains("Dans 25% des cas (test): Soins : 5 (test) (3 tours)") && panel.DescriptionText == "Frappe de test."
            && panel.Characteristics.Any(row => row.Value == "1/50") && panel.Characteristics.Any(row => row.Value == "1/100"), "Spell detail differs: " + panel.EffectsText);
        panel.ShowLevel(4); Application.DoEvents();
        Check(panel.DetailLevel == 4 && panel.EffectsText.Contains("Dommages : 4 à 8 (test)") && panel.Characteristics.Any(row => row.Value == "30"), "Level tab differs");
        ((JobTabStrip)Get(panel, "effectTabs")).ClickTab(1); Application.DoEvents();
        Check(panel.EffectsText.Contains("Dommages : 9 à 13 (test)") && !panel.EffectsText.Contains("Soins"), "Critical effects tab differs: " + panel.EffectsText);
        ((JobTabStrip)Get(panel, "effectTabs")).ClickTab(0);
        panel.ShowLevel(6);
        Check(panel.DetailLevel == 4, "A level beyond the maximum was shown");
        FeedFromNetwork(account, "SB283;3;10");
        PumpUntil(() => ((Label)Get(panel, "spellModificators")).Text.Contains("+10 de dommages sur le sort Frappe fictive (test)"));

        // « Oublier » grisé hors fenêtre d'oubli ; « Améliorer » envoie SB<id>.
        Check(!forget.Enabled && upgrade.Enabled && help.Text.Contains("Niveau suivant : 3"), "Buttons outside the forget window differ: " + help.Text);
        Click(forget); NoPacket(peer, "A greyed « Oublier » sent SF");
        Click(upgrade); Expect(peer, "SB3", "« Améliorer » does not send SB3");
        FeedFromNetwork(account, "SUK3~3"); FeedFromNetwork(account, FullAs(0, 3));
        PumpUntil(() => list.SelectedItems.Count == 1 && list.SelectedItems[0].SubItems[1].Text == "3" && !upgrade.Enabled);
        Check(help.Text.Contains("Niveau 30 requis (test)"), "Required level not shown: " + help.Text);
        panel.SelectSpell(19); Application.DoEvents();
        Check(help.Text.Contains("maximal") && !upgrade.Enabled, "Maximal spell can still be upgraded: " + help.Text);

        // SF+ : la fenêtre s'affiche, seuls les sorts de niveau 2 et plus restent, Oublier demande confirmation.
        drawer.RequestClose(panel); Application.DoEvents();
        FeedFromNetwork(account, "SF+");
        PumpUntil(() => drawer.Current == panel && panel.IsServerWindowOpen);
        Check(list.Items.Cast<ListViewItem>().Select(row => row.Text).SequenceEqual(new[] { "Frappe fictive", "Sort maximal" }) && !type.Enabled
            && ((Control)Get(panel, "forgetBar")).Visible, "Forget mode list differs");
        panel.SelectSpell(3); Application.DoEvents();
        Check(forget.Enabled, "« Oublier » stays greyed during the forget window");
        Click(forget); PumpUntil(() => panel.ForgetDialog != null);
        Check(DialogMessage(panel.ForgetDialog) == "Oublier Frappe fictive ? (test)" && !forget.Enabled, "Forget confirmation differs: " + DialogMessage(panel.ForgetDialog));
        NoPacket(peer, "SF sent before the answer");
        Answer(panel.ForgetDialog, "Non"); PumpUntil(() => panel.ForgetDialog == null);
        NoPacket(peer, "« Non » sent a packet");
        Click(forget); PumpUntil(() => panel.ForgetDialog != null);
        Answer(panel.ForgetDialog, "Oui"); Expect(peer, "SF3", "« Oui » does not send SF3");
        PumpUntil(() => !book.ForgetWindowOpen && !((Control)Get(panel, "forgetBar")).Visible);
        FeedFromNetwork(account, "SUK3~1");
        FeedFromNetwork(account, "SF+"); PumpUntil(() => panel.IsServerWindowOpen);
        Click(Get(panel, "cancelForget")); Expect(peer, "SF-1", "« Annuler » does not send SF-1");
        FeedFromNetwork(account, "SF+"); PumpUntil(() => panel.IsServerWindowOpen);
        drawer.CloseAll(); Application.DoEvents();
        Check(drawer.IsOpen(panel), "CloseAll removed the open forget window");
        drawer.RequestClose(panel); Expect(peer, "SF-1", "Closing the forget window does not send SF-1");
        PumpUntil(() => !drawer.IsOpen(panel) && !book.ForgetWindowOpen);

        // Option « voir tous les sorts » (SLo+) : sorts de classe non appris, grisés.
        drawer.Show(panel); FeedFromNetwork(account, "SLo+");
        var seeAll = (CheckBox)Get(panel, "seeAll");
        PumpUntil(() => seeAll.Visible);
        seeAll.Checked = true; Application.DoEvents();
        Check(list.Items.Cast<ListViewItem>().Any(row => row.Text == "Sort non appris" && row.ForeColor.ToArgb() == Color.FromArgb(108, 100, 74).ToArgb()), "Unlearned class spells are not listed");
        panel.SelectSpell(18); Application.DoEvents();
        Check(!upgrade.Enabled && panel.CreateDragData(18) == null, "An unlearned spell can be upgraded or dragged");

        // Glisser vers la barre : ordre local, SM<id>|<position> (réponse BN seulement).
        DataObject data = panel.CreateDragData(3);
        var payload = data?.GetData(ShortcutBar.DragFormat) as ShortcutPayload;
        Check(payload != null && payload.Kind == ShortcutSlotKind.Spell && payload.Id == 3, "Spell drag payload differs");
        Check(Result(form.Banner.Shortcuts.DropSpellAsync((short)payload.Id, 2)), "Drop on the shortcut bar refused");
        Expect(peer, "SM3|2", "Dropping a spell does not send SM3|2");
        Feed(account, "BN");
        Check(form.Banner.Shortcuts.SpellAt(2) == 3, "The local spell bar order was not kept");
        drawer.RequestClose(panel); Application.DoEvents();
        NoPacket(peer, "Closing the spells panel sent a packet");
    }
}
