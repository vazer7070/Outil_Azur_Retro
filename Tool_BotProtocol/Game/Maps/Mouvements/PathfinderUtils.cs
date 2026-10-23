using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Tool_BotProtocol.Utils.Crypto;

namespace Tool_BotProtocol.Game.Maps.Mouvements
{
    /// <summary>
    /// Codage des chemins du client 1.34 (<c>ank.battlefield.utils.Compressor</c>) : chaque point d'inflexion tient en trois
    /// caractères de l'alphabet <c>a–z A–Z 0–9 - _</c> — direction (0 à 7), puis numéro de cellule sur deux caractères
    /// (<c>num / 64</c>, <c>num % 64</c>) — et durées d'animation (<see cref="AnimDuration"/>).
    /// </summary>
    public class PathfinderUtils
    {
        /// <summary>
        /// Durée d'animation du chemin (départ compris) pour un joueur hors combat, au minimum 20 ms : allure et vitesses du
        /// client (<see cref="MoveSpeeds"/>). <paramref name="actualCell"/> est conservé pour compatibilité ; le premier
        /// élément du chemin sert de départ. Pour un autre profil (groupe de monstres, PNJ…), utiliser
        /// <see cref="AnimDuration.Compute"/> avec <see cref="MoveProfile.ForActor"/>.
        /// </summary>
        public static int GetTimeOnMap(Cell actualCell, List<Cell> CellsHover, bool monture = false)
        {
            if (CellsHover == null || CellsHover.Count == 0) return 20;
            return Math.Max(20, AnimDuration.Compute(CellsHover, MoveProfile.Player(monture)).Total);
        }

        /// <summary>
        /// Chemin compressé envoyé dans <c>GA001</c>, comme <c>compressPath(chemin)</c> du client (<c>makeLightPath</c> sans
        /// la cellule de départ) : pour chaque suite de pas de même direction, la direction et sa dernière cellule. Le serveur
        /// ajoute lui-même <c>a&lt;cellule de départ&gt;</c> devant le chemin qu'il renvoie. Chaîne vide pour moins de deux cellules.
        /// </summary>
        public static string GetCleanRoad(List<Cell> Road) => CompressPath(Road);

        /// <summary>Voir <see cref="GetCleanRoad"/> ; les cellules consécutives doivent être voisines.</summary>
        public static string CompressPath(IList<Cell> road)
        {
            if (road == null || road.Count < 2) return string.Empty;
            var path = new StringBuilder(road.Count * 3);
            int lastDirection = -1;
            for (int i = 1; i < road.Count; i++)
            {
                int direction = MoveSpeeds.DirectionBetween(road[i - 1], road[i]);
                if (direction < 0) throw new ArgumentException("Deux cellules consécutives du chemin ne sont pas voisines.", nameof(road));
                if (i > 1 && direction != lastDirection) AppendPoint(path, lastDirection, road[i - 1].CellID);
                lastDirection = direction;
            }
            AppendPoint(path, lastDirection, road[road.Count - 1].CellID);
            return path.ToString();
        }

        /// <summary>Un point du chemin : direction puis cellule (<c>encode64(dir &amp; 7)</c>, <c>(num &amp; 4032) &gt;&gt; 6</c>, <c>num &amp; 63</c>).</summary>
        public static string EncodePoint(int direction, int cellId)
        {
            var point = new StringBuilder(3);
            AppendPoint(point, direction, cellId);
            return point.ToString();
        }

        private static void AppendPoint(StringBuilder path, int direction, int cellId)
        {
            path.Append(Hash.caracteres_array[direction & 7]);
            path.Append(Hash.caracteres_array[(cellId & 4032) >> 6]);
            path.Append(Hash.caracteres_array[cellId & 63]);
        }

        /// <summary>
        /// Chemin complet, cellule par cellule, d'un chemin compressé dont le premier point est la cellule de départ — le
        /// <c>a&lt;cellule&gt;&lt;chemin&gt;</c> que StarLoco diffuse dans <c>GA&lt;id&gt;;1</c> — comme <c>extractFullPath</c> puis
        /// <c>makeFullPath</c> du client : un point identique au précédent n'ajoute aucun pas (chemin refusé par le serveur,
        /// <c>&lt;orientation&gt;&lt;cellule actuelle&gt;</c>). Null pour une chaîne illisible, une direction au-delà de 7, une
        /// cellule absente de la carte, un point hors de la ligne de sa direction ou plus de 2 × largeur + 1 pas par segment.
        /// </summary>
        public static List<Cell> DecodeServerPath(Map map, string encoded)
        {
            Cell[] cells = map?.MapCells;
            if (cells == null || string.IsNullOrEmpty(encoded) || encoded.Length % 3 != 0
                || encoded.Any(c => Array.IndexOf(Hash.caracteres_array, c) < 0)) return null;
            int width = map.MapWidth;
            int maxSteps = 2 * width + 1;
            var path = new List<Cell>();
            for (int index = 0; index < encoded.Length; index += 3)
            {
                int direction = Array.IndexOf(Hash.caracteres_array, encoded[index]);
                int cellId = ((Array.IndexOf(Hash.caracteres_array, encoded[index + 1]) & 15) << 6)
                    | Array.IndexOf(Hash.caracteres_array, encoded[index + 2]);
                if (direction > 7 || cellId >= cells.Length || cells[cellId] == null) return null;
                Cell target = cells[cellId];
                if (path.Count == 0) { path.Add(target); continue; }
                Cell previous = path[path.Count - 1];
                MoveSpeeds.Offset(direction, out int dx, out int dy);
                int steps = 0;
                while (previous != target)
                {
                    if (++steps > maxSteps || path.Count >= 4096) return null;
                    Cell next = NeighbourOf(cells, width, previous, dx, dy);
                    if (next == null) return null;
                    path.Add(next);
                    previous = next;
                }
            }
            return path;
        }

        /// <summary>Cellule voisine dans la direction (dx, dy) en coordonnées X/Y, ou null hors de la carte.</summary>
        public static Cell NeighbourOf(Cell[] cells, int width, Cell cell, int dx, int dy)
        {
            if (cells == null || cell == null || width < 2) return null;
            int x = cell.X + dx, y = cell.Y + dy;
            int id = x * width + y * (width - 1);
            if (id < 0 || id >= cells.Length) return null;
            Cell next = cells[id];
            return next != null && next.X == x && next.Y == y ? next : null;
        }
    }
}
