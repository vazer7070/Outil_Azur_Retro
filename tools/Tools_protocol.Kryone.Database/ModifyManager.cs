using MySql.Data.MySqlClient;
using System;
using System.Linq;
using Tools_protocol.Json;
using Tools_protocol.Query;

namespace Tools_protocol.Kryone.Database
{
	public class ModifyManager
	{
		public static string TablePerso
		{
			get
			{
				return JsonManager.SearchAuth("perso");
			}
		}

		public ModifyManager()
		{
		}

		public static string ModifyAccepted(string col, int count, string sujet, string result, string ID)
		{
			string str;
			if (sujet.Count<char>() <= count)
			{
				string[] args = new string[] { col };
				DatabaseManager.UpdateQuery(QueryBuilder.UpdateFromQuery(ModifyManager.TablePerso, sujet, 1, result, "id", ID));
				MySqlDataReader lec = DatabaseManager.SelectQuery(QueryBuilder.SelectFromQuery(args, ModifyManager.TablePerso, "id", ID));
				if (lec.Read())
				{
					str = lec.GetString(col);
					return str;
				}
				lec.Close();
				lec.Dispose();
			}
			str = result;
			return str;
		}
	}
}