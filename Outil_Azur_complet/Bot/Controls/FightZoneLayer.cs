using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using Tool_BotProtocol.Game.Combats;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Maps;

namespace Outil_Azur_complet.Bot.Controls
{
    /// <summary>
    /// Zones de combat posées au sol par <c>GDZ</c> (lot X7 : pièges du Sram, glyphes), dessinées comme <c>Game.onZoneData</c> →
    /// <c>Zone.drawCircle</c> du client 1.34 : les cellules à une distance au plus égale à la taille de la zone, remplies de
    /// <c>ZONE_COLOR[clé]</c> (entrée <c>config</c> de <c>lang.xml</c>) à l'alpha <c>Zone.ALPHA</c> (30 %), bordure d'un pixel
    /// de la même couleur sur le contour. Rien n'est lu sur le fil de l'interface hors de la table des couleurs, mise en cache.
    /// </summary>
    public static class FightZoneLayer
    {
        /// <summary><c>Zone.ALPHA</c> du client : opacité du remplissage, en pour cent.</summary>
        public const int FillAlphaPercent = 30;
        // Valeur de ZONE_COLOR de lang.xml (Resources/Bot/BotLang), lue à l'exécution ; copie de repli sans fichier chargé.
        private const string DefaultZoneColors = "16777215,0,13644564,3222918,13243184,2258204,3222918,5911580,5911580,6888565,2258204,3222918,10219757,0";
        private static int[] colors;

        /// <summary>Couleur <c>ZONE_COLOR[clé]</c> (0xRRGGBB) ; la clé 0 (blanc) hors table.</summary>
        public static int Rgb(int colorKey)
        {
            int[] table = colors ?? (colors = ReadColors());
            return colorKey >= 0 && colorKey < table.Length ? table[colorKey] : table.Length > 0 ? table[0] : 0xFFFFFF;
        }

        /// <summary>Oublie la table lue (tests : rechargement de <c>lang.xml</c>).</summary>
        public static void ResetColors() => colors = null;

        private static int[] ReadColors()
        {
            string value = null;
            try { value = LangData.Text.Config("ZONE_COLOR"); }
            catch (Exception error) when (!(error is OutOfMemoryException)) { value = null; }
            int[] parsed = Parse(value);
            return parsed.Length > 0 ? parsed : Parse(DefaultZoneColors);
        }

        private static int[] Parse(string value) => (value ?? string.Empty).Split(',')
            .Select(part => int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int color) ? color & 0xFFFFFF : -1)
            .TakeWhile(color => color >= 0).ToArray();

        /// <summary>Cellules de la zone : celles à une distance (|dx| + |dy|) au plus égale à sa taille, dans l'ordre des numéros.</summary>
        public static List<int> CellsOf(FightZone zone, Cell[] cells)
        {
            var result = new List<int>();
            if (zone == null || cells == null || zone.CellId < 0 || zone.CellId >= cells.Length) return result;
            Cell center = cells[zone.CellId];
            if (center == null) return result;
            int size = Math.Max(0, zone.Size);
            foreach (Cell cell in cells)
                if (cell != null && center.GetDistanceBetweenCells(cell) <= size) result.Add(cell.CellID);
            return result;
        }

        /// <summary>
        /// Dessine les zones dans le repère de la carte : remplissage à 30 % puis contour (arêtes des cellules de la zone sans
        /// voisine dans la zone) ; <paramref name="polygons"/> sont les losanges des cellules, <paramref name="penWidth"/> la
        /// largeur d'un pixel d'écran dans ce repère.
        /// </summary>
        public static void Draw(Graphics graphics, IEnumerable<FightZone> zones, Cell[] cells, PointF[][] polygons, float penWidth)
        {
            if (graphics == null || zones == null || cells == null || polygons == null) return;
            foreach (FightZone zone in zones)
            {
                List<int> members = CellsOf(zone, cells);
                if (members.Count == 0) continue;
                Color rgb = Color.FromArgb(Rgb(zone.Color) | unchecked((int)0xFF000000));
                var inside = new HashSet<int>(members);
                using (var path = new GraphicsPath(FillMode.Winding))
                using (var fill = new SolidBrush(Color.FromArgb(FillAlphaPercent * 255 / 100, rgb)))
                using (var pen = new Pen(rgb, penWidth))
                {
                    foreach (int id in members)
                        if (id < polygons.Length && polygons[id] != null && polygons[id].Length >= 3) path.AddPolygon(polygons[id]);
                    graphics.FillPath(fill, path);
                    foreach (int id in members)
                    {
                        PointF[] polygon = id < polygons.Length ? polygons[id] : null;
                        if (polygon == null || polygon.Length < 3) continue;
                        PointF center = Centroid(polygon);
                        for (int i = 0; i < polygon.Length; i++)
                        {
                            PointF a = polygon[i], b = polygon[(i + 1) % polygon.Length];
                            // Voisine de l'autre côté de l'arête : le point symétrique du centre par rapport à son milieu.
                            var mirrored = new PointF(a.X + b.X - center.X, a.Y + b.Y - center.Y);
                            bool shared = members.Any(other => other != id && other < polygons.Length && polygons[other] != null
                                && Distance(Centroid(polygons[other]), mirrored) < 1f);
                            if (!shared) graphics.DrawLine(pen, a, b);
                        }
                    }
                }
            }
        }

        private static PointF Centroid(PointF[] polygon) => new PointF(polygon.Average(p => p.X), polygon.Average(p => p.Y));
        private static float Distance(PointF a, PointF b) => (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    }
}
