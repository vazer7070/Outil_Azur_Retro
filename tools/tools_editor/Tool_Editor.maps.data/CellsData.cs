
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
			int Type = 0;

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
				if (Trigger)
					Type = (int)MoveEnums.TRIGGER;
			}
			else
			{
				if (id == (int)MoveEnums.UNWALKABLE)
				{
					UnWalk = true;
				}
				else
				{
					UnWalk = false;
					if (id == (int)MoveEnums.PADDOCK)
						Paddock = true;
					if (id == (int)MoveEnums.PATH)
						Path = true;
					if (id == (int)MoveEnums.DOOR)
						Door = true;
					if (id == (int)MoveEnums.TRIGGER)
						TriggerCell = true;
				}

			}
			return Type;
		}
        #region Géometrie
        public Graphics Border( Graphics G, Brush color)
        {
			G.DrawPolygon(new Pen(color), new Point[] { Location[0], Location[1], Location[2], Location[3] });
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
		public Graphics Draw_Tiles(Graphics G, TilesData Tile, bool Flip, int Rotate)
        {
			Image Pic = (Image)Tile.Image(true).Clone();
			Size ImageSize = new Size((int)(Pic.Size.Width * PourceTile), (int)(Pic.Size.Height * PourceTile));
			int Base_x = 0;
			int Base_y = 0;
			var Apos = new TilesData.Pos();
			if(Tile.type == TilesData.TileType.ground)
            {
				if(TilesData.SelectedTiles.ID == 0)
                {
					var Bpos = new TilesData.Pos();
					Bpos.ID = Tile.ID;
					Bpos.X = (Pic.Width / 2);
					Bpos.Y = (Pic.Height / 2);
					TilesData.PosGround[TilesData.Count_Ground()]= Bpos;
				}
				Apos = TilesData.Get_Grounds(TilesData.SelectedTiles.ID);
            }
            else
            {
				if(TilesData.SelectedTiles.ID == 0)
                {
					var Cpos = new TilesData.Pos();
					Cpos.ID = Apos.ID;
					Cpos.X = (Pic.Width / 2);
					Cpos.Y = (Pic.Height / 2);
					TilesData.PosObject[TilesData.Count_Object()] = Cpos;
                }
				Apos = TilesData.Get_Object(Tile.ID);
            }
			Base_x = Apos.X;
			Base_y = Apos.Y;
			int posX = (int)(Base_x * PourceTile);
			int posY = (int)(Base_y * PourceTile);

            if (Flip)
            {
				Pic.RotateFlip(RotateFlipType.RotateNoneFlipX);
				if (Tile.type == TilesData.TileType.objet)
					posX = (int)(Pic.Width - (Base_x * PourceTile));
            }

			if(Rotate != 0)
            {
                switch (Rotate)
                {
					case 1:
						Pic.RotateFlip(RotateFlipType.Rotate90FlipNone);
						ImageSize.Height = (int)Math.Ceiling(Pic.Height / 100 * 51.85 * PourceTile);
						ImageSize.Width = (int)Math.Ceiling(Pic.Width / 100 * 192.86 * PourceTile);
						RezizePic(Pic, ImageSize.Height, ImageSize.Width);
						posY = (int)((Base_x * PourceTile) / 100 * 51.85);
						posX = (int)(ImageSize.Width - (Base_y * PourceTile) / 100 * 192.85);
						break;
					case 2:
						Pic.RotateFlip(RotateFlipType.Rotate180FlipNone);
						if (Tile.type == TilesData.TileType.objet)
							posX = (int)(ImageSize.Width - (Base_x * PourceTile));
						posY = (int)(ImageSize.Height - (Base_y * PourceTile));
						break;
					case 3:
						Pic.RotateFlip(RotateFlipType.Rotate270FlipNone);
						ImageSize.Height = (int)Math.Ceiling(Pic.Height / 100 * 51.85 * PourceTile);
						ImageSize.Width = (int)Math.Ceiling(Pic.Width / 100 * 192.86 * PourceTile);
						RezizePic(Pic, ImageSize.Height, ImageSize.Width);
						posY = (int)(Base_x * PourceTile / 100 * 51.85);
						posX = (int)(Base_y * PourceTile / 100 * 192.86);
						break;
                }
            }
            
            G.DrawImage(Pic, new Rectangle(new Point(Location[3].X + SizeCell - posX, Location[2].Y - (SizeCell / 2) - posY), ImageSize));
			Pic.Dispose();
			return G;
			
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
		public Graphics SurRound(Graphics G, TilesData Tiles, bool flip, int rotate)
        {
			if(Tiles != null)
            {
				Image Pic = (Image)Tiles.Image().Clone();
				Size ImageSize = new Size((int)(Pic.Size.Width * PourceTile), (int)(Pic.Size.Height * PourceTile));

				int basex;
				int basey;
				var Pos = new TilesData.Pos();

				if(Tiles.type == TilesData.TileType.ground)
                {
					Pos = TilesData.Get_Grounds(Tiles.ID);
                }
                else
                {
					Pos = TilesData.Get_Object(Tiles.ID);
                }
				basex = Pos.X;
				basey = Pos.Y;
				int PosX = (int)(basex * PourceTile);
				int PosY = (int)(basey * PourceTile);

				if (flip)
				{
					Pic.RotateFlip(RotateFlipType.RotateNoneFlipX);
					if (Tiles.type == TilesData.TileType.objet)
						PosX = (int)(Pic.Width - (basex * PourceTile));
				}

				if (rotate != 0)
				{
					switch (rotate)
					{
						case 1:
							Pic.RotateFlip(RotateFlipType.Rotate90FlipNone);
							ImageSize.Height = (int)Math.Ceiling(Pic.Height / 100 * 51.85 * PourceTile);
							ImageSize.Width = (int)Math.Ceiling(Pic.Width / 100 * 192.86 * PourceTile);
							RezizePic(Pic, ImageSize.Height, ImageSize.Width);
							PosY = (int)((basex * PourceTile) / 100 * 51.85);
							PosX = (int)(ImageSize.Width - (basey * PourceTile) / 100 * 192.85);
							break;
						case 2:
							Pic.RotateFlip(RotateFlipType.Rotate180FlipNone);
							if (Tiles.type == TilesData.TileType.objet)
								PosX = (int)(ImageSize.Width - (basex * PourceTile));
							PosY = (int)(ImageSize.Height - (basey * PourceTile));
							break;
						case 3:
							Pic.RotateFlip(RotateFlipType.Rotate270FlipNone);
							ImageSize.Height = (int)Math.Ceiling(Pic.Height / 100 * 51.85 * PourceTile);
							ImageSize.Width = (int)Math.Ceiling(Pic.Width / 100 * 192.86 * PourceTile);
							RezizePic(Pic, ImageSize.Height, ImageSize.Width);
							PosY = (int)(basex * PourceTile / 100 * 51.85);
							PosX = (int)(basey * PourceTile / 100 * 192.86);
							break;
					}
				}
				Rectangle Rect = new Rectangle(new Point(Location[3].X + SizeCell - PosX, Location[2].Y - (SizeCell / 2) - PosY), ImageSize);
				G.DrawRectangle(Pens.White, Rect);
			}
			return G;
        }
        #endregion
    }
}