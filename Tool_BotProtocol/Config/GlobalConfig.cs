using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using static Tool_BotProtocol.Json.Config;

namespace Tool_BotProtocol.Config
{
    public class GlobalConfig
    {
        static readonly string ConfigPath = @".\ressources\Bot\BotConfig.json";
        static readonly JObject DefaultConfig = new JObject(
            new JProperty(
                "Config",
                new JObject(
                    new JProperty("IP", "127.0.0.1"),
                    new JProperty("AuthPort", "450"),
                    new JProperty("GamePort", "5555"),
                    new JProperty("Version", "1.34.1"),
                    new JProperty("CoreSize", "2528660"),
                    new JProperty("LoaderSize", "2362079"))));

        static readonly Dictionary<string, string> ConfigDico = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public const string BOTVERSION = "N/A";
        public static string IP => SearchConfig("IP");
        public static string AUTHPORT => SearchConfig("authport");
        public static string GAMEPORT => SearchConfig("gameport");
        public static string VERSION => SearchConfig("version");
        public static string CORESIZE => SearchConfig("coresize");
        public static string LOADERSIZE => SearchConfig("loadersize");
        public static string OFFICIALIP = "172.65.213.92";
        public static string OFFICIALPORT = "443";
        public static bool BYPASS = false;

        public static void InitializeConfig()
        {
            if (!File.Exists(ConfigPath))
            {
                WriteFile(ConfigPath);
            }

            LectConfig(ConfigPath);
        }

        public static void writenewconfig(string ip, string ap, string gp, string version, string coresize, string loadersize)
        {
            if (File.Exists(ConfigPath))
            {
                File.Delete(ConfigPath);
            }

            JObject newConfig = new JObject(
                new JProperty(
                    "Config",
                    new JObject(
                        new JProperty("IP", ip),
                        new JProperty("AuthPort", ap),
                        new JProperty("GamePort", gp),
                        new JProperty("Version", version),
                        new JProperty("CoreSize", coresize),
                        new JProperty("LoaderSize", loadersize))));

            WriteJsonFile(ConfigPath, newConfig);
            LectConfig(ConfigPath);
        }

        static void WriteFile(string path)
        {
            WriteJsonFile(path, DefaultConfig);
        }

        static void WriteJsonFile(string path, JObject content)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            JObject parsed = JObject.Parse(content.ToString());
            File.WriteAllText(path, parsed.ToString());
        }

        public static void LectConfig(string path)
        {
            try
            {
                string fileContent = File.ReadAllText(path);
                if (!IsValidJson(fileContent))
                {
                    WriteFile(path);
                    fileContent = File.ReadAllText(path);
                }

                ConfigEntries.DataEntries data = JsonConvert.DeserializeObject<ConfigEntries.DataEntries>(fileContent);
                ConfigEntries config = data?.ConfigEntries ?? throw new InvalidDataException("Configuration file is missing the 'Config' section.");

                ConfigDico.Clear();
                ConfigDico["IP"] = config.IP ?? string.Empty;
                ConfigDico["authport"] = config.AuthPort ?? string.Empty;
                ConfigDico["gameport"] = config.GamePort ?? string.Empty;
                ConfigDico["version"] = config.Version ?? string.Empty;
                ConfigDico["coresize"] = config.Coresize ?? string.Empty;
                ConfigDico["loadersize"] = config.Loadersize ?? string.Empty;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        static bool IsValidJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                JObject.Parse(json);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        public static string SearchConfig(string key)
        {
            return ConfigDico.TryGetValue(key, out string value) ? value : string.Empty;
        }
    }
}
