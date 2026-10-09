using System;
using System.Collections.Generic;

namespace Tool_BotProtocol.Game.Maps.Mouvements
{
    /// <summary>
    /// Minutage d'un chemin tel que le client l'anime (<see cref="MoveSpeeds"/>) : allure, durée et direction de chaque pas,
    /// durée totale avant l'acquittement <c>GKK</c>, et cellule « courante » à un instant donné. Le client met à jour la
    /// cellule du sprite au début de chaque pas : pendant un pas, la cellule courante est donc celle vers laquelle il marche.
    /// </summary>
    public sealed class AnimDuration
    {
        private readonly int[] steps;
        private readonly int[] directions;
        private readonly long[] stepStarts;

        private AnimDuration(MoveMode mode, int[] steps, int[] directions)
        {
            Mode = mode;
            this.steps = steps;
            this.directions = directions;
            stepStarts = new long[steps.Length];
            long total = 0;
            for (int i = 0; i < steps.Length; i++) { stepStarts[i] = total; total += steps[i]; }
            Total = (int)Math.Min(int.MaxValue, total);
        }

        public static AnimDuration Empty { get; } = new AnimDuration(MoveMode.Walk, new int[0], new int[0]);

        public MoveMode Mode { get; }
        /// <summary>Durée de chaque pas en millisecondes (un de moins que de cellules dans le chemin).</summary>
        public IReadOnlyList<int> Steps => steps;
        /// <summary>Direction (0 à 7) de chaque pas, -1 pour deux cellules qui ne sont pas voisines.</summary>
        public IReadOnlyList<int> Directions => directions;
        /// <summary>Durée totale en millisecondes : délai du client entre le <c>GA</c> reçu et son <c>GKK</c>.</summary>
        public int Total { get; }
        public int StepCount => steps.Length;
        /// <summary>Direction du dernier pas (orientation finale du sprite), -1 sans pas.</summary>
        public int FinalDirection => directions.Length == 0 ? -1 : directions[directions.Length - 1];

        /// <summary>Minutage du chemin <paramref name="path"/> (départ compris) pour le profil donné.</summary>
        public static AnimDuration Compute(IList<Cell> path, MoveProfile profile)
        {
            if (path == null || path.Count < 2) return Empty;
            profile = profile ?? MoveProfile.Player();
            MoveMode mode = MoveSpeeds.ChooseMode(path.Count, profile);
            double moderator = profile.EffectiveModerator;
            var durations = new int[path.Count - 1];
            var headings = new int[path.Count - 1];
            for (int i = 1; i < path.Count; i++)
            {
                headings[i - 1] = MoveSpeeds.DirectionBetween(path[i - 1], path[i]);
                durations[i - 1] = MoveSpeeds.StepDuration(path[i - 1], path[i], mode, moderator);
            }
            return new AnimDuration(mode, durations, headings);
        }

        /// <summary>
        /// Indice, dans le chemin, de la cellule du sprite après <paramref name="elapsedMilliseconds"/> : la destination du
        /// pas en cours (0 sans pas, dernier indice une fois la durée totale écoulée).
        /// </summary>
        public int CellIndexAt(double elapsedMilliseconds)
        {
            if (steps.Length == 0) return 0;
            int index = 1;
            for (int i = 1; i < stepStarts.Length; i++)
            {
                if (stepStarts[i] <= elapsedMilliseconds) index = i + 1;
                else break;
            }
            return index;
        }

        /// <summary>Direction du pas en cours à l'instant donné (orientation affichée du sprite), -1 sans pas.</summary>
        public int DirectionAt(double elapsedMilliseconds)
        {
            if (steps.Length == 0) return -1;
            return directions[CellIndexAt(elapsedMilliseconds) - 1];
        }
    }
}
