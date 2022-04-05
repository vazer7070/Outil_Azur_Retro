using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Tools_protocol.Json;
using Tools_protocol.Query;

namespace Tools_protocol.Kryone.Database
{
	public class AccountList
	{
		public static Dictionary<string, AccountList> AllAccount = new Dictionary<string, AccountList>();

		private static string hashing;

		public string Account { get; set; }

		public sbyte Banned { get; set; }

		public uint Guid { get; set; }

		public string lastIp { get; set; }

		public int Logged { get; set; }

		public string Pass { get; set; }

		public int Points { get; set; }

		public string Pseudo { get; set; }

		public string Question { get; set; }

		public string Reponse { get; set; }

		public static string TableCompte
		{
			get
			{
				return JsonManager.SearchAuth("comptes");
			}
		}

		public int Vip { get; set; }
		public static int AccountListCount { get; set; }


		public AccountList(IDataReader reader)
		{
			this.Guid = (uint)reader["guid"];
			this.Account = (string)reader["account"];
			this.Pass = (string)reader["pass"];
			this.Banned = (sbyte)reader["banned"];
			this.Pseudo = (string)reader["pseudo"];
			this.Question = (string)reader["question"];
			this.Reponse = (string)reader["reponse"];
			this.lastIp = (string)reader["lastIp"];
			this.Vip = (int)reader["vip"];
			this.Points = (int)reader["points"];
			this.Logged = (int)reader["logged"];
		}

		public static void AllAccounts()
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
						AllAccount.Add(Ac.Account, Ac);
						if (!CharacterList.IdCompte.ContainsKey((int)Ac.Guid))
						{
							CharacterList.IdCompte.Add(Convert.ToInt32(Ac.Guid), Ac.Account);
						}
						AccountListCount = AllAccount.Count;
					}
					lecteur.Close();
					lecteur.Dispose();
					connection.Close();
					connection.Dispose();
				}
				catch (MySqlException) { }
			}
		}

		public static void CreateAccount(string compte, int hash, string mdp, string question, string reponse)
		{
			string[] col = new string[] { "account", "pass", "question", "reponse" };
			string[] val = new string[] { compte, AccountList.GetHash(hash, mdp), question, reponse };
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
		public static AccountList ReturnById(int id)
        {
			return AllAccount.FirstOrDefault(x => x.Value.Guid == id).Value;
        }
		public static AccountList Informations(string account)
		{
			if(AllAccount.TryGetValue(account, out AccountList result))
				return result;
			return null;
			
		}
		public static void ModifyAccount(AccountList acc, string key, string changed, int sw)
        {
            switch (sw)
            {
				case 1:
					AllAccount.Remove(key);
					acc.Account = changed;
					AllAccount.Add(changed, acc);
					break;
			    case 2:
					AllAccount.Remove(key);
					acc.Pseudo = changed;
					AllAccount.Add(key, acc);
					break;
				case 3:
					AllAccount.Remove(key);
					acc.Pass = changed;
					AllAccount.Add(key, acc);
					break;
				case 4:
					AllAccount.Remove(key);
					acc.Question = changed;
					AllAccount.Add(key, acc);
					break;
				case 5:
					AllAccount.Remove(key);
					acc.Reponse = changed;
					AllAccount.Add(key, acc);
					break;
				case 6:
					AllAccount.Remove(key);
					acc.Points = int.Parse(changed);
					AllAccount.Add(key, acc);
					break;
				case 7:
					AllAccount.Remove(key);
					acc.lastIp = changed;
					AllAccount.Add(key, acc);
					break;
				case 8:
					if(changed == "0")
						changed = "1";
					else
						changed = "0";
					AllAccount.Remove(key);
					acc.Vip = int.Parse(changed);
					AllAccount.Add(key, acc);
					break;
				case 9:
					if(changed=="0")
						changed="1";
					else
						changed="0";
					AllAccount.Remove(key);
					acc.Banned = sbyte.Parse(changed);
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