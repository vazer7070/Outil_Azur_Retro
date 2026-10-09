using System;
using System.Collections.Generic;
using System.Linq;
using Tool_BotProtocol.Game.Maps.Enums;

namespace Tool_BotProtocol.Game.Maps.Mouvements
{
    /// <summary>Options d'une recherche de chemin, reprises de <c>Pathfinding.pathFind</c> du client 1.34.</summary>
    public sealed class PathRequest
    {
        /// <summary>Huit directions (restriction 8192 <c>canMoveInAllDirections</c> du personnage) ; sinon les quatre
        /// directions impaires, celles des axes de la grille.</summary>
        public bool AllDirections { get; set; }
        /// <summary>Second essai du client hors combat : ignore les sprites et les cellules de changement de carte.</summary>
        public bool IgnoreSprites { get; set; }
        public bool InFight { get; set; }
        /// <summary>Longueur maximale (somme des coûts : 1 par pas impair, 1,5 par pas pair) ; 500 dans le client.</summary>
        public double MaxLength { get; set; } = 500;
        /// <summary>Cellules occupées par d'autres joueurs : bloquent les pas intermédiaires hors combat ; en combat, toute
        /// cellule occupée (destination comprise) est bloquée.</summary>
        public ICollection<int> SpriteCells { get; set; }
        /// <summary>Cellules interdites en toutes circonstances, sauf la destination (usage historique du bot).</summary>
        public ICollection<int> BlockedCells { get; set; }
        /// <summary>
        /// Comme le client : une cellule non praticable mais active (déplacement 0) peut être traversée au prix de +1000 et le
        /// chemin s'arrête juste avant elle. Faux = recherche stricte de l'ancien bot (cellules non praticables exclues).
        /// </summary>
        public bool CrossUnwalkable { get; set; } = true;
        /// <summary>Arrêt dès qu'une cellule est à cette distance (somme des écarts X/Y) de la destination ; 0 = destination exacte.</summary>
        public int StopDistance { get; set; }
        /// <summary>Garde-fou : nombre maximal de cellules développées (0 = huit fois le nombre de cellules).</summary>
        public int NodeBudget { get; set; }
    }

    /// <summary>
    /// A* du client 1.34 sur la grille indexée de la carte, avec file de priorité binaire (O(n log n)) : mêmes voisins
    /// (décalages 1, w, 2w−1, w−1, −1, −w, −2w+1, −w+1), mêmes coûts (1,5 pour les directions paires, 1 pour les impaires,
    /// +0,5 à chaque changement de direction, +(5 − déplacement)/3 qui favorise les chemins), même heuristique (distance
    /// euclidienne en X/Y) et même départage des égalités (dernier nœud ouvert d'abord). Sans état partagé : utilisable
    /// depuis n'importe quel fil, la recherche porte sur un instantané du tableau de cellules.
    /// </summary>
    public class Pathfinder : IDisposable
    {
        /// <summary>Objets de calque qui font d'une cellule un déclencheur (<c>MAP_TRIGGER_LAYEROBJECTS</c> du client).</summary>
        public static readonly short[] TriggerLayerObjects = { 1030, 1029, 4088 };
        /// <summary>Objets de calque infranchissables en cours de chemin (<c>MAP_UNWALKABLE_LAYEROBJECTS</c>).</summary>
        public static readonly short[] UnwalkableLayerObjects = { 7020 };
        private static readonly double[] AllDirectionCosts = { 1.5, 1, 1.5, 1, 1.5, 1, 1.5, 1 };
        private static readonly int[] OddDirections = { 1, 3, 5, 7 };
        private static readonly int[] EveryDirection = { 0, 1, 2, 3, 4, 5, 6, 7 };

        private Cell[] cells;
        private int width;

        public void SetMap(Map M)
        {
            cells = M?.MapCells;
            width = M?.MapWidth ?? 0;
        }

        /// <summary>
        /// Recherche historique du bot (chemin départ compris, null si aucun) : <paramref name="D"/> autorise les huit
        /// directions, <paramref name="distance"/> arrête la recherche à cette distance de la destination et les cellules
        /// interdites ne sont jamais traversées. La recherche est stricte : aucune cellule non praticable n'est traversée.
        /// </summary>
        public List<Cell> GetPath(Cell CelluleInitiale, Cell CellFinale, List<Cell> ForbidenCell, bool D, byte distance, Map M = null)
        {
            if (M != null) SetMap(M);
            var request = new PathRequest
            {
                AllDirections = D,
                IgnoreSprites = true,
                CrossUnwalkable = false,
                StopDistance = distance,
                BlockedCells = ForbidenCell == null ? null : new HashSet<int>(ForbidenCell.Where(cell => cell != null).Select(cell => (int)cell.CellID))
            };
            return FindPath(cells, width, CelluleInitiale, CellFinale, request);
        }

        /// <summary>Cellules voisines en X/Y (axes, puis diagonales si <paramref name="D"/>) de la carte chargée.</summary>
        public List<Cell> GetAdjacenteCells(Cell Node, bool D)
        {
            var neighbours = new List<Cell>();
            if (Node == null || cells == null) return neighbours;
            foreach (int direction in D ? new[] { 1, 5, 3, 7, 6, 2, 4, 0 } : new[] { 1, 5, 3, 7 })
            {
                MoveSpeeds.Offset(direction, out int dx, out int dy);
                Cell next = PathfinderUtils.NeighbourOf(cells, width, Node, dx, dy);
                if (next != null) neighbours.Add(next);
            }
            return neighbours;
        }

        /// <summary>Recherche sur la carte chargée par <see cref="SetMap"/>.</summary>
        public List<Cell> FindPath(Cell start, Cell end, PathRequest request) => FindPath(cells, width, start, end, request);

        /// <summary>
        /// Chemin de <paramref name="start"/> à <paramref name="end"/>, départ compris : null si aucun, un chemin d'une seule
        /// cellule si le premier pas est impossible (chemin tronqué avant une cellule non praticable).
        /// </summary>
        public static List<Cell> FindPath(Cell[] cells, int width, Cell start, Cell end, PathRequest request)
        {
            request = request ?? new PathRequest();
            if (cells == null || width < 2 || start == null || end == null) return null;
            if (!Contains(cells, start) || !Contains(cells, end)) return null;
            if (start.CellID == end.CellID) return new List<Cell> { start };

            int count = cells.Length;
            int[] order = request.AllDirections ? EveryDirection : OddDirections;
            var offsets = new[] { 1, width, width * 2 - 1, width - 1, -1, -width, -width * 2 + 1, -width + 1 };
            bool considerTriggers = !request.IgnoreSprites && !request.InFight;
            int budget = request.NodeBudget > 0 ? request.NodeBudget : Math.Max(64, count * 8);

            var nodes = new Node[count];
            var heap = new NodeHeap();
            long sequence = 0;
            var first = new Node(start.CellID)
            {
                G = 0, V = 0, H = Distance(start, end), Direction = -1, Level = start.layer_ground_Level,
                Movement = (int)start.C_Types, Open = true, Sequence = sequence++
            };
            nodes[start.CellID] = first;
            heap.Push(first, first.H);

            int expanded = 0;
            while (heap.TryPop(out Node current))
            {
                if (!current.Open) continue;
                current.Open = false;
                current.Closed = true;
                Cell here = cells[current.Index];
                if (current.Index == end.CellID
                    || (request.StopDistance > 0 && here.GetDistanceBetweenCells(end) <= request.StopDistance))
                    return Rebuild(cells, start, current, request.CrossUnwalkable);
                if (++expanded > budget) return null;

                for (int slot = 0; slot < order.Length; slot++)
                {
                    // Le client compare l'indice dans sa table de directions (0 à 7, ou 0 à 3 en quatre directions) :
                    // le changement de direction et la parité du surcoût des cellules bloquées portent sur cet indice.
                    int direction = order[slot];
                    double stepCost = request.AllDirections ? AllDirectionCosts[direction] : 1;
                    int index = current.Index + offsets[direction];
                    if (index < 0 || index >= count) continue;
                    Cell next = cells[index];
                    MoveSpeeds.Offset(direction, out int dx, out int dy);
                    // Le client écarte un voisin à plus de 53 px en X (bord de ligne) ; en X/Y, le voisin doit être exact.
                    if (next == null || next.X != here.X + dx || next.Y != here.Y + dy) continue;

                    int movement = (int)next.C_Types;
                    bool isEnd = index == end.CellID;
                    bool endOnObject = isEnd && movement == (int)CellTypes.INTERACTIVE_OBJECT;
                    bool levelOk = Math.Abs(next.layer_ground_Level - current.Level) < 2;
                    if (!levelOk || !next.IsActive || (movement == (int)CellTypes.INTERACTIVE_OBJECT && !endOnObject)) continue;
                    if (movement == (int)CellTypes.NOT_WALKABLE && (!request.CrossUnwalkable || isEnd)) continue;
                    if (!isEnd && request.BlockedCells != null && request.BlockedCells.Contains(index)) continue;
                    if (!request.IgnoreSprites && (request.InFight || !isEnd) && request.SpriteCells != null && request.SpriteCells.Contains(index)) continue;
                    if (considerTriggers && !isEnd && HasLayerObject(next, TriggerLayerObjects)) continue;
                    if (!isEnd && HasLayerObject(next, UnwalkableLayerObjects)) continue;

                    double blocked = movement == (int)CellTypes.NOT_WALKABLE || movement == (int)CellTypes.INTERACTIVE_OBJECT
                        ? 1000 + (slot % 2 == 0 ? 3 : 0) : 0;
                    double turn = endOnObject ? -1000 : slot == current.Direction ? 0 : 0.5;
                    double value = current.V + stepCost + blocked + turn + (5 - movement) / 3.0;
                    double length = current.G + stepCost;
                    if (length > request.MaxLength) continue;

                    Node known = nodes[index];
                    if (known != null && known.V <= value) continue;
                    if (known == null || known.Closed)
                    {
                        // Nouveau nœud ouvert (ou rouvert) : il passe devant les nœuds de même coût déjà ouverts.
                        known = new Node(index) { Sequence = sequence++ };
                        nodes[index] = known;
                    }
                    known.G = length;
                    known.V = value;
                    known.H = Distance(next, end);
                    known.Direction = slot;
                    known.Level = next.layer_ground_Level;
                    known.Movement = movement;
                    known.Parent = current;
                    known.Open = true;
                    known.Closed = false;
                    known.Version++;
                    heap.Push(known, known.V + known.H);
                }
            }
            return null;
        }

        private static bool Contains(Cell[] cells, Cell cell) =>
            cell.CellID >= 0 && cell.CellID < cells.Length && ReferenceEquals(cells[cell.CellID], cell);

        private static bool HasLayerObject(Cell cell, short[] objects) =>
            Array.IndexOf(objects, cell.layer_object_1_num) >= 0 || Array.IndexOf(objects, cell.layer_object_2_num) >= 0;

        private static double Distance(Cell a, Cell b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        // Comme le client : en remontant depuis l'arrivée, chaque cellule non praticable efface ce qui la suit.
        private static List<Cell> Rebuild(Cell[] cells, Cell start, Node last, bool truncate)
        {
            var reversed = new List<Cell>();
            for (Node node = last; node != null && node.Parent != null; node = node.Parent)
            {
                if (truncate && node.Movement == (int)CellTypes.NOT_WALKABLE) { reversed.Clear(); continue; }
                reversed.Add(cells[node.Index]);
            }
            reversed.Add(start);
            reversed.Reverse();
            return reversed;
        }

        public void Dispose() => Dispose(true);

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                cells = null;
                width = 0;
            }
        }

        private sealed class Node
        {
            public Node(int index) { Index = index; }
            public readonly int Index;
            public double G;
            public double V;
            public double H;
            public int Direction;
            public int Level;
            public int Movement;
            public Node Parent;
            public bool Open;
            public bool Closed;
            public long Sequence;
            public int Version;
        }

        // Tas binaire à suppression paresseuse : une entrée périmée (version changée ou nœud fermé) est ignorée au retrait.
        private sealed class NodeHeap
        {
            private struct Entry
            {
                public Node Node;
                public double Priority;
                public long Sequence;
                public int Version;
            }

            private Entry[] items = new Entry[64];
            private int size;

            public void Push(Node node, double priority)
            {
                if (size == items.Length) Array.Resize(ref items, size * 2);
                items[size] = new Entry { Node = node, Priority = priority, Sequence = node.Sequence, Version = node.Version };
                int child = size++;
                while (child > 0)
                {
                    int parent = (child - 1) / 2;
                    if (!Before(items[child], items[parent])) break;
                    Swap(child, parent);
                    child = parent;
                }
            }

            public bool TryPop(out Node node)
            {
                while (size > 0)
                {
                    Entry top = items[0];
                    items[0] = items[--size];
                    items[size] = default(Entry);
                    int parent = 0;
                    while (true)
                    {
                        int left = parent * 2 + 1, right = left + 1, best = parent;
                        if (left < size && Before(items[left], items[best])) best = left;
                        if (right < size && Before(items[right], items[best])) best = right;
                        if (best == parent) break;
                        Swap(parent, best);
                        parent = best;
                    }
                    if (top.Node.Version == top.Version && top.Node.Open)
                    {
                        node = top.Node;
                        return true;
                    }
                }
                node = null;
                return false;
            }

            // Plus petit f d'abord ; à égalité, le nœud ouvert le plus récemment (parcours for…in du client).
            private static bool Before(Entry a, Entry b) => a.Priority < b.Priority || (a.Priority == b.Priority && a.Sequence > b.Sequence);

            private void Swap(int a, int b)
            {
                Entry swap = items[a];
                items[a] = items[b];
                items[b] = swap;
            }
        }
    }
}
