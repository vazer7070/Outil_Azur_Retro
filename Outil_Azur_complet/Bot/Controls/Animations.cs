using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Outil_Azur_complet.Bot.Controls
{
    /// <summary>Immutable world-space movement timeline; the view's UI timer only repaints it.</summary>
    internal sealed class Animations : IDisposable
    {
        public int Entites_Id { get; private set; }
        public int StartCellId => cellIds[0];
        public int EndCellId => cellIds[cellIds.Length - 1];
        public AnimationType AnimationType { get; private set; }
        private readonly PointF[] points;
        private readonly short[] cellIds;
        private readonly int[] directions;
        private readonly double[] cumulative;
        private readonly double startedAt, duration;
        private bool disposed;

        public Animations(int id, IEnumerable<short> cells, IEnumerable<PointF> worldPoints,
            IEnumerable<int> orientations, int milliseconds, AnimationType type, double now)
        {
            Entites_Id = id; cellIds = cells.ToArray(); points = worldPoints.ToArray(); directions = orientations.ToArray();
            if (cellIds.Length < 2 || points.Length != cellIds.Length || directions.Length != points.Length - 1)
                throw new ArgumentException("Le chemin visuel est incomplet.");
            AnimationType = type; duration = Math.Max(20, milliseconds); startedAt = now;
            cumulative = new double[points.Length];
            for (int index = 1; index < points.Length; index++)
            {
                double dx = points[index].X - points[index - 1].X, dy = points[index].Y - points[index - 1].Y;
                cumulative[index] = cumulative[index - 1] + Math.Max(.01, Math.Sqrt(dx * dx + dy * dy));
            }
        }

        public bool Matches(IEnumerable<short> ids) => !disposed && cellIds.SequenceEqual(ids);
        public bool TrySample(double now, out Frame frame)
        {
            frame = null;
            if (disposed) return false;
            double progress = Math.Max(0, Math.Min(1, (now - startedAt) / duration));
            double distance = cumulative[cumulative.Length - 1] * progress;
            int segment = 0;
            while (segment < points.Length - 2 && cumulative[segment + 1] <= distance) segment++;
            double part = (distance - cumulative[segment]) / (cumulative[segment + 1] - cumulative[segment]);
            PointF first = points[segment], second = points[segment + 1];
            frame = new Frame
            {
                Position = new PointF((float)(first.X + (second.X - first.X) * part), (float)(first.Y + (second.Y - first.Y) * part)),
                CellId = part >= .5 ? cellIds[segment + 1] : cellIds[segment],
                Direction = directions[segment], Complete = progress >= 1, Progress = progress
            };
            return true;
        }

        public sealed class Frame
        {
            public PointF Position;
            public int CellId, Direction;
            public bool Complete;
            public double Progress;
        }

        public void Dispose() { disposed = true; }
    }
}
