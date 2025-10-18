using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace Tool_Bot.Config
{
    public class GlobalConfig
    {
		static JObject DefaultConfig = new JObject(new JProperty("Config", new JObject(new object[] { new JProperty("IP", "127.0.0.1"), new JProperty("AuthPort", "450"), new JProperty("GamePort", "5555"), new JProperty("Version", "1.34.1")})));
                static readonly Dictionary<string, string> ConfigDico = new Dictionary<string,string>();

		public static string IP = "";
		public static string AUTHPORT = "";
		public static string GAMEPORT = "";
		public static string VERSION = "";
                public static void InitializeConfig(string configPath)
        {
                        if (!File.Exists(configPath))
                        {
                                WriteFile(configPath);
                        }

                        LectConfig(configPath);
        }
                public static bool Verif_Json(string json)
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
                        catch
                        {
                                return false;
                        }
                }
		static void WriteFile(string path)
        {
			JObject Parse = JObject.Parse(DefaultConfig.ToString());
			File.WriteAllText(path, Parse.ToString());
        }
                public static void LectConfig(string path)
        {
                        try
                        {
                                string fileContent = File.ReadAllText(path);
                                if (!Verif_Json(fileContent))
                                {
                                        WriteFile(path);
                                        fileContent = File.ReadAllText(path);
                                }

                                Json.Config.ConfigEntries.DataEntries data = JsonConvert.DeserializeObject<Json.Config.ConfigEntries.DataEntries>(fileContent);
                                if (data?.ConfigEntries == null)
                                {
                                        throw new InvalidDataException("Configuration file does not contain valid entries.");
                                }

                                Json.Config.ConfigEntries config = data.ConfigEntries;

                                ConfigDico.Clear();
                                ConfigDico["IP"] = config.IP;
                                ConfigDico["authport"] = config.AuthPort;
                                ConfigDico["gameport"] = config.GamePort;
                                ConfigDico["version"] = config.Version;

                                IP = config.IP;
                                AUTHPORT = config.AuthPort;
                                GAMEPORT = config.GamePort;
                                VERSION = config.Version;
                        }
                        catch (Exception ex)
                        {
                                MessageBox.Show(ex.Message);
                        }
        }
	}
}
