using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Tools_protocol.Json;
using Tools_protocol.Query;

namespace Tools_protocol.Kryone.Database
{
	public class AccountList
	{
		public static List<string> Accounts;

		private static string hashing;

		public string Account
		{
			get;
			set;
		}

		public sbyte Banned
		{
			get;
			set;
		}

		public uint Guid
		{
			get;
			set;
		}

		public string lastIp
		{
			get;
			set;
		}

		public int Logged
		{
			get;
			set;
		}

		public string Pass
		{
			get;
			set;
		}

		public int Points
		{
			get;
			set;
		}

		public string Pseudo
		{
			get;
			set;
		}

		public string Question
		{
			get;
			set;
		}

		public string Reponse
		{
			get;
			set;
		}

		public static string TableCompte
		{
			get
			{
				return JsonManager.SearchAuth("comptes");
			}
		}

		public int Vip
		{
			get;
			set;
		}

		static AccountList()
		{
			AccountList.Accounts = new List<string>();
		}

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
			string[] args = new string[] { "guid", "account" };
			MySqlDataReader lecteur = DatabaseManager.SelectQuery(QueryBuilder.SelectFromQuery(args, AccountList.TableCompte, "", ""));
			while (lecteur.Read())
			{
				AccountList.Accounts.Add(lecteur["account"].ToString());
				if (!CharacterList.IdCompte.ContainsKey(Convert.ToInt32(lecteur["guid"])))
				{
					CharacterList.IdCompte.Add(Convert.ToInt32(lecteur["guid"]), lecteur["account"].ToString());
				}
			}
			lecteur.Close();
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

		public static AccountList IdInformations(uint id)
		{
			AccountList comptes = null;
			string[] args = new string[] { "*" };
			MySqlDataReader read = DatabaseManager.SelectQuery(QueryBuilder.SelectFromQuery(args, AccountList.TableCompte, "guid", id.ToString()));
			while (read.Read())
			{
				comptes = new AccountList(read);
			}
			read.Close();
			return comptes;
		}

		public static AccountList Informations(string account)
		{
			AccountList comptes = null;
			string[] args = new string[] { "*" };
			MySqlDataReader read = DatabaseManager.SelectQuery(QueryBuilder.SelectFromQuery(args, AccountList.TableCompte, "account", account));
			while (read.Read())
			{
				comptes = new AccountList(read);
			}
			read.Close();
			return comptes;
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

		public static void UpdateBan(string compte, int actuel)
		{
			if (!actuel.Equals(1))
			{
				DatabaseManager.UpdateQuery(QueryBuilder.UpdateFromQuery(AccountList.TableCompte, "banned", 1, "1", "account", compte));
			}
			else
			{
				DatabaseManager.UpdateQuery(QueryBuilder.UpdateFromQuery(AccountList.TableCompte, "banned", 1, "0", "account", compte));
			}
		}

		public static void UpdateVip(string compte, int actuel)
		{
			if (!actuel.Equals(1))
			{
				DatabaseManager.UpdateQuery(QueryBuilder.UpdateFromQuery(AccountList.TableCompte, "vip", 1, "1", "account", compte));
			}
			else
			{
				DatabaseManager.UpdateQuery(QueryBuilder.UpdateFromQuery(AccountList.TableCompte, "vip", 1, "0", "account", compte));
			}
		}
	}
}