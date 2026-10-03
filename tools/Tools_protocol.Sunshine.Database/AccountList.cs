using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tools_protocol.Json;
using Tools_protocol.Kryone.Database;
using Tools_protocol.Managers;
using Tools_protocol.Query;

namespace Tools_protocol.Sunshine.Database
{
    [EmuManager("Sunshine", "AccountList")]
    public  class AccountList
    {
        public  Dictionary<string, AccountList> AllAccount = new Dictionary<string, AccountList>();

        private static string hashing;

        public int Id { get; set; }

        public string Username { get; set; }

       public string Password { get; set; }

        public string Nickname { get; set; }

        public int Role { get; set; }

        public string SecretQuestion { get; set; }

        public string SecretAnswer { get; set; }

        public byte IsBanned { get; set; }

        public string Ticket { get; set; }

        public static string TableCompte
        {
            get
            {
                return JsonManager.SearchAuth("comptes");
            }
        }
        public AccountList()
        {
            
        }
        public AccountList(IDataReader reader)
        {
            this.Id = reader["id"] != DBNull.Value ? Convert.ToInt32(reader["id"]) : 0;
            this.Username = reader["Username"] != DBNull.Value ? reader["Username"].ToString() : "";
            this.Password = reader["Password"] != DBNull.Value ? reader["Password"].ToString() : "";
            this.Nickname = reader["Nickname"] != DBNull.Value ? reader["Nickname"].ToString() : "";
            this.Role = reader["Role"] != DBNull.Value ? Convert.ToInt32(reader["Role"]) : 0;
            this.SecretQuestion = reader["SecretQuestion"] != DBNull.Value ? reader["SecretQuestion"].ToString() : "";
            this.SecretAnswer = reader["SecretAnswer"] != DBNull.Value ? reader["SecretAnswer"].ToString() : "";
            this.IsBanned = reader["IsBanned"] != DBNull.Value ? Convert.ToByte(reader["IsBanned"]) : (byte)0;
            this.Ticket = reader["Ticket"] != DBNull.Value ? reader["Ticket"].ToString() : "";
        }
        public void AllAccounts()
        {
            
            string[] args = new string[] { "*" };
            string query = QueryBuilder.SelectFromQuery(args, AccountList.TableCompte, "", "");
            using (MySqlConnection connection = new MySqlConnection(DatabaseManager.ConnectionString))
            {
                try
                {
                    AccountList Ac = null;
                    connection.Open();
                    MySqlDataReader lecteur = new MySqlCommand(query, connection).ExecuteReader();
                    while (lecteur.Read())
                    {
                        Ac = new AccountList(lecteur);
                        AllAccount.Add(Ac.Username, Ac);
                        if (!CharacterList.IdCompte.ContainsKey((int)Ac.Id))
                        {
                            CharacterList.IdCompte.Add(Convert.ToInt32(Ac.Id), Ac.Username);
                        }
                    }
                    
                        lecteur.Close();
                    lecteur.Dispose();
                    connection.Close();
                    connection.Dispose();
                }
                catch (MySqlException) { }
            }
        }

        public  void CreateAccount(string compte, int hash, string mdp, string question, string reponse)
        {

            string Hmdp = GetHash(hash, mdp);
            string[] col = new string[] { "Username", "Password", "SecretQuestion", "SecretAnswer" };
            string[] val = new string[] { compte, Hmdp, question, reponse };
            AccountList A = new AccountList()
            {
                Username = compte,
                Password = Hmdp,
                SecretQuestion = question,
                SecretAnswer = reponse
            };
            AllAccount.Add(compte, A);
            DatabaseManager.UpdateQuery(QueryBuilder.InsertIntoQuery(AccountList.TableCompte, col, val, ""));
        }

        private static string GetHash(int hash, string mdp)
        {
            switch (hash)
            {
                case 0:
                    {
                        hashing = mdp;
                        break;
                    }
                case 1:
                    {
                        hashing = GetMd5Hash(MD5.Create(), mdp);
                        break;
                    }
                case 2:
                    {
                        hashing = SHA51(mdp);
                        break;
                    }
            }
            return hashing;
        }

        private static string GetMd5Hash(MD5 md5Hash, string input)
        {
            byte[] data = md5Hash.ComputeHash(Encoding.UTF8.GetBytes(input));
            StringBuilder sBuilder = new StringBuilder();
            for (int i = 0; i < (int)data.Length; i++)
            {
                sBuilder.Append(data[i].ToString("x2"));
            }
            return sBuilder.ToString();
        }
        public  AccountList ReturnById(int id)
        {
            return AllAccount.FirstOrDefault(x => x.Value.Id == id).Value;
        }
        public  AccountList Informations(string account)
        {
            if (AllAccount.TryGetValue(account, out AccountList result))
                return result;
            return null;

        }
        public  void ModifyAccount(AccountList acc, string key, string changed, int sw)
        {
            switch (sw)
            {
                case 1:
                    AllAccount.Remove(key);
                    acc.Username = changed;
                    AllAccount.Add(changed, acc);
                    break;
               case 2:
                    AllAccount.Remove(key);
                    acc.Nickname = changed;
                    AllAccount.Add(key, acc);
                    break;
                case 3:
                    AllAccount.Remove(key);
                    acc.Password = changed;
                    AllAccount.Add(key, acc);
                    break;
                case 4:
                    AllAccount.Remove(key);
                    acc.SecretQuestion = changed;
                    AllAccount.Add(key, acc);
                    break;
                case 5:
                    AllAccount.Remove(key);
                    acc.SecretAnswer = changed;
                    AllAccount.Add(key, acc);
                    break;
                case 6:
                   
                    break;
                case 7:
                    break;
                case 8:
                    break;
                case 9:
                    if (changed == "0")
                        changed = "1";
                    else
                        changed = "0";
                    AllAccount.Remove(key);
                    acc.IsBanned = byte.Parse(changed);
                    AllAccount.Add(key, acc);
                    break;
            }
        }
        public static string SHA51(string input)
        {
            string str;
            byte[] bytes = Encoding.UTF8.GetBytes(input);
            using (SHA512 hash = SHA512.Create())
            {
                byte[] hashedInputBytes = hash.ComputeHash(bytes);
                StringBuilder hashedInputStringBuilder = new StringBuilder(128);
                byte[] numArray = hashedInputBytes;
                for (int i = 0; i < (int)numArray.Length; i++)
                {
                    byte b = numArray[i];
                    hashedInputStringBuilder.Append(b.ToString("X2"));
                }
                str = hashedInputStringBuilder.ToString();
            }
            return str;
        }
    }
}
