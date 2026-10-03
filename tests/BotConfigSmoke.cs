using System;
using System.IO;
using System.Reflection;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Utils.Logger;

internal static class BotConfigSmoke
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Reject(Action action, string message)
    {
        try { action(); } catch (ArgumentException) { return; } catch (FormatException) { return; }
        throw new Exception(message);
    }
    private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            string path = Path.Combine(TestPaths.ApplicationBin, new AssemblyName(args.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        Run();
    }

    private static void Run()
    {
        string work = Path.Combine(TestPaths.Work, "bot-config");
        Directory.CreateDirectory(work);
        Directory.SetCurrentDirectory(work);
        GlobalConfig.InitializeConfig(); GlobalConfig.InitializeConfig();
        Check(GlobalConfig.IP == "127.0.0.1" && GlobalConfig.AUTHPORT == "450", "Missing configuration did not use StarLoco defaults");
        GlobalConfig.writenewconfig("localhost", "450", "5555", "1.34.1", "2528660", "2362079");
        Check(GlobalConfig.IP == "localhost", "Hostnames were not accepted");
        string path = Path.Combine("ressources", "Bot", "BotConfig.json");
        string saved = File.ReadAllText(path);
        Reject(() => GlobalConfig.writenewconfig("localhost", "0", "5555", "1.34.1", "1", "2"), "Invalid authentication port accepted");
        Reject(() => GlobalConfig.writenewconfig("localhost", "450", "65536", "1.34.1", "1", "2"), "Invalid game port accepted");
        Reject(() => GlobalConfig.Validate("bad host", "450", "5555", "1.34.1", "1", "2"), "Invalid host accepted");
        Reject(() => GlobalConfig.Validate("localhost", "450", "5555", "1.34.1\nAf", "1", "2"), "Line injection accepted");
        Check(File.ReadAllText(path) == saved && GlobalConfig.IP == "localhost", "A rejected config destroyed previous settings");
        GlobalConfig.writenewconfig("[::1]", "450", "5555", "1.34.1", "2528660", "2362079");
        Check(GlobalConfig.IP == "::1", "IPv6 brackets were not normalized");
        string host; int port;
        Accounts.ParseEndpoint("[::1]:5555", out host, out port);
        Check(host == "::1" && port == 5555, "IPv6 game endpoint parsing failed");
        Accounts.ParseEndpoint("localhost:5555", out host, out port);
        Check(host == "localhost" && port == 5555, "DNS game endpoint parsing failed");
        Reject(() => Accounts.ParseEndpoint("localhost:0", out host, out port), "Invalid redirect accepted");

        AccountConfig.LoadAccount(); AccountConfig.LoadAccount();
        Check(AccountConfig.AccountsDico.Count == 0, "Fresh account directory was not initialized");
        const string name = "../test\\unsafe:name";
        const string password = "  synthetic-secret-with-spaces  ";
        AccountConfig.WriteCompte(name, password, "Privé");
        string accountDirectory = Path.Combine("ressources", "Bot", "AccountSingle");
        string[] files = Directory.GetFiles(accountDirectory, "*.json");
        Check(files.Length == 1 && !File.ReadAllText(files[0]).Contains(password), "Saved password was plaintext or filename escaped its directory");
        AccountConfig.LoadAccount(); AccountConfig.LoadAccount();
        Check(AccountConfig.AccountsDico.Count == 1 && AccountConfig.ReturnAccountInfo(name).Password == password, "Protected password or spaces did not roundtrip");
        File.WriteAllText(Path.Combine(accountDirectory, "legacy.json"), "{\"Comptes\":{\"Compte\":\"legacy-test\",\"MDP\":\"synthetic-legacy\",\"Lieu\":\"Privé\"}}");
        AccountConfig.LoadAccount();
        Check(AccountConfig.ReturnAccountInfo("legacy-test").Password == "synthetic-legacy", "Legacy accounts were not readable");
        AccountConfig.WriteCompte("legacy-test", "synthetic-new", "Privé");
        Check(!File.Exists(Path.Combine(accountDirectory, "legacy.json")), "Migrated plaintext file remains");
        AccountConfig.LoadAccount();
        Check(AccountConfig.ReturnAccountInfo("legacy-test").Password == "synthetic-new", "Migrated account did not reload");
        AccountConfig.DeleteCompte(name);
        Check(!AccountConfig.AccountsDico.ContainsKey(name) && Directory.GetFiles(accountDirectory, "*.json").Length == 1, "Delete touched the wrong account");

        using (var account = new Accounts(new AccountConfig("synthetic-user", "synthetic-secret", "Privé")))
        {
            account.GameTicket = "synthetic-ticket";
            foreach (string packet in new[] { "HCabcdefghijkl", "AYKlocalhost:5555;synthetic-ticket", "ATsynthetic-ticket", "AD42|synthetic-answer", "#1encrypted", "synthetic-user", "synthetic-secret" })
                Check(BotPacketRedactor.Redact(packet, account).Contains("masqué"), "Authentication packet leaked into log");
            Check(BotPacketRedactor.Redact("ATK0", account) == "ATK0", "Game authentication acknowledgement hidden");
            Check(BotPacketRedactor.Redact(new string('x', 5000), account).Length < 2200, "Large packet floods log");
        }
        var logger = new Logger(); int received = 0;
        logger.log_event += (message, color) => { throw new Exception("Closed UI"); };
        logger.log_event += (message, color) => received++;
        logger.LogInfo("Test", "Subscriber isolation");
        Check(received == 1, "Failing logger subscriber prevented other listeners");
        Console.WriteLine("OK: bot defaults/config validation, DNS/IPv6, DPAPI accounts, legacy migration, path safety, log masking and logger isolation");
    }
}
