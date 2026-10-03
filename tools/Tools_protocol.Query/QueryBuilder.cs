using System;
using System.Collections.Generic;
using System.Text;

namespace Tools_protocol.Query
{
	public static class QueryBuilder
	{
		public static string MultiDeleteQuery(string table, string[] col, string[] value)
        {
			if (col == null) throw new ArgumentNullException(nameof(col));
			if (value == null) throw new ArgumentNullException(nameof(value));
			if (col.Length == 0 || col.Length != value.Length)
				throw new ArgumentException("Chaque colonne doit avoir une valeur.");

			var conditions = new List<string>(col.Length);
			for (int i = 0; i < col.Length; i++)
				conditions.Add($"{QuoteIdentifier(col[i])}={QuoteValue(value[i])}");
			return $"DELETE FROM {QuoteIdentifier(table)} WHERE {string.Join(" AND ", conditions)}";
        }
		public static string DeleteFromQuery(string dest, string quand, string operande_result)
		{
			if (string.IsNullOrEmpty(quand) != string.IsNullOrEmpty(operande_result))
				throw new ArgumentException("La colonne et sa valeur doivent être renseignées ensemble.");
			string query = "DELETE FROM " + QuoteIdentifier(dest);
			return string.IsNullOrEmpty(quand) ? query :
				query + " WHERE " + QuoteIdentifier(quand) + "=" + QuoteValue(operande_result);
		}

		public static string InsertIntoQuery(string table, string[] colums, string[] values, string quand)
		{
			if (colums == null) throw new ArgumentNullException(nameof(colums));
			if (values == null) throw new ArgumentNullException(nameof(values));
			if (values.Length == 0) throw new ArgumentException("Au moins une valeur est requise.", nameof(values));
			if (colums.Length > 0 && !(colums.Length == 1 && colums[0] == "*") &&
				colums.Length != values.Length)
				throw new ArgumentException("Le nombre de colonnes et de valeurs doit être identique.");
			if (!string.IsNullOrEmpty(quand))
				throw new ArgumentException("INSERT INTO ne prend pas de clause WHERE.", nameof(quand));

			var query = new StringBuilder("INSERT INTO ");
			query.Append(QuoteIdentifier(table));
			if (colums.Length > 0 && !(colums.Length == 1 && colums[0] == "*"))
			{
				var names = new List<string>(colums.Length);
				foreach (string column in colums) names.Add(QuoteIdentifier(column));
				query.Append('(').Append(string.Join(",", names)).Append(')');
			}
			var literals = new List<string>(values.Length);
			foreach (string value in values) literals.Add(QuoteValue(value));
			query.Append(" VALUES (").Append(string.Join(",", literals)).Append(')');
			return query.ToString();
		}

		public static string SelectExistQuery(string number, string table, string col, string value, bool upper)
		{
			string selected = number == "*" || int.TryParse(number, out _) ? number : QuoteIdentifier(number);
			string compared = upper ? (value ?? string.Empty).ToUpperInvariant() : value;
			return $"SELECT EXISTS (SELECT {selected} FROM {QuoteIdentifier(table)} " +
				$"WHERE {QuoteIdentifier(col)}={QuoteValue(compared)})";
		}

		public static string SelectFromQuery(string[] subjet, string dest, string quand, string egals)
		{
			if (subjet == null || subjet.Length == 0)
				throw new ArgumentException("Au moins une colonne doit être sélectionnée.", nameof(subjet));
			if (string.IsNullOrEmpty(quand) != string.IsNullOrEmpty(egals))
				throw new ArgumentException("La colonne et sa valeur doivent être renseignées ensemble.");
			string columns = string.Join(",", Array.ConvertAll(subjet,
				column => column == "*" ? "*" : QuoteIdentifier(column)));
			string query = $"SELECT {columns} FROM {QuoteIdentifier(dest)}";
			return string.IsNullOrEmpty(quand) ? query :
				query + " WHERE " + QuoteIdentifier(quand) + "=" + QuoteValue(egals);
		}

		public static string SelectFromQueryAnd(string[] subjet, string dest, string quand1, string egals1, string quand2, string egals2)
		{
			if (subjet == null || subjet.Length == 0)
				throw new ArgumentException("Au moins une colonne doit être sélectionnée.", nameof(subjet));
			string columns = string.Join(",", Array.ConvertAll(subjet,
				column => column == "*" ? "*" : QuoteIdentifier(column)));
			return $"SELECT {columns} FROM {QuoteIdentifier(dest)} WHERE " +
				$"{QuoteIdentifier(quand1)}={QuoteValue(egals1)} AND " +
				$"{QuoteIdentifier(quand2)}={QuoteValue(egals2)}";
		}

		public static string UpdateFromQuery(string table, string voulue, int operation, string result, string quand, string egals)
		{
			if (string.IsNullOrEmpty(quand) != string.IsNullOrEmpty(egals))
				throw new ArgumentException("La colonne et sa valeur doivent être renseignées ensemble.");
			string column = QuoteIdentifier(voulue);
			StringBuilder query = new StringBuilder("UPDATE ");
			query.Append(QuoteIdentifier(table));
			query.Append(" SET ");
			query.Append(column);
			switch (operation)
			{
				case 1: query.Append('='); break;
				case 2: query.Append('=').Append(column).Append('+'); break;
				case 3: query.Append('=').Append(column).Append('-'); break;
				case 4: query.Append('=').Append(column).Append('*'); break;
				case 5: query.Append('=').Append(column).Append('/'); break;
				default: throw new ArgumentOutOfRangeException(nameof(operation));
			}
			query.Append(QuoteValue(result));
			if (!string.IsNullOrEmpty(quand))
				query.Append(" WHERE ").Append(QuoteIdentifier(quand)).Append('=').Append(QuoteValue(egals));
			return query.ToString();
		}

		public static string UpdateMultipleFromQuery(string table, string[] col, int opesolo, string[] args, string where, string egals)
		{
			if (col == null) throw new ArgumentNullException(nameof(col));
			if (args == null) throw new ArgumentNullException(nameof(args));
			if (col.Length == 0 || col.Length != args.Length)
				throw new ArgumentException("Chaque colonne doit avoir une valeur.");
			if (string.IsNullOrEmpty(where) || string.IsNullOrEmpty(egals))
				throw new ArgumentException("Une condition WHERE est requise pour modifier des lignes.");

			string operation;
			switch (opesolo)
			{
				case 1: operation = null; break;
				case 2: operation = "+"; break;
				case 3: operation = "-"; break;
				case 4: operation = "*"; break;
				case 5: operation = "/"; break;
				default: throw new ArgumentOutOfRangeException(nameof(opesolo));
			}

			var assignments = new List<string>(col.Length);
			for (int i = 0; i < col.Length; i++)
			{
				string name = QuoteIdentifier(col[i]);
				string expression = operation == null ? QuoteValue(args[i]) : name + operation + QuoteValue(args[i]);
				assignments.Add(name + "=" + expression);
			}
			return $"UPDATE {QuoteIdentifier(table)} SET {string.Join(",", assignments)} WHERE {QuoteIdentifier(where)}={QuoteValue(egals)}";
		}

		private static string QuoteIdentifier(string identifier)
		{
			if (string.IsNullOrEmpty(identifier) ||
				!((identifier[0] >= 'A' && identifier[0] <= 'Z') ||
				  (identifier[0] >= 'a' && identifier[0] <= 'z') || identifier[0] == '_'))
				throw new ArgumentException("Nom de table ou de colonne invalide.", nameof(identifier));
			foreach (char c in identifier)
			{
				if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
				      (c >= '0' && c <= '9') || c == '_'))
					throw new ArgumentException("Nom de table ou de colonne invalide.", nameof(identifier));
			}
			return "`" + identifier + "`";
		}

		private static string QuoteValue(string value)
		{
			return "'" + (value ?? string.Empty).Replace("\\", "\\\\").Replace("'", "''") + "'";
		}
	}
}
