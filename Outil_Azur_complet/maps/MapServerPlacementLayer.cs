using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using Tool_Editor.maps.data;

namespace Outil_Azur_complet.maps
{
    // Server placements are an optional, read-only layer. They never enter AME/SWF exports.
    public sealed class MapServerPlacementLayer
    {
        public sealed class Marker
        {
            public ServerResourceKind Kind { get; internal set; }
            public int CellId { get; internal set; }
            public string Description { get; internal set; }
        }

        private static readonly ServerResourceKind[] Kinds =
        {
            ServerResourceKind.Npcs, ServerResourceKind.MonsterGroups,
            ServerResourceKind.Zaaps, ServerResourceKind.Paddocks
        };
        private readonly Dictionary<int, List<Marker>> byCell = new Dictionary<int, List<Marker>>();
        private readonly Dictionary<ServerResourceKind, int> counts = new Dictionary<ServerResourceKind, int>();
        public int MapId { get; private set; }
        public string Notice { get; private set; }
        public int PaddockCount { get { return Count(ServerResourceKind.Paddocks); } }

        private MapServerPlacementLayer(int mapId) { MapId = mapId; }

        public static MapServerPlacementLayer Load(int mapId, int cellCount)
        {
            var tables = new Dictionary<ServerResourceKind, DataTable>();
            var failures = new List<string>();
            foreach (ServerResourceKind kind in Kinds)
            {
                try { tables.Add(kind, ServerDataService.Load(kind, mapId, cellCount).Data); }
                catch (Exception error) { failures.Add(Name(kind) + " : " + error.Message); }
            }
            if (tables.Count == 0)
                throw new InvalidOperationException("Aucun placement serveur n'a pu être chargé. " + string.Join(" ; ", failures));
            var result = FromTables(mapId, cellCount, tables);
            result.Notice = failures.Count == 0 ? "" : "Tables indisponibles : " + string.Join(" ; ", failures);
            return result;
        }

        public static MapServerPlacementLayer FromTables(int mapId, int cellCount,
            IDictionary<ServerResourceKind, DataTable> tables)
        {
            if (mapId <= 0 || cellCount <= 0 || tables == null)
                throw new ArgumentException("Carte ou données de placement invalides.");
            var result = new MapServerPlacementLayer(mapId);
            foreach (ServerResourceKind kind in Kinds)
            {
                DataTable table;
                if (!tables.TryGetValue(kind, out table) || table == null) continue;
                foreach (DataRow row in table.Rows)
                {
                    if (row.RowState == DataRowState.Deleted) continue;
                    object rowMap = Field(row, "mapid") ?? Field(row, "map");
                    if (rowMap != null && rowMap != DBNull.Value && Convert.ToInt32(rowMap) != mapId) continue;
                    int cell;
                    if (kind == ServerResourceKind.Paddocks)
                    {
                        result.Increment(kind);
                        // Some Kryone dumps have a cellid; older ones only have mapid.
                        if (!TryCell(row, cellCount, out cell)) continue;
                        result.Add(cell, kind, "Enclos · cellule " + cell);
                        continue;
                    }
                    if (!TryCell(row, cellCount, out cell)) continue;
                    string description = kind == ServerResourceKind.Npcs
                        ? "PNJ #" + Value(row, "npcid") + " · cellule " + cell
                        : kind == ServerResourceKind.MonsterGroups
                            ? "Groupe fixe · cellule " + cell + " · " + Short(Value(row, "groupData"))
                            : "Zaap · cellule " + cell;
                    result.Add(cell, kind, description);
                    result.Increment(kind);
                }
            }
            return result;
        }

        public int Count(ServerResourceKind kind)
        {
            int value;
            return counts.TryGetValue(kind, out value) ? value : 0;
        }

        public IList<Marker> At(int cellId)
        {
            List<Marker> list;
            return byCell.TryGetValue(cellId, out list) ? (IList<Marker>)list.AsReadOnly() : new Marker[0];
        }

        public string Summary
        {
            get
            {
                return Count(ServerResourceKind.Npcs) + " PNJ · " +
                    Count(ServerResourceKind.MonsterGroups) + " groupes · " +
                    Count(ServerResourceKind.Zaaps) + " zaaps · " + PaddockCount + " enclos";
            }
        }

        public string DescribeCell(int cellId, bool paddockCell)
        {
            var lines = At(cellId).Select(marker => marker.Description).ToList();
            if (paddockCell && PaddockCount > 0 && !lines.Any(line => line.StartsWith("Enclos ·")))
                lines.Add("Cellule d'enclos définie dans la carte");
            return string.Join("\n", lines);
        }

        public void Draw(Graphics graphics, CellsData[] cells, int cellSize)
        {
            if (graphics == null || cells == null) return;
            var previous = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            try
            {
                if (PaddockCount > 0)
                    foreach (CellsData cell in cells)
                        if (cell != null && cell.Paddock && cell.Location != null && cell.Location.Length >= 3)
                            using (var pen = new Pen(Color.FromArgb(213, 123, 24), 2))
                                graphics.DrawPolygon(pen, cell.Location);
                foreach (var pair in byCell)
                {
                    if (pair.Key < 0 || pair.Key >= cells.Length) continue;
                    CellsData cell = cells[pair.Key];
                    if (cell == null || cell.Location == null || cell.Location.Length < 4) continue;
                    int x = (cell.Location[0].X + cell.Location[2].X) / 2;
                    int y = (cell.Location[0].Y + cell.Location[2].Y) / 2;
                    int radius = Math.Max(7, Math.Min(11, cellSize / 3));
                    var groups = pair.Value.GroupBy(marker => marker.Kind).ToArray();
                    for (int i = 0; i < groups.Length; i++)
                    {
                        var group = groups[i];
                        int offset = (int)Math.Round((i - (groups.Length - 1) / 2.0) * (radius * 2 + 2));
                        var bounds = new Rectangle(x + offset - radius, y - radius, radius * 2, radius * 2);
                        using (var fill = new SolidBrush(ColorFor(group.Key))) graphics.FillEllipse(fill, bounds);
                        graphics.DrawEllipse(Pens.White, bounds);
                        using (var font = new Font("Segoe UI", Math.Max(7, radius - (group.Count() > 1 ? 3 : 2)), FontStyle.Bold, GraphicsUnit.Pixel))
                        using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                            graphics.DrawString(Initial(group.Key) + (group.Count() > 1 ? group.Count().ToString() : ""), font, Brushes.White, bounds, format);
                    }
                }
            }
            finally { graphics.SmoothingMode = previous; }
        }

        private void Increment(ServerResourceKind kind) { counts[kind] = Count(kind) + 1; }
        private void Add(int cell, ServerResourceKind kind, string description)
        {
            List<Marker> list;
            if (!byCell.TryGetValue(cell, out list)) byCell[cell] = list = new List<Marker>();
            list.Add(new Marker { Kind = kind, CellId = cell, Description = description });
        }
        private static bool TryCell(DataRow row, int cellCount, out int cell)
        {
            cell = -1;
            object value = Field(row, "cellid");
            if (value == null || value == DBNull.Value) return false;
            try { cell = Convert.ToInt32(value); }
            catch (Exception) { return false; }
            return cell >= 0 && cell < cellCount;
        }
        private static object Field(DataRow row, string name)
        {
            DataColumn column = row.Table.Columns.Cast<DataColumn>().FirstOrDefault(
                item => item.ColumnName.Equals(name, StringComparison.OrdinalIgnoreCase));
            return column == null ? null : row[column];
        }
        private static string Value(DataRow row, string name)
        {
            object value = Field(row, name);
            return value == null || value == DBNull.Value ? "?" : Convert.ToString(value);
        }
        private static string Short(string value)
        {
            return value.Length <= 45 ? value : value.Substring(0, 42) + "…";
        }
        private static string Name(ServerResourceKind kind)
        {
            return kind == ServerResourceKind.Npcs ? "PNJ" : kind == ServerResourceKind.MonsterGroups ? "groupes" :
                kind == ServerResourceKind.Zaaps ? "zaaps" : "enclos";
        }
        private static string Initial(ServerResourceKind kind)
        {
            return kind == ServerResourceKind.Npcs ? "P" : kind == ServerResourceKind.MonsterGroups ? "M" :
                kind == ServerResourceKind.Paddocks ? "E" : "Z";
        }
        private static Color ColorFor(ServerResourceKind kind)
        {
            return kind == ServerResourceKind.Npcs ? Color.FromArgb(31, 105, 174) :
                kind == ServerResourceKind.MonsterGroups ? Color.FromArgb(154, 58, 118) :
                kind == ServerResourceKind.Paddocks ? Color.FromArgb(213, 123, 24) : Color.FromArgb(37, 128, 96);
        }
    }
}
