using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Tool_BotProtocol.Config
{
    public class AccountConfig
    {
        public static Dictionary<string, AccountConfig> AccountsDico = new Dictionary<string, AccountConfig>(StringComparer.OrdinalIgnoreCase);
        public static List<AccountConfig> AccountsActive = new List<AccountConfig>();
        private static readonly string comptePath = Path.Combine("ressources", "Bot", "AccountSingle");
        private static readonly Dictionary<string, string> LoadedPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private const string ProtectedPrefix = "dpapi:";
        public static string[] LoadWarnings { get; private set; } = new string[0];
        public string Account { get; set; }
        public string Password { get; set; }
        public string Lieu { get; set; }
        public int Server_id;
        public List<string> Servers { get; set; }
        public List<string> Perso_names_and_levels;

        public AccountConfig(string account, string pass, string lieu)
        {
            Account = account; Password = pass; Lieu = lieu;
            Servers = new List<string>(); Perso_names_and_levels = new List<string>();
        }

        public static AccountConfig ReturnAccountInfo(string key) => AccountsDico[key];

        public static void LoadAccount()
        {
            Directory.CreateDirectory(comptePath);
            AccountsDico.Clear(); LoadedPaths.Clear();
            var warnings = new List<string>();
            foreach (string path in Directory.GetFiles(comptePath, "*.json"))
            {
                try
                {
                    var entry = JObject.Parse(File.ReadAllText(path))["Comptes"] as JObject;
                    if (entry == null) continue;
                    string name = (string)entry["Compte"];
                    string password = (string)entry["MDP"];
                    Validate(name, password);
                    if ((string)entry["PasswordProtection"] == "WindowsCurrentUser")
                        password = Unprotect(password);
                    AccountsDico[name] = new AccountConfig(name, password, (string)entry["Lieu"] ?? "Privé");
                    LoadedPaths[name] = Path.GetFullPath(path);
                }
                catch (Exception error) when (error is IOException || error is Newtonsoft.Json.JsonException ||
                    error is CryptographicException || error is FormatException || error is ArgumentException)
                {
                    // Keep unreadable accounts on disk. Never log or overwrite their credentials.
                    warnings.Add(Path.GetFileName(path) + " : compte illisible ou protégé pour un autre utilisateur Windows.");
                }
            }
            LoadWarnings = warnings.ToArray();
        }

        public static void Validate(string account, string password)
        {
            if (string.IsNullOrWhiteSpace(account) || account.IndexOfAny(new[] { '\n', '\r', '\0' }) >= 0)
                throw new ArgumentException("Indiquez un nom de compte valide.");
            if (string.IsNullOrEmpty(password) || password.IndexOfAny(new[] { '\n', '\r', '\0' }) >= 0)
                throw new ArgumentException("Indiquez un mot de passe valide.");
        }

        private static string AccountPath(string account)
        {
            using (var hash = SHA256.Create())
            {
                string name = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(account.ToLowerInvariant()))).Replace("-", "");
                return Path.GetFullPath(Path.Combine(comptePath, name + ".json"));
            }
        }

        private static string Protect(string password) => ProtectedPrefix + Convert.ToBase64String(
            ProtectedData.Protect(Encoding.UTF8.GetBytes(password), null, DataProtectionScope.CurrentUser));

        private static string Unprotect(string password)
        {
            if (password == null || !password.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
                throw new FormatException("Mot de passe protégé invalide.");
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(password.Substring(ProtectedPrefix.Length)),
                null, DataProtectionScope.CurrentUser));
        }

        public static void WriteCompte(string account, string password, string lieu)
        {
            Validate(account, password);
            account = account.Trim();
            Directory.CreateDirectory(comptePath);
            var data = new JObject(new JProperty("Comptes", new JObject(new JProperty("Compte", account),
                new JProperty("MDP", Protect(password)), new JProperty("PasswordProtection", "WindowsCurrentUser"),
                new JProperty("Lieu", lieu ?? "Privé"))));
            string path = AccountPath(account);
            GlobalConfig.AtomicWrite(path, data.ToString());
            string oldPath;
            if (LoadedPaths.TryGetValue(account, out oldPath) && !string.Equals(oldPath, path, StringComparison.OrdinalIgnoreCase))
                File.Delete(oldPath);
            LoadedPaths[account] = path;
            AccountsDico[account] = new AccountConfig(account, password, lieu ?? "Privé");
        }

        public static void DeleteCompte(string account)
        {
            if (string.IsNullOrWhiteSpace(account)) return;
            string path;
            if (LoadedPaths.TryGetValue(account, out path) && File.Exists(path)) File.Delete(path);
            path = AccountPath(account);
            if (File.Exists(path)) File.Delete(path);
            LoadedPaths.Remove(account); AccountsDico.Remove(account);
        }
    }
}
