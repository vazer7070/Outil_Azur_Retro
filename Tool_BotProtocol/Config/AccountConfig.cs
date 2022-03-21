using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Tool_BotProtocol.Config
{
    public class AccountConfig
    {
        public static Dictionary<string, AccountConfig> AccountsDico = new Dictionary<string, AccountConfig>();
        public  string Account {get; set;}
        public  string Password { get; set; }
        public  string Server { get; set;}
        public  string player { get; set; }

        static string comptePath = @".\ressources\Bot\AccountSingle\";
        public static List<AccountConfig> AccountsActive = new List<AccountConfig>();
        public AccountConfig(string account, string pass, string server, string perso)
        {
            Account = account;
            Password = pass;
            Server = server;
            player = perso;
        }
        public static AccountConfig ReturnAccountInfo(string key)
        {
            return AccountsDico[key];    
        }
        public static void LoadAccount()
        {
            string[] FileArray = Directory.GetFiles(comptePath);
            if(FileArray.Length != 0)
            {
                foreach (string json in FileArray)
                {
                    try
                    {

                        string lec = File.ReadAllText(json);
                        Json.AccountsJson.AccountEntries accounts = JsonConvert.DeserializeObject<Json.AccountsJson.DataEntries>(lec).accountEntries;
                        AccountConfig A = new AccountConfig(accounts.Compte, accounts.MDP, accounts.Serveur, accounts.Perso);
                        AccountsDico.Add(accounts.Compte, A);

                    }
                    catch(Exception e)
                    {
                        MessageBox.Show(e.Message);
                        return;
                    }
                }
            }
        }
        public static void WriteCompte(string a, string mdp, string s, string pla)
        {
            JObject newAccount = new JObject(new JProperty("Comptes", new JObject(new object[] { new JProperty("Compte", a), new JProperty("MDP", mdp), new JProperty("Serveur", s), new JProperty("Perso", pla) })));
            JObject Parse = JObject.Parse(newAccount.ToString());
            File.WriteAllText($"{comptePath}{a}.json", Parse.ToString());
        }
        public static void DeleteCompte(string c)
        {
            string path = $"{comptePath}{c}.json";
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            AccountsDico.Remove(c);
        }
        
    }
}
