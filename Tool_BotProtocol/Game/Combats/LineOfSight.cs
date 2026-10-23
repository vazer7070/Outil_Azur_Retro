using System;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Utils.Crypto;

namespace Tool_BotProtocol.Game.Combats
{
    /// <summary>
    /// Ligne de vue d'un sort, calculée comme <c>checkView</c> et <c>checkCellView</c> du client 1.34
    /// (<c>ank.battlefield.utils.Pathfinding</c>), que le client appelle sur chaque cellule survolée ou relâchée en visée
    /// (<c>checkCanLaunchSpellOnCell</c>) sans rien envoyer quand elle échoue :
    /// <list type="bullet">
    /// <item>les deux extrémités sont prises au centre de leur cellule, à la hauteur <c>getCellHeight</c> (niveau du sol − 7,
    /// plus 0,5 sur une pente), plus 1,5 si un combattant s'y tient et encore 1,5 s'il en porte un autre ;</item>
    /// <item>le segment est parcouru colonne par colonne (x), en prenant chaque ligne (y) qu'il traverse dans la colonne ;</item>
    /// <item>une cellule traversée bloque si elle est hors du terrain, inactive, sans ligne de vue (<c>Cell.LineofSight</c>,
    /// tenu à jour par <c>GDC</c>), plus haute que le segment à cet endroit, ou occupée par un combattant (sauf la cellule
    /// du lanceur et la cible) ;</item>
    /// <item>la cible passe par le même contrôle, sauf pour le combattant qui s'y tient : une cellule cible inactive ou
    /// sans ligne de vue (mur, pilier) ne peut donc pas être visée.</item>
    /// </list>
    /// Les arrondis sont ceux de Flash (<c>Math.round(v)</c> = <c>floor(v + 0,5)</c>) et les comparaisons gardent le sens
    /// du client pour les valeurs non numériques (<c>!(hauteur &gt; z)</c>).
    /// </summary>
    public static class LineOfSight
    {
        /// <summary>Hauteur ajoutée par un combattant posé sur une extrémité (et encore une fois s'il en porte un).</summary>
        public const double SpriteHeight = 1.5;
        // Garde-fou : une carte compte au plus 4096 cellules, donc au plus quelques dizaines de colonnes et de lignes.
        private const int MaxSteps = 4096;

        /// <summary>
        /// Vrai si la cible <paramref name="to"/> est vue depuis <paramref name="from"/>. <paramref name="occupants"/> donne,
        /// pour une cellule, 0 si elle est libre, 1 si un combattant s'y tient, 2 s'il porte en plus un autre combattant
        /// (<c>spriteOnID</c> et <c>carriedSpriteOnId</c> du client) ; null : aucune cellule occupée.
        /// </summary>
        public static bool Check(Map map, int from, int to, Func<int, int> occupants)
        {
            // Le client n'appelle checkView que sur des cellules de la carte ; une extrémité inconnue n'est jamais vue.
            if (map == null || map.MapCells == null || map.MapWidth < 2) return false;
            if (from < 0 || to < 0 || from >= map.MapCells.Length || to >= map.MapCells.Length) return false;
            int width = map.MapWidth;
            Func<int, int> occupied = occupants ?? (cell => 0);
            Point source = Coordinates(width, from), target = Coordinates(width, to);
            source.Z = Height(map, from) + SpriteHeight * Math.Min(2, Math.Max(0, occupied(from)));
            target.Z = Height(map, to) + SpriteHeight * Math.Min(2, Math.Max(0, occupied(to)));
            var line = new Segment { Map = map, Width = width, Source = source, Target = target, Occupants = occupied,
                Rise = target.Z - source.Z, Length = Math.Max(Math.Abs(source.Y - target.Y), Math.Abs(source.X - target.X)) };
            double slope = (double)(source.Y - target.Y) / (source.X - target.X);
            double offset = source.Y - slope * source.X;
            int stepX = target.X - source.X < 0 ? -1 : 1;
            int stepY = target.Y - source.Y < 0 ? -1 : 1;
            double row = source.Y;
            double column = source.X + 0.5 * stepX;
            int guard = 0;
            while (!(column * stepX > target.X * stepX))
            {
                double y = slope * column + offset;
                double next, last;
                if (stepY > 0) { next = FlashRound(y); last = Math.Ceiling(y - 0.5); }
                else { next = Math.Ceiling(y - 0.5); last = FlashRound(y); }
                for (double cell = row; !(cell * stepY > last * stepY); cell += stepY)
                {
                    if (++guard > MaxSteps) return false;
                    if (!line.CellView(column - stepX / 2.0, cell, false)) return false;
                }
                row = next;
                column += stepX;
            }
            double current = row;
            for (; !(current * stepY > target.Y * stepY); current += stepY)
            {
                if (++guard > MaxSteps) return false;
                if (!line.CellView(column - 0.5 * stepX, current, false)) return false;
            }
            return line.CellView(column - 0.5 * stepX, current - stepY, true);
        }

        /// <summary><c>Math.round</c> de Flash : moitié arrondie vers +∞ (et non à l'entier pair comme <see cref="Math.Round(double)"/>).</summary>
        public static double FlashRound(double value) => Math.Floor(value + 0.5);

        /// <summary>Hauteur de la cellule comme <c>getCellHeight</c> : niveau − 7, plus 0,5 sur une pente ; 0 hors de la carte.</summary>
        public static double Height(Map map, int cellId)
        {
            CellFacts facts;
            if (!Read(map, cellId, out facts)) return 0;
            return facts.Level - 7 + (facts.Slope == 1 ? 0 : 0.5);
        }

        private struct Point { public int X, Y; public double Z; }

        private struct CellFacts { public bool Active, Sight; public int Level, Slope; }

        private sealed class Segment
        {
            public Map Map;
            public int Width;
            public Point Source, Target;
            public Func<int, int> Occupants;
            public double Rise;
            public double Length;

            /// <summary><c>checkCellView</c> : la cellule (x, y) laisse-t-elle passer la vue ? <paramref name="last"/> : cible.</summary>
            public bool CellView(double x, double y, bool last)
            {
                // getCaseNum : une coordonnée hors de la grille peut tomber sur une autre cellule, comme dans le client.
                double number = x * Width + y * (Width - 1);
                double distance = Math.Max(Math.Abs(Source.Y - y), Math.Abs(Source.X - x));
                double z = distance / Length * Rise + Source.Z;
                CellFacts facts = default(CellFacts);
                bool known = number >= 0 && number <= short.MaxValue && number == Math.Floor(number) && Read(Map, (int)number, out facts);
                if (!known) return last;
                double height = facts.Level - 7 + (facts.Slope == 1 ? 0 : 0.5);
                bool sprite = !(distance == 0 || last || (Target.X == x && Target.Y == y)) && Occupants((int)number) > 0;
                if (facts.Sight && facts.Active && !(height > z) && !sprite) return true;
                return last;
            }
        }

        private static Point Coordinates(int width, int cellId)
        {
            int period = width * 2 - 1;
            int line = cellId / period;
            int y = line - (cellId - line * period) % width;
            return new Point { X = (cellId - (width - 1) * y) / width, Y = y };
        }

        /// <summary>
        /// Champs de la cellule utiles à la vue : activité, niveau et pente pris dans ses dix caractères effectifs
        /// (<see cref="Map.CellDataOf"/>, <c>GDC</c> compris), ligne de vue dans <see cref="Cell.LineofSight"/>.
        /// </summary>
        private static bool Read(Map map, int cellId, out CellFacts facts)
        {
            facts = default(CellFacts);
            if (map?.MapCells == null || cellId < 0 || cellId >= map.MapCells.Length) return false;
            Cell cell = map.MapCells[cellId];
            if (cell == null) return false;
            facts.Active = cell.IsActive; facts.Sight = cell.LineofSight;
            facts.Level = cell.layer_ground_Level; facts.Slope = cell.layer_ground_slope;
            string data = map.CellDataOf(cellId);
            if (data != null && data.Length == 10)
            {
                int v0 = Index(data[0]), v1 = Index(data[1]), v4 = Index(data[4]);
                if (v0 >= 0 && v1 >= 0 && v4 >= 0)
                { facts.Active = (v0 & 32) != 0; facts.Level = v1 & 15; facts.Slope = (v4 & 60) >> 2; }
            }
            return true;
        }

        private static int Index(char value) => Array.IndexOf(Hash.caracteres_array, value);
    }
}
