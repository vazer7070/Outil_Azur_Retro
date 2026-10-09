using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Tool_BotProtocol.Game.Maps.Mouvements;

namespace Outil_Azur_complet.Bot.Controls
{
    /// <summary>
    /// Chronologie immuable d'un déplacement dans le repère de la carte. Chaque pas dure ce que le client lui donne
    /// (<see cref="AnimDuration"/>, lot M2 : vitesse par direction, pente, marche ou course) ; le sprite avance à vitesse
    /// constante pendant un pas, sa cellule devient celle du pas en cours dès son début (comme le client) et l'image de la
    /// bande <c>walk</c>/<c>run</c> avance à 40 images par seconde. La minuterie de la vue ne fait que redessiner.
    /// </summary>
    internal sealed class Animations : IDisposable
    {
        public int Entites_Id { get; private set; }
        public int StartCellId => cellIds[0];
        public int EndCellId => cellIds[cellIds.Length - 1];
        public AnimationType AnimationType { get; private set; }
        /// <summary>Allure du chemin : bande <c>walk</c> pour la marche, <c>run</c> sinon.</summary>
        public MoveMode Mode { get; private set; }
        public double Duration => stepEnds[stepEnds.Length - 1];
        private readonly PointF[] points;
        private readonly short[] cellIds;
        private readonly int[] directions;
        private readonly double[] stepEnds;
        private readonly double startedAt;
        private bool disposed;

        /// <param name="stepDurations">Durée de chaque pas (ms), un de moins que de cellules.</param>
        public Animations(int id, IEnumerable<short> cells, IEnumerable<PointF> worldPoints, IEnumerable<int> orientations,
            IEnumerable<double> stepDurations, MoveMode mode, AnimationType type, double now)
        {
            Entites_Id = id; cellIds = cells.ToArray(); points = worldPoints.ToArray(); directions = orientations.ToArray();
            double[] steps = stepDurations.ToArray();
            if (cellIds.Length < 2 || points.Length != cellIds.Length || directions.Length != points.Length - 1 || steps.Length != directions.Length)
                throw new ArgumentException("Le chemin visuel est incomplet.");
            AnimationType = type; Mode = mode; startedAt = now;
            stepEnds = new double[steps.Length];
            double total = 0;
            for (int index = 0; index < steps.Length; index++)
            {
                double step = double.IsNaN(steps[index]) || double.IsInfinity(steps[index]) ? 0 : Math.Max(1, steps[index]);
                total += step;
                stepEnds[index] = total;
            }
        }

        /// <summary>
        /// Durées des pas de <paramref name="timing"/> ramenées à <paramref name="total"/> ms (durée imposée par l'appelant,
        /// par exemple l'attente avant <c>GKK</c>) ; pas égaux si le minutage est inconnu.
        /// </summary>
        public static double[] ScaleSteps(AnimDuration timing, int stepCount, int total)
        {
            var steps = new double[Math.Max(0, stepCount)];
            if (steps.Length == 0) return steps;
            double known = timing != null && timing.StepCount == stepCount ? timing.Total : 0;
            double target = total > 0 ? total : known > 0 ? known : 20 * steps.Length;
            for (int index = 0; index < steps.Length; index++)
                steps[index] = known > 0 ? timing.Steps[index] * target / known : target / steps.Length;
            return steps;
        }

        public bool Matches(IEnumerable<short> ids) => !disposed && cellIds.SequenceEqual(ids);

        public bool TrySample(double now, out Frame frame)
        {
            frame = null;
            if (disposed) return false;
            double duration = Duration;
            double elapsed = Math.Max(0, Math.Min(duration, now - startedAt));
            int step = 0;
            while (step < stepEnds.Length - 1 && stepEnds[step] <= elapsed) step++;
            double begin = step == 0 ? 0 : stepEnds[step - 1];
            double length = Math.Max(1e-6, stepEnds[step] - begin);
            double part = Math.Max(0, Math.Min(1, (elapsed - begin) / length));
            PointF first = points[step], second = points[step + 1];
            bool complete = now - startedAt >= duration;
            frame = new Frame
            {
                Position = complete ? points[points.Length - 1]
                    : new PointF((float)(first.X + (second.X - first.X) * part), (float)(first.Y + (second.Y - first.Y) * part)),
                CellId = cellIds[step + 1],
                Direction = directions[step], Complete = complete, Progress = duration <= 0 ? 1 : elapsed / duration,
                Elapsed = Math.Max(0, now - startedAt)
            };
            return true;
        }

        public sealed class Frame
        {
            public PointF Position;
            /// <summary>Cellule du sprite : destination du pas en cours (le client la met à jour au début du pas).</summary>
            public int CellId, Direction;
            public bool Complete;
            public double Progress;
            /// <summary>Temps écoulé depuis le départ (ms), pour l'image de la bande.</summary>
            public double Elapsed;
        }

        public void Dispose() { disposed = true; }
    }
}
