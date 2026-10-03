using System;
using System.IO;
using System.Text;
using Tools_protocol.Query;

namespace Outil_Azur_complet.maps
{
    internal static class MapActionSqlBuilder
    {
        internal static string[] BuildTrigger(string table, int sourceMap, int sourceCell, int targetMap, int targetCell)
        {
            Validate(sourceMap, sourceCell, targetMap, targetCell);
            return new[]
            {
                QueryBuilder.MultiDeleteQuery(table, new[] { "MapID", "CellID", "ActionID", "EventID", "Conditions" },
                    new[] { sourceMap.ToString(), sourceCell.ToString(), "0", "1", "-1" }),
                QueryBuilder.InsertIntoQuery(table, new[] { "MapID", "CellID", "ActionID", "EventID", "ActionsArgs", "Conditions" },
                    new[] { sourceMap.ToString(), sourceCell.ToString(), "0", "1", $"{targetMap},{targetCell}", "-1" }, "")
            };
        }

        internal static string[] BuildEndFight(string table, int sourceMap, int targetMap, int targetCell)
        {
            Validate(sourceMap, 0, targetMap, targetCell);
            return new[]
            {
                QueryBuilder.MultiDeleteQuery(table, new[] { "map", "fighttype", "action", "cond" },
                    new[] { sourceMap.ToString(), "4", "0", "" }),
                QueryBuilder.InsertIntoQuery(table, new[] { "map", "args", "fighttype", "action", "cond" },
                    new[] { sourceMap.ToString(), $"{targetMap},{targetCell}", "4", "0", "" }, "")
            };
        }

        private static void Validate(int sourceMap, int sourceCell, int targetMap, int targetCell)
        {
            if (sourceMap <= 0 || targetMap <= 0 || sourceCell < 0 || targetCell < 0)
                throw new ArgumentOutOfRangeException("Les cartes et cellules de téléportation sont invalides.");
            if (sourceMap == targetMap && sourceCell == targetCell)
                throw new InvalidOperationException("La cellule de départ et d'arrivée doit être différente.");
        }

        internal static string Export(string folder, string prefix, string[] queries)
        {
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, prefix + "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") +
                "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".sql");
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                foreach (string query in queries) writer.WriteLine(query.TrimEnd(';') + ";");
            return path;
        }
    }
}
