using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Outil_Azur_complet.Bot.Controls
{
    public class UserMapCell
    {
        public short id;
        private Point[] MapPoints;
        public CellState State { get; set; }
        public Brush CustomBrush { get; set; }
        public Pen BorderPen { get; set; }
        public Pen MouseOverpen { get; set; }
        public Rectangle Rectangle { get; set; }
        public Point Centre => new Point((Points[0].X + Points[2].X) / 2, (Points[1].Y + Points[3].Y) / 2);

        public UserMapCell(short Id = -147)
        {
            if (id != -147)
            {
                id = Id;
                State = CellState.NO_WALKABLE;
            }
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
        public void RefreshBounds()
        {
            int x = Points.Min(entry => entry.X);
            int y = Points.Min(entry => entry.Y);

            int width = Points.Max(entry => entry.X) - x;
            int height = Points.Max(entry => entry.Y) - y;

            Rectangle = new Rectangle(x, y, width, height);
        }
        public Graphics Border(Graphics G, Brush color)
        {
            G.DrawPolygon(new Pen(color), new Point[] { Points[0], Points[1], Points[2], Points[3] });
            return G;
        }
        
        public void DrawColor(Graphics G, Color BorderColor, Color? FillColor)
        {
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddLines(Points);

                if (FillColor != null)
                {
                    using (SolidBrush brush = new SolidBrush(FillColor.Value))
                    {
                        G.FillPath(brush, path);
                    }
                }

                using (Pen pen = new Pen(BorderColor))
                {
                    G.DrawPath(pen, path);
                }
            }
        }
        public virtual void DrawTree(Bitmap image, Graphics G)
        {
            G.DrawImage(image, new Rectangle(Points[1].X - 30, Points[0].Y - 40, 50, 50));
        }
        public virtual void DrawMisc(Bitmap image, Graphics G)
        {
            G.DrawImage(image, new Rectangle(Points[1].X - 15, Points[0].Y - 22, 35, 35));
        }
        public virtual void DrawStatue(Bitmap image, Graphics G)
        {
            G.DrawImage(image, new Rectangle(Points[1].X - 5, Points[0].Y - 45, 45, 50));
        }
        public virtual void DrawDoor(Bitmap image, Graphics G)
        {
            G.DrawImage(image, new Rectangle(Points[1].X - 15, Points[0].Y - 25, 20, 35));
        }
        public virtual void DrawZaapi(Bitmap image, Graphics G)
        {
            G.DrawImage(image, new Rectangle(Points[1].X - 25, Points[0].Y - 35, 50, 50));
        }
        public virtual void DrawZaap(Bitmap image, Graphics G, bool r = false)
        {
            if (r)
                G.DrawImage(image, new Rectangle(Points[1].X - 20, Points[0].Y - 30, 40, 40));
            else
                G.DrawImage(image, new Rectangle(Points[1].X - 25, Points[0].Y - 35, 40, 40));
        }
        public virtual void DrawTrigger(Bitmap image, Graphics G)
        {
                G.DrawImage(image, new Rectangle(Points[1].X - 10, Points[0].Y - 10, 20, 20));
        }
        public virtual void DrawNPC(Bitmap image, Graphics G)
        {
            G.DrawImage(image, new Rectangle(Points[1].X - 10, Points[0].Y - 25, 20, 35));
        }
        public virtual void DrawCell_ID(UserMapControl parent, Graphics G)
        {
            StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            G.DrawString(id.ToString(), parent.Font, Brushes.Black, new RectangleF(Rectangle.X, Rectangle.Y, Rectangle.Width, Rectangle.Height), format);
        }
        public void Draw_FillingPie(Graphics G, Color color, float size)
        {
            using(SolidBrush brush = new SolidBrush(color))
            {
                G.FillPie(brush, Points[1].X - size / 2, Points[1].Y + 4.2f, size, size, 0, 360);
            }
        }
        
        public void DrawObstacle(Graphics G, Color border, Color FillingColor)
        {
            using (GraphicsPath GP = new GraphicsPath())
            {
                GP.AddLines(new PointF[]
                {
                    new PointF(Points[0].X, Points[0].Y - 10),
                    new PointF(Points[1].X, Points[1].Y - 10),
                    new PointF(Points[2].X, Points[2].Y - 10),
                    new PointF(Points[3].X, Points[3].Y - 10),
                    new PointF(Points[0].X, Points[0].Y - 10)
                });

                GP.AddLines(new PointF[]
               {
                    new PointF(Points[0].X, Points[0].Y - 10),
                    new PointF(Points[3].X, Points[3].Y - 10),
                    Points[3],
                    Points[0],
                    new PointF(Points[0].X, Points[0].Y - 10)

               }) ;
                GP.AddLines(new PointF[]
               {
                    new PointF(Points[3].X, Points[3].Y - 10),
                    new PointF(Points[2].X, Points[2].Y - 10),
                    Points[2],
                    Points[3],
                    new PointF(Points[3].X, Points[3].Y - 10)
               });
                using(SolidBrush B = new SolidBrush(FillingColor))
                    G.FillPath(B, GP);
                using(Pen P = new Pen(border))
                    G.DrawPath(P, GP);
            }
        }
        public bool IsRectangle(RectangleF R) => Rectangle.IntersectsWith(Rectangle.Ceiling(R));
    }
}
