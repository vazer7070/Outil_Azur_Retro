using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Tool_BotProtocol.Config
{
    public class GlobalConfig
    {
        private static readonly string COnfigPath = Path.Combine("ressources", "Bot", "BotConfig.json");
        private static readonly Dictionary<string, string> ConfigDico = new Dictionary<string, string>();
        private static readonly object Sync = new object();
        public const string BOTVERSION = "Azur Retro / StarLoco";
        public static string IP => SearchConfig("IP");
        public static string AUTHPORT => SearchConfig("authport");
        public static string GAMEPORT => SearchConfig("gameport");
        public static string VERSION => SearchConfig("version");
        public static string CORESIZE => SearchConfig("coresize");
        public static string LOADERSIZE => SearchConfig("loadersize");
        public static string OFFICIALIP = "172.65.213.92";
        public static string OFFICIALPORT = "443";
        public static bool BYPASS;

        private static JObject Defaults() => new JObject(new JProperty("Config", new JObject(
            new JProperty("IP", "127.0.0.1"), new JProperty("AuthPort", "450"),
            new JProperty("GamePort", "5555"), new JProperty("Version", "1.34.1"),
            new JProperty("CoreSize", "2528660"), new JProperty("LoaderSize", "2362079"))));

        public static void InitializeConfig()
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(COnfigPath));
                if (!File.Exists(COnfigPath)) File.WriteAllText(COnfigPath, Defaults().ToString());
                LectConfig(COnfigPath);
            }
        }

        public static void Validate(string ip, string ap, string gp, string version, string coresize, string loadersize)
        {
            if (string.IsNullOrWhiteSpace(ip) || Uri.CheckHostName(ip.Trim().Trim('[', ']')) == UriHostNameType.Unknown)
                throw new ArgumentException("Indiquez une adresse IP ou un nom de serveur valide.");
            int number;
            if (!int.TryParse(ap, out number) || number < 1 || number > 65535)
                throw new ArgumentException("Le port d’authentification doit être compris entre 1 et 65535.");
            if (!int.TryParse(gp, out number) || number < 1 || number > 65535)
                throw new ArgumentException("Le port de jeu doit être compris entre 1 et 65535.");
            if (string.IsNullOrWhiteSpace(version) || version.IndexOfAny(new[] { '\n', '\r', '\0' }) >= 0)
                throw new ArgumentException("Indiquez une version du client valide.");
            long size;
            if (!long.TryParse(coresize, NumberStyles.None, CultureInfo.InvariantCulture, out size) || size < 0 ||
                !long.TryParse(loadersize, NumberStyles.None, CultureInfo.InvariantCulture, out size) || size < 0)
                throw new ArgumentException("Les tailles Core et Loader doivent être des nombres entiers positifs.");
        }

        public static void writenewconfig(string ip, string ap, string gp, string version, string coresize, string loadersize)
        {
            Validate(ip, ap, gp, version, coresize, loadersize);
            var data = Defaults();
            var config = (JObject)data["Config"];
            config["IP"] = ip.Trim().Trim('[', ']');
            config["AuthPort"] = ap.Trim(); config["GamePort"] = gp.Trim();
            config["Version"] = version.Trim(); config["CoreSize"] = coresize; config["LoaderSize"] = loadersize;
            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(COnfigPath));
                AtomicWrite(COnfigPath, data.ToString());
                Apply(config);
            }
        }

        internal static void AtomicWrite(string path, string contents)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, contents);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        public static void LectConfig(string path)
        {
            lock (Sync)
            {
                var config = JObject.Parse(File.ReadAllText(path))["Config"] as JObject;
                if (config == null) throw new InvalidDataException("Le fichier BotConfig.json ne contient pas de configuration.");
                var defaults = (JObject)Defaults()["Config"];
                foreach (var property in defaults.Properties())
                    if (config[property.Name] == null) config[property.Name] = property.Value.DeepClone();
                Validate((string)config["IP"], (string)config["AuthPort"], (string)config["GamePort"],
                    (string)config["Version"], (string)config["CoreSize"], (string)config["LoaderSize"]);
                Apply(config);
            }
        }

        private static void Apply(JObject config)
        {
            ConfigDico["IP"] = ((string)config["IP"]).Trim().Trim('[', ']');
            ConfigDico["authport"] = (string)config["AuthPort"];
            ConfigDico["gameport"] = (string)config["GamePort"];
            ConfigDico["version"] = (string)config["Version"];
            ConfigDico["coresize"] = (string)config["CoreSize"];
            ConfigDico["loadersize"] = (string)config["LoaderSize"];
        }

        public static string SearchConfig(string key)
        {
            lock (Sync)
            {
                string value;
                return ConfigDico.TryGetValue(key, out value) ? value : null;
            }
        }
    }
}
