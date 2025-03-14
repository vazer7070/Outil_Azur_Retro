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
            if (M != null && cells == null)
                cells = M.MapCells;

            if (CelluleInitiale == null || CellFinale == null)
                return null;

            List<Cell> CellPermises = new List<Cell>() { CelluleInitiale };

            if (ForbidenCell.Contains(CellFinale))
                ForbidenCell.Remove(CellFinale);

            while (CellPermises.Count > 0)
            {
                Cell Actual = CellPermises.OrderBy(c => c.F).ThenByDescending(c => c.G).FirstOrDefault();

                if (Actual == CellFinale)
                    return GetBackSpace(CelluleInitiale, CellFinale);

                CellPermises.Remove(Actual);
                ForbidenCell.Add(Actual);

                foreach (Cell CS in GetAdjacenteCells(Actual, D))
                {
                    if (ForbidenCell.Contains(CS) || !CS.IsWalkable())
                        continue;

                    int TempG = Actual.G + GetDistanceNodes(CS, Actual, D);

                    if (!CellPermises.Contains(CS))
                        CellPermises.Add(CS);
                    else if (TempG >= CS.G)
                        continue;

                    CS.G = TempG;
                    CS.H = GetDistanceNodes(CS, CellFinale, D);
                    CS.F = CS.G + CS.H;
                    CS.Node = Actual;
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
                return (int)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

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
