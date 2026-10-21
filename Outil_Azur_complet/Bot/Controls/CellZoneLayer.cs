using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace Outil_Azur_complet.Bot.Controls
{
    /// <summary>
    /// Ensemble de cellules dessiné comme une zone du client 1.34 (<c>ank.battlefield.mc.Zone</c>, lot X8) : remplissage de la
    /// couleur à l'alpha <c>Zone.ALPHA</c> (30 %) et trait d'un pixel opaque sur le contour, trou compris pour un anneau. Sert à la
    /// portée d'un sort (<c>CELL_SPELL_RANGE_COLOR</c>) et aux formes du pointeur (<c>CELL_SPELL_EFFECT_COLOR</c>).
    /// La géométrie (chemin rempli, arêtes du contour) est calculée une fois à la construction, en temps linéaire : la voisine
    /// d'une arête est cherchée par le centre symétrique dans une table ; le dessin ne fait que remplir et tracer.
    /// </summary>
    public sealed class CellZoneLayer : IDisposable
    {
        /// <summary><c>Zone.ALPHA</c> du client : opacité du remplissage, en pour cent.</summary>
        public const int FillAlphaPercent = 30;
        private readonly GraphicsPath fill = new GraphicsPath(FillMode.Winding);
        private readonly GraphicsPath outline = new GraphicsPath();
        private readonly HashSet<short> cells;

        private CellZoneLayer(HashSet<short> cells, PointF[][] polygons)
        {
            this.cells = cells;
            Source = polygons;
            var drawn = new List<KeyValuePair<PointF[], PointF>>(cells.Count);
            var centers = new HashSet<long>();
            foreach (short id in cells)
            {
                if (id < 0 || id >= polygons.Length || polygons[id] == null || polygons[id].Length < 3) continue;
                PointF center = Centroid(polygons[id]);
                drawn.Add(new KeyValuePair<PointF[], PointF>(polygons[id], center));
                centers.Add(Key(center));
                fill.AddPolygon(polygons[id]);
            }
            foreach (KeyValuePair<PointF[], PointF> entry in drawn)
            {
                PointF[] polygon = entry.Key;
                for (int i = 0; i < polygon.Length; i++)
                {
                    PointF a = polygon[i], b = polygon[(i + 1) % polygon.Length];
                    // Voisine de l'autre côté de l'arête : le symétrique du centre par rapport au milieu de l'arête.
                    if (centers.Contains(Key(new PointF(a.X + b.X - entry.Value.X, a.Y + b.Y - entry.Value.Y)))) continue;
                    outline.StartFigure();
                    outline.AddLine(a, b);
                }
            }
            DrawnCount = drawn.Count;
        }

        /// <summary>Losanges de la vue d'où la géométrie a été tirée : une autre grille demande une nouvelle couche.</summary>
        public PointF[][] Source { get; }
        /// <summary>Cellules de la couche (numéros), y compris celles hors de la grille.</summary>
        public IReadOnlyCollection<short> Cells => cells;
        /// <summary>Cellules réellement dessinées (présentes dans la grille).</summary>
        public int DrawnCount { get; }
        /// <summary>Nombre d'arêtes du contour (tests).</summary>
        public int OutlineEdgeCount => outline.PointCount / 2;

        /// <summary>Couche des cellules <paramref name="ids"/> sur les losanges <paramref name="polygons"/> ; null sans cellule ni grille.</summary>
        public static CellZoneLayer Build(IEnumerable<short> ids, PointF[][] polygons)
        {
            if (ids == null || polygons == null) return null;
            var set = new HashSet<short>(ids);
            return set.Count == 0 ? null : new CellZoneLayer(set, polygons);
        }

        /// <summary>
        /// Remplit à 30 % puis trace le contour de la couleur opaque ; <paramref name="penWidth"/> est la largeur d'un pixel
        /// d'écran dans le repère du dessin.
        /// </summary>
        public void Draw(Graphics graphics, Color rgb, float penWidth)
        {
            if (graphics == null || DrawnCount == 0) return;
            Color opaque = Color.FromArgb(255, rgb);
            using (var brush = new SolidBrush(Color.FromArgb(FillAlphaPercent * 255 / 100, opaque)))
            using (var pen = new Pen(opaque, penWidth))
            {
                graphics.FillPath(brush, fill);
                if (outline.PointCount > 0) graphics.DrawPath(pen, outline);
            }
        }

        public void Dispose()
        {
            fill.Dispose();
            outline.Dispose();
        }

        private static PointF Centroid(PointF[] polygon) => new PointF(polygon.Average(p => p.X), polygon.Average(p => p.Y));
        // Centres des cellules sur une grille de demi-unités (26,5 et 13,5) : arrondir le double suffit à comparer.
        private static long Key(PointF point) => ((long)Math.Round(point.X * 2) << 32) ^ (uint)(int)Math.Round(point.Y * 2);
    }
}
