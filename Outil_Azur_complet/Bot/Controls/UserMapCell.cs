using System.Drawing;
using System.Globalization;
using System.Linq;

namespace Outil_Azur_complet.Bot.Controls
{
    /// <summary>
    /// Géométrie d'une cellule de la vue à l'écran (losange recalculé à chaque zoom), son état pour les couleurs de
    /// repli et le numéro affiché en mode diagnostic. Le décor et les acteurs sont dessinés par <see cref="UserMapControl"/>.
    /// </summary>
    public class UserMapCell
    {
        public short id;
        private Point[] MapPoints;
        public CellState State { get; set; }
        public Rectangle Rectangle { get; private set; }
        public Point Centre => new Point((Points[0].X + Points[2].X) / 2, (Points[1].Y + Points[3].Y) / 2);

        public UserMapCell(short Id = -147)
        {
            id = Id;
            State = CellState.NO_WALKABLE;
        }

        public Point[] Points
        {
            get => MapPoints;
            set
            {
                MapPoints = value;
                RefreshBounds();
            }
        }

        private void RefreshBounds()
        {
            if (MapPoints == null || MapPoints.Length == 0) { Rectangle = Rectangle.Empty; return; }
            int x = MapPoints.Min(entry => entry.X);
            int y = MapPoints.Min(entry => entry.Y);
            Rectangle = new Rectangle(x, y, MapPoints.Max(entry => entry.X) - x, MapPoints.Max(entry => entry.Y) - y);
        }

        public Graphics Border(Graphics G, Brush color)
        {
            using (var pen = new Pen(color, 2)) G.DrawPolygon(pen, Points);
            return G;
        }

        public virtual void DrawCell_ID(UserMapControl parent, Graphics G)
        {
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                G.DrawString(id.ToString(CultureInfo.InvariantCulture), parent.Font, Brushes.Black, new RectangleF(Rectangle.X, Rectangle.Y, Rectangle.Width, Rectangle.Height), format);
        }
    }
}
