using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Tool_Bot.Config
{
    public class GlobalConfig
    {
		static JObject DefaultConfig = new JObject(new JProperty("Config", new JObject(new object[] { new JProperty("IP", "127.0.0.1"), new JProperty("AuthPort", "450"), new JProperty("GamePort", "5555"), new JProperty("Version", "1.34.1")})));
		static Dictionary<string, string> ConfigDico = new Dictionary<string,string>();

		public static string IP = "";
		public static string AUTHPORT = "";
		public static string GAMEPORT = "";
		public static string VERSION = "";
		public static void InitializeConfig(string configPath)
        {
            if (!File.Exists(configPath))
            {
				WriteFile(configPath);
				LectConfig(configPath);
            }
            else
            {
				LectConfig(configPath);
            }
        }
		public static bool Verif_Json(string json)
		{
			bool flag;
			try
			{
				JObject.Parse(json);
				flag = true;
			}
			catch
			{
				flag = false;
			}
			return flag;
		}
		static void WriteFile(string path)
        {
			JObject Parse = JObject.Parse(DefaultConfig.ToString());
			File.WriteAllText(path, Parse.ToString());
        }
		public static void LectConfig(string path)
        {
            if (!Verif_Json(path))
            {
				WriteFile(path);
				LectConfig(path);
            }
            try
            {
				string lec = File.ReadAllText(path);
				Json.Config.ConfigEntries config = JsonConvert.DeserializeObject<Json.Config.ConfigEntries.DataEntries>(lec).ConfigEntries;
				ConfigDico.Add("IP", config.IP);
				ConfigDico.Add("authport", config.AuthPort);
				ConfigDico.Add("gameport", config.GamePort);
				ConfigDico.Add("version", config.Version);
				IP = config.IP;
				AUTHPORT = config.AuthPort;
				GAMEPORT = config.GamePort;
				VERSION = config.Version;
            }
            catch (Exception ex)
            {
				MessageBox.Show(ex.Message);
				return;
			}
        }
	}
}
