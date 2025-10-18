using System;
using System.Collections.Generic;
using System.Text;

namespace Tools_protocol.Query
{
	public static class QueryBuilder
	{
                public static string MultiDeleteQuery(string table, string[] col, string[] value)
                {
                        if (string.IsNullOrWhiteSpace(table))
                        {
                                throw new ArgumentException("Table name must be provided.", nameof(table));
                        }

                        if (col == null)
                        {
                                throw new ArgumentNullException(nameof(col));
                        }

                        if (value == null)
                        {
                                throw new ArgumentNullException(nameof(value));
                        }

                        if (col.Length != value.Length)
                        {
                                throw new ArgumentException("The number of columns must match the number of values.");
                        }

                        StringBuilder query = new StringBuilder("DELETE FROM ");
                        query.Append(table);

                        if (col.Length > 0)
                        {
                                List<string> conditions = new List<string>(col.Length);
                                for (int i = 0; i < col.Length; i++)
                                {
                                        conditions.Add(string.Concat(col[i], "='", value[i], "'"));
                                }

                                query.Append(" WHERE ");
                                query.Append(string.Join(" AND ", conditions));
                        }

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
                        if (string.IsNullOrWhiteSpace(table))
                        {
                                throw new ArgumentException("Table name must be provided.", nameof(table));
                        }

                        if (colums == null)
                        {
                                throw new ArgumentNullException(nameof(colums));
                        }

                        if (values == null)
                        {
                                throw new ArgumentNullException(nameof(values));
                        }

                        if (values.Length == 0)
                        {
                                throw new ArgumentException("At least one value must be provided.", nameof(values));
                        }

                        bool usesWildcardColumn = colums.Length == 1 && colums[0] == "*";
                        if (!usesWildcardColumn && colums.Length != values.Length)
                        {
                                throw new ArgumentException("The number of columns must match the number of values.");
                        }

                        StringBuilder query = new StringBuilder("INSERT INTO ");
                        query.Append(table);

                        if (!usesWildcardColumn && colums.Length > 0)
                        {
                                query.Append(' ');
                                query.Append('(');
                                query.Append(string.Join(",", colums));
                                query.Append(')');
                        }

                        query.Append(" VALUES ");

                        if (values.Length == 1)
                        {
                                query.Append("('");
                                query.Append(values[0]);
                                query.Append("')");
                        }
                        else
                        {
                                List<string> insertValues = new List<string>(values.Length);
                                for (int i = 0; i < values.Length; i++)
                                {
                                        insertValues.Add(string.Concat("'", values[i], "'"));
                                }

                                query.Append('(');
                                query.Append(string.Join(",", insertValues));
                                query.Append(')');
                        }

                        if (!string.IsNullOrWhiteSpace(quand))
                        {
                                query.Append(" WHERE ");
                                query.Append(quand);
                        }

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
                        if (col == null)
                        {
                                throw new ArgumentNullException(nameof(col));
                        }

                        if (args == null)
                        {
                                throw new ArgumentNullException(nameof(args));
                        }

                        if (col.Length != args.Length)
                        {
                                throw new ArgumentException("The number of columns must match the number of values.");
                        }

                        StringBuilder query = new StringBuilder("UPDATE ");
                        query.Append(table);
                        query.Append(" SET ");

                        List<string> assignments = new List<string>();

                        for (int i = 0; i < col.Length; i++)
                        {
                                string column = col[i];
                                string value = args[i];

                                if (col.Length == 1)
                                {
                                        string operation = "=";
                                        switch (opesolo)
                                        {
                                                case 2:
                                                        operation = $"={column}+";
                                                        break;
                                                case 3:
                                                        operation = $"={column}-";
                                                        break;
                                                case 4:
                                                        operation = $"={column}*";
                                                        break;
                                                case 5:
                                                        operation = $"={column}/";
                                                        break;
                                        }

                                        if (opesolo == 1 || opesolo < 1 || opesolo > 5)
                                        {
                                                assignments.Add(string.Concat(column, "='", value, "'"));
                                        }
                                        else
                                        {
                                                assignments.Add(string.Concat(column, operation, "'", value, "'"));
                                        }
                                }
                                else
                                {
                                        assignments.Add(string.Concat(column, "='", value, "'"));
                                }
                        }

                        query.Append(string.Join(",", assignments));

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
