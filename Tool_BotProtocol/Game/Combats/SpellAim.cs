using System;
using System.Collections.Generic;
using System.Linq;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Perso.Spells;

namespace Tool_BotProtocol.Game.Combats
{
    /// <summary>Forme d'une zone d'effet : lettre (<c>P</c>, <c>C</c>, <c>D</c>, <c>L</c>, <c>X</c>, <c>T</c>, <c>O</c>, <c>R</c>) et taille.</summary>
    public struct SpellZoneShape
    {
        public SpellZoneShape(char shape, int size) { Shape = shape; Size = Math.Max(0, size); }
        public char Shape { get; }
        public int Size { get; }
        public override string ToString() => Shape + Size.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Visée d'un sort en combat, comme <c>GameManager.switchToSpellLaunch</c> et <c>checkRange</c> du client 1.34 : portée
    /// (bonus des PO quand la portée est modifiable et que la portée maximale n'est pas 0, puis au moins la portée minimale),
    /// cellules de portée dessinées en <c>CELL_SPELL_RANGE_COLOR</c> (cercle, ou croix pour un sort en ligne ; toute la carte
    /// pour une portée de 63) et formes des zones d'effet que le client pose sous le pointeur
    /// (<c>PointerHandler.draw</c>). La même instance sert à valider une cible et à la dessiner.
    /// </summary>
    public sealed class SpellAim
    {
        /// <summary>Portée maximale de la donnée qui couvre toute la carte (<c>rangeMax == 63</c> du client).</summary>
        public const int WholeMapRange = 63;
        /// <summary>Taille à partir de laquelle une zone d'effet autre qu'une ligne n'est pas dessinée sous le pointeur.</summary>
        public const int UndrawnZoneSize = 63;
        // Directions impaires du client (axes X/Y de la grille) : 1 = +X, 3 = +Y, 5 = −X, 7 = −Y.
        private static readonly int[] AxisX = { 0, 1, 0, 0, 0, -1, 0, 0 };
        private static readonly int[] AxisY = { 0, 0, 0, 1, 0, 0, 0, -1 };

        private SpellAim(short spellId, int minRange, int baseMaxRange, int bonus, bool boostable, bool lineOnly, bool lineOfSight,
            IReadOnlyList<SpellZoneShape> zones)
        {
            SpellId = spellId; MinRange = Math.Max(0, minRange); BaseMaxRange = Math.Max(0, baseMaxRange); Boostable = boostable;
            RangeBonus = boostable && BaseMaxRange != 0 ? bonus : 0;
            // checkRange : une portée maximale nulle reste nulle ; sinon portée + bonus, au moins la portée minimale.
            MaxRange = BaseMaxRange == 0 ? 0 : Math.Max(MinRange, BaseMaxRange + RangeBonus);
            LineOnly = lineOnly; LineOfSight = lineOfSight; EffectZones = zones ?? new SpellZoneShape[0];
        }

        public short SpellId { get; }
        public int MinRange { get; }
        /// <summary>Portée maximale de la donnée du sort, sans bonus.</summary>
        public int BaseMaxRange { get; }
        /// <summary>Portée maximale effective (bonus compris, au moins <see cref="MinRange"/>, 0 si la donnée vaut 0).</summary>
        public int MaxRange { get; }
        /// <summary>Bonus appliqué à la portée maximale (0 si la portée n'est pas modifiable ou vaut 0).</summary>
        public int RangeBonus { get; }
        public bool Boostable { get; }
        public bool LineOnly { get; }
        public bool LineOfSight { get; }
        /// <summary>Vrai pour une portée de 63 : le client colore toute la carte.</summary>
        public bool WholeMap => BaseMaxRange == WholeMapRange;
        /// <summary>Zones d'effet dans l'ordre de la donnée (effets normaux puis critiques), comme <c>effectZones</c>.</summary>
        public IReadOnlyList<SpellZoneShape> EffectZones { get; }

        /// <summary>Visée d'un sort appris : <paramref name="rangeBonus"/> est le bonus de PO du lanceur (<see cref="Fights.RangeBonus"/>).</summary>
        public static SpellAim For(short spellId, SpellStats stats, int rangeBonus)
        {
            if (stats == null) return null;
            var zones = new List<SpellZoneShape>();
            foreach (SpellEffect effect in stats.NormalEffect.Concat(stats.CriticalEffect))
                if (effect?.ZoneEffet != null) zones.Add(new SpellZoneShape(ShapeLetter(effect.ZoneEffet.Type), effect.ZoneEffet.taille));
            return new SpellAim(spellId, stats.Min_portee, stats.Max_portee, rangeBonus, stats.portee_modifiable, stats.IsInLine,
                stats.AvecLigneDeVue, zones);
        }

        /// <summary>Visée du corps à corps (sort 0) : portée de l'arme ou des poings, jamais modifiable, zone du type d'arme.</summary>
        public static SpellAim ForCloseCombat(CloseCombatStats stats)
        {
            if (stats == null || !stats.HasData) return null;
            return new SpellAim(Fights.CloseCombatSpellId, stats.MinRange, stats.MaxRange, 0, false, stats.LineOnly, stats.LineOfSight,
                stats.EffectZones);
        }

        /// <summary>Lettre du client d'une forme du bot (<see cref="Zones.Parse"/>).</summary>
        public static char ShapeLetter(SpellActionZone type)
        {
            switch (type)
            {
                case SpellActionZone.CERCLE: return 'C';
                case SpellActionZone.LIGNE: return 'L';
                case SpellActionZone.CROIX: return 'X';
                case SpellActionZone.ANNEAU: return 'O';
                case SpellActionZone.RECTANGLE: return 'R';
                case SpellActionZone.TLIGNE: return 'T';
                case SpellActionZone.DAMIER: return 'D';
                default: return 'P';
            }
        }

        /// <summary>
        /// Zones d'une chaîne du client (<c>"Pa"</c>, <c>"Tb"</c>…, lettre puis taille en base 64) ; une paire illisible est
        /// ignorée.
        /// </summary>
        public static IReadOnlyList<SpellZoneShape> ParseZones(string zones)
        {
            var result = new List<SpellZoneShape>();
            string text = zones ?? string.Empty;
            for (int index = 0; index + 1 < text.Length; index += 2)
            {
                Zones parsed;
                try { parsed = Zones.Parse(text.Substring(index, 2)); }
                catch (ArgumentException) { continue; }
                catch (IndexOutOfRangeException) { continue; }
                result.Add(new SpellZoneShape(ShapeLetter(parsed.Type), parsed.taille));
            }
            return result;
        }

        /// <summary><c>checkRange</c> du client : alignement pour un sort en ligne, puis portée minimale et maximale.</summary>
        public bool InRange(Cell origin, Cell target)
        {
            if (origin == null || target == null) return false;
            if (LineOnly && !origin.AreCellsOnline(target)) return false;
            int distance = origin.GetDistanceBetweenCells(target);
            return distance >= MinRange && distance <= MaxRange;
        }

        /// <summary>
        /// Cellules de portée dessinées par <c>drawZone(lanceur, min, max, "spell", CELL_SPELL_RANGE_COLOR, "C" ou "X")</c> :
        /// distance entre min et max (alignées pour un sort en ligne) ; toute la carte pour une portée de 63.
        /// </summary>
        public List<short> RangeCells(Cell origin, Cell[] cells)
        {
            var result = new List<short>();
            if (origin == null || cells == null) return result;
            foreach (Cell cell in cells)
            {
                if (cell == null) continue;
                if (WholeMap) { result.Add(cell.CellID); continue; }
                if (LineOnly && !origin.AreCellsOnline(cell)) continue;
                int distance = origin.GetDistanceBetweenCells(cell);
                if (distance >= MinRange && distance <= MaxRange) result.Add(cell.CellID);
            }
            return result;
        }

        /// <summary>
        /// Formes posées sous le pointeur quand la cible <paramref name="target"/> est valable, une liste de cellules par zone
        /// d'effet (le client les dessine l'une après l'autre, leurs remplissages s'additionnent) ; une zone d'au moins 63 cellules
        /// qui n'est pas une ligne est omise, comme dans le client.
        /// </summary>
        public List<List<short>> PointerCells(Cell caster, Cell target, Cell[] cells, int mapWidth)
        {
            var result = new List<List<short>>();
            foreach (SpellZoneShape zone in EffectZones)
            {
                if (zone.Size >= UndrawnZoneSize && zone.Shape != 'L') continue;
                result.Add(ShapeCells(zone, caster, target, cells, mapWidth));
            }
            return result;
        }

        /// <summary>
        /// Cellules d'une forme posée sur <paramref name="target"/> (<c>PointerHandler.draw</c>) :
        /// <c>P</c> la cellule ; <c>C</c> n distance ≤ n ; <c>D</c> n les anneaux de même parité que n entre 1 et n ;
        /// <c>X</c> n la croix ; <c>L</c> n la ligne qui prolonge lanceur → cible sur n cellules ; <c>T</c> n la ligne
        /// perpendiculaire, n de chaque côté ; <c>O</c> n l'anneau de rayon n. Le client ne prolonge une ligne que dans une
        /// direction impaire (un axe de la grille) ; <c>R</c>, que les sorts n'emploient pas, se réduit à la cellule.
        /// </summary>
        public static List<short> ShapeCells(SpellZoneShape zone, Cell caster, Cell target, Cell[] cells, int mapWidth)
        {
            var result = new List<short>();
            if (target == null || cells == null) return result;
            int size = zone.Size;
            switch (zone.Shape)
            {
                case 'C':
                    return Where(cells, cell => target.GetDistanceBetweenCells(cell) <= size);
                case 'D':
                    // for (r = n pair ? 1 : 0 ; r < n ; r += 2) drawRing(r + 1, r) : anneaux de rayon r + 1.
                    return Where(cells, cell =>
                    {
                        int distance = target.GetDistanceBetweenCells(cell);
                        return distance >= 1 && distance <= size && (distance - size) % 2 == 0;
                    });
                case 'O':
                    return Where(cells, cell => target.GetDistanceBetweenCells(cell) == size);
                case 'X':
                    return Where(cells, cell => target.AreCellsOnline(cell) && target.GetDistanceBetweenCells(cell) <= size);
                case 'L':
                case 'T':
                {
                    var lengths = new int[8];
                    if (caster != null && caster.CellID != target.CellID)
                    {
                        int direction = Direction(caster, target, mapWidth);
                        if (zone.Shape == 'T') { lengths[(direction + 6) % 8] = size; lengths[(direction + 10) % 8] = size; }
                        else lengths[direction] = size;
                    }
                    var lookup = ByCoordinates(cells);
                    result.Add(target.CellID);
                    for (int direction = 1; direction < 8; direction += 2)
                        for (int step = 1; step <= lengths[direction]; step++)
                            if (lookup.TryGetValue(Key(target.X + AxisX[direction] * step, target.Y + AxisY[direction] * step), out Cell cell))
                                result.Add(cell.CellID);
                    result.Sort();
                    return result;
                }
                default:
                    result.Add(target.CellID);
                    return result;
            }
        }

        /// <summary>
        /// <c>Pathfinding.getDirection</c> du client : direction de la cellule voisine exacte (décalages 1, w, 2w−1, w−1, −1,
        /// −w, −2w+1, −w+1), sinon 3 ou 7 si les X sont égaux (selon Y), 1 si la cible est vers +X, 5 sinon.
        /// </summary>
        public static int Direction(Cell from, Cell to, int mapWidth)
        {
            int[] offsets = { 1, mapWidth, mapWidth * 2 - 1, mapWidth - 1, -1, -mapWidth, -mapWidth * 2 + 1, -(mapWidth - 1) };
            int delta = to.CellID - from.CellID;
            for (int direction = 7; direction >= 0; direction--)
                if (offsets[direction] == delta) return direction;
            int dx = to.X - from.X, dy = to.Y - from.Y;
            if (dx == 0) return dy > 0 ? 3 : 7;
            return dx > 0 ? 1 : 5;
        }

        private static List<short> Where(Cell[] cells, Func<Cell, bool> keep)
        {
            var result = new List<short>();
            foreach (Cell cell in cells) if (cell != null && keep(cell)) result.Add(cell.CellID);
            return result;
        }

        private static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;

        private static Dictionary<long, Cell> ByCoordinates(Cell[] cells)
        {
            var lookup = new Dictionary<long, Cell>(cells.Length);
            foreach (Cell cell in cells) if (cell != null) lookup[Key(cell.X, cell.Y)] = cell;
            return lookup;
        }
    }
}
