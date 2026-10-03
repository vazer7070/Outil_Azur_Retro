
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace Tool_Editor.maps.data
{
	public class CellsData
	{
		public Form MapForm;
		

		public int ID;
		public Point[] Location;
		public TilesData GFX1;
		public TilesData GFX2;
		public TilesData GFX3;
		public bool UnWalk = false;
		public bool Path = false;
		public bool Los = false;
		public bool Active = true;
		public int Movement = (int)MoveEnums.WALKABLE;
		public bool Paddock = false;
		public bool TriggerCell = false;
		public bool Door = false;
		public bool IO = false;
		public int FightCell = 0;

		public bool FlipGFX1 = false;
		public bool FlipGFX2 = false;
		public bool FlipGFX3 = false;
		public int RotaGFX1 = 0;
		public int RotaGFX2 = 0;
		public int IncliSol = 1;
		public int NivSol = 7;

		public static int SizeCell = 26;
		public static double PourceTile;

		public bool Trigger = false;
		public string TriggerName = "";

     

        public void New(Form F)
        {
			if (F != null)
				MapForm = F;
        }
		
		public void JoinMap(Form F)
        {
			MapForm = F;
        }
		public void GetDatas()
        {
			BuilderClass.GetCellData(this);
        }
		public int Type(int id = -1)
		{
			int Type = Movement;

			if (id == -1)
			{
				if (UnWalk)
					Type = (int)MoveEnums.UNWALKABLE;
				if (Paddock)
					Type = (int)MoveEnums.PADDOCK;
				if (Path)
					Type = (int)MoveEnums.PATH;
				if (Door)
					Type = (int)MoveEnums.DOOR;
				if (TriggerCell)
					Type = (int)MoveEnums.TRIGGER;
			}
			else
			{
				if (id < 0 || id > 7) throw new ArgumentOutOfRangeException(nameof(id));
				Movement = id == 3 || id == 6 ? id : (int)MoveEnums.WALKABLE;
				UnWalk = id == (int)MoveEnums.UNWALKABLE;
				Paddock = id == (int)MoveEnums.PADDOCK;
				Path = id == (int)MoveEnums.PATH;
				Door = id == (int)MoveEnums.DOOR;
				TriggerCell = id == (int)MoveEnums.TRIGGER;
				Type = id;
			}
			return Type;
		}
        #region Géometrie
        public Graphics Border( Graphics G, Brush color)
        {
			using (var pen = new Pen(color))
				G.DrawPolygon(pen, new Point[] { Location[0], Location[1], Location[2], Location[3] });
			return G;
        }
		public Graphics Fill (Graphics G, Brush color)
        {
			G.FillPolygon(color, new Point[] { Location[0], Location[1], Location[2], Location[3] }, FillMode.Winding);
			return G;
        }
		public Graphics DrawString(Graphics G, string S, Brush color = null)
        {
			if (color == null)
				color = Brushes.White;
			G.DrawString(S, MapForm.Font, color, new Point(Location[3].X + SizeCell - 5, Location[0].Y + (SizeCell / 4)));
			return G;
        }
		public void DrawWalk(Graphics G)
        {
			Fill(G, new SolidBrush(Color.FromArgb(50, Color.Red)));
			Point A = new Point(Location[0].X + (SizeCell / 5), Location[3].Y - (SizeCell / 10));
			Point D = new Point(Location[0].X - (SizeCell / 5), A.Y);
			Point C = new Point(D.X, Location[3].Y + (SizeCell / 10));
			Point B = new Point(A.X, C.Y);
			G.DrawLine(Pens.DarkMagenta, A, C);
			G.DrawLine(Pens.DarkMagenta, B, D);
			Border(G, Brushes.DarkMagenta);
		}

		public void DrawPath(Graphics G)
        {
			Fill(G, new SolidBrush(Color.FromArgb(50, Color.Yellow)));
			Border(G, Brushes.Yellow);

			Rectangle Rect = new Rectangle(new Point(Location[0].X - (SizeCell / 5), Location[3].Y - (SizeCell / 10)), new Size((int)(SizeCell / 2.5), SizeCell / 5));
			G.DrawEllipse(Pens.Yellow, Rect);
		}

		public void DrawLOS (Graphics G)
        {
			SolidBrush Br = new SolidBrush(Color.FromArgb(50, Color.Blue));
			Fill(G, Br);

			Point A = new Point(Location[0].X, Location[0].Y + (SizeCell / 4));
			Point B = new Point(Location[1].X - (SizeCell / 2), Location[1].Y);
			Point C = new Point(Location[2].X, Location[2].Y - (SizeCell / 4));
			Point D = new Point(Location[3].X + (SizeCell / 2), Location[3].Y);
			G.DrawPolygon(Pens.DarkBlue, new Point[] { A, B, C, D });
			G.FillPolygon(Br, new Point[] { A, B, C, D }, FillMode.Winding);
        }

		public void DrawPaddock(Graphics G)
		{
			Fill(G, new SolidBrush(Color.FromArgb(50, Color.SaddleBrown)));
			Border(G, Brushes.SaddleBrown);

			Rectangle Rect = new Rectangle(new Point(Location[0].X - (SizeCell / 5), Location[3].Y - (SizeCell / 10)), new Size((int)(SizeCell / 2.5), SizeCell / 5));
			G.DrawRectangle(Pens.SaddleBrown, Rect);
        }
		public void DrawFightCell(Graphics G, int num, Color BGColor, Brush BorderColor)
        {
			Fill(G, new SolidBrush(Color.FromArgb(90, BGColor)));
			G.DrawString(num.ToString(), MapForm.Font, Brushes.White, new Point(Location[3].X + (SizeCell - 5), Location[0].Y + (SizeCell / 4)));
			Border(G, BorderColor);
        }
		public void DrawTrigger(Graphics G)
        {
			Fill(G, new SolidBrush(Color.FromArgb(90, Color.Yellow)));
			DrawString(G, TriggerName);
			Border(G, Brushes.Blue);
        }

		public Graphics DrawIO(Graphics G)
        {
			DrawString(G, "IO", Brushes.Yellow);
			return G;
        }
		public Graphics Draw_ID(Graphics G)
        {
			DrawString(G, ID.ToString());
			return G;
        }

		public Graphics DrawMode(Graphics G)
        {
			
			if (UnWalk)
				DrawWalk(G);
			if (Path)
				DrawPath(G);
			if (Los)
			DrawLOS(G);
			if (Paddock)
				DrawPaddock(G);
			if (FightCell == 1)
				DrawFightCell(G, 1, Color.Red, Brushes.Red);
			if (FightCell == 2)
				DrawFightCell(G, 2, Color.Blue, Brushes.Blue);
			if (Trigger)
				DrawTrigger(G);
			return G;
        }
        #endregion
        #region TilesFunction

		public Graphics Draw_GFX1(Graphics G)
        {
			if (GFX1 != null)
				Draw_Tiles(G, GFX1, FlipGFX1, RotaGFX1);
			return G;
        }
		public Graphics Draw_GFX2(Graphics G)
		{
			if (GFX2 != null)
				Draw_Tiles(G, GFX2, FlipGFX2, RotaGFX2);
			return G;
		}
		public Graphics Draw_GFX3(Graphics G)
		{
			if (GFX3 != null)
				Draw_Tiles(G, GFX3, FlipGFX3, 0);
			return G;
		}
        private Rectangle TileBounds(TilesData tile, Image picture, bool flip, int rotate)
        {
            Point anchor = TilesData.Anchor(tile, picture);
            int width = picture.Width, height = picture.Height;
            if (flip) { picture.RotateFlip(RotateFlipType.RotateNoneFlipX); anchor.X = width - anchor.X; }
            switch (rotate)
            {
                case 1:
                    picture.RotateFlip(RotateFlipType.Rotate90FlipNone);
                    anchor = new Point(height - anchor.Y, anchor.X);
                    break;
                case 2:
                    picture.RotateFlip(RotateFlipType.Rotate180FlipNone);
                    anchor = new Point(width - anchor.X, height - anchor.Y);
                    break;
                case 3:
                    picture.RotateFlip(RotateFlipType.Rotate270FlipNone);
                    anchor = new Point(anchor.Y, width - anchor.X);
                    break;
            }
            int scaledWidth = Math.Max(1, (int)Math.Round(picture.Width * PourceTile));
            int scaledHeight = Math.Max(1, (int)Math.Round(picture.Height * PourceTile));
            int centerX = Location[3].X + SizeCell;
            int centerY = Location[0].Y + SizeCell / 2;
            return new Rectangle(centerX - (int)Math.Round(anchor.X * PourceTile),
                centerY - (int)Math.Round(anchor.Y * PourceTile), scaledWidth, scaledHeight);
        }

        public Graphics Draw_Tiles(Graphics graphics, TilesData tile, bool flip, int rotate)
        {
            if (graphics == null || tile == null) return graphics;
            using (Image picture = (Image)tile.Image(true).Clone())
                graphics.DrawImage(picture, TileBounds(tile, picture, flip, rotate));
            return graphics;
        }
        #region image
		public Image RezizePic(Image pic, int NewWidth, int NewHeight)
        {
			Bitmap Bit = new Bitmap(NewWidth, NewHeight);
			Graphics G = Graphics.FromImage(Bit);
			G.DrawImage(pic, new Rectangle(0, 0, NewWidth, NewHeight), new Rectangle(0, 0, pic.Width, pic.Height), GraphicsUnit.Pixel);
			G.Dispose();
			pic = (Image)Bit.Clone();
			Bit.Dispose();
			return pic;
        }
        #endregion
        #endregion
        #region SurRound
        public Graphics SurRound_GFX1(Graphics G)
        {
			if (GFX1 != null)
				SurRound(G, GFX1, FlipGFX1, RotaGFX1);
			return G;
        }
		public Graphics SurRound_GFX2(Graphics G)
		{
			if (GFX2 != null)
				SurRound(G, GFX2, FlipGFX2, RotaGFX2);
			return G;
		}
		public Graphics SurRound_GFX3(Graphics G)
		{
			if (GFX3 != null)
				SurRound(G, GFX3, FlipGFX3, 0);
			return G;
		}
        public Graphics SurRound(Graphics graphics, TilesData tile, bool flip, int rotate)
        {
            if (graphics == null || tile == null) return graphics;
            using (Image picture = (Image)tile.Image(true).Clone())
                graphics.DrawRectangle(Pens.White, TileBounds(tile, picture, flip, rotate));
            return graphics;
        }
        #endregion
    }
}
