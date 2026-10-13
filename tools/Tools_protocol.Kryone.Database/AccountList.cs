using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Tools_protocol.Emulators;
using Tools_protocol.Managers;
using Tools_protocol.Query;

namespace Tools_protocol.Kryone.Database
{
    [EmuManager("Kryone", "AccountList")]
    public class AccountList
	{
		public static Dictionary<string, AccountList> AllAccount = new Dictionary<string, AccountList>();


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

		/// <summary>Table des comptes selon le profil d'émulateur courant.</summary>
		public static string TableCompte
		{
			get
			{
				return EmulatorRegistry.Current.Table("comptes");
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
		public AccountList()
        {

        }
		public static void AllAccounts()
		{
			string[] args = new string[] { "*" };
			string query = QueryBuilder.SelectFromQuery(args, AccountList.TableCompte, "", "");
			var accounts = new Dictionary<string, AccountList>(StringComparer.OrdinalIgnoreCase);
			var namesById = new Dictionary<int, string>();
			using (var connection = new MySqlConnection(EmulatorRegistry.ConnectionFor("comptes")))
			using (var command = new MySqlCommand(query, connection))
			{
				connection.Open();
				using (var reader = command.ExecuteReader())
				{
					while (reader.Read())
					{
						var account = new AccountList(reader);
						accounts.Add(account.Account, account);
						namesById.Add(Convert.ToInt32(account.Guid), account.Account);
					}
				}
			}
			AllAccount = accounts;
			CharacterList.IdCompte = namesById;
			AccountListCount = accounts.Count;
		}

		public static void CreateAccount(string compte, int hash, string mdp, string question, string reponse)
		{
			string tableName = TableCompte;
			if (!QueryBuilder.IsIdentifier(tableName))
				throw new InvalidOperationException("Le nom de la table des comptes est invalide.");

			var knownValues = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
			{
				["account"] = compte,
				["pass"] = GetHash(hash, mdp),
				["question"] = question,
				["reponse"] = reponse,
				["pseudo"] = compte,
				["lastConnectionDate"] = string.Empty,
				["lastIP"] = string.Empty,
				["friends"] = string.Empty,
				["enemy"] = string.Empty,
				["heurevote"] = 0L,
				["lastConnectDay"] = string.Empty,
				["pass_no_crypt"] = string.Empty,
				["banRaison"] = string.Empty,
				["dateRegister"] = DateTime.Today.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
			};
			AccountList account;
			using (var connection = new MySqlConnection(EmulatorRegistry.ConnectionFor("comptes")))
			{
				connection.Open();
				using (var engineCommand = new MySqlCommand(
					"SET SESSION sql_mode=CONCAT_WS(',',NULLIF(@@SESSION.sql_mode,''),'STRICT_ALL_TABLES')", connection))
					engineCommand.ExecuteNonQuery();
				using (var engineCommand = new MySqlCommand(
					"SELECT ENGINE FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@table", connection))
				{
					engineCommand.Parameters.AddWithValue("@table", tableName);
					string engine = Convert.ToString(engineCommand.ExecuteScalar());
					if (!string.Equals(engine, "InnoDB", StringComparison.OrdinalIgnoreCase))
						throw new InvalidOperationException($"La création d'un compte exige InnoDB pour {tableName} (actuel : {(string.IsNullOrEmpty(engine) ? "absent" : engine)}).");
				}
				using (var transaction = connection.BeginTransaction())
				{
					var values = new List<KeyValuePair<string, object>>();
					var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
					using (var schema = new MySqlCommand($"SHOW COLUMNS FROM `{tableName}`", connection, transaction))
					using (var reader = schema.ExecuteReader())
					{
						while (reader.Read())
						{
							string name = Convert.ToString(reader["Field"]);
							columns.Add(name);
							string extra = Convert.ToString(reader["Extra"]);
							if (extra.IndexOf("auto_increment", StringComparison.OrdinalIgnoreCase) >= 0 ||
								extra.IndexOf("VIRTUAL GENERATED", StringComparison.OrdinalIgnoreCase) >= 0 ||
								extra.IndexOf("STORED GENERATED", StringComparison.OrdinalIgnoreCase) >= 0)
								continue;
							if (knownValues.TryGetValue(name, out object value))
							{
								Match limit = Regex.Match(Convert.ToString(reader["Type"]), @"\A(?:var)?char\((\d+)\)", RegexOptions.IgnoreCase);
								if (value is string text && limit.Success && text.Length > int.Parse(limit.Groups[1].Value, CultureInfo.InvariantCulture))
									throw new FormatException($"Le champ {name} accepte au maximum {limit.Groups[1].Value} caractères dans cette base.");
								values.Add(new KeyValuePair<string, object>(name, value));
							}
							else if (string.Equals(Convert.ToString(reader["Null"]), "NO", StringComparison.OrdinalIgnoreCase) &&
								reader["Default"] == DBNull.Value)
								throw new InvalidOperationException($"La table des comptes exige une valeur pour le champ inconnu « {name} ». Adaptez la configuration du schéma avant de créer un compte.");
						}
					}
					foreach (string required in new[] { "guid", "account", "pass", "question", "reponse" })
						if (!columns.Contains(required))
							throw new InvalidOperationException($"Le champ « {required} » est absent de la table des comptes.");

					string query = $"INSERT INTO `{tableName}` (" +
						string.Join(",", values.Select(entry => "`" + entry.Key.Replace("`", "``") + "`")) + ") VALUES (" +
						string.Join(",", values.Select((entry, index) => "@value" + index)) + ")";
					long insertedId;
					using (var command = new MySqlCommand(query, connection, transaction))
					{
						for (int index = 0; index < values.Count; index++)
							command.Parameters.AddWithValue("@value" + index, values[index].Value ?? DBNull.Value);
						if (command.ExecuteNonQuery() != 1)
							throw new InvalidOperationException("La création du compte n'a pas été enregistrée.");
						insertedId = command.LastInsertedId;
					}
					if (insertedId <= 0 || insertedId > uint.MaxValue)
						throw new InvalidOperationException("L'identifiant du compte créé est invalide.");
					using (var command = new MySqlCommand($"SELECT * FROM `{tableName}` WHERE `guid`=@id", connection, transaction))
					{
						command.Parameters.AddWithValue("@id", insertedId);
						using (var reader = command.ExecuteReader())
						{
							if (!reader.Read()) throw new InvalidOperationException("Le compte créé ne peut pas être relu.");
							account = new AccountList(reader);
						}
					}
					transaction.Commit();
				}
			}
			AllAccount[account.Account] = account;
			if (account.Guid > 0 && account.Guid <= int.MaxValue)
				CharacterList.IdCompte[(int)account.Guid] = account.Account;
			AccountListCount = AllAccount.Count;
		}

		private static string GetHash(int hash, string mdp)
		{
			switch (hash)
			{
				case 0:
					return mdp;
				case 1:
					using (var md5 = MD5.Create())
						return GetMd5Hash(md5, mdp);
				case 2:
					return SHA51(mdp);
				default:
					throw new ArgumentOutOfRangeException(nameof(hash));
			}
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
