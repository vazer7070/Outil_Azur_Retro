using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static Tool_BotProtocol.Json.Config;

namespace Tool_BotProtocol.Config
{
    public class GlobalConfig
    {
		static string COnfigPath = @".\ressources\Bot\BotConfig.json";
		static JObject DefaultConfig = new JObject(new JProperty("Config", new JObject(new object[] { new JProperty("IP", "127.0.0.1"), new JProperty("AuthPort", "450"), new JProperty("GamePort", "5555"), new JProperty("Version", "1.34.1")})));
		static Dictionary<string, string> ConfigDico = new Dictionary<string,string>();
		public const string BOTVERSION = "N/A";
		public static string IP => SearchConfig("IP");
		public static string AUTHPORT => SearchConfig("authport");
		public static string GAMEPORT => SearchConfig("gameport");
		public static string VERSION => SearchConfig("version");
		public static bool BYPASS = false;
		public static void InitializeConfig()
        {
            if (!File.Exists(COnfigPath))
            {
				WriteFile(COnfigPath);
				LectConfig(COnfigPath);
            }
            else
            {
				LectConfig(COnfigPath);
            }
        }
		public static void writenewconfig(string ip, string ap, string gp, string version)
        {
			if(File.Exists(COnfigPath))
				File.Delete(COnfigPath);
			JObject newConfig = new JObject(new JProperty("Config", new JObject(new object[] { new JProperty("IP", ip), new JProperty("AuthPort", ap), new JProperty("GamePort", gp), new JProperty("Version", version) })));
			JObject Parse = JObject.Parse(newConfig.ToString());
			File.WriteAllText(COnfigPath, Parse.ToString());
			ConfigDico.Clear();
			LectConfig(COnfigPath);
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
				string lec = File.ReadAllText(path);
				Json.Config.ConfigEntries config = JsonConvert.DeserializeObject<ConfigEntries.DataEntries>(lec).ConfigEntries;
				ConfigDico.Add("IP", config.IP);
				ConfigDico.Add("authport", config.AuthPort);
				ConfigDico.Add("gameport", config.GamePort);
				ConfigDico.Add("version", config.Version);
            }
            catch (Exception ex)
            {
				MessageBox.Show(ex.Message);
				return;
			}
        }
		public static string SearchConfig(string key)
        {
			return ConfigDico.FirstOrDefault(x => x.Key == key).Value;
        }
	}
}
