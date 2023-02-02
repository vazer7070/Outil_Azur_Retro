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
        public string Lieu { get; set; }
        public int Server_id = 0;
        public List<string> Servers { get; set; }
        public List<string> Perso_names_and_levels;

        static string comptePath = @".\ressources\Bot\AccountSingle\";
        public static List<AccountConfig> AccountsActive = new List<AccountConfig>();
        public AccountConfig(string account, string pass, string lieu)
        {
            Account = account;
            Password = pass;
            Perso_names_and_levels = new List<string>();
            Servers = new List<string>();
            Lieu = lieu;
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
                        AccountConfig A = new AccountConfig(accounts.Compte, accounts.MDP, accounts.Lieu);
                        AccountsDico.Add(accounts.Compte, A);

                    }
                    catch
                    {
                    }
                }
            }
        }
        public static void WriteCompte(string a, string mdp, string lieu)
        {
            JObject newAccount = new JObject(new JProperty("Comptes", new JObject(new object[] { new JProperty("Compte", a), new JProperty("MDP", mdp), new JProperty("Lieu", lieu) })));
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
