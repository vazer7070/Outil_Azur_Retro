using System;
using System.Collections.Generic;
using System.Text;

namespace Tools_protocol.Query
{
	public static class QueryBuilder
	{
		static List<string> InsertValues;

		public static string MultiDeleteQuery(string table, string[] col, string[] value)
        {
			List<string> S = new List<string>();
			StringBuilder query = new StringBuilder("DELETE FROM ");
			query.Append($"'{table}'");
			query.Append(" WHERE ");
			
				for(int i = 0; i == col.Length; i++)
                {
					S.Add($"'{col[i]}' = '{value[i]}'");
                }
				query.Append(String.Join(" AND ", S));
            
			return query.ToString();
        }
        public static string DeleteFromQuery(string dest, string quand, string operande_result)
		{
			string str;
			StringBuilder query = new StringBuilder("DELETE FROM ");
			query.Append(dest ?? "");
			if (!string.IsNullOrEmpty(quand))
			{
				query.Append(" WHERE ");
				query.Append(quand ?? "");
				query.Append("=");
				query.Append(string.Concat("'", operande_result, "'"));
				str = query.ToString();
			}
			else
			{
				str = query.ToString();
			}
			return str;
		}

		public static string InsertIntoQuery(string table, string[] colums, string[] values, string quand)
		{
			StringBuilder query = new StringBuilder("INSERT INTO ");
			query.Append(table);
			string test = string.Join("", colums);
			if(test != "*")
            {
				if (colums.Length.Equals(1))
				{
					string array = string.Join("", colums);
					query.Append(string.Concat("(", array, ")"));
				}
				else if (colums.Length >= 2)
				{
					string array = string.Join(",", colums);
					query.Append(string.Concat("(", array, ")"));
				}
			}
			query.Append("VALUES");
			if (values.Length.Equals(1))
			{
				string arrayvalues = string.Join("", values);
				query.Append(string.Concat("('", arrayvalues, "')"));
			}
			else if (values.Length >= 2)
			{
                InsertValues = new List<string>();
				string[] strArrays = values;
				for (int num = 0; num < (int)strArrays.Length; num++)
				{
					string i = strArrays[num];
                    InsertValues.Add(string.Concat("'", i, "'"));
				}
				string arrayvalues = string.Join(",", InsertValues.ToArray());
				query.Append(string.Concat("(", arrayvalues, ")"));
			}
			if (!string.IsNullOrEmpty(quand))
			{
				query.Append("WHERE");
				query.Append(quand);
			}
            InsertValues.Clear();
			return query.ToString();
		}

		public static string SelectExistQuery(string number, string table, string col, string value, bool upper)
		{
			StringBuilder query = new StringBuilder("SELECT EXISTS (SELECT ");
			query.Append(number);
			query.Append(" FROM ");
			query.Append(table);
			query.Append(" WHERE ");
			query.Append(col);
			query.Append('=');
			if (!upper)
			{
				query.Append(string.Concat("'", value, "')"));
			}
			else
			{
				query.Append(string.Concat("'", value.ToUpper(), "')"));
			}
			return query.ToString();
		}

		public static string SelectFromQuery(string[] subjet, string dest, string quand, string egals)
		{
			string str;
			StringBuilder query = new StringBuilder("SELECT ");
			if (subjet.Length.Equals(1))
			{
				query.Append(string.Join("", subjet));
			}
			else if ((int)subjet.Length >= 2)
			{
				query.Append(string.Join(",", subjet));
			}
			query.Append(" FROM ");
			query.Append(dest);
			if ((string.IsNullOrEmpty(quand) ? false : !string.IsNullOrEmpty(egals)))
			{
				query.Append(" WHERE ");
				query.Append(quand);
				query.Append('=');
				query.Append(string.Concat("'", egals, "'"));
				str = query.ToString();
			}
			else
			{
				str = query.ToString();
			}
			return str;
		}

		public static string SelectFromQueryAnd(string[] subjet, string dest, string quand1, string egals1, string quand2, string egals2)
		{
			StringBuilder query = new StringBuilder("SELECT ");
			if (subjet.Length.Equals(1))
			{
				query.Append(string.Join("", subjet));
			}
			else if ((int)subjet.Length >= 2)
			{
				query.Append(string.Join(",", subjet));
			}
			query.Append(" FROM ");
			query.Append(dest);
			query.Append(" WHERE ");
			query.Append(quand1);
			query.Append('=');
			query.Append(string.Concat("'", egals1, "'"));
			query.Append(" AND ");
			query.Append(quand2);
			query.Append('=');
			query.Append(string.Concat("'", egals2, "'"));
			return query.ToString();
		}

		public static string UpdateFromQuery(string table, string voulue, int operation, string result, string quand, string egals)
		{
			string str;
			StringBuilder query = new StringBuilder("UPDATE ");
			query.Append(table);
			query.Append(" SET ");
			query.Append(voulue);
			switch (operation)
			{
				case 1:
				{
					query.Append('=');
					break;
				}
				case 2:
				{
					query.Append('=');
					query.Append(voulue);
					query.Append('+');
					break;
				}
				case 3:
				{
					query.Append('=');
					query.Append(voulue);
					query.Append('-');
					break;
				}
				case 4:
				{
					query.Append('=');
					query.Append(voulue);
					query.Append('*');
					break;
				}
				case 5:
				{
					query.Append('=');
					query.Append(voulue);
					query.Append('/');
					break;
				}
			}
			query.Append(string.Concat("'", result, "'"));
			if (!string.IsNullOrEmpty(quand) && !string.IsNullOrEmpty(egals))
			{
				query.Append(" WHERE ");
				query.Append(quand);
				query.Append('=');
				query.Append(string.Concat("'", egals, "'"));
				str = query.ToString();
			}
			else
			{
				str = query.ToString();
			}
			return str;
		}

		public static string UpdateMultipleFromQuery(string table, string[] col, int opesolo, string[] args, string where, string egals)
		{
			string str;
			List<string> Col = new List<string>();
			List<string> Args = new List<string>();
			List<string> resultofparse = new List<string>();
			StringBuilder query = new StringBuilder("UPDATE ");
			query.Append(table);
			query.Append(" SET");
			if (col.Length.Equals(1) && args.Length.Equals(1))
			{
				string i = string.Join("", col);
				string j = string.Join("", args);
				query.Append(i);
				switch (opesolo)
				{
					case 1:
					{
						query.Append("=");
						break;
					}
					case 2:
					{
						query.Append("=");
						query.Append(i);
						query.Append("+");
						break;
					}
					case 3:
					{
						query.Append("=");
						query.Append(i);
						query.Append("-");
						break;
					}
					case 4:
					{
						query.Append("=");
						query.Append(i);
						query.Append("*");
						break;
					}
					case 5:
					{
						query.Append("=");
						query.Append(i);
						query.Append("/");
						break;
					}
				}
				query.Append(string.Concat("'", j, "'"));
			}
			else if (((int)col.Length < 2 || (int)args.Length < 2 ? false : (int)col.Length == (int)args.Length))
			{
				for (int o = 0; o == (int)col.Length; o++)
				{
					if (!Col.Contains(col[0]))
					{
						Col.Add(col[0]);
					}
					if (!Args.Contains(args[0]))
					{
						Args.Add(args[0]);
					}
					Col.Add(col[o]);
					Args.Add(string.Concat("'", args[o], "'"));
				}
				string[] colonnes = Col.ToArray();
				string[] arguments = Args.ToArray();
				for (int u = 0; u == (int)colonnes.Length; u++)
				{
					if (!resultofparse.Contains(colonnes[0]))
					{
						resultofparse.Add(string.Concat(colonnes[0], "=", arguments[0]));
					}
					resultofparse.Add(string.Concat(colonnes[u], "=", arguments[u]));
				}
				string[] parsing = resultofparse.ToArray();
				query.Append(string.Join(",", parsing));
			}
			if (!string.IsNullOrEmpty(egals) && !string.IsNullOrEmpty(where))
			{
				query.Append(" WHERE ");
				query.Append(where);
				query.Append("=");
				query.Append(string.Concat("'", egals, "'"));
				str = query.ToString();
			}
			else
			{
				str = query.ToString();
			}
			return str;
		}
	}
}