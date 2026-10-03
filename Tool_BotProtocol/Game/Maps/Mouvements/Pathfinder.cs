using System;
using System.Collections.Generic;
using System.Linq;
using Tool_BotProtocol.Game.Maps;

namespace Tool_BotProtocol.Game.Maps.Mouvements
{
    public class Pathfinder : IDisposable
    {
        private Cell[] cells;
        private Map map;

        public void SetMap(Map M)
        {
            map = M;
            cells = M.MapCells;
        }

        public List<Cell> GetPath(Cell CelluleInitiale, Cell CellFinale, List<Cell> ForbidenCell, bool D, byte distance, Map M = null)
        {
            if (M != null) SetMap(M);

            if (cells == null || CelluleInitiale == null || CellFinale == null)
                return null;
            var forbidden = new HashSet<Cell>(ForbidenCell ?? new List<Cell>());
            var closed = new HashSet<Cell>();
            var costs = new Dictionary<Cell, int> { { CelluleInitiale, 0 } };
            var parents = new Dictionary<Cell, Cell>();
            var open = new List<Cell> { CelluleInitiale };
            while (open.Count > 0)
            {
                Cell actual = open.OrderBy(c => costs[c] + GetDistanceNodes(c, CellFinale, D)).First();
                if (actual == CellFinale || (distance > 0 && actual.GetDistanceBetweenCells(CellFinale) <= distance))
                {
                    var path = new List<Cell> { actual };
                    while (parents.TryGetValue(actual, out Cell previous)) { path.Add(previous); actual = previous; }
                    path.Reverse();
                    return path;
                }
                open.Remove(actual);
                closed.Add(actual);
                foreach (Cell next in GetAdjacenteCells(actual, D))
                {
                    if (closed.Contains(next) || forbidden.Contains(next) || !next.IsWalkable()) continue;
                    int cost = costs[actual] + 1;
                    if (costs.TryGetValue(next, out int known) && cost >= known) continue;
                    costs[next] = cost;
                    parents[next] = actual;
                    if (!open.Contains(next)) open.Add(next);
                }
            }

            return null;
        }

        private List<Cell> GetBackSpace(Cell InitNode, Cell FinalNode)
        {
            Cell NodeActual = FinalNode;
            List<Cell> Backcells = new List<Cell>();

            while (NodeActual != InitNode)
            {
                Backcells.Add(NodeActual);
                NodeActual = NodeActual.Node;
            }

            Backcells.Add(InitNode);
            Backcells.Reverse();
            return Backcells;
        }

        public List<Cell> GetAdjacenteCells(Cell Node, bool D)
        {
            List<Cell> Acells = new List<Cell>();

            Cell Right_Cell = cells.FirstOrDefault(n => n.X == Node.X + 1 && n.Y == Node.Y);
            Cell Left_Cell = cells.FirstOrDefault(n => n.X == Node.X - 1 && n.Y == Node.Y);
            Cell DownCell = cells.FirstOrDefault(n => n.X == Node.X && n.Y == Node.Y + 1);
            Cell UpCell = cells.FirstOrDefault(m => m.X == Node.X && m.Y == Node.Y - 1);

            if (Right_Cell != null)
                Acells.Add(Right_Cell);
            if (Left_Cell != null)
                Acells.Add(Left_Cell);
            if (DownCell != null)
                Acells.Add(DownCell);
            if (UpCell != null)
                Acells.Add(UpCell);

            if (!D)
                return Acells;

            Cell SupLeft = cells.FirstOrDefault(n => n.X == Node.X - 1 && n.Y == Node.Y - 1);
            Cell InfRight = cells.FirstOrDefault(n => n.X == Node.X + 1 && n.Y == Node.Y + 1);
            Cell InfLeft = cells.FirstOrDefault(n => n.X == Node.X - 1 && n.Y == Node.Y + 1);
            Cell SupRight = cells.FirstOrDefault(n => n.X == Node.X + 1 && n.Y == Node.Y - 1);

            if (SupLeft != null)
                Acells.Add(SupLeft);
            if (InfRight != null)
                Acells.Add(InfRight);
            if (InfLeft != null)
                Acells.Add(InfLeft);
            if (SupRight != null)
                Acells.Add(SupRight);

            return Acells;
        }

        private int GetDistanceNodes(Cell a, Cell b, bool useDiag)
        {
            if (useDiag)
                return Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

            return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
        }

        public void Dispose() => Dispose(true);

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                cells = null;
                map = null;
            }
        }
    }
}
