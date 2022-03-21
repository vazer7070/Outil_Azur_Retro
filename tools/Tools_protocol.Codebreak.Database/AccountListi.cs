using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using Tools_protocol.Json;
using Tools_protocol.Query;

namespace Tools_protocol.Codebreak.Database
{
	public class AccountListi
	{
		public static List<AccountListi> AccountsList;

		public static List<string> AccountsName;

		public short Banned
		{
			get;
			set;
		}

		public DateTime CreationDate
		{
			get;
			set;
		}

		public string Email
		{
			get;
			set;
		}

		public int HeureVote
		{
			get;
			set;
		}

		public uint Id
		{
			get;
			set;
		}

		public DateTime LastConnectionDate
		{
			get;
			set;
		}

		public string LastConnectionIp
		{
			get;
			set;
		}

		public string Name
		{
			get;
			set;
		}

		public string Password
		{
			get;
			set;
		}

		public int Points
		{
			get;
			set;
		}

		public int Power
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

		public DateTime RemainingSubscription
		{
			get;
			set;
		}

		public string Reponse
		{
			get;
			set;
		}

		public static string TableAccount
		{
			get
			{
				return JsonManager.SearchAuth("comptes");
			}
		}

		public int VIP
		{
			get;
			set;
		}

		public int Votes
		{
			get;
			set;
		}

		static AccountListi()
		{
			AccountListi.AccountsList = new List<AccountListi>();
			AccountListi.AccountsName = new List<string>();
		}

		public AccountListi(IDataReader reader)
		{
			this.Id = (uint)reader["Id"];
			this.Name = (string)reader["Name"];
			this.Pseudo = (string)reader["Pseudo"];
			this.Password = (string)reader["Password"];
			this.Power = (int)reader["Power"];
			this.VIP = (int)reader["Vip"];
			this.CreationDate = (DateTime)reader["CreationDate"];
			this.LastConnectionDate = (DateTime)reader["LastConnectionDate"];
			this.LastConnectionIp = (string)reader["LastConnectionIp"];
			this.RemainingSubscription = (DateTime)reader["RemainingSubscription"];
			this.Banned = (short)reader["Banned"];
			this.Question = (string)reader["Question"];
			this.Reponse = (string)reader["Reponse"];
			this.Email = (string)reader["Email"];
			this.Votes = (int)reader["Votes"];
			this.HeureVote = (int)reader["HeureVote"];
			this.Points = (int)reader["Points"];
		}

		public static uint AccountId(string name)
		{
			uint id = AccountListi.AccountsList.FirstOrDefault<AccountListi>((AccountListi x) => x.Name == name).Id;
			return id;
		}

		public static string Accountinfos(uint id, int sw)
		{
			return "";
		}

		public static void LoadAccounts()
		{
			AccountListi acc = null;
			MySqlDataReader reader = DatabaseManager.SelectQuery(QueryBuilder.SelectFromQuery(new string[] { "*" }, AccountListi.TableAccount, "", ""));
			while (reader.Read())
			{
				acc = new AccountListi(reader);
				AccountListi.AccountsList.Add(acc);
				AccountListi.AccountsName.Add(acc.Name);
			}
			reader.Close();
			reader.Dispose();
		}
	}
}